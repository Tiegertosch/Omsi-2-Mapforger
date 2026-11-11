using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
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
        private VisualElement signalPhasesBox;

        private VisualElement CreateSignalPhasesPage()
        {
            signalPhasesPage = new ScrollView(ScrollViewMode.Vertical);
            signalPhasesPage.AddToClassList("signal-scroll-page");
            signalPhasesInfo = new Label();
            signalPhasesInfo.AddToClassList("panel-info");
            signalPhasesPage.Add(signalPhasesInfo);
            signalPhasesBox = new VisualElement();
            signalPhasesPage.Add(signalPhasesBox);
            signalPhasesPage.style.display = DisplayStyle.None;
            return signalPhasesPage;
        }

        private void RenderSignalPhases()
        {
            if (signalPhasesBox == null)
                return;
            signalPhasesBox.Clear();
            var shown = ShownSignalProgram;
            var editable = SignalsEditable && shown != null;
            if (shown == null || shown.Lights.Count == 0)
            {
                signalPhasesInfo.text = Loc.T(editable ? "signals.phases.empty" : "signals.timeline.empty"); // i18n:keys
                if (editable)
                {
                    signalPhasesBox.Add(SignalWizardButton());
                    signalPhasesBox.Add(SignalTemplatesButton());
                }
                return;
            }
            var plan = OmsiSignalPhasePlan.Detect(shown);
            if (plan == null)
            {
                // A simplified, read-only view: when which groups may go.
                signalPhasesInfo.text = Loc.T("signals.phases.none");
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

            var times = new VisualElement();
            times.AddToClassList("signal-group-row");
            times.Add(SignalPlanField("signals.phases.red_yellow", plan.RedYellowSeconds, editable, value => EditSignalPlan(plan, changed => changed.RedYellowSeconds = value), true));
            times.Add(SignalPlanField("signals.phases.yellow", plan.YellowSeconds, editable, value => EditSignalPlan(plan, changed => changed.YellowSeconds = value), true));
            times.Add(SignalPlanField("signals.phases.clearance", plan.ClearanceSeconds, editable, value => EditSignalPlan(plan, changed => changed.ClearanceSeconds = value), true));
            signalPhasesBox.Add(times);

            if (editable)
            {
                var add = LocBindings.WithTooltip(LocBindings.NewButton("signals.phases.add", () => AddSignalPhase(plan, shown)), "signals.phases.add.tooltip");
                add.AddToClassList("map-control-button");
                signalPhasesBox.Add(add);
            }
        }

        /// <summary>One phase: its number, green time and buttons, then a chip for every group, lit when it goes in this phase.</summary>
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
                value => EditSignalPlan(plan, changed => changed.Phases[index].GreenSeconds = value), false));
            if (editable && index > 0)
            {
                var up = LocBindings.WithTooltip(new Button(() => EditSignalPlan(plan, changed =>
                {
                    var phase = changed.Phases[index];
                    changed.Phases.RemoveAt(index);
                    changed.Phases.Insert(index - 1, phase);
                })) { text = "↑" }, "signals.phases.up.tooltip");
                up.AddToClassList("panel-icon-button");
                head.Add(up);
            }
            if (editable && plan.Phases.Count > 1)
            {
                var remove = LocBindings.WithTooltip(new Button(() => EditSignalPlan(plan, changed => changed.Phases.RemoveAt(index))) { text = "×" },
                    "signals.phases.remove.tooltip");
                remove.AddToClassList("panel-icon-button");
                head.Add(remove);
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
                    chip.style.borderLeftColor = SignalGroupColour(light);
                chip.tooltip = Loc.T(going ? "signals.phases.chip.on" : "signals.phases.chip.off", ("group", shown.Lights[light].Name)); // i18n:keys
                chip.SetEnabled(editable);
                groups.Add(chip);
            }
            box.Add(groups);
            return box;
        }

        private VisualElement SignalPlanField(string key, double value, bool editable, Action<double> changed, bool zeroAllowed)
        {
            var row = new VisualElement();
            row.AddToClassList("signal-plan-field");
            var text = LocBindings.NewLabel(key); // i18n:keys
            text.AddToClassList("signal-movement-text");
            row.Add(text);
            var field = new TextField { isDelayed = true, value = Seconds(value) };
            field.AddToClassList("picker-field");
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
