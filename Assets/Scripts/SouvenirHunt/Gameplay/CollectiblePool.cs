using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 오브젝트 풀 (T3). 시작 시 사전 생성 → SetActive on/off 방식.
    /// 런타임 Instantiate/Destroy 금지 (GC·프레임 저하 방지).
    /// </summary>
    public class CollectiblePool : MonoBehaviour
    {
        [SerializeField] Collectible prefab;
        [Tooltip("사전 생성 개수. 목표 10개보다 여유 있게(기본 12).")]
        [SerializeField] int prewarmCount = 12;

        readonly List<Collectible> all = new();
        int cursor;

        public int Capacity => prewarmCount;

        void Awake()
        {
            if (prefab == null)
            {
                Debug.LogError("[CollectiblePool] prefab 미배선 — 수집품을 만들 수 없음 (씬 배선 확인: 메뉴 XREAL/10)");
                return; // Get()은 null을 돌려주고 SpawnManager가 중단 — 앱은 계속
            }
            for (int i = 0; i < prewarmCount; i++)
            {
                var c = Instantiate(prefab, transform);
                c.gameObject.SetActive(false);
                all.Add(c);
            }
        }

        /// <summary>비활성 인스턴스 하나를 활성화해 반환.</summary>
        public Collectible Get()
        {
            for (int i = 0; i < all.Count; i++)
            {
                int idx = (cursor + i) % all.Count;
                if (!all[idx].gameObject.activeSelf)
                {
                    cursor = idx + 1;
                    all[idx].gameObject.SetActive(true);
                    return all[idx];
                }
            }
            Debug.LogWarning("[CollectiblePool] 여유 인스턴스 부족 — prewarmCount를 늘리세요.");
            return null;
        }

        /// <summary>전체 회수(비활성화). 리셋 시 호출.</summary>
        public void ReturnAll()
        {
            foreach (var c in all) c.gameObject.SetActive(false);
            cursor = 0;
        }
    }
}
