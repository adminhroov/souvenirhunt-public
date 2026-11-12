using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SouvenirHunt;

// 인형뽑기 미션형 아이템 에셋(10-15): 세라믹·유리 그릇(Downloads zip의 Tripo FBX를 폭 1로 정규화해 굽고 볼록 헐을 정점 실측),
// 캡슐·폭탄(임시 절차 메시 + 작은 생성 텍스처), 폭발 연출(파티클 5종 + 합성 폭발음). 메뉴 30(SetupClawScene)이 부른다.
// 메시·텍스처·재질은 매번 다시 굽되 같은 경로를 써 GUID를 유지한다(씬·프리팹 참조가 깨지지 않게).
// 비주얼 1차(10-15): 아이템 재질 = 셰이더 SouvenirHunt/ClawItemLit(테두리 빛·조준 하이라이트·발광 맥동, 키워드 없는 이미션),
// 환경 반사 = 밝은 스튜디오 큐브맵(Claw_StudioEnv.exr, 메뉴 17이 Claw 씬 커스텀 반사로 지정).
public static partial class SouvenirHuntSetup
{
    const string ClawItemFolder = "Assets/Settings/ClawItems";
    const string CeramicFbxPath = "Assets/Models/CeramicBowl/ceramic+bowl+3d+model.fbx";
    const string GlassFbxPath = "Assets/Models/GlassBowl/glass+bowl+3d+model.fbx";
    const string ExplosionWav = "explosion.wav";

    // 캡슐 윗반구 5색 — OST에서 어두움 = 투명이므로 밝은 색 + 약한 자체 발광
    static readonly Color[] CapsuleColors =
    {
        new Color(1f, 0.45f, 0.62f), new Color(1f, 0.84f, 0.30f), new Color(0.35f, 0.85f, 1f),
        new Color(0.55f, 1f, 0.45f), new Color(1f, 0.60f, 0.30f),
    };

    struct ClawItemAssets
    {
        public Mesh ceramic, glass, capsule, bomb, ceramicHull, glassHull, keyring;
        public Material ceramicMat, glassMat, bombMat, keyringMat;
        public Material[] capsuleMats;
        public float ceramicRatio, glassRatio; // 높이 / 폭(실측)
    }

    static ClawItemAssets BuildClawItemAssets(StringBuilder report)
    {
        EnsureFolder(ClawItemFolder);
        var a = new ClawItemAssets();
        a.ceramic = BakeBowl(CeramicFbxPath, "Claw_CeramicBowl", out a.ceramicHull, out a.ceramicRatio, out var cerTex, report);
        a.glass = BakeBowl(GlassFbxPath, "Claw_GlassBowl", out a.glassHull, out a.glassRatio, out var glsTex, report);
        a.ceramicMat = BowlMaterial("Claw_CeramicBowl", cerTex, new Color(0.95f, 0.92f, 0.86f), 0.55f);
        a.glassMat = BowlMaterial("Claw_GlassBowl", glsTex, new Color(0.92f, 0.96f, 1f), 0.75f);

        a.capsule = SaveMesh(BuildCapsuleMesh(), $"{ClawItemFolder}/Claw_CapsuleMesh.asset");
        a.capsuleMats = new Material[CapsuleColors.Length];
        for (int i = 0; i < CapsuleColors.Length; i++)
        {
            var c = CapsuleColors[i];
            var tex = SaveTexture($"{ClawItemFolder}/Claw_CapsuleTex{i}.asset", 4, 64, (x, y) => CapsuleTexel(c, (y + 0.5f) / 64f));
            // 비주얼 1차: 자체 발광을 0.32 → 0.10으로 낮춰 음영을 살리고, 밝기는 광택(환경 반사)·테두리 빛으로 — 반짝이는 가챠 캡슐
            a.capsuleMats[i] = ItemMat($"ClawItems/Claw_Capsule{i}", tex, tex, Color.white, new Color(0.14f, 0.14f, 0.14f),
                                       smoothness: 0.85f, metallic: 0f, rim: Color.white, rimStrength: 0.25f, rimPower: 3f, rimTint: 0.6f);
        }

        a.bomb = SaveMesh(BuildBombMesh(), $"{ClawItemFolder}/Claw_BombMesh.asset");
        var bombBase = SaveTexture($"{ClawItemFolder}/Claw_BombTex.asset", 4, 64, (x, y) => BombTexel((y + 0.5f) / 64f, emission: false));
        var bombEmit = SaveTexture($"{ClawItemFolder}/Claw_BombEmit.asset", 4, 64, (x, y) => BombTexel((y + 0.5f) / 64f, emission: true));
        // 기존 Claw_Bomb 재질 경로 유지(GUID) — 10-06 이전 생성분은 _EMISSION·GI 플래그가 빠져 붉은 빛이 꺼져 있었음 → FinishLit로 바로잡음
        // 비주얼 1차: 붉은 테두리 빛으로 어두운 몸통 윤곽(글래스에서 검정 = 투명) + 띠·심지 불꽃 맥동(위험 신호)
        a.bombMat = ItemMat("Claw_Bomb", bombBase, bombEmit, Color.white, new Color(1.3f, 1.3f, 1.3f),
                            smoothness: 0.82f, metallic: 0.25f, rim: new Color(1f, 0.32f, 0.16f), rimStrength: 0.9f, rimPower: 2.2f, rimTint: 0f,
                            pulse: 0.45f, pulseSpeed: 7f);

        // 10-22 키링(Assets/Models/KoreaCubeKeyring, Meshy 저폴리 1,888삼각형) — 큐브 몸통 모서리 1·중심 원점, 체인·고리는 바깥으로
        a.keyring = BakeKeyring(KeyringObjPath, "Claw_KeyringMesh", report);
        var keyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(KeyringTexPath);
        a.keyringMat = ItemMat("ClawItems/Claw_Keyring", keyTex, keyTex, Color.white, new Color(0.18f, 0.18f, 0.18f),
                               smoothness: 0.6f, metallic: 0f, rim: Color.white, rimStrength: 0.3f, rimPower: 3f, rimTint: 0.5f);

        AssetDatabase.SaveAssets();
        report?.AppendLine($"  · 아이템 에셋 {ClawItemFolder}: 세라믹 그릇(높이/폭 {a.ceramicRatio:0.00})·유리 그릇({a.glassRatio:0.00}) 정규화 메시 + 볼록 헐, 캡슐 {CapsuleColors.Length}색·폭탄 임시 메시, 키링 {(a.keyring != null ? "O" : "X")}");
        return a;
    }

