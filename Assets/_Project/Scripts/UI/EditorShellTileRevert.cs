namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The question before a tile goes back to the original map (M8 step 5): every way to revert (context menu, inspector button)
    /// asks first. The revert is one step of the history, so the text says that Undo brings the edits back; nothing is cleared.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private ModalDialog revertTileDialog;

        /// <summary>Asks whether the selected tile's edits are discarded; <see cref="RevertSelectedTileNow"/> runs only after the danger button.</summary>
        private void RevertSelectedTile()
        {
            if (selectedTile == null || rootElement == null)
                return;
            // The same refusals as the revert itself, so a read-only map or an added tile gets the status line and no question.
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.revert_tiles"));
                return;
            }
            if (session.IsTileAdded(selectedTile.FileName))
            {
                SetStatusT("tile.status.added_here", ("file", selectedTile.FileName));
                return;
            }
            if (revertTileDialog == null)
            {
                revertTileDialog = NewDialog(new DialogOptions
                {
                    Name = "revert-tile-dialog",
                    TitleKey = "tile.revert.title",
                    DescriptionKey = "tile.revert.text",
                    Width = DialogWidth.Narrow,
                    BodyScrolls = false,
                    Buttons = new[]
                    {
                        new DialogButton { TextKey = "common.cancel", Role = DialogButtonRole.Cancel, Name = "revert-tile-cancel" },
                        new DialogButton { TextKey = "tile.revert.confirm", Role = DialogButtonRole.Danger, Name = "revert-tile-confirm", Clicked = RevertSelectedTileNow }
                    }
                });
            }
            revertTileDialog.Show();
        }
    }
}
