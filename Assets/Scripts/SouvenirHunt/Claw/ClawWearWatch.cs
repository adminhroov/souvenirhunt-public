using System;
using System.Text;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.XR.XREAL;
#endif

namespace SouvenirHunt
{
    /// <summary>
    /// 10-26 안경 착용 감지 + 자동 처음으로 — 사용자: "착용하고 있을 때만 안구 인식", "다시 실행했을 때 혹은 글래스를 벗고 30초 정도 지나면
    /// 게임 처음 시작 단계로, 30초 카운트는 빔프로 화면에".
    /// · 착용: XREAL SDK 착용 이벤트(XREALCallbackHandler.OnXREALGlassesWearingState — 안경 근접 센서 PUT_ON/TAKE_OFF, SDK가 메인 스레드로 넘김).
    ///   바뀔 때만 오고 지금 상태를 묻는 API는 없다 → 처음엔 '모름'(= 쓴 것으로 봄). 지원 안 하는 안경이면 이벤트가 없어 예전처럼 동작.
    ///   벗은 동안 VipIntro는 홍채·시선 판정을 멈춘다(AllowEyeScan — 아무도 안 쓴 안경이 저절로 인증되지 않게).
    /// · 벗음 → Beam Pro 화면(ClawPadInput.SetNotice)에 clawWearResetSeconds(30) 카운트다운 → 0이면 운영자 리셋과 같은 "처음으로"(인증 씬 [시연 시작] 대기).
    ///   결과·보상 화면 [다시 시작](ClawSessionBase.Restart)도 같은 곳으로 — NoteManualReset으로 씬이 바뀌는 동안 카운트가 한 번 더 리셋하지 않게.
    ///   그 전에 다시 쓰면 취소. 단 결과 화면에서 [보상 확인](경품 요청 EVT-002) 전이면 처음으로 가지 않는다 — 경품을 잃지 않게, 진행자가 확인.
    /// · 다시 실행(멀티 리줌이라 홈·뒤로로 나가도 앱이 살아 있다): 백그라운드에서 clawResumeResetSeconds(3) 이상 있다 돌아오면 처음으로.
    ///   벗은 채 멈췄던 것이면 벗음 규칙(30초)으로. 새로 켜진 앱은 원래 부팅 씬(인증)부터.
    /// 시간은 실제 시계(UTC) — 앱이 멈춰 있던 동안도 센다. 인형뽑기 세션(ClawIntro·ClawSessionBase)에서만 동작. 씬과 무관하게 하나(시작 때 자동 생성).
    /// 예외를 던지지 않는다.
    /// </summary>
    public class ClawWearWatch : MonoBehaviour
    {
        public enum WearState { Unknown, On, Off }

        public static ClawWearWatch Instance { get; private set; }
        /// <summary>마지막으로 받은 착용 상태(이벤트가 아직 없으면 Unknown)</summary>
        public static WearState State { get; private set; } = WearState.Unknown;
        /// <summary>config clawWearDetect — 끄면 착용 상태를 무시(예전처럼)</summary>
        public static bool Enabled { get; private set; } = true;
        /// <summary>홍채·시선 인증을 진행해도 되는가 — 벗었다는 신호를 받은 동안만 false(모름·지원 안 함 = true)</summary>
        public static bool AllowEyeScan => !Enabled || State != WearState.Off;
        /// <summary>연결된 안경이 착용 감지를 지원한다고 SDK가 답함(로그·운영자 패널용 — 동작은 이벤트로만)</summary>
        public static bool Supported { get; private set; }
        public static event Action<WearState> Changed;

        /// <summary>벗은 뒤 처음으로까지 남은 초(카운트다운 중이 아니면 -1)</summary>
        public float Remaining { get; private set; } = -1f;
        public int AutoResets { get; private set; }
        public string LastResetReason { get; private set; } = "";
        /// <summary>Beam Pro 화면에 띄운 안내(빈 문자열 = 숨김) — 셀프테스트</summary>
        public string Notice { get; private set; } = "";

