using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using SouvenirHunt;
using Debug = UnityEngine.Debug;

// Phase 4 — 베이크 조명 · Bloom · VFX 풀 · FPS 계측 · 한글 폰트 static 베이크 (인수인계 §4-7, §5 Phase 4).
// SouvenirHuntSetup의 partial 확장 — 헬퍼(SetRef·CreateText·GetOrCreateKoreanFont 등)를 그대로 재사용.
// 메뉴 17: 씬·에셋 규칙 적용(멱등)  /  18: 라이트맵 베이크(동기)  /  19: 폰트 베이크.
public static partial class SouvenirHuntSetup
{
    const string LightingSettingsPath = "Assets/Settings/SouvenirHuntLighting.lighting";
    const string VolumeProfilePath = "Assets/Settings/SouvenirHuntVolume.asset";
    const string SparkMatPath = "Assets/Settings/VfxSpark.mat";
    const string BurstPrefabPath = "Assets/Prefabs/CollectBurst.prefab";
    const string MobileRpPath = "Assets/Settings/Mobile_RPAsset.asset";
    const string PcRpPath = "Assets/Settings/PC_RPAsset.asset";
    const string KsxListPath = "Assets/Fonts/ksx1001_hangul.txt";
    const string FallbackFontPath = "Assets/Fonts/NotoSansKR-Bold Fallback SDF.asset";

    /// <summary>자동 실행(SouvenirHuntAutoRun)·연속 실행 시 확인 다이얼로그 생략</summary>
    public static bool Quiet;

    /// <summary>메뉴 17/18이 다루는 씬 — 기본 Main. 메뉴 30/31 또는 AutoRun 토큰 @main/@claw이 바꾼다(09-03).</summary>
    public static string TargetScenePath = ScenePath;
    static bool IsClawTarget => TargetScenePath == ClawScenePath;

    // ─ 17. 조명 규칙 + Bloom + VFX 풀 + Perf ──────────────────────

