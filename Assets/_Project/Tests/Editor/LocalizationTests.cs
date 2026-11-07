using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;

namespace OmsiMapEditor.Tests
{
    public sealed class LocalizationTests
    {
        private string root;

        [SetUp]
        public void CreateLocales()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-locales-" + Guid.NewGuid().ToString("N"));
            Language("en-US", "English", "approved");
            Write("en-US", "menus.json", "{ \"menu.file\": \"File\", \"menu.edit\": \"Edit\", \"@menu.edit\": \"The menu for changing the map\" }");
            Write("en-US", "common.json",
                "{ \"export.done\": \"Exported {count} objects to {name}.\"," +
                "  \"objects.count\": \"{count, plural, =0 {No objects} one {# object} other {# objects}}\"," +
                "  \"variant.note\": \"This variant's lines can't run.\" }");
            Language("de-DE", "Deutsch", "approved");
            Write("de-DE", "menus.json", "{ \"menu.file\": \"Datei\", \"menu.edit\": \"Bearbeiten\" }");
            Write("de-DE", "common.json",
                "{ \"export.done\": \"{count} Objekte nach {name} exportiert.\"," +
                "  \"objects.count\": \"{count, plural, =0 {Keine Objekte} one {# Objekt} other {# Objekte}}\" }");
            Language("pl-PL", "Polski", "draft");
        }

        [TearDown]
        public void DeleteLocales()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void GermanAndEnglishLoadFromTheirFolders()
        {
            var manager = new TranslationManager(root);

            Assert.IsTrue(manager.ReferenceLoaded);
            Assert.AreEqual("File", manager.T("menu.file"));
            Assert.IsTrue(manager.SetLanguage("de-DE"));
            Assert.AreEqual("de-DE", manager.Language);
            Assert.AreEqual("Datei", manager.T("menu.file"));
            Assert.IsTrue(manager.SetLanguage("en-US"));
            Assert.AreEqual("File", manager.T("menu.file"));
        }

        [Test]
        public void OnlyReleasedLanguagesCanBeChosenAndTheyShowTheirOwnNames()
        {
            var manager = new TranslationManager(root);

            CollectionAssert.AreEqual(new[] { "Deutsch", "English" }, manager.Languages.Select(info => info.NativeName).OrderBy(name => name).ToList());
            Assert.IsFalse(manager.SetLanguage("pl-PL"));
            Assert.AreEqual("en-US", manager.Language);
            Assert.AreEqual(3, new TranslationManager(root, true).Languages.Count);
        }

        [Test]
        public void ASwitchTellsListenersAndNeedsNoRestart()
        {
            var manager = new TranslationManager(root);
            var calls = 0;
            manager.LanguageChanged += () => calls++;
            var generation = manager.Generation;

            manager.SetLanguage("de-DE");
            manager.SetLanguage("de-DE");

            Assert.AreEqual(1, calls);
            Assert.AreEqual(generation + 1, manager.Generation);
        }

        [Test]
        public void ThePlaceholdersTakeTheirValuesAndKeepFileNamesAsTheyAre()
        {
            var manager = new TranslationManager(root);
            manager.SetLanguage("de-DE");

            var text = manager.T("export.done", ("count", 12), ("name", "Hafen_{1}'s.map"));

            Assert.AreEqual("12 Objekte nach Hafen_{1}'s.map exportiert.", text);
            manager.SetLanguage("en-US");
            Assert.AreEqual("Exported 12 objects to Hafen_{1}'s.map.", manager.T("export.done", ("count", 12), ("name", "Hafen_{1}'s.map")));
        }

        [Test]
        public void SingularAndPluralFollowTheLanguage()
        {
            var manager = new TranslationManager(root);
            manager.SetLanguage("de-DE");

            Assert.AreEqual("Keine Objekte", manager.T("objects.count", ("count", 0)));
            Assert.AreEqual("1 Objekt", manager.T("objects.count", ("count", 1)));
            Assert.AreEqual("2 Objekte", manager.T("objects.count", ("count", 2)));
            Assert.AreEqual("1.234 Objekte", manager.T("objects.count", ("count", 1234)));
            manager.SetLanguage("en-US");
            Assert.AreEqual("1 object", manager.T("objects.count", ("count", 1)));
            Assert.AreEqual("1,234 objects", manager.T("objects.count", ("count", 1234)));
        }

