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

        private static OmsiGeneratedJunction ReadBack(OmsiGeneratedObject made)
        {
            return OmsiGeneratedJunction.Parse(@"Sceneryobjects\Mapforge\Test\Signals\Signals.sco", OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252),
                path => OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), path));
        }

        /// <summary>Two lights for the T junction: the through road (east-west both ways) and the side road turning in.</summary>
        private static OmsiJunctionSignals TeeSignals()
        {
            var signals = new OmsiJunctionSignals();
            signals.Program.CycleSeconds = 60;
            var through = signals.Program.AddLight("Through");
            signals.Program.Lights[through].Phases.Insert(0, new OmsiSignalPhase(6, 30));
            var side = signals.Program.AddLight("Side");
            signals.Program.Lights[side].Phases.Insert(0, new OmsiSignalPhase(0, 33));
            signals.Program.Lights[side].Phases.Insert(1, new OmsiSignalPhase(6, 20));
            signals.WayLights["1.0>2.1"] = through;
            signals.WayLights["2.0>1.1"] = through;
            signals.WayLights["0.0>1.1"] = side;
            signals.WayLights["0.0>2.1"] = side;
            return signals;
        }

        [Test]
        public void TheSignalsOfAMadeJunctionAreWrittenOnTheFirstArcOfEachWay()
        {
            var made = OmsiJunctionGenerator.Junction(Tee(), null, "Signals", signals: TeeSignals());

            StringAssert.Contains("MapWorks signals: 0.0>1.1=1 0.0>2.1=1 1.0>2.1=0 2.0>1.1=0", made.Sco);
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var program = OmsiSignalProgram.Read(bytes);
            CollectionAssert.AreEqual(new[] { "Through", "Side" }, program.Lights.Select(light => light.Name).ToArray());
            Assert.AreEqual(60, program.CycleSeconds);
            var layout = OmsiJunctionLayout.Build(Tee(), null);
            for (var index = 0; index < layout.Paths.Count; index++)
            {
                var path = layout.Paths[index];
                var expected = path.StartsWay ? TeeSignals().WayLights.TryGetValue(path.WayThrough, out var light) ? light : -1 : -1;
                Assert.AreEqual(expected, program.PathLights[index], "path " + index + " " + path.WayThrough);
            }
            // The four bound ways obey their light at their first arc, the two ways into the side road none, and the analysis sees it so.
            var crossing = OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(bytes), program);
            var road = crossing.Movements.Where(movement => movement.Turn != OmsiMovementTurn.Pedestrian).ToList();
            Assert.AreEqual(6, road.Count);
            Assert.AreEqual(2, road.Count(movement => movement.Light == 0));
            Assert.AreEqual(2, road.Count(movement => movement.Light == 1));
            Assert.AreEqual(2, road.Count(movement => movement.Light == -1));
            // Each arm has one lane in; its two ways start on arcs of their own but at the same place, so they share the lane.
            Assert.AreEqual(3, crossing.Approaches.Count);
            Assert.IsTrue(crossing.Approaches.All(approach => approach.Lanes.Count == 1 && approach.Lanes[0].Movements.Count == 2));
            CollectionAssert.IsEmpty(made.LostSignalWays);
            // Without a program nothing is written.
            StringAssert.DoesNotContain("traffic_light", OmsiJunctionGenerator.Junction(Tee(), null, "Plain").Sco);
        }

        [Test]
        public void TheSignalsSurviveMakingTheJunctionAgainAndLoseOnlyWaysThatAreGone()
        {
            var first = ReadBack(OmsiJunctionGenerator.Junction(Tee(), null, "Signals", signals: TeeSignals()));
            Assert.IsNotNull(first.Signals);
            Assert.AreEqual(4, first.Signals.WayLights.Count);

            // Made again with markings, as the markings tool does: the program and every binding stay.
            var again = OmsiJunctionGenerator.Junction(first.Arms, null, "Signals", first.Markings, first.Areas, first.Arrows, first.Connections, first.Signals);
            var second = ReadBack(again);
            CollectionAssert.AreEquivalent(TeeSignals().WayLights, second.Signals.WayLights);
            CollectionAssert.AreEqual(new[] { "Through", "Side" }, second.Signals.Program.Lights.Select(light => light.Name).ToArray());

            // Made again with fewer lanes, as the lanes tool does: the ways that are gone lose their binding, and say so.
            var fewer = OmsiJunctionGenerator.Junction(second.Arms, null, "Signals", null, null, null, new HashSet<string> { "1.0>2.1", "2.0>1.1", "0.0>2.1" },
                second.Signals);
            CollectionAssert.AreEqual(new[] { "0.0>1.1" }, fewer.LostSignalWays);
            var third = ReadBack(fewer);
            Assert.AreEqual(3, third.Signals.WayLights.Count);
            Assert.IsFalse(third.Signals.WayLights.ContainsKey("0.0>1.1"));
            // The junction's own copy of the signals is not changed by making the new one.
            Assert.AreEqual(4, second.Signals.WayLights.Count);
        }
    }
}
