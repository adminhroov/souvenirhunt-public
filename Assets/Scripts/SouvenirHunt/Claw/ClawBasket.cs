using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 바스켓 물리 관리 — 아이템 배치+정착(예열 시뮬레이션), 속도 클램프, 이탈 복구, 잡을 대상 조회, 폭발, 레이어 충돌 행렬.
    /// 10-15 미션형 복원: 그릇·캡슐·폭탄(크기가 서로 다름) 수십 개. 셀 = 가장 큰 폭 + 여유의 균일 격자, 최대 2층 — 그릇은 보이는 위층부터.
    /// 폭발은 물리 함수(AddExplosionForce) 대신 아이템마다 속도를 직접 준다(결과 예측 가능): 기계 전체가 움직이되 높이는 캐리지 아래까지.
    /// 이 오브젝트는 BoothRoot 자식(로컬 identity)이므로 로컬 좌표 = ClawLayout 좌표.
    /// 콜라이더 상자(바닥·벽 4·뚜껑)는 메뉴 30이 자식 Colliders에 만들어 둔다 — 닫힌 상자라 아이템이 물리적으로 나갈 수 없고,
    /// 여기의 이탈 복구는 보험(스트레스 테스트에서 0회여야 정상).
    /// </summary>
    public class ClawBasket : MonoBehaviour
    {
        [SerializeField] ClawItemPool pool;
        [SerializeField] Transform chute;
        [Tooltip("아이템 규격. 미배선이면 풀의 spec")]
        [SerializeField] ClawItemSpec spec;
        [Tooltip("10-20 받침 더미(아래층 고정). 없거나 꺼져 있으면 예전 바닥 2층 배치")]
        [SerializeField] ClawBed bed;

        public ClawItemSpec Spec => spec != null ? spec : pool != null ? pool.Spec : ClawItemSpec.Default;
        public ClawBed Bed => bed;
        /// <summary>받침 더미가 켜져 있음 — 실물은 그 위 한 층만</summary>
        public bool BedActive => bed != null && bed.Active && bed.Count > 0;

        /// <summary>config clawMaxItemSpeed</summary>
        public float MaxItemSpeed { get; set; } = 3.5f;
        /// <summary>config clawBombForce — 폭발 옆 퍼짐 속도(m/s, 중심 가까운 아이템 기준)</summary>
        public float ExplodeSpread { get; set; } = 1.6f;
        /// <summary>config clawBombLift — 튀는 높이 비율(바닥~캐리지 아래 높이 대비)</summary>
        public float ExplodeLift { get; set; } = 0.65f;
        /// <summary>폭발 직후 속도 상한(벽 두께 0.2m를 한 스텝에 못 지나는 값)과 유지 시간</summary>
        public const float ExplosionMaxSpeed = 4.5f, ExplosionClampSec = 0.5f;

        public int EscapeCount { get; private set; }
        /// <summary>배출구 칸에 들어간 아이템을 파일로 되돌린 횟수(안전망). 10-20부터 칸 전체가 뚜껑까지 막힌 기둥이라 0이어야 정상</summary>
        public int ChuteIntrusionCount { get; private set; }
        public int ExplosionCount { get; private set; }
        public float MaxSpeedSeen { get; private set; }
        public string LastStressReport { get; private set; } = "";
        public bool StressRunning { get; private set; }

        public Vector3 ChuteWorld => chute != null ? chute.position : transform.TransformPoint(ClawLayout.ChuteLocal);

        int stepCounter;
        float explodedAt = -10f;
        readonly List<Vector3> cells0 = new(64), cells1 = new(64);   // 셀 바닥 중심(로컬) — 0층·1층
        readonly List<ClawItem> bowls = new(16), others = new(64);
        readonly HashSet<ClawItem> placingSet = new();
        readonly List<ClawItem> frozenTop = new(160);

        // ─ 진짜 맨 윗층(10-22, 사용자 "캡슐 더미가 실제 집을 수 있는 아이템에 비해 너무 크다 — 맨 위 1층 분량은 진짜 캡슐로") ─────────────
        /// <summary>config clawRealTopLayer — 받침 맨 윗층을 진짜 캡슐로(끄면 예전: 받침 전부 고정 + 실물 한 층)</summary>
        public bool RealTopLayer { get; set; } = true;
        /// <summary>이번 배치에서 진짜로 바꾼 맨 윗층 캡슐 수</summary>
        public int RealTopCount { get; private set; }
        /// <summary>진짜 맨 윗층에 필요한 풀 몫(받침이 켜져 있고 config가 켜져 있으면 맨 윗층 조각 수)</summary>
        public int RealTopReserve => BedActive && RealTopLayer ? bed.TopLayer.Length : 0;

        /// <summary>
        /// 받침 맨 윗층 조각을 숨기고 같은 자리·같은 색 진짜 캡슐(풀)을 놓는다 — 구운 자리 그대로라 서로·아래층과 안 겹치고 잠든 채 시작.
        /// ResetPile 다음, 실물 배치(PlaceAndSettle) 전에 부른다
        /// </summary>
        public int SpawnRealTopLayer()
        {
            RealTopCount = 0;
            if (!BedActive || !RealTopLayer || pool == null) return 0;
            foreach (int i in bed.TopLayer)
            {
                if (bed.IsHidden(i)) continue;
                var it = pool.Get();
                if (it == null) break;
                it.SetKind(bed.PieceKind(i), 0); // 10-26 받침 = 상품 3종(예전 캡슐 색)
                bed.Hide(i);
                it.TopLayer = true;
                it.Teleport(bed.WorldPosition(i), bed.WorldRotation(i));
                it.Body.Sleep();
                RealTopCount++;
            }
            Physics.SyncTransforms();
            return RealTopCount;
        }

        /// <summary>재배치(리센터) 전 — 아직 기계 안에 있는(안 들린) 맨 윗층 캡슐을 풀로(다시 만든다)</summary>
        public void ReturnRealTop()
        {
            if (pool == null) return;
            foreach (var it in pool.All) if (it.gameObject.activeSelf && !it.Held && it.TopLayer) it.ReleaseToPool();
            RealTopCount = 0;
        }
        /// <summary>마지막 배치의 예열 시뮬레이션 시간(ms) — 기기 목표 ≤ 500</summary>
        public float LastSettleMs { get; private set; }

        void Awake() => ApplyLayerMatrix();

        /// <summary>
        /// ClawItem↔ClawItem/ClawMachine만 충돌, ClawHeld는 무충돌, 기계는 아이템하고만. 그 외 레이어(Default·UI·Environment·XRI)와는 전부 off.
        /// 메뉴 30이 에디터에서도 호출하고 런타임 Awake에서 재적용(멱등) — 기기 상태 보장.
        /// </summary>
        public static void ApplyLayerMatrix()
        {
            int item = LayerMask.NameToLayer("ClawItem");
            int machine = LayerMask.NameToLayer("ClawMachine");
            int held = LayerMask.NameToLayer("ClawHeld");
            int bedLayer = LayerMask.NameToLayer("ClawBed"); // 10-20 받침 더미 — 없으면(구버전 씬) 무시
            int fingerLayer = LayerMask.NameToLayer("ClawFinger"); // 10-22 물리 집게발 — 아이템하고만
            if (item < 0 || machine < 0 || held < 0)
            {
                Debug.LogWarning("[ClawBasket] Claw 레이어 미생성 — 충돌 행렬 미적용(메뉴 XREAL/30 실행 필요). 앱은 계속");
                return;
            }
            for (int l = 0; l < 32; l++)
            {
                Physics.IgnoreLayerCollision(item, l, !(l == item || l == machine || (bedLayer >= 0 && l == bedLayer) || (fingerLayer >= 0 && l == fingerLayer)));
                Physics.IgnoreLayerCollision(held, l, true);
                if (l != item) Physics.IgnoreLayerCollision(machine, l, true);
                if (bedLayer >= 0 && l != item) Physics.IgnoreLayerCollision(bedLayer, l, true);
                if (fingerLayer >= 0 && l != item) Physics.IgnoreLayerCollision(fingerLayer, l, true);
            }
        }

        // ─ 배치 + 정착 ──────────────────────────────────────────────
        /// <summary>
        /// 겹침 없는 격자에 아이템을 놓고(층 안에서 섞음, 지터·회전, 배출구 주변 비움) 예열 시뮬레이션으로 정착한 상태로 보이게 한다.
        /// 0층에 다 들어가면 전부 0층. 넘치면 넘친 만큼 1층(반 칸 오프셋)에 — 그릇을 먼저 1층에 올려 아래에 묻히지 않게(인수인계 10차 우려).
        /// 그릇·폭탄은 바로 세워 무작위 방향, 캡슐은 아무 방향. Playing 중에는 호출하지 않는다(LastSettleMs로 측정).
        /// </summary>
        public void PlaceAndSettle(IList<ClawItem> items)
        {
            placingSet.Clear();
            foreach (var it in items) if (it != null) placingSet.Add(it);
            BuildCells(placingSet);
            Shuffle(cells0);
            Shuffle(cells1);
            if (BedActive)
            {
                // 10-20 받침 위: 그릇(먼저 놓임)이 받침 표면이 높은 칸에 오게 — 낮은 골에 납작한 그릇이 들어가면 옆 캡슐(봉우리 위)보다
                // 집게 닿는 깊이(GrabReach) 넘게 낮아 못 잡는다. 높은 칸 상위 1/3(최소 그릇 수×2) 안에서는 섞는다
                cells0.Sort((a, b) => b.y.CompareTo(a.y));
                int top = Mathf.Min(cells0.Count, Mathf.Max(cells0.Count / 3, CountBowls(items) * 2));
                ShuffleRange(cells0, 0, top);
                ShuffleRange(cells0, top, cells0.Count);
            }
            // 10-26 목표물(그릇·키링)은 집게가 닿는 안전 구역 칸부터(ClawLayout.InTargetSafeZone) — 위 순서(받침 위 = 높은 칸)는 구역 안에서 유지
            SafeFirst(cells0);
            SafeFirst(cells1);
            bowls.Clear();
            others.Clear();
            foreach (var it in items) (it.IsTarget ? bowls : others).Add(it); // 10-22 목표물(그릇·키링)을 보이는 높은 칸부터
            Shuffle(bowls);
            Shuffle(others);
            LastSafeCells = 0;
            foreach (var p in cells0) if (ClawLayout.InTargetSafeZone(p.x, p.z)) LastSafeCells++;
            if (bowls.Count > LastSafeCells)
                Debug.LogWarning($"[ClawBasket] 목표물 {bowls.Count}개 > 안전 구역 칸 {LastSafeCells} — 남는 목표물은 구역 밖 칸에(못 잡을 수 있음)");

            int overflow = Mathf.Max(0, items.Count - cells0.Count);
            int c0 = 0, c1 = 0;
            // 1층 몫: 그릇부터
            int upper = Mathf.Min(overflow, cells1.Count);
            int upperBowls = Mathf.Min(upper, bowls.Count);
            for (int i = 0; i < bowls.Count; i++)
            {
                if (i < upperBowls) Place(bowls[i], cells1[c1++], 1);
                else if (c0 < cells0.Count) Place(bowls[i], cells0[c0++], 0);
                else Place(bowls[i], OverflowPoint(true), -1);
            }
            int upperOthers = upper - upperBowls;
            for (int i = 0; i < others.Count; i++)
            {
                if (c0 < cells0.Count && (i >= upperOthers || c1 >= cells1.Count)) Place(others[i], cells0[c0++], 0);
                else if (c1 < cells1.Count) Place(others[i], cells1[c1++], 1);
                else Place(others[i], OverflowPoint(false), -1);
            }
            if (items.Count > cells0.Count + cells1.Count)
                Debug.LogWarning($"[ClawBasket] 배치 셀 {cells0.Count + cells1.Count} < 아이템 {items.Count} — 초과분은 파일 위에 낙하 배치");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            // 10-20 받침 위(울퉁불퉁)에서는 그릇이 캡슐 봉우리에 걸려 최대 50° 기운다 → 예열 동안만 기울기 고정(바로 선 채 내려앉음).
            // 10-26(2) 사용자 "너무 부자연스러워 — 자연스럽게 쌓이게": 받침 위에서는 아무 방향으로 떨어뜨려(Place → 붓기) 기울기 고정 안 함, 예열을 길게
            bool holdBowls = false;
            if (holdBowls)
                foreach (var item in items)
                    if (item.IsTarget && !item.Body.isKinematic) item.Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            // 10-22 진짜 맨 윗층은 예열 동안 고정 — 떨어지는 상품에 깨어 굴러 그 위 그릇이 기울었다(셀프테스트 3/6 기울어짐). 끝나면 풀고 재운다
            frozenTop.Clear();
            if (pool != null)
                foreach (var it in pool.All)
                    if (it.gameObject.activeSelf && it.TopLayer && !it.Held && !it.Body.isKinematic) { it.Body.isKinematic = true; frozenTop.Add(it); }
            Physics.SyncTransforms();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                int warm = BedActive ? 120 : 60; // 붓기는 굴러 자리 잡을 시간이 더 필요
                for (int s = 0; s < warm; s++) Physics.Simulate(0.02f);
                if (BedActive) FixPoured(items);
            }
            finally
            {
                Physics.simulationMode = prev;
            }
            foreach (var item in items)
            {
                if (item.Body.isKinematic) continue;
                if (holdBowls && item.IsTarget) item.Body.constraints = RigidbodyConstraints.None;
                item.Body.linearVelocity = Vector3.zero;
                item.Body.angularVelocity = Vector3.zero;
                item.Body.Sleep();
            }
            foreach (var it in frozenTop)
            {
                it.Body.isKinematic = false;
                it.Body.linearVelocity = Vector3.zero;
                it.Body.angularVelocity = Vector3.zero;
                it.Body.Sleep();
            }
            frozenTop.Clear();
            sw.Stop();
            LastSettleMs = (float)sw.Elapsed.TotalMilliseconds;
            stepCounter = 0;
        }

        /// <summary>셀(바닥 중심 로컬) 위에 종류별 높이로 놓기. layer −1 = 넘침(공중에서 낙하)</summary>
        void Place(ClawItem item, Vector3 cellBase, int layer)
        {
            if (BedActive && layer >= 0) { Pour(item, cellBase); return; }
            var s = Spec;
            var local = cellBase;
            if (layer >= 0)
            {
                local.x += Random.Range(-s.jitter, s.jitter);
                local.z += Random.Range(-s.jitter, s.jitter);
                local.y += item.HalfHeight + 0.002f;
            }
            Quaternion rot = item.ItemKind switch
            {
                ClawItem.Kind.Capsule => Random.rotation,
                ClawItem.Kind.Bomb => Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f)),
                _ => Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
            };
            item.Teleport(transform.TransformPoint(local), transform.rotation * rot);
            item.Body.WakeUp();
        }

        /// <summary>
        /// 10-26(2) 받침 위 붓기(사용자 "너무 부자연스러워 — 좀 더 자연스럽게 쌓이게, 좀 더 섞이게"): 칸 자리에서 조금 비켜(칸의 ±22%) 아무 방향으로 돌려
        /// 더미 표면 위 공중에 띄운다 — 예열 동안 떨어지며 구르고 엎어진다. 이웃 칸끼리 닿지 않게 체크무늬로 높이를 감싸는 지름만큼 엇갈림
        /// </summary>
        void Pour(ClawItem item, Vector3 cellBase)
        {
            var s = Spec;
            float step = s.CellSize, w = item.Width;
            float dia = item.ItemKind == ClawItem.Kind.Keyring ? w * 1.74f : Mathf.Sqrt(w * w + item.HalfHeight * item.HalfHeight * 4f);
            int tier = (Mathf.RoundToInt(cellBase.x / step) + Mathf.RoundToInt(cellBase.z / step)) & 1;
            var local = cellBase;
            local.x += Random.Range(-0.22f, 0.22f) * step;
            local.z += Random.Range(-0.22f, 0.22f) * step;
            local.y += dia * 0.5f + 0.01f + tier * dia + Random.Range(0f, 0.03f);
            item.Teleport(transform.TransformPoint(local), transform.rotation * Random.rotation);
            item.Body.WakeUp();
            item.Body.angularVelocity = transform.TransformDirection(Random.insideUnitSphere * 3f);
        }

        /// <summary>
        /// 10-26(2) 붓기 뒤 손보기(4번까지): 더미 비탈을 굴러 안전 구역 밖으로 나간 목표물, 엎어진 채 다른 것 위에 올라 더미 꼭대기(PileTopY)보다
        /// 2cm 넘게 솟은 것은 구역 안 무작위 자리 여러 곳 중 표면이 가장 낮은 곳에 덜 기울여(±25°) 낮게 다시 떨어뜨린다. 예열 시뮬레이션 안에서 부른다
        /// </summary>
        void FixPoured(IList<ClawItem> items)
        {
            var s = Spec;
            for (int pass = 0; pass < 4; pass++)
            {
                int n = 0;
                Physics.SyncTransforms();
                foreach (var item in items)
                {
                    if (item == null || item.Held || item.Body.isKinematic) continue;
                    var lp = transform.InverseTransformPoint(item.transform.position);
                    float top = transform.InverseTransformPoint(new Vector3(item.transform.position.x, item.TopWorldY, item.transform.position.z)).y;
                    bool outZone = item.IsTarget && !ClawLayout.InTargetSafeZone(lp.x, lp.z, 0.04f);
                    bool tooHigh = top > ClawLayout.PileTopY + 0.02f;
                    if (!outZone && !tooHigh) continue;
                    float he = item.HalfExtent;
                    float clear = Mathf.Max(s.CellClear, he + 0.01f);
                    // 무작위 자리 6곳 중 표면이 가장 낮은 곳(골) — 다른 것 위에 또 올라 솟지 않게
                    Vector2 xz = Vector2.zero; float y = float.MaxValue;
                    for (int k = 0; k < 6 + pass * 4; k++) // 다시 할수록 더 많이 찾아 본다
                    {
                        var cand = item.IsTarget ? RandomSafeXZ(clear) : RandomPileXZ(clear);
                        float cy = SurfaceCenterY(cand.x, cand.y, he, item);
                        if (cy < y) { y = cy; xz = cand; }
                    }
                    y += 0.005f;
                    item.Teleport(transform.TransformPoint(new Vector3(xz.x, y, xz.y)), transform.rotation * Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)));
                    item.Body.linearVelocity = Vector3.zero;
                    item.Body.angularVelocity = Vector3.zero;
                    n++;
                }
                if (n == 0) break;
                Physics.SyncTransforms();
                for (int k = 0; k < 50; k++) Physics.Simulate(0.02f);
            }
        }

        /// <summary>마지막 배치에서 만든 셀 수(셀프테스트·경고용)</summary>
        public int CellCount => cells0.Count + cells1.Count;
        /// <summary>10-26 마지막 배치의 0층 칸 중 목표물 안전 구역 칸 수(로그·셀프테스트)</summary>
        public int LastSafeCells { get; private set; }

        /// <summary>안전 구역 칸을 앞으로(각 묶음 안 순서 유지 — 안정 분할)</summary>
        static void SafeFirst(List<Vector3> cells)
        {
            safeTmp.Clear();
            restTmp.Clear();
            foreach (var p in cells) (ClawLayout.InTargetSafeZone(p.x, p.z) ? safeTmp : restTmp).Add(p);
            cells.Clear();
            cells.AddRange(safeTmp);
            cells.AddRange(restTmp);
        }
        static readonly List<Vector3> safeTmp = new(), restTmp = new();
        public int LowerCellCount => cells0.Count;

        /// <summary>승격(받침 캡슐 → 같은 자리 진짜 캡슐)에 남겨 두는 풀 몫(10-20). 배치 개수 합 상한 = 풀 − 이 값</summary>
        public const int PromotionReserve = 30;

        /// <summary>한 층 셀 수(물리 질의 없이 격자 개수만 — 받침 더미일 때 배치 개수 상한)</summary>
        public int SingleLayerCellCount
        {
            get
            {
                var s = Spec;
                float step = s.CellSize, clear = s.CellClear;
                int nx = Mathf.Max(1, Mathf.FloorToInt(2f * (ClawLayout.HalfX - clear) / step) + 1);
                int nz = Mathf.Max(1, Mathf.FloorToInt(2f * (ClawLayout.HalfZ - clear) / step) + 1);
                float x0 = -(nx - 1) * step * 0.5f, z0 = ClawLayout.CenterZ - (nz - 1) * step * 0.5f;
                int n = 0;
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                        if (!(x0 + step * i - clear < ClawLayout.ChuteWallX && z0 + step * j - clear < ClawLayout.ChuteWallZ)) n++;
                return n;
            }
        }

        /// <summary>
        /// 배치 셀(치수 파생): 0층 = 내부(벽 − 반 폭 − 지터)에 CellSize 격자, 1층 = 반 칸 오프셋·가장 큰 높이 위.
        /// 배출구 울타리를 걸치거나 배출구 칸 안인 셀은 제외. 최대 층 수는 spec(기본 2).
        /// </summary>
        void BuildCells(ICollection<ClawItem> placing = null)
        {
            cells0.Clear();
            cells1.Clear();
            var s = Spec;
            float step = s.CellSize, clear = s.CellClear;
            float hx = ClawLayout.HalfX - clear;
            float hz = ClawLayout.HalfZ - clear;
            int nx = Mathf.Max(1, Mathf.FloorToInt(2f * hx / step) + 1);
            int nz = Mathf.Max(1, Mathf.FloorToInt(2f * hz / step) + 1);
            // 10-20 받침 더미가 있으면 실물은 그 위 한 층만(더미 꼭대기 PileTopY 안) — 셀 바닥 높이 = 그 자리 받침 표면(물리 질의, 실물 아이템은 무시)
            bool onBed = BedActive;
            int layers = onBed ? 1 : Mathf.Clamp(s.maxPlaceLayers, 1, 2);
            float probeR = s.MaxItemWidth * 0.4f;
            int bedMask = onBed ? (RealTopCount > 0 ? LayerMask.GetMask("ClawBed", "ClawMachine", "ClawItem") : LayerMask.GetMask("ClawBed", "ClawMachine")) : 0;
            if (onBed) Physics.SyncTransforms();
            for (int layer = 0; layer < layers; layer++)
            {
                var list = layer == 0 ? cells0 : cells1;
                int odd = layer & 1;
                int cx = Mathf.Max(1, nx - odd), cz = Mathf.Max(1, nz - odd);
                float x0 = -(cx - 1) * step * 0.5f;
                float z0 = ClawLayout.CenterZ - (cz - 1) * step * 0.5f;
                float y = ClawLayout.FloorY + layer * (s.MaxItemHeight + 0.01f);
                for (int i = 0; i < cx; i++)
                    for (int j = 0; j < cz; j++)
                    {
                        var p = new Vector3(x0 + step * i, y, z0 + step * j);
                        if (p.x - clear < ClawLayout.ChuteWallX && p.z - clear < ClawLayout.ChuteWallZ) continue;
                        if (onBed) p.y = SurfaceCenterY(p.x, p.z, probeR, null, bedMask, placing) - probeR;
                        list.Add(p);
                    }
            }
        }

        /// <summary>배출구 기둥 밖 무작위 (x, z) — 벽·기둥에서 clear만큼 안쪽</summary>
        static Vector2 RandomPileXZ(float clear)
        {
            float x = Random.Range(-ClawLayout.HalfX + clear, ClawLayout.HalfX - clear);
            float zMin = x < ClawLayout.ChuteWallX + clear ? ClawLayout.ChuteWallZ + clear : ClawLayout.FrontZ + clear;
            return new Vector2(x, Random.Range(zMin, ClawLayout.BackZ - clear));
        }

        /// <summary>목표물 안전 구역 안 무작위 (x, z) — 몇 번 뽑아 안 되면 배출구 기둥 밖 아무 곳(구역이 비정상적으로 좁을 때)</summary>
        static Vector2 RandomSafeXZ(float clear)
        {
            for (int k = 0; k < 24; k++)
            {
                var xz = new Vector2(Random.Range(ClawLayout.CarriageMinX, ClawLayout.CarriageMaxX), Random.Range(ClawLayout.CarriageMinZ, ClawLayout.CarriageMaxZ));
                if (ClawLayout.InTargetSafeZone(xz.x, xz.y)) return xz;
            }
            return RandomPileXZ(clear);
        }

        /// <summary>셀이 모자랄 때 초과분 낙하 지점 — 배출구 기둥 밖(목표물은 안전 구역), 더미 꼭대기 위·쉬는 발끝 아래 공중(배치 직후 예열 시뮬레이션이 떨어뜨린다)</summary>
        Vector3 OverflowPoint(bool target)
        {
            var s = Spec;
            var xz = target ? RandomSafeXZ(s.CellClear) : RandomPileXZ(s.CellClear);
            float he = s.MaxItemHeight * 0.5f;
            float y = Mathf.Min(ClawLayout.PileTopY + he + 0.02f, ClawLayout.IdleTipY - he - 0.02f);
            return new Vector3(xz.x, Mathf.Max(y, ClawLayout.FloorY + s.MaxItemHeight), xz.y);
        }

        int surfaceMask;
        readonly RaycastHit[] surfaceHits = new RaycastHit[16];
        int SurfaceMask
        {
            get
            {
                if (surfaceMask == 0) surfaceMask = LayerMask.GetMask("ClawItem", "ClawMachine", "ClawBed");
                return surfaceMask != 0 ? surfaceMask : ~0;
            }
        }

        /// <summary>
        /// 부스 로컬 (x, z)에 반지름 r 공을 뚜껑 아래에서 떨어뜨렸을 때 처음 닿는 자리의 공 중심 로컬 높이 — 아이템·받침·기계 바닥(exclude 제외).
        /// 아무것도 안 닿으면 바닥 + r. (x, z)는 배출구 기둥·벽에서 r 넘게 떨어진 자리여야 한다(시작부터 겹친 콜라이더는 무시)
        /// </summary>
        public float SurfaceCenterY(float x, float z, float r, ClawItem exclude = null, int mask = 0, ICollection<ClawItem> excludeSet = null)
        {
            var from = transform.TransformPoint(new Vector3(x, ClawLayout.LidY - r - 0.005f, z));
            var dir = transform.TransformDirection(Vector3.down);
            float maxDist = ClawLayout.LidY - ClawLayout.FloorY;
            int n = Physics.SphereCastNonAlloc(from, r, dir, surfaceHits, maxDist, mask != 0 ? mask : SurfaceMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = surfaceHits[i];
                if (h.distance <= 0f || h.distance >= best || h.collider == null) continue;
                if (exclude != null || excludeSet != null)
                {
                    var hi = h.collider.GetComponentInParent<ClawItem>();
                    if (hi != null && (hi == exclude || (excludeSet != null && excludeSet.Contains(hi)))) continue;
                }
                best = h.distance;
            }
            if (best == float.MaxValue) return ClawLayout.FloorY + r;
            return transform.InverseTransformPoint(from + dir * best).y;
        }

        static void Shuffle<T>(IList<T> list) => ShuffleRange(list, 0, list.Count);

        /// <summary>[from, to) 구간만 섞기</summary>
        static void ShuffleRange<T>(IList<T> list, int from, int to)
        {
            for (int i = to - 1; i > from; i--)
            {
                int j = Random.Range(from, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static int CountBowls(IList<ClawItem> items)
        {
            int n = 0;
            foreach (var it in items) if (it != null && it.IsTarget) n++;
            return n;
        }

        /// <summary>
        /// 이탈·침입 복구 지점(월드) — 배출구 기둥 밖 무작위 자리의 더미 표면 바로 위(10-20: 더미가 높아져 예전 바닥+0.25 공중은 더미 속).
        /// 쉬는 집게 발끝보다 높지 않게. 10-26 목표물(그릇·키링)은 집게가 닿는 안전 구역 안(ClawLayout.InTargetSafeZone)
        /// </summary>
        public Vector3 RespawnPoint(ClawItem item = null)
        {
            var s = Spec;
            float he = item != null ? item.HalfExtent : s.MaxItemHeight * 0.5f;
            float clear = Mathf.Max(s.CellClear, he + 0.01f);
            var xz = item != null && item.IsTarget ? RandomSafeXZ(clear) : RandomPileXZ(clear);
            float y = SurfaceCenterY(xz.x, xz.y, he, item) + 0.01f;
            y = Mathf.Clamp(y, ClawLayout.FloorY + he, Mathf.Max(ClawLayout.FloorY + he, ClawLayout.IdleTipY - he - 0.02f));
            return transform.TransformPoint(new Vector3(xz.x, y, xz.y));
        }

        /// <summary>아이템을 더미 위 무작위 자리로 옮긴다(이탈·배출구 침입 복구, 배출구 위에서 놓친 것)</summary>
        public void RespawnOnPile(ClawItem item)
        {
            if (item == null) return;
            item.Teleport(RespawnPoint(item), Quaternion.identity);
            item.Body.WakeUp();
        }

        public bool IsInside(Vector3 world, float margin)
        {
            var p = transform.InverseTransformPoint(world);
            return Mathf.Abs(p.x) <= ClawLayout.HalfX + margin
                && p.z >= ClawLayout.FrontZ - margin && p.z <= ClawLayout.BackZ + margin
                && p.y >= ClawLayout.FloorY - margin && p.y <= ClawLayout.LidY + margin;
        }

        /// <summary>배출구 칸(기둥 안) 판정 — 10-20부터 뚜껑까지 어느 높이든(벽에 기댄 정상 아이템은 중심이 벽 밖이라 오검출 없음)</summary>
        public bool IsInChuteCell(Vector3 world, float halfExtent)
        {
            var p = transform.InverseTransformPoint(world);
            return p.x < ClawLayout.ChuteWallX && p.z < ClawLayout.ChuteWallZ
                && p.y < ClawLayout.LidY + halfExtent;
        }

        /// <summary>배출구 기둥에 조금이라도 걸치는지(반 크기 halfExtent) — 들고 있던 것을 그 위에서 놓칠 때 기둥과 겹쳐 엉뚱한 쪽으로 밀려나지 않게</summary>
        public bool OverChuteColumn(Vector3 world, float halfExtent)
        {
            var p = transform.InverseTransformPoint(world);
            return p.x - halfExtent < ClawLayout.ChuteWallX && p.z - halfExtent < ClawLayout.ChuteWallZ;
        }

        /// <summary>
        /// 잡을 대상 하나(10-15 미션형): 부스 로컬 (cx, cz) 중심 수평 반경 radius 안, 윗면이 minTopWorldY 이상인 활성·비흡착 아이템 중
        /// **수평으로 가장 가까운 것**(거의 같으면 더 높은 것). 없으면 null. 가이드가 가리키는 것과 실제로 잡히는 것이 같도록 헤드가 둘 다 이걸 쓴다.
        /// </summary>
        public ClawItem PickUnder(Vector2 centerLocalXZ, float radius, float minTopWorldY)
        {
            if (pool == null) return null;
            ClawItem best = null;
            float bestD = float.MaxValue, bestTop = float.MinValue, r2 = radius * radius;
            var all = pool.All;
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (!it.gameObject.activeSelf || it.Held) continue;
                float top = it.TopWorldY;
                if (top < minTopWorldY) continue;
                var lp = transform.InverseTransformPoint(it.transform.position);
                float dx = lp.x - centerLocalXZ.x, dz = lp.z - centerLocalXZ.y, d = dx * dx + dz * dz;
                if (d > r2) continue;
                if (d < bestD - 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && top > bestTop))
                {
                    best = it; bestD = d; bestTop = top;
                }
            }
            // 10-20 받침 조각도 같은 기준(수평 가장 가까움 · 윗면이 닿는 깊이 안 = 드러난 것만)으로 겨룬다 — 이기면 그 자리 진짜 상품(10-26 조각 종류)으로 승격.
            // 가이드(매 프레임 조준)와 실제 잡기(Dwell)가 모두 이 함수라 발광·폭탄 판정·잡기가 그대로 돈다
            if (BedActive)
            {
                int bestPiece = -1;
                for (int i = 0; i < bed.Count; i++)
                {
                    if (bed.IsHidden(i)) continue;
                    var lp = bed.LocalPosition(i);
                    float dx = lp.x - centerLocalXZ.x, dz = lp.z - centerLocalXZ.y, d = dx * dx + dz * dz;
                    if (d > r2) continue;
                    float top = bed.TopWorldY(i);
                    if (top < minTopWorldY) continue;
                    if (d < bestD - 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && top > bestTop))
                    {
                        bestPiece = i; bestD = d; bestTop = top;
                    }
                }
                if (bestPiece >= 0)
                {
                    var proxy = Promote(bestPiece);
                    if (proxy != null) best = proxy;
                }
            }
            if (best != null && best.BedIndex >= 0) lastPicked[best] = Time.time;
            return best;
        }

        /// <summary>풀의 전체 아이템(활성·비활성) — 10-22 물리 집게가 집게 안에 딸려 온 것을 찾을 때</summary>
        public IReadOnlyList<ClawItem> PoolItems => pool != null ? pool.All : null;

        // ─ 승격(10-20): 받침 조각 → 같은 자리·같은 색 진짜 캡슐(풀) ─────────────
        /// <summary>승격본(되돌리기 후보) — 제자리에 잠든 채 0.5초 안 겨눠지면 다시 받침으로. 움직이면(폭발·밀림) 리셋까지 그냥 실물</summary>
        readonly List<ClawItem> promoted = new(32);
        readonly Dictionary<ClawItem, float> lastPicked = new(32);
        const float DemoteAfterSec = 0.5f, DemoteMoveTol = 0.01f;
        /// <summary>폭발 한 번에 승격하는 폭탄 주변 받침 윗면 조각 수 상한(움푹 파인 자리)</summary>
        public const int BlastPromoteMax = 6;
        /// <summary>폭발 승격은 풀 여유가 이만큼 넘을 때만(조준 승격 몫을 남김)</summary>
        const int BlastPoolKeep = 10;
        public int PromotedTotal { get; private set; }
        public int DemotedTotal { get; private set; }
        public int BlastPromotedTotal { get; private set; }
        /// <summary>지금 활성인 승격본(제자리든 움직였든) 수 — 배치 개수 비교에서 뺀다</summary>
        public int PromotedActive
        {
            get
            {
                int n = 0;
                if (pool != null) foreach (var it in pool.All) if (it.gameObject.activeSelf && it.BedIndex >= 0) n++;
                return n;
            }
        }

        /// <summary>잡기 시작 때 헤드가 부른다 — 겨눈 승격본을 하강·잡기가 끝날 때까지 되돌리지 않게(그동안 PickUnder가 불리지 않는다)</summary>
        public void HoldPromoted(ClawItem it, float seconds)
        {
            if (it != null && it.BedIndex >= 0) lastPicked[it] = Time.time + Mathf.Max(0f, seconds);
        }

        int FreePoolCount()
        {
            if (pool == null) return 0;
            int n = 0;
            foreach (var it in pool.All) if (!it.gameObject.activeSelf) n++;
            return n;
        }

        ClawItem Promote(int i)
        {
            if (pool == null || bed == null || FreePoolCount() == 0) return null;   // 풀이 다 찼으면 승격 안 함(경고 로그 반복 방지)
            var it = pool.Get();
            if (it == null) return null;
            it.SetKind(bed.PieceKind(i), 0); // 10-26 받침 = 상품 3종(예전 캡슐 색)
            bed.Hide(i);
            it.BedIndex = i;
            it.Teleport(bed.WorldPosition(i), bed.WorldRotation(i));
            it.Body.Sleep();
            promoted.Add(it);
            lastPicked[it] = Time.time;
            PromotedTotal++;
            return it;
        }

        /// <summary>5스텝마다: 배달된 것(풀로 감)·움직인 것은 목록에서 빼고, 제자리에 잠든 채 안 겨눠진 것은 되돌린다</summary>
        void SweepPromoted()
        {
            for (int k = promoted.Count - 1; k >= 0; k--)
            {
                var it = promoted[k];
                int i = it.BedIndex;
                if (!it.gameObject.activeSelf || i < 0) { promoted.RemoveAt(k); lastPicked.Remove(it); continue; } // 배출됨 — 조각은 숨긴 채(상품이 나갔다)
                if (it.Held) continue;
                bool home = (it.Body.position - bed.WorldPosition(i)).sqrMagnitude < DemoteMoveTol * DemoteMoveTol;
                if (!home) { promoted.RemoveAt(k); lastPicked.Remove(it); continue; } // 움직였다 — 리셋까지 그냥 실물
                if (!it.Body.IsSleeping() && it.Body.linearVelocity.sqrMagnitude > 1e-4f) continue;
                if (Time.time - (lastPicked.TryGetValue(it, out float t) ? t : 0f) < DemoteAfterSec) continue;
                it.ReleaseToPool();
                bed.Show(i);
                promoted.RemoveAt(k);
                lastPicked.Remove(it);
                DemotedTotal++;
            }
        }

        /// <summary>폭발 중심(로컬) 주변 받침 윗면 조각을 승격 — 같이 날아가 움푹 파인 자리가 생긴다</summary>
        void PromoteNear(Vector3 centerLocal, float radius, int max)
        {
            if (!BedActive || max <= 0) return;
            float r2 = radius * radius, d = bed.PieceRadius * 2f;
            var picks = new List<(float dist, int index)>();
            for (int i = 0; i < bed.Count; i++)
            {
                if (bed.IsHidden(i)) continue;
                var p = bed.LocalPosition(i);
                float dx = p.x - centerLocal.x, dz = p.z - centerLocal.z, dd = dx * dx + dz * dz;
                if (dd > r2) continue;
                if (bed.LocalTop(i) < bed.LocalTopNear(p.x, p.z, d) - bed.PieceRadius) continue; // 윗면 층만
                picks.Add((dd, i));
            }
            picks.Sort((a, b) => a.dist.CompareTo(b.dist));
            int n = 0;
            foreach (var (_, i) in picks)
            {
                if (n >= max || FreePoolCount() <= BlastPoolKeep) break;
                if (Promote(i) != null) { n++; BlastPromotedTotal++; }
            }
        }

        /// <summary>
        /// 리셋·재배치 — 받침 조각 전부 복원 + 승격본(제자리든 움직였든) 풀로. 들고 있는 승격본은 헤드가 처리하므로 두고 그 조각만 숨긴 채
        /// </summary>
        public void ResetPile()
        {
            promoted.Clear();
            lastPicked.Clear();
            if (bed == null) return;
            bed.RestoreAll();
            if (pool == null) return;
            foreach (var it in pool.All)
            {
                if (!it.gameObject.activeSelf || it.BedIndex < 0) continue;
                if (it.Held) bed.Hide(it.BedIndex);
                else it.ReleaseToPool();
            }
        }

        /// <summary>
        /// 재정렬(4차): BoothRoot 변환 델타를 월드 공간 아이템에 그대로 적용해 파일 형태를 유지한다.
        /// 들린(kinematic) 아이템은 transform만(다음 프레임 헤드가 덮어씀), 동적 아이템은 위치·회전·속도 회전 후 자던 것은 다시 Sleep.
        /// 호출자가 Physics.SyncTransforms()를 한 번 부른다.
        /// </summary>
        public int TransformActiveItems(Matrix4x4 delta)
        {
            if (pool == null) return 0;
            var rot = delta.rotation;
            int n = 0;
            foreach (var item in pool.All)
            {
                if (!item.gameObject.activeSelf) continue;
                var t = item.transform;
                var pos = delta.MultiplyPoint3x4(t.position);
                var r = rot * t.rotation;
                var rb = item.Body;
                if (item.Held || rb == null || rb.isKinematic)
                {
                    t.SetPositionAndRotation(pos, r);
                    n++;
                    continue;
                }
                bool sleeping = rb.IsSleeping();
                var v = rb.linearVelocity;
                var w = rb.angularVelocity;
                t.SetPositionAndRotation(pos, r);
                rb.position = pos;
                rb.rotation = r;
                rb.linearVelocity = rot * v;
                rb.angularVelocity = rot * w;
                if (sleeping) rb.Sleep();
                n++;
            }
            return n;
        }

        // ─ 10-28 기계 다가오기 동안 상품 고정 ─────────────────────────
        readonly List<ClawItem> transitFrozen = new(64);
        bool transitFreeze;
        /// <summary>다가오기 이동 중 고정된 상태(셀프테스트)</summary>
        public bool TransitFrozen => transitFreeze;

        /// <summary>
        /// 10-28 기계 다가오기(BoothApproach): 이동 중엔 활성 상품(쥔 것 제외)을 kinematic + 보간 없음으로 — 루트와 같은 델타로 transform을 옮긴다
        /// (보간이 켜져 있으면 한 물리 스텝 뒤처져 보이고, 물리로 두면 움직이는 바닥·벽에 밀려 튄다). 끄면 원래대로(보간 켬)·속도 0·잠재움. 멱등.
        /// 받침 맨 윗층 예열 고정(frozenTop)과는 다른 목록
        /// </summary>
        public void FreezeForTransit(bool on)
        {
            if (on == transitFreeze) return;
            transitFreeze = on;
            if (on)
            {
                transitFrozen.Clear();
                if (pool == null) return;
                foreach (var it in pool.All)
                {
                    if (!it.gameObject.activeSelf || it.Held || it.Body == null || it.Body.isKinematic) continue;
                    it.Body.isKinematic = true;
                    it.Body.interpolation = RigidbodyInterpolation.None;
                    transitFrozen.Add(it);
                }
                return;
            }
            foreach (var it in transitFrozen)
            {
                if (it == null || it.Body == null) continue;
                it.Body.interpolation = RigidbodyInterpolation.Interpolate;
                if (it.Held) continue; // 혹시 그 사이 쥐었으면 헤드가 관리
                it.Body.isKinematic = false;
                if (!it.gameObject.activeSelf) continue;
                it.Body.linearVelocity = Vector3.zero;
                it.Body.angularVelocity = Vector3.zero;
                it.Body.Sleep();
            }
            transitFrozen.Clear();
        }

        // ─ 폭발(10-15: 집게가 폭탄에 닿는 순간) ───────────────────────
        /// <summary>
        /// 기계 안 물체를 크게 흩뜨린다. 아이템마다 속도를 직접 지정: 중심에서 멀수록 약하지만(최소 55%) 기계 전체가 움직이고,
        /// 위로 튀는 높이는 캐리지 아래(RailY − 0.1)까지, 옆으로는 ExplodeSpread m/s + 접선 성분, 회전은 크게.
        /// 헤드 바로 아래(반경 HeadRadius)는 옆으로 강하게·위로 약하게 — 집게 시각을 관통해 보이지 않게. 반환 = 움직인 개수.
        /// </summary>
        public int Explode(Vector3 worldCenter, ClawItem exclude)
        {
            if (pool == null) return 0;
            var c = transform.InverseTransformPoint(worldCenter);
            PromoteNear(c, ClawLayout.HeadRadius * 1.2f, BlastPromoteMax); // 10-20 폭탄 바로 옆 받침 윗면도 같이 날아가게
            float diag = Mathf.Sqrt(ClawLayout.InnerX * ClawLayout.InnerX + ClawLayout.InnerZ * ClawLayout.InnerZ);
            float ceiling = ClawLayout.RailY - 0.1f;
            float apex = Mathf.Max(0.05f, ExplodeLift * (ceiling - ClawLayout.FloorY));
            float g = Mathf.Max(1f, -Physics.gravity.y);
            int moved = 0;
            var all = pool.All;
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it == exclude || !it.gameObject.activeSelf || it.Held || it.Body.isKinematic) continue;
                var p = transform.InverseTransformPoint(it.transform.position);
                var d = new Vector2(p.x - c.x, p.z - c.z);
                float dist = d.magnitude;
                Vector2 dir = dist > 0.02f ? d / dist : Random.insideUnitCircle.normalized;
                if (dir.sqrMagnitude < 0.5f) dir = Vector2.right;
                float fall = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(dist / diag));
                float rise = Mathf.Min(apex * fall * Random.Range(0.85f, 1.1f), ceiling - p.y);
                float vUp = Mathf.Sqrt(2f * g * Mathf.Max(0.02f, rise));
                float vLat = ExplodeSpread * fall * Random.Range(0.7f, 1.2f);
                var tangent = new Vector2(-dir.y, dir.x);
                Vector2 lat = dir * vLat + tangent * (Random.Range(-0.3f, 0.3f) * ExplodeSpread);
                if (dist < ClawLayout.HeadRadius)
                {
                    lat = dir * Mathf.Max(vLat, 1.2f * ExplodeSpread);
                    vUp *= 0.6f;
                }
                var rb = it.Body;
                rb.WakeUp();
                rb.linearVelocity = transform.TransformDirection(new Vector3(lat.x, vUp, lat.y));
                rb.angularVelocity = Random.insideUnitSphere * 12f;
                moved++;
            }
            explodedAt = Time.time;
            ExplosionCount++;
            return moved;
        }

        // ─ 매 물리 스텝: 속도 상한 + (5스텝마다) 이탈·배출구 칸·울타리 걸림 검사 ─────────────
        void FixedUpdate()
        {
            if (pool == null) return;
            bool check = (++stepCounter % 5) == 0;
            if (check && promoted.Count > 0) SweepPromoted();
            float cap = Time.time - explodedAt < ExplosionClampSec ? Mathf.Max(MaxItemSpeed, ExplosionMaxSpeed) : MaxItemSpeed;
            float maxSq = cap * cap;
            var all = pool.All;
            for (int i = 0; i < all.Count; i++)
            {
                var item = all[i];
                if (!item.gameObject.activeSelf || item.Held) continue;
                var rb = item.Body;
                if (rb.isKinematic) continue;

                // 배출구 칸 안전망(4차) — 잠든 아이템도 검사. 10-20부터 칸이 뚜껑까지 막힌 기둥이라 걸리면 이상(경고)
                if (check && IsInChuteCell(rb.position, item.HalfExtent))
                {
                    ChuteIntrusionCount++;
                    RespawnOnPile(item);
                    if (ChuteIntrusionCount % 20 == 1) Debug.LogWarning($"[ClawPhys] chute → pile #{ChuteIntrusionCount} ({item.name} {item.ItemKind})");
                    continue;
                }
                if (rb.IsSleeping()) continue;

                var v = rb.linearVelocity;
                float sq = v.sqrMagnitude;
                if (sq > maxSq)
                {
                    rb.linearVelocity = v * (cap / Mathf.Sqrt(sq));
                    sq = maxSq;
                }
                if (StressRunning && sq > MaxSpeedSeen * MaxSpeedSeen) MaxSpeedSeen = Mathf.Sqrt(sq);

                if (check && !IsInside(rb.position, 0.05f))
                {
                    EscapeCount++;
                    var lp = transform.InverseTransformPoint(rb.position);
                    var lv = transform.InverseTransformDirection(rb.linearVelocity);
                    RespawnOnPile(item);
                    Debug.LogWarning($"[ClawPhys] escape recovered #{EscapeCount} ({item.name} {item.ItemKind}) at ({lp.x:F2},{lp.y:F2},{lp.z:F2}) v({lv.x:F1},{lv.y:F1},{lv.z:F1})");
                }
            }
        }

        // ─ 스트레스 테스트(에디터 메뉴 32 / 셀프테스트) ───────────────────
        /// <summary>폭발 n회(0.3초 간격, 무작위 위치) — 게임과 같은 Explode 경로·값</summary>
        public void StartStressTest(int explosions) => StartCoroutine(StressRoutine(explosions));

        IEnumerator StressRoutine(int explosions)
        {
            StressRunning = true;
            MaxSpeedSeen = 0f;
            int escapesBefore = EscapeCount;
            for (int i = 0; i < explosions; i++)
            {
                var local = new Vector3(Random.Range(-ClawLayout.HalfX * 0.8f, ClawLayout.HalfX * 0.8f), ClawLayout.FloorY + 0.05f,
                                        ClawLayout.CenterZ + Random.Range(-ClawLayout.HalfZ * 0.8f, ClawLayout.HalfZ * 0.8f));
                Explode(transform.TransformPoint(local), null);
                yield return new WaitForSeconds(0.3f);
            }
            yield return new WaitForSeconds(3f);
            int total = 0, asleep = 0, above = 0;
            foreach (var item in pool.All)
            {
                if (!item.gameObject.activeSelf || item.Held) continue;
                total++;
                if (item.Body.IsSleeping() || item.Body.linearVelocity.sqrMagnitude < 0.0004f) asleep++;
                if (transform.InverseTransformPoint(item.transform.position).y > ClawLayout.LidY) above++;
            }
            StressRunning = false;
            LastStressReport = $"[ClawStress] explosions={explosions} escapes={EscapeCount - escapesBefore} maxSpeed={MaxSpeedSeen:F2}m/s (clamp {ExplosionMaxSpeed:F1}) asleep={asleep}/{total} aboveLid={above}";
            Debug.Log(LastStressReport);
        }
    }
}
