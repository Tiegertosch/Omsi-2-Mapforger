using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A junction this editor made (ADR 027, 028), read back from its .sco under Sceneryobjects\Mapforge: its arms in the
    /// order of its connection points, so it can be made again with an arm turned. Which arms are main and which see their
    /// profile turned over is written in a line of the .sco since 25 October 2026; for junctions made before, the arms
    /// ahead and back count as main and the rest follows from the roads joined (<see cref="OmsiJunctionRebuild"/>).
    /// </summary>
    public sealed class OmsiGeneratedJunction
    {
        internal const string ArmsPrefix = "MapWorks arms:";
        private const string JunctionMark = ": a junction of ";

        private OmsiGeneratedJunction()
        {
        }

        public string AssetPath { get; private set; }

        /// <summary>The name it was made with, without the number the store adds to a name in use.</summary>
        public string Name { get; private set; }

        /// <summary>The arms in the order of the object's connection points.</summary>
        public List<OmsiJunctionArm> Arms { get; } = new List<OmsiJunctionArm>();

        /// <summary>True when the .sco says which arms are main and turned over; otherwise those are guessed.</summary>
        public bool SidesKnown { get; private set; }

        /// <summary>Its road markings (ADR 044), by the anchors of its arms.</summary>
        public List<OmsiMarkingLine> Markings { get; } = new List<OmsiMarkingLine>();

        /// <summary>True for an object file the editor makes, under Sceneryobjects\Mapforge.</summary>
        public static bool IsMadeHere(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   assetPath.Trim().Replace('/', '\\').StartsWith(OmsiGeneratedObjectStore.Folder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The junction an object file holds, or null when the editor did not make it as a junction or it cannot be read.</summary>
        public static OmsiGeneratedJunction Read(string installationRoot, string assetPath, Func<string, OmsiSplineProfile> profiles)
        {
            if (!IsMadeHere(assetPath))
                return null;
            try
            {
                var path = OmsiRelativePath.Resolve(installationRoot, assetPath);
                return File.Exists(path) ? Parse(assetPath, File.ReadAllBytes(path), profiles) : null;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException ||
                                              exception is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The junction in the bytes of a .sco, or null when they hold none the editor made.</summary>
        public static OmsiGeneratedJunction Parse(string assetPath, byte[] bytes, Func<string, OmsiSplineProfile> profiles)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            var text = OmsiTextFile.Decode(bytes);
            if (text.IndexOf(JunctionMark, StringComparison.Ordinal) < 0)
                return null;
            var helpers = OmsiSceneryObject.Parse(bytes).SplineHelpers;
            if (helpers.Count < 2)
                return null;
            var junction = new OmsiGeneratedJunction { AssetPath = assetPath, Name = NameOf(assetPath) };
            foreach (var helper in helpers)
                junction.Arms.Add(new OmsiJunctionArm
                {
                    HeadingDegrees = Normalize360(helper.HeadingDegrees), ProfilePath = helper.SplinePath, Profile = profiles?.Invoke(helper.SplinePath)
                });
            junction.Markings.AddRange(OmsiJunctionMarkings.Read(text));
            var sides = ReadSides(text, helpers.Count);
            if (sides != null)
            {
                for (var index = 0; index < sides.Length; index++)
                {
                    junction.Arms[index].Main = sides[index].Contains(" main");
                    junction.Arms[index].Mirrored = sides[index].Contains(" mirrored");
                }
                junction.SidesKnown = true;
            }
            else
            {
                var pair = OmsiRoadJunctions.MainPair(helpers);
                if (pair != null)
                    foreach (var index in pair)
                        junction.Arms[index].Main = true;
            }
            return junction;
        }

        /// <summary>The words after each heading of the arms line, one per arm with a space before, or null without a fitting line.</summary>
        private static string[] ReadSides(string text, int count)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith(ArmsPrefix, StringComparison.Ordinal))
                    continue;
                var parts = line.Substring(ArmsPrefix.Length).Split(';');
                if (parts.Length != count)
                    return null;
                for (var index = 0; index < parts.Length; index++)
                {
                    var words = parts[index].Trim();
                    var space = words.IndexOf(' ');
                    parts[index] = space < 0 ? string.Empty : words.Substring(space);
                }
                return parts;
            }
            return null;
        }

        private static string NameOf(string assetPath)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName((assetPath ?? string.Empty).Replace('/', '\\').Replace('\\', Path.DirectorySeparatorChar)));
            return Regex.Replace(folder ?? string.Empty, "_[0-9]+$", string.Empty);
        }

        internal static double Normalize360(double degrees)
        {
            return (degrees % 360.0 + 360.0) % 360.0;
        }
    }

    /// <summary>
    /// What turning one arm of a junction the editor made would do: the arms it is made again with, its connection points
    /// at its place, and the roads joined to it led to their points.
    /// </summary>
    public sealed class OmsiJunctionArmTurn
    {
        /// <summary>The turned arm, by its place among the junction's connection points.</summary>
        public int Arm { get; internal set; }

        /// <summary>The arm's new heading in the object, kept clear of its neighbours.</summary>
        public double HeadingDegrees { get; internal set; }

        public List<OmsiJunctionArm> Arms { get; } = new List<OmsiJunctionArm>();

        /// <summary>The connection points the junction made again would have, in world metres.</summary>
        public List<OmsiJunctionPort> Ports { get; } = new List<OmsiJunctionPort>();

        /// <summary>How the road on each moved connection point is led to it.</summary>
        public List<OmsiJunctionRoadLead> Leads { get; } = new List<OmsiJunctionRoadLead>();

        /// <summary>Why the junction cannot be made this way, or null.</summary>
        public string Problem { get; internal set; }

        public bool HasErrors => Problem != null || Leads.Exists(lead => lead.Piece.HasErrors);
    }

    /// <summary>
    /// How the road on a connection point is led to where the point moves, as when a junction is inserted (ADR 025): it is
    /// cut where it lies far enough beyond the new point, the part before the cut goes, and a short piece closes the gap.
    /// </summary>
    public sealed class OmsiJunctionRoadLead
    {
        /// <summary>The arm, by its place among the junction's connection points.</summary>
        public int Arm { get; internal set; }

        /// <summary>The connection point the road is led to.</summary>
        public OmsiJunctionPort Port { get; internal set; }

        /// <summary>The spline on the point before, and whether the point is that spline's end rather than its start.</summary>
        public int SplineId { get; internal set; }

        public bool AtEnd { get; internal set; }

        /// <summary>How far along the road from the point it is cut, and where in world metres.</summary>
        public double CutMeters { get; internal set; }

        public OmsiMapPoint Cut { get; internal set; }

        /// <summary>The profile and terrain fit of the spline at the cut, which the piece takes over.</summary>
        public string ProfilePath { get; internal set; }

        public bool AlignsTerrain { get; internal set; }

        /// <summary>The short piece from the cut to the point.</summary>
        public OmsiRoadPlan Piece { get; internal set; }
    }

    /// <summary>
    /// Turns an arm of a junction the editor made: the junction is made again with the arm at its new heading, and the
    /// roads joined to its connection points are led to them without a kink. Every point may move, as the length of each
    /// arm follows from the corners beside it (ADR 027). Prepared once when the drag starts, planned at every pointer move.
    /// </summary>
    public sealed class OmsiJunctionRebuild
    {
        /// <summary>Neighbouring arms stay at least this far apart, as junctions are not made for flatter crossings (ADR 028).</summary>
        public const double MinimumArmAngleDegrees = 30.0;

        private const double SamePlaceMeters = 0.001;

        /// <summary>
        /// The piece leading a road to a moved point bends no tighter than this where the road allows: the road is cut as
        /// near the point as that permits, at least the 5 m of an inserted junction beyond it. A point that moved to the
        /// side, as when an arm turns or a road curves away from a longer arm, needs a longer piece.
        /// </summary>
        public const double BendRadiusMeters = 25.0;

        /// <summary>The road is followed this far at most to find where to cut it; failing that, the gentlest cut is taken.</summary>
        public const double LeadReachMeters = 120.0;

        private const double SearchStepMeters = 0.5;

        private readonly OmsiMapSummary summary;
        private OmsiSplineEnd[] joined;
        private OmsiJunctionPort[] partners;

        private OmsiJunctionRebuild(OmsiMapSummary summary, OmsiGeneratedJunction made, OmsiTileSummary tile, OmsiObjectRecord junction,
            List<OmsiJunctionPort> ports)
        {
            this.summary = summary;
            Made = made;
            Tile = tile;
            Junction = junction;
            Ports = ports;
        }

        public OmsiGeneratedJunction Made { get; }
        public OmsiTileSummary Tile { get; }
        public OmsiObjectRecord Junction { get; }

        /// <summary>The junction's connection points now, in the order of its arms.</summary>
        public IReadOnlyList<OmsiJunctionPort> Ports { get; }

        /// <summary>
        /// Finds what is joined to each connection point of a placed junction the editor made. Null when the object's points
        /// do not match the junction read from its file.
        /// </summary>
        public static OmsiJunctionRebuild Prepare(OmsiMapSummary summary, OmsiJunctionCatalog catalog, OmsiGeneratedJunction made, OmsiTileSummary tile,
            OmsiObjectRecord junction)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (made == null)
                throw new ArgumentNullException(nameof(made));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (junction == null)
                throw new ArgumentNullException(nameof(junction));
            var ports = catalog.Ports(tile, junction, summary.TileSizeMeters);
            if (ports.Count != made.Arms.Count)
                return null;
            var around = catalog.PortsAround(summary, tile);
            var rebuild = new OmsiJunctionRebuild(summary, made, tile, junction, ports)
            {
                joined = new OmsiSplineEnd[ports.Count],
                partners = new OmsiJunctionPort[ports.Count]
            };
            var tolerances = new OmsiJunctionTolerances();
            var checks = OmsiJunctionCatalog.Check(summary, ports, tolerances, around);
            for (var index = 0; index < checks.Count; index++)
            {
                var check = checks[index];
                if (check.Partner != null)
                    rebuild.partners[index] = check.Partner;
                else if (check.End != null && check.DistanceMeters <= tolerances.PositionMeters && check.HeadingOffsetDegrees <= 90.0)
                    rebuild.joined[index] = check.End;
            }
            return rebuild;
        }

        /// <summary>The spline end on a connection point, by the place of its arm, or null.</summary>
        public OmsiSplineEnd JoinedAt(int arm)
        {
            return joined[arm];
        }

        /// <summary>
        /// Plans the junction with one arm at a new heading in the object, clockwise from its forward direction. The heading
        /// is kept at least <see cref="MinimumArmAngleDegrees"/> from the arms beside it, between which it stays.
        /// </summary>
        /// <param name="roadTexture">A plain carriageway texture relative to the installation, or null, as the junction is made with.</param>
        public OmsiJunctionArmTurn PlanTurn(int arm, double headingDegrees, string roadTexture)
        {
            if (arm < 0 || arm >= Made.Arms.Count)
                throw new ArgumentOutOfRangeException(nameof(arm));
            var turn = new OmsiJunctionArmTurn { Arm = arm, HeadingDegrees = Clamp(arm, headingDegrees) };
            for (var index = 0; index < Made.Arms.Count; index++)
            {
                var source = Made.Arms[index];
                // Junctions made before the arms line: a joined road says whether it runs towards the junction; an arm
                // without one keeps the guess the junction was made with, the arm back of the main road turned over.
                var mirrored = Made.SidesKnown
                    ? source.Mirrored
                    : joined[index] != null ? joined[index].AtEnd : source.Main && Math.Abs(OmsiRoadPlanner.Normalize(source.HeadingDegrees - 180.0)) < 0.5;
                turn.Arms.Add(new OmsiJunctionArm
                {
                    HeadingDegrees = index == arm ? turn.HeadingDegrees : source.HeadingDegrees, ProfilePath = source.ProfilePath, Profile = source.Profile,
                    Main = source.Main, Mirrored = mirrored
                });
            }

            OmsiJunctionLayout layout;
            try
            {
                layout = OmsiJunctionLayout.Build(turn.Arms, roadTexture);
            }
            catch (ArgumentException exception)
            {
                turn.Problem = exception.Message;
                return turn;
            }
            var helpers = new List<OmsiSplineHelper>();
            foreach (var made in layout.Arms)
            {
                var radians = made.HeadingDegrees * Math.PI / 180.0;
                helpers.Add(new OmsiSplineHelper
                {
                    SplinePath = made.ProfilePath, X = Math.Sin(radians) * made.Length, Height = 0.0, Y = Math.Cos(radians) * made.Length,
                    HeadingDegrees = made.HeadingDegrees
                });
            }
            var ports = OmsiJunctionCatalog.Place(helpers, Tile, Junction, summary.TileSizeMeters, Junction.LocalX, Junction.LocalY, Junction.Height,
                Junction.HeadingDegrees, Junction.PitchDegrees, Junction.BankDegrees);

            for (var index = 0; index < turn.Arms.Count; index++)
            {
                var place = layout.Arms.FindIndex(made => Math.Abs(OmsiRoadPlanner.Normalize(made.HeadingDegrees - turn.Arms[index].HeadingDegrees)) < 1e-6);
                var port = ports[place];
                turn.Ports.Add(port);
                var before = Ports[index];
                var moved = Distance(port.Position, before.Position) > SamePlaceMeters || Math.Abs(port.Height - before.Height) > SamePlaceMeters ||
                            Math.Abs(OmsiRoadPlanner.Normalize(port.OutwardHeadingDegrees - before.OutwardHeadingDegrees)) > 0.01;
                if (!moved)
                    continue;
                if (partners[index] != null)
                {
                    turn.Problem = "Point " + (index + 1) + " meets object #" + partners[index].Object.Id + " directly and would come off it.";
                    return turn;
                }
                if (joined[index] == null)
                    continue;
                var lead = Lead(index, joined[index], port);
                if (lead == null)
                {
                    turn.Problem = "The road at point " + (index + 1) + " ends too near the junction to be led to its point.";
                    return turn;
                }
                turn.Leads.Add(lead);
            }
            return turn;
        }

        /// <summary>
        /// Plans leading the road on a connection point to where the point moves: cut at the first place along it that lies
        /// at least 5 m beyond the new point, ahead along its heading, and lets the piece bend no tighter than
        /// <see cref="BendRadiusMeters"/>; failing that within <see cref="LeadReachMeters"/>, the gentlest such place. Null
        /// when the road ends before any.
        /// </summary>
        private OmsiJunctionRoadLead Lead(int arm, OmsiSplineEnd end, OmsiJunctionPort port)
        {
            var spline = end.Spline;
            var chain = OmsiSplineChain.Around(summary, end.Tile, spline, LeadReachMeters);
            // Chain distances run along the spline on the point; away from the junction is backwards from its end.
            var from = end.AtEnd ? spline.LengthMeters : 0.0;
            var away = end.AtEnd ? -1.0 : 1.0;
            var radians = port.OutwardHeadingDegrees * Math.PI / 180.0;
            OmsiJunctionRoadLead gentlest = null;
            var gentlestRadius = 0.0;
            for (var along = 0.0; along <= LeadReachMeters; along += SearchStepMeters)
            {
                var at = from + away * along;
                // Some road stays beyond the cut, for the piece to join.
                if (away > 0.0 ? at > chain.End - OmsiJunctionInsertion.ConnectorMeters : at < chain.Start + OmsiJunctionInsertion.ConnectorMeters)
                    break;
                chain.FrameAt(at, out var point, out _, out _);
                if ((point.X - port.Position.X) * Math.Sin(radians) + (point.Y - port.Position.Y) * Math.Cos(radians) < OmsiJunctionInsertion.ConnectorMeters)
                    continue;
                // A cut this near a joint lies on it, as no spline is split off that short.
                var segment = chain.At(at);
                if (at - segment.Start <= OmsiRoadPlanner.MinimumLength)
                    at = segment.Start;
                else if (segment.End - at <= OmsiRoadPlanner.MinimumLength)
                    at = segment.End;
                chain.FrameAt(at, out point, out var heading, out var height);
                // Beyond the cut the road keeps its own direction: it ends at the cut when it runs towards the junction.
                var beyond = chain.At(at + away * OmsiRoadPlanner.MinimumLength);
                var endsThere = (beyond.Reversed ? -1.0 : 1.0) == -away;
                var towards = away < 0.0 ? heading : heading + 180.0;
                var piece = OmsiJunctionInsertion.PieceToPort(point, towards, height, endsThere, port);
                if (piece.HasErrors)
                    continue;
                var tightest = double.MaxValue;
                foreach (var part in piece.Pieces)
                    if (part.RadiusMeters != 0.0)
                        tightest = Math.Min(tightest, Math.Abs(part.RadiusMeters));
                if (gentlest != null && tightest <= gentlestRadius)
                    continue;
                gentlestRadius = tightest;
                gentlest = new OmsiJunctionRoadLead
                {
                    Arm = arm, Port = port, SplineId = spline.Id, AtEnd = end.AtEnd, CutMeters = Math.Abs(at - from), Cut = point,
                    ProfilePath = beyond.Spline.AssetPath, AlignsTerrain = beyond.Spline.AlignsTerrain, Piece = piece
                };
                if (tightest >= BendRadiusMeters)
                    break;
            }
            return gentlest;
        }

        /// <summary>
        /// A heading for an arm kept within the gap between its neighbours, at least <see cref="MinimumArmAngleDegrees"/>
        /// from each; outside, the nearer limit.
        /// </summary>
        private double Clamp(int arm, double headingDegrees)
        {
            var current = Made.Arms[arm].HeadingDegrees;
            double behind = 360.0, ahead = 360.0;
            for (var index = 0; index < Made.Arms.Count; index++)
            {
                if (index == arm)
                    continue;
                var other = Made.Arms[index].HeadingDegrees;
                var back = OmsiGeneratedJunction.Normalize360(current - other);
                var forward = OmsiGeneratedJunction.Normalize360(other - current);
                behind = Math.Min(behind, back == 0.0 ? 360.0 : back);
                ahead = Math.Min(ahead, forward == 0.0 ? 360.0 : forward);
            }
            // Measured clockwise from the neighbour behind, the arm may lie anywhere in the gap but near its ends.
            var gap = behind + ahead;
            if (gap > 360.0)
                gap = 360.0;
            var from = current - behind;
            var offset = OmsiGeneratedJunction.Normalize360(headingDegrees - from);
            var lowest = MinimumArmAngleDegrees;
            var highest = gap - MinimumArmAngleDegrees;
            if (highest < lowest)
                return current;
            if (offset < lowest || offset > highest)
            {
                // Outside the gap: the limit nearer round the circle.
                var toLowest = Math.Min(Math.Abs(offset - lowest), 360.0 - Math.Abs(offset - lowest));
                var toHighest = Math.Min(Math.Abs(offset - highest), 360.0 - Math.Abs(offset - highest));
                offset = toLowest <= toHighest ? lowest : highest;
            }
            return OmsiGeneratedJunction.Normalize360(from + offset);
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }
}
