Shader "Sandrone/M3/ShadowReceiver"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.32, 0.31, 0.31, 1)
        _ShadowTint("Shadow Tint", Color) = (0.30, 0.32, 0.40, 1)
        [HideInInspector] _ReceiverDebug("Receiver Debug", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "M3ShadowReceiver"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadowTint;
                half _ReceiverDebug;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Cascade selection must happen per pixel. GetShadowCoord in the vertex
                // stage selects an atlas tile per vertex; interpolating those coordinates
                // across Unity's coarse Plane grid creates rectangular tile boundaries and
                // clipped character shadows whenever a triangle crosses a cascade boundary.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = input.screenPos;
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = saturate(mainLight.shadowAttenuation);
                half3 color = _BaseColor.rgb * lerp(_ShadowTint.rgb, half3(1.0h, 1.0h, 1.0h), shadow);
                return half4(_ReceiverDebug > 0.5h ? shadow.xxx : color, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
