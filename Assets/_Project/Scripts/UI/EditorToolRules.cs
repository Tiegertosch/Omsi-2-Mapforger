using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The rules of the editor's tools that need no shell: the key that chooses a tool, the permission it needs, the read-only
    /// message key, and how choosing and leaving it behave. The shell's tool table (<c>EditorShellTools.cs</c>) reads these and
    /// holds only what needs the shell, the actions.
    /// </summary>
    internal static class EditorToolRules
    {
        /// <summary>Every tool, in the order of the enum.</summary>
        public static readonly EditorTool[] All =
        {
            EditorTool.Drag, EditorTool.Move, EditorTool.Rotate, EditorTool.Road, EditorTool.Rows, EditorTool.Terrain, EditorTool.Markings,
            EditorTool.Priorities, EditorTool.Lanes, EditorTool.Signals, EditorTool.Line,
        };

        /// <summary>The key that chooses the tool (without Ctrl).</summary>
        public static KeyCode KeyOf(EditorTool tool)
        {
            switch (tool)
            {
                case EditorTool.Drag: return KeyCode.Q;
                case EditorTool.Move: return KeyCode.W;
                case EditorTool.Rotate: return KeyCode.E;
                case EditorTool.Road: return KeyCode.R;
                case EditorTool.Rows: return KeyCode.T;
                case EditorTool.Terrain: return KeyCode.G;
                case EditorTool.Markings: return KeyCode.M;
                case EditorTool.Priorities: return KeyCode.V;
                case EditorTool.Lanes: return KeyCode.L;
                case EditorTool.Signals: return KeyCode.A;
                case EditorTool.Line: return KeyCode.B;
                default: return KeyCode.None;
            }
        }

        /// <summary>The tool a key chooses; false for a key that chooses none.</summary>
        public static bool TryGetToolOfKey(KeyCode key, out EditorTool tool)
        {
            if (key != KeyCode.None)
            {
                for (var index = 0; index < All.Length; index++)
                {
                    if (KeyOf(All[index]) == key)
                    {
                        tool = All[index];
                        return true;
                    }
                }
            }
            tool = EditorTool.Drag;
            return false;
        }

        /// <summary>
        /// Whether the tool's key switches between the tool and Drag and acts only while nothing is being placed. False for Drag,
        /// Move and Rotate: their keys always choose them, also while placing.
        /// </summary>
        public static bool KeyTogglesWithDrag(EditorTool tool)
        {
            return !IsSelectionTool(tool);
        }

        /// <summary>Drag, Move and Rotate: tools that work on the selection and have no mode, panel or status of their own.</summary>
        public static bool IsSelectionTool(EditorTool tool)
        {
            return tool == EditorTool.Drag || tool == EditorTool.Move || tool == EditorTool.Rotate;
        }

        /// <summary>
        /// Whether a click in the map's road mode goes to the tool: every tool with a mode of its own does, Drag, Move and Rotate
        /// take none and the click then does nothing.
        /// </summary>
        public static bool TakesRoadModeClicks(EditorTool tool)
        {
            return !IsSelectionTool(tool);
        }

        /// <summary>Whether a click in the map's road mode builds a road: only with the road tool.</summary>
        public static bool BuildsRoadOnClick(EditorTool tool)
        {
            return tool == EditorTool.Road;
        }

        /// <summary>Whether the tool needs objects to be editable (<c>CanEditObjects</c>); the others need the whole map (<c>CanEdit</c>).</summary>
        public static bool NeedsObjectEditingOnly(EditorTool tool)
        {
            return tool == EditorTool.Drag || tool == EditorTool.Move || tool == EditorTool.Rotate || tool == EditorTool.Priorities || tool == EditorTool.Road;
        }

        /// <summary>Whether the tool may be chosen with these permissions. Drag always may.</summary>
        public static bool IsAllowed(EditorTool tool, bool canEdit, bool canEditObjects)
        {
            return tool == EditorTool.Drag || (NeedsObjectEditingOnly(tool) ? canEditObjects : canEdit);
        }

        /// <summary>The key of the action named in the read-only message when the tool cannot be chosen; null for Drag.</summary>
        public static string ReadOnlyKey(EditorTool tool)
        {
            switch (tool)
            {
                case EditorTool.Drag: return null;
                case EditorTool.Road: return "readonly.action.build_roads";
                case EditorTool.Rows: return "readonly.action.lay_rows";
                case EditorTool.Terrain: return "readonly.action.shape_ground";
                case EditorTool.Markings: return "readonly.action.draw_markings";
                case EditorTool.Priorities: return "readonly.action.right_of_way";
                case EditorTool.Lanes: return "readonly.action.choose_lanes";
                case EditorTool.Signals: return "readonly.action.edit_signals";
                case EditorTool.Line: return "readonly.action.make_lines";
                default: return "readonly.action.move_rotate";
            }
        }

        /// <summary>
        /// Whether the tool begins again when it is chosen while it is already on (Road, Rows, Terrain). The other tools with a
        /// mode of their own only show their panel again then.
        /// </summary>
        public static bool BeginsAgain(EditorTool tool)
        {
            return tool == EditorTool.Road || tool == EditorTool.Rows || tool == EditorTool.Terrain;
        }

        /// <summary>
        /// Whether showing another map or view always ends the tool, as it holds records of the summary shown (Road, Markings,
        /// Priorities, Signals, Lanes). The others end only when the new view cannot be edited with them.
        /// </summary>
        public static bool EndsOnEveryView(EditorTool tool)
        {
            return tool == EditorTool.Road || tool == EditorTool.Markings || tool == EditorTool.Priorities || tool == EditorTool.Signals || tool == EditorTool.Lanes;
        }
    }
}
