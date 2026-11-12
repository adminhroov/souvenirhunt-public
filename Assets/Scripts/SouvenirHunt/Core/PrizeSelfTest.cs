using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 경품 연동(PrizeLink) 시나리오 검증 — 가짜 서버로만 돈다(실서버 호출 없음). ClawSelfTest가 재생 중에 부른다.
    /// 정의서 v1.0의 응답 예시·오류코드 시트를 그대로 흉내 낸다.
    /// </summary>
    public static class PrizeSelfTest
    {
#if UNITY_EDITOR
        public const string Did = "did:etritmv:8PCpgQeoUjcfjw66ZDT6Lb";
        public const string BodyDid = "{\"resultCode\":200,\"resultMessage\":\"SUCCESS\",\"data\":{\"userDid\":\"" + Did + "\"}}";
        public const string BodyPurchase8 = "{\"resultCode\":200,\"resultMessage\":\"SUCCESS\",\"data\":{\"purchaseNo\":8}}";
        public const string BodyNoParticipant = "{\"resultCode\":404,\"resultMessage\":\"EventParticipant(started) not found.\"}";
        const string BodyAlready = "{\"resultCode\":409,\"resultMessage\":\"Reward already requested for this start.\"}";
        const string BodyNotCurrent = "{\"resultCode\":409,\"resultMessage\":\"EventParticipant(userDid) is not the current participant.\"}";
        public const string Did2 = "did:etritmv:3QxZr7NcVb2wLk9aHs4Yt";
        public const string BodyDid2 ="{\"resultCode\":200,\"resultMessage\":\"SUCCESS\",\"data\":{\"userDid\":\"" + Did2 + "\"}}";
        const string BodyPurchase9 = "{\"resultCode\":200,\"resultMessage\":\"SUCCESS\",\"data\":{\"purchaseNo\":9}}";
        const string BodyStock = "{\"resultCode\":404,\"resultMessage\":\"Data Not found. : 마켓 판매 정보의 재고 수량 확인\"}";
        const string BodyBadToken = "{\"resultCode\":401,\"resultMessage\":\"Unauthorized\"}";

        /// <summary>경로별 응답 대기열. 비면 통신 실패(0). 요청은 전부 기록</summary>
        public class FakeTransport : IPrizeTransport
        {
            public readonly Queue<PrizeHttpResult> currentUser = new();
            public readonly Queue<PrizeHttpResult> reward = new();
            public readonly List<string> requests = new(); // "GET url" / "POST url body"
            public readonly List<string> auth = new();
            public int aborts;
            public bool HoldRewards;

            public FakeTransport User(long status, string body) { currentUser.Enqueue(new PrizeHttpResult { status = status, body = body }); return this; }
            public FakeTransport Reward(long status, string body) { reward.Enqueue(new PrizeHttpResult { status = status, body = body }); return this; }
            public int Count(string method) => requests.FindAll(r => r.StartsWith(method + " ")).Count;

            public IEnumerator Send(string method, string url, string token, string jsonBody, int timeoutSec, Action<PrizeHttpResult> done)
            {
                requests.Add($"{method} {url}" + (jsonBody != null ? " " + jsonBody : ""));
                auth.Add(token);
                yield return null; // 한 프레임 뒤 응답(실제처럼 비동기)
                while (HoldRewards && method == "POST") yield return null;
                var q = url.EndsWith("/current-user") ? currentUser : reward;
                done(q.Count > 0 ? q.Dequeue() : new PrizeHttpResult { status = 0, error = "fake: no response" });
            }

            public void Abort() => aborts++;
        }

        public static GameConfig TestConfig(bool enabled = true, string token = "test-token")
            => new GameConfig { prizeEnabled = enabled, prizeToken = token, prizeServer = "https://fake.local", prizeTimeoutSeconds = 3 };

        /// <summary>PrizeLink를 테스트용으로 구성 — 가짜 서버·재시도 대기 0</summary>
        public static FakeTransport Arm(PrizeLink p, GameConfig cfg = null)
        {
            p.ResetParticipant();
            p.Configure(cfg ?? TestConfig());
            var f = new FakeTransport();
            p.Transport = f;
            p.RetryDelays = new[] { 0f, 0f };
            p.WriteLogFile = false; // 가짜 응답이 기기 문의용 로그에 섞이지 않게
            return f;
        }

        static IEnumerator Until(Func<bool> cond, int maxFrames = 120)
        {
            for (int i = 0; i < maxFrames && !cond(); i++) yield return null;
        }

        public static IEnumerator Run(Action<bool, string> check)
        {
            var go = new GameObject("PrizeSelfTest");
            var p = go.AddComponent<PrizeLink>();

            // 1) 정상: 시작 EVT-001 → 경품 받기 EVT-002 → 접수번호 8, 두 번째 요청은 무시
            var f = Arm(p).User(200, BodyDid).Reward(200, BodyPurchase8);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            check(p.Did == PrizeLink.DidState.Ready && p.UserDid == Did && f.requests[0] == "GET https://fake.local/api/partner/event/current-user" && f.auth[0] == "test-token",
                  $"[경품] EVT-001 GET → DID 보관 ({p.Did}, {p.DidTail})");
            p.RequestPrize(4);
            check(p.Claim == PrizeLink.ClaimState.Sending, "[경품] 경품 받기 → 요청 중");
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            string post = f.requests.Count > 1 ? f.requests[1] : "";
            check(p.Claim == PrizeLink.ClaimState.Accepted && p.PurchaseNo == 8 &&
                  post == "POST https://fake.local/api/partner/event/reward {\"userDid\":\"" + Did + "\",\"nftId\":4}",
                  $"[경품] EVT-002 POST {{userDid,nftId}} → 접수번호 {p.PurchaseNo} ({post})");
            p.RequestPrize(4);
            yield return null; yield return null;
            check(f.Count("POST") == 1 && p.Claim == PrizeLink.ClaimState.Accepted, $"[경품] 게임당 1회 — 두 번째 요청 안 보냄 (POST {f.Count("POST")})");

            // 1b) 실서버(10-15)는 EVT-002 접수를 201(Created)로 응답 — 200번대는 전부 접수(실패·다시 요청 버튼이 뜨면 안 됨)
            f = Arm(p).User(200, BodyDid).Reward(201, BodyPurchase9);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(23);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && p.PurchaseNo == 9 && !p.CanRetry && f.Count("POST") == 1,
                  $"[경품] 실서버 EVT-002 201(Created) → 접수 #{p.PurchaseNo} ({p.Claim}, 다시 요청 {p.CanRetry})");

            // 2) 시작 때 404, 경품 받기 때도 404 → POST 없이 '로그인되지 않음'
            f = Arm(p).User(404, BodyNoParticipant).User(404, BodyNoParticipant);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            check(p.Did == PrizeLink.DidState.NoParticipant, $"[경품] EVT-001 404 → 참가자 없음 ({p.Did})");
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Rejected && f.Count("POST") == 0 && f.Count("GET") == 2 && p.Reason.Contains("로그인"),
                  $"[경품] 경품 받기 때 재조회도 404 → POST 안 함, 사유 '{p.Reason}'");
            check(p.CanRetry && ClawRewardPanel.PrizeGuide(p).Contains("다시 요청"), "[경품] 로그인 없음 → 보상 화면에서 다시 요청 가능");
            f.User(200, BodyDid).Reward(200, BodyPurchase8);
            p.RetryPrize();
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && p.PurchaseNo == 8 && f.Count("POST") == 1 && !p.CanRetry,
                  $"[경품] 진행자 로그인 후 [경품 다시 요청] → 접수 ({p.Claim}, GET {f.Count("GET")})");

            // 3) 진행자가 늦게 로그인: 시작 404 → 경품 받기 때 재조회 200 → 접수
            f = Arm(p).User(404, BodyNoParticipant).User(200, BodyDid).Reward(200, BodyPurchase8);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(7);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && f.Count("GET") == 2 && f.requests[2].Contains("\"nftId\":7"),
                  $"[경품] 늦은 로그인 → 재조회 후 접수 ({p.Claim})");

            // 4) 통신 실패 2번 뒤 성공 → 접수(재시도 2회)
            f = Arm(p).User(200, BodyDid).Reward(0, null).Reward(503, "").Reward(200, BodyPurchase8);
            p.BeginParticipant();
            p.RequestPrize(4); // 조회 중에 눌러도 조회를 기다렸다가 보낸다
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && p.PurchaseNo == 8 && f.Count("POST") == 3 && f.Count("GET") == 1,
                  $"[경품] 조회 중 요청 대기 + 통신 실패·503 재시도 → 접수 (POST {f.Count("POST")}, GET {f.Count("GET")})");

            // 5) 응답 유실 뒤 재시도가 409 '이미 요청됨' → 앞 요청이 접수된 것 → 성공(접수번호 모름)
            f = Arm(p).User(200, BodyDid).Reward(0, null).Reward(409, BodyAlready);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && p.PurchaseNo < 0 && f.Count("POST") == 2,
                  $"[경품] 재시도 중 409 이미 요청됨 → 접수로 처리 ({p.Claim}, POST {f.Count("POST")})");
            check(ClawRewardPanel.PrizeGuide(p) != null && ClawRewardPanel.PrizeGuide(p).StartsWith("경품 요청 완료\n"), "[경품] 접수번호 모를 때 문구 '경품 요청 완료'");

            // 6) 첫 요청부터 409 이미 요청됨 = 진행자가 참가자 교체(로그아웃 → 이벤트 로그인)를 빼먹음 → 실패, 같은 DID로는 재요청 안 함.
            //    [다시 요청] 때 재조회 — 아직 같은 참가자면 보내지 않고, 진행자가 바꾼 뒤면 새 참가자로 보낸다(10-12)
            f = Arm(p).User(200, BodyDid).Reward(409, BodyAlready);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Rejected && f.Count("POST") == 1 && p.Reason.Contains("이미 경품이 요청됨") && p.Reason.Contains("로그아웃") && p.CanRetry,
                  $"[경품] 첫 409 → 재요청 없이 실패 '{p.Reason}', 참가자 교체 후 다시 요청 가능");
            f.User(200, BodyDid);
            p.RetryPrize();
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Rejected && f.Count("POST") == 1 && f.Count("GET") == 2 && p.Reason.Contains("그대로") && p.CanRetry,
                  $"[경품] 다시 요청 때 같은 참가자 → POST 안 함(409 재요청 금지) '{p.Reason}'");
            f.User(200, BodyDid2).Reward(200, BodyPurchase9);
            p.RetryPrize();
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && p.PurchaseNo == 9 && f.Count("POST") == 2 && f.requests[f.requests.Count - 1].Contains(Did2) && !p.CanRetry && p.DidTail.EndsWith(Did2.Substring(Did2.Length - 6)),
                  $"[경품] 진행자가 참가자 교체 → 다시 요청 → 새 참가자로 접수 ({p.Claim} #{p.PurchaseNo}, {p.DidTail})");

            // 6-1) 게임 도중 진행자가 참가자를 바꿈 → 시작 때 받은 DID는 409 현재 참가자 아님 → 다시 요청 → 새 참가자로 접수
            f = Arm(p).User(200, BodyDid).Reward(409, BodyNotCurrent);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Rejected && p.Reason.Contains("현재 진행 중인 참가자가 아님") && p.CanRetry,
                  $"[경품] 게임 도중 교체 → 409 현재 참가자 아님, 다시 요청 가능 '{p.Reason}'");
            f.User(200, BodyDid2).Reward(200, BodyPurchase9);
            p.RetryPrize();
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && f.Count("POST") == 2 && f.requests[f.requests.Count - 1].Contains(Did2),
                  $"[경품] 게임 도중 교체 → 다시 요청 → 새 참가자로 접수 ({p.Claim})");

            // 6-2) 진행자가 로그아웃만 하고 다음 참가자를 아직 로그인 안 함 → 지급 요청 404 → 앞 참가자 DID 지우고 다시 요청 가능 → 로그인 후 접수
            f = Arm(p).User(200, BodyDid).Reward(404, BodyNoParticipant);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Rejected && p.Did == PrizeLink.DidState.NoParticipant && p.DidTail == "" && p.Reason.Contains("로그인") && p.CanRetry,
                  $"[경품] 로그아웃만 함 → 지급 요청 404 → 다시 요청 가능, 앞 참가자 표시 안 함 '{p.Reason}'");
            f.User(200, BodyDid2).Reward(200, BodyPurchase9);
            p.RetryPrize();
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && f.Count("POST") == 2 && f.requests[f.requests.Count - 1].Contains(Did2),
                  $"[경품] 다음 참가자 로그인 후 다시 요청 → 접수 ({p.Claim})");

            // 6-3) 교체한 참가자도 이미 받은 사람 → 그 409는 '앞 요청 접수'가 아니다(앞서 보낸 건 다른 DID) → 실패
            f = Arm(p).User(200, BodyDid).Reward(409, BodyAlready).User(200, BodyDid2).Reward(409, BodyAlready);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            p.RetryPrize();
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Rejected && f.Count("POST") == 2 && p.Reason.Contains("이미 경품이 요청됨"),
                  $"[경품] 교체한 참가자도 409 이미 요청됨 → 접수로 오인하지 않음 ({p.Claim})");

            // 7) 재고 소진 404
            f = Arm(p).User(200, BodyDid).Reward(404, BodyStock);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Rejected && p.Reason == "경품 재고 소진" && f.Count("POST") == 1, $"[경품] 404 재고 → '{p.Reason}'");

            // 8) 토큰 오류 401 → 재시도 없이 실패, 화면 사유 '토큰'
            f = Arm(p).User(401, BodyBadToken).User(401, BodyBadToken);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Did == PrizeLink.DidState.Failed && p.Claim == PrizeLink.ClaimState.Failed && p.Reason.Contains("토큰") && f.Count("GET") == 2 && f.Count("POST") == 0,
                  $"[경품] 401 → 실패 '{p.Reason}' (GET {f.Count("GET")})");

            // 9) 서버가 아예 안 됨(통신 실패 3회) → 실패 '연결'
            f = Arm(p);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            check(p.Did == PrizeLink.DidState.Failed && f.Count("GET") == 3, $"[경품] 통신 실패 → 재시도 2회 후 실패 (GET {f.Count("GET")})");
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Failed && p.Reason.Contains("연결") && p.CanRetry, $"[경품] 경품 받기 → '{p.Reason}', 다시 요청 가능");

            // 9-1) 지급 요청 3번 모두 응답 없음 → 실패 → [다시 요청]에 409 이미 요청됨 = 앞 요청이 접수됐던 것 → 접수
            f = Arm(p).User(200, BodyDid).Reward(0, null).Reward(0, null).Reward(0, null).Reward(409, BodyAlready);
            p.BeginParticipant();
            yield return Until(() => p.Did != PrizeLink.DidState.Fetching);
            p.RequestPrize(4);
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Failed && p.CanRetry && f.Count("POST") == 3, $"[경품] 지급 요청 응답 없음 3회 → 실패·다시 요청 가능 (POST {f.Count("POST")})");
            p.RetryPrize();
            yield return Until(() => p.Claim != PrizeLink.ClaimState.Sending);
            check(p.Claim == PrizeLink.ClaimState.Accepted && f.Count("POST") == 4, $"[경품] 다시 요청에 409 이미 요청됨 → 접수로 처리 ({p.Claim})");

            // 10) nftId 0 → 요청 안 함(Skipped) / 연동 꺼짐 → 아무 요청 없음 / 토큰 없음 → 비활성
            f = Arm(p).User(200, BodyDid);
            p.RequestPrize(0);
            yield return null;
            check(p.Claim == PrizeLink.ClaimState.Skipped && f.requests.Count == 0, $"[경품] nftId 0 → 요청 없이 '{p.Reason}'");
            f = Arm(p, TestConfig(enabled: false));
            p.BeginParticipant(); p.RequestPrize(4);
            yield return null; yield return null;
            check(!p.Active && f.requests.Count == 0 && p.Claim == PrizeLink.ClaimState.None && ClawRewardPanel.PrizeGuide(p) == null,
                  "[경품] prizeEnabled=false → 요청 0, 기존 안내 문구");
            f = Arm(p, TestConfig(token: " "));
            p.BeginParticipant();
            yield return null;
            check(!p.Active && f.requests.Count == 0, "[경품] 토큰 비어 있음 → 비활성");

            // 11) 요청 중 운영자 리셋 → 끊고 비움
            f = Arm(p).User(200, BodyDid);
            p.BeginParticipant();
            p.ResetParticipant();
            yield return null; yield return null;
            check(p.Did == PrizeLink.DidState.None && p.Claim == PrizeLink.ClaimState.None && f.aborts >= 1, $"[경품] 요청 중 리셋 → 상태 비움·전송 중단 (abort {f.aborts})");

            // 12) 오류코드 시트 문구
            check(PrizeLink.DescribeFailure(404, "Data Not found. : 마켓 판매 정보").Contains("nftId") &&
                  PrizeLink.DescribeFailure(409, "EventParticipant(userDid) is not the current participant.").Contains("현재 진행 중인 참가자가 아님") &&
                  PrizeLink.DescribeFailure(404, "User(userNo) not found.").Contains("오스리움") &&
                  PrizeLink.DescribeFailure(500, "").Contains("오스리움"),
                  "[경품] 오류코드 시트 → 현장 문구 매핑");
            check(ConfigLoader.MaskToken("header.payload.fake-signature-ABCD") == "…ABCD", "[경품] 토큰은 로그에 끝 4자만");

            UnityEngine.Object.Destroy(go);
        }
#endif
    }
}
