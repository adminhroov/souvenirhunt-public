// 인형뽑기 기계 안쪽 네온 선(10-15 비주얼 2차 — 연출 묶음). 가산(OST에서 가장 잘 보이는 "빛"), 깊이 쓰기 없음.
// 메시 UV: x = 경로 누적 길이(m), y = 선 단면 0~1(가운데가 가장 밝음). 빛 덩어리가 경로를 따라 흐른다(LED 체이스).
// 색은 경로를 따라 _Color → _Color2로 천천히 바뀐다. 이벤트(ClawMachineFx, MPB): _Flash(0~1, _FlashColor로 번쩍) · _Warn(0~1, 남은 시간 경고 붉은 맥동).
Shader "SouvenirHunt/ClawNeon"
{
    Properties
    {
        [HDR] _Color ("Color A", Color) = (0.25, 0.9, 1.0, 1)
        [HDR] _Color2 ("Color B", Color) = (1.0, 0.3, 0.85, 1)
        _Base ("Base Brightness", Range(0, 2)) = 0.8
        _Chase ("Chase Brightness", Range(0, 4)) = 2.0
        _Density ("Chase per Meter", Float) = 2.5
        _Speed ("Chase Speed", Float) = 0.5
        [HDR] _FlashColor ("Flash Color", Color) = (1, 0.25, 0.15, 1)
        _Flash ("Flash", Range(0, 1)) = 0
        _Warn ("Warn", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Neon"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Color2;
                half _Base;
                half _Chase;
                float _Density;
                float _Speed;
                half4 _FlashColor;
                half _Flash;
                half _Warn;
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

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                // 단면: 가운데 밝고 가장자리로 부드럽게
                half across = 1.0h - abs(i.uv.y * 2.0h - 1.0h);
                half core = saturate(across * across * 1.6h);
                // 흐르는 빛 덩어리(앞쪽은 날카롭게, 꼬리는 길게)
                float ph = frac(i.uv.x * _Density - t * _Speed * _Density);
                half chase = smoothstep(0.0h, 0.08h, ph) * (1.0h - smoothstep(0.08h, 0.55h, ph));
                half3 col = lerp(_Color.rgb, _Color2.rgb, 0.5h + 0.5h * sin(i.uv.x * 1.7h + t * 0.35h));
                half3 c = col * (_Base + _Chase * chase);
                // 남은 시간 경고: 붉은 맥동
                half warnPulse = 0.55h + 0.45h * sin(t * 6.0h);
                c = lerp(c, half3(1.0h, 0.18h, 0.12h) * (0.6h + 1.2h * warnPulse), _Warn * 0.85h);
                // 이벤트 번쩍임
                c = lerp(c, _FlashColor.rgb * 2.2h, _Flash);
                return half4(c * core, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
