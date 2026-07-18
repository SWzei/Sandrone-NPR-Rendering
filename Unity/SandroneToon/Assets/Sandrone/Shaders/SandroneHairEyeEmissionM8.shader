Shader "Sandrone/M8/HairEyeEmission"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset] _RampMap("Warm/Cool Ramp", 2D) = "white" {}
        [NoScaleOffset] _ControlMap("Control Map", 2D) = "white" {}
        [NoScaleOffset] _MatCapMap("Metal MatCap", 2D) = "black" {}
        _RampRow("Ramp Row", Range(0,4)) = 0
        [HideInInspector] _RampRowCount("Ramp Row Count", Float) = 5
        _Threshold("Band Threshold", Range(0,1)) = 0.5
        _BandSoftness("Band Minimum Softness", Range(0,0.1)) = 0.015
        _BandAA("Band Derivative AA", Range(0,4)) = 1
        _CastShadowStrength("Cast Shadow Strength", Range(0,1)) = 0.85
        _CastShadowLow("Cast Shadow Low", Range(0,1)) = 0.2
        _CastShadowHigh("Cast Shadow High", Range(0,1)) = 0.8
        _ResponseType("Response Type", Float) = 0
        _FeatureGroup("Feature Group", Float) = 0
        _SpecIntensity("Specular Intensity", Range(0,2)) = 0
        _SpecPower("Specular Power", Range(1,128)) = 32
        _MatCapIntensity("MatCap Intensity", Range(0,2)) = 0
        _MetalMaskFallback("Metal Mask Fallback", Range(0,1)) = 0
        _AOIntensity("Authored AO Intensity", Range(0,1)) = 1
        _OverlayColorBoost("Overlay Color Boost", Range(0.5,2)) = 1
        _LayerWeight("Layer Weight", Range(0,1)) = 1
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 0
        [HideInInspector] _ShadowCull("Shadow Cull", Float) = 2
        [HideInInspector] _M4DebugMode("M4 Debug Compatibility", Float) = 0
        [HideInInspector] _M4FeatureWeight("M4 Feature Weight", Range(0,1)) = 1

        [HideInInspector] _M6Role("M6 Role", Float) = 0
        _EyeFlatLighting("Eye Flat Lighting", Range(0,1)) = 0
        _HairSpecIntensity("Hair Specular Intensity", Range(0,1)) = 0.16
        _HairSpecPower("Hair Specular Power", Range(1,128)) = 28
        _HairSpecThreshold("Hair Specular Threshold", Range(0,1)) = 0.52
        _HairSpecSoftness("Hair Specular Softness", Range(0.001,0.25)) = 0.06
        _HairSpecColor("Hair Specular Color", Color) = (0.82,0.76,0.68,1)
        [HideInInspector] _M6HairSpecWeight("M6 Hair Specular Weight", Range(0,1)) = 1
        [HideInInspector] _M6EyeLayerWeight("M6 Eye Layer Weight", Range(0,1)) = 1
        [HideInInspector] _M6DebugMode("M6 Debug Mode", Float) = 0
        [HideInInspector] _M6StencilRef("M6 Stencil Ref", Float) = 0
        [HideInInspector] _M6StencilReadMask("M6 Stencil Read Mask", Float) = 0
        [HideInInspector] _M6StencilWriteMask("M6 Stencil Write Mask", Float) = 0
        [HideInInspector] _M6StencilComp("M6 Stencil Comparison", Float) = 8
        [HideInInspector] _M6StencilPass("M6 Stencil Pass", Float) = 0
        [HideInInspector] _M6AuditSlotId("M6 Audit Slot Id", Float) = -1

        [NoScaleOffset] _EmissionMask("Emission Mask", 2D) = "black" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0.08,0.55,1,1)
        _EmissionIntensity("Emission Intensity", Range(0,8)) = 3.2
        [HideInInspector] _M8EmissionWeight("M8 Emission Weight", Range(0,1)) = 1
        [HideInInspector] _M8DebugMode("M8 Debug Mode", Float) = 0
        [HideInInspector] _M8Role("M8 Role", Float) = 1
        [HideInInspector] _M8BloomThreshold("M8 Bloom Threshold", Float) = 1.1
        [HideInInspector] _M8AuditSlotId("M8 Audit Slot Id", Float) = 10
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "M8HairEyeEmission"
            Tags { "LightMode"="UniversalForward" }
            Stencil
            {
                Ref [_M6StencilRef]
                ReadMask [_M6StencilReadMask]
                WriteMask [_M6StencilWriteMask]
                Comp [_M6StencilComp]
                Pass [_M6StencilPass]
            }
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

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord:TEXCOORD3;
                #endif
                half3 viewDirectionWS:TEXCOORD4; half3 tangentWS:TEXCOORD5;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RampMap); SAMPLER(sampler_RampMap);
            TEXTURE2D(_ControlMap); SAMPLER(sampler_ControlMap);
            TEXTURE2D(_MatCapMap); SAMPLER(sampler_MatCapMap);
            TEXTURE2D(_EmissionMask); SAMPLER(sampler_EmissionMask);

            CBUFFER_START(UnityPerMaterial)
                // Exact M4 + M6 prefix. M8 fields are append-only so the reused ShadowCaster contract cannot drift.
                float4 _BaseMap_ST; float4 _BaseColor; float _RampRow; float _RampRowCount; float _Threshold;
                float _BandSoftness; float _BandAA; float _CastShadowStrength; float _CastShadowLow; float _CastShadowHigh;
                float _ResponseType; float _FeatureGroup; float _SpecIntensity; float _SpecPower; float _MatCapIntensity;
                float _MetalMaskFallback; float _AOIntensity; float _OverlayColorBoost; float _LayerWeight; float _Cutoff;
                float _AlphaClip; float _SrcBlend; float _DstBlend; float _ZWrite; float _Cull; float _ShadowCull;
                float _M4DebugMode; float _M4FeatureWeight; float4 _HeadForwardWS; float4 _HeadRightWS; float4 _HeadUpWS;
                float _M6Role; float _EyeFlatLighting; float _HairSpecIntensity; float _HairSpecPower; float _HairSpecThreshold;
                float _HairSpecSoftness; float4 _HairSpecColor; float _M6HairSpecWeight; float _M6EyeLayerWeight;
                float _M6DebugMode; float _M6StencilRef; float _M6StencilReadMask; float _M6StencilWriteMask;
                float _M6StencilComp; float _M6StencilPass; float _M6AuditSlotId;
                float4 _EmissionColor; float _EmissionIntensity; float _M8EmissionWeight; float _M8DebugMode;
                float _M8Role; float _M8BloomThreshold; float _M8AuditSlotId;
            CBUFFER_END

            half IsOverlayFeature() { return step(1.5h, _FeatureGroup); }
            half IsEyeRole() { return step(1.5h, _M6Role) * (1.0h - step(3.5h, _M6Role)); }
            half IsEyeLayer() { return step(2.5h, _M6Role) * (1.0h - step(3.5h, _M6Role)); }
            half IsHairBase() { return step(3.5h, _M6Role) * (1.0h - step(4.5h, _M6Role)); }

            Varyings Vert(Attributes input)
            {
                Varyings o; VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.tangentWS=TransformObjectToWorldDir(input.tangentOS.xyz); o.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    o.shadowCoord=GetShadowCoord(p);
                #endif
                o.viewDirectionWS=GetWorldSpaceNormalizeViewDir(p.positionWS); return o;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace:FRONT_FACE_SEMANTIC):SV_Target
            {
                half4 baseSample=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;
                half eyeLayerWeight=lerp(1.0h,_M6EyeLayerWeight,IsEyeLayer());
                baseSample.a*=_LayerWeight*eyeLayerWeight*lerp(1.0h,_M4FeatureWeight,IsOverlayFeature());
                if(_AlphaClip>0.5h)clip(baseSample.a-_Cutoff);
                half4 control=SAMPLE_TEXTURE2D(_ControlMap,sampler_ControlMap,input.uv);
                half faceSign=IS_FRONT_VFACE(frontFace,1.0h,-1.0h); half3 N=normalize(input.normalWS)*faceSign;
                half3 T=normalize(input.tangentWS)*faceSign; half3 V=normalize(input.viewDirectionWS);
                float4 shadowCoord=float4(0.0,0.0,0.0,0.0);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    shadowCoord=input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light light=GetMainLight(shadowCoord); half3 L=normalize(light.direction); half3 H=normalize(L+V);
                half halfLambert=saturate(dot(N,L)*0.5h+0.5h); half edge=max(_BandSoftness+fwidth(halfLambert)*_BandAA,1e-4h);
                half formBand=smoothstep(_Threshold-edge,_Threshold+edge,halfLambert); half castRaw=saturate(light.shadowAttenuation);
                half castStyled=lerp(1.0h,smoothstep(_CastShadowLow,_CastShadowHigh,castRaw),_CastShadowStrength);
                half finalLitMask=min(formBand,castStyled); half row=clamp(floor(_RampRow+0.5h),0.0h,max(_RampRowCount-1.0h,0.0h));
                half2 rampUV=half2(lerp(0.25h,0.75h,finalLitMask),(row+0.5h)/max(_RampRowCount,1.0h));
                half3 ramp=SAMPLE_TEXTURE2D(_RampMap,sampler_RampMap,rampUV).rgb; half ao=lerp(1.0h,control.g,_AOIntensity);
                half3 conventionalLighting=ramp*light.color*light.distanceAttenuation;
                half3 flatEyeLighting=max(conventionalLighting,light.color*light.distanceAttenuation*lerp(0.72h,1.0h,castStyled));
                half eyeFlat=saturate(_EyeFlatLighting*IsEyeRole());
                half3 baseLit=baseSample.rgb*lerp(conventionalLighting,flatEyeLighting,eyeFlat)*ao;
                half strand=sqrt(saturate(1.0h-dot(T,H)*dot(T,H))); half tangentLobe=pow(strand,max(_HairSpecPower*0.25h,1.0h));
                half hairEdge=max(_HairSpecSoftness+fwidth(tangentLobe),1e-4h);
                half hairBand=smoothstep(_HairSpecThreshold-hairEdge,_HairSpecThreshold+hairEdge,tangentLobe);
                half hairSpec=hairBand*control.r*_HairSpecIntensity*_M6HairSpecWeight*IsHairBase()*castStyled*light.distanceAttenuation;
                half3 baseOutput=saturate(baseLit+hairSpec*_HairSpecColor.rgb);
                if(_FeatureGroup>2.5h&&_FeatureGroup<3.5h)baseOutput=saturate(baseOutput*_OverlayColorBoost);

                half emissionMask=SAMPLE_TEXTURE2D(_EmissionMask,sampler_EmissionMask,input.uv).r;
                half3 emission=emissionMask*_EmissionColor.rgb*_EmissionIntensity*_M8EmissionWeight;
                half3 outputColor=baseOutput+emission;
                if(_M8DebugMode>0.5h&&_M8DebugMode<1.5h)outputColor=emissionMask.xxx*saturate(_M8Role);
                else if(_M8DebugMode>1.5h&&_M8DebugMode<2.5h)outputColor=emission;
                else if(_M8DebugMode>2.5h)outputColor=step(_M8BloomThreshold,max(outputColor.r,max(outputColor.g,outputColor.b))).xxx;
                return half4(outputColor,baseSample.a);
            }
            ENDHLSL
        }
        UsePass "Sandrone/M4/MaterialResponse/ShadowCaster"
    }
    FallBack Off
}
