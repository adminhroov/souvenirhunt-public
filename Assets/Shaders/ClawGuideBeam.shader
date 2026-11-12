// 조준 선(10-19, 사용자 선택 = 시안 3 "옅은 선 + 떨어지는 빛 방울") — 집게 하단 → 대상 윗면 LineRenderer(텍스처 모드 Stretch: uv.x 0 = 집게, 1 = 대상).
// 깊이 쓰기 없음. _Length(MPB) = 선 길이 m(빛 방울 크기 일정). 색(_Color)은 ClawGuide가 config clawGuideColor로. 실선·흐르는 점선 시안은 git 이력(10-19)에.
Shader "SouvenirHunt/ClawGuideBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.9, 1.0, 1)
        _Length ("Length (m)", Float) = 1
        _Intensity ("Intensity", Range(0, 4)) = 1.0
        _Opacity ("Opacity (0 = 순수 가산)", Range(0, 2)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Beam"
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
                float _Length;
                half _Intensity;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
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
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                half across = 1.0h - abs(i.uv.y * 2.0h - 1.0h);
                half core = saturate(across * 1.6h);
                float k = frac(t / 0.9);
                float drop = exp(-abs(i.uv.x - k) * _Length * 28.0);              // 위에서 아래로 떨어지는 빛 방울
                half v = 0.28h + drop * 2.0h;
                v *= smoothstep(0.0, 0.04, i.uv.x);                                // 집게 쪽 끝은 부드럽게
                v *= core * _Intensity * i.color.a;
                return half4(_Color.rgb * i.color.rgb * saturate(v), saturate(v * _Opacity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
