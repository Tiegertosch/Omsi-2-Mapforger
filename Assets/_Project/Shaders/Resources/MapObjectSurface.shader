// A scenery object seen from above, drawn with GPU instancing: texture times material colour, shaded by a fixed sun so
// that roofs and walls read as shapes. Materials with [matl_alpha] cut away where the texture is transparent.
Shader "Mapforge/ObjectSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff, 0 for opaque", Float) = 0
        _UvRect ("Texture area: offset xy, size zw", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Cutoff;
                float4 _UvRect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // Tree crowns use only the crown of their side-view texture.
                output.uv = _UvRect.xy + input.uv * _UvRect.zw;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                if (_Cutoff > 0.0)
                    clip(texel.a - _Cutoff);
                // A sun from the south-east, high up; faces pointing either way are lit, as the meshes are drawn
                // without culling.
                float3 sun = normalize(float3(0.35, 0.85, -0.4));
                // Meshes without normals would give NaN with normalize().
                float3 normal = input.normalWS / max(length(input.normalWS), 1e-5);
                half light = 0.55 + 0.45 * abs(dot(normal, sun));
                return half4(texel.rgb * _Color.rgb * light, 1.0);
            }
            ENDHLSL
        }
    }
}
