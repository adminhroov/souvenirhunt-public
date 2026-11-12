using UnityEngine;
using UnityEngine.Rendering;

namespace SouvenirHunt
{
    /// <summary>
    /// 조명 묶음(10-20 자연광 1단계) — 해(방향광) 방향·색·세기 + 주변광(환경 이미지를 구면 조화로 미리 계산) + 반사 큐브맵·세기.
    /// 메뉴 30(SouvenirHuntClawLighting.cs)이 낮 하늘(코드 생성)·실사 HDRI에서 만든다. ClawLighting이 config clawLighting으로 골라 적용.
    /// 밝기는 지금 조명(studio)과 같은 평균이 되도록 맞춰 둔다(OST는 어두우면 투명).
    /// </summary>
    public class ClawLightingPreset : ScriptableObject
    {
        public string id = "daylight";
        public string label = "";
        public Vector3 sunEuler = new Vector3(50f, -30f, 0f);
        public Color sunColor = Color.white;
        public float sunIntensity = 1f;
        /// <summary>구면 조화 L2 계수 [채널 r·g·b][계수 0~8] — RenderSettings.ambientProbe</summary>
        public float[] ambientSH = new float[27];
        /// <summary>주변광 배율 — 적용 때 studio 평균 밝기에 맞춘 뒤 곱한다(1 = 같은 밝기)</summary>
        public float ambientScale = 1f;
        public Cubemap reflection;
        public float reflectionIntensity = 1f;
        /// <summary>원본 파일 해시(바뀌었을 때만 다시 계산)</summary>
        public string sourceHash = "";

        public SphericalHarmonicsL2 AmbientProbe
        {
            get
            {
                var sh = new SphericalHarmonicsL2();
                if (ambientSH != null && ambientSH.Length == 27)
                    for (int c = 0; c < 3; c++)
                        for (int k = 0; k < 9; k++)
                            sh[c, k] = ambientSH[c * 9 + k];
                return sh;
            }
            set
            {
                ambientSH = new float[27];
                for (int c = 0; c < 3; c++)
                    for (int k = 0; k < 9; k++)
                        ambientSH[c * 9 + k] = value[c, k];
            }
        }
    }
}
