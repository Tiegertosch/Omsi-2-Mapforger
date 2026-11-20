using System;
using System.IO;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.UI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The interface side of the language switch (ADR 068): bound elements write their texts anew when the language changes, and the
    /// language files that ship with the editor pass the validator.
    /// </summary>
    public sealed class LocalizationInterfaceTests
    {
        private string root;
        private TranslationManager previous;

        [SetUp]
        public void SetUp()
        {
            previous = Loc.Manager;
            root = Path.Combine(Path.GetTempPath(), "mapforge-ui-locales-" + Guid.NewGuid().ToString("N"));
            Language("en-US", "English");
            File.WriteAllText(Path.Combine(root, "en-US", "t.json"),
                "{ \"t.hello\": \"Hello {name}\", \"t.tip\": \"A tip\", \"t.other\": \"Other text\", \"t.items\": \"{count, plural, one {# item} other {# items}}\" }");
            Language("de-DE", "Deutsch");
            File.WriteAllText(Path.Combine(root, "de-DE", "t.json"),
                "{ \"t.hello\": \"Hallo {name}\", \"t.tip\": \"Ein Tipp\", \"t.other\": \"Anderer Text\", \"t.items\": \"{count, plural, one {# Eintrag} other {# Eintr\u00e4ge}}\" }");
            Loc.Manager = new TranslationManager(root);
            Loc.Manager.SetLanguage("en-US");
        }

        [TearDown]
        public void TearDown()
        {
            Loc.Manager = previous;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private sealed class PromptFrame : FloatingWindowFrame
        {
            public PromptFrame() : base(new FloatingWindowOptions { Icon = "minus", TitleKey = "t.other" }, null)
            {
                CompleteFrame();
            }

            public string PromptText => this.Q<Label>(className: "signal-prompt-text").text;
        }

        [Test]
        public void AWindowQuestionFollowsALanguageChangeAndEmptiesWhenTaken()
        {
            var frame = new PromptFrame();
            var parent = new VisualElement();
            parent.Add(frame);
            frame.ShowPrompt("t.tip", true);
            Assert.AreEqual("A tip", frame.PromptText);

            Loc.Manager.SetLanguage("de-DE");
            LocBindings.Refresh(parent);
            Assert.AreEqual("Ein Tipp", frame.PromptText);

            frame.ShowPrompt(null, false);
            LocBindings.Refresh(parent);
            Assert.AreEqual(string.Empty, frame.PromptText);
        }

        [Test]
        public void ABoundLabelWritesItsTextAnewWhenTheTreeIsRefreshed()
        {
            var parent = new VisualElement();
            var label = LocBindings.NewLabel("t.hello", ("name", "Anna"));
            parent.Add(label);
            Assert.AreEqual("Hello Anna", label.text);

            Loc.Manager.SetLanguage("de-DE");
            Assert.AreEqual("Hello Anna", label.text, "nothing changes before the tree is refreshed");
            LocBindings.Refresh(parent);

            Assert.AreEqual("Hallo Anna", label.text);
        }

        [Test]
        public void TextAndTooltipAreBoundSeparatelyAndAToggleKeepsItsTextBesideTheBox()
        {
            var parent = new VisualElement();
            var toggle = new Toggle();
            LocBindings.Text(toggle, "t.other");
            LocBindings.Tooltip(toggle, "t.tip");
            parent.Add(toggle);
            Assert.AreEqual("Other text", toggle.text);
            Assert.IsTrue(string.IsNullOrEmpty(toggle.label), "the label at the left stays empty");

            Loc.Manager.SetLanguage("de-DE");
            LocBindings.Refresh(parent);

            Assert.AreEqual("Anderer Text", toggle.text);
            Assert.AreEqual("Ein Tipp", toggle.tooltip);
        }

        [Test]
        public void BindingAnotherKeyReplacesTheOldOneAndPluralsFollowTheLanguage()
        {
            var parent = new VisualElement();
            var label = new Label();
            parent.Add(label);
            LocBindings.Text(label, "t.items", ("count", 1));
            LocBindings.Text(label, "t.items", ("count", 3));
            Assert.AreEqual("3 items", label.text);

            Loc.Manager.SetLanguage("de-DE");
            LocBindings.Refresh(parent);
            Assert.AreEqual("3 Einträge", label.text);

            LocBindings.Text(label, "t.other");
            Loc.Manager.SetLanguage("en-US");
            LocBindings.Refresh(parent);
            Assert.AreEqual("Other text", label.text);
        }

        [Test]
        public void ANestedElementIsFoundByTheRefresh()
        {
            var parent = new VisualElement();
            var middle = new VisualElement();
            parent.Add(middle);
            var button = LocBindings.NewButton("t.other", () => { });
            middle.Add(button);

            Loc.Manager.SetLanguage("de-DE");
            LocBindings.Refresh(parent);

            Assert.AreEqual("Anderer Text", button.text);
        }

        [Test]
        public void TheLanguageFilesThatShipPassTheValidatorAndGermanIsComplete()
        {
            var shipped = RepositoryLocales();
            var report = LocaleValidator.Validate(shipped);

            Assert.AreEqual(0, report.ErrorCount, string.Join("\n", report.Issues));
            var german = new TranslationManager(shipped);
            Assert.IsTrue(german.SetLanguage("de-DE"));
            Assert.AreEqual("Datei", german.T("menu.file"));
            Assert.AreEqual("Bereit", german.T("status.ready"));
        }

        [Test]
        public void ASingleSplineIsNamedByItsIdInEveryLanguageNotByTheCount()
        {
            var shipped = new TranslationManager(RepositoryLocales());
            foreach (var language in new[] { "cs-CZ", "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "pl-PL", "pt-PT" })
            {
                Assert.IsTrue(shipped.SetLanguage(language), language);
                foreach (var key in new[] { "history.step.spline_move", "history.step.road_of", "history.step.spline_deleted" })
                {
                    var text = shipped.T(key, ("count", 1), ("id", 4186));
                    Assert.That(text, Does.Contain("4186"), language + " " + key + ": " + text);
                    Assert.That(text, Does.Not.Contain("14186"), language + " " + key + ": " + text);
                    Assert.That(text, Does.Not.Contain("1{id}"), language + " " + key + ": " + text);
                }
                var list = shipped.T("roadbuild.splines", ("count", 2), ("ids", "#7, #9"));
                Assert.That(list, Does.Contain("#7, #9"), language + ": " + list);
                Assert.That(list, Does.Not.Contain("2 #7"), language + ": " + list);
            }
        }

        [Test]
        public void AMissingSplineReasonAndTheShiftBoxHintAreTranslatedInEveryLanguage()
        {
            var shipped = new TranslationManager(RepositoryLocales());
            var english = new TranslationManager(RepositoryLocales());
            Assert.IsTrue(english.SetLanguage("en-US"));
            Assert.AreEqual("#4186: not in the map any more", english.T("splinevar.status.gone_in_map", ("id", 4186)));
            foreach (var language in new[] { "cs-CZ", "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "pl-PL", "pt-PT" })
            {
                Assert.IsTrue(shipped.SetLanguage(language), language);
                var reason = shipped.T("splinevar.status.gone_in_map", ("id", 4186));
                Assert.That(reason, Does.Contain("4186"), language + ": " + reason);
                Assert.That(reason, Does.Not.Contain("{id}"), language + ": " + reason);
                if (language != "en-US")
                    Assert.AreNotEqual(english.T("splinevar.status.gone_in_map", ("id", 4186)), reason, language);
                Assert.That(shipped.T("toolbar.drag.tooltip"), Does.Contain("Shift").Or.Contain("Umschalt").Or.Contain("Maj").Or.Contain("Mayús")
                    .Or.Contain("Maiusc"), language);
            }
        }

        private void Language(string code, string name)
        {
            Directory.CreateDirectory(Path.Combine(root, code));
            File.WriteAllText(Path.Combine(root, code, "locale.json"),
                "{ \"code\": \"" + code + "\", \"name\": \"" + name + "\", \"englishName\": \"" + name + "\", \"status\": \"approved\" }");
        }

        private static string RepositoryLocales()
        {
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 6 && folder != null; depth++, folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(Path.Combine(folder, "locales")) && Directory.Exists(Path.Combine(folder, "Assets", "_Project", "Scripts")))
                    return Path.Combine(folder, "locales");
            Assert.Fail("The repository root was not found from " + Directory.GetCurrentDirectory());
            return null;
        }
    }
}
