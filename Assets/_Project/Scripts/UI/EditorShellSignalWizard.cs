using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The signal assistant and the signal heads of the traffic signals tool (ADR 085, milestone 4; §6 and §9 of the brief).
    /// The assistant proposes groups and a basic program for a junction made here (<see cref="OmsiSignalWizard"/>), shows
    /// them on the map and on a timeline with what the conflict check found, and on Use writes them with heads for every
    /// group, as one undo step. The heads of any crossing are listed with their group, which can be changed wherever the
    /// map can be edited: it is only the head's first label in the tile, so OMSI's own crossings too (decided by the user).
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>Heads are hung this high above the junction when no head of the type stands in the map yet, as OMSI's own do.</summary>
        private const double DefaultHeadHeight = 2.8;

        /// <summary>The most a mast stands below its head to count when masts are learnt from a map.</summary>
        private const double MaxMastDrop = 9.0;

        /// <summary>Head files of each installation, by the root: the object files with [trafficlight].</summary>
        private static readonly Dictionary<string, List<string>> HeadTypesByRoot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private VisualElement signalWizardPage;
        private Label signalWizardInfo;
        private Toggle signalWizardLeftToggle;
        private Toggle signalWizardFootToggle;
        private VisualElement signalWizardTimes;
        private VisualElement signalWizardGroups;
        private SignalTimelineElement signalWizardTimeline;
        private Label signalWizardCheck;
        private Toggle signalWizardHeadsToggle;
        private DropdownField signalWizardHeadType;
        private DropdownField signalWizardLeftHeadType;
        private TextField signalWizardHeadHeight;
        private DropdownField signalWizardMastType;
        private TextField signalWizardMastSearch;

        /// <summary>The mast files behind the entries of the mast field; null for no mast.</summary>
        private List<string> signalMastChoices = new List<string>();

        /// <summary>Which heads and masts the open map uses, or another map of the installation when it has no heads; found once for it.</summary>
        private SignalUsage signalUsage;
        private OmsiMapSummary signalUsageOf;

        /// <summary>The usage learnt from another map of each installation, for maps without heads of their own.</summary>
        private static readonly Dictionary<string, SignalUsage> ReferenceUsageByRoot = new Dictionary<string, SignalUsage>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// How a map uses signal heads: how often each head type stands in it, and the masts under them. A new map has none,
        /// so the assistant learns from the smallest other map of the installation that has heads (OMSI's Grundorf in most).
        /// </summary>
        private sealed class SignalUsage
        {
            public Dictionary<string, int> Heads = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<SignalMastType> Masts = new List<SignalMastType>();

            /// <summary>The other map learnt from, or null for the open map.</summary>
            public string MapName;
        }
        private Label signalHeadsPlaceWith;

        /// <summary>
        /// A mast type found under heads of the map: how often, and how it stands to its head there (median height below
        /// the head, turn against the head), so a new one stands the same way.
        /// </summary>
        private sealed class SignalMastType
        {
            public string AssetPath;
            public int Count;
            public double Drop;
            public double Turn;
        }
        private Button signalWizardUseButton;

        /// <summary>Back and Use, below the assistant's scrolling page so they stay in sight.</summary>
        private VisualElement signalWizardButtons;
        private OmsiSignalProposal signalProposal;
        private readonly OmsiSignalWizardOptions signalWizardOptions = new OmsiSignalWizardOptions();
        private bool signalWizardConfirm;
        private List<OmsiSignalConflict> signalConflicts;
        private OmsiSignalIntersection signalConflictsOf;
        private VisualElement signalHeadsBox;
        private List<string> signalHeadTypes = new List<string>();

        /// <summary>Heads that could not be placed in the last placing, which the status names.</summary>
        private int signalHeadFailures;

        /// <summary>The button that opens the assistant, offered on the movements and the empty phases page.</summary>
        private Button SignalWizardButton()
        {
            var button = LocBindings.WithTooltip(LocBindings.NewButton("signals.wizard.open", OpenSignalWizard), "signals.wizard.open.tooltip");
            button.AddToClassList("map-control-button");
            button.AddToClassList("dialog-primary");
            return button;
        }

        private VisualElement CreateSignalWizardPage()
        {
            signalWizardPage = new ScrollView(ScrollViewMode.Vertical);
            signalWizardPage.AddToClassList("signal-scroll-page");
            var title = LocBindings.NewLabel("signals.wizard.title");
            title.AddToClassList("inspector-section-heading");
            signalWizardPage.Add(title);
            signalWizardInfo = new Label();
            signalWizardInfo.AddToClassList("panel-info");
            signalWizardPage.Add(signalWizardInfo);

            signalWizardLeftToggle = new Toggle();
            LocBindings.Text(signalWizardLeftToggle, "signals.wizard.separate_left");
            LocBindings.Tooltip(signalWizardLeftToggle, "signals.wizard.separate_left.tooltip");
            signalWizardLeftToggle.value = signalWizardOptions.SeparateLeftTurns;
            signalWizardLeftToggle.RegisterValueChangedCallback(evt =>
            {
                signalWizardOptions.SeparateLeftTurns = evt.newValue;
                RenderSignalWizard();
            });
            signalWizardPage.Add(signalWizardLeftToggle);

            signalWizardFootToggle = new Toggle();
            LocBindings.Text(signalWizardFootToggle, "signals.wizard.foot");
            LocBindings.Tooltip(signalWizardFootToggle, "signals.wizard.foot.tooltip");
            signalWizardFootToggle.value = signalWizardOptions.PedestrianLights;
            signalWizardFootToggle.RegisterValueChangedCallback(evt =>
            {
                signalWizardOptions.PedestrianLights = evt.newValue;
                RenderSignalWizard();
            });
            signalWizardPage.Add(signalWizardFootToggle);

            signalWizardTimes = new VisualElement();
            signalWizardTimes.AddToClassList("signal-phase-groups");
            signalWizardPage.Add(signalWizardTimes);

            var groupsTitle = LocBindings.NewLabel("signals.wizard.groups");
            groupsTitle.AddToClassList("inspector-section-heading");
            signalWizardPage.Add(groupsTitle);
            signalWizardGroups = new VisualElement();
            signalWizardPage.Add(signalWizardGroups);
            signalWizardTimeline = new SignalTimelineElement();
            signalWizardPage.Add(signalWizardTimeline);
            signalWizardCheck = new Label();
            signalWizardCheck.AddToClassList("panel-info");
            signalWizardCheck.AddToClassList("signal-issues");
            signalWizardPage.Add(signalWizardCheck);

            signalWizardHeadsToggle = new Toggle { value = true };
            LocBindings.Text(signalWizardHeadsToggle, "signals.wizard.heads");
            LocBindings.Tooltip(signalWizardHeadsToggle, "signals.wizard.heads.tooltip");
            signalWizardHeadsToggle.RegisterValueChangedCallback(_ => RenderSignalWizard());
            signalWizardPage.Add(signalWizardHeadsToggle);
            signalWizardHeadType = SignalHeadTypeField("signals.heads.type");
            signalWizardPage.Add(signalWizardHeadType);
            signalWizardLeftHeadType = SignalHeadTypeField("signals.heads.left_type");
            signalWizardPage.Add(signalWizardLeftHeadType);
            signalWizardHeadHeight = new TextField { isDelayed = true, value = Seconds(DefaultHeadHeight) };
            LocBindings.Text(signalWizardHeadHeight, "signals.heads.height");
            LocBindings.Tooltip(signalWizardHeadHeight, "signals.heads.height.tooltip");
            signalWizardHeadHeight.AddToClassList("picker-field");
            signalWizardPage.Add(signalWizardHeadHeight);
            signalWizardMastType = SignalHeadTypeField("signals.heads.mast");
            LocBindings.Tooltip(signalWizardMastType, "signals.heads.mast.tooltip");
            signalWizardPage.Add(signalWizardMastType);
            signalWizardMastSearch = new TextField { isDelayed = true };
            LocBindings.Text(signalWizardMastSearch, "signals.heads.mast_search");
            LocBindings.Tooltip(signalWizardMastSearch, "signals.heads.mast_search.tooltip");
            signalWizardMastSearch.AddToClassList("picker-field");
            signalWizardMastSearch.RegisterValueChangedCallback(_ => FillSignalMastTypes(true));
            signalWizardPage.Add(signalWizardMastSearch);

            var buttons = new VisualElement();
            buttons.AddToClassList("signal-group-row");
            var back = LocBindings.WithTooltip(LocBindings.NewButton("signals.wizard.back", () => ShowSignalTab(SignalTab.Movements)), "signals.wizard.back.tooltip");
            back.AddToClassList("map-control-button");
            buttons.Add(back);
            signalWizardUseButton = LocBindings.WithTooltip(LocBindings.NewButton("signals.wizard.use", UseSignalProposal), "signals.wizard.use.tooltip");
            signalWizardUseButton.AddToClassList("map-control-button");
            signalWizardUseButton.AddToClassList("dialog-primary");
            buttons.Add(signalWizardUseButton);
            signalWizardButtons = buttons;
            signalWizardButtons.style.display = DisplayStyle.None;
            signalWizardPage.style.display = DisplayStyle.None;
            return signalWizardPage;
        }

        private DropdownField SignalHeadTypeField(string key)
        {
            var field = new DropdownField();
            LocBindings.Text(field, key); // i18n:keys
            LocBindings.Tooltip(field, "signals.heads.type.tooltip");
            field.AddToClassList("signal-head-type");
            return field;
        }

        private void OpenSignalWizard()
        {
            if (!SignalsEditable)
                return;
            signalWizardConfirm = false;
            // Heads for every group: on by default where the crossing has none yet.
            signalWizardHeadsToggle.SetValueWithoutNotify(SignalHeadsOfCrossing().Count == 0);
            ShowSignalTab(SignalTab.Wizard);
            RenderSignalWizard();
        }

        /// <summary>The conflicts of the crossing shown, found once for it.</summary>
        private List<OmsiSignalConflict> SignalConflicts()
        {
            if (signalCrossing == null)
                return new List<OmsiSignalConflict>();
            if (!ReferenceEquals(signalConflictsOf, signalCrossing))
            {
                var sceneryObject = PrioritySceneryObject(signalJunction.AssetPath);
                signalConflicts = sceneryObject != null ? OmsiSignalConflicts.Of(sceneryObject, signalCrossing, signalProgram?.PathLights) : new List<OmsiSignalConflict>();
                signalConflictsOf = signalCrossing;
            }
            return signalConflicts;
        }

        private void RenderSignalWizard()
        {
            if (signalWizardPage == null || signalCrossing == null || !SignalsEditable)
                return;
            var crossing = signalCrossing;
            // The footpaths over the arms of a junction made here can follow a light: those with a way to bind them by.
            var bindableFootpaths = crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && WayOf(movement) != null).Select(movement => movement.Index).ToList();
            var proposal = OmsiSignalWizard.Propose(crossing, SignalConflicts(), signalWizardOptions, OmsiSignalWizard.TurnLaneMovements(crossing, signalLayout), bindableFootpaths);
            var footNumber = 0;
            foreach (var group in proposal.Groups)
            {
                if (group.Pedestrian)
                {
                    group.Name = Loc.T("signals.wizard.group_foot", ("n", ++footNumber));
                    continue;
                }
                var arms = string.Join("+", group.Approaches.Select(approach => (approach + 1).ToString(CultureInfo.InvariantCulture)));
                group.Name = Loc.T(group.LeftTurns ? "signals.wizard.group_left" : "signals.wizard.group", ("arms", arms)); // i18n:keys
            }
            for (var light = 0; light < proposal.Groups.Count; light++)
                proposal.Program.Lights[light].Name = proposal.Groups[light].Name;
            signalProposal = proposal;

            var heads = SignalHeadsOfCrossing();
            var vehicles = crossing.Movements.Count(movement => movement.Turn != OmsiMovementTurn.Pedestrian);
            signalWizardInfo.text = Loc.T("signals.wizard.analysis", ("approaches", crossing.Approaches.Count),
                ("lanes", crossing.Approaches.Sum(approach => approach.Lanes.Count)), ("movements", vehicles), ("groups", signalProgram.Lights.Count),
                ("heads", heads.Count));
            signalWizardLeftToggle.style.display = proposal.LeftLanes > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            signalWizardFootToggle.style.display = bindableFootpaths.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            signalWizardTimes.Clear();
            signalWizardTimes.Add(SignalWizardField("signals.wizard.green", signalWizardOptions.GreenSeconds, false, value => signalWizardOptions.GreenSeconds = value));
            if (proposal.Groups.Any(group => group.LeftTurns))
                signalWizardTimes.Add(SignalWizardField("signals.wizard.left_green", signalWizardOptions.LeftGreenSeconds, false,
                    value => signalWizardOptions.LeftGreenSeconds = value));
            signalWizardTimes.Add(SignalWizardField("signals.phases.red_yellow", signalWizardOptions.RedYellowSeconds, true, value => signalWizardOptions.RedYellowSeconds = value));
            signalWizardTimes.Add(SignalWizardField("signals.phases.yellow", signalWizardOptions.YellowSeconds, true, value => signalWizardOptions.YellowSeconds = value));
            signalWizardTimes.Add(SignalWizardField("signals.phases.clearance", signalWizardOptions.ClearanceSeconds, true, value => signalWizardOptions.ClearanceSeconds = value));

            signalWizardGroups.Clear();
            for (var light = 0; light < proposal.Groups.Count; light++)
            {
                var row = new VisualElement();
                row.AddToClassList("signal-group-row");
                var chip = new VisualElement();
                chip.AddToClassList("signal-group-chip");
                chip.style.backgroundColor = SignalGroupColour(light);
                row.Add(chip);
                var name = new Label(Loc.T("signals.wizard.group_row", ("name", proposal.Groups[light].Name), ("count", proposal.Groups[light].Movements.Count)));
                name.AddToClassList("signal-movement-text");
                row.Add(name);
                signalWizardGroups.Add(row);
            }
            var phases = new Label(Loc.T("signals.wizard.phases", ("count", proposal.Plan.Phases.Count), ("cycle", Seconds(proposal.Plan.CycleSeconds))));
            phases.AddToClassList("panel-info");
            signalWizardGroups.Add(phases);
            signalWizardTimeline.Show(proposal.Program, SignalGroupColour, false);

            var lines = new List<string>();
            lines.Add(proposal.ConflictFree
                ? Loc.T("signals.wizard.check.free", ("permitted", proposal.PermittedClashes.Count))
                : Loc.T("signals.wizard.check.conflicts", ("count", proposal.HardClashes.Count)));
            foreach (var clash in proposal.HardClashes.Take(4))
                lines.Add(Loc.T("signals.wizard.check.clash", ("first", MovementText(new List<OmsiSignalMovement> { crossing.Movements[clash.Conflict.First] }, null)),
                    ("second", MovementText(new List<OmsiSignalMovement> { crossing.Movements[clash.Conflict.Second] }, null)), ("seconds", Seconds(clash.Seconds))));
            if (proposal.CrossingFootpaths > 0)
                lines.Add(Loc.T("signals.wizard.check.footpaths", ("count", proposal.CrossingFootpaths)));
            if (signalProgram.Lights.Count > 0)
                lines.Add(Loc.T("signals.wizard.replaces", ("count", signalProgram.Lights.Count)));
            if (heads.Count > 0)
                lines.Add(Loc.T("signals.wizard.heads_kept", ("count", heads.Count)));
            signalWizardCheck.text = string.Join("\n", lines);

            FillSignalHeadTypes(signalWizardHeadType, false);
            FillSignalHeadTypes(signalWizardLeftHeadType, true);
            var placing = signalWizardHeadsToggle.value && signalHeadTypes.Count > 0;
            signalWizardHeadsToggle.SetEnabled(signalHeadTypes.Count > 0);
            signalWizardHeadType.style.display = placing ? DisplayStyle.Flex : DisplayStyle.None;
            signalWizardLeftHeadType.style.display = placing && proposal.Groups.Any(group => group.LeftTurns) ? DisplayStyle.Flex : DisplayStyle.None;
            signalWizardHeadHeight.style.display = placing ? DisplayStyle.Flex : DisplayStyle.None;
            FillSignalMastTypes(false);
            signalWizardMastType.style.display = placing ? DisplayStyle.Flex : DisplayStyle.None;
            signalWizardMastSearch.style.display = placing ? DisplayStyle.Flex : DisplayStyle.None;

            signalWizardUseButton.text = Loc.T(signalWizardConfirm ? "signals.wizard.confirm" : "signals.wizard.use", ("count", signalProgram.Lights.Count)); // i18n:keys
            signalWizardUseButton.SetEnabled(proposal.Groups.Count > 0);
            UpdateSignalPreview();
        }

        private VisualElement SignalWizardField(string key, double value, bool zeroAllowed, Action<double> changed)
        {
            return SignalPlanField(key, value, true, seconds =>
            {
                if (!zeroAllowed && seconds <= 0)
                    return;
                changed(seconds);
                RenderSignalWizard();
            }, zeroAllowed);
        }

        /// <summary>Writes the proposal: groups, program and bindings anew, with heads when chosen, as one undo step.</summary>
        private void UseSignalProposal()
        {
            if (!SignalsEditable || signalProposal == null || signalProposal.Groups.Count == 0)
                return;
            if (signalProgram.Lights.Count > 0 && !signalWizardConfirm)
            {
                // Replacing groups that exist asks once more, on the same button.
                signalWizardConfirm = true;
                RenderSignalWizard();
                return;
            }
            signalWizardConfirm = false;
            var proposal = signalProposal;
            var signals = new OmsiJunctionSignals();
            signals.SetProgram(proposal.Program);
            var unbound = 0;
            foreach (var movement in signalCrossing.Movements)
            {
                var light = proposal.MovementLights[movement.Index];
                if (light < 0)
                    continue;
                var way = WayOf(movement);
                if (way == null)
                    unbound++;
                else
                    signals.WayLights[way] = light;
            }
            if (unbound > 0)
                Debug.LogWarning("Signals: the assistant could not bind " + unbound + " movements without a way");
            Func<List<object>> heads = null;
            if (signalWizardHeadsToggle.value && signalHeadTypes.Count > 0)
            {
                var type = signalWizardHeadType.index >= 0 ? signalHeadTypes[signalWizardHeadType.index] : null;
                var leftType = signalWizardLeftHeadType.index >= 0 ? signalHeadTypes[signalWizardLeftHeadType.index] : type;
                var height = TrySignalSeconds(signalWizardHeadHeight.value, out var metres) ? metres : DefaultHeadHeight;
                var mast = ChosenSignalMast();
                var crossing = signalCrossing;
                var lights = proposal.MovementLights;
                var lefts = new HashSet<int>(Enumerable.Range(0, proposal.Groups.Count).Where(light => proposal.Groups[light].LeftTurns));
                if (type != null)
                    heads = () => PlaceSignalHeads(crossing, lights, lefts, Enumerable.Range(0, proposal.Groups.Count), type, leftType, height, mast);
            }
            var placed = heads != null;
            signalHeadFailures = 0;
            ApplySignals(signals, heads, Loc.T(placed ? "signals.done.wizard_heads" : "signals.done.wizard", ("id", signalJunction.Id), // i18n:keys
                ("groups", proposal.Groups.Count), ("cycle", Seconds(proposal.Plan.CycleSeconds))));
            if (signalHeadFailures > 0)
                SetStatusT("signals.heads.status.place_failed", ("count", signalHeadFailures));
            ShowSignalTab(SignalTab.Phases);
        }

        /// <summary>
        /// Heads for the lights given: for every approach a light serves, one head at its stop line, on the right of the
        /// lanes (on the left for left turns from a lane of their own), facing the traffic, with the crossing as variable
        /// parent and the light's number as first label, as OMSI's own crossings have them. Appended at the tile end.
        /// </summary>
        private List<object> PlaceSignalHeads(OmsiSignalIntersection crossing, IList<int> movementLights, ICollection<int> leftLights, IEnumerable<int> forLights,
            string type, string leftType, double height, SignalMastType mast)
        {
            var steps = new List<object>();
            signalHeadFailures = 0;
            var record = signalJunction;
            var tile = signalTile;
            var sceneryObject = PrioritySceneryObject(record.AssetPath);
            // A chrono variant has no command for a variable parent: heads placed there would show no crossing.
            if (sceneryObject == null || session.EditedVariant != null)
                return steps;
            var size = currentSummary.TileSizeMeters;
            var terrain = new OmsiTerrainSampler(currentSummary, session.Source);
            // OMSI stands objects on the terrain and takes their height as above it; only junctions and [absheight] objects
            // keep it as it is. Heights here are above the junction's surface, which is turned into what the file needs.
            double FileHeight(string assetPath, OmsiMapPoint at, double aboveJunction)
            {
                var placed = PrioritySceneryObject(assetPath);
                if (placed != null && (placed.HasAbsoluteHeight || placed.HasSplineHelper))
                    return record.Height + aboveJunction;
                return terrain.TryGetHeight(at.X, at.Y, out var ground) ? record.Height + aboveJunction - ground : aboveJunction;
            }
            // Without a mast to learn from, a mast stands on the junction's pavement, the height of its footpaths.
            var pavement = sceneryObject.Paths.Where(path => path.Kind == 1).Select(path => path.Height).DefaultIfEmpty(0.0).Max();
            var footType = PedestrianHeadType();
            foreach (var light in forLights)
            {
                var left = leftLights.Contains(light);
                // A crossing's light: a head at each end of the footpath, beside it, facing the people who wait there.
                foreach (var foot in crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && movementLights[movement.Index] == light))
                {
                    if (footType == null)
                        break;
                    var footPath = sceneryObject.Paths[foot.ControlPath < sceneryObject.Paths.Count ? foot.ControlPath : foot.Paths[0]];
                    var half = Math.Max(1.0, footPath.WidthMeters / 2.0);
                    var dx = foot.ExitX - foot.EntryX;
                    var dy = foot.ExitY - foot.EntryY;
                    var walk = Math.Sqrt(dx * dx + dy * dy);
                    if (walk < 0.5)
                        continue;
                    for (var end = 0; end < 2; end++)
                    {
                        // People wait at one end and read the head at the other: it stands just past the far end, beside the way,
                        // facing back across the crossing to them.
                        var sign = end == 0 ? 1.0 : -1.0;
                        double forwardX = dx / walk * sign, forwardY = dy / walk * sign;
                        double rightX = forwardY, rightY = -forwardX;
                        var baseX = end == 0 ? foot.ExitX : foot.EntryX;
                        var baseY = end == 0 ? foot.ExitY : foot.EntryY;
                        var localX = baseX + rightX * (half + 0.7) + forwardX * 0.4;
                        var localY = baseY + rightY * (half + 0.7) + forwardY * 0.4;
                        var world = ObjectToWorld(tile, record, localX, localY);
                        var heading = OmsiRoadPlanner.Normalize(record.HeadingDegrees + Math.Atan2(forwardX, forwardY) * 180.0 / Math.PI + 180.0);
                        if (heading < 0)
                            heading += 360.0;
                        // Not lower than the mast's top, or the mast would start below the surface.
                        var footHeight = mast != null && mast.Count > 0 ? Math.Max(PedestrianHeadHeight, mast.Drop) : PedestrianHeadHeight;
                        try
                        {
                            var change = session.PlaceObject(currentSummary, tile, footType, world.X - tile.X * size, world.Y - tile.Y * size,
                                FileHeight(footType, world, footHeight), heading, 0.0, 0.0, new List<string> { light.ToString(CultureInfo.InvariantCulture) });
                            steps.Add(change);
                            var parent = session.SetVarParent(currentSummary, tile, change.Record, record.Id);
                            if (parent != null)
                                steps.Add(parent);
                            if (mast != null)
                            {
                                var mastHeading = OmsiRoadPlanner.Normalize(heading + mast.Turn);
                                if (mastHeading < 0)
                                    mastHeading += 360.0;
                                var mastHeight = FileHeight(mast.AssetPath, world, mast.Count > 0 ? footHeight - mast.Drop : pavement);
                                steps.Add(session.PlaceObject(currentSummary, tile, mast.AssetPath, world.X - tile.X * size, world.Y - tile.Y * size, mastHeight, mastHeading));
                            }
                        }
                        catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
                        {
                            Debug.LogException(exception);
                            signalHeadFailures++;
                        }
                    }
                }
                foreach (var approachGroup in crossing.Movements.Where(movement => movement.Approach >= 0 && movementLights[movement.Index] == light &&
                                                                                   movement.Turn != OmsiMovementTurn.Pedestrian).GroupBy(movement => movement.Approach))
                {
                    var approach = crossing.Approaches[approachGroup.Key];
                    var inward = approach.InwardDegrees * Math.PI / 180.0;
                    double forwardX = Math.Sin(inward), forwardY = Math.Cos(inward), rightX = Math.Cos(inward), rightY = -Math.Sin(inward);
                    // The stop line of each movement: where its way starts (after a turn lane's lead-in).
                    var stops = approachGroup.Select(movement =>
                    {
                        // Before a crossing the stop is the start of the lane's lead-in, else the first arc of the way.
                        var stopPath = movement.Paths.FirstOrDefault(index => signalLayout != null && index < signalLayout.Paths.Count &&
                                                                              (signalLayout.Paths[index].LeadInLane != null || signalLayout.Paths[index].StartsWay));
                        var path = sceneryObject.Paths[stopPath < sceneryObject.Paths.Count ? stopPath : movement.ControlPath];
                        var entry = OmsiSignalWizard.EntryOf(path);
                        return (X: entry.X, Y: entry.Y, Half: Math.Max(1.0, entry.HalfWidth));
                    }).ToList();
                    var across = stops.Select(stop => stop.X * rightX + stop.Y * rightY).ToList();
                    var chosen = stops[left ? across.IndexOf(across.Min()) : across.IndexOf(across.Max())];
                    var offset = left ? -(chosen.Half + 0.75) : chosen.Half + 1.5;
                    var localX = chosen.X + rightX * offset - forwardX * 0.5;
                    var localY = chosen.Y + rightY * offset - forwardY * 0.5;
                    var world = ObjectToWorld(tile, record, localX, localY);
                    var heading = OmsiRoadPlanner.Normalize(record.HeadingDegrees + approach.InwardDegrees + 180.0);
                    if (heading < 0)
                        heading += 360.0;
                    // A head that cannot be placed is left out and counted; the junction made anew stays, with the others.
                    try
                    {
                        var headType = left ? leftType : type;
                        var change = session.PlaceObject(currentSummary, tile, headType, world.X - tile.X * size, world.Y - tile.Y * size,
                            FileHeight(headType, world, height), heading, 0.0, 0.0, new List<string> { light.ToString(CultureInfo.InvariantCulture) });
                        steps.Add(change);
                        var parent = session.SetVarParent(currentSummary, tile, change.Record, record.Id);
                        if (parent != null)
                            steps.Add(parent);
                        if (mast != null)
                        {
                            var mastHeading = OmsiRoadPlanner.Normalize(heading + mast.Turn);
                            if (mastHeading < 0)
                                mastHeading += 360.0;
                            var mastHeight = FileHeight(mast.AssetPath, world, mast.Count > 0 ? height - mast.Drop : pavement);
                            steps.Add(session.PlaceObject(currentSummary, tile, mast.AssetPath, world.X - tile.X * size, world.Y - tile.Y * size, mastHeight, mastHeading));
                        }
                    }
                    catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
                    {
                        Debug.LogException(exception);
                        signalHeadFailures++;
                    }
                }
            }
            return steps;
        }

        /// <summary>
        /// Command line, after <c>-signals</c>, through the handlers the buttons call: <c>-signalWizard</c> opens the assistant
        /// (with <c>-signalWizardLeft on|off</c>, <c>-signalWizardGreen s</c>, <c>-signalWizardNoHeads</c>), <c>-signalWizardUse</c>
        /// clicks Use (twice where it asks to replace groups: give it twice), <c>-signalHeadsPlace</c> places heads for groups
        /// without, <c>-signalHeadGroup id g</c> sets head id to group g (from 1, 0 for none), <c>-signalPhaseGreen n s</c> and
        /// <c>-signalPhaseToggle n g</c> change phase n (from 1) on the phases tab. Logs "Signal wizard:" and "Signal heads:".
        /// </summary>
        private void RunSignalWizardArguments(string[] args)
        {
            int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : int.MinValue;
            if (Array.IndexOf(args, "-signalWizard") >= 0)
            {
                var left = Array.IndexOf(args, "-signalWizardLeft");
                if (left >= 0 && left + 1 < args.Length)
                    signalWizardOptions.SeparateLeftTurns = !string.Equals(args[left + 1], "off", StringComparison.OrdinalIgnoreCase);
                var green = Array.IndexOf(args, "-signalWizardGreen");
                if (green >= 0 && green + 1 < args.Length && TrySignalSeconds(args[green + 1], out var seconds) && seconds > 0)
                    signalWizardOptions.GreenSeconds = seconds;
                signalWizardLeftToggle?.SetValueWithoutNotify(signalWizardOptions.SeparateLeftTurns);
                OpenSignalWizard();
                if (Array.IndexOf(args, "-signalWizardNoHeads") >= 0)
                    signalWizardHeadsToggle.value = false;
                // -signalWizardMast none|<text>: no mast, or the first mast whose file matches the text (searched in the installation too).
                var mastArgument = Array.IndexOf(args, "-signalWizardMast");
                if (mastArgument >= 0 && mastArgument + 1 < args.Length)
                {
                    if (string.Equals(args[mastArgument + 1], "none", StringComparison.OrdinalIgnoreCase))
                        signalWizardMastType.index = 0;
                    else
                        signalWizardMastSearch.value = args[mastArgument + 1];
                }
                if (signalProposal != null)
                    Debug.Log("Signal wizard: " + signalProposal.Groups.Count + " groups (" + string.Join(", ", signalProposal.Groups.Select(group => group.Name + " " + group.Movements.Count)) +
                              "), " + signalProposal.Plan.Phases.Count + " phases, cycle " + Seconds(signalProposal.Plan.CycleSeconds) + ", conflict-free " +
                              signalProposal.ConflictFree + ", hard " + signalProposal.HardClashes.Count + ", permitted " + signalProposal.PermittedClashes.Count +
                              ", left lanes " + signalProposal.LeftLanes + ", head types " + signalHeadTypes.Count + " (" +
                              (signalWizardHeadType.index >= 0 && signalWizardHeadType.index < signalHeadTypes.Count ? signalHeadTypes[signalWizardHeadType.index] : "-") + "), mast " +
                              (ChosenSignalMast() != null ? ChosenSignalMast().AssetPath + " drop " + ChosenSignalMast().Drop.ToString("0.00", CultureInfo.InvariantCulture) +
                                                           " turn " + ChosenSignalMast().Turn.ToString("0", CultureInfo.InvariantCulture) : "none"));
                for (var index = 0; index < args.Length; index++)
                    if (args[index] == "-signalWizardUse" && signalTab == SignalTab.Wizard)
                        UseSignalProposal();
            }
            if (Array.IndexOf(args, "-signalHeadsPlace") >= 0 && signalCrossing != null)
            {
                var program = SignalsEditable && signalDraft != null ? signalDraft : signalProgram;
                var heads = SignalHeadsOfCrossing();
                PlaceMissingSignalHeads(Enumerable.Range(0, program.Lights.Count).Where(light => heads.All(head => (head.LightIndex ?? 0) != light)).ToList());
            }
            for (var index = 0; index + 2 < args.Length; index++)
                if (args[index] == "-signalHeadGroup" && signalCrossing != null)
                {
                    var id = Number(args[index + 1]);
                    var group = Number(args[index + 2]);
                    var head = SignalHeadsOfCrossing().Find(candidate => candidate.Id == id);
                    if (head != null && group >= 0)
                        RelabelSignalHead(head, group - 1);
                }
            for (var index = 0; index + 2 < args.Length; index++)
            {
                if ((args[index] != "-signalPhaseGreen" && args[index] != "-signalPhaseToggle") || signalDraft == null)
                    continue;
                var plan = OmsiSignalPhasePlan.Detect(signalDraft);
                var phase = Number(args[index + 1]) - 1;
                if (plan == null || phase < 0 || phase >= plan.Phases.Count)
                    continue;
                if (args[index] == "-signalPhaseGreen" && TrySignalSeconds(args[index + 2], out var green) && green > 0)
                    EditSignalPlan(plan, changed => changed.Phases[phase].GreenSeconds = green);
                else if (args[index] == "-signalPhaseToggle" && Number(args[index + 2]) >= 1)
                    ToggleSignalPhaseGroup(plan, phase, Number(args[index + 2]) - 1);
            }
            if (signalCrossing != null)
                foreach (var head in SignalHeadsOfCrossing())
                    Debug.Log("Signal heads: #" + head.Id + " " + head.AssetPath + " light " + (head.LightIndex.HasValue ? head.LightIndex.Value.ToString(CultureInfo.InvariantCulture) : "-") +
                              (head.Object != null ? " at " + head.Object.LocalX.ToString("0.0", CultureInfo.InvariantCulture) + " " +
                                                     head.Object.LocalY.ToString("0.0", CultureInfo.InvariantCulture) + " " +
                                                     head.Object.Height.ToString("0.0", CultureInfo.InvariantCulture) + " heading " +
                                                     head.Object.HeadingDegrees.ToString("0", CultureInfo.InvariantCulture) : string.Empty));
        }

        /// <summary>
        /// The mast field: no mast, the masts standing under heads in this map (the most used first, chosen at first), then
        /// objects of the installation matching the search text.
        /// </summary>
        private void FillSignalMastTypes(bool searched)
        {
            if (signalWizardMastType == null || currentSummary == null)
                return;
            var previous = signalWizardMastType.index >= 0 && signalWizardMastType.index < signalMastChoices.Count ? signalMastChoices[signalWizardMastType.index] : null;
            var hadChoice = signalWizardMastType.index >= 0;
            var usage = SignalUsageOfMap();
            var found = usage.Masts;
            signalMastChoices = new List<string> { null };
            var names = new List<string> { Loc.T("signals.heads.mast_none") };
            foreach (var mast in found)
            {
                signalMastChoices.Add(mast.AssetPath);
                names.Add(usage.MapName == null
                    ? Loc.T("signals.heads.mast_used", ("name", Path.GetFileNameWithoutExtension(mast.AssetPath)), ("count", mast.Count))
                    : Loc.T("signals.heads.mast_used_in", ("name", Path.GetFileNameWithoutExtension(mast.AssetPath)), ("map", usage.MapName), ("count", mast.Count)));
            }
            var search = (signalWizardMastSearch.value ?? string.Empty).Trim();
            if (search.Length > 0 && installationCatalog != null)
                foreach (var path in installationCatalog.Objects.Where(path => path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                                               !signalMastChoices.Contains(path, StringComparer.OrdinalIgnoreCase)).Take(40))
                {
                    signalMastChoices.Add(path);
                    names.Add(path);
                }
            signalWizardMastType.choices = names;
            var index = searched && signalMastChoices.Count > found.Count + 1 ? found.Count + 1
                : previous != null ? signalMastChoices.FindIndex(path => string.Equals(path, previous, StringComparison.OrdinalIgnoreCase))
                : hadChoice ? 0 : found.Count > 0 ? 1 : 0;
            signalWizardMastType.SetValueWithoutNotify(names[Math.Max(0, index)]);
            signalWizardMastType.index = Math.Max(0, index);
        }

        private SignalMastType ChosenSignalMast()
        {
            if (signalWizardMastType == null || signalWizardMastType.index <= 0 || signalWizardMastType.index >= signalMastChoices.Count)
                return null;
            var path = signalMastChoices[signalWizardMastType.index];
            return SignalUsageOfMap().Masts.Find(mast => string.Equals(mast.AssetPath, path, StringComparison.OrdinalIgnoreCase)) ??
                   new SignalMastType { AssetPath = path };
        }

        private List<string> SignalHeadTypesOfInstallation(string root)
        {
            if (!HeadTypesByRoot.TryGetValue(root ?? string.Empty, out var heads))
            {
                heads = FindSignalHeadTypes(root);
                HeadTypesByRoot[root ?? string.Empty] = heads;
            }
            return heads;
        }

        /// <summary>The heads and masts of the open map, or of another map of the installation when the open one has no heads.</summary>
        private SignalUsage SignalUsageOfMap()
        {
            if (signalUsage != null && ReferenceEquals(signalUsageOf, currentSummary))
                return signalUsage;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var isHead = new HashSet<string>(SignalHeadTypesOfInstallation(root), StringComparer.OrdinalIgnoreCase);
            var tiles = currentSummary.Tiles.Select(tile => tile.Objects.Where(item => item.AssetPath != null)
                .Select(item => (item.AssetPath, item.LocalX, item.LocalY, item.Height, item.HeadingDegrees)).ToList());
            var usage = UsageOf(tiles, isHead);
            foreach (var tile in currentSummary.Tiles)
                foreach (var item in tile.AttachedObjects)
                    if (item.AssetPath != null && isHead.Contains(item.AssetPath))
                        usage.Heads[item.AssetPath] = usage.Heads.TryGetValue(item.AssetPath, out var count) ? count + 1 : 1;
            if (usage.Heads.Count == 0 && root != null)
            {
                if (!ReferenceUsageByRoot.TryGetValue(root, out var reference))
                {
                    reference = ReferenceUsage(root, currentSummary.RootPath, isHead);
                    ReferenceUsageByRoot[root] = reference;
                }
                if (reference != null)
                    usage = reference;
            }
            signalUsage = usage;
            signalUsageOf = currentSummary;
            return usage;
        }

        /// <summary>
        /// Heads counted by type, and the objects standing right under them (within 0.8 m across, at least 1 m lower, not
        /// heads themselves): the masts they hang on, with how they stand to them, the lowest (the mast itself, not a sign
        /// on it) first among the common ones.
        /// </summary>
        private static SignalUsage UsageOf(IEnumerable<List<(string Path, double X, double Y, double Z, double Heading)>> tiles, HashSet<string> isHead)
        {
            var usage = new SignalUsage();
            var pairs = new Dictionary<string, List<(double Drop, double Turn)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in tiles)
            {
                var headObjects = tile.Where(item => isHead.Contains(item.Path)).ToList();
                foreach (var head in headObjects)
                    usage.Heads[head.Path] = usage.Heads.TryGetValue(head.Path, out var count) ? count + 1 : 1;
                if (headObjects.Count == 0)
                    continue;
                foreach (var item in tile)
                {
                    if (isHead.Contains(item.Path))
                        continue;
                    foreach (var head in headObjects)
                    {
                        var dx = item.X - head.X;
                        var dy = item.Y - head.Y;
                        // A head more than MaxMastDrop above the thing beside it is no pair: such heads (bus heads at absolute heights in
                        // a hilly map) would give every mast a drop of tens of metres.
                        if (dx * dx + dy * dy > 0.64 || item.Z > head.Z - 1.0 || head.Z - item.Z > MaxMastDrop)
                            continue;
                        if (!pairs.TryGetValue(item.Path, out var list))
                            pairs[item.Path] = list = new List<(double, double)>();
                        list.Add((head.Z - item.Z, OmsiRoadPlanner.Normalize(item.Heading - head.Heading)));
                        break;
                    }
                }
            }
            double Median(IEnumerable<double> values)
            {
                var sorted = values.OrderBy(value => value).ToList();
                return sorted.Count == 0 ? 0.0 : sorted[sorted.Count / 2];
            }
            // Signs hang on the same masts, a little higher: the mast is what stands lowest, on the pavement. Types found under
            // only a few heads (a third of the most common) are left out, then the lowest comes first.
            var found = pairs.Select(pair => new SignalMastType
            {
                AssetPath = pair.Key, Count = pair.Value.Count, Drop = Median(pair.Value.Select(value => value.Drop)), Turn = Median(pair.Value.Select(value => value.Turn))
            }).ToList();
            var common = found.Count > 0 ? found.Max(mast => mast.Count) / 3 : 0;
            usage.Masts = found.Where(mast => mast.Count >= Math.Max(1, common)).OrderByDescending(mast => mast.Drop).ThenByDescending(mast => mast.Count)
                .ThenBy(mast => mast.AssetPath, StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            return usage;
        }

        /// <summary>
        /// The usage of the smallest other map of the installation with heads, read straight from its tile files; null when
        /// none has any. Only the object records are read: path, position, height and heading.
        /// </summary>
        private static SignalUsage ReferenceUsage(string root, string openMap, HashSet<string> isHead)
        {
            var maps = Path.Combine(root, "maps");
            if (!Directory.Exists(maps))
                return null;
            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            var open = Path.GetFullPath(openMap ?? string.Empty).TrimEnd(separators);
            var folders = Directory.EnumerateDirectories(maps)
                .Where(folder => !string.Equals(Path.GetFullPath(folder).TrimEnd(separators), open, StringComparison.OrdinalIgnoreCase))
                .Select(folder => (Folder: folder, Tiles: Directory.GetFiles(folder, "tile_*.map")))
                .Where(map => map.Tiles.Length > 0).OrderBy(map => map.Tiles.Length).ToList();
            foreach (var map in folders)
            {
                var tiles = new List<List<(string Path, double X, double Y, double Z, double Heading)>>();
                foreach (var file in map.Tiles)
                {
                    string[] lines;
                    try
                    {
                        lines = OmsiTextFile.Decode(File.ReadAllBytes(file)).Replace("\r", string.Empty).Split('\n');
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        continue;
                    }
                    var objects = new List<(string Path, double X, double Y, double Z, double Heading)>();
                    for (var index = 0; index + 7 < lines.Length; index++)
                    {
                        if (lines[index].Trim() != "[object]")
                            continue;
                        if (double.TryParse(lines[index + 4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                            double.TryParse(lines[index + 5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                            double.TryParse(lines[index + 6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z) &&
                            double.TryParse(lines[index + 7].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var heading))
                            objects.Add((lines[index + 2].Trim(), x, y, z, heading));
                    }
                    tiles.Add(objects);
                }
                var usage = UsageOf(tiles, isHead);
                if (usage.Heads.Count > 0)
                {
                    usage.MapName = Path.GetFileName(map.Folder);
                    Debug.Log("Signals: heads and masts learnt from map " + usage.MapName + " (" + usage.Heads.Values.Sum() + " heads)");
                    return usage;
                }
            }
            return null;
        }

        /// <summary>The heads in the map that show the crossing shown, by its variable parent.</summary>
        private List<OmsiSignalHead> SignalHeadsOfCrossing()
        {
            if (signalJunction == null || signalTile == null || currentSummary == null)
                return new List<OmsiSignalHead>();
            return OmsiSignalHead.Of(currentSummary, signalTile, signalJunction.Id, path => PrioritySceneryObject(path)?.IsTrafficLight == true);
        }

        /// <summary>The head files of the installation: the most used in the map (or the map learnt from) first, then the rest by name.</summary>
        private void FillSignalHeadTypes(DropdownField field, bool forLeft)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (!HeadTypesByRoot.TryGetValue(root ?? string.Empty, out var all))
            {
                all = FindSignalHeadTypes(root);
                HeadTypesByRoot[root ?? string.Empty] = all;
            }
            // Ranked by use in this map, or in the map the assistant learns from when this one has no heads.
            var used = SignalUsageOfMap().Heads;
            signalHeadTypes = all.OrderByDescending(path => used.TryGetValue(path, out var count) ? count : 0).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            var previous = field.index >= 0 && field.index < field.choices.Count ? field.value : null;
            field.choices = signalHeadTypes.Select(path => Path.GetFileNameWithoutExtension(path)).ToList();
            if (signalHeadTypes.Count == 0)
                return;
            if (previous != null && field.choices.Contains(previous))
                return;
            // A car head by default: one whose script is OMSI's car head script; a left-arrow head for left turns when there is one.
            var best = signalHeadTypes.FindIndex(path => HeadScript(root, path, forLeft));
            if (best < 0 && forLeft)
                best = signalHeadTypes.FindIndex(path => HeadScript(root, path, false));
            field.index = Math.Max(0, best);
        }

        private static readonly Dictionary<string, string> HeadTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True for a head running the car head script (ampel1), with a left arrow when asked (an "L" mesh, as OMSI's KfzL heads).</summary>
        private static bool HeadScript(string root, string relative, bool leftArrow)
        {
            if (root == null)
                return false;
            if (!HeadTexts.TryGetValue(relative, out var text))
            {
                try
                {
                    text = OmsiTextFile.Decode(File.ReadAllBytes(Path.Combine(root, relative)));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
                {
                    text = string.Empty;
                }
                HeadTexts[relative] = text;
            }
            if (text.IndexOf("ampel1", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return leftArrow == (text.IndexOf("_L_", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>True for a head running the pedestrian head script (ampel2): red and green only.</summary>
        private static bool PedestrianHeadScript(string root, string relative)
        {
            if (root == null)
                return false;
            if (!HeadTexts.TryGetValue(relative, out var text))
            {
                HeadScript(root, relative, false);
                HeadTexts.TryGetValue(relative, out text);
            }
            return text != null && text.IndexOf("ampel2", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The pedestrian head to place: the one most used in the map (or the map learnt from), else the first of the installation; null without any.</summary>
        private string PedestrianHeadType()
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (!HeadTypesByRoot.TryGetValue(root ?? string.Empty, out var all))
            {
                all = FindSignalHeadTypes(root);
                HeadTypesByRoot[root ?? string.Empty] = all;
            }
            var used = SignalUsageOfMap().Heads;
            return all.Where(path => PedestrianHeadScript(root, path))
                .OrderByDescending(path => used.TryGetValue(path, out var count) ? count : 0).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        /// <summary>Pedestrian heads stand lower than car heads, at about eye height of a walker looking up.</summary>
        private const double PedestrianHeadHeight = 2.3;

        private static List<string> FindSignalHeadTypes(string root)
        {
            var found = new List<string>();
            if (root == null)
                return found;
            var folder = Path.Combine(root, "Sceneryobjects");
            if (!Directory.Exists(folder))
                return found;
            var marker = Encoding.ASCII.GetBytes("[trafficlight]");
            foreach (var file in Directory.EnumerateFiles(folder, "*.sco", SearchOption.AllDirectories))
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(file);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    continue;
                }
                if (!Contains(bytes, marker))
                    continue;
                var relative = file.Substring(root.Length).TrimStart('\\', '/').Replace('/', '\\');
                try
                {
                    if (OmsiSceneryObject.Parse(bytes).IsTrafficLight)
                        found.Add(relative);
                }
                catch (Exception exception) when (exception is FormatException || exception is ArgumentException)
                {
                    Debug.LogWarning("Signals: " + relative + " could not be read as a head: " + exception.Message);
                }
            }
            return found;
        }

        private static bool Contains(byte[] bytes, byte[] marker)
        {
            for (var index = 0; index + marker.Length <= bytes.Length; index++)
            {
                var match = true;
                for (var offset = 0; offset < marker.Length && match; offset++)
                    match = bytes[index + offset] == marker[offset];
                if (match)
                    return true;
            }
            return false;
        }

        /// <summary>The heads section of the movements page: each head with its group, changeable where the map can be edited.</summary>
        private void RenderSignalHeads()
        {
            if (signalHeadsBox == null)
                return;
            signalHeadsBox.Clear();
            if (signalCrossing == null)
                return;
            var heads = SignalHeadsOfCrossing();
            var program = SignalsEditable && signalDraft != null ? signalDraft : signalProgram;
            if (heads.Count == 0)
            {
                var none = new Label(Loc.T(program.Lights.Count > 0 ? "signals.heads.none_warning" : "signals.heads.none")); // i18n:keys
                none.AddToClassList("panel-info");
                if (program.Lights.Count > 0)
                    none.AddToClassList("signal-shared-hint");
                signalHeadsBox.Add(none);
            }
            var choices = new List<string> { Loc.T("signals.heads.group_none") };
            choices.AddRange(program.Lights.Select(light => light.Name));
            foreach (var head in heads.OrderBy(head => head.LightIndex ?? 0).ThenBy(head => head.Id))
            {
                var row = new VisualElement();
                row.AddToClassList("signal-movement-row");
                var chip = new VisualElement();
                chip.AddToClassList("signal-group-chip");
                var light = head.LightIndex ?? 0;
                chip.style.backgroundColor = light < program.Lights.Count ? SignalGroupColour(light) : new Color(0.5f, 0.5f, 0.5f);
                row.Add(chip);
                var text = new Label(Loc.T("signals.heads.row", ("id", head.Id), ("type", Path.GetFileNameWithoutExtension(head.AssetPath))));
                text.AddToClassList("signal-movement-text");
                row.Add(text);
                var dropdown = new DropdownField(choices, light < program.Lights.Count ? light + 1 : 0);
                dropdown.AddToClassList("signal-movement-group");
                dropdown.SetEnabled(CanEdit && session != null);
                LocBindings.Tooltip(dropdown, "signals.heads.group.tooltip");
                var shown = head;
                dropdown.RegisterValueChangedCallback(_ => RelabelSignalHead(shown, dropdown.index - 1));
                row.Add(dropdown);
                signalHeadsBox.Add(row);
            }
            // Heads for groups without any, on a junction made here.
            if (SignalsEditable && program.Lights.Count > 0)
            {
                var missing = Enumerable.Range(0, program.Lights.Count).Where(light => heads.All(head => (head.LightIndex ?? 0) != light) &&
                    signalCrossing.Movements.Any(movement => movement.Light == light && movement.Approach >= 0)).ToList();
                if (missing.Count > 0)
                {
                    var button = LocBindings.WithTooltip(LocBindings.NewButton("signals.heads.place", () => PlaceMissingSignalHeads(missing)), "signals.heads.place.tooltip");
                    button.AddToClassList("map-control-button");
                    signalHeadsBox.Add(button);
                    // What the button places with: the head, mast and height last set in the assistant.
                    FillSignalHeadTypes(signalWizardHeadType, false);
                    FillSignalMastTypes(false);
                    var head = signalWizardHeadType.index >= 0 && signalWizardHeadType.index < signalHeadTypes.Count
                        ? Path.GetFileNameWithoutExtension(signalHeadTypes[signalWizardHeadType.index]) : "-";
                    var mast = ChosenSignalMast();
                    signalHeadsPlaceWith = new Label(Loc.T("signals.heads.place_with", ("head", head),
                        ("mast", mast != null ? Path.GetFileNameWithoutExtension(mast.AssetPath) : Loc.T("signals.heads.mast_none")),
                        ("height", signalWizardHeadHeight.value)));
                    signalHeadsPlaceWith.AddToClassList("panel-info");
                    signalHeadsBox.Add(signalHeadsPlaceWith);
                }
            }
        }

        /// <summary>A head shows another group (-1: none, its label taken away so it follows light 0 as openOMSI does without one); one undo step.</summary>
        private void RelabelSignalHead(OmsiSignalHead head, int light)
        {
            if (!CanEdit || session == null)
            {
                SetStatus(ReadOnlyMessage("readonly.action.edit_signals"));
                RenderSignalHeads();
                return;
            }
            var element = (object)head.Object ?? head.Attached;
            var labels = (head.Object?.Labels ?? head.Attached.Labels).ToList();
            if (labels.Count == 0)
                labels.Add(string.Empty);
            labels[0] = light >= 0 ? light.ToString(CultureInfo.InvariantCulture) : string.Empty;
            OmsiObjectChange change;
            try
            {
                change = session.SetElementLabels(head.Tile, element, labels);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus(Loc.T("signals.heads.status.failed", ("id", head.Id), ("reason", exception.Message)));
                RenderSignalHeads();
                return;
            }
            if (change == null)
                return;
            var program = SignalsEditable && signalDraft != null ? signalDraft : signalProgram;
            PushSteps(new List<object> { change }, Loc.T("signals.heads.step", ("id", head.Id)));
            RefreshSignalTarget();
            SetStatus(Loc.T("signals.heads.done", ("id", head.Id), ("name", light >= 0 && light < program.Lights.Count ? program.Lights[light].Name : "-")));
        }

        private void PlaceMissingSignalHeads(List<int> lights)
        {
            if (!SignalsEditable)
                return;
            FillSignalHeadTypes(signalWizardHeadType, false);
            FillSignalHeadTypes(signalWizardLeftHeadType, true);
            if (signalHeadTypes.Count == 0)
            {
                SetStatusT("signals.heads.status.no_types");
                return;
            }
            var type = signalHeadTypes[Math.Max(0, signalWizardHeadType.index)];
            var leftType = signalHeadTypes[Math.Max(0, signalWizardLeftHeadType.index)];
            var height = TrySignalSeconds(signalWizardHeadHeight.value, out var metres) ? metres : DefaultHeadHeight;
            FillSignalMastTypes(false);
            var mast = ChosenSignalMast();
            var movementLights = signalCrossing.Movements.Select(movement => movement.Light).ToList();
            // A group whose movements all turn left from lanes of their own gets its head on the left.
            var turnLanes = OmsiSignalWizard.TurnLaneMovements(signalCrossing, signalLayout);
            var lefts = new HashSet<int>(lights.Where(light => signalCrossing.Movements.Where(movement => movement.Light == light).All(movement => turnLanes.Contains(movement.Index))));
            var steps = PlaceSignalHeads(signalCrossing, movementLights, lefts, lights, type, leftType, height, mast);
            if (steps.Count == 0)
            {
                SetStatusT("signals.heads.status.place_failed", ("count", signalHeadFailures));
                RefreshAfterChange(null);
                return;
            }
            PushSteps(steps, Loc.T("signals.heads.step_place", ("id", signalJunction.Id)));
            RefreshSignalTarget();
            SetStatus(Loc.T("signals.heads.done_place", ("count", steps.Count(step => step is OmsiObjectChange change && change.Kind == OmsiObjectChangeKind.Placement)),
                ("id", signalJunction.Id)));
            if (signalHeadFailures > 0)
                SetStatusT("signals.heads.status.place_failed", ("count", signalHeadFailures));
        }
    }
}
