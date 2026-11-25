using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The [addseason] entries of a map in the settings dialog (feature parity I): one row per season with its kind and first and last day,
    /// a button to remove it, a button to add one, and the findings of <see cref="OmsiSeasons.Check"/> underneath. The logic lives in Editor.OMSI.
    /// </summary>
    internal sealed class SeasonListEditor : VisualElement
    {
        private sealed class SeasonRow
        {
            public VisualElement Root;
            public DropdownField Kind;
            public int[] KindValues;
            public TextField First;
            public TextField Last;
        }

        private readonly List<SeasonRow> rows = new List<SeasonRow>();
        private readonly VisualElement list = new VisualElement();
        private readonly Label emptyLabel = LocBindings.NewLabel("seasons.list.empty");
        private readonly Label findings = new Label();
        private readonly Button addButton;

        public SeasonListEditor()
        {
            AddToClassList("season-editor");
            emptyLabel.AddToClassList("new-map-hint");
            emptyLabel.AddToClassList("season-empty");
            list.AddToClassList("season-list");
            Add(list);
            Add(emptyLabel);
            findings.AddToClassList("new-map-hint");
            findings.AddToClassList("season-findings");
            Add(findings);
            var actions = OptionControls.Group(this);
            addButton = OptionControls.TextChoice(actions, "seasons.add", "seasons.add.tooltip", () =>
            {
                AddRow(NextFreeEntry());
                Changed?.Invoke();
            });
            addButton.AddToClassList("season-add");
        }

        /// <summary>The list changed (a field was edited, a season added or removed).</summary>
        public event Action Changed;

        /// <summary>Shows these entries in place of the present rows.</summary>
        public void SetEntries(IList<OmsiSeasonEntry> entries)
        {
            rows.Clear();
            list.Clear();
            foreach (var entry in entries)
                AddRow(entry);
            Refresh();
        }

        /// <summary>The entries the rows show, or null with the reason when a field is not a whole number.</summary>
        public List<OmsiSeasonEntry> ReadEntries(out string problem)
        {
            problem = null;
            var result = new List<OmsiSeasonEntry>();
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (!TryDay(row.First, out var first) || !TryDay(row.Last, out var last))
                {
                    problem = Loc.T("seasons.problem.number", ("number", index + 1));
                    return null;
                }
                var kindIndex = row.Kind.index;
                result.Add(new OmsiSeasonEntry(kindIndex >= 0 && kindIndex < row.KindValues.Length ? row.KindValues[kindIndex] : OmsiSeasonKind.Summer, first, last));
            }
            return result;
        }

        /// <summary>The first error of the rows as a sentence of the language, or null; overlaps are no error.</summary>
        public string Problem()
        {
            var entries = ReadEntries(out var problem);
            if (entries == null)
                return problem;
            foreach (var finding in OmsiSeasons.Check(entries))
            {
                if (!finding.IsError)
                    continue;
                var number = finding.Index + 1;
                if (finding.Problem == OmsiSeasonProblem.DayOutOfRange)
                    return Loc.T("seasons.problem.range", ("number", number), ("max", OmsiSeasons.MaximumDay));
                if (finding.Problem == OmsiSeasonProblem.LastBeforeFirst)
                    return Loc.T("seasons.problem.order", ("number", number));
                return Loc.T("seasons.problem.kind", ("number", number));
            }
            return null;
        }

        /// <summary>Writes the kind names and the findings again, after a language change.</summary>
        public void RefreshTexts()
        {
            var entries = ReadEntries(out _);
            if (entries != null)
                SetEntries(entries);
            else
                Refresh();
        }

        private void AddRow(OmsiSeasonEntry entry)
        {
            var row = new SeasonRow { Root = new VisualElement() };
            row.Root.AddToClassList("season-row");
            row.KindValues = KindValuesFor(entry.Kind);
            row.Kind = new DropdownField();
            row.Kind.AddToClassList("new-map-dropdown");
            row.Kind.AddToClassList("season-kind");
            var names = new List<string>();
            foreach (var kind in row.KindValues)
                names.Add(KindName(kind));
            row.Kind.choices = names;
            row.Kind.SetValueWithoutNotify(names[Array.IndexOf(row.KindValues, entry.Kind)]);
            LocBindings.Tooltip(row.Kind, "seasons.kind.tooltip"); // i18n:keys
            row.Kind.RegisterValueChangedCallback(_ => OnEdited());
            row.Root.Add(row.Kind);

            row.First = DayField(entry.FirstDay, "seasons.first.tooltip");
            row.Root.Add(row.First);
            var dash = new Label("–");
            dash.AddToClassList("new-map-dash");
            row.Root.Add(dash);
            row.Last = DayField(entry.LastDay, "seasons.last.tooltip");
            row.Root.Add(row.Last);

            var remove = new Button(() => Remove(row));
            remove.AddToClassList("option-icon");
            remove.AddToClassList("option-icon-danger");
            remove.Add(new IconElement("trash-2"));
            LocBindings.Tooltip(remove, "seasons.remove.tooltip"); // i18n:keys
            row.Root.Add(remove);

            rows.Add(row);
            list.Add(row.Root);
            Refresh();
        }

        private TextField DayField(int day, string tooltipKey)
        {
            var field = new TextField { value = day.ToString(CultureInfo.InvariantCulture) };
            field.AddToClassList("picker-field");
            field.AddToClassList("new-map-number");
            field.AddToClassList("season-day");
            LocBindings.Tooltip(field, tooltipKey, ("max", OmsiSeasons.MaximumDay)); // i18n:keys
            field.RegisterValueChangedCallback(_ => OnEdited());
            return field;
        }

        private void Remove(SeasonRow row)
        {
            rows.Remove(row);
            list.Remove(row.Root);
            Refresh();
            Changed?.Invoke();
        }

        private void OnEdited()
        {
            Refresh();
            Changed?.Invoke();
        }

        /// <summary>A new season starts after the last day the others reach, as a summer of the rest of the year.</summary>
        private OmsiSeasonEntry NextFreeEntry()
        {
            var entries = ReadEntries(out _);
            var start = 0;
            if (entries != null)
                foreach (var entry in entries)
                    start = Math.Max(start, Math.Min(entry.LastDay + 1, OmsiSeasons.MaximumDay));
            return new OmsiSeasonEntry(OmsiSeasonKind.Summer, start, OmsiSeasons.MaximumDay);
        }

        private void Refresh()
        {
            emptyLabel.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var entries = ReadEntries(out _);
            var text = string.Empty;
            if (entries != null)
                foreach (var finding in OmsiSeasons.Check(entries))
                    if (finding.Problem == OmsiSeasonProblem.Overlap)
                        text += (text.Length > 0 ? "\n" : string.Empty) + Loc.T("seasons.warning.overlap", ("first", finding.Other + 1), ("second", finding.Index + 1));
            findings.text = text;
            findings.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            // Rows whose own days are wrong are marked; findings about the whole list stay in the text.
            for (var index = 0; index < rows.Count; index++)
            {
                var wrong = entries != null && entries[index].LastDay < entries[index].FirstDay ||
                            entries != null && (entries[index].FirstDay < 0 || entries[index].LastDay > OmsiSeasons.MaximumDay) || entries == null && !RowParses(rows[index]);
                rows[index].Root.EnableInClassList("season-row-problem", wrong);
            }
        }

        private static bool RowParses(SeasonRow row)
        {
            return TryDay(row.First, out _) && TryDay(row.Last, out _);
        }

        private static bool TryDay(TextField field, out int day)
        {
            return int.TryParse((field.value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out day);
        }

        /// <summary>The kinds a row offers: the known ones, and the entry's own when the file says something else, so it is not lost.</summary>
        private static int[] KindValuesFor(int kind)
        {
            var values = new List<int>(OmsiSeasonKind.Offered);
            if (!values.Contains(kind))
                values.Add(kind);
            return values.ToArray();
        }

        private static string KindName(int kind)
        {
            switch (kind)
            {
                case OmsiSeasonKind.Summer:
                    return Loc.T("seasons.kind.summer");
                case OmsiSeasonKind.Spring:
                    return Loc.T("seasons.kind.spring");
                case OmsiSeasonKind.Autumn:
                    return Loc.T("seasons.kind.autumn");
                case OmsiSeasonKind.Winter:
                    return Loc.T("seasons.kind.winter");
                case OmsiSeasonKind.SnowyWinter:
                    return Loc.T("seasons.kind.snow");
                case OmsiSeasonKind.DrySummer:
                    return Loc.T("seasons.kind.dry");
                default:
                    return Loc.T("seasons.kind.other", ("kind", kind));
            }
        }
    }
}
