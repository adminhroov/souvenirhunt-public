using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// 종료/취소 다이얼로그(월드, 기계 위쪽·눈을 향해 기울임). 조이스틱 좌우로 선택, 잡기로 확정 — 빔 미사용(3차).
    /// 수동형: ClawGameManager가 매 프레임 Tick(스틱 x, 잡기, 홈 재누름)을 넣고 결과를 받는다(입력 소비자는 GM 하나).
    /// 기본 선택은 취소(안전). 10초 무조작 시 자동 취소. 실기기 종료는 GM의 QuitHandler(SDK와 같은 XREALPlugin.QuitApplication).
    /// </summary>
    public class ClawExitDialog : MonoBehaviour
    {
        public enum Result { None, Quit, Cancel }

        [SerializeField] GameObject panelRoot;
        [SerializeField] Image quitCard;    // 왼쪽 "종료"
        [SerializeField] Image cancelCard;  // 오른쪽 "취소"
        [SerializeField] TMP_Text hintText;
        [SerializeField] Color normalColor = new Color(0.10f, 0.14f, 0.20f, 0.95f);
        [SerializeField] Color selectedColor = new Color(0.15f, 0.55f, 0.75f, 1f);
        [SerializeField] float autoCloseSeconds = 10f;
        [Tooltip("스틱 x가 이 값을 넘을 때만 선택 이동(|x| 이하 = 데드밴드 → 히스테리시스)")]
        [SerializeField] float selectThreshold = 0.5f;

        public bool IsOpen { get; private set; }
        /// <summary>0 = 종료(왼쪽), 1 = 취소(오른쪽)</summary>
        public int Selected { get; private set; } = 1;
        public float AutoCloseSeconds { get => autoCloseSeconds; set => autoCloseSeconds = Mathf.Max(0.1f, value); }

        float closeAt;
        int lastShownSec = -1;

        void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Open()
        {
            Selected = 1;
            closeAt = Time.unscaledTime + autoCloseSeconds;
            lastShownSec = -1;
            IsOpen = true;
            if (panelRoot != null) panelRoot.SetActive(true);
            Paint();
            UpdateHint();
        }

        public void Close()
        {
            IsOpen = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <param name="machineX">기계 좌표 스틱 x(+ = 사용자 오른쪽) — clawPadRotation과 무관하게 방향 일치</param>
        public Result Tick(float machineX, bool confirm, bool homeAgain)
        {
            if (!IsOpen) return Result.None;

            if (machineX < -selectThreshold && Selected != 0) { Selected = 0; Paint(); closeAt = Time.unscaledTime + autoCloseSeconds; }
            else if (machineX > selectThreshold && Selected != 1) { Selected = 1; Paint(); closeAt = Time.unscaledTime + autoCloseSeconds; }

            if (confirm)
            {
                var r = Selected == 0 ? Result.Quit : Result.Cancel;
                Close();
                return r;
            }
            if (homeAgain || Time.unscaledTime >= closeAt)
            {
                Close();
                return Result.Cancel;
            }
            UpdateHint();
            return Result.None;
        }

        void Paint()
        {
            if (quitCard != null) quitCard.color = Selected == 0 ? selectedColor : normalColor;
            if (cancelCard != null) cancelCard.color = Selected == 1 ? selectedColor : normalColor;
        }

        void UpdateHint()
        {
            if (hintText == null) return;
            int sec = Mathf.CeilToInt(Mathf.Max(0f, closeAt - Time.unscaledTime));
            if (sec == lastShownSec) return;
            lastShownSec = sec;
            Loc.Fit(hintText);
            hintText.SetText(Loc.T("← 종료 · 취소 →   [잡기] 확인 · {0}초 후 자동 취소", "← Quit · Cancel →   [Grab] OK · auto-cancel in {0}s"), sec); // 11-10 Loc
        }
    }
}
