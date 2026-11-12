using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 세션 공통 흐름(10-01 분리). 10-15부터 게임은 미션형 하나(ClawGameManager — 모드1·2 삭제)지만 틀은 그대로 둔다:
    /// 상태 흐름 Ready → Reveal(검정 전환) → Guide(START) → Countdown → Playing → Result → Reward.
    /// 운영자 리셋, 정면 재정렬(홈 길게), 종료/취소 다이얼로그(홈), 백그라운드 일시정지, 보상 발급, IGameSession.
    /// 10-26 동영상만 삭제. 인증 완료 후 기존 검정 전환을 거쳐 기계·부스·상품을 표시한다.
    /// 10-26 끝 화면(시간 초과 결과·보상)에서 [다시 시작] → 처음 화면(인증 씬 [시연 시작] 대기 — Restart).
    /// 10-28 Reveal 안에 기계 다가오기: 검정 뒤 루트를 멀리(BoothApproach.Prepare) → 디졸브 끝 → 가속하며 제자리로 → 작은 흔들림 → Guide.
    /// Playing 안의 규칙은 파생 클래스가 TickPlaying·PlaceItems·OnPlayingStarted·OnResetGame·ApplyGameConfig로 채운다.
    /// 규칙(09-03부터 동일): 이벤트만 구독(직접 참조 금지), config.txt 리셋마다 재로드, SafeInvoke, 앱은 절대 죽지 않게.
    /// 직렬화 필드 이름은 분리 전 ClawGameManager와 같다 — 기존 씬 배선이 그대로 유지된다.
    /// </summary>
    public abstract class ClawSessionBase : MonoBehaviour, IGameSession
    {
        public static ClawSessionBase Instance { get; private set; }

        /// <summary>삭제한 Video의 숫자는 재사용하지 않는다.</summary>
        public enum State { Ready = 0, Reveal = 2, Guide = 3, Countdown = 4, Playing = 5, Result = 6, Reward = 7 }
        public State Current { get; private set; }

        [Header("의존성(공통 틀)")]
        [SerializeField] protected ClawItemPool pool;
        [SerializeField] protected ClawBasket basket;
        [SerializeField] protected ClawCarriage carriage;
        [SerializeField] protected ClawHead head;
        [SerializeField] protected MeshLibrary meshLibrary;
        [SerializeField] protected BoothAligner booth;
        [SerializeField] protected MachineVisibility machine;
        [SerializeField] protected MachineShake shake;
        [SerializeField] protected InteractorSuppressor suppressor;
        [SerializeField] protected ClawExitDialog exitDialog;
        [SerializeField] protected DigitalReveal reveal;
        bool revealEnabled = true;
        public bool RevealEnabled => revealEnabled && reveal != null;
        public DigitalReveal RevealPanel => reveal;
        // 10-28 기계 다가오기(BoothApproach — BoothRoot에 런타임 자동 부착). 새 상태 없이 Reveal 안에서: 디졸브가 끝나면 멀리서 제자리로 → Guide
        BoothApproach approach;
        float approachDistance = 3f, approachSeconds = 1.5f, approachPower = 3f;
        // 11-10 시점 기울기(ClawGame 시점) — config clawViewTiltDeg. 검정 전환(RevealBlack)에서 켜고 처음 화면으로 갈 때 끈다(BoothAligner가 XR 리그를 숙임)
        float viewTiltDeg = 13.2f;
        public float ViewTiltDeg => viewTiltDeg;
        public BoothApproach Approach => approach;
        /// <summary>기계가 다가오는 중(Reveal 안)</summary>
        public bool Approaching => approach != null && approach.Running;
        public bool ApproachEnabled => RevealEnabled && approach != null && approachDistance > 0f;
        // 게임별 ApplyGameConfig가 채운다(로드 전 기본값)
        protected int countdownFrom = 3;
        const float CountdownStepSec = 0.9f;

        // ─ 공통 이벤트 허브 ─
        public event Action<State> OnStateChanged;
        public event Action<int> OnCountdown;                    // 3·2·1·0
        public event Action<float> OnTimeTick;                   // 남은 초 — 정수 초가 바뀔 때만(게임이 의미를 정함)
        /// <summary>표시 숫자 중 하나라도 바뀜 — 표시 쪽이 프로퍼티를 다시 읽는다</summary>
        public event Action OnCountsChanged;
        public event Action OnGameReset;
        public event Action<bool> OnPauseChanged;
        public event Action<bool> OnExitDialogChanged;           // 종료/취소 다이얼로그 열림/닫힘
        public event Action OnRealigned;                         // 홈 길게 누르기 재정렬(4차) — IGameSession.OnRecentered(메서드)와 이름 구분
        /// <summary>Playing 중 오버레이 잡기·조이스틱 라벨/활성이 바뀜(게임 단계 전환) — ClawPadPresenter가 다시 그린다</summary>
        public event Action OnPadLabelsChanged;

        public int RecenterCount { get; private set; }
        public IRewardIssuer Reward { get; set; } = new MockRewardIssuer();
        /// <summary>오스리움 경품 연동(10-06) — 같은 오브젝트에 자동 부착(씬 배선 불필요). config prizeEnabled=false면 아무 것도 안 함</summary>
        public PrizeLink Prize { get; private set; }
        /// <summary>실기기 = SDK와 동일(XREALPlugin.QuitApplication). 셀프테스트가 교체해 실제 종료 없이 경로 검증.</summary>
        public Action QuitHandler { get; set; } = QuitLikeSdk;
        /// <summary>[다시 시작]이 실제로 하는 일 — null이면 운영자 리셋과 같은 길(인증 씬 [시연 시작] 대기). 셀프테스트가 교체해 씬을 떠나지 않고 경로 검증</summary>
        public Action RestartHandler { get; set; }

        /// <summary>결과·보상 화면에 들어온 뒤 [다시 시작]을 받기까지(초) — 잡기 연타로 결과를 못 보고 넘어가지 않게</summary>
        public const float RestartGuardSec = 1.5f;
        /// <summary>경품 요청(EVT-002) 응답을 기다리는 중 — 이때 씬을 떠나면 응답 처리가 끊긴다</summary>
        public bool PrizeSending => Prize != null && Prize.Active && Prize.Claim == PrizeLink.ClaimState.Sending;
        /// <summary>
        /// 10-26 지금 화면이 [다시 시작]을 내놓는가 — 시간 초과 결과(보상 대상 아님), 보상 화면(경품 요청 중·다시 요청 가능이 아닐 때).
        /// 보상 대상 결과는 [보상 확인]이 먼저(경품을 잃지 않게)
        /// </summary>
        public bool RestartOffered =>
            (Current == State.Result && !RewardEligible) ||
            (Current == State.Reward && !PrizeSending && !(Prize != null && Prize.CanRetry));
        /// <summary>[다시 시작]을 지금 누르면 받는가(화면에 들어온 뒤 RestartGuardSec 지남)</summary>
        public bool RestartAllowed => RestartOffered && !restarting && Time.unscaledTime - endShownAt >= RestartGuardSec;
        public int Restarts { get; private set; }
        float endShownAt;
        bool restarting;
        public bool ExitDialogOpen => exitDialog != null && exitDialog.IsOpen;

        public float WarnThresholdSec { get; protected set; } = 5f;
        public bool RewardEligible { get; protected set; }
        public GameConfig Config { get; private set; }
        public RewardTable Rewards { get; private set; }
        public float LastResetMs { get; private set; }
        public virtual string StateName => Current.ToString();
        public float LastSettleMs => basket != null ? basket.LastSettleMs : -1f;
        /// <summary>풀 크기(= spec.poolSize) — config 공 개수 상한</summary>
        public int PoolCapacity => pool != null ? pool.Capacity : ClawItemSpec.Default.poolSize;
        /// <summary>
        /// 배치 개수 합 상한 — 풀 크기. 10-20 받침 더미가 켜져 있으면 실물은 그 위 한 층뿐이라 그 층 셀 수 안으로(넘치면 더미 꼭대기를 넘는다),
        /// 풀은 승격(받침 캡슐 → 진짜 캡슐) 몫을 남긴다
        /// </summary>
        public int PlaceCapacity => basket != null && basket.BedActive
            ? Mathf.Max(0, Mathf.Min(PoolCapacity - ClawBasket.PromotionReserve - basket.RealTopReserve, basket.SingleLayerCellCount))
            : PoolCapacity; // 10-22 진짜 맨 윗층 캡슐 몫도 뺀다
        /// <summary>기계·부스가 보이는 중 — 디졸브로 이미 드러났으면 Guide([START])에서도 true</summary>
        public bool MachineShown => machine != null && machine.IsShown;

        // ─ 오버레이 라벨(Playing 중) — 게임이 단계별로 덮어쓴다 ─
        public virtual bool PlayingGrabEnabled => true;
        public virtual string PlayingGrabLabel => Loc.T("잡기", "Grab");
        public virtual string PlayingStickLabel => Loc.T("조이스틱", "Joystick");

        protected bool paused, resuming, finishing;
        protected float playStart, lastElapsed;
        protected readonly List<ClawItem> scratch = new(320);
        float prevMaxDelta = -1f;

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GameSession.Register(this);
            Prize = GetComponent<PrizeLink>();
            if (Prize == null) Prize = gameObject.AddComponent<PrizeLink>();
            Prize.OnChanged += RaisePadLabelsChanged; // 보상 화면 [잡기] 라벨(완료 ↔ 경품 다시 요청)
            if (booth != null)
            {
                // 10-28 기계 다가오기 — BoothRoot에 자동 부착(씬 배선 불필요, 메뉴 30도 미리 붙임)
                approach = booth.Root.GetComponent<BoothApproach>();
                if (approach == null) approach = booth.Root.gameObject.AddComponent<BoothApproach>();
                approach.OnMoved += OnApproachMoved;
            }
            prevMaxDelta = Time.maximumDeltaTime;
            Time.maximumDeltaTime = 0.1f; // 히치 뒤 물리 따라잡기 스텝 제한(기본 0.333 = 최대 16스텝)
#if UNITY_ANDROID && !UNITY_EDITOR
            Screen.orientation = ScreenOrientation.Portrait; // Beam Pro를 눕혀도 화면은 세로 고정(오버레이 좌표 기준)
#endif
        }

        protected virtual void Start()
        {
            ResetGame();
            // 인증 씬(ClawIntro)에서 넘어왔으면 바로 검정 전환 후 공간 표시
            if (ClawSceneRouter.ConsumeAutoStart()) RequestStart();
        }

        /// <summary>
        /// 운영자 리셋(SCR-07 버튼 → GameSession.Current.ResetGame) = 체험자 교체 → 인증 씬(ClawIntro)으로(VIP 인증부터).
        /// 인증 씬이 빌드에 없으면 기존처럼 이 씬에서 Ready로. 코드에서 직접 부르는 ResetGame()(셀프테스트 등)은 씬을 떠나지 않는다.
        /// </summary>
        void IGameSession.ResetGame()
        {
            ClawWearWatch.NoteManualReset();
            if (booth != null) booth.SetViewTilt(0f, 0.5f); // 11-10 처음 화면(인증 씬 — 새 리그는 기울기 없음)으로 — 0.5초 안에 부드럽게 끔
            if (!ClawSceneRouter.LoadIntro()) ResetGame();
        }

        /// <summary>
        /// 10-26 결과·보상 화면 [다시 시작](작업 리스트 "게임 종료 후 다시 시작 UI — 선택 시 최초 시작 단계로 복귀, 게임·컨트롤러 상태 초기화")
        /// → 운영자 리셋과 같은 길: 인증 씬 [시연 시작] 대기(씬을 새로 불러 게임 상태 전부 새로), 패드 입력 비움. 막 들어온 화면·보상 확인 전·경품 요청 중이면 무시
        /// </summary>
        public void Restart()
        {
            if (!RestartAllowed) return;
            restarting = true;
            Restarts++;
            ClawPadInput.Clear();
            ClawWearWatch.NoteManualReset();
            Debug.Log($"[Cycle] RESTART — [다시 시작] ({Current}) → 처음 화면([시연 시작] 대기)");
            try
            {
                if (RestartHandler != null) RestartHandler();
                else ((IGameSession)this).ResetGame();
            }
            catch (Exception e) { Debug.LogException(e); restarting = false; }
        }

        protected virtual void OnDestroy()
        {
            if (approach != null) approach.OnMoved -= OnApproachMoved;
            if (prevMaxDelta > 0f) Time.maximumDeltaTime = prevMaxDelta;
            if (Instance == this) Instance = null;
            GameSession.Unregister(this);
        }

        // ─ 게임별 훅 ────────────────────────────────────────────────
        /// <summary>기계 등장 직후(카운트다운 전) 공 배치 — 이미 정착한 모습으로 보이게</summary>
        protected abstract void PlaceItems();
        /// <summary>카운트다운 끝 → Playing 진입 직후</summary>
        protected abstract void OnPlayingStarted();
        /// <summary>Playing 매 프레임(일시정지·다이얼로그 중엔 호출 안 됨)</summary>
        protected abstract void TickPlaying();
        /// <summary>운영자 리셋 — 게임 숫자 초기화(config는 이미 적용됨)</summary>
        protected abstract void OnResetGame();
        /// <summary>config 중 게임 고유 키 적용 — WarnThresholdSec도 여기서</summary>
        protected abstract void ApplyGameConfig(GameConfig cfg);
        /// <summary>보상 발급에 넘길 점수(미션형 = 넣은 그릇 수)</summary>
        protected abstract int RewardScore { get; }
        /// <summary>이 게임의 경품 nftId(10-15: 넣은 그릇 종류별 prizeNftIdCeramic / prizeNftIdGlass). 0 = 요청 안 함</summary>
        protected abstract int PrizeNftId { get; }
        /// <summary>운영자 패널 — 게임 고유 줄. 경품 연동 줄은 공통으로 뒤에 붙는다</summary>
        protected abstract void AppendGameSummary(StringBuilder sb);

        public void AppendOperatorSummary(StringBuilder sb)
        {
            AppendGameSummary(sb);
            AppendPrizeSummary(sb);
            ClawWearWatch.AppendSummary(sb);
        }

        void AppendPrizeSummary(StringBuilder sb)
        {
            var cfg = Config;
            if (cfg == null || !cfg.prizeEnabled) return;
            if (Prize == null || !Prize.Active) { sb.AppendLine("경품 연동: 켜짐 — 토큰 없음(요청 안 됨)"); return; }
            string did = Prize.Did switch
            {
                PrizeLink.DidState.Ready => "참가자 " + Prize.DidTail,
                PrizeLink.DidState.Fetching => "참가자 조회 중",
                PrizeLink.DidState.NoParticipant => "진행자 화면 로그인 없음",
                PrizeLink.DidState.Failed => "참가자 조회 실패(HTTP " + Prize.LastStatus + ")",
                _ => "게임 시작 전",
            };
            string claim = Prize.Claim switch
            {
                PrizeLink.ClaimState.Sending => " · 경품 요청 중",
                PrizeLink.ClaimState.Accepted => " · 경품 접수" + (Prize.PurchaseNo > 0 ? " #" + Prize.PurchaseNo : ""),
                PrizeLink.ClaimState.Rejected or PrizeLink.ClaimState.Failed or PrizeLink.ClaimState.Skipped => " · 경품 실패: " + Prize.Reason,
                _ => "",
            };
            sb.AppendLine($"경품 연동 nftId {PrizeNftId}: {did}{claim}");
        }
        /// <summary>RESET 로그 꼬리(예: "다음 목표 72")</summary>
        protected virtual string ResetSummary => "";
        protected virtual void OnPlayPaused() { }
        protected virtual void OnPlayResumed() { }
        protected virtual void OnExitDialogOpened() { }
        protected virtual void OnExitDialogClosed() { }

        // ─ 플로우 ────────────────────────────────────────────────────
        /// <summary>인증 후 동영상 없이 검정 전환 → 공간 표시 → START.</summary>
        public void RequestStart()
        {
            if (Current != State.Ready) return;
            ClawPadInput.Clear();
            Prize?.BeginParticipant();
            if (RevealEnabled)
            {
                SetState(State.Reveal);
                reveal.Begin(RevealBlack, RevealDone, Config != null && Config.audio);
                return;
            }
            RevealBlack();
            GoGuide();
        }

        void RevealBlack()
        {
            if (booth != null) booth.Align();
            // 11-10 기계가 드러나기 직전 검정 화면 — 시점 기울기 켬(즉시, 돔이 가린 동안). EditorSkipReveal·전환 끔 경로도 여기를 지난다
            if (booth != null) booth.SetViewTilt(viewTiltDeg);
            // 10-28 기계 다가오기: 정렬 자세(제자리)를 기억하고 루트를 멀리 — 기계 표시·상품 배치·정착은 멀리 있는 자리에서(돔이 가림)
            if (ApproachEnabled && booth != null) approach.Prepare(booth.Root.position, booth.Root.rotation, approachDistance);
            if (machine != null) machine.Show(false);
            if (head != null) head.ResetHead();
            PlaceItems();
            if (carriage != null) carriage.SetLocalXZ(new Vector2(0f, ClawLayout.CenterZ));
            Debug.Log("[Cycle] SPACE READY — 영상 없이 인형뽑기 공간 배치");
        }

        void RevealDone()
        {
            if (Current != State.Reveal) return;
            if (approach != null && approach.Active)
            {
                // 10-28 디졸브가 멀리 있는 기계를 드러낸 뒤 → 가속하며 제자리로. 이동 중 상품은 고정해 루트와 같이 옮긴다
                if (basket != null) basket.FreezeForTransit(true);
                approach.Begin(approachSeconds, approachPower, ApproachDone);
                return;
            }
            GoGuide();
        }

        /// <summary>10-28 다가오기 도착 — 상품 풀고(잠재움) 작은 흔들림으로 도킹 느낌 → [START]</summary>
        void ApproachDone()
        {
            if (basket != null) basket.FreezeForTransit(false);
            Physics.SyncTransforms();
            if (shake != null) shake.Shake(0.004f, 0.3f);
            if (Current == State.Reveal) GoGuide();
        }

        /// <summary>
        /// 10-28 부스 루트가 delta만큼 옮겨졌을 때 월드 공간 상품·헤드 연출 좌표를 같은 델타로(재정렬·다가오기 공통). 헤드·캐리지·콜라이더는 루트 자식이라 자동
        /// </summary>
        int CarryWorld(Matrix4x4 delta)
        {
            int moved = basket != null ? basket.TransformActiveItems(delta) : 0;
            if (head != null) head.ApplyWorldDelta(delta);
            Physics.SyncTransforms();
            return moved;
        }

        void OnApproachMoved(Matrix4x4 delta) => CarryWorld(delta);

