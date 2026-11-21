using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The scale of the interface. M6 step 0 only adds the check argument <c>-uiScale &lt;factor&gt;</c> and the log line;
    /// the automatic scale from the Windows display setting follows in step 3.
    /// </summary>
    public static class EditorShellScale
    {
        public const float MinScale = 0.5f;
        public const float MaxScale = 3f;

        /// <summary>Reads <c>-uiScale &lt;factor&gt;</c>; null when absent or not a number in range.</summary>
        public static float? ArgumentFactor(string[] arguments)
        {
            if (arguments == null)
                return null;
            var index = Array.IndexOf(arguments, "-uiScale");
            if (index < 0 || index + 1 >= arguments.Length)
                return null;
            if (!float.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) ||
                float.IsNaN(factor) || float.IsInfinity(factor) || factor < MinScale || factor > MaxScale)
                return null;
            return factor;
        }

        /// <summary>The factor of a run: the argument wins over the configured value.</summary>
        public static float Resolve(string[] arguments, float configured)
        {
            return ArgumentFactor(arguments) ?? configured;
        }

        /// <summary>Sets the panel scale and writes <c>UI scale: dpi=… scale=…</c> to the log.</summary>
        public static void Apply(PanelSettings panelSettings, float configured, string[] arguments)
        {
            var scale = Resolve(arguments, configured);
            panelSettings.scale = scale;
            Debug.Log("UI scale: dpi=" + Screen.dpi.ToString("0.#", CultureInfo.InvariantCulture) + " scale=" +
                scale.ToString("0.###", CultureInfo.InvariantCulture) + (ArgumentFactor(arguments).HasValue ? " (-uiScale)" : string.Empty));
        }
    }
}
