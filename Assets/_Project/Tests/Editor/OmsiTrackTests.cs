using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Tracks and trips on rails: path numbers, the way over the rails, and the files (ADR 097).</summary>
    public sealed class OmsiTrackTests
    {
        private static OmsiMapPath Rail(int id, double x0, double y0, double x1, double y1)
        {
            return new OmsiMapPath
            {
                TileX = 0, TileY = 0, ElementId = id, PathIndex = 0, OnSpline = true, Kind = 2, Direction = 2, LengthMeters = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)),
                Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint((x0 + x1) / 2.0, (y0 + y1) / 2.0), new OmsiMapPoint(x1, y1) }
            };
        }

        private static OmsiTripStopChoice Station(string name, int id, double x, double y)
        {
            return new OmsiTripStopChoice { Name = name, TileIndex = 0, ObjectId = id, World = new OmsiMapPoint(x, y) };
        }

        [Test]
        public void ElementsNumberTheirPathsInTheOrderOfTheTileFile()
        {
            var tile = OmsiRecordFile.Parse(Encoding.UTF8.GetBytes(
                "[version]\r\n14\r\n\r\n[object]\r\n0\r\nSceneryobjects\\a.sco\r\n5\r\n[spline]\r\n0\r\nSplines\\b.sli\r\n6\r\n[spline_h]\r\n0\r\nSplines\\c.sli\r\n7\r\n" +
                "[object]\r\n0\r\nSceneryobjects\\a.sco\r\n8\r\n[object]\r\n0\r\nSceneryobjects\\none.sco\r\n9\r\n[spline]\r\n0\r\nSplines\\b.sli\r\n10\r\n"), null);
            Func<string, int?> counts = asset => asset.EndsWith("a.sco") ? 2 : asset.EndsWith("b.sli") ? 1 : asset.EndsWith("c.sli") ? 3 : (int?)null;
            var numbers = OmsiTilePathNumbers.Of(tile, counts);
            // 5: paths 0-1, 6: path 2, 7: paths 3-5, 8: paths 6-7, 9 has none, 10 starts at 8.
            Assert.AreEqual(0, numbers[5]);
            Assert.AreEqual(2, numbers[6]);
            Assert.AreEqual(3, numbers[7]);
            Assert.AreEqual(6, numbers[8]);
            Assert.AreEqual(8, numbers[9]);
            Assert.AreEqual(8, numbers[10]);
        }

        [Test]
        public void TheWayOverTheRailsMakesEntriesAndPlacesTheStationsAlongThem()
        {
            var network = OmsiPathNetwork.Build(new[] { Rail(1, 0, 0, 100, 0), Rail(2, 100, 0, 200, 0), Rail(3, 200, 0, 300, 0) }, 2);
            var plan = OmsiTrackPlanner.Plan(new[] { Station("A", 10, 30, -3), Station("B", 11, 250, -3) }, network, (x, y) => 0);
            Assert.IsTrue(plan.IsValid, string.Join(" ", plan.Problems));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, plan.Entries.Select(entry => entry.ElementId).ToArray());
            Assert.AreEqual(0, plan.Stations[0].Entry);
            Assert.AreEqual(30.0, plan.Stations[0].Position, 1e-6);
            Assert.AreEqual(2, plan.Stations[1].Entry);
            Assert.AreEqual(50.0, plan.Stations[1].Position, 1e-6);
            Assert.AreEqual(250.0, plan.Stations[1].Distance, 1e-6);
            Assert.AreEqual(220.0, plan.LengthMeters, 1e-6);
            // South of an east-bound rail is its right-hand side.
            Assert.AreEqual(3.0, plan.Stations[0].Lateral, 1e-6);
        }

        [Test]
        public void TheWayBackTakesTheRailTheOtherWayAndAStationNeedsARailNearby()
        {
            var network = OmsiPathNetwork.Build(new[] { Rail(1, 0, 0, 100, 0), Rail(2, 100, 0, 200, 0) }, 2);
            var back = OmsiTrackPlanner.Plan(new[] { Station("B", 11, 150, -3), Station("A", 10, 30, 3) }, network, (x, y) => 0);
            Assert.IsTrue(back.IsValid, string.Join(" ", back.Problems));
            CollectionAssert.AreEqual(new[] { 2, 1 }, back.Entries.Select(entry => entry.ElementId).ToArray());
            var far = OmsiTrackPlanner.Plan(new[] { Station("A", 10, 30, -3), Station("Z", 12, 150, 80) }, network, (x, y) => 0);
            Assert.IsFalse(far.IsValid);
            StringAssert.Contains("Z", string.Join(" ", far.Problems));
            var gap = OmsiPathNetwork.Build(new[] { Rail(1, 0, 0, 100, 0), Rail(2, 160, 0, 260, 0) }, 2);
            var cut = OmsiTrackPlanner.Plan(new[] { Station("A", 10, 30, -3), Station("B", 11, 200, -3) }, gap, (x, y) => 0);
            Assert.IsFalse(cut.IsValid);
            StringAssert.Contains("No way", string.Join(" ", cut.Problems));
        }

        [Test]
        public void ATrackAndATripWriteAsOMSIHasThemAndReadBack()
        {
            var entries = new[]
            {
                new OmsiPathEntry { ElementId = 8811, PathIndex = 0, TileIndex = 17, LengthMeters = 300.0 },
                new OmsiPathEntry { ElementId = 130, PathIndex = 1, TileIndex = 5, LengthMeters = 100.0 }
            };
            var track = Encoding.UTF8.GetString(OmsiTrackWriter.CreateTrack(entries, new[] { 0, 7 }));
            StringAssert.Contains("   1:\r\n[track_entry]\r\n130\r\n1\r\n5\r\n8\r\n100.0000000000\r\n0\r\n", track);
            StringAssert.Contains("Time Table Track File", track);
            var trip = Encoding.UTF8.GetString(OmsiTrackWriter.CreateTrip("RB2", "Halberstadt", "RB", new[]
            {
                new OmsiTrackStation { Stop = Station("Grundorf", 8789, 0, 0), Entry = 1, Position = 89.277, Distance = 389.277, Lateral = -0.206 }
            }, "standard", 4.0));
            StringAssert.Contains("[trip]\r\nRB2\r\nHalberstadt\r\nRB\r\n", trip);
            StringAssert.Contains("[station]\r\n8789\r\n1\r\nGrundorf\r\n0\r\n-0.206\r\n389.277\r\n89.277\r\n10.000\r\n", trip);
            StringAssert.Contains("[profile]\r\nstandard\r\n4.000", trip);
        }

        [Test]
        public void ARailLineIsWrittenWithATrackAndATripThatTheCheckAccepts()
        {
            var root = Path.Combine(Path.GetTempPath(), "mapforge-rail-" + Guid.NewGuid().ToString("N"));
            try
            {
                var map = Path.Combine(root, "OMSI 2", "maps", "Railtown");
                Directory.CreateDirectory(Path.Combine(map, "TTData"));
                Directory.CreateDirectory(Path.Combine(root, "OMSI 2", "Splines"));
                File.WriteAllBytes(Path.Combine(root, "OMSI 2", "Splines", "rail.sli"), Encoding.UTF8.GetBytes("[path]\r\n2\r\n0\r\n0.449\r\n0\r\n2\r\n"));
                Func<string[], byte[]> utf16 = lines => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
                File.WriteAllBytes(Path.Combine(map, "global.cfg"), utf16(new[] { "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "" }));
                File.WriteAllBytes(Path.Combine(map, "tile_0_0.map"), utf16(new[] { "[version]", "14", "", "[spline]", "0", "Splines\\rail.sli", "77" }));
                var session = OmsiMapSession.OpenProject(Path.Combine(map, "global.cfg"), Path.Combine(root, "Projects", "Rail.omproj"));
                var summary = session.Read();
                var network = OmsiPathNetwork.Build(new[] { Rail(77, 0, 0, 100, 0) }, 2);
                var plan = session.PlanTrack(summary, new[] { Station("One", 10, 20, -3), Station("Two", 11, 80, -3) }, network);
                Assert.IsTrue(plan.IsValid, string.Join(" ", plan.Problems));

                var change = session.CreateRailLine(summary, "R1", "Trains", new[] { new OmsiRailTripSpec { Name = "R1_One-Two", Destination = "Two", Plan = plan, RunningMinutes = 2.0 } });
                Assert.IsNotNull(change);
                var timetable = OmsiTimetable.Read(session.Workspace);
                Assert.AreEqual(77, timetable.Tracks["R1_One-Two"].Entries.Single().ElementId);
                var trip = timetable.Trips["R1_One-Two"];
                Assert.AreEqual("R1_One-Two", trip.Track);
                CollectionAssert.AreEqual(new[] { 10, 11 }, trip.Stops.Select(stop => stop.ObjectId).ToArray());
                Assert.IsTrue(trip.Stops.All(stop => stop.IsLegacy && stop.TrackEntry == 0));
                Assert.AreEqual("R1", timetable.Lines.Single().Name);
                var findings = OmsiTimetableCheck.Check(timetable, summary).Problems.Select(problem => problem.ToString()).ToList();
                // The made-up tile holds neither the stations nor a readable spline, so only those findings may remain.
                Assert.IsTrue(findings.All(text => text.Contains("does not contain")), string.Join("\n", findings));
                Assert.Throws<InvalidOperationException>(() => session.CreateRailLine(summary, "R1", "Trains", new[] { new OmsiRailTripSpec { Name = "R1_One-Two", Destination = "Two", Plan = plan, RunningMinutes = 2.0 } }));
                session.Undo(summary, change);
                Assert.IsFalse(session.Workspace.FileExists("TTData\\R1_One-Two.ttr"));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
