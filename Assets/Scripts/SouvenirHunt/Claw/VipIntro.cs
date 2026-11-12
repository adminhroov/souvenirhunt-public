using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// MR VIP 인증 연출(10-12, 기획 MR_VIP_인증연출_시각화_개발기획서.pptx) — 체험자마다 맨 처음(ClawIntro 씬, 게임 전 — 10-15 모드 카드 삭제).
    ///   WaitDock(가슴 앞 인증 패널) → [인증](잡기) 또는 Beam Pro를 수평으로 vipPoseHoldSeconds 유지 → Linking(스캔 linkSeconds) → CardOk
    ///   → Gaze(정면 타깃을 vipGazeSeconds 연속 응시, 벗어나면 0부터) → Granted(ACCESS GRANTED) → Done(게임 씬으로).
    /// 경품 연동이 켜져 있으면 CardOk 시작 때 참가자 조회(EVT-001) → 승인 화면에 DID 끝자리. 로그인 없음·조회 실패·직전 체험자와 같은 참가자면
    ///   Warning: [잡기] 1회 = 다시 확인(재조회), 그래도 경고면 [잡기] = 그대로 진행. 게임은 막지 않는다(외부 입력 실패에도 앱은 진행).
    /// 시선 판정은 카메라 정면과 타깃 방향의 각도(실제 안구 인식 아님). 타깃·승인 화면은 단계 시작 때 한 번 놓고 월드 고정(head-lock 금지),
    /// 패드·안내는 시선 yaw를 감쇠 추종(ClawGuidePanel과 같은 방식). 연출은 Scale·Rotation·Alpha만, 소리는 config audio=true일 때만.
    /// 거치 감지: Beam Pro는 3DoF(회전만)라 가상 패드 위 위치를 모른다 → [잡기] 또는 자세(화면이 위·아래로 수평 + 정지) 감지.
    /// 이미지·소리는 고정 파일명 에셋(메뉴 34가 없을 때만 임시본 생성) — 파일만 바꾸면 코드 수정 없이 교체된다.
    /// 10-13 연출안(3단계 인증 시나리오 업데이트): STEP 01 카드키 인증 = 가슴 앞 카드 스캐너 패널(3D 모델, 화면 위 오버레이):
    ///   대기(SCAN YOUR CARD) → 인식 중(스캔 라인·진행 바 linkSeconds 2초) → CardOk(초록 체크 ACCESS GRANTED cardOkSeconds).
    ///   STEP 02 홍채·시선 인증 = 정면 EYE SCAN 패널(홍채 그래픽 가운데를 응시, 진행 %·파형) → Granted(초록 ACCESS GRANTED — VIP 배지 대신).
    /// 10-26(사용자 "카드 인식은 제거하고 안구 인식만 — 롤백할 수 있으니 삭제는 말고"): config vipCardStep(기본 false)이면 카드키 단계(WaitDock·Linking·CardOk)를
    ///   건너뛰고 Begin에서 바로 Gaze. 카드키 코드·패널·씬 오브젝트는 그대로(true로 되돌림). 참가자 조회(EVT-001)는 예전엔 카드키 인식 끝(체험자가 움직인 뒤)에
    ///   시작했으므로, 생략 때는 체험자가 홍채를 처음 바라보는 순간 시작(리셋 직후 조회하면 진행자가 다음 참가자로 바꾸기 전이라 경고가 잦다).
    /// 10-26 착용 감지(사용자 "착용하고 있을 때만 안구 인식"): 안경이 벗었다고 알린 동안(ClawWearWatch.AllowEyeScan=false) 홍채·시선 판정을 멈추고
    ///   "안경을 쓰면 시작" 안내, 다시 쓰면 그때 보는 정면에 타깃을 다시 놓고 처음부터.
    /// 10-26(작업 리스트 "Beam Pro 시연 시작 버튼 — 글래스 착용 후 시연 시작 버튼을 눌러야 게임 시작"): config vipStartButton(기본 true)이면 Begin에서
    ///   StartWait — EYE SCAN 패널을 READY로 띄워 시선 앞에 따라오게 두고 Beam Pro [시연 시작]을 기다린다. 누르면 그때 보는 정면에 타깃을 놓고 Gaze.
    ///   안경 센서가 벗었다고 알린 동안엔 첫 누름은 안내만("안경을 썼다면 한 번 더"), 몇 초 안에 한 번 더 누르면 쓴 것으로 보고 시작(센서가 늦을 때).
    ///   처음 단계(AtStart) = StartWait — 30초 자동 처음으로·[다시 시작]·운영자 리셋이 모두 여기로 온다.
    /// </summary>
    public class VipIntro : MonoBehaviour
    {
        public enum Phase { Idle, WaitDock, Linking, CardOk, Gaze, Granted, Warning, Done, StartWait }

        [Header("루트")]
        [SerializeField] Transform followRoot;      // 패드·안내(눈 원점, 시선 yaw 감쇠 추종)
        [SerializeField] GameObject dockGroup;      // 패드 + 안내 문구
        [SerializeField] Transform gazeRoot;        // 시선 타깃·승인 화면(단계 시작 때 배치 후 월드 고정)
        [SerializeField] GameObject gazeGroup;
        [SerializeField] GameObject grantedGroup;

        [Header("STEP 01 카드키 — 스캐너 패널(WaitDock·Linking·CardOk)")]
        [SerializeField] Transform scannerModel;    // 카드 스캐너 3D 모델(텍스처에 SCAN YOUR CARD)
        [SerializeField] Image windowGlow;          // 가운데 창 테두리 빛(대기 중 맥동)
        [SerializeField] RectTransform padRing;     // 카드 아이콘 둘레 회전 링
        [SerializeField] Image scanRing;            // 인식 중 확장 링
        [SerializeField] Image scanLine;            // 인식 중 위아래로 훑는 선
        [SerializeField] Image scanBar;             // 인식 진행 바(Filled Horizontal)
        [SerializeField] Image cardCheck;           // 완료 체크(초록)
        [SerializeField] GameObject cardCheckGroup; // 체크 + 뒤 어두운 원(텍스처의 카드 아이콘을 가림 — OST에서 어두움 = 비움)
        [SerializeField] TMP_Text cardStatus;       // SCANNING n% / ACCESS GRANTED
        [SerializeField] TMP_Text dockTitle;
        [SerializeField] TMP_Text dockHint;

        [Header("STEP 02 홍채·시선 — EYE SCAN(Gaze)")]
        [SerializeField] Image gazeTarget;          // 홍채(가운데 = 시선 판정 중심)
        [SerializeField] Image progressRing;        // Filled Radial360
        [SerializeField] Image gazeFrame;
        [SerializeField] Image eyeIcon;
        [SerializeField] RectTransform[] waveBars;  // 시선 추적 파형
        [SerializeField] TMP_Text gazePercent;
        [SerializeField] TMP_Text gazeHint;

        [Header("승인(Granted·Warning) — ACCESS GRANTED")]
        [SerializeField] RectTransform badge;       // 초록 체크 원
        [SerializeField] Image burstRing;
        [SerializeField] Image grantedFrame;
        [SerializeField] TMP_Text grantedTitle;
        [SerializeField] TMP_Text participantText;
        [SerializeField] TMP_Text warningText;

        [Header("색")]
        [SerializeField] Color cyan = new Color(0.35f, 0.9f, 1f, 1f);
        [SerializeField] Color grantColor = new Color(0.35f, 1f, 0.6f, 1f);
        [SerializeField] Color warnColor = new Color(1f, 0.55f, 0.35f, 1f);

        [Header("시간(초)")]
        [SerializeField] float linkSeconds = 2f;    // 연출안: 카드키 인증 2초
        [SerializeField] float cardOkSeconds = 0.9f;
        [SerializeField] float grantedSeconds = 2f;
        [Tooltip("승인 화면에서 참가자 조회 응답을 더 기다리는 최대 초")]
        [SerializeField] float participantWaitSeconds = 3f;
        [SerializeField] float followDamping = 5f;

        [Header("시선 타깃 배치(눈 원점 기준)")]
        [SerializeField] float gazeDistance = 1.4f;
        [SerializeField] float gazePitchDeg = -5f;

        [Header("소리(config audio=true일 때)")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip linkClip;
        [SerializeField] AudioClip scanSuccessClip;
        [SerializeField] AudioClip vipSuccessClip;

        [SerializeField] PrizeLink prize;

        const float GazeExitMarginDeg = 2f;     // 보는 중 → 벗어남 판정 여유(경계 떨림 방지)
        const float StartConfirmSeconds = 5f;   // 안경 벗음 상태에서 [시연 시작] 첫 누름 뒤 '한 번 더'를 받는 초
        const float PoseStillG = 0.15f;         // 자세 감지: 이보다 크게 움직이는 중이면 '정지' 아님
        const float GravityTau = 0.2f;

        /// <summary>직전 체험자의 참가자 DID — 씬이 바뀌어도 유지(앱을 다시 켜면 비어 있음)</summary>
        public static string PreviousDid { get; set; } = "";

        public Phase Current { get; private set; }
        public bool Running => Current != Phase.Idle && Current != Phase.Done;
        /// <summary>시선 진행률 0~1</summary>
        public float GazeProgress { get; private set; }
        /// <summary>수평 자세 유지 초</summary>
        public float PoseHeld { get; private set; }
        /// <summary>Linking을 시작시킨 입력("버튼" / "자세")</summary>
        public string LinkTrigger { get; private set; } = "";
        public string WarningReason { get; private set; } = "";
        /// <summary>경고 화면에서 [다시 확인]을 이미 했는가 — 다음 [잡기]는 그대로 진행</summary>
        public bool WarningRechecked { get; private set; }
        public PrizeLink Prize => prize;
        /// <summary>STEP 01 패널에 카드 스캐너 모델이 배선돼 있고 보이는 렌더러가 있음(셀프테스트)</summary>
        public bool ScannerReady => scannerModel != null && scannerModel.GetComponentInChildren<Renderer>(true) != null;
        /// <summary>승인 화면 제목(셀프테스트 — ACCESS GRANTED)</summary>
        public string GrantedTitle => grantedTitle != null ? grantedTitle.text : "";
        /// <summary>맨 처음 단계에서 아직 아무 진행이 없음(자동 처음으로가 필요 없는 상태 — ClawWearWatch)</summary>
        public bool AtStart => cfg != null && (cfg.vipCardStep
            ? Current == Phase.WaitDock && PoseHeld <= 0f
            : cfg.vipStartButton
                ? Current == Phase.StartWait
                : Current == Phase.Gaze && GazeProgress <= 0f && !participantStarted);
        /// <summary>안경을 벗어 홍채·시선 판정을 멈춘 중</summary>
        public bool WaitingWear => waitingWear;
        /// <summary>[시연 시작] 대기 중 안경 벗음 상태에서 한 번 눌러 '한 번 더'를 기다리는 중(Beam Pro 안내 — ClawWearWatch)</summary>
        public bool StartConfirmPending => Current == Phase.StartWait && Time.unscaledTime <= startConfirmUntil;
        /// <summary>[시연 시작]이 눌려 홍채·시선으로 넘어간 횟수(셀프테스트)</summary>
        public int StartPresses { get; private set; }
        /// <summary>STEP 01 카드키 패널(스캐너·안내)이 보이는 중 — 셀프테스트(vipCardStep=false면 안 보여야)</summary>
        public bool CardPanelShown => dockGroup != null && dockGroup.activeInHierarchy;
        /// <summary>카드키 단계 완료 표시(초록 체크)가 보이는 중</summary>
        public bool CardCheckShown => cardCheck != null && cardCheck.gameObject.activeInHierarchy && cardCheck.color.a > 0.5f;
        public event Action<Phase> OnPhaseChanged;

#if UNITY_EDITOR
        /// <summary>셀프테스트 — null이면 실제 판정</summary>
        public bool? GazeOverride { get; set; }
        public bool? PoseOverride { get; set; }
#endif

        GameConfig cfg;
        Action onDone;
        Transform cam;
        float phaseTime, gazeSeconds = 2f, gazeAngle = 6f, poseTilt = 25f, poseHold = 0.8f, startConfirmUntil = -1f;
        bool poseDetect, audioOn, gazeHit, needSnap, rechecking, needPlaceGaze, participantStarted, waitingWear;
        string prevDidAtStart = "";

        Accelerometer accel;
        Vector3 gravity;
        bool gravityInit;
        float nextProbe;

        void Awake()
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            ShowGroups(Phase.Idle);
        }

        // 11-10 언어가 바뀌면(실행 앱에서 언어를 바꿔 이어하기·config) 안내·패드 문구 다시
        void OnEnable() => Loc.Changed += OnLangChanged;
        void OnDisable() => Loc.Changed -= OnLangChanged;
        void OnLangChanged()
        {
            ApplyGazeHint();
            ApplyDockText();
            ApplyPad();
        }

        /// <summary>인증 시작(운영자 리셋·씬 진입마다). done = 승인 뒤 게임 씬으로</summary>
        public void Begin(GameConfig config, Action done)
        {
            cfg = config ?? new GameConfig();
            onDone = done;
            poseDetect = cfg.vipPoseDetect;
            poseTilt = cfg.vipPoseTiltDeg;
            poseHold = cfg.vipPoseHoldSeconds;
            gazeSeconds = cfg.vipGazeSeconds;
            gazeAngle = cfg.vipGazeAngleDeg;
            audioOn = cfg.audio;
            if (prize != null)
            {
                prize.ResetParticipant();
                prize.Configure(cfg);
            }
            prevDidAtStart = "";
            WarningReason = "";
            WarningRechecked = false;
            rechecking = false;
            LinkTrigger = "";
            GazeProgress = 0f;
            PoseHeld = 0f;
            gravityInit = false;
            participantStarted = false;
            startConfirmUntil = -1f;
            needSnap = !SnapFollow(1f);
            if (cfg.vipCardStep)
            {
                SetPhase(Phase.WaitDock);
                Debug.Log($"[Cycle] VIP WAIT — 인증 패드 대기 (자세 감지 {(poseDetect ? $"켬 {poseTilt:0}°/{poseHold:0.0}s" : "끔")}, 참가자 확인 {(prize != null && prize.Active ? "켬" : "끔")})");
            }
            else if (cfg.vipStartButton)
            {
                BeginStartWait();
            }
            else
            {
                Debug.Log($"[Cycle] VIP START — 카드키 단계 생략(vipCardStep=false) → 바로 홍채·시선 (참가자 확인 {(prize != null && prize.Active ? "켬 — 처음 바라볼 때" : "끔")})");
                BeginGaze();
            }
        }

        /// <summary>연출을 끄고 숨김(vipIntroEnabled=false)</summary>
        public void Stop()
        {
            if (prize != null) prize.ResetParticipant();
            Current = Phase.Idle;
            ShowGroups(Phase.Idle);
        }

        /// <summary>오버레이가 늦게 붙었을 때·단계가 바뀔 때 라벨 갱신</summary>
        public void ApplyPad()
        {
            if (!Running) return;
            ClawPadInput.SetRootActive(true);
            switch (Current)
            {
                case Phase.StartWait: Pad(Loc.T("시연 시작", "Start Demo"), true); break;
                case Phase.WaitDock: Pad(Loc.T("인증", "Verify"), true); break;
                case Phase.Linking: Pad(Loc.T("인증 중", "Verifying"), false); break;
                case Phase.CardOk: Pad(Loc.T("카드키 확인", "Card Key OK"), false); break;
                case Phase.Gaze:
                    if (waitingWear) Pad(Loc.T("인증 시작", "Start Scan"), true); // 10-26 안경 센서가 안 알려 줄 때 — 누르면 쓴 것으로 보고 시작
                    else Pad(Loc.T("정면 응시", "Look Ahead"), false);
                    break;
                case Phase.Granted: Pad(Loc.T("인증 완료", "Verified"), false); break;
                case Phase.Warning:
                    Pad(rechecking ? Loc.T("확인 중", "Checking") : WarningRechecked ? Loc.T("그대로 진행", "Continue") : Loc.T("다시 확인", "Check Again"), !rechecking);
                    break;
            }
        }

        static void Pad(string grab, bool enabled)
        {
            ClawPadInput.GrabEnabled = enabled;
            ClawPadInput.SetGrabLabel(grab);
            ClawPadInput.SetStickLabel("");
        }

        /// <summary>홈 길게(정면 재정렬) — 패드는 시선 앞으로, 시선 단계면 타깃을 다시 놓고 처음부터</summary>
        public void Recenter()
        {
            if (!Running) return;
            SnapFollow(1f);
            if (Current == Phase.StartWait) needPlaceGaze = !PlaceGaze();
            else if (Current == Phase.Gaze) { needPlaceGaze = !PlaceGaze(); GazeProgress = 0f; }
            else if (Current == Phase.Granted || Current == Phase.Warning) PlaceGaze();
            ClawHaptics.Pulse(0.5f, 0.15f);
        }

        void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            phaseTime += dt;
            if (ClawPadInput.TryConsumeRecenter()) Recenter();
            ClawPadInput.TryConsumeHome(); // 인증 화면엔 종료 다이얼로그 없음(선택 화면과 같음) — 래치만 비움
            bool grab = ClawPadInput.TryConsumeGrab();

            switch (Current)
            {
                case Phase.StartWait:
                    if (needPlaceGaze) needPlaceGaze = !PlaceGaze();
                    if (!grab) break;
                    if (!ClawWearWatch.AllowEyeScan)
                    {
                        // 안경 센서가 벗었다고 알린 상태 — 첫 누름은 안내만, 몇 초 안에 한 번 더 누르면 쓴 것으로(센서가 늦거나 안 알려 줄 때)
                        if (Time.unscaledTime > startConfirmUntil)
                        {
                            startConfirmUntil = Time.unscaledTime + StartConfirmSeconds;
                            ClawHaptics.Pulse(0.3f, 0.06f);
                            Debug.Log("[Vip] [시연 시작] — 안경 벗음 상태라 대기(한 번 더 누르면 시작)");
                            break;
                        }
                        ClawWearWatch.ForceWorn("[시연 시작] 한 번 더");
                    }
                    PressStart();
                    break;

                case Phase.WaitDock:
                    if (grab) { StartLinking("버튼"); break; }
                    if (poseDetect && IsFlatNow(dt)) PoseHeld += dt;
                    else PoseHeld = 0f;
                    if (poseDetect && PoseHeld >= poseHold) StartLinking("자세");
                    break;

                case Phase.Linking:
                    if (phaseTime >= linkSeconds) FinishLinking();
                    break;

                case Phase.CardOk:
                    if (phaseTime >= cardOkSeconds) BeginGaze();
                    break;

                case Phase.Gaze:
                    if (needPlaceGaze)
                    {
                        needPlaceGaze = !PlaceGaze();
                        if (needPlaceGaze) break; // 부팅 직후 카메라가 아직 없음 — 놓일 때까지 판정 안 함
                    }
                    if (!ClawWearWatch.AllowEyeScan)
                    {
                        // 10-26 안경을 벗음 — 판정 멈춤(아무도 안 쓴 안경이 저절로 인증되지 않게). [인증 시작] = 센서가 안 알려 줄 때 쓴 것으로
                        if (!waitingWear) { waitingWear = true; ApplyGazeHint(); ApplyPad(); Debug.Log("[Vip] 안경 벗음 — 홍채·시선 인증 멈춤"); }
                        GazeProgress = 0f;
                        gazeHit = false;
                        if (grab) ClawWearWatch.ForceWorn("[인증 시작] 버튼");
                        break;
                    }
                    if (waitingWear)
                    {
                        // 다시 씀 — 그때 보는 정면에 타깃을 다시 놓고 처음부터
                        waitingWear = false;
                        needPlaceGaze = !PlaceGaze();
                        ApplyGazeHint();
                        ApplyPad();
                        Debug.Log("[Vip] 안경 착용 — 홍채·시선 인증 시작");
                        break;
                    }
                    gazeHit = IsGazeHit();
                    if (gazeHit && !participantStarted) StartParticipantCheck(); // 카드키 생략 때: 체험자가 처음 바라보는 순간
                    GazeProgress = gazeHit ? GazeProgress + dt / Mathf.Max(0.1f, gazeSeconds) : 0f; // 이탈 → 처음부터(기획서)
                    if (GazeProgress >= 1f) { GazeProgress = 1f; StartGranted(); }
                    break;

                case Phase.Granted:
                    bool waitingParticipant = prize != null && prize.Active && prize.Did == PrizeLink.DidState.Fetching &&
                                              phaseTime < grantedSeconds + participantWaitSeconds;
                    if (phaseTime >= grantedSeconds && !waitingParticipant) Evaluate();
                    break;

                case Phase.Warning:
                    if (rechecking)
                    {
                        if (prize == null || prize.Did != PrizeLink.DidState.Fetching || phaseTime >= participantWaitSeconds)
                        {
                            rechecking = false;
                            WarningRechecked = true;
                            WarningReason = ParticipantWarning();
                            if (WarningReason.Length == 0) { Finish("다시 확인 후 정상"); break; }
                            Debug.LogWarning($"[Vip] 다시 확인해도 경고 — {Flat(WarningReason)}");
                            ApplyPad();
                        }
                    }
                    else if (grab)
                    {
                        if (!WarningRechecked) Recheck();
                        else Finish("경고 무시하고 진행");
                    }
                    break;
            }
            Animate();
        }

        void LateUpdate()
        {
            if (!Running) return;
            if (needSnap) { needSnap = !SnapFollow(1f); return; }
            if (InDock)
                SnapFollow(1f - Mathf.Exp(-followDamping * Time.deltaTime));
            else if (Current == Phase.StartWait && !needPlaceGaze)
                FollowGaze(1f - Mathf.Exp(-followDamping * Time.deltaTime)); // 누가 언제 쓸지 모름 — 쓴 사람 정면에 READY 패널이 오게
        }

        bool InDock => Current == Phase.WaitDock || Current == Phase.Linking || Current == Phase.CardOk;

        // ─ 단계 전환 ─────────────────────────────────────────────

        void StartLinking(string trigger)
        {
            LinkTrigger = trigger;
            SetPhase(Phase.Linking);
            Play(linkClip);
            ClawHaptics.Pulse(0.4f, 0.08f);
            Debug.Log($"[Cycle] VIP LINK — {trigger}로 인증 시작");
        }

        /// <summary>STEP 01 인식 끝 → 스캐너에 초록 ACCESS GRANTED. 참가자 조회는 여기서 시작(응시 동안 응답을 받아 둔다)</summary>
        void FinishLinking()
        {
            Play(scanSuccessClip);
            ClawHaptics.Pulse(0.6f, 0.12f);
            StartParticipantCheck();
            SetPhase(Phase.CardOk);
            Debug.Log("[Cycle] VIP CARD OK — 카드키 인증 완료(ACCESS GRANTED)");
        }

        void ApplyGazeHint()
        {
            if (gazeHint == null) return;
            Loc.Fit(gazeHint);
            gazeHint.text = Current == Phase.StartWait
                ? Loc.T("Beam Pro의 [시연 시작] 버튼을 누르면\n홍채 인증을 시작합니다", "Press [Start Demo] on Beam Pro\nto begin the iris scan")
                : waitingWear
                    ? Loc.T("안경을 쓰면 홍채 인증을 시작합니다", "Put on the glasses to begin the iris scan")
                    : (cfg != null && cfg.vipCardStep ? "STEP 02 · " : "") + Loc.T($"가운데 홍채를 {gazeSeconds:0.#}초간 바라보세요", $"Look at the iris in the center for {gazeSeconds:0.#}s");
        }

        /// <summary>10-26 처음 단계 — EYE SCAN 패널을 READY로 띄우고 Beam Pro [시연 시작] 대기(패널은 시선 앞을 따라온다)</summary>
        void BeginStartWait()
        {
            waitingWear = false;
            startConfirmUntil = -1f;
            GazeProgress = 0f;
            gazeHit = false;
            needPlaceGaze = !PlaceGaze();
            SetPhase(Phase.StartWait);
            ApplyGazeHint();
            Debug.Log($"[Cycle] VIP READY — Beam Pro [시연 시작] 대기 (안경 {(ClawWearWatch.AllowEyeScan ? "착용·모름" : "벗음")}, 참가자 확인 {(prize != null && prize.Active ? "켬 — 처음 바라볼 때" : "끔")})");
        }

        /// <summary>[시연 시작] → 그때 보는 정면에 타깃을 놓고 홍채·시선</summary>
        void PressStart()
        {
            StartPresses++;
            startConfirmUntil = -1f;
            ClawHaptics.Pulse(0.5f, 0.1f);
            Play(linkClip);
            Debug.Log("[Cycle] VIP START — [시연 시작] → 홍채·시선");
            BeginGaze();
        }

        /// <summary>참가자 조회(EVT-001) 시작 — 카드키 인식 끝, 또는 카드키 생략 때 처음 바라보는 순간. 경품 연동이 꺼져 있으면 아무 것도 안 함(켜지면 다음 기회에)</summary>
        void StartParticipantCheck()
        {
            if (participantStarted || prize == null || !prize.Active) return;
            participantStarted = true;
            prevDidAtStart = PreviousDid ?? "";                          // 조회 시작 시점의 '직전 체험자'
            prize.BeginParticipant();                                   // EVT-001
        }

        /// <summary>STEP 02 — 정면 EYE SCAN 패널을 시선 앞에 놓고 응시 대기(카메라가 아직 없으면 Update에서 다시)</summary>
        void BeginGaze()
        {
            needPlaceGaze = !PlaceGaze();
            waitingWear = false;
            GazeProgress = 0f;
            gazeHit = false;
            SetPhase(Phase.Gaze);
            ApplyGazeHint();
            Debug.Log("[Cycle] VIP GAZE — 홍채·시선 인증(EYE SCAN) 응시 대기");
        }

        void StartGranted()
        {
            Play(vipSuccessClip);
            ClawHaptics.Pulse(0.8f, 0.2f);
            SetPhase(Phase.Granted);
            Debug.Log("[Cycle] VIP GRANTED — 인증 완료(ACCESS GRANTED)");
        }

        void Evaluate()
        {
            WarningReason = ParticipantWarning();
            if (WarningReason.Length == 0) { Finish("정상"); return; }
            SetPhase(Phase.Warning);
            Debug.LogWarning($"[Vip] 참가자 경고 — {Flat(WarningReason)}");
        }

        void Recheck()
        {
            rechecking = true;
            if (prize != null && prize.Active)
            {
                prize.ResetParticipant();
                prize.BeginParticipant();
            }
            phaseTime = 0f;
            ApplyPad();
            Debug.Log("[Vip] 참가자 다시 확인");
        }

        void Finish(string how)
        {
            if (prize != null && prize.Active && prize.Did == PrizeLink.DidState.Ready) PreviousDid = prize.UserDid;
            SetPhase(Phase.Done);
            ClawPadInput.Clear();
            Debug.Log($"[Cycle] VIP DONE — {how}{(prize != null && prize.Active ? $" (참가자 {(prize.DidTail.Length > 0 ? prize.DidTail : "없음")})" : "")}");
            onDone?.Invoke();
        }

        /// <summary>경고 문구 — 빈 문자열이면 정상. 경품 연동이 꺼져 있으면 참가자 확인을 하지 않는다</summary>
        string ParticipantWarning()
        {
            if (prize == null || !prize.Active) return "";
            switch (prize.Did)
            {
                case PrizeLink.DidState.Ready:
                    return prevDidAtStart.Length > 0 && prize.UserDid == prevDidAtStart
                        ? Loc.T("직전 체험자와 같은 참가자입니다\n진행자 화면에서 [로그아웃] → 다음 참가자 [이벤트 로그인]",
                                "Same participant as the previous player\nHost screen: [Log out] → next participant [Event Login]")
                        : "";
                case PrizeLink.DidState.NoParticipant:
                    return Loc.T("진행자 화면에 로그인된 참가자가 없습니다\n진행자 화면에서 [이벤트 로그인]을 확인하세요",
                                 "No participant is logged in on the host screen\nCheck [Event Login] on the host screen");
                case PrizeLink.DidState.Fetching:
                    return Loc.T("참가자 확인이 늦어지고 있습니다\n와이파이를 확인하세요", "Participant check is taking long\nPlease check the Wi-Fi");
                case PrizeLink.DidState.Failed: // 11-10 실패 사유(DescribeFailure)는 진행요원용 진단이라 번역 안 함
                    return Loc.T("참가자를 확인하지 못했습니다\n", "Could not verify the participant\n") + PrizeLink.DescribeFailure(prize.LastStatus, prize.LastServerMessage);
                default:
                    return "";
            }
        }

        void SetPhase(Phase p)
        {
            Current = p;
            phaseTime = 0f;
            lastPercent = -1;
            ShowGroups(p);
            ApplyPad();
            ApplyDockText();
            EventUtil.SafeInvoke(OnPhaseChanged, p);
        }

        void ApplyDockText()
        {
            Loc.Fit(dockHint);
            if (dockHint != null)
                dockHint.text = Current switch
                {
                    Phase.Linking => Loc.T("카드키 인식 중… 잠시 그대로 두세요", "Reading card key… please hold still"),
                    Phase.CardOk => Loc.T("<color=#5CFF99>ACCESS GRANTED</color>  카드키 인증 완료", "<color=#5CFF99>ACCESS GRANTED</color>  Card key verified"),
                    _ => Loc.T("Beam Pro를 가슴 앞 인증 패널에 올려놓거나\n[인증] 버튼을 누르세요 ↓", "Place Beam Pro on the panel at your chest\nor press [Verify] ↓"),
                };
            var check = cardCheckGroup != null ? cardCheckGroup : cardCheck != null ? cardCheck.gameObject : null;
            if (check != null) check.SetActive(Current == Phase.CardOk);
        }

        void ShowGroups(Phase p)
        {
            bool dock = p == Phase.WaitDock || p == Phase.Linking || p == Phase.CardOk;
            bool gaze = p == Phase.Gaze || p == Phase.StartWait;
            bool granted = p == Phase.Granted || p == Phase.Warning;
            if (dockGroup) dockGroup.SetActive(dock);
            if (gazeGroup) gazeGroup.SetActive(gaze);
            if (grantedGroup) grantedGroup.SetActive(granted);
        }

        // ─ 판정 ──────────────────────────────────────────────────

        bool IsGazeHit()
        {
#if UNITY_EDITOR
            if (GazeOverride.HasValue) return GazeOverride.Value;
#endif
            if (!EnsureCam() || gazeRoot == null) return false;
            var to = gazeRoot.position - cam.position;
            if (to.sqrMagnitude < 1e-6f) return false;
            float ang = Vector3.Angle(cam.forward, to);
            return ang <= (gazeHit ? gazeAngle + GazeExitMarginDeg : gazeAngle);
        }

        /// <summary>Beam Pro가 수평(화면 위·아래 무관)이고 거의 정지해 있는가. 센서가 없으면 에디터에서 V 키</summary>
        bool IsFlatNow(float dt)
        {
#if UNITY_EDITOR
            if (PoseOverride.HasValue) return PoseOverride.Value;
            var kb = Keyboard.current;
            if (kb != null && kb.vKey.isPressed) return true;
#endif
            if (!ReadGravity(dt, out var g, out float linearG)) return false;
            return IsFlat(g, poseTilt) && linearG < PoseStillG;
        }

        /// <summary>중력 방향이 기기 화면 축(z)과 tiltDeg 안이면 수평 — 순수 함수(셀프테스트)</summary>
        public static bool IsFlat(Vector3 gravity, float tiltDeg)
        {
            float m = gravity.magnitude;
            if (m < 1e-4f) return false;
            return Mathf.Abs(gravity.z) / m >= Mathf.Cos(tiltDeg * Mathf.Deg2Rad);
        }

        bool ReadGravity(float dt, out Vector3 g, out float linearG)
        {
            g = Vector3.zero;
            linearG = 0f;
            if (accel == null || !accel.added)
            {
                if (Time.unscaledTime < nextProbe) return false;
                nextProbe = Time.unscaledTime + 1f;
                try
                {
                    accel = Accelerometer.current;
                    if (accel == null) return false;
                    if (!accel.enabled) InputSystem.EnableDevice(accel);
                    gravityInit = false;
                }
                catch (Exception e)
                {
                    accel = null;
                    Debug.LogWarning("[Vip] 가속도계 사용 불가 — 자세 감지 없음([인증] 버튼만): " + e.Message);
                    return false;
                }
            }
            Vector3 a = accel.acceleration.ReadValue();
            if (a.sqrMagnitude < 1e-6f) return false;
            if (!gravityInit) { gravity = a; gravityInit = true; }
            gravity = Vector3.Lerp(gravity, a, 1f - Mathf.Exp(-Mathf.Max(dt, 1e-4f) / GravityTau));
            float unit = gravity.magnitude > 3f ? 1f / 9.81f : 1f; // m/s² 또는 g 자동 판별
            g = gravity;
            linearG = (a - gravity).magnitude * unit;
            return true;
        }

        // ─ 배치 ──────────────────────────────────────────────────

        bool EnsureCam()
        {
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            return cam != null;
        }

        /// <summary>패드 루트를 눈 위치·시선 수평 yaw 쪽으로 k만큼(k=1 스냅). 카메라가 없으면 false</summary>
        bool SnapFollow(float k)
        {
            if (followRoot == null) return true;
            if (!EnsureCam()) return false;
            var fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return false;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            followRoot.SetPositionAndRotation(Vector3.Lerp(followRoot.position, cam.position, k), Quaternion.Slerp(followRoot.rotation, rot, k));
            return true;
        }

        /// <summary>시선 타깃·승인 화면을 지금 시선 yaw 정면(gazePitchDeg, gazeDistance)에 한 번 놓는다 — 이후 월드 고정. 카메라가 없으면 false</summary>
        bool PlaceGaze()
        {
            if (gazeRoot == null) return true;
            if (!GazePose(out var pos, out var rot)) return false;
            gazeRoot.SetPositionAndRotation(pos, rot);
            return true;
        }

        /// <summary>10-26 [시연 시작] 대기 중 — 시선 앞 자리로 k만큼(감쇠 추종)</summary>
        void FollowGaze(float k)
        {
            if (gazeRoot == null || !GazePose(out var pos, out var rot)) return;
            gazeRoot.SetPositionAndRotation(Vector3.Lerp(gazeRoot.position, pos, k), Quaternion.Slerp(gazeRoot.rotation, rot, k));
        }

        bool GazePose(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = Quaternion.identity;
            if (!EnsureCam()) return false;
            var fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = followRoot != null ? followRoot.forward : Vector3.forward;
            var yaw = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            var dir = yaw * Quaternion.Euler(-gazePitchDeg, 0f, 0f) * Vector3.forward;
            pos = cam.position + dir * gazeDistance;
            rot = Quaternion.LookRotation(dir, Vector3.up);
            return true;
        }

        // ─ 연출(Scale·Rotation·Alpha) ─────────────────────────────

        int lastPercent = -1;

        void Animate()
        {
            float t = Time.time;
            switch (Current)
            {
                case Phase.StartWait:
                {
                    // READY — 홍채는 천천히 돌며 흐리게 숨 쉬고, 진행 링은 비움(누르기 전엔 판정 없음)
                    float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2f);
                    if (gazeTarget)
                    {
                        gazeTarget.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 6f);
                        gazeTarget.rectTransform.localScale = Vector3.one;
                        gazeTarget.color = WithAlpha(cyan, 0.4f + 0.25f * pulse);
                    }
                    if (progressRing) { progressRing.fillAmount = 0f; progressRing.color = cyan; }
                    if (gazeFrame) gazeFrame.color = WithAlpha(cyan, 0.6f);
                    if (eyeIcon) eyeIcon.color = WithAlpha(cyan, 0.4f + 0.4f * pulse);
                    if (waveBars != null)
                        for (int i = 0; i < waveBars.Length; i++)
                            if (waveBars[i] != null) waveBars[i].localScale = new Vector3(1f, 0.15f + 0.1f * Mathf.Abs(Mathf.Sin(t * 1.5f + i * 0.7f)), 1f);
                    if (gazePercent != null && lastPercent != -2) { lastPercent = -2; gazePercent.text = "READY"; }
                    break;
                }

                case Phase.WaitDock:
                    if (padRing) { padRing.localRotation = Quaternion.Euler(0f, 0f, -t * 30f); padRing.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(t * 2.5f)); }
                    SetColor(padRing, WithAlpha(cyan, 0.8f));
                    if (windowGlow) windowGlow.color = WithAlpha(cyan, 0.35f + 0.55f * (0.5f + 0.5f * Mathf.Sin(t * 2f)));
                    if (scanRing) scanRing.color = WithAlpha(cyan, 0f);
                    if (scanLine) scanLine.color = WithAlpha(cyan, 0f);
                    if (scanBar) { scanBar.fillAmount = 0f; scanBar.color = cyan; }
                    SetStatus("", cyan);
                    break;

                case Phase.Linking:
                {
                    float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.05f, linkSeconds));
                    if (padRing) padRing.localRotation = Quaternion.Euler(0f, 0f, -t * 30f - phaseTime * 240f);
                    SetColor(padRing, cyan);
                    if (windowGlow) windowGlow.color = cyan;
                    if (scanRing)
                    {
                        float r = Mathf.Repeat(phaseTime * 1.2f, 1f); // 반복 확장 링
                        scanRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.5f, r);
                        scanRing.color = WithAlpha(cyan, 1f - r);
                    }
                    if (scanLine)
                    {
                        float h = scanLine.rectTransform.parent is RectTransform pr ? pr.rect.height : 0f;
                        var lp = scanLine.rectTransform.anchoredPosition;
                        lp.y = Mathf.Lerp(h * 0.45f, -h * 0.45f, Mathf.PingPong(phaseTime * 1.6f, 1f)); // 위아래로 훑기
                        scanLine.rectTransform.anchoredPosition = lp;
                        scanLine.color = cyan;
                    }
                    if (scanBar) { scanBar.fillAmount = k; scanBar.color = cyan; }
                    int pc = Mathf.FloorToInt(k * 100f);
                    if (pc != lastPercent) { lastPercent = pc; SetStatus($"SCANNING  {pc}%", cyan); }
                    break;
                }

                case Phase.CardOk:
                {
                    float a = phaseTime;
                    if (cardCheck)
                    {
                        cardCheck.rectTransform.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(a / 0.35f));
                        cardCheck.color = WithAlpha(grantColor, Mathf.Clamp01(a / 0.15f));
                    }
                    SetColor(padRing, WithAlpha(grantColor, 0.9f));
                    if (windowGlow) windowGlow.color = grantColor;
                    if (scanRing)
                    {
                        float k = Mathf.Clamp01(a / 0.6f);
                        scanRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.8f, k);
                        scanRing.color = WithAlpha(grantColor, 1f - k);
                    }
                    if (scanLine) scanLine.color = WithAlpha(cyan, 0f);
                    if (scanBar) { scanBar.fillAmount = 1f; scanBar.color = grantColor; }
                    SetStatus("ACCESS GRANTED", grantColor);
                    break;
                }

                case Phase.Gaze:
                {
                    if (gazeTarget)
                    {
                        gazeTarget.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 12f);
                        gazeTarget.rectTransform.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(t * 4f));
                        gazeTarget.color = WithAlpha(cyan, gazeHit ? 1f : 0.7f);
                    }
                    if (progressRing) { progressRing.fillAmount = GazeProgress; progressRing.color = cyan; }
                    if (gazeFrame) gazeFrame.color = WithAlpha(cyan, 0.85f);
                    if (eyeIcon) eyeIcon.color = WithAlpha(cyan, gazeHit ? 1f : 0.55f + 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 3f)));
                    if (waveBars != null)
                        for (int i = 0; i < waveBars.Length; i++)
                        {
                            if (waveBars[i] == null) continue;
                            float amp = gazeHit ? 1f : 0.3f;
                            float h = 0.2f + 0.8f * amp * Mathf.Abs(Mathf.Sin(t * (5f + i * 0.9f) + i * 1.3f));
                            waveBars[i].localScale = new Vector3(1f, h, 1f);
                        }
                    int pc = Mathf.FloorToInt(GazeProgress * 100f);
                    if (gazePercent != null && pc != lastPercent) { lastPercent = pc; gazePercent.text = $"{pc}%"; }
                    break;
                }

                case Phase.Granted:
                case Phase.Warning:
                {
                    float a = Current == Phase.Granted ? phaseTime : 10f; // 경고 화면은 등장 연출이 끝난 상태로
                    if (burstRing)
                    {
                        float k = Mathf.Clamp01(a / 0.8f);
                        burstRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.5f, 2f, k);
                        burstRing.color = WithAlpha(grantColor, 1f - k);
                    }
                    if (grantedFrame) grantedFrame.color = WithAlpha(Current == Phase.Warning ? warnColor : grantColor, 0.75f);
                    if (badge) badge.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(a / 0.45f));
                    if (grantedTitle) grantedTitle.alpha = Mathf.Clamp01((a - 0.2f) / 0.4f);
                    if (participantText) participantText.text = ParticipantLine();
                    if (warningText)
                    {
                        bool show = Current == Phase.Warning;
                        if (warningText.gameObject.activeSelf != show) warningText.gameObject.SetActive(show);
                        if (show)
                        {
                            warningText.color = warnColor;
                            Loc.Fit(warningText);
                            warningText.text = rechecking ? Loc.T("참가자 다시 확인 중…", "Re-checking participant…") :
                                WarningReason + (WarningRechecked ? Loc.T("\n<size=80%>[잡기] = 그대로 진행</size>", "\n<size=80%>[Grab] = Continue</size>")
                                                                  : Loc.T("\n<size=80%>[잡기] = 다시 확인</size>", "\n<size=80%>[Grab] = Check again</size>"));
                        }
                    }
                    break;
                }
            }
        }

        string ParticipantLine()
        {
            if (prize == null || !prize.Active) return "";
            return prize.Did switch
            {
                PrizeLink.DidState.Ready => Loc.T($"참가자 {prize.DidTail} 확인", $"Participant {prize.DidTail} verified"),
                PrizeLink.DidState.Fetching => Loc.T("참가자 확인 중…", "Checking participant…"),
                _ => "",
            };
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float y = x - 1f;
            return 1f + c3 * y * y * y + c1 * y * y;
        }

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        static void SetColor(RectTransform rt, Color c)
        {
            if (rt != null && rt.TryGetComponent<Graphic>(out var g)) g.color = c;
        }

        string lastStatus;

        void SetStatus(string s, Color c)
        {
            if (cardStatus == null) return;
            cardStatus.color = c;
            if (s == lastStatus) return;
            lastStatus = s;
            cardStatus.text = s;
        }

        static string Flat(string s) => s.Replace('\n', ' ');

        void Play(AudioClip clip)
        {
            if (!audioOn || clip == null || audioSource == null) return;
            audioSource.PlayOneShot(clip);
        }

        /// <summary>운영자 패널 한 줄</summary>
        public void AppendSummary(StringBuilder sb)
        {
            if (cfg == null) return;
            if (!cfg.vipIntroEnabled) { sb.AppendLine("VIP 인증 연출: 꺼짐(vipIntroEnabled=false)"); return; }
            string phase = Current switch
            {
                Phase.StartWait => StartConfirmPending ? "[시연 시작] 대기 — 안경 벗음(한 번 더 누르면 시작)" : "[시연 시작] 대기",
                Phase.WaitDock => "STEP 01 카드키 대기",
                Phase.Linking => "STEP 01 카드키 인식 중",
                Phase.CardOk => "STEP 01 카드키 완료",
                Phase.Gaze => waitingWear ? "홍채·시선 — 안경 벗음(대기)" : $"{(cfg.vipCardStep ? "STEP 02 " : "")}홍채·시선 {GazeProgress * 100f:0}%",
                Phase.Granted => "ACCESS GRANTED",
                Phase.Warning => "경고 — " + Flat(WarningReason),
                Phase.Done => "완료",
                _ => "대기",
            };
            string who = prize != null && prize.Active ? (prize.DidTail.Length > 0 ? $" · 참가자 {prize.DidTail}" : " · 참가자 미확인") : "";
            string prev = PreviousDid.Length > 0 ? $" · 직전 …{(PreviousDid.Length > 6 ? PreviousDid.Substring(PreviousDid.Length - 6) : PreviousDid)}" : "";
            sb.AppendLine($"VIP 인증: {phase}{who}{prev}");
        }
    }
}
