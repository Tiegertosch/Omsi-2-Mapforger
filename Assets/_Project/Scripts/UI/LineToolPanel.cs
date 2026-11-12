using System;
using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>The options of the line tool (ADR 094): the line's name, the stops of each direction in order, waypoints, the result of the way and saving.</summary>
    internal sealed class LineToolPanel : VisualElement
    {
        private readonly TextField nameField = new TextField { isDelayed = true };
        private readonly VisualElement groupHost = new VisualElement();
        private readonly Button outButton;
        private readonly Button backButton;
        private readonly Toggle ringToggle;
        private readonly Toggle waypointToggle;
        private readonly VisualElement list = new VisualElement();
        private readonly Label resultLabel = new Label();
        private readonly Label hintLabel;
        private readonly Label editLabel = new Label();
        private readonly List<VisualElement> newLineOnly = new List<VisualElement>();
        private Button saveButton;
        private Toggle railToggle;
        private DropdownField groupField;
        private TextField groupText;

        public LineToolPanel()
        {
            AddToClassList("tool-panel");
            AddToClassList("picker-options");
            hintLabel = LocBindings.NewLabel("line.hint");
            hintLabel.AddToClassList("panel-info");
            Add(hintLabel);
            editLabel.AddToClassList("panel-info");
            editLabel.style.whiteSpace = WhiteSpace.Normal;
            editLabel.style.display = DisplayStyle.None;
            Add(editLabel);

            nameField.AddToClassList("picker-field");
            nameField.style.width = StyleKeyword.Auto;
            nameField.style.flexGrow = 1;
            nameField.RegisterValueChangedCallback(change => NameChanged?.Invoke(change.newValue));
            newLineOnly.Add(Row("line.name", nameField));
            groupHost.style.flexGrow = 1;
            newLineOnly.Add(Row("line.group", groupHost));

            var directions = new VisualElement();
            directions.AddToClassList("panel-tabs");
            directions.AddToClassList("picker-segmented");
            outButton = LocBindings.WithTooltip(LocBindings.NewButton("line.mode.out", () => DirectionChosen?.Invoke(false)), "line.mode.out.tooltip");
            backButton = LocBindings.WithTooltip(LocBindings.NewButton("line.mode.back", () => DirectionChosen?.Invoke(true)), "line.mode.back.tooltip");
            foreach (var button in new[] { outButton, backButton })
            {
                button.AddToClassList("panel-tab");
                directions.Add(button);
            }
            Add(directions);
            newLineOnly.Add(directions);

            ringToggle = new Toggle(Loc.T("line.ring")) { tooltip = Loc.T("line.ring.tooltip") };
            ringToggle.AddToClassList("content-folders-toggle");
            ringToggle.RegisterValueChangedCallback(change => RingChanged?.Invoke(change.newValue));
            Add(ringToggle);
            newLineOnly.Add(ringToggle);
            railToggle = new Toggle(Loc.T("line.rail")) { tooltip = Loc.T("line.rail.tooltip") };
            railToggle.AddToClassList("content-folders-toggle");
            railToggle.RegisterValueChangedCallback(change => RailChanged?.Invoke(change.newValue));
            Add(railToggle);
            newLineOnly.Add(railToggle);
            waypointToggle = new Toggle(Loc.T("line.waypoint")) { tooltip = Loc.T("line.waypoint.tooltip") };
            waypointToggle.AddToClassList("content-folders-toggle");
            waypointToggle.RegisterValueChangedCallback(change => WaypointModeChanged?.Invoke(change.newValue));
            Add(waypointToggle);

            list.style.marginTop = 4;
            list.style.marginBottom = 4;
            Add(list);

            var actions = new VisualElement();
            actions.AddToClassList("panel-tabs");
            actions.AddToClassList("picker-segmented");
            var copy = LocBindings.WithTooltip(LocBindings.NewButton("line.copy", () => CopyReversedRequested?.Invoke()), "line.copy.tooltip");
            newLineOnly.Add(copy);
            foreach (var button in new[] { copy, LocBindings.WithTooltip(LocBindings.NewButton("line.clear", () => ClearRequested?.Invoke()), "line.clear.tooltip") })
            {
                button.AddToClassList("panel-tab");
                actions.Add(button);
            }
            Add(actions);

            resultLabel.AddToClassList("panel-info");
            resultLabel.style.whiteSpace = WhiteSpace.Normal;
            Add(resultLabel);

            saveButton = LocBindings.WithTooltip(LocBindings.NewButton("line.save", () => SaveRequested?.Invoke()), "line.save.tooltip");
            saveButton.AddToClassList("map-control-button");
            saveButton.AddToClassList("dialog-primary");
            Add(saveButton);
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
        public event Action<int, int> MoveRequested;
        public event Action<int> RemoveRequested;
        public event Action<OmsiTripStopChoice, string> StopNamed;

        /// <summary>The line's name as typed.</summary>
        public string LineName => nameField.value ?? string.Empty;

        /// <summary>The vehicle group chosen or typed.</summary>
        public string Group => groupField != null ? groupField.value ?? string.Empty : groupText?.value ?? string.Empty;

        public bool WaypointMode => waypointToggle.value;

        /// <summary>
        /// Shows the tool for the stops of a trip that exists (a text about it), or as the tool for new lines (null): without the line's name and group,
        /// the return trip and the ring, and with a button that saves the stops into the trip.
        /// </summary>
        public void SetEditing(string text)
        {
            var editing = text != null;
            editLabel.text = text ?? string.Empty;
            editLabel.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var element in newLineOnly)
                element.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
            saveButton.text = editing ? Loc.T("line.save_trip") : Loc.T("line.save");
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
                groupField.RegisterValueChangedCallback(change => GroupChanged?.Invoke(change.newValue));
                groupHost.Add(groupField);
            }
            else
            {
                groupText = new TextField { value = chosen ?? string.Empty, isDelayed = true };
                groupText.AddToClassList("picker-field");
                groupText.style.width = StyleKeyword.Auto;
                groupText.RegisterValueChangedCallback(change => GroupChanged?.Invoke(change.newValue));
                groupHost.Add(groupText);
            }
        }

        /// <summary>Shows which direction is being made, whether the line is a ring (no return), and the stops of that direction.</summary>
        public void ShowDirection(bool back, bool ring, bool waypointMode, IList<OmsiTripStopChoice> items)
        {
            outButton.EnableInClassList("panel-tab-active", !back);
            backButton.EnableInClassList("panel-tab-active", back);
            backButton.SetEnabled(!ring);
            ringToggle.SetValueWithoutNotify(ring);
            waypointToggle.SetValueWithoutNotify(waypointMode);
            list.Clear();
            var number = 0;
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var position = index;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;
                if (item.IsWaypoint)
                {
                    var waypoint = new Label(Loc.T("line.waypoint.row")) { tooltip = Loc.T("line.waypoint.tooltip") };
                    waypoint.AddToClassList("panel-info");
                    waypoint.style.flexGrow = 1;
                    row.Add(waypoint);
                }
                else
                {
                    number++;
                    var label = new Label(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                    label.AddToClassList("popup-description");
                    label.style.width = 22;
                    row.Add(label);
                    if (item.IsNew)
                    {
                        var name = new TextField { value = item.Name, isDelayed = true, tooltip = Loc.T("line.stop.new.tooltip") };
                        name.AddToClassList("picker-field");
                        name.style.width = StyleKeyword.Auto;
                        name.style.flexGrow = 1;
                        name.RegisterValueChangedCallback(change => StopNamed?.Invoke(item, change.newValue));
                        row.Add(name);
                    }
                    else
                    {
                        var known = new Label(item.Name) { tooltip = item.Name };
                        known.AddToClassList("popup-description");
                        known.style.flexGrow = 1;
                        row.Add(known);
                    }
                }
                row.Add(RowButton("↑", "line.up", () => MoveRequested?.Invoke(position, -1)));
                row.Add(RowButton("↓", "line.down", () => MoveRequested?.Invoke(position, 1)));
                row.Add(RowButton("×", "line.remove", () => RemoveRequested?.Invoke(position)));
                list.Add(row);
            }
            hintLabel.style.display = items.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ShowResult(string text, bool problem)
        {
            resultLabel.text = text;
            resultLabel.EnableInClassList("panel-info-warning", problem);
        }

        private VisualElement Row(string labelKey, VisualElement control)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.AddToClassList("picker-option-label");
            row.Add(label);
            row.Add(control);
            Add(row);
            return row;
        }

        private static Button RowButton(string text, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked) { text = text, tooltip = Loc.T(tooltipKey) }; // i18n:keys
            button.AddToClassList("map-control-button");
            button.style.marginLeft = 4;
            return button;
        }
    }
}
