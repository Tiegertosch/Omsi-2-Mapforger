using System;
using System.Globalization;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The window of Help > Check for updates... (ADR 054): looking on GitHub, the newest version, a new release with its
    /// notes and Install, the download's progress, or what went wrong. The shell does the work and calls the Show methods.
    /// </summary>
    internal sealed class UpdateDialog : VisualElement
    {
        private readonly Label titleLabel = new Label();
        private readonly VisualElement messageRow = new VisualElement();
        private readonly SpinnerElement spinner = new SpinnerElement();
        private readonly Label messageLabel = new Label();
        private readonly ScrollView notes = new ScrollView(ScrollViewMode.Vertical);
        private readonly Label notesLabel = new Label();
        private readonly VisualElement progressTrack = new VisualElement();
        private readonly VisualElement progressFill = new VisualElement();
        private readonly Label hintLabel = new Label();
        private readonly Toggle atStartToggle = new Toggle { text = "Check for updates when MapWorks starts" };
        private readonly Button skipButton;
        private readonly Button closeButton;
        private readonly Button primaryButton;
        private Action primaryAction;

        public UpdateDialog()
        {
            AddToClassList("dialog");
            AddToClassList("update-dialog");
            titleLabel.AddToClassList("popup-title");
            Add(titleLabel);
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
            atStartToggle.AddToClassList("update-toggle");
            atStartToggle.tooltip = "Once a day at most, MapWorks asks GitHub for a new release when it starts, and only says something when there is one";
            atStartToggle.RegisterValueChangedCallback(evt => CheckAtStartChanged?.Invoke(evt.newValue));
            Add(atStartToggle);

            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            skipButton = new Button(() => SkipRequested?.Invoke()) { text = "Skip this version", tooltip = "Do not mention this version again when MapWorks starts" };
            skipButton.AddToClassList("map-control-button");
            skipButton.AddToClassList("update-skip");
            closeButton = new Button(() => CloseRequested?.Invoke()) { text = "Close" };
            closeButton.AddToClassList("map-control-button");
            primaryButton = new Button(() => primaryAction?.Invoke());
            primaryButton.AddToClassList("map-control-button");
            primaryButton.AddToClassList("dialog-primary");
            buttons.Add(skipButton);
            buttons.Add(closeButton);
            buttons.Add(primaryButton);
            Add(buttons);
        }

        public event Action InstallRequested;
        public event Action OpenPageRequested;
        public event Action SkipRequested;

        /// <summary>Close, Later or Cancel: the shell hides the window and stops a download.</summary>
        public event Action CloseRequested;

        public event Action<bool> CheckAtStartChanged;

        public void SetCheckAtStart(bool value)
        {
            atStartToggle.SetValueWithoutNotify(value);
        }

        public void ShowChecking()
        {
            Show("Check for updates", "Looking for a new version on GitHub...", true);
            Buttons("Close", null, null, false);
        }

        public void ShowCurrent(string version)
        {
            Show("Check for updates", "You have the newest version, " + version + ".", false);
            Buttons("Close", null, null, false);
        }

        public void ShowNoRelease()
        {
            Show("Check for updates", "There is no release on GitHub yet.", false);
            Buttons("Close", null, null, false);
        }

        /// <summary>A newer release; without <paramref name="installProblem"/> MapWorks can install it itself.</summary>
        public void ShowAvailable(MapWorksRelease release, string currentVersion, string installProblem)
        {
            var published = release.Published.HasValue
                ? " It was published on " + release.Published.Value.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + "."
                : string.Empty;
            Show("MapWorks " + release.Version + " is available", "You have " + currentVersion + "." + published, false);
            notesLabel.text = string.IsNullOrWhiteSpace(release.Notes) ? "The release has no notes." : MapWorksUpdates.NotesAsText(release.Notes);
            notes.style.display = DisplayStyle.Flex;
            if (installProblem == null)
            {
                hintLabel.text = "Install downloads " + release.AssetName + " (" + Megabytes(release.AssetSize) + "), checks it, closes MapWorks and starts the new " +
                                 "version with the open project. Projects, maps and settings stay as they are.";
                Buttons("Later", "Install and restart", () => InstallRequested?.Invoke(), true);
            }
            else
            {
                hintLabel.text = installProblem + " The release page has the download.";
                Buttons("Later", "Open release page", () => OpenPageRequested?.Invoke(), true);
            }
            hintLabel.style.display = DisplayStyle.Flex;
        }

        public void ShowDownloading(string version, long done, long total)
        {
            Show("Installing MapWorks " + version, total > 0
                ? "Downloading " + Megabytes(done) + " of " + Megabytes(total) + "..."
                : "Downloading " + Megabytes(done) + "...", true);
            progressTrack.style.display = DisplayStyle.Flex;
            progressFill.style.width = Length.Percent(total > 0 ? Math.Min(100f, 100f * done / total) : 0f);
            Buttons("Cancel", null, null, false);
        }

        public void ShowInstalling(string version)
        {
            Show("Installing MapWorks " + version, "MapWorks closes now and starts again with the new version.", true);
            Buttons(null, null, null, false);
        }

        /// <summary>Something went wrong; with a release page, a button opens it.</summary>
        public void ShowFailed(string title, string message, bool withPage)
        {
            Show(title, message, false);
            messageLabel.AddToClassList("update-error");
            Buttons("Close", withPage ? "Open release page" : null, withPage ? () => OpenPageRequested?.Invoke() : (Action)null, false);
        }

        private void Show(string title, string message, bool busy)
        {
            titleLabel.text = title;
            messageLabel.text = message;
            messageLabel.RemoveFromClassList("update-error");
            spinner.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            notes.style.display = DisplayStyle.None;
            progressTrack.style.display = DisplayStyle.None;
            hintLabel.style.display = DisplayStyle.None;
        }

        private void Buttons(string close, string primary, Action action, bool skip)
        {
            closeButton.text = close ?? string.Empty;
            closeButton.style.display = close != null ? DisplayStyle.Flex : DisplayStyle.None;
            primaryButton.text = primary ?? string.Empty;
            primaryButton.style.display = primary != null ? DisplayStyle.Flex : DisplayStyle.None;
            primaryAction = action;
            skipButton.style.display = skip ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
