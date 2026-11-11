using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Deleting and renaming lines, trips and bus stops with their references (ADR 096).</summary>
    public sealed class OmsiTimetableEditTests
    {
        private string root;
        private string map;
        private OmsiMapSession session;
        private OmsiMapSummary summary;
        private int stopId;

        [SetUp]
        public void CreateMap()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-ttedit-" + Guid.NewGuid().ToString("N"));
            map = Path.Combine(root, "maps", "Edittown");
            Directory.CreateDirectory(Path.Combine(map, "TTData"));
            Utf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "");
            Utf16("tile_0_0.map", "[version]", "14", "", "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "");
            session = OmsiMapSession.OpenProject(Path.Combine(map, "global.cfg"), Path.Combine(root, "Projects", "Edit.omproj"));
            summary = session.Read();
            stopId = summary.Tiles[0].Objects[0].Id;
            var stops = new[]
            {
                new OmsiTripStopChoice { Name = "One", TileIndex = 0, ObjectId = stopId },
                new OmsiTripStopChoice { Name = "Other", TileIndex = 0, ObjectId = 99 }
            };
            Bytes("TTData\\Busstops.cfg", OmsiTimetableWriter.AppendBusStops(null, stops));
            Bytes("TTData\\StnLinks.cfg", Encoding.UTF8.GetBytes(
                "---------------------------\r\nTime Table StnLinkList File\r\n---------------------------\r\n\r\nCreated with OMSI MapWorks\r\n\r\n" +
                "One ==> Other\r\n\r\n[StnLink]\r\n120\r\n" + stopId + "\r\n99\r\n3\r\n3\r\n30\r\n50\r\n0\r\n1\r\n\r\n" +
                "   0:\r\n[StnLink_entry]\r\n1\r\n0\r\n0\r\n100\r\n-1\r\n0\r\n0\r\n\r\n   1:\r\n[StnLink_entry]\r\n2\r\n0\r\n0\r\n100\r\n-1\r\n0\r\n0\r\n\r\n" +
                "Other ==> Third\r\n\r\n[StnLink]\r\n50\r\n99\r\n98\r\n3\r\n3\r\n10\r\n20\r\n0\r\n0\r\n\r\n   0:\r\n[StnLink_entry]\r\n3\r\n0\r\n0\r\n50\r\n-1\r\n0\r\n0\r\n\r\n"));
            Bytes("TTData\\T1.ttp", OmsiTimetableWriter.CreateTrip("Other", "1", new[] { stopId, 99 }, "standard", 4.0));
            Bytes("TTData\\1.ttl", OmsiTimetableWriter.CreateLine("Busses"));
            var line = session.ReadLineFile("1");
            line.AddTrip(0, "T1", 0, 360.0);
            session.WriteLineFile("1", line);
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private void Utf16(string relative, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(map, relative), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray());
        }

        private void Bytes(string relative, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(map, relative), bytes);
        }

        [Test]
        public void ATripThatATourRunsIsKeptAndOneNoTourRunsGoes()
        {
            var refused = Assert.Throws<InvalidOperationException>(() => session.DeleteTrip("T1"));
            StringAssert.Contains("1 ", refused.Message);
            Assert.IsTrue(session.Workspace.FileExists("TTData\\T1.ttp"));

            var line = session.ReadLineFile("1");
            line.RemoveTrip(0, 0);
            session.WriteLineFile("1", line);
            var change = session.DeleteTrip("T1");
            Assert.IsNotNull(change);
            Assert.IsFalse(session.Workspace.FileExists("TTData\\T1.ttp"));
            Assert.IsTrue(File.Exists(Path.Combine(map, "TTData", "T1.ttp")), "The original map keeps its file.");
        }

        [Test]
        public void ARenamedTripChangesTheToursThatRunIt()
        {
            var change = session.RenameTrip("T1", "Round");
            Assert.IsNotNull(change);
            Assert.IsFalse(session.Workspace.FileExists("TTData\\T1.ttp"));
            Assert.IsTrue(session.Workspace.FileExists("TTData\\Round.ttp"));
            Assert.AreEqual("Round", session.ReadLineFile("1").Tours[0].Trips[0].Trip);
            Assert.Throws<InvalidOperationException>(() => session.RenameTrip("Missing", "x"));
            Assert.Throws<ArgumentException>(() => session.RenameTrip("Round", "a/b"));
            var timetable = OmsiTimetable.Read(session.Workspace);
            Assert.IsTrue(timetable.Trips.ContainsKey("Round"));
            Assert.IsFalse(timetable.Trips.ContainsKey("T1"));
            Assert.AreEqual(0, OmsiTimetableCheck.Check(timetable, summary).Problems.Count(problem => problem.ToString().Contains("no .ttp file")));
        }

        [Test]
        public void ALineCanBeRenamedAndDeleted()
        {
            Assert.IsNotNull(session.RenameLine("1", "Express"));
            CollectionAssert.AreEqual(new[] { "Express" }, session.TimetableLineNames());
            Assert.IsNotNull(session.DeleteLine("Express"));
            Assert.AreEqual(0, session.TimetableLineNames().Count);
            Assert.IsTrue(File.Exists(Path.Combine(map, "TTData", "1.ttl")));
        }

        [Test]
        public void ABusStopGoesWithItsLinksUnlessATripStopsThere()
        {
            var tile = summary.Tiles[0];
            var record = tile.Objects[0];
            var refused = Assert.Throws<InvalidOperationException>(() => session.RemoveBusStop(summary, tile, record));
            StringAssert.Contains("T1", refused.Message);

            // The tour still runs T1, so take it out of the tour first.
            var line = session.ReadLineFile("1");
            line.RemoveTrip(0, 0);
            session.WriteLineFile("1", line);
            Assert.IsNotNull(session.DeleteTrip("T1"));
            var change = session.RemoveBusStop(summary, tile, record);
            Assert.IsNotNull(change);
            var timetable = OmsiTimetable.Read(session.Workspace);
            CollectionAssert.AreEqual(new[] { "Other" }, timetable.BusStops.Select(stop => stop.Name).ToArray());
            CollectionAssert.AreEqual(new[] { 99 }, timetable.StationLinks.Select(link => link.FromStopId).ToArray());
        }

        private static OmsiMapPath Straight(int id, double x0, double y0, double x1, double y1)
        {
            return new OmsiMapPath
            {
                TileX = 0, TileY = 0, ElementId = id, PathIndex = 0, OnSpline = true, Kind = 0, Direction = 0, LengthMeters = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)),
                Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint((x0 + x1) / 2.0, (y0 + y1) / 2.0), new OmsiMapPoint(x1, y1) }
            };
        }

        [Test]
        public void StopsAreReplacedInAnExistingTripAndTheTimetableGetsWhatItLacks()
        {
            var network = OmsiPathNetwork.Build(new[] { Straight(1, 0, 0, 100, 0), Straight(2, 100, 0, 200, 0), Straight(3, 200, 0, 300, 0) });
            var stops = new[]
            {
                new OmsiTripStopChoice { Name = "One", TileIndex = 0, ObjectId = stopId, World = new OmsiMapPoint(30, -3) },
                new OmsiTripStopChoice { Name = "Other", TileIndex = 0, ObjectId = 99, World = new OmsiMapPoint(150, -3) },
                new OmsiTripStopChoice { Name = "Far", TileIndex = 0, ObjectId = 77, World = new OmsiMapPoint(250, -3), IsNew = true }
            };
            var plan = session.PlanTrip(summary, OmsiTimetable.Read(session.Workspace), stops, network, true);
            Assert.IsTrue(plan.IsValid, string.Join(" ", plan.Problems));

            var change = session.ReplaceTripStops("T1", plan, "Far");
            Assert.IsNotNull(change);
            var timetable = OmsiTimetable.Read(session.Workspace);
            CollectionAssert.AreEqual(new[] { stopId, 99, 77 }, timetable.Trips["T1"].Stops.Select(stop => stop.ObjectId).ToArray());
            Assert.AreEqual("Far", timetable.Trips["T1"].Destination);
            CollectionAssert.Contains(timetable.BusStops.Select(stop => stop.ObjectId).ToArray(), 77);
            Assert.IsTrue(timetable.StationLinks.Exists(link => link.FromStopId == 99 && link.ToStopId == 77));
            Assert.AreEqual(1, timetable.StationLinks.Count(link => link.FromStopId == stopId && link.ToStopId == 99), "The link the map had stays.");
            Assert.IsFalse(OmsiTimetableCheck.Check(timetable, summary).Problems.Any(problem => problem.ToString().Contains("T1.ttp") && problem.ToString().Contains("station link")));
            // The tour that runs the trip keeps running it.
            Assert.AreEqual("T1", session.ReadLineFile("1").Tours[0].Trips[0].Trip);

            session.Undo(summary, change);
            CollectionAssert.AreEqual(new[] { stopId, 99 }, OmsiTimetable.Read(session.Workspace).Trips["T1"].Stops.Select(stop => stop.ObjectId).ToArray());
        }

        [Test]
        public void ADeleteIsUndoneByItsFileStates()
        {
            var change = session.RenameLine("1", "Express");
            Assert.IsNotNull(change);
            session.Undo(summary, change);
            CollectionAssert.AreEqual(new[] { "1" }, session.TimetableLineNames());
            Assert.IsFalse(session.Workspace.IsModified("TTData\\1.ttl") && !session.Workspace.FileExists("TTData\\1.ttl"));
        }
    }
}
