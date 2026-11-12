using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace SouvenirHunt
{
    /// <summary>
    /// 렌더 품질 A/B (Phase 5-0). config.txt의 bloom / renderScale / hdr 값을 부팅·리셋마다 런타임에 적용해
    /// 빌드 없이 기기에서 60FPS 조합을 찾는다. 인수인계 §4-7 분리 순서: Bloom → RenderScale → HDR.
    /// 적용 결과는 "[Render]" 로그와 운영자 패널에 표시. URP 에셋 값은 프로세스 안에서만 바뀐다(에셋 파일 불변).
    /// 세션은 GameSession.Current(Main=GameManager, Claw=ClawGameManager) — 씬 무관(09-03).
    /// </summary>
    public class RenderQualityApplier : MonoBehaviour
    {
        public static RenderQualityApplier Instance { get; private set; }

        /// <summary>마지막 적용 결과 1줄 (운영자 패널용)</summary>
        public string Summary { get; private set; } = "렌더: 미적용";

        IGameSession gm;
        bool bound;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            GameSession.Changed += OnSessionChanged;
            TryBind();
        }

        void Start() => TryBind();

        void OnDisable()
        {
            GameSession.Changed -= OnSessionChanged;
            Unbind();
        }

        // 10-15 성능 조사: XR이 실제로 그리는 눈 텍스처 크기를 확인 — renderScale 0.6에서도 FPS가 그대로여서
        // XREAL이 URP renderScale(XR 디스플레이 scaleOfAllRenderTargets)을 무시하는지 의심. 시작 10초 뒤 1번 더 로그(그때는 XR이 확실히 그리는 중).
        float xrLogAt = 10f;

        void Update()
        {
            if (xrLogAt < 0f || Time.unscaledTime < xrLogAt) return;
            xrLogAt = -1f;
            Debug.Log("[Render] " + XrInfo());
        }

        static readonly List<XRDisplaySubsystem> displays = new();

        /// <summary>XR 디스플레이 뷰포트 배율(config xrViewportScale) 적용. 1이면 손대지 않음(원래 값 유지)</summary>
        static void ApplyViewportScale(float scale)
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
                if (d.running && (scale < 0.999f || d.scaleOfAllViewports < 0.999f) && !Mathf.Approximately(d.scaleOfAllViewports, scale))
                    d.scaleOfAllViewports = scale;
        }

        static string XrInfo()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                if (!d.running) continue;
                string rt = "패스 없음";
                int passes = d.GetRenderPassCount();
                if (passes > 0)
                {
                    d.GetRenderPass(0, out var p);
                    var t = p.renderTargetDesc;
                    rt = $"{t.width}×{t.height}×{t.volumeDepth} {t.graphicsFormat} msaa{t.msaaSamples}";
                }
                return $"XR 눈 텍스처 {rt} (패스 {passes}) · RT배율 {d.scaleOfAllRenderTargets:0.00} · 뷰포트배율 {d.scaleOfAllViewports:0.00}(적용 {d.appliedViewportScale:0.00}) · " +
                       $"eye {XRSettings.eyeTextureWidth}×{XRSettings.eyeTextureHeight} · 모드 {XRSettings.stereoRenderingMode}";
            }
            return "XR 디스플레이 없음(에디터·비XR)";
        }

        void OnSessionChanged(IGameSession _)
        {
            Unbind();
            TryBind();
        }

        void Unbind()
        {
            if (bound && gm != null) gm.OnGameReset -= Apply;
            bound = false;
            gm = null;
        }

        void TryBind()
        {
            if (bound) return;
            gm = GameSession.Current;
            if (gm == null) return;
            gm.OnGameReset += Apply;
            bound = true;
            if (gm.Config != null) Apply(); // 세션의 Start(ResetGame)가 먼저 지나간 경우
        }

        void Apply()
        {
            var cfg = gm != null ? gm.Config : null;
            if (cfg == null) return;

            try
            {
                var cam = Camera.main;
                string camState = "카메라 없음";
                if (cam != null)
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    data.renderPostProcessing = cfg.bloom;
                    camState = cfg.bloom ? "ON" : "OFF";
                }

                var rp = UniversalRenderPipeline.asset;
                string rpState = "URP 에셋 없음";
                if (rp != null)
                {
                    // renderScale은 URP 동적 해상도로 런타임 변경이 안전 — 값이 다를 때만 적용(불필요한 RT 재생성 회피).
                    if (!Mathf.Approximately(rp.renderScale, cfg.renderScale))
                        rp.renderScale = cfg.renderScale;
                    // supportsHDR는 런타임에 대입하면 XR 스왑체인 포맷이 재생성돼 컴포지터가 프레임을 드롭한다(글래스 검은 화면).
                    // → HDR은 빌드타임 URP 에셋 값으로만 고정(에디터 메뉴 17이 관리). 여기서는 절대 대입하지 않는다.
                    rpState = $"Scale {rp.renderScale:0.00} · HDR {(rp.supportsHDR ? "ON" : "OFF")}(빌드 고정)";
                    // config의 hdr가 빌드 값과 다르면 운영자에게 "반영 안 됨"을 알린다 (§4-2 결과 노출)
                    if (cfg.hdr != rp.supportsHDR)
                        cfg.warnings.Add($"hdr={cfg.hdr}는 런타임 반영 불가 — 빌드 고정값 {(rp.supportsHDR ? "true" : "false")} 유지");
                }

                ApplyViewportScale(cfg.xrViewportScale);
                Summary = $"렌더: Bloom {camState} · {rpState}" + (cfg.xrViewportScale < 0.999f ? $" · XR뷰포트 {cfg.xrViewportScale:0.00}" : "");
                Debug.Log($"[Render] bloom={cfg.bloom} renderScale={cfg.renderScale:0.00} xrViewportScale={cfg.xrViewportScale:0.00} (hdr는 빌드고정, 런타임 미변경) → {Summary} · {XrInfo()}");
            }
            catch (System.Exception e)
            {
                // 렌더 설정 실패가 게임을 멈추면 안 된다 — 기본 렌더로 계속
                Summary = "렌더: 적용 실패(기본값 유지)";
                Debug.LogWarning("[Render] 적용 실패: " + e.Message);
            }
        }
    }
}
