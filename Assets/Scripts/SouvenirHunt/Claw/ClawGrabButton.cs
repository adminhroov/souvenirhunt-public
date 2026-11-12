using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// 오버레이 하단 절반(가로로 눕히면 오른쪽) 전체가 "잡기" 버튼. 화면을 보지 않고도 엄지가 닿으면 눌린다.
    /// 누름은 ClawPadInput 래치로 전달(씬이 TryConsumeGrab으로 소비) — 이벤트 순서에 안전.
    /// </summary>
    public class ClawGrabButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] Image face;
        [SerializeField] Color normalColor = new Color(0.55f, 0.16f, 0.18f, 1f);
        [SerializeField] Color pressedColor = new Color(0.95f, 0.35f, 0.30f, 1f);
        [SerializeField] Color disabledColor = new Color(0.25f, 0.25f, 0.27f, 1f);   // 8차: 영상 시청 종료 대기

        int pointerId = int.MinValue;
        bool shownEnabled = true;

        void Update()
        {
            // 10-26 외부(일시정지·화면 이동 등)에서 Clear돼 손 뗌을 못 받았으면 다음 터치가 막힌다(조이스틱과 같은 동기화) — "다시 실행하면 컨트롤러가 꼬인다"
            if (pointerId != int.MinValue && !ClawPadInput.GrabHeld) { pointerId = int.MinValue; Tint(false); }
            if (shownEnabled == ClawPadInput.GrabEnabled) return;
            shownEnabled = ClawPadInput.GrabEnabled;
            Tint(pointerId != int.MinValue);
        }

        void OnDisable()
        {
            pointerId = int.MinValue;
            ClawPadInput.ReleaseGrab();
            Tint(false);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
            ClawPadInput.PressGrab();
            Tint(true);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = int.MinValue;
            ClawPadInput.ReleaseGrab();
            Tint(false);
        }

        void Tint(bool pressed)
        {
            if (face != null) face.color = !ClawPadInput.GrabEnabled ? disabledColor : pressed ? pressedColor : normalColor;
        }
    }
}
