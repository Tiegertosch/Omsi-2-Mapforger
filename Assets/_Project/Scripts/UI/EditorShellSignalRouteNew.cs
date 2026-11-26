using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Creating a signal route (ADR 105, M4 interface): "New route..." in the popup, or "New signal route..." in the menu of a signal, replaces the list by a plan.
    /// The signal is chosen with a pipette on the map (objects, attached objects and signals along a road), the end is the next signal, a signal or a piece of
    /// track. The ways over the rails are found on a worker thread (<see cref="OmsiMapSession.FindSignalRouteWays"/>) and shown on the map, the one chosen in
    /// the accent colour and the others thin and dim; the fields of M3 give kind, aspect, distant signal, next signal and speed. "Create route" writes one undoable
    /// step. Nothing is written before that; Esc or Cancel drops the plan.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private enum NewRouteTargetMode
        {
            NextSignal,
            Signal,
            Track
        }

        private enum NewRoutePickKind
        {
            None,
            Signal,
            Target,
            Distant,
            Next
        }

        private enum NewRouteStart
        {
            Automatic,
            AtSignal,
            AfterSignal
        }

        /// <summary>What the worker thread brings back: the rails and the signals (kept for the next search while the map is as it was) and the search.</summary>
        private sealed class NewRouteWork
        {
            public OmsiPathNetwork Rails;
            public List<OmsiSignalPose> Signals;
            public OmsiSignalRouteSearch Search;
            public Exception Failure;
            public bool NoInstallation;
        }

        private const int NewRouteMaximumOverlap = 3;

        // The plan: nothing of it is written before "Create route".
        private bool newRouteActive;
        private int newRoutePlanId;
        private int? newRouteSignalId;
        private string newRouteSignalFile;
        private NewRouteTargetMode newRouteTargetMode;
        private int? newRouteTargetSignalId;
        private bool newRouteHasTrackTarget;
        private int newRouteTrackTileX, newRouteTrackTileY, newRouteTrackElement, newRouteTrackPath;
        private NewRouteStart newRouteStart;
        private int newRouteOverlap = 1;
        private NewRoutePickKind newRoutePick;
        private float newRouteScroll;

        // The values of the new route, as the fields show them.
        private int newRouteKind;
        private int? newRouteAspect;
        private int? newRouteDistant;
        private int? newRouteNext;
        private double? newRouteSpeed;
        private bool newRouteDistantEdited;
        private bool newRouteNextEdited;

        // The ways found, the one chosen, and what they were found for.
        private OmsiSignalRouteSearch newRouteSearch;
        private SignalRouteEditStamp newRouteSearchStamp;
        private int newRouteWay;
        private bool newRouteSearching;
        private string newRouteFailure;
        private bool newRouteNoInstallation;
        private bool newRouteFitWhenFound;
        private int newRouteGeneration;

        // The rails and the signals of the map for one state of the working copy.
        private OmsiPathNetwork newRouteRails;
        private List<OmsiSignalPose> newRouteSignals;
        private SignalRouteEditStamp newRouteRailsStamp;
        private OmsiSignalRouteMap newRouteElements;
        private SignalRouteEditStamp newRouteElementsStamp;

        private bool PlanningSignalRoute => newRouteActive;

        private bool NewRouteStampCurrent(SignalRouteEditStamp stamp)
        {
            var now = CurrentSignalRouteStamp();
            return stamp.Session != null && ReferenceEquals(stamp.Session, now.Session) && string.Equals(stamp.Map, now.Map, StringComparison.OrdinalIgnoreCase) &&
                   stamp.WriteCount == now.WriteCount;
        }

        /// <summary>True when the file of the object (or row) is a signal; false when it is not or cannot be told.</summary>
        private bool IsSignalFile(string assetPath)
        {
            var isSignal = session?.IsSignalAsset;
            return isSignal != null && !string.IsNullOrEmpty(assetPath) && isSignal(assetPath) == true;
        }

        private OmsiSignalRouteWay SelectedNewRouteWay()
        {
            return newRouteSearch != null && newRouteWay >= 0 && newRouteWay < newRouteSearch.Ways.Count ? newRouteSearch.Ways[newRouteWay] : null;
        }

        // --- starting and leaving the plan ---

        /// <summary>Opens the plan in the popup, with the signal when given (the menu of a signal) or waiting for the pipette.</summary>
        private void BeginNewSignalRoute(int? signalId)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.signal_routes"));
                return;
            }
            if (session.IsSignalAsset == null)
            {
                SetStatusT("sigroute.new.status.no_installation");
                return;
            }
            if (!SignalToolMayYield(SignalExitKind.Action, () => BeginNewSignalRoute(signalId)))
                return;
            EndVarParentPick(false);
            EndSignalRoutePick(false);
            StopPlacement();
            if (activeTool != EditorTool.Drag)
                SetTool(EditorTool.Drag);
            if (activeTool == EditorTool.Signals)
                return;

            CancelNewSignalRoute(false);
            signalRouteSelected = -1;
            signalRouteShape = null;
            signalRouteShapeTask = null;
            signalRouteShapeGeneration++;
            signalRouteFitWhenShaped = false;
            ShowProjectTab(ProjectTab.SignalRoutes);

            newRouteActive = true;
            newRouteScroll = 0f;
            newRoutePlanId++;
            newRouteSignalId = null;
            newRouteSignalFile = null;
            newRouteTargetMode = NewRouteTargetMode.NextSignal;
            newRouteTargetSignalId = null;
            newRouteHasTrackTarget = false;
            newRouteStart = NewRouteStart.Automatic;
            newRouteOverlap = 1;
            newRouteKind = 0;
            newRouteAspect = null;
            newRouteDistant = null;
            newRouteNext = null;
            newRouteSpeed = null;
            newRouteDistantEdited = false;
            newRouteNextEdited = false;
            newRouteSearch = null;
            newRouteWay = 0;
            newRouteFailure = null;
            newRouteNoInstallation = false;
            FillSignalRouteBody();
            ApplyNewRouteHighlight();
            if (signalId.HasValue)
                SetNewRouteSignal(signalId.Value);
            else
            {
                // The rails and the signals are read while the user looks for the signal.
                StartNewRouteWork(null);
                StartNewRoutePick(NewRoutePickKind.Signal);
            }
        }

        /// <summary>Drops the plan: nothing was written. Called by Esc, "Cancel", a new map and the popup closing.</summary>
        private void CancelNewSignalRoute(bool announce)
        {
            if (!newRouteActive)
                return;
            EndNewRoutePick(false);
            newRouteActive = false;
            newRouteGeneration++;
            newRouteSearching = false;
            newRouteSearch = null;
            newRouteRails = null;
            newRouteSignals = null;
            newRouteElements = null;
            mapCanvas?.SetSignalRouteHighlight(null, null, null, null, null);
            ApplySignalRouteLayer();
            if (announce)
                SetStatusT("sigroute.new.status.cancelled");
            FillSignalRouteBody();
        }

        /// <summary>Esc: a point waiting to be clicked stops first, the next Esc drops the plan.</summary>
        private void EscapeSignalRouteNew()
        {
            if (newRoutePick != NewRoutePickKind.None)
                EndNewRoutePick(true);
            else
                CancelNewSignalRoute(true);
        }

        private void RefreshNewRoutePanelSoon()
        {
            var plan = newRoutePlanId;
            mapCanvas?.schedule.Execute(() =>
            {
                if (newRouteActive && plan == newRoutePlanId)
                    FillSignalRouteBody();
            }).StartingIn(1);
        }

        // --- the choices ---

        private void SetNewRouteSignal(int id)
        {
            newRouteSignalId = id;
            var place = NewRouteElement(id);
            newRouteSignalFile = place != null && !string.IsNullOrEmpty(place.AssetPath) ? System.IO.Path.GetFileName(place.AssetPath.Replace('\\', '/')) : null;
            var file = SignalRoutesFile();
            newRouteKind = file != null ? OmsiSignalRouteDefaults.KindFor(file, id) : 0;
            newRouteDistant = file != null ? OmsiSignalRouteDefaults.DistantSignalFor(file, id) : null;
            newRouteDistantEdited = false;
            newRouteNextEdited = false;
            newRouteNext = null;
            newRouteFitWhenFound = true;
            RunNewRouteSearch();
        }

        private void SetNewRouteTargetMode(NewRouteTargetMode mode)
        {
            if (newRouteTargetMode == mode)
                return;
            EndNewRoutePick(false);
            newRouteTargetMode = mode;
            newRouteNextEdited = false;
            newRouteFitWhenFound = true;
            RunNewRouteSearch();
        }

        private void SetNewRouteStart(NewRouteStart start)
        {
            if (newRouteStart == start)
                return;
            newRouteStart = start;
            RunNewRouteSearch();
        }

        private void SetNewRouteOverlap(int pieces)
        {
            pieces = Mathf.Clamp(pieces, 0, NewRouteMaximumOverlap);
            if (newRouteOverlap == pieces)
                return;
            newRouteOverlap = pieces;
            RunNewRouteSearch();
        }

        private void ChooseNewRouteWay(int index)
        {
            if (newRouteSearch == null || index < 0 || index >= newRouteSearch.Ways.Count)
                return;
            newRouteWay = index;
            if (!newRouteNextEdited)
                newRouteNext = newRouteSearch.Ways[index].EndSignalId;
            FillSignalRouteBody();
            ApplyNewRouteHighlight();
        }

        private OmsiMapElementPlace NewRouteElement(int id)
        {
            if (currentSummary == null)
                return null;
            var stamp = CurrentSignalRouteStamp();
            if (newRouteElements == null || !NewRouteStampCurrent(newRouteElementsStamp))
            {
                newRouteElements = OmsiSignalRouteMap.ElementsOf(currentSummary);
                newRouteElementsStamp = stamp;
            }
            return newRouteElements.FindElement(id);
        }

        // --- finding the ways ---

        /// <summary>Starts the search when the signal and the end are known; otherwise says what is missing.</summary>
        private void RunNewRouteSearch()
        {
            newRouteSearch = null;
            newRouteWay = 0;
            newRouteFailure = null;
            newRouteNoInstallation = false;
            // The next signal of a way that is not found yet is not known: only a value the user typed or picked stays.
            if (!newRouteNextEdited)
                newRouteNext = null;
            var target = NewRouteTarget();
            if (!newRouteSignalId.HasValue || target == null)
            {
                newRouteSearching = false;
                newRouteGeneration++;
                FillSignalRouteBody();
                ApplyNewRouteHighlight();
                // Nothing to search, but the pipette needs the signals of this state of the map: when the read before was dropped (the map changed
                // meanwhile) or has not happened, it is made again.
                if (newRouteFailure == null && !newRouteNoInstallation && (newRouteSignals == null || !NewRouteStampCurrent(newRouteRailsStamp)))
                    StartNewRouteWork(null);
                if (newRouteSignalId.HasValue && newRoutePick == NewRoutePickKind.None)
                    StartNewRoutePick(NewRoutePickKind.Target);
                return;
            }
            StartNewRouteWork(target);
            FillSignalRouteBody();
            ApplyNewRouteHighlight();
        }

        private OmsiSignalRouteTarget NewRouteTarget()
        {
            switch (newRouteTargetMode)
            {
                case NewRouteTargetMode.NextSignal:
                    return OmsiSignalRouteTarget.NextSignal();
                case NewRouteTargetMode.Signal:
                    return newRouteTargetSignalId.HasValue ? OmsiSignalRouteTarget.Signal(newRouteTargetSignalId.Value) : null;
                default:
                    return newRouteHasTrackTarget ? OmsiSignalRouteTarget.TrackAt(newRouteTrackTileX, newRouteTrackTileY, newRouteTrackElement, newRouteTrackPath) : null;
            }
        }

        /// <summary>
        /// Reads the rails and the signals of the map when they are not at hand for this state of the working copy and, with a target, searches. The worker thread
        /// touches no Unity object; a result for an older plan or an older map is dropped.
        /// </summary>
        private void StartNewRouteWork(OmsiSignalRouteTarget target)
        {
            if (!newRouteActive || session == null || currentSummary == null || mapCanvas == null)
                return;
            var generation = ++newRouteGeneration;
            var plan = newRoutePlanId;
            var stamp = CurrentSignalRouteStamp();
            var work = session;
            var summary = currentSummary;
            var signalId = newRouteSignalId;
            var options = new OmsiSignalRouteSearchOptions
            {
                StartOnSignalTrack = newRouteStart == NewRouteStart.Automatic ? (bool?)null : newRouteStart == NewRouteStart.AtSignal,
                OverlapPieces = newRouteOverlap
            };
            // The session reads the signal files lazily on first use; that must not happen on two threads at once.
            var isSignalAsset = work.IsSignalAsset;
            var railsKnown = newRouteRails != null && NewRouteStampCurrent(newRouteRailsStamp) ? newRouteRails : null;
            var signalsKnown = railsKnown != null ? newRouteSignals : null;
            var withRoutes = target != null && target.Kind == OmsiSignalRouteTargetKind.NextSignal ? work.SignalIdsWithRoutes() : null;
            newRouteSearching = target != null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var task = Task.Run(() =>
            {
                var result = new NewRouteWork();
                try
                {
                    var rails = railsKnown ?? work.SignalRouteRails(summary);
                    if (rails == null || isSignalAsset == null)
                    {
                        result.NoInstallation = true;
                        return result;
                    }
                    result.Rails = rails;
                    result.Signals = signalsKnown ?? OmsiSignalRouteFinder.SignalPoses(summary, isSignalAsset);
                    if (target != null && signalId.HasValue)
                        result.Search = work.FindSignalRouteWays(signalId.Value, target, summary, rails, options, withRoutes);
                }
                catch (Exception exception)
                {
                    result.Failure = exception;
                }
                return result;
            });
            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                var result = task.IsFaulted ? new NewRouteWork { Failure = task.Exception?.GetBaseException() } : task.Result;
                // The rails and signals of a state of the map that is still the one shown serve the next search, whatever plan asked.
                if (result.Rails != null && NewRouteStampCurrent(stamp))
                {
                    newRouteRails = result.Rails;
                    newRouteSignals = result.Signals;
                    newRouteRailsStamp = stamp;
                }
                if (generation != newRouteGeneration || !newRouteActive || plan != newRoutePlanId)
                    return;
                newRouteSearching = false;
                if (!NewRouteStampCurrent(stamp))
                {
                    // The map changed while the track was read: read it again.
                    RunNewRouteSearch();
                    return;
                }
                newRouteNoInstallation = result.NoInstallation;
                if (result.Failure != null)
                {
                    Debug.LogWarning("New signal route: the search failed: " + result.Failure.Message);
                    newRouteFailure = result.Failure.Message;
                }
                // Without the signals the pipette cannot work: the problem is shown in the plan and the pipette ends.
                if (result.Failure != null || result.NoInstallation)
                    EndNewRoutePick(false);
                newRouteSearch = result.Search;
                newRouteSearchStamp = stamp;
                newRouteWay = 0;
                var first = SelectedNewRouteWay();
                if (!newRouteNextEdited)
                    newRouteNext = first?.EndSignalId;
                if (result.Search != null)
                    Debug.Log("New signal route: signal " + signalId + ", " + result.Search.Ways.Count + " ways" + (result.Search.Found ? ", the first " +
                              first.Entries.Count + " pieces " + first.LengthMeters.ToString("0", CultureInfo.InvariantCulture) + " m ending at " +
                              (first.EndSignalId?.ToString(CultureInfo.InvariantCulture) ?? "-") + ", overlap " + first.OverlapEntries : ", " + result.Search.Problem + ": " +
                              result.Search.Message) + ", " + watch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms" +
                              (railsKnown != null ? " (rails at hand)." : "."));
                FillSignalRouteBody();
                ApplyNewRouteHighlight();
                if (result.Search != null && result.Search.Found)
                {
                    // With -view in a test run the view stays where it was put.
                    if (newRouteFitWhenFound && signalRouteArgFit)
                        FitNewRoute();
                    SetStatusT("sigroute.new.status.found", ("count", result.Search.Ways.Count), ("length", Math.Round(first.RouteLengthMeters)));
                }
                else if (result.Search != null)
                    SetStatus(NewRouteProblemText(result.Search));
                newRouteFitWhenFound = false;
            }).Every(50);
        }

        private void FitNewRoute()
        {
            var way = SelectedNewRouteWay();
            if (way == null || mapCanvas == null)
                return;
            var shape = new OmsiSignalRouteShape();
            foreach (var lane in way.Lanes)
                shape.Lines.Add(lane.Geometry);
            var places = new List<OmsiMapPoint>();
            if (newRouteSignalId.HasValue)
            {
                var signal = NewRouteElement(newRouteSignalId.Value);
                if (signal != null)
                    places.Add(signal.World);
            }
            if (OmsiSignalRouteList.Bounds(shape, places, out var min, out var max))
                ShowSignalRouteBox(min, max);
        }

        // --- the map ---

        /// <summary>The way chosen in the accent colour, the others thin and dim, and the signal, the distant signal and the next signal by their shapes.</summary>
        private void ApplyNewRouteHighlight()
        {
            if (mapCanvas == null || !newRouteActive || currentSummary == null)
                return;
            var way = SelectedNewRouteWay();
            OmsiMapPoint? World(int? id)
            {
                var place = id.HasValue ? NewRouteElement(id.Value) : null;
                return place != null ? place.World : (OmsiMapPoint?)null;
            }

            mapCanvas.SetSignalRouteHighlight(way?.Lanes.Select(lane => lane.Geometry).ToList(), null, World(newRouteSignalId), World(newRouteDistant), World(newRouteNext));
            if (newRouteSearch != null && newRouteSearch.Ways.Count > 1)
            {
                var others = new List<OmsiMapPoint[]>();
                for (var index = 0; index < newRouteSearch.Ways.Count; index++)
                    if (index != newRouteWay)
                        foreach (var lane in newRouteSearch.Ways[index].Lanes)
                            others.Add(lane.Geometry);
                mapCanvas.SetSignalRouteAlternatives(others);
            }
            else
                mapCanvas.SetSignalRouteAlternatives(null);
        }

        // --- the pipette on the map ---

        private void StartNewRoutePick(NewRoutePickKind kind)
        {
            if (!newRouteActive || mapCanvas == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.signal_routes"));
                return;
            }
            if (newRoutePick == kind)
            {
                EndNewRoutePick(true);
                return;
            }
            // The pipette of the aerial image placement shares the map's one click: only one of the two waits.
            if (placementPickFor >= 0)
                EndPlacementPick();
            if (newRoutePick == NewRoutePickKind.None)
                mapCanvas.BeginPointPick();
            newRoutePick = kind;
            switch (kind)
            {
                case NewRoutePickKind.Signal:
                    SetStatusT("sigroute.new.status.pick_signal");
                    break;
                case NewRoutePickKind.Target:
                    SetStatusT("sigroute.new.status.pick_target");
                    break;
                case NewRoutePickKind.Distant:
                    SetStatusT("sigroute.new.status.pick_distant");
                    break;
                default:
                    SetStatusT("sigroute.new.status.pick_next");
                    break;
            }
            RefreshNewRoutePanelSoon();
        }

        private void EndNewRoutePick(bool announce)
        {
            if (newRoutePick == NewRoutePickKind.None)
                return;
            newRoutePick = NewRoutePickKind.None;
            mapCanvas?.EndPointPick();
            if (announce)
                SetStatusT("sigroute.new.status.pick_ended");
            RefreshNewRoutePanelSoon();
        }

        /// <summary>A click while a point waits for the pipette: the signal or rail nearest to it; nothing there keeps the pipette on.</summary>
        private void NewRoutePointPicked(double x, double y)
        {
            if (!newRouteActive || newRoutePick == NewRoutePickKind.None || mapCanvas == null)
                return;
            var kind = newRoutePick;
            var point = new OmsiMapPoint(x, y);
            var reach = mapCanvas.ThreeDimensional ? 6.0 : Math.Max(3.0, 12.0 * mapCanvas.MetersPerPixel);
            if (newRouteSignals == null)
            {
                SetStatusT("sigroute.new.status.signals_reading");
                return;
            }
            OmsiSignalPose nearest = null;
            var best = double.MaxValue;
            foreach (var pose in newRouteSignals)
            {
                var distance = Math.Sqrt((pose.World.X - x) * (pose.World.X - x) + (pose.World.Y - y) * (pose.World.Y - y));
                if (distance <= reach && distance < best)
                {
                    best = distance;
                    nearest = pose;
                }
            }
            DoNewRoutePick(kind, point, nearest, reach);
        }

        private void DoNewRoutePick(NewRoutePickKind kind, OmsiMapPoint point, OmsiSignalPose signal, double reach)
        {
            if (kind == NewRoutePickKind.Target && signal == null)
            {
                if (newRouteRails == null)
                {
                    SetStatusT("sigroute.new.status.rails_reading");
                    return;
                }
                var lane = OmsiSignalRouteFinder.NearestLane(newRouteRails, point, reach);
                if (lane == null)
                {
                    SetStatusT("sigroute.new.status.no_target_there");
                    return;
                }
                EndNewRoutePick(false);
                newRouteTargetMode = NewRouteTargetMode.Track;
                newRouteHasTrackTarget = true;
                newRouteTrackTileX = lane.Path.TileX;
                newRouteTrackTileY = lane.Path.TileY;
                newRouteTrackElement = lane.Path.ElementId;
                newRouteTrackPath = lane.Path.PathIndex;
                newRouteNextEdited = false;
                newRouteFitWhenFound = true;
                RunNewRouteSearch();
                return;
            }
            if (signal == null)
            {
                SetStatusT(kind == NewRoutePickKind.Target ? "sigroute.new.status.no_target_there" : "sigroute.new.status.no_signal_there"); // i18n:keys
                return;
            }
            EndNewRoutePick(false);
            switch (kind)
            {
                case NewRoutePickKind.Signal:
                    SetNewRouteSignal(signal.Id);
                    break;
                case NewRoutePickKind.Target:
                    newRouteTargetMode = NewRouteTargetMode.Signal;
                    newRouteTargetSignalId = signal.Id;
                    newRouteNextEdited = false;
                    newRouteFitWhenFound = true;
                    RunNewRouteSearch();
                    break;
                case NewRoutePickKind.Distant:
                    newRouteDistant = signal.Id;
                    newRouteDistantEdited = true;
                    FillSignalRouteBody();
                    ApplyNewRouteHighlight();
                    break;
                default:
                    newRouteNext = signal.Id;
                    newRouteNextEdited = true;
                    FillSignalRouteBody();
                    ApplyNewRouteHighlight();
                    break;
            }
        }

        // --- the panel ---

        private static string NewRouteProblemKey(OmsiSignalRouteSearchProblem problem)
        {
            switch (problem)
            {
                case OmsiSignalRouteSearchProblem.UnknownSignal: return "sigroute.new.problem.unknown_signal";
                case OmsiSignalRouteSearchProblem.NoTrackAtSignal: return "sigroute.new.problem.no_track_at_signal";
                case OmsiSignalRouteSearchProblem.UnknownTarget: return "sigroute.new.problem.unknown_target";
                case OmsiSignalRouteSearchProblem.TargetHasNoTrack: return "sigroute.new.problem.target_no_track";
                case OmsiSignalRouteSearchProblem.TooFar: return "sigroute.new.problem.too_far";
                case OmsiSignalRouteSearchProblem.StepLimit: return "sigroute.new.problem.step_limit";
                default: return "sigroute.new.problem.no_way";
            }
        }

        /// <summary>Why the search found no way, in the language of the interface.</summary>
        private string NewRouteProblemText(OmsiSignalRouteSearch search)
        {
            var defaults = new OmsiSignalRouteSearchOptions();
            return Loc.T(NewRouteProblemKey(search.Problem), ("id", newRouteSignalId ?? 0), ("meters", Math.Round(defaults.SignalReachMeters)), // i18n:keys
                ("length", Math.Round(defaults.MaximumLengthMeters)), ("pieces", defaults.MaximumEntries));
        }

        private VisualElement NewRoutePanel()
        {
            // The plan scrolls; "Create route" and "Cancel" stand below it and stay in view.
            var frame = new VisualElement { name = "new-route-panel" };
            frame.AddToClassList("sigroute-new");
            var panel = new ScrollView(ScrollViewMode.Vertical);
            panel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.AddToClassList("sigroute-new-scroll");
            frame.Add(panel);
            // A change redraws the plan; the place in it is kept.
            panel.verticalScroller.valueChanged += value => newRouteScroll = value;
            var offset = newRouteScroll;
            panel.schedule.Execute(() => panel.scrollOffset = new Vector2(0f, offset)).StartingIn(30);
            var plan = newRoutePlanId;

            var title = LocBindings.NewLabel("sigroute.new.title");
            title.AddToClassList("sigroute-detail-title");
            panel.Add(title);

            // The signal.
            var signalRow = OptionControls.Row(panel, "sigroute.new.signal", true, true);
            signalRow.AddToClassList("sigroute-edit-row");
            var signalText = new Label(newRouteSignalId.HasValue
                ? newRouteSignalFile != null ? Loc.T("sigroute.row.signal", ("id", newRouteSignalId.Value), ("file", newRouteSignalFile))
                    : Loc.T("sigroute.row.signal_unknown", ("id", newRouteSignalId.Value))
                : Loc.T("sigroute.new.not_chosen"));
            signalText.name = "new-route-signal";
            signalText.AddToClassList("sigroute-new-value");
            signalRow.Add(signalText);
            var pickSignal = OptionControls.Choice(signalRow, "pipette", "sigroute.new.signal", "sigroute.new.signal.pick.tooltip", () => StartNewRoutePick(NewRoutePickKind.Signal));
            pickSignal.name = "new-route-pick-signal";
            pickSignal.EnableInClassList("panel-tab-active", newRoutePick == NewRoutePickKind.Signal);

            // The end.
            var targetRow = OptionControls.Row(panel, "sigroute.new.target", true, true);
            targetRow.AddToClassList("sigroute-aspect-row");
            var targetChoices = new VisualElement();
            targetChoices.AddToClassList("sigroute-new-choices");
            var next = OptionControls.TextChoice(targetChoices, "sigroute.new.target.next", "sigroute.new.target.next.tooltip", () => SetNewRouteTargetMode(NewRouteTargetMode.NextSignal));
            var toSignal = OptionControls.TextChoice(targetChoices, "sigroute.new.target.signal", "sigroute.new.target.signal.tooltip", () => SetNewRouteTargetMode(NewRouteTargetMode.Signal));
            var toTrack = OptionControls.TextChoice(targetChoices, "sigroute.new.target.track", "sigroute.new.target.track.tooltip", () => SetNewRouteTargetMode(NewRouteTargetMode.Track));
            next.name = "new-route-target-next";
            toSignal.name = "new-route-target-signal";
            toTrack.name = "new-route-target-track";
            next.EnableInClassList("panel-tab-active", newRouteTargetMode == NewRouteTargetMode.NextSignal);
            toSignal.EnableInClassList("panel-tab-active", newRouteTargetMode == NewRouteTargetMode.Signal);
            toTrack.EnableInClassList("panel-tab-active", newRouteTargetMode == NewRouteTargetMode.Track);
            targetRow.Add(targetChoices);
            if (newRouteTargetMode != NewRouteTargetMode.NextSignal)
            {
                var chosenRow = OptionControls.Row(panel, "sigroute.new.target.value", true, true);
                chosenRow.AddToClassList("sigroute-edit-row");
                string chosen;
                if (newRouteTargetMode == NewRouteTargetMode.Signal && newRouteTargetSignalId.HasValue)
                    chosen = Loc.T("sigroute.new.target.signal_value", ("id", newRouteTargetSignalId.Value));
                else if (newRouteTargetMode == NewRouteTargetMode.Track && newRouteHasTrackTarget)
                    chosen = Loc.T("sigroute.new.target.track_value", ("element", newRouteTrackElement), ("path", newRouteTrackPath));
                else
                    chosen = Loc.T("sigroute.new.not_chosen");
                var chosenText = new Label(chosen) { name = "new-route-target" };
                chosenText.AddToClassList("sigroute-new-value");
                chosenRow.Add(chosenText);
                var pickTarget = OptionControls.Choice(chosenRow, "pipette", "sigroute.new.target.value", "sigroute.new.target.pick.tooltip", () => StartNewRoutePick(NewRoutePickKind.Target));
                pickTarget.name = "new-route-pick-target";
                pickTarget.EnableInClassList("panel-tab-active", newRoutePick == NewRoutePickKind.Target);
            }

            // Where it starts.
            var startRow = OptionControls.Row(panel, "sigroute.new.start", true, true);
            startRow.AddToClassList("sigroute-aspect-row");
            var startChoices = new VisualElement();
            startChoices.AddToClassList("sigroute-new-choices");
            var auto = OptionControls.TextChoice(startChoices, "sigroute.new.start.auto", "sigroute.new.start.auto.tooltip", () => SetNewRouteStart(NewRouteStart.Automatic));
            var at = OptionControls.TextChoice(startChoices, "sigroute.new.start.at", "sigroute.new.start.at.tooltip", () => SetNewRouteStart(NewRouteStart.AtSignal));
            var after = OptionControls.TextChoice(startChoices, "sigroute.new.start.after", "sigroute.new.start.after.tooltip", () => SetNewRouteStart(NewRouteStart.AfterSignal));
            auto.name = "new-route-start-auto";
            at.name = "new-route-start-at";
            after.name = "new-route-start-after";
            auto.EnableInClassList("panel-tab-active", newRouteStart == NewRouteStart.Automatic);
            at.EnableInClassList("panel-tab-active", newRouteStart == NewRouteStart.AtSignal);
            after.EnableInClassList("panel-tab-active", newRouteStart == NewRouteStart.AfterSignal);
            startRow.Add(startChoices);
            if (newRouteSearch != null && newRouteSearch.SignalTrack != null)
            {
                var startNote = LocBindings.NewLabel(newRouteSearch.StartsOnSignalTrack ? "sigroute.new.start.note_at" : "sigroute.new.start.note_after"); // i18n:keys
                startNote.AddToClassList("sigroute-edit-note");
                panel.Add(startNote);
            }

            // The overlap beyond the target signal.
            var overlapRow = OptionControls.Row(panel, "sigroute.new.overlap", true, false);
            var overlapGroup = OptionControls.Group(overlapRow);
            var trackTarget = newRouteTargetMode == NewRouteTargetMode.Track;
            for (var pieces = 0; pieces <= NewRouteMaximumOverlap; pieces++)
            {
                var count = pieces;
                var choice = OptionControls.TextChoice(overlapGroup, null, "sigroute.new.overlap.tooltip", () => SetNewRouteOverlap(count));
                choice.text = count.ToString(CultureInfo.InvariantCulture);
                choice.name = "new-route-overlap-" + count;
                choice.EnableInClassList("panel-tab-active", !trackTarget && newRouteOverlap == count);
                choice.SetEnabled(!trackTarget);
            }

            // The ways.
            OptionControls.Heading(panel, "sigroute.new.ways");
            AddNewRouteWays(panel);

            // The values.
            AddNewRouteFields(panel, plan);

            var buttons = new VisualElement();
            buttons.AddToClassList("sigroute-new-buttons");
            var create = OptionControls.TextChoice(buttons, "sigroute.new.create", "sigroute.new.create.tooltip", CreateNewSignalRoute);
            create.AddToClassList("option-icon-primary");
            create.name = "new-route-create";
            create.SetEnabled(SelectedNewRouteWay() != null && newRouteSignalId.HasValue);
            var cancel = OptionControls.TextChoice(buttons, "common.cancel", null, () => CancelNewSignalRoute(true));
            cancel.name = "new-route-cancel";
            frame.Add(buttons);
            return frame;
        }

        private void AddNewRouteWays(VisualElement panel)
        {
            // A failed read is said first: without a signal chosen it would otherwise stay silent.
            if (!newRouteSearching && (newRouteNoInstallation || newRouteFailure != null))
            {
                var failed = new Label(newRouteNoInstallation ? Loc.T("sigroute.new.problem.no_installation") : Loc.T("sigroute.new.problem.failed", ("reason", newRouteFailure)));
                failed.name = "new-route-problem";
                failed.AddToClassList("sigroute-problem");
                panel.Add(failed);
                return;
            }
            if (!newRouteSignalId.HasValue)
            {
                var hint = LocBindings.NewLabel("sigroute.new.hint.signal");
                hint.AddToClassList("sigroute-edit-note");
                panel.Add(hint);
                return;
            }
            if (newRouteSearching)
            {
                var searching = LocBindings.NewLabel("sigroute.new.searching");
                searching.AddToClassList("sigroute-edit-note");
                panel.Add(searching);
                return;
            }
            if (newRouteSearch == null)
            {
                var hint = LocBindings.NewLabel("sigroute.new.hint.target");
                hint.AddToClassList("sigroute-edit-note");
                panel.Add(hint);
                return;
            }
            if (!newRouteSearch.Found)
            {
                var problem = new Label(NewRouteProblemText(newRouteSearch)) { name = "new-route-problem" };
                problem.AddToClassList("sigroute-problem");
                panel.Add(problem);
                return;
            }
            for (var index = 0; index < newRouteSearch.Ways.Count; index++)
            {
                var way = newRouteSearch.Ways[index];
                var number = index;
                var card = new Button(() => ChooseNewRouteWay(number)) { name = "new-route-way-" + (index + 1) };
                card.AddToClassList("sigroute-card");
                card.EnableInClassList("sigroute-card-selected", index == newRouteWay);
                LocBindings.Tooltip(card, "sigroute.new.way.tooltip");
                var top = new VisualElement { pickingMode = PickingMode.Ignore };
                top.AddToClassList("sigroute-top");
                // Length and pieces up to the target, as the ways are sorted; the overlap beyond it is told apart.
                var title = way.OverlapEntries > 0
                    ? Loc.T("sigroute.new.way.overlap", ("number", index + 1), ("length", Math.Round(way.RouteLengthMeters)), ("count", way.RouteEntryCount),
                        ("overlap", way.OverlapEntries))
                    : Loc.T("sigroute.new.way", ("number", index + 1), ("length", Math.Round(way.RouteLengthMeters)), ("count", way.RouteEntryCount));
                var text = new Label(title) { pickingMode = PickingMode.Ignore };
                text.AddToClassList("sigroute-title");
                top.Add(text);
                card.Add(top);
                if (way.EndSignalId.HasValue)
                {
                    var end = new Label(Loc.T("sigroute.new.way.end", ("id", way.EndSignalId.Value))) { pickingMode = PickingMode.Ignore };
                    end.AddToClassList("sigroute-detail");
                    end.AddToClassList("sigroute-way-detail");
                    card.Add(end);
                }
                panel.Add(card);
            }
            if (newRouteSearch.MoreWaysPossible)
            {
                var more = LocBindings.NewLabel("sigroute.new.ways.more");
                more.AddToClassList("sigroute-edit-note");
                panel.Add(more);
            }
        }

        // --- the values ---

        private void AddNewRouteFields(VisualElement panel, int plan)
        {
            var culture = CultureInfo.InvariantCulture;
            var editor = new VisualElement { name = "new-route-fields" };
            editor.AddToClassList("sigroute-edit");

            var kindRow = OptionControls.Row(editor, "sigroute.edit.kind", true, false);
            var kind = new TextField { value = newRouteKind.ToString(culture), isDelayed = true, name = "new-route-kind" };
            kind.RegisterValueChangedCallback(change =>
            {
                if (plan != newRoutePlanId || !newRouteActive)
                    return;
                if (!int.TryParse((change.newValue ?? string.Empty).Trim(), NumberStyles.Integer, culture, out var value) || value < 0)
                {
                    SetStatusT("sigroute.status.bad_kind");
                    kind.SetValueWithoutNotify(newRouteKind.ToString(culture));
                    return;
                }
                newRouteKind = value;
            });
            OptionControls.ValueBox(kindRow, "hash", kind, null, "sigroute.edit.kind", "sigroute.edit.kind.tooltip");
            kind.AddToClassList("sigroute-edit-field");

            var aspectRow = OptionControls.Row(editor, "sigroute.edit.aspect", true, true);
            aspectRow.AddToClassList("sigroute-aspect-row");
            var choices = new VisualElement();
            choices.AddToClassList("sigroute-new-choices");
            var automatic = OptionControls.TextChoice(choices, "sigroute.new.aspect.auto", "sigroute.new.aspect.auto.tooltip", () => SetNewRouteAspect(null));
            var clear = OptionControls.TextChoice(choices, "sigroute.aspect.clear", "sigroute.edit.aspect.tooltip", () => SetNewRouteAspect(1));
            var reduced = OptionControls.TextChoice(choices, "sigroute.aspect.reduced", "sigroute.edit.aspect.tooltip", () => SetNewRouteAspect(2));
            automatic.name = "new-route-aspect-auto";
            clear.name = "new-route-aspect-clear";
            reduced.name = "new-route-aspect-reduced";
            automatic.EnableInClassList("panel-tab-active", !newRouteAspect.HasValue);
            clear.EnableInClassList("panel-tab-active", newRouteAspect == 1);
            reduced.EnableInClassList("panel-tab-active", newRouteAspect == 2);
            aspectRow.Add(choices);

            AddNewRouteSignalRow(editor, plan, NewRoutePickKind.Distant);
            AddNewRouteSignalRow(editor, plan, NewRoutePickKind.Next);

            var speedRow = OptionControls.Row(editor, "sigroute.edit.speed", true, false);
            var speed = new TextField { value = newRouteSpeed.HasValue ? newRouteSpeed.Value.ToString("0.###", culture) : string.Empty, isDelayed = true, name = "new-route-speed" };
            speed.RegisterValueChangedCallback(change =>
            {
                if (plan != newRoutePlanId || !newRouteActive)
                    return;
                var text = (change.newValue ?? string.Empty).Trim().Replace(',', '.');
                if (text.Length == 0)
                {
                    newRouteSpeed = null;
                    return;
                }
                if (!double.TryParse(text, NumberStyles.Float, culture, out var value) || value <= 0.0 || value > OmsiMapSession.MaximumSignalRouteSpeed)
                {
                    SetStatusT("sigroute.status.bad_speed");
                    speed.SetValueWithoutNotify(newRouteSpeed.HasValue ? newRouteSpeed.Value.ToString("0.###", culture) : string.Empty);
                    return;
                }
                newRouteSpeed = value;
            });
            OptionControls.ValueBox(speedRow, "gauge", speed, Loc.T("sigroute.edit.unit"), "sigroute.edit.speed", "sigroute.edit.speed.tooltip");
            speed.AddToClassList("sigroute-edit-field");
            panel.Add(editor);
        }

        private void SetNewRouteAspect(int? aspect)
        {
            newRouteAspect = aspect;
            FillSignalRouteBody();
        }

        private void AddNewRouteSignalRow(VisualElement editor, int plan, NewRoutePickKind role)
        {
            var distant = role == NewRoutePickKind.Distant;
            var id = distant ? newRouteDistant : newRouteNext;
            var nameKey = distant ? "sigroute.edit.distant" : "sigroute.edit.next";
            var row = OptionControls.Row(editor, nameKey, true, true);
            row.AddToClassList("sigroute-edit-row");
            var field = new TextField { value = id.HasValue ? id.Value.ToString(CultureInfo.InvariantCulture) : string.Empty, isDelayed = true, name = distant ? "new-route-distant" : "new-route-next" };
            field.RegisterValueChangedCallback(change =>
            {
                if (plan != newRoutePlanId || !newRouteActive)
                    return;
                var text = (change.newValue ?? string.Empty).Trim();
                int? value = null;
                if (text.Length > 0)
                {
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                    {
                        SetStatusT("sigroute.status.bad_id");
                        field.SetValueWithoutNotify((distant ? newRouteDistant : newRouteNext)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                        return;
                    }
                    value = parsed;
                }
                SetNewRouteSignalValue(role, value);
            });
            OptionControls.ValueBox(row, "hash", field, null, nameKey, distant ? "sigroute.new.distant.tooltip" : "sigroute.new.next.tooltip"); // i18n:keys
            field.AddToClassList("sigroute-edit-field");
            var pick = OptionControls.Choice(row, "pipette", nameKey, "sigroute.edit.pick.tooltip", () => StartNewRoutePick(role));
            pick.name = distant ? "new-route-pick-distant" : "new-route-pick-next";
            pick.EnableInClassList("panel-tab-active", newRoutePick == role);
            if (id.HasValue)
            {
                var remove = OptionControls.AsTextChoice(LocBindings.WithTooltip(LocBindings.NewButton("sigroute.edit.remove", () => SetNewRouteSignalValue(role, null)), "sigroute.edit.remove.tooltip")); // i18n:keys
                remove.name = distant ? "new-route-remove-distant" : "new-route-remove-next";
                row.Add(remove);
            }
        }

        private void SetNewRouteSignalValue(NewRoutePickKind role, int? value)
        {
            if (role == NewRoutePickKind.Distant)
            {
                newRouteDistant = value;
                newRouteDistantEdited = true;
            }
            else
            {
                newRouteNext = value;
                newRouteNextEdited = true;
            }
            RefreshNewRoutePanelSoon();
            ApplyNewRouteHighlight();
        }

        // --- creating ---

        /// <summary>Writes the route as one undoable step; the new route is chosen afterwards. Whatever the session refuses is said in the status bar and the plan stays.</summary>
        private void CreateNewSignalRoute()
        {
            if (!newRouteActive)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.signal_routes"));
                return;
            }
            var way = SelectedNewRouteWay();
            if (way == null || !newRouteSignalId.HasValue)
                return;
            if (!NewRouteStampCurrent(newRouteSearchStamp))
            {
                // The ways were found for a map that has changed since (Undo, another tool): they are found again and the user looks at them first.
                SetStatusT("sigroute.new.status.stale");
                RunNewRouteSearch();
                return;
            }
            var signalId = newRouteSignalId.Value;
            var draft = new OmsiSignalRouteDraft
            {
                Kind = newRouteKind, Aspect = newRouteAspect, DistantSignal = newRouteDistant, NextSignal = newRouteNext, SpeedLimit = newRouteSpeed
            };
            draft.Entries.AddRange(way.Entries);
            OmsiSignalRouteChange change;
            try
            {
                change = session.CreateSignalRoute(signalId, draft, currentSummary, NewRouteStampCurrent(newRouteRailsStamp) ? newRouteRails : null);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is System.IO.IOException ||
                                              exception is UnauthorizedAccessException)
            {
                if (exception is ArgumentException)
                    Debug.LogWarning("New signal route not created: " + exception.Message);
                else
                    Debug.LogException(exception);
                SetStatusT("sigroute.new.status.failed", ("reason", exception.Message));
                return;
            }
            // The file is written: the plan is over, so a second click on the button finds nothing to do.
            CancelNewSignalRoute(false);
            var number = SignalRouteNumber(change.RouteIndex);
            PushHistory(new ObjectEditStep { SignalRouteChange = change, Description = Loc.T("sigroute.step.create", ("number", number)) });
            if (signalRouteFilter.HasValue && signalRouteFilter.Value != signalId)
                signalRouteFilter = null;
            // The routes are read again; the new one is chosen when they are (a list that does not hold it yet cannot choose it).
            signalRouteSelected = change.RouteIndex;
            signalRouteSelectAfter = change.RouteIndex;
            AfterSignalRouteWrite();
            SetStatusT("sigroute.new.status.created", ("number", number));
        }

        /// <summary>Shows a box in world metres in the part of the map the popup and the inspector leave free.</summary>
        private void ShowSignalRouteBox(OmsiMapPoint min, OmsiMapPoint max)
        {
            if (mapCanvas == null)
                return;
            // A little room round it, so a route of one signal does not come up to the pixel.
            const double room = 25.0;
            var insets = FreeMapInsets();
            if (barPopup != null && barPopupOpen && mapCanvas.worldBound.width > 0f && barPopup.worldBound.width > 0f)
                insets.z = Mathf.Max(insets.z, mapCanvas.worldBound.xMax - barPopup.worldBound.xMin);
            mapCanvas.ShowWorldBox(new OmsiMapPoint(min.X - room, min.Y - room), new OmsiMapPoint(max.X + room, max.Y + room), insets);
            UpdateZoomLabel();
        }

        // --- command line ---

        /// <summary>
        /// Command line (ADR 105, M4 interface), with the list open: <c>-signalRouteNew id</c> opens the plan for that signal, followed by words that
        /// set it up as the clicks would: <c>next</c>, <c>signal id</c> or <c>track tileX tileY element path</c> for the end, <c>way n</c> (from 1), <c>overlap n</c>,
        /// <c>start auto|at|after</c>, <c>kind n</c>, <c>aspect auto|1|2</c>, <c>speed km/h|none</c>, <c>distant id|none</c>, <c>nextid id|none</c>, then <c>create</c>,
        /// <c>undo</c> and <c>redo</c> if wanted. Without an ID the plan opens waiting for the pipette. "Signal route new:" in the log names the ways, the values and the status.
        /// </summary>
        private IEnumerator RunSignalRouteNewArguments(string[] args)
        {
            var at = Array.IndexOf(args, "-signalRouteNew");
            if (at < 0)
                yield break;
            var culture = CultureInfo.InvariantCulture;
            var words = new List<string>();
            for (var index = at + 1; index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal); index++)
                words.Add(args[index]);
            int? signal = null;
            var first = 0;
            if (words.Count > 0 && int.TryParse(words[0], NumberStyles.Integer, culture, out var parsedSignal))
            {
                signal = parsedSignal;
                first = 1;
            }
            yield return WaitForSignalRoutesRead();
            BeginNewSignalRoute(signal);
            // "touchearly": the working copy changes while the rails and signals are still being read (the read is then dropped and made again).
            if (words.Contains("touchearly"))
                TouchWorkingCopyForTest();
            yield return WaitForNewRoute();
            if (!newRouteActive)
            {
                Debug.Log("Signal route new: the plan did not start, status: " + statusLabel?.text);
                yield break;
            }

            string Word(int index) => index < words.Count ? words[index] : null;
            bool create = false, undo = false, redo = false;
            for (var index = first; index < words.Count; index++)
            {
                switch (words[index].ToLowerInvariant())
                {
                    case "next":
                        SetNewRouteTargetMode(NewRouteTargetMode.NextSignal);
                        break;
                    case "signal":
                        if (int.TryParse(Word(index + 1), NumberStyles.Integer, culture, out var targetSignal))
                        {
                            index++;
                            newRouteTargetMode = NewRouteTargetMode.Signal;
                            newRouteTargetSignalId = targetSignal;
                            newRouteNextEdited = false;
                            newRouteFitWhenFound = true;
                            RunNewRouteSearch();
                        }
                        else
                            SetNewRouteTargetMode(NewRouteTargetMode.Signal); // no ID: the pipette waits for the target
                        break;
                    case "track":
                        if (int.TryParse(Word(index + 1), NumberStyles.Integer, culture, out var tileX) && int.TryParse(Word(index + 2), NumberStyles.Integer, culture, out var tileY) &&
                            int.TryParse(Word(index + 3), NumberStyles.Integer, culture, out var element) && int.TryParse(Word(index + 4), NumberStyles.Integer, culture, out var path))
                        {
                            newRouteTargetMode = NewRouteTargetMode.Track;
                            newRouteHasTrackTarget = true;
                            newRouteTrackTileX = tileX;
                            newRouteTrackTileY = tileY;
                            newRouteTrackElement = element;
                            newRouteTrackPath = path;
                            newRouteNextEdited = false;
                            newRouteFitWhenFound = true;
                            RunNewRouteSearch();
                            index += 4;
                        }
                        else
                            SetNewRouteTargetMode(NewRouteTargetMode.Track);
                        break;
                    case "start":
                        var start = (Word(++index) ?? "auto").ToLowerInvariant();
                        SetNewRouteStart(start == "at" ? NewRouteStart.AtSignal : start == "after" ? NewRouteStart.AfterSignal : NewRouteStart.Automatic);
                        break;
                    case "overlap":
                        if (int.TryParse(Word(++index), NumberStyles.Integer, culture, out var overlap))
                            SetNewRouteOverlap(overlap);
                        break;
                    case "way":
                        yield return WaitForNewRoute();
                        if (int.TryParse(Word(++index), NumberStyles.Integer, culture, out var way))
                            ChooseNewRouteWay(way - 1);
                        break;
                    case "kind":
                        if (int.TryParse(Word(++index), NumberStyles.Integer, culture, out var kind) && kind >= 0)
                            newRouteKind = kind;
                        break;
                    case "aspect":
                        var aspect = Word(++index);
                        newRouteAspect = int.TryParse(aspect, NumberStyles.Integer, culture, out var aspectNumber) ? aspectNumber : (int?)null;
                        break;
                    case "speed":
                        var speedText = Word(++index);
                        newRouteSpeed = double.TryParse(speedText, NumberStyles.Float, culture, out var speedValue) ? speedValue : (double?)null;
                        break;
                    case "distant":
                    case "nextid":
                        var role = words[index].ToLowerInvariant() == "distant" ? NewRoutePickKind.Distant : NewRoutePickKind.Next;
                        var idText = Word(++index);
                        int? idValue = int.TryParse(idText, NumberStyles.Integer, culture, out var idNumber) ? idNumber : (int?)null;
                        if (role == NewRoutePickKind.Distant)
                        {
                            newRouteDistant = idValue;
                            newRouteDistantEdited = true;
                        }
                        else
                        {
                            newRouteNext = idValue;
                            newRouteNextEdited = true;
                        }
                        break;
                    case "pick":
                        // A click at a place in world metres: "pick signal|target x y" does what the pipette would.
                        var kindWord = (Word(++index) ?? string.Empty).ToLowerInvariant();
                        if (double.TryParse(Word(++index), NumberStyles.Float, culture, out var pickX) && double.TryParse(Word(++index), NumberStyles.Float, culture, out var pickY))
                        {
                            var wanted = kindWord == "target" ? NewRoutePickKind.Target : kindWord == "distant" ? NewRoutePickKind.Distant : kindWord == "next" ? NewRoutePickKind.Next : NewRoutePickKind.Signal;
                            if (newRoutePick != wanted)
                                StartNewRoutePick(wanted);
                            yield return WaitForNewRoute();
                            NewRoutePointPicked(pickX, pickY);
                        }
                        break;
                    case "pickid":
                        // The pipette at the place of the signal with this ID (the signals are read by now): "pickid 3212376".
                        yield return WaitForNewRoute();
                        if (int.TryParse(Word(++index), NumberStyles.Integer, culture, out var pickId) && newRouteSignals != null)
                        {
                            var pose = newRouteSignals.Find(candidate => candidate.Id == pickId);
                            if (pose != null)
                            {
                                if (newRoutePick == NewRoutePickKind.None)
                                    StartNewRoutePick(NewRoutePickKind.Signal);
                                NewRoutePointPicked(pose.World.X, pose.World.Y);
                            }
                            else
                                Debug.Log("Signal route new: no signal " + pickId + " among the " + newRouteSignals.Count + " read.");
                        }
                        else
                            Debug.Log("Signal route new: signals not read (" + (newRouteSignals == null ? "none" : "ok") + "), failure " + (newRouteFailure ?? "-"));
                        break;
                    case "aerialpick":
                        // The pipette of the aerial image placement, as its window starts it: "aerialpick 0" (reference point) or 1/2 (fit points).
                        BeginPlacementPick(int.TryParse(Word(++index), NumberStyles.Integer, culture, out var aerialWhich) ? aerialWhich : 0);
                        break;
                    case "pipette":
                        // The pipette of the plan, as its button starts it: "pipette signal|target|distant|next".
                        var pipetteWord = (Word(++index) ?? "signal").ToLowerInvariant();
                        StartNewRoutePick(pipetteWord == "target" ? NewRoutePickKind.Target : pipetteWord == "distant" ? NewRoutePickKind.Distant :
                                          pipetteWord == "next" ? NewRoutePickKind.Next : NewRoutePickKind.Signal);
                        break;
                    case "click":
                        // A click of the map while a point is awaited: whoever waits for it gets it, as the canvas tells all that listen.
                        if (double.TryParse(Word(++index), NumberStyles.Float, culture, out var clickX) && double.TryParse(Word(++index), NumberStyles.Float, culture, out var clickY))
                        {
                            PlacementPointPicked(clickX, clickY);
                            NewRoutePointPicked(clickX, clickY);
                        }
                        break;
                    case "chronoview":
                        // The map in the view of a day (the plan cannot go on there): "chronoview 2005-01-01".
                        if (DateTime.TryParseExact(Word(++index) ?? string.Empty, "yyyy-MM-dd", culture, DateTimeStyles.None, out var chronoDay))
                        {
                            ShowChronoDate(chronoDay);
                            var chronoWaited = Time.realtimeSinceStartup;
                            while (chronoTask != null && Time.realtimeSinceStartup - chronoWaited < 120f)
                                yield return null;
                            for (var frame = 0; frame < 20; frame++)
                                yield return null;
                        }
                        break;
                    case "touch":
                        TouchWorkingCopyForTest();
                        break;
                    case "state":
                        yield return null;
                        var anchor = geoSettings?.Anchor;
                        Debug.Log("Signal route new (state): plan " + newRouteActive + ", plan pipette " + newRoutePick + ", map waits for a click " + (mapCanvas != null && mapCanvas.IsPickingPoint) +
                                  ", aerial pipette " + placementPickFor + ", signals read " + (newRouteSignals != null) + ", signal " + (newRouteSignalId?.ToString(culture) ?? "-") +
                                  ", can edit " + CanEdit + ", anchor " + (anchor == null ? "none" : anchor.TileX + "/" + anchor.OffsetX.ToString("0.###", culture) + "/" +
                                  anchor.OffsetY.ToString("0.###", culture)) + ", status: " + statusLabel?.text);
                        break;
                    case "create":
                        create = true;
                        break;
                    case "undo":
                        undo = true;
                        break;
                    case "redo":
                        redo = true;
                        break;
                }
            }
            yield return WaitForNewRoute();
            FillSignalRouteBody();
            ApplyNewRouteHighlight();
            LogNewRoute("planned");
            if (!create)
                yield break;
            CreateNewSignalRoute();
            yield return WaitForSignalRoutesRead(30);
            LogNewRoute("after create");
            var route = signalRouteSelected >= 0 ? CurrentSignalRoute(signalRouteSelected) : null;
            Debug.Log("Signal route new: created route " + (route == null ? "none chosen" : (route.Number ?? route.Index + 1) + " signal " + route.SignalId + ", kind " + route.Kind + ", aspect " + route.Aspect +
                      ", pieces " + route.Entries.Count + ", distant " + (route.DistantSignalId?.ToString(culture) ?? "-") + ", next " + (route.NextSignalId?.ToString(culture) ?? "-") + ", speed " +
                      (route.SpeedLimit?.ToString("0.###", culture) ?? "-") + ", edited " + signalRouteEdited.Contains(route.Index)) + "; chosen " + signalRouteSelected + ", undo steps " +
                      undoSteps.Count + ", status: " + statusLabel?.text);
            if (undo)
            {
                StepHistory(undoSteps, redoSteps, true);
                yield return WaitForSignalRoutesRead(30);
                Debug.Log("Signal route new: after undo " + (signalRouteMap?.Routes.Routes.Count.ToString(culture) ?? "?") + " routes, chosen " + signalRouteSelected + ", undo steps " + undoSteps.Count +
                          ", redo steps " + redoSteps.Count);
            }
            if (redo)
            {
                StepHistory(redoSteps, undoSteps, false);
                yield return WaitForSignalRoutesRead(30);
                Debug.Log("Signal route new: after redo " + (signalRouteMap?.Routes.Routes.Count.ToString(culture) ?? "?") + " routes, chosen " + signalRouteSelected + ", undo steps " + undoSteps.Count +
                          ", redo steps " + redoSteps.Count);
            }
        }

        /// <summary>Test runs: a small move of one object, which writes the working copy once (the write count changes).</summary>
        private void TouchWorkingCopyForTest()
        {
            if (currentSummary == null)
                return;
            foreach (var tile in currentSummary.Tiles)
                if (tile.Objects.Count > 0)
                {
                    var item = tile.Objects[0];
                    CommitEdits(new List<MapObjectEdit>
                    {
                        new MapObjectEdit
                        {
                            Tile = tile, Record = item, LocalX = item.LocalX + 0.01, LocalY = item.LocalY, Height = item.Height,
                            HeadingDegrees = item.HeadingDegrees, PitchDegrees = item.PitchDegrees, BankDegrees = item.BankDegrees
                        }
                    });
                    Debug.Log("Signal route new: the working copy was touched (write count " + (session?.Workspace?.WriteCount ?? 0) + ").");
                    return;
                }
        }

        private IEnumerator WaitForNewRoute()
        {
            for (var frame = 0; frame < 20 || newRouteSearching || (newRouteActive && newRouteSignals == null); frame++)
            {
                if (frame > 3000)
                    yield break;
                yield return null;
            }
        }

        private void LogNewRoute(string moment)
        {
            var culture = CultureInfo.InvariantCulture;
            var text = new System.Text.StringBuilder("Signal route new (" + moment + "): signal " + (newRouteSignalId?.ToString(culture) ?? "-") + " (" + (newRouteSignalFile ?? "-") + "), target " + newRouteTargetMode +
                                                     ", start " + newRouteStart + ", overlap " + newRouteOverlap);
            if (newRouteSearch != null)
            {
                text.Append(", start on the signal's piece ").Append(newRouteSearch.StartsOnSignalTrack).Append(", ways ").Append(newRouteSearch.Ways.Count).Append(", more possible ").Append(newRouteSearch.MoreWaysPossible);
                for (var index = 0; index < newRouteSearch.Ways.Count; index++)
                {
                    var way = newRouteSearch.Ways[index];
                    text.Append("; way ").Append(index + 1).Append(" ").Append(way.RouteLengthMeters.ToString("0", culture)).Append(" m to target, ")
                        .Append(way.LengthMeters.ToString("0", culture)).Append(" m ").Append(way.Entries.Count).Append(" pieces (overlap ")
                        .Append(way.OverlapEntries).Append(") to ").Append(way.EndSignalId?.ToString(culture) ?? "-");
                }
                if (!newRouteSearch.Found)
                    text.Append("; problem ").Append(newRouteSearch.Problem).Append(": ").Append(newRouteSearch.Message);
            }
            text.Append("; chosen way ").Append(newRouteWay + 1).Append(", kind ").Append(newRouteKind).Append(", aspect ").Append(newRouteAspect?.ToString(culture) ?? "auto").Append(", distant ")
                .Append(newRouteDistant?.ToString(culture) ?? "-").Append(", next ").Append(newRouteNext?.ToString(culture) ?? "-").Append(", speed ").Append(newRouteSpeed?.ToString("0.###", culture) ?? "-")
                .Append(", status: ").Append(statusLabel?.text);
            Debug.Log(text.ToString());
        }
    }
}
