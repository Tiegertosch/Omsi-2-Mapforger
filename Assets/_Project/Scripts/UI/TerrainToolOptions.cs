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
    /// shapes the ground; the options only hold the settings.
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
        private readonly Label planLabel;

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
            AddToClassList("tool-panel");
            AddToClassList("picker-options");
            var title = LocBindings.NewLabel("terrain.title");
            title.AddToClassList("picker-options-title");
            Add(title);

            AddModes(Row("terrain.brush"), new[]
            {
                new KeyValuePair<string, OmsiTerrainBrushMode?>("terrain.mode.raise", OmsiTerrainBrushMode.Raise),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("terrain.mode.lower", OmsiTerrainBrushMode.Lower),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("terrain.mode.smooth", OmsiTerrainBrushMode.Smooth)
            });
            AddModes(Row(string.Empty), new[]
            {
                new KeyValuePair<string, OmsiTerrainBrushMode?>("terrain.mode.flatten", OmsiTerrainBrushMode.Flatten),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("terrain.mode.roughen", OmsiTerrainBrushMode.Roughen),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("terrain.mode.ramp", null)
            });
            AddModes(Row(string.Empty), new[]
            {
                new KeyValuePair<string, OmsiTerrainBrushMode?>("terrain.mode.water", OmsiTerrainBrushMode.Water)
            });

            radiusSlider = new Slider(0f, 1f);
            LocBindings.Tooltip(radiusSlider, "terrain.radius.tooltip");
            radiusLabel = new Label();
            AddSlider(Row("terrain.radius"), radiusSlider, radiusLabel, "terrain.radius.tooltip");
            strengthSlider = new Slider(0.05f, 1f);
            LocBindings.Tooltip(strengthSlider, "terrain.strength.tooltip");
            strengthLabel = new Label();
            AddSlider(Row("terrain.strength"), strengthSlider, strengthLabel, "terrain.strength.tooltip");
            flattenField = new TextField();
            LocBindings.Tooltip(flattenField, "terrain.flatten.tooltip");
            flattenField.AddToClassList("picker-field");
            flattenField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row("terrain.flatten").Add(flattenField);
            depthField = new TextField { value = "1.5" };
            LocBindings.Tooltip(depthField, "terrain.depth.tooltip");
            depthField.AddToClassList("picker-field");
            Row("terrain.depth").Add(depthField);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = LocBindings.NewLabel("terrain.hint");
            hint.AddToClassList("panel-info");
            Add(hint);

            var heightMapTitle = LocBindings.NewLabel("terrain.heightmap");
            heightMapTitle.AddToClassList("picker-options-title");
            Add(heightMapTitle);
            var heightMapHint = LocBindings.NewLabel("terrain.heightmap.hint");
            heightMapHint.AddToClassList("panel-info");
            Add(heightMapHint);
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
            Add(buttons);

            importBox = new VisualElement();
            importBox.style.display = DisplayStyle.None;
            Add(importBox);
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

        public void SetMode(OmsiTerrainBrushMode? mode)
        {
            Mode = mode;
            foreach (var pair in modeButtons)
                pair.Key.EnableInClassList("panel-tab-active", pair.Value == mode);
            flattenField.SetEnabled(mode == OmsiTerrainBrushMode.Flatten);
            depthField?.SetEnabled(mode == OmsiTerrainBrushMode.Water);
            SettingsChanged?.Invoke();
        }

        /// <summary>Sets mode, radius and strength as a user would; for command-line checks.</summary>
        public void Apply(string mode, string radius, string strength)
        {
            if (mode != null)
                SetMode(string.Equals(mode, "ramp", StringComparison.OrdinalIgnoreCase) ? (OmsiTerrainBrushMode?)null
                    : Enum.TryParse<OmsiTerrainBrushMode>(mode, true, out var parsed) ? parsed : Mode);
            if (double.TryParse(radius, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres))
                radiusSlider.value = RadiusToSlider(metres);
            if (double.TryParse(strength, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
                strengthSlider.value = (float)Math.Max(0.05, Math.Min(1.0, factor));
        }

        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.tooltip = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
        }

        private void AddModes(VisualElement row, IEnumerable<KeyValuePair<string, OmsiTerrainBrushMode?>> modes)
        {
            var group = new VisualElement();
            group.AddToClassList("panel-tabs");
            group.AddToClassList("picker-segmented");
            foreach (var pair in modes)
            {
                var mode = pair.Value;
                var button = LocBindings.NewButton(pair.Key, () => SetMode(mode)); // i18n:keys
                LocBindings.Tooltip(button, Describe(mode)); // i18n:keys
                button.AddToClassList("panel-tab");
                group.Add(button);
                modeButtons[button] = mode;
            }
            row.Add(group);
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

        private void AddSlider(VisualElement row, Slider slider, Label value, string tooltipKey)
        {
            slider.AddToClassList("new-map-slider");
            LocBindings.Tooltip(row, tooltipKey); // i18n:keys
            slider.RegisterValueChangedCallback(_ =>
            {
                ShowValues();
                SettingsChanged?.Invoke();
            });
            row.Add(slider);
            value.AddToClassList("new-map-factor");
            row.Add(value);
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

        private VisualElement Row(string labelKey)
        {
            return Row(this, labelKey, out _);
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
