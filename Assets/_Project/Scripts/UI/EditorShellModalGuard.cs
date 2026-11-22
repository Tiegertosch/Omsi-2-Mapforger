using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// While a modal dialog is open, the map behind it gets no pointer input and Esc closes the dialog wherever the focus is (M7
    /// step 2, correction after the owner's hand test of the signal notice: a click on a building reached the map, the dialog lost the
    /// focus and Esc did nothing). Two guards at the shell root, one for each way the dialog could be bypassed:
    /// <list type="bullet">
    /// <item>A pointer press or wheel turn whose target lies outside every shown modal dialog goes no further (trickle phase at the
    /// root, before the map sees it); the focus goes back into the dialog.</item>
    /// <item>With no focused element at all, a key event targets the panel root, which the shell root's own handler is not on the path
    /// of; the same Esc handling is attached to the panel root, so Esc closes the topmost dialog from any focus state.</item>
    /// </list>
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private bool modalGuardAttached;

        private void AttachModalGuard(VisualElement root)
        {
            if (modalGuardAttached || root == null)
                return;
            modalGuardAttached = true;
            root.RegisterCallback<PointerDownEvent>(OnPointerBehindModal, TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(OnWheelBehindModal, TrickleDown.TrickleDown);
            root.RegisterCallback<FocusOutEvent>(OnFocusLeavesModal);
            if (root.panel != null)
                root.panel.visualTree.RegisterCallback<KeyDownEvent>(OnPanelRootKey, TrickleDown.TrickleDown);
            else
                root.RegisterCallback<AttachToPanelEvent>(OnGuardAttached);
        }

        /// <summary>Hooks the panel-root key handler once, when the shell root first reaches a panel.</summary>
        private void OnGuardAttached(AttachToPanelEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            element.UnregisterCallback<AttachToPanelEvent>(OnGuardAttached);
            var visualTree = element.panel.visualTree;
            visualTree.UnregisterCallback<KeyDownEvent>(OnPanelRootKey, TrickleDown.TrickleDown);
            visualTree.RegisterCallback<KeyDownEvent>(OnPanelRootKey, TrickleDown.TrickleDown);
        }

        /// <summary>Whether the element is inside a modal dialog that is shown (its overlay is the element or an ancestor).</summary>
        private bool InsideShownModal(VisualElement element)
        {
            return overlays.IsInsideShownModal(element);
        }

        private void OnPointerBehindModal(PointerDownEvent evt)
        {
            if (BlocksForModal(evt.target as VisualElement))
                KeepModalFocus(evt);
        }

        private void OnWheelBehindModal(WheelEvent evt)
        {
            if (BlocksForModal(evt.target as VisualElement))
            {
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        /// <summary>A press or wheel turn outside every shown modal dialog while one is shown (the tooltip never takes input).</summary>
        private bool BlocksForModal(VisualElement target)
        {
            return overlays.BlocksPointer(target, rootElement);
        }

        /// <summary>The focus moves from inside a modal dialog to an element outside every one (the map, a bar button): it goes back in.</summary>
        private void OnFocusLeavesModal(FocusOutEvent evt)
        {
            // The list of a dropdown opened from the dialog takes the focus while it is open: that is the dialog's own popup.
            if (!(evt.relatedTarget is VisualElement next) || !AnyModalDialogShown() || InsideShownModal(next) || OverlayRegistry.IsPopupElement(next, rootElement)
                || !InsideShownModal(evt.target as VisualElement))
                return;
            rootElement.schedule.Execute(() =>
            {
                if (!(overlays.TopShownModal() is VisualElement top) || top.panel == null)
                    return;
                var focusedNow = top.panel.focusController?.focusedElement as VisualElement;
                if (!InsideShownModal(focusedNow) && !OverlayRegistry.IsPopupElement(focusedNow, rootElement))
                    top.Focus();
            });
        }

        /// <summary>The names of the dialog overlays that are shown, bottom to top (for the Esc check).</summary>
        private string ShownDialogNames()
        {
            return string.Join(", ", rootElement.Query<VisualElement>(className: ModalDialog.OverlayClass).ToList().Where(OverlayRegistry.IsShown).Select(e => e.name));
        }

        private static string Describe(VisualElement element)
        {
            return element == null ? "nothing" : element.GetType().Name + "#" + element.name + "." + string.Join(".", element.GetClasses());
        }

        private void KeepModalFocus(EventBase evt)
        {
            evt.StopPropagation();
            evt.PreventDefault();
            if (overlays.TopShownModal() is VisualElement top && top.panel != null)
            {
                var focused = top.panel.focusController?.focusedElement as VisualElement;
                if (focused == null || !top.Contains(focused))
                    top.Focus();
            }
        }

        /// <summary>Esc with the focus on nothing: the key targets the panel root and would never reach the shell root's handler.</summary>
        private void OnPanelRootKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || evt.target != rootElement?.panel?.visualTree || !AnyModalDialogShown())
                return;
            OnEscape(evt);
        }

        /// <summary>
        /// For -dropdownCheck [index]: opens the n-th dropdown field inside the shown modal dialog the way the keyboard does (Submit),
        /// then looks at the list Unity built (where it hangs, who has the focus), moves the focus to a row as the pointer does while
        /// it hovers, and logs after each step whether the list and the dialog are still there ("Dropdown check:").
        /// With -pressEscape-like steps it can also send Esc to the open list (-dropdownEscape).
        /// </summary>
        private void RunDropdownCheck()
        {
            var panel = rootElement.panel;
            var args = System.Environment.GetCommandLineArgs();
            var at = System.Array.IndexOf(args, "-dropdownCheck");
            var which = 0;
            if (at >= 0 && at + 1 < args.Length)
                int.TryParse(args[at + 1], out which);
            var top = overlays.TopShownModal();
            var fields = top?.Query<DropdownField>().ToList();
            if (fields == null || which < 0 || which >= fields.Count)
            {
                Debug.Log("Dropdown check: no dropdown field " + which + " in the shown dialog");
                return;
            }
            var field = fields[which];
            var before = field.value;
            // Trace: every focus change and press in the panel while the check runs, to see what happens to the list.
            panel.visualTree.RegisterCallback<FocusOutEvent>(e => Debug.Log("Dropdown trace: FocusOut " + Describe(e.target as VisualElement) + " -> " + Describe(e.relatedTarget as VisualElement)), TrickleDown.TrickleDown);
            panel.visualTree.RegisterCallback<FocusInEvent>(e => Debug.Log("Dropdown trace: FocusIn " + Describe(e.target as VisualElement) + " <- " + Describe(e.relatedTarget as VisualElement)), TrickleDown.TrickleDown);
            field.Focus();
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = field;
                field.SendEvent(submit);
            }
            rootElement.schedule.Execute(() =>
            {
                VisualElement menu = null;
                panel.visualTree.Query<VisualElement>().ForEach(e =>
                {
                    foreach (var name in e.GetClasses())
                        if (menu == null && name.StartsWith(OverlayRegistry.UnityDropdownClassPrefix, System.StringComparison.Ordinal) && name.EndsWith("outer", System.StringComparison.Ordinal))
                            menu = e;
                });
                if (menu == null)
                {
                    Debug.Log("Dropdown check: the list did not open");
                    return;
                }
                var chain = string.Empty;
                for (var element = menu; element != null; element = element.parent)
                    chain += (chain.Length == 0 ? string.Empty : " < ") + element.GetType().Name + (string.IsNullOrEmpty(element.name) ? string.Empty : "#" + element.name);
                // The rows: focusable descendants of the list (the focus moves to one as soon as the pointer hovers it).
                var rows = menu.Query<VisualElement>().Where(e => e.focusable && e != menu).ToList();
                var rowClasses = rows.Count == 0 ? "none" : string.Join(" ", rows[0].GetClasses());
                var inShell = rootElement.Contains(menu);
                Debug.Log("Dropdown check: list open (" + (inShell ? "below" : "outside") + " the shell root, " + rows.Count + " focusable rows (first: " + rowClasses +
                          "), popup element " + OverlayRegistry.IsPopupElement(menu, rootElement) + "), parents " + chain + "; field " + field.name + " was '" + before + "'");
                var row = rows.Count > 1 ? rows[1] : (rows.Count > 0 ? rows[0] : null);
                row?.Focus();
                if (row != null)
                {
                    using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = row.worldBound.center }))
                    {
                        move.target = row;
                        row.SendEvent(move);
                    }
                }
                rootElement.schedule.Execute(() =>
                {
                    var focused = panel.focusController?.focusedElement as VisualElement;
                    var stillOpen = menu.panel != null && menu.resolvedStyle.display != DisplayStyle.None;
                    Debug.Log("Dropdown check: after the focus moved to a row (as hovering does) the list is " + (stillOpen ? "still open" : "closed") + ", focus on " +
                              (focused == null ? "nothing" : focused.GetType().Name + " '" + focused.name + "'") + ", dialog shown " + AnyModalDialogShown());
                    if (System.Array.IndexOf(args, "-dropdownEscape") >= 0)
                    {
                        var keyTarget = focused ?? rootElement;
                        using (var press2 = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
                        {
                            press2.target = keyTarget;
                            keyTarget.SendEvent(press2);
                        }
                        // UI Toolkit turns Esc into this navigation event for the focused element; sent by hand it is not translated.
                        using (var cancel = NavigationCancelEvent.GetPooled())
                        {
                            cancel.target = keyTarget;
                            keyTarget.SendEvent(cancel);
                        }
                        rootElement.schedule.Execute(() => Debug.Log("Dropdown check: after Esc the list is " +
                            (menu.panel != null && menu.resolvedStyle.display != DisplayStyle.None ? "still open" : "closed") + ", dialog shown " + AnyModalDialogShown())).StartingIn(150);
                    }
                }).StartingIn(150);
            }).StartingIn(150);
        }

        /// <summary>
        /// For -clickMap: presses the left button at a point (the middle of the window by default) the way the pointer does and logs
        /// what the panel picked there, who has the focus afterwards, and whether a dialog is still shown.
        /// </summary>
        private void RunClickMapCheck()
        {
            var panel = rootElement.panel;
            var args = System.Environment.GetCommandLineArgs();
            var at = System.Array.IndexOf(args, "-clickMap");
            var position = new Vector2(rootElement.layout.width / 2f, rootElement.layout.height / 2f);
            if (at >= 0 && at + 2 < args.Length && float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                && float.TryParse(args[at + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                position = new Vector2(x, y);
            var picked = panel.Pick(position);
            var chain = string.Empty;
            for (var element = picked; element != null; element = element.parent)
                chain += (chain.Length == 0 ? string.Empty : " < ") + element.GetType().Name + (string.IsNullOrEmpty(element.name) ? string.Empty : "#" + element.name);
            var dialogBefore = AnyModalDialogShown();
            var selectionBefore = selection.Count;
            var menusBefore = OpenBarMenus();
            if (picked != null)
            {
                using (var press = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = position, button = 0 }))
                {
                    press.target = picked;
                    picked.SendEvent(press);
                }
                // -clickRelease also lets the button go, so a button of the menu bar acts as after a whole click.
                if (System.Array.IndexOf(args, "-clickRelease") >= 0)
                {
                    using (var release = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = position, button = 0 }))
                    {
                        release.target = picked;
                        picked.SendEvent(release);
                    }
                }
            }
            var focused = panel.focusController?.focusedElement as VisualElement;
            Debug.Log("Click check: picked " + (chain.Length == 0 ? "nothing" : chain) + "; focus on " +
                      (focused == null ? "nothing" : focused.GetType().Name + " '" + focused.name + "'") + (focused == mapCanvas ? " (the map)" : string.Empty) +
                      "; dialog shown before " + dialogBefore + ", after " + AnyModalDialogShown() + "; selection " + selectionBefore + " -> " + selection.Count +
                      "; menu bar menus open before [" + menusBefore + "], after [" + OpenBarMenus() + "]");
        }

        /// <summary>The names of the menus of the menu bar that are shown (for the click check).</summary>
        private string OpenBarMenus()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var (menuName, _, _) in MenuButtons)
                if (IsShown(rootElement.Q<VisualElement>(menuName)))
                    names.Add(menuName);
            return string.Join(", ", names);
        }
    }
}
