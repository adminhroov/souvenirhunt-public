using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SouvenirHunt
{
    /// <summary>HTTP 응답 한 건. status 0 = 통신 실패(연결 안 됨·제한 시간 초과)</summary>
    public struct PrizeHttpResult
    {
        public long status;
        public string body;
        public string error;
    }

    /// <summary>전송 계층 — 실기기는 UnityWebRequest, 셀프테스트는 가짜 서버로 교체</summary>
    public interface IPrizeTransport
    {
        IEnumerator Send(string method, string url, string token, string jsonBody, int timeoutSec, Action<PrizeHttpResult> done);
        void Abort();
    }

    public class UnityWebRequestPrizeTransport : IPrizeTransport
    {
        UnityWebRequest current;

        public IEnumerator Send(string method, string url, string token, string jsonBody, int timeoutSec, Action<PrizeHttpResult> done)
        {
            var req = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = timeoutSec };
            if (jsonBody != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("Authorization", "Bearer " + token);
            current = req;
            yield return req.SendWebRequest();
            if (current != req) yield break; // Abort로 이미 정리됨
            current = null;
            bool noResponse = req.result == UnityWebRequest.Result.ConnectionError || req.responseCode == 0;
            var r = new PrizeHttpResult { status = noResponse ? 0 : req.responseCode, body = req.downloadHandler?.text, error = req.error };
            req.Dispose();
            done(r);
        }

        public void Abort()
        {
            var req = current;
            current = null;
            if (req == null) return;
            try { req.Abort(); req.Dispose(); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>
    /// 오스리움 이벤트 경품 연동(API 정의서 v1.0, 10-06).
    ///   게임 시작 시 EVT-001 GET /api/partner/event/current-user → userDid 보관
    ///   [보상 확인] 시 EVT-002 POST /api/partner/event/reward {userDid, nftId} → purchaseNo (게임당 1회, 200번대 = 접수 완료 — 실서버는 201)
    /// 실제 NFT 전송 결과(약 20초)는 진행자 화면 팝업에만 뜬다 — 여기서는 접수까지만 안다.
    /// 통신 실패·5xx만 재시도한다. 409는 재요청하지 않는다(정의서). 단, 우리 재시도 뒤의 "이미 요청됨"은 앞 요청이 접수된 것이라 성공으로 본다.
    /// (10-12) 오스리움 진행자 화면은 참가자를 [로그아웃] → [이벤트 로그인]으로 바꾼다. 409를 받은 DID로는 다시 보내지 않고,
    /// 진행자가 참가자를 바꾼 뒤 [경품 다시 요청]을 누르면 재조회해서 다른 참가자일 때만 보낸다.
    /// 문의용 기록(시각 KST·API·HTTP 코드·resultMessage·요청 값·purchaseNo)을 기기 폴더 prize_log.txt에 남긴다.
    /// 어떤 실패도 게임 흐름을 막지 않는다 — 화면에 사유만 보이고 진행요원이 실물로 처리.
    /// </summary>
    public class PrizeLink : MonoBehaviour
    {
        public enum DidState { None, Fetching, Ready, NoParticipant, Failed }
        public enum ClaimState { None, Sending, Accepted, Rejected, Failed, Skipped }

        public const string LogFileName = "prize_log.txt";
        const string PathCurrentUser = "/api/partner/event/current-user";
        const string PathReward = "/api/partner/event/reward";

        public IPrizeTransport Transport { get; set; } = new UnityWebRequestPrizeTransport();
        /// <summary>재시도 전 대기 초(1회차·2회차) — 셀프테스트는 0으로</summary>
        public float[] RetryDelays { get; set; } = { 1f, 2f };
        /// <summary>prize_log.txt 기록 여부 — 셀프테스트는 끔</summary>
        public bool WriteLogFile { get; set; } = true;

        public event Action OnChanged;

        public bool Enabled { get; private set; }
        public bool Active => Enabled && !string.IsNullOrWhiteSpace(token);
        public DidState Did { get; private set; }
        public string UserDid { get; private set; } = "";
        public ClaimState Claim { get; private set; }
        public long PurchaseNo { get; private set; } = -1;
        public int ClaimNftId { get; private set; }
        /// <summary>현장 화면용 한국어 사유(실패·건너뜀일 때)</summary>
        public string Reason { get; private set; } = "";
        /// <summary>서버 resultMessage 원문(마지막 응답) — 오스리움 문의용</summary>
        public string LastServerMessage { get; private set; } = "";
        public long LastStatus { get; private set; } = -1;
        /// <summary>진행자 대조용 DID 끝 6자</summary>
        public string DidTail => string.IsNullOrEmpty(UserDid) ? "" : UserDid.Length <= 6 ? UserDid : "…" + UserDid.Substring(UserDid.Length - 6);
        public static string LogPath => Path.Combine(Application.persistentDataPath, LogFileName);

        string server = "", token = "";
        int timeoutSec = 8;
        string sentDid = "";     // 이번 게임에서 EVT-002를 보낸 DID(응답 여부 무관) — 같은 DID 재시도의 "이미 요청됨"만 접수로 본다
        string rejectedDid = ""; // 이번 게임에서 409로 거절된 DID — 이 DID로는 다시 보내지 않는다(정의서)
        bool awaitingSwap;       // 마지막 결과가 409 거절 — 진행자가 참가자를 바꾸면 다시 요청할 수 있다

        public void Configure(GameConfig cfg)
        {
            Enabled = cfg.prizeEnabled;
            server = (cfg.prizeServer ?? "").TrimEnd('/');
            token = (cfg.prizeToken ?? "").Trim();
            timeoutSec = cfg.prizeTimeoutSeconds;
        }

        /// <summary>새 참가자(운영자 리셋·씬 진입) — 진행 중 요청은 끊고 상태를 비운다</summary>
        public void ResetParticipant()
        {
            StopAllCoroutines();
            Transport?.Abort();
            Did = DidState.None; UserDid = "";
            Claim = ClaimState.None; PurchaseNo = -1; ClaimNftId = 0;
            sentDid = ""; rejectedDid = ""; awaitingSwap = false;
            Reason = ""; LastServerMessage = ""; LastStatus = -1;
            Raise();
        }

        /// <summary>게임 시작 시 EVT-001. 꺼져 있으면 아무 것도 안 함</summary>
        public void BeginParticipant()
        {
            if (!Active || Did == DidState.Fetching || Did == DidState.Ready) return;
            StartCoroutine(FetchDid());
        }

        /// <summary>[보상 확인] 시 EVT-002 — 게임당 1회. 꺼져 있으면 아무 것도 안 함</summary>
        public void RequestPrize(int nftId)
        {
            if (!Active || Claim != ClaimState.None) return;
            ClaimNftId = nftId;
            awaitingSwap = false;
            if (nftId <= 0)
            {
                SetClaim(ClaimState.Skipped, "키링 경품 번호(nftId)가 설정되지 않음");
                Debug.LogWarning("[Prize] 경품 번호 0 — 요청 안 함 (config prizeNftIdKeyring)"); // 10-28 경품 = 키링 큐브만
                return;
            }
            Claim = ClaimState.Sending; Reason = "";
            Raise();
            StartCoroutine(ClaimRoutine(nftId));
        }

        /// <summary>
        /// 보상 화면에서 다시 요청할 수 있는가 — 지급 요청이 서버에 접수되지 않았거나(진행자 로그인 없음) 접수 여부를 모를 때(통신 실패·5xx),
        /// 그리고 409로 거절됐을 때(진행자가 참가자를 바꾸면 재조회한 새 참가자로 보냄 — 같은 참가자면 보내지 않음).
        /// 재고·nftId 오류처럼 다시 보내도 같은 거절은 불가. 중복 지급은 서버가 409로 막는다.
        /// </summary>
        public bool CanRetry => Active && (Claim == ClaimState.Failed ||
                                           (Claim == ClaimState.Rejected && (Did == DidState.NoParticipant || awaitingSwap)));

        /// <summary>보상 화면 [잡기] — 진행자가 로그인·와이파이를 고친 뒤 같은 nftId로 다시 요청</summary>
        public void RetryPrize()
        {
            if (!CanRetry) return;
            Debug.Log($"[Prize] 경품 다시 요청 (직전 {Claim}: {Reason})");
            Claim = ClaimState.None;
            RequestPrize(ClaimNftId);
        }

        void OnDestroy() => Transport?.Abort();

        // ─ EVT-001 ─
        IEnumerator FetchDid()
        {
            Did = DidState.Fetching;
            Raise();
            PrizeHttpResult res = default;
            yield return SendWithRetry("EVT-001", "GET", PathCurrentUser, null, r => res = r);
            var body = Parse(res.body);
            LastStatus = res.status;
            LastServerMessage = body?.resultMessage ?? res.error ?? "";
            WriteLog("EVT-001", res, body, null);

            if (Ok(res.status) && !string.IsNullOrEmpty(body?.data?.userDid))
            {
                UserDid = body.data.userDid;
                Did = DidState.Ready;
                Debug.Log($"[Prize] EVT-001 참가자 DID {DidTail}");
            }
            else if (res.status == 404)
            {
                Did = DidState.NoParticipant;
                UserDid = ""; // 재조회 때 앞 참가자 DID가 화면에 남지 않게
                Debug.LogWarning("[Prize] EVT-001 404 — 진행자 화면에 시작한 참가자 없음(경품 받기 때 다시 조회)");
            }
            else
            {
                Did = DidState.Failed;
                UserDid = "";
                Debug.LogWarning($"[Prize] EVT-001 실패 HTTP {res.status} {LastServerMessage}");
            }
            Raise();
        }

        // ─ EVT-002 ─
        IEnumerator ClaimRoutine(int nftId)
        {
            // 시작 때 조회가 아직 진행 중이면 기다리고, 실패·참가자 없음이었으면 한 번 더 조회(진행자가 늦게 로그인한 경우).
            // 409로 거절된 적이 있으면 진행자가 참가자를 바꿨을 수 있으므로 항상 다시 조회한다.
            while (Did == DidState.Fetching) yield return null;
            if (Did != DidState.Ready || rejectedDid.Length > 0) yield return FetchDid();
            if (Did != DidState.Ready)
            {
                SetClaim(Did == DidState.NoParticipant ? ClaimState.Rejected : ClaimState.Failed,
                         Did == DidState.NoParticipant ? "진행자 화면에서 참가자가 로그인되지 않음" : DescribeFailure(LastStatus, LastServerMessage));
                yield break;
            }
            if (UserDid == rejectedDid)
            {
                // 409 재요청 금지(정의서) — 같은 참가자로는 보내지 않고 진행자가 참가자를 바꾸기를 기다린다
                awaitingSwap = true;
                SetClaim(ClaimState.Rejected, "진행자 화면 참가자가 그대로 — 로그아웃 후 다음 참가자 로그인");
                Debug.LogWarning($"[Prize] 다시 요청 — 참가자 그대로({DidTail}), 요청 안 함");
                yield break;
            }

            string did = UserDid;
            string json = "{\"userDid\":\"" + JsonEscape(did) + "\",\"nftId\":" + nftId + "}";
            PrizeHttpResult res = default;
            int attemptsUsed = 0;
            bool sentBefore = sentDid == did; // 같은 참가자로 앞서(이전 [다시 요청] 포함) 보낸 적이 있으면 '이미 요청됨' = 그 요청이 접수된 것
            sentDid = did;
            yield return SendWithRetry("EVT-002", "POST", PathReward, json, r => res = r, n => attemptsUsed = n);
            var body = Parse(res.body);
            LastStatus = res.status;
            LastServerMessage = body?.resultMessage ?? res.error ?? "";
            string msg = LastServerMessage;

            if (Ok(res.status))
            {
                PurchaseNo = body?.data != null ? body.data.purchaseNo : -1;
                SetClaim(ClaimState.Accepted, "");
            }
            else if (res.status == 409 && (attemptsUsed > 1 || sentBefore) && Has(msg, "already requested"))
            {
                // 앞 시도가 서버에 닿았는데 응답만 못 받은 경우 — 접수된 것으로 본다(접수번호는 모름)
                PurchaseNo = -1;
                SetClaim(ClaimState.Accepted, "");
            }
            else if (res.status == 409)
            {
                // 진행자가 참가자 교체를 빼먹었거나(이미 요청됨) 게임 도중 바꿨다(현재 참가자 아님) — 이 DID로는 다시 보내지 않고,
                // 진행자가 참가자를 바꾼 뒤 [경품 다시 요청]하면 재조회한 새 참가자로 보낸다
                rejectedDid = did;
                awaitingSwap = true;
                SetClaim(ClaimState.Rejected, DescribeFailure(res.status, msg));
            }
            else if (res.status == 404 && Has(msg, "EventParticipant(started)"))
            {
                // 진행자가 로그아웃만 하고 다음 참가자를 아직 로그인하지 않음 — 로그인 뒤 [경품 다시 요청]하면 재조회해서 보낸다
                Did = DidState.NoParticipant;
                UserDid = "";
                SetClaim(ClaimState.Rejected, DescribeFailure(res.status, msg));
            }
            else if (res.status == 404 || res.status == 400 || res.status == 401)
                SetClaim(ClaimState.Rejected, DescribeFailure(res.status, msg));
            else
                SetClaim(ClaimState.Failed, DescribeFailure(res.status, msg));

            WriteLog("EVT-002", res, body, $"userDid={did} nftId={nftId}" + (attemptsUsed > 1 ? $" 시도={attemptsUsed}" : ""));
            Debug.Log($"[Prize] EVT-002 {Claim} HTTP {res.status} purchaseNo={PurchaseNo} {msg} {Reason}");
        }

        /// <summary>통신 실패(0)·5xx만 RetryDelays 수만큼 재시도. 4xx는 바로 반환</summary>
        IEnumerator SendWithRetry(string api, string method, string path, string json, Action<PrizeHttpResult> done, Action<int> attempts = null)
        {
            PrizeHttpResult last = default;
            int maxAttempts = 1 + (RetryDelays?.Length ?? 0);
            int n = 0;
            for (int a = 0; a < maxAttempts; a++)
            {
                if (a > 0)
                {
                    float d = RetryDelays[a - 1];
                    Debug.LogWarning($"[Prize] {api} 재시도 {a}/{maxAttempts - 1} ({d:0.#}s 뒤) — 직전 HTTP {last.status} {last.error}");
                    if (d > 0f) yield return new WaitForSecondsRealtime(d);
                }
                bool got = false;
                n = a + 1;
                yield return Transport.Send(method, server + path, token, json, timeoutSec, r => { last = r; got = true; });
                if (!got) last = new PrizeHttpResult { status = 0, error = "응답 없음" };
                if (last.status != 0 && last.status < 500) break;
            }
            attempts?.Invoke(n);
            done(last);
        }

        void SetClaim(ClaimState s, string reason)
        {
            Claim = s;
            Reason = reason ?? "";
            Raise();
        }

        void Raise() => EventUtil.SafeInvoke(OnChanged);

        /// <summary>정의서 오류코드 시트 → 현장 화면 문구</summary>
        public static string DescribeFailure(long status, string msg)
        {
            msg ??= "";
            switch (status)
            {
                case 0: return "서버에 연결할 수 없음(와이파이 확인)";
                case 400: return "요청 형식 오류";
                case 401: return "인증 토큰 오류(config prizeToken 확인)";
                case 404:
                    if (Has(msg, "EventParticipant(started)")) return "진행자 화면에서 참가자가 로그인되지 않음";
                    if (Has(msg, "EventParticipant(userDid)")) return "참가자를 찾을 수 없음";
                    if (Has(msg, "재고")) return "경품 재고 소진";
                    if (Has(msg, "Data Not found")) return "경품 번호(nftId)가 마켓에 없음";
                    if (Has(msg, "User(userNo)")) return "참가자 계정 오류(오스리움 문의)";
                    return "찾을 수 없음";
                case 409:
                    if (Has(msg, "already requested")) return "이 참가자는 이미 경품이 요청됨 — 로그아웃 후 다음 참가자 로그인";
                    if (Has(msg, "not the current participant")) return "현재 진행 중인 참가자가 아님 — 진행자 화면 확인";
                    if (Has(msg, "not started")) return "참가자가 시작 상태가 아님 — 진행자 화면 확인";
                    return "지급 불가";
                default:
                    return status >= 500 ? "경품 서버 오류(오스리움 문의)" : $"알 수 없는 응답 {status}";
            }
        }

        /// <summary>성공 = 200번대. 정의서 예시는 200이지만 실서버 EVT-002 접수는 201(Created)로 온다(10-15 실서버 테스트 —
        /// 200만 성공으로 봐 "알 수 없는 응답 201" 실패를 띄웠는데 실제로는 지급 완료)</summary>
        static bool Ok(long status) => status >= 200 && status < 300;

        static bool Has(string s, string part) => s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        [Serializable] class Resp { public int resultCode; public string resultMessage; public RespData data; }
        [Serializable] class RespData { public string userDid; public long purchaseNo = -1; }

        static Resp Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<Resp>(json); }
            catch { return null; }
        }

        /// <summary>오스리움 문의 시 전달 항목 그대로 — 기기 폴더 prize_log.txt(adb pull)와 Debug 로그</summary>
        void WriteLog(string api, PrizeHttpResult res, Resp body, string request)
        {
            string kst = DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd HH:mm:ss");
            string line = $"{kst} KST | {api} | HTTP {res.status} | {body?.resultMessage ?? res.error ?? ""}" +
                          (request != null ? $" | {request}" : "") +
                          (api == "EVT-001" && !string.IsNullOrEmpty(body?.data?.userDid) ? $" | userDid={body.data.userDid}" : "") +
                          (api == "EVT-002" && Ok(res.status) && body?.data != null ? $" | purchaseNo={body.data.purchaseNo}" : "");
            Debug.Log("[Prize] " + line);
            if (!WriteLogFile) return;
            try { File.AppendAllText(LogPath, line + "\n", new UTF8Encoding(false)); }
            catch (Exception e) { Debug.LogWarning("[Prize] 로그 파일 기록 실패: " + e.Message); }
        }
    }
}
