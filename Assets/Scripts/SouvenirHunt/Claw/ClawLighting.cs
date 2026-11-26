using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 조명 전환(10-20 자연광 1단계, 사용자 "자연광 느낌" → 코드 생성 낮 하늘·실사 HDRI 둘 다 비교).
    /// config clawLighting = studio(지금 조명 — 씬 시작 값 그대로) | daylight(코드로 만든 낮 하늘) | lebombo(실내 자연 채광 HDRI) | warehouse(형광등+창 HDRI).
    /// 바꾸는 것: 해(방향광) 방향·색·세기, 주변광(구면 조화), 반사 큐브맵·세기. 화면 배경은 그대로 검정(OST 투명).
    /// 리셋마다 config대로(같으면 아무 것도 안 함). 예외를 던지지 않는다 — 실패하면 지금 조명 유지.
    /// </summary>
    public class ClawLighting : MonoBehaviour
    {
        public static ClawLighting Instance { get; private set; }
        public static readonly string[] Ids = { "studio", "daylight", "lebombo", "warehouse" };

        [SerializeField] Light sun;
        [SerializeField] ClawLightingPreset[] presets;

        public string Current { get; private set; } = "studio";
        public string Summary { get; private set; } = "조명 studio";

        // studio = 씬이 시작할 때의 값(메뉴 17이 만든 트라이라이트 주변광·스튜디오 반사·해)
        Quaternion baseRot;
        Color baseColor;
        float baseIntensity, baseReflIntensity;
        AmbientMode baseMode;
        SphericalHarmonicsL2 baseProbe;
        DefaultReflectionMode baseReflMode;
        Texture baseRefl;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (sun == null) sun = GetComponent<Light>();
            if (sun != null) { baseRot = sun.transform.rotation; baseColor = sun.color; baseIntensity = sun.intensity; }
            baseMode = RenderSettings.ambientMode;
            baseProbe = RenderSettings.ambientProbe;
            baseReflMode = RenderSettings.defaultReflectionMode;
            baseRefl = RenderSettings.customReflectionTexture;
            baseReflIntensity = RenderSettings.reflectionIntensity;
            baseMean = MeanIrradiance(baseProbe);
            AdoptFirstLoadBase();
        }

        // 11-26 [다시 시작]으로 이 씬을 같은 프로세스에서 두 번째 불러오면 부스·기계·상품이 전부 검게(OST에선 안 보임) 나오던 문제(안경 캡처로 확인:
        // 조명 없는 네온·바닥 격자·글씨만 보임). 주변광 밝기 배율 k는 씬 시작 때 읽은 RenderSettings.ambientProbe 평균에서 나오는데, 다시 불러온 씬에서는
        // 그 값이 첫 로드와 다를 수 있다 → 프로세스 첫 로드의 기준(studio) 값을 기억해 두고, 이후 로드에서 읽은 값이 이상하거나 다르면 첫 로드 값을 쓴다.
        static bool firstValid;
        static AmbientMode firstMode;
        static SphericalHarmonicsL2 firstProbe;
        static float firstMean;
        float baseMean;

        static float Lum(Color c) { c = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c; return c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f; }

        /// <summary>
        /// 트라이라이트 색(씬 설정)에서 계산한 주변광 평균 — MeanIrradiance와 같은 14방향 가중(위 1·아래 1·수평 4·대각 8).
        /// 11-26 기기에서 씬 시작 때 읽는 RenderSettings.ambientProbe는 0·NaN·2.66으로 로드마다 달라 기준으로 못 쓴다(로그) → 밝기 기준은 항상 이 값.
        /// 색은 선형 공간으로 바꿔 계산(프로젝트가 Linear — 색 값 그대로 평균하면 2배 넘게 밝다)
        /// </summary>
        static float TrilightMean()
        {
            const float d = 0.57735f; // 대각 방향의 y
            float sky = Lum(RenderSettings.ambientSkyColor), eq = Lum(RenderSettings.ambientEquatorColor), gr = Lum(RenderSettings.ambientGroundColor);
            return (sky * (1f + 4f * d) + gr * (1f + 4f * d) + eq * (4f + 8f * (1f - d))) / 14f;
        }

        static bool SaneBase(float mean) => !float.IsNaN(mean) && !float.IsInfinity(mean) && mean > 0.03f && mean < 1.5f;

        void AdoptFirstLoadBase()
        {
            float read = baseMean;
            if (!firstValid)
            {
                float tri = TrilightMean();
                firstMean = SaneBase(tri) ? tri : 0.2f;
                firstMode = baseMode;
                firstProbe = baseProbe;
                firstValid = true;
                Debug.Log($"[Lighting] 기준 주변광(첫 로드) 색에서 계산 {tri:F3} → 사용 {firstMean:F3} · 읽은 값 {read:F3} mode {baseMode}");
            }
            else Debug.Log($"[Lighting] 기준 주변광(다시 로드) 사용 {firstMean:F3}(첫 로드 값) · 읽은 값 {read:F3} mode {baseMode}");
            baseMean = firstMean;
        }

        static readonly Vector3[] SampleDirs =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
            new Vector3(1, 1, 1).normalized, new Vector3(-1, 1, 1).normalized, new Vector3(1, 1, -1).normalized, new Vector3(-1, 1, -1).normalized,
            new Vector3(1, -1, 1).normalized, new Vector3(-1, -1, 1).normalized, new Vector3(1, -1, -1).normalized, new Vector3(-1, -1, -1).normalized,
        };

        static float MeanIrradiance(SphericalHarmonicsL2 sh)
        {
            var res = new Color[SampleDirs.Length];
            sh.Evaluate(SampleDirs, res);
            float s = 0f;
            foreach (var c in res) s += c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            return s / res.Length;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static string Normalize(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(Ids, v) >= 0 ? v : "daylight"; // 10-29 감사: 오타는 GameConfig 기본값 daylight로(예전 studio)
        }

        ClawLightingPreset Find(string id)
        {
            if (presets == null) return null;
            foreach (var p in presets) if (p != null && p.id == id) return p;
            return null;
        }

        public void Apply(string id)
        {
            id = Normalize(id);
            if (id == Current) return;
            try
            {
                if (id == "studio")
                {
                    if (sun != null) { sun.transform.rotation = baseRot; sun.color = baseColor; sun.intensity = baseIntensity; }
                    RenderSettings.ambientMode = baseMode;
                    RenderSettings.ambientProbe = baseProbe;
                    RenderSettings.defaultReflectionMode = baseReflMode;
                    RenderSettings.customReflectionTexture = baseRefl;
                    RenderSettings.reflectionIntensity = baseReflIntensity;
                    Current = id;
                    Summary = "조명 studio(지금 조명)";
                }
                else
                {
                    var p = Find(id);
                    if (p == null) { Debug.LogWarning($"[Lighting] 조명 묶음 '{id}' 없음 — {Current} 유지(메뉴 30 재실행 필요)"); return; }
                    if (sun != null) { sun.transform.rotation = Quaternion.Euler(p.sunEuler); sun.color = p.sunColor; sun.intensity = p.sunIntensity; }
                    // 밝기는 씬의 지금 주변광(studio, Unity가 트라이라이트에서 만든 값)과 같은 평균으로 — 모양(색·방향 차이)만 묶음에서
                    var sh = p.AmbientProbe;
                    float k = baseMean / Mathf.Max(1e-4f, MeanIrradiance(sh));
                    if (float.IsNaN(k) || float.IsInfinity(k) || k <= 0f) { Debug.LogWarning($"[Lighting] 주변광 배율 {k} 이상 → 1"); k = 1f; }
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    RenderSettings.ambientProbe = sh * (k * Mathf.Max(0f, p.ambientScale));
                    Debug.Log($"[Lighting] 주변광 배율 {k:F3} → 적용 평균 {MeanIrradiance(RenderSettings.ambientProbe):F3} (해 켜짐 {(sun != null && sun.isActiveAndEnabled)})");
                    if (p.reflection != null)
                    {
                        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                        RenderSettings.customReflectionTexture = p.reflection;
                    }
                    RenderSettings.reflectionIntensity = p.reflectionIntensity;
                    Current = id;
                    Summary = $"조명 {id}({p.label})";
                }
                Debug.Log($"[Lighting] {Summary} — 해 {(sun != null ? $"{sun.transform.eulerAngles.x:F0}°/{sun.transform.eulerAngles.y:F0}° 세기 {sun.intensity:F2}" : "없음")}, 반사 세기 {RenderSettings.reflectionIntensity:F2}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Lighting] 적용 실패(무시): " + e.Message);
            }
        }
    }
}
