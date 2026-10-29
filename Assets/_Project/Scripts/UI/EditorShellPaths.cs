using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The AI paths view (ADR 046): "Paths" beside "Routes" shows the paths AI traffic and people use, read on a worker
    /// thread for the whole map as openOMSI builds its lanes. The project browser lists them by kind to show or hide, with
    /// the ends no other path joins; the inspector names the paths of the selected object or spline and their rules.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private static readonly string[] PathKindNames = { "Roads", "Footpaths", "Rails" };

        private Button pathsToggleButton;
        private List<OmsiMapPath> mapPaths;
        private Task<List<OmsiMapPath>> pathsTask;
        private int pathsGeneration;
        private bool pathsShown;
        private readonly bool[] shownPathKinds = { true, true, true };

        // The open end the view went to last, by its place in the list of open ends; -1 before the first.
        private int openEndCursor = -1;

        /// <summary>Puts "Paths" after "Routes" among the view controls, built like it.</summary>
        private void CreatePathsButton()
        {
            var parent = routesToggleButton?.parent;
            if (parent == null)
                return;
            pathsToggleButton = new Button(() => ShowPaths(!pathsShown))
            {
                tooltip = "Show the paths AI traffic and people use in the view from above: roads blue, footpaths green, rails yellow; wide with the " +
                          "right of way, dashed where they give way, red rings where no other path joins. They show closer in than " +
                          MapCanvasElement.PathMetersPerPixel.ToString("0.#", CultureInfo.InvariantCulture) + " m per pixel."
            };
            foreach (var name in routesToggleButton.GetClasses())
                pathsToggleButton.AddToClassList(name);
            pathsToggleButton.RemoveFromClassList("view-control-active");
            DecorateButton(pathsToggleButton, "spline", "Paths");
            parent.Insert(parent.IndexOf(routesToggleButton) + 1, pathsToggleButton);
        }

        /// <summary>Shows or hides the AI paths, reading them first when the map's are not known yet.</summary>
        private void ShowPaths(bool visible)
        {
            if (mapCanvas == null)
                return;
            pathsShown = visible && currentSummary != null;
            mapCanvas.ShowPaths = pathsShown;
            pathsToggleButton?.EnableInClassList("view-control-active", pathsShown);
            if (pathsShown && mapPaths == null && pathsTask == null)
                StartPathsBuild(currentSummary);
            if (pathsShown)
                SetStatus(DescribePaths());
            RenderProjectBrowser();
            RenderInspector();
        }

        private string DescribePaths()
        {
            if (mapPaths == null)
                return "Finding the AI paths of the map...";
            var open = mapPaths.FindAll(path => path.OpenStart || path.OpenEnd).Count;
            return mapPaths.Count + " AI paths shown, " + open + " with an end no other path joins" +
                   (mapCanvas != null && mapCanvas.MetersPerPixel > MapCanvasElement.PathMetersPerPixel ? "; zoom in to see them." : ".");
        }

        /// <summary>Forgets the paths of the map before, as after loading another map or a chrono date, and reads them again while shown.</summary>
        private void ResetPaths(OmsiMapSummary summary)
        {
            mapPaths = null;
            pathsTask = null;
            pathsGeneration++;
            openEndCursor = -1;
            mapCanvas?.SetPaths(null);
            if (pathsShown)
                StartPathsBuild(summary);
        }

        /// <summary>
        /// Reads the paths of the whole map on a worker thread; the paths before stay on screen until then. After an edit
        /// they come in quietly (<paramref name="announce"/> false), so the tool's own message stays in the status line.
        /// </summary>
        private void StartPathsBuild(OmsiMapSummary summary, bool announce = true)
        {
            if (mapCanvas == null || summary == null)
                return;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var generation = ++pathsGeneration;
            var task = Task.Run(() => BuildPaths(summary, root));
            pathsTask = task;
            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                // Another map or an edit started another build meanwhile.
                if (generation != pathsGeneration)
                    return;
                pathsTask = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatus("The AI paths could not be read: " + task.Exception?.GetBaseException().Message);
                    return;
                }
                mapPaths = task.Result;
                openEndCursor = -1;
                mapCanvas.SetPaths(mapPaths);
                if (pathsShown)
                {
                    if (announce)
                        SetStatus(DescribePaths());
                    RenderProjectBrowser();
                    RenderInspector();
                }
            }).Every(150);
        }

        private static List<OmsiMapPath> BuildPaths(OmsiMapSummary summary, string installationRoot)
        {
            var profiles = new OmsiSplineProfileCatalog(installationRoot);
            var objects = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);
            OmsiSceneryObject Read(string assetPath)
            {
                if (objects.TryGetValue(assetPath, out var cached))
                    return cached;
                OmsiSceneryObject sceneryObject = null;
                try
                {
                    var path = OmsiRelativePath.Resolve(installationRoot, assetPath);
                    if (File.Exists(path))
                        sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(path));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException ||
                                                  exception is ArgumentException)
                {
                    // An unreadable object has no paths.
                }
                objects.Add(assetPath, sceneryObject);
                return sceneryObject;
            }

            var paths = new List<OmsiMapPath>();
            foreach (var tile in summary.Tiles)
                paths.AddRange(OmsiMapPaths.OfTile(summary, tile, Read, profiles.Get));
            OmsiMapPaths.FindOpenEnds(paths);
            return paths;
        }

        /// <summary>The project browser's list of paths by kind, each shown or hidden by a click, and a way to the open ends.</summary>
        private VisualElement PathsSection()
        {
            var section = new Foldout { text = "AI paths (" + mapPaths.Count + ")", value = true };
            section.AddToClassList("browser-lines");
            for (var kind = 0; kind < 3; kind++)
            {
                var captured = kind;
                var ofKind = mapPaths.FindAll(path => path.Kind == captured);
                var open = ofKind.FindAll(path => path.OpenStart || path.OpenEnd).Count;
                var entry = new Button(() =>
                {
                    shownPathKinds[captured] = !shownPathKinds[captured];
                    mapCanvas?.SetPathKinds(shownPathKinds[0], shownPathKinds[1], shownPathKinds[2]);
                    openEndCursor = -1;
                    RenderProjectBrowser();
                })
                {
                    text = PathKindNames[kind] + "     " + ofKind.Count + " paths" + (open > 0 ? "  /  " + open + " open" : string.Empty),
                    tooltip = "Show or hide the " + PathKindNames[kind].ToLowerInvariant() + " among the paths."
                };
                entry.AddToClassList("browser-tile-entry");
                entry.EnableInClassList("browser-tile-entry-selected", shownPathKinds[kind]);
                section.Add(entry);
            }
            var ends = OpenEnds();
            var next = new Button(NextOpenEnd)
            {
                text = ends.Count > 0 ? "Next open end     " + (openEndCursor + 1) + " of " + ends.Count : "No open ends",
                tooltip = "Goes to the next end of a shown path that no other path of its kind joins within " +
                          OmsiMapPaths.LinkMeters.ToString("0.#", CultureInfo.InvariantCulture) + " m: AI traffic cannot go on there, or never comes."
            };
            next.AddToClassList("browser-tile-entry");
            next.SetEnabled(ends.Count > 0);
            section.Add(next);
            return section;
        }

        /// <summary>The open ends of the shown kinds of path: each path and whether its first point is open, else its last.</summary>
        private List<KeyValuePair<OmsiMapPath, bool>> OpenEnds()
        {
            var ends = new List<KeyValuePair<OmsiMapPath, bool>>();
            if (mapPaths == null)
                return ends;
            foreach (var path in mapPaths)
            {
                if (path.Kind < 0 || path.Kind > 2 || !shownPathKinds[path.Kind])
                    continue;
                if (path.OpenStart)
                    ends.Add(new KeyValuePair<OmsiMapPath, bool>(path, true));
                if (path.OpenEnd)
                    ends.Add(new KeyValuePair<OmsiMapPath, bool>(path, false));
            }
            return ends;
        }

        /// <summary>Centres the view on the next open end and selects the spline or object it belongs to.</summary>
        private void NextOpenEnd()
        {
            var ends = OpenEnds();
            if (ends.Count == 0 || mapCanvas == null)
                return;
            openEndCursor = (openEndCursor + 1) % ends.Count;
            var path = ends[openEndCursor].Key;
            var point = ends[openEndCursor].Value ? path.Points[0] : path.Points[path.Points.Length - 1];
            var tile = currentSummary.Tiles.Find(candidate => candidate.X == path.TileX && candidate.Y == path.TileY);
            if (tile != null && path.OnSpline && tile.Splines.Find(spline => spline.Id == path.ElementId) is OmsiSplineRecord spline)
                SelectSpline(tile, spline);
            else if (tile != null && tile.Objects.Find(item => item.Id == path.ElementId) is OmsiObjectRecord item)
                SelectObject(tile, item, false);
            mapCanvas.CenterOnWorld(point);
            RenderProjectBrowser();
            SetStatus("Open end " + (openEndCursor + 1) + " of " + ends.Count + ": the " + (ends[openEndCursor].Value ? "start" : "end") + " of path " + path.PathIndex +
                      " of " + (path.OnSpline ? "spline #" : "object #") + path.ElementId + " on tile " + path.TileX + ", " + path.TileY +
                      " - no other " + PathKindNoun(path.Kind) + " joins it.");
        }

        /// <summary>The inspector's list of the paths of the selected object or spline, with their rules.</summary>
        private void AddPathDetails(VisualElement container, OmsiTileSummary tile, int elementId, bool onSpline)
        {
            var paths = mapPaths.FindAll(path => path.TileX == tile.X && path.TileY == tile.Y && path.ElementId == elementId && path.OnSpline == onSpline);
            if (CanEdit && paths.Exists(path => path.Kind == 0))
                AddPathRuleEditor(container, tile, elementId, onSpline, paths);
            var heading = new Label("AI PATHS  " + paths.Count);
            heading.AddToClassList("inspector-section-heading");
            container.Add(heading);
            if (paths.Count == 0)
                AddInspectorRecord(container, "It has no paths.", "inspector-record-empty");
            foreach (var path in paths)
            {
                var length = 0.0;
                for (var index = 1; index < path.Points.Length; index++)
                    length += Math.Sqrt(Math.Pow(path.Points[index].X - path.Points[index - 1].X, 2.0) + Math.Pow(path.Points[index].Y - path.Points[index - 1].Y, 2.0));
                var text = "Path " + path.PathIndex + "  " + PathKindNoun(path.Kind) + ", " +
                           (path.Direction == 2 ? "both ways" : path.Direction == 1 ? "against" : "along") + ", " + length.ToString("F1", CultureInfo.InvariantCulture) + " m" +
                           (path.Blinker == 2 ? ", indicates left" : path.Blinker == 3 ? ", indicates right" : string.Empty) +
                           (path.OpenStart || path.OpenEnd ? "\nOpen at its " + (path.OpenStart && path.OpenEnd ? "start and end" : path.OpenStart ? "start" : "end") : string.Empty);
                foreach (var rule in path.Rules)
                    text += "\n" + (rule.Kills ? "takes away " : string.Empty) + DescribeRule(rule);
                AddInspectorRecord(container, text, "inspector-record");
            }
        }

        // The road lanes the rule editor changes, for the element it was last shown for.
        private string ruleEditorElement;
        private readonly HashSet<int> ruleEditorPaths = new HashSet<int>();

        /// <summary>Traffic densities the editor offers, by name; null for the map's normal traffic, without a rule.</summary>
        private static readonly KeyValuePair<string, double?>[] TrafficLevels =
        {
            new KeyValuePair<string, double?>("Default", null), new KeyValuePair<string, double?>("None", 0.0), new KeyValuePair<string, double?>("Low", 0.1),
            new KeyValuePair<string, double?>("Half", 0.5), new KeyValuePair<string, double?>("High", 3.0)
        };

        /// <summary>
        /// The rules of the element's road lanes (stage 4 of the AI paths): which lanes, a speed limit, how much random
        /// traffic, and whether AI cars, buses and trucks may use them, as openOMSI reads [rule] no_cars, bus and trucks.
        /// Shown with the values of the first lane chosen; Apply writes them to every lane chosen, as one undo step.
        /// </summary>
        private void AddPathRuleEditor(VisualElement container, OmsiTileSummary tile, int elementId, bool onSpline, List<OmsiMapPath> paths)
        {
            var lanes = new SortedSet<int>(paths.Where(path => path.Kind == 0).Select(path => path.PathIndex));
            var key = tile.X + "|" + tile.Y + "|" + elementId + "|" + onSpline;
            if (ruleEditorElement != key || !ruleEditorPaths.IsSubsetOf(lanes) || ruleEditorPaths.Count == 0)
            {
                ruleEditorElement = key;
                ruleEditorPaths.Clear();
                ruleEditorPaths.UnionWith(lanes);
            }
            // On splines the rules go to the selected one and those chosen with Ctrl+click of the same road type.
            var skipped = 0;
            var targets = onSpline ? RuleSplines(out skipped) : null;
            var heading = new Label("TRAFFIC RULES" + (targets != null && targets.Count > 1 ? "  " + targets.Count + " SPLINES" : string.Empty));
            heading.AddToClassList("inspector-section-heading");
            container.Add(heading);
            if (onSpline)
            {
                var whole = new Button(() =>
                {
                    var chain = new OmsiSplineAttachmentLayout(currentSummary).Chain(selectedSplineTile, selectedSpline);
                    SetMoreSplines(chain.Where(segment => SameAsset(segment.Spline.AssetPath, selectedSpline.AssetPath))
                        .Select(segment => new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(segment.Tile, segment.Spline)));
                    RenderInspector();
                    SetStatus("Traffic rules: chose the " + (moreSplines.Count + 1) + " pieces of this road from junction or end to junction or end; Ctrl+click adds or takes away more.");
                })
                {
                    text = "Whole road", tooltip = "Chooses every piece of this road from junction or free end to junction or free end; Ctrl+click on the map adds or takes away pieces"
                };
                whole.AddToClassList("map-control-button");
                whole.AddToClassList("inspector-rule-whole");
                container.Add(whole);
                if (skipped > 0)
                {
                    var note = new Label(skipped + (skipped == 1 ? " spline chosen has" : " splines chosen have") + " another road type; its lanes are numbered otherwise, so it is left out.");
                    note.AddToClassList("panel-info");
                    container.Add(note);
                }
            }

            // Which road lanes: all, or single ones by their number in the list below.
            var chips = new VisualElement();
            chips.AddToClassList("panel-tabs");
            chips.AddToClassList("inspector-rule-lanes");
            var all = new Button(() =>
            {
                ruleEditorPaths.Clear();
                ruleEditorPaths.UnionWith(lanes);
                RenderInspector();
            }) { text = "All", tooltip = "Every road lane of it" };
            all.AddToClassList("panel-tab");
            all.EnableInClassList("panel-tab-active", ruleEditorPaths.SetEquals(lanes));
            chips.Add(all);
            foreach (var lane in lanes)
            {
                var each = lane;
                var chip = new Button(() =>
                {
                    if (ruleEditorPaths.SetEquals(lanes))
                        ruleEditorPaths.Clear();
                    if (!ruleEditorPaths.Remove(each))
                        ruleEditorPaths.Add(each);
                    if (ruleEditorPaths.Count == 0)
                        ruleEditorPaths.UnionWith(lanes);
                    RenderInspector();
                }) { text = lane.ToString(CultureInfo.InvariantCulture), tooltip = "Path " + lane + "; click more to change several" };
                chip.AddToClassList("panel-tab");
                chip.EnableInClassList("panel-tab-active", !ruleEditorPaths.SetEquals(lanes) && ruleEditorPaths.Contains(lane));
                chips.Add(chip);
            }
            container.Add(chips);

            // The values of the first lane chosen.
            var first = paths.First(path => path.Kind == 0 && path.PathIndex == ruleEditorPaths.Min());
            double? Rule(string name) => first.Rules.LastOrDefault(rule => !rule.Kills && string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                                                            (name != "trafficdensity" || Math.Abs(rule.Extra) < 0.5))?.Value;
            var limit = Rule("speedlimit");
            var density = Rule("trafficdensity");
            var level = Array.FindIndex(TrafficLevels, pair => pair.Value.HasValue == density.HasValue && (!density.HasValue || Math.Abs(pair.Value.Value - density.Value) < 1e-6));

            var limitRow = RuleRow(container, "Speed limit (km/h)");
            var limitField = new TextField
            {
                value = limit.HasValue ? limit.Value.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty,
                tooltip = "Empty for none; AI traffic then keeps to the speed of the road"
            };
            limitField.AddToClassList("picker-field");
            limitRow.Add(limitField);

            var trafficRow = RuleRow(container, "Traffic");
            var levels = new VisualElement();
            levels.AddToClassList("panel-tabs");
            levels.AddToClassList("picker-segmented");
            var chosenLevel = level;
            var levelButtons = new List<Button>();
            for (var index = 0; index < TrafficLevels.Length; index++)
            {
                var each = index;
                var button = new Button { text = TrafficLevels[index].Key };
                button.tooltip = TrafficLevels[index].Value.HasValue
                    ? "Random traffic " + TrafficLevels[index].Value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " times the normal ([rule] trafficdensity)"
                    : "The map's normal random traffic, without a rule";
                button.AddToClassList("panel-tab");
                button.clicked += () =>
                {
                    chosenLevel = each;
                    for (var other = 0; other < levelButtons.Count; other++)
                        levelButtons[other].EnableInClassList("panel-tab-active", other == chosenLevel);
                };
                button.EnableInClassList("panel-tab-active", index == level);
                levelButtons.Add(button);
                levels.Add(button);
            }
            trafficRow.Add(levels);

            var cars = RuleToggle(container, "AI cars", "AI cars may drive here; off writes [rule] no_cars, as on a bus lane", !Rule("no_cars").HasValue);
            var buses = RuleToggle(container, "AI buses", "Buses of the random traffic may drive here ([rule] bus); timetable buses always may", Rule("bus").HasValue);
            var trucks = RuleToggle(container, "AI trucks", "Trucks of the random traffic may drive here ([rule] trucks)", Rule("trucks").HasValue);

            var apply = new Button(() =>
            {
                double? newLimit = null;
                var text = limitField.value.Trim().Replace(',', '.');
                if (text.Length > 0)
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0.0)
                    {
                        SetStatus("Traffic rules: the speed limit must be a number of km/h above 0, or empty for none.");
                        return;
                    }
                    newLimit = parsed;
                }
                ApplyPathRules(tile, elementId, onSpline, new List<int>(ruleEditorPaths), newLimit,
                    chosenLevel >= 0 ? TrafficLevels[chosenLevel].Value : density, cars.value, buses.value, trucks.value);
            })
            {
                text = (ruleEditorPaths.Count == 1 ? "Apply to path " + ruleEditorPaths.Min() : "Apply to " + ruleEditorPaths.Count + " lanes") +
                       (targets != null && targets.Count > 1 ? " of " + targets.Count + " splines" : string.Empty),
                tooltip = "Writes the rules of the lanes chosen, marked orange on the map; Ctrl+Z undoes it"
            };
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("dialog-primary");
            apply.AddToClassList("inspector-rule-apply");
            container.Add(apply);

            // The lanes chosen, on every spline they apply to, marked on the map.
            var marked = mapPaths.FindAll(path => path.Kind == 0 && ruleEditorPaths.Contains(path.PathIndex) && path.OnSpline == onSpline &&
                                                  (onSpline
                                                      ? targets.Exists(target => target.Key.X == path.TileX && target.Key.Y == path.TileY && target.Value.Id == path.ElementId)
                                                      : path.TileX == tile.X && path.TileY == tile.Y && path.ElementId == elementId));
            mapCanvas?.SetHighlightedPaths(marked);
        }

        /// <summary>
        /// The splines the traffic rules go to: the selected one and those chosen with Ctrl+click of its road type, whose
        /// lanes are numbered alike; <paramref name="skipped"/> counts the others.
        /// </summary>
        private List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> RuleSplines(out int skipped)
        {
            skipped = 0;
            var splines = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
            if (selectedSpline == null || selectedSplineTile == null)
                return splines;
            splines.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(selectedSplineTile, selectedSpline));
            foreach (var pair in moreSplines)
                if (SameAsset(pair.Value.AssetPath, selectedSpline.AssetPath))
                    splines.Add(pair);
                else
                    skipped++;
            return splines;
        }

        private static VisualElement RuleRow(VisualElement container, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = new Label(label);
            text.AddToClassList("picker-option-label");
            row.Add(text);
            container.Add(row);
            return row;
        }

        private static Toggle RuleToggle(VisualElement container, string label, string tooltip, bool value)
        {
            var row = RuleRow(container, label);
            row.tooltip = tooltip;
            var toggle = new Toggle { value = value, tooltip = tooltip };
            toggle.AddToClassList("picker-option-toggle");
            row.Add(toggle);
            return toggle;
        }

        /// <summary>For command-line checks: writes traffic rules of the selected object's or spline's road lanes from six arguments.</summary>
        private void RunPathRulesArgument(string[] args, int at)
        {
            if (selectedTile == null || mapPaths == null || selectedObject == null && selectedSpline == null)
            {
                SetStatus("Traffic rules: select an object or a spline with -select first.");
                return;
            }
            var onSpline = selectedObject == null;
            var id = selectedObject?.Id ?? selectedSpline.Id;
            var lanes = mapPaths.Where(path => path.TileX == selectedTile.X && path.TileY == selectedTile.Y && path.ElementId == id && path.OnSpline == onSpline &&
                                               path.Kind == 0).Select(path => path.PathIndex).Distinct().ToList();
            if (args[at] != "all")
                lanes = args[at].Split(',').Select(text => int.Parse(text, CultureInfo.InvariantCulture)).Where(lanes.Contains).ToList();
            double? Number(string text) => text == "-" ? (double?)null : double.Parse(text, CultureInfo.InvariantCulture);
            // The editor shows the lanes chosen, as clicking them would.
            ruleEditorElement = selectedTile.X + "|" + selectedTile.Y + "|" + id + "|" + onSpline;
            ruleEditorPaths.Clear();
            ruleEditorPaths.UnionWith(lanes);
            ApplyPathRules(selectedTile, id, onSpline, lanes, Number(args[at + 1]), Number(args[at + 2]), args[at + 3] == "1", args[at + 4] == "1", args[at + 5] == "1");
        }

        /// <summary>Writes the traffic rules of road lanes of an object or spline as one undo step.</summary>
        private void ApplyPathRules(OmsiTileSummary tile, int elementId, bool onSpline, List<int> lanes, double? limit, double? density, bool cars, bool buses, bool trucks)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("change traffic rules"));
                return;
            }
            var rules = new List<OmsiPathRule>();
            foreach (var lane in lanes)
            {
                if (limit.HasValue)
                    rules.Add(new OmsiPathRule(lane, "speedlimit", limit.Value));
                if (density.HasValue)
                    rules.Add(new OmsiPathRule(lane, "trafficdensity", density.Value));
                if (!cars)
                    rules.Add(new OmsiPathRule(lane, "no_cars", 0));
                if (buses)
                    rules.Add(new OmsiPathRule(lane, "bus", 0));
                if (trucks)
                    rules.Add(new OmsiPathRule(lane, "trucks", 0));
            }
            var names = OmsiMapSession.TrafficRuleNames.ToList();
            var what = (lanes.Count == 1 ? "path " + lanes[0] : lanes.Count + " lanes") + " of " + (onSpline ? "spline #" : "object #") + elementId;
            try
            {
                if (onSpline)
                {
                    var targets = RuleSplines(out _);
                    if (targets.Count == 0)
                        return;
                    var edits = new List<OmsiSplinePathRules>();
                    foreach (var target in targets)
                    {
                        var edit = new OmsiSplinePathRules { Tile = target.Key, Spline = target.Value, Paths = lanes, Names = names };
                        edit.Rules.AddRange(rules);
                        edits.Add(edit);
                    }
                    if (targets.Count > 1)
                        what = (lanes.Count == 1 ? "path " + lanes[0] : lanes.Count + " lanes") + " of " + targets.Count + " splines";
                    var change = session.SetSplinePathRules(currentSummary, edits);
                    if (change == null)
                    {
                        SetStatus("Traffic rules: " + what + " already have these rules.");
                        return;
                    }
                    // The splines are read again; the selection finds them by their IDs.
                    RefreshSplineViews(change);
                    PushHistory(new ObjectEditStep { SplineChange = change, Description = "the traffic rules of " + what });
                }
                else
                {
                    var record = tile.Objects.Find(candidate => candidate.Id == elementId);
                    if (record == null)
                        return;
                    var change = session.SetObjectPathRules(tile, record, lanes, names, rules);
                    if (change == null)
                    {
                        SetStatus("Traffic rules: " + what + " already have these rules.");
                        return;
                    }
                    PushHistory(new ObjectEditStep { TileX = tile.X, TileY = tile.Y, ObjectId = record.Id, Change = change, Description = "the traffic rules of " + what });
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.IO.IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("Traffic rules could not be written: " + exception.Message);
                return;
            }
            RefreshAfterChange(null);
            SetStatus("Traffic rules of " + what + ": " + (limit.HasValue ? limit.Value.ToString("0.#", CultureInfo.InvariantCulture) + " km/h" : "no speed limit") +
                      ", " + (density.HasValue ? "traffic " + density.Value.ToString("0.###", CultureInfo.InvariantCulture) : "normal traffic") +
                      (cars ? string.Empty : ", no AI cars") + (buses ? ", AI buses" : string.Empty) + (trucks ? ", AI trucks" : string.Empty) + ". Ctrl+Z undoes it.");
        }

        /// <summary>One path of a kind, as the status and the inspector name it.</summary>
        private static string PathKindNoun(int kind)
        {
            return kind == 1 ? "footpath" : kind == 2 ? "track" : "road lane";
        }

        private static string DescribeRule(OmsiPathRule rule)
        {
            var value = rule.Value.ToString("0.###", CultureInfo.InvariantCulture);
            switch (rule.Name.ToLowerInvariant())
            {
                case "priority":
                    return "priority " + value + (rule.Value > 128.5 ? " (right of way)" : rule.Value < 127.5 ? " (gives way)" : string.Empty);
                case "speedlimit":
                    return "speed limit " + value + " km/h";
                case "trafficdensity":
                    return "traffic density " + value + " for group " + rule.Extra.ToString("0", CultureInfo.InvariantCulture);
                default:
                    return rule.Name + " " + value;
            }
        }
    }
}
