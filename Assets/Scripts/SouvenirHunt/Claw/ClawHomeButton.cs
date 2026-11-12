using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// 오버레이 홈 버튼(세로 우상단 = 눕히면 가로 좌상단, 조이스틱 위쪽).
    /// 짧게(클릭) → ClawPadInput.PressHome() → 종료/취소 다이얼로그. **길게(1초, 4차) → ClawPadInput.PressRecenter() → 정면 재정렬.**
    /// uGUI는 Up 다음에 Click을 보내므로 롱프레스가 발화했으면 그 뒤 Click은 무시한다. SDK Home은 오버레이가 덮어 도달 불가.
    /// </summary>
    public class ClawHomeButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        [SerializeField] Image face;
        [SerializeField] Color normalColor = new Color(0.10f, 0.14f, 0.20f, 1f);
        [SerializeField] Color pressedColor = new Color(0.25f, 0.45f, 0.60f, 1f);
        [SerializeField] Color longPressColor = new Color(0.15f, 0.55f, 0.75f, 1f);
        [Tooltip("이 시간 이상 누르면 재정렬(초)")]
        [SerializeField] float longPressSec = 1.0f;

        int pointerId = int.MinValue, clearAtDown;
        float downAt;
        bool longFired;

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
            clearAtDown = ClawPadInput.ClearCount;
            downAt = Time.unscaledTime;
            longFired = false;
            Tint(pressedColor);
        }

        void Update()
        {
            // 10-26 누르는 중에 입력이 비워짐([다시 시작]·씬 전환·일시정지) — 이번 누름은 버린다(뗄 때 오는 Click도 무시)
            if (pointerId != int.MinValue && clearAtDown != ClawPadInput.ClearCount)
            {
                ResetState();
                longFired = true;
                return;
            }
            if (pointerId == int.MinValue || longFired) return;
            if (Time.unscaledTime - downAt >= longPressSec)
            {
                longFired = true;
                ClawPadInput.PressRecenter();
                Tint(longPressColor);
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = int.MinValue;
            Tint(normalColor);
            // longFired는 유지 — 바로 뒤에 오는 Click이 이걸 보고 무시한다
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (longFired) { longFired = false; return; }
            ClawPadInput.PressHome();
        }

        void OnDisable() => ResetState();
        void OnApplicationPause(bool pause) { if (pause) ResetState(); }

        void ResetState()
        {
            pointerId = int.MinValue;
            longFired = false;
            Tint(normalColor);
        }

        void Tint(Color c)
        {
            if (face != null) face.color = c;
        }
    }
}