    [MenuItem("XREAL/17. Setup Lighting + VFX + Perf (Phase 4)")]
    public static void SetupLighting()
    {
        try
        {
            if (!EnsureTargetSceneOpen(out var scene)) return;
            var font = GetOrCreateKoreanFont();
            var report = new StringBuilder($"[SouvenirHuntSetup] Phase 4 적용 ({Path.GetFileNameWithoutExtension(TargetScenePath)}):\n");

            // 1) 실시간 라이트 1개(Directional, Mixed, 섀도우 없음) — §4-7
            Light sun = null;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                var go = new GameObject("Directional Light");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
                go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.None;
            sun.intensity = 1f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            RenderSettings.sun = sun;
            report.AppendLine("  · Directional Light: Mixed, 섀도우 OFF");

            // 2) 환경광: 스카이박스 없음(OST 글래스 검정=투명), Trilight 앰비언트
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.36f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.11f, 0.10f);
            if (IsClawTarget)
            {
                // 비주얼 1차(10-15, Claw만): OST에서 어두운 면은 비쳐 보이므로 주변광(특히 아래)을 올리고,
                // 비어 있던 환경 반사(= 검정 = 투명)를 밝은 스튜디오 큐브맵으로 채운다 — 캡슐·그릇·기계 유리에 광택
                RenderSettings.ambientSkyColor = new Color(0.62f, 0.65f, 0.72f);
                RenderSettings.ambientEquatorColor = new Color(0.45f, 0.46f, 0.50f);
                RenderSettings.ambientGroundColor = new Color(0.30f, 0.29f, 0.28f);
                var studio = EnsureStudioEnv();
                if (studio != null)
                {
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    RenderSettings.customReflectionTexture = studio;
                    RenderSettings.reflectionIntensity = 1f;
                    report.AppendLine("  · Claw 비주얼: 주변광 0.62/0.45/0.30 · 환경 반사 = Claw_StudioEnv(스튜디오 큐브맵)");
                }
                else report.AppendLine("  ⚠ Claw_StudioEnv 큐브맵 생성 실패 — 반사 비어 있음");
            }

            // 3) LightingSettings 에셋 — 보수적 해상도, AO ON, 비방향성, 실시간 GI OFF
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "SouvenirHuntLighting" };
                AssetDatabase.CreateAsset(ls, LightingSettingsPath);
                ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            }
            ls.realtimeGI = false;
            ls.bakedGI = true;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU; // 미지원 시 Unity가 CPU로 폴백
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapResolution = 12f;
            ls.lightmapPadding = 2;
            ls.lightmapMaxSize = 1024;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.ao = true;
            ls.aoMaxDistance = 1f;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 128;
            ls.prioritizeView = false;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
            report.AppendLine("  · LightingSettings: res 12, max 1024, AO, NonDirectional, IndirectOnly");

            // 4) 환경 오브젝트 static(ContributeGI) 확인 — Claw 씬은 제외(기계 시각 루트가 재정렬·흔들림으로 움직임, 라이트맵 미사용)
            var boothRoot = GameObject.Find("BoothRoot");
            var env = boothRoot != null ? boothRoot.transform.Find("Environment") : null;
            int staticCount = 0;
            if (IsClawTarget)
            {
                report.AppendLine("  · Claw: static 플래그·라이트맵 생략(기계는 움직이는 시각 루트, 방향광+앰비언트로 충분)");
            }
            else if (env != null)
            {
                foreach (var r in env.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject)
                                | StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic
                                | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                    r.receiveGI = ReceiveGI.Lightmaps;
                    staticCount++;
                }
                report.AppendLine($"  · 환경 static 렌더러 {staticCount}개");
            }
            else report.AppendLine("  ⚠ Environment 없음 — 메뉴 13을 먼저 실행하면 라이트맵 대상이 생깁니다");

            // 5) 라이트 프로브: 스폰 영역(1.5~5m, 수직 −1.4~+3.5m) 격자 — 수집품(동적)은 라이트맵 대신 프로브
            //    Claw 씬은 메뉴 30이 바스켓 주변 소격자(SpawnProbes)를 이미 만들어 두므로 유지
            if (IsClawTarget)
            {
                var existing = GameObject.Find("SpawnProbes");
                report.AppendLine(existing != null ? "  · LightProbeGroup SpawnProbes(바스켓 소격자) 유지" : "  ⚠ SpawnProbes 없음 — 메뉴 30 재실행");
            }
            else
            {
                var lpg = ReplaceSingleton<LightProbeGroup>("SpawnProbes", boothRoot != null ? boothRoot.transform : null);
                var positions = new List<Vector3>();
                float[] ys = { -1.4f, -0.2f, 1.0f, 2.2f, 3.4f };
                for (float x = -5f; x <= 5.01f; x += 1.25f)
                    for (float z = -3f; z <= 5.51f; z += 1.25f)
                    {
                        float horiz = Mathf.Sqrt(x * x + z * z);
                        if (horiz > 5.6f) continue;
                        foreach (var y in ys) positions.Add(new Vector3(x, y, z));
                    }
                lpg.probePositions = positions.ToArray();
                report.AppendLine($"  · LightProbeGroup SpawnProbes: {positions.Count}개");
            }

            // 6) Collectible 프리팹: 섀도우 OFF·프로브 수신·HDR 글로우(Bloom용)
            var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                foreach (var r in prefabRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                }
                var col = prefabRoot.GetComponent<Collectible>();
                if (col != null)
                {
                    var so = new SerializedObject(col);
                    so.FindProperty("glowColor").colorValue = new Color(0f, 2.5f, 2.5f, 1f); // HDR 시안 → Bloom
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
            report.AppendLine("  · Collectible 프리팹: 섀도우 OFF, BlendProbes, glow HDR(0,2.5,2.5)");

            // 7) 포스트프로세싱: Bloom 1개만 (§4-7)
            var profile = GetOrCreateBloomProfile();
            var vol = ReplaceSingleton<Volume>("GlobalVolume");
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;

            var cam = FindMainCamera();
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(cam);
                report.AppendLine($"  · 카메라 '{cam.name}': PostProcessing ON(Bloom), AA None, 섀도우 OFF");
            }
            else report.AppendLine("  ⚠ 카메라 없음 — PostProcessing 플래그 미적용");

            // 8) URP 에셋: 섀도우·추가 라이트 OFF, 볼륨 프로필 교체 (Mobile=Android, PC=에디터 재생)
            foreach (var rpPath in new[] { MobileRpPath, PcRpPath })
                report.AppendLine("  · " + ApplyRpAssetRules(rpPath, profile));

            // 9) VFX 풀 (스파크 + "+1")
            var burstPrefab = BuildCollectBurstPrefab(font);
            var pool = ReplaceSingleton<CollectVfxPool>("CollectVfxPool");
            SetRef(pool, "prefab", burstPrefab.GetComponent<CollectBurst>());
            report.AppendLine("  · CollectVfxPool(4) + CollectBurst 프리팹");

            // 10) Perf: PerfMonitor + HUD 오버레이(config showFps)
            ReplaceSingleton<PerfMonitor>("PerfMonitor");

            var hud = GameObject.Find("HUD");
            var hudCanvas = hud != null ? hud.transform.Find("HudCanvas") : null;
            if (hudCanvas != null)
            {
                var oldText = hudCanvas.Find("PerfText");
                if (oldText != null) UnityEngine.Object.DestroyImmediate(oldText.gameObject);
                var oldOverlay = hud.GetComponent<PerfOverlay>();
                if (oldOverlay != null) UnityEngine.Object.DestroyImmediate(oldOverlay);

                var perfText = CreateText(hudCanvas, "PerfText", "-- fps", font, 28, new Color(1f, 0.9f, 0.3f),
                                          new Vector2(0f, 325f), new Vector2(760f, 40f));
                perfText.gameObject.SetActive(false); // showFps=true일 때만 PerfOverlay가 켬
                var overlay = hud.AddComponent<PerfOverlay>();
                SetRef(overlay, "text", perfText);
                report.AppendLine("  · PerfMonitor + HUD PerfText (config showFps=true 시 표시)");
            }
            else report.AppendLine("  ⚠ HUD/HudCanvas 없음 — FPS 오버레이 생략 (PerfMonitor 로그는 동작)");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            report.AppendLine("다음: 메뉴 18(라이트맵 베이크) → 19(폰트 베이크) → 11(APK)");
            Debug.Log(report.ToString());
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup", report.ToString(), "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] SetupLighting FAILED: " + e);
        }
    }

    /// <summary>Main 전용 메뉴(13·16·20·21)가 사용 — 항상 Main.unity.</summary>
    static bool EnsureMainSceneOpen(out Scene scene) => EnsureSceneOpen(ScenePath, out scene);

    /// <summary>씬 공용 메뉴(17·18)가 사용 — TargetScenePath(Main 또는 Claw).</summary>
    static bool EnsureTargetSceneOpen(out Scene scene)
    {
        if (!File.Exists(TargetScenePath))
        {
            Debug.LogError($"[SouvenirHuntSetup] 대상 씬 없음: {TargetScenePath} — 먼저 메뉴 {(TargetScenePath == ClawScenePath ? "30" : "10")} 실행");
            scene = default;
            return false;
        }
        return EnsureSceneOpen(TargetScenePath, out scene);
    }

    static bool EnsureSceneOpen(string path, out Scene scene)
    {
        scene = EditorSceneManager.GetActiveScene();
        if (scene.path == path) return true;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        return true;
    }

    static Camera FindMainCamera()
    {
        var cam = Camera.main;
        if (cam != null) return cam;
        foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.CompareTag("MainCamera")) return c;
        var all = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return all.Length > 0 ? all[0] : null;
    }

    static VolumeProfile GetOrCreateBloomProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        }

        // Bloom 외 오버라이드는 전부 제거 (§4-7 "그 외 금지")
        for (int i = profile.components.Count - 1; i >= 0; i--)
        {
            var c = profile.components[i];
            if (c is Bloom) continue;
            profile.components.RemoveAt(i);
            if (c != null) AssetDatabase.RemoveObjectFromAsset(c);
        }

        if (!profile.TryGet<Bloom>(out var bloom))
        {
            bloom = profile.Add<Bloom>(true);
            bloom.name = "Bloom";
            AssetDatabase.AddObjectToAsset(bloom, profile);
        }
        bloom.active = true;
        bloom.threshold.Override(1.0f);
        bloom.intensity.Override(0.7f);
        bloom.scatter.Override(0.6f);
        bloom.highQualityFiltering.Override(false);
        bloom.maxIterations.Override(4);   // 모바일: 반복 수 축소(기본 6)
        EditorUtility.SetDirty(bloom);
        EditorUtility.SetDirty(profile);
        return profile;
    }

    // URP 에셋은 SerializedObject로 직접 — 공개 setter가 없는 필드(섀도우 지원 등) 포함
    static string ApplyRpAssetRules(string path, VolumeProfile profile)
    {
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        if (rp == null) return $"{Path.GetFileName(path)}: 없음(건너뜀)";
        var so = new SerializedObject(rp);
        so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
        so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 0;   // Disabled
        so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
        so.FindProperty("m_ShadowDistance").floatValue = 0f;
        so.FindProperty("m_SupportsLightLayers").boolValue = false;
        so.FindProperty("m_VolumeProfile").objectReferenceValue = profile;
        float scale = so.FindProperty("m_RenderScale").floatValue;
        bool hdr = so.FindProperty("m_SupportsHDR").boolValue;
        int msaa = so.FindProperty("m_MSAA").intValue;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        return $"{Path.GetFileName(path)}: 섀도우 OFF, 추가라이트 OFF, 볼륨=Bloom | 유지: RenderScale {scale}, HDR {(hdr ? "ON" : "OFF")}, MSAA {msaa}x (프로파일링 후 결정)";
    }

    // ─ VFX: CollectBurst 프리팹 (ParticleSystem 스파크 + TMP "+1") ─

    static GameObject BuildCollectBurstPrefab(TMP_FontAsset font)
    {
        EnsureFolder("Assets/Prefabs");
        var mat = GetOrCreateSparkMaterial();

        var root = new GameObject("CollectBurst");

        var sparkGo = new GameObject("Spark");
        sparkGo.transform.SetParent(root.transform, false);
        var ps = sparkGo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
        main.startColor = new Color(0f, 2.5f, 2.5f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 32;
        main.gravityModifier = 0.35f;
        main.stopAction = ParticleSystemStopAction.None;
        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });
        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.05f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var psr = sparkGo.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = mat;
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.lightProbeUsage = LightProbeUsage.Off;
        psr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var labelGo = new GameObject("PlusOne");
        labelGo.transform.SetParent(root.transform, false);
        var tmp = labelGo.AddComponent<TextMeshPro>();
        tmp.text = "+1";
        tmp.fontSize = 1.6f;           // 3D TMP: 10pt ≈ 1m → 약 0.16m
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.6f, 1f, 1f, 1f);
        if (font != null) tmp.font = font;
        tmp.rectTransform.sizeDelta = new Vector2(1f, 0.4f);
        var lr = labelGo.GetComponent<MeshRenderer>();
        if (lr != null)
        {
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = LightProbeUsage.Off;
            lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        labelGo.SetActive(false);

        var burst = root.AddComponent<CollectBurst>();
        SetRef(burst, "spark", ps);
        SetRef(burst, "label", tmp);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, BurstPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static Material GetOrCreateSparkMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SparkMatPath);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, SparkMatPath);
            mat = AssetDatabase.LoadAssetAtPath<Material>(SparkMatPath);
        }
        var tex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        // Additive 투명 (URP Particles/Unlit: Surface=Transparent, Blend=Additive)
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        // URP 파티클 가산 = SrcAlpha·One — 재임포트 때 URP가 계산하는 값과 같게(10-12, 커밋본 .mat 기준)
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.SetFloat("_ColorMode", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ─ 18. 라이트맵 베이크 ─────────────────────────────────────────

    [MenuItem("XREAL/18. Bake Lightmaps (Phase 4)")]
    public static void BakeLightmaps()
    {
        try
        {
            if (!EnsureTargetSceneOpen(out var scene)) return;
            if (Lightmapping.lightingSettings == null || GameObject.Find("SpawnProbes") == null)
            {
                Debug.LogWarning("[SouvenirHuntSetup] 조명 설정이 없습니다 — 메뉴 17을 먼저 실행하세요");
                if (!Application.isBatchMode && !Quiet)
                    EditorUtility.DisplayDialog("SouvenirHunt Setup", "메뉴 17(Setup Lighting)을 먼저 실행하세요.", "확인");
                return;
            }

            var sw = Stopwatch.StartNew();
            bool ok = Lightmapping.Bake();
            sw.Stop();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            int maps = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0;
            int probes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
            long bytes = 0;
            string dataDir = Path.Combine(Path.GetDirectoryName(TargetScenePath) ?? "", Path.GetFileNameWithoutExtension(TargetScenePath));
            if (Directory.Exists(dataDir))
                foreach (var f in Directory.GetFiles(dataDir))
                    if (!f.EndsWith(".meta")) bytes += new FileInfo(f).Length;

            string msg = $"[SouvenirHuntSetup] 라이트맵 베이크 {(ok ? "완료" : "실패")} — {sw.Elapsed.TotalSeconds:F0}초, " +
                         $"라이트맵 {maps}장, 프로브 {probes}개, 데이터 {bytes / 1024f / 1024f:F1}MB ({dataDir})";
            Debug.Log(msg);
            if (!Application.isBatchMode && !Quiet) EditorUtility.DisplayDialog("SouvenirHunt Setup", msg, "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] BakeLightmaps FAILED: " + e);
        }
    }

    // ─ 19. 한글 폰트 static 베이크 ────────────────────────────────
    // 주 폰트(90pt): 코드·config에 쓰인 문자만 미리 넣고 Static — 런타임 글리프 생성(히치) 제거.
    // 폴백(40pt, 2048×4096): KS X 1001 한글 2350자 전부 — 운영자가 config 보상 문구를 바꿔도 생성 없이 표시.
    // 주 폰트가 Dynamic이면 TMP가 폴백보다 먼저 주 아틀라스에 추가를 시도하므로 반드시 Static.

    [MenuItem("XREAL/19. Bake Korean Font (Phase 4)")]
    public static void BakeKoreanFont()
    {
        try
        {
            var primary = GetOrCreateKoreanFont();
            if (primary == null)
            {
                Debug.LogError("[SouvenirHuntSetup] 한글 폰트 에셋 없음 — Assets/Fonts에 ttf/otf 필요");
                return;
            }

            // 1) 주 폰트: 사용 문자 수집 → (아틀라스 2048²로 확장) → 추가 → Static
            string used = CollectUsedCharacters();
            primary.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            primary.isMultiAtlasTexturesEnabled = true;
            if (primary.atlasWidth < 2048 || primary.atlasHeight < 2048)
            {
                // 90pt+9pad ≈ 108px 셀 → 1024²엔 81자뿐(600자면 8장). 2048²로 올려 2장으로 — 텍스트 서브메시 분할 최소화
                var soF = new SerializedObject(primary);
                soF.FindProperty("m_AtlasWidth").intValue = 2048;
                soF.FindProperty("m_AtlasHeight").intValue = 2048;
                soF.ApplyModifiedPropertiesWithoutUndo();
            }
            primary.ClearFontAssetData(true); // 기존 페이지 정리 후 새 크기로 재생성 (재실행 멱등)
            primary.TryAddCharacters(used, out string missingPrimary);
            primary.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(primary);

            // 2) 폴백 폰트: KS X 1001 2350자 + ASCII + 기호
            var fallback = GetOrCreateFallbackFont(primary);
            string missingFallback = "";
            if (fallback != null)
            {
                var ksxAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(KsxListPath);
                string ksx = ksxAsset != null ? ksxAsset.text.Trim() : "";
                if (ksx.Length == 0) Debug.LogWarning("[SouvenirHuntSetup] " + KsxListPath + " 없음 — 폴백에 코드 사용 문자만 들어갑니다");
                fallback.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fallback.isMultiAtlasTexturesEnabled = true;
                fallback.TryAddCharacters(used + ksx, out missingFallback);
                fallback.atlasPopulationMode = AtlasPopulationMode.Static;
                EditorUtility.SetDirty(fallback);

                primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
                EditorUtility.SetDirty(primary);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var msg = new StringBuilder("[SouvenirHuntSetup] 한글 폰트 베이크 완료\n");
            msg.AppendLine($"  주 폰트 '{primary.name}': 글리프 {primary.characterTable.Count}개, 아틀라스 {primary.atlasTextures?.Length ?? 0}장 ({primary.atlasWidth}×{primary.atlasHeight}), Static" +
                           (string.IsNullOrEmpty(missingPrimary) ? "" : $", 누락 {missingPrimary.Length}자"));
            if (fallback != null)
                msg.AppendLine($"  폴백 '{fallback.name}': 글리프 {fallback.characterTable.Count}개, 아틀라스 {fallback.atlasTextures?.Length ?? 0}장 ({fallback.atlasWidth}×{fallback.atlasHeight}), Static" +
                               (string.IsNullOrEmpty(missingFallback) ? "" : $", 누락 {missingFallback.Length}자"));
            else
                msg.AppendLine("  ⚠ 폴백 폰트 생성 실패 — 원본 ttf/otf를 Assets/Fonts에서 찾지 못함");
            Debug.Log(msg.ToString());
            if (!Application.isBatchMode && !Quiet) EditorUtility.DisplayDialog("SouvenirHunt Setup", msg.ToString(), "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] BakeKoreanFont FAILED: " + e);
        }
    }

    // 코드(SouvenirHunt 런타임 + Editor 셋업)와 기본 config에 등장하는 모든 문자 + ASCII 전체 + UI 기호
    static string CollectUsedCharacters()
    {
        var set = new HashSet<char>();
        for (char c = ' '; c <= '~'; c++) set.Add(c);
        foreach (char c in ConfigLoader.DefaultConfigText) if (c > ' ') set.Add(c);
        foreach (char c in "✓·—–→←↓↑□■●○★☆…℃%,.()[]{}:;!?/+-=×÷°") set.Add(c); // ↓↑: 인형뽑기 가이드 화살표(4차)

        var dirs = new[]
        {
            Path.Combine(Application.dataPath, "Scripts", "SouvenirHunt"),
            Path.Combine(Application.dataPath, "Editor"),
        };
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                foreach (char c in File.ReadAllText(file, Encoding.UTF8))
                    if (c > ' ' && !char.IsControl(c) && !char.IsSurrogate(c)) set.Add(c);
        }

        var sb = new StringBuilder(set.Count);
        foreach (var c in set) sb.Append(c);
        return sb.ToString();
    }

    static TMP_FontAsset GetOrCreateFallbackFont(TMP_FontAsset primary)
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontPath);
        if (fa != null) return fa;

        Font source = primary.sourceFontFile;
        if (source == null)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { FontFolder }))
            {
                source = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (source != null) break;
            }
        }
        if (source == null) return null;

        // 40pt·padding 5 → 50px 셀, 2048×4096 = 약 3,300 글리프 (2350 + ASCII·기호 여유)
        fa = TMP_FontAsset.CreateFontAsset(source, 40, 5, GlyphRenderMode.SDFAA, 2048, 4096,
                                           AtlasPopulationMode.Dynamic, true);
        if (fa == null) return null;
        fa.name = Path.GetFileNameWithoutExtension(FallbackFontPath);
        AssetDatabase.CreateAsset(fa, FallbackFontPath);
        fa.material.name = fa.name + " Material";
        fa.atlasTexture.name = fa.name + " Atlas";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
        AssetDatabase.SaveAssets();
        Debug.Log("[SouvenirHuntSetup] 폴백 폰트 생성: " + FallbackFontPath);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontPath);
    }
}
