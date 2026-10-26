using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The markings tool (ADR 044), modelled on Town Road Lane: on a junction made in this project it shows the anchors of
    /// its arms; a click on one and then another draws a line between them in the chosen style, a click on a line selects
    /// it for another style, another curvature or deleting. Every change makes the junction again with its markings and
    /// puts it in place of the old one, as one undo step.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>An anchor or line counts as under the pointer within this many pixels.</summary>
        private const float MarkingPickPixels = 10f;

        private MarkingToolOptions markingOptions;

        // The junction whose markings are edited and what was read of it: its anchors in the object frame and in world
        // metres with their heights; the anchor a new line starts from, the one under the pointer and the selected line,
        // each -1 for none.
        private OmsiTileSummary markingTile;
        private OmsiObjectRecord markingJunction;
        private OmsiGeneratedJunction markingMade;
        private List<OmsiMarkingAnchor> markingAnchors = new List<OmsiMarkingAnchor>();
        private readonly List<OmsiMapPoint> markingAnchorPoints = new List<OmsiMapPoint>();
        private readonly List<double> markingAnchorHeights = new List<double>();
        private int markingFrom = -1;
        private int markingHover = -1;
        private int markingSelected = -1;

        private void CreateMarkingOptions()
        {
            markingOptions = new MarkingToolOptions();
            markingOptions.style.display = DisplayStyle.None;
            markingOptions.StyleChosen += style =>
            {
                if (markingSelected >= 0 && markingMade != null && markingMade.Markings[markingSelected].Style != style)
                {
                    var lines = CopyMarkings();
                    lines[markingSelected].Style = style;
                    ApplyMarkings(lines, markingSelected, "Changed the line to " + OmsiMarkingStyleInfo.Of(style).Name.ToLowerInvariant());
                }
                else
                    UpdateMarkingPreview();
            };
            markingOptions.CurvatureCommitted += () =>
            {
                if (markingSelected < 0 || markingMade == null || markingMade.Markings[markingSelected].Curvature == markingOptions.Curvature)
                    return;
                var lines = CopyMarkings();
                lines[markingSelected].Curvature = markingOptions.Curvature;
                ApplyMarkings(lines, markingSelected, "Changed the curvature of the line");
            };
            markingOptions.DeleteRequested += DeleteSelectedMarking;
            objectsPanelHost.Add(markingOptions);
        }

        private void BeginMarkingTool()
        {
            if (markingOptions == null || mapCanvas == null || currentSummary == null)
                return;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (terrainOptions != null)
                terrainOptions.style.display = DisplayStyle.None;
            markingOptions.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.ShowSplineEnds = false;
            mapCanvas.BeginRoad();
            ClearMarkingTarget();
            var item = selection.Count == 1 ? selection[0] : null;
            if (item?.Tile == null || !TargetMarkingJunction(item.Tile, item.Record))
                SetStatus("Markings: click a junction made in this project to draw its road markings; Esc leaves the tool.");
        }

        private void EndMarkingTool()
        {
            mapCanvas?.EndRoad();
            if (mapCanvas != null)
            {
                mapCanvas.ShowSplineEnds = true;
                mapCanvas.ClearRoadPreview();
            }
            if (markingOptions != null)
                markingOptions.style.display = DisplayStyle.None;
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            ClearMarkingTarget();
        }

        private void ClearMarkingTarget()
        {
            markingTile = null;
            markingJunction = null;
            markingMade = null;
            markingAnchors = new List<OmsiMarkingAnchor>();
            markingAnchorPoints.Clear();
            markingAnchorHeights.Clear();
            markingFrom = -1;
            markingHover = -1;
            markingSelected = -1;
            markingOptions?.ShowLine(null);
            markingOptions?.ShowJunction("Click a junction made in this project.");
            markingOptions?.ShowPlan(string.Empty, false);
        }

        /// <summary>
        /// Edits the markings of a junction the editor made: reads its arms as they would be made again and lays out their
        /// anchors. False with the reason shown when it is no such junction.
        /// </summary>
        private bool TargetMarkingJunction(OmsiTileSummary tile, OmsiObjectRecord record)
        {
            var made = MadeJunction(record.AssetPath);
            if (made == null)
                return false;
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
            if (rebuild == null)
            {
                SetStatus("Junction #" + record.Id + " cannot be made again: its connection points do not match its file.");
                return false;
            }
            OmsiJunctionLayout layout;
            try
            {
                layout = OmsiJunctionLayout.Build(rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, PlainRoadTexture()).Arms, PlainRoadTexture());
            }
            catch (ArgumentException exception)
            {
                SetStatus("Junction #" + record.Id + " cannot be laid out: " + exception.Message);
                return false;
            }
            var keep = ReferenceEquals(record, markingJunction) ? markingSelected : -1;
            markingTile = tile;
            markingJunction = record;
            markingMade = made;
            markingAnchors = OmsiJunctionMarkings.Anchors(layout);
            markingAnchorPoints.Clear();
            markingAnchorHeights.Clear();
            foreach (var anchor in markingAnchors)
            {
                markingAnchorPoints.Add(ObjectToWorld(tile, record, anchor.X, anchor.Y));
                markingAnchorHeights.Add(record.Height + anchor.Height + OmsiJunctionMarkings.Lift);
            }
            markingFrom = -1;
            markingSelected = keep >= 0 && keep < made.Markings.Count ? keep : -1;
            markingOptions.ShowLine(markingSelected >= 0 ? made.Markings[markingSelected] : null);
            markingOptions.ShowJunction("Junction #" + record.Id + ": " + made.Arms.Count + " arms, " + made.Markings.Count + " line(s).");
            if (selection.Count != 1 || !ReferenceEquals(selection[0].Record, record))
            {
                selection.Clear();
                selection.Add(new MapSelectionItem { Tile = tile, Record = record });
                ApplySelection(tile);
            }
            UpdateMarkingPreview();
            SetStatus("Markings of junction #" + record.Id + ": click an anchor, then another to draw a line; click a line to select it. Esc leaves the tool.");
            return true;
        }

        /// <summary>The junction was made again or taken back, such as by undo: its markings are read anew.</summary>
        private void RefreshMarkingTarget()
        {
            if (markingJunction == null || markingTile == null)
                return;
            if (!markingTile.Objects.Contains(markingJunction))
            {
                ClearMarkingTarget();
                UpdateMarkingPreview();
                return;
            }
            if (markingMade == null || !string.Equals(markingMade.AssetPath, markingJunction.AssetPath, StringComparison.OrdinalIgnoreCase))
                TargetMarkingJunction(markingTile, markingJunction);
        }

        private string PlainRoadTexture()
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            return File.Exists(Path.Combine(root, PlainAsphalt)) ? PlainAsphalt : null;
        }

        /// <summary>A point of an object's frame (x right, y forward) in world metres; pitch and bank are left out, as junctions keep them at 0.</summary>
        private OmsiMapPoint ObjectToWorld(OmsiTileSummary tile, OmsiObjectRecord record, double x, double y)
        {
            var centre = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX, record.LocalY, currentSummary.TileSizeMeters);
            var heading = record.HeadingDegrees * Math.PI / 180.0;
            return new OmsiMapPoint(centre.X + x * Math.Cos(heading) + y * Math.Sin(heading), centre.Y - x * Math.Sin(heading) + y * Math.Cos(heading));
        }

        /// <summary>A line's centre in world metres, or null when its anchors are gone.</summary>
        private OmsiMapPoint[] MarkingPolyline(OmsiMarkingLine line)
        {
            var path = OmsiJunctionMarkings.Path(markingAnchors, line);
            if (path == null)
                return null;
            var points = new OmsiMapPoint[path.Count];
            for (var index = 0; index < path.Count; index++)
                points[index] = ObjectToWorld(markingTile, markingJunction, path[index][0], path[index][1]);
            return points;
        }

        private double MarkingPickReach()
        {
            return mapCanvas != null && mapCanvas.ThreeDimensional
                ? Math.Max(0.5, mapCanvas.OrbitDistance * 0.015)
                : Math.Max(0.3, MarkingPickPixels * (mapCanvas?.MetersPerPixel ?? 1f));
        }

        /// <summary>The anchor nearest a point within reach, or -1.</summary>
        private int MarkingAnchorAt(OmsiMapPoint point)
        {
            var best = -1;
            var bestDistance = MarkingPickReach();
            for (var index = 0; index < markingAnchorPoints.Count; index++)
            {
                var distance = Distance(markingAnchorPoints[index], point);
                if (distance <= bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>The line of the junction nearest a point within reach, or -1.</summary>
        private int MarkingLineAt(OmsiMapPoint point)
        {
            if (markingMade == null)
                return -1;
            var best = -1;
            var bestDistance = MarkingPickReach();
            for (var line = 0; line < markingMade.Markings.Count; line++)
            {
                var points = MarkingPolyline(markingMade.Markings[line]);
                if (points == null)
                    continue;
                for (var index = 1; index < points.Length; index++)
                {
                    var distance = DistanceToSegment(point, points[index - 1], points[index]);
                    if (distance <= bestDistance)
                    {
                        best = line;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        private static double DistanceToSegment(OmsiMapPoint point, OmsiMapPoint a, OmsiMapPoint b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared < 1e-12 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared));
            return Distance(point, new OmsiMapPoint(a.X + dx * t, a.Y + dy * t));
        }

        /// <summary>A junction made in this project around a point, the nearest within its reach, or false.</summary>
        private bool MadeJunctionAt(OmsiMapPoint point, out OmsiTileSummary tile, out OmsiObjectRecord record)
        {
            tile = null;
            record = null;
            var best = double.MaxValue;
            foreach (var candidateTile in currentSummary.Tiles)
                foreach (var candidate in candidateTile.Objects)
                {
                    if (!OmsiGeneratedJunction.IsMadeHere(candidate.AssetPath))
                        continue;
                    var centre = OmsiMapGeometry.TileLocalToWorld(candidateTile.X, candidateTile.Y, candidate.LocalX, candidate.LocalY, currentSummary.TileSizeMeters);
                    var distance = Distance(centre, point);
                    if (distance >= best || distance > 40.0)
                        continue;
                    var made = MadeJunction(candidate.AssetPath);
                    if (made == null)
                        continue;
                    // Within the reach of its farthest connection point.
                    var reach = 0.0;
                    foreach (var port in Junctions().Ports(candidateTile, candidate, currentSummary.TileSizeMeters))
                        reach = Math.Max(reach, Distance(port.Position, centre));
                    if (distance > reach + 1.0)
                        continue;
                    best = distance;
                    tile = candidateTile;
                    record = candidate;
                }
            return record != null;
        }

        /// <summary>Shows the anchors, the junction's lines, the selected one and the line being drawn to the pointer.</summary>
        private void UpdateMarkingPreview()
        {
            if (mapCanvas == null || activeTool != EditorTool.Markings)
                return;
            RefreshMarkingTarget();
            if (markingJunction == null)
            {
                mapCanvas.ClearRoadPreview();
                markingOptions?.ShowPlan(string.Empty, false);
                return;
            }
            markingHover = roadPointer.HasValue ? MarkingAnchorAt(roadPointer.Value) : -1;
            mapCanvas.SetMarkingAnchors(markingAnchorPoints, markingAnchorHeights, markingHover, markingFrom);
            var lines = new List<OmsiMapPoint[]>();
            for (var line = 0; line < markingMade.Markings.Count; line++)
            {
                var points = MarkingPolyline(markingMade.Markings[line]);
                if (points != null && line != markingSelected)
                    lines.Add(points);
            }
            mapCanvas.SetRoadGuides(lines);

            var shown = new List<OmsiMapPoint[]>();
            string plan;
            if (markingFrom >= 0)
            {
                var from = markingAnchors[markingFrom];
                if (markingHover >= 0 && markingHover != markingFrom)
                {
                    var preview = MarkingPolyline(new OmsiMarkingLine
                    {
                        From = from.Key, To = markingAnchors[markingHover].Key, Style = markingOptions.Style, Curvature = markingOptions.Curvature
                    });
                    if (preview != null)
                        shown.Add(preview);
                    plan = "Click to draw a " + OmsiMarkingStyleInfo.Of(markingOptions.Style).Name.ToLowerInvariant() + " to this anchor.";
                }
                else
                {
                    if (roadPointer.HasValue)
                        shown.Add(new[] { markingAnchorPoints[markingFrom], roadPointer.Value });
                    plan = "Click the anchor the line ends at; Esc lets go of this one.";
                }
            }
            else if (markingSelected >= 0)
            {
                var selectedLine = MarkingPolyline(markingMade.Markings[markingSelected]);
                if (selectedLine != null)
                    shown.Add(selectedLine);
                plan = "Selected: " + OmsiMarkingStyleInfo.Of(markingMade.Markings[markingSelected].Style).Name.ToLowerInvariant() +
                       ". Choose another style or curvature, or press Del.";
            }
            else
                plan = markingHover >= 0 ? "Click to start a line at this anchor." : "Click an anchor to start a line, or a line to select it.";
            mapCanvas.SetRoadPreview(shown, true, null, null, null);
            markingOptions.ShowPlan(plan, false);
        }

        /// <summary>A click with the markings tool: picks anchors, draws a line between two, selects a line or another junction.</summary>
        private void ClickMarking(double x, double y)
        {
            if (currentSummary == null)
                return;
            var point = new OmsiMapPoint(x, y);
            roadPointer = point;
            RefreshMarkingTarget();
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("draw road markings"));
                return;
            }
            var anchor = markingJunction != null ? MarkingAnchorAt(point) : -1;
            if (anchor >= 0)
            {
                markingSelected = -1;
                markingOptions.ShowLine(null);
                if (markingFrom < 0 || markingFrom == anchor)
                {
                    markingFrom = markingFrom == anchor ? -1 : anchor;
                    UpdateMarkingPreview();
                    SetStatus(markingFrom >= 0 ? "Click the anchor the line ends at." : "Let go of the anchor.");
                    return;
                }
                var line = new OmsiMarkingLine
                {
                    From = markingAnchors[markingFrom].Key, To = markingAnchors[anchor].Key, Style = markingOptions.Style, Curvature = markingOptions.Curvature
                };
                markingFrom = -1;
                var lines = CopyMarkings();
                lines.Add(line);
                ApplyMarkings(lines, -1, "Drew a " + OmsiMarkingStyleInfo.Of(line.Style).Name.ToLowerInvariant());
                return;
            }
            var hit = markingJunction != null ? MarkingLineAt(point) : -1;
            if (hit >= 0)
            {
                markingFrom = -1;
                markingSelected = hit;
                markingOptions.ShowLine(markingMade.Markings[hit]);
                UpdateMarkingPreview();
                SetStatus("Selected a " + OmsiMarkingStyleInfo.Of(markingMade.Markings[hit].Style).Name.ToLowerInvariant() +
                          ": choose another style or curvature in the panel, Y switches the style, Del deletes it.");
                return;
            }
            if (MadeJunctionAt(point, out var tile, out var record) && !ReferenceEquals(record, markingJunction))
            {
                TargetMarkingJunction(tile, record);
                return;
            }
            markingFrom = -1;
            markingSelected = -1;
            markingOptions.ShowLine(null);
            UpdateMarkingPreview();
            if (markingJunction == null)
                SetStatus("Markings: click a junction made in this project; only those can carry markings drawn here.");
        }

        private List<OmsiMarkingLine> CopyMarkings()
        {
            var lines = new List<OmsiMarkingLine>();
            foreach (var line in markingMade.Markings)
                lines.Add(line.Copy());
            return lines;
        }

        private void DeleteSelectedMarking()
        {
            if (markingSelected < 0 || markingMade == null)
                return;
            var lines = CopyMarkings();
            lines.RemoveAt(markingSelected);
            ApplyMarkings(lines, -1, "Deleted the line");
        }

        /// <summary>Switches the style for the next line, or of the selected line, to the next one, as Y does.</summary>
        private void NextMarkingStyle()
        {
            markingOptions?.ChooseStyle(markingOptions.NextStyle());
            SetStatus("Markings: " + OmsiMarkingStyleInfo.Of(markingOptions.Style).Name.ToLowerInvariant() + ".");
        }

        /// <summary>
        /// Makes the junction again with these markings under Sceneryobjects\Mapforge and puts it in place of the old one, as
        /// one undo step; <paramref name="select"/> is the line to keep selected, or -1.
        /// </summary>
        private void ApplyMarkings(List<OmsiMarkingLine> lines, int select, string done)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("draw road markings"));
                return;
            }
            var record = markingJunction;
            var made = markingMade;
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, markingTile, record);
            if (rebuild == null)
            {
                SetStatus("Junction #" + record.Id + " cannot be made again: its connection points do not match its file.");
                return;
            }
            var plain = PlainRoadTexture();
            var turn = rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, plain);
            if (turn.HasErrors)
            {
                SetStatus("Junction #" + record.Id + " stays: " + (turn.Problem ?? "the roads at it cannot be kept as they are."));
                return;
            }
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, made.Name, OmsiJunctionGenerator.Junction(turn.Arms, plain, "Mapforge " + made.Name, lines));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("The junction could not be made again: " + exception.Message);
                return;
            }
            OmsiJunctionInsertionResult result;
            try
            {
                result = session.RebuildJunction(currentSummary, rebuild, turn, assetPath);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                RefreshAfterChange(null);
                SetStatus("Junction #" + record.Id + " could not be made again: " + exception.Message);
                return;
            }
            if (!result.Succeeded)
            {
                RefreshAfterChange(null);
                SetStatus("Junction #" + record.Id + " stays: " + DescribeReferences(result.Blockers) + " still need it as it is.");
                return;
            }
            var steps = new List<object>(result.Steps);
            foreach (var step in steps)
                if (step is OmsiSplineChange change)
                    RefreshSplineViews(change);
            roadPorts = null;
            PushSteps(steps, "the markings of junction #" + record.Id);
            markingSelected = -1;
            TargetMarkingJunction(markingTile, record);
            if (select >= 0 && select < markingMade.Markings.Count)
            {
                markingSelected = select;
                markingOptions.ShowLine(markingMade.Markings[select]);
                UpdateMarkingPreview();
            }
            SetStatus(done + " on junction #" + record.Id + ", now " + markingMade.Markings.Count + " line(s), made again as " +
                      Path.GetFileNameWithoutExtension(assetPath) + ". Ctrl+Z takes it back.");
        }

        /// <summary>
        /// For command-line checks: opens the markings tool on the selected junction and, unless the argument is "show", draws
        /// lines given as <c>from-to:style</c>, separated by commas, such as <c>1.0.1-2.0.1:laneline,0.1.0-0.1.1:stop</c>. The
        /// status goes to the log as "Markings:".
        /// </summary>
        private void RunMarkingArgument(string argument)
        {
            SetTool(EditorTool.Markings);
            for (var index = 0; index < markingAnchors.Count; index++)
                Debug.Log("Marking anchor " + markingAnchors[index].Key + " (" + markingAnchors[index].Kind + ") at " +
                          markingAnchorPoints[index].X.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                          markingAnchorPoints[index].Y.ToString("0.0", CultureInfo.InvariantCulture));
            if (markingJunction != null && !string.Equals(argument, "show", StringComparison.OrdinalIgnoreCase))
            {
                var lines = CopyMarkings();
                foreach (var part in argument.Split(','))
                {
                    var pieces = part.Split(':');
                    var ends = pieces[0].Split('-');
                    if (ends.Length != 2)
                        continue;
                    var style = pieces.Length > 1 ? OmsiMarkingStyleInfo.ByKey(pieces[1]) : null;
                    lines.Add(new OmsiMarkingLine { From = ends[0].Trim(), To = ends[1].Trim(), Style = style?.Style ?? markingOptions.Style });
                }
                ApplyMarkings(lines, -1, "Drew " + (lines.Count - markingMade.Markings.Count) + " line(s)");
            }
            Debug.Log("Markings: " + statusLabel?.text);
        }

        /// <summary>Esc with the markings tool: lets go of the anchor, then of the selected line, then leaves the tool.</summary>
        private void EscapeMarking()
        {
            if (markingFrom >= 0 || markingSelected >= 0)
            {
                markingFrom = -1;
                markingSelected = -1;
                markingOptions.ShowLine(null);
                UpdateMarkingPreview();
                SetStatus("Markings: click an anchor to start a line, or a line to select it; Esc leaves the tool.");
            }
            else
                SetTool(EditorTool.Drag);
        }
    }
}
