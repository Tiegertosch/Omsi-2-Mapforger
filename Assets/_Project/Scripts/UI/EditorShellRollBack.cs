using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A step whose roll-back failed may have left part of itself in the files, so the map is read again. The session reports such a
    /// failure (<see cref="OmsiMapSession.RollBackFailed"/>) while its call is still unwinding and the caller has yet to show its
    /// status; the shell only takes note then and reads the map again in the next <see cref="LateUpdate"/>, whatever edit it was.
    /// Undo and redo read the map at once (<see cref="ReloadAfterFailedRollBack"/>), which clears the note, so no map is read twice.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private OmsiRollBackFailedException pendingRollBackFailure;
        private string rollBackNotedStatusKey;
        private (string Name, object Value)[] rollBackNotedStatusArguments;
        private string rollBackNotedStatusText;

        /// <summary>Makes <paramref name="next"/> the open session: only it reports failed roll-backs, and a note of the one leaving goes.</summary>
        private void UseSession(OmsiMapSession next)
        {
            if (session != null)
                session.RollBackFailed -= NoteRollBackFailure;
            session = next;
            if (session != null)
                session.RollBackFailed += NoteRollBackFailure;
            pendingRollBackFailure = null;
        }

        /// <summary>Notes a failed roll-back and the status shown then, so the next frame can tell whether the edit showed its failure.</summary>
        private void NoteRollBackFailure(OmsiRollBackFailedException failure)
        {
            if (failure == null || pendingRollBackFailure != null)
                return;
            pendingRollBackFailure = failure;
            rollBackNotedStatusKey = statusKey;
            rollBackNotedStatusArguments = statusArguments;
            rollBackNotedStatusText = statusLabel?.tooltip;
        }

        /// <summary>
        /// Reads the map again after a failed roll-back noted since the last frame. Reading it shows its own status, so the status the
        /// edit set afterwards is shown again, with a note that the map was read again and the history is empty, as that status may
        /// tell of success; when the edit set none, a status names the failure; when reading fails, a status says so.
        /// </summary>
        private void ReloadAfterPendingRollBack()
        {
            var failure = pendingRollBackFailure;
            if (failure == null)
                return;
            if (session == null)
            {
                pendingRollBackFailure = null;
                return;
            }
            var key = statusKey;
            var arguments = statusArguments;
            var text = statusLabel?.tooltip;
            var shownByEdit = key != rollBackNotedStatusKey || !ReferenceEquals(arguments, rollBackNotedStatusArguments) || text != rollBackNotedStatusText;
            Debug.LogWarning("Putting back the files of a failed step failed as well; the map is read again. " + failure.Message);
            if (!ReloadAfterFailedRollBack())
                SetStatusT("history.rollback_failed_reload_failed", ("reason", failure.Message));
            else if (!shownByEdit || key == null && text == null)
                SetStatusT("history.rollback_failed_reloaded", ("reason", failure.Message));
            else
                SetStatusT("history.reloaded_after_rollback_note", ("status", key != null ? Loc.T(key, arguments) /* i18n:keys */ : text));
        }
    }
}
