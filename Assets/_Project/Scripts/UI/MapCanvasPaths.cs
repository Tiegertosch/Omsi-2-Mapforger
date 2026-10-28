using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The AI paths of the map from above and in the 3D view (ADR 046): roads blue, footpaths green and rails yellow, in the design's colours,
    /// with arrows the way they run when close in. A path with the right of way ([rule] priority above the default) is
    /// drawn wider, one that gives way dashed; ends no other path joins get a red ring.
    /// </summary>
    internal sealed partial class MapCanvasElement
    {
        /// <summary>Paths show closer in than this many metres per pixel; further out they would only blur together.</summary>
        public const float PathMetersPerPixel = 1.5f;

        /// <summary>Arrows along the paths show closer in than this.</summary>
        private const float PathArrowMetersPerPixel = 0.35f;

        private sealed class PathLine
        {
            public Vector2[] Points;
            public Rect Bounds;
            public int Kind;
            public int Direction;
            public bool OpenStart;
            public bool OpenEnd;
            public double Priority;

            // Ground heights under the points for the 3D view, with the terrain version they were read at.
            public float[] Heights;
            public int HeightsVersion;
            public float LowestHeight;
            public float HighestHeight;

            // The points the simplified forms keep, by tolerance, made from the heights above.
            public int[][] Simplified;
        }

        private readonly List<PathLine> pathLines = new List<PathLine>();

        // The signs of the arms of the junction the priority tool works on (ADR 047): where they stand, their levels
        // (0 normal, 1 main road, 2 give way) and the one under the pointer, -1 for none.
        private readonly List<Vector2> priorityHandles = new List<Vector2>();
        private readonly List<float> priorityHeights = new List<float>();
        private readonly List<int> priorityLevels = new List<int>();
        private int priorityHovered = -1;

        // The lane ends of the lanes tool (ADR 048): where they are, what they are (0 a lane in, 1 a lane out, 2 the lane in
        // chosen, 3 a lane out it leads to) and the one under the pointer, -1 for none. Heights are NaN where the ground is meant.
        private readonly List<Vector2> laneEnds = new List<Vector2>();
        private readonly List<float> laneEndHeights = new List<float>();
        private readonly List<int> laneEndStates = new List<int>();
        private int laneEndHovered = -1;
        private readonly bool[] pathKinds = { true, true, true };
        private bool showPaths;

        /// <summary>Shows the AI paths in the view from above.</summary>
        public bool ShowPaths
        {
            get => showPaths;
            set
            {
                showPaths = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Which kinds of path show: roads, footpaths, rails.</summary>
        public void SetPathKinds(bool roads, bool footpaths, bool rails)
        {
            pathKinds[0] = roads;
            pathKinds[1] = footpaths;
            pathKinds[2] = rails;
            MarkDirtyRepaint();
        }

        /// <summary>Takes the AI paths of the loaded map; null removes them.</summary>
        public void SetPaths(IList<OmsiMapPath> paths)
        {
            pathLines.Clear();
            if (paths != null && summary != null)
                foreach (var path in paths)
                {
                    if (path.Points == null || path.Points.Length < 2 || path.Kind < 0 || path.Kind > 2)
                        continue;
                    var points = new Vector2[path.Points.Length];
                    for (var index = 0; index < points.Length; index++)
                        points[index] = WorldToMap(path.Points[index]);
                    pathLines.Add(new PathLine
                    {
                        Points = points, Bounds = Bounds(points), Kind = path.Kind, Direction = path.Direction, OpenStart = path.OpenStart, OpenEnd = path.OpenEnd,
                        Priority = path.Rule("priority") ?? 128.0
                    });
                }
            MarkDirtyRepaint();
        }

        /// <summary>The signs of a junction's arms for the priority tool, as world points with their levels; null clears them.</summary>
        /// <param name="levels">0 normal, 1 main road, 2 give way, by sign.</param>
        /// <param name="heights">Where the signs stand in the 3D view, by sign; null for the ground.</param>
        public void SetPriorityHandles(IList<OmsiMapPoint> points, IList<int> levels, int hovered, IList<double> heights = null)
        {
            priorityHandles.Clear();
            priorityHeights.Clear();
            priorityLevels.Clear();
            if (points != null)
                for (var index = 0; index < points.Count; index++)
                {
                    priorityHandles.Add(WorldToMap(points[index]));
                    priorityHeights.Add(heights != null && index < heights.Count ? (float)heights[index] : float.NaN);
                    priorityLevels.Add(levels[index]);
                }
            priorityHovered = hovered;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Where a handle shows: from above, or with a camera in the 3D view at its height (the ground for NaN), lifted a
        /// little; false when it lies behind the camera.
        /// </summary>
        private bool HandleScreen(Vector2 map, float height, MapOrbitCamera? camera, out Vector2 screen)
        {
            if (!camera.HasValue)
            {
                screen = MapToScreen(map);
                return true;
            }
            return camera.Value.TryProject(new Vector3(map.x, (float.IsNaN(height) ? GroundAt(map) : height) + 0.3f, map.y), out screen);
        }

        /// <summary>A round sign per arm: a yellow diamond for a main road, a red-edged triangle for give way, a grey ring for normal.</summary>
        private void DrawPriorityHandles(Painter2D painter, MapOrbitCamera? camera = null)
        {
            for (var index = 0; index < priorityHandles.Count; index++)
            {
                if (!HandleScreen(priorityHandles[index], priorityHeights[index], camera, out var at))
                    continue;
                var radius = index == priorityHovered ? 16f : 13f;
                painter.fillColor = handleFillColor;
                painter.strokeColor = index == priorityHovered ? splineColor : tileBorderColor;
                painter.lineWidth = 2f;
                painter.lineJoin = LineJoin.Round;
                painter.BeginPath();
                painter.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                painter.Stroke();
                var size = radius * 0.62f;
                if (priorityLevels[index] == 1)
                {
                    painter.fillColor = selectedTileColor;
                    painter.strokeColor = routeColor;
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.MoveTo(at + new Vector2(0f, -size));
                    painter.LineTo(at + new Vector2(size, 0f));
                    painter.LineTo(at + new Vector2(0f, size));
                    painter.LineTo(at + new Vector2(-size, 0f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
                else if (priorityLevels[index] == 2)
                {
                    painter.fillColor = routeColor;
                    painter.strokeColor = selectedObjectColor;
                    painter.lineWidth = 2.5f;
                    painter.BeginPath();
                    painter.MoveTo(at + new Vector2(-size, -size * 0.7f));
                    painter.LineTo(at + new Vector2(size, -size * 0.7f));
                    painter.LineTo(at + new Vector2(0f, size));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
                else
                {
                    painter.strokeColor = wireColor;
                    painter.lineWidth = 2f;
                    painter.BeginPath();
                    painter.Arc(at, size * 0.55f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Stroke();
                }
            }
        }

        /// <summary>The lane ends of a junction for the lanes tool, as world points with what they are; null clears them.</summary>
        /// <param name="states">0 a lane in, 1 a lane out, 2 the lane in chosen, 3 a lane out it leads to, by end.</param>
        /// <param name="heights">Where the ends lie in the 3D view, by end; null for the ground.</param>
        public void SetLaneEnds(IList<OmsiMapPoint> points, IList<int> states, int hovered, IList<double> heights = null)
        {
            laneEnds.Clear();
            laneEndHeights.Clear();
            laneEndStates.Clear();
            if (points != null)
                for (var index = 0; index < points.Count; index++)
                {
                    laneEnds.Add(WorldToMap(points[index]));
                    laneEndHeights.Add(heights != null && index < heights.Count ? (float)heights[index] : float.NaN);
                    laneEndStates.Add(states[index]);
                }
            laneEndHovered = hovered;
            MarkDirtyRepaint();
        }

        /// <summary>Lanes in as filled dots, lanes out as rings; the chosen lane in orange, the lanes out it leads to green.</summary>
        private void DrawLaneEnds(Painter2D painter, MapOrbitCamera? camera = null)
        {
            for (var index = 0; index < laneEnds.Count; index++)
            {
                if (!HandleScreen(laneEnds[index], laneEndHeights[index], camera, out var at))
                    continue;
                var state = laneEndStates[index];
                var radius = index == laneEndHovered ? 9f : 7f;
                var colour = state == 2 ? splineColor : state == 3 ? placementColor : (Color)objectColor;
                painter.lineWidth = state == 1 || state == 3 ? 3f : 2f;
                painter.strokeColor = state == 1 || state == 3 ? colour : state == 2 ? routeColor : handleFillColor;
                painter.fillColor = state == 1 ? handleFillColor : colour;
                painter.BeginPath();
                painter.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                painter.Stroke();
            }
        }

        /// <summary>Centres the view from above on a point in world metres, close enough to see the paths there.</summary>
        public void CenterOnWorld(OmsiMapPoint point)
        {
            center = WorldToMap(point);
            if (metersPerPixel > 0.25f)
                metersPerPixel = 0.25f;
            hasView = true;
            isFitted = false;
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        private Color PathColor(int kind)
        {
            return kind == 0 ? (Color)objectColor : kind == 1 ? treeStrokeColor : selectedTileColor;
        }

        private void DrawPaths(Painter2D painter, Rect visible)
        {
            if (!showPaths || pathLines.Count == 0 || metersPerPixel > PathMetersPerPixel)
                return;
            StrokePaths(painter, line => line.Bounds.Overlaps(visible), null, metersPerPixel, Mathf.Clamp(0.6f / metersPerPixel, 1.2f, 3f),
                metersPerPixel <= PathArrowMetersPerPixel);
        }

        /// <summary>
        /// The paths in the 3D view on the ground around the camera's centre, drawn as from above; they show as far out as
        /// they would from above at the same height.
        /// </summary>
        private void DrawPaths3D(Painter2D painter, MapOrbitCamera camera)
        {
            if (!showPaths || pathLines.Count == 0 || OrbitDistance > PathMetersPerPixel * layout.height)
                return;
            var reach = OrbitDistance * 2f;
            var near = new Rect(OrbitTarget.x - reach, OrbitTarget.y - reach, reach * 2f, reach * 2f);
            StrokePaths(painter, line => line.Bounds.Overlaps(near), camera, 1f / camera.PixelsPerMeter(OrbitDistance), 2f,
                OrbitDistance <= PathArrowMetersPerPixel * layout.height);
        }

        /// <summary>0 for a plain path, 1 for one with the right of way (drawn wider), 2 for one that gives way (dashed).</summary>
        private static int PathLook(PathLine line)
        {
            return line.Priority < 127.5 ? 2 : line.Priority > 128.5 ? 1 : 0;
        }

        // What the last drawing of the paths took, for the report of slow redraws.
        private double pathProjectMilliseconds;
        private int pathShownCount;

        /// <summary>
        /// Draws the paths near enough, cut to the view: by kind with rails first and roads last, so the roads lie on top
        /// where they share a place, each kind in one stroke per look and one for its arrows, since a stroke per path and
        /// per arrow made the view slow with many paths; open ends on top.
        /// </summary>
        /// <param name="metresPerPixel">Metres a pixel shows, in the 3D view at the camera's centre.</param>
        private void StrokePaths(Painter2D painter, Func<PathLine, bool> near, MapOrbitCamera? camera, float metresPerPixel, float width, bool arrows)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            const float margin = 20f;
            var screen = new Rect(-margin, -margin, layout.width + margin * 2f, layout.height + margin * 2f);
            var groundVersion = GroundVersion != null ? GroundVersion() : 0;
            var shown = new List<KeyValuePair<PathLine, List<List<Vector2>>>>();
            foreach (var line in pathLines)
                if (pathKinds[line.Kind] && near(line))
                {
                    var runs = ScreenRuns(line, camera, screen, groundVersion, metresPerPixel);
                    if (runs.Count > 0)
                        shown.Add(new KeyValuePair<PathLine, List<List<Vector2>>>(line, runs));
                }
            pathShownCount = shown.Count;
            pathProjectMilliseconds = watch.Elapsed.TotalMilliseconds;
            painter.lineJoin = LineJoin.Round;
            for (var kind = 2; kind >= 0; kind--)
            {
                painter.strokeColor = PathColor(kind);
                for (var look = 0; look < 3; look++)
                {
                    painter.lineWidth = look == 1 ? width * 2f : width;
                    painter.lineCap = look == 2 ? LineCap.Butt : LineCap.Round;
                    painter.BeginPath();
                    var segments = 0;
                    foreach (var pair in shown)
                    {
                        if (pair.Key.Kind != kind || PathLook(pair.Key) != look)
                            continue;
                        foreach (var run in pair.Value)
                        {
                            if (look == 2)
                                segments += AddDashedPath(painter, run, 6f, 4f);
                            else
                            {
                                painter.MoveTo(run[0]);
                                for (var index = 1; index < run.Count; index++)
                                    painter.LineTo(run[index]);
                                segments += run.Count - 1;
                            }
                            segments = FlushStroke(painter, segments);
                        }
                    }
                    if (segments > 0)
                        painter.Stroke();
                }
                if (!arrows)
                    continue;
                painter.lineWidth = Mathf.Max(1.5f, width);
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                var arrowSegments = 0;
                foreach (var pair in shown)
                    if (pair.Key.Kind == kind)
                        foreach (var run in pair.Value)
                        {
                            arrowSegments += AddPathArrows(painter, run, pair.Key.Direction, 4f + width);
                            arrowSegments = FlushStroke(painter, arrowSegments);
                        }
                if (arrowSegments > 0)
                    painter.Stroke();
            }

            painter.strokeColor = selectedObjectColor;
            painter.lineWidth = 2f;
            foreach (var pair in shown)
            {
                var line = pair.Key;
                foreach (var open in new[] { line.OpenStart ? line.Points[0] : (Vector2?)null, line.OpenEnd ? line.Points[line.Points.Length - 1] : (Vector2?)null })
                {
                    if (!open.HasValue || !HandleScreen(open.Value, float.NaN, camera, out var at) || !screen.Contains(at))
                        continue;
                    painter.BeginPath();
                    painter.Arc(at, 5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Stroke();
                }
            }
        }

        /// <summary>
        /// A line in screen pixels cut to the view: the stretches inside it and before the camera, each of two points or
        /// more, leaving out points closer than 2 pixels to the one before. Cutting keeps a stretch running far off the
        /// view, such as one passing close by the camera, from being dashed and tessellated over thousands of pixels.
        /// In the 3D view the line lies on the ground, with the heights it keeps, and only the points of its simplified
        /// form for <paramref name="metresPerPixel"/> are projected.
        /// </summary>
        private List<List<Vector2>> ScreenRuns(PathLine line, MapOrbitCamera? camera, Rect screen, int groundVersion, float metresPerPixel)
        {
            var runs = new List<List<Vector2>>();
            var points = line.Points;
            var threeDimensional = camera.HasValue;
            var view = camera.GetValueOrDefault();
            float[] heights = null;
            int[] kept = null;
            if (threeDimensional)
            {
                heights = PathHeights(line, groundVersion);
                if (BesideView(line, view, screen))
                    return runs;
                kept = KeptPoints(line, metresPerPixel);
            }
            var count = kept != null ? kept.Length : points.Length;

            bool Project(int place, out Vector2 at)
            {
                var index = kept != null ? kept[place] : place;
                if (!threeDimensional)
                {
                    at = MapToScreen(points[index]);
                    return true;
                }
                return view.TryProject(new Vector3(points[index].x, heights[index] + 0.3f, points[index].y), out at);
            }

            List<Vector2> run = null;
            Vector2? skipped = null;
            var hasLast = Project(0, out var last);
            for (var place = 1; place < count; place++)
            {
                var has = Project(place, out var next);
                if (hasLast && has && ClipSegment(screen, last, next, out var from, out var to, out var cutStart, out var cutEnd))
                {
                    if (run == null || cutStart)
                    {
                        EndRun(run, ref skipped);
                        run = new List<Vector2> { from };
                        runs.Add(run);
                    }
                    if (!cutEnd && place < count - 1 && (to - run[run.Count - 1]).sqrMagnitude < 4f)
                        skipped = to;
                    else
                    {
                        run.Add(to);
                        skipped = null;
                    }
                    if (cutEnd)
                        run = null;
                }
                else
                {
                    EndRun(run, ref skipped);
                    run = null;
                }
                last = next;
                hasLast = has;
            }
            EndRun(run, ref skipped);
            runs.RemoveAll(stretch => stretch.Count < 2);
            return runs;
        }

        /// <summary>
        /// The ground heights under a path's points, kept until the terrain changes, since reading them anew for every
        /// redraw took most of the time of drawing the paths in the 3D view.
        /// </summary>
        private float[] PathHeights(PathLine line, int groundVersion)
        {
            if (line.Heights != null && line.HeightsVersion == groundVersion)
                return line.Heights;
            var heights = new float[line.Points.Length];
            var lowest = float.MaxValue;
            var highest = float.MinValue;
            for (var index = 0; index < heights.Length; index++)
            {
                heights[index] = GroundAt(line.Points[index]);
                lowest = Mathf.Min(lowest, heights[index]);
                highest = Mathf.Max(highest, heights[index]);
            }
            line.Heights = heights;
            line.LowestHeight = lowest;
            line.HighestHeight = highest;
            line.HeightsVersion = groundVersion;
            line.Simplified = null;
            return heights;
        }

        /// <summary>
        /// How far, in metres, the simplified forms of a path may stray from it. Paths have a point every 4 metres even
        /// where straight, far more than the 3D view can show from further out, and projecting them all made it slow.
        /// </summary>
        private static readonly float[] PathTolerances = { 0.15f, 0.6f, 2.5f };

        /// <summary>
        /// The points of the coarsest simplified form of a path that strays less than a pixel at this many metres per
        /// pixel, made when first needed; null for all its points. Needs the path's heights.
        /// </summary>
        private static int[] KeptPoints(PathLine line, float metresPerPixel)
        {
            for (var level = PathTolerances.Length - 1; level >= 0; level--)
            {
                if (PathTolerances[level] > metresPerPixel)
                    continue;
                if (line.Simplified == null)
                    line.Simplified = new int[PathTolerances.Length][];
                return line.Simplified[level] ?? (line.Simplified[level] = Simplify(line.Points, line.Heights, PathTolerances[level]));
            }
            return null;
        }

        /// <summary>The indices of the points of a line on the ground that Douglas-Peucker keeps so it strays no more than the tolerance.</summary>
        private static int[] Simplify(Vector2[] points, float[] heights, float tolerance)
        {
            var keep = new bool[points.Length];
            keep[0] = true;
            keep[points.Length - 1] = true;
            var spans = new Stack<Vector2Int>();
            spans.Push(new Vector2Int(0, points.Length - 1));
            var squared = tolerance * tolerance;
            while (spans.Count > 0)
            {
                var span = spans.Pop();
                var start = new Vector3(points[span.x].x, heights[span.x], points[span.x].y);
                var end = new Vector3(points[span.y].x, heights[span.y], points[span.y].y);
                var farthest = -1;
                var farthestDistance = squared;
                for (var index = span.x + 1; index < span.y; index++)
                {
                    var distance = SquaredDistanceToSegment(new Vector3(points[index].x, heights[index], points[index].y), start, end);
                    if (distance > farthestDistance)
                    {
                        farthest = index;
                        farthestDistance = distance;
                    }
                }
                if (farthest < 0)
                    continue;
                keep[farthest] = true;
                spans.Push(new Vector2Int(span.x, farthest));
                spans.Push(new Vector2Int(farthest, span.y));
            }
            var kept = new List<int>();
            for (var index = 0; index < keep.Length; index++)
                if (keep[index])
                    kept.Add(index);
            return kept.ToArray();
        }

        private static float SquaredDistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            var along = end - start;
            var length = along.sqrMagnitude;
            var share = length > 0f ? Mathf.Clamp01(Vector3.Dot(point - start, along) / length) : 0f;
            return (start + along * share - point).sqrMagnitude;
        }

        /// <summary>
        /// True when the box around a path lies wholly beside the view on one side, so none of it can show; false when
        /// unsure, such as with a corner behind the camera.
        /// </summary>
        private static bool BesideView(PathLine line, MapOrbitCamera camera, Rect screen)
        {
            var left = true;
            var right = true;
            var above = true;
            var below = true;
            for (var corner = 0; corner < 8; corner++)
            {
                var world = new Vector3((corner & 1) == 0 ? line.Bounds.xMin : line.Bounds.xMax,
                    ((corner & 4) == 0 ? line.LowestHeight : line.HighestHeight) + 0.3f, (corner & 2) == 0 ? line.Bounds.yMin : line.Bounds.yMax);
                if (!camera.TryProject(world, out var at))
                    return false;
                left &= at.x < screen.xMin;
                right &= at.x > screen.xMax;
                above &= at.y < screen.yMin;
                below &= at.y > screen.yMax;
            }
            return left || right || above || below;
        }

        /// <summary>Segments in one stroke at most: a stroke is one mesh, which holds no more than 65,535 vertices, and round joins take many.</summary>
        private const int StrokeSegments = 1500;

        /// <summary>Strokes the path drawn so far and begins another once it has <see cref="StrokeSegments"/> segments; the segments left in it.</summary>
        private static int FlushStroke(Painter2D painter, int segments)
        {
            if (segments < StrokeSegments)
                return segments;
            painter.Stroke();
            painter.BeginPath();
            return 0;
        }

        private static void EndRun(List<Vector2> run, ref Vector2? skipped)
        {
            if (run != null && skipped.HasValue)
                run.Add(skipped.Value);
            skipped = null;
        }

        /// <summary>The part of a segment inside a rectangle (Liang-Barsky), and whether its start or end was cut; false when none is.</summary>
        private static bool ClipSegment(Rect rect, Vector2 a, Vector2 b, out Vector2 from, out Vector2 to, out bool cutStart, out bool cutEnd)
        {
            var enter = 0f;
            var leave = 1f;
            var delta = b - a;
            var inside = ClipEdge(-delta.x, a.x - rect.xMin, ref enter, ref leave) && ClipEdge(delta.x, rect.xMax - a.x, ref enter, ref leave) &&
                         ClipEdge(-delta.y, a.y - rect.yMin, ref enter, ref leave) && ClipEdge(delta.y, rect.yMax - a.y, ref enter, ref leave);
            from = a + delta * enter;
            to = a + delta * leave;
            cutStart = enter > 0f;
            cutEnd = leave < 1f;
            return inside;
        }

        private static bool ClipEdge(float direction, float distance, ref float enter, ref float leave)
        {
            if (Mathf.Abs(direction) < 1e-6f)
                return distance >= 0f;
            var at = distance / direction;
            if (direction < 0f)
            {
                if (at > leave)
                    return false;
                enter = Mathf.Max(enter, at);
            }
            else
            {
                if (at < enter)
                    return false;
                leave = Mathf.Min(leave, at);
            }
            return true;
        }

        /// <summary>
        /// Adds chevrons along a path the way it runs, about every 120 pixels, both ways for a path used both ways, to the
        /// path being drawn; the segments added, none when the path is too short for one.
        /// </summary>
        private static int AddPathArrows(Painter2D painter, List<Vector2> screen, int direction, float size)
        {
            var length = 0f;
            for (var index = 1; index < screen.Count; index++)
                length += Vector2.Distance(screen[index - 1], screen[index]);
            if (length < 30f)
                return 0;
            var segments = 0;
            var count = Mathf.Max(1, Mathf.RoundToInt(length / 120f));
            for (var arrow = 0; arrow < count; arrow++)
            {
                var at = length * (arrow + 0.5f) / count;
                var travelled = 0f;
                for (var index = 1; index < screen.Count; index++)
                {
                    var step = Vector2.Distance(screen[index - 1], screen[index]);
                    if (travelled + step < at || step < 0.01f)
                    {
                        travelled += step;
                        continue;
                    }
                    var along = (screen[index] - screen[index - 1]) / step;
                    var point = screen[index - 1] + along * (at - travelled);
                    foreach (var way in direction == 2 ? new[] { 1f, -1f } : new[] { direction == 1 ? -1f : 1f })
                    {
                        var forward = along * way;
                        var side = new Vector2(-forward.y, forward.x);
                        var tip = point + forward * (size * (direction == 2 ? 1.6f : 0.5f));
                        painter.MoveTo(tip - forward * size + side * size * 0.7f);
                        painter.LineTo(tip);
                        painter.LineTo(tip - forward * size - side * size * 0.7f);
                        segments += 2;
                    }
                    break;
                }
            }
            return segments;
        }
    }
}
