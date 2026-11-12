using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using SouvenirHunt;

// Phase 5 — 예외처리 · 오디오 훅 · 운영 리허설 (인수인계 §5 Phase 5, §6) + 보상 UI(기획서 §03/§07, 08-27).
// SouvenirHuntSetup partial 확장. 메뉴 20·21 모두 멱등(재실행 안전).
public static partial class SouvenirHuntSetup
{
    // ─ 공용: "같은 이름의 싱글턴 오브젝트를 지우고 새로 만든다" (Phase 4/5 공통 패턴) ─────────
    static T ReplaceSingleton<T>(string name, Transform parent = null) where T : Component
    {
        var old = GameObject.Find(name);
        if (old != null) UnityEngine.Object.DestroyImmediate(old);
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }

    // ─ 20. Phase 5 배선 ───────────────────────────────────────────

    [MenuItem("XREAL/20. Setup Phase 5 (예외처리·오디오·리허설)")]
    public static void SetupPhase5()
    {
        try
        {
            if (!EnsureMainSceneOpen(out var scene)) return;
            var report = new System.Text.StringBuilder("[SouvenirHuntSetup] Phase 5 배선:\n");

            // 0) 렌더 품질 A/B 적용기
            ReplaceSingleton<RenderQualityApplier>("RenderQualityApplier");
            report.AppendLine("  · RenderQualityApplier (config bloom / renderScale / hdr → 부팅·리셋마다 적용)");

            // 1) 런타임 예외 카운터
            ReplaceSingleton<RuntimeExceptionCounter>("RuntimeExceptionCounter");
            report.AppendLine("  · RuntimeExceptionCounter (예외/에러 집계 → 운영자 패널 배지)");

            // 1b) 오디오 훅 (Phase 5-2, 1차 무음 — 슬롯 비움)
            ReplaceSingleton<GameAudio>("GameAudio"); // AudioSource 2개는 Awake에서 자동 생성
            report.AppendLine("  · GameAudio (이벤트 훅 배선, 클립 슬롯 비움 = 1차 무음)");

            // 2) 리센터 감지 → 부스 재정렬 (XREAL 리센터 InputAction 재사용)
            var booth = UnityEngine.Object.FindAnyObjectByType<BoothAligner>();
            var rw = ReplaceSingleton<RecenterWatcher>("RecenterWatcher", booth != null ? booth.transform : null);
            if (booth != null) SetRef(rw, "booth", booth);
            // SDK가 실제로 쓰는 리센터 참조를 그대로 복사 — XREALSessionManager.m_RecenterAction (이름 추측 안 함).
            // 컴포넌트는 리그 프리팹 인스턴스 안이라 타입명으로 찾고 SerializedObject로 읽는다(XREAL 어셈블리 직접 참조 회피).
            InputActionReference recenterRef = null;
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != "XREALSessionManager") continue;
                var sp = new SerializedObject(mb).FindProperty("m_RecenterAction");
                if (sp != null) recenterRef = sp.objectReferenceValue as InputActionReference;
                break;
            }
            if (recenterRef != null) { SetRef(rw, "recenterAction", recenterRef); report.AppendLine($"  · RecenterWatcher (recenterAction={recenterRef.name})"); }
            else report.AppendLine("  ⚠ RecenterWatcher: 리센터 액션 못 찾음 — 리센터 재정렬 비활성(앱 정상)");

