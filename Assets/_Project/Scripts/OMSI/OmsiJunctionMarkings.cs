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
    /// or at its mouth where the kerbs turn off (row 1), on a carriageway edge, a lane boundary or a median edge; or where
    /// two lines between arms cross. In the object frame: x right, y forward, heights up.
    /// </summary>
    public sealed class OmsiMarkingAnchor
    {
        private string crossingKey;

        /// <summary>The arm, or -1 for a crossing of two lines.</summary>
        public int Arm { get; internal set; }

        /// <summary>0 at the arm's end, 1 at its mouth.</summary>
        public int Row { get; internal set; }

        /// <summary>Its place across the arm, from the left as seen leaving the junction.</summary>
        public int Index { get; internal set; }

        /// <summary>Metres right of the arm's centre line, as seen leaving the junction.</summary>
        public double Lateral { get; internal set; }

        public double X { get; internal set; }
        public double Y { get; internal set; }
        public double Height { get; internal set; }

        /// <summary>The direction into the junction along the arm; NaN for a crossing.</summary>
        public double InwardDegrees { get; internal set; }

        /// <summary>"edge", "lane", "median" or "crossing".</summary>
        public string Kind { get; internal set; }

        public bool IsCrossing => Kind == "crossing";

        /// <summary>
        /// How the .sco names it: arm, row and index, such as <c>2.0.1</c>; a crossing by the ends of both lines and which
        /// of their crossings it is, such as <c>x0.0.1~2.0.1~1.1.0~1.1.1~0</c>.
        /// </summary>
        public string Key
        {
            get => crossingKey ?? Arm.ToString(CultureInfo.InvariantCulture) + "." + Row.ToString(CultureInfo.InvariantCulture) + "." +
                   Index.ToString(CultureInfo.InvariantCulture);
            internal set => crossingKey = value;
        }
    }

    /// <summary>
    /// How one piece of a line between crossings differs from the line: hidden or in another style. It is kept by where the
    /// piece lay, as a share of the line's length, so it stays with its piece when other lines come or go.
    /// </summary>
    public sealed class OmsiMarkingPieceSetting
    {
        /// <summary>The middle of the piece, as a share of the line's length from its start.</summary>
        public double At { get; set; }

        public bool Hidden { get; set; }

        /// <summary>Another style for the piece, or null for the line's.</summary>
        public OmsiMarkingStyle? Style { get; set; }
    }

    /// <summary>A marking line between two anchors, in a style, with the curvature of its curve or null for one like a circle.</summary>
    public sealed class OmsiMarkingLine
    {
        public string From { get; set; }
        public string To { get; set; }
        public OmsiMarkingStyle Style { get; set; }

        /// <summary>
        /// How far the curve's control points lie from its ends along their directions, as a share of the distance between
        /// the ends: 0 is straight, null takes the share that comes closest to a circular arc. Lines on one arm or from a
        /// crossing are straight.
        /// </summary>
        public double? Curvature { get; set; }

        /// <summary>Pieces between crossings that are hidden or in another style.</summary>
        public List<OmsiMarkingPieceSetting> Pieces { get; } = new List<OmsiMarkingPieceSetting>();

        /// <summary>The ends as one name, the same either way round, such as <c>0.0.1~2.0.1</c>.</summary>
        public string Ends => string.CompareOrdinal(From, To) <= 0 ? From + "~" + To : To + "~" + From;

        public OmsiMarkingLine Copy()
        {
            var copy = new OmsiMarkingLine { From = From, To = To, Style = Style, Curvature = Curvature };
            foreach (var piece in Pieces)
                copy.Pieces.Add(new OmsiMarkingPieceSetting { At = piece.At, Hidden = piece.Hidden, Style = piece.Style });
            return copy;
        }

        /// <summary>The setting of the piece from one share of the length to another, or null.</summary>
        public OmsiMarkingPieceSetting SettingBetween(double from, double to)
        {
            foreach (var piece in Pieces)
                if (piece.At >= from && piece.At <= to)
                    return piece;
            return null;
        }
    }

    /// <summary>One piece of a line between the crossings with other lines, as it is drawn.</summary>
    public sealed class OmsiMarkingPiece
    {
        /// <summary>The line's place in the junction's list.</summary>
        public int Line { get; internal set; }

        /// <summary>Where the piece starts and ends, in metres along the line.</summary>
        public double From { get; internal set; }

        public double To { get; internal set; }

        /// <summary>The whole line's length.</summary>
        public double LineLength { get; internal set; }

        public bool Hidden { get; internal set; }
        public OmsiMarkingStyle Style { get; internal set; }

        /// <summary>The middle of the piece as a share of the line's length, as a <see cref="OmsiMarkingPieceSetting"/> keeps it.</summary>
        public double Middle => LineLength > 0.0 ? (From + To) / 2.0 / LineLength : 0.5;
    }

    /// <summary>
    /// Road markings of junctions the editor makes (ADR 044), modelled on Town Road Lane for Cities: Skylines II: lines only
    /// run between anchors on the arms or where lines cross, as curves leaving each anchor along its lane, and are built
    /// into the junction's model as flat strips just above the carriageway. Where lines cross they fall into pieces, each of
    /// which can be hidden or take another style. The .sco keeps them as a line of text, so the junction can be made again
    /// with them, such as with an arm turned.
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

        /// <summary>Lines touching within this distance of an end do not cross there, such as two lines from one anchor.</summary>
        private const double EndClearance = 0.3;

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
                            Arm = arm, Row = row, Index = index, Lateral = across[index].Key,
                            X = Math.Sin(heading) * along + Math.Cos(heading) * across[index].Key,
                            Y = Math.Cos(heading) * along - Math.Sin(heading) * across[index].Key,
                            Height = section.RoadHeight, InwardDegrees = junctionArm.HeadingDegrees + 180.0, Kind = across[index].Value
                        });
                }
            }
            return anchors;
        }

        /// <summary>The anchors of the arms and of the crossings of the lines between them.</summary>
        public static List<OmsiMarkingAnchor> AllAnchors(OmsiJunctionLayout layout, IList<OmsiMarkingLine> lines)
        {
            var anchors = Anchors(layout);
            anchors.AddRange(CrossingAnchors(anchors, lines));
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
                var leftHalf = HalfWidth(left);
                var rightHalf = HalfWidth(right);
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

        private static double HalfWidth(OmsiSplineLane lane)
        {
            return (lane.Width > 0.0 ? lane.Width : 3.0) / 2.0;
        }

        /// <summary>
        /// The crossings of lines between arm anchors, as anchors other lines can start or end at, in a stable order. Lines
        /// that start or end at a crossing make none of their own, so crossings never depend on each other.
        /// </summary>
        public static List<OmsiMarkingAnchor> CrossingAnchors(IReadOnlyList<OmsiMarkingAnchor> armAnchors, IEnumerable<OmsiMarkingLine> lines)
        {
            var paths = new List<KeyValuePair<OmsiMarkingLine, List<double[]>>>();
            foreach (var line in lines)
            {
                if (line.From.StartsWith("x", StringComparison.Ordinal) || line.To.StartsWith("x", StringComparison.Ordinal))
                    continue;
                var path = Path(armAnchors, line);
                if (path != null)
                    paths.Add(new KeyValuePair<OmsiMarkingLine, List<double[]>>(line, path));
            }
            paths.Sort((first, second) => string.CompareOrdinal(first.Key.Ends, second.Key.Ends));
            var crossings = new List<OmsiMarkingAnchor>();
            for (var first = 0; first < paths.Count; first++)
                for (var second = first + 1; second < paths.Count; second++)
                {
                    var found = Crossings(paths[first].Value, paths[second].Value);
                    for (var index = 0; index < found.Count; index++)
                        crossings.Add(new OmsiMarkingAnchor
                        {
                            Arm = -1, Row = -1, Index = index, Kind = "crossing", InwardDegrees = double.NaN, X = found[index][2], Y = found[index][3],
                            Height = found[index][4],
                            Key = "x" + paths[first].Key.Ends + "~" + paths[second].Key.Ends + "~" + index.ToString(CultureInfo.InvariantCulture)
                        });
                }
            return crossings;
        }

        /// <summary>
        /// Where two paths cross, away from their ends, in order along the first: distance along each, x, y and height.
        /// </summary>
        private static List<double[]> Crossings(List<double[]> first, List<double[]> second)
        {
            var found = new List<double[]>();
            var firstDistances = Distances(first);
            var secondDistances = Distances(second);
            var firstLength = firstDistances[first.Count - 1];
            var secondLength = secondDistances[second.Count - 1];
            for (var a = 1; a < first.Count; a++)
                for (var b = 1; b < second.Count; b++)
                {
                    if (!SegmentsCross(first[a - 1], first[a], second[b - 1], second[b], out var s, out var t))
                        continue;
                    var alongFirst = firstDistances[a - 1] + (firstDistances[a] - firstDistances[a - 1]) * s;
                    var alongSecond = secondDistances[b - 1] + (secondDistances[b] - secondDistances[b - 1]) * t;
                    if (alongFirst < EndClearance || alongFirst > firstLength - EndClearance || alongSecond < EndClearance || alongSecond > secondLength - EndClearance)
                        continue;
                    // A crossing on a joint of points shows on both segments meeting there.
                    if (found.Count > 0 && Math.Abs(found[found.Count - 1][0] - alongFirst) < 0.05)
                        continue;
                    found.Add(new[]
                    {
                        alongFirst, alongSecond, first[a - 1][0] + (first[a][0] - first[a - 1][0]) * s, first[a - 1][1] + (first[a][1] - first[a - 1][1]) * s,
                        first[a - 1][2] + (first[a][2] - first[a - 1][2]) * s
                    });
                }
            found.Sort((x, y) => x[0].CompareTo(y[0]));
            return found;
        }

        private static bool SegmentsCross(double[] a, double[] b, double[] c, double[] d, out double s, out double t)
        {
            s = t = 0.0;
            var rx = b[0] - a[0];
            var ry = b[1] - a[1];
            var qx = d[0] - c[0];
            var qy = d[1] - c[1];
            var cross = rx * qy - ry * qx;
            if (Math.Abs(cross) < 1e-12)
                return false;
            s = ((c[0] - a[0]) * qy - (c[1] - a[1]) * qx) / cross;
            t = ((c[0] - a[0]) * ry - (c[1] - a[1]) * rx) / cross;
            return s >= 0.0 && s <= 1.0 && t >= 0.0 && t <= 1.0;
        }

        private static double[] Distances(List<double[]> path)
        {
            var distances = new double[path.Count];
            for (var index = 1; index < path.Count; index++)
                distances[index] = distances[index - 1] + Math.Sqrt(Square(path[index][0] - path[index - 1][0]) + Square(path[index][1] - path[index - 1][1]));
            return distances;
        }

        /// <summary>
        /// The centre line of a marking as points (x, y, height) in the object frame, or null when an anchor is missing.
        /// Between arms it is a cubic curve leaving each anchor into the junction along its arm; on one arm or from a crossing
        /// it is straight.
        /// </summary>
        public static List<double[]> Path(IReadOnlyList<OmsiMarkingAnchor> anchors, OmsiMarkingLine line)
        {
            var from = Find(anchors, line.From);
            var to = Find(anchors, line.To);
            if (from == null || to == null || ReferenceEquals(from, to))
                return null;
            var chord = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            var points = new List<double[]>();
            if (from.Arm == to.Arm || from.IsCrossing || to.IsCrossing || chord < 1e-6)
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
        /// The pieces of every line between its crossings with the other lines, in the order of the lines and along each,
        /// with what its settings make of them. Lines whose anchors are gone have none.
        /// </summary>
        /// <param name="anchors">The anchors of the arms and of the crossings, as <see cref="AllAnchors"/> gives them.</param>
        public static List<OmsiMarkingPiece> Pieces(IReadOnlyList<OmsiMarkingAnchor> anchors, IList<OmsiMarkingLine> lines)
        {
            var paths = new List<List<double[]>>();
            foreach (var line in lines)
                paths.Add(Path(anchors, line));
            var pieces = new List<OmsiMarkingPiece>();
            for (var index = 0; index < lines.Count; index++)
            {
                var path = paths[index];
                if (path == null)
                    continue;
                var length = Distances(path)[path.Count - 1];
                var cuts = new List<double> { 0.0, length };
                for (var other = 0; other < lines.Count; other++)
                    if (other != index && paths[other] != null)
                        foreach (var crossing in Crossings(path, paths[other]))
                            cuts.Add(crossing[0]);
                cuts.Sort();
                for (var cut = 1; cut < cuts.Count; cut++)
                {
                    if (cuts[cut] - cuts[cut - 1] < 0.05)
                        continue;
                    var setting = length > 0.0 ? lines[index].SettingBetween(cuts[cut - 1] / length, cuts[cut] / length) : null;
                    pieces.Add(new OmsiMarkingPiece
                    {
                        Line = index, From = cuts[cut - 1], To = cuts[cut], LineLength = length, Hidden = setting != null && setting.Hidden,
                        Style = setting?.Style ?? lines[index].Style
                    });
                }
            }
            return pieces;
        }

        /// <summary>The points of a piece of a path, from one distance along it to another.</summary>
        public static List<double[]> Cut(List<double[]> path, double from, double to)
        {
            var distances = Distances(path);
            var points = new List<double[]> { At(path, distances, from) };
            for (var index = 0; index < path.Count; index++)
                if (distances[index] > from + 1e-6 && distances[index] < to - 1e-6)
                    points.Add(path[index]);
            points.Add(At(path, distances, to));
            return points;
        }

        /// <summary>
        /// Adds the markings to a model: each visible piece of each line as flat strips just above the carriageway, its
        /// strokes cut along its length, two strips for a double line, in white or yellow. A piece in its line's style keeps
        /// the line's run of strokes; one in another style starts its own. Lines whose anchors the junction no longer has
        /// are left out.
        /// </summary>
        /// <param name="anchors">The anchors of the arms; those of the crossings are added here.</param>
        public static void AddMeshes(IReadOnlyList<OmsiMarkingAnchor> anchors, IEnumerable<OmsiMarkingLine> lines, List<OmsiGeneratedMesh> meshes)
        {
            var list = new List<OmsiMarkingLine>(lines);
            var all = new List<OmsiMarkingAnchor>(anchors);
            if (!all.Exists(anchor => anchor.IsCrossing))
                all.AddRange(CrossingAnchors(anchors, list));
            OmsiGeneratedMesh white = null, yellow = null;
            foreach (var piece in Pieces(all, list))
            {
                if (piece.Hidden)
                    continue;
                var path = Path(all, list[piece.Line]);
                var info = OmsiMarkingStyleInfo.Of(piece.Style);
                OmsiGeneratedMesh mesh;
                if (info.Yellow)
                    mesh = yellow = yellow ?? new OmsiGeneratedMesh { Texture = YellowTexture };
                else
                    mesh = white = white ?? new OmsiGeneratedMesh { Texture = WhiteTexture };
                var distances = Distances(path);
                var offsets = info.TwoLines ? new[] { -info.Width, info.Width } : new[] { 0.0 };
                // The line's own strokes run on from its start across its pieces; another style starts at the piece.
                var origin = piece.Style == list[piece.Line].Style ? 0.0 : piece.From;
                foreach (var offset in offsets)
                {
                    if (info.Stroke <= 0.0)
                    {
                        Strip(mesh, path, distances, piece.From, piece.To, offset, info.Width);
                        continue;
                    }
                    for (var start = origin; start < piece.To - 0.01; start += info.Stroke + info.Gap)
                    {
                        var from = Math.Max(start, piece.From);
                        var to = Math.Min(start + info.Stroke, piece.To);
                        if (to > from)
                            Strip(mesh, path, distances, from, to, offset, info.Width);
                    }
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

        /// <summary>
        /// A proposal after the right of way (ADR 044, stage 2): along the main road the lane lines run through and a broken
        /// edge line crosses the mouth of each side road, and side roads get a give-way line over the lanes coming in. A
        /// junction of two arms carries the lane lines through; one without a main road gets stop lines on every arm.
        /// </summary>
        public static List<OmsiMarkingLine> Suggest(OmsiJunctionLayout layout)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            var anchors = Anchors(layout);
            var lines = new List<OmsiMarkingLine>();
            var mains = new List<int>();
            for (var arm = 0; arm < layout.Arms.Count; arm++)
                if (layout.Arms[arm].Main)
                    mains.Add(arm);
            if (layout.Arms.Count == 2)
                mains = new List<int> { 0, 1 };
            if (mains.Count == 2)
            {
                var a = mains[0];
                var b = mains[1];
                var ends = anchors.FindAll(anchor => anchor.Row == 0);
                // Each boundary between lanes runs on to the one at the same place across the other arm, seen from its side.
                foreach (var from in ends.FindAll(anchor => anchor.Arm == a && anchor.Kind == "lane"))
                {
                    var to = Nearest(ends.FindAll(anchor => anchor.Arm == b && anchor.Kind == "lane"), -from.Lateral, 0.5);
                    if (to != null)
                        lines.Add(new OmsiMarkingLine { From = from.Key, To = to.Key, Style = OmsiMarkingStyle.LaneLine });
                }
                // A side road on either side of the main road gets the broken edge line across its mouth.
                var aEdges = ends.FindAll(anchor => anchor.Arm == a && anchor.Kind == "edge");
                var bEdges = ends.FindAll(anchor => anchor.Arm == b && anchor.Kind == "edge");
                if (aEdges.Count == 2 && bEdges.Count == 2)
                {
                    var heading = layout.Arms[a].HeadingDegrees;
                    var left = false;
                    var right = false;
                    for (var arm = 0; arm < layout.Arms.Count; arm++)
                    {
                        if (mains.Contains(arm))
                            continue;
                        var turn = OmsiRoadPlanner.Normalize(layout.Arms[arm].HeadingDegrees - heading);
                        left |= turn < 0.0;
                        right |= turn > 0.0;
                    }
                    // The arms face opposite ways: the left edge of arm a lies on the side of the right edge of arm b.
                    if (left)
                        lines.Add(new OmsiMarkingLine { From = aEdges[0].Key, To = bEdges[1].Key, Style = OmsiMarkingStyle.BrokenEdgeLine });
                    if (right)
                        lines.Add(new OmsiMarkingLine { From = aEdges[1].Key, To = bEdges[0].Key, Style = OmsiMarkingStyle.BrokenEdgeLine });
                }
            }
            for (var arm = 0; arm < layout.Arms.Count; arm++)
            {
                if (mains.Contains(arm))
                    continue;
                var style = mains.Count == 2 ? OmsiMarkingStyle.GiveWayLine : OmsiMarkingStyle.StopLine;
                var across = Inbound(layout.Arms[arm].Profile, anchors.FindAll(anchor => anchor.Arm == arm && anchor.Row == 1));
                if (across != null)
                    lines.Add(new OmsiMarkingLine { From = across[0].Key, To = across[1].Key, Style = style, Curvature = 0.0 });
            }
            return lines;
        }

        /// <summary>
        /// The anchors bounding the lanes that come into the junction on an arm, left and right, or null without such lanes.
        /// In the arm's frame lanes against the profile's direction run in (the layout turns profiles of incoming roads over).
        /// </summary>
        private static OmsiMarkingAnchor[] Inbound(OmsiSplineProfile profile, List<OmsiMarkingAnchor> row)
        {
            if (profile == null || row.Count < 2)
                return null;
            double left = double.MaxValue, right = double.MinValue;
            foreach (var lane in profile.Lanes)
                if (lane.Kind == 0 && lane.Direction != 0)
                {
                    left = Math.Min(left, lane.X - HalfWidth(lane));
                    right = Math.Max(right, lane.X + HalfWidth(lane));
                }
            if (left > right)
                return null;
            // The anchors enclosing those lanes: the last at or left of their left side, the first at or right of their right side.
            OmsiMarkingAnchor from = null, to = null;
            foreach (var anchor in row)
            {
                if (anchor.Lateral <= left + 0.25 && (from == null || anchor.Lateral > from.Lateral))
                    from = anchor;
                if (anchor.Lateral >= right - 0.25 && (to == null || anchor.Lateral < to.Lateral))
                    to = anchor;
            }
            return from != null && to != null && !ReferenceEquals(from, to) ? new[] { from, to } : null;
        }

        private static OmsiMarkingAnchor Nearest(List<OmsiMarkingAnchor> anchors, double lateral, double reach)
        {
            OmsiMarkingAnchor best = null;
            var bestDistance = reach;
            foreach (var anchor in anchors)
            {
                var distance = Math.Abs(anchor.Lateral - lateral);
                if (distance <= bestDistance)
                {
                    best = anchor;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// The line of a .sco that keeps the markings, such as
        /// <c>MapWorks markings: 0.0.1-2.0.1 laneline auto 0.42:hide; 1.1.0-1.1.1 stop 0</c>; a piece setting is its middle as a
        /// share of the line's length, then <c>hide</c> and or another style.
        /// </summary>
        public static string Write(IEnumerable<OmsiMarkingLine> lines)
        {
            var text = new StringBuilder(Prefix);
            var first = true;
            foreach (var line in lines)
            {
                text.Append(first ? " " : "; ").Append(line.From).Append('-').Append(line.To).Append(' ')
                    .Append(OmsiMarkingStyleInfo.Of(line.Style).Key).Append(' ')
                    .Append(line.Curvature.HasValue ? line.Curvature.Value.ToString("0.###", CultureInfo.InvariantCulture) : "auto");
                foreach (var piece in line.Pieces)
                {
                    text.Append(' ').Append(piece.At.ToString("0.####", CultureInfo.InvariantCulture));
                    if (piece.Hidden)
                        text.Append(":hide");
                    if (piece.Style.HasValue)
                        text.Append(':').Append(OmsiMarkingStyleInfo.Of(piece.Style.Value).Key);
                }
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
                    var line = new OmsiMarkingLine { From = ends[0], To = ends[1], Style = style.Style };
                    if (words.Length > 2 && double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        line.Curvature = value;
                    for (var index = 3; index < words.Length; index++)
                    {
                        var fields = words[index].Split(':');
                        if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                            continue;
                        var setting = new OmsiMarkingPieceSetting { At = at };
                        for (var field = 1; field < fields.Length; field++)
                            if (fields[field] == "hide")
                                setting.Hidden = true;
                            else if (OmsiMarkingStyleInfo.ByKey(fields[field]) is OmsiMarkingStyleInfo pieceStyle)
                                setting.Style = pieceStyle.Style;
                        line.Pieces.Add(setting);
                    }
                    lines.Add(line);
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
