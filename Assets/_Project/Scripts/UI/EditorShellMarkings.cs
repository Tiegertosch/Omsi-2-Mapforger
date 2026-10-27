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
        private OmsiJunctionLayout markingLayout;
        private List<OmsiMarkingAnchor> markingAnchors = new List<OmsiMarkingAnchor>();
        private readonly List<OmsiMapPoint> markingAnchorPoints = new List<OmsiMapPoint>();
        private readonly List<double> markingAnchorHeights = new List<double>();
        private List<OmsiMarkingPiece> markingPieces = new List<OmsiMarkingPiece>();
        private int markingFrom = -1;
        private int markingHover = -1;
        private int markingSelected = -1;

        // The piece of the selected line under the pointer when it was clicked, by its place in markingPieces; -1 for none.
        private int markingPiece = -1;

        // The anchors round the area being outlined, by their place in markingAnchors, and the selected area, -1 for none.
        private readonly List<int> markingCorners = new List<int>();
        private int markingArea = -1;

        // The selected arrow, -1 for none.
        private int markingArrow = -1;

        private void CreateMarkingOptions()
        {
            markingOptions = new MarkingToolOptions();
            markingOptions.style.display = DisplayStyle.None;
            markingOptions.StyleChosen += style =>
            {
                // A line style ends outlining an area and lets go of a selected area or arrow.
                markingCorners.Clear();
                if (markingArea >= 0 || markingArrow >= 0)
                {
                    markingArea = -1;
                    markingArrow = -1;
                    markingOptions.ShowLine(null, null);
                }
                if (markingSelected < 0 || markingMade == null)
                {
                    UpdateMarkingPreview();
                    return;
                }
                var name = OmsiMarkingStyleInfo.Of(style).Name.ToLowerInvariant();
                if (markingOptions.PieceScope && markingPiece >= 0)
                {
                    var piece = markingPieces[markingPiece];
                    if (piece.Style == style)
                        return;
                    var lines = CopyMarkings();
                    ChangePiece(lines[piece.Line], piece, setting => setting.Style = style == lines[piece.Line].Style ? (OmsiMarkingStyle?)null : style);
                    ApplyMarkings(lines, CopyAreas(), markingSelected, "Changed the piece to " + name);
                }
                else if (markingMade.Markings[markingSelected].Style != style)
                {
                    var lines = CopyMarkings();
                    lines[markingSelected].Style = style;
                    ApplyMarkings(lines, CopyAreas(), markingSelected, "Changed the line to " + name);
                }
            };
            markingOptions.FillChosen += fill =>
            {
                // A fill starts outlining an area; with an area selected it changes that area.
                markingFrom = -1;
                markingArrow = -1;
                if (markingSelected >= 0)
                {
                    markingSelected = -1;
                    markingPiece = -1;
                    markingOptions.ShowLine(null, null);
                }
                if (markingArea < 0 || markingMade == null)
                {
                    UpdateMarkingPreview();
                    SetStatus("Markings: click the anchors round the area, then the first one again to close it.");
                    return;
                }
                if (markingMade.Areas[markingArea].Fill == fill)
                    return;
                var areas = CopyAreas();
                areas[markingArea].Fill = fill;
                ApplyMarkings(CopyMarkings(), areas, -1, "Changed the area to " + OmsiMarkingFillInfo.Of(fill).Name.ToLowerInvariant(), markingArea);
            };
            markingOptions.AngleCommitted += () =>
            {
                if (markingArea < 0 || markingMade == null || Math.Abs(markingMade.Areas[markingArea].Angle - markingOptions.Angle) < 0.05)
                    return;
                var areas = CopyAreas();
                areas[markingArea].Angle = markingOptions.Angle;
                ApplyMarkings(CopyMarkings(), areas, -1, "Changed the angle of the stripes", markingArea);
            };
            markingOptions.ArrowChosen += kind =>
            {
                // An arrow kind lays arrows on clicked lanes; with an arrow selected it changes that arrow.
                markingFrom = -1;
                markingCorners.Clear();
                markingArea = -1;
                if (markingSelected >= 0)
                {
                    markingSelected = -1;
                    markingPiece = -1;
                    markingOptions.ShowLine(null, null);
                }
                if (markingArrow < 0 || markingMade == null)
                {
                    UpdateMarkingPreview();
                    SetStatus("Markings: click a lane of an arm to lay a " + OmsiMarkingArrowInfo.Of(kind).Name.ToLowerInvariant() + " arrow there.");
                    return;
                }
                if (markingMade.Arrows[markingArrow].Kind == kind)
                    return;
                var arrows = CopyArrows();
                arrows[markingArrow].Kind = kind;
                ApplyMarkings(CopyMarkings(), CopyAreas(), arrows, -1, "Changed the arrow to " + OmsiMarkingArrowInfo.Of(kind).Name.ToLowerInvariant(), -1, markingArrow);
            };
            markingOptions.DistanceCommitted += () =>
            {
                if (markingArrow < 0 || markingMade == null || Math.Abs(markingMade.Arrows[markingArrow].Distance - markingOptions.Distance) < 0.005)
                    return;
                var arrows = CopyArrows();
                arrows[markingArrow].Distance = markingOptions.Distance;
                ApplyMarkings(CopyMarkings(), CopyAreas(), arrows, -1, "Moved the arrow", -1, markingArrow);
            };
            markingOptions.PieceVisibilityToggled += () =>
            {
                if (markingPiece < 0 || markingMade == null)
                    return;
                var piece = markingPieces[markingPiece];
                var lines = CopyMarkings();
                ChangePiece(lines[piece.Line], piece, setting => setting.Hidden = !piece.Hidden);
                ApplyMarkings(lines, CopyAreas(), markingSelected, piece.Hidden ? "Showed the piece again" : "Hid the piece");
            };
            markingOptions.AutoRequested += SuggestMarkings;
            markingOptions.CurvatureCommitted += () =>
            {
                if (markingSelected < 0 || markingMade == null || markingMade.Markings[markingSelected].Curvature == markingOptions.Curvature)
                    return;
                var lines = CopyMarkings();
                lines[markingSelected].Curvature = markingOptions.Curvature;
                ApplyMarkings(lines, CopyAreas(), markingSelected, "Changed the curvature of the line");
            };
            markingOptions.DeleteRequested += DeleteSelectedMarking;
            markingOptions.CloseRequested += () => SetTool(EditorTool.Drag);
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
            markingLayout = null;
            markingAnchors = new List<OmsiMarkingAnchor>();
            markingAnchorPoints.Clear();
            markingAnchorHeights.Clear();
            markingPieces = new List<OmsiMarkingPiece>();
            markingFrom = -1;
            markingHover = -1;
            markingSelected = -1;
            markingPiece = -1;
            markingCorners.Clear();
            markingArea = -1;
            markingArrow = -1;
            markingOptions?.ShowLine(null, null);
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
            var keepArea = ReferenceEquals(record, markingJunction) ? markingArea : -1;
            var keepArrow = ReferenceEquals(record, markingJunction) ? markingArrow : -1;
            markingTile = tile;
            markingJunction = record;
            markingMade = made;
            markingLayout = layout;
            markingAnchors = OmsiJunctionMarkings.AllAnchors(layout, made.Markings);
            markingPieces = OmsiJunctionMarkings.Pieces(markingAnchors, made.Markings);
            markingPiece = -1;
            markingAnchorPoints.Clear();
            markingAnchorHeights.Clear();
            foreach (var anchor in markingAnchors)
            {
                markingAnchorPoints.Add(ObjectToWorld(tile, record, anchor.X, anchor.Y));
                markingAnchorHeights.Add(record.Height + anchor.Height + OmsiJunctionMarkings.Lift);
            }
            markingFrom = -1;
            // Anchors are counted anew, so an outline begun is let go.
            markingCorners.Clear();
            markingSelected = keep >= 0 && keep < made.Markings.Count ? keep : -1;
            markingArea = keepArea >= 0 && keepArea < made.Areas.Count ? keepArea : -1;
            markingArrow = keepArrow >= 0 && keepArrow < made.Arrows.Count ? keepArrow : -1;
            markingOptions.ShowLine(markingSelected >= 0 ? made.Markings[markingSelected] : null, null);
            if (markingArea >= 0)
                markingOptions.ShowArea(made.Areas[markingArea]);
            if (markingArrow >= 0)
                markingOptions.ShowArrow(made.Arrows[markingArrow]);
            markingOptions.ShowJunction("Junction #" + record.Id + ": " + made.Arms.Count + " arms, " + made.Markings.Count + " line(s), " + made.Areas.Count +
                                        " area(s), " + made.Arrows.Count + " arrow(s).");
            if (selection.Count != 1 || !ReferenceEquals(selection[0].Record, record))
            {
                selection.Clear();
                selection.Add(new MapSelectionItem { Tile = tile, Record = record });
                ApplySelection(tile);
            }
            UpdateMarkingPreview();
            SetStatus("Markings of junction #" + record.Id + ": click an anchor, then another to draw a line; click a line or an area to select it. Esc leaves the tool.");
            return true;
        }

        /// <summary>An area's outline in world metres, closed, or null when its corners are gone.</summary>
        private OmsiMapPoint[] AreaPolygon(OmsiMarkingArea area)
        {
            var outline = OmsiJunctionMarkings.Outline(markingAnchors, markingMade.Markings, area);
            if (outline == null)
                return null;
            var points = new OmsiMapPoint[outline.Count + 1];
            for (var index = 0; index < outline.Count; index++)
                points[index] = ObjectToWorld(markingTile, markingJunction, outline[index][0], outline[index][1]);
            points[outline.Count] = points[0];
            return points;
        }

        /// <summary>The area of the junction a point lies in, the last drawn where several overlap, or -1.</summary>
        private int MarkingAreaAt(OmsiMapPoint point)
        {
            if (markingMade == null)
                return -1;
            for (var area = markingMade.Areas.Count - 1; area >= 0; area--)
            {
                var polygon = AreaPolygon(markingMade.Areas[area]);
                if (polygon == null)
                    continue;
                var inside = false;
                for (int index = 0, previous = polygon.Length - 1; index < polygon.Length; previous = index++)
                    if (polygon[index].Y > point.Y != polygon[previous].Y > point.Y &&
                        point.X < (polygon[previous].X - polygon[index].X) * (point.Y - polygon[index].Y) / (polygon[previous].Y - polygon[index].Y) + polygon[index].X)
                        inside = !inside;
                if (inside)
                    return area;
            }
            return -1;
        }

        /// <summary>
        /// A click on an anchor while outlining an area: adds it as the next corner, takes the last corner back when clicked
        /// again, or closes the area on the first corner and marks it, as one undo step.
        /// </summary>
        private void ClickAreaCorner(int anchor)
        {
            var fill = markingOptions.AreaFill ?? OmsiMarkingFill.Hatched;
            if (markingCorners.Count >= 3 && anchor == markingCorners[0])
            {
                var area = new OmsiMarkingArea { Fill = fill, Angle = markingOptions.Angle };
                foreach (var corner in markingCorners)
                    area.Corners.Add(markingAnchors[corner].Key);
                markingCorners.Clear();
                var areas = CopyAreas();
                areas.Add(area);
                ApplyMarkings(CopyMarkings(), areas, -1, "Marked a " + OmsiMarkingFillInfo.Of(fill).Name.ToLowerInvariant());
                return;
            }
            if (markingCorners.Count > 0 && anchor == markingCorners[markingCorners.Count - 1])
            {
                markingCorners.RemoveAt(markingCorners.Count - 1);
                SetStatus("Took the last corner back.");
            }
            else if (markingCorners.Contains(anchor))
                SetStatus(anchor == markingCorners[0] ? "An area needs three corners at least." : "That anchor is a corner already; click the first corner to close the area.");
            else
            {
                markingCorners.Add(anchor);
                SetStatus(markingCorners.Count >= 3
                    ? "Corner " + markingCorners.Count + ": click the next anchor, or the first one to close the area."
                    : "Corner " + markingCorners.Count + ": click the next anchor round the area.");
            }
            UpdateMarkingPreview();
        }

        private List<OmsiMarkingArea> CopyAreas()
        {
            var areas = new List<OmsiMarkingArea>();
            foreach (var area in markingMade.Areas)
                areas.Add(area.Copy());
            return areas;
        }

        private List<OmsiMarkingArrow> CopyArrows()
        {
            var arrows = new List<OmsiMarkingArrow>();
            foreach (var arrow in markingMade.Arrows)
                arrows.Add(arrow.Copy());
            return arrows;
        }

        /// <summary>A point in world metres in the frame of the marked junction (x right, y forward), as ObjectToWorld undoes.</summary>
        private double[] WorldToObject(OmsiMapPoint point)
        {
            var centre = OmsiMapGeometry.TileLocalToWorld(markingTile.X, markingTile.Y, markingJunction.LocalX, markingJunction.LocalY, currentSummary.TileSizeMeters);
            var heading = markingJunction.HeadingDegrees * Math.PI / 180.0;
            var dx = point.X - centre.X;
            var dy = point.Y - centre.Y;
            return new[] { dx * Math.Cos(heading) - dy * Math.Sin(heading), dx * Math.Sin(heading) + dy * Math.Cos(heading) };
        }

        /// <summary>An arrow as lines in world metres, its shafts and heads; null when it cannot lie.</summary>
        private List<OmsiMapPoint[]> ArrowLines(OmsiMarkingArrow arrow)
        {
            var outlines = OmsiJunctionMarkings.ArrowOutlines(markingAnchors, arrow);
            if (outlines == null)
                return null;
            var lines = new List<OmsiMapPoint[]>();
            foreach (var outline in outlines)
                lines.Add(outline.ConvertAll(point => ObjectToWorld(markingTile, markingJunction, point[0], point[1])).ToArray());
            return lines;
        }

        /// <summary>The arrow of the junction under a point, the one whose middle line lies nearest, or -1.</summary>
        private int MarkingArrowAt(OmsiMapPoint point)
        {
            if (markingMade == null)
                return -1;
            var local = WorldToObject(point);
            var best = -1;
            var bestAcross = 1.6;
            for (var arrow = 0; arrow < markingMade.Arrows.Count; arrow++)
            {
                if (!OmsiJunctionMarkings.ArrowFrame(markingAnchors, markingMade.Arrows[arrow], out var tail, out var direction, out _))
                    continue;
                var dx = local[0] - tail[0];
                var dy = local[1] - tail[1];
                var along = dx * direction[0] + dy * direction[1];
                var across = Math.Abs(dx * direction[1] - dy * direction[0]);
                if (along < -0.3 || along > OmsiJunctionMarkings.ArrowLength + 0.3 || across > bestAcross)
                    continue;
                best = arrow;
                bestAcross = across;
            }
            return best;
        }

        /// <summary>
        /// The arrow the chosen kind would make in the lane under a point, its middle at the point to half a metre, at least
        /// at the mouth; null off the lanes.
        /// </summary>
        private OmsiMarkingArrow ArrowAt(OmsiMapPoint point)
        {
            if (markingMade == null || !markingOptions.ArrowKind.HasValue)
                return null;
            var local = WorldToObject(point);
            var lane = OmsiJunctionMarkings.LaneAt(markingAnchors, local[0], local[1], 15.0);
            if (lane == null)
                return null;
            var outX = -Math.Sin(lane[0].InwardDegrees * Math.PI / 180.0);
            var outY = -Math.Cos(lane[0].InwardDegrees * Math.PI / 180.0);
            var along = (local[0] - (lane[0].X + lane[1].X) / 2.0) * outX + (local[1] - (lane[0].Y + lane[1].Y) / 2.0) * outY;
            return new OmsiMarkingArrow
            {
                From = lane[0].Key, To = lane[1].Key, Kind = markingOptions.ArrowKind.Value, Outward = !OmsiJunctionMarkings.RunsIn(markingLayout, lane[0], lane[1]),
                Distance = Math.Max(0.0, Math.Round((along - OmsiJunctionMarkings.ArrowLength / 2.0) * 2.0) / 2.0)
            };
        }

        private void SelectMarkingArrow(int arrow)
        {
            markingFrom = -1;
            markingSelected = -1;
            markingPiece = -1;
            markingArea = -1;
            markingArrow = arrow;
            markingOptions.ShowArrow(markingMade.Arrows[arrow]);
            UpdateMarkingPreview();
            SetStatus("Selected a " + OmsiMarkingArrowInfo.Of(markingMade.Arrows[arrow].Kind).Name.ToLowerInvariant() +
                      " arrow: choose another kind or distance in the panel, Del deletes it.");
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

        /// <summary>The piece of a line of the junction nearest a point within reach, by its place in markingPieces, or -1.</summary>
        private int MarkingPieceAt(OmsiMapPoint point)
        {
            if (markingMade == null)
                return -1;
            var best = -1;
            var bestDistance = MarkingPickReach();
            for (var piece = 0; piece < markingPieces.Count; piece++)
            {
                var points = PiecePolyline(markingPieces[piece]);
                if (points == null)
                    continue;
                for (var index = 1; index < points.Length; index++)
                {
                    var distance = DistanceToSegment(point, points[index - 1], points[index]);
                    if (distance <= bestDistance)
                    {
                        best = piece;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        /// <summary>A piece of a line in world metres, or null when the line's anchors are gone.</summary>
        private OmsiMapPoint[] PiecePolyline(OmsiMarkingPiece piece)
        {
            var path = OmsiJunctionMarkings.Path(markingAnchors, markingMade.Markings[piece.Line]);
            if (path == null)
                return null;
            var cut = OmsiJunctionMarkings.Cut(path, piece.From, piece.To);
            var points = new OmsiMapPoint[cut.Count];
            for (var index = 0; index < cut.Count; index++)
                points[index] = ObjectToWorld(markingTile, markingJunction, cut[index][0], cut[index][1]);
            return points;
        }

        /// <summary>
        /// Changes the setting of a piece of a line: the one kept within the piece, or a new one at its middle. A setting that
        /// neither hides nor restyles its piece goes.
        /// </summary>
        private static void ChangePiece(OmsiMarkingLine line, OmsiMarkingPiece piece, Action<OmsiMarkingPieceSetting> change)
        {
            var setting = piece.LineLength > 0.0 ? line.SettingBetween(piece.From / piece.LineLength, piece.To / piece.LineLength) : null;
            if (setting == null)
            {
                setting = new OmsiMarkingPieceSetting { At = piece.Middle, Style = piece.Style == line.Style ? (OmsiMarkingStyle?)null : piece.Style, Hidden = piece.Hidden };
                line.Pieces.Add(setting);
            }
            change(setting);
            line.Pieces.RemoveAll(candidate => !candidate.Hidden && !candidate.Style.HasValue);
        }

        /// <summary>Puts the suggestion after the right of way in place of the junction's lines, as one undo step.</summary>
        private void SuggestMarkings()
        {
            if (markingMade == null || markingLayout == null)
            {
                SetStatus("Markings: click a junction made in this project first.");
                return;
            }
            var lines = OmsiJunctionMarkings.Suggest(markingLayout);
            if (lines.Count == 0)
            {
                SetStatus("Junction #" + markingJunction.Id + " has no lanes to suggest markings for.");
                return;
            }
            var replaced = markingMade.Markings.Count;
            // Areas stay where their corners still are; those at crossings of lines that go, go too.
            var anchors = OmsiJunctionMarkings.AllAnchors(markingLayout, lines);
            var areas = CopyAreas();
            var gone = areas.RemoveAll(area => area.Corners.Exists(corner => !anchors.Exists(anchor => anchor.Key == corner)));
            ApplyMarkings(lines, areas, -1, "Put in " + lines.Count + " suggested line(s)" + (replaced > 0 ? " in place of " + replaced : string.Empty) +
                                            (gone > 0 ? ", without " + gone + " area(s) at crossings that went" : string.Empty));
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
            for (var area = 0; area < markingMade.Areas.Count; area++)
            {
                var points = AreaPolygon(markingMade.Areas[area]);
                if (points != null && area != markingArea)
                    lines.Add(points);
            }
            for (var arrow = 0; arrow < markingMade.Arrows.Count; arrow++)
            {
                var parts = arrow != markingArrow ? ArrowLines(markingMade.Arrows[arrow]) : null;
                if (parts != null)
                    lines.AddRange(parts);
            }
            // The lines are many points each; no dots at them.
            mapCanvas.SetRoadGuides(lines, false);

            var shown = new List<OmsiMapPoint[]>();
            string plan;
            if (markingCorners.Count > 0)
            {
                // With the anchor under the pointer as the next corner, the area shows as it would be closed.
                var corners = new List<int>(markingCorners);
                if (markingHover >= 0 && !corners.Contains(markingHover))
                    corners.Add(markingHover);
                var outline = new OmsiMarkingArea();
                foreach (var corner in corners)
                    outline.Corners.Add(markingAnchors[corner].Key);
                var polygon = corners.Count >= 3 ? AreaPolygon(outline) : null;
                if (polygon != null)
                    shown.Add(polygon);
                else
                {
                    var open = new List<OmsiMapPoint>();
                    foreach (var corner in corners)
                        open.Add(markingAnchorPoints[corner]);
                    if (markingHover < 0 && roadPointer.HasValue)
                        open.Add(roadPointer.Value);
                    if (open.Count >= 2)
                        shown.Add(open.ToArray());
                }
                plan = markingHover >= 0 && markingHover == markingCorners[0] && markingCorners.Count >= 3
                    ? "Click to close the " + OmsiMarkingFillInfo.Of(markingOptions.AreaFill ?? OmsiMarkingFill.Hatched).Name.ToLowerInvariant() + "."
                    : "Click the next anchor round the area, the first to close it; Esc lets go.";
            }
            else if (markingArea >= 0)
            {
                var polygon = AreaPolygon(markingMade.Areas[markingArea]);
                if (polygon != null)
                    shown.Add(polygon);
                plan = "Selected: " + OmsiMarkingFillInfo.Of(markingMade.Areas[markingArea].Fill).Name.ToLowerInvariant() +
                       ". Choose another fill or stripe angle, or press Del.";
            }
            else if (markingArrow >= 0)
            {
                var parts = ArrowLines(markingMade.Arrows[markingArrow]);
                if (parts != null)
                    shown.AddRange(parts);
                plan = "Selected: " + OmsiMarkingArrowInfo.Of(markingMade.Arrows[markingArrow].Kind).Name.ToLowerInvariant() +
                       " arrow. Choose another kind or distance, or press Del.";
            }
            else if (markingFrom >= 0)
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
                // In piece scope only the clicked piece shows as selected, else the whole line.
                var piece = markingPiece >= 0 && markingOptions.PieceScope ? PiecePolyline(markingPieces[markingPiece]) : null;
                var selectedLine = piece ?? MarkingPolyline(markingMade.Markings[markingSelected]);
                if (selectedLine != null)
                    shown.Add(selectedLine);
                var count = markingPieces.FindAll(candidate => candidate.Line == markingSelected).Count;
                plan = "Selected: " + OmsiMarkingStyleInfo.Of(markingMade.Markings[markingSelected].Style).Name.ToLowerInvariant() +
                       (count > 1 ? " in " + count + " pieces" : string.Empty) + ". Choose another style or curvature, hide the piece, or press Del.";
            }
            else if (markingOptions.ArrowKind.HasValue)
            {
                // The arrow a click would lay, in the lane under the pointer.
                var candidate = roadPointer.HasValue ? ArrowAt(roadPointer.Value) : null;
                var parts = candidate != null ? ArrowLines(candidate) : null;
                if (parts != null)
                    shown.AddRange(parts);
                plan = parts != null
                    ? "Click to lay the arrow " + candidate.Distance.ToString("0.#", CultureInfo.InvariantCulture) + " m behind the mouth" +
                      (candidate.Outward ? ", pointing out of the junction as the lane runs." : ".")
                    : "Point at a lane of an arm to lay an arrow, or click an arrow to select it.";
            }
            else if (markingOptions.AreaFill.HasValue)
                plan = markingHover >= 0 ? "Click to start an area at this anchor." : "Click the anchors round an area, or an area to select it.";
            else
                plan = markingHover >= 0 ? "Click to start a line at this anchor." : "Click an anchor to start a line, or a line or an area to select it.";
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
            // With an arrow kind chosen a click selects an arrow or lays one in the lane under it.
            if (markingJunction != null && markingOptions.ArrowKind.HasValue)
            {
                var arrowHit = MarkingArrowAt(point);
                if (arrowHit >= 0)
                {
                    SelectMarkingArrow(arrowHit);
                    return;
                }
                var arrow = ArrowAt(point);
                if (arrow != null)
                {
                    var arrows = CopyArrows();
                    arrows.Add(arrow);
                    ApplyMarkings(CopyMarkings(), CopyAreas(), arrows, -1, "Laid a " + OmsiMarkingArrowInfo.Of(arrow.Kind).Name.ToLowerInvariant() + " arrow");
                    return;
                }
            }
            var anchor = markingJunction != null && !markingOptions.ArrowKind.HasValue ? MarkingAnchorAt(point) : -1;
            if (anchor >= 0)
            {
                markingSelected = -1;
                markingArea = -1;
                markingArrow = -1;
                markingOptions.ShowLine(null, null);
                if (markingOptions.AreaFill.HasValue)
                {
                    markingFrom = -1;
                    ClickAreaCorner(anchor);
                    return;
                }
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
                ApplyMarkings(lines, CopyAreas(), -1, "Drew a " + OmsiMarkingStyleInfo.Of(line.Style).Name.ToLowerInvariant());
                return;
            }
            if (markingCorners.Count > 0)
            {
                SetStatus("Click the next anchor round the area, or the first one to close it; Esc lets go of the area.");
                return;
            }
            var hit = markingJunction != null ? MarkingPieceAt(point) : -1;
            if (hit >= 0)
            {
                markingFrom = -1;
                markingArea = -1;
                markingArrow = -1;
                markingPiece = hit;
                markingSelected = markingPieces[hit].Line;
                markingOptions.ShowLine(markingMade.Markings[markingSelected], markingPieces[hit]);
                UpdateMarkingPreview();
                SetStatus("Selected a " + OmsiMarkingStyleInfo.Of(markingMade.Markings[markingSelected].Style).Name.ToLowerInvariant() +
                          " and its piece under the pointer: choose another style or curvature in the panel, hide the piece, Y switches the style, Del deletes the line.");
                return;
            }
            var arrowAt = markingJunction != null ? MarkingArrowAt(point) : -1;
            if (arrowAt >= 0)
            {
                SelectMarkingArrow(arrowAt);
                return;
            }
            var areaHit = markingJunction != null ? MarkingAreaAt(point) : -1;
            if (areaHit >= 0)
            {
                markingFrom = -1;
                markingSelected = -1;
                markingPiece = -1;
                markingArrow = -1;
                markingArea = areaHit;
                markingOptions.ShowArea(markingMade.Areas[areaHit]);
                UpdateMarkingPreview();
                SetStatus("Selected a " + OmsiMarkingFillInfo.Of(markingMade.Areas[areaHit].Fill).Name.ToLowerInvariant() +
                          ": choose another fill or stripe angle in the panel, Del deletes it.");
                return;
            }
            if (MadeJunctionAt(point, out var tile, out var record) && !ReferenceEquals(record, markingJunction))
            {
                TargetMarkingJunction(tile, record);
                return;
            }
            markingFrom = -1;
            markingSelected = -1;
            markingArea = -1;
            markingArrow = -1;
            markingOptions.ShowLine(null, null);
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
            if (markingMade == null)
                return;
            if (markingArrow >= 0)
            {
                var keptArrows = CopyArrows();
                keptArrows.RemoveAt(markingArrow);
                markingArrow = -1;
                ApplyMarkings(CopyMarkings(), CopyAreas(), keptArrows, -1, "Deleted the arrow");
                return;
            }
            if (markingArea >= 0)
            {
                var kept = CopyAreas();
                kept.RemoveAt(markingArea);
                markingArea = -1;
                ApplyMarkings(CopyMarkings(), kept, -1, "Deleted the area");
                return;
            }
            if (markingSelected < 0)
                return;
            var lines = CopyMarkings();
            var gone = lines[markingSelected].Ends;
            lines.RemoveAt(markingSelected);
            bool AtItsCrossing(string key) => key.StartsWith("x", StringComparison.Ordinal) && key.Contains(gone);
            // Lines and areas drawn from a crossing of this line lose their anchor and go with it.
            var dependent = lines.RemoveAll(line => AtItsCrossing(line.From) || AtItsCrossing(line.To));
            var areas = CopyAreas();
            var dependentAreas = areas.RemoveAll(area => area.Corners.Exists(AtItsCrossing));
            ApplyMarkings(lines, areas, -1, "Deleted the line" + (dependent > 0 ? " and " + dependent + " line(s) drawn from its crossings" : string.Empty) +
                                            (dependentAreas > 0 ? ", with " + dependentAreas + " area(s) at them" : string.Empty));
        }

        /// <summary>Switches to the next marking of the chosen kind, for the next one or the selected one, as Y does.</summary>
        private void NextMarkingStyle()
        {
            if (markingOptions == null)
                return;
            markingOptions.ChooseNext();
            SetStatus("Markings: " + markingOptions.ChoiceName.ToLowerInvariant() + ".");
        }

        /// <summary>
        /// Makes the junction again with these markings under Sceneryobjects\Mapforge and puts it in place of the old one, as
        /// one undo step, with its arrows as they are.
        /// </summary>
        private void ApplyMarkings(List<OmsiMarkingLine> lines, List<OmsiMarkingArea> areas, int select, string done, int selectArea = -1)
        {
            ApplyMarkings(lines, areas, CopyArrows(), select, done, selectArea);
        }

        /// <summary>
        /// Makes the junction again with these markings under Sceneryobjects\Mapforge and puts it in place of the old one, as
        /// one undo step; <paramref name="select"/> is the line to keep selected, <paramref name="selectArea"/> the area and
        /// <paramref name="selectArrow"/> the arrow, each -1 for none.
        /// </summary>
        private void ApplyMarkings(List<OmsiMarkingLine> lines, List<OmsiMarkingArea> areas, List<OmsiMarkingArrow> arrows, int select, string done,
            int selectArea = -1, int selectArrow = -1)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("draw road markings"));
                return;
            }
            var record = markingJunction;
            var made = markingMade;
            double? pieceAt = markingPiece >= 0 ? markingPieces[markingPiece].Middle : (double?)null;
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
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, made.Name, OmsiJunctionGenerator.Junction(turn.Arms, plain, "Mapforge " + made.Name, lines, areas, arrows));
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
            markingArea = -1;
            markingArrow = -1;
            TargetMarkingJunction(markingTile, record);
            if (selectArea >= 0 && selectArea < markingMade.Areas.Count)
            {
                markingArea = selectArea;
                markingOptions.ShowArea(markingMade.Areas[selectArea]);
                UpdateMarkingPreview();
            }
            if (selectArrow >= 0 && selectArrow < markingMade.Arrows.Count)
            {
                markingArrow = selectArrow;
                markingOptions.ShowArrow(markingMade.Arrows[selectArrow]);
                UpdateMarkingPreview();
            }
            if (select >= 0 && select < markingMade.Markings.Count)
            {
                markingSelected = select;
                // The piece clicked stays selected where it still is: the one of its line round the point it was at.
                markingPiece = -1;
                if (pieceAt.HasValue)
                    for (var index = 0; index < markingPieces.Count; index++)
                        if (markingPieces[index].Line == select && markingPieces[index].LineLength > 0.0 &&
                            pieceAt.Value >= markingPieces[index].From / markingPieces[index].LineLength && pieceAt.Value <= markingPieces[index].To / markingPieces[index].LineLength)
                            markingPiece = index;
                markingOptions.ShowLine(markingMade.Markings[select], markingPiece >= 0 ? markingPieces[markingPiece] : null);
                UpdateMarkingPreview();
            }
            SetStatus(done + " on junction #" + record.Id + ", now " + markingMade.Markings.Count + " line(s), " + markingMade.Areas.Count + " area(s) and " + markingMade.Arrows.Count + " arrow(s), made again as " +
                      Path.GetFileNameWithoutExtension(assetPath) + ". Ctrl+Z takes it back.");
        }

        /// <summary>
        /// For command-line checks: opens the markings tool on the selected junction and, unless the argument is "show", draws
        /// lines given as <c>from-to:style</c>, separated by commas, such as <c>1.0.1-2.0.1:laneline,0.1.0-0.1.1:stop</c>, and
        /// areas given as <c>area:fill:corner+corner+corner[:angle]</c>, such as <c>area:hatched:1.0.0+1.0.2+1.1.2+1.1.0</c>,
        /// and arrows given as <c>arrow:kind:from~to[:distance]</c>, such as <c>arrow:straightleft:1.1.0~1.1.1:2</c>. The
        /// status goes to the log as "Markings:".
        /// </summary>
        private void RunMarkingArgument(string argument)
        {
            SetTool(EditorTool.Markings);
            for (var index = 0; index < markingAnchors.Count; index++)
                Debug.Log("Marking anchor " + markingAnchors[index].Key + " (" + markingAnchors[index].Kind + ") at " +
                          markingAnchorPoints[index].X.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                          markingAnchorPoints[index].Y.ToString("0.0", CultureInfo.InvariantCulture));
            if (markingJunction != null && string.Equals(argument, "auto", StringComparison.OrdinalIgnoreCase))
                SuggestMarkings();
            else if (markingJunction != null && !argument.StartsWith("show", StringComparison.OrdinalIgnoreCase))
            {
                var lines = CopyMarkings();
                var areas = CopyAreas();
                var arrows = CopyArrows();
                foreach (var part in argument.Split(','))
                {
                    var pieces = part.Split(':');
                    if (pieces.Length >= 3 && pieces[0].Trim() == "area" && OmsiMarkingFillInfo.ByKey(pieces[1].Trim()) is OmsiMarkingFillInfo fill)
                    {
                        var area = new OmsiMarkingArea { Fill = fill.Fill };
                        foreach (var corner in pieces[2].Split('+'))
                            area.Corners.Add(corner.Trim());
                        if (pieces.Length > 3 && double.TryParse(pieces[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
                            area.Angle = angle;
                        areas.Add(area);
                        continue;
                    }
                    if (pieces.Length >= 3 && pieces[0].Trim() == "arrow" && OmsiMarkingArrowInfo.ByKey(pieces[1].Trim()) is OmsiMarkingArrowInfo kind)
                    {
                        var lane = pieces[2].Split('~');
                        var from = markingAnchors.Find(candidate => candidate.Key == lane[0].Trim());
                        var to = lane.Length == 2 ? markingAnchors.Find(candidate => candidate.Key == lane[1].Trim()) : null;
                        if (from == null || to == null)
                            continue;
                        var arrow = new OmsiMarkingArrow { From = from.Key, To = to.Key, Kind = kind.Kind, Outward = !OmsiJunctionMarkings.RunsIn(markingLayout, from, to) };
                        if (pieces.Length > 3 && double.TryParse(pieces[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
                            arrow.Distance = distance;
                        arrows.Add(arrow);
                        continue;
                    }
                    var ends = pieces[0].Split('-');
                    if (ends.Length != 2)
                        continue;
                    var style = pieces.Length > 1 ? OmsiMarkingStyleInfo.ByKey(pieces[1]) : null;
                    lines.Add(new OmsiMarkingLine { From = ends[0].Trim(), To = ends[1].Trim(), Style = style?.Style ?? markingOptions.Style });
                }
                ApplyMarkings(lines, areas, arrows, -1, "Drew " + (lines.Count - markingMade.Markings.Count) + " line(s), " + (areas.Count - markingMade.Areas.Count) +
                                                        " area(s) and " + (arrows.Count - markingMade.Arrows.Count) + " arrow(s)");
            }
            // Then the menu as a click would leave it: "choose:key" picks a marking, "select:line|area|arrow:n" selects one.
            foreach (var part in argument.Split(','))
            {
                var pieces = part.Trim().Split(':');
                if (pieces.Length == 2 && pieces[0] == "choose")
                {
                    if (OmsiMarkingStyleInfo.ByKey(pieces[1]) is OmsiMarkingStyleInfo style)
                        markingOptions.ChooseStyle(style.Style);
                    else if (OmsiMarkingFillInfo.ByKey(pieces[1]) is OmsiMarkingFillInfo fill)
                        markingOptions.ChooseFill(fill.Fill);
                    else if (OmsiMarkingArrowInfo.ByKey(pieces[1]) is OmsiMarkingArrowInfo kind)
                        markingOptions.ChooseArrow(kind.Kind);
                }
                else if (pieces.Length == 3 && pieces[0] == "select" && markingMade != null && int.TryParse(pieces[2], out var index) && index >= 0)
                {
                    if (pieces[1] == "line" && index < markingMade.Markings.Count)
                    {
                        markingSelected = index;
                        markingOptions.ShowLine(markingMade.Markings[index], null);
                    }
                    else if (pieces[1] == "area" && index < markingMade.Areas.Count)
                    {
                        markingArea = index;
                        markingOptions.ShowArea(markingMade.Areas[index]);
                    }
                    else if (pieces[1] == "arrow" && index < markingMade.Arrows.Count)
                        SelectMarkingArrow(index);
                    UpdateMarkingPreview();
                }
            }
            Debug.Log("Markings: " + statusLabel?.text);
        }

        /// <summary>Esc with the markings tool: lets go of the anchor or the area begun, then of the selected line or area, then leaves the tool.</summary>
        private void EscapeMarking()
        {
            if (markingFrom >= 0 || markingSelected >= 0 || markingCorners.Count > 0 || markingArea >= 0 || markingArrow >= 0)
            {
                markingFrom = -1;
                markingSelected = -1;
                markingPiece = -1;
                markingCorners.Clear();
                markingArea = -1;
                markingArrow = -1;
                markingOptions.ShowLine(null, null);
                UpdateMarkingPreview();
                SetStatus("Markings: click an anchor to start a line, or a line to select it; Esc leaves the tool.");
            }
            else
                SetTool(EditorTool.Drag);
        }
    }
}
