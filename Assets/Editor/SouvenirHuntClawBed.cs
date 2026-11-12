using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SouvenirHunt;
using Random = UnityEngine.Random;

// 받침 더미 굽기(10-20, 사용자 "4~5층 높이만큼" → 결정 "아래층 고정 + 위층 실물"). SouvenirHuntSetup partial — 메뉴 30이 BuildBasketColliders 다음에 부른다.
// 10-26(사용자 "인형뽑기 안의 모든 상품을 큐브/그릇1/그릇2로, 비율 1:1:1 — 캡슐 더미까지 전부"): 캡슐 구 대신 진짜 상품 모양으로 쌓는다.
//   1) 고정 시드로 임시 상품(세라믹 그릇·유리 그릇 = 볼록 헐, 키링 = 큐브 상자 — 실물과 같은 충돌체·폭)을 육각 격자로 여러 층 띄워 놓고
//      실제 바스켓 콜라이더 안에서 물리로 떨어뜨려 쌓는다(에디터 Physics.Simulate). 종류는 세 개씩 묶어 섞은 순서 = 1:1:1.
//   2) 받침 윗면(ClawLayout.BedTopY — 그 위 실물 한 층이 더미 꼭대기 PileTopY 안에 들어가게)보다 높은 것은 버린다. 배출구 기둥 콜라이더 때문에 칸 안에는 안 쌓인다.
//   3) 보이는 조각만 그린다: 윗면에서 깊이 BedShellDepth×폭 안 + 앞·좌·우 유리와 배출구 칸막이에 닿는 것. 충돌은 전 조각(정적 — 실물과 같은 모양).
//   4) 메시 1개(종류별 하위 메시 3개 — 실물 재질, 정점을 묶어 줄인 가벼운 모양, 윗면부터) + ClawBed 데이터(조각 자리·방향·종류·윗면 높이·인덱스 범위).
// 배율(SupClawScale)·아이템 크기가 바뀌면 메뉴 30을 다시 돌린다(셀프테스트가 지문을 확인).
public static partial class SouvenirHuntSetup
{
    const string BedMeshPath = ClawItemFolder + "/Claw_PileBed.asset";
    const int BedSeed = 20261026;
    const float BedShellDepth = 2.0f;            // 윗면 아래 이 배수(폭) 안이면 그린다 — 맨 윗층이 진짜 상품이라(숨김) 그 아래 층이 보이게
    const int BedSettleSteps = 250;              // 5초
    static readonly ClawItem.Kind[] BedKinds = { ClawItem.Kind.Ceramic, ClawItem.Kind.Glass, ClawItem.Kind.Keyring };
    // 받침 그림용 단순화 격자(정규화 메시 한 변 칸 수) — 실물 메시는 1,100~2,800 정점이라 수백 조각을 그대로 그리면 글래스에 무겁다
    static readonly Dictionary<ClawItem.Kind, int> BedClusterCells = new() { { ClawItem.Kind.Ceramic, 8 }, { ClawItem.Kind.Glass, 8 }, { ClawItem.Kind.Keyring, 7 } };

