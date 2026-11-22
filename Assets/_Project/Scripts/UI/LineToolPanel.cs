using System;
using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The line tool (ADR 094) as the other tools' windows (ADR 098, M4): at the left the options column (the line's name and vehicle group, road or rail,
    /// the direction a click adds for, waypoints, copying and clearing), at the right a window like the catalog of the other tools with the button that
    /// saves the line in its header and the route strip below it. The strip is the line as a horizontal diagram in the order of the outbound trip: a
    /// thick track, a round node for each stop with its name written aslant below it, and above each node the two buttons that move it one place left or
    /// right. The strip shows the list of the direction chosen at the left. What the next click does and the result of the way go to the status bar's hint place.
    /// </summary>
    internal sealed class LineToolPanel : VisualElement
    {
        private const float DragThreshold = 5f;

        private readonly VisualElement options;
        private readonly ToolCatalog catalog;
        private readonly TextField nameField = new TextField { isDelayed = true };
        private readonly VisualElement groupHost = new VisualElement();
        private readonly Button roadButton;
        private readonly Button railButton;
        private readonly Button outButton;
        private readonly Button backButton;
        private readonly Toggle ringToggle;
        private readonly Toggle waypointToggle;
        private readonly Button copyButton;
        private readonly ScrollView strip = new ScrollView(ScrollViewMode.Horizontal);
        private readonly VisualElement content;
        private readonly VisualElement dropIndicator = new VisualElement { pickingMode = PickingMode.Ignore };
        private readonly List<VisualElement> columns = new List<VisualElement>();
        private readonly Label editTitle = new Label();
        private readonly List<VisualElement> newLineOnly = new List<VisualElement>();
        private readonly Button saveButton;
        private DropdownField groupField;
        private TextField groupText;
        private string editTrip;
        private string resultText = string.Empty;
        private bool resultProblem;
        private int stopCount;

        // What was shown last, so a language change or the end of a drag shows it again.
        private IList<OmsiTripStopChoice> shownEntries;
        private bool shownBack;
        private bool shownRing;
        private bool shownWaypointMode;
        private bool shownRail;
        private int selected = -1;
        private int revealIndex = -1;

        // A press on a node: a click selects, a drag along the strip moves the stop.
        private int pressIndex = -1;
        private Vector2 pressPoint;
        private bool dragging;
        private int dropSlot;
        private TextField renameField;
        private string renameOriginal;
        private VisualElement pressNode;
        private int pressPointer;
        private float pointerX;
        private IVisualElementScheduledItem autoScroll;
        private bool rebuildAfterwards;

        public LineToolPanel()
        {
            AddToClassList("picker-tool");
            // The row is as tall as its taller panel; the empty part above the shorter one must not catch clicks meant for the map.
            pickingMode = PickingMode.Ignore;
            options = ToolCatalog.Options(icons: true);
            Add(options);

            // While the stops of a trip are edited, its name heads the column; the explanation is the heading's tooltip.
            editTitle.AddToClassList("option-heading");
            editTitle.style.display = DisplayStyle.None;
            options.Add(editTitle);

            var nameRow = OptionControls.Row(options, "line.row.name");
            nameField.AddToClassList("picker-field");
            nameField.AddToClassList("form-field-grow");
            LocBindings.TitledTooltip(nameField, "line.name", "line.name.tooltip");
            nameField.RegisterValueChangedCallback(change => NameChanged?.Invoke(change.newValue));
            nameRow.Add(nameField);
            newLineOnly.Add(nameRow);

            var groupRow = OptionControls.Row(options, "line.row.group");
            groupHost.AddToClassList("mf-grow");
            groupRow.Add(groupHost);
            newLineOnly.Add(groupRow);

            var vehicleRow = OptionControls.Row(options, "line.row.vehicle");
            var vehicle = OptionControls.Group(vehicleRow);
            roadButton = OptionControls.Choice(vehicle, "bus-front", "line.vehicle.road", "line.vehicle.road.tooltip", () => RailChanged?.Invoke(false));
            railButton = OptionControls.Choice(vehicle, "train-front", "line.rail", "line.rail.tooltip", () => RailChanged?.Invoke(true));
            newLineOnly.Add(vehicleRow);

            var directionRow = OptionControls.Row(options, "line.row.direction");
            var directions = OptionControls.Group(directionRow);
            outButton = OptionControls.Choice(directions, "arrow-right", "line.mode.out", "line.mode.out.tooltip", () => DirectionChosen?.Invoke(false));
            backButton = OptionControls.Choice(directions, "arrow-left", "line.mode.back", "line.mode.back.tooltip", () => DirectionChosen?.Invoke(true));
            ringToggle = OptionControls.Switch(directionRow, "repeat", "line.ring", "line.ring.tooltip", false);
            ringToggle.RegisterValueChangedCallback(change => RingChanged?.Invoke(change.newValue));
            newLineOnly.Add(directionRow);

            var stopsRow = OptionControls.Row(options, "line.row.stops");
            waypointToggle = OptionControls.Switch(stopsRow, "waypoints", "line.waypoint", "line.waypoint.tooltip", false);
            waypointToggle.RegisterValueChangedCallback(change => WaypointModeChanged?.Invoke(change.newValue));
            var actions = OptionControls.Group(stopsRow);
            copyButton = OptionControls.Choice(actions, "copy", "line.copy", "line.copy.tooltip", () => CopyReversedRequested?.Invoke());
            newLineOnly.Add(copyButton);
            var clear = OptionControls.Choice(actions, "trash-2", "line.clear", "line.clear.tooltip", () => ClearRequested?.Invoke());
            clear.AddToClassList("option-icon-danger");

            // The window at the right: the save button where the other tools have their tabs, the X, and the strip under them.
            catalog = new ToolCatalog("line.close.tooltip", () => CloseRequested?.Invoke(), scrollingTiles: false);
            catalog.Panel.AddToClassList("line-panel");
            saveButton = LocBindings.WithTooltip(LocBindings.NewButton("line.save", () => SaveRequested?.Invoke()), "line.save.tooltip");
            saveButton.AddToClassList("map-control-button");
            saveButton.AddToClassList("dialog-primary");
            saveButton.AddToClassList("line-save");
            catalog.Tabs.Add(saveButton);
            LocBindings.Text(catalog.Empty, "line.strip.empty");

            strip.AddToClassList("line-strip-scroll");
            strip.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            strip.horizontalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            content = strip.contentContainer;
            content.AddToClassList("line-strip");
            dropIndicator.AddToClassList("line-drop");
            dropIndicator.style.display = DisplayStyle.None;
            catalog.Panel.Insert(1, strip);
            Add(catalog.Panel);
            UpdateHint();
        }

        public event Action<string> NameChanged;
        public event Action<string> GroupChanged;
        public event Action<bool> DirectionChosen;
        public event Action<bool> RingChanged;
        public event Action<bool> WaypointModeChanged;

        /// <summary>The line runs on rails (train or tram) instead of roads.</summary>
        public event Action<bool> RailChanged;
        public event Action CopyReversedRequested;
        public event Action ClearRequested;
        public event Action SaveRequested;
        public event Action CloseRequested;

        /// <summary>A stop was dragged or moved by its arrows: its place in the strip and the place it is to take.</summary>
        public event Action<int, int> MoveRequested;
        public event Action<int> RemoveRequested;

        /// <summary>A stop was clicked.</summary>
        public event Action<int> SelectRequested;

        public event Action<OmsiTripStopChoice, string> StopNamed;

        /// <summary>What the next click does or what the way is; the shell shows it in the status bar's hint place.</summary>
        public event Action<string, bool> HintChanged;

        /// <summary>The line's name as typed.</summary>
        public string LineName => nameField.value ?? string.Empty;

        /// <summary>The vehicle group chosen or typed.</summary>
        public string Group => groupField != null ? groupField.value ?? string.Empty : groupText?.value ?? string.Empty;

        public bool WaypointMode => waypointToggle.value;

        /// <summary>True while a stop is dragged or renamed; the strip is not drawn anew then, so the drag and the typing are not lost.</summary>
        public bool Interacting => dragging || renameField != null;

        /// <summary>
        /// Shows the tool for the stops of a trip that exists (its name), or as the tool for new lines (null): without the line's name and group,
        /// the vehicle, the return trip and the ring, and with a button that saves the stops into the trip.
        /// </summary>
        public void SetEditing(string trip)
        {
            var editing = trip != null;
            var changed = editing != (editTrip != null);
            editTrip = trip;
            if (editing)
            {
                editTitle.text = Loc.T("line.edit.title", ("trip", trip));
                editTitle.tooltip = Loc.T("line.edit.banner", ("trip", trip));
            }
            editTitle.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var element in newLineOnly)
                element.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
            saveButton.text = editing ? Loc.T("line.save_trip") : Loc.T("line.save");
            if (changed && shownEntries != null)
            {
                if (Interacting)
                    rebuildAfterwards = true;
                else
                    BuildStrip();
            }
            UpdateHint();
        }

        /// <summary>
        /// Lets go of a drag or a rename without applying it (the tool is left, or Esc is pressed): the pointer is released, the stop stays where it was,
        /// the rename field closes without its text. True when something was going on.
        /// </summary>
        public bool CancelInteraction()
        {
            var was = Interacting;
            if (pressNode != null && pressNode.HasPointerCapture(pressPointer))
                pressNode.ReleasePointer(pressPointer);
            FinishPress(false);
            if (renameField != null)
            {
                var field = renameField;
                field.SetValueWithoutNotify(renameOriginal);
                EndRename(field);
            }
            return was;
        }

        public void SetName(string name)
        {
            nameField.SetValueWithoutNotify(name ?? string.Empty);
        }

        /// <summary>The vehicle groups to choose from; without any the group is typed.</summary>
        public void SetGroups(List<string> groups, string chosen)
        {
            groupHost.Clear();
            groupField = null;
            groupText = null;
            if (groups != null && groups.Count > 0)
            {
                var index = Math.Max(0, groups.IndexOf(chosen));
                groupField = new DropdownField(groups, index);
                groupField.AddToClassList("new-map-dropdown");
                LocBindings.TitledTooltip(groupField, "line.group", "line.group.tooltip");
                groupField.RegisterValueChangedCallback(change => GroupChanged?.Invoke(change.newValue));
                groupHost.Add(groupField);
            }
            else
            {
                groupText = new TextField { value = chosen ?? string.Empty, isDelayed = true };
                groupText.AddToClassList("picker-field");
                groupText.AddToClassList("form-field-auto");
                LocBindings.TitledTooltip(groupText, "line.group", "line.group.tooltip");
                groupText.RegisterValueChangedCallback(change => GroupChanged?.Invoke(change.newValue));
                groupHost.Add(groupText);
            }
        }

        /// <summary>
        /// Shows which vehicle runs the line, which direction a click adds for, whether the line is a ring (no return), and the strip.
        /// </summary>
        /// <param name="selectedStop">The place of the selected stop in the strip, or -1.</param>
        public void ShowDirection(bool back, bool ring, bool waypointMode, bool rail, IList<OmsiTripStopChoice> entries, int selectedStop)
        {
            roadButton.EnableInClassList("panel-tab-active", !rail);
            railButton.EnableInClassList("panel-tab-active", rail);
            outButton.EnableInClassList("panel-tab-active", !back);
            backButton.EnableInClassList("panel-tab-active", back);
            backButton.SetEnabled(!ring);
            backButton.EnableInClassList("option-icon-locked", ring);
            ringToggle.SetValueWithoutNotify(ring);
            waypointToggle.SetValueWithoutNotify(waypointMode);
            shownBack = back;
            shownRing = ring;
            shownWaypointMode = waypointMode;
            shownRail = rail;
            shownEntries = entries;
            selected = selectedStop;
            stopCount = entries.Count;
            if (Interacting)
            {
                rebuildAfterwards = true;
                UpdateHint();
                return;
            }
            BuildStrip();
            UpdateHint();
        }

        /// <summary>Scrolls the strip so that the stop at this place is in view.</summary>
        public void ScrollTo(int index)
        {
            if (index < 0 || index >= columns.Count)
                return;
            var column = columns[index];
            strip.schedule.Execute(() =>
            {
                if (column.parent == content)
                    strip.ScrollTo(column);
            });
        }

        /// <summary>Marks the selected stop without drawing the strip anew.</summary>
        public void SetSelected(int index)
        {
            selected = index;
            for (var position = 0; position < columns.Count; position++)
                columns[position].EnableInClassList("line-stop-selected", position == index);
        }

        /// <summary>The next time the strip is shown, it scrolls to this place (a stop just added or moved); the end of the strip for a place at the end.</summary>
        public void Reveal(int index)
        {
            revealIndex = index;
        }

        /// <summary>Sets the texts that were written from the language again: the heading, the hint and the strip's labels and tooltips.</summary>
        public void RefreshTexts()
        {
            SetEditing(editTrip);
            if (shownEntries != null)
                ShowDirection(shownBack, shownRing, shownWaypointMode, shownRail, shownEntries, selected);
        }

        public void ShowResult(string text, bool problem)
        {
            resultText = text ?? string.Empty;
            resultProblem = problem;
            UpdateHint();
        }

        private void BuildStrip()
        {
            rebuildAfterwards = false;
            var scrolled = strip.scrollOffset;
            content.Clear();
            columns.Clear();
            content.Add(dropIndicator);
            var entries = shownEntries;
            for (var index = 0; index < entries.Count; index++)
            {
                var column = BuildColumn(index, entries[index], entries.Count);
                columns.Add(column);
                content.Add(column);
            }
            SetSelected(selected);
            var empty = entries.Count == 0;
            strip.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            catalog.Empty.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            if (revealIndex >= 0 && columns.Count > 0)
            {
                var target = Math.Min(revealIndex, columns.Count - 1);
                var atEnd = revealIndex >= columns.Count - 1;
                revealIndex = -1;
                strip.schedule.Execute(() =>
                {
                    if (atEnd)
                        strip.scrollOffset = new Vector2(Mathf.Max(0f, strip.horizontalScroller.highValue), 0f);
                    else if (target < columns.Count && columns[target].parent == content)
                        strip.ScrollTo(columns[target]);
                }).ExecuteLater(30);
            }
            else
            {
                RestoreScroll(scrolled);
            }
        }

        private VisualElement BuildColumn(int index, OmsiTripStopChoice item, int count)
        {
            var column = new VisualElement();
            column.AddToClassList("line-stop");
            if (item.IsWaypoint)
                column.AddToClassList("line-stop-waypoint-column");

            // Above the node: one place to the left, one place to the right; locked at the ends of the list. A waypoint gets the smaller pair.
            var moves = new VisualElement();
            moves.AddToClassList("line-stop-moves");
            moves.Add(BuildMoveButton(index, -1, count, item.IsWaypoint));
            moves.Add(BuildMoveButton(index, 1, count, item.IsWaypoint));
            column.Add(moves);

            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("line-stop-track");
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("line-track-bar");
            track.Add(bar);
            var node = new VisualElement();
            node.AddToClassList("line-node");
            node.AddToClassList(item.IsWaypoint ? "line-node-waypoint" : "line-node-stop");
            node.tooltip = LocBindings.CardTooltip(item.IsWaypoint ? Loc.T("line.waypoint.row") : item.Name, Loc.T("line.stop.tooltip"));
            node.RegisterCallback<PointerDownEvent>(evt => OnNodeDown(evt, index, node));
            node.RegisterCallback<PointerMoveEvent>(OnNodeMove);
            node.RegisterCallback<PointerUpEvent>(evt => OnNodeUp(evt, node));
            node.RegisterCallback<PointerCaptureOutEvent>(_ => FinishPress(false));
            track.Add(node);
            var remove = new Button(() => RemoveRequested?.Invoke(index)) { tooltip = Loc.T("line.remove") };
            remove.AddToClassList("line-stop-remove");
            remove.Add(new IconElement("x"));
            track.Add(remove);
            column.Add(track);

            if (!item.IsWaypoint)
            {
                var label = new Label(item.Name);
                label.AddToClassList("line-stop-label");
                label.tooltip = item.IsNew ? LocBindings.CardTooltip(item.Name, Loc.T("line.stop.new.double")) : item.Name;
                label.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.clickCount == 2 && item.IsNew)
                        BeginRename(column, item);
                    else if (evt.clickCount == 1)
                        SelectRequested?.Invoke(index);
                });
                column.Add(label);
            }
            return column;
        }

        private Button BuildMoveButton(int index, int delta, int count, bool small)
        {
            var button = new Button();
            button.AddToClassList("line-move");
            if (small)
                button.AddToClassList("line-move-small");
            button.Add(new IconElement(delta < 0 ? "arrow-left" : "arrow-right"));
            button.tooltip = delta < 0 ? Loc.T("line.move.left") : Loc.T("line.move.right");
            var target = LineStrip.StepTarget(index, delta, count);
            var available = target >= 0;
            button.SetEnabled(available);
            button.EnableInClassList("line-move-locked", !available);
            button.clicked += () => MoveRequested?.Invoke(index, target);
            return button;
        }

        private void OnNodeDown(PointerDownEvent evt, int index, VisualElement node)
        {
            if (evt.button != 0 || pressIndex >= 0)
                return;
            pressIndex = index;
            pressPoint = evt.position;
            dragging = false;
            pressNode = node;
            pressPointer = evt.pointerId;
            node.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnNodeMove(PointerMoveEvent evt)
        {
            if (pressIndex < 0)
                return;
            if (!dragging)
            {
                if (Mathf.Abs(evt.position.x - pressPoint.x) < DragThreshold)
                    return;
                dragging = true;
                columns[pressIndex].AddToClassList("line-stop-dragging");
                // The strip keeps scrolling while the pointer rests at its edge.
                autoScroll = strip.schedule.Execute(() =>
                {
                    if (!dragging)
                        return;
                    ScrollNearEdge(pointerX);
                    UpdateDrop(pointerX);
                }).Every(16);
            }
            pointerX = evt.position.x;
            UpdateDrop(pointerX);
            ScrollNearEdge(pointerX);
        }

        private void UpdateDrop(float x)
        {
            dropSlot = SlotAt(x);
            ShowDropIndicator();
        }

        private void OnNodeUp(PointerUpEvent evt, VisualElement node)
        {
            if (pressIndex < 0 || evt.button != 0)
                return;
            // Finished first: releasing the pointer sends the capture-out event, which would end the press without its result.
            FinishPress(true);
            node.ReleasePointer(evt.pointerId);
        }

        /// <summary>The press on a node ends: a drag moves the stop, a click selects it, a lost pointer does neither.</summary>
        private void FinishPress(bool commit)
        {
            if (pressIndex < 0)
                return;
            var index = pressIndex;
            var moved = dragging;
            var slot = dropSlot;
            pressIndex = -1;
            dragging = false;
            pressNode = null;
            autoScroll?.Pause();
            autoScroll = null;
            dropIndicator.style.display = DisplayStyle.None;
            if (index < columns.Count)
                columns[index].RemoveFromClassList("line-stop-dragging");
            if (commit)
            {
                if (moved)
                {
                    var target = LineStrip.DropTarget(index, slot);
                    if (target != index)
                        MoveRequested?.Invoke(index, target);
                }
                else
                {
                    SelectRequested?.Invoke(index);
                }
            }
            if (rebuildAfterwards && !Interacting)
                BuildStrip();
        }

        /// <summary>The place a dragged stop would take: 0 before the first column, the count after the last.</summary>
        private int SlotAt(float x)
        {
            var slot = 0;
            for (var index = 0; index < columns.Count; index++)
                if (x > columns[index].worldBound.center.x)
                    slot = index + 1;
            return slot;
        }

        private void ShowDropIndicator()
        {
            if (columns.Count == 0 || LineStrip.DropTarget(pressIndex, dropSlot) == pressIndex)
            {
                dropIndicator.style.display = DisplayStyle.None;
                return;
            }
            var worldX = dropSlot >= columns.Count ? columns[columns.Count - 1].worldBound.xMax : columns[dropSlot].worldBound.xMin;
            var local = content.WorldToLocal(new Vector2(worldX, 0f));
            dropIndicator.style.left = local.x - 1.5f;
            dropIndicator.style.display = DisplayStyle.Flex;
        }

        /// <summary>A stop dragged to the edge of the strip scrolls it on, so far places can be reached.</summary>
        private void ScrollNearEdge(float x)
        {
            var view = strip.contentViewport.worldBound;
            var offset = strip.scrollOffset;
            if (x < view.xMin + 28f)
                offset.x = Mathf.Max(0f, offset.x - 12f);
            else if (x > view.xMax - 28f)
                offset.x += 12f;
            else
                return;
            strip.scrollOffset = offset;
        }

        /// <summary>The name of a new stop typed in place of its label, as the label's text field does in the old list.</summary>
        private void BeginRename(VisualElement column, OmsiTripStopChoice item)
        {
            if (renameField != null)
                return;
            var field = new TextField { value = item.Name, isDelayed = true };
            renameField = field;
            renameOriginal = item.Name;
            field.AddToClassList("picker-field");
            field.AddToClassList("line-stop-rename");
            field.tooltip = Loc.T("line.stop.new.double");
            field.style.left = column.layout.x;
            field.RegisterValueChangedCallback(change =>
            {
                renameField = null;
                StopNamed?.Invoke(item, change.newValue);
            });
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                field.SetValueWithoutNotify(item.Name);
                field.Blur();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => field.schedule.Execute(() => EndRename(field)));
            content.Add(field);
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.textSelection.SelectAll();
            });
        }

        private void EndRename(TextField field)
        {
            field.RemoveFromHierarchy();
            if (renameField == field)
                renameField = null;
            if (rebuildAfterwards && !Interacting)
                BuildStrip();
        }

        /// <summary>The strip has no layout right after it was rebuilt, so the offset is set again once its content has its size.</summary>
        private void RestoreScroll(Vector2 offset)
        {
            if (offset == Vector2.zero)
                return;
            strip.scrollOffset = offset;
            EventCallback<GeometryChangedEvent> restore = null;
            restore = _ =>
            {
                content.UnregisterCallback(restore);
                strip.scrollOffset = offset;
            };
            content.RegisterCallback(restore);
        }

        /// <summary>
        /// The result of the way when there is one, else what to click next. Without a stop the list of the panel says to click one, so
        /// the status bar stays silent then (M6 step 5), except for the trip being edited.
        /// </summary>
        private void UpdateHint()
        {
            HintChanged?.Invoke(HintText(resultText, editTrip, stopCount), resultText.Length > 0 && resultProblem);
        }

        /// <summary>The status bar text of the panel: the result, else the banner of an edited trip without a stop, else the hint once a stop is chosen, else nothing.</summary>
        internal static string HintText(string result, string editTrip, int stops)
        {
            if (!string.IsNullOrEmpty(result))
                return result;
            if (editTrip != null && stops == 0)
                return Loc.T("line.edit.banner", ("trip", editTrip));
            return stops > 0 ? Loc.T("line.hint") : string.Empty;
        }
    }
}
