using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace SouvenirHunt
{
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
                Unity.XR.XREAL.XREALMultiResumeMediator.Singleton?.AddXRDisplayListener(displayListener);
            }
            catch (Exception e) { Debug.LogWarning("[XRHealth] phone bridge unavailable: " + e.Message); }
            QueueAlignment("boot");
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
            var prize = ClawSessionBase.Instance != null ? ClawSessionBase.Instance.Prize : null;
            if (prize != subscribedPrize)
            {
                if (subscribedPrize != null) subscribedPrize.OnChanged -= Publish;
                subscribedPrize = prize;
                if (subscribedPrize != null) subscribedPrize.OnChanged += Publish;
            }
            if (Time.unscaledTime >= nextSample) { nextSample = Time.unscaledTime + 1f; Publish(); }
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
            string diagnostic = $"xrDisplays={displays.Count} running={running} camera={(camera != null && camera.isActiveAndEnabled)} wear={ClawWearWatch.State}";
            if (diagnostic != lastDiagnostic)
            {
                lastDiagnostic = diagnostic;
                Debug.Log("[XRHealth] " + diagnostic);
            }
            try
            {
                var session = ClawSessionBase.Instance;
                bridge?.CallStatic("publish", context, GameSession.Current?.StateName ?? "Loading",
                    session != null && session.PrizeSending, diagnostic);
            }
            catch (Exception e) { Debug.LogWarning("[XRHealth] publish failed: " + e.Message); }
        }

        void OnDestroy()
        {
            ClawWearWatch.Changed -= WearChanged;
            SceneManager.sceneLoaded -= SceneLoaded;
            Unity.XR.XREAL.XREALCallbackHandler.OnXREALGlassesScreenStatus -= ScreenChanged;
            if (subscribedPrize != null) subscribedPrize.OnChanged -= Publish;
            if (displayListener != null) Unity.XR.XREAL.XREALMultiResumeMediator.Singleton?.RemoveXRDisplayListener(displayListener);
            bridge?.Dispose(); context?.Dispose();
            if (instance == this) instance = null;
        }
#endif
    }
}
