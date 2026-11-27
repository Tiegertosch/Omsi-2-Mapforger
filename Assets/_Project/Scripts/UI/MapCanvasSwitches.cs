using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The branches of a switch object (ADR 105, M5), shown only while one is selected and, as small marks, where a signal route chosen passes a
    /// switch. Each branch is drawn as a line in the colour of its switch position with the position written at its middle; a path without a
    /// position is the plain continuation and carries no number.
    /// </summary>
    internal sealed partial class MapCanvasElement
    {
        private sealed class SwitchBranch
        {
            public readonly List<Vector2[]> Line = new List<Vector2[]>();
            public Vector2 Middle;
            public Vector2 Direction;
            public int? Position;

            // Where the plaque of the position sits: on segment AnchorSegment at AnchorT, where the branch is farthest from the others;
            // AnchorAway points from there away from the nearest other branch (zero without another branch).
            public int AnchorSegment;
            public float AnchorT = 0.5f;
            public Vector2 Anchor;
            public Vector2 AnchorDirection;
            public Vector2 AnchorAway;

            // The points in the owner's object frame for the 3D view; null draws them on the ground.
            public Vector3[] Local;
        }

        private OmsiObjectRecord switchBranchOwner;

        private struct SwitchMark
        {
            public Vector2 At;
            public Vector2 Direction;
            public int Position;
            public string Text;
        }

        private const float SwitchPlaqueHeight = 18f;
        private const float SwitchPlaqueCharacterPixels = 7.4f;
        private static readonly Color SwitchPlaqueText = new Color(0.93f, 0.95f, 0.97f, 1f);

        private readonly List<SwitchBranch> switchBranches = new List<SwitchBranch>();
        private readonly List<SwitchMark> routeSwitchMarks = new List<SwitchMark>();
        // Kept between redraws so drawing the texts allocates nothing.
        private readonly List<Rect> switchPlaquesPlaced = new List<Rect>();
        private readonly List<Vector2> switchPlaquesAvoid = new List<Vector2>();

        /// <summary>
        /// The paths of the selected switch object with the position of each (null for none); null or empty removes them. With the object and
        /// the paths in its frame (x right, y up, z forward; one array per branch) the 3D view draws them in the frame the model is drawn in.
        /// </summary>
        public void SetSwitchBranches(IEnumerable<KeyValuePair<OmsiMapPoint[], int?>> branches, OmsiObjectRecord owner = null, IList<Vector3[]> local = null)
        {
            switchBranches.Clear();
            switchBranchOwner = owner;
            var branchIndex = -1;
            if (branches != null && summary != null)
                foreach (var pair in branches)
                {
                    branchIndex++;
                    if (pair.Key == null || pair.Key.Length < 2)
                        continue;
                    var map = new Vector2[pair.Key.Length];
                    for (var index = 0; index < map.Length; index++)
                        map[index] = WorldToMap(pair.Key[index]);
                    var middle = map.Length / 2;
                    var direction = map[Mathf.Min(middle + 1, map.Length - 1)] - map[Mathf.Max(middle - 1, 0)];
                    var branch = new SwitchBranch { Position = pair.Value, Middle = map[middle], Direction = direction,
                        Local = local != null && branchIndex < local.Count && local[branchIndex] != null && local[branchIndex].Length == map.Length ? local[branchIndex] : null };
                    branch.Line.Add(map);
                    switchBranches.Add(branch);
                }
            foreach (var branch in switchBranches)
                PlaceSwitchAnchor(branch);
            MarkDirtyRepaint();
        }

        private const float SwitchAnchorFrom = 0.55f;
        private const float SwitchAnchorTo = 0.9f;
        private const float SwitchAnchorFallback = 0.8f;

        private static float DistanceToLine(Vector2 point, Vector2[] line, out Vector2 nearest)
        {
            var best = float.MaxValue;
            nearest = line[0];
            for (var index = 0; index + 1 < line.Length; index++)
            {
                var a = line[index];
                var ab = line[index + 1] - a;
                var length = ab.sqrMagnitude;
                var t = length > 1e-9f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / length) : 0f;
                var candidate = a + ab * t;
                var distance = (point - candidate).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = candidate;
                }
            }
            return Mathf.Sqrt(best);
        }

        private static void LocateOnLine(Vector2[] line, float distance, out int segment, out float t)
        {
            segment = 0;
            t = 0f;
            for (var index = 0; index + 1 < line.Length; index++)
            {
                var length = (line[index + 1] - line[index]).magnitude;
                segment = index;
                if (distance <= length || index + 2 == line.Length)
                {
                    t = length > 1e-9f ? Mathf.Clamp01(distance / length) : 0f;
                    return;
                }
                distance -= length;
            }
        }

        /// <summary>Finds the point of a branch, between 55 % and 90 % of its length, with the largest distance to the other branches (80 % when it has no other).</summary>
        private void PlaceSwitchAnchor(SwitchBranch branch)
        {
            var line = branch.Line[0];
            var total = 0f;
            for (var index = 0; index + 1 < line.Length; index++)
                total += (line[index + 1] - line[index]).magnitude;
            var hasOthers = switchBranches.Count > 1;
            var bestFraction = SwitchAnchorFallback;
            if (hasOthers && total > 1e-6f)
            {
                var bestDistance = -1f;
                const int steps = 14;
                for (var step = 0; step <= steps; step++)
                {
                    var fraction = Mathf.Lerp(SwitchAnchorFrom, SwitchAnchorTo, step / (float)steps);
                    LocateOnLine(line, total * fraction, out var segment, out var t);
                    var point = Vector2.Lerp(line[segment], line[segment + 1], t);
                    var nearestDistance = float.MaxValue;
                    foreach (var other in switchBranches)
                        if (other != branch)
                            nearestDistance = Mathf.Min(nearestDistance, DistanceToLine(point, other.Line[0], out _));
                    if (nearestDistance > bestDistance + 1e-4f)
                    {
                        bestDistance = nearestDistance;
                        bestFraction = fraction;
                    }
                }
            }
            LocateOnLine(line, total * bestFraction, out branch.AnchorSegment, out branch.AnchorT);
            branch.Anchor = Vector2.Lerp(line[branch.AnchorSegment], line[branch.AnchorSegment + 1], branch.AnchorT);
            branch.AnchorDirection = line[branch.AnchorSegment + 1] - line[branch.AnchorSegment];
            branch.AnchorAway = Vector2.zero;
            var nearestOther = float.MaxValue;
            foreach (var other in switchBranches)
            {
                if (other == branch)
                    continue;
                var distance = DistanceToLine(branch.Anchor, other.Line[0], out var nearest);
                if (distance < nearestOther && distance > 1e-5f)
                {
                    nearestOther = distance;
                    branch.AnchorAway = branch.Anchor - nearest;
                }
            }
        }

        /// <summary>The places where the route chosen passes a switch with the position it needs there, and a second point of each branch for its direction; null or empty removes them.</summary>
        public void SetRouteSwitchMarks(IEnumerable<KeyValuePair<OmsiMapPoint, int>> marks, IEnumerable<OmsiMapPoint> ahead = null)
        {
            routeSwitchMarks.Clear();
            if (marks != null && summary != null)
            {
                var aheadList = ahead != null ? new List<OmsiMapPoint>(ahead) : new List<OmsiMapPoint>();
                var index = 0;
                foreach (var pair in marks)
                {
                    var at = WorldToMap(pair.Key);
                    var direction = index < aheadList.Count ? WorldToMap(aheadList[index]) - at : Vector2.right;
                    routeSwitchMarks.Add(new SwitchMark { At = at, Direction = direction, Position = pair.Value,
                        Text = Loc.T("sigroute.switch_mark", ("position", pair.Value)) });
                    index++;
                }
            }
            MarkDirtyRepaint();
        }

        private Color SwitchPositionColor(int? position)
        {
            if (!position.HasValue)
                return pathRailColor;
            switch (position.Value % 3)
            {
                case 0: return placementColor;
                case 1: return (Color)objectColor;
                default: return splineColor;
            }
        }

        private void DrawSwitchBranches(Painter2D painter, MapOrbitCamera? camera = null)
        {
            if (switchBranches.Count == 0)
                return;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            var halo = (Color)routeColor;
            halo.a = 0.55f;
            if (camera.HasValue && SwitchFrame(out var frame))
            {
                foreach (var branch in switchBranches)
                    StrokeSwitchBranch3D(painter, branch, frame, camera.Value, halo, 9f);
                foreach (var branch in switchBranches)
                    StrokeSwitchBranch3D(painter, branch, frame, camera.Value, SwitchPositionColor(branch.Position), 5f);
                painter.lineCap = LineCap.Butt;
                return;
            }
            foreach (var branch in switchBranches)
                StrokeRouteLines(painter, branch.Line, halo, 9f, camera.HasValue, camera);
            foreach (var branch in switchBranches)
                StrokeRouteLines(painter, branch.Line, SwitchPositionColor(branch.Position), 5f, camera.HasValue, camera);
            painter.lineCap = LineCap.Butt;
        }

        /// <summary>The frame the switch model is drawn in now, a drag included; false without one, when the branches lie on the ground.</summary>
        private bool SwitchFrame(out Matrix4x4 frame)
        {
            frame = Matrix4x4.identity;
            return switchBranchOwner != null && ObjectRenderer != null && ObjectRenderer.TryGetPlacement(switchBranchOwner, out frame, out _);
        }

        /// <summary>A point of a branch on the screen of the 3D view: in the model's frame when there is one, else on the ground.</summary>
        private bool SwitchPointScreen(SwitchBranch branch, int index, bool hasFrame, Matrix4x4 frame, MapOrbitCamera camera, out Vector2 screen)
        {
            if (hasFrame && branch.Local != null)
            {
                var at = frame.MultiplyPoint3x4(branch.Local[index]);
                at.y += 0.3f;
                return camera.TryProject(at, out screen);
            }
            return HandleScreen(branch.Line[0][index], float.NaN, camera, out screen);
        }

        private void StrokeSwitchBranch3D(Painter2D painter, SwitchBranch branch, Matrix4x4 frame, MapOrbitCamera camera, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            var started = false;
            var segments = 0;
            var line = branch.Line[0];
            for (var index = 0; index < line.Length; index++)
            {
                if (!SwitchPointScreen(branch, index, true, frame, camera, out var at))
                {
                    started = false;
                    continue;
                }
                if (!started)
                    painter.MoveTo(at);
                else
                {
                    painter.LineTo(at);
                    segments++;
                }
                started = true;
            }
            if (segments > 0)
                painter.Stroke();
        }

        /// <summary>The position of each branch on a plaque in the 3D view, at the projected middle of the branch; the route marks stay from above.</summary>
        private void DrawSwitchTexts3D(MeshGenerationContext context)
        {
            if (switchBranches.Count == 0 || ObjectRenderer == null)
                return;
            var camera = OrbitCamera();
            var hasFrame = SwitchFrame(out var frame);
            switchPlaquesPlaced.Clear();
            switchPlaquesAvoid.Clear();
            foreach (var branch in switchBranches)
            {
                if (!branch.Position.HasValue)
                    continue;
                var segment = branch.AnchorSegment;
                if (!SwitchPointScreen(branch, segment, hasFrame, frame, camera, out var from) ||
                    !SwitchPointScreen(branch, segment + 1, hasFrame, frame, camera, out var to))
                    continue;
                var at = Vector2.Lerp(from, to, branch.AnchorT);
                var direction = to - from;
                // The side away from the other branches, found on the ground and taken to the screen.
                var away = Vector2.zero;
                if (branch.AnchorAway.sqrMagnitude > 0f &&
                    HandleScreen(branch.Anchor, float.NaN, camera, out var groundAt) &&
                    HandleScreen(branch.Anchor + branch.AnchorAway.normalized, float.NaN, camera, out var groundOut))
                    away = groundOut - groundAt;
                // WriteSwitchPlaque takes directions with the map's y up.
                WriteSwitchPlaque(context, context.painter2D, switchPlaquesPlaced, switchPlaquesAvoid, at, new Vector2(direction.x, -direction.y),
                    branch.Position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new Vector2(away.x, -away.y), SwitchPositionColor(branch.Position));
            }
        }

        /// <summary>The marks of a route over switches: a ring each; the text comes with <see cref="DrawSwitchTexts"/>.</summary>
        private void DrawRouteSwitchMarks(Painter2D painter, MapOrbitCamera? camera = null)
        {
            if (routeSwitchMarks.Count == 0)
                return;
            painter.lineWidth = 3f;
            painter.strokeColor = routeColor;
            painter.fillColor = splineColor;
            foreach (var mark in routeSwitchMarks)
            {
                if (!HandleScreen(mark.At, float.NaN, camera, out var at))
                    continue;
                painter.BeginPath();
                painter.Arc(at, 5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                painter.Stroke();
            }
        }

        /// <summary>
        /// The position of each branch and "switch → n" at the switches of a route, each on a dark plaque with light text. A plaque sits beside
        /// the track, not on it, so it leaves the signals on the track in view; one that would cover another plaque or a signal mark goes to
        /// the other side of the track. From above only.
        /// </summary>
        private void DrawSwitchTexts(MeshGenerationContext context)
        {
            if (switchBranches.Count == 0 && routeSwitchMarks.Count == 0)
                return;
            var painter = context.painter2D;
            var placed = switchPlaquesPlaced;
            var avoid = switchPlaquesAvoid;
            placed.Clear();
            avoid.Clear();
            if (signalRouteSignal.HasValue)
                avoid.Add(MapToScreen(signalRouteSignal.Value));
            if (signalRouteDistant.HasValue)
                avoid.Add(MapToScreen(signalRouteDistant.Value));
            if (signalRouteNext.HasValue)
                avoid.Add(MapToScreen(signalRouteNext.Value));
            foreach (var branch in switchBranches)
                if (branch.Position.HasValue)
                    WriteSwitchPlaque(context, painter, placed, avoid, MapToScreen(branch.Anchor), branch.AnchorDirection,
                        branch.Position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), branch.AnchorAway, SwitchPositionColor(branch.Position));
            foreach (var mark in routeSwitchMarks)
                WriteSwitchPlaque(context, painter, placed, avoid, MapToScreen(mark.At), mark.Direction, mark.Text);
        }

        private static bool SwitchPlaqueBlocked(Rect area, List<Rect> placed, List<Vector2> avoid)
        {
            foreach (var other in placed)
                if (other.Overlaps(area))
                    return true;
            var grown = new Rect(area.x - 12f, area.y - 12f, area.width + 24f, area.height + 24f);
            foreach (var point in avoid)
                if (grown.Contains(point))
                    return true;
            return false;
        }

        private void WriteSwitchPlaque(MeshGenerationContext context, Painter2D painter, List<Rect> placed, List<Vector2> avoid, Vector2 screen, Vector2 mapDirection, string text, Vector2 awayMap = default, Color? border = null)
        {
            if (!new Rect(-60f, -20f, layout.width + 120f, layout.height + 40f).Contains(screen))
                return;
            // The map's y points up and the screen's down.
            var direction = new Vector2(mapDirection.x, -mapDirection.y);
            direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            var normal = new Vector2(-direction.y, direction.x);
            var size = new Vector2(text.Length * SwitchPlaqueCharacterPixels + 12f, SwitchPlaqueHeight);
            var reach = 12f + Mathf.Abs(normal.x) * size.x * 0.5f + Mathf.Abs(normal.y) * size.y * 0.5f;
            // Outer side first: the side facing away from the other branches.
            if (awayMap.sqrMagnitude > 1e-12f && Vector2.Dot(normal, new Vector2(awayMap.x, -awayMap.y)) < 0f)
                normal = -normal;
            var first = screen + normal * reach;
            var second = screen - normal * reach;
            var chosen = new Rect(first.x - size.x * 0.5f, first.y - size.y * 0.5f, size.x, size.y);
            if (SwitchPlaqueBlocked(chosen, placed, avoid))
            {
                var other = new Rect(second.x - size.x * 0.5f, second.y - size.y * 0.5f, size.x, size.y);
                var chosenOverlaps = false;
                foreach (var item in placed)
                    if (item.Overlaps(chosen))
                    {
                        chosenOverlaps = true;
                        break;
                    }
                if (!SwitchPlaqueBlocked(other, placed, avoid) || chosenOverlaps)
                    chosen = other;
            }
            placed.Add(chosen);

            const float radius = 5f;
            painter.fillColor = new Color(stopOutlineColor.r, stopOutlineColor.g, stopOutlineColor.b, 0.94f);
            painter.strokeColor = border ?? (Color)routeColor;
            painter.lineWidth = border.HasValue ? 2f : 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(chosen.xMin + radius, chosen.yMin));
            painter.LineTo(new Vector2(chosen.xMax - radius, chosen.yMin));
            painter.ArcTo(new Vector2(chosen.xMax, chosen.yMin), new Vector2(chosen.xMax, chosen.yMin + radius), radius);
            painter.LineTo(new Vector2(chosen.xMax, chosen.yMax - radius));
            painter.ArcTo(new Vector2(chosen.xMax, chosen.yMax), new Vector2(chosen.xMax - radius, chosen.yMax), radius);
            painter.LineTo(new Vector2(chosen.xMin + radius, chosen.yMax));
            painter.ArcTo(new Vector2(chosen.xMin, chosen.yMax), new Vector2(chosen.xMin, chosen.yMax - radius), radius);
            painter.LineTo(new Vector2(chosen.xMin, chosen.yMin + radius));
            painter.ArcTo(new Vector2(chosen.xMin, chosen.yMin), new Vector2(chosen.xMin + radius, chosen.yMin), radius);
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
            context.DrawText(text, new Vector2(chosen.x + 6f, chosen.y + 1f), 14f, SwitchPlaqueText, null);
        }
    }
}
