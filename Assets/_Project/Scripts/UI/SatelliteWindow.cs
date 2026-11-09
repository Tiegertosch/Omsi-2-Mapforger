using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
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
        /// <summary>The first choice of the source list, in the language in use.</summary>
        public static string SharpestChoice => Loc.T("satellite.sharpest");

        private readonly TextField searchField = new TextField();
        private readonly VisualElement results = new VisualElement();
        private readonly Label attributionLabel = new Label();
        private readonly VisualElement placeSection = new VisualElement();
        private readonly Label worldLabel = new Label();
        private readonly Button placeButton;
        private readonly TextField rotationField = new TextField();
        private readonly Label anchorLabel = new Label();
        private readonly Label sharpLabel = new Label();
        private readonly TextField positionField = new TextField();
        private readonly TextField scaleField = new TextField();
        private readonly TextField stepField = new TextField { value = "1" };
        private readonly Label referenceLabel = new Label();
        private readonly Button[] pickButtons = new Button[3];
        private static readonly string[] PickKeys = { "satellite.pick.map", "satellite.pick.point1", "satellite.pick.point2" };
        private readonly TextField[] fitFields = { new TextField(), new TextField() };
        private readonly Label[] fitLabels = { new Label(), new Label() };
        private readonly Toggle fitScaleToggle = new Toggle();
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
        private readonly Toggle levelWaterToggle = new Toggle();
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
        private readonly FloatField rotationValue = new FloatField { isDelayed = true };
        private readonly Toggle mirrorXToggle = new Toggle();
        private readonly Toggle mirrorYToggle = new Toggle();
        private readonly FloatField offsetXValue = new FloatField { isDelayed = true };
        private readonly FloatField offsetYValue = new FloatField { isDelayed = true };
        private readonly FloatField scaleValue = new FloatField { isDelayed = true };
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
        private readonly IntegerField ownZoomField = new IntegerField { value = 19 };
        private readonly Toggle ownWmsToggle = new Toggle();
        private readonly Label ownProblem = new Label();

        /// <summary>The choices of "Picture of a tile" and the edges they stand for; 0 for every pixel of the source.</summary>
        private static string[] PictureSizeNames => new[] { Loc.T("satellite.size.every"), "1024 × 1024", "2048 × 2048", "4096 × 4096" };
        private static readonly int[] PictureSizes = { 0, 1024, 2048, 4096 };

        /// <summary>The names the command line knows the tabs by.</summary>
        private static readonly string[] TabNames = { "Place", "Pictures", "Terrain", "More" };

        private static readonly string[] TabKeys = { "satellite.tab.place", "satellite.tab.pictures", "satellite.tab.terrain", "satellite.tab.more" };

        private static readonly string[] TabTipKeys = { "satellite.tab.place.tooltip", "satellite.tab.pictures.tooltip", "satellite.tab.terrain.tooltip", "satellite.tab.more.tooltip" };

        private IReadOnlyList<OmsiImagerySource> shownSources = new List<OmsiImagerySource>();

        private readonly VisualElement[] tabs = new VisualElement[4];
        private readonly Button[] tabButtons = new Button[4];
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
            var title = LocBindings.NewLabel("satellite.title");
            title.AddToClassList("floating-panel-title");
            header.Add(title);
            var close = LocBindings.WithTooltip(new Button(() => Closed?.Invoke()), "satellite.close.tooltip");
            close.AddToClassList("panel-collapse-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            Add(header);
            MakeMovable(header);

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("floating-panel-content");
            body.AddToClassList("satellite-body");
            Add(body);

            // One column, four tabs by task: what to do first is on the first tab and the rest is one click away.
            var tabBar = new VisualElement();
            tabBar.AddToClassList("panel-tabs");
            tabBar.AddToClassList("satellite-tabs");
            Insert(IndexOf(body), tabBar);
            for (var index = 0; index < TabNames.Length; index++)
            {
                var chosen = index;
                tabButtons[index] = LocBindings.WithTooltip(LocBindings.NewButton(TabKeys[index], () => ShowTab(chosen)), TabTipKeys[index]); // i18n:keys
                tabButtons[index].AddToClassList("panel-tab");
                tabBar.Add(tabButtons[index]);
                tabs[index] = new VisualElement();
                tabs[index].AddToClassList("satellite-tab");
                body.Add(tabs[index]);
            }
            var placeTab = tabs[0];
            var picturesTab = tabs[1];
            var terrainTab = tabs[2];
            var moreTab = tabs[3];

            // Loading goes on whichever tab is open, so its progress stays in view below the tabs.
            var footer = new VisualElement();
            footer.AddToClassList("satellite-footer");
            Add(footer);

            // Find a place and see it from the air.
            var searchRow = new VisualElement();
            searchRow.AddToClassList("satellite-row");
            searchField.AddToClassList("picker-field");
            searchField.AddToClassList("satellite-search");
            searchField.textEdition.placeholder = Loc.T("satellite.search.placeholder");
            LocBindings.Tooltip(searchField, "satellite.search.tooltip");
            searchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Return || evt.keyCode == UnityEngine.KeyCode.KeypadEnter)
                    Search();
            }, TrickleDown.TrickleDown);
            searchRow.Add(searchField);
            searchRow.Add(Button("satellite.find", Search, "satellite.find.tooltip", "search"));
            placeTab.Add(searchRow);
            results.AddToClassList("satellite-results");
            placeTab.Add(results);
            WorldMap = new WorldMapElement();
            WorldMap.Clicked += mercator =>
            {
                if (placing)
                {
                    SetPlacing(false);
                    PlaceRequested?.Invoke(mercator);
                }
            };
            placeTab.Add(WorldMap);
            attributionLabel.AddToClassList("satellite-attribution");
            placeTab.Add(attributionLabel);

            // Where the map lies.
            Section(placeTab, "satellite.section.place");
            worldLabel.AddToClassList("satellite-note");
            LocBindings.Text(worldLabel, "satellite.world");
            placeTab.Add(worldLabel);
            placeSection.AddToClassList("satellite-place");
            placeTab.Add(placeSection);
            var placeRow = new VisualElement();
            placeRow.AddToClassList("satellite-row");
            placeButton = Button("satellite.place_here", () => SetPlacing(!placing), "satellite.place_here.tooltip", "crosshair");
            placeRow.Add(placeButton);
            placeRow.Add(Button("satellite.at_middle", () => PlaceRequested?.Invoke(WorldMap.Centre), "satellite.at_middle.tooltip"));
            placeSection.Add(placeRow);
            var rotationRow = Row(placeSection, "satellite.rotation");
            Field(rotationRow, rotationField, "satellite.rotation.tooltip");
            rotationField.RegisterCallback<FocusOutEvent>(_ => ApplyRotation());
            rotationField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Return || evt.keyCode == UnityEngine.KeyCode.KeypadEnter)
                    ApplyRotation();
            }, TrickleDown.TrickleDown);
            anchorLabel.AddToClassList("satellite-note");
            placeSection.Add(anchorLabel);
            BuildExactPlace();

            // The user's own pictures.
            Section(moreTab, "satellite.section.own");
            var ownNote = LocBindings.NewLabel("satellite.own.note");
            ownNote.AddToClassList("satellite-note");
            moreTab.Add(ownNote);
            var tilesRow = new VisualElement();
            tilesRow.AddToClassList("satellite-row");
            var from = LocBindings.NewLabel("satellite.tiles_from");
            from.AddToClassList("satellite-inline-label");
            tilesRow.Add(from);
            for (var index = 0; index < pictureTiles.Length; index++)
            {
                if (index == 2)
                {
                    var to = LocBindings.NewLabel("satellite.tiles_to");
                    to.AddToClassList("satellite-inline-label");
                    tilesRow.Add(to);
                }
                pictureTiles[index].AddToClassList("satellite-number");
                LocBindings.Tooltip(pictureTiles[index], index < 2 ? "satellite.tile.sw" : "satellite.tile.ne"); // i18n:keys
                tilesRow.Add(pictureTiles[index]);
            }
            moreTab.Add(tilesRow);
            moreTab.Add(Button("satellite.load_file", () => PictureLoadRequested?.Invoke(pictureTiles[0].value, pictureTiles[1].value, pictureTiles[2].value,
                pictureTiles[3].value), "satellite.load_file.tooltip", "layers"));
            pictureList.AddToClassList("satellite-results");
            moreTab.Add(pictureList);
            moreTab.Add(fitSection);

            // Pictures from the air and heights for the tiles.
            Section(picturesTab, "satellite.section.imagery");
            var sourceRow = Row(picturesTab, "satellite.source");
            sourceField.AddToClassList("new-map-dropdown");
            LocBindings.Tooltip(sourceField, "satellite.source.tooltip");
            SetSources(OmsiImagerySource.Imagery);
            sourceField.SetValueWithoutNotify(SharpestChoice);
            sourceField.RegisterValueChangedCallback(evt => SourceChanged?.Invoke(evt.newValue == SharpestChoice ? null : evt.newValue));
            sourceRow.Add(sourceField);
            var sizeRow = Row(picturesTab, "satellite.tile_picture");
            pictureSizeField.AddToClassList("new-map-dropdown");
            pictureSizeField.choices = new List<string>(PictureSizeNames);
            pictureSizeField.SetValueWithoutNotify(PictureSizeNames[0]);
            LocBindings.Tooltip(pictureSizeField, "satellite.tile_picture.tooltip");
            pictureSizeField.RegisterValueChangedCallback(evt => PictureSizeChanged?.Invoke(PictureSizes[Math.Max(0, Array.IndexOf(PictureSizeNames, evt.newValue))]));
            sizeRow.Add(pictureSizeField);
            var copiesHolder = new VisualElement();
            var copiesRow = Row(copiesHolder, "satellite.copies");
            LocBindings.Tooltip(copiesRow, "satellite.copies.tooltip");
            sharpLabel.AddToClassList("satellite-note");
            sharpLabel.AddToClassList("satellite-grow");
            copiesRow.Add(sharpLabel);
            copiesRow.Add(Button("satellite.copies.clear", () => ClearSharpRequested?.Invoke(), "satellite.copies.clear.tooltip"));
            detailLabel.AddToClassList("satellite-note");
            picturesTab.Add(detailLabel);
            LocBindings.Text(heightsToggle, "satellite.with_heights");
            heightsToggle.AddToClassList("picker-option-toggle");
            heightsToggle.AddToClassList("panel-toggle");
            LocBindings.Tooltip(heightsToggle, "satellite.with_heights.tooltip");
            heightsToggle.RegisterValueChangedCallback(evt => TicksChanged?.Invoke(evt.newValue, autoToggle.value));
            picturesTab.Add(heightsToggle);
            var loadRow = new VisualElement();
            loadRow.AddToClassList("satellite-row");
            loadRow.AddToClassList("satellite-wrap");
            loadAllButton = Button("satellite.load_all", () => LoadRequested?.Invoke(false, true, heightsToggle.value), "satellite.load_all.tooltip");
            loadAllButton.AddToClassList("dialog-primary");
            loadTileButton = Button("satellite.load_tile", () => LoadRequested?.Invoke(true, true, heightsToggle.value), "satellite.load_tile.tooltip");
            heightsOnlyButton = Button("satellite.heights_only", () => LoadRequested?.Invoke(false, false, true), "satellite.heights_only.tooltip");
            loadRow.Add(loadAllButton);
            loadRow.Add(loadTileButton);
            picturesTab.Add(loadRow);
            // The heights button belongs to the terrain tab, at its top.
            Section(terrainTab, "satellite.section.terrain");
            terrainTab.Add(heightsOnlyButton);
            LocBindings.Text(autoToggle, "satellite.auto");
            autoToggle.AddToClassList("picker-option-toggle");
            autoToggle.AddToClassList("panel-toggle");
            LocBindings.Tooltip(autoToggle, "satellite.auto.tooltip");
            autoToggle.RegisterValueChangedCallback(evt => TicksChanged?.Invoke(heightsToggle.value, evt.newValue));
            picturesTab.Add(autoToggle);
            var progressRow = new VisualElement();
            progressRow.AddToClassList("satellite-row");
            progressLabel.AddToClassList("satellite-progress");
            progressRow.Add(progressLabel);
            cancelButton = Button("satellite.cancel", () => CancelRequested?.Invoke(), "satellite.cancel.tooltip");
            progressRow.Add(cancelButton);
            footer.Add(progressRow);
            edgesRow.AddToClassList("satellite-row");
            edgesRow.AddToClassList("satellite-wrap");
            edgesLabel.AddToClassList("satellite-note");
            edgesLabel.AddToClassList("satellite-warning");
            edgesLabel.AddToClassList("satellite-grow");
            edgesRow.Add(edgesLabel);
            edgesRow.Add(Button("satellite.edges.fit", () => EdgesFitRequested?.Invoke(), "satellite.edges.fit.tooltip"));
            edgesRow.Add(Button("satellite.edges.leave", () => EdgesLeft?.Invoke(), "satellite.edges.leave.tooltip"));
            edgesRow.style.display = DisplayStyle.None;
            footer.Add(edgesRow);

            Section(terrainTab, "satellite.section.height_options");
            LocBindings.Text(relativeToggle, "satellite.relative");
            relativeToggle.AddToClassList("picker-option-toggle");
            relativeToggle.AddToClassList("panel-toggle");
            LocBindings.Tooltip(relativeToggle, "satellite.relative.tooltip");
            relativeToggle.RegisterValueChangedCallback(_ => HeightOptionsChanged?.Invoke());
            terrainTab.Add(relativeToggle);
            LocBindings.Text(levelWaterToggle, "satellite.level_water");
            levelWaterToggle.AddToClassList("picker-option-toggle");
            levelWaterToggle.AddToClassList("panel-toggle");
            LocBindings.Tooltip(levelWaterToggle, "satellite.level_water.tooltip");
            levelWaterToggle.RegisterValueChangedCallback(_ => HeightOptionsChanged?.Invoke());
            terrainTab.Add(levelWaterToggle);
            Field(Row(terrainTab, "satellite.raise"), offsetField, "satellite.raise.tooltip");
            Field(Row(terrainTab, "satellite.smoothing"), smoothingField, "satellite.smoothing.tooltip");
            offsetField.RegisterCallback<FocusOutEvent>(_ => HeightOptionsChanged?.Invoke());
            smoothingField.RegisterCallback<FocusOutEvent>(_ => HeightOptionsChanged?.Invoke());
            var baseRow = new VisualElement();
            baseRow.AddToClassList("satellite-row");
            baseLabel.AddToClassList("satellite-note");
            baseLabel.AddToClassList("satellite-grow");
            baseRow.Add(baseLabel);
            measureButton = Button("satellite.measure", () => MeasureAgainRequested?.Invoke(), "satellite.measure.tooltip");
            baseRow.Add(measureButton);
            terrainTab.Add(baseRow);
            var terrainNote = LocBindings.NewLabel("satellite.terrain_note");
            terrainNote.AddToClassList("satellite-attribution");
            terrainTab.Add(terrainNote);

            Section(picturesTab, "satellite.section.overlay");
            var showRow = new VisualElement();
            showRow.AddToClassList("satellite-row");
            LocBindings.Text(showToggle, "satellite.show");
            showToggle.AddToClassList("picker-option-toggle");
            showToggle.AddToClassList("panel-toggle");
            showToggle.RegisterValueChangedCallback(evt => OverlayChanged?.Invoke(evt.newValue, opacitySlider.value));
            showRow.Add(showToggle);
            opacitySlider.AddToClassList("new-map-slider");
            opacitySlider.AddToClassList("satellite-grow");
            LocBindings.Tooltip(opacitySlider, "satellite.opacity.tooltip");
            opacitySlider.RegisterValueChangedCallback(evt =>
            {
                ShowOpacity(evt.newValue);
                OverlayChanged?.Invoke(showToggle.value, evt.newValue);
            });
            showRow.Add(opacitySlider);
            opacityLabel.AddToClassList("new-map-factor");
            showRow.Add(opacityLabel);
            picturesTab.Add(showRow);
            var note = LocBindings.NewLabel("satellite.overlay_note");
            note.AddToClassList("satellite-note");
            picturesTab.Add(note);

            // Lining up the own picture chosen in the list (shown below the list of own pictures).
            fitTitle.AddToClassList("inspector-section-heading");
            fitSection.Add(fitTitle);
            var turnRow = new VisualElement();
            turnRow.AddToClassList("satellite-row");
            turnRow.Add(Button("satellite.turn_left", () => ChangeFit(fit => fit.Rotation = Wrap(fit.Rotation - 90.0)), "satellite.turn_left.tooltip"));
            turnRow.Add(Button("satellite.turn_right", () => ChangeFit(fit => fit.Rotation = Wrap(fit.Rotation + 90.0)), "satellite.turn_right.tooltip"));
            rotationValue.AddToClassList("satellite-number");
            LocBindings.Text(rotationValue, "satellite.turn");
            LocBindings.Tooltip(rotationValue, "satellite.turn.tooltip");
            LocBindings.Text(mirrorXToggle, "satellite.mirror_x");
            LocBindings.Text(mirrorYToggle, "satellite.mirror_y");
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
            LocBindings.Text(offsetXValue, "satellite.east");
            LocBindings.Tooltip(offsetXValue, "satellite.east.tooltip");
            LocBindings.Text(offsetYValue, "satellite.north");
            LocBindings.Tooltip(offsetYValue, "satellite.north.tooltip");
            LocBindings.Text(scaleValue, "satellite.size");
            LocBindings.Tooltip(scaleValue, "satellite.size.tooltip");
            offsetXValue.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.OffsetX = evt.newValue));
            offsetYValue.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.OffsetY = evt.newValue));
            scaleValue.RegisterValueChangedCallback(evt => ChangeFit(fit => fit.Scale = Math.Max(0.05, Math.Min(20.0, evt.newValue))));
            fitSection.Add(moveRow);
            var fitButtons = new VisualElement();
            fitButtons.AddToClassList("satellite-row");
            sizeLabel.AddToClassList("satellite-note");
            sizeLabel.AddToClassList("satellite-grow");
            fitButtons.Add(sizeLabel);
            fitButtons.Add(Button("satellite.reset", () => ChangeFit(fit => fit.ResetFit()), "satellite.reset.tooltip"));
            var remove = Button("satellite.remove", () =>
            {
                if (fitShown != null)
                    PictureRemoved?.Invoke(fitShown.Id);
            }, "satellite.remove.tooltip");
            remove.AddToClassList("inspector-edit-delete");
            fitButtons.Add(remove);
            fitSection.Add(fitButtons);
            fitSection.style.display = DisplayStyle.None;

            // Sources with the user's own key, and the user's own tile servers; both stay on this computer.
            var keys = new Foldout { value = false, name = "keys" };
            LocBindings.Text(keys, "satellite.keys.fold");
            keys.AddToClassList("satellite-foldout");
            var keysNote = LocBindings.NewLabel("satellite.keys.note");
            keysNote.AddToClassList("satellite-note");
            keys.Add(keysNote);
            keys.Add(keyRows);
            moreTab.Add(keys);
            var own = new Foldout { value = false, name = "server" };
            LocBindings.Text(own, "satellite.own.fold");
            own.AddToClassList("satellite-foldout");
            Field(Row(own, "satellite.own.name"), ownNameField, "satellite.own.name.tooltip");
            Field(Row(own, "satellite.own.url"), ownUrlField, "satellite.own.url.tooltip", UrlPlaceholders);
            Field(Row(own, "satellite.own.attribution"), ownAttributionField, "satellite.own.attribution.tooltip");
            foreach (var field in new[] { ownNameField, ownUrlField, ownAttributionField })
                field.AddToClassList("satellite-wide-field");
            var zoomRow = new VisualElement();
            zoomRow.AddToClassList("satellite-row");
            ownZoomField.AddToClassList("satellite-number");
            LocBindings.Text(ownZoomField, "satellite.own.zoom");
            LocBindings.Tooltip(ownZoomField, "satellite.own.zoom.tooltip");
            LocBindings.Text(ownWmsToggle, "satellite.own.wms", ("bbox", "{bbox}"));
            zoomRow.Add(ownZoomField);
            ownWmsToggle.AddToClassList("picker-option-toggle");
            ownWmsToggle.AddToClassList("panel-toggle");
            zoomRow.Add(ownWmsToggle);
            own.Add(zoomRow);
            var serverNote = new Label();
            LocBindings.Text(serverNote, "satellite.own.url_note", ("zxy", "{z} {x} {y}"), ("bbox", "{bbox}"), ("widthHeight", "{width} {height}"), ("key", "{key}"));
            serverNote.AddToClassList("satellite-note");
            own.Add(serverNote);
            ownProblem.AddToClassList("satellite-note");
            ownProblem.AddToClassList("export-problem");
            own.Add(ownProblem);
            own.Add(Button("satellite.own.add", () =>
            {
                ownProblem.text = string.Empty;
                OwnSourceAdded?.Invoke(ownNameField.value, ownUrlField.value, ownAttributionField.value, ownZoomField.value, ownWmsToggle.value);
            }, "satellite.own.add.tooltip", "plus"));
            own.Add(ownSourceRows);
            moreTab.Add(own);
            Section(moreTab, "satellite.section.disk");
            moreTab.Add(copiesHolder);
            ShowTab(0);
            UpdateButtons();
        }

        public WorldMapElement WorldMap { get; }

        public event Action Closed;
        public event Action<string> SearchRequested;
        public event Action<OmsiPlace> PlaceChosen;

        /// <summary>The anchor tile's middle goes to this Web Mercator position.</summary>
        public event Action<OmsiMapPoint> PlaceRequested;

        public event Action<double> RotationChanged;

        /// <summary>A point of the map is to be picked with a click: 0 for the reference point, 1 and 2 for the two points of a fit.</summary>
        public event Action<int> PickRequested;

        /// <summary>The reference point should be the middle of its tile again.</summary>
        public event Action ReferenceCentred;

        /// <summary>Text typed or pasted for where the reference point lies on the Earth.</summary>
        public event Action<string> PositionEntered;

        /// <summary>How many metres of ground one metre of the map covers.</summary>
        public event Action<double> ScaleChanged;

        /// <summary>The map is to move east and north on the Earth by these metres.</summary>
        public event Action<double, double> MoveRequested;

        /// <summary>The two points of a fit with the text of their positions, and whether the scale is fitted too.</summary>
        public event Action<string, string, bool> FitRequested;

        /// <summary>The source chosen by name, or null for the sharpest.</summary>
        public event Action<string> SourceChanged;

        /// <summary>Loading asked for: the selected tile only or all, pictures, heights.</summary>
        public event Action<bool, bool, bool> LoadRequested;

        public event Action CancelRequested;

        /// <summary>"Picture of a tile" changed: the edge in pixels, 0 for every pixel of the source.</summary>
        public event Action<int> PictureSizeChanged;

        /// <summary>The compressed copies for fast zooming are to be deleted.</summary>
        public event Action ClearSharpRequested;

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

        public bool LevelWater => levelWaterToggle.value;

        public double? Offset => Number(offsetField.value);

        public double? Smoothing => Number(smoothingField.value);

        /// <summary>Shows the project's settings and whether the map lies on the Earth by itself.</summary>
        public void Show(OmsiGeoSettings settings, bool worldCoordinates, string chosenSource)
        {
            worldLabel.style.display = worldCoordinates ? DisplayStyle.Flex : DisplayStyle.None;
            placeSection.style.display = worldCoordinates ? DisplayStyle.None : DisplayStyle.Flex;
            rotationField.SetValueWithoutNotify((settings.Anchor?.RotationDegrees ?? 0.0).ToString("0.##", CultureInfo.InvariantCulture));
            relativeToggle.SetValueWithoutNotify(settings.Heights.Relative);
            levelWaterToggle.SetValueWithoutNotify(settings.Heights.LevelWater);
            offsetField.SetValueWithoutNotify(settings.Heights.Offset.ToString("0.##", CultureInfo.InvariantCulture));
            smoothingField.SetValueWithoutNotify(settings.Heights.Smoothing.ToString("0.##", CultureInfo.InvariantCulture));
            baseLabel.text = settings.Heights.Relative && settings.Heights.Base.HasValue
                ? Loc.T("satellite.base.taken", ("base", settings.Heights.Base.Value))
                : settings.Heights.Relative ? Loc.T("satellite.base.measured") : string.Empty;
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
        /// <summary>The language changed: what the window wrote from the language files is written again.</summary>
        public void RefreshTexts()
        {
            searchField.textEdition.placeholder = Loc.T("satellite.search.placeholder");
            var chosenName = sourceField.index <= 0 ? null : sourceField.value;
            var sizeIndex = Math.Max(0, pictureSizeField.index);
            SetSources(shownSources);
            sourceField.SetValueWithoutNotify(chosenName != null && sourceField.choices.Contains(chosenName) ? chosenName : SharpestChoice);
            pictureSizeField.choices = new List<string>(PictureSizeNames);
            pictureSizeField.SetValueWithoutNotify(PictureSizeNames[Math.Min(sizeIndex, PictureSizeNames.Length - 1)]);
            if (placing)
                SetPlacing(true);
            UpdateButtons();
        }

        public void SetSources(IEnumerable<OmsiImagerySource> sources)
        {
            var choices = new List<string> { SharpestChoice };
            var list = new List<OmsiImagerySource>(sources);
            shownSources = list;
            foreach (var source in list)
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
                    var row = RowText(keyRows, name);
                    var field = new TextField { isPasswordField = true, value = key(name) ?? string.Empty };
                    field.textEdition.placeholder = Loc.T("satellite.key.placeholder");
                    Field(row, field, "satellite.key.tooltip", new (string Name, object Value)[] { ("name", name) });
                    field.AddToClassList("satellite-wide-field");
                    field.RegisterCallback<FocusOutEvent>(_ => KeyChanged?.Invoke(name, field.value));
                    if (!string.IsNullOrEmpty(source.Terms))
                    {
                        var link = source.Terms;
                        row.Add(Button("satellite.key.get", () => Application.OpenURL(link), "satellite.key.get.tooltip", null, ("link", link)));
                    }
                }
                if (source.IsOwn)
                {
                    var name = source.Name;
                    var row = new VisualElement();
                    row.AddToClassList("satellite-row");
                    var label = new Label(Loc.T("satellite.own.source", ("name", name), ("wms", source.Kind == OmsiImageryKind.Wms ? "yes" : "no"), ("zoom", source.MaxZoom)));
                    label.AddToClassList("satellite-note");
                    label.AddToClassList("satellite-grow");
                    row.Add(label);
                    var remove = Button("satellite.own.remove", () => OwnSourceRemoved?.Invoke(name), "satellite.own.remove.tooltip");
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
            edgesLabel.text = Loc.T("satellite.edges.stepped", ("tiles", tiles));
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
                    text = Loc.T("satellite.picture.item", ("file", picture.File), ("count", count), ("west", picture.West), ("south", picture.South))
                };
                item.AddToClassList("satellite-result");
                item.EnableInClassList("satellite-result-chosen", chosen != null && chosen.Id == picture.Id);
                pictureList.Add(item);
            }
            fitShown = chosen?.Clone();
            fitSection.style.display = chosen != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (chosen == null)
                return;
            LocBindings.TextUpper(fitTitle, "satellite.fit.title", ("file", chosen.File));
            rotationValue.SetValueWithoutNotify((float)chosen.Rotation);
            mirrorXToggle.SetValueWithoutNotify(chosen.MirrorX);
            mirrorYToggle.SetValueWithoutNotify(chosen.MirrorY);
            offsetXValue.SetValueWithoutNotify((float)chosen.OffsetX);
            offsetYValue.SetValueWithoutNotify((float)chosen.OffsetY);
            scaleValue.SetValueWithoutNotify((float)chosen.Scale);
            chosen.Frame(tileSize, out _, out _, out var width, out var height, out _);
            sizeLabel.text = Loc.T("satellite.size_info", ("width", width), ("height", height), ("hasPixel", image != null ? "yes" : "no"), ("pixel", image != null ? width / image.Width : 0.0));
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

        /// <summary>Where the map's reference point lies, or null while the map is not placed.</summary>
        public void ShowAnchor(OmsiGeoAnchor anchor, bool worldCoordinates)
        {
            placed = worldCoordinates || anchor != null;
            anchorLabel.text = anchor == null
                ? Loc.T("satellite.anchor.none")
                : Loc.T("satellite.anchor.set", ("x", anchor.TileX), ("y", anchor.TileY), ("latitude", anchor.Latitude.ToString("0.000000", CultureInfo.InvariantCulture)),
                    ("longitude", anchor.Longitude.ToString("0.000000", CultureInfo.InvariantCulture)));
            referenceLabel.text = anchor == null
                ? Loc.T("satellite.reference.none")
                : Loc.T("satellite.reference.set", ("x", anchor.TileX), ("y", anchor.TileY), ("first", Direction(anchor.OffsetX, "east", "west")), ("second", Direction(anchor.OffsetY, "north", "south")));
            positionField.SetValueWithoutNotify(anchor == null
                ? string.Empty
                : anchor.Latitude.ToString("0.000000", CultureInfo.InvariantCulture) + ", " + anchor.Longitude.ToString("0.000000", CultureInfo.InvariantCulture));
            scaleField.SetValueWithoutNotify((anchor?.Scale ?? 1.0).ToString("0.######", CultureInfo.InvariantCulture));
            UpdateButtons();
        }

        private static string Direction(double meters, string positive, string negative)
        {
            return Loc.T("satellite.direction", ("meters", Math.Abs(meters)), ("direction", meters >= 0.0 ? positive : negative));
        }

        /// <summary>Which pick is waiting for a click: 0 reference point, 1 and 2 the fit's points, -1 none.</summary>
        public void ShowPicking(int which)
        {
            for (var index = 0; index < pickButtons.Length; index++)
            {
                pickButtons[index].EnableInClassList("dialog-primary", index == which);
                LocBindings.Text(pickButtons[index].Q<Label>(), index == which ? "satellite.pick.clicking" : PickKeys[index]); // i18n:keys
            }
        }

        /// <summary>How much room the compressed copies take on this computer, against their limit.</summary>
        public void ShowSharpUsage(long bytes, long cap)
        {
            sharpLabel.text = Loc.T("satellite.usage", ("used", Megabytes(bytes)), ("cap", Megabytes(cap)));
        }

        private static string Megabytes(long bytes)
        {
            return bytes >= 1024L * 1024 * 1024
                ? (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
                : Math.Round(bytes / (1024.0 * 1024)).ToString("0", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>Shows where one of the fit's two map points lies, or that it is not picked yet.</summary>
        public void ShowFitPoint(int index, string text)
        {
            fitLabels[index].text = text ?? Loc.T("satellite.fit.not_picked");
        }

        /// <summary>The text of the position typed for one of the fit's two points.</summary>
        public string FitPositionText(int index)
        {
            return fitFields[index].value;
        }

        public void SetFitScale(bool on)
        {
            fitScaleToggle.SetValueWithoutNotify(on);
        }

        public void SetFitPositionText(int index, string text)
        {
            fitFields[index].SetValueWithoutNotify(text ?? string.Empty);
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

        /// <summary>Shows one tab: 0 Place, 1 Pictures, 2 Terrain, 3 More.</summary>
        public void ShowTab(int index)
        {
            index = Math.Max(0, Math.Min(tabs.Length - 1, index));
            for (var tab = 0; tab < tabs.Length; tab++)
            {
                tabs[tab].style.display = tab == index ? DisplayStyle.Flex : DisplayStyle.None;
                tabButtons[tab].EnableInClassList("panel-tab-active", tab == index);
            }
        }

        /// <summary>Opens the folded parts of the window, all or one by its name (exact, fit, keys, server); for checks by screenshot.</summary>
        public void Unfold(string which)
        {
            var all = string.IsNullOrEmpty(which) || string.Equals(which, "all", StringComparison.OrdinalIgnoreCase);
            this.Query<Foldout>().ForEach(foldout =>
            {
                if (all || string.Equals(foldout.name, which, StringComparison.OrdinalIgnoreCase))
                    foldout.value = true;
            });
        }

        /// <summary>The tab by its name (case does not matter); false when there is none.</summary>
        public bool ShowTab(string name)
        {
            var index = Array.FindIndex(TabNames, tab => string.Equals(tab, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;
            ShowTab(index);
            return true;
        }

        private void UpdateButtons()
        {
            loadAllButton.SetEnabled(placed && !busy);
            loadTileButton.SetEnabled(placed && !busy && tileChosen);
            heightsOnlyButton.SetEnabled(placed && !busy);
            cancelButton.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            LocBindings.Tooltip(loadTileButton, tileChosen ? "satellite.load_tile.tooltip" : "satellite.load_tile.tooltip_none"); // i18n:keys
        }

        private void SetPlacing(bool on)
        {
            placing = on;
            placeButton.EnableInClassList("dialog-primary", on);
            LocBindings.Text(placeButton.Q<Label>(), on ? "satellite.placing" : "satellite.place_here"); // i18n:keys
        }

        private void Search()
        {
            var text = searchField.value?.Trim();
            if (!string.IsNullOrEmpty(text))
                SearchRequested?.Invoke(text);
        }

        /// <summary>Exact placement for a map without world coordinates: reference point, position, scale, moving, fit by two points.</summary>
        private void BuildExactPlace()
        {
            var exact = new Foldout { value = false };
            LocBindings.Text(exact, "satellite.exact.fold");
            exact.name = "exact";
            exact.AddToClassList("satellite-foldout");
            placeSection.Add(exact);
            var note = LocBindings.NewLabel("satellite.exact.note");
            note.AddToClassList("satellite-note");
            exact.Add(note);

            referenceLabel.AddToClassList("satellite-note");
            exact.Add(referenceLabel);
            var referenceRow = new VisualElement();
            referenceRow.AddToClassList("satellite-row");
            pickButtons[0] = Button(PickKeys[0], () => PickRequested?.Invoke(0), "satellite.pick.tooltip", "crosshair");
            referenceRow.Add(pickButtons[0]);
            referenceRow.Add(Button("satellite.middle_of_tile", () => ReferenceCentred?.Invoke(), "satellite.middle_of_tile.tooltip"));
            exact.Add(referenceRow);

            var positionRow = Row(exact, "satellite.lies_at");
            Field(positionRow, positionField, "satellite.lies_at.tooltip");
            positionField.textEdition.placeholder = "52.5200, 13.4050";
            positionField.style.width = 190;
            OnCommit(positionField, () => PositionEntered?.Invoke(positionField.value));

            var scaleRow = Row(exact, "satellite.scale");
            Field(scaleRow, scaleField, "satellite.scale.tooltip");
            OnCommit(scaleField, () =>
            {
                var value = Number(scaleField.value);
                if (value.HasValue && value.Value > 0.01 && value.Value < 100.0)
                    ScaleChanged?.Invoke(value.Value);
            });

            var moveRow = Row(exact, "satellite.move");
            Field(moveRow, stepField, "satellite.move.tooltip");
            stepField.style.width = 44;
            foreach (var move in new[] { ("satellite.move.west", -1, 0), ("satellite.move.east", 1, 0), ("satellite.move.north", 0, 1), ("satellite.move.south", 0, -1) })
            {
                var east = move.Item2;
                var north = move.Item3;
                moveRow.Add(Button(move.Item1, () =>
                {
                    var step = Number(stepField.value);
                    if (step.HasValue && step.Value > 0.0)
                        MoveRequested?.Invoke(east * step.Value, north * step.Value);
                }, move.Item1 + ".tooltip")); // i18n:keys
            }

            var fitFold = new Foldout { value = false };
            LocBindings.Text(fitFold, "satellite.fit.fold");
            fitFold.name = "fit";
            fitFold.AddToClassList("satellite-foldout");
            placeSection.Add(fitFold);
            var fitNote = LocBindings.NewLabel("satellite.fit.note");
            fitNote.AddToClassList("satellite-note");
            fitFold.Add(fitNote);
            for (var index = 0; index < 2; index++)
            {
                var number = index + 1;
                var row = new VisualElement();
                row.AddToClassList("satellite-row");
                pickButtons[number] = Button(PickKeys[number], () => PickRequested?.Invoke(number), "satellite.fit.pick.tooltip", "crosshair");
                row.Add(pickButtons[number]);
                fitFold.Add(row);
                fitLabels[index].AddToClassList("satellite-note");
                fitLabels[index].text = Loc.T("satellite.fit.not_picked");
                fitFold.Add(fitLabels[index]);
                var field = fitFields[index];
                var fieldRow = Row(fitFold, "satellite.lies_at");
                Field(fieldRow, field, "satellite.fit.lies_at.tooltip");
                field.textEdition.placeholder = "52.5200, 13.4050";
                field.style.width = 190;
            }
            var fitRow = new VisualElement();
            fitRow.AddToClassList("satellite-row");
            // Like the other ticks of the window, not the bare toggle (which draws a white box).
            fitScaleToggle.AddToClassList("picker-option-toggle");
            fitScaleToggle.AddToClassList("panel-toggle");
            LocBindings.Text(fitScaleToggle, "satellite.fit.scale");
            LocBindings.Tooltip(fitScaleToggle, "satellite.fit.scale.tooltip");
            fitRow.Add(fitScaleToggle);
            fitRow.Add(Button("satellite.fit.button", () => FitRequested?.Invoke(fitFields[0].value, fitFields[1].value, fitScaleToggle.value), "satellite.fit.button.tooltip"));
            fitFold.Add(fitRow);
        }

        private static void OnCommit(TextField field, Action action)
        {
            field.RegisterCallback<FocusOutEvent>(_ => action());
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Return || evt.keyCode == UnityEngine.KeyCode.KeypadEnter)
                    action();
            }, TrickleDown.TrickleDown);
        }

        private void ApplyRotation()
        {
            var value = Number(rotationField.value);
            if (value.HasValue)
                RotationChanged?.Invoke(value.Value);
        }

        private void ShowOpacity(float value)
        {
            opacityLabel.text = Loc.T("satellite.percent", ("percent", (int)Math.Round(value * 100f)));
        }

        private static double? Number(string text)
        {
            return double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (double?)null;
        }

        private static Button Button(string textKey, Action action, string tooltipKey, string icon = null, params (string Name, object Value)[] tooltipArguments)
        {
            var button = new Button(action);
            LocBindings.Tooltip(button, tooltipKey, tooltipArguments); // i18n:keys
            button.AddToClassList("map-control-button");
            button.AddToClassList("satellite-button");
            if (icon == null)
                LocBindings.Text(button, textKey); // i18n:keys
            else
            {
                button.Add(new IconElement(icon));
                var label = LocBindings.NewLabel(textKey); // i18n:keys
                label.pickingMode = PickingMode.Ignore;
                button.Add(label);
            }
            return button;
        }

        private static void Section(VisualElement parent, string titleKey)
        {
            var label = new Label();
            LocBindings.TextUpper(label, titleKey); // i18n:keys
            label.AddToClassList("inspector-section-heading");
            parent.Add(label);
        }

        private static VisualElement Row(VisualElement parent, string labelKey)
        {
            return Row(parent, new Label(), labelKey);
        }

        /// <summary>A row whose label is a text of the data, such as the name of a source, not of the language files.</summary>
        private static VisualElement RowText(VisualElement parent, string label)
        {
            return Row(parent, new Label(label), null);
        }

        private static VisualElement Row(VisualElement parent, Label text, string labelKey)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            if (labelKey != null)
                LocBindings.Text(text, labelKey); // i18n:keys
            text.AddToClassList("picker-option-label");
            text.AddToClassList("satellite-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }

        private static readonly (string Name, object Value)[] UrlPlaceholders =
        {
            ("zxy", "{z} {x} {y}"), ("bbox", "{bbox}"), ("widthHeight", "{width} {height}"), ("key", "{key}")
        };

        private static void Field(VisualElement row, TextField field, string tooltipKey, params (string Name, object Value)[] arguments)
        {
            field.AddToClassList("picker-field");
            LocBindings.Tooltip(field, tooltipKey, arguments); // i18n:keys
            LocBindings.Tooltip(row, tooltipKey, arguments); // i18n:keys
            row.Add(field);
        }
    }
}
