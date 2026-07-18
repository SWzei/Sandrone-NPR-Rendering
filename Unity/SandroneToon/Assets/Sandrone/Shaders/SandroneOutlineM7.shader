Shader "Sandrone/M7/Outline"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (0.12,0.09,0.12,1)
        _OutlinePixels("Outline Pixels", Range(0,3)) = 1
        _OutlineWidthWeight("Outline Width Weight", Range(0,2)) = 1
        [HideInInspector] _OutlineMasterWeight("Outline Master Weight", Range(0,2)) = 1
        [HideInInspector] _M7DebugMode("M7 Debug Mode", Float) = 0
        [HideInInspector] _M7SlotId("M7 Slot Id", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Name "M7Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest Less
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _OutlineWidthWeight;
                float _OutlineMasterWeight;
                float _M7DebugMode;
                float _M7SlotId;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half widthWeight : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float normalLength = length(normalVS.xy);
                float2 direction = normalVS.xy / max(normalLength, 0.05);
                float vertexWeight = saturate(input.color.a);
                float width = _OutlinePixels * _OutlineWidthWeight * _OutlineMasterWeight * vertexWeight;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                positionCS.xy += direction * width * (2.0 / _ScaledScreenParams.xy) * positionCS.w;
                output.positionCS = positionCS;
                output.normalWS = normalWS;
                output.widthWeight = vertexWeight * _OutlineWidthWeight;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(_OutlineMasterWeight - 0.0001h);
                half3 color = _OutlineColor.rgb;
                if (_M7DebugMode > 0.5h && _M7DebugMode < 1.5h) color = input.widthWeight.xxx;
                else if (_M7DebugMode > 1.5h && _M7DebugMode < 2.5h) color = normalize(input.normalWS) * 0.5h + 0.5h;
                else if (_M7DebugMode > 2.5h) color = frac(half3(_M7SlotId * 0.173h, _M7SlotId * 0.317h, _M7SlotId * 0.619h));
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
