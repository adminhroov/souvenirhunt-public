using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// HUD용 lazy follow (T6). 카메라를 부드럽게(지연) 따라오며 항상 마주본다.
    /// head-locked(카메라 직결) 금지 — 어지럼 유발. 반드시 감쇠 이동.
    /// </summary>
    public class LazyFollow : MonoBehaviour
    {
        [SerializeField] Transform target;          // 미지정 시 Camera.main
        [SerializeField] float distance = 1.5f;
        [SerializeField] float verticalOffset = -0.15f;
        [Tooltip("클수록 빨리 따라옴. 낮을수록 부드럽고 지연이 큼.")]
        [SerializeField] float positionDamping = 4f;
        [SerializeField] float rotationDamping = 6f;
        [Tooltip("이 각도 이상 벗어나야 따라오기 시작(데드존).")]
        [SerializeField] float deadZoneDeg = 10f;

        Transform cam;

        void Start()
        {
            cam = target != null ? target : (Camera.main ? Camera.main.transform : null);
        }

        void LateUpdate()
        {
            if (cam == null)
            {
                if (Camera.main == null) return;
                cam = Camera.main.transform;
            }

            Vector3 flatForward = cam.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) return;
            flatForward.Normalize();

            Vector3 desiredPos = cam.position + flatForward * distance + Vector3.up * verticalOffset;
            Vector3 toHud = (transform.position - cam.position);
            float angle = Vector3.Angle(flatForward, new Vector3(toHud.x, 0f, toHud.z));

            // 데드존 밖일 때만 위치 추종(작은 흔들림 무시)
            // 1−e^(−k·dt): 프레임레이트와 무관한 지수 감쇠 (k·dt 선형 보간은 30fps에서 추종이 느려짐)
            float posK = angle > deadZoneDeg ? positionDamping : positionDamping * 0.25f;
            transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-posK * Time.deltaTime));

            Quaternion desiredRot = Quaternion.LookRotation(transform.position - cam.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationDamping * Time.deltaTime));
        }
    }
}
