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
    /// The priority tool (ADR 047), modelled on the priorities tool of Traffic for Cities: Skylines II: a click on a
    /// junction object, of the map or made here, shows a sign before each arm; a click on a sign switches the arm between
    /// normal, main road and give way, written as the [rule] priority records of the arm's paths, one undo step each. The
    /// paths show while the tool is on, wide where they have the right of way and dashed where they give way.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>A sign counts as under the pointer within this many pixels.</summary>
        private const float PriorityPickPixels = 18f;

        /// <summary>Signs stand this far out from where traffic comes into the junction.</summary>
        private const double PriorityHandleMeters = 5.0;

        private static readonly string[] PriorityLevelNames = { "normal", "main road", "give way" };

        private VisualElement priorityOptions;
        private Label priorityJunctionLabel;
        private Label priorityPlanLabel;
        private OmsiTileSummary priorityTile;
        private OmsiObjectRecord priorityJunction;
        private List<OmsiPriorityArm> priorityArms = new List<OmsiPriorityArm>();
        private readonly List<OmsiMapPoint> priorityHandlePoints = new List<OmsiMapPoint>();
        private int priorityHover = -1;
        private bool pathsBeforePriorities;
        private readonly Dictionary<string, OmsiSceneryObject> prioritySceneryObjects = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);

        private void CreatePriorityOptions()
        {
            priorityOptions = new VisualElement();
            priorityOptions.AddToClassList("tool-panel");
            priorityOptions.AddToClassList("picker-options");
            priorityOptions.style.display = DisplayStyle.None;
            var title = new Label("Right of way");
            title.AddToClassList("picker-options-title");
            priorityOptions.Add(title);
            priorityJunctionLabel = new Label();
            priorityJunctionLabel.AddToClassList("panel-info");
            priorityJunctionLabel.style.marginTop = 0f;
            priorityOptions.Add(priorityJunctionLabel);
            foreach (var text in new[]
                     {
                         "Main road (yellow): its paths straight on and to the right go first, with priority 192.",
                         "Give way (red edge): all its paths let the others go first, with 64.",
                         "Normal (grey): no rule; who comes first goes first, and left turns wait for the oncoming traffic."
                     })
            {
                var legend = new Label(text);
                legend.AddToClassList("panel-info");
                priorityOptions.Add(legend);
            }
            var clear = new Button(ClearPriorities)
            {
                text = "All arms normal", tooltip = "Takes the priority rules off every arm of the junction, as one undo step"
            };
            clear.AddToClassList("map-control-button");
            priorityOptions.Add(clear);
            priorityPlanLabel = new Label();
            priorityPlanLabel.AddToClassList("panel-info");
            priorityOptions.Add(priorityPlanLabel);
            var hint = new Label("Click a sign to switch its arm. Esc: leave.");
            hint.AddToClassList("panel-info");
            priorityOptions.Add(hint);
            objectsPanelHost.Add(priorityOptions);
        }

        private void BeginPriorityTool()
        {
            if (priorityOptions == null || mapCanvas == null || currentSummary == null)
                return;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (terrainOptions != null)
                terrainOptions.style.display = DisplayStyle.None;
            priorityOptions.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.ShowSplineEnds = false;
            mapCanvas.BeginRoad();
            ClearPriorityTarget();
            // The paths show the right of way: wide where it is, dashed where they give way.
            pathsBeforePriorities = pathsShown;
            if (!pathsShown)
                ShowPaths(true);
            var item = selection.Count == 1 ? selection[0] : null;
            if (item?.Tile == null || !TargetPriorityJunction(item.Tile, item.Record))
                SetStatus("Priority: click a junction to see the signs of its arms; Esc leaves the tool.");
        }

        private void EndPriorityTool()
        {
            mapCanvas?.EndRoad();
            if (mapCanvas != null)
            {
                mapCanvas.ShowSplineEnds = true;
                mapCanvas.ClearRoadPreview();
                mapCanvas.SetPriorityHandles(null, null, -1);
            }
            if (priorityOptions != null)
                priorityOptions.style.display = DisplayStyle.None;
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            ClearPriorityTarget();
            if (!pathsBeforePriorities && pathsShown)
                ShowPaths(false);
        }

        private void ClearPriorityTarget()
        {
            priorityTile = null;
            priorityJunction = null;
            priorityArms = new List<OmsiPriorityArm>();
            priorityHandlePoints.Clear();
            priorityHover = -1;
            if (priorityJunctionLabel != null)
                priorityJunctionLabel.text = "Click a junction.";
            if (priorityPlanLabel != null)
                priorityPlanLabel.text = string.Empty;
            mapCanvas?.SetPriorityHandles(null, null, -1);
        }

        /// <summary>The .sco of an object of the installation, read once; null when it cannot be read.</summary>
        private OmsiSceneryObject PrioritySceneryObject(string assetPath)
        {
            if (prioritySceneryObjects.TryGetValue(assetPath, out var cached))
                return cached;
            OmsiSceneryObject sceneryObject = null;
            try
            {
                var path = OmsiRelativePath.Resolve(OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath), assetPath);
                if (File.Exists(path))
                    sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException ||
                                              exception is ArgumentException)
            {
                // An unreadable object is no junction.
            }
            prioritySceneryObjects[assetPath] = sceneryObject;
            return sceneryObject;
        }

        /// <summary>Works on a junction object: finds its arms and the levels its rules give them; false when it has fewer than two.</summary>
        private bool TargetPriorityJunction(OmsiTileSummary tile, OmsiObjectRecord record)
        {
            var sceneryObject = PrioritySceneryObject(record.AssetPath);
            var arms = sceneryObject != null ? OmsiJunctionPriorities.Arms(sceneryObject, record.Rules) : new List<OmsiPriorityArm>();
            if (arms.Count == 0)
                return false;
            priorityTile = tile;
            priorityJunction = record;
            priorityArms = arms;
            priorityHandlePoints.Clear();
            foreach (var arm in arms)
            {
                // Before the arm, against the way traffic comes in.
                var inward = arm.InwardDegrees * Math.PI / 180.0;
                priorityHandlePoints.Add(ObjectToWorld(tile, record, arm.X - Math.Sin(inward) * PriorityHandleMeters, arm.Y - Math.Cos(inward) * PriorityHandleMeters));
            }
            priorityJunctionLabel.text = "Junction #" + record.Id + ": " + arms.Count + " arms, " + arms.FindAll(arm => arm.Level == OmsiPriorityLevel.Main).Count +
                                         " main road, " + arms.FindAll(arm => arm.Level == OmsiPriorityLevel.Yield).Count + " give way.";
            if (selection.Count != 1 || !ReferenceEquals(selection[0].Record, record))
            {
                selection.Clear();
                selection.Add(new MapSelectionItem { Tile = tile, Record = record });
                ApplySelection(tile);
            }
            UpdatePriorityPreview();
            SetStatus("Right of way at junction #" + record.Id + ": click the sign of an arm to switch it between normal, main road and give way.");
            return true;
        }

        /// <summary>The junction was changed or taken back, such as by undo: its arms are read anew from its rules.</summary>
        private void RefreshPriorityTarget()
        {
            if (priorityJunction == null || priorityTile == null)
                return;
            if (!priorityTile.Objects.Contains(priorityJunction) || !TargetPriorityJunction(priorityTile, priorityJunction))
                ClearPriorityTarget();
        }

        private int PriorityHandleAt(OmsiMapPoint point)
        {
            var best = -1;
            var bestDistance = mapCanvas != null && mapCanvas.ThreeDimensional
                ? Math.Max(0.8, mapCanvas.OrbitDistance * 0.025)
                : Math.Max(0.5, PriorityPickPixels * (mapCanvas?.MetersPerPixel ?? 1f));
            for (var index = 0; index < priorityHandlePoints.Count; index++)
            {
                var distance = Distance(priorityHandlePoints[index], point);
                if (distance <= bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>A junction object around a point, the nearest whose arms reach it, or false.</summary>
        private bool PriorityJunctionAt(OmsiMapPoint point, out OmsiTileSummary tile, out OmsiObjectRecord record)
        {
            tile = null;
            record = null;
            var best = double.MaxValue;
            foreach (var candidateTile in currentSummary.Tiles)
                foreach (var candidate in candidateTile.Objects)
                {
                    var centre = OmsiMapGeometry.TileLocalToWorld(candidateTile.X, candidateTile.Y, candidate.LocalX, candidate.LocalY, currentSummary.TileSizeMeters);
                    var distance = Distance(centre, point);
                    if (distance >= best || distance > 60.0)
                        continue;
                    var sceneryObject = PrioritySceneryObject(candidate.AssetPath);
                    if (sceneryObject == null || sceneryObject.Paths.Count < 2)
                        continue;
                    var arms = OmsiJunctionPriorities.Arms(sceneryObject, null);
                    var reach = 0.0;
                    foreach (var arm in arms)
                        reach = Math.Max(reach, Math.Sqrt(arm.X * arm.X + arm.Y * arm.Y) + PriorityHandleMeters);
                    if (arms.Count == 0 || distance > reach + 2.0)
                        continue;
                    best = distance;
                    tile = candidateTile;
                    record = candidate;
                }
            return record != null;
        }

        /// <summary>Shows the signs, the one under the pointer larger, and what a click would do.</summary>
        private void UpdatePriorityPreview()
        {
            if (mapCanvas == null || activeTool != EditorTool.Priorities)
                return;
            priorityHover = roadPointer.HasValue ? PriorityHandleAt(roadPointer.Value) : -1;
            var levels = new List<int>();
            var heights = new List<double>();
            foreach (var arm in priorityArms)
            {
                levels.Add((int)arm.Level);
                // In the 3D view the signs stand at the junction's height, not on the ground below a bridge or in a cutting.
                heights.Add(priorityJunction.Height);
            }
            mapCanvas.SetPriorityHandles(priorityHandlePoints, levels, priorityHover, heights);
            if (priorityPlanLabel == null)
                return;
            priorityPlanLabel.text = priorityJunction == null ? "Click a junction to see its arms."
                : priorityHover >= 0 ? "Arm " + (priorityHover + 1) + ", " + PriorityLevelNames[(int)priorityArms[priorityHover].Level] + ": click to make it " +
                                       PriorityLevelNames[(int)OmsiJunctionPriorities.Next(priorityArms[priorityHover].Level)] + "."
                : "Point at a sign to switch its arm.";
        }

        /// <summary>A click with the priority tool: switches the arm of a sign, or works on the junction clicked.</summary>
        private void ClickPriority(double x, double y)
        {
            if (currentSummary == null)
                return;
            var point = new OmsiMapPoint(x, y);
            roadPointer = point;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("set the right of way"));
                return;
            }
            var handle = PriorityHandleAt(point);
            if (handle >= 0)
            {
                SwitchPriority(handle);
                return;
            }
            if (PriorityJunctionAt(point, out var tile, out var record))
            {
                if (!ReferenceEquals(record, priorityJunction))
                    TargetPriorityJunction(tile, record);
                return;
            }
            SetStatus("Priority: no junction here; click a junction object to see the signs of its arms.");
        }

        /// <summary>Switches an arm to its next level and writes the priorities of its paths, as one undo step.</summary>
        private void SwitchPriority(int armIndex)
        {
            var arm = priorityArms[armIndex];
            arm.Level = OmsiJunctionPriorities.Next(arm.Level);
            WritePriorities(OmsiJunctionPriorities.Priorities(new[] { arm }),
                "Arm " + (armIndex + 1) + " of junction #" + priorityJunction.Id + " is " + PriorityLevelNames[(int)arm.Level] + " now");
        }

        /// <summary>Takes the priorities off every arm of the junction.</summary>
        private void ClearPriorities()
        {
            if (priorityJunction == null)
            {
                SetStatus("Priority: click a junction first.");
                return;
            }
            foreach (var arm in priorityArms)
                arm.Level = OmsiPriorityLevel.Normal;
            WritePriorities(OmsiJunctionPriorities.Priorities(priorityArms), "Every arm of junction #" + priorityJunction.Id + " is normal now");
        }

        private void WritePriorities(Dictionary<int, double?> priorities, string done)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("set the right of way"));
                return;
            }
            var record = priorityJunction;
            OmsiObjectChange change;
            try
            {
                change = session.SetPriorities(priorityTile, record, priorities);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("The right of way could not be written: " + exception.Message);
                RefreshPriorityTarget();
                return;
            }
            if (change == null)
            {
                RefreshPriorityTarget();
                SetStatus(done + "; its rules were so already.");
                return;
            }
            PushSteps(new List<object> { change }, "the right of way at junction #" + record.Id);
            RefreshPriorityTarget();
            var count = 0;
            foreach (var pair in priorities)
                if (pair.Value.HasValue)
                    count++;
            SetStatus(done + ": " + count + " path(s) with a priority rule. Ctrl+Z takes it back.");
        }

        /// <summary>
        /// Gives a junction the editor made the right of way after its arms (ADR 047): the main road's arms go first, the
        /// others give way; adds the change to the steps of its insertion.
        /// </summary>
        private void ApplyMadePriorities(OmsiObjectRecord junction, IList<OmsiJunctionArm> arms, List<object> steps)
        {
            var tile = currentSummary.Tiles.Find(candidate => candidate.Objects.Contains(junction));
            var sceneryObject = tile != null ? PrioritySceneryObject(junction.AssetPath) : null;
            if (sceneryObject == null)
                return;
            var priorities = OmsiJunctionPriorities.ForMadeJunction(sceneryObject, arms);
            if (!priorities.ContainsValue(OmsiJunctionPriorities.MainPriority) && !priorities.ContainsValue(OmsiJunctionPriorities.YieldPriority))
                return;
            try
            {
                var change = session.SetPriorities(tile, junction, priorities);
                if (change != null)
                    steps.Add(change);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// For command-line checks: opens the priority tool on the selected junction, writes its arms to the log and clicks
        /// the signs given by number from 1, such as <c>-priorities 2 3 3</c>. The status goes to the log as "Priorities:".
        /// </summary>
        private void RunPriorityArgument(IList<string> signs)
        {
            SetTool(EditorTool.Priorities);
            for (var index = 0; index < priorityArms.Count; index++)
                Debug.Log("Priority arm " + (index + 1) + " (" + PriorityLevelNames[(int)priorityArms[index].Level] + ") at " +
                          priorityHandlePoints[index].X.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                          priorityHandlePoints[index].Y.ToString("0.0", CultureInfo.InvariantCulture) + ": paths " + string.Join(",", priorityArms[index].Paths));
            foreach (var sign in signs)
                if (int.TryParse(sign, out var number) && number >= 1 && number <= priorityArms.Count)
                    SwitchPriority(number - 1);
            Debug.Log("Priorities: " + statusLabel?.text);
        }
    }
}