        [Test]
        public void AMissingTranslationShowsTheEnglishTextAndIsLoggedOnce()
        {
            var manager = new TranslationManager(root);
            var log = new List<string>();
            manager.Warn = log.Add;
            manager.SetLanguage("de-DE");

            Assert.AreEqual("This variant's lines can't run.", manager.T("variant.note"));
            Assert.AreEqual("This variant's lines can't run.", manager.T("variant.note"));

            Assert.AreEqual(1, log.Count(line => line.Contains("variant.note") && line.Contains("de-DE")));
        }

        [Test]
        public void AKeyWithoutAnyTextShowsAReadableSubstituteAndNeverThrows()
        {
            var manager = new TranslationManager(root);
            var log = new List<string>();
            manager.Warn = log.Add;

            Assert.AreEqual("[no.such.key]", manager.T("no.such.key"));
            Assert.AreEqual("[no.such.key]", manager.T("no.such.key", ("a", 1)));
            Assert.AreEqual(string.Empty, manager.T(null));
            Assert.AreEqual(1, log.Count);
            Assert.AreEqual("[menu.file]", Loc.T("menu.file"));
        }

        [Test]
        public void AMissingArgumentShowsItsNameAndABadTextShowsItselfInsteadOfThrowing()
        {
            Write("en-US", "more.json", "{ \"more.one\": \"Value {missing} here\", \"more.bad\": \"Broken {count, plural, one {x}\" }");
            var manager = new TranslationManager(root);
            manager.Warn = _ => { };

            Assert.AreEqual("Value {missing} here", manager.T("more.one"));
            Assert.DoesNotThrow(() => manager.T("more.bad", ("count", 1)));
            Assert.AreEqual("Broken {count, plural, one {x}", manager.T("more.bad", ("count", 1)));
        }

        [Test]
        public void DamagedLanguageFilesLoseOnlyWhatTheyCannotGive()
        {
            File.WriteAllText(Path.Combine(root, "de-DE", "common.json"), "{ \"export.done\": \"{count} Objekte\", \"objects.count\": ");
            var manager = new TranslationManager(root);

            Assert.IsTrue(manager.SetLanguage("de-DE"));
            Assert.AreEqual("Datei", manager.T("menu.file"));
            Assert.AreEqual("12 Objekte", manager.T("export.done", ("count", 12), ("name", "x")));
            Assert.AreEqual("1 object", manager.T("objects.count", ("count", 1)));
            Assert.IsTrue(manager.Issues.Any(issue => issue.Severity == LocaleIssueSeverity.Error && issue.File.Contains("common.json")));
        }

        [Test]
        public void AFolderWithOnlyGarbageIsRefusedAndTheLanguageStays()
        {
            File.WriteAllText(Path.Combine(root, "de-DE", "menus.json"), "not json at all");
            File.WriteAllText(Path.Combine(root, "de-DE", "common.json"), "[1, 2]");
            var manager = new TranslationManager(root);

            Assert.IsFalse(manager.SetLanguage("de-DE"));
            Assert.AreEqual("en-US", manager.Language);
            Assert.AreEqual("File", manager.T("menu.file"));
        }

        [Test]
        public void WithoutTheEnglishReferenceTextsShowAsKeysAndTheErrorIsReported()
        {
            Directory.Delete(Path.Combine(root, "en-US"), true);
            var manager = new TranslationManager(root);
            var errors = new List<string>();
            manager.Error = errors.Add;
            manager.ReportIssues();

            Assert.IsFalse(manager.ReferenceLoaded);
            Assert.AreEqual("[menu.file]", manager.T("menu.file"));
            Assert.IsTrue(errors.Count > 0);
        }

        [Test]
        public void TheSystemLanguageIsUsedOnTheFirstStartAndKept()
        {
            var store = new MemoryStore();
            var available = new[] { "en-US", "de-DE" };

            var first = LanguageChoice.Resolve(store, "de-AT", available);
            var later = LanguageChoice.Resolve(store, "en-GB", available);

            Assert.AreEqual("de-DE", first);
            Assert.AreEqual("de-DE", store.Stored);
            Assert.AreEqual("de-DE", later);
        }

