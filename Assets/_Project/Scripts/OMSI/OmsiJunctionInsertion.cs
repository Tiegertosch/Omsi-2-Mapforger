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

        /// <param name="throughPair">The two connection points to lay on the road, by their place among the object's; null chooses.</param>
        /// <returns>The plan, or null when the object has fewer than two connection points.</returns>
        public static OmsiJunctionInsertionPlan Plan(OmsiMapSummary summary, OmsiJunctionCatalog catalog, string assetPath, OmsiTileSummary tile,
            OmsiSplineRecord spline, double distance, double pointerX, double pointerY, int[] throughPair = null)
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
            if (throughPair != null)
            {
                pairFirst = throughPair[0];
                pairSecond = throughPair[1];
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

    /// <summary>Where a road being planned crosses an existing road: the point on that road and the new road's way there.</summary>
    public sealed class OmsiRoadCrossing
    {
        /// <summary>The point on the existing road.</summary>
        public OmsiSplinePoint Road { get; internal set; }

        /// <summary>How far along the planned road the crossing lies, in metres.</summary>
        public double Along { get; internal set; }

        /// <summary>The planned road's heading at the crossing.</summary>
        public double HeadingDegrees { get; internal set; }
    }

    /// <summary>
    /// Finds where roads being built meet existing roads, so a junction can be made there (stage 4d, ADR 028), and the arms
    /// such a junction needs. Only roads count: profiles with a carriageway at least 3 m wide and a lane for vehicles, not
    /// rails, wires, fences or footpaths.
    /// </summary>
    public static class OmsiRoadJunctions
    {
        /// <summary>
        /// True for a profile with a carriageway at least 3 m wide and a lane for vehicles; not for rails and tram tracks,
        /// also in a street, since a junction made here would have no rails.
        /// </summary>
        public static bool IsRoad(OmsiSplineProfile profile)
        {
            var category = OmsiSplineCategories.Of(profile);
            if (category == OmsiSplineCategory.Rail || category == OmsiSplineCategory.Path || category == OmsiSplineCategory.Other)
                return false;
            var sections = OmsiProfileSections.Of(profile);
            return sections.RoadTexture >= 0 && sections.RoadLeft + sections.RoadRight >= 3.0;
        }

        /// <summary>The road whose carriageway lies under a world position, or null.</summary>
        public static OmsiSplinePoint RoadAt(OmsiMapSummary summary, OmsiSplineProfileCatalog profiles, double x, double y)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));
            var point = OmsiSplinePoint.Nearest(summary, x, y, 10.0);
            if (point == null)
                return null;
            var profile = profiles.Get(point.Spline.AssetPath);
            if (!IsRoad(profile))
                return null;
            var sections = OmsiProfileSections.Of(profile);
            return point.Offset <= Math.Max(sections.RoadLeft, sections.RoadRight) ? point : null;
        }

        /// <summary>
        /// The first place where a planned road crosses the centre line of an existing road, at least <paramref name="skip"/>
        /// metres from either end of the planned road; splines in <paramref name="ignore"/>, such as the ones it joins, do
        /// not count. Null when it crosses none.
        /// </summary>
        public static OmsiRoadCrossing FirstCrossing(OmsiMapSummary summary, OmsiSplineProfileCatalog profiles, OmsiRoadPlan plan, double skip,
            ICollection<int> ignore)
        {
            var crossings = Crossings(summary, profiles, plan, skip, ignore);
            return crossings.Count > 0 ? crossings[0] : null;
        }

        /// <summary>
        /// Every place where a planned road crosses the centre line of an existing road, in the order the road meets them,
        /// at least <paramref name="skip"/> metres from either end of the planned road; splines in <paramref name="ignore"/>
        /// do not count. A road crossed twice, as by a bend, is listed twice.
        /// </summary>
        public static List<OmsiRoadCrossing> Crossings(OmsiMapSummary summary, OmsiSplineProfileCatalog profiles, OmsiRoadPlan plan, double skip,
            ICollection<int> ignore)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var found = new List<OmsiRoadCrossing>();
            if (plan == null || plan.Pieces.Count == 0)
                return found;
            // The planned road as a line every half metre, with distance and heading at each point.
            var course = new List<double[]>();
            var travelled = 0.0;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var piece in plan.Pieces)
            {
                var steps = Math.Max(1, (int)Math.Ceiling(piece.LengthMeters / 0.5));
                for (var step = course.Count == 0 ? 0 : 1; step <= steps; step++)
                {
                    var along = piece.LengthMeters * step / steps;
                    piece.PointAt(along, out var x, out var y, out _);
                    var heading = piece.RadiusMeters == 0.0 ? piece.HeadingDegrees : piece.HeadingDegrees + along / piece.RadiusMeters * 180.0 / Math.PI;
                    course.Add(new[] { x, y, travelled + along, heading });
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
                travelled += piece.LengthMeters;
            }

            var size = summary.TileSizeMeters;
            foreach (var tile in summary.Tiles)
            {
                if ((tile.X + 1) * size < minX - 1.0 || tile.X * size > maxX + 1.0 || (tile.Y + 1) * size < minY - 1.0 || tile.Y * size > maxY + 1.0)
                    continue;
                foreach (var spline in tile.Splines)
                {
                    if (ignore != null && ignore.Contains(spline.Id))
                        continue;
                    var origin = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, spline.LocalX, spline.LocalY, size);
                    if (origin.X + spline.LengthMeters < minX || origin.X - spline.LengthMeters > maxX ||
                        origin.Y + spline.LengthMeters < minY || origin.Y - spline.LengthMeters > maxY)
                        continue;
                    if (!IsRoad(profiles.Get(spline.AssetPath)))
                        continue;
                    var steps = Math.Max(1, (int)Math.Ceiling(spline.LengthMeters));
                    var previous = Point(tile, spline, 0.0, size);
                    for (var step = 1; step <= steps; step++)
                    {
                        var along = spline.LengthMeters * step / steps;
                        var current = Point(tile, spline, along, size);
                        for (var index = 1; index < course.Count; index++)
                        {
                            var a = course[index - 1];
                            var b = course[index];
                            if (!Intersect(a[0], a[1], b[0], b[1], previous.X, previous.Y, current.X, current.Y, out var t, out var u))
                                continue;
                            var at = a[2] + (b[2] - a[2]) * t;
                            if (at < skip || at > travelled - skip)
                                continue;
                            found.Add(new OmsiRoadCrossing
                            {
                                Road = new OmsiSplinePoint { Tile = tile, Spline = spline, Distance = along - spline.LengthMeters / steps * (1.0 - u), Offset = 0.0 },
                                Along = at, HeadingDegrees = a[3] + OmsiRoadPlanner.Normalize(b[3] - a[3]) * t
                            });
                        }
                        previous = current;
                    }
                }
            }
            // Where the lines meet at a corner of either, the same crossing is found twice; the first one stays.
            found.Sort((first, second) => first.Along.CompareTo(second.Along));
            var result = new List<OmsiRoadCrossing>();
            foreach (var crossing in found)
                if (!result.Exists(kept => kept.Road.Spline.Id == crossing.Road.Spline.Id && Math.Abs(kept.Along - crossing.Along) < 1.0))
                    result.Add(crossing);
            return result;
        }

        /// <summary>
        /// The arms of a junction where a road meets an existing one, relative to the existing road's heading: its arms
        /// ahead (0°) and back (180°), marked main, the one back seeing the road come towards it; then the new road's arms,
        /// those it arrives on and those it leaves on.
        /// </summary>
        public static List<OmsiJunctionArm> Arms(string mainProfilePath, OmsiSplineProfile mainProfile, string sideProfilePath, OmsiSplineProfile sideProfile,
            IEnumerable<double> arriving, IEnumerable<double> leaving)
        {
            var arms = new List<OmsiJunctionArm>
            {
                new OmsiJunctionArm { HeadingDegrees = 0.0, ProfilePath = mainProfilePath, Profile = mainProfile, Main = true },
                new OmsiJunctionArm { HeadingDegrees = 180.0, ProfilePath = mainProfilePath, Profile = mainProfile, Main = true, Mirrored = true }
            };
            foreach (var heading in arriving)
                arms.Add(new OmsiJunctionArm { HeadingDegrees = (heading % 360.0 + 360.0) % 360.0, ProfilePath = sideProfilePath, Profile = sideProfile, Mirrored = true });
            foreach (var heading in leaving)
                arms.Add(new OmsiJunctionArm { HeadingDegrees = (heading % 360.0 + 360.0) % 360.0, ProfilePath = sideProfilePath, Profile = sideProfile });
            return arms;
        }

        /// <summary>The heading of a side road relative to the main road, kept between 35° and 145° off it on its side.</summary>
        public static double SideHeading(double relativeDegrees)
        {
            var relative = OmsiRoadPlanner.Normalize(relativeDegrees);
            var side = relative < 0.0 ? -1.0 : 1.0;
            return side * Math.Max(35.0, Math.Min(145.0, Math.Abs(relative)));
        }

        /// <summary>The places of the helpers ahead and back (0° and 180°) among an object's connection points, or null.</summary>
        public static int[] MainPair(IReadOnlyList<OmsiSplineHelper> helpers)
        {
            int ahead = -1, back = -1;
            for (var index = 0; index < helpers.Count; index++)
            {
                var heading = OmsiRoadPlanner.Normalize(helpers[index].HeadingDegrees);
                if (Math.Abs(heading) < 0.5 && ahead < 0)
                    ahead = index;
                else if (Math.Abs(Math.Abs(heading) - 180.0) < 0.5 && back < 0)
                    back = index;
            }
            return ahead >= 0 && back >= 0 ? new[] { back, ahead } : null;
        }

        private static OmsiMapPoint Point(OmsiTileSummary tile, OmsiSplineRecord spline, double along, double size)
        {
            OmsiMapGeometry.GetLocalFrameAt(spline, along, out var local, out _);
            return OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, local.X, local.Y, size);
        }

        /// <summary>Where segment a-b meets segment c-d, as fractions along each; false when they do not meet.</summary>
        private static bool Intersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy, out double t, out double u)
        {
            t = 0.0;
            u = 0.0;
            var rx = bx - ax;
            var ry = by - ay;
            var sx = dx - cx;
            var sy = dy - cy;
            var denominator = rx * sy - ry * sx;
            if (Math.Abs(denominator) < 1e-12)
                return false;
            t = ((cx - ax) * sy - (cy - ay) * sx) / denominator;
            u = ((cx - ax) * ry - (cy - ay) * rx) / denominator;
            return t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0;
        }
    }
}
