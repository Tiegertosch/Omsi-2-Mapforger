using System.Collections.Generic;

namespace OmsiMapEditor.UI
{
    /// <summary>What Esc can do in the shell; the shell maps each to what it does.</summary>
    internal enum EscapeAction
    {
        /// <summary>Nothing to do; the key goes on to the map view.</summary>
        None,
        CloseContextMenu,
        /// <summary>Closes the topmost dialog, window or list popup, if there is one.</summary>
        CloseTopOverlay,
        /// <summary>The object search field handles the key itself.</summary>
        LeaveToSearchField,
        EndVarParentPick,
        /// <summary>Stops picking a signal of a signal route from the map (the pipette of the signal routes list).</summary>
        EndSignalRoutePick,
        /// <summary>Ends picking a signal or target for a new signal route if one is picked, else drops the new route being planned.</summary>
        CancelSignalRouteNew,
        EndElementTypePick,
        EndSplineTypePick,
        /// <summary>Esc of the active tool (the tool table), if the tool has something to end.</summary>
        EscapeActiveTool,
        /// <summary>Stops placing without a status text (Esc in the object library).</summary>
        StopPlacement,
        /// <summary>Stops placing and says so in the status bar.</summary>
        StopPlacementWithStatus,
        CloseObjectsPanel,
        /// <summary>Closes the inspector window (M5 step 3), before the selection is let go of.</summary>
        CloseInspector,
        ClearSelection,
    }

    /// <summary>What Esc does inside the terrain tool, one step at a time.</summary>
    internal enum TerrainEscapeStep
    {
        /// <summary>Lets go of covering or clearing tiles by click.</summary>
        DisarmTileMode,
        /// <summary>Lets go of the start of a ramp.</summary>
        ReleaseRampStart,
        /// <summary>Nothing is under way: the tool ends.</summary>
        LeaveTool,
    }

    /// <summary>The order of Esc inside the terrain tool (ADR 098, M4): the armed tile mode first, then a begun ramp, then the tool.</summary>
    internal static class TerrainEscape
    {
        public static TerrainEscapeStep Next(bool tileModeArmed, bool rampStarted)
        {
            return tileModeArmed ? TerrainEscapeStep.DisarmTileMode : rampStarted ? TerrainEscapeStep.ReleaseRampStart : TerrainEscapeStep.LeaveTool;
        }
    }

    /// <summary>What Esc does inside the traffic rules tool, one step at a time.</summary>
    internal enum TrafficEscapeStep
    {
        /// <summary>Takes the sign from the pointer: a sign being dragged from a tile goes back.</summary>
        CancelDrag,
        /// <summary>Lets go of the armed sign, so clicks in the map select again.</summary>
        DisarmSign,
        /// <summary>Nothing is under way: the tool ends.</summary>
        LeaveTool,
    }

    /// <summary>The order of Esc inside the traffic rules tool (ADR 098, M4): a drag first, then the armed sign, then the tool.</summary>
    internal static class TrafficEscape
    {
        public static TrafficEscapeStep Next(bool dragging, bool signArmed)
        {
            return dragging ? TrafficEscapeStep.CancelDrag : signArmed ? TrafficEscapeStep.DisarmSign : TrafficEscapeStep.LeaveTool;
        }
    }

    /// <summary>What Esc does inside the line tool, one step at a time.</summary>
    internal enum LineEscapeStep
    {
        /// <summary>Lets go of the selected stop of the route strip.</summary>
        ClearStopSelection,
        /// <summary>Nothing is selected: the tool ends.</summary>
        LeaveTool,
    }

    /// <summary>The order of Esc inside the line tool (ADR 098, M4): the selected stop first, then the tool.</summary>
    internal static class LineEscape
    {
        public static LineEscapeStep Next(bool stopSelected)
        {
            return stopSelected ? LineEscapeStep.ClearStopSelection : LineEscapeStep.LeaveTool;
        }
    }

