using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A small vector painter on an RGB picture, as much of tiny-skia as openMapper's drawn map needs: shapes filled by
    /// the even-odd or the non-zero rule with soft edges, lines of a width with round or flat ends and round corners,
    /// dashes, circles and rectangles. Points are in pixels, x right and y down; colours 0xRRGGBB.
    /// </summary>
    public sealed class OmsiCanvas
    {
        // Four rows of samples to a pixel; across a row the coverage is exact.
        private const int SubRows = 4;

        private readonly float[] cover;

        public OmsiCanvas(int width, int height, uint background)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            Rgb = new byte[width * height * 3];
            for (var index = 0; index < Rgb.Length; index += 3)
            {
                Rgb[index] = (byte)(background >> 16);
                Rgb[index + 1] = (byte)(background >> 8);
                Rgb[index + 2] = (byte)background;
            }
            cover = new float[width + 2];
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Three bytes a pixel, rows from the top.</summary>
        public byte[] Rgb { get; }

        public OmsiRgbImage ToImage()
        {
            return new OmsiRgbImage(Width, Height, Rgb);
        }

        /// <summary>Fills the rings together: with even-odd, a ring inside another cuts a hole.</summary>
        public void Fill(IEnumerable<IList<OmsiMapPoint>> rings, uint colour, bool evenOdd)
        {
            var edges = new List<Edge>();
            foreach (var ring in rings)
                AddRing(edges, ring);
            Rasterize(edges, colour, evenOdd);
        }

        public void Fill(IList<OmsiMapPoint> ring, uint colour)
        {
            Fill(new[] { ring }, colour, true);
        }

        /// <summary>
        /// Draws a line of a width: round corners, round or flat ends, a closed ring joined all round, dashes of
        /// alternating drawn and left lengths from its start.
        /// </summary>
        public void Stroke(IList<OmsiMapPoint> line, double width, uint colour, bool roundEnds, bool closed = false, double[] dash = null)
        {
            Fill(StrokeOutline(line, width, roundEnds, closed, dash), colour, false);
        }

        public void FillCircle(double x, double y, double radius, uint colour)
        {
            Fill(new[] { Circle(x, y, radius) }, colour, false);
        }

        public void FillRect(double x, double y, double width, double height, uint colour)
        {
            Fill(new[] { new[] { new OmsiMapPoint(x, y), new OmsiMapPoint(x + width, y), new OmsiMapPoint(x + width, y + height), new OmsiMapPoint(x, y + height) } }, colour,
                false);
        }

        /// <summary>The colour at a pixel, 0xRRGGBB.</summary>
        public uint At(int x, int y)
        {
            var index = (y * Width + x) * 3;
            return ((uint)Rgb[index] << 16) | ((uint)Rgb[index + 1] << 8) | Rgb[index + 2];
        }

        /// <summary>
        /// The shapes that make up a line of a width, all turning the same way so that the non-zero rule joins them: a
        /// four-sided piece for each stretch and a disc at each corner (and at each end for round ends).
        /// </summary>
        public static List<IList<OmsiMapPoint>> StrokeOutline(IList<OmsiMapPoint> line, double width, bool roundEnds, bool closed = false, double[] dash = null)
        {
            var shapes = new List<IList<OmsiMapPoint>>();
            if (line == null || line.Count < 2 || width <= 0.0)
                return shapes;
            if (dash != null && dash.Length >= 2 && dash[0] > 0.0 && dash[1] > 0.0)
            {
                foreach (var piece in Dashes(line, closed, dash))
                    shapes.AddRange(StrokeOutline(piece, width, roundEnds));
                return shapes;
            }
            var half = width * 0.5;
            var count = line.Count;
            var stretches = closed ? count : count - 1;
            for (var index = 0; index < stretches; index++)
            {
                var a = line[index];
                var b = line[(index + 1) % count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-9)
                    continue;
                var nx = -dy / length * half;
                var ny = dx / length * half;
                shapes.Add(Turned(new[]
                {
                    new OmsiMapPoint(a.X + nx, a.Y + ny), new OmsiMapPoint(b.X + nx, b.Y + ny), new OmsiMapPoint(b.X - nx, b.Y - ny),
                    new OmsiMapPoint(a.X - nx, a.Y - ny)
                }));
            }
            for (var index = 0; index < count; index++)
            {
                var end = !closed && (index == 0 || index == count - 1);
                if (!end || roundEnds)
                    shapes.Add(Circle(line[index].X, line[index].Y, half));
            }
            return shapes;
        }

        /// <summary>A disc as a polygon with enough corners to look round at its size.</summary>
        public static OmsiMapPoint[] Circle(double x, double y, double radius)
        {
            var corners = (int)Math.Max(8, Math.Min(96, Math.Ceiling(radius * 2.0)));
            var points = new OmsiMapPoint[corners];
            for (var index = 0; index < corners; index++)
            {
                var angle = index * 2.0 * Math.PI / corners;
                points[index] = new OmsiMapPoint(x + Math.Cos(angle) * radius, y + Math.Sin(angle) * radius);
            }
            return Turned(points);
        }

        /// <summary>The same shape turning the way all shapes turn here (positive area in pixels).</summary>
        private static OmsiMapPoint[] Turned(OmsiMapPoint[] shape)
        {
            var area = 0.0;
            for (var index = 0; index < shape.Length; index++)
            {
                var a = shape[index];
                var b = shape[(index + 1) % shape.Length];
                area += a.X * b.Y - b.X * a.Y;
            }
            if (area < 0.0)
                Array.Reverse(shape);
            return shape;
        }

        private static IEnumerable<List<OmsiMapPoint>> Dashes(IList<OmsiMapPoint> line, bool closed, double[] dash)
        {
            var points = new List<OmsiMapPoint>(line);
            if (closed)
                points.Add(line[0]);
            var phase = 0;
            var left = dash[0];
            var current = new List<OmsiMapPoint> { points[0] };
            for (var index = 1; index < points.Count; index++)
            {
                var a = points[index - 1];
                var b = points[index];
                var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                var done = 0.0;
                while (length - done > left)
                {
                    done += left;
                    var t = done / length;
                    var at = new OmsiMapPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                    if (phase % 2 == 0)
                    {
                        current.Add(at);
                        yield return current;
                    }
                    current = new List<OmsiMapPoint> { at };
                    phase++;
                    left = dash[phase % 2];
                }
                left -= length - done;
                current.Add(b);
            }
            if (phase % 2 == 0 && current.Count >= 2)
                yield return current;
        }

        private struct Edge
        {
            public double X0;
            public double Y0;
            public double Y1;
            public double Slope;
            public int Winding;
        }

        private static void AddRing(List<Edge> edges, IList<OmsiMapPoint> ring)
        {
            if (ring == null || ring.Count < 3)
                return;
            for (var index = 0; index < ring.Count; index++)
            {
                var a = ring[index];
                var b = ring[(index + 1) % ring.Count];
                if (Math.Abs(a.Y - b.Y) < 1e-12 || double.IsNaN(a.X) || double.IsNaN(b.X) || double.IsNaN(a.Y) || double.IsNaN(b.Y))
                    continue;
                var down = a.Y < b.Y;
                var top = down ? a : b;
                var bottom = down ? b : a;
                edges.Add(new Edge { X0 = top.X, Y0 = top.Y, Y1 = bottom.Y, Slope = (bottom.X - top.X) / (bottom.Y - top.Y), Winding = down ? 1 : -1 });
            }
        }

        private void Rasterize(List<Edge> edges, uint colour, bool evenOdd)
        {
            if (edges.Count == 0)
                return;
            edges.Sort((a, b) => a.Y0.CompareTo(b.Y0));
            var firstRow = (int)Math.Max(0, Math.Floor(edges[0].Y0));
            var lastY = double.MinValue;
            foreach (var edge in edges)
                lastY = Math.Max(lastY, edge.Y1);
            var lastRow = (int)Math.Min(Height - 1, Math.Ceiling(lastY));
            var active = new List<Edge>();
            var crossings = new List<KeyValuePair<double, int>>();
            var next = 0;
            byte red = (byte)(colour >> 16), green = (byte)(colour >> 8), blue = (byte)colour;
            for (var row = firstRow; row <= lastRow; row++)
            {
                var low = Width;
                var high = -1;
                for (var sub = 0; sub < SubRows; sub++)
                {
                    var y = row + (sub + 0.5) / SubRows;
                    while (next < edges.Count && edges[next].Y0 <= y)
                        active.Add(edges[next++]);
                    active.RemoveAll(edge => edge.Y1 <= y);
                    crossings.Clear();
                    foreach (var edge in active)
                        if (edge.Y0 <= y)
                            crossings.Add(new KeyValuePair<double, int>(edge.X0 + (y - edge.Y0) * edge.Slope, edge.Winding));
                    if (crossings.Count < 2)
                        continue;
                    crossings.Sort((a, b) => a.Key.CompareTo(b.Key));
                    var winding = 0;
                    for (var index = 0; index < crossings.Count - 1; index++)
                    {
                        winding += crossings[index].Value;
                        var inside = evenOdd ? (index + 1) % 2 == 1 : winding != 0;
                        if (!inside)
                            continue;
                        var from = Math.Max(0.0, crossings[index].Key);
                        var to = Math.Min(Width, crossings[index + 1].Key);
                        if (to <= from)
                            continue;
                        AddSpan(from, to, 1f / SubRows);
                        low = Math.Min(low, (int)from);
                        high = Math.Max(high, Math.Min(Width - 1, (int)to));
                    }
                }
                for (var x = low; x <= high; x++)
                {
                    var alpha = Math.Min(1f, cover[x]);
                    cover[x] = 0f;
                    if (alpha <= 0f)
                        continue;
                    var index = (row * Width + x) * 3;
                    Rgb[index] = (byte)(Rgb[index] + (red - Rgb[index]) * alpha + 0.5f);
                    Rgb[index + 1] = (byte)(Rgb[index + 1] + (green - Rgb[index + 1]) * alpha + 0.5f);
                    Rgb[index + 2] = (byte)(Rgb[index + 2] + (blue - Rgb[index + 2]) * alpha + 0.5f);
                }
                if (high + 1 < cover.Length)
                    cover[high + 1] = 0f;
                if (next >= edges.Count && active.Count == 0)
                    break;
            }
        }

        private void AddSpan(double from, double to, float weight)
        {
            var first = (int)from;
            var last = (int)to;
            if (first == last)
            {
                cover[first] += (float)(to - from) * weight;
                return;
            }
            cover[first] += (float)(first + 1 - from) * weight;
            for (var x = first + 1; x < last; x++)
                cover[x] += weight;
            if (last < Width)
                cover[last] += (float)(to - last) * weight;
        }
    }
}
