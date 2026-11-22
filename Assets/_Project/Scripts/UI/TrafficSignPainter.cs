using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>The traffic signs the editor draws itself (ADR 098, M4); no picture of the game or the internet is used.</summary>
    internal enum TrafficSignKind
    {
        /// <summary>No rule: a grey ring.</summary>
        Normal,

        /// <summary>StVO 306: a yellow diamond.</summary>
        PriorityRoad,

        /// <summary>StVO 205: a red-bordered triangle on its point.</summary>
        Yield,

        /// <summary>StVO 274: a red-bordered round sign with the speed.</summary>
        Speed,

        /// <summary>StVO 276: a red-bordered round sign with a black and a red car.</summary>
        OvertakingBan
    }

    /// <summary>
    /// Draws the signs with Painter2D, so tiles, the sign dragged over the map and the markers on the map show the same picture, in 2D and
    /// in 3D. Digits are strokes (the painter has no text), which look the same everywhere. A sign the owner supplies as an SVG file
    /// (<see cref="TrafficSignSvgs"/>) is drawn from that instead, and this drawing stays as the fallback.
    /// </summary>
    internal static class TrafficSignPainter
    {
        private static readonly Color Red = new Color(0.80f, 0.09f, 0.12f);
        private static readonly Color Yellow = new Color(0.98f, 0.80f, 0.10f);
        private static readonly Color White = new Color(0.97f, 0.97f, 0.95f);
        private static readonly Color Black = new Color(0.07f, 0.07f, 0.08f);
        private static readonly Color Grey = new Color(0.62f, 0.65f, 0.70f);
        private static readonly Color Backdrop = new Color(0.07f, 0.09f, 0.13f, 0.78f);

        /// <summary>The sign a level of the right of way shows.</summary>
        public static TrafficSignKind Of(OmsiRightOfWaySign sign)
        {
            switch (sign)
            {
                case OmsiRightOfWaySign.PriorityRoad:
                    return TrafficSignKind.PriorityRoad;
                case OmsiRightOfWaySign.Yield:
                    return TrafficSignKind.Yield;
                default:
                    return TrafficSignKind.Normal;
            }
        }

        /// <summary>
        /// Draws a sign around a centre, <paramref name="radius"/> pixels from its middle to the edge of its widest point.
        /// </summary>
        /// <param name="kmh">The number a <see cref="TrafficSignKind.Speed"/> shows; 0 for none (three dots).</param>
        /// <param name="backdrop">True to put a dark disc behind it, so it reads on any ground (the markers on the map).</param>
        public static void Draw(Painter2D painter, TrafficSignKind kind, int kmh, Vector2 centre, float radius, bool backdrop = false)
        {
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            if (backdrop)
            {
                Disc(painter, centre, radius * 1.18f, Backdrop);
            }
            if (TrafficSignSvgs.TryDraw(painter, kind, kmh, centre, radius))
                return;
            switch (kind)
            {
                case TrafficSignKind.PriorityRoad:
                    Poly(painter, Diamond(centre, radius * 1.02f), Black);
                    Poly(painter, Diamond(centre, radius * 0.94f), White);
                    Poly(painter, Diamond(centre, radius * 0.76f), Yellow);
                    break;
                case TrafficSignKind.Yield:
                    DrawTriangle(painter, centre, radius);
                    break;
                case TrafficSignKind.Speed:
                    Disc(painter, centre, radius, Red);
                    Disc(painter, centre, radius * 0.72f, White);
                    DrawGlyphs(painter, kmh > 0 ? kmh.ToString(CultureInfo.InvariantCulture) : "...", centre, radius * 0.5f, radius * 1.1f, Black, radius * 0.11f);
                    break;
                case TrafficSignKind.OvertakingBan:
                    Disc(painter, centre, radius, Red);
                    Disc(painter, centre, radius * 0.72f, White);
                    DrawCar(painter, centre + new Vector2(-radius * 0.29f, radius * 0.04f), radius * 0.5f, Black);
                    DrawCar(painter, centre + new Vector2(radius * 0.3f, radius * 0.04f), radius * 0.5f, Red);
                    break;
                default:
                    painter.strokeColor = Grey;
                    painter.lineWidth = radius * 0.16f;
                    painter.BeginPath();
                    painter.Arc(centre, radius * 0.66f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Stroke();
                    break;
            }
        }

        /// <summary>A sign with a thin outline around it, for the one under the pointer or the one armed.</summary>
        public static void Ring(Painter2D painter, Vector2 centre, float radius, Color colour, float width)
        {
            painter.strokeColor = colour;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.Stroke();
        }

        private static void DrawTriangle(Painter2D painter, Vector2 centre, float radius)
        {
            // An equilateral triangle on its point as wide as the sign; its middle (not its bounding box) sits at the centre of the sign.
            var corners = new[] { new Vector2(-radius, -0.87f * radius), new Vector2(radius, -0.87f * radius), new Vector2(0f, 0.87f * radius) };
            var middle = (corners[0] + corners[1] + corners[2]) / 3f;
            var outer = new Vector2[3];
            var white = new Vector2[3];
            for (var index = 0; index < 3; index++)
            {
                outer[index] = centre + (corners[index] - middle) * 1.0f;
                white[index] = centre + (corners[index] - middle) * 0.56f;
            }
            Poly(painter, Scale(outer, centre, 1.04f), White);
            Poly(painter, outer, Red);
            Poly(painter, white, White);
        }

        private static Vector2[] Scale(Vector2[] points, Vector2 about, float factor)
        {
            var scaled = new Vector2[points.Length];
            for (var index = 0; index < points.Length; index++)
                scaled[index] = about + (points[index] - about) * factor;
            return scaled;
        }

        private static Vector2[] Diamond(Vector2 centre, float half)
        {
            return new[] { centre + new Vector2(0f, -half), centre + new Vector2(half, 0f), centre + new Vector2(0f, half), centre + new Vector2(-half, 0f) };
        }

        private static void Poly(Painter2D painter, Vector2[] points, Color colour)
        {
            painter.fillColor = colour;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (var index = 1; index < points.Length; index++)
                painter.LineTo(points[index]);
            painter.ClosePath();
            painter.Fill();
        }

        private static void Disc(Painter2D painter, Vector2 centre, float radius, Color colour)
        {
            painter.fillColor = colour;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.Fill();
        }

        private static void Line(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        /// <summary>A car seen from the side, about one unit wide, facing right.</summary>
        private static void DrawCar(Painter2D painter, Vector2 centre, float size, Color colour)
        {
            var body = new[]
            {
                new Vector2(-0.5f, 0.12f), new Vector2(-0.5f, -0.05f), new Vector2(-0.34f, -0.16f), new Vector2(-0.2f, -0.4f), new Vector2(0.14f, -0.4f),
                new Vector2(0.3f, -0.16f), new Vector2(0.5f, -0.1f), new Vector2(0.5f, 0.12f)
            };
            var points = new Vector2[body.Length];
            for (var index = 0; index < body.Length; index++)
                points[index] = centre + body[index] * size;
            Poly(painter, points, colour);
            Disc(painter, centre + new Vector2(-0.3f, 0.14f) * size, size * 0.1f, colour);
            Disc(painter, centre + new Vector2(0.3f, 0.14f) * size, size * 0.1f, colour);
        }

        // Digits as a seven-segment display: a top, upper right, lower right, bottom, lower left, upper left and middle bar.
        private static readonly string[] Segments = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };

        private static readonly Dictionary<char, Vector2[][]> Letters = new Dictionary<char, Vector2[][]>
        {
            ['.'] = new[] { new[] { new Vector2(0.5f, 0.97f), new Vector2(0.5f, 1f) } }
        };

        /// <summary>Writes digits and dots centred at a point with a given height, narrowed to fit a width.</summary>
        private static void DrawGlyphs(Painter2D painter, string text, Vector2 centre, float height, float maxWidth, Color colour, float strokeWidth)
        {
            var letterWidth = height * 0.62f;
            var gap = height * 0.3f;
            var width = text.Length * letterWidth + (text.Length - 1) * gap;
            var scale = width > maxWidth ? maxWidth / width : 1f;
            letterWidth *= scale;
            gap *= scale;
            width *= scale;
            painter.strokeColor = colour;
            painter.lineWidth = Mathf.Max(1f, strokeWidth);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            var left = centre.x - width / 2f;
            var top = centre.y - height / 2f;
            for (var index = 0; index < text.Length; index++)
            {
                var x = left + index * (letterWidth + gap);
                Vector2 At(float u, float v) => new Vector2(x + u * letterWidth, top + v * height);
                var ch = text[index];
                if (ch >= '0' && ch <= '9')
                {
                    foreach (var segment in Segments[ch - '0'])
                        switch (segment)
                        {
                            case 'a': Line(painter, At(0f, 0f), At(1f, 0f)); break;
                            case 'b': Line(painter, At(1f, 0f), At(1f, 0.5f)); break;
                            case 'c': Line(painter, At(1f, 0.5f), At(1f, 1f)); break;
                            case 'd': Line(painter, At(0f, 1f), At(1f, 1f)); break;
                            case 'e': Line(painter, At(0f, 0.5f), At(0f, 1f)); break;
                            case 'f': Line(painter, At(0f, 0f), At(0f, 0.5f)); break;
                            default: Line(painter, At(0f, 0.5f), At(1f, 0.5f)); break;
                        }
                }
                else if (Letters.TryGetValue(ch, out var strokes))
                {
                    foreach (var stroke in strokes)
                    {
                        painter.BeginPath();
                        painter.MoveTo(At(stroke[0].x, stroke[0].y));
                        for (var point = 1; point < stroke.Length; point++)
                            painter.LineTo(At(stroke[point].x, stroke[point].y));
                        painter.Stroke();
                    }
                }
            }
        }
    }

    /// <summary>A sign as an element: in a tile of the catalog, and under the pointer while one is dragged to the map.</summary>
    internal sealed class TrafficSignElement : VisualElement
    {
        private TrafficSignKind kind;
        private int kmh;

        public TrafficSignElement(TrafficSignKind kind, int kmh = 0)
        {
            this.kind = kind;
            this.kmh = kmh;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public TrafficSignKind Kind => kind;

        public int Kmh => kmh;

        public void Set(TrafficSignKind newKind, int newKmh)
        {
            if (newKind == kind && newKmh == kmh)
                return;
            kind = newKind;
            kmh = newKmh;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var size = contentRect.size;
            if (size.x <= 0f || size.y <= 0f)
                return;
            TrafficSignPainter.Draw(context.painter2D, kind, kmh, size / 2f, Mathf.Min(size.x, size.y) / 2f * 0.9f);
        }
    }
}
