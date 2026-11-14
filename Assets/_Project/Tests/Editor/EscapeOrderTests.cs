using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The order in which Esc is offered to what is open (ADR 098, M3).</summary>
    public sealed class EscapeOrderTests
    {
        private static EscapeAction[] Order(EscapeState state)
        {
            return EscapeOrder.Order(state).ToArray();
        }

        [Test]
        public void NothingOpenOffersTheOverlayAndTheToolAndThenHasNothingToDo()
        {
            Assert.That(Order(new EscapeState()), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EscapeActiveTool, EscapeAction.None }));
        }

        [Test]
        public void TheContextMenuComesBeforeEverything()
        {
            var state = new EscapeState { ContextMenuOpen = true, SearchHoldsFocus = true, MapPlacing = true, SelectionNotEmpty = true };

            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseContextMenu }));
        }

        [Test]
        public void TheTopOverlayComesBeforeTheSearchFieldAndTheLibrary()
        {
            Assert.That(Order(new EscapeState { SearchHoldsFocus = true })[0], Is.EqualTo(EscapeAction.CloseTopOverlay));
            Assert.That(Order(new EscapeState { FocusInObjectLibrary = true })[0], Is.EqualTo(EscapeAction.CloseTopOverlay));
            Assert.That(Order(new EscapeState { TypingInField = true })[0], Is.EqualTo(EscapeAction.CloseTopOverlay));
        }

        [Test]
        public void TheSearchFieldKeepsTheKeyAndTheLibraryBranchIsNotReached()
        {
            var state = new EscapeState { SearchHoldsFocus = true, FocusInObjectLibrary = true, PickingElementType = true, TypingInField = true };

            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.LeaveToSearchField }));
        }

        [Test]
        public void InTheLibraryTheElementTypePickEndsFirstThenPlacingThenThePanelCloses()
        {
            var inLibrary = new EscapeState { FocusInObjectLibrary = true, TypingInField = true, ModalDialogShown = true, ControlHeld = true };

            Assert.That(Order(inLibrary), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.CloseObjectsPanel }));
            inLibrary.PlacementAssetChosen = true;
            Assert.That(Order(inLibrary), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.StopPlacement }));
            inLibrary.PickingElementType = true;
            Assert.That(Order(inLibrary), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EndElementTypePick }));
        }

        [Test]
        public void TypingInAFieldAModalDialogAndTheWaitingSatelliteWindowLeaveTheKey()
        {
            foreach (var state in new[]
            {
                new EscapeState { TypingInField = true, PickingVarParent = true, MapPlacing = true },
                new EscapeState { ModalDialogShown = true, PickingSplineType = true, SelectionNotEmpty = true },
                new EscapeState { SatelliteWaitsForPick = true, ObjectsPanelOpen = true },
            })
                Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.None }));
        }

        [Test]
        public void ThePicksEndBeforeTheToolInTheirOrder()
        {
            var state = new EscapeState { PickingVarParent = true, PickingElementType = true, PickingSplineType = true, MapPlacing = true };

            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EndVarParentPick }));
            state.PickingVarParent = false;
            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EndElementTypePick }));
            state.PickingElementType = false;
            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EndSplineTypePick }));
        }

        [Test]
        public void WithCtrlThePicksTheToolAndTheRestIgnoreTheKey()
        {
            var state = new EscapeState
            {
                ControlHeld = true, PickingVarParent = true, PickingElementType = true, PickingSplineType = true,
                MapPlacing = true, ObjectsPanelOpen = true, SelectionNotEmpty = true,
            };

            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.None }));
        }

        [Test]
        public void TheToolComesBeforePlacingPlacingBeforeThePanelAndThePanelBeforeTheSelection()
        {
            var state = new EscapeState { MapPlacing = true, ObjectsPanelOpen = true, SelectionNotEmpty = true };

            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EscapeActiveTool, EscapeAction.StopPlacementWithStatus }));
            state.MapPlacing = false;
            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EscapeActiveTool, EscapeAction.CloseObjectsPanel }));
            state.ObjectsPanelOpen = false;
            Assert.That(Order(state), Is.EqualTo(new[] { EscapeAction.CloseTopOverlay, EscapeAction.EscapeActiveTool, EscapeAction.ClearSelection }));
        }

        [Test]
        public void OnlyTheOverlayAndTheToolCanDeclineAndEveryOrderEndsWithAnActionThatCannot()
        {
            foreach (EscapeAction action in Enum.GetValues(typeof(EscapeAction)))
            {
                var declines = action == EscapeAction.CloseTopOverlay || action == EscapeAction.EscapeActiveTool;
                Assert.That(EscapeOrder.CanDecline(action), Is.EqualTo(declines), action.ToString());
            }

            for (var bits = 0; bits < 1 << 14; bits++)
            {
                var state = new EscapeState
                {
                    ContextMenuOpen = (bits & 1) != 0, SearchHoldsFocus = (bits & 2) != 0, FocusInObjectLibrary = (bits & 4) != 0,
                    PickingElementType = (bits & 8) != 0, PickingVarParent = (bits & 16) != 0, PickingSplineType = (bits & 32) != 0,
                    PlacementAssetChosen = (bits & 64) != 0, MapPlacing = (bits & 128) != 0, TypingInField = (bits & 256) != 0,
                    ModalDialogShown = (bits & 512) != 0, SatelliteWaitsForPick = (bits & 1024) != 0, ControlHeld = (bits & 2048) != 0,
                    ObjectsPanelOpen = (bits & 4096) != 0, SelectionNotEmpty = (bits & 8192) != 0,
                };
                var order = EscapeOrder.Order(state);
                Assert.That(EscapeOrder.CanDecline(order[order.Count - 1]), Is.False, bits.ToString());
            }
        }

        [Test]
        public void OnlyNothingTheSearchFieldAndTheSelectionLetTheKeyGoOn()
        {
            foreach (EscapeAction action in Enum.GetValues(typeof(EscapeAction)))
            {
                var goesOn = action == EscapeAction.None || action == EscapeAction.LeaveToSearchField || action == EscapeAction.ClearSelection;
                Assert.That(EscapeOrder.StopsPropagation(action), Is.EqualTo(!goesOn), action.ToString());
            }
        }

        [Test]
        public void TheTerrainToolLetsGoOfTheArmedTileModeFirstThenOfARampThenEndsTheTool()
        {
            Assert.That(TerrainEscape.Next(true, false), Is.EqualTo(TerrainEscapeStep.DisarmTileMode));
            Assert.That(TerrainEscape.Next(true, true), Is.EqualTo(TerrainEscapeStep.DisarmTileMode));
            Assert.That(TerrainEscape.Next(false, true), Is.EqualTo(TerrainEscapeStep.ReleaseRampStart));
            Assert.That(TerrainEscape.Next(false, false), Is.EqualTo(TerrainEscapeStep.LeaveTool));
        }
    }
}
