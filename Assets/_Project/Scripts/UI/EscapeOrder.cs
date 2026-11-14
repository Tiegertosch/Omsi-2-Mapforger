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
        EndElementTypePick,
        EndSplineTypePick,
        /// <summary>Esc of the active tool (the tool table), if the tool has something to end.</summary>
        EscapeActiveTool,
        /// <summary>Stops placing without a status text (Esc in the object library).</summary>
        StopPlacement,
        /// <summary>Stops placing and says so in the status bar.</summary>
        StopPlacementWithStatus,
        CloseObjectsPanel,
        ClearSelection,
    }

    /// <summary>What the shell knows when Esc is pressed.</summary>
    internal struct EscapeState
    {
        public bool ContextMenuOpen;
        public bool SearchHoldsFocus;
        public bool FocusInObjectLibrary;
        public bool PickingElementType;
        public bool PickingVarParent;
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
        public bool SelectionNotEmpty;
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
            order.Add(EscapeAction.CloseTopOverlay);
            if (state.SearchHoldsFocus)
            {
                order.Add(EscapeAction.LeaveToSearchField);
                return order;
            }
            if (state.FocusInObjectLibrary)
            {
                order.Add(state.PickingElementType ? EscapeAction.EndElementTypePick
                    : state.PlacementAssetChosen ? EscapeAction.StopPlacement
                    : EscapeAction.CloseObjectsPanel);
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
