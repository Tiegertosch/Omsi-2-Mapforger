using System;
using OmsiMapEditor.Core;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The form of Help > Report a bug... (ADR 053): a title, what happened, how to get there and what was expected. After
    /// a crash it opens by itself and says so. The shell sends it when <see cref="SendRequested"/> fires.
    /// </summary>
    internal sealed class BugReportDialog : VisualElement
    {
        private readonly Label titleLabel = new Label();
        private readonly Label introLabel = new Label();
        private readonly TextField titleField = new TextField();
        private readonly TextField whatField = new TextField { multiline = true };
        private readonly TextField stepsField = new TextField { multiline = true };
        private readonly TextField expectedField = new TextField { multiline = true };
        private readonly Toggle logToggle = new Toggle { value = true };
        private readonly Label includedLabel = new Label();
        private readonly Button sendButton;

        public BugReportDialog()
        {
            AddToClassList("dialog");
            AddToClassList("bug-report-dialog");
            titleLabel.AddToClassList("popup-title");
            Add(titleLabel);
            introLabel.AddToClassList("popup-description");
            Add(introLabel);

            AddField("Title", titleField, "One line that names the problem, such as \"Junction arms jump when turned\"", false);
            titleField.textSelection.selectAllOnFocus = false;
            titleField.RegisterValueChangedCallback(_ => UpdateSend());
            AddField("What happened", whatField, "What you saw, and any message MapWorks showed", true);
            AddField("Steps", stepsField, "What you did before, step by step, and which map; the more exact, the faster it can be fixed", true);
            AddField("Expected", expectedField, "What you expected to happen instead", true);

            var logRow = new VisualElement();
            logRow.AddToClassList("bug-report-row");
            var logLabel = new Label("Log");
            logLabel.AddToClassList("bug-report-label");
            logRow.Add(logLabel);
            logToggle.text = "Attach the log";
            logToggle.tooltip = "The end of MapWorks' log goes into the report; the whole log lies ready in a folder to drag into the issue";
            logToggle.AddToClassList("bug-report-toggle");
            logToggle.RegisterValueChangedCallback(_ => UpdateIncluded());
            logRow.Add(logToggle);
            Add(logRow);
            includedLabel.AddToClassList("bug-report-hint");
            Add(includedLabel);

            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = new Button(() => Cancelled?.Invoke()) { text = "Cancel" };
            cancel.AddToClassList("map-control-button");
            sendButton = new Button(() => SendRequested?.Invoke()) { text = "Open on GitHub", tooltip = "Opens GitHub's form for a new issue in your browser, filled in; nothing is sent before you press Submit there" };
            sendButton.AddToClassList("map-control-button");
            sendButton.AddToClassList("dialog-primary");
            buttons.Add(cancel);
            buttons.Add(sendButton);
            Add(buttons);
        }

        /// <summary>The report is to be opened on GitHub with the texts of <see cref="FillIn"/>.</summary>
        public event Action SendRequested;

        public event Action Cancelled;

        public bool IncludeLog => logToggle.value;

        public bool Crashed { get; private set; }

        /// <summary>Starts an empty report; after a crash its texts say so and its log is that of the crashed session.</summary>
        public void Open(bool crashed)
        {
            Crashed = crashed;
            titleLabel.text = crashed ? "MapWorks closed unexpectedly" : "Report a bug";
            introLabel.text = crashed
                ? "The last session ended without MapWorks closing normally, by a crash or because it had to be ended. A report helps to find out why: " +
                  "say what you were doing, and your browser opens GitHub with the report and the log of that session filled in."
                : "Describe the problem, and your browser opens a new issue on GitHub with your text, facts about your computer and the open map, " +
                  "and the log filled in. You need a GitHub account to send it.";
            titleField.SetValueWithoutNotify(crashed ? "Crash: " : string.Empty);
            whatField.SetValueWithoutNotify(string.Empty);
            stepsField.SetValueWithoutNotify(string.Empty);
            expectedField.SetValueWithoutNotify(string.Empty);
            logToggle.SetValueWithoutNotify(true);
            UpdateIncluded();
            UpdateSend();
            // The cursor goes behind "Crash: ", so typing adds to it.
            schedule.Execute(() =>
            {
                titleField.Focus();
                titleField.SelectRange(titleField.value.Length, titleField.value.Length);
            });
        }

        /// <summary>Sets the title, as the command line does for a check.</summary>
        public void SetTitle(string title)
        {
            titleField.value = title;
        }

        /// <summary>Copies what the user wrote into the report.</summary>
        public void FillIn(BugReport report)
        {
            report.Title = titleField.value.Trim();
            report.WhatHappened = whatField.value;
            report.Steps = stepsField.value;
            report.Expected = expectedField.value;
            report.Crashed = Crashed;
        }

        private void AddField(string label, TextField field, string tooltip, bool tall)
        {
            var row = new VisualElement();
            row.AddToClassList("bug-report-row");
            if (tall)
                row.AddToClassList("bug-report-row-tall");
            var text = new Label(label);
            text.AddToClassList("bug-report-label");
            row.Add(text);
            field.AddToClassList("picker-field");
            field.AddToClassList("bug-report-field");
            if (tall)
                field.AddToClassList("bug-report-text");
            field.tooltip = tooltip;
            row.tooltip = tooltip;
            row.Add(field);
            Add(row);
        }

        private void UpdateIncluded()
        {
            var log = !IncludeLog ? string.Empty
                : Crashed ? ", the end of the crashed session's log and Unity's crash report if there is one"
                : " and the end of the log";
            includedLabel.text = "Goes along: MapWorks' version, Windows, graphics card, processor and memory" + (Crashed ? string.Empty : ", the open map") + log + ". " +
                                 (IncludeLog ? "The whole log lies ready in a folder that opens, to drag into the issue. " : string.Empty) +
                                 "Your Windows user folder shows as %USERPROFILE%, keys of picture sources are left out. You see everything on GitHub before you submit it.";
        }

        private void UpdateSend()
        {
            var title = titleField.value.Trim();
            sendButton.SetEnabled(title.Length > 0 && title != "Crash:");
        }
    }
}