    /// <summary>What the shell knows when Esc is pressed.</summary>
    internal struct EscapeState
    {
        public bool ContextMenuOpen;
        public bool SearchHoldsFocus;
        public bool FocusInObjectLibrary;
        public bool PickingElementType;
        public bool PickingVarParent;
        /// <summary>A signal of a signal route waits to be clicked in the map.</summary>
        public bool PickingSignalRoute;
        /// <summary>A new signal route is being planned in the popup (M4 interface), with or without a point waiting to be clicked.</summary>
        public bool PlanningSignalRoute;
        public bool PickingSplineType;
        /// <summary>An object file is chosen for placing (the placement asset is set).</summary>
        public bool PlacementAssetChosen;
        /// <summary>The map view is placing.</summary>
        public bool MapPlacing;
        public bool TypingInField;
        public bool ModalDialogShown;
        /// <summary>The window for pictures from the air is shown and waits for a point to be clicked in the map.</summary>
        public bool SatelliteWaitsForPick;
        /// <summary>Ctrl (or Cmd) is held.</summary>
        public bool ControlHeld;
        public bool ObjectsPanelOpen;
        /// <summary>The Objects window is in Row mode (the rows tool is active); it is not <see cref="ObjectsPanelOpen"/> then.</summary>
        public bool RowToolActive;
        public bool SelectionNotEmpty;
        /// <summary>The inspector window is open and nothing is being dragged in the map (a drag takes Esc to cancel itself).</summary>
        public bool InspectorShown;
    }

    /// <summary>
    /// The order in which Esc is offered to what is open (ADR 098, M3). Pure: the shell builds the state, asks for the order and tries
    /// the actions one after the other until one takes the key.
    /// </summary>
    internal static class EscapeOrder
    {
        /// <summary>
        /// The actions to try, in order. An action that <see cref="CanDecline"/> may not find anything to do; then the next one is tried.
        /// The last action never declines. The state is that before the first action: a declining action changes nothing.
        /// </summary>
        public static List<EscapeAction> Order(EscapeState state)
        {
            var order = new List<EscapeAction>();
            if (state.ContextMenuOpen)
            {
                order.Add(EscapeAction.CloseContextMenu);
                return order;
            }
            // The pipette of the signal routes list comes before the popup it belongs to, which Esc would close otherwise.
            if (state.PickingSignalRoute && !state.ModalDialogShown)
            {
                order.Add(EscapeAction.EndSignalRoutePick);
                return order;
            }
            // So does planning a new route: Esc stops a pick first and then drops the plan, and does not close the popup that shows it.
            if (state.PlanningSignalRoute && !state.ModalDialogShown)
            {
                order.Add(EscapeAction.CancelSignalRouteNew);
                return order;
            }
            order.Add(EscapeAction.CloseTopOverlay);
            if (state.SearchHoldsFocus)
            {
                order.Add(EscapeAction.LeaveToSearchField);
                return order;
            }
            if (state.FocusInObjectLibrary)
            {
                if (state.PickingElementType)
                    order.Add(EscapeAction.EndElementTypePick);
                else if (state.RowToolActive)
                {
                    // In Row mode the rows tool's own Esc comes first: let go of the row begun or selected, then leave the tool.
                    order.Add(EscapeAction.EscapeActiveTool);
                    order.Add(EscapeAction.CloseObjectsPanel);
                }
                else
                    order.Add(state.PlacementAssetChosen ? EscapeAction.StopPlacement : EscapeAction.CloseObjectsPanel);
                return order;
            }
            if (state.TypingInField || state.ModalDialogShown || state.SatelliteWaitsForPick || state.ControlHeld)
            {
                order.Add(EscapeAction.None);
                return order;
            }
            if (state.PickingVarParent)
                order.Add(EscapeAction.EndVarParentPick);
            else if (state.PickingElementType)
                order.Add(EscapeAction.EndElementTypePick);
            else if (state.PickingSplineType)
                order.Add(EscapeAction.EndSplineTypePick);
            else
            {
                order.Add(EscapeAction.EscapeActiveTool);
                order.Add(state.MapPlacing ? EscapeAction.StopPlacementWithStatus
                    : state.ObjectsPanelOpen ? EscapeAction.CloseObjectsPanel
                    : state.InspectorShown ? EscapeAction.CloseInspector
                    : state.SelectionNotEmpty ? EscapeAction.ClearSelection
                    : EscapeAction.None);
            }
            return order;
        }

        /// <summary>Whether the action may find nothing to do and leave the key to the next one.</summary>
        public static bool CanDecline(EscapeAction action)
        {
            return action == EscapeAction.CloseTopOverlay || action == EscapeAction.EscapeActiveTool;
        }

        /// <summary>
        /// Whether the key stops here once the action took it. Not for the search field, which handles it itself, nor for the selection:
        /// the map view also cancels a drag in progress.
        /// </summary>
        public static bool StopsPropagation(EscapeAction action)
        {
            return action != EscapeAction.None && action != EscapeAction.LeaveToSearchField && action != EscapeAction.ClearSelection;
        }
    }
}
