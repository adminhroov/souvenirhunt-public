using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 콘솔 조이스틱·버튼(Model2) 연출(5차, 09-14) — 플레이어 패드 입력을 기계의 물리 조이스틱으로 보여준다. 시각 전용.
    /// stickPivot(받침 상단 중심, 부스 축 정렬)을 MachineStick 방향으로 기울이고(앞 = 기계 안쪽 +z), 잡기 누름 동안 버튼을 눌린 위치로.
    /// 입력은 그대로 따라감(다이얼로그 좌우 선택도 조이스틱이 움직여 보이는 게 자연스럽다). 폴링 소비자 규칙대로 LateUpdate.
    /// </summary>
    public class ClawConsoleVisual : MonoBehaviour
    {
        [SerializeField] Transform stickPivot;
        [SerializeField] Transform button;
        [SerializeField] float maxTiltDeg = 18f;
        [SerializeField] float buttonTravel = 0.006f;
        [Tooltip("지수 평활 시정수(초)")]
        [SerializeField] float smoothTau = 0.06f;

        Vector2 stick;
        float press;
        Quaternion pivotRest = Quaternion.identity;
        Vector3 buttonRest;

        /// <summary>현재 조이스틱 기울기(부스 로컬, x=오른쪽 성분·y=앞 성분, −1~1) — 셀프테스트용</summary>
        public Vector2 Tilt => stick;
        public Transform StickPivot => stickPivot;
        public Transform Button => button;
        public float MaxTiltDeg => maxTiltDeg;
        public float ButtonTravel => buttonTravel;

        void Awake()
        {
            if (stickPivot != null) pivotRest = stickPivot.localRotation;
            if (button != null) buttonRest = button.localPosition;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, smoothTau));

            stick = Vector2.Lerp(stick, Vector2.ClampMagnitude(ClawPadInput.MachineStick, 1f), a);
            if (stickPivot != null)
                // Euler(+θ,0,0)은 위(+y)를 앞(+z)으로, Euler(0,0,+θ)는 위를 왼쪽(−x)으로 기울인다
                stickPivot.localRotation = pivotRest * Quaternion.Euler(stick.y * maxTiltDeg, 0f, -stick.x * maxTiltDeg);

            press = Mathf.Lerp(press, ClawPadInput.GrabHeld ? 1f : 0f, 1f - Mathf.Exp(-dt / 0.03f));
            if (button != null) button.localPosition = buttonRest + Vector3.down * (buttonTravel * press);
        }
    }
}
