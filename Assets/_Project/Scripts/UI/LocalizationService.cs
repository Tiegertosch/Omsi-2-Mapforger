using System;
using System.Globalization;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>The chosen language in the application's own preferences (PlayerPrefs), not in any project.</summary>
    internal sealed class PlayerPrefsLanguageStore : ILanguageStore
    {
        public const string Key = "Mapforge.Language";

        public string Stored => PlayerPrefs.GetString(Key, string.Empty);

        public void Store(string code)
        {
            PlayerPrefs.SetString(Key, code ?? string.Empty);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Starts the interface language (ADR 068): finds the language folders, makes the one <see cref="TranslationManager"/> and picks
    /// the language to start in: <c>-language &lt;code&gt;</c> for one run, else the one kept, else the system's, else English. Missing
    /// keys are logged in the editor and in development builds only.
    /// </summary>
    internal static class LocalizationService
    {
        public const string FolderName = "locales";

        private static bool started;

        public static ILanguageStore Store { get; } = new PlayerPrefsLanguageStore();

        /// <summary>The folder with one folder per language: next to the player's data in StreamingAssets, in the editor the repository's own.</summary>
        public static string LocalesFolder(string[] arguments)
        {
            var given = ArgumentAfter(arguments, "-localesFolder");
            if (!string.IsNullOrEmpty(given))
                return given;
            var streaming = Path.Combine(Application.streamingAssetsPath, FolderName);
            if (Directory.Exists(streaming))
                return streaming;
            // In the editor and in a player run from the project: Application.dataPath is the Assets folder.
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FolderName));
        }

        public static void Initialize(string[] arguments)
        {
            if (started)
                return;
            started = true;
            var development = Application.isEditor || Debug.isDebugBuild;
            var manager = new TranslationManager(LocalesFolder(arguments), development)
            {
                Warn = development ? (Action<string>)Debug.LogWarning : null,
                Error = Debug.LogError
            };
            manager.ReportIssues();
            Loc.Manager = manager;

            var forced = ArgumentAfter(arguments, "-language");
            var codes = manager.Languages.Select(info => info.Code).ToList();
            var start = forced != null
                ? LanguageChoice.Match(forced, codes) ?? TranslationManager.ReferenceLanguage
                : LanguageChoice.Resolve(Store, SystemLanguageCode(), codes);
            if (!manager.SetLanguage(start) && manager.ReferenceLoaded)
                manager.SetLanguage(TranslationManager.ReferenceLanguage);
            Debug.Log("Language: " + manager.Language + " (" + (forced != null ? "-language" : "kept or system language") + "; files in " + manager.Root + ")");
        }

        /// <summary>Switches the language and keeps the choice for the next start.</summary>
        public static bool Choose(string code)
        {
            var manager = Loc.Manager;
            if (manager == null || !manager.SetLanguage(code))
                return false;
            Store.Store(manager.Language);
            return true;
        }

        /// <summary>
        /// The system's language as a code. Unity's own report is used: it comes from the operating system's display language, while the
        /// culture of the player's runtime may be another.
        /// </summary>
        public static string SystemLanguageCode()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.German: return "de-DE";
                case SystemLanguage.English: return "en-US";
                case SystemLanguage.Polish: return "pl-PL";
                case SystemLanguage.Czech: return "cs-CZ";
                case SystemLanguage.French: return "fr-FR";
                case SystemLanguage.Spanish: return "es-ES";
                default: return null;
            }
        }

        /// <summary>
        /// A line in the log for the language checks: a number, a date and a plural written by the language in use, and the system's
        /// own report, which is how the player's runtime was seen to handle cultures (<c>-i18nSelfTest</c>).
        /// </summary>
        public static string SelfTest()
        {
            var manager = Loc.Manager;
            if (manager == null)
                return "No language loaded.";
            var when = new DateTime(2026, 11, 7, 14, 5, 0);
            var lines = new[]
            {
                manager.T("dev.selftest.objects", ("count", 0), ("name", "Grundorf")),
                manager.T("dev.selftest.objects", ("count", 1), ("name", "Grundorf")),
                manager.T("dev.selftest.objects", ("count", 1234), ("name", "Grundorf")),
                manager.T("dev.selftest.when", ("when", when), ("value", 1234.5))
            };
            return string.Join(" | ", lines) + " | systemLanguage=" + Application.systemLanguage + " culture=" + CultureInfo.CurrentCulture.Name +
                   " uiculture=" + CultureInfo.CurrentUICulture.Name;
        }

        private static string ArgumentAfter(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length && !arguments[index + 1].StartsWith("-", StringComparison.Ordinal) ? arguments[index + 1] : null;
        }
    }
}
