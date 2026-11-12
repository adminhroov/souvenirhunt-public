// 조준 대상 바닥 표시(10-19, 사용자 선택 = 시안 3 "스포트라이트" + 골드). 대상 밑 바닥에 눕힌 사각 판 하나에 절차적으로 그린다 — 깊이 쓰기 없음.
// 바닥을 은은하게 채운 원판 + 밝은 테두리 + 바깥으로 퍼지는 물결. UV 중심 0.5. d = 중심 거리 / 링 반지름(판 반폭 = 링 반지름 × _Extent).
// 색(_Color)은 ClawGuide가 config clawGuideColor로(MPB). 시안 1(조준경)·2(회전 점선)는 git 이력(10-19)에.
Shader "SouvenirHunt/ClawGuideMarker"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.9, 1.0, 1)
        _Extent ("Extent (ring radii)", Float) = 1.9
        _Intensity ("Intensity", Range(0, 4)) = 1.0
        _Opacity ("Opacity (0 = 순수 가산)", Range(0, 2)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Marker"
            Tags { "LightMode"="SRPDefaultUnlit" }
            // 10-19 색 시안: 미리 곱한 알파 — 강한 부분(테두리·심지)은 바닥을 덮어 색이 그대로, 약한 부분은 더해지는 빛. 색은 상한 1(하얗게 날지 않게)
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Extent;
                half _Intensity;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // 중심 c에서 반폭 w인 띠(가운데 1 → 가장자리 0)
            half Band(float x, float c, float w) { return saturate(1.0 - abs(x - c) / w); }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                float2 p = (i.uv - 0.5) * 2.0 * _Extent;
                float d = length(p);
                half v = 0;
                // 바닥을 은은하게 채운 원판 + 밝은 테두리 + 바깥으로 퍼지는 물결
                half breathe = 0.8 + 0.2 * sin(t * 3.0);
                v += smoothstep(1.05, 0.15, d) * 0.5 * breathe;
                v += Band(d, 1.0, 0.09) * 1.7;
                float k = frac(t / 1.2);
                v += Band(d, 1.0 + k * 0.85, 0.06) * (1.0 - k) * (1.0 - k) * 1.3;
                v *= saturate((_Extent - d) * 6.0) * _Intensity; // 판 가장자리에서 잘리지 않게
                return half4(_Color.rgb * saturate(v), saturate(v * _Opacity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
