using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 스폰 포인트 속성 마커 (Phase 2).
    /// hidden = 은폐형 포인트(가구 뒤, 25곳 중 8~10곳) — 완전 은폐 금지, 일부는 항상 보여야 함.
    /// SpawnPointGizmo가 색으로 구분해 표시한다.
    /// </summary>
    public class SpawnPointMarker : MonoBehaviour
    {
        [Tooltip("은폐형 포인트 여부 (기획서 §04 — 3~4개는 가구 뒤 배치)")]
        [SerializeField] bool hidden;

        public bool Hidden
        {
            get => hidden;
            set => hidden = value;
        }
    }
}
