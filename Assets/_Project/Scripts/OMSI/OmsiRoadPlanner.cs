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
        Manual
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
                RadiusMeters = RadiusMeters, EndHeight = EndHeight
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

        /// <summary>Headings closer than this meet without a kink.</summary>
        private const double JoinToleranceDegrees = 2.0;

        /// <param name="terrain">The terrain heights, or null when the map has none.</param>
        public static OmsiRoadPlan Plan(OmsiRoadRequest request, IOmsiTerrainHeights terrain)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
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
            TangentArc(request.StartX, request.StartY, heading, request.EndX, request.EndY, out var length, out var radius, out var endHeading);
            plan.LengthMeters = length;
            plan.EndHeadingDegrees = endHeading;
            if (Math.Abs(radius) > 0.0 && length > Math.PI * Math.Abs(radius))
                plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning, "The curve turns by more than half a circle; set a point in between.",
                    request.EndX, request.EndY));

            var count = Math.Max(1, (int)Math.Ceiling(length / Math.Max(1.0, request.MaximumPieceLength)));
            var full = new OmsiSplinePiece { StartX = request.StartX, StartY = request.StartY, HeadingDegrees = heading, LengthMeters = length, RadiusMeters = radius };
            var boundaries = new double[count + 1, 2];
            var headings = new double[count + 1];
            for (var index = 0; index <= count; index++)
            {
                full.PointAt(length * index / count, out var x, out var y, out _);
                boundaries[index, 0] = x;
                boundaries[index, 1] = y;
                headings[index] = radius == 0.0 ? heading : heading + length * index / count / radius * 180.0 / Math.PI;
            }
            // The last boundary is the requested end point itself, so the road ends exactly where it was asked to.
            boundaries[count, 0] = request.EndX;
            boundaries[count, 1] = request.EndY;

            var heights = Heights(request, terrain, boundaries, count, plan);
            for (var index = 0; index < count; index++)
            {
                var pieceLength = length / count;
                var gradient = (heights[index + 1] - heights[index]) / pieceLength * 100.0;
                plan.Pieces.Add(new OmsiSplinePiece
                {
                    StartX = boundaries[index, 0], StartY = boundaries[index, 1], StartHeight = heights[index], HeadingDegrees = headings[index],
                    LengthMeters = pieceLength, RadiusMeters = radius, GradientStartPercent = gradient, GradientEndPercent = gradient
                });
                plan.MaximumGradientPercent = Math.Max(plan.MaximumGradientPercent, Math.Abs(gradient));
                if (Math.Abs(gradient) > request.GradientWarningPercent)
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Gradient of " + Percent(gradient) + " is steeper than " + Percent(request.GradientWarningPercent) + ".",
                        (boundaries[index, 0] + boundaries[index + 1, 0]) / 2.0, (boundaries[index, 1] + boundaries[index + 1, 1]) / 2.0));
            }
            plan.EndHeight = heights[count];

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

        private static double[] Heights(OmsiRoadRequest request, IOmsiTerrainHeights terrain, double[,] boundaries, int count, OmsiRoadPlan plan)
        {
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
                    heights[index] = heights[0] + (end - heights[0]) * index / count;
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
