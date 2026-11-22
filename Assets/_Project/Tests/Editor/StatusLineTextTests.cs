using System.IO;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The status bar says states and results in one short sentence, in every language; what to click is the hint of the tool, and the
    /// mouse and keys of the 3D view are in the Help menu (ADR 098, M6 step 5).
    /// </summary>
    public sealed class StatusLineTextTests
    {
        private static readonly string[] LanguageCodes = { "cs-CZ", "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "pl-PL", "pt-PT" };

        /// <summary>The messages of a tool opening, a tool choosing its target and a road starting, with arguments as the code passes them.</summary>
        private static readonly (string Key, (string Name, object Value)[] Arguments)[] StateTexts =
        {
            ("terrain.status.start", new (string, object)[0]),
            ("rows.status.start", new (string, object)[0]),
            ("rows.status.selected", new (string, object)[] { ("path", "Sceneryobjects\\Trees\\Birch.sco"), ("id", 12) }),
            ("roadbuild.status.start", new (string, object)[0]),
            ("roadbuild.status.start_variant", new (string, object)[] { ("title", "Winter 1992") }),
            ("roadbuild.started", new (string, object)[0]),
            ("roadbuild.started.continue", new (string, object)[] { ("id", 4229) }),
            ("roadbuild.started.port", new (string, object)[] { ("port", "connection point 2 of #17 Crossroads") }),
            ("roadbuilder.status.start", new (string, object)[0]),
            ("roadbuilder.status.pick_row", new (string, object)[] { ("lane", 2) }),
            ("roadbuilder.status.edit_foreign", new (string, object)[] { ("name", "Road 2 lanes") }),
            ("traffic.status.start", new (string, object)[0]),
            ("lanes.status.start", new (string, object)[0]),
            ("lanes.status.target", new (string, object)[] { ("id", 472) }),
            ("markings.status.start", new (string, object)[0]),
            ("markings.status.target", new (string, object)[] { ("id", 472) }),
            ("markings.status.line_selected", new (string, object)[] { ("style", "dashed") }),
            ("signals.status.start", new (string, object)[0]),
            ("line.status.start", new (string, object)[0]),
            ("view.status.3d", new (string, object)[0]),
        };

        private TranslationManager previous;

        [SetUp]
        public void RememberLanguage()
        {
            previous = Loc.Manager;
        }

        [TearDown]
        public void RestoreLanguage()
        {
            Loc.Manager = previous;
        }

        private static TranslationManager Manager(string code)
        {
            var manager = new TranslationManager(TooltipTextTests.LocalesFolder());
            Assert.That(manager.SetLanguage(code), Is.True, code);
            return manager;
        }

        [Test]
        public void StateTextsAreOneShortSentenceInEveryLanguage()
        {
            foreach (var code in LanguageCodes)
            {
                Loc.Manager = Manager(code);
                foreach (var (key, arguments) in StateTexts)
                {
                    var text = Loc.T(key, arguments); // i18n:keys
                    Assert.That(text.Length, Is.LessThanOrEqualTo(125), code + " " + key + ": " + text);
                    Assert.That(text, Does.Not.Contain("\n"), code + " " + key);
                    Assert.That(text, Does.Not.Contain("{"), code + " " + key + " has an unfilled placeholder: " + text);
                }
            }
        }

        [Test]
        public void EnglishStateTextsDoNotTellWhatToClick()
        {
            Loc.Manager = Manager("en-US");
            foreach (var (key, arguments) in StateTexts)
            {
                var text = Loc.T(key, arguments); // i18n:keys
                Assert.That(text.ToLowerInvariant(), Does.Not.Contain("click"), key + ": " + text);
                Assert.That(text, Does.Not.Contain("Esc"), key + ": " + text);
                Assert.That(text.Length, Is.LessThanOrEqualTo(90), key + ": " + text);
            }
        }

        [Test]
        public void TheStatusOfTheThreeDimensionalViewPointsToTheWikiInsteadOfListingTheMouse()
        {
            foreach (var code in LanguageCodes)
            {
                Loc.Manager = Manager(code);
                var status = Loc.T("view.status.3d");
                Assert.That(status.Length, Is.LessThanOrEqualTo(80), code + ": " + status);
                Assert.That(status, Does.Contain(Loc.T("menu.help")).And.Contain(Loc.T("menu.help.wiki").TrimEnd('.')), code + ": the status names Help and Wiki");
            }
        }

        [Test]
        public void TheRoadPlanLineIsShortInEveryLanguageAndTheFindingsAreCounted()
        {
            foreach (var code in LanguageCodes)
            {
                Loc.Manager = Manager(code);
                var main = Loc.T("roadbuild.describe.main", ("length", 142.3), ("pieces", 3), ("straight", "no"), ("radius", 120.0), ("gradient", 12.5), ("height", 123.45),
                    ("join", "spline"), ("id", 4229), ("port", string.Empty));
                Assert.That(main.Length, Is.LessThanOrEqualTo(EditorShellBootstrap.MaxJoinedHintLength), code + ": " + main);
                Assert.That(main, Does.Not.Contain("{"), code);
                var plan = main + "\n" + Loc.T("roadbuild.describe.corner", ("cornerRadius", 8.0), ("trim", 4.5)) + "\n" + Loc.T("roadbuild.issue", ("severity", "Warning"), ("message", "The gradient is steep."));
                var line = EditorShellBootstrap.HintLine(plan);
                Assert.That(line, Does.StartWith(main), code);
                Assert.That(line, Does.Not.Contain("\n"), code);
                Assert.That(line, Does.Contain("+2"), code + ": " + line);
            }
        }

        [Test]
        public void ShortHintsOfSeveralLinesStayWhole()
        {
            Loc.Manager = Manager("en-US");
            Assert.That(EditorShellBootstrap.HintLine("Click a movement.\nCrossing #3: 4 approaches"), Is.EqualTo("Click a movement.  ·  Crossing #3: 4 approaches"));
            Assert.That(EditorShellBootstrap.HintLine("One line"), Is.EqualTo("One line"));
        }

        [Test]
        public void LanesAndLineSayNothingInTheStatusBarWhereThePanelAlreadySaysWhatToClick()
        {
            var lanes = new LaneToolOptions();
            string hint = null;
            lanes.HintChanged += (text, warning) => hint = text;
            lanes.ShowJunction(string.Empty);
            Assert.That(hint, Is.Empty);
            lanes.ShowJunction("Junction #3: 4 arms, 2 of 6 ways through.");
            Assert.That(hint, Is.EqualTo("Junction #3: 4 arms, 2 of 6 ways through."));
        }

        [Test]
        public void LineSaysNothingWithoutAStopExceptForAnEditedTripAndTheResult()
        {
            Loc.Manager = Manager("en-US");
            Assert.That(LineToolPanel.HintText(string.Empty, null, 0), Is.Empty, "no trip and no stop: the list says what to click");
            var banner = LineToolPanel.HintText(string.Empty, "Line 7 / 06:00", 0);
            Assert.That(banner, Is.Not.Empty);
            StringAssert.Contains("Line 7 / 06:00", banner);
            Assert.That(LineToolPanel.HintText(string.Empty, "Line 7 / 06:00", 2), Is.EqualTo(Loc.T("line.hint")), "from the first stop on, the hint");
            Assert.That(LineToolPanel.HintText(string.Empty, null, 1), Is.EqualTo(Loc.T("line.hint")));
            Assert.That(LineToolPanel.HintText(string.Empty, null, 1), Is.Not.Empty);
            Assert.That(LineToolPanel.HintText("No way between the stops.", "Line 7", 3), Is.EqualTo("No way between the stops."), "a result wins");
        }
    }
}
