using TMPro;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 보상 화면 (기획서 SCR-06, 1차 Mock). Reward 상태에서만 표시.
    /// 1차: config.txt의 rewardN.text 나열 + 수령 안내 문구. 2차: 3종 카드 중 1개 선택 → 재고 → NFT 발급 상태가 이 자리에 들어간다.
    /// 버튼 없음 — 다음 진행은 운영자 리셋(SCR-07). 콜라이더도 없어 레이를 막지 않는다.
    /// </summary>
    public class RewardPanelController : MonoBehaviour
    {
        [SerializeField] GameObject panelRoot;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text rewardText;
        [SerializeField] TMP_Text guideText;

        GameManager gm;
        bool bound;

        void OnEnable() => TryBind();
        void Start() => TryBind();

        void TryBind()
        {
            if (bound) return;
            gm = GameManager.Instance;
            if (gm == null) { Show(false); return; }
            gm.OnStateChanged += HandleState;
            gm.OnGameReset += HandleReset;
            bound = true;
            HandleState(gm.Current);
        }

        void OnDisable()
        {
            if (!bound) return;
            gm.OnStateChanged -= HandleState;
            gm.OnGameReset -= HandleReset;
            bound = false;
        }

        void HandleState(GameManager.State s)
        {
            bool show = s == GameManager.State.Reward;
            if (show) Fill();
            Show(show);
        }

        void HandleReset() => Show(false);

        void Fill()
        {
            if (titleText) titleText.text = "보상 안내";
            string items = gm != null && gm.Rewards != null ? gm.Rewards.BuildRewardText() : "";
            if (rewardText) rewardText.text = string.IsNullOrEmpty(items) ? "실물 기념품" : items;
            if (guideText) guideText.text = "출구에서 실물 기념품을 수령하세요\n진행요원에게 기기를 전달해 주세요";
        }

        void Show(bool on)
        {
            if (panelRoot) panelRoot.SetActive(on);
        }
    }
}
