using System;
using System.Globalization;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Writes numbers the way the OMSI editor does: up to 15 significant digits, invariant culture, and a short
    /// exponent such as <c>-3.59590604939723E-6</c>. This reproduces 243,482 of 243,487 floating point fields in the
    /// installed maps; the rest were written by other tools and are only rewritten when their value changes.
    /// </summary>
    public static class OmsiNumberFormat
    {
        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "OMSI files cannot store NaN or infinity.");
            if (value == 0.0)
                return "0";

            var text = value.ToString("G15", CultureInfo.InvariantCulture);
            var exponent = text.IndexOf('E');
            if (exponent < 0)
                return text;

            // .NET writes E-06 and E+20; OMSI writes E-6 and E20.
            var builder = new StringBuilder(text, 0, exponent + 1, text.Length);
            var index = exponent + 1;
            if (text[index] == '-')
                builder.Append('-');
            if (text[index] == '-' || text[index] == '+')
                index++;
            while (index < text.Length - 1 && text[index] == '0')
                index++;
            builder.Append(text, index, text.Length - index);
            return builder.ToString();
        }

        public static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
