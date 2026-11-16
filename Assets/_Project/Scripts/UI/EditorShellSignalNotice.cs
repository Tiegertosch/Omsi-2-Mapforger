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
        private VisualElement signalNoticeDialog;

        private void ShowSignalDraftDroppedNotice()
        {
            if (rootElement == null)
                return;
            if (signalNoticeDialog == null)
            {
                signalNoticeDialog = new VisualElement { name = "signal-notice-dialog" };
                signalNoticeDialog.AddToClassList("dialog-overlay");
                signalNoticeDialog.style.display = DisplayStyle.None;
                RegisterOverlay(signalNoticeDialog, true);
                var dialog = new VisualElement();
                dialog.AddToClassList("dialog");
                var title = LocBindings.NewLabel("signals.notice.title");
                title.AddToClassList("popup-title");
                dialog.Add(title);
                var text = LocBindings.NewLabel("signals.notice.text");
                text.AddToClassList("popup-description");
                dialog.Add(text);
                var buttons = new VisualElement();
                buttons.AddToClassList("dialog-buttons");
                var ok = LocBindings.NewButton("signals.notice.ok", () => HidePopup(signalNoticeDialog));
                ok.AddToClassList("map-control-button");
                ok.AddToClassList("dialog-primary");
                buttons.Add(ok);
                dialog.Add(buttons);
                signalNoticeDialog.Add(dialog);
                rootElement.Add(signalNoticeDialog);
            }
            signalNoticeDialog.style.display = DisplayStyle.Flex;
        }
    }
}
