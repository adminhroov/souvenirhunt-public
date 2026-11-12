using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 스폰 시스템 (T3). SpawnPoints(25개) 중 spawnCount(10)곳을 골라 풀 오브젝트 배치.
    /// Random 모드는 층화 추첨 — 은폐형(SpawnPointMarker.Hidden) 포인트를 hiddenPerRound 범위만큼 반드시 포함
    /// (기획서 §02 "가구 뒤 부분 은폐 3~4개"). Fixed 모드는 하이어라키 앞 N개(발주처 고정 배치 확정 시 사용).
    /// 개수·모드는 GameManager.ApplyConfig가 config.txt 값으로 주입 (Phase 0) — 인스펙터 값은 로드 전 기본값.
    /// 예외를 던지지 않는다 — 참조 누락 시 로그만 남기고 리셋은 계속된다.
    /// </summary>
    public class SpawnManager : MonoBehaviour
    {
        public enum Mode { Fixed, Random }

        [Tooltip("config.txt useFixedLayout이 리셋마다 덮어씀 — 여기는 로드 전 기본값.")]
        [SerializeField] Mode spawnMode = Mode.Random;
        [Tooltip("SpawnPoints 부모 (자식 빈 Transform 25개).")]
        [SerializeField] Transform spawnPointsRoot;
        [SerializeField] CollectiblePool pool;
        [Tooltip("config.txt targetCount가 리셋마다 덮어씀 — 여기는 로드 전 기본값.")]
        [SerializeField] int spawnCount = 10;
        [Tooltip("Random 모드에서 한 판에 포함할 은폐형 포인트 수 범위 (기획서 §02: 3~4개). 은폐 포인트가 부족하면 있는 만큼.")]
        [SerializeField] Vector2Int hiddenPerRound = new Vector2Int(3, 4);
        [Tooltip("0이면 시간 기반(매번 랜덤). 그 외 값은 재현용 고정 시드 — 운영 빌드에서는 반드시 0.")]
        [SerializeField] int randomSeed = 0;

        List<Transform> allPoints;
        RewardTable rewards;
        // Phase 5-1: 리센터 재배치용 — 각 활성 수집품이 어느 스폰 포인트에 놓였는지 기억
        readonly List<(Collectible c, Transform point)> placements = new();
        readonly List<Transform> hiddenPts = new();
        readonly List<Transform> openPts = new();
        readonly List<Transform> picked = new();
        readonly StringBuilder spawnLog = new(); // 리셋당 1회만 사용 — 프레임 할당 아님

        public int MaxSpawnable => Mathf.Min(EnsurePoints().Count, pool != null ? pool.Capacity : int.MaxValue);

        void Awake() => EnsurePoints();

        List<Transform> EnsurePoints()
        {
            // Awake 순서와 무관하게 안전 (GameManager.Start → ResetGame이 먼저 올 수 있음)
            if (allPoints == null)
            {
                allPoints = new List<Transform>();
                if (spawnPointsRoot != null)
                    foreach (Transform t in spawnPointsRoot) allPoints.Add(t);
                else
                    Debug.LogWarning("[SpawnManager] spawnPointsRoot 미지정 — 스폰 불가");
            }
            return allPoints;
        }

        /// <summary>config 적용 (리셋마다 호출). useFixedLayout → Fixed 모드.</summary>
        public void Configure(int count, bool fixedLayout, RewardTable rewardTable)
        {
            spawnCount = count;
            spawnMode = fixedLayout ? Mode.Fixed : Mode.Random;
            rewards = rewardTable;
        }

        public void Respawn()
        {
            if (pool == null)
            {
                Debug.LogError("[SpawnManager] pool 미배선 — 스폰 생략 (씬 배선 확인: 메뉴 XREAL/10)");
                return;
            }
            pool.ReturnAll();
            placements.Clear();

            var points = EnsurePoints();
            picked.Clear();
            if (spawnMode == Mode.Fixed)
            {
                for (int i = 0; i < points.Count && picked.Count < spawnCount; i++) picked.Add(points[i]); // 하이어라키 앞 N개
            }
            else
            {
                PickStratified(points);
            }

            spawnLog.Clear();
            spawnLog.Append("[Spawn] 모드=").Append(spawnMode)
                    .Append(" 요청=").Append(spawnCount)
                    .Append(" 포인트풀=").Append(points.Count);

            int idx = 0;
            foreach (var p in picked)
            {
                var c = pool.Get();
                if (c == null) break;
                c.transform.SetPositionAndRotation(p.position, p.rotation);
                string meshLabel = "(기본)";
                if (rewards != null && rewards.Count > 0)
                {
                    var entry = rewards.GetMeshEntry(idx);
                    c.SetVisual(entry); // 보상 메시 순환 배정 (Phase 0)
                    meshLabel = string.IsNullOrEmpty(entry.label) ? "(무라벨)" : entry.label;
                }
                c.ResetState();
                placements.Add((c, p));
                AppendSpawnLine(p, meshLabel);
                idx++;
            }
            // 배치 전수를 한 줄로 남긴다 — 플레이테스트에서 "안 보이는 수집품" 신고 시 사후 추적용.
            // Debug.Log 1회 = 스택트레이스 1개 (항목마다 로그하면 logcat이 스택으로 도배된다)
            Debug.Log(spawnLog.ToString());
        }

        // 부스 원점(=체험자 눈)이 기준 — 3DoF라 게임 내내 불변. yaw/pitch는 고개를 얼마나 돌려야 하는지,
        // 은폐 여부는 앞에 오클루더 패널이 있는지를 뜻한다.
        void AppendSpawnLine(Transform p, string meshLabel)
        {
            // BoothRoot 기준으로 되돌린다. 손으로 편 식(부모 회전·위치만 곱하기)은 스케일과 계층 깊이를
            // 가정하게 되는데, 이 로그가 곧 사후 추적 수단이라 조용히 틀리면 안 된다.
            Transform booth = spawnPointsRoot != null ? spawnPointsRoot.parent : null;
            Vector3 local = booth != null ? booth.InverseTransformPoint(p.position) : p.localPosition;
            float dist = local.magnitude;
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            bool hidden = p.TryGetComponent<SpawnPointMarker>(out var m) && m.Hidden;

            spawnLog.AppendLine().Append("  ").Append(p.name)
                    .Append(hidden ? " [은폐]" : "       ")
                    .Append(" d=").Append(dist.ToString("F2"))
                    .Append("m yaw=").Append(yaw.ToString("F0"))
                    .Append("° pitch=").Append(pitch.ToString("F0"))
                    .Append("° mesh=").Append(meshLabel);
        }

        // 은폐형 k개(hiddenPerRound 범위 내 랜덤) + 일반 (spawnCount−k)개 → 섞어서 배정
        void PickStratified(List<Transform> points)
        {
            var rng = randomSeed == 0 ? new System.Random() : new System.Random(randomSeed);
            hiddenPts.Clear();
            openPts.Clear();
            foreach (var t in points)
            {
                var m = t.GetComponent<SpawnPointMarker>();
                (m != null && m.Hidden ? hiddenPts : openPts).Add(t);
            }
            Shuffle(hiddenPts, rng);
            Shuffle(openPts, rng);

            int lo = Mathf.Max(0, Mathf.Min(hiddenPerRound.x, hiddenPerRound.y));
            int hi = Mathf.Max(hiddenPerRound.x, hiddenPerRound.y);
            int k = rng.Next(lo, hi + 1);
            k = Mathf.Min(k, hiddenPts.Count, spawnCount);

            for (int i = 0; i < k; i++) picked.Add(hiddenPts[i]);
            for (int i = 0; i < openPts.Count && picked.Count < spawnCount; i++) picked.Add(openPts[i]);
            // 일반 포인트가 부족하면 남은 은폐 포인트로 채움 (완전 은폐는 메뉴 13 가시성 검증이 이미 배제)
            for (int i = k; i < hiddenPts.Count && picked.Count < spawnCount; i++) picked.Add(hiddenPts[i]);
            Shuffle(picked, rng); // 보상 메시 순환 배정이 은폐 여부와 상관없게
        }

        static void Shuffle(List<Transform> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>리센터 시 (Phase 5-1): 게임 상태·선택을 유지한 채, 이동된 스폰 포인트로 아직 살아있는 수집품만 재배치.</summary>
        public void Reposition()
        {
            foreach (var (c, point) in placements)
            {
                if (c == null || point == null) continue;
                if (!c.gameObject.activeSelf) continue; // 이미 수집된 것은 그대로 (풀에 반환됨)
                c.transform.SetPositionAndRotation(point.position, point.rotation);
            }
        }
    }
}