    static ClawBed BakeClawBed(GameObject basketGo, Transform boothRoot, ClawItemSpec spec, PhysicsMaterial phys, int itemLayer, int bedLayer, int renderLayer, StringBuilder report)
    {
        float wMax = BedKinds.Max(k => spec.Width(k)), wAvg = BedKinds.Average(k => spec.Width(k)), hAvg = BedKinds.Average(k => spec.Height(k));
        float floorY = ClawLayout.FloorY, topY = ClawLayout.BedTopY(spec.MaxItemHeight);
        if (!ClawLayout.CabinetSup || topY - floorY < hAvg || bedLayer < 0)
        {
            report.AppendLine($"  · 받침 더미 없음 (높이 예산 {topY - floorY:F3} < 상품 평균 높이 {hAvg:F3} 또는 레이어 없음)");
            return null;
        }
        // 실물 메시·헐·재질(NewScene 뒤라 경로로 다시 잡는다)
        Mesh LoadMesh(string n) => AssetDatabase.LoadAssetAtPath<Mesh>($"{ClawItemFolder}/{n}.asset");
        var visual = new Dictionary<ClawItem.Kind, Mesh> { { ClawItem.Kind.Ceramic, LoadMesh("Claw_CeramicBowl") }, { ClawItem.Kind.Glass, LoadMesh("Claw_GlassBowl") }, { ClawItem.Kind.Keyring, LoadMesh("Claw_KeyringMesh") } };
        var hull = new Dictionary<ClawItem.Kind, Mesh> { { ClawItem.Kind.Ceramic, LoadMesh("Claw_CeramicBowl_Hull") }, { ClawItem.Kind.Glass, LoadMesh("Claw_GlassBowl_Hull") } };
        var mats = new[] { "Claw_CeramicBowl", "Claw_GlassBowl", "Claw_Keyring" }.Select(n => AssetDatabase.LoadAssetAtPath<Material>($"{ClawItemFolder}/{n}.mat")).ToArray();
        if (visual.Values.Any(m => m == null) || hull.Values.Any(m => m == null) || mats.Any(m => m == null))
            throw new Exception("상품 메시·헐·재질(Assets/Settings/ClawItems/Claw_CeramicBowl·GlassBowl·Keyring*) 없음 — BuildClawItemAssets 먼저");

        var basket = basketGo.transform;
        var randomState = Random.state;
        Random.InitState(BedSeed);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Collider AddShape(GameObject go, ClawItem.Kind k)
        {
            Collider c;
            if (k == ClawItem.Kind.Keyring) { var bc = go.AddComponent<BoxCollider>(); bc.size = Vector3.one; c = bc; }
            else { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = hull[k]; mc.convex = true; c = mc; }
            c.contactOffset = spec.contactOffset;
            if (phys != null) c.sharedMaterial = phys;
            return c;
        }

        // 1) 임시 상품 — 10-26(2) 사용자 "너무 부자연스러워, 좀 더 자연스럽게 쌓이고 좀 더 섞이게": 아무 방향으로 돌린 채(그릇이 엎어지고 눕고, 큐브가 기운다)
        //    돌려도 서로 닿지 않는 간격(가장 큰 상품을 감싸는 지름 D)의 육각 격자로 뚜껑 아래까지 띄워 떨어뜨리기를 차오를 때까지 여러 번 →
        //    예산 위로 나온 것 자르기 → 골 채우기(한 층 더 떨어뜨리고 자르기) 2번. 종류는 세 종류를 섞은 주머니(윗면이 1:1:1에 가깝게, 무늬 없이 섞임)
        var tempRoot = new GameObject("BedBakeTemp");
        tempRoot.transform.SetParent(basket, false);
        var temps = new List<(Rigidbody rb, Collider col, ClawItem.Kind kind)>();
        var bag = new List<ClawItem.Kind>();
        Dictionary<ClawItem.Kind, float> fillWeight = null; // 골 채우기 때 모자란 종류 쪽으로
        ClawItem.Kind NextKind()
        {
            if (fillWeight != null)
            {
                float total = fillWeight.Values.Sum(), r = Random.value * total;
                foreach (var kv in fillWeight) { r -= kv.Value; if (r <= 0f) return kv.Key; }
                return BedKinds[BedKinds.Length - 1];
            }
            if (bag.Count == 0)
            {
                // 세라믹 4·유리 4·키링 5 — 큐브는 키가 커서 예산 위로 잘려 나가는 일이 많아(4:4:4이면 맨 윗층 32·35·25) 하나 더 넣어 윗면이 1:1:1에 가깝게
                for (int r = 0; r < 4; r++) bag.AddRange(BedKinds);
                bag.Add(ClawItem.Kind.Keyring);
                for (int i = bag.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
            }
            var k = bag[bag.Count - 1];
            bag.RemoveAt(bag.Count - 1);
            return k;
        }
        float Dia(ClawItem.Kind k) => k == ClawItem.Kind.Keyring ? spec.Width(k) * 1.74f : Mathf.Sqrt(spec.Width(k) * spec.Width(k) + spec.Height(k) * spec.Height(k));
        float D = BedKinds.Max(Dia) * 1.03f, rowZ = D * 0.866f, hr = D * 0.5f;
        float xMin = -ClawLayout.HalfX + hr + 0.004f, xMax = ClawLayout.HalfX - hr - 0.004f;
        float zMin = ClawLayout.FrontZ + hr + 0.004f, zMax = ClawLayout.BackZ - hr - 0.004f;
        float lid = ClawLayout.LidY - 0.03f;
        int spawned = 0;
        void SpawnLayer(float y, int k)
        {
            float oz = (k % 2) * rowZ / 3f;
            int row = 0;
            for (float z = zMin + oz; z <= zMax; z += rowZ, row++)
                for (float x = xMin + ((row + k) % 2) * D * 0.5f; x <= xMax; x += D)
                {
                    if (x - hr < ClawLayout.ChuteWallX + 0.004f && z - hr < ClawLayout.ChuteWallZ + 0.004f) continue;
                    var kind = NextKind();
                    var go = new GameObject("t") { layer = itemLayer };
                    go.transform.SetParent(tempRoot.transform, false);
                    go.transform.localPosition = new Vector3(x + Random.Range(-0.012f, 0.012f), y + Random.Range(0f, 0.02f), z + Random.Range(-0.012f, 0.012f));
                    go.transform.localRotation = Random.rotation; // 아무 방향 — 엎어지고 눕고 기운 채 떨어진다
                    go.transform.localScale = Vector3.one * spec.Width(kind);
                    var col = AddShape(go, kind);
                    var rb = go.AddComponent<Rigidbody>();
                    spec.ConfigureBody(rb, kind);
                    rb.interpolation = RigidbodyInterpolation.None;
                    rb.angularVelocity = Random.insideUnitSphere * 4f; // 떨어지며 구른다
                    temps.Add((rb, col, kind));
                    spawned++;
                }
        }
        void Settle(int steps)
        {
            Physics.SyncTransforms();
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try { for (int i = 0; i < steps; i++) Physics.Simulate(0.02f); }
            finally { Physics.simulationMode = prevMode; }
        }
        float LocalTop(Collider c) => basket.InverseTransformPoint(c.bounds.max).y;
        int dropped = 0;
        void Trim()
        {
            Physics.SyncTransforms();
            for (int i = temps.Count - 1; i >= 0; i--)
                if (LocalTop(temps[i].col) > topY + 0.002f) { UnityEngine.Object.DestroyImmediate(temps[i].rb.gameObject); temps.RemoveAt(i); dropped++; }
        }
        // 윗면 평균 = 폭 크기 칸마다 가장 높은 조각 윗면의 평균(최고점 하나가 아니라 표면 전체가 예산에 닿았는지)
        float SurfaceMean()
        {
            Physics.SyncTransforms();
            var cell = new Dictionary<(int, int), float>();
            foreach (var t in temps)
            {
                var p = basket.InverseTransformPoint(t.rb.transform.position);
                var key = (Mathf.FloorToInt(p.x / wAvg), Mathf.FloorToInt(p.z / wAvg));
                float top = LocalTop(t.col);
                cell[key] = cell.TryGetValue(key, out float o) ? Mathf.Max(o, top) : top;
            }
            return cell.Count > 0 ? cell.Values.Average() : floorY;
        }
        float MaxTop() { Physics.SyncTransforms(); return temps.Count > 0 ? temps.Max(t => LocalTop(t.col)) : floorY; }
        // 차오를 때까지(표면 평균이 예산 + 반 높이 넘게) 뚜껑 아래 빈 공간에 층층이 띄워 떨어뜨림
        int layers = 0, rounds = 0;
        for (; rounds < 10; rounds++)
        {
            if (rounds > 0 && SurfaceMean() >= topY + hAvg * 0.5f) break;
            float cur = MaxTop();
            int k = 0;
            for (float y = cur + hr + 0.01f; y + hr < lid; y += D, k++) SpawnLayer(y, layers + k);
            if (k == 0) break;
            layers += k;
            Settle(BedSettleSteps);
        }
        Trim();
        // 윗면 종류 맞추기(사용자 "1:1:1" — 큐브는 키가 커서 잘려 나가고 납작한 세라믹은 남아 윗면이 기울었다): 윗면 띠에서 많은 종류를 넘친 만큼 빼고
        // 한 층을 모자란 종류 위주로 다시 떨어뜨려 골을 채운 뒤 자르기 — 3번
        string Rebalance()
        {
            Physics.SyncTransforms();
            float band = topY - hAvg * 0.9f;
            var surf = temps.Where(t => LocalTop(t.col) > band).ToList();
            if (surf.Count == 0) { fillWeight = null; return "-"; }
            var cnt = BedKinds.ToDictionary(k => k, k => surf.Count(t => t.kind == k));
            float mean = (float)surf.Count / BedKinds.Length;
            foreach (var k in BedKinds)
            {
                int excess = Mathf.FloorToInt((cnt[k] - mean) * 0.8f);
                if (excess <= 0) continue;
                foreach (var t in surf.Where(t => t.kind == k).OrderBy(_ => Random.value).Take(excess).ToList())
                {
                    UnityEngine.Object.DestroyImmediate(t.rb.gameObject);
                    temps.Remove(t);
                    dropped++;
                }
            }
            fillWeight = BedKinds.ToDictionary(k => k, k => Mathf.Max(0.1f, mean * 1.15f - cnt[k]));
            return string.Join("/", BedKinds.Select(k => cnt[k]));
        }
        int refills = 0;
        var balanceLog = new List<string>();
        for (; refills < 3; refills++)
        {
            balanceLog.Add(Rebalance());
            SpawnLayer(MaxTop() + hr + 0.01f, layers + refills);
            Settle(150);
            Trim();
        }
        fillWeight = null;
        float surfMean = SurfaceMean();
        int awake = temps.Count(t => !t.rb.IsSleeping());

        // 2) 자리 기록(바스켓 로컬) — 기계 밖·배출구 칸 안은 버림(기둥 콜라이더가 있어 정상이면 0)
        Physics.SyncTransforms();
        var pos = new List<Vector3>();
        var rot = new List<Quaternion>();
        var kinds = new List<ClawItem.Kind>();
        var tops = new List<float>();
        var invRot = Quaternion.Inverse(basket.rotation);
        foreach (var t in temps)
        {
            var p = basket.InverseTransformPoint(t.rb.transform.position);
            bool inside = Mathf.Abs(p.x) <= ClawLayout.HalfX && p.z >= ClawLayout.FrontZ && p.z <= ClawLayout.BackZ && p.y >= floorY - 0.01f;
            bool chute = p.x < ClawLayout.ChuteWallX && p.z < ClawLayout.ChuteWallZ;
            if (inside && !chute)
            {
                pos.Add(p); rot.Add(invRot * t.rb.transform.rotation); kinds.Add(t.kind);
                tops.Add(LocalTop(t.col) - p.y);
            }
            else dropped++;
        }
        UnityEngine.Object.DestroyImmediate(tempRoot);
        int n = pos.Count;

        // 3) 보이는 조각: 윗면(주변 폭 안 조각 윗면 최대)에서 깊이 BedShellDepth×폭 안, 또는 앞·좌·우 유리·칸막이에 닿음
        var shell = new bool[n];
        float maxTop = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            var p = pos[i];
            float top = p.y + tops[i], surf = top;
            for (int j = 0; j < n; j++)
            {
                var q = pos[j];
                float dx = q.x - p.x, dz = q.z - p.z;
                if (dx * dx + dz * dz < wAvg * wAvg) surf = Mathf.Max(surf, q.y + tops[j]);
            }
            float m = hr + wAvg * 0.25f;
            bool glass = p.x > ClawLayout.HalfX - m || p.x < -ClawLayout.HalfX + m || p.z < ClawLayout.FrontZ + m;
            bool guard = (p.x < ClawLayout.ChuteWallX + m && p.z < ClawLayout.ChuteWallZ + hr) || (p.z < ClawLayout.ChuteWallZ + m && p.x < ClawLayout.ChuteWallX + hr);
            shell[i] = surf - top < BedShellDepth * wAvg || glass || guard;
            maxTop = Mathf.Max(maxTop, top);
        }
        Random.state = randomState;

        // 4) 메시 — 종류별 하위 메시(실물 재질), 그 안에서 높은 것부터. 모양은 정점을 묶어 줄인 것
        var lod = new Dictionary<ClawItem.Kind, (List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> t)>();
        foreach (var k in BedKinds) lod[k] = ClusterMesh(visual[k], BedClusterCells[k]);
        var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
        var subTris = new List<int>[BedKinds.Length];
        var idxStart = new int[n]; var idxCount = new int[n]; var firstVert = new int[n];
        int offset = 0, rendered = 0, renderedTris = 0;
        for (int m = 0; m < BedKinds.Length; m++)
        {
            var k = BedKinds[m];
            var (bv, bn, buv, bt) = lod[k];
            float w = spec.Width(k);
            subTris[m] = new List<int>();
            foreach (int i in Enumerable.Range(0, n).Where(i => shell[i] && kinds[i] == k).OrderByDescending(i => pos[i].y))
            {
                int first = verts.Count;
                for (int q = 0; q < bv.Count; q++)
                {
                    verts.Add(pos[i] + rot[i] * (bv[q] * w));
                    normals.Add(rot[i] * bn[q]);
                    uvs.Add(buv[q]);
                }
                idxStart[i] = offset + subTris[m].Count;
                idxCount[i] = bt.Count;
                firstVert[i] = first;
                foreach (int t in bt) subTris[m].Add(first + t);
                rendered++;
                renderedTris += bt.Count / 3;
            }
            offset += subTris[m].Count;
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BedMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, BedMeshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BedMeshPath);
        }
        mesh.Clear();
        mesh.name = "Claw_PileBed";
        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = BedKinds.Length;
        for (int m = 0; m < BedKinds.Length; m++) mesh.SetTriangles(subTris[m], m);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        EditorUtility.SetDirty(mesh);

