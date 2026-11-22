using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The menus of the menu bar (File, Edit, Tools, Help) stand under their own button and not at a fixed distance from the left edge
    /// (M6 step 4): the buttons move with the language and with the interface scale, and a menu must not run out of the window.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private const float MenuEdgeMargin = 4f;

        private static readonly (string Menu, string Button, string Argument)[] MenuButtons =
        {
            ("file-menu", "file-menu-button", "file"), ("edit-menu", "edit-menu-button", "edit"),
            ("tools-menu", "tools-menu-button", "tools"), ("help-menu", "help-menu-button", "help")
        };

        /// <summary>
        /// A press outside the open menu closes it and goes no further, as in Windows (M8 step 4, after the owner's hand test): the map
        /// selects nothing, no tool acts. Not closed by it: a press inside the menu and one on a button of the menu bar, which toggles
        /// (its own menu closes, another one replaces the open one). Any mouse button counts. The rule is separate for the test.
        /// </summary>
        internal static bool PressClosesBarMenu(bool anyMenuOpen, bool insideOpenMenu, bool onMenuBarButton)
        {
            return anyMenuOpen && !insideOpenMenu && !onMenuBarButton;
        }

        private static bool IsSelfOrInside(VisualElement container, VisualElement element)
        {
            return container != null && element != null && (ReferenceEquals(container, element) || container.Contains(element));
        }

        private void OnPressOutsideBarMenu(PointerDownEvent evt, VisualElement root)
        {
            var target = evt.target as VisualElement;
            var anyOpen = false;
            var inside = false;
            var onButton = false;
            foreach (var (menuName, buttonName, _) in MenuButtons)
            {
                var menu = root.Q<VisualElement>(menuName);
                if (IsShown(menu))
                {
                    anyOpen = true;
                    inside |= IsSelfOrInside(menu, target);
                }
                onButton |= IsSelfOrInside(root.Q<VisualElement>(buttonName), target);
            }
            if (!PressClosesBarMenu(anyOpen, inside, onButton))
                return;
            foreach (var (menuName, _, _) in MenuButtons)
                HidePopup(root.Q<VisualElement>(menuName));
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void SetUpMenuPlacement(VisualElement root)
        {
            root.RegisterCallback<PointerDownEvent>(evt => OnPressOutsideBarMenu(evt, root), TrickleDown.TrickleDown);
            foreach (var (menuName, buttonName, _) in MenuButtons)
            {
                var menu = root.Q<VisualElement>(menuName);
                var button = root.Q<VisualElement>(buttonName);
                if (menu == null || button == null)
                    continue;
                // Entries that need a map follow the state of the map each time a menu is opened.
                if (button is Button menuButton)
                    menuButton.clicked += UpdateMapMenuItems;
                EventCallback<GeometryChangedEvent> place = _ => PlaceMenuUnder(root, menu, button);
                menu.RegisterCallback(place);
                button.RegisterCallback(place);
            }
            UpdateMapMenuItems();
        }

        /// <summary>Sets the menu's left edge to the button's, but keeps the menu inside the window; changes only when it differs, so it does not trigger itself.</summary>
        private static void PlaceMenuUnder(VisualElement root, VisualElement menu, VisualElement button)
        {
            var rootBound = root.worldBound;
            var buttonBound = button.worldBound;
            if (float.IsNaN(rootBound.width) || float.IsNaN(buttonBound.xMin) || menu.resolvedStyle.display == DisplayStyle.None)
                return;
            // Without a width the right edge cannot be kept inside: wait for the next GeometryChangedEvent instead of placing for one frame.
            var left = MenuLeft(buttonBound.xMin - rootBound.xMin, rootBound.width, menu.resolvedStyle.width);
            if (!left.HasValue)
                return;
            if (Mathf.Abs(menu.resolvedStyle.left - left.Value) > 0.5f)
                menu.style.left = left.Value;
        }

        /// <summary>The left edge of a menu under its button, inside the window; null while the menu has no width yet.</summary>
        internal static float? MenuLeft(float buttonLeft, float windowWidth, float menuWidth)
        {
            if (float.IsNaN(menuWidth) || float.IsNaN(buttonLeft) || float.IsNaN(windowWidth))
                return null;
            return Mathf.Max(MenuEdgeMargin, Mathf.Min(buttonLeft, windowWidth - menuWidth - MenuEdgeMargin));
        }

        /// <summary>For checks: <c>-openMenu file|edit|tools|help</c> opens that menu of the menu bar.</summary>
        private void RunMenuArgument(string[] args)
        {
            var at = Array.IndexOf(args, "-openMenu");
            if (at < 0 || at + 1 >= args.Length || rootElement == null)
                return;
            foreach (var (menuName, _, argument) in MenuButtons)
                if (string.Equals(args[at + 1], argument, StringComparison.OrdinalIgnoreCase))
                {
                    var menu = rootElement.Q<VisualElement>(menuName);
                    foreach (var (otherName, _, _) in MenuButtons)
                        if (otherName != menuName)
                            HidePopup(rootElement.Q<VisualElement>(otherName));
                    UpdateMapMenuItems();
                    if (menu != null)
                        menu.style.display = DisplayStyle.Flex;
                    Debug.Log("Open menu: " + argument);
                }
        }
    }
}
