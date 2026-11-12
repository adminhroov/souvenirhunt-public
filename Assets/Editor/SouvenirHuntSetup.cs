using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;
using SouvenirHunt;

// XR 기념품 찾기 Main 씬·에셋 자동 구성 (Phase 0+1).
// 메뉴 번호: 0 Player/XR 설정 · 10 씬 신규 생성(파괴적!) · 11 빌드 · 12 진단 · 13 부스 환경 · 14 운영자 패널 ·
//           16 360 레퍼런스 · 17~19 조명/폰트(Phase4.cs) · 20 Phase 5 배선 · 21 보상 UI+씬 보정(Phase5.cs)
// 10 이외는 전부 멱등(재실행 안전). 10은 Main.unity를 새로 만들므로 13→14→17→19→18→20→21 재실행이 필요하다.
public static partial class SouvenirHuntSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string PrefabPath = "Assets/Prefabs/Collectible.prefab";
    const string LibraryPath = "Assets/Settings/MeshLibrary.asset";
    const string FontFolder = "Assets/Fonts";

    /// <summary>앱 버전 — 메뉴 0/10(ApplyIdentity)·11(빌드)이 공유. 릴리스마다 여기만 올린다.</summary>
    public const string AppVersion = "0.3.0";
    /// <summary>메뉴 10 이후 재실행해야 하는 순서 (10 완료 메시지·문서에 사용)</summary>
    public const string RebuildOrder = "13 → 14 → 17 → 19 → 18 → 20 → 21";

    [MenuItem("XREAL/10. Setup SouvenirHunt Scene (씬 신규 생성 — 파괴적)")]
    public static void SetupScene()
    {
        try
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // 파괴적 작업 확인 — Main.unity를 빈 씬에서 다시 만들므로 13/14/16/17/18/20/21 결과가 전부 사라진다
            if (!Application.isBatchMode && !Quiet && File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("⚠ Main.unity 재생성",
                    "Main.unity를 새로 만듭니다.\n부스 환경(13)·파노라마(16)·조명/VFX/프로브(17)·라이트맵(18)·Phase 5 배선(20)·보상 UI(21)가 모두 사라집니다.\n\n" +
                    $"계속하면 이후 {RebuildOrder} 순서로 재실행해야 합니다.",
                    "계속(재생성)", "취소"))
                return;

            ApplyIdentity();

            var koreanFont = GetOrCreateKoreanFont();
            var library = BuildMeshLibrary();
            var prefab = BuildCollectiblePrefab(library);
            WriteStreamingConfig();

            // ─ 씬 구성 ─────────────────────────────────────────
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // XREAL 리그 (SouvenirPop과 동일 처리: CameraYOffset=0 → 눈높이 = y0)
            var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.xreal.xr/Runtime/Prefabs/XR Interaction Setup.prefab");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
            rig.AddComponent<XRRigDedup>(); // 10-15: 씬 전환 때 이전 씬 리그(SDK가 DontDestroyOnLoad)를 지워 카메라 중복 렌더 방지
            var origin = rig.GetComponentInChildren<XROrigin>();
            if (origin != null) origin.CameraYOffset = 0f;

            // BoothRoot > SpawnPoints(25개 자동 생성)
            var boothRoot = new GameObject("BoothRoot");
            boothRoot.AddComponent<BoothAligner>();
            var spawnPoints = new GameObject("SpawnPoints");
            spawnPoints.transform.SetParent(boothRoot.transform, false);
            spawnPoints.AddComponent<SpawnPointGizmo>();
            GenerateSpawnPoints(spawnPoints.transform);

            // 풀 + 게임 매니저
            var poolGo = new GameObject("CollectiblePool");
            var pool = poolGo.AddComponent<CollectiblePool>();
            SetRef(pool, "prefab", prefab.GetComponent<Collectible>());

            var gmGo = new GameObject("GameManager");
            var spawner = gmGo.AddComponent<SpawnManager>();
            SetRef(spawner, "spawnPointsRoot", spawnPoints.transform);
            SetRef(spawner, "pool", pool);
            var gm = gmGo.AddComponent<GameManager>();
            SetRef(gm, "spawner", spawner);
            SetRef(gm, "meshLibrary", library);

            BuildStartPanel(boothRoot.transform, koreanFont);
            BuildResultPanel(boothRoot.transform, koreanFont);
            BuildHud(koreanFont);
            BuildRewardUi(boothRoot.transform, koreanFont); // [보상 확인] 버튼 + 보상 패널(SCR-06) — 메뉴 21과 동일 함수
            // 리셋은 SCR-07 운영자 패널(메뉴 14)만 — Phase 1 임시 DevResetButton은 08-27 제거

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath); // 목록을 덮어쓰지 않음 — 부팅 씬(Main/Claw)은 메뉴 31이 관리(09-03)

            Debug.Log("[SouvenirHuntSetup] Main 씬 구성 완료: " + ScenePath + $" — 이어서 {RebuildOrder} 재실행 필요" +
                      (koreanFont == null ? "\n주의: Assets/Fonts에 한글 폰트(ttf/otf)가 없어 기본 폰트 사용 — 한글이 □로 보입니다." : ""));
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup",
                    "Main 씬 구성 완료!\n(리그·스폰 25·풀·패널·HUD·보상 UI·config)\n\n" +
                    $"⚠ 새 씬입니다 — 반드시 {RebuildOrder} 순서로 재실행하세요." +
                    (koreanFont == null ? "\n\n⚠ 한글 폰트 없음 — Assets/Fonts에 ttf/otf를 넣고 재실행하세요." : ""), "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] FAILED: " + e);
        }
    }

    const string ApkPath = "Builds/SouvenirHunt.apk";
    const string PrevApkPath = "Builds/prev/SouvenirHunt.apk";

    /// <summary>
    /// 10-29 버전 B(큐브 3·폭탄 0(vc59부터, vc56~58은 3)·칸막이 낮춤·가운데 복귀) — 사용자 "A(0.3.0 vc55)와 B를 Beam Pro에 동시에 따로 설치": 별도 패키지·앱 이름으로 빌드한다.
    /// 빌드 동안만 PlayerSettings를 B로 바꾸고 끝나면 A(com.hroov.souvenirhunt / SouvenirHunt / AppVersion)로 되돌린다(versionCode만 +1 남음 — A·B 공용 카운터).
    /// 실행 앱 B(com.hroov.souvenirhunt.launcher.b)는 tools/beam-launcher/build.py --variant B
    /// </summary>
    public const string PackageB = "com.hroov.souvenirhunt.b";
    public const string ProductNameB = "B SouvenirHunt"; // 10-29(2) 사용자 요청: 앱 이름 앞에 "B"
    public const string AppVersionB = "0.4.0";
    const string ApkPathB = "Builds/SouvenirHunt_B.apk";
    const string PrevApkPathB = "Builds/prev/SouvenirHunt_B.apk";

    /// <summary>
    /// 10-29 감사 I2: 지금 소스(게임 규칙 기본값)가 어느 변형인가. "B"면 메뉴 11(A 패키지 0.3.0)을 거부한다 — A에 B 규칙이 들어가지 않게.
    /// A를 다시 만들려면 커밋 8578a7e(A vc55 소스)에서 빌드한다. 소스 기본값을 A로 되돌릴 때만 "A"로 바꾼다
    /// </summary>
    const string SourceVariant = "B";
    static bool SourceIs(string v) => SourceVariant == v; // const끼리 직접 비교하면 뒤 코드가 '도달 불가'(CS0162) 경고 — 함수로 감쌈

    [MenuItem("XREAL/11. Build SouvenirHunt APK")]
    public static void BuildApk()
    {
        if (!SourceIs("A"))
        {
            // 배치·AutoRun(토큰 11)도 같은 거부 — 창 없이 오류 로그만
            Debug.LogError($"[SouvenirHuntSetup] 소스 기본값이 {SourceVariant} — A(0.3.0)는 커밋 8578a7e에서 빌드. 중단");
            return;
        }
        BuildApkCore(ApkPath, PrevApkPath, AppVersion);
    }

    [MenuItem("XREAL/11b. Build SouvenirHunt B APK (별도 패키지 com.hroov.souvenirhunt.b)")]
    public static void BuildApkB()
    {
        var android = NamedBuildTarget.Android;
        string id = PlayerSettings.GetApplicationIdentifier(android), name = PlayerSettings.productName, ver = PlayerSettings.bundleVersion;
        try
        {
            PlayerSettings.SetApplicationIdentifier(android, PackageB);
            PlayerSettings.productName = ProductNameB;
            Debug.Log($"[SouvenirHuntSetup] 버전 B 빌드: {PackageB} / {ProductNameB} / {AppVersionB} (끝나면 {id} / {name} / {ver}로 되돌림)");
            BuildApkCore(ApkPathB, PrevApkPathB, AppVersionB);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(android, id);
            PlayerSettings.productName = name;
            PlayerSettings.bundleVersion = ver;
            AssetDatabase.SaveAssets();
            Debug.Log($"[SouvenirHuntSetup] PlayerSettings A로 되돌림: {PlayerSettings.GetApplicationIdentifier(android)} / {PlayerSettings.productName} / {PlayerSettings.bundleVersion} · versionCode {PlayerSettings.Android.bundleVersionCode}");
        }
    }

    /// <summary>11·11b 공통 — apkPath로 빌드, version = bundleVersion(10-29 B 분리로 매개변수화)</summary>
    static void BuildApkCore(string apkPath, string prevApkPath, string version)
    {
        const string ndkPath = @"C:\Users\user\Android\android-ndk-r27c";
        if (Directory.Exists(ndkPath))
            UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndkPath;

        // 버전: bundleVersion은 AppVersion 상수, versionCode는 빌드마다 +1 (기기에 구버전이 남지 않게). 실패 시 되돌려 번호 연속 유지.
        PlayerSettings.bundleVersion = version;
        int prevCode = PlayerSettings.Android.bundleVersionCode;
        int code = prevCode + 1;
        PlayerSettings.Android.bundleVersionCode = code;

        // 직전 APK 1세대 보관 (롤백용)
        try
        {
            if (File.Exists(apkPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(prevApkPath));
                File.Copy(apkPath, prevApkPath, true);
            }
        }
        catch (Exception e) { Debug.LogWarning("[SouvenirHuntSetup] 직전 APK 보관 실패(무시): " + e.Message); }

        var scenes = BuildSceneList(); // enabled 씬만(부팅 씬 1개가 기본) — 메뉴 31로 Main ↔ Claw 전환
        Debug.Log("[SouvenirHuntSetup] 빌드 씬: " + string.Join(", ", scenes));
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        var matSnaps = ReadMaterialsOnDisk();
        int fixedBefore = RestoreMaterials(matSnaps, "빌드 전");
        var report = BuildPipeline.BuildPlayer(options);
        bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        if (!ok) PlayerSettings.Android.bundleVersionCode = prevCode;
        int strippedDuring = RestoreMaterials(matSnaps, "빌드 중");
        if (strippedDuring > 0)
            Debug.LogError($"[SouvenirHuntSetup] 빌드 도중 재질 {strippedDuring}개가 바뀜 — 이 APK는 발광·블렌드가 틀렸을 수 있음. 재질은 복원했으니 다시 빌드할 것");
        AssetDatabase.SaveAssets(); // ProjectSettings의 versionCode 변경 영구화
        if (fixedBefore > 0) Debug.LogWarning($"[SouvenirHuntSetup] 빌드 전 재질 {fixedBefore}개를 디스크(커밋본) 기준으로 복원 — 빌드에는 복원된 상태로 들어감");
        Debug.Log($"[SouvenirHuntSetup] Build {report.summary.result} v{version} (versionCode {(ok ? code : prevCode)}), " +
                  $"size: {report.summary.totalSize / (1024 * 1024)}MB, errors: {report.summary.totalErrors} → {apkPath} ({PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)})");
        if (ok && !Application.isBatchMode && !Quiet)
            EditorUtility.RevealInFinder(apkPath);
    }

    /// <summary>
    /// 10-06: 재질 보호. 셋업 메뉴는 MPB 글로우를 위해 이미션 색이 검정이어도 _EMISSION을 켜 두고 블렌드 값도 직접 정해 두는데,
    /// 에디터가 재질을 다시 임포트할 때(URP 재질 검증 추정 — 배치 -nographics뿐 아니라 GUI 에디터에서도 10-06 재현) _EMISSION을 지우고
    /// _SrcBlend 등도 고쳐 쓴다. 빌드 뒤 SaveAssets가 그것을 디스크에 써 버린다.
    /// 디스크의 .mat(= 커밋본)을 기준으로 빌드 직전에 키워드만 되돌린다(블렌드는 차이만 로그 — 실기기 육안 확인 대상).
    /// </summary>
    class MatSnapshot { public string path; public List<string> keywords = new(); public Dictionary<string, float> blend = new(); }

    static readonly string[] BlendProps = { "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_ZWrite" };

    static List<MatSnapshot> ReadMaterialsOnDisk()
    {
        var list = new List<MatSnapshot>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Settings", "Assets/Models" }))
        {
            var snap = new MatSnapshot { path = AssetDatabase.GUIDToAssetPath(guid) };
            try
            {
                bool inValid = false;
                foreach (var line in File.ReadLines(snap.path))
                {
                    if (line.StartsWith("  m_ValidKeywords:")) { inValid = !line.Contains("[]"); continue; }
                    if (inValid)
                    {
                        if (line.StartsWith("  - ")) { snap.keywords.Add(line.Substring(4).Trim()); continue; }
                        inValid = false;
                    }
                    string t = line.Trim();
                    foreach (var prop in BlendProps)
                        if (t.StartsWith("- " + prop + ": ") &&
                            float.TryParse(t.Substring(prop.Length + 4), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                            snap.blend[prop] = v;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[SouvenirHuntSetup] {snap.path} 읽기 실패: {e.Message}"); continue; }
            list.Add(snap);
        }
        return list;
    }

    /// <summary>메모리 재질에서 디스크 스냅샷 대비 빠진 키워드를 다시 켠다. 고친 재질 수 반환(로그에 이름). 블렌드 차이는 로그만</summary>
    static int RestoreMaterials(List<MatSnapshot> snaps, string when)
    {
        var fixedNames = new List<string>();
        var blendNotes = new List<string>();
        foreach (var snap in snaps)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(snap.path);
            if (mat == null) continue;
            var what = new List<string>();
            foreach (var kw in snap.keywords)
                if (!mat.IsKeywordEnabled(kw)) { mat.EnableKeyword(kw); what.Add(kw); }
            if (what.Count > 0) EditorUtility.SetDirty(mat);
            // 블렌드 값은 기록만 — 되돌리면 빌드 도중 URP가 재질을 일괄 재검증해 오히려 _EMISSION이 지워진다(10-06 versionCode 29·30에서 확인)
            foreach (var kv in snap.blend)
                if (mat.HasProperty(kv.Key) && !Mathf.Approximately(mat.GetFloat(kv.Key), kv.Value))
                    blendNotes.Add($"{mat.name} {kv.Key} 디스크 {kv.Value} / 에디터 {mat.GetFloat(kv.Key)}");
            if (what.Count == 0) continue;
            fixedNames.Add($"{mat.name}({string.Join(" ", what)})");
        }
        if (blendNotes.Count > 0)
            Debug.Log($"[SouvenirHuntSetup] 재질 블렌드 차이({when}, 에디터 값으로 빌드): " + string.Join(", ", blendNotes));
        Debug.Log($"[SouvenirHuntSetup] 재질 검사({when}): 대상 {snaps.Count}개, 디스크와 달랐던 것 {fixedNames.Count}개" +
                  (fixedNames.Count > 0 ? " → " + string.Join(", ", fixedNames) : ""));
        return fixedNames.Count;
    }

    // ─ 프로젝트 정체성 ─────────────────────────────────────────

    static void ApplyIdentity()
    {
        PlayerSettings.productName = "SouvenirHunt";
        PlayerSettings.companyName = "HROOV"; // (주)에이치루브 — appId com.hroov.* 와 일치
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.hroov.souvenirhunt");
        PlayerSettings.bundleVersion = AppVersion;
        Debug.Log($"[SouvenirHuntSetup] Identity: SouvenirHunt / com.hroov.souvenirhunt / {AppVersion}");
    }

    // ─ MeshLibrary + 더미 머티리얼 ─────────────────────────────

    static MeshLibrary BuildMeshLibrary()
    {
        EnsureFolder("Assets/Settings");

        Mesh capsule = PrimitiveMesh(PrimitiveType.Capsule);
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere);
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube);

        var entries = new List<MeshLibrary.Entry>
        {
            new() { label = "생수(더미 핑크 캡슐)", mesh = capsule,
                    material = DummyMat("Dummy_Water_Pink", new Color(1f, 0.55f, 0.75f)) },
            new() { label = "키링(KoreaCubeKeyring)", mesh = MeshFromModel("Assets/Models/KoreaCubeKeyring/KoreaCubeKeyring.obj"),
                    material = ModelMat("KoreaCubeKeyring", "Assets/Models/KoreaCubeKeyring/KoreaCubeKeyring.png") },
            new() { label = "약과(더미 노랑 구)", mesh = sphere,
                    material = DummyMat("Dummy_Yakgwa_Yellow", new Color(1f, 0.85f, 0.2f)) },
            new() { label = "선물상자(더미 흰 큐브)", mesh = cube,
                    material = DummyMat("Dummy_GiftBox_White", Color.white) },
            new() { label = "여분(MeshyModel)", mesh = MeshFromModel("Assets/Models/MeshyModel/MeshyModel.obj"),
                    material = ModelMat("MeshyModel", "Assets/Models/MeshyModel/MeshyModel.png") },
        };
        // 임포트 안 된 모델 항목은 제외 (인덱스 0~2는 항상 유효하도록 더미가 앞에 오지 않게 순서 유지 필수 —
        // config 기본값: 0=생수, 1=키링, 2=약과. 키링 모델이 없으면 더미 큐브로 대체해 인덱스를 지킨다.)
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].mesh == null)
            {
                Debug.LogWarning($"[SouvenirHuntSetup] '{entries[i].label}' 메시 없음 → 더미 큐브로 대체 (인덱스 유지)");
                var e = entries[i];
                e.mesh = cube;
                if (e.material == null) e.material = DummyMat("Dummy_GiftBox_White", Color.white);
                entries[i] = e;
            }
        }

        var lib = AssetDatabase.LoadAssetAtPath<MeshLibrary>(LibraryPath);
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<MeshLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }
        lib.SetEntries(entries.ToArray());
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return lib;
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(go);
        return mesh;
    }

    static Mesh MeshFromModel(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) return null;
        var mf = asset.GetComponentInChildren<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    // URP Lit 더미 머티리얼 — MPB 이미션 글로우가 먹도록 _EMISSION 키워드 활성 필수
    static Material DummyMat(string name, Color color)
    {
        string path = $"Assets/Settings/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        mat.SetColor("_BaseColor", color);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // URP가 재임포트 때 _EMISSION을 지우지 않게(10-06)
        mat.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Meshy 모델 머티리얼 — 구 프로젝트(menu 4/5)가 만든 에셋 재사용, 없으면 텍스처로 생성
    static Material ModelMat(string name, string texPath)
    {
        string path = $"Assets/Models/{name}_Mat.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) return null;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", tex);
            AssetDatabase.CreateAsset(mat, path);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // URP가 재임포트 때 _EMISSION을 지우지 않게(10-06)
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ─ Collectible 프리팹 ──────────────────────────────────────

    static GameObject BuildCollectiblePrefab(MeshLibrary library)
    {
        EnsureFolder("Assets/Prefabs");
        const float visualSize = 0.2f;

        var root = new GameObject("Collectible");
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        var filter = visual.AddComponent<MeshFilter>();
        var renderer = visual.AddComponent<MeshRenderer>();

        var entry = library.Get(0); // 기본 비주얼 = 0번 (스폰 시 SetVisual로 교체됨)
        filter.sharedMesh = entry.mesh;
        renderer.sharedMaterial = entry.material;

        var b = entry.mesh.bounds;
        float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        float s = maxDim > 0f ? visualSize / maxDim : 1f;
        visual.transform.localScale = Vector3.one * s;
        visual.transform.localPosition = -b.center * s;

        var box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = b.size * s * 1.2f; // 시각 모델보다 20% 크게 (기획서 §08)

        var rb = root.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;

        root.AddComponent<XRSimpleInteractable>();
        var col = root.AddComponent<Collectible>();
        SetRef(col, "visualRenderer", renderer);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    // ─ 스폰 포인트 25곳 자동 생성 (3DoF 규칙: 1.5~5m, ±120°, −30°~+45°) ─

    static void GenerateSpawnPoints(Transform parent)
    {
        var rng = new System.Random(83); // 고정 시드 — 재실행 멱등
        const int count = 25;
        const float minSeparation = 0.7f;
        var placed = new List<Vector3>();

        int guard = 0;
        while (placed.Count < count && guard++ < 5000)
        {
            float yaw = Mathf.Lerp(-120f, 120f, (float)rng.NextDouble());
            float pitch = Mathf.Lerp(-30f, 45f, (float)rng.NextDouble());
            float dist = Mathf.Lerp(1.5f, 5f, (float)rng.NextDouble());

            var dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            var pos = dir * dist;
            pos.y = Mathf.Clamp(pos.y, -1.0f, 2.2f); // 실내 천장·바닥 고려한 실용 범위

            bool tooClose = false;
            foreach (var p in placed)
                if (Vector3.Distance(p, pos) < minSeparation) { tooClose = true; break; }
            if (tooClose) continue;

            placed.Add(pos);
        }

        for (int i = 0; i < placed.Count; i++)
        {
            var pt = new GameObject($"SP_{i:00}");
            pt.transform.SetParent(parent, false);
            pt.transform.localPosition = placed[i];
        }
        Debug.Log($"[SouvenirHuntSetup] 스폰 포인트 {placed.Count}개 생성");
    }

    // ─ UI ──────────────────────────────────────────────────────

    static void BuildStartPanel(Transform boothRoot, TMP_FontAsset font)
    {
        // 루트(인터랙터블) + 자식 캔버스(visualRoot) — 전방 2m 눈높이 (기획서 SCR-01)
        var root = new GameObject("StartPanel");
        root.transform.SetParent(boothRoot, false);
        root.transform.localPosition = new Vector3(0f, 0f, 2f);

        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(0.9f, 0.45f, 0.02f);

        root.AddComponent<XRSimpleInteractable>();

        var canvasGo = CreateWorldCanvas("StartCanvas", root.transform, new Vector2(900, 450));
        CreatePanelBackground(canvasGo.transform, new Color(0.06f, 0.1f, 0.16f, 0.92f));
        CreateText(canvasGo.transform, "Title", "XR 기념품 찾기", font, 90, new Color(0.4f, 0.9f, 1f),
                   new Vector2(0, 90), new Vector2(860, 140));
        CreateText(canvasGo.transform, "Button", "게임 시작하기", font, 64, Color.white,
                   new Vector2(0, -80), new Vector2(860, 120));

        var ctrl = root.AddComponent<StartPanelController>();
        SetRef(ctrl, "visualRoot", canvasGo);
    }

    static void BuildResultPanel(Transform boothRoot, TMP_FontAsset font)
    {
        var root = new GameObject("ResultPanel");
        root.transform.SetParent(boothRoot, false);
        root.transform.localPosition = new Vector3(0f, 0f, 2f);

        var canvasGo = CreateWorldCanvas("ResultCanvas", root.transform, new Vector2(900, 600));
        CreatePanelBackground(canvasGo.transform, new Color(0.06f, 0.1f, 0.16f, 0.92f));
        var title = CreateText(canvasGo.transform, "Title", "미션 완료!", font, 84, new Color(0.4f, 0.9f, 1f),
                               new Vector2(0, 180), new Vector2(860, 130));
        var detail = CreateText(canvasGo.transform, "Detail", "", font, 52, Color.white,
                                new Vector2(0, 40), new Vector2(860, 150));
        // 힌트·[보상 확인] 버튼·보상 패널은 BuildRewardUi(메뉴 10·21 공용)가 붙인다

        var ctrl = root.AddComponent<ResultPanelController>();
        SetRef(ctrl, "panelRoot", canvasGo);
        SetRef(ctrl, "titleText", title);
        SetRef(ctrl, "detailText", detail);
    }

    static void BuildHud(TMP_FontAsset font)
    {
        // HUD 루트: lazy follow (head-lock 금지 — 멀미 방지 요건)
        var root = new GameObject("HUD");
        var canvasGo = CreateWorldCanvas("HudCanvas", root.transform, new Vector2(1100, 700));

        // Playing 중 표시 요소 (기획서 SCR-03: 좌상단 n/10+진행바, 우상단 m:ss)
        var hudRoot = new GameObject("HudRoot");
        hudRoot.transform.SetParent(canvasGo.transform, false);
        hudRoot.AddComponent<RectTransform>();

        var count = CreateText(hudRoot.transform, "Count", "0 / 10", font, 56, new Color(0.4f, 0.9f, 1f),
                               new Vector2(-330, 300), new Vector2(380, 90));
        var time = CreateText(hudRoot.transform, "Time", "--:--", font, 56, new Color(0.4f, 0.9f, 1f),
                              new Vector2(330, 300), new Vector2(380, 90));

        var barBg = CreateBar(hudRoot.transform, "ProgressBg", new Color(1f, 1f, 1f, 0.2f),
                              new Vector2(-330, 240), new Vector2(380, 22));
        var barFill = CreateBar(barBg.transform, "ProgressFill", new Color(0.4f, 0.9f, 1f, 0.95f),
                                Vector2.zero, new Vector2(380, 22));
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillAmount = 0f;

        // 카운트다운 (SCR-02: 중앙 대형 3·2·1)
        var cdRoot = new GameObject("CountdownRoot");
        cdRoot.transform.SetParent(canvasGo.transform, false);
        cdRoot.AddComponent<RectTransform>();
        var cdText = CreateText(cdRoot.transform, "CountdownText", "3", font, 300, new Color(0.4f, 0.9f, 1f),
                                Vector2.zero, new Vector2(600, 400));

        // 초기 상태는 숨김 — 바인딩 전 첫 프레임에 남은 텍스트가 보이지 않게
        hudRoot.SetActive(false);
        cdRoot.SetActive(false);

        var hud = root.AddComponent<HudController>();
        SetRef(hud, "countText", count);
        SetRef(hud, "progressBar", barFill);
        SetRef(hud, "timeText", time);
        SetRef(hud, "countdownText", cdText);
        SetRef(hud, "countdownRoot", cdRoot);
        SetRef(hud, "hudRoot", hudRoot);

        root.AddComponent<SouvenirHunt.LazyFollow>(); // XRI Starter Assets LazyFollow와 단순명 충돌 — 완전수식 필수
    }

    // ─ UI 헬퍼 ─────────────────────────────────────────────────

    static GameObject CreateWorldCanvas(string name, Transform parent, Vector2 sizePx)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = sizePx;
        rt.localScale = Vector3.one * 0.001f; // 1000px = 1m
        return go;
    }

    static void CreatePanelBackground(Transform parent, Color color)
    {
        var go = new GameObject("Bg");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = color;
    }

    static TMP_Text CreateText(Transform parent, string name, string text, TMP_FontAsset font,
                               float size, Color color, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        if (font != null) tmp.font = font;
        return tmp;
    }

    static Image CreateBar(Transform parent, string name, Color color, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    // ─ 한글 TMP 폰트 (Dynamic 아틀라스 — 1.1만 음절 베이크 회피) ─

    static TMP_FontAsset GetOrCreateKoreanFont()
    {
        // 이미 만든 폰트 에셋이 있으면 재사용
        foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { FontFolder }))
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (fa != null && !fa.name.Contains("Fallback")) return fa; // 폴백 폰트(메뉴 19)는 주 폰트가 아님
        }

        if (!AssetDatabase.IsValidFolder(FontFolder)) return null;
        foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { FontFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null) continue;

            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                   AtlasPopulationMode.Dynamic, true);
            if (fa == null) continue;
            string assetPath = Path.ChangeExtension(path, null) + " SDF.asset";
            fa.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.material.name = fa.name + " Material";
            fa.atlasTexture.name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            AssetDatabase.SaveAssets();
            Debug.Log("[SouvenirHuntSetup] 한글 TMP 폰트 생성: " + assetPath);
            return fa;
        }
        return null;
    }

    // ─ 기타 ────────────────────────────────────────────────────

    static void WriteStreamingConfig()
    {
        string dir = Path.Combine(Application.dataPath, "StreamingAssets");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ConfigLoader.FileName),
                          ConfigLoader.DefaultConfigText, new UTF8Encoding(false));
        AssetDatabase.Refresh();
    }

    static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    // ─ Phase 2: 부스 환경 목업 + 은폐 포인트 ──────────────────

    [MenuItem("XREAL/13. Setup Booth Environment (Phase 2)")]
    public static void SetupEnvironment()
    {
        try
        {
            if (!EnsureMainSceneOpen(out var scene)) return;

            int envLayer = EnsureLayer("Environment");

            var boothRoot = GameObject.Find("BoothRoot");
            var spawnPoints = boothRoot != null ? boothRoot.transform.Find("SpawnPoints") : null;
            if (boothRoot == null || spawnPoints == null)
            {
                Debug.LogError("[SouvenirHuntSetup] BoothRoot/SpawnPoints 없음 — 먼저 메뉴 10 실행");
                return;
            }

            // 재실행 대비: 기존 환경 제거, 스폰 포인트를 seed-83 원본 좌표로 재생성
            // (가시성 보정이 SP 좌표를 옮기므로, 재생성 없이는 실행할 때마다 배치가 누적 드리프트한다 — N회 실행 = 1회 실행)
            var old = boothRoot.transform.Find("Environment");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            for (int i = spawnPoints.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(spawnPoints.GetChild(i).gameObject);
            GenerateSpawnPoints(spawnPoints);

            var env = new GameObject("Environment");
            env.transform.SetParent(boothRoot.transform, false);

            // ── 방 목업 (Downloads/area.png 스펙, 08-31 — 눈높이 기준 로컬 좌표, 바닥 ≈ y −1.4)
            // 바닥+벽 2장 Plane(천장·오른쪽 벽 없음), 뒷벽 창문, 공중 Rectangle Light,
            // 왼쪽 벽 캐비닛, 책상 Box ×4 + 모니터 얇은 Box ×4.
            // 콜라이더는 전부 제거 — 레이저가 가구에 막혀 오선택되는 것 방지 (인수인계 §4-5).
            // 은폐 난이도는 "시각적 가림"만으로 만들고, 조준 레이는 통과시킨다.
            var wallM    = DummyMat("Env_Wall",        new Color(0.86f, 0.85f, 0.80f));
            var floorM   = DummyMat("Env_Floor",       new Color(0.60f, 0.58f, 0.54f));
            var glassM   = DummyMat("Env_WindowGlass", new Color(0.66f, 0.80f, 0.95f));
            var frameM   = DummyMat("Env_WindowFrame", new Color(0.22f, 0.35f, 0.55f));
            var deskM    = DummyMat("Env_Desk",        new Color(0.72f, 0.68f, 0.92f));
            var monitorM = DummyMat("Env_Monitor",     new Color(0.16f, 0.15f, 0.34f));
            var cabinetM = DummyMat("Env_Cabinet",     new Color(0.45f, 0.83f, 0.70f));
            var fixtureM = DummyMat("Env_LightFixture", new Color(1.0f, 0.72f, 0.30f));
            fixtureM.SetColor("_EmissionColor", new Color(1.0f, 0.72f, 0.30f) * 2.5f);
            fixtureM.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

            // 방 껍데기: 바닥 12×12m + 뒷벽(z+5.8)·왼쪽 벽(x−5.8) — 스폰 반경(최대 5m, yaw ±120°)을 감싼다.
            // Quad는 단면 렌더라 뒷벽은 −z(플레이어 쪽), 왼쪽 벽은 +x 를 향하게 두면 "제거한 면"은 저절로 없다.
            // 벽 높이 4.0(상단 y+2.6) — 스폰 y 최대 2.2 + 상향 보정 0.3 + 기념품 반높이 0.1 커버
            EnvPrim(env, envLayer, PrimitiveType.Plane, "Floor",    new Vector3(0f, -1.4f, 1.0f),   new Vector3(1.2f, 1f, 1.2f), 0f,   floorM);
            EnvPrim(env, envLayer, PrimitiveType.Quad,  "WallBack", new Vector3(0f, 0.6f, 5.8f),    new Vector3(12f, 4.0f, 1f),  0f,   wallM);
            EnvPrim(env, envLayer, PrimitiveType.Quad,  "WallLeft", new Vector3(-5.8f, 0.6f, 1.0f), new Vector3(12f, 4.0f, 1f),  -90f, wallM);

            // 창문(뒷벽): 유리 1장 + 십자·테두리 프레임 = 2×2 창
            EnvPrim(env, envLayer, PrimitiveType.Quad, "WindowGlass",       new Vector3(0.4f, 0.7f, 5.78f),   new Vector3(2.6f, 1.5f, 1f),      0f, glassM);
            EnvPrim(env, envLayer, PrimitiveType.Cube, "WindowFrameH",      new Vector3(0.4f, 0.7f, 5.77f),   new Vector3(2.68f, 0.05f, 0.02f), 0f, frameM);
            EnvPrim(env, envLayer, PrimitiveType.Cube, "WindowFrameV",      new Vector3(0.4f, 0.7f, 5.77f),   new Vector3(0.05f, 1.56f, 0.02f), 0f, frameM);
            EnvPrim(env, envLayer, PrimitiveType.Cube, "WindowFrameTop",    new Vector3(0.4f, 1.45f, 5.77f),  new Vector3(2.68f, 0.05f, 0.02f), 0f, frameM);
            EnvPrim(env, envLayer, PrimitiveType.Cube, "WindowFrameBottom", new Vector3(0.4f, -0.05f, 5.77f), new Vector3(2.68f, 0.05f, 0.02f), 0f, frameM);
            EnvPrim(env, envLayer, PrimitiveType.Cube, "WindowFrameLeft",   new Vector3(-0.9f, 0.7f, 5.77f),  new Vector3(0.05f, 1.56f, 0.02f), 0f, frameM);
            EnvPrim(env, envLayer, PrimitiveType.Cube, "WindowFrameRight",  new Vector3(1.7f, 0.7f, 5.77f),   new Vector3(0.05f, 1.56f, 0.02f), 0f, frameM);

            // 캐비닛(수납, 왼쪽 벽에 붙임)
            EnvPrim(env, envLayer, PrimitiveType.Cube, "Cabinet", new Vector3(-5.45f, -0.4f, 0.6f), new Vector3(0.55f, 2.0f, 1.1f), 0f, cabinetM);

            // 책상 2×2 + 모니터(얇은 박스, 책상 뒤쪽 모서리)
            for (int d = 0; d < 4; d++)
            {
                float dx = (d % 2 == 0) ? -1.5f : 1.5f;
                float dz = (d < 2) ? 1.7f : 3.2f;
                EnvPrim(env, envLayer, PrimitiveType.Cube, $"Desk_{d + 1}",    new Vector3(dx, -1.04f, dz),         new Vector3(1.5f, 0.72f, 0.8f),   0f, deskM);
                EnvPrim(env, envLayer, PrimitiveType.Cube, $"Monitor_{d + 1}", new Vector3(dx, -0.46f, dz + 0.28f), new Vector3(0.65f, 0.42f, 0.05f), 0f, monitorM);
            }

            // Rectangle Light — 천장 없이 공중. 실시간 추가 광원은 URP에서 꺼져 있으므로(메뉴 17)
            // Baked 전용으로 두고, 눈에 보이는 오렌지 등기구 박스를 같은 자리에 놓는다.
            EnvPrim(env, envLayer, PrimitiveType.Cube, "CeilingLampBar", new Vector3(0f, 2.35f, 2.2f), new Vector3(2.0f, 0.07f, 0.45f), 0f, fixtureM);
            var areaGo = new GameObject("RectangleLight");
            areaGo.transform.SetParent(env.transform, false);
            areaGo.transform.localPosition = new Vector3(0f, 2.3f, 2.2f);
            areaGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 아래를 향함
            areaGo.layer = envLayer;
            var areaLight = areaGo.AddComponent<Light>();
            areaLight.type = LightType.Rectangle;
            areaLight.areaSize = new Vector2(2.0f, 0.45f);
            areaLight.lightmapBakeType = LightmapBakeType.Baked;
            areaLight.intensity = 3f;
            areaLight.color = new Color(1f, 0.95f, 0.85f);

            // ── 은폐 포인트: 전용 패널 대신 "가구 그림자" 자연 은폐 (08-31, 25곳 중 9곳, §4-5: 8~10곳)
            // 책상·모니터의 부분 그림자 슬롯을 격자 스캔으로 찾아 은폐 SP를 그 자리로 스냅한다.
            // 공중 부유 패널 제거 — 일반 SP는 기존대로 공중 스폰(±120°) 유지.
            // "가장 얇은 기념품" 실루엣 기준: 어느 메시가 어느 포인트에 놓일지는 런타임
            // (RewardTable.GetMeshEntry의 idx 순환)이 매 판 정하므로, 배치·검증은 최악 메시 기준이어야 한다.
            var silhouette = SilhouetteHalfWidthRange();
            int hiddenCount = PlaceHiddenPointsInFurnitureCover(boothRoot.transform, spawnPoints, env, silhouette.min, 9);

            // ── 실제 부스 설치 모드 지원: 환경 전체를 오클루전 마스크로 바꿀 스위치 (config: envOcclusionMode)
            var occSwitch = env.AddComponent<EnvironmentOcclusionSwitch>();
            SetRef(occSwitch, "occlusionMaterial", GetOrCreateOcclusionMat());

            // ── 구조물(바닥·벽·창문·등기구) 런타임 숨김 (config: showStructures, 기본 false)
            // 오브젝트는 씬에 남는다 — 에디터 씬 뷰·라이트맵 베이크·가시성 검증은 종전대로 사용.
            // 08-31 글래스 실측: 좁은 FOV에서 큰 평면의 잘린 경계가 그대로 드러나 몰입을 깼다.
            var structRenderers = new List<Renderer>();
            foreach (var r in env.GetComponentsInChildren<Renderer>(true))
                if (IsStructureName(r.gameObject.name)) structRenderers.Add(r);
            var structVis = env.AddComponent<StructureVisibility>();
            var svSo = new SerializedObject(structVis);
            var arr = svSo.FindProperty("structureRenderers");
            arr.arraySize = structRenderers.Count;
            for (int i = 0; i < structRenderers.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = structRenderers[i];
            svSo.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[SouvenirHuntSetup] 구조물 런타임 숨김 배선 {structRenderers.Count}개 (config showStructures=true 로 복원 가능)");

            // ── 가시성 검증·자동 보정: 어느 포인트도 완전 은폐되지 않게 (§4-5 완전 은폐 금지)
            ValidateAndFixVisibility(boothRoot.transform, spawnPoints, env, silhouette);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SouvenirHuntSetup] 방 환경 구성 완료(area.png) — Plane 3장·창문·책상4·모니터4·캐비닛·Rect Light, 가구 그림자 자연 은폐 {hiddenCount}곳·패널 없음 (Environment 레이어 {envLayer})");
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup",
                    $"방 환경 구성 완료(area.png)!\n바닥·벽 2장 + 창문 + 책상×4/모니터×4 + 캐비닛 + Rect Light\n가구 그림자 자연 은폐 {hiddenCount}곳(패널 없음) · (환경엔 콜라이더 없음 — 레이저 관통)", "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] SetupEnvironment FAILED: " + e);
        }
    }

    // ─ 기념품 실루엣 · 자연 은폐 배치 ──────────────────────────────
    //
    // 실루엣 배경(08-27 회귀 수정): Collectible.SetVisual은 visualSize(0.2m)를 "최장축"에만 맞춘다
    // (s = visualSize / maxDim). 길쭉한 메시일수록 나머지 축은 훨씬 얇다 — 예: Capsule(1:2:1)은
    // 0.10 × 0.20 × 0.10m 라 수평 반폭 0.05m. 배치·검증 모두 최악(가장 얇은) 메시 기준이어야 한다.
    //
    // 자연 은폐(08-31, 환경 v2): 전용 패널(공중 부유 판) 대신 방 가구(책상·모니터)의 그림자에서
    // "부분만 보이는" 슬롯을 찾아 은폐 SP를 옮긴다. 슬롯 조건: 노출 28~52% — 검증 게이트(25%↑)
    // 안쪽이라 재검증에서 흔들리지 않는다. 사전 시뮬(08-31): 현 가구에서 0.7m 분리 슬롯 25개
    // (전부 yaw ±60°·y −1.28~−0.28 하단) — 목표 9개 대비 여유 충분. 은폐가 전방 하단에 몰리는
    // 것은 수용한 트레이드오프(일반 SP 16곳이 ±120° 전역을 커버).

    static int PlaceHiddenPointsInFurnitureCover(Transform boothRoot, Transform spawnPoints, GameObject env, float hiddenHalfWidth, int target)
    {
        // 임시 콜라이더 — ValidateAndFixVisibility와 동일 패턴
        var envColliders = new HashSet<Collider>();
        foreach (var mf in env.GetComponentsInChildren<MeshFilter>())
            envColliders.Add(mf.gameObject.AddComponent<MeshCollider>());
        Physics.SyncTransforms();
        try
        {
            Vector3 eye = boothRoot.position;

            // 은폐로 지정할 SP: 종전과 같은 기준(원거리 2.2m↑, 자식 순서)
            var hiddenSps = new List<Transform>();
            foreach (Transform p in spawnPoints)
            {
                if (hiddenSps.Count >= target) break;
                if (p.localPosition.magnitude >= 2.2f) hiddenSps.Add(p);
            }
            var openPos = new List<Vector3>();
            foreach (Transform p in spawnPoints)
                if (!hiddenSps.Contains(p)) openPos.Add(p.position);

            // 격자 스캔 — 부분 그림자는 가구 높이상 하단(y ≤ 0)에만 생기므로 그 범위만.
            // 5레이 프리필터(QuickBlockedCount)로 완전 개방·심부 그림자는 64샘플 정밀 측정 생략.
            var slots = new List<(Vector3 pos, float frac)>();
            for (float z = 1.6f; z <= 5.0f; z += 0.15f)
                for (float x = -5.7f; x <= 4.5f; x += 0.15f)
                    for (float y = -1.28f; y <= 0f; y += 0.1f)
                    {
                        var local = new Vector3(x, y, z);
                        float dist = local.magnitude;
                        if (dist < 2.2f || dist > 5.0f) continue;
                        if (Mathf.Abs(Mathf.Atan2(x, z) * Mathf.Rad2Deg) > 120f) continue;
                        Vector3 world = boothRoot.TransformPoint(local);
                        if (OverlapsEnv(world, envColliders)) continue;
                        if (BehindStructure(eye, world, envColliders, hiddenHalfWidth)) continue; // 벽·바닥·등기구 뒤 금지
                        int quick = QuickBlockedCount(eye, world, envColliders, hiddenHalfWidth);
                        if (quick == 0 || quick == 5) continue;
                        float frac = VisibleFraction(eye, world, envColliders, hiddenHalfWidth);
                        if (frac >= 0.28f && frac <= 0.52f) slots.Add((world, frac));
                    }

            // 노출 40% 근접 우선 그리디 — 채택끼리 0.9m, 일반 SP와 0.5m(검증 간격) 분리
            slots.Sort((a, b) => Mathf.Abs(a.frac - 0.4f).CompareTo(Mathf.Abs(b.frac - 0.4f)));
            var picked = new List<(Vector3 pos, float frac)>();
            foreach (var s in slots)
            {
                if (picked.Count >= hiddenSps.Count) break;
                bool ok = true;
                foreach (var q in picked)
                    if (Vector3.Distance(q.pos, s.pos) < 0.9f) { ok = false; break; }
                if (ok)
                    foreach (var q in openPos)
                        if (Vector3.Distance(q, s.pos) < 0.5f) { ok = false; break; }
                if (ok) picked.Add(s);
            }

            // 각 은폐 SP를 원래 위치에서 가장 가까운 미사용 슬롯으로 스냅
            var sb = new StringBuilder($"[SouvenirHuntSetup] 자연 은폐 배치 — 후보 슬롯 {slots.Count} → 채택 {picked.Count} (목표 {target}):\n");
            var free = new List<int>();
            for (int i = 0; i < picked.Count; i++) free.Add(i);
            int count = 0;
            foreach (var p in hiddenSps)
            {
                if (free.Count == 0) break;
                int best = free[0];
                float bd = float.MaxValue;
                foreach (int i in free)
                {
                    float d = Vector3.Distance(p.position, picked[i].pos);
                    if (d < bd) { bd = d; best = i; }
                }
                free.Remove(best);
                p.position = picked[best].pos;
                p.gameObject.AddComponent<SpawnPointMarker>().Hidden = true;
                var lp = boothRoot.InverseTransformPoint(p.position);
                sb.AppendLine($"  {p.name} → ({lp.x:F2}, {lp.y:F2}, {lp.z:F2}) 노출 {picked[best].frac:P0}");
                count++;
            }
            if (count < 8)
            {
                sb.AppendLine($"  ⚠ 은폐 {count}곳 — §4-5(8~10곳) 미달! 가구를 늘리거나 슬롯 기준을 확인할 것");
                Debug.LogWarning(sb.ToString());
            }
            else Debug.Log(sb.ToString());
            return count;
        }
        finally
        {
            foreach (var c in envColliders)
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }
    }

    // 값싼 프리필터: 실루엣 원판의 중심 + 가장자리 4점, 5레이. 0=완전 개방, 5=심부 그림자 —
    // 둘 다 "부분 은폐"가 아니므로 64샘플 정밀 측정 대상에서 제외해 격자 스캔을 수 배 줄인다.
    static int QuickBlockedCount(Vector3 eye, Vector3 target, HashSet<Collider> envColliders, float half)
    {
        Vector3 dir = (target - eye).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        if (right.sqrMagnitude < 0.5f) right = Vector3.right;
        Vector3 up = Vector3.Cross(dir, right).normalized;
        int blocked = 0;
        foreach (var off in new[] { Vector3.zero, right * half, -right * half, up * half, -up * half })
        {
            Vector3 to = target + off - eye;
            foreach (var hit in Physics.RaycastAll(eye, to.normalized, to.magnitude - 0.02f))
                if (envColliders.Contains(hit.collider)) { blocked++; break; }
        }
        return blocked;
    }

    // "벽·전등·바닥 뒤 배치 금지" (08-31 사용자 요구): 가림은 오직 가구(책상·모니터·캐비닛)로만.
    // 눈→포인트 실루엣 5레이(중심+가장자리 4점) 중 하나라도 구조물에 막히면 그 위치는 실격.
    // 벽 밖(뒷벽·왼쪽 벽 관통)·바닥 아래·등기구 뒤가 전부 이 한 검사로 막힌다 — 현 배치에선
    // 기하상 발생 불가지만, 가구·벽·스캔 범위를 옮겨도 깨지지 않는 명시적 불변 조건으로 둔다.
    /// 방 "구조물" = 바닥·벽·창문·천장 등기구. 가구(책상·모니터·캐비닛)와 구분되는 두 가지 규칙의 공통 기준:
    ///   · 배치 규칙: 구조물 뒤에는 기념품을 두지 않는다 (BehindStructure)
    ///   · 표시 규칙: 구조물은 런타임에 숨긴다 (StructureVisibility, config showStructures)
    static bool IsStructureName(string n) =>
        n == "Floor" || n.StartsWith("Wall") || n.StartsWith("Window") || n == "CeilingLampBar";

    static bool IsStructure(Collider c) => IsStructureName(c.gameObject.name);

    static bool BehindStructure(Vector3 eye, Vector3 target, HashSet<Collider> envColliders, float half)
    {
        Vector3 dir = (target - eye).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        if (right.sqrMagnitude < 0.5f) right = Vector3.right;
        Vector3 up = Vector3.Cross(dir, right).normalized;
        foreach (var off in new[] { Vector3.zero, right * half, -right * half, up * half, -up * half })
        {
            Vector3 to = target + off - eye;
            foreach (var hit in Physics.RaycastAll(eye, to.normalized, to.magnitude - 0.02f))
                if (envColliders.Contains(hit.collider) && IsStructure(hit.collider)) return true;
        }
        return false;
    }

    /// 샘플 원판이 이보다 작아지면 64샘플이 전부 중심 몇 mm 안에 몰려 사실상 중심 레이 1발과 같아진다.
    /// 그런 기념품은 어차피 글래스로 보이지도 않으므로, 검증이 조용히 무력화되느니 경고하고 바닥을 친다.
    const float MinSilhouetteHalfWidth = 0.03f;

    /// <summary>
    /// MeshLibrary 전 항목의 수평 반폭 범위(m).
    /// 두 게이트의 최악 케이스가 서로 반대다:
    ///   · 은폐형(HiddenMin, "완전히 가려지면 안 된다") → <b>가장 얇은</b> 메시가 최악. 작을수록 패널 뒤로 숨는다.
    ///   · 일반형(OpenMin, "뚜렷이 보여야 한다")       → <b>가장 큰</b> 메시가 최악. 클수록 가구 모서리에 걸린다.
    /// 하나로 뭉뚱그리면 한쪽 게이트가 반드시 무력해진다 — 실제로 min 하나만 쓰던 판(08-27)에서는
    /// 일반 포인트가 거의 전부 100%로 나와 60% 게이트가 사실상 중심 레이 검사로 퇴화했다.
    /// </summary>
    static (float min, float max) SilhouetteHalfWidthRange()
    {
        float visualSize = 0.2f;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null && prefab.TryGetComponent<Collectible>(out var col)) visualSize = col.VisualSize;

        var lib = AssetDatabase.LoadAssetAtPath<MeshLibrary>(LibraryPath);
        if (lib == null || lib.Count == 0)
        {
            // MeshLibrary.asset 은 메뉴 10(씬 생성)이 만든다. 21은 배선이 끊겼을 때 폴백으로 만들 뿐이고 순서상 맨 뒤다.
            Debug.LogWarning("[SouvenirHuntSetup] MeshLibrary 없음 — 실루엣을 Capsule 기준으로 가정 (메뉴 10 먼저 실행)");
            float fallback = visualSize * 0.25f; // Capsule(1:2:1) 상당
            return (fallback, visualSize * 0.5f);
        }

        float min = float.MaxValue, max = 0f;
        var sb = new StringBuilder("[SouvenirHuntSetup] 기념품 실루엣 실측 (visualSize=" + visualSize.ToString("F2") + "m, 최장축 기준 정규화):\n");
        for (int i = 0; i < lib.Count; i++)
        {
            var e = lib.Get(i);
            if (e.mesh == null) continue;
            Vector3 b = e.mesh.bounds.size;
            float maxDim = Mathf.Max(b.x, Mathf.Max(b.y, b.z));
            if (maxDim <= 0f) continue;
            float s = visualSize / maxDim;
            // 스폰 포인트는 회전이 없으므로 수평 실루엣은 시선 yaw에 따라 x~z 사이를 오간다 → 최소축이 가장 얇다
            float half = Mathf.Min(b.x, b.z) * s * 0.5f;
            min = Mathf.Min(min, half);
            max = Mathf.Max(max, Mathf.Max(b.x, b.z) * s * 0.5f);
            sb.AppendLine($"  [{i}] {e.label}: {b.x * s:F3} × {b.y * s:F3} × {b.z * s:F3} m → 수평 반폭 {half:F3}~{Mathf.Max(b.x, b.z) * s * 0.5f:F3} m");
        }
        if (min == float.MaxValue) { min = visualSize * 0.25f; max = visualSize * 0.5f; }

        if (min < MinSilhouetteHalfWidth)
        {
            sb.AppendLine($"  ⚠ 가장 얇은 항목 {min:F3}m 이 하한 {MinSilhouetteHalfWidth:F3}m 미만 — 하한으로 올려 검증한다.");
            sb.AppendLine("     (이보다 얇은 기념품은 은폐 여부와 무관하게 글래스로 식별하기 어렵다. 메시 교체를 검토할 것)");
            Debug.LogWarning(sb.ToString());
            min = MinSilhouetteHalfWidth;
            max = Mathf.Max(max, min);
            return (min, max);
        }

        sb.AppendLine($"  → 은폐 게이트 기준(가장 얇음) {min:F3} m · 일반 게이트 기준(가장 큼) {max:F3} m");
        Debug.Log(sb.ToString());
        return (min, max);
    }

    static GameObject EnvPrim(GameObject parent, int layer, PrimitiveType type, string name,
                              Vector3 localPos, Vector3 localScale, float yawDeg, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.DestroyImmediate(col); // 레이캐스트 차단 방지
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.layer = layer;
        go.isStatic = true; // Phase 4 라이트맵 베이크 대비
        return go;
    }

    // 각 스폰 포인트의 "눈에서 보이는 비율"을 임시 콜라이더 레이캐스트로 측정하고, 기준 미달이면
    // (1) 좌우·상하·눈쪽 당기기 이동 → (2) 원점 기준 yaw 회전 재배치 순으로 보정한다.
    // 3DoF는 위치 이동이 없어 "다가가서 보기"가 불가 → 완전 은폐 = 영구 미발견 (§4-5 위반).
    static void ValidateAndFixVisibility(Transform boothRoot, Transform spawnPoints, GameObject env, (float min, float max) silhouette)
    {
        const float HiddenMin = 0.25f; // 은폐형: 일부만 보이면 됨
        const float OpenMin = 0.60f;   // 일반형: 뚜렷이 보여야 함 (글래스 FOV·밝기 감안)

        var envColliders = new HashSet<Collider>();
        foreach (var mf in env.GetComponentsInChildren<MeshFilter>())
            envColliders.Add(mf.gameObject.AddComponent<MeshCollider>());
        Physics.SyncTransforms();

        Vector3 eye = boothRoot.position; // 부스 원점 = 체험자 눈 (3DoF: 게임 내내 불변)
        var report = new StringBuilder(
            $"[SouvenirHuntSetup] 가시성 검증 — 은폐 {HiddenMin:P0}↑(반폭 {silhouette.min:F3}m 기준) · " +
            $"일반 {OpenMin:P0}↑(반폭 {silhouette.max:F3}m 기준) · {VisibilitySamples}샘플 면적 균등:\n");
        int fixedCount = 0, lowCount = 0;

        var allPoints = new List<Transform>();
        foreach (Transform t in spawnPoints) allPoints.Add(t);

        foreach (Transform p in allPoints)
        {
            bool hidden = p.TryGetComponent<SpawnPointMarker>(out var marker) && marker.Hidden;
            float need = hidden ? HiddenMin : OpenMin;
            // 게이트마다 최악 메시가 반대다 (SilhouetteHalfWidthRange 주석 참고)
            float silhouetteHalfWidth = hidden ? silhouette.min : silhouette.max;
            float frac = VisibleFraction(eye, p.position, envColliders, silhouetteHalfWidth);
            // 가림과 별개로 "가구 관통"도 실격 — 이 검증은 레이 가림만 재서, 책상 표면 3cm 앞 포인트도
            // 노출 84%로 통과시킨다(08-31 SP_17: 기념품 뒤쪽 절반이 Desk_1 안에 박힘). 기념품
            // 반경(SpawnClearanceRadius)만큼 환경 메시와 겹치면 노출과 무관하게 이동 보정에 태운다.
            bool embedded = OverlapsEnv(p.position, envColliders);
            // "벽·전등·바닥 뒤 배치 금지": 구조물이 조금이라도 가리는 위치도 실격 → 이동 보정에 태운다
            bool behindStruct = BehindStructure(eye, p.position, envColliders, silhouetteHalfWidth);
            string fixNote = "";

            // 1단계: 좌우 → 상하 → 눈쪽 당기기 → 조합 탐색
            if (frac < need || embedded || behindStruct)
            {
                Vector3 basePos = p.position;
                Vector3 dir = (basePos - eye).normalized;
                Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized;
                float dist = Vector3.Distance(basePos, eye);

                var candidates = new List<Vector3>();
                foreach (float off in new[] { 0.08f, -0.08f, 0.16f, -0.16f, 0.24f, -0.24f, 0.32f, -0.32f, 0.4f, -0.4f, 0.5f, -0.5f })
                    candidates.Add(basePos + perp * off);
                foreach (float up in new[] { 0.15f, 0.3f, -0.15f, -0.3f })
                    candidates.Add(basePos + Vector3.up * up);
                foreach (float k in new[] { 0.85f, 0.72f })
                    if (dist * k >= 1.5f) candidates.Add(eye + dir * (dist * k));
                foreach (float off in new[] { 0.3f, -0.3f })
                    foreach (float up in new[] { 0.2f, -0.2f })
                        candidates.Add(basePos + perp * off + Vector3.up * up);

                foreach (var c in candidates)
                {
                    if (TooCloseToOthers(allPoints, p, c) || OverlapsEnv(c, envColliders) ||
                        BehindStructure(eye, c, envColliders, silhouetteHalfWidth)) continue;
                    p.position = c;
                    Physics.SyncTransforms();
                    frac = VisibleFraction(eye, p.position, envColliders, silhouetteHalfWidth);
                    if (frac >= need)
                    {
                        fixedCount++;
                        fixNote = embedded ? " [가구 관통→이동]" : behindStruct ? " [구조물 뒤→이동]" : " [이동 보정]";
                        embedded = false; behindStruct = false;
                        break;
                    }
                }
                if (frac < need || embedded || behindStruct) { p.position = basePos; Physics.SyncTransforms(); frac = VisibleFraction(eye, p.position, envColliders, silhouetteHalfWidth); }
            }

            // 2단계: 최후 — 원점 기준 yaw 회전 재배치 (거리·높이 유지, §4-5 후방 ±120° 준수)
            if (frac < need || embedded || behindStruct)
            {
                Vector3 basePos = p.position;
                Vector3 local = basePos - eye;
                foreach (float yawOff in new[] { 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f })
                {
                    Vector3 c = eye + Quaternion.Euler(0f, yawOff, 0f) * local;
                    Vector3 lp = boothRoot.InverseTransformPoint(c);
                    if (Mathf.Abs(Mathf.Atan2(lp.x, lp.z) * Mathf.Rad2Deg) > 120f) continue;
                    if (TooCloseToOthers(allPoints, p, c) || OverlapsEnv(c, envColliders) ||
                        BehindStructure(eye, c, envColliders, silhouetteHalfWidth)) continue;
                    p.position = c;
                    Physics.SyncTransforms();
                    frac = VisibleFraction(eye, p.position, envColliders, silhouetteHalfWidth);
                    if (frac >= need)
                    {
                        fixedCount++;
                        fixNote = embedded ? " [가구 관통→회전 재배치]" : behindStruct ? " [구조물 뒤→회전 재배치]" : " [회전 재배치]";
                        embedded = false; behindStruct = false;
                        break;
                    }
                }
                if (frac < need || embedded || behindStruct)
                {
                    p.position = basePos;
                    Physics.SyncTransforms();
                    lowCount++;
                    fixNote = " [!미해결]";
                }
            }

            report.AppendLine($"  {p.name}{(hidden ? " (은폐)" : "")}: 노출 {frac:P0}{fixNote}");
        }

        foreach (var c in envColliders)
            if (c != null) UnityEngine.Object.DestroyImmediate(c);

        report.AppendLine($"  → 보정 {fixedCount}곳" + (lowCount > 0 ? $", 미해결 {lowCount}곳 (수동 확인 필요!)" : " — 전 포인트 기준 충족"));
        if (lowCount > 0) Debug.LogWarning(report.ToString());
        else Debug.Log(report.ToString());
    }

    static bool TooCloseToOthers(List<Transform> all, Transform self, Vector3 pos)
    {
        foreach (var t in all)
            if (t != self && Vector3.Distance(t.position, pos) < 0.5f) return true;
        return false;
    }

    /// 기념품 최장축 절반(visualSize 0.2 → 0.1m) + 여유 0.02. 이 반경 구가 환경 메시와 겹치면
    /// 기념품이 가구를 관통해 렌더링된다 — 노출 % 게이트로는 잡히지 않는 별도 실격 조건.
    const float SpawnClearanceRadius = 0.12f;

    static bool OverlapsEnv(Vector3 pos, HashSet<Collider> envColliders)
    {
        foreach (var c in Physics.OverlapSphere(pos, SpawnClearanceRadius))
            if (envColliders.Contains(c)) return true;
        return false;
    }

    // 기념품 실루엣(반경 silhouetteHalfWidth) 원판을 면적 균등 샘플링해, 환경에 가려지지 않는 면적 비율을 구한다.
    //
    // 종전에는 중심 1점 + 반경 극단의 링 8점 = 9샘플이었다. 링이 전부 최외곽에 몰려 있어
    // 패널 옆으로 삐져나온 얇은 초승달을 과대평가했다 — 실제 연속 노출 23.4%인 배치가 9샘플로는 33.3%로
    // 계산돼 기준(25%)을 통과했다. 피보나치(Vogel) 배치는 r = R·√(i/N) 이라 표본이 면적에 비례해 퍼진다.
    const int VisibilitySamples = 64;

    static float VisibleFraction(Vector3 eye, Vector3 target, HashSet<Collider> envColliders, float silhouetteHalfWidth)
    {
        Vector3 dir = (target - eye).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        if (right.sqrMagnitude < 0.5f) right = Vector3.right;
        Vector3 up = Vector3.Cross(dir, right).normalized;

        const float GoldenAngle = 2.39996323f; // π(3−√5)
        int visible = 0;
        for (int i = 0; i < VisibilitySamples; i++)
        {
            float r = silhouetteHalfWidth * Mathf.Sqrt((i + 0.5f) / VisibilitySamples);
            float ang = i * GoldenAngle;
            Vector3 sample = target + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * r;
            Vector3 to = sample - eye;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(eye, to.normalized, to.magnitude - 0.02f))
                if (envColliders.Contains(hit.collider)) { blocked = true; break; }
            if (!blocked) visible++;
        }
        return (float)visible / VisibilitySamples;
    }

    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 6; i < layers.arraySize; i++)
        {
            var slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[SouvenirHuntSetup] 레이어 생성: {name} (index {i})");
                return i;
            }
        }
        Debug.LogWarning("[SouvenirHuntSetup] 빈 레이어 슬롯 없음 — Default 사용");
        return 0;
    }

    static Material GetOrCreateOcclusionMat()
    {
        var shader = Shader.Find("SouvenirHunt/DepthOcclusion");
        if (shader == null)
        {
            Debug.LogWarning("[SouvenirHuntSetup] SouvenirHunt/DepthOcclusion 셰이더 없음 — 오클루전 모드 비활성 (Assets/Shaders 확인)");
            return null;
        }
        const string path = "Assets/Settings/OcclusionMask.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        return mat;
    }

    // ─ 현장 설치 지원: 360 파노라마 레퍼런스 정렬 (에디터 전용) ──────
    // 체험자 의자 위치에서 찍은 파노라마(스티칭 equirect)를 부스 원점의 구 안쪽에 표시.
    // 씬 뷰에서 가구 메시를 사진 실루엣에 맞춰 정렬하는 배경 가이드다.
    // EditorOnly 태그 → APK 빌드에서 자동 제외. 방향(yaw) 정렬은 PanoReference360의 Y 회전으로.
    // 파노라마는 방향만 정확함 — 거리(깊이)는 줄자 실측 값으로 넣을 것.

    [MenuItem("XREAL/16. Load 360 Reference (배치 정렬)")]
    public static void Load360Reference()
    {
        try
        {
            if (!EnsureMainSceneOpen(out var scene)) return;

            string src = EditorUtility.OpenFilePanel("360 파노라마 이미지 선택 (equirect)", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(src)) return;

            EnsureFolder("Assets/Editor");
            EnsureFolder("Assets/Editor/RefImages");
            string dst = "Assets/Editor/RefImages/" + Path.GetFileName(src);
            File.Copy(src, dst.Replace('/', Path.DirectorySeparatorChar), true);
            AssetDatabase.ImportAsset(dst);
            if (AssetImporter.GetAtPath(dst) is TextureImporter imp)
            {
                imp.maxTextureSize = 8192;               // 파노라마 해상도 보존
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            if (tex == null)
            {
                Debug.LogError("[SouvenirHuntSetup] 이미지 임포트 실패: " + dst);
                return;
            }

            var boothRoot = GameObject.Find("BoothRoot");
            Vector3 origin = boothRoot != null ? boothRoot.transform.position : Vector3.zero;

            var old = GameObject.Find("PanoReference360");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "PanoReference360";
            sphere.tag = "EditorOnly";                   // 빌드(APK)에서 자동 제외
            sphere.transform.position = origin;          // 부스 원점 = 체험자 눈
            sphere.transform.localScale = Vector3.one * 40f; // 반경 20m — 가구(≤5m) 바깥
            UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());

            const string matPath = "Assets/Editor/RefImages/PanoRef.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, matPath);
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(-1f, 1f)); // 구 안쪽에서 좌우 정상으로
            mat.SetFloat("_Cull", 1f);                             // Front cull → 안쪽 면 렌더
            sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // 씬 뷰 클릭이 이 구에 먹히지 않게 — 가구 선택 방해 방지
            SceneVisibilityManager.instance.DisablePicking(sphere, true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SouvenirHuntSetup] 파노라마 레퍼런스 로드 완료: " + dst +
                      " — 씬 카메라를 원점 근처에 두고 가구를 실루엣에 맞춰 정렬하세요." +
                      " 사진 정면 보정은 PanoReference360의 Y 회전, 완료 후엔 비활성화/삭제 (빌드엔 미포함).");
            EditorUtility.DisplayDialog("SouvenirHunt Setup",
                "파노라마 레퍼런스 로드 완료!" + System.Environment.NewLine +
                "정면 보정: PanoReference360의 Y 회전" + System.Environment.NewLine +
                "정렬 후 비활성화/삭제해도 되고, 놔둬도 빌드엔 안 들어갑니다.", "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] Load360Reference FAILED: " + e);
        }
    }

    // ─ Phase 3 보강: 빔프로 상태 안내 (시야 밖 조준·연결 끊김) ──────

    // ─ Phase 3: SCR-07 Beam Pro 숨김 운영자 패널 ──────────────

    const string VcVariantPath = "Assets/Prefabs/SouvenirVirtualController.prefab";
    const string VcSdkPath = "Packages/com.xreal.xr/Runtime/Prefabs/XREALVirtualController.prefab";
    const string XrealSettingsPath = "Assets/XR/Settings/XREALSettings.asset";

    [MenuItem("XREAL/14. Setup Operator Panel (SCR-07)")]
    public static void SetupOperatorPanel()
    {
        try
        {
            EnsureFolder("Assets/Prefabs");
            var font = GetOrCreateKoreanFont();

            // 멱등: 기존 생성본이 있으면 그걸 열어 OperatorRoot만 재구성,
            // 없으면 SDK 프리팹에서 시작한다(아래에서 완전 언팩 → 일반 프리팹으로 저장).
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(VcVariantPath);
            var src = variant != null ? variant : AssetDatabase.LoadAssetAtPath<GameObject>(VcSdkPath);
            if (src == null)
            {
                Debug.LogError("[SouvenirHuntSetup] SDK 프리팹 없음: " + VcSdkPath);
                return;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);

            // SDK 프리팹 루트에 로드 불가(missing) 스크립트가 있으면 SaveAsPrefabAsset이 거부한다
            // ("save a Prefab with a missing script"). 프리팹 인스턴스에서는 컴포넌트 제거가 불가하므로
            // 완전 언팩 후 missing만 제거하고 일반 프리팹으로 저장한다.
            // (Variant 포기 — SDK 업데이트가 자동 전파되지 않으므로 SDK 갱신 시 이 메뉴를 재실행할 것)
            if (PrefabUtility.IsPartOfPrefabInstance(inst))
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            int removedMissing = 0;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                removedMissing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (removedMissing > 0)
                Debug.LogWarning("[SouvenirHuntSetup] 로드 불가 스크립트 " + removedMissing +
                                 "개 제거 — #if로 컴파일 제외된 에디터용 시뮬레이터류로 추정 (실기기 동작 무관, 원본 SDK 프리팹은 불변)");

            var canvas = inst.transform.Find("Canvas");
            if (canvas == null)
            {
                Debug.LogError("[SouvenirHuntSetup] 프리팹에 Canvas 없음 — SDK 구조 변경? 수동 확인 필요");
                UnityEngine.Object.DestroyImmediate(inst);
                return;
            }

            var old = canvas.Find("OperatorRoot");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            // 인형뽑기 가로 오버레이(조이스틱·잡기) — OperatorRoot보다 앞 형제(코너탭·운영자 패널이 항상 위에 오도록)
            BuildClawPad(canvas, font);

            // OperatorRoot — Canvas 마지막 자식(공식 문서: 기존 버튼과 겹침 방지) = 렌더·레이캐스트 최상위.
            // 중첩 Canvas 금지: 빌드 시 SDK가 자식 Canvas 전부를 ScreenSpaceCamera로 강제 전환한다.
            var opRoot = NewRect("OperatorRoot", canvas);
            StretchFull((RectTransform)opRoot.transform);
            opRoot.transform.SetAsLastSibling();
            var opc = opRoot.AddComponent<OperatorPanelController>();

            // 탭 존: 기본 anchor (0.7,0.85)~(1,1) = 우상단(Main). Claw 세션이면 OperatorPanelController.ApplyLayout이
            // 세로 기준 우하단 (0.8,0)~(1,0.15)로 옮긴다(눕히면 가로 우상단). 두 위치 모두 SDK 버튼(Trigger·App·Home·Logo)과 겹치지 않음.
            var zone = NewRect("CornerTapZone", opRoot.transform);
            var zrt = (RectTransform)zone.transform;
            zrt.anchorMin = new Vector2(0.7f, 0.85f);
            zrt.anchorMax = Vector2.one;
            zrt.offsetMin = Vector2.zero;
            zrt.offsetMax = Vector2.zero;
            var zoneImg = zone.AddComponent<Image>();
            zoneImg.color = Color.clear;         // 투명이어도 raycastTarget이면 탭 인식
            zoneImg.raycastTarget = true;
            var reveal = zone.AddComponent<CornerTapReveal>();

            // 남은 횟수 카운터 (우상단 소형, §4-3: 3회째부터, raycastTarget=false라 탭을 막지 않음)
            var counter = CreateText(opRoot.transform, "TapCounter", "", font, 44,
                                     new Color(1f, 1f, 1f, 0.6f), Vector2.zero, new Vector2(140, 70));
            counter.raycastTarget = false;
            var crt = (RectTransform)counter.transform;
            crt.anchorMin = Vector2.one;
            crt.anchorMax = Vector2.one;
            crt.anchoredPosition = new Vector2(-100f, -100f);
            counter.gameObject.SetActive(false);

            // 운영자 패널 — 세로(Main)·가로(Claw, 눕힌 방향으로 회전 + 개수 스테퍼) 두 뷰. 평소 비활성.
            // 열리면 전면 배경이 조작 영역을 덮음(의도된 운영자 모드). 어느 뷰를 쓸지는 OperatorPanelController.ApplyLayout이 세션·config로 결정.
            var portraitView = BuildOperatorView(opRoot.transform, font, landscape: false);
            var landscapeView = BuildOperatorView(opRoot.transform, font, landscape: true);

            // [SerializeField] 배선
            SetRef(reveal, "counterText", counter);
            SetRef(reveal, "panel", opc);
            SetRef(opc, "portrait", portraitView);
            SetRef(opc, "landscape", landscapeView);
            SetRef(opc, "cornerZone", zrt);
            SetRef(opc, "tapCounter", crt);

            // Beam Pro 화면은 URPUICamera(컬링 마스크 = UI 레이어만)가 그린다 — Default(0) 레이어면 실기기에서 안 보일 수 있음(09-03 수정)
            SetLayerRecursively(opRoot, 5);

            var saved = PrefabUtility.SaveAsPrefabAsset(inst, VcVariantPath);
            UnityEngine.Object.DestroyImmediate(inst);
            if (saved == null)
            {
                Debug.LogError("[SouvenirHuntSetup] 프리팹 저장 실패 — XREALSettings는 변경하지 않음. 위 에러 로그 확인");
                return;
            }

            // 앱 시작 시 이 프리팹이 Instantiate되도록 XREALSettings.VirtualController 교체
            var settings = AssetDatabase.LoadMainAssetAtPath(XrealSettingsPath);
            if (settings == null)
            {
                Debug.LogError("[SouvenirHuntSetup] XREALSettings.asset 없음: " + XrealSettingsPath);
            }
            else
            {
                var so = new SerializedObject(settings);
                var prop = so.FindProperty("VirtualController");
                if (prop == null) Debug.LogError("[SouvenirHuntSetup] VirtualController 필드 없음 — SDK 버전 확인");
                else { prop.objectReferenceValue = saved; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            AssetDatabase.SaveAssets();

            Debug.Log("[SouvenirHuntSetup] SCR-07 운영자 패널 구성 완료: " + VcVariantPath +
                      "\n세로 뷰(Main, 우상단 10탭) + 가로 뷰(Claw, 세로 기준 우하단 10탭, 눕힌 방향 회전, 목표 최소·최대·공 개수 스테퍼) + 인형뽑기 오버레이." +
                      "\n연타 방식(A 시간창/B 간격) 전환은 이 프리팹의 CornerTapZone 인스펙터에서 직접." +
                      "\n(메뉴 재실행 시 OperatorRoot는 재생성되어 그 안의 인스펙터 조정값은 초기화됨)");
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup",
                    "SCR-07 운영자 패널 구성 완료!\nMain: 우상단 구석 10연타 → 세로 패널\nClaw: 세로 기준 우하단(눕히면 우상단) 10연타 → 가로 패널(개수 스테퍼)", "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] SetupOperatorPanel FAILED: " + e);
        }
    }

    static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Button BuildScreenButton(Transform parent, string name, string label, TMP_FontAsset font,
                                    Vector2 anchoredPos, Vector2 size, Color bg, float labelSize = 54f)
    {
        var go = NewRect(name, parent);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        CreateText(go.transform, "Label", label, font, labelSize, Color.white,
                   Vector2.zero, new Vector2(size.x - 20f, size.y - 10f));
        return btn;
    }

    // ─ 운영자 패널 뷰 빌더 (09-03: 세로/가로 2뷰) ─────────────────────────────

    /// <summary>
    /// 운영자 패널 한 뷰. landscape=false: 세로 1080×2340 현행 레이아웃(Main).
    /// landscape=true: 2340×1080 콘텐츠를 중심에 두고 z 회전(기본 −90°, 런타임 ApplyLayout이 config에 맞춰 재설정) —
    /// Beam Pro를 눕힌 채 읽는 Claw 세션용. 왼쪽 열 config 텍스트, 오른쪽 열 개수 스테퍼·경고·상태·버튼.
    /// </summary>
    static OperatorPanelView BuildOperatorView(Transform opRoot, TMP_FontAsset font, bool landscape)
    {
        var cyan = new Color(0.4f, 0.9f, 1f);
        var green = new Color(0.5f, 1f, 0.6f);
        var red = new Color(0.55f, 0.13f, 0.13f, 1f);
        var gray = new Color(0.25f, 0.27f, 0.32f, 1f);

        var root = NewRect(landscape ? "OperatorPanelLandscape" : "OperatorPanel", opRoot);
        StretchFull((RectTransform)root.transform);
        CreatePanelBackground(root.transform, new Color(0.02f, 0.03f, 0.05f, 0.94f));
        var view = root.AddComponent<OperatorPanelView>();

        var contentGo = NewRect("Content", root.transform);
        var content = (RectTransform)contentGo.transform;
        if (landscape)
        {
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = new Vector2(2340f, 1080f);
            content.anchoredPosition = Vector2.zero;
            content.localRotation = Quaternion.Euler(0f, 0f, -90f);
        }
        else StretchFull(content);
        var p = content;

        TMP_Text cfgText, warnText, statusText;
        GameObject badge;
        Button resetBtn, closeBtn;
        OperatorClawCounts counts = null;

        if (!landscape)
        {
            CreateText(p, "Title", "운영자 패널", font, 76, cyan, new Vector2(0f, 900f), new Vector2(900f, 120f));
            cfgText = CreateText(p, "ConfigText", "", font, 46, Color.white, new Vector2(0f, 420f), new Vector2(940f, 780f));
            cfgText.alignment = TextAlignmentOptions.TopLeft;

            badge = NewRect("WarningsBadge", p);
            var brt = (RectTransform)badge.transform;
            brt.anchoredPosition = new Vector2(0f, -260f);
            brt.sizeDelta = new Vector2(940f, 320f);
            badge.AddComponent<Image>().color = new Color(0.75f, 0.4f, 0.05f, 0.9f);
            warnText = CreateText(badge.transform, "WarningsText", "", font, 38, Color.white, Vector2.zero, new Vector2(900f, 300f));
            warnText.alignment = TextAlignmentOptions.TopLeft;
            badge.SetActive(false);

            resetBtn = BuildScreenButton(p, "ResetButton", "게임 리셋", font, new Vector2(0f, -650f), new Vector2(620f, 150f), red);
            closeBtn = BuildScreenButton(p, "CloseButton", "닫기", font, new Vector2(0f, -860f), new Vector2(400f, 120f), gray);
            statusText = CreateText(p, "StatusText", "", font, 42, green, new Vector2(0f, -500f), new Vector2(940f, 90f));
        }
        else
        {
            CreateText(p, "Title", "운영자 패널 — 인형뽑기", font, 64, cyan, new Vector2(0f, 475f), new Vector2(2260f, 90f));
            cfgText = CreateText(p, "ConfigText", "", font, 44, Color.white, new Vector2(-480f, -40f), new Vector2(1300f, 920f));
            cfgText.alignment = TextAlignmentOptions.TopLeft;
            cfgText.overflowMode = TextOverflowModes.Truncate;

            counts = BuildClawCountRows(p, font);

            badge = NewRect("WarningsBadge", p);
            var brt = (RectTransform)badge.transform;
            brt.anchoredPosition = new Vector2(680f, -130f);
            brt.sizeDelta = new Vector2(900f, 220f);
            badge.AddComponent<Image>().color = new Color(0.75f, 0.4f, 0.05f, 0.9f);
            warnText = CreateText(badge.transform, "WarningsText", "", font, 40, Color.white, Vector2.zero, new Vector2(860f, 200f));
            warnText.alignment = TextAlignmentOptions.TopLeft;
            warnText.overflowMode = TextOverflowModes.Truncate;
            badge.SetActive(false);

            statusText = CreateText(p, "StatusText", "", font, 42, green, new Vector2(680f, -290f), new Vector2(900f, 70f));
            resetBtn = BuildScreenButton(p, "ResetButton", "게임 리셋", font, new Vector2(445f, -420f), new Vector2(430f, 130f), red);
            closeBtn = BuildScreenButton(p, "CloseButton", "닫기", font, new Vector2(915f, -420f), new Vector2(430f, 130f), gray);
        }
        resetBtn.gameObject.AddComponent<OperatorResetButton>(); // Button → 세션.ResetGame()
        // closeButton.onClick은 OperatorPanelController.Awake가 런타임 배선
        statusText.gameObject.SetActive(false);
        root.SetActive(false);

        SetRef(view, "root", root);
        SetRef(view, "content", content);
        SetRef(view, "configText", cfgText);
        SetRef(view, "warningsBadge", badge);
        SetRef(view, "warningsText", warnText);
        SetRef(view, "statusText", statusText);
        SetRef(view, "closeButton", closeBtn);
        SetRef(view, "resetButton", resetBtn);
        if (counts != null) SetRef(view, "counts", counts);
        return view;
    }

    /// <summary>가로 뷰 오른쪽 열의 스테퍼(10-28: 큐브·그릇(종류별) 2행 — 캡슐·폭탄은 config 전용) + 요약. 좌표는 2340×1080 콘텐츠 기준(중심 원점).</summary>
    static OperatorClawCounts BuildClawCountRows(Transform content, TMP_FontAsset font)
    {
        var rowsRoot = NewRect("ClawCounts", content);
        ((RectTransform)rowsRoot.transform).sizeDelta = Vector2.zero;
        var counts = rowsRoot.AddComponent<OperatorClawCounts>();

        string[] names = { "Cubes", "Bowls" };
        string[] labels = { "큐브", "그릇(종류별)" }; // 10-28 큐브 = 키링(점수) / 그릇 = 세라믹·유리 같은 수(방해물)
        string[] hints = { $"{OperatorClawCounts.CubeLo}~{OperatorClawCounts.CubeHi}", $"각 {OperatorClawCounts.BowlLo}~{OperatorClawCounts.BowlHi}" };
        float[] ys = { 370f, 250f };
        var btnColor = new Color(0.2f, 0.32f, 0.42f, 1f);
        var values = new TMP_Text[2];
        var minus = new Button[2];
        var plus = new Button[2];
        for (int i = 0; i < 2; i++)
        {
            var label = CreateText(rowsRoot.transform, names[i] + "Label", labels[i], font, 44, Color.white, new Vector2(340f, ys[i]), new Vector2(220f, 110f));
            label.alignment = TextAlignmentOptions.Left;
            minus[i] = BuildScreenButton(rowsRoot.transform, names[i] + "Minus", "−", font, new Vector2(525f, ys[i]), new Vector2(130f, 100f), btnColor, 60f);
            values[i] = CreateText(rowsRoot.transform, names[i] + "Value", "0", font, 56, new Color(0.4f, 0.9f, 1f), new Vector2(680f, ys[i]), new Vector2(160f, 110f));
            plus[i] = BuildScreenButton(rowsRoot.transform, names[i] + "Plus", "+", font, new Vector2(835f, ys[i]), new Vector2(130f, 100f), btnColor, 60f);
            var hint = CreateText(rowsRoot.transform, names[i] + "Hint", hints[i], font, 40, new Color(1f, 1f, 1f, 0.5f), new Vector2(1020f, ys[i]), new Vector2(220f, 110f));
            hint.raycastTarget = false;
            // 엄지 히트 영역 확대(시각 크기 유지)
            if (minus[i].targetGraphic is Image mi) mi.raycastPadding = new Vector4(-20f, -10f, -20f, -10f);
            if (plus[i].targetGraphic is Image pi) pi.raycastPadding = new Vector4(-20f, -10f, -20f, -10f);
        }
        var sum = CreateText(rowsRoot.transform, "SumText", "큐브 10 · 그릇 5씩 · 합계 20 / 60 · 성공 큐브 5개", font, 40, new Color(1f, 1f, 1f, 0.7f), new Vector2(680f, 140f), new Vector2(900f, 60f));

        SetRef(counts, "root", rowsRoot);
        SetRef(counts, "cubeValue", values[0]);
        SetRef(counts, "bowlValue", values[1]);
        SetRef(counts, "sumText", sum);
        SetRef(counts, "cubeMinus", minus[0]);
        SetRef(counts, "cubePlus", plus[0]);
        SetRef(counts, "bowlMinus", minus[1]);
        SetRef(counts, "bowlPlus", plus[1]);
        return counts;
    }

    // private [SerializeField] 필드 배선용 (기존 컴포넌트의 캡슐화를 깨지 않기 위함)
    static void SetRef(Component comp, string field, UnityEngine.Object value)
    {
        var so = new SerializedObject(comp);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogError($"[SouvenirHuntSetup] {comp.GetType().Name}.{field} 필드 없음 — 배선 실패");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
