using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmsiMapEditor.Core.Localization
{
    /// <summary>A language of the program: its code, its name in its own language and whether it was released.</summary>
    public sealed class LocaleInfo
    {
        public const string Approved = "approved";
        public const string Draft = "draft";

        public LocaleInfo(string code, string nativeName, string englishName, string status, string directory)
        {
            Code = code;
            NativeName = nativeName;
            EnglishName = englishName;
            Status = status;
            Directory = directory;
        }

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
    /// Reads the language folders: <c>locales/&lt;code&gt;/locale.json</c> says what the language is, every other <c>*.json</c> beside
    /// it holds texts (one object of keys and texts). Never throws: what cannot be read becomes an issue.
    /// </summary>
    public static class LocaleLoader
    {
        public const string InfoFileName = "locale.json";

        /// <summary>The languages found under <paramref name="root"/>, by code; a folder without a usable locale.json is reported and left out.</summary>
        public static List<LocaleInfo> List(string root, List<LocaleIssue> issues)
        {
            var result = new List<LocaleInfo>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                issues?.Add(new LocaleIssue(LocaleIssueSeverity.Error, root ?? string.Empty, 0, null, "the folder with the language files does not exist"));
                return result;
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
                var info = ReadInfo(folder, issues);
                if (info != null)
                    result.Add(info);
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
