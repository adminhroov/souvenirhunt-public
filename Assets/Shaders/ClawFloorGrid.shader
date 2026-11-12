// 인형뽑기 바닥 디지털 그리드(10-15 비주얼 2차). 바닥 바로 위 사각 쿼드(기계 내부 치수)에 가산으로 그린다 — 뭉개진 바닥 텍스처 위에
// 가는 격자 + 앞→뒤로 지나가는 스캔 띠 + 이벤트 물결(그릇 획득·폭발 위치에서 퍼지는 원). 아이템(불투명)이 깊이로 가린다.
// 좌표는 정점 로컬 xz(m, 기계 좌표). 배출구 칸(x < _ChuteX && z < _ChuteZ)은 비운다. 물결은 MPB(_RippleOrigin·_RippleStart·_RippleColor).
Shader "SouvenirHunt/ClawFloorGrid"
{
    Properties
    {
        [HDR] _Color ("Grid Color", Color) = (0.3, 0.85, 1.0, 1)
        _GridAlpha ("Grid Strength", Range(0, 1)) = 0.16
        _Cell ("Cell Size (m)", Float) = 0.07
        _Line ("Line Width (m)", Float) = 0.0035
        [HDR] _ScanColor ("Scan Color", Color) = (0.4, 0.95, 1.0, 1)
        _ScanStrength ("Scan Strength", Range(0, 2)) = 0.35
        _ScanWidth ("Scan Width (m)", Float) = 0.06
        _ScanPeriod ("Scan Period (s)", Float) = 3.2
        _MinZ ("Min Z", Float) = 0
        _MaxZ ("Max Z", Float) = 1
        _HalfX ("Half X", Float) = 0.5
        _ChuteX ("Chute Wall X", Float) = -0.3
        _ChuteZ ("Chute Wall Z", Float) = 0.3
        _RippleOrigin ("Ripple Origin XZ", Vector) = (0, 0, 0, 0)
        _RippleStart ("Ripple Start Time", Float) = -100
        [HDR] _RippleColor ("Ripple Color", Color) = (0.4, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Grid"
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
                half _GridAlpha;
                float _Cell;
                float _Line;
                half4 _ScanColor;
                half _ScanStrength;
                float _ScanWidth;
                float _ScanPeriod;
                float _MinZ;
                float _MaxZ;
                float _HalfX;
                float _ChuteX;
                float _ChuteZ;
                float4 _RippleOrigin;
                float _RippleStart;
                half4 _RippleColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 xz : TEXCOORD0;
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
                o.xz = v.positionOS.xz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.xz;
                if (p.x < _ChuteX && p.y < _ChuteZ) return half4(0, 0, 0, 1);

                // 격자: 셀 경계까지 거리(m) → 화면 미분으로 계단 없이
                float2 g = abs(frac(p / _Cell - 0.5) - 0.5) * _Cell;
                float2 fw = max(fwidth(p), 1e-5);
                float2 lineMask = 1.0 - smoothstep(_Line * 0.5, _Line * 0.5 + fw * 1.5, g);
                half grid = (half)max(lineMask.x, lineMask.y);

                // 벽 가까이는 흐리게(유리 경계와 겹치지 않게)
                float edge = min(min(p.x + _HalfX, _HalfX - p.x), min(p.y - _MinZ, _MaxZ - p.y));
                half fade = (half)smoothstep(0.0, 0.04, edge);

                // 스캔 띠: 앞(min z) → 뒤(max z)
                float t = _Time.y;
                float zScan = lerp(_MinZ - 0.1, _MaxZ + 0.1, frac(t / _ScanPeriod));
                float dz = (p.y - zScan) / _ScanWidth;
                half band = (half)exp(-dz * dz);

                // 이벤트 물결: 원점에서 1.1m/s로 퍼지는 고리, 1.1초에 사라짐
                float age = t - _RippleStart;
                float rr = age * 1.1;
                float dr = (length(p - _RippleOrigin.xy) - rr) / 0.035;
                half ripple = (half)(exp(-dr * dr) * saturate(1.0 - age / 1.1) * step(0.0, age));

                half3 c = _Color.rgb * (_GridAlpha * grid)
                        + _ScanColor.rgb * (_ScanStrength * band * (0.25h + grid))
                        + _RippleColor.rgb * (ripple * (0.6h + grid));
                return half4(c * fade, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
