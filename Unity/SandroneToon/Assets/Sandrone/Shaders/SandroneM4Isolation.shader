Shader "Sandrone/M4/ValidationIsolation"
{
    Properties { _Color("Color",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; };
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            V Vert(A i){ V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 Frag(V i):SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
