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
        private readonly Label planLabel;

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

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = new Label("Hold the left button over the ground to shape it; the ground never rises through a road beside it. " +
                                 "Ramp: click where it starts and where it ends. Roads fitted to the terrain stay fitted. Esc ends the tool.");
            hint.AddToClassList("panel-info");
            Add(hint);

            radiusSlider.SetValueWithoutNotify(RadiusToSlider(25.0));
            strengthSlider.SetValueWithoutNotify(0.5f);
            ShowValues();
            SetMode(OmsiTerrainBrushMode.Raise);
        }

        /// <summary>A setting changed; the shell draws the brush again.</summary>
        public event Action SettingsChanged;

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

        public void SetMode(OmsiTerrainBrushMode? mode)
        {
            Mode = mode;
            foreach (var pair in modeButtons)
                pair.Key.EnableInClassList("panel-tab-active", pair.Value == mode);
            flattenField.SetEnabled(mode == OmsiTerrainBrushMode.Flatten);
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
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = new Label(label);
            text.AddToClassList("picker-option-label");
            row.Add(text);
            Add(row);
            return row;
        }
    }
}
