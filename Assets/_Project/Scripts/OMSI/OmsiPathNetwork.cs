using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A path driven one way round: the unit of the route a bus takes.</summary>
    public sealed class OmsiLane
    {
        public OmsiMapPath Path { get; internal set; }

        /// <summary>True when the lane runs from the path's first point to its last.</summary>
        public bool Forward { get; internal set; }

        internal OmsiMapPoint[] Points;

        /// <summary>The lane's line in world metres, in the way of travel.</summary>
        public OmsiMapPoint[] Geometry => Points;
        internal double PolylineLength;
        internal int Index;
        internal List<int> Next = new List<int>();

        /// <summary>The length OMSI counts for the path.</summary>
        public double LengthMeters => Path.LengthMeters > 0.0 ? Path.LengthMeters : PolylineLength;
    }

    /// <summary>Where a stop lies on a lane: how far along it in the way of travel, and how far to the side.</summary>
    public sealed class OmsiStopPlace
    {
        public OmsiLane Lane { get; internal set; }

        /// <summary>Metres from the lane's start to the stop, counted on the path's own length.</summary>
        public double Position { get; internal set; }

        /// <summary>Metres between the stop's object and the lane.</summary>
        public double Distance { get; internal set; }
    }

    /// <summary>The way between two stops: the lanes from the first stop's to the second's, and its length counted as OMSI does for a station link.</summary>
    public sealed class OmsiNetworkRoute
    {
        public List<OmsiLane> Lanes { get; } = new List<OmsiLane>();

        /// <summary>Length from the first stop to the second in metres.</summary>
        public double LengthMeters { get; internal set; }
    }

    /// <summary>
    /// The vehicle lanes of a map as a directed graph (ADR 093): each path of the wanted kind once for each way it may be driven, a lane leads
    /// on to those that start within <see cref="OmsiMapPaths.LinkMeters"/> where it ends, heading the same way within
    /// <see cref="OmsiMapPaths.LinkDegrees"/>, which is how openOMSI links lanes. It places a stop on the lane beside it and finds the shortest way
    /// from one stop to the next.
    /// </summary>
    public sealed class OmsiPathNetwork
    {
        private readonly List<OmsiLane> lanes = new List<OmsiLane>();
        private readonly Dictionary<long, List<int>> starts = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, List<int>> places = new Dictionary<long, List<int>>();
        private const double PlaceCell = 16.0;

        private OmsiPathNetwork()
        {
        }

        public IReadOnlyList<OmsiLane> Lanes => lanes;

        /// <summary>The vehicle lanes of all tiles of a map, read with the object and spline files of the installation at <paramref name="installationRoot"/>.</summary>
        public static OmsiPathNetwork ForMap(OmsiMapSummary summary, string installationRoot)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (string.IsNullOrEmpty(installationRoot))
                throw new ArgumentException("The OMSI installation is needed for the lanes of objects and splines.", nameof(installationRoot));
            var profiles = new OmsiSplineProfileCatalog(installationRoot);
            var objects = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);
            Func<string, OmsiSceneryObject> scenery = assetPath =>
            {
                if (objects.TryGetValue(assetPath, out var cached))
                    return cached;
                OmsiSceneryObject sceneryObject = null;
                try
                {
                    var path = OmsiRelativePath.Resolve(installationRoot, assetPath);
                    if (System.IO.File.Exists(path))
                        sceneryObject = OmsiSceneryObject.Parse(System.IO.File.ReadAllBytes(path));
                }
                catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException || exception is FormatException ||
                                                  exception is ArgumentException)
                {
                    // An unreadable object has no lanes.
                }
                objects[assetPath] = sceneryObject;
                return sceneryObject;
            };
            var paths = new List<OmsiMapPath>();
            foreach (var tile in summary.Tiles)
                if (tile.IsAvailable)
                    paths.AddRange(OmsiMapPaths.OfTile(summary, tile, scenery, profiles.Get));
            return Build(paths);
        }

        /// <summary>The lanes of the paths that are of <paramref name="kind"/> (0: vehicles) with their links.</summary>
        public static OmsiPathNetwork Build(IEnumerable<OmsiMapPath> paths, int kind = 0)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            var network = new OmsiPathNetwork();
            foreach (var path in paths)
            {
                if (path.Kind != kind || path.Points == null || path.Points.Length < 2)
                    continue;
                if (path.Direction != 1)
                    network.AddLane(path, true);
                if (path.Direction != 0)
                    network.AddLane(path, false);
            }
            foreach (var lane in network.lanes)
            {
                var start = lane.Points[0];
                var cell = Cell(start.X, start.Y, OmsiMapPaths.LinkMeters);
                if (!network.starts.TryGetValue(cell, out var list))
                    network.starts[cell] = list = new List<int>();
                list.Add(lane.Index);
            }
            foreach (var lane in network.lanes)
            {
                var end = lane.Points[lane.Points.Length - 1];
                var endHeading = Heading(lane.Points[lane.Points.Length - 2], end);
                var cellX = (long)Math.Floor(end.X / OmsiMapPaths.LinkMeters);
                var cellY = (long)Math.Floor(end.Y / OmsiMapPaths.LinkMeters);
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        if (!network.starts.TryGetValue(((cellX + dx) << 32) ^ (uint)(int)(cellY + dy), out var list))
                            continue;
                        foreach (var candidate in list)
                        {
                            var other = network.lanes[candidate];
                            if (other.Path == lane.Path)
                                continue;
                            var begin = other.Points[0];
                            var distance = Math.Sqrt((begin.X - end.X) * (begin.X - end.X) + (begin.Y - end.Y) * (begin.Y - end.Y));
                            var turn = Math.Abs(OmsiRoadPlanner.Normalize(Heading(begin, other.Points[1]) - endHeading));
                            if (distance <= OmsiMapPaths.LinkMeters && turn < OmsiMapPaths.LinkDegrees)
                                lane.Next.Add(candidate);
                        }
                    }
            }
            // A grid of the lanes' bounding cells, for placing stops.
            foreach (var lane in network.lanes)
            {
                var seen = new HashSet<long>();
                foreach (var point in lane.Points)
                {
                    var cell = Cell(point.X, point.Y, PlaceCell);
                    if (!seen.Add(cell))
                        continue;
                    if (!network.places.TryGetValue(cell, out var list))
                        network.places[cell] = list = new List<int>();
                    list.Add(lane.Index);
                }
            }
            return network;
        }

        /// <summary>
        /// The lane a stop at a position belongs to: the nearest within <paramref name="maxDistance"/>, one that has the stop on the side of the way of
        /// travel traffic stops at (the right with right-hand traffic) coming before any that has it on the other side. Null when no lane is near.
        /// </summary>
        public OmsiStopPlace PlaceStop(OmsiMapPoint position, bool rightHandTraffic, double maxDistance = 8.0)
        {
            OmsiStopPlace best = null;
            var bestScore = double.MaxValue;
            var reach = (int)Math.Ceiling(maxDistance / PlaceCell) + 1;
            var cellX = (long)Math.Floor(position.X / PlaceCell);
            var cellY = (long)Math.Floor(position.Y / PlaceCell);
            var tried = new HashSet<int>();
            for (var dx = -reach; dx <= reach; dx++)
                for (var dy = -reach; dy <= reach; dy++)
                {
                    if (!places.TryGetValue(((cellX + dx) << 32) ^ (uint)(int)(cellY + dy), out var list))
                        continue;
                    foreach (var index in list)
                    {
                        if (!tried.Add(index))
                            continue;
                        var lane = lanes[index];
                        Project(lane, position, out var along, out var distance, out var right);
                        if (distance > maxDistance)
                            continue;
                        var score = distance + (right == rightHandTraffic ? 0.0 : 100.0);
                        if (score >= bestScore)
                            continue;
                        bestScore = score;
                        var scale = lane.PolylineLength > 0.0 ? lane.LengthMeters / lane.PolylineLength : 1.0;
                        best = new OmsiStopPlace { Lane = lane, Position = along * scale, Distance = distance };
                    }
                }
            return best;
        }

        /// <summary>
        /// Every lane a stop could be served from: those of the element of the nearest lane, within <paramref name="maxDistance"/>, that have the stop on the side traffic stops at, nearest first.
        /// A road with several lanes one way gives several; the route picks the one that leads on best.
        /// </summary>
        public List<OmsiStopPlace> PlaceStopCandidates(OmsiMapPoint position, bool rightHandTraffic, double maxDistance = 8.0)
        {
            var result = new List<OmsiStopPlace>();
            var reach = (int)Math.Ceiling(maxDistance / PlaceCell) + 1;
            var cellX = (long)Math.Floor(position.X / PlaceCell);
            var cellY = (long)Math.Floor(position.Y / PlaceCell);
            var tried = new HashSet<int>();
            for (var dx = -reach; dx <= reach; dx++)
                for (var dy = -reach; dy <= reach; dy++)
                {
                    if (!places.TryGetValue(((cellX + dx) << 32) ^ (uint)(int)(cellY + dy), out var list))
                        continue;
                    foreach (var index in list)
                    {
                        if (!tried.Add(index))
                            continue;
                        var lane = lanes[index];
                        Project(lane, position, out var along, out var distance, out var right);
                        if (distance > maxDistance || right != rightHandTraffic)
                            continue;
                        var scale = lane.PolylineLength > 0.0 ? lane.LengthMeters / lane.PolylineLength : 1.0;
                        result.Add(new OmsiStopPlace { Lane = lane, Position = along * scale, Distance = distance });
                    }
                }
            result.Sort((first, second) => first.Distance.CompareTo(second.Distance));
            // Only the lanes of the element the nearest one belongs to: a lane of the next junction or road, that is further away, is not where the
            // stop is served from.
            if (result.Count > 1)
            {
                var nearest = result[0].Lane.Path;
                result.RemoveAll(place => place.Lane.Path.ElementId != nearest.ElementId || place.Lane.Path.TileX != nearest.TileX ||
                                          place.Lane.Path.TileY != nearest.TileY || place.Lane.Path.OnSpline != nearest.OnSpline);
            }
            return result;
        }

        /// <summary>
        /// The shortest way from one stop to another by the length OMSI counts for each path, or null when none leads there. A stop further along its
        /// own lane is reached without leaving it.
        /// </summary>
        public OmsiNetworkRoute Route(OmsiStopPlace from, OmsiStopPlace to)
        {
            if (from == null)
                throw new ArgumentNullException(nameof(from));
            if (to == null)
                throw new ArgumentNullException(nameof(to));
            return Route(new[] { from }, new[] { to });
        }

        /// <summary>
        /// The shortest way from any of the lanes the first stop could be served from to any of those of the second. A little extra is counted for
        /// each metre a lane lies away from its stop, so the nearest lane wins where the ways are as long.
        /// </summary>
        public OmsiNetworkRoute Route(IReadOnlyList<OmsiStopPlace> fromChoices, IReadOnlyList<OmsiStopPlace> toChoices)
        {
            if (fromChoices == null || fromChoices.Count == 0)
                throw new ArgumentException("A route needs a place of the first stop.", nameof(fromChoices));
            if (toChoices == null || toChoices.Count == 0)
                throw new ArgumentException("A route needs a place of the second stop.", nameof(toChoices));
            const double DistanceWeight = 2.0;
            var route = new OmsiNetworkRoute();
            OmsiStopPlace direct = null;
            OmsiStopPlace directTo = null;
            var directCost = double.MaxValue;
            foreach (var start in fromChoices)
                foreach (var end in toChoices)
                    if (start.Lane == end.Lane && end.Position >= start.Position)
                    {
                        var way = end.Position - start.Position + DistanceWeight * (start.Distance + end.Distance);
                        if (way < directCost)
                        {
                            directCost = way;
                            direct = start;
                            directTo = end;
                        }
                    }
            if (direct != null)
            {
                route.Lanes.Add(direct.Lane);
                route.LengthMeters = directTo.Position - direct.Position;
                return route;
            }
            var cost = new double[lanes.Count];
            // The lane before, or -2 - the lane of a first stop's place the route starts on.
            var before = new int[lanes.Count];
            for (var index = 0; index < cost.Length; index++)
            {
                cost[index] = double.MaxValue;
                before[index] = -1;
            }
            var queue = new SortedSet<KeyValuePair<double, int>>(Comparer<KeyValuePair<double, int>>.Create((first, second) =>
            {
                var order = first.Key.CompareTo(second.Key);
                return order != 0 ? order : first.Value.CompareTo(second.Value);
            }));
            var startPlaces = new Dictionary<int, OmsiStopPlace>();
            foreach (var start in fromChoices)
            {
                if (!startPlaces.TryGetValue(start.Lane.Index, out var known) || start.Distance < known.Distance)
                    startPlaces[start.Lane.Index] = start;
                var first = Math.Max(0.0, start.Lane.LengthMeters - start.Position) + DistanceWeight * start.Distance;
                foreach (var next in start.Lane.Next)
                    if (first < cost[next])
                    {
                        cost[next] = first;
                        before[next] = -2 - start.Lane.Index;
                        queue.Add(new KeyValuePair<double, int>(first, next));
                    }
            }
            var endPlaces = new Dictionary<int, OmsiStopPlace>();
            foreach (var end in toChoices)
                if (!endPlaces.TryGetValue(end.Lane.Index, out var known) || end.Distance < known.Distance)
                    endPlaces[end.Lane.Index] = end;
            var bestTotal = double.MaxValue;
            var bestLane = -1;
            while (queue.Count > 0)
            {
                var top = queue.Min;
                if (top.Key >= bestTotal)
                    break;
                queue.Remove(top);
                var index = top.Value;
                if (top.Key > cost[index])
                    continue;
                if (endPlaces.TryGetValue(index, out var place) && top.Key + place.Position + DistanceWeight * place.Distance < bestTotal)
                {
                    bestTotal = top.Key + place.Position + DistanceWeight * place.Distance;
                    bestLane = index;
                }
                var lane = lanes[index];
                var through = top.Key + lane.LengthMeters;
                foreach (var next in lane.Next)
                    if (through < cost[next])
                    {
                        cost[next] = through;
                        before[next] = index;
                        queue.Add(new KeyValuePair<double, int>(through, next));
                    }
            }
            if (bestLane < 0)
                return null;
            var chain = new List<OmsiLane> { lanes[bestLane] };
            var origin = before[bestLane];
            for (var guard = 0; origin >= 0 && guard <= lanes.Count; guard++)
            {
                chain.Add(lanes[origin]);
                origin = before[origin];
            }
            var startLane = -2 - origin;
            chain.Add(lanes[startLane]);
            chain.Reverse();
            route.Lanes.AddRange(chain);
            var length = Math.Max(0.0, lanes[startLane].LengthMeters - startPlaces[startLane].Position) + endPlaces[bestLane].Position;
            for (var index = 1; index + 1 < chain.Count; index++)
                length += chain[index].LengthMeters;
            route.LengthMeters = length;
            return route;
        }

        private void AddLane(OmsiMapPath path, bool forward)
        {
            var points = new OmsiMapPoint[path.Points.Length];
            for (var index = 0; index < points.Length; index++)
                points[index] = forward ? path.Points[index] : path.Points[points.Length - 1 - index];
            var length = 0.0;
            for (var index = 1; index < points.Length; index++)
                length += Distance(points[index - 1], points[index]);
            lanes.Add(new OmsiLane { Path = path, Forward = forward, Points = points, PolylineLength = length, Index = lanes.Count });
        }

        /// <summary>How far along a lane the nearest point to a position is, how far it lies off, and whether the position is to the right of the way of travel there.</summary>
        private static void Project(OmsiLane lane, OmsiMapPoint position, out double along, out double distance, out bool right)
        {
            distance = double.MaxValue;
            along = 0.0;
            right = true;
            var run = 0.0;
            for (var index = 1; index < lane.Points.Length; index++)
            {
                var a = lane.Points[index - 1];
                var b = lane.Points[index];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var squared = dx * dx + dy * dy;
                var t = squared > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((position.X - a.X) * dx + (position.Y - a.Y) * dy) / squared)) : 0.0;
                var px = a.X + dx * t;
                var py = a.Y + dy * t;
                var off = Math.Sqrt((position.X - px) * (position.X - px) + (position.Y - py) * (position.Y - py));
                if (off < distance)
                {
                    distance = off;
                    along = run + Math.Sqrt(squared) * t;
                    // The cross product of the way of travel and the way to the stop: negative means the stop is to the right.
                    right = dx * (position.Y - a.Y) - dy * (position.X - a.X) <= 0.0;
                }
                run += Math.Sqrt(squared);
            }
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }

        private static double Heading(OmsiMapPoint from, OmsiMapPoint to)
        {
            return Math.Atan2(to.X - from.X, to.Y - from.Y) * 180.0 / Math.PI;
        }

        private static long Cell(double x, double y, double size)
        {
            return ((long)Math.Floor(x / size) << 32) ^ (uint)(int)Math.Floor(y / size);
        }
    }
}
