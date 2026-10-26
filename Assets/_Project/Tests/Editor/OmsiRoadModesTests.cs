using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiRoadModesTests
    {
        private sealed class FlatTerrain : IOmsiTerrainHeights
        {
            public bool TryGetHeight(double worldX, double worldY, out double value)
            {
                value = 2.0;
                return true;
            }
        }

        [Test]
        public void StraightRoadsGoStraightAndReportTheKinkWhereTheyContinueARoad()
        {
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 20, EndY = 20, Shape = OmsiRoadShape.Straight
            }, new FlatTerrain());

            Assert.IsTrue(plan.Pieces.All(piece => piece.RadiusMeters == 0.0));
            Assert.AreEqual(45.0, plan.Pieces[0].HeadingDegrees, 1e-9);
            Assert.AreEqual(45.0, plan.EndHeadingDegrees, 1e-9);
            Assert.That(plan.Issues.Select(issue => issue.Message), Has.Some.Contains("kink of 45°"));
            AssertEndsAt(plan, 20, 20);

            // A free start has no kink to report, and a free start joining a road is not bent to meet it.
            var free = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 30, EndJoinHeadingDegrees = 90, Shape = OmsiRoadShape.Straight },
                new FlatTerrain());
            Assert.IsTrue(free.Pieces.All(piece => piece.RadiusMeters == 0.0));
            Assert.That(free.Issues.Select(issue => issue.Message), Has.Some.Contains("joined road with a kink of 90°"));
        }

        [Test]
        public void StraightRoadsRoundTheCornerWhereTheyTurnOffTheRoadTheyContinue()
        {
            // A road coming north ends at 0, 0 rising 2 %; the next straight goes east: a quarter circle of 15 m joins them.
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, StartHeight = 1, StartGradientPercent = 2, EndX = 40, EndY = 0,
                Shape = OmsiRoadShape.Straight, CornerRadiusMeters = 15, StartTrimMaximumMeters = 20
            }, new FlatTerrain());

            Assert.AreEqual(15.0, plan.StartTrimMeters, 1e-9);
            Assert.AreEqual(15.0, plan.CornerRadiusMeters, 1e-9);
            Assert.AreEqual(0.0, plan.Pieces[0].StartX, 1e-9);
            Assert.AreEqual(-15.0, plan.Pieces[0].StartY, 1e-9);
            Assert.AreEqual(0.0, plan.Pieces[0].HeadingDegrees, 1e-9);
            Assert.AreEqual(15.0, plan.Pieces[0].RadiusMeters, 1e-9);
            // It starts where the road before is cut back to, at that road's height there.
            Assert.AreEqual(0.7, plan.Pieces[0].StartHeight, 1e-9);
            Assert.AreEqual(90.0, plan.EndHeadingDegrees, 1e-9);
            Assert.IsTrue(plan.Pieces.Last().RadiusMeters == 0.0);
            AssertWithoutKinks(plan);
            AssertEndsAt(plan, 40, 0);
            CollectionAssert.IsEmpty(plan.Issues.Where(issue => issue.Message.Contains("kink of")));

            // A short road before leaves room for a tighter corner only.
            var tight = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 40, EndY = 0, Shape = OmsiRoadShape.Straight, CornerRadiusMeters = 15,
                StartTrimMaximumMeters = 5
            }, new FlatTerrain());
            Assert.AreEqual(5.0, tight.StartTrimMeters, 1e-9);
            Assert.AreEqual(5.0, tight.CornerRadiusMeters, 1e-9);

            // Without a radius, or straight on, the kink stays as before.
            var kinked = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 40, EndY = 0, Shape = OmsiRoadShape.Straight, StartTrimMaximumMeters = 20
            }, new FlatTerrain());
            Assert.AreEqual(0.0, kinked.StartTrimMeters);
            Assert.That(kinked.Issues.Select(issue => issue.Message), Has.Some.Contains("kink of 90°"));
        }

        [Test]
        public void CurvedRoadsLeaveTowardsTheControlPointAndArriveComingFromIt()
        {
            // Start, control and end on three corners of a square: a quarter circle.
            var quarter = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, ControlX = 0, ControlY = 50, EndX = 50, EndY = 50, Shape = OmsiRoadShape.Curved
            }, new FlatTerrain());
            Assert.IsFalse(quarter.HasErrors);
            Assert.AreEqual(0.0, quarter.Pieces[0].HeadingDegrees, 1e-9);
            Assert.AreEqual(90.0, quarter.EndHeadingDegrees, 1e-9);
            Assert.IsTrue(quarter.Pieces.All(piece => Math.Abs(piece.RadiusMeters - 50.0) < 1e-6));
            AssertEndsAt(quarter, 50, 50);

            // An uneven control point: two arcs without a kink, still leaving towards it and arriving from it.
            var uneven = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, ControlX = 10, ControlY = 60, EndX = 70, EndY = 70, Shape = OmsiRoadShape.Curved
            }, new FlatTerrain());
            Assert.AreEqual(OmsiRoadPlanner.ChordHeading(0, 0, 10, 60), uneven.Pieces[0].HeadingDegrees, 1e-9);
            Assert.AreEqual(OmsiRoadPlanner.ChordHeading(10, 60, 70, 70), uneven.EndHeadingDegrees, 1e-9);
            AssertWithoutKinks(uneven);
            AssertEndsAt(uneven, 70, 70);

            // Ending on a road beside it that runs the same way: an S curve meets the road in its direction.
            var sCurve = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, ControlX = 0, ControlY = 30, EndX = 10, EndY = 60, EndJoinHeadingDegrees = 0, Shape = OmsiRoadShape.Curved
            }, new FlatTerrain());
            Assert.Greater(sCurve.Pieces.First().RadiusMeters, 0.0);
            Assert.Less(sCurve.Pieces.Last().RadiusMeters, 0.0);
            Assert.AreEqual(0.0, sCurve.EndHeadingDegrees, 1e-9);
            CollectionAssert.IsEmpty(sCurve.Issues.Where(issue => issue.Message.Contains("kink")));
            AssertEndsAt(sCurve, 10, 60);

            // Without a control point a curved road is planned as a free one.
            var noControl = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 20, EndY = 20, Shape = OmsiRoadShape.Curved },
                new FlatTerrain());
            Assert.AreEqual(90.0, noControl.EndHeadingDegrees, 1e-9);
        }

        [Test]
        public void PointersSnapToRightAnglesOfTheReference()
        {
            // 2.3° off north snaps onto the north ray, keeping the length along it.
            Assert.IsTrue(OmsiRoadSnapping.SnapToAngle(0, 0, 0, 90, 2, 50, out var x, out var y));
            Assert.AreEqual(0.0, x, 1e-9);
            Assert.AreEqual(Math.Sqrt(2 * 2 + 50 * 50) * Math.Cos(Math.Atan2(2, 50)), y, 1e-9);
            // 11° off is a direction of its own.
            Assert.IsFalse(OmsiRoadSnapping.SnapToAngle(0, 0, 0, 90, 10, 50, out x, out y));
            Assert.AreEqual(10.0, x);
            // Square to a road heading 30°: a pointer at 118° snaps to 120°.
            var pointer = 118.0 * Math.PI / 180.0;
            Assert.IsTrue(OmsiRoadSnapping.SnapToAngle(5, 5, 30, 90, 5 + 40 * Math.Sin(pointer), 5 + 40 * Math.Cos(pointer), out x, out y));
            Assert.AreEqual(120.0, OmsiRoadPlanner.ChordHeading(5, 5, x, y), 1e-9);
            // Too close to the origin to tell a direction.
            Assert.IsFalse(OmsiRoadSnapping.SnapToAngle(0, 0, 0, 90, 0.2, 0.5, out _, out _));
        }

        [Test]
        public void TangentArcsSnapToEndStraightOnOrSquare()
        {
            // The line to the end 44° off the start heading: the arc would turn 88°, so it snaps to a quarter turn.
            var pointer = 44.0 * Math.PI / 180.0;
            Assert.IsTrue(OmsiRoadSnapping.SnapArcEnd(0, 0, 0, 40 * Math.Sin(pointer), 40 * Math.Cos(pointer), out var x, out var y));
            OmsiRoadPlanner.TangentArc(0, 0, 0, x, y, out _, out _, out var endHeading);
            Assert.AreEqual(90.0, endHeading, 1e-9);
            // 1.5° off straight on snaps straight; 10° off does not.
            pointer = 1.5 * Math.PI / 180.0;
            Assert.IsTrue(OmsiRoadSnapping.SnapArcEnd(0, 0, 0, 40 * Math.Sin(pointer), 40 * Math.Cos(pointer), out x, out _));
            Assert.AreEqual(0.0, x, 1e-9);
            pointer = 10.0 * Math.PI / 180.0;
            Assert.IsFalse(OmsiRoadSnapping.SnapArcEnd(0, 0, 0, 40 * Math.Sin(pointer), 40 * Math.Cos(pointer), out _, out _));
        }

        private static void AssertEndsAt(OmsiRoadPlan plan, double x, double y)
        {
            var last = plan.Pieces.Last();
            last.PointAt(last.LengthMeters, out var endX, out var endY, out _);
            Assert.AreEqual(x, endX, 1e-6);
            Assert.AreEqual(y, endY, 1e-6);
        }

        private static void AssertWithoutKinks(OmsiRoadPlan plan)
        {
            for (var index = 1; index < plan.Pieces.Count; index++)
            {
                var before = plan.Pieces[index - 1];
                var turn = before.RadiusMeters == 0.0 ? 0.0 : before.LengthMeters / before.RadiusMeters * 180.0 / Math.PI;
                Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(before.HeadingDegrees + turn - plan.Pieces[index].HeadingDegrees), 1e-6);
            }
        }
    }
}
