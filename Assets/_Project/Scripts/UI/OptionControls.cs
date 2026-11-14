using System;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The controls of a tool's option column as in the references of ADR 098: a short label at the left of a row, icons for choices
    /// and switches, numbers in small fields with an icon and their unit. Name and explanation of each stand in its tooltip.
    /// </summary>
    internal static class OptionControls
    {
        /// <summary>A row with its label; the controls follow at its right.</summary>
        public static VisualElement Row(VisualElement parent, string labelKey)
        {
            var row = new VisualElement();
            row.AddToClassList("option-row");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("option-row-label");
            row.Add(label);
            parent.Add(row);
            return row;
        }

        /// <summary>A group of choices standing together, such as the modes of a tool.</summary>
        public static VisualElement Group(VisualElement row)
        {
            var group = new VisualElement();
            group.AddToClassList("option-icons");
            row.Add(group);
            return group;
        }

        /// <summary>One choice as an icon; the caller marks the chosen one with <c>panel-tab-active</c>.</summary>
        public static Button Choice(VisualElement group, string icon, string nameKey, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            LocBindings.TitledTooltip(button, nameKey, tooltipKey); // i18n:keys
            button.AddToClassList("option-icon");
            button.Add(new IconElement(icon));
            group.Add(button);
            return button;
        }

        /// <summary>A switch drawn as an icon that lights up while on; it stays a <see cref="Toggle"/> for its value and events.</summary>
        public static Toggle Switch(VisualElement parent, string icon, string nameKey, string tooltipKey, bool value)
        {
            var toggle = new Toggle { value = value };
            LocBindings.TitledTooltip(toggle, nameKey, tooltipKey); // i18n:keys
            toggle.AddToClassList("option-switch");
            toggle.Q(className: Toggle.inputUssClassName)?.Add(new IconElement(icon));
            parent.Add(toggle);
            return toggle;
        }

        /// <summary>A small number field with an icon before it and its unit after it; the element returned is the field.</summary>
        public static TextField Value(VisualElement row, string icon, string value, string unit, string nameKey, string tooltipKey)
        {
            var field = new TextField { value = value };
            ValueBox(row, icon, field, unit, nameKey, tooltipKey);
            return field;
        }

        /// <summary>Puts any field (text, whole numbers, decimals) in the small box with an icon and a unit.</summary>
        public static void ValueBox(VisualElement row, string icon, VisualElement field, string unit, string nameKey, string tooltipKey)
        {
            var box = new VisualElement();
            box.AddToClassList("option-value");
            LocBindings.TitledTooltip(box, nameKey, tooltipKey); // i18n:keys
            var mark = new IconElement(icon) { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("option-value-icon");
            box.Add(mark);
            field.AddToClassList("picker-field");
            field.AddToClassList("option-value-field");
            box.Add(field);
            if (!string.IsNullOrEmpty(unit))
            {
                var unitLabel = new Label(unit) { pickingMode = PickingMode.Ignore };
                unitLabel.AddToClassList("option-value-unit");
                box.Add(unitLabel);
            }
            row.Add(box);
        }

        /// <summary>A slider with an icon before it and its current value after it; the caller keeps the value text up to date.</summary>
        public static void SliderBox(VisualElement row, string icon, VisualElement slider, Label valueLabel, string nameKey, string tooltipKey)
        {
            var box = new VisualElement();
            box.AddToClassList("option-slider");
            LocBindings.TitledTooltip(box, nameKey, tooltipKey); // i18n:keys
            var mark = new IconElement(icon) { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("option-value-icon");
            box.Add(mark);
            slider.AddToClassList("new-map-slider");
            slider.AddToClassList("option-slider-control");
            box.Add(slider);
            valueLabel.AddToClassList("option-slider-value");
            valueLabel.pickingMode = PickingMode.Ignore;
            box.Add(valueLabel);
            row.Add(box);
        }

        /// <summary>A short heading above a block of rows that belong together, such as a list.</summary>
        public static Label Heading(VisualElement parent, string textKey)
        {
            var heading = LocBindings.NewLabel(textKey); // i18n:keys
            heading.AddToClassList("option-heading");
            parent.Add(heading);
            return heading;
        }
    }
}
