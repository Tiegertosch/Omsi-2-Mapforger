using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// What the windows above the bottom bar need to know about it: the height of the bar is measured once
    /// (<c>EditorShellBootstrap.OnBottomBarGeometryChanged</c>) and every window's bottom distance is that height plus a gap.
    /// The height itself lives in one place, <c>.bottom-bar</c> in <c>EditorShell.uss</c>.
    /// </summary>
    internal static class BottomBarLayout
    {
        /// <summary>The space between the bar and the inspector or a popup of the bar.</summary>
        public const float PopupGap = 12f;

        /// <summary>The space between the bar and the tool windows, the imagery attribution and the signal window.</summary>
        public const float ToolGap = 10f;

        /// <summary>The measured height of the bar, or 0 when it is not laid out yet (NaN) or negative.</summary>
        public static float CleanHeight(float measured) => float.IsNaN(measured) ? 0f : Mathf.Max(0f, measured);

        /// <summary>The distance of a window's lower edge from the bottom of the area: the bar's height plus the gap.</summary>
        public static float DistanceAbove(float barHeight, float gap) => CleanHeight(barHeight) + gap;
    }
}
