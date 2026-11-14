using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.Core.Localization;
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
        private readonly ToolCatalog catalog;
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
        private readonly Toggle fitTerrainToggle;
        private readonly Toggle junctionsToggle;
        private readonly Toggle crossingToggle;
        private readonly Toggle parkingToggle;
        private readonly IntegerField parkingListField;
        private readonly FloatField pathWidthField;
        private bool? fitBeforePaths;
        private readonly Toggle pathBothWaysToggle;
        private readonly VisualElement pathWidthRow;
        private readonly VisualElement buildRow;
        private readonly VisualElement pathBothWaysRow;
        private string planText;
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
            // The row is as tall as its tallest panel and the shorter ones stand at its bottom: the empty part above them must not catch clicks meant for the map.
            pickingMode = PickingMode.Ignore;
            LoadFavorites();

            // The options on the left, in rows of icons that all fit at once (ADR 098, stage 2): mode, snapping, height, parallel roads,
            // what a click builds, and the pieces. Name and explanation of each control stand in its tooltip.
            var options = ToolCatalog.Options(icons: true);
            optionsColumn = options;

            // How the road runs to the click (ADR 043), as the building modes of Cities: Skylines.
            var modeRow = OptionControls.Row(options, "roadtool.mode");
            var shapes = OptionControls.Group(modeRow);
            straightButton = OptionControls.Choice(shapes, "slash", "roadtool.shape.straight", "roadtool.shape.straight.tooltip", () => SetShape(OmsiRoadShape.Straight));
            curvedButton = OptionControls.Choice(shapes, "spline", "roadtool.shape.curved", "roadtool.shape.curved.tooltip", () => SetShape(OmsiRoadShape.Curved));
            freeButton = OptionControls.Choice(shapes, "pen-tool", "roadtool.shape.free", "roadtool.shape.free.tooltip", () => SetShape(OmsiRoadShape.Free));
            cornerRadiusField = OptionControls.Value(modeRow, "radius", "15", "m", "roadtool.corner", "roadtool.corner.tooltip");
            cornerRadiusField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            BaseMapOnly(cornerRadiusField.parent, "roadtool.corner", "roadtool.corner.tooltip");

            var snaps = OptionControls.Group(OptionControls.Row(options, "roadtool.snap"));
            snapRoadsButton = OptionControls.Choice(snaps, "magnet", "roadtool.snap.roads", "roadtool.snap.roads.tooltip", () => SetSnapping(!SnapToRoads, SnapToAngles, SnapToGuides));
            snapAnglesButton = OptionControls.Choice(snaps, "triangle-right", "roadtool.snap.angles", "roadtool.snap.angles.tooltip", () => SetSnapping(SnapToRoads, !SnapToAngles, SnapToGuides));
            snapGuidesButton = OptionControls.Choice(snaps, "crosshair", "roadtool.snap.guides", "roadtool.snap.guides.tooltip", () => SetSnapping(SnapToRoads, SnapToAngles, !SnapToGuides));

            var heightRow = OptionControls.Row(options, "roadtool.height");
            var modes = OptionControls.Group(heightRow);
            followButton = OptionControls.Choice(modes, "mountain", "roadtool.height.terrain", "roadtool.height.terrain.tooltip", () => SetHeightMode(OmsiRoadHeightMode.FollowTerrain));
            smoothButton = OptionControls.Choice(modes, "waves", "roadtool.height.smooth", "roadtool.height.smooth.tooltip", () => SetHeightMode(OmsiRoadHeightMode.Smooth));
            manualButton = OptionControls.Choice(modes, "pencil", "roadtool.height.manual", "roadtool.height.manual.tooltip", () => SetHeightMode(OmsiRoadHeightMode.Manual));
            endHeightField = OptionControls.Value(heightRow, "move-vertical", "0", "m", "roadtool.end_height", "roadtool.end_height.tooltip");
            endHeightField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());

            // Roads beside the one being drawn, at a distance (ADR 043).
            var parallelRow = OptionControls.Row(options, "roadtool.parallel");
            BaseMapOnly(parallelRow, null, null);
            var parallels = OptionControls.Group(parallelRow);
            parallelOffButton = OptionControls.Choice(parallels, "x", "roadtool.parallel.off", "roadtool.parallel.off.tooltip", () => SetParallel(false, false));
            parallelLeftButton = OptionControls.Choice(parallels, "arrow-left-to-line", "roadtool.parallel.left", "roadtool.parallel.left.tooltip", () => SetParallel(true, false));
            parallelRightButton = OptionControls.Choice(parallels, "arrow-right-to-line", "roadtool.parallel.right", "roadtool.parallel.right.tooltip", () => SetParallel(false, true));
            parallelBothButton = OptionControls.Choice(parallels, "arrow-left-right", "roadtool.parallel.both", "roadtool.parallel.both.tooltip", () => SetParallel(true, true));
            parallelDistanceField = OptionControls.Value(parallelRow, "move-horizontal", "15", "m", "roadtool.distance", "roadtool.distance.tooltip");
            parallelDistanceField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            parallelOppositeToggle = OptionControls.Switch(parallelRow, "arrow-down-up", "roadtool.opposite", "roadtool.opposite.tooltip", false);
            parallelOppositeToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());

            // What a click builds besides the road: terrain fitted, junctions, a pedestrian crossing (ADR 086, step 5) or a row of
            // parking spaces from a parking list (ADR 087; 0 is parklist_p.txt, n is parklist_p_n.txt).
            buildRow = OptionControls.Row(options, "roadtool.build");
            fitTerrainToggle = OptionControls.Switch(buildRow, "shovel", "roadtool.fit", "roadtool.fit.tooltip", true);
            BaseMapOnly(fitTerrainToggle, "roadtool.fit", "roadtool.fit.tooltip");
            junctionsToggle = OptionControls.Switch(buildRow, "cross", "roadtool.junctions", "roadtool.junctions.tooltip", true);
            junctionsToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            BaseMapOnly(junctionsToggle, "roadtool.junctions", "roadtool.junctions.tooltip");
            crossingToggle = OptionControls.Switch(buildRow, "footprints", "roadtool.crossing", "roadtool.crossing.tooltip", false);
            crossingToggle.RegisterValueChangedCallback(change =>
            {
                if (change.newValue)
                    parkingToggle.SetValueWithoutNotify(false);
                SettingsChanged?.Invoke();
            });
            parkingToggle = OptionControls.Switch(buildRow, "square-parking", "roadtool.parking", "roadtool.parking.tooltip", false);
            parkingToggle.RegisterValueChangedCallback(change =>
            {
                if (change.newValue)
                    crossingToggle.SetValueWithoutNotify(false);
                SettingsChanged?.Invoke();
            });
            parkingListField = new IntegerField { value = 0 };
            parkingListField.RegisterValueChangedCallback(change =>
            {
                if (change.newValue < 0 || change.newValue > 99)
                    parkingListField.SetValueWithoutNotify(Mathf.Clamp(change.newValue, 0, 99));
            });
            OptionControls.ValueBox(buildRow, "hash", parkingListField, null, "roadtool.parking.list", "roadtool.parking.list.tooltip");

            // The width, and for vehicle paths the direction, of the paths the path tool draws (ADR 089); in place of the row above
            // while a path profile is chosen.
            pathWidthRow = OptionControls.Row(options, "roadtool.path");
            pathWidthField = new FloatField { value = 3f };
            pathWidthField.RegisterCallback<BlurEvent>(_ => PathOptionsChanged?.Invoke());
            pathWidthField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    PathOptionsChanged?.Invoke();
            });
            OptionControls.ValueBox(pathWidthRow, "move-horizontal", pathWidthField, "m", "roadtool.path_width", "roadtool.path_width.tooltip");
            pathBothWaysToggle = OptionControls.Switch(pathWidthRow, "arrow-left-right", "roadtool.path_both", "roadtool.path_both.tooltip", false);
            pathBothWaysToggle.RegisterValueChangedCallback(_ => PathOptionsChanged?.Invoke());
            pathBothWaysRow = pathBothWaysToggle;
            pathWidthRow.style.display = DisplayStyle.None;

            var piecesRow = OptionControls.Row(options, "roadtool.pieces");
            pieceLengthField = OptionControls.Value(piecesRow, "ruler", "25", "m", "roadtool.piece", "roadtool.piece.tooltip");
            pieceLengthField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            gradientField = OptionControls.Value(piecesRow, "triangle-alert", "10", "%", "roadtool.warn", "roadtool.warn.tooltip");
            gradientField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Add(options);

            // The catalog beside them.
            catalog = new ToolCatalog("roadtool.close.tooltip", () => CloseRequested?.Invoke(), "roadtool.search");
            AddTab(Tab.InMap, OmsiSplineCategory.Other, "In map", "map-pin", "roadtool.tab.in_map", "roadtool.tab.in_map.tooltip");
            AddTab(Tab.Favorites, OmsiSplineCategory.Other, "Favorites", "star", "roadtool.tab.favorites", "roadtool.tab.favorites.tooltip");
            AddTab(Tab.MyRoads, OmsiSplineCategory.Other, "My roads", "user", "roadtool.tab.mine", "roadtool.tab.mine.tooltip");
            foreach (OmsiSplineCategory each in Enum.GetValues(typeof(OmsiSplineCategory)))
                AddTab(Tab.Category, each, OmsiSplineCategories.Name(each), CategoryIcon(each), CategoryTexts.SplineNameKey(each), CategoryTexts.SplineTooltipKey(each));
            search = catalog.Search;
            search.RegisterValueChangedCallback(_ => Refresh());
            tiles = catalog.Tiles;
            emptyLabel = catalog.Empty;
            Add(catalog.Panel);
            lastDefaultHint = Loc.T("roadtool.hint");
            planText = lastDefaultHint;

            SetHeightMode(OmsiRoadHeightMode.FollowTerrain);
            SetShape(OmsiRoadShape.Free);
            SetSnapping(true, true, true);
            SetParallel(false, false);
            schedule.Execute(TakePreviews).Every(100);
        }

        /// <summary>The panel wants to close the road tool, or to end choosing a profile.</summary>
        public event Action CloseRequested;

        /// <summary>What the road under the pointer would be, or the next step; the shell shows it in the status bar, a problem in the warning colour.</summary>
        public event Action<string, bool> HintChanged
        {
            add => catalog.HintChanged += value;
            remove => catalog.HintChanged -= value;
        }

        private void ShowHint(string text, bool problem)
        {
            planText = text;
            catalog.ShowHint(text, problem);
        }

        private string lastDefaultHint;
        private VisualElement optionsColumn;
        private string pickedHint;

        /// <summary>A click on a profile while the panel only chooses one (<see cref="BeginPick"/>): its path, relative to the installation.</summary>
        public event Action<string> ProfilePicked;

        /// <summary>True while the panel chooses a profile for something else, such as a spline of a chrono variant, and not for the road tool.</summary>
        public bool Picking { get; private set; }

        /// <summary>
        /// Lets a click on a profile choose it for something else: the road tool's options are hidden, the road tool's own
        /// profile stays as it is, and <see cref="ProfilePicked"/> carries the click.
        /// </summary>
        public void BeginPick(string title, string hint)
        {
            if (!Picking)
                pickedHint = planText;
            Picking = true;
            optionsColumn.style.display = DisplayStyle.None;
            ShowHint(title + ": " + hint, false);
        }

        // The controls only the base map has, with the tooltip they show again once the road tool builds there.
        private readonly List<(VisualElement Element, string NameKey, string TooltipKey)> baseMapOnly = new List<(VisualElement, string, string)>();

        private void BaseMapOnly(VisualElement element, string nameKey, string tooltipKey)
        {
            baseMapOnly.Add((element, nameKey, tooltipKey));
        }

        /// <summary>
        /// Greys the options that only the base map has while the road tool builds in a chrono variant, where a road stands alone:
        /// rounded corners, parallel roads, fitting the terrain and junctions (ADR 066).
        /// </summary>
        public void SetStandalone(bool standalone)
        {
            foreach (var (element, nameKey, tooltipKey) in baseMapOnly)
            {
                element.SetEnabled(!standalone);
                if (standalone)
                    LocBindings.Tooltip(element, "roadtool.standalone.tooltip");
                else if (nameKey != null)
                    LocBindings.TitledTooltip(element, nameKey, tooltipKey); // i18n:keys
                else
                    LocBindings.ClearTooltip(element);
            }
        }

        /// <summary>Ends choosing a profile for something else; the road tool's options and title come back.</summary>
        public void EndPick()
        {
            if (!Picking)
                return;
            Picking = false;
            optionsColumn.style.display = DisplayStyle.Flex;
            ShowHint(pickedHint, false);
        }

        /// <summary>The pencil on a road type the builder made was clicked: it should open in the road builder.</summary>
        public event Action<string> EditRoadTypeRequested;

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

        /// <summary>True when a click on a road inserts a pedestrian crossing piece instead of building a road.</summary>
        public bool CrossingMode
        {
            get => crossingToggle.value;
            set => crossingToggle.value = value;
        }

        /// <summary>True when two clicks on a road put a row of parking spaces along it.</summary>
        /// <summary>The number of the parking list new spaces use (0 is the map's own list).</summary>
        public int ParkingList
        {
            get => Mathf.Clamp(parkingListField.value, 0, 99);
            set => parkingListField.value = Mathf.Clamp(value, 0, 99);
        }

        /// <summary>Raised when the width or direction of the paths to draw was changed.</summary>
        public event Action PathOptionsChanged;

        /// <summary>The width chosen for the paths the path tool draws, in metres.</summary>
        public double PathWidth => Mathf.Clamp(pathWidthField.value, (float)OmsiPathProfiles.MinimumWidth, (float)OmsiPathProfiles.MaximumWidth);

        /// <summary>True when a path for vehicles runs both ways.</summary>
        public bool PathBothWays => pathBothWaysToggle.value;

        /// <summary>Shows the width (and, for vehicles, direction) rows while a path profile is chosen, with the profile's own values; hides them for null.</summary>
        public void ShowPathOptions(int? kind, double width, bool bothWays)
        {
            // Paths never fit the terrain: the switch shows off and cannot be set while a path profile is chosen, and comes back after.
            fitTerrainToggle.SetEnabled(!kind.HasValue);
            if (kind.HasValue)
            {
                if (!fitBeforePaths.HasValue)
                    fitBeforePaths = fitTerrainToggle.value;
                fitTerrainToggle.SetValueWithoutNotify(false);
            }
            else if (fitBeforePaths.HasValue)
            {
                fitTerrainToggle.SetValueWithoutNotify(fitBeforePaths.Value);
                fitBeforePaths = null;
            }
            pathWidthRow.style.display = kind.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            buildRow.style.display = kind.HasValue ? DisplayStyle.None : DisplayStyle.Flex;
            pathBothWaysRow.style.display = kind == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (!kind.HasValue)
                return;
            pathWidthField.SetValueWithoutNotify((float)Math.Round(width, 2));
            pathBothWaysToggle.SetValueWithoutNotify(bothWays);
        }

        /// <summary>Sets the width and direction fields, as typing them does; for command-line checks.</summary>
        public void SetPathOptions(double width, bool bothWays)
        {
            pathWidthField.SetValueWithoutNotify((float)width);
            pathBothWaysToggle.SetValueWithoutNotify(bothWays);
        }

        public bool ParkingMode
        {
            get => parkingToggle.value;
            set => parkingToggle.value = value;
        }

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

        /// <summary>Draws a profile's preview and sorts it into its tab again, after a road type was changed (ADR 049).</summary>
        public void ForgetProfile(string path)
        {
            lock (previewLock)
                previewRequested.Remove(path);
            if (previews.TryGetValue(path, out var texture) && texture != null)
                UnityEngine.Object.Destroy(texture);
            previews.Remove(path);
            categories?.Remove(path);
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
            ShowHint(text, problem);
        }

        /// <summary>Chooses a profile by its path relative to the installation, as a click on its tile does; for command-line checks.</summary>
        public void ChooseProfile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                SetProfile(path.Trim());
        }

        /// <summary>Shows a tab by its name, such as "Median" or "In map"; for command-line checks.</summary>
        /// <summary>The language changed: what the panel wrote from the language files is written again.</summary>
        public void RefreshTexts()
        {
            search.textEdition.placeholder = Loc.T("roadtool.search");
            if (planText == lastDefaultHint)
            {
                lastDefaultHint = Loc.T("roadtool.hint");
                planText = lastDefaultHint;
                ShowHint(planText, false);
            }
            Refresh();
        }

        public void ShowTab(string name)
        {
            foreach (var pair in tabButtons)
                if (pair.Key.userData is string english && string.Equals(english, name, StringComparison.OrdinalIgnoreCase))
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
            parallelOppositeToggle.SetEnabled(left || right);
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
            if (tileByPath.TryGetValue(path, out var tile))
                tile.AddToClassList("picker-tile-selected");
            SettingsChanged?.Invoke();
        }


        private static double? Number(TextField field, double minimum)
        {
            return double.TryParse(field.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= minimum &&
                   !double.IsInfinity(value)
                ? value
                : (double?)null;
        }

        private void AddTab(Tab kind, OmsiSplineCategory each, string englishName, string icon, string textKey, string tooltipKey)
        {
            var button = catalog.AddTab(icon, textKey, tooltipKey, () => ChooseTab(kind, each));
            button.userData = englishName;
            tabButtons.Add(button, new KeyValuePair<Tab, OmsiSplineCategory>(kind, each));
        }

        private static string CategoryIcon(OmsiSplineCategory category)
        {
            switch (category)
            {
                case OmsiSplineCategory.OneWay: return "arrow-up";
                case OmsiSplineCategory.TwoLanes: return "arrow-down-up";
                case OmsiSplineCategory.FourLanes: return "columns-3";
                case OmsiSplineCategory.SixLanes: return "columns-4";
                case OmsiSplineCategory.Median: return "separator-vertical";
                case OmsiSplineCategory.Rail: return "train-track";
                case OmsiSplineCategory.Path: return "footprints";
                default: return "ellipsis";
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
        private OmsiContentOrigins origins;
        private bool hideDlc;

        /// <summary>Where the installation's files come from, to tell road types of a DLC apart; null until the installation is read.</summary>
        public void SetOrigins(OmsiContentOrigins value, bool hide)
        {
            if (ReferenceEquals(origins, value) && hideDlc == hide)
                return;
            origins = value;
            hideDlc = hide;
            Refresh();
        }

        private int foldersLeftOut;

        /// <summary>How many spline folders the listing leaves out; the empty note says so, as an empty list could be just that.</summary>
        public int FoldersLeftOut
        {
            get => foldersLeftOut;
            set
            {
                if (foldersLeftOut == value)
                    return;
                foldersLeftOut = value;
                Refresh();
            }
        }

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
                // Road types of a DLC leave every list but the map's own while File › Settings hides DLC content.
                if (hideDlc && tab != Tab.InMap && origins != null && origins.Of(path) == OmsiContentOrigin.Dlc)
                    continue;
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
                : text.Length > 0 ? Loc.T("roadtool.empty.no_match", ("text", text))
                : Loc.T(tab == Tab.Category && categories == null ? "roadtool.empty.sorting" // i18n:keys
                    : tab == Tab.InMap ? "roadtool.empty.in_map"
                    : tab == Tab.Favorites ? "roadtool.empty.favorites"
                    : tab == Tab.MyRoads ? "roadtool.empty.mine"
                    : "roadtool.empty.none"); // i18n:keys
            // Only where a folder left out can be why the list is empty: a search and the lists of the installation.
            if (shown.Count == 0 && foldersLeftOut > 0 && (text.Length > 0 || tab == Tab.Category))
                emptyLabel.text += "\n" + Loc.T("contentfolders.left_out", ("count", foldersLeftOut));
            // The empty note lies over the tile area, which keeps its size.
            emptyLabel.style.display = shown.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement MakeTile(string path)
        {
            var tile = new VisualElement { tooltip = LocBindings.CardTooltip(Path.GetFileNameWithoutExtension(path), path) };
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
                var badge = new Label(count.ToString(CultureInfo.InvariantCulture)) { tooltip = Loc.T("roadtool.badge.tooltip", ("count", count)) };
                badge.AddToClassList("picker-tile-count");
                tile.Add(badge);
            }
            var star = new Button { tooltip = Loc.T(favorites.Contains(path) ? "roadtool.favorite.remove" : "roadtool.favorite.add") }; // i18n:keys
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
            Button edit = null;
            if (OmsiRoadTypeStore.IsMade(path))
            {
                edit = new Button(() => EditRoadTypeRequested?.Invoke(path)) { tooltip = Loc.T("roadtool.edit.tooltip") };
                edit.AddToClassList("panel-icon-button");
                edit.AddToClassList("picker-tile-edit");
                edit.Add(new IconElement("pencil"));
                tile.Add(edit);
            }
            tile.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement target && (target == star || star.Contains(target) || edit != null && (target == edit || edit.Contains(target))))
                    return;
                if (Picking)
                {
                    ProfilePicked?.Invoke(path);
                    return;
                }
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
