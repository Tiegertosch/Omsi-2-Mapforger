using System;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The window of Help > Check for updates... (ADR 054): looking on GitHub, the newest version, a new release with its
    /// notes and Install, the download's progress, or what went wrong. The shell does the work and calls the Show methods. The form is
    /// the body of a <see cref="ModalDialog"/> (M7 step 5), which owns the title and the row of three buttons (Skip, Close/Later/Cancel,
    /// and the main action); the form changes their texts and shows or hides them for each state.
    /// </summary>
    internal sealed class UpdateDialog : VisualElement
    {
        private readonly ModalDialog host;
        private readonly VisualElement messageRow = new VisualElement();
        private readonly SpinnerElement spinner = new SpinnerElement();
        private readonly Label messageLabel = new Label();
        private readonly ScrollView notes = new ScrollView(ScrollViewMode.Vertical);
        private readonly Label notesLabel = new Label();
        private readonly VisualElement progressTrack = new VisualElement();
        private readonly VisualElement progressFill = new VisualElement();
        private readonly Label hintLabel = new Label();
        private readonly Toggle atStartToggle = new Toggle();
        private readonly Button skipButton;
        private readonly Button closeButton;
        private readonly Button primaryButton;
        private Action primaryAction;

        /// <summary>The three buttons of <paramref name="host"/> in the order they stand: Skip, Close (cancel role), main action.</summary>
        public UpdateDialog(ModalDialog host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            skipButton = host.Buttons[0];
            closeButton = host.Buttons[1];
            primaryButton = host.Buttons[2];
            LocBindings.Tooltip(skipButton, "updates.skip.tooltip");
            skipButton.AddToClassList("update-skip");
            // The texts of Close and the main action change with the state and are written ready translated by the Show methods.
            LocBindings.ClearText(closeButton);
            LocBindings.ClearText(primaryButton);
            LocBindings.ClearText(host.Title);
            messageRow.AddToClassList("update-message-row");
            spinner.AddToClassList("update-spinner");
            messageRow.Add(spinner);
            messageLabel.AddToClassList("popup-description");
            messageLabel.AddToClassList("update-message");
            messageRow.Add(messageLabel);
            Add(messageRow);
            notes.AddToClassList("update-notes");
            notesLabel.AddToClassList("update-notes-text");
            notesLabel.selection.isSelectable = true;
            notes.Add(notesLabel);
            Add(notes);
            progressTrack.AddToClassList("update-progress");
            progressFill.AddToClassList("update-progress-fill");
            progressTrack.Add(progressFill);
            Add(progressTrack);
            hintLabel.AddToClassList("update-hint");
            Add(hintLabel);
            atStartToggle.AddToClassList("form-toggle");
            LocBindings.Text(atStartToggle, "updates.at_start");
            LocBindings.Tooltip(atStartToggle, "updates.at_start.tooltip");
            atStartToggle.RegisterValueChangedCallback(evt => CheckAtStartChanged?.Invoke(evt.newValue));
            Add(atStartToggle);
        }

        public event Action InstallRequested;
        public event Action OpenPageRequested;

        /// <summary>The skip button was pressed: the shell remembers the release and closes.</summary>
        public event Action SkipRequested;

        /// <summary>Runs what the main action button does in the state shown (Install or Open page).</summary>
        public void RunPrimary()
        {
            primaryAction?.Invoke();
        }

        /// <summary>Runs what the skip button does.</summary>
        public void RunSkip()
        {
            SkipRequested?.Invoke();
        }

        /// <summary>Focuses the main action, or Close when the state has none, so Tab and Enter start in the dialog.</summary>
        public void FocusDefault()
        {
            schedule.Execute(() => (primaryButton.resolvedStyle.display != DisplayStyle.None ? primaryButton : closeButton).Focus());
        }

        public event Action<bool> CheckAtStartChanged;

        public void SetCheckAtStart(bool value)
        {
            atStartToggle.SetValueWithoutNotify(value);
        }

        public void ShowChecking()
        {
            Show(Loc.T("updates.title.check"), Loc.T("updates.checking"), true);
            Buttons(Loc.T("updates.button.close"), null, null, false, true);
        }

        public void ShowCurrent(string version)
        {
            Show(Loc.T("updates.title.check"), Loc.T("updates.current", ("version", version)), false);
            Buttons(Loc.T("updates.button.close"), null, null, false, true);
        }

        public void ShowNoRelease()
        {
            Show(Loc.T("updates.title.check"), Loc.T("updates.no_release"), false);
            Buttons(Loc.T("updates.button.close"), null, null, false, true);
        }

        /// <summary>A newer release; without <paramref name="installProblem"/> MapWorks can install it itself.</summary>
        public void ShowAvailable(MapWorksRelease release, string currentVersion, string installProblem)
        {
            var published = release.Published.HasValue
                ? " " + Loc.T("updates.available.published", ("published", release.Published.Value.ToLocalTime()))
                : string.Empty;
            Show(Loc.T("updates.available.title", ("version", release.Version)), Loc.T("updates.available.current", ("version", currentVersion)) + published, false);
            notesLabel.text = string.IsNullOrWhiteSpace(release.Notes) ? Loc.T("updates.notes.none") : MapWorksUpdates.NotesAsText(release.Notes);
            notes.style.display = DisplayStyle.Flex;
            if (installProblem == null)
            {
                hintLabel.text = Loc.T("updates.install.hint", ("asset", release.AssetName), ("size", Megabytes(release.AssetSize)));
                Buttons(Loc.T("updates.button.later"), Loc.T("updates.button.install"), () => InstallRequested?.Invoke(), true, false);
            }
            else
            {
                hintLabel.text = Loc.T("updates.install.problem", ("problem", installProblem));
                Buttons(Loc.T("updates.button.later"), Loc.T("updates.button.open_page"), () => OpenPageRequested?.Invoke(), true, false);
            }
            hintLabel.style.display = DisplayStyle.Flex;
        }

        public void ShowDownloading(string version, long done, long total)
        {
            Show(Loc.T("updates.installing.title", ("version", version)), total > 0
                ? Loc.T("updates.downloading.progress", ("done", Megabytes(done)), ("total", Megabytes(total)))
                : Loc.T("updates.downloading", ("done", Megabytes(done))), true);
            progressTrack.style.display = DisplayStyle.Flex;
            progressFill.style.width = Length.Percent(total > 0 ? Math.Min(100f, 100f * done / total) : 0f);
            Buttons(Loc.T("updates.button.cancel"), null, null, false, false);
        }

        public void ShowInstalling(string version)
        {
            Show(Loc.T("updates.installing.title", ("version", version)), Loc.T("updates.installing.message"), true);
            Buttons(null, null, null, false, false);
        }

        /// <summary>Something went wrong; with a release page, a button opens it.</summary>
        public void ShowFailed(string title, string message, bool withPage)
        {
            Show(title, message, false);
            messageLabel.AddToClassList("update-error");
            Buttons(Loc.T("updates.button.close"), withPage ? Loc.T("updates.button.open_page") : null, withPage ? () => OpenPageRequested?.Invoke() : (Action)null, false, !withPage);
        }

        private void Show(string title, string message, bool busy)
        {
            host.Title.text = title;
            messageLabel.text = message;
            messageLabel.RemoveFromClassList("update-error");
            spinner.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            notes.style.display = DisplayStyle.None;
            progressTrack.style.display = DisplayStyle.None;
            hintLabel.style.display = DisplayStyle.None;
        }

        /// <param name="closeIsMain">Close stands alone as the way out and takes the main action's look (orange), as the single button of every other dialog does.</param>
        private void Buttons(string close, string primary, Action action, bool skip, bool closeIsMain)
        {
            closeButton.text = close ?? string.Empty;
            closeButton.style.display = close != null ? DisplayStyle.Flex : DisplayStyle.None;
            closeButton.EnableInClassList("dialog-primary", closeIsMain && primary == null);
            primaryButton.text = primary ?? string.Empty;
            primaryButton.style.display = primary != null ? DisplayStyle.Flex : DisplayStyle.None;
            primaryAction = action;
            skipButton.style.display = skip ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static string Megabytes(long bytes)
        {
            return Loc.T("updates.megabytes", ("size", bytes / (1024.0 * 1024.0)));
        }
    }
}
