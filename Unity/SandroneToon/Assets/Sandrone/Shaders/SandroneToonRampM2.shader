Shader "Sandrone/M2/ToonRamp"
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
        _LayerWeight("Layer Weight", Range(0, 1)) = 1
        [HideInInspector] _M2DebugMode("M2 Debug Mode", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 0
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
            Name "M2ToonRamp"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

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
                half _LayerWeight;
                half _M2DebugMode;
                half _Cutoff;
                half _AlphaClip;
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
                Light mainLight = GetMainLight();
                half3 lightDirectionWS = normalize(mainLight.direction);
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                half ndotl = dot(normalWS, lightDirectionWS);
                half halfLambert = saturate(ndotl * 0.5h + 0.5h);
                half derivativeWidth = fwidth(halfLambert) * _BandAA;
                half edgeHalfWidth = max(_BandSoftness + derivativeWidth, 1e-4h);
                half bandMask = smoothstep(_Threshold - edgeHalfWidth, _Threshold + edgeHalfWidth, halfLambert);

                // Keep away from the texture border; the authored ramp interpolates
                // shadow-to-light between U=0.25 and U=0.75.
                half rampU = lerp(0.25h, 0.75h, bandMask);
                half row = clamp(floor(_RampRow + 0.5h), 0.0h, max(_RampRowCount - 1.0h, 0.0h));
                half rampV = (row + 0.5h) / max(_RampRowCount, 1.0h);
                half3 rampSample = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, half2(rampU, rampV)).rgb;
                half ndotv = saturate(dot(normalWS, viewDirectionWS));

                half3 outputColor;
                if (_M2DebugMode < 0.5h)
                {
                    outputColor = baseSample.rgb * rampSample * mainLight.color * mainLight.distanceAttenuation;
                }
                else if (_M2DebugMode < 1.5h)
                {
                    outputColor = halfLambert.xxx;
                }
                else if (_M2DebugMode < 2.5h)
                {
                    outputColor = bandMask.xxx;
                }
                else if (_M2DebugMode < 3.5h)
                {
                    outputColor = half3(rampU, rampV, 0.0h);
                }
                else if (_M2DebugMode < 4.5h)
                {
                    outputColor = rampSample;
                }
                else if (_M2DebugMode < 5.5h)
                {
                    outputColor = ndotv.xxx;
                }
                else if (_M2DebugMode < 6.5h)
                {
                    half3 headNormal = half3(
                        dot(normalWS, normalize(_HeadRightWS.xyz)),
                        dot(normalWS, normalize(_HeadUpWS.xyz)),
                        dot(normalWS, normalize(_HeadForwardWS.xyz)));
                    outputColor = saturate(headNormal * 0.5h + 0.5h);
                }
                else
                {
                    outputColor = half3(1.0h, 1.0h, 1.0h);
                }

                return half4(outputColor, baseSample.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
