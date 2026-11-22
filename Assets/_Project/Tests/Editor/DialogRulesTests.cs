using System;
using System.Collections.Generic;
using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>The keyboard and button rules of the dialog factory (M7 step 1) and a dialog built without a panel.</summary>
    public sealed class DialogRulesTests
    {
        private static readonly DialogButtonRole P = DialogButtonRole.Primary;
        private static readonly DialogButtonRole C = DialogButtonRole.Cancel;
        private static readonly DialogButtonRole S = DialogButtonRole.Secondary;
        private static readonly DialogButtonRole D = DialogButtonRole.Danger;

        [Test]
        public void WidthsAreTheFourClassesOfTheOwnersDecision()
        {
            Assert.That(DialogRules.WidthPixels(DialogWidth.Narrow), Is.EqualTo(420));
            Assert.That(DialogRules.WidthPixels(DialogWidth.Standard), Is.EqualTo(560));
            Assert.That(DialogRules.WidthPixels(DialogWidth.Wide), Is.EqualTo(720));
            Assert.That(DialogRules.WidthPixels(DialogWidth.Form), Is.EqualTo(880));
            Assert.That(DialogRules.WidthClass(DialogWidth.Narrow), Is.EqualTo("dialog-narrow"));
            Assert.That(DialogRules.WidthClass(DialogWidth.Standard), Is.EqualTo("dialog-standard"));
            Assert.That(DialogRules.WidthClass(DialogWidth.Wide), Is.EqualTo("dialog-wide"));
            Assert.That(DialogRules.WidthClass(DialogWidth.Form), Is.EqualTo("dialog-form"));
        }

        [Test]
        public void CancelStandsLeftAndPrimaryRightmost()
        {
            Assert.That(DialogRules.RowOrder(new[] { P, C }), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(DialogRules.RowOrder(new[] { C, P }), Is.EqualTo(new[] { 0, 1 }));
            // Updates: Skip (secondary), Close (cancel), then the action of the state.
            Assert.That(DialogRules.RowOrder(new[] { P, C, S }), Is.EqualTo(new[] { 2, 1, 0 }));
            Assert.That(DialogRules.RowOrder(new[] { P, D, C }), Is.EqualTo(new[] { 2, 1, 0 }));
            // Equal roles keep the order they were given in.
            Assert.That(DialogRules.RowOrder(new[] { S, S, P }), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void AtMostOneEachOfPrimaryAndCancelAndAtLeastOneButton()
        {
            Assert.DoesNotThrow(() => DialogRules.ValidateButtons(new[] { C, P }));
            Assert.DoesNotThrow(() => DialogRules.ValidateButtons(new[] { P }));
            Assert.Throws<ArgumentException>(() => DialogRules.ValidateButtons(new[] { P, P }));
            Assert.Throws<ArgumentException>(() => DialogRules.ValidateButtons(new[] { C, C, P }));
            Assert.Throws<ArgumentException>(() => DialogRules.ValidateButtons(new DialogButtonRole[0]));
            Assert.Throws<ArgumentException>(() => DialogRules.ValidateButtons(null));
        }

        [Test]
        public void EscStandsForTheCancelButtonOrJustClosesWhenThereIsNone()
        {
            Assert.That(DialogRules.EscapeButton(new[] { P, C }), Is.EqualTo(1));
            Assert.That(DialogRules.EscapeButton(new[] { S, C, P }), Is.EqualTo(1));
            Assert.That(DialogRules.EscapeButton(new[] { P }), Is.EqualTo(-1));
        }

        [Test]
        public void EnterActivatesThePrimaryButtonOnlyWithExactlyOneSingleLineField()
        {
            // One single-line field, focus in it, primary enabled: yes.
            Assert.That(DialogRules.EnterActivatesPrimary(1, 0, false, true), Is.True);
            // No field (timetable without text field, notice): no.
            Assert.That(DialogRules.EnterActivatesPrimary(0, 0, false, true), Is.False);
            // Several fields: no, even single-line ones.
            Assert.That(DialogRules.EnterActivatesPrimary(2, 0, false, true), Is.False);
            Assert.That(DialogRules.EnterActivatesPrimary(5, 0, false, true), Is.False);
            // The only field is multi-line: Enter is a new line.
            Assert.That(DialogRules.EnterActivatesPrimary(1, 1, false, true), Is.False);
            // A single-line plus a multi-line field: no.
            Assert.That(DialogRules.EnterActivatesPrimary(2, 1, false, true), Is.False);
            // Focus on a button: the button takes Enter itself.
            Assert.That(DialogRules.EnterActivatesPrimary(1, 0, true, true), Is.False);
            // Primary missing or disabled.
            Assert.That(DialogRules.EnterActivatesPrimary(1, 0, false, false), Is.False);
        }

        [Test]
        public void TheDialogFocusesAMarkedElementThenTheFirstFieldThenThePrimaryButton()
        {
            Assert.That(DialogRules.InitialFocus(true, true, true, true), Is.EqualTo(DialogInitialFocus.Marked));
            Assert.That(DialogRules.InitialFocus(false, true, true, true), Is.EqualTo(DialogInitialFocus.TextField));
            Assert.That(DialogRules.InitialFocus(false, false, true, true), Is.EqualTo(DialogInitialFocus.Primary));
            Assert.That(DialogRules.InitialFocus(false, false, false, true), Is.EqualTo(DialogInitialFocus.FirstButton));
            Assert.That(DialogRules.InitialFocus(false, false, false, false), Is.EqualTo(DialogInitialFocus.None));
        }

        [Test]
        public void TabStaysInTheDialogByWrappingAtBothEnds()
        {
            Assert.That(DialogRules.TabTarget(3, 2, false), Is.EqualTo(0), "Tab after the last wraps to the first");
            Assert.That(DialogRules.TabTarget(3, 0, true), Is.EqualTo(2), "Shift+Tab before the first wraps to the last");
            Assert.That(DialogRules.TabTarget(3, 1, false), Is.EqualTo(-1), "inside: normal order");
            Assert.That(DialogRules.TabTarget(3, 1, true), Is.EqualTo(-1));
            Assert.That(DialogRules.TabTarget(3, -1, false), Is.EqualTo(0), "focus outside the dialog comes back to the first");
            Assert.That(DialogRules.TabTarget(3, -1, true), Is.EqualTo(2));
            Assert.That(DialogRules.TabTarget(0, -1, false), Is.EqualTo(-1), "nothing to focus");
            Assert.That(DialogRules.TabTarget(1, 0, false), Is.EqualTo(0), "a single element keeps the focus");
        }

        private static DialogOptions Options(string name, DialogWidth width, params DialogButton[] buttons)
        {
            return new DialogOptions { Name = name, TitleKey = "signals.notice.title", Width = width, Buttons = buttons };
        }

        [Test]
        public void ADialogIsBuiltHiddenWithWidthClassTitleBodyAndTheButtonsInRowOrder()
        {
            var order = new List<string>();
            var options = Options("test-dialog", DialogWidth.Wide,
                new DialogButton { TextKey = "signals.notice.ok", Role = P, Clicked = () => order.Add("primary") },
                new DialogButton { TextKey = "signals.notice.ok", Role = C, Clicked = () => order.Add("cancel") });
            options.DescriptionKey = "signals.notice.text";
            var dialog = ModalDialog.Create(options);

            Assert.That(dialog.Overlay.name, Is.EqualTo("test-dialog"));
            Assert.That(dialog.Overlay.ClassListContains("dialog-overlay"));
            Assert.That(dialog.Dialog.ClassListContains("dialog-wide"));
            Assert.That(dialog.Dialog.ClassListContains("dialog"));
            Assert.That(dialog.IsShown, Is.False);
            Assert.That(dialog.Description, Is.Not.Null);
            Assert.That(dialog.Buttons.Count, Is.EqualTo(2));
            Assert.That(dialog.Buttons[0].ClassListContains("dialog-primary"), Is.False, "cancel first");
            Assert.That(dialog.Buttons[1], Is.SameAs(dialog.Primary));
            Assert.That(dialog.Primary.ClassListContains("dialog-primary"));
            Assert.That(dialog.Body.GetFirstAncestorOfType<ScrollView>(), Is.Not.Null, "the body scrolls");
        }

        [Test]
        public void ADialogWithoutScrollingHasAPlainBody()
        {
            var options = Options("plain", DialogWidth.Narrow, new DialogButton { TextKey = "signals.notice.ok", Role = P });
            options.BodyScrolls = false;
            var dialog = ModalDialog.Create(options);
            Assert.That(dialog.Body.GetFirstAncestorOfType<ScrollView>(), Is.Null);
            Assert.That(dialog.Description, Is.Null);
        }

        [Test]
        public void CancelAndEscRunTheSameThingsAndThePrimaryButtonDoesNotRunClosed()
        {
            var log = new List<string>();
            var options = Options("flow", DialogWidth.Standard,
                new DialogButton { TextKey = "signals.notice.ok", Role = C, Clicked = () => log.Add("cancel") },
                new DialogButton { TextKey = "signals.notice.ok", Role = P, Clicked = () => log.Add("primary") });
            options.Closed = () => log.Add("closed");
            var dialog = ModalDialog.Create(options);

            dialog.Show();
            Assert.That(dialog.IsShown);
            dialog.Dismiss();
            var viaCancel = string.Join(",", log);
            Assert.That(dialog.IsShown, Is.False);

            log.Clear();
            dialog.Show();
            OverlayRegistry.Hide(dialog.Overlay); // what the registry does on Esc
            dialog.OnEscaped();
            Assert.That(string.Join(",", log), Is.EqualTo(viaCancel));
            Assert.That(viaCancel, Is.EqualTo("cancel,closed"));

            log.Clear();
            dialog.Show();
            dialog.Press(dialog.Primary);
            Assert.That(string.Join(",", log), Is.EqualTo("primary"));
            Assert.That(dialog.IsShown, Is.False, "the primary button closes after its action");
        }

        [Test]
        public void AnOverlayRegisteredByTheShellClosesWithEscThroughTheRegistry()
        {
            var closed = 0;
            var options = Options("esc", DialogWidth.Standard,
                new DialogButton { TextKey = "signals.notice.ok", Role = C },
                new DialogButton { TextKey = "signals.notice.ok", Role = P });
            options.Closed = () => closed++;
            var dialog = ModalDialog.Create(options);
            var registry = new OverlayRegistry();
            Assert.That(registry.Register(dialog.Overlay, true, dialog.OnEscaped));

            dialog.Show();
            Assert.That(registry.AnyModalShown());
            Assert.That(registry.CloseAllShown());
            Assert.That(dialog.IsShown, Is.False);
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(registry.CloseAllShown(), Is.False, "nothing open: Esc does not run Closed again");
            Assert.That(closed, Is.EqualTo(1));
        }

        [Test]
        public void EnterPressesThePrimaryButtonOnlyInADialogWithOneTextField()
        {
            var presses = 0;
            var options = Options("enter", DialogWidth.Narrow,
                new DialogButton { TextKey = "signals.notice.ok", Role = C },
                new DialogButton { TextKey = "signals.notice.ok", Role = P, Clicked = () => presses++ });
            var dialog = ModalDialog.Create(options);

            Assert.That(dialog.TryEnter(null), Is.False, "no field: Enter does nothing");

            var first = new TextField();
            dialog.Body.Add(first);
            Assert.That(dialog.TryEnter(first), Is.True);
            Assert.That(presses, Is.EqualTo(1));
            Assert.That(dialog.TryEnter(dialog.Buttons[0]), Is.False, "focus on a button");

            dialog.Body.Add(new TextField());
            Assert.That(dialog.TryEnter(first), Is.False, "two fields");
            Assert.That(presses, Is.EqualTo(1));
        }

        [Test]
        public void EnterDoesNotPressThePrimaryButtonWhenTheDialogTurnsItOff()
        {
            var presses = 0;
            var options = Options("noenter", DialogWidth.Standard,
                new DialogButton { TextKey = "signals.notice.ok", Role = C },
                new DialogButton { TextKey = "signals.notice.ok", Role = P, Clicked = () => presses++ });
            options.EnterPressesPrimary = false;
            var dialog = ModalDialog.Create(options);
            var field = new TextField();
            dialog.Body.Add(field);

            Assert.That(dialog.TryEnter(field), Is.False, "one single-line field, but the dialog does not take Enter (park list)");
            Assert.That(presses, Is.EqualTo(0));
            Assert.That(dialog.IsShown, Is.False);

            // The same dialog with the default does press it, so the option is what decides.
            var other = Options("withenter", DialogWidth.Standard,
                new DialogButton { TextKey = "signals.notice.ok", Role = P, Clicked = () => presses++ });
            var enabled = ModalDialog.Create(other);
            var otherField = new TextField();
            enabled.Body.Add(otherField);
            Assert.That(enabled.TryEnter(otherField), Is.True);
            Assert.That(presses, Is.EqualTo(1));
        }

        [Test]
        public void EscClosesADialogWithoutCancelButtonWithoutPressingItsOneButtonAndOnlyOnce()
        {
            var log = new List<string>();
            var options = Options("only-ok", DialogWidth.Narrow,
                new DialogButton { TextKey = "signals.notice.ok", Role = P, Clicked = () => log.Add("primary") });
            options.Closed = () => log.Add("closed");
            var dialog = ModalDialog.Create(options);
            var registry = new OverlayRegistry();
            registry.Register(dialog.Overlay, true, dialog.OnEscaped);

            dialog.Show();
            Assert.That(registry.CloseAllShown(), Is.True, "Esc closes the notice although it has no cancel button");
            Assert.That(dialog.IsShown, Is.False);
            Assert.That(string.Join(",", log), Is.EqualTo("closed"), "the main action does not run by Esc");

            // The navigation event that follows the key in the same frame reaches a dialog already hidden: it is not closed twice.
            dialog.Dismiss();
            Assert.That(string.Join(",", log), Is.EqualTo("closed"));

            // The dialog from itself (the key reached it first): same result, once.
            log.Clear();
            dialog.Show();
            dialog.Dismiss();
            dialog.Dismiss();
            Assert.That(string.Join(",", log), Is.EqualTo("closed"));
        }

        [Test]
        public void AMultiLineFieldNeverTakesEnter()
        {
            var presses = 0;
            var options = Options("multiline", DialogWidth.Standard,
                new DialogButton { TextKey = "signals.notice.ok", Role = P, Clicked = () => presses++ });
            var dialog = ModalDialog.Create(options);
            var field = new TextField { multiline = true };
            dialog.Body.Add(field);

            Assert.That(dialog.TryEnter(field), Is.False);
            Assert.That(presses, Is.EqualTo(0));
        }

        [Test]
        public void ARefusedBuildNamesTheProblem()
        {
            var button = new DialogButton { TextKey = "signals.notice.ok", Role = P };
            Assert.Throws<ArgumentException>(() => ModalDialog.Create(new DialogOptions { TitleKey = "signals.notice.title", Buttons = new[] { button } }));
            Assert.Throws<ArgumentException>(() => ModalDialog.Create(new DialogOptions { Name = "x", Buttons = new[] { button } }));
            Assert.Throws<ArgumentException>(() => ModalDialog.Create(new DialogOptions { Name = "x", TitleKey = "signals.notice.title" }));
            Assert.Throws<ArgumentException>(() => ModalDialog.Create(Options("x", DialogWidth.Standard, button, new DialogButton { Role = P })));
            Assert.Throws<ArgumentNullException>(() => ModalDialog.Create(null));
        }
    }
}
