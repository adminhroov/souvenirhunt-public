// 조준 가이드 원뿔(10-15 비주얼 2차) — 가산 반투명 원뿔에 아래로 흐르는 스캔 줄무늬. 원뿔 메시 UV y: 0 = 꼭지(집게 하단), 1 = 밑면(대상 윗면).
// 위쪽은 옅게, 밑면 쪽으로 진하게 — 집게에서 대상 쪽으로 빛이 내려가는 느낌.
Shader "SouvenirHunt/ClawGuideScan"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.85, 0.9, 1)
        _Base ("Base", Range(0, 1)) = 0.16
        _Stripe ("Stripe", Range(0, 2)) = 0.45
        _StripeCount ("Stripe Count", Float) = 5
        _StripeSpeed ("Stripe Speed", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Scan"
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
                half _Base;
                half _Stripe;
                float _StripeCount;
                float _StripeSpeed;
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
                half y = (half)i.uv.y;
                float ph = frac(i.uv.y * _StripeCount - _Time.y * _StripeSpeed);
                half stripe = (half)(smoothstep(0.0, 0.15, ph) * (1.0 - smoothstep(0.15, 0.5, ph)));
                half k = (_Base + _Stripe * stripe) * (0.25h + 0.75h * y);
                return half4(_Color.rgb * k, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