        // 그림(BoothRoot/PileBed — 기계와 함께 등장) + 충돌(ClawBasket/Colliders/Bed — 조각마다 정적 충돌체, 실물과 같은 모양)
        var renderGo = new GameObject("PileBed") { layer = renderLayer };
        renderGo.transform.SetParent(boothRoot, false);
        renderGo.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = renderGo.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        var colliderParent = basket.Find("Colliders") ?? basket;
        var colGo = new GameObject("Bed") { layer = bedLayer };
        colGo.transform.SetParent(colliderParent, false);
        var cols = new Collider[n];
        for (int i = 0; i < n; i++)
        {
            var pg = new GameObject($"p{i}") { layer = bedLayer };
            pg.transform.SetParent(colGo.transform, false);
            pg.transform.SetPositionAndRotation(basket.TransformPoint(pos[i]), basket.rotation * rot[i]);
            pg.transform.localScale = Vector3.one * spec.Width(kinds[i]);
            cols[i] = AddShape(pg, kinds[i]);
        }

        var bed = basketGo.AddComponent<ClawBed>();
        bed.EditorAssign(mr, colGo, cols, pos.ToArray(), rot.ToArray(), kinds.Select(k => (int)k).ToArray(), tops.ToArray(),
                         idxStart, idxCount, firstVert, wAvg * 0.5f, maxTop, ClawBed.ComputeHash(spec));
        EditorUtility.SetDirty(bed);
        AssetDatabase.SaveAssets();
        sw.Stop();
        string mix = string.Join("·", BedKinds.Select(k => $"{ClawGameManager.KindLabel(k)} {kinds.Count(x => x == k)}"));
        var topIdx = bed.TopLayer;
        string topMix = string.Join("·", BedKinds.Select(k => $"{ClawGameManager.KindLabel(k)} {topIdx.Count(i => kinds[i] == k)}"));
        int upright = 0;
        foreach (int i in topIdx) if (kinds[i] != ClawItem.Kind.Keyring && Vector3.Dot(rot[i] * Vector3.up, Vector3.up) > 0.94f) upright++;
        report.AppendLine($"  · 받침 더미(10-26 상품 1:1:1, 아무 방향): 조각 {n}개({mix}) · 맨 윗층 {topIdx.Length}({topMix}, 그릇 바로 선 것 {upright}) · 그림 {rendered}·삼각형 {renderedTris}(정점 {verts.Count}) · 버림 {dropped} · 아직 움직임 {awake} · " +
                          $"윗면 최고 +{maxTop - floorY:F3}·평균 +{surfMean - floorY:F3}/예산 {topY - floorY:F3} · 아무 방향 {spawned}개 {rounds}번·{layers}층 떨어뜨림 + 골 채우기(윗면 종류 맞추기 {string.Join(" → ", balanceLog)}) {refills}번 · {sw.ElapsedMilliseconds}ms — " +
                          $"그 위 실물 한 층, 더미 꼭대기 +{ClawLayout.PileTopY - floorY:F3} · 단순화 정점 " +
                          string.Join("/", BedKinds.Select(k => $"{visual[k].vertexCount}→{lod[k].v.Count}")));
        return bed;
    }

    /// <summary>
    /// 정점 묶기 단순화(받침 그림용): cells³ 격자 칸 × 노멀 방향(6) 마다 정점 하나(평균 위치·평균 노멀·첫 정점 UV), 퇴화·중복 삼각형 제거.
    /// 노멀 방향을 나눠 그릇 벽 안·바깥 면이 한 면으로 합쳐지지 않게
    /// </summary>
    static (List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> t) ClusterMesh(Mesh src, int cells)
    {
        var vs = src.vertices; var ns = src.normals; var us = src.uv; var ts = src.triangles;
        var b = src.bounds;
        float cs = Mathf.Max(1e-5f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / cells);
        var map = new Dictionary<(int, int, int, int), int>();
        var sumV = new List<Vector3>(); var sumN = new List<Vector3>(); var uv0 = new List<Vector2>(); var cnt = new List<int>();
        var remap = new int[vs.Length];
        for (int i = 0; i < vs.Length; i++)
        {
            var g = (vs[i] - b.min) / cs;
            var nn = i < ns.Length ? ns[i] : Vector3.up;
            float ax = Mathf.Abs(nn.x), ay = Mathf.Abs(nn.y), az = Mathf.Abs(nn.z);
            int dir = ax >= ay && ax >= az ? (nn.x >= 0 ? 0 : 1) : ay >= az ? (nn.y >= 0 ? 2 : 3) : (nn.z >= 0 ? 4 : 5);
            var key = (Mathf.FloorToInt(g.x), Mathf.FloorToInt(g.y), Mathf.FloorToInt(g.z), dir);
            if (!map.TryGetValue(key, out int c))
            {
                c = sumV.Count;
                map[key] = c;
                sumV.Add(Vector3.zero); sumN.Add(Vector3.zero); uv0.Add(i < us.Length ? us[i] : Vector2.zero); cnt.Add(0);
            }
            sumV[c] += vs[i]; sumN[c] += nn; cnt[c]++;
            remap[i] = c;
        }
        var outV = new List<Vector3>(sumV.Count); var outN = new List<Vector3>(sumV.Count);
        for (int c = 0; c < sumV.Count; c++) { outV.Add(sumV[c] / cnt[c]); outN.Add(sumN[c].sqrMagnitude > 1e-8f ? sumN[c].normalized : Vector3.up); }
        var outT = new List<int>(ts.Length / 4);
        var seen = new HashSet<(int, int, int)>();
        for (int t = 0; t < ts.Length; t += 3)
        {
            int a = remap[ts[t]], bb = remap[ts[t + 1]], c = remap[ts[t + 2]];
            if (a == bb || bb == c || a == c) continue;
            // 같은 세 정점 삼각형은 한 번만(방향 무관 키)
            int lo = Mathf.Min(a, Mathf.Min(bb, c)), hi = Mathf.Max(a, Mathf.Max(bb, c)), mid = a + bb + c - lo - hi;
            if (!seen.Add((lo, mid, hi))) continue;
            outT.Add(a); outT.Add(bb); outT.Add(c);
        }
        return (outV, outN, uv0, outT);
    }
}
