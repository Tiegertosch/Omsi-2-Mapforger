using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The lanes tool (ADR 048), modelled on the lane connector of Traffic for Cities: Skylines II: on a junction made in
    /// this project it shows a dot where each lane leads in and a ring where each leads out; a click on a lane in and then
    /// on a lane out adds the way between them or takes it away. Every change makes the junction again with its lanes,
    /// keeping its markings and the right of way of its arms, as one undo step.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement laneOptions;
        private Label laneJunctionLabel;
        private Label lanePlanLabel;
        private OmsiTileSummary laneTile;
        private OmsiObjectRecord laneJunction;
        private OmsiGeneratedJunction laneMade;
        private List<OmsiLaneEnd> laneEnds = new List<OmsiLaneEnd>();
        private readonly List<OmsiMapPoint> laneEndPoints = new List<OmsiMapPoint>();
        private readonly List<double> laneEndHeights = new List<double>();
        private HashSet<string> laneConnections = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The ways through the junction has without chosen ones, as it was made.</summary>
        private HashSet<string> laneDefaultWays = new HashSet<string>(StringComparer.Ordinal);

        private int laneSource = -1;
        private int laneHover = -1;
        private VisualElement laneTurnRows;
        private TextField laneTaperField;
        private TextField laneStorageField;

        private void CreateLaneOptions()
        {
            laneOptions = ToolCatalog.Options();
            laneOptions.style.display = DisplayStyle.None;
            laneJunctionLabel = new Label();
            laneJunctionLabel.AddToClassList("panel-info");
            laneJunctionLabel.style.marginTop = 0f;
            laneOptions.Add(laneJunctionLabel);
            var legend = LocBindings.NewLabel("lanes.legend");
            legend.AddToClassList("panel-info");
            laneOptions.Add(legend);
            var reset = LocBindings.WithTooltip(LocBindings.NewButton("lanes.every", () => ApplyLaneConnections(null, Loc.T("lanes.done.every"))), "lanes.every.tooltip");
            reset.AddToClassList("map-control-button");
            laneOptions.Add(reset);
            var none = LocBindings.WithTooltip(LocBindings.NewButton("lanes.none", () => ApplyLaneConnections(new HashSet<string>(StringComparer.Ordinal), Loc.T("lanes.done.none"))),
                "lanes.none.tooltip");
            none.AddToClassList("map-control-button");
            laneOptions.Add(none);

            // Left-turn lanes out of the median of an arm (ADR 051, stage 2).
            var turnTitle = LocBindings.NewLabel("lanes.turn.title");
            turnTitle.AddToClassList("picker-options-title");
            turnTitle.style.marginTop = 8f;
            laneOptions.Add(turnTitle);
            laneTaperField = LaneNumberRow("lanes.taper", OmsiTurnLane.DefaultTaperMeters, "lanes.taper.tooltip");
            laneStorageField = LaneNumberRow("lanes.storage", OmsiTurnLane.DefaultStorageMeters, "lanes.storage.tooltip");
            laneTurnRows = new VisualElement();
            laneOptions.Add(laneTurnRows);
            CreateLaneCrossingOptions();

            lanePlanLabel = new Label();
            lanePlanLabel.AddToClassList("panel-info");
            laneOptions.Add(lanePlanLabel);
            var hint = LocBindings.NewLabel("lanes.hint");
            hint.AddToClassList("panel-info");
            laneOptions.Add(hint);
            objectsPanelHost.Add(laneOptions);
        }

        private TextField LaneNumberRow(string labelKey, double value, string tooltipKey)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            LocBindings.Tooltip(row, tooltipKey); // i18n:keys
            var text = LocBindings.NewLabel(labelKey); // i18n:keys
            text.AddToClassList("picker-option-label");
            row.Add(text);
            var field = new TextField { value = value.ToString("0.#", CultureInfo.InvariantCulture) };
            LocBindings.Tooltip(field, tooltipKey); // i18n:keys
            field.AddToClassList("picker-field");
            row.Add(field);
            laneOptions.Add(row);
            return field;
        }

        /// <summary>The lengths in the fields, kept within the bounds a turn lane allows.</summary>
        private OmsiTurnLane LaneTurnLengths()
        {
            double Read(TextField field, double fallback) =>
                double.TryParse(field.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var meters) ? OmsiTurnLane.Clamp(meters) : fallback;
            return new OmsiTurnLane
            {
                TaperMeters = Read(laneTaperField, OmsiTurnLane.DefaultTaperMeters), StorageMeters = Read(laneStorageField, OmsiTurnLane.DefaultStorageMeters)
            };
        }

        /// <summary>A row for each arm of the junction with a median: a left-turn lane to add, or to take away or give the lengths in the fields.</summary>
        private void RefreshLaneTurnRows(OmsiJunctionLayout layout)
        {
            if (laneTurnRows == null)
                return;
            laneTurnRows.Clear();
            if (layout == null || laneMade == null)
                return;
            for (var arm = 0; arm < layout.Arms.Count && arm < laneMade.Arms.Count; arm++)
            {
                var lane = laneMade.Arms[arm].TurnLane;
                if (lane == null && (arm >= layout.IslandNoses.Length || double.IsNaN(layout.IslandNoses[arm])))
                    continue;
                var each = arm;
                var direction = LaneArmDirection(layout.Arms[arm]);
                var toggle = new Button(() => ApplyTurnLane(each, lane == null ? LaneTurnLengths() : null));
                if (lane == null)
                {
                    LocBindings.Text(toggle, "lanes.turn.add", ("n", arm + 1), ("direction", direction));
                    LocBindings.Tooltip(toggle, "lanes.turn.add.tooltip");
                }
                else
                {
                    LocBindings.Text(toggle, "lanes.turn.remove", ("n", arm + 1), ("direction", direction), ("taper", lane.TaperMeters), ("storage", lane.StorageMeters));
                    LocBindings.Tooltip(toggle, "lanes.turn.remove.tooltip");
                }
                toggle.AddToClassList("map-control-button");
                toggle.style.whiteSpace = WhiteSpace.Normal;
                HighlightArmOnHover(toggle, layout.Arms[arm]);
                laneTurnRows.Add(toggle);
                if (lane == null)
                    continue;
                var lengths = new Button(() => ApplyTurnLane(each, LaneTurnLengths()));
                LocBindings.Text(lengths, "lanes.turn.lengths", ("n", arm + 1), ("direction", direction));
                LocBindings.Tooltip(lengths, "lanes.turn.lengths.tooltip");
                lengths.AddToClassList("map-control-button");
                lengths.style.whiteSpace = WhiteSpace.Normal;
                HighlightArmOnHover(lengths, layout.Arms[arm]);
                laneTurnRows.Add(lengths);
            }
        }

        /// <summary>The way an arm of the selected junction points on the map, such as "north-east".</summary>
        private string LaneArmDirection(OmsiJunctionArm arm)
        {
            var radians = arm.HeadingDegrees * Math.PI / 180.0;
            var centre = ObjectToWorld(laneTile, laneJunction, 0.0, 0.0);
            var end = ObjectToWorld(laneTile, laneJunction, Math.Sin(radians) * 10.0, Math.Cos(radians) * 10.0);
            var bearing = (Math.Atan2(end.X - centre.X, end.Y - centre.Y) * 180.0 / Math.PI + 360.0) % 360.0;
            var names = new[] { "lanes.dir.n", "lanes.dir.ne", "lanes.dir.e", "lanes.dir.se", "lanes.dir.s", "lanes.dir.sw", "lanes.dir.w", "lanes.dir.nw" };
            return Loc.T(names[(int)Math.Round(bearing / 45.0) % 8]); // i18n:keys
        }

        /// <summary>
        /// Gives an arm of the selected junction a left-turn lane with these lengths, or takes it away with null, making the
        /// junction again as one undo step. Chosen ways through stay, but for the ways from that arm, which become the ones
        /// the junction has without chosen ways.
        /// </summary>
        private void ApplyTurnLane(int arm, OmsiTurnLane lane)
        {
            if (laneJunction == null || laneMade == null || arm < 0 || arm >= laneMade.Arms.Count)
            {
                SetStatusT("lanes.status.first");
                return;
            }
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.change_lanes"));
                return;
            }
            var made = laneMade;
            var before = made.Arms[arm].TurnLane;
            made.Arms[arm].TurnLane = lane;
            try
            {
                var plain = PlainRoadTexture();
                var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, laneTile, laneJunction);
                if (rebuild == null)
                {
                    SetStatusT("lanes.status.mismatch", ("id", laneJunction.Id));
                    return;
                }
                OmsiJunctionLayout layout;
                try
                {
                    layout = OmsiJunctionLayout.Build(rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, plain).Arms, plain);
                }
                catch (ArgumentException exception)
                {
                    SetStatusT("lanes.status.layout_failed", ("id", laneJunction.Id), ("reason", exception.Message));
                    return;
                }
                if (lane != null && arm < layout.TurnLaneProblems.Length && layout.TurnLaneProblems[arm] != null)
                {
                    SetStatusT("lanes.status.no_turn", ("reason", layout.TurnLaneProblems[arm]));
                    return;
                }
                HashSet<string> connections = null;
                if (made.Connections != null)
                {
                    var prefix = arm.ToString(CultureInfo.InvariantCulture) + ".";
                    connections = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var way in made.Connections)
                        if (!way.StartsWith(prefix, StringComparison.Ordinal))
                            connections.Add(way);
                    foreach (var way in layout.WaysThrough)
                        if (way.StartsWith(prefix, StringComparison.Ordinal))
                            connections.Add(way);
                }
                ApplyLaneConnections(connections, lane == null
                    ? Loc.T("lanes.done.turn_removed", ("n", arm + 1))
                    : Loc.T(before == null ? "lanes.done.turn_added" : "lanes.done.turn_changed", ("n", arm + 1), ("taper", lane.TaperMeters), ("storage", lane.StorageMeters)), // i18n:keys
                    layout.WaysThrough);
            }
            finally
            {
                // The junction made again is read from its own file; the one it replaced stays as it was, for undo.
                made.Arms[arm].TurnLane = before;
            }
        }

        private void BeginLaneTool()
        {
            if (laneOptions == null || mapCanvas == null || currentSummary == null)
                return;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (terrainOptions != null)
                terrainOptions.style.display = DisplayStyle.None;
            laneOptions.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.ShowSplineEnds = false;
            mapCanvas.BeginRoad();
            ClearLaneTarget();
            var item = selection.Count == 1 ? selection[0] : null;
            if (item?.Tile == null || !TargetLaneJunction(item.Tile, item.Record))
                SetStatusT("lanes.status.start");
        }

        private void EndLaneTool()
        {
            mapCanvas?.EndRoad();
            if (mapCanvas != null)
            {
                mapCanvas.ShowSplineEnds = true;
                mapCanvas.ClearRoadPreview();
                mapCanvas.SetLaneEnds(null, null, -1);
            }
            if (laneOptions != null)
                laneOptions.style.display = DisplayStyle.None;
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            ClearLaneTarget();
        }

        private void ClearLaneTarget()
        {
            laneTile = null;
            laneJunction = null;
            laneMade = null;
            laneEnds = new List<OmsiLaneEnd>();
            laneEndPoints.Clear();
            laneEndHeights.Clear();
            laneConnections = new HashSet<string>(StringComparer.Ordinal);
            laneSource = -1;
            laneHover = -1;
            if (laneJunctionLabel != null)
                LocBindings.Text(laneJunctionLabel, "lanes.click_junction");
            if (lanePlanLabel != null)
                lanePlanLabel.text = string.Empty;
            mapCanvas?.SetLaneEnds(null, null, -1);
        }

        /// <summary>
        /// The arms of a made junction as it would be made again, laid out with these ways through, null for those it has
        /// without chosen ones; null with the reason shown when it cannot be.
        /// </summary>
        private OmsiJunctionLayout LaneLayout(OmsiTileSummary tile, OmsiObjectRecord record, OmsiGeneratedJunction made, ICollection<string> connections)
        {
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
            if (rebuild == null)
            {
                SetStatusT("lanes.status.mismatch", ("id", record.Id));
                return null;
            }
            try
            {
                return OmsiJunctionLayout.Build(rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, PlainRoadTexture()).Arms, PlainRoadTexture(), connections);
            }
            catch (ArgumentException exception)
            {
                SetStatusT("lanes.status.layout_failed", ("id", record.Id), ("reason", exception.Message));
                return null;
            }
        }

        /// <summary>Works on a junction the editor made: its lane ends and the ways through it has; false when it is no such junction.</summary>
        private bool TargetLaneJunction(OmsiTileSummary tile, OmsiObjectRecord record)
        {
            var made = MadeJunction(record.AssetPath);
            if (made == null)
                return false;
            var layout = LaneLayout(tile, record, made, made.Connections);
            if (layout == null)
                return false;
            var defaults = made.Connections == null ? layout : LaneLayout(tile, record, made, null);
            if (layout.IsTransition)
            {
                SetStatusT("lanes.status.transition", ("id", record.Id));
                return false;
            }
            var keepSource = ReferenceEquals(record, laneJunction) ? laneSource : -1;
            laneTile = tile;
            laneJunction = record;
            laneMade = made;
            laneEnds = layout.LaneEnds();
            laneConnections = new HashSet<string>(layout.WaysThrough, StringComparer.Ordinal);
            laneDefaultWays = new HashSet<string>(defaults?.WaysThrough ?? layout.WaysThrough, StringComparer.Ordinal);
            laneEndPoints.Clear();
            laneEndHeights.Clear();
            foreach (var end in laneEnds)
            {
                laneEndPoints.Add(ObjectToWorld(tile, record, end.X, end.Y));
                laneEndHeights.Add(record.Height + end.Height);
            }
            laneSource = keepSource >= 0 && keepSource < laneEnds.Count && laneEnds[keepSource].Inbound ? keepSource : -1;
            LocBindings.Text(laneJunctionLabel, "lanes.junction", ("id", record.Id), ("arms", made.Arms.Count), ("count", laneConnections.Count),
                ("total", OmsiLaneConnections.All(laneEnds).Count));
            RefreshLaneTurnRows(layout);
            RefreshLaneCrossingRows(layout);
            if (selection.Count != 1 || !ReferenceEquals(selection[0].Record, record))
            {
                selection.Clear();
                selection.Add(new MapSelectionItem { Tile = tile, Record = record });
                ApplySelection(tile);
            }
            UpdateLanePreview();
            SetStatusT("lanes.status.target", ("id", record.Id));
            return true;
        }

        /// <summary>The junction was made again or taken back, such as by undo: its lanes are read anew.</summary>
        private void RefreshLaneTarget()
        {
            if (laneJunction == null || laneTile == null)
                return;
            if (!laneTile.Objects.Contains(laneJunction))
                ClearLaneTarget();
            else if (laneMade == null || !string.Equals(laneMade.AssetPath, laneJunction.AssetPath, StringComparison.OrdinalIgnoreCase))
                TargetLaneJunction(laneTile, laneJunction);
        }

        private int LaneEndAt(OmsiMapPoint point)
        {
            var best = -1;
            var bestDistance = MarkingPickReach();
            for (var index = 0; index < laneEndPoints.Count; index++)
            {
                var distance = Distance(laneEndPoints[index], point);
                if (distance <= bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>A way through in world metres, from a lane end to another.</summary>
        private OmsiMapPoint[] LaneCurve(OmsiLaneEnd from, OmsiLaneEnd to)
        {
            var curve = OmsiLaneConnections.Curve(from, to);
            var points = new OmsiMapPoint[curve.Count];
            for (var index = 0; index < curve.Count; index++)
                points[index] = ObjectToWorld(laneTile, laneJunction, curve[index][0], curve[index][1]);
            return points;
        }

        /// <summary>The heights of a way through's points in the 3D view, rising evenly from one lane end to the other.</summary>
        private double[] LaneCurveHeights(OmsiLaneEnd from, OmsiLaneEnd to, int count)
        {
            var heights = new double[count];
            for (var index = 0; index < count; index++)
                heights[index] = laneJunction.Height + from.Height + (to.Height - from.Height) * index / Math.Max(1, count - 1);
            return heights;
        }

        /// <summary>Shows the lane ends, every way through faintly, the chosen lane's ways brightly, and what a click would do.</summary>
        private void UpdateLanePreview()
        {
            if (mapCanvas == null || activeTool != EditorTool.Lanes)
                return;
            RefreshLaneTarget();
            if (laneJunction == null)
            {
                mapCanvas.ClearRoadPreview();
                mapCanvas.SetLaneEnds(null, null, -1);
                return;
            }
            laneHover = roadPointer.HasValue ? LaneEndAt(roadPointer.Value) : -1;
            var states = new List<int>();
            var guides = new List<OmsiMapPoint[]>();
            var guideHeights = new List<double[]>();
            var shown = new List<OmsiMapPoint[]>();
            var shownHeights = new List<double[]>();
            for (var index = 0; index < laneEnds.Count; index++)
            {
                var end = laneEnds[index];
                var led = laneSource >= 0 && !end.Inbound && laneConnections.Contains(OmsiLaneConnections.Key(laneEnds[laneSource], end));
                states.Add(end.Inbound ? (index == laneSource ? 2 : 0) : led ? 3 : 1);
            }
            foreach (var from in laneEnds)
                foreach (var to in laneEnds)
                    if (from.Inbound && !to.Inbound && laneConnections.Contains(OmsiLaneConnections.Key(from, to)))
                    {
                        var curve = LaneCurve(from, to);
                        var bright = laneSource >= 0 && ReferenceEquals(from, laneEnds[laneSource]);
                        (bright ? shown : guides).Add(curve);
                        (bright ? shownHeights : guideHeights).Add(LaneCurveHeights(from, to, curve.Length));
                    }
            string plan;
            if (laneSource < 0)
                plan = Loc.T(laneHover >= 0 && laneEnds[laneHover].Inbound ? "lanes.plan.choose" : "lanes.plan.pick_in"); // i18n:keys
            else if (laneHover >= 0 && !laneEnds[laneHover].Inbound)
            {
                var key = OmsiLaneConnections.Key(laneEnds[laneSource], laneEnds[laneHover]);
                var has = laneConnections.Contains(key);
                if (!has && laneEnds[laneHover].Arm != laneEnds[laneSource].Arm)
                {
                    var curve = LaneCurve(laneEnds[laneSource], laneEnds[laneHover]);
                    shown.Add(curve);
                    shownHeights.Add(LaneCurveHeights(laneEnds[laneSource], laneEnds[laneHover], curve.Length));
                }
                plan = Loc.T(laneEnds[laneHover].Arm == laneEnds[laneSource].Arm ? "lanes.plan.same_arm" : has ? "lanes.plan.remove" : "lanes.plan.add"); // i18n:keys
            }
            else
                plan = Loc.T("lanes.plan.pick_out");
            mapCanvas.SetLaneEnds(laneEndPoints, states, laneHover, laneEndHeights);
            KeepPinnedArmHighlight();
            mapCanvas.SetRoadGuides(guides, false, guideHeights);
            mapCanvas.SetRoadPreview(shown, true, null, null, null, shownHeights);
            lanePlanLabel.text = plan;
        }

        /// <summary>A click with the lanes tool: chooses a lane in, adds or takes away a way to a lane out, or works on another junction.</summary>
        private void ClickLane(double x, double y)
        {
            if (currentSummary == null)
                return;
            var point = new OmsiMapPoint(x, y);
            roadPointer = point;
            RefreshLaneTarget();
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.choose_lanes"));
                return;
            }
            var end = laneJunction != null ? LaneEndAt(point) : -1;
            if (end >= 0 && laneEnds[end].Inbound)
            {
                laneSource = laneSource == end ? -1 : end;
                UpdateLanePreview();
                SetStatusT(laneSource >= 0 ? "lanes.status.chosen" : "lanes.status.let_go"); // i18n:keys
                return;
            }
            if (end >= 0)
            {
                if (laneSource < 0)
                {
                    SetStatusT("lanes.status.in_first");
                    return;
                }
                if (laneEnds[end].Arm == laneEnds[laneSource].Arm)
                {
                    SetStatusT("lanes.status.own_arm");
                    return;
                }
                ToggleLaneConnection(laneSource, end);
                return;
            }
            if (MadeJunctionAt(point, out var tile, out var record) && !ReferenceEquals(record, laneJunction))
            {
                TargetLaneJunction(tile, record);
                return;
            }
            laneSource = -1;
            UpdateLanePreview();
            if (laneJunction == null)
                SetStatusT("lanes.status.only_made");
        }

        private void ToggleLaneConnection(int from, int to)
        {
            var key = OmsiLaneConnections.Key(laneEnds[from], laneEnds[to]);
            var connections = new HashSet<string>(laneConnections, StringComparer.Ordinal);
            var added = connections.Add(key);
            if (!added)
                connections.Remove(key);
            ApplyLaneConnections(connections, Loc.T(added ? "lanes.done.way_added" : "lanes.done.way_removed", ("from", laneEnds[from].Arm + 1), ("to", laneEnds[to].Arm + 1))); // i18n:keys
        }

        /// <summary>
        /// Makes the junction again with these ways through under Sceneryobjects\Mapforge and puts it in place of the old one,
        /// keeping its markings and turn lanes; its arms keep their right of way on the new paths. One undo step. Null, or
        /// the ways it has without chosen ones (<paramref name="defaults"/>, else those of the junction now), makes it as it
        /// was made. False when it stays as it is.
        /// </summary>
        private bool ApplyLaneConnections(HashSet<string> connections, string done, HashSet<string> defaults = null)
        {
            if (laneJunction == null || laneMade == null)
            {
                SetStatusT("lanes.status.first");
                return false;
            }
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.choose_lanes"));
                return false;
            }
            if (connections != null && connections.SetEquals(defaults ?? laneDefaultWays))
                connections = null;
            var record = laneJunction;
            var tile = laneTile;
            var made = laneMade;
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
            if (rebuild == null)
            {
                SetStatusT("lanes.status.mismatch", ("id", record.Id));
                return false;
            }
            var plain = PlainRoadTexture();
            var turn = rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, plain);
            if (turn.HasErrors)
            {
                SetStatusT("lanes.status.stays", ("id", record.Id), ("reason", turn.Problem ?? Loc.T("lanes.stays.default")));
                return false;
            }
            // The right of way of the arms as the old paths have it, to put on the new ones.
            var oldObject = PrioritySceneryObject(record.AssetPath);
            var oldArms = oldObject != null ? OmsiJunctionPriorities.Arms(oldObject, record.Rules) : new List<OmsiPriorityArm>();
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, made.Name,
                    WithSignalReport(OmsiJunctionGenerator.Junction(turn.Arms, plain, "Mapforge " + made.Name, made.Markings, made.Areas, made.Arrows, connections, made.Signals)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("lanes.status.rebuild_failed", ("reason", exception.Message));
                return false;
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
                SetStatusT("lanes.status.rebuild_failed_id", ("id", record.Id), ("reason", exception.Message));
                return false;
            }
            if (!result.Succeeded)
            {
                RefreshAfterChange(null);
                SetStatus(Loc.T("lanes.status.stays_refs", ("id", record.Id), ("references", DescribeReferences(result.Blockers))));
                return false;
            }
            var steps = new List<object>(result.Steps);
            foreach (var step in steps)
                if (step is OmsiSplineChange change)
                    RefreshSplineViews(change);
            roadPorts = null;
            CarryPriorities(tile, record, oldObject, oldArms, turn.Arms, assetPath, steps);
            PushSteps(steps, Loc.T("lanes.step", ("id", record.Id)));
            laneSource = -1;
            TargetLaneJunction(tile, record);
            SetStatus(Loc.T("lanes.status.done", ("done", done), ("id", record.Id), ("count", laneConnections.Count), ("name", Path.GetFileNameWithoutExtension(assetPath))));
            return true;
        }

        /// <summary>
        /// Puts the right of way the old paths had on the new paths of a junction made again with other lanes (ADR 047): each
        /// new arm takes the level of the old arm traffic came in the same way, and rules on paths that are gone go. When
        /// the old junction had no ways through, and so no arms, the main road's arms get the right of way as when it was made.
        /// </summary>
        private void CarryPriorities(OmsiTileSummary tile, OmsiObjectRecord record, OmsiSceneryObject oldObject, List<OmsiPriorityArm> oldArms,
            IList<OmsiJunctionArm> madeArms, string assetPath, List<object> steps)
        {
            if (oldObject == null || oldArms.Count >= 2 && !oldArms.Exists(arm => arm.Level != OmsiPriorityLevel.Normal))
                return;
            var newObject = PrioritySceneryObject(assetPath);
            if (newObject == null)
                return;
            Dictionary<int, double?> made;
            if (oldArms.Count < 2)
                made = OmsiJunctionPriorities.ForMadeJunction(newObject, madeArms);
            else
            {
                var newArms = OmsiJunctionPriorities.Arms(newObject, null);
                foreach (var arm in newArms)
                {
                    OmsiPriorityArm nearest = null;
                    var best = 30.0;
                    foreach (var old in oldArms)
                    {
                        var off = Math.Abs(OmsiRoadPlanner.Normalize(arm.InwardDegrees - old.InwardDegrees));
                        if (off < best)
                        {
                            best = off;
                            nearest = old;
                        }
                    }
                    arm.Level = nearest?.Level ?? OmsiPriorityLevel.Normal;
                }
                made = OmsiJunctionPriorities.Priorities(newArms);
            }
            var priorities = new Dictionary<int, double?>();
            for (var index = 0; index < Math.Max(oldObject.Paths.Count, newObject.Paths.Count); index++)
                priorities[index] = null;
            foreach (var pair in made)
                priorities[pair.Key] = pair.Value;
            try
            {
                var change = session.SetPriorities(tile, record, priorities);
                if (change != null)
                    steps.Add(change);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>Esc with the lanes tool: lets go of the lane in, then leaves the tool.</summary>
        private void EscapeLane()
        {
            if (laneSource >= 0)
            {
                laneSource = -1;
                UpdateLanePreview();
                SetStatusT("lanes.status.follow");
            }
            else
                SetTool(EditorTool.Drag);
        }

        /// <summary>
        /// For command-line checks: opens the lanes tool on the selected junction and gives arm <c>n</c> (from 1) a left-turn
        /// lane, with <c>n taper storage</c> those lengths, or takes it away with <c>n off</c>. The status goes to the log
        /// as "Turn lane:".
        /// </summary>
        private void RunTurnLaneArgument(IList<string> words)
        {
            SetTool(EditorTool.Lanes);
            if (words.Count == 0 || !int.TryParse(words[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var arm))
            {
                Debug.Log("Turn lane: give the arm, from 1, then its taper and storage in metres or off.");
                return;
            }
            if (words.Count >= 3)
            {
                laneTaperField.value = words[1];
                laneStorageField.value = words[2];
            }
            ApplyTurnLane(arm - 1, words.Count >= 2 && words[1] == "off" ? null : LaneTurnLengths());
            Debug.Log("Turn lane: " + statusLabel?.text);
        }

        /// <summary>
        /// For command-line checks: opens the lanes tool on the selected junction, writes its lane ends and ways to the log,
        /// and adds or takes away the ways given, such as <c>1.0>2.1</c>, makes every way again with <c>every</c> or takes
        /// them all away with <c>none</c>. The
        /// status goes to the log as "Lanes:".
        /// </summary>
        private void RunLaneArgument(IList<string> ways)
        {
            SetTool(EditorTool.Lanes);
            for (var index = 0; index < laneEnds.Count; index++)
                Debug.Log("Lane end " + laneEnds[index].Arm + "." + laneEnds[index].Lane + (laneEnds[index].Inbound ? " in" : " out") + " at " +
                          laneEndPoints[index].X.ToString("0.0", CultureInfo.InvariantCulture) + ", " + laneEndPoints[index].Y.ToString("0.0", CultureInfo.InvariantCulture));
            if (laneJunction != null)
                Debug.Log("Lanes through: " + string.Join(" ", laneConnections));
            foreach (var way in ways)
            {
                if (laneJunction == null)
                    break;
                if (way == "every")
                {
                    ApplyLaneConnections(null, Loc.T("lanes.done.every"));
                    continue;
                }
                if (way == "none")
                {
                    ApplyLaneConnections(new HashSet<string>(StringComparer.Ordinal), Loc.T("lanes.done.none"));
                    continue;
                }
                var connections = new HashSet<string>(laneConnections, StringComparer.Ordinal);
                if (!connections.Add(way))
                    connections.Remove(way);
                ApplyLaneConnections(connections, Loc.T("lanes.done.changed", ("way", way)));
            }
            Debug.Log("Lanes: " + statusLabel?.text);
        }
    }
}