        [Test]
        public void AnUnsupportedSystemLanguageStartsInEnglish()
        {
            var store = new MemoryStore();

            Assert.AreEqual("en-US", LanguageChoice.Resolve(store, "ja-JP", new[] { "en-US", "de-DE" }));
            Assert.AreEqual("en-US", store.Stored);
            Assert.AreEqual("en-US", LanguageChoice.Resolve(new MemoryStore(), null, new[] { "en-US", "de-DE" }));
        }

        [Test]
        public void AChoiceMadeByHandIsNotOverwrittenByTheSystemLanguage()
        {
            var store = new MemoryStore { Stored = "en-US" };

            Assert.AreEqual("en-US", LanguageChoice.Resolve(store, "de-DE", new[] { "en-US", "de-DE" }));
            Assert.AreEqual("en-US", store.Stored);
            // A kept language that is not there for now stays kept, and the program starts in the system's.
            var gone = new MemoryStore { Stored = "pl-PL" };
            Assert.AreEqual("de-DE", LanguageChoice.Resolve(gone, "de-DE", new[] { "en-US", "de-DE" }));
            Assert.AreEqual("pl-PL", gone.Stored);
        }

        [Test]
        public void CodesMatchInAnyWritingAndByTheirLanguage()
        {
            var available = new[] { "en-US", "de-DE", "pl-PL" };

            Assert.AreEqual("de-DE", LanguageChoice.Match("de_de", available));
            Assert.AreEqual("pl-PL", LanguageChoice.Match("pl", available));
            Assert.AreEqual("en-US", LanguageChoice.Match("en-GB", available));
            Assert.IsNull(LanguageChoice.Match("fr-FR", available));
            Assert.IsNull(LanguageChoice.Match("", available));
        }

        [Test]
        public void ApostrophesInEnglishTextsAreKeptAndBracesCanBeQuoted()
        {
            Assert.AreEqual("variant's can't", Format("variant's can't"));
            Assert.AreEqual("it's {not} an argument", Format("it's '{'not'}' an argument"));
            Assert.AreEqual("two '' stay one '", Format("two '''' stay one ''"));
            Assert.AreEqual("a ' b", Format("a '' b"));
            Assert.AreEqual("5 items' cost", Format("{n} items' cost", ("n", 5)));
        }

        [Test]
        public void NumbersAndDatesAreWrittenInTheCultureOfTheLanguage()
        {
            var german = CultureInfo.GetCultureInfo("de-DE");
            var english = CultureInfo.GetCultureInfo("en-US");
            var message = MessageFormat.Parse("{n, number} m, {p, number, percent}, {i, number, integer}, {d, number, 0.0}");
            var arguments = new[] { ("n", (object)1234.5), ("p", 0.5), ("i", 1234.6), ("d", 3.25) };

            Assert.AreEqual("1.234,5 m, 50 %, 1.235, 3,3", message.Format(german, "de-DE", arguments).Replace(" ", " "));
            // The runtime's own culture data decides the space before the percent sign (.NET Framework "50%", Unity's Mono "50 %").
            Assert.AreEqual("1,234.5 m, 50%, 1,235, 3.3", message.Format(english, "en-US", arguments).Replace(" %", "%"));
            var when = new DateTime(2026, 11, 7, 14, 5, 0);
            var dated = MessageFormat.Parse("{w, date, short} {w, time, short}");
            Assert.AreEqual("07.11.2026 14:05", dated.Format(german, "de-DE", new[] { ("w", (object)when) }));
            Assert.AreEqual("11/7/2026 2:05 PM", dated.Format(english, "en-US", new[] { ("w", (object)when) }));
            // The medium date is the date only; the time comes from its own placeholder.
            var medium = MessageFormat.Parse("{w, date, medium}");
            StringAssert.Contains("2026", medium.Format(german, "de-DE", new[] { ("w", (object)when) }));
            StringAssert.DoesNotContain("14", medium.Format(german, "de-DE", new[] { ("w", (object)when) }).Replace("2026", string.Empty).Replace("7.", string.Empty));
            Assert.AreEqual("Nov 7, 2026", medium.Format(english, "en-US", new[] { ("w", (object)when) }));
        }

