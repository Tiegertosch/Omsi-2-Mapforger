using System;
using OmsiMapEditor.Core;
using OmsiMapEditor.Core.Localization;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The form of Help > Report a bug... (ADR 053): a title, what happened, how to get there and what was expected. After
    /// a crash it opens by itself and says so. The form is the body of a <see cref="ModalDialog"/> (M7 step 5), which owns the title,
    /// the intro and the buttons; the shell sends the report when the primary button is pressed. One of its four fields is a line and three
    /// are text areas, so Enter never presses Send (rule 2B).
    /// </summary>
    internal sealed class BugReportDialog : VisualElement
    {
        private readonly ModalDialog host;
        private readonly TextField titleField = new TextField();
        private readonly TextField whatField = new TextField { multiline = true };
        private readonly TextField stepsField = new TextField { multiline = true };
        private readonly TextField expectedField = new TextField { multiline = true };
        private readonly Toggle logToggle = new Toggle { value = true };
        private readonly Label includedLabel = new Label();

        public BugReportDialog(ModalDialog host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            LocBindings.Tooltip(host.Primary, "bugreport.send.tooltip");

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
            logToggle.AddToClassList("form-toggle");
            logToggle.RegisterValueChangedCallback(_ => UpdateIncluded());
            logRow.Add(logToggle);
            Add(logRow);
            includedLabel.AddToClassList("bug-report-hint");
            Add(includedLabel);
        }

        public bool IncludeLog => logToggle.value;

        public bool Crashed { get; private set; }

        /// <summary>Starts an empty report; after a crash its texts say so and its log is that of the crashed session.</summary>
        public void Open(bool crashed)
        {
            Crashed = crashed;
            LocBindings.Text(host.Title, crashed ? "bugreport.title.crash" : "bugreport.title"); // i18n:keys
            LocBindings.Text(host.Description, crashed ? "bugreport.intro.crash" : "bugreport.intro"); // i18n:keys
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
            host.Primary.SetEnabled(title.Length > 0 && title != "Crash:");
        }
    }
}
