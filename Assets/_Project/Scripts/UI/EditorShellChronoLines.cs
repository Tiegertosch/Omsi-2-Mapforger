using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The lines a chrono variant switches off (ADR 067): while a variant is edited, the project browser lists the lines that run before
    /// it and a checkbox each; a checked line is written to <c>[deactivate_lines]</c> in the variant's Chrono.cfg and the routes on the map
    /// follow at once.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private List<string> lineCandidates;
        private string lineCandidatesFor;

        /// <summary>The line names the edited variant could switch off: those of the timetable as the variants before it leave it.</summary>
        private List<string> LineCandidates()
        {
            if (lineCandidates != null && string.Equals(lineCandidatesFor, editedVariant.Name, StringComparison.OrdinalIgnoreCase))
                return lineCandidates;
            var names = new List<string>();
            try
            {
                var editIndex = chronoVariants.FindIndex(variant => string.Equals(variant.Name, editedVariant.Name, StringComparison.OrdinalIgnoreCase));
                var before = chronoVariants.FindAll(variant => chronoVariants.IndexOf(variant) < editIndex && variant.IsActiveOn(chronoDate.Value));
                foreach (var line in OmsiTimetable.ReadForVariants(session.Source, before).Lines)
                    names.Add(line.Name);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException)
            {
                Debug.LogException(exception);
            }
            // A line this variant switches off is not in that list any more only when an earlier variant already did.
            foreach (var name in editedVariant.DeactivatedLines)
                if (!names.Exists(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)))
                    names.Add(name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            lineCandidates = names;
            lineCandidatesFor = editedVariant.Name;
            return names;
        }

        /// <summary>The project browser's checkboxes for the lines the edited variant switches off.</summary>
        private VisualElement VariantLinesEditor()
        {
            var foldout = new Foldout { text = Loc.T("chronolines.foldout", ("count", editedVariant.DeactivatedLines.Count)), value = variantLinesOpen };
            foldout.AddToClassList("browser-lines");
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                    variantLinesOpen = evt.newValue;
            });
            if (!variantLinesOpen)
                return foldout;
            var candidates = LineCandidates();
            if (candidates.Count == 0)
            {
                foldout.Add(new Label(Loc.T("chronolines.none")));
                return foldout;
            }
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("browser-line-list");
            foreach (var name in candidates)
            {
                var captured = name;
                var toggle = new Toggle(Loc.T("chronolines.line", ("name", name)))
                {
                    value = editedVariant.DeactivatedLines.Exists(known => string.Equals(known, captured, StringComparison.OrdinalIgnoreCase)),
                    tooltip = Loc.T("chronolines.line.tooltip", ("name", name))
                };
                toggle.RegisterValueChangedCallback(evt => SetLineOff(captured, evt.newValue));
                list.Add(toggle);
            }
            foldout.Add(list);
            return foldout;
        }

        private bool variantLinesOpen;

        /// <summary>Switches a line off in the edited variant, or on again; the others it switches off keep their order.</summary>
        private void SetLineOff(string name, bool off)
        {
            var names = new List<string>(editedVariant.DeactivatedLines);
            names.RemoveAll(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
            if (off)
                names.Add(name);
            ApplyDeactivatedLines(names, off ? Loc.T("chronolines.status.off", ("name", name), ("variant", editedVariant.Title))
                : Loc.T("chronolines.status.on", ("name", name), ("variant", editedVariant.Title)));
        }

        private void ApplyDeactivatedLines(List<string> names, string done)
        {
            if (!CanEditRules)
            {
                SetStatus(ReadOnlyMessage("readonly.action.variant_lines"));
                return;
            }
            try
            {
                session.SetDeactivatedLines(editedVariant, names);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("chronolines.status.failed", ("reason", exception.Message));
                return;
            }
            // The list of variants the map shows and the routes of this day follow what the file says now.
            foreach (var listed in chronoVariants)
                if (string.Equals(listed.Name, editedVariant.Name, StringComparison.OrdinalIgnoreCase) && !ReferenceEquals(listed, editedVariant))
                {
                    listed.DeactivatedLines.Clear();
                    listed.DeactivatedLines.AddRange(editedVariant.DeactivatedLines);
                }
            var editIndex = chronoVariants.FindIndex(variant => string.Equals(variant.Name, editedVariant.Name, StringComparison.OrdinalIgnoreCase));
            var active = chronoVariants.FindAll(variant => chronoVariants.IndexOf(variant) <= editIndex && variant.IsActiveOn(chronoDate.Value));
            StartRoutesBuild(currentSummary, active);
            RenderProjectBrowser();
            SetStatus(done);
        }

        /// <summary>
        /// Command line (ADR 067), with a variant being edited: <c>-linesOff A|B|C</c> makes the variant switch off exactly these lines,
        /// <c>-linesOff -</c> none. The status goes to the log as "Lines off:".
        /// </summary>
        private void RunLinesArgument(string[] args)
        {
            var index = Array.IndexOf(args, "-linesOff");
            if (index < 0 || index + 1 >= args.Length || !EditingVariant)
                return;
            var text = args[index + 1];
            var names = text == "-" ? new List<string>() : text.Split('|').ToList();
            ApplyDeactivatedLines(names, names.Count == 0 ? Loc.T("chronolines.status.none", ("variant", editedVariant.Title))
                : Loc.T("chronolines.status.some", ("variant", editedVariant.Title), ("names", string.Join(", ", names))));
            Debug.Log("Lines off: " + statusLabel?.text);
        }
    }
}
