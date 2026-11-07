using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// A language is added by a few lines in locales/languages.json and a folder of texts, without a change in the code (ADR 068,
    /// addendum). These tests add Portuguese that way, and check the plural rules written in the file against the ones built in.
    /// </summary>
    public sealed class LocalizationLanguageListTests
    {
        private string root;

        [SetUp]
        public void CreateLocales()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-languages-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "en-US"));
            File.WriteAllText(Path.Combine(root, "en-US", "common.json"),
                "{ \"menu.file\": \"File\", \"objects.count\": \"{count, plural, one {# object} other {# objects}}\" }");
        }

        [TearDown]
        public void DeleteLocales()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void ANewLanguageNeedsOnlyAnEntryAndAFolder()
        {
            Overview("{ \"en-US.name\": \"English\", \"en-US.englishName\": \"English\", \"en-US.status\": \"approved\", \"en-US.plural.one\": \"i = 1 and v = 0\"," +
                     "  \"pt-BR.name\": \"Português (Brasil)\", \"pt-BR.englishName\": \"Portuguese (Brazil)\", \"pt-BR.status\": \"approved\"," +
                     "  \"pt-BR.system\": \"Portuguese\", \"pt-BR.plural.one\": \"i = 0..1\" }");
            Directory.CreateDirectory(Path.Combine(root, "pt-BR"));
            File.WriteAllText(Path.Combine(root, "pt-BR", "common.json"),
                "{ \"menu.file\": \"Arquivo\", \"objects.count\": \"{count, plural, one {# objeto} other {# objetos}}\" }");

            var manager = new TranslationManager(root);

            CollectionAssert.AreEqual(new[] { "en-US", "pt-BR" }, manager.Languages.Select(info => info.Code).ToList());
            Assert.AreEqual("Português (Brasil)", manager.Languages.Single(info => info.Code == "pt-BR").NativeName);
            Assert.IsTrue(manager.SetLanguage("pt-BR"));
            Assert.AreEqual("Arquivo", manager.T("menu.file"));
            // Portuguese counts 0 and 1 as singular, English only 1.
            Assert.AreEqual("0 objeto", manager.T("objects.count", ("count", 0)));
            Assert.AreEqual("1 objeto", manager.T("objects.count", ("count", 1)));
            Assert.AreEqual("2 objetos", manager.T("objects.count", ("count", 2)));
            Assert.AreEqual("pt-BR", manager.CodeForSystemLanguage("Portuguese"));
            Assert.IsNull(manager.CodeForSystemLanguage("Japanese"));
            Assert.AreEqual("pt-BR", LanguageChoice.Resolve(null, manager.CodeForSystemLanguage("Portuguese"), manager.Languages.Select(info => info.Code)));
            Assert.AreEqual(0, LocaleValidator.Validate(root).ErrorCount, string.Join("\n", LocaleValidator.Validate(root).Issues));
        }

        [Test]
        public void TheRulesInTheFileGoBeforeTheOnesBuiltIn()
        {
            // Arabic is not built in; its six cases come from the file.
            Overview("{ \"en-US.name\": \"English\", \"en-US.englishName\": \"English\", \"en-US.status\": \"approved\"," +
                     "  \"ar.name\": \"العربية\", \"ar.englishName\": \"Arabic\", \"ar.status\": \"approved\"," +
                     "  \"ar.plural.zero\": \"n = 0\", \"ar.plural.one\": \"n = 1\", \"ar.plural.two\": \"n = 2\"," +
                     "  \"ar.plural.few\": \"n % 100 = 3..10\", \"ar.plural.many\": \"n % 100 = 11..99\" }");
            Directory.CreateDirectory(Path.Combine(root, "ar"));
            File.WriteAllText(Path.Combine(root, "ar", "common.json"),
                "{ \"objects.count\": \"{count, plural, zero {Z} one {O} two {T} few {F} many {M} other {X}}\" }");

            var manager = new TranslationManager(root);

            Assert.IsTrue(manager.SetLanguage("ar"));
            var expected = new Dictionary<int, string> { { 0, "Z" }, { 1, "O" }, { 2, "T" }, { 5, "F" }, { 11, "M" }, { 99, "M" }, { 100, "X" }, { 103, "F" } };
            foreach (var pair in expected)
                Assert.AreEqual(pair.Value, manager.T("objects.count", ("count", pair.Key)), "count " + pair.Key);
            CollectionAssert.AreEqual(new[] { "zero", "one", "two", "few", "many", "other" },
                manager.Languages.Single(info => info.Code == "ar").RequiredPluralCategories.ToList());
        }

        [Test]
        public void TheRulesOfTheShippedLanguagesAgreeWithTheOnesBuiltIn()
        {
            var shipped = LocaleLoader.List(RepositoryLocales(), null);
            var builtIn = new[] { "en", "de", "es", "fr", "pl", "cs" };
            CollectionAssert.IsSubsetOf(new[] { "en-US", "de-DE", "es-ES", "fr-FR", "pl-PL", "cs-CZ" }, shipped.Select(info => info.Code).ToList());
            foreach (var info in shipped)
            {
                Assert.IsNotNull(info.PluralRules, info.Code + " gives no plural rules in languages.json");
                var language = PluralRules.LanguageOf(info.Code);
                if (Array.IndexOf(builtIn, language) < 0)
                    continue; // no built-in rules to compare with
                for (var value = 0; value <= 2000; value++)
                    Assert.AreEqual(PluralRules.Select(language, value, 0), info.PluralRules.Select(value, 0), info.Code + " " + value);
                foreach (var value in new[] { 0.5m, 1.0m, 1.5m, 2.0m, 2.5m, 5.25m, 11.1m, 21.0m })
                {
                    var digits = (decimal.GetBits(value)[3] >> 16) & 0xFF;
                    Assert.AreEqual(PluralRules.Select(language, value, (int)digits), info.PluralRules.Select(value, (int)digits), info.Code + " " + value);
                }
            }
        }

        [Test]
        public void PluralRulesAreReadInTheNotationOfTheCldr()
        {
            Assert.IsTrue(PluralRuleSet.TryParse(new[]
            {
                new KeyValuePair<string, string>("one", "i = 1 and v = 0 @integer 1"),
                new KeyValuePair<string, string>("few", "v = 0 and i % 10 = 2..4 and i % 100 != 12..14"),
                new KeyValuePair<string, string>("many", "e = 0 and i != 0 and i % 1000000 = 0 and v = 0 or e != 0..5")
            }, out var set, out var error), error);

            Assert.AreEqual("one", set.Select(1, 0));
            Assert.AreEqual("other", set.Select(1, 1), "1.0 is not singular");
            Assert.AreEqual("few", set.Select(22, 0));
            Assert.AreEqual("other", set.Select(12, 0));
            Assert.AreEqual("many", set.Select(2000000, 0));
            Assert.AreEqual("other", set.Select(0, 0));
            Assert.AreEqual("one", set.Select(-1, 0), "the sign does not count");
        }

        [TestCase("one", "i = ")]
        [TestCase("one", "x = 1")]
        [TestCase("one", "i = 1 and")]
        [TestCase("one", "i = 5..2")]
        [TestCase("one", "i % 0 = 1")]
        [TestCase("one", "")]
        [TestCase("other", "i = 1")]
        [TestCase("some", "i = 1")]
        public void BrokenRulesAreRefusedWithAReason(string category, string rule)
        {
            var ok = PluralRuleSet.TryParse(new[] { new KeyValuePair<string, string>(category, rule) }, out var set, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(set);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void FaultsInTheOverviewAreReportedAndLeaveOutOnlyTheBrokenLanguage()
        {
            Overview("{ \"en-US.name\": \"English\", \"en-US.englishName\": \"English\", \"en-US.status\": \"approved\"," +
                     "  \"xx-XX.name\": \"Xx\", \"xx-XX.englishName\": \"Xx\", \"xx-XX.status\": \"maybe\"," +
                     "  \"yy-YY.name\": \"Yy\", \"yy-YY.englishName\": \"Yy\", \"yy-YY.status\": \"draft\", \"yy-YY.colour\": \"red\"," +
                     "  \"zz-ZZ.name\": \"Zz\", \"zz-ZZ.englishName\": \"Zz\", \"zz-ZZ.status\": \"draft\", \"zz-ZZ.plural.one\": \"i =\"," +
                     "  \"ww-WW.name\": \"Ww\", \"ww-WW.status\": \"draft\"," +
                     "  \"nodot\": \"x\" }");
            var issues = new List<LocaleIssue>();

            var languages = LocaleLoader.List(root, issues);

            CollectionAssert.AreEqual(new[] { "en-US" }, languages.Select(info => info.Code).ToList());
            var text = string.Join("\n", issues);
            StringAssert.Contains("approved", text);
            StringAssert.Contains("yy-YY.colour", text);
            StringAssert.Contains("zz-ZZ.plural", text);
            StringAssert.Contains("ww-WW.englishName", text);
            StringAssert.Contains("nodot", text);
        }

        [Test]
        public void AFolderWithoutAnEntryAndAnEntryWithoutAFolderAreReported()
        {
            Overview("{ \"en-US.name\": \"English\", \"en-US.englishName\": \"English\", \"en-US.status\": \"approved\"," +
                     "  \"fr-FR.name\": \"Français\", \"fr-FR.englishName\": \"French\", \"fr-FR.status\": \"draft\"," +
                     "  \"de-DE.name\": \"Deutsch\", \"de-DE.englishName\": \"German\", \"de-DE.status\": \"approved\" }");
            Directory.CreateDirectory(Path.Combine(root, "pt-BR"));
            File.WriteAllText(Path.Combine(root, "pt-BR", "common.json"), "{ \"menu.file\": \"Arquivo\" }");
            var issues = new List<LocaleIssue>();

            var languages = LocaleLoader.List(root, issues);

            CollectionAssert.AreEqual(new[] { "de-DE", "en-US", "fr-FR" }, languages.Select(info => info.Code).ToList());
            Assert.IsTrue(issues.Any(issue => issue.Severity == LocaleIssueSeverity.Warning && issue.File == "pt-BR" && issue.Message.Contains("not listed")));
            Assert.IsFalse(issues.Any(issue => issue.Key == "fr-FR"), "a draft may still be without texts");
            Assert.IsTrue(issues.Any(issue => issue.Severity == LocaleIssueSeverity.Error && issue.Key == "de-DE"), "a released language needs its folder");
        }

        [Test]
        public void TheValidatorNeedsTheCasesTheRulesOfTheLanguageName()
        {
            Overview("{ \"en-US.name\": \"English\", \"en-US.englishName\": \"English\", \"en-US.status\": \"approved\", \"en-US.plural.one\": \"i = 1 and v = 0\"," +
                     "  \"pt-BR.name\": \"Português\", \"pt-BR.englishName\": \"Portuguese\", \"pt-BR.status\": \"approved\", \"pt-BR.plural.one\": \"i = 0..1\"," +
                     "  \"qq-QQ.name\": \"Qq\", \"qq-QQ.englishName\": \"Qq\", \"qq-QQ.status\": \"draft\" }");
            Directory.CreateDirectory(Path.Combine(root, "pt-BR"));
            File.WriteAllText(Path.Combine(root, "pt-BR", "common.json"),
                "{ \"menu.file\": \"Arquivo\", \"objects.count\": \"{count, plural, other {# objetos}}\" }");

            var report = LocaleValidator.Validate(root);

            Assert.IsTrue(report.Issues.Any(issue => issue.Severity == LocaleIssueSeverity.Error && issue.Key == "objects.count" && issue.Message.Contains("one")),
                string.Join("\n", report.Issues));
            Assert.IsTrue(report.Issues.Any(issue => issue.Severity == LocaleIssueSeverity.Warning && issue.Key == "qq-QQ" && issue.Message.Contains("plural")),
                "a language with no rules and none built in is pointed out");
        }

        [Test]
        public void AFolderWithItsOwnLocaleFileStillWorksWithoutAnOverview()
        {
            File.WriteAllText(Path.Combine(root, "en-US", "locale.json"),
                "{ \"code\": \"en-US\", \"name\": \"English\", \"englishName\": \"English\", \"status\": \"approved\" }");

            var manager = new TranslationManager(root);

            Assert.IsTrue(manager.ReferenceLoaded);
            Assert.AreEqual("1 object", manager.T("objects.count", ("count", 1)));
        }

        private void Overview(string json)
        {
            File.WriteAllText(Path.Combine(root, "languages.json"), json);
        }

        private static string RepositoryLocales()
        {
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 6 && folder != null; depth++, folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(Path.Combine(folder, "locales")) && Directory.Exists(Path.Combine(folder, "Assets", "_Project", "Scripts")))
                    return Path.Combine(folder, "locales");
            throw new DirectoryNotFoundException("locales was not found from " + Directory.GetCurrentDirectory());
        }
    }
}
