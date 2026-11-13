using System;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The tool table: one entry per <see cref="EditorTool"/> with what the shell does when the tool begins and ends, on Esc,
    /// and with the pointer in the map's road mode. <see cref="SetTool"/>, <see cref="PresentSummary"/>, the shortcuts and the
    /// road-mode events all read it, so a new tool is entered here once. The rules that need no shell are in
    /// <see cref="EditorToolRules"/>.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private sealed class ToolEntry
        {
            public EditorTool Tool;

            /// <summary>Starts the tool's mode; null for Drag, Move and Rotate, which have none.</summary>
            public Action Begin;

            /// <summary>Ends the tool's mode. It runs after <c>activeTool</c> names the next tool.</summary>
            public Action End;

            /// <summary>Esc while the tool is on; returns whether it took the key. Null leaves Esc to the shell.</summary>
            public Func<bool> Escape;

            /// <summary>The pointer moved in the map's road mode.</summary>
            public Action PointerMoved;

            /// <summary>A click in the map's road mode. Null: the tool takes no clicks there, and nothing is built.</summary>
            public Action<double, double> Clicked;

            /// <summary>The left button was let go in the map's road mode.</summary>
            public Action Released;
        }

        private ToolEntry[] toolTable;

        /// <summary>The entry of a tool; the table is made on first use, as its actions are methods of this shell.</summary>
        private ToolEntry ToolEntryOf(EditorTool tool)
        {
            if (toolTable == null)
                toolTable = BuildToolTable();
            return toolTable[(int)tool];
        }

        private ToolEntry[] BuildToolTable()
        {
            var tools = (EditorTool[])Enum.GetValues(typeof(EditorTool));
            if (tools.Length != EditorToolRules.All.Length)
                throw new InvalidOperationException("EditorToolRules.All lists " + EditorToolRules.All.Length + " tools, the enum has " + tools.Length + ".");
            for (var index = 0; index < tools.Length; index++)
            {
                if ((int)tools[index] != index || EditorToolRules.All[index] != tools[index])
                    throw new InvalidOperationException("EditorToolRules.All does not list the tool " + tools[index] + " at " + index + ", in the order of the enum.");
            }
            var table = new ToolEntry[tools.Length];
            Func<bool> toDrag = EscapeToDrag;
            Action<double, double> roadClick = BuildRoadTo;
            table[(int)EditorTool.Drag] = new ToolEntry { Tool = EditorTool.Drag };
            table[(int)EditorTool.Move] = new ToolEntry { Tool = EditorTool.Move, Escape = toDrag };
            table[(int)EditorTool.Rotate] = new ToolEntry { Tool = EditorTool.Rotate, Escape = toDrag };
            table[(int)EditorTool.Road] = new ToolEntry
            {
                Tool = EditorTool.Road, Begin = BeginRoadTool, End = EndRoadTool, Escape = EscapeRoadTool,
                PointerMoved = UpdateRoadPreview, Clicked = roadClick,
            };
            table[(int)EditorTool.Rows] = new ToolEntry
            {
                Tool = EditorTool.Rows, Begin = BeginRowTool, End = EndRowTool, Escape = EscapeRowTool,
                PointerMoved = UpdateRowPreview, Clicked = ClickRow,
            };
            table[(int)EditorTool.Terrain] = new ToolEntry
            {
                Tool = EditorTool.Terrain, Begin = BeginTerrainTool, End = EndTerrainTool, Escape = EscapeTerrainTool,
                PointerMoved = ShowTerrainBrush, Clicked = PressTerrain, Released = ReleaseTerrain,
            };
            table[(int)EditorTool.Markings] = new ToolEntry
            {
                Tool = EditorTool.Markings, Begin = BeginMarkingTool, End = EndMarkingTool, Escape = EscapeMarkingTool,
                PointerMoved = UpdateMarkingPreview, Clicked = ClickMarking,
            };
            table[(int)EditorTool.Priorities] = new ToolEntry
            {
                Tool = EditorTool.Priorities, Begin = BeginPriorityTool, End = EndPriorityTool, Escape = toDrag,
                PointerMoved = UpdatePriorityPreview, Clicked = ClickPriority,
            };
            table[(int)EditorTool.Lanes] = new ToolEntry
            {
                Tool = EditorTool.Lanes, Begin = BeginLaneTool, End = EndLaneTool, Escape = EscapeLaneTool,
                PointerMoved = UpdateLanePreview, Clicked = ClickLane,
            };
            table[(int)EditorTool.Signals] = new ToolEntry
            {
                Tool = EditorTool.Signals, Begin = BeginSignalTool, End = EndSignalTool, Escape = toDrag,
                PointerMoved = UpdateSignalPlan, Clicked = ClickSignal,
            };
            table[(int)EditorTool.Line] = new ToolEntry
            {
                Tool = EditorTool.Line, Begin = BeginLineTool, End = EndLineTool, Escape = toDrag,
                PointerMoved = UpdateLinePreview, Clicked = ClickLine,
            };
            for (var index = 0; index < table.Length; index++)
            {
                if (table[index] == null || (int)table[index].Tool != index)
                    throw new InvalidOperationException("The tool table has no entry for " + (EditorTool)index + ".");
                if ((table[index].Clicked != null) != EditorToolRules.TakesRoadModeClicks(table[index].Tool))
                    throw new InvalidOperationException("The tool table and EditorToolRules disagree on whether " + table[index].Tool + " takes clicks in the map's road mode.");
                if ((table[index].Clicked == roadClick) != EditorToolRules.BuildsRoadOnClick(table[index].Tool))
                    throw new InvalidOperationException("Only the road tool may build a road on a click; the table says otherwise for " + table[index].Tool + ".");
            }
            return table;
        }

        /// <summary>
        /// Ends the active tool because another map or view is shown: <c>activeTool</c> becomes Drag first, then the tool's end
        /// runs, as the end methods read <c>activeTool</c>. Move and Rotate have no end and only become Drag.
        /// </summary>
        private void EndToolForView()
        {
            var leaving = activeTool;
            activeTool = EditorTool.Drag;
            ToolEntryOf(leaving).End?.Invoke();
        }

        /// <summary>Esc with the active tool, if it has an Esc of its own; returns whether the key was taken.</summary>
        private bool EscapeActiveTool()
        {
            var escape = ToolEntryOf(activeTool).Escape;
            return escape != null && escape();
        }

        /// <summary>
        /// The tool chosen by a key without Ctrl, if the key acts now: the keys that switch with Drag only while nothing is
        /// placed, those of Drag, Move and Rotate always.
        /// </summary>
        private bool TryGetToolOfShortcut(KeyCode key, out EditorTool tool)
        {
            if (!EditorToolRules.TryGetToolOfKey(key, out tool))
                return false;
            return !EditorToolRules.KeyTogglesWithDrag(tool) || mapCanvas != null && !mapCanvas.IsPlacing;
        }

        /// <summary>The pointer moved in the map's road mode: the active tool follows it.</summary>
        private void OnToolPointerMoved()
        {
            ToolEntryOf(activeTool).PointerMoved?.Invoke();
        }

        private void OnToolReleased()
        {
            ToolEntryOf(activeTool).Released?.Invoke();
        }

        /// <summary>A click in the map's road mode goes to the active tool; a tool without clicks there builds nothing.</summary>
        private void OnToolClicked(double x, double y)
        {
            var clicked = ToolEntryOf(activeTool).Clicked;
            if (clicked != null)
                clicked(x, y);
            else
                Debug.LogWarning("The map's road mode got a click while the " + activeTool + " tool is active, which takes no clicks there; nothing is done.");
        }

        private bool EscapeToDrag()
        {
            SetTool(EditorTool.Drag);
            return true;
        }

        /// <summary>Esc with the rows tool: lets go of the row begun or selected, then leaves the tool.</summary>
        private bool EscapeRowTool()
        {
            if (rowStart != null || selectedRow != null)
            {
                rowStart = null;
                DeselectRow();
                UpdateRowPreview();
                SetStatusT("rows.status.idle");
            }
            else
                SetTool(EditorTool.Drag);
            return true;
        }

        private bool EscapeMarkingTool()
        {
            EscapeMarking();
            return true;
        }

        private bool EscapeLaneTool()
        {
            EscapeLane();
            return true;
        }

        /// <summary>Esc with the terrain tool: lets go of the start of a ramp, then leaves the tool.</summary>
        private bool EscapeTerrainTool()
        {
            if (terrainRampStart.HasValue)
            {
                terrainRampStart = null;
                ShowTerrainBrush();
                SetStatusT("terrain.status.ramp_start");
            }
            else
                SetTool(EditorTool.Drag);
            return true;
        }

        /// <summary>
        /// Esc with the road tool, one step at a time: the pick of a row object, the road builder, the control point, the road
        /// begun, then the tool.
        /// </summary>
        private bool EscapeRoadTool()
        {
            if (pickingRowObject)
                EndRowObjectPick(null);
            else if (RoadBuilderShown)
            {
                ShowRoadBuilder(false);
                SetStatusT("roadbuilder.status.closed");
            }
            else if (roadControl.HasValue)
            {
                roadControl = null;
                UpdateRoadPreview();
                SetStatusT("roadbuild.status.control_back");
            }
            else if (roadStarted)
            {
                EndRoad();
                SetStatusT("roadbuild.status.ended");
            }
            else
                SetTool(EditorTool.Drag);
            return true;
        }
    }
}
