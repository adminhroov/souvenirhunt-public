// 가벼운 조명 셰이더(10-15 성능): 부스·기계처럼 화면을 넓게 덮는 배경용. 조명은 정점에서만(램버트: SH 앰비언트 + 주광 N·L) 계산하고
// 픽셀에서는 텍스처 × 정점 조명 + 발광만 — 노멀맵·금속성·반사·스페큘러 없음. 실기기 측정에서 부스(URP Lit, 화면 전체)가 GPU 24ms를 썼음.
// 구멍은 키워드 _ALPHATEST_ON(clip이 있는 셰이더는 모바일에서 이른 깊이 검사가 꺼지므로 불투명 변형에는 넣지 않음),
// 반투명(_Surface 1: 블렌드 SrcAlpha/OneMinusSrcAlpha, 깊이 쓰기 없음)은 재질 값으로.
// 재질은 런타임 ClawRenderTier가 원본 URP Lit 재질에서 만든다 → 빌드에 그 키워드를 쓰는 재질 에셋이 없으므로 shader_feature가 아니라
// multi_compile_local(변형이 항상 빌드에 들어감, 2개뿐).
Shader "SouvenirHunt/LiteLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLite"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _Cutoff;
                half _Surface;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
                half _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 light : TEXCOORD1;
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
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                half3 n = TransformObjectToWorldNormal(v.normalOS);
                Light l = GetMainLight();
                o.light = SampleSH(n) + l.color * saturate(dot(n, l.direction));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                clip(a.a - _Cutoff);
                #endif
                half3 c = a.rgb * i.light + SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                return half4(c, _Surface > 0.5h ? a.a : 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
