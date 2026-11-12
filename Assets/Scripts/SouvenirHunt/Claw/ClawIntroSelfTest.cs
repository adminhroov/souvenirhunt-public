using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SouvenirHunt
{
    /// <summary>
    /// 에디터 전용 자동 검증 — 인증 씬(ClawIntro)·씬 전환(10-15 미션형 복원). AutoRun 토큰 @introtest → 마커 + ClawIntro 씬 재생.
    /// VIP 인증 상세(10-26 기본: 카드키 없이 바로 홍채·시선 — 처음 바라볼 때 참가자 조회·시선 이탈·직전 참가자 경고) → 인증 끝 = 바로 Claw 씬(Ready 없이 영상/디졸브)
    /// → 운영자 리셋 = 인증부터 → 로그인 없음 경고 → [그대로 진행] → 게임 → 리셋 → vipCardStep=true(되돌린 예전 흐름: 자세·중복 입력·카드키 2초) → 게임
    /// → 착용 감지(벗은 동안 인증 멈춤 → 벗음 카운트다운 → 30초 뒤 처음으로 → 다시 실행 처음으로) → 게임 끝 [다시 시작] → 처음([시연 시작] 대기, 30초 자동과 겹쳐도 한 번)
    /// → vipStartButton=false(바로 홍채·시선) → 리셋 → vipIntroEnabled=false면 인증 없이 바로 게임 → config 원복.
    /// 10-29 맨 앞: [홈] 짧게 → 종료/취소 창(취소 → 인증 계속 · 홍채·시선에서 판정 멈춤 → ← 종료 + [잡기] → QuitHandler 가짜 호출) → 리셋.
    /// 10-26부터 인증 맨 앞 = Beam Pro [시연 시작] 대기(vipStartButton) — 각 부분은 [시연 시작]을 눌러 홍채·시선으로 들어간다.
    /// 씬을 넘나들므로 DontDestroyOnLoad. 결과 "[IntroSelfTest] RESULT PASS/FAIL" 후 재생 종료. 마커가 없으면 아무 것도 하지 않는다.
    /// 가짜 서버만 쓴다(실서버 호출 없음).
    /// </summary>
    public class ClawIntroSelfTest : MonoBehaviour
    {
#if UNITY_EDITOR
        public static readonly string MarkerPath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "claw_introtest.txt"));

        StringBuilder log;
        int fails;

        IEnumerator Start()
        {
            if (!File.Exists(MarkerPath)) yield break;
            try { File.Delete(MarkerPath); } catch { }
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            yield return StartCoroutine(Run());
        }

        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) fails++;
        }

        static IEnumerator WaitScene(string path, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while ((SceneManager.GetActiveScene().path != path || ClawSceneRouter.Loading) && Time.realtimeSinceStartup - t0 < timeout) yield return null;
            yield return null; yield return null; // Awake·Start
        }

        static void Tap()
        {
            ClawPadInput.PressGrab();
            ClawPadInput.ReleaseGrab();
        }

        static IEnumerator WaitPhase(VipIntro intro, float timeout, params VipIntro.Phase[] phases)
        {
            float t0 = Time.realtimeSinceStartup;
            while (intro != null && Array.IndexOf(phases, intro.Current) < 0 && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        static IEnumerator WaitSec(float sec)
        {
            float t0 = Time.time;
            while (Time.time - t0 < sec) yield return null;
        }

        /// <summary>게임 씬 도착 확인 — 미션형 세션·Ready 없이 바로 시작(영상 또는 디졸브 또는 안내)·운영자 패널 세션 교체</summary>
        IEnumerator ExpectGame(string where)
        {
            yield return WaitScene(ClawSceneRouter.GameScenePath, 20f);
            var s = ClawSessionBase.Instance;
            Check(SceneManager.GetActiveScene().path == ClawSceneRouter.GameScenePath && s is ClawGameManager,
                  $"{where}: Claw 씬 도착·세션 ClawGameManager (실제 {SceneManager.GetActiveScene().path}, {(s != null ? s.GetType().Name : "null")})");
            if (s == null) yield break;
            var expect = s.RevealEnabled ? ClawSessionBase.State.Reveal : ClawSessionBase.State.Guide;
            Check(s.Current == expect, $"{where}: 인증에서 왔으면 Ready 없이 바로 {expect} (실제 {s.Current})");
            Check(ReferenceEquals(GameSession.Current, s), $"{where}: 운영자 패널 세션 = 게임 세션");
            CheckSingleRig(where);
        }

        /// <summary>10-15 성능: 씬 전환 뒤 XR 리그·메인 카메라는 1개(이전 씬 리그가 DontDestroyOnLoad로 남아 장면을 2번 그리던 문제)</summary>
        void CheckSingleRig(string where)
        {
            var rigs = FindObjectsByType<XRRigDedup>(FindObjectsSortMode.None);
            int mains = 0;
            foreach (var c in Camera.allCameras) if (c.CompareTag("MainCamera")) mains++;
            // 남은 1개는 SDK 세션 관리자가 다시 DontDestroyOnLoad로 옮기므로 씬 이름은 따지지 않는다
            Check(rigs.Length == 1, $"{where}: XR 리그 1개 (실제 {rigs.Length}개{(rigs.Length > 0 ? ", 씬 " + rigs[0].gameObject.scene.name : "")})");
            Check(mains == 1, $"{where}: 메인 카메라 1대 (실제 {mains}대 — 2대면 장면을 2번 그림)");
        }

        /// <summary>운영자 리셋(IGameSession) → 인증 씬 복귀, 맨 앞 VIP 인증 패드부터(체험자 교체)</summary>
        IEnumerator OperatorResetToIntro()
        {
            GameSession.Current?.ResetGame();
            yield return WaitScene(ClawSceneRouter.IntroScenePath, 20f);
            var ci = ClawIntro.Instance;
            Check(SceneManager.GetActiveScene().path == ClawSceneRouter.IntroScenePath && ci != null && !ci.Launched,
                  $"운영자 리셋 → 인증 씬 복귀 (실제 {SceneManager.GetActiveScene().path})");
            if (ci != null && ci.Intro != null && ci.Config.vipIntroEnabled)
            {
                var first = ci.Config.vipCardStep ? VipIntro.Phase.WaitDock : ci.Config.vipStartButton ? VipIntro.Phase.StartWait : VipIntro.Phase.Gaze;
                Check(ci.InIntro && ci.Intro.Current == first, $"운영자 리셋 → 맨 앞 VIP 인증({first})부터");
            }
            CheckSingleRig("운영자 리셋 → 인증 씬");
        }

        /// <summary>10-26 [시연 시작] 대기 → Beam Pro 잡기 → 홍채·시선</summary>
        IEnumerator PressStart(VipIntro intro, string where)
        {
            Check(intro.Current == VipIntro.Phase.StartWait, $"{where}: [시연 시작] 대기 ({intro.Current})");
            int n0 = intro.StartPresses;
            Tap();
            yield return WaitPhase(intro, 2f, VipIntro.Phase.Gaze);
            Check(intro.Current == VipIntro.Phase.Gaze && intro.StartPresses == n0 + 1 && !intro.AtStart, $"{where}: [시연 시작] → 홍채·시선 ({intro.Current})");
        }

        /// <summary>
        /// 10-29 인증 씬 [홈] 짧게 → 종료/취소 창: [시연 시작] 대기에서 열림 → (기본 = 취소) [잡기] → 닫힘·인증 그대로(확인 누름이 시작으로 새지 않음)
        /// → [시연 시작] → 홍채·시선에서 열림(정면을 봐도 판정 멈춤) → 조이스틱 ← + [잡기] = 종료 → QuitHandler(가짜) 1번 → 리셋 → [시연 시작] 대기
        /// </summary>
        IEnumerator IntroPartExit(ClawIntro ci)
        {
            var intro = ci.Intro;
            int quits = 0;
            var keepQuit = ci.QuitHandler;
            ci.QuitHandler = () => quits++;
            intro.GazeOverride = false; // 시선 판정·참가자 조회가 돌지 않게(서버 호출 없음)
            ClawPadInput.PressHome();
            yield return null; yield return null;
            Check(ci.ExitDialogOpen && intro.Current == VipIntro.Phase.StartWait && ci.StateName.Contains("종료 창") &&
                  (ClawPadInput.Root == null || (ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel == "확인")),
                  $"[종료 창] [시연 시작] 대기에서 홈 → 창 열림 ({ci.StateName}, 잡기 '{ClawPadInput.GrabLabel}')");
            int n0 = intro.StartPresses;
            Tap(); // 기본 선택 = 취소
            yield return null; yield return null;
            Check(!ci.ExitDialogOpen && quits == 0 && intro.Current == VipIntro.Phase.StartWait && intro.StartPresses == n0 &&
                  (ClawPadInput.Root == null || ClawPadInput.GrabLabel == "시연 시작"),
                  $"[종료 창] 취소 → 닫힘·인증 그대로 (단계 {intro.Current}, 시작 누름 {intro.StartPresses - n0}, 잡기 '{ClawPadInput.GrabLabel}')");
            yield return PressStart(intro, "[종료 창]");
            ClawPadInput.PressHome();
            yield return null; yield return null;
            intro.GazeOverride = true; // 창이 열린 동안엔 정면을 봐도 진행 안 함
            yield return WaitSec(0.5f);
            Check(ci.ExitDialogOpen && intro.Current == VipIntro.Phase.Gaze && intro.GazeProgress == 0f,
                  $"[종료 창] 홍채·시선에서 홈 → 창 열림·판정 멈춤 (진행 {intro.GazeProgress:P0})");
            intro.GazeOverride = false;
            ClawPadInput.SetMachineStick(new Vector2(-0.9f, 0f));
            yield return null; yield return null;
            ClawPadInput.SetMachineStick(Vector2.zero);
            Tap();
            yield return null; yield return null;
            Check(!ci.ExitDialogOpen && quits == 1, $"[종료 창] ← 종료 + [잡기] → QuitHandler {quits}번");
            ci.QuitHandler = keepQuit;
            intro.GazeOverride = null;
            ci.ResetGame();
            yield return null; yield return null;
            Check(ci.InIntro && intro.Current == VipIntro.Phase.StartWait && intro.AtStart && !ci.ExitDialogOpen, $"[종료 창] 리셋 → [시연 시작] 대기 ({intro.Current})");
        }

        /// <summary>VIP 인증 상세 1부(부팅, 10-26 기본 = 카드키 없음): 바로 EYE SCAN·카드 패널 안 보임 → 처음 바라볼 때 참가자 조회 → 시선 이탈 → 승인
        /// → 직전 참가자 경고 → 교체 → 정상 → 게임</summary>
        IEnumerator IntroPartA(ClawIntro ci)
        {
            var intro = ci.Intro;
            var cfg = ci.Config;
            intro.GazeOverride = false; // 에디터 카메라는 놓인 타깃을 이미 보고 있다 — 판정은 여기서 조절
            Check(!cfg.vipCardStep && cfg.vipStartButton && ci.InIntro && intro.Current == VipIntro.Phase.StartWait && intro.AtStart && ci.StateName.StartsWith("VipIntro/") && !intro.CardPanelShown,
                  $"[VIP] 부팅 → [시연 시작] 대기(EYE SCAN READY), 카드 패널 숨김 ({ci.StateName}, vipCardStep={cfg.vipCardStep}, vipStartButton={cfg.vipStartButton})");
            Check(ClawPadInput.Root == null || (ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel == "시연 시작"), $"[VIP] Beam Pro '시연 시작' 활성 ('{ClawPadInput.GrabLabel}')");

            // 직전 참가자 경고 준비 — 가짜 서버: 조회 1회차 = 직전 체험자와 같음, 2회차 = 진행자가 교체한 다음 참가자
            var fake = PrizeSelfTest.Arm(intro.Prize).User(200, PrizeSelfTest.BodyDid).User(200, PrizeSelfTest.BodyDid2);
            VipIntro.PreviousDid = PrizeSelfTest.Did;
            intro.GazeOverride = true; // 정면을 보고 있어도
            yield return WaitSec(0.6f);
            Check(intro.Current == VipIntro.Phase.StartWait && intro.GazeProgress == 0f && fake.Count("GET") == 0,
                  $"[VIP] [시연 시작] 전에는 정면을 봐도 인증 진행·참가자 조회 없음 (진행 {intro.GazeProgress:P0}, GET {fake.Count("GET")})");
            intro.GazeOverride = false;
            yield return PressStart(intro, "[VIP]");
            Check(ClawPadInput.Root == null || !ClawPadInput.GrabEnabled, "[VIP] 응시 중 [잡기] 꺼짐");
            yield return WaitSec(0.3f);
            Check(fake.Count("GET") == 0, $"[VIP] 바라보기 전에는 참가자 조회 안 함 (GET {fake.Count("GET")})");

            // 1) 시선 — 1초 보다가 벗어나면 0부터, 다시 vipGazeSeconds 연속이면 승인. 처음 바라보는 순간 참가자 조회
            intro.GazeOverride = true;
            yield return WaitSec(1f);
            float p1 = intro.GazeProgress;
            Check(fake.Count("GET") == 1, $"[VIP] 처음 바라보는 순간 참가자 조회 시작 (GET {fake.Count("GET")})");
            intro.GazeOverride = false;
            yield return null; yield return null;
            Check(p1 > 0.25f && p1 < 0.95f && intro.GazeProgress == 0f && intro.Current == VipIntro.Phase.Gaze,
                  $"[VIP] 응시 1초 진행 {p1:P0} → 시선 이탈 → 0%(처음부터)");
            intro.GazeOverride = true;
            float t0 = Time.time;
            yield return WaitPhase(intro, 8f, VipIntro.Phase.Granted, VipIntro.Phase.Warning);
            float gazeT = Time.time - t0;
            Check(intro.Current == VipIntro.Phase.Granted && gazeT >= cfg.vipGazeSeconds - 0.1f && intro.GrantedTitle == "ACCESS GRANTED" && fake.Count("GET") == 1,
                  $"[VIP] {gazeT:F1}s 연속 응시(기준 {cfg.vipGazeSeconds}s) → {intro.GrantedTitle} (다시 봐도 조회는 1번 — GET {fake.Count("GET")})");

            // 2) 직전 체험자와 같은 참가자 → 경고(게임으로 안 넘어감) → 진행자 교체 → [다시 확인] → 정상 → 게임
            yield return WaitPhase(intro, 6f, VipIntro.Phase.Warning, VipIntro.Phase.Done);
            Check(intro.Current == VipIntro.Phase.Warning && intro.WarningReason.Contains("직전 체험자") && !ci.Launched,
                  $"[VIP] 직전 체험자와 같은 참가자 → 경고, 게임으로 안 넘어감 ('{intro.WarningReason.Replace('\n', ' ')}')");
            Tap();
            yield return WaitPhase(intro, 6f, VipIntro.Phase.Done);
            Check(intro.Current == VipIntro.Phase.Done && fake.Count("GET") == 2 && VipIntro.PreviousDid == PrizeSelfTest.Did2 && ci.Launched,
                  $"[VIP] 진행자 교체 후 [다시 확인] → 정상 → 게임 씬 로드, 직전 참가자 갱신 (GET {fake.Count("GET")})");
            intro.GazeOverride = null;
        }

        /// <summary>VIP 인증 2부(리셋 뒤): 응시 → 진행자 로그인 없음 → [다시 확인]해도 없음 → [그대로 진행](게임은 막지 않음)</summary>
        IEnumerator IntroPartB(ClawIntro ci, string prevDid)
        {
            var intro = ci.Intro;
            intro.GazeOverride = false;
            var fake = PrizeSelfTest.Arm(intro.Prize).User(404, PrizeSelfTest.BodyNoParticipant).User(404, PrizeSelfTest.BodyNoParticipant);
            yield return PressStart(intro, "[VIP 2]");
            intro.GazeOverride = true;
            yield return WaitPhase(intro, 10f, VipIntro.Phase.Warning, VipIntro.Phase.Done);
            Check(intro.Current == VipIntro.Phase.Warning && intro.WarningReason.Contains("로그인") && !intro.WarningRechecked && fake.Count("GET") == 1,
                  "[VIP] 진행자 로그인 없음 → 경고 '[잡기] = 다시 확인'");
            Tap();
            float t1 = Time.realtimeSinceStartup;
            while (!intro.WarningRechecked && intro.Current == VipIntro.Phase.Warning && Time.realtimeSinceStartup - t1 < 6f) yield return null;
            yield return null;
            Check(intro.Current == VipIntro.Phase.Warning && intro.WarningRechecked && fake.Count("GET") == 2 && !ci.Launched,
                  $"[VIP] 다시 확인해도 로그인 없음 → 경고 유지 '[잡기] = 그대로 진행' (GET {fake.Count("GET")})");
            intro.GazeOverride = null;
            Tap();
            yield return WaitPhase(intro, 3f, VipIntro.Phase.Done);
            Check(intro.Current == VipIntro.Phase.Done && ci.Launched && VipIntro.PreviousDid == prevDid,
                  "[VIP] [그대로 진행] → 게임(막지 않음), 직전 참가자는 그대로");
        }

        static string Flat(string s) => (s ?? "").Replace('\n', ' ');

        /// <summary>
        /// 10-26 착용 감지·자동 처음으로(안경 센서는 흉내): 벗은 동안 홍채 인증 멈춤·조회 없음·Beam Pro "다음 체험자" → 쓰면 시작 → 게임
        /// → 벗음 카운트다운·다시 쓰면 취소 → 벗고 30초 → 처음(인증 맨 앞) → 게임 → 다시 실행(백그라운드 1초 그대로·벗은 채 멈춤 그대로·몇 초 이상 처음으로)
        /// </summary>
        IEnumerator IntroPartWear(ClawIntro ci)
        {
            var ww = ClawWearWatch.Instance;
            Check(ww != null, "[착용] ClawWearWatch 자동 생성(씬과 무관하게 1개)");
            if (ww == null) yield break;
            var intro = ci.Intro;
            float resetSec = ci.Config.clawWearResetSeconds, resumeSec = ci.Config.clawResumeResetSeconds;
            int resets0 = ww.AutoResets;
            intro.GazeOverride = false;
            var fake = PrizeSelfTest.Arm(intro.Prize).User(200, PrizeSelfTest.BodyDid2);
            VipIntro.PreviousDid = "";

            // [시연 시작] 대기에서 벗음 → 카운트 없이 '다음 체험자 — 안경을 쓰고 [시연 시작]' → 누르면 '한 번 더' 안내만 → 한 번 더 = 쓴 것으로 보고 시작
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
            yield return null; yield return null;
            Check(intro.Current == VipIntro.Phase.StartWait && ww.Notice.Contains("다음 체험자") && ww.Notice.Contains("[시연 시작]") && ww.Remaining < 0f && ww.AutoResets == resets0,
                  $"[착용] [시연 시작] 대기에서 벗음 → 카운트 없이 Beam Pro 안내 ('{Flat(ww.Notice)}')");
            Tap();
            yield return null; yield return null;
            Check(intro.Current == VipIntro.Phase.StartWait && intro.StartConfirmPending && ww.Notice.Contains("한 번 더") && ClawWearWatch.State == ClawWearWatch.WearState.Off,
                  $"[착용] 안경 벗음 상태 [시연 시작] → 시작 안 함·'한 번 더' 안내 ('{Flat(ww.Notice)}')");
            Tap();
            yield return null; yield return null;
            Check(intro.Current == VipIntro.Phase.Gaze && ClawWearWatch.State == ClawWearWatch.WearState.On,
                  $"[착용] 몇 초 안에 한 번 더 → 쓴 것으로 보고 홍채·시선 ({intro.Current}, 착용 {ClawWearWatch.State})");

            // 인증 중 벗음 → 멈춤(조회 없음)·Beam Pro 카운트다운 → 다시 쓰면 이어서
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
            intro.GazeOverride = true; // 정면을 보고 있어도
            yield return WaitSec(1f);
            Check(intro.Current == VipIntro.Phase.Gaze && intro.WaitingWear && intro.GazeProgress == 0f && fake.Count("GET") == 0,
                  $"[착용] 안경 벗음 → 정면을 봐도 홍채 인증 멈춤·참가자 조회 없음 (진행 {intro.GazeProgress:P0}, GET {fake.Count("GET")})");
            Check(ww.Remaining > 0f && ww.Remaining <= resetSec && ww.Notice.Contains("처음 화면") && ww.AutoResets == resets0,
                  $"[착용] [시연 시작] 뒤 벗음 → Beam Pro 카운트다운 {ww.Remaining:F1}s ('{Flat(ww.Notice)}')");
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.On);
            yield return WaitSec(1f);
            Check(!intro.WaitingWear && intro.GazeProgress > 0.2f && fake.Count("GET") == 1 && ww.Notice.Length == 0,
                  $"[착용] 안경 씀 → 홍채 인증 시작·조회 1번·안내 숨김 (진행 {intro.GazeProgress:P0}, GET {fake.Count("GET")})");
            yield return WaitPhase(intro, 8f, VipIntro.Phase.Done, VipIntro.Phase.Warning);
            intro.GazeOverride = null;
            Check(intro.Current == VipIntro.Phase.Done && ci.Launched, $"[착용] 응시 → 승인 → 게임 ({intro.Current})");
            yield return WaitScene(ClawSceneRouter.GameScenePath, 20f);

            // 게임 중 벗음 → Beam Pro 카운트다운 → 다시 쓰면 취소
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
            yield return null; yield return null;
            Check(ww.Remaining > resetSec - 2f && ww.Remaining <= resetSec && ww.Notice.Contains("처음 화면"),
                  $"[착용] 게임 중 벗음 → Beam Pro 카운트다운 {ww.Remaining:F1}s/{resetSec:0}s ('{Flat(ww.Notice)}')");
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.On);
            yield return null; yield return null;
            Check(ww.Remaining < 0f && ww.Notice.Length == 0 && SceneManager.GetActiveScene().path == ClawSceneRouter.GameScenePath && ww.AutoResets == resets0,
                  "[착용] 카운트 끝나기 전에 다시 씀 → 취소·게임 그대로");

            // 벗고 30초 → 처음(인증 맨 앞)
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
            ClawWearWatch.SimulateOffAgo(resetSec - 0.5f);
            yield return WaitSec(1f);
            yield return WaitScene(ClawSceneRouter.IntroScenePath, 20f);
            ci = ClawIntro.Instance;
            Check(ww.AutoResets == resets0 + 1 && SceneManager.GetActiveScene().path == ClawSceneRouter.IntroScenePath && ci != null && ci.InIntro && ci.Intro.AtStart &&
                  ci.Intro.Current == VipIntro.Phase.StartWait,
                  $"[착용] 벗고 {resetSec:0}초 → 자동으로 처음([시연 시작] 대기) ({ww.LastResetReason})");
            yield return null;
            Check(ww.Notice.Contains("다음 체험자"), $"[착용] 처음으로 온 뒤 벗은 채 → '다음 체험자' 안내 ('{Flat(ww.Notice)}')");
            CheckSingleRig("[착용] 자동 처음으로 → 인증 씬");
            if (ci == null) yield break;
            Check(ClawPadInput.Root == null || ClawPadInput.GrabEnabled, "[착용] 벗은 채 [시연 시작] 대기 → 버튼 활성");
            Tap();
            yield return null; yield return null;
            Tap();
            yield return null; yield return null;
            Check(ci.Intro.Current == VipIntro.Phase.Gaze, $"[착용] [시연 시작] 두 번 → 홍채·시선 ({ci.Intro.Current})");
            // 인증 중 벗음 — 센서가 안 알려 줄 때 [인증 시작] = 쓴 것으로
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
            yield return null; yield return null;
            Check(ci.Intro.WaitingWear && (ClawPadInput.Root == null || (ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel == "인증 시작")),
                  $"[착용] 인증 중 벗음 → [인증 시작] 버튼 활성 ('{ClawPadInput.GrabLabel}')");
            Tap();
            yield return null; yield return null; yield return null;
            Check(ClawWearWatch.State == ClawWearWatch.WearState.On && !ci.Intro.WaitingWear,
                  $"[착용] 센서가 안 알려 줘도 [인증 시작] → 쓴 것으로 보고 홍채 인증 시작 (착용 {ClawWearWatch.State})");

            // 다시 실행(멀티 리줌: 홈·뒤로로 나가도 앱이 살아 있다)
            ci.Intro.GazeOverride = true;
            yield return WaitPhase(ci.Intro, 8f, VipIntro.Phase.Done, VipIntro.Phase.Warning);
            ci.Intro.GazeOverride = null;
            yield return WaitScene(ClawSceneRouter.GameScenePath, 20f);
            int r1 = ww.AutoResets;
            ClawWearWatch.SimulateResume(Mathf.Max(0f, resumeSec - 1f));
            yield return null;
            ClawWearWatch.SimulateResume(resumeSec + 5f, offWhenPaused: true);
            yield return null; yield return null;
            Check(ww.AutoResets == r1 && SceneManager.GetActiveScene().path == ClawSceneRouter.GameScenePath,
                  $"[다시 실행] 백그라운드 {Mathf.Max(0f, resumeSec - 1f):0}초 → 그대로, 벗은 채 멈췄던 것 → 벗음 규칙(바로 처음으로 안 감)");
            ClawWearWatch.SimulateResume(resumeSec + 2f);
            yield return WaitScene(ClawSceneRouter.IntroScenePath, 20f);
            var ci2 = ClawIntro.Instance;
            Check(ww.AutoResets == r1 + 1 && ci2 != null && ci2.InIntro && ci2.Intro.Current == VipIntro.Phase.StartWait && SceneManager.GetActiveScene().path == ClawSceneRouter.IntroScenePath,
                  $"[다시 실행] 백그라운드 {resumeSec + 2f:0}초 → 자동으로 처음([시연 시작] 대기) ({ww.LastResetReason})");
        }

        /// <summary>
        /// 10-26 게임 끝 → [다시 시작](작업 리스트 "게임 종료 후 다시 시작 UI → 최초 시작 단계로, 게임·컨트롤러 상태 초기화")
        /// → 처음([시연 시작] 대기)·패드 입력 비움. 벗음 30초 카운트가 끝나 갈 때 눌러도 자동 처음으로와 겹치지 않음(한 번만)
        /// </summary>
        IEnumerator IntroPartRestart(ClawIntro ci)
        {
            var ww = ClawWearWatch.Instance;
            var intro = ci.Intro;
            float resetSec = ci.Config.clawWearResetSeconds;
            yield return PressStart(intro, "[다시 시작] 준비");
            intro.GazeOverride = true;
            yield return WaitPhase(intro, 8f, VipIntro.Phase.Done, VipIntro.Phase.Warning);
            intro.GazeOverride = null;
            if (intro.Current == VipIntro.Phase.Warning) { Tap(); yield return null; Tap(); yield return WaitPhase(intro, 6f, VipIntro.Phase.Done); }
            yield return WaitScene(ClawSceneRouter.GameScenePath, 20f);
            var gm = ClawGameManager.Instance;
            Check(gm != null, "[다시 시작] 게임 씬 도착");
            if (gm == null) yield break;
            float t0 = Time.realtimeSinceStartup;
            while (gm.Current != ClawSessionBase.State.Guide && Time.realtimeSinceStartup - t0 < 9f) yield return null; // 10-28 6 → 9초(기계 다가오기 1.5초 포함)
            gm.EditorSkipReveal(); yield return null;
            gm.ConfirmGuide();
            while (gm.Current != ClawSessionBase.State.Playing && Time.realtimeSinceStartup - t0 < 15f) yield return null;
            gm.EditorSetTimeRemaining(0.05f);
            while (gm.Current != ClawSessionBase.State.Result && Time.realtimeSinceStartup - t0 < 18f) yield return null;
            Check(gm.Current == ClawSessionBase.State.Result && !gm.RewardEligible && gm.RestartOffered,
                  $"[다시 시작] 게임 → 시간 초과 결과 → [다시 시작] 내놓음 ({gm.Current}, 보상 대상 {gm.RewardEligible})");
            Tap(); yield return null; yield return null;
            Check(gm != null && gm.Current == ClawSessionBase.State.Result && !ClawSceneRouter.Loading, "[다시 시작] 결과가 뜨자마자 누른 잡기(연타)는 무시");
            t0 = Time.realtimeSinceStartup;
            while (!gm.RestartAllowed && Time.realtimeSinceStartup - t0 < ClawSessionBase.RestartGuardSec + 1f) yield return null;
            yield return null;
            Check(ClawPadInput.Root == null || (ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel == "다시 시작"), $"[다시 시작] Beam Pro '{ClawPadInput.GrabLabel}' 활성");

            // 벗음 30초 카운트가 거의 끝난 채로(0.3초 남음) [다시 시작] + 조이스틱을 누른 채
            int resets0 = ww.AutoResets;
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
            ClawWearWatch.SimulateOffAgo(resetSec - 0.3f);
            ClawPadInput.SetStick(new Vector2(0.5f, 0.2f), true);
            Tap();
            yield return WaitScene(ClawSceneRouter.IntroScenePath, 20f);
            yield return WaitSec(1f); // 카운트가 끝났을 시각을 넘김
            var ci2 = ClawIntro.Instance;
            Check(SceneManager.GetActiveScene().path == ClawSceneRouter.IntroScenePath && ci2 != null && ci2.InIntro && ci2.Intro.Current == VipIntro.Phase.StartWait && ci2.Intro.AtStart,
                  $"[다시 시작] 잡기 → 처음([시연 시작] 대기) ({(ci2 != null ? ci2.StateName : "인증 씬 없음")})");
            Check(ww.AutoResets == resets0 && ww.Remaining < 0f && ww.Notice.Contains("다음 체험자"),
                  $"[다시 시작] 벗음 카운트가 끝날 무렵 눌러도 자동 처음으로와 겹치지 않음(자동 {resets0}→{ww.AutoResets}), 처음 화면 '다음 체험자' 안내 ('{Flat(ww.Notice)}')");
            Check(!ClawPadInput.IsTouching && ClawPadInput.RawStick == Vector2.zero && !ClawPadInput.GrabHeld &&
                  (ClawPadInput.Root == null || (ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel == "시연 시작")),
                  $"[다시 시작] 패드 입력 비움(조이스틱 {ClawPadInput.RawStick}·터치 {ClawPadInput.IsTouching})·Beam Pro '{ClawPadInput.GrabLabel}'");
            CheckSingleRig("[다시 시작] → 인증 씬");
            ClawWearWatch.SimulateWear(ClawWearWatch.WearState.On);
            yield return null;
        }

        /// <summary>10-26 되돌리기: vipStartButton=false → [시연 시작] 대기 없이 바로 홍채·시선</summary>
        IEnumerator IntroPartNoStartButton(ClawIntro ci)
        {
            bool wrote = ConfigLoader.SetValues(new Dictionary<string, string> { { "vipStartButton", "false" } }, out string wmsg);
            Check(wrote, $"config 쓰기 vipStartButton=false ({wmsg})");
            ci.Intro.GazeOverride = false; // 에디터 카메라는 놓인 타깃을 이미 보고 있다
            ci.ResetGame();
            yield return null;
            Check(!ci.Config.vipStartButton && ci.Intro.Current == VipIntro.Phase.Gaze && ci.Intro.AtStart,
                  $"[시연 시작 끔] 리셋 → 바로 홍채·시선·맨 앞 ({ci.Intro.Current}, vipStartButton={ci.Config.vipStartButton}, 진행 {ci.Intro.GazeProgress:P0})");
            ci.Intro.GazeOverride = null;
            ConfigLoader.SetValues(new Dictionary<string, string> { { "vipStartButton", "true" } }, out _);
            ci.ResetGame();
            yield return null;
            Check(ci.Config.vipStartButton && ci.Intro.Current == VipIntro.Phase.StartWait, $"config 원복 vipStartButton=true → [시연 시작] 대기 ({ci.Intro.Current})");
        }

        /// <summary>
        /// 10-26 되돌리기 경로(vipCardStep=true = 예전 흐름): 패드 대기 → 자세 감지 → 인증 중 [잡기] 무시 → 카드키 2초·초록 체크 → 홍채·시선(조회는 카드키 끝에) → 게임
        /// </summary>
        IEnumerator IntroPartCard(ClawIntro ci)
        {
            var intro = ci.Intro;
            var cfg = ci.Config;
            Check(cfg.vipCardStep && ci.InIntro && intro.Current == VipIntro.Phase.WaitDock && intro.CardPanelShown,
                  $"[VIP 카드키 켬] 리셋 → 카드키 인증 패드 대기·패널 보임 ({ci.StateName})");
            Check(ClawPadInput.Root == null || ClawPadInput.GrabEnabled, "[VIP 카드키 켬] 패드 대기 중 [인증] 버튼 활성");
            var fake = PrizeSelfTest.Arm(intro.Prize).User(200, PrizeSelfTest.BodyDid2);
            VipIntro.PreviousDid = "";

            // 자세 감지 — 수평을 vipPoseHoldSeconds 유지하면 버튼 없이 인증 시작
            intro.PoseOverride = true;
            float t0 = Time.time;
            yield return WaitPhase(intro, 5f, VipIntro.Phase.Linking);
            float held = Time.time - t0;
            intro.PoseOverride = null;
            float linkStart = Time.time;
            Check(intro.Current == VipIntro.Phase.Linking && intro.LinkTrigger == "자세" && held >= cfg.vipPoseHoldSeconds - 0.05f,
                  $"[VIP 카드키 켬] Beam Pro 수평 {held:F2}s(기준 {cfg.vipPoseHoldSeconds}s) → 인증 시작(자세)");
            Check(intro.ScannerReady, "[VIP 카드키 켬] STEP 01 인증 패널 = 카드 스캐너 모델(10-13 연출안)");
            Tap();
            yield return null;
            Check(intro.Current == VipIntro.Phase.Linking, "[VIP 카드키 켬] 인증 중 [잡기] 무시(중복 인증 차단)");
            yield return WaitPhase(intro, 5f, VipIntro.Phase.CardOk);
            float linkT = Time.time - linkStart;
            yield return WaitSec(0.3f);
            Check(intro.Current == VipIntro.Phase.CardOk && intro.CardCheckShown && linkT >= 1.9f && linkT < 2.6f,
                  $"[VIP 카드키 켬] STEP 01 카드키 인식 {linkT:F1}s(연출안 2초) → 스캐너에 초록 ACCESS GRANTED");
            yield return WaitPhase(intro, 5f, VipIntro.Phase.Gaze);
            Check(intro.Current == VipIntro.Phase.Gaze && fake.Count("GET") == 1 && !intro.CardPanelShown,
                  $"[VIP 카드키 켬] → STEP 02 홍채·시선 + 참가자 조회는 카드키 끝에 (GET {fake.Count("GET")})");
            intro.GazeOverride = true;
            yield return WaitPhase(intro, 10f, VipIntro.Phase.Done, VipIntro.Phase.Warning);
            Check(intro.Current == VipIntro.Phase.Done && ci.Launched && fake.Count("GET") == 1,
                  $"[VIP 카드키 켬] 응시 → 승인 → 게임 (GET {fake.Count("GET")})");
            intro.GazeOverride = null;
        }

        IEnumerator Run()
        {
            log = new StringBuilder("[IntroSelfTest] 시작\n");
            fails = 0;
            yield return null; yield return null;

            var ci = ClawIntro.Instance;
            Check(ci != null, "ClawIntro 존재");
            if (ci == null) { Finish(); yield break; }
            var cfg0 = ci.Config;
            bool vipOn = cfg0.vipIntroEnabled;
            bool cardOn = cfg0.vipCardStep;
            Check(ReferenceEquals(GameSession.Current, ci), "인증 씬이 운영자 패널 세션(IGameSession)");
            Check(ClawSceneRouter.InBuild(ClawSceneRouter.IntroScenePath) && ClawSceneRouter.InBuild(ClawSceneRouter.GameScenePath),
                  "빌드 설정: ClawIntro·Claw enabled");
            Check(SceneUtility.GetBuildIndexByScenePath(ClawSceneRouter.IntroScenePath) == 0, "부팅 씬 = ClawIntro(index 0)");
            Check(VipIntro.IsFlat(new Vector3(0f, 0f, -1f), 25f) && VipIntro.IsFlat(new Vector3(0f, 0f, 9.8f), 25f) &&
                  !VipIntro.IsFlat(new Vector3(0f, -1f, 0f), 25f) && !VipIntro.IsFlat(new Vector3(0.5f, 0f, -0.8f), 25f) &&
                  VipIntro.IsFlat(new Vector3(0.5f, 0f, -0.8f), 35f), "[VIP] 자세 판정 — 화면 위·아래 수평만, 기울기 32°는 25°에선 아님·35°에선 수평");
            string prevSaved = VipIntro.PreviousDid;

            if (ci.Intro != null && vipOn && !cardOn)
            {
                // 10-29 [홈] 짧게 → 종료/취소 창
                yield return IntroPartExit(ci);
                // 1) 인증 상세 → 게임
                yield return IntroPartA(ci);
                yield return ExpectGame("인증 끝");
                yield return OperatorResetToIntro();

                // 2) 로그인 없음 → 그대로 진행 → 게임
                ci = ClawIntro.Instance;
                if (ci != null && ci.InIntro)
                {
                    yield return IntroPartB(ci, VipIntro.PreviousDid);
                    yield return ExpectGame("그대로 진행");
                    yield return OperatorResetToIntro();
                }

                // 10-26 착용 감지·자동 처음으로·다시 실행
                ci = ClawIntro.Instance;
                if (ci != null && ci.InIntro) yield return IntroPartWear(ci);

                // 10-26 게임 끝 → [다시 시작] → 처음([시연 시작] 대기) · [시연 시작] 끄기(되돌리기)
                ci = ClawIntro.Instance;
                if (ci != null && ci.InIntro) yield return IntroPartRestart(ci);
                ci = ClawIntro.Instance;
                if (ci != null && ci.InIntro) yield return IntroPartNoStartButton(ci);

                // 3) 되돌리기 경로: vipCardStep=true(예전 카드키 → 홍채·시선) → 게임 → 원래 값
                ci = ClawIntro.Instance;
                if (ci != null && ci.InIntro)
                {
                    bool wrote = ConfigLoader.SetValues(new Dictionary<string, string> { { "vipCardStep", "true" } }, out string wmsg);
                    Check(wrote, $"config 쓰기 vipCardStep=true ({wmsg})");
                    ci.ResetGame();
                    yield return null;
                    yield return IntroPartCard(ci);
                    yield return ExpectGame("카드키 켬");
                    ConfigLoader.SetValues(new Dictionary<string, string> { { "vipCardStep", cardOn ? "true" : "false" } }, out _);
                    yield return OperatorResetToIntro();
                    ci = ClawIntro.Instance;
                    Check(ci != null && ci.Config.vipCardStep == cardOn, $"config 원복 vipCardStep={cardOn}");
                }
            }
            else Check(false, $"config vipIntroEnabled=true·vipCardStep=false(기본)·VipIntro 배선 필요 (vipIntroEnabled={vipOn}, vipCardStep={cardOn}, intro {(ci.Intro != null ? "있음" : "없음")})");
            VipIntro.PreviousDid = prevSaved;

            // 3) vipIntroEnabled=false → 인증 없이 바로 게임 → 원복
            ci = ClawIntro.Instance;
            if (ci != null)
            {
                bool wrote = ConfigLoader.SetValues(new Dictionary<string, string> { { "vipIntroEnabled", "false" } }, out string wmsg);
                ci.ResetGame();
                Check(wrote, $"config 쓰기 vipIntroEnabled=false ({wmsg})");
                yield return ExpectGame("인증 꺼짐");
                ConfigLoader.SetValues(new Dictionary<string, string> { { "vipIntroEnabled", vipOn ? "true" : "false" } }, out _);
                yield return OperatorResetToIntro();
                ci = ClawIntro.Instance;
                Check(ci != null && ci.Config.vipIntroEnabled == vipOn, $"config 원복 vipIntroEnabled={vipOn}");
            }

            Finish();
        }

        void Finish()
        {
            string verdict = fails == 0 ? "PASS" : $"FAIL({fails})";
            log.AppendLine($"[IntroSelfTest] RESULT {verdict}");
            if (fails == 0) Debug.Log(log.ToString());
            else Debug.LogError(log.ToString());
            UnityEditor.EditorApplication.ExitPlaymode();
            Destroy(gameObject);
        }
#endif
    }
}
