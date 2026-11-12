// 디지털 디졸브 전환(연출안 STEP 04, 10-13) — 눈을 감싼 구(DigitalReveal 돔)에 그린다.
// OST 글래스에서 검정 = 투명(현실이 그대로 보임) — 연출안의 "검은 화면"을 그대로 쓴다:
//   ① _Black 0→1: 영상이 검게 사라짐(= 현실만 보임)  ② _Grid 0→1: 검은 화면에 디지털 그리드가 칸 단위로 생김
//   ③ _Pixels 0→1: 픽셀이 반짝이며 분해 시작  ④ _Progress 0→1: 정면(기계 쪽)부터 바깥으로 칸이 픽셀로 부서지며 뒤의 가상 공간이 드러남
// 깊이 무시(ZTest Always)·투명 큐 맨 뒤라 무엇이든 덮는다. 미리곱 알파: rgb = 발광, a = 덮는 정도(검정).
// 칸 좌표는 돔 로컬 방향의 yaw·pitch(도) — 돔을 눈 위치·시선 yaw에 놓으면 정면이 디졸브 중심.
Shader "SouvenirHunt/DigitalReveal"
{
    Properties
    {
        _ColorA ("Pixel Blue", Color) = (0.15, 0.45, 1.0, 1)
        _ColorB ("Pixel Cyan", Color) = (0.35, 0.9, 1.0, 1)
        _Black ("Black", Range(0, 1)) = 0
        _Grid ("Grid", Range(0, 1)) = 0
        _Pixels ("Pixels", Range(0, 1)) = 0
        _Progress ("Progress", Range(0, 1)) = 0
        _CellDeg ("Cell Degrees", Float) = 2.2
        _CenterPitch ("Center Pitch", Float) = -15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Reveal"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                float _Black;
                float _Grid;
                float _Pixels;
                float _Progress;
                float _CellDeg;
                float _CenterPitch;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dirOS = v.positionOS.xyz;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            /// 칸 안쪽 사각형(가장자리 gap만큼 비움) — 픽셀 한 칸
            float Box(float2 fr, float gap)
            {
                float2 s = step(gap, fr) * step(fr, 1.0 - gap);
                return s.x * s.y;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dirOS);
                float yaw = atan2(d.x, d.z) * 57.29578;
                float pitch = asin(clamp(d.y, -1.0, 1.0)) * 57.29578;
                float2 cell = float2(yaw, pitch) / _CellDeg;
                float2 id = floor(cell);
                float2 fr = cell - id;
                float2 idC = floor(cell / 4.0);           // 굵은 칸(4×4) — 덩어리로 부서지게
                float hF = Hash21(id);
                float hC = Hash21(idC + 17.0);

                // 그리드 선(화면 픽셀 기준 두께) — 칸 경계, 4칸마다 굵게
                float2 w = fwidth(cell);
                float2 g = abs(frac(cell - 0.5) - 0.5) / max(w, 1e-4);
                float line1 = 1.0 - saturate(min(g.x, g.y));
                float2 cellC = cell / 4.0;
                float2 gC = abs(frac(cellC - 0.5) - 0.5) / max(fwidth(cellC), 1e-4);
                float lineC = 1.0 - saturate(min(gC.x, gC.y) * 0.7);
                float fade = 1.0 - smoothstep(55.0, 80.0, abs(pitch));      // 극 근처는 선이 몰리므로 흐리게
                // ② 그리드 형성: 굵은 칸이 무작위로 하나씩 켜지고, 켜지는 순간 번쩍
                float k = _Grid * 1.15;
                float gridOn = step(hC, k);
                float flash = gridOn * (1.0 - saturate((k - hC) / 0.08));
                float gridLine = max(line1 * 0.35, lineC * 0.75) * gridOn * fade;

                // ④ 디졸브 점수: 정면(기계 쪽)에서 멀수록 늦게 + 굵은·작은 칸 무작위 — 무작위 비중을 크게 해 시야(±25°) 안에서도 서서히 드러나게
                float2 cc = (id + 0.5) * _CellDeg;
                float ang = length(float2(cc.x, (cc.y - _CenterPitch) * 1.2)) / 120.0;
                float score = saturate(ang) * 0.35 + (hC * 0.6 + hF * 0.4) * 0.65;
                const float band = 0.22;
                float p = lerp(-0.02, 1.0 + band * 1.6, _Progress);
                float b = (p - score) / band;              // <0 아직 검정 · 0~1 픽셀로 부서지는 중 · >1 드러남

                float tw = Hash21(id + floor(_Time.y * 12.0) * 7.13);
                half3 pix = lerp(_ColorA.rgb, _ColorB.rgb, hF);

                // 아직 검정인 칸: 검정(현실) + 그리드 선 + ③ 반짝이는 픽셀
                float sparkle = step(1.0 - _Pixels * 0.22, tw) * Box(fr, 0.1) * fade;
                half3 colBlack = pix * gridLine * (0.8 + flash * 1.5) + pix * sparkle * 1.2;
                float aBlack = max(_Black, sparkle);

                // 부서지는 칸: 픽셀이 밝게 켜졌다가 작아지며 사라짐(칸 사이 검정은 먼저 빠짐 → 뒤 공간이 보이기 시작)
                float shrink = saturate((b - 0.3) / 0.7);
                float inner = Box(fr, lerp(0.06, 0.5, shrink));
                half3 colEdge = pix * inner * (1.6 - b) * fade + pix * line1 * 0.4 * fade;
                float aEdge = inner * (1.0 - shrink);

                // 드러난 칸: 그리드 잔광만 짧게(덮지 않음)
                half3 colOpen = pix * gridLine * saturate(1.6 - b) * 0.6;

                float isBlack = step(b, 0.0);
                float isOpen = step(1.0, b);
                float isEdge = (1.0 - isBlack) * (1.0 - isOpen);
                half3 col = colBlack * isBlack + colEdge * isEdge + colOpen * isOpen;
                float a = aBlack * isBlack + aEdge * isEdge;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