    const string KeyringObjPath = "Assets/Models/KoreaCubeKeyring/KoreaCubeKeyring.obj";
    const string KeyringTexPath = "Assets/Models/KoreaCubeKeyring/KoreaCubeKeyring.png";

    /// <summary>
    /// 키링 메시: 위가 +Y(원본 그대로 — 큐브가 바닥에 놓이고 체인·고리·집게고리가 바닥에 납작). 큐브 모서리 H = 전체 높이,
    /// 큐브 자리 = 높이 위쪽 40%에 있는 정점(체인·고리는 바닥에 납작해 큐브만 닿는다)의 XZ 가운데 → 큐브 중심을 원점, H를 1로. 없으면 null(키링 0개로 동작).
    /// 10-26: 예전 "바운즈 네 모서리 중 H×H 정사각에 정점이 가장 많은 곳"은 정점이 많은 고리 쪽 모서리를 골라, 원점·상자 충돌체가 큐브 옆 고리에 있었다
    /// (위에서 본 렌더로 확인 — 집게가 보이지 않는 상자를 잡음). 위쪽 정점이 모자라면 예전 방식
    /// </summary>
    static Mesh BakeKeyring(string objPath, string assetName, StringBuilder report)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
        if (src == null) { report?.AppendLine($"  ⚠ {objPath} 없음 — 키링 없음"); return null; }
        var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        var inst = UnityEngine.Object.Instantiate(src);
        try
        {
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            var toRoot = inst.transform.worldToLocalMatrix;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var mtx = toRoot * mf.transform.localToWorldMatrix;
                int baseIdx = verts.Count;
                var v = m.vertices; var n = m.normals; var uv = m.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    verts.Add(mtx.MultiplyPoint3x4(v[i]));
                    normals.Add(n.Length == v.Length ? mtx.MultiplyVector(n[i]).normalized : Vector3.up);
                    uvs.Add(uv.Length == v.Length ? uv[i] : Vector2.zero);
                }
                for (int s = 0; s < m.subMeshCount; s++)
                    foreach (int t in m.GetTriangles(s)) tris.Add(baseIdx + t);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(inst); }
        if (verts.Count == 0) { report?.AppendLine($"  ⚠ {objPath}: 메시 없음 — 키링 없음"); return null; }

        var b = BoundsOf(verts);
        float h = b.size.y;
        var upper = new List<Vector3>();
        foreach (var p in verts) if (p.y > b.min.y + h * 0.6f) upper.Add(p);
        Vector3 c;
        int bestN;
        if (upper.Count >= 8)
        {
            var ub = BoundsOf(upper);
            c = new Vector3(ub.center.x, b.center.y, ub.center.z);
            bestN = upper.Count;
        }
        else
        {
            Vector2 best = Vector2.zero; bestN = -1;
            foreach (float x0 in new[] { b.min.x, b.max.x - h })
                foreach (float z0 in new[] { b.min.z, b.max.z - h })
                {
                    int n = 0;
                    foreach (var p in verts) if (p.x >= x0 - 1e-4f && p.x <= x0 + h + 1e-4f && p.z >= z0 - 1e-4f && p.z <= z0 + h + 1e-4f) n++;
                    if (n > bestN) { bestN = n; best = new Vector2(x0, z0); }
                }
            c = new Vector3(best.x + h * 0.5f, b.center.y, best.y + h * 0.5f);
        }
        float inv = h > 1e-6f ? 1f / h : 1f;
        for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - c) * inv;
        var mesh = new Mesh { indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        var saved = SaveMesh(mesh, $"{ClawItemFolder}/{assetName}.asset");
        var nb = BoundsOf(verts);
        report?.AppendLine($"    {assetName}: 정점 {verts.Count} · 삼각형 {tris.Count / 3} · 큐브(위쪽) 정점 {bestN} · 원점 이동 {c.x:0.000},{c.z:0.000} · 전체/큐브 {nb.size.x:0.00}×{nb.size.y:0.00}×{nb.size.z:0.00}");
        return saved;
    }

    /// <summary>그릇 비율은 메시 실측값을 규격 에셋에 적는다(튜닝 값이 아니라 모델 사실 — 배치·집게 높이가 이걸로 계산된다)</summary>
    static void ApplyBowlRatios(ClawItemSpec spec, ClawItemAssets a)
    {
        if (spec == null) return;
        if (a.ceramicRatio > 0.05f) spec.ceramic.heightToWidth = a.ceramicRatio;
        if (a.glassRatio > 0.05f) spec.glass.heightToWidth = a.glassRatio;
        EditorUtility.SetDirty(spec);
        AssetDatabase.SaveAssets();
    }

    // ─ 그릇 굽기 ────────────────────────────────────────────────

    /// <summary>
    /// FBX 인스턴스의 모든 MeshFilter를 루트 좌표로 합쳐 한 메시로: 대칭축을 +Y로(옆으로 누운 모델 보정), 입구가 위로(아래 반지름 &lt; 위 반지름),
    /// 바운즈 중심을 원점, 폭(XZ 최대) 1로 정규화. 헐 = 높이 8구간마다 최대 반지름의 16각 고리(볼록 쿠킹이 외피를 만든다).
    /// FBX가 없으면 캡슐 메시로 대체(앱은 계속).
    /// </summary>
    static Mesh BakeBowl(string fbxPath, string assetName, out Mesh hull, out float ratio, out Texture2D baseTex, StringBuilder report)
    {
        hull = null;
        ratio = 0.5f;
        baseTex = null;
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (src == null)
        {
            report?.AppendLine($"  ⚠ {fbxPath} 없음 — {assetName}은 임시 원뿔대 메시");
            var stub = SaveMesh(BuildFrustumMesh(0.32f, 0.5f, 0.5f, 16, closed: true), $"{ClawItemFolder}/{assetName}.asset");
            hull = SaveMesh(BuildFrustumMesh(0.32f, 0.5f, 0.5f, 16, closed: true), $"{ClawItemFolder}/{assetName}_Hull.asset");
            return stub;
        }
        ConfigureBowlImport(fbxPath);
        baseTex = FindBowlTexture(fbxPath);

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var inst = UnityEngine.Object.Instantiate(src);
        try
        {
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            var toRoot = inst.transform.worldToLocalMatrix;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var mtx = toRoot * mf.transform.localToWorldMatrix;
                int baseIdx = verts.Count;
                var v = m.vertices; var n = m.normals; var uv = m.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    verts.Add(mtx.MultiplyPoint3x4(v[i]));
                    normals.Add(n.Length == v.Length ? mtx.MultiplyVector(n[i]).normalized : Vector3.up);
                    uvs.Add(uv.Length == v.Length ? uv[i] : Vector2.zero);
                }
                for (int s = 0; s < m.subMeshCount; s++)
                    foreach (int t in m.GetTriangles(s)) tris.Add(baseIdx + t);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(inst); }
        if (verts.Count == 0) throw new Exception($"{fbxPath}: 메시 없음");

        // 대칭축 = 나머지 두 축 크기가 가장 비슷한 축 → +Y로
        var b = BoundsOf(verts);
        var e = b.size;
        float dx = Mathf.Abs(e.y - e.z) / Mathf.Max(e.y, e.z), dy = Mathf.Abs(e.x - e.z) / Mathf.Max(e.x, e.z), dz = Mathf.Abs(e.x - e.y) / Mathf.Max(e.x, e.y);
        Quaternion fix = Quaternion.identity;
        if (dx < dy && dx < dz) fix = Quaternion.Euler(0f, 0f, 90f);        // X축 대칭 → Y로
        else if (dz < dy && dz < dx) fix = Quaternion.Euler(-90f, 0f, 0f);  // Z축 대칭 → Y로
        Rotate(verts, normals, fix, b.center);

        // 입구가 위: 아래 15% 구간 최대 반지름 < 위 15% 구간 최대 반지름이어야 함
        b = BoundsOf(verts);
        if (MaxRadius(verts, b, 0f, 0.15f) > MaxRadius(verts, b, 0.85f, 1f))
        {
            Rotate(verts, normals, Quaternion.Euler(180f, 0f, 0f), b.center);
            b = BoundsOf(verts);
        }

        float width = Mathf.Max(b.size.x, b.size.z);
        float inv = width > 1e-6f ? 1f / width : 1f;
        var c = b.center;
        for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - c) * inv;
        ratio = b.size.y * inv;

        var mesh = new Mesh { indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        var saved = SaveMesh(mesh, $"{ClawItemFolder}/{assetName}.asset");

        // 헐: 높이 8구간 × 16각, 각 고리 반지름 = 그 구간(+경계) 최대 반지름
        const int slabs = 8, segs = 16;
        var nb = BoundsOf(verts);
        var ring = new List<Vector3>();
        for (int s = 0; s <= slabs; s++)
        {
            float t0 = Mathf.Max(0f, (s - 0.5f) / slabs), t1 = Mathf.Min(1f, (s + 0.5f) / slabs);
            float r = MaxRadius(verts, nb, t0, t1);
            if (r <= 0f) continue;
            float y = Mathf.Lerp(nb.min.y, nb.max.y, s / (float)slabs);
            r /= Mathf.Cos(Mathf.PI / segs); // 16각이 원을 안쪽으로 깎지 않게(외접)
            for (int k = 0; k < segs; k++)
            {
                float ang = k / (float)segs * Mathf.PI * 2f;
                ring.Add(new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r));
            }
        }
        hull = SaveMesh(BuildPointHullMesh(ring, segs), $"{ClawItemFolder}/{assetName}_Hull.asset");
        report?.AppendLine($"    {assetName}: 정점 {verts.Count} · 삼각형 {tris.Count / 3} · 높이/폭 {ratio:0.00}");
        return saved;
    }

    static void ConfigureBowlImport(string fbxPath)
    {
        string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(p) is TextureImporter ti && ti.maxTextureSize > 1024)
            {
                ti.maxTextureSize = 1024; // 기계 안에서 12cm 남짓 — 4K 원본은 낭비
                ti.SaveAndReimport();
            }
        }
    }

    static Texture2D FindBowlTexture(string fbxPath)
    {
        string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.IndexOf("basecolor", StringComparison.OrdinalIgnoreCase) >= 0) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        return null;
    }

    /// <summary>그릇 재질 — 비주얼 1차: 자체 발광 0.42 → 0.16~0.22(평평함 해소), 유약·자기 광택은 환경 반사, 가장자리는 테두리 빛</summary>
    static Material BowlMaterial(string name, Texture2D tex, Color tint, float smoothness)
    {
        bool porcelain = smoothness >= 0.7f; // 유리(흰 자기) 그릇 — 광택을 0.7로 눌러 은색 금속처럼 보이지 않게(비주얼 1차 렌더 확인)
        // 자기 텍스처 자체가 회색 톤이라 기본색을 1.15배로 올려 흰 자기로(그대로면 은색 금속처럼 보임 — 1차 렌더)
        var baseCol = tex == null ? tint : porcelain ? new Color(1.15f, 1.15f, 1.15f, 1f) : Color.white;
        return ItemMat($"ClawItems/{name}", tex, tex, baseCol,
                       porcelain ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.16f, 0.16f, 0.16f),
                       smoothness: porcelain ? 0.7f : smoothness, metallic: 0f,
                       rim: porcelain ? new Color(0.85f, 0.95f, 1f) : new Color(1f, 0.92f, 0.8f), rimStrength: 0.3f, rimPower: 3f, rimTint: 0.3f);
    }

    const string ItemShaderName = "SouvenirHunt/ClawItemLit";

    /// <summary>아이템 재질(ClawItemLit) — 있던 재질(예전 URP Lit)은 셰이더만 바꿔 GUID 유지. 셰이더가 없으면(컴파일 실패) Lit 그대로 두고 경고</summary>
    static Material ItemMat(string name, Texture2D baseTex, Texture2D emitTex, Color baseColor, Color emission,
                            float smoothness, float metallic, Color rim, float rimStrength, float rimPower, float rimTint = 0.5f,
                            float pulse = 0f, float pulseSpeed = 6f)
    {
        var m = GetOrCreateMat(name, ItemShaderName);
        var sh = Shader.Find(ItemShaderName);
        if (sh == null) Debug.LogWarning($"[SouvenirHuntSetup] 셰이더 {ItemShaderName} 없음 — {name}은 URP Lit 유지");
        else if (m.shader != sh) m.shader = sh;
        m.shaderKeywords = new string[0];   // 예전 Lit 키워드(_EMISSION 등) 정리 — 이 셰이더는 키워드를 쓰지 않음
        m.renderQueue = -1;
        m.SetTexture("_BaseMap", baseTex);
        m.SetColor("_BaseColor", baseColor);
        m.SetTexture("_EmissionMap", emitTex);
        m.SetColor("_EmissionColor", emission);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_ReflectStrength", 1f);
        m.SetColor("_RimColor", rim);
        m.SetFloat("_RimStrength", rimStrength);
        m.SetFloat("_RimPower", rimPower);
        m.SetFloat("_RimTint", rimTint);
        m.SetFloat("_EmissionPulse", pulse);
        m.SetFloat("_EmissionPulseSpeed", pulseSpeed);
        m.SetColor("_HighlightColor", new Color(0f, 1.4f, 1.6f, 1f));
        m.SetFloat("_Highlight", 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ─ 환경 반사(비주얼 1차) ───────────────────────────────────────
    const string StudioEnvPath = ClawItemFolder + "/Claw_StudioEnv.exr";

    /// <summary>
    /// 밝은 스튜디오 환경(위도·경도 1024×512 EXR → 큐브맵, 스페큘러 컨볼루션)을 만들어 반환. 메뉴 17이 Claw 씬 커스텀 반사로 지정한다.
    /// 지금은 하늘·반사가 비어 반사가 검정(= OST에서 투명)이라 광택이 없었다. 위는 밝은 흰빛, 아래는 회색(검정 금지 — 아랫면 반사가 구멍이 됨),
    /// 소프트박스 몇 개(1보다 밝음 → 또렷한 하이라이트)와 청록·분홍 네온 줄(기계 네온 톤). 앞뒤 대칭으로 둬 큐브맵 방향 규약과 무관하게 비슷하게 보인다.
    /// 파일은 내용이 바뀔 때만 다시 쓴다(재임포트 최소화).
    /// </summary>
    public static Cubemap EnsureStudioEnv()
    {
        EnsureFolder(ClawItemFolder);
        const int W = 1024, H = 512;
        var tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float el = ((y + 0.5f) / H - 0.5f) * 180f;
            for (int x = 0; x < W; x++)
            {
                float az = ((x + 0.5f) / W - 0.5f) * 360f;
                px[y * W + x] = StudioEnv(az, el);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false);
        var bytes = tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
        UnityEngine.Object.DestroyImmediate(tex);
        string full = Path.GetFullPath(StudioEnvPath);
        bool write = !File.Exists(full) || !File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes);
        if (write)
        {
            File.WriteAllBytes(full, bytes);
            AssetDatabase.ImportAsset(StudioEnvPath, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(StudioEnvPath) is TextureImporter ti)
        {
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            bool dirty = s.textureShape != TextureImporterShape.TextureCube || s.generateCubemap != TextureImporterGenerateCubemap.Cylindrical ||
                         s.cubemapConvolution != TextureImporterCubemapConvolution.Specular || s.sRGBTexture || !s.mipmapEnabled || ti.maxTextureSize != 256;
            if (dirty)
            {
                s.textureShape = TextureImporterShape.TextureCube;
                s.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                s.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
                s.sRGBTexture = false;
                s.mipmapEnabled = true;
                ti.SetTextureSettings(s);
                ti.maxTextureSize = 256;
                ti.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Cubemap>(StudioEnvPath);
    }

    static float SoftBox(float az, float el, float azC, float elC, float w, float h)
    {
        float dAz = Mathf.Abs(Mathf.DeltaAngle(az, azC)), dEl = Mathf.Abs(el - elC);
        const float f = 5f; // 가장자리 부드럽게(도)
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((w * 0.5f - dAz) / f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((h * 0.5f - dEl) / f));
    }

    static Color StudioEnv(float az, float el)
    {
        // 바탕은 중간 회색(검정은 OST에서 구멍, 너무 밝으면 넓은 반사가 색을 씻어냄 — 1차 렌더에서 확인) · 소프트박스만 1보다 밝게
        var horizon = new Color(0.30f, 0.31f, 0.34f);
        var zenith = new Color(0.58f, 0.60f, 0.64f);
        var ground = new Color(0.20f, 0.19f, 0.21f);
        Color c = el >= 0f ? Color.Lerp(horizon, zenith, Mathf.Pow(el / 90f, 0.6f))
                           : Color.Lerp(horizon * 0.85f, ground, Mathf.Pow(-el / 90f, 0.5f));
        var warm = new Color(1f, 0.97f, 0.92f);
        var cool = new Color(0.9f, 0.95f, 1f);
        foreach (float mirror in new[] { 0f, 180f })
        {
            c += warm * (4.0f * SoftBox(az, el, -35f + mirror, 35f, 36f, 22f));   // 키 소프트박스
            c += cool * (2.2f * SoftBox(az, el, 40f + mirror, 18f, 28f, 16f));    // 보조
            c += new Color(0.3f, 0.9f, 1f) * (1.6f * SoftBox(az, el, -90f + mirror, 4f, 70f, 5f));  // 청록 네온 줄
            c += new Color(1f, 0.35f, 0.85f) * (1.3f * SoftBox(az, el, 90f + mirror, 4f, 70f, 5f)); // 분홍 네온 줄
        }
        c += Color.white * (2.6f * SoftBox(az, el, 0f, 72f, 80f, 18f));  // 천장 조명
        c += Color.white * (2.6f * SoftBox(az, el, 180f, 72f, 80f, 18f));
        c += Color.white * (0.4f * SoftBox(az, el, 0f, 0f, 400f, 4f));  // 수평선 띠(가장자리 반짝임)
        c.a = 1f;
        return c;
    }

    static Bounds BoundsOf(List<Vector3> v)
    {
        var b = new Bounds(v[0], Vector3.zero);
        foreach (var p in v) b.Encapsulate(p);
        return b;
    }

    static void Rotate(List<Vector3> verts, List<Vector3> normals, Quaternion q, Vector3 pivot)
    {
        if (q == Quaternion.identity) return;
        for (int i = 0; i < verts.Count; i++)
        {
            verts[i] = pivot + q * (verts[i] - pivot);
            normals[i] = q * normals[i];
        }
    }

    /// <summary>높이 비율 [t0, t1] 구간 정점의 Y축 기준 최대 반지름(중심 = 바운즈 중심)</summary>
    static float MaxRadius(List<Vector3> v, Bounds b, float t0, float t1)
    {
        float y0 = Mathf.Lerp(b.min.y, b.max.y, t0) - 1e-5f, y1 = Mathf.Lerp(b.min.y, b.max.y, t1) + 1e-5f, r = 0f;
        foreach (var p in v)
            if (p.y >= y0 && p.y <= y1)
                r = Mathf.Max(r, new Vector2(p.x - b.center.x, p.z - b.center.z).magnitude);
        return r;
    }

    /// <summary>고리 정점(segs개씩)들을 옆면 + 위·아래 마개로 이은 닫힌 메시 — 볼록 MeshCollider 쿠킹 입력</summary>
    static Mesh BuildPointHullMesh(List<Vector3> rings, int segs)
    {
        var v = new List<Vector3>(rings);
        int n = rings.Count / segs;
        var t = new List<int>();
        for (int r = 0; r < n - 1; r++)
            for (int k = 0; k < segs; k++)
            {
                int a = r * segs + k, b2 = r * segs + (k + 1) % segs, c = a + segs, d = b2 + segs;
                t.AddRange(new[] { a, c, b2, b2, c, d });
            }
        int bottom = v.Count; v.Add(new Vector3(0f, rings[0].y, 0f));
        int top = v.Count; v.Add(new Vector3(0f, rings[rings.Count - 1].y, 0f));
        for (int k = 0; k < segs; k++)
        {
            t.AddRange(new[] { bottom, k, (k + 1) % segs });
            int o = (n - 1) * segs;
            t.AddRange(new[] { top, o + (k + 1) % segs, o + k });
        }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>원뿔대(아래 반지름 rb, 위 rt, 높이 h, 중심 원점) — FBX가 없을 때의 그릇 대체</summary>
    static Mesh BuildFrustumMesh(float rb, float rt, float h, int segs, bool closed)
    {
        var rings = new List<Vector3>();
        for (int k = 0; k < segs; k++) { float a = k / (float)segs * Mathf.PI * 2f; rings.Add(new Vector3(Mathf.Cos(a) * rb, -h * 0.5f, Mathf.Sin(a) * rb)); }
        for (int k = 0; k < segs; k++) { float a = k / (float)segs * Mathf.PI * 2f; rings.Add(new Vector3(Mathf.Cos(a) * rt, h * 0.5f, Mathf.Sin(a) * rt)); }
        return BuildPointHullMesh(rings, segs);
    }

    // ─ 캡슐·폭탄(임시 절차 메시) ────────────────────────────────

    /// <summary>UV 구(반지름 r, 중심 c) — u = 경도, v = vMin~vMax(아래→위)</summary>
    static void AddUvSphere(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, float r, int lon, int lat, float vMin, float vMax)
    {
        int start = v.Count;
        for (int j = 0; j <= lat; j++)
        {
            float th = Mathf.PI * j / lat;          // 0 = 아래 극
            float y = -Mathf.Cos(th), rr = Mathf.Sin(th);
            for (int i = 0; i <= lon; i++)
            {
                float ph = 2f * Mathf.PI * i / lon;
                var dir = new Vector3(Mathf.Cos(ph) * rr, y, Mathf.Sin(ph) * rr);
                v.Add(c + dir * r);
                n.Add(dir);
                uv.Add(new Vector2(i / (float)lon, Mathf.Lerp(vMin, vMax, j / (float)lat)));
            }
        }
        for (int j = 0; j < lat; j++)
            for (int i = 0; i < lon; i++)
            {
                int a = start + j * (lon + 1) + i, b = a + 1, cc = a + lon + 1, d = cc + 1;
                t.AddRange(new[] { a, cc, b, b, cc, d });
            }
    }

    /// <summary>원기둥 옆면 + 윗마개(아래 y0 → 위 y1, 반지름 r), uv v 고정</summary>
    static void AddCylinder(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, float r, float y0, float y1, int segs, float vCoord)
    {
        int start = v.Count;
        for (int k = 0; k <= segs; k++)
        {
            float a = 2f * Mathf.PI * k / segs;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(c + d * r + Vector3.up * y0); n.Add(d); uv.Add(new Vector2(k / (float)segs, vCoord));
            v.Add(c + d * r + Vector3.up * y1); n.Add(d); uv.Add(new Vector2(k / (float)segs, vCoord));
        }
        for (int k = 0; k < segs; k++)
        {
            int a = start + k * 2, b = a + 1, cc = a + 2, d = a + 3;
            t.AddRange(new[] { a, b, cc, cc, b, d });
        }
        int capStart = v.Count;
        v.Add(c + Vector3.up * y1); n.Add(Vector3.up); uv.Add(new Vector2(0.5f, vCoord));
        for (int k = 0; k <= segs; k++)
        {
            float a = 2f * Mathf.PI * k / segs;
            v.Add(c + new Vector3(Mathf.Cos(a) * r, y1, Mathf.Sin(a) * r)); n.Add(Vector3.up); uv.Add(new Vector2(k / (float)segs, vCoord));
        }
        for (int k = 0; k < segs; k++) t.AddRange(new[] { capStart, capStart + 2 + k, capStart + 1 + k });
    }

    static Mesh FinishMesh(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetUVs(0, uv);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }

    /// <summary>캡슐: 지름 1 구 — 텍스처가 위 반구 색·이음 띠·아래 반구 흰색을 칠한다(v = 높이)</summary>
    static Mesh BuildCapsuleMesh()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        AddUvSphere(v, n, uv, t, Vector3.zero, 0.5f, 20, 14, 0f, 1f);
        return FinishMesh("Claw_Capsule", v, n, uv, t);
    }

    static Color CapsuleTexel(Color top, float vv)
    {
        if (vv > 0.535f) return Color.Lerp(top, Color.white, 0.15f);       // 위 반구(색)
        if (vv > 0.465f) return new Color(0.62f, 0.66f, 0.72f);             // 이음 띠
        return new Color(0.97f, 0.97f, 0.97f);                              // 아래 반구(흰색)
    }

    /// <summary>폭탄: 지름 1 구(v 0~0.8) + 금속 마개(0.85) + 심지(0.91) + 불꽃(0.97). 콜라이더는 구 — 심지는 시각만</summary>
    static Mesh BuildBombMesh()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        AddUvSphere(v, n, uv, t, Vector3.zero, 0.5f, 20, 14, 0f, 0.8f);
        AddCylinder(v, n, uv, t, Vector3.zero, 0.13f, 0.40f, 0.53f, 14, 0.85f);
        AddCylinder(v, n, uv, t, new Vector3(0.02f, 0f, 0f), 0.032f, 0.52f, 0.66f, 8, 0.91f);
        AddUvSphere(v, n, uv, t, new Vector3(0.02f, 0.68f, 0f), 0.055f, 8, 6, 0.97f, 0.97f);
        return FinishMesh("Claw_Bomb", v, n, uv, t);
    }

    /// <summary>폭탄 텍스처(세로 64): 몸통 진회색(OST에서 하이라이트로 형태가 보이게 너무 검지 않게) + 가운데 붉은 경고 띠 + 마개·심지·불꽃.
    /// emission=true면 발광 맵(띠·불꽃만 밝음)</summary>
    static Color BombTexel(float vv, bool emission)
    {
        if (vv >= 0.94f) return emission ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.85f, 0.4f);     // 불꽃
        if (vv >= 0.88f) return emission ? Color.black : new Color(0.75f, 0.62f, 0.42f);                // 심지
        if (vv >= 0.82f) return emission ? Color.black : new Color(0.55f, 0.56f, 0.6f);                 // 금속 마개
        bool stripe = vv > 0.36f && vv < 0.44f;                                                         // 구 적도(0.4) 띠
        if (stripe) return emission ? new Color(0.9f, 0.08f, 0.04f) : new Color(1f, 0.2f, 0.12f);
        return emission ? new Color(0.06f, 0.0f, 0.0f) : new Color(0.30f, 0.31f, 0.36f); // 비주얼 1차: 몸통 조금 밝게(광택이 보이게)
    }

    // ─ 공용: 에셋 저장 ──────────────────────────────────────────

    /// <summary>메시 에셋 저장 — 있으면 내용만 교체(GUID 유지)</summary>
    static Mesh SaveMesh(Mesh src, string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null)
        {
            src.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(src, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        m.Clear();
        m.indexFormat = src.indexFormat;
        m.SetVertices(src.vertices);
        if (src.normals.Length == src.vertexCount) m.SetNormals(src.normals);
        if (src.tangents.Length == src.vertexCount) m.SetTangents(src.tangents);
        if (src.uv.Length == src.vertexCount) m.SetUVs(0, src.uv);
        m.SetTriangles(src.triangles, 0);
        m.RecalculateBounds();
        EditorUtility.SetDirty(m);
        UnityEngine.Object.DestroyImmediate(src);
        return m;
    }

    /// <summary>작은 생성 텍스처(.asset, 무압축 RGBA32 — 수 KB). 있으면 픽셀만 교체(GUID 유지)</summary>
    static Texture2D SaveTexture(string path, int w, int h, Func<int, int, Color> texel)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        bool create = tex == null || tex.width != w || tex.height != h || tex.format != TextureFormat.RGBA32;
        if (create && tex != null) { AssetDatabase.DeleteAsset(path); tex = null; }
        // 밉맵 없음 — 4×64 띠 텍스처는 밉맵이 색 띠를 섞어 버린다
        if (tex == null) tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(path) };
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) px[y * w + x] = texel(x, y);
        tex.SetPixels(px);
        tex.Apply(false);
        if (create) AssetDatabase.CreateAsset(tex, path);
        else EditorUtility.SetDirty(tex);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ─ 폭발 연출 ────────────────────────────────────────────────

    /// <summary>폭발음: 짧은 충격(노이즈 어택) + 저역 노이즈 감쇠 + 60→35Hz 쿵. 기존 파일이 있으면 그대로(교체본 보존)</summary>
    static AudioClip EnsureExplosionSound()
    {
        EnsureFolder(VipFolder);
        if (MakeWav(ExplosionWav, SynthExplosion)) AssetDatabase.Refresh();
        return VipClip(ExplosionWav);
    }

    static float[] SynthExplosion()
    {
        var b = SndBuf(1.4f);
        var rng = new System.Random(15);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            float t = (float)i / SndRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float k = Mathf.Lerp(0.5f, 0.04f, Mathf.Clamp01(t / 0.5f));   // 처음엔 밝게, 점점 먹먹하게
            lp += (noise - lp) * k;
            lp2 += (lp - lp2) * 0.08f;
            float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * 4.2f);
            float crack = Mathf.Exp(-t * 40f) * noise * 0.6f;
            b[i] += env * (lp * 0.8f + lp2 * 1.6f) + crack;
        }
        SndChirp(b, 0f, 70f, 32f, 0.6f, 0.9f);
        return b;
    }

    static Material FxMaterial(string name, Texture2D tex)
    {
        var m = ClawAdditive(name, Color.white);
        m.SetTexture("_BaseMap", tex);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>연출용 파티클 텍스처·재질(동그란 점 / 고리) — 폭발·그릇 획득 공용</summary>
    static (Material dot, Material ring) FxMaterials()
    {
        var dot = SaveTexture($"{ClawItemFolder}/Claw_FxDot.asset", 64, 64, (x, y) =>
        {
            float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 31.5f;
            float a = Mathf.Clamp01(1f - d);
            return new Color(1f, 1f, 1f, a * a);
        });
        var ringTex = SaveTexture($"{ClawItemFolder}/Claw_FxRing.asset", 64, 64, (x, y) =>
        {
            float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 31.5f;
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.16f);
            return new Color(1f, 1f, 1f, a);
        });
        return (FxMaterial("Claw_FxDot", dot), FxMaterial("Claw_FxRing", ringTex));
    }

    /// <summary>한 번 터지는 파티클 시스템 하나(버스트, 월드 시뮬레이션, 색·크기 변화). 크기·속도는 k배(헤드 반지름 비례)</summary>
    static ParticleSystem FxBurst(Transform root, List<ParticleSystem> list, float k, string n, Material mat, int count,
                                  Vector2 life, Vector2 speed, Vector2 size, Gradient color, float grow, float gravity,
                                  ParticleSystemRenderMode mode, float shapeRadius, float coneAngle = -1f)
    {
        var go = new GameObject(n);
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x * k, speed.y * k);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x * k, size.y * k);
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = Mathf.Max(8, count * 2);
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.enabled = shapeRadius > 0f || coneAngle >= 0f;
        if (coneAngle >= 0f)
        {
            shape.shapeType = ParticleSystemShapeType.Cone;   // 위로 솟는 반짝이(셰이프 기본 방향 = 로컬 +z → 위로 눕힘)
            shape.angle = coneAngle;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }
        else shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.001f, shapeRadius * k);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = color;
        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, grow));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = mode;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.alignment = mode == ParticleSystemRenderMode.HorizontalBillboard ? ParticleSystemRenderSpace.World : ParticleSystemRenderSpace.View;
        if (mode == ParticleSystemRenderMode.Stretch) { r.lengthScale = 2.2f; r.velocityScale = 0.03f; }
        list.Add(ps);
        return ps;
    }

    static Gradient FxGradient(params (float t, Color c)[] keys)
    {
        var g = new Gradient();
        var ck = new GradientColorKey[keys.Length];
        var ak = new GradientAlphaKey[keys.Length];
        for (int i = 0; i < keys.Length; i++) { ck[i] = new GradientColorKey(keys[i].c, keys[i].t); ak[i] = new GradientAlphaKey(keys[i].c.a, keys[i].t); }
        g.SetKeys(ck, ak);
        return g;
    }

    /// <summary>파티클 묶음 + 소리 → ClawExplosionVfx(Play(위치, 소리)) — 폭발·그릇 획득 공용</summary>
    static ClawExplosionVfx FinishFx(GameObject root, List<ParticleSystem> list, AudioClip clip, float volume)
    {
        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;
        audio.volume = volume;
        var fx = root.AddComponent<ClawExplosionVfx>();
        SetRefArray(fx, "systems", list.ToArray());
        SetRef(fx, "audioSource", audio);
        if (clip != null) SetRef(fx, "clip", clip);
        return fx;
    }

    /// <summary>폭발 연출 1세트(번쩍임·불덩이·불꽃·충격파 고리·옅은 연기) — 크기·속도는 헤드 반지름 비례(기계 교체 대비)</summary>
    static ClawExplosionVfx BuildExplosionVfx(Transform parent, string name, AudioClip clip)
    {
        var (dotMat, ringMat) = FxMaterials();
        float k = ClawLayout.HeadRadius / 0.13f;
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var list = new List<ParticleSystem>();
        var tr = root.transform;

        FxBurst(tr, list, k, "Flash", dotMat, 1, new Vector2(0.16f, 0.16f), Vector2.zero, new Vector2(0.8f, 0.8f),
                FxGradient((0f, new Color(1f, 0.97f, 0.85f, 1f)), (1f, new Color(1f, 0.8f, 0.4f, 0f))), 1.6f, 0f, ParticleSystemRenderMode.Billboard, 0f);
        FxBurst(tr, list, k, "Fireball", dotMat, 22, new Vector2(0.35f, 0.65f), new Vector2(0.4f, 1.1f), new Vector2(0.12f, 0.22f),
                FxGradient((0f, new Color(1f, 0.9f, 0.55f, 1f)), (0.4f, new Color(1f, 0.45f, 0.1f, 0.9f)), (1f, new Color(0.7f, 0.12f, 0.05f, 0f))), 1.7f, -0.05f,
                ParticleSystemRenderMode.Billboard, 0.03f);
        FxBurst(tr, list, k, "Sparks", dotMat, 56, new Vector2(0.4f, 0.8f), new Vector2(1.5f, 3.2f), new Vector2(0.012f, 0.022f),
                FxGradient((0f, new Color(1f, 0.95f, 0.6f, 1f)), (0.6f, new Color(1f, 0.6f, 0.15f, 1f)), (1f, new Color(1f, 0.3f, 0.05f, 0f))), 0.6f, 0.6f,
                ParticleSystemRenderMode.Stretch, 0.02f);
        FxBurst(tr, list, k, "Shockwave", ringMat, 1, new Vector2(0.32f, 0.32f), Vector2.zero, new Vector2(0.12f, 0.12f),
                FxGradient((0f, new Color(1f, 0.75f, 0.35f, 0.9f)), (1f, new Color(1f, 0.5f, 0.2f, 0f))), 9f, 0f, ParticleSystemRenderMode.HorizontalBillboard, 0f);
        FxBurst(tr, list, k, "Smoke", dotMat, 8, new Vector2(0.8f, 1.2f), new Vector2(0.1f, 0.3f), new Vector2(0.12f, 0.2f),
                FxGradient((0f, new Color(0.7f, 0.7f, 0.75f, 0f)), (0.2f, new Color(0.6f, 0.6f, 0.65f, 0.28f)), (1f, new Color(0.5f, 0.5f, 0.55f, 0f))), 2.2f, -0.03f,
                ParticleSystemRenderMode.Billboard, 0.05f);
        return FinishFx(root, list, clip, 0.9f);
    }

    /// <summary>
    /// 그릇 획득 연출 1세트(비주얼 2차): 배출구에서 청록 번쩍임 + 바닥에 퍼지는 고리 2겹 + 위로 솟는 반짝이 + 짧은 빛기둥.
    /// "+1" 라벨(CollectVfxPool)은 그대로 두고 그 위에 겹친다.
    /// </summary>
    static ClawExplosionVfx BuildCollectVfx(Transform parent, string name, AudioClip clip)
    {
        var (dotMat, ringMat) = FxMaterials();
        float k = ClawLayout.HeadRadius / 0.13f;
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var list = new List<ParticleSystem>();
        var tr = root.transform;
        var cyan = new Color(0.35f, 1f, 1f, 1f);

        FxBurst(tr, list, k, "Flash", dotMat, 1, new Vector2(0.18f, 0.18f), Vector2.zero, new Vector2(0.38f, 0.38f),
                FxGradient((0f, new Color(0.85f, 1f, 1f, 1f)), (1f, new Color(0.3f, 0.9f, 1f, 0f))), 1.5f, 0f, ParticleSystemRenderMode.Billboard, 0f);
        FxBurst(tr, list, k, "Ring", ringMat, 1, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.1f, 0.1f),
                FxGradient((0f, new Color(0.5f, 1f, 1f, 1f)), (1f, new Color(0.2f, 0.8f, 1f, 0f))), 6f, 0f, ParticleSystemRenderMode.HorizontalBillboard, 0f);
        FxBurst(tr, list, k, "Ring2", ringMat, 1, new Vector2(0.7f, 0.7f), Vector2.zero, new Vector2(0.06f, 0.06f),
                FxGradient((0f, new Color(1f, 0.5f, 0.9f, 0.8f)), (1f, new Color(1f, 0.3f, 0.8f, 0f))), 8f, 0f, ParticleSystemRenderMode.HorizontalBillboard, 0f);
        FxBurst(tr, list, k, "Sparkles", dotMat, 34, new Vector2(0.6f, 1.1f), new Vector2(0.35f, 1.0f), new Vector2(0.012f, 0.024f),
                FxGradient((0f, new Color(1f, 1f, 1f, 1f)), (0.4f, cyan), (1f, new Color(0.3f, 0.8f, 1f, 0f))), 0.5f, -0.15f,
                ParticleSystemRenderMode.Billboard, 0.04f, coneAngle: 28f);
        FxBurst(tr, list, k, "Beam", dotMat, 6, new Vector2(0.35f, 0.5f), new Vector2(0.9f, 1.4f), new Vector2(0.05f, 0.07f),
                FxGradient((0f, new Color(0.6f, 1f, 1f, 0.9f)), (1f, new Color(0.3f, 0.9f, 1f, 0f))), 0.4f, 0f,
                ParticleSystemRenderMode.Stretch, 0.01f, coneAngle: 4f);
        return FinishFx(root, list, clip, 0.8f);
    }

    const string CollectWav = "collect.wav";

    /// <summary>그릇 획득 효과음: 밝게 올라가는 세 음(기존 파일이 있으면 그대로 — 교체본 보존)</summary>
    static AudioClip EnsureCollectSound()
    {
        EnsureFolder(VipFolder);
        if (MakeWav(CollectWav, SynthCollect)) AssetDatabase.Refresh();
        return VipClip(CollectWav);
    }

    static float[] SynthCollect()
    {
        var b = SndBuf(0.8f);
        SndTone(b, 0f, 1318.5f, 0.25f, 0.45f, 14f, 0.25f);
        SndTone(b, 0.07f, 1568f, 0.3f, 0.45f, 12f, 0.25f);
        SndTone(b, 0.14f, 2093f, 0.6f, 0.5f, 6f, 0.3f, 0.1f);
        return b;
    }
}
