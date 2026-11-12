using System;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The one row at the top of a picker (ADR 098, stage 2): its tabs as icons, a magnifier that unfolds the search, and the X.
    /// Name and description of a tab show in its tooltip.
    /// </summary>
    internal static class PickerHeader
    {
        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("picker-header");
            return row;
        }

        public static Button IconTab(string icon, string nameKey, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            LocBindings.TitledTooltip(button, nameKey, tooltipKey); // i18n:keys
            button.AddToClassList("panel-tab");
            button.AddToClassList("picker-tab");
            button.AddToClassList("picker-icon-tab");
            button.Add(new IconElement(icon));
            return button;
        }

        /// <summary>The magnifier: a click unfolds the field and puts the cursor in it, another folds it and clears the search.</summary>
        public static Button SearchToggle(VisualElement searchRow, TextField search, string tooltipKey)
        {
            searchRow.style.display = DisplayStyle.None;
            var button = LocBindings.WithTooltip(new Button(), tooltipKey); // i18n:keys
            button.AddToClassList("panel-icon-button");
            button.Add(new IconElement("search"));
            button.clicked += () =>
            {
                var open = searchRow.style.display == DisplayStyle.None;
                ShowSearch(searchRow, button, open);
                if (open)
                    search.Focus();
                else
                    search.value = string.Empty;
            };
            return button;
        }

        public static void ShowSearch(VisualElement searchRow, Button toggle, bool open)
        {
            searchRow.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            toggle.EnableInClassList("picker-search-open", open);
        }

        public static Button Close(string tooltipKey, Action clicked)
        {
            var close = LocBindings.WithTooltip(new Button(clicked), tooltipKey); // i18n:keys
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            return close;
        }
    }
}
