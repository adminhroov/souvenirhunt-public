using System;
using System.Collections;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 코어 상태 머신 · 타이머 · 이벤트 허브 (T5).
    /// 플로우: Ready → Countdown → Playing → Result → Reward, 그리고 어느 상태에서든 Reset → Ready (기획서 §03).
    /// UI·후속 시스템은 아래 이벤트만 구독한다 (직접 참조 금지).
    /// 규칙 값은 리셋마다 config.txt에서 재로드 (Phase 0) — 인스펙터 값은 에디터 기본값일 뿐.
    /// 모든 이벤트는 SafeInvoke로 발행 — 구독자 하나의 예외가 나머지 배선을 끊지 않는다 (앱은 절대 죽지 않게).
    /// </summary>
    public class GameManager : MonoBehaviour, IGameSession
    {
        public static GameManager Instance { get; private set; }

        public enum State { Ready, Countdown, Playing, Result, Reward }
        public State Current { get; private set; }

        [Header("의존성")]
        [SerializeField] SpawnManager spawner;
        [SerializeField] MeshLibrary meshLibrary;

        [Header("규칙 (config.txt가 소스오브트루스 — 여기는 로드 전 기본값)")]
        [SerializeField] int targetCount = 10;
        [Tooltip("제한시간(초). 기본 3:00. 0이면 무제한.")]
        [SerializeField] float timeLimitSec = 180f;
        [Tooltip("config.txt rewardOnPartial이 리셋마다 덮어씀. 발주처 미확정 — 기본 false(전체 수집 시에만 보상).")]
        [SerializeField] bool rewardOnPartial = false;
        [Tooltip("게임 시작 버튼 클릭 후 카운트다운 초.")]
        [SerializeField] int countdownFrom = 3;

        // 카운트다운 한 자리당 간격 — 인수인계 §4-1 명시값 (1초 아님)
        const float CountdownStepSec = 0.9f;

        // ─ 이벤트 허브 ─────────────────────────────────────────────
        public event Action<int, int> OnCollected;          // (current, total)
        public event Action<int> OnCountdown;               // 3·2·1
        public event Action<float> OnTimeTick;              // 남은 시간(초) — 정수 초가 바뀔 때만 (초당 1회)
        public event Action<bool, int, float> OnGameResult; // (success, collected, elapsed)
        public event Action OnGameReset;
        public event Action<State> OnStateChanged;
        public event Action<bool> OnPauseChanged;   // Phase 5-1: true=일시정지(백그라운드)

        // ─ 2차 연동 자리 (1차 = Mock) ─────────────────────────────
        public IAuthGate Auth { get; set; } = new MockAuthGate();
        public IRewardIssuer Reward { get; set; } = new MockRewardIssuer();

        public int TargetCount => targetCount;
        public int Collected => collected;
        public float WarnThresholdSec { get; private set; } = 30f;
        public bool RewardOnPartial => rewardOnPartial;
        /// <summary>이번 판 보상 대상 여부 (EndGame에서 확정: 성공 or 부분 보상 허용)</summary>
        public bool RewardEligible { get; private set; }
        public GameConfig Config { get; private set; }
        public RewardTable Rewards { get; private set; }

        public float LastResetMs { get; private set; }  // Phase 5-1: §4-2 리셋 1초 계측
        /// <summary>마지막 수집 위치 (GameAudio 3D 수집음 등에서 사용)</summary>
        public Vector3 LastCollectedPosition { get; private set; }

        int collected;
        float remain;
        float lastElapsed;   // Result·Reward에서 참조하는 이번 판 소요 시간
        int lastWholeSec = int.MinValue; // OnTimeTick 초당 1회 발행용
        bool paused;         // Phase 5-1: 백그라운드 중 타이머 정지
        bool resuming;       // 카운트다운 재시작 중복 방지

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GameSession.Register(this); // 운영자 패널·렌더 품질 등 공용 컴포넌트가 씬 무관하게 찾는 경로
        }

        void Start() => ResetGame();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameSession.Unregister(this);
        }

        // ─ IGameSession (씬 공용 계약) ────────────────────────────────
        public string StateName => Current.ToString();
        /// <summary>BoothAligner 첫 정렬 후 — GameManager.Start(0프레임)가 정렬 전 위치에 스폰했으므로 재스폰</summary>
        public void OnBoothAligned() => ResetGame();
        /// <summary>Home 리센터 후 — 상태 유지, 수집품만 이동된 스폰 포인트로</summary>
        public void OnRecentered() => RepositionOnly();

        public void AppendOperatorSummary(System.Text.StringBuilder sb)
        {
            var cfg = Config;
            if (cfg == null) return;
            sb.AppendLine($"제한시간   {cfg.timeLimitSeconds}초");
            sb.AppendLine($"목표 개수  {TargetCount}개" +
                          (TargetCount != cfg.targetCount ? $"  (config {cfg.targetCount} → clamp)" : ""));
            sb.AppendLine($"카운트다운 {cfg.countdownSeconds}초 · 경고시점 {cfg.warningTimeSeconds}초");
            sb.AppendLine($"고정 배치  {(cfg.useFixedLayout ? "ON" : "OFF")}");
            sb.AppendLine("보상:");
            for (int i = 0; i < cfg.rewards.Count; i++)
                sb.AppendLine($" · {cfg.rewards[i].text} (mesh {cfg.rewards[i].meshIndex})");
        }

        // StartPanel 버튼이 호출 — 플로우 Step 2 ("게임 시작하기")
        public void RequestStart()
        {
            if (Current != State.Ready) return;
            if (!Auth.IsAuthorized()) return;          // 2차: DID 인증 통과 후에만
            StartCoroutine(CountdownRoutine());
        }

        IEnumerator CountdownRoutine()
        {
            SetState(State.Countdown);
            for (int n = countdownFrom; n > 0; n--)
            {
                SafeInvoke(OnCountdown, n);             // HUD가 3·2·1 표시
                yield return new WaitForSeconds(CountdownStepSec);
            }
            SafeInvoke(OnCountdown, 0);
            // 타이머는 카운트다운이 끝난 뒤 기산 (공정성 — 인수인계 §4-1)
            remain = timeLimitSec;
            lastWholeSec = int.MinValue;
            SetState(State.Playing);
            Debug.Log($"[Cycle] START target={targetCount} timeLimit={timeLimitSec:F0}s"); // Phase 5-3 리허설 추적
        }

        void Update()
        {
            if (Current != State.Playing || timeLimitSec <= 0f || paused) return;

            remain -= Time.deltaTime;
            float shown = Mathf.Max(remain, 0f);
            // 표시값(정수 초)이 바뀔 때만 발행 — HUD 문자열 재생성·TMP 리빌드를 초당 1회로 제한 (60FPS 수호)
            int whole = Mathf.FloorToInt(shown);
            if (whole != lastWholeSec)
            {
                lastWholeSec = whole;
                SafeInvoke(OnTimeTick, shown);
            }
            if (remain <= 0f) EndGame(success: false);  // 플로우 4-ⓑ 시간 초과
        }

        // Collectible이 수집될 때 호출 — 클릭 시점에 즉시 (연출 대기 없음, 막판 클릭 유실 방지)
        public void NotifyCollected(Collectible c)
        {
            if (Current != State.Playing) return;
            collected++;
            if (c != null) LastCollectedPosition = c.transform.position;
            SafeInvoke(OnCollected, collected, targetCount);
            if (collected >= targetCount) EndGame(success: true); // 플로우 4-ⓐ 전체 수집
        }

        void EndGame(bool success)
        {
            SetState(State.Result);
            lastElapsed = timeLimitSec - remain; // 일시정지 시간 제외 (Time.time 기반 아님, Phase 5-1)
            RewardEligible = success || rewardOnPartial;   // 부분 보상 정책(발주처 미확정, 기본 미지급)
            SafeInvoke(OnGameResult, success, collected, lastElapsed);
            float min60 = PerfMonitor.Instance != null ? PerfMonitor.Instance.Min60 : -1f;
            Debug.Log($"[Cycle] END {(success ? "성공" : "시간초과")} 수집={collected}/{targetCount} 경과={lastElapsed:F1}s min60={min60:F0} reward={RewardEligible}");
        }

        /// <summary>결과 패널 [보상 확인] 버튼이 호출 — Result → Reward (기획서 §03). 보상 대상이 아니면 무시.</summary>
        public void ConfirmReward()
        {
            if (Current != State.Result || !RewardEligible) return;
            try { Reward.Issue(collected, lastElapsed); }          // 플로우 5 보상 (1차 Mock, 2차 NFT)
            catch (Exception e) { Debug.LogException(e); }         // 보상 발급 실패가 화면 전환을 막지 않게
            SetState(State.Reward);
            Debug.Log("[Cycle] REWARD 안내 표시");
        }

        // 운영자 리셋 버튼(T7)이 호출 — 어느 상태에서든, 목표 1초 이내 복귀.
        // config.txt 재로드 → 적용 → 재배치 순서 (재시작 없이 값 반영, §4-2 재로드)
        public void ResetGame()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            StopAllCoroutines();
            collected = 0;
            paused = false;
            resuming = false;
            RewardEligible = false;
            lastWholeSec = int.MinValue;

            ApplyConfig(ConfigLoader.Load());

            remain = timeLimitSec;
            if (spawner != null) spawner.Respawn();
            if (CollectVfxPool.Instance != null) CollectVfxPool.Instance.StopAll(); // 진행 중이던 "+1" 잔상 정리
            SetState(State.Ready);                      // 시작 버튼 대기로 복귀
            SafeInvoke(OnGameReset);
            sw.Stop();
            LastResetMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[Reset] {LastResetMs:F0}ms (목표 ≤1000)");
            Debug.Log("[Cycle] RESET → Ready");
        }

        /// <summary>리센터(Home)용 — config 재로드·상태 변경 없이 이동된 스폰 포인트로 수집품만 재배치 (Phase 5-1).</summary>
        public void RepositionOnly()
        {
            if (spawner != null) spawner.Reposition();
        }

        // ─ 일시정지 / 복귀 (Phase 5-1, 인수인계 §6 글래스 이탈·재착용) ──────────
        // runInBackground=false → 백그라운드 시 OnApplicationPause(true). 기기별로 Focus만 오는 경우도 대비.
        void OnApplicationPause(bool pause) => SetBackground(pause);
        void OnApplicationFocus(bool focus) => SetBackground(!focus);

        void SetBackground(bool background)
        {
            if (background)
            {
                if (Current == State.Playing && !paused)
                {
                    paused = true;
                    SafeInvoke(OnPauseChanged, true);
                }
                else if (Current == State.Countdown)
                {
                    // 카운트다운 도중 이탈 → 복귀 시 처음부터 (사용자 결정 08-26)
                    StopAllCoroutines();
                    resuming = true;
                }
            }
            else
            {
                if (paused)
                {
                    paused = false;
                    SafeInvoke(OnPauseChanged, false);
                }
                if (resuming)
                {
                    resuming = false;
                    SetState(State.Ready);
                    RequestStart(); // 카운트다운 3부터 재시작
                }
            }
        }

        // ─ 구독자 한 명의 예외가 나머지 UI 배선을 끊지 않도록 격리 (Phase 5-1) — 본문은 EventUtil로 이동(09-03, Claw와 공유) ─
        static void SafeInvoke(Action evt) => EventUtil.SafeInvoke(evt);
        static void SafeInvoke<T>(Action<T> evt, T a) => EventUtil.SafeInvoke(evt, a);
        static void SafeInvoke<T1, T2>(Action<T1, T2> evt, T1 a, T2 b) => EventUtil.SafeInvoke(evt, a, b);
        static void SafeInvoke<T1, T2, T3>(Action<T1, T2, T3> evt, T1 a, T2 b, T3 c) => EventUtil.SafeInvoke(evt, a, b, c);

        void ApplyConfig(GameConfig cfg)
        {
            Config = cfg;
            if (meshLibrary == null)
                cfg.warnings.Add("meshLibrary 미배선 — 기념품이 기본 모양으로만 보임 (에디터 메뉴 XREAL/21 실행)");
            Rewards = new RewardTable(cfg, meshLibrary); // meshIndex 범위 검증·경고는 RewardTable이 담당

            timeLimitSec = cfg.timeLimitSeconds;
            countdownFrom = cfg.countdownSeconds;
            WarnThresholdSec = cfg.warningTimeSeconds;
            rewardOnPartial = cfg.rewardOnPartial;

            // targetCount 상한 = 스폰 포인트·풀 여유 (§4-2 clamp 규칙의 씬 의존 부분)
            int cap = spawner != null ? spawner.MaxSpawnable : cfg.targetCount;
            targetCount = Mathf.Clamp(cfg.targetCount, 1, Mathf.Max(1, cap));
            if (targetCount != cfg.targetCount)
                cfg.warnings.Add($"targetCount={cfg.targetCount} > 배치 가능 수({cap}) → {targetCount} 적용");

            if (spawner != null)
                spawner.Configure(targetCount, cfg.useFixedLayout, Rewards);
        }

        void SetState(State next)
        {
            Current = next;
            SafeInvoke(OnStateChanged, next);
        }
    }
}
