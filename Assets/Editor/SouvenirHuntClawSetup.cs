using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;
using SouvenirHunt;

// 인형뽑기 미니게임(Claw 씬) 자동 구성 (09-03). SouvenirHuntSetup partial 확장 — 헬퍼(SetRef·CreateText·EnsureLayer 등) 재사용.
// 메뉴 30: Claw.unity 신규 생성(파괴적) + 인형뽑기 빌드 구성  /  31: 부팅 씬 토글(Main ↔ 인형뽑기)  /  32: 재생 중 물리 스트레스(폭발 20회).
// 10-15 미션형 복원: 모드1(컬러 게이트)·모드2(목표 수 맞추기) 삭제 → 세라믹·유리 그릇·캡슐·폭탄(SouvenirHuntClawItems.cs), 폭발 연출, 그릇 n/N 스코어보드.
// 14(운영자 패널)는 BuildClawPad로 Beam Pro 화면에 가로 오버레이(조이스틱·잡기)를 함께 만든다.
public static partial class SouvenirHuntSetup
{
    public const string ClawScenePath = "Assets/Scenes/Claw.unity";
    const string ClawItemPrefabPath = "Assets/Prefabs/ClawItem.prefab";
    const string ClawConeMeshPath = "Assets/Settings/ClawCone.asset";
    const string ClawPhysMatPath = "Assets/Settings/Claw_ItemPhys.physicsMaterial";
    /// <summary>미션형 아이템 규격 에셋(10-15). 없을 때만 만들고 이후엔 에셋 값이 소스오브트루스(튜닝 보존) — 그릇 높이/폭만 메시 실측으로 매번 갱신</summary>
    public const string ClawSpecMissionPath = "Assets/Settings/ClawSpec_Mission.asset";

    /// <summary>규격 에셋을 불러오거나(있으면 값 유지) 없으면 init으로 만들어 저장</summary>
    static ClawItemSpec GetOrCreateClawSpec(string path, Action<ClawItemSpec> init)
    {
        var spec = AssetDatabase.LoadAssetAtPath<ClawItemSpec>(path);
        if (spec != null) return spec;
        EnsureFolder("Assets/Settings");
        spec = ScriptableObject.CreateInstance<ClawItemSpec>();
        init?.Invoke(spec);
        AssetDatabase.CreateAsset(spec, path);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<ClawItemSpec>(path);
    }

    static ClawItemSpec GetOrCreateMissionSpec() => GetOrCreateClawSpec(ClawSpecMissionPath, null);

    /// <summary>메뉴 30 이후 실행 순서 (AutoRun 토큰 포함)</summary>
    public const string ClawRebuildOrder = "30 → @claw → 17 → 34 → 19 → @main → 14 → 11";

    struct ClawMats
    {
        public Material machineDark, accent, glass, guideAdditive, lineAdditive;
        public PhysicsMaterial phys;
    }

    // ─ 30. 인형뽑기 게임 씬 생성 ────────────────────────────────

    /// <summary>
    /// 인형뽑기 미션형 게임 씬. 기계(Model3)·부스·리그·패널·검정 디졸브·오버레이 + 아이템·폭발 연출.
    /// 인증(VIP)은 별도 씬 ClawIntro(메뉴 34)가 맡고, 끝나면 이 씬을 연다.
    /// </summary>
    [MenuItem("XREAL/30. Setup Claw Scene (인형뽑기 미션형 — 파괴적)")]
    public static void SetupClawScene()
    {
        string scenePath = ClawScenePath;
        try
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!Application.isBatchMode && !Quiet && File.Exists(scenePath) &&
                !EditorUtility.DisplayDialog("⚠ Claw.unity 재생성",
                    "인형뽑기 씬(Claw.unity)을 빈 씬에서 다시 만듭니다. 이후 17(조명)·34(인증 씬)·19(폰트)·14(운영자 패널)를 재실행하세요.",
                    "계속(재생성)", "취소"))
                return;

            var report = new StringBuilder("[SouvenirHuntSetup] Claw 씬 구성(미션형 — 그릇 n개 넣기):\n");

            int itemLayer = EnsureLayer("ClawItem");
            int machineLayer = EnsureLayer("ClawMachine");
            EnsureLayer("ClawHeld");
            int bedLayer = EnsureLayer("ClawBed"); // 10-20 받침 더미(정적 구 — 아이템하고만)
            EnsureLayer("ClawFinger"); // 10-22 물리 집게발(kinematic 캡슐 — 아이템하고만, ClawFingers가 런타임에 만든다)
            ClawBasket.ApplyLayerMatrix();
            report.AppendLine($"  · 레이어 ClawItem={itemLayer} ClawMachine={machineLayer} ClawHeld={LayerMask.NameToLayer("ClawHeld")} ClawBed={bedLayer} + 충돌 행렬");

            var font = GetOrCreateKoreanFont();
            var library = AssetDatabase.LoadAssetAtPath<MeshLibrary>(LibraryPath);
            if (library == null) library = BuildMeshLibrary();
            var mats = BuildClawMaterials();
            var modelParts = BuildClawModelParts(report);           // Model2: 유리 재질 등(Model3에서도 씀)
            if (ClawLayout.IsModel3) BuildClawModel3Parts(modelParts, report); // 10-13: 기계 몸체·헤드·캐리지·콘솔을 Model3로
            if (ClawLayout.SupClaw) BuildSupClawParts(modelParts, report);    // 10-19: 집게·캐리지·케이블은 StartUpPark 원본으로 덮어쓴다
            var coneMesh = BuildClawConeMesh();
            var items = BuildClawItemAssets(report);
            var spec = GetOrCreateMissionSpec();
            ApplyBowlRatios(spec, items);
            BuildClawItemPrefab(mats, items, itemLayer, spec);
            var boomClip = EnsureExplosionSound();
            var collectClip = EnsureCollectSound(); // 비주얼 2차: 그릇 획득 효과음
            WriteStreamingConfig();
            EnsureVipAssets(report); // 10-12 보상 카드·효과음 — NewScene 뒤 AddVipRewardCard가 다시 불러온다
            report.AppendLine($"  · 머티리얼·원뿔 메시·ClawItem 프리팹(그릇 헐 2·구 1)·폭발음 {(boomClip != null ? ExplosionWav : "없음")}·StreamingAssets config");
            report.AppendLine($"  · 규격 {spec.name}: 세라믹 {spec.Width(ClawItem.Kind.Ceramic):0.000}m·유리 {spec.Width(ClawItem.Kind.Glass):0.000}m·" +
                              $"캡슐 {spec.Width(ClawItem.Kind.Capsule):0.000}m·폭탄 {spec.Width(ClawItem.Kind.Bomb):0.000}m · 풀 {spec.poolSize}");

