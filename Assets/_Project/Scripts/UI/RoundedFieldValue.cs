using System;
using System.Globalization;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A number field of the narrow inspector that shows its value to three decimals so it fits, yet keeps the full value: the field shows the
    /// full text while it has the focus, and a field left as shown applies the original value, so a round trip through the display never changes a number.
    /// </summary>
    public static class RoundedFieldValue
    {
        public const int Decimals = 3;

        private sealed class State
        {
            public string Full;
            public string Display;
        }

        /// <summary>The text shown for a stored number: rounded to <see cref="Decimals"/> places, or the text itself when it is not a number or already short.</summary>
        public static string DisplayOf(string full)
        {
            if (!double.TryParse(full, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
                return full;
            var rounded = Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
            return rounded == value ? full : OmsiNumberFormat.Format(rounded);
        }

        /// <summary>Makes <paramref name="field"/>, which holds the full text, show the rounded one until it is focused.</summary>
        public static void Attach(TextField field)
        {
            var full = field.value ?? string.Empty;
            var display = DisplayOf(full);
            if (display == full)
                return;
            var state = new State { Full = full, Display = display };
            field.userData = state;
            field.SetValueWithoutNotify(display);
            field.RegisterCallback<FocusInEvent>(_ =>
            {
                if (field.value == state.Display)
                    field.SetValueWithoutNotify(state.Full);
            });
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (field.value == state.Full)
                    field.SetValueWithoutNotify(state.Display);
            });
        }

        /// <summary>The full text when the field still shows what <see cref="Attach"/> put there; false when it was edited or never rounded.</summary>
        public static bool TryOriginal(TextField field, out string full)
        {
            if (field.userData is State state && field.value == state.Display)
            {
                full = state.Full;
                return true;
            }
            full = null;
            return false;
        }
    }
}
