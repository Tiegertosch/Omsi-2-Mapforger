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

        /// <summary>
        /// Builds a modal dialog, adds it to the shell and registers it for Esc and the shortcut guard in one step (M7): a dialog
        /// built here cannot be left unregistered. Esc does what the cancel button does (<see cref="ModalDialog.OnEscaped"/>); focus
        /// goes back to the map when the element focused before is gone.
        /// </summary>
        private ModalDialog NewDialog(DialogOptions options)
        {
            if (rootElement == null)
                throw new InvalidOperationException("A dialog can only be built after the shell root exists.");
            if (options.ReturnFocus == null)
                options.ReturnFocus = () => mapCanvas?.Focus();
            var dialog = ModalDialog.Create(options);
            rootElement.Add(dialog.Overlay);
            RegisterOverlay(dialog.Overlay, true, dialog.OnEscaped);
            return dialog;
        }

        /// <summary>Whether a dialog that takes the keys is open: every registered overlay but those that float beside the map.</summary>
        private bool AnyModalDialogShown()
        {
            return overlays.AnyModalShown();
        }

        /// <summary>Esc closes the topmost overlay: a dialog or window first, then the list popup above the bottom bar.</summary>
        private bool CloseTopOverlay()
        {
            // A modal dialog first and alone: the windows beside the map stay until the next Esc (M7, decision 3A).
            if (overlays.CloseTopModal())
                return true;
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
