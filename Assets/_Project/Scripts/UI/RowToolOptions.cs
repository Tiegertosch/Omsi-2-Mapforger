using System;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The row options of the Objects window, shown in its option column while the window is in Row mode (ADR 098, M4): the distance
    /// between objects, the offset from the road's centre, height, heading, following the slope, both sides and whether a row runs
    /// along the whole road. The shell plans and lays the rows; the options only hold the settings. While a row of the map is
    /// selected, the fields show its values instead and the laying values wait. What the options used to say as text goes to the
    /// hint of the status bar, through <see cref="PlanChanged"/>.
    /// </summary>
    internal sealed class RowToolOptions : VisualElement
    {
        private readonly Label title;
        private readonly TextField intervalField;
        private readonly TextField offsetField;
        private readonly TextField heightField;
        private readonly TextField headingField;
        private readonly Toggle bothSidesToggle;
        private readonly Toggle wholeRoadToggle;
        private readonly Toggle tiltToggle;

        /// <summary>The laying values of the fields while a selected row's values are shown; null otherwise.</summary>
        private string[] laying;

        private bool layingTilt;

        public RowToolOptions()
        {
            title = OptionControls.Heading(this, "rows.selected");
            title.style.display = DisplayStyle.None;

            intervalField = Field(OptionControls.Value(OptionControls.Row(this, "rows.row.spacing"), "ruler", "25", "m", "rows.interval", "rows.interval.tooltip"));
            var position = OptionControls.Row(this, "rows.row.position");
            offsetField = Field(OptionControls.Value(position, "move-horizontal", string.Empty, "m", "rows.offset", "rows.offset.tooltip"));
            heightField = Field(OptionControls.Value(position, "move-vertical", "0", "m", "rows.height", "rows.height.tooltip"));
            var heading = OptionControls.Row(this, "objects.heading");
            headingField = Field(OptionControls.Value(heading, "compass", "0", "°", "rows.heading", "rows.heading.tooltip"));
            // A selected row changes at once; the laying values only plan the next row again.
            tiltToggle = OptionControls.Switch(heading, "trending-up", "rows.tilt", "rows.tilt.tooltip", false);
            tiltToggle.RegisterValueChangedCallback(_ =>
            {
                if (IsEditing)
                    Committed?.Invoke();
                else
                    SettingsChanged?.Invoke();
            });
            var lay = OptionControls.Row(this, "rows.row.layout");
            bothSidesToggle = OptionControls.Switch(lay, "arrow-left-right", "rows.both", "rows.both.tooltip", false);
            bothSidesToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            wholeRoadToggle = OptionControls.Switch(lay, "route", "rows.whole", "rows.whole.tooltip", true);
            wholeRoadToggle.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
        }

        /// <summary>The line about the next row or the selected one changed; the shell shows it in the hint of the status bar, a problem in its colour.</summary>
        public event Action<string, bool> PlanChanged;

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
            title.style.display = DisplayStyle.Flex;
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
            title.style.display = DisplayStyle.None;
            bothSidesToggle.SetEnabled(true);
            wholeRoadToggle.SetEnabled(true);
        }

        /// <summary>The object of the selected row, in the tooltip of its title.</summary>
        public void ShowObject(string assetPath)
        {
            title.tooltip = assetPath ?? string.Empty;
        }

        public void ShowPlan(string text, bool problem)
        {
            PlanChanged?.Invoke(text, problem);
        }

        private TextField Field(TextField field)
        {
            field.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    Committed?.Invoke();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Committed?.Invoke());
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
