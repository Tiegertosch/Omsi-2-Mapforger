using System.Collections.Generic;
using System.Linq;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The line tool's route strip (ADR 098, M4): it shows the list of the direction chosen at the left (the outbound or the return list, which stay two
    /// separate lists as before); a click in the map appends to the end of that list, and the strip moves, removes and selects by place in it.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private int lineStripSelected = -1;

        /// <summary>Nothing is drafted any more (after saving, or when a trip is let go).</summary>
        private void ResetLineDraft()
        {
            lineOut.Clear();
            lineBack.Clear();
            lineStripSelected = -1;
        }

        /// <summary>A stop clicked in the map: it goes to the end of the list being made.</summary>
        private void LineAddStop(OmsiTripStopChoice stop)
        {
            CurrentLineList.Add(stop);
            lineOptions.Reveal(CurrentLineList.Count - 1);
        }

        /// <summary>A waypoint clicked in the map: after the last place of the list being made, as the stops are.</summary>
        private void LineAddWaypoint(OmsiTripStopChoice waypoint)
        {
            CurrentLineList.Add(waypoint);
            lineOptions.Reveal(CurrentLineList.Count - 1);
        }

        /// <summary>The item at a place moves to another (dragged, one place by the arrows, or by Ctrl with the arrow keys).</summary>
        private void MoveLineStop(int from, int to)
        {
            if (!LineStrip.Move(CurrentLineList, from, to))
                return;
            lineStripSelected = LineStrip.SelectionAfterMove(lineStripSelected, from, to);
            lineOptions.Reveal(to);
            LineChanged(true);
        }

        private void RemoveLineStop(int index)
        {
            if (index < 0 || index >= CurrentLineList.Count)
                return;
            CurrentLineList.RemoveAt(index);
            lineStripSelected = LineStrip.SelectionAfterRemove(lineStripSelected, index);
            LineChanged(true);
        }

        /// <summary>"Copy reversed": the stops of the outbound trip backwards are the return trip, which is shown then.</summary>
        private void CopyLineReversed()
        {
            lineBack.Clear();
            for (var index = lineOut.Count - 1; index >= 0; index--)
                lineBack.Add(lineOut[index]);
            lineRing = false;
            lineBackMode = true;
            lineStripSelected = -1;
            LineChanged(true);
        }

        private void ClearLineStops()
        {
            CurrentLineList.Clear();
            if (!lineBackMode)
                lineRing = false;
            lineStripSelected = -1;
            LineChanged(true);
        }

        /// <summary>A stop of the strip was clicked or reached by the keys: selected, and the map centred on it with the zoom kept.</summary>
        private void SelectLineStop(int index, bool centre)
        {
            if (index < 0 || index >= CurrentLineList.Count)
                return;
            lineStripSelected = index;
            lineOptions.SetSelected(index);
            lineOptions.ScrollTo(index);
            if (centre && mapCanvas != null)
            {
                var item = CurrentLineList[index];
                mapCanvas.CenterOnWorld(item.World, false);
            }
            DrawLine();
        }

        /// <summary>
        /// The keys of the strip while the line tool is on: Left and Right move the selection (the first or last stop when none is selected), Ctrl with
        /// them moves the selected stop, Delete takes it out. False when the key is not for the strip.
        /// </summary>
        private bool LineStripKey(KeyCode key, bool control)
        {
            if (activeTool != EditorTool.Line || lineOptions == null)
                return false;
            var count = CurrentLineList.Count;
            // While a stop is dragged the places are fixed; the keys do nothing then (and Delete never reaches the map's selection).
            if (lineOptions.Interacting && (key == KeyCode.Delete || key == KeyCode.LeftArrow || key == KeyCode.RightArrow))
                return true;
            if (key == KeyCode.Delete)
            {
                // Always consumed in the line tool: it takes out the selected stop or does nothing, never the objects selected in the map.
                var index = control ? -1 : LineStrip.DeleteIndex(lineStripSelected, count);
                if (index >= 0)
                    RemoveLineStop(index);
                return true;
            }
            if ((key != KeyCode.LeftArrow && key != KeyCode.RightArrow) || count == 0)
                return false;
            var delta = key == KeyCode.RightArrow ? 1 : -1;
            if (control)
            {
                if (lineStripSelected < 0)
                    return false;
                var to = LineStrip.StepTarget(lineStripSelected, delta, count);
                if (to >= 0)
                    MoveLineStop(lineStripSelected, to);
                return true;
            }
            var next = lineStripSelected < 0 ? (delta > 0 ? 0 : count - 1) : Mathf.Clamp(lineStripSelected + delta, 0, count - 1);
            SelectLineStop(next, true);
            return true;
        }

        /// <summary>Esc with the line tool: lets go of the selected stop of the strip, then leaves the tool.</summary>
        private bool EscapeLineTool()
        {
            // Esc during a drag only lets the drag go.
            if (lineOptions != null && lineOptions.CancelInteraction())
                return true;
            if (LineEscape.Next(lineStripSelected >= 0) == LineEscapeStep.ClearStopSelection)
            {
                lineStripSelected = -1;
                lineOptions?.SetSelected(-1);
                DrawLine();
            }
            else
                SetTool(EditorTool.Drag);
            return true;
        }
    }
}
