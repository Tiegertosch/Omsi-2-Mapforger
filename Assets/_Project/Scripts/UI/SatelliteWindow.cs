using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The "Satellite and terrain" window, as openMapper's: where the map lies on the Earth, found by name and clicked on a
    /// map of the world; pictures from the air and terrain heights loaded for its tiles; and how the pictures lie on the
    /// ground. The shell does the work when the events fire and tells the window what came of it.
    /// </summary>
    internal sealed class SatelliteWindow : VisualElement
    {
        /// <summary>The choice of source that takes the sharpest at the map's place.</summary>
        public const string SharpestChoice = "The sharpest here";

        private readonly TextField searchField = new TextField();
        private readonly VisualElement results = new VisualElement();
        private readonly Label attributionLabel = new Label();
        private readonly VisualElement placeSection = new VisualElement();
        private readonly Label worldLabel = new Label();
        private readonly Button placeButton;
        private readonly TextField rotationField = new TextField();
        private readonly Label anchorLabel = new Label();
        private readonly DropdownField sourceField = new DropdownField();
        private readonly Label detailLabel = new Label();
        private readonly Toggle heightsToggle = new Toggle();
        private readonly Toggle autoToggle = new Toggle();
        private readonly Button loadAllButton;
        private readonly Button loadTileButton;
        private readonly Button heightsOnlyButton;
        private readonly Label progressLabel = new Label();
        private readonly Button cancelButton;
        private readonly Toggle relativeToggle = new Toggle();
        private readonly TextField offsetField = new TextField();
        private readonly TextField smoothingField = new TextField();
        private readonly Label baseLabel = new Label();
        private readonly Button measureButton;
        private readonly Toggle showToggle = new Toggle();
        private readonly Slider opacitySlider = new Slider(0f, 1f);
        private readonly Label opacityLabel = new Label();

        private bool placing;
        private bool busy;
        private bool placed;
        private bool tileChosen;

        public SatelliteWindow()
        {
            AddToClassList("floating-panel");
            AddToClassList("satellite-frame");
            var header = new VisualElement();
            header.AddToClassList("floating-panel-header");
            var badge = new IconElement("map");
            badge.AddToClassList("floating-panel-icon");
            header.Add(badge);
            var title = new Label("Satellite and terrain");
            title.AddToClassList("floating-panel-title");
            header.Add(title);
            var close = new Button(() => Closed?.Invoke()) { tooltip = "Close; loading goes on" };
            close.AddToClassList("panel-collapse-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            Add(header);
            MakeMovable(header);

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("floating-panel-content");
            body.AddToClassList("satellite-body");
            Add(body);
            var columns = new VisualElement();
            columns.AddToClassList("satellite-columns");
            body.Add(columns);
            var left = new VisualElement();
            left.AddToClassList("satellite-column");
            columns.Add(left);
            var right = new VisualElement();
            right.AddToClassList("satellite-column");
            columns.Add(right);

            // Find a place and see it from the air.
            var searchRow = new VisualElement();
            searchRow.AddToClassList("satellite-row");
            searchField.AddToClassList("picker-field");
            searchField.AddToClassList("satellite-search");
            searchField.textEdition.placeholder = "Find a place: a town, a street, an address";
            searchField.tooltip = "Looks the place up with OpenStreetMap's Nominatim; Enter searches too";
            searchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Return || evt.keyCode == UnityEngine.KeyCode.KeypadEnter)
                    Search();
            }, TrickleDown.TrickleDown);
            searchRow.Add(searchField);
            searchRow.Add(Button("Find", Search, "Looks the place up with OpenStreetMap's Nominatim", "search"));
            left.Add(searchRow);
            results.AddToClassList("satellite-results");
            left.Add(results);
            WorldMap = new WorldMapElement();
            WorldMap.Clicked += mercator =>
            {
                if (placing)
                {
                    SetPlacing(false);
                    PlaceRequested?.Invoke(mercator);
                }
            };
            left.Add(WorldMap);
            attributionLabel.AddToClassList("satellite-attribution");
            left.Add(attributionLabel);

            // Where the map lies.
            Section(left, "Place on the Earth");
            worldLabel.AddToClassList("satellite-note");
            worldLabel.text = "This map has world coordinates ([worldcoordinates]): it lies on the Earth by itself, its tiles are Web Mercator tiles.";
            left.Add(worldLabel);
            placeSection.AddToClassList("satellite-place");
            left.Add(placeSection);
            var placeRow = new VisualElement();
            placeRow.AddToClassList("satellite-row");
            placeButton = Button("Place the map here", () => SetPlacing(!placing), "Then click on the map of the world: the middle of the orange tile goes there", "crosshair");
            placeRow.Add(placeButton);
            placeRow.Add(Button("At the middle", () => PlaceRequested?.Invoke(WorldMap.Centre), "Lays the orange tile's middle on the middle of the map of the world"));
            placeSection.Add(placeRow);
            var rotationRow = Row(placeSection, "North turned (°)");
            Field(rotationRow, rotationField, "How far the map's north is turned from true north, clockwise");
            rotationField.RegisterCallback<FocusOutEvent>(_ => ApplyRotation());
            rotationField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Return || evt.keyCode == UnityEngine.KeyCode.KeypadEnter)
                    ApplyRotation();
            }, TrickleDown.TrickleDown);
            anchorLabel.AddToClassList("satellite-note");
            placeSection.Add(anchorLabel);

            // Pictures from the air and heights for the tiles.
            Section(right, "Imagery");
            var sourceRow = Row(right, "Source");
            sourceField.AddToClassList("new-map-dropdown");
            sourceField.tooltip = "Where the pictures come from; none of them needs a key. The sharpest is chosen at the map's middle.";
            var choices = new List<string> { SharpestChoice };
            foreach (var source in OmsiImagerySource.Imagery)
                choices.Add(source.Name);
            sourceField.choices = choices;
            sourceField.SetValueWithoutNotify(SharpestChoice);
            sourceField.RegisterValueChangedCallback(evt => SourceChanged?.Invoke(evt.newValue == SharpestChoice ? null : evt.newValue));
            sourceRow.Add(sourceField);
            detailLabel.AddToClassList("satellite-note");
            right.Add(detailLabel);
            heightsToggle.text = "Also load terrain heights";
            heightsToggle.AddToClassList("picker-option-toggle");
            heightsToggle.AddToClassList("panel-toggle");
            heightsToggle.tooltip = "Takes the ground's heights from the AWS terrain tiles too; the shaped ground of those tiles is replaced, in one step Ctrl+Z takes back";
            heightsToggle.RegisterValueChangedCallback(evt => TicksChanged?.Invoke(evt.newValue, autoToggle.value));
            right.Add(heightsToggle);
            var loadRow = new VisualElement();
            loadRow.AddToClassList("satellite-row");
            loadRow.AddToClassList("satellite-wrap");
            loadAllButton = Button("Load for all tiles", () => LoadRequested?.Invoke(false, true, heightsToggle.value), "Pictures (and heights) for every tile of the map");
            loadAllButton.AddToClassList("dialog-primary");
            loadTileButton = Button("For the selected tile", () => LoadRequested?.Invoke(true, true, heightsToggle.value), "Pictures (and heights) for the tile selected in the map");
            heightsOnlyButton = Button("Heights only", () => LoadRequested?.Invoke(false, false, true), "Terrain heights for every tile, without pictures");
            loadRow.Add(loadAllButton);
            loadRow.Add(loadTileButton);
            loadRow.Add(heightsOnlyButton);
            right.Add(loadRow);
            autoToggle.text = "New tiles get pictures by themselves";
            autoToggle.AddToClassList("picker-option-toggle");
            autoToggle.AddToClassList("panel-toggle");
            autoToggle.tooltip = "A tile added to the map gets its picture from the air at once, and its terrain heights too when \"Also load terrain heights\" is ticked";
            autoToggle.RegisterValueChangedCallback(evt => TicksChanged?.Invoke(heightsToggle.value, evt.newValue));
            right.Add(autoToggle);
            var progressRow = new VisualElement();
            progressRow.AddToClassList("satellite-row");
            progressLabel.AddToClassList("satellite-progress");
            progressRow.Add(progressLabel);
            cancelButton = Button("Cancel", () => CancelRequested?.Invoke(), "Stops loading; tiles done so far stay");
            progressRow.Add(cancelButton);
            right.Add(progressRow);

            Section(right, "Terrain heights");
            relativeToggle.text = "Relative: the middle tile's mean height is 0 m";
            relativeToggle.AddToClassList("picker-option-toggle");
            relativeToggle.AddToClassList("panel-toggle");
            relativeToggle.tooltip = "Takes the middle tile's mean height off all heights, as most OMSI maps lie near 0 m; off keeps the height above the sea";
            relativeToggle.RegisterValueChangedCallback(_ => HeightOptionsChanged?.Invoke());
            right.Add(relativeToggle);
            Field(Row(right, "Raise all by (m)"), offsetField, "Added to every height");
            Field(Row(right, "Smoothing (m)"), smoothingField, "How far each height is smoothed with its neighbours; 0 for none");
            offsetField.RegisterCallback<FocusOutEvent>(_ => HeightOptionsChanged?.Invoke());
            smoothingField.RegisterCallback<FocusOutEvent>(_ => HeightOptionsChanged?.Invoke());
            var baseRow = new VisualElement();
            baseRow.AddToClassList("satellite-row");
            baseLabel.AddToClassList("satellite-note");
            baseLabel.AddToClassList("satellite-grow");
            baseRow.Add(baseLabel);
            measureButton = Button("Measure again", () => MeasureAgainRequested?.Invoke(), "The next load measures the middle tile's mean height again; tiles loaded before may then step at their edges");
            baseRow.Add(measureButton);
            right.Add(baseRow);
            var terrainNote = new Label("Terrain: Mapzen Terrain Tiles on AWS Open Data (SRTM, USGS 3DEP, GMTED2010, ETOPO1, Copernicus EU-DEM and others).");
            terrainNote.AddToClassList("satellite-attribution");
            right.Add(terrainNote);

            Section(right, "Overlay on the ground");
            var showRow = new VisualElement();
            showRow.AddToClassList("satellite-row");
            showToggle.text = "Show";
            showToggle.AddToClassList("picker-option-toggle");
            showToggle.AddToClassList("panel-toggle");
            showToggle.RegisterValueChangedCallback(evt => OverlayChanged?.Invoke(evt.newValue, opacitySlider.value));
            showRow.Add(showToggle);
            opacitySlider.AddToClassList("new-map-slider");
            opacitySlider.AddToClassList("satellite-grow");
            opacitySlider.tooltip = "How opaque the pictures lie on the ground";
            opacitySlider.RegisterValueChangedCallback(evt =>
            {
                ShowOpacity(evt.newValue);
                OverlayChanged?.Invoke(showToggle.value, evt.newValue);
            });
            showRow.Add(opacitySlider);
            opacityLabel.AddToClassList("new-map-factor");
            showRow.Add(opacityLabel);
            right.Add(showRow);
            var note = new Label("The pictures stay in the project and are not exported. Check a source's terms before you share pictures made from it.");
            note.AddToClassList("satellite-note");
            right.Add(note);
            UpdateButtons();
        }

        public WorldMapElement WorldMap { get; }

        public event Action Closed;
        public event Action<string> SearchRequested;
        public event Action<OmsiPlace> PlaceChosen;

        /// <summary>The anchor tile's middle goes to this Web Mercator position.</summary>
        public event Action<OmsiMapPoint> PlaceRequested;

        public event Action<double> RotationChanged;

        /// <summary>The source chosen by name, or null for the sharpest.</summary>
        public event Action<string> SourceChanged;

        /// <summary>Loading asked for: the selected tile only or all, pictures, heights.</summary>
        public event Action<bool, bool, bool> LoadRequested;

        public event Action CancelRequested;

        /// <summary>The ticks for terrain heights and for new tiles changed.</summary>
        public event Action<bool, bool> TicksChanged;
        public event Action HeightOptionsChanged;
        public event Action MeasureAgainRequested;

        /// <summary>The overlay shown or not, and its opacity.</summary>
        public event Action<bool, float> OverlayChanged;

        /// <summary>The source chosen by name, or null for the sharpest.</summary>
        public string ChosenSource => sourceField.value == SharpestChoice ? null : sourceField.value;

        public bool Relative => relativeToggle.value;

        public double? Offset => Number(offsetField.value);

        public double? Smoothing => Number(smoothingField.value);

        /// <summary>Shows the project's settings and whether the map lies on the Earth by itself.</summary>
        public void Show(OmsiGeoSettings settings, bool worldCoordinates, string chosenSource)
        {
            worldLabel.style.display = worldCoordinates ? DisplayStyle.Flex : DisplayStyle.None;
            placeSection.style.display = worldCoordinates ? DisplayStyle.None : DisplayStyle.Flex;
            rotationField.SetValueWithoutNotify((settings.Anchor?.RotationDegrees ?? 0.0).ToString("0.##", CultureInfo.InvariantCulture));
            relativeToggle.SetValueWithoutNotify(settings.Heights.Relative);
            offsetField.SetValueWithoutNotify(settings.Heights.Offset.ToString("0.##", CultureInfo.InvariantCulture));
            smoothingField.SetValueWithoutNotify(settings.Heights.Smoothing.ToString("0.##", CultureInfo.InvariantCulture));
            baseLabel.text = settings.Heights.Relative && settings.Heights.Base.HasValue
                ? "Taken off: " + settings.Heights.Base.Value.ToString("0.00", CultureInfo.InvariantCulture) + " m"
                : settings.Heights.Relative ? "Measured at the middle tile on the first load." : string.Empty;
            measureButton.style.display = settings.Heights.Relative && settings.Heights.Base.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            showToggle.SetValueWithoutNotify(settings.ShowImagery);
            opacitySlider.SetValueWithoutNotify((float)settings.ImageryOpacity);
            ShowOpacity((float)settings.ImageryOpacity);
            sourceField.SetValueWithoutNotify(chosenSource ?? SharpestChoice);
            heightsToggle.SetValueWithoutNotify(settings.WithTerrain);
            autoToggle.SetValueWithoutNotify(settings.AutoNewTiles);
        }

        /// <summary>Where the anchor tile lies, or null while the map is not placed.</summary>
        public void ShowAnchor(OmsiGeoAnchor anchor, bool worldCoordinates)
        {
            placed = worldCoordinates || anchor != null;
            anchorLabel.text = anchor == null
                ? "1. Find the place.  2. Press \"Place the map here\" and click on the map of the world: the orange tile, the map's middle, goes there."
                : "Orange tile " + anchor.TileX + ", " + anchor.TileY + ": " + anchor.Latitude.ToString("0.000000", CultureInfo.InvariantCulture) + "°, " +
                  anchor.Longitude.ToString("0.000000", CultureInfo.InvariantCulture) + "°";
            UpdateButtons();
        }

        public void ShowAttribution(string text)
        {
            attributionLabel.text = text;
        }

        public void ShowDetail(string text)
        {
            detailLabel.text = text ?? string.Empty;
            detailLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void ShowPlaces(List<OmsiPlace> places, string problem)
        {
            results.Clear();
            if (problem != null)
            {
                var label = new Label(problem);
                label.AddToClassList("satellite-note");
                label.AddToClassList("export-problem");
                results.Add(label);
                return;
            }
            // A few places at most, so the window keeps its height.
            foreach (var place in places.GetRange(0, Math.Min(5, places.Count)))
            {
                var captured = place;
                var item = new Button(() => PlaceChosen?.Invoke(captured)) { text = place.Name, tooltip = place.Name };
                item.AddToClassList("satellite-result");
                results.Add(item);
            }
        }

        /// <summary>What loading does now, and whether it runs.</summary>
        public void ShowProgress(string text, bool running, bool problem = false)
        {
            busy = running;
            progressLabel.text = text ?? string.Empty;
            progressLabel.EnableInClassList("export-problem", problem);
            UpdateButtons();
        }

        /// <summary>Whether a tile is selected in the map, for "For the selected tile".</summary>
        public void ShowTileChosen(bool chosen)
        {
            tileChosen = chosen;
            UpdateButtons();
        }

        /// <summary>
        /// Lets the window be dragged by its title bar, kept inside the area around it. Once moved it stays where it was put
        /// while the program runs.
        /// </summary>
        private void MakeMovable(VisualElement header)
        {
            Vector2? grabbed = null;
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target is Button || (evt.target as VisualElement)?.GetFirstAncestorOfType<Button>() != null)
                    return;
                grabbed = (Vector2)evt.position - new Vector2(layout.x, layout.y);
                header.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!grabbed.HasValue || !header.HasPointerCapture(evt.pointerId))
                    return;
                MoveTo((Vector2)evt.position - grabbed.Value);
                evt.StopPropagation();
            });
            header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!header.HasPointerCapture(evt.pointerId))
                    return;
                header.ReleasePointer(evt.pointerId);
                grabbed = null;
                evt.StopPropagation();
            });
            // A smaller window around it keeps the window reachable.
            RegisterCallback<AttachToPanelEvent>(_ => parent?.RegisterCallback<GeometryChangedEvent>(__ =>
            {
                if (resolvedStyle.display != DisplayStyle.None)
                    MoveTo(new Vector2(layout.x, layout.y));
            }));
        }

        private void MoveTo(Vector2 position)
        {
            if (parent == null)
                return;
            var area = parent.contentRect;
            var x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, area.width - layout.width));
            var y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, area.height - layout.height));
            style.left = x;
            style.top = y;
        }

        private void UpdateButtons()
        {
            loadAllButton.SetEnabled(placed && !busy);
            loadTileButton.SetEnabled(placed && !busy && tileChosen);
            heightsOnlyButton.SetEnabled(placed && !busy);
            cancelButton.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            loadTileButton.tooltip = tileChosen ? "Pictures (and heights) for the tile selected in the map" : "Select a tile in the map first";
        }

        private void SetPlacing(bool on)
        {
            placing = on;
            placeButton.EnableInClassList("dialog-primary", on);
            placeButton.Q<Label>().text = on ? "Click on the map of the world..." : "Place the map here";
        }

        private void Search()
        {
            var text = searchField.value?.Trim();
            if (!string.IsNullOrEmpty(text))
                SearchRequested?.Invoke(text);
        }

        private void ApplyRotation()
        {
            var value = Number(rotationField.value);
            if (value.HasValue)
                RotationChanged?.Invoke(value.Value);
        }

        private void ShowOpacity(float value)
        {
            opacityLabel.text = Math.Round(value * 100f).ToString(CultureInfo.InvariantCulture) + " %";
        }

        private static double? Number(string text)
        {
            return double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (double?)null;
        }

        private static Button Button(string text, Action action, string tooltip, string icon = null)
        {
            var button = new Button(action) { text = icon == null ? text : string.Empty, tooltip = tooltip };
            button.AddToClassList("map-control-button");
            button.AddToClassList("satellite-button");
            if (icon != null)
            {
                button.Add(new IconElement(icon));
                button.Add(new Label(text) { pickingMode = PickingMode.Ignore });
            }
            return button;
        }

        private static void Section(VisualElement parent, string text)
        {
            var label = new Label(text.ToUpperInvariant());
            label.AddToClassList("inspector-section-heading");
            parent.Add(label);
        }

        private static VisualElement Row(VisualElement parent, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = new Label(label);
            text.AddToClassList("picker-option-label");
            text.AddToClassList("satellite-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }

        private static void Field(VisualElement row, TextField field, string tooltip)
        {
            field.AddToClassList("picker-field");
            field.tooltip = tooltip;
            row.tooltip = tooltip;
            row.Add(field);
        }
    }
}
