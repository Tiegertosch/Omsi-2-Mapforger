// The textured surface of a spline (road, sidewalk, track) seen from above. Opaque surfaces write depth so that the
// higher one wins where splines overlap, such as a sidewalk over a lane or a bridge over a road. Surfaces whose
// texture uses alpha ([matl_alpha]) blend over them without writing depth.
Shader "Mapforge/SplineSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SourceBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DestinationBlend ("Destination blend", Float) = 0
        _DepthWrite ("Write depth", Float) = 1
        _UseAlpha ("Use alpha", Float) = 0
        _Opacity ("Opacity", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend [_SourceBlend] [_DestinationBlend]
            ZWrite [_DepthWrite]
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _UseAlpha;
                float _Opacity;
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
                half4 colour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                if (_UseAlpha < 0.5)
                    colour.a = 1.0;
                colour.a *= _Opacity;
                return colour;
            }
            ENDHLSL
        }
    }
}
