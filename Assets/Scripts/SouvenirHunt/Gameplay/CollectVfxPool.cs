using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 수집 VFX 풀 (Phase 4). CollectBurst를 Awake에서 prewarmCount만큼 사전 생성하고 재사용한다.
    /// 런타임 Instantiate/Destroy 금지 규칙 준수. 여유가 없으면 가장 오래된 버스트를 가로채 재생.
    /// Collectible.TryCollect가 CollectVfxPool.Instance?.Play(...)로 호출 — 풀이 씬에 없어도 게임은 정상.
    /// </summary>
    public class CollectVfxPool : MonoBehaviour
    {
        public static CollectVfxPool Instance { get; private set; }

        [SerializeField] CollectBurst prefab;
        [Tooltip("동시 재생 가능 수. 클릭 간격이 0.15s 이상이라 4면 충분")]
        [SerializeField] int prewarmCount = 4;
        [Tooltip("프로파일링 비교용 — false면 VFX 전체 생략")]
        [SerializeField] bool enableVfx = true;

        CollectBurst[] all;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (prefab == null) { Debug.LogWarning("[CollectVfxPool] prefab 미지정 — VFX 생략"); return; }

            all = new CollectBurst[Mathf.Max(1, prewarmCount)];
            for (int i = 0; i < all.Length; i++)
            {
                all[i] = Instantiate(prefab, transform);
                all[i].gameObject.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <param name="showLabel">false면 "+1" 없이 스파크만(인형뽑기 캡슐·폭탄)</param>
        public void Play(Vector3 pos, Color color, bool showLabel = true)
        {
            if (!enableVfx || all == null) return;

            CollectBurst pick = null;
            float oldest = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (!b.IsPlaying) { pick = b; break; }
                if (b.StartedAt < oldest) { oldest = b.StartedAt; pick = b; }
            }
            if (pick != null) pick.Play(pos, color, showLabel);
        }

        /// <summary>리셋 등에서 즉시 정리</summary>
        public void StopAll()
        {
            if (all == null) return;
            foreach (var b in all) b.gameObject.SetActive(false);
        }
    }
}
