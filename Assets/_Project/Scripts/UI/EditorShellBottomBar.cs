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
        private Label objectCountBadge;
        private VisualElement projectPill;
        private Label projectPillLabel;
        private VisualElement projectModeBadge;
        private Label projectModeLabel;
        private readonly Dictionary<ProjectTab, Button> barButtons = new Dictionary<ProjectTab, Button>();
        private bool barPopupOpen;
        // The height of the bar as laid out, in panel pixels (0 before the first layout); see ApplyBottomBarHeight.
        private float bottomBarHeight;

        // Below this width of the window the bar uses its compact form.
        private const float CompactBarWidth = 1400f;

        private static readonly (ProjectTab Tab, string Icon, string TooltipKey)[] RightBarButtons =
        {
            (ProjectTab.Paths, "waypoints", "bar.paths.tooltip"), (ProjectTab.Lines, "route", "bar.lines.tooltip"),
            (ProjectTab.SignalRoutes, "train-track", "bar.signalroutes.tooltip"),
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

            // The objects button stands right of the tile button; its badge counts the objects placed on the map.
            var objects = BarButton(ProjectTab.Objects, "boxes", "bar.objects.tooltip");
            objects.RemoveFromClassList("bar-round-button");
            objects.AddToClassList("bar-tile-button");
            objectCountBadge = new Label { pickingMode = PickingMode.Ignore };
            objectCountBadge.AddToClassList("bar-badge");
            objects.Add(objectCountBadge);
            left.Add(objects);

            // The pill only shows the name and the mode; the map's settings open from the Map button at the right.
            projectPill = new VisualElement();
            projectPill.AddToClassList("bar-pill");
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
            {
                // The topology button stands left of the map settings, the second last button of the bar (UI Research/Skitze.png).
                if (tab == ProjectTab.Map)
                    right.Add(CreateTopologyButton());
                right.Add(BarButton(tab, icon, tooltipKey)); // i18n:keys
            }

            // No media queries in USS: a narrow window gets a compact bar (shorter name pill, smaller round buttons).
            bottomBar.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                bottomBar.EnableInClassList("bar-compact", evt.newRect.width > 0f && evt.newRect.width < CompactBarWidth);
                ApplyBottomBarHeight(evt.newRect.height);
            });

            var close = root.Q<Button>("bar-popup-close-button");
            if (close != null)
            {
                DecorateButton(close, "x", null);
                close.clicked += () => ShowBarPopup(false);
            }
            UpdateBarButtons();
        }

        /// <summary>
        /// The one place that sets how far the windows above the bar keep from the bottom: the measured height of the bar plus the
        /// gap of each kind (<see cref="BottomBarLayout"/>). Runs whenever the bar changes size (empty bar, map opened, resolution
        /// or UI scale changed).
        /// </summary>
        private void ApplyBottomBarHeight(float measured)
        {
            bottomBarHeight = BottomBarLayout.CleanHeight(measured);
            var popupBottom = BottomBarLayout.DistanceAbove(bottomBarHeight, BottomBarLayout.PopupGap);
            var toolBottom = BottomBarLayout.DistanceAbove(bottomBarHeight, BottomBarLayout.ToolGap);
            if (inspectorFrame != null && !inspectorAtPointer) // the floating inspector sizes itself (UpdateInspectorPlace)
                inspectorFrame.style.bottom = popupBottom;
            if (barPopup != null)
                barPopup.style.bottom = popupBottom;
            if (topologyPopup != null)
                topologyPopup.style.bottom = popupBottom;
            if (objectsPanelHost != null)
                objectsPanelHost.style.bottom = toolBottom;
            if (imageryAttribution != null)
                imageryAttribution.style.bottom = toolBottom;
            if (signalWindow != null)
                signalWindow.BottomInset = toolBottom;
            if (roadBuilder != null)
                roadBuilder.BottomInset = toolBottom;
            UpdateRoadDock();
            KeepSatelliteWindowAboveBar();
        }

        private bool satelliteLimitRegistered;
        private float satelliteMaxHeight = -1f;

        /// <summary>
        /// The window for pictures from the air stands at the left and grows with its content up to 86 % of the area (USS); on a low area
        /// that reaches into the bottom bar, so its maximum height is the room between its top edge and the bar (M6 step 4).
        /// </summary>
        private void KeepSatelliteWindowAboveBar()
        {
            if (satelliteWindow == null || workspaceElement == null)
                return;
            if (!satelliteLimitRegistered)
            {
                satelliteLimitRegistered = true;
                satelliteWindow.RegisterCallback<GeometryChangedEvent>(_ => KeepSatelliteWindowAboveBar());
                workspaceElement.RegisterCallback<GeometryChangedEvent>(_ => KeepSatelliteWindowAboveBar());
            }
            var area = workspaceElement.resolvedStyle.height;
            var top = satelliteWindow.resolvedStyle.top;
            if (float.IsNaN(area) || float.IsNaN(top) || area <= 0f)
                return;
            var room = System.Math.Max(200f, area - top - BottomBarLayout.DistanceAbove(bottomBarHeight, BottomBarLayout.ToolGap));
            if (System.Math.Abs(room - satelliteMaxHeight) > 0.5f)
            {
                satelliteMaxHeight = room;
                satelliteWindow.style.maxHeight = room; // computed: room above the bottom bar
            }
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
            if (barPopupOpen)
                ShowTopologyPopup(false); // both open at the right edge: one at a time
            if (barPopup != null)
                barPopup.style.display = barPopupOpen ? DisplayStyle.Flex : DisplayStyle.None;
            RenderBarPopup();
        }

        /// <summary>The number of tiles on the tile button, the name on the pill, and which buttons work and which is open.</summary>
        private void UpdateBarButtons()
        {
            if (bottomBar == null)
                return;
            var loaded = currentSummary != null;
            if (tileCountBadge != null)
                tileCountBadge.text = loaded ? currentSummary.Tiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
            UpdateObjectBadge();
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
            UpdateTopologyButton();
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
                case ProjectTab.Chrono: return "project.tab.chrono";
                case ProjectTab.Lines: return "project.tab.lines";
                case ProjectTab.Paths: return "project.tab.paths";
                case ProjectTab.Objects: return "project.tab.objects";
                case ProjectTab.SignalRoutes: return "project.tab.signalroutes";
                default: return "project.tab.tiles";
            }
        }
    }
}
