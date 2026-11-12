// 인형뽑기 아이템 셰이더(10-15 비주얼 1차 — 그릇·캡슐·폭탄). URP Lit과 같은 물리 기반 조명(주광 + SH 앰비언트 + 환경 반사 큐브맵)에
// OST 글래스에서 잘 보이는 "밝히는" 효과만 더한다:
//   · 테두리 빛(림/프레넬): 겹친 캡슐 경계를 갈라 주고 어두운 폭탄 몸통을 윤곽으로 보이게(글래스에서 검정 = 투명)
//   · 조준 하이라이트(_Highlight, MPB): 예전처럼 이미션으로 하얗게 덮지 않고 가장자리만 청록 — 물체 색 유지
//   · 발광 맥동(_EmissionPulse): 폭탄 띠·심지 불꽃 깜빡임
// 이미션은 키워드(_EMISSION) 없이 항상 계산 — URP Lit의 GI 플래그 함정(10-06)과 무관. 그림자·라이트맵·추가 광원 없음(씬 설정과 같음).
// config 전역 세기: _ClawRimDelta·_ClawReflDelta(= 값 − 1, 미설정 0 → 1배) — ClawGameManager.ApplyGameConfig가 설정.
Shader "SouvenirHunt/ClawItemLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _ReflectStrength ("Reflection Strength", Range(0, 2)) = 1
        [NoScaleOffset] _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _EmissionPulse ("Emission Pulse", Range(0, 1)) = 0
        _EmissionPulseSpeed ("Pulse Speed", Float) = 6
        [HDR] _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.4
        _RimTint ("Rim Tint by Albedo", Range(0, 1)) = 0.5
        [HDR] _HighlightColor ("Highlight Color", Color) = (0, 1.4, 1.6, 1)
        _Highlight ("Highlight", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                half _ReflectStrength;
                half4 _EmissionColor;
                half _EmissionPulse;
                half _EmissionPulseSpeed;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half _RimTint;
                half4 _HighlightColor;
                half _Highlight;
            CBUFFER_END

            half _ClawRimDelta;   // config clawRim − 1
            half _ClawReflDelta;  // config clawReflection − 1
            // 10-22 조준 스포트라이트(가짜 — 실시간 광원 추가 없음, 사용자 "은은한 스포트라이트·간접적인 느낌"):
            // 집게(꼭짓점)에서 아래로 퍼지는 원뿔 안 표면을 부드럽게 밝힌다. ClawGuide가 전역 값을 넣는다(w = 0이면 꺼짐)
            float4 _ClawSpotPos;      // xyz = 꼭짓점(월드), w = 켜짐(0~1, 서서히)
            float4 _ClawSpotParams;   // x = 기준 높이에서 반지름, y = 기준 높이(월드 y), z = 가장자리 부드러움(0~1), w = 안쪽 밝히기 세기 배율(10-29 config clawGuideSpotIntensity)
            half4 _ClawSpotColor;     // rgb = 안쪽 색 곱(1보다 크면 밝게), a = 둘레 어둡게(0~1)
            // 10-29 밝힌 색의 부드러운 상한: 가장 밝은 채널이 이 값을 넘으면 1에 다가가기만(흰색으로 포화돼 질감·색이 날아가지 않게)
            // 10-29(2) 0.8 → 0.9(사용자 "차이가 안 보인다, 더 밝게" — 흰색 포화 직전에서만 누름)
            #define CLAW_SPOT_KNEE 0.9h

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
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionHCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 N = normalize(i.normalWS);
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half NoV = saturate(dot(N, V));

                half alpha = 1.0h;
                BRDFData brdf;
                InitializeBRDFData(albedo.rgb, _Metallic, half3(0.0h, 0.0h, 0.0h), _Smoothness, alpha, brdf);

                // 간접광: SH 앰비언트 + 환경 반사(씬 커스텀 반사 큐브맵 = unity_SpecCube0)
                half3 bakedGI = SampleSH(N);
                half3 R = reflect(-V, N);
                half reflK = max(0.0h, _ReflectStrength * (1.0h + _ClawReflDelta));
                half3 indirectSpecular = GlossyEnvironmentReflection(R, brdf.perceptualRoughness, 1.0h) * reflK;
                half3 color = EnvironmentBRDF(brdf, bakedGI, indirectSpecular, Pow4(1.0h - NoV));

                // 직접광: 주광 1개(그림자 없음)
                Light mainLight = GetMainLight();
                color += LightingPhysicallyBased(brdf, mainLight, N, V);

                // 발광(맵 × 색) + 맥동
                half pulse = 1.0h + _EmissionPulse * sin(_Time.y * _EmissionPulseSpeed) * (0.75h + 0.25h * sin(_Time.y * _EmissionPulseSpeed * 2.37h));
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb * pulse;

                // 테두리 빛 + 조준 하이라이트(가장자리 위주 + 약한 채움)
                half edge = 1.0h - NoV;
                // 테두리 빛은 표면 색을 따라감(_RimTint) — 흰 테두리가 색을 씻어내지 않게
                half3 rimCol = _RimColor.rgb * lerp(half3(1.0h, 1.0h, 1.0h), saturate(albedo.rgb * 1.4h), _RimTint);
                color += pow(edge, _RimPower) * rimCol * (_RimStrength * max(0.0h, 1.0h + _ClawRimDelta));
                color += _Highlight * _HighlightColor.rgb * (pow(edge, 1.5h) * 1.3h + 0.10h);

                // 조준 스포트라이트: 꼭짓점 아래 깊이에 비례해 넓어지는 원(기준 높이에서 반지름 x), 가장자리 부드럽게, 위를 향한 면일수록 밝게
                if (_ClawSpotPos.w > 0.001)
                {
                    float depth = max(0.0, _ClawSpotPos.y - i.positionWS.y);
                    float refDepth = max(0.01, _ClawSpotPos.y - _ClawSpotParams.y);
                    float r = _ClawSpotParams.x * depth / refDepth;
                    float d = length(i.positionWS.xz - _ClawSpotPos.xz);
                    half spot = (half)(1.0 - smoothstep(r * (1.0 - _ClawSpotParams.z), r, d));
                    half up = saturate(N.y * 0.6h + 0.4h);
                    half fade = (half)_ClawSpotPos.w;
                    // 안 = 따뜻하게 물들이며 조금 밝게(색 곱 — 흰 캡슐도 보이게), 둘레(반지름 1~2.2배) = 살짝 어둡게(빛 웅덩이 대비)
                    // 10-29 사용자 "스포트라이트를 더 밝게, 너무 강해서 상품이 안 보이지 않게": 세기 배율(_ClawSpotParams.w)은 색 곱에서 1을 넘는 몫에만 곱한다
                    // (흰색: 예전 1.3배 → 기본 1 + 0.3 × 2.2 = 1.66배). 곱셈이라 질감·색 비율은 그대로, 가장 밝은 채널은 CLAW_SPOT_KNEE부터 1 미만으로
                    // 부드럽게 눌러 흰색 포화 없음(원래보다 어두워지지는 않음). 둘레 어둡기는 배율과 무관(켜짐 정도만)
                    half3 gain = max(0.0h, 1.0h + (_ClawSpotColor.rgb - 1.0h) * (half)_ClawSpotParams.w);
                    half3 lit = color * gain;
                    half m0 = max(max(color.r, color.g), color.b);
                    half m = max(max(lit.r, lit.g), lit.b);
                    half mk = m <= CLAW_SPOT_KNEE ? m : CLAW_SPOT_KNEE + (1.0h - CLAW_SPOT_KNEE) * (1.0h - exp((CLAW_SPOT_KNEE - m) / (1.0h - CLAW_SPOT_KNEE)));
                    lit *= max(mk, min(m, m0)) / max(m, 1e-4h);
                    color = lerp(color, lit, spot * up * fade);
                    half surround = (1.0h - spot) * (half)(1.0 - smoothstep(r, r * 2.2, d));
                    color *= 1.0h - _ClawSpotColor.a * surround * fade;
                }

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
