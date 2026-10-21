// One ground texture of an OMSI tile: the texture times its detail texture, each repeated across the tile, blended
// over the layers below through the tile's mask, and shaded by the slope of the terrain. The first layer covers the
// tile without a mask; in the 3D view it writes depth so that hills hide what lies behind them.
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
        _DepthWrite ("Write depth", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_DepthWrite]
            ZTest LEqual
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
                float _DepthWrite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half3 colour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv * _Repeats).rgb;
                // OMSI multiplies the detail texture in plainly; its stock detail textures are bright.
                colour *= SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, input.uv * _DetailRepeats).rgb;
                half coverage = _UseMask > 0.5 ? SAMPLE_TEXTURE2D(_Mask, sampler_Mask, input.uv).r : 1.0;
                // The objects' sun; flat ground keeps its full colour, slopes away from the sun darken.
                float3 sun = normalize(float3(0.35, 0.85, -0.4));
                float3 normal = input.normalWS / max(length(input.normalWS), 1e-5);
                colour *= (0.55 + 0.45 * saturate(dot(normal, sun))) / (0.55 + 0.45 * sun.y);
                return half4(colour, coverage);
            }
            ENDHLSL
        }
    }
}
