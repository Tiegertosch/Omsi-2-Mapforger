using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Road markings of junctions the editor makes (ADR 044), after the StVO and the RMS.</summary>
    public enum OmsiMarkingStyle
    {
        /// <summary>Leitlinie (sign 340): 0.12 m wide, 3 m strokes and 6 m gaps, as in towns.</summary>
        LaneLine,

        /// <summary>Fahrstreifenbegrenzung (sign 295): 0.12 m, unbroken.</summary>
        LaneBoundary,

        /// <summary>Doppelte Fahrstreifenbegrenzung: two unbroken lines of 0.12 m, 0.12 m apart.</summary>
        DoubleBoundary,

        /// <summary>Fahrbahnbegrenzung as a Breitstrich: 0.25 m, unbroken.</summary>
        EdgeLine,

        /// <summary>Unterbrochener Breitstrich, as across the mouth of a side road: 0.25 m, 3 m strokes and 1.5 m gaps.</summary>
        BrokenEdgeLine,

        /// <summary>Haltlinie (sign 294): 0.5 m, unbroken.</summary>
        StopLine,

        /// <summary>Wartelinie (sign 341): 0.5 m wide, 0.5 m strokes and 0.25 m gaps.</summary>
        GiveWayLine,

        /// <summary>A yellow Leitlinie, as for road works.</summary>
        YellowLaneLine,

        /// <summary>A yellow Fahrstreifenbegrenzung, as for road works.</summary>
        YellowBoundary
    }

    /// <summary>How a marking style is drawn and named.</summary>
    public sealed class OmsiMarkingStyleInfo
    {
        private OmsiMarkingStyleInfo(OmsiMarkingStyle style, string key, string name, string description, double width, double stroke, double gap,
            bool twoLines, bool yellow)
        {
            Style = style;
            Key = key;
            Name = name;
            Description = description;
            Width = width;
            Stroke = stroke;
            Gap = gap;
            TwoLines = twoLines;
            Yellow = yellow;
        }

        public OmsiMarkingStyle Style { get; }

        /// <summary>The word the .sco stores the style by.</summary>
        public string Key { get; }

        public string Name { get; }
        public string Description { get; }

        /// <summary>Width of the line in metres; of each of two lines for <see cref="TwoLines"/>, which lie that far apart.</summary>
        public double Width { get; }

        /// <summary>Length of each stroke and gap in metres; 0 for an unbroken line.</summary>
        public double Stroke { get; }

        public double Gap { get; }
        public bool TwoLines { get; }
        public bool Yellow { get; }

        public static readonly IReadOnlyList<OmsiMarkingStyleInfo> All = new[]
        {
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.LaneLine, "laneline", "Lane line", "Leitlinie (sign 340): 0.12 m, 3 m strokes, 6 m gaps", 0.12, 3.0, 6.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.LaneBoundary, "boundary", "Lane boundary", "Fahrstreifenbegrenzung (sign 295): 0.12 m, unbroken", 0.12, 0.0, 0.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.DoubleBoundary, "double", "Double boundary", "Doppelte Fahrstreifenbegrenzung: two lines of 0.12 m, 0.12 m apart", 0.12, 0.0, 0.0, true, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.EdgeLine, "edge", "Edge line", "Fahrbahnbegrenzung (Breitstrich): 0.25 m, unbroken", 0.25, 0.0, 0.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.BrokenEdgeLine, "brokenedge", "Broken edge line", "Unterbrochener Breitstrich, as across a side road: 0.25 m, 3 m strokes, 1.5 m gaps", 0.25, 3.0, 1.5, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.StopLine, "stop", "Stop line", "Haltlinie (sign 294): 0.5 m, unbroken", 0.5, 0.0, 0.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.GiveWayLine, "giveway", "Give-way line", "Wartelinie (sign 341): 0.5 m, 0.5 m strokes, 0.25 m gaps", 0.5, 0.5, 0.25, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.YellowLaneLine, "yellowlaneline", "Yellow lane line", "A yellow Leitlinie, as for road works", 0.12, 3.0, 6.0, false, true),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.YellowBoundary, "yellowboundary", "Yellow boundary", "A yellow Fahrstreifenbegrenzung, as for road works", 0.12, 0.0, 0.0, false, true)
        };

        public static OmsiMarkingStyleInfo Of(OmsiMarkingStyle style)
        {
            foreach (var info in All)
                if (info.Style == style)
                    return info;
            return All[0];
        }

        public static OmsiMarkingStyleInfo ByKey(string key)
        {
            foreach (var info in All)
                if (string.Equals(info.Key, key, StringComparison.OrdinalIgnoreCase))
                    return info;
            return null;
        }
    }

    /// <summary>
    /// A point a marking line can start or end at: on an arm of the junction, at its end where the road joins it (row 0)
    /// or at its mouth where the kerbs turn off (row 1), on a carriageway edge, a lane boundary or a median edge. In the
    /// object frame: x right, y forward, heights up.
    /// </summary>
    public sealed class OmsiMarkingAnchor
    {
        public int Arm { get; internal set; }

        /// <summary>0 at the arm's end, 1 at its mouth.</summary>
        public int Row { get; internal set; }

        /// <summary>Its place across the arm, from the left as seen leaving the junction.</summary>
        public int Index { get; internal set; }

        public double X { get; internal set; }
        public double Y { get; internal set; }
        public double Height { get; internal set; }

        /// <summary>The direction into the junction along the arm.</summary>
        public double InwardDegrees { get; internal set; }

        /// <summary>"edge", "lane" or "median".</summary>
        public string Kind { get; internal set; }

        /// <summary>How the .sco names it: arm, row and index, such as <c>2.0.1</c>.</summary>
        public string Key => Arm.ToString(CultureInfo.InvariantCulture) + "." + Row.ToString(CultureInfo.InvariantCulture) + "." +
                             Index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A marking line between two anchors, in a style, with the curvature of its curve or null for one like a circle.</summary>
    public sealed class OmsiMarkingLine
    {
        public string From { get; set; }
        public string To { get; set; }
        public OmsiMarkingStyle Style { get; set; }

        /// <summary>
        /// How far the curve's control points lie from its ends along their directions, as a share of the distance between
        /// the ends: 0 is straight, null takes the share that comes closest to a circular arc. Lines on one arm are straight.
        /// </summary>
        public double? Curvature { get; set; }

        public OmsiMarkingLine Copy()
        {
            return new OmsiMarkingLine { From = From, To = To, Style = Style, Curvature = Curvature };
        }
    }

    /// <summary>
    /// Road markings of junctions the editor makes (ADR 044), modelled on Town Road Lane for Cities: Skylines II: lines only
    /// run between anchors on the arms, as curves leaving each anchor along its lane, and are built into the junction's
    /// model as flat strips just above the carriageway. The .sco keeps them as a line of text, so the junction can be made
    /// again with them, such as with an arm turned.
    /// </summary>
    public static class OmsiJunctionMarkings
    {
        internal const string Prefix = "MapWorks markings:";

        /// <summary>Strips lie this far above the carriageway, so they never flicker with it.</summary>
        public const double Lift = 0.02;

        /// <summary>The largest curvature a line takes; beyond it short curves loop back on themselves.</summary>
        public const double MaximumCurvature = 0.8;

        public const string WhiteTexture = "MapWorks_marking_white.bmp";
        public const string YellowTexture = "MapWorks_marking_yellow.bmp";

        /// <summary>Points along curves lie this far apart at most.</summary>
        private const double SampleMeters = 0.25;

        /// <summary>
        /// The anchors of a junction's arms, in the order of its connection points: on each arm its carriageway edges, the
        /// boundaries between neighbouring lanes and the edges of a median, at the arm's end and at its mouth.
        /// </summary>
        public static List<OmsiMarkingAnchor> Anchors(OmsiJunctionLayout layout)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            var anchors = new List<OmsiMarkingAnchor>();
            for (var arm = 0; arm < layout.Arms.Count; arm++)
            {
                var junctionArm = layout.Arms[arm];
                if (junctionArm.Profile == null)
                    continue;
                var section = OmsiProfileSections.Of(junctionArm.Profile);
                var across = Across(junctionArm.Profile, section);
                var heading = junctionArm.HeadingDegrees * Math.PI / 180.0;
                for (var row = 0; row < 2; row++)
                {
                    var along = row == 0 ? junctionArm.Length : Math.Max(0.0, junctionArm.Length - OmsiJunctionLayout.ArmExtension);
                    for (var index = 0; index < across.Count; index++)
                        anchors.Add(new OmsiMarkingAnchor
                        {
                            Arm = arm, Row = row, Index = index,
                            X = Math.Sin(heading) * along + Math.Cos(heading) * across[index].Key,
                            Y = Math.Cos(heading) * along - Math.Sin(heading) * across[index].Key,
                            Height = section.RoadHeight, InwardDegrees = junctionArm.HeadingDegrees + 180.0, Kind = across[index].Value
                        });
                }
            }
            return anchors;
        }

        /// <summary>Where anchors lie across an arm, metres right of it, with their kind; left to right.</summary>
        private static List<KeyValuePair<double, string>> Across(OmsiSplineProfile profile, OmsiProfileSections section)
        {
            var places = new List<KeyValuePair<double, string>>
            {
                new KeyValuePair<double, string>(-section.RoadLeft, "edge"), new KeyValuePair<double, string>(section.RoadRight, "edge")
            };
            var lanes = profile.Lanes.FindAll(lane => lane.Kind == 0);
            lanes.Sort((first, second) => first.X.CompareTo(second.X));
            for (var index = 1; index < lanes.Count; index++)
            {
                var left = lanes[index - 1];
                var right = lanes[index];
                if (right.X - left.X < 0.05)
                    continue;
                var leftHalf = (left.Width > 0.0 ? left.Width : 3.0) / 2.0;
                var rightHalf = (right.Width > 0.0 ? right.Width : 3.0) / 2.0;
                // Lanes farther apart than they are wide have a median between them, with an edge on each side.
                if (right.X - left.X > leftHalf + rightHalf + 0.5)
                {
                    places.Add(new KeyValuePair<double, string>(left.X + leftHalf, "median"));
                    places.Add(new KeyValuePair<double, string>(right.X - rightHalf, "median"));
                }
                else
                    places.Add(new KeyValuePair<double, string>((left.X + right.X) / 2.0, "lane"));
            }
            places.RemoveAll(place => place.Key < -section.RoadLeft - 0.01 || place.Key > section.RoadRight + 0.01);
            places.Sort((first, second) => first.Key.CompareTo(second.Key));
            for (var index = places.Count - 1; index > 0; index--)
                if (places[index].Key - places[index - 1].Key < 0.05)
                    places.RemoveAt(places[index].Value == "edge" ? index - 1 : index);
            return places;
        }

        /// <summary>
        /// The centre line of a marking as points (x, y, height) in the object frame, or null when an anchor is missing.
        /// Between arms it is a cubic curve leaving each anchor into the junction along its arm; on one arm it is straight.
        /// </summary>
        public static List<double[]> Path(IReadOnlyList<OmsiMarkingAnchor> anchors, OmsiMarkingLine line)
        {
            var from = Find(anchors, line.From);
            var to = Find(anchors, line.To);
            if (from == null || to == null || ReferenceEquals(from, to))
                return null;
            var chord = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            var points = new List<double[]>();
            if (from.Arm == to.Arm || chord < 1e-6)
            {
                points.Add(new[] { from.X, from.Y, from.Height });
                points.Add(new[] { to.X, to.Y, to.Height });
                return points;
            }
            var fromX = Math.Sin(from.InwardDegrees * Math.PI / 180.0);
            var fromY = Math.Cos(from.InwardDegrees * Math.PI / 180.0);
            var toX = Math.Sin(to.InwardDegrees * Math.PI / 180.0);
            var toY = Math.Cos(to.InwardDegrees * Math.PI / 180.0);
            var pull = Math.Max(0.0, Math.Min(MaximumCurvature, line.Curvature ?? CircularCurvature(fromX, fromY, -toX, -toY))) * chord;
            double ax = from.X, ay = from.Y;
            double bx = from.X + fromX * pull, by = from.Y + fromY * pull;
            double cx = to.X + toX * pull, cy = to.Y + toY * pull;
            double dx = to.X, dy = to.Y;
            var count = Math.Max(8, (int)Math.Ceiling(chord * 1.5 / SampleMeters));
            for (var step = 0; step <= count; step++)
            {
                var t = (double)step / count;
                var u = 1.0 - t;
                var x = u * u * u * ax + 3.0 * u * u * t * bx + 3.0 * u * t * t * cx + t * t * t * dx;
                var y = u * u * u * ay + 3.0 * u * u * t * by + 3.0 * u * t * t * cy + t * t * t * dy;
                points.Add(new[] { x, y, from.Height + (to.Height - from.Height) * t });
            }
            return points;
        }

        /// <summary>
        /// The curvature that brings a cubic curve between two directions closest to a circular arc: the control points lie
        /// (4/3)·tan(θ/4)·R from the ends of an arc of radius R turning by θ, over its chord 2·R·sin(θ/2). A third for a
        /// straight line. The approach follows Town Road Lane's adaptive pull factor.
        /// </summary>
        public static double CircularCurvature(double startX, double startY, double endX, double endY)
        {
            var cos = Math.Max(-1.0, Math.Min(1.0, startX * endX + startY * endY));
            var theta = Math.Acos(cos);
            if (theta < 1e-3)
                return 1.0 / 3.0;
            return Math.Max(0.0, Math.Min(MaximumCurvature, 4.0 / 3.0 * Math.Tan(theta / 4.0) / (2.0 * Math.Sin(theta / 2.0))));
        }

        private static OmsiMarkingAnchor Find(IReadOnlyList<OmsiMarkingAnchor> anchors, string key)
        {
            foreach (var anchor in anchors)
                if (string.Equals(anchor.Key, key, StringComparison.Ordinal))
                    return anchor;
            return null;
        }

        /// <summary>
        /// Adds the markings to a model: each line as flat strips just above the carriageway, its strokes cut along its
        /// length, two strips for a double line, in white or yellow. Lines whose anchors the junction no longer has are left out.
        /// </summary>
        public static void AddMeshes(IReadOnlyList<OmsiMarkingAnchor> anchors, IEnumerable<OmsiMarkingLine> lines, List<OmsiGeneratedMesh> meshes)
        {
            OmsiGeneratedMesh white = null, yellow = null;
            foreach (var line in lines)
            {
                var path = Path(anchors, line);
                if (path == null)
                    continue;
                var info = OmsiMarkingStyleInfo.Of(line.Style);
                OmsiGeneratedMesh mesh;
                if (info.Yellow)
                    mesh = yellow = yellow ?? new OmsiGeneratedMesh { Texture = YellowTexture };
                else
                    mesh = white = white ?? new OmsiGeneratedMesh { Texture = WhiteTexture };
                var distances = new double[path.Count];
                for (var index = 1; index < path.Count; index++)
                    distances[index] = distances[index - 1] + Math.Sqrt(Square(path[index][0] - path[index - 1][0]) + Square(path[index][1] - path[index - 1][1]));
                var length = distances[path.Count - 1];
                var offsets = info.TwoLines ? new[] { -info.Width, info.Width } : new[] { 0.0 };
                foreach (var offset in offsets)
                {
                    if (info.Stroke <= 0.0)
                    {
                        Strip(mesh, path, distances, 0.0, length, offset, info.Width);
                        continue;
                    }
                    // Strokes from the start, the last one cut short where the line ends.
                    for (var start = 0.0; start < length - 0.01; start += info.Stroke + info.Gap)
                        Strip(mesh, path, distances, start, Math.Min(length, start + info.Stroke), offset, info.Width);
                }
            }
            if (white != null && white.Triangles.Count > 0)
                meshes.Add(white);
            if (yellow != null && yellow.Triangles.Count > 0)
                meshes.Add(yellow);
        }

        /// <summary>A flat strip along a path from one distance to another, <paramref name="offset"/> metres to its right.</summary>
        private static void Strip(OmsiGeneratedMesh mesh, List<double[]> path, double[] distances, double from, double to, double offset, double width)
        {
            if (to - from < 0.01)
                return;
            var centre = new List<double[]> { At(path, distances, from) };
            for (var index = 0; index < path.Count; index++)
                if (distances[index] > from + 1e-6 && distances[index] < to - 1e-6)
                    centre.Add(path[index]);
            centre.Add(At(path, distances, to));
            var first = mesh.Positions.Count / 3;
            for (var index = 0; index < centre.Count; index++)
            {
                var before = centre[Math.Max(0, index - 1)];
                var after = centre[Math.Min(centre.Count - 1, index + 1)];
                var dx = after[0] - before[0];
                var dy = after[1] - before[1];
                var size = Math.Sqrt(dx * dx + dy * dy);
                if (size < 1e-9)
                {
                    dx = 0.0;
                    dy = 1.0;
                    size = 1.0;
                }
                // Right of the direction (dx, dy) in plan is (dy, -dx).
                var rightX = dy / size;
                var rightY = -dx / size;
                foreach (var side in new[] { -0.5, 0.5 })
                {
                    var across = offset + side * width;
                    mesh.Positions.Add(centre[index][0] + rightX * across);
                    mesh.Positions.Add(centre[index][2] + Lift);
                    mesh.Positions.Add(centre[index][1] + rightY * across);
                    mesh.Uvs.Add(side < 0.0 ? 0.0 : 1.0);
                    mesh.Uvs.Add(0.0);
                }
            }
            for (var index = 0; index + 1 < centre.Count; index++)
            {
                var a = first + index * 2;
                OmsiJunctionLayout.AddUpward(mesh, a, a + 1, a + 2);
                OmsiJunctionLayout.AddUpward(mesh, a + 1, a + 3, a + 2);
            }
        }

        private static double[] At(List<double[]> path, double[] distances, double distance)
        {
            for (var index = 1; index < path.Count; index++)
                if (distances[index] >= distance || index == path.Count - 1)
                {
                    var span = distances[index] - distances[index - 1];
                    var t = span < 1e-9 ? 0.0 : Math.Max(0.0, Math.Min(1.0, (distance - distances[index - 1]) / span));
                    var a = path[index - 1];
                    var b = path[index];
                    return new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };
                }
            return path[0];
        }

        private static double Square(double value)
        {
            return value * value;
        }

        /// <summary>The line of a .sco that keeps the markings, such as <c>MapWorks markings: 0.0.1-2.0.1 laneline auto; 1.1.0-1.1.2 stop 0</c>.</summary>
        public static string Write(IEnumerable<OmsiMarkingLine> lines)
        {
            var text = new StringBuilder(Prefix);
            var first = true;
            foreach (var line in lines)
            {
                text.Append(first ? " " : "; ").Append(line.From).Append('-').Append(line.To).Append(' ')
                    .Append(OmsiMarkingStyleInfo.Of(line.Style).Key).Append(' ')
                    .Append(line.Curvature.HasValue ? line.Curvature.Value.ToString("0.###", CultureInfo.InvariantCulture) : "auto");
                first = false;
            }
            return text.ToString();
        }

        /// <summary>The markings in the text of a .sco; none without a markings line. Lines it cannot read are left out.</summary>
        public static List<OmsiMarkingLine> Read(string scoText)
        {
            var lines = new List<OmsiMarkingLine>();
            foreach (var raw in (scoText ?? string.Empty).Split('\n'))
            {
                var text = raw.Trim();
                if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;
                foreach (var part in text.Substring(Prefix.Length).Split(';'))
                {
                    var words = part.Trim().Split(' ');
                    if (words.Length < 2)
                        continue;
                    var ends = words[0].Split('-');
                    var style = OmsiMarkingStyleInfo.ByKey(words[1]);
                    if (ends.Length != 2 || style == null)
                        continue;
                    double? curvature = null;
                    if (words.Length > 2 && double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        curvature = value;
                    lines.Add(new OmsiMarkingLine { From = ends[0], To = ends[1], Style = style.Style, Curvature = curvature });
                }
                break;
            }
            return lines;
        }

        /// <summary>
        /// A small plain picture for the strips, a 24-bit BMP 8 pixels square, as OMSI reads them; the editor writes it
        /// next to the junction, so no texture of the game is copied.
        /// </summary>
        public static byte[] PlainBitmap(byte red, byte green, byte blue)
        {
            const int size = 8;
            const int rowBytes = size * 3;
            const int pixelBytes = rowBytes * size;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'B');
                writer.Write((byte)'M');
                writer.Write(54 + pixelBytes);
                writer.Write(0);
                writer.Write(54);
                writer.Write(40);
                writer.Write(size);
                writer.Write(size);
                writer.Write((short)1);
                writer.Write((short)24);
                writer.Write(0);
                writer.Write(pixelBytes);
                writer.Write(2835);
                writer.Write(2835);
                writer.Write(0);
                writer.Write(0);
                for (var pixel = 0; pixel < size * size; pixel++)
                {
                    writer.Write(blue);
                    writer.Write(green);
                    writer.Write(red);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