        [Test]
        public void ThePluralCategoriesFollowTheRulesOfEachLanguage()
        {
            Assert.AreEqual("one", PluralRules.Select("en", 1, 0));
            Assert.AreEqual("other", PluralRules.Select("en", 1, 1));
            Assert.AreEqual("other", PluralRules.Select("de", 0, 0));
            Assert.AreEqual("one", PluralRules.Select("fr", 0, 0));
            Assert.AreEqual("one", PluralRules.Select("fr", 1, 0));
            Assert.AreEqual("other", PluralRules.Select("fr", 2, 0));
            Assert.AreEqual("one", PluralRules.Select("es", 1, 0));
            Assert.AreEqual("other", PluralRules.Select("es", 0, 0));
            Assert.AreEqual("one", PluralRules.Select("pl", 1, 0));
            Assert.AreEqual("few", PluralRules.Select("pl", 2, 0));
            Assert.AreEqual("few", PluralRules.Select("pl", 24, 0));
            Assert.AreEqual("many", PluralRules.Select("pl", 5, 0));
            Assert.AreEqual("many", PluralRules.Select("pl", 12, 0));
            Assert.AreEqual("many", PluralRules.Select("pl", 22 * 10 + 1, 0));
            Assert.AreEqual("many", PluralRules.Select("pl", 0, 0));
            Assert.AreEqual("other", PluralRules.Select("pl", 1.5m, 1));
            Assert.AreEqual("one", PluralRules.Select("cs", 1, 0));
            Assert.AreEqual("few", PluralRules.Select("cs", 3, 0));
            Assert.AreEqual("other", PluralRules.Select("cs", 5, 0));
            Assert.AreEqual("many", PluralRules.Select("cs", 1.5m, 1));
            Assert.AreEqual("other", PluralRules.Select("ja", 1, 0));
        }

        [Test]
        public void PluralsHandleOffsetsExactCasesAndNestedSelections()
        {
            var message = MessageFormat.Parse("{n, plural, offset:1 =0 {nobody} =1 {only you} one {you and # other} other {you and # others}}");
            var english = CultureInfo.GetCultureInfo("en-US");

            Assert.AreEqual("nobody", message.Format(english, "en-US", new[] { ("n", (object)0) }));
            Assert.AreEqual("only you", message.Format(english, "en-US", new[] { ("n", (object)1) }));
            Assert.AreEqual("you and 1 other", message.Format(english, "en-US", new[] { ("n", (object)2) }));
            Assert.AreEqual("you and 4 others", message.Format(english, "en-US", new[] { ("n", (object)5) }));
            Assert.AreEqual("a road in a tile", Format("a {what, select, road {road} other {thing}} in a {where, select, tile {tile} other {map}}", ("what", "road"), ("where", "tile")));
            Assert.AreEqual("a thing", Format("a {what, select, road {road} other {thing}}", ("what", "tree")));
        }

        [Test]
        public void MalformedTextsAreReportedWithTheirPlace()
        {
            foreach (var bad in new[] { "{", "}", "{n, plural, one {x}}", "{n, plural, few {x} other {y}", "{n, unknown}", "{ , }", "{n, select, a {x}}", "{n, plural, bogus {x} other {y}}" })
            {
                Assert.IsFalse(MessageFormat.TryParse(bad, out _, out var error), bad);
                StringAssert.Contains("character", error, bad);
            }
        }

        [Test]
        public void LoadingAndFormattingNeverTouchTheCultureOfTheThread()
        {
            var before = CultureInfo.CurrentCulture;
            var beforeUi = CultureInfo.CurrentUICulture;
            var manager = new TranslationManager(root);
            manager.SetLanguage("de-DE");
            manager.T("objects.count", ("count", 1234));

            Assert.AreSame(before, CultureInfo.CurrentCulture);
            Assert.AreSame(beforeUi, CultureInfo.CurrentUICulture);
        }

