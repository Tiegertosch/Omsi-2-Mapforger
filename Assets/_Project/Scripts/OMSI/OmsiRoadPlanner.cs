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

        /// <summary>A change of gradient larger than this, in percentage points, at a joined start is noted.</summary>
        private const double GradientChangeNote = 2.0;

        /// <summary>Smooth roads sample the terrain this often.</summary>
        private const double SmoothSampleStep = 2.5;

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

            var pieceLength = length / count;
            double[] heights;
            double[] gradients;
            if (request.HeightMode == OmsiRoadHeightMode.Smooth)
                SmoothProfile(request, terrain, full, length, count, plan, out heights, out gradients);
            else
            {
                heights = Heights(request, terrain, boundaries, count, plan);
                gradients = null;
            }
            for (var index = 0; index < count; index++)
            {
                var even = (heights[index + 1] - heights[index]) / pieceLength * 100.0;
                var startGradient = gradients?[index] ?? even;
                var endGradient = gradients?[index + 1] ?? even;
                plan.Pieces.Add(new OmsiSplinePiece
                {
                    StartX = boundaries[index, 0], StartY = boundaries[index, 1], StartHeight = heights[index], HeadingDegrees = headings[index],
                    LengthMeters = pieceLength, RadiusMeters = radius, GradientStartPercent = startGradient, GradientEndPercent = endGradient
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

        /// <summary>
        /// Heights and gradients of a smooth road at every piece boundary. The terrain under the centre line is averaged
        /// over <see cref="OmsiRoadRequest.SmoothingMeters"/>; near a fixed start or end the curve is bent towards that
        /// height, and at a joined start towards its gradient, with a smooth step so no new kink appears. The gradient at
        /// each boundary is the slope of that curve, and the heights follow from the gradients piece by piece as OMSI
        /// computes them, so height and gradient are continuous. A fixed end height is met by spreading the small
        /// remainder evenly over the gradients after the start.
        /// </summary>
        private static void SmoothProfile(OmsiRoadRequest request, IOmsiTerrainHeights terrain, OmsiSplinePiece full, double length, int count,
            OmsiRoadPlan plan, out double[] heights, out double[] gradients)
        {
            // Terrain along the road; samples without terrain take their neighbours' heights and are reported once.
            var samples = Math.Max(2, (int)Math.Ceiling(length / SmoothSampleStep));
            var ground = new double[samples + 1];
            var known = new bool[samples + 1];
            var anyKnown = false;
            var reported = false;
            for (var index = 0; index <= samples; index++)
            {
                full.PointAt(length * index / samples, out var x, out var y, out _);
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
            var pieceLength = length / count;
            // The slope over one piece length, so short bumps between boundaries do not tilt a single boundary.
            var slopeStep = pieceLength / 2.0;
            for (var index = 0; index <= count; index++)
            {
                var distance = pieceLength * index;
                var before = Math.Max(0.0, distance - slopeStep);
                var after = Math.Min(length, distance + slopeStep);
                gradients[index] = (target(after) - target(before)) / (after - before) * 100.0;
            }
            gradients[0] = startGradient * 100.0;
            heights[0] = startHeight;
            for (var index = 0; index < count; index++)
                heights[index + 1] = heights[index] + pieceLength * (gradients[index] + gradients[index + 1]) / 200.0;
            if (request.EndHeight.HasValue)
            {
                // Every gradient after the start changes by the same amount; piece 0 gains it once, the others twice.
                var change = (endHeight - heights[count]) * 200.0 / (pieceLength * (2 * count - 1));
                for (var index = 1; index <= count; index++)
                    gradients[index] += change;
                for (var index = 0; index < count; index++)
                    heights[index + 1] = heights[index] + pieceLength * (gradients[index] + gradients[index + 1]) / 200.0;
                heights[count] = endHeight;
            }
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
