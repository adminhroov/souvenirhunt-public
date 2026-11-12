// 오클루전 마스크 (실제 부스 설치 모드, envOcclusionMode=true).
// 색은 전혀 그리지 않고(ColorMask 0) 깊이만 기록 → OST 글래스에서 이 영역은 현실이 그대로 보이고,
// 뒤에 있는 가상 수집품은 깊이 테스트에 막혀 "실제 구조물에 가려진 것"처럼 숨는다.
// Queue를 Geometry보다 앞에 둬서 수집품(Geometry)보다 먼저 깊이를 쓴다.
Shader "SouvenirHunt/DepthOcclusion"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DepthOnlyMask"
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings  { float4 positionHCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
