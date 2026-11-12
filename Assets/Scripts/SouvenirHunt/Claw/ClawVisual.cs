using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 비주얼 공용(10-15 비주얼 1차). 예외를 던지지 않는다.
    ///   · 아이템 셰이더(SouvenirHunt/ClawItemLit) 전역 세기 — config clawRim·clawReflection(빌드 없이 실기기에서 맞춤)
    ///   · 텍스처 이방성 필터 — 기계·부스 바닥을 비스듬히 볼 때 뭉개짐 완화. 임포트 설정을 바꾸면 안드로이드 텍스처를 다시 압축해야 해서
    ///     (Model3 아틀라스 2048×4096은 십수 분) 런타임에 텍스처 객체에만 적용한다. 품질 설정 anisotropicTextures = 텍스처별(Mobile 1)이라 유효.
    /// </summary>
    public static class ClawVisual
    {
        public const int AnisoLevel = 4;   // config clawAniso 기본값(10-15 성능: config로 낮출 수 있게)

        static int appliedAniso = -1;
        static readonly int RimDelta = Shader.PropertyToID("_ClawRimDelta");
        static readonly int ReflDelta = Shader.PropertyToID("_ClawReflDelta");
        static readonly int[] TexIds = { Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_MainTex") };

        /// <summary>셰이더 전역은 "값 − 1"로 넣는다 — 한 번도 설정하지 않은 상태(0)가 1배가 되게</summary>
        public static void ApplyGlobals(float rim, float reflection)
        {
            Shader.SetGlobalFloat(RimDelta, Mathf.Max(0f, rim) - 1f);
            Shader.SetGlobalFloat(ReflDelta, Mathf.Max(0f, reflection) - 1f);
        }

        /// <summary>씬을 새로 불러왔을 때(텍스처가 다시 로드될 수 있음) 다음 ApplyAnisotropic이 반드시 다시 훑게</summary>
        public static void InvalidateAniso() => appliedAniso = -1;

        /// <summary>씬의 메시 렌더러(숨김 포함) 재질 텍스처 이방성 필터를 level로(0~16, 높을수록 비스듬한 면이 선명·무거움).
        /// 같은 값이 이미 적용됐으면 아무 것도 안 한다(리셋마다 호출). 반환 = 바꾼 텍스처 수</summary>
        public static int ApplyAnisotropic(int level)
        {
            level = Mathf.Clamp(level, 0, 16);
            if (level == appliedAniso) return 0;
            int changed = 0;
            try
            {
                var seen = new HashSet<Texture>();
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        foreach (int id in TexIds)
                        {
                            if (!m.HasProperty(id)) continue;
                            var tex = m.GetTexture(id);
                            if (tex == null || !seen.Add(tex) || tex.anisoLevel == level) continue;
                            tex.anisoLevel = level;
                            changed++;
                        }
                    }
                appliedAniso = level;
                Debug.Log($"[Visual] 이방성 필터 {level} — 텍스처 {changed}개");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Visual] 이방성 필터 적용 실패(무시): " + e.Message);
            }
            return changed;
        }
    }
}
