using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The phases tab of the traffic signals tool (ADR 085, milestone 4; the simple mode of the brief, §8): the program as a
    /// sequence of traffic phases, each with the groups that go and its green time, and the red and yellow, yellow and
    /// clearance times they share. It edits the same draft as the timeline. A program counts as phases only when they give
    /// back its very timeline (<see cref="OmsiSignalPhasePlan.Detect"/>); any other is shown with a note and edited on the
    /// timeline, so nothing is lost by switching.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The simplified view lists at most this many windows of the cycle.</summary>
        private const int MostSignalWindows = 40;

        private VisualElement signalPhasesPage;
        private Label signalPhasesInfo;
        private VisualElement signalPhasesFlag;
        private VisualElement signalPhasesBox;

        private VisualElement CreateSignalPhasesPage()
        {
            signalPhasesPage = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            signalPhasesInfo = new Label();
            signalPhasesInfo.AddToClassList("panel-info");
            signalPhasesPage.Add(signalPhasesInfo);
            // A program that is no phase plan is flagged here, with the way to the timeline where it is edited; it is never converted.
            signalPhasesFlag = new VisualElement();
            signalPhasesFlag.AddToClassList("signal-flag");
            var flagText = LocBindings.NewLabel("signals.phases.flag");
            flagText.AddToClassList("signal-flag-text");
            signalPhasesFlag.Add(flagText);
            signalPhasesFlag.Add(SignalLabelButton("chart-gantt", "signals.phases.to_timeline", "signals.phases.to_timeline.tooltip", () => ShowSignalTab(SignalTab.Timeline)));
            signalPhasesFlag.style.display = DisplayStyle.None;
            signalPhasesPage.Add(signalPhasesFlag);
            // The phases stand one under the other: a vertical list in the order of the cycle.
            signalPhasesBox = new VisualElement();
            signalPhasesBox.AddToClassList("signal-phases-list");
            signalPhasesPage.Add(signalPhasesBox);
            signalPhasesPage.style.display = DisplayStyle.None;
            return signalPhasesPage;
        }

        private void RenderSignalPhases()
        {
            if (signalPhasesBox == null)
                return;
            var scroll = signalPhasesPage as ScrollView;
            var offset = scroll != null ? scroll.scrollOffset : default;
            var focusKey = SignalFocus.Capture(signalPhasesPage);
            signalPhasesBox.Clear();
            var shown = ShownSignalProgram;
            var editable = SignalsEditable && shown != null;
            var view = SignalWindowRules.PhaseViewOf(shown);
            signalPhasesFlag.style.display = view == SignalPhaseView.NotRepresentable ? DisplayStyle.Flex : DisplayStyle.None;
            if (view == SignalPhaseView.Empty)
            {
                signalPhasesInfo.text = Loc.T(editable ? "signals.phases.empty" : "signals.timeline.empty"); // i18n:keys
                return;
            }
            var plan = OmsiSignalPhasePlan.Detect(shown);
            if (plan == null)
            {
                // Not a phase plan: shown, not converted. When which groups may go; the program is edited on the timeline.
                signalPhasesInfo.text = Loc.T("signals.phases.windows");
                var windows = OmsiSignalPhasePlan.Windows(shown);
                foreach (var window in windows.Take(MostSignalWindows))
                {
                    var names = string.Join(", ", window.Lights.Select(light => shown.Lights[light].Name));
                    var line = new Label(window.Lights.Count > 0
                        ? Loc.T("signals.phases.window", ("from", Seconds(window.From)), ("to", Seconds(window.To)), ("groups", names))
                        : Loc.T("signals.phases.window_none", ("from", Seconds(window.From)), ("to", Seconds(window.To))));
                    line.AddToClassList("signal-window");
                    signalPhasesBox.Add(line);
                }
                if (windows.Count > MostSignalWindows)
                {
                    var more = new Label(Loc.T("signals.check.more", ("count", windows.Count - MostSignalWindows)));
                    more.AddToClassList("panel-info");
                    signalPhasesBox.Add(more);
                }
                return;
            }
            signalPhasesInfo.text = Loc.T(editable ? "signals.phases.info" : "signals.phases.readonly", ("count", plan.Phases.Count), // i18n:keys
                ("cycle", Seconds(plan.CycleSeconds)));

            for (var index = 0; index < plan.Phases.Count; index++)
                signalPhasesBox.Add(SignalPhaseRow(plan, index, shown, editable));

            if (editable)
            {
                var addRow = new VisualElement();
                addRow.AddToClassList("signal-toolbar");
                addRow.Add(SignalLabelButton("plus", "signals.phases.add", "signals.phases.add.tooltip", () => AddSignalPhase(plan, shown)));
                signalPhasesBox.Add(addRow);
            }

            // The times every phase change shares: red-yellow before green, yellow after it, and all red between the phases. A program with
            // other times for single groups is no phase plan (it is flagged above and edited on the timeline), so these are the real times.
            var times = new VisualElement();
            times.AddToClassList("signal-phase");
            times.AddToClassList("signal-phase-times");
            var timesTitle = LocBindings.NewLabel("signals.phases.times");
            timesTitle.AddToClassList("inspector-section-heading");
            times.Add(timesTitle);
            var timeFields = new VisualElement();
            timeFields.AddToClassList("signal-details");
            timeFields.Add(SignalPlanField("signals.phases.red_yellow", plan.RedYellowSeconds, editable, value => EditSignalPlan(plan, changed => changed.RedYellowSeconds = value), true, "focus:times:redyellow"));
            timeFields.Add(SignalPlanField("signals.phases.yellow", plan.YellowSeconds, editable, value => EditSignalPlan(plan, changed => changed.YellowSeconds = value), true, "focus:times:yellow"));
            timeFields.Add(SignalPlanField("signals.phases.clearance", plan.ClearanceSeconds, editable, value => EditSignalPlan(plan, changed => changed.ClearanceSeconds = value), true, "focus:times:clearance"));
            times.Add(timeFields);
            var timesNote = LocBindings.NewLabel("signals.phases.times_note");
            timesNote.AddToClassList("panel-info");
            times.Add(timesNote);
            signalPhasesBox.Add(times);
            if (scroll != null)
                scroll.schedule.Execute(() => scroll.scrollOffset = offset);
            SignalFocus.Restore(signalPhasesPage, focusKey);
        }

        /// <summary>One phase: its number with the time it starts, its green time and buttons, then a chip for every group, lit when it goes in this phase.</summary>
        private VisualElement SignalPhaseRow(OmsiSignalPhasePlan plan, int index, OmsiSignalProgram shown, bool editable)
        {
            var box = new VisualElement();
            box.AddToClassList("signal-phase");
            var head = new VisualElement();
            head.AddToClassList("signal-group-row");
            var title = new Label(Loc.T("signals.phases.phase", ("n", index + 1), ("start", Seconds(plan.StartOf(index)))));
            title.AddToClassList("signal-phase-title");
            head.Add(title);
            head.Add(SignalPlanField("signals.phases.green", plan.Phases[index].GreenSeconds, editable,
                value => EditSignalPlan(plan, changed => changed.Phases[index].GreenSeconds = value), false, "focus:phase:" + index.ToString(CultureInfo.InvariantCulture)));
            if (editable)
            {
                var buttons = OptionControls.Group(head);
                if (index > 0)
                    OptionControls.Choice(buttons, "arrow-up", "signals.phases.up", "signals.phases.up.tooltip", () => EditSignalPlan(plan, changed =>
                    {
                        var phase = changed.Phases[index];
                        changed.Phases.RemoveAt(index);
                        changed.Phases.Insert(index - 1, phase);
                    }));
                if (index < plan.Phases.Count - 1)
                    OptionControls.Choice(buttons, "arrow-down", "signals.phases.down", "signals.phases.down.tooltip", () => EditSignalPlan(plan, changed =>
                    {
                        var phase = changed.Phases[index];
                        changed.Phases.RemoveAt(index);
                        changed.Phases.Insert(index + 1, phase);
                    }));
                if (plan.Phases.Count > 1)
                    OptionControls.Choice(buttons, "trash-2", "signals.phases.remove", "signals.phases.remove.tooltip", () => EditSignalPlan(plan, changed => changed.Phases.RemoveAt(index)))
                        .AddToClassList("option-icon-danger");
            }
            box.Add(head);

            var groups = new VisualElement();
            groups.AddToClassList("signal-phase-groups");
            for (var light = 0; light < shown.Lights.Count; light++)
            {
                var member = light;
                var going = plan.Phases[index].Lights.Contains(light);
                var chip = new Button(() => ToggleSignalPhaseGroup(plan, index, member)) { text = shown.Lights[light].Name };
                chip.AddToClassList("signal-phase-chip");
                chip.EnableInClassList("signal-phase-chip-on", going);
                if (going)
                    chip.style.borderLeftColor = SignalColourOf(light);
                chip.tooltip = Loc.T(going ? "signals.phases.chip.on" : "signals.phases.chip.off", ("group", shown.Lights[light].Name)); // i18n:keys
                chip.SetEnabled(editable);
                groups.Add(chip);
            }
            box.Add(groups);
            return box;
        }

        /// <summary>A time as a labelled field with its name before it and "(s)" in the name; its key keeps the focus when the page is rebuilt.</summary>
        private VisualElement SignalPlanField(string key, double value, bool editable, Action<double> changed, bool zeroAllowed, string focusKey = null)
        {
            var row = new VisualElement();
            row.AddToClassList("signal-plan-field");
            var text = LocBindings.NewLabel(key); // i18n:keys
            text.AddToClassList("signal-movement-text");
            row.Add(text);
            var field = new TextField { isDelayed = true, value = Seconds(value) };
            field.AddToClassList("picker-field");
            field.userData = focusKey;
            field.SetEnabled(editable);
            field.RegisterValueChangedCallback(evt =>
            {
                if (!TrySignalSeconds(evt.newValue, out var seconds) || (!zeroAllowed && seconds <= 0))
                {
                    SetStatusT("signals.status.bad_seconds");
                    RenderSignalPhases();
                    return;
                }
                changed(seconds);
            });
            row.Add(field);
            return row;
        }

        private void ToggleSignalPhaseGroup(OmsiSignalPhasePlan plan, int index, int light)
        {
            var lights = plan.Phases[index].Lights;
            if (lights.Contains(light) && lights.Count == 1)
            {
                SetStatusT("signals.phases.status.last_group");
                return;
            }
            EditSignalPlan(plan, changed =>
            {
                var phase = changed.Phases[index].Lights;
                if (!phase.Remove(light))
                {
                    phase.Add(light);
                    phase.Sort();
                }
            });
        }

        /// <summary>A new phase at the end, for the groups no phase has yet, or else the first group.</summary>
        private void AddSignalPhase(OmsiSignalPhasePlan plan, OmsiSignalProgram shown)
        {
            EditSignalPlan(plan, changed =>
            {
                var phase = new OmsiSignalPlanPhase { GreenSeconds = 10 };
                phase.Lights.AddRange(Enumerable.Range(0, shown.Lights.Count).Where(light => !changed.Phases.Any(other => other.Lights.Contains(light))));
                if (phase.Lights.Count == 0)
                    phase.Lights.Add(0);
                changed.Phases.Add(phase);
            });
        }

        /// <summary>
        /// Changes a copy of the plan and writes it into the draft, refused when the phases written would not come back as
        /// they were meant: two phases in a row with the same groups run into one, which the status says.
        /// </summary>
        private void EditSignalPlan(OmsiSignalPhasePlan plan, Action<OmsiSignalPhasePlan> change)
        {
            if (!SignalsEditable || signalDraft == null)
                return;
            var changed = plan.Copy();
            change(changed);
            var problems = changed.Problems(signalDraft.Lights.Count);
            if (problems.Count > 0)
            {
                SetStatusT("signals.phases.status.invalid");
                RenderSignalPhases();
                return;
            }
            var trial = signalDraft.Copy();
            changed.WriteTo(trial);
            var again = OmsiSignalPhasePlan.Detect(trial);
            if (again == null || again.Phases.Count != changed.Phases.Count)
            {
                SetStatusT("signals.phases.status.merged");
                RenderSignalPhases();
                return;
            }
            EditSignalDraft(() => changed.WriteTo(signalDraft));
        }
    }
}
