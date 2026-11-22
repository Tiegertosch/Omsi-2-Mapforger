using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Where the road picker and the road type window stand while both are open: the picker at the left edge, the window right of it, both
    /// above the bottom bar. Pure rules, so the arithmetic is tested without a window.
    /// </summary>
    internal static class RoadDockRules
    {
        /// <summary>The picker never gets narrower than this (its tabs and a column of tiles).</summary>
        public const float LeastPickerWidth = 396f;

        /// <summary>The picker never gets wider than this (the width of its style).</summary>
        public const float MostPickerWidth = 780f;

        /// <summary>The width of the picker: what the area leaves after the window with its preferred width and the three gaps.</summary>
        public static float PickerWidth(float area, float preferredWindowWidth, float margin)
        {
            return Mathf.Clamp(area - preferredWindowWidth - 3f * margin, LeastPickerWidth, MostPickerWidth);
        }

        /// <summary>The window beside the picker: from its right edge plus a gap to the right margin, from the top margin to the bar.</summary>
        public static Rect WindowRect(Vector2 area, float pickerWidth, Vector2 preferredSize, float margin, float bottom)
        {
            var x = margin + pickerWidth + margin;
            var width = Mathf.Min(preferredSize.x, Mathf.Max(0f, area.x - x - margin));
            var height = Mathf.Min(preferredSize.y, Mathf.Max(0f, area.y - margin - bottom));
            return new Rect(x, margin, width, height);
        }
    }

    /// <summary>
    /// The road picker and the road type window side by side (M6 step 4, fix): the window used to open over the picker and the road options,
    /// which at a high interface scale (a window of 900 px or less) left nothing of the picker to use. While the window is shown the
    /// road options, which a click on the map cannot use then anyway, give way, the picker stands at the left edge with the width the
    /// area leaves it, and the window takes the rest of the width right of it.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private bool roadDocked;
        private bool roadDockWatching;

        /// <summary>Puts the picker and the window apart while the window shows, and back in the middle when it does not.</summary>
        private void UpdateRoadDock()
        {
            if (roadPanel == null || roadBuilder == null || objectsPanelHost == null)
                return;
            var area = (roadBuilder.parent ?? objectsPanelHost).contentRect;
            var dock = RoadBuilderShown && activeTool == EditorTool.Road && area.width > 1f && area.height > 1f;
            if (!roadDockWatching && roadBuilder.parent != null)
            {
                roadDockWatching = true;
                roadBuilder.parent.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    if (roadDocked && Mathf.Abs(evt.newRect.width - evt.oldRect.width) + Mathf.Abs(evt.newRect.height - evt.oldRect.height) > 0.5f)
                        UpdateRoadDock();
                });
            }
            objectsPanelHost.EnableInClassList("tool-panel-host-docked", dock);
            if (!dock)
            {
                if (roadDocked)
                {
                    roadDocked = false;
                    roadPanel.Dock(null);
                }
                return;
            }
            var wasDocked = roadDocked;
            roadDocked = true;
            var margin = FloatingWindowFrame.EdgeMargin;
            var picker = RoadDockRules.PickerWidth(area.width, RoadTypeWindow.PreferredSize.x, margin);
            roadPanel.Dock(picker);
            var rect = RoadDockRules.WindowRect(new Vector2(area.width, area.height), picker, RoadTypeWindow.PreferredSize, margin, roadBuilder.BottomInset);
            // A window the person has moved stays where it is, unless it covers the picker; the first time and a changed area place it anew.
            var covers = roadBuilder.IsLaidOut && roadBuilder.layout.xMin < margin + picker && roadBuilder.layout.xMax > margin;
            if (!wasDocked || covers || !roadBuilder.IsLaidOut)
                roadBuilder.PlaceAt(rect.x, rect.y, rect.width, rect.height);
        }
    }
}
