Shader "DATN/Effects/CenterMist"
{
    Properties
    {
        [MainColor] _Tint ("Mist Tint", Color) = (0.72, 0.82, 1.0, 1.0)
        [NoScaleOffset] _NoiseTex ("Noise Texture", 2D) = "gray" {}
        _WorldTiling ("World Tiling", Range(0.05, 2.0)) = 0.35
        _ScrollA ("Scroll A", Vector) = (0.035, 0.018, 0.0, 0.0)
        _ScrollB ("Scroll B", Vector) = (-0.022, 0.031, 0.0, 0.0)
        _Density ("Density", Range(0.0, 1.0)) = 0.38
        _Softness ("Softness", Range(0.01, 0.5)) = 0.22
        _Opacity ("Opacity", Range(0.0, 1.0)) = 0.16
        _EdgeFade ("Center Edge Fade", Range(0.001, 0.5)) = 0.16
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "CenterMist"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float4 _ScrollA;
                float4 _ScrollB;
                float _WorldTiling;
                float _Density;
                float _Softness;
                float _Opacity;
                float _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half SampleTriplanarNoise(float3 position, half3 weights, float2 scroll)
            {
                half sampleX = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, position.zy + scroll).r;
                half sampleY = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, position.xz + scroll).r;
                half sampleZ = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, position.xy + scroll).r;
                return sampleX * weights.x + sampleY * weights.y + sampleZ * weights.z;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 weights = pow(abs(normalize(input.normalWS)), 4.0h);
                weights /= max(weights.x + weights.y + weights.z, 0.0001h);

                float3 noisePosition = input.positionWS * _WorldTiling;
                float2 scrollA = _ScrollA.xy * _Time.y;
                float2 scrollB = _ScrollB.xy * _Time.y;

                half noiseA = SampleTriplanarNoise(noisePosition, weights, scrollA);
                half noiseB = SampleTriplanarNoise(noisePosition * 1.73 + 9.17, weights, scrollB);
                half combinedNoise = (noiseA + noiseB) * 0.5h;
                half mist = smoothstep(_Density - _Softness, _Density + _Softness, combinedNoise);

                float2 edgeDistance = min(input.uv, 1.0 - input.uv);
                half centerMask = smoothstep(0.0, _EdgeFade, min(edgeDistance.x, edgeDistance.y));
                half alpha = mist * centerMask * _Opacity * _Tint.a;

                half3 color = MixFog(_Tint.rgb, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}