// 관중 셰이더(11-06 경기장 4종 4단계, 설계 실험/2026-11-05-설계-경기장-4종.md §3).
// 손으로 쓴 URP Unlit HLSL. 알파 클립(불투명 큐라 정렬 없음)·GPU 인스턴싱·그림자 패스 없음(드리움·받음 모두 없음).
// DepthOnly·DepthNormals 패스가 없는 것은 의도다: 모바일 렌더러는 깊이 텍스처를 안 써서 영향이 없고, PC 에디터의 SSAO에서만 관중이 빠진다.
// 인스턴스 버퍼 없이 인스턴스의 월드 위치(행렬 이동 성분)를 0.1m로 반올림해 해시 → 아틀라스 칸(4색 × 2자세)·홈 색 여부·밝기를 고른다.
// 같은 자리는 늘 같은 모양이라 재시작해도 관중이 바뀌지 않는다. 홈 색: 해시가 _HomeShare 안이면 몸통(셀 아래쪽)을 홈 유니폼 색으로 칠한다.
// 아틀라스는 CrowdAtlasBuilder가 그린다: 머리(피부·머리색)는 셀 위쪽 _HeadV 위, 몸통은 그 아래. _HeadV는 CrowdAtlasBuilder.HeadV와 같아야 한다.
Shader "PitchSimulator/Crowd"
{
    Properties
    {
        [NoScaleOffset] _Atlas ("실루엣 아틀라스(4×2)", 2D) = "white" {}
        _HomeColor ("홈 유니폼 색", Color) = (0.16, 0.24, 0.48, 1)
        _HomeShare ("홈 색 비율", Range(0, 1)) = 0.6
        _Cutoff ("알파 클립 문턱", Range(0, 1)) = 0.5
        _Brightness ("전체 밝기", Range(0, 1.5)) = 0.9
        _HeadV ("셀 안 머리 시작 높이 0~1", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define ATLAS_COLUMNS 4.0
            #define ATLAS_ROWS 2.0
            #define SHIRT_SHADE_LOW 0.8

            TEXTURE2D(_Atlas);
            SAMPLER(sampler_Atlas);

            CBUFFER_START(UnityPerMaterial)
                half4 _HomeColor;
                half _HomeShare;
                half _Cutoff;
                half _Brightness;
                half _HeadV;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation uint seed : TEXCOORD1;
            };

            uint CrowdHash(uint x)
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
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;

                float3 origin = GetObjectToWorldMatrix()._m03_m13_m23;
                int3 q = (int3)round(origin * 10.0);
                output.seed = CrowdHash((uint)q.x * 73856093u ^ CrowdHash((uint)q.z * 19349663u) ^ CrowdHash((uint)q.y * 83492791u));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                uint column = input.seed % 4u;
                uint pose = (input.seed >> 4) % 2u;
                half homeRoll = (half)((input.seed >> 8) % 100u) / 100.0h;
                half shade = 0.85h + 0.15h * (half)((input.seed >> 16) % 256u) / 255.0h;

                float2 atlasUV = (float2(column, pose) + input.uv) / float2(ATLAS_COLUMNS, ATLAS_ROWS);
                half4 texel = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, atlasUV);
                clip(texel.a - _Cutoff);

                half3 color = texel.rgb;
                if (input.uv.y < _HeadV && homeRoll < _HomeShare)
                {
                    color = _HomeColor.rgb * lerp(SHIRT_SHADE_LOW, 1.0h, input.uv.y / _HeadV);
                }
                return half4(color * shade * _Brightness, 1.0h);
            }
            ENDHLSL
        }
    }
}