#if UNITY_EDITOR
        public void EditorSkipReveal()
        {
            if (Current == State.Reveal && reveal != null) reveal.EditorFinish();
            if (approach != null) approach.Finish(); // 10-28 다가오기도 바로 도착(같은 프레임에 Guide)
        }
#endif

        void GoGuide()
        {
            SetState(State.Guide);
            Debug.Log(MachineShown ? "[Cycle] GUIDE — [START] (기계 등장 완료, Beam Pro 가로 안내)" : "[Cycle] GUIDE — Beam Pro 가로 전환 안내");
        }

        /// <summary>Guide에서 잡기(확인) → 재정렬·기계 등장·아이템 배치·카운트다운 → Playing</summary>
        public void ConfirmGuide()
        {
            if (Current != State.Guide) return;
            StartCoroutine(BeginRoutine());
        }

        IEnumerator BeginRoutine()
        {
            bool revealed = MachineShown;              // 10-13: 디졸브로 이미 드러났으면 정렬·등장·배치 없이 3·2·1만
            SetState(State.Countdown);
            if (!revealed)
            {
                if (booth != null) booth.Align();          // 현재 시선 yaw로 정면 재정렬
                if (machine != null) machine.Show(true);   // 아래에서 슬라이드 인
                yield return new WaitForSeconds(machine != null ? machine.SlideDuration : 0f);
                if (head != null) head.ResetHead();
                PlaceItems();                              // 이미 정착한 모습으로 등장
                if (carriage != null) carriage.SetLocalXZ(new Vector2(0f, ClawLayout.CenterZ));
            }
            for (int n = countdownFrom; n > 0; n--)
            {
                EventUtil.SafeInvoke(OnCountdown, n);
                yield return new WaitForSeconds(CountdownStepSec);
            }
            EventUtil.SafeInvoke(OnCountdown, 0);
            playStart = Time.time;
            SetState(State.Playing);
            OnPlayingStarted();
        }

        protected virtual void Update()
        {
            // 종료/취소 다이얼로그가 열려 있으면 입력을 다이얼로그가 전부 소비(타이머·잡기 정지, 재정렬 요청은 버림)
            if (ExitDialogOpen) { ClawPadInput.TryConsumeRecenter(); TickExitDialog(); return; }
            // 카운트다운·디졸브 전환 중엔 재정렬·종료 다이얼로그 없음(전환 돔이 다이얼로그를 덮는다) — 누름은 소비만
            bool transit = Current == State.Countdown || Current == State.Reveal;
            if (ClawPadInput.TryConsumeRecenter() && !transit) Recenter();          // 홈 길게(4차)
            if (ClawPadInput.TryConsumeHome() && !transit) { OpenExitDialog(); return; }

            switch (Current)
            {
                case State.Ready:
                    if (ClawPadInput.TryConsumeGrab()) RequestStart(); // 빔 미사용 — [공간 열기]로 시작
                    break;

                case State.Reveal:
                    ClawPadInput.TryConsumeGrab();
                    break;

                case State.Guide:
                    if (ClawPadInput.TryConsumeGrab()) ConfirmGuide();
                    break;

                case State.Playing:
                    if (paused) { ClawPadInput.TryConsumeGrab(); return; }
                    TickPlaying();
                    break;

                case State.Result:
                    // 보상 대상 = [보상 확인], 아니면(시간 초과) [다시 시작]
                    if (ClawPadInput.TryConsumeGrab())
                    {
                        if (RewardEligible) ConfirmReward();
                        else Restart();
                    }
                    break;

                case State.Reward:
                    // 경품 요청이 '로그인 없음'·통신 실패로 끝났으면 진행자가 고친 뒤 [잡기]로 다시 요청, 그 외엔 [다시 시작](요청 중이면 누름만 소비)
                    if (ClawPadInput.TryConsumeGrab())
                    {
                        if (Prize != null && Prize.CanRetry) Prize.RetryPrize();
                        else Restart();
                    }
                    break;

                default:
                    ClawPadInput.TryConsumeGrab(); // 래치 누적 방지
                    break;
            }
        }

        /// <summary>게임 종료 공통 처리 — 들고 있던 공은 바스켓에 떨어뜨리고 경과 시간 기록. 이후 파생이 결과를 정하고 SetState(Result)</summary>
        protected void StopPlayForResult()
        {
            if (head != null) { head.GuideEnabled = false; head.Abort(); }
            lastElapsed = Time.time - playStart;
        }

        /// <summary>결과 패널 [보상 확인] 또는 잡기 버튼 → Reward. 보상 대상이 아니면 무시.</summary>
        public void ConfirmReward()
        {
            if (Current != State.Result || !RewardEligible) return;
            try { Reward.Issue(RewardScore, lastElapsed); }
            catch (Exception e) { Debug.LogException(e); }
            Prize?.RequestPrize(PrizeNftId); // EVT-002 — 성공해 보상 대상일 때만 여기까지 온다(nftId = 10-28부터 키링 prizeNftIdKeyring)
            SetState(State.Reward);
            Debug.Log("[Cycle] REWARD 안내 표시");
        }

        /// <summary>운영자 리셋 — 어느 상태에서든 Ready. config 재로드, 게임 숫자 초기화.</summary>
        public void ResetGame()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            StopAllCoroutines();
            paused = false; resuming = false; finishing = false;
            restarting = false;
            RewardEligible = false;

            if (head != null) { head.GuideEnabled = false; head.ResetHead(); }
            if (basket != null) basket.FreezeForTransit(false); // 10-28 다가오는 중 리셋 — 상품 고정 풀고(풀로 돌아가기 전에)
            if (approach != null) approach.Stop();               //       다가오기 멈춤(루트 제자리)
            if (pool != null) pool.ReturnAll();
            if (basket != null) basket.ResetPile(); // 10-20 받침 더미 조각 전부 복원(승격·배출·폭발로 숨긴 것)
            Prize?.ResetParticipant();
            ApplyConfig(ConfigLoader.Load());
            OnResetGame();
            if (carriage != null) carriage.SetLocalXZ(new Vector2(0f, ClawLayout.CenterZ));
            if (shake != null) shake.StopAndRestore();
            if (booth != null) booth.SetViewTilt(0f, 0.5f); // 11-10 Ready(시작 패널) — 시점 기울기 끔(0.5초)
            if (reveal != null) reveal.Stop();
            if (machine != null) machine.Hide();
            suppressor?.SetSuppressed(true); // 빔·컨트롤러 모델은 세션 내내 숨김(빔 미사용, 3차)
            exitDialog?.Close();
            if (carriage != null) carriage.InputBlocked = false;
            ClawPadInput.Clear();
            CollectVfxPool.Instance?.StopAll();
            SetState(State.Ready);
            EventUtil.SafeInvoke(OnGameReset);
            EventUtil.SafeInvoke(OnCountsChanged);
            sw.Stop();
            LastResetMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[Reset] {LastResetMs:F0}ms (목표 ≤1000)");
            Debug.Log($"[Cycle] RESET → Ready ({ResetSummary})");
        }

        // ─ IGameSession ─
        public void OnBoothAligned()
        {
            // 10-28 기계가 다가오는 중이면 제자리만 새 정렬 자세로(루트는 BoothApproach가 진행률 자세로 되돌리며 상품도 같이)
            if (approach != null && approach.Active)
            {
                if (booth != null && booth.TryGetAlignPose(out var p, out var r)) approach.Retarget(p, r);
                return;
            }
            // 부팅 시 첫 정렬: 기계가 숨겨져 있어 할 일 없음. 기계가 보이는 중이면 아이템만 재배치
            if (ItemsShown) RePlaceActive();
        }

        public void OnRecentered()
        {
            if (ItemsShown) RePlaceActive();
        }

        /// <summary>공이 기계 안에 보이는 상태(디졸브 뒤 [START] 대기 포함)</summary>
        bool ItemsShown => Current == State.Countdown || Current == State.Playing || (Current == State.Guide && MachineShown);

        /// <summary>SDK 리센터 등으로 부스가 통째로 옮겨졌을 때 — 파일만 다시 쌓는다. 흡착 중인 공은 헤드를 따라가므로 그대로 둔다(라운드 유지).</summary>
        void RePlaceActive()
        {
            if (pool == null || basket == null) return;
            basket.ReturnRealTop(); // 10-22 진짜 맨 윗층은 다시 만든다
            basket.ResetPile(); // 10-20 승격본은 받침으로 되돌리고(풀로) 실물만 다시 쌓는다
            basket.SpawnRealTopLayer();
            scratch.Clear();
            foreach (var it in pool.All) if (it.gameObject.activeSelf && !it.Held && !it.TopLayer) scratch.Add(it);
            basket.PlaceAndSettle(scratch);
        }

        // ─ 일시정지 / 복귀 (GameManager와 동일 정책) ─
        void OnApplicationPause(bool pause) => SetBackground(pause);
        void OnApplicationFocus(bool focus) => SetBackground(!focus);

        void SetBackground(bool background)
        {
            if (background)
            {
                ClawPadInput.Clear();
                if (Current == State.Playing && !paused)
                {
                    paused = true;
                    OnPlayPaused();
                    EventUtil.SafeInvoke(OnPauseChanged, true);
                }
                else if (Current == State.Countdown)
                {
                    StopAllCoroutines();
                    resuming = true;
                }
            }
            else
            {
                if (paused)
                {
                    paused = false;
                    OnPlayResumed();
                    EventUtil.SafeInvoke(OnPauseChanged, false);
                }
                if (resuming)
                {
                    resuming = false;
                    SetState(State.Guide);
                    ConfirmGuide(); // 재정렬부터 다시
                }
            }
        }

        void ApplyConfig(GameConfig cfg)
        {
            revealEnabled = cfg.revealEnabled;
            Config = cfg;
            if (meshLibrary == null)
                cfg.warnings.Add("meshLibrary 미배선 — 보상 표시 메시 없음 (인형뽑기 씬 셋업 메뉴 재실행)");
            Rewards = new RewardTable(cfg, meshLibrary);
            Prize?.Configure(cfg);
            countdownFrom = cfg.countdownSeconds;
            approachDistance = cfg.clawApproachDistance; // 10-28 기계 다가오기
            approachSeconds = cfg.clawApproachSeconds;
            approachPower = cfg.clawApproachPower;
            viewTiltDeg = cfg.clawViewTiltDeg;            // 11-10 시점 기울기

            ApplyGameConfig(cfg);


            // Beam Pro 오버레이 입력은 두 게임 공통(같은 기기·같은 들기 방식)
            ClawPadInput.Relative = cfg.clawStickRelative;
            if (!ClawPadInput.TryParseRotation(cfg.clawPadRotation, out var rot))
                cfg.warnings.Add($"clawPadRotation={cfg.clawPadRotation} 인식 불가 → left 적용");
            ClawPadInput.Rotation = rot;
        }

        // ─ 정면 재정렬 (4차, 홈 길게 누르기) ─
        /// <summary>
        /// BoothRoot를 현재 시선 yaw로 재정렬하고, 월드 공간에 있는 아이템들을 같은 델타로 이동시켜 파일 형태를 유지한다
        /// (재배치·셔플 없음). 헤드·캐리지·콜라이더는 BoothRoot 자식이라 자동. 상태는 유지(Playing 중에도 허용).
        /// </summary>
        public void Recenter()
        {
            if (booth == null) return;
            int moved = 0;
            if (approach != null && approach.Active)
            {
                // 10-28 기계가 다가오는 중: 제자리만 새 정면으로 — 루트·상품은 BoothApproach가 지금 진행률 자세로 옮긴다
                if (booth.TryGetAlignPose(out var p, out var r)) approach.Retarget(p, r);
            }
            else
            {
                var root = booth.Root;
                var old = root.localToWorldMatrix;
                booth.Align();
                moved = CarryWorld(root.localToWorldMatrix * old.inverse);
            }
            RecenterCount++;
            ClawHaptics.Pulse(0.5f, 0.15f);
            EventUtil.SafeInvoke(OnRealigned);
            Debug.Log($"[Recenter] 홈 길게 누름 → 정면 재정렬 (상태 {StateName} 유지, 아이템 {moved}개 이동{(approach != null && approach.Active ? " · 다가오는 중 — 제자리만 갱신" : "")})");
        }

        // ─ 종료/취소 다이얼로그 (3차, 빔 미사용) ─
        void OpenExitDialog()
        {
            if (exitDialog == null) return;
            ClawPadInput.Clear();
            if (carriage != null) carriage.InputBlocked = true;
            exitDialog.Open();
            OnExitDialogOpened();
            if (Current == State.Playing && !paused) EventUtil.SafeInvoke(OnPauseChanged, true); // 스코어보드 점멸 정지
            EventUtil.SafeInvoke(OnExitDialogChanged, true);
            Debug.Log("[Claw] 종료 다이얼로그 열림");
        }

        void TickExitDialog()
        {
            var r = exitDialog.Tick(ClawPadInput.MachineStick.x, ClawPadInput.TryConsumeGrab(), ClawPadInput.TryConsumeHome());
            if (r == ClawExitDialog.Result.None) return;

            if (carriage != null) carriage.InputBlocked = false;
            ClawPadInput.Clear();
            OnExitDialogClosed();
            if (Current == State.Playing && !paused) EventUtil.SafeInvoke(OnPauseChanged, false);
            EventUtil.SafeInvoke(OnExitDialogChanged, false);

            if (r == ClawExitDialog.Result.Quit)
            {
                Debug.Log("[Cycle] QUIT — 종료 선택");
                try { QuitHandler?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); Application.Quit(); }
            }
            else Debug.Log("[Claw] 종료 다이얼로그 취소");
        }

        static void QuitLikeSdk()
        {
#if UNITY_EDITOR
            Debug.Log("[Claw] (에디터) 종료 → 재생 중지");
            UnityEditor.EditorApplication.isPlaying = false;
#else
            try { Unity.XR.XREAL.XREALPlugin.QuitApplication(); }   // SDK 홈 메뉴의 Exit와 동일(로더 해제 → ForceKill/Quit)
            catch (Exception e) { Debug.LogException(e); Application.Quit(); }
#endif
        }

        // ─ 파생용 발행 헬퍼 ─
        protected void SetState(State next)
        {
            if (next == State.Result || next == State.Reward) endShownAt = Time.unscaledTime; // [다시 시작] 연타 막이 기준
            Current = next;
            EventUtil.SafeInvoke(OnStateChanged, next);
        }

        protected void RaiseTimeTick(float remain) => EventUtil.SafeInvoke(OnTimeTick, remain);
        protected void RaiseCountsChanged() => EventUtil.SafeInvoke(OnCountsChanged);
        protected void RaisePadLabelsChanged() => EventUtil.SafeInvoke(OnPadLabelsChanged);
    }
}
