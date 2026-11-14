using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The options of the terrain tool (ADR 040), as openMapper's Terrain tool has them: the brush (raise, lower, smooth,
    /// flatten, roughen) or a ramp, the brush's radius and strength, and the height Flatten works towards. The shell
    /// shapes the ground; the options only hold the settings. Laid out like the road tool and the references of ADR 098:
    /// the settings in a column at the left, the brushes as tiles in a panel beside it, the height map on its second tab.
    /// </summary>
    internal sealed class TerrainToolOptions : VisualElement
    {
        private readonly Dictionary<Button, OmsiTerrainBrushMode?> modeButtons = new Dictionary<Button, OmsiTerrainBrushMode?>();
        private readonly Slider radiusSlider;
        private readonly Label radiusLabel;
        private readonly Slider strengthSlider;
        private readonly Label strengthLabel;
        private readonly TextField flattenField;
        private readonly TextField depthField;
        private readonly VisualElement flattenBox;
        private readonly VisualElement depthBox;
        private readonly VisualElement options;
        private readonly ToolCatalog catalog;
        private readonly VisualElement brushPage;
        private readonly VisualElement heightMapPage;
        private readonly Button brushTab;
        private readonly Button paintTab;
        private readonly Button heightMapTab;
        private readonly VisualElement heightsRow;
        private readonly Label terrainHint;
        private OmsiTerrainBrushMode? lastBrush = OmsiTerrainBrushMode.Raise;

        private enum Page
        {
            Brushes,
            Paint,
            HeightMap
        }

        // The height map: export and import, and the settings of a picture about to be laid on the map.
        private readonly VisualElement importBox;
        private readonly Label importName;
        private readonly Label importInfo;
        private readonly Label lowestLabel;
        private readonly TextField lowestField;
        private readonly Label highestLabel;
        private readonly TextField highestField;
        private readonly SliderInt smoothingSlider;
        private readonly Label smoothingLabel;
        private readonly Button replaceButton;
        private readonly Button addButton;
        private readonly Label coarseLabel;

        public TerrainToolOptions()
        {
            AddToClassList("picker-tool");
            // The row is as tall as its taller panel; the empty part above the shorter one must not catch clicks meant for the map.
            pickingMode = PickingMode.Ignore;
            options = ToolCatalog.Options(scrolling: true, icons: true);
            Add(options);

            // The brushes beside the options, as tiles, and the height map on the second tab.
            catalog = new ToolCatalog("terrain.close.tooltip", () => CloseRequested?.Invoke(), scrollingTiles: false);
            catalog.Panel.AddToClassList("terrain-picker");
            catalog.Empty.style.display = DisplayStyle.None;
            // Three tabs: the brushes that shape the ground, painting ground textures (its textures are tiles of their own), the height map.
            brushTab = catalog.AddTab("mountain", "terrain.brushes", null, () =>
            {
                if (Painting)
                    SetMode(lastBrush);
                else
                    ShowPage(Page.Brushes);
            });
            paintTab = catalog.AddTab("paintbrush", "terrain.mode.paint", "terrain.mode.paint.tooltip", () =>
            {
                if (!Painting)
                    SetPainting();
                else
                    ShowPage(Page.Paint);
            });
            heightMapTab = catalog.AddTab("image", "terrain.heightmap", "terrain.heightmap.hint", () => ShowPage(Page.HeightMap));
            brushPage = new VisualElement();
            brushPage.AddToClassList("picker-tiles-content");
            brushPage.AddToClassList("terrain-page");
            catalog.Panel.Insert(1, brushPage);
            Paint = new GroundPaintOptions();
            Paint.style.display = DisplayStyle.None;
            Paint.TextureChosen += () => SettingsChanged?.Invoke();
            catalog.Panel.Insert(2, Paint.Page);
            heightMapPage = new VisualElement();
            heightMapPage.AddToClassList("terrain-page");
            catalog.Panel.Insert(3, heightMapPage);
            Add(catalog.Panel);

            AddMode("terrain.mode.raise", OmsiTerrainBrushMode.Raise, "arrow-up");
            AddMode("terrain.mode.lower", OmsiTerrainBrushMode.Lower, "arrow-down");
            AddMode("terrain.mode.smooth", OmsiTerrainBrushMode.Smooth, "waves");
            AddMode("terrain.mode.flatten", OmsiTerrainBrushMode.Flatten, "equal");
            AddMode("terrain.mode.roughen", OmsiTerrainBrushMode.Roughen, "activity");
            AddMode("terrain.mode.ramp", null, "trending-up");
            AddMode("terrain.mode.water", OmsiTerrainBrushMode.Water, "droplets");

            // The options as rows of icons and small fields, as the road tool has them (ADR 098, M4): the brush's radius and strength as
            // sliders, and the two heights some brushes work with as fields in metres.
            radiusSlider = new Slider(0f, 1f);
            radiusLabel = new Label();
            AddSlider(OptionControls.Row(options, "terrain.radius"), "radius", radiusSlider, radiusLabel, "terrain.radius", "terrain.radius.tooltip");
            strengthSlider = new Slider(0.05f, 1f);
            strengthLabel = new Label();
            AddSlider(OptionControls.Row(options, "terrain.strength"), "gauge", strengthSlider, strengthLabel, "terrain.strength", "terrain.strength.tooltip");
            heightsRow = OptionControls.Row(options, "terrain.row.heights");
            flattenField = OptionControls.Value(heightsRow, "move-vertical", string.Empty, "m", "terrain.flatten", "terrain.flatten.tooltip");
            flattenField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            flattenBox = flattenField.parent;
            depthField = OptionControls.Value(heightsRow, "arrow-down-to-line", "1.5", "m", "terrain.depth", "terrain.depth.tooltip");
            depthBox = depthField.parent;

            options.Add(Paint);

            terrainHint = LocBindings.NewLabel("terrain.hint");
            terrainHint.AddToClassList("panel-info");
            options.Add(terrainHint);

            var heightMapHint = LocBindings.NewLabel("terrain.heightmap.hint");
            heightMapHint.AddToClassList("panel-info");
            heightMapPage.Add(heightMapHint);
            var buttons = new VisualElement();
            buttons.AddToClassList("panel-tabs");
            buttons.AddToClassList("picker-segmented");
            var exportButton = LocBindings.WithTooltip(LocBindings.NewButton("terrain.export", () => ExportRequested?.Invoke()), "terrain.export.tooltip");
            var importButton = LocBindings.WithTooltip(LocBindings.NewButton("terrain.import", () => ImportRequested?.Invoke()), "terrain.import.tooltip");
            foreach (var button in new[] { exportButton, importButton })
            {
                button.AddToClassList("panel-tab");
                buttons.Add(button);
            }
            heightMapPage.Add(buttons);

            importBox = new VisualElement();
            importBox.style.display = DisplayStyle.None;
            heightMapPage.Add(importBox);
            importName = new Label();
            importName.AddToClassList("picker-options-title");
            importBox.Add(importName);
            importInfo = new Label();
            importInfo.AddToClassList("panel-info");
            importBox.Add(importInfo);
            var lowestRow = Row(importBox, string.Empty, out lowestLabel);
            lowestField = new TextField();
            LocBindings.Tooltip(lowestField, "terrain.black.tooltip");
            lowestField.AddToClassList("picker-field");
            lowestRow.Add(lowestField);
            var highestRow = Row(importBox, string.Empty, out highestLabel);
            highestField = new TextField();
            LocBindings.Tooltip(highestField, "terrain.white.tooltip");
            highestField.AddToClassList("picker-field");
            highestRow.Add(highestField);
            smoothingSlider = new SliderInt(0, 6);
            LocBindings.Tooltip(smoothingSlider, "terrain.smoothing.tooltip");
            smoothingLabel = new Label();
            smoothingSlider.AddToClassList("new-map-slider");
            smoothingSlider.RegisterValueChangedCallback(_ => smoothingLabel.text = smoothingSlider.value.ToString(CultureInfo.InvariantCulture));
            var smoothingRow = Row(importBox, "terrain.smoothing", out _);
            smoothingRow.Add(smoothingSlider);
            smoothingLabel.AddToClassList("new-map-factor");
            smoothingRow.Add(smoothingLabel);
            var how = new VisualElement();
            how.AddToClassList("panel-tabs");
            how.AddToClassList("picker-segmented");
            replaceButton = LocBindings.WithTooltip(LocBindings.NewButton("terrain.replace", () => SetAdd(false)), "terrain.replace.tooltip");
            addButton = LocBindings.WithTooltip(LocBindings.NewButton("terrain.add", () => SetAdd(true)), "terrain.add.tooltip");
            foreach (var button in new[] { replaceButton, addButton })
            {
                button.AddToClassList("panel-tab");
                how.Add(button);
            }
            importBox.Add(how);
            var keep = LocBindings.NewLabel("terrain.keep");
            keep.AddToClassList("panel-info");
            importBox.Add(keep);
            coarseLabel = LocBindings.NewLabel("terrain.coarse");
            coarseLabel.AddToClassList("panel-info");
            importBox.Add(coarseLabel);
            var actions = new VisualElement();
            actions.AddToClassList("panel-tabs");
            actions.AddToClassList("picker-segmented");
            var lay = LocBindings.NewButton("terrain.lay", () => ImportConfirmed?.Invoke());
            var cancel = LocBindings.NewButton("terrain.cancel", HideImport);
            foreach (var button in new[] { lay, cancel })
            {
                button.AddToClassList("panel-tab");
                actions.Add(button);
            }
            importBox.Add(actions);

            radiusSlider.SetValueWithoutNotify(RadiusToSlider(25.0));
            strengthSlider.SetValueWithoutNotify(0.5f);
            ShowValues();
            SetMode(OmsiTerrainBrushMode.Raise);
            ShowPage(Page.Brushes);
        }

        /// <summary>The height under the pointer or the ramp; the shell shows it in the status bar.</summary>
        public event Action<string, bool> HintChanged
        {
            add => catalog.HintChanged += value;
            remove => catalog.HintChanged -= value;
        }

        /// <summary>The X of the panel was pressed; the shell ends the tool.</summary>
        public event Action CloseRequested;

        /// <summary>Shows the brushes, the ground textures or the height map in the panel beside the options.</summary>
        private void ShowPage(Page shown)
        {
            // Covering or clearing a tile by click belongs to the Paint tab.
            if (shown != Page.Paint)
                Paint.Arm(PaintTileArm.None);
            brushPage.style.display = shown == Page.Brushes ? DisplayStyle.Flex : DisplayStyle.None;
            Paint.Page.style.display = shown == Page.Paint ? DisplayStyle.Flex : DisplayStyle.None;
            heightMapPage.style.display = shown == Page.HeightMap ? DisplayStyle.Flex : DisplayStyle.None;
            brushTab.EnableInClassList("panel-tab-active", shown == Page.Brushes);
            paintTab.EnableInClassList("panel-tab-active", shown == Page.Paint);
            heightMapTab.EnableInClassList("panel-tab-active", shown == Page.HeightMap);
        }

        /// <summary>Opens a tab by its name (terrain, paint or heightmap) as a click on it would; for command-line checks.</summary>
        public void ShowTab(string name)
        {
            if (string.Equals(name, "paint", StringComparison.OrdinalIgnoreCase))
            {
                if (!Painting)
                    SetPainting();
                else
                    ShowPage(Page.Paint);
            }
            else if (string.Equals(name, "heightmap", StringComparison.OrdinalIgnoreCase))
                ShowPage(Page.HeightMap);
            else if (Painting)
                SetMode(lastBrush);
            else
                ShowPage(Page.Brushes);
        }

        /// <summary>A setting changed; the shell draws the brush again.</summary>
        public event Action SettingsChanged;

        /// <summary>Export... was pressed; the shell asks where and writes the height map.</summary>
        public event Action ExportRequested;

        /// <summary>Import... was pressed; the shell asks for a picture and shows it with <see cref="ShowImport"/>.</summary>
        public event Action ImportRequested;

        /// <summary>"Lay it on the map" was pressed with the settings shown.</summary>
        public event Action ImportConfirmed;

        /// <summary>Whether the picture is added to the ground rather than taking its place.</summary>
        public bool ImportAdds { get; private set; }

        public int ImportSmoothing => smoothingSlider.value;

        /// <summary>The heights black and white stand for, or null while one of them is no number.</summary>
        public OmsiHeightRange? ImportRange
        {
            get
            {
                if (double.TryParse(lowestField.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lowest) &&
                    double.TryParse(highestField.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var highest))
                    return new OmsiHeightRange { Lowest = lowest, Highest = highest };
                return null;
            }
        }

        /// <summary>Shows a picture about to be laid on the map: its name, how it fits, and the settings to choose.</summary>
        public void ShowImport(string fileName, string info, OmsiHeightRange range, bool coarse)
        {
            importName.text = fileName;
            importInfo.text = info;
            lowestField.SetValueWithoutNotify(range.Lowest.ToString("0.##", CultureInfo.InvariantCulture));
            highestField.SetValueWithoutNotify(range.Highest.ToString("0.##", CultureInfo.InvariantCulture));
            smoothingSlider.value = coarse ? 2 : 0;
            smoothingLabel.text = smoothingSlider.value.ToString(CultureInfo.InvariantCulture);
            coarseLabel.style.display = coarse ? DisplayStyle.Flex : DisplayStyle.None;
            SetAdd(false);
            importBox.style.display = DisplayStyle.Flex;
            ShowPage(Page.HeightMap);
        }

        /// <summary>The language changed: what the panel wrote from the language files is written again.</summary>
        public void RefreshTexts()
        {
            SetAdd(ImportAdds);
        }

        public void HideImport()
        {
            importBox.style.display = DisplayStyle.None;
        }

        /// <summary>Sets the import's way and smoothing as a user would; for command-line checks.</summary>
        public void ApplyImport(bool add, int? smoothing)
        {
            SetAdd(add);
            if (smoothing.HasValue)
                smoothingSlider.value = Math.Max(0, Math.Min(6, smoothing.Value));
        }

        private void SetAdd(bool add)
        {
            ImportAdds = add;
            replaceButton.EnableInClassList("panel-tab-active", !add);
            addButton.EnableInClassList("panel-tab-active", add);
            lowestLabel.text = Loc.T(add ? "terrain.black_adds" : "terrain.black_is"); // i18n:keys
            highestLabel.text = Loc.T(add ? "terrain.white_adds" : "terrain.white_is"); // i18n:keys
        }

        /// <summary>The brush chosen, or null for the ramp.</summary>
        public OmsiTerrainBrushMode? Mode { get; private set; }

        /// <summary>The brush's radius in metres, 3 to 150 on a logarithmic slider as in openMapper.</summary>
        public double Radius => Math.Round(3.0 * Math.Pow(50.0, radiusSlider.value), 1);

        public double Strength => Math.Round(strengthSlider.value, 2);

        /// <summary>The height typed for Flatten, or null for the height where each stroke starts.</summary>
        public double? FlattenHeight =>
            double.TryParse(flattenField.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsInfinity(value)
                ? value
                : (double?)null;

        /// <summary>How deep Lake / river digs under the water's surface; 1.5 m unless a positive depth is typed.</summary>
        public double WaterDepth =>
            double.TryParse(depthField.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0.0 && !double.IsInfinity(value)
                ? value
                : 1.5;

        /// <summary>The ground painting options (ADR 091), shown while <see cref="Painting"/>.</summary>
        public GroundPaintOptions Paint { get; }

        /// <summary>True while the brush paints ground textures instead of shaping the ground.</summary>
        public bool Painting { get; private set; }

        /// <summary>Switches the brush to painting ground textures.</summary>
        public void SetPainting()
        {
            Painting = true;
            Mode = null;
            foreach (var pair in modeButtons)
                pair.Key.EnableInClassList("picker-tile-selected", false);
            Paint.style.display = DisplayStyle.Flex;
            heightsRow.style.display = DisplayStyle.None;
            terrainHint.style.display = DisplayStyle.None;
            ShowPage(Page.Paint);
            flattenBox.SetEnabled(false);
            depthBox?.SetEnabled(false);
            PaintingChanged?.Invoke();
            SettingsChanged?.Invoke();
        }

        /// <summary>The brush changed between shaping and painting; the shell reads the map's textures when painting starts.</summary>
        public event Action PaintingChanged;

        public void SetMode(OmsiTerrainBrushMode? mode)
        {
            var wasPainting = Painting;
            Painting = false;
            Mode = mode;
            lastBrush = mode;
            Paint.style.display = DisplayStyle.None;
            heightsRow.style.display = DisplayStyle.Flex;
            terrainHint.style.display = DisplayStyle.Flex;
            ShowPage(Page.Brushes);
            if (wasPainting)
                PaintingChanged?.Invoke();
            foreach (var pair in modeButtons)
                pair.Key.EnableInClassList("picker-tile-selected", pair.Value == mode);
            flattenBox.SetEnabled(mode == OmsiTerrainBrushMode.Flatten);
            depthBox?.SetEnabled(mode == OmsiTerrainBrushMode.Water);
            SettingsChanged?.Invoke();
        }

        /// <summary>Sets mode, radius and strength as a user would; for command-line checks.</summary>
        public void Apply(string mode, string radius, string strength)
        {
            if (string.Equals(mode, "paint", StringComparison.OrdinalIgnoreCase))
                SetPainting();
            else if (mode != null)
                SetMode(string.Equals(mode, "ramp", StringComparison.OrdinalIgnoreCase) ? (OmsiTerrainBrushMode?)null
                    : Enum.TryParse<OmsiTerrainBrushMode>(mode, true, out var parsed) ? parsed : Mode);
            if (double.TryParse(radius, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres))
                radiusSlider.value = RadiusToSlider(metres);
            if (double.TryParse(strength, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
                strengthSlider.value = (float)Math.Max(0.05, Math.Min(1.0, factor));
        }

        public void ShowPlan(string text, bool problem)
        {
            catalog.ShowHint(text, problem);
        }

        private void AddMode(string nameKey, OmsiTerrainBrushMode? mode, string icon)
        {
            modeButtons[catalog.AddTile(icon, nameKey, Describe(mode), () => SetMode(mode), brushPage)] = mode;
        }

        /// <summary>The key of the tooltip of a brush.</summary>
        private static string Describe(OmsiTerrainBrushMode? mode)
        {
            switch (mode)
            {
                case OmsiTerrainBrushMode.Raise:
                    return "terrain.mode.raise.tooltip";
                case OmsiTerrainBrushMode.Lower:
                    return "terrain.mode.lower.tooltip";
                case OmsiTerrainBrushMode.Smooth:
                    return "terrain.mode.smooth.tooltip";
                case OmsiTerrainBrushMode.Flatten:
                    return "terrain.mode.flatten.tooltip";
                case OmsiTerrainBrushMode.Roughen:
                    return "terrain.mode.roughen.tooltip";
                case OmsiTerrainBrushMode.Water:
                    return "terrain.mode.water.tooltip";
                default:
                    return "terrain.mode.ramp.tooltip";
            }
        }

        private void AddSlider(VisualElement row, string icon, Slider slider, Label value, string nameKey, string tooltipKey)
        {
            slider.RegisterValueChangedCallback(_ =>
            {
                ShowValues();
                SettingsChanged?.Invoke();
            });
            OptionControls.SliderBox(row, icon, slider, value, nameKey, tooltipKey);
        }

        private void ShowValues()
        {
            radiusLabel.text = Radius.ToString("0.#", CultureInfo.InvariantCulture) + " m";
            strengthLabel.text = Strength.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static float RadiusToSlider(double metres)
        {
            return (float)(Math.Log(Math.Max(3.0, Math.Min(150.0, metres)) / 3.0) / Math.Log(50.0));
        }

        private static VisualElement Row(VisualElement parent, string labelKey, out Label text)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            text = labelKey.Length == 0 ? new Label() : LocBindings.NewLabel(labelKey); // i18n:keys
            text.AddToClassList("picker-option-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }
    }
}
