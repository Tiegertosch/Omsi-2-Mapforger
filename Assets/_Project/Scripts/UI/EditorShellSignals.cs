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
    /// The traffic signals tool (ADR 085, milestone 2): a click on a crossing shows its approaches, lanes and movements, each
    /// movement in the colour of the signal group (OMSI light) it obeys. On a junction MapWorks made, signal groups can be
    /// added, renamed and taken away and each movement given a group, written into the junction's .sco as one undo step;
    /// OMSI's own crossings are shown as they are (the user's decision: their programs belong to OMSI's object files).
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>A movement counts as under the pointer within this many pixels.</summary>
        private const float SignalPickPixels = 10f;

        private VisualElement signalOptions;
        private SignalWindow signalWindow;
        private VisualElement signalGroupsBox;
        private VisualElement signalMovementsBox;
        private Label signalEmptyNote;
        private string signalPlanText = string.Empty;
        private OmsiTileSummary signalTile;
        private OmsiObjectRecord signalJunction;
        private OmsiGeneratedJunction signalMade;
        private OmsiSignalProgram signalProgram;
        private OmsiSignalIntersection signalCrossing;
        private OmsiJunctionLayout signalLayout;
        private readonly List<OmsiMapPoint[]> signalLines = new List<OmsiMapPoint[]>();
        private int signalSelected = -1;

        /// <summary>The group chosen in the list of groups (its movements are marked in the list and on the map), or -1.</summary>
        private int signalSelectedGroup = -1;

        /// <summary>The group whose delete button was clicked once and asks to be clicked again, or -1.</summary>
        private int signalDeleteArmed = -1;

        private readonly List<VisualElement> signalGroupRows = new List<VisualElement>();
        private readonly List<(VisualElement Row, int ControlPath, int Light)> signalMovementRows = new List<(VisualElement, int, int)>();

        private bool SignalsEditable => signalMade != null && CanEdit && signalLayout != null;

        /// <summary>
        /// The window of the signals tool (ADR 085, ADR 098): one window that floats beside the map like the one for pictures from the air.
        /// Its areas are the signal groups, the phases and times, the check and the assistant; the preview and the footer stay in view.
        /// </summary>
        private void CreateSignalOptions()
        {
            signalWindow = new SignalWindow(() => SetTool(EditorTool.Drag), ChooseSignalArea, ChooseSignalPage);
            signalOptions = signalWindow;
            signalOptions.style.display = DisplayStyle.None;
            signalEmptyNote = LocBindings.NewLabel("signals.click_junction");
            signalEmptyNote.AddToClassList("panel-info");
            signalEmptyNote.AddToClassList("signal-empty");
            signalWindow.Body.Add(signalEmptyNote);
            signalWindow.AddPage(CreateSignalMovementsPage());
            signalWindow.AddPage(CreateSignalPhasesPage());
            signalWindow.AddPage(CreateSignalTimelinePage());
            signalWindow.AddPage(CreateSignalCheckPage());
            signalWindow.AddPage(CreateSignalWizardPage());
            signalWindow.AddPage(CreateSignalTemplatesPage());
            CreateSignalPreview(signalWindow.PreviewHost);
            CreateSignalFooter();
            ShowSignalTab(SignalTab.Movements);
            // The window floats over the map: it is a child of the workspace, not of the row of the tool windows above the bottom bar.
            (objectsPanelHost.parent ?? objectsPanelHost).Add(signalOptions);
        }

        /// <summary>
        /// The groups page: the signal groups in a compact list on the left (colour, name, number of movements, delete; a button to add one), on
        /// the right the movements by approach and lane with the group each obeys, and below them the heads that show the groups.
        /// </summary>
        private VisualElement CreateSignalMovementsPage()
        {
            var page = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            page.contentContainer.AddToClassList("signal-columns");
            signalMovementsPage = page;
            var groups = new VisualElement();
            groups.AddToClassList("signal-column");
            groups.AddToClassList("signal-column-groups");
            var groupsHead = new VisualElement();
            groupsHead.AddToClassList("signal-column-head");
            var groupsTitle = LocBindings.NewLabel("signals.groups"); // i18n:keys
            groupsTitle.AddToClassList("inspector-section-heading");
            groupsTitle.AddToClassList("signal-column-title");
            groupsHead.Add(groupsTitle);
            signalAddGroupButton = SignalLabelButton("plus", "signals.group.add", "signals.group.add.tooltip", AddSignalGroup);
            groupsHead.Add(signalAddGroupButton);
            groups.Add(groupsHead);
            signalGroupsBox = new VisualElement();
            groups.Add(signalGroupsBox);
            page.Add(groups);

            var right = new VisualElement();
            right.AddToClassList("signal-column");
            right.AddToClassList("signal-column-movements");
            var movementsTitle = LocBindings.NewLabel("signals.movements"); // i18n:keys
            movementsTitle.AddToClassList("inspector-section-heading");
            right.Add(movementsTitle);
            signalMovementsBox = new VisualElement();
            right.Add(signalMovementsBox);
            var headsTitle = LocBindings.NewLabel("signals.heads"); // i18n:keys
            headsTitle.AddToClassList("inspector-section-heading");
            right.Add(headsTitle);
            signalHeadsBox = new VisualElement();
            right.Add(signalHeadsBox);
            page.Add(right);
            return page;
        }

        private void BeginSignalTool()
        {
            if (signalOptions == null || mapCanvas == null || currentSummary == null)
                return;
            signalPlanText = string.Empty;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (terrainOptions != null)
                terrainOptions.style.display = DisplayStyle.None;
            signalOptions.style.display = DisplayStyle.Flex;
            mapCanvas.ShowSplineEnds = false;
            mapCanvas.BeginRoad();
            ClearSignalTarget();
            var item = selection.Count == 1 ? selection[0] : null;
            if (item?.Tile == null || !TargetSignalJunction(item.Tile, item.Record))
                SetStatusT("signals.status.start");
            RefreshSignalHint();
        }

        private void EndSignalTool()
        {
            mapCanvas?.EndRoad();
            if (mapCanvas != null)
            {
                mapCanvas.ShowSplineEnds = true;
                mapCanvas.ClearRoadPreview();
            }
            if (signalOptions != null)
                signalOptions.style.display = DisplayStyle.None;
            ClearSignalTarget();
        }

        private void ClearSignalTarget()
        {
            StopSignalPreview();
            signalHeadPlaces.Clear();
            signalFindings.Clear();
            mapCanvas?.SetSignalMarkers(null);
            if (signalDirty)
                SetStatusT("signals.status.discarded");
            signalDirty = false;
            signalDraft = null;
            signalTile = null;
            signalJunction = null;
            signalMade = null;
            signalProgram = null;
            signalCrossing = null;
            signalLayout = null;
            signalLines.Clear();
            signalSelected = -1;
            signalSelectedGroup = -1;
            signalDeleteArmed = -1;
            signalGuard.Clear();
            signalWindow?.ShowPrompt(null, false);
            signalConfirmText = string.Empty;
            signalPlanText = string.Empty;
            signalIdentity.Reset(0);
            signalGroupsBox?.Clear();
            signalMovementsBox?.Clear();
            signalHeadsBox?.Clear();
            signalGroupRows.Clear();
            signalMovementRows.Clear();
            ShowSignalEmptyNote();
            RenderSignalPreviewTracks();
            RefreshSignalHint();
            UpdateSignalRows();
            mapCanvas?.SetSignalMovements(null, null, null, double.NaN);
        }

        /// <summary>The program of an object file: of the junction made here if it is one, else read from its .sco.</summary>
        private OmsiSignalProgram ReadSignalProgram(string assetPath, OmsiSceneryObject sceneryObject)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            try
            {
                var path = OmsiRelativePath.Resolve(root, assetPath);
                if (File.Exists(path))
                    return OmsiSignalProgram.Read(File.ReadAllBytes(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
            }
            var empty = new OmsiSignalProgram();
            for (var index = 0; index < sceneryObject.Paths.Count; index++)
                empty.PathLights.Add(-1);
            return empty;
        }

        /// <summary>Works on a crossing: reads its program and movements; false when it has fewer than two approaches.</summary>
        private bool TargetSignalJunction(OmsiTileSummary tile, OmsiObjectRecord record, bool keepDraft = false)
        {
            var sceneryObject = PrioritySceneryObject(record.AssetPath);
            if (sceneryObject == null || OmsiJunctionPriorities.Arms(sceneryObject, null).Count < 2)
                return false;
            var program = ReadSignalProgram(record.AssetPath, sceneryObject);
            // (a program that does not fit the object's paths is shown without its bindings)
            while (program.PathLights.Count < sceneryObject.Paths.Count)
                program.PathLights.Add(-1);
            var crossing = OmsiSignalIntersection.Analyse(sceneryObject, program);
            // Another crossing drops a draft with changes and says so; the same one read anew after Apply keeps quiet.
            var sameCrossing = signalJunction != null && signalJunction.Id == record.Id;
            signalTile = tile;
            signalJunction = record;
            signalProgram = program;
            signalCrossing = crossing;
            signalMade = MadeJunction(record.AssetPath);
            signalLayout = signalMade != null ? OmsiJunctionLayout.Build(signalMade.Arms, PlainRoadTexture(), signalMade.Connections) : null;
            // The layout made anew must be the one the file holds, or its ways cannot be told apart.
            if (signalLayout != null && signalLayout.Paths.Count != sceneryObject.Paths.Count)
                signalLayout = null;
            if (!sameCrossing)
            {
                StopSignalPreview();
                signalSelectedGroup = -1;
                signalDeleteArmed = -1;
                signalIdentity.Reset(program.Lights.Count);
            }
            else
                signalIdentity.Sync(program.Lights.Count);
            // A draft with changes that the caller wants kept (heads changed, an apply that failed) survives the crossing being read anew.
            var keptDraft = sameCrossing && keepDraft && signalDirty ? signalDraft : null;
            ResetSignalDraft(!sameCrossing);
            if (keptDraft != null && keptDraft.Lights.Count == program.Lights.Count && SignalsEditable)
            {
                signalDraft = keptDraft;
                signalDirty = true;
            }
            if (signalSelected >= crossing.Movements.Count)
                signalSelected = -1;
            signalLines.Clear();
            foreach (var movement in crossing.Movements)
                signalLines.Add(MovementLine(tile, record, sceneryObject, movement));
            if (selection.Count != 1 || !ReferenceEquals(selection[0].Record, record))
            {
                selection.Clear();
                selection.Add(new MapSelectionItem { Tile = tile, Record = record });
                ApplySelection(tile);
            }
            FindSignalHeadPlaces();
            RenderSignalPanel();
            if (signalTab == SignalTab.Wizard && !SignalsEditable)
                ShowSignalTab(SignalTab.Movements);
            else if (signalTab == SignalTab.Wizard)
                RenderSignalWizard();
            else if (signalTab == SignalTab.Templates)
                RenderSignalTemplates();
            RenderSignalDraft();
            UpdateSignalPreview();
            RefreshSignalHint();
            // Another crossing (not the same one read anew after Apply) is kept in sight beside the window, which stands over the map.
            if (!sameCrossing)
                KeepClearOfFloatingWindow(signalWindow, () => activeTool == EditorTool.Signals, () => ObjectWorldPoint(tile, record));
            SetStatusT("signals.status.target", ("id", record.Id), ("approaches", crossing.Approaches.Count), ("movements", crossing.Movements.Count),
                ("groups", program.Lights.Count));
            return true;
        }

        /// <summary>The way of a movement through the crossing in world metres, sampled along its paths.</summary>
        private OmsiMapPoint[] MovementLine(OmsiTileSummary tile, OmsiObjectRecord record, OmsiSceneryObject sceneryObject, OmsiSignalMovement movement)
        {
            var points = new List<OmsiMapPoint>();
            foreach (var index in movement.Paths)
            {
                var path = sceneryObject.Paths[index];
                var steps = Math.Max(1, (int)Math.Ceiling(path.LengthMeters / 1.5));
                for (var step = 0; step <= steps; step++)
                {
                    var along = path.LengthMeters * step / steps;
                    if (path.Direction == 1)
                        along = path.LengthMeters - along;
                    var local = path.PointAt(along);
                    points.Add(ObjectToWorld(tile, record, local.X, local.Y));
                }
            }
            return points.ToArray();
        }

        /// <summary>
        /// Reads the crossing anew after files changed. Undo and redo put other files back, so a draft with changes is dropped, which the
        /// status says (undo asks the draft first, see <see cref="UndoSignalDraft"/>). Changes of the heads and an apply that failed leave
        /// the program as it was: the draft stays.
        /// </summary>
        private void RefreshSignalTarget(bool keepDraft = false)
        {
            if (signalJunction == null || signalTile == null)
                return;
            keepDraft |= signalApplying;
            var id = signalJunction.Id;
            if (!keepDraft)
                StopSignalPreview();
            if (signalDirty && !keepDraft)
            {
                signalDirty = false;
                SetStatusT("signals.status.discarded");
            }
            var current = signalTile.Objects.Find(candidate => candidate.Id == id);
            if (current == null || !TargetSignalJunction(signalTile, current, keepDraft))
                ClearSignalTarget();
        }

        /// <summary>True while ApplySignals runs: a refresh of the crossing it causes must not drop the draft of a failed apply.</summary>
        private bool signalApplying;

        /// <summary>
        /// Undo while the signals tool has a draft with changes takes back the draft first (the newest change), as the history holds only what
        /// was applied; the next undo goes on in the history. Returns whether it took the key.
        /// </summary>
        private bool UndoSignalDraft()
        {
            if (activeTool != EditorTool.Signals || !SignalsEditable || !signalDirty)
                return false;
            DiscardSignalDraft();
            return true;
        }

        private void UpdateSignalPreview()
        {
            if (mapCanvas == null || activeTool != EditorTool.Signals)
                return;
            if (signalCrossing == null)
            {
                mapCanvas.SetSignalMovements(null, null, null, double.NaN);
                return;
            }
            var colours = new List<Color?>();
            var selected = new List<bool>();
            // While the assistant is open, the map shows the groups it proposes; in the preview, what each movement is shown.
            var proposed = signalTab == SignalTab.Wizard && signalProposal != null ? signalProposal.MovementLights
                : signalTab == SignalTab.Templates ? signalTemplateLights : null;
            foreach (var movement in signalCrossing.Movements)
            {
                var light = proposed != null ? proposed[movement.Index] : movement.Light;
                if (SignalPreviewActive && proposed == null)
                    colours.Add(light >= 0 ? SignalStateColour(signalClockProgram.StateAt(light, signalClock.Time)) : (Color?)null);
                else
                    colours.Add(light >= 0 ? (proposed != null ? SignalGroupColour(light) : SignalColourOf(light)) : (Color?)null);
                selected.Add((signalSelected >= 0 && signalCrossing.Movements[signalSelected].ControlPath == movement.ControlPath) ||
                             (signalSelectedGroup >= 0 && movement.Light == signalSelectedGroup && proposed == null));
            }
            mapCanvas.SetSignalMovements(signalLines, colours, selected, signalJunction.Height);
        }

        /// <summary>The page: the signal groups with their colours, then the movements by approach and lane with the group each obeys, then the heads.</summary>
        private void RenderSignalPanel()
        {
            var scroll = signalMovementsPage as ScrollView;
            var offset = scroll != null ? scroll.scrollOffset : default;
            var focusKey = SignalFocus.Capture(signalMovementsPage);
            signalGroupsBox.Clear();
            signalMovementsBox.Clear();
            signalGroupRows.Clear();
            signalMovementRows.Clear();
            ShowSignalEmptyNote();
            if (signalCrossing == null)
            {
                RefreshSignalHint();
                UpdateSignalRows();
                return;
            }
            var editable = SignalsEditable;
            for (var light = 0; light < signalProgram.Lights.Count; light++)
                signalGroupsBox.Add(SignalGroupRow(light, editable));
            if (signalProgram.Lights.Count == 0)
            {
                // Without groups the assistant is offered first: its button in the navigation says so in its tooltip.
                var none = LocBindings.NewLabel(editable ? "signals.wizard.offer" : "signals.groups.none"); // i18n:keys
                none.AddToClassList("panel-info");
                signalGroupsBox.Add(none);
            }

            var shown = new HashSet<int>();
            var unassigned = signalCrossing.Movements.Where(movement => movement.Light < 0).Select(movement => movement.ControlPath).Distinct().Count();
            // A crossing that is only read shows its groups as OMSI made them; "no group yet" would be no call to do anything.
            if (unassigned > 0 && editable)
            {
                var warn = new Label(Loc.T("signals.movements.unassigned", ("count", unassigned)));
                warn.AddToClassList("panel-info");
                warn.AddToClassList("panel-info-warning");
                signalMovementsBox.Add(warn);
            }
            foreach (var approach in signalCrossing.Approaches)
            {
                var heading = new Label(Loc.T("signals.approach", ("n", approach.Index + 1), ("lanes", approach.Lanes.Count)));
                heading.AddToClassList("signal-approach");
                signalMovementsBox.Add(heading);
                foreach (var lane in approach.Lanes)
                    foreach (var index in lane.Movements)
                        AddMovementRow(signalCrossing.Movements[index], lane, editable, shown);
            }
            var others = signalCrossing.Movements.Where(movement => movement.Approach < 0).ToList();
            if (others.Count > 0)
            {
                var heading = new Label(Loc.T("signals.others", ("count", others.Count)));
                heading.AddToClassList("signal-approach");
                signalMovementsBox.Add(heading);
                foreach (var movement in others)
                    AddMovementRow(movement, null, editable, shown);
            }
            UpdateSignalPlan();
            RenderSignalHeads();
            UpdateSignalSelection();
            UpdateSignalRows();
            if (scroll != null)
                scroll.schedule.Execute(() => scroll.scrollOffset = offset);
            // The rebuild must not take the cursor from the field being typed in (Tab to the next name).
            SignalFocus.Restore(signalMovementsPage, focusKey);
        }

        private VisualElement SignalGroupRow(int light, bool editable)
        {
            var row = new VisualElement();
            row.AddToClassList("signal-group-row");
            row.AddToClassList("signal-group-item");
            var chip = new VisualElement();
            chip.AddToClassList("signal-group-chip");
            chip.style.backgroundColor = SignalColourOf(light);
            row.Add(chip);
            var name = new TextField { value = signalProgram.Lights[light].Name, isDelayed = true };
            name.userData = "focus:group:" + signalIdentity.IdOf(light).ToString(CultureInfo.InvariantCulture);
            name.AddToClassList("picker-field");
            name.AddToClassList("signal-group-name");
            name.SetEnabled(editable);
            name.tooltip = Loc.T("signals.group.name.tooltip", ("n", light));
            name.RegisterValueChangedCallback(evt => RenameSignalGroup(light, evt.newValue));
            row.Add(name);
            var movements = signalCrossing.Movements.Where(movement => movement.Light == light).Select(movement => movement.ControlPath).Distinct().Count();
            var count = new Label(Loc.T("signals.group.count", ("count", movements)));
            count.AddToClassList("signal-group-count");
            row.Add(count);
            if (editable)
            {
                var remove = new Button(() => RemoveSignalGroup(light)) { tooltip = Loc.T("signals.group.remove.tooltip") };
                remove.AddToClassList("panel-icon-button");
                remove.EnableInClassList("signal-group-remove-armed", signalDeleteArmed == light);
                remove.Add(new IconElement("trash-2"));
                row.Add(remove);
            }
            // A click on the row (not in the name field) chooses the group; its movements light up in the list and on the map.
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is TextElement || evt.target is Button)
                    return;
                SelectSignalGroup(light);
            });
            signalGroupRows.Add(row);
            return row;
        }

        private void AddMovementRow(OmsiSignalMovement movement, OmsiSignalLane lane, bool editable, HashSet<int> shown)
        {
            // Movements that stop at the same path switch together: one row for them, as one lane with several turns.
            if (!shown.Add(movement.ControlPath))
                return;
            var together = signalCrossing.SharingControl(movement);
            var row = new VisualElement();
            row.AddToClassList("signal-movement-row");
            row.EnableInClassList("signal-movement-unassigned", movement.Light < 0);
            var chip = new VisualElement();
            chip.AddToClassList("signal-group-chip");
            chip.style.backgroundColor = movement.Light >= 0 ? SignalColourOf(movement.Light) : SignalDark;
            row.Add(chip);
            var text = new Label(MovementText(together, lane));
            text.AddToClassList("signal-movement-text");
            row.Add(text);
            var choices = new List<string> { Loc.T("signals.group.none") };
            choices.AddRange(signalProgram.Lights.Select((light, index) => string.IsNullOrWhiteSpace(light.Name) ? Loc.T("signals.group.unnamed", ("n", index)) : light.Name));
            var dropdown = new DropdownField(choices, movement.Light + 1);
            dropdown.AddToClassList("signal-movement-group");
            dropdown.SetEnabled(editable && WayOf(movement) != null);
            dropdown.RegisterValueChangedCallback(_ => AssignSignalGroup(movement.Index, dropdown.index - 1));
            row.Add(dropdown);
            row.RegisterCallback<ClickEvent>(_ => SelectSignalMovement(movement.Index));
            signalMovementsBox.Add(row);
            signalMovementRows.Add((row, movement.ControlPath, movement.Light));
            if (together.Count > 1)
            {
                var hint = LocBindings.NewLabel("signals.shared_lane");
                hint.AddToClassList("panel-info");
                hint.AddToClassList("signal-shared-hint");
                signalMovementsBox.Add(hint);
            }
            if (movement.HasSeveralLights)
            {
                var hint = LocBindings.NewLabel("signals.several_lights");
                hint.AddToClassList("panel-info");
                hint.AddToClassList("signal-shared-hint");
                signalMovementsBox.Add(hint);
            }
        }

        private string MovementText(List<OmsiSignalMovement> movements, OmsiSignalLane lane)
        {
            var turns = string.Join(", ", movements.Select(movement => movement.Turn == OmsiMovementTurn.Pedestrian ? Loc.T("signals.turn.pedestrian")
                : Loc.T("signals.turn", ("turn", TurnName(movement.Turn)), ("hasExit", movement.ExitApproach >= 0 ? "yes" : "no"), ("exit", movement.ExitApproach + 1))));
            return lane == null ? turns : Loc.T("signals.lane", ("n", lane.Index + 1), ("turns", turns));
        }

        private static string TurnName(OmsiMovementTurn turn)
        {
            switch (turn)
            {
                case OmsiMovementTurn.Left: return Loc.T("signals.turn.left");
                case OmsiMovementTurn.Right: return Loc.T("signals.turn.right");
                case OmsiMovementTurn.UTurn: return Loc.T("signals.turn.uturn");
                default: return Loc.T("signals.turn.straight");
            }
        }

        private void SelectSignalMovement(int index)
        {
            signalSelected = index;
            signalSelectedGroup = -1;
            DisarmSignalDelete();
            UpdateSignalSelection();
            UpdateSignalPlan();
            UpdateSignalPreview();
        }

        /// <summary>Chooses a group in the list: its movements are marked in the list and on the map. Nothing is rebuilt, so a field keeps its focus.</summary>
        private void SelectSignalGroup(int light)
        {
            signalSelectedGroup = signalSelectedGroup == light ? -1 : light;
            signalSelected = -1;
            DisarmSignalDelete();
            UpdateSignalSelection();
            UpdateSignalPlan();
            UpdateSignalPreview();
        }

        /// <summary>Takes back the question "delete this group?" when the person does something else.</summary>
        private void DisarmSignalDelete()
        {
            if (signalDeleteArmed < 0)
                return;
            signalDeleteArmed = -1;
            signalConfirmText = string.Empty;
            foreach (var row in signalGroupRows)
                row.Q<Button>()?.RemoveFromClassList("signal-group-remove-armed");
        }

        /// <summary>Marks the chosen group and the chosen movement in the lists in place.</summary>
        private void UpdateSignalSelection()
        {
            for (var light = 0; light < signalGroupRows.Count; light++)
                signalGroupRows[light].EnableInClassList("signal-group-selected", light == signalSelectedGroup);
            var selectedPath = signalSelected >= 0 && signalCrossing != null && signalSelected < signalCrossing.Movements.Count ? signalCrossing.Movements[signalSelected].ControlPath : -1;
            foreach (var entry in signalMovementRows)
            {
                entry.Row.EnableInClassList("signal-movement-selected", entry.ControlPath == selectedPath);
                entry.Row.EnableInClassList("signal-movement-ingroup", signalSelectedGroup >= 0 && entry.Light == signalSelectedGroup);
            }
        }

        /// <summary>Without a crossing the window shows the note to click one; the pages show with it.</summary>
        private void ShowSignalEmptyNote()
        {
            if (signalWindow == null)
                return;
            signalEmptyNote.style.display = signalCrossing == null ? DisplayStyle.Flex : DisplayStyle.None;
            ApplySignalPageVisibility();
        }

        /// <summary>Only the open page shows, and none without a crossing.</summary>
        private void ApplySignalPageVisibility()
        {
            void Show(VisualElement page, SignalTab of)
            {
                if (page != null)
                    page.style.display = signalTab == of && signalCrossing != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Show(signalMovementsPage, SignalTab.Movements);
            Show(signalPhasesPage, SignalTab.Phases);
            Show(signalTimelinePage, SignalTab.Timeline);
            Show(signalCheckPage, SignalTab.Check);
            Show(signalWizardPage, SignalTab.Wizard);
            Show(signalTemplatesPage, SignalTab.Templates);
        }

        /// <summary>What the next click does: the plan line of the movements page, shown in the status bar with the crossing's line.</summary>
        private void UpdateSignalPlan()
        {
            if (signalCrossing == null)
                signalPlanText = string.Empty;
            else if (!SignalsEditable)
                signalPlanText = Loc.T("signals.plan.readonly");
            else if (signalSelected >= 0)
                signalPlanText = Loc.T("signals.plan.selected", ("text", MovementText(signalCrossing.SharingControl(signalCrossing.Movements[signalSelected]),
                    null)));
            else
                signalPlanText = Loc.T("signals.plan.point");
            RefreshSignalHint();
        }

        /// <summary>
        /// The way through of a movement on a junction made here, by the first arc of its way; null elsewhere. A movement
        /// from a left-turn lane first follows the lane's lead-in, which belongs to no way.
        /// </summary>
        private string WayOf(OmsiSignalMovement movement)
        {
            if (signalLayout == null)
                return null;
            foreach (var index in movement.Paths)
                if (index >= 0 && index < signalLayout.Paths.Count && signalLayout.Paths[index].StartsWay)
                    return signalLayout.Paths[index].WayThrough;
            return null;
        }

        /// <summary>A click with the signals tool: selects the movement under the pointer, or works on the crossing clicked.</summary>
        private void ClickSignal(double x, double y)
        {
            if (currentSummary == null)
                return;
            var point = new OmsiMapPoint(x, y);
            roadPointer = point;
            var movement = SignalMovementAt(point);
            if (movement >= 0)
            {
                SelectSignalMovement(movement);
                return;
            }
            if (PriorityJunctionAt(point, out var tile, out var record))
            {
                if (!ReferenceEquals(record, signalJunction))
                {
                    // A draft with changes is not dropped for another crossing without asking.
                    if (signalJunction != null && !SignalMayLeave(new SignalExit { Kind = SignalExitKind.OtherCrossing, Payload = new SignalCrossingTarget { Tile = tile, ObjectId = record.Id } }))
                        return;
                    signalSelected = -1;
                    if (!TargetSignalJunction(tile, record))
                        SetStatusT("signals.status.none_here");
                }
                return;
            }
            SetStatusT("signals.status.none_here");
        }

        private int SignalMovementAt(OmsiMapPoint point)
        {
            var reach = Math.Max(0.4, SignalPickPixels * (mapCanvas?.MetersPerPixel ?? 1f));
            var best = -1;
            for (var index = 0; index < signalLines.Count; index++)
            {
                var line = signalLines[index];
                for (var segment = 0; segment + 1 < line.Length; segment++)
                {
                    var distance = SegmentDistance(point, line[segment], line[segment + 1]);
                    if (distance < reach)
                    {
                        reach = distance;
                        best = index;
                    }
                }
            }
            return best;
        }

        private static double SegmentDistance(OmsiMapPoint point, OmsiMapPoint a, OmsiMapPoint b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = dx * dx + dy * dy;
            var t = length > 1e-9 ? Math.Max(0.0, Math.Min(1.0, ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / length)) : 0.0;
            var x = a.X + t * dx - point.X;
            var y = a.Y + t * dy - point.Y;
            return Math.Sqrt(x * x + y * y);
        }

        private OmsiJunctionSignals CurrentSignals()
        {
            var signals = signalMade.Signals?.Copy() ?? new OmsiJunctionSignals();
            if (signalMade.Signals == null)
                signals.Program.CycleSeconds = 60;
            // Changes of the timeline not yet applied go along with any other change.
            if (signalDirty && signalDraft != null)
                signals.SetProgram(signalDraft);
            return signals;
        }

        private void AddSignalGroup()
        {
            if (!SignalsEditable || !SignalGate(AddSignalGroup))
                return;
            var signals = CurrentSignals();
            var index = signals.Program.AddLight(Loc.T("signals.group.default_name", ("n", signals.Program.Lights.Count + 1)));
            signalDeleteArmed = -1;
            // The new group takes the colour no group has; the groups before it keep theirs. The colours go back when the junction could not be made.
            signalIdentity.Sync(signalProgram.Lights.Count);
            var identities = signalIdentity.Snapshot();
            signalIdentity.Added();
            if (!ApplySignals(signals, null, Loc.T("signals.done.added", ("name", signals.Program.Lights[index].Name), ("id", signalJunction.Id))))
                RestoreSignalIdentities(identities, signalSelectedGroup);
        }

        private void RenameSignalGroup(int light, string name)
        {
            if (!SignalsEditable || light >= signalProgram.Lights.Count)
                return;
            if (!SignalGate(() => RenameSignalGroup(light, name)))
                return;
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.StartsWith("[", StringComparison.Ordinal) || name == signalProgram.Lights[light].Name)
            {
                RenderSignalPanel();
                return;
            }
            var signals = CurrentSignals();
            signals.Program.Lights[light].Name = name;
            ApplySignals(signals, null, Loc.T("signals.done.renamed", ("name", name), ("id", signalJunction.Id)));
        }

        /// <summary>Takes a group away; refused while heads in the map show it, which would then show another group or none.</summary>
        private void RemoveSignalGroup(int light)
        {
            if (!SignalsEditable || light >= signalProgram.Lights.Count || !SignalGate(() => RemoveSignalGroup(light)))
                return;
            var heads = OmsiSignalHead.Of(currentSummary, signalTile, signalJunction.Id, path => PrioritySceneryObject(path)?.IsTrafficLight == true);
            var showing = heads.Count(head => (head.LightIndex ?? 0) == light);
            if (showing > 0)
            {
                SetStatusT("signals.status.heads_show", ("name", signalProgram.Lights[light].Name), ("count", showing));
                return;
            }
            // A group with movements asks once more: they lose their group, which the text says.
            var obeying = signalCrossing.Movements.Where(movement => movement.Light == light).Select(movement => movement.ControlPath).Distinct().Count();
            if (obeying > 0 && signalDeleteArmed != light)
            {
                signalDeleteArmed = light;
                signalConfirmText = Loc.T("signals.group.remove.confirm", ("name", signalProgram.Lights[light].Name), ("count", obeying));
                RenderSignalPanel();
                return;
            }
            signalDeleteArmed = -1;
            signalConfirmText = string.Empty;
            var signals = CurrentSignals();
            var name = signals.Program.Lights[light].Name;
            var map = signals.Program.RemoveLight(light);
            signalIdentity.Sync(signalProgram.Lights.Count);
            var identities = signalIdentity.Snapshot();
            var selectedGroup = signalSelectedGroup;
            signalIdentity.Removed(light);
            if (signalSelectedGroup == light)
                signalSelectedGroup = -1;
            else if (signalSelectedGroup > light)
                signalSelectedGroup--;
            foreach (var key in signals.WayLights.Keys.ToList())
            {
                var moved = map[signals.WayLights[key]];
                if (moved < 0)
                    signals.WayLights.Remove(key);
                else
                    signals.WayLights[key] = moved;
            }
            signals.RemapExceptions(map);
            // Heads of the groups after it follow their group to its new number, in the same step.
            var relabel = heads.Where(head => head.LightIndex.HasValue && head.LightIndex.Value > light).ToList();
            if (!ApplySignals(signals, relabel.Count > 0 ? (Func<List<object>>)(() => RelabelHeads(relabel, map)) : null,
                    Loc.T("signals.done.removed", ("name", name), ("id", signalJunction.Id))))
                RestoreSignalIdentities(identities, selectedGroup);
        }

        /// <summary>A change of the groups was not written: the colours and the chosen group are as before it, and the lists show them again.</summary>
        private void RestoreSignalIdentities(int[] identities, int selectedGroup)
        {
            signalIdentity.Restore(identities);
            signalSelectedGroup = selectedGroup;
            if (signalCrossing != null)
                RenderSignalPanel();
            RenderSignalDraft();
        }

        private List<object> RelabelHeads(List<OmsiSignalHead> heads, int[] map)
        {
            var steps = new List<object>();
            foreach (var head in heads)
            {
                var labels = (head.Object?.Labels ?? head.Attached.Labels).ToList();
                labels[0] = map[head.LightIndex.Value].ToString(CultureInfo.InvariantCulture);
                steps.Add(session.SetElementLabels(head.Tile, (object)head.Object ?? head.Attached, labels));
            }
            return steps;
        }

        private void AssignSignalGroup(int movementIndex, int light)
        {
            // (a group that does not exist, from the command line, is refused instead of failing in the middle of the write)
            if (!SignalsEditable || signalCrossing == null || movementIndex < 0 || movementIndex >= signalCrossing.Movements.Count || light >= signalProgram.Lights.Count)
                return;
            if (!SignalGate(() => AssignSignalGroup(movementIndex, light)))
                return;
            var movement = signalCrossing.Movements[movementIndex];
            var way = WayOf(movement);
            if (way == null || movement.Light == light)
                return;
            var signals = CurrentSignals();
            // Movements that start on one lead-in (a lane before a crossing) obey one light: every way of the lane follows.
            foreach (var shared in signalCrossing.SharingControl(movement))
            {
                var sharedWay = WayOf(shared);
                if (sharedWay == null)
                    continue;
                if (light < 0)
                    signals.WayLights.Remove(sharedWay);
                else
                    signals.WayLights[sharedWay] = light;
            }
            signalSelected = movementIndex;
            ApplySignals(signals, null, Loc.T(light < 0 ? "signals.done.unassigned" : "signals.done.assigned", // i18n:keys
                ("text", MovementText(new List<OmsiSignalMovement> { movement }, null)),
                ("name", light < 0 ? string.Empty : signals.Program.Lights[light].Name), ("id", signalJunction.Id)));
        }

        /// <summary>
        /// Makes the junction again with these signals under Sceneryobjects\Mapforge and puts it in place of the old one, as one
        /// undo step together with <paramref name="more"/> (changes of heads), as the markings tool does (ADR 044).
        /// </summary>
        /// <returns>True when the junction was made and put in place; false when something refused or failed, and nothing was changed.</returns>
        private bool ApplySignals(OmsiJunctionSignals signals, Func<List<object>> more, string done)
        {
            signalApplying = true;
            try
            {
                return ApplySignalsCore(signals, more, done);
            }
            finally
            {
                signalApplying = false;
            }
        }

        private bool ApplySignalsCore(OmsiJunctionSignals signals, Func<List<object>> more, string done)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.edit_signals"));
                return false;
            }
            var record = signalJunction;
            var made = signalMade;
            var tile = signalTile;
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
            if (rebuild == null)
            {
                SetStatusT("lanes.status.mismatch", ("id", record.Id));
                return false;
            }
            var plain = PlainRoadTexture();
            var turn = rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, plain);
            if (turn.HasErrors)
            {
                SetStatusT("lanes.status.stays", ("id", record.Id), ("reason", turn.Problem ?? Loc.T("lanes.stays.default")));
                return false;
            }
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, made.Name,
                    WithSignalReport(OmsiJunctionGenerator.Junction(turn.Arms, plain, "Mapforge " + made.Name, made.Markings, made.Areas, made.Arrows,
                        made.Connections, signals)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("lanes.status.rebuild_failed", ("reason", exception.Message));
                return false;
            }
            OmsiJunctionInsertionResult result;
            List<object> extra;
            try
            {
                result = session.RebuildJunction(currentSummary, rebuild, turn, assetPath);
                extra = result.Succeeded && more != null ? more() : new List<object>();
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                RefreshAfterChange(null);
                SetStatusT("lanes.status.rebuild_failed_id", ("id", record.Id), ("reason", exception.Message));
                return false;
            }
            if (!result.Succeeded)
            {
                RefreshAfterChange(null);
                SetStatus(Loc.T("lanes.status.stays_refs", ("id", record.Id), ("references", DescribeReferences(result.Blockers))));
                return false;
            }
            var steps = new List<object>(result.Steps);
            steps.AddRange(extra);
            foreach (var step in steps)
                if (step is OmsiSplineChange change)
                    RefreshSplineViews(change);
            roadPorts = null;
            PushSteps(steps, Loc.T("signals.step", ("id", record.Id)));
            var selected = signalSelected;
            signalApplying = false;
            TargetSignalJunction(tile, record);
            if (selected >= 0 && signalCrossing != null && selected < signalCrossing.Movements.Count)
                SelectSignalMovement(selected);
            SetStatus(done);
            return true;
        }

        /// <summary>Notes, after a junction was made anew, the movements that lost their signal group with it (ADR 085).</summary>
        private OmsiGeneratedObject WithSignalReport(OmsiGeneratedObject generated)
        {
            if (generated != null && generated.LostSignalWays.Count > 0)
            {
                Debug.LogWarning("Signals: ways no longer in the junction lost their group: " + string.Join(" ", generated.LostSignalWays));
                SetStatusT("signals.status.lost", ("count", generated.LostSignalWays.Count));
            }
            return generated;
        }

        /// <summary>
        /// Command line, after <c>-signals</c>, for the window: <c>-signalWindowAt x y [width height]</c> puts it there (pixels of the area
        /// around it), <c>-signalWindowCollapse</c> collapses it, <c>-signalClose</c> clicks its close button (which asks when a draft has
        /// changes) and <c>-signalPromptAnswer apply|discard|keep</c> answers that question, <c>-signalGroupSelect n</c> chooses group n
        /// (from 1) in the list, <c>-signalGroupRemove n</c> clicks delete for group n (twice where it asks). The ways out that ask the same
        /// question: <c>-signalOpenMap global.cfg</c>, <c>-signalChrono yyyy-MM-dd</c>, <c>-signalObjects</c> (opens the object library) and
        /// <c>-signalRedo</c>.
        /// </summary>
        private void RunSignalWindowArguments(string[] args)
        {
            int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
            var at = Array.IndexOf(args, "-signalWindowAt");
            if (at >= 0 && at + 2 < args.Length && signalWindow != null)
            {
                var hasSize = at + 4 < args.Length && Number(args[at + 3]) > 0 && Number(args[at + 4]) > 0;
                signalWindow.PlaceAt(Number(args[at + 1]), Number(args[at + 2]), hasSize ? Number(args[at + 3]) : 0f, hasSize ? Number(args[at + 4]) : 0f);
            }
            if (Array.IndexOf(args, "-signalWindowCollapse") >= 0 && signalWindow != null)
                signalWindow.Collapsed = true;
        }

        /// <summary>The arguments that act on the crossing and the groups, after the crossing is read (see <see cref="RunSignalWindowArguments"/>).</summary>
        private void RunSignalGroupWindowArguments(string[] args)
        {
            int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
            var select = Array.IndexOf(args, "-signalGroupSelect");
            if (select >= 0 && select + 1 < args.Length && signalProgram != null && Number(args[select + 1]) >= 1 && Number(args[select + 1]) <= signalProgram.Lights.Count)
                SelectSignalGroup(Number(args[select + 1]) - 1);
            for (var index = 0; index + 1 < args.Length; index++)
                if (args[index] == "-signalGroupRemove" && signalProgram != null && Number(args[index + 1]) >= 1 && Number(args[index + 1]) <= signalProgram.Lights.Count)
                    RemoveSignalGroup(Number(args[index + 1]) - 1);
        }

        /// <summary>The arguments that leave the tool, last: the close button and the answer to its question.</summary>
        private void RunSignalLeaveArguments(string[] args)
        {
            if (Array.IndexOf(args, "-signalClose") >= 0)
                SetTool(EditorTool.Drag);
            // The other ways out, each of which asks when a draft has changes: another map, a chrono view, the object library, a redo.
            var openMap = Array.IndexOf(args, "-signalOpenMap");
            if (openMap >= 0 && openMap + 1 < args.Length)
                OpenMapInBackground(args[openMap + 1], string.Empty, null, "Could not read map: ", null);
            var chrono = Array.IndexOf(args, "-signalChrono");
            if (chrono >= 0 && chrono + 1 < args.Length && TryParseDay(args[chrono + 1], out var chronoDay))
                ShowChrono(chronoDay, null);
            if (Array.IndexOf(args, "-signalObjects") >= 0)
                ShowObjectsPanel(true);
            if (Array.IndexOf(args, "-signalRedo") >= 0)
                StepHistory(redoSteps, undoSteps, false);
            var answer = Array.IndexOf(args, "-signalPromptAnswer");
            if (answer >= 0 && answer + 1 < args.Length && signalGuard.Asking)
            {
                var word = args[answer + 1].ToLowerInvariant();
                AnswerSignalPrompt(word == "apply" ? SignalPromptChoice.Apply : word == "discard" ? SignalPromptChoice.Discard : SignalPromptChoice.KeepEditing);
            }
        }

        /// <summary>
        /// Command line, with a map: <c>-signals</c> starts the tool at the selected crossing and writes its approaches,
        /// lanes and movements to the log ("Signals:"); <c>-signalGroupAdd Name</c> adds a group, <c>-signalAssign m g</c>
        /// gives movement m (from 1) group g (from 1, 0 for none), <c>-signalSelect m</c> selects a movement.
        /// </summary>
        private void RunSignalArguments(string[] args)
        {
            if (Array.IndexOf(args, "-signals") < 0)
                return;
            SetTool(EditorTool.Signals);
            RunSignalWindowArguments(args);
            if (signalCrossing == null)
            {
                Debug.Log("Signals: no crossing selected");
                return;
            }
            var add = Array.IndexOf(args, "-signalGroupAdd");
            if (add >= 0 && add + 1 < args.Length && SignalsEditable)
            {
                var signals = CurrentSignals();
                signals.Program.AddLight(args[add + 1]);
                ApplySignals(signals, null, Loc.T("signals.done.added", ("name", args[add + 1]), ("id", signalJunction.Id)));
            }
            for (var index = 0; index + 2 < args.Length; index++)
                if (args[index] == "-signalAssign" && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var movement) &&
                    int.TryParse(args[index + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) && signalCrossing != null)
                    AssignSignalGroup(movement - 1, group - 1);
            var select = Array.IndexOf(args, "-signalSelect");
            if (select >= 0 && select + 1 < args.Length && int.TryParse(args[select + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var chosen) &&
                signalCrossing != null && chosen >= 1 && chosen <= signalCrossing.Movements.Count)
                SelectSignalMovement(chosen - 1);
            if (signalCrossing == null)
                return;
            RunSignalWizardArguments(args);
            if (signalCrossing == null)
                return;
            RunSignalTemplateArguments(args);
            if (signalCrossing == null)
                return;
            RunSignalGroupWindowArguments(args);
            if (signalCrossing == null)
                return;
            RunSignalTimelineArguments(args);
            if (signalCrossing == null)
                return;
            RunSignalCheckArguments(args);
            if (signalCrossing == null)
                return;
            RunSignalLeaveArguments(args);
            if (signalCrossing == null)
                return;
            Debug.Log("Signals: crossing " + signalJunction.Id + " (" + signalJunction.AssetPath + "), " + (SignalsEditable ? "editable" : "read-only") + ", " +
                      signalCrossing.Approaches.Count + " approaches, " + signalCrossing.Movements.Count + " movements, groups " +
                      string.Join(", ", signalProgram.Lights.Select(light => light.Name)));
            foreach (var movement in signalCrossing.Movements)
                Debug.Log("Signals: movement " + (movement.Index + 1) + " approach " + (movement.Approach + 1) + " lane " + (movement.Lane + 1) + " " + movement.Turn +
                          " to " + (movement.ExitApproach + 1) + " paths " + string.Join(",", movement.Paths) + " group " + (movement.Light + 1) +
                          (WayOf(movement) != null ? " way " + WayOf(movement) : string.Empty));
        }
    }
}
