using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// A tooltip with a title is joined by <see cref="LocBindings.CardTooltip"/> and split again by the tooltip card
    /// (ADR 098, M3); and the tooltip texts of every language can be joined and split that way.
    /// </summary>
    public sealed class TooltipTextTests
    {
        private static readonly string[] LanguageCodes = { "cs-CZ", "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "pl-PL", "pt-PT" };

        internal static string LocalesFolder()
        {
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 8 && folder != null; depth++, folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(Path.Combine(folder, "locales", "en-US")))
                    return Path.Combine(folder, "locales");
            throw new DirectoryNotFoundException("locales was not found from " + Directory.GetCurrentDirectory());
        }

        [Test]
        public void ATitleAndATextComeBackAsTheyWentIn()
        {
            var joined = LocBindings.CardTooltip("Straight road", "Every click ends a straight piece.");

            Assert.That(EditorShellBootstrap.SplitCardTooltip(joined, out var title, out var text), Is.True);
            Assert.That(title, Is.EqualTo("Straight road"));
            Assert.That(text, Is.EqualTo("Every click ends a straight piece."));
        }

        [Test]
        public void AnEmptyTextStillSplitsAndGivesTheTitleAlone()
        {
            var joined = LocBindings.CardTooltip("Tiles", string.Empty);

            Assert.That(EditorShellBootstrap.SplitCardTooltip(joined, out var title, out var text), Is.True);
            Assert.That(title, Is.EqualTo("Tiles"));
            Assert.That(text, Is.EqualTo(string.Empty));
        }

        [Test]
        public void LineBreaksAndColonsInTheTextSurvive()
        {
            var original = "First line: with a colon.\nSecond line\r\nThird (Q): line.";
            var joined = LocBindings.CardTooltip("Title: with colon", original);

            Assert.That(EditorShellBootstrap.SplitCardTooltip(joined, out var title, out var text), Is.True);
            Assert.That(title, Is.EqualTo("Title: with colon"));
            Assert.That(text, Is.EqualTo(original));
        }

        [Test]
        public void TheSeparatorInsideTheTextStaysInTheTextAsOnlyTheFirstOneSplits()
        {
            var original = "before" + LocBindings.TooltipSeparator + "after";
            var joined = LocBindings.CardTooltip("Title", original);

            Assert.That(EditorShellBootstrap.SplitCardTooltip(joined, out var title, out var text), Is.True);
            Assert.That(title, Is.EqualTo("Title"));
            Assert.That(text, Is.EqualTo(original));
        }

        [Test]
        public void AnEmptyTitleGivesThePlainTextWhichIsNotSplit()
        {
            var joined = LocBindings.CardTooltip(string.Empty, "Only a text: no title.");

            Assert.That(joined, Is.EqualTo("Only a text: no title."));
            Assert.That(EditorShellBootstrap.SplitCardTooltip(joined, out var title, out var text), Is.False);
            Assert.That(title, Is.EqualTo(string.Empty));
            Assert.That(text, Is.EqualTo(string.Empty));
        }

        [Test]
        public void APlainTooltipIsNotSplit()
        {
            Assert.That(EditorShellBootstrap.SplitCardTooltip("Drag (Q): Click objects and drag them.", out _, out _), Is.False);
            Assert.That(EditorShellBootstrap.SplitCardTooltip(string.Empty, out _, out _), Is.False);
        }

        [Test]
        public void TheSeparatorIsNoCharacterOfOrdinaryText()
        {
            Assert.That(char.IsControl(LocBindings.TooltipSeparator), Is.True);
            Assert.That(char.IsWhiteSpace(LocBindings.TooltipSeparator), Is.False);
        }

        [TestCaseSource(nameof(LanguageCodes))]
        public void EveryTooltipTextOfALanguageIsFilledFreeOfTheSeparatorAndSurvivesTheRoundTrip(string code)
        {
            var messages = ContextMenuEntriesTests.LoadMessages(code);
            var tooltips = messages.Where(pair => IsTooltipKey(pair.Key)).ToList();
            var problems = new List<string>();

            Assert.That(tooltips.Count, Is.GreaterThan(100), code + " has too few tooltip texts; the keys were not recognised");
            foreach (var pair in tooltips)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    problems.Add(pair.Key + " is empty");
                    continue;
                }
                if (pair.Value.IndexOf(LocBindings.TooltipSeparator) >= 0)
                {
                    problems.Add(pair.Key + " contains the separator of the card tooltip");
                    continue;
                }
                var joined = LocBindings.CardTooltip("Title", pair.Value);
                if (!EditorShellBootstrap.SplitCardTooltip(joined, out var title, out var text) || title != "Title" || text != pair.Value)
                    problems.Add(pair.Key + " does not survive joining and splitting");
                if (EditorShellBootstrap.SplitCardTooltip(pair.Value, out _, out _))
                    problems.Add(pair.Key + " is split although it is a plain tooltip");
            }

            Assert.That(problems, Is.Empty, code + ": " + string.Join("; ", problems));
        }

        [TestCaseSource(nameof(LanguageCodes))]
        public void EveryTooltipTitleOfALanguageIsFilledAndFreeOfTheSeparator(string code)
        {
            var messages = ContextMenuEntriesTests.LoadMessages(code);
            // A titled tooltip names its title by the key without the ".tooltip" of the text: "roadtool.shape.straight" for "roadtool.shape.straight.tooltip".
            var titles = messages.Keys.Where(key => key.EndsWith(".tooltip", StringComparison.Ordinal))
                .Select(key => key.Substring(0, key.Length - ".tooltip".Length)).Where(messages.ContainsKey).ToList();
            var problems = new List<string>();

            Assert.That(titles.Count, Is.GreaterThan(50), code + " has too few titles; the keys were not recognised");
            foreach (var key in titles)
            {
                if (string.IsNullOrWhiteSpace(messages[key]))
                    problems.Add(key + " is empty");
                else if (messages[key].IndexOf(LocBindings.TooltipSeparator) >= 0)
                    problems.Add(key + " contains the separator of the card tooltip");
                else
                {
                    var joined = LocBindings.CardTooltip(messages[key], messages[key + ".tooltip"]);
                    if (!EditorShellBootstrap.SplitCardTooltip(joined, out var title, out var text) || title != messages[key] || text != messages[key + ".tooltip"])
                        problems.Add(key + " does not survive joining and splitting with its text");
                }
            }

            Assert.That(problems, Is.Empty, code + ": " + string.Join("; ", problems));
        }

        [Test]
        public void ANameWithAKeyAndATextGivesTitleKeyCapAndText()
        {
            var parts = EditorShellBootstrap.SplitTooltip("Drag (Q): Click objects and drag them.");

            Assert.That(parts.HasTitle, Is.True);
            Assert.That(parts.Title, Is.EqualTo("Drag"));
            Assert.That(parts.HasKeyCap, Is.True);
            Assert.That(parts.KeyCap, Is.EqualTo("Q"));
            Assert.That(parts.Text, Is.EqualTo("Click objects and drag them."));
            Assert.That(parts.ShowsText, Is.True);
        }

        [Test]
        public void ANameWithoutAKeyGivesNoKeyCap()
        {
            var parts = EditorShellBootstrap.SplitTooltip("Tiles: the list of tiles.");

            Assert.That(parts.HasTitle, Is.True);
            Assert.That(parts.Title, Is.EqualTo("Tiles"));
            Assert.That(parts.HasKeyCap, Is.False);
            Assert.That(parts.KeyCap, Is.EqualTo(string.Empty));
            Assert.That(parts.Text, Is.EqualTo("the list of tiles."));
        }

        [Test]
        public void KeyCapsOfSeveralCharactersUpToTwelveAreRecognised()
        {
            var combination = EditorShellBootstrap.SplitTooltip("Duplicate (Ctrl+D): Copies the object.");
            var word = EditorShellBootstrap.SplitTooltip("Delete (Del): Removes the object.");
            var tooLong = EditorShellBootstrap.SplitTooltip("Name (Ctrl+Shift+Alt): Text.");

            Assert.That(combination.Title, Is.EqualTo("Duplicate"));
            Assert.That(combination.KeyCap, Is.EqualTo("Ctrl+D"));
            Assert.That(word.Title, Is.EqualTo("Delete"));
            Assert.That(word.KeyCap, Is.EqualTo("Del"));
            // A key longer than twelve characters is no key cap; the regex then keeps it in the head, which is still a name of two words.
            Assert.That(tooLong.HasTitle, Is.True);
            Assert.That(tooLong.Title, Is.EqualTo("Name (Ctrl+Shift+Alt)"));
            Assert.That(tooLong.HasKeyCap, Is.False);
        }

        [Test]
        public void TheFrenchSpaceBeforeTheColonIsAllowed()
        {
            var withKey = EditorShellBootstrap.SplitTooltip("Dessiner (Q) : Cliquer sur les objets.");
            var withoutKey = EditorShellBootstrap.SplitTooltip("Tuiles : la liste des tuiles.");

            Assert.That(withKey.Title, Is.EqualTo("Dessiner"));
            Assert.That(withKey.KeyCap, Is.EqualTo("Q"));
            Assert.That(withKey.Text, Is.EqualTo("Cliquer sur les objets."));
            Assert.That(withoutKey.Title, Is.EqualTo("Tuiles"));
            Assert.That(withoutKey.HasKeyCap, Is.False);
        }

        [Test]
        public void AHeadOfThreeWordsIsAName()
        {
            var parts = EditorShellBootstrap.SplitTooltip("Straight road piece: Every click ends it.");

            Assert.That(parts.HasTitle, Is.True);
            Assert.That(parts.Title, Is.EqualTo("Straight road piece"));
        }

        [Test]
        public void AHeadOfMoreThanThreeWordsIsTheStartOfASentenceAndTheWholeTextShows()
        {
            const string tooltip = "Click the left button: the object moves.";
            var parts = EditorShellBootstrap.SplitTooltip(tooltip);

            Assert.That(parts.HasTitle, Is.False);
            Assert.That(parts.HasKeyCap, Is.False);
            Assert.That(parts.Text, Is.EqualTo(tooltip));
            Assert.That(parts.ShowsText, Is.True);
        }

        [Test]
        public void ATextWithoutAColonShowsAsItIs()
        {
            var parts = EditorShellBootstrap.SplitTooltip("Shows the tiles of the map.");

            Assert.That(parts.HasTitle, Is.False);
            Assert.That(parts.Text, Is.EqualTo("Shows the tiles of the map."));
        }

        [Test]
        public void AColonAfterTheFirstSentenceOrWithoutASpaceAfterItGivesNoTitle()
        {
            const string laterColon = "Click here. Then: drag.";
            const string noSpace = "Tiles:the list";

            Assert.That(EditorShellBootstrap.SplitTooltip(laterColon).HasTitle, Is.False);
            Assert.That(EditorShellBootstrap.SplitTooltip(laterColon).Text, Is.EqualTo(laterColon));
            Assert.That(EditorShellBootstrap.SplitTooltip(noSpace).HasTitle, Is.False);
            Assert.That(EditorShellBootstrap.SplitTooltip(noSpace).Text, Is.EqualTo(noSpace));
        }

        [Test]
        public void ACardTooltipKeepsItsTitleOfAnyLengthWithoutAKeyCap()
        {
            var parts = EditorShellBootstrap.SplitTooltip(LocBindings.CardTooltip("A title of more than three words (Q)", "Text: with colon."));

            Assert.That(parts.HasTitle, Is.True);
            Assert.That(parts.Title, Is.EqualTo("A title of more than three words (Q)"));
            Assert.That(parts.HasKeyCap, Is.False);
            Assert.That(parts.KeyCap, Is.EqualTo(string.Empty));
            Assert.That(parts.Text, Is.EqualTo("Text: with colon."));
            Assert.That(parts.ShowsText, Is.True);
        }

        [Test]
        public void ACardTooltipWithoutATextHidesTheTextElement()
        {
            var parts = EditorShellBootstrap.SplitTooltip(LocBindings.CardTooltip("Tiles", string.Empty));

            Assert.That(parts.HasTitle, Is.True);
            Assert.That(parts.Title, Is.EqualTo("Tiles"));
            Assert.That(parts.Text, Is.EqualTo(string.Empty));
            Assert.That(parts.ShowsText, Is.False);
        }

        // A translation that splits otherwise than the English text loses or gains the heading of the card, or changes the key cap: a
        // head of three words or fewer before a colon becomes a title, so a translated sentence start must keep more words or no colon.
        [TestCaseSource(nameof(LanguageCodes))]
        public void EveryPlainTooltipOfALanguageSplitsLikeTheEnglishOne(string code)
        {
            var english = ContextMenuEntriesTests.LoadMessages("en-US");
            var messages = ContextMenuEntriesTests.LoadMessages(code);
            var problems = new List<string>();
            var checkedCount = 0;

            foreach (var pair in english.Where(pair => IsTooltipKey(pair.Key) && IsPlainTooltip(english, pair.Key)))
            {
                if (!messages.TryGetValue(pair.Key, out var translated) || string.IsNullOrWhiteSpace(translated))
                    continue;
                checkedCount++;
                var expected = EditorShellBootstrap.SplitTooltip(pair.Value);
                var actual = EditorShellBootstrap.SplitTooltip(translated);
                var differences = new List<string>();
                if (expected.HasTitle != actual.HasTitle)
                    differences.Add("titled " + expected.HasTitle + " -> " + actual.HasTitle);
                if (expected.HasKeyCap != actual.HasKeyCap || expected.KeyCap != actual.KeyCap)
                    differences.Add("key cap '" + (expected.HasKeyCap ? expected.KeyCap : "none") + "' -> '" + (actual.HasKeyCap ? actual.KeyCap : "none") + "'");
                if (actual.Text.Length == 0)
                    differences.Add("empty text");
                if (differences.Count > 0)
                    problems.Add(code + "|" + pair.Key + ": " + string.Join(", ", differences));
            }

            Assert.That(checkedCount, Is.GreaterThan(20), code + " has too few plain tooltips; the keys were not recognised");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // A tooltip with a title sibling is joined as a card tooltip and never goes through the heading regex.
        private static bool IsPlainTooltip(IReadOnlyDictionary<string, string> messages, string key)
        {
            return !(key.EndsWith(".tooltip", StringComparison.Ordinal) && messages.ContainsKey(key.Substring(0, key.Length - ".tooltip".Length)));
        }

        [Test]
        public void AllEightLanguagesAreCheckedAndListed()
        {
            var root = LocalesFolder();
            foreach (var code in LanguageCodes)
                Assert.That(Directory.Exists(Path.Combine(root, code)), Is.True, code);
        }

        private static bool IsTooltipKey(string key)
        {
            var last = key.Substring(key.LastIndexOf('.') + 1);
            return last == "tooltip" || last.StartsWith("tooltip_", StringComparison.Ordinal);
        }
    }
}
