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

        private void FreeTheStop()
        {
            var line = session.ReadLineFile("1");
            line.RemoveTrip(0, 0);
            session.WriteLineFile("1", line);
            Assert.IsNotNull(session.DeleteTrip("T1"));
        }

        private byte[] Current(string relative)
        {
            return session.Workspace.FileExists(relative) ? session.Workspace.ReadAllBytes(relative) : null;
        }

        [Test]
        public void DeletingAnUnusedStopTakesItsEntriesAlongInOneStep()
        {
            FreeTheStop();
            var paths = new[] { "TTData\\Busstops.cfg", "TTData\\StnLinks.cfg", "tile_0_0.map", "global.cfg" };
            var before = paths.Select(Current).ToArray();
            var otherLink = Encoding.UTF8.GetString(before[1]);
            otherLink = otherLink.Substring(otherLink.IndexOf("Other ==> Third", StringComparison.Ordinal));

            var result = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0]);
            Assert.IsTrue(result.Succeeded, string.Join(", ", result.Blockers.Select(reference => reference.Kind + " " + reference.File)));
            Assert.IsTrue(result.Change.ChangesTimetable);
            var after = paths.Select(Current).ToArray();
            var timetable = OmsiTimetable.Read(session.Workspace);
            CollectionAssert.AreEqual(new[] { "Other" }, timetable.BusStops.Select(stop => stop.Name).ToArray());
            CollectionAssert.AreEqual(new[] { 99 }, timetable.StationLinks.Select(link => link.FromStopId).ToArray());
            StringAssert.EndsWith(otherLink, Encoding.UTF8.GetString(after[1]), "The link of the other stops stays as it was.");
            Assert.IsFalse(session.BuildReferenceIndex().To(stopId).Any());

            session.Undo(summary, result.Change);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(before[index], Current(paths[index]), paths[index] + " after undo");
            Assert.AreEqual(1, summary.Tiles[0].Objects.Count);
            session.Redo(summary, result.Change);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(after[index], Current(paths[index]), paths[index] + " after redo");
            Assert.AreEqual(0, summary.Tiles[0].Objects.Count);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(map, "TTData", "Busstops.cfg")), before[0], "The original map keeps its file.");
        }

        [Test]
        public void AStopATripUsesIsNotDeleted()
        {
            var bus = Current("TTData\\Busstops.cfg");
            var links = Current("TTData\\StnLinks.cfg");
            var result = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0]);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Blockers.Any(reference => reference.Kind == "trip stop" && reference.File.EndsWith("T1.ttp", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(result.Blockers.Any(reference => reference.Kind == "bus stop" || reference.Kind == "station link"), "Only what has to change first is named.");
            CollectionAssert.AreEqual(bus, Current("TTData\\Busstops.cfg"));
            CollectionAssert.AreEqual(links, Current("TTData\\StnLinks.cfg"));
            Assert.AreEqual(1, summary.Tiles[0].Objects.Count);
        }

        [Test]
        public void TwoLinkedStopsDeletedTogetherTakeTheirEntriesAlongAndComeBack()
        {
            Utf16("tile_0_0.map", "[version]", "14", "", "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[object]", "0", "b.sco", "6", "30", "40", "0", "0", "0", "0", "0", "");
            session = OmsiMapSession.OpenProject(Path.Combine(map, "global.cfg"), Path.Combine(root, "Projects", "Two.omproj"));
            summary = session.Read();
            var tile = summary.Tiles[0];
            var first = tile.Objects[0];
            var second = tile.Objects[1];
            Bytes("TTData\\Busstops.cfg", OmsiTimetableWriter.AppendBusStops(null, new[]
            {
                new OmsiTripStopChoice { Name = "One", TileIndex = 0, ObjectId = first.Id },
                new OmsiTripStopChoice { Name = "Two", TileIndex = 0, ObjectId = second.Id },
                new OmsiTripStopChoice { Name = "Other", TileIndex = 0, ObjectId = 99 }
            }));
            Bytes("TTData\\StnLinks.cfg", Encoding.UTF8.GetBytes(
                "---------------------------\r\nTime Table StnLinkList File\r\n---------------------------\r\n\r\n" +
                "One ==> Two\r\n\r\n[StnLink]\r\n120\r\n" + first.Id + "\r\n" + second.Id + "\r\n3\r\n3\r\n30\r\n50\r\n0\r\n0\r\n\r\n   0:\r\n[StnLink_entry]\r\n1\r\n0\r\n0\r\n100\r\n-1\r\n0\r\n0\r\n\r\n" +
                "Two ==> One\r\n\r\n[StnLink]\r\n120\r\n" + second.Id + "\r\n" + first.Id + "\r\n3\r\n3\r\n30\r\n50\r\n0\r\n0\r\n\r\n   0:\r\n[StnLink_entry]\r\n2\r\n0\r\n0\r\n100\r\n-1\r\n0\r\n0\r\n\r\n" +
                "Other ==> Third\r\n\r\n[StnLink]\r\n50\r\n99\r\n98\r\n3\r\n3\r\n10\r\n20\r\n0\r\n0\r\n\r\n   0:\r\n[StnLink_entry]\r\n3\r\n0\r\n0\r\n50\r\n-1\r\n0\r\n0\r\n\r\n"));
            File.Delete(Path.Combine(map, "TTData", "T1.ttp"));
            File.Delete(Path.Combine(map, "TTData", "1.ttl"));
            var paths = new[] { "TTData\\Busstops.cfg", "TTData\\StnLinks.cfg", "tile_0_0.map" };
            var before = paths.Select(Current).ToArray();

            // One index for both, as the shell deletes a selection.
            var references = session.BuildReferenceIndex();
            var one = session.DeleteObject(summary, tile, first, references);
            var two = session.DeleteObject(summary, tile, second, references);
            Assert.IsTrue(one.Succeeded && two.Succeeded, string.Join(", ", one.Blockers.Concat(two.Blockers)));
            var timetable = OmsiTimetable.Read(session.Workspace);
            CollectionAssert.AreEqual(new[] { "Other" }, timetable.BusStops.Select(stop => stop.Name).ToArray());
            CollectionAssert.AreEqual(new[] { 99 }, timetable.StationLinks.Select(link => link.FromStopId).ToArray());
            var after = paths.Select(Current).ToArray();

            session.Undo(summary, two.Change);
            session.Undo(summary, one.Change);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(before[index], Current(paths[index]), paths[index] + " after undo");
            session.Redo(summary, one.Change);
            session.Redo(summary, two.Change);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(after[index], Current(paths[index]), paths[index] + " after redo");
        }

        [Test]
        public void AStopALinkOfOtherStopsRunsOverIsNotDeleted()
        {
            var original = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(map, "TTData", "StnLinks.cfg")));
            var other = original.IndexOf("Other ==> Third", StringComparison.Ordinal);
            var entry = original.IndexOf("[StnLink_entry]\r\n3\r\n", other, StringComparison.Ordinal);
            Bytes("TTData\\StnLinks.cfg", Encoding.UTF8.GetBytes(original.Substring(0, entry) + "[StnLink_entry]\r\n" + stopId + "\r\n" +
                                                                  original.Substring(entry + "[StnLink_entry]\r\n3\r\n".Length)));
            FreeTheStop();
            var result = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0]);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Blockers.Any(reference => reference.Kind == "station link path"));
            Assert.IsFalse(result.Blockers.Any(reference => reference.Kind == "bus stop"), "Its own entry would go, so it is not named.");
            Assert.AreEqual(2, OmsiTimetable.Read(session.Workspace).BusStops.Count);
        }

        [Test]
        public void AStopWhoseIdAnotherTileNamesAsBusStopIsNotDeleted()
        {
            FreeTheStop();
            var stops = Current("TTData\\Busstops.cfg");
            Bytes("TTData\\Busstops.cfg", OmsiTimetableWriter.AppendBusStops(stops, new[] { new OmsiTripStopChoice { Name = "Elsewhere", TileIndex = 1, ObjectId = stopId } }));
            var bus = Current("TTData\\Busstops.cfg");
            var links = Current("TTData\\StnLinks.cfg");

            var result = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0]);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, result.Blockers.Count(reference => reference.Kind == "bus stop"), "Only the entry that stays is named.");
            Assert.IsFalse(result.Blockers.Any(reference => reference.Kind == "station link" || reference.Kind == "station link path"));
            CollectionAssert.AreEqual(bus, Current("TTData\\Busstops.cfg"));
            CollectionAssert.AreEqual(links, Current("TTData\\StnLinks.cfg"));
            Assert.AreEqual(1, summary.Tiles[0].Objects.Count);
        }

        [Test]
        public void ABusStopOnAnAttachedObjectGoesWithItsParent()
        {
            Utf16("tile_0_0.map", "[version]", "14", "", "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[attachObj]", "0", "sign.sco", "8", "5", "0", "1", "0", "0", "0", "0", "");
            File.Delete(Path.Combine(map, "TTData", "T1.ttp"));
            File.Delete(Path.Combine(map, "TTData", "1.ttl"));
            File.Delete(Path.Combine(map, "TTData", "StnLinks.cfg"));
            Bytes("TTData\\Busstops.cfg", OmsiTimetableWriter.AppendBusStops(null, new[]
            {
                new OmsiTripStopChoice { Name = "Sign", TileIndex = 0, ObjectId = 8 },
                new OmsiTripStopChoice { Name = "Other", TileIndex = 0, ObjectId = 99 }
            }));
            session = OmsiMapSession.OpenProject(Path.Combine(map, "global.cfg"), Path.Combine(root, "Projects", "Attached.omproj"));
            summary = session.Read();
            var before = Current("TTData\\Busstops.cfg");

            var result = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0]);
            Assert.IsTrue(result.Succeeded, string.Join(", ", result.Blockers.Select(reference => reference.Kind + " " + reference.File)));
            Assert.IsTrue(result.Change.ChangesTimetable);
            CollectionAssert.AreEqual(new[] { "Other" }, OmsiTimetable.Read(session.Workspace).BusStops.Select(stop => stop.Name).ToArray());
            session.Undo(summary, result.Change);
            CollectionAssert.AreEqual(before, Current("TTData\\Busstops.cfg"));
        }

        [Test]
        public void AUtf16BusStopFileWithMixedLineEndingsStaysLossless()
        {
            FreeTheStop();
            var kept = "[busstop]\nOther\r\n0\n99\r\n0\n0\r\n0\n\r\n";
            var text = "[busstop]\r\nOne\n0\r\n" + stopId + "\n0\r\n0\n0\r\n\n" + kept;
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
            Bytes("TTData\\Busstops.cfg", bytes);

            var result = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0]);
            Assert.IsTrue(result.Succeeded, string.Join(", ", result.Blockers.Select(reference => reference.Kind + " " + reference.File)));
            var after = Current("TTData\\Busstops.cfg");
            CollectionAssert.AreEqual(Encoding.Unicode.GetPreamble(), after.Take(2).ToArray(), "The BOM stays.");
            var afterText = Encoding.Unicode.GetString(after, 2, after.Length - 2);
            StringAssert.EndsWith(kept, afterText, "The other stop keeps its line endings.");
            StringAssert.DoesNotContain("One", afterText);
            session.Undo(summary, result.Change);
            CollectionAssert.AreEqual(bytes, Current("TTData\\Busstops.cfg"));
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

        private static byte[] TrackTrip(string track)
        {
            return Encoding.UTF8.GetBytes("[trip]\r\n" + track + "\r\nOther\r\n1\r\n0\r\n\r\n[station_typ2]\r\n99\r\n\r\n");
        }

        [Test]
        public void DeletingALineTakesItsOwnTripsAndUnusedTracksAlongAndComesBack()
        {
            // Line 1 runs T1, T2 (track R2) and Shared (track R3); line 2 runs Shared, so Shared and R3 stay.
            Bytes("TTData\\T2.ttp", TrackTrip("R2"));
            Bytes("TTData\\Shared.ttp", TrackTrip("R3"));
            Bytes("TTData\\R2.ttr", Encoding.UTF8.GetBytes("[track_entry]\r\n1\r\n0\r\n0\r\n0\r\n100\r\n\r\n"));
            Bytes("TTData\\R3.ttr", Encoding.UTF8.GetBytes("[track_entry]\r\n2\r\n0\r\n0\r\n0\r\n100\r\n\r\n"));
            Bytes("TTData\\2.ttl", OmsiTimetableWriter.CreateLine("Busses"));
            var line = session.ReadLineFile("1");
            line.AddTrip(0, "T2", 0, 400.0);
            line.AddTrip(0, "Shared", 0, 440.0);
            session.WriteLineFile("1", line);
            var other = session.ReadLineFile("2");
            other.AddTrip(0, "Shared", 0, 500.0);
            session.WriteLineFile("2", other);

            var paths = new[] { "TTData\\1.ttl", "TTData\\2.ttl", "TTData\\T1.ttp", "TTData\\T2.ttp", "TTData\\Shared.ttp", "TTData\\R2.ttr", "TTData\\R3.ttr" };
            var before = paths.Select(Current).ToArray();
            var change = session.DeleteLine("1");
            Assert.IsNotNull(change);
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\T1.ttp", "TTData\\T2.ttp", "TTData\\R2.ttr" },
                change.Files.Where(file => file.After == null).Select(file => file.RelativePath).ToArray());
            Assert.IsNull(Current("TTData\\T1.ttp"));
            Assert.IsNull(Current("TTData\\T2.ttp"));
            Assert.IsNull(Current("TTData\\R2.ttr"));
            Assert.IsNotNull(Current("TTData\\Shared.ttp"), "A trip another line runs stays.");
            Assert.IsNotNull(Current("TTData\\R3.ttr"), "A track a remaining trip drives stays.");
            Assert.IsTrue(File.Exists(Path.Combine(map, "TTData", "T2.ttp")), "The original map keeps its file.");
            var after = paths.Select(Current).ToArray();

            session.Undo(summary, change);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(before[index], Current(paths[index]), paths[index] + " after undo");
            session.Redo(summary, change);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(after[index], Current(paths[index]), paths[index] + " after redo");

            // With its trips gone, nothing stops at the first stop any more, so it can be deleted.
            var result = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0]);
            Assert.IsTrue(result.Succeeded, string.Join(", ", result.Blockers.Select(reference => reference.Kind + " " + reference.File)));
        }

        private void Variant(string chronoCfg, params (string Name, byte[] Bytes)[] files)
        {
            var folder = Path.Combine(map, "Chrono", "0100_Works");
            Directory.CreateDirectory(Path.Combine(folder, "TTData"));
            File.WriteAllBytes(Path.Combine(folder, "Chrono.cfg"), Encoding.UTF8.GetBytes(chronoCfg));
            foreach (var file in files)
                File.WriteAllBytes(Path.Combine(folder, "TTData", file.Name), file.Bytes);
        }

        private static byte[] LineRunning(params string[] trips)
        {
            var line = OmsiLineFile.Parse(OmsiTimetableWriter.CreateLine("Busses"));
            foreach (var trip in trips)
                line.AddTrip(0, trip, 0, 400.0);
            return line.ToBytes();
        }

        private static readonly byte[] Track = Encoding.UTF8.GetBytes("[track_entry]\r\n1\r\n0\r\n0\r\n0\r\n100\r\n\r\n");

        private static string[] Removed(OmsiTerrainChange change)
        {
            return change.Files.Where(file => file.After == null).Select(file => file.RelativePath).ToArray();
        }

        [Test]
        public void ATripALineOfAChronoVariantRunsStaysWithTheLine()
        {
            Variant("[startdate]\r\n20000101\r\n\r\n", ("9.ttl", LineRunning("T1")));
            var change = session.DeleteLine("1");
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl" }, Removed(change));
            Assert.IsNotNull(Current("TTData\\T1.ttp"));
        }

        [Test]
        public void ATrackATripOfAChronoVariantDrivesStays()
        {
            Bytes("TTData\\T2.ttp", TrackTrip("R2"));
            Bytes("TTData\\R2.ttr", Track);
            session.Workspace.Save("TTData\\1.ttl", LineRunning("T2"));
            Variant("[startdate]\r\n20000101\r\n\r\n", ("VT.ttp", TrackTrip("R2")));
            var change = session.DeleteLine("1");
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\T2.ttp" }, Removed(change));
            Assert.IsNotNull(Current("TTData\\R2.ttr"));
        }

        [Test]
        public void ATrackATripWithoutATourDrivesStays()
        {
            Bytes("TTData\\T2.ttp", TrackTrip("R2"));
            Bytes("TTData\\Loose.ttp", TrackTrip("r2"));
            Bytes("TTData\\R2.ttr", Track);
            session.Workspace.Save("TTData\\1.ttl", LineRunning("T2"));
            var change = session.DeleteLine("1");
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\T2.ttp" }, Removed(change));
            Assert.IsNotNull(Current("TTData\\R2.ttr"));
            Assert.IsNotNull(Current("TTData\\Loose.ttp"));
        }

        [Test]
        public void ALineAChronoVariantSwitchesOffIsRefusedAndNothingIsWritten()
        {
            Variant("[startdate]\r\n20000101\r\n\r\n[deactivate_lines]\r\n1\r\n1\r\n\r\n");
            var paths = new[] { "TTData\\1.ttl", "TTData\\T1.ttp" };
            var before = paths.Select(Current).ToArray();
            var refused = Assert.Throws<InvalidOperationException>(() => session.DeleteLine("1"));
            StringAssert.Contains("0100_Works", refused.Message);
            for (var index = 0; index < paths.Length; index++)
                CollectionAssert.AreEqual(before[index], Current(paths[index]), paths[index]);
            CollectionAssert.IsEmpty(session.Workspace.DeletedFiles());
        }

        [Test]
        public void ATripOnlyTheWorkspaceHasLeavesNoDeletionEntryAndComesBack()
        {
            session.Workspace.Save("TTData\\New.ttp", TrackTrip("R9"));
            session.Workspace.Save("TTData\\R9.ttr", Track);
            var line = session.ReadLineFile("1");
            line.AddTrip(0, "New", 0, 400.0);
            session.WriteLineFile("1", line);
            var change = session.DeleteLine("1");
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\T1.ttp", "TTData\\New.ttp", "TTData\\R9.ttr" }, Removed(change));
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\T1.ttp" }, session.Workspace.DeletedFiles(), "Only files of the original map are listed.");
            session.Undo(summary, change);
            CollectionAssert.IsEmpty(session.Workspace.DeletedFiles());
            Assert.IsNotNull(Current("TTData\\New.ttp"));
            Assert.IsNotNull(Current("TTData\\R9.ttr"));
        }

        [Test]
        public void ATourNamingATripInOtherLetterCaseTakesItsFile()
        {
            Bytes("TTData\\T2.ttp", TrackTrip("R2"));
            Bytes("TTData\\R2.ttr", Track);
            session.Workspace.Save("TTData\\1.ttl", LineRunning("t2"));
            var change = session.DeleteLine("1");
            var removed = Removed(change);
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\T2.ttp", "TTData\\R2.ttr" }, removed);
            Assert.IsTrue(removed.Contains("TTData\\T2.ttp", StringComparer.Ordinal), "The path is the file's own name.");
            Assert.IsNull(Current("TTData\\T2.ttp"));
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\R2.ttr", "TTData\\T2.ttp" }, session.Workspace.DeletedFiles());
        }

        [Test]
        public void ATripNamingItsTrackInOtherLetterCaseTakesTheTrackFile()
        {
            Bytes("TTData\\T2.ttp", TrackTrip("r2"));
            Bytes("TTData\\R2.ttr", Track);
            session.Workspace.Save("TTData\\1.ttl", LineRunning("T2"));
            var change = session.DeleteLine("1");
            var removed = Removed(change);
            CollectionAssert.AreEquivalent(new[] { "TTData\\1.ttl", "TTData\\T2.ttp", "TTData\\R2.ttr" }, removed);
            Assert.IsTrue(removed.Contains("TTData\\R2.ttr", StringComparer.Ordinal), "The path is the file's own name.");
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
