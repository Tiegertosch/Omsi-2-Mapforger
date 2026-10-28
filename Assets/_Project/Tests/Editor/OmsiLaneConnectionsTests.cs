using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiLaneConnectionsTests
    {
        private const string ProfilePath = @"Splines\Test\road.sli";

        // An 8 m carriageway at 0.1 m with a lane each way, 2 m either side of the spline (lane 0 against it, lane 1
        // along it), and pavements with a footpath each.
        private const string Profile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-7\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.1\r\n1\r\n0.2\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.1\r\n0\r\n0.2\r\n[profilepnt]\r\n4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n7\r\n0.25\r\n1\r\n0.2\r\n" +
            "[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n" +
            "[path]\r\n1\r\n-5.5\r\n0.25\r\n1\r\n2\r\n[path]\r\n1\r\n5.5\r\n0.25\r\n1\r\n2\r\n";

        /// <summary>A T junction: arm 0 forward, arm 1 east and arm 2 west.</summary>
        private static List<OmsiJunctionArm> Tee()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            return new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList();
        }

        private static int RoadPaths(OmsiJunctionLayout layout)
        {
            return layout.Paths.Count(path => path.Kind == 0);
        }

        [Test]
        public void EveryLaneInLeadsToEveryLaneOutUnlessConnectionsAreChosen()
        {
            var layout = OmsiJunctionLayout.Build(Tee(), null);
            var ends = layout.LaneEnds();

            // A lane in and a lane out on each of the three arms: lane 0 runs against the profile, so into the junction.
            Assert.AreEqual(6, ends.Count);
            Assert.IsTrue(ends.Where(end => end.Inbound).All(end => end.Lane == 0));
            Assert.IsTrue(ends.Where(end => !end.Inbound).All(end => end.Lane == 1));
            var east = ends.Single(end => end.Arm == 1 && end.Inbound);
            Assert.AreEqual(12.0, east.X, 1e-9);
            Assert.AreEqual(2.0, east.Y, 1e-9);
            Assert.AreEqual(270.0, east.HeadingDegrees, 1e-9);
            var all = OmsiLaneConnections.All(ends);
            Assert.AreEqual(6, all.Count);
            Assert.IsTrue(all.Contains("1.0>2.1"));
            Assert.IsFalse(all.Contains("1.0>1.1"));

            // All of them chosen build the same lanes as none chosen; fewer build fewer, and the footpaths stay.
            Assert.AreEqual(RoadPaths(layout), RoadPaths(OmsiJunctionLayout.Build(Tee(), null, all)));
            var straight = new HashSet<string> { "1.0>2.1", "2.0>1.1" };
            var through = OmsiJunctionLayout.Build(Tee(), null, straight);
            Assert.AreEqual(2, RoadPaths(through));
            Assert.AreEqual(layout.Paths.Count(path => path.Kind == 1), through.Paths.Count(path => path.Kind == 1));
            Assert.AreEqual(0, RoadPaths(OmsiJunctionLayout.Build(Tee(), null, new HashSet<string>())));

            // The curve of a connection runs from the lane in to the lane out.
            var west = ends.Single(end => end.Arm == 2 && !end.Inbound);
            var curve = OmsiLaneConnections.Curve(east, west);
            Assert.AreEqual(12.0, curve.First()[0], 1e-6);
            Assert.AreEqual(-12.0, curve.Last()[0], 1e-6);
            Assert.AreEqual(2.0, curve.Last()[1], 1e-6);
        }

        [Test]
        public void ChosenConnectionsAreKeptInTheJunctionsFile()
        {
            var straight = new HashSet<string> { "2.0>1.1", "1.0>2.1" };

            var made = OmsiJunctionGenerator.Junction(Tee(), null, "Through", null, null, null, straight);

            StringAssert.Contains("MapWorks lanes: 1.0>2.1 2.0>1.1", made.Sco);
            var back = OmsiGeneratedJunction.Parse(@"Sceneryobjects\Mapforge\Test\Through\Through.sco", Encoding.UTF8.GetBytes(made.Sco),
                path => OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), path));
            CollectionAssert.AreEquivalent(straight, back.Connections);
            Assert.AreEqual(2, OmsiSceneryObject.Parse(Encoding.UTF8.GetBytes(made.Sco)).Paths.Count(path => path.Kind == 0));

            // Without chosen connections the file has no line and every way through.
            var every = OmsiJunctionGenerator.Junction(Tee(), null, "Every");
            StringAssert.DoesNotContain(OmsiLaneConnections.Prefix, every.Sco);
            Assert.IsNull(OmsiLaneConnections.Read(every.Sco));
            Assert.IsEmpty(OmsiLaneConnections.Read("MapWorks lanes:\r\n"));
        }
    }
}
