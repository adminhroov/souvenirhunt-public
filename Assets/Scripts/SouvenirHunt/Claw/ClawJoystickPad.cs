using UnityEngine;
using UnityEngine.EventSystems;

namespace SouvenirHunt
{
    /// <summary>
    /// 오버레이 상단 절반(가로로 눕히면 왼쪽)의 아날로그 조이스틱. uGUI 포인터 이벤트로 직접 계산 —
    /// 절대 모드: 패드 중심에서 떨어진 거리/travelRadius = 강도(사용자 결정 기본). 상대 모드: 첫 터치점이 중심.
    /// 포인터 ID를 고정 추적해 잡기 버튼과 멀티터치가 섞이지 않는다. 손을 떼면 즉시 0.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ClawJoystickPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] RectTransform ring;   // 이동 범위 표시(상대 모드에선 터치점으로 이동)
        [SerializeField] RectTransform knob;   // 엄지 추종
        [Tooltip("절대 모드: 패드 중심에서 최대 변위(px, 캔버스 1080×2340 기준)")]
        [SerializeField] float travelRadiusPx = 300f;
        [Tooltip("상대 모드: 첫 터치점에서 최대 변위(px)")]
        [SerializeField] float relativeRadiusPx = 260f;

        RectTransform rect;
        int pointerId = int.MinValue;
        Vector2 anchor;

        void Awake() => rect = (RectTransform)transform;

        void OnDisable() => Release();

        void Update()
        {
            // 외부(일시정지 등)에서 Clear된 뒤 포인터가 남아 있으면 다음 터치가 막힌다 → 동기화
            if (pointerId != int.MinValue && !ClawPadInput.IsTouching) Release();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;           // 두 번째 손가락은 무시
            if (!ToLocal(e, out var local)) return;
            pointerId = e.pointerId;
            anchor = ClawPadInput.Relative ? local : Vector2.zero;
            if (ring != null) ring.anchoredPosition = anchor;
            Apply(local);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            if (ToLocal(e, out var local)) Apply(local);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            Release();
        }

        void Apply(Vector2 local)
        {
            float r = ClawPadInput.Relative ? relativeRadiusPx : travelRadiusPx;
            var d = (local - anchor) / Mathf.Max(1f, r);
            if (d.sqrMagnitude > 1f) d.Normalize();
            ClawPadInput.SetStick(d, true);
            if (knob != null) knob.anchoredPosition = anchor + d * r;
        }

        void Release()
        {
            pointerId = int.MinValue;
            ClawPadInput.SetStick(Vector2.zero, false);
            if (knob != null) knob.anchoredPosition = Vector2.zero;
            if (ring != null) ring.anchoredPosition = Vector2.zero;
        }

        bool ToLocal(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out local);
    }
}
