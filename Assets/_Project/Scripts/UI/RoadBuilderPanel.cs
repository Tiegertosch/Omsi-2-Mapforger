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
    /// The road builder (ADR 049), laid out after the Road Builder mod for Cities: Skylines II: a cross-section of the road
    /// type as a bar of lanes, where a click picks a lane or the line between two, a drag moves a lane; buttons to add lanes
    /// or start from a template; and the settings of what is picked: kind, width, direction and surface of a lane, or the
    /// line between two. The panel only edits the road type; the shell writes it.
    /// </summary>
    internal sealed class RoadBuilderPanel : VisualElement
    {
        // A press this close to the boundary between two lanes picks the line there.
        private const float LinePickPixels = 7f;
        private const float DragPixels = 6f;
        private const int PreviewSize = 64;

        private readonly Label titleLabel;
        private readonly TextField nameField;
        private readonly Label summaryLabel;
        private readonly Button saveButton;
        private readonly Button saveAsNewButton;
        private readonly Button deleteButton;
        private readonly Button fromChosenButton;
        private readonly VisualElement laneRow;
        private readonly BarOverlay overlay;
        private readonly VisualElement bottom;
        private readonly VisualElement surfaces;
        private readonly Label selectedTitle;
        private readonly VisualElement laneOptions;
        private readonly VisualElement lineOptions;
        private readonly Dictionary<OmsiRoadLaneKind, Button> kindButtons = new Dictionary<OmsiRoadLaneKind, Button>();
        private readonly Dictionary<Button, OmsiRoadMarking?> lineButtons = new Dictionary<Button, OmsiRoadMarking?>();
        private readonly TextField widthField;
        private readonly Button forwardButton;
        private readonly Button backwardButton;
        private readonly Button surfaceButton;
        private readonly VisualElement trafficRow;
        private readonly Button tramOnlyButton;
        private readonly Button carsTooButton;
        private readonly Button rowObjectButton;
        private readonly VisualElement objectRow;
        private readonly VisualElement placedRow;
        private readonly VisualElement turnRow;
        private readonly TextField rowIntervalField;
        private readonly TextField rowTurnField;
        private readonly Dictionary<OmsiRoadRowPlace, Button> placeButtons = new Dictionary<OmsiRoadRowPlace, Button>();
        private readonly Label surfaceTitle;
        private readonly TextField surfaceSearch;
        private readonly ScrollView surfaceTiles;
        private readonly Label surfaceEmpty;
        private readonly List<VisualElement> chips = new List<VisualElement>();
        private OmsiRoadType type = new OmsiRoadType();
        private int selected = -1;
        private int selectedLine = -1;
        private int hoverLine = -1;
        private bool nameTyped;
        private int editingUses;

        // A press on a lane: where it began, whether it became a drag and where the lane would go.
        private int pressedLane = -1;
        private float pressedX;
        private bool dragging;
        private int dropAt = -1;

        // The surfaces of the installation and their previews, read on worker threads.
        private string installationRoot;
        private Task<List<string>> surfaceList;
        private readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> previewRequested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object previewLock = new object();
        private readonly List<KeyValuePair<string, OmsiImage>> previewsDone = new List<KeyValuePair<string, OmsiImage>>();

        public RoadBuilderPanel()
        {
            AddToClassList("tool-panel");
            AddToClassList("picker-panel");
            AddToClassList("road-builder");

            var header = new VisualElement();
            header.AddToClassList("tool-panel-header");
            header.AddToClassList("picker-header");
            var badge = new VisualElement();
            badge.AddToClassList("tool-panel-badge");
            badge.Add(new IconElement("spline"));
            header.Add(badge);
            var titles = new VisualElement();
            titles.AddToClassList("tool-panel-titles");
            titleLabel = new Label("New road type");
            titleLabel.AddToClassList("tool-panel-title");
            titles.Add(titleLabel);
            summaryLabel = new Label();
            summaryLabel.AddToClassList("tool-panel-subtitle");
            summaryLabel.AddToClassList("picker-subtitle");
            titles.Add(summaryLabel);
            header.Add(titles);
            nameField = new TextField { tooltip = "The name of the road type, also its file name under Splines\\Mapforge in your OMSI installation" };
            nameField.AddToClassList("panel-search-field");
            nameField.AddToClassList("road-builder-name");
            nameField.textEdition.placeholder = "Name";
            nameField.RegisterValueChangedCallback(_ => nameTyped = true);
            header.Add(nameField);
            deleteButton = new Button(() => DeleteRequested?.Invoke()) { text = "Delete" };
            deleteButton.AddToClassList("map-control-button");
            deleteButton.AddToClassList("road-builder-remove");
            header.Add(deleteButton);
            saveAsNewButton = new Button(() => SaveRequested?.Invoke(RoadName, type.Copy()))
            {
                text = "Save as new type", tooltip = "Makes a road type of its own under the name typed; the one edited stays as it was"
            };
            saveAsNewButton.AddToClassList("map-control-button");
            header.Add(saveAsNewButton);
            saveButton = new Button(() =>
            {
                if (EditingPath != null)
                    ReplaceRequested?.Invoke(type.Copy());
                else
                    SaveRequested?.Invoke(RoadName, type.Copy());
            }) { text = "Save" };
            saveButton.AddToClassList("map-control-button");
            saveButton.AddToClassList("dialog-primary");
            header.Add(saveButton);
            var close = new Button(() => CloseRequested?.Invoke()) { tooltip = "Back to the road picker without saving (Esc)" };
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            Add(header);

            // The cross-section: a chip per lane, as wide as the lane, with the lines, the picked line and where a dragged
            // lane would go drawn over it.
            var lanes = new VisualElement();
            lanes.AddToClassList("road-builder-lanes");
            laneRow = new VisualElement();
            laneRow.AddToClassList("road-builder-lane-row");
            laneRow.RegisterCallback<PointerDownEvent>(OnPointerDown);
            laneRow.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            laneRow.RegisterCallback<PointerUpEvent>(OnPointerUp);
            laneRow.RegisterCallback<PointerLeaveEvent>(_ => SetHoverLine(-1));
            lanes.Add(laneRow);
            overlay = new BarOverlay(this);
            lanes.Add(overlay);
            Add(lanes);

            bottom = new VisualElement();
            bottom.AddToClassList("road-builder-bottom");
            var add = new VisualElement();
            add.AddToClassList("road-builder-column");
            add.Add(SectionLabel("Add a lane right of the one picked"));
            var addRow = new VisualElement();
            addRow.AddToClassList("road-builder-buttons");
            foreach (OmsiRoadLaneKind kind in Enum.GetValues(typeof(OmsiRoadLaneKind)))
            {
                var each = kind;
                var button = new Button(() => AddLane(each)) { text = OmsiRoadLane.Name(kind), tooltip = "Adds a " + OmsiRoadLane.Name(kind).ToLowerInvariant() + " right of the lane picked" };
                button.AddToClassList("map-control-button");
                addRow.Add(button);
            }
            add.Add(addRow);
            add.Add(SectionLabel("Start from a template"));
            var templateRow = new VisualElement();
            templateRow.AddToClassList("road-builder-buttons");
            foreach (var template in OmsiRoadType.Templates)
            {
                var description = template.Value;
                var button = new Button(() => SetType(OmsiRoadType.Parse(description))) { text = template.Key, tooltip = description };
                button.AddToClassList("map-control-button");
                templateRow.Add(button);
            }
            fromChosenButton = new Button(() => FromChosenRequested?.Invoke()) { text = "From the chosen road" };
            fromChosenButton.AddToClassList("map-control-button");
            templateRow.Add(fromChosenButton);
            add.Add(templateRow);
            bottom.Add(add);

            var picked = new VisualElement();
            picked.AddToClassList("road-builder-column");
            picked.AddToClassList("road-builder-lane-options");
            selectedTitle = SectionLabel("Pick a lane or the line between two in the bar above");
            picked.Add(selectedTitle);

            laneOptions = new VisualElement();
            var kinds = new VisualElement();
            kinds.AddToClassList("panel-tabs");
            kinds.AddToClassList("road-builder-choices");
            foreach (OmsiRoadLaneKind kind in Enum.GetValues(typeof(OmsiRoadLaneKind)))
            {
                var each = kind;
                var button = new Button(() => ChangeSelected(chosen => chosen.Kind = each)) { text = Short(kind), tooltip = OmsiRoadLane.Name(kind) };
                button.AddToClassList("panel-tab");
                kinds.Add(button);
                kindButtons[kind] = button;
            }
            laneOptions.Add(kinds);
            widthField = new TextField { tooltip = "The lane's width in metres" };
            widthField.AddToClassList("picker-field");
            widthField.RegisterValueChangedCallback(evt =>
            {
                if (double.TryParse(evt.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && width > 0.0)
                    ChangeSelected(chosen => chosen.Width = width, false);
            });
            Row(laneOptions, "Width (m)").Add(widthField);
            var directions = new VisualElement();
            directions.AddToClassList("panel-tabs");
            directions.AddToClassList("picker-segmented");
            forwardButton = new Button(() => ChangeSelected(chosen => chosen.Direction = OmsiRoadLaneDirection.Forward))
            {
                text = "With the road", tooltip = "Traffic runs the way the road is drawn"
            };
            backwardButton = new Button(() => ChangeSelected(chosen => chosen.Direction = OmsiRoadLaneDirection.Backward))
            {
                text = "Against it", tooltip = "Traffic runs against the way the road is drawn"
            };
            foreach (var button in new[] { forwardButton, backwardButton })
            {
                button.AddToClassList("panel-tab");
                directions.Add(button);
            }
            Row(laneOptions, "Direction").Add(directions);
            var surfaceRow = Row(laneOptions, "Surface");
            surfaceButton = new Button(ShowSurfaces) { tooltip = "Choose a texture of your installation's spline profiles for the lane" };
            surfaceButton.AddToClassList("map-control-button");
            surfaceButton.AddToClassList("road-builder-surface");
            surfaceRow.Add(surfaceButton);
            var standard = new Button(() => ChangeSelected(chosen => chosen.Texture = null)) { text = "Standard", tooltip = "The base game's texture for this kind of lane" };
            standard.AddToClassList("map-control-button");
            surfaceRow.Add(standard);

            // A tram lane's track, for trams alone or shared with cars.
            trafficRow = Row(laneOptions, "Traffic");
            var traffic = new VisualElement();
            traffic.AddToClassList("panel-tabs");
            traffic.AddToClassList("picker-segmented");
            tramOnlyButton = new Button(() => ChangeSelected(chosen => chosen.Shared = false)) { text = "Tram only", tooltip = "A track of its own, kept apart from cars with a solid line" };
            carsTooButton = new Button(() => ChangeSelected(chosen => chosen.Shared = true)) { text = "Cars too", tooltip = "Tracks laid in the street: cars and buses drive on them too" };
            foreach (var button in new[] { tramOnlyButton, carsTooButton })
            {
                button.AddToClassList("panel-tab");
                traffic.Add(button);
            }
            trafficRow.Add(traffic);

            // A row of objects along the lane, laid whenever the road is built.
            objectRow = Row(laneOptions, "Objects");
            rowObjectButton = new Button(() => RowObjectRequested?.Invoke()) { tooltip = "Choose a tree, a street light or any object in the library; a row of it is laid along the lane whenever the road is built" };
            rowObjectButton.AddToClassList("map-control-button");
            rowObjectButton.AddToClassList("road-builder-surface");
            objectRow.Add(rowObjectButton);
            var noObjects = new Button(() => ChangeSelected(chosen => chosen.RowObject = null)) { text = "None", tooltip = "No objects along this lane" };
            noObjects.AddToClassList("map-control-button");
            objectRow.Add(noObjects);
            placedRow = Row(laneOptions, "Every (m)");
            rowIntervalField = new TextField { tooltip = "Metres between the objects" };
            rowIntervalField.AddToClassList("picker-field");
            rowIntervalField.AddToClassList("road-builder-every");
            rowIntervalField.RegisterValueChangedCallback(evt =>
            {
                if (double.TryParse(evt.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var every) && every > 0.0)
                    ChangeSelected(chosen => chosen.RowInterval = every, false);
            });
            placedRow.Add(rowIntervalField);
            var places = new VisualElement();
            places.AddToClassList("panel-tabs");
            places.AddToClassList("picker-segmented");
            foreach (OmsiRoadRowPlace place in Enum.GetValues(typeof(OmsiRoadRowPlace)))
            {
                var each = place;
                var button = new Button(() => ChangeSelected(chosen => chosen.RowPlace = each)) { text = PlaceName(place), tooltip = PlaceTooltip(place) };
                button.AddToClassList("panel-tab");
                places.Add(button);
                placeButtons[place] = button;
            }
            placedRow.Add(places);
            turnRow = Row(laneOptions, "Turn (°)");
            rowTurnField = new TextField { tooltip = "Degrees the objects are turned by, clockwise; 90 lays a hedge modelled across along the road" };
            rowTurnField.AddToClassList("picker-field");
            rowTurnField.AddToClassList("road-builder-every");
            rowTurnField.RegisterValueChangedCallback(evt =>
            {
                if (double.TryParse(evt.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var turn))
                    ChangeSelected(chosen => chosen.RowTurn = turn, false);
            });
            turnRow.Add(rowTurnField);
            var actions = new VisualElement();
            actions.AddToClassList("road-builder-buttons");
            var left = new Button(() => MoveSelected(-1)) { text = "Move left", tooltip = "Moves the lane one place left; you can also drag it in the bar" };
            left.AddToClassList("map-control-button");
            var right = new Button(() => MoveSelected(1)) { text = "Move right", tooltip = "Moves the lane one place right; you can also drag it in the bar" };
            right.AddToClassList("map-control-button");
            var remove = new Button(RemoveSelected) { text = "Remove", tooltip = "Takes the lane away" };
            remove.AddToClassList("map-control-button");
            remove.AddToClassList("road-builder-remove");
            foreach (var button in new[] { left, right, remove })
                actions.Add(button);
            laneOptions.Add(actions);
            picked.Add(laneOptions);

            lineOptions = new VisualElement();
            var lines = new VisualElement();
            lines.AddToClassList("panel-tabs");
            lines.AddToClassList("road-builder-choices");
            AddLineButton(lines, null, "Auto", "The line the editor chooses, as on German roads");
            foreach (OmsiRoadMarking marking in Enum.GetValues(typeof(OmsiRoadMarking)))
                AddLineButton(lines, marking, OmsiRoadLane.Name(marking), LineTooltip(marking));
            lineOptions.Add(lines);
            var lineHint = new Label("A line at a kerb lies 0.2 m out on the carriageway, as an edge line.");
            lineHint.AddToClassList("panel-info");
            lineOptions.Add(lineHint);
            picked.Add(lineOptions);
            bottom.Add(picked);
            Add(bottom);

            // The surface picker, in place of the bottom row while open.
            surfaces = new VisualElement();
            surfaces.AddToClassList("road-builder-surfaces");
            surfaces.style.display = DisplayStyle.None;
            var surfaceHeader = new VisualElement();
            surfaceHeader.AddToClassList("road-builder-surface-header");
            surfaceTitle = SectionLabel("Surface");
            surfaceTitle.AddToClassList("road-builder-grow");
            surfaceHeader.Add(surfaceTitle);
            surfaceSearch = new TextField();
            surfaceSearch.AddToClassList("panel-search-field");
            surfaceSearch.AddToClassList("road-builder-name");
            surfaceSearch.textEdition.placeholder = "Search textures...";
            surfaceSearch.RegisterValueChangedCallback(_ => FillSurfaces());
            surfaceHeader.Add(surfaceSearch);
            var back = new Button(HideSurfaces) { text = "Back", tooltip = "Back to the lane without changing its surface" };
            back.AddToClassList("map-control-button");
            surfaceHeader.Add(back);
            surfaces.Add(surfaceHeader);
            surfaceTiles = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            surfaceTiles.AddToClassList("road-builder-surface-tiles");
            surfaceTiles.contentContainer.AddToClassList("picker-tiles-content");
            surfaces.Add(surfaceTiles);
            surfaceEmpty = new Label();
            surfaceEmpty.AddToClassList("panel-info");
            surfaces.Add(surfaceEmpty);
            Add(surfaces);

            schedule.Execute(TakePreviews).Every(100);
            SetType(OmsiRoadType.Parse(OmsiRoadType.Templates[0].Value));
            ShowEditing();
            SetChosenProfile(null);
        }

        /// <summary>Save on a new road type or Save as new type was clicked, with the name typed or suggested and a copy of the road type.</summary>
        public event Action<string, OmsiRoadType> SaveRequested;

        /// <summary>Save was clicked while editing a road type: it should be written anew, with every spline of it changing.</summary>
        public event Action<OmsiRoadType> ReplaceRequested;

        /// <summary>Delete was clicked while editing a road type no spline of the map uses.</summary>
        public event Action DeleteRequested;

        /// <summary>From the chosen road was clicked: the profile chosen in the road tool should be taken apart into lanes.</summary>
        public event Action FromChosenRequested;

        /// <summary>The object button of the lane picked was clicked: the object library should open to choose its row's object.</summary>
        public event Action RowObjectRequested;

        /// <summary>The lane picked, by its place from the left; -1 for none.</summary>
        public int SelectedLane => selected;

        /// <summary>
        /// Gives the lane picked a row of the object, chosen in the object library, turned as suggested for its footprint.
        /// A new row on a median starts in its middle.
        /// </summary>
        public void SetRowObject(string assetPath, double turn)
        {
            ChangeSelected(chosen =>
            {
                if (chosen.RowObject == null && chosen.Kind == OmsiRoadLaneKind.Median)
                    chosen.RowPlace = OmsiRoadRowPlace.Middle;
                chosen.RowObject = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath.Trim();
                chosen.RowTurn = turn;
            });
        }

        /// <summary>The road type being edited, by its path relative to the installation; null for a new one.</summary>
        public string EditingPath { get; private set; }

        /// <summary>The builder wants to close without saving.</summary>
        public event Action CloseRequested;

        public OmsiRoadType Type => type.Copy();

        /// <summary>The name typed, or one suggested from the lanes.</summary>
        public string RoadName => nameTyped && !string.IsNullOrWhiteSpace(nameField.value) ? nameField.value.Trim() : type.SuggestName();

        /// <summary>The installation whose textures the surface picker offers.</summary>
        public void SetInstallation(string root)
        {
            if (string.Equals(root, installationRoot, StringComparison.OrdinalIgnoreCase))
                return;
            installationRoot = root;
            surfaceList = null;
            foreach (var texture in previews.Values)
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            previews.Clear();
            lock (previewLock)
            {
                previewRequested.Clear();
                previewsDone.Clear();
            }
        }

        /// <summary>Starts again from these lanes.</summary>
        public void SetType(OmsiRoadType road)
        {
            type = road?.Copy() ?? new OmsiRoadType();
            selected = type.Lanes.Count > 0 ? Math.Min(Math.Max(selected, 0), type.Lanes.Count - 1) : -1;
            selectedLine = -1;
            HideSurfaces();
            Refresh();
        }

        /// <summary>Starts a new road type: the first template and a suggested name.</summary>
        public void Reset()
        {
            EditingPath = null;
            nameTyped = false;
            nameField.SetValueWithoutNotify(string.Empty);
            selected = -1;
            ShowEditing();
            SetType(OmsiRoadType.Parse(OmsiRoadType.Templates[0].Value));
        }

        /// <summary>
        /// Opens a road type the builder made, to change it for every spline of it, save it as a new type or delete it.
        /// </summary>
        /// <param name="uses">How many splines of the map use it; Delete waits until none do.</param>
        public void Edit(string assetPath, OmsiRoadType road, int uses)
        {
            EditingPath = assetPath;
            editingUses = uses;
            nameTyped = true;
            nameField.SetValueWithoutNotify(Path.GetFileNameWithoutExtension(assetPath));
            selected = -1;
            ShowEditing();
            SetType(road);
        }

        /// <summary>The road chosen in the road tool, which From the chosen road takes apart into lanes; null for none.</summary>
        public void SetChosenProfile(string path)
        {
            fromChosenButton.SetEnabled(path != null);
            fromChosenButton.tooltip = path != null
                ? "Takes " + Path.GetFileNameWithoutExtension(path) + ", the road chosen in the road tool, apart into lanes to start from"
                : "Choose a road in the road tool first; its lanes are taken to start from";
        }

        private void ShowEditing()
        {
            var editing = EditingPath != null;
            var name = editing ? Path.GetFileNameWithoutExtension(EditingPath) : null;
            titleLabel.text = editing ? "Edit " + name : "New road type";
            nameField.tooltip = editing
                ? "The name for Save as new type; Save keeps the road type under its name"
                : "The name of the road type, also its file name under Splines\\Mapforge in your OMSI installation";
            saveButton.tooltip = editing
                ? "Changes " + name + ": " + (editingUses == 1 ? "the spline" : "all " + editingUses + " splines") + " of it in this map and the junctions made with it change too; Ctrl+Z undoes it"
                : "Makes the road type and chooses it in the road tool, ready to build";
            saveAsNewButton.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
            deleteButton.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
            deleteButton.SetEnabled(editing && editingUses == 0);
            deleteButton.tooltip = editingUses == 0
                ? "Deletes " + name + " from your OMSI installation, unless another map or a made junction still uses it; Ctrl+Z brings it back"
                : "No spline of this map may use " + name + " to delete it; " + editingUses + " still do";
        }

        /// <summary>Sets the name as if typed; for command-line checks.</summary>
        public void SetName(string name)
        {
            nameField.value = name ?? string.Empty;
            nameTyped = !string.IsNullOrWhiteSpace(name);
        }

        /// <summary>Picks a lane by its place from the left, or none with -1.</summary>
        public void SelectLane(int index)
        {
            selected = index >= 0 && index < type.Lanes.Count ? index : -1;
            selectedLine = -1;
            HideSurfaces();
            Refresh();
        }

        /// <summary>Picks the line between lane <paramref name="index"/> and the next, counted from the left.</summary>
        public void SelectLine(int index)
        {
            selectedLine = index >= 0 && index + 1 < type.Lanes.Count ? index : -1;
            if (selectedLine >= 0)
                selected = -1;
            HideSurfaces();
            Refresh();
        }

        /// <summary>Opens the surface picker for the lane picked, as the Surface button does.</summary>
        public void ShowSurfaces()
        {
            if (selected < 0)
                return;
            bottom.style.display = DisplayStyle.None;
            surfaces.style.display = DisplayStyle.Flex;
            surfaceTitle.text = "Surface for lane " + (selected + 1) + ": " + OmsiRoadLane.Name(type.Lanes[selected].Kind);
            if (surfaceList == null && installationRoot != null)
            {
                var root = installationRoot;
                var list = Task.Run(() => OmsiRoadTypeStore.SurfaceTextures(root));
                surfaceList = list;
                IVisualElementScheduledItem poll = null;
                poll = schedule.Execute(() =>
                {
                    if (!list.IsCompleted)
                        return;
                    poll.Pause();
                    if (list.IsFaulted)
                        Debug.LogException(list.Exception);
                    FillSurfaces();
                }).Every(100);
            }
            FillSurfaces();
        }

        /// <summary>Whether the surface list is read, for command-line checks that wait for it.</summary>
        public bool SurfacesReady => surfaceList != null && surfaceList.IsCompleted;

        private void HideSurfaces()
        {
            surfaces.style.display = DisplayStyle.None;
            bottom.style.display = DisplayStyle.Flex;
        }

        private void FillSurfaces()
        {
            surfaceTiles.Clear();
            if (surfaceList == null || !surfaceList.IsCompleted)
            {
                surfaceEmpty.text = installationRoot == null ? "Open a map to choose from its installation's textures." : "Reading the textures of the installation's spline profiles...";
                return;
            }
            var text = surfaceSearch.value?.Trim() ?? string.Empty;
            var shown = 0;
            foreach (var path in surfaceList.IsFaulted ? new List<string>() : surfaceList.Result)
            {
                if (text.Length > 0 && path.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (++shown > 240)
                    break;
                surfaceTiles.Add(SurfaceTile(path));
            }
            surfaceEmpty.text = shown == 0 ? text.Length > 0 ? "No texture matches \"" + text + "\"." : "The installation's spline profiles use no textures." : string.Empty;
        }

        private VisualElement SurfaceTile(string path)
        {
            var tile = new VisualElement { tooltip = path };
            tile.AddToClassList("picker-tile");
            tile.EnableInClassList("picker-tile-selected", selected >= 0 && string.Equals(type.Lanes[selected].Texture, path, StringComparison.OrdinalIgnoreCase));
            var preview = new VisualElement();
            preview.AddToClassList("picker-tile-preview");
            if (Preview(path) is Texture2D image)
                preview.style.backgroundImage = new StyleBackground(image);
            tile.Add(preview);
            var name = new Label(Path.GetFileNameWithoutExtension(path));
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            tile.RegisterCallback<ClickEvent>(_ =>
            {
                ChangeSelected(chosen => chosen.Texture = path);
                HideSurfaces();
            });
            return tile;
        }

        /// <summary>The preview of a texture, or null while it is read on a worker thread or when it cannot be read.</summary>
        private Texture2D Preview(string path)
        {
            if (previews.TryGetValue(path, out var texture))
                return texture;
            if (installationRoot == null)
                return null;
            lock (previewLock)
                if (!previewRequested.Add(path))
                    return null;
            var root = installationRoot;
            Task.Run(() =>
            {
                OmsiImage image = null;
                try
                {
                    var file = OmsiRelativePath.Resolve(root, path);
                    var bytes = File.Exists(file) ? File.ReadAllBytes(file) : File.ReadAllBytes(Path.ChangeExtension(file, ".dds"));
                    if (OmsiImageReader.TryDecode(bytes, out var decoded))
                        image = decoded.Reduce(PreviewSize);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is ArgumentException)
                {
                    // Without a preview the tile stays plain.
                }
                lock (previewLock)
                    previewsDone.Add(new KeyValuePair<string, OmsiImage>(path, image));
            });
            return null;
        }

        /// <summary>Turns previews read on the worker threads into textures and shows them.</summary>
        private void TakePreviews()
        {
            List<KeyValuePair<string, OmsiImage>> done;
            lock (previewLock)
            {
                if (previewsDone.Count == 0)
                    return;
                done = new List<KeyValuePair<string, OmsiImage>>(previewsDone);
                previewsDone.Clear();
            }
            foreach (var pair in done)
            {
                Texture2D texture = null;
                var image = pair.Value;
                if (image != null)
                {
                    var rgba = new byte[image.Width * image.Height * 4];
                    for (var row = 0; row < image.Height; row++)
                        for (var column = 0; column < image.Width; column++)
                        {
                            // OMSI images run from the top row down, Unity's from the bottom up.
                            var from = (row * image.Width + column) * image.Channels;
                            var to = ((image.Height - 1 - row) * image.Width + column) * 4;
                            for (var channel = 0; channel < 4; channel++)
                                rgba[to + channel] = image.Channels == 4 ? image.Pixels[from + channel] : channel == 3 ? (byte)255 : image.Pixels[from];
                        }
                    texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    texture.LoadRawTextureData(rgba);
                    texture.Apply(false, true);
                }
                previews[pair.Key] = texture;
            }
            if (surfaces.style.display == DisplayStyle.Flex)
                FillSurfaces();
            Refresh(false);
        }

        private void AddLineButton(VisualElement parent, OmsiRoadMarking? marking, string text, string tooltip)
        {
            var button = new Button(() => ChangeLine(marking)) { text = text, tooltip = tooltip };
            button.AddToClassList("panel-tab");
            parent.Add(button);
            lineButtons[button] = marking;
        }

        private static string LineTooltip(OmsiRoadMarking marking)
        {
            switch (marking)
            {
                case OmsiRoadMarking.Dashed: return "Guide line, 3 m dashes with 6 m gaps, as in towns";
                case OmsiRoadMarking.LongDashed: return "Guide line, 6 m dashes with 12 m gaps, as on country roads";
                case OmsiRoadMarking.Solid: return "Lane line traffic must not cross";
                case OmsiRoadMarking.Double: return "Two solid lines";
                case OmsiRoadMarking.SolidDashed: return "Solid on the left, dashed on the right: traffic on the right may cross";
                case OmsiRoadMarking.DashedSolid: return "Dashed on the left, solid on the right: traffic on the left may cross";
                case OmsiRoadMarking.Broad: return "Broad solid line, as beside bus and bike lanes";
                case OmsiRoadMarking.BroadDashed: return "Broad dashed line, 3 m dashes with 3 m gaps, as where traffic may cross into a bus lane";
                default: return "No line";
            }
        }

        private void ChangeLine(OmsiRoadMarking? marking)
        {
            if (selectedLine < 0)
                return;
            type.Lanes[selectedLine].RightMarking = marking;
            Refresh();
        }

        private void AddLane(OmsiRoadLaneKind kind)
        {
            if (type.Lanes.Count >= OmsiRoadType.MaximumLanes)
                return;
            var lane = new OmsiRoadLane { Kind = kind, Width = OmsiRoadLane.DefaultWidth(kind) };
            // A new lane runs as the one picked, else with the road.
            if (selected >= 0)
                lane.Direction = type.Lanes[selected].Direction;
            var at = selected >= 0 ? selected + 1 : selectedLine >= 0 ? selectedLine + 1 : type.Lanes.Count;
            type.Lanes.Insert(at, lane);
            selected = at;
            selectedLine = -1;
            Refresh();
        }

        private void ChangeSelected(Action<OmsiRoadLane> change, bool refreshWidth = true)
        {
            if (selected < 0)
                return;
            var lane = type.Lanes[selected];
            var kind = lane.Kind;
            change(lane);
            if (lane.Kind != kind)
                lane.Width = OmsiRoadLane.DefaultWidth(lane.Kind);
            Refresh(refreshWidth || lane.Kind != kind);
        }

        private void MoveSelected(int step)
        {
            if (selected >= 0)
                MoveLane(selected, step > 0 ? selected + 2 : selected - 1);
        }

        /// <summary>Moves a lane to a place between the lanes, counted from 0 at the left edge to the number of lanes at the right.</summary>
        private void MoveLane(int from, int to)
        {
            if (from < 0 || from >= type.Lanes.Count || to < 0 || to > type.Lanes.Count || to == from || to == from + 1)
                return;
            var lane = type.Lanes[from];
            type.Lanes.RemoveAt(from);
            var at = to > from ? to - 1 : to;
            type.Lanes.Insert(at, lane);
            selected = at;
            selectedLine = -1;
            Refresh();
        }

        private void RemoveSelected()
        {
            if (selected < 0)
                return;
            type.Lanes.RemoveAt(selected);
            selected = Math.Min(selected, type.Lanes.Count - 1);
            Refresh();
        }

        // The lane the bar shows at an x in its own pixels, the line within reach of it, and the place a lane dropped there takes.
        private int LaneAt(float x)
        {
            for (var index = 0; index < chips.Count; index++)
                if (x < chips[index].layout.xMax || index == chips.Count - 1)
                    return index;
            return -1;
        }

        private int LineAt(float x)
        {
            for (var index = 0; index + 1 < chips.Count; index++)
                if (Mathf.Abs(chips[index].layout.xMax - x) <= LinePickPixels)
                    return index;
            return -1;
        }

        private int DropAt(float x)
        {
            for (var index = 0; index < chips.Count; index++)
                if (x < chips[index].layout.center.x)
                    return index;
            return chips.Count;
        }

        private void SetHoverLine(int line)
        {
            if (line == hoverLine)
                return;
            hoverLine = line;
            overlay.MarkDirtyRepaint();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || chips.Count == 0)
                return;
            var x = evt.localPosition.x;
            var line = LineAt(x);
            if (line >= 0)
            {
                SelectLine(line);
                evt.StopPropagation();
                return;
            }
            pressedLane = LaneAt(x);
            pressedX = x;
            dragging = false;
            dropAt = -1;
            laneRow.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            var x = evt.localPosition.x;
            if (pressedLane < 0)
            {
                SetHoverLine(LineAt(x));
                return;
            }
            if (!dragging && Mathf.Abs(x - pressedX) >= DragPixels)
                dragging = true;
            if (dragging)
            {
                var at = DropAt(x);
                if (at != dropAt)
                {
                    dropAt = at;
                    overlay.MarkDirtyRepaint();
                }
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (pressedLane < 0)
                return;
            laneRow.ReleasePointer(evt.pointerId);
            var lane = pressedLane;
            pressedLane = -1;
            if (dragging)
            {
                dragging = false;
                var to = dropAt;
                dropAt = -1;
                if (to == lane || to == lane + 1)
                    SelectLane(lane);
                else
                    MoveLane(lane, to);
            }
            else
                SelectLane(lane);
            evt.StopPropagation();
        }

        private void Refresh(bool refreshWidth = true)
        {
            laneRow.Clear();
            chips.Clear();
            for (var index = 0; index < type.Lanes.Count; index++)
            {
                var chip = Chip(type.Lanes[index], index);
                laneRow.Add(chip);
                chips.Add(chip);
            }

            var problems = type.Problems();
            summaryLabel.text = problems.Count > 0 ? problems[0]
                : type.Lanes.Count + " lanes, " + type.Width.ToString("0.00", CultureInfo.InvariantCulture) + " m wide";
            summaryLabel.EnableInClassList("panel-info-warning", problems.Count > 0);
            saveButton.SetEnabled(problems.Count == 0);
            saveAsNewButton.SetEnabled(problems.Count == 0);
            if (!nameTyped)
                nameField.SetValueWithoutNotify(type.SuggestName());

            var lane = selected >= 0 ? type.Lanes[selected] : null;
            laneOptions.style.display = selectedLine >= 0 ? DisplayStyle.None : DisplayStyle.Flex;
            lineOptions.style.display = selectedLine >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (selectedLine >= 0)
            {
                var chosen = type.Lanes[selectedLine].RightMarking;
                selectedTitle.text = "Line between lane " + (selectedLine + 1) + " and " + (selectedLine + 2);
                foreach (var pair in lineButtons)
                {
                    pair.Key.EnableInClassList("panel-tab-active", pair.Value == chosen);
                    if (!pair.Value.HasValue)
                        pair.Key.text = "Auto: " + OmsiRoadLane.Name(type.AutoMarking(selectedLine));
                }
            }
            else
                selectedTitle.text = lane == null ? "Pick a lane or the line between two in the bar above" : "Lane " + (selected + 1) + ": " + OmsiRoadLane.Name(lane.Kind);
            laneOptions.SetEnabled(lane != null);
            foreach (var pair in kindButtons)
                pair.Value.EnableInClassList("panel-tab-active", lane != null && lane.Kind == pair.Key);
            if (lane != null && refreshWidth)
                widthField.SetValueWithoutNotify(lane.Width.ToString("0.00", CultureInfo.InvariantCulture));
            var directed = lane != null && lane.HasDirection;
            forwardButton.parent.parent.SetEnabled(directed);
            forwardButton.EnableInClassList("panel-tab-active", directed && lane.Direction == OmsiRoadLaneDirection.Forward);
            backwardButton.EnableInClassList("panel-tab-active", directed && lane.Direction == OmsiRoadLaneDirection.Backward);
            surfaceButton.text = lane?.Texture != null ? Path.GetFileNameWithoutExtension(lane.Texture) : "Standard";
            surfaceButton.tooltip = lane?.Texture ?? "The base game's texture for this kind of lane; click to choose another";
            var tram = lane != null && lane.Kind == OmsiRoadLaneKind.Tram;
            trafficRow.style.display = tram ? DisplayStyle.Flex : DisplayStyle.None;
            tramOnlyButton.EnableInClassList("panel-tab-active", tram && !lane.Shared);
            carsTooButton.EnableInClassList("panel-tab-active", tram && lane.Shared);
            rowObjectButton.text = lane?.RowObject != null ? Path.GetFileNameWithoutExtension(lane.RowObject) : "Choose...";
            rowObjectButton.tooltip = lane?.RowObject ?? "Choose a tree, a street light or any object in the library; a row of it is laid along the lane whenever the road is built";
            // Objects stand only beside the traffic; a row given to another lane before shows, so it can be taken away.
            objectRow.style.display = lane != null && (OmsiRoadLane.CanHoldRow(lane.Kind) || lane.RowObject != null) ? DisplayStyle.Flex : DisplayStyle.None;
            rowObjectButton.SetEnabled(lane != null && OmsiRoadLane.CanHoldRow(lane.Kind));
            var rows = lane?.RowObject != null && OmsiRoadLane.CanHoldRow(lane.Kind);
            placedRow.style.display = rows ? DisplayStyle.Flex : DisplayStyle.None;
            turnRow.style.display = rows ? DisplayStyle.Flex : DisplayStyle.None;
            if (lane != null && refreshWidth)
            {
                rowIntervalField.SetValueWithoutNotify(lane.RowInterval.ToString("0.##", CultureInfo.InvariantCulture));
                rowTurnField.SetValueWithoutNotify(lane.RowTurn.ToString("0.##", CultureInfo.InvariantCulture));
            }
            foreach (var pair in placeButtons)
                pair.Value.EnableInClassList("panel-tab-active", lane != null && lane.RowPlace == pair.Key);
            overlay.MarkDirtyRepaint();
        }

        private VisualElement Chip(OmsiRoadLane lane, int index)
        {
            var chip = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                tooltip = OmsiRoadLane.Name(lane.Kind) + ", " + lane.Width.ToString("0.00", CultureInfo.InvariantCulture) + " m"
            };
            chip.AddToClassList("road-builder-lane");
            chip.AddToClassList("road-builder-lane-" + OmsiRoadLane.Word(lane.Kind));
            chip.EnableInClassList("road-builder-lane-raised", lane.Raised);
            chip.EnableInClassList("road-builder-lane-selected", index == selected);
            chip.style.flexGrow = (float)lane.Width;
            if (lane.Texture != null && Preview(lane.Texture) is Texture2D surface)
            {
                chip.style.backgroundImage = new StyleBackground(surface);
                chip.AddToClassList("road-builder-lane-textured");
            }
            if (lane.HasDirection)
            {
                var arrow = new IconElement(lane.Direction == OmsiRoadLaneDirection.Forward ? "arrow-up" : "arrow-down") { pickingMode = PickingMode.Ignore };
                arrow.AddToClassList("road-builder-lane-arrow");
                chip.Add(arrow);
            }
            if (lane.Kind == OmsiRoadLaneKind.Tram)
                foreach (var side in new[] { "left", "right" })
                {
                    var rail = new VisualElement { pickingMode = PickingMode.Ignore };
                    rail.AddToClassList("road-builder-rail");
                    rail.AddToClassList("road-builder-rail-" + side);
                    chip.Add(rail);
                }
            if (lane.RowObject != null)
            {
                var row = new IconElement("tree-deciduous") { pickingMode = PickingMode.Ignore, tooltip = lane.RowObject };
                row.AddToClassList("road-builder-lane-row-icon");
                chip.Add(row);
            }
            var kind = new Label(Short(lane.Kind) + (lane.Kind == OmsiRoadLaneKind.Tram && lane.Shared ? " + cars" : string.Empty)) { pickingMode = PickingMode.Ignore };
            kind.AddToClassList("road-builder-lane-kind");
            chip.Add(kind);
            var width = new Label(lane.Width.ToString("0.00", CultureInfo.InvariantCulture) + " m") { pickingMode = PickingMode.Ignore };
            width.AddToClassList("road-builder-lane-width");
            chip.Add(width);
            chip.RegisterCallback<GeometryChangedEvent>(_ => overlay.MarkDirtyRepaint());
            return chip;
        }

        private static string Short(OmsiRoadLaneKind kind)
        {
            switch (kind)
            {
                case OmsiRoadLaneKind.Sidewalk: return "Walk";
                case OmsiRoadLaneKind.Median: return "Median";
                case OmsiRoadLaneKind.Shoulder: return "Shoulder";
                case OmsiRoadLaneKind.Bus: return "Bus";
                case OmsiRoadLaneKind.Bike: return "Bike";
                case OmsiRoadLaneKind.Parking: return "Parking";
                case OmsiRoadLaneKind.Tram: return "Tram";
                default: return "Lane";
            }
        }

        private static string PlaceName(OmsiRoadRowPlace place)
        {
            switch (place)
            {
                case OmsiRoadRowPlace.Middle: return "Middle";
                case OmsiRoadRowPlace.Outer: return "Far side";
                default: return "Kerb";
            }
        }

        private static string PlaceTooltip(OmsiRoadRowPlace place)
        {
            switch (place)
            {
                case OmsiRoadRowPlace.Middle: return "In the middle of the lane";
                case OmsiRoadRowPlace.Outer: return "At the side away from the road, 0.75 m in";
                default: return "By the kerb, or the side towards the road, 0.75 m in";
            }
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("picker-options-title");
            return label;
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

        /// <summary>
        /// Draws over the lane bar: the lines on the boundaries between lanes as they will be painted, the line picked or
        /// under the pointer, and where a dragged lane would go.
        /// </summary>
        private sealed class BarOverlay : VisualElement
        {
            private static readonly Color Paint = new Color(0.95f, 0.95f, 0.93f);
            private static readonly Color Picked = new Color(0.94f, 0.54f, 0.2f, 0.9f);
            private static readonly Color Hovered = new Color(0.68f, 0.72f, 0.77f, 0.5f);
            private readonly RoadBuilderPanel owner;

            public BarOverlay(RoadBuilderPanel owner)
            {
                this.owner = owner;
                pickingMode = PickingMode.Ignore;
                AddToClassList("road-builder-markings");
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                var painter = context.painter2D;
                var chips = owner.chips;
                var lanes = owner.type.Lanes;
                for (var index = 0; index + 1 < chips.Count && index + 1 < lanes.Count; index++)
                {
                    var a = chips[index].layout;
                    var b = chips[index + 1].layout;
                    var x = a.xMax;
                    var top = Mathf.Max(a.yMin, b.yMin) + 6f;
                    var bottom = a.yMax - 6f;
                    if (index == owner.selectedLine || index == owner.hoverLine)
                    {
                        painter.fillColor = index == owner.selectedLine ? Picked : Hovered;
                        painter.BeginPath();
                        painter.MoveTo(new Vector2(x - 5f, a.yMin));
                        painter.LineTo(new Vector2(x + 5f, a.yMin));
                        painter.LineTo(new Vector2(x + 5f, a.yMax));
                        painter.LineTo(new Vector2(x - 5f, a.yMax));
                        painter.ClosePath();
                        painter.Fill();
                    }
                    var marking = owner.type.MarkingAfter(index);
                    if (marking == OmsiRoadMarking.None || lanes[index].Raised && lanes[index + 1].Raised)
                        continue;
                    // A line at a kerb shows on the carriageway side, as it is painted.
                    x += lanes[index].Raised ? 4f : lanes[index + 1].Raised ? -4f : 0f;
                    switch (marking)
                    {
                        case OmsiRoadMarking.Double:
                            Stroke(painter, x - 2.5f, top, bottom, false, false, false);
                            Stroke(painter, x + 2.5f, top, bottom, false, false, false);
                            break;
                        case OmsiRoadMarking.SolidDashed:
                            Stroke(painter, x - 2.5f, top, bottom, false, false, false);
                            Stroke(painter, x + 2.5f, top, bottom, true, false, false);
                            break;
                        case OmsiRoadMarking.DashedSolid:
                            Stroke(painter, x - 2.5f, top, bottom, true, false, false);
                            Stroke(painter, x + 2.5f, top, bottom, false, false, false);
                            break;
                        default:
                            Stroke(painter, x, top, bottom, marking != OmsiRoadMarking.Solid && marking != OmsiRoadMarking.Broad, marking == OmsiRoadMarking.LongDashed,
                                marking == OmsiRoadMarking.Broad || marking == OmsiRoadMarking.BroadDashed);
                            break;
                    }
                }

                if (owner.dragging && owner.dropAt >= 0 && chips.Count > 0)
                {
                    var x = owner.dropAt < chips.Count ? chips[owner.dropAt].layout.xMin : chips[chips.Count - 1].layout.xMax;
                    painter.strokeColor = Picked;
                    painter.lineWidth = 3f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, 2f));
                    painter.LineTo(new Vector2(x, contentRect.height - 2f));
                    painter.Stroke();
                }
            }

            private static void Stroke(Painter2D painter, float x, float top, float bottom, bool dashed, bool longDashes, bool broad)
            {
                painter.strokeColor = Paint;
                painter.lineWidth = broad ? 4f : 2f;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                if (!dashed)
                {
                    painter.MoveTo(new Vector2(x, top));
                    painter.LineTo(new Vector2(x, bottom));
                }
                else
                {
                    var dash = longDashes ? 10f : 6f;
                    var step = longDashes ? 26f : broad ? 12f : 15f;
                    for (var y = top; y < bottom; y += step)
                    {
                        painter.MoveTo(new Vector2(x, y));
                        painter.LineTo(new Vector2(x, Mathf.Min(bottom, y + dash)));
                    }
                }
                painter.Stroke();
            }
        }
    }
}
