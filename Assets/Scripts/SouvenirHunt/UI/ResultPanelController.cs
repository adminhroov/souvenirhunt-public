using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SouvenirHunt
{
    /// <summary>
    /// 결과 패널 (기획서 SCR-04 성공 / SCR-05 시간초과). Result 상태에서만 표시.
    /// [보상 확인] 버튼(월드 스페이스: BoxCollider+XRSimpleInteractable, StartPanel과 같은 방식) →
    /// GameManager.ConfirmReward() → Reward 상태 → RewardPanel(SCR-06)로 넘어간다.
    /// 시간초과 시 버튼 노출 여부는 config rewardOnPartial(기본 false)이 결정 — 인수인계 §7 부분 보상 정책.
    /// 패널이 숨겨질 땐 버튼 콜라이더도 함께 꺼서 전방 2m 지점의 레이를 막지 않게 한다.
    /// </summary>
    public class ResultPanelController : MonoBehaviour
    {
        [SerializeField] GameObject panelRoot;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text detailText;
        [Tooltip("버튼 안내/부분 보상 안내 1줄")]
        [SerializeField] TMP_Text hintText;
        [Tooltip("[보상 확인] 버튼 — 루트 자식(콜라이더+인터랙터블). 메뉴 XREAL/21이 배선")]
        [SerializeField] XRSimpleInteractable rewardButton;
        [Tooltip("버튼 그래픽(캔버스 안) — 보상 대상이 아닐 때 숨김")]
        [SerializeField] GameObject rewardButtonVisual;

        GameManager gm;
        bool bound;
        Collider rewardCollider;

        void Awake()
        {
            if (rewardButton != null)
            {
                rewardCollider = rewardButton.GetComponent<Collider>();
                rewardButton.selectEntered.AddListener(_ => OnRewardClicked());
            }
        }

        // OnEnable 시점엔 GameManager.Awake가 아직일 수 있음 → Start에서 재시도
        void OnEnable() => TryBind();
        void Start() => TryBind();

        void TryBind()
        {
            if (bound) return;
            gm = GameManager.Instance;
            if (gm == null) { SetVisible(false, false); return; }
            gm.OnGameResult += HandleResult;
            gm.OnGameReset += HandleReset;
            gm.OnStateChanged += HandleState;
            bound = true;
            SetVisible(gm.Current == GameManager.State.Result, gm.RewardEligible);
        }

        void OnDisable()
        {
            if (!bound) return;
            gm.OnGameResult -= HandleResult;
            gm.OnGameReset -= HandleReset;
            gm.OnStateChanged -= HandleState;
            bound = false;
        }

        void HandleResult(bool success, int collected, float elapsed)
        {
            int mm = Mathf.FloorToInt(elapsed / 60f);
            int ss = Mathf.FloorToInt(elapsed % 60f);
            int total = gm != null ? gm.TargetCount : 10;
            bool eligible = gm != null && gm.RewardEligible;

            if (success)
            {
                if (titleText) titleText.text = "미션 완료!";
                if (detailText) detailText.text = $"{collected}/{total} · 소요 {mm:00}:{ss:00}";
            }
            else
            {
                if (titleText) titleText.text = "시간 종료";
                if (detailText) detailText.text = $"{collected}/{total} 수집 · 소요 {mm:00}:{ss:00}\n아쉽지만 시간이 종료되었습니다";
            }

            if (hintText)
                hintText.text = eligible
                    ? "[보상 확인]을 눌러 주세요"
                    : "다음 체험자를 위해 진행요원에게 기기를 전달해 주세요";

            SetVisible(true, eligible);
        }

        void HandleState(GameManager.State s)
        {
            // Result → Reward 전환(또는 그 외 상태)에서는 숨김. Result 진입 자체는 HandleResult가 채운 뒤 표시.
            if (s != GameManager.State.Result) SetVisible(false, false);
        }

        void HandleReset() => SetVisible(false, false);

        void OnRewardClicked()
        {
            if (gm == null) gm = GameManager.Instance;
            if (gm != null) gm.ConfirmReward(); // Result + 보상 대상일 때만 진입
        }

        void SetVisible(bool show, bool buttonOn)
        {
            if (panelRoot) panelRoot.SetActive(show);
            bool btn = show && buttonOn;
            if (rewardButtonVisual) rewardButtonVisual.SetActive(btn);
            if (rewardButton) rewardButton.enabled = btn;
            if (rewardCollider) rewardCollider.enabled = btn; // 숨김 상태에서 레이를 막지 않게
        }
    }
}
