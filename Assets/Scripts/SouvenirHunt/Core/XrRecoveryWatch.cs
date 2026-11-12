using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace SouvenirHunt
{
    /// <summary>
    /// 10-29 안경 화면 링크 끊김 서명 감지(순수 함수 — 기기 감시와 에디터 재생 테스트가 같이 쓴다).
    /// 검은 화면 재현 로그(10-26 vc52): 검은 프로세스에서만 XREAL(MyGlasses 서버 lib)이 "get rsp error, 5001"을 1초마다 찍었다 = 안경 출력 링크가 세션에 안 붙음.
    /// 줄 = logcat threadtime 형식("MM-dd HH:mm:ss.fff  pid  tid L Tag: msg"). 최근 WindowSeconds 안에 같은 pid의 서명 줄이 FireCount 이상이면 발화
    /// </summary>
    public static class XrLinkLoss
    {
        public const string DefaultSignature = "get rsp error";
        public const int FireCount = 3;
        public const double WindowSeconds = 5.0;

        /// <summary>threadtime 줄의 시각(year 적용)·pid. 형식이 아니면 false</summary>
        public static bool TryParse(string line, int year, out DateTime time, out int pid)
        {
            time = default; pid = 0;
            if (string.IsNullOrEmpty(line) || line.Length < 24 || line[2] != '-' || line[5] != ' ' || line[8] != ':') return false;
            if (!DateTime.TryParseExact(year.ToString("D4", CultureInfo.InvariantCulture) + "-" + line.Substring(0, 18), "yyyy-MM-dd HH:mm:ss.fff",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) return false;
            int i = 18;
            while (i < line.Length && line[i] == ' ') i++;
            int start = i;
            while (i < line.Length && char.IsDigit(line[i])) i++;
            return i > start && int.TryParse(line.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
        }

        /// <summary>
        /// 10-29 감사: 이 앱 자기 로그인가 — Unity 태그(Debug.Log 전부) 또는 [XRHealth]·[ConfigLoader] 줄. 우리 로그에 서명 문자열이 섞여도(설정 요약 등) 집계하지 않게.
        /// 실제 서명 줄은 XREAL 태그(10-26 vc52 재현 로그 100·184줄 전부 E XREAL)라 영향 없음
        /// </summary>
        public static bool IsOwnLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (line.IndexOf("[XRHealth]", StringComparison.Ordinal) >= 0 || line.IndexOf("[ConfigLoader]", StringComparison.Ordinal) >= 0) return true;
            return TagOf(line) == "Unity";
        }

        /// <summary>threadtime 줄 "MM-dd HH:mm:ss.fff  pid  tid L Tag: msg"의 Tag(형식이 아니면 빈 문자열)</summary>
        static string TagOf(string line)
        {
            int i = 18;
            for (int field = 0; field < 2; field++) // pid, tid
            {
                while (i < line.Length && line[i] == ' ') i++;
                while (i < line.Length && char.IsDigit(line[i])) i++;
            }
            while (i < line.Length && line[i] == ' ') i++;
            i++; // 레벨 글자(V/D/I/W/E/F)
            if (i >= line.Length) return "";
            int colon = line.IndexOf(':', i);
            return colon < 0 ? "" : line.Substring(i, colon - i).Trim();
        }

        /// <summary>now 기준 최근 windowSec 안에 pid(0 = 모두)가 찍은 signature 줄 수(미래 시각 줄·10-29 자기 로그 줄은 뺌)</summary>
        public static int CountRecent(IReadOnlyList<string> lines, string signature, int pid, DateTime now, double windowSec = WindowSeconds)
        {
            if (lines == null || string.IsNullOrEmpty(signature)) return 0;
            int n = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l == null || l.IndexOf(signature, StringComparison.Ordinal) < 0) continue;
                if (IsOwnLog(l)) continue; // 10-29 감사: 자기 로그 오탐 방지
                if (!TryParse(l, now.Year, out var t, out int p)) continue;
                if (t > now.AddDays(1)) t = t.AddYears(-1); // 해 넘김(12-31 → 01-01)
                if (pid != 0 && p != pid) continue;
                double age = (now - t).TotalSeconds;
                if (age >= 0 && age <= windowSec) n++;
            }
            return n;
        }

        /// <summary>발화 = 최근 5초 안 서명 줄 ≥ 3</summary>
        public static bool ShouldFire(IReadOnlyList<string> lines, string signature, int pid, DateTime now)
            => CountRecent(lines, signature, pid, now) >= FireCount;
    }

    /// <summary>
    /// Records output health independently of game resets. Re-aligns content after wear/resume;
    /// native phone entry can restart a stuck XR process without relying on Unity rendering.
    /// Does not restart the native XR loader in-place (SDK owns that lifecycle).
    /// </summary>
    public sealed class XrRecoveryWatch : MonoBehaviour
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        static XrRecoveryWatch instance;
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        float nextSample, alignAt = -1f, alignDeadline;
        string lastDiagnostic;
        PrizeLink subscribedPrize;
        AndroidJavaClass bridge;
        AndroidJavaObject context;
        Unity.XR.XREAL.XRDisplayListener displayListener;
        bool displayListenerAdded; // 10-29 감사: SDK 중재자가 Awake 때 없으면 Update에서 늦게 등록
        const int MaxLinkLines = 400; // 10-29 감사: 서명 줄 보관 상한(넘는 줄은 버리며 EOF까지 읽음)
        // 10-29 안경 링크 끊김 서명 감시(백그라운드 스레드가 2초마다 자기 pid logcat 마지막 400줄을 읽어 최근 5초 서명 줄 수를 낸다)
        // 11-10(2) 실행 앱에 언어 선택 단계가 생겨 경로에 넣음 — 버튼 이름은 시작 화면(BeamEntryActivity)과 같게
        static string LinkLostNotice => Loc.T("안경 화면 연결이 끊어졌어요\n<size=70%>Beam Pro 앱 목록 → 실행 앱 → 언어 선택 → [처음부터 다시 실행]</size>",
                                              "Glasses display link lost\n<size=70%>Beam Pro app list → launcher app → choose language → [Restart from scratch]</size>"); // 11-10 Loc
        System.Threading.Thread linkThread;
        volatile bool linkStop;
        volatile int linkCount = -1;       // 최근 5초 서명 줄 수(−1 = 아직 없음)
        volatile string linkSignature = XrLinkLoss.DefaultSignature;
        volatile string linkError;         // 읽기 실패(권한 등) — 감시 끔
        bool linkLost, linkErrorLogged;
        float linkQuietSince = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("XrRecoveryWatch");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<XrRecoveryWatch>();
        }

        void Awake()
        {
            ClawWearWatch.Changed += WearChanged;
            SceneManager.sceneLoaded += SceneLoaded;
            Unity.XR.XREAL.XREALCallbackHandler.OnXREALGlassesScreenStatus += ScreenChanged;
            try
            {
                bridge = new AndroidJavaClass("com.hroov.souvenirhunt.bridge.BeamEntryActivity");
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                context = activity.Call<AndroidJavaObject>("getApplicationContext");
                displayListener = new Unity.XR.XREAL.XRDisplayListener(
                    (id, w, h) => Unity.XR.XREAL.XREALMainThreadDispatcher.Singleton.QueueOnMainThread(() =>
                        { Debug.Log($"[XRHealth] display added id={id} {w}x{h}"); QueueAlignment("display added"); }),
                    id => Debug.LogWarning($"[XRHealth] display removed id={id}"));
                TryAddDisplayListener(false);
            }
            catch (Exception e) { Debug.LogWarning("[XRHealth] phone bridge unavailable: " + e.Message); }
            QueueAlignment("boot");
            StartLinkWatch();
        }

        // ─ 10-29 안경 링크 끊김 서명 감시(안내만 — 자동 재시작 없음) ─
        void StartLinkWatch()
        {
            int pid;
            try { using var proc = new AndroidJavaClass("android.os.Process"); pid = proc.CallStatic<int>("myPid"); }
            catch (Exception e) { Debug.LogWarning("[XRHealth] link watch off — pid: " + e.Message); return; }
            linkThread = new System.Threading.Thread(() => LinkLoop(pid)) { IsBackground = true, Name = "XrLinkWatch" };
            linkThread.Start();
        }

        void LinkLoop(int pid)
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                var lines = new List<string>(MaxLinkLines);
                string[] cmd = { "logcat", "-d", "-v", "threadtime", "--pid=" + pid, "-t", "400" };
                // 10-29 감사 I5: 비교 기준 시각 = Java(currentTimeMillis + 기기 시간대) — logcat threadtime(기기 현지 시각)과 같은 기준.
                // 예전 DateTime.Now(IL2CPP 시간대)가 어긋나면 조용히 영원히 미발화할 수 있었다
                using var javaSystem = new AndroidJavaClass("java.lang.System");
                using var javaTimeZone = new AndroidJavaClass("java.util.TimeZone");
                var start = JavaLocalNow(javaSystem, javaTimeZone, out int offsetMs);
                Debug.Log($"[XRHealth] link watch tz offset={FormatOffset(offsetMs)} now={start:MM-dd HH:mm:ss.fff}");
                while (!linkStop)
                {
                    string sig = linkSignature;
                    if (string.IsNullOrEmpty(sig)) { linkCount = 0; System.Threading.Thread.Sleep(2000); continue; }
                    lines.Clear();
                    if (!ReadLogcat(cmd, sig, lines)) return;
                    linkCount = XrLinkLoss.CountRecent(lines, sig, pid, JavaLocalNow(javaSystem, javaTimeZone, out _));
                    System.Threading.Thread.Sleep(2000);
                }
            }
            catch (Exception e) { linkCount = 0; linkError = e.GetType().Name + ": " + e.Message; } // 10-29 감사: 스레드 밖으로 예외를 내지 않고 감시 끔
            finally { AndroidJNI.DetachCurrentThread(); }
        }

        /// <summary>
        /// 10-29 감사: logcat 출력을 EOF까지 읽는다(상한 넘는 줄은 버리며 읽기 — 예전 2000줄 상한에서 멈추면 파이프가 차 logcat·waitFor가 멈출 수 있었다).
        /// 끝나면 스트림 닫기·프로세스 정리(destroy). 실패하면 linkCount 0·linkError → 메인 스레드가 감시 끔·안내 해제
        /// </summary>
        bool ReadLogcat(string[] cmd, string sig, List<string> lines)
        {
            AndroidJavaObject pb = null, process = null, stream = null, isr = null, reader = null;
            try
            {
                pb = new AndroidJavaObject("java.lang.ProcessBuilder", (object)cmd);
                using (pb.Call<AndroidJavaObject>("redirectErrorStream", true)) { }
                process = pb.Call<AndroidJavaObject>("start");
                stream = process.Call<AndroidJavaObject>("getInputStream");
                isr = new AndroidJavaObject("java.io.InputStreamReader", stream);
                reader = new AndroidJavaObject("java.io.BufferedReader", isr);
                while (true)
                {
                    string line = reader.Call<string>("readLine");
                    if (line == null) break;
                    if (lines.Count < MaxLinkLines && line.IndexOf(sig, StringComparison.Ordinal) >= 0) lines.Add(line);
                }
                return true;
            }
            catch (Exception e) { linkCount = 0; linkError = e.Message; return false; }
            finally
            {
                try { reader?.Call("close"); } catch (Exception) { }
                try { process?.Call("destroy"); } catch (Exception) { }
                reader?.Dispose(); isr?.Dispose(); stream?.Dispose(); process?.Dispose(); pb?.Dispose();
            }
        }

        /// <summary>10-29 감사 I5: 기기 현지 시각(Java System.currentTimeMillis + TimeZone.getDefault().getOffset(now))</summary>
        static DateTime JavaLocalNow(AndroidJavaClass system, AndroidJavaClass timeZone, out int offsetMs)
        {
            long ms = system.CallStatic<long>("currentTimeMillis");
            using var tz = timeZone.CallStatic<AndroidJavaObject>("getDefault");
            offsetMs = tz.Call<int>("getOffset", ms);
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMilliseconds((double)ms + offsetMs);
        }

        static string FormatOffset(int ms)
        {
            int m = Math.Abs(ms) / 60000;
            return (ms < 0 ? "-" : "+") + (m / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>10-29 감사: 디스플레이 리스너 등록 — Awake 때 SDK 중재자(XREALMultiResumeMediator, SDK OnLoad가 만듦)가 아직 없으면 Update에서 늦게(성공하면 1회 로그)</summary>
        void TryAddDisplayListener(bool late)
        {
            var mediator = Unity.XR.XREAL.XREALMultiResumeMediator.Singleton;
            if (displayListener == null || mediator == null) return;
            try
            {
                mediator.AddXRDisplayListener(displayListener);
                displayListenerAdded = true;
                if (late) Debug.Log("[XRHealth] display listener registered (late)");
            }
            catch (Exception e) { displayListener = null; Debug.LogWarning("[XRHealth] display listener failed: " + e.Message); }
        }

        /// <summary>메인 스레드: 서명 줄 수로 안내 켜고 끄기. 끈 뒤 10초 조용하면 안내를 내린다(오탐 대비)</summary>
        void TickLinkWatch()
        {
            var cfgSig = GameSession.Current?.Config?.clawLinkLossSignature;
            if (cfgSig != null) linkSignature = cfgSig.Trim();
            if (linkError != null)
            {
                if (!linkErrorLogged)
                {
                    linkErrorLogged = true;
                    Debug.LogWarning("[XRHealth] link watch off — logcat read failed: " + linkError);
                    // 10-29 감사: 감시가 꺼지면 켜져 있던 안내를 바로 내린다(영구 고착 방지)
                    if (linkLost)
                    {
                        linkLost = false;
                        linkQuietSince = -1f;
                        ClawPadInput.SetPriorityNotice("");
                        Publish();
                    }
                }
                return;
            }
            int n = linkCount;
            if (n < 0) return;
            if (!linkLost && n >= XrLinkLoss.FireCount)
            {
                linkLost = true;
                linkQuietSince = -1f;
                Debug.LogWarning($"[XRHealth] glasses link lost ({n}/5s)");
                ClawPadInput.SetPriorityNotice(LinkLostNotice);
                Publish();
            }
            else if (linkLost)
            {
                if (n > 0) linkQuietSince = -1f;
                else if (linkQuietSince < 0f) linkQuietSince = Time.unscaledTime;
                else if (Time.unscaledTime - linkQuietSince >= 10f)
                {
                    linkLost = false;
                    Debug.Log("[XRHealth] glasses link signature quiet 10s — notice cleared");
                    ClawPadInput.SetPriorityNotice("");
                    Publish();
                }
            }
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode) => QueueAlignment("scene " + scene.name);
        void WearChanged(ClawWearWatch.WearState state)
        {
            if (state == ClawWearWatch.WearState.On) QueueAlignment("worn");
        }
        void ScreenChanged(Unity.XR.XREAL.XREALDisplayState status)
        {
            Debug.Log("[XRHealth] glasses screen=" + status);
            QueueAlignment("screen event");
        }

        void OnApplicationPause(bool pause)
        {
            Debug.Log("[XRHealth] pause=" + pause);
            Publish();
            if (!pause) QueueAlignment("resume");
        }

        void QueueAlignment(string reason)
        {
            alignAt = Time.unscaledTime + 0.8f;
            alignDeadline = Time.unscaledTime + 8f;
            Debug.Log("[XRHealth] wait for tracking before alignment: " + reason);
        }

        void Update()
        {
            if (!displayListenerAdded && displayListener != null) TryAddDisplayListener(true); // 10-29 감사: 지연 등록
            var prize = ClawSessionBase.Instance != null ? ClawSessionBase.Instance.Prize : null;
            if (prize != subscribedPrize)
            {
                if (subscribedPrize != null) subscribedPrize.OnChanged -= Publish;
                subscribedPrize = prize;
                if (subscribedPrize != null) subscribedPrize.OnChanged += Publish;
            }
            if (Time.unscaledTime >= nextSample) { nextSample = Time.unscaledTime + 1f; Publish(); }
            TickLinkWatch();
            if (alignAt < 0f || Time.unscaledTime < alignAt) return;
            if (Time.unscaledTime > alignDeadline) { alignAt = -1f; return; }
            if (ClawSceneRouter.Loading || ClawWearWatch.State == ClawWearWatch.WearState.Off) return;
            var camera = Camera.main;
            if (camera == null || !camera.isActiveAndEnabled) return;
            InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked);
            if (!tracked) return;
            var session = GameSession.Current;
            if (session is ClawIntro intro) intro.OnRecentered();
            else if (session is ClawSessionBase game)
            {
                // 10-28 Reveal에는 기계 다가오기(약 1.5초)도 포함 — 끝나 Guide가 되면 여기서 재정렬(검정 0.6 + 디졸브 3.8 + 다가오기 1.5 = 5.9초 < 8초 마감)
                if (game.Current == ClawSessionBase.State.Countdown || game.Current == ClawSessionBase.State.Reveal) return;
                game.Recenter();
            }
            else return;
            alignAt = -1f;
            Debug.Log("[XRHealth] tracked pose restored; content aligned");
        }

        void Publish()
        {
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            int running = 0;
            foreach (var d in displays) if (d.running) running++;
            var camera = Camera.main;
            string diagnostic = $"xrDisplays={displays.Count} running={running} camera={(camera != null && camera.isActiveAndEnabled)} wear={ClawWearWatch.State} linkLost={linkLost}";
            if (diagnostic != lastDiagnostic)
            {
                lastDiagnostic = diagnostic;
                Debug.Log("[XRHealth] " + diagnostic);
            }
            try
            {
                var session = ClawSessionBase.Instance;
                bridge?.CallStatic("publish", context, GameSession.Current?.StateName ?? "Loading",
                    session != null && session.PrizeSending, diagnostic, linkLost); // 10-29 linkLost = 시작 화면이 [처음부터 다시 실행] 강조
            }
            catch (Exception e) { Debug.LogWarning("[XRHealth] publish failed: " + e.Message); }
        }

        void OnDestroy()
        {
            linkStop = true;
            ClawWearWatch.Changed -= WearChanged;
            SceneManager.sceneLoaded -= SceneLoaded;
            Unity.XR.XREAL.XREALCallbackHandler.OnXREALGlassesScreenStatus -= ScreenChanged;
            if (subscribedPrize != null) subscribedPrize.OnChanged -= Publish;
            if (displayListenerAdded && displayListener != null) Unity.XR.XREAL.XREALMultiResumeMediator.Singleton?.RemoveXRDisplayListener(displayListener);
            bridge?.Dispose(); context?.Dispose();
            if (instance == this) instance = null;
        }
#endif
    }
}
