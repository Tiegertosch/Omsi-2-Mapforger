using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// What goes on road pieces in the traffic rules tool and the dragging of signs to the map (ADR 098, M4): a speed limit (Speed tab) or an
    /// overtaking ban (first tab) goes on every lane path of the road piece it hits, or of every piece of the road, as one undo step; each has
    /// its own eraser that leaves the other rule alone. A drop, a click with an armed tile and the command-line checks all end in <see cref="ApplyTrafficItem"/>.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement trafficGhost;
        private TrafficItem? trafficDragged;
        private string trafficHoverKey;
        private int trafficHoverCount;
        private OmsiMapPoint trafficProbe = new OmsiMapPoint(double.MinValue, double.MinValue);

        /// <summary>The name of a sign as a sentence takes it, such as "Priority road".</summary>
        private static string SignName(OmsiRightOfWaySign sign)
        {
            switch (sign)
            {
                case OmsiRightOfWaySign.PriorityRoad:
                    return Loc.T("traffic.sign.priority_road");
                case OmsiRightOfWaySign.Yield:
                    return Loc.T("traffic.sign.yield");
                default:
                    return Loc.T("traffic.sign.normal");
            }
        }

        /// <summary>The name of a tile: its sign, or its speed.</summary>
        private string TrafficItemName(TrafficItem item)
        {
            switch (item.Kind)
            {
                case TrafficItemKind.RightOfWay:
                    return SignName(item.Sign);
                case TrafficItemKind.Speed:
                    return Loc.T("traffic.sign.speed", ("kmh", item.Kmh));
                case TrafficItemKind.CustomSpeed:
                    return trafficOptions.CustomKmh.HasValue ? Loc.T("traffic.sign.speed", ("kmh", trafficOptions.CustomKmh.Value.ToString("0.#", CultureInfo.InvariantCulture)))
                        : Loc.T("traffic.sign.custom");
                case TrafficItemKind.OvertakingBan:
                    return Loc.T("traffic.sign.overtaking");
                case TrafficItemKind.ClearOvertaking:
                    return Loc.T("traffic.sign.clear_overtaking");
                default:
                    return Loc.T("traffic.sign.clear_road");
            }
        }

        /// <summary>The tile in use: the one being dragged, else the armed one.</summary>
        private TrafficItem? CurrentTrafficItem => trafficDragged ?? trafficOptions?.Armed;

        private static OmsiRoadRuleKind RoadKindOf(TrafficItem item)
        {
            return item.Kind == TrafficItemKind.OvertakingBan ? OmsiRoadRuleKind.OvertakingBan
                : item.Kind == TrafficItemKind.ClearSpeed ? OmsiRoadRuleKind.ClearSpeedLimit
                : item.Kind == TrafficItemKind.ClearOvertaking ? OmsiRoadRuleKind.ClearOvertakingBan : OmsiRoadRuleKind.SpeedLimit;
        }

        private readonly Dictionary<string, bool> trafficRoadAssets = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>True for a spline of a road type with a lane for vehicles; the answer is kept per road type, as the canvas asks it while drawing.</summary>
        private bool IsTrafficRoad(OmsiSplineRecord spline)
        {
            var asset = spline.AssetPath ?? string.Empty;
            if (!trafficRoadAssets.TryGetValue(asset, out var road))
            {
                road = OmsiTrafficSigns.RoadLanes(RoadProfiles().Get(asset)).Count > 0;
                trafficRoadAssets[asset] = road;
            }
            return road;
        }

        /// <summary>The road piece nearest a point that has a lane for vehicles; any piece when there is none, so the reply can say so.</summary>
        private bool FindTrafficPiece(OmsiMapPoint point, out OmsiTileSummary tile, out OmsiSplineRecord spline)
        {
            tile = null;
            spline = null;
            return mapCanvas != null && currentSummary != null &&
                   mapCanvas.TrySplineAtWorld(point.X, point.Y, candidate => OmsiTrafficSigns.RoadLanes(RoadProfiles().Get(candidate.AssetPath)).Count > 0, out tile, out spline);
        }

        /// <summary>
        /// The pieces a speed goes to: the one given, or with Whole road every piece of its road from junction or free end to junction or free
        /// end that has its road type (as Whole road in the inspector chooses them).
        /// </summary>
        private List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> TrafficPieces(OmsiTileSummary tile, OmsiSplineRecord spline)
        {
            if (trafficOptions != null && trafficOptions.WholeRoad)
                return OmsiWholeRoad.Pieces(new OmsiSplineAttachmentLayout(currentSummary), tile, spline);
            return new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> { new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(tile, spline) };
        }

        private void ClearTrafficHover()
        {
            trafficHoverKey = null;
            trafficHoverCount = 0;
            mapCanvas?.SetTrafficHighlight(null);
        }

        /// <summary>Marks the road piece (or the whole road) a tile in use would go on, and says what a click would do.</summary>
        private void UpdateTrafficRoadPreview()
        {
            if (mapCanvas == null || trafficOptions == null)
                return;
            var item = CurrentTrafficItem;
            if (!item.HasValue || !item.Value.OnRoad || !roadPointer.HasValue || !FindTrafficPiece(roadPointer.Value, out var tile, out var spline) ||
                OmsiTrafficSigns.RoadLanes(RoadProfiles().Get(spline.AssetPath)).Count == 0)
            {
                if (trafficHoverKey != null)
                    ClearTrafficHover();
                UpdateTrafficRoadHint();
                return;
            }
            var key = tile.X + "|" + tile.Y + "|" + spline.Id + "|" + trafficOptions.WholeRoad;
            if (key != trafficHoverKey)
            {
                var pieces = TrafficPieces(tile, spline);
                trafficHoverKey = key;
                trafficHoverCount = pieces.Count;
                mapCanvas.SetTrafficHighlight(pieces.Select(pair => pair.Value));
            }
            UpdateTrafficRoadHint();
        }

        private void UpdateTrafficRoadHint()
        {
            var item = CurrentTrafficItem;
            if (!item.HasValue || !item.Value.OnRoad)
            {
                trafficOptions.ShowHint(Loc.T("traffic.hint.speed"), false);
                return;
            }
            var name = TrafficItemName(item.Value);
            if (trafficHoverKey != null)
                trafficOptions.ShowHint(Loc.T("traffic.hint.road", ("sign", name), ("count", trafficHoverCount)), false);
            else
                trafficOptions.ShowHint(Loc.T("traffic.hint.speed_armed", ("sign", name), ("whole", trafficOptions.WholeRoad ? "whole" : "piece")), false);
        }

        /// <summary>Puts a speed sign, the ban or one of the erasers on the road piece at a point, or on its whole road, as one undo step.</summary>
        private void ApplyRoadItem(TrafficItem item, OmsiMapPoint point)
        {
            if (!CanEditRules)
            {
                SetStatus(ReadOnlyMessage("readonly.action.traffic_rules"));
                return;
            }
            if (!FindTrafficPiece(point, out var tile, out var spline))
            {
                SetStatusT("traffic.status.no_road");
                return;
            }
            var kmh = 0.0;
            if (item.Kind == TrafficItemKind.Speed)
                kmh = item.Kmh;
            else if (item.Kind == TrafficItemKind.CustomSpeed)
            {
                if (!trafficOptions.CustomKmh.HasValue)
                {
                    SetStatusT("traffic.status.custom_invalid");
                    return;
                }
                kmh = trafficOptions.CustomKmh.Value;
            }
            var name = TrafficItemName(item);
            OmsiRoadRuleEdits edits;
            OmsiSplineChange change;
            try
            {
                edits = OmsiTrafficSigns.Edits(TrafficPieces(tile, spline), asset => RoadProfiles().Get(asset), RoadKindOf(item), kmh);
                if (edits.Edits.Count == 0)
                {
                    SetStatusT("traffic.status.no_lanes");
                    return;
                }
                change = session.SetSplinePathRules(currentSummary, edits.Edits);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("traffic.road.failed", ("reason", exception.Message));
                return;
            }
            var count = edits.Edits.Count;
            if (change == null)
            {
                SetStatus(Loc.T("traffic.road.same", ("sign", name), ("count", count)));
                return;
            }
            // The splines are read again; the selection finds them by their IDs.
            RefreshSplineViews(change);
            PushHistory(new ObjectEditStep { SplineChange = change, Description = Loc.T("traffic.road.step", ("sign", name), ("count", count)) });
            RefreshAfterChange(null);
            ClearTrafficHover();
            UpdateTrafficPreview();
            var done = Loc.T("traffic.road.done", ("sign", name), ("count", count));
            SetStatus(edits.Skipped > 0 ? done + " " + Loc.T("traffic.road.skipped", ("skipped", edits.Skipped)) : done);
        }

        // ---- A sign dragged from a tile to the map ----

        /// <summary>The map point under a position of the window, or false when something else than the map is there.</summary>
        private bool TrafficMapPointAt(Vector2 panelPosition, out OmsiMapPoint world)
        {
            world = default;
            if (mapCanvas == null || rootElement?.panel == null)
                return false;
            var picked = rootElement.panel.Pick(panelPosition);
            var over = false;
            for (var element = picked; element != null; element = element.parent)
                if (element == mapCanvas)
                {
                    over = true;
                    break;
                }
            if (!over || !mapCanvas.TryLocalToWorld(mapCanvas.WorldToLocal(panelPosition), out var x, out var y))
                return false;
            world = new OmsiMapPoint(x, y);
            return true;
        }

        private void BeginTrafficDrag(TrafficItem item, Vector2 position)
        {
            EndTrafficDrag();
            trafficDragged = item;
            var ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("traffic-ghost");
            ghost.style.position = Position.Absolute;
            if (item.IsEraser)
            {
                ghost.AddToClassList("traffic-ghost-icon");
                ghost.Add(new IconElement("eraser"));
            }
            else
            {
                var kind = item.Kind == TrafficItemKind.RightOfWay ? TrafficSignPainter.Of(item.Sign)
                    : item.Kind == TrafficItemKind.OvertakingBan ? TrafficSignKind.OvertakingBan : TrafficSignKind.Speed;
                var kmh = item.Kind == TrafficItemKind.Speed ? item.Kmh : item.Kind == TrafficItemKind.CustomSpeed && trafficOptions.CustomKmh.HasValue ? (int)Math.Round(trafficOptions.CustomKmh.Value) : 0;
                var sign = new TrafficSignElement(kind, kmh);
                sign.AddToClassList("traffic-sign");
                ghost.Add(sign);
            }
            rootElement.Add(ghost);
            trafficGhost = ghost;
            MoveTrafficDrag(item, position);
        }

        private void MoveTrafficDrag(TrafficItem item, Vector2 position)
        {
            // A drag that outlived the tool must not touch the selection of another one.
            if (activeTool != EditorTool.Priorities || trafficDragged == null)
                return;
            if (trafficGhost != null)
            {
                var local = rootElement.WorldToLocal(position);
                trafficGhost.style.left = local.x - 24f;
                trafficGhost.style.top = local.y - 24f;
            }
            if (!TrafficMapPointAt(position, out var world))
            {
                roadPointer = null;
                ClearTrafficHover();
                if (item.OnRoad)
                    UpdateTrafficRoadHint();
                else
                {
                    priorityHover = -1;
                    UpdatePriorityPreview();
                }
                return;
            }
            roadPointer = world;
            // A sign near a junction brings the signs of its arms up, so it can be dropped on one.
            if (!item.OnRoad && Distance(world, trafficProbe) > 2.0)
            {
                trafficProbe = world;
                if (PriorityHandleAt(world) < 0 && PriorityJunctionAt(world, out var tile, out var record) && !ReferenceEquals(record, priorityJunction))
                    TargetPriorityJunction(tile, record);
            }
            UpdateTrafficPreview();
        }

        private void DropTrafficSign(TrafficItem item, Vector2 position)
        {
            if (activeTool != EditorTool.Priorities || trafficDragged == null)
            {
                EndTrafficDrag();
                return;
            }
            var over = TrafficMapPointAt(position, out var world);
            EndTrafficDrag();
            if (!over)
            {
                SetStatusT("traffic.status.dropped_outside");
                return;
            }
            roadPointer = world;
            ApplyTrafficItem(item, world);
        }

        /// <summary>Takes the sign from under the pointer and the marks of the target from the map.</summary>
        private void EndTrafficDrag()
        {
            trafficGhost?.RemoveFromHierarchy();
            trafficGhost = null;
            trafficDragged = null;
            if (activeTool == EditorTool.Priorities)
            {
                ClearTrafficHover();
                UpdateTrafficPreview();
            }
        }

        // ---- Command-line checks ----

        /// <summary>A name from the command line as a tile: priority, yield, normal, overtaking, clearspeed, clearovertaking, custom or a speed in km/h.</summary>
        private static bool TryParseTrafficItem(string name, out TrafficItem item)
        {
            item = default;
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "priority":
                    item = new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.PriorityRoad };
                    return true;
                case "yield":
                    item = new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.Yield };
                    return true;
                case "normal":
                    item = new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.Normal };
                    return true;
                case "overtaking":
                    item = new TrafficItem { Kind = TrafficItemKind.OvertakingBan };
                    return true;
                case "clearspeed":
                    item = new TrafficItem { Kind = TrafficItemKind.ClearSpeed };
                    return true;
                case "clearovertaking":
                    item = new TrafficItem { Kind = TrafficItemKind.ClearOvertaking };
                    return true;
                case "custom":
                    item = new TrafficItem { Kind = TrafficItemKind.CustomSpeed };
                    return true;
            }
            if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kmh) && kmh > 0 && kmh <= OmsiTrafficSigns.MaximumKmh)
            {
                item = new TrafficItem { Kind = TrafficItemKind.Speed, Kmh = kmh };
                return true;
            }
            return false;
        }

        /// <summary>The name <see cref="TrafficToolOptions.ShowTab"/> takes for the tab a tile stands in.</summary>
        private static string TabName(TrafficItem item)
        {
            return item.Tab == TrafficTab.Speed ? "speed" : "rules";
        }

        private static List<string> WordsAfter(string[] args, string flag)
        {
            var words = new List<string>();
            var at = Array.IndexOf(args, flag);
            for (var index = at + 1; at >= 0 && index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal); index++)
                words.Add(args[index]);
            return words;
        }

        /// <summary>
        /// For command-line checks, after the map and the selection: <c>-trafficTab rules|speed</c> opens the traffic rules tool on that tab,
        /// <c>-trafficArm &lt;tile&gt;</c> arms a tile, <c>-trafficSign &lt;sign&gt; &lt;arm n&gt; ...</c> drops signs on the arms of the selected
        /// junction, <c>-trafficSpeed &lt;kmh|overtaking|clearspeed|clearovertaking&gt; [whole]</c> drops a speed, the ban or an eraser on the selected spline's piece or whole road, and
        /// <c>-trafficPointer tileX tileY x y</c> holds the pointer there. The status goes to the log as "Traffic sign:" and "Traffic speed:".
        /// </summary>
        private void RunTrafficArguments(string[] args)
        {
            var tab = WordsAfter(args, "-trafficTab").FirstOrDefault();
            var signs = WordsAfter(args, "-trafficSign");
            var speed = WordsAfter(args, "-trafficSpeed");
            var arm = WordsAfter(args, "-trafficArm").FirstOrDefault();
            var pointer = WordsAfter(args, "-trafficPointer");
            if (tab == null && signs.Count == 0 && speed.Count == 0 && arm == null && pointer.Count == 0)
                return;
            if (currentSummary == null || trafficOptions == null)
                return;
            if (activeTool != EditorTool.Priorities)
                SetTool(EditorTool.Priorities);
            if (tab != null && !trafficOptions.ShowTab(tab))
                Debug.LogWarning("Traffic tab: unknown tab " + tab + "; it is rules or speed.");
            for (var index = 0; index + 1 < signs.Count; index += 2)
            {
                if (!TryParseTrafficItem(signs[index], out var item) || item.OnRoad || !int.TryParse(signs[index + 1], out var number) || number < 1 || number > priorityArms.Count)
                {
                    Debug.LogWarning("Traffic sign: " + signs[index] + " " + signs[index + 1] + " is no sign and arm of the junction (arms: " + priorityArms.Count + ").");
                    continue;
                }
                trafficOptions.ShowTab("rules");
                ApplyTrafficItem(item, priorityHandlePoints[number - 1]);
                Debug.Log("Traffic sign: " + statusLabel?.text);
            }
            if (speed.Count > 0)
            {
                // The tab of the tile (the ban is on the first one); the speed tab for a tile that is none.
                trafficOptions.ShowTab(TryParseTrafficItem(speed[0], out var shown) ? TabName(shown) : "speed");
                trafficOptions.ChooseScope(speed.Contains("whole"));
                if (selectedSpline == null || selectedSplineTile == null)
                    Debug.LogWarning("Traffic speed: select a spline first (-selectSpline).");
                else if (!TryParseTrafficItem(speed[0], out var item) || !item.OnRoad)
                    Debug.LogWarning("Traffic speed: " + speed[0] + " is no speed, overtaking, clearspeed or clearovertaking.");
                else
                {
                    var line = OmsiMapGeometry.BuildSplinePolyline(selectedSplineTile, selectedSpline, currentSummary.TileSizeMeters);
                    ApplyTrafficItem(item, line[line.Length / 2]);
                    Debug.Log("Traffic speed: " + statusLabel?.text);
                }
            }
            if (arm != null)
            {
                if (TryParseTrafficItem(arm, out var armed))
                {
                    trafficOptions.ShowTab(TabName(armed));
                    trafficOptions.Arm(armed);
                }
                else
                    Debug.LogWarning("Traffic arm: " + arm + " is no tile.");
            }
            if (pointer.Count >= 4 && int.TryParse(pointer[0], out var tileX) && int.TryParse(pointer[1], out var tileY) &&
                double.TryParse(pointer[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(pointer[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                roadPointer = OmsiMapGeometry.TileLocalToWorld(tileX, tileY, x, y, currentSummary.TileSizeMeters);
                UpdateTrafficPreview();
            }
        }

        /// <summary>
        /// Checks that need the laid-out window: <c>-trafficGhost &lt;tile&gt; px py</c> holds a sign being dragged at that pixel, from the top left,
        /// <c>-trafficDrop &lt;tile&gt; px py</c> lets it go there, as releasing the mouse would, through the same hit test of the map.
        /// </summary>
        private void RunTrafficPointerArguments(string[] args)
        {
            // What the last redraw spent on the speed signs, for the redraw budget of large maps.
            if (mapCanvas != null && activeTool == EditorTool.Priorities && Array.Exists(args, argument => argument.StartsWith("-traffic", StringComparison.Ordinal)))
                Debug.Log("Traffic signs: the last redraw spent " + mapCanvas.LastTrafficLabelMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms on " +
                          mapCanvas.LastTrafficLabelCount + " signs, " + mapCanvas.LastDrawMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms in all.");
            foreach (var flag in new[] { "-trafficGhost", "-trafficDrop" })
            {
                var words = WordsAfter(args, flag);
                if (words.Count < 3 || trafficOptions == null || activeTool != EditorTool.Priorities)
                    continue;
                if (!TryParseTrafficItem(words[0], out var item) ||
                    !float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                {
                    Debug.LogWarning(flag.Substring(1) + ": " + string.Join(" ", words) + " is no tile and pixel.");
                    continue;
                }
                trafficOptions.ShowTab(TabName(item));
                var position = new Vector2(px, py);
                BeginTrafficDrag(item, position);
                if (flag == "-trafficDrop")
                {
                    DropTrafficSign(item, position);
                    Debug.Log("Traffic drop: " + statusLabel?.text);
                }
            }
            RunTrafficLeaveArgument(args);
        }

        /// <summary>
        /// <c>-trafficLeave &lt;tile&gt; px py</c> holds a sign being dragged, switches to the drag tool, then sends the stale move and drop a
        /// late pointer event would. Logs "Traffic leave:" with whether the ghost is gone and whether a step was written (it must not be).
        /// </summary>
        private void RunTrafficLeaveArgument(string[] args)
        {
            var words = WordsAfter(args, "-trafficLeave");
            if (words.Count < 3 || trafficOptions == null || activeTool != EditorTool.Priorities)
                return;
            if (!TryParseTrafficItem(words[0], out var item) ||
                !float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
            {
                Debug.LogWarning("trafficLeave: " + string.Join(" ", words) + " is no tile and pixel.");
                return;
            }
            trafficOptions.ShowTab(TabName(item));
            var position = new Vector2(px, py);
            BeginTrafficDrag(item, position);
            var held = trafficGhost != null && trafficDragged.HasValue;
            var steps = undoSteps.Count;
            SetTool(EditorTool.Drag);
            var cleared = trafficGhost == null && !trafficDragged.HasValue && !trafficOptions.IsDragging;
            MoveTrafficDrag(item, position);
            DropTrafficSign(item, position);
            Debug.Log("Traffic leave: held=" + held + " cleared=" + cleared + " tool=" + activeTool + " steps " + steps + " -> " + undoSteps.Count);
        }
    }
}
