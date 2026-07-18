Shader "Sandrone/M8/VFXEmission"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset] _EmissionMask("Emission Mask", 2D) = "black" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0.05,0.72,1,1)
        _EmissionIntensity("Emission Intensity", Range(0,8)) = 2.8
        _FresnelIntensity("Fresnel Intensity", Range(0,2)) = 0.55
        _FresnelPower("Fresnel Power", Range(0.25,8)) = 3
        [HideInInspector] _M8EmissionWeight("M8 Emission Weight", Range(0,1)) = 1
        [HideInInspector] _M8DebugMode("M8 Debug Mode", Float) = 0
        [HideInInspector] _M8Role("M8 Role", Float) = 2
        [HideInInspector] _M8BloomThreshold("M8 Bloom Threshold", Float) = 1.1
        [HideInInspector] _M8AuditSlotId("M8 Audit Slot Id", Float) = 1
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+20" }
        Pass
        {
            Name "M8VFXEmission"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half3 normalWS:TEXCOORD1; half3 viewWS:TEXCOORD2; };
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_EmissionMask); SAMPLER(sampler_EmissionMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; float4 _BaseColor; float4 _EmissionColor; float _EmissionIntensity;
                float _FresnelIntensity; float _FresnelPower; float _M8EmissionWeight; float _M8DebugMode;
                float _M8Role; float _M8BloomThreshold; float _M8AuditSlotId; float _SrcBlend; float _DstBlend; float _ZWrite; float _Cull;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o; VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz); o.positionCS=p.positionCS;
                o.uv=TRANSFORM_TEX(input.uv,_BaseMap); o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.viewWS=GetWorldSpaceNormalizeViewDir(p.positionWS); return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 baseSample=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;
                half mask=SAMPLE_TEXTURE2D(_EmissionMask,sampler_EmissionMask,input.uv).r;
                half fresnel=pow(saturate(1.0h-abs(dot(normalize(input.normalWS),normalize(input.viewWS)))),max(_FresnelPower,0.25h));
                half3 emission=mask*_EmissionColor.rgb*_EmissionIntensity*_M8EmissionWeight*(1.0h+fresnel*_FresnelIntensity);
                half3 outputColor=saturate(baseSample.rgb)+emission;
                if(_M8DebugMode>0.5h&&_M8DebugMode<1.5h)outputColor=mask.xxx*saturate(_M8Role);
                else if(_M8DebugMode>1.5h&&_M8DebugMode<2.5h)outputColor=emission;
                else if(_M8DebugMode>2.5h)outputColor=step(_M8BloomThreshold,max(outputColor.r,max(outputColor.g,outputColor.b))).xxx;
                return half4(outputColor,baseSample.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