            // 3) HUD 일시정지 안내 텍스트 (멱등 — 있으면 스킵)
            var hud = GameObject.Find("HUD");
            var hudCanvas = hud != null ? hud.transform.Find("HudCanvas") : null;
            if (hud != null && hudCanvas != null)
            {
                var font = GetOrCreateKoreanFont();
                var pausedRoot = hudCanvas.Find("PausedRoot");
                if (pausedRoot == null)
                {
                    var txt = CreateText(hudCanvas, "PausedRoot", "⏸ 일시정지 — 다시 착용하면 이어집니다", font, 46,
                                         new Color(1f, 0.9f, 0.4f), new Vector2(0f, -60f), new Vector2(960f, 120f));
                    txt.gameObject.SetActive(false);
                    pausedRoot = txt.transform;
                }
                var hc = hud.GetComponent<HudController>();
                if (hc != null) SetRef(hc, "pausedRoot", pausedRoot.gameObject);
                report.AppendLine("  · HUD 일시정지 안내 배선");
            }
            else report.AppendLine("  ⚠ HUD/HudCanvas 없음 — 일시정지 안내 생략");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup", report.ToString(), "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] SetupPhase5 FAILED: " + e);
        }
    }

    // ─ 21. 보상 UI(SCR-06) + 씬 보정 (08-27 검토 후 추가) ─────────────────────

    [MenuItem("XREAL/21. Setup Reward UI (SCR-06) + 씬 보정")]
    public static void SetupReward()
    {
        try
        {
            if (!EnsureMainSceneOpen(out var scene)) return;
            var report = new System.Text.StringBuilder("[SouvenirHuntSetup] 보상 UI + 씬 보정:\n");
            var font = GetOrCreateKoreanFont();

            var boothRoot = GameObject.Find("BoothRoot");
            if (boothRoot == null)
            {
                Debug.LogError("[SouvenirHuntSetup] BoothRoot 없음 — 먼저 메뉴 10 실행");
                return;
            }

            // 1) Phase 1 임시 DevResetButton 제거 — 체험자 전방에 노출된 라이브 리셋 버튼(리셋은 SCR-07 운영자 패널만)
            var dev = GameObject.Find("DevResetButton");
            if (dev != null) { UnityEngine.Object.DestroyImmediate(dev); report.AppendLine("  · DevResetButton 제거"); }
            else report.AppendLine("  · DevResetButton 없음 (OK)");

            // 2) GameManager.meshLibrary 배선 복구 — 비어 있으면 기념품 10개가 전부 기본 캡슐로만 보인다
            var gm = UnityEngine.Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gm != null)
            {
                var so = new SerializedObject(gm);
                var prop = so.FindProperty("meshLibrary");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    var lib = AssetDatabase.LoadAssetAtPath<MeshLibrary>(LibraryPath) ?? BuildMeshLibrary();
                    SetRef(gm, "meshLibrary", lib);
                    report.AppendLine($"  · meshLibrary 배선 복구 → {LibraryPath} ({lib.Count}항목)");
                }
                else report.AppendLine("  · meshLibrary 배선 OK");
            }
            else report.AppendLine("  ⚠ GameManager 없음 — 메뉴 10 먼저");

            // 3) 결과 패널 [보상 확인] 버튼 + 보상 패널(SCR-06)
            BuildRewardUi(boothRoot.transform, font);
            report.AppendLine("  · ResultPanel: [보상 확인] 버튼(콜라이더+인터랙터블) + 힌트 · RewardPanel(SCR-06) 생성/배선");

            // 4) StreamingAssets config 참조본 동기화 (rewardOnPartial 키 포함)
            WriteStreamingConfig();
            report.AppendLine("  · StreamingAssets/config.txt 동기화");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            report.AppendLine("  ※ 새 한글 문자열이 추가됐으므로 메뉴 19(폰트 베이크)를 재실행할 것");
            Debug.Log(report.ToString());
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup", report.ToString(), "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] SetupReward FAILED: " + e);
        }
    }

    /// <summary>
    /// 결과 패널에 [보상 확인] 버튼(루트 자식 콜라이더+XRSimpleInteractable, 캔버스 안 그래픽)을 붙이고
    /// 보상 패널(SCR-06, 전방 2m)을 만든다. 메뉴 10(새 씬)·21(기존 씬) 공용, 멱등.
    /// 월드 스페이스 클릭은 StartPanel과 같은 방식 — uGUI Button이 아니라 콜라이더를 레이로 쏜다.
    /// </summary>
    static void BuildRewardUi(Transform boothRoot, TMP_FontAsset font)
    {
        // ── ResultPanel: 힌트 텍스트 + 버튼
        var result = boothRoot.Find("ResultPanel");
        if (result == null)
        {
            Debug.LogError("[SouvenirHuntSetup] ResultPanel 없음 — 메뉴 10 먼저");
            return;
        }
        var canvas = result.Find("ResultCanvas");
        if (canvas == null)
        {
            Debug.LogError("[SouvenirHuntSetup] ResultCanvas 없음 — 메뉴 10 먼저");
            return;
        }

        // 구 인라인 보상 텍스트("Reward") → 힌트 1줄로 전환 (보상 문구는 RewardPanel로 이동)
        foreach (var n in new[] { "Reward", "Hint", "RewardButtonBg" })
        {
            var t = canvas.Find(n);
            if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
        var oldBtn = result.Find("RewardButton");
        if (oldBtn != null) UnityEngine.Object.DestroyImmediate(oldBtn.gameObject);

        var hint = CreateText(canvas, "Hint", "", font, 40, new Color(1f, 0.85f, 0.4f),
                              new Vector2(0f, -100f), new Vector2(860f, 110f));

        // 버튼 그래픽(캔버스 px) — 캔버스 1000px = 1m, 원점 중앙 → (0,−215px) = 루트 기준 (0,−0.215m)
        var bg = CreateBar(canvas, "RewardButtonBg", new Color(0.15f, 0.55f, 0.75f, 0.95f),
                           new Vector2(0f, -215f), new Vector2(420f, 90f));
        CreateText(bg.transform, "Label", "보상 확인", font, 50, Color.white, Vector2.zero, new Vector2(400f, 80f));

        // 버튼 히트 영역(루트 자식, 미터 단위) — 캔버스보다 살짝 앞(−z)에 두어 레이가 먼저 맞게
        var btnGo = new GameObject("RewardButton");
        btnGo.transform.SetParent(result, false);
        btnGo.transform.localPosition = new Vector3(0f, -0.215f, -0.01f);
        var box = btnGo.AddComponent<BoxCollider>();
        box.size = new Vector3(0.42f, 0.09f, 0.02f);
        var interactable = btnGo.AddComponent<XRSimpleInteractable>();

        var ctrl = result.GetComponent<ResultPanelController>();
        if (ctrl == null) ctrl = result.gameObject.AddComponent<ResultPanelController>();
        SetRef(ctrl, "hintText", hint);
        SetRef(ctrl, "rewardButton", interactable);
        SetRef(ctrl, "rewardButtonVisual", bg.gameObject);

        // ── RewardPanel (SCR-06): 전방 2m 눈높이, 콜라이더 없음(레이 통과), Reward 상태에서만 표시
        var oldReward = boothRoot.Find("RewardPanel");
        if (oldReward != null) UnityEngine.Object.DestroyImmediate(oldReward.gameObject);

        var root = new GameObject("RewardPanel");
        root.transform.SetParent(boothRoot, false);
        root.transform.localPosition = new Vector3(0f, 0f, 2f);

        var canvasGo = CreateWorldCanvas("RewardCanvas", root.transform, new Vector2(900, 600));
        CreatePanelBackground(canvasGo.transform, new Color(0.06f, 0.1f, 0.16f, 0.92f));
        var title = CreateText(canvasGo.transform, "Title", "보상 안내", font, 84, new Color(0.4f, 0.9f, 1f),
                               new Vector2(0, 190), new Vector2(860, 130));
        var reward = CreateText(canvasGo.transform, "Reward", "", font, 52, new Color(1f, 0.85f, 0.4f),
                                new Vector2(0, 30), new Vector2(860, 170));
        var guide = CreateText(canvasGo.transform, "Guide", "", font, 42, Color.white,
                               new Vector2(0, -170), new Vector2(860, 160));

        var rc = root.AddComponent<RewardPanelController>();
        SetRef(rc, "panelRoot", canvasGo);
        SetRef(rc, "titleText", title);
        SetRef(rc, "rewardText", reward);
        SetRef(rc, "guideText", guide);
        canvasGo.SetActive(false); // Reward 상태 진입 시 컨트롤러가 켠다
    }
}
