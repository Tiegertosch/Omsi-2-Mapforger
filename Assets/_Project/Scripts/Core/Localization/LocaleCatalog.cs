using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OmsiMapEditor.Core.Localization
{
    /// <summary>A language of the program: its code, its name in its own language and whether it was released.</summary>
    public sealed class LocaleInfo
    {
        public const string Approved = "approved";
        public const string Draft = "draft";

        public LocaleInfo(string code, string nativeName, string englishName, string status, string directory,
            PluralRuleSet pluralRules = null, IReadOnlyList<string> systemLanguages = null)
        {
            Code = code;
            NativeName = nativeName;
            EnglishName = englishName;
            Status = status;
            Directory = directory;
            PluralRules = pluralRules;
            SystemLanguages = systemLanguages ?? new string[0];
        }

        /// <summary>
        /// The plural rules written in <c>languages.json</c>; null when the language gives none, then the rules built into
        /// <see cref="Localization.PluralRules"/> apply for the languages it knows and "other" for all others.
        /// </summary>
        public PluralRuleSet PluralRules { get; }

        /// <summary>The names of the operating system's languages (Unity's <c>SystemLanguage</c>, such as <c>Portuguese</c>) this language answers to.</summary>
        public IReadOnlyList<string> SystemLanguages { get; }

        /// <summary>The plural cases a translation has to give, besides the exact =N cases: the ones with a rule, and always "other".</summary>
        public IReadOnlyList<string> RequiredPluralCategories => PluralRules != null ? PluralRules.Categories : Localization.PluralRules.RequiredCategories(Code);

        /// <summary>The language tag, such as <c>de-DE</c>.</summary>
        public string Code { get; }

        /// <summary>The name in the language itself, as the language list shows it: Deutsch, English, Polski.</summary>
        public string NativeName { get; }

        public string EnglishName { get; }

        /// <summary><c>approved</c> or <c>draft</c>; only an approved language can be chosen.</summary>
        public string Status { get; }

        public string Directory { get; }

        public bool IsApproved => string.Equals(Status, Approved, StringComparison.OrdinalIgnoreCase);

        public override string ToString()
        {
            return Code + " (" + NativeName + ", " + Status + ")";
        }
    }

    /// <summary>The texts of one language, read from the files of its folder.</summary>
    public sealed class LocaleCatalog
    {
        public LocaleCatalog(LocaleInfo info)
        {
            Info = info;
        }

        public LocaleInfo Info { get; }

        /// <summary>Key to text, every file of the folder together.</summary>
        public Dictionary<string, string> Messages { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The hints for translators, written as <c>"@key": "..."</c> beside a text; they are no texts.</summary>
        public Dictionary<string, string> Notes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The file each key was read from, without folder, and the line it stands on.</summary>
        public Dictionary<string, KeyValuePair<string, int>> Origins { get; } = new Dictionary<string, KeyValuePair<string, int>>(StringComparer.Ordinal);

        public List<LocaleIssue> Issues { get; } = new List<LocaleIssue>();

        public bool HasErrors => Issues.Exists(issue => issue.Severity == LocaleIssueSeverity.Error);
    }

    /// <summary>
    /// Reads the languages: <c>locales/languages.json</c> lists every language with its name, status, plural rules and system languages,
    /// and the folder <c>locales/&lt;code&gt;/</c> holds its texts (every <c>*.json</c> in it is one object of keys and texts). A
    /// language is added by a few lines in <c>languages.json</c> and a folder; no code changes. A folder with its own
    /// <c>locale.json</c> and no entry in the overview still works (the older way). Never throws: what cannot be read becomes an issue.
    /// </summary>
    public static class LocaleLoader
    {
        public const string OverviewFileName = "languages.json";

        /// <summary>The older description of one language, inside its folder; used only for a folder the overview does not list.</summary>
        public const string InfoFileName = "locale.json";

        private static readonly Regex CodePattern = new Regex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant);

        /// <summary>The languages found under <paramref name="root"/>, by code; a folder without a usable locale.json is reported and left out.</summary>
        public static List<LocaleInfo> List(string root, List<LocaleIssue> issues)
        {
            var result = new List<LocaleInfo>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, root ?? string.Empty, 0, null, "the folder with the language files does not exist"));
                return result;
            }
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var overview = Path.Combine(root, OverviewFileName);
            if (File.Exists(overview))
                foreach (var info in ReadOverview(overview, root, issues))
                {
                    listed.Add(info.Code);
                    result.Add(info);
                    // A draft may still be without texts (and so without a folder); a released language may not.
                    if (info.IsApproved && !Directory.Exists(info.Directory))
                        issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, OverviewFileName, 0, info.Code,
                            "the folder " + info.Code + " with the texts of this language does not exist"));
                }
            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
            }
            catch (IOException exception)
            {
                issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, root, 0, null, "the folder cannot be read: " + exception.Message));
                return result;
            }
            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                if (listed.Contains(name))
                    continue;
                if (File.Exists(Path.Combine(folder, InfoFileName)))
                {
                    var info = ReadInfo(folder, issues);
                    if (info != null)
                    {
                        listed.Add(info.Code);
                        result.Add(info);
                    }
                }
                else if (Directory.GetFiles(folder, "*.json").Length > 0)
                    issues?.Add(new LocaleIssue(LocaleIssueSeverity.Warning, name, 0, null,
                        "the folder holds texts but " + name + " is not listed in " + OverviewFileName + ", so the language is not offered"));
            }
            result.Sort((first, second) => string.Compare(first.Code, second.Code, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>
        /// Reads <c>languages.json</c>: flat entries <c>"&lt;code&gt;.name"</c>, <c>".englishName"</c>, <c>".status"</c> (approved or draft),
        /// optional <c>".system"</c> (names of operating system languages, separated by commas) and <c>".plural.&lt;case&gt;"</c> (a rule in CLDR
        /// notation for zero, one, two, few or many). A language with a fault is reported and left out.
        /// </summary>
        public static List<LocaleInfo> ReadOverview(string path, string root, List<LocaleIssue> issues)
        {
            var result = new List<LocaleInfo>();
            if (!TryRead(path, OverviewFileName, out var json, issues))
                return result;
            var order = new List<string>();
            var fields = new Dictionary<string, Dictionary<string, LocaleEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in json.Entries)
            {
                if (entry.Key.Length > 0 && entry.Key[0] == '@')
                    continue;
                var dot = entry.Key.IndexOf('.');
                if (dot <= 0 || dot == entry.Key.Length - 1)
                {
                    issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, OverviewFileName, entry.Line, entry.Key,
                        "the key must be like pt-BR.name: the language code, a dot and the setting"));
                    continue;
                }
                var code = entry.Key.Substring(0, dot);
                if (!fields.TryGetValue(code, out var own))
                {
                    own = new Dictionary<string, LocaleEntry>(StringComparer.Ordinal);
                    fields[code] = own;
                    order.Add(code);
                }
                own[entry.Key.Substring(dot + 1)] = entry;
            }
            foreach (var code in order)
            {
                var own = fields[code];
                var failed = false;
                void Fail(string field, int line, string message)
                {
                    issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, OverviewFileName, line, code + "." + field, message));
                    failed = true;
                }
                string Text(string field)
                {
                    if (own.TryGetValue(field, out var entry) && entry.Value.Trim().Length > 0)
                        return entry.Value.Trim();
                    Fail(field, 0, "the setting is missing or empty");
                    return string.Empty;
                }
                if (!CodePattern.IsMatch(code))
                    Fail("name", 0, "\"" + code + "\" is not a language code such as de-DE or pt-BR");
                var native = Text("name");
                var english = Text("englishName");
                var status = Text("status");
                if (status.Length > 0 && status != LocaleInfo.Approved && status != LocaleInfo.Draft)
                    Fail("status", own["status"].Line, "the status must be \"approved\" or \"draft\"");
                var rules = new List<KeyValuePair<string, string>>();
                var systems = new List<string>();
                var ruleLine = 0;
                foreach (var pair in own)
                {
                    if (pair.Key == "name" || pair.Key == "englishName" || pair.Key == "status")
                        continue;
                    if (pair.Key == "system")
                    {
                        foreach (var part in pair.Value.Value.Split(','))
                            if (part.Trim().Length > 0)
                                systems.Add(part.Trim());
                    }
                    else if (pair.Key.StartsWith("plural.", StringComparison.Ordinal))
                    {
                        rules.Add(new KeyValuePair<string, string>(pair.Key.Substring("plural.".Length), pair.Value.Value));
                        ruleLine = pair.Value.Line;
                    }
                    else
                        Fail(pair.Key, pair.Value.Line, "unknown setting (known: name, englishName, status, system, plural.zero/one/two/few/many)");
                }
                PluralRuleSet plural = null;
                if (rules.Count > 0 && !PluralRuleSet.TryParse(rules, out plural, out var reason))
                    Fail("plural", ruleLine, reason);
                if (!failed)
                    result.Add(new LocaleInfo(code, native, english, status, Path.Combine(root, code), plural, systems));
            }
            return result;
        }

        public static LocaleInfo ReadInfo(string folder, List<LocaleIssue> issues)
        {
            var path = Path.Combine(folder, InfoFileName);
            var name = Path.GetFileName(folder);
            if (!File.Exists(path))
            {
                issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, name + "/" + InfoFileName, 0, null, "the file is missing"));
                return null;
            }
            if (!TryRead(path, name + "/" + InfoFileName, out var json, issues))
                return null;
            string Get(string key)
            {
                var entry = json.Entries.Find(candidate => candidate.Key == key);
                return entry?.Value ?? string.Empty;
            }

            var code = Get("code");
            var native = Get("name");
            var status = Get("status");
            var failed = false;
            foreach (var required in new[] { "code", "name", "englishName", "status" })
                if (Get(required).Length == 0)
                {
                    issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, name + "/" + InfoFileName, 0, required, "the entry is missing or empty"));
                    failed = true;
                }
            if (code.Length > 0 && !string.Equals(code, name, StringComparison.OrdinalIgnoreCase))
            {
                issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, name + "/" + InfoFileName, 0, "code", "the code \"" + code + "\" does not match the folder name \"" + name + "\""));
                failed = true;
            }
            if (status.Length > 0 && status != LocaleInfo.Approved && status != LocaleInfo.Draft)
            {
                issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, name + "/" + InfoFileName, 0, "status", "the status must be \"approved\" or \"draft\""));
                failed = true;
            }
            return failed ? null : new LocaleInfo(code, native, Get("englishName"), status, folder);
        }

        /// <summary>Reads the text files of a language. The result always exists; look at its issues.</summary>
        public static LocaleCatalog Load(LocaleInfo info)
        {
            var catalog = new LocaleCatalog(info);
            if (!Directory.Exists(info.Directory))
                return catalog;
            string[] files;
            try
            {
                files = Directory.GetFiles(info.Directory, "*.json");
            }
            catch (IOException exception)
            {
                catalog.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, info.Code, 0, null, "the folder cannot be read: " + exception.Message));
                return catalog;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                var fileName = Path.GetFileName(path);
                if (string.Equals(fileName, InfoFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var display = info.Code + "/" + fileName;
                if (!TryRead(path, display, out var json, catalog.Issues))
                    continue;
                foreach (var entry in json.Entries)
                {
                    if (entry.Key.Length > 1 && entry.Key[0] == '@')
                    {
                        catalog.Notes[entry.Key.Substring(1)] = entry.Value;
                        continue;
                    }
                    if (catalog.Origins.TryGetValue(entry.Key, out var origin))
                    {
                        // A key twice in one file was reported by the reader; across files it is reported here.
                        if (!string.Equals(origin.Key, fileName, StringComparison.OrdinalIgnoreCase))
                            catalog.Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, display, entry.Line, entry.Key,
                                "the key is already defined in " + origin.Key + " (line " + origin.Value + ")"));
                        continue;
                    }
                    catalog.Messages[entry.Key] = entry.Value;
                    catalog.Origins[entry.Key] = new KeyValuePair<string, int>(fileName, entry.Line);
                }
            }
            return catalog;
        }

        private static bool TryRead(string path, string display, out LocaleJson json, List<LocaleIssue> issues)
        {
            json = null;
            string text;
            try
            {
                text = File.ReadAllText(path, new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, display, 0, null, "the file cannot be read: " + exception.Message));
                return false;
            }
            json = LocaleJson.Parse(text, display);
            issues?.AddRange(json.Issues);
            return true;
        }
    }
}
