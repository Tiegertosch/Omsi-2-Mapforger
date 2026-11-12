using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The bottom bar (ADR 098): the view and zoom controls at the left end, a tile button with the number of tiles and the name of
    /// the project, the tools in the middle, and round buttons at the right for the map's lists, which were the tabs of the project
    /// browser (ADR 078). Each list opens as one popup above the bar; the map stays free of standing panels.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement bottomBar;
        private VisualElement barPopup;
        private Label barPopupTitle;
        private Label tileCountBadge;
        private VisualElement projectPill;
        private Label projectPillLabel;
        private VisualElement projectModeBadge;
        private Label projectModeLabel;
        private readonly Dictionary<ProjectTab, Button> barButtons = new Dictionary<ProjectTab, Button>();
        private bool barPopupOpen;

        // Below this width of the window the bar uses its compact form.
        private const float CompactBarWidth = 1400f;

        private static readonly (ProjectTab Tab, string Icon, string TooltipKey)[] RightBarButtons =
        {
            (ProjectTab.Paths, "waypoints", "bar.paths.tooltip"), (ProjectTab.Entries, "flag", "bar.entries.tooltip"), (ProjectTab.Lines, "route", "bar.lines.tooltip"),
            (ProjectTab.Chrono, "calendar", "bar.chrono.tooltip"), (ProjectTab.Map, "settings-2", "bar.map.tooltip")
        };

        /// <summary>Builds the buttons of the bar around the view controls and the tool bar the UXML holds.</summary>
        private void SetUpBottomBar(VisualElement root)
        {
            bottomBar = root.Q<VisualElement>("bottom-bar");
            barPopup = root.Q<VisualElement>("bar-popup");
            barPopupTitle = root.Q<Label>("project-title");
            var left = root.Q<VisualElement>("bar-left");
            var right = root.Q<VisualElement>("bar-right");
            if (bottomBar == null || left == null || right == null)
                return;

            var tiles = BarButton(ProjectTab.Tiles, "layout-grid", "bar.tiles.tooltip");
            tiles.RemoveFromClassList("bar-round-button");
            tiles.AddToClassList("bar-tile-button");
            tileCountBadge = new Label { pickingMode = PickingMode.Ignore };
            tileCountBadge.AddToClassList("bar-badge");
            tiles.Add(tileCountBadge);
            left.Add(tiles);

            // The pill only shows the name and the mode; the map's settings open from the Map button at the right.
            projectPill = new VisualElement();
            projectPill.AddToClassList("bar-pill");
            LocBindings.Tooltip(projectPill, "bar.project.tooltip");
            projectPillLabel = new Label { pickingMode = PickingMode.Ignore };
            projectPill.Add(projectPillLabel);
            projectModeBadge = new VisualElement { pickingMode = PickingMode.Ignore };
            projectModeBadge.AddToClassList("bar-mode");
            var modeDot = new VisualElement { pickingMode = PickingMode.Ignore };
            modeDot.AddToClassList("bar-mode-dot");
            projectModeBadge.Add(modeDot);
            projectModeLabel = new Label { pickingMode = PickingMode.Ignore };
            projectModeLabel.AddToClassList("bar-mode-text");
            projectModeBadge.Add(projectModeLabel);
            projectPill.Add(projectModeBadge);
            left.Add(projectPill);

            foreach (var (tab, icon, tooltipKey) in RightBarButtons)
                right.Add(BarButton(tab, icon, tooltipKey)); // i18n:keys

            // No media queries in USS: a narrow window gets a compact bar (shorter name pill, smaller round buttons).
            bottomBar.RegisterCallback<GeometryChangedEvent>(evt =>
                bottomBar.EnableInClassList("bar-compact", evt.newRect.width > 0f && evt.newRect.width < CompactBarWidth));

            var close = root.Q<Button>("project-collapse-button");
            if (close != null)
            {
                DecorateButton(close, "x", null);
                close.clicked += () => ShowBarPopup(false);
            }
            UpdateBarButtons();
        }

        /// <summary>
        /// Shows the view controls and the row of buttons once a map is open; before, the bar is only the strip with the status line,
        /// as small as that.
        /// </summary>
        private void SetBarContentVisible(bool visible)
        {
            if (bottomBar == null)
                return;
            bottomBar.EnableInClassList("bottom-bar-empty", !visible);
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            bottomBar.Q<VisualElement>("map-controls").style.display = display;
            bottomBar.Q<VisualElement>("bar-row").style.display = display;
        }

        private Button BarButton(ProjectTab tab, string icon, string tooltipKey)
        {
            var button = new Button(() => ToggleBarPopup(tab));
            button.AddToClassList("bar-round-button");
            LocBindings.Tooltip(button, tooltipKey); // i18n:keys
            button.Add(new IconElement(icon));
            barButtons[tab] = button;
            return button;
        }

        /// <summary>A click on a bar button opens its list, or closes it when it is the one that is open.</summary>
        private void ToggleBarPopup(ProjectTab tab)
        {
            if (barPopupOpen && projectTab == tab)
                ShowBarPopup(false);
            else
                ShowProjectTab(tab);
        }

        /// <summary>Opens or closes the popup of the bar; what it shows is <see cref="projectTab"/>.</summary>
        private void ShowBarPopup(bool show)
        {
            barPopupOpen = show && currentSummary != null;
            if (barPopup != null)
                barPopup.style.display = barPopupOpen ? DisplayStyle.Flex : DisplayStyle.None;
            RenderProjectBrowser();
        }

        /// <summary>The number of tiles on the tile button, the name on the pill, and which buttons work and which is open.</summary>
        private void UpdateBarButtons()
        {
            if (bottomBar == null)
                return;
            var loaded = currentSummary != null;
            if (tileCountBadge != null)
                tileCountBadge.text = loaded ? currentSummary.Tiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
            if (projectPillLabel != null)
                projectPillLabel.text = !loaded ? string.Empty
                    : !string.IsNullOrWhiteSpace(currentProjectName) ? currentProjectName
                    : string.IsNullOrEmpty(currentSummary.FriendlyName) ? currentSummary.Name : currentSummary.FriendlyName;
            UpdateProjectModeBadge();
            foreach (var pair in barButtons)
            {
                pair.Value.SetEnabled(loaded && ProjectTabAvailable(pair.Key));
                pair.Value.EnableInClassList("bar-button-active", barPopupOpen && projectTab == pair.Key);
            }
        }

        /// <summary>
        /// The badge on the project pill says what the map is open as when that is not plain editing: the variant being edited
        /// (yellow), the date the map is shown on, or read-only (both grey). An editable map shows no badge.
        /// </summary>
        private void UpdateProjectModeBadge()
        {
            if (projectModeBadge == null)
                return;
            var loaded = currentSummary != null;
            var text = !loaded ? null
                : EditingVariant ? editedVariant.Title
                : chronoDate != null ? chronoDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : session != null && session.CanEdit ? null
                : Loc.T("status.mode.readonly");
            projectModeBadge.style.display = text == null ? DisplayStyle.None : DisplayStyle.Flex;
            projectModeBadge.EnableInClassList("bar-mode-variant", loaded && EditingVariant);
            projectPill.EnableInClassList("bar-pill-with-mode", text != null);
            projectModeLabel.text = text ?? string.Empty;
        }

        private static string ProjectTabTitleKey(ProjectTab tab)
        {
            switch (tab)
            {
                case ProjectTab.Map: return "project.tab.map";
                case ProjectTab.Entries: return "project.tab.entries";
                case ProjectTab.Chrono: return "project.tab.chrono";
                case ProjectTab.Lines: return "project.tab.lines";
                case ProjectTab.Paths: return "project.tab.paths";
                default: return "project.tab.tiles";
            }
        }
    }
}
