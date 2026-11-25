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
    /// The template library of the signals tool (ADR 085, milestone 6; §12 of the brief): the basic programs the program brings
    /// and the user's own, kept as files that name no object. A template is checked against the crossing before it is applied:
    /// movements it leaves without a light can be given one first. Saving works on any crossing (also OMSI's own, which are only
    /// read); applying needs a junction made here.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement signalTemplatesPage;
        private VisualElement signalTemplateList;
        private VisualElement signalTemplateDetails;
        private Label signalTemplatesInfo;
        private TextField signalTemplateName;
        private Button signalTemplateApplyButton;
        private Button signalTemplateSaveButton;
        private SignalTimelineElement signalTemplateTimeline;
        private Toggle signalTemplateHeadsToggle;
        private List<OmsiSignalTemplate> signalUserTemplates = new List<OmsiSignalTemplate>();
        private OmsiSignalTemplate signalTemplateChosen;
        private readonly Dictionary<int, int> signalTemplateOverrides = new Dictionary<int, int>();
        private int[] signalTemplateLights;
        private bool signalTemplateConfirm;
        private string signalTemplateOverwriting;
        private string signalTemplateDeleting;

        /// <summary>Rows shown for movements without a light; the rest are counted.</summary>
        private const int MostTemplateRows = 12;

        private static string SignalTemplatesFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OMSI MapWorks", "Signal templates");
        }

        /// <summary>The templates page: the templates and the name to keep this crossing's program under on the left, the chosen template on the right.</summary>
        private VisualElement CreateSignalTemplatesPage()
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("signal-wrapper-page");
            wrapper.Add(BuildSignalTemplatesToolbar());
            var page = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            page.AddToClassList("signal-wrapper-scroll");
            page.contentContainer.AddToClassList("signal-columns");
            wrapper.Add(page);
            signalTemplatesPage = wrapper;
            var left = new VisualElement();
            left.AddToClassList("signal-column");
            left.AddToClassList("signal-column-templates");
            var right = new VisualElement();
            right.AddToClassList("signal-column");
            right.AddToClassList("signal-column-template-details");
            page.Add(left);
            page.Add(right);
            signalTemplatesInfo = new Label();
            signalTemplatesInfo.AddToClassList("panel-info");
            signalTemplatesInfo.AddToClassList("mf-mt-0");
            left.Add(signalTemplatesInfo);
            signalTemplateList = new VisualElement();
            left.Add(signalTemplateList);
            signalTemplateDetails = new VisualElement();
            right.Add(signalTemplateDetails);
            signalTemplateTimeline = new SignalTimelineElement();

            wrapper.style.display = DisplayStyle.None;
            return wrapper;
        }

        /// <summary>The templates' bar: apply the chosen one (red for the second click that replaces), cancel, whether heads are placed, and keeping this program under a name.</summary>
        private VisualElement BuildSignalTemplatesToolbar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("signal-toolbar");
            signalTemplateApplyButton = SignalLabelButton("check", "signals.templates.apply", "signals.templates.apply.tooltip", () => ApplySignalTemplate(false));
            signalTemplateApplyButton.AddToClassList("option-icon-primary");
            bar.Add(signalTemplateApplyButton);
            bar.Add(SignalLabelButton("arrow-left", "signals.templates.back", "signals.templates.back.tooltip", () => ShowSignalTab(SignalTab.Movements)));
            var options = new VisualElement();
            options.AddToClassList("signal-toolbar-switches");
            signalTemplateHeadsToggle = OptionControls.Switch(options, "traffic-light", "signals.templates.heads", "signals.templates.heads.tooltip", true);
            bar.Add(options);
            // Keeping this crossing's program: the name, then the save button (red while a second click would replace a template of that name).
            signalTemplateName = new TextField { isDelayed = false };
            LocBindings.Tooltip(signalTemplateName, "signals.templates.save.name.tooltip");
            signalTemplateName.textEdition.placeholder = Loc.T("signals.templates.save.name"); // i18n:keys
            signalTemplateName.AddToClassList("picker-field");
            signalTemplateName.AddToClassList("signal-save-name");
            signalTemplateName.RegisterValueChangedCallback(_ =>
            {
                signalTemplateOverwriting = null;
                UpdateSignalTemplateButtons();
            });
            bar.Add(signalTemplateName);
            signalTemplateSaveButton = SignalLabelButton("save", "signals.templates.save", "signals.templates.save.tooltip", SaveSignalTemplate);
            bar.Add(signalTemplateSaveButton);
            return bar;
        }

        private void OpenSignalTemplates()
        {
            if (signalCrossing == null)
                return;
            LoadSignalUserTemplates();
            signalTemplateConfirm = false;
            signalTemplateOverwriting = null;
            signalTemplateDeleting = null;
            if (signalTemplateChosen == null)
                ChooseSignalTemplate(OmsiSignalTemplates.BuiltIn.FirstOrDefault(template =>
                    OmsiSignalTemplateMatch.Match(template, signalCrossing, SignalOwnLaneMovements(), null, SignalCrossingFootpaths()).Fits) ?? OmsiSignalTemplates.BuiltIn[0]);
            ShowSignalTab(SignalTab.Templates);
        }

        private void LoadSignalUserTemplates()
        {
            var (templates, problems) = OmsiSignalTemplates.Load(SignalTemplatesFolder());
            signalUserTemplates = templates;
            foreach (var problem in problems)
                Debug.LogWarning("Signal templates: " + problem.File + ": " + problem.Problem);
            if (problems.Count > 0)
                SetStatusT("signals.templates.status.load_problems", ("count", problems.Count), ("folder", SignalTemplatesFolder()));
        }

        /// <summary>The footpaths across arms that a template's pedestrian group can bind: those with a way of their own (ADR 086).</summary>
        private HashSet<int> SignalCrossingFootpaths()
        {
            return signalCrossing == null
                ? new HashSet<int>()
                : new HashSet<int>(signalCrossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && WayOf(movement) != null).Select(movement => movement.Index));
        }

        /// <summary>The movements that start in a left-turn lane of their own; only a junction made here knows its lanes.</summary>
        private HashSet<int> SignalOwnLaneMovements()
        {
            return signalCrossing != null && signalLayout != null ? OmsiSignalWizard.TurnLaneMovements(signalCrossing, signalLayout) : new HashSet<int>();
        }

        private void ChooseSignalTemplate(OmsiSignalTemplate template)
        {
            signalTemplateChosen = template;
            signalTemplateOverrides.Clear();
            signalTemplateConfirm = false;
            signalTemplateDeleting = null;
        }

        private string SignalTemplateName(OmsiSignalTemplate template)
        {
            switch (template.Id)
            {
                case OmsiSignalTemplates.TwoPhase: return Loc.T("signals.template.two_phase");
                case OmsiSignalTemplates.FourArms: return Loc.T("signals.template.four_arms");
                case OmsiSignalTemplates.TJunction: return Loc.T("signals.template.t_junction");
                case OmsiSignalTemplates.SeparateLeft: return Loc.T("signals.template.separate_left");
                case OmsiSignalTemplates.PedestrianRequest: return Loc.T("signals.template.pedestrian_request");
                default: return template.Name;
            }
        }

        private string SignalTemplateDescription(OmsiSignalTemplate template)
        {
            switch (template.Id)
            {
                case OmsiSignalTemplates.TwoPhase: return Loc.T("signals.template.two_phase.desc");
                case OmsiSignalTemplates.FourArms: return Loc.T("signals.template.four_arms.desc");
                case OmsiSignalTemplates.TJunction: return Loc.T("signals.template.t_junction.desc");
                case OmsiSignalTemplates.SeparateLeft: return Loc.T("signals.template.separate_left.desc");
                case OmsiSignalTemplates.PedestrianRequest: return Loc.T("signals.template.pedestrian_request.desc");
                default: return template.Description;
            }
        }

        /// <summary>The template's program with the group names of a built-in template in the language of the program.</summary>
        private OmsiSignalProgram SignalTemplateProgram(OmsiSignalTemplate template)
        {
            var program = template.Program.Copy();
            program.PathLights.Clear();
            if (!template.BuiltIn)
                return program;
            var footNumber = 0;
            for (var light = 0; light < program.Lights.Count; light++)
            {
                var roles = template.Roles[light];
                // The group of the pedestrian crossings is named as the assistant names it.
                if (roles.Count > 0 && roles.All(role => role.IsPedestrian))
                {
                    program.Lights[light].Name = Loc.T("signals.wizard.group_foot", ("n", ++footNumber));
                    continue;
                }
                var arms = string.Join("+", roles.Select(role => role.Arm + 1).Distinct().OrderBy(arm => arm).Select(arm => arm.ToString(CultureInfo.InvariantCulture)));
                program.Lights[light].Name = Loc.T(roles.Count > 0 && roles.All(role => role.Lane == OmsiSignalRoleLane.OwnLane) ? "signals.wizard.group_left" : "signals.wizard.group", ("arms", arms)); // i18n:keys
            }
            return program;
        }

        private OmsiSignalTemplateMatch SignalTemplateMatch()
        {
            return OmsiSignalTemplateMatch.Match(signalTemplateChosen, signalCrossing, SignalOwnLaneMovements(), signalTemplateOverrides, SignalCrossingFootpaths());
        }

        private void RenderSignalTemplates()
        {
            if (signalTemplatesPage == null)
                return;
            signalTemplateList.Clear();
            signalTemplateDetails.Clear();
            signalTemplateLights = null;
            if (signalCrossing == null)
            {
                signalTemplatesInfo.text = Loc.T("signals.click_junction");
                UpdateSignalTemplateButtons();
                return;
            }
            var vehicles = signalCrossing.Movements.Count(movement => movement.Approach >= 0 && movement.Turn != OmsiMovementTurn.Pedestrian);
            signalTemplatesInfo.text = Loc.T(SignalsEditable ? "signals.templates.info" : "signals.templates.info_readonly", // i18n:keys
                ("approaches", signalCrossing.Approaches.Count), ("movements", vehicles), ("groups", signalProgram.Lights.Count));

            AddTemplateHeading("signals.templates.builtin");
            foreach (var template in OmsiSignalTemplates.BuiltIn)
                signalTemplateList.Add(SignalTemplateRow(template));
            AddTemplateHeading("signals.templates.user");
            if (signalUserTemplates.Count == 0)
            {
                var none = LocBindings.NewLabel("signals.templates.user_none");
                none.AddToClassList("panel-info");
                signalTemplateList.Add(none);
            }
            foreach (var template in signalUserTemplates)
                signalTemplateList.Add(SignalTemplateRow(template));

            if (signalTemplateChosen != null)
                RenderSignalTemplateDetails();
            UpdateSignalTemplateButtons();
            UpdateSignalPreview();
        }

        private void AddTemplateHeading(string key)
        {
            var heading = LocBindings.NewLabel(key); // i18n:keys
            heading.AddToClassList("inspector-section-heading");
            signalTemplateList.Add(heading);
        }

        private VisualElement SignalTemplateRow(OmsiSignalTemplate template)
        {
            var row = new VisualElement();
            row.AddToClassList("signal-movement-row");
            row.EnableInClassList("signal-movement-selected", ReferenceEquals(template, signalTemplateChosen) ||
                                                              (signalTemplateChosen != null && !template.BuiltIn && !signalTemplateChosen.BuiltIn && signalTemplateChosen.Name == template.Name));
            var label = new Label(SignalTemplateName(template));
            label.AddToClassList("signal-movement-text");
            row.Add(label);
            if (!template.BuiltIn)
            {
                var name = template.Name;
                var confirming = signalTemplateDeleting == name;
                var delete = new Button(() => DeleteSignalTemplate(name));
                delete.AddToClassList("option-icon");
                delete.AddToClassList("option-icon-danger");
                delete.Add(new IconElement("trash-2"));
                delete.tooltip = LocBindings.CardTooltip(Loc.T("signals.templates.delete"), Loc.T(confirming ? "signals.templates.delete.confirm" : "signals.templates.delete.tooltip")); // i18n:keys
                delete.EnableInClassList("option-icon-confirm", confirming);
                row.Add(delete);
            }
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                    return;
                ChooseSignalTemplate(template);
                RenderSignalTemplates();
            });
            return row;
        }

        private void RenderSignalTemplateDetails()
        {
            var template = signalTemplateChosen;
            var match = SignalTemplateMatch();
            signalTemplateLights = match.MovementLights;
            var description = SignalTemplateDescription(template);
            if (!string.IsNullOrWhiteSpace(description))
            {
                var text = new Label(description);
                text.AddToClassList("panel-info");
                signalTemplateDetails.Add(text);
            }
            var facts = new Label(Loc.T("signals.templates.facts", ("arms", template.ArmsNeeded), ("groups", template.Program.Lights.Count),
                ("cycle", Seconds(template.Program.CycleLength))));
            facts.AddToClassList("panel-info");
            signalTemplateDetails.Add(facts);
            signalTemplateTimeline.Show(SignalTemplateProgram(template), SignalGroupColour, false);
            signalTemplateDetails.Add(signalTemplateTimeline);

            var lines = new List<string>();
            var program = SignalTemplateProgram(template);
            if (match.Fits)
                lines.Add(Loc.T("signals.templates.fit.ok"));
            if (match.MissingArms.Count > 0)
                lines.Add(Loc.T("signals.templates.fit.arms", ("needed", template.ArmsNeeded), ("have", signalCrossing.Approaches.Count)));
            if (match.EmptyLights.Count > 0)
                lines.Add(Loc.T("signals.templates.fit.empty", ("names", string.Join(", ", match.EmptyLights.Select(light => program.Lights[light].Name)))));
            if (match.Contested.Count > 0)
                lines.Add(Loc.T("signals.templates.fit.contested", ("count", match.Contested.Count)));
            if (match.Unlit.Count > 0)
                lines.Add(Loc.T("signals.templates.fit.unlit", ("count", match.Unlit.Count)));
            if (signalCrossing.Movements.Any(movement => movement.Turn == OmsiMovementTurn.Pedestrian && match.MovementLights[movement.Index] < 0))
                lines.Add(Loc.T("signals.templates.fit.footpaths"));
            var report = new Label(string.Join("\n", lines));
            report.AddToClassList("panel-info");
            report.AddToClassList(match.Fits ? "signal-ok" : "signal-issues");
            signalTemplateDetails.Add(report);

            if (match.Unlit.Count > 0)
            {
                var heading = LocBindings.NewLabel("signals.templates.correct");
                heading.AddToClassList("inspector-section-heading");
                signalTemplateDetails.Add(heading);
                foreach (var index in match.Unlit.Take(MostTemplateRows))
                    signalTemplateDetails.Add(SignalTemplateCorrection(signalCrossing.Movements[index], program));
                if (match.Unlit.Count > MostTemplateRows)
                {
                    var more = new Label(Loc.T("signals.templates.more", ("count", match.Unlit.Count - MostTemplateRows)));
                    more.AddToClassList("panel-info");
                    signalTemplateDetails.Add(more);
                }
            }
        }

        private VisualElement SignalTemplateCorrection(OmsiSignalMovement movement, OmsiSignalProgram program)
        {
            var row = new VisualElement();
            row.AddToClassList("signal-movement-row");
            var text = new Label(Loc.T("signals.templates.correct_row", ("arm", movement.Approach + 1),
                ("text", MovementText(new List<OmsiSignalMovement> { movement }, null))));
            text.AddToClassList("signal-movement-text");
            row.Add(text);
            var choices = new List<string> { Loc.T("signals.group.none") };
            choices.AddRange(program.Lights.Select(light => light.Name));
            var chosen = signalTemplateOverrides.TryGetValue(movement.Index, out var light) ? light + 1 : 0;
            var dropdown = new DropdownField(choices, chosen);
            dropdown.AddToClassList("signal-movement-group");
            dropdown.SetEnabled(SignalsEditable && WayOf(movement) != null);
            dropdown.RegisterValueChangedCallback(_ =>
            {
                signalTemplateOverrides[movement.Index] = dropdown.index - 1;
                signalTemplateConfirm = false;
                RenderSignalTemplates();
            });
            row.Add(dropdown);
            return row;
        }

        private void UpdateSignalTemplateButtons()
        {
            if (signalTemplateApplyButton == null)
                return;
            var match = signalCrossing != null && signalTemplateChosen != null ? SignalTemplateMatch() : null;
            var usable = SignalsEditable && match != null && match.MovementLights.Any(light => light >= 0);
            signalTemplateApplyButton.SetEnabled(usable);
            var existing = signalProgram?.Lights.Count ?? 0;
            // The button is an icon: the second click that replaces groups (or applies a template that leaves movements without a signal) shows red
            // and says so in the tooltip and in the status bar.
            string key;
            if (signalTemplateConfirm)
                key = existing > 0 ? "signals.templates.confirm.replace" : "signals.templates.confirm.unfitting";
            else
                key = "signals.templates.apply.tooltip";
            signalTemplateApplyButton.tooltip = LocBindings.CardTooltip(Loc.T("signals.templates.apply"), Loc.T(key, ("count", existing), ("unlit", match?.Unlit.Count ?? 0))); // i18n:keys
            signalTemplateApplyButton.EnableInClassList("option-icon-confirm", signalTemplateConfirm);
            signalTemplateApplyButton.EnableInClassList("option-icon-primary", !signalTemplateConfirm);
            signalConfirmText = signalTemplateConfirm ? Loc.T(key, ("count", existing), ("unlit", match?.Unlit.Count ?? 0)) : string.Empty; // i18n:keys
            var name = signalTemplateName.value.Trim();
            var hasProgram = signalCrossing != null && signalProgram != null && signalProgram.Lights.Count > 0;
            signalTemplateSaveButton.SetEnabled(hasProgram && OmsiSignalTemplates.FileNameOf(name) != null);
            // Saving over a template of the same name asks once more: the icon turns red and the tooltip says what a second click does.
            signalTemplateSaveButton.EnableInClassList("option-icon-confirm", signalTemplateOverwriting != null);
            signalTemplateSaveButton.tooltip = LocBindings.CardTooltip(Loc.T("signals.templates.save"), // i18n:keys
                Loc.T(signalTemplateOverwriting != null ? "signals.templates.save.overwrite" : "signals.templates.save.tooltip")); // i18n:keys
            if (signalConfirmText.Length == 0 && signalTemplateOverwriting != null)
                signalConfirmText = Loc.T("signals.templates.save.overwrite");
            if (signalConfirmText.Length == 0 && signalTemplateDeleting != null)
                signalConfirmText = Loc.T("signals.templates.delete.confirm");
            RefreshSignalHint();
        }

        /// <summary>Writes the template's groups, program and bindings into the junction as one undo step.</summary>
        private void ApplySignalTemplate(bool confirmed)
        {
            if (!SignalsEditable || signalTemplateChosen == null || signalCrossing == null)
                return;
            if (!SignalGate(() => ApplySignalTemplate(confirmed)))
                return;
            var match = SignalTemplateMatch();
            if (!match.MovementLights.Any(light => light >= 0))
                return;
            var existing = signalProgram.Lights.Count;
            if (!confirmed && !signalTemplateConfirm && (existing > 0 || !match.Fits))
            {
                // Replacing groups that exist, or movements that stay without a light, asks once more, on the same button.
                signalTemplateConfirm = true;
                RenderSignalTemplates();
                return;
            }
            signalTemplateConfirm = false;
            var signals = new OmsiJunctionSignals();
            var templateProgram = SignalTemplateProgram(signalTemplateChosen);
            // Without footpaths to bind, the pedestrians' stop point goes to the traffic's light, where vehicles ask.
            match.AdaptHolds(templateProgram);
            signals.SetProgram(templateProgram);
            var unbound = 0;
            foreach (var movement in signalCrossing.Movements)
            {
                var light = match.MovementLights[movement.Index];
                if (light < 0)
                    continue;
                var way = WayOf(movement);
                if (way == null)
                    unbound++;
                else
                    signals.WayLights[way] = light;
            }
            if (unbound > 0)
                Debug.LogWarning("Signals: the template could not bind " + unbound + " movements without a way");
            var heads = SignalHeadsOfCrossing();
            var needHeads = Enumerable.Range(0, signals.Program.Lights.Count).Where(light => heads.All(head => (head.LightIndex ?? -1) != light)).ToList();
            Func<List<object>> place = null;
            if (signalTemplateHeadsToggle.value && needHeads.Count > 0)
            {
                // The head types, height and mast are those of the assistant's fields.
                FillSignalHeadTypes(signalWizardHeadType, false);
                FillSignalHeadTypes(signalWizardLeftHeadType, true);
                if (signalHeadTypes.Count == 0)
                    SetStatusT("signals.heads.status.no_types");
                else
                {
                    var type = signalHeadTypes[Math.Max(0, signalWizardHeadType.index)];
                    var leftType = signalHeadTypes[Math.Max(0, signalWizardLeftHeadType.index)];
                    var height = TrySignalSeconds(signalWizardHeadHeight.value, out var metres) ? metres : DefaultHeadHeight;
                    FillSignalMastTypes(false);
                    var mast = ChosenSignalMast();
                    var crossing = signalCrossing;
                    var lights = match.MovementLights;
                    var program = signals.Program;
                    var lefts = new HashSet<int>(Enumerable.Range(0, program.Lights.Count).Where(light =>
                        signalTemplateChosen.Roles[light].Count > 0 && signalTemplateChosen.Roles[light].All(role => role.Lane == OmsiSignalRoleLane.OwnLane)));
                    signalHeadFailures = 0;
                    place = () => PlaceSignalHeads(crossing, lights, lefts, needHeads, type, leftType, height, mast);
                }
            }
            var missingHeads = place == null ? needHeads.Count : 0;
            var done = Loc.T("signals.templates.done", ("name", SignalTemplateName(signalTemplateChosen)), ("id", signalJunction.Id), ("groups", signals.Program.Lights.Count));
            if (missingHeads > 0)
                done += " " + Loc.T("signals.templates.heads_hint", ("count", missingHeads));
            ApplySignals(signals, place, done);
            if (place != null && signalHeadFailures > 0)
                SetStatusT("signals.heads.status.place_failed", ("count", signalHeadFailures));
            ShowSignalTab(SignalTab.Phases);
        }

        private void SaveSignalTemplate()
        {
            var name = signalTemplateName.value.Trim();
            if (signalCrossing == null || signalProgram == null || signalProgram.Lights.Count == 0)
            {
                SetStatusT("signals.templates.status.nothing");
                return;
            }
            if (OmsiSignalTemplates.FileNameOf(name) == null)
            {
                SetStatusT("signals.templates.status.no_name");
                return;
            }
            if (signalDirty)
            {
                SetStatusT("signals.templates.status.dirty");
                return;
            }
            var folder = SignalTemplatesFolder();
            if (OmsiSignalTemplates.Exists(folder, name) && signalTemplateOverwriting != name)
            {
                signalTemplateOverwriting = name;
                UpdateSignalTemplateButtons();
                return;
            }
            signalTemplateOverwriting = null;
            var (template, imprecise) = OmsiSignalTemplate.FromCrossing(name, null, signalProgram, signalCrossing, SignalOwnLaneMovements());
            try
            {
                OmsiSignalTemplates.Save(folder, template);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("signals.templates.status.save_failed", ("reason", exception.Message));
                return;
            }
            LoadSignalUserTemplates();
            ChooseSignalTemplate(signalUserTemplates.Find(candidate => candidate.Name == name) ?? template);
            RenderSignalTemplates();
            SetStatus(Loc.T("signals.templates.status.saved", ("name", name), ("folder", folder)) +
                      (imprecise > 0 ? " " + Loc.T("signals.templates.status.imprecise", ("count", imprecise)) : string.Empty));
        }

        private void DeleteSignalTemplate(string name)
        {
            if (signalTemplateDeleting != name)
            {
                signalTemplateDeleting = name;
                RenderSignalTemplates();
                return;
            }
            signalTemplateDeleting = null;
            try
            {
                OmsiSignalTemplates.Delete(SignalTemplatesFolder(), name);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("signals.templates.status.save_failed", ("reason", exception.Message));
                return;
            }
            if (signalTemplateChosen != null && !signalTemplateChosen.BuiltIn && signalTemplateChosen.Name == name)
                ChooseSignalTemplate(OmsiSignalTemplates.BuiltIn[0]);
            LoadSignalUserTemplates();
            RenderSignalTemplates();
            SetStatusT("signals.templates.status.deleted", ("name", name));
        }

        /// <summary>
        /// Command line: <c>-signalTemplate id|name</c> opens the templates and chooses one (the match goes to the log as "Signal
        /// template:"), <c>-signalTemplateOverride m g</c> gives movement m (from 1) group g (from 1, 0 for none),
        /// <c>-signalTemplateUse</c> applies it, <c>-signalTemplateSave name</c> keeps the crossing's program as a template,
        /// <c>-signalTemplateDelete name</c> removes one of the user's.
        /// </summary>
        private void RunSignalTemplateArguments(string[] args)
        {
            var open = Array.IndexOf(args, "-signalTemplate");
            if (open >= 0 && open + 1 < args.Length)
            {
                LoadSignalUserTemplates();
                var wanted = args[open + 1];
                var chosen = OmsiSignalTemplates.Find(wanted) ?? signalUserTemplates.Find(template => string.Equals(template.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (chosen == null)
                {
                    Debug.Log("Signal template: no template " + wanted);
                    return;
                }
                ChooseSignalTemplate(chosen);
                OpenSignalTemplates();
                for (var index = 0; index + 2 < args.Length; index++)
                    if (args[index] == "-signalTemplateOverride" && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var movement) &&
                        int.TryParse(args[index + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) && movement >= 1 && movement <= signalCrossing.Movements.Count)
                        signalTemplateOverrides[movement - 1] = group - 1;
                RenderSignalTemplates();
                var match = SignalTemplateMatch();
                Debug.Log("Signal template: " + (chosen.Id ?? chosen.Name) + " groups " + chosen.Program.Lights.Count + " arms needed " + chosen.ArmsNeeded + ", crossing arms " +
                          signalCrossing.Approaches.Count + ", fits " + match.Fits + ", missing arms " + match.MissingArms.Count + ", unlit " + match.Unlit.Count + ", empty groups " +
                          match.EmptyLights.Count + ", contested " + match.Contested.Count);
                if (Array.IndexOf(args, "-signalTemplateUse") >= 0)
                    ApplySignalTemplate(true);
            }
            var save = Array.IndexOf(args, "-signalTemplateSave");
            if (save >= 0 && save + 1 < args.Length && signalCrossing != null)
            {
                signalTemplateName.value = args[save + 1];
                signalTemplateOverwriting = args[save + 1];
                SaveSignalTemplate();
            }
            var delete = Array.IndexOf(args, "-signalTemplateDelete");
            if (delete >= 0 && delete + 1 < args.Length)
            {
                signalTemplateDeleting = args[delete + 1];
                DeleteSignalTemplate(args[delete + 1]);
            }
        }
    }
}
