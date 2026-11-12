using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// 열린 에디터에서 셋업 메뉴를 원격(마커 파일)으로 실행하는 부트스트랩.
// Temp/souvenirhunt_autorun.txt 에 "17,19,18" 처럼 메뉴 번호를 적어두면 2초 내(에디터가 유휴 상태일 때) 순서대로 실행 후 마커 삭제.
// 배치모드는 에디터가 열려 있으면 잠금 때문에 불가 → 이 경로로 대체. 결과는 Editor.log([SouvenirHuntSetup])에 남는다.
[InitializeOnLoad]
static class SouvenirHuntAutoRun
{
    static readonly string Marker = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "souvenirhunt_autorun.txt"));

    static double nextPoll;

    static SouvenirHuntAutoRun()
    {
        // 도메인 리로드 직후 1회 + 이후 2초 간격 폴링 (스크립트 변경 없이도 마커만 쓰면 실행되도록)
        if (File.Exists(Marker)) EditorApplication.delayCall += Run;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2.0;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying) return;
        if (File.Exists(Marker)) Run();
    }

    /// <summary>
    /// 에디터가 닫혀 있을 때의 헤드리스 경로 (마커 폴링은 GUI 전용).
    /// 사용: Unity.exe -batchmode -quit -projectPath &lt;경로&gt; -executeMethod SouvenirHuntAutoRun.BatchRun -souvenirSteps 13,14,17,19,18,20,21
    /// (단계 순서는 SouvenirHuntSetup.RebuildOrder 와 같아야 한다 — 14 운영자 패널을 빠뜨리면 SCR-07 이 사라진다)
    ///
    /// 종료 코드: 0 정상 / 1 단계 실패 또는 알 수 없는 단계 / 2 인자 누락.
    /// 각 셋업 메서드는 예외를 스스로 삼켜 Debug.LogError 로만 남기므로, 로그 콜백으로 에러를 세지 않으면
    /// 깨진 씬을 만들고도 exit 0 이 나온다 (CI·래퍼 스크립트가 성공으로 오인).
    /// </summary>
    public static void BatchRun()
    {
        string steps = null;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-souvenirSteps") { steps = args[i + 1]; break; }

        if (string.IsNullOrWhiteSpace(steps))
        {
            Debug.LogError("[AutoRun] -souvenirSteps 인자가 없습니다 (예: -souvenirSteps 13,14,17,19,18,20,21)");
            EditorApplication.Exit(2);
            return;
        }

        int errors = 0;
        Application.LogCallback counter = (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        };
        Application.logMessageReceived += counter;
        bool unknownStep;
        try { unknownStep = RunSteps(steps); }
        finally { Application.logMessageReceived -= counter; }

        if (errors > 0 || unknownStep)
        {
            Debug.LogWarning($"[AutoRun] 실패 — 에러 {errors}건" + (unknownStep ? ", 알 수 없는 단계 포함" : ""));
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("[AutoRun] 배치 성공 (에러 0건)");
    }

    static void Run()
    {
        if (!File.Exists(Marker)) return; // delayCall·Poll 이중 호출 방어
        string steps;
        try { steps = File.ReadAllText(Marker).Trim(); File.Delete(Marker); }
        catch (Exception e) { Debug.LogError("[AutoRun] 마커 읽기 실패: " + e.Message); return; }

        RunSteps(steps);
    }

    /// <summary>
    /// 단계 토큰: 숫자 = 메뉴 번호. @main/@claw = 이후 17/18의 대상 씬(SouvenirHuntSetup.TargetScenePath).
    /// @selftest = Claw 씬을 열고 재생 진입(ClawSelfTest가 플로우·물리 자동 검증 후 재생 종료) — 반드시 마지막 토큰.
    /// @introtest = ClawIntro(인증) 씬을 열고 재생 진입(ClawIntroSelfTest) — 반드시 마지막 토큰.
    /// @quit = 열린 씬·에셋 저장 후 에디터 종료 — 반드시 마지막 토큰(예: 11,@quit = 빌드 후 저장·종료).
    /// 인형뽑기 전체 재구성 예: 30,@claw,17,34,19,@main,14,11b (10-29 감사 I2: 소스 기본값 B — 11(A 패키지)은 BuildApk가 거부)
    /// </summary>
    /// <returns>알 수 없는 단계가 하나라도 있었으면 true (오타로 단계가 조용히 건너뛰어지는 것 방지).</returns>
    static bool RunSteps(string steps)
    {
        bool unknownStep = false;
        Debug.Log("[AutoRun] 실행: " + steps);
        SouvenirHuntSetup.Quiet = true;
        try
        {
            foreach (var raw in steps.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = DateTime.Now;
                switch (raw.Trim())
                {
                    case "0":  SouvenirHuntSetup.ApplyPlayerSettings(); break;
                    case "13": SouvenirHuntSetup.SetupEnvironment(); break;
                    case "14": SouvenirHuntSetup.SetupOperatorPanel(); break;
                    case "17": SouvenirHuntSetup.SetupLighting(); break;
                    case "18": SouvenirHuntSetup.BakeLightmaps(); break;
                    case "19": SouvenirHuntSetup.BakeKoreanFont(); break;
                    case "20": SouvenirHuntSetup.SetupPhase5(); break;
                    case "21": SouvenirHuntSetup.SetupReward(); break;
                    case "30": SouvenirHuntSetup.SetupClawScene(); break;
                    case "31": SouvenirHuntSetup.ToggleBootScene(); break;
                    case "32": SouvenirHuntSetup.ClawStressTest(); break;
                    case "11": SouvenirHuntSetup.BuildApk(); break;
                    case "11b": SouvenirHuntSetup.BuildApkB(); break; // 10-29 버전 B 별도 패키지(com.hroov.souvenirhunt.b) — 빌드 뒤 A 설정 복원
                    case "33": SouvenirHuntSetup.SetupImportedModels(); break;
                    case "34": SouvenirHuntSetup.SetupClawIntroScene(); break;
                    case "36": SouvenirHuntMaintenance.ApplyDirectStart(); break;
                    case "@main": SouvenirHuntSetup.TargetScenePath = "Assets/Scenes/Main.unity"; break;
                    case "@claw": SouvenirHuntSetup.TargetScenePath = SouvenirHuntSetup.ClawScenePath; break;
                    case "@selftest":
                        if (!StartSceneSelfTest(SouvenirHuntSetup.ClawScenePath, SouvenirHunt.ClawSelfTest.MarkerPath)) unknownStep = true;
                        Debug.Log("[AutoRun] @selftest — 재생 진입, 이후 토큰은 무시");
                        return unknownStep;
                    case "@quit":
                        // 원격 마무리(10-15): 열린 씬·에셋을 저장하고 에디터 종료(저장 확인 창 없이) — 반드시 마지막 토큰. 마커는 이미 지워져 다음 실행에 남지 않는다
                        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                        AssetDatabase.SaveAssets();
                        Debug.Log("[AutoRun] @quit — 씬·에셋 저장 완료, 에디터 종료");
                        EditorApplication.delayCall += () => EditorApplication.Exit(0);
                        return unknownStep;
                    case "@introtest":
                        if (!StartSceneSelfTest(SouvenirHunt.ClawSceneRouter.IntroScenePath, SouvenirHunt.ClawIntroSelfTest.MarkerPath)) unknownStep = true;
                        Debug.Log("[AutoRun] @introtest — 재생 진입, 이후 토큰은 무시");
                        return unknownStep;
                    default: Debug.LogWarning("[AutoRun] 알 수 없는 단계: " + raw); unknownStep = true; continue;
                }
                Debug.Log($"[AutoRun] 단계 {raw} 완료 ({(DateTime.Now - t).TotalSeconds:F0}s)");
            }
        }
        finally
        {
            SouvenirHuntSetup.Quiet = false;
            Debug.Log("[AutoRun] 전체 완료");
        }
        return unknownStep;
    }

    /// <summary>씬을 열고 셀프테스트 마커를 쓴 뒤 재생 진입. 결과는 Editor.log의 "[ClawSelfTest]/[IntroSelfTest] RESULT".</summary>
    static bool StartSceneSelfTest(string scenePath, string markerPath)
    {
        if (!File.Exists(scenePath))
        {
            Debug.LogError($"[AutoRun] {scenePath} 없음 — 먼저 셋업 메뉴(30/34)");
            return false;
        }
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        File.WriteAllText(markerPath, DateTime.Now.ToString("s"));
        EditorApplication.EnterPlaymode();
        return true;
    }
}
