// One ground texture of an OMSI tile, seen from above: the texture times its detail texture, each repeated across
// the tile, blended over the layers below through the tile's mask. The first layer covers the tile without a mask.
Shader "Mapforge/GroundLayer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DetailTex ("Detail texture", 2D) = "white" {}
        _Mask ("Mask", 2D) = "white" {}
        _Repeats ("Repeats per tile", Float) = 1
        _DetailRepeats ("Detail repeats per tile", Float) = 1
        _UseMask ("Use mask", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);
            TEXTURE2D(_Mask);
            SAMPLER(sampler_Mask);

            CBUFFER_START(UnityPerMaterial)
                float _Repeats;
                float _DetailRepeats;
                float _UseMask;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half3 colour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv * _Repeats).rgb;
                // OMSI multiplies the detail texture in plainly; its stock detail textures are bright.
                colour *= SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, input.uv * _DetailRepeats).rgb;
                half coverage = _UseMask > 0.5 ? SAMPLE_TEXTURE2D(_Mask, sampler_Mask, input.uv).r : 1.0;
                return half4(colour, coverage);
            }
            ENDHLSL
        }
    }
}
