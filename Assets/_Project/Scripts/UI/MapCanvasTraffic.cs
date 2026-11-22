using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// What the traffic rules tool needs of the map (ADR 098, M4): where a position of the window lies on the ground, the road piece under
    /// a point, the piece the pointer is over while a sign is dragged, and the speed limits and overtaking bans of the pieces as small signs.
    /// </summary>
    internal sealed partial class MapCanvasElement
    {
        /// <summary>Speed signs show on the roads closer in than this many metres per pixel.</summary>
        public const float TrafficLabelMetersPerPixel = 1.5f;

        private const int TrafficLabelsMost = 500;
        private const float TrafficLabelRadius = 11f;

        private bool showTrafficLabels;
        private float slowestTrafficLabelMilliseconds;
        private readonly List<Rect> trafficLabelAreas = new List<Rect>();

        /// <summary>What the last redraw spent on the speed signs, in milliseconds, and how many it drew; for checks.</summary>
        public float LastTrafficLabelMilliseconds { get; private set; }

        public int LastTrafficLabelCount { get; private set; }
        private readonly List<Vector2[]> trafficHighlight = new List<Vector2[]>();

        /// <summary>Shows the speed limit and the overtaking ban of each road piece that has one as a small sign; on while the traffic rules tool is.</summary>
        public bool ShowTrafficLabels
        {
            get => showTrafficLabels;
            set
            {
                if (showTrafficLabels == value)
                    return;
                showTrafficLabels = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Tells which splines are roads for vehicles; only those show a sign. Asked for the few splines in view that have a limit or a ban.</summary>
        public Func<OmsiSplineRecord, bool> TrafficLabelFilter { get; set; }

        /// <summary>
        /// The ground point in world metres under a position of this element (in its own coordinates), as the map's own clicks find it: from
        /// above by the zoom, in 3D by the ray through the position. False when there is none, such as over the sky.
        /// </summary>
        public bool TryLocalToWorld(Vector2 local, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            if (summary == null || !hasView)
                return false;
            if (!ThreeDimensional)
            {
                var map = ScreenToMap(local);
                x = map.x + originX;
                y = map.y + originY;
                return true;
            }
            var ground = GroundPoint(OrbitCamera().RayThrough(local));
            if (!ground.HasValue)
                return false;
            x = ground.Value.x + originX;
            y = ground.Value.z + originY;
            return true;
        }

        /// <summary>
        /// The spline whose line lies nearest a world point within the reach of a click, the nearest <paramref name="accept"/> takes first and
        /// else the nearest of any; null for none. Hover and drop find road pieces this way.
        /// </summary>
        public bool TrySplineAtWorld(double x, double y, Func<OmsiSplineRecord, bool> accept, out OmsiTileSummary tile, out OmsiSplineRecord spline)
        {
            tile = null;
            spline = null;
            if (summary == null)
                return false;
            var map = new Vector2((float)(x - originX), (float)(y - originY));
            var reach = ThreeDimensional ? Mathf.Max(2.5f, OrbitDistance * 0.02f) : Mathf.Max(2.5f, PickRadius * 1.75f * metersPerPixel);
            var nearestAccepted = reach;
            var nearestAny = reach;
            OmsiTileSummary anyTile = null;
            OmsiSplineRecord anySpline = null;
            foreach (var path in splines)
            {
                var bounds = path.Bounds;
                if (map.x < bounds.xMin - reach || map.x > bounds.xMax + reach || map.y < bounds.yMin - reach || map.y > bounds.yMax + reach)
                    continue;
                var best = float.MaxValue;
                for (var index = 1; index < path.Points.Length; index++)
                    best = Mathf.Min(best, DistanceToSegment(map, path.Points[index - 1], path.Points[index]));
                if (best > reach)
                    continue;
                if (best <= nearestAny)
                {
                    nearestAny = best;
                    anyTile = path.Tile;
                    anySpline = path.Record;
                }
                if (best <= nearestAccepted && (accept == null || accept(path.Record)))
                {
                    nearestAccepted = best;
                    tile = path.Tile;
                    spline = path.Record;
                }
            }
            if (spline == null)
            {
                tile = anyTile;
                spline = anySpline;
            }
            return spline != null;
        }

        /// <summary>Marks road pieces in the accent colour, such as the one a sign would go on; null or none clears it.</summary>
        public void SetTrafficHighlight(IEnumerable<OmsiSplineRecord> pieces)
        {
            trafficHighlight.Clear();
            if (pieces != null)
                foreach (var piece in pieces)
                {
                    var path = splines.Find(candidate => ReferenceEquals(candidate.Record, piece));
                    if (path != null)
                        trafficHighlight.Add(path.Points);
                }
            MarkDirtyRepaint();
        }

        private void DrawTrafficHighlight(Painter2D painter, MapOrbitCamera? camera = null)
        {
            if (trafficHighlight.Count == 0)
                return;
            var glow = (Color)splineColor;
            glow.a = 0.6f;
            painter.strokeColor = glow;
            painter.lineWidth = camera.HasValue ? 8f : Mathf.Clamp(7f / metersPerPixel, 5f, 14f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            foreach (var points in trafficHighlight)
            {
                painter.BeginPath();
                var started = false;
                foreach (var point in points)
                {
                    if (!HandleScreen(point, float.NaN, camera, out var at))
                        continue;
                    if (started)
                        painter.LineTo(at);
                    else
                        painter.MoveTo(at);
                    started = true;
                }
                if (started)
                    painter.Stroke();
            }
        }

        /// <summary>
        /// The speed limit and the overtaking ban of the road pieces as small round signs on them. Pieces out of view are skipped by their
        /// bounds and a sign that would lie on one already written is left out, so it stays cheap on a large map.
        /// </summary>
        private void DrawTrafficLabels(Painter2D painter, Rect visible, MapOrbitCamera? camera = null)
        {
            if (!showTrafficLabels)
                return;
            if (camera.HasValue ? OrbitDistance > TrafficLabelMetersPerPixel * layout.height : metersPerPixel > TrafficLabelMetersPerPixel)
                return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var screen = new Rect(-20f, -20f, layout.width + 40f, layout.height + 40f);
            var written = trafficLabelAreas;
            written.Clear();
            foreach (var path in splines)
            {
                var rules = path.Record.Rules;
                if (rules.Count == 0 || !path.Bounds.Overlaps(visible))
                    continue;
                if (TrafficLabelFilter != null && !TrafficLabelFilter(path.Record))
                    continue;
                OmsiTrafficSigns.Look(rules, out var kmh, out var ban);
                if (!kmh.HasValue && !ban)
                    continue;
                if (!HandleScreen(path.Points[path.Points.Length / 2], float.NaN, camera, out var at) || !screen.Contains(at))
                    continue;
                var width = (kmh.HasValue ? 1 : 0) + (ban ? 1 : 0);
                var area = new Rect(at.x - TrafficLabelRadius - 2f, at.y - TrafficLabelRadius - 2f, width * (TrafficLabelRadius * 2f + 2f) + 2f, TrafficLabelRadius * 2f + 4f);
                var covered = false;
                for (var other = 0; other < written.Count; other++)
                    if (written[other].Overlaps(area))
                    {
                        covered = true;
                        break;
                    }
                if (covered)
                    continue;
                written.Add(area);
                var centre = at;
                if (kmh.HasValue)
                {
                    TrafficSignPainter.Draw(painter, TrafficSignKind.Speed, (int)Math.Round(kmh.Value), centre, TrafficLabelRadius, true);
                    centre.x += TrafficLabelRadius * 2f + 2f;
                }
                if (ban)
                    TrafficSignPainter.Draw(painter, TrafficSignKind.OvertakingBan, 0, centre, TrafficLabelRadius, true);
                if (written.Count >= TrafficLabelsMost)
                    break;
            }
            // Slow redraws are reported once per new maximum, as for the map itself, so large maps can be diagnosed from the player log.
            var milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            LastTrafficLabelMilliseconds = milliseconds;
            LastTrafficLabelCount = written.Count;
            if (milliseconds > 10f && milliseconds > slowestTrafficLabelMilliseconds * 1.5f)
            {
                slowestTrafficLabelMilliseconds = milliseconds;
                Debug.LogWarning("Traffic signs took " + milliseconds.ToString("0") + " ms of a redraw, " + written.Count + " shown.");
            }
        }

        /// <summary>In the 3D view the signs of the pieces around the camera's centre show as from above at the same height.</summary>
        private void DrawTrafficLabels3D(Painter2D painter, MapOrbitCamera camera)
        {
            var reach = OrbitDistance * 2f;
            DrawTrafficLabels(painter, new Rect(OrbitTarget.x - reach, OrbitTarget.y - reach, reach * 2f, reach * 2f), camera);
        }
    }
}
