Shader "Hidden/RideReflex/CaptureFogStreetLabel"
{
    Properties
    {
        _MainTex ("Immutable font atlas", 2D) = "white" {}
        _Cutoff ("Glyph edge", Range(0,1)) = 0.23
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Back
        ZWrite On
        ZTest LEqual
        Pass
        {
            Name "StreetLabelFog"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float fogFactor : TEXCOORD1; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Cutoff;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                o.uv=TRANSFORM_TEX(input.uv,_MainTex);
                o.color=input.color;
                o.fogFactor=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a;
                clip(alpha-_Cutoff);
                return half4(MixFog(input.color.rgb,input.fogFactor),1);
            }
            ENDHLSL
        }
    }
}
