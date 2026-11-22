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
                root.RegisterCallback<AttachToPanelEvent>(evt => ((VisualElement)evt.currentTarget).panel.visualTree
                    .RegisterCallback<KeyDownEvent>(OnPanelRootKey, TrickleDown.TrickleDown));
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
            return AnyModalDialogShown() && !InsideShownModal(target);
        }

        /// <summary>The focus moves from inside a modal dialog to an element outside every one (the map, a bar button): it goes back in.</summary>
        private void OnFocusLeavesModal(FocusOutEvent evt)
        {
            if (!(evt.relatedTarget is VisualElement next) || !AnyModalDialogShown() || InsideShownModal(next) || !InsideShownModal(evt.target as VisualElement))
                return;
            rootElement.schedule.Execute(() =>
            {
                if (overlays.TopShownModal() is VisualElement top && top.panel != null && !InsideShownModal(top.panel.focusController?.focusedElement as VisualElement))
                    top.Focus();
            });
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
            if (picked != null)
            {
                using (var press = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = position, button = 0 }))
                {
                    press.target = picked;
                    picked.SendEvent(press);
                }
            }
            var focused = panel.focusController?.focusedElement as VisualElement;
            Debug.Log("Click check: picked " + (chain.Length == 0 ? "nothing" : chain) + "; focus on " +
                      (focused == null ? "nothing" : focused.GetType().Name + " '" + focused.name + "'") + (focused == mapCanvas ? " (the map)" : string.Empty) +
                      "; dialog shown before " + dialogBefore + ", after " + AnyModalDialogShown() + "; selection " + selectionBefore + " -> " + selection.Count);
        }
    }
}
