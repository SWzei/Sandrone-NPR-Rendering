Shader "Sandrone/M5/FaceSDF"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset] _RampMap("Warm/Cool Ramp", 2D) = "white" {}
        [NoScaleOffset] _ControlMap("Control Map", 2D) = "white" {}
        [NoScaleOffset] _MatCapMap("Metal MatCap", 2D) = "black" {}
        [NoScaleOffset] _FaceMap("Face SDF (Linear R)", 2D) = "white" {}
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
        [HideInInspector] _UseFaceSDF("Use Face SDF", Float) = 0
        _FaceSoftness("Face Minimum Softness", Range(0,0.1)) = 0.02
        _FaceAA("Face Derivative AA", Range(0,4)) = 1
        _FaceMirrorBlendWidth("Face Mirror Blend Width", Range(0.001,0.25)) = 0.10
        [HideInInspector] _FaceSDFWeight("Face SDF Weight", Range(0,1)) = 1
        [HideInInspector] _M5DebugMode("M5 Debug Mode", Float) = 0
        [HideInInspector] _M5AuditSlotId("M5 Audit Slot Id", Float) = -1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "M5FaceSDF"
            Tags { "LightMode"="UniversalForward" }
            // Audit-only identity channel: no stencil read/write and no color/depth effect.
            // Frame Debugger exposes Ref reliably even when unused MPB constants are stripped.
            Stencil { Ref [_M5AuditSlotId] ReadMask 0 WriteMask 0 Comp Always Pass Keep }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ _SANDRONE_FACE
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord:TEXCOORD3;
                #endif
                half3 viewDirectionWS:TEXCOORD4;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RampMap); SAMPLER(sampler_RampMap);
            TEXTURE2D(_ControlMap); SAMPLER(sampler_ControlMap);
            TEXTURE2D(_MatCapMap); SAMPLER(sampler_MatCapMap);
            TEXTURE2D(_FaceMap); SAMPLER(sampler_FaceMap);

            CBUFFER_START(UnityPerMaterial)
                // Keep this complete prefix byte-for-byte compatible with M4. The reused
                // M4 ShadowCaster pass reads this exact UnityPerMaterial layout.
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _RampRow;
                float _RampRowCount;
                float _Threshold;
                float _BandSoftness;
                float _BandAA;
                float _CastShadowStrength;
                float _CastShadowLow;
                float _CastShadowHigh;
                float _ResponseType;
                float _FeatureGroup;
                float _SpecIntensity;
                float _SpecPower;
                float _MatCapIntensity;
                float _MetalMaskFallback;
                float _AOIntensity;
                float _OverlayColorBoost;
                float _LayerWeight;
                float _Cutoff;
                float _AlphaClip;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Cull;
                float _ShadowCull;
                float _M4DebugMode;
                float _M4FeatureWeight;
                float4 _HeadForwardWS;
                float4 _HeadRightWS;
                float4 _HeadUpWS;
                // M5-only fields must remain after the shared M4 prefix.
                float _UseFaceSDF;
                float _FaceSoftness;
                float _FaceAA;
                float _FaceMirrorBlendWidth;
                float _FaceSDFWeight;
                float _M5DebugMode;
                // Diagnostic-only MPB tag. It never participates in shading and remains
                // appended after the exact M4-compatible prefix required by UsePass.
                float _M5AuditSlotId;
            CBUFFER_END

            half IsOverlayFeature() { return step(1.5h, _FeatureGroup); }

            Varyings Vert(Attributes input)
            {
                Varyings o; VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    o.shadowCoord=GetShadowCoord(p);
                #endif
                o.viewDirectionWS=GetWorldSpaceNormalizeViewDir(p.positionWS); return o;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace:FRONT_FACE_SEMANTIC):SV_Target
            {
                half4 baseSample=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;
                baseSample.a *= _LayerWeight * lerp(1.0h,_M4FeatureWeight,IsOverlayFeature());
                if (_AlphaClip > 0.5h) clip(baseSample.a-_Cutoff);

                half4 control=SAMPLE_TEXTURE2D(_ControlMap,sampler_ControlMap,input.uv);
                half faceSign=IS_FRONT_VFACE(frontFace,1.0h,-1.0h);
                half3 N=normalize(input.normalWS)*faceSign; half3 V=normalize(input.viewDirectionWS);
                float4 shadowCoord=float4(0.0,0.0,0.0,0.0);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    shadowCoord=input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light light=GetMainLight(shadowCoord); half3 L=normalize(light.direction); half3 H=normalize(L+V);
                half ndoth=saturate(dot(N,H)); half halfLambert=saturate(dot(N,L)*0.5h+0.5h);
                half formEdge=max(_BandSoftness+fwidth(halfLambert)*_BandAA,1e-4h);
                half formBand=smoothstep(_Threshold-formEdge,_Threshold+formEdge,halfLambert);

                half3 headF=normalize(_HeadForwardWS.xyz); half3 headR=normalize(_HeadRightWS.xyz); half3 headU=normalize(_HeadUpWS.xyz);
                half3 horizontal=L-headU*dot(L,headU); half horizontalLength=length(horizontal);
                horizontal=horizontalLength>1e-4h?horizontal/horizontalLength:headF;
                half faceForward=clamp(dot(horizontal,headF),-1.0h,1.0h);
                half faceRight=clamp(dot(horizontal,headR),-1.0h,1.0h);
                half faceThreshold=(1.0h-faceForward)*0.5h;
                half faceSdf=0.5h; half faceLit=formBand;
                half faceEnabled=saturate(_UseFaceSDF*_FaceSDFWeight);
                #if defined(_SANDRONE_FACE)
                {
                    // A hard sign-based UV switch makes an asymmetric authored map pop when
                    // dot(Lh, HeadRight) crosses zero. Sample both orientations and blend only
                    // in a narrow angular band. Away from the crossing this is the same mapping
                    // as the documented sign flip; at the crossing it is continuous.
                    half sdfRight=SAMPLE_TEXTURE2D(_FaceMap,sampler_FaceMap,input.uv).r;
                    half sdfLeft=SAMPLE_TEXTURE2D(_FaceMap,sampler_FaceMap,half2(1.0h-input.uv.x,input.uv.y)).r;
                    half mirrorBlend=smoothstep(-_FaceMirrorBlendWidth,_FaceMirrorBlendWidth,faceRight);
                    faceSdf=lerp(sdfLeft,sdfRight,mirrorBlend);
                    half faceEdge=max(_FaceSoftness+fwidth(faceSdf)*_FaceAA,1e-4h);
                    faceLit=smoothstep(faceThreshold-faceEdge,faceThreshold+faceEdge,faceSdf);
                }
                #endif

                half castRaw=saturate(light.shadowAttenuation);
                half castStyled=lerp(1.0h,smoothstep(_CastShadowLow,_CastShadowHigh,castRaw),_CastShadowStrength);
                half formMask=lerp(formBand,faceLit,faceEnabled); half finalLitMask=min(formMask,castStyled);
                half row=clamp(floor(_RampRow+0.5h),0.0h,max(_RampRowCount-1.0h,0.0h));
                half2 rampUV=half2(lerp(0.25h,0.75h,finalLitMask),(row+0.5h)/max(_RampRowCount,1.0h));
                half3 ramp=SAMPLE_TEXTURE2D(_RampMap,sampler_RampMap,rampUV).rgb;

                half ao=lerp(1.0h,control.g,_AOIntensity); half spec=0.0h;
                half metalMask=saturate(max(control.b,_MetalMaskFallback))*_M4FeatureWeight;
                if (_ResponseType > 0.5h && _ResponseType < 1.5h) spec=smoothstep(0.35h,0.78h,pow(ndoth,max(_SpecPower*0.25h,1.0h)))*control.r;
                else if (_ResponseType > 1.5h && _ResponseType < 2.5h)
                { half broad=pow(ndoth,max(_SpecPower*0.18h,1.0h)); spec=smoothstep(0.20h,0.55h,broad)*control.r*(0.65h+0.35h*pow(1.0h-saturate(dot(N,V)),2.0h)); }
                else if (_ResponseType > 2.5h) spec=smoothstep(0.35h,0.68h,pow(ndoth,max(_SpecPower*0.12h,1.0h)))*control.r*metalMask;
                spec *= _SpecIntensity*castStyled*light.distanceAttenuation;

                half3 normalVS=normalize(mul((half3x3)GetWorldToViewMatrix(),N)); half2 matCapUV=normalVS.xy*0.5h+0.5h; half3 matCap=0.0h;
                UNITY_BRANCH if (_MatCapIntensity>0.0001h || (_M5DebugMode>7.5h && _M5DebugMode<8.5h)) matCap=SAMPLE_TEXTURE2D(_MatCapMap,sampler_MatCapMap,matCapUV).rgb;
                half3 baseLit=baseSample.rgb*ramp*light.color*light.distanceAttenuation*ao;
                half3 specColor=lerp(half3(1.0h,0.92h,0.78h),baseSample.rgb,(_ResponseType>2.5h)?0.35h:0.0h);
                half3 outputColor=saturate(baseLit+spec*specColor+matCap*_MatCapIntensity*metalMask*castStyled);
                if (_FeatureGroup>2.5h && _FeatureGroup<3.5h) outputColor=saturate(outputColor*_OverlayColorBoost);

                if (_M5DebugMode>0.5h && _M5DebugMode<1.5h) outputColor=half3(control.r,0,0);
                else if (_M5DebugMode>1.5h && _M5DebugMode<2.5h) outputColor=half3(0,control.g,0);
                else if (_M5DebugMode>2.5h && _M5DebugMode<3.5h) outputColor=half3(0,0,control.b);
                else if (_M5DebugMode>3.5h && _M5DebugMode<4.5h) outputColor=half3(control.a,control.a,0);
                else if (_M5DebugMode>4.5h && _M5DebugMode<5.5h) outputColor=ndoth.xxx;
                else if (_M5DebugMode>5.5h && _M5DebugMode<6.5h) outputColor=spec.xxx;
                else if (_M5DebugMode>6.5h && _M5DebugMode<7.5h) outputColor=half3(matCapUV,0);
                else if (_M5DebugMode>7.5h && _M5DebugMode<8.5h) outputColor=matCap*metalMask;
                else if (_M5DebugMode>8.5h && _M5DebugMode<9.5h)
                { outputColor=_ResponseType<0.5h?half3(.12h,.12h,.12h):_ResponseType<1.5h?half3(1,.45h,.35h):_ResponseType<2.5h?half3(.35h,.55h,1):half3(1,.75h,.15h); }
                else if (_M5DebugMode>9.5h && _M5DebugMode<10.5h) outputColor=finalLitMask.xxx;
                else if (_M5DebugMode>10.5h && _M5DebugMode<11.5h) outputColor=1.0h.xxx;
                else if (_M5DebugMode>11.5h && _M5DebugMode<12.5h) outputColor=faceSdf.xxx*_UseFaceSDF;
                else if (_M5DebugMode>12.5h && _M5DebugMode<13.5h) outputColor=faceThreshold.xxx*_UseFaceSDF;
                else if (_M5DebugMode>13.5h && _M5DebugMode<14.5h) outputColor=faceLit.xxx*_UseFaceSDF;
                else if (_M5DebugMode>14.5h && _M5DebugMode<15.5h) outputColor=half3(faceRight*.5h+.5h,faceForward*.5h+.5h,dot(L,headU)*.5h+.5h)*_UseFaceSDF;
                else if (_M5DebugMode>15.5h) outputColor=abs(faceLit-formBand).xxx*_UseFaceSDF;
                return half4(outputColor,baseSample.a);
            }
            ENDHLSL
        }
        UsePass "Sandrone/M4/MaterialResponse/ShadowCaster"
    }
    FallBack Off
}
