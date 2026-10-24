using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Where a new road takes its heights from.</summary>
    public enum OmsiRoadHeightMode
    {
        /// <summary>Every piece boundary lies on the terrain; the road keeps its own heights and does not change the terrain.</summary>
        FollowTerrain,

        /// <summary>The height runs evenly from the start height to a given end height, whatever the terrain does.</summary>
        Manual,

        /// <summary>
        /// The height follows the terrain smoothed over a stretch of road, with the gradient changing steadily across piece
        /// boundaries instead of in steps; at a joined start it continues the height and gradient of the road it joins.
        /// </summary>
        Smooth
    }

    public enum OmsiRoadIssueSeverity
    {
        /// <summary>Worth knowing, such as a road high above the terrain.</summary>
        Note,

        /// <summary>Worth checking, such as a steep gradient; the road can still be built.</summary>
        Warning,

        /// <summary>The road cannot be built, such as a missing terrain height.</summary>
        Error
    }

    /// <summary>A finding about a planned road, with the world position to mark in the view.</summary>
    public sealed class OmsiRoadIssue
    {
        public OmsiRoadIssue(OmsiRoadIssueSeverity severity, string message, double worldX, double worldY)
        {
            Severity = severity;
            Message = message;
            WorldX = worldX;
            WorldY = worldY;
        }

        public OmsiRoadIssueSeverity Severity { get; }
        public string Message { get; }
        public double WorldX { get; }
        public double WorldY { get; }

        public override string ToString()
        {
            return Severity + ": " + Message;
        }
    }

    /// <summary>
    /// One spline of a planned road, in world metres like <see cref="OmsiMapGeometry.TileLocalToWorld"/>: a straight
    /// line or circular arc from its start, with its height running by the gradients at start and end as OMSI reads them.
    /// </summary>
    public sealed class OmsiSplinePiece
    {
        public double StartX { get; internal set; }
        public double StartY { get; internal set; }
        public double StartHeight { get; internal set; }

        /// <summary>Heading at the start in degrees; 0 points north and the angle grows clockwise.</summary>
        public double HeadingDegrees { get; internal set; }

        public double LengthMeters { get; internal set; }

        /// <summary>0 for a straight piece; a positive radius turns right.</summary>
        public double RadiusMeters { get; internal set; }

        public double GradientStartPercent { get; internal set; }
        public double GradientEndPercent { get; internal set; }

        /// <summary>The height at the end: the start height plus the length times the mean of the two gradients.</summary>
        public double EndHeight => StartHeight + LengthMeters * (GradientStartPercent + GradientEndPercent) / 200.0;

        /// <summary>Position and height after <paramref name="distance"/> metres.</summary>
        public void PointAt(double distance, out double x, out double y, out double height)
        {
            OmsiMapGeometry.GetLocalFrameAt(AsRecord(), distance, out var position, out _);
            x = position.X;
            y = position.Y;
            var gradient = GradientStartPercent + (GradientEndPercent - GradientStartPercent) * distance / LengthMeters;
            height = StartHeight + distance * (GradientStartPercent + gradient) / 200.0;
        }

        internal OmsiSplineRecord AsRecord()
        {
            return new OmsiSplineRecord
            {
                LocalX = StartX, LocalY = StartY, Height = StartHeight, HeadingDegrees = HeadingDegrees, LengthMeters = LengthMeters,
                RadiusMeters = RadiusMeters, EndHeight = EndHeight, GradientStartPercent = GradientStartPercent, GradientEndPercent = GradientEndPercent
            };
        }
    }

    /// <summary>What a road between two points should look like.</summary>
    public sealed class OmsiRoadRequest
    {
        public double StartX { get; set; }
        public double StartY { get; set; }

        /// <summary>The heading the road leaves its start with, such as the end of the road it continues; null starts straight towards the end.</summary>
        public double? StartHeadingDegrees { get; set; }

        /// <summary>The height at the start, such as the end of the road it continues; null takes the terrain there.</summary>
        public double? StartHeight { get; set; }

        /// <summary>The gradient in percent the road it continues ends with; smooth roads start with it, others report a change.</summary>
        public double? StartGradientPercent { get; set; }

        public double EndX { get; set; }
        public double EndY { get; set; }

        /// <summary>
        /// The height at the end: the end of a road it joins, or the height entered for <see cref="OmsiRoadHeightMode.Manual"/>.
        /// Null takes the terrain when following it and keeps the start height in manual mode.
        /// </summary>
        public double? EndHeight { get; set; }

        /// <summary>The heading a joined road continues with at the end, to report a kink; null when the end is open.</summary>
        public double? EndJoinHeadingDegrees { get; set; }

        public OmsiRoadHeightMode HeightMode { get; set; } = OmsiRoadHeightMode.FollowTerrain;

        /// <summary>Longer roads are split into pieces of at most this length, so each piece can follow the terrain.</summary>
        public double MaximumPieceLength { get; set; } = 25.0;

        /// <summary>Gradients steeper than this, in percent, are reported as a warning; it is a check, not a rule.</summary>
        public double GradientWarningPercent { get; set; } = 10.0;

        /// <summary>Smooth roads follow the terrain averaged over this many metres.</summary>
        public double SmoothingMeters { get; set; } = 100.0;

        /// <summary>
        /// Half the width of the road's profile, or null when unknown: an arc tighter than this folds its inner edge over,
        /// in OMSI as here, and is reported.
        /// </summary>
        public double? HalfWidthMeters { get; set; }
    }

    /// <summary>A road as it would be built: its pieces, where it ends and what should be checked.</summary>
    public sealed class OmsiRoadPlan
    {
        public List<OmsiSplinePiece> Pieces { get; } = new List<OmsiSplinePiece>();
        public List<OmsiRoadIssue> Issues { get; } = new List<OmsiRoadIssue>();

        public double LengthMeters { get; internal set; }
        public double EndHeadingDegrees { get; internal set; }
        public double EndHeight { get; internal set; }

        /// <summary>The steepest gradient of any piece, as a positive percentage.</summary>
        public double MaximumGradientPercent { get; internal set; }

        public bool HasErrors => Issues.Exists(issue => issue.Severity == OmsiRoadIssueSeverity.Error);
    }

    /// <summary>
    /// Plans roads the way OMSI stores them: a circular arc that leaves its start tangentially and reaches the end point,
    /// or a straight line when no start heading is given, split into equal pieces, with heights from the terrain or
    /// given values. Pure calculation: the same request gives the same plan.
    /// </summary>
    public static class OmsiRoadPlanner
    {
        /// <summary>Roads shorter than this cannot be built.</summary>
        public const double MinimumLength = 0.5;

        /// <summary>Terrain checks in manual mode look this often along the road.</summary>
        private const double TerrainCheckStep = 5.0;

        /// <summary>A road this far below the terrain is reported as running underground.</summary>
        private const double BelowTerrainTolerance = 0.3;

        /// <summary>A road this far above the terrain is noted, as it would need a bridge or embankment.</summary>
        private const double AboveTerrainNote = 3.0;

        /// <summary>An arc turning further than this is refused: its radius grows without bound as the turn nears a full circle.</summary>
        private const double MaximumTurnDegrees = 350.0;

        /// <summary>Headings closer than this meet without a kink.</summary>
        private const double JoinToleranceDegrees = 2.0;

        /// <summary>A change of gradient larger than this, in percentage points, at a joined start is noted.</summary>
        private const double GradientChangeNote = 2.0;

        /// <summary>Smooth roads sample the terrain this often.</summary>
        private const double SmoothSampleStep = 2.5;

        /// <param name="terrain">The terrain heights, or null when the map has none.</param>
        public static OmsiRoadPlan Plan(OmsiRoadRequest request, IOmsiTerrainHeights terrain)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            // A free start and a joined end: the road is planned from the end, so it meets the joined road or junction
            // without a kink, and then turned round to run from the start.
            if (!request.StartHeadingDegrees.HasValue && request.EndJoinHeadingDegrees.HasValue)
                return Reversed(Plan(new OmsiRoadRequest
                {
                    StartX = request.EndX, StartY = request.EndY, StartHeadingDegrees = request.EndJoinHeadingDegrees.Value + 180.0,
                    StartHeight = request.EndHeight, EndX = request.StartX, EndY = request.StartY, EndHeight = request.StartHeight,
                    HeightMode = request.HeightMode, MaximumPieceLength = request.MaximumPieceLength,
                    GradientWarningPercent = request.GradientWarningPercent, SmoothingMeters = request.SmoothingMeters,
                    HalfWidthMeters = request.HalfWidthMeters
                }, terrain), request);
            var plan = new OmsiRoadPlan();
            if (!AllFinite(request.StartX, request.StartY, request.EndX, request.EndY))
            {
                plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The road has no valid start or end point.", request.StartX, request.StartY));
                return plan;
            }

            var chord = Math.Sqrt(Square(request.EndX - request.StartX) + Square(request.EndY - request.StartY));
            if (chord < MinimumLength)
            {
                plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "Start and end lie too close together.", request.StartX, request.StartY));
                return plan;
            }

            var heading = request.StartHeadingDegrees ?? ChordHeading(request.StartX, request.StartY, request.EndX, request.EndY);
            // With a heading at both ends one arc would meet the end with a kink, which leaves a gap at the joint where OMSI
            // draws each spline square to its own end; two arcs meet both headings without one.
            var arcs = request.StartHeadingDegrees.HasValue && request.EndJoinHeadingDegrees.HasValue
                ? Biarc(request.StartX, request.StartY, heading, request.EndX, request.EndY, request.EndJoinHeadingDegrees.Value)
                : new List<OmsiSplinePiece> { Arc(request.StartX, request.StartY, heading, request.EndX, request.EndY) };
            var length = 0.0;
            foreach (var arc in arcs)
            {
                length += arc.LengthMeters;
                // A point straight behind the heading would need an endless radius.
                if (arc.RadiusMeters != 0.0 && Math.Abs(arc.LengthMeters / arc.RadiusMeters) * 180.0 / Math.PI > MaximumTurnDegrees)
                {
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The point lies almost straight behind the road; set a point beside it first.",
                        request.EndX, request.EndY));
                    return plan;
                }
                if (arc.RadiusMeters != 0.0 && request.HalfWidthMeters.HasValue && Math.Abs(arc.RadiusMeters) < request.HalfWidthMeters.Value)
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "The curve radius of " + Meters(Math.Abs(arc.RadiusMeters)) + " is tighter than half the road's width (" + Meters(request.HalfWidthMeters.Value) +
                        "); its inner edge folds over.", arc.StartX, arc.StartY));
                if (Math.Abs(arc.RadiusMeters) > 0.0 && arc.LengthMeters > Math.PI * Math.Abs(arc.RadiusMeters))
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning, "The curve turns by more than half a circle; set a point in between.",
                        request.EndX, request.EndY));
            }
            var endHeading = EndHeading(arcs[arcs.Count - 1]);
            plan.LengthMeters = length;
            plan.EndHeadingDegrees = endHeading;

            // Each arc is split into equal pieces of at most the piece length; a piece never reaches over a joint of arcs.
            var starts = new List<OmsiSplinePiece>();
            foreach (var arc in arcs)
            {
                var arcCount = Math.Max(1, (int)Math.Ceiling(arc.LengthMeters / Math.Max(1.0, request.MaximumPieceLength)));
                for (var index = 0; index < arcCount; index++)
                {
                    var along = arc.LengthMeters * index / arcCount;
                    double x = arc.StartX, y = arc.StartY;
                    if (index > 0)
                        arc.PointAt(along, out x, out y, out _);
                    starts.Add(new OmsiSplinePiece
                    {
                        StartX = x, StartY = y, LengthMeters = arc.LengthMeters / arcCount, RadiusMeters = arc.RadiusMeters,
                        HeadingDegrees = arc.RadiusMeters == 0.0 ? arc.HeadingDegrees : arc.HeadingDegrees + along / arc.RadiusMeters * 180.0 / Math.PI
                    });
                }
            }
            var count = starts.Count;
            var boundaries = new double[count + 1, 2];
            var distances = new double[count + 1];
            for (var index = 0; index < count; index++)
            {
                boundaries[index, 0] = starts[index].StartX;
                boundaries[index, 1] = starts[index].StartY;
                distances[index + 1] = distances[index] + starts[index].LengthMeters;
            }
            // The last boundary is the requested end point itself, so the road ends exactly where it was asked to.
            boundaries[count, 0] = request.EndX;
            boundaries[count, 1] = request.EndY;

            double[] heights;
            double[] gradients;
            if (request.HeightMode == OmsiRoadHeightMode.Smooth)
                SmoothProfile(request, terrain, arcs, distances, plan, out heights, out gradients);
            else
            {
                heights = Heights(request, terrain, boundaries, distances, plan);
                gradients = null;
            }
            for (var index = 0; index < count; index++)
            {
                var pieceLength = starts[index].LengthMeters;
                var even = (heights[index + 1] - heights[index]) / pieceLength * 100.0;
                var startGradient = gradients?[index] ?? even;
                var endGradient = gradients?[index + 1] ?? even;
                plan.Pieces.Add(new OmsiSplinePiece
                {
                    StartX = boundaries[index, 0], StartY = boundaries[index, 1], StartHeight = heights[index], HeadingDegrees = starts[index].HeadingDegrees,
                    LengthMeters = pieceLength, RadiusMeters = starts[index].RadiusMeters, GradientStartPercent = startGradient, GradientEndPercent = endGradient
                });
                var steepest = Math.Abs(startGradient) > Math.Abs(endGradient) ? startGradient : endGradient;
                plan.MaximumGradientPercent = Math.Max(plan.MaximumGradientPercent, Math.Abs(steepest));
                if (Math.Abs(steepest) > request.GradientWarningPercent)
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Gradient of " + Percent(steepest) + " is steeper than " + Percent(request.GradientWarningPercent) + ".",
                        (boundaries[index, 0] + boundaries[index + 1, 0]) / 2.0, (boundaries[index, 1] + boundaries[index + 1, 1]) / 2.0));
            }
            plan.EndHeight = heights[count];
            if (request.StartGradientPercent.HasValue && plan.Pieces.Count > 0 &&
                Math.Abs(plan.Pieces[0].GradientStartPercent - request.StartGradientPercent.Value) > GradientChangeNote)
                plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Note,
                    "The gradient changes from " + Signed(request.StartGradientPercent.Value) + " to " + Signed(plan.Pieces[0].GradientStartPercent) +
                    " where the road continues; Smooth avoids the kink.", request.StartX, request.StartY));

            if (request.HeightMode == OmsiRoadHeightMode.Manual && terrain != null)
                CheckAgainstTerrain(plan, terrain);
            if (request.EndJoinHeadingDegrees.HasValue)
            {
                var kink = Math.Abs(Normalize(request.EndJoinHeadingDegrees.Value - endHeading));
                if (kink > JoinToleranceDegrees)
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "The road meets the joined road with a kink of " + kink.ToString("0", CultureInfo.InvariantCulture) + "°.", request.EndX, request.EndY));
            }
            foreach (var piece in plan.Pieces)
                if (!AllFinite(piece.StartX, piece.StartY, piece.StartHeight, piece.HeadingDegrees, piece.LengthMeters, piece.RadiusMeters,
                        piece.GradientStartPercent))
                {
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The road geometry has invalid values.", piece.StartX, piece.StartY));
                    break;
                }
            return plan;
        }

        /// <summary>A plan from the end of <paramref name="request"/> to its start, turned round to run from the start.</summary>
        private static OmsiRoadPlan Reversed(OmsiRoadPlan backwards, OmsiRoadRequest request)
        {
            var plan = new OmsiRoadPlan
            {
                LengthMeters = backwards.LengthMeters, EndHeadingDegrees = request.EndJoinHeadingDegrees ?? 0.0,
                MaximumGradientPercent = backwards.MaximumGradientPercent
            };
            plan.Issues.AddRange(backwards.Issues);
            var count = backwards.Pieces.Count;
            for (var index = 0; index < count; index++)
            {
                var piece = backwards.Pieces[count - 1 - index];
                // Each piece starts where the next one of the backward plan starts, and the first at the requested start.
                double startX, startY;
                if (index == 0)
                {
                    startX = request.StartX;
                    startY = request.StartY;
                }
                else
                {
                    startX = backwards.Pieces[count - index].StartX;
                    startY = backwards.Pieces[count - index].StartY;
                }
                var turn = piece.RadiusMeters == 0.0 ? 0.0 : piece.LengthMeters / piece.RadiusMeters * 180.0 / Math.PI;
                plan.Pieces.Add(new OmsiSplinePiece
                {
                    StartX = startX, StartY = startY, StartHeight = piece.EndHeight, HeadingDegrees = (piece.HeadingDegrees + turn + 180.0) % 360.0,
                    LengthMeters = piece.LengthMeters, RadiusMeters = -piece.RadiusMeters, GradientStartPercent = -piece.GradientEndPercent,
                    GradientEndPercent = -piece.GradientStartPercent
                });
            }
            plan.EndHeight = plan.Pieces.Count > 0 ? backwards.Pieces[0].StartHeight : request.EndHeight ?? 0.0;
            return plan;
        }

        /// <summary>
        /// The circular arc that leaves (<paramref name="startX"/>, <paramref name="startY"/>) with the given heading and
        /// reaches the end point, as OMSI describes splines: length, radius (0 for straight, positive turning right) and the
        /// heading at the end. The arc turns by twice the angle between the heading and the line to the end.
        /// </summary>
        public static void TangentArc(double startX, double startY, double headingDegrees, double endX, double endY,
            out double length, out double radius, out double endHeadingDegrees)
        {
            var chord = Math.Sqrt(Square(endX - startX) + Square(endY - startY));
            var deflection = Normalize(ChordHeading(startX, startY, endX, endY) - headingDegrees) * Math.PI / 180.0;
            if (Math.Abs(deflection) < 1e-6)
            {
                length = chord;
                radius = 0.0;
                endHeadingDegrees = headingDegrees;
                return;
            }
            radius = chord / (2.0 * Math.Sin(deflection));
            length = radius * 2.0 * deflection;
            endHeadingDegrees = headingDegrees + 2.0 * deflection * 180.0 / Math.PI;
        }

        /// <summary>The tangent arc from a point with a heading to an end point, as a piece without heights.</summary>
        private static OmsiSplinePiece Arc(double startX, double startY, double headingDegrees, double endX, double endY)
        {
            TangentArc(startX, startY, headingDegrees, endX, endY, out var length, out var radius, out _);
            return new OmsiSplinePiece { StartX = startX, StartY = startY, HeadingDegrees = headingDegrees, LengthMeters = length, RadiusMeters = radius };
        }

        private static double EndHeading(OmsiSplinePiece piece)
        {
            return piece.RadiusMeters == 0.0 ? piece.HeadingDegrees : piece.HeadingDegrees + piece.LengthMeters / piece.RadiusMeters * 180.0 / Math.PI;
        }

        /// <summary>
        /// Two arcs from a start with a heading to an end reached with another heading, meeting without a kink: the biarc
        /// whose arcs have tangent legs of equal length (Ryan Juckett, "Biarc Interpolation"). When one arc already meets
        /// both headings, both arcs lie on its circle; a straight line in line with both headings stays one piece.
        /// </summary>
        public static List<OmsiSplinePiece> Biarc(double startX, double startY, double startHeadingDegrees, double endX, double endY, double endHeadingDegrees)
        {
            var t1x = Math.Sin(startHeadingDegrees * Math.PI / 180.0);
            var t1y = Math.Cos(startHeadingDegrees * Math.PI / 180.0);
            var t2x = Math.Sin(endHeadingDegrees * Math.PI / 180.0);
            var t2y = Math.Cos(endHeadingDegrees * Math.PI / 180.0);
            var vx = endX - startX;
            var vy = endY - startY;
            var vv = vx * vx + vy * vy;
            var denominator = 2.0 * (1.0 - (t1x * t2x + t1y * t2y));
            double jointX, jointY;
            if (denominator < 1e-9)
            {
                // Both headings alike: in line with them a straight piece; across them two half circles.
                var along = vx * t2x + vy * t2y;
                if (Math.Abs(vx * t1y - vy * t1x) < 1e-6 && along > 0.0)
                    return new List<OmsiSplinePiece> { Arc(startX, startY, startHeadingDegrees, endX, endY) };
                if (Math.Abs(along) < 1e-9)
                {
                    jointX = startX + vx / 2.0;
                    jointY = startY + vy / 2.0;
                }
                else
                {
                    var leg = vv / (4.0 * along);
                    jointX = (startX + endX + leg * (t1x - t2x)) / 2.0;
                    jointY = (startY + endY + leg * (t1y - t2y)) / 2.0;
                }
            }
            else
            {
                var vt = vx * (t1x + t2x) + vy * (t1y + t2y);
                var leg = (-vt + Math.Sqrt(vt * vt + denominator * vv)) / denominator;
                jointX = (startX + endX + leg * (t1x - t2x)) / 2.0;
                jointY = (startY + endY + leg * (t1y - t2y)) / 2.0;
            }
            var first = Arc(startX, startY, startHeadingDegrees, jointX, jointY);
            var second = Arc(jointX, jointY, EndHeading(first), endX, endY);
            var result = new List<OmsiSplinePiece>();
            if (first.LengthMeters > 1e-6)
                result.Add(first);
            if (second.LengthMeters > 1e-6)
                result.Add(second);
            if (result.Count == 0)
                result.Add(Arc(startX, startY, startHeadingDegrees, endX, endY));
            return result;
        }

        /// <summary>The heading of the line from start to end; 0 points north and the angle grows clockwise.</summary>
        public static double ChordHeading(double startX, double startY, double endX, double endY)
        {
            return Math.Atan2(endX - startX, endY - startY) * 180.0 / Math.PI;
        }

        /// <summary>An angle in degrees brought into (-180, 180].</summary>
        public static double Normalize(double degrees)
        {
            var value = degrees % 360.0;
            if (value > 180.0)
                value -= 360.0;
            else if (value <= -180.0)
                value += 360.0;
            return value;
        }

        private static double[] Heights(OmsiRoadRequest request, IOmsiTerrainHeights terrain, double[,] boundaries, double[] distances, OmsiRoadPlan plan)
        {
            var count = distances.Length - 1;
            var heights = new double[count + 1];
            var known = new bool[count + 1];
            var start = request.StartHeight ?? TerrainHeight(terrain, boundaries[0, 0], boundaries[0, 1]);
            if (start.HasValue)
            {
                heights[0] = start.Value;
                known[0] = true;
            }
            else
                plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The start lies outside the terrain and has no height.",
                    boundaries[0, 0], boundaries[0, 1]));

            if (request.HeightMode == OmsiRoadHeightMode.Manual)
            {
                var end = request.EndHeight ?? heights[0];
                for (var index = 1; index <= count; index++)
                {
                    heights[index] = heights[0] + (end - heights[0]) * distances[index] / distances[count];
                    known[index] = known[0];
                }
                return heights;
            }

            var outside = false;
            for (var index = 1; index <= count; index++)
            {
                var height = index == count && request.EndHeight.HasValue
                    ? request.EndHeight
                    : TerrainHeight(terrain, boundaries[index, 0], boundaries[index, 1]);
                if (height.HasValue)
                {
                    heights[index] = height.Value;
                    known[index] = true;
                }
                else if (!outside)
                {
                    outside = true;
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The road leaves the terrain; there is no height to follow.",
                        boundaries[index, 0], boundaries[index, 1]));
                }
            }
            // Boundaries without terrain take the height between their known neighbours, only to draw the preview.
            for (var index = 0; index <= count; index++)
            {
                if (known[index])
                    continue;
                int before = index - 1, after = index + 1;
                while (before >= 0 && !known[before])
                    before--;
                while (after <= count && !known[after])
                    after++;
                heights[index] = before >= 0 && after <= count
                    ? heights[before] + (heights[after] - heights[before]) * (index - before) / (after - before)
                    : before >= 0 ? heights[before] : after <= count ? heights[after] : 0.0;
            }
            return heights;
        }

        /// <summary>
        /// Heights and gradients of a smooth road at every piece boundary. The terrain under the centre line is averaged
        /// over <see cref="OmsiRoadRequest.SmoothingMeters"/>; near a fixed start or end the curve is bent towards that
        /// height, and at a joined start towards its gradient, with a smooth step so no new kink appears. The gradient at
        /// each boundary is the slope of that curve, and the heights follow from the gradients piece by piece as OMSI
        /// computes them, so height and gradient are continuous. A fixed end height is met by spreading the small
        /// remainder evenly over the gradients after the start.
        /// </summary>
        private static void SmoothProfile(OmsiRoadRequest request, IOmsiTerrainHeights terrain, List<OmsiSplinePiece> arcs, double[] distances,
            OmsiRoadPlan plan, out double[] heights, out double[] gradients)
        {
            var count = distances.Length - 1;
            var length = distances[count];
            // Terrain along the road; samples without terrain take their neighbours' heights and are reported once.
            var samples = Math.Max(2, (int)Math.Ceiling(length / SmoothSampleStep));
            var ground = new double[samples + 1];
            var known = new bool[samples + 1];
            var anyKnown = false;
            var reported = false;
            for (var index = 0; index <= samples; index++)
            {
                CoursePoint(arcs, length * index / samples, out var x, out var y);
                if (terrain != null && terrain.TryGetHeight(x, y, out var height))
                {
                    ground[index] = height;
                    known[index] = anyKnown = true;
                }
                else if (!reported && !(index == 0 && request.StartHeight.HasValue) && !(index == samples && request.EndHeight.HasValue))
                {
                    reported = true;
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The road leaves the terrain; there is no height to smooth.", x, y));
                }
            }
            if (!anyKnown)
            {
                var fallback = request.StartHeight ?? request.EndHeight ?? 0.0;
                if (!request.StartHeight.HasValue && !request.EndHeight.HasValue && !reported)
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The start lies outside the terrain and has no height.", request.StartX, request.StartY));
                for (var index = 0; index <= samples; index++)
                    ground[index] = fallback;
            }
            else
                FillGaps(ground, known);

            // Moving average with a triangular weight over the smoothing length; the ends repeat their edge value.
            var radius = Math.Max(1, (int)Math.Round(request.SmoothingMeters / 2.0 / (length / samples)));
            var smooth = new double[samples + 1];
            for (var index = 0; index <= samples; index++)
            {
                double sum = 0.0, weights = 0.0;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    var weight = radius + 1 - Math.Abs(offset);
                    sum += ground[Math.Max(0, Math.Min(samples, index + offset))] * weight;
                    weights += weight;
                }
                smooth[index] = sum / weights;
            }

            Func<double, double> profile = distance =>
            {
                var position = distance / length * samples;
                var lower = Math.Max(0, Math.Min(samples - 1, (int)Math.Floor(position)));
                var fraction = position - lower;
                return smooth[lower] + (smooth[lower + 1] - smooth[lower]) * fraction;
            };
            var blend = Math.Min(request.SmoothingMeters, length);
            var startSlope = (profile(Math.Min(length, 1.0)) - profile(0.0)) / Math.Min(length, 1.0);
            var startHeight = request.StartHeight ?? profile(0.0);
            var startGradient = request.StartGradientPercent.HasValue ? request.StartGradientPercent.Value / 100.0 : startSlope;
            var endHeight = request.EndHeight ?? profile(length);
            Func<double, double> target = distance =>
            {
                // Bent towards the start height and gradient near the start and towards the end height near the end.
                var fromStart = 1.0 - SmoothStep(distance / blend);
                var fromEnd = SmoothStep((distance - (length - blend)) / blend);
                return profile(distance) + (startHeight - profile(0.0) + (startGradient - startSlope) * distance) * fromStart +
                       (endHeight - profile(length) - (startHeight - profile(0.0) + (startGradient - startSlope) * length) * (1.0 - SmoothStep(length / blend))) * fromEnd;
            };

            heights = new double[count + 1];
            gradients = new double[count + 1];
            for (var index = 0; index <= count; index++)
            {
                var distance = distances[index];
                // The slope over one piece length, so short bumps between boundaries do not tilt a single boundary.
                var slopeStep = (index < count ? distances[index + 1] - distance : distance - distances[index - 1]) / 2.0;
                var before = Math.Max(0.0, distance - slopeStep);
                var after = Math.Min(length, distance + slopeStep);
                gradients[index] = (target(after) - target(before)) / (after - before) * 100.0;
            }
            gradients[0] = startGradient * 100.0;
            heights[0] = startHeight;
            for (var index = 0; index < count; index++)
                heights[index + 1] = heights[index] + (distances[index + 1] - distances[index]) * (gradients[index] + gradients[index + 1]) / 200.0;
            if (request.EndHeight.HasValue)
            {
                // Every gradient after the start changes by the same amount; piece 0 gains it once, the others twice.
                var change = (endHeight - heights[count]) * 200.0 / (2.0 * length - distances[1]);
                for (var index = 1; index <= count; index++)
                    gradients[index] += change;
                for (var index = 0; index < count; index++)
                    heights[index + 1] = heights[index] + (distances[index + 1] - distances[index]) * (gradients[index] + gradients[index + 1]) / 200.0;
                heights[count] = endHeight;
            }
        }

        /// <summary>The position <paramref name="distance"/> metres along consecutive arcs.</summary>
        private static void CoursePoint(List<OmsiSplinePiece> arcs, double distance, out double x, out double y)
        {
            for (var index = 0; index < arcs.Count; index++)
            {
                var arc = arcs[index];
                if (distance <= arc.LengthMeters || index == arcs.Count - 1)
                {
                    arc.PointAt(Math.Max(0.0, Math.Min(distance, arc.LengthMeters)), out x, out y, out _);
                    return;
                }
                distance -= arc.LengthMeters;
            }
            x = arcs[0].StartX;
            y = arcs[0].StartY;
        }

        /// <summary>0 before 0, 1 after 1 and an S-curve with flat ends in between.</summary>
        private static double SmoothStep(double value)
        {
            var t = Math.Max(0.0, Math.Min(1.0, value));
            return t * t * (3.0 - 2.0 * t);
        }

        private static void FillGaps(double[] values, bool[] known)
        {
            for (var index = 0; index < values.Length; index++)
            {
                if (known[index])
                    continue;
                int before = index - 1, after = index + 1;
                while (before >= 0 && !known[before])
                    before--;
                while (after < values.Length && !known[after])
                    after++;
                values[index] = before >= 0 && after < values.Length
                    ? values[before] + (values[after] - values[before]) * (index - before) / (after - before)
                    : before >= 0 ? values[before] : values[after];
            }
        }

        private static string Signed(double value)
        {
            return (value >= 0 ? "+" : string.Empty) + value.ToString("0.0", CultureInfo.InvariantCulture) + " %";
        }

        /// <summary>Manual heights can leave the terrain; report where the road would run underground or high above it.</summary>
        private static void CheckAgainstTerrain(OmsiRoadPlan plan, IOmsiTerrainHeights terrain)
        {
            double lowest = 0.0, highest = 0.0, lowX = 0.0, lowY = 0.0, highX = 0.0, highY = 0.0;
            foreach (var piece in plan.Pieces)
            {
                var steps = Math.Max(1, (int)Math.Ceiling(piece.LengthMeters / TerrainCheckStep));
                for (var step = 0; step <= steps; step++)
                {
                    piece.PointAt(piece.LengthMeters * step / steps, out var x, out var y, out var height);
                    if (!terrain.TryGetHeight(x, y, out var ground))
                        continue;
                    var above = height - ground;
                    if (above < lowest)
                    {
                        lowest = above;
                        lowX = x;
                        lowY = y;
                    }
                    if (above > highest)
                    {
                        highest = above;
                        highX = x;
                        highY = y;
                    }
                }
            }
            if (lowest < -BelowTerrainTolerance)
                plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                    "The road runs up to " + Meters(-lowest) + " below the terrain.", lowX, lowY));
            if (highest > AboveTerrainNote)
                plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Note,
                    "The road runs up to " + Meters(highest) + " above the terrain.", highX, highY));
        }

        private static double? TerrainHeight(IOmsiTerrainHeights terrain, double x, double y)
        {
            return terrain != null && terrain.TryGetHeight(x, y, out var height) ? height : (double?)null;
        }

        private static bool AllFinite(params double[] values)
        {
            foreach (var value in values)
                if (double.IsNaN(value) || double.IsInfinity(value))
                    return false;
            return true;
        }

        private static double Square(double value)
        {
            return value * value;
        }

        private static string Percent(double value)
        {
            return Math.Abs(value).ToString("0.0", CultureInfo.InvariantCulture) + " %";
        }

        private static string Meters(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        }
    }
}
