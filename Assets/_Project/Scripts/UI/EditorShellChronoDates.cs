using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The first and last day of a chrono variant, changed after it was made (ADR 075): while a variant is edited, the project browser shows
    /// its two days as fields and an Apply button; they are written to <c>[startdate]</c> and <c>[enddate]</c> in its Chrono.cfg.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The project browser's fields for the days the edited variant applies on.</summary>
        private VisualElement VariantDatesEditor()
        {
            var box = new VisualElement();
            box.style.marginTop = 6;
            var first = new TextField(Loc.T("chrono.field.first")) { value = FormatDay(editedVariant.Start), tooltip = Loc.T("chrono.dates.first.tooltip") };
            var last = new TextField(Loc.T("chrono.field.last")) { value = FormatDay(editedVariant.End), tooltip = Loc.T("chrono.field.last.tooltip") };
            foreach (var field in new[] { first, last })
            {
                field.AddToClassList("browser-chrono-date");
                box.Add(field);
            }
            var apply = new Button(() => ApplyVariantDates(first.value, last.value)) { text = Loc.T("chrono.dates.apply"), tooltip = Loc.T("chrono.dates.apply.tooltip") };
            apply.AddToClassList("map-control-button");
            box.Add(apply);
            return box;
        }

        private static string FormatDay(DateTime? day)
        {
            return day.HasValue ? day.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
        }

        /// <summary>Writes new days into the edited variant; the map is shown again on its first day when the day it showed no longer applies.</summary>
        private void ApplyVariantDates(string firstText, string lastText)
        {
            if (!EditingVariant)
                return;
            if (!CanEditRules)
            {
                SetStatus(ReadOnlyMessage("readonly.action.variant_dates"));
                return;
            }
            if (!TryParseDay(firstText, out var first))
            {
                SetStatusT("chrono.status.first_day");
                return;
            }
            DateTime? last = null;
            if (!string.IsNullOrWhiteSpace(lastText))
            {
                if (!TryParseDay(lastText, out var lastDay))
                {
                    SetStatusT("chrono.status.last_day");
                    return;
                }
                last = lastDay;
            }
            var title = editedVariant.Title;
            var name = editedVariant.Name;
            bool changed;
            try
            {
                changed = session.SetChronoDates(editedVariant, first, last);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("chrono.dates.failed", ("reason", exception.Message));
                return;
            }
            if (!changed)
            {
                SetStatusT("chrono.dates.unchanged", ("title", title));
                return;
            }
            // The list of variants the map shows follows what the file says now.
            foreach (var listed in chronoVariants)
                if (string.Equals(listed.Name, name, StringComparison.OrdinalIgnoreCase) && !ReferenceEquals(listed, editedVariant))
                    session.SetChronoDates(listed, first, last); // the file already says so, so this only brings the listed copy up to date
            var stays = chronoDate.HasValue && editedVariant.IsActiveOn(chronoDate.Value);
            if (stays)
            {
                var editIndex = chronoVariants.FindIndex(variant => string.Equals(variant.Name, name, StringComparison.OrdinalIgnoreCase));
                var active = chronoVariants.FindAll(variant => chronoVariants.IndexOf(variant) <= editIndex && variant.IsActiveOn(chronoDate.Value));
                StartRoutesBuild(currentSummary, active);
                RenderProjectBrowser();
                SetStatusT("chrono.dates.changed", ("title", title), ("first", FormatDay(first)), ("last", FormatDay(last)), ("hasLast", last.HasValue ? "yes" : "no"));
                return;
            }
            // The shown day is outside the new days, so the variant would vanish from the view: edit it on its new first day (this clears the undo history).
            var moved = chronoVariants.Find(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            EditChronoVariant(moved);
            SetStatusT("chrono.dates.moved", ("title", title), ("first", FormatDay(first)), ("last", FormatDay(last)), ("hasLast", last.HasValue ? "yes" : "no"));
        }

        /// <summary>
        /// Command line, with a variant being edited: <c>-chronoDates yyyy-MM-dd [yyyy-MM-dd|-]</c> gives it a first and a last day ("-" or none
        /// keeps it applying). The status goes to the log as "Chrono dates:".
        /// </summary>
        private void RunChronoDatesArgument(string[] args)
        {
            var index = Array.IndexOf(args, "-chronoDates");
            if (index < 0 || index + 1 >= args.Length || !EditingVariant)
                return;
            var raw = index + 2 < args.Length ? args[index + 2] : string.Empty;
            var last = raw.StartsWith("-", StringComparison.Ordinal) ? string.Empty : raw;
            ApplyVariantDates(args[index + 1], last);
            Debug.Log("Chrono dates: " + statusLabel?.text);
        }
    }
}
