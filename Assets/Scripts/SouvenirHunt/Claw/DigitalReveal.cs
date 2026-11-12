using System;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 디지털 디졸브 전환(10-13, 연출안 STEP 04) — 영상이 끝나면 게임 공간(부스·기계)이 픽셀로 드러난다. 셰이더 SouvenirHunt/DigitalReveal.
    /// OST에서 검정 = 투명이라 "검은 화면" = 현실이 그대로 보이는 상태(사용자 의도). 눈을 감싼 돔이 깊이 무시로 모든 것을 덮는다:
    ///   ① 영상 → 검정(BlackSec)  ② 디지털 그리드 형성  ③ 픽셀 반짝임(분해 시작)  ④ 정면부터 픽셀로 부서지며 공간 드러남  ⑤ 완료
    /// 완전히 검어진 순간 onBlack — 세션이 그 뒤에서 부스 정렬·기계 표시·공 배치를 한다(가려진 동안이라 정착 히치도 안 보임).
    /// 돔은 시작 때 눈 위치·시선 yaw로 한 번 놓고 월드 고정(head-lock 아님). 재질은 공유 에셋 그대로, 값은 MaterialPropertyBlock.
    /// Update 구동(코루틴 아님) — 백그라운드에서 돌아오면 이어서 진행. 소리는 config audio=true일 때만.
    /// </summary>
    public class DigitalReveal : MonoBehaviour
    {
        [SerializeField] Renderer dome;
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip revealClip;

        [Header("시간(초)")]
        [SerializeField] float blackSec = 0.6f;     // ① 영상이 검게
        [SerializeField] float gridSec = 1.0f;      // ② 그리드 형성
        [SerializeField] float pixelSec = 0.6f;     // ③ 픽셀 반짝임(분해 시작) — ④와 겹침
        [SerializeField] float dissolveSec = 2.2f;  // ④ 공간 드러남(선형 — 시야 안 칸이 약 1.5초에 걸쳐 드러남)
        [SerializeField] float tailSec = 0.3f;      // ⑤ 잔광 정리

        static readonly int IdBlack = Shader.PropertyToID("_Black");
        static readonly int IdGrid = Shader.PropertyToID("_Grid");
        static readonly int IdPixels = Shader.PropertyToID("_Pixels");
        static readonly int IdProgress = Shader.PropertyToID("_Progress");

        public bool Running { get; private set; }
        /// <summary>onBlack이 이미 불렸는가(공간이 뒤에 준비됨)</summary>
        public bool BlackReached { get; private set; }
        /// <summary>현재 검정 정도 0~1 — 영상 소리 페이드용</summary>
        public float Black { get; private set; }
        public float Elapsed => t;
        public float Duration => blackSec + gridSec + pixelSec * 0.5f + dissolveSec + tailSec;
        /// <summary>디졸브 진행 0~1(④)</summary>
        public float Progress { get; private set; }

        MaterialPropertyBlock mpb;
        Action onBlack, onDone;
        float t;

        void Awake()
        {
            if (dome != null) dome.gameObject.SetActive(false);
        }

        /// <summary>시작 — 눈 위치·시선 yaw에 돔을 놓고 검게 시작</summary>
        public void Begin(Action black, Action done, bool audio)
        {
            onBlack = black;
            onDone = done;
            t = 0f;
            Black = 0f;
            Progress = 0f;
            BlackReached = false;
            Running = true;
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                var fwd = cam.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
                transform.SetPositionAndRotation(cam.position, Quaternion.LookRotation(fwd.normalized, Vector3.up));
            }
            Apply(0f, 0f, 0f, 0f);
            if (dome != null) dome.gameObject.SetActive(true);
            if (audio && audioSource != null && revealClip != null) audioSource.PlayOneShot(revealClip);
            Debug.Log($"[Cycle] REVEAL — 디지털 디졸브 전환 시작 ({Duration:F1}s)");
        }

        /// <summary>즉시 끔(운영자 리셋) — 콜백 없음</summary>
        public void Stop()
        {
            Running = false;
            onBlack = onDone = null;
            if (audioSource != null) audioSource.Stop();
            if (dome != null) dome.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!Running) return;
            t += Time.deltaTime;
            Tick();
        }

        void Tick()
        {
            float tg = blackSec, tp = tg + gridSec, td = tp + pixelSec * 0.5f, te = td + dissolveSec, tEnd = te + tailSec;
            Black = Smooth(t / Mathf.Max(0.01f, blackSec));
            float grid = Smooth((t - tg) / Mathf.Max(0.01f, gridSec));
            float pixels = Mathf.Clamp01((t - tp) / Mathf.Max(0.01f, pixelSec)) * (1f - Mathf.Clamp01((t - td) / Mathf.Max(0.01f, dissolveSec)));
            Progress = Mathf.Clamp01((t - td) / Mathf.Max(0.01f, dissolveSec));
            Apply(Black, grid, pixels, Progress);

            if (!BlackReached && t >= blackSec)
            {
                BlackReached = true;
                Fire(ref onBlack, "검정");
            }
            if (t >= tEnd)
            {
                Running = false;
                if (dome != null) dome.gameObject.SetActive(false);
                Debug.Log("[Cycle] REVEAL END — 공간 등장 완료");
                Fire(ref onDone, "완료");
            }
        }

        static void Fire(ref Action a, string what)
        {
            var cb = a;
            a = null;
            try { cb?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); Debug.LogWarning($"[Reveal] {what} 콜백 예외 — 전환은 계속"); }
        }

        void Apply(float black, float grid, float pixels, float progress)
        {
            if (dome == null) return;
            mpb ??= new MaterialPropertyBlock();
            dome.GetPropertyBlock(mpb);
            mpb.SetFloat(IdBlack, black);
            mpb.SetFloat(IdGrid, grid);
            mpb.SetFloat(IdPixels, pixels);
            mpb.SetFloat(IdProgress, progress);
            dome.SetPropertyBlock(mpb);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

#if UNITY_EDITOR
        /// <summary>셀프테스트 — 남은 단계를 즉시 끝낸다(검정 콜백 → 완료 콜백 순서 유지)</summary>
        public void EditorFinish()
        {
            if (!Running) return;
            t = Duration + 1f;
            Tick();
        }
#endif
    }
}
