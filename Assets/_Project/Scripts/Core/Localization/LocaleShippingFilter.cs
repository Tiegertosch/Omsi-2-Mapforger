using System;
using System.IO;
using System.Text.RegularExpressions;

namespace OmsiMapEditor.Core.Localization
{
    /// <summary>
    /// Decides which files of the locales folder go into a player (M8 step 6): the language list and the *.json files directly in a
    /// language folder named like xx-YY. Hidden folders and files (state files of developer tools such as .omc) never ship.
    /// </summary>
    public static class LocaleShippingFilter
    {
        private const string LanguageListName = "languages.json";
        private static readonly Regex LanguageFolder = new Regex(@"^[a-z]{2,3}-[A-Z]{2}$", RegexOptions.CultureInvariant);

        /// <param name="relativePath">The path below the locales folder, with / or \ as separator.</param>
        public static bool ShouldShip(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return false;
            var parts = relativePath.Split(new[] { '/', (char)92 }, StringSplitOptions.None);
            foreach (var part in parts)
                if (part.Length == 0 || part.StartsWith(".", StringComparison.Ordinal))
                    return false;
            var fileName = parts[parts.Length - 1];
            if (!string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
                return false;
            if (parts.Length == 1)
                return string.Equals(fileName, LanguageListName, StringComparison.Ordinal);
            return parts.Length == 2 && LanguageFolder.IsMatch(parts[0]);
        }
    }
}
