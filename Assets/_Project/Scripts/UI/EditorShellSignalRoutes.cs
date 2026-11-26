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
    /// The signal routes of the map, shown only (ADR 105, M1): a round button in the bottom bar opens the list of the routes of signalroutes.cfg, each
    /// with its number, signal, track pieces, speed limit and the signals it announces, findings marked in yellow. A click chooses the route: the map shows
    /// its track pieces in orange and tells its signal (a disc), distant signal (a diamond) and next signal (a square) apart. While the list is open every
    /// signal with a route carries a small marker; closing it takes the layer off the map. The routes are resolved against the map on a worker thread
    /// (<see cref="OmsiSignalRouteMap.Resolve"/>) and again after a change of the map; the right-click menu of a signal opens the list for that signal.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // The parsed file, for the menu and the list; it belongs to the map it was read for and is dropped with a new session.
        private OmsiSignalRoutes signalRoutesFile;
        private string signalRoutesFileMap;
        private int signalRoutesFileStamp;

        // The routes laid onto the map: null while not read yet or after a change that may have moved a signal.
        private OmsiSignalRouteMap signalRouteMap;
        private Task<OmsiSignalRouteMap> signalRouteTask;
        private int signalRouteGeneration;
        private int signalRouteStamp = -1; // write count of the working copy when the reading started
        private bool signalRouteRebuildPending;
        private double signalRouteResolveMilliseconds;

        // The routes (their places in the file) that differ from the original map, for the yellow mark; found together with the routes laid onto the map.
        private HashSet<int> signalRouteEdited = new HashSet<int>();
        private OmsiSignalRoutes signalRouteOriginal;

        // A route to choose once the routes are read again (after Undo of a deletion), as its place in the list is not known before.
        private int? signalRouteSelectAfter;

        // What the user chose: the route (its index in the file), the signal the list is limited to, and where the list is scrolled to.
        private int signalRouteSelected = -1;
        private int? signalRouteFilter;
        private float signalRouteScroll;

        // The track of the route chosen, found on a worker thread.
        private OmsiSignalRouteShape signalRouteShape;
        private Task<OmsiSignalRouteShape> signalRouteShapeTask;
        private int signalRouteShapeGeneration;
        private bool signalRouteFitWhenShaped;

        // The list while the popup shows it.
        private VisualElement signalRouteBody;
        private readonly Dictionary<int, Button> signalRouteButtons = new Dictionary<int, Button>();
        private bool signalRouteLayerOn;
        private ScrollView signalRouteScrollView;

        // -signalRoutes, -signalRoute n and -signalRouteSignal id of a test run, applied once the routes are read.
        private int? signalRouteArgSelect;
        private bool signalRouteArgFit = true;

        private bool SignalRoutesOpen => barPopupOpen && projectTab == ProjectTab.SignalRoutes && signalRouteBody != null && currentSummary != null;

        /// <summary>The parsed signalroutes.cfg of the open map (read once for each map); null without a map.</summary>
        private OmsiSignalRoutes SignalRoutesFile()
        {
            if (session == null || currentSummary == null)
                return null;
            var key = currentSummary.GlobalConfigPath ?? string.Empty;
            // Every save, revert and deletion of the working copy counts (ADR 057), so a changed file is read again.
            var stamp = session.Workspace != null ? session.Workspace.WriteCount : 0;
            if (signalRoutesFile != null && stamp == signalRoutesFileStamp && string.Equals(key, signalRoutesFileMap, StringComparison.OrdinalIgnoreCase))
                return signalRoutesFile;
            signalRoutesFileStamp = stamp;
            try
            {
                signalRoutesFile = OmsiSignalRoutes.Read(session.Source);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                signalRoutesFile = OmsiSignalRoutes.Empty();
            }
            signalRoutesFileMap = key;
            return signalRoutesFile;
        }

        /// <summary>True when the object with this ID is a signal that has a route (for the right-click menu).</summary>
        private bool SignalHasRoutes(int objectId)
        {
            var file = SignalRoutesFile();
            return file != null && file.Routes.Count > 0 && file.RoutesOfSignal(objectId).Count > 0;
        }

        /// <summary>Opens the list, limited to one signal when given (the right-click menu of a signal) and with its first route chosen.</summary>
        private void ShowSignalRoutes(int? signalId)
        {
            signalRouteFilter = signalId;
            signalRouteSelected = -1;
            signalRouteShape = null;
            signalRouteFitWhenShaped = false;
            ShowProjectTab(ProjectTab.SignalRoutes);
            if (signalId.HasValue && signalRouteMap != null)
                ChooseFirstRouteOfFilter(true);
        }

        private void ChooseFirstRouteOfFilter(bool fit)
        {
            if (signalRouteMap == null || !signalRouteFilter.HasValue || signalRouteSelected >= 0)
                return;
            var rows = OmsiSignalRouteList.Rows(signalRouteMap, signalRouteFilter);
            if (rows.Count > 0)
                SelectSignalRoute(rows[0].RouteIndex, fit, false);
        }

        /// <summary>Forgets the routes laid onto the map after a change of the map (a signal may have moved or gone); with the list open they are read again.</summary>
        /// <param name="force">True for a new map or summary; without it a refresh after a refused step (nothing was written) keeps the routes and the list as they are.</param>
        private void InvalidateSignalRoutes(bool force = false)
        {
            // A reading started or finished for the very state of the working copy is still right: no list redrawn, no flicker, no new reading.
            var stamp = session != null && session.Workspace != null ? session.Workspace.WriteCount : 0;
            if (!force && (signalRouteMap != null || signalRouteTask != null) && stamp == signalRouteStamp)
            {
                Debug.Log("Signal routes kept: the working copy did not change (write count " + stamp + ").");
                return;
            }
            if (force || !SignalRoutesOpen || mapCanvas == null || signalRouteMap == null)
            {
                signalRouteMap = null;
                signalRouteTask = null;
                signalRouteGeneration++;
                signalRouteShape = null;
                signalRouteShapeTask = null;
                signalRouteShapeGeneration++;
                if (!SignalRoutesOpen || mapCanvas == null)
                    return;
                // A new map or a first reading: the markers of the routes before are gone at once; the list says it reads.
                ApplySignalRouteLayer();
                FillSignalRouteBody();
            }
            else
            {
                // A real change with the list open: list, chosen route, highlight and scroll stay as they are while the routes are resolved
                // again in the background; the result replaces them when it arrives. A reading still running is outdated and dropped.
                signalRouteTask = null;
                signalRouteGeneration++;
            }
            if (signalRouteRebuildPending)
                return;
            // One new reading 100 ms after the last change, as the object list does. The timer sits on the map, which outlives the popup.
            signalRouteRebuildPending = true;
            mapCanvas.schedule.Execute(() =>
            {
                signalRouteRebuildPending = false;
                if (!SignalRoutesOpen || signalRouteTask != null)
                    return;
                StartSignalRouteBuild();
                // The list only says it reads when there is nothing to show yet.
                if (signalRouteMap == null)
                    FillSignalRouteBody();
            }).ExecuteLater(100);
        }

        /// <summary>A new map: the file, the choice and the layer of the map before are gone.</summary>
        private void ForgetSignalRoutesOfMap()
        {
            ReleaseSignalRouteFields();
            EndSignalRoutePick(false);
            CancelNewSignalRoute(false);
            signalRouteEdited = new HashSet<int>();
            signalRouteOriginal = null;
            signalRouteSelectAfter = null;
            signalRoutesFile = null;
            signalRoutesFileMap = null;
            signalRouteSelected = -1;
            signalRouteFilter = null;
            signalRouteScroll = 0f;
            signalRouteFitWhenShaped = false;
            signalRouteRebuildPending = false;
            InvalidateSignalRoutes(true);
            mapCanvas?.SetSignalRouteMarkers(null);
            mapCanvas?.SetSignalRouteHighlight(null, null, null, null, null);
        }

        /// <summary>The popup closes or shows another list: the markers and the highlight leave the map, the choice stays for the next time.</summary>
        private void LeaveSignalRoutes()
        {
            ReleaseSignalRouteFields();
            EndSignalRoutePick(false);
            CancelNewSignalRoute(false);
            signalRouteBody = null;
            signalRouteRebuildPending = false;
            signalRouteButtons.Clear();
            if (!signalRouteLayerOn)
                return;
            signalRouteLayerOn = false;
            mapCanvas?.SetSignalRouteMarkers(null);
            mapCanvas?.SetSignalRouteHighlight(null, null, null, null, null);
        }

        /// <summary>The page of the popup: the routes as a list, or why there is none.</summary>
        private VisualElement SignalRoutesTab()
        {
            var tab = new VisualElement();
            tab.AddToClassList("sigroute");
            signalRouteBody = tab;
            signalRouteLayerOn = true;
            if (signalRouteMap == null && signalRouteTask == null)
                StartSignalRouteBuild();
            FillSignalRouteBody();
            ApplySignalRouteLayer();
            return tab;
        }

        private void StartSignalRouteBuild()
        {
            var file = SignalRoutesFile();
            if (file == null || currentSummary == null || mapCanvas == null)
                return;
            var summary = currentSummary;
            var source = session.Source;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var generation = ++signalRouteGeneration;
            signalRouteStamp = session.Workspace != null ? session.Workspace.WriteCount : 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var workspace = session.Workspace;
            HashSet<int> edited = null;
            OmsiSignalRoutes original = null;
            var task = Task.Run(() =>
            {
                // Without the installation the path checks are left out; the routes still show.
                Func<string, int?> pathCount = null;
                if (!string.IsNullOrEmpty(root))
                    pathCount = new OmsiAssetPathCounter(root).Count;
                var resolved = OmsiSignalRouteMap.Resolve(file, summary, source, pathCount);
                original = ReadOriginalSignalRoutes(workspace);
                edited = original != null ? OmsiSignalRouteComparison.EditedRoutes(file, original) : new HashSet<int>();
                return resolved;
            });
            signalRouteTask = task;
            // A reading over a list already shown stays unseen unless it takes long.
            if (signalRouteMap != null)
                mapCanvas.schedule.Execute(() =>
                {
                    if (generation == signalRouteGeneration && signalRouteTask == task && !task.IsCompleted && SignalRoutesOpen)
                        SetStatus(Loc.T("sigroute.reading"));
                }).ExecuteLater(1000);
            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                // Another map or a change started another reading meanwhile.
                if (generation != signalRouteGeneration)
                    return;
                signalRouteTask = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatusT("sigroute.status.failed", ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    return;
                }
                var replaced = signalRouteMap;
                var replacedSignature = replaced != null ? SignalRouteListSignature(replaced) : null;
                signalRouteMap = task.Result;
                signalRouteEdited = edited ?? new HashSet<int>();
                signalRouteOriginal = original;
                var selectAfter = signalRouteSelectAfter;
                signalRouteSelectAfter = null;
                signalRouteResolveMilliseconds = watch.Elapsed.TotalMilliseconds;
                Debug.Log("Signal routes: " + signalRouteMap.Routes.Routes.Count + " routes at " + signalRouteMap.Signals.Count + " signals, " + signalRouteMap.Problems.Count +
                          " findings, resolved in " + signalRouteResolveMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms" +
                          (string.IsNullOrEmpty(root) ? " (no installation: path checks left out)." : ".") + (replaced != null ? " Replaced the list shown without redrawing it." : string.Empty));
                if (selectAfter.HasValue && !SignalRoutesOpen)
                    signalRouteSelected = selectAfter.Value;
                var choiceGone = signalRouteSelected >= signalRouteMap.Routes.Routes.Count;
                if (choiceGone)
                {
                    signalRouteSelected = -1;
                    signalRouteShape = null;
                    signalRouteShapeTask = null;
                    signalRouteShapeGeneration++;
                }
                if (SignalRoutesOpen)
                {
                    // The list is only drawn again when what it shows differs; the same rows leave the cards, the choice and the scroll untouched.
                    if (replaced == null || choiceGone || replacedSignature != SignalRouteListSignature(signalRouteMap))
                        FillSignalRouteBody();
                    else
                        RefreshSignalRouteDetail();
                    ApplySignalRouteLayer();
                    if (selectAfter.HasValue)
                        SelectSignalRoute(selectAfter.Value, false, false);
                    else if (signalRouteArgSelect.HasValue)
                    {
                        var wanted = signalRouteArgSelect.Value;
                        signalRouteArgSelect = null;
                        ChooseSignalRouteByNumber(wanted);
                    }
                    else if (signalRouteFilter.HasValue && signalRouteSelected < 0)
                        ChooseFirstRouteOfFilter(true);
                    else if (signalRouteSelected >= 0 && (replaced != null || (signalRouteShape == null && signalRouteShapeTask == null)))
                        StartSignalRouteShape(false); // the track of the chosen route is found again; the line shown stays until then
                    else if (choiceGone)
                        ApplySignalRouteHighlight();
                }
            }).Every(100);
        }

        /// <summary>Everything the cards of the list show, as one text: equal texts mean the list need not be drawn again.</summary>
        private string SignalRouteListSignature(OmsiSignalRouteMap map)
        {
            // The marks of the routes edited belong to the map read last, so a reading that only changes them draws the list again.
            var culture = CultureInfo.InvariantCulture;
            var text = new System.Text.StringBuilder();
            text.Append(map.Routes.SignalIds().Count).Append('|').Append(string.Join(";", OmsiSignalRouteList.FileProblems(map))).Append('\n');
            foreach (var row in OmsiSignalRouteList.Rows(map, signalRouteFilter))
                text.Append(row.RouteIndex).Append(',').Append(row.Number).Append(',').Append(row.SignalId).Append(',').Append(row.SignalFile).Append(',').Append(row.PieceCount)
                    .Append(',').Append(row.SpeedLimit?.ToString("R", culture)).Append(',').Append(row.DistantSignalId).Append(',').Append(row.NextSignalId).Append(',')
                    .Append(string.Join(";", row.Problems)).Append(',').Append(signalRouteEdited.Contains(row.RouteIndex)).Append('\n');
            return text.ToString();
        }

        private bool SignalRoutesReady => signalRouteMap != null && signalRouteTask == null && signalRouteShapeTask == null;

        /// <summary>Redraws the content of the list; the place in the list is kept.</summary>
        private void FillSignalRouteBody()
        {
            if (signalRouteBody == null)
                return;
            signalRouteBody.Clear();
            signalRouteButtons.Clear();
            if (newRouteActive)
            {
                // Planning a new route replaces the list; it comes back when the plan ends.
                signalRouteBody.Add(NewRoutePanel());
                return;
            }
            if (signalRouteMap == null)
            {
                var reading = new Label(Loc.T("sigroute.reading"));
                reading.AddToClassList("muted");
                signalRouteBody.Add(reading);
                return;
            }

            var routes = signalRouteMap.Routes.Routes;
            var fileProblems = OmsiSignalRouteList.FileProblems(signalRouteMap);
            if (routes.Count == 0)
            {
                var none = new Label(Loc.T("sigroute.empty"));
                none.AddToClassList("muted");
                signalRouteBody.Add(none);
                var hint = new Label(Loc.T("sigroute.empty.hint"));
                hint.AddToClassList("panel-info");
                signalRouteBody.Add(hint);
                if (CanEdit)
                    signalRouteBody.Add(NewRouteButton());
                AddSignalRouteProblems(fileProblems);
                return;
            }

            var summary = new Label(Loc.T("sigroute.summary", ("count", routes.Count), ("signals", signalRouteMap.Routes.SignalIds().Count)));
            summary.AddToClassList("objlist-summary");
            if (CanEdit)
            {
                var header = new VisualElement();
                header.AddToClassList("sigroute-header");
                summary.AddToClassList("sigroute-header-text");
                header.Add(summary);
                header.Add(NewRouteButton());
                signalRouteBody.Add(header);
            }
            else
                signalRouteBody.Add(summary);
            AddSignalRouteProblems(fileProblems);

            if (signalRouteFilter.HasValue)
            {
                var chip = new Button(() =>
                {
                    signalRouteFilter = null;
                    FillSignalRouteBody();
                }) { text = Loc.T("sigroute.filter", ("id", signalRouteFilter.Value)), tooltip = Loc.T("sigroute.filter.tooltip") };
                chip.AddToClassList("objlist-chip");
                chip.AddToClassList("objlist-chip-on");
                chip.AddToClassList("sigroute-filter");
                signalRouteBody.Add(chip);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sigroute-list");
            var rows = OmsiSignalRouteList.Rows(signalRouteMap, signalRouteFilter);
            if (rows.Count == 0)
            {
                var noMatch = new Label(Loc.T("sigroute.no_match", ("id", signalRouteFilter ?? 0)));
                noMatch.AddToClassList("muted");
                scroll.Add(noMatch);
            }
            foreach (var row in rows)
                scroll.Add(MakeSignalRouteRow(row));
            scroll.verticalScroller.valueChanged += value => signalRouteScroll = value;
            signalRouteBody.Add(scroll);
            var offset = signalRouteScroll;
            scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0f, offset)).StartingIn(30);
            signalRouteScrollView = scroll;

            if (signalRouteSelected >= 0 && signalRouteSelected < routes.Count)
                signalRouteBody.Add(SignalRouteDetail(signalRouteSelected));
        }

        private void AddSignalRouteProblems(List<string> fileProblems)
        {
            if (fileProblems.Count == 0)
                return;
            var banner = new Label(Loc.T("sigroute.file_problems", ("count", fileProblems.Count))) { tooltip = string.Join("\n", fileProblems.Take(20)) };
            banner.AddToClassList("sigroute-problem");
            signalRouteBody.Add(banner);
        }

        /// <summary>"New route...": plans a new route (only offered where the map can be edited).</summary>
        private Button NewRouteButton()
        {
            var button = OptionControls.AsTextChoice(LocBindings.WithTooltip(LocBindings.NewButton("sigroute.new", () => BeginNewSignalRoute(null)), "sigroute.new.tooltip")); // i18n:keys
            button.name = "signal-route-new";
            button.AddToClassList("sigroute-new-button");
            return button;
        }

        private Button MakeSignalRouteRow(OmsiSignalRouteRow row)
        {
            var culture = CultureInfo.InvariantCulture;
            var index = row.RouteIndex;
            var card = new Button(() => SelectSignalRoute(index, true, true));
            card.AddToClassList("sigroute-card");
            card.EnableInClassList("sigroute-card-selected", index == signalRouteSelected);
            card.EnableInClassList("sigroute-card-problem", row.HasProblems);
            var edited = signalRouteEdited.Contains(index);
            card.EnableInClassList("sigroute-card-edited", edited);
            card.tooltip = Loc.T("sigroute.row.tooltip");

            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.AddToClassList("sigroute-top");
            var number = new Label(row.Number.ToString(culture)) { pickingMode = PickingMode.Ignore };
            number.AddToClassList("sigroute-number");
            string titleText;
            if (!row.SignalId.HasValue)
                titleText = Loc.T("sigroute.row.no_signal");
            else if (row.SignalFile != null)
                titleText = Loc.T("sigroute.row.signal", ("id", row.SignalId.Value), ("file", row.SignalFile));
            else
                titleText = Loc.T("sigroute.row.signal_unknown", ("id", row.SignalId.Value));
            var title = new Label(titleText) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("sigroute-title");
            var pieces = new Label(Loc.T("sigroute.row.pieces", ("count", row.PieceCount))) { pickingMode = PickingMode.Ignore };
            pieces.AddToClassList("sigroute-pieces");
            top.Add(number);
            top.Add(title);
            if (edited)
            {
                var tag = LocBindings.NewLabel("sigroute.edited"); // i18n:keys
                tag.pickingMode = PickingMode.Ignore;
                tag.AddToClassList("sigroute-tag");
                top.Add(tag);
            }
            top.Add(pieces);
            card.Add(top);

            var parts = new List<string>();
            if (row.SpeedLimit.HasValue)
                parts.Add(Loc.T("sigroute.row.speed", ("speed", row.SpeedLimit.Value)));
            if (row.DistantSignalId.HasValue)
                parts.Add(Loc.T("sigroute.row.distant", ("id", row.DistantSignalId.Value)));
            if (row.NextSignalId.HasValue)
                parts.Add(Loc.T("sigroute.row.next", ("id", row.NextSignalId.Value)));
            if (parts.Count > 0)
            {
                var detail = new Label(string.Join("  ·  ", parts)) { pickingMode = PickingMode.Ignore };
                detail.AddToClassList("sigroute-detail");
                card.Add(detail);
            }
            if (row.HasProblems)
            {
                var problem = new Label(Loc.T("sigroute.row.problems", ("count", row.Problems.Count), ("first", row.Problems[0]))) { pickingMode = PickingMode.Ignore };
                problem.AddToClassList("sigroute-problem");
                card.Add(problem);
                card.tooltip = string.Join("\n", row.Problems.Take(12));
            }
            else if (edited)
                card.tooltip = Loc.T("sigroute.edited.tooltip");
            signalRouteButtons[index] = card;
            return card;
        }

        /// <summary>Below the list: the aspect of the chosen route, how much of its track the map has, what the three shapes on the map mean and its findings.</summary>
        private VisualElement SignalRouteDetail(int routeIndex)
        {
            // A scroll view, so that on a small window the list above keeps some rows and the fields below scroll.
            var box = new ScrollView(ScrollViewMode.Vertical);
            box.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            box.AddToClassList("sigroute-detail-box");
            // The values come from the file as it is now, so a change shows at once, before the routes are laid onto the map again.
            var route = CurrentSignalRoute(routeIndex);
            if (route == null)
                return box;
            var resolved = signalRouteMap.Resolved.Count > route.Index ? signalRouteMap.Resolved[route.Index] : null;
            var number = route.Number ?? route.Index + 1;
            string aspect;
            if (route.Aspect == 1)
                aspect = Loc.T("sigroute.aspect.clear");
            else if (route.Aspect == 2)
                aspect = Loc.T("sigroute.aspect.reduced");
            else
                aspect = Loc.T("sigroute.aspect.other", ("aspect", route.Aspect ?? 0));
            var heading = new Label(Loc.T("sigroute.detail", ("number", number), ("aspect", aspect)));
            heading.AddToClassList("sigroute-detail-title");
            if (signalRouteEdited.Contains(route.Index))
            {
                var top = new VisualElement();
                top.AddToClassList("sigroute-top");
                heading.AddToClassList("sigroute-title");
                top.Add(heading);
                var tag = LocBindings.WithTooltip(LocBindings.NewLabel("sigroute.edited"), "sigroute.edited.tooltip"); // i18n:keys
                tag.AddToClassList("sigroute-tag");
                top.Add(tag);
                box.Add(top);
            }
            else
                box.Add(heading);

            if (resolved != null)
            {
                var found = resolved.Entries.Count(entry => entry.Element != null);
                var track = new Label(Loc.T("sigroute.detail.track", ("found", found), ("count", resolved.Entries.Count)));
                track.AddToClassList("sigroute-detail");
                box.Add(track);
            }

            if (CanEdit)
                AddSignalRouteEditor(box, route);

            var legend = new VisualElement();
            legend.AddToClassList("sigroute-legend");
            AddLegendItem(legend, "sigroute-swatch-track", "sigroute.legend.track");
            AddLegendItem(legend, "sigroute-swatch-signal", "sigroute.legend.signal");
            AddLegendItem(legend, "sigroute-swatch-distant", "sigroute.legend.distant");
            AddLegendItem(legend, "sigroute-swatch-next", "sigroute.legend.next");
            box.Add(legend);
            return box;
        }

        private static void AddLegendItem(VisualElement legend, string swatchClass, string textKey)
        {
            var item = new VisualElement();
            item.AddToClassList("sigroute-legend-item");
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("sigroute-swatch");
            swatch.AddToClassList(swatchClass);
            item.Add(swatch);
            var label = new Label(Loc.T(textKey)) { pickingMode = PickingMode.Ignore }; // i18n:keys
            label.AddToClassList("sigroute-legend-text");
            item.Add(label);
            legend.Add(item);
        }

        /// <summary>A click on a route chooses it and brings it into view; a second click on the chosen route lets it go.</summary>
        private void SelectSignalRoute(int routeIndex, bool fit, bool toggle)
        {
            if (newRouteActive || signalRouteMap == null || routeIndex < 0 || routeIndex >= signalRouteMap.Resolved.Count)
                return;
            EndSignalRoutePick(false);
            signalRouteSelected = toggle && routeIndex == signalRouteSelected ? -1 : routeIndex;
            signalRouteShape = null;
            signalRouteShapeTask = null;
            signalRouteShapeGeneration++;
            foreach (var pair in signalRouteButtons)
                pair.Value.EnableInClassList("sigroute-card-selected", pair.Key == signalRouteSelected);
            // A route chosen from outside the list (the menu of a signal, the command line) may stand out of view in it.
            if (!toggle && signalRouteSelected >= 0 && signalRouteButtons.TryGetValue(signalRouteSelected, out var chosen) && signalRouteScrollView != null)
            {
                var view = signalRouteScrollView;
                view.schedule.Execute(() => view.ScrollTo(chosen)).StartingIn(80);
            }
            // The detail below the list follows the choice; the list itself keeps its place.
            RefreshSignalRouteDetail();
            ApplySignalRouteHighlight();
            if (signalRouteSelected < 0)
            {
                SetStatusT("sigroute.status.cleared");
                return;
            }
            var resolved = signalRouteMap.Resolved[signalRouteSelected];
            // The signals are in view at once; the track comes when its paths are read.
            if (fit)
                FitSignalRoute(resolved, null);
            StartSignalRouteShape(fit);
            SetStatusT("sigroute.status.selected", ("number", resolved.Route.Number ?? resolved.Route.Index + 1), ("count", resolved.Entries.Count),
                ("found", resolved.Entries.Count(entry => entry.Element != null)));
        }

        private void RefreshSignalRouteDetail()
        {
            if (signalRouteBody == null || signalRouteMap == null)
                return;
            var old = signalRouteBody.Q<VisualElement>(className: "sigroute-detail-box");
            old?.RemoveFromHierarchy();
            var routes = signalRouteMap.Routes.Routes;
            if (signalRouteSelected >= 0 && signalRouteSelected < routes.Count)
                signalRouteBody.Add(SignalRouteDetail(signalRouteSelected));
        }

        /// <summary>Reads the paths of the tiles the chosen route runs over and makes the line of each of its track pieces; the paths of the whole map are used when they were read already.</summary>
        private void StartSignalRouteShape(bool fit)
        {
            if (signalRouteMap == null || signalRouteSelected < 0 || mapCanvas == null || currentSummary == null)
                return;
            var resolved = signalRouteMap.Resolved[signalRouteSelected];
            var summary = currentSummary;
            var known = mapPaths;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var generation = ++signalRouteShapeGeneration;
            signalRouteFitWhenShaped = fit;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var task = Task.Run(() =>
            {
                var byTile = new Dictionary<int, IReadOnlyList<OmsiMapPath>>();
                if (known != null)
                {
                    foreach (var index in OmsiSignalRouteList.TilesOf(resolved))
                    {
                        var tile = summary.Tiles[index];
                        byTile[index] = known.FindAll(path => path.TileX == tile.X && path.TileY == tile.Y);
                    }
                }
                else if (!string.IsNullOrEmpty(root))
                {
                    var profiles = new OmsiSplineProfileCatalog(root);
                    var objects = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);
                    OmsiSceneryObject Read(string assetPath)
                    {
                        if (objects.TryGetValue(assetPath, out var cached))
                            return cached;
                        OmsiSceneryObject sceneryObject = null;
                        try
                        {
                            var path = OmsiRelativePath.Resolve(root, assetPath);
                            if (File.Exists(path))
                                sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(path));
                        }
                        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException ||
                                                          exception is ArgumentException)
                        {
                            // An unreadable object has no paths; its piece shows as a point.
                        }
                        objects[assetPath] = sceneryObject;
                        return sceneryObject;
                    }

                    foreach (var index in OmsiSignalRouteList.TilesOf(resolved))
                        byTile[index] = OmsiMapPaths.OfTile(summary, summary.Tiles[index], Read, profiles.Get);
                }
                return OmsiSignalRouteList.Shape(resolved, summary, index => byTile.TryGetValue(index, out var paths) ? paths : null);
            });
            signalRouteShapeTask = task;
            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                if (generation != signalRouteShapeGeneration)
                    return;
                signalRouteShapeTask = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    return;
                }
                signalRouteShape = task.Result;
                Debug.Log("Signal route " + (resolved.Route.Number ?? resolved.Route.Index + 1) + ": " + signalRouteShape.Lines.Count + " track lines, " + signalRouteShape.Points.Count +
                          " points, " + signalRouteShape.Missing + " pieces not on the map, read in " + watch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms" +
                          (known != null ? " (paths of the map)." : "."));
                ApplySignalRouteHighlight();
                if (signalRouteFitWhenShaped)
                    FitSignalRoute(resolved, signalRouteShape);
            }).Every(50);
        }

        /// <summary>Marks every signal with a route and the route chosen on the map, while the list is open.</summary>
        private void ApplySignalRouteLayer()
        {
            if (mapCanvas == null)
                return;
            if (!signalRouteLayerOn || (signalRouteMap == null && !newRouteActive))
            {
                mapCanvas.SetSignalRouteMarkers(null);
                mapCanvas.SetSignalRouteHighlight(null, null, null, null, null);
                return;
            }
            mapCanvas.SetSignalRouteMarkers(signalRouteMap?.Signals.Select(place => place.World));
            ApplySignalRouteHighlight();
        }

        private void ApplySignalRouteHighlight()
        {
            if (newRouteActive)
            {
                // The ways of a route being planned own the highlight.
                ApplyNewRouteHighlight();
                return;
            }
            if (mapCanvas == null || !signalRouteLayerOn || signalRouteMap == null)
                return;
            var resolved = signalRouteSelected >= 0 && signalRouteSelected < signalRouteMap.Resolved.Count ? signalRouteMap.Resolved[signalRouteSelected] : null;
            OmsiMapPoint? signal = null;
            if (resolved != null)
                signal = resolved.Signal?.World;
            else if (signalRouteFilter.HasValue)
                signal = signalRouteMap.FindElement(signalRouteFilter.Value)?.World;
            mapCanvas.SetSignalRouteHighlight(signalRouteShape?.Lines, signalRouteShape?.Points, signal, resolved?.DistantSignal?.World, resolved?.NextSignal?.World);
        }

        /// <summary>Brings the route into the part of the map the popup and the inspector leave free: its signals first, then, with the track known, all of it.</summary>
        private void FitSignalRoute(OmsiResolvedSignalRoute resolved, OmsiSignalRouteShape shape)
        {
            if (mapCanvas == null)
                return;
            var places = new List<OmsiMapPoint>();
            if (resolved.Signal != null)
                places.Add(resolved.Signal.World);
            if (resolved.DistantSignal != null)
                places.Add(resolved.DistantSignal.World);
            if (resolved.NextSignal != null)
                places.Add(resolved.NextSignal.World);
            if (!OmsiSignalRouteList.Bounds(shape, places, out var min, out var max))
                return;
            ShowSignalRouteBox(min, max);
        }

        /// <summary>
        /// Command line (test runs): <c>-signalRoutes</c> opens the list, <c>-signalRoute n</c> chooses the route with that number in the list (and brings
        /// it into view unless <c>-view</c> is given), <c>-signalRouteSignal id</c> limits the list to one signal as the right-click menu does.
        /// </summary>
        private bool ReadSignalRouteArguments(string[] args)
        {
            string Value(string option)
            {
                var at = Array.IndexOf(args, option);
                return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            }

            var any = Array.IndexOf(args, "-signalRoutes") >= 0 || Array.IndexOf(args, "-signalRouteNew") >= 0;
            signalRouteArgFit = Array.IndexOf(args, "-view") < 0;
            if (int.TryParse(Value("-signalRouteSignal"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var signal))
            {
                signalRouteFilter = signal;
                any = true;
            }
            if (int.TryParse(Value("-signalRoute"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                signalRouteArgSelect = number;
                any = true;
            }
            return any;
        }

        /// <summary>The route with the number the file gives it (or its place counted from 1), chosen as a click would; false when there is none.</summary>
        private bool ChooseSignalRouteByNumber(int number)
        {
            if (signalRouteMap == null)
                return false;
            foreach (var row in OmsiSignalRouteList.Rows(signalRouteMap))
                if (row.Number == number)
                {
                    SelectSignalRoute(row.RouteIndex, signalRouteArgFit, false);
                    return true;
                }
            Debug.LogWarning("-signalRoute: the map has no route numbered " + number + ".");
            return false;
        }
    }
}
