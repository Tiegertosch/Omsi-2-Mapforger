using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The notice for the one case in which changes not applied in the signals window cannot wait for an answer: the map is read again
    /// by the shell itself (after a step whose roll-back failed), so the crossing goes away under the draft. Every way the person starts
    /// asks first (<see cref="SignalToolMayYield"/>); this notice says that the draft was dropped instead of dropping it silently.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private ModalDialog signalNoticeDialog;

        private void ShowSignalDraftDroppedNotice()
        {
            if (rootElement == null)
                return;
            if (signalNoticeDialog == null)
            {
                signalNoticeDialog = NewDialog(new DialogOptions
                {
                    Name = "signal-notice-dialog",
                    TitleKey = "signals.notice.title",
                    DescriptionKey = "signals.notice.text",
                    Width = DialogWidth.Narrow,
                    BodyScrolls = false,
                    Buttons = new[] { new DialogButton { TextKey = "signals.notice.ok", Role = DialogButtonRole.Primary } }
                });
            }
            signalNoticeDialog.Show();
        }
    }
}
