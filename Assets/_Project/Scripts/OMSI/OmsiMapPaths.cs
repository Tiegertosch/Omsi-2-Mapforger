using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// One path the AI drives or walks: a lane of a spline after the [path]s of its profile, or a [path] of an object such
    /// as a junction. Its points run in the path's own order; <see cref="Direction"/> says which way it is used.
    /// </summary>
    public sealed class OmsiMapPath
    {
        public int TileX { get; internal set; }
        public int TileY { get; internal set; }

        /// <summary>The ID of the spline or object it belongs to.</summary>
        public int ElementId { get; internal set; }

        public bool OnSpline { get; internal set; }

        /// <summary>Its place among the [path]s of the profile or object, as [rule]s name it.</summary>
        public int PathIndex { get; internal set; }

        /// <summary>0 for roads, 1 for pedestrians, 2 for rails.</summary>
        public int Kind { get; internal set; }

        /// <summary>0 from its first point to its last, 1 the other way, 2 both ways; a mirrored spline's are turned round already.</summary>
        public int Direction { get; internal set; }

        /// <summary>The indicator a vehicle sets on it: 0 none, 2 left, 3 right.</summary>
        public int Blinker { get; internal set; }

        public double Width { get; internal set; }

        /// <summary>The path in world metres.</summary>
        public OmsiMapPoint[] Points { get; internal set; }

        /// <summary>The rules of its element for this path, in the order of the tile.</summary>
        public List<OmsiPathRule> Rules { get; } = new List<OmsiPathRule>();

        /// <summary>True when no other path of its kind joins its first point, as traffic would need to go on or come in there.</summary>
        public bool OpenStart { get; internal set; }

        /// <summary>True when no other path of its kind joins its last point.</summary>
        public bool OpenEnd { get; internal set; }

        /// <summary>The value of the last rule of a name for this path that is not taken away, or null.</summary>
        public double? Rule(string name)
        {
            double? value = null;
            foreach (var rule in Rules)
                if (string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase))
                    value = rule.Kills ? (double?)null : rule.Value;
            return value;
        }
    }

    /// <summary>
    /// The AI paths of a map, as openOMSI builds its lanes: every [path] of a spline's profile beside the spline, every
    /// [path] of an object turned with it, and the rules after each element. Paths join where one ends within
    /// <see cref="LinkMeters"/> of where another of its kind starts, heading the same way within <see cref="LinkDegrees"/>.
    /// </summary>
    public static class OmsiMapPaths
    {
        public const double LinkMeters = 1.5;
        public const double LinkDegrees = 40.0;

        /// <summary>The paths of a tile's splines and objects; elements whose file the installation lacks have none.</summary>
        /// <param name="sceneryObjects">The .sco of an asset path, or null when there is none.</param>
        /// <param name="splineProfiles">The .sli of an asset path, or null when there is none.</param>
        public static List<OmsiMapPath> OfTile(OmsiMapSummary summary, OmsiTileSummary tile, Func<string, OmsiSceneryObject> sceneryObjects,
            Func<string, OmsiSplineProfile> splineProfiles)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            var paths = new List<OmsiMapPath>();
            foreach (var spline in tile.Splines)
            {
                var profile = splineProfiles?.Invoke(spline.AssetPath);
                if (profile == null || !(spline.LengthMeters > 0.0) || double.IsInfinity(spline.LengthMeters))
                    continue;
                for (var index = 0; index < profile.Lanes.Count; index++)
                {
                    var lane = profile.Lanes[index];
                    if (lane.Kind == OmsiSplineProfile.UnreadableLane)
                        continue;
                    // A mirrored spline's lanes lie on the other side and run the other way, as its carriageway does.
                    var direction = spline.IsMirrored && lane.Direction != 2 ? 1 - lane.Direction : lane.Direction;
                    var path = new OmsiMapPath
                    {
                        TileX = tile.X, TileY = tile.Y, ElementId = spline.Id, OnSpline = true, PathIndex = index, Kind = lane.Kind, Direction = direction,
                        Width = lane.Width, Points = SplineLane(tile, spline, lane.X * (spline.IsMirrored ? -1.0 : 1.0), summary.TileSizeMeters)
                    };
                    path.Rules.AddRange(spline.Rules.FindAll(rule => rule.PathIndex == index));
                    paths.Add(path);
                }
            }
            foreach (var record in tile.Objects)
            {
                var sceneryObject = sceneryObjects?.Invoke(record.AssetPath);
                if (sceneryObject == null)
                    continue;
                for (var index = 0; index < sceneryObject.Paths.Count; index++)
                {
                    var objectPath = sceneryObject.Paths[index];
                    if (!(objectPath.LengthMeters > 0.0) || double.IsInfinity(objectPath.LengthMeters))
                        continue;
                    var path = new OmsiMapPath
                    {
                        TileX = tile.X, TileY = tile.Y, ElementId = record.Id, PathIndex = index, Kind = objectPath.Kind, Direction = objectPath.Direction,
                        Blinker = objectPath.Blinker, Width = objectPath.WidthMeters, Points = ObjectLane(tile, record, objectPath, summary.TileSizeMeters)
                    };
                    path.Rules.AddRange(record.Rules.FindAll(rule => rule.PathIndex == index));
                    paths.Add(path);
                }
            }
            return paths;
        }

        /// <summary>
        /// Marks the ends of paths no other path joins, as openOMSI would leave its lanes unlinked there: traffic leaving a
        /// path finds no path starting within reach and heading its way, or traffic coming in finds none ending there.
        /// </summary>
        /// <param name="paths">All paths around those to check, such as of their tiles and the neighbours.</param>
        public static void FindOpenEnds(IList<OmsiMapPath> paths)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            // The ends of every path in each way it is used, in a grid of cells as large as the reach.
            var starts = new Dictionary<long, List<End>>();
            var finishes = new Dictionary<long, List<End>>();
            for (var index = 0; index < paths.Count; index++)
            {
                var path = paths[index];
                if (path.Points == null || path.Points.Length < 2)
                    continue;
                foreach (var forward in Ways(path.Direction))
                {
                    Add(starts, Entry(path, index, forward));
                    Add(finishes, Exit(path, index, forward));
                }
            }
            for (var index = 0; index < paths.Count; index++)
            {
                var path = paths[index];
                if (path.Points == null || path.Points.Length < 2)
                    continue;
                path.OpenStart = path.OpenEnd = false;
                foreach (var forward in Ways(path.Direction))
                {
                    // Leaving: another path must start here; coming in: another must end here.
                    var exit = Exit(path, index, forward);
                    var entry = Entry(path, index, forward);
                    var open = !Joined(starts, exit);
                    var unfed = !Joined(finishes, entry);
                    if (forward)
                    {
                        path.OpenEnd |= open;
                        path.OpenStart |= unfed;
                    }
                    else
                    {
                        path.OpenStart |= open;
                        path.OpenEnd |= unfed;
                    }
                }
            }
        }

        /// <summary>A lane beside a spline, <paramref name="offset"/> metres to its right, in world metres.</summary>
        public static OmsiMapPoint[] SplineLane(OmsiTileSummary tile, OmsiSplineRecord spline, double offset, double tileSize)
        {
            var count = SegmentCount(spline.LengthMeters);
            var points = new OmsiMapPoint[count + 1];
            for (var index = 0; index <= count; index++)
            {
                OmsiMapGeometry.GetLocalFrameAt(spline, spline.LengthMeters * index / count, out var position, out var heading);
                points[index] = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, position.X + Math.Cos(heading) * offset, position.Y - Math.Sin(heading) * offset,
                    tileSize);
            }
            return points;
        }

        /// <summary>A [path] of an object in world metres: in the object frame (x right, y forward) like a spline from its start, then turned with the object.</summary>
        public static OmsiMapPoint[] ObjectLane(OmsiTileSummary tile, OmsiObjectRecord record, OmsiObjectPath path, double tileSize)
        {
            var lane = new OmsiSplineRecord
            {
                LocalX = path.X, LocalY = path.Y, HeadingDegrees = path.HeadingDegrees, LengthMeters = path.LengthMeters, RadiusMeters = path.RadiusMeters
            };
            var heading = record.HeadingDegrees * Math.PI / 180.0;
            var cos = Math.Cos(heading);
            var sin = Math.Sin(heading);
            var count = SegmentCount(path.LengthMeters);
            var points = new OmsiMapPoint[count + 1];
            for (var index = 0; index <= count; index++)
            {
                OmsiMapGeometry.GetLocalFrameAt(lane, path.LengthMeters * index / count, out var local, out _);
                points[index] = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX + local.X * cos + local.Y * sin, record.LocalY - local.X * sin + local.Y * cos,
                    tileSize);
            }
            return points;
        }

        /// <summary>A point every 4 m, at least 2 and at most 256 segments.</summary>
        private static int SegmentCount(double length)
        {
            return Math.Max(2, Math.Min(256, (int)Math.Ceiling(length / 4.0)));
        }

        /// <summary>Where a path is entered or left, one way round, with the heading of travel there in degrees.</summary>
        private struct End
        {
            public int Path;
            public int Kind;
            public double X;
            public double Y;
            public double Heading;
        }

        private static IEnumerable<bool> Ways(int direction)
        {
            if (direction != 1)
                yield return true;
            if (direction != 0)
                yield return false;
        }

        private static End Entry(OmsiMapPath path, int index, bool forward)
        {
            var points = path.Points;
            return forward ? At(path, index, points[0], points[1]) : At(path, index, points[points.Length - 1], points[points.Length - 2]);
        }

        private static End Exit(OmsiMapPath path, int index, bool forward)
        {
            var points = path.Points;
            return forward ? At(path, index, points[points.Length - 1], points[points.Length - 2], true) : At(path, index, points[0], points[1], true);
        }

        /// <summary>The end at <paramref name="point"/>, heading away from <paramref name="next"/>, or towards it when <paramref name="leaving"/> is false.</summary>
        private static End At(OmsiMapPath path, int index, OmsiMapPoint point, OmsiMapPoint next, bool leaving = false)
        {
            var dx = leaving ? point.X - next.X : next.X - point.X;
            var dy = leaving ? point.Y - next.Y : next.Y - point.Y;
            return new End { Path = index, Kind = path.Kind, X = point.X, Y = point.Y, Heading = Math.Atan2(dx, dy) * 180.0 / Math.PI };
        }

        private static long Cell(double x, double y)
        {
            return ((long)Math.Floor(x / LinkMeters) << 32) ^ (uint)(int)Math.Floor(y / LinkMeters);
        }

        private static void Add(Dictionary<long, List<End>> grid, End end)
        {
            var cell = Cell(end.X, end.Y);
            if (!grid.TryGetValue(cell, out var list))
                grid[cell] = list = new List<End>();
            list.Add(end);
        }

        /// <summary>True when another path's end of the same kind lies within reach and heads the same way.</summary>
        private static bool Joined(Dictionary<long, List<End>> grid, End end)
        {
            var cellX = (long)Math.Floor(end.X / LinkMeters);
            var cellY = (long)Math.Floor(end.Y / LinkMeters);
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!grid.TryGetValue(((cellX + dx) << 32) ^ (uint)(int)(cellY + dy), out var list))
                        continue;
                    foreach (var other in list)
                    {
                        if (other.Path == end.Path || other.Kind != end.Kind)
                            continue;
                        var distance = Math.Sqrt((other.X - end.X) * (other.X - end.X) + (other.Y - end.Y) * (other.Y - end.Y));
                        var turn = Math.Abs(OmsiRoadPlanner.Normalize(other.Heading - end.Heading));
                        if (distance <= LinkMeters && turn < LinkDegrees)
                            return true;
                    }
                }
            return false;
        }
    }
}
