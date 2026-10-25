using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// What a left drag on the map does: drag the selection freely, move it freely or along axis arrows, or turn the
    /// selected object. Every tool selects with a click.
    /// </summary>
    internal enum EditorTool { Drag, Move, Rotate, Road, Rows, Terrain }

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
            AddTool(selection, EditorTool.Drag, "mouse-pointer-2", "Drag", "Q",
                "Click objects to select them and drag them to move them freely; Ctrl+click and Ctrl+drag select several.");
            AddTool(selection, EditorTool.Move, "move", "Move", "W",
                "Move the selection along axis arrows: east, north and in 3D up. Shift moves in whole metres.");
            AddTool(selection, EditorTool.Rotate, "rotate-cw", "Rotate", "E",
                "Turn the selected object: the round handle from above, the rings in 3D. Shift snaps to 15°.");
            deleteButton = AddButton(selection, "trash-2", "Delete", "Del",
                "Delete the selection. Objects that other parts of the map refer to stay.", () => DeleteRequested?.Invoke());

            AddSeparator();
            var world = AddGroup();
            AddTool(world, EditorTool.Road, "spline", "Road", "R",
                "Build roads from the view from above: click to start, click for each bend, Esc ends. Roads join free spline ends.");
            AddTool(world, EditorTool.Rows, "fence", "Rows", "T",
                "Lay rows of objects along roads, such as lamps or posts: choose an object, point at a road and click.");
            AddTool(world, EditorTool.Terrain, "mountain", "Terrain", "G",
                "Shape the ground with a brush held over it, raising, lowering, smoothing, flattening or roughening it, or lay a ramp between two clicks.");
            objectsButton = AddButton(world, "box", "Objects", "3",
                "Place scenery objects: choose one, then click into the map. Shift+click keeps placing, R turns it, Esc stops.",
                () => ObjectsRequested?.Invoke());
        }

        public event Action<EditorTool> ToolChosen;
        public event Action DeleteRequested;
        public event Action ObjectsRequested;

        /// <summary>Highlights the active tool; while placing, the Objects button is active instead.</summary>
        public void ShowState(EditorTool tool, bool placing, bool canEdit, bool hasSelection)
        {
            foreach (var pair in toolButtons)
            {
                pair.Value.EnableInClassList("tool-button-active", !placing && pair.Key == tool);
                pair.Value.SetEnabled(canEdit || pair.Key == EditorTool.Drag);
            }
            objectsButton.EnableInClassList("tool-button-active", placing);
            objectsButton.SetEnabled(canEdit);
            deleteButton.SetEnabled(canEdit && hasSelection);
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

        private static Button AddButton(VisualElement group, string icon, string name, string key, string description, Action clicked)
        {
            var button = new Button(clicked) { tooltip = name + " (" + key + "): " + description };
            button.AddToClassList("tool-button");
            var iconElement = new IconElement(icon);
            iconElement.AddToClassList("tool-icon");
            button.Add(iconElement);
            var label = new Label(name);
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
