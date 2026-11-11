using System.Collections.Generic;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiParkingCheckTests
    {
        // A road lane running east for 100 m: its right-hand side is the south.
        private static List<OmsiMapPath> Lane(int direction = 0)
        {
            return new List<OmsiMapPath>
            {
                new OmsiMapPath { Kind = 0, Direction = direction, Points = new[] { new OmsiMapPoint(0, 0), new OmsiMapPoint(100, 0) } },
                new OmsiMapPath { Kind = 1, Direction = 2, Points = new[] { new OmsiMapPoint(0, -2), new OmsiMapPoint(100, -2) } }
            };
        }

        [Test]
        public void ASpaceOnTheRightOfALaneInLineWithItAwayFromItsEndsIsUsable()
        {
            var verdict = OmsiParkingCheck.Judge(50, -2.6, 90, Lane());
            Assert.AreEqual(OmsiParkingFault.None, verdict.Fault);
            // Facing the other way within 12 degrees still is in line; 20 degrees is not.
            Assert.AreEqual(OmsiParkingFault.None, OmsiParkingCheck.Judge(50, -2.6, 100, Lane()).Fault);
            var turned = OmsiParkingCheck.Judge(50, -2.6, 110, Lane());
            Assert.AreEqual(OmsiParkingFault.Heading, turned.Fault);
            Assert.AreEqual(20.0, turned.Value, 1e-6);
        }

        [Test]
        public void ASpaceOnTheWrongSideOrTooNearOrFarFromTheLaneIsRefused()
        {
            var left = OmsiParkingCheck.Judge(50, 2.6, 90, Lane());
            Assert.AreEqual(OmsiParkingFault.Side, left.Fault);
            Assert.AreEqual(-2.6, left.Value, 1e-6);
            Assert.AreEqual(OmsiParkingFault.Side, OmsiParkingCheck.Judge(50, -0.8, 90, Lane()).Fault);
            Assert.AreEqual(OmsiParkingFault.Side, OmsiParkingCheck.Judge(50, -4.5, 90, Lane()).Fault);
            // A footpath is no lane for cars, and a space too far from any lane has none to park beside.
            Assert.AreEqual(OmsiParkingFault.NoLane, OmsiParkingCheck.Judge(50, -40, 90, Lane()).Fault);
            Assert.AreEqual(OmsiParkingFault.NoLane, OmsiParkingCheck.Judge(50, -2.6, 90, new List<OmsiMapPath>()).Fault);
        }

        [Test]
        public void TheLaneMustRunEightMetresBeforeTheSpaceAndFourAfterIt()
        {
            Assert.AreEqual(OmsiParkingFault.LaneEnd, OmsiParkingCheck.Judge(5, -2.6, 90, Lane()).Fault);
            Assert.AreEqual(OmsiParkingFault.LaneEnd, OmsiParkingCheck.Judge(98, -2.6, 90, Lane()).Fault);
            Assert.AreEqual(OmsiParkingFault.None, OmsiParkingCheck.Judge(9, -2.6, 90, Lane()).Fault);
            Assert.AreEqual(OmsiParkingFault.None, OmsiParkingCheck.Judge(95, -2.6, 90, Lane()).Fault);
        }

        [Test]
        public void ALaneAgainstItsPointsHasItsRightOnTheOtherSideAndALaneForBothWaysSuitsEitherSide()
        {
            // Against its points (west), the right is the north.
            Assert.AreEqual(OmsiParkingFault.None, OmsiParkingCheck.Judge(50, 2.6, 270, Lane(1)).Fault);
            Assert.AreEqual(OmsiParkingFault.Side, OmsiParkingCheck.Judge(50, -2.6, 90, Lane(1)).Fault);
            // Both ways: a space on either side faces along the lane that has it on its right.
            Assert.AreEqual(OmsiParkingFault.None, OmsiParkingCheck.Judge(50, -2.6, 90, Lane(2)).Fault);
            Assert.AreEqual(OmsiParkingFault.None, OmsiParkingCheck.Judge(50, 2.6, 270, Lane(2)).Fault);
        }

        [Test]
        public void TheParkingListFilesAreNamedByNumber()
        {
            Assert.AreEqual("parklist_p.txt", OmsiParkList.FileName(0));
            Assert.AreEqual("parklist_p_3.txt", OmsiParkList.FileName(3));
        }
    }
}
