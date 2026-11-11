using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The lane network, the way between two stops and the timetable files of a new trip (ADR 093).</summary>
    public sealed class OmsiTripPlannerTests
    {
        private static OmsiMapPath Straight(int id, int index, double x0, double y0, double x1, double y1, int direction = 0, double length = 0.0)
        {
            return new OmsiMapPath
            {
                TileX = 0, TileY = 0, ElementId = id, PathIndex = index, OnSpline = true, Kind = 0, Direction = direction,
                LengthMeters = length > 0.0 ? length : Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)),
                Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint((x0 + x1) / 2.0, (y0 + y1) / 2.0), new OmsiMapPoint(x1, y1) }
            };
        }

        [Test]
        public void ARouteFollowsTheLanesAndCountsTheRestOfTheFirstAndTheStartOfTheLastPath()
        {
            var a = Straight(1, 0, 0, 0, 100, 0);
            var b = Straight(2, 0, 100, 0, 200, 0);
            var network = OmsiPathNetwork.Build(new[] { a, b });
            // East-bound: the right-hand side is south (negative y).
            var from = network.PlaceStop(new OmsiMapPoint(30, -3), true);
            var to = network.PlaceStop(new OmsiMapPoint(150, -3), true);
            Assert.AreEqual(30.0, from.Position, 1e-6);
            Assert.AreEqual(3.0, from.Distance, 1e-6);
            var route = network.Route(from, to);
            CollectionAssert.AreEqual(new[] { a, b }, route.Lanes.Select(lane => lane.Path).ToArray());
            Assert.AreEqual(70.0 + 50.0, route.LengthMeters, 1e-6);
        }

        [Test]
        public void AStopFurtherAlongItsOwnLaneNeedsNoOtherLaneAndOneBehindItGoesRoundOrHasNoWay()
        {
            var a = Straight(1, 0, 0, 0, 100, 0);
            var network = OmsiPathNetwork.Build(new[] { a });
            var first = network.PlaceStop(new OmsiMapPoint(30, -2), true);
            var second = network.PlaceStop(new OmsiMapPoint(80, -2), true);
            var ahead = network.Route(first, second);
            Assert.AreEqual(1, ahead.Lanes.Count);
            Assert.AreEqual(50.0, ahead.LengthMeters, 1e-6);
            Assert.IsNull(network.Route(second, first));
        }

        [Test]
        public void ALaneOfADirectionOneRunsBackwardsAndAPathThatTurnsAwayIsNotLinked()
        {
            var a = Straight(1, 0, 0, 0, 100, 0);
            // Direction 1: traffic runs from the end to the start, so it comes from the east and leaves to the west.
            var back = Straight(2, 0, 200, 0, 100, 0, 1);
            back.Points = new[] { new OmsiMapPoint(100, 0), new OmsiMapPoint(150, 0), new OmsiMapPoint(200, 0) };
            // A path that leaves to the north from where a ends: 90 degrees off, no link.
            var turn = Straight(3, 0, 100, 0, 100, 100);
            var network = OmsiPathNetwork.Build(new[] { a, back, turn });
            var lanes = network.Lanes;
            var laneA = lanes.First(lane => lane.Path == a);
            var laneBack = lanes.Single(lane => lane.Path == back);
            Assert.IsFalse(laneBack.Forward);
            Assert.AreEqual(0, laneA.Next.Count);
            Assert.AreEqual(1, lanes.Count(lane => lane.Path == back));
        }

        [Test]
        public void AStopOnTheLeftOfAnEastboundLaneBelongsToTheLaneRunningTheOtherWayInRightHandTraffic()
        {
            var east = Straight(1, 0, 0, 0, 100, 0);
            var west = Straight(1, 1, 0, 3, 100, 3, 2);
            var network = OmsiPathNetwork.Build(new[] { east, west });
            // A stop at y = -1: right of the eastbound lane (nearest), and to the left of the westbound lane's eastbound way.
            var place = network.PlaceStop(new OmsiMapPoint(50, -1), true);
            Assert.IsTrue(place.Lane.Forward);
            Assert.AreSame(east, place.Lane.Path);
            // On the other side of the road, the westbound lane has it on its right.
            var other = network.PlaceStop(new OmsiMapPoint(50, 4), true);
            Assert.AreSame(west, other.Lane.Path);
            Assert.IsFalse(other.Lane.Forward);
            Assert.IsNull(network.PlaceStop(new OmsiMapPoint(50, 40), true));
        }

        [Test]
        public void SeveralLanesOneWayAreAllCandidatesAndTheRouteTakesTheOneThatLeadsOn()
        {
            // Two eastbound lanes of one road, 3 m apart; only the inner one continues.
            var outer = Straight(1, 0, 0, 0, 100, 0);
            var inner = Straight(1, 1, 0, 3, 100, 3);
            var ahead = Straight(2, 0, 100, 3, 200, 3);
            var network = OmsiPathNetwork.Build(new[] { outer, inner, ahead });
            var candidates = network.PlaceStopCandidates(new OmsiMapPoint(40, -1), true);
            // Both lanes have the stop on their right; the outer is nearest.
            Assert.AreEqual(2, candidates.Count);
            Assert.AreSame(outer, candidates[0].Lane.Path);
            var target = network.PlaceStopCandidates(new OmsiMapPoint(150, 1), true);
            var route = network.Route(candidates, target);
            Assert.IsNotNull(route);
            CollectionAssert.AreEqual(new[] { inner, ahead }, route.Lanes.Select(lane => lane.Path).ToArray());
        }

        [Test]
        public void ThePlannerFindsTheWayForALegAndTakesAnExistingLinkAsItIsAndNamesAStopWithoutALane()
        {
            var a = Straight(1, 0, 0, 0, 100, 0);
            var b = Straight(2, 0, 100, 0, 200, 0);
            var network = OmsiPathNetwork.Build(new[] { a, b });
            var stops = new[]
            {
                new OmsiTripStopChoice { Name = "One", TileIndex = 0, ObjectId = 10, World = new OmsiMapPoint(30, -3) },
                new OmsiTripStopChoice { Name = "Two", TileIndex = 0, ObjectId = 11, World = new OmsiMapPoint(150, -3) },
                new OmsiTripStopChoice { Name = "Far", TileIndex = 0, ObjectId = 12, World = new OmsiMapPoint(150, 500) }
            };
            var timetable = new OmsiTimetable();
            var plan = OmsiTripPlanner.Plan(stops.Take(2).ToList(), timetable, network, (x, y) => x == 0 && y == 0 ? 0 : -1);
            Assert.IsTrue(plan.IsValid, string.Join(";", plan.Problems));
            Assert.AreEqual(1, plan.Legs.Count);
            var leg = plan.Legs[0];
            Assert.AreEqual(120.0, leg.LengthMeters, 1e-6);
            Assert.AreEqual(30.0, leg.FromPosition, 1e-6);
            Assert.AreEqual(50.0, leg.ToPosition, 1e-6);
            CollectionAssert.AreEqual(new[] { 1, 2 }, leg.Entries.Select(entry => entry.ElementId).ToArray());
            Assert.AreEqual(2, leg.Lines.Count);

            timetable.StationLinks.Add(new OmsiStationLink { FromStopId = 10, ToStopId = 11, LengthMeters = 99.0 });
            var reused = OmsiTripPlanner.Plan(stops.Take(2).ToList(), timetable, network, (x, y) => 0);
            Assert.IsTrue(reused.Legs[0].Existing);
            Assert.AreEqual(99.0, reused.Legs[0].LengthMeters, 1e-9);
            Assert.AreEqual(0, reused.Legs[0].Entries.Count);

            var broken = OmsiTripPlanner.Plan(new[] { stops[1], stops[2] }, new OmsiTimetable(), network, (x, y) => 0);
            Assert.IsFalse(broken.IsValid);
            StringAssert.Contains("Far", broken.Problems[0]);
            Assert.IsFalse(OmsiTripPlanner.Plan(stops.Take(1).ToList(), timetable, network, (x, y) => 0).IsValid);
        }

        [Test]
        public void TheFilesOfANewTripReadBackAsTheTimetableOfTheMapAndKeepWhatWasThere()
        {
            var folder = Path.Combine(Path.GetTempPath(), "mapforge-trip-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "TTData"));
                var original = "---\r\nTime Table BusStopList File\r\n---\r\n\r\n[busstop]\r\nOld\r\n3\r\n5\r\n0.0000000000\r\n0\r\n0\r\n\r\n";
                File.WriteAllBytes(Path.Combine(folder, "TTData", "Busstops.cfg"), Encoding.UTF8.GetBytes(original));
                var a = Straight(1, 0, 0, 0, 100, 0);
                var b = Straight(2, 0, 100, 0, 200, 0);
                var network = OmsiPathNetwork.Build(new[] { a, b });
                var stops = new[]
                {
                    new OmsiTripStopChoice { Name = "One", TileIndex = 2, ObjectId = 10, World = new OmsiMapPoint(30, -3), IsNew = true },
                    new OmsiTripStopChoice { Name = "Two", TileIndex = 2, ObjectId = 11, World = new OmsiMapPoint(150, -3), IsNew = true }
                };
                var plan = OmsiTripPlanner.Plan(stops, new OmsiTimetable(), network, (x, y) => 2);
                var busStops = OmsiTimetableWriter.AppendBusStops(File.ReadAllBytes(Path.Combine(folder, "TTData", "Busstops.cfg")), stops);
                File.WriteAllBytes(Path.Combine(folder, "TTData", "Busstops.cfg"), busStops);
                File.WriteAllBytes(Path.Combine(folder, "TTData", "StnLinks.cfg"), OmsiTimetableWriter.AppendStationLinks(null, plan.Legs));
                File.WriteAllBytes(Path.Combine(folder, "TTData", "1_A-B.ttp"), OmsiTimetableWriter.CreateTrip("Two", "1", new[] { 10, 11 }, "standard", 4.5));
                File.WriteAllBytes(Path.Combine(folder, "TTData", "1.ttl"), OmsiTimetableWriter.CreateLine("Busses"));

                var timetable = OmsiTimetable.Read(new OmsiFolderSource(folder));
                CollectionAssert.AreEqual(new[] { "Old", "One", "Two" }, timetable.BusStops.Select(stop => stop.Name).ToArray());
                StringAssert.StartsWith(original, Encoding.UTF8.GetString(busStops));
                var link = timetable.StationLinks.Single();
                Assert.AreEqual(10, link.FromStopId);
                Assert.AreEqual(11, link.ToStopId);
                Assert.AreEqual(120.0, link.LengthMeters, 1e-6);
                Assert.AreEqual(30.0, link.FromPosition, 1e-6);
                Assert.AreEqual(50.0, link.ToPosition, 1e-6);
                Assert.AreEqual(3.0, link.FromDistance, 1e-6);
                Assert.AreEqual(0, link.FromEntry);
                Assert.AreEqual(1, link.ToEntry);
                CollectionAssert.AreEqual(new[] { 1, 2 }, link.Entries.Select(entry => entry.ElementId).ToArray());
                Assert.AreEqual(2, link.Entries[0].TileIndex);
                Assert.AreEqual(100.0, link.Entries[0].LengthMeters, 1e-6);
                var trip = timetable.Trips["1_A-B"];
                CollectionAssert.AreEqual(new[] { 10, 11 }, trip.Stops.Select(stop => stop.ObjectId).ToArray());
                Assert.AreEqual("Two", trip.Destination);
                Assert.AreEqual("1", trip.Line);
                Assert.AreEqual(4.5, trip.Profiles.Single().Value, 1e-9);
                var tours = OmsiLineFile.Parse(OmsiTimetableWriter.CreateLine("Busses")).Tours;
                Assert.AreEqual(1, tours.Count);
                Assert.AreEqual("Busses", tours[0].VehicleGroup);
                Assert.AreEqual(1023, tours[0].DayMask);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