        [Test]
        public void TheJsonReaderSeesDuplicatesAndGivesTheLine()
        {
            var json = LocaleJson.Parse("{\n  \"a.b\": \"x\",\n  \"c.d\": \"y\",\n  \"a.b\": \"z\"\n}", "f.json");

            Assert.AreEqual(3, json.Entries.Count);
            var issue = json.Issues.Single();
            Assert.AreEqual(4, issue.Line);
            StringAssert.Contains("twice", issue.Message);
            Assert.IsTrue(json.HasErrors);
        }

        [Test]
        public void TheJsonReaderKnowsEscapesAndRefusesNestedStructures()
        {
            var good = LocaleJson.Parse("﻿{ \"a.b\": \"line\\nbreak \\\"q\\\" \\u00e4 \\\\\" }", "g.json");
            Assert.IsFalse(good.HasErrors);
            Assert.AreEqual("line\nbreak \"q\" ä \\", good.Entries.Single().Value);

            var nested = LocaleJson.Parse("{ \"a.b\": { \"c\": 1 }, \"d.e\": \"fine\" }", "n.json");
            Assert.IsTrue(nested.HasErrors);
            Assert.AreEqual("fine", nested.Entries.Single().Value);

            foreach (var broken in new[] { "", "{", "{ \"a\" \"b\" }", "{ \"a.b\": \"x\" ", "{ \"a.b\": \"x\", }", "{ \"a.b\": \"x\" } trailing", "{ \"a.b\": \"open }" })
                Assert.IsTrue(LocaleJson.Parse(broken, "b.json").HasErrors, broken);
        }

        [Test]
        public void TheValidatorAcceptsTheCompleteFixtureAndTreatsADraftAsAWarning()
        {
            // German lacks variant.note, which the fallback tests need; here it is complete.
            Write("de-DE", "notes.json", "{ \"variant.note\": \"Die Linien der Variante k\\u00f6nnen nicht fahren.\" }");
            var report = LocaleValidator.Validate(root);

            Assert.AreEqual(0, report.ErrorCount, string.Join("\n", report.Issues));
            Assert.AreEqual(3, report.LanguageCount);
            // The draft Polish has no texts yet: one warning that says how many are missing, no error.
            var draft = report.Issues.Single(issue => issue.File == "pl-PL");
            Assert.AreEqual(LocaleIssueSeverity.Warning, draft.Severity);
            StringAssert.Contains("5 of 5 texts", draft.Message);
        }

        [Test]
        public void TheValidatorFindsMissingUnknownAndMisplacedPlaceholders()
        {
            Write("de-DE", "common.json",
                "{ \"export.done\": \"{anzahl} Objekte nach {name} exportiert.\"," +
                "  \"objects.count\": \"{count, plural, one {# Objekt} other {# Objekte}}\"," +
                "  \"variant.note\": \"Die Linien der Variante k\\u00f6nnen nicht fahren.\"," +
                "  \"extra.key\": \"Gibt es nicht\" }");
            var report = LocaleValidator.Validate(root);
            var messages = report.Issues.Where(issue => issue.File.StartsWith("de-DE")).Select(issue => issue.Key + ": " + issue.Message).ToList();

            Assert.IsTrue(messages.Any(line => line.StartsWith("extra.key") && line.Contains("does not exist")), string.Join("\n", messages));
            Assert.IsTrue(messages.Any(line => line.StartsWith("export.done") && line.Contains("{anzahl:Plain}")), string.Join("\n", messages));
            Assert.IsTrue(messages.Any(line => line.StartsWith("export.done") && line.Contains("{count:Plain} of the English text is missing")), string.Join("\n", messages));
        }

        [Test]
        public void TheValidatorRequiresThePluralCasesOfTheLanguage()
        {
            Language("pl-PL", "Polski", "approved");
            Write("pl-PL", "common.json",
                "{ \"export.done\": \"Wyeksportowano {count} do {name}.\"," +
                "  \"objects.count\": \"{count, plural, one {# obiekt} other {# obiektu}}\"," +
                "  \"variant.note\": \"Linie wariantu nie mog\\u0105 jecha\\u0107.\" }");
            Write("pl-PL", "menus.json", "{ \"menu.file\": \"Plik\", \"menu.edit\": \"Edycja\" }");
            var report = LocaleValidator.Validate(root);

            Assert.IsTrue(report.Issues.Any(issue => issue.File.StartsWith("pl-PL") && issue.Key == "objects.count" && issue.Message.Contains("few, many")),
                string.Join("\n", report.Issues));
        }

