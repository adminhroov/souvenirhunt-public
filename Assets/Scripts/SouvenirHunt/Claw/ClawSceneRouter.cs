using UnityEngine;
using UnityEngine.SceneManagement;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 씬 전환. 10-15(미션형 복원, 모드1·2 삭제): 부팅 = ClawIntro(VIP 인증) → Claw(게임) → 운영자 리셋이면 ClawIntro로 복귀.
    /// 인증 씬에서 넘어온 게임 씬은 Ready에서 멈추지 않고 바로 시작(영상 → 전환 → START)한다 — AutoStart 플래그(정적, 씬 간 전달).
    /// 게임 씬을 단독으로 재생(셀프테스트·에디터)하면 플래그가 없어 기존처럼 Ready에서 [잡기]를 기다린다.
    /// 빌드에 없는 씬은 로드하지 않고 false(앱은 계속).
    /// </summary>
    public static class ClawSceneRouter
    {
        public const string IntroScenePath = "Assets/Scenes/ClawIntro.unity";
        public const string GameScenePath = "Assets/Scenes/Claw.unity";

        static bool autoStart;
        public static bool Loading { get; private set; }

        /// <summary>빌드 설정(enabled)에 들어 있는 씬인가 — 에디터 재생도 같은 목록을 쓴다</summary>
        public static bool InBuild(string path) => SceneUtility.GetBuildIndexByScenePath(path) >= 0;

        public static bool HasIntro => InBuild(IntroScenePath);

        /// <summary>게임 씬으로 — 도착하면 Ready 없이 바로 시작</summary>
        public static bool LoadGame()
        {
            if (!InBuild(GameScenePath))
            {
                Debug.LogWarning($"[ClawRouter] 게임 씬({GameScenePath})이 빌드에 없음 — 이동 안 함");
                return false;
            }
            autoStart = true;
            Load(GameScenePath);
            Debug.Log($"[Cycle] GAME → 인형뽑기 ({GameScenePath})");
            return true;
        }

        /// <summary>인증 씬으로(운영자 리셋 = 체험자 교체)</summary>
        public static bool LoadIntro()
        {
            if (!HasIntro)
            {
                Debug.LogWarning($"[ClawRouter] 인증 씬({IntroScenePath})이 빌드에 없음 — 현재 씬에서 리셋");
                return false;
            }
            autoStart = false;
            Load(IntroScenePath);
            Debug.Log("[Cycle] INTRO — 인증 화면으로");
            return true;
        }

        /// <summary>게임 씬 Start에서 1회 — 인증 씬에서 왔으면 true</summary>
        public static bool ConsumeAutoStart()
        {
            bool a = autoStart;
            autoStart = false;
            return a;
        }

        static string loadingPath;

        static void Load(string path)
        {
            // 10-26 같은 씬을 불러오는 중에 또 요청([다시 시작]과 30초 자동 처음으로가 겹침 등) → 한 번만
            if (Loading && loadingPath == path) { Debug.Log($"[ClawRouter] 이미 {path} 불러오는 중 — 다시 안 함"); return; }
            Loading = true;
            loadingPath = path;
            ClawPadInput.Clear();
            var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
            if (op == null) { Loading = false; loadingPath = null; return; }
            op.completed += _ => { Loading = false; loadingPath = null; };
        }
    }
}
