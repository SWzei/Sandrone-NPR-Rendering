Shader "Sandrone/M3/ToonShadow"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _RampMap("Warm/Cool Ramp", 2D) = "white" {}
        _RampRow("Ramp Row", Range(0, 4)) = 0
        [HideInInspector] _RampRowCount("Ramp Row Count", Float) = 5
        _Threshold("Band Threshold", Range(0, 1)) = 0.5
        _BandSoftness("Band Minimum Softness", Range(0, 0.1)) = 0.015
        _BandAA("Band Derivative AA", Range(0, 4)) = 1
        _CastShadowStrength("Cast Shadow Strength", Range(0, 1)) = 0.85
        _CastShadowLow("Cast Shadow Low", Range(0, 1)) = 0.2
        _CastShadowHigh("Cast Shadow High", Range(0, 1)) = 0.8
        _LayerWeight("Layer Weight", Range(0, 1)) = 1
        [HideInInspector] _M3DebugMode("M3 Debug Mode", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 0
        [HideInInspector] _ShadowCull("Shadow Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "M3ToonShadow"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord : TEXCOORD3;
                #endif
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RampMap);
            SAMPLER(sampler_RampMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _RampRow;
                half _RampRowCount;
                half _Threshold;
                half _BandSoftness;
                half _BandAA;
                half _CastShadowStrength;
                half _CastShadowLow;
                half _CastShadowHigh;
                half _LayerWeight;
                half _M3DebugMode;
                half _Cutoff;
                half _AlphaClip;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
                half _Cull;
                half _ShadowCull;
                float4 _HeadForwardWS;
                float4 _HeadRightWS;
                float4 _HeadUpWS;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    output.shadowCoord = GetShadowCoord(positionInputs);
                #endif
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                baseSample.a *= _LayerWeight;
                if (_AlphaClip > 0.5h)
                {
                    clip(baseSample.a - _Cutoff);
                }

                half faceSign = IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
                half3 normalWS = normalize(input.normalWS) * faceSign;
                float4 shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half3 lightDirectionWS = normalize(mainLight.direction);

                half halfLambert = saturate(dot(normalWS, lightDirectionWS) * 0.5h + 0.5h);
                half edgeHalfWidth = max(_BandSoftness + fwidth(halfLambert) * _BandAA, 1e-4h);
                half formBand = smoothstep(_Threshold - edgeHalfWidth, _Threshold + edgeHalfWidth, halfLambert);

                half castShadowRaw = saturate(mainLight.shadowAttenuation);
                half castShadowStyled = smoothstep(_CastShadowLow, _CastShadowHigh, castShadowRaw);
                castShadowStyled = lerp(1.0h, castShadowStyled, _CastShadowStrength);
                // The form band remains discrete while the cast-shadow PCF edge is
                // represented once. Multiplication would create a second dark edge.
                half finalLitMask = min(formBand, castShadowStyled);

                half rampU = lerp(0.25h, 0.75h, finalLitMask);
                half row = clamp(floor(_RampRow + 0.5h), 0.0h, max(_RampRowCount - 1.0h, 0.0h));
                half rampV = (row + 0.5h) / max(_RampRowCount, 1.0h);
                half3 rampSample = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, half2(rampU, rampV)).rgb;

                half3 outputColor;
                if (_M3DebugMode < 0.5h)
                {
                    outputColor = baseSample.rgb * rampSample * mainLight.color * mainLight.distanceAttenuation;
                }
                else if (_M3DebugMode < 1.5h)
                {
                    outputColor = castShadowRaw.xxx;
                }
                else if (_M3DebugMode < 2.5h)
                {
                    outputColor = castShadowStyled.xxx;
                }
                else if (_M3DebugMode < 3.5h)
                {
                    outputColor = formBand.xxx;
                }
                else if (_M3DebugMode < 4.5h)
                {
                    outputColor = finalLitMask.xxx;
                }
                else if (_M3DebugMode < 5.5h)
                {
                    outputColor = rampSample;
                }
                else if (_M3DebugMode < 6.5h)
                {
                    #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                        outputColor = (ComputeCascadeIndex(input.positionWS) / 3.0h).xxx;
                    #else
                        outputColor = half3(0.0h, 0.0h, 0.0h);
                    #endif
                }
                else
                {
                    outputColor = half3(1.0h, 1.0h, 1.0h);
                }

                return half4(outputColor, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_ShadowCull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _RampRow;
                half _RampRowCount;
                half _Threshold;
                half _BandSoftness;
                half _BandAA;
                half _CastShadowStrength;
                half _CastShadowLow;
                half _CastShadowHigh;
                half _LayerWeight;
                half _M3DebugMode;
                half _Cutoff;
                half _AlphaClip;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
                half _Cull;
                half _ShadowCull;
                float4 _HeadForwardWS;
                float4 _HeadRightWS;
                float4 _HeadUpWS;
            CBUFFER_END

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_TARGET
            {
                // Alpha-blended overlays cannot produce physically meaningful
                // binary shadow maps and are explicitly excluded. Opaque/cutout
                // materials share BaseMap, BaseColor, LayerWeight and Cutoff with
                // the forward pass.
                if (_ZWrite < 0.5h)
                {
                    clip(-1.0h);
                }
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a *
                             _BaseColor.a * _LayerWeight;
                if (_AlphaClip > 0.5h)
                {
                    clip(alpha - _Cutoff);
                }
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
