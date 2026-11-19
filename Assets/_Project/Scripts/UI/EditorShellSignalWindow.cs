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
    /// The frame of the signals window (ADR 098; the signal tool as one window that floats beside the map): the navigation, the preview
    /// that stays in view, the footer with the state of the changes, and the question that is asked before changes not yet applied would be
    /// lost. What each area shows is built by the page files (<c>EditorShellSignals</c>, <c>EditorShellSignalPhases</c>, ...).
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private readonly SignalGroupIdentities signalIdentity = new SignalGroupIdentities();
        private readonly SignalLeaveGuard signalGuard = new SignalLeaveGuard();
        private SignalTab signalLastPhasePage = SignalTab.Phases;
        private SignalTab signalLastAssistantPage = SignalTab.Wizard;
        private Button signalAddGroupButton;

        /// <summary>A question the window asks with a second click (replace groups, apply, overwrite, delete); shown red in the footer and the status bar.</summary>
        private string signalConfirmText = string.Empty;

        // The preview below the work area.
        private SignalTimelineElement signalPreviewTrack;
        private ScrollView signalPreviewTrackScroll;
        private Slider signalPreviewSlider;
        private Label signalPreviewCycleLabel;
        private Label signalPreviewEmpty;
        private bool signalPreviewUpdating;

        /// <summary>The colour of group <paramref name="light"/> of the crossing: it stays with the group when other groups come and go.</summary>
        private Color SignalColourOf(int light)
        {
            return signalIdentity.ColourOf(light);
        }

        /// <summary>The colour of a group the assistant or a template proposes; they have no id yet, so the colours come in the order of the palette.</summary>
        private static Color SignalGroupColour(int slot)
        {
            return SignalGroupIdentities.ColourOfSlot(slot);
        }

        /// <summary>A button with an icon and a visible text, for the work areas (the satellite window's buttons).</summary>
        private static Button SignalLabelButton(string icon, string textKey, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            LocBindings.Tooltip(button, tooltipKey); // i18n:keys
            button.AddToClassList("map-control-button");
            button.AddToClassList("signal-label-button");
            button.Add(new IconElement(icon));
            var label = LocBindings.NewLabel(textKey); // i18n:keys
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);
            return button;
        }

        /// <summary>A text for a row of a work area, like the satellite window's notes.</summary>
        private static Label SignalNote(string key)
        {
            var label = LocBindings.NewLabel(key); // i18n:keys
            label.AddToClassList("panel-info");
            return label;
        }

        // ----- navigation -----

        /// <summary>The navigation chose an area: its page opens, the one last open in an area that has two.</summary>
        private void ChooseSignalArea(SignalArea area)
        {
            ChooseSignalPage(SignalWindowRules.PageOf(area, signalLastPhasePage, signalLastAssistantPage));
        }

        private void ChooseSignalPage(SignalTab page)
        {
            if (signalCrossing == null)
                return;
            if (page == SignalTab.Wizard)
            {
                if (SignalsEditable)
                    OpenSignalWizard();
                else
                    OpenSignalTemplates();
            }
            else if (page == SignalTab.Templates)
                OpenSignalTemplates();
            else
                ShowSignalTab(page);
        }

        // ----- preview -----

        /// <summary>
        /// The preview, always under the work area: play and pause, back to the start, the steps, a scrubber over the cycle, the second and the
        /// cycle, the speed and the loop, the switches for requests, and a track for every group in the colours of its light with one marker over all.
        /// </summary>
        private void CreateSignalPreview(VisualElement host)
        {
            var title = LocBindings.NewLabel("signals.preview.title");
            title.AddToClassList("signal-preview-title");
            host.Add(title);
            var row = new VisualElement();
            row.AddToClassList("signal-preview-row");
            host.Add(row);
            var transport = OptionControls.Group(row);
            signalPreviewPlayButton = OptionControls.Choice(transport, "play", "signals.preview.play", "signals.preview.play.tooltip", ToggleSignalPreview);
            OptionControls.Choice(transport, "skip-back", "signals.preview.restart", "signals.preview.restart.tooltip", RestartSignalPreview);
            OptionControls.Choice(transport, "chevron-left", "signals.preview.back", "signals.preview.back.tooltip", () => StepSignalPreview(-1));
            OptionControls.Choice(transport, "chevron-right", "signals.preview.forward", "signals.preview.forward.tooltip", () => StepSignalPreview(1));

            signalPreviewSlider = new Slider(0f, 60f);
            signalPreviewSlider.AddToClassList("new-map-slider");
            signalPreviewSlider.AddToClassList("signal-preview-slider");
            LocBindings.Tooltip(signalPreviewSlider, "signals.preview.scrub.tooltip");
            signalPreviewSlider.RegisterValueChangedCallback(evt =>
            {
                if (signalPreviewUpdating)
                    return;
                // Dragging the scrubber holds the playback (the clock would fight the pointer); Play goes on from where it was put.
                if (signalPreviewPlaying)
                {
                    signalPreviewPlaying = false;
                    SetSignalPlayIcon(false);
                }
                JumpSignalPreview(Math.Round(evt.newValue, 1));
            });
            row.Add(signalPreviewSlider);

            signalPreviewTimeField = new TextField { isDelayed = true };
            signalPreviewTimeField.AddToClassList("signal-preview-time");
            signalPreviewTimeField.RegisterValueChangedCallback(evt =>
            {
                if (signalPreviewUpdating)
                    return;
                if (TrySignalSeconds(evt.newValue.Replace("(" + Loc.T("signals.preview.held") + ")", string.Empty), out var seconds))
                    JumpSignalPreview(seconds);
                else
                    SetStatusT("signals.status.bad_seconds");
            });
            OptionControls.ValueBox(row, "clock", signalPreviewTimeField, null, "signals.preview.time", "signals.preview.time.tooltip");
            signalPreviewCycleLabel = new Label();
            signalPreviewCycleLabel.AddToClassList("signal-preview-cycle");
            row.Add(signalPreviewCycleLabel);

            signalPreviewSpeedField = new DropdownField(SignalPreviewSpeeds.Select(speed => speed.ToString("0.#", CultureInfo.InvariantCulture) + "x").ToList(), 1);
            signalPreviewSpeedField.AddToClassList("signal-movement-group");
            signalPreviewSpeedField.AddToClassList("signal-preview-speed");
            LocBindings.TitledTooltip(signalPreviewSpeedField, "signals.preview.speed", "signals.preview.speed.tooltip");
            signalPreviewSpeedField.RegisterValueChangedCallback(_ => signalPreviewSpeed = SignalPreviewSpeeds[Math.Max(0, signalPreviewSpeedField.index)]);
            row.Add(signalPreviewSpeedField);
            signalPreviewLoopToggle = OptionControls.Switch(row, "repeat", "signals.preview.loop", "signals.preview.loop.tooltip", true);

            // The switches for requests: one for every group a stop or jump point waits for.
            signalPreviewRequests = new VisualElement();
            signalPreviewRequests.AddToClassList("signal-phase-groups");
            host.Add(signalPreviewRequests);

            signalPreviewEmpty = LocBindings.NewLabel("signals.preview.empty");
            signalPreviewEmpty.AddToClassList("panel-info");
            host.Add(signalPreviewEmpty);
            signalPreviewTrackScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            signalPreviewTrackScroll.AddToClassList("signal-preview-tracks");
            signalPreviewTrack = new SignalTimelineElement();
            signalPreviewTrack.AddToClassList("signal-preview-track");
            signalPreviewTrackScroll.Add(signalPreviewTrack);
            host.Add(signalPreviewTrackScroll);
        }

        /// <summary>The tracks of the preview show the program shown (the draft where there is one); rebuilt when the groups or the cycle change, not on every tick.</summary>
        private void RenderSignalPreviewTracks()
        {
            if (signalPreviewTrack == null)
                return;
            var program = signalCrossing == null ? null : ShownSignalProgram ?? signalProgram;
            var has = program != null && program.Lights.Count > 0;
            signalPreviewEmpty.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            signalPreviewEmpty.text = Loc.T(signalCrossing == null ? "signals.preview.nothing" : "signals.preview.empty"); // i18n:keys
            signalPreviewTrackScroll.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            signalPreviewTrack.Show(has ? program : null, SignalColourOf, false);
            RenderSignalPreviewRequests();
            ShowSignalPreviewScale(program);
            signalPreviewTrack.SetPlayhead(signalClock?.Time);
            // Without a running preview the second reads 0.0, not an empty field.
            if (signalClock == null && signalPreviewTimeField != null)
                signalPreviewTimeField.SetValueWithoutNotify(has ? "0.0" : string.Empty);
        }

        /// <summary>The scrubber spans the cycle and the cycle is written beside the second; the second is kept within a cycle that became shorter.</summary>
        private void ShowSignalPreviewScale(OmsiSignalProgram program)
        {
            var cycle = program != null && program.Lights.Count > 0 ? program.CycleLength : 0;
            signalPreviewUpdating = true;
            signalPreviewSlider.highValue = (float)Math.Max(1, cycle);
            signalPreviewSlider.SetValueWithoutNotify((float)SignalWindowRules.ClampPreviewTime(signalClock?.Time ?? 0, cycle));
            signalPreviewCycleLabel.text = Loc.T("signals.preview.cycle", ("cycle", Seconds(cycle)));
            signalPreviewUpdating = false;
            var usable = cycle > 0;
            signalPreviewSlider.SetEnabled(usable);
            signalPreviewTimeField.SetEnabled(usable);
            if (!usable)
                signalPreviewPlayButton.SetEnabled(false);
            else
                signalPreviewPlayButton.SetEnabled(true);
        }

        // ----- footer -----

        private void CreateSignalFooter()
        {
            LocBindings.Text(signalWindow.DiscardButton, "signals.discard");
            LocBindings.Tooltip(signalWindow.DiscardButton, "signals.discard.tooltip");
            signalWindow.DiscardButton.clicked += DiscardSignalDraft;
            LocBindings.Text(signalWindow.ApplyButton, "signals.apply");
            LocBindings.Tooltip(signalWindow.ApplyButton, "signals.apply.tooltip");
            signalWindow.ApplyButton.clicked += ApplySignalDraft;
            LocBindings.Text(signalWindow.PromptApply, "signals.prompt.apply");
            LocBindings.Text(signalWindow.PromptDiscard, "signals.prompt.discard");
            LocBindings.Text(signalWindow.PromptKeep, "signals.prompt.keep");
            signalWindow.PromptApply.clicked += () => AnswerSignalPrompt(SignalPromptChoice.Apply);
            signalWindow.PromptDiscard.clicked += () => AnswerSignalPrompt(SignalPromptChoice.Discard);
            signalWindow.PromptKeep.clicked += () => AnswerSignalPrompt(SignalPromptChoice.KeepEditing);
        }

        /// <summary>Shows the state of the changes in the footer, enables what can act now, and writes the crossing beside the title.</summary>
        private void UpdateSignalRows()
        {
            if (signalWindow == null)
                return;
            var editable = SignalsEditable;
            var state = SignalWindowRules.ChangeState(editable, signalDirty);
            var actions = SignalWindowRules.DraftActionsEnabled(editable, signalDirty);
            // A question about changes that were applied or discarded by another way (the footer, undo) is stale: it goes with the way out that waited.
            var dropped = signalGuard.DropIfStale(signalDirty && editable);
            if (dropped != null)
            {
                signalWindow.ShowPrompt(null, false);
                dropped.Cancelled?.Invoke();
            }
            signalWindow.ApplyButton.SetEnabled(actions);
            signalWindow.DiscardButton.SetEnabled(actions);
            var text = SignalFooterText(state);
            signalWindow.StatusLabel.text = text;
            signalWindow.StatusLabel.tooltip = text;
            signalWindow.StatusLabel.EnableInClassList("signal-footer-dirty", state == SignalChangeState.Unapplied);
            signalWindow.StatusLabel.EnableInClassList("signal-footer-warning", SignalFooterWarning(state));
            signalWindow.EnableInClassList("signal-dirty", state == SignalChangeState.Unapplied);
            signalAddGroupButton?.SetEnabled(editable);
            // A crossing that is only read can still have its program kept as a template.
            var hasProgram = signalProgram != null && signalProgram.Lights.Count > 0;
            var assistant = signalCrossing != null && (editable || hasProgram);
            signalWindow.SetAreaEnabled(SignalArea.Assistant, assistant);
            signalWindow.SetAreaEnabled(SignalArea.Groups, signalCrossing != null);
            signalWindow.SetAreaEnabled(SignalArea.Phases, signalCrossing != null);
            signalWindow.SetAreaEnabled(SignalArea.Check, signalCrossing != null);
            signalWindow.SetCrossingText(signalCrossing != null && signalProgram != null && signalJunction != null
                ? Loc.T("signals.junction", ("id", signalJunction.Id), ("approaches", signalCrossing.Approaches.Count), ("movements", signalCrossing.Movements.Count),
                    ("groups", signalProgram.Lights.Count))
                : null);
        }

        /// <summary>What the footer says: the question that waits for a second click, else why the crossing is only read, else the state of the draft.</summary>
        private string SignalFooterText(SignalChangeState state)
        {
            if (signalCrossing == null)
                return string.Empty;
            if (signalConfirmText.Length > 0)
                return signalConfirmText;
            if (state == SignalChangeState.ReadOnly)
                return SignalReadOnlyReason();
            return Loc.T(state == SignalChangeState.Unapplied ? "signals.footer.unapplied" : "signals.footer.applied"); // i18n:keys
        }

        private bool SignalFooterWarning(SignalChangeState state)
        {
            return signalConfirmText.Length > 0 || (state == SignalChangeState.ReadOnly && signalMade != null && CanEdit);
        }

        private string SignalReadOnlyReason()
        {
            return signalMade != null ? Loc.T(CanEdit ? "signals.owner.mismatch" : "signals.owner.readonly") : Loc.T("signals.owner.omsi"); // i18n:keys
        }

        /// <summary>
        /// The window's line in the status bar: why the crossing cannot be changed (when that is so), and what the next click does or what a
        /// second click would do. Red for a question that replaces something and for a junction that no longer matches its file.
        /// </summary>
        private void RefreshSignalHint()
        {
            if (signalWindow == null)
                return;
            string crossing = null, reason = null, next = null;
            var warning = false;
            if (signalCrossing != null && signalProgram != null && signalJunction != null)
            {
                crossing = Loc.T("signals.junction", ("id", signalJunction.Id), ("approaches", signalCrossing.Approaches.Count),
                    ("movements", signalCrossing.Movements.Count), ("groups", signalProgram.Lights.Count));
                if (!SignalsEditable)
                {
                    reason = SignalReadOnlyReason();
                    warning = signalMade != null && CanEdit;
                }
                if (signalConfirmText.Length > 0)
                {
                    next = signalConfirmText;
                    warning = true;
                }
                else if (signalTab == SignalTab.Movements)
                    next = signalPlanText;
                else if (signalTab == SignalTab.Timeline && SignalsEditable)
                    next = signalSegmentText;
            }
            ShowToolHint(signalWindow, SignalWindowRules.Hint(crossing, reason, next), warning);
            UpdateSignalFooterOnly();
        }

        /// <summary>The footer follows the confirmation text, which changes without the rows being rebuilt.</summary>
        private void UpdateSignalFooterOnly()
        {
            if (signalWindow == null)
                return;
            // The text goes back to the state of the draft when the question is gone (the confirmation text changes without the rows being rebuilt).
            var state = SignalWindowRules.ChangeState(SignalsEditable, signalDirty);
            var text = SignalFooterText(state);
            signalWindow.StatusLabel.text = text;
            signalWindow.StatusLabel.tooltip = text;
            signalWindow.StatusLabel.EnableInClassList("signal-footer-warning", SignalFooterWarning(state));
        }

        /// <summary>The language changed: the pages and the hint are written again; a draft with changes stays.</summary>
        private void RefreshSignalTexts()
        {
            foreach (var pair in signalBrushButtons)
                pair.Value.tooltip = AspectName(pair.Key);
            signalTemplateName.textEdition.placeholder = Loc.T("signals.templates.save.name"); // i18n:keys
            signalWindow?.ShowArea(signalTab);
            if (signalCrossing == null)
            {
                RenderSignalPanel();
                UpdateSignalRows();
                RenderSignalPreviewTracks();
                return;
            }
            RenderSignalPanel();
            UpdateSignalPlan();
            RenderSignalDraft();
            if (signalTab == SignalTab.Wizard)
                RenderSignalWizard();
            else if (signalTab == SignalTab.Templates)
                RenderSignalTemplates();
            if (signalGuard.Asking)
                ShowSignalPrompt();
            RefreshSignalHint();
            UpdateSignalRows();
        }

        // ----- changes not applied -----

        /// <summary>
        /// True when the signals tool may be left now. With a draft that has changes the question is asked inside the window (apply, discard,
        /// keep editing) and the way out waits for the answer, so nothing is dropped unnoticed.
        /// </summary>
        private bool SignalMayLeave(SignalExit exit)
        {
            if (signalGuard.Request(SignalsEditable && signalDirty, exit))
            {
                signalWindow?.ShowPrompt(null, false);
                return true;
            }
            ShowSignalPrompt();
            return false;
        }

        /// <summary>
        /// The gate of every change that is written at once: with a timing draft that has changes it would take the draft along, and Discard
        /// would then have nothing to go back to. So the question is asked first and the change runs again after Apply or Discard.
        /// </summary>
        private bool SignalGate(Action again)
        {
            return SignalMayLeave(new SignalExit { Kind = SignalExitKind.Action, Payload = again });
        }

        /// <summary>Runs <paramref name="what"/> with the draft kept: reading the crossing anew after it must not drop a timing draft (heads changed, nothing placed).</summary>
        private void KeepingSignalDraft(Action what)
        {
            var was = signalApplying;
            signalApplying = true;
            try
            {
                what();
            }
            finally
            {
                signalApplying = was;
            }
        }

        private void ShowSignalPrompt()
        {
            if (signalWindow == null)
                return;
            signalWindow.Collapsed = false;
            signalWindow.ShowPrompt(SignalWindowRules.PromptKey(signalGuard.Pending?.Kind), true); // i18n:keys
            SetStatusT("signals.prompt.status");
        }

        /// <summary>
        /// True when something that replaces or ends what the signals tool works on may go on now. Another tool than the signals tool has
        /// nothing to ask; with changes not applied the question is asked and <paramref name="again"/> runs after Apply or Discard (Keep editing
        /// drops it). <paramref name="kind"/> says what waits: a change of view cancels with Keep editing, an action of the shell runs again.
        /// </summary>
        private bool SignalToolMayYield(SignalExitKind kind, Action again, Action cancelled = null)
        {
            if (activeTool != EditorTool.Signals)
                return true;
            if (SignalMayLeave(new SignalExit { Kind = kind, Payload = again, Cancelled = cancelled }))
                return true;
            // A list above the bottom bar that was open for the action must not cover the question.
            ShowBarPopup(false);
            return false;
        }

        private void AnswerSignalPrompt(SignalPromptChoice choice)
        {
            // Apply and Discard clean the draft, which would make the question look stale before its answer is read.
            signalGuard.BeginAnswer();
            try
            {
                if (choice == SignalPromptChoice.Apply)
                    ApplySignalDraft();
                else if (choice == SignalPromptChoice.Discard)
                    DiscardSignalDraft();
            }
            finally
            {
                signalGuard.EndAnswer();
            }
            var waiting = signalGuard.Pending;
            var exit = signalGuard.Answer(choice, signalDirty);
            signalWindow.ShowPrompt(null, signalGuard.Asking);
            if (exit == null)
            {
                if (choice == SignalPromptChoice.KeepEditing)
                    waiting?.Cancelled?.Invoke();
                // An Apply that failed leaves the draft and the question: it is shown again with its text.
                if (signalGuard.Asking)
                {
                    ShowSignalPrompt();
                    return;
                }
                // Keep editing: a field that was typed in for the change that waited shows what the file holds again.
                if (choice == SignalPromptChoice.KeepEditing && signalCrossing != null)
                {
                    RenderSignalPanel();
                    RenderSignalDraft();
                }
                return;
            }
            if (exit.Kind == SignalExitKind.Action || exit.Kind == SignalExitKind.ViewChange)
                ((Action)exit.Payload)();
            else if (exit.Kind == SignalExitKind.OtherTool)
                SetTool((EditorTool)exit.Payload);
            else if (exit.Kind == SignalExitKind.OtherCrossing)
            {
                var target = (SignalCrossingTarget)exit.Payload;
                signalSelected = -1;
                var record = target.Tile.Objects.Find(candidate => candidate.Id == target.ObjectId);
                if (record == null || !TargetSignalJunction(target.Tile, record))
                    SetStatusT("signals.status.none_here");
            }
        }
    }

    /// <summary>The crossing a click chose while a draft with changes waited for an answer: its tile and the id of its object, so it is found again in the records that Apply reads anew.</summary>
    internal sealed class SignalCrossingTarget
    {
        public OmsiTileSummary Tile;
        public int ObjectId;
    }

    /// <summary>
    /// Keeps the keyboard focus in a page that is rebuilt: fields carry a key, and the field with the same key gets the focus back, so
    /// typing in one field and going on to the next with Tab is not interrupted by the rebuild the first one causes.
    /// </summary>
    internal static class SignalFocus
    {
        /// <summary>The key of the field inside <paramref name="root"/> that has the focus, or null.</summary>
        public static string Capture(VisualElement root)
        {
            var focused = root?.panel?.focusController?.focusedElement as VisualElement;
            for (var element = focused; element != null && element != root; element = element.parent)
                if (element.userData is string key && key.StartsWith("focus:", StringComparison.Ordinal))
                    return key;
            return null;
        }

        /// <summary>Gives the focus back to the field with the key, after the page was rebuilt.</summary>
        public static void Restore(VisualElement root, string key)
        {
            if (key == null || root == null)
                return;
            var target = root.Query<VisualElement>().Where(element => element.userData is string text && text == key).First();
            target?.Focus();
        }
    }
}
