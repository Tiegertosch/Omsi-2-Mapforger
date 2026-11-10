using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>How two movements of a crossing meet.</summary>
    public enum OmsiConflictKind
    {
        /// <summary>Their paths cross inside the crossing.</summary>
        Crossing,

        /// <summary>They end in the same lane.</summary>
        Merging,

        /// <summary>A footpath crosses a lane.</summary>
        Pedestrian
    }

    /// <summary>Two movements of a crossing that must not go at the same time, or only when one gives way.</summary>
    public sealed class OmsiSignalConflict
    {
        public int First { get; internal set; }
        public int Second { get; internal set; }
        public OmsiConflictKind Kind { get; internal set; }

        /// <summary>
        /// True when one of them gives way and both may go together, as traffic lights usually allow: a left turn against
        /// the oncoming traffic, a turning vehicle against pedestrians crossing where it leaves.
        /// </summary>
        public bool Permitted { get; internal set; }

        /// <summary>Where they meet in the crossing's frame: the first crossing point, or where they end in one lane.</summary>
        public double X { get; internal set; }

        public double Y { get; internal set; }
    }

    /// <summary>A conflict whose movements are given way at the same time by the program.</summary>
    public sealed class OmsiSignalClash
    {
        public OmsiSignalConflict Conflict { get; internal set; }

        /// <summary>The first second of the cycle at which both may go.</summary>
        public double Seconds { get; internal set; }

        /// <summary>How long in one cycle both may go together.</summary>
        public double OverlapSeconds { get; internal set; }

        /// <summary>
        /// Both movements obey the same light: they always go together, so no program can keep them apart (a matter for the
        /// lanes or the groups, not the timing).
        /// </summary>
        public bool SameLight { get; internal set; }
    }

    /// <summary>
    /// Which movements of a crossing conflict, found from the geometry of their paths (ADR 085, milestone 4; the conflict
    /// matrix of the brief, §10, shows these in milestone 5), and where a program gives conflicting movements way at the
    /// same time. Movements of one approach never conflict: they leave the same stop line together. Others conflict where
    /// their paths cross or end in the same lane; a turn against the oncoming approach (a left turn where traffic keeps
    /// right) and a turn against a footpath are permitted conflicts, since one of them gives way.
    /// </summary>
    public static class OmsiSignalConflicts
    {
        /// <summary>Paths are followed in steps of this many metres.</summary>
        private const double StepMeters = 1.0;

        /// <summary>A crossing this close to where a movement starts or ends counts as touching, not crossing.</summary>
        private const double EndMeters = 1.0;

        /// <summary>Two movements whose ends lie this close end in the same lane.</summary>
        private const double SameLaneMeters = 1.2;

        /// <summary>Approaches whose directions differ from opposite by at most this face each other.</summary>
        private const double OppositeDegrees = 35.0;

        /// <summary>The program is looked at in steps of this many seconds.</summary>
        private const double SampleSeconds = 0.25;

        /// <param name="pathLights">
        /// The light each path is bound to (<see cref="OmsiSignalProgram.PathLights"/>), when known: a movement is then only
        /// followed until the next bound path after its own stop line, where it obeys another light (crossing objects of OMSI
        /// hold several junctions, and one chain of paths runs through all of them).
        /// </param>
        public static List<OmsiSignalConflict> Of(OmsiSceneryObject sceneryObject, OmsiSignalIntersection crossing, IList<int> pathLights = null)
        {
            if (sceneryObject == null)
                throw new ArgumentNullException(nameof(sceneryObject));
            if (crossing == null)
                throw new ArgumentNullException(nameof(crossing));
            var governed = crossing.Movements.Select(movement => Governed(movement, pathLights)).ToList();
            var lines = governed.Select(paths => Line(sceneryObject, paths)).ToList();
            var conflicts = new List<OmsiSignalConflict>();
            for (var first = 0; first < crossing.Movements.Count; first++)
            for (var second = first + 1; second < crossing.Movements.Count; second++)
            {
                var a = crossing.Movements[first];
                var b = crossing.Movements[second];
                var footA = a.Turn == OmsiMovementTurn.Pedestrian;
                var footB = b.Turn == OmsiMovementTurn.Pedestrian;
                if (footA && footB)
                    continue;
                if (!footA && !footB && a.Approach >= 0 && a.Approach == b.Approach)
                    continue;
                if (footA || footB)
                {
                    // (a footpath's ends lie on the pavement: a lane passing the corner close by does not cross it)
                    if (!Cross(lines[first], lines[second], false, false, out var at))
                        continue;
                    var vehicle = footA ? b : a;
                    conflicts.Add(new OmsiSignalConflict
                    {
                        First = first, Second = second, Kind = OmsiConflictKind.Pedestrian, X = at.X, Y = at.Y,
                        Permitted = vehicle.Turn == OmsiMovementTurn.Left || vehicle.Turn == OmsiMovementTurn.Right
                    });
                    continue;
                }
                OmsiConflictKind kind;
                OmsiMapPoint meet;
                // Movements that run on in a path they share have joined one lane (in OMSI's own crossings chains often do).
                var endA = lines[first].Count > 0 ? lines[first][lines[first].Count - 1] : new OmsiMapPoint(a.ExitX, a.ExitY);
                var endB = lines[second].Count > 0 ? lines[second][lines[second].Count - 1] : new OmsiMapPoint(b.ExitX, b.ExitY);
                if (Distance(endA.X, endA.Y, endB.X, endB.Y) <= SameLaneMeters || governed[first].Intersect(governed[second]).Any())
                {
                    kind = OmsiConflictKind.Merging;
                    meet = MergePoint(sceneryObject, governed[first], governed[second], endA);
                }
                else if (Cross(lines[first], lines[second], false, false, out var crossingPoint))
                {
                    kind = OmsiConflictKind.Crossing;
                    meet = crossingPoint;
                }
                else
                    continue;
                conflicts.Add(new OmsiSignalConflict
                {
                    First = first, Second = second, Kind = kind, X = meet.X, Y = meet.Y, Permitted = GivesWay(crossing, a, b) || GivesWay(crossing, b, a)
                });
            }
            return conflicts;
        }

        /// <summary>
        /// The conflicts a program lets happen: both movements may go (green, or dark) at the same moment. Movements whose
        /// light <paramref name="lightOf"/> gives as -1 have none and are left out.
        /// </summary>
        public static List<OmsiSignalClash> Clashes(OmsiSignalProgram program, IEnumerable<OmsiSignalConflict> conflicts, Func<int, int> lightOf)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            var cycle = program.CycleLength;
            var steps = Math.Max(1, (int)Math.Ceiling(cycle / SampleSeconds));
            var go = new Dictionary<int, bool[]>();
            bool[] GoOf(int light)
            {
                if (!go.TryGetValue(light, out var times))
                {
                    times = new bool[steps];
                    for (var step = 0; step < steps; step++)
                        times[step] = OmsiSignalAspects.AllowsGo(program.StateAt(light, (step + 0.5) * cycle / steps));
                    go[light] = times;
                }
                return times;
            }
            var clashes = new List<OmsiSignalClash>();
            foreach (var conflict in conflicts)
            {
                var first = lightOf(conflict.First);
                var second = lightOf(conflict.Second);
                if (first < 0 || second < 0 || first >= program.Lights.Count || second >= program.Lights.Count)
                    continue;
                var a = GoOf(first);
                var b = GoOf(second);
                var together = 0;
                var at = -1;
                for (var step = 0; step < steps; step++)
                    if (a[step] && b[step])
                    {
                        together++;
                        if (at < 0)
                            at = step;
                    }
                if (together > 0)
                    clashes.Add(new OmsiSignalClash { Conflict = conflict, Seconds = at * cycle / steps, OverlapSeconds = together * cycle / steps, SameLight = first == second });
            }
            return clashes;
        }

        /// <summary>True when the approaches of two vehicle movements face each other.</summary>
        public static bool Opposite(OmsiSignalIntersection crossing, int first, int second)
        {
            if (first < 0 || second < 0 || first == second)
                return false;
            var turn = OmsiRoadPlanner.Normalize(crossing.Approaches[first].InwardDegrees - crossing.Approaches[second].InwardDegrees);
            return Math.Abs(Math.Abs(turn) - 180.0) <= OppositeDegrees;
        }

        /// <summary>
        /// Between approaches facing each other, a turning movement gives way: it crosses or joins the oncoming traffic (a left
        /// turn where traffic keeps right, a right turn where it keeps left), and turns facing each other pass one another.
        /// </summary>
        private static bool GivesWay(OmsiSignalIntersection crossing, OmsiSignalMovement turning, OmsiSignalMovement other)
        {
            return turning.Turn != OmsiMovementTurn.Straight && Opposite(crossing, turning.Approach, other.Approach);
        }

        /// <summary>The paths of a movement up to the next bound path after its stop line, or all without bindings.</summary>
        private static List<int> Governed(OmsiSignalMovement movement, IList<int> pathLights)
        {
            if (pathLights == null)
                return movement.Paths;
            var start = Math.Max(0, movement.Paths.IndexOf(movement.ControlPath));
            for (var index = start + 1; index < movement.Paths.Count; index++)
            {
                var path = movement.Paths[index];
                if (path >= 0 && path < pathLights.Count && pathLights[path] >= 0)
                    return movement.Paths.Take(index).ToList();
            }
            return movement.Paths;
        }

        private static List<OmsiMapPoint> Line(OmsiSceneryObject sceneryObject, IEnumerable<int> paths)
        {
            var line = new List<OmsiMapPoint>();
            foreach (var index in paths)
            {
                if (index < 0 || index >= sceneryObject.Paths.Count)
                    continue;
                var path = sceneryObject.Paths[index];
                var length = path.LengthMeters;
                if (!(length > 0.0) || double.IsInfinity(length))
                    continue;
                var steps = Math.Max(2, (int)Math.Ceiling(length / StepMeters));
                for (var step = line.Count == 0 ? 0 : 1; step <= steps; step++)
                    line.Add(path.PointAt(length * step / steps));
            }
            return line;
        }

        /// <summary>
        /// True when two lines cross away from their ends (a footpath may be crossed anywhere: its ends lie on the pavement).
        /// </summary>
        /// <summary>Where two merging movements meet: the start of the first path they share, or else where the first ends.</summary>
        private static OmsiMapPoint MergePoint(OmsiSceneryObject sceneryObject, List<int> first, List<int> second, OmsiMapPoint end)
        {
            var shared = first.FirstOrDefault(path => second.Contains(path));
            if (first.Any(path => second.Contains(path)) && shared >= 0 && shared < sceneryObject.Paths.Count)
            {
                var path = sceneryObject.Paths[shared];
                return path.PointAt(path.Direction == 1 ? path.LengthMeters : 0.0);
            }
            return end;
        }

        private static bool Cross(List<OmsiMapPoint> first, List<OmsiMapPoint> second, bool firstAnywhere, bool secondAnywhere, out OmsiMapPoint at)
        {
            at = default;
            if (first.Count < 2 || second.Count < 2 || !Overlap(Bounds(first, 0, first.Count), Bounds(second, 0, second.Count)))
                return false;
            // Pieces of a few steps each are passed over at once when their boxes do not meet.
            const int Piece = 8;
            for (var i0 = 0; i0 + 1 < first.Count; i0 += Piece)
            {
                var firstBox = Bounds(first, i0, Math.Min(first.Count, i0 + Piece + 1));
                for (var j0 = 0; j0 + 1 < second.Count; j0 += Piece)
                {
                    if (!Overlap(firstBox, Bounds(second, j0, Math.Min(second.Count, j0 + Piece + 1))))
                        continue;
                    for (var i = i0; i + 1 < first.Count && i < i0 + Piece; i++)
                    for (var j = j0; j + 1 < second.Count && j < j0 + Piece; j++)
                    {
                        if (!Intersect(first[i], first[i + 1], second[j], second[j + 1], out var point))
                            continue;
                        if (!firstAnywhere && NearEnd(first, point))
                            continue;
                        if (!secondAnywhere && NearEnd(second, point))
                            continue;
                        at = point;
                        return true;
                    }
                }
            }
            return false;
        }

        private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(List<OmsiMapPoint> line, int from, int to)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (var index = from; index < to; index++)
            {
                minX = Math.Min(minX, line[index].X);
                minY = Math.Min(minY, line[index].Y);
                maxX = Math.Max(maxX, line[index].X);
                maxY = Math.Max(maxY, line[index].Y);
            }
            return (minX, minY, maxX, maxY);
        }

        private static bool Overlap((double MinX, double MinY, double MaxX, double MaxY) a, (double MinX, double MinY, double MaxX, double MaxY) b)
        {
            return a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinY <= b.MaxY && b.MinY <= a.MaxY;
        }

        private static bool NearEnd(List<OmsiMapPoint> line, OmsiMapPoint point)
        {
            return Distance(point.X, point.Y, line[0].X, line[0].Y) < EndMeters ||
                   Distance(point.X, point.Y, line[line.Count - 1].X, line[line.Count - 1].Y) < EndMeters;
        }

        private static bool Intersect(OmsiMapPoint a, OmsiMapPoint b, OmsiMapPoint c, OmsiMapPoint d, out OmsiMapPoint point)
        {
            point = default;
            var rX = b.X - a.X;
            var rY = b.Y - a.Y;
            var sX = d.X - c.X;
            var sY = d.Y - c.Y;
            var denominator = rX * sY - rY * sX;
            if (Math.Abs(denominator) < 1e-12)
                return false;
            var t = ((c.X - a.X) * sY - (c.Y - a.Y) * sX) / denominator;
            var u = ((c.X - a.X) * rY - (c.Y - a.Y) * rX) / denominator;
            if (t < 0 || t > 1 || u < 0 || u > 1)
                return false;
            point = new OmsiMapPoint(a.X + t * rX, a.Y + t * rY);
            return true;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
        }
    }
}
