using UnityEngine;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// 운영자 리셋 (T7). Beam Pro 화면의 UI 버튼 → 현재 세션(GameManager 또는 ClawGameManager).ResetGame().
    /// UI 버튼 방식이라 에디터 테스트 가능 (XREALVirtualController의 Input Action 버튼은 실기기 전용).
    /// 버튼은 하이어라키 Buttons 섹션 맨 끝에 추가 — Trigger 버튼 오버랩 이슈 방지(공식 문서 명시).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class OperatorResetButton : MonoBehaviour
    {
        Button button;

        void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(OnReset);
        }

        void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(OnReset);
        }

        void OnReset()
        {
            GameSession.Current?.ResetGame(); // 씬 무관 — Main/Claw 어느 세션이든
        }
    }
}
