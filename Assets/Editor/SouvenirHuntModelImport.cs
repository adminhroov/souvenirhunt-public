using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

// 외부 FBX 모델(Blender FBX + Substance 텍스처 세트) 임포트 → URP 재질·프리팹 자동 구성 (09-14).
// 규칙: Assets/Models/<Name>/<Name>.fbx + <Name>_BaseColor/_Normal/_Metallic/_Roughness/_Emissive/_opecity(_Opacity).png
// 메뉴 33 (AutoRun "33"): 폴더를 전부 훑어 규칙에 맞는 모델마다
//   ① FBX 임포터 정리(카메라·라이트·애니메이션 끔)  ② 노멀맵 타입 지정
//   ③ 채널 패킹 생성 — <Name>_BaseColorA.png(RGB=BaseColor, A=Opacity) / <Name>_MetallicSmoothness.png(R=Metallic, A=1−Roughness)
//   ④ 렌더러별 불투명도(삼각형 무게중심 UV 샘플)로 분류 → <Name>_Mat(불투명) / _Cutout(알파 테스트·양면) / _Glass(반투명·그림자 없음)
//   ⑤ FBX 내장 재질을 <Name>_Mat 으로 리맵  ⑥ <Name>.prefab(FBX 프리팹 변형)에 분류별 재질 배정·빈 메시 렌더러 끔
// 재실행 안전(덮어쓰기). 알파 테스트·반투명은 모바일 TBDR에서 비싸므로 필요한 렌더러에만 쓴다.
public static partial class SouvenirHuntSetup
{
    const string ImportedModelsRoot = "Assets/Models";

    enum SurfaceClass { Opaque, Cutout, Glass }

