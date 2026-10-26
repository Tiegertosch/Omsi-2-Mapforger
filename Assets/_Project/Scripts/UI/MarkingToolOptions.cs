using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The options of the markings tool (ADR 044): the style of the next line or of the selected one, its curvature and
    /// deleting it. The shell picks the anchors, draws the lines and makes the junction again; the options only hold the
    /// settings. While a line of the junction is selected, they show its style and curvature, and changing them changes it.
    /// </summary>
    internal sealed class MarkingToolOptions : VisualElement
    {
        private readonly Label title;
        private readonly Label junctionLabel;
        private readonly Dictionary<OmsiMarkingStyle, Button> styleButtons = new Dictionary<OmsiMarkingStyle, Button>();
        private readonly TextField curvatureField;
        private readonly Button deleteButton;
        private readonly Label planLabel;

        public MarkingToolOptions()
        {
            AddToClassList("tool-panel");
            AddToClassList("picker-options");
            title = new Label("Road markings");
            title.AddToClassList("picker-options-title");
            Add(title);

            junctionLabel = new Label("Click a junction made in this project.");
            junctionLabel.AddToClassList("panel-info");
            Add(junctionLabel);

            var styles = new VisualElement();
            styles.AddToClassList("panel-tabs");
            styles.style.flexDirection = FlexDirection.Row;
            styles.style.flexWrap = Wrap.Wrap;
            foreach (var info in OmsiMarkingStyleInfo.All)
            {
                var style = info.Style;
                var button = new Button(() => ChooseStyle(style)) { text = info.Name, tooltip = info.Description };
                button.AddToClassList("panel-tab");
                styles.Add(button);
                styleButtons[style] = button;
            }
            Add(styles);

            var curvatureRow = new VisualElement();
            curvatureRow.AddToClassList("picker-option");
            var curvatureLabel = new Label("Curvature (%)");
            curvatureLabel.AddToClassList("picker-option-label");
            curvatureRow.Add(curvatureLabel);
            curvatureField = new TextField
            {
                value = string.Empty,
                tooltip = "How far the curve bulges between two arms, 0 to 80; empty bends it like a circle. Lines across one arm stay straight."
            };
            curvatureField.AddToClassList("picker-field");
            curvatureField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    CurvatureCommitted?.Invoke();
            }, TrickleDown.TrickleDown);
            curvatureField.RegisterCallback<FocusOutEvent>(_ => CurvatureCommitted?.Invoke());
            curvatureRow.tooltip = curvatureField.tooltip;
            curvatureRow.Add(curvatureField);
            Add(curvatureRow);

            deleteButton = new Button(() => DeleteRequested?.Invoke()) { text = "Delete line", tooltip = "Deletes the selected line (Del)" };
            deleteButton.AddToClassList("map-control-button");
            deleteButton.AddToClassList("inspector-edit-delete");
            Add(deleteButton);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = new Label("Click an anchor, then another: a line in the chosen style joins them. Click a line to select it; " +
                                 "Y switches the style, Del deletes it. Esc lets go of an anchor or a line, then ends the tool.");
            hint.AddToClassList("panel-info");
            Add(hint);
            ShowLine(null);
        }

        /// <summary>A style was chosen; the shell changes a selected line to it.</summary>
        public event Action<OmsiMarkingStyle> StyleChosen;

        /// <summary>Enter was pressed in the curvature field or it lost focus; the shell changes a selected line.</summary>
        public event Action CurvatureCommitted;

        public event Action DeleteRequested;

        public OmsiMarkingStyle Style { get; private set; } = OmsiMarkingStyle.LaneLine;

        /// <summary>The curvature typed, as a share of the line's chord, or null for a circle-like curve.</summary>
        public double? Curvature
        {
            get
            {
                var text = curvatureField.value?.Trim().Replace(',', '.') ?? string.Empty;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || double.IsInfinity(percent))
                    return null;
                return Math.Max(0.0, Math.Min(OmsiJunctionMarkings.MaximumCurvature, percent / 100.0));
            }
        }

        /// <summary>The style after the current one, as Y switches it.</summary>
        public OmsiMarkingStyle NextStyle()
        {
            var all = OmsiMarkingStyleInfo.All;
            for (var index = 0; index < all.Count; index++)
                if (all[index].Style == Style)
                    return all[(index + 1) % all.Count].Style;
            return all[0].Style;
        }

        /// <summary>Chooses a style as a click on its button does.</summary>
        public void ChooseStyle(OmsiMarkingStyle style)
        {
            ShowStyle(style);
            StyleChosen?.Invoke(style);
        }

        /// <summary>Shows a selected line's style and curvature, or with null the values for the next line.</summary>
        public void ShowLine(OmsiMarkingLine line)
        {
            title.text = line != null ? "Selected line" : "Road markings";
            deleteButton.SetEnabled(line != null);
            if (line == null)
                return;
            ShowStyle(line.Style);
            curvatureField.SetValueWithoutNotify(line.Curvature.HasValue ? (line.Curvature.Value * 100.0).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty);
        }

        public void ShowJunction(string text)
        {
            junctionLabel.text = text;
        }

        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.tooltip = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
        }

        private void ShowStyle(OmsiMarkingStyle style)
        {
            Style = style;
            foreach (var pair in styleButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == style);
        }
    }
}
