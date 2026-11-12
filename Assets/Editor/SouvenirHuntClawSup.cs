using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SouvenirHunt;

// 인형뽑기 기계 몸체 = StartUpPark 인형뽑기 기계 (10-19, ClawLayout.CabinetSup). SouvenirHuntSetup partial — 메뉴 30이 호출.
// 원본: Downloads StartUPPark.zip → Assets/Models/StartUpPark(메뉴 33이 재질·프리팹). 기계는 3메시:
//   · Gacha_Machine.005 몸체(삼각형 1584, 연결 성분 27개) — 하부 캐비닛·콘솔·상단 프레임·간판·모서리 기둥·안쪽 배출구 상자 + 자체 집게·캐리지·조이스틱·버튼
//   · .006 유리 상자(4면, 반투명) · .007 배출구 문(반투명 판)
// 사용자(10-19): "대체할 수 있는 부분만 대체, 집게·캐리지·케이블은 지금 것" →
//   몸체에서 자체 집게·캐리지 성분(유리 상자 가운데 위쪽의 작은 성분)을 잘라 버리고 Model3 집게·캐리지·케이블(BuildClawRig)을 쓴다.
//   조이스틱·버튼 성분은 떼어 따로 구워 ClawConsoleVisual로 움직인다(Model3 것 대신). 배출구 상자는 그대로 두고 칸·벽은 ClawLayout.SupChute*.
// 구운 메시는 yaw(ClawLayout.MachineYaw) 적용, 원점 = 프리팹 루트(씬에서 Machine = MachineOriginLocal, 로컬 회전 identity) — Model3와 같은 구조.
public static partial class SouvenirHuntSetup
{
    const string SupMeshFolder = "Assets/Settings/ClawSup";
    const string SupBodyPart = "StartUpPark_Gacha_Machine.005";
    const string SupGlassPart = "StartUpPark_Gacha_Machine.006";
    const string SupDoorPart = "StartUpPark_Gacha_Machine.007";
    // 콘솔 성분 기준점(프리팹 루트 m, 10-19 실측) — 가장 가까운 성분(3cm 안)
    static readonly Vector3 SupJoystickRef = new Vector3(0.160f, 0.719f, 1.012f);
    static readonly Vector3 SupButtonRef = new Vector3(-0.154f, 0.686f, 1.011f);

    sealed class SupPart
    {
        public Vector3[] v, n;
        public Vector2[] uv;
        public int[] tris;
        public Material mat;
    }

