using System.Text;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// SCR-07 운영자 패널 (Phase 3, 인수인계 §4-3). CornerTapReveal이 Toggle()로 연다.
    /// 표시: 적용된 config 값 + 파싱 경고 배지 (§4-2 "먹었는지 확인용"). 리셋은 자식 ResetButton의 OperatorResetButton이 담당 —
    /// 세션.ResetGame()이 config를 재로드하고 OnGameReset을 발화하므로 표시가 자동 갱신된다.
    /// 컨트롤러 프리팹은 런타임 Instantiate(XREALSettings)라 씬 참조 불가 → GameSession.Current를 TryBind 패턴으로 재시도.
    /// 09-03: 뷰 2개 — 세로(Main) / 가로(Claw 세션: Beam Pro를 눕힌 방향으로 콘텐츠 회전, 개수 스테퍼 포함).
    /// 탭 존·카운터도 세션에 맞춰 이동(Main 우상단 / Claw 세로 기준 우하단 = 눕히면 가로 우상단). 회전각은 오버레이와 같은
    /// ClawPadInput.Rotation(config clawPadRotation)에서 온다 — 실기기에서 반대면 config 한 줄로 오버레이·패널·존이 함께 바뀐다.
    /// 패널이 열려 있는 동안엔 전면 배경이 조작 영역을 덮는다(의도 — 운영자 모드). autoHideSeconds 무조작 시 자동 닫힘.
    /// </summary>
    public class OperatorPanelController : MonoBehaviour
    {
        [SerializeField] OperatorPanelView portrait;    // Main(세로)
        [SerializeField] OperatorPanelView landscape;   // Claw(가로, 회전)
        [SerializeField] RectTransform cornerZone;      // CornerTapZone — 세션별 위치
        [SerializeField] RectTransform tapCounter;      // 남은 횟수 표시 — 존 근처로 이동
        [Tooltip("무조작 자동 닫힘(초)")]
        [SerializeField] float autoHideSeconds = 30f;

        IGameSession gm;   // Main=GameManager / Claw=ClawGameManager — GameSession.Current로 씬 무관 바인딩
        bool bound;
        float lastInteraction;
        float statusUntil;
        float nextPerfRefresh;
        OperatorPanelView view;

        public OperatorPanelView ActiveView => view;
        public bool IsOpen => (portrait != null && portrait.IsOpen) || (landscape != null && landscape.IsOpen);

        void Awake()
        {
            foreach (var v in new[] { portrait, landscape })
            {
                if (v == null) continue;
                if (v.CloseButton != null) v.CloseButton.onClick.AddListener(Hide);
                if (v.ResetButton != null) v.ResetButton.onClick.AddListener(OnResetPressed);
                if (v.Counts != null) v.Counts.Init(this);
            }
            view = portrait != null ? portrait : landscape;
        }

        // 세션 Awake와의 순서 비보장 → HudController와 동일하게 재시도 + 세션 교체 알림으로 재바인드
        void OnEnable()
        {
            GameSession.Changed += OnSessionChanged;
            TryBind();
        }

        void Start() => TryBind();

        void OnDisable()
        {
            GameSession.Changed -= OnSessionChanged;
            Unbind();
        }

        void OnSessionChanged(IGameSession _)
        {
            Unbind();
            TryBind();
        }

        void Unbind()
        {
            if (bound && gm != null) gm.OnGameReset -= HandleReset;
            bound = false;
            gm = null;
        }

        void TryBind()
        {
            if (bound) return;
            gm = GameSession.Current;
            if (gm == null) return;
            gm.OnGameReset += HandleReset;
            bound = true;
            ApplyLayout();
            Refresh();
        }

        void HandleReset()
        {
            lastInteraction = Time.unscaledTime; // 리셋 조작도 상호작용으로 취급
            ApplyLayout();                       // config(clawPadRotation) 재로드 반영
            Refresh();
            ShowStatus("✓ 리셋 완료 — 시작 대기(Ready) 상태");
        }

        /// <summary>세션·회전 설정에 따라 뷰 선택, 가로 콘텐츠 회전, 탭 존·카운터 위치 적용.</summary>
        void ApplyLayout()
        {
            var rot = gm is ClawSessionBase || gm is ClawIntro ? ClawPadInput.Rotation : ClawPadInput.PadRotation.None;
            var next = rot == ClawPadInput.PadRotation.None
                ? (portrait != null ? portrait : landscape)
                : (landscape != null ? landscape : portrait);
            if (next != view)
            {
                bool wasOpen = IsOpen;
                if (view != null) view.SetOpen(false);
                view = next;
                if (wasOpen && view != null) view.SetOpen(true);
            }

            float z = rot == ClawPadInput.PadRotation.Left90 ? -90f : rot == ClawPadInput.PadRotation.Right90 ? 90f : 0f;
            if (landscape != null && landscape.Content != null)
                landscape.Content.localRotation = Quaternion.Euler(0f, 0f, z);

            if (cornerZone != null)
            {
                switch (rot)
                {
                    case ClawPadInput.PadRotation.Left90:  cornerZone.anchorMin = new Vector2(0.8f, 0f);    cornerZone.anchorMax = new Vector2(1f, 0.15f); break;
                    case ClawPadInput.PadRotation.Right90: cornerZone.anchorMin = new Vector2(0f, 0.85f);   cornerZone.anchorMax = new Vector2(0.2f, 1f);  break;
                    default:                               cornerZone.anchorMin = new Vector2(0.7f, 0.85f); cornerZone.anchorMax = Vector2.one;             break;
                }
                cornerZone.offsetMin = Vector2.zero;
                cornerZone.offsetMax = Vector2.zero;
            }
            if (tapCounter != null)
            {
                switch (rot)
                {
                    case ClawPadInput.PadRotation.Left90:
                        tapCounter.anchorMin = tapCounter.anchorMax = new Vector2(1f, 0f);
                        tapCounter.anchoredPosition = new Vector2(-100f, 100f);
                        tapCounter.localRotation = Quaternion.Euler(0f, 0f, -90f);
                        break;
                    case ClawPadInput.PadRotation.Right90:
                        tapCounter.anchorMin = tapCounter.anchorMax = new Vector2(0f, 1f);
                        tapCounter.anchoredPosition = new Vector2(100f, -100f);
                        tapCounter.localRotation = Quaternion.Euler(0f, 0f, 90f);
                        break;
                    default:
                        tapCounter.anchorMin = tapCounter.anchorMax = Vector2.one;
                        tapCounter.anchoredPosition = new Vector2(-100f, -100f);
                        tapCounter.localRotation = Quaternion.identity;
                        break;
                }
            }
        }

        // 리셋 버튼 피드백: Ready 상태에서는 리셋해도 눈에 보이는 변화가 없으므로
        // 성공/실패를 무조건 글자로 보여준다 (운영자는 개발자가 아니다).
        void OnResetPressed()
        {
            lastInteraction = Time.unscaledTime;
            if (GameSession.Current == null)
                ShowStatus("리셋 실패 — 게임 세션 없음 (씬 초기화 전)");
            // 성공 메시지는 OnGameReset(HandleReset)이 표시
        }

        public void ShowStatus(string msg)
        {
            var st = view != null ? view.StatusText : null;
            if (st == null) return;
            st.text = msg;
            st.gameObject.SetActive(true);
            statusUntil = Time.unscaledTime + 2.5f;
        }

        /// <summary>스테퍼 등 자식 조작이 자동 닫힘 타이머를 연장할 때.</summary>
        public void MarkInteraction() => lastInteraction = Time.unscaledTime;

        public void Toggle()
        {
            ApplyLayout();
            if (view == null || view.Root == null) return;
            bool open = !view.IsOpen;
            if (portrait != null && portrait != view) portrait.SetOpen(false);
            if (landscape != null && landscape != view) landscape.SetOpen(false);
            view.SetOpen(open);
            if (open)
            {
                TryBind();
                Refresh();
                lastInteraction = Time.unscaledTime;
            }
        }

        public void Hide()
        {
            if (portrait != null) portrait.SetOpen(false);
            if (landscape != null) landscape.SetOpen(false);
        }

        void Update()
        {
            bool open = IsOpen;
            if (open && Time.unscaledTime - lastInteraction > autoHideSeconds)
                Hide();

            // Phase 4: 패널이 열려 있는 동안 FPS 줄을 1초마다 갱신 (닫혀 있으면 비용 0)
            if (open && PerfMonitor.Instance != null && Time.unscaledTime >= nextPerfRefresh)
            {
                nextPerfRefresh = Time.unscaledTime + 1f;
                Refresh();
            }

            var st = view != null ? view.StatusText : null;
            if (st != null && st.gameObject.activeSelf && Time.unscaledTime > statusUntil)
                st.gameObject.SetActive(false);
        }

        void Refresh()
        {
            if (view == null) return;
            var cfg = gm != null ? gm.Config : null;
            if (view.Counts != null) view.Counts.Sync(gm as ClawGameManager);

            if (cfg == null)
            {
                if (view.ConfigText != null) view.ConfigText.text = "config 미로드 (게임 초기화 전)";
                if (view.WarningsBadge != null) view.WarningsBadge.SetActive(false);
                return;
            }

            if (view.ConfigText != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{gm.StateName}]");
                gm.AppendOperatorSummary(sb); // 세션 고유 줄(제한시간·목표·보상 등) — Main/Claw가 각자 제공
                // Phase 4: 운영자가 개발 도구 없이 §8 "60FPS 유지"를 확인 (PerfMonitor가 씬에 없으면 생략)
                if (PerfMonitor.Instance != null)
                    sb.AppendLine(PerfMonitor.Instance.Summary());
                if (RenderQualityApplier.Instance != null)
                    sb.AppendLine(RenderQualityApplier.Instance.Summary);
                if (gm.LastResetMs > 0f)
                    sb.AppendLine($"리셋 {gm.LastResetMs:F0}ms (목표 ≤1000)");
                view.ConfigText.text = sb.ToString();
            }

            string exLine = RuntimeExceptionCounter.Instance != null ? RuntimeExceptionCounter.Instance.Summary() : null;
            bool hasWarnings = cfg.warnings.Count > 0 || exLine != null;
            if (view.WarningsBadge != null) view.WarningsBadge.SetActive(hasWarnings);
            if (hasWarnings && view.WarningsText != null)
            {
                var sb = new StringBuilder();
                if (exLine != null) sb.AppendLine(exLine); // Phase 5-1: 런타임 예외 우선 노출
                if (cfg.warnings.Count > 0)
                {
                    sb.AppendLine($"config 경고 {cfg.warnings.Count}건");
                    foreach (var w in cfg.warnings) sb.AppendLine("- " + w);
                }
                view.WarningsText.text = sb.ToString();
            }
        }
    }
}
