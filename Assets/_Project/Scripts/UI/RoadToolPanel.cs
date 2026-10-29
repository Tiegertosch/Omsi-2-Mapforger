using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The road tool's panels, laid out as in Cities: Skylines: on the left the options (heights, terrain, junctions,
    /// piece length, gradient warning), beside them the road picker with tabs by kind of road, a search and a tile with a
    /// preview for each spline profile (.sli). The profiles are sorted into tabs and their previews drawn from their
    /// textures on worker threads. The shell plans and builds the road; the panel only holds the settings and shows the plan.
    /// </summary>
    internal sealed class RoadToolPanel : VisualElement
    {
        private enum Tab { InMap, Favorites, MyRoads, Category }

        private const string FavoritesKey = "Mapforge.RoadFavorites";
        private const int PreviewWidth = 96;
        private const int PreviewHeight = 64;

        private readonly TextField search;
        private readonly VisualElement tabRow;
        private readonly ScrollView tiles;
        private readonly Label emptyLabel;
        private readonly Button straightButton;
        private readonly Button curvedButton;
        private readonly Button freeButton;
        private readonly Button snapRoadsButton;
        private readonly Button snapAnglesButton;
        private readonly Button snapGuidesButton;
        private readonly Button followButton;
        private readonly Button smoothButton;
        private readonly Button manualButton;
        private readonly TextField cornerRadiusField;
        private readonly Button parallelOffButton;
        private readonly Button parallelLeftButton;
        private readonly Button parallelRightButton;
        private readonly Button parallelBothButton;
        private readonly TextField parallelDistanceField;
        private readonly Toggle parallelOppositeToggle;
        private readonly TextField endHeightField;
        private readonly TextField pieceLengthField;
        private readonly TextField gradientField;
        private readonly Label profileLabel;
        private readonly Toggle fitTerrainToggle;
        private readonly Toggle junctionsToggle;
        private readonly Label planLabel;
        private readonly List<string> profiles = new List<string>();
        private readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VisualElement> tileByPath = new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Button, KeyValuePair<Tab, OmsiSplineCategory>> tabButtons = new Dictionary<Button, KeyValuePair<Tab, OmsiSplineCategory>>();
        private IReadOnlyDictionary<string, int> used = new Dictionary<string, int>();
        private Dictionary<string, OmsiSplineCategory> categories;
        private string installationRoot;
        private Tab tab = Tab.InMap;
        private OmsiSplineCategory category = OmsiSplineCategory.TwoLanes;
        private bool tabChosen;

        // Previews are drawn one after another on a worker thread; finished pixels wait here for the main thread.
        private readonly object previewLock = new object();
        private readonly Queue<string> previewQueue = new Queue<string>();
        private readonly HashSet<string> previewRequested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, byte[]>> previewsDone = new List<KeyValuePair<string, byte[]>>();
        private Task previewWorker;
        private Task<Dictionary<string, OmsiSplineCategory>> sorting;

        public RoadToolPanel()
        {
            AddToClassList("picker-tool");
            LoadFavorites();

            // The options on the left.
            var options = new VisualElement();
            options.AddToClassList("tool-panel");
            options.AddToClassList("picker-options");
            var optionsTitle = new Label("Options");
            optionsTitle.AddToClassList("picker-options-title");
            options.Add(optionsTitle);

            // How the road runs to the click (ADR 043), as the building modes of Cities: Skylines.
            var shapes = new VisualElement();
            shapes.AddToClassList("panel-tabs");
            shapes.AddToClassList("picker-segmented");
            straightButton = new Button(() => SetShape(OmsiRoadShape.Straight))
            {
                text = "Straight", tooltip = "Each click ends a straight piece of road, whatever way the road before it runs."
            };
            curvedButton = new Button(() => SetShape(OmsiRoadShape.Curved))
            {
                text = "Curved",
                tooltip = "Click a control point, then the end: the road leaves towards the control point and arrives coming from it. " +
                          "Ending on a road it meets it in its direction, with an S curve where needed."
            };
            freeButton = new Button(() => SetShape(OmsiRoadShape.Free))
            {
                text = "Free", tooltip = "Each click bends the road on from the way it runs, without a kink."
            };
            foreach (var button in new[] { straightButton, curvedButton, freeButton })
            {
                button.AddToClassList("panel-tab");
                shapes.Add(button);
            }
            Row(options, "Mode").Add(shapes);

            var snaps = new VisualElement();
            snaps.AddToClassList("panel-tabs");
            snaps.AddToClassList("picker-segmented");
            snapRoadsButton = new Button(() => SetSnapping(!SnapToRoads, SnapToAngles, SnapToGuides))
            {
                text = "Roads", tooltip = "Roads start and end on free spline ends and on free connection points of junctions near the pointer."
            };
            snapAnglesButton = new Button(() => SetSnapping(SnapToRoads, !SnapToAngles, SnapToGuides))
            {
                text = "90°",
                tooltip = "Directions snap to right angles: to the road a new road continues or to a road near its start, else to north. " +
                          "Free roads snap to end straight on or square to their start."
            };
            snapGuidesButton = new Button(() => SetSnapping(SnapToRoads, SnapToAngles, !SnapToGuides))
            {
                text = "Guides",
                tooltip = "Free spline ends and connection points of junctions nearby cast guide lines: the road's extension and the line square to it. " +
                          "The pointer snaps onto them and onto where two cross, also where they cross a right-angle direction."
            };
            foreach (var button in new[] { snapRoadsButton, snapAnglesButton, snapGuidesButton })
            {
                button.AddToClassList("panel-tab");
                snaps.Add(button);
            }
            Row(options, "Snap").Add(snaps);
            cornerRadiusField = Field(Row(options, "Corner radius (m)"), "15",
                "Straight: where a road turns off the road it continues, the corner is rounded with this radius; that road is cut back for it. " +
                "0 leaves a sharp corner. Only roads built in this project are cut back.");

            // Roads beside the one being drawn, at a distance (ADR 043).
            var parallels = new VisualElement();
            parallels.AddToClassList("panel-tabs");
            parallels.AddToClassList("picker-segmented");
            parallelOffButton = new Button(() => SetParallel(false, false)) { text = "Off", tooltip = "Build only the road you draw." };
            parallelLeftButton = new Button(() => SetParallel(true, false)) { text = "Left", tooltip = "Build a second road at the distance below, left of the road you draw." };
            parallelRightButton = new Button(() => SetParallel(false, true)) { text = "Right", tooltip = "Build a second road at the distance below, right of the road you draw." };
            parallelBothButton = new Button(() => SetParallel(true, true)) { text = "Both", tooltip = "Build a road on each side at the distance below." };
            foreach (var button in new[] { parallelOffButton, parallelLeftButton, parallelRightButton, parallelBothButton })
            {
                button.AddToClassList("panel-tab");
                parallels.Add(button);
            }
            Row(options, "Parallel").Add(parallels);
            parallelDistanceField = Field(Row(options, "Distance (m)"), "15",
                "How far the parallel roads lie from the road you draw, centre line to centre line, in the same profile");
            parallelOppositeToggle = new Toggle
            {
                value = false,
                tooltip = "The parallel roads run the other way, as the other carriageway of a dual road with one-way profiles"
            };
            parallelOppositeToggle.AddToClassList("picker-option-toggle");
            parallelOppositeToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row(options, "Opposite way").Add(parallelOppositeToggle);

            var heightRow = Row(options, "Height");
            var modes = new VisualElement();
            modes.AddToClassList("panel-tabs");
            modes.AddToClassList("picker-segmented");
            followButton = new Button(() => SetHeightMode(OmsiRoadHeightMode.FollowTerrain))
            {
                text = "Terrain", tooltip = "Every piece of the road starts and ends on the terrain; the terrain itself is not changed."
            };
            smoothButton = new Button(() => SetHeightMode(OmsiRoadHeightMode.Smooth))
            {
                text = "Smooth",
                tooltip = "Follows the terrain averaged over 100 m, with the gradient changing steadily and no kinks, also where the road continues another. " +
                          "With Fit terrain the ground is cut or banked up to the road."
            };
            manualButton = new Button(() => SetHeightMode(OmsiRoadHeightMode.Manual))
            {
                text = "Manual",
                tooltip = "The height runs evenly from the start to the end height below, whatever the terrain does. " +
                          "Switch off Fit terrain for bridges and overpasses, so the ground stays as it is."
            };
            foreach (var button in new[] { followButton, smoothButton, manualButton })
            {
                button.AddToClassList("panel-tab");
                modes.Add(button);
            }
            heightRow.Add(modes);

            endHeightField = Field(Row(options, "End height (m)"), "0", "Height at the end of each new piece of road in manual mode");
            pieceLengthField = Field(Row(options, "Piece length (m)"), "25", "Longer roads are split into splines of at most this length, so they can follow the terrain");
            gradientField = Field(Row(options, "Warn above (%)"), "10", "Gradients steeper than this are marked; a check, not a rule");

            fitTerrainToggle = new Toggle
            {
                value = true,
                tooltip = "Lays the ground under the road and a shoulder of about 7 m beside it just below kerb height, as OMSI cuts it away " +
                          "under the road, and cuts or banks up slopes of 1:1.5 beyond. " +
                          "The original terrain stays in the original map; deleting the road brings it back."
            };
            fitTerrainToggle.AddToClassList("picker-option-toggle");
            Row(options, "Fit terrain").Add(fitTerrainToggle);

            junctionsToggle = new Toggle
            {
                value = true,
                tooltip = "A road ending on another road joins it with a new T junction; a road crossing another gets crossroads there; " +
                          "a road joining one of another width gets a transition. They are made in the profiles of both roads and stored " +
                          "under Sceneryobjects\\Mapforge in your OMSI installation."
            };
            junctionsToggle.AddToClassList("picker-option-toggle");
            junctionsToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row(options, "Make junctions").Add(junctionsToggle);
            Add(options);

            // The picker beside them.
            var picker = new VisualElement();
            picker.AddToClassList("tool-panel");
            picker.AddToClassList("picker-panel");
            var header = new VisualElement();
            header.AddToClassList("tool-panel-header");
            header.AddToClassList("picker-header");
            var badge = new VisualElement();
            badge.AddToClassList("tool-panel-badge");
            badge.Add(new IconElement("spline"));
            header.Add(badge);
            var titles = new VisualElement();
            titles.AddToClassList("tool-panel-titles");
            profileLabel = new Label("Road");
            profileLabel.AddToClassList("tool-panel-title");
            profileLabel.AddToClassList("picker-title");
            titles.Add(profileLabel);
            planLabel = new Label("Click the points of a road; Esc ends it");
            planLabel.AddToClassList("tool-panel-subtitle");
            planLabel.AddToClassList("picker-subtitle");
            titles.Add(planLabel);
            header.Add(titles);
            var newType = new Button(() => NewRoadTypeRequested?.Invoke())
            {
                tooltip = "Opens the road builder: put lanes together into a road type of your own (ADR 049)"
            };
            newType.AddToClassList("map-control-button");
            newType.AddToClassList("road-picker-new");
            newType.Add(new IconElement("plus"));
            newType.Add(new Label("New road type..."));
            header.Add(newType);
            var searchRow = new VisualElement();
            searchRow.AddToClassList("panel-search");
            searchRow.AddToClassList("picker-search");
            search = new TextField();
            search.AddToClassList("panel-search-field");
            search.textEdition.placeholder = "Search all roads...";
            search.RegisterValueChangedCallback(_ => Refresh());
            searchRow.Add(search);
            var searchIcon = new IconElement("search");
            searchIcon.AddToClassList("panel-search-icon");
            searchRow.Add(searchIcon);
            header.Add(searchRow);
            var close = new Button(() => CloseRequested?.Invoke()) { tooltip = "Close the road tool (R)" };
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            picker.Add(header);

            tabRow = new VisualElement();
            tabRow.AddToClassList("panel-tabs");
            tabRow.AddToClassList("picker-tabs");
            AddTab(Tab.InMap, OmsiSplineCategory.Other, "In map", "Profiles this map uses, most used first");
            AddTab(Tab.Favorites, OmsiSplineCategory.Other, "Favorites", "Profiles you marked with the star");
            AddTab(Tab.MyRoads, OmsiSplineCategory.Other, "My roads", "Road types you made with the road builder, kept under Splines\\Mapforge");
            foreach (OmsiSplineCategory each in Enum.GetValues(typeof(OmsiSplineCategory)))
                AddTab(Tab.Category, each, OmsiSplineCategories.Name(each), TabTooltip(each));
            picker.Add(tabRow);

            // The scroll bar always shows, so the tiles keep their places whatever the tab holds.
            tiles = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible, horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            tiles.AddToClassList("picker-tiles");
            tiles.contentContainer.AddToClassList("picker-tiles-content");
            picker.Add(tiles);
            emptyLabel = new Label();
            emptyLabel.AddToClassList("panel-info");
            emptyLabel.AddToClassList("picker-empty");
            picker.Add(emptyLabel);
            Add(picker);

            SetHeightMode(OmsiRoadHeightMode.FollowTerrain);
            SetShape(OmsiRoadShape.Free);
            SetSnapping(true, true, true);
            SetParallel(false, false);
            schedule.Execute(TakePreviews).Every(100);
        }

        /// <summary>The panel wants to close the road tool.</summary>
        public event Action CloseRequested;

        /// <summary>New road type... was clicked: the road builder should open.</summary>
        public event Action NewRoadTypeRequested;

        /// <summary>A setting changed; the preview should be planned again.</summary>
        public event Action SettingsChanged;

        public string Profile { get; private set; }

        public OmsiRoadHeightMode HeightMode { get; private set; }

        public OmsiRoadShape Shape { get; private set; }

        /// <summary>True when roads start and end on free spline ends and connection points near the pointer.</summary>
        public bool SnapToRoads { get; private set; }

        /// <summary>True when road directions snap to right angles.</summary>
        public bool SnapToAngles { get; private set; }

        /// <summary>True when the pointer snaps onto guide lines of the roads nearby.</summary>
        public bool SnapToGuides { get; private set; }

        /// <summary>The mode changed; a road being drawn starts its next part anew.</summary>
        public event Action ShapeChanged;

        /// <summary>True when the terrain should be fitted to new roads.</summary>
        public bool FitTerrain => fitTerrainToggle.value;

        /// <summary>True when roads meeting other roads should be joined with new junctions (ADR 028).</summary>
        public bool MakeJunctions => junctionsToggle.value;

        /// <summary>The end height for manual mode, or null while the field holds no number.</summary>
        public double? EndHeight => Number(endHeightField, double.MinValue);

        public double PieceLength => Number(pieceLengthField, 1.0) ?? 25.0;

        /// <summary>True when a road is built beside the drawn one on its left; the same for the right.</summary>
        public bool ParallelLeft { get; private set; }

        public bool ParallelRight { get; private set; }

        /// <summary>The distance of parallel roads, centre line to centre line; 0 while the field holds no number.</summary>
        public double ParallelDistance => Number(parallelDistanceField, 0.0) ?? 0.0;

        /// <summary>True when parallel roads run the other way.</summary>
        public bool ParallelOpposite => parallelOppositeToggle.value;

        /// <summary>The radius Straight roads round the corner with where they turn off the road they continue; 0 for none.</summary>
        public double CornerRadius => Number(cornerRadiusField, 0.0) ?? 0.0;

        public double GradientWarningPercent => Number(gradientField, 0.0) ?? 10.0;

        /// <summary>The OMSI installation whose profiles and textures the picker reads; read again when another opens.</summary>
        public void SetInstallation(string root)
        {
            if (string.Equals(root, installationRoot, StringComparison.OrdinalIgnoreCase))
                return;
            installationRoot = root;
            categories = null;
            sorting = null;
            lock (previewLock)
            {
                previewQueue.Clear();
                previewRequested.Clear();
                previewsDone.Clear();
            }
            foreach (var texture in previews.Values)
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            previews.Clear();
        }

        /// <summary>
        /// The profiles to offer: those of the installation and those the map uses, with their use counts. The most used
        /// profile of the map is chosen unless one is chosen already. The profiles are sorted into tabs in the background.
        /// </summary>
        public void SetProfiles(IEnumerable<string> installationProfiles, IReadOnlyDictionary<string, int> mapProfiles)
        {
            used = mapProfiles ?? new Dictionary<string, int>();
            profiles.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inMap = new List<KeyValuePair<string, int>>(used);
            inMap.Sort((left, right) => right.Value != left.Value ? right.Value.CompareTo(left.Value) : string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));
            foreach (var pair in inMap)
                if (seen.Add(pair.Key))
                    profiles.Add(pair.Key);
            if (installationProfiles != null)
                foreach (var path in installationProfiles)
                    if (seen.Add(path))
                        profiles.Add(path);
            if (Profile == null && profiles.Count > 0)
                SetProfile(profiles[0]);
            if (!tabChosen)
                tab = used.Count > 0 ? Tab.InMap : Tab.Category;
            StartSorting();
            Refresh();
        }

        /// <summary>Fills the end height field, such as with the height where the road would start.</summary>
        public void SuggestEndHeight(double height)
        {
            endHeightField.SetValueWithoutNotify(height.ToString("0.00", CultureInfo.InvariantCulture));
        }

        /// <summary>What the road under the pointer would be, or the next step to take.</summary>
        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.tooltip = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
        }

        /// <summary>Chooses a profile by its path relative to the installation, as a click on its tile does; for command-line checks.</summary>
        public void ChooseProfile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                SetProfile(path.Trim());
        }

        /// <summary>Shows a tab by its name, such as "Median" or "In map"; for command-line checks.</summary>
        public void ShowTab(string name)
        {
            foreach (var pair in tabButtons)
                if (string.Equals(pair.Key.text, name, StringComparison.OrdinalIgnoreCase))
                {
                    ChooseTab(pair.Value.Key, pair.Value.Value);
                    return;
                }
        }

        public void SetShape(OmsiRoadShape shape)
        {
            Shape = shape;
            straightButton.EnableInClassList("panel-tab-active", shape == OmsiRoadShape.Straight);
            cornerRadiusField.parent.SetEnabled(shape == OmsiRoadShape.Straight);
            curvedButton.EnableInClassList("panel-tab-active", shape == OmsiRoadShape.Curved);
            freeButton.EnableInClassList("panel-tab-active", shape == OmsiRoadShape.Free);
            ShapeChanged?.Invoke();
            SettingsChanged?.Invoke();
        }

        public void SetParallel(bool left, bool right)
        {
            ParallelLeft = left;
            ParallelRight = right;
            parallelOffButton.EnableInClassList("panel-tab-active", !left && !right);
            parallelLeftButton.EnableInClassList("panel-tab-active", left && !right);
            parallelRightButton.EnableInClassList("panel-tab-active", right && !left);
            parallelBothButton.EnableInClassList("panel-tab-active", left && right);
            parallelDistanceField.parent.SetEnabled(left || right);
            parallelOppositeToggle.parent.SetEnabled(left || right);
            SettingsChanged?.Invoke();
        }

        /// <summary>Sets the distance of parallel roads and whether they run the other way; for command-line checks.</summary>
        public void SetParallelOptions(double distance, bool opposite)
        {
            parallelDistanceField.value = distance.ToString(CultureInfo.InvariantCulture);
            parallelOppositeToggle.value = opposite;
        }

        public void SetSnapping(bool roads, bool angles, bool guides)
        {
            SnapToRoads = roads;
            SnapToAngles = angles;
            SnapToGuides = guides;
            snapRoadsButton.EnableInClassList("panel-tab-active", roads);
            snapAnglesButton.EnableInClassList("panel-tab-active", angles);
            snapGuidesButton.EnableInClassList("panel-tab-active", guides);
            SettingsChanged?.Invoke();
        }

        public void SetHeightMode(OmsiRoadHeightMode mode)
        {
            HeightMode = mode;
            followButton.EnableInClassList("panel-tab-active", mode == OmsiRoadHeightMode.FollowTerrain);
            smoothButton.EnableInClassList("panel-tab-active", mode == OmsiRoadHeightMode.Smooth);
            manualButton.EnableInClassList("panel-tab-active", mode == OmsiRoadHeightMode.Manual);
            endHeightField.parent.SetEnabled(mode == OmsiRoadHeightMode.Manual);
            SettingsChanged?.Invoke();
        }

        private void SetProfile(string path)
        {
            if (Profile != null && tileByPath.TryGetValue(Profile, out var previous))
                previous.RemoveFromClassList("picker-tile-selected");
            Profile = path;
            used.TryGetValue(path, out var count);
            profileLabel.text = Path.GetFileNameWithoutExtension(path) + (count > 0 ? "  ·  " + count + " in this map" : string.Empty);
            profileLabel.tooltip = path;
            if (tileByPath.TryGetValue(path, out var tile))
                tile.AddToClassList("picker-tile-selected");
            SettingsChanged?.Invoke();
        }

        private static VisualElement Row(VisualElement parent, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = new Label(label);
            text.AddToClassList("picker-option-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }

        private TextField Field(VisualElement row, string value, string tooltip)
        {
            var field = new TextField { value = value, tooltip = tooltip };
            field.AddToClassList("picker-field");
            field.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            row.tooltip = tooltip;
            row.Add(field);
            return field;
        }

        private static double? Number(TextField field, double minimum)
        {
            return double.TryParse(field.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= minimum &&
                   !double.IsInfinity(value)
                ? value
                : (double?)null;
        }

        private void AddTab(Tab kind, OmsiSplineCategory each, string text, string tooltip)
        {
            var button = new Button { text = text, tooltip = tooltip };
            button.AddToClassList("panel-tab");
            button.AddToClassList("picker-tab");
            button.clicked += () => ChooseTab(kind, each);
            tabButtons.Add(button, new KeyValuePair<Tab, OmsiSplineCategory>(kind, each));
            tabRow.Add(button);
        }

        private static string TabTooltip(OmsiSplineCategory each)
        {
            switch (each)
            {
                case OmsiSplineCategory.OneWay: return "Roads whose lanes all run one way, also one carriageway of a dual road";
                case OmsiSplineCategory.TwoLanes: return "Roads with one or two lanes, both ways";
                case OmsiSplineCategory.FourLanes: return "Roads with three or four lanes";
                case OmsiSplineCategory.SixLanes: return "Roads with five or more lanes";
                case OmsiSplineCategory.Median: return "Roads with a median between their carriageways";
                case OmsiSplineCategory.Rail: return "Rails and tram tracks";
                case OmsiSplineCategory.Path: return "Footpaths and pavements";
                default: return "Wires, fences, walls and helpers only the OMSI editor shows";
            }
        }

        private void ChooseTab(Tab kind, OmsiSplineCategory each)
        {
            tab = kind;
            category = each;
            tabChosen = true;
            search.SetValueWithoutNotify(string.Empty);
            Refresh();
        }

        /// <summary>Sorts the profiles into their tabs on a worker thread, once per installation and list of profiles.</summary>
        private void StartSorting()
        {
            if (installationRoot == null || profiles.Count == 0)
                return;
            if (categories != null && profiles.TrueForAll(categories.ContainsKey))
                return;
            if (sorting != null && !sorting.IsCompleted)
                return;
            var root = installationRoot;
            var paths = new List<string>(profiles);
            var known = categories;
            var task = Task.Run(() =>
            {
                var catalog = new OmsiSplineProfileCatalog(root);
                var result = known != null
                    ? new Dictionary<string, OmsiSplineCategory>(known, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, OmsiSplineCategory>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in paths)
                    if (!result.ContainsKey(path))
                        result[path] = OmsiSplineCategories.Of(catalog.Get(path));
                return result;
            });
            sorting = task;
            IVisualElementScheduledItem poll = null;
            poll = schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                if (!ReferenceEquals(task, sorting))
                    return;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    categories = new Dictionary<string, OmsiSplineCategory>(StringComparer.OrdinalIgnoreCase);
                }
                else
                    categories = task.Result;
                if (!tabChosen && tab == Tab.Category && Profile != null && categories.TryGetValue(Profile, out var chosen))
                    category = chosen;
                Refresh();
            }).Every(100);
        }

        /// <summary>Builds the tiles of the current tab, or of every profile matching the search.</summary>
        private void Refresh()
        {
            var text = search.value?.Trim() ?? string.Empty;
            foreach (var pair in tabButtons)
                pair.Key.EnableInClassList("panel-tab-active", text.Length == 0 && pair.Value.Key == tab && (tab != Tab.Category || pair.Value.Value == category));
            tiles.Clear();
            tileByPath.Clear();
            var shown = new List<string>();
            foreach (var path in profiles)
            {
                if (text.Length > 0)
                {
                    if (path.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        shown.Add(path);
                    continue;
                }
                if (tab == Tab.InMap && used.ContainsKey(path) || tab == Tab.Favorites && favorites.Contains(path) || tab == Tab.MyRoads && OmsiRoadTypeStore.IsMade(path) ||
                    tab == Tab.Category && categories != null && categories.TryGetValue(path, out var each) && each == category)
                    shown.Add(path);
            }
            foreach (var path in shown)
                tiles.Add(MakeTile(path));
            emptyLabel.text = shown.Count > 0 ? string.Empty
                : text.Length > 0 ? "No profile matches \"" + text + "\"."
                : tab == Tab.Category && categories == null ? "Sorting the profiles of the installation..."
                : tab == Tab.InMap ? "This map uses no spline profiles yet."
                : tab == Tab.Favorites ? "Mark profiles with the star to find them here."
                : tab == Tab.MyRoads ? "Road types you make with New road type... show here."
                : "No profile of this kind in the installation.";
            // The empty note lies over the tile area, which keeps its size.
            emptyLabel.style.display = shown.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement MakeTile(string path)
        {
            var tile = new VisualElement { tooltip = path };
            tile.AddToClassList("picker-tile");
            if (string.Equals(path, Profile, StringComparison.OrdinalIgnoreCase))
                tile.AddToClassList("picker-tile-selected");
            var preview = new VisualElement { name = "preview" };
            preview.AddToClassList("picker-tile-preview");
            if (previews.TryGetValue(path, out var texture) && texture != null)
                preview.style.backgroundImage = new StyleBackground(texture);
            else
                RequestPreview(path);
            tile.Add(preview);
            var name = new Label(Path.GetFileNameWithoutExtension(path));
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            if (used.TryGetValue(path, out var count) && count > 0)
            {
                var badge = new Label(count.ToString(CultureInfo.InvariantCulture)) { tooltip = count + " splines of this map use it" };
                badge.AddToClassList("picker-tile-count");
                tile.Add(badge);
            }
            var star = new Button { tooltip = favorites.Contains(path) ? "Remove from favorites" : "Add to favorites" };
            star.AddToClassList("panel-icon-button");
            star.AddToClassList("favorite-button");
            star.AddToClassList("picker-tile-star");
            star.EnableInClassList("favorite-on", favorites.Contains(path));
            star.Add(new IconElement("star"));
            star.clicked += () =>
            {
                if (!favorites.Remove(path))
                    favorites.Add(path);
                SaveFavorites();
                Refresh();
            };
            tile.Add(star);
            tile.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement target && (target == star || star.Contains(target)))
                    return;
                SetProfile(path);
            });
            tileByPath[path] = tile;
            return tile;
        }

        /// <summary>Queues a profile's preview; a single worker draws them in turn, reading each texture once.</summary>
        private void RequestPreview(string path)
        {
            if (installationRoot == null)
                return;
            lock (previewLock)
            {
                if (!previewRequested.Add(path))
                    return;
                previewQueue.Enqueue(path);
                if (previewWorker != null && !previewWorker.IsCompleted)
                    return;
            }
            var root = installationRoot;
            previewWorker = Task.Run(() => DrawPreviews(root));
        }

        private void DrawPreviews(string root)
        {
            var catalog = new OmsiSplineProfileCatalog(root);
            var images = new Dictionary<string, OmsiImage>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                string path;
                lock (previewLock)
                {
                    if (previewQueue.Count == 0 || !string.Equals(root, installationRoot, StringComparison.OrdinalIgnoreCase))
                        return;
                    path = previewQueue.Dequeue();
                }
                byte[] pixels = null;
                try
                {
                    var profile = catalog.Get(path);
                    if (profile != null)
                        pixels = OmsiSplineThumbnail.Render(profile, texture => ReadTexture(root, texture, images), PreviewWidth, PreviewHeight);
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException ||
                                                  exception is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Could not draw the preview of " + path + ": " + exception.Message);
                }
                lock (previewLock)
                    previewsDone.Add(new KeyValuePair<string, byte[]>(path, pixels));
            }
        }

        /// <summary>A texture of the installation, decoded once and reduced, or null when it cannot be read.</summary>
        private static OmsiImage ReadTexture(string root, string relativePath, Dictionary<string, OmsiImage> images)
        {
            if (images.TryGetValue(relativePath, out var cached))
                return cached;
            OmsiImage image = null;
            try
            {
                var file = OmsiRelativePath.Resolve(root, relativePath);
                // OMSI prefers a .dds of the same name.
                var dds = Path.ChangeExtension(file, ".dds");
                var bytes = File.Exists(dds) ? File.ReadAllBytes(dds) : File.Exists(file) ? File.ReadAllBytes(file) : null;
                if (bytes != null && OmsiImageReader.TryDecode(bytes, out var decoded))
                    image = decoded.Reduce(256);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException ||
                                              exception is UnauthorizedAccessException)
            {
                image = null;
            }
            images[relativePath] = image;
            return image;
        }

        /// <summary>Turns finished previews into textures on the main thread and shows them on their tiles.</summary>
        private void TakePreviews()
        {
            List<KeyValuePair<string, byte[]>> done;
            lock (previewLock)
            {
                if (previewsDone.Count == 0)
                    return;
                done = new List<KeyValuePair<string, byte[]>>(previewsDone);
                previewsDone.Clear();
            }
            foreach (var pair in done)
            {
                Texture2D texture = null;
                if (pair.Value != null)
                {
                    // The pixels run from the top row down; Unity textures from the bottom row up.
                    var flipped = new byte[pair.Value.Length];
                    var stride = PreviewWidth * 4;
                    for (var row = 0; row < PreviewHeight; row++)
                        Buffer.BlockCopy(pair.Value, row * stride, flipped, (PreviewHeight - 1 - row) * stride, stride);
                    texture = new Texture2D(PreviewWidth, PreviewHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    texture.LoadRawTextureData(flipped);
                    texture.Apply(false, true);
                }
                previews[pair.Key] = texture;
                if (texture != null && tileByPath.TryGetValue(pair.Key, out var tile))
                    tile.Q<VisualElement>("preview").style.backgroundImage = new StyleBackground(texture);
            }
        }

        private void LoadFavorites()
        {
            favorites.Clear();
            foreach (var path in PlayerPrefs.GetString(FavoritesKey, string.Empty).Split('\n'))
                if (path.Trim().Length > 0)
                    favorites.Add(path.Trim());
        }

        private void SaveFavorites()
        {
            PlayerPrefs.SetString(FavoritesKey, string.Join("\n", favorites));
            PlayerPrefs.Save();
        }
    }
}
