using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OmsiMapEditor.Core.Localization
{
    public sealed class LocaleReport
    {
        public List<LocaleIssue> Issues { get; } = new List<LocaleIssue>();
        public int LanguageCount { get; internal set; }
        public int KeyCount { get; internal set; }

        public int ErrorCount => Issues.Count(issue => issue.Severity == LocaleIssueSeverity.Error);
        public int WarningCount => Issues.Count(issue => issue.Severity == LocaleIssueSeverity.Warning);
    }

    /// <summary>
    /// Checks the language folders against the English reference: JSON syntax, keys defined twice, texts that are not texts, unknown
    /// and missing keys (an error for a released language, a warning for a draft), placeholders that differ from the English text's, and
    /// plural texts that lack a case their language needs. It is what a pull request with a translation has to pass.
    /// </summary>
    public static class LocaleValidator
    {
        private static readonly Regex KeyPattern = new Regex(@"^[a-z][a-z0-9]*(\.[A-Za-z0-9_\-]+)+$", RegexOptions.CultureInvariant);

        public static LocaleReport Validate(string root)
        {
            var report = new LocaleReport();
            var languages = LocaleLoader.List(root, report.Issues);
            report.LanguageCount = languages.Count;
            var referenceInfo = languages.Find(info => string.Equals(info.Code, TranslationManager.ReferenceLanguage, StringComparison.OrdinalIgnoreCase));
            if (referenceInfo == null)
            {
                report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, root ?? string.Empty, 0, null,
                    "the reference language " + TranslationManager.ReferenceLanguage + " is not listed in languages.json (or its folder has no locale.json)"));
                return report;
            }

            var reference = LocaleLoader.Load(referenceInfo);
            report.Issues.AddRange(reference.Issues);
            report.KeyCount = reference.Messages.Count;
            if (reference.Messages.Count == 0)
                report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, referenceInfo.Code, 0, null, "the reference language has no texts"));
            var referenceMessages = CheckTexts(reference, report, null);
            foreach (var note in reference.Notes.Keys)
                if (!reference.Messages.ContainsKey(note))
                    report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Warning, referenceInfo.Code, 0, note, "a note (\"@" + note + "\") belongs to no key"));

            foreach (var info in languages)
                CheckInfo(info, report);
            foreach (var info in languages)
            {
                if (ReferenceEquals(info, referenceInfo))
                    continue;
                var catalog = LocaleLoader.Load(info);
                report.Issues.AddRange(catalog.Issues);
                CheckTexts(catalog, report, referenceMessages);
                foreach (var key in catalog.Messages.Keys)
                    if (!reference.Messages.ContainsKey(key))
                    {
                        var origin = catalog.Origins[key];
                        report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, info.Code + "/" + origin.Key, origin.Value, key, "the key does not exist in the English reference"));
                    }
                var missing = reference.Messages.Keys.Where(key => !catalog.Messages.ContainsKey(key)).ToList();
                if (info.IsApproved)
                    foreach (var key in missing)
                        report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, info.Code, 0, key, "the translation is missing"));
                else if (missing.Count > 0)
                    // A draft is allowed to be unfinished: one line says how much, instead of a warning for every key.
                    report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Warning, info.Code, 0, null,
                        "draft language: " + missing.Count + " of " + reference.Messages.Count + " texts are not translated yet"));
                foreach (var note in catalog.Notes.Keys)
                    if (!catalog.Messages.ContainsKey(note))
                        report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Warning, info.Code, 0, note, "a note (\"@" + note + "\") belongs to no key"));
            }
            return report;
        }

        /// <summary>Checks what languages.json says about a language: its plural rules must be known, and so must its culture.</summary>
        private static void CheckInfo(LocaleInfo info, LocaleReport report)
        {
            if (info.PluralRules == null && PluralRules.RequiredCategories(info.Code).Count == 1 && !IsNoPluralLanguage(info.Code))
                report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Warning, LocaleLoader.OverviewFileName, 0, info.Code,
                    "no plural rules (plural.one, plural.few and so on) are given for " + info.Code + ", so every number counts as \"other\""));
            try
            {
                System.Globalization.CultureInfo.GetCultureInfo(info.Code);
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Warning, LocaleLoader.OverviewFileName, 0, info.Code,
                    "the system knows no culture " + info.Code + ", so numbers and dates are written in the neutral format"));
            }
        }

        // Languages whose nouns do not change with the number (the CLDR gives them "other" only).
        private static bool IsNoPluralLanguage(string code)
        {
            switch (PluralRules.LanguageOf(code))
            {
                case "ja": case "zh": case "ko": case "vi": case "th": case "id": case "ms": case "tr": case "hu": case "fa": case "ka":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Checks key names, the syntax of each text, its plural cases and, against the reference, its placeholders; returns the parsed texts.</summary>
        private static Dictionary<string, MessageFormat> CheckTexts(LocaleCatalog catalog, LocaleReport report, Dictionary<string, MessageFormat> reference)
        {
            var parsed = new Dictionary<string, MessageFormat>(StringComparer.Ordinal);
            foreach (var pair in catalog.Messages)
            {
                var key = pair.Key;
                var origin = catalog.Origins[key];
                var file = catalog.Info.Code + "/" + origin.Key;
                if (!KeyPattern.IsMatch(key))
                    report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, file, origin.Value, key,
                        "the key must be like menu.file: lower case area first, then parts of letters, digits, _ and -, joined with dots"));
                if (pair.Value.Trim().Length == 0)
                    report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, file, origin.Value, key, "the text is empty"));
                if (!MessageFormat.TryParse(pair.Value, out var message, out var error))
                {
                    report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, file, origin.Value, key, "the text is not valid: " + error));
                    continue;
                }
                parsed[key] = message;
                CheckPlurals(message, catalog.Info, file, origin.Value, key, report);
                if (reference != null && reference.TryGetValue(key, out var english))
                    ComparePlaceholders(english, message, file, origin.Value, key, report);
            }
            return parsed;
        }

        private static void CheckPlurals(MessageFormat message, LocaleInfo info, string file, int line, string key, LocaleReport report)
        {
            var required = info.RequiredPluralCategories;
            var language = info.Code;
            foreach (var argument in message.Arguments.Where(item => item.Kind == MessageArgumentKind.Plural))
            {
                var missing = required.Where(category => !argument.Cases.Contains(category)).ToList();
                if (missing.Count > 0)
                    report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, file, line, key,
                        "the plural {" + argument.Name + "} needs the cases " + string.Join(", ", missing) + " in " + language));
            }
        }

        private static void ComparePlaceholders(MessageFormat english, MessageFormat translation, string file, int line, string key, LocaleReport report)
        {
            var wanted = Signature(english);
            var given = Signature(translation);
            foreach (var item in given.Except(wanted))
                report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, file, line, key, "the placeholder {" + item + "} is not in the English text"));
            foreach (var item in wanted.Except(given))
                report.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, file, line, key, "the placeholder {" + item + "} of the English text is missing"));
        }

        /// <summary>The arguments of a text as "name:kind"; a plain {n} inside the cases of a plural of n does not count on its own.</summary>
        private static HashSet<string> Signature(MessageFormat message)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argument in message.Arguments)
            {
                if (argument.Kind == MessageArgumentKind.Plain && message.Arguments.Exists(other => other.Name == argument.Name && other.Kind != MessageArgumentKind.Plain))
                    continue;
                result.Add(argument.Name + ":" + argument.Kind);
            }
            return result;
        }
    }
}
