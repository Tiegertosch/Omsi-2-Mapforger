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

        private readonly IntegerField[] pictureTiles = { new IntegerField("x"), new IntegerField("y"), new IntegerField("x"), new IntegerField("y") };
        private readonly VisualElement pictureList = new VisualElement();
        private readonly VisualElement fitSection = new VisualElement();
        private readonly Label fitTitle = new Label();
        private readonly FloatField rotationValue = new FloatField("Turn (°)") { isDelayed = true };
        private readonly Toggle mirrorXToggle = new Toggle { text = "Mirror left-right" };
        private readonly Toggle mirrorYToggle = new Toggle { text = "Mirror top-bottom" };
        private readonly FloatField offsetXValue = new FloatField("East (m)") { isDelayed = true };
        private readonly FloatField offsetYValue = new FloatField("North (m)") { isDelayed = true };
        private readonly FloatField scaleValue = new FloatField("Size") { isDelayed = true };
        private readonly Label sizeLabel = new Label();
        private OmsiOwnPicture fitShown;
        private readonly DropdownField pictureSizeField = new DropdownField();
        private readonly VisualElement edgesRow = new VisualElement();
        private readonly Label edgesLabel = new Label();
        private readonly VisualElement keyRows = new VisualElement();
        private readonly VisualElement ownSourceRows = new VisualElement();
        private readonly TextField ownNameField = new TextField();
        private readonly TextField ownUrlField = new TextField();
        private readonly TextField ownAttributionField = new TextField();
        private readonly IntegerField ownZoomField = new IntegerField("Finest zoom") { value = 19 };
        private readonly Toggle ownWmsToggle = new Toggle { text = "WMS: each tile asked by its {bbox}" };
        private readonly Label ownProblem = new Label();

        /// <summary>The choices of "Picture of a tile" and the edges they stand for; 0 for every pixel of the source.</summary>
        private static readonly string[] PictureSizeNames = { "Every pixel (up to 4096)", "1024 × 1024", "2048 × 2048", "4096 × 4096" };
        private static readonly int[] PictureSizes = { 0, 1024, 2048, 4096 };

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

            // The user's own pictures.
            Section(left, "Own pictures");
            var ownNote = new Label("A scan, a screenshot, an old plan: laid on tiles and lined up by eye. No place on the Earth needed.");
            ownNote.AddToClassList("satellite-note");
            left.Add(ownNote);
            var tilesRow = new VisualElement();
            tilesRow.AddToClassList("satellite-row");
            var from = new Label("Tiles from");
            from.AddToClassList("satellite-inline-label");
            tilesRow.Add(from);
            for (var index = 0; index < pictureTiles.Length; index++)
            {
                if (index == 2)
                {
                    var to = new Label("to");
                    to.AddToClassList("satellite-inline-label");
                    tilesRow.Add(to);
                }
                pictureTiles[index].AddToClassList("satellite-number");
                pictureTiles[index].tooltip = index < 2 ? "The tile at the picture's south-west corner" : "The tile at the picture's north-east corner";
                tilesRow.Add(pictureTiles[index]);
            }
            left.Add(tilesRow);
            left.Add(Button("Load image file over these tiles...", () => PictureLoadRequested?.Invoke(pictureTiles[0].value, pictureTiles[1].value, pictureTiles[2].value,
                pictureTiles[3].value), "A JPEG, PNG, BMP or TGA picture stretched over these tiles; the copy stays in the project", "layers"));
            pictureList.AddToClassList("satellite-results");
            left.Add(pictureList);

            // Pictures from the air and heights for the tiles.
            Section(right, "Imagery");
            var sourceRow = Row(right, "Source");
            sourceField.AddToClassList("new-map-dropdown");
            sourceField.tooltip = "Where the pictures come from; none of them needs a key. The sharpest is chosen at the map's middle. " +
                                  "\"OpenStreetMap map\" draws the map as openstreetmap.org shows it, with streets' names, houses and stops, to build over by eye.";
            SetSources(OmsiImagerySource.Imagery);
            sourceField.SetValueWithoutNotify(SharpestChoice);
            sourceField.RegisterValueChangedCallback(evt => SourceChanged?.Invoke(evt.newValue == SharpestChoice ? null : evt.newValue));
            sourceRow.Add(sourceField);
            var sizeRow = Row(right, "Picture of a tile");
            pictureSizeField.AddToClassList("new-map-dropdown");
            pictureSizeField.choices = new List<string>(PictureSizeNames);
            pictureSizeField.SetValueWithoutNotify(PictureSizeNames[0]);
            pictureSizeField.tooltip = "How many pixels each tile's picture keeps. The map shows 1024 a tile and the full picture of the tiles in view when you zoom in.";
            pictureSizeField.RegisterValueChangedCallback(evt => PictureSizeChanged?.Invoke(PictureSizes[Math.Max(0, Array.IndexOf(PictureSizeNames, evt.newValue))]));
            sizeRow.Add(pictureSizeField);
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
            edgesRow.AddToClassList("satellite-row");
            edgesRow.AddToClassList("satellite-wrap");
            edgesLabel.AddToClassList("satellite-note");
            edgesLabel.AddToClassList("satellite-warning");
            edgesLabel.AddToClassList("satellite-grow");
            edgesRow.Add(edgesLabel);
            edgesRow.Add(Button("Fit their edges", () => EdgesFitRequested?.Invoke(), "Their edge takes the loaded heights, fading out 60 m into the tile; Ctrl+Z takes it back"));
            edgesRow.Add(Button("Leave them", () => EdgesLeft?.Invoke(), "Keeps the step at their edge"));
            edgesRow.style.display = DisplayStyle.None;
            right.Add(edgesRow);

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

            // Lining up the own picture chosen in the list.
            right.Add(fitSection);
            fitTitle.AddToClassList("inspector-section-heading");
            fitSection.Add(fitTitle);
            var turnRow = new VisualElement();
            turnRow.AddToClassList("satellite-row");
            turnRow.Add(Button("Turn left 90°", () => ChangeFit(fit => fit.Rotation = Wrap(fit.Rotation - 90.0)), "Turns the picture a quarter anticlockwise"));
            turnRow.Add(Button("Turn right 90°", () => ChangeFit(fit => fit.Rotation = Wrap(fit.Rotation + 90.0)), "Turns the picture a quarter clockwise"));
            rotationValue.AddToClassList("satellite-number");
            rotationValue.tooltip = "Turned clockwise in degrees; drag the label or type a value";
            rotationValue.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.Rotation = Wrap(evt.newValue)));
            turnRow.Add(rotationValue);
            fitSection.Add(turnRow);
            var mirrorRow = new VisualElement();
            mirrorRow.AddToClassList("satellite-row");
            foreach (var toggle in new[] { mirrorXToggle, mirrorYToggle })
            {
                toggle.AddToClassList("picker-option-toggle");
                toggle.AddToClassList("panel-toggle");
                toggle.AddToClassList("satellite-mirror");
                mirrorRow.Add(toggle);
            }
            mirrorXToggle.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.MirrorX = evt.newValue));
            mirrorYToggle.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.MirrorY = evt.newValue));
            fitSection.Add(mirrorRow);
            var moveRow = new VisualElement();
            moveRow.AddToClassList("satellite-row");
            foreach (var field in new[] { offsetXValue, offsetYValue, scaleValue })
            {
                field.AddToClassList("satellite-number");
                moveRow.Add(field);
            }
            offsetXValue.tooltip = "Moved east in metres; drag the label or type a value";
            offsetYValue.tooltip = "Moved north in metres; drag the label or type a value";
            scaleValue.tooltip = "Size against the tiles, 1 for stretched over them; drag the label or type a value";
            offsetXValue.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.OffsetX = evt.newValue));
            offsetYValue.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.OffsetY = evt.newValue));
            scaleValue.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.Scale = Math.Max(0.05, Math.Min(20.0, evt.newValue))));
            fitSection.Add(moveRow);
            var fitButtons = new VisualElement();
            fitButtons.AddToClassList("satellite-row");
            sizeLabel.AddToClassList("satellite-note");
            sizeLabel.AddToClassList("satellite-grow");
            fitButtons.Add(sizeLabel);
            fitButtons.Add(Button("Reset", () => ChangeFit(fit => fit.ResetFit()), "Stretched over its tiles again, unturned and unmirrored"));
            var remove = Button("Remove", () =>
            {
                if (fitShown != null)
                    PictureRemoved?.Invoke(fitShown.Id);
            }, "Takes the picture off its tiles and out of the project");
            remove.AddToClassList("inspector-edit-delete");
            fitButtons.Add(remove);
            fitSection.Add(fitButtons);
            fitSection.style.display = DisplayStyle.None;

            // Sources with the user's own key, and the user's own tile servers; both stay on this computer.
            var keys = new Foldout { text = "Keys for more sources (optional)", value = false };
            keys.AddToClassList("satellite-foldout");
            var keysNote = new Label("Everything works without a key. With your own free key these may be sharper in some places; the key stays on this computer.");
            keysNote.AddToClassList("satellite-note");
            keys.Add(keysNote);
            keys.Add(keyRows);
            right.Add(keys);
            var own = new Foldout { text = "Your own tile server", value = false };
            own.AddToClassList("satellite-foldout");
            Field(Row(own, "Name"), ownNameField, "How the server is called in the source list");
            Field(Row(own, "URL"), ownUrlField, "{z} {x} {y} are filled in; a WMS gets {bbox} (EPSG:3857) and {width} {height}; {key} is your key");
            Field(Row(own, "Attribution"), ownAttributionField, "Shown under the map while its pictures show");
            foreach (var field in new[] { ownNameField, ownUrlField, ownAttributionField })
                field.AddToClassList("satellite-wide-field");
            var zoomRow = new VisualElement();
            zoomRow.AddToClassList("satellite-row");
            ownZoomField.AddToClassList("satellite-number");
            ownZoomField.tooltip = "The finest zoom the server has";
            zoomRow.Add(ownZoomField);
            ownWmsToggle.AddToClassList("picker-option-toggle");
            ownWmsToggle.AddToClassList("panel-toggle");
            zoomRow.Add(ownWmsToggle);
            own.Add(zoomRow);
            var serverNote = new Label("{z} {x} {y} are filled in; a WMS gets {bbox} (EPSG:3857) and {width} {height}; {key} is your key.");
            serverNote.AddToClassList("satellite-note");
            own.Add(serverNote);
            ownProblem.AddToClassList("satellite-note");
            ownProblem.AddToClassList("export-problem");
            own.Add(ownProblem);
            own.Add(Button("Add", () =>
            {
                ownProblem.text = string.Empty;
                OwnSourceAdded?.Invoke(ownNameField.value, ownUrlField.value, ownAttributionField.value, ownZoomField.value, ownWmsToggle.value);
            }, "Adds the server to the source list", "plus"));
            own.Add(ownSourceRows);
            right.Add(own);
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

        /// <summary>"Picture of a tile" changed: the edge in pixels, 0 for every pixel of the source.</summary>
        public event Action<int> PictureSizeChanged;

        public event Action EdgesFitRequested;
        public event Action EdgesLeft;

        /// <summary>A key was typed for a source, by the source's name; empty to take it away.</summary>
        public event Action<string, string> KeyChanged;

        /// <summary>An own tile server: name, URL, attribution, finest zoom, WMS.</summary>
        public event Action<string, string, string, int, bool> OwnSourceAdded;

        public event Action<string> OwnSourceRemoved;

        /// <summary>A picture file is to be laid over the tiles from west, south to east, north.</summary>
        public event Action<int, int, int, int> PictureLoadRequested;

        public event Action<string> PictureSelected;

        /// <summary>The chosen picture's fit changed: a copy with the new values.</summary>
        public event Action<OmsiOwnPicture> PictureFitChanged;

        public event Action<string> PictureRemoved;

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
            sourceField.SetValueWithoutNotify(chosenSource != null && sourceField.choices.Contains(chosenSource) ? chosenSource : SharpestChoice);
            pictureSizeField.SetValueWithoutNotify(PictureSizeNames[Math.Max(0, Array.IndexOf(PictureSizes, settings.PictureSize))]);
            heightsToggle.SetValueWithoutNotify(settings.WithTerrain);
            autoToggle.SetValueWithoutNotify(settings.AutoNewTiles);
        }

        /// <summary>The sources to choose from: the sharpest, these, and the drawn OpenStreetMap map.</summary>
        public void SetSources(IEnumerable<OmsiImagerySource> sources)
        {
            var choices = new List<string> { SharpestChoice };
            foreach (var source in sources)
                choices.Add(source.Name);
            // The map as OpenStreetMap draws it, made from its data with streets' names, in place of a picture from the air.
            choices.Add(OmsiImagerySource.OsmMap.Name);
            sourceField.choices = choices;
            if (!choices.Contains(sourceField.value))
                sourceField.SetValueWithoutNotify(SharpestChoice);
        }

        /// <summary>The sources that need a key with the key entered for each (shown hidden), and the user's own servers.</summary>
        public void ShowKeys(IEnumerable<OmsiImagerySource> sources, Func<string, string> key)
        {
            keyRows.Clear();
            ownSourceRows.Clear();
            foreach (var source in sources)
            {
                if (source.NeedsKey)
                {
                    var name = source.Name;
                    var row = Row(keyRows, name);
                    var field = new TextField { isPasswordField = true, value = key(name) ?? string.Empty };
                    field.textEdition.placeholder = "key";
                    Field(row, field, "Your key for " + name + "; it stays on this computer");
                    field.AddToClassList("satellite-wide-field");
                    field.RegisterCallback<FocusOutEvent>(_ => KeyChanged?.Invoke(name, field.value));
                    if (!string.IsNullOrEmpty(source.Terms))
                    {
                        var link = source.Terms;
                        row.Add(Button("Get a key", () => Application.OpenURL(link), "Opens " + link + " in your browser"));
                    }
                }
                if (source.IsOwn)
                {
                    var name = source.Name;
                    var row = new VisualElement();
                    row.AddToClassList("satellite-row");
                    var label = new Label(name + (source.Kind == OmsiImageryKind.Wms ? " (WMS)" : string.Empty) + ", zoom " + source.MaxZoom);
                    label.AddToClassList("satellite-note");
                    label.AddToClassList("satellite-grow");
                    row.Add(label);
                    var remove = Button("Remove", () => OwnSourceRemoved?.Invoke(name), "Takes the server out of the source list");
                    remove.AddToClassList("inspector-edit-delete");
                    row.Add(remove);
                    ownSourceRows.Add(row);
                }
            }
        }

        /// <summary>Why an own tile server could not be added; null when it was.</summary>
        public void ShowOwnSourceProblem(string problem)
        {
            ownProblem.text = problem ?? string.Empty;
            if (problem == null)
            {
                ownNameField.SetValueWithoutNotify(string.Empty);
                ownUrlField.SetValueWithoutNotify(string.Empty);
                ownAttributionField.SetValueWithoutNotify(string.Empty);
            }
        }

        /// <summary>How many tiles beside loaded heights step at their edge; 0 hides the question.</summary>
        public void ShowSteppedEdges(int tiles)
        {
            edgesRow.style.display = tiles > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            edgesLabel.text = tiles + " tile(s) beside the loaded heights were not loaded: the ground steps at their edge.";
        }

        /// <summary>The rectangle of tiles a new picture goes over.</summary>
        public void SetPictureTiles(int west, int south, int east, int north)
        {
            pictureTiles[0].SetValueWithoutNotify(west);
            pictureTiles[1].SetValueWithoutNotify(south);
            pictureTiles[2].SetValueWithoutNotify(east);
            pictureTiles[3].SetValueWithoutNotify(north);
        }

        /// <summary>The own pictures, the chosen one with its fit; its picture's size, when known, for the size in metres.</summary>
        public void ShowPictures(List<OmsiOwnPicture> pictures, OmsiOwnPicture chosen, double tileSize, OmsiImage image)
        {
            pictureList.Clear();
            foreach (var picture in pictures)
            {
                var captured = picture.Id;
                var count = (picture.East - picture.West + 1) * (picture.North - picture.South + 1);
                var item = new Button(() => PictureSelected?.Invoke(captured))
                {
                    text = picture.File + " (" + count + " tile" + (count == 1 ? "" : "s") + " from " + picture.West + ", " + picture.South + ")"
                };
                item.AddToClassList("satellite-result");
                item.EnableInClassList("satellite-result-chosen", chosen != null && chosen.Id == picture.Id);
                pictureList.Add(item);
            }
            fitShown = chosen?.Clone();
            fitSection.style.display = chosen != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (chosen == null)
                return;
            fitTitle.text = ("Line up " + chosen.File).ToUpperInvariant();
            rotationValue.SetValueWithoutNotify((float)chosen.Rotation);
            mirrorXToggle.SetValueWithoutNotify(chosen.MirrorX);
            mirrorYToggle.SetValueWithoutNotify(chosen.MirrorY);
            offsetXValue.SetValueWithoutNotify((float)chosen.OffsetX);
            offsetYValue.SetValueWithoutNotify((float)chosen.OffsetY);
            scaleValue.SetValueWithoutNotify((float)chosen.Scale);
            chosen.Frame(tileSize, out _, out _, out var width, out var height, out _);
            sizeLabel.text = width.ToString("0.0", CultureInfo.InvariantCulture) + " × " + height.ToString("0.0", CultureInfo.InvariantCulture) + " m" +
                             (image != null ? ", " + (width / image.Width).ToString("0.00", CultureInfo.InvariantCulture) + " m a pixel" : string.Empty);
        }

        private void ChangeFit(Action<OmsiOwnPicture> change)
        {
            if (fitShown == null)
                return;
            var fit = fitShown.Clone();
            change(fit);
            PictureFitChanged?.Invoke(fit);
        }

        private static double Wrap(double degrees)
        {
            var wrapped = (degrees + 180.0) % 360.0;
            return (wrapped < 0 ? wrapped + 360.0 : wrapped) - 180.0;
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
