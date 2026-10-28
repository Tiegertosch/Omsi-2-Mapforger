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
        private int laneSource = -1;
        private int laneHover = -1;

        private void CreateLaneOptions()
        {
            laneOptions = new VisualElement();
            laneOptions.AddToClassList("tool-panel");
            laneOptions.AddToClassList("picker-options");
            laneOptions.style.display = DisplayStyle.None;
            var title = new Label("Lane connections");
            title.AddToClassList("picker-options-title");
            laneOptions.Add(title);
            laneJunctionLabel = new Label();
            laneJunctionLabel.AddToClassList("panel-info");
            laneJunctionLabel.style.marginTop = 0f;
            laneOptions.Add(laneJunctionLabel);
            var legend = new Label("Dots are lanes leading in, rings lanes leading out. Click a lane in, then a lane out to add the way between them or take it away.");
            legend.AddToClassList("panel-info");
            laneOptions.Add(legend);
            var reset = new Button(() => ApplyLaneConnections(null, "Every lane in leads to every lane out again"))
            {
                text = "Every way through", tooltip = "Gives the junction a lane for every way through again, as it was made"
            };
            reset.AddToClassList("map-control-button");
            laneOptions.Add(reset);
            var none = new Button(() => ApplyLaneConnections(new HashSet<string>(StringComparer.Ordinal), "Took away every way through"))
            {
                text = "No ways through",
                tooltip = "Takes every way through the junction away, so you can connect the lanes anew; until you do, AI traffic cannot pass it. Ctrl+Z brings them back."
            };
            none.AddToClassList("map-control-button");
            laneOptions.Add(none);
            lanePlanLabel = new Label();
            lanePlanLabel.AddToClassList("panel-info");
            laneOptions.Add(lanePlanLabel);
            var hint = new Label("Esc: let go of the lane, then leave.");
            hint.AddToClassList("panel-info");
            laneOptions.Add(hint);
            objectsPanelHost.Add(laneOptions);
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
                SetStatus("Lanes: click a junction made in this project to choose its ways through; Esc leaves the tool.");
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
                laneJunctionLabel.text = "Click a junction made in this project.";
            if (lanePlanLabel != null)
                lanePlanLabel.text = string.Empty;
            mapCanvas?.SetLaneEnds(null, null, -1);
        }

        /// <summary>The arms of a made junction as it would be made again, laid out; null with the reason shown when it cannot be.</summary>
        private OmsiJunctionLayout LaneLayout(OmsiTileSummary tile, OmsiObjectRecord record, OmsiGeneratedJunction made)
        {
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
            if (rebuild == null)
            {
                SetStatus("Junction #" + record.Id + " cannot be made again: its connection points do not match its file.");
                return null;
            }
            try
            {
                return OmsiJunctionLayout.Build(rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, PlainRoadTexture()).Arms, PlainRoadTexture(), made.Connections);
            }
            catch (ArgumentException exception)
            {
                SetStatus("Junction #" + record.Id + " cannot be laid out: " + exception.Message);
                return null;
            }
        }

        /// <summary>Works on a junction the editor made: its lane ends and the ways through it has; false when it is no such junction.</summary>
        private bool TargetLaneJunction(OmsiTileSummary tile, OmsiObjectRecord record)
        {
            var made = MadeJunction(record.AssetPath);
            if (made == null)
                return false;
            var layout = LaneLayout(tile, record, made);
            if (layout == null)
                return false;
            if (layout.IsTransition)
            {
                SetStatus("Object #" + record.Id + " joins two cross-sections; it has no ways through to choose.");
                return false;
            }
            var keepSource = ReferenceEquals(record, laneJunction) ? laneSource : -1;
            laneTile = tile;
            laneJunction = record;
            laneMade = made;
            laneEnds = layout.LaneEnds();
            laneConnections = made.Connections != null ? new HashSet<string>(made.Connections, StringComparer.Ordinal) : OmsiLaneConnections.All(laneEnds);
            laneEndPoints.Clear();
            laneEndHeights.Clear();
            foreach (var end in laneEnds)
            {
                laneEndPoints.Add(ObjectToWorld(tile, record, end.X, end.Y));
                laneEndHeights.Add(record.Height + end.Height);
            }
            laneSource = keepSource >= 0 && keepSource < laneEnds.Count && laneEnds[keepSource].Inbound ? keepSource : -1;
            laneJunctionLabel.text = "Junction #" + record.Id + ": " + made.Arms.Count + " arms, " + laneConnections.Count + " of " +
                                     OmsiLaneConnections.All(laneEnds).Count + " ways through.";
            if (selection.Count != 1 || !ReferenceEquals(selection[0].Record, record))
            {
                selection.Clear();
                selection.Add(new MapSelectionItem { Tile = tile, Record = record });
                ApplySelection(tile);
            }
            UpdateLanePreview();
            SetStatus("Lanes of junction #" + record.Id + ": click a lane in (dot), then a lane out (ring) to add or take away the way between them.");
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
                plan = laneHover >= 0 && laneEnds[laneHover].Inbound ? "Click to choose this lane in." : "Click a lane in (dot) to see where it leads.";
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
                plan = laneEnds[laneHover].Arm == laneEnds[laneSource].Arm ? "A lane cannot lead back into its own arm."
                    : has ? "Click to take this way away." : "Click to add this way.";
            }
            else
                plan = "Click a lane out (ring) to add or take away the way to it; Esc lets go of the lane.";
            mapCanvas.SetLaneEnds(laneEndPoints, states, laneHover, laneEndHeights);
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
                SetStatus(ReadOnlyMessage("choose the lanes of junctions"));
                return;
            }
            var end = laneJunction != null ? LaneEndAt(point) : -1;
            if (end >= 0 && laneEnds[end].Inbound)
            {
                laneSource = laneSource == end ? -1 : end;
                UpdateLanePreview();
                SetStatus(laneSource >= 0 ? "Lane in chosen: click a lane out to add or take away the way to it." : "Let go of the lane.");
                return;
            }
            if (end >= 0)
            {
                if (laneSource < 0)
                {
                    SetStatus("Lanes: click a lane in (dot) first, then this lane out.");
                    return;
                }
                if (laneEnds[end].Arm == laneEnds[laneSource].Arm)
                {
                    SetStatus("Lanes: a lane cannot lead back into its own arm.");
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
                SetStatus("Lanes: click a junction made in this project; only those can have their ways through chosen.");
        }

        private void ToggleLaneConnection(int from, int to)
        {
            var key = OmsiLaneConnections.Key(laneEnds[from], laneEnds[to]);
            var connections = new HashSet<string>(laneConnections, StringComparer.Ordinal);
            var added = connections.Add(key);
            if (!added)
                connections.Remove(key);
            ApplyLaneConnections(connections, (added ? "Added the way " : "Took away the way ") + "from arm " + (laneEnds[from].Arm + 1) + " to arm " + (laneEnds[to].Arm + 1));
        }

        /// <summary>
        /// Makes the junction again with these ways through under Sceneryobjects\Mapforge and puts it in place of the old one,
        /// keeping its markings; its arms keep their right of way on the new paths. One undo step. Null, or every way
        /// through, makes it as it was made.
        /// </summary>
        private void ApplyLaneConnections(HashSet<string> connections, string done)
        {
            if (laneJunction == null || laneMade == null)
            {
                SetStatus("Lanes: click a junction made in this project first.");
                return;
            }
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("choose the lanes of junctions"));
                return;
            }
            if (connections != null && connections.SetEquals(OmsiLaneConnections.All(laneEnds)))
                connections = null;
            var record = laneJunction;
            var tile = laneTile;
            var made = laneMade;
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
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
            // The right of way of the arms as the old paths have it, to put on the new ones.
            var oldObject = PrioritySceneryObject(record.AssetPath);
            var oldArms = oldObject != null ? OmsiJunctionPriorities.Arms(oldObject, record.Rules) : new List<OmsiPriorityArm>();
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, made.Name,
                    OmsiJunctionGenerator.Junction(turn.Arms, plain, "Mapforge " + made.Name, made.Markings, made.Areas, made.Arrows, connections));
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
            CarryPriorities(tile, record, oldObject, oldArms, turn.Arms, assetPath, steps);
            PushSteps(steps, "the lanes of junction #" + record.Id);
            laneSource = -1;
            TargetLaneJunction(tile, record);
            SetStatus(done + " on junction #" + record.Id + ", now " + laneConnections.Count + " way(s) through, made again as " +
                      Path.GetFileNameWithoutExtension(assetPath) + ". Ctrl+Z takes it back.");
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
                SetStatus("Lanes: click a lane in to see where it leads; Esc leaves the tool.");
            }
            else
                SetTool(EditorTool.Drag);
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
                    ApplyLaneConnections(null, "Every lane in leads to every lane out again");
                    continue;
                }
                if (way == "none")
                {
                    ApplyLaneConnections(new HashSet<string>(StringComparer.Ordinal), "Took away every way through");
                    continue;
                }
                var connections = new HashSet<string>(laneConnections, StringComparer.Ordinal);
                if (!connections.Add(way))
                    connections.Remove(way);
                ApplyLaneConnections(connections, "Changed the way " + way);
            }
            Debug.Log("Lanes: " + statusLabel?.text);
        }
    }
}
