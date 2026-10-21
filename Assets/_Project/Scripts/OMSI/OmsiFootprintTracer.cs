using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Traces the area that triangles cover seen from above. The triangles are drawn into a fine grid, the borders of
    /// the covered cells are followed into closed contours and the cell steps are smoothed away. Unlike a convex hull
    /// this keeps L-shaped buildings, the arms of crossings and courtyards.
    /// </summary>
    public static class OmsiFootprintTracer
    {
        /// <summary>Finest cell edge in metres; larger objects use coarser cells to stay within the grid limit.</summary>
        public const double FinestCellSize = 0.2;

        private const int MaxCellsPerSide = 1000;

        /// <summary>
        /// Contours of the covered area: outer contours counter-clockwise, holes clockwise. Parts smaller than
        /// <paramref name="minimumPartArea"/> square metres are dropped unless nothing else is left.
        /// </summary>
        /// <param name="corners">Three points per triangle, in order.</param>
        public static List<OmsiMapPoint[]> Trace(IReadOnlyList<OmsiMapPoint> corners, double minimumPartArea = 0.5)
        {
            var contours = new List<OmsiMapPoint[]>();
            if (corners == null || corners.Count < 3)
                return contours;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var point in corners)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            var cell = Math.Max(FinestCellSize, Math.Max(maxX - minX, maxY - minY) / (MaxCellsPerSide - 2));
            var grid = new Grid(minX - cell, minY - cell, cell,
                (int)Math.Ceiling((maxX - minX) / cell) + 3, (int)Math.Ceiling((maxY - minY) / cell) + 3);
            for (var index = 0; index + 2 < corners.Count; index += 3)
                grid.Fill(corners[index], corners[index + 1], corners[index + 2]);

            var loops = grid.TraceLoops();
            var largest = 0.0;
            foreach (var loop in loops)
                largest = Math.Max(largest, SignedArea(loop));

            foreach (var loop in loops)
            {
                var area = SignedArea(loop);
                // Keep every part when they are all small, for example a sign or a bollard.
                if (Math.Abs(area) < minimumPartArea && largest >= minimumPartArea)
                    continue;
                // One cell smooths the steps of diagonal edges away.
                var simplified = Simplify(loop, cell);
                if (simplified.Length >= 3)
                    contours.Add(simplified);
            }
            return contours;
        }

        public static double SignedArea(IReadOnlyList<OmsiMapPoint> ring)
        {
            var area = 0.0;
            for (int index = 0, previous = ring.Count - 1; index < ring.Count; previous = index++)
                area += ring[previous].X * ring[index].Y - ring[index].X * ring[previous].Y;
            return area * 0.5;
        }

        /// <summary>Douglas-Peucker for a closed ring, split at the two points farthest apart.</summary>
        private static OmsiMapPoint[] Simplify(List<OmsiMapPoint> ring, double tolerance)
        {
            if (ring.Count <= 4)
                return ring.ToArray();

            var far = 0;
            var farthest = -1.0;
            for (var index = 1; index < ring.Count; index++)
            {
                var dx = ring[index].X - ring[0].X;
                var dy = ring[index].Y - ring[0].Y;
                if (dx * dx + dy * dy > farthest)
                {
                    farthest = dx * dx + dy * dy;
                    far = index;
                }
            }

            var keep = new bool[ring.Count];
            keep[0] = true;
            keep[far] = true;
            MarkKept(ring, 0, far, tolerance, keep);
            MarkKept(ring, far, ring.Count, tolerance, keep);

            var result = new List<OmsiMapPoint>();
            for (var index = 0; index < ring.Count; index++)
                if (keep[index])
                    result.Add(ring[index]);
            return result.ToArray();
        }

        /// <summary>Keeps the points between first and last (exclusive; last may be the ring length) that matter.</summary>
        private static void MarkKept(List<OmsiMapPoint> ring, int first, int last, double tolerance, bool[] keep)
        {
            var stack = new Stack<KeyValuePair<int, int>>();
            stack.Push(new KeyValuePair<int, int>(first, last));
            while (stack.Count > 0)
            {
                var span = stack.Pop();
                var start = ring[span.Key];
                var end = ring[span.Value % ring.Count];
                var worst = -1;
                var worstDistance = tolerance;
                for (var index = span.Key + 1; index < span.Value; index++)
                {
                    var distance = DistanceToSegment(ring[index], start, end);
                    if (distance > worstDistance)
                    {
                        worstDistance = distance;
                        worst = index;
                    }
                }
                if (worst < 0)
                    continue;
                keep[worst] = true;
                stack.Push(new KeyValuePair<int, int>(span.Key, worst));
                stack.Push(new KeyValuePair<int, int>(worst, span.Value));
            }
        }

        private static double DistanceToSegment(OmsiMapPoint point, OmsiMapPoint start, OmsiMapPoint end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared > 0 ? ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared : 0;
            t = Math.Max(0, Math.Min(1, t));
            var x = start.X + t * dx - point.X;
            var y = start.Y + t * dy - point.Y;
            return Math.Sqrt(x * x + y * y);
        }

        /// <summary>Cells covered by triangles; cell (x, y) spans [x, x + 1] × [y, y + 1] in grid units, y up.</summary>
        private sealed class Grid
        {
            private readonly double originX;
            private readonly double originY;
            private readonly double cell;
            private readonly int width;
            private readonly int height;
            private readonly bool[] covered;

            public Grid(double originX, double originY, double cell, int width, int height)
            {
                this.originX = originX;
                this.originY = originY;
                this.cell = cell;
                this.width = width;
                this.height = height;
                covered = new bool[width * height];
            }

            private bool IsCovered(int x, int y)
            {
                return x >= 0 && y >= 0 && x < width && y < height && covered[y * width + x];
            }

            /// <summary>
            /// Covers the cells whose centre lies in the triangle. Triangles thinner than a cell, such as walls seen
            /// from above, would cover no centre; they cover the cells along their edges instead.
            /// </summary>
            public void Fill(OmsiMapPoint a, OmsiMapPoint b, OmsiMapPoint c)
            {
                double ax = (a.X - originX) / cell, ay = (a.Y - originY) / cell;
                double bx = (b.X - originX) / cell, by = (b.Y - originY) / cell;
                double cx = (c.X - originX) / cell, cy = (c.Y - originY) / cell;
                var doubleArea = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                var longest = Math.Max(Math.Max(Length(ax, ay, bx, by), Length(bx, by, cx, cy)), Length(cx, cy, ax, ay));
                // Width across the longest edge, in cells.
                if (longest < 1e-9 || Math.Abs(doubleArea) / longest < 1.0)
                {
                    FillSegment(ax, ay, bx, by);
                    FillSegment(bx, by, cx, cy);
                    FillSegment(cx, cy, ax, ay);
                    return;
                }

                var sign = doubleArea > 0 ? 1.0 : -1.0;
                var x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
                var x1 = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                var y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
                var y1 = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                for (var y = y0; y <= y1; y++)
                {
                    var py = y + 0.5;
                    for (var x = x0; x <= x1; x++)
                    {
                        var px = x + 0.5;
                        // Centres on an edge count, so neighbouring triangles leave no gap between them.
                        if (sign * ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) >= 0 &&
                            sign * ((cx - bx) * (py - by) - (cy - by) * (px - bx)) >= 0 &&
                            sign * ((ax - cx) * (py - cy) - (ay - cy) * (px - cx)) >= 0)
                            covered[y * width + x] = true;
                    }
                }
            }

            private static double Length(double x0, double y0, double x1, double y1)
            {
                return Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            }

            private void FillSegment(double x0, double y0, double x1, double y1)
            {
                var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2));
                for (var step = 0; step <= steps; step++)
                {
                    var t = (double)step / steps;
                    var x = (int)Math.Floor(x0 + (x1 - x0) * t);
                    var y = (int)Math.Floor(y0 + (y1 - y0) * t);
                    if (x >= 0 && y >= 0 && x < width && y < height)
                        covered[y * width + x] = true;
                }
            }

            /// <summary>
            /// Follows the borders between covered and empty cells with the covered side on the left, so outer
            /// contours run counter-clockwise and holes clockwise. Returns the corner points in metres.
            /// </summary>
            public List<List<OmsiMapPoint>> TraceLoops()
            {
                // Directed border edges; at most two leave a grid vertex (where two covered cells touch diagonally).
                var starts = new List<int>();
                var directions = new List<int>();
                var outgoing = new Dictionary<int, int>();
                var secondOutgoing = new Dictionary<int, int>();
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (!covered[y * width + x])
                            continue;
                        if (!IsCovered(x, y - 1))
                            AddEdge(x, y, 0, starts, directions, outgoing, secondOutgoing);
                        if (!IsCovered(x + 1, y))
                            AddEdge(x + 1, y, 1, starts, directions, outgoing, secondOutgoing);
                        if (!IsCovered(x, y + 1))
                            AddEdge(x + 1, y + 1, 2, starts, directions, outgoing, secondOutgoing);
                        if (!IsCovered(x - 1, y))
                            AddEdge(x, y + 1, 3, starts, directions, outgoing, secondOutgoing);
                    }
                }

                var used = new bool[starts.Count];
                var loops = new List<List<OmsiMapPoint>>();
                for (var first = 0; first < starts.Count; first++)
                {
                    if (used[first])
                        continue;
                    var loop = new List<OmsiMapPoint>();
                    var edge = first;
                    while (!used[edge])
                    {
                        used[edge] = true;
                        var direction = directions[edge];
                        var end = EndVertex(starts[edge], direction);
                        var next = NextEdge(end, direction, used, directions, outgoing, secondOutgoing);
                        // The loop closes at the first edge, which is already used.
                        var following = next >= 0 ? next : first;
                        // Only corners are kept; straight runs along the grid add nothing.
                        if (directions[following] != direction)
                            loop.Add(new OmsiMapPoint(originX + (end % (width + 1)) * cell, originY + (end / (width + 1)) * cell));
                        if (next < 0)
                            break;
                        edge = next;
                    }
                    if (loop.Count >= 3)
                        loops.Add(loop);
                }
                return loops;
            }

            // Directions: 0 = +x, 1 = +y, 2 = -x, 3 = -y.
            private void AddEdge(int x, int y, int direction, List<int> starts, List<int> directions,
                Dictionary<int, int> outgoing, Dictionary<int, int> secondOutgoing)
            {
                var vertex = y * (width + 1) + x;
                if (outgoing.ContainsKey(vertex))
                    secondOutgoing[vertex] = starts.Count;
                else
                    outgoing[vertex] = starts.Count;
                starts.Add(vertex);
                directions.Add(direction);
            }

            private int EndVertex(int start, int direction)
            {
                switch (direction)
                {
                    case 0: return start + 1;
                    case 1: return start + width + 1;
                    case 2: return start - 1;
                    default: return start - (width + 1);
                }
            }

            /// <summary>
            /// The unused edge leaving a vertex. Where two edges leave, the left turn wins, which keeps cells that only
            /// touch at a corner in separate contours.
            /// </summary>
            private static int NextEdge(int vertex, int arrivingDirection, bool[] used, List<int> directions,
                Dictionary<int, int> outgoing, Dictionary<int, int> secondOutgoing)
            {
                var first = outgoing.TryGetValue(vertex, out var a) && !used[a] ? a : -1;
                var second = secondOutgoing.TryGetValue(vertex, out var b) && !used[b] ? b : -1;
                if (first < 0 || second < 0)
                    return first >= 0 ? first : second;
                var left = (arrivingDirection + 1) % 4;
                return directions[first] == left ? first : second;
            }
        }
    }
}
