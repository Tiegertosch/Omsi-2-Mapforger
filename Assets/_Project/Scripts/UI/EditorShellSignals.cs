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

        /// <summary>Colours that tell the signal groups apart; none of them is red, yellow or green, the colours of the lights.</summary>
        private static readonly Color[] SignalGroupColours =
        {
            new Color(0.30f, 0.55f, 0.96f), new Color(0.66f, 0.44f, 1.00f), new Color(0.13f, 0.76f, 0.84f), new Color(0.94f, 0.42f, 0.78f),
            new Color(0.48f, 0.55f, 1.00f), new Color(0.18f, 0.77f, 0.63f), new Color(0.79f, 0.63f, 1.00f), new Color(0.37f, 0.66f, 1.00f)
        };

        private VisualElement signalOptions;
        private Label signalJunctionLabel;
        private Label signalOwnerLabel;
        private VisualElement signalGroupsBox;
        private ScrollView signalMovementsBox;
        private Label signalPlanLabel;
        private OmsiTileSummary signalTile;
        private OmsiObjectRecord signalJunction;
        private OmsiGeneratedJunction signalMade;
        private OmsiSignalProgram signalProgram;
        private OmsiSignalIntersection signalCrossing;
        private OmsiJunctionLayout signalLayout;
        private readonly List<OmsiMapPoint[]> signalLines = new List<OmsiMapPoint[]>();
        private int signalSelected = -1;

        private static Color SignalGroupColour(int light)
        {
            return SignalGroupColours[((light % SignalGroupColours.Length) + SignalGroupColours.Length) % SignalGroupColours.Length];
        }

        private bool SignalsEditable => signalMade != null && CanEdit && signalLayout != null;

        private void CreateSignalOptions()
        {
            signalOptions = new VisualElement();
            signalOptions.AddToClassList("tool-panel");
            signalOptions.AddToClassList("picker-options");
            signalOptions.AddToClassList("signal-options");
            signalOptions.style.display = DisplayStyle.None;
            var title = LocBindings.NewLabel("signals.title");
            title.AddToClassList("picker-options-title");
            signalOptions.Add(title);
            signalJunctionLabel = new Label();
            signalJunctionLabel.AddToClassList("panel-info");
            signalJunctionLabel.style.marginTop = 0f;
            signalOptions.Add(signalJunctionLabel);
            signalOwnerLabel = new Label();
            signalOwnerLabel.AddToClassList("panel-info");
            signalOptions.Add(signalOwnerLabel);
            signalOptions.Add(CreateSignalTabs());
            signalMovementsPage = new VisualElement();
            var groupsTitle = LocBindings.NewLabel("signals.groups");
            groupsTitle.AddToClassList("inspector-section-heading");
            signalMovementsPage.Add(groupsTitle);
            signalGroupsBox = new VisualElement();
            signalMovementsPage.Add(signalGroupsBox);
            var movementsTitle = LocBindings.NewLabel("signals.movements");
            movementsTitle.AddToClassList("inspector-section-heading");
            signalMovementsPage.Add(movementsTitle);
            signalMovementsBox = new ScrollView(ScrollViewMode.Vertical);
            signalMovementsBox.AddToClassList("signal-movements");
            signalMovementsPage.Add(signalMovementsBox);
            signalPlanLabel = new Label();
            signalPlanLabel.AddToClassList("panel-info");
            signalMovementsPage.Add(signalPlanLabel);
            var headsTitle = LocBindings.NewLabel("signals.heads");
            headsTitle.AddToClassList("inspector-section-heading");
            signalMovementsPage.Add(headsTitle);
            signalHeadsBox = new VisualElement();
            signalMovementsPage.Add(signalHeadsBox);
            signalOptions.Add(signalMovementsPage);
            signalOptions.Add(CreateSignalPhasesPage());
            signalOptions.Add(CreateSignalTimelinePage());
            signalOptions.Add(CreateSignalWizardPage());
            signalOptions.Add(signalWizardButtons);
            signalOptions.Add(signalDraftRow);
            ShowSignalTab(SignalTab.Movements);
            objectsPanelHost.Add(signalOptions);
        }

        private void BeginSignalTool()
        {
            if (signalOptions == null || mapCanvas == null || currentSummary == null)
                return;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (terrainOptions != null)
                terrainOptions.style.display = DisplayStyle.None;
            signalOptions.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.ShowSplineEnds = false;
            mapCanvas.BeginRoad();
            ClearSignalTarget();
            var item = selection.Count == 1 ? selection[0] : null;
            if (item?.Tile == null || !TargetSignalJunction(item.Tile, item.Record))
                SetStatusT("signals.status.start");
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
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            ClearSignalTarget();
        }

        private void ClearSignalTarget()
        {
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
            if (signalJunctionLabel != null)
                LocBindings.Text(signalJunctionLabel, "signals.click_junction");
            if (signalOwnerLabel != null)
                signalOwnerLabel.text = string.Empty;
            signalGroupsBox?.Clear();
            signalMovementsBox?.Clear();
            if (signalPlanLabel != null)
                signalPlanLabel.text = string.Empty;
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
        private bool TargetSignalJunction(OmsiTileSummary tile, OmsiObjectRecord record)
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
            ResetSignalDraft(!sameCrossing);
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
            RenderSignalPanel();
            if (signalTab == SignalTab.Wizard && !SignalsEditable)
                ShowSignalTab(SignalTab.Movements);
            else if (signalTab == SignalTab.Wizard)
                RenderSignalWizard();
            RenderSignalDraft();
            UpdateSignalPreview();
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

        private void RefreshSignalTarget()
        {
            if (signalJunction == null || signalTile == null)
                return;
            var id = signalJunction.Id;
            // Undo puts other files back: a draft with changes is dropped.
            if (signalDirty)
            {
                signalDirty = false;
                SetStatusT("signals.status.discarded");
            }
            var current = signalTile.Objects.Find(candidate => candidate.Id == id);
            if (current == null || !TargetSignalJunction(signalTile, current))
                ClearSignalTarget();
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
            // While the assistant is open, the map shows the groups it proposes.
            var proposed = signalTab == SignalTab.Wizard ? signalProposal : null;
            foreach (var movement in signalCrossing.Movements)
            {
                var light = proposed != null ? proposed.MovementLights[movement.Index] : movement.Light;
                colours.Add(light >= 0 ? SignalGroupColour(light) : (Color?)null);
                selected.Add(signalSelected >= 0 && signalCrossing.Movements[signalSelected].ControlPath == movement.ControlPath);
            }
            mapCanvas.SetSignalMovements(signalLines, colours, selected, signalJunction.Height);
        }

        /// <summary>The panel: the signal groups with their colours, then the movements by approach and lane with the group each obeys.</summary>
        private void RenderSignalPanel()
        {
            signalGroupsBox.Clear();
            signalMovementsBox.Clear();
            if (signalCrossing == null)
                return;
            LocBindings.Text(signalJunctionLabel, "signals.junction", ("id", signalJunction.Id), ("approaches", signalCrossing.Approaches.Count),
                ("movements", signalCrossing.Movements.Count), ("groups", signalProgram.Lights.Count));
            var editable = SignalsEditable;
            signalOwnerLabel.text = editable ? Loc.T("signals.owner.made")
                : signalMade != null ? Loc.T(CanEdit ? "signals.owner.mismatch" : "signals.owner.readonly") // i18n:keys
                : Loc.T("signals.owner.omsi");
            for (var light = 0; light < signalProgram.Lights.Count; light++)
                signalGroupsBox.Add(SignalGroupRow(light, editable));
            if (signalProgram.Lights.Count == 0)
            {
                var none = LocBindings.NewLabel("signals.groups.none");
                none.AddToClassList("panel-info");
                signalGroupsBox.Add(none);
            }
            if (editable)
            {
                // Without groups the assistant is offered first (§6 of the brief); it stays at hand next to adding a group by hand.
                if (signalProgram.Lights.Count == 0)
                {
                    var offer = LocBindings.NewLabel("signals.wizard.offer");
                    offer.AddToClassList("panel-info");
                    signalGroupsBox.Add(offer);
                }
                var buttons = new VisualElement();
                buttons.AddToClassList("signal-group-row");
                buttons.Add(SignalWizardButton());
                var add = LocBindings.WithTooltip(LocBindings.NewButton("signals.group.add", AddSignalGroup), "signals.group.add.tooltip");
                add.AddToClassList("map-control-button");
                buttons.Add(add);
                signalGroupsBox.Add(buttons);
            }

            var shown = new HashSet<int>();
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
        }

        private VisualElement SignalGroupRow(int light, bool editable)
        {
            var row = new VisualElement();
            row.AddToClassList("signal-group-row");
            var chip = new VisualElement();
            chip.AddToClassList("signal-group-chip");
            chip.style.backgroundColor = SignalGroupColour(light);
            row.Add(chip);
            var name = new TextField { value = signalProgram.Lights[light].Name, isDelayed = true };
            name.AddToClassList("picker-field");
            name.AddToClassList("signal-group-name");
            name.SetEnabled(editable);
            name.tooltip = Loc.T("signals.group.name.tooltip", ("n", light));
            name.RegisterValueChangedCallback(evt => RenameSignalGroup(light, evt.newValue));
            row.Add(name);
            var count = new Label(Loc.T("signals.group.count", ("count", signalCrossing.Movements.Count(movement => movement.Light == light))));
            count.AddToClassList("signal-group-count");
            row.Add(count);
            if (editable)
            {
                var remove = new Button(() => RemoveSignalGroup(light)) { tooltip = Loc.T("signals.group.remove.tooltip") };
                remove.AddToClassList("panel-icon-button");
                remove.Add(new IconElement("trash-2"));
                row.Add(remove);
            }
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
            row.EnableInClassList("signal-movement-selected", signalSelected >= 0 && signalCrossing.Movements[signalSelected].ControlPath == movement.ControlPath);
            var chip = new VisualElement();
            chip.AddToClassList("signal-group-chip");
            chip.style.backgroundColor = movement.Light >= 0 ? SignalGroupColour(movement.Light) : new Color(0.45f, 0.48f, 0.53f);
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
            RenderSignalPanel();
            UpdateSignalPreview();
        }

        private void UpdateSignalPlan()
        {
            if (signalPlanLabel == null)
                return;
            if (signalCrossing == null)
                signalPlanLabel.text = string.Empty;
            else if (!SignalsEditable)
                signalPlanLabel.text = Loc.T("signals.plan.readonly");
            else if (signalSelected >= 0)
                signalPlanLabel.text = Loc.T("signals.plan.selected", ("text", MovementText(signalCrossing.SharingControl(signalCrossing.Movements[signalSelected]),
                    null)));
            else
                signalPlanLabel.text = Loc.T("signals.plan.point");
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
            if (!SignalsEditable)
                return;
            var signals = CurrentSignals();
            var index = signals.Program.AddLight(Loc.T("signals.group.default_name", ("n", signals.Program.Lights.Count + 1)));
            ApplySignals(signals, null, Loc.T("signals.done.added", ("name", signals.Program.Lights[index].Name), ("id", signalJunction.Id)));
        }

        private void RenameSignalGroup(int light, string name)
        {
            if (!SignalsEditable || light >= signalProgram.Lights.Count)
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
            if (!SignalsEditable || light >= signalProgram.Lights.Count)
                return;
            var heads = OmsiSignalHead.Of(currentSummary, signalTile, signalJunction.Id, path => PrioritySceneryObject(path)?.IsTrafficLight == true);
            var showing = heads.Count(head => (head.LightIndex ?? 0) == light);
            if (showing > 0)
            {
                SetStatusT("signals.status.heads_show", ("name", signalProgram.Lights[light].Name), ("count", showing));
                return;
            }
            var signals = CurrentSignals();
            var name = signals.Program.Lights[light].Name;
            var map = signals.Program.RemoveLight(light);
            foreach (var key in signals.WayLights.Keys.ToList())
            {
                var moved = map[signals.WayLights[key]];
                if (moved < 0)
                    signals.WayLights.Remove(key);
                else
                    signals.WayLights[key] = moved;
            }
            // Heads of the groups after it follow their group to its new number, in the same step.
            var relabel = heads.Where(head => head.LightIndex.HasValue && head.LightIndex.Value > light).ToList();
            ApplySignals(signals, relabel.Count > 0 ? (Func<List<object>>)(() => RelabelHeads(relabel, map)) : null,
                Loc.T("signals.done.removed", ("name", name), ("id", signalJunction.Id)));
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
            if (!SignalsEditable || signalCrossing == null || movementIndex >= signalCrossing.Movements.Count)
                return;
            var movement = signalCrossing.Movements[movementIndex];
            var way = WayOf(movement);
            if (way == null || movement.Light == light)
                return;
            var signals = CurrentSignals();
            if (light < 0)
                signals.WayLights.Remove(way);
            else
                signals.WayLights[way] = light;
            signalSelected = movementIndex;
            ApplySignals(signals, null, Loc.T(light < 0 ? "signals.done.unassigned" : "signals.done.assigned", // i18n:keys
                ("text", MovementText(new List<OmsiSignalMovement> { movement }, null)),
                ("name", light < 0 ? string.Empty : signals.Program.Lights[light].Name), ("id", signalJunction.Id)));
        }

        /// <summary>
        /// Makes the junction again with these signals under Sceneryobjects\Mapforge and puts it in place of the old one, as one
        /// undo step together with <paramref name="more"/> (changes of heads), as the markings tool does (ADR 044).
        /// </summary>
        private void ApplySignals(OmsiJunctionSignals signals, Func<List<object>> more, string done)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.edit_signals"));
                return;
            }
            var record = signalJunction;
            var made = signalMade;
            var tile = signalTile;
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
            if (rebuild == null)
            {
                SetStatusT("lanes.status.mismatch", ("id", record.Id));
                return;
            }
            var plain = PlainRoadTexture();
            var turn = rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, plain);
            if (turn.HasErrors)
            {
                SetStatusT("lanes.status.stays", ("id", record.Id), ("reason", turn.Problem ?? Loc.T("lanes.stays.default")));
                return;
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
                return;
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
                return;
            }
            if (!result.Succeeded)
            {
                RefreshAfterChange(null);
                SetStatus(Loc.T("lanes.status.stays_refs", ("id", record.Id), ("references", DescribeReferences(result.Blockers))));
                return;
            }
            var steps = new List<object>(result.Steps);
            steps.AddRange(extra);
            foreach (var step in steps)
                if (step is OmsiSplineChange change)
                    RefreshSplineViews(change);
            roadPorts = null;
            PushSteps(steps, Loc.T("signals.step", ("id", record.Id)));
            var selected = signalSelected;
            TargetSignalJunction(tile, record);
            if (selected >= 0 && signalCrossing != null && selected < signalCrossing.Movements.Count)
                SelectSignalMovement(selected);
            SetStatus(done);
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
        /// Command line, with a map: <c>-signals</c> starts the tool at the selected crossing and writes its approaches,
        /// lanes and movements to the log ("Signals:"); <c>-signalGroupAdd Name</c> adds a group, <c>-signalAssign m g</c>
        /// gives movement m (from 1) group g (from 1, 0 for none), <c>-signalSelect m</c> selects a movement.
        /// </summary>
        private void RunSignalArguments(string[] args)
        {
            if (Array.IndexOf(args, "-signals") < 0)
                return;
            SetTool(EditorTool.Signals);
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
            RunSignalTimelineArguments(args);
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
