using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Splines joined by neighbour IDs around one spline, measured along that spline's direction: distance 0 is its start.
    /// Splines joined against the direction, end to end or start to start, count as reversed.
    /// </summary>
    public sealed class OmsiSplineChain
    {
        public sealed class Segment
        {
            public OmsiTileSummary Tile { get; internal set; }
            public OmsiSplineRecord Spline { get; internal set; }

            /// <summary>True when the spline runs against the chain.</summary>
            public bool Reversed { get; internal set; }

            /// <summary>Where the segment begins along the chain, in metres.</summary>
            public double Start { get; internal set; }

            public double End => Start + Spline.LengthMeters;
        }

        private readonly double tileSize;

        private OmsiSplineChain(double tileSize)
        {
            this.tileSize = tileSize;
        }

        public List<Segment> Segments { get; } = new List<Segment>();

        public double Start => Segments[0].Start;
        public double End => Segments[Segments.Count - 1].End;

        /// <summary>The chain through a spline, followed in both directions for up to <paramref name="reachMeters"/>.</summary>
        public static OmsiSplineChain Around(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineRecord spline, double reachMeters)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            var size = summary.TileSizeMeters;
            var byId = new Dictionary<int, KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
            foreach (var candidate in summary.Tiles)
                foreach (var record in candidate.Splines)
                    byId[record.Id] = new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(candidate, record);

            var chain = new OmsiSplineChain(size);
            chain.Segments.Add(new Segment { Tile = tile, Spline = spline, Start = 0.0 });
            var visited = new HashSet<int> { spline.Id };
            // Ahead from the end, then back from the start.
            Follow(chain, byId, visited, tile, spline, true, reachMeters, true);
            Follow(chain, byId, visited, tile, spline, false, reachMeters, false);
            return chain;
        }

        private static void Follow(OmsiSplineChain chain, Dictionary<int, KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> byId, HashSet<int> visited,
            OmsiTileSummary tile, OmsiSplineRecord spline, bool fromEnd, double reach, bool ahead)
        {
            var size = chain.tileSize;
            var travelled = ahead ? spline.LengthMeters : 0.0;
            while (true)
            {
                var id = fromEnd ? spline.NextId : spline.PreviousId;
                if (id == 0 || visited.Contains(id) || !byId.TryGetValue(id, out var next) || Math.Abs(travelled) > reach + spline.LengthMeters)
                    return;
                var joint = new OmsiSplineEnd(tile, spline, fromEnd).Position(size);
                var startsThere = Distance(new OmsiSplineEnd(next.Key, next.Value, false).Position(size), joint) <=
                                  Distance(new OmsiSplineEnd(next.Key, next.Value, true).Position(size), joint);
                visited.Add(id);
                var length = next.Value.LengthMeters;
                if (ahead)
                {
                    chain.Segments.Add(new Segment { Tile = next.Key, Spline = next.Value, Reversed = !startsThere, Start = travelled });
                    travelled += length;
                }
                else
                {
                    travelled -= length;
                    chain.Segments.Insert(0, new Segment { Tile = next.Key, Spline = next.Value, Reversed = startsThere, Start = travelled });
                }
                tile = next.Key;
                spline = next.Value;
                // Go on from the end that does not touch the joint.
                fromEnd = startsThere;
                if (Math.Abs(travelled) > reach)
                    return;
            }
        }

        /// <summary>The segment holding a distance along the chain; past the ends, the first or last one.</summary>
        public Segment At(double distance)
        {
            foreach (var segment in Segments)
                if (distance <= segment.End)
                    return segment;
            return Segments[Segments.Count - 1];
        }

        /// <summary>The distance along a segment's spline record for a distance along the chain.</summary>
        public static double LocalDistance(Segment segment, double distance)
        {
            var along = Math.Max(0.0, Math.Min(segment.Spline.LengthMeters, distance - segment.Start));
            return segment.Reversed ? segment.Spline.LengthMeters - along : along;
        }

        /// <summary>World position, heading along the chain and height at a distance along it.</summary>
        public void FrameAt(double distance, out OmsiMapPoint world, out double headingDegrees, out double height)
        {
            var segment = At(distance);
            var local = LocalDistance(segment, distance);
            OmsiMapGeometry.GetLocalFrameAt(segment.Spline, local, out var position, out var headingRadians);
            world = OmsiMapGeometry.TileLocalToWorld(segment.Tile.X, segment.Tile.Y, position.X, position.Y, tileSize);
            headingDegrees = headingRadians * 180.0 / Math.PI + (segment.Reversed ? 180.0 : 0.0);
            height = segment.Spline.HeightAt(local);
        }

        /// <summary>The distance along the chain nearest to a world position.</summary>
        public double Project(double x, double y)
        {
            var best = Start;
            var bestDistance = double.MaxValue;
            for (var distance = Start; distance <= End; distance += 0.25)
            {
                FrameAt(distance, out var point, out _, out _);
                var squared = (point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y);
                if (squared < bestDistance)
                {
                    bestDistance = squared;
                    best = distance;
                }
            }
            return best;
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }

    /// <summary>A point on a spline: which spline, how far along it and how far from the world position asked about.</summary>
    public sealed class OmsiSplinePoint
    {
        public OmsiTileSummary Tile { get; internal set; }
        public OmsiSplineRecord Spline { get; internal set; }
        public double Distance { get; internal set; }
        public double Offset { get; internal set; }

        /// <summary>The spline whose centre line passes nearest to a world position, within <paramref name="maximumOffset"/>; or null.</summary>
        public static OmsiSplinePoint Nearest(OmsiMapSummary summary, double x, double y, double maximumOffset)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var size = summary.TileSizeMeters;
            var tileX = (int)Math.Floor(x / size);
            var tileY = (int)Math.Floor(y / size);
            OmsiSplinePoint best = null;
            foreach (var tile in summary.Tiles)
            {
                if (Math.Abs(tile.X - tileX) > 1 || Math.Abs(tile.Y - tileY) > 1)
                    continue;
                foreach (var spline in tile.Splines)
                {
                    var origin = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, spline.LocalX, spline.LocalY, size);
                    var reach = spline.LengthMeters + maximumOffset;
                    if (Math.Abs(origin.X - x) > reach || Math.Abs(origin.Y - y) > reach)
                        continue;
                    // Every metre, then every 5 cm around the nearest metre.
                    var coarse = Closest(tile, spline, x, y, 0.0, spline.LengthMeters, 1.0, size, out var offset);
                    var fine = Closest(tile, spline, x, y, Math.Max(0.0, coarse - 1.0), Math.Min(spline.LengthMeters, coarse + 1.0), 0.05, size, out offset);
                    if (offset <= maximumOffset && (best == null || offset < best.Offset))
                        best = new OmsiSplinePoint { Tile = tile, Spline = spline, Distance = fine, Offset = offset };
                }
            }
            return best;
        }

        private static double Closest(OmsiTileSummary tile, OmsiSplineRecord spline, double x, double y, double from, double to, double step, double size,
            out double offset)
        {
            var best = from;
            offset = double.MaxValue;
            for (var distance = from; distance <= to + 1e-9; distance += step)
            {
                OmsiMapGeometry.GetLocalFrameAt(spline, Math.Min(distance, to), out var local, out _);
                var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, local.X, local.Y, size);
                var gap = Math.Sqrt((world.X - x) * (world.X - x) + (world.Y - y) * (world.Y - y));
                if (gap < offset)
                {
                    offset = gap;
                    best = Math.Min(distance, to);
                }
            }
            return best;
        }
    }

    /// <summary>
    /// How a junction object would sit in a road: its place, heading and height with two opposite connection points on the
    /// road's centre line, and where the road is cut on either side. <see cref="Problem"/> says why it cannot go there.
    /// </summary>
    public sealed class OmsiJunctionInsertionPlan
    {
        public string AssetPath { get; internal set; }
        public OmsiTileSummary Tile { get; internal set; }
        public double LocalX { get; internal set; }
        public double LocalY { get; internal set; }
        public double Height { get; internal set; }
        public double HeadingDegrees { get; internal set; }

        /// <summary>The object's origin in world metres.</summary>
        public OmsiMapPoint Position { get; internal set; }

        public OmsiSplineChain Chain { get; internal set; }

        /// <summary>Where the road is cut before and after the junction, in metres along the chain.</summary>
        public double CutStart { get; internal set; }

        public double CutEnd { get; internal set; }

        /// <summary>The connection points that face the road before and after the junction.</summary>
        public OmsiJunctionPort PortAtStart { get; internal set; }

        public OmsiJunctionPort PortAtEnd { get; internal set; }

        /// <summary>The profile of the road, for the short pieces that lead to the connection points.</summary>
        public string ProfilePath { get; internal set; }

        public bool AlignsTerrain { get; internal set; }

        /// <summary>Why the junction cannot be inserted here, or null.</summary>
        public string Problem { get; internal set; }
    }

    /// <summary>
    /// Plans inserting a junction object into a road: two opposite connection points are laid on the road's centre line at
    /// the chosen point, the other arms point to the side the pointer is on, and the road is cut a little beyond each
    /// point so short pieces can lead to the points without a kink, also on curves and slopes.
    /// </summary>
    public static class OmsiJunctionInsertion
    {
        /// <summary>The road is cut this far beyond each connection point; the gap is closed by a short new piece.</summary>
        public const double ConnectorMeters = 5.0;

        /// <summary>Opposite connection points face away from each other within this many degrees.</summary>
        private const double OppositeToleranceDegrees = 15.0;

        /// <returns>The plan, or null when the object has fewer than two connection points.</returns>
        public static OmsiJunctionInsertionPlan Plan(OmsiMapSummary summary, OmsiJunctionCatalog catalog, string assetPath, OmsiTileSummary tile,
            OmsiSplineRecord spline, double distance, double pointerX, double pointerY)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            var helpers = catalog.Helpers(assetPath);
            if (helpers.Count < 2)
                return null;
            var plan = new OmsiJunctionInsertionPlan
            {
                AssetPath = assetPath, ProfilePath = spline.AssetPath, AlignsTerrain = spline.AlignsTerrain
            };

            // Two connection points facing away from each other, the profile of the road preferred, then the farthest apart.
            int pairFirst = -1, pairSecond = -1;
            var bestScore = double.MinValue;
            for (var first = 0; first < helpers.Count; first++)
                for (var second = 0; second < helpers.Count; second++)
                {
                    if (first == second)
                        continue;
                    var a = helpers[first];
                    var b = helpers[second];
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var span = Math.Sqrt(dx * dx + dy * dy);
                    if (span < 1.0)
                        continue;
                    var axis = Math.Atan2(dx, dy) * 180.0 / Math.PI;
                    if (Math.Abs(OmsiRoadPlanner.Normalize(a.HeadingDegrees - b.HeadingDegrees - 180.0)) > OppositeToleranceDegrees ||
                        Math.Abs(OmsiRoadPlanner.Normalize(b.HeadingDegrees - axis)) > OppositeToleranceDegrees)
                        continue;
                    var score = span + (SameProfile(a.SplinePath, spline.AssetPath) ? 100.0 : 0.0) + (SameProfile(b.SplinePath, spline.AssetPath) ? 100.0 : 0.0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        pairFirst = first;
                        pairSecond = second;
                    }
                }
            if (pairFirst < 0)
            {
                plan.Problem = "The object has no two connection points facing away from each other to lay along a road.";
                return plan;
            }

            plan.Chain = OmsiSplineChain.Around(summary, tile, spline, 200.0);
            plan.Chain.FrameAt(distance, out var point, out var roadHeading, out var roadHeight);

            // Either point may face ahead; the other arms then point to the side the pointer is on.
            var bestSide = double.MinValue;
            int start = pairFirst, end = pairSecond;
            foreach (var option in new[] { new[] { pairFirst, pairSecond }, new[] { pairSecond, pairFirst } })
            {
                var a = helpers[option[0]];
                var b = helpers[option[1]];
                var heading = roadHeading - Math.Atan2(b.X - a.X, b.Y - a.Y) * 180.0 / Math.PI;
                var side = 0.0;
                for (var index = 0; index < helpers.Count; index++)
                {
                    if (index == option[0] || index == option[1])
                        continue;
                    var arm = (heading + helpers[index].HeadingDegrees) * Math.PI / 180.0;
                    side += Math.Sin(arm) * (pointerX - point.X) + Math.Cos(arm) * (pointerY - point.Y);
                }
                if (side > bestSide)
                {
                    bestSide = side;
                    start = option[0];
                    end = option[1];
                }
            }
            var startHelper = helpers[start];
            var endHelper = helpers[end];
            var junctionHeading = Normalize360(roadHeading - Math.Atan2(endHelper.X - startHelper.X, endHelper.Y - startHelper.Y) * 180.0 / Math.PI);

            // The middle between the two points lies on the road.
            var middleX = (startHelper.X + endHelper.X) / 2.0;
            var middleY = (startHelper.Y + endHelper.Y) / 2.0;
            var radians = junctionHeading * Math.PI / 180.0;
            var originX = point.X - (middleX * Math.Cos(radians) + middleY * Math.Sin(radians));
            var originY = point.Y - (-middleX * Math.Sin(radians) + middleY * Math.Cos(radians));
            var size = summary.TileSizeMeters;
            var target = summary.Tiles.Find(candidate => candidate.IsAvailable && candidate.X == (int)Math.Floor(originX / size) &&
                                                         candidate.Y == (int)Math.Floor(originY / size)) ?? tile;
            plan.Tile = target;
            plan.Position = new OmsiMapPoint(originX, originY);
            plan.LocalX = originX - target.X * size;
            plan.LocalY = originY - target.Y * size;
            plan.HeadingDegrees = junctionHeading;
            plan.Height = roadHeight - (startHelper.Height + endHelper.Height) / 2.0;

            var item = new OmsiObjectRecord
            {
                AssetPath = assetPath, LocalX = plan.LocalX, LocalY = plan.LocalY, Height = plan.Height, HeadingDegrees = plan.HeadingDegrees
            };
            var ports = catalog.Ports(target, item, size);
            plan.PortAtStart = ports[start];
            plan.PortAtEnd = ports[end];

            var half = Math.Sqrt((endHelper.X - startHelper.X) * (endHelper.X - startHelper.X) + (endHelper.Y - startHelper.Y) * (endHelper.Y - startHelper.Y)) / 2.0;
            plan.CutStart = distance - half - ConnectorMeters;
            plan.CutEnd = distance + half + ConnectorMeters;
            if (plan.CutStart < plan.Chain.Start || plan.CutEnd > plan.Chain.End)
                plan.Problem = "The road is too short here: the junction needs " + OmsiNumberFormat.Format(ConnectorMeters) +
                               " m of road beyond each connection point.";
            return plan;
        }

        private static bool SameProfile(string left, string right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static double Normalize360(double degrees)
        {
            return (degrees % 360.0 + 360.0) % 360.0;
        }
    }
}
