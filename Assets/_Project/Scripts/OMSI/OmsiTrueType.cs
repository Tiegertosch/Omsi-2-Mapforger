using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The outlines of a TrueType font's letters (tables cmap, head, hhea, hmtx, loca and glyf, simple and composite
    /// glyphs), to write names on a drawn map the way openMapper writes them with ab_glyph: each letter's outline turned
    /// into rings of pixels, without kerning or hinting.
    /// </summary>
    public sealed class OmsiTrueTypeFont
    {
        private readonly byte[] data;
        private readonly Dictionary<int, int> glyphOfCharacter = new Dictionary<int, int>();
        private readonly int glyf;
        private readonly int loca;
        private readonly int hmtx;
        private readonly int longMetrics;
        private readonly bool longOffsets;
        private readonly int glyphCount;

        /// <exception cref="InvalidDataException">The data is not a TrueType font with outlines in glyf.</exception>
        public OmsiTrueTypeFont(byte[] data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            if (data.Length < 12 || U32(0) != 0x00010000 && U32(0) != 0x74727565)
                throw new InvalidDataException("Not a TrueType font.");
            var tables = new Dictionary<string, int>(StringComparer.Ordinal);
            var count = U16(4);
            for (var index = 0; index < count; index++)
            {
                var entry = 12 + index * 16;
                var tag = new string(new[] { (char)data[entry], (char)data[entry + 1], (char)data[entry + 2], (char)data[entry + 3] });
                tables[tag] = (int)U32(entry + 8);
            }
            foreach (var needed in new[] { "cmap", "head", "hhea", "hmtx", "loca", "glyf", "maxp" })
                if (!tables.ContainsKey(needed))
                    throw new InvalidDataException("The font has no " + needed + " table.");
            UnitsPerEm = U16(tables["head"] + 18);
            longOffsets = S16(tables["head"] + 50) != 0;
            longMetrics = U16(tables["hhea"] + 34);
            glyphCount = U16(tables["maxp"] + 4);
            glyf = tables["glyf"];
            loca = tables["loca"];
            hmtx = tables["hmtx"];
            ReadCharacterMap(tables["cmap"]);
        }

        public int UnitsPerEm { get; }

        /// <summary>
        /// A text's outline at a size in pixels: rings with the baseline at y = 0 and y down, starting at x = 0; and its
        /// width in pixels.
        /// </summary>
        public List<IList<OmsiMapPoint>> TextOutline(string text, double pixels, out double width)
        {
            var rings = new List<IList<OmsiMapPoint>>();
            var scale = pixels / UnitsPerEm;
            var pen = 0.0;
            foreach (var character in Characters(text ?? string.Empty))
            {
                glyphOfCharacter.TryGetValue(character, out var glyph);
                foreach (var contour in Glyph(glyph, 0))
                {
                    var ring = new List<OmsiMapPoint>(contour.Count);
                    foreach (var point in contour)
                        ring.Add(new OmsiMapPoint(pen + point.X * scale, -point.Y * scale));
                    rings.Add(ring);
                }
                pen += Advance(glyph) * scale;
            }
            width = pen;
            return rings;
        }

        private static IEnumerable<int> Characters(string text)
        {
            for (var index = 0; index < text.Length; index++)
            {
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    yield return char.ConvertToUtf32(text[index], text[index + 1]);
                    index++;
                }
                else
                    yield return text[index];
            }
        }

        private int Advance(int glyph)
        {
            var index = Math.Min(glyph, longMetrics - 1);
            return U16(hmtx + index * 4);
        }

        private void ReadCharacterMap(int cmap)
        {
            var count = U16(cmap + 2);
            int best = -1, bestFormat = 0;
            for (var index = 0; index < count; index++)
            {
                var record = cmap + 4 + index * 8;
                var platform = U16(record);
                var encoding = U16(record + 2);
                var offset = cmap + (int)U32(record + 4);
                var format = U16(offset);
                // Unicode tables: full repertoire (format 12) before the basic plane (format 4).
                var unicode = platform == 0 || platform == 3 && (encoding == 1 || encoding == 10);
                if (unicode && (format == 12 || format == 4 && bestFormat != 12))
                {
                    best = offset;
                    bestFormat = format;
                }
            }
            if (best < 0)
                throw new InvalidDataException("The font has no Unicode character map.");
            if (bestFormat == 12)
            {
                var groups = (int)U32(best + 12);
                for (var index = 0; index < groups; index++)
                {
                    var group = best + 16 + index * 12;
                    var start = (int)U32(group);
                    var end = (int)U32(group + 4);
                    var first = (int)U32(group + 8);
                    for (var character = start; character <= end && character - start < 0x10000; character++)
                        glyphOfCharacter[character] = first + character - start;
                }
                return;
            }
            var segments = U16(best + 6) / 2;
            var ends = best + 14;
            var starts = ends + segments * 2 + 2;
            var deltas = starts + segments * 2;
            var ranges = deltas + segments * 2;
            for (var segment = 0; segment < segments; segment++)
            {
                var end = U16(ends + segment * 2);
                var start = U16(starts + segment * 2);
                var delta = S16(deltas + segment * 2);
                var rangeOffset = U16(ranges + segment * 2);
                for (var character = start; character <= end && character != 0xFFFF; character++)
                {
                    int glyph;
                    if (rangeOffset == 0)
                        glyph = (character + delta) & 0xFFFF;
                    else
                    {
                        var at = ranges + segment * 2 + rangeOffset + (character - start) * 2;
                        glyph = at + 1 < data.Length ? U16(at) : 0;
                        if (glyph != 0)
                            glyph = (glyph + delta) & 0xFFFF;
                    }
                    if (glyph != 0)
                        glyphOfCharacter[character] = glyph;
                }
            }
        }

        /// <summary>A glyph's contours in font units, y up, curves flattened.</summary>
        private List<List<OmsiMapPoint>> Glyph(int glyph, int depth)
        {
            var contours = new List<List<OmsiMapPoint>>();
            if (glyph < 0 || glyph >= glyphCount || depth > 8)
                return contours;
            var start = longOffsets ? (int)U32(loca + glyph * 4) : U16(loca + glyph * 2) * 2;
            var end = longOffsets ? (int)U32(loca + glyph * 4 + 4) : U16(loca + glyph * 2 + 2) * 2;
            if (end <= start)
                return contours;
            var at = glyf + start;
            var contourCount = S16(at);
            if (contourCount >= 0)
                return Simple(at, contourCount);
            // A composite glyph: its parts moved, and scaled where a part says so.
            var position = at + 10;
            while (true)
            {
                var flags = U16(position);
                var part = U16(position + 2);
                position += 4;
                double dx, dy;
                if ((flags & 1) != 0)
                {
                    dx = S16(position);
                    dy = S16(position + 2);
                    position += 4;
                }
                else
                {
                    dx = (sbyte)data[position];
                    dy = (sbyte)data[position + 1];
                    position += 2;
                }
                double a = 1, b = 0, c = 0, d = 1;
                if ((flags & 8) != 0)
                {
                    a = d = S16(position) / 16384.0;
                    position += 2;
                }
                else if ((flags & 0x40) != 0)
                {
                    a = S16(position) / 16384.0;
                    d = S16(position + 2) / 16384.0;
                    position += 4;
                }
                else if ((flags & 0x80) != 0)
                {
                    a = S16(position) / 16384.0;
                    b = S16(position + 2) / 16384.0;
                    c = S16(position + 4) / 16384.0;
                    d = S16(position + 6) / 16384.0;
                    position += 8;
                }
                foreach (var contour in Glyph(part, depth + 1))
                {
                    var moved = new List<OmsiMapPoint>(contour.Count);
                    foreach (var point in contour)
                        moved.Add(new OmsiMapPoint(point.X * a + point.Y * c + dx, point.X * b + point.Y * d + dy));
                    contours.Add(moved);
                }
                if ((flags & 0x20) == 0)
                    break;
            }
            return contours;
        }

        private List<List<OmsiMapPoint>> Simple(int at, int contourCount)
        {
            var endPoints = new int[contourCount];
            for (var index = 0; index < contourCount; index++)
                endPoints[index] = U16(at + 10 + index * 2);
            var pointCount = contourCount > 0 ? endPoints[contourCount - 1] + 1 : 0;
            var instructions = U16(at + 10 + contourCount * 2);
            var position = at + 12 + contourCount * 2 + instructions;
            var flags = new byte[pointCount];
            for (var index = 0; index < pointCount;)
            {
                var flag = data[position++];
                flags[index++] = flag;
                if ((flag & 8) != 0)
                {
                    var repeat = data[position++];
                    for (var copy = 0; copy < repeat && index < pointCount; copy++)
                        flags[index++] = flag;
                }
            }
            var xs = new int[pointCount];
            var ys = new int[pointCount];
            var value = 0;
            for (var index = 0; index < pointCount; index++)
            {
                var flag = flags[index];
                if ((flag & 2) != 0)
                    value += (flag & 16) != 0 ? data[position++] : -data[position++];
                else if ((flag & 16) == 0)
                {
                    value += S16(position);
                    position += 2;
                }
                xs[index] = value;
            }
            value = 0;
            for (var index = 0; index < pointCount; index++)
            {
                var flag = flags[index];
                if ((flag & 4) != 0)
                    value += (flag & 32) != 0 ? data[position++] : -data[position++];
                else if ((flag & 32) == 0)
                {
                    value += S16(position);
                    position += 2;
                }
                ys[index] = value;
            }
            var contours = new List<List<OmsiMapPoint>>();
            var first = 0;
            foreach (var last in endPoints)
            {
                contours.Add(Flatten(xs, ys, flags, first, last));
                first = last + 1;
            }
            return contours;
        }

        /// <summary>A contour of on- and off-curve points as a ring, each quadratic curve in a few straight pieces.</summary>
        private static List<OmsiMapPoint> Flatten(int[] xs, int[] ys, byte[] flags, int first, int last)
        {
            var ring = new List<OmsiMapPoint>();
            var points = new List<KeyValuePair<OmsiMapPoint, bool>>();
            for (var index = first; index <= last; index++)
                points.Add(new KeyValuePair<OmsiMapPoint, bool>(new OmsiMapPoint(xs[index], ys[index]), (flags[index] & 1) != 0));
            if (points.Count < 2)
                return ring;
            // Start on a point on the curve; with none, on the middle between the first two.
            var on = points.FindIndex(point => point.Value);
            if (on < 0)
                points.Insert(0, new KeyValuePair<OmsiMapPoint, bool>(Middle(points[0].Key, points[1].Key), true));
            else
            {
                var turned = points.GetRange(on, points.Count - on);
                turned.AddRange(points.GetRange(0, on));
                points = turned;
            }
            var current = points[0].Key;
            ring.Add(current);
            OmsiMapPoint? control = null;
            for (var index = 1; index <= points.Count; index++)
            {
                var point = points[index % points.Count];
                if (point.Value)
                {
                    if (control.HasValue)
                        Curve(ring, current, control.Value, point.Key);
                    else
                        ring.Add(point.Key);
                    current = point.Key;
                    control = null;
                }
                else if (control.HasValue)
                {
                    var middle = Middle(control.Value, point.Key);
                    Curve(ring, current, control.Value, middle);
                    current = middle;
                    control = point.Key;
                }
                else
                    control = point.Key;
            }
            if (ring.Count > 1 && ring[ring.Count - 1].X == ring[0].X && ring[ring.Count - 1].Y == ring[0].Y)
                ring.RemoveAt(ring.Count - 1);
            return ring;
        }

        private static void Curve(List<OmsiMapPoint> ring, OmsiMapPoint from, OmsiMapPoint control, OmsiMapPoint to)
        {
            const int pieces = 6;
            for (var piece = 1; piece <= pieces; piece++)
            {
                var t = piece / (double)pieces;
                var u = 1.0 - t;
                ring.Add(new OmsiMapPoint(u * u * from.X + 2 * u * t * control.X + t * t * to.X, u * u * from.Y + 2 * u * t * control.Y + t * t * to.Y));
            }
        }

        private static OmsiMapPoint Middle(OmsiMapPoint a, OmsiMapPoint b)
        {
            return new OmsiMapPoint((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
        }

        private int U16(int at)
        {
            return (data[at] << 8) | data[at + 1];
        }

        private short S16(int at)
        {
            return (short)U16(at);
        }

        private uint U32(int at)
        {
            return ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];
        }
    }
}
