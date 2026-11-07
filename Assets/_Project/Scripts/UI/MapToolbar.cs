using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// What a left drag on the map does: drag the selection freely, move it freely or along axis arrows, or turn the
    /// selected object. Every tool selects with a click.
    /// </summary>
    internal enum EditorTool { Drag, Move, Rotate, Road, Rows, Terrain, Markings, Priorities, Lanes }

    /// <summary>
    /// The tool bar at the bottom of the map: grouped buttons with an icon, a name and their key, as the corporate
    /// design describes. It shows finished tools only; the shell decides what each button does and which is active.
    /// </summary>
    internal sealed class MapToolbar : VisualElement
    {
        private readonly Dictionary<EditorTool, Button> toolButtons = new Dictionary<EditorTool, Button>();
        private readonly Button deleteButton;
        private readonly Button objectsButton;

        public MapToolbar()
        {
            AddToClassList("tool-bar");

            var selection = AddGroup();
            AddTool(selection, EditorTool.Drag, "mouse-pointer-2", "toolbar.drag", "Q", "toolbar.drag.tooltip");
            AddTool(selection, EditorTool.Move, "move", "toolbar.move", "W", "toolbar.move.tooltip");
            AddTool(selection, EditorTool.Rotate, "rotate-cw", "toolbar.rotate", "E", "toolbar.rotate.tooltip");
            deleteButton = AddButton(selection, "trash-2", "toolbar.delete", "Del", "toolbar.delete.tooltip", () => DeleteRequested?.Invoke());

            AddSeparator();
            var world = AddGroup();
            AddTool(world, EditorTool.Road, "spline", "toolbar.road", "R", "toolbar.road.tooltip");
            AddTool(world, EditorTool.Rows, "fence", "toolbar.rows", "T", "toolbar.rows.tooltip");
            AddTool(world, EditorTool.Terrain, "mountain", "toolbar.terrain", "G", "toolbar.terrain.tooltip");
            AddTool(world, EditorTool.Markings, "brush", "toolbar.markings", "M", "toolbar.markings.tooltip");
            AddTool(world, EditorTool.Priorities, "diamond", "toolbar.priority", "V", "toolbar.priority.tooltip");
            AddTool(world, EditorTool.Lanes, "split", "toolbar.lanes", "L", "toolbar.lanes.tooltip");
            objectsButton = AddButton(world, "box", "toolbar.objects", "3", "toolbar.objects.tooltip", () => ObjectsRequested?.Invoke());
        }

        public event Action<EditorTool> ToolChosen;
        public event Action DeleteRequested;
        public event Action ObjectsRequested;

        /// <summary>Highlights the active tool; while placing, the Objects button is active instead.</summary>
        /// <param name="canEdit">Every tool works, as on the base map of a project.</param>
        /// <param name="canEditObjects">Only objects can be edited, as in a chrono variant: Drag, Move, Rotate, Objects and Delete.</param>
        public void ShowState(EditorTool tool, bool placing, bool canEdit, bool canEditObjects, bool hasSelection)
        {
            foreach (var pair in toolButtons)
            {
                pair.Value.EnableInClassList("tool-button-active", !placing && pair.Key == tool);
                pair.Value.SetEnabled(canEdit || pair.Key == EditorTool.Drag ||
                                      canEditObjects && (pair.Key == EditorTool.Move || pair.Key == EditorTool.Rotate || pair.Key == EditorTool.Priorities || pair.Key == EditorTool.Road));
            }
            objectsButton.EnableInClassList("tool-button-active", placing);
            objectsButton.SetEnabled(canEditObjects);
            deleteButton.SetEnabled(canEditObjects && hasSelection);
        }

        private VisualElement AddGroup()
        {
            var group = new VisualElement();
            group.AddToClassList("tool-group");
            Add(group);
            return group;
        }

        private void AddSeparator()
        {
            var separator = new VisualElement();
            separator.AddToClassList("tool-separator");
            Add(separator);
        }

        private void AddTool(VisualElement group, EditorTool tool, string icon, string name, string key, string description)
        {
            toolButtons[tool] = AddButton(group, icon, name, key, description, () => ToolChosen?.Invoke(tool));
        }

        private static Button AddButton(VisualElement group, string icon, string nameKey, string key, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            LocBindings.Tooltip(button, tooltipKey); // i18n:keys
            button.AddToClassList("tool-button");
            var iconElement = new IconElement(icon);
            iconElement.AddToClassList("tool-icon");
            button.Add(iconElement);
            var label = LocBindings.NewLabel(nameKey); // i18n:keys
            label.AddToClassList("tool-label");
            button.Add(label);
            var keycap = new Label(key);
            keycap.AddToClassList("tool-keycap");
            button.Add(keycap);
            group.Add(button);
            return button;
        }
    }
}
