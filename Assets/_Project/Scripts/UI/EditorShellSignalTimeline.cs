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
    /// The timeline tab of the traffic signals tool (ADR 085, milestone 3). On a junction made here the timeline edits a
    /// draft of the program: painting a stretch of time by dragging along a row with the colour chosen above, dragging
    /// edges, splitting the selected stretch, its value, start and length, removing and repeating stretches, the cycle
    /// length. Nothing is written until Apply, which makes the junction again as one undo step (a file
    /// per drag would fill the folder and the history); Discard, another crossing, leaving the tool and undo drop the
    /// draft and say so. OMSI's own crossings show their timeline read-only. The timing check runs on every change.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private static readonly OmsiSignalAspect[] VehicleAspects =
            { OmsiSignalAspect.Red, OmsiSignalAspect.RedYellow, OmsiSignalAspect.Green, OmsiSignalAspect.Yellow, OmsiSignalAspect.Dark };

        private static readonly OmsiSignalAspect[] PedestrianAspects = { OmsiSignalAspect.Red, OmsiSignalAspect.Green };

        /// <summary>The names the command line uses for the colours.</summary>
        private static readonly Dictionary<string, OmsiSignalAspect> SignalAspectWords = new Dictionary<string, OmsiSignalAspect>(StringComparer.OrdinalIgnoreCase)
        {
            { "red", OmsiSignalAspect.Red }, { "redyellow", OmsiSignalAspect.RedYellow }, { "green", OmsiSignalAspect.Green },
            { "yellow", OmsiSignalAspect.Yellow }, { "dark", OmsiSignalAspect.Dark }
        };

        private Button signalMovementsTab;
        private Button signalTimelineTab;
        private VisualElement signalMovementsPage;
        private VisualElement signalTimelinePage;
        private SignalTimelineElement signalTimeline;
        private TextField signalCycleField;
        private Label signalSegmentLabel;
        private DropdownField signalStateDropdown;
        private TextField signalStartField;
        private TextField signalLengthField;
        private Button signalRemoveSegmentButton;
        private Button signalRepeatSegmentButton;
        private Button signalSplitSegmentButton;
        private VisualElement signalBrushRow;
        private readonly Dictionary<OmsiSignalAspect, Button> signalBrushButtons = new Dictionary<OmsiSignalAspect, Button>();
        private OmsiSignalAspect signalBrush = OmsiSignalAspect.Green;
        private Label signalTimelineHint;
        private Label signalIssuesLabel;
        private Button signalApplyButton;
        private Button signalDiscardButton;
        private OmsiSignalProgram signalDraft;
        private bool signalDirty;
        private SignalTab signalTab = SignalTab.Movements;
        private Button signalPhasesTab;
        private VisualElement signalDraftRow;

        /// <summary>The pages of the signals tool; the assistant has no tab of its own and is opened from the movements.</summary>
        private enum SignalTab
        {
            Movements,
            Phases,
            Timeline,
            Check,
            Wizard
        }

        private bool signalTimelineShown => signalTab == SignalTab.Timeline;
        private List<OmsiSignalAspect> signalStateChoices = new List<OmsiSignalAspect>();

        private static string AspectName(OmsiSignalAspect aspect)
        {
            switch (aspect)
            {
                case OmsiSignalAspect.Red: return Loc.T("signals.state.red");
                case OmsiSignalAspect.RedYellow: return Loc.T("signals.state.red_yellow");
                case OmsiSignalAspect.Green: return Loc.T("signals.state.green");
                case OmsiSignalAspect.GreenYellow: return Loc.T("signals.state.green_yellow");
                case OmsiSignalAspect.Yellow: return Loc.T("signals.state.yellow");
                default: return Loc.T("signals.state.dark");
            }
        }

        /// <summary>The tabs Movements and Timeline above the two pages; called from <see cref="CreateSignalOptions"/>.</summary>
        private VisualElement CreateSignalTabs()
        {
            var tabs = new VisualElement();
            tabs.AddToClassList("panel-tabs");
            signalMovementsTab = LocBindings.NewButton("signals.tab.movements", () => ShowSignalTab(SignalTab.Movements));
            signalMovementsTab.AddToClassList("panel-tab");
            tabs.Add(signalMovementsTab);
            signalPhasesTab = LocBindings.NewButton("signals.tab.phases", () => ShowSignalTab(SignalTab.Phases));
            signalPhasesTab.AddToClassList("panel-tab");
            tabs.Add(signalPhasesTab);
            signalTimelineTab = LocBindings.NewButton("signals.tab.timeline", () => ShowSignalTab(SignalTab.Timeline));
            signalTimelineTab.AddToClassList("panel-tab");
            tabs.Add(signalTimelineTab);
            tabs.Add(CreateSignalCheckTab());
            return tabs;
        }

        private VisualElement CreateSignalTimelinePage()
        {
            signalTimelinePage = new VisualElement();
            var cycleRow = new VisualElement();
            cycleRow.AddToClassList("signal-group-row");
            var cycleText = LocBindings.NewLabel("signals.cycle");
            cycleText.AddToClassList("signal-movement-text");
            cycleRow.Add(cycleText);
            signalCycleField = new TextField { isDelayed = true };
            signalCycleField.AddToClassList("picker-field");
            LocBindings.Tooltip(signalCycleField, "signals.cycle.tooltip");
            signalCycleField.RegisterValueChangedCallback(evt => SetSignalCycle(evt.newValue));
            cycleRow.Add(signalCycleField);
            signalTimelinePage.Add(cycleRow);
            signalTimelinePage.Add(CreateSignalPreviewBar());

            // The colour a drag along a row paints with.
            signalBrushRow = new VisualElement();
            signalBrushRow.AddToClassList("signal-group-row");
            var brushText = LocBindings.NewLabel("signals.brush");
            brushText.AddToClassList("signal-movement-text");
            signalBrushRow.Add(brushText);
            foreach (var aspect in VehicleAspects)
            {
                var chosen = aspect;
                var swatch = new Button(() => SetSignalBrush(chosen));
                swatch.AddToClassList("signal-brush");
                swatch.style.backgroundColor = SignalTimelineElement.ColourOf(OmsiSignalAspects.StateOf(aspect));
                if (aspect == OmsiSignalAspect.RedYellow)
                {
                    // Red above, yellow below, as the timeline draws it.
                    var lower = new VisualElement();
                    lower.AddToClassList("signal-brush-lower");
                    lower.style.backgroundColor = SignalTimelineElement.ColourOf(OmsiSignalAspects.StateOf(OmsiSignalAspect.Yellow));
                    lower.pickingMode = PickingMode.Ignore;
                    swatch.Add(lower);
                }
                swatch.tooltip = AspectName(aspect);
                signalBrushButtons[aspect] = swatch;
                signalBrushRow.Add(swatch);
            }
            signalTimelinePage.Add(signalBrushRow);

            signalTimeline = new SignalTimelineElement();
            signalTimeline.SelectionChanged += RenderSignalTimelineDetails;
            signalTimeline.PaintRequested += PaintSignalStretch;
            signalTimeline.Edited += () =>
            {
                signalDirty = true;
                RenderSignalTimelineDetails();
            };
            signalTimelinePage.Add(signalTimeline);
            signalTimelineHint = LocBindings.NewLabel("signals.timeline.hint");
            signalTimelineHint.AddToClassList("panel-info");
            signalTimelinePage.Add(signalTimelineHint);
            SetSignalBrush(signalBrush);

            signalSegmentLabel = new Label();
            signalSegmentLabel.AddToClassList("panel-info");
            signalTimelinePage.Add(signalSegmentLabel);
            var stateRow = new VisualElement();
            stateRow.AddToClassList("signal-group-row");
            signalStateDropdown = new DropdownField();
            signalStateDropdown.AddToClassList("signal-movement-group");
            LocBindings.Tooltip(signalStateDropdown, "signals.state.tooltip");
            signalStateDropdown.RegisterValueChangedCallback(_ => PaintSignalSegment());
            stateRow.Add(signalStateDropdown);
            signalStartField = new TextField { isDelayed = true };
            signalStartField.AddToClassList("picker-field");
            LocBindings.Tooltip(signalStartField, "signals.segment.start.tooltip");
            signalStartField.RegisterValueChangedCallback(evt => MoveSignalSegment(evt.newValue, true));
            stateRow.Add(signalStartField);
            signalLengthField = new TextField { isDelayed = true };
            signalLengthField.AddToClassList("picker-field");
            LocBindings.Tooltip(signalLengthField, "signals.segment.length.tooltip");
            signalLengthField.RegisterValueChangedCallback(evt => MoveSignalSegment(evt.newValue, false));
            stateRow.Add(signalLengthField);
            signalTimelinePage.Add(stateRow);

            var segmentButtons = new VisualElement();
            segmentButtons.AddToClassList("signal-group-row");
            signalSplitSegmentButton = LocBindings.WithTooltip(LocBindings.NewButton("signals.segment.split", SplitSignalSegment), "signals.segment.split.tooltip");
            signalSplitSegmentButton.AddToClassList("map-control-button");
            segmentButtons.Add(signalSplitSegmentButton);
            signalRemoveSegmentButton = LocBindings.WithTooltip(LocBindings.NewButton("signals.segment.remove", RemoveSignalSegment), "signals.segment.remove.tooltip");
            signalRemoveSegmentButton.AddToClassList("map-control-button");
            segmentButtons.Add(signalRemoveSegmentButton);
            signalRepeatSegmentButton = LocBindings.WithTooltip(LocBindings.NewButton("signals.segment.repeat", RepeatSignalSegment), "signals.segment.repeat.tooltip");
            signalRepeatSegmentButton.AddToClassList("map-control-button");
            segmentButtons.Add(signalRepeatSegmentButton);
            signalTimelinePage.Add(segmentButtons);

            signalIssuesLabel = new Label();
            signalIssuesLabel.AddToClassList("panel-info");
            signalIssuesLabel.AddToClassList("signal-issues");
            signalTimelinePage.Add(signalIssuesLabel);

            // Apply and Discard serve the phases and the timeline alike; the panel puts them below both.
            var applyRow = new VisualElement();
            applyRow.AddToClassList("signal-group-row");
            signalDraftRow = applyRow;
            signalDiscardButton = LocBindings.WithTooltip(LocBindings.NewButton("signals.discard", DiscardSignalDraft), "signals.discard.tooltip");
            signalDiscardButton.AddToClassList("map-control-button");
            applyRow.Add(signalDiscardButton);
            signalApplyButton = LocBindings.WithTooltip(LocBindings.NewButton("signals.apply", ApplySignalDraft), "signals.apply.tooltip");
            signalApplyButton.AddToClassList("map-control-button");
            signalApplyButton.AddToClassList("dialog-primary");
            applyRow.Add(signalApplyButton);
            signalTimelinePage.style.display = DisplayStyle.None;
            return signalTimelinePage;
        }

        private void ShowSignalTab(SignalTab tab)
        {
            signalTab = tab;
            void Show(VisualElement page, SignalTab of)
            {
                if (page != null)
                    page.style.display = tab == of ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Show(signalMovementsPage, SignalTab.Movements);
            Show(signalPhasesPage, SignalTab.Phases);
            Show(signalTimelinePage, SignalTab.Timeline);
            Show(signalCheckPage, SignalTab.Check);
            Show(signalWizardPage, SignalTab.Wizard);
            Show(signalWizardButtons, SignalTab.Wizard);
            signalMovementsTab?.EnableInClassList("panel-tab-active", tab == SignalTab.Movements);
            signalPhasesTab?.EnableInClassList("panel-tab-active", tab == SignalTab.Phases);
            signalTimelineTab?.EnableInClassList("panel-tab-active", tab == SignalTab.Timeline);
            signalCheckTab?.EnableInClassList("panel-tab-active", tab == SignalTab.Check);
            if (tab != SignalTab.Wizard)
                signalProposal = null;
            if (tab == SignalTab.Timeline)
                RenderSignalTimeline();
            if (tab == SignalTab.Phases)
                RenderSignalPhases();
            if (tab == SignalTab.Check)
                RenderSignalCheck();
            UpdateSignalDraftRow();
            UpdateSignalPreview();
            UpdateSignalMarkers();
        }

        /// <summary>Apply and Discard below the phases and the timeline of a junction made here.</summary>
        private void UpdateSignalDraftRow()
        {
            if (signalDraftRow == null)
                return;
            var shown = SignalsEditable && (signalTab == SignalTab.Phases || signalTab == SignalTab.Timeline);
            signalDraftRow.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            signalApplyButton.SetEnabled(shown && signalDirty);
            signalDiscardButton.SetEnabled(shown && signalDirty);
            signalApplyButton.EnableInClassList("signal-dirty", signalDirty);
        }

        /// <summary>Shows the draft again wherever it is shown, after a change.</summary>
        private void RenderSignalDraft()
        {
            if (signalTab == SignalTab.Timeline)
                RenderSignalTimeline();
            if (signalTab == SignalTab.Phases)
                RenderSignalPhases();
            if (signalTab == SignalTab.Check)
                RenderSignalCheck();
            UpdateSignalDraftRow();
            // A preview follows the draft at once: the same program, changed in place.
            if (SignalPreviewActive)
            {
                EnsureSignalClock();
                ShowSignalPreviewState();
            }
        }

        /// <summary>The program the timeline shows: the draft on a junction made here, else the crossing's own.</summary>
        private OmsiSignalProgram ShownSignalProgram => SignalsEditable ? signalDraft : signalProgram;

        /// <summary>A new crossing (or the same one read anew): a fresh draft; a draft with changes is dropped, which the status says.</summary>
        private void ResetSignalDraft(bool sayIfDropped)
        {
            if (sayIfDropped && signalDirty)
                SetStatusT("signals.status.discarded");
            signalDirty = false;
            signalDraft = null;
            if (signalProgram != null && SignalsEditable)
            {
                signalDraft = signalProgram.Copy();
                // A junction made here always has a cycle of its own (0 would mean "the longest light").
                if (!signalDraft.CycleSeconds.HasValue || signalDraft.CycleSeconds.Value <= 0)
                    signalDraft.CycleSeconds = Math.Max(60, signalDraft.CycleLength);
            }
        }

        private void RenderSignalTimeline()
        {
            if (signalTimeline == null)
                return;
            var shown = ShownSignalProgram;
            var editable = SignalsEditable && shown != null;
            signalTimeline.Show(shown, SignalGroupColour, editable);
            signalCycleField.SetValueWithoutNotify(shown != null ? shown.CycleLength.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty);
            signalCycleField.SetEnabled(editable);
            RenderSignalPreviewRequests();
            RenderSignalTimelineDetails();
        }

        /// <summary>The lights whose movements are all footpaths: their heads show red and green only.</summary>
        private HashSet<int> PedestrianSignalLights()
        {
            var lights = new HashSet<int>();
            if (signalCrossing == null)
                return lights;
            foreach (var group in signalCrossing.Movements.Where(movement => movement.Light >= 0).GroupBy(movement => movement.Light))
                if (group.All(movement => movement.Turn == OmsiMovementTurn.Pedestrian))
                    lights.Add(group.Key);
            return lights;
        }

        private void RenderSignalTimelineDetails()
        {
            if (signalTimeline == null)
                return;
            var shown = ShownSignalProgram;
            var editable = SignalsEditable && shown != null;
            var light = signalTimeline.SelectedLight;
            var segments = shown != null && light >= 0 && light < shown.Lights.Count ? OmsiSignalTimeline.Segments(shown, light) : null;
            var index = signalTimeline.SelectedSegment;
            var segment = segments != null && index >= 0 && index < segments.Count ? segments[index] : null;
            var pedestrian = PedestrianSignalLights();
            if (segment == null)
            {
                signalSegmentLabel.text = Loc.T(shown == null || shown.Lights.Count == 0 ? "signals.timeline.empty" : "signals.timeline.pick"); // i18n:keys
                signalStateDropdown.choices = new List<string>();
                signalStateDropdown.SetValueWithoutNotify(string.Empty);
                signalStartField.SetValueWithoutNotify(string.Empty);
                signalLengthField.SetValueWithoutNotify(string.Empty);
            }
            else
            {
                signalSegmentLabel.text = Loc.T("signals.segment", ("group", shown.Lights[light].Name), ("start", Seconds(segment.Start)), ("end", Seconds(segment.End)),
                    ("holds", segment.Holds ? "yes" : "no"));
                signalStateChoices = (pedestrian.Contains(light) ? PedestrianAspects : VehicleAspects).ToList();
                var aspect = OmsiSignalAspects.Of(segment.State);
                if (!signalStateChoices.Contains(aspect))
                    signalStateChoices.Add(aspect);
                signalStateDropdown.choices = signalStateChoices.Select(AspectName).ToList();
                signalStateDropdown.SetValueWithoutNotify(AspectName(aspect));
                signalStartField.SetValueWithoutNotify(Seconds(segment.Start));
                signalLengthField.SetValueWithoutNotify(Seconds(segment.Seconds));
            }
            var canEditSegment = editable && segment != null;
            signalStateDropdown.SetEnabled(canEditSegment);
            signalStartField.SetEnabled(canEditSegment && index > 0);
            signalLengthField.SetEnabled(canEditSegment && index < segments.Count - 1);
            signalRemoveSegmentButton.SetEnabled(canEditSegment && segments.Count > 1);
            signalRepeatSegmentButton.SetEnabled(canEditSegment);
            signalSplitSegmentButton.SetEnabled(canEditSegment);
            signalBrushRow.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
            signalTimelineHint.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateSignalDraftRow();
            signalIssuesLabel.text = shown == null ? string.Empty : DescribeSignalIssues(OmsiSignalTimeline.CheckTiming(shown, pedestrian), shown);
        }

        private static string Seconds(double value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private string DescribeSignalIssues(List<OmsiSignalIssue> issues, OmsiSignalProgram program)
        {
            if (issues.Count == 0)
                return Loc.T("signals.check.clean");
            var lines = new List<string>();
            foreach (var issue in issues.OrderBy(issue => issue.Level).Take(6))
            {
                var level = Loc.T(issue.Level == OmsiSignalIssueLevel.Error ? "signals.check.error" : issue.Level == OmsiSignalIssueLevel.Warning // i18n:keys
                    ? "signals.check.warning" : "signals.check.hint");
                var group = issue.Light >= 0 && issue.Light < program.Lights.Count ? program.Lights[issue.Light].Name : "?";
                string text;
                switch (issue.Kind)
                {
                    case OmsiSignalIssueKind.PhasesBeyondCycle: text = Loc.T("signals.check.beyond", ("group", group), ("seconds", Seconds(issue.Seconds))); break;
                    case OmsiSignalIssueKind.EmptyPhase: text = Loc.T("signals.check.empty", ("group", group), ("seconds", Seconds(issue.Seconds))); break;
                    case OmsiSignalIssueKind.GreenToRed: text = Loc.T("signals.check.green_red", ("group", group), ("seconds", Seconds(issue.Seconds))); break;
                    case OmsiSignalIssueKind.RedToGreen: text = Loc.T("signals.check.red_green", ("group", group), ("seconds", Seconds(issue.Seconds))); break;
                    case OmsiSignalIssueKind.PedestrianAspect: text = Loc.T("signals.check.pedestrian", ("group", group), ("seconds", Seconds(issue.Seconds))); break;
                    default: text = Loc.T("signals.check.hold", ("seconds", Seconds(issue.Seconds))); break;
                }
                lines.Add(level + ": " + text);
            }
            if (issues.Count > 6)
                lines.Add(Loc.T("signals.check.more", ("count", issues.Count - 6)));
            return string.Join("\n", lines);
        }

        private bool TrySignalSeconds(string text, out double seconds)
        {
            return double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
                   !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds >= 0;
        }

        private void EditSignalDraft(Action edit)
        {
            if (!SignalsEditable || signalDraft == null)
                return;
            edit();
            signalDirty = true;
            RenderSignalDraft();
        }

        private void SetSignalCycle(string text)
        {
            if (!TrySignalSeconds(text, out var seconds))
            {
                SetStatusT("signals.status.bad_seconds");
                RenderSignalTimeline();
                return;
            }
            EditSignalDraft(() => OmsiSignalTimeline.SetCycle(signalDraft, seconds));
        }

        private void PaintSignalSegment()
        {
            var light = signalTimeline.SelectedLight;
            var index = signalStateDropdown.index;
            if (signalDraft == null || light < 0 || index < 0 || index >= signalStateChoices.Count)
                return;
            var segments = OmsiSignalTimeline.Segments(signalDraft, light);
            var segment = signalTimeline.SelectedSegment;
            if (segment < 0 || segment >= segments.Count)
                return;
            var state = OmsiSignalAspects.StateOf(signalStateChoices[index]);
            if (OmsiSignalAspects.Of(segments[segment].State) == signalStateChoices[index])
                return;
            EditSignalDraft(() => OmsiSignalTimeline.Paint(signalDraft, light, segments[segment].Start, segments[segment].End, state));
        }

        /// <summary>The start (moving the edge before the stretch) or the length (moving the edge after it) typed in.</summary>
        private void MoveSignalSegment(string text, bool start)
        {
            var light = signalTimeline.SelectedLight;
            var segment = signalTimeline.SelectedSegment;
            if (signalDraft == null || light < 0 || segment < 0)
                return;
            if (!TrySignalSeconds(text, out var seconds))
            {
                SetStatusT("signals.status.bad_seconds");
                RenderSignalTimelineDetails();
                return;
            }
            var segments = OmsiSignalTimeline.Segments(signalDraft, light);
            if (segment >= segments.Count)
                return;
            if (start && segment > 0)
                EditSignalDraft(() => OmsiSignalTimeline.MoveBoundary(signalDraft, light, segment, seconds));
            else if (!start && segment < segments.Count - 1)
                EditSignalDraft(() => OmsiSignalTimeline.MoveBoundary(signalDraft, light, segment + 1, segments[segment].Start + seconds));
        }

        private void SetSignalBrush(OmsiSignalAspect aspect)
        {
            signalBrush = aspect;
            foreach (var pair in signalBrushButtons)
                pair.Value.EnableInClassList("signal-brush-active", pair.Key == aspect);
            if (signalTimeline != null)
                signalTimeline.BrushColour = SignalTimelineElement.ColourOf(OmsiSignalAspects.StateOf(aspect));
        }

        /// <summary>Paints a stretch of one group with the chosen colour and selects it; pedestrian heads show red and green only.</summary>
        private void PaintSignalStretch(int light, double from, double to)
        {
            if (signalDraft == null || light < 0 || light >= signalDraft.Lights.Count)
                return;
            if (PedestrianSignalLights().Contains(light) && Array.IndexOf(PedestrianAspects, signalBrush) < 0)
            {
                SetStatusT("signals.status.brush_pedestrian", ("group", signalDraft.Lights[light].Name));
                return;
            }
            var state = OmsiSignalAspects.StateOf(signalBrush);
            EditSignalDraft(() => OmsiSignalTimeline.Paint(signalDraft, light, from, to, state));
            var segments = OmsiSignalTimeline.Segments(signalDraft, light);
            var middle = (from + to) / 2;
            signalTimeline.Select(light, segments.FindIndex(segment => middle >= segment.Start && middle < segment.End));
        }

        /// <summary>
        /// Splits the selected stretch where it was last clicked (or in its middle) and paints the part after the cut with the
        /// chosen colour; the same colour as the stretch would make it one again, which the status says.
        /// </summary>
        private void SplitSignalSegment()
        {
            var light = signalTimeline.SelectedLight;
            var index = signalTimeline.SelectedSegment;
            if (signalDraft == null || light < 0 || index < 0)
                return;
            var segments = OmsiSignalTimeline.Segments(signalDraft, light);
            if (index >= segments.Count)
                return;
            SplitSignalSegmentAt(light, segments[index], signalTimeline.ClickedSeconds);
        }

        private void SplitSignalSegmentAt(int light, OmsiSignalSegment segment, double clicked)
        {
            if (OmsiSignalAspects.Of(segment.State) == signalBrush)
            {
                SetStatusT("signals.status.split_same", ("colour", AspectName(signalBrush)));
                return;
            }
            // Whole seconds where the stretch is long enough, else tenths; never at its very start or end.
            var round = segment.Seconds >= 2 ? 0 : 1;
            var at = clicked > segment.Start && clicked < segment.End ? clicked : (segment.Start + segment.End) / 2;
            at = Math.Round(at, round);
            var step = round == 0 ? 1.0 : 0.1;
            at = Math.Max(segment.Start + step, Math.Min(segment.End - step, at));
            if (at <= segment.Start || at >= segment.End)
            {
                SetStatusT("signals.status.split_short");
                return;
            }
            PaintSignalStretch(light, at, segment.End);
        }

        private void RemoveSignalSegment()
        {
            var light = signalTimeline.SelectedLight;
            var segment = signalTimeline.SelectedSegment;
            if (signalDraft == null || light < 0 || segment < 0)
                return;
            EditSignalDraft(() => OmsiSignalTimeline.RemoveSegment(signalDraft, light, segment));
            signalTimeline.Select(light, Math.Max(0, segment - 1));
        }

        private void RepeatSignalSegment()
        {
            var light = signalTimeline.SelectedLight;
            var segment = signalTimeline.SelectedSegment;
            if (signalDraft == null || light < 0 || segment < 0)
                return;
            EditSignalDraft(() => OmsiSignalTimeline.DuplicateSegment(signalDraft, light, segment));
        }

        private void DiscardSignalDraft()
        {
            if (!signalDirty)
                return;
            ResetSignalDraft(false);
            RenderSignalDraft();
            SetStatusT("signals.status.discarded");
        }

        private void ApplySignalDraft()
        {
            if (!SignalsEditable || signalDraft == null || !signalDirty)
                return;
            ApplySignals(CurrentSignals(), null, Loc.T("signals.done.timing", ("id", signalJunction.Id), ("cycle", Seconds(signalDraft.CycleLength))));
        }

        /// <summary>
        /// Command line, after <c>-signals</c>: <c>-signalTab timeline</c>, <c>-signalCycle s</c>, <c>-signalPaint g from to
        /// red|redyellow|green|yellow|dark</c> (group from 1, may repeat), <c>-signalPick g s</c> (selects the stretch of group
        /// g at second s), <c>-signalBrush colour</c>, <c>-signalDrag g from to</c> (paints like a drag), <c>-signalSplit g s</c>
        /// (splits like a click at second s and Split), <c>-signalApply</c> and <c>-signalUndo [n]</c>; the program goes to the log
        /// as "Signal program:".
        /// </summary>
        private void RunSignalTimelineArguments(string[] args)
        {
            var tab = Array.IndexOf(args, "-signalTab");
            if (tab >= 0 && tab + 1 < args.Length)
                ShowSignalTab(string.Equals(args[tab + 1], "timeline", StringComparison.OrdinalIgnoreCase) ? SignalTab.Timeline
                    : string.Equals(args[tab + 1], "phases", StringComparison.OrdinalIgnoreCase) ? SignalTab.Phases
                    : string.Equals(args[tab + 1], "check", StringComparison.OrdinalIgnoreCase) ? SignalTab.Check : SignalTab.Movements);
            var cycle = Array.IndexOf(args, "-signalCycle");
            if (cycle >= 0 && cycle + 1 < args.Length)
                SetSignalCycle(args[cycle + 1]);
            for (var index = 0; index + 4 < args.Length; index++)
            {
                if (args[index] != "-signalPaint" || signalDraft == null)
                    continue;
                if (!int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) || group < 1 || group > signalDraft.Lights.Count ||
                    !TrySignalSeconds(args[index + 2], out var from) || !TrySignalSeconds(args[index + 3], out var to))
                    continue;
                if (SignalAspectWords.TryGetValue(args[index + 4], out var aspect))
                    EditSignalDraft(() => OmsiSignalTimeline.Paint(signalDraft, group - 1, from, to, OmsiSignalAspects.StateOf(aspect)));
            }
            // Colour, drags and splits in the order given, as one would paint by hand.
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "-signalBrush" && index + 1 < args.Length && SignalAspectWords.TryGetValue(args[index + 1], out var brushAspect))
                    SetSignalBrush(brushAspect);
                // Like a drag along the row of group g from one second to another, with the colour chosen.
                if (args[index] == "-signalDrag" && index + 3 < args.Length && signalDraft != null &&
                    int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dragGroup) && dragGroup >= 1 &&
                    dragGroup <= signalDraft.Lights.Count && TrySignalSeconds(args[index + 2], out var dragFrom) && TrySignalSeconds(args[index + 3], out var dragTo))
                    PaintSignalStretch(dragGroup - 1, Math.Min(dragFrom, dragTo), Math.Min(signalDraft.CycleLength, Math.Max(dragFrom, dragTo)));
                // Like a click on group g at second s, then Split.
                if (args[index] == "-signalSplit" && index + 2 < args.Length && signalDraft != null &&
                    int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var splitGroup) && splitGroup >= 1 &&
                    splitGroup <= signalDraft.Lights.Count && TrySignalSeconds(args[index + 2], out var splitAt))
                {
                    var stretch = OmsiSignalTimeline.Segments(signalDraft, splitGroup - 1).Find(segment => splitAt >= segment.Start && splitAt < segment.End);
                    if (stretch != null)
                        SplitSignalSegmentAt(splitGroup - 1, stretch, splitAt);
                }
            }
            var pick = Array.IndexOf(args, "-signalPick");
            if (pick >= 0 && pick + 2 < args.Length && ShownSignalProgram != null &&
                int.TryParse(args[pick + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pickGroup) && pickGroup >= 1 &&
                pickGroup <= ShownSignalProgram.Lights.Count && TrySignalSeconds(args[pick + 2], out var pickSecond))
            {
                var segments = OmsiSignalTimeline.Segments(ShownSignalProgram, pickGroup - 1);
                signalTimeline.Select(pickGroup - 1, segments.FindIndex(segment => pickSecond >= segment.Start && pickSecond < segment.End));
            }
            if (Array.IndexOf(args, "-signalApply") >= 0)
                ApplySignalDraft();
            var undo = Array.IndexOf(args, "-signalUndo");
            if (undo >= 0)
            {
                var count = undo + 1 < args.Length && int.TryParse(args[undo + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var steps) ? steps : 1;
                for (var step = 0; step < count; step++)
                {
                    StepHistory(undoSteps, redoSteps, true);
                    Debug.Log("Undo: " + statusLabel?.text);
                }
            }
            var program = ShownSignalProgram;
            if (program != null)
                for (var light = 0; light < program.Lights.Count; light++)
                    Debug.Log("Signal program: " + program.Lights[light].Name + " (cycle " + Seconds(program.CycleLength) + ") " +
                              string.Join(" ", program.Lights[light].Phases.Select(phase => phase.State + "/" + Seconds(phase.Seconds))) + (signalDirty ? " [draft]" : string.Empty));
        }
    }
}
