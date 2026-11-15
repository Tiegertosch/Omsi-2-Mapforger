using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The rules of the line tool's route strip (ADR 098, M4): a stop moves by its place in the list shown, one place left or right, dragged or by the keys.</summary>
    public sealed class LineStripTests
    {
        private static OmsiTripStopChoice Stop(string name, int id)
        {
            return new OmsiTripStopChoice { Name = name, ObjectId = id };
        }

        private static OmsiTripStopChoice Waypoint(double x, double y)
        {
            return new OmsiTripStopChoice { Name = "waypoint", IsWaypoint = true, World = new OmsiMapPoint(x, y) };
        }

        private static string Names(IEnumerable<OmsiTripStopChoice> items)
        {
            return string.Join(",", items.Select(item => item.IsWaypoint ? "~" : item.Name));
        }

        /// <summary>One step as the arrows and Ctrl with the arrow keys make it.</summary>
        private static bool Step(IList<OmsiTripStopChoice> list, int index, int delta)
        {
            var target = LineStrip.StepTarget(index, delta, list.Count);
            return target >= 0 && LineStrip.Move(list, index, target);
        }

        [Test]
        public void TheRightArrowMovesAStopOnePlaceRight()
        {
            var list = new List<OmsiTripStopChoice> { Stop("A", 1), Stop("B", 2), Stop("C", 3) };

            Assert.That(Step(list, 0, 1), Is.True);
            Assert.That(Names(list), Is.EqualTo("B,A,C"));
        }

        [Test]
        public void TheLeftArrowMovesAStopOnePlaceLeft()
        {
            var list = new List<OmsiTripStopChoice> { Stop("A", 1), Stop("B", 2), Stop("C", 3) };

            Assert.That(Step(list, 2, -1), Is.True);
            Assert.That(Names(list), Is.EqualTo("A,C,B"));
        }

        [Test]
        public void TheFirstStopCannotGoLeftAndTheLastCannotGoRight()
        {
            var list = new List<OmsiTripStopChoice> { Stop("A", 1), Stop("B", 2), Stop("C", 3) };

            Assert.That(LineStrip.StepTarget(0, -1, 3), Is.EqualTo(-1));
            Assert.That(LineStrip.StepTarget(2, 1, 3), Is.EqualTo(-1));
            Assert.That(Step(list, 0, -1), Is.False);
            Assert.That(Step(list, 2, 1), Is.False);
            Assert.That(Names(list), Is.EqualTo("A,B,C"));
        }

        [Test]
        public void ALoneStopCannotMoveEitherWay()
        {
            Assert.That(LineStrip.StepTarget(0, -1, 1), Is.EqualTo(-1));
            Assert.That(LineStrip.StepTarget(0, 1, 1), Is.EqualTo(-1));
        }

        [Test]
        public void AStepOutsideTheListIsNoStep()
        {
            Assert.That(LineStrip.StepTarget(-1, 1, 3), Is.EqualTo(-1));
            Assert.That(LineStrip.StepTarget(3, -1, 3), Is.EqualTo(-1));
        }

        [Test]
        public void TheEndsGiveTheNeighbouringPlaceInTheOtherDirection()
        {
            Assert.That(LineStrip.StepTarget(0, 1, 3), Is.EqualTo(1));
            Assert.That(LineStrip.StepTarget(2, -1, 3), Is.EqualTo(1));
        }

        [Test]
        public void TheSameStopTwiceMovesByPlaceAndNotById()
        {
            var a = Stop("A", 1);
            var b = Stop("B", 2);
            var list = new List<OmsiTripStopChoice> { a, b, a };

            Assert.That(Step(list, 2, -1), Is.True);
            Assert.That(Names(list), Is.EqualTo("A,A,B"));
            Assert.That(Step(list, 0, 1), Is.True);
            Assert.That(Names(list), Is.EqualTo("A,A,B"));
            Assert.That(Step(list, 1, 1), Is.True);
            Assert.That(Names(list), Is.EqualTo("A,B,A"));
        }

        [Test]
        public void TheSelectionStaysOnTheMovedPlaceWhenItIsMovedBySteps()
        {
            var list = new List<OmsiTripStopChoice> { Stop("A", 1), Stop("B", 2), Stop("C", 3) };
            var selected = 1;

            var target = LineStrip.StepTarget(selected, 1, list.Count);
            LineStrip.Move(list, selected, target);
            selected = LineStrip.SelectionAfterMove(selected, selected, target);

            Assert.That(selected, Is.EqualTo(2));
            Assert.That(list[selected].Name, Is.EqualTo("B"));
        }

        [Test]
        public void AWaypointMovesLikeAStop()
        {
            var list = new List<OmsiTripStopChoice> { Stop("A", 1), Waypoint(5, 5), Stop("B", 2) };

            Assert.That(Step(list, 1, 1), Is.True);
            Assert.That(Names(list), Is.EqualTo("A,B,~"));
            Assert.That(Step(list, 2, 1), Is.False);
        }

        [Test]
        public void MovingPutsTheItemAtTheTargetPlace()
        {
            var list = new List<string> { "a", "b", "c", "d" };

            Assert.That(LineStrip.Move(list, 0, 2), Is.True);
            Assert.That(string.Join("", list), Is.EqualTo("bcad"));
            Assert.That(LineStrip.Move(list, 3, 0), Is.True);
            Assert.That(string.Join("", list), Is.EqualTo("dbca"));
        }

        [Test]
        public void MovingToTheSamePlaceOrOutsideChangesNothing()
        {
            var list = new List<string> { "a", "b", "c" };

            Assert.That(LineStrip.Move(list, 1, 1), Is.False);
            Assert.That(LineStrip.Move(list, -1, 1), Is.False);
            Assert.That(LineStrip.Move(list, 0, 3), Is.False);
            Assert.That(string.Join("", list), Is.EqualTo("abc"));
        }

        [TestCase(0, 0, 0)]
        [TestCase(0, 1, 0)]
        [TestCase(0, 2, 1)]
        [TestCase(0, 4, 3)]
        [TestCase(3, 0, 0)]
        [TestCase(3, 3, 3)]
        [TestCase(3, 4, 3)]
        [TestCase(2, 1, 1)]
        public void ADropBeforeASlotGivesTheFinalPlace(int from, int slot, int expected)
        {
            Assert.That(LineStrip.DropTarget(from, slot), Is.EqualTo(expected));
        }

        [Test]
        public void TheSelectionFollowsTheStopThatMoves()
        {
            Assert.That(LineStrip.SelectionAfterMove(1, 1, 3), Is.EqualTo(3));
        }

        [Test]
        public void TheSelectionStepsBackWhenAStopFromBeforeItMovesPastIt()
        {
            Assert.That(LineStrip.SelectionAfterMove(2, 0, 3), Is.EqualTo(1));
        }

        [Test]
        public void TheSelectionStepsOnWhenAStopFromAfterItMovesInFrontOfIt()
        {
            Assert.That(LineStrip.SelectionAfterMove(1, 3, 0), Is.EqualTo(2));
        }

        [Test]
        public void TheSelectionStaysWhenTheMoveDoesNotCrossIt()
        {
            Assert.That(LineStrip.SelectionAfterMove(0, 2, 3), Is.EqualTo(0));
            Assert.That(LineStrip.SelectionAfterMove(3, 0, 1), Is.EqualTo(3));
            Assert.That(LineStrip.SelectionAfterMove(-1, 0, 2), Is.EqualTo(-1));
        }

        [Test]
        public void RemovingTheSelectedStopClearsTheSelectionAndOneBeforeItShiftsIt()
        {
            Assert.That(LineStrip.SelectionAfterRemove(2, 2), Is.EqualTo(-1));
            Assert.That(LineStrip.SelectionAfterRemove(2, 0), Is.EqualTo(1));
            Assert.That(LineStrip.SelectionAfterRemove(2, 3), Is.EqualTo(2));
            Assert.That(LineStrip.SelectionAfterRemove(-1, 0), Is.EqualTo(-1));
        }

        [TestCase(-1, 3, -1)]
        [TestCase(0, 3, 0)]
        [TestCase(2, 3, 2)]
        [TestCase(3, 3, -1)]
        [TestCase(0, 0, -1)]
        public void DeleteTakesOutTheSelectedStopOrDoesNothing(int selected, int count, int expected)
        {
            Assert.That(LineStrip.DeleteIndex(selected, count), Is.EqualTo(expected));
        }

        [TestCase(1, 1, 1)]
        [TestCase(1, 2, 1)]
        [TestCase(4, 2, 2)]
        [TestCase(4, 5, 4)]
        [TestCase(1, 5, 4)]
        public void ADropNextToTheOwnPlaceKeepsItAndOthersGiveTheFinalPlace(int from, int slot, int expected)
        {
            Assert.That(LineStrip.DropTarget(from, slot), Is.EqualTo(expected));
        }

        [Test]
        public void TheSelectionStaysOnItsStopWhenEveryMoveIsReplayedOnTheList()
        {
            for (var from = 0; from < 4; from++)
                for (var to = 0; to < 4; to++)
                    for (var selected = 0; selected < 4; selected++)
                    {
                        var list = new List<int> { 0, 1, 2, 3 };
                        LineStrip.Move(list, from, to);
                        Assert.That(list[LineStrip.SelectionAfterMove(selected, from, to)], Is.EqualTo(selected), $"{selected} from {from} to {to}");
                    }
        }

        [Test]
        public void TheSelectionStepsOnWhenAStopFromAfterItMovesToItsPlace()
        {
            Assert.That(LineStrip.SelectionAfterMove(1, 3, 1), Is.EqualTo(2));
        }

        [Test]
        public void StepTargetsAtBothEndsOfLongListsAndOutsideIndexes()
        {
            Assert.That(LineStrip.StepTarget(7, 1, 8), Is.EqualTo(-1));
            Assert.That(LineStrip.StepTarget(6, 1, 8), Is.EqualTo(7));
            Assert.That(LineStrip.StepTarget(0, 1, 0), Is.EqualTo(-1));
        }
    }
}
