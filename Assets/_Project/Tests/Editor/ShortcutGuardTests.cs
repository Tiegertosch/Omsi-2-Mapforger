using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The guards of the shell's shortcuts (ADR 098, M1): keys typed into a field are typing and never shortcuts, and only arrows,
    /// Enter and the modifier keys alone leave an open context menu open.
    /// </summary>
    public sealed class ShortcutGuardTests
    {
        [Test]
        public void KeysInTextAndNumberFieldsAreTyping()
        {
            Assert.That(EditorShellBootstrap.InTextInput(new TextField()), Is.True);
            Assert.That(EditorShellBootstrap.InTextInput(new IntegerField()), Is.True);
            Assert.That(EditorShellBootstrap.InTextInput(new FloatField()), Is.True);
            Assert.That(EditorShellBootstrap.InTextInput(new DoubleField()), Is.True);
        }

        [Test]
        public void TheFocusedPartInsideAFieldCountsAsTheField()
        {
            var field = new IntegerField();
            var inner = new VisualElement();
            field.Add(inner);
            Assert.That(EditorShellBootstrap.InTextInput(inner), Is.True);
        }

        [Test]
        public void ButtonsTogglesAndNothingAreNoTextInput()
        {
            var panel = new VisualElement();
            var button = new Button();
            panel.Add(button);
            Assert.That(EditorShellBootstrap.InTextInput(button), Is.False);
            Assert.That(EditorShellBootstrap.InTextInput(new Toggle()), Is.False);
            Assert.That(EditorShellBootstrap.InTextInput(new Slider()), Is.False);
            Assert.That(EditorShellBootstrap.InTextInput(null), Is.False);
        }

        [TestCase(KeyCode.None)]
        [TestCase(KeyCode.UpArrow)]
        [TestCase(KeyCode.DownArrow)]
        [TestCase(KeyCode.LeftArrow)]
        [TestCase(KeyCode.RightArrow)]
        [TestCase(KeyCode.Return)]
        [TestCase(KeyCode.KeypadEnter)]
        [TestCase(KeyCode.LeftShift)]
        [TestCase(KeyCode.RightControl)]
        [TestCase(KeyCode.LeftAlt)]
        public void TheseKeysLeaveAnOpenContextMenuOpen(KeyCode key)
        {
            Assert.That(EditorShellBootstrap.KeepsContextMenu(key), Is.True);
        }

        [TestCase(KeyCode.Delete)]
        [TestCase(KeyCode.Z)]
        [TestCase(KeyCode.Q)]
        [TestCase(KeyCode.W)]
        [TestCase(KeyCode.Alpha3)]
        [TestCase(KeyCode.Space)]
        [TestCase(KeyCode.Escape)]
        public void EveryOtherKeyClosesAnOpenContextMenu(KeyCode key)
        {
            Assert.That(EditorShellBootstrap.KeepsContextMenu(key), Is.False);
        }

        [TestCase(true, false, false, true)]  // map, bar or other surface outside the open menu: close and consume
        [TestCase(true, true, false, false)]  // inside the menu: as before
        [TestCase(true, false, true, false)]  // a menu bar button: it toggles (closes its own menu, replaces another)
        [TestCase(false, false, false, false)] // no menu open: nothing to close
        [TestCase(false, false, true, false)]
        public void APressClosesTheOpenBarMenuOnlyOutsideItAndOffTheMenuButtons(bool anyOpen, bool inside, bool onButton, bool closes)
        {
            Assert.That(EditorShellBootstrap.PressClosesBarMenu(anyOpen, inside, onButton), Is.EqualTo(closes));
        }

        [TestCase(7, 7, true)]   // the release of the pointer whose press closed a menu is swallowed too
        [TestCase(7, 8, false)]  // another pointer's release passes
        [TestCase(-1, 0, false)] // no swallowed press: nothing is swallowed
        [TestCase(-1, -1, false)]
        public void TheReleaseOfASwallowedPressIsSwallowedToo(int swallowed, int released, bool swallows)
        {
            Assert.That(EditorShellBootstrap.SwallowsRelease(swallowed, released), Is.EqualTo(swallows));
        }
    }
}