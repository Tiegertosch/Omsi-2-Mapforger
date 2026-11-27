using System;
using System.Collections;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Editing and deleting signal routes in the popup (ADR 105, M3 interface): below the chosen route the kind, the aspect, the distant and next
    /// signal (an ID, a pipette that takes a signal clicked in the map, and Remove) and the speed limit; a red button deletes the route after a question.
    /// Every change is one step of the history (<see cref="ObjectEditStep.SignalRouteChange"/>). The fields only show on a map that can be edited; the
    /// signal of the route itself cannot be changed yet (M4).
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private enum SignalRouteEditResult
        {
            Done,
            Unchanged,
            /// <summary>A value the file or the map cannot take (a bad number, an element that is no signal); the message says which.</summary>
            Rejected,
            /// <summary>Nothing was written for another reason (read-only map, a file the editor leaves as it is).</summary>
            Failed
        }

        private const int DistantSignalRole = 1;
        private const int NextSignalRole = 2;

        // While a signal of a route waits to be clicked in the map: the route (its place in the file) and which signal (1 distant, 2 next).
        private int signalRoutePickRoute = -1;
        private int signalRoutePickRole;
        private ModalDialog signalRouteDeleteDialog;
        private int signalRouteDeleteIndex = -1;

        private bool PickingSignalRoute => signalRoutePickRoute >= 0;

        /// <summary>
        /// What the fields of the editor were built for: the session, the map, the state of the working copy and the open editor. A delayed field commits only when it
        /// loses focus, which can be after another map, Undo or a closed popup; then its route index no longer means the route it was built for.
        /// </summary>
        private readonly struct SignalRouteEditStamp
        {
            public SignalRouteEditStamp(OmsiMapSession session, string map, int writeCount, int epoch)
            {
                Session = session;
                Map = map;
                WriteCount = writeCount;
                Epoch = epoch;
            }

            public OmsiMapSession Session { get; }
            public string Map { get; }
            public int WriteCount { get; }
            public int Epoch { get; }
        }

        // Counts the editors that were left (a new map, the popup closed): the fields of an older one commit nothing.
        private int signalRouteEditEpoch;
        private SignalRouteEditStamp signalRoutePickStamp;

        private SignalRouteEditStamp CurrentSignalRouteStamp()
        {
            return new SignalRouteEditStamp(session, currentSummary?.GlobalConfigPath ?? string.Empty, session?.Workspace != null ? session.Workspace.WriteCount : 0, signalRouteEditEpoch);
        }

        /// <summary>True when a field built for <paramref name="stamp"/> may still write; otherwise the commit is dropped without a step and the log says so.</summary>
        private bool SignalRouteEditCurrent(SignalRouteEditStamp stamp, string what)
        {
            var now = CurrentSignalRouteStamp();
            if (stamp.Session != null && ReferenceEquals(stamp.Session, now.Session) && string.Equals(stamp.Map, now.Map, StringComparison.OrdinalIgnoreCase) &&
                stamp.WriteCount == now.WriteCount && stamp.Epoch == now.Epoch)
                return true;
            Debug.Log("Signal route edit dropped (" + what + "): the map or the working copy changed since the field was shown (write count " + stamp.WriteCount + " -> " + now.WriteCount +
                      ", same session " + ReferenceEquals(stamp.Session, now.Session) + ", same editor " + (stamp.Epoch == now.Epoch) + ").");
            RefreshSignalRouteDetailSoon();
            return false;
        }

        /// <summary>
        /// The editor is about to go (another map, the popup closes): a field still being typed in gets its shown value back and loses focus, and what it would
        /// commit then is dropped, as the route it was typed for may not be the one at that place any more.
        /// </summary>
        private void ReleaseSignalRouteFields()
        {
            signalRouteEditEpoch++;
            var focused = signalRouteBody?.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null || !signalRouteBody.Contains(focused))
                return;
            var field = focused as TextField ?? focused.GetFirstAncestorOfType<TextField>();
            if (field != null && field.userData is string shown)
                field.SetValueWithoutNotify(shown);
            focused.Blur();
        }

        /// <summary>The original signalroutes.cfg, to see which routes were edited; null when the map has none (or the project no original).</summary>
        private static OmsiSignalRoutes ReadOriginalSignalRoutes(OmsiMapWorkspace workspace)
        {
            if (workspace == null)
                return null;
            try
            {
                var bytes = workspace.ReadOriginalBytes(OmsiSignalRoutes.FileName);
                return bytes == null ? null : OmsiSignalRoutes.Parse(bytes);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException || exception is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The route as the file has it now, which can be newer than the routes laid onto the map; null when there is no such route.</summary>
        private OmsiSignalRoute CurrentSignalRoute(int index)
        {
            var file = SignalRoutesFile();
            if (file != null && index >= 0 && index < file.Routes.Count)
                return file.Routes[index];
            var routes = signalRouteMap?.Routes.Routes;
            return routes != null && index >= 0 && index < routes.Count ? routes[index] : null;
        }

        private int SignalRouteNumber(int index)
        {
            var route = CurrentSignalRoute(index);
            return route == null ? index + 1 : route.Number ?? route.Index + 1;
        }

        /// <summary>Draws the detail box again after the current event, as a field's own change event must not remove the field.</summary>
        private void RefreshSignalRouteDetailSoon()
        {
            mapCanvas?.schedule.Execute(RefreshSignalRouteDetail).StartingIn(1);
        }

        private static void MarkSignalRouteChanged(VisualElement row, bool changed)
        {
            // A value that differs from the original map shows in yellow, as every edited value does.
            row.EnableInClassList("sigroute-edit-changed", changed);
        }

        /// <summary>The fields below the chosen route, only on a map that can be edited.</summary>
        private void AddSignalRouteEditor(VisualElement box, OmsiSignalRoute route)
        {
            var culture = CultureInfo.InvariantCulture;
            var index = route.Index;
            var stamp = CurrentSignalRouteStamp();
            var original = OmsiSignalRouteComparison.Counterpart(route, signalRouteOriginal);
            var editor = new VisualElement { name = "signal-route-editor" };
            editor.AddToClassList("sigroute-edit");

            var kindRow = OptionControls.Row(editor, "sigroute.edit.kind", true, false);
            MarkSignalRouteChanged(kindRow, original != null && original.Kind != route.Kind);
            var kind = new TextField { value = route.Kind.ToString(culture), isDelayed = true, name = "signal-route-kind" };
            kind.RegisterValueChangedCallback(change => CommitSignalRouteKind(stamp, index, route.Kind, change.newValue));
            OptionControls.ValueBox(kindRow, "hash", kind, null, "sigroute.edit.kind", "sigroute.edit.kind.tooltip");
            kind.AddToClassList("sigroute-edit-field");
            kind.userData = kind.value;

            var aspectRow = OptionControls.Row(editor, "sigroute.edit.aspect", true, true);
            // The two choices stand under their label: "Proceed at reduced speed" in some languages is longer than the room beside it.
            aspectRow.AddToClassList("sigroute-aspect-row");
            MarkSignalRouteChanged(aspectRow, original != null && original.Aspect != route.Aspect);
            var choices = new VisualElement();
            choices.AddToClassList("sigroute-aspect-choices");
            var clear = OptionControls.TextChoice(choices, "sigroute.aspect.clear", "sigroute.edit.aspect.tooltip", () => CommitSignalRouteAspect(stamp, index, 1, route.Aspect));
            var reduced = OptionControls.TextChoice(choices, "sigroute.aspect.reduced", "sigroute.edit.aspect.tooltip", () => CommitSignalRouteAspect(stamp, index, 2, route.Aspect));
            clear.name = "signal-route-aspect-clear";
            reduced.name = "signal-route-aspect-reduced";
            clear.EnableInClassList("panel-tab-active", route.Aspect == 1);
            reduced.EnableInClassList("panel-tab-active", route.Aspect == 2);
            // A route without [signal] has no aspect to give.
            clear.SetEnabled(route.SignalRecord != null);
            reduced.SetEnabled(route.SignalRecord != null);
            aspectRow.Add(choices);
            if (route.Aspect.HasValue && route.Aspect.Value > 2)
            {
                // An aspect from another map or program is shown and kept, not offered.
                var note = LocBindings.NewLabel("sigroute.edit.aspect.other", ("aspect", route.Aspect.Value));
                note.AddToClassList("sigroute-edit-note");
                editor.Add(note);
            }

            AddSignalRouteSignalRow(editor, route, stamp, DistantSignalRole, original != null && original.DistantSignalId != route.DistantSignalId);
            AddSignalRouteSignalRow(editor, route, stamp, NextSignalRole, original != null && original.NextSignalId != route.NextSignalId);

            var speedRow = OptionControls.Row(editor, "sigroute.edit.speed", true, false);
            MarkSignalRouteChanged(speedRow, original != null && original.SpeedLimit != route.SpeedLimit);
            var speed = new TextField { value = route.SpeedLimit.HasValue ? route.SpeedLimit.Value.ToString("0.###", culture) : string.Empty, isDelayed = true, name = "signal-route-speed" };
            speed.RegisterValueChangedCallback(change => CommitSignalRouteSpeed(stamp, index, route.SpeedLimit, change.newValue));
            OptionControls.ValueBox(speedRow, "gauge", speed, Loc.T("sigroute.edit.unit"), "sigroute.edit.speed", "sigroute.edit.speed.tooltip");
            speed.AddToClassList("sigroute-edit-field");
            speed.userData = speed.value;

            var delete = LocBindings.WithTooltip(LocBindings.NewButton("sigroute.edit.delete", () => RequestDeleteSignalRoute(index)), "sigroute.edit.delete.tooltip"); // i18n:keys
            delete.name = "signal-route-delete";
            delete.AddToClassList("map-control-button");
            delete.AddToClassList("sigroute-delete");
            editor.Add(delete);
            box.Add(editor);
        }

        private void AddSignalRouteSignalRow(VisualElement editor, OmsiSignalRoute route, SignalRouteEditStamp stamp, int role, bool changed)
        {
            var index = route.Index;
            var id = role == DistantSignalRole ? route.DistantSignalId : route.NextSignalId;
            var nameKey = role == DistantSignalRole ? "sigroute.edit.distant" : "sigroute.edit.next";
            var row = OptionControls.Row(editor, nameKey, true, true);
            row.AddToClassList("sigroute-edit-row");
            MarkSignalRouteChanged(row, changed);
            var field = new TextField { value = id.HasValue ? id.Value.ToString(CultureInfo.InvariantCulture) : string.Empty, isDelayed = true };
            field.name = role == DistantSignalRole ? "signal-route-distant" : "signal-route-next";
            field.RegisterValueChangedCallback(change => CommitSignalRouteSignal(stamp, index, role, id, change.newValue));
            OptionControls.ValueBox(row, "hash", field, null, nameKey, role == DistantSignalRole ? "sigroute.edit.distant.tooltip" : "sigroute.edit.next.tooltip"); // i18n:keys
            field.AddToClassList("sigroute-edit-field");
            field.userData = field.value;
            var pick = OptionControls.Choice(row, "pipette", nameKey, "sigroute.edit.pick.tooltip", () => StartSignalRoutePick(index, role));
            pick.name = role == DistantSignalRole ? "signal-route-pick-distant" : "signal-route-pick-next";
            pick.EnableInClassList("panel-tab-active", signalRoutePickRoute == index && signalRoutePickRole == role);
            if (id.HasValue)
            {
                var remove = OptionControls.AsTextChoice(LocBindings.WithTooltip(LocBindings.NewButton("sigroute.edit.remove", () => CommitSignalRouteSignal(stamp, index, role, id, string.Empty)),
                    "sigroute.edit.remove.tooltip")); // i18n:keys
                remove.name = role == DistantSignalRole ? "signal-route-remove-distant" : "signal-route-remove-next";
                row.Add(remove);
            }
        }

        private void CommitSignalRouteKind(SignalRouteEditStamp stamp, int index, int current, string text)
        {
            if (!SignalRouteEditCurrent(stamp, "kind"))
                return;
            if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind) || kind < 0)
            {
                SetStatusT("sigroute.status.bad_kind");
                RefreshSignalRouteDetailSoon();
                return;
            }
            if (kind == current)
            {
                RefreshSignalRouteDetailSoon();
                return;
            }
            ApplySignalRouteEdit(index, new OmsiSignalRouteEdit { Kind = kind });
        }

        private void CommitSignalRouteAspect(SignalRouteEditStamp stamp, int index, int aspect, int? current)
        {
            if (!SignalRouteEditCurrent(stamp, "aspect") || current == aspect)
                return;
            ApplySignalRouteEdit(index, new OmsiSignalRouteEdit { Aspect = aspect });
        }

        private void CommitSignalRouteSignal(SignalRouteEditStamp stamp, int index, int role, int? current, string text)
        {
            if (!SignalRouteEditCurrent(stamp, role == DistantSignalRole ? "distant signal" : "next signal"))
                return;
            text = (text ?? string.Empty).Trim();
            OmsiSignalRouteFieldEdit<int> field;
            if (text.Length == 0)
            {
                if (!current.HasValue)
                    return;
                field = OmsiSignalRouteFieldEdit<int>.Remove;
            }
            else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id >= 0)
            {
                if (current == id)
                {
                    RefreshSignalRouteDetailSoon();
                    return;
                }
                field = OmsiSignalRouteFieldEdit<int>.Set(id);
            }
            else
            {
                SetStatusT("sigroute.status.bad_id");
                RefreshSignalRouteDetailSoon();
                return;
            }
            var edit = new OmsiSignalRouteEdit();
            if (role == DistantSignalRole)
                edit.DistantSignal = field;
            else
                edit.NextSignal = field;
            ApplySignalRouteEdit(index, edit);
        }

        private void CommitSignalRouteSpeed(SignalRouteEditStamp stamp, int index, double? current, string text)
        {
            if (!SignalRouteEditCurrent(stamp, "speed limit"))
                return;
            text = (text ?? string.Empty).Trim().Replace(',', '.');
            OmsiSignalRouteFieldEdit<double> field;
            if (text.Length == 0)
            {
                if (!current.HasValue)
                    return;
                field = OmsiSignalRouteFieldEdit<double>.Remove;
            }
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && speed > 0.0 && speed <= OmsiMapSession.MaximumSignalRouteSpeed)
            {
                if (current.HasValue && Math.Abs(current.Value - speed) < 1e-9)
                {
                    RefreshSignalRouteDetailSoon();
                    return;
                }
                field = OmsiSignalRouteFieldEdit<double>.Set(speed);
            }
            else
            {
                SetStatusT("sigroute.status.bad_speed");
                RefreshSignalRouteDetailSoon();
                return;
            }
            ApplySignalRouteEdit(index, new OmsiSignalRouteEdit { SpeedLimit = field });
        }

        /// <summary>Changes a route as one step of the history and says what happened; nothing the session refuses goes unsaid and nothing is thrown.</summary>
        private SignalRouteEditResult ApplySignalRouteEdit(int routeIndex, OmsiSignalRouteEdit edit)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.signal_routes"));
                return SignalRouteEditResult.Failed;
            }
            var number = SignalRouteNumber(routeIndex);
            OmsiSignalRouteChange change;
            try
            {
                change = session.UpdateSignalRoute(routeIndex, edit, currentSummary);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                // A value the map cannot take is an answer, not a fault; anything else goes to the log as well.
                if (exception is ArgumentException)
                    Debug.LogWarning("Signal route " + number + " not changed: " + exception.Message);
                else
                    Debug.LogException(exception);
                SetStatusT("sigroute.status.edit_failed", ("number", number), ("reason", exception.Message));
                RefreshSignalRouteDetailSoon();
                return exception is ArgumentException ? SignalRouteEditResult.Rejected : SignalRouteEditResult.Failed;
            }
            if (change == null)
            {
                SetStatusT("sigroute.status.unchanged", ("number", number));
                RefreshSignalRouteDetailSoon();
                return SignalRouteEditResult.Unchanged;
            }
            PushHistory(new ObjectEditStep { SignalRouteChange = change, Description = Loc.T("sigroute.step.edit", ("number", number)) });
            AfterSignalRouteWrite();
            SetStatusT("sigroute.status.edited", ("number", number));
            return SignalRouteEditResult.Done;
        }

        /// <summary>After a step was written (not after Undo and Redo, whose refresh does the same): the routes are read again, the fields show the new values.</summary>
        private void AfterSignalRouteWrite()
        {
            InvalidateSignalRoutes();
            UpdateMapStateLabel();
            RefreshSignalRouteDetailSoon();
        }

        /// <summary>Undo or Redo of a signal route step: the choice follows (a deleted route comes back chosen).</summary>
        private void AfterSignalRouteStep(OmsiSignalRouteChange change, bool undo)
        {
            EndSignalRoutePick(false);
            // A deleted route that is undone and a created route that is redone are in the file again; the other two directions take a route out of it.
            var comesBack = change.Kind == OmsiSignalRouteChangeKind.Deletion ? undo : change.Kind == OmsiSignalRouteChangeKind.Creation && !undo;
            if (change.Kind == OmsiSignalRouteChangeKind.Deletion || change.Kind == OmsiSignalRouteChangeKind.Creation)
            {
                if (comesBack)
                {
                    // The route is back in the file at once, and the box below the list reads the file: the choice moves to it now (what stood at or after
                    // that place moved up by one). The list, the markers and the highlight follow when the routes are read again.
                    signalRouteSelected = change.RouteIndex;
                    if (SignalRoutesOpen)
                    {
                        signalRouteSelectAfter = change.RouteIndex;
                        signalRouteShape = null;
                        signalRouteShapeTask = null;
                        signalRouteShapeGeneration++;
                        ApplySignalRouteHighlight();
                        RefreshSignalRouteDetailSoon();
                    }
                }
                else
                    SignalRouteGone(change.RouteIndex);
            }
            else
                RefreshSignalRouteDetailSoon();
            Debug.Log("Signal route step " + (undo ? "undone" : "redone") + ": " + change.Kind + " of route " + change.RouteIndex + ", chosen now " + signalRouteSelected +
                      ", to choose after reading " + (signalRouteSelectAfter?.ToString(CultureInfo.InvariantCulture) ?? "-") + ".");
        }

        /// <summary>A route is no longer in the file: the choice lets go of it, or moves up with the routes after it.</summary>
        private void SignalRouteGone(int index)
        {
            if (signalRouteSelected == index)
            {
                signalRouteSelected = -1;
                signalRouteShape = null;
                signalRouteShapeTask = null;
                signalRouteShapeGeneration++;
                ApplySignalRouteHighlight();
                RefreshSignalRouteDetailSoon();
            }
            else if (signalRouteSelected > index)
                signalRouteSelected--;
        }

        // --- the pipette ---

        /// <summary>The next signal clicked in the map becomes the distant or next signal of the route; Esc or the button again stops.</summary>
        private void StartSignalRoutePick(int index, int role)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.signal_routes"));
                return;
            }
            if (PickingSignalRoute && signalRoutePickRoute == index && signalRoutePickRole == role)
            {
                EndSignalRoutePick(true);
                return;
            }
            if (!SignalToolMayYield(SignalExitKind.Action, () => StartSignalRoutePick(index, role)))
                return;
            EndVarParentPick(false);
            StopPlacement();
            if (activeTool != EditorTool.Drag)
                SetTool(EditorTool.Drag);
            if (activeTool == EditorTool.Signals)
                return;
            signalRoutePickRoute = index;
            signalRoutePickRole = role;
            signalRoutePickStamp = CurrentSignalRouteStamp();
            SetStatusT(role == DistantSignalRole ? "sigroute.status.pick_distant" : "sigroute.status.pick_next", ("number", SignalRouteNumber(index))); // i18n:keys
            RefreshSignalRouteDetailSoon();
        }

        private void EndSignalRoutePick(bool announce)
        {
            if (!PickingSignalRoute)
                return;
            signalRoutePickRoute = -1;
            signalRoutePickRole = 0;
            if (announce)
                SetStatusT("sigroute.status.pick_ended");
            RefreshSignalRouteDetailSoon();
        }

        /// <summary>Called when an object is clicked: true when the click was meant for the pipette. An element that is no signal is refused with a message and the pipette stays.</summary>
        private bool CompleteSignalRoutePick(OmsiObjectRecord clicked)
        {
            if (!PickingSignalRoute)
                return false;
            var index = signalRoutePickRoute;
            if (!SignalRouteEditCurrent(signalRoutePickStamp, "pipette"))
            {
                // The routes may have moved since the pipette was started: it ends rather than writing into another route.
                EndSignalRoutePick(true);
                return true;
            }
            var edit = new OmsiSignalRouteEdit();
            var value = OmsiSignalRouteFieldEdit<int>.Set(clicked.Id);
            if (signalRoutePickRole == DistantSignalRole)
                edit.DistantSignal = value;
            else
                edit.NextSignal = value;
            if (ApplySignalRouteEdit(index, edit) != SignalRouteEditResult.Rejected)
                EndSignalRoutePick(false);
            return true;
        }

        // --- deleting ---

        /// <summary>Asks before a route is taken out of the file; <see cref="DeleteSignalRouteNow"/> runs only after the red button.</summary>
        private void RequestDeleteSignalRoute(int index)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.signal_routes"));
                return;
            }
            if (rootElement == null)
                return;
            if (signalRouteDeleteDialog == null)
            {
                signalRouteDeleteDialog = NewDialog(new DialogOptions
                {
                    Name = "signal-route-delete-dialog",
                    TitleKey = "sigroute.delete.title",
                    DescriptionKey = "sigroute.delete.text",
                    Width = DialogWidth.Narrow,
                    BodyScrolls = false,
                    Buttons = new[]
                    {
                        new DialogButton { TextKey = "common.cancel", Role = DialogButtonRole.Cancel, Name = "signal-route-delete-cancel" },
                        new DialogButton { TextKey = "sigroute.delete.confirm", Role = DialogButtonRole.Danger, Name = "signal-route-delete-confirm", Clicked = DeleteSignalRouteNow }
                    }
                });
            }
            signalRouteDeleteIndex = index;
            LocBindings.Text(signalRouteDeleteDialog.Title, "sigroute.delete.title", ("number", SignalRouteNumber(index)));
            signalRouteDeleteDialog.Show();
        }

        private void DeleteSignalRouteNow()
        {
            var index = signalRouteDeleteIndex;
            signalRouteDeleteIndex = -1;
            if (index < 0)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.signal_routes"));
                return;
            }
            var number = SignalRouteNumber(index);
            OmsiSignalRouteChange change;
            try
            {
                change = session.DeleteSignalRoute(index);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                if (exception is ArgumentException)
                    Debug.LogWarning("Signal route " + number + " not deleted: " + exception.Message);
                else
                    Debug.LogException(exception);
                SetStatusT("sigroute.status.edit_failed", ("number", number), ("reason", exception.Message));
                return;
            }
            if (change == null)
            {
                SetStatusT("sigroute.status.unchanged", ("number", number));
                return;
            }
            EndSignalRoutePick(false);
            PushHistory(new ObjectEditStep { SignalRouteChange = change, Description = Loc.T("sigroute.step.delete", ("number", number)) });
            SignalRouteGone(index);
            AfterSignalRouteWrite();
            SetStatusT("sigroute.status.deleted", ("number", number));
        }

        // --- command line ---

        /// <summary>
        /// Command line (ADR 105, M3 interface), for test runs with the list open and a route chosen (<c>-signalRoute n</c>):
        /// <c>-signalRouteEdit kind|aspect|speed|distant|next value|none</c> changes the route as the fields do; <c>-signalRoutePick distant|next [id]</c> starts the
        /// pipette and with an ID clicks that object; <c>-signalRouteDeleteDialog</c> shows the question, <c>-signalRouteDelete</c> deletes the route without it;
        /// <c>-signalRouteUndo</c> and <c>-signalRouteRedo</c> step the history. "Signal route edit:" in the log names the route's values, its mark and the status.
        /// </summary>
        private IEnumerator RunSignalRouteEditArguments(string[] args)
        {
            string Value(string option, int offset)
            {
                var at = Array.IndexOf(args, option);
                return at >= 0 && at + offset < args.Length ? args[at + offset] : null;
            }

            var any = false;
            var field = Value("-signalRouteEdit", 1);
            if (field != null)
            {
                any = true;
                ApplySignalRouteEditArgument(field, Value("-signalRouteEdit", 2));
            }
            var pickRole = Value("-signalRoutePick", 1);
            if (pickRole != null && signalRouteSelected >= 0)
            {
                any = true;
                StartSignalRoutePick(signalRouteSelected, string.Equals(pickRole, "next", StringComparison.OrdinalIgnoreCase) ? NextSignalRole : DistantSignalRole);
                if (int.TryParse(Value("-signalRoutePick", 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var clickedId) && currentSummary != null)
                    foreach (var tile in currentSummary.Tiles)
                    {
                        var item = tile.Objects.Find(candidate => candidate.Id == clickedId);
                        if (item != null)
                        {
                            SelectObject(tile, item, false);
                            break;
                        }
                    }
            }
            if (Array.IndexOf(args, "-signalRouteDeleteDialog") >= 0 && signalRouteSelected >= 0)
            {
                any = true;
                RequestDeleteSignalRoute(signalRouteSelected);
            }
            if (Array.IndexOf(args, "-signalRouteDelete") >= 0 && signalRouteSelected >= 0)
            {
                any = true;
                yield return WaitForSignalRoutesRead();
                signalRouteDeleteIndex = signalRouteSelected;
                DeleteSignalRouteNow();
            }
            var staleField = Value("-signalRouteStaleField", 1);
            if (staleField != null)
            {
                any = true;
                yield return WaitForSignalRoutesRead();
                RunStaleFieldCheck(staleField, Value("-signalRouteStaleField", 2), Value("-signalRouteStaleField", 3));
            }
            if (Array.IndexOf(args, "-signalRouteUndo") >= 0)
            {
                any = true;
                yield return WaitForSignalRoutesRead();
                StepHistory(undoSteps, redoSteps, true);
            }
            if (Array.IndexOf(args, "-signalRouteRedo") >= 0)
            {
                any = true;
                // After Undo the routes are read again first; the next step looks at the new state, not at the one before.
                yield return WaitForSignalRoutesRead();
                StepHistory(redoSteps, undoSteps, false);
            }
            if (!any)
                yield break;
            // The routes are read again after a change; the list and the marks are looked at once that is done.
            yield return WaitForSignalRoutesRead(30);
            var route = signalRouteSelected >= 0 ? CurrentSignalRoute(signalRouteSelected) : null;
            var culture = CultureInfo.InvariantCulture;
            Debug.Log("Signal route edit: route " + (route == null ? "none chosen" : (route.Number ?? route.Index + 1) + " kind " + route.Kind + ", aspect " + route.Aspect +
                      ", distant " + (route.DistantSignalId?.ToString(culture) ?? "-") + ", next " + (route.NextSignalId?.ToString(culture) ?? "-") + ", speed " +
                      (route.SpeedLimit?.ToString("0.###", culture) ?? "-") + ", marked edited " + signalRouteEdited.Contains(route.Index)) +
                      "; edited routes " + signalRouteEdited.Count + ", picking " + PickingSignalRoute + ", undo steps " + undoSteps.Count + ", redo steps " + redoSteps.Count +
                      ", status: " + statusLabel?.text);
        }

        private IEnumerator WaitForSignalRoutesRead(int minimumFrames = 10)
        {
            for (var frame = 0; frame < 600 && (frame < minimumFrames || signalRouteTask != null || signalRouteRebuildPending || signalRouteShapeTask != null); frame++)
                yield return null;
        }

        /// <summary>
        /// <c>-signalRouteStaleField speed|kind|distant|next value undo|close</c>: gives the field of the chosen route focus, then
        /// Undoes the last step (or closes the popup), then commits the value the way the field does on losing focus. The value must not be written: "Signal route edit dropped" in the log and no new step.
        /// </summary>
        private void RunStaleFieldCheck(string fieldName, string value, string interruption)
        {
            var name = "signal-route-" + (fieldName ?? string.Empty).ToLowerInvariant();
            var field = signalRouteBody?.Q<TextField>(name);
            if (field == null)
            {
                Debug.LogWarning("-signalRouteStaleField found no field " + name + "; choose a route first.");
                return;
            }
            var stepsBefore = undoSteps.Count;
            var redoBefore = redoSteps.Count;
            var shownBefore = field.value;
            // The Player has no keyboard to type with: the field gets focus, and what a delayed field does when it loses focus (take the typed text over as its
            // value, which runs the commit) is done at the end by setting the value.
            field.Focus();
            Debug.Log("Signal route stale field: " + name + " has focus, shows '" + shownBefore + "', undo steps " + undoSteps.Count + ", redo steps " + redoSteps.Count + ".");
            if (string.Equals(interruption, "close", StringComparison.OrdinalIgnoreCase))
                ShowProjectTab(ProjectTab.Objects);
            else
                StepHistory(undoSteps, redoSteps, true);
            Debug.Log("Signal route stale field: after the interruption undo steps " + undoSteps.Count + ", redo steps " + redoSteps.Count + ", write count " +
                      (session?.Workspace != null ? session.Workspace.WriteCount : 0) + ", status: " + statusLabel?.text);
            field.value = value ?? string.Empty;
            Debug.Log("Signal route stale field: late commit of '" + value + "': undo steps " + stepsBefore + " -> " + undoSteps.Count + ", redo steps " + redoBefore + " -> " + redoSteps.Count +
                      ", write count " + (session?.Workspace != null ? session.Workspace.WriteCount : 0) + ".");
        }

        private void ApplySignalRouteEditArgument(string field, string value)
        {
            if (signalRouteSelected < 0)
            {
                Debug.LogWarning("-signalRouteEdit needs a route chosen with -signalRoute.");
                return;
            }
            var culture = CultureInfo.InvariantCulture;
            var none = value == null || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
            var edit = new OmsiSignalRouteEdit();
            switch (field.ToLowerInvariant())
            {
                case "kind":
                case "aspect":
                    if (!int.TryParse(value, NumberStyles.Integer, culture, out var number))
                    {
                        Debug.LogWarning("-signalRouteEdit " + field + " takes a whole number.");
                        return;
                    }
                    if (field.ToLowerInvariant() == "kind")
                        edit.Kind = number;
                    else
                        edit.Aspect = number;
                    break;
                case "speed":
                    if (none)
                        edit.SpeedLimit = OmsiSignalRouteFieldEdit<double>.Remove;
                    else if (double.TryParse(value, NumberStyles.Float, culture, out var speed))
                        edit.SpeedLimit = OmsiSignalRouteFieldEdit<double>.Set(speed);
                    else
                    {
                        Debug.LogWarning("-signalRouteEdit speed takes km/h or none.");
                        return;
                    }
                    break;
                case "distant":
                case "next":
                    OmsiSignalRouteFieldEdit<int> signal;
                    if (none)
                        signal = OmsiSignalRouteFieldEdit<int>.Remove;
                    else if (int.TryParse(value, NumberStyles.Integer, culture, out var id))
                        signal = OmsiSignalRouteFieldEdit<int>.Set(id);
                    else
                    {
                        Debug.LogWarning("-signalRouteEdit " + field + " takes an ID or none.");
                        return;
                    }
                    if (field.ToLowerInvariant() == "distant")
                        edit.DistantSignal = signal;
                    else
                        edit.NextSignal = signal;
                    break;
                default:
                    Debug.LogWarning("-signalRouteEdit knows kind, aspect, speed, distant and next, not " + field + ".");
                    return;
            }
            ApplySignalRouteEdit(signalRouteSelected, edit);
        }
    }
}
