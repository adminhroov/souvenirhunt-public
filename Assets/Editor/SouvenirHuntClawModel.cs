using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using SouvenirHunt;

// 인형뽑기 기계 = 임포트 모델 Model2 (5차, 09-14). SouvenirHuntSetup partial — 메뉴 30(SetupClawScene)이 호출.
// Model2.fbx(Blender, 파트 피벗 전부 모델 원점)에서 움직이는 파트를 연결 성분 단위로 떼어 메시 에셋으로 굽는다:
//   UFO(Model_2.006) → 본체 / 천장 마운트 판 / 케이블(코일·중심축)   조이스틱(Model_2.004) → 받침 / 스틱   버튼(Model_2.005)
//   경품 공(Model_2.019~032) → 띠 있는 6종(목표물) / 띠 없는 3종(캡슐·폭탄)
// 구운 메시는 부스 방향(yaw MachineYaw 적용) + 파트 피벗 원점이라 씬에서 로컬 회전 identity로 쓴다.
// 나머지(유리·간판·콘솔·기둥·프레임·발)는 프리팹 인스턴스를 언팩해 Environment 아래 그대로 둔다.
public static partial class SouvenirHuntSetup
{
    const string Model2PrefabPath = "Assets/Models/Model2/Model2.prefab";
    const string Model2MatPath = "Assets/Models/Model2/Model2_Mat.mat";
    const string Model2GlassMatPath = "Assets/Models/Model2/Model2_Glass.mat";
    const string ClawModelMeshFolder = "Assets/Settings/ClawModel";

    const string UfoPartName = "Model_2.006";
    const string JoystickPartName = "Model_2.004";
    const string ButtonPartName = "Model_2.005";
    const string GlassPartName = "Model_2";
    // 경품 공 고유 변형(텍스처 UV 색 분석, 09-14): 띠(분홍·청록) 있음 6종 / 없음 3종. 028·029·031·032·030은 중복 무늬
    static readonly string[] TargetBallParts = { "Model_2.019", "Model_2.020", "Model_2.021", "Model_2.022", "Model_2.025", "Model_2.026" };
    static readonly string[] FillerBallParts = { "Model_2.023", "Model_2.024", "Model_2.027" };
    static IEnumerable<string> AllBallParts()
    {
        for (int i = 19; i <= 32; i++) yield return $"Model_2.{i:000}";
    }

    class ClawModelParts
    {
        public GameObject prefab;
        public Material mat, glass, ball, ballTarget;
        public Mesh ufoBody, ufoMount, ufoCable, joyBase, joyStick, button;
        public Vector3 joyPivotModel, buttonPivotModel;
        public Mesh[] targetBalls, fillerBalls;
        // 10-13 Model3(SouvenirHuntClawModel3.cs): 몸체·유리를 구운 메시로(프리팹 인스턴스 대신), 부스 배치용 기계 바운즈(모델 로컬)
        public Mesh machineBody, machineGlass;
        public Bounds? machineBoundsModel;
        // 10-19 집게발 여닫기(SplitClawFingers): 발 메시(원점 = 관절)·관절 위치(헤드 로컬) — 없으면 null(여닫기 없음)
        public Mesh[] fingers;
        public Vector3[] fingerHinges;
        // 10-22 뼈 집게(SouvenirHuntClawSupRig.cs): 있으면 BuildClawRig가 헤드 몸통·발 메시 대신 뼈 계층 + 스킨 메시
        public SupRig rig;
    }

