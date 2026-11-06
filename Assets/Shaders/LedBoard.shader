// LED 광고판 셰이더(11-06 경기장 더그아웃·LED 단계).
// 손으로 쓴 URP Unlit HLSL. SRP Batcher 호환(CBUFFER UnityPerMaterial), GPU 인스턴싱·그림자 패스 없음(Crowd 셰이더와 같은 이유).
// 판의 길이 방향 미터 좌표를 월드 좌표에서 얻는다: s = x + z. 광고판은 Far·Near에서 z가 상수, 골대 뒤 면에서 x가 상수라 s는 길이 방향 위치 + 상수가 된다.
// 그래서 판 길이가 달라도 패널 폭이 같고, 판마다 패널 경계 위상만 다르다. 높이는 월드 y(미터)를 그대로 쓴다.
// DepthOnly 패스 없음 = 의도(모바일 렌더러는 깊이 텍스처를 안 씀, PC 에디터 SSAO에서만 빠진다).
// 패널(_PanelWidth m)마다 해시로 팔레트 색·무늬(가로줄·블록·띠)를 고르고, 경계에 어두운 이음매(_SeamWidth m)를 그린다. 실제 브랜드·글자 없음.
Shader "PitchSimulator/LedBoard"
{
    Properties
    {
        _PanelWidth ("패널 폭(m)", Float) = 6
        _SeamWidth ("이음매 폭(m)", Float) = 0.15
        _SeamColor ("이음매 색", Color) = (0.03, 0.03, 0.05, 1)
        _Glow ("발광 밝기 배율", Range(0.5, 2)) = 1.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define PALETTE_COUNT 5
            #define PATTERN_COUNT 3
            #define STRIPE_PER_METER 5.0      // [가정] 가로줄 밀도
            #define BLOCK_PER_METER 3.0       // [가정] 블록 밀도
            #define DIM_FACTOR 0.5            // [가정] 줄·블록의 어두운 쪽 밝기

            CBUFFER_START(UnityPerMaterial)
                float _PanelWidth;
                float _SeamWidth;
                half4 _SeamColor;
                half _Glow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            // [가정] 팔레트 5색: 청록·자홍·주황·연두·노랑
            static const half3 Palette[PALETTE_COUNT] =
            {
                half3(0.10, 0.70, 0.95),
                half3(0.90, 0.20, 0.65),
                half3(0.98, 0.50, 0.12),
                half3(0.35, 0.85, 0.30),
                half3(0.95, 0.88, 0.25)
            };

            uint PanelHash(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return x;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half Pattern(uint kind, float local, float y)
            {
                if (kind == 0u)
                {
                    return fmod(floor(y * STRIPE_PER_METER), 2.0) < 1.0 ? 1.0h : DIM_FACTOR;
                }
                if (kind == 1u)
                {
                    float cell = floor(local * BLOCK_PER_METER) + floor(y * BLOCK_PER_METER * 2.0);
                    return fmod(cell, 2.0) < 1.0 ? 1.0h : DIM_FACTOR;
                }
                return abs(frac(y) - 0.5) < 0.2 ? 1.0h : DIM_FACTOR;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float s = input.positionWS.x + input.positionWS.z;
                float cell = floor(s / _PanelWidth);
                float local = s - cell * _PanelWidth;

                float edge = min(local, _PanelWidth - local);
                if (edge < _SeamWidth * 0.5)
                {
                    return _SeamColor;
                }

                uint hash = PanelHash((uint)(int)cell * 2654435761u);
                half3 color = Palette[hash % PALETTE_COUNT];
                half shade = Pattern((hash >> 8) % PATTERN_COUNT, local, input.positionWS.y);
                return half4(saturate(color * shade * _Glow), 1.0h);
            }
            ENDHLSL
        }
    }
}
