// A scenery object, drawn with GPU instancing: texture times material colour, shaded by a fixed sun so that roofs and
// walls read as shapes. Materials with [matl_alpha] 1 cut away where the texture is transparent. A see-through material
// is drawn twice: its opaque texels with depth among the other objects, then the rest, such as glass, blended over what
// lies behind without writing depth.
Shader "Mapforge/ObjectSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _AlphaTex ("Opacity texture ([matl_transmap])", 2D) = "white" {}
        _UseAlphaTex ("Use opacity texture", Float) = 0
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff, 0 for opaque", Float) = 0
        _CutAbove ("Cut texels above this alpha, 0 for none", Float) = 0
        _UvRect ("Texture area: offset xy, size zw", Vector) = (0, 0, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SourceBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DestinationBlend ("Destination blend", Float) = 0
        _DepthWrite ("Write depth", Float) = 1
        _Blended ("Blended", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend [_SourceBlend] [_DestinationBlend]
            ZWrite [_DepthWrite]
            ZTest LEqual
            // Like OMSI, back faces are not drawn: a sign's grey back must not cover its face. Direct3D and Unity both
            // treat clockwise triangles as front faces.
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex);
            SAMPLER(sampler_AlphaTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Cutoff;
                float _CutAbove;
                float4 _UvRect;
                float _SourceBlend;
                float _DestinationBlend;
                float _DepthWrite;
                float _Blended;
                float _Cull;
                float _UseAlphaTex;
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
                // With a transparency map the main texture's alpha is reflection strength, not opacity.
                if (_UseAlphaTex > 0.5)
                    texel.a = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, input.uv).a;
                half opacity = texel.a * _Color.a;
                if (_Cutoff > 0.0)
                    clip(opacity - _Cutoff);
                // The blended pass leaves out what the opaque pass has drawn.
                if (_CutAbove > 0.0)
                    clip(_CutAbove - opacity);
                // A sun from the south-east, high up; both sides of a normal are lit alike, which suits the
                // two-sided tree planes and meshes with inconsistent normals.
                float3 sun = normalize(float3(0.35, 0.85, -0.4));
                // Meshes without normals would give NaN with normalize().
                float3 normal = input.normalWS / max(length(input.normalWS), 1e-5);
                half light = 0.55 + 0.45 * abs(dot(normal, sun));
                half alpha = _Blended > 0.5 ? opacity : 1.0;
                return half4(texel.rgb * _Color.rgb * light, alpha);
            }
            ENDHLSL
        }
    }
}