    static SupPart LoadSupPart(GameObject prefab, string name)
    {
        var t = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        var mf = t != null ? t.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) throw new Exception($"StartUpPark 기계 파트 '{name}' 없음 — 모델이 바뀌었으면 SouvenirHuntClawSup.cs 갱신");
        var m = prefab.transform.worldToLocalMatrix * t.localToWorldMatrix;
        var mesh = mf.sharedMesh;
        return new SupPart
        {
            v = mesh.vertices.Select(p => m.MultiplyPoint3x4(p)).ToArray(),
            n = mesh.normals.Select(x => m.MultiplyVector(x).normalized).ToArray(),
            uv = mesh.uv,
            tris = mesh.triangles,
            mat = t.GetComponent<MeshRenderer>()?.sharedMaterial,
        };
    }

    /// <summary>연결 성분(삼각형 연결 + 같은 위치 정점 용접) — 삼각형 시작 인덱스 목록</summary>
    static List<List<int>> SupComponents(SupPart p)
    {
        var parent = Enumerable.Range(0, p.v.Length).ToArray();
        int Root(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Union(int a, int b) { a = Root(a); b = Root(b); if (a != b) parent[a] = b; }
        var weld = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < p.v.Length; i++)
        {
            var key = Vector3Int.RoundToInt(p.v[i] * 1e4f);
            if (weld.TryGetValue(key, out int j)) Union(i, j); else weld[key] = i;
        }
        for (int t = 0; t < p.tris.Length; t += 3) { Union(p.tris[t], p.tris[t + 1]); Union(p.tris[t], p.tris[t + 2]); }
        var map = new Dictionary<int, List<int>>();
        for (int t = 0; t < p.tris.Length; t += 3)
        {
            int r = Root(p.tris[t]);
            if (!map.TryGetValue(r, out var l)) map[r] = l = new List<int>();
            l.Add(t);
        }
        return map.Values.OrderByDescending(l => l.Count).ToList();
    }

    static Bounds SupBounds(SupPart p, IEnumerable<int> triStarts)
    {
        Bounds? b = null;
        foreach (int t in triStarts)
            for (int k = 0; k < 3; k++)
            {
                var x = p.v[p.tris[t + k]];
                if (b == null) b = new Bounds(x, Vector3.zero); else { var y = b.Value; y.Encapsulate(x); b = y; }
            }
        return b ?? new Bounds();
    }

    /// <summary>조각들을 한 메시로: (정점 − 피벗) × 배율 → yaw. 기존 에셋이 있으면 내용만 교체(GUID 유지)</summary>
    static Mesh BakeSup(string name, IEnumerable<(SupPart p, List<int> t)> pieces, Vector3 pivot, Quaternion yaw, float scale = 1f)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        foreach (var (p, triStarts) in pieces)
        {
            var remap = new Dictionary<int, int>();
            foreach (int t in triStarts)
                for (int k = 0; k < 3; k++)
                {
                    int s = p.tris[t + k];
                    if (!remap.TryGetValue(s, out int d))
                    {
                        d = verts.Count;
                        remap[s] = d;
                        verts.Add(yaw * ((p.v[s] - pivot) * scale));
                        normals.Add(p.n.Length > s ? yaw * p.n[s] : Vector3.up);
                        uvs.Add(p.uv.Length > s ? p.uv[s] : Vector2.zero);
                    }
                    tris.Add(d);
                }
        }
        return SaveSupMesh(name, verts, normals, uvs, tris);
    }

    static Mesh SaveSupMesh(string name, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
    {
        string path = $"{SupMeshFolder}/{name}.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null)
        {
            m = new Mesh();
            AssetDatabase.CreateAsset(m, path);
            m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        m.Clear();
        m.name = name;
        m.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        m.RecalculateTangents(); // 노멀맵
        EditorUtility.SetDirty(m);
        return m;
    }

    // 유리 상자 불투명도: 원본(불투명도 맵 0.25)은 앞 유리가 아이템을 25% 흰 막으로 덮어 OST에서 대비가 떨어진다(10-19 에디터 렌더)
    // → 게임용 사본 재질에서 기본색 알파 0.4를 곱해 약 0.10. 원본 재질(부스 모델용)은 그대로
    const string SupGlassMatPath = "Assets/Settings/Claw_SupGlass.mat";
    const float SupGlassAlphaScale = 0.4f;

    static Material SupGlassMaterial(Material src)
    {
        if (src == null) return null;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SupGlassMatPath);
        if (mat == null)
        {
            mat = new Material(src);
            AssetDatabase.CreateAsset(mat, SupGlassMatPath);
            mat = AssetDatabase.LoadAssetAtPath<Material>(SupGlassMatPath);
        }
        mat.shader = src.shader;
        mat.CopyPropertiesFromMaterial(src);
        mat.shaderKeywords = src.shaderKeywords;
        mat.renderQueue = src.renderQueue;
        mat.SetOverrideTag("RenderType", src.GetTag("RenderType", false, "Transparent"));
        var c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
        c.a *= SupGlassAlphaScale;
        mat.SetColor("_BaseColor", c);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>몸체 메시 안의 자체 집게·캐리지 성분: 유리 상자 가운데 위쪽(y 1.25~2.0)의 작은 성분(가로 0.4 미만)</summary>
    static bool IsSupClaw(Bounds b) =>
        Mathf.Abs(b.center.x) < 0.35f && Mathf.Abs(b.center.z) < 0.35f && b.center.y > 1.25f && b.center.y < 2.0f && Mathf.Max(b.size.x, b.size.z) < 0.4f;

    /// <summary>
    /// 10-19 원본 집게·캐리지(ClawLayout.SupClaw): 자체 집게 성분을 캐리지 상자(밑면 = 케이블 앵커) · 헤드(스프링·허브·팔) · 발 3개(관절 = 발 맨 위)로 굽는다.
    /// 헤드 피벗 = 스프링 윗면~발끝 가운데(ClawLayout.SupHead*). 케이블은 원본에 없어(스프링만) 스프링 색 UV 한 점의 가는 원기둥.
    /// parts.ufoBody/ufoMount/ufoCable/fingers/fingerHinges/mat을 덮어쓴다 — Model3 집게 대신 BuildClawRig가 그대로 쓴다.
    /// </summary>
    static void BuildSupClawParts(ClawModelParts parts, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartUpParkPrefabPath);
        if (prefab == null) throw new Exception($"{StartUpParkPrefabPath} 없음 — 메뉴 33 먼저 (또는 ClawLayout.CabinetSup=false)");
        EnsureFolder(SupMeshFolder);
        var body = LoadSupPart(prefab, SupBodyPart);
        var claw = SupComponents(body).Select(c => (tris: c, b: SupBounds(body, c))).Where(c => IsSupClaw(c.b)).ToList();
        var carriage = claw.Where(c => c.b.min.y >= ClawLayout.SupCarriageBottomY - 0.005f).ToList();
        var fingers = claw.Where(c => c.b.min.y < ClawLayout.SupHeadBottomY + 0.03f).OrderBy(c => Mathf.Atan2(c.b.center.z, c.b.center.x)).ToList();
        var head = claw.Where(c => !carriage.Contains(c) && !fingers.Contains(c)).ToList();
        if (carriage.Count != 1 || fingers.Count != 3 || head.Count < 3)
            throw new Exception($"StartUpPark 집게 성분 분류 실패(캐리지 {carriage.Count}·발 {fingers.Count}·헤드 {head.Count}) — 모델이 바뀌었으면 ClawLayout.Sup* 갱신");

        var yaw = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);
        float hs = ClawLayout.SupClawScale;
        var headPivot = new Vector3(0f, (ClawLayout.SupHeadTopY + ClawLayout.SupHeadBottomY) * 0.5f, 0f);
        parts.ufoMount = BakeSup("Sup_Carriage", carriage.Select(c => (body, c.tris)), new Vector3(0f, ClawLayout.SupCarriageBottomY, 0f), yaw);
        parts.ufoBody = BakeSup("Sup_Head", head.Select(c => (body, c.tris)), headPivot, yaw, hs);
        var meshes = new Mesh[3];
        var hinges = new Vector3[3];
        for (int k = 0; k < 3; k++)
        {
            var f = fingers[k];
            var idx = f.tris.SelectMany(i => new[] { body.tris[i], body.tris[i + 1], body.tris[i + 2] }).Distinct().ToList();
            var topPts = idx.Select(i => body.v[i]).Where(p => p.y > f.b.max.y - 0.015f).ToList();
            var hinge = topPts.Aggregate(Vector3.zero, (acc, p) => acc + p) / topPts.Count; // 발 맨 위(팔과 만나는 관절)
            meshes[k] = BakeSup($"Sup_Finger{k}", new[] { (body, f.tris) }, hinge, yaw, hs);
            hinges[k] = yaw * ((hinge - headPivot) * hs);
        }
        parts.fingers = meshes;
        parts.fingerHinges = hinges;

        // 케이블: 반지름 4mm 8각 원기둥, 원점 = 앵커, 아래로 원형 길이(ClawCableMesh가 늘인다). 색 = 스프링(맨 위 코일 성분) UV
        var coil = head.OrderByDescending(c => c.b.min.y).First();
        var coilIdx = coil.tris.SelectMany(i => new[] { body.tris[i], body.tris[i + 1], body.tris[i + 2] }).Distinct().ToList();
        var coilUv = coilIdx.Aggregate(Vector2.zero, (acc, i) => acc + body.uv[i]) / coilIdx.Count;
        {
            const int seg = 8;
            float r = 0.004f, len = ClawLayout.CableRestLength;
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(d * r); verts.Add(d * r + Vector3.down * len);
                normals.Add(d); normals.Add(d);
                uvs.Add(coilUv); uvs.Add(coilUv);
            }
            for (int i = 0; i < seg; i++) { int a = i * 2, b2 = a + 2; tris.AddRange(new[] { a, b2, a + 1, b2, b2 + 1, a + 1 }); }
            parts.ufoCable = SaveSupMesh("Sup_Cable", verts, normals, uvs, tris);
        }
        parts.mat = body.mat;
        parts.rig = BuildSupRig(report); // 10-22 헤드 몸통·발은 StartUpPark_set.fbx 뼈 집게로(없으면 위 발 메시)
        AssetDatabase.SaveAssets();
        report.AppendLine($"  · 집게·캐리지 = StartUpPark 원본(×{hs:F2}): 캐리지 상자 · 헤드 성분 {head.Count}개(스프링·허브·팔) · 발 3개(관절 r {new Vector2(hinges[0].x, hinges[0].z).magnitude:F3}) · 케이블(스프링 색) — " +
                          $"헤드 반지름 {ClawLayout.HeadRadius:F3} · 반높이 {ClawLayout.HeadHalfHeight:F3} · 레일 y {ClawLayout.RailY:F2} · 캐리지 여유 {ClawLayout.CarriageMargin:F2}");
    }

    /// <summary>NewScene(Single)이 미사용 에셋을 내린다 — 씬 생성 뒤 원본 집게 메시·재질을 경로로 다시 잡는다</summary>
    static void ReloadSupClawParts(ClawModelParts parts)
    {
        Mesh M(string n) => AssetDatabase.LoadAssetAtPath<Mesh>($"{SupMeshFolder}/{n}.asset");
        parts.ufoMount = M("Sup_Carriage");
        parts.ufoBody = M("Sup_Head");
        parts.ufoCable = M("Sup_Cable");
        parts.fingers = Enumerable.Range(0, 3).Select(k => M($"Sup_Finger{k}")).ToArray();
        if (parts.rig != null) parts.rig.mesh = M("Sup_ClawSkin");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartUpParkPrefabPath);
        var t = prefab != null ? prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == SupBodyPart) : null;
        if (t != null) parts.mat = t.GetComponent<MeshRenderer>().sharedMaterial;
    }

    static void WarnIfOffSup(StringBuilder report, string what, float measured, float constant)
    {
        if (Mathf.Abs(measured - constant) <= 0.01f) return;
        string msg = $"[SouvenirHuntSetup] StartUpPark 기계 실측 {what} = {measured:F3} ≠ ClawLayout {constant:F3} — 모델이 바뀌었으면 ClawLayout.Sup* 상수 갱신";
        Debug.LogWarning(msg);
        report.AppendLine("  ⚠ " + msg);
    }

    /// <summary>
    /// 배출구 아크릴 칸막이(10-20, 사용자 "벽 메시도 더미 높이까지" → 투명 아크릴 연장): 모델 상자 벽(높이 ChuteWallHeight) 위에 파일 쪽 두 면(X·Z) 판을
    /// ChuteGuardTopY까지. 판 바깥면 = 칸막이 좌표(ChuteWallX/Z, 기둥 콜라이더 안쪽면과 같은 자리 — 아이템이 판에 기대 보임), 앞·왼쪽 끝은 기계 유리 안쪽면 너머 3mm,
    /// 아래는 상자 윗면에 3mm 박음(동일 평면 금지). 시각 전용 — 막는 건 ChuteBarrier 콜라이더, 위 테두리 네온은 BuildMachineFx
    /// </summary>
    static void BuildSupChuteGuard(GameObject env, int layer, Material mat)
    {
        var p = new GameObject("ChuteGuard") { layer = layer }.transform;
        p.SetParent(env.transform, false);
        float gt = ClawLayout.ChuteGuardThickness;
        float y0 = ClawLayout.FloorY + ClawLayout.ChuteWallHeight - 0.003f, y1 = ClawLayout.ChuteGuardTopY;
        float front = ClawLayout.FrontZ - 0.003f, left = -ClawLayout.HalfX - 0.003f;
        float cwx = ClawLayout.ChuteWallX, cwz = ClawLayout.ChuteWallZ;
        var q = Quaternion.identity;
        ClawPrim(p, layer, PrimitiveType.Cube, "GuardX", new Vector3(cwx - gt * 0.5f, (y0 + y1) * 0.5f, (front + cwz) * 0.5f), new Vector3(gt, y1 - y0, cwz - front), q, mat);
        ClawPrim(p, layer, PrimitiveType.Cube, "GuardZ", new Vector3((left + cwx - gt) * 0.5f, (y0 + y1) * 0.5f, cwz - gt * 0.5f), new Vector3(cwx - gt - left, y1 - y0, gt), q, mat);
    }

    /// <summary>StartUpPark 기계: 몸체(자체 집게·캐리지 제외)·유리·문을 Environment/Machine 아래에, 조이스틱·버튼은 Console 아래 따로(ClawConsoleVisual)</summary>
    static ClawConsoleVisual BuildSupMachine(GameObject env, int layer, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StartUpParkPrefabPath);
        if (prefab == null) throw new Exception($"{StartUpParkPrefabPath} 없음 — StartUPPark.zip을 Assets/Models/StartUpPark에 두고 메뉴 33 → 30 (또는 ClawLayout.CabinetSup=false)");
        EnsureFolder(SupMeshFolder);

        var body = LoadSupPart(prefab, SupBodyPart);
        var glass = LoadSupPart(prefab, SupGlassPart);
        var door = LoadSupPart(prefab, SupDoorPart);
        var comps = SupComponents(body).Select(c => (tris: c, b: SupBounds(body, c))).ToList();

        // 실측 대조(유리 상자·바닥은 ClawLayout 상수와 1cm 안)
        var gb = SupBounds(glass, Enumerable.Range(0, glass.tris.Length / 3).Select(i => i * 3));
        var bb = SupBounds(body, Enumerable.Range(0, body.tris.Length / 3).Select(i => i * 3));
        WarnIfOffSup(report, "유리 상자 반폭", gb.extents.x, ClawLayout.SupInnerHalf + 0.006f);
        WarnIfOffSup(report, "캐비닛 밑면 y", bb.min.y, ClawLayout.SupBottomY);
        WarnIfOffSup(report, "콘솔 앞면 z", bb.max.z, ClawLayout.SupFrontFaceZ);

        // 성분 분류: 콘솔 조이스틱·버튼(기준점 3cm 안 가장 가까운 것), 자체 집게·캐리지(유리 상자 가운데 위쪽의 작은 성분)
        (List<int> tris, Bounds b) Nearest(Vector3 r)
        {
            var best = comps.OrderBy(c => (c.b.center - r).sqrMagnitude).First();
            return (best.b.center - r).magnitude < 0.03f ? best : (null, default);
        }
        var stick = Nearest(SupJoystickRef);
        var button = Nearest(SupButtonRef);
        if (stick.tris == null) report.AppendLine("  ⚠ StartUpPark 조이스틱 성분 못 찾음 — 몸체에 그대로(움직이지 않음)");
        if (button.tris == null) report.AppendLine("  ⚠ StartUpPark 버튼 성분 못 찾음 — 몸체에 그대로(움직이지 않음)");
        bool IsClaw((List<int> tris, Bounds b) c) => IsSupClaw(c.b);
        var claw = comps.Where(IsClaw).ToList();
        var keep = comps.Where(c => !IsClaw(c) && c.tris != stick.tris && c.tris != button.tris).ToList();

        var yaw = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);
        var bodyMesh = BakeSup("Sup_Body", keep.Select(c => (body, c.tris)), Vector3.zero, yaw);
        var glassMesh = BakeSup("Sup_Glass", new[]
        {
            (glass, Enumerable.Range(0, glass.tris.Length / 3).Select(i => i * 3).ToList()),
            (door, Enumerable.Range(0, door.tris.Length / 3).Select(i => i * 3).ToList()),
        }, Vector3.zero, yaw);

        var machine = new GameObject("Machine") { layer = layer };
        machine.transform.SetParent(env.transform, false);
        machine.transform.localPosition = ClawLayout.MachineOriginLocal; // 메시에 yaw가 이미 들어 있음
        MeshPart(machine.transform, "Body", bodyMesh, Vector3.zero, body.mat, layer);
        var glassMat = SupGlassMaterial(glass.mat);
        MeshPart(machine.transform, "Glass", glassMesh, Vector3.zero, glassMat, layer);
        BuildSupChuteGuard(env, layer, glassMat);

        Vector3 ToBooth(Vector3 model) => ClawLayout.MachineOriginLocal + yaw * model;
        var console = new GameObject("Console") { layer = layer };
        console.transform.SetParent(env.transform, false);
        var cv = console.AddComponent<ClawConsoleVisual>();
        if (stick.tris != null)
        {
            var pivotModel = new Vector3(stick.b.center.x, stick.b.min.y, stick.b.center.z);
            var pivot = new GameObject("JoystickPivot") { layer = layer };
            pivot.transform.SetParent(console.transform, false);
            pivot.transform.localPosition = ToBooth(pivotModel);
            MeshPart(pivot.transform, "JoyStick", BakeSup("Sup_JoyStick", new[] { (body, stick.tris) }, pivotModel, yaw), Vector3.zero, body.mat, layer);
            SetRef(cv, "stickPivot", pivot.transform);
        }
        if (button.tris != null)
        {
            var pivotModel = new Vector3(button.b.center.x, button.b.min.y, button.b.center.z);
            var btn = MeshPart(console.transform, "Button", BakeSup("Sup_Button", new[] { (body, button.tris) }, pivotModel, yaw), ToBooth(pivotModel), body.mat, layer);
            SetRef(cv, "button", btn.transform);
        }
        AssetDatabase.SaveAssets();

        report.AppendLine($"  · 기계 = StartUpPark 인형뽑기 기계 ({bb.size.x:F2}×{bb.size.y:F2}×{bb.size.z:F2}m, 원점 {ClawLayout.MachineOriginLocal}, yaw {ClawLayout.MachineYaw:F0}) — " +
                          $"몸체 성분 {keep.Count}개(삼각형 {keep.Sum(c => c.tris.Count)}) · 유리 상자+문, 자체 집게·캐리지 성분 {claw.Count}개(삼각형 {claw.Sum(c => c.tris.Count)})는 몸체에서 떼어 {(ClawLayout.SupClaw ? "움직이는 집게로" : "버리고 Model3 집게")}, " +
                          $"조이스틱 {(stick.tris != null ? "O" : "X")}·버튼 {(button.tris != null ? "O" : "X")} 분리(움직임), 내부 {ClawLayout.InnerX:F2}×{ClawLayout.InnerZ:F2}m, 바닥 y {ClawLayout.FloorY:F2}, 뚜껑 y {ClawLayout.LidY:F2}, 레일 y {ClawLayout.RailY:F2}");
        return cv;
    }
}
