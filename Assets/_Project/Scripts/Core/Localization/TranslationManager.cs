using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.Core.Localization
{
    /// <summary>
    /// The texts of the program's interface in the language chosen: <see cref="T(string)"/> gives the text of a key, with arguments
    /// for <see cref="MessageFormat"/> placeholders. A key missing in the language falls back to the English reference (en-US), and a key
    /// missing there shows as <c>[key]</c>; a damaged language file loses only what it could not give. None of it ever throws. The
    /// manager does not touch the thread's culture: numbers and dates are written with the culture of the language. Every text is
    /// compiled when its language loads and never changes afterwards, so <see cref="T(string)"/> may be called from any thread;
    /// changing the language and subscribing to its change belong to the main thread.
    /// </summary>
    public sealed class TranslationManager
    {
        public const string ReferenceLanguage = "en-US";

        private sealed class Entry
        {
            public string Text;
            public MessageFormat Compiled;
            public bool Failed;
        }

        private sealed class Loaded
        {
            public LocaleInfo Info;
            public CultureInfo Culture;
            public Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        private readonly bool includeDrafts;
        private readonly List<LocaleInfo> found;
        private readonly Dictionary<string, Loaded> cache = new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        private readonly object reportedLock = new object();
        private Loaded reference;
        private Loaded current;

        /// <param name="root">The folder with one folder per language.</param>
        /// <param name="includeDrafts">Offers languages not yet released too, as a developer wants.</param>
        public TranslationManager(string root, bool includeDrafts = false)
        {
            Root = root;
            this.includeDrafts = includeDrafts;
            found = LocaleLoader.List(root, Issues);
            var info = found.Find(candidate => string.Equals(candidate.Code, ReferenceLanguage, StringComparison.OrdinalIgnoreCase));
            if (info != null)
                reference = Load(info);
            if (reference == null || reference.Entries.Count == 0)
            {
                Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, ReferenceLanguage, 0, null,
                    "the English reference language could not be loaded, so texts show as their keys"));
                reference = null;
            }
            current = reference;
        }

        public string Root { get; }

        /// <summary>Findings while reading the language files, errors and warnings.</summary>
        public List<LocaleIssue> Issues { get; } = new List<LocaleIssue>();

        /// <summary>Called with a line about a missing key or a damaged file; the program writes them to its log in development.</summary>
        public Action<string> Warn { get; set; }

        /// <summary>Called with a line about something that makes the interface unusable, such as a missing reference language.</summary>
        public Action<string> Error { get; set; }

        /// <summary>The languages that can be chosen: the released ones, with drafts only when the manager was made to offer them.</summary>
        public List<LocaleInfo> Languages => found.FindAll(info => includeDrafts || info.IsApproved);

        public bool ReferenceLoaded => reference != null;

        /// <summary>The code of the language in use; empty when no language could be loaded.</summary>
        public string Language => current?.Info.Code ?? string.Empty;

        /// <summary>The culture of the language in use, for numbers and dates; never the thread's culture.</summary>
        public CultureInfo Culture => current?.Culture ?? CultureInfo.InvariantCulture;

        /// <summary>Counts the language changes, so a piece of the interface can tell that its text was set under another language.</summary>
        public int Generation { get; private set; }

        public event Action LanguageChanged;

        /// <summary>Tells the problems found so far, so they can be logged once the log is there.</summary>
        public void ReportIssues()
        {
            foreach (var issue in Issues)
            {
                var line = "Language files: " + issue;
                if (issue.Severity == LocaleIssueSeverity.Error)
                    Error?.Invoke(line);
                else
                    Warn?.Invoke(line);
            }
        }

        /// <summary>
        /// Switches to a language that can be chosen. False, and nothing changes, when there is none of that code or its files give
        /// no text at all. The change event comes after the switch.
        /// </summary>
        public bool SetLanguage(string code)
        {
            var info = Languages.Find(candidate => string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));
            if (info == null)
                return false;
            Loaded loaded;
            if (string.Equals(info.Code, ReferenceLanguage, StringComparison.OrdinalIgnoreCase) && reference != null)
                loaded = reference;
            else
            {
                loaded = Load(info);
                if (loaded.Entries.Count == 0)
                    return false;
            }
            if (ReferenceEquals(loaded, current))
                return true;
            current = loaded;
            Generation++;
            LanguageChanged?.Invoke();
            return true;
        }

        public bool Has(string key)
        {
            return current != null && current.Entries.ContainsKey(key) || reference != null && reference.Entries.ContainsKey(key);
        }

        /// <summary>The text of the key, or the English text, or <c>[key]</c>.</summary>
        public string T(string key)
        {
            return Resolve(key, null);
        }

        /// <summary>The text of the key with the named arguments put in: <c>T("export.done", ("count", 12))</c>.</summary>
        public string T(string key, params (string Name, object Value)[] arguments)
        {
            return Resolve(key, arguments);
        }

        private string Resolve(string key, IReadOnlyList<(string Name, object Value)> arguments)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;
            // One reading of each, so a language change on the main thread cannot split a lookup.
            var chosen = current;
            var english = reference;
            Loaded source = null;
            Entry entry = null;
            if (chosen != null && chosen.Entries.TryGetValue(key, out entry))
                source = chosen;
            else if (english != null && english.Entries.TryGetValue(key, out entry))
            {
                source = english;
                if (chosen != null && !ReferenceEquals(chosen, english))
                    ReportOnce(chosen.Info.Code + "|" + key, "Missing translation [" + key + "] in " + chosen.Info.Code + "; the English text is shown.");
            }
            if (entry == null)
            {
                ReportOnce("|" + key, "Missing translation key [" + key + "].");
                return "[" + key + "]";
            }
            if (entry.Failed)
                return entry.Text;
            if (entry.Compiled.IsPlain)
                return entry.Compiled.PlainText;
            return entry.Compiled.Format(source.Culture, source.Info.Code, arguments);
        }

        private void ReportOnce(string id, string message)
        {
            bool first;
            lock (reportedLock)
                first = reported.Add(id);
            if (first)
                Warn?.Invoke(message);
        }

        private Loaded Load(LocaleInfo info)
        {
            if (cache.TryGetValue(info.Code, out var known))
                return known;
            var catalog = LocaleLoader.Load(info);
            Issues.AddRange(catalog.Issues);
            foreach (var issue in catalog.Issues)
                Warn?.Invoke("Language files: " + issue);
            var loaded = new Loaded { Info = info, Culture = CultureOf(info.Code) };
            foreach (var pair in catalog.Messages)
            {
                var entry = new Entry { Text = pair.Value };
                if (MessageFormat.TryParse(pair.Value, out var compiled, out var error))
                    entry.Compiled = compiled;
                else
                {
                    // A text that does not parse is shown as it is; the validator and the log say why.
                    entry.Failed = true;
                    var origin = catalog.Origins[pair.Key];
                    var issue = new LocaleIssue(LocaleIssueSeverity.Warning, info.Code + "/" + origin.Key, origin.Value, pair.Key, "the text is not valid: " + error);
                    Issues.Add(issue);
                    Warn?.Invoke("Language files: " + issue);
                }
                loaded.Entries[pair.Key] = entry;
            }
            cache[info.Code] = loaded;
            return loaded;
        }

        private static CultureInfo CultureOf(string code)
        {
            try
            {
                return CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
