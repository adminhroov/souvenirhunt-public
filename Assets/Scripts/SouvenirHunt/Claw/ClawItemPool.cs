using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 아이템 풀 — CollectiblePool과 같은 규칙(시작 시 사전 생성, SetActive on/off, 런타임 Instantiate 금지).
    /// 활성 아이템 순회는 All을 돌며 activeSelf 검사(11차: 320개 — 순회 자체는 가볍다).
    /// </summary>
    public class ClawItemPool : MonoBehaviour
    {
        [SerializeField] ClawItem prefab;
        [Tooltip("사전 생성 수 = spec.poolSize (10-01: 게임마다 다름)")]
        [SerializeField] ClawItemSpec spec;

        readonly List<ClawItem> all = new();
        int cursor;

        public ClawItemSpec Spec => ClawItemSpec.Or(spec);
        public int Capacity => all.Count > 0 ? all.Count : Spec.poolSize;
        public IReadOnlyList<ClawItem> All => all;

        void Awake()
        {
            if (prefab == null)
            {
                Debug.LogError("[ClawItemPool] prefab 미배선 — 아이템을 만들 수 없음 (메뉴 XREAL/30 재실행)");
                return;
            }
            int prewarmCount = Spec.poolSize;
            for (int i = 0; i < prewarmCount; i++)
            {
                var c = Instantiate(prefab, transform);
                c.name = $"ClawItem_{i:000}";
                c.gameObject.SetActive(false);
                all.Add(c);
            }
        }

        public ClawItem Get()
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
            Debug.LogWarning($"[ClawItemPool] 여유 인스턴스 부족 — config 공 개수가 풀({all.Count})을 넘음");
            return null;
        }

        public void ReturnAll()
        {
            foreach (var c in all)
                if (c.gameObject.activeSelf) c.ReleaseToPool();
            cursor = 0;
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var c in all) if (c.gameObject.activeSelf) n++;
                return n;
            }
        }
    }
}
