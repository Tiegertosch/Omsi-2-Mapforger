using System;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The options of the row tool, shown beside the object library in place of its placing options: the object chosen
    /// in the library, the distance between objects, how far from the road's centre, height, heading, both sides and
    /// whether a row runs along the whole road. The shell plans and lays the rows; the options only hold the settings.
    /// While a row of the map is selected, the fields show its values instead and the laying values wait.
    /// </summary>
    internal sealed class RowToolOptions : ScrollView
    {
        private readonly Label title;
        private readonly Label objectLabel;
        private readonly TextField intervalField;
        private readonly TextField offsetField;
        private readonly TextField heightField;
        private readonly TextField headingField;
        private readonly Toggle bothSidesToggle;
        private readonly Toggle wholeRoadToggle;
        private readonly Toggle tiltToggle;
        private readonly Label planLabel;

        /// <summary>The laying values of the fields while a selected row's values are shown; null otherwise.</summary>
        private string[] laying;

        private bool layingTilt;

        public RowToolOptions() : base(ScrollViewMode.Vertical)
        {
            // Scrolls like the other option columns once taller than 210 px (ADR 098).
            horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            AddToClassList("tool-options-scroll");
            AddToClassList("tool-panel");
            AddToClassList("picker-options");
            title = LocBindings.NewLabel("rows.title");
            title.AddToClassList("picker-options-title");
            Add(title);

            objectLabel = LocBindings.NewLabel("rows.choose");
            objectLabel.AddToClassList("panel-info");
            Add(objectLabel);

            intervalField = Field(Row("rows.interval"), "25", "rows.interval.tooltip");
            offsetField = Field(Row("rows.offset"), string.Empty, "rows.offset.tooltip");
            heightField = Field(Row("rows.height"), "0", "rows.height.tooltip");
            headingField = Field(Row("rows.heading"), "0", "rows.heading.tooltip");

            bothSidesToggle = new Toggle();
            LocBindings.Tooltip(bothSidesToggle, "rows.both.tooltip");
            bothSidesToggle.AddToClassList("picker-option-toggle");
            bothSidesToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row("rows.both").Add(bothSidesToggle);

            wholeRoadToggle = new Toggle { value = true };
            LocBindings.Tooltip(wholeRoadToggle, "rows.whole.tooltip");
            wholeRoadToggle.AddToClassList("picker-option-toggle");
            wholeRoadToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            Row("rows.whole").Add(wholeRoadToggle);

            tiltToggle = new Toggle();
            LocBindings.Tooltip(tiltToggle, "rows.tilt.tooltip");
            tiltToggle.AddToClassList("picker-option-toggle");
            // A selected row changes at once; the laying values only plan the next row again.
            tiltToggle.RegisterValueChangedCallback(_ =>
            {
                if (IsEditing)
                    Committed?.Invoke();
                else
                    SettingsChanged?.Invoke();
            });
            Row("rows.tilt").Add(tiltToggle);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = LocBindings.NewLabel("rows.hint");
            hint.AddToClassList("panel-info");
            Add(hint);
        }

        /// <summary>A setting changed; the shell plans the row again.</summary>
        public event Action SettingsChanged;

        /// <summary>Enter was pressed in a field or a field lost focus; the shell changes a selected row to the values.</summary>
        public event Action Committed;

        /// <summary>True while the fields show the values of a selected row.</summary>
        public bool IsEditing => laying != null;

        public double Interval => Number(intervalField, 0.0) ?? 25.0;

        /// <summary>The distance from the centre line the user typed, or null for the profile's edge.</summary>
        public double? Offset => Number(offsetField, 0.0);

        public double Height => Number(heightField, double.MinValue) ?? 0.0;
        public double HeadingDegrees => Number(headingField, double.MinValue) ?? 0.0;
        public bool BothSides => bothSidesToggle.value;
        public bool WholeRoad => wholeRoadToggle.value;

        /// <summary>The objects follow the gradient and cant of the road.</summary>
        public bool Tilt => tiltToggle.value;

        /// <summary>Sets the tilt as a user would; for command-line checks.</summary>
        public void SetTilt(bool tilt)
        {
            tiltToggle.value = tilt;
        }

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

        /// <summary>Shows the values of a selected row; the laying values come back with <see cref="EndEditing"/>.</summary>
        public void BeginEditing(double interval, double offset, double height, double heading, bool tilt)
        {
            if (laying == null)
                layingTilt = tiltToggle.value;
            laying = laying ?? new[] { intervalField.value, offsetField.value, heightField.value, headingField.value };
            tiltToggle.SetValueWithoutNotify(tilt);
            LocBindings.Text(title, "rows.selected");
            intervalField.SetValueWithoutNotify(Format(interval));
            offsetField.SetValueWithoutNotify(Format(offset));
            heightField.SetValueWithoutNotify(Format(height));
            headingField.SetValueWithoutNotify(Format(heading));
            bothSidesToggle.SetEnabled(false);
            wholeRoadToggle.SetEnabled(false);
        }

        public void EndEditing()
        {
            if (laying == null)
                return;
            intervalField.SetValueWithoutNotify(laying[0]);
            offsetField.SetValueWithoutNotify(laying[1]);
            heightField.SetValueWithoutNotify(laying[2]);
            headingField.SetValueWithoutNotify(laying[3]);
            tiltToggle.SetValueWithoutNotify(layingTilt);
            laying = null;
            LocBindings.Text(title, "rows.title");
            bothSidesToggle.SetEnabled(true);
            wholeRoadToggle.SetEnabled(true);
        }

        public void ShowObject(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                LocBindings.Text(objectLabel, "rows.choose");
            else
                LocBindings.Text(objectLabel, "rows.object", ("name", System.IO.Path.GetFileNameWithoutExtension(assetPath)));
            objectLabel.tooltip = assetPath ?? string.Empty;
        }

        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.tooltip = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
        }

        private VisualElement Row(string labelKey)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = LocBindings.NewLabel(labelKey); // i18n:keys
            text.AddToClassList("picker-option-label");
            row.Add(text);
            Add(row);
            return row;
        }

        private TextField Field(VisualElement row, string value, string tooltipKey)
        {
            var field = new TextField { value = value };
            LocBindings.Tooltip(field, tooltipKey); // i18n:keys
            field.AddToClassList("picker-field");
            field.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    Committed?.Invoke();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Committed?.Invoke());
            row.tooltip = tooltip;
            row.Add(field);
            return field;
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
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
