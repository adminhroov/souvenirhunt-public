using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace SouvenirHunt
{
    /// <summary>
    /// 에디터 전용 테스트 입력: WASD/화살표 = 조이스틱(기계 좌표), Space = 잡기, Esc/H = 홈, R = 정면 재정렬.
    /// Beam Pro 프리팹 오버레이가 없거나 마우스 조작이 번거로울 때. 오버레이 터치 중엔 개입하지 않는다.
    /// </summary>
    public class ClawKeyboardFallback : MonoBehaviour
    {
#if UNITY_EDITOR
        bool keyboardActive;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (ClawPadInput.IsTouching && !keyboardActive) return; // 마우스(오버레이) 우선

            Vector2 m = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) m.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) m.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) m.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) m.x -= 1f;
            if (kb.leftShiftKey.isPressed) m *= 0.4f; // 약한 강도 테스트

            bool any = m.sqrMagnitude > 0f;
            if (any || keyboardActive) ClawPadInput.SetMachineStick(any ? Vector2.ClampMagnitude(m, 1f) : Vector2.zero);
            keyboardActive = any;

            if (kb.spaceKey.wasPressedThisFrame) ClawPadInput.PressGrab();
            if (kb.spaceKey.wasReleasedThisFrame) ClawPadInput.ReleaseGrab();
            if (kb.escapeKey.wasPressedThisFrame || kb.hKey.wasPressedThisFrame) ClawPadInput.PressHome(); // 종료/취소 다이얼로그
            if (kb.rKey.wasPressedThisFrame) ClawPadInput.PressRecenter();                                     // 정면 재정렬(홈 길게 누르기)
        }
#endif
    }
}
