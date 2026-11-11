using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The line tool (ADR 094): click the bus stops of a line in the order the bus serves them, see the way over the map's vehicle lanes at once, make the
    /// return trip from other stops (or close a ring), lead the way over another road with waypoints, and save the line with its trips.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private LineToolPanel lineOptions;
        private OmsiPathNetwork lineNetwork;
        private Task<OmsiPathNetwork> lineNetworkTask;
        private OmsiTimetable lineTimetable = new OmsiTimetable();
        private bool lineRightHand = true;
        private List<OmsiTripStopChoice> lineObjects = new List<OmsiTripStopChoice>();
        private readonly List<OmsiTripStopChoice> lineOut = new List<OmsiTripStopChoice>();
        private readonly List<OmsiTripStopChoice> lineBack = new List<OmsiTripStopChoice>();
        private bool lineBackMode;
        private bool lineRing;
        private bool lineWaypointMode;
        private string lineName = string.Empty;
        private string lineGroup;
        private OmsiTripPlan linePlanOut;
        private OmsiTripPlan linePlanBack;
        private bool lineRail;
        private OmsiPathNetwork lineRailNetwork;
        private Task<OmsiPathNetwork> lineRailTask;
        private OmsiTrackPlan lineTrackOut;
        private OmsiTrackPlan lineTrackBack;
        private List<OmsiMapPoint[]> lineHoverLines = new List<OmsiMapPoint[]>();
        private string lineHoverText = string.Empty;
        private int lineHoverKey = -1;

        private List<OmsiTripStopChoice> CurrentLineList => lineBackMode ? lineBack : lineOut;

        /// <summary>Makes the tool's panel and wires its events.</summary>
        private void CreateLineTool(VisualElement host)
        {
            lineOptions = new LineToolPanel();
            lineOptions.style.display = DisplayStyle.None;
            lineOptions.NameChanged += name => lineName = name.Trim();
            lineOptions.GroupChanged += group => lineGroup = group;
            lineOptions.DirectionChosen += back =>
            {
                lineBackMode = back && !lineRing;
                LineChanged(false);
            };
            lineOptions.RingChanged += ring =>
            {
                lineRing = ring;
                if (ring)
                    lineBackMode = false;
                LineChanged(true);
            };
            lineOptions.WaypointModeChanged += on => lineWaypointMode = on;
            lineOptions.RailChanged += on =>
            {
                lineRail = on;
                if (on)
                {
                    StartRailNetwork();
                    ChooseRailGroup();
                }
                LineChanged(true);
            };
            lineOptions.CopyReversedRequested += () =>
            {
                lineBack.Clear();
                for (var index = lineOut.Count - 1; index >= 0; index--)
                    lineBack.Add(lineOut[index]);
                lineRing = false;
                lineBackMode = true;
                LineChanged(true);
            };
            lineOptions.ClearRequested += () =>
            {
                CurrentLineList.Clear();
                if (!lineBackMode)
                    lineRing = false;
                LineChanged(true);
            };
            lineOptions.MoveRequested += (index, delta) =>
            {
                var list = CurrentLineList;
                var target = index + delta;
                if (index < 0 || index >= list.Count || target < 0 || target >= list.Count)
                    return;
                var item = list[index];
                list.RemoveAt(index);
                list.Insert(target, item);
                LineChanged(true);
            };
            lineOptions.RemoveRequested += index =>
            {
                var list = CurrentLineList;
                if (index >= 0 && index < list.Count)
                    list.RemoveAt(index);
                LineChanged(true);
            };
            lineOptions.StopNamed += (stop, name) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return;
                stop.Name = name.Trim();
                // The name is the object's first label, as in OMSI's editor; Busstops.cfg gets it with the line.
                if (stop.TileIndex >= 0 && stop.TileIndex < currentSummary.Tiles.Count)
                {
                    var tile = currentSummary.Tiles[stop.TileIndex];
                    var record = tile.Objects.Find(item => item.Id == stop.ObjectId);
                    if (record != null)
                        try
                        {
                            var labelChange = session.SetElementLabels(tile, record, OmsiMapSession.StopLabels(record.Labels, stop.Name));
                            if (labelChange != null)
                            {
                                PushHistory(new ObjectEditStep { TileX = tile.X, TileY = tile.Y, ObjectId = labelChange.ElementId, Change = labelChange,
                                    Description = Loc.T("busstop.step", ("name", stop.Name)) });
                                RefreshAfterChange(null);
                            }
                        }
                        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException)
                        {
                            Debug.LogException(exception);
                        }
                }
                LineChanged(true);
            };
            lineOptions.SaveRequested += SaveLine;
            host.Add(lineOptions);
        }

        private void BeginLineTool()
        {
            if (lineOptions == null || mapCanvas == null || currentSummary == null || session == null)
                return;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            lineOptions.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.BeginRoad();
            try
            {
                lineTimetable = OmsiTimetable.Read(session.Workspace);
                lineRightHand = !session.HasLeftHandTraffic();
                lineObjects = session.ListStopObjects(currentSummary);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("line.status.failed", ("reason", exception.Message));
                return;
            }
            // Stops the draft holds stay the same objects, so renaming and the lists keep agreeing.
            ReplaceDraftStops();
            var groups = session.ReadAiGroups();
            if (lineGroup == null || groups.Count > 0 && !groups.Contains(lineGroup))
                lineGroup = groups.Find(each => each.IndexOf("bus", StringComparison.OrdinalIgnoreCase) >= 0) ?? (groups.Count > 0 ? groups[0] : string.Empty);
            lineOptions.SetGroups(groups, lineGroup);
            lineOptions.SetName(lineName);
            ApplyTripEditDraft();
            lineNetwork = null;
            StartLineNetwork();
            LineChanged(true);
            SetStatusT("line.status.start");
        }

        /// <summary>The draft's stops after the map was read again: the same objects by ID.</summary>
        private void ReplaceDraftStops()
        {
            foreach (var list in new[] { lineOut, lineBack })
                for (var index = 0; index < list.Count; index++)
                {
                    if (list[index].IsWaypoint)
                        continue;
                    var fresh = lineObjects.Find(each => each.ObjectId == list[index].ObjectId);
                    if (fresh != null)
                    {
                        fresh.Name = list[index].Name;
                        list[index] = fresh;
                    }
                }
        }

        private void StartLineNetwork()
        {
            var summary = currentSummary;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var task = Task.Run(() => OmsiPathNetwork.ForMap(summary, root));
            lineNetworkTask = task;
            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                if (lineNetworkTask != task)
                    return;
                lineNetworkTask = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatusT("line.status.failed", ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    return;
                }
                lineNetwork = task.Result;
                if (activeTool == EditorTool.Line)
                    LineChanged(true);
            }).Every(150);
        }

        /// <summary>Reads the rails of the map in the background the first time a line on rails is made.</summary>
        private void StartRailNetwork()
        {
            if (lineRailNetwork != null || lineRailTask != null || currentSummary == null)
                return;
            var summary = currentSummary;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var task = Task.Run(() => OmsiPathNetwork.ForMap(summary, root, 2));
            lineRailTask = task;
            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                if (lineRailTask != task)
                    return;
                lineRailTask = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatusT("line.status.failed", ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    return;
                }
                lineRailNetwork = task.Result;
                if (activeTool == EditorTool.Line && lineRail)
                    LineChanged(true);
            }).Every(150);
        }

        /// <summary>A line on rails runs with trains or trams: the first vehicle group that sounds like one is chosen, unless the group already does.</summary>
        private void ChooseRailGroup()
        {
            var groups = session.ReadAiGroups();
            Func<string, bool> sounds = name => new[] { "bahn", "tram", "rail", "zug", "train", "metro", "tren", "tramv" }.Any(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
            if (lineGroup != null && sounds(lineGroup))
                return;
            var rail = groups.Find(name => sounds(name));
            if (rail == null)
                return;
            lineGroup = rail;
            lineOptions.SetGroups(groups, lineGroup);
        }

        private OmsiTrackPlan TrackPlanOf(List<OmsiTripStopChoice> items)
        {
            if (lineRailNetwork == null || items.Count(item => !item.IsWaypoint) < 2)
                return null;
            return session.PlanTrack(currentSummary, items, lineRailNetwork);
        }

        private void EndLineTool()
        {
            mapCanvas?.EndRoad();
            mapCanvas?.ClearRoadPreview();
            lineNetworkTask = null;
            LeaveTripEdit();
            if (lineOptions != null)
                lineOptions.style.display = DisplayStyle.None;
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
        }

        /// <summary>The stop object nearest to a position, within a few pixels of the map's scale; null when none is that near.</summary>
        private OmsiTripStopChoice StopAt(double x, double y)
        {
            var reach = Math.Max(5.0, mapCanvas.MetersPerPixel * 14.0);
            OmsiTripStopChoice best = null;
            var bestDistance = reach;
            foreach (var stop in lineObjects)
            {
                var distance = Math.Sqrt((stop.World.X - x) * (stop.World.X - x) + (stop.World.Y - y) * (stop.World.Y - y));
                if (distance <= bestDistance)
                {
                    best = stop;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void ClickLine(double x, double y)
        {
            if (!CanEdit || session == null)
                return;
            var list = CurrentLineList;
            if (lineWaypointMode)
            {
                if (!list.Exists(item => !item.IsWaypoint))
                {
                    SetStatusT("line.status.waypoint_first");
                    return;
                }
                if (lineNetwork != null && lineNetwork.PlaceWaypointCandidates(new OmsiMapPoint(x, y)).Count == 0)
                {
                    SetStatusT("line.status.waypoint_off");
                    return;
                }
                list.Add(new OmsiTripStopChoice { Name = "waypoint", IsWaypoint = true, World = new OmsiMapPoint(x, y) });
                LineChanged(true);
                return;
            }
            var stop = StopAt(x, y);
            if (stop == null)
            {
                SetStatusT("line.status.click_stop");
                return;
            }
            var stops = list.Where(item => !item.IsWaypoint).ToList();
            if (stops.Count > 0 && stops[stops.Count - 1] == stop && list[list.Count - 1] == stop)
                return;
            // Clicking the first stop again closes a ring.
            if (!lineBackMode && !lineRing && stops.Count >= 2 && stops[0] == stop)
                lineRing = true;
            list.Add(stop);
            LineChanged(true);
        }

        private void UpdateLinePreview()
        {
            if (mapCanvas == null || !roadPointer.HasValue)
                return;
            var hover = StopAt(roadPointer.Value.X, roadPointer.Value.Y);
            var list = CurrentLineList;
            var last = list.LastOrDefault(item => !item.IsWaypoint);
            var key = hover == null ? -1 : hover.ObjectId;
            if (key == lineHoverKey)
                return;
            lineHoverKey = key;
            lineHoverLines = new List<OmsiMapPoint[]>();
            lineHoverText = string.Empty;
            if (hover != null && last != null && hover != last && lineRail && lineRailNetwork != null && !lineWaypointMode)
            {
                var track = session.PlanTrack(currentSummary, new List<OmsiTripStopChoice> { last, hover }, lineRailNetwork);
                lineHoverLines = track.Lines.ToList();
                lineHoverText = track.IsValid ? Loc.T("line.hover", ("name", hover.Name), ("length", Math.Round(track.LengthMeters))) : Loc.T("line.hover.none", ("name", hover.Name));
            }
            else if (hover != null && last != null && hover != last && !lineRail && lineNetwork != null && !lineWaypointMode)
            {
                var plan = session.PlanTrip(currentSummary, lineTimetable, new List<OmsiTripStopChoice> { last, hover }, lineNetwork, lineRightHand);
                lineHoverLines = plan.Legs.SelectMany(leg => leg.Lines).ToList();
                lineHoverText = plan.IsValid ? Loc.T("line.hover", ("name", hover.Name), ("length", Math.Round(plan.LengthMeters))) : Loc.T("line.hover.none", ("name", hover.Name));
            }
            DrawLine();
        }

        /// <summary>The draft changed: the way is found again, the panel and the map show it.</summary>
        private void LineChanged(bool replan)
        {
            if (lineOptions == null)
                return;
            if (replan)
            {
                lineHoverKey = -1;
                lineHoverLines = new List<OmsiMapPoint[]>();
                linePlanOut = lineRail ? null : PlanOf(lineOut);
                linePlanBack = lineRing || lineRail ? null : PlanOf(lineBack);
                lineTrackOut = lineRail ? TrackPlanOf(lineOut) : null;
                lineTrackBack = lineRail && !lineRing ? TrackPlanOf(lineBack) : null;
            }
            lineOptions.ShowDirection(lineBackMode, lineRing, lineWaypointMode, CurrentLineList);
            ShowLineResult();
            DrawLine();
        }

        private OmsiTripPlan PlanOf(List<OmsiTripStopChoice> items)
        {
            if (lineNetwork == null || items.Count(item => !item.IsWaypoint) < 2)
                return null;
            return session.PlanTrip(currentSummary, lineTimetable, items, lineNetwork, lineRightHand);
        }

        private void ShowLineResult()
        {
            if (lineRail ? lineRailNetwork == null : lineNetwork == null)
            {
                lineOptions.ShowResult(Loc.T("line.network"), false);
                return;
            }
            var text = new List<string>();
            var problem = false;
            if (lineRail)
            {
                foreach (var pair in new[] { new KeyValuePair<string, OmsiTrackPlan>(lineRing ? "line.result.ring" : "line.result.out", lineTrackOut),
                                             new KeyValuePair<string, OmsiTrackPlan>("line.result.back", lineTrackBack) })
                {
                    if (pair.Value == null)
                        continue;
                    if (pair.Value.IsValid)
                        text.Add(Loc.T(pair.Key, ("count", pair.Value.Stations.Count), ("length", Math.Round(pair.Value.LengthMeters)))); // i18n:keys
                    else
                    {
                        problem = true;
                        text.Add(Loc.T(pair.Key + ".problem", ("problems", string.Join(" ", pair.Value.Problems)))); // i18n:keys
                    }
                }
                lineOptions.ShowResult(string.Join("\n", text), problem);
                return;
            }
            foreach (var pair in new[] { new KeyValuePair<string, OmsiTripPlan>(lineRing ? "line.result.ring" : "line.result.out", linePlanOut),
                                         new KeyValuePair<string, OmsiTripPlan>("line.result.back", linePlanBack) })
            {
                if (pair.Value == null)
                    continue;
                if (pair.Value.IsValid)
                    text.Add(Loc.T(pair.Key, ("count", pair.Value.Legs.Count + 1), ("length", Math.Round(pair.Value.LengthMeters)))); // i18n:keys
                else
                {
                    problem = true;
                    text.Add(Loc.T(pair.Key + ".problem", ("problems", string.Join(" ", pair.Value.Problems)))); // i18n:keys
                }
            }
            if (lineHoverText.Length > 0)
                text.Add(lineHoverText);
            lineOptions.ShowResult(string.Join("\n", text), problem);
        }

        /// <summary>The stop objects as rings, the chosen stops bigger, and the ways of both directions over the map.</summary>
        private void DrawLine()
        {
            if (mapCanvas == null || activeTool != EditorTool.Line)
                return;
            var marks = new List<OmsiMapPoint[]>();
            var chosen = new HashSet<int>(lineOut.Concat(lineBack).Where(item => !item.IsWaypoint).Select(item => item.ObjectId));
            foreach (var stop in lineObjects)
                marks.Add(Circle(stop.World, chosen.Contains(stop.ObjectId) ? 8.0 : 4.0));
            foreach (var item in lineOut.Concat(lineBack).Where(item => item.IsWaypoint))
                marks.Add(Circle(item.World, 3.0));
            foreach (var plan in new[] { linePlanOut, linePlanBack })
                if (plan != null)
                    foreach (var leg in plan.Legs)
                        marks.AddRange(leg.Lines);
            foreach (var track in new[] { lineTrackOut, lineTrackBack })
                if (track != null)
                    marks.AddRange(track.Lines);
            marks.AddRange(lineHoverLines);
            var valid = (linePlanOut == null || linePlanOut.IsValid) && (linePlanBack == null || linePlanBack.IsValid) &&
                        (lineTrackOut == null || lineTrackOut.IsValid) && (lineTrackBack == null || lineTrackBack.IsValid);
            mapCanvas.SetRoadPreview(marks, valid, null, null, null);
        }

        private static string FileName(string text)
        {
            var result = new System.Text.StringBuilder();
            foreach (var character in text ?? string.Empty)
                result.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : character == ' ' ? '_' : '\0');
            return result.ToString().Replace("\0", string.Empty);
        }

        private OmsiTripSpec TripSpec(string name, OmsiTripPlan plan, string destination)
        {
            var minutes = Math.Max(1.0, Math.Round((plan.LengthMeters / (25.0 / 3.6) / 60.0 + 0.5 * plan.Legs.Count) * 2.0) / 2.0);
            return new OmsiTripSpec { Name = name, Destination = destination, Plan = plan, RunningMinutes = minutes };
        }

        private void SaveLine()
        {
            if (!CanEdit || session == null || lineOptions == null)
                return;
            if (lineEditTrip != null)
            {
                SaveTripStops();
                return;
            }
            var line = FileName(lineOptions.LineName);
            if (line.Length == 0)
            {
                SetStatusT("line.status.need_name");
                return;
            }
            if (lineRail)
            {
                SaveRailLine(line);
                return;
            }
            var specs = new List<OmsiTripSpec>();
            if (linePlanOut == null || !linePlanOut.IsValid)
            {
                SetStatusT("line.status.need_route");
                return;
            }
            var outFirst = linePlanOut.Legs[0].From;
            var outLast = linePlanOut.Legs[linePlanOut.Legs.Count - 1].To;
            if (lineRing)
                specs.Add(TripSpec(line + "_ring", linePlanOut, outFirst.Name));
            else
            {
                specs.Add(TripSpec(line + "_" + FileName(outFirst.Name) + "-" + FileName(outLast.Name), linePlanOut, outLast.Name));
                if (linePlanBack != null)
                {
                    if (!linePlanBack.IsValid)
                    {
                        SetStatusT("line.status.need_route");
                        return;
                    }
                    var backFirst = linePlanBack.Legs[0].From;
                    var backLast = linePlanBack.Legs[linePlanBack.Legs.Count - 1].To;
                    specs.Add(TripSpec(line + "_" + FileName(backFirst.Name) + "-" + FileName(backLast.Name), linePlanBack, backLast.Name));
                }
            }
            OmsiTerrainChange change;
            try
            {
                change = session.CreateLine(line, lineOptions.Group, specs);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("line.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("line.step", ("name", line)) });
            StartRoutesBuild(currentSummary);
            // The files just written are checked as the map check does: a line that OMSI would not run is reported now, not in the game.
            var findings = new List<string>();
            try
            {
                foreach (var problem in OmsiTimetableCheck.Check(OmsiTimetable.Read(session.Workspace), currentSummary).Problems)
                    findings.Add(problem.ToString());
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
            }
            if (findings.Count == 0)
                SetStatusT("line.status.saved", ("name", line), ("count", specs.Count));
            else
            {
                SetStatusT("line.status.saved_findings", ("name", line), ("count", specs.Count), ("findings", findings.Count));
                foreach (var finding in findings)
                    Debug.LogWarning("Timetable check: " + finding);
            }
            Debug.Log("Line: " + statusLabel?.text);
            lineOut.Clear();
            lineBack.Clear();
            lineRing = false;
            lineBackMode = false;
            lineName = string.Empty;
            lineOptions.SetName(string.Empty);
            OpenTimetable(line);
            lineTimetable = OmsiTimetable.Read(session.Workspace);
            LineChanged(true);
        }

        /// <summary>For checks: -line [-lineName x] [-lineStops id,id,...] [-lineBack id,...] [-lineWaypoint x y] [-lineRing] [-lineSave].</summary>
        private void RunLineArguments(Func<string, int, string> argument, string[] args)
        {
            if (Array.IndexOf(args, "-line") < 0 || session == null || !session.CanEdit)
                return;
            SetTool(EditorTool.Line);
            if (lineNetworkTask != null)
                lineNetworkTask.Wait();
            lineNetwork = lineNetworkTask?.Result ?? lineNetwork ?? OmsiPathNetwork.ForMap(currentSummary, OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath));
            if (Array.IndexOf(args, "-lineRail") >= 0)
            {
                lineRail = true;
                StartRailNetwork();
                if (lineRailTask != null)
                    lineRailTask.Wait();
                lineRailNetwork = lineRailTask?.Result ?? lineRailNetwork ?? OmsiPathNetwork.ForMap(currentSummary, OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath), 2);
            }
            var groupArgument = argument("-lineGroup", 1);
            if (groupArgument != null)
            {
                lineGroup = groupArgument;
                lineOptions.SetGroups(session.ReadAiGroups(), groupArgument);
            }
            else if (lineRail)
            {
                ChooseRailGroup();
            }
            var name = argument("-lineName", 1);
            if (name != null)
            {
                lineName = name;
                lineOptions.SetName(name);
            }
            foreach (var pair in new[] { new KeyValuePair<string, List<OmsiTripStopChoice>>("-lineStops", lineOut), new KeyValuePair<string, List<OmsiTripStopChoice>>("-lineBack", lineBack) })
            {
                var ids = argument(pair.Key, 1);
                if (ids == null)
                    continue;
                pair.Value.Clear();
                foreach (var text in ids.Split(','))
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    {
                        var stop = lineObjects.Find(each => each.ObjectId == id);
                        if (stop != null)
                            pair.Value.Add(stop);
                    }
            }
            if (Array.IndexOf(args, "-lineWaypoint") >= 0 && double.TryParse(argument("-lineWaypoint", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var wx) &&
                double.TryParse(argument("-lineWaypoint", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var wy) && lineOut.Count > 1)
                lineOut.Insert(lineOut.Count - 1, new OmsiTripStopChoice { Name = "waypoint", IsWaypoint = true, World = new OmsiMapPoint(wx, wy) });
            lineRing = Array.IndexOf(args, "-lineRing") >= 0;
            if (argument("-lineBack", 1) != null && !lineRing)
                lineBackMode = Array.IndexOf(args, "-lineBackMode") >= 0;
            LineChanged(true);
            Debug.Log("Line plan: out " + (linePlanOut == null ? "none" : linePlanOut.IsValid ? Math.Round(linePlanOut.LengthMeters) + " m" : string.Join(" ", linePlanOut.Problems)) +
                      ", back " + (linePlanBack == null ? "none" : linePlanBack.IsValid ? Math.Round(linePlanBack.LengthMeters) + " m" : string.Join(" ", linePlanBack.Problems)));
            if (lineRail)
                Debug.Log("Track plan: out " + (lineTrackOut == null ? "none" : lineTrackOut.IsValid ? Math.Round(lineTrackOut.LengthMeters) + " m, " + lineTrackOut.Entries.Count + " entries" : string.Join(" ", lineTrackOut.Problems)));
            if (Array.IndexOf(args, "-lineSave") >= 0)
                SaveLine();
        }
    }
}
