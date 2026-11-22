using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The dialogs and windows that close with Esc. Each registers once with whether it is modal (takes the keys while shown) and
    /// what has to happen after Esc hid it. Esc hides all of them at once and then runs the clean-up of those that were shown, in
    /// the order they registered.
    /// </summary>
    internal sealed class OverlayRegistry
    {
        private sealed class Entry
        {
            public VisualElement Element;
            public bool Modal;
            public Action Closed;
        }

        private readonly List<Entry> entries = new List<Entry>();

        /// <summary>Whether the element is shown: it exists and its resolved display is not None.</summary>
        public static bool IsShown(VisualElement element)
        {
            return element != null && element.resolvedStyle.display != DisplayStyle.None;
        }

        /// <summary>Hides the element through its inline style; null is ignored.</summary>
        public static void Hide(VisualElement element)
        {
            if (element != null)
                element.style.display = DisplayStyle.None;
        }

        /// <summary>The number of overlays registered.</summary>
        public int Count => entries.Count;

        /// <summary>
        /// Registers an overlay. A null element (missing from a stripped UXML) is ignored, as is an element registered before; both
        /// return false.
        /// </summary>
        /// <param name="modal">Whether the overlay takes the keys while it is shown, so the shortcuts do nothing.</param>
        /// <param name="closed">Runs after Esc hid the overlay, if it was shown; null for none.</param>
        public bool Register(VisualElement element, bool modal, Action closed = null)
        {
            if (element == null || IsRegistered(element))
                return false;
            entries.Add(new Entry { Element = element, Modal = modal, Closed = closed });
            return true;
        }

        public bool IsRegistered(VisualElement element)
        {
            foreach (var entry in entries)
                if (ReferenceEquals(entry.Element, element))
                    return true;
            return false;
        }

        /// <summary>Whether any registered overlay is shown.</summary>
        public bool AnyShown()
        {
            foreach (var entry in entries)
                if (IsShown(entry.Element))
                    return true;
            return false;
        }

        /// <summary>Whether a modal overlay is shown.</summary>
        public bool AnyModalShown()
        {
            foreach (var entry in entries)
                if (entry.Modal && IsShown(entry.Element))
                    return true;
            return false;
        }

        /// <summary>Whether the element is, or lies inside, a modal overlay that is shown.</summary>
        public bool IsInsideShownModal(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                foreach (var entry in entries)
                    if (entry.Modal && ReferenceEquals(entry.Element, current) && IsShown(current))
                        return true;
            return false;
        }

        /// <summary>Diagnostics only (-modalGuardStrict): switches the popup exemption off, to show the old behaviour in a check run.</summary>
        internal static bool PopupExemptionOff;

        /// <summary>The class prefix of Unity's dropdown menu (GenericDropdownMenu: "unity-base-dropdown", "...__container-outer", "...__item").</summary>
        internal const string UnityDropdownClassPrefix = "unity-base-dropdown";

        /// <summary>
        /// Whether the element belongs to a popup that is not part of any dialog's own tree: the list of a DropdownField, an enum or
        /// popup field, or a tooltip. Unity hangs these on the panel, outside the dialog that opened them, but they belong to it: the
        /// modal guard neither stops the pointer on them nor pulls the focus back from them (M7 step 5, correction: the language and
        /// interface size lists closed as soon as the pointer reached them). Two signs, either is enough: the element lies outside the
        /// shell root altogether (the structural sign, independent of Unity's class names), or an ancestor carries one of Unity's
        /// dropdown or tooltip classes (for a menu that Unity hangs below the shell root).
        /// </summary>
        public static bool IsPopupElement(VisualElement element, VisualElement shellRoot)
        {
            if (element == null || PopupExemptionOff)
                return false;
            if (shellRoot != null && element.panel != null && element != shellRoot && !shellRoot.Contains(element))
                return true;
            for (var current = element; current != null; current = current.parent)
            {
                foreach (var name in current.GetClasses())
                    if (name.StartsWith(UnityDropdownClassPrefix, StringComparison.Ordinal) || name == "unity-tooltip" || name == "unity-base-tooltip")
                        return true;
            }
            return false;
        }

        /// <summary>The modal overlay that is shown and stands highest in its parent (the one on top), or null.</summary>
        public VisualElement TopShownModal()
        {
            VisualElement top = null;
            var topIndex = -1;
            foreach (var entry in entries)
            {
                if (!entry.Modal || !IsShown(entry.Element) || entry.Element.parent == null)
                    continue;
                var index = entry.Element.parent.IndexOf(entry.Element);
                if (index > topIndex)
                {
                    top = entry.Element;
                    topIndex = index;
                }
            }
            return top;
        }

        /// <summary>
        /// If a modal overlay is shown: hides only the one on top (<see cref="TopShownModal"/>), runs its clean-up and returns true; the
        /// windows beside the map and any modal dialog below stay. Otherwise does nothing and returns false.
        /// </summary>
        public bool CloseTopModal()
        {
            var top = TopShownModal();
            if (top == null)
                return false;
            Hide(top);
            foreach (var entry in entries)
                if (ReferenceEquals(entry.Element, top))
                    entry.Closed?.Invoke();
            return true;
        }

        /// <summary>
        /// If any overlay is shown: hides every registered overlay, then runs the clean-up of those that were shown, in the order
        /// they registered, and returns true. Otherwise does nothing and returns false.
        /// </summary>
        public bool CloseAllShown()
        {
            var shown = new List<Entry>();
            foreach (var entry in entries)
                if (IsShown(entry.Element))
                    shown.Add(entry);
            if (shown.Count == 0)
                return false;
            foreach (var entry in entries)
                Hide(entry.Element);
            foreach (var entry in shown)
                entry.Closed?.Invoke();
            return true;
        }
    }
}
