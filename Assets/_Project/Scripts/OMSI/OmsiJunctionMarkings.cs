using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// Fußgängerüberweg (sign 293): bars 0.5 m wide and 0.5 m apart, 4 m long, along the traffic. Across one arm the
        /// bars lie beside the line, in the arm away from the junction.
        /// </summary>
        Crosswalk,

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

        /// <summary>True for a band beside its line rather than on it, as a pedestrian crossing lies in its arm.</summary>
        public bool Beside => Style == OmsiMarkingStyle.Crosswalk;

        public static readonly IReadOnlyList<OmsiMarkingStyleInfo> All = new[]
        {
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.LaneLine, "laneline", "Lane line", "Leitlinie (sign 340): 0.12 m, 3 m strokes, 6 m gaps", 0.12, 3.0, 6.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.LaneBoundary, "boundary", "Lane boundary", "Fahrstreifenbegrenzung (sign 295): 0.12 m, unbroken", 0.12, 0.0, 0.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.DoubleBoundary, "double", "Double boundary", "Doppelte Fahrstreifenbegrenzung: two lines of 0.12 m, 0.12 m apart", 0.12, 0.0, 0.0, true, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.EdgeLine, "edge", "Edge line", "Fahrbahnbegrenzung (Breitstrich): 0.25 m, unbroken", 0.25, 0.0, 0.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.BrokenEdgeLine, "brokenedge", "Broken edge line", "Unterbrochener Breitstrich, as across a side road: 0.25 m, 3 m strokes, 1.5 m gaps", 0.25, 3.0, 1.5, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.StopLine, "stop", "Stop line", "Haltlinie (sign 294): 0.5 m, unbroken", 0.5, 0.0, 0.0, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.GiveWayLine, "giveway", "Give-way line", "Wartelinie (sign 341): 0.5 m, 0.5 m strokes, 0.25 m gaps", 0.5, 0.5, 0.25, false, false),
            new OmsiMarkingStyleInfo(OmsiMarkingStyle.Crosswalk, "zebra", "Zebra crossing",
                "Fußgängerüberweg (sign 293): bars 0.5 m wide and 0.5 m apart, 4 m long; drawn across an arm, it lies in the arm behind the line", 4.0, 0.5, 0.5,
                false, false),
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

    /// <summary>How a marked area is filled (ADR 044, stage 3).</summary>
    public enum OmsiMarkingFill
    {
        /// <summary>Sperrfläche (sign 298): stripes 0.5 m wide with 1 m between them, 45° to the traffic.</summary>
        Hatched,

        /// <summary>A yellow Sperrfläche, as for road works.</summary>
        YellowHatched
    }

    /// <summary>How a fill is drawn and named.</summary>
    public sealed class OmsiMarkingFillInfo
    {
        private OmsiMarkingFillInfo(OmsiMarkingFill fill, string key, string name, string description, double stroke, double gap, bool yellow)
        {
            Fill = fill;
            Key = key;
            Name = name;
            Description = description;
            Stroke = stroke;
            Gap = gap;
            Yellow = yellow;
        }

        public OmsiMarkingFill Fill { get; }

        /// <summary>The word the .sco stores the fill by.</summary>
        public string Key { get; }

        public string Name { get; }
        public string Description { get; }

        /// <summary>Width of each stripe and of the space between two, in metres, across the stripes.</summary>
        public double Stroke { get; }

        public double Gap { get; }
        public bool Yellow { get; }

        public static readonly IReadOnlyList<OmsiMarkingFillInfo> All = new[]
        {
            new OmsiMarkingFillInfo(OmsiMarkingFill.Hatched, "hatched", "Restricted area", "Sperrfläche (sign 298): stripes 0.5 m wide, 1 m apart, 45° to the traffic",
                0.5, 1.0, false),
            new OmsiMarkingFillInfo(OmsiMarkingFill.YellowHatched, "yellowhatched", "Yellow restricted area", "A yellow Sperrfläche, as for road works", 0.5, 1.0, true)
        };

        public static OmsiMarkingFillInfo Of(OmsiMarkingFill fill)
        {
            foreach (var info in All)
                if (info.Fill == fill)
                    return info;
            return All[0];
        }

        public static OmsiMarkingFillInfo ByKey(string key)
        {
            foreach (var info in All)
                if (string.Equals(info.Key, key, StringComparison.OrdinalIgnoreCase))
                    return info;
            return null;
        }
    }

    /// <summary>
    /// A marked area, such as a Sperrfläche, by the anchors round it in order. Between two corners on one line its side
    /// follows the line, else it runs straight.
    /// </summary>
    public sealed class OmsiMarkingArea
    {
        public const double DefaultAngle = 45.0;

        public List<string> Corners { get; } = new List<string>();
        public OmsiMarkingFill Fill { get; set; }

        /// <summary>The angle of the stripes in degrees, counter-clockwise from the longest side between two corners.</summary>
        public double Angle { get; set; } = DefaultAngle;

        public OmsiMarkingArea Copy()
        {
            var copy = new OmsiMarkingArea { Fill = Fill, Angle = Angle };
            copy.Corners.AddRange(Corners);
            return copy;
        }
    }

    /// <summary>Which ways a direction arrow (sign 297) points (ADR 044, stage 4).</summary>
    public enum OmsiMarkingArrowKind
    {
        Straight,
        Left,
        Right,
        StraightLeft,
        StraightRight,
        LeftRight,
        All
    }

    /// <summary>How an arrow kind is named.</summary>
    public sealed class OmsiMarkingArrowInfo
    {
        private OmsiMarkingArrowInfo(OmsiMarkingArrowKind kind, string key, string name, bool straight, bool left, bool right)
        {
            Kind = kind;
            Key = key;
            Name = name;
            Straight = straight;
            Left = left;
            Right = right;
        }

        public OmsiMarkingArrowKind Kind { get; }

        /// <summary>The word the .sco stores the kind by.</summary>
        public string Key { get; }

        public string Name { get; }
        public bool Straight { get; }
        public bool Left { get; }
        public bool Right { get; }

        public static readonly IReadOnlyList<OmsiMarkingArrowInfo> All = new[]
        {
            new OmsiMarkingArrowInfo(OmsiMarkingArrowKind.Straight, "straight", "Straight on", true, false, false),
            new OmsiMarkingArrowInfo(OmsiMarkingArrowKind.Left, "left", "Left", false, true, false),
            new OmsiMarkingArrowInfo(OmsiMarkingArrowKind.Right, "right", "Right", false, false, true),
            new OmsiMarkingArrowInfo(OmsiMarkingArrowKind.StraightLeft, "straightleft", "Straight on or left", true, true, false),
            new OmsiMarkingArrowInfo(OmsiMarkingArrowKind.StraightRight, "straightright", "Straight on or right", true, false, true),
            new OmsiMarkingArrowInfo(OmsiMarkingArrowKind.LeftRight, "leftright", "Left or right", false, true, true),
            new OmsiMarkingArrowInfo(OmsiMarkingArrowKind.All, "all", "Straight on, left or right", true, true, true)
        };

        public static OmsiMarkingArrowInfo Of(OmsiMarkingArrowKind kind)
        {
            foreach (var info in All)
                if (info.Kind == kind)
                    return info;
            return All[0];
        }

        public static OmsiMarkingArrowInfo ByKey(string key)
        {
            foreach (var info in All)
                if (string.Equals(info.Key, key, StringComparison.OrdinalIgnoreCase))
                    return info;
            return null;
        }
    }

    /// <summary>
    /// A direction arrow in the lane between two neighbouring anchors of an arm, a distance behind them; it points into the
    /// junction on a lane that runs in, out of it on one that runs out.
    /// </summary>
    public sealed class OmsiMarkingArrow
    {
        public const double DefaultDistance = 1.0;

        /// <summary>The anchors on either side of the lane, such as <c>1.1.0</c> and <c>1.1.1</c>.</summary>
        public string From { get; set; }

        public string To { get; set; }
        public OmsiMarkingArrowKind Kind { get; set; }

        /// <summary>Metres from the anchors out along the arm to the end of the arrow nearer the junction.</summary>
        public double Distance { get; set; } = DefaultDistance;

        /// <summary>True on a lane running out of the junction, so the arrow points away from it.</summary>
        public bool Outward { get; set; }

        public OmsiMarkingArrow Copy()
        {
            return new OmsiMarkingArrow { From = From, To = To, Kind = Kind, Distance = Distance, Outward = Outward };
        }
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

        /// <summary>Areas lie a little lower than lines, so the lines round them never flicker with their stripes.</summary>
        public const double AreaLift = 0.015;

        /// <summary>The largest curvature a line takes; beyond it short curves loop back on themselves.</summary>
        public const double MaximumCurvature = 0.8;

        public const string WhiteTexture = "MapWorks_marking_white.bmp";
        public const string YellowTexture = "MapWorks_marking_yellow.bmp";

        /// <summary>Points along curves lie this far apart at most.</summary>
        private const double SampleMeters = 0.25;

        /// <summary>Lines touching within this distance of an end do not cross there, such as two lines from one anchor.</summary>
        private const double EndClearance = 0.3;

        /// <summary>Stripes of areas are laid in bands this wide at most, so their ends follow the outline.</summary>
        private const double HatchStep = 0.1;

        internal const string AreasPrefix = "MapWorks marking areas:";
        internal const string ArrowsPrefix = "MapWorks marking arrows:";

        /// <summary>Direction arrows are 5 m long, as in towns.</summary>
        public const double ArrowLength = 5.0;

        // German direction arrows in arrow lengths, measured from the drawings of sign 297: the shaft narrows from the tail
        // to the head; a turn alone bends the shaft into a hook with a tall head, a turn beside straight on branches off lower.
        private const double ArrowTail = 0.037;
        private const double ArrowNeck = 0.020;
        private const double ArrowHeadBase = 0.631;
        private const double ArrowHeadHalf = 0.077;
        private const double HookBase = 0.083;
        private const double HookInner = 0.637;
        private const double HookOuter = 0.712;
        private const double HookHeadHalf = 0.211;
        private const double HookHeadLength = 0.117;
        private const double BranchBase = 0.124;
        private const double BranchMiddle = 0.410;
        private const double BranchHeadHalf = 0.134;
        private const double BranchHeadLength = 0.087;
        private const double BranchLowCentre = 0.277;
        private const double BranchLowRadius = 0.106;
        private const double BranchHighCentre = 0.305;
        private const double BranchHighRadius = 0.135;

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
        /// are left out. Areas lie just below the lines, so lines drawn round them stay on top.
        /// </summary>
        /// <param name="anchors">The anchors of the arms; those of the crossings are added here.</param>
        /// <param name="areas">Marked areas to fill; null for none.</param>
        /// <param name="arrows">Direction arrows; null for none.</param>
        public static void AddMeshes(IReadOnlyList<OmsiMarkingAnchor> anchors, IEnumerable<OmsiMarkingLine> lines, List<OmsiGeneratedMesh> meshes,
            IEnumerable<OmsiMarkingArea> areas = null, IEnumerable<OmsiMarkingArrow> arrows = null)
        {
            var list = new List<OmsiMarkingLine>(lines);
            var all = new List<OmsiMarkingAnchor>(anchors);
            if (!all.Exists(anchor => anchor.IsCrossing))
                all.AddRange(CrossingAnchors(anchors, list));
            OmsiGeneratedMesh white = null, yellow = null;
            OmsiGeneratedMesh MeshFor(bool isYellow) => isYellow
                ? yellow = yellow ?? new OmsiGeneratedMesh { Texture = YellowTexture }
                : white = white ?? new OmsiGeneratedMesh { Texture = WhiteTexture };
            foreach (var piece in Pieces(all, list))
            {
                if (piece.Hidden)
                    continue;
                var path = Path(all, list[piece.Line]);
                var info = OmsiMarkingStyleInfo.Of(piece.Style);
                var mesh = MeshFor(info.Yellow);
                var distances = Distances(path);
                var offsets = info.TwoLines ? new[] { -info.Width, info.Width } : new[] { info.Beside ? BesideOffset(all, list[piece.Line], path, info.Width) : 0.0 };
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
            if (areas != null)
                foreach (var area in areas)
                {
                    var outline = Outline(all, list, area);
                    if (outline == null)
                        continue;
                    var info = OmsiMarkingFillInfo.Of(area.Fill);
                    Hatch(MeshFor(info.Yellow), outline, StripeDegrees(all, area), info.Stroke, info.Gap);
                }
            if (arrows != null)
                foreach (var arrow in arrows)
                {
                    if (!ArrowFrame(all, arrow, out var tail, out var direction, out var height))
                        continue;
                    var mesh = MeshFor(false);
                    // The parts overlap where they join; the paint is opaque, so that does not show.
                    foreach (var part in ArrowShape(arrow.Kind))
                    {
                        var ring = part.ConvertAll(point => ArrowPoint(tail, direction, height, point));
                        var first = mesh.Positions.Count / 3;
                        foreach (var point in ring)
                        {
                            mesh.Positions.Add(point[0]);
                            mesh.Positions.Add(height + Lift);
                            mesh.Positions.Add(point[1]);
                            mesh.Uvs.Add(0.0);
                            mesh.Uvs.Add(0.0);
                        }
                        foreach (var triangle in OmsiJunctionLayout.Triangulate(ring))
                            OmsiJunctionLayout.AddUpward(mesh, first + triangle[0], first + triangle[1], first + triangle[2]);
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

        /// <summary>
        /// The shape of an arrow in its own frame, metres across to the right and forward from its tail, as polygons that
        /// overlap where they join: the shaft with its head straight on and a branch for each turn beside it, or for turns
        /// alone the shaft bent into a hook to each side.
        /// </summary>
        public static List<List<double[]>> ArrowShape(OmsiMarkingArrowKind kind)
        {
            var info = OmsiMarkingArrowInfo.Of(kind);
            var parts = new List<List<double[]>>();
            if (info.Straight)
                parts.Add(new List<double[]>
                {
                    new[] { -ArrowTail, 0.0 }, new[] { ArrowTail, 0.0 }, new[] { ArrowNeck, ArrowHeadBase }, new[] { ArrowHeadHalf, ArrowHeadBase },
                    new[] { 0.0, 1.0 }, new[] { -ArrowHeadHalf, ArrowHeadBase }, new[] { -ArrowNeck, ArrowHeadBase }
                });
            foreach (var side in new[] { info.Left ? -1.0 : 0.0, info.Right ? 1.0 : 0.0 })
            {
                if (side == 0.0)
                    continue;
                var part = info.Straight ? ArrowBranch() : ArrowHook();
                if (side < 0.0)
                {
                    part = part.ConvertAll(point => new[] { -point[0], point[1] });
                    part.Reverse();
                }
                parts.Add(part);
            }
            foreach (var part in parts)
                foreach (var point in part)
                {
                    point[0] *= ArrowLength;
                    point[1] *= ArrowLength;
                }
            return parts;
        }

        /// <summary>Half the width of an arrow's shaft a distance from its tail, in arrow lengths.</summary>
        private static double ArrowHalfShaft(double along)
        {
            return ArrowTail + (ArrowNeck - ArrowTail) * along / ArrowHeadBase;
        }

        /// <summary>A turn to the right alone: the shaft bends through two quarter circles into a head as tall as a fifth of the arrow.</summary>
        private static List<double[]> ArrowHook()
        {
            var inner = HookBase - ArrowHalfShaft(HookInner);
            var outer = HookBase + ArrowHalfShaft(HookOuter);
            var middle = (HookInner + inner + HookOuter + outer) / 2.0;
            var points = new List<double[]> { new[] { -ArrowTail, 0.0 }, new[] { ArrowTail, 0.0 } };
            AddArc(points, HookBase, HookInner, inner, Math.PI, Math.PI / 2.0);
            points.Add(new[] { HookBase, middle - HookHeadHalf });
            points.Add(new[] { HookBase + HookHeadLength, middle });
            points.Add(new[] { HookBase, middle + HookHeadHalf });
            AddArc(points, HookBase, HookOuter, outer, Math.PI / 2.0, Math.PI);
            return points;
        }

        /// <summary>A turn to the right beside straight on: a curved branch from inside the shaft out to its head.</summary>
        private static List<double[]> ArrowBranch()
        {
            var points = new List<double[]>();
            AddArc(points, BranchBase, BranchLowCentre, BranchLowRadius, Math.PI / 2.0 + BranchRoot(BranchLowCentre, BranchLowRadius), Math.PI / 2.0);
            points.Add(new[] { BranchBase, BranchMiddle - BranchHeadHalf });
            points.Add(new[] { BranchBase + BranchHeadLength, BranchMiddle });
            points.Add(new[] { BranchBase, BranchMiddle + BranchHeadHalf });
            AddArc(points, BranchBase, BranchHighCentre, BranchHighRadius, Math.PI / 2.0, Math.PI / 2.0 + BranchRoot(BranchHighCentre, BranchHighRadius));
            return points;
        }

        /// <summary>How far past the top of its circle an edge of a branch starts: inside the shaft, halfway to its middle.</summary>
        private static double BranchRoot(double centre, double radius)
        {
            var low = 0.0;
            var high = Math.PI / 2.0;
            for (var step = 0; step < 40; step++)
            {
                var middle = (low + high) / 2.0;
                if (BranchBase - radius * Math.Sin(middle) > ArrowHalfShaft(centre + radius * Math.Cos(middle)) / 2.0)
                    low = middle;
                else
                    high = middle;
            }
            return low;
        }

        /// <summary>Adds the points of a circular arc from one angle to another, counter-clockwise from east.</summary>
        private static void AddArc(List<double[]> points, double x, double y, double radius, double from, double to)
        {
            const int steps = 10;
            for (var step = 0; step <= steps; step++)
            {
                var angle = from + (to - from) * step / steps;
                points.Add(new[] { x + radius * Math.Cos(angle), y + radius * Math.Sin(angle) });
            }
        }

        /// <summary>
        /// The border of polygons that overlap, as lines: each edge cut where the others cross or touch it, keeping the pieces
        /// with none of the others just outside them. An edge two polygons share comes once from each.
        /// </summary>
        private static List<List<double[]>> Border(List<List<double[]>> polygons)
        {
            var rings = polygons.ConvertAll(polygon => SignedArea(polygon) < 0.0 ? Enumerable.Reverse(polygon).ToList() : polygon);
            var border = new List<List<double[]>>();
            for (var index = 0; index < rings.Count; index++)
            {
                var ring = rings[index];
                List<double[]> line = null;
                for (var corner = 0; corner < ring.Count; corner++)
                {
                    var a = ring[corner];
                    var b = ring[(corner + 1) % ring.Count];
                    var length = Math.Sqrt(Square(b[0] - a[0]) + Square(b[1] - a[1]));
                    if (length < 1e-9)
                        continue;
                    var cuts = new List<double> { 0.0, 1.0 };
                    for (var other = 0; other < rings.Count; other++)
                    {
                        if (other == index)
                            continue;
                        var otherRing = rings[other];
                        for (var next = 0; next < otherRing.Count; next++)
                        {
                            var c = otherRing[next];
                            if (SegmentsCross(a, b, c, otherRing[(next + 1) % otherRing.Count], out var s, out _))
                                cuts.Add(s);
                            // A corner of the other lying on this edge, where their edges run together.
                            var t = ((c[0] - a[0]) * (b[0] - a[0]) + (c[1] - a[1]) * (b[1] - a[1])) / (length * length);
                            var off = ((c[0] - a[0]) * (b[1] - a[1]) - (c[1] - a[1]) * (b[0] - a[0])) / length;
                            if (t > 0.0 && t < 1.0 && Math.Abs(off) < 1e-6)
                                cuts.Add(t);
                        }
                    }
                    cuts.Sort();
                    for (var cut = 0; cut + 1 < cuts.Count; cut++)
                    {
                        if (cuts[cut + 1] - cuts[cut] < 1e-9)
                            continue;
                        double[] Point(double t) => new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t };
                        var middle = Point((cuts[cut] + cuts[cut + 1]) / 2.0);
                        // Just outside: to the right of an edge of a counter-clockwise ring.
                        var probe = new[] { middle[0] + (b[1] - a[1]) / length * 1e-4, middle[1] - (b[0] - a[0]) / length * 1e-4 };
                        var covered = false;
                        for (var other = 0; other < rings.Count && !covered; other++)
                            covered = other != index && Contains(rings[other], probe);
                        if (covered)
                        {
                            line = null;
                            continue;
                        }
                        var from = Point(cuts[cut]);
                        if (line == null)
                        {
                            line = new List<double[]> { from };
                            border.Add(line);
                        }
                        line.Add(Point(cuts[cut + 1]));
                    }
                }
            }
            return border;
        }

        private static double SignedArea(List<double[]> ring)
        {
            var area = 0.0;
            for (var index = 0; index < ring.Count; index++)
            {
                var a = ring[index];
                var b = ring[(index + 1) % ring.Count];
                area += a[0] * b[1] - b[0] * a[1];
            }
            return area / 2.0;
        }

        private static bool Contains(List<double[]> ring, double[] point)
        {
            var inside = false;
            for (int index = 0, previous = ring.Count - 1; index < ring.Count; previous = index++)
            {
                var a = ring[index];
                var b = ring[previous];
                if (a[1] > point[1] != b[1] > point[1] && point[0] < (b[0] - a[0]) * (point[1] - a[1]) / (b[1] - a[1]) + a[0])
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Where an arrow lies in the object frame: its tail, the direction it points and its height; false when its anchors
        /// are gone or not on one arm.
        /// </summary>
        public static bool ArrowFrame(IReadOnlyList<OmsiMarkingAnchor> anchors, OmsiMarkingArrow arrow, out double[] tail, out double[] direction, out double height)
        {
            tail = direction = null;
            height = 0.0;
            var from = Find(anchors, arrow.From);
            var to = Find(anchors, arrow.To);
            if (from == null || to == null || from.IsCrossing || to.IsCrossing || from.Arm != to.Arm)
                return false;
            var outX = -Math.Sin(from.InwardDegrees * Math.PI / 180.0);
            var outY = -Math.Cos(from.InwardDegrees * Math.PI / 180.0);
            // The end nearer the junction lies the distance out from the anchors.
            var nearX = (from.X + to.X) / 2.0 + outX * arrow.Distance;
            var nearY = (from.Y + to.Y) / 2.0 + outY * arrow.Distance;
            height = (from.Height + to.Height) / 2.0;
            if (arrow.Outward)
            {
                tail = new[] { nearX, nearY };
                direction = new[] { outX, outY };
            }
            else
            {
                tail = new[] { nearX + outX * ArrowLength, nearY + outY * ArrowLength };
                direction = new[] { -outX, -outY };
            }
            return true;
        }

        /// <summary>A point of an arrow's own frame in the object frame, with the arrow's height.</summary>
        private static double[] ArrowPoint(double[] tail, double[] direction, double height, double[] local)
        {
            // Right of the direction (dx, dy) in plan is (dy, -dx).
            return new[]
            {
                tail[0] + direction[1] * local[0] + direction[0] * local[1], tail[1] - direction[0] * local[0] + direction[1] * local[1], height
            };
        }

        /// <summary>An arrow as lines in the object frame to show it by, its border; null when it cannot lie.</summary>
        public static List<List<double[]>> ArrowOutlines(IReadOnlyList<OmsiMarkingAnchor> anchors, OmsiMarkingArrow arrow)
        {
            if (!ArrowFrame(anchors, arrow, out var tail, out var direction, out var height))
                return null;
            return Border(ArrowShape(arrow.Kind)).ConvertAll(line => line.ConvertAll(point => ArrowPoint(tail, direction, height, point)));
        }

        /// <summary>
        /// The anchors on either side of the lane of an arm around a point of the object frame, at the arm's mouth, or null.
        /// The point lies between them across the arm and from a metre inside the mouth to <paramref name="reach"/> metres
        /// beyond it; the space between two edges of a median is no lane.
        /// </summary>
        public static OmsiMarkingAnchor[] LaneAt(IReadOnlyList<OmsiMarkingAnchor> anchors, double x, double y, double reach)
        {
            var arms = new SortedDictionary<int, List<OmsiMarkingAnchor>>();
            foreach (var anchor in anchors)
                if (!anchor.IsCrossing && anchor.Row == 1)
                {
                    if (!arms.TryGetValue(anchor.Arm, out var row))
                        arms[anchor.Arm] = row = new List<OmsiMarkingAnchor>();
                    row.Add(anchor);
                }
            foreach (var row in arms.Values)
            {
                row.Sort((first, second) => first.Index.CompareTo(second.Index));
                // The arm leaves the junction against its inward direction.
                var heading = (row[0].InwardDegrees - 180.0) * Math.PI / 180.0;
                var along = x * Math.Sin(heading) + y * Math.Cos(heading);
                var lateral = x * Math.Cos(heading) - y * Math.Sin(heading);
                var mouth = row[0].X * Math.Sin(heading) + row[0].Y * Math.Cos(heading);
                if (along < mouth - 1.0 || along > mouth + reach)
                    continue;
                for (var index = 1; index < row.Count; index++)
                    if (lateral >= row[index - 1].Lateral && lateral <= row[index].Lateral && !(row[index - 1].Kind == "median" && row[index].Kind == "median"))
                        return new[] { row[index - 1], row[index] };
            }
            return null;
        }

        /// <summary>
        /// True when the lane between two anchors of an arm runs into the junction, as the profile says; true too where it
        /// says nothing. In the arm's frame lanes against the profile's direction run in.
        /// </summary>
        public static bool RunsIn(OmsiJunctionLayout layout, OmsiMarkingAnchor from, OmsiMarkingAnchor to)
        {
            if (layout == null || from == null || to == null || from.Arm < 0 || from.Arm >= layout.Arms.Count || layout.Arms[from.Arm].Profile == null)
                return true;
            var middle = (from.Lateral + to.Lateral) / 2.0;
            OmsiSplineLane nearest = null;
            foreach (var lane in layout.Arms[from.Arm].Profile.Lanes)
                if (lane.Kind == 0 && Math.Abs(lane.X - middle) <= HalfWidth(lane) && (nearest == null || Math.Abs(lane.X - middle) < Math.Abs(nearest.X - middle)))
                    nearest = lane;
            return nearest == null || nearest.Direction != 0;
        }

        /// <summary>
        /// How far right of a line a band beside it lies, so that across one arm it lies in the arm away from the junction;
        /// 0 for a line between arms or from a crossing.
        /// </summary>
        private static double BesideOffset(IReadOnlyList<OmsiMarkingAnchor> anchors, OmsiMarkingLine line, List<double[]> path, double width)
        {
            var from = Find(anchors, line.From);
            var to = Find(anchors, line.To);
            if (from == null || to == null || from.IsCrossing || to.IsCrossing || from.Arm != to.Arm || path.Count < 2)
                return 0.0;
            var dx = path[path.Count - 1][0] - path[0][0];
            var dy = path[path.Count - 1][1] - path[0][1];
            // The arm leaves the junction against its inward direction; right of the line is (dy, -dx).
            var outX = -Math.Sin(from.InwardDegrees * Math.PI / 180.0);
            var outY = -Math.Cos(from.InwardDegrees * Math.PI / 180.0);
            return dy * outX - dx * outY >= 0.0 ? width / 2.0 : -width / 2.0;
        }

        /// <summary>
        /// The outline of an area in the object frame as points (x, y, height), each once, or null when it has fewer than
        /// three corners or one of them is gone. Between two corners on one line the side follows the line, the shortest
        /// way where several lines join them; otherwise it runs straight.
        /// </summary>
        public static List<double[]> Outline(IReadOnlyList<OmsiMarkingAnchor> anchors, IList<OmsiMarkingLine> lines, OmsiMarkingArea area)
        {
            if (area == null || area.Corners.Count < 3)
                return null;
            var corners = new List<OmsiMarkingAnchor>();
            foreach (var key in area.Corners)
            {
                var corner = Find(anchors, key);
                if (corner == null)
                    return null;
                corners.Add(corner);
            }
            var paths = new List<List<double[]>>();
            foreach (var line in lines ?? new List<OmsiMarkingLine>())
                if (Path(anchors, line) is List<double[]> path)
                    paths.Add(path);
            var outline = new List<double[]>();
            for (var index = 0; index < corners.Count; index++)
            {
                var side = Side(paths, corners[index], corners[(index + 1) % corners.Count]);
                for (var point = 0; point < side.Count - 1; point++)
                    outline.Add(side[point]);
            }
            return outline.Count >= 3 ? outline : null;
        }

        /// <summary>The side of an area from one corner to the next: along the shortest stretch of a line through both, or straight.</summary>
        private static List<double[]> Side(List<List<double[]>> paths, OmsiMarkingAnchor from, OmsiMarkingAnchor to)
        {
            List<double[]> best = null;
            var bestLength = double.MaxValue;
            foreach (var path in paths)
            {
                var distances = Distances(path);
                var start = Along(path, distances, from);
                var end = Along(path, distances, to);
                if (start < 0.0 || end < 0.0 || Math.Abs(end - start) < 0.01 || Math.Abs(end - start) >= bestLength)
                    continue;
                best = Cut(path, Math.Min(start, end), Math.Max(start, end));
                if (start > end)
                    best.Reverse();
                bestLength = Math.Abs(end - start);
            }
            return best ?? new List<double[]> { new[] { from.X, from.Y, from.Height }, new[] { to.X, to.Y, to.Height } };
        }

        /// <summary>How far along a path an anchor lies, or -1 when it lies off the path.</summary>
        private static double Along(List<double[]> path, double[] distances, OmsiMarkingAnchor anchor)
        {
            var best = -1.0;
            var bestDistance = 0.05;
            for (var index = 1; index < path.Count; index++)
            {
                var a = path[index - 1];
                var b = path[index];
                var dx = b[0] - a[0];
                var dy = b[1] - a[1];
                var lengthSquared = dx * dx + dy * dy;
                var t = lengthSquared < 1e-12 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((anchor.X - a[0]) * dx + (anchor.Y - a[1]) * dy) / lengthSquared));
                var distance = Math.Sqrt(Square(a[0] + dx * t - anchor.X) + Square(a[1] + dy * t - anchor.Y));
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = distances[index - 1] + (distances[index] - distances[index - 1]) * t;
                }
            }
            return best;
        }

        /// <summary>
        /// The direction of an area's stripes in degrees counter-clockwise from x: its angle from the longest side between two
        /// corners, which mostly runs with the traffic.
        /// </summary>
        public static double StripeDegrees(IReadOnlyList<OmsiMarkingAnchor> anchors, OmsiMarkingArea area)
        {
            var longest = -1.0;
            var degrees = 0.0;
            for (var index = 0; index < area.Corners.Count; index++)
            {
                var from = Find(anchors, area.Corners[index]);
                var to = Find(anchors, area.Corners[(index + 1) % area.Corners.Count]);
                if (from == null || to == null)
                    continue;
                var length = Square(to.X - from.X) + Square(to.Y - from.Y);
                if (length <= longest)
                    continue;
                longest = length;
                degrees = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI;
            }
            return degrees + area.Angle;
        }

        /// <summary>
        /// Fills an outline with stripes in a direction, cut along the outline: each stripe is laid in thin bands across it,
        /// each band from where the outline crosses its one edge to where it crosses the other.
        /// </summary>
        private static void Hatch(OmsiGeneratedMesh mesh, List<double[]> outline, double degrees, double stroke, double gap)
        {
            var radians = degrees * Math.PI / 180.0;
            var ux = Math.Cos(radians);
            var uy = Math.Sin(radians);
            // u runs along the stripes, v across them.
            var u = new double[outline.Count];
            var v = new double[outline.Count];
            double lowest = double.MaxValue, highest = double.MinValue;
            for (var index = 0; index < outline.Count; index++)
            {
                u[index] = outline[index][0] * ux + outline[index][1] * uy;
                v[index] = -outline[index][0] * uy + outline[index][1] * ux;
                lowest = Math.Min(lowest, v[index]);
                highest = Math.Max(highest, v[index]);
            }
            var period = stroke + gap;
            var count = (int)Math.Floor((highest - lowest - stroke) / period) + 1;
            if (count < 1)
                return;
            // The stripes sit in the middle of the area.
            var first = lowest + (highest - lowest - (count - 1) * period - stroke) / 2.0;
            var steps = Math.Max(1, (int)Math.Ceiling(stroke / HatchStep));
            for (var stripe = 0; stripe < count; stripe++)
            {
                var level = first + stripe * period;
                var before = Across(u, v, level);
                for (var step = 1; step <= steps; step++)
                {
                    var next = level + stroke * step / steps;
                    var after = Across(u, v, next);
                    // Where the outline turns within the band, the crossings do not pair up; the band is left out there.
                    if (before.Count == after.Count)
                        for (var pair = 0; pair + 1 < after.Count; pair += 2)
                        {
                            var corner = mesh.Positions.Count / 3;
                            AddAreaPoint(mesh, outline, ux, uy, before[pair], next - stroke / steps);
                            AddAreaPoint(mesh, outline, ux, uy, before[pair + 1], next - stroke / steps);
                            AddAreaPoint(mesh, outline, ux, uy, after[pair + 1], next);
                            AddAreaPoint(mesh, outline, ux, uy, after[pair], next);
                            OmsiJunctionLayout.AddUpward(mesh, corner, corner + 1, corner + 2);
                            OmsiJunctionLayout.AddUpward(mesh, corner, corner + 2, corner + 3);
                        }
                    before = after;
                }
            }
        }

        /// <summary>Where an outline crosses a line across the stripes, along them, in order.</summary>
        private static List<double> Across(double[] u, double[] v, double level)
        {
            var found = new List<double>();
            for (var index = 0; index < u.Length; index++)
            {
                var next = (index + 1) % u.Length;
                if (v[index] <= level && v[next] > level || v[next] <= level && v[index] > level)
                    found.Add(u[index] + (u[next] - u[index]) * (level - v[index]) / (v[next] - v[index]));
            }
            found.Sort();
            return found;
        }

        private static void AddAreaPoint(OmsiGeneratedMesh mesh, List<double[]> outline, double ux, double uy, double along, double across)
        {
            var x = along * ux - across * uy;
            var y = along * uy + across * ux;
            // The height of the outline round the point, weighted by nearness.
            double weights = 0.0, sum = 0.0, height = double.NaN;
            foreach (var point in outline)
            {
                var squared = Square(point[0] - x) + Square(point[1] - y);
                if (squared < 1e-8)
                {
                    height = point[2];
                    break;
                }
                weights += 1.0 / squared;
                sum += point[2] / squared;
            }
            mesh.Positions.Add(x);
            mesh.Positions.Add((double.IsNaN(height) ? sum / weights : height) + AreaLift);
            mesh.Positions.Add(y);
            mesh.Uvs.Add(0.0);
            mesh.Uvs.Add(0.0);
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
        /// <summary>
        /// Direction arrows from the ways through chosen with the lanes tool (ADR 048, stage 4 of the AI paths): on each lane
        /// leading in, an arrow for where its connections go, straight on within 30° as the paths' indicators are set, else
        /// left or right. An arm gets them where a lane leading in does not go every way the arm can, such as a lane kept
        /// for turning left; then each of its lanes with a way through gets one. Arms whose lanes all go every way, and
        /// lanes without a way through, get none. Null for a junction with every way through, which says nothing a driver
        /// needs an arrow for.
        /// </summary>
        public static List<OmsiMarkingArrow> SuggestArrows(OmsiJunctionLayout layout, ICollection<string> connections)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (connections == null || layout.IsTransition)
                return null;
            var anchors = Anchors(layout);
            var ends = layout.LaneEnds();
            var arrows = new List<OmsiMarkingArrow>();
            for (var arm = 0; arm < layout.Arms.Count; arm++)
            {
                var inbound = ends.FindAll(end => end.Inbound && end.Arm == arm);
                // Where this arm's traffic could go: the arms with lanes leading out.
                var possible = Directions(layout, arm, ends.FindAll(end => !end.Inbound && end.Arm != arm).Select(end => end.Arm).Distinct());
                var ways = new List<int>();
                foreach (var end in inbound)
                {
                    var targets = new List<int>();
                    foreach (var key in connections)
                        if (key.StartsWith(arm.ToString(CultureInfo.InvariantCulture) + "." + end.Lane.ToString(CultureInfo.InvariantCulture) + ">", StringComparison.Ordinal) &&
                            int.TryParse(key.Substring(key.IndexOf('>') + 1).Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
                            targets.Add(to);
                    ways.Add(Directions(layout, arm, targets.Distinct()));
                }
                if (ways.TrueForAll(directions => directions == 0 || directions == possible))
                    continue;
                for (var index = 0; index < inbound.Count; index++)
                {
                    var end = inbound[index];
                    var directions = ways[index];
                    if (directions == 0)
                        continue;
                    var lane = LaneAt(anchors, end.X, end.Y, 15.0);
                    if (lane == null)
                        continue;
                    var info = OmsiMarkingArrowInfo.All.FirstOrDefault(each =>
                        each.Straight == ((directions & 1) != 0) && each.Left == ((directions & 2) != 0) && each.Right == ((directions & 4) != 0));
                    if (info != null)
                        arrows.Add(new OmsiMarkingArrow { From = lane[0].Key, To = lane[1].Key, Kind = info.Kind });
                }
            }
            return arrows;
        }

        /// <summary>The ways from one arm to others: 1 straight on, 2 left, 4 right, added together.</summary>
        private static int Directions(OmsiJunctionLayout layout, int from, IEnumerable<int> arms)
        {
            var directions = 0;
            foreach (var to in arms)
            {
                if (to == from)
                    continue;
                var turn = OmsiRoadPlanner.Normalize(layout.Arms[to].HeadingDegrees - (layout.Arms[from].HeadingDegrees + 180.0));
                directions |= Math.Abs(turn) < 30.0 ? 1 : turn > 0.0 ? 4 : 2;
            }
            return directions;
        }

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
        /// The line of a .sco that keeps the marked areas, such as
        /// <c>MapWorks marking areas: hatched 45 0.1.0,1.1.2,x0.0.1~1.0.2~1.0.1~2.0.1~0</c>: the fill, the angle of its
        /// stripes and the corners in order.
        /// </summary>
        public static string WriteAreas(IEnumerable<OmsiMarkingArea> areas)
        {
            var text = new StringBuilder(AreasPrefix);
            var first = true;
            foreach (var area in areas)
            {
                text.Append(first ? " " : "; ").Append(OmsiMarkingFillInfo.Of(area.Fill).Key).Append(' ')
                    .Append(area.Angle.ToString("0.#", CultureInfo.InvariantCulture)).Append(' ').Append(string.Join(",", area.Corners));
                first = false;
            }
            return text.ToString();
        }

        /// <summary>The marked areas in the text of a .sco; none without an areas line. Areas it cannot read are left out.</summary>
        public static List<OmsiMarkingArea> ReadAreas(string scoText)
        {
            var areas = new List<OmsiMarkingArea>();
            foreach (var raw in (scoText ?? string.Empty).Split('\n'))
            {
                var text = raw.Trim();
                if (!text.StartsWith(AreasPrefix, StringComparison.Ordinal))
                    continue;
                foreach (var part in text.Substring(AreasPrefix.Length).Split(';'))
                {
                    var words = part.Trim().Split(' ');
                    var fill = words.Length == 3 ? OmsiMarkingFillInfo.ByKey(words[0]) : null;
                    if (fill == null || !double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
                        continue;
                    var area = new OmsiMarkingArea { Fill = fill.Fill, Angle = angle };
                    area.Corners.AddRange(words[2].Split(','));
                    if (area.Corners.Count >= 3)
                        areas.Add(area);
                }
                break;
            }
            return areas;
        }

        /// <summary>
        /// The line of a .sco that keeps the arrows, such as <c>MapWorks marking arrows: 1.1.0~1.1.1 straightleft 1; 1.1.1~1.1.2 straight 1 out</c>:
        /// the anchors of the lane, the kind, the distance and <c>out</c> for a lane running out.
        /// </summary>
        public static string WriteArrows(IEnumerable<OmsiMarkingArrow> arrows)
        {
            var text = new StringBuilder(ArrowsPrefix);
            var first = true;
            foreach (var arrow in arrows)
            {
                text.Append(first ? " " : "; ").Append(arrow.From).Append('~').Append(arrow.To).Append(' ').Append(OmsiMarkingArrowInfo.Of(arrow.Kind).Key)
                    .Append(' ').Append(arrow.Distance.ToString("0.##", CultureInfo.InvariantCulture));
                if (arrow.Outward)
                    text.Append(" out");
                first = false;
            }
            return text.ToString();
        }

        /// <summary>The arrows in the text of a .sco; none without an arrows line. Arrows it cannot read are left out.</summary>
        public static List<OmsiMarkingArrow> ReadArrows(string scoText)
        {
            var arrows = new List<OmsiMarkingArrow>();
            foreach (var raw in (scoText ?? string.Empty).Split('\n'))
            {
                var text = raw.Trim();
                if (!text.StartsWith(ArrowsPrefix, StringComparison.Ordinal))
                    continue;
                foreach (var part in text.Substring(ArrowsPrefix.Length).Split(';'))
                {
                    var words = part.Trim().Split(' ');
                    var lane = words[0].Split('~');
                    var kind = words.Length >= 3 ? OmsiMarkingArrowInfo.ByKey(words[1]) : null;
                    if (lane.Length != 2 || kind == null || !double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
                        continue;
                    arrows.Add(new OmsiMarkingArrow
                    {
                        From = lane[0], To = lane[1], Kind = kind.Kind, Distance = distance, Outward = words.Length > 3 && words[3] == "out"
                    });
                }
                break;
            }
            return arrows;
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
