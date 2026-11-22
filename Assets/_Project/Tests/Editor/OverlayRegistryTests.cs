using System.Collections.Generic;
using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The registry of the dialogs and windows that close with Esc (ADR 098, M3). The elements stand on no panel: an inline
    /// <c>display</c> of an element without a panel shows in its resolved style at once, so the registry can be tested without a window.
    /// </summary>
    public sealed class OverlayRegistryTests
    {
        private static VisualElement Hidden()
        {
            var element = new VisualElement();
            OverlayRegistry.Hide(element);
            return element;
        }

        [Test]
        public void AnElementIsShownUntilItIsHiddenAndANullElementNever()
        {
            var element = new VisualElement();

            Assert.That(OverlayRegistry.IsShown(element), Is.True);
            OverlayRegistry.Hide(element);
            Assert.That(OverlayRegistry.IsShown(element), Is.False);
            element.style.display = DisplayStyle.Flex;
            Assert.That(OverlayRegistry.IsShown(element), Is.True);
            Assert.That(OverlayRegistry.IsShown(null), Is.False);
            Assert.DoesNotThrow(() => OverlayRegistry.Hide(null));
        }

        [Test]
        public void ANullElementAndAnElementRegisteredBeforeAreRefused()
        {
            var registry = new OverlayRegistry();
            var element = new VisualElement();

            Assert.That(registry.Register(null, true), Is.False);
            Assert.That(registry.Count, Is.EqualTo(0));
            Assert.That(registry.Register(element, false), Is.True);
            Assert.That(registry.Register(element, true, () => { }), Is.False, "a second registration, also with other settings");
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.IsRegistered(element), Is.True);
            Assert.That(registry.IsRegistered(new VisualElement()), Is.False);
        }

        [Test]
        public void TheFirstRegistrationKeepsItsModalSettingWhenARepeatIsRefused()
        {
            var registry = new OverlayRegistry();
            var element = new VisualElement();
            registry.Register(element, false);
            registry.Register(element, true);

            Assert.That(registry.AnyShown(), Is.True);
            Assert.That(registry.AnyModalShown(), Is.False);
        }

        [Test]
        public void NothingIsShownInAnEmptyRegistryOrWhileAllOverlaysAreHidden()
        {
            var registry = new OverlayRegistry();
            Assert.That(registry.AnyShown(), Is.False);
            Assert.That(registry.AnyModalShown(), Is.False);

            registry.Register(Hidden(), true);
            registry.Register(Hidden(), false);

            Assert.That(registry.AnyShown(), Is.False);
            Assert.That(registry.AnyModalShown(), Is.False);
        }

        [Test]
        public void AnyShownSeesAnyOverlayButAnyModalShownOnlyAModalOne()
        {
            var registry = new OverlayRegistry();
            var modal = Hidden();
            var window = new VisualElement();
            registry.Register(modal, true);
            registry.Register(window, false);

            Assert.That(registry.AnyShown(), Is.True);
            Assert.That(registry.AnyModalShown(), Is.False, "only the window that takes no keys is shown");
            modal.style.display = DisplayStyle.Flex;
            Assert.That(registry.AnyModalShown(), Is.True);
            OverlayRegistry.Hide(window);
            OverlayRegistry.Hide(modal);
            Assert.That(registry.AnyShown(), Is.False);
        }

        [Test]
        public void CloseAllShownDoesNothingAndReturnsFalseWhenNothingIsShown()
        {
            var registry = new OverlayRegistry();
            var calls = 0;
            registry.Register(Hidden(), true, () => calls++);

            Assert.That(registry.CloseAllShown(), Is.False);
            Assert.That(calls, Is.EqualTo(0));
        }

        [Test]
        public void CloseAllShownHidesAllAndCleansUpOnlyThoseThatWereShownInTheOrderOfRegistration()
        {
            var registry = new OverlayRegistry();
            var order = new List<string>();
            var first = new VisualElement();
            var second = Hidden();
            var third = new VisualElement();
            var fourth = new VisualElement();
            registry.Register(third, false, () => order.Add("third"));
            registry.Register(first, true, () => order.Add("first"));
            registry.Register(second, true, () => order.Add("second"));
            registry.Register(fourth, false);

            Assert.That(registry.CloseAllShown(), Is.True);

            Assert.That(order, Is.EqualTo(new[] { "third", "first" }), "registration order, not the hidden one");
            Assert.That(registry.AnyShown(), Is.False);
            foreach (var element in new[] { first, second, third, fourth })
                Assert.That(OverlayRegistry.IsShown(element), Is.False);
            Assert.That(registry.CloseAllShown(), Is.False, "a second Esc finds nothing to close");
            Assert.That(order.Count, Is.EqualTo(2));
        }

        [Test]
        public void TheCleanUpRunsAfterEveryOverlayIsHidden()
        {
            var registry = new OverlayRegistry();
            var first = new VisualElement();
            var second = new VisualElement();
            var shownWhenCleaningUp = new List<bool>();
            registry.Register(first, false, () => shownWhenCleaningUp.Add(OverlayRegistry.IsShown(second)));
            registry.Register(second, false);

            registry.CloseAllShown();

            Assert.That(shownWhenCleaningUp, Is.EqualTo(new[] { false }));
        }

        [Test]
        public void EscClosesOnlyTheTopModalDialogAndLeavesWindowsAndDialogsBelow()
        {
            var registry = new OverlayRegistry();
            var root = new VisualElement();
            var window = new VisualElement();
            var lower = new VisualElement();
            var upper = new VisualElement();
            root.Add(window);
            root.Add(lower);
            root.Add(upper);
            var closed = new List<string>();
            registry.Register(window, false, () => closed.Add("window"));
            registry.Register(lower, true, () => closed.Add("lower"));
            registry.Register(upper, true, () => closed.Add("upper"));

            Assert.That(registry.TopShownModal(), Is.SameAs(upper));
            Assert.That(registry.CloseTopModal(), Is.True);
            Assert.That(closed, Is.EqualTo(new[] { "upper" }));
            Assert.That(OverlayRegistry.IsShown(lower), Is.True);
            Assert.That(OverlayRegistry.IsShown(window), Is.True);
            Assert.That(registry.CloseTopModal(), Is.True);
            Assert.That(registry.CloseTopModal(), Is.False, "no modal dialog left: the window is not a dialog");
            Assert.That(closed, Is.EqualTo(new[] { "upper", "lower" }));
        }

        [Test]
        public void ADialogThatIsBroughtToTheFrontIsTheTopOne()
        {
            var registry = new OverlayRegistry();
            var root = new VisualElement();
            var first = new VisualElement();
            var second = new VisualElement();
            root.Add(first);
            root.Add(second);
            registry.Register(first, true);
            registry.Register(second, true);

            first.BringToFront();

            Assert.That(registry.TopShownModal(), Is.SameAs(first));
        }

        [Test]
        public void ElementsInsideAShownModalDialogAreInsideAndTheMapBesideItIsNot()
        {
            var registry = new OverlayRegistry();
            var root = new VisualElement();
            var map = new VisualElement();
            var dialog = new VisualElement();
            var button = new VisualElement();
            root.Add(map);
            root.Add(dialog);
            dialog.Add(button);
            var window = new VisualElement();
            root.Add(window);
            registry.Register(dialog, true);
            registry.Register(window, false);

            Assert.That(registry.IsInsideShownModal(button), Is.True);
            Assert.That(registry.IsInsideShownModal(dialog), Is.True);
            Assert.That(registry.IsInsideShownModal(map), Is.False, "a click on the map behind a dialog is not inside it");
            Assert.That(registry.IsInsideShownModal(window), Is.False, "a window beside the map is not a modal dialog");
            Assert.That(registry.IsInsideShownModal(null), Is.False);
            OverlayRegistry.Hide(dialog);
            Assert.That(registry.IsInsideShownModal(button), Is.False, "a hidden dialog covers nothing");
        }
    }
}
