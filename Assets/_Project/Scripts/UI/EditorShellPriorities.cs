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
    /// The traffic rules tool (V; ADR 047 and ADR 098, M4), which grew out of the priority tool modelled on the priorities tool of Traffic
    /// for Cities: Skylines II. Its window has two tabs of signs. With a priority sign a click or a drop on the sign of an arm of a junction,
    /// of the map or made here, writes the [rule] priority records of the arm's paths, one undo step each; the paths show while the first
    /// tab ("Traffic rules") is open, wide where they have the right of way and dashed where they give way. What goes on road pieces (the
    /// speeds of the second tab, the overtaking ban of the first) is in <c>EditorShellTrafficRoads.cs</c>.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>A sign counts as under the pointer within this many pixels.</summary>
        private const float PriorityPickPixels = 18f;

        /// <summary>Signs stand this far out from where traffic comes into the junction.</summary>
        private const double PriorityHandleMeters = 5.0;

        /// <summary>The levels as the log writes them.</summary>
        private static readonly string[] PriorityLevelNames = { "normal", "main road", "give way" };

        private TrafficToolOptions trafficOptions;
        private OmsiTileSummary priorityTile;
        private OmsiObjectRecord priorityJunction;
        private List<OmsiPriorityArm> priorityArms = new List<OmsiPriorityArm>();
        private readonly List<OmsiMapPoint> priorityHandlePoints = new List<OmsiMapPoint>();
        private int priorityHover = -1;
        private bool pathsBeforePriorities;
        private readonly Dictionary<string, OmsiSceneryObject> prioritySceneryObjects = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);

        private void CreateTrafficOptions()
        {
            trafficOptions = new TrafficToolOptions();
            trafficOptions.style.display = DisplayStyle.None;
            trafficOptions.CloseRequested += () => SetTool(EditorTool.Drag);
            trafficOptions.ClearJunctionRequested += ClearPriorities;
            trafficOptions.ArmedChanged += OnTrafficArmedChanged;
            trafficOptions.TabChanged += OnTrafficTabChanged;
            trafficOptions.ScopeChanged += UpdateTrafficPreview;
            trafficOptions.CustomSpeedChanged += UpdateTrafficHint;
            trafficOptions.DragStarted += BeginTrafficDrag;
            trafficOptions.DragMoved += MoveTrafficDrag;
            trafficOptions.DragDropped += DropTrafficSign;
            trafficOptions.DragCancelled += EndTrafficDrag;
            trafficOptions.HintChanged += (text, warning) => ShowToolHint(trafficOptions, text, warning);
            objectsPanelHost.Add(trafficOptions);
        }

        private void BeginPriorityTool()
        {
            if (trafficOptions == null || mapCanvas == null || currentSummary == null)
                return;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (terrainOptions != null)
                terrainOptions.style.display = DisplayStyle.None;
            trafficOptions.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.ShowSplineEnds = false;
            mapCanvas.BeginRoad();
            trafficRoadAssets.Clear();
            mapCanvas.TrafficLabelFilter = IsTrafficRoad;
            mapCanvas.ShowTrafficLabels = true;
            ClearPriorityTarget();
            // The paths show the right of way: wide where it is, dashed where they give way.
            pathsBeforePriorities = pathsShown;
            if (!pathsShown)
                ShowPaths(true);
            ShowTrafficTabView();
            var item = selection.Count == 1 ? selection[0] : null;
            if (item?.Tile == null || !TargetPriorityJunction(item.Tile, item.Record))
                SetStatusT("traffic.status.start");
            UpdateTrafficHint();
        }

        private void EndPriorityTool()
        {
            // A tile still held by the pointer lets go of it, or its capture would eat the clicks of the next tool.
            trafficOptions?.CancelDrag();
            EndTrafficDrag();
            mapCanvas?.EndRoad();
            if (mapCanvas != null)
            {
                mapCanvas.ShowSplineEnds = true;
                mapCanvas.ShowTrafficLabels = false;
                mapCanvas.ClearRoadPreview();
                mapCanvas.SetPriorityHandles(null, null, -1);
                mapCanvas.SetTrafficHighlight(null);
                mapCanvas.SetPointerBadge(null);
            }
            trafficOptions?.Arm(null);
            if (trafficOptions != null)
                trafficOptions.style.display = DisplayStyle.None;
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            ClearPriorityTarget();
            ClearTrafficHover();
            if (!pathsBeforePriorities && pathsShown)
                ShowPaths(false);
            else if (mapCanvas != null)
            {
                mapCanvas.ShowPaths = pathsShown;
                pathsToggleButton?.EnableInClassList("view-control-active", pathsShown);
            }
        }

        /// <summary>Esc with the traffic rules tool: a sign being dragged goes back, then the armed sign is let go of, then the tool ends.</summary>
        private bool EscapeTrafficTool()
        {
            switch (TrafficEscape.Next(trafficOptions != null && trafficOptions.IsDragging, trafficOptions != null && trafficOptions.Armed.HasValue))
            {
                case TrafficEscapeStep.CancelDrag:
                    trafficOptions.CancelDrag();
                    break;
                case TrafficEscapeStep.DisarmSign:
                    trafficOptions.Arm(null);
                    SetStatusT("traffic.status.disarmed");
                    break;
                default:
                    SetTool(EditorTool.Drag);
                    break;
            }
            return true;
        }

        private void ClearPriorityTarget()
        {
            priorityTile = null;
            priorityJunction = null;
            priorityArms = new List<OmsiPriorityArm>();
            priorityHandlePoints.Clear();
            priorityHover = -1;
            trafficOptions?.ShowJunction(Loc.T("traffic.click_junction"));
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
            trafficOptions?.ShowJunction(Loc.T("traffic.junction", ("id", record.Id), ("arms", arms.Count),
                ("main", arms.FindAll(arm => arm.Level == OmsiPriorityLevel.Main).Count), ("yield", arms.FindAll(arm => arm.Level == OmsiPriorityLevel.Yield).Count)));
            if (selection.Count != 1 || !ReferenceEquals(selection[0].Record, record))
            {
                selection.Clear();
                selection.Add(new MapSelectionItem { Tile = tile, Record = record });
                ApplySelection(tile);
            }
            UpdatePriorityPreview();
            SetStatusT("traffic.status.target", ("id", record.Id));
            return true;
        }

        /// <summary>The junction was changed or taken back, such as by undo: its arms are read anew from its rules.</summary>
        private void RefreshPriorityTarget()
        {
            if (priorityJunction == null || priorityTile == null)
                return;
            if (!priorityTile.Objects.Contains(priorityJunction))
            {
                // In a chrono variant a copy with the new rules takes the junction's place; the ID finds it.
                var id = priorityJunction.Id;
                var current = priorityTile.Objects.Find(candidate => candidate.Id == id);
                if (current != null)
                    priorityJunction = current;
            }
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

        /// <summary>Shows the signs, the one under the pointer larger, and says in the hint what a click would do.</summary>
        private void UpdatePriorityPreview()
        {
            if (mapCanvas == null || activeTool != EditorTool.Priorities)
                return;
            priorityHover = roadPointer.HasValue ? PriorityHandleAt(roadPointer.Value) : -1;
            var levels = new List<int>();
            var heights = new List<double>();
            foreach (var arm in priorityArms)
            {
                levels.Add((int)OmsiTrafficSigns.SignOf(arm));
                // In the 3D view the signs stand at the junction's height, not on the ground below a bridge or in a cutting.
                heights.Add(priorityJunction.Height);
            }
            mapCanvas.SetPriorityHandles(priorityHandlePoints, levels, priorityHover, heights);
            UpdateTrafficHint();
        }

        /// <summary>
        /// True when the pointer works on road pieces: the tile in use is one of the road (a speed, the ban, their erasers), or none is in use
        /// on the Speed tab. The tab alone does not tell, as the first tab holds the ban beside the signs of the arms.
        /// </summary>
        private bool TrafficOnRoad()
        {
            var item = CurrentTrafficItem;
            return item.HasValue ? item.Value.OnRoad : trafficOptions.Tab == TrafficTab.Speed;
        }

        /// <summary>The pointer moved in the map: the tile in use (else the open tab) says whether the arms of a junction or the road pieces follow it.</summary>
        private void UpdateTrafficPreview()
        {
            if (activeTool != EditorTool.Priorities || trafficOptions == null)
                return;
            if (TrafficOnRoad())
                UpdateTrafficRoadPreview();
            else
                UpdatePriorityPreview();
        }

        /// <summary>Shows in the hint of the status bar what the next click or drop does.</summary>
        private void UpdateTrafficHint()
        {
            if (trafficOptions == null || activeTool != EditorTool.Priorities)
                return;
            if (TrafficOnRoad())
            {
                UpdateTrafficRoadHint();
                return;
            }
            var armed = CurrentTrafficItem;
            if (armed.HasValue && priorityHover >= 0 && priorityHover < priorityArms.Count)
                trafficOptions.ShowHint(Loc.T("traffic.hint.arm", ("n", priorityHover + 1), ("sign", TrafficItemName(armed.Value))), false);
            else if (armed.HasValue)
                trafficOptions.ShowHint(Loc.T("traffic.hint.priority_armed", ("sign", TrafficItemName(armed.Value))), false);
            else if (priorityHover >= 0 && priorityHover < priorityArms.Count)
            {
                var arm = priorityArms[priorityHover];
                trafficOptions.ShowHint(Loc.T("traffic.hint.arm_cycle", ("n", priorityHover + 1), ("sign", SignName(OmsiTrafficSigns.SignOf(arm))),
                    ("next", SignName(CycledSign(arm)))), false);
            }
            else
                trafficOptions.ShowHint(Loc.T(priorityJunction == null ? "traffic.hint.priority_none" : "traffic.hint.priority"), false); // i18n:keys
        }

        private void OnTrafficArmedChanged()
        {
            var armed = trafficOptions.Armed;
            mapCanvas?.SetPointerBadge(!armed.HasValue ? null : armed.Value.IsEraser ? "eraser" : armed.Value.Kind == TrafficItemKind.RightOfWay ? "diamond" : "gauge");
            // The first tab holds signs of both kinds, so arming can switch between the arms and the road pieces without a tab change.
            ClearTrafficHover();
            UpdateTrafficPreview();
            UpdateTrafficHint();
        }

        private void OnTrafficTabChanged()
        {
            ShowTrafficTabView();
            ClearTrafficHover();
            UpdateTrafficPreview();
        }

        /// <summary>The paths show on the first tab only, as they say who goes first; the Speed tab shows the limits on the roads without them.</summary>
        private void ShowTrafficTabView()
        {
            if (mapCanvas == null || trafficOptions == null)
                return;
            mapCanvas.ShowPaths = pathsShown && (trafficOptions.Tab == TrafficTab.Rules || pathsBeforePriorities);
            // The button of the view says what the map shows.
            pathsToggleButton?.EnableInClassList("view-control-active", mapCanvas.ShowPaths);
        }

        /// <summary>A click with the traffic rules tool: puts the armed sign where it hits, cycles the arm of a sign, or works on the junction clicked.</summary>
        private void ClickPriority(double x, double y)
        {
            if (currentSummary == null)
                return;
            var point = new OmsiMapPoint(x, y);
            roadPointer = point;
            var armed = trafficOptions.Armed;
            if (armed.HasValue)
            {
                ApplyTrafficItem(armed.Value, point);
                return;
            }
            if (trafficOptions.Tab == TrafficTab.Speed)
            {
                SetStatusT("traffic.status.pick_sign");
                return;
            }
            if (!CanEditRules)
            {
                SetStatus(ReadOnlyMessage("readonly.action.right_of_way"));
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
            SetStatusT("traffic.status.none_here");
        }

        /// <summary>
        /// Puts a sign where a click or a drop landed: the one path for the armed tile, a sign dragged to the map and the command-line
        /// checks. A priority sign goes on the arm whose sign is there; a speed sign on the road piece there.
        /// </summary>
        private void ApplyTrafficItem(TrafficItem item, OmsiMapPoint point)
        {
            if (activeTool != EditorTool.Priorities)
                return;
            if (item.OnRoad)
            {
                ApplyRoadItem(item, point);
                return;
            }
            if (!CanEditRules)
            {
                SetStatus(ReadOnlyMessage("readonly.action.right_of_way"));
                return;
            }
            var handle = PriorityHandleAt(point);
            if (handle < 0 && PriorityJunctionAt(point, out var tile, out var record))
            {
                // A sign let go on a junction not shown yet shows its arms; a drop on one of the signs then still lands.
                if (!ReferenceEquals(record, priorityJunction))
                    TargetPriorityJunction(tile, record);
                handle = PriorityHandleAt(point);
            }
            if (handle >= 0)
                SetArmSign(handle, item.Sign);
            else if (priorityJunction != null)
                SetStatusT("traffic.status.drop_on_arm");
            else
                SetStatusT("traffic.status.none_here");
        }

        /// <summary>Switches an arm to its next sign (normal, priority road, give way) and writes the priorities of its paths, as one undo step.</summary>
        private void SwitchPriority(int armIndex)
        {
            SetArmSign(armIndex, CycledSign(priorityArms[armIndex]));
        }

        /// <summary>The sign a click without an armed sign gives an arm: the one after what it shows.</summary>
        private static OmsiRightOfWaySign CycledSign(OmsiPriorityArm arm)
        {
            var next = OmsiJunctionPriorities.Next(arm.Level);
            return next == OmsiPriorityLevel.Main ? OmsiRightOfWaySign.PriorityRoad : next == OmsiPriorityLevel.Yield ? OmsiRightOfWaySign.Yield : OmsiRightOfWaySign.Normal;
        }

        /// <summary>Puts a sign on an arm of the junction and writes the priorities of its paths, as one undo step.</summary>
        private void SetArmSign(int armIndex, OmsiRightOfWaySign sign)
        {
            var arm = priorityArms[armIndex];
            // The arm shows its new level only once the file has it; a failed write leaves it as it was.
            WritePriorities(OmsiTrafficSigns.Priorities(arm, sign), Loc.T("traffic.done.arm", ("n", armIndex + 1), ("id", priorityJunction.Id), ("sign", SignName(sign))),
                () => arm.Level = OmsiTrafficSigns.LevelOf(sign));
        }

        /// <summary>Takes the priorities off every arm of the junction.</summary>
        private void ClearPriorities()
        {
            if (priorityJunction == null)
            {
                SetStatusT("traffic.status.first");
                return;
            }
            var priorities = new Dictionary<int, double?>();
            foreach (var arm in priorityArms)
                foreach (var index in arm.Paths)
                    priorities[index] = null;
            WritePriorities(priorities, Loc.T("traffic.done.all", ("id", priorityJunction.Id)), () =>
            {
                foreach (var arm in priorityArms)
                    arm.Level = OmsiPriorityLevel.Normal;
            });
        }

        private void WritePriorities(Dictionary<int, double?> priorities, string done, Action written = null)
        {
            if (!CanEditRules)
            {
                SetStatus(ReadOnlyMessage("readonly.action.right_of_way"));
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
                SetStatusT("traffic.status.failed", ("reason", exception.Message));
                RefreshPriorityTarget();
                return;
            }
            written?.Invoke();
            if (change == null)
            {
                RefreshPriorityTarget();
                SetStatus(Loc.T("traffic.status.same", ("done", done)));
                return;
            }
            PushSteps(new List<object> { change }, Loc.T("traffic.step", ("id", record.Id)));
            RefreshPriorityTarget();
            var count = 0;
            foreach (var pair in priorities)
                if (pair.Value.HasValue)
                    count++;
            SetStatus(Loc.T("traffic.status.written", ("done", done), ("count", count)));
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
        /// For command-line checks: opens the traffic rules tool on the selected junction, writes its arms to the log and clicks
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
