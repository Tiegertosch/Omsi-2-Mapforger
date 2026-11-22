using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The scale of the interface (ADR 098, M6 step 3): the Windows display scale (dpi / 96) times the user's "Interface size" times
    /// the default factor of <c>editor-settings.json</c>, kept so far that the logical window width never falls below
    /// <see cref="MinLogicalWidth"/>. The check argument <c>-uiScale &lt;factor&gt;</c> replaces the product for one run (the limit still applies).
    /// The arithmetic is in static methods without any Unity runtime dependency, so tests can run it.
    /// </summary>
    public static class EditorShellScale
    {
        public const float MinScale = 0.5f;
        public const float MaxScale = 3f;

        /// <summary>Reference dpi of Windows at 100 %; also the fallback when Unity reports 0.</summary>
        public const float ReferenceDpi = 96f;

        public const int MinUserPercent = 90;
        public const int MaxUserPercent = 200;
        public const int UserPercentStep = 10;
        public const int DefaultUserPercent = 100;

        /// <summary>The window is never narrower than this many logical (scaled) pixels.</summary>
        public const float MinLogicalWidth = 900f;

        public const string UserPercentKey = "Mapforge.InterfaceSizePercent";

        /// <summary>The outcome of one calculation.</summary>
        public readonly struct Result
        {
            public Result(float dpi, float dpiFactor, float userFactor, float wanted, float effective, bool clamped)
            {
                Dpi = dpi;
                DpiFactor = dpiFactor;
                UserFactor = userFactor;
                Wanted = wanted;
                Effective = effective;
                Clamped = clamped;
            }

            /// <summary>The dpi used (96 when Unity reported none).</summary>
            public float Dpi { get; }
            public float DpiFactor { get; }
            public float UserFactor { get; }

            /// <summary>The scale the settings ask for, before the window limit.</summary>
            public float Wanted { get; }

            /// <summary>The scale that is applied.</summary>
            public float Effective { get; }

            public bool Clamped { get; }
        }

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

        /// <summary>The dpi to calculate with: the reported one, or 96 when it is 0, negative or not a number.</summary>
        public static float EffectiveDpi(float reportedDpi)
        {
            return reportedDpi > 0f && !float.IsNaN(reportedDpi) && !float.IsInfinity(reportedDpi) ? reportedDpi : ReferenceDpi;
        }

        /// <summary>Puts a percentage into 90 to 200 and onto steps of 10 (a stored value from elsewhere cannot break the scale).</summary>
        public static int NormalizePercent(int percent)
        {
            var clamped = Math.Max(MinUserPercent, Math.Min(MaxUserPercent, percent));
            return (int)Math.Round(clamped / (double)UserPercentStep, MidpointRounding.AwayFromZero) * UserPercentStep;
        }

        /// <summary>
        /// The scale of a run. <paramref name="argument"/> (from <c>-uiScale</c>) replaces dpi and user factor. The window limit lowers the
        /// result to <c>window width / 900</c>, but never below 1 (a small window is not shrunk beyond 100 %) and never below what was asked
        /// for when that is smaller. The wanted value is returned unchanged so the stored setting is not touched by the limit.
        /// </summary>
        public static Result Calculate(float reportedDpi, int userPercent, float configured, float windowWidthPixels, float? argument)
        {
            var dpi = EffectiveDpi(reportedDpi);
            var dpiFactor = dpi / ReferenceDpi;
            var userFactor = NormalizePercent(userPercent) / 100f;
            var wanted = argument ?? dpiFactor * userFactor * (configured > 0f && !float.IsNaN(configured) ? configured : 1f);
            var effective = wanted;
            if (windowWidthPixels > 0f && !float.IsNaN(windowWidthPixels))
            {
                var limit = Math.Max(1f, windowWidthPixels / MinLogicalWidth);
                if (wanted > limit)
                    effective = limit;
            }
            effective = Math.Max(MinScale, Math.Min(MaxScale, effective));
            return new Result(dpi, dpiFactor, userFactor, wanted, effective, effective < wanted - 0.0001f);
        }

        /// <summary>The user's "Interface size" in percent from the preferences (100 when never set).</summary>
        public static int LoadUserPercent()
        {
            if (!cachedUserPercent.HasValue)
                cachedUserPercent = NormalizePercent(PlayerPrefs.GetInt(UserPercentKey, DefaultUserPercent));
            return cachedUserPercent.Value;
        }

        public static void SaveUserPercent(int percent)
        {
            PlayerPrefs.SetInt(UserPercentKey, NormalizePercent(percent));
            PlayerPrefs.Save();
            cachedUserPercent = null;
        }

        private static int? cachedUserPercent;

        /// <summary>Whether the scale has to be calculated again: the window width or the reported dpi changed since the last run.</summary>
        public static bool NeedsRecalculation(float lastWidth, float lastDpi, float width, float dpi)
        {
            return Math.Abs(lastWidth - width) > 0.5f || Math.Abs(lastDpi - dpi) > 0.01f;
        }

        /// <summary>
        /// Sets the panel scale from the current dpi, the stored user factor and the window width, and writes
        /// <c>UI scale: dpi=… user=… scale=…</c> to the log when the result changed.
        /// </summary>
        public static Result Apply(PanelSettings panelSettings, float configured, string[] arguments, Result? previous)
        {
            var argument = ArgumentFactor(arguments);
            var result = Calculate(Screen.dpi, LoadUserPercent(), configured, Screen.width, argument);
            // The factor is calculated here, not by Unity's physical-size mode, so that the user's factor and the window limit apply to it.
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = result.Effective;
            panelSettings.referenceDpi = ReferenceDpi;
            panelSettings.fallbackDpi = ReferenceDpi;
            if (!previous.HasValue || Math.Abs(previous.Value.Effective - result.Effective) > 0.0001f || Math.Abs(previous.Value.Wanted - result.Wanted) > 0.0001f)
                Debug.Log(Describe(result, argument.HasValue, Screen.width));
            return result;
        }

        /// <summary>The text of the log line.</summary>
        public static string Describe(Result result, bool fromArgument, float windowWidthPixels)
        {
            var text = "UI scale: dpi=" + result.Dpi.ToString("0.#", CultureInfo.InvariantCulture) +
                " user=" + result.UserFactor.ToString("0.###", CultureInfo.InvariantCulture) +
                " scale=" + result.Effective.ToString("0.###", CultureInfo.InvariantCulture);
            if (fromArgument)
                text += " (-uiScale " + result.Wanted.ToString("0.###", CultureInfo.InvariantCulture) + ")";
            if (result.Clamped)
                text += " clamped from " + result.Wanted.ToString("0.###", CultureInfo.InvariantCulture) + " (window " +
                    windowWidthPixels.ToString("0", CultureInfo.InvariantCulture) + " px, logical width >= " + MinLogicalWidth.ToString("0", CultureInfo.InvariantCulture) + ")";
            return text;
        }
    }
}
