// Contour lines (the topology view) over the terrain. The surface shares the mesh of the ground, so the height of every
// fragment is its interpolated world Y; a line lies where that height is a multiple of the spacing. The lines keep their
// width in pixels at every zoom, in the view from above and in 3D. Where the ground is flat (no change in height from
// pixel to pixel) there is no line, and where the lines would lie closer than a few pixels they fade out. Every option is
// a float uniform: no keywords, so the build cannot strip a variant of this shader.
Shader "Mapforge/ContourOverlay"
{
    Properties
    {
        _Interval ("Line spacing in metres", Float) = 1
        _MajorEvery ("Every n-th line is major", Float) = 5
        _MajorFactor ("Major line width factor", Float) = 2
        _LineWidth ("Line width in pixels", Float) = 1
        _BaseHeight ("Height of world Y = 0 (added to Y)", Float) = 0
        _MinHeight ("Lowest height of the map", Float) = 0
        _MaxHeight ("Highest height of the map", Float) = 0
        _Band ("Colour band by height, 0 or 1", Float) = 0
        _BandOpacity ("Colour band opacity", Float) = 0.4
        _Opacity ("Visibility, 0 to 1", Float) = 1
        _LineColor ("Line colour", Color) = (0.97, 0.94, 0.86, 1)
        _HaloColor ("Halo colour", Color) = (0.04, 0.04, 0.06, 0.6)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            // The same triangles as the ground below; the offset keeps the lines from fighting its depth in 3D.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Interval;
                float _MajorEvery;
                float _MajorFactor;
                float _LineWidth;
                float _BaseHeight;
                float _MinHeight;
                float _MaxHeight;
                float _Band;
                float _BandOpacity;
                float _Opacity;
                float4 _LineColor;
                float4 _HaloColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float height : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.height = TransformObjectToWorld(input.positionOS.xyz).y + _BaseHeight;
                return output;
            }

            // Distance in pixels from this fragment to the nearest whole value of h; w is the change of h per pixel.
            float LineDistance(float h, float w)
            {
                return abs(frac(h - 0.5) - 0.5) / w;
            }

            // Coverage of a line of the given width in pixels, antialiased over one pixel.
            float Coverage(float distance, float width)
            {
                return saturate(width * 0.5 + 0.5 - distance);
            }

            float3 BandColour(float t)
            {
                // Low to high: green, yellow, brown, near white.
                float3 low = float3(0.30, 0.62, 0.34);
                float3 middle = float3(0.90, 0.82, 0.40);
                float3 high = float3(0.62, 0.42, 0.26);
                float3 top = float3(0.96, 0.95, 0.93);
                float3 colour = lerp(low, middle, saturate(t * 3.0));
                colour = lerp(colour, high, saturate(t * 3.0 - 1.0));
                return lerp(colour, top, saturate(t * 3.0 - 2.0));
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float interval = max(_Interval, 0.01);
                float h = input.height / interval;
                float w = fwidth(h);

                // A flat surface has no change in height: no line (every height of such a map would otherwise be "on a line").
                float lineAlpha = 0.0;
                float haloAlpha = 0.0;
                if (w > 0.0001)
                {
                    float every = max(_MajorEvery, 1.0);
                    // Levels of lines: the base spacing and every n-th, n^2-th and n^3-th multiple of it. Lines closer than 3 px
                    // are gone and those up to 6 px fade in, so steep slopes do not turn into moire. When a level is gone the
                    // next one takes its place: with the base spacing hidden, the n-fold lines are the minor ones and the
                    // n^2-fold ones the major ones. A level coarser by two steps than a fully visible one is not drawn.
                    float previousFade = 0.0;
                    float beforePreviousFade = 0.0;
                    float scale = 1.0;
                    [unroll]
                    for (int level = 0; level < 4; level++)
                    {
                        float fade = saturate((scale / w - 3.0) / 3.0);
                        float show = fade;
                        if (level >= 2)
                            show *= 1.0 - beforePreviousFade;
                        // Minor width, major width once the next finer level shows; level 0 is always minor.
                        float width = _LineWidth;
                        if (level >= 1)
                            width = lerp(_LineWidth, _LineWidth * _MajorFactor, previousFade);
                        float lineDistance = LineDistance(h / scale, w / scale);
                        lineAlpha = max(lineAlpha, Coverage(lineDistance, width) * show);
                        haloAlpha = max(haloAlpha, Coverage(lineDistance, width + 2.0) * show);
                        beforePreviousFade = previousFade;
                        previousFade = fade;
                        scale *= every;
                    }
                }

                // The light line on a dark halo: readable on the dark map, on textures and on a light aerial picture.
                float haloOnly = haloAlpha * _HaloColor.a;
                float alpha = max(lineAlpha, haloOnly);
                float3 colour = lerp(_HaloColor.rgb, _LineColor.rgb, lineAlpha);

                float bandAlpha = 0.0;
                float3 bandColour = float3(0, 0, 0);
                float span = _MaxHeight - _MinHeight;
                if (_Band > 0.5 && span > 0.05)
                {
                    bandColour = BandColour(saturate((input.height - _MinHeight) / span));
                    bandAlpha = _BandOpacity;
                }

                float total = alpha + bandAlpha * (1.0 - alpha);
                float3 mixed = (colour * alpha + bandColour * bandAlpha * (1.0 - alpha)) / max(total, 0.0001);
                return half4(mixed, total * _Opacity);
            }
            ENDHLSL
        }
    }
}
