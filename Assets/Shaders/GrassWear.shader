// 잔디 마모 셰이더(11-06 경기장 4종 3단계, 설계 실험/2026-11-05-설계-경기장-4종.md §4).
// 손으로 쓴 URP HLSL이다. Shader Graph(.shadergraph)는 JSON 그래프라 코드로 안정적으로 만들 수 없어 대신 택했다.
// 구성: Lerp(좋은 잔디 A, 마른 잔디·맨땅 B, smoothstep(마스크 × _Wear)). 줄무늬는 A에 곱한다.
// SRP Batcher 호환: 모든 머티리얼 값이 CBUFFER_START(UnityPerMaterial)에 있고 MaterialPropertyBlock은 쓰지 않는다. 머티리얼 4개의 _Wear·_PlaneSize·_Stripe만 다르다.
// 조명: 메인 라이트 램버트 + 환경광(SH) + 메인 라이트 그림자 받기. 추가 라이트·안개·법선 맵은 없다(모바일 비용).
Shader "PitchSimulator/GrassWear"
{
    Properties
    {
        [NoScaleOffset] _GoodTex ("좋은 잔디 A", 2D) = "white" {}
        [NoScaleOffset] _DryTex ("마른 잔디·맨땅 B", 2D) = "white" {}
        [NoScaleOffset] _WearMask ("마모 마스크(R)", 2D) = "black" {}
        _GoodTint ("A 색 곱", Color) = (1, 1, 1, 1)
        _DryTint ("B 색 곱", Color) = (1.3, 1.1, 0.7, 1)
        _Wear ("마모 0~1", Range(0, 1)) = 0
        _PlaneSize ("잔디 평면 크기(m) xy", Vector) = (105, 68, 0, 0)
        _MaskSize ("마스크가 덮는 크기(m) xy", Vector) = (117, 80, 0, 0)
        _TileMeters ("텍스처 한 장이 덮는 길이(m)", Float) = 4
        _StripeWidth ("줄무늬 한 줄 폭(m)", Float) = 5
        _Stripe ("줄무늬 세기 0~1", Range(0, 1)) = 0
        _StripeDark ("어두운 줄 밝기", Range(0, 1)) = 0.82
        _WearLow ("마모 시작", Range(0, 1)) = 0.1
        _WearHigh ("마모 완료", Range(0, 1)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_GoodTex);
        SAMPLER(sampler_GoodTex);
        TEXTURE2D(_DryTex);
        SAMPLER(sampler_DryTex);
        TEXTURE2D(_WearMask);
        SAMPLER(sampler_WearMask);

        CBUFFER_START(UnityPerMaterial)
            half4 _GoodTint;
            half4 _DryTint;
            half _Wear;
            float4 _PlaneSize;
            float4 _MaskSize;
            float _TileMeters;
            float _StripeWidth;
            half _Stripe;
            half _StripeDark;
            half _WearLow;
            half _WearHigh;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD2;   // 캐스케이드 없는 그림자(모바일)는 정점에서 구해 보간한다(URP Lit과 같은 분기)
            #endif
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = TransformWorldToShadowCoord(output.positionWS);
            #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 평면 중심 기준 미터 좌표. 줄무늬·텍스처 반복·마스크가 전부 이 좌표를 쓴다(평면 크기가 달라도 같은 크기로 보인다)
                float2 meters = (input.uv - 0.5) * _PlaneSize.xy;
                float2 tileUV = meters / _TileMeters;
                half3 good = SAMPLE_TEXTURE2D(_GoodTex, sampler_GoodTex, tileUV).rgb * _GoodTint.rgb;
                half3 dry = SAMPLE_TEXTURE2D(_DryTex, sampler_DryTex, tileUV).rgb * _DryTint.rgb;

                half bar = step(0.5, frac(meters.y / (_StripeWidth * 2.0)));
                good *= lerp(1.0h, lerp(_StripeDark, 1.0h, bar), _Stripe);

                float2 maskUV = meters / _MaskSize.xy + 0.5;
                half mask = SAMPLE_TEXTURE2D(_WearMask, sampler_WearMask, maskUV).r;
                half wear = smoothstep(_WearLow, _WearHigh, mask * _Wear);
                half3 albedo = lerp(good, dry, wear);

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord = input.shadowCoord;
            #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif
                Light light = GetMainLight(shadowCoord);
                half3 normal = half3(0, 1, 0);
                half3 lit = light.color * (saturate(dot(normal, light.direction)) * light.shadowAttenuation) + SampleSH(normal);
                return half4(albedo * lit, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
