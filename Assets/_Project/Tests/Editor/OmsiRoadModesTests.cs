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

        private sealed class SlopeTerrain : IOmsiTerrainHeights
        {
            public bool TryGetHeight(double worldX, double worldY, out double value)
            {
                value = 0.1 * worldX;
                return true;
            }
        }

        [Test]
        public void ParallelRoadsKeepTheirDistanceAndCanRunTheOtherWay()
        {
            // A quarter circle of 50 m to the right, from 0, 0 heading north to 50, 50 heading east.
            var main = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, ControlX = 0, ControlY = 50, EndX = 50, EndY = 50, Shape = OmsiRoadShape.Curved, HeightMode = OmsiRoadHeightMode.Manual,
                StartHeight = 2, EndHeight = 4
            }, new FlatTerrain());

            // 5 m to its right: the inner road, a radius of 45 m around the same centre.
            var inner = OmsiRoadPlanner.Offset(main, 5, false, OmsiRoadHeightMode.Manual, new FlatTerrain());
            Assert.IsFalse(inner.HasErrors);
            Assert.AreEqual(5.0, inner.Pieces[0].StartX, 1e-9);
            Assert.AreEqual(0.0, inner.Pieces[0].StartY, 1e-9);
            Assert.IsTrue(inner.Pieces.All(piece => Math.Abs(piece.RadiusMeters - 45.0) < 1e-6));
            Assert.AreEqual(main.LengthMeters * 0.9, inner.LengthMeters, 1e-6);
            AssertEndsAt(inner, 50, 45);
            // Heights as on the road it runs beside.
            Assert.AreEqual(2.0, inner.Pieces[0].StartHeight, 1e-9);
            Assert.AreEqual(4.0, inner.EndHeight, 1e-9);

            // The other way round: from the end back to the start, turning left.
            var back = OmsiRoadPlanner.Offset(main, 5, true, OmsiRoadHeightMode.Manual, new FlatTerrain());
            Assert.AreEqual(50.0, back.Pieces[0].StartX, 1e-6);
            Assert.AreEqual(45.0, back.Pieces[0].StartY, 1e-6);
            Assert.AreEqual(270.0, back.Pieces[0].HeadingDegrees, 1e-6);
            Assert.IsTrue(back.Pieces.All(piece => Math.Abs(piece.RadiusMeters + 45.0) < 1e-6));
            Assert.AreEqual(4.0, back.Pieces[0].StartHeight, 1e-9);
            Assert.AreEqual(2.0, back.EndHeight, 1e-9);
            AssertEndsAt(back, 5, 0);
            AssertWithoutKinks(back);

            // Closer than the road is wide, the two overlap: reported, still built.
            var overlapping = OmsiRoadPlanner.Offset(main, 5, false, OmsiRoadHeightMode.Manual, new FlatTerrain(), 5.25, 10.5);
            Assert.IsFalse(overlapping.HasErrors);
            Assert.That(overlapping.Issues.Select(issue => issue.Message), Has.Some.Contains("overlaps it"));

            // 60 m to the right would pass the centre of the curve.
            Assert.IsTrue(OmsiRoadPlanner.Offset(main, 60, false, OmsiRoadHeightMode.Manual, new FlatTerrain()).HasErrors);

            // Following the terrain, the road 10 m to the left of a road going north lies on the terrain there.
            var straight = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 20, StartY = 0, EndX = 20, EndY = 40 }, new SlopeTerrain());
            var left = OmsiRoadPlanner.Offset(straight, -10, false, OmsiRoadHeightMode.FollowTerrain, new SlopeTerrain());
            Assert.AreEqual(10.0, left.Pieces[0].StartX, 1e-9);
            Assert.AreEqual(1.0, left.Pieces[0].StartHeight, 1e-9);
            Assert.AreEqual(1.0, left.EndHeight, 1e-9);
        }

        [Test]
        public void PointersSnapOntoGuideLinesAndWhereTheyCross()
        {
            // A road ends at 0, 0 heading north: its extension runs north, the line square to it east and west.
            var north = new OmsiGuideLine(0, 0, 0, false);
            var across = new OmsiGuideLine(0, 0, 90, true);
            // Another free end at 30, 40 facing west: its extension runs west along y = 40, its square line through x = 30.
            var other = new OmsiGuideLine(30, 40, 270, false);
            var square = new OmsiGuideLine(30, 40, 180, true);
            var lines = new[] { north, across, other, square };
            var used = new System.Collections.Generic.List<OmsiGuideLine>();

            // Near one line: onto it.
            Assert.IsTrue(OmsiRoadGuides.Snap(lines, 1.5, 60, 2, 200, null, out var x, out var y, used));
            Assert.AreEqual(0.0, x, 1e-9);
            Assert.AreEqual(60.0, y, 1e-9);
            CollectionAssert.AreEqual(new[] { north }, used);

            // Near where the extension meets the other end's east line: onto the crossing.
            used.Clear();
            Assert.IsTrue(OmsiRoadGuides.Snap(lines, 1, 41.5, 2, 200, null, out x, out y, used));
            Assert.AreEqual(0.0, x, 1e-9);
            Assert.AreEqual(40.0, y, 1e-9);
            CollectionAssert.AreEquivalent(new[] { north, other }, used);

            // Behind a ray's origin it does not count, and nothing else is near.
            Assert.IsFalse(OmsiRoadGuides.Snap(lines, 1, -60, 2, 200, null, out _, out _, null));
            // Lines whose origin is out of reach are left out.
            Assert.IsFalse(OmsiRoadGuides.Snap(lines, 1.5, 60, 2, 20, null, out _, out _, null));

            // A road already heading west from 100, 20 only snaps where a guide crosses its direction.
            var west = new OmsiGuideLine(100, 20, 270, false);
            Assert.IsTrue(OmsiRoadGuides.Snap(lines, 31.5, 20, 2, 200, west, out x, out y, null));
            Assert.AreEqual(30.0, x, 1e-9);
            Assert.AreEqual(20.0, y, 1e-9);
            Assert.IsFalse(OmsiRoadGuides.Snap(lines, 15, 20, 2, 200, west, out _, out _, null));
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
