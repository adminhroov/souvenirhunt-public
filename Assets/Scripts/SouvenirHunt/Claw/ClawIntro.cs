using System;
using System.Text;
using TMPro;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 인증 씬(ClawIntro, 부팅 씬) — 10-15 미션형 복원으로 모드 카드(ClawModeSelect)를 없애고 인증만 남겼다.
    /// 체험자마다 맨 처음 MR VIP 인증 연출(VipIntro: [시연 시작](10-26, 카드키는 vipCardStep) → 홍채·시선 → ACCESS GRANTED, 참가자 확인·직전 참가자 경고)을 돌고,
    /// 끝나면 바로 게임 씬(ClawSceneRouter.LoadGame). config vipIntroEnabled=false면 인증 없이 바로 게임.
    /// 운영자 패널이 붙도록 IGameSession을 구현한다(리셋 = 인증부터 다시). 입력은 VipIntro가 받는다.
    /// </summary>
    public class ClawIntro : MonoBehaviour, IGameSession
    {
        public static ClawIntro Instance { get; private set; }

        [SerializeField] VipIntro intro;
        [SerializeField] Transform followRoot;      // 상태 문구 루트(눈 원점, 시선 yaw 추종)
        [SerializeField] TMP_Text statusText;       // 불러오는 중 / 경고
        [SerializeField] InteractorSuppressor suppressor;
        [SerializeField] float followDamping = 5f;

        public VipIntro Intro => intro;
        /// <summary>VIP 인증 연출이 도는 중</summary>
        public bool InIntro => intro != null && intro.Running;
        /// <summary>게임 씬 로드를 요청함</summary>
        public bool Launched { get; private set; }

        // ─ IGameSession ─
        public GameConfig Config { get; private set; }
        public string StateName => InIntro ? $"VipIntro/{intro.Current}" : Launched ? "Loading/Claw" : "Intro";
        public float LastResetMs { get; private set; }
        public event Action OnGameReset;

        string lastStatus;
        Transform cam;
        bool padBound, snapped;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GameSession.Register(this);
#if UNITY_ANDROID && !UNITY_EDITOR
            Screen.orientation = ScreenOrientation.Portrait; // 오버레이 좌표 기준(게임 씬과 동일)
#endif
        }

        void OnDestroy()
        {
            ClawPadInput.Resumed -= OnPadResumed;
            if (Instance == this) Instance = null;
            GameSession.Unregister(this);
        }

        void OnEnable() { ClawPadInput.Resumed += OnPadResumed; Loc.Changed += OnPadResumed; } // 11-10 언어가 바뀌면 패드 라벨 다시
        void OnDisable() { ClawPadInput.Resumed -= OnPadResumed; Loc.Changed -= OnPadResumed; }

        /// <summary>10-26 앱이 돌아옴 — 패드를 다시 입힌다(인증 중이면 VipIntro, 게임 불러오는 중이면 여기)</summary>
        void OnPadResumed()
        {
            if (InIntro) intro.ApplyPad();
            else if (Launched) ApplyLoadingPad();
        }

        void Start() => ResetGame();

        /// <summary>운영자 리셋 — config 재로드, 인증부터 다시. 인증을 끄면 바로 게임.</summary>
        public void ResetGame()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Config = ConfigLoader.Load();
            ClawPadInput.Relative = Config.clawStickRelative;
            if (!ClawPadInput.TryParseRotation(Config.clawPadRotation, out var rot))
                Config.warnings.Add($"clawPadRotation={Config.clawPadRotation} 인식 불가 → left 적용");
            ClawPadInput.Rotation = rot;
            if (!ClawSceneRouter.InBuild(ClawSceneRouter.GameScenePath))
                Config.warnings.Add("인형뽑기 게임 씬(Claw)이 빌드에 없음 — 인증 뒤 이동 안 함 (메뉴 XREAL/30)");

            Launched = false;
            suppressor?.SetSuppressed(true); // 빔·컨트롤러 모델 숨김(빔 미사용)
            ClawPadInput.Clear();
            SetStatus("");
            bool useIntro = intro != null && Config.vipIntroEnabled;
            sw.Stop();
            LastResetMs = (float)sw.Elapsed.TotalMilliseconds;
            EventUtil.SafeInvoke(OnGameReset);
            if (useIntro) intro.Begin(Config, Launch); // 인증이 끝나면 게임으로
            else
            {
                if (intro != null) intro.Stop();
                Launch();
            }
        }

        /// <summary>인증 끝(또는 인증 꺼짐) → 게임 씬. 빌드에 없으면 안내만 하고 머문다.</summary>
        void Launch()
        {
            if (Launched) return;
            ClawPadInput.Clear();
            if (ClawSceneRouter.LoadGame())
            {
                Launched = true;
                ClawHaptics.Pulse(0.5f, 0.12f);
                SetStatus(Loc.T("인형뽑기 불러오는 중…", "Loading the claw game…"));
                Follow(1f);
                ApplyLoadingPad();
            }
            else SetStatus(Loc.T("인형뽑기 게임이 아직 준비되지 않았습니다", "The claw game is not ready yet"));
            Debug.Log($"[Cycle] INTRO DONE → {(Launched ? "게임 씬" : "게임 씬 없음 — 머묾")}");
        }

        void Update()
        {
            // 오버레이(운영자 패널 프리팹)가 Start 뒤에 등록되면 그때 패드 상태를 다시 입힌다
            if (!padBound && ClawPadInput.Root != null)
            {
                padBound = true;
                if (InIntro) intro.ApplyPad(); else if (Launched) ApplyLoadingPad();
            }
            if (!snapped) snapped = Follow(1f);
            if (InIntro) return; // 입력은 VipIntro가 받는다
            ClawPadInput.TryConsumeGrab();
            ClawPadInput.TryConsumeHome();
            ClawPadInput.TryConsumeRecenter();
        }

        static void ApplyLoadingPad()
        {
            ClawPadInput.SetRootActive(true);
            ClawPadInput.GrabEnabled = false;
            ClawPadInput.SetGrabLabel(Loc.T("불러오는 중", "Loading"));
            ClawPadInput.SetStickLabel("");
        }

        void LateUpdate()
        {
            if (!InIntro) Follow(1f - Mathf.Exp(-followDamping * Time.deltaTime));
        }

        /// <summary>문구 루트를 눈 위치·시선 수평 yaw 쪽으로 k만큼(k=1 스냅). 카메라가 없으면 false(다음 프레임 재시도).</summary>
        bool Follow(float k)
        {
            if (followRoot == null) return true;
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam == null) return false;
            var fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return false;
            var targetRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            followRoot.SetPositionAndRotation(Vector3.Lerp(followRoot.position, cam.position, k), Quaternion.Slerp(followRoot.rotation, targetRot, k));
            return true;
        }

        void SetStatus(string s)
        {
            if (statusText == null || s == lastStatus) return;
            lastStatus = s;
            Loc.Fit(statusText);
            statusText.text = s;
            statusText.gameObject.SetActive(s.Length > 0);
        }

        public void OnBoothAligned() { }
        public void OnRecentered()
        {
            if (InIntro) intro.Recenter();
            else Follow(1f);
        }

        public void AppendOperatorSummary(StringBuilder sb)
        {
            if (Config == null) return;
            sb.AppendLine("화면: VIP 인증(ClawIntro) → 인형뽑기 미션형");
            intro?.AppendSummary(sb);
            ClawWearWatch.AppendSummary(sb);
            sb.AppendLine($"게임 씬: {(ClawSceneRouter.InBuild(ClawSceneRouter.GameScenePath) ? "준비됨" : "빌드에 없음")}");
        }
    }
}
