Shader "Sandrone/M1/MainLightBaseline"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _LayerWeight("Layer Weight", Range(0, 1)) = 1
        [Enum(BaseLit,0,NdotL,1,NdotV,2,HeadAxis,3,MainLightColor,4,MainLightDistanceAttenuation,5)] _M1DebugMode("M1 Debug Mode", Float) = 0
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
            Name "M1MainLight"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            // URP 17.5 uses the cluster loop in Forward+. Without this variant,
            // GetMainLight() reads unity_LightData.z, which is not populated by
            // the Forward+ renderer and incorrectly suppresses the main light.
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _LayerWeight;
                half _M1DebugMode;
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
                half ndotlSigned01 = saturate(ndotl * 0.5h + 0.5h);
                half ndotv = saturate(dot(normalWS, viewDirectionWS));

                half3 outputColor;
                if (_M1DebugMode < 0.5h)
                {
                    outputColor = baseSample.rgb * mainLight.color * ndotlSigned01 * mainLight.distanceAttenuation;
                }
                else if (_M1DebugMode < 1.5h)
                {
                    outputColor = ndotlSigned01.xxx;
                }
                else if (_M1DebugMode < 2.5h)
                {
                    outputColor = ndotv.xxx;
                }
                else if (_M1DebugMode < 3.5h)
                {
                    half3 headRightWS = normalize(_HeadRightWS.xyz);
                    half3 headUpWS = normalize(_HeadUpWS.xyz);
                    half3 headForwardWS = normalize(_HeadForwardWS.xyz);
                    half3 headNormal = half3(
                        dot(normalWS, headRightWS),
                        dot(normalWS, headUpWS),
                        dot(normalWS, headForwardWS));
                    outputColor = saturate(headNormal * 0.5h + 0.5h);
                }
                else if (_M1DebugMode < 4.5h)
                {
                    outputColor = mainLight.color;
                }
                else
                {
                    // Heat-map presentation keeps the exact scalar in R while
                    // remaining distinguishable from a white MainLightColor view
                    // for directional lights, whose distance attenuation is 1.
                    outputColor = half3(mainLight.distanceAttenuation,
                        mainLight.distanceAttenuation * 0.5h, 0.0h);
                }

                return half4(outputColor, baseSample.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
