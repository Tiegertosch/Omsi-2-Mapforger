using System;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>What saving a road type gave: the path it is saved under, or the text of why it failed (the draft stays either way).</summary>
    internal sealed class RoadSaveResult
    {
        public string Path;

        /// <summary>The failure as translated text for the window; null when the road type was saved.</summary>
        public string Error;

        public static RoadSaveResult Saved(string path)
        {
            return new RoadSaveResult { Path = path };
        }

        public static RoadSaveResult Failed(string error)
        {
            return new RoadSaveResult { Error = error };
        }
    }

    /// <summary>
    /// Saving, deleting and the state of the saved road type in the road type window (C8, owner's decisions of 18 November 2026): Save road type
    /// asks first when the open map uses the road type, Save as new type is the way for a road type outside the project's folder and offers a
    /// name, Delete asks before it goes, a road type that changed under the draft (undo on the map) is marked stale with ways out, and a failed
    /// save keeps the draft and says why. One bar between the work area and the footer carries every question and message.
    /// </summary>
    internal sealed partial class RoadTypeWindow
    {
        private RoadNotice pending;
        private string pendingText = string.Empty;
        private Action retry;
        private bool savedMissing;
        private VisualElement noticeRow;
        private Label noticeLabel;
        private TextField noticeName;
        private Button noticeFirst;
        private Button noticeSecond;
        private Button noticeThird;
        private Button saveAsNewButton;
        private Button deleteButton;
        private Action noticeFirstAction;
        private Action noticeSecondAction;
        private Action noticeThirdAction;

        // ----- what the shell supplies -----

        /// <summary>Makes a new road type from the name and the road type; set by the shell, which writes the files.</summary>
        public Func<string, OmsiRoadType, RoadSaveResult> CreateHandler { get; set; }

        /// <summary>Writes the saved road type anew, with everything that uses it in the map, as one undo step; set by the shell.</summary>
        public Func<OmsiRoadType, RoadSaveResult> ReplaceHandler { get; set; }

        /// <summary>What of the open map uses the road type being edited; set by the shell.</summary>
        public Func<OmsiRoadTypeUsage> UsageProvider { get; set; }

        /// <summary>The person confirmed Delete: the shell searches the other maps and deletes, then tells the window by <see cref="DeleteFailed"/> or resets it.</summary>
        public event Action DeleteRequested;

        /// <summary>Reload for a stale draft: the shell reads the saved road type again and opens it with <see cref="Edit"/>.</summary>
        public event Action ReloadRequested;

        /// <summary>Whether the road type lies in the open project's own folder, so it may be overwritten and deleted.</summary>
        public bool OwnedByProject => !draft.IsNew && OmsiRoadTypeOwnership.InProjectFolder(draft.AssetPath, projectName);

        /// <summary>What the bar shows now.</summary>
        public RoadNotice Notice => RoadTypeWindowRules.Notice(pending, draft.IsNew, draft.IsStale, savedMissing, OwnedByProject);

        public bool IsSaving => saving;

        // ----- building -----

        private void BuildNotice()
        {
            noticeRow = new VisualElement();
            noticeRow.AddToClassList("road-window-notice");
            noticeRow.style.display = DisplayStyle.None;
            noticeLabel = new Label();
            noticeLabel.AddToClassList("signal-prompt-text");
            noticeRow.Add(noticeLabel);
            noticeName = new TextField { maxLength = 60 };
            noticeName.AddToClassList("picker-field");
            noticeName.AddToClassList("road-window-notice-name");
            noticeName.RegisterValueChangedCallback(_ => RenderNotice());
            noticeName.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                evt.StopPropagation();
                ConfirmName();
            });
            noticeRow.Add(noticeName);
            noticeFirst = NoticeButton(() => noticeFirstAction?.Invoke());
            noticeSecond = NoticeButton(() => noticeSecondAction?.Invoke());
            noticeThird = NoticeButton(() => noticeThirdAction?.Invoke());
            noticeRow.Add(noticeFirst);
            noticeRow.Add(noticeSecond);
            noticeRow.Add(noticeThird);
            Content.Add(noticeRow);
        }

        private static Button NoticeButton(Action clicked)
        {
            var button = new Button(clicked);
            button.AddToClassList("map-control-button");
            button.AddToClassList("signal-prompt-button");
            return button;
        }

        private void BuildDeleteButton(VisualElement row)
        {
            deleteButton = TextIconButton("trash-2", "roadbuilder.window.delete", "roadbuilder.window.delete.tooltip", AskDelete);
            deleteButton.AddToClassList("road-window-danger");
            deleteButton.AddToClassList("road-window-delete");
            row.Add(deleteButton);
        }

        /// <summary>Adds Save as new type to the footer, left of the main button. Called after the frame has made the footer.</summary>
        private void BuildSaveAsNewButton()
        {
            saveAsNewButton = new Button(SaveAsNewClicked);
            saveAsNewButton.AddToClassList("map-control-button");
            saveAsNewButton.AddToClassList("signal-footer-button");
            LocBindings.Text(saveAsNewButton, "roadbuilder.window.save_new");
            Footer.Insert(Footer.IndexOf(ApplyButton), saveAsNewButton);
        }

        // ----- the footer's buttons -----

        /// <summary>The main button: makes a new road type, writes a saved one of the project (after asking when the map uses it), or opens the name for a copy.</summary>
        private void Save()
        {
            if (Busy || PrimaryBlock() != RoadSaveBlock.None)
                return;
            switch (RoadTypeWindowRules.PrimarySave(draft.IsNew, OwnedByProject))
            {
                case RoadPrimarySave.Create:
                    RunCreate(RoadName, true);
                    break;
                case RoadPrimarySave.SaveAsNew:
                    OpenNameBar();
                    break;
                default:
                    var usage = UsageProvider?.Invoke();
                    if (usage != null && RoadTypeWindowRules.AsksBeforeOverwrite(usage.Pieces, usage.JunctionCount))
                    {
                        SetPending(RoadNotice.SaveChoice, Loc.T("roadbuilder.window.save.used", ("pieces", usage.Pieces), ("junctions", usage.JunctionCount)));
                        noticeFirstAction = UpdateAll;
                        noticeSecondAction = OpenNameBar;
                        noticeThirdAction = ClearNotice;
                    }
                    else
                        UpdateAll();
                    break;
            }
        }

        private void SaveAsNewClicked()
        {
            if (Busy || SaveAsNewBlockNow() != RoadSaveBlock.None)
                return;
            OpenNameBar();
        }

        /// <summary>A save is running, or the search before a delete: neither starts another.</summary>
        private bool Busy => saving || pending == RoadNotice.Searching;

        private RoadSaveBlock SaveAsNewBlockNow()
        {
            return RoadTypeWindowRules.SaveAsNewBlock(draft.Problems().Count, canEdit());
        }

        private RoadSaveBlock PrimaryBlock()
        {
            return RoadTypeWindowRules.PrimaryBlock(draft.IsNew, draft.IsDirty, draft.IsStale, draft.Problems().Count, canEdit(), OwnedByProject);
        }

        /// <summary>Update all: writes the saved road type anew, everything of the map that uses it changes with it.</summary>
        private void UpdateAll()
        {
            // A road type that went stale or foreign while the question stood is not written over.
            if (Busy || draft.IsNew || !OwnedByProject || PrimaryBlock() != RoadSaveBlock.None)
            {
                ClearNotice();
                return;
            }
            RunSave(() => ReplaceHandler?.Invoke(draft.ToRoadType()), UpdateAll);
        }

        /// <summary>The bar asks for the name of the new road type; the name field of the window stays the file name of the saved one (decision 4).</summary>
        private void OpenNameBar()
        {
            if (Busy || SaveAsNewBlockNow() != RoadSaveBlock.None)
                return;
            SetPending(RoadNotice.SaveName, Loc.T("roadbuilder.window.save_new.prompt"));
            noticeName.SetValueWithoutNotify(RoadTypeWindowRules.CopyName(draft.IsNew ? RoadName : System.IO.Path.GetFileNameWithoutExtension(draft.AssetPath), Loc.T("roadbuilder.window.save_new.copy")));
            noticeFirstAction = ConfirmName;
            noticeSecondAction = null;
            noticeThirdAction = ClearNotice;
            Render();
            noticeName.schedule.Execute(() => noticeName.Focus());
        }

        private void ConfirmName()
        {
            var name = noticeName.value?.Trim();
            if (Busy || pending != RoadNotice.SaveName || string.IsNullOrWhiteSpace(name) || SaveAsNewBlockNow() != RoadSaveBlock.None)
                return;
            RunCreate(name, false);
        }

        /// <summary>Makes a new road type; Try again after a failure comes back here, so the checks run once more against the draft as it is then.</summary>
        private void RunCreate(string name, bool primary)
        {
            var block = primary ? PrimaryBlock() : SaveAsNewBlockNow();
            if (Busy || block != RoadSaveBlock.None)
            {
                ClearNotice();
                return;
            }
            RunSave(() => CreateHandler?.Invoke(name, draft.ToRoadType()), () => RunCreate(name, primary));
        }

        /// <summary>
        /// Runs a save of the shell. A failure keeps the draft and shows what failed with Try again, which runs <paramref name="again"/> (a
        /// path that checks the draft anew); the shell wrote nothing then. A second click while it runs finds <see cref="saving"/> set.
        /// </summary>
        private void RunSave(Func<RoadSaveResult> action, Action again)
        {
            if (saving)
                return;
            saving = true;
            RoadSaveResult result;
            try
            {
                result = action();
            }
            finally
            {
                saving = false;
            }
            if (result == null || result.Error != null)
            {
                ShowFailure(result?.Error ?? Loc.T("roadbuilder.window.error.invalid", ("reason", string.Empty)), again);
                return;
            }
            Saved(result.Path);
        }

        private void ShowFailure(string text, Action again)
        {
            retry = again;
            SetPending(RoadNotice.Error, text);
            noticeFirstAction = () =>
            {
                var next = retry;
                ClearNotice();
                next?.Invoke();
            };
            noticeSecondAction = null;
            noticeThirdAction = ClearNotice;
            Render();
        }

        // ----- delete -----

        private void AskDelete()
        {
            if (Busy || !RoadTypeWindowRules.CanDelete(draft.IsNew, OwnedByProject, canEdit()))
                return;
            var usage = UsageProvider?.Invoke() ?? new OmsiRoadTypeUsage(0, null);
            var name = System.IO.Path.GetFileNameWithoutExtension(draft.AssetPath);
            if (!RoadTypeWindowRules.MayDelete(usage.Pieces, usage.JunctionCount))
            {
                SetPending(RoadNotice.DeleteBlocked, Loc.T("roadbuilder.window.delete.blocked", ("name", name), ("pieces", usage.Pieces), ("junctions", usage.JunctionCount)));
                noticeFirstAction = null;
                noticeSecondAction = null;
                noticeThirdAction = ClearNotice;
                Render();
                return;
            }
            SetPending(RoadNotice.DeleteConfirm, Loc.T("roadbuilder.window.delete.confirm", ("name", name)));
            noticeFirstAction = ConfirmDelete;
            noticeSecondAction = null;
            noticeThirdAction = ClearNotice;
            Render();
        }

        private void ConfirmDelete()
        {
            if (pending == RoadNotice.DeleteConfirm)
                StartDeleteSearch();
        }

        private void StartDeleteSearch()
        {
            // The search of the other maps takes a moment: the bar shows it and has no button, so a second click cannot start another.
            SetPending(RoadNotice.Searching, Loc.T("roadbuilder.window.delete.searching", ("name", System.IO.Path.GetFileNameWithoutExtension(draft.AssetPath))));
            noticeFirstAction = null;
            noticeSecondAction = null;
            noticeThirdAction = null;
            Render();
            DeleteRequested?.Invoke();
        }

        /// <summary>The shell could not delete: the road type stays and the bar says why (a map still names it, or the file could not be deleted, which can be tried again).</summary>
        public void DeleteFailed(string text, bool canTryAgain)
        {
            if (canTryAgain)
            {
                ShowFailure(text, () =>
                {
                    if (Busy || !RoadTypeWindowRules.CanDelete(draft.IsNew, OwnedByProject, canEdit()))
                        ClearNotice();
                    else
                        StartDeleteSearch();
                });
                return;
            }
            SetPending(RoadNotice.DeleteBlocked, text);
            noticeFirstAction = null;
            noticeSecondAction = null;
            noticeThirdAction = ClearNotice;
            Render();
        }

        // ----- the state of the saved road type -----

        /// <summary>
        /// The saved road type may have changed under the draft (undo or redo on the map): the draft is marked stale when the file reads
        /// otherwise than the draft's saved state, and no longer when it reads so again. A question about the old state goes away.
        /// </summary>
        /// <param name="onDisk">The road type as the file holds it now; null when the file is gone or holds none.</param>
        public void CheckSaved(OmsiRoadType onDisk)
        {
            if (draft.IsNew)
                return;
            savedMissing = onDisk == null;
            var stale = draft.CheckSaved(onDisk);
            if (stale && RoadTypeWindowRules.DropsPendingWhenStale(pending))
                pending = RoadNotice.None;
            if (!stale)
                savedMissing = false;
            Render();
            Changed?.Invoke();
        }

        private void ReloadClicked()
        {
            ReloadRequested?.Invoke();
        }

        // ----- drawing the bar and the buttons -----

        // The bar is drawn with the rest of the window (Render), which also keeps the keyboard focus in the window when the button that was
        // clicked goes away with its question.
        private void SetPending(RoadNotice kind, string text)
        {
            pending = kind;
            pendingText = text;
            Render();
        }

        /// <summary>Takes the question or message away; the state-based notice, if there is one, shows again.</summary>
        public void ClearNotice()
        {
            DropNotice();
            Render();
        }

        /// <summary>Forgets the question or message without drawing, for a caller that draws next.</summary>
        private void DropNotice()
        {
            pending = RoadNotice.None;
            pendingText = string.Empty;
            retry = null;
            noticeFirstAction = null;
            noticeSecondAction = null;
            noticeThirdAction = null;
        }

        private void RenderNotice()
        {
            if (draft == null || noticeRow == null)
                return;
            var notice = Notice;
            var name = draft.IsNew ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(draft.AssetPath);
            noticeRow.style.display = notice == RoadNotice.None ? DisplayStyle.None : DisplayStyle.Flex;
            noticeRow.EnableInClassList("road-window-notice-info", notice == RoadNotice.Foreign || notice == RoadNotice.Searching || notice == RoadNotice.SaveChoice ||
                                                                      notice == RoadNotice.SaveName || notice == RoadNotice.DeleteBlocked);
            noticeRow.EnableInClassList("road-window-notice-error", notice == RoadNotice.Error || notice == RoadNotice.DeleteConfirm);
            string text;
            switch (notice)
            {
                case RoadNotice.Stale:
                    text = Loc.T("roadbuilder.window.notice.stale");
                    noticeFirstAction = ReloadClicked;
                    noticeSecondAction = OpenNameBar;
                    noticeThirdAction = null;
                    break;
                case RoadNotice.Missing:
                    text = Loc.T("roadbuilder.window.notice.missing");
                    noticeFirstAction = null;
                    noticeSecondAction = OpenNameBar;
                    noticeThirdAction = null;
                    break;
                case RoadNotice.Foreign:
                    text = Loc.T("roadbuilder.window.notice.foreign", ("name", name));
                    noticeFirstAction = null;
                    noticeSecondAction = null;
                    noticeThirdAction = null;
                    break;
                default:
                    text = pendingText;
                    break;
            }
            noticeLabel.text = text;
            noticeLabel.tooltip = text;
            noticeName.style.display = notice == RoadNotice.SaveName ? DisplayStyle.Flex : DisplayStyle.None;
            var first = notice == RoadNotice.Stale ? "roadbuilder.window.notice.reload"
                : notice == RoadNotice.SaveChoice ? "roadbuilder.window.save.used.update"
                : notice == RoadNotice.SaveName ? "roadbuilder.window.save_new"
                : notice == RoadNotice.DeleteConfirm ? "roadbuilder.window.delete.confirm.button"
                : notice == RoadNotice.Error ? "roadbuilder.window.error.retry"
                : null;
            var second = notice == RoadNotice.Stale || notice == RoadNotice.Missing ? "roadbuilder.window.notice.keep"
                : notice == RoadNotice.SaveChoice ? "roadbuilder.window.save_new"
                : null;
            var third = notice == RoadNotice.SaveChoice || notice == RoadNotice.SaveName || notice == RoadNotice.DeleteConfirm ? "roadbuilder.window.notice.cancel"
                : notice == RoadNotice.Error ? "roadbuilder.window.error.dismiss"
                : notice == RoadNotice.DeleteBlocked ? "roadbuilder.window.notice.ok"
                : null;
            ShowNoticeButton(noticeFirst, first);
            ShowNoticeButton(noticeSecond, second);
            ShowNoticeButton(noticeThird, third);
            noticeFirst.EnableInClassList("dialog-primary", notice != RoadNotice.DeleteConfirm);
            noticeFirst.EnableInClassList("road-window-danger", notice == RoadNotice.DeleteConfirm);
            noticeFirst.SetEnabled(notice != RoadNotice.SaveName || !string.IsNullOrWhiteSpace(noticeName.value));
            noticeSecond.EnableInClassList("dialog-primary", false);
            // A second action that is not possible (no name, read-only) leaves its button disabled with the reason as its tooltip.
            var copyBlock = SaveAsNewBlockNow();
            noticeSecond.SetEnabled(copyBlock == RoadSaveBlock.None);
            noticeSecond.tooltip = copyBlock == RoadSaveBlock.None ? string.Empty : Loc.T(RoadTypeWindowRules.SaveBlockKey(copyBlock)); // i18n:keys
        }

        private static void ShowNoticeButton(Button button, string key)
        {
            button.style.display = key == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (key != null)
                button.text = Loc.T(key); // i18n:keys
        }

        /// <summary>The footer's buttons and the delete button as the draft's state allows them.</summary>
        private void RenderSaveButtons()
        {
            var owned = OwnedByProject;
            var primary = RoadTypeWindowRules.PrimarySave(draft.IsNew, owned);
            var block = PrimaryBlock();
            var copyBlock = SaveAsNewBlockNow();
            LocBindings.Text(ApplyButton, primary == RoadPrimarySave.SaveAsNew ? "roadbuilder.window.save_new" : "roadbuilder.window.save"); // i18n:keys
            ApplyButton.SetEnabled(block == RoadSaveBlock.None && !Busy);
            if (block != RoadSaveBlock.None)
                LocBindings.Tooltip(ApplyButton, RoadTypeWindowRules.SaveBlockKey(block)); // i18n:keys
            else if (primary == RoadPrimarySave.Create)
                LocBindings.Tooltip(ApplyButton, "roadbuilder.window.save.tooltip_new");
            else if (primary == RoadPrimarySave.SaveAsNew)
                LocBindings.Tooltip(ApplyButton, "roadbuilder.window.save_new.tooltip_foreign");
            else
                LocBindings.Tooltip(ApplyButton, "roadbuilder.window.save.tooltip_edit", ("name", RoadName), ("uses", uses));
            var showCopy = RoadTypeWindowRules.ShowsSaveAsNew(draft.IsNew, owned);
            saveAsNewButton.style.display = showCopy ? DisplayStyle.Flex : DisplayStyle.None;
            saveAsNewButton.SetEnabled(copyBlock == RoadSaveBlock.None && !Busy);
            if (copyBlock == RoadSaveBlock.None)
                LocBindings.Tooltip(saveAsNewButton, "roadbuilder.window.save_new.tooltip", ("name", RoadName));
            else
                LocBindings.Tooltip(saveAsNewButton, RoadTypeWindowRules.SaveBlockKey(copyBlock)); // i18n:keys
            var canDelete = RoadTypeWindowRules.CanDelete(draft.IsNew, owned, canEdit());
            deleteButton.style.display = !draft.IsNew && owned ? DisplayStyle.Flex : DisplayStyle.None;
            deleteButton.SetEnabled(canDelete && !Busy);
        }

        /// <summary>The remark of taking a road apart as translated text; the English sentence is for the log.</summary>
        internal static string NoteText(OmsiRoadTypeNote note)
        {
            var key = RoadTypeWindowRules.NoteKey(note.Code);
            switch (note.Code)
            {
                case OmsiRoadTypeNoteCode.WidthFitted:
                    return Loc.T(key, ("kind", Loc.T(RoadTypeWindowRules.LaneKey(note.Kind))), ("lane", note.LaneNumber), ("fitted", note.Fitted), ("original", note.Original)); // i18n:keys
                case OmsiRoadTypeNoteCode.LanesLimited:
                    return Loc.T(key, ("limit", note.Limit)); // i18n:keys
                default:
                    return Loc.T(key); // i18n:keys
            }
        }
    }
}
