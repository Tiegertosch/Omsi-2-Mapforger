using System;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The dialogs and windows that Esc closes and that may take the keys: each registers where it is made (<see cref="RegisterOverlay"/>),
    /// so a new dialog closes with Esc and blocks the shortcuts without being added to a list here.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private readonly OverlayRegistry overlays = new OverlayRegistry();

        /// <summary>
        /// Registers a dialog or window for Esc and the shortcut guard. Null (missing from the UXML) and a second registration are
        /// ignored. The clean-ups run in the order of registration, so the window for pictures from the air (registered in
        /// <c>InitializeSatellite</c>) cleans up before the update dialog (<c>InitializeUpdates</c>).
        /// </summary>
        /// <param name="modal">Whether the overlay takes the keys while shown; false for a window that floats beside the map.</param>
        /// <param name="closed">What has to happen after Esc hid the overlay; null for nothing.</param>
        private void RegisterOverlay(VisualElement element, bool modal, Action closed = null)
        {
            overlays.Register(element, modal, closed);
        }

        /// <summary>Whether a dialog that takes the keys is open: every registered overlay but those that float beside the map.</summary>
        private bool AnyModalDialogShown()
        {
            return overlays.AnyModalShown();
        }

        /// <summary>Esc closes the topmost overlay: a dialog or window first, then the list popup above the bottom bar.</summary>
        private bool CloseTopOverlay()
        {
            if (overlays.CloseAllShown())
                return true;
            if (barPopupOpen)
            {
                ShowBarPopup(false);
                return true;
            }
            return false;
        }
    }
}