    [MenuItem("XREAL/33. Setup Imported Models (Models/<Name> FBX → URP 재질·프리팹)")]
    public static void SetupImportedModels()
    {
        var report = new StringBuilder("[ModelImport] 모델 구성:\n");
        int count = 0;
        try
        {
            foreach (var dir in Directory.GetDirectories(ImportedModelsRoot))
            {
                string name = Path.GetFileName(dir);
                string folder = $"{ImportedModelsRoot}/{name}";
                if (!File.Exists($"{folder}/{name}.fbx")) continue;
                if (Directory.Exists($"{folder}/Texture") && Directory.GetDirectories($"{folder}/Texture").Length > 0)
                    SetupImportedModelSets(folder, name, report); // 10-19: 텍스처 세트가 여러 벌(Texture/<세트>/)인 모델
                else
                    SetupImportedModel(folder, name, report);
                count++;
            }
            AssetDatabase.SaveAssets();
            report.AppendLine(count == 0 ? "  (규칙에 맞는 모델 없음)" : $"  완료: {count}개");
            Debug.Log(report.ToString());
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ModelImport] 실패: " + e);
        }
    }

    static void SetupImportedModel(string folder, string name, StringBuilder report)
    {
        string fbxPath = $"{folder}/{name}.fbx";
        string Tex(string suffix) => FindTexture(folder, name, suffix);

        // ① FBX 임포터
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (mi.importCameras || mi.importLights || mi.importAnimation || mi.animationType != ModelImporterAnimationType.None)
        {
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();
        }

        // ② 노멀맵
        string normalPath = Tex("Normal");
        if (normalPath != null)
            ConfigureTexture(normalPath, ti => { ti.textureType = TextureImporterType.NormalMap; });

        // ③ 채널 패킹
        var opacity = LoadRaw(Tex("Opacity"));
        string baseAPath = null, msPath = null;
        try
        {
            using (var baseCol = new RawTex(Tex("BaseColor")))
            {
                if (baseCol.tex == null) { report.AppendLine($"  {name}: BaseColor 없음 — 건너뜀"); return; }
                baseAPath = $"{folder}/{name}_BaseColorA.png";
                WritePacked(baseAPath, baseCol.tex, (c, u, v) =>
                {
                    c.a = opacity != null ? opacity.GetPixelBilinear(u, v).r : 1f;
                    return c;
                });
                ConfigureTexture(baseAPath, ti =>
                {
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = true;
                });
            }
            using (var metal = new RawTex(Tex("Metallic")))
            using (var rough = new RawTex(Tex("Roughness")))
            {
                var size = metal.tex ?? rough.tex;
                if (size != null)
                {
                    msPath = $"{folder}/{name}_MetallicSmoothness.png";
                    WritePacked(msPath, size, (c, u, v) =>
                    {
                        float m = metal.tex != null ? metal.tex.GetPixelBilinear(u, v).r : 0f;
                        float r = rough.tex != null ? rough.tex.GetPixelBilinear(u, v).r : 0.5f;
                        return new Color(m, m, m, 1f - r);
                    });
                    ConfigureTexture(msPath, ti =>
                    {
                        ti.sRGBTexture = false;
                        ti.alphaSource = TextureImporterAlphaSource.FromInput;
                        ti.alphaIsTransparency = false;
                    });
                }
            }

            // ④ 렌더러 분류
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var classes = new Dictionary<string, SurfaceClass>();
            var lines = new StringBuilder();
            int tris = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                tris += mesh.triangles.Length / 3;
                var cls = ClassifySurface(mesh, opacity, out string stats);
                classes[mf.name] = cls;
                if (cls != SurfaceClass.Opaque) lines.AppendLine($"      {mf.name} → {cls} ({stats})");
            }

            // ⑤ 재질
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseAPath);
            var normalTex = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;
            var msTex = msPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(msPath) : null;
            string emissivePath = Tex("Emissive");
            var emissiveTex = emissivePath != null && HasNonBlack(emissivePath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(emissivePath) : null;

            var matOpaque = BuildModelMaterial($"{folder}/{name}_Mat.mat", SurfaceClass.Opaque, baseTex, normalTex, msTex, emissiveTex);
            var matCutout = classes.ContainsValue(SurfaceClass.Cutout)
                ? BuildModelMaterial($"{folder}/{name}_Cutout.mat", SurfaceClass.Cutout, baseTex, normalTex, msTex, emissiveTex) : null;
            var matGlass = classes.ContainsValue(SurfaceClass.Glass)
                ? BuildModelMaterial($"{folder}/{name}_Glass.mat", SurfaceClass.Glass, baseTex, normalTex, msTex, emissiveTex) : null;

            // ⑥ 내장 재질 리맵 (FBX를 그대로 끌어놔도 불투명 재질로 보이게)
            bool remapped = false;
            mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath); // ① 재임포트 뒤 새 인스턴스
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>())
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), matOpaque);
                remapped = true;
            }
            if (remapped) mi.SaveAndReimport();

            // ⑦ 프리팹 변형
            string prefabPath = $"{folder}/{name}.prefab";
            root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(root);
            int disabled = 0;
            try
            {
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
                    {
                        mr.enabled = false;
                        disabled++;
                        continue;
                    }
                    var cls = classes.TryGetValue(mr.name, out var c) ? c : SurfaceClass.Opaque;
                    var mat = cls == SurfaceClass.Glass ? matGlass : cls == SurfaceClass.Cutout ? matCutout : matOpaque;
                    mr.sharedMaterials = Enumerable.Repeat(mat, mf.sharedMesh.subMeshCount).ToArray();
                    mr.shadowCastingMode = cls == SurfaceClass.Glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                }
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            }
            finally { Object.DestroyImmediate(inst); }

            var bounds = new Bounds();
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            report.AppendLine($"  {name}: 렌더러 {classes.Count}개 · 삼각형 {tris} · 크기 {bounds.size.x:F2}×{bounds.size.y:F2}×{bounds.size.z:F2}m" +
                              $" · 재질 Mat{(matCutout ? "+Cutout" : "")}{(matGlass ? "+Glass" : "")}" +
                              $" · 발광 {(emissiveTex != null ? "O" : "X")}{(disabled > 0 ? $" · 빈 메시 렌더러 {disabled}개 끔" : "")} → {prefabPath}");
            report.Append(lines);
        }
        finally
        {
            if (opacity != null) Object.DestroyImmediate(opacity);
        }
    }

    /// <summary>
    /// 텍스처 세트가 여러 벌인 모델(10-19 StartUpPark: 본체 아틀라스 + Trim). 규칙: Assets/Models/&lt;Name&gt;/&lt;Name&gt;.fbx + Texture/&lt;세트&gt;/*.png.
    /// 세트 안 파일 이름은 키워드로 찾는다(BaseColor|base_Color, Metallic|Metalness, Roughness, Normal(OpenGL 우선·DirectX 제외), Emissive, Opacity|opecity).
    /// FBX 내장 재질 → 세트: Unity가 이름으로 찾아 붙인 내장 재질의 기본색 텍스처 폴더(없으면 재질 순서 = 세트 이름 순서).
    /// 세트마다 BaseColorA·MetallicSmoothness를 그 세트 폴더에 만들고 &lt;Name&gt;_&lt;세트&gt;_Mat(+_Cutout·_Glass) 재질 → 렌더러는 서브메시별 내장 재질의 세트로 배정.
    /// </summary>
    static void SetupImportedModelSets(string folder, string name, StringBuilder report)
    {
        string fbxPath = $"{folder}/{name}.fbx";
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (mi.importCameras || mi.importLights || mi.importAnimation || mi.animationType != ModelImporterAnimationType.None)
        {
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();
        }

        var setDirs = Directory.GetDirectories($"{folder}/Texture").Select(d => d.Replace('\\', '/')).OrderBy(d => d).ToList();
        // FBX 재질 슬롯(원본 이름 → 지금 쓰이는 재질): 처음엔 내장 재질, 재실행이면 이미 리맵된 우리 재질(기본색 = 세트 폴더의 _BaseColorA)
        var slots = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>().Select(m => (src: m.name, mat: m)).ToList();
        foreach (var kv in mi.GetExternalObjectMap())
            if (kv.Key.type == typeof(Material) && kv.Value is Material ext && slots.All(s => s.src != kv.Key.name)) slots.Add((kv.Key.name, ext));
        slots = slots.OrderBy(s => s.src).ToList();

        // 재질 슬롯 → 세트 폴더 (재질 객체 기준 — FBX 렌더러가 참조하는 바로 그 재질)
        var setOfSlot = new Dictionary<Material, string>();
        var setOfEmbedded = new Dictionary<string, string>();
        for (int i = 0; i < slots.Count; i++)
        {
            var m = slots[i].mat;
            string texPath = null;
            foreach (var prop in new[] { "_BaseMap", "_MainTex" })
                if (texPath == null && m.HasProperty(prop) && m.GetTexture(prop) != null) texPath = AssetDatabase.GetAssetPath(m.GetTexture(prop));
            string dir = texPath != null ? Path.GetDirectoryName(texPath).Replace('\\', '/') : null;
            if (dir == null || !setDirs.Contains(dir)) dir = setDirs[Mathf.Min(i, setDirs.Count - 1)];
            setOfSlot[m] = dir;
            setOfEmbedded[slots[i].src] = dir;
        }

        // 세트별 텍스처·재질
        var mats = new Dictionary<string, (Material opaque, Material cutout, Material glass, Texture2D opacity)>();
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var classes = new Dictionary<string, SurfaceClass>();
        var lines = new StringBuilder();
        try
        {
            foreach (var setDir in setDirs.Where(d => setOfEmbedded.ContainsValue(d)))
            {
                string set = Path.GetFileName(setDir);
                string Find(params string[] keys) => FindTextureByKey(setDir, keys);
                string basePath = Find("BaseColor", "base_Color");
                if (basePath == null) { report.AppendLine($"  {name}/{set}: 기본색 텍스처 없음 — 건너뜀"); continue; }
                string normalPath = FindNormalTexture(setDir);
                if (normalPath != null) ConfigureTexture(normalPath, ti => { ti.textureType = TextureImporterType.NormalMap; });
                var opacity = LoadRaw(Find("Opacity", "opecity"));

                string baseAPath = $"{setDir}/{set}_BaseColorA.png";
                using (var baseCol = new RawTex(basePath))
                    WritePacked(baseAPath, baseCol.tex, (c, u, v) => { c.a = opacity != null ? opacity.GetPixelBilinear(u, v).r : 1f; return c; });
                ConfigureTexture(baseAPath, ti => { ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true; });

                string msPath = null;
                using (var metal = new RawTex(Find("Metallic", "Metalness")))
                using (var rough = new RawTex(Find("Roughness")))
                {
                    var size = metal.tex ?? rough.tex;
                    if (size != null)
                    {
                        msPath = $"{setDir}/{set}_MetallicSmoothness.png";
                        WritePacked(msPath, size, (c, u, v) =>
                        {
                            float m = metal.tex != null ? metal.tex.GetPixelBilinear(u, v).r : 0f;
                            float r = rough.tex != null ? rough.tex.GetPixelBilinear(u, v).r : 0.5f;
                            return new Color(m, m, m, 1f - r);
                        });
                        ConfigureTexture(msPath, ti => { ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false; });
                    }
                }

                // 이 세트를 쓰는 렌더러 분류(서브메시 0의 내장 재질 기준)
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
                    var em = mr.sharedMaterials.FirstOrDefault();
                    if (em == null || !setOfSlot.TryGetValue(em, out var d) || d != setDir) continue;
                    var cls = ClassifySurface(mf.sharedMesh, opacity, out string stats);
                    classes[mr.name] = cls;
                    if (cls != SurfaceClass.Opaque) lines.AppendLine($"      {mr.name} → {cls} ({stats})");
                }

                var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseAPath);
                var normalTex = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;
                var msTex = msPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(msPath) : null;
                string emissivePath = Find("Emissive");
                var emissiveTex = emissivePath != null && HasNonBlack(emissivePath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(emissivePath) : null;
                string prefix = $"{folder}/{name}_{set}";
                mats[setDir] = (
                    BuildModelMaterial($"{prefix}_Mat.mat", SurfaceClass.Opaque, baseTex, normalTex, msTex, emissiveTex),
                    BuildModelMaterial($"{prefix}_Cutout.mat", SurfaceClass.Cutout, baseTex, normalTex, msTex, emissiveTex),
                    BuildModelMaterial($"{prefix}_Glass.mat", SurfaceClass.Glass, baseTex, normalTex, msTex, emissiveTex),
                    opacity);
                report.AppendLine($"  {name}/{set}: 기본색 {Path.GetFileName(basePath)} · 노멀 {(normalPath != null ? Path.GetFileName(normalPath) : "없음")} · " +
                                  $"금속·거칠기 {(msPath != null ? "O" : "X")} · 발광 {(emissiveTex != null ? "O" : "X")} · 불투명도 {(opacity != null ? "O" : "X")}");
            }
        }
        finally
        {
            foreach (var v in mats.Values) if (v.opacity != null) Object.DestroyImmediate(v.opacity);
        }

        // 내장 재질 리맵 → 세트의 불투명 재질
        mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        bool remapped = false;
        foreach (var s in slots)
            if (setOfEmbedded.TryGetValue(s.src, out var d) && mats.TryGetValue(d, out var sm) && s.mat != sm.opaque)
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), s.src), sm.opaque);
                remapped = true;
            }
        if (remapped) mi.SaveAndReimport();

        // 프리팹 변형: 렌더러 분류 × 서브메시별 세트
        string prefabPath = $"{folder}/{name}.prefab";
        root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(root);
        int tris = 0, renderers = 0;
        var bounds = new Bounds(); bool first = true;
        try
        {
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) { mr.enabled = false; continue; }
                var cls = classes.TryGetValue(mr.name, out var c) ? c : SurfaceClass.Opaque;
                var cur = mr.sharedMaterials;
                var next = new Material[mf.sharedMesh.subMeshCount];
                for (int i = 0; i < next.Length; i++)
                {
                    // 리맵 뒤에는 서브메시 재질이 이미 세트 불투명 재질 — 그 재질이 속한 세트를 역으로 찾는다
                    var m = i < cur.Length ? cur[i] : cur.LastOrDefault();
                    var entry = mats.Values.FirstOrDefault(v => v.opaque == m);
                    if (entry.opaque == null) entry = mats.Values.First();
                    next[i] = cls == SurfaceClass.Glass ? entry.glass : cls == SurfaceClass.Cutout ? entry.cutout : entry.opaque;
                }
                mr.sharedMaterials = next;
                mr.shadowCastingMode = cls == SurfaceClass.Glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                tris += mf.sharedMesh.triangles.Length / 3;
                renderers++;
                if (first) { bounds = mr.bounds; first = false; } else bounds.Encapsulate(mr.bounds);
            }
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
        }
        finally { Object.DestroyImmediate(inst); }

        // 쓰이지 않은 컷아웃·유리 재질은 지운다(빈 재질 에셋이 쌓이지 않게)
        foreach (var v in mats.Values)
        {
            if (!UsesMaterial(prefabPath, v.cutout)) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(v.cutout));
            if (!UsesMaterial(prefabPath, v.glass)) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(v.glass));
        }
        report.AppendLine($"  {name}: 세트 {mats.Count}벌 · 렌더러 {renderers}개 · 삼각형 {tris} · 크기 {bounds.size.x:F2}×{bounds.size.y:F2}×{bounds.size.z:F2}m → {prefabPath}");
        report.Append(lines);
    }

    static bool UsesMaterial(string prefabPath, Material mat)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        return p != null && mat != null && p.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Contains(mat));
    }

    /// <summary>세트 폴더에서 키워드(대소문자 무시)가 들어간 PNG — 우리가 만든 패킹 결과(_BaseColorA·_MetallicSmoothness)는 제외</summary>
    static string FindTextureByKey(string dir, params string[] keys)
    {
        foreach (var file in Directory.GetFiles(dir, "*.png").OrderBy(f => f))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            if (stem.EndsWith("_BaseColorA") || stem.EndsWith("_MetallicSmoothness")) continue;
            if (keys.Any(k => stem.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)) return $"{dir}/{Path.GetFileName(file)}";
        }
        return null;
    }

    /// <summary>노멀맵 — Unity는 OpenGL(Y+) 규약. 이름에 DirectX가 들어간 것은 피하고 OpenGL을 우선</summary>
    static string FindNormalTexture(string dir)
    {
        var all = Directory.GetFiles(dir, "*.png").Select(Path.GetFileName)
            .Where(f => f.IndexOf("Normal", System.StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(f => f).ToList();
        string pick = all.FirstOrDefault(f => f.IndexOf("DirectX", System.StringComparison.OrdinalIgnoreCase) < 0 && f.IndexOf("OpenGL", System.StringComparison.OrdinalIgnoreCase) >= 0)
                      ?? all.FirstOrDefault(f => f.IndexOf("DirectX", System.StringComparison.OrdinalIgnoreCase) < 0)
                      ?? all.FirstOrDefault();
        return pick != null ? $"{dir}/{pick}" : null;
    }

    /// <summary>불투명도 평균 0.9 이상 = 불투명 / 완전 투명 면이 섞임 = 컷아웃(잎·스텐실) / 그 외 반투명 = 유리.</summary>
    static SurfaceClass ClassifySurface(Mesh mesh, Texture2D opacity, out string stats)
    {
        stats = "";
        if (opacity == null || mesh.uv == null || mesh.uv.Length == 0) return SurfaceClass.Opaque;
        var uv = mesh.uv;
        var tris = mesh.triangles;
        int n = tris.Length / 3, clear = 0;
        float sum = 0;
        for (int i = 0; i < n; i++)
        {
            var c = (uv[tris[i * 3]] + uv[tris[i * 3 + 1]] + uv[tris[i * 3 + 2]]) / 3f;
            float a = opacity.GetPixelBilinear(c.x, c.y).r;
            sum += a;
            if (a < 0.02f) clear++;
        }
        if (n == 0) return SurfaceClass.Opaque;
        float mean = sum / n;
        stats = $"평균 불투명도 {mean:F2}, 투명 면 {clear * 100 / n}%";
        if (mean >= 0.9f) return SurfaceClass.Opaque;
        return clear > 0 ? SurfaceClass.Cutout : SurfaceClass.Glass;
    }

    static Material BuildModelMaterial(string path, SurfaceClass cls, Texture2D baseTex, Texture2D normal, Texture2D ms, Texture2D emissive)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        mat.SetFloat("_WorkflowMode", 1f); // Metallic
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        mat.SetTexture("_MetallicGlossMap", ms);
        mat.SetFloat("_Metallic", 1f);   // 맵이 있으면 무시됨
        mat.SetFloat("_Smoothness", 1f); // 맵이 있으면 알파에 곱하는 스케일
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.SetTexture("_EmissionMap", emissive);
        mat.SetColor("_EmissionColor", emissive != null ? Color.white : Color.black);
        mat.globalIlluminationFlags = emissive != null ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        mat.SetFloat("_Surface", cls == SurfaceClass.Glass ? 1f : 0f);
        mat.SetFloat("_Blend", 0f); // Alpha
        mat.SetFloat("_AlphaClip", cls == SurfaceClass.Cutout ? 1f : 0f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.SetFloat("_Cull", cls == SurfaceClass.Cutout ? (float)CullMode.Off : (float)CullMode.Back);
        mat.renderQueue = -1; // 표면 타입 기본 큐 (Geometry / AlphaTest / Transparent)

        // URP 인스펙터와 동일한 경로로 키워드·블렌드·패스 설정 (_NORMALMAP·_METALLICSPECGLOSSMAP·_EMISSION·_ALPHATEST_ON·투명 블렌드)
        BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);
        if (emissive != null) mat.EnableKeyword("_EMISSION"); else mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>접미사로 텍스처 찾기 (대소문자 무시, Opacity는 원본 오타 "opecity"도 허용).</summary>
    static string FindTexture(string folder, string name, string suffix)
    {
        var aliases = suffix == "Opacity" ? new[] { "Opacity", "opecity" } : new[] { suffix };
        foreach (var file in Directory.GetFiles(folder, "*.png"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            foreach (var a in aliases)
                if (string.Equals(stem, $"{name}_{a}", System.StringComparison.OrdinalIgnoreCase))
                    return $"{folder}/{Path.GetFileName(file)}";
        }
        return null;
    }

    static void ConfigureTexture(string path, System.Action<TextureImporter> apply)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) { AssetDatabase.ImportAsset(path); ti = (TextureImporter)AssetImporter.GetAtPath(path); }
        apply(ti);
        ti.SaveAndReimport();
    }

    /// <summary>원본 크기로 픽셀마다 변환해 PNG로 저장 후 임포트.</summary>
    static void WritePacked(string path, Texture2D size, System.Func<Color, float, float, Color> pixel)
    {
        int w = size.width, h = size.height;
        var src = size.GetPixels();
        var dst = new Color[src.Length];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                dst[i] = pixel(src[i], (x + 0.5f) / w, v);
            }
        }
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        try
        {
            outTex.SetPixels(dst);
            File.WriteAllBytes(path, outTex.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(outTex); }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    static bool HasNonBlack(string path)
    {
        var t = LoadRaw(path);
        if (t == null) return false;
        try { return t.GetPixels32().Any(p => p.r > 8 || p.g > 8 || p.b > 8); }
        finally { Object.DestroyImmediate(t); }
    }

    /// <summary>임포터 설정과 무관하게 PNG 원본 값을 그대로(sRGB 변환 없이) 읽는다.</summary>
    static Texture2D LoadRaw(string path)
    {
        if (path == null || !File.Exists(path)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (t.LoadImage(File.ReadAllBytes(path))) return t;
        Object.DestroyImmediate(t);
        return null;
    }

    sealed class RawTex : System.IDisposable
    {
        public readonly Texture2D tex;
        public RawTex(string path) { tex = LoadRaw(path); }
        public void Dispose() { if (tex != null) Object.DestroyImmediate(tex); }
    }
}
