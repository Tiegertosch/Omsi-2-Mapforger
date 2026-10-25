using System;
using System.Collections.Generic;
using System.Globalization;
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
            var title = new Label("Terrain");
            title.AddToClassList("picker-options-title");
            Add(title);

            AddModes(Row("Brush"), new[]
            {
                new KeyValuePair<string, OmsiTerrainBrushMode?>("Raise", OmsiTerrainBrushMode.Raise),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("Lower", OmsiTerrainBrushMode.Lower),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("Smooth", OmsiTerrainBrushMode.Smooth)
            });
            AddModes(Row(string.Empty), new[]
            {
                new KeyValuePair<string, OmsiTerrainBrushMode?>("Flatten", OmsiTerrainBrushMode.Flatten),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("Roughen", OmsiTerrainBrushMode.Roughen),
                new KeyValuePair<string, OmsiTerrainBrushMode?>("Ramp", null)
            });
            AddModes(Row(string.Empty), new[]
            {
                new KeyValuePair<string, OmsiTerrainBrushMode?>("Lake / river", OmsiTerrainBrushMode.Water)
            });

            radiusSlider = new Slider(0f, 1f) { tooltip = "How far the brush reaches, 3 to 150 m; it works fully in the middle and softly towards its rim" };
            radiusLabel = new Label();
            AddSlider(Row("Radius"), radiusSlider, radiusLabel);
            strengthSlider = new Slider(0.05f, 1f) { tooltip = "How fast the brush works while the button is held" };
            strengthLabel = new Label();
            AddSlider(Row("Strength"), strengthSlider, strengthLabel);
            flattenField = new TextField { tooltip = "The height Flatten works towards; empty uses the height where each stroke starts" };
            flattenField.AddToClassList("picker-field");
            flattenField.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row("Flatten to (m)").Add(flattenField);
            depthField = new TextField { value = "1.5", tooltip = "How deep Lake / river digs under the water's surface" };
            depthField.AddToClassList("picker-field");
            Row("Water depth (m)").Add(depthField);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = new Label("Hold the left button over the ground to shape it; the ground never rises through a road beside it. " +
                                 "Ramp: click where it starts and where it ends. Roads fitted to the terrain stay fitted. Esc ends the tool.");
            hint.AddToClassList("panel-info");
            Add(hint);

            var heightMapTitle = new Label("Height map");
            heightMapTitle.AddToClassList("picker-options-title");
            Add(heightMapTitle);
            var heightMapHint = new Label("The ground as a grey picture, to shape in another program and bring back; openMapper reads and writes the same.");
            heightMapHint.AddToClassList("panel-info");
            Add(heightMapHint);
            var buttons = new VisualElement();
            buttons.AddToClassList("panel-tabs");
            buttons.AddToClassList("picker-segmented");
            var exportButton = new Button(() => ExportRequested?.Invoke())
            {
                text = "Export...", tooltip = "Writes the ground of all tiles as a 16-bit grey PNG, a pixel per terrain point, with its lowest and highest height in a .txt beside it"
            };
            var importButton = new Button(() => ImportRequested?.Invoke())
            {
                text = "Import...", tooltip = "Lays a grey picture (PNG, 8 or 16 bits) over all tiles, stretched to the map's outer edges"
            };
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
            lowestField = new TextField { tooltip = "The height black stands for" };
            lowestField.AddToClassList("picker-field");
            lowestRow.Add(lowestField);
            var highestRow = Row(importBox, string.Empty, out highestLabel);
            highestField = new TextField { tooltip = "The height white stands for" };
            highestField.AddToClassList("picker-field");
            highestRow.Add(highestField);
            smoothingSlider = new SliderInt(0, 6) { tooltip = "Passes of averaging each pixel with its neighbours, which hides the steps of an 8-bit picture" };
            smoothingLabel = new Label();
            smoothingSlider.AddToClassList("new-map-slider");
            smoothingSlider.RegisterValueChangedCallback(_ => smoothingLabel.text = smoothingSlider.value.ToString(CultureInfo.InvariantCulture));
            var smoothingRow = Row(importBox, "Smoothing", out _);
            smoothingRow.Add(smoothingSlider);
            smoothingLabel.AddToClassList("new-map-factor");
            smoothingRow.Add(smoothingLabel);
            var how = new VisualElement();
            how.AddToClassList("panel-tabs");
            how.AddToClassList("picker-segmented");
            replaceButton = new Button(() => SetAdd(false)) { text = "Replace the ground", tooltip = "The picture's heights take the ground's place" };
            addButton = new Button(() => SetAdd(true)) { text = "Add to it", tooltip = "The picture's heights are added to the ground as it is, as hills on a slope" };
            foreach (var button in new[] { replaceButton, addButton })
            {
                button.AddToClassList("panel-tab");
                how.Add(button);
            }
            importBox.Add(how);
            var keep = new Label("Roads and objects keep their heights: best done before building. Roads fitted to the terrain are fitted again.");
            keep.AddToClassList("panel-info");
            importBox.Add(keep);
            coarseLabel = new Label("An 8-bit picture has only 256 steps of height: smoothing hides them.");
            coarseLabel.AddToClassList("panel-info");
            importBox.Add(coarseLabel);
            var actions = new VisualElement();
            actions.AddToClassList("panel-tabs");
            actions.AddToClassList("picker-segmented");
            var lay = new Button(() => ImportConfirmed?.Invoke()) { text = "Lay it on the map" };
            var cancel = new Button(HideImport) { text = "Cancel" };
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
            lowestLabel.text = add ? "Black adds (m)" : "Black is (m)";
            highestLabel.text = add ? "White adds (m)" : "White is (m)";
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
                var button = new Button(() => SetMode(mode)) { text = pair.Key, tooltip = Describe(mode) };
                button.AddToClassList("panel-tab");
                group.Add(button);
                modeButtons[button] = mode;
            }
            row.Add(group);
        }

        private static string Describe(OmsiTerrainBrushMode? mode)
        {
            switch (mode)
            {
                case OmsiTerrainBrushMode.Raise:
                    return "Raises the ground under the brush while the button is held";
                case OmsiTerrainBrushMode.Lower:
                    return "Lowers the ground under the brush while the button is held";
                case OmsiTerrainBrushMode.Smooth:
                    return "Evens the ground out towards the average around each point";
                case OmsiTerrainBrushMode.Flatten:
                    return "Brings the ground to one height: where the stroke starts, or the height typed below";
                case OmsiTerrainBrushMode.Roughen:
                    return "Adds the gentle rolls and dips of a natural field";
                case OmsiTerrainBrushMode.Water:
                    return "Digs a lake or a river bed under the water of the tile, or 0.3 m under the ground where the stroke starts, " +
                           "and lays water on the tiles it reaches; it never fills";
                default:
                    return "Click where it starts and where it ends: the ground between becomes one even slope as wide as the brush";
            }
        }

        private void AddSlider(VisualElement row, Slider slider, Label value)
        {
            slider.AddToClassList("new-map-slider");
            row.tooltip = slider.tooltip;
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

        private VisualElement Row(string label)
        {
            return Row(this, label, out _);
        }

        private static VisualElement Row(VisualElement parent, string label, out Label text)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            text = new Label(label);
            text.AddToClassList("picker-option-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }
    }
}