    /// <summary>모델 로드·파트 메시 굽기·재질. 모델이 없거나 파트를 못 찾으면 예외(메뉴 30 전체 실패로 알림 — 반쪽 씬 금지).</summary>
    static ClawModelParts BuildClawModelParts(StringBuilder report)
    {
        var parts = new ClawModelParts
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Model2PrefabPath),
            mat = AssetDatabase.LoadAssetAtPath<Material>(Model2MatPath),
        };
        if (parts.prefab == null || parts.mat == null)
            throw new Exception($"{Model2PrefabPath} / {Model2MatPath} 없음 — Assets/Models/Model2 임포트 후 메뉴 33 먼저 실행");
        EnsureFolder(ClawModelMeshFolder);

        var inst = (GameObject)UnityEngine.Object.Instantiate(parts.prefab);
        try
        {
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var yaw = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);

            float us = ClawLayout.UfoBakeScale, ms = ClawLayout.MachineScale;
            // 10-13: Model3가 기계면 UFO·조이스틱·버튼은 Model3 것을 쓴다(ClawLayout.Model*도 Model3 실측) — 여기서는 공·재질만
            if (!ClawLayout.IsModel3)
            {
                // ── UFO: 본체(최대 수평 면적) / 마운트(천장 판) / 나머지 = 케이블 ──
                var ufo = PartSource.Find(inst, UfoPartName);
                var comps = ufo.Components();
                var body = comps.OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).First();
                var mounts = comps.Where(c => c != body && c.bounds.min.y >= ClawLayout.ModelUfoMountY - 0.003f).ToList();
                var cables = comps.Where(c => c != body && !mounts.Contains(c)).ToList();
                if (mounts.Count == 0 || cables.Count == 0) throw new Exception($"UFO 분할 실패 — 성분 {comps.Count}개(마운트 {mounts.Count}, 케이블 {cables.Count})");
                // 7차: UFO 3파트는 ClawLayout.UfoScale(0.7)로 피벗 기준 축소 — 헤드 치수 상수가 같은 배율로 파생됨. 9차: × MachineScale
                parts.ufoBody = ufo.Bake(new[] { body }, new Vector3(0f, ClawLayout.ModelUfoBodyCenterY, 0f), yaw, "Claw_UfoBody", us);
                parts.ufoMount = ufo.Bake(mounts, new Vector3(0f, ClawLayout.ModelUfoMountY, 0f), yaw, "Claw_UfoMount", us);
                parts.ufoCable = ufo.Bake(cables, new Vector3(0f, ClawLayout.ModelUfoMountY, 0f), yaw, "Claw_UfoCable", us);

                // 모델 실측 ↔ ClawLayout 대조(재수출로 치수가 바뀌면 경고)
                var glassB = PartSource.Find(inst, GlassPartName).Bounds();
                WarnIfOff(report, "유리 바닥 y", glassB.min.y, ClawLayout.ModelFloorY);
                WarnIfOff(report, "유리 상단 y", glassB.max.y, ClawLayout.ModelCeilingY);
                WarnIfOff(report, "UFO 본체 중심 y", body.bounds.center.y, ClawLayout.ModelUfoBodyCenterY);
                WarnIfOff(report, "UFO 본체 상단 y", body.bounds.max.y, ClawLayout.ModelUfoBodyTopY);
                WarnIfOff(report, "UFO 반지름", body.bounds.extents.x, ClawLayout.ModelUfoRadius);
                WarnIfOff(report, "마운트 하면 y", mounts.Min(c => c.bounds.min.y), ClawLayout.ModelUfoMountY);

                // ── 조이스틱: 받침(최대 y가 가장 낮은 성분) / 스틱(나머지), 피벗 = 받침 상단 중심 ──
                var joy = PartSource.Find(inst, JoystickPartName);
                var jc = joy.Components();
                var jbase = jc.OrderBy(c => c.bounds.max.y).First();
                var jstick = jc.Where(c => c != jbase).ToList();
                if (jstick.Count == 0) throw new Exception("조이스틱 분할 실패 — 성분 1개");
                parts.joyPivotModel = new Vector3(jbase.bounds.center.x, jbase.bounds.max.y, jbase.bounds.center.z);
                parts.joyBase = joy.Bake(new[] { jbase }, parts.joyPivotModel, yaw, "Claw_JoyBase", ms);
                parts.joyStick = joy.Bake(jstick, parts.joyPivotModel, yaw, "Claw_JoyStick", ms);

                var btn = PartSource.Find(inst, ButtonPartName);
                var bb = btn.Bounds();
                parts.buttonPivotModel = new Vector3(bb.center.x, bb.min.y, bb.center.z);
                parts.button = btn.Bake(btn.Components(), parts.buttonPivotModel, yaw, "Claw_Button", ms);
            }

            // ── 경품 공: 피벗 = 바운즈 중심(ClawItem.SetShrink가 원점 기준으로 줄이도록). 표시 크기는 ClawItem이 ItemDiameter로 맞춤 ──
            Mesh Ball(string n)
            {
                var src = PartSource.Find(inst, n);
                // 표시 크기는 ClawItem이 spec 지름으로 맞추므로 배율은 모양에 무관 — Model3(×1.8)일 때 Model2 공 에셋이 바뀌지 않게 1
                return src.Bake(src.Components(), src.Bounds().center, yaw, "Claw_Ball_" + n.Substring(n.Length - 3), ClawLayout.IsModel3 ? 1f : ms);
            }
            parts.targetBalls = TargetBallParts.Select(Ball).ToArray();
            parts.fillerBalls = FillerBallParts.Select(Ball).ToArray();
        }
        finally { UnityEngine.Object.DestroyImmediate(inst); }

        BuildClawModelMaterials(parts);
        AssetDatabase.SaveAssets();
        report.AppendLine(ClawLayout.IsModel3
            ? $"  · Model2 파트 메시 {ClawModelMeshFolder}: 공 {parts.targetBalls.Length}+{parts.fillerBalls.Length}종·공/유리 재질만(기계는 Model3)"
            : $"  · Model2 파트 메시 {ClawModelMeshFolder}: UFO 본체·마운트·케이블, 조이스틱 받침·스틱, 버튼, 공 {parts.targetBalls.Length}+{parts.fillerBalls.Length}종");
        return parts;
    }

    static void WarnIfOff(StringBuilder report, string what, float measured, float constant)
    {
        if (Mathf.Abs(measured - constant) <= 0.01f) return;
        string msg = $"[SouvenirHuntSetup] Model2 실측 {what} = {measured:F3} ≠ ClawLayout {constant:F3} — 모델이 바뀌었으면 ClawLayout.Model* 상수 갱신";
        Debug.LogWarning(msg);
        report.AppendLine("  ⚠ " + msg);
    }

    /// <summary>유리: Model2_Glass 복제 + 알파 ×0.25(OST에서 반투명 유리가 파일 대비를 죽이지 않게 — 기존 α 0.10 교훈).
    /// 공: Model2 텍스처 + 이미션 맵 = 베이스(ClawItem 글로우 MPB가 무늬째로 빛나게). 목표물만 은은한 발광.</summary>
    static void BuildClawModelMaterials(ClawModelParts parts)
    {
        var glassSrc = AssetDatabase.LoadAssetAtPath<Material>(Model2GlassMatPath);
        parts.glass = GetOrCreateMat("Claw_MachineGlass", "Universal Render Pipeline/Lit");
        if (glassSrc != null) parts.glass.CopyPropertiesFromMaterial(glassSrc);
        parts.glass.SetFloat("_Surface", 1f);
        parts.glass.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.25f));
        parts.glass.SetTexture("_EmissionMap", null);
        parts.glass.SetColor("_EmissionColor", Color.black);
        FinishLit(parts.glass, emission: false);

        var baseMap = parts.mat.GetTexture("_BaseMap");
        parts.ball = GetOrCreateMat("Claw_Ball", "Universal Render Pipeline/Lit");
        parts.ball.CopyPropertiesFromMaterial(parts.mat);
        parts.ball.SetTexture("_EmissionMap", baseMap);
        parts.ball.SetColor("_EmissionColor", Color.black);
        FinishLit(parts.ball, emission: true);

        parts.ballTarget = GetOrCreateMat("Claw_BallTarget", "Universal Render Pipeline/Lit");
        parts.ballTarget.CopyPropertiesFromMaterial(parts.mat);
        parts.ballTarget.SetTexture("_EmissionMap", baseMap);
        parts.ballTarget.SetColor("_EmissionColor", new Color(0.45f, 0.42f, 0.36f)); // ≤1 → 블룸 없이 무늬째 밝게
        FinishLit(parts.ballTarget, emission: true);
    }

    static void FinishLit(Material mat, bool emission)
    {
        mat.renderQueue = -1;
        BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);
        if (emission) mat.EnableKeyword("_EMISSION"); else mat.DisableKeyword("_EMISSION"); // 이미션 색 검정이어도 MPB 글로우가 먹도록 유지
        // URP는 Emissive GI 비트가 없으면 재임포트 때 _EMISSION을 지운다(10-06) — 켜 둔 재질은 RealtimeEmissive
        mat.globalIlluminationFlags = emission ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(mat);
    }

    /// <summary>Model2 인스턴스를 Environment 아래에 두고 떼어낸 파트(UFO·조이스틱·버튼·경품 공)를 지운다. 콘솔 조이스틱·버튼은 따로 배치.</summary>
    static ClawConsoleVisual BuildModelMachine(GameObject env, int layer, ClawModelParts parts, StringBuilder report)
    {
        var yaw = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);
        var machine = (GameObject)PrefabUtility.InstantiatePrefab(parts.prefab);
        PrefabUtility.UnpackPrefabInstance(machine, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        machine.name = "Machine";
        machine.transform.SetParent(env.transform, false);
        machine.transform.localPosition = ClawLayout.MachineOriginLocal;
        machine.transform.localRotation = yaw;
        machine.transform.localScale = ClawLayout.MachineLocalScale; // 11차: 가로(모델 x)만 MachineWidthScale 추가

        var remove = new HashSet<string>(AllBallParts()) { UfoPartName, JoystickPartName, ButtonPartName };
        int removed = 0;
        foreach (var t in machine.GetComponentsInChildren<Transform>(true).Where(t => remove.Contains(t.name)).ToList())
        {
            UnityEngine.Object.DestroyImmediate(t.gameObject);
            removed++;
        }
        foreach (var t in machine.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (var mr in machine.GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (mr.name == GlassPartName) mr.sharedMaterial = parts.glass;
        }
        if (removed != remove.Count)
            report.AppendLine($"  ⚠ Model2 제거 파트 {removed}/{remove.Count} — 파트 이름이 바뀌었는지 확인");

        // 콘솔: 조이스틱(받침 고정 + 부스 축 정렬 피벗 아래 스틱) · 버튼 — Environment 직속(흔들림·등장 연출 공유)
        var console = new GameObject("Console") { layer = layer };
        console.transform.SetParent(env.transform, false);
        Vector3 ToBooth(Vector3 model) => ClawLayout.MachineOriginLocal + yaw * Vector3.Scale(model, ClawLayout.MachineLocalScale); // 부품은 늘이지 않고 위치만
        MeshPart(console.transform, "JoyBase", parts.joyBase, ToBooth(parts.joyPivotModel), parts.mat, layer);
        var pivot = new GameObject("JoystickPivot") { layer = layer };
        pivot.transform.SetParent(console.transform, false);
        pivot.transform.localPosition = ToBooth(parts.joyPivotModel);
        MeshPart(pivot.transform, "JoyStick", parts.joyStick, Vector3.zero, parts.mat, layer);
        var button = MeshPart(console.transform, "Button", parts.button, ToBooth(parts.buttonPivotModel), parts.mat, layer);
        var cv = console.AddComponent<ClawConsoleVisual>();
        SetRef(cv, "stickPivot", pivot.transform);
        SetRef(cv, "button", button.transform);
        report.AppendLine($"  · 기계 = Model2 (×{ClawLayout.MachineScale:F2}, 가로 ×{ClawLayout.MachineWidthScale:F2}, 원점 {ClawLayout.MachineOriginLocal}, yaw {ClawLayout.MachineYaw:F0}) — UFO·조이스틱·버튼·공 {removed}개 떼어냄, 유리 Claw_MachineGlass");
        return cv;
    }

    static GameObject MeshPart(Transform parent, string name, Mesh mesh, Vector3 localPos, Material mat, int layer)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    /// <summary>FBX 파트 하나(메시 + 모델 원점 기준 변환). 연결 성분 = 삼각형 연결 + 같은 위치 정점 용접(UV 이음새로 쪼개진 정점 병합).</summary>
    sealed class PartSource
    {
        public sealed class Comp { public List<int> tris = new(); public Bounds bounds; }

        readonly Mesh mesh;
        readonly Matrix4x4 toModel;
        readonly Vector3[] modelVerts;

        PartSource(Mesh m, Matrix4x4 mtx)
        {
            mesh = m;
            toModel = mtx;
            var v = m.vertices;
            modelVerts = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) modelVerts[i] = mtx.MultiplyPoint3x4(v[i]);
        }

        public static PartSource Find(GameObject root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            var mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) throw new Exception($"Model2 파트 '{name}' 없음 — 모델 구조가 바뀌었는지 확인");
            return new PartSource(mf.sharedMesh, root.transform.worldToLocalMatrix * t.localToWorldMatrix);
        }

        public Bounds Bounds()
        {
            var b = new Bounds(modelVerts[0], Vector3.zero);
            foreach (var p in modelVerts) b.Encapsulate(p);
            return b;
        }

        public List<Comp> Components()
        {
            var tris = mesh.triangles;
            int n = modelVerts.Length;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Root(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Root(a); b = Root(b); if (a != b) parent[a] = b; }

            var weld = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n; i++)
            {
                var p = modelVerts[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));
                if (weld.TryGetValue(key, out int j)) Union(i, j); else weld[key] = i;
            }
            for (int t = 0; t < tris.Length; t += 3) { Union(tris[t], tris[t + 1]); Union(tris[t], tris[t + 2]); }

            var map = new Dictionary<int, Comp>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int r = Root(tris[t]);
                if (!map.TryGetValue(r, out var c))
                {
                    c = new Comp { bounds = new Bounds(modelVerts[tris[t]], Vector3.zero) };
                    map[r] = c;
                }
                c.tris.Add(t);
                for (int k = 0; k < 3; k++) c.bounds.Encapsulate(modelVerts[tris[t + k]]);
            }
            return map.Values.ToList();
        }

        /// <summary>선택 성분만 새 메시로: 모델 좌표 → (pivot 빼기) → ×scale → yaw 회전. 기존 에셋이 있으면 내용만 교체(GUID 유지).</summary>
        public Mesh Bake(IEnumerable<Comp> comps, Vector3 pivotModel, Quaternion yaw, string assetName, float scale = 1f)
        {
            var srcTris = mesh.triangles;
            var srcN = mesh.normals;
            var srcT = mesh.tangents;
            var srcUv = mesh.uv;
            var remap = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var rot = yaw * toModel.rotation;
            foreach (var c in comps)
                foreach (int t in c.tris)
                    for (int k = 0; k < 3; k++)
                    {
                        int s = srcTris[t + k];
                        if (!remap.TryGetValue(s, out int d))
                        {
                            d = verts.Count;
                            remap[s] = d;
                            verts.Add(yaw * ((modelVerts[s] - pivotModel) * scale));
                            if (srcN.Length > 0) normals.Add((rot * srcN[s]).normalized);
                            if (srcT.Length > 0) { var tv = rot * (Vector3)srcT[s]; tangents.Add(new Vector4(tv.x, tv.y, tv.z, srcT[s].w)); }
                            if (srcUv.Length > 0) uvs.Add(srcUv[s]);
                        }
                        tris.Add(d);
                    }

            string path = $"{ClawModelMeshFolder}/{assetName}.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null)
            {
                m = new Mesh();
                AssetDatabase.CreateAsset(m, path);
                m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            }
            m.Clear();
            m.name = assetName;
            m.SetVertices(verts);
            if (normals.Count == verts.Count) m.SetNormals(normals); else m.RecalculateNormals();
            if (tangents.Count == verts.Count) m.SetTangents(tangents);
            if (uvs.Count == verts.Count) m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