            // ─ 씬 ─
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene(Single)이 미사용 에셋을 내린다 — 방금 만든 에셋 참조가 무효(fake null)가 되어 배선이 0으로 저장되던 것 방지(10-01)
            spec = GetOrCreateMissionSpec();
            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClawItemPrefabPath);
            boomClip = VipClip(ExplosionWav);
            collectClip = VipClip(CollectWav);
            if (ClawLayout.IsModel3) ReloadModel3Parts(modelParts);
            if (ClawLayout.SupClaw) ReloadSupClawParts(modelParts);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            // 10-20 자연광 1단계: 조명 묶음(코드 생성 낮 하늘·실사 HDRI 2종) — config clawLighting으로 전환, studio = 이 씬의 지금 조명(메뉴 17)
            var lighting = lightGo.AddComponent<ClawLighting>();
            SetRef(lighting, "sun", light);
            SetRefArray(lighting, "presets", Array.ConvertAll(EnsureClawLightingPresets(report), p => (UnityEngine.Object)p));

            var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.xreal.xr/Runtime/Prefabs/XR Interaction Setup.prefab");
            if (rigAsset == null) throw new Exception("XR Interaction Setup.prefab 없음 — XREAL SDK 확인");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
            rig.AddComponent<XRRigDedup>(); // 10-15: 씬 전환 때 이전 씬 리그(SDK가 DontDestroyOnLoad)를 지워 카메라 중복 렌더 방지
            var origin = rig.GetComponentInChildren<XROrigin>();
            if (origin != null) origin.CameraYOffset = 0f;

            // BoothRoot(정렬 원점 = 눈높이) + 재정렬·흔들림·표시 제어
            var boothRoot = new GameObject("BoothRoot");
            var aligner = boothRoot.AddComponent<BoothAligner>();
            var shake = boothRoot.AddComponent<MachineShake>();
            var machineVis = boothRoot.AddComponent<MachineVisibility>();
            boothRoot.AddComponent<BoothApproach>(); // 10-28 기계 다가오기(세션이 없으면 런타임 자동 부착)

            var rw = new GameObject("RecenterWatcher").AddComponent<RecenterWatcher>();
            rw.transform.SetParent(boothRoot.transform, false);
            SetRef(rw, "booth", aligner);
            var recenterRef = FindRecenterActionReference();
            if (recenterRef != null) { SetRef(rw, "recenterAction", recenterRef); report.AppendLine($"  · RecenterWatcher (recenterAction={recenterRef.name})"); }
            else report.AppendLine("  ⚠ RecenterWatcher: 리센터 액션 못 찾음 — 리센터 재정렬 비활성(앱 정상)");

            // 기계 시각(몸체) — Environment 이름 유지(메뉴 17 호환). static 플래그는 주지 않는다(정렬·흔들림으로 움직임)
            var env = new GameObject("Environment");
            env.transform.SetParent(boothRoot.transform, false);
            // 10-19: 몸체·유리·배출구·콘솔 = StartUpPark 기계(집게·캐리지·케이블은 아래 BuildClawRig의 Model3 것)
            if (ClawLayout.CabinetSup) BuildSupMachine(env, machineLayer, report);
            else if (ClawLayout.IsModel3) BuildModel3Machine(env, machineLayer, modelParts, report);
            else BuildModelMachine(env, machineLayer, modelParts, report);
            if (!ClawLayout.CabinetSup) BuildChuteVisuals(env, machineLayer, mats); // StartUpPark는 기계 자체 배출구 상자
            var machineFx = BuildMachineFx(env, machineLayer, report); // 비주얼 2차: 네온·바닥 그리드(기계 치수 파생)

            // 부스(Model1) — 가운데 구조물 앞에 기계, 기계와 함께 등장(흔들림 제외)
            var booth = BuildClawBooth(boothRoot.transform, machineLayer, modelParts, report);

            // 바스켓(물리 상자)
            var basketGo = new GameObject("ClawBasket");
            basketGo.transform.SetParent(boothRoot.transform, false);
            var basket = basketGo.AddComponent<ClawBasket>();
            SetRef(basket, "spec", spec);
            BuildBasketColliders(basketGo, machineLayer, mats.phys);
            // 10-20 받침 더미(아래층 고정): 바스켓 콜라이더 안에서 물리로 쌓아 굽는다 — 그림은 BoothRoot/PileBed(기계와 함께 등장)
            var bed = BakeClawBed(basketGo, boothRoot.transform, spec, mats.phys, itemLayer, bedLayer, machineLayer, report);
            if (bed != null) SetRef(basket, "bed", bed);
            var chute = new GameObject("Chute");
            chute.transform.SetParent(basketGo.transform, false);
            chute.transform.localPosition = ClawLayout.ChuteLocal;
            SetRef(basket, "chute", chute.transform);

            // 폭발 연출 2세트(번갈아 — 연속 폭발에도 끊기지 않게). 바스켓 자식 = 재정렬 때 같이 움직임
            var explosions = new UnityEngine.Object[2];
            for (int i = 0; i < explosions.Length; i++) explosions[i] = BuildExplosionVfx(basketGo.transform, $"Explosion{i}", boomClip);
            report.AppendLine($"  · 폭발 연출 {explosions.Length}세트(번쩍임·불덩이·불꽃·충격파·연기 + 폭발음 — config audio=true일 때만 소리)");
            // 비주얼 2차: 그릇 획득 연출 2세트(배출구 청록 번쩍임·고리 2겹·반짝이·빛기둥 + 효과음)
            var collects = new UnityEngine.Object[2];
            for (int i = 0; i < collects.Length; i++) collects[i] = BuildCollectVfx(basketGo.transform, $"CollectFx{i}", collectClip);

            // 리그: 캐리지·케이블·헤드·가이드
            var clawRig = new GameObject("ClawRig");
            clawRig.transform.SetParent(boothRoot.transform, false);
            BuildClawRig(clawRig, machineLayer, mats, modelParts, coneMesh, basket, spec, out var carriage, out var head);

            // 스코어보드(뒷유리 바깥, 헤드와 파일 사이 띠, 월드 고정)
            var scoreboard = BuildScoreboard(boothRoot.transform, font);

            // 패널: 시작 / 가이드 / 결과·보상 / 종료 다이얼로그 — 전부 표시 전용(빔 미사용, 조작은 오버레이 잡기·조이스틱)
            BuildClawStartPanel(boothRoot.transform, font);
            BuildClawGuidePanel(boothRoot.transform, font, mats.lineAdditive);
            BuildClawResultAndReward(boothRoot.transform, font);
            var exitDialog = BuildClawExitDialog(boothRoot.transform, font);
            var reveal = BuildDigitalReveal(report);

            // HUD(LazyFollow) — PerfText(메뉴 17)용 캔버스만
            var hud = new GameObject("HUD");
            CreateWorldCanvas("HudCanvas", hud.transform, new Vector2(1100, 700));
            hud.AddComponent<SouvenirHunt.LazyFollow>();

            // 라이트 프로브(기계 주변 소격자, 치수 파생) — 메뉴 18 호환 이름
            var lpg = new GameObject("SpawnProbes").AddComponent<LightProbeGroup>();
            lpg.transform.SetParent(boothRoot.transform, false);
            var probes = new List<Vector3>();
            float px = ClawLayout.HalfX + 0.11f;
            for (float x = -px; x <= px + 0.001f; x += px * 2f / 3f)
                for (float y = ClawLayout.FloorY - 0.1f; y <= ClawLayout.RailY + 0.32f; y += 0.3f)
                    for (float z = ClawLayout.FrontZ - 0.1f; z <= ClawLayout.BackZ + 0.21f; z += 0.3f)
                        probes.Add(new Vector3(x, y, z));
            lpg.probePositions = probes.ToArray();

            // 풀 · 매니저 · 공용
            var poolGo = new GameObject("ClawItemPool");
            var pool = poolGo.AddComponent<ClawItemPool>();
            SetRef(pool, "prefab", itemPrefab.GetComponent<ClawItem>());
            SetRef(pool, "spec", spec);

            var suppressor = new GameObject("InteractorSuppressor").AddComponent<InteractorSuppressor>();

            var gmGo = new GameObject("ClawGameManager");
            var gm = gmGo.AddComponent<ClawGameManager>();
            gmGo.AddComponent<ClawSelfTest>();
            gmGo.AddComponent<ClawKeyboardFallback>();
            gmGo.AddComponent<ClawPerfProbe>(); // 성능 진단(config perfHide, 비우면 아무 것도 안 함)
            // 성능(10-15): 부스·기계 렌더 등급(config clawBooth·clawMachine). 셰이더를 씬이 참조해야 빌드에 들어간다
            var tier = gmGo.AddComponent<ClawRenderTier>();
            var liteShader = Shader.Find("SouvenirHunt/LiteLit");
            if (liteShader != null) SetRef(tier, "liteShader", liteShader);
            else report.AppendLine("  ⚠ 셰이더 SouvenirHunt/LiteLit 없음(컴파일 실패?) — clawBooth/clawMachine=lite가 원본으로 동작");
            SetRefArray(tier, "boothRoots", booth != null ? new UnityEngine.Object[] { booth } : new UnityEngine.Object[0]);
            SetRefArray(tier, "machineRoots", new UnityEngine.Object[] { env, clawRig });
            SetRef(gm, "pool", pool);
            SetRef(gm, "basket", basket);
            SetRef(gm, "carriage", carriage);
            SetRef(gm, "head", head);
            SetRef(gm, "meshLibrary", library);
            SetRef(gm, "booth", aligner);
            SetRef(gm, "machine", machineVis);
            SetRef(gm, "shake", shake);
            SetRef(gm, "suppressor", suppressor);
            SetRef(gm, "exitDialog", exitDialog);
            SetRef(gm, "reveal", reveal);
            SetRefArray(gm, "explosions", explosions);
            SetRefArray(gm, "collectFx", collects);
            if (machineFx != null) SetRef(gm, "machineFx", machineFx);
            SetRef(basket, "pool", pool);

            new GameObject("ClawPadPresenter").AddComponent<ClawPadPresenter>();

            var vfxPool = new GameObject("CollectVfxPool").AddComponent<CollectVfxPool>();
            var burst = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPrefabPath);
            if (burst != null) SetRef(vfxPool, "prefab", burst.GetComponent<CollectBurst>());
            else report.AppendLine("  ⚠ CollectBurst 프리팹 없음 — 메뉴 17이 만든다(VFX는 그때부터)");

            new GameObject("PerfMonitor").AddComponent<PerfMonitor>();
            new GameObject("RenderQualityApplier").AddComponent<RenderQualityApplier>();
            new GameObject("RuntimeExceptionCounter").AddComponent<RuntimeExceptionCounter>();

            // 순차 등장(6차): 부스가 바닥 아래에서 먼저, 이어서 기계(몸체·리그·스코어보드)가 그 앞에서 솟음
            var visRoots = new List<UnityEngine.Object> { env, clawRig, scoreboard };
            if (bed != null && bed.Render != null) visRoots.Add(bed.Render.gameObject); // 받침 더미도 기계와 함께 숨김·등장
            SetRefArray(machineVis, "roots", visRoots.ToArray());
            SetRefArray(machineVis, "boothRoots", booth != null ? new UnityEngine.Object[] { booth } : new UnityEngine.Object[0]);
            SetRefArray(shake, "targets", new UnityEngine.Object[] { env.transform, clawRig.transform });

            EditorSceneManager.SaveScene(scene, scenePath);
            SetClawBuildScenes(); // ClawIntro(있으면 부팅)·Claw 함께 빌드
            TargetScenePath = scenePath;
            report.AppendLine($"  · 저장 {scenePath} — 인형뽑기 빌드 구성(부팅 {BootScenePath}, 메뉴 31로 Main 전환)");
            report.AppendLine($"다음: {ClawRebuildOrder}");

            Debug.Log(report.ToString());
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup", report.ToString(), "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] SetupClawScene FAILED: " + e);
        }
    }

    // ─ 31. 부팅 씬 토글 ───────────────────────────────────────────

    [MenuItem("XREAL/31. Toggle Boot Scene (Main ↔ 인형뽑기)")]
    public static void ToggleBootScene()
    {
        try
        {
            string boot = BootScenePath;
            bool toMain = IsClawScene(boot);
            string next = toMain ? ScenePath : ClawScenePath;
            if (!File.Exists(next))
            {
                Debug.LogError($"[SouvenirHuntSetup] {next} 없음 — 먼저 메뉴 {(toMain ? "10" : "30")} 실행");
                return;
            }
            // 인형뽑기 쪽은 인증·게임 2씬 구성(부팅 = ClawIntro가 있으면 그것)
            if (toMain) SetBootScene(next); else SetClawBuildScenes();
            next = BootScenePath;
            TargetScenePath = toMain ? next : ClawScenePath;
            string msg = $"[SouvenirHuntSetup] 부팅 씬: {boot} → {next} (메뉴 11 빌드·17/18 대상 씬도 {Path.GetFileNameWithoutExtension(next)})";
            Debug.Log(msg);
            if (!Application.isBatchMode && !Quiet) EditorUtility.DisplayDialog("SouvenirHunt Setup", msg, "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] ToggleBootScene FAILED: " + e);
        }
    }

    // ─ 32. 물리 스트레스(재생 중) ─────────────────────────────────

    [MenuItem("XREAL/32. Claw Stress Test (재생 중 폭발 20회)")]
    public static void ClawStressTest()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SouvenirHuntSetup] 재생 중에만 실행 가능 (Claw 씬 Playing 상태에서)");
            return;
        }
        var basket = UnityEngine.Object.FindAnyObjectByType<ClawBasket>();
        if (basket == null) { Debug.LogWarning("[SouvenirHuntSetup] ClawBasket 없음 — Claw 씬이 아님"); return; }
        basket.StartStressTest(20);
        Debug.Log("[SouvenirHuntSetup] 스트레스 시작 — 약 20초 후 [ClawStress] 로그 확인 (폭발 20회 — 이탈 0·속도 상한·재정착)");
    }

    // ─ 빌드 씬 목록 관리 ──────────────────────────────────────────

    /// <summary>현재 부팅 씬(첫 enabled 항목). 없으면 Main.</summary>
    public static string BootScenePath
    {
        get
        {
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path)) return s.path;
            return ScenePath;
        }
    }

    /// <summary>bootPath만 enabled·index 0, 나머지(Main/Claw 포함)는 disabled로 유지 — APK엔 부팅 씬만 들어간다.</summary>
    public static void SetBootScene(string bootPath)
    {
        var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(bootPath, true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (!string.IsNullOrEmpty(s.path) && s.path != bootPath && !list.Exists(x => x.path == s.path))
                list.Add(new EditorBuildSettingsScene(s.path, false));
        foreach (var known in new[] { ScenePath, ClawScenePath, ClawSceneRouter.IntroScenePath })
            if (known != bootPath && File.Exists(known) && !list.Exists(x => x.path == known))
                list.Add(new EditorBuildSettingsScene(known, false));
        EditorBuildSettings.scenes = list.ToArray();
    }

    /// <summary>메뉴 10용 — 목록을 덮어쓰지 않고 Main을 보존 추가(비어 있으면 enabled).</summary>
    static void AddSceneToBuild(string path)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (list.Exists(s => s.path == path)) return;
        list.Add(new EditorBuildSettingsScene(path, list.Count == 0));
        EditorBuildSettings.scenes = list.ToArray();
    }

    /// <summary>메뉴 11용 — enabled이고 파일이 있는 씬만. 없으면 Main 폴백.</summary>
    static string[] BuildSceneList()
    {
        var list = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path)) list.Add(s.path);
        if (list.Count == 0) list.Add(ScenePath);
        return list.ToArray();
    }

    // ─ 에셋 빌더 ─────────────────────────────────────────────────

    static ClawMats BuildClawMaterials()
    {
        EnsureFolder("Assets/Settings");
        var m = new ClawMats();

        // 5차: 기계 몸체는 Model2 재질 — 아래는 슈트(구멍·프레임·울타리) 전용
        m.machineDark = ClawLit("Claw_Machine_Dark", new Color(0.12f, 0.13f, 0.16f), 0.3f, 0.5f);
        m.accent = ClawLit("Claw_Accent", new Color(0.3f, 0.85f, 0.9f), 0f, 0.6f);
        m.accent.SetColor("_EmissionColor", new Color(0f, 1.4f, 1.6f));
        m.accent.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // _EMISSION 유지(ClawLit 주석)
        EditorUtility.SetDirty(m.accent);

        // 슈트 울타리 유리: 밝은 틴트 α 0.10 — OST에서 어두움=투명이라 틴트는 밝게, 존재감은 발광 모서리 바가 담당(어둡게 금지)
        m.glass = ClawLit("Claw_Glass", new Color(0.62f, 0.88f, 1f, 0.10f), 0f, 0.85f);
        MakeLitTransparent(m.glass);

        m.guideAdditive = ClawAdditive("Claw_Guide_Additive", new Color(0.3f, 0.85f, 0.9f, 0.22f));
        m.lineAdditive = ClawAdditive("Claw_Line_Additive", Color.white);

        m.phys = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ClawPhysMatPath);
        if (m.phys == null)
        {
            var pm = new PhysicsMaterial("Claw_ItemPhys");
            try { AssetDatabase.CreateAsset(pm, ClawPhysMatPath); m.phys = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ClawPhysMatPath); }
            catch (Exception e) { Debug.LogWarning("[SouvenirHuntSetup] 물리 머티리얼 저장 실패(기본 마찰 사용): " + e.Message); m.phys = null; }
        }
        if (m.phys != null)
        {
            m.phys.dynamicFriction = 0.5f;
            m.phys.staticFriction = 0.6f;
            m.phys.bounciness = 0.05f;
            m.phys.frictionCombine = PhysicsMaterialCombine.Average;
            m.phys.bounceCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(m.phys);
        }
        AssetDatabase.SaveAssets();
        return m;
    }

    static Material GetOrCreateMat(string name, string shaderName)
    {
        string path = $"Assets/Settings/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        return mat;
    }

    static Material ClawLit(string name, Color baseColor, float metallic, float smoothness)
    {
        var mat = GetOrCreateMat(name, "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        mat.EnableKeyword("_EMISSION");                 // MPB 글로우가 먹도록
        // URP는 GI 플래그에 Emissive 비트가 있을 때만 _EMISSION을 유지한다(재임포트 때 ValidateMaterial이 다시 계산 — 10-06).
        // None이면 재질이 다시 임포트될 때마다 키워드가 지워져 공·게이트 발광이 빠진 APK가 나온다. 실시간 GI는 안 쓰므로 화면 영향 없음.
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void MakeLitTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend", 0f);     // Alpha
        // URP Lit 알파 블렌드는 반사광 보존(셰이더가 알파를 미리 곱함)이라 Src = One — 재임포트 때 URP가 계산하는 값과 같게(10-12, 커밋본 .mat 기준)
        mat.SetFloat("_SrcBlend", (float)BlendMode.One);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
    }

    static Material ClawAdditive(string name, Color color)
    {
        var mat = GetOrCreateMat(name, "Universal Render Pipeline/Particles/Unlit");
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);     // Additive
        // URP 파티클 가산 = SrcAlpha·One(알파로 밝기 조절) — 재임포트 때 URP가 계산하는 값과 같게(10-12, 커밋본 .mat 기준)
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
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

    /// <summary>가이드 원뿔: 꼭지 y=0(반지름 ConeTipRatio), 밑면 y=−1(반지름 1), 16각, 측면만. y 스케일 = 높이(m),
    /// x·z 스케일 = 포획 링 반지름(10-01: 게임마다 spec이 달라 ClawGuide가 런타임에 맞춘다 — 메시는 두 씬 공용).</summary>
    const float ConeTipRatio = 0.09f; // 링 0.135m에서 꼭지 반지름 약 0.012m(11차와 같은 모양)
    static Mesh BuildClawConeMesh()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ClawConeMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "ClawCone" };
            AssetDatabase.CreateAsset(mesh, ClawConeMeshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ClawConeMeshPath);
        }
        const int segs = 16;
        var verts = new Vector3[segs * 2];
        var uvs = new Vector2[segs * 2];
        var tris = new int[segs * 6];
        for (int i = 0; i < segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i] = new Vector3(c * ConeTipRatio, 0f, s * ConeTipRatio);
            verts[segs + i] = new Vector3(c, -1f, s);
            uvs[i] = new Vector2(i / (float)segs, 0f);
            uvs[segs + i] = new Vector2(i / (float)segs, 1f);
            int n = (i + 1) % segs;
            int t = i * 6;
            tris[t] = i; tris[t + 1] = segs + i; tris[t + 2] = segs + n;
            tris[t + 3] = i; tris[t + 4] = segs + n; tris[t + 5] = n;
        }
        mesh.Clear();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// 아이템 프리팹 1종(10-15): 루트 = Rigidbody + 구 충돌체(캡슐·폭탄) + ClawItem, 자식 Visual(메시·재질은 SetKind가 교체),
    /// 자식 CeramicHull·GlassHull(볼록 MeshCollider, 폭 1 헐 — ClawItem.Awake가 종류 폭으로 스케일). 종류마다 충돌체 하나만 켠다.
    /// </summary>
    static GameObject BuildClawItemPrefab(ClawMats mats, ClawItemAssets items, int itemLayer, ClawItemSpec spec)
    {
        EnsureFolder("Assets/Prefabs");
        var root = new GameObject("ClawItem") { layer = itemLayer };
        var rb = root.AddComponent<Rigidbody>();
        spec.ConfigureBody(rb, ClawItem.Kind.Capsule);
        var sphere = root.AddComponent<SphereCollider>();
        sphere.radius = spec.Width(ClawItem.Kind.Capsule) * 0.5f;
        sphere.contactOffset = spec.contactOffset;
        if (mats.phys != null) sphere.sharedMaterial = mats.phys;

        var visual = new GameObject("Visual") { layer = itemLayer };
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * spec.Width(ClawItem.Kind.Capsule);
        visual.AddComponent<MeshFilter>().sharedMesh = items.capsule;
        var mr = visual.AddComponent<MeshRenderer>();
        mr.sharedMaterial = items.capsuleMats[0];
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off; // 40개 × 스테레오 — 자체 발광 + 주광만
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        MeshCollider Hull(string name, Mesh mesh, ClawItem.Kind kind)
        {
            var go = new GameObject(name) { layer = itemLayer };
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * spec.Width(kind);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            mc.contactOffset = spec.contactOffset;
            if (mats.phys != null) mc.sharedMaterial = mats.phys;
            mc.enabled = false;
            return mc;
        }
        var ceramicHull = Hull("CeramicHull", items.ceramicHull, ClawItem.Kind.Ceramic);
        var glassHull = Hull("GlassHull", items.glassHull, ClawItem.Kind.Glass);
        // 10-22 키링: 큐브 몸통 상자(모서리 1 × Width) — 체인·고리는 시각만(볼록 헐이면 체인까지 감싼 넓은 판이 돼 옆 아이템이 떠 보임)
        var keyringGo = new GameObject("KeyringBox") { layer = itemLayer };
        keyringGo.transform.SetParent(root.transform, false);
        keyringGo.transform.localScale = Vector3.one * spec.Width(ClawItem.Kind.Keyring);
        var keyringBox = keyringGo.AddComponent<BoxCollider>();
        keyringBox.size = Vector3.one;
        keyringBox.contactOffset = spec.contactOffset;
        if (mats.phys != null) keyringBox.sharedMaterial = mats.phys;
        keyringBox.enabled = false;

        var item = root.AddComponent<ClawItem>();
        SetRef(item, "spec", spec);
        SetRef(item, "visual", mr);
        SetRef(item, "sphere", sphere);
        SetRef(item, "ceramicHull", ceramicHull);
        SetRef(item, "glassHull", glassHull);
        SetRef(item, "ceramicMesh", items.ceramic);
        SetRef(item, "glassMesh", items.glass);
        SetRef(item, "capsuleMesh", items.capsule);
        SetRef(item, "bombMesh", items.bomb);
        SetRef(item, "ceramicMaterial", items.ceramicMat);
        SetRef(item, "glassMaterial", items.glassMat);
        SetRefArray(item, "capsuleMaterials", items.capsuleMats);
        SetRef(item, "bombMaterial", items.bombMat);
        SetRef(item, "keyringBox", keyringBox);
        if (items.keyring != null) SetRef(item, "keyringMesh", items.keyring);
        if (items.keyringMat != null) SetRef(item, "keyringMaterial", items.keyringMat);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, ClawItemPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    // ─ 씬 파트 빌더 ──────────────────────────────────────────────

    /// <summary>EnvPrim과 달리 isStatic을 주지 않는다(재정렬·흔들림으로 움직이는 시각 루트). 콜라이더 제거.</summary>
    static GameObject ClawPrim(Transform parent, int layer, PrimitiveType type, string name,
                               Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.DestroyImmediate(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.transform.localRotation = localRot;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.layer = layer;
        return go;
    }

    /// <summary>
    /// 슈트 시각(5차 — 기계 몸체는 Model2, BuildModelMachine). 앞-왼쪽 캐리지 도달 한계 지점의 구멍·프레임 + 파일 쪽 두 면 유리 울타리.
    /// 동일 평면(z-fighting) 금지 규칙(4차): 유리 바닥(FloorY = 콘솔 상판)·바닥 프레임 링(FloorY~+0.025)과 겹치는 면은 박거나 3mm 이상 띄운다.
    /// 콜라이더는 BuildBasketColliders — 여기는 전부 시각 전용.
    /// </summary>
    static void BuildChuteVisuals(GameObject env, int layer, ClawMats m)
    {
        var p = new GameObject("Chute") { layer = layer }.transform;
        p.SetParent(env.transform, false);
        var q = Quaternion.identity;
        float floorY = ClawLayout.FloorY, hx = ClawLayout.HalfX, fz = ClawLayout.FrontZ;
        float hole = ClawLayout.ChuteHoleSize;

        // 구멍(어두운 판, 바닥 위 3mm) + 발광 프레임(바닥에 4mm 박힘·6mm 돌출, 구멍보다 한 변 3cm 큼)
        var ch = ClawLayout.ChuteLocal;
        ClawPrim(p, layer, PrimitiveType.Cube, "ChuteFrame", new Vector3(ch.x, floorY + 0.001f, ch.z), new Vector3(hole + 0.03f, 0.010f, hole + 0.03f), q, m.accent);
        ClawPrim(p, layer, PrimitiveType.Cube, "ChuteHole", new Vector3(ch.x, floorY + 0.004f, ch.z), new Vector3(hole, 0.010f, hole), q, m.machineDark);

        // 유리 울타리(파일 쪽 두 면): 바깥 끝은 기계 유리 안쪽면 너머 9mm(틈 없음), 하단은 바닥 아래 5mm, 상단은 액센트 바(0.008) 안
        float cwx = ClawLayout.ChuteWallX, cwz = ClawLayout.ChuteWallZ, cwh = ClawLayout.ChuteGuardHeight;
        float cwBottom = floorY - 0.005f, cwTop = floorY + cwh - 0.004f;
        float cwMid = (cwBottom + cwTop) * 0.5f, cwHeight = cwTop - cwBottom;
        float cwFront = fz - 0.009f, cwLeft = -hx - 0.009f;
        ClawPrim(p, layer, PrimitiveType.Cube, "ChuteGlassX", new Vector3(cwx - 0.003f, cwMid, (cwFront + cwz) * 0.5f), new Vector3(0.006f, cwHeight, cwz - cwFront), q, m.glass);
        ClawPrim(p, layer, PrimitiveType.Cube, "ChuteGlassZ", new Vector3((cwLeft + cwx) * 0.5f, cwMid, cwz - 0.003f), new Vector3(cwx - cwLeft, cwHeight, 0.006f), q, m.glass);
        float cwEdgeY = floorY + cwh - 0.004f;
        ClawPrim(p, layer, PrimitiveType.Cube, "ChuteEdgeX", new Vector3(cwx - 0.003f, cwEdgeY, (cwFront + cwz + 0.004f) * 0.5f), new Vector3(0.008f, 0.008f, cwz + 0.004f - cwFront), q, m.accent);
        ClawPrim(p, layer, PrimitiveType.Cube, "ChuteEdgeZ", new Vector3((cwLeft + cwx - 0.007f) * 0.5f, cwEdgeY, cwz - 0.003f), new Vector3(cwx - 0.007f - cwLeft, 0.008f, 0.008f), q, m.accent);
    }

    static void BuildBasketColliders(GameObject basketGo, int layer, PhysicsMaterial phys)
    {
        var root = new GameObject("Colliders") { layer = layer };
        root.transform.SetParent(basketGo.transform, false);
        float cz = ClawLayout.CenterZ, t = ClawLayout.WallThickness;
        float sizeX = ClawLayout.InnerX + 2f * t, sizeZ = ClawLayout.InnerZ + 2f * t;
        float wallH = ClawLayout.LidY - ClawLayout.FloorY + 2f * t;
        float wallMid = (ClawLayout.LidY + ClawLayout.FloorY) * 0.5f;
        void Box(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = center;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            if (phys != null) bc.sharedMaterial = phys;
        }
        Box("Floor", new Vector3(0f, ClawLayout.FloorY - t * 0.5f, cz), new Vector3(sizeX, t, sizeZ));
        Box("Lid", new Vector3(0f, ClawLayout.LidY + t * 0.5f, cz), new Vector3(sizeX, t, sizeZ));
        Box("WallFront", new Vector3(0f, wallMid, ClawLayout.FrontZ - t * 0.5f), new Vector3(sizeX, wallH, t));
        Box("WallBack", new Vector3(0f, wallMid, ClawLayout.BackZ + t * 0.5f), new Vector3(sizeX, wallH, t));
        Box("WallLeft", new Vector3(-ClawLayout.HalfX - t * 0.5f, wallMid, cz), new Vector3(t, wallH, sizeZ));
        Box("WallRight", new Vector3(ClawLayout.HalfX + t * 0.5f, wallMid, cz), new Vector3(t, wallH, sizeZ));

        // 모서리 기둥(5차, Model2): 유리 안쪽 코너에서 PillarInset만큼 튀어나온 기둥 — 코너 공이 기둥을 파고들지 않게. 벽 쪽으로 두께 t 겹침
        float pix = ClawLayout.PillarInsetX, piz = ClawLayout.PillarInsetZ, psx = pix + t, psz = piz + t;
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            Box($"Pillar{(sz < 0 ? 'F' : 'B')}{(sx < 0 ? 'L' : 'R')}",
                new Vector3(sx * (ClawLayout.HalfX - pix + psx * 0.5f), wallMid, cz + sz * (ClawLayout.HalfZ - piz + psz * 0.5f)),
                new Vector3(psx, wallH, psz));

        // 배출구 칸 = 보이지 않는 기둥(10-20, 사용자 "밀리거나 폭발로 넘어가 배출되는 일이 없게 투명 벽을 상단까지"): 칸 전체를 바닥 아래~뚜껑 위까지 채운 상자 1개.
        // 안쪽 두 면 = 칸막이 좌표(ChuteWallX/Z). 집게(콜라이더 없음)·잡힌 상품(ClawHeld 무충돌)만 지나가고 배달은 애니메이션이라 그대로.
        // 4차의 두 장 울타리(높이 0.22)는 위로 넘어 들어오는 것을 못 막아 안전망(침입 복구)에 기댔다.
        // 바깥 세 면(왼쪽·앞·아래)은 벽 두께만큼 밖으로 — 깊이 박힌 것도 가장 가까운 면 = 파일 쪽 안쪽 면으로 밀려나게(아래로 빠지지 않게)
        float bx0 = -ClawLayout.HalfX - t, bz0 = ClawLayout.FrontZ - t;
        float by0 = ClawLayout.FloorY - t, by1 = ClawLayout.LidY + t;
        Box("ChuteBarrier", new Vector3((bx0 + ClawLayout.ChuteWallX) * 0.5f, (by0 + by1) * 0.5f, (bz0 + ClawLayout.ChuteWallZ) * 0.5f),
            new Vector3(ClawLayout.ChuteWallX - bx0, by1 - by0, ClawLayout.ChuteWallZ - bz0));
    }

    static void BuildClawRig(GameObject rig, int layer, ClawMats m, ClawModelParts parts, Mesh coneMesh, ClawBasket basket, ClawItemSpec spec,
                             out ClawCarriage carriage, out ClawHead head)
    {
        // 5차: 캐리지·케이블·헤드 시각 = Model2 UFO 파트(부스 방향으로 구운 메시, 로컬 회전 identity). 레일은 없음 — 마운트 판이 천장을 따라 미끄러진다
        // 캐리지 = 천장 마운트 판(원점 = 판 하면 = 케이블 앵커 = RailY)
        var carriageGo = new GameObject("Carriage");
        carriageGo.transform.SetParent(rig.transform, false);
        carriageGo.transform.localPosition = new Vector3(0f, ClawLayout.RailY, ClawLayout.CenterZ);
        carriage = carriageGo.AddComponent<ClawCarriage>();
        MeshPart(carriageGo.transform, "UfoMount", parts.ufoMount, Vector3.zero, parts.mat, layer);
        var anchor = new GameObject("CableAnchor");
        anchor.transform.SetParent(carriageGo.transform, false);

        // 케이블 = 코일·중심축 메시(원점 = 앵커), ClawCableMesh가 앵커→헤드 상단으로 늘인다
        var cableGo = MeshPart(rig.transform, "Cable", parts.ufoCable, new Vector3(0f, ClawLayout.RailY, ClawLayout.CenterZ), parts.mat, layer);
        var cableMesh = cableGo.AddComponent<ClawCableMesh>();

        // 헤드(UFO 본체, 원점 = 본체 중심)
        var headGo = new GameObject("Head");
        headGo.transform.SetParent(rig.transform, false);
        headGo.transform.localPosition = new Vector3(0f, ClawLayout.RailY - ClawLayout.CableLength, ClawLayout.CenterZ);
        head = headGo.AddComponent<ClawHead>();
        var hv = new GameObject("HeadVisual");
        hv.transform.SetParent(headGo.transform, false);
        if (parts.rig != null && parts.rig.mesh != null)
        {
            // 10-22 뼈 집게: 스프링·허브·팔·발이 한 스킨 메시, 발마다 관절 3개(팔·윗마디·갈고리)를 ClawFingers가 클립 자세로 섞어 굽힌다
            var roots = BuildSupRigVisual(hv.transform, parts.rig, parts.mat, layer);
            var fingers = headGo.AddComponent<ClawFingers>();
            SetRef(fingers, "head", head);
            SetRefArray(fingers, "pivots", Array.ConvertAll(roots, t => (UnityEngine.Object)t));
            SetRef(fingers, "skin", hv.GetComponentInChildren<SkinnedMeshRenderer>());
            SetQuatArray(fingers, "closePose", parts.rig.closePose);
            SetQuatArray(fingers, "openPose", parts.rig.openPose);
            // 각도 눈금: 0 = 휴식(클립 첫 자세), −48 = 클립 오므림, +60 = 클립 펼침(윗마디 약 +60°)
            SetFloatField(fingers, "poseCloseDeg", -48f);
            SetFloatField(fingers, "poseOpenDeg", 60f);
            SetFloatField(fingers, "restDeg", 0f);       // 유휴 = 원작 휴식 자세
            SetFloatField(fingers, "openDeg", 15f);      // 하강·놓기 = 윗마디 +15°(발끝 반지름 약 0.26 — 예전 펼침과 비슷)
            SetFloatField(fingers, "fullCloseDeg", -48f);
            SetFloatField(fingers, "recoilDeg", 28f);
        }
        else MeshPart(hv.transform, "UfoBody", parts.ufoBody, Vector3.zero, parts.mat, layer);
        // 10-19: 집게발 3개 — 관절 피벗(헤드 비주얼 자식)에 따로 붙여 ClawFingers가 잡기 단계에 따라 여닫는다
        if ((parts.rig == null || parts.rig.mesh == null) && parts.fingers != null && parts.fingerHinges != null)
        {
            var pivots = new UnityEngine.Object[parts.fingers.Length];
            for (int k = 0; k < parts.fingers.Length; k++)
            {
                var pv = new GameObject($"FingerPivot{k}") { layer = layer };
                pv.transform.SetParent(hv.transform, false);
                pv.transform.localPosition = parts.fingerHinges[k];
                MeshPart(pv.transform, "Finger", parts.fingers[k], Vector3.zero, parts.mat, layer);
                pivots[k] = pv.transform;
            }
            var fingers = headGo.AddComponent<ClawFingers>();
            SetRef(fingers, "head", head);
            SetRefArray(fingers, "pivots", pivots);
        }
        var attach = new GameObject("AttachPoint");
        attach.transform.SetParent(headGo.transform, false);
        attach.transform.localPosition = new Vector3(0f, spec.AttachOffsetY(ClawItem.Kind.Ceramic), 0f); // 표시용 기준 — 실제 들린 위치는 ClawHead가 종류별로

        // 가이드(원뿔·링·레이저)
        var guideGo = new GameObject("Guide");
        guideGo.transform.SetParent(rig.transform, false);
        var guide = guideGo.AddComponent<ClawGuide>();
        // 10-19(사용자 "원뿔형 레이저 같은 잡는 연출이 더 필요할까? 밑에 원으로 잡을 물체 표시하는 것만"): 집게발이 실제로 여닫히므로
        // 헤드→대상 원뿔과 잡을 때 줄어드는 레이저 고리는 만들지 않는다(ClawGuide는 둘 다 없으면 건너뜀). 대상 링·핑 고리·대상 발광만 남김
        LineRenderer MakeLine(string name, float width)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(guideGo.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.startWidth = lr.endWidth = width;
            lr.sharedMaterial = m.lineAdditive;
            lr.startColor = lr.endColor = new Color(0.3f, 0.85f, 0.9f, 1f);
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }
        // 10-19 조준 표시(사용자 "원형 이펙트가 조금 더 눈에 띄게 + 가느다란 선" → 시안 3 스포트라이트 + 골드 선택): 예전 링·핑 고리 대신
        // 대상 밑 바닥 표시판(절차적 셰이더) + 집게→대상 가는 선(떨어지는 빛 방울). 색은 config clawGuideColor
        var markerGo = new GameObject("Marker") { layer = layer };
        markerGo.transform.SetParent(guideGo.transform, false);
        markerGo.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var markerMr = markerGo.AddComponent<MeshRenderer>();
        var markerMat = FxShaderMat("Claw_GuideMarker", "SouvenirHunt/ClawGuideMarker");
        if (markerMat != null)
        {
            // 10-19 색 시안: 색은 런타임 config clawGuideColor(MPB). 세기 1.3·덮기 0.7 — 테두리는 진한 원색, 원판은 반투명
            markerMat.SetColor("_Color", new Color(1f, 0.7f, 0.08f, 1f));
            markerMat.SetFloat("_Intensity", 1.3f);
            markerMat.SetFloat("_Opacity", 0.7f);
            EditorUtility.SetDirty(markerMat);
        }
        markerMr.sharedMaterial = markerMat;
        markerMr.shadowCastingMode = ShadowCastingMode.Off;
        markerMr.receiveShadows = false;
        var beam = MakeLine("Beam", 0.004f);
        var beamMat = FxShaderMat("Claw_GuideBeam", "SouvenirHunt/ClawGuideBeam");
        if (beamMat != null)
        {
            beamMat.SetColor("_Color", new Color(1f, 0.7f, 0.08f, 1f));
            beamMat.SetFloat("_Intensity", 1.6f);
            beamMat.SetFloat("_Opacity", 0.8f);
            EditorUtility.SetDirty(beamMat);
        }
        beam.sharedMaterial = beamMat;
        beam.textureMode = LineTextureMode.Stretch;
        beam.startColor = beam.endColor = Color.white;
        beam.numCapVertices = 2;
        SetRef(guide, "marker", markerMr);
        SetRef(guide, "beam", beam);
        SetRef(guide, "spec", spec);
        var scanMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Claw_GuideScan.mat"); // BuildMachineFx(FxShaderMat)가 만든 것
        if (scanMat != null) SetRef(guide, "spotConeMaterial", scanMat); // 10-22 조준 원뿔(S2)

        SetRef(head, "spec", spec);
        SetRef(head, "carriage", carriage);
        SetRef(head, "basket", basket);
        SetRef(head, "guide", guide);
        SetRef(head, "attachPoint", attach.transform);
        SetRef(head, "cableAnchor", anchor.transform);
        SetRef(head, "cableMesh", cableMesh);
    }

    static GameObject BuildScoreboard(Transform boothRoot, TMP_FontAsset font)
    {
        var root = new GameObject("Scoreboard");
        root.transform.SetParent(boothRoot, false);
        root.transform.localPosition = ClawLayout.ScoreboardLocal;
        // 눈을 향하게(7차 −15°에서는 12° 기울임 — 10-20 StartUpPark 0.5°는 눈 방향 그대로)
        root.transform.localRotation = ClawLayout.ScoreboardSplit ? Quaternion.LookRotation(ClawLayout.ScoreboardLocal.normalized, Vector3.up) : Quaternion.Euler(12f, 0f, 0f);
        root.transform.localScale = Vector3.one * ClawLayout.ScoreboardScale; // 7차: 뒤 기둥 창 안으로

        // 10-15 미션형: 왼쪽 열 그릇 n / N + 종류별, 오른쪽 열 남은 시간(경고 시 빨간 점멸) + 아이템 구성. 가운데는 유휴 UFO가 가려 비움(5차).
        // 배너는 아래 한 줄(획득!·캡슐 — 무득점·폭탄!·성공!). 카운트다운은 가운데 크게
        // 10-20 StartUpPark(ScoreboardSplit): 가운데를 감겨 올라간 집게가 가려 배너·카운트다운도 좌·우 열 아래·안에 둘 다 띄운다(배경은 열 뒤에만)
        bool split = ClawLayout.ScoreboardSplit;
        float w = ClawLayout.ScoreboardWidth, colW = ClawLayout.ScoreboardColumnWidth, side = w * 0.5f - colW * 0.5f - 12f;
        var canvas = CreateWorldCanvas("ScoreCanvas", root.transform, new Vector2(w, 230));
        var bgColor = new Color(0.02f, 0.03f, 0.05f, 0.85f);
        if (!split) CreatePanelBackground(canvas.transform, bgColor);
        else
            foreach (float sx in new[] { -side, side })
            {
                var bg = new GameObject(sx < 0 ? "BgLeft" : "BgRight", typeof(RectTransform));
                bg.transform.SetParent(canvas.transform, false);
                var rt = (RectTransform)bg.transform;
                rt.anchoredPosition = new Vector2(sx, 0f);
                rt.sizeDelta = new Vector2(colW + 36f, 226f);
                bg.AddComponent<Image>().color = bgColor;
            }
        var cyan = new Color(0.4f, 0.9f, 1f);
        var left = CreateText(canvas.transform, "Left", "큐브 0 / 5\n그릇은 점수 없음", font, 34, cyan, new Vector2(-side, 40f), new Vector2(colW, 120f));
        left.alignment = TextAlignmentOptions.Left;
        left.lineSpacing = -10f;
        var right = CreateText(canvas.transform, "Right", "캡슐 0 · 폭탄 0", font, 28, cyan, new Vector2(side, 62f), new Vector2(colW, 60f));
        right.alignment = TextAlignmentOptions.Right;
        var time = CreateText(canvas.transform, "Time", "0:45", font, 44, cyan, new Vector2(side, 8f), new Vector2(colW, 56f));
        time.alignment = TextAlignmentOptions.Right;
        if (split)
        {
            // 열이 좁아(230) 긴 줄(세라믹 0 · 유리 0, 캡슐 33 · 폭탄 4)은 자동으로 줄여 열 안에
            left.enableAutoSizing = true; left.fontSizeMin = 20f; left.fontSizeMax = 34f;
            right.enableAutoSizing = true; right.fontSizeMin = 16f; right.fontSizeMax = 28f;
        }
        TMP_Text MakeBanner(string name, float x, float width)
        {
            var b = CreateText(canvas.transform, name, "", font, 30, Color.white, new Vector2(x, -62f), new Vector2(width, 46f));
            b.enableAutoSizing = true;
            b.fontSizeMin = 18f;
            b.fontSizeMax = 30f;
            b.gameObject.SetActive(false);
            return b;
        }
        var banner = split ? MakeBanner("Banner", -side, colW) : MakeBanner("Banner", 0f, w - 20f);
        var banner2 = split ? MakeBanner("Banner2", side, colW) : null;
        var cdRoot = new GameObject("CountdownRoot", typeof(RectTransform));
        cdRoot.transform.SetParent(canvas.transform, false);
        var cdText = CreateText(cdRoot.transform, "CountdownText", "3", font, 110, cyan, new Vector2(split ? -side : 0f, 0f), new Vector2(split ? colW : w, 150f));
        var cdText2 = split ? CreateText(cdRoot.transform, "CountdownText2", "3", font, 110, cyan, new Vector2(side, 0f), new Vector2(colW, 150f)) : null;
        cdRoot.SetActive(false);

        var sb = root.AddComponent<ClawScoreboard>();
        SetRef(sb, "leftText", left);
        SetRef(sb, "rightText", right);
        SetRef(sb, "timeText", time);
        SetRef(sb, "countdownRoot", cdRoot);
        SetRef(sb, "countdownText", cdText);
        SetRef(sb, "bannerText", banner);
        if (cdText2 != null) SetRef(sb, "countdownText2", cdText2);
        if (banner2 != null) SetRef(sb, "bannerText2", banner2);
        return root;
    }

    static void BuildClawStartPanel(Transform boothRoot, TMP_FontAsset font)
    {
        // 표시 전용(3차, 빔 미사용) — 콜라이더·인터랙터블 없음. 시작은 오버레이 [잡기]
        // 5차: 기계 앞 시선 방향(상단 간판이 기계 위·뒤를 가림), 기존 2.0m 각크기 유지 스케일
        var root = new GameObject("StartPanel");
        root.transform.SetParent(boothRoot, false);
        root.transform.localPosition = ClawLayout.StartPanelLocal;
        root.transform.localRotation = Quaternion.LookRotation(ClawLayout.StartPanelLocal.normalized, Vector3.up);
        root.transform.localScale = Vector3.one * ClawLayout.StartPanelScale;

        var canvasGo = CreateWorldCanvas("StartCanvas", root.transform, new Vector2(900, 450));
        CreatePanelBackground(canvasGo.transform, new Color(0.06f, 0.1f, 0.16f, 0.92f));
        CreateText(canvasGo.transform, "Title", "상품 미션", font, 90, new Color(0.4f, 0.9f, 1f), new Vector2(0, 120), new Vector2(860, 130));
        // 규칙 한 줄 요약(10-15 미션형) — 개수·시간은 config라 숫자 없이(스코어보드가 그릇 n / N·남은 시간 표시)
        // 10-26 상품만(세라믹 그릇·유리 그릇·키링 1:1:1, 캡슐·폭탄 없음) — 목표 수는 config라 숫자 없이(스코어보드가 같은 상품 n / N)
        // 10-28 점수 = 키링 큐브만, 그릇은 방해물(스코어보드가 큐브 n / N)
        CreateText(canvasGo.transform, "Rule", "제한 시간 안에 키링 큐브를 모아 넣으세요\n그릇은 점수가 없어요 — 큐브로 목표 수를 채우면 성공!",
                   font, 40, new Color(1f, 0.85f, 0.4f), new Vector2(0, 5), new Vector2(860, 110));
        CreateText(canvasGo.transform, "Button", "Beam Pro 큰 버튼 [공간 열기]를 눌러 주세요", font, 46, Color.white, new Vector2(0, -130), new Vector2(860, 100));

        var ctrl = root.AddComponent<ClawStartPanel>();
        SetRef(ctrl, "visualRoot", canvasGo);
    }

    /// <summary>
    /// Guide 안내(4차 분리): 프레임은 가슴 앞(GuideFrameLocal, −54°) — Beam Pro를 겹치는 가상 틀. 문구는 정면 시야 하단(GuideTextLocal, 중심 −13.5°)에
    /// 윗부분이 걸려 아래를 보게 유도. 문구 패널은 유리 박스 부피 안에 있으므로 Guide 중에는 기계가 숨겨져 있어야 한다(MachineVisibility.Hide).
    /// </summary>
    static void BuildClawGuidePanel(Transform boothRoot, TMP_FontAsset font, Material lineMat)
    {
        var root = new GameObject("GuidePanel");
        root.transform.SetParent(boothRoot, false);

        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);

        // 프레임 루트 — 눈(원점)을 향하도록(forward = 눈에서 멀어지는 방향)
        var frameRoot = new GameObject("FrameRoot");
        frameRoot.transform.SetParent(visual.transform, false);
        frameRoot.transform.localPosition = ClawLayout.GuideFrameLocal;
        frameRoot.transform.localRotation = Quaternion.LookRotation(ClawLayout.GuideFrameLocal.normalized, Vector3.up);

        var frameGo = new GameObject("Frame");
        frameGo.transform.SetParent(frameRoot.transform, false);
        var frame = frameGo.AddComponent<LineRenderer>();
        frame.useWorldSpace = false;
        frame.loop = true;
        frame.positionCount = 4;
        float w = ClawLayout.GuideFrameW * 0.5f, h = ClawLayout.GuideFrameH * 0.5f;
        frame.SetPositions(new[] { new Vector3(-w, -h, 0f), new Vector3(w, -h, 0f), new Vector3(w, h, 0f), new Vector3(-w, h, 0f) });
        frame.startWidth = frame.endWidth = 0.006f;
        frame.sharedMaterial = lineMat;
        frame.startColor = frame.endColor = new Color(0.3f, 0.85f, 0.9f, 1f);
        frame.shadowCastingMode = ShadowCastingMode.Off;
        frame.receiveShadows = false;

        // 문구 루트 — 정면 시야 하단에 걸리는 위치, 눈을 향하도록
        var textRoot = new GameObject("TextRoot");
        textRoot.transform.SetParent(root.transform, false);
        textRoot.transform.localPosition = ClawLayout.GuideTextLocal;
        textRoot.transform.localRotation = Quaternion.LookRotation(ClawLayout.GuideTextLocal.normalized, Vector3.up);
        // 7차: 560 폭에서 1행(한글 약 20자)이 자동 줄바꿈돼 2행과 겹쳤음(플레이테스트) → 폭 760·줄바꿈 끔·행 간격 확대
        var canvasGo = CreateWorldCanvas("GuideCanvas", textRoot.transform, new Vector2(760, 230));
        CreatePanelBackground(canvasGo.transform, new Color(0.06f, 0.1f, 0.16f, 0.85f));
        var line1 = CreateText(canvasGo.transform, "Line1", "Beam Pro를 가로로 눕혀 가슴 앞에 들고", font, 34, Color.white, new Vector2(0f, 70f), new Vector2(720f, 54f));
        var line2 = CreateText(canvasGo.transform, "Line2", "↓ 아래 틀에 맞춘 뒤 [잡기]", font, 34, new Color(0.4f, 0.9f, 1f), new Vector2(0f, 15f), new Vector2(720f, 54f));
        line1.textWrappingMode = TextWrappingModes.NoWrap;
        line2.textWrappingMode = TextWrappingModes.NoWrap;
        CreateText(canvasGo.transform, "Arrow", "↓", font, 72, new Color(0.3f, 0.85f, 0.9f), new Vector2(0f, -60f), new Vector2(200f, 90f));

        var ctrl = root.AddComponent<ClawGuidePanel>();
        SetRef(ctrl, "visualRoot", visual);
        SetRef(ctrl, "textRoot", textRoot);
        SetRef(ctrl, "frame", frame);
        var start = BuildStartPrompt(boothRoot, font); // 10-13 연출안 STEP 05 — 디졸브로 기계가 드러난 뒤의 [START](부스 고정)
        SetRef(ctrl, "startRoot", start.root);
        SetRef(ctrl, "startHex", start.hex);
        visual.SetActive(false);
        textRoot.SetActive(false);
    }

    static void BuildClawResultAndReward(Transform boothRoot, TMP_FontAsset font)
    {
        // 결과 패널 (Main의 BuildResultPanel 규격). 5차: 기계 앞 시선 방향(−20°, 0.85m)에 눈을 향해, 기존(2.3m) 각크기 유지 스케일.
        // 보상 확인은 오버레이 [잡기] → 월드 버튼 없음(캡션만)
        var faceEye = Quaternion.LookRotation(ClawLayout.ResultPanelLocal.normalized, Vector3.up);
        var panelScale = Vector3.one * ClawLayout.ResultPanelScale;
        var result = new GameObject("ResultPanel");
        result.transform.SetParent(boothRoot, false);
        result.transform.localPosition = ClawLayout.ResultPanelLocal;
        result.transform.localRotation = faceEye;
        result.transform.localScale = panelScale;

        var canvas = CreateWorldCanvas("ResultCanvas", result.transform, new Vector2(900, 600));
        CreatePanelBackground(canvas.transform, new Color(0.06f, 0.1f, 0.16f, 0.92f));
        var title = CreateText(canvas.transform, "Title", "성공!", font, 84, new Color(0.4f, 0.9f, 1f), new Vector2(0, 180), new Vector2(860, 130));
        var detail = CreateText(canvas.transform, "Detail", "", font, 52, Color.white, new Vector2(0, 40), new Vector2(860, 150));
        var hint = CreateText(canvas.transform, "Hint", "", font, 40, new Color(1f, 0.85f, 0.4f), new Vector2(0f, -100f), new Vector2(860f, 110f));
        var bg = CreateBar(canvas.transform, "RewardButtonBg", new Color(0.15f, 0.55f, 0.75f, 0.95f), new Vector2(0f, -215f), new Vector2(520f, 90f));
        CreateText(bg.transform, "Label", "[잡기] = 보상 확인", font, 46, Color.white, Vector2.zero, new Vector2(500f, 80f));
        // 10-26 보상 대상이 아니면(시간 초과) 같은 자리에 [다시 시작] 안내 막대(Beam Pro 잡기 = 다시 시작)
        var rbg = CreateBar(canvas.transform, "RestartBg", new Color(0.22f, 0.3f, 0.42f, 0.95f), new Vector2(0f, -215f), new Vector2(560f, 90f));
        CreateText(rbg.transform, "Label", "[다시 시작] = 처음 화면", font, 46, Color.white, Vector2.zero, new Vector2(540f, 80f));
        rbg.gameObject.SetActive(false);

        var rc = result.AddComponent<ClawResultPanel>();
        SetRef(rc, "panelRoot", canvas);
        SetRef(rc, "titleText", title);
        SetRef(rc, "detailText", detail);
        SetRef(rc, "hintText", hint);
        SetRef(rc, "rewardButtonVisual", bg.gameObject); // rewardButton(인터랙터블)은 미배선 — 컨트롤러가 null 가드
        SetRef(rc, "restartVisual", rbg.gameObject);
        canvas.SetActive(false);

        // 보상 패널(SCR-06)
        var reward = new GameObject("RewardPanel");
        reward.transform.SetParent(boothRoot, false);
        reward.transform.localPosition = ClawLayout.ResultPanelLocal;
        reward.transform.localRotation = faceEye;
        reward.transform.localScale = panelScale;
        var rcanvas = CreateWorldCanvas("RewardCanvas", reward.transform, new Vector2(900, 600));
        CreatePanelBackground(rcanvas.transform, new Color(0.06f, 0.1f, 0.16f, 0.92f));
        var rtitle = CreateText(rcanvas.transform, "Title", "보상 안내", font, 84, new Color(0.4f, 0.9f, 1f), new Vector2(0, 190), new Vector2(860, 130));
        var rtext = CreateText(rcanvas.transform, "Reward", "", font, 52, new Color(1f, 0.85f, 0.4f), new Vector2(0, 30), new Vector2(860, 170));
        var rguide = CreateText(rcanvas.transform, "Guide", "", font, 42, Color.white, new Vector2(0, -170), new Vector2(860, 160));
        var rp = reward.AddComponent<ClawRewardPanel>();
        SetRef(rp, "panelRoot", rcanvas);
        SetRef(rp, "titleText", rtitle);
        SetRef(rp, "rewardText", rtext);
        SetRef(rp, "guideText", rguide);
        AddVipRewardCard(reward, rcanvas, rp); // 10-12 보상 카드 Scale·Fade + 효과음
        AddRewardPrizeCube(reward, rp);        // 10-28 상품 소개 = 키링 큐브 3D 회전
        rcanvas.SetActive(false);
    }

    /// <summary>
    /// 10-28 보상 패널 위(글자 위쪽·눈 쪽)에 키링 큐브 3D 모델 — 패널과 함께 켜지고 ClawRewardPanel이 천천히 돌린다(prizeModel).
    /// 크기: 메시 바운즈 최대 변(큐브 + 고리·체인) ≈ 월드 10cm. 패널 forward가 눈 반대라 눈 쪽 = −z. 윗면이 보이게 눈 쪽으로 25° 기울임
    /// </summary>
    static void AddRewardPrizeCube(GameObject reward, ClawRewardPanel rp)
    {
        const string MeshPath = "Assets/Settings/ClawItems/Claw_KeyringMesh.asset", MatPath = "Assets/Settings/ClawItems/Claw_Keyring.mat";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mesh == null || mat == null)
        {
            Debug.LogWarning($"[SouvenirHuntClawSetup] 보상 큐브 생략 — {MeshPath} 또는 {MatPath} 없음");
            return;
        }
        var go = new GameObject("PrizeCube") { layer = reward.layer };
        go.transform.SetParent(reward.transform, false);
        var size = mesh.bounds.size;
        float maxDim = Mathf.Max(0.001f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
        float local = 0.10f / maxDim / ClawLayout.ResultPanelScale; // 월드 ≈10cm
        // 캔버스 600px(로컬 0.6) 위 끝 0.30보다 위, 눈 쪽으로 조금
        go.transform.localPosition = new Vector3(0f, 0.30f + 0.10f, -0.08f);
        go.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
        go.transform.localScale = Vector3.one * local;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        SetRef(rp, "prizeModel", go.transform);
        go.SetActive(false); // Show가 패널과 함께 켬
    }

    /// <summary>종료/취소 다이얼로그(5차: 기계 앞·결과창 앞, 눈을 향해, 기존 각크기 유지 스케일). 조이스틱 좌우 선택·잡기 확정(ClawGameManager가 구동). 기호는 ← → 만(폰트 베이크 목록).</summary>
    static ClawExitDialog BuildClawExitDialog(Transform boothRoot, TMP_FontAsset font)
    {
        var root = new GameObject("ExitDialog");
        root.transform.SetParent(boothRoot, false);
        root.transform.localPosition = ClawLayout.ExitDialogLocal;
        root.transform.localRotation = Quaternion.LookRotation(ClawLayout.ExitDialogLocal.normalized, Vector3.up);
        root.transform.localScale = Vector3.one * ClawLayout.ExitDialogScale;

        var canvas = CreateWorldCanvas("ExitCanvas", root.transform, new Vector2(700, 320));
        CreatePanelBackground(canvas.transform, new Color(0.06f, 0.1f, 0.16f, 0.95f));
        CreateText(canvas.transform, "Title", "앱을 종료할까요?", font, 52, Color.white, new Vector2(0f, 95f), new Vector2(660f, 80f));
        var normal = new Color(0.10f, 0.14f, 0.20f, 0.95f);
        var quitCard = CreateBar(canvas.transform, "QuitCard", normal, new Vector2(-165f, -10f), new Vector2(300f, 120f));
        CreateText(quitCard.transform, "Label", "종료", font, 60, Color.white, Vector2.zero, new Vector2(280f, 110f));
        var cancelCard = CreateBar(canvas.transform, "CancelCard", new Color(0.15f, 0.55f, 0.75f, 1f), new Vector2(165f, -10f), new Vector2(300f, 120f));
        CreateText(cancelCard.transform, "Label", "취소", font, 60, Color.white, Vector2.zero, new Vector2(280f, 110f));
        var hint = CreateText(canvas.transform, "Hint", "← 종료 · 취소 →   [잡기] 확인 · 10초 후 자동 취소", font, 30, new Color(1f, 0.85f, 0.4f), new Vector2(0f, -115f), new Vector2(660f, 60f));

        var dlg = root.AddComponent<ClawExitDialog>();
        SetRef(dlg, "panelRoot", canvas);
        SetRef(dlg, "quitCard", quitCard);
        SetRef(dlg, "cancelCard", cancelCard);
        SetRef(dlg, "hintText", hint);
        canvas.SetActive(false);
        return dlg;
    }

    // ─ Beam Pro 화면 오버레이(메뉴 14가 호출) ────────────────────────

    /// <summary>
    /// SouvenirVirtualController 프리팹 Canvas 안에 가로용 오버레이를 만든다. 상단 절반=조이스틱, 하단 절반=잡기.
    /// 기본 비활성(ClawPadPresenter가 상태별로 켬). 전부 layer 5(UI) — URPUICamera 컬링 마스크. 중첩 Canvas 금지.
    /// 활성 앵커(ClawPadAnchor)의 ClawPadRegistrar가 루트를 ClawPadInput에 등록한다.
    /// </summary>
    static void BuildClawPad(Transform canvas, TMP_FontAsset font)
    {
        foreach (var n in new[] { "ClawPadRoot", "ClawPadAnchor", "WearNotice" })
        {
            var old = canvas.Find(n);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        var knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var labelRot = Quaternion.Euler(0f, 0f, -90f); // 왼쪽 90° 눕힘 기준 정립 방향

        var root = NewRect("ClawPadRoot", canvas);
        StretchFull((RectTransform)root.transform);
        root.AddComponent<ClawPadRoot>();
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.04f, 0.06f, 1f);
        bg.raycastTarget = true; // 아래 Trigger/App/Home 차단

        // 조이스틱(상단 절반)
        var pad = NewRect("JoystickPad", root.transform);
        var prt = (RectTransform)pad.transform;
        prt.anchorMin = new Vector2(0f, 0.5f); prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        var padImg = pad.AddComponent<Image>();
        padImg.color = new Color(0.06f, 0.08f, 0.11f, 1f);
        padImg.raycastTarget = true;
        var joystick = pad.AddComponent<ClawJoystickPad>();

        var ring = NewRect("Ring", pad.transform);
        var ringRt = (RectTransform)ring.transform;
        ringRt.sizeDelta = new Vector2(680f, 680f);
        var ringImg = ring.AddComponent<Image>();
        ringImg.sprite = knobSprite;
        ringImg.color = new Color(0.3f, 0.85f, 0.9f, 0.14f);
        ringImg.raycastTarget = false;

        var knob = NewRect("Knob", pad.transform);
        var knobRt = (RectTransform)knob.transform;
        knobRt.sizeDelta = new Vector2(160f, 160f);
        var knobImg = knob.AddComponent<Image>();
        knobImg.sprite = knobSprite;
        knobImg.color = new Color(0.3f, 0.85f, 0.9f, 0.95f);
        knobImg.raycastTarget = false;

        var padLabel = CreateText(pad.transform, "Label", "조이스틱", font, 60, new Color(1f, 1f, 1f, 0.45f), new Vector2(440f, 0f), new Vector2(900f, 90f));
        padLabel.rectTransform.localRotation = labelRot;
        padLabel.raycastTarget = false;
        SetRef(joystick, "ring", ringRt);
        SetRef(joystick, "knob", knobRt);

        // 잡기(하단 절반)
        var grab = NewRect("GrabButton", root.transform);
        var grt = (RectTransform)grab.transform;
        grt.anchorMin = Vector2.zero; grt.anchorMax = new Vector2(1f, 0.5f);
        grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
        var grabImg = grab.AddComponent<Image>();
        grabImg.color = new Color(0.09f, 0.06f, 0.07f, 1f);
        grabImg.raycastTarget = true;
        var grabBtn = grab.AddComponent<ClawGrabButton>();

        var face = NewRect("Face", grab.transform);
        ((RectTransform)face.transform).sizeDelta = new Vector2(760f, 760f);
        var faceImg = face.AddComponent<Image>();
        faceImg.sprite = knobSprite;
        faceImg.color = new Color(0.55f, 0.16f, 0.18f, 1f);
        faceImg.raycastTarget = false;

        var grabLabel = CreateText(grab.transform, "Label", "잡기", font, 170, Color.white, Vector2.zero, new Vector2(700f, 400f));
        grabLabel.rectTransform.localRotation = labelRot;
        grabLabel.raycastTarget = false;
        SetRef(grabBtn, "face", faceImg);

        // 홈 버튼(3차): 세로 우상단 코너 = 눕히면 가로 좌상단(조이스틱 위쪽). 패드 중심에서 434px > 노브 최대 380px → 간섭 없음.
        // 조이스틱·잡기 뒤 형제 = 레이캐스트 우선. 종료/취소 다이얼로그를 연다(SDK Home은 오버레이가 덮어 도달 불가).
        var home = NewRect("HomeButton", root.transform);
        var hrt = (RectTransform)home.transform;
        hrt.anchorMin = hrt.anchorMax = Vector2.one;
        hrt.pivot = Vector2.one;
        hrt.anchoredPosition = new Vector2(-36f, -36f);
        hrt.sizeDelta = new Vector2(220f, 220f);
        var homeImg = home.AddComponent<Image>();
        homeImg.sprite = knobSprite;
        homeImg.color = new Color(0.10f, 0.14f, 0.20f, 1f);
        homeImg.raycastTarget = true;
        var homeBtn = home.AddComponent<ClawHomeButton>();
        var homeLabel = CreateText(home.transform, "Label", "홈", font, 64, new Color(1f, 1f, 1f, 0.9f), new Vector2(18f, 0f), new Vector2(200f, 120f));
        homeLabel.rectTransform.localRotation = labelRot;
        homeLabel.raycastTarget = false;
        var homeSub = CreateText(home.transform, "SubLabel", "길게: 정면", font, 26, new Color(1f, 1f, 1f, 0.55f), new Vector2(-48f, 0f), new Vector2(200f, 40f));
        homeSub.rectTransform.localRotation = labelRot; // 눕힌 기준 "홈" 아래 줄
        homeSub.raycastTarget = false;
        SetRef(homeBtn, "face", homeImg);

        SetLayerRecursively(root, 5);
        root.SetActive(false);

        // 10-26 안경 벗음 안내(ClawWearWatch — "n초 뒤 처음 화면으로"): 패드 바로 위 조이스틱 쪽 절반(잡기 버튼은 보이게 — 인증 대기의 [인증 시작]),
        // 가로 눕힘 글자. 터치는 막지 않음(진행자가 아래 패드를 쓸 수 있게)
        var notice = NewRect("WearNotice", canvas);
        notice.transform.SetSiblingIndex(root.transform.GetSiblingIndex() + 1);
        var nrt = (RectTransform)notice.transform;
        nrt.anchorMin = new Vector2(0f, 0.5f); nrt.anchorMax = Vector2.one;
        nrt.offsetMin = Vector2.zero; nrt.offsetMax = Vector2.zero;
        var noticeBg = notice.AddComponent<Image>();
        noticeBg.color = new Color(0.02f, 0.03f, 0.05f, 0.9f);
        noticeBg.raycastTarget = false;
        var noticeText = CreateText(notice.transform, "Text", "", font, 72, Color.white, Vector2.zero, new Vector2(1100f, 900f));
        noticeText.rectTransform.localRotation = labelRot;
        noticeText.raycastTarget = false;
        SetLayerRecursively(notice, 5);
        notice.SetActive(false);

        var anchor = NewRect("ClawPadAnchor", canvas);
        ((RectTransform)anchor.transform).sizeDelta = Vector2.zero;
        anchor.layer = 5;
        var reg = anchor.AddComponent<ClawPadRegistrar>();
        SetRef(reg, "padRoot", root);
        SetRef(reg, "grabLabel", grabLabel);
        SetRef(reg, "stickLabel", padLabel);
        SetRef(reg, "notice", notice);
        SetRef(reg, "noticeText", noticeText);
    }

    // ─ 공용 헬퍼 ─────────────────────────────────────────────────

    static void SetLayerRecursively(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    static void SetRefArray(Component comp, string field, UnityEngine.Object[] values)
    {
        var so = new SerializedObject(comp);
        var prop = so.FindProperty(field);
        if (prop == null || !prop.isArray)
        {
            Debug.LogError($"[SouvenirHuntSetup] {comp.GetType().Name}.{field} 배열 필드 없음 — 배선 실패");
            return;
        }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>씬의 XREALSessionManager.m_RecenterAction을 그대로 복사(Phase5 메뉴 20과 동일 방식, XREAL 어셈블리 직접 참조 회피).</summary>
    static UnityEngine.InputSystem.InputActionReference FindRecenterActionReference()
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null || mb.GetType().Name != "XREALSessionManager") continue;
            var sp = new SerializedObject(mb).FindProperty("m_RecenterAction");
            return sp != null ? sp.objectReferenceValue as UnityEngine.InputSystem.InputActionReference : null;
        }
        return null;
    }
}
