using System;
using System.Collections.Generic;

namespace OmsiMapEditor.Core.Localization
{
    /// <summary>
    /// The plural categories of the languages the program knows, after the cardinal rules of the Unicode CLDR: which of zero, one,
    /// two, few, many and other a number falls into. Other languages only have "other" until their rules are added here.
    /// </summary>
    public static class PluralRules
    {
        /// <summary>The primary language subtag of a code, in lower case: "de" for "de-DE".</summary>
        public static string LanguageOf(string code)
        {
            if (string.IsNullOrEmpty(code))
                return string.Empty;
            var cut = code.IndexOfAny(new[] { '-', '_' });
            return (cut < 0 ? code : code.Substring(0, cut)).ToLowerInvariant();
        }

        /// <summary>
        /// The category of <paramref name="value"/> in the language. <paramref name="visibleFractionDigits"/> is how many decimals
        /// the number is shown with, as 1.0 is plural in English and 1 is not.
        /// </summary>
        public static string Select(string language, decimal value, int visibleFractionDigits)
        {
            var n = Math.Abs(value);
            var integer = Math.Truncate(n);
            var wholeNumber = visibleFractionDigits == 0;
            switch (LanguageOf(language))
            {
                case "en":
                case "de":
                    return integer == 1 && wholeNumber ? "one" : "other";
                case "es":
                    if (n == 1)
                        return "one";
                    return wholeNumber && integer != 0 && integer % 1000000m == 0 ? "many" : "other";
                case "fr":
                    if (integer == 0 || integer == 1)
                        return "one";
                    return wholeNumber && integer % 1000000m == 0 ? "many" : "other";
                case "pl":
                    if (!wholeNumber)
                        return "other";
                    if (integer == 1)
                        return "one";
                    var last = integer % 10m;
                    var lastTwo = integer % 100m;
                    if (last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14))
                        return "few";
                    return "many";
                case "cs":
                    if (!wholeNumber)
                        return "many";
                    if (integer == 1)
                        return "one";
                    return integer >= 2 && integer <= 4 ? "few" : "other";
                default:
                    return "other";
            }
        }

        /// <summary>The categories a translation into the language has to give, besides the exact =N cases: always "other".</summary>
        public static IReadOnlyList<string> RequiredCategories(string language)
        {
            switch (LanguageOf(language))
            {
                case "pl":
                    return new[] { "one", "few", "many", "other" };
                case "cs":
                    return new[] { "one", "few", "other" };
                case "en":
                case "de":
                case "es":
                case "fr":
                    return new[] { "one", "other" };
                default:
                    return new[] { "other" };
            }
        }
    }
}
