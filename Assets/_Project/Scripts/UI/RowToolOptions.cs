using System;
using System.Globalization;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The options of the row tool, shown beside the object library in place of its placing options: the object chosen
    /// in the library, the distance between objects, how far from the road's centre, height, heading, both sides and
    /// whether a row runs along the whole road. The shell plans and lays the rows; the options only hold the settings.
    /// </summary>
    internal sealed class RowToolOptions : VisualElement
    {
        private readonly Label objectLabel;
        private readonly TextField intervalField;
        private readonly TextField offsetField;
        private readonly TextField heightField;
        private readonly TextField headingField;
        private readonly Toggle bothSidesToggle;
        private readonly Toggle wholeRoadToggle;
        private readonly Label planLabel;

        public RowToolOptions()
        {
            AddToClassList("tool-panel");
            AddToClassList("picker-options");
            var title = new Label("Rows along roads");
            title.AddToClassList("picker-options-title");
            Add(title);

            objectLabel = new Label("Choose an object in the library.");
            objectLabel.AddToClassList("panel-info");
            Add(objectLabel);

            intervalField = Field(Row("Every (m)"), "25", "Distance between the objects of a row along the road");
            offsetField = Field(Row("From centre (m)"), string.Empty,
                "Distance of the objects from the road's centre line; empty puts them 0.5 m inside the edge of the road's profile, on the pavement");
            heightField = Field(Row("Height (m)"), "0", "Height of the objects above the road");
            headingField = Field(Row("Heading (°)"), "0", "Turn of each object against the road's direction; on the other side the object faces the road the other way round");

            bothSidesToggle = new Toggle { tooltip = "Lays a row on both sides of the road at once" };
            bothSidesToggle.AddToClassList("picker-option-toggle");
            bothSidesToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row("Both sides").Add(bothSidesToggle);

            wholeRoadToggle = new Toggle
            {
                value = true,
                tooltip = "A click lays the row along the whole road, from junction or free end to junction or free end, evenly spaced. " +
                          "Switch off to click where the row starts and where it ends."
            };
            wholeRoadToggle.AddToClassList("picker-option-toggle");
            wholeRoadToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row("Whole road").Add(wholeRoadToggle);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = new Label("Point at a road: the row lies on the side of the pointer. Click to lay it; R turns the objects by 15°, Shift+R back. Click an object of a row to select the row; Del deletes it. Esc ends the tool.");
            hint.AddToClassList("panel-info");
            Add(hint);
        }

        /// <summary>A setting changed; the shell plans the row again.</summary>
        public event Action SettingsChanged;

        public double Interval => Number(intervalField, 0.0) ?? 25.0;

        /// <summary>The distance from the centre line the user typed, or null for the profile's edge.</summary>
        public double? Offset => Number(offsetField, 0.0);

        public double Height => Number(heightField, double.MinValue) ?? 0.0;
        public double HeadingDegrees => Number(headingField, double.MinValue) ?? 0.0;
        public bool BothSides => bothSidesToggle.value;
        public bool WholeRoad => wholeRoadToggle.value;

        /// <summary>Turns the objects of the next row by some degrees, as R does while placing.</summary>
        public void TurnBy(double degrees)
        {
            var heading = ((HeadingDegrees + degrees) % 360.0 + 360.0) % 360.0;
            headingField.value = heading.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>Sets both sides, the distance between objects and the heading, as a user would; for command-line checks.</summary>
        public void Apply(bool bothSides, string interval, string heading)
        {
            bothSidesToggle.value = bothSides;
            if (!string.IsNullOrWhiteSpace(interval))
                intervalField.value = interval;
            if (!string.IsNullOrWhiteSpace(heading))
                headingField.value = heading;
        }

        public void ShowObject(string assetPath)
        {
            objectLabel.text = string.IsNullOrEmpty(assetPath) ? "Choose an object in the library." : "Object: " + System.IO.Path.GetFileNameWithoutExtension(assetPath);
            objectLabel.tooltip = assetPath ?? string.Empty;
        }

        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.tooltip = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
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
    }
}
