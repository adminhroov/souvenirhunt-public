using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;
using SouvenirHunt;

// 인형뽑기 인증 씬(ClawIntro, 부팅 씬) 자동 구성 + 인형뽑기 빌드 씬 구성.
// 메뉴 34: ClawIntro.unity 신규 생성(파괴적) — MR VIP 인증 연출(VipIntro, SouvenirHuntVipSetup.cs, 리소스 Assets/VipIntro)만 두고,
//          끝나면 바로 Claw 씬. 10-15 미션형 복원으로 모드 선택 카드(ClawSelect)는 없앴다.
// 빌드 구성: ClawIntro(부팅)·Claw enabled, Main은 disabled(메뉴 31로 Main 단독 전환).
public static partial class SouvenirHuntSetup
{
    /// <summary>상태 문구(불러오는 중·경고) 루트(눈 원점 기준): 1.0m 앞, 시선 −8°. 캔버스 1000px × 0.42 = 0.42m</summary>
    const float IntroStatusDistance = 1.0f, IntroStatusPitchDeg = -8f, IntroStatusScale = 0.42f;

    [MenuItem("XREAL/34. Setup ClawIntro Scene (인형뽑기 인증 씬 — 파괴적)")]
    public static void SetupClawIntroScene()
    {
        try
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = ClawSceneRouter.IntroScenePath;
            if (!Application.isBatchMode && !Quiet && File.Exists(path) &&
                !EditorUtility.DisplayDialog("⚠ ClawIntro.unity 재생성",
                    "인형뽑기 인증 씬을 빈 씬에서 다시 만듭니다. 이후 19(폰트)·14(운영자 패널)를 재실행하세요.",
                    "계속(재생성)", "취소"))
                return;

            var report = new StringBuilder("[SouvenirHuntSetup] ClawIntro 씬 구성:\n");
            var font = GetOrCreateKoreanFont();
            EnsureVipAssets(report); // VIP 인증 리소스(없을 때만 임시본) — NewScene 뒤 BuildVipIntro가 다시 불러온다
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.xreal.xr/Runtime/Prefabs/XR Interaction Setup.prefab");
            if (rigAsset == null) throw new Exception("XR Interaction Setup.prefab 없음 — XREAL SDK 확인");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
            rig.AddComponent<XRRigDedup>(); // 10-15: 씬 전환 때 이전 씬 리그(SDK가 DontDestroyOnLoad)를 지워 카메라 중복 렌더 방지
            var origin = rig.GetComponentInChildren<XROrigin>();
            if (origin != null) origin.CameraYOffset = 0f;

            var suppressor = new GameObject("InteractorSuppressor").AddComponent<InteractorSuppressor>();

            // 상태 문구(인증 뒤 "불러오는 중"·게임 씬 없음 경고) — 눈 원점, 시선 yaw 추종
            var introGo = new GameObject("ClawIntro");
            var followRoot = new GameObject("FollowRoot").transform;
            followRoot.SetParent(introGo.transform, false);
            var panelRoot = new GameObject("StatusPanel").transform;
            panelRoot.SetParent(followRoot, false);
            var panelLocal = Quaternion.Euler(-IntroStatusPitchDeg, 0f, 0f) * Vector3.forward * IntroStatusDistance;
            panelRoot.localPosition = panelLocal;
            panelRoot.localRotation = Quaternion.LookRotation(panelLocal.normalized, Vector3.up);
            panelRoot.localScale = Vector3.one * IntroStatusScale;
            var canvas = CreateWorldCanvas("StatusCanvas", panelRoot, new Vector2(1000f, 120f));
            var status = CreateText(canvas.transform, "Status", "", font, 44, new Color(1f, 0.85f, 0.4f), Vector2.zero, new Vector2(960f, 100f));
            status.gameObject.SetActive(false);

            var ci = introGo.AddComponent<ClawIntro>();
            introGo.AddComponent<ClawKeyboardFallback>();
            new GameObject("ClawIntroSelfTest").AddComponent<ClawIntroSelfTest>(); // 따로 — DontDestroyOnLoad가 인증 화면을 끌고 가지 않게
            SetRef(ci, "followRoot", followRoot);
            SetRef(ci, "statusText", status);
            SetRef(ci, "suppressor", suppressor);
            var intro = BuildVipIntro(font, report); // 체험자마다 맨 처음, 운영자 리셋도 여기부터
            SetRef(ci, "intro", intro);

            new GameObject("PerfMonitor").AddComponent<PerfMonitor>();
            new GameObject("RenderQualityApplier").AddComponent<RenderQualityApplier>();
            new GameObject("RuntimeExceptionCounter").AddComponent<RuntimeExceptionCounter>();

            EditorSceneManager.SaveScene(scene, path);
            SetClawBuildScenes();
            report.AppendLine("  · VIP 인증(카드키 → 홍채·시선 → ACCESS GRANTED) → 바로 Claw 씬 · 상태 문구 1.0m 앞 시선 yaw 추종");
            report.AppendLine($"  · 저장 {path} — 인형뽑기 빌드 구성(부팅 {BootScenePath})");
            report.AppendLine("다음: 19 → 14 (폰트·운영자 패널)");
            Debug.Log(report.ToString());
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup", report.ToString(), "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] SetupClawIntroScene FAILED: " + e);
        }
    }

    /// <summary>
    /// 인형뽑기 빌드 구성: 있는 것만 ClawIntro(부팅, index 0) → Claw 순으로 enabled, Main 등 나머지는 disabled 보존.
    /// ClawIntro가 아직 없으면 Claw가 부팅(메뉴 30만 실행한 상태). 지운 씬(ClawSelect·ClawGate) 항목은 목록에서 뺀다.
    /// </summary>
    public static void SetClawBuildScenes()
    {
        var list = new List<EditorBuildSettingsScene>();
        foreach (var p in new[] { ClawSceneRouter.IntroScenePath, ClawSceneRouter.GameScenePath })
            if (File.Exists(p)) list.Add(new EditorBuildSettingsScene(p, true));
        foreach (var s in EditorBuildSettings.scenes)
            if (!string.IsNullOrEmpty(s.path) && File.Exists(s.path) && !list.Exists(x => x.path == s.path))
                list.Add(new EditorBuildSettingsScene(s.path, false));
        if (File.Exists(ScenePath) && !list.Exists(x => x.path == ScenePath))
            list.Add(new EditorBuildSettingsScene(ScenePath, false));
        EditorBuildSettings.scenes = list.ToArray();
    }

    /// <summary>현재 부팅 씬이 인형뽑기 계열(인증·게임)인가</summary>
    static bool IsClawScene(string path) =>
        path == ClawSceneRouter.IntroScenePath || path == ClawSceneRouter.GameScenePath;
}
