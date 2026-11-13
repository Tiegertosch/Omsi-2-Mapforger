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