        [Test]
        public void TheValidatorReportsBadKeysDuplicatesAcrossFilesAndBrokenFiles()
        {
            Write("en-US", "extra.json", "{ \"Bad Key\": \"x\", \"menu.file\": \"Again\", \"empty.text\": \"  \", \"bad.icu\": \"{n, plural, one {x}}\" }");
            Write("de-DE", "broken.json", "{ \"a.b\": \"x\", \"a.b\": \"y\" ");
            var report = LocaleValidator.Validate(root);
            var text = string.Join("\n", report.Issues);

            StringAssert.Contains("Bad Key", text);
            StringAssert.Contains("already defined in extra.json", text);
            StringAssert.Contains("the text is empty", text);
            StringAssert.Contains("has no \"other\" case", text);
            StringAssert.Contains("broken.json", text);
        }

        [Test]
        public void ALanguageFolderNeedsAUsableLocaleFile()
        {
            File.WriteAllText(Path.Combine(root, "pl-PL", "locale.json"), "{ \"code\": \"xx-XX\", \"name\": \"Polski\", \"englishName\": \"Polish\", \"status\": \"maybe\" }");
            var issues = new List<LocaleIssue>();

            var languages = LocaleLoader.List(root, issues);

            Assert.AreEqual(2, languages.Count);
            Assert.IsTrue(issues.Any(issue => issue.Message.Contains("does not match the folder name")));
            Assert.IsTrue(issues.Any(issue => issue.Message.Contains("approved")));
        }

        [Test]
        public void TheLanguageOfAMapNeverReachesItsFiles()
        {
            var german = SaveSampleMap("de-DE");
            var english = SaveSampleMap("en-US");

            CollectionAssert.AreEqual(english, german);
        }

        private byte[] SaveSampleMap(string language)
        {
            var manager = new TranslationManager(root);
            Assert.IsTrue(manager.SetLanguage(language));
            Loc.Manager = manager;
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "mapforge-i18nmap-" + Guid.NewGuid().ToString("N"));
                var maps = Path.Combine(folder, "OMSI 2", "maps", "Testmap");
                Directory.CreateDirectory(maps);
                File.WriteAllBytes(Path.Combine(maps, "global.cfg"),
                    OMSI.OmsiTextFile.Encode("[name]\r\nTestmap\r\n\r\n[NextIDCode]\r\n20\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OMSI.OmsiTextEncoding.Utf16LittleEndian));
                File.WriteAllBytes(Path.Combine(maps, "tile_0_0.map"), OMSI.OmsiTextFile.Encode("[version]\r\n14\r\n\r\n", OMSI.OmsiTextEncoding.Utf16LittleEndian));
                try
                {
                    var session = OMSI.OmsiMapSession.OpenProject(Path.Combine(maps, "global.cfg"), Path.Combine(folder, "Projects", "Test.omproj"));
                    var summary = session.Read();
                    session.PlaceObject(summary, summary.Tiles[0], "tree.sco", 10, 20, 0, 90);
                    return session.Workspace.ReadAllBytes(OMSI.OmsiRelativePath.Normalize(summary.Tiles[0].FileName));
                }
                finally
                {
                    Directory.Delete(folder, true);
                }
            }
            finally
            {
                Loc.Manager = null;
            }
        }

        private static string Format(string template, params (string Name, object Value)[] arguments)
        {
            return MessageFormat.Parse(template).Format(CultureInfo.GetCultureInfo("en-US"), "en-US", arguments);
        }

        private void Language(string code, string name, string status)
        {
            Write(code, "locale.json", "{ \"code\": \"" + code + "\", \"name\": \"" + name + "\", \"englishName\": \"" + name + "\", \"status\": \"" + status + "\" }");
        }

        private void Write(string code, string file, string text)
        {
            var folder = Path.Combine(root, code);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, file), text);
        }

        private sealed class MemoryStore : ILanguageStore
        {
            public string Stored { get; set; }

            public void Store(string code)
            {
                Stored = code;
            }
        }
    }
}
