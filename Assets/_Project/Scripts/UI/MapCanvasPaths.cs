using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The AI paths of the map from above (ADR 046): roads blue, footpaths green and rails yellow, in the design's colours,
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
        }

        private readonly List<PathLine> pathLines = new List<PathLine>();

        // The signs of the arms of the junction the priority tool works on (ADR 047): where they stand, their levels
        // (0 normal, 1 main road, 2 give way) and the one under the pointer, -1 for none.
        private readonly List<Vector2> priorityHandles = new List<Vector2>();
        private readonly List<int> priorityLevels = new List<int>();
        private int priorityHovered = -1;
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
        public void SetPriorityHandles(IList<OmsiMapPoint> points, IList<int> levels, int hovered)
        {
            priorityHandles.Clear();
            priorityLevels.Clear();
            if (points != null)
                for (var index = 0; index < points.Count; index++)
                {
                    priorityHandles.Add(WorldToMap(points[index]));
                    priorityLevels.Add(levels[index]);
                }
            priorityHovered = hovered;
            MarkDirtyRepaint();
        }

        /// <summary>A round sign per arm: a yellow diamond for a main road, a red-edged triangle for give way, a grey ring for normal.</summary>
        private void DrawPriorityHandles(Painter2D painter)
        {
            for (var index = 0; index < priorityHandles.Count; index++)
            {
                var at = MapToScreen(priorityHandles[index]);
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
            var width = Mathf.Clamp(0.6f / metersPerPixel, 1.2f, 3f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            // Rails first, roads last, so the roads lie on top where they share a place.
            for (var kind = 2; kind >= 0; kind--)
            {
                if (!pathKinds[kind])
                    continue;
                painter.strokeColor = PathColor(kind);
                foreach (var line in pathLines)
                {
                    if (line.Kind != kind || !line.Bounds.Overlaps(visible))
                        continue;
                    var screen = new List<Vector2>(line.Points.Length);
                    foreach (var point in line.Points)
                        screen.Add(MapToScreen(point));
                    painter.lineWidth = line.Priority > 128.5 ? width * 2f : width;
                    painter.BeginPath();
                    if (line.Priority < 127.5)
                        AddDashedPath(painter, screen, 6f, 4f);
                    else
                    {
                        painter.MoveTo(screen[0]);
                        for (var index = 1; index < screen.Count; index++)
                            painter.LineTo(screen[index]);
                    }
                    painter.Stroke();
                    if (metersPerPixel <= PathArrowMetersPerPixel)
                        DrawPathArrows(painter, screen, line.Direction, width);
                }
            }

            // Open ends on top of everything.
            painter.strokeColor = selectedObjectColor;
            painter.lineWidth = 2f;
            foreach (var line in pathLines)
            {
                if (!pathKinds[line.Kind] || !(line.OpenStart || line.OpenEnd) || !line.Bounds.Overlaps(visible))
                    continue;
                foreach (var open in new[] { line.OpenStart ? line.Points[0] : (Vector2?)null, line.OpenEnd ? line.Points[line.Points.Length - 1] : (Vector2?)null })
                {
                    if (!open.HasValue || !visible.Contains(open.Value))
                        continue;
                    painter.BeginPath();
                    painter.Arc(MapToScreen(open.Value), 5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Stroke();
                }
            }
        }

        /// <summary>Chevrons along a path the way it runs, about every 120 pixels; both ways for a path used both ways.</summary>
        private static void DrawPathArrows(Painter2D painter, List<Vector2> screen, int direction, float width)
        {
            var length = 0f;
            for (var index = 1; index < screen.Count; index++)
                length += Vector2.Distance(screen[index - 1], screen[index]);
            if (length < 30f)
                return;
            var count = Mathf.Max(1, Mathf.RoundToInt(length / 120f));
            var size = 4f + width;
            painter.lineWidth = Mathf.Max(1.5f, width);
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
                        painter.BeginPath();
                        painter.MoveTo(tip - forward * size + side * size * 0.7f);
                        painter.LineTo(tip);
                        painter.LineTo(tip - forward * size - side * size * 0.7f);
                        painter.Stroke();
                    }
                    break;
                }
            }
        }
    }
}
