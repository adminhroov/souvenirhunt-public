using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 프레임 성능 측정 (Phase 4, 인수인계 §8 "60FPS 유지" 판정 근거).
    /// 1초 구간 평균/최악 FPS와 최근 60초 창의 평균·최저를 유지하고, logIntervalSec마다 "[Perf]" 로그를 남긴다
    /// (실기기: adb logcat -s Unity | findstr Perf). Application.targetFrameRate는 건드리지 않음 — XR 런타임이 페이싱.
    /// 무할당: 링버퍼 고정 배열, 로그 문자열만 5초에 1회.
    /// 10-15: 로그 간격 동안의 CPU 메인·렌더 스레드·GPU 프레임 시간 평균(FrameTimingManager, PlayerSettings 'Frame Timing Stats' 필요)도 붙인다 —
    /// FPS가 낮을 때 어디가 병목인지(cpu/render/gpu) 바로 보이게. 지원 안 되면(0) 생략.
    /// </summary>
    public class PerfMonitor : MonoBehaviour
    {
        public static PerfMonitor Instance { get; private set; }

        [Tooltip("logcat용 [Perf] 로그 간격(초). 0이면 로그 안 함")]
        [SerializeField] float logIntervalSec = 5f;

        /// <summary>직전 1초 구간 평균 FPS</summary>
        public float CurrentFps { get; private set; }
        /// <summary>직전 1초 구간의 최악 프레임 기준 FPS(1/최장 프레임)</summary>
        public float CurrentMinFps { get; private set; }
        /// <summary>직전 1초 구간의 최장 프레임(ms)</summary>
        public float WorstFrameMs { get; private set; }
        /// <summary>최근 60초 구간 평균 FPS</summary>
        public float Avg60 { get; private set; }
        /// <summary>최근 60초 중 최저 1초 평균 FPS</summary>
        public float Min60 { get; private set; }
        /// <summary>측정된 1초 구간 수(최대 60)</summary>
        public int SampleCount => ringCount;

        const int RingSize = 60;
        readonly float[] avgRing = new float[RingSize];
        int ringHead, ringCount;

        float accum, worst, logTimer;
        int frames;
        readonly FrameTiming[] timing = new FrameTiming[1];
        double tMain, tRender, tGpu;
        int tCount;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            accum += dt;
            frames++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                tMain += timing[0].cpuMainThreadFrameTime;
                tRender += timing[0].cpuRenderThreadFrameTime;
                tGpu += timing[0].gpuFrameTime;
                tCount++;
            }
            if (dt > worst) worst = dt;
            logTimer += dt;

            if (accum >= 1f)
            {
                CurrentFps = frames / accum;
                CurrentMinFps = 1f / worst;
                WorstFrameMs = worst * 1000f;

                avgRing[ringHead] = CurrentFps;
                ringHead = (ringHead + 1) % RingSize;
                if (ringCount < RingSize) ringCount++;

                float sum = 0f, min = float.MaxValue;
                for (int i = 0; i < ringCount; i++)
                {
                    sum += avgRing[i];
                    if (avgRing[i] < min) min = avgRing[i];
                }
                Avg60 = sum / ringCount;
                Min60 = min;

                accum = 0f; frames = 0; worst = 0f;
            }

            if (logIntervalSec > 0f && logTimer >= logIntervalSec)
            {
                logTimer = 0f;
                string ft = tCount > 0 ? $" cpuMain={tMain / tCount:F1}ms cpuRender={tRender / tCount:F1}ms gpu={tGpu / tCount:F1}ms" : "";
                Debug.Log($"[Perf] fps={CurrentFps:F1} worstFrame={WorstFrameMs:F1}ms avg60={Avg60:F1} min60={Min60:F1} (n={ringCount}s){ft}");
                tMain = tRender = tGpu = 0; tCount = 0;
            }
        }

        /// <summary>운영자 패널·진단용 1줄 요약</summary>
        public string Summary()
        {
            if (ringCount == 0) return "측정 중...";
            return $"FPS 현재 {CurrentFps:F0} · 최근{ringCount}초 평균 {Avg60:F0} / 최저 {Min60:F0} · 최장 프레임 {WorstFrameMs:F1}ms";
        }
    }
}
