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
    /// The check tab and the preview of the traffic signals tool (ADR 085, milestone 5; §10, §11 and §13 of the brief). The
    /// check shows the groups × groups conflict matrix with manual exceptions (on junctions made here, kept in the .sco) and
    /// the findings of <see cref="OmsiSignalCheck"/>, with the conflicts it found marked on the map. The preview runs the
    /// program as openOMSI does (<see cref="OmsiSignalClock"/>) without touching a file: play, pause, restart, steps, a
    /// second to jump to, speed, loop and requests; the movements and the heads on the map, in 2D and 3D, and the timeline
    /// follow it, and changes of the draft show at once.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private static readonly Color SignalRed = new Color(0.86f, 0.24f, 0.24f);
        private static readonly Color SignalYellow = new Color(0.96f, 0.76f, 0.20f);
        private static readonly Color SignalGreen = new Color(0.25f, 0.73f, 0.36f);
        private static readonly Color SignalDark = new Color(0.45f, 0.48f, 0.53f);
        private static readonly float[] SignalPreviewSpeeds = { 0.5f, 1f, 2f, 5f, 10f };

        private Button signalCheckTab;
        private VisualElement signalCheckPage;
        private Label signalCheckSummary;
        private VisualElement signalMatrixBox;
        private VisualElement signalFindingsBox;
        private List<OmsiSignalFinding> signalFindings = new List<OmsiSignalFinding>();

        private VisualElement signalPreviewBar;
        private Button signalPreviewPlayButton;
        private TextField signalPreviewTimeField;
        private DropdownField signalPreviewSpeedField;
        private Toggle signalPreviewLoopToggle;
        private VisualElement signalPreviewRequests;
        private OmsiSignalClock signalClock;
        private OmsiSignalProgram signalClockProgram;
        private bool signalPreviewPlaying;
        private float signalPreviewSpeed = 1f;
        private double signalPreviewRun;
        private bool signalPreviewLog;
        private int signalPreviewLoggedSecond = -1;

        /// <summary>The heads of the crossing shown with where they stand, for the map; found when the crossing is.</summary>
        private List<(OmsiSignalHead Head, OmsiMapPoint At, double Height)> signalHeadPlaces = new List<(OmsiSignalHead, OmsiMapPoint, double)>();

        private bool SignalPreviewActive => signalClock != null;

        private Button CreateSignalCheckTab()
        {
            signalCheckTab = LocBindings.NewButton("signals.tab.check", () => ShowSignalTab(SignalTab.Check));
            signalCheckTab.AddToClassList("panel-tab");
            return signalCheckTab;
        }

        private VisualElement CreateSignalCheckPage()
        {
            signalCheckPage = new ScrollView(ScrollViewMode.Vertical);
            signalCheckPage.AddToClassList("signal-scroll-page");
            signalCheckSummary = new Label();
            signalCheckSummary.AddToClassList("panel-info");
            signalCheckPage.Add(signalCheckSummary);
            var matrixTitle = LocBindings.NewLabel("signals.check.matrix");
            matrixTitle.AddToClassList("inspector-section-heading");
            signalCheckPage.Add(matrixTitle);
            var matrixScroll = new ScrollView(ScrollViewMode.Horizontal);
            matrixScroll.AddToClassList("signal-matrix-scroll");
            signalMatrixBox = new VisualElement();
            signalMatrixBox.AddToClassList("signal-matrix");
            matrixScroll.Add(signalMatrixBox);
            signalCheckPage.Add(matrixScroll);
            var legend = new VisualElement();
            legend.AddToClassList("signal-phase-groups");
            foreach (var relation in new[] { OmsiGroupRelation.Compatible, OmsiGroupRelation.Conditional, OmsiGroupRelation.Incompatible, OmsiGroupRelation.NotChecked })
            {
                var item = new VisualElement();
                item.AddToClassList("signal-group-row");
                var swatch = new VisualElement();
                swatch.AddToClassList("signal-matrix-swatch");
                swatch.style.backgroundColor = RelationColour(relation);
                item.Add(swatch);
                var name = new Label(RelationName(relation));
                name.AddToClassList("signal-movement-text");
                item.Add(name);
                legend.Add(item);
            }
            signalCheckPage.Add(legend);
            var findingsTitle = LocBindings.NewLabel("signals.check.findings");
            findingsTitle.AddToClassList("inspector-section-heading");
            signalCheckPage.Add(findingsTitle);
            signalFindingsBox = new VisualElement();
            signalCheckPage.Add(signalFindingsBox);
            signalCheckPage.style.display = DisplayStyle.None;
            return signalCheckPage;
        }

        private static Color RelationColour(OmsiGroupRelation relation)
        {
            switch (relation)
            {
                case OmsiGroupRelation.Compatible: return new Color(0.25f, 0.62f, 0.36f);
                case OmsiGroupRelation.Conditional: return new Color(0.85f, 0.66f, 0.18f);
                case OmsiGroupRelation.Incompatible: return new Color(0.78f, 0.24f, 0.24f);
                default: return new Color(0.36f, 0.39f, 0.44f);
            }
        }

        private static string RelationName(OmsiGroupRelation relation)
        {
            switch (relation)
            {
                case OmsiGroupRelation.Compatible: return Loc.T("signals.matrix.compatible");
                case OmsiGroupRelation.Conditional: return Loc.T("signals.matrix.conditional");
                case OmsiGroupRelation.Incompatible: return Loc.T("signals.matrix.incompatible");
                default: return Loc.T("signals.matrix.unchecked");
            }
        }

        /// <summary>The manual exceptions of the junction shown: those of a junction made here, none elsewhere.</summary>
        private HashSet<(int, int)> SignalExceptions()
        {
            return signalMade?.Signals != null ? new HashSet<(int, int)>(signalMade.Signals.Exceptions) : new HashSet<(int, int)>();
        }

        private List<int> SignalMovementLights() => signalCrossing.Movements.Select(movement => movement.Light).ToList();

        /// <summary>Runs the check on the program shown (the draft on a junction made here) and shows matrix, findings and the conflicts on the map.</summary>
        private void RenderSignalCheck()
        {
            if (signalCheckPage == null)
                return;
            signalMatrixBox.Clear();
            signalFindingsBox.Clear();
            signalFindings = new List<OmsiSignalFinding>();
            var program = ShownSignalProgram;
            if (signalCrossing == null || program == null || program.Lights.Count == 0)
            {
                signalCheckSummary.text = Loc.T("signals.check.nothing");
                UpdateSignalMarkers();
                return;
            }
            var conflicts = SignalConflicts();
            var lights = SignalMovementLights();
            var exceptions = SignalExceptions();
            var heads = SignalHeadsOfCrossing().Select(head => head.LightIndex).ToList();
            signalFindings = OmsiSignalCheck.Run(program, signalCrossing, conflicts, lights, exceptions, heads, PedestrianSignalLights());
            var errors = signalFindings.Count(finding => finding.Level == OmsiSignalIssueLevel.Error);
            var warnings = signalFindings.Count(finding => finding.Level == OmsiSignalIssueLevel.Warning);
            var hints = signalFindings.Count(finding => finding.Level == OmsiSignalIssueLevel.Hint);
            signalCheckSummary.text = Loc.T(errors + warnings == 0 ? "signals.check.summary_clean" : "signals.check.summary", ("errors", errors), // i18n:keys
                ("warnings", warnings), ("hints", hints), ("conflicts", conflicts.Count)) +
                (exceptions.Count > 0 ? "\n" + Loc.T("signals.check.exceptions_note", ("count", exceptions.Count)) : string.Empty);

            // The matrix: a row and a column per group, the cells coloured by how they get on; manual exceptions marked M.
            var matrix = OmsiSignalMatrix.Build(program.Lights.Count, conflicts, lights);
            var editable = SignalsEditable;
            var header = new VisualElement();
            header.AddToClassList("signal-matrix-row");
            header.Add(MatrixCorner());
            for (var column = 0; column < program.Lights.Count; column++)
                header.Add(MatrixHead(column, program));
            signalMatrixBox.Add(header);
            for (var row = 0; row < program.Lights.Count; row++)
            {
                var line = new VisualElement();
                line.AddToClassList("signal-matrix-row");
                line.Add(MatrixHead(row, program));
                for (var column = 0; column < program.Lights.Count; column++)
                {
                    var cell = new Button();
                    cell.AddToClassList("signal-matrix-cell");
                    if (row == column)
                    {
                        cell.AddToClassList("signal-matrix-self");
                        cell.SetEnabled(false);
                        line.Add(cell);
                        continue;
                    }
                    var relation = matrix[row, column];
                    var pair = OmsiJunctionSignals.Pair(row, column);
                    var excepted = exceptions.Contains(pair);
                    cell.style.backgroundColor = RelationColour(relation);
                    if (excepted)
                    {
                        cell.text = "M";
                        cell.AddToClassList("signal-matrix-manual");
                    }
                    var together = signalFindings.Where(finding => finding.First == pair.Item1 && finding.Second == pair.Item2 &&
                                                                   (finding.Kind == OmsiSignalFindingKind.ConflictingGreen || finding.Kind == OmsiSignalFindingKind.ConditionalGreen ||
                                                                    finding.Kind == OmsiSignalFindingKind.AllowedByException)).Select(finding => finding.Amount).DefaultIfEmpty(0).Max();
                    cell.tooltip = Loc.T("signals.matrix.cell", ("first", program.Lights[row].Name), ("second", program.Lights[column].Name),
                        ("relation", RelationName(relation)), ("conflicts", matrix.Conflicts[row, column].Count), ("together", Seconds(together))) +
                        (excepted ? "\n" + Loc.T("signals.matrix.manual") : string.Empty) +
                        (editable && (relation == OmsiGroupRelation.Incompatible || excepted) ? "\n" + Loc.T(excepted ? "signals.matrix.click_remove" : "signals.matrix.click_add") : string.Empty); // i18n:keys
                    var first = pair.Item1;
                    var second = pair.Item2;
                    cell.clicked += () => ToggleSignalException(first, second);
                    cell.SetEnabled(editable && (relation == OmsiGroupRelation.Incompatible || excepted));
                    line.Add(cell);
                }
                signalMatrixBox.Add(line);
            }

            foreach (var finding in signalFindings.Take(40))
            {
                var label = new Label(DescribeSignalFinding(finding, program));
                label.AddToClassList("signal-finding");
                label.AddToClassList(finding.Level == OmsiSignalIssueLevel.Error ? "signal-finding-error"
                    : finding.Level == OmsiSignalIssueLevel.Warning ? "signal-finding-warning" : "signal-finding-hint");
                signalFindingsBox.Add(label);
            }
            if (signalFindings.Count > 40)
            {
                var more = new Label(Loc.T("signals.check.more", ("count", signalFindings.Count - 40)));
                more.AddToClassList("panel-info");
                signalFindingsBox.Add(more);
            }
            UpdateSignalMarkers();
        }

        private static VisualElement MatrixCorner()
        {
            var corner = new VisualElement();
            corner.AddToClassList("signal-matrix-head");
            return corner;
        }

        private VisualElement MatrixHead(int light, OmsiSignalProgram program)
        {
            var head = new Label((light + 1).ToString(CultureInfo.InvariantCulture));
            head.AddToClassList("signal-matrix-head");
            head.style.borderBottomColor = SignalGroupColour(light);
            head.tooltip = program.Lights[light].Name;
            return head;
        }

        private string DescribeSignalFinding(OmsiSignalFinding finding, OmsiSignalProgram program)
        {
            string Name(int light) => light >= 0 && light < program.Lights.Count ? program.Lights[light].Name : "#" + (light + 1).ToString(CultureInfo.InvariantCulture);
            var level = Loc.T(finding.Level == OmsiSignalIssueLevel.Error ? "signals.check.error" : finding.Level == OmsiSignalIssueLevel.Warning // i18n:keys
                ? "signals.check.warning" : "signals.check.hint");
            string text;
            switch (finding.Kind)
            {
                case OmsiSignalFindingKind.ConflictingGreen:
                    text = Loc.T(finding.Conflict != null && finding.Conflict.Kind == OmsiConflictKind.Crossing ? "signals.finding.crossing" // i18n:keys
                        : finding.Conflict != null && finding.Conflict.Kind == OmsiConflictKind.Merging ? "signals.finding.merging" : "signals.finding.pedestrian",
                        ("first", Name(finding.First)), ("second", Name(finding.Second)), ("seconds", Seconds(finding.Seconds)), ("length", Seconds(finding.Amount)));
                    break;
                case OmsiSignalFindingKind.ConditionalGreen:
                    text = Loc.T("signals.finding.conditional", ("first", Name(finding.First)), ("second", Name(finding.Second)));
                    break;
                case OmsiSignalFindingKind.WithinGroup:
                    text = Loc.T("signals.finding.within", ("group", Name(finding.First)));
                    break;
                case OmsiSignalFindingKind.AllowedByException:
                    text = Loc.T("signals.finding.exception", ("first", Name(finding.First)), ("second", Name(finding.Second)), ("length", Seconds(finding.Amount)));
                    break;
                case OmsiSignalFindingKind.ShortIntergreen:
                    text = Loc.T("signals.finding.intergreen", ("first", Name(finding.First)), ("second", Name(finding.Second)), ("seconds", Seconds(finding.Seconds)),
                        ("length", Seconds(finding.Amount)), ("least", Seconds(OmsiSignalCheck.ShortestIntergreen)));
                    break;
                case OmsiSignalFindingKind.HeadWithoutGroup:
                    text = Loc.T("signals.finding.head_group", ("count", (int)finding.Amount), ("n", finding.First + 1));
                    break;
                case OmsiSignalFindingKind.GroupWithoutHead:
                    text = Loc.T("signals.finding.no_head", ("group", Name(finding.First)));
                    break;
                case OmsiSignalFindingKind.NoHeads:
                    text = Loc.T("signals.heads.none_warning");
                    break;
                case OmsiSignalFindingKind.UnboundMovements:
                    text = Loc.T("signals.finding.unbound", ("count", (int)finding.Amount));
                    break;
                case OmsiSignalFindingKind.DarkPhase:
                    text = Loc.T("signals.finding.dark", ("group", Name(finding.First)), ("seconds", Seconds(finding.Seconds)));
                    break;
                default:
                    return DescribeSignalIssues(new List<OmsiSignalIssue> { finding.Timing }, program);
            }
            return level + ": " + text;
        }

        /// <summary>Allows (or no longer allows) two incompatible groups to go together; written into the junction as one undo step.</summary>
        private void ToggleSignalException(int first, int second)
        {
            if (!SignalsEditable)
                return;
            var signals = CurrentSignals();
            var pair = OmsiJunctionSignals.Pair(first, second);
            var added = !signals.Exceptions.Remove(pair);
            if (added)
                signals.Exceptions.Add(pair);
            var names = signals.Program.Lights;
            ApplySignals(signals, null, Loc.T(added ? "signals.done.exception_added" : "signals.done.exception_removed", // i18n:keys
                ("first", names[pair.Item1].Name), ("second", names[pair.Item2].Name), ("id", signalJunction.Id)));
            ShowSignalTab(SignalTab.Check);
        }

        // ----- heads on the map -----

        /// <summary>Where each head of the crossing stands, in world metres and height above sea; attached heads at their parent's attachment point.</summary>
        private void FindSignalHeadPlaces()
        {
            signalHeadPlaces = new List<(OmsiSignalHead, OmsiMapPoint, double)>();
            if (signalCrossing == null || currentSummary == null)
                return;
            var size = currentSummary.TileSizeMeters;
            var terrain = session != null ? new OmsiTerrainSampler(currentSummary, session.Source) : null;
            double Above(string assetPath, OmsiMapPoint at, double z)
            {
                var placed = PrioritySceneryObject(assetPath);
                if (placed != null && (placed.HasAbsoluteHeight || placed.HasSplineHelper))
                    return z;
                return terrain != null && terrain.TryGetHeight(at.X, at.Y, out var ground) ? ground + z : z;
            }
            foreach (var head in SignalHeadsOfCrossing())
            {
                if (head.Object != null)
                {
                    var at = OmsiMapGeometry.TileLocalToWorld(head.Tile.X, head.Tile.Y, head.Object.LocalX, head.Object.LocalY, size);
                    signalHeadPlaces.Add((head, at, Above(head.AssetPath, at, head.Object.Height)));
                    continue;
                }
                var parent = head.Tile.Objects.Find(item => item.Id == head.Attached.ParentId);
                var model = parent != null ? PrioritySceneryObject(parent.AssetPath) : null;
                if (parent == null || model == null || head.Attached.AttachPointIndex < 0 || head.Attached.AttachPointIndex >= model.AttachPoints.Count)
                    continue;
                var origin = OmsiMapGeometry.TileLocalToWorld(head.Tile.X, head.Tile.Y, parent.LocalX, parent.LocalY, size);
                var parentHeight = Above(parent.AssetPath, origin, parent.Height);
                var frame = Matrix4x4.TRS(new Vector3(0f, (float)parentHeight, 0f), Quaternion.Euler((float)parent.PitchDegrees, (float)parent.HeadingDegrees,
                    (float)parent.BankDegrees), Vector3.one) * SignalAttachMatrix(model.AttachPoints[head.Attached.AttachPointIndex]);
                var offset = frame.MultiplyPoint3x4(Vector3.zero);
                signalHeadPlaces.Add((head, new OmsiMapPoint(origin.X + offset.x, origin.Y + offset.z), offset.y));
            }
        }

        /// <summary>An attachment point as the object renderer applies it (x right, y forward, z up in OMSI; Unity's y is up).</summary>
        private static Matrix4x4 SignalAttachMatrix(OmsiAttachPoint point)
        {
            var matrix = Matrix4x4.identity;
            foreach (var step in point.Steps)
            {
                Matrix4x4 operation;
                switch (step.Kind)
                {
                    case "attach_trans":
                        operation = Matrix4x4.Translate(new Vector3((float)step.X, (float)step.Z, (float)step.Y));
                        break;
                    case "attach_rot_x":
                        operation = Matrix4x4.Rotate(Quaternion.Euler((float)step.X, 0f, 0f));
                        break;
                    case "attach_rot_y":
                        operation = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, (float)step.X));
                        break;
                    default:
                        operation = Matrix4x4.Rotate(Quaternion.Euler(0f, (float)step.X, 0f));
                        break;
                }
                matrix = operation * matrix;
            }
            return matrix;
        }

        /// <summary>The colour a signal state shows in the preview: red, yellow, green, or grey for dark.</summary>
        private static Color SignalStateColour(int state)
        {
            switch (OmsiSignalAspects.Of(state))
            {
                case OmsiSignalAspect.Red: return SignalRed;
                case OmsiSignalAspect.RedYellow:
                case OmsiSignalAspect.Yellow: return SignalYellow;
                case OmsiSignalAspect.Green:
                case OmsiSignalAspect.GreenYellow: return SignalGreen;
                default: return SignalDark;
            }
        }

        /// <summary>Heads (in their group's colour, or their state in the preview) and, on the check tab, where conflicting movements meet.</summary>
        private void UpdateSignalMarkers()
        {
            if (mapCanvas == null)
                return;
            if (activeTool != EditorTool.Signals || signalCrossing == null)
            {
                mapCanvas.SetSignalMarkers(null);
                return;
            }
            var markers = new List<(OmsiMapPoint, double, Color, MapCanvasElement.SignalMarkerKind)>();
            var program = ShownSignalProgram ?? signalProgram;
            foreach (var place in signalHeadPlaces)
            {
                var light = place.Head.LightIndex ?? 0;
                var colour = SignalPreviewActive ? SignalStateColour(program.StateAt(light, signalClock.Time))
                    : light < program.Lights.Count ? SignalGroupColour(light) : SignalDark;
                markers.Add((place.At, place.Height, colour, MapCanvasElement.SignalMarkerKind.Head));
            }
            if (signalTab == SignalTab.Check)
                foreach (var finding in signalFindings.Where(finding => finding.Conflict != null))
                {
                    var at = ObjectToWorld(signalTile, signalJunction, finding.Conflict.X, finding.Conflict.Y);
                    var kind = finding.Level == OmsiSignalIssueLevel.Hint ? MapCanvasElement.SignalMarkerKind.ConditionalConflict : MapCanvasElement.SignalMarkerKind.Conflict;
                    markers.Add((at, signalJunction.Height, kind == MapCanvasElement.SignalMarkerKind.Conflict ? SignalRed : SignalYellow, kind));
                }
            mapCanvas.SetSignalMarkers(markers);
        }

        // ----- preview -----

        private VisualElement CreateSignalPreviewBar()
        {
            signalPreviewBar = new VisualElement();
            signalPreviewBar.AddToClassList("signal-preview");
            var row = new VisualElement();
            row.AddToClassList("signal-group-row");
            signalPreviewPlayButton = LocBindings.WithTooltip(new Button(ToggleSignalPreview), "signals.preview.play.tooltip");
            DecorateButton(signalPreviewPlayButton, "play", null);
            signalPreviewPlayButton.AddToClassList("signal-preview-button");
            row.Add(signalPreviewPlayButton);
            var restart = LocBindings.WithTooltip(new Button(RestartSignalPreview), "signals.preview.restart.tooltip");
            DecorateButton(restart, "skip-back", null);
            restart.AddToClassList("signal-preview-button");
            row.Add(restart);
            var back = LocBindings.WithTooltip(new Button(() => StepSignalPreview(-1)), "signals.preview.back.tooltip");
            DecorateButton(back, "chevron-left", null);
            back.AddToClassList("signal-preview-button");
            row.Add(back);
            var forward = LocBindings.WithTooltip(new Button(() => StepSignalPreview(1)), "signals.preview.forward.tooltip");
            DecorateButton(forward, "chevron-right", null);
            forward.AddToClassList("signal-preview-button");
            row.Add(forward);
            signalPreviewTimeField = new TextField { isDelayed = true };
            signalPreviewTimeField.AddToClassList("picker-field");
            signalPreviewTimeField.AddToClassList("signal-preview-time");
            LocBindings.Tooltip(signalPreviewTimeField, "signals.preview.time.tooltip");
            signalPreviewTimeField.RegisterValueChangedCallback(evt =>
            {
                if (TrySignalSeconds(evt.newValue, out var seconds))
                    JumpSignalPreview(seconds);
                else
                    SetStatusT("signals.status.bad_seconds");
            });
            row.Add(signalPreviewTimeField);
            signalPreviewSpeedField = new DropdownField(SignalPreviewSpeeds.Select(speed => speed.ToString("0.#", CultureInfo.InvariantCulture) + "x").ToList(), 1);
            signalPreviewSpeedField.AddToClassList("signal-movement-group");
            signalPreviewSpeedField.AddToClassList("signal-preview-speed");
            LocBindings.Tooltip(signalPreviewSpeedField, "signals.preview.speed.tooltip");
            signalPreviewSpeedField.RegisterValueChangedCallback(_ => signalPreviewSpeed = SignalPreviewSpeeds[Math.Max(0, signalPreviewSpeedField.index)]);
            row.Add(signalPreviewSpeedField);
            signalPreviewBar.Add(row);
            var options = new VisualElement();
            options.AddToClassList("signal-phase-groups");
            signalPreviewLoopToggle = new Toggle { value = true };
            LocBindings.Text(signalPreviewLoopToggle, "signals.preview.loop");
            LocBindings.Tooltip(signalPreviewLoopToggle, "signals.preview.loop.tooltip");
            options.Add(signalPreviewLoopToggle);
            signalPreviewRequests = new VisualElement();
            signalPreviewRequests.AddToClassList("signal-phase-groups");
            options.Add(signalPreviewRequests);
            signalPreviewBar.Add(options);
            return signalPreviewBar;
        }

        /// <summary>The request switches: one for every group a stop or jump point waits for.</summary>
        private void RenderSignalPreviewRequests()
        {
            if (signalPreviewRequests == null)
                return;
            signalPreviewRequests.Clear();
            var program = ShownSignalProgram;
            if (program == null)
                return;
            foreach (var light in program.Holds.Select(hold => hold.Light).Where(light => light >= 0 && light < program.Lights.Count).Distinct().OrderBy(light => light))
            {
                var asked = light;
                var toggle = new Toggle(Loc.T("signals.preview.request", ("group", program.Lights[light].Name)))
                {
                    value = signalClock != null && light < signalClock.Requests.Length && signalClock.Requests[light]
                };
                toggle.tooltip = Loc.T("signals.preview.request.tooltip", ("group", program.Lights[light].Name));
                toggle.RegisterValueChangedCallback(evt =>
                {
                    EnsureSignalClock();
                    if (signalClock != null && asked < signalClock.Requests.Length)
                        signalClock.Requests[asked] = evt.newValue;
                    ShowSignalPreviewState();
                });
                signalPreviewRequests.Add(toggle);
            }
        }

        /// <summary>A clock for the program shown; a new one (at the same second, with the same requests) when the program was replaced.</summary>
        private void EnsureSignalClock()
        {
            var program = ShownSignalProgram;
            if (program == null || program.Lights.Count == 0)
            {
                StopSignalPreview();
                return;
            }
            if (signalClock != null && ReferenceEquals(signalClockProgram, program))
                return;
            var old = signalClock;
            signalClock = new OmsiSignalClock(program);
            signalClockProgram = program;
            if (old != null)
            {
                for (var light = 0; light < Math.Min(old.Requests.Length, signalClock.Requests.Length); light++)
                    signalClock.Requests[light] = old.Requests[light];
                signalClock.JumpTo(Math.Min(old.Time, program.CycleLength));
            }
        }

        private void ToggleSignalPreview()
        {
            EnsureSignalClock();
            if (signalClock == null)
                return;
            signalPreviewPlaying = !signalPreviewPlaying;
            signalPreviewRun = 0;
            ShowSignalPreviewState();
        }

        private void RestartSignalPreview()
        {
            EnsureSignalClock();
            if (signalClock == null)
                return;
            signalClock.JumpTo(0);
            signalPreviewRun = 0;
            ShowSignalPreviewState();
        }

        private void StepSignalPreview(int seconds)
        {
            EnsureSignalClock();
            if (signalClock == null)
                return;
            signalPreviewPlaying = false;
            if (seconds > 0)
                signalClock.Advance(seconds);
            else
                signalClock.JumpTo(signalClock.Time + seconds);
            ShowSignalPreviewState();
        }

        private void JumpSignalPreview(double seconds)
        {
            EnsureSignalClock();
            if (signalClock == null)
                return;
            signalClock.JumpTo(seconds);
            ShowSignalPreviewState();
        }

        /// <summary>Ends the preview: the map shows groups again. On another crossing, leaving the tool and undo.</summary>
        private void StopSignalPreview()
        {
            var was = signalClock != null;
            signalClock = null;
            signalClockProgram = null;
            signalPreviewPlaying = false;
            if (signalTimeline != null)
                signalTimeline.SetPlayhead(null);
            SetSignalPlayIcon(false);
            if (signalPreviewTimeField != null)
                signalPreviewTimeField.SetValueWithoutNotify(string.Empty);
            if (was)
            {
                UpdateSignalPreview();
                UpdateSignalMarkers();
            }
        }

        /// <summary>Each frame: runs the preview on by the time passed times its speed, and shows it.</summary>
        private void TickSignalPreview(float seconds)
        {
            if (signalClock == null || !signalPreviewPlaying)
                return;
            if (activeTool != EditorTool.Signals || signalCrossing == null)
            {
                StopSignalPreview();
                return;
            }
            EnsureSignalClock();
            if (signalClock == null)
                return;
            var step = seconds * signalPreviewSpeed;
            signalClock.Advance(step);
            signalPreviewRun += step;
            // Without the loop, one cycle and then a pause.
            if (!signalPreviewLoopToggle.value && signalPreviewRun >= signalClock.CycleLength)
                signalPreviewPlaying = false;
            ShowSignalPreviewState();
            if (signalPreviewLog && (int)signalClock.Time != signalPreviewLoggedSecond)
            {
                signalPreviewLoggedSecond = (int)signalClock.Time;
                Debug.Log("Signal preview: t=" + signalClock.Time.ToString("0.0", CultureInfo.InvariantCulture) + (signalClock.Held ? " held" : string.Empty) + " " +
                          string.Join(" ", Enumerable.Range(0, signalClockProgram.Lights.Count).Select(light => signalClock.StateOf(light).ToString(CultureInfo.InvariantCulture))));
            }
        }

        private bool signalPlayIconShowsPause;

        /// <summary>The play button shows pause while the preview plays.</summary>
        private void SetSignalPlayIcon(bool playing)
        {
            if (signalPreviewPlayButton == null || (signalPlayIconShowsPause == playing && signalPreviewPlayButton.childCount > 0))
                return;
            signalPlayIconShowsPause = playing;
            signalPreviewPlayButton.Clear();
            DecorateButton(signalPreviewPlayButton, playing ? "pause" : "play", null);
        }

        /// <summary>The preview on the timeline, the map and its controls.</summary>
        private void ShowSignalPreviewState()
        {
            if (signalClock == null)
                return;
            signalTimeline?.SetPlayhead(signalClock.Time);
            SetSignalPlayIcon(signalPreviewPlaying);
            if (signalPreviewTimeField != null && signalPreviewTimeField.focusController?.focusedElement != signalPreviewTimeField)
                signalPreviewTimeField.SetValueWithoutNotify(signalClock.Time.ToString("0.0", CultureInfo.InvariantCulture) + (signalClock.Held ? " (" + Loc.T("signals.preview.held") + ")" : string.Empty));
            UpdateSignalPreview();
            UpdateSignalMarkers();
        }

        /// <summary>
        /// Command line, after <c>-signals</c>: <c>-signalTab check</c> (the check, its findings as "Signal check:" in the log),
        /// <c>-signalException a b</c> (groups from 1, toggles like a click on the matrix), <c>-signalPreview s</c> (the preview at
        /// second s, paused), <c>-signalPreviewPlay</c> (plays it in real frames, its seconds as "Signal preview:" in the log),
        /// <c>-signalPreviewSpeed x</c> and <c>-signalRequest g</c> (group from 1).
        /// </summary>
        private void RunSignalCheckArguments(string[] args)
        {
            int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
            for (var index = 0; index + 2 < args.Length; index++)
                if (args[index] == "-signalException" && Number(args[index + 1]) >= 1 && Number(args[index + 2]) >= 1 && ShownSignalProgram != null &&
                    Number(args[index + 1]) <= ShownSignalProgram.Lights.Count && Number(args[index + 2]) <= ShownSignalProgram.Lights.Count)
                    ToggleSignalException(Number(args[index + 1]) - 1, Number(args[index + 2]) - 1);
            if (signalTab == SignalTab.Check)
                foreach (var finding in signalFindings)
                    Debug.Log("Signal check: " + finding.Level + " " + finding.Kind + " " + finding.First + " " + finding.Second + " at " +
                              finding.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " amount " + finding.Amount.ToString("0.0", CultureInfo.InvariantCulture));
            var speed = Array.IndexOf(args, "-signalPreviewSpeed");
            if (speed >= 0 && speed + 1 < args.Length && float.TryParse(args[speed + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) && factor > 0)
                signalPreviewSpeed = factor;
            for (var index = 0; index + 1 < args.Length; index++)
                if (args[index] == "-signalRequest" && Number(args[index + 1]) >= 1)
                {
                    EnsureSignalClock();
                    if (signalClock != null && Number(args[index + 1]) <= signalClock.Requests.Length)
                        signalClock.Requests[Number(args[index + 1]) - 1] = true;
                }
            var at = Array.IndexOf(args, "-signalPreview");
            if (at >= 0 && at + 1 < args.Length && TrySignalSeconds(args[at + 1], out var second))
            {
                if (signalTab != SignalTab.Timeline)
                    ShowSignalTab(SignalTab.Timeline);
                JumpSignalPreview(second);
                Debug.Log("Signal preview: t=" + signalClock?.Time.ToString("0.0", CultureInfo.InvariantCulture) + " " +
                          (signalClock != null ? string.Join(" ", Enumerable.Range(0, signalClockProgram.Lights.Count).Select(light => signalClock.StateOf(light).ToString(CultureInfo.InvariantCulture))) : "-"));
            }
            if (Array.IndexOf(args, "-signalPreviewPlay") >= 0)
            {
                if (signalTab != SignalTab.Timeline)
                    ShowSignalTab(SignalTab.Timeline);
                signalPreviewLog = true;
                if (!signalPreviewPlaying)
                    ToggleSignalPreview();
            }
        }
    }
}
