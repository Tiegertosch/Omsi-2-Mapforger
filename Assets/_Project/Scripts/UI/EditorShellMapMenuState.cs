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

        /// <summary>The rule: an entry that needs a map is usable exactly while a map is loaded.</summary>
        internal static bool MapMenuItemEnabled(bool mapLoaded)
        {
            return mapLoaded;
        }

        /// <summary>Sets the state of the entries; called on start, on every load of a map and whenever a menu is opened.</summary>
        private void UpdateMapMenuItems()
        {
            if (rootElement == null)
                return;
            var enabled = MapMenuItemEnabled(currentSummary != null);
            foreach (var name in MapMenuItemNames)
                rootElement.Q<Button>(name)?.SetEnabled(enabled);
        }
    }
}
