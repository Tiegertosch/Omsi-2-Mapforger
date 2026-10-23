using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTimetableTests
    {
        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-timetable-" + Guid.NewGuid().ToString("N"), "Testmap");
            Directory.CreateDirectory(Path.Combine(mapFolder, "TTData"));
            WriteTile("global.cfg", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            // Tile index 0: two stops, a spline 100 m long and a crossing; tile index 1: a spline of 50 m.
            WriteTile("tile_0_0.map", "[version]", "14", "",
                "[object]", "0", @"Sceneryobjects\stop.sco", "10", "5", "5", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\stop.sco", "11", "95", "5", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\crossing.sco", "12", "150", "150", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "20", "0", "0", "0", "0", "0", "90", "100", "0", "0", "0", "0", "0", "0", "0", "");
            WriteTile("tile_1_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "30", "0", "0", "0", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "");
            WriteText(Path.Combine("TTData", "Busstops.cfg"), "Header", "", "[busstop]", "West", "0", "10", "0.0", "0", "0", "",
                "[busstop]", "East", "0", "11", "0.0", "0", "0", "", "[busstop]", "Ghost", "0", "99", "0.0", "0", "0", "");
            // From stop 10 at 5 m along spline 20 to stop 11 at 95 m: 90 m.
            WriteText(Path.Combine("TTData", "StnLinks.cfg"), "Header", "", "[StnLink]", "90.0", "10", "11", "1.5", "1.5", "5.0", "95.0", "0", "0", "",
                "[StnLink_entry]", "20", "0", "0", "100.0", "-1", "0", "0", "",
                "[StnLink]", "200.0", "11", "10", "1.5", "1.5", "5.0", "95.0", "0", "0", "",
                "[StnLink_entry]", "77", "0", "0", "100.0", "-1", "0", "0", "");
            WriteText(Path.Combine("TTData", "R1.ttr"), "Header", "", "[track_entry]", "20", "0", "0", "3", "100.0", "0", "",
                "[track_entry]", "30", "0", "1", "0", "48.0", "0", "");
            WriteText(Path.Combine("TTData", "A.ttp"), "Header", "", "[trip]", "", "East", "A", "", "[station_typ2]", "10", "", "[station_typ2]", "11", "",
                "[profile]", "standard", "4.000", "");
            WriteText(Path.Combine("TTData", "B.ttp"), "Header", "", "[trip]", "R2", "West", "B", "", "[station_typ2]", "11", "", "[station_typ2]", "12", "");
            WriteText(Path.Combine("TTData", "A.ttl"), "Header", "", "[userallowed]", "", "[priority]", "1", "", "[newtour]", "1", "Busses", "0", "",
                "Dep.: 4:7:0", "[addtrip]", "A", "0", "247.000", "", "[addtrip]", "A", "2", "300.000", "", "[addtrip]", "C", "0", "320.000", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(mapFolder).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void ReadsStopsLinksTracksTripsAndLines()
        {
            var timetable = OmsiTimetable.Read(new OmsiFolderSource(mapFolder));

            Assert.AreEqual(3, timetable.BusStops.Count);
            Assert.AreEqual("West", timetable.BusStops[0].Name);
            Assert.AreEqual(2, timetable.StationLinks.Count);
            Assert.AreEqual(90.0, OmsiTimetableCheck.LinkLength(timetable.StationLinks[0]).Value, 1e-9);
            Assert.AreEqual(2, timetable.Tracks["R1"].Entries.Count);
            Assert.AreEqual(48.0, timetable.Tracks["R1"].Entries[1].LengthMeters);
            var trip = timetable.Trips["A"];
            Assert.AreEqual(string.Empty, trip.Track);
            Assert.AreEqual("East", trip.Destination);
            CollectionAssert.AreEqual(new[] { 10, 11 }, trip.Stops.Select(stop => stop.ObjectId));
            Assert.AreEqual(4.0, trip.Profiles.Single().Value);
            var tour = timetable.Lines.Single().Tours.Single();
            Assert.AreEqual(3, tour.Trips.Count);
            Assert.AreEqual(247.0, tour.Trips[0].DepartureMinutes);
        }

        [Test]
        public void ReportsBrokenReferencesAndLengths()
        {
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var report = OmsiTimetableCheck.Check(OmsiTimetable.Read(new OmsiFolderSource(mapFolder)), summary);
            var messages = report.Problems.Select(problem => problem.ToString()).ToList();

            Assert.AreEqual(3, report.BusStopCount);
            Assert.AreEqual(3, report.ScheduledTripCount);
            Assert.That(messages, Has.Some.Contains("stop \"Ghost\" refers to object 99"));
            Assert.That(messages, Has.Some.Contains("link 11 → 10 drives element 77"));
            Assert.That(messages, Has.Some.Contains("link 11 → 10 is 200 m long, but its entries give 90 m"));
            // The 50 m spline was entered with 48 m.
            Assert.That(messages, Has.Some.Contains("entry on spline 30 is 48 m long, the spline 50 m"));
            Assert.That(messages, Has.Some.Contains("B.ttp: drives track \"R2\", which has no .ttr file"));
            Assert.That(messages, Has.Some.Contains("B.ttp: has no [profile]"));
            Assert.That(messages, Has.Some.Contains("stops at object 12, which is no stop"));
            Assert.That(messages, Has.Some.Contains("A.ttl: tour 1 runs \"A\" with profile 2"));
            Assert.That(messages, Has.Some.Contains("A.ttl: runs trip \"C\", which has no .ttp file"));
            // The first link and trip A are fine.
            Assert.That(messages, Has.None.Contains("link 10 → 11"));
            Assert.That(messages, Has.None.Contains("A.ttp"));

            var known = OmsiTimetableCheck.Check(OmsiTimetable.Read(new OmsiFolderSource(mapFolder)), summary, new System.Collections.Generic.HashSet<int> { 99 });
            Assert.That(known.Problems.Select(problem => problem.Message), Has.Some.Contains("object 99, which only a chrono variant adds"));
        }

        [Test]
        public void ReadsChronoVariantsAndLaysThemOverTheMapForADate()
        {
            var works = Path.Combine("Chrono", "0100_Works");
            Directory.CreateDirectory(Path.Combine(mapFolder, works, "TTData"));
            WriteText(Path.Combine(works, "Chrono.cfg"), "", "[startdate]", "19920401", "", "[enddate]", "19920414", "",
                "[deactivate_lines]", "1", "A", "");
            WriteTile(Path.Combine(works, "Chrono_ENG.dsc"), "", "[name]", "Road works", "", "[description]", "Closed lane", "[end]", "");
            WriteTile(Path.Combine(works, "tile_0_0.map"), "[version]", "14", "",
                "[selobject]", "10", "", "[delete]", "",
                "[selobject]", "11", "", "[typ]", @"Sceneryobjects\stop_new.sco", "",
                "[selspline]", "20", "", "[rule]", "0", "trafficdensity", "0.000", "0", "",
                "[object]", "0", @"Sceneryobjects\cone.sco", "40", "50", "8", "0", "0", "0", "0", "0", "");
            WriteText(Path.Combine(works, "TTData", "Busstops.cfg"), "Header", "", "[busstop]", "Detour", "0", "40", "0.0", "0", "0", "");
            WriteText(Path.Combine(works, "TTData", "B.ttp"), "Header", "", "[trip]", "", "West", "B", "", "[station_typ2]", "40", "", "[profile]", "x", "2", "");
            var later = Path.Combine("Chrono", "0200_Later");
            Directory.CreateDirectory(Path.Combine(mapFolder, later));
            WriteText(Path.Combine(later, "Chrono.cfg"), "[startdate]", "19950101", "");

            var source = new OmsiFolderSource(mapFolder);
            var summary = OmsiMapReader.Read(source);
            var variants = OmsiChrono.ReadVariants(source);

            Assert.AreEqual(2, variants.Count);
            var variant = variants[0];
            Assert.AreEqual("0100_Works", variant.Name);
            Assert.AreEqual("Road works", variant.Title);
            Assert.AreEqual(new DateTime(1992, 4, 1), variant.Start);
            Assert.AreEqual(new DateTime(1992, 4, 14), variant.End);
            CollectionAssert.AreEqual(new[] { "A" }, variant.DeactivatedLines);
            Assert.AreEqual(1, variant.TileFiles.Count);
            CollectionAssert.AreEqual(new[] { variant }, OmsiChrono.ActiveOn(variants, new DateTime(1992, 4, 5)));
            CollectionAssert.IsEmpty(OmsiChrono.ActiveOn(variants, new DateTime(1992, 4, 15)));

            var overlay = OmsiChrono.Overlay(summary, source, OmsiChrono.ActiveOn(variants, new DateTime(1992, 4, 5)), out var report);
            var tile = overlay.Tiles[0];
            CollectionAssert.AreEquivalent(new[] { 11, 12, 40 }, tile.Objects.Select(item => item.Id));
            Assert.AreEqual(@"Sceneryobjects\stop_new.sco", tile.Objects.Single(item => item.Id == 11).AssetPath);
            Assert.AreEqual(1, report.DeletedElements);
            Assert.AreEqual(1, report.ChangedTypes);
            Assert.AreEqual(1, report.AddedObjects);
            Assert.AreEqual(0, report.UnresolvedSelections);
            // The base map stays as it was.
            Assert.AreEqual(3, summary.Tiles[0].Objects.Count);
            Assert.AreEqual(@"Sceneryobjects\stop.sco", summary.Tiles[0].Objects.Single(item => item.Id == 11).AssetPath);

            var timetable = OmsiTimetable.ReadForVariants(source, new[] { variant });
            Assert.AreEqual(4, timetable.BusStops.Count);
            Assert.AreEqual(40, timetable.Trips["B"].Stops.Single().ObjectId);
            CollectionAssert.IsEmpty(timetable.Lines);
            CollectionAssert.Contains(OmsiChrono.AddedIds(source, summary, variants), 40);
        }

        [Test]
        public void ChronoKeepsRowsAlongSplinesOnTheirSplineWhenAnEarlierSplineIsDeleted()
        {
            WriteTile("tile_1_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "30", "0", "0", "0", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "31", "0", "0", "0", "20", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "32", "0", "0", "0", "40", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "41", "1", "2", "0", "0", "0", "0", "0", "10", "50", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "42", "2", "2", "0", "0", "0", "0", "0", "10", "50", "0", "0", "");
            var works = Path.Combine("Chrono", "0100_Works");
            Directory.CreateDirectory(Path.Combine(mapFolder, works));
            WriteText(Path.Combine(works, "Chrono.cfg"), "[startdate]", "19920401", "");
            // The new row names spline number 2, spline 32, although spline 31 before it is deleted.
            WriteTile(Path.Combine(works, "tile_1_0.map"), "[version]", "14", "",
                "[selspline]", "31", "", "[delete]", "",
                "[splineAttachement]", "0", "bench.sco", "43", "2", "3", "0", "0", "0", "0", "0", "10", "50", "0", "0", "");
            var source = new OmsiFolderSource(mapFolder);
            var summary = OmsiMapReader.Read(source);

            var overlay = OmsiChrono.Overlay(summary, source, OmsiChrono.ReadVariants(source), out var report);

            var tile = overlay.Tiles[1];
            CollectionAssert.AreEqual(new[] { 30, 32 }, tile.Splines.Select(item => item.Id));
            // The row on spline 31 goes with it; the other rows now name spline 32 by its new number 1.
            CollectionAssert.AreEqual(new[] { 42, 43 }, tile.SplineAttachments.Select(item => item.Id));
            CollectionAssert.AreEqual(new[] { 1, 1 }, tile.SplineAttachments.Select(item => item.SplineIndex));
            Assert.AreEqual(2, report.DeletedElements);
            // The base map keeps its numbers.
            CollectionAssert.AreEqual(new[] { 1, 2 }, summary.Tiles[1].SplineAttachments.Select(item => item.SplineIndex));
        }

        [Test]
        public void RoutesFollowTheLanesOfSplinesAndObjects()
        {
            // Line D drives its own track over lane 0 of the crossing, which runs 10 m forward from the object.
            WriteText(Path.Combine("TTData", "R3.ttr"), "Header", "", "[track_entry]", "12", "0", "0", "0", "10.0", "0", "");
            WriteText(Path.Combine("TTData", "D.ttp"), "Header", "", "[trip]", "R3", "North", "D", "", "[station_typ2]", "12", "", "[profile]", "x", "1", "");
            WriteText(Path.Combine("TTData", "D.ttl"), "Header", "", "[newtour]", "1", "Busses", "0", "", "[addtrip]", "D", "0", "300.000", "");
            var crossing = OmsiSceneryObject.Parse(Encoding.UTF8.GetBytes("[path]\r\n0\r\n0\r\n0\r\n0\r\n0\r\n10\r\n0\r\n0\r\n0\r\n3\r\n"));
            // Lane 0 of the road lies 2 m to the right of the spline.
            var road = OmsiSplineProfile.Parse(Encoding.UTF8.GetBytes("[path]\r\n0\r\n2\r\n0\r\n3\r\n0\r\n"), @"Splines\road.sli");
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var timetable = OmsiTimetable.Read(new OmsiFolderSource(mapFolder));
            var heading = summary.Tiles[0].Objects.Single(item => item.Id == 12);
            Assert.AreEqual(0.0, heading.HeadingDegrees);

            var routes = OmsiTimetableRoutes.Build(timetable, summary,
                asset => asset.EndsWith("crossing.sco", StringComparison.OrdinalIgnoreCase) ? crossing : null,
                asset => asset.EndsWith("road.sli", StringComparison.OrdinalIgnoreCase) ? road : null);

            // The stop "Ghost" names an object the map does not have.
            CollectionAssert.AreEqual(new[] { "West", "East" }, routes.Stops.Select(stop => stop.Name));
            Assert.AreEqual(95.0, routes.Stops[1].Position.X, 1e-9);
            var lineA = routes.Lines.Single(line => line.Name == "A");
            Assert.AreEqual(3, lineA.ScheduledTripCount);
            CollectionAssert.AreEquivalent(new[] { 10, 11 }, lineA.StopIds);
            // Trip A has no track and drives the link from 10 to 11 over spline 20, which runs east from 0, 0.
            var lane = lineA.Paths.Single();
            Assert.AreEqual(0.0, lane[0].X, 1e-9);
            Assert.AreEqual(-2.0, lane[0].Y, 1e-9);
            Assert.AreEqual(100.0, lane[lane.Length - 1].X, 1e-9);
            Assert.AreEqual(-2.0, lane[lane.Length - 1].Y, 1e-9);
            var crossingLane = routes.Lines.Single(line => line.Name == "D").Paths.Single();
            Assert.AreEqual(150.0, crossingLane[0].X, 1e-9);
            Assert.AreEqual(150.0, crossingLane[0].Y, 1e-9);
            Assert.AreEqual(150.0, crossingLane[crossingLane.Length - 1].X, 1e-9);
            Assert.AreEqual(160.0, crossingLane[crossingLane.Length - 1].Y, 1e-9);
            Assert.AreEqual(0, routes.MissingEntryCount);
        }

        [Test]
        public void MapCheckGroupsMissingVariableParentsAndNamesDuplicateIds()
        {
            WriteTile("tile_1_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "30", "0", "0", "0", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\lamp.sco", "10", "20", "20", "0", "0", "0", "0", "0", "",
                "[varparent]", "555", "",
                "[object]", "0", @"Sceneryobjects\lamp.sco", "31", "30", "20", "0", "0", "0", "0", "0", "",
                "[varparent]", "555", "",
                "[object]", "0", @"Sceneryobjects\lamp.sco", "32", "40", "20", "0", "0", "0", "0", "0", "",
                "[varparent]", "12", "");
            var source = new OmsiFolderSource(mapFolder);

            var report = OmsiMapCheck.Run(OmsiMapReader.Read(source), source, null);

            var parents = report.Sections.Single(section => section.Title == "Variable parents");
            Assert.AreEqual(OmsiCheckStatus.Note, parents.Status);
            Assert.AreEqual("1 of 3 variable parents exist.", parents.Summary);
            CollectionAssert.AreEqual(new[] { "2 elements of tile 1, 0 take their variables from 555, which the map does not have" }, parents.Findings);
            var ids = report.Sections.Single(section => section.Title == "IDs");
            Assert.AreEqual(OmsiCheckStatus.Problem, ids.Status);
            Assert.That(ids.Findings, Has.Some.Contains("ID 10 is used in tile 0, 0 and again in tile 1, 0"));
            var chrono = report.Sections.Single(section => section.Title == "Chrono variants");
            Assert.AreEqual(OmsiCheckStatus.Passed, chrono.Status);
            Assert.AreEqual("The map has no chrono variants.", chrono.Summary);
        }

        private void WriteTile(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }

        private void WriteText(string relative, params string[] lines)
        {
            File.WriteAllText(Path.Combine(mapFolder, relative), string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        }
    }
}
