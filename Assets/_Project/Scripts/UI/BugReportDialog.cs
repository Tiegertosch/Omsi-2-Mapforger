using System;
using OmsiMapEditor.Core;
using OmsiMapEditor.Core.Localization;
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

            AddField("bugreport.field.title", titleField, "bugreport.field.title.tooltip", false);
            titleField.textSelection.selectAllOnFocus = false;
            titleField.RegisterValueChangedCallback(_ => UpdateSend());
            AddField("bugreport.field.what", whatField, "bugreport.field.what.tooltip", true);
            AddField("bugreport.field.steps", stepsField, "bugreport.field.steps.tooltip", true);
            AddField("bugreport.field.expected", expectedField, "bugreport.field.expected.tooltip", true);

            var logRow = new VisualElement();
            logRow.AddToClassList("bug-report-row");
            var logLabel = LocBindings.NewLabel("bugreport.log");
            logLabel.AddToClassList("bug-report-label");
            logRow.Add(logLabel);
            LocBindings.Text(logToggle, "bugreport.log.attach");
            LocBindings.Tooltip(logToggle, "bugreport.log.attach.tooltip");
            logToggle.AddToClassList("bug-report-toggle");
            logToggle.RegisterValueChangedCallback(_ => UpdateIncluded());
            logRow.Add(logToggle);
            Add(logRow);
            includedLabel.AddToClassList("bug-report-hint");
            Add(includedLabel);

            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = LocBindings.NewButton("bugreport.cancel", () => Cancelled?.Invoke());
            cancel.AddToClassList("map-control-button");
            sendButton = LocBindings.WithTooltip(LocBindings.NewButton("bugreport.send", () => SendRequested?.Invoke()), "bugreport.send.tooltip");
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
            LocBindings.Text(titleLabel, crashed ? "bugreport.title.crash" : "bugreport.title"); // i18n:keys
            LocBindings.Text(introLabel, crashed ? "bugreport.intro.crash" : "bugreport.intro"); // i18n:keys
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

        private void AddField(string labelKey, TextField field, string tooltipKey, bool tall)
        {
            var row = new VisualElement();
            row.AddToClassList("bug-report-row");
            if (tall)
                row.AddToClassList("bug-report-row-tall");
            var text = LocBindings.NewLabel(labelKey); // i18n:keys
            text.AddToClassList("bug-report-label");
            row.Add(text);
            field.AddToClassList("picker-field");
            field.AddToClassList("bug-report-field");
            if (tall)
                field.AddToClassList("bug-report-text");
            LocBindings.Tooltip(field, tooltipKey); // i18n:keys
            LocBindings.Tooltip(row, tooltipKey); // i18n:keys
            row.Add(field);
            Add(row);
        }

        private void UpdateIncluded()
        {
            var what = Crashed ? (IncludeLog ? "crashlog" : "crash") : (IncludeLog ? "maplog" : "map");
            LocBindings.Text(includedLabel, "bugreport.included", ("what", what), ("log", IncludeLog ? "yes" : "no"));
        }

        private void UpdateSend()
        {
            var title = titleField.value.Trim();
            sendButton.SetEnabled(title.Length > 0 && title != "Crash:");
        }
    }
}
