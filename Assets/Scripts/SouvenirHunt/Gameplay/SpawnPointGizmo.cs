using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 스폰 포인트 배치용 에디터 Gizmo (T3 + Phase 2 강화).
    /// SpawnPoints 부모에 붙이면 자식 위치를 씬 뷰에 시각화.
    /// 색 규칙(인수인계 §4-5): 일반=시안, 은폐형(SpawnPointMarker.Hidden)=주황,
    /// 3DoF 배치 규칙(거리 1.5~5m, 수평 ±120°, 수직 -30°~+45°) 위반=빨강 — 레벨 디자인 실수를 초기에 잡는다.
    /// </summary>
    public class SpawnPointGizmo : MonoBehaviour
    {
        [SerializeField] float radius = 0.12f;
        [SerializeField] Color color = new Color(0.3f, 1f, 0.9f, 0.9f);
        [SerializeField] Color hiddenColor = new Color(1f, 0.6f, 0.1f, 0.9f);
        [SerializeField] Color invalidColor = new Color(1f, 0.15f, 0.15f, 1f);
        [Tooltip("기준 원점(체험자 눈 위치). 미지정 시 이 오브젝트 기준.")]
        [SerializeField] Transform origin;

        // 3DoF 배치 제약 (인수인계 §4-5)
        const float MinDist = 1.5f, MaxDist = 5f;
        const float MaxYawDeg = 120f;
        const float MinPitchDeg = -30f, MaxPitchDeg = 45f;

        void OnDrawGizmos()
        {
            Transform o = origin != null ? origin : transform;
            foreach (Transform p in transform)
            {
                bool hidden = p.TryGetComponent<SpawnPointMarker>(out var marker) && marker.Hidden;
                Gizmos.color = IsValid(o, p.position) ? (hidden ? hiddenColor : color) : invalidColor;
                Gizmos.DrawSphere(p.position, radius);
                Gizmos.DrawLine(o.position, p.position);
            }
        }

        bool IsValid(Transform o, Vector3 worldPos)
        {
            // 원점 로컬(전방=+Z) 기준으로 판정 — BoothRoot가 회전해도 규칙은 부스 기준 유지
            Vector3 local = o.InverseTransformPoint(worldPos);
            float dist = local.magnitude;
            if (dist < MinDist || dist > MaxDist) return false;

            float yaw = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);
            if (yaw > MaxYawDeg) return false;

            float flat = new Vector2(local.x, local.z).magnitude;
            float pitch = Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg;
            return pitch >= MinPitchDeg && pitch <= MaxPitchDeg;
        }
    }
}
