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
                    float k = MeanIrradiance(baseProbe) / Mathf.Max(1e-4f, MeanIrradiance(sh));
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    RenderSettings.ambientProbe = sh * (k * Mathf.Max(0f, p.ambientScale));
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
