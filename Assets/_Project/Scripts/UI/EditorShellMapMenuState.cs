using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Entries of the Edit and Tools menus that do nothing without a map are greyed out until one is loaded (M8 step 5, rest), as
    /// the button "Object and spline folders..." in Settings already was. Inspector stays: it opens without a map and shows its hint.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>Names of the menu entries that need a loaded map (UXML names).</summary>
        internal static readonly string[] MapMenuItemNames =
        {
            "place-object-menu-item", "delete-object-menu-item", "refit-terrain-menu-item",
            "test-junction-menu-item", "tee-junction-menu-item", "cross-junction-menu-item",
            "undo-menu-item", "redo-menu-item",
            "satellite-menu-item", "road-builder-menu-item", "check-map-menu-item"
        };

        /// <summary>Sets the state of the entries; called on start, on every load of a map and whenever a menu is opened.</summary>
        private void UpdateMapMenuItems()
        {
            if (rootElement == null)
                return;
            var enabled = currentSummary != null;
            foreach (var name in MapMenuItemNames)
                rootElement.Q<Button>(name)?.SetEnabled(enabled);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-logMenuState") >= 0)
                LogMenuState();
        }

        /// <summary>
        /// For checks (<c>-logMenuState</c>): the log lists which menu entries are greyed out; every call of <see cref="UpdateMapMenuItems"/>
        /// writes one line, so the lines before and after <c>-openMap</c> show the change.
        /// </summary>
        private void LogMenuState()
        {
            var greyed = new System.Collections.Generic.List<string>();
            var usable = new System.Collections.Generic.List<string>();
            foreach (var name in MapMenuItemNames)
            {
                var item = rootElement.Q<Button>(name);
                if (item == null)
                    greyed.Add(name + "(missing)");
                else if (item.enabledSelf)
                    usable.Add(name);
                else
                    greyed.Add(name);
            }
            Debug.Log("Menu state: map " + (currentSummary != null ? "loaded" : "not loaded") + "; greyed [" + string.Join(", ", greyed) +
                      "]; usable [" + string.Join(", ", usable) + "]");
        }
    }
}
