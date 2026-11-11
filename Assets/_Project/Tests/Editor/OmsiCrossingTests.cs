using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiCrossingTests
    {
        private const string ProfilePath = @"Splines\Test\road.sli";
        private const string Texture = @"Splines\Test\texture\plain.bmp";

        // 14 m wide: pavements 3 m wide, an 8 m carriageway, a lane each way and a footpath on each pavement used both ways.
        private const string Profile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-7\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.1\r\n1\r\n0.2\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.1\r\n0\r\n0.2\r\n[profilepnt]\r\n4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n7\r\n0.25\r\n1\r\n0.2\r\n" +
            "[path]\r\n1\r\n-5.5\r\n0.25\r\n1\r\n2\r\n[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n[path]\r\n1\r\n5.5\r\n0.25\r\n1\r\n2\r\n";

        // The same road without footpaths.
        private const string BareProfile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n";

        private static OmsiJunctionLayout Tee(string profileText, OmsiCrossing crossingOnSideArm)
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(profileText), ProfilePath);
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList();
            arms[2].Crossing = crossingOnSideArm;
            return OmsiJunctionLayout.Build(arms, Texture);
        }

        private static (double X, double Y) End(OmsiJunctionPath path)
        {
            var radians = path.HeadingDegrees * Math.PI / 180.0;
            return (path.X + Math.Sin(radians) * path.LengthMeters, path.Y + Math.Cos(radians) * path.LengthMeters);
        }

        [Test]
        public void AnArmWithoutACrossingIsBuiltAsBefore()
        {
            var plain = Tee(Profile, null);
            Assert.IsTrue(plain.Arms.All(arm => Math.Abs(arm.Length - 12.0) < 1e-9));
            Assert.IsTrue(plain.CrossingProblems.All(problem => problem == null));
            Assert.IsFalse(plain.Paths.Any(path => path.Kind == 1 && Math.Abs(path.LengthMeters - 11.0) < 1e-6));
        }

        [Test]
        public void ACrossingMakesTheArmReachOutAndLaysAFootpathAcrossIt()
        {
            var layout = Tee(Profile, new OmsiCrossing());

            // The corner starts 12 m out; the crossing's centre is 3 m beyond, 3 m wide, and the arm reaches 2 m past its edge.
            var side = layout.Arms.Single(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            Assert.AreEqual(12.0 + 3.0 + 1.5 + 2.0, side.Length, 1e-9);
            Assert.IsNull(layout.CrossingProblems[layout.Arms.IndexOf(side)]);

            var across = layout.Paths.Single(path => path.Kind == 1 && path.RadiusMeters == 0.0 && Math.Abs(path.LengthMeters - 11.0) < 1e-6 && Math.Abs(path.Y - 15.0) < 1e-6);
            Assert.AreEqual(2, across.Direction);
            Assert.AreEqual(3.0, across.Width, 1e-9);
            Assert.AreEqual(5.5, across.X, 1e-6);
            Assert.AreEqual(270.0, across.HeadingDegrees, 1e-6);
            var end = End(across);
            Assert.AreEqual(-5.5, end.X, 1e-6);
            Assert.AreEqual(15.0, end.Y, 1e-6);
        }

        [Test]
        public void ThePavementPathsOfTheArmAreCutWhereTheCrossingJoinsThem()
        {
            var layout = Tee(Profile, new OmsiCrossing());
            var along = layout.Paths.Where(path => path.Kind == 1 && path.RadiusMeters == 0.0 && Math.Abs(Math.Abs(path.X) - 5.5) < 1e-6 &&
                                                   Math.Abs(path.HeadingDegrees - 180.0) < 1e-6).ToList();
            // On each side one piece from the end of the arm to the crossing and one on to the corner.
            Assert.AreEqual(4, along.Count);
            foreach (var side in new[] { 5.5, -5.5 })
            {
                var outer = along.Single(path => Math.Abs(path.X - side) < 1e-6 && Math.Abs(path.Y - 18.5) < 1e-6);
                Assert.AreEqual(3.5, outer.LengthMeters, 1e-6);
                var inner = along.Single(path => Math.Abs(path.X - side) < 1e-6 && Math.Abs(path.Y - 15.0) < 1e-6);
                Assert.AreEqual(3.0, inner.LengthMeters, 1e-6);
                Assert.AreEqual(2, inner.Direction);
            }
        }

        [Test]
        public void TheCrossingJoinsTheFootpathsAtBothEnds()
        {
            var layout = Tee(Profile, new OmsiCrossing());
            var across = layout.Paths.Single(path => path.Kind == 1 && Math.Abs(path.LengthMeters - 11.0) < 1e-6 && Math.Abs(path.Y - 15.0) < 1e-6);
            var others = layout.Paths.Where(path => path.Kind == 1 && path != across).ToList();
            foreach (var point in new[] { (across.X, across.Y), End(across) })
                Assert.IsTrue(others.Any(path => Near(point, (path.X, path.Y)) || Near(point, End(path))), "No footpath ends at " + point);
        }

        [Test]
        public void TheVehicleLanesRunStraightOnToTheCornerAndNoPathCrossesTheMiddleOfTheWay()
        {
            var layout = Tee(Profile, new OmsiCrossing());
            // The lane out of the arm leads from the corner's start to the arm's end; the one in runs the other way.
            var lanes = layout.Paths.Where(path => path.Kind == 0 && path.RadiusMeters == 0.0 && Math.Abs(path.LengthMeters - 6.5) < 1e-6).ToList();
            Assert.AreEqual(2, lanes.Count, string.Join(" | ", layout.Paths.Select(path => path.Kind + ":" + path.X + "," + path.Y + " len " + path.LengthMeters)));
        }

        [Test]
        public void AnArmWithoutPavementsKeepsItsLengthAndSaysWhy()
        {
            var layout = Tee(BareProfile, new OmsiCrossing());
            var index = layout.Arms.FindIndex(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            Assert.IsNotNull(layout.CrossingProblems[index]);
            Assert.IsNull(layout.Arms[index].Crossing);
            Assert.AreEqual(layout.Arms[(index + 1) % 3].Length, layout.Arms[index].Length, 1e-9);
        }

        [Test]
        public void TheCrossingsAreKeptWithTheJunctionAsALine()
        {
            var layout = Tee(Profile, new OmsiCrossing { DistanceMeters = 4.0, WidthMeters = 2.5 });
            var line = OmsiCrossing.Write(layout.Arms);
            var arm = layout.Arms.FindIndex(each => each.Crossing != null);
            Assert.AreEqual("MapWorks crossings: " + arm + " 4 2.5", line);
            var read = OmsiCrossing.Read("text\r\n" + line + "\r\nmore");
            Assert.AreEqual(1, read.Count);
            Assert.AreEqual(4.0, read[arm].DistanceMeters, 1e-9);
            Assert.AreEqual(2.5, read[arm].WidthMeters, 1e-9);
            Assert.IsNull(OmsiCrossing.Write(Tee(Profile, null).Arms));
        }

        [Test]
        public void ValuesOutOfRangeAreKeptWithinTheBounds()
        {
            var read = OmsiCrossing.Read("MapWorks crossings: 1 0.5 20");
            Assert.AreEqual(OmsiCrossing.ClosestMeters, read[1].DistanceMeters);
            Assert.AreEqual(OmsiCrossing.WidestMeters, read[1].WidthMeters);
        }

        [Test]
        public void ATransitionAndAnArmWithRailsRefuseTheCrossing()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var transition = OmsiJunctionLayout.Build(new[] { 90.0, 270.0 }.Select(heading => new OmsiJunctionArm
                { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile, Crossing = new OmsiCrossing() }).ToList(), Texture);
            Assert.IsTrue(transition.CrossingProblems.All(problem => problem != null));
            Assert.IsTrue(transition.Arms.All(arm => arm.Crossing == null));

            var withRails = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile + "[path]\r\n2\r\n0\r\n0.1\r\n1.4\r\n2\r\n"), ProfilePath);
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = withRails }).ToList();
            arms[2].Crossing = new OmsiCrossing();
            var layout = OmsiJunctionLayout.Build(arms, Texture);
            Assert.IsTrue(layout.CrossingProblems.Any(problem => problem != null && problem.Contains("rails")));
            Assert.IsTrue(layout.Arms.All(arm => arm.Crossing == null && Math.Abs(arm.Length - 12.0) < 1e-9));
        }

        [Test]
        public void FootpathsJoinByPlaceWhateverTheirHeadingButVehiclePathsNeedTheSameWay()
        {
            OmsiMapPath Make(int kind, double x0, double y0, double x1, double y1) =>
                new OmsiMapPath { Kind = kind, Direction = 0, Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint(x1, y1) } };
            // A path along y ending at (0, 10) and another leaving it to the east: a right angle.
            var footpaths = new List<OmsiMapPath> { Make(1, 0, 0, 0, 10), Make(1, 0, 10, 10, 10) };
            OmsiMapPaths.FindOpenEnds(footpaths);
            Assert.IsFalse(footpaths[0].OpenEnd);
            Assert.IsFalse(footpaths[1].OpenStart);
            var vehicles = new List<OmsiMapPath> { Make(0, 0, 0, 0, 10), Make(0, 0, 10, 10, 10) };
            OmsiMapPaths.FindOpenEnds(vehicles);
            Assert.IsTrue(vehicles[0].OpenEnd);
            Assert.IsTrue(vehicles[1].OpenStart);
        }

        private static bool Near((double X, double Y) a, (double X, double Y) b)
        {
            return Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;
        }
    }
}
