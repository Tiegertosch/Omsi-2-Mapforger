using System;
using System.Collections.Generic;
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
        public void AWaypointLeadsTheWayOverAnotherRoadAndTheLengthStaysTheSumOfTheLanes()
        {
            var a = Straight(1, 0, 0, 0, 100, 0);
            var shortWay = Straight(2, 0, 100, 0, 200, 0);
            var upperOne = Straight(3, 0, 100, 0, 150, 10);
            var upperTwo = Straight(4, 0, 150, 10, 200, 0);
            var b = Straight(5, 0, 200, 0, 300, 0);
            var network = OmsiPathNetwork.Build(new[] { a, shortWay, upperOne, upperTwo, b });
            var stops = new[]
            {
                new OmsiTripStopChoice { Name = "One", TileIndex = 0, ObjectId = 10, World = new OmsiMapPoint(30, -3) },
                new OmsiTripStopChoice { Name = "Two", TileIndex = 0, ObjectId = 11, World = new OmsiMapPoint(250, -3) }
            };
            var direct = OmsiTripPlanner.Plan(stops, new OmsiTimetable(), network, (x, y) => 0);
            CollectionAssert.AreEqual(new[] { 1, 2, 5 }, direct.Legs[0].Entries.Select(entry => entry.ElementId).ToArray());
            Assert.AreEqual(70.0 + 100.0 + 50.0, direct.Legs[0].LengthMeters, 1e-6);

            var items = new List<OmsiTripStopChoice> { stops[0], new OmsiTripStopChoice { Name = "via", IsWaypoint = true, World = new OmsiMapPoint(150, 12) }, stops[1] };
            var led = OmsiTripPlanner.Plan(items, new OmsiTimetable(), network, (x, y) => 0);
            Assert.IsTrue(led.IsValid, string.Join(";", led.Problems));
            Assert.IsTrue(led.Legs[0].HasWaypoints);
            var ids = led.Legs[0].Entries.Select(entry => entry.ElementId).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 3, 4, 5 }, ids);
            var expected = 70.0 + upperOne.LengthMeters + upperTwo.LengthMeters + 50.0;
            Assert.AreEqual(expected, led.Legs[0].LengthMeters, 1e-6);

            // A waypoint far from every lane keeps the trip from being made; one before the first stop is left out.
            var lost = new List<OmsiTripStopChoice> { stops[0], new OmsiTripStopChoice { IsWaypoint = true, World = new OmsiMapPoint(150, 300) }, stops[1] };
            Assert.IsFalse(OmsiTripPlanner.Plan(lost, new OmsiTimetable(), network, (x, y) => 0).IsValid);
            var leading = new List<OmsiTripStopChoice> { new OmsiTripStopChoice { IsWaypoint = true, World = new OmsiMapPoint(150, 300) }, stops[0], stops[1] };
            Assert.IsTrue(OmsiTripPlanner.Plan(leading, new OmsiTimetable(), network, (x, y) => 0).IsValid);
        }

        [Test]
        public void ALegThatTakesOnFromTheLaneTheOneBeforeEndedOnKeepsTheLanesOfATripJoined()
        {
            // Two eastbound lanes of one road to the junction; only the inner one continues to the second stop's road, and the first leg has to end on it.
            var outer = Straight(1, 0, 0, 0, 100, 0);
            var inner = Straight(1, 1, 0, 3, 100, 3);
            var ahead = Straight(2, 0, 100, 3, 200, 3);
            var network = OmsiPathNetwork.Build(new[] { outer, inner, ahead });
            var stops = new[]
            {
                new OmsiTripStopChoice { Name = "One", TileIndex = 0, ObjectId = 10, World = new OmsiMapPoint(20, -1) },
                new OmsiTripStopChoice { Name = "Two", TileIndex = 0, ObjectId = 11, World = new OmsiMapPoint(80, -1) },
                new OmsiTripStopChoice { Name = "Three", TileIndex = 0, ObjectId = 12, World = new OmsiMapPoint(150, 1) }
            };
            var plan = OmsiTripPlanner.Plan(stops, new OmsiTimetable(), network, (x, y) => 0);
            Assert.IsTrue(plan.IsValid, string.Join(";", plan.Problems));
            // Two stands beside both lanes of its road; the second leg leaves on the lane the first arrived on, and that one leads on to the third stop.
            var firstEnd = plan.Legs[0].Entries.Last();
            var secondStart = plan.Legs[1].Entries.First();
            Assert.AreEqual(firstEnd.ElementId, secondStart.ElementId);
            Assert.AreEqual(firstEnd.PathIndex, secondStart.PathIndex);
        }

        [Test]
        public void AReplacedLinkTakesTheOldOnesPlaceInStnLinksAndTheOthersStay()
        {
            Func<int, int, double, bool, OmsiTripLeg> leg = (from, to, length, replaces) => new OmsiTripLeg
            {
                From = new OmsiTripStopChoice { Name = "S" + from, ObjectId = from }, To = new OmsiTripStopChoice { Name = "S" + to, ObjectId = to },
                LengthMeters = length, ReplacesLink = replaces
            };
            var first = leg(1, 2, 100.0, false);
            first.Entries.Add(new OmsiPathEntry { ElementId = 7, PathIndex = 0, TileIndex = 0, LengthMeters = 100.0 });
            var second = leg(2, 3, 200.0, false);
            second.Entries.Add(new OmsiPathEntry { ElementId = 8, PathIndex = 0, TileIndex = 0, LengthMeters = 200.0 });
            var file = OmsiTimetableWriter.AppendStationLinks(null, new[] { first, second });
            var replacement = leg(1, 2, 150.0, true);
            replacement.Entries.Add(new OmsiPathEntry { ElementId = 9, PathIndex = 1, TileIndex = 0, LengthMeters = 150.0 });
            var replaced = OmsiTimetableWriter.AppendStationLinks(file, new[] { replacement });

            var folder = Path.Combine(Path.GetTempPath(), "mapforge-stn-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "TTData"));
                File.WriteAllBytes(Path.Combine(folder, "TTData", "StnLinks.cfg"), replaced);
                var links = OmsiTimetable.Read(new OmsiFolderSource(folder)).StationLinks;
                Assert.AreEqual(2, links.Count);
                var one = links.Single(link => link.FromStopId == 1);
                Assert.AreEqual(150.0, one.LengthMeters, 1e-6);
                Assert.AreEqual(9, one.Entries.Single().ElementId);
                Assert.AreEqual(200.0, links.Single(link => link.FromStopId == 2).LengthMeters, 1e-6);
                StringAssert.DoesNotContain("100.0000000000", Encoding.UTF8.GetString(replaced));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void TheNameOfAStopIsSetWhereItIsListedAndAddedWhereItIsNot()
        {
            var listed = Encoding.UTF8.GetBytes("---\r\nTime Table BusStopList File\r\n---\r\n\r\n[busstop]\r\nOld\r\n3\r\n5\r\n0.0000000000\r\n0\r\n0\r\n\r\n\r\n[busstop]\r\nOther\r\n3\r\n6\r\n2.0000000000\r\n0\r\n0\r\n\r\n\r\n");
            var renamed = Encoding.UTF8.GetString(OmsiTimetableWriter.SetBusStopName(listed, 3, 5, "New name"));
            StringAssert.Contains("[busstop]\r\nNew name\r\n3\r\n5\r\n", renamed);
            StringAssert.Contains("[busstop]\r\nOther\r\n3\r\n6\r\n2.0000000000", renamed);
            StringAssert.DoesNotContain("Old", renamed);
            // The same name again changes nothing.
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(renamed), OmsiTimetableWriter.SetBusStopName(Encoding.UTF8.GetBytes(renamed), 3, 5, "New name"));
            var added = Encoding.UTF8.GetString(OmsiTimetableWriter.SetBusStopName(listed, 3, 9, "Third"));
            StringAssert.StartsWith(Encoding.UTF8.GetString(listed), added);
            StringAssert.EndsWith("[busstop]\r\nThird\r\n3\r\n9\r\n0.0000000000\r\n0\r\n0\r\n\r\n\r\n", added);
            // A map without a stop list gets one.
            StringAssert.Contains("[busstop]\r\nFirst\r\n0\r\n1\r\n", Encoding.UTF8.GetString(OmsiTimetableWriter.SetBusStopName(null, 0, 1, "First")));
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
                // Every file the planner wrote is one the timetable check can read; what it lists is only what the test left out (the objects are no part of a map here).
                var findings = OmsiTimetableCheck.Check(timetable, new OmsiMapSummary()).Problems.Select(problem => problem.ToString()).ToList();
                Assert.IsFalse(findings.Any(text => text.Contains("could not be read") || text.Contains("no station link") || text.Contains("no [profile]") ||
                                                    text.Contains("no .ttp file") || text.Contains("no .ttr file")), string.Join("\n", findings));
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
