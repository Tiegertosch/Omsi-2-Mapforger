using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The signal routes on the map (ADR 105, M1), drawn only while the list of signal routes is open: a small marker at each signal that has a
    /// route, and for the route chosen its track pieces in the accent colour with its signal (a filled disc), its distant signal (a diamond) and its
    /// next signal (a square) told apart by shape. Nothing is kept or drawn while the layer is off.
    /// </summary>
    internal sealed partial class MapCanvasElement
    {
        private readonly List<Vector2> signalRouteMarkers = new List<Vector2>();
        private readonly List<Vector2[]> signalRouteLines = new List<Vector2[]>();
        private readonly List<Vector2> signalRoutePoints = new List<Vector2>();
        private Vector2? signalRouteSignal;
        private Vector2? signalRouteDistant;
        private Vector2? signalRouteNext;

        /// <summary>The signals that have a route, as small markers; null or empty removes them.</summary>
        public void SetSignalRouteMarkers(IEnumerable<OmsiMapPoint> signals)
        {
            signalRouteMarkers.Clear();
            if (signals != null && summary != null)
                foreach (var point in signals)
                    signalRouteMarkers.Add(WorldToMap(point));
            MarkDirtyRepaint();
        }

        /// <summary>The route chosen: its track pieces and its three kinds of signal (each may be missing); all null removes the highlight.</summary>
        public void SetSignalRouteHighlight(IEnumerable<OmsiMapPoint[]> lines, IEnumerable<OmsiMapPoint> points, OmsiMapPoint? signal, OmsiMapPoint? distant, OmsiMapPoint? next)
        {
            signalRouteLines.Clear();
            signalRoutePoints.Clear();
            signalRouteSignal = signalRouteDistant = signalRouteNext = null;
            if (summary != null)
            {
                if (lines != null)
                    foreach (var line in lines)
                    {
                        var map = new Vector2[line.Length];
                        for (var index = 0; index < map.Length; index++)
                            map[index] = WorldToMap(line[index]);
                        signalRouteLines.Add(map);
                    }
                if (points != null)
                    foreach (var point in points)
                        signalRoutePoints.Add(WorldToMap(point));
                signalRouteSignal = signal.HasValue ? WorldToMap(signal.Value) : (Vector2?)null;
                signalRouteDistant = distant.HasValue ? WorldToMap(distant.Value) : (Vector2?)null;
                signalRouteNext = next.HasValue ? WorldToMap(next.Value) : (Vector2?)null;
            }
            MarkDirtyRepaint();
        }

        /// <summary>True while anything of the signal routes layer is set.</summary>
        public bool ShowsSignalRoutes => signalRouteMarkers.Count > 0 || signalRouteLines.Count > 0 || signalRoutePoints.Count > 0 || signalRouteSignal.HasValue;

        /// <summary>
        /// Brings a box in world metres into view, keeping out of the strips the windows take (left, top, right and bottom, in pixels): from above at the
        /// zoom that fits it, within 0.15 and 8 metres a pixel; in 3D the camera looks at its middle from a distance that takes it in.
        /// </summary>
        public void ShowWorldBox(OmsiMapPoint min, OmsiMapPoint max, Vector4 insets)
        {
            if (summary == null)
                return;
            var low = WorldToMap(min);
            var high = WorldToMap(max);
            var middle = (low + high) * 0.5f;
            var width = Mathf.Abs(high.x - low.x);
            var height = Mathf.Abs(high.y - low.y);
            if (ThreeDimensional)
            {
                OrbitTarget = middle;
                SetOrbit(OrbitYaw, Mathf.Max(OrbitPitch, 35f), Mathf.Clamp(Mathf.Max(width, height) * 1.6f, 40f, 1500f));
                return;
            }
            var size = layout.size;
            if (float.IsNaN(size.x) || size.x < 10f || size.y < 10f)
            {
                center = middle;
                hasView = true;
                isFitted = false;
                MarkDirtyRepaint();
                return;
            }
            const float margin = 70f;
            var free = new Vector2(Mathf.Max(50f, size.x - insets.x - insets.z - margin * 2f), Mathf.Max(50f, size.y - insets.y - insets.w - margin * 2f));
            metersPerPixel = Mathf.Clamp(Mathf.Max(width / free.x, height / free.y), 0.15f, 8f);
            // The middle of the box goes to the middle of the free part.
            center = middle + new Vector2((insets.z - insets.x) * 0.5f, (insets.y - insets.w) * 0.5f) * metersPerPixel;
            hasView = true;
            isFitted = false;
            MarkDirtyRepaint();
        }

        private void DrawSignalRoutes(Painter2D painter, Rect visible, MapOrbitCamera? camera = null)
        {
            if (!ShowsSignalRoutes)
                return;
            var threeDimensional = camera.HasValue;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;

            // The track of the route chosen: a wide soft stroke under a narrow bright one.
            if (signalRouteLines.Count > 0)
            {
                var halo = (Color)splineColor;
                halo.a = 0.35f;
                var core = (Color)splineColor;
                foreach (var pass in new[] { 0, 1 })
                {
                    painter.strokeColor = pass == 0 ? halo : core;
                    painter.lineWidth = pass == 0 ? 11f : 4f;
                    painter.BeginPath();
                    var segments = 0;
                    foreach (var line in signalRouteLines)
                    {
                        if (!threeDimensional)
                        {
                            for (var index = 0; index < line.Length; index++)
                            {
                                var at = MapToScreen(line[index]);
                                if (index == 0)
                                    painter.MoveTo(at);
                                else
                                    painter.LineTo(at);
                            }
                            segments += line.Length - 1;
                        }
                        else
                        {
                            var started = false;
                            for (var index = 0; index < line.Length; index++)
                            {
                                if (!HandleScreen(line[index], float.NaN, camera, out var at))
                                {
                                    started = false;
                                    continue;
                                }
                                if (!started)
                                    painter.MoveTo(at);
                                else
                                    painter.LineTo(at);
                                started = true;
                                segments++;
                            }
                        }
                        segments = FlushStroke(painter, segments);
                    }
                    if (segments > 0)
                        painter.Stroke();
                }
            }

            // Pieces that have no line (an object without paths): a ring each.
            painter.lineWidth = 3f;
            painter.strokeColor = splineColor;
            foreach (var point in signalRoutePoints)
            {
                if (!HandleScreen(point, float.NaN, camera, out var at))
                    continue;
                painter.BeginPath();
                painter.Arc(at, 8f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Stroke();
            }

            // All signals with a route: one small ring each, only what is in view.
            painter.lineWidth = 2f;
            painter.strokeColor = routeColor;
            painter.fillColor = handleFillColor;
            foreach (var marker in signalRouteMarkers)
            {
                if (!threeDimensional && !visible.Contains(marker))
                    continue;
                if (!HandleScreen(marker, float.NaN, camera, out var at))
                    continue;
                painter.BeginPath();
                painter.Arc(at, 5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                painter.Stroke();
            }

            // The signals of the route chosen, by shape: next signal a square, distant signal a diamond, the signal itself a disc on top.
            if (signalRouteNext.HasValue && HandleScreen(signalRouteNext.Value, float.NaN, camera, out var nextAt))
                DrawRouteSignalShape(painter, nextAt, 0, placementColor);
            if (signalRouteDistant.HasValue && HandleScreen(signalRouteDistant.Value, float.NaN, camera, out var distantAt))
                DrawRouteSignalShape(painter, distantAt, 1, (Color)objectColor);
            if (signalRouteSignal.HasValue && HandleScreen(signalRouteSignal.Value, float.NaN, camera, out var signalAt))
                DrawRouteSignalShape(painter, signalAt, 2, splineColor);
            painter.lineCap = LineCap.Butt;
        }

        /// <summary>0 a square outline, 1 a diamond outline, 2 a filled disc; each with a white outer ring so it stands out on any ground.</summary>
        private void DrawRouteSignalShape(Painter2D painter, Vector2 at, int shape, Color colour)
        {
            // The diamond is larger than the disc, so a distant signal on the pole of its signal still shows its corners round it.
            var radius = shape == 2 ? 8f : 10f;
            painter.strokeColor = routeColor;
            painter.lineWidth = 6f;
            AddRouteShape(painter, at, shape, radius);
            painter.Stroke();
            painter.strokeColor = colour;
            painter.fillColor = colour;
            painter.lineWidth = 3f;
            AddRouteShape(painter, at, shape, radius);
            if (shape == 2)
                painter.Fill();
            painter.Stroke();
        }

        private static void AddRouteShape(Painter2D painter, Vector2 at, int shape, float radius)
        {
            painter.BeginPath();
            if (shape == 2)
            {
                painter.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                return;
            }
            var reach = shape == 1 ? radius * 1.7f : radius * 0.9f;
            if (shape == 1)
            {
                painter.MoveTo(at + new Vector2(0f, -reach));
                painter.LineTo(at + new Vector2(reach, 0f));
                painter.LineTo(at + new Vector2(0f, reach));
                painter.LineTo(at + new Vector2(-reach, 0f));
            }
            else
            {
                painter.MoveTo(at + new Vector2(-reach, -reach));
                painter.LineTo(at + new Vector2(reach, -reach));
                painter.LineTo(at + new Vector2(reach, reach));
                painter.LineTo(at + new Vector2(-reach, reach));
            }
            painter.ClosePath();
        }
    }
}
