using System;
using System.Collections.Generic;

namespace OmsiMapEditor.Core.Localization
{
    /// <summary>Where the chosen language is kept between runs: an application setting, never part of a project.</summary>
    public interface ILanguageStore
    {
        /// <summary>The language code kept, or empty when none was ever kept.</summary>
        string Stored { get; }

        void Store(string code);
    }

    /// <summary>
    /// Which language the program starts in: the one kept, else the system's if the program has it, else English. The first start keeps
    /// what it found, so a later change of the system language does not change the program's, and a language chosen by hand is
    /// never overwritten.
    /// </summary>
    public static class LanguageChoice
    {
        /// <summary>
        /// The language of <paramref name="available"/> that fits a request: the same code, else the first with the same primary
        /// language (de-AT gets de-DE, en-GB gets en-US), else null. Codes may be written with - or _ and in any case.
        /// </summary>
        public static string Match(string requested, IEnumerable<string> available)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return null;
            var wanted = requested.Trim().Replace('_', '-');
            string sameLanguage = null;
            var wantedLanguage = PluralRules.LanguageOf(wanted);
            foreach (var code in available)
            {
                if (string.Equals(code, wanted, StringComparison.OrdinalIgnoreCase))
                    return code;
                if (sameLanguage == null && PluralRules.LanguageOf(code) == wantedLanguage)
                    sameLanguage = code;
            }
            return sameLanguage;
        }

        public static string Choose(string stored, string system, IEnumerable<string> available, string fallback)
        {
            var codes = new List<string>(available);
            return Match(stored, codes) ?? Match(system, codes) ?? Match(fallback, codes) ?? fallback;
        }

        /// <summary>
        /// The language to start in, and the first start remembers it. What is kept stays as it is even when its language is gone for
        /// now, such as a language file the user removed; it comes back with the file.
        /// </summary>
        public static string Resolve(ILanguageStore store, string systemLanguage, IEnumerable<string> available)
        {
            var chosen = Choose(store?.Stored, systemLanguage, available, TranslationManager.ReferenceLanguage);
            if (store != null && string.IsNullOrWhiteSpace(store.Stored))
                store.Store(chosen);
            return chosen;
        }
    }
}
