using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The movements of a crossing for the traffic signals tool (ADR 085): each way through as a line with an arrow at its
    /// end, in the colour of the signal group it obeys, grey and thin without one. Drawn from above and in 3D at the
    /// crossing's height.
    /// </summary>
    internal sealed partial class MapCanvasElement
    {
        private struct SignalLine
        {
            public Vector2[] Points;
            public Color Colour;
            public bool Selected;
            public bool Bound;
        }

        private readonly List<SignalLine> signalLines = new List<SignalLine>();

        /// <summary>What a signal marker shows: a head (editor overlay in its group's or its current colour) or where movements meet.</summary>
        internal enum SignalMarkerKind
        {
            Head,
            Conflict,
            ConditionalConflict
        }

        private struct SignalMarker
        {
            public Vector2 At;
            public float Height;
            public Color Colour;
            public SignalMarkerKind Kind;
        }

        private readonly List<SignalMarker> signalMarkers = new List<SignalMarker>();

        /// <summary>Heads and conflict points of the signals tool, in world metres with their height for the 3D view; null clears them.</summary>
        public void SetSignalMarkers(IList<(OmsiMapPoint At, double Height, Color Colour, SignalMarkerKind Kind)> markers)
        {
            signalMarkers.Clear();
            if (markers != null && summary != null)
                foreach (var marker in markers)
                    signalMarkers.Add(new SignalMarker { At = WorldToMap(marker.At), Height = (float)marker.Height, Colour = marker.Colour, Kind = marker.Kind });
            MarkDirtyRepaint();
        }

        private void DrawSignalMarkers(Painter2D painter, MapOrbitCamera? camera)
        {
            foreach (var marker in signalMarkers)
            {
                if (!HandleScreen(marker.At, marker.Height, camera, out var at))
                    continue;
                if (marker.Kind == SignalMarkerKind.Head)
                {
                    // A dark ring round a disc of the colour: plainly the editor's mark, not a lamp of the model.
                    painter.fillColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
                    painter.BeginPath();
                    painter.Arc(at, 8.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Fill();
                    painter.fillColor = marker.Colour;
                    painter.BeginPath();
                    painter.Arc(at, 5.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Fill();
                    painter.strokeColor = Color.white;
                    painter.lineWidth = 1.2f;
                    painter.BeginPath();
                    painter.Arc(at, 8.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Stroke();
                    continue;
                }
                painter.lineCap = LineCap.Round;
                painter.strokeColor = new Color(0.05f, 0.05f, 0.06f, 0.9f);
                painter.lineWidth = 5.5f;
                StrokeCross(painter, at, 6f);
                painter.strokeColor = marker.Colour;
                painter.lineWidth = 3f;
                StrokeCross(painter, at, 6f);
            }
        }

        private static void StrokeCross(Painter2D painter, Vector2 at, float size)
        {
            painter.BeginPath();
            painter.MoveTo(at + new Vector2(-size, -size));
            painter.LineTo(at + new Vector2(size, size));
            painter.MoveTo(at + new Vector2(size, -size));
            painter.LineTo(at + new Vector2(-size, size));
            painter.Stroke();
        }
        private float signalHeight = float.NaN;
        private static readonly Color UnboundSignalColour = new Color(0.68f, 0.72f, 0.77f, 0.75f);

        /// <summary>The movements to show as world points, their group colours (null for none) and which are selected; null clears them.</summary>
        /// <param name="height">Where they lie in the 3D view; NaN for the ground.</param>
        public void SetSignalMovements(IList<OmsiMapPoint[]> lines, IList<Color?> colours, IList<bool> selected, double height)
        {
            signalLines.Clear();
            signalHeight = (float)height;
            if (lines != null && summary != null)
                for (var index = 0; index < lines.Count; index++)
                {
                    var line = lines[index];
                    if (line == null || line.Length < 2)
                        continue;
                    var points = new Vector2[line.Length];
                    for (var point = 0; point < line.Length; point++)
                        points[point] = WorldToMap(line[point]);
                    var colour = colours != null && index < colours.Count ? colours[index] : null;
                    signalLines.Add(new SignalLine
                    {
                        Points = points, Colour = colour ?? UnboundSignalColour, Bound = colour.HasValue,
                        Selected = selected != null && index < selected.Count && selected[index]
                    });
                }
            MarkDirtyRepaint();
        }

        private void DrawSignalMovements(Painter2D painter, MapOrbitCamera? camera = null)
        {
            DrawSignalLines(painter, camera);
            DrawSignalMarkers(painter, camera);
        }

        private void DrawSignalLines(Painter2D painter, MapOrbitCamera? camera)
        {
            if (signalLines.Count == 0)
                return;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            // The unbound first, so the coloured ones lie on top; the selected last of all.
            for (var pass = 0; pass < 3; pass++)
                foreach (var line in signalLines)
                {
                    var wanted = line.Selected ? 2 : line.Bound ? 1 : 0;
                    if (wanted != pass)
                        continue;
                    var screen = new List<Vector2>(line.Points.Length);
                    var visible = true;
                    foreach (var point in line.Points)
                    {
                        if (!HandleScreen(point, signalHeight, camera, out var at))
                        {
                            visible = false;
                            break;
                        }
                        screen.Add(at);
                    }
                    if (!visible || screen.Count < 2)
                        continue;
                    if (line.Selected)
                    {
                        // A light halo under a selected movement.
                        painter.strokeColor = routeColor;
                        painter.lineWidth = 9f;
                        Stroke(painter, screen);
                    }
                    painter.strokeColor = line.Colour;
                    painter.lineWidth = line.Bound ? 4.5f : 2f;
                    Stroke(painter, screen);
                    // An arrow head where it leaves.
                    var end = screen[screen.Count - 1];
                    var back = screen[screen.Count - 2];
                    for (var index = screen.Count - 2; index >= 0 && (end - back).sqrMagnitude < 36f; index--)
                        back = screen[index];
                    var direction = (end - back).normalized;
                    if (direction.sqrMagnitude < 0.5f)
                        continue;
                    var side = new Vector2(-direction.y, direction.x);
                    var size = line.Bound ? 9f : 6f;
                    painter.fillColor = line.Colour;
                    painter.BeginPath();
                    painter.MoveTo(end + direction * size * 0.4f);
                    painter.LineTo(end - direction * size + side * size * 0.6f);
                    painter.LineTo(end - direction * size - side * size * 0.6f);
                    painter.ClosePath();
                    painter.Fill();
                }
        }

        private static void Stroke(Painter2D painter, List<Vector2> points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (var index = 1; index < points.Count; index++)
                painter.LineTo(points[index]);
            painter.Stroke();
        }
    }
}
