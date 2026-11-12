using System;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인덱스 기반 메시 카탈로그 (Phase 0, 인수인계 §2-3).
    /// config.txt에는 인덱스만 노출되고, 실제 에셋은 이 인스펙터 배열에만 등록한다.
    /// 더미 → 실물 교체 = 배열 항목 교체 or config의 meshIndex 변경만으로 끝나야 한다.
    /// 배열은 빌드 포함물이므로 후보를 넉넉히 등록해 둘 것 (§4-2 한계).
    /// </summary>
    [CreateAssetMenu(menuName = "SouvenirHunt/Mesh Library", fileName = "MeshLibrary")]
    public class MeshLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("운영 확인용 라벨 (기능에는 영향 없음)")]
            public string label;
            public Mesh mesh;
            public Material material;
        }

        [SerializeField] Entry[] entries;

        public int Count => entries?.Length ?? 0;

        /// <summary>범위 밖 인덱스는 0번으로 폴백 (§4-2 방어 — 앱은 죽지 않는다).</summary>
        public Entry Get(int index)
        {
            if (entries == null || entries.Length == 0)
            {
                Debug.LogWarning("[MeshLibrary] 등록된 항목이 없습니다.");
                return default;
            }
            if (index < 0 || index >= entries.Length)
            {
                Debug.LogWarning($"[MeshLibrary] meshIndex {index} 범위(0~{entries.Length - 1}) 초과 → 0번 폴백");
                index = 0;
            }
            return entries[index];
        }

#if UNITY_EDITOR
        /// <summary>에디터 셋업 스크립트 전용 — 배열 일괄 등록.</summary>
        public void SetEntries(Entry[] newEntries) => entries = newEntries;
#endif
    }
}