        float resetSeconds = 30f, resumeSeconds = 3f;
        DateTime offSinceUtc, pausedAtUtc;
        bool paused, offAtPause, resetDoneForThisOff;
        float cooldownUntil;
        IGameSession resetSession; // 쿨다운은 처음으로 보낸 그 세션(떠나는 화면)에만 — 도착한 새 화면은 바로 판단
#if UNITY_ANDROID && !UNITY_EDITOR
        float nextSupportProbe;
        bool supportLogged;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("ClawWearWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<ClawWearWatch>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            State = WearState.Unknown;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { XREALCallbackHandler.OnXREALGlassesWearingState += OnSdkWearing; }
            catch (Exception e) { Debug.LogWarning("[Wear] 착용 이벤트 구독 실패 — 착용 감지 없이(예전처럼): " + e.Message); }
#endif
        }

        void OnDestroy()
        {
            if (Instance != this) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { XREALCallbackHandler.OnXREALGlassesWearingState -= OnSdkWearing; } catch { }
#endif
            Instance = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void OnSdkWearing(XREALWearingStatus s) =>
            Apply(s == XREALWearingStatus.PUT_ON ? WearState.On : s == XREALWearingStatus.TAKE_OFF ? WearState.Off : WearState.Unknown, "안경 센서");
#endif

        void Apply(WearState next, string source)
        {
            if (next == State) return;
            State = next;
            if (next == WearState.Off) { offSinceUtc = DateTime.UtcNow; resetDoneForThisOff = false; }
            Debug.Log($"[Wear] {Label(next)} ({source})");
            EventUtil.SafeInvoke(Changed, next);
        }

        /// <summary>센서가 착용을 안 알려 줄 때 사람이 "썼음"으로(인증 화면 [인증 시작])</summary>
        public static void ForceWorn(string why) { if (Instance != null) Instance.Apply(WearState.On, why); }

        static string Label(WearState s) => s == WearState.On ? "착용" : s == WearState.Off ? "벗음" : "모름";

        static bool IsClaw(IGameSession s) => s is ClawIntro || s is ClawSessionBase;

        /// <summary>이미 처음 단계(인증 맨 앞 [시연 시작] 대기, 아무 진행 없음 — 인증 씬이 빌드에 없으면 게임 Ready) — 처음으로 갈 필요 없음</summary>
        static bool AtStart(IGameSession s) =>
            (s is ClawIntro ci && ci.InIntro && ci.Intro != null && ci.Intro.AtStart) ||
            (s is ClawSessionBase g && g.Current == ClawSessionBase.State.Ready);

        /// <summary>처음 단계에서 벗음 — Beam Pro 안내(카운트 없음)</summary>
        static string StartNotice(IGameSession s)
        {
            var vi = s is ClawIntro ci ? ci.Intro : null;
            if (vi != null && vi.StartConfirmPending)
                return Loc.T("안경 착용이 감지되지 않았어요\n<size=70%>안경을 썼다면 [시연 시작]을 한 번 더 누르세요</size>",
                             "Glasses not detected\n<size=70%>If you are wearing them, press [Start Demo] again</size>");
            if (vi != null && vi.Current == VipIntro.Phase.StartWait)
                return Loc.T("다음 체험자\n<size=70%>안경을 쓰고 [시연 시작]을 누르세요</size>", "Next player\n<size=70%>Put on the glasses and press [Start Demo]</size>");
            return Loc.T("다음 체험자\n<size=70%>안경을 쓰면 홍채 인증이 시작됩니다\n시작이 안 되면 [인증 시작]</size>",
                         "Next player\n<size=70%>The iris scan starts when you put on the glasses\nIf it doesn't start, press [Start Scan]</size>");
        }

        /// <summary>
        /// 10-26 [다시 시작]·운영자 리셋으로 처음으로 가는 중 — 씬이 바뀌는 동안 벗음 카운트가 끝나 한 번 더 리셋하지 않게(작업 리스트
        /// "수동 재시작과 30초 자동 초기화의 충돌"). 도착한 처음 단계에서 벗은 채면 카운트 없이 '다음 체험자' 안내
        /// </summary>
        public static void NoteManualReset()
        {
            var w = Instance;
            if (w == null) return;
            w.cooldownUntil = Time.unscaledTime + 3f;
            w.resetSession = GameSession.Current;
            w.resetDoneForThisOff = State == WearState.Off;
            w.Remaining = -1f;
        }

        /// <summary>보상 확인 전·경품 요청 중·재시도 대기에는 자동으로 씬을 떠나지 않는다.</summary>
        static bool RewardPending(IGameSession s) => s is ClawSessionBase g &&
            ((g.Current == ClawSessionBase.State.Result && g.RewardEligible) || g.PrizeSending ||
             (g.Current == ClawSessionBase.State.Reward && g.Prize != null && g.Prize.CanRetry));

        void Update()
        {
            ProbeSupport();
            var s = GameSession.Current;
            if (s == null || !IsClaw(s)) { Show(""); Remaining = -1f; return; }
            var cfg = s.Config;
            if (cfg != null)
            {
                Enabled = cfg.clawWearDetect;
                resetSeconds = cfg.clawWearResetSeconds;
                resumeSeconds = cfg.clawResumeResetSeconds;
            }
            if (!Enabled || State != WearState.Off) { Remaining = -1f; Show(""); return; }
            if (ClawSceneRouter.Loading) { Remaining = -1f; return; } // 씬을 바꾸는 중(처음으로·게임으로) — 도착한 화면에서 다시 판단(겹친 리셋 방지)

            if (AtStart(s)) { Remaining = -1f; Show(StartNotice(s)); return; }
            if (Time.unscaledTime < cooldownUntil && ReferenceEquals(s, resetSession)) { Remaining = -1f; Show(Loc.T("처음 화면으로 돌아가는 중…", "Going back to the start…")); return; }
            // 이번 벗음으로 이미 처음으로 보냈는데 도착한 화면이 처음 단계가 아님(vipIntroEnabled=false 등) — 다시 쓸 때까지 또 보내지 않음(씬마다 반복 방지)
            if (resetDoneForThisOff || resetSeconds <= 0f) { Remaining = -1f; Show(Loc.T("안경을 벗었어요\n<size=70%>다시 쓰면 이어서 해요</size>", "Glasses off\n<size=70%>Put them back on to continue</size>")); return; }
            if (RewardPending(s))
            {
                Remaining = -1f;
                bool sending = s is ClawSessionBase game && game.PrizeSending;
                Show(sending ? Loc.T("경품 요청 중이에요\n<size=65%>요청이 끝날 때까지 기다려 주세요</size>", "Requesting your prize\n<size=65%>Please wait until it finishes</size>")
                    : Loc.T("안경을 벗었어요\n<size=65%>[보상 확인] 또는 경품 처리가 끝나기 전이라 처음으로 돌아가지 않아요</size>",
                            "Glasses off\n<size=65%>Staying here until [View Reward] or the prize request is done</size>"));
                return;
            }
            Remaining = Mathf.Max(0f, resetSeconds - (float)(DateTime.UtcNow - offSinceUtc).TotalSeconds);
            if (Remaining <= 0f) { DoReset($"안경 벗고 {resetSeconds:0}초"); return; }
            Show(Loc.T($"안경을 벗었어요\n<size=260%>{Mathf.CeilToInt(Remaining)}</size>\n<size=70%>초 뒤 처음 화면으로 돌아갑니다\n다시 쓰면 이어서 해요</size>",
                       $"Glasses off\n<size=260%>{Mathf.CeilToInt(Remaining)}</size>\n<size=70%>seconds until back to the start\nPut them back on to continue</size>"));
        }

        void DoReset(string why)
        {
            var s = GameSession.Current;
            if (s == null) return;
            resetDoneForThisOff = State == WearState.Off;
            cooldownUntil = Time.unscaledTime + 3f; // 씬 로드 중 같은 세션에 다시 리셋하지 않게
            resetSession = s;
            AutoResets++;
            LastResetReason = why;
            Remaining = -1f;
            Debug.Log($"[Cycle] AUTO RESET — {why} → 처음으로 (지금 {s.StateName})");
            try { s.ResetGame(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Show(string text)
        {
            if (text == Notice) return;
            Notice = text;
            ClawPadInput.SetNotice(text);
        }

        /// <summary>SDK 지원 여부는 안경 세션이 붙은 뒤에야 답이 나온다 — 몇 초마다 물어 처음 true일 때 로그(동작은 이벤트로만)</summary>
        void ProbeSupport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (supportLogged || Time.unscaledTime < nextSupportProbe) return;
            nextSupportProbe = Time.unscaledTime + 3f;
            try { Supported = XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_WEARING_STATUS_OF_GLASSES); }
            catch (Exception e) { Supported = false; supportLogged = true; Debug.LogWarning("[Wear] 착용 감지 지원 확인 실패: " + e.Message); return; }
            if (Supported || Time.unscaledTime > 30f)
            {
                supportLogged = true;
                Debug.Log($"[Wear] 안경 착용 감지 {(Supported ? "지원" : "지원 안 함(30초 동안 false) — 이벤트가 오면 그래도 씀")}");
            }
#endif
        }

        // ─ 다시 실행(백그라운드에서 돌아옴) ─
        void OnApplicationPause(bool pause)
        {
            if (Application.isEditor) return; // 에디터는 창 포커스로도 불린다 — 셀프테스트는 SimulateResume
            if (pause)
            {
                if (paused) return;
                paused = true;
                pausedAtUtc = DateTime.UtcNow;
                offAtPause = State == WearState.Off;
                return;
            }
            if (!paused) return;
            paused = false;
            OnResumed((DateTime.UtcNow - pausedAtUtc).TotalSeconds);
        }

        void OnResumed(double away)
        {
            var s = GameSession.Current;
            Debug.Log($"[Wear] 백그라운드에서 돌아옴 {away:0.0}초 (착용 {Label(State)}, 멈출 때 {(offAtPause ? "벗음" : "벗지 않음")}, {(s != null ? s.StateName : "세션 없음")})");
            if (s == null || !IsClaw(s) || resumeSeconds <= 0f) return;
            if (offAtPause || State == WearState.Off) return; // 벗은 채 멈춤 — 벗음 규칙(Update가 실제 시계로 센다)
            if (away < resumeSeconds || AtStart(s)) return;
            if (RewardPending(s)) { Debug.Log("[Wear] 다시 실행 — [보상 확인] 전이라 처음으로 가지 않음"); return; }
            DoReset($"다시 실행(백그라운드 {away:0}초)");
        }

        /// <summary>운영자 패널 한 줄</summary>
        public static void AppendSummary(StringBuilder sb)
        {
            var w = Instance;
            string countdown = w != null && w.Remaining >= 0f ? $" · 처음으로까지 {Mathf.CeilToInt(w.Remaining)}초" : "";
            string resets = w != null && w.AutoResets > 0 ? $" · 자동 처음으로 {w.AutoResets}회(마지막: {w.LastResetReason})" : "";
            sb.AppendLine($"안경 착용: {(Enabled ? Label(State) : "감지 끔")}{(Supported ? "" : " (센서 응답 없음)")}{countdown}{resets}");
        }

#if UNITY_EDITOR
        /// <summary>셀프테스트 — 안경 센서 이벤트 흉내</summary>
        public static void SimulateWear(WearState s) { if (Instance != null) Instance.Apply(s, "테스트"); }
        /// <summary>셀프테스트 — 벗은 시각을 secondsAgo초 전으로(카운트다운 단축)</summary>
        public static void SimulateOffAgo(double secondsAgo) { if (Instance != null) Instance.offSinceUtc = DateTime.UtcNow.AddSeconds(-secondsAgo); }
        /// <summary>셀프테스트 — 백그라운드에서 away초 있다 돌아옴</summary>
        public static void SimulateResume(double away, bool offWhenPaused = false)
        {
            if (Instance == null) return;
            Instance.offAtPause = offWhenPaused;
            Instance.OnResumed(away);
        }
#endif
    }
}
