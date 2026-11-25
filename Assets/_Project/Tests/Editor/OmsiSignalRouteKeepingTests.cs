using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>signalroutes.cfg stays in step with edits of the map (ADR 105, M2).</summary>
    public sealed class OmsiSignalRouteKeepingTests
    {
        private const string Head = "-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.2.032\r\nDate: 05.10.2015\r\n\r\n";

        // Tile 0 holds the switch 5 (two paths), the spline 10 (one), the switch 11 (two) and the signals 12 and 13, so 10/0 is path 2 and 11/1 path 4.
        // Route 0 runs over 10/0, 11/1 and the spline 20 on tile 1; route 1 over 11/0.
        private const string Route0Head = "   0:\r\n[signalroute]\r\n1\r\n\r\n[signal]\r\n12\r\n2\r\n\r\n";
        private const string Route0Tail = "[distsignal]\r\n12\r\n\r\n\r\n[nextsignal]\r\n13\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n\r\n";
        private const string Route1Head = "   1:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n12\r\n1\r\n\r\n";

        private static string Routes(string entries0, string entries1)
        {
            return Head + Route0Head + entries0 + Route0Tail + Route1Head + entries1 + "\r\n\r\n";
        }

        private static string Entry(int element, int path, int tile, int tilePath)
        {
            return "[entry]\r\n" + element + "\r\n" + path + "\r\n" + tile + "\r\n" + tilePath + "\r\n\r\n";
        }

        private static readonly string Original = Routes(Entry(10, 0, 0, 2) + Entry(11, 1, 0, 4) + Entry(20, 0, 1, 0), Entry(11, 0, 0, 3));

        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-routekeeping-" + Guid.NewGuid().ToString("N"), "maps", "Signaltown");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[object]", "0", @"Sceneryobjects\switch.sco", "5", "20", "20", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\rail.sli", "10", "0", "0", "100", "50", "0", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\switch.sco", "11", "100", "100", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "12", "120", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "13", "200", "90", "0", "0", "0", "0", "0", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\rail.sli", "20", "0", "0", "0", "50", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "");
            File.WriteAllBytes(Path.Combine(mapFolder, "signalroutes.cfg"), Encoding.ASCII.GetBytes(Original));
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void SignalsAndTrackOfARouteCannotBeDeleted()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var references = session.BuildReferenceIndex();

            // Several deletions share one index, as deleting a selection does.
            foreach (var id in new[] { 11, 12, 13 })
            {
                var result = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == id), references);
                Assert.IsNull(result.Change, "object " + id);
                Assert.That(result.Blockers.Select(blocker => blocker.ToString()), Has.Some.Contains("signal route 0").And.Contains("signalroutes.cfg"), "object " + id);
            }
            var switchBlockers = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 11), references).Blockers.Select(blocker => blocker.ToString()).ToList();
            Assert.That(switchBlockers, Has.Some.EqualTo("track of signal route 0 (signal 12) in signalroutes.cfg"));
            Assert.That(switchBlockers, Has.Some.EqualTo("track of signal route 1 (signal 12) in signalroutes.cfg"));
            var spline = session.DeleteSpline(summary, tile, tile.Splines.Single(item => item.Id == 10));
            Assert.IsNull(spline.Change);
            Assert.That(spline.Blockers.Select(blocker => blocker.Kind), Has.Some.EqualTo("track of signal route 0 (signal 12)"));
            // The tile of a route's track stays as well (ADR 105, M0).
            Assert.IsFalse(session.DeleteTile(summary, summary.Tiles[1]).Succeeded);

            Assert.AreEqual(Original, Encoding.ASCII.GetString(session.Workspace.ReadAllBytes("signalroutes.cfg")));
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
        }

        [Test]
        public void DeletingAnElementBeforeTheTrackRenumbersItsPathsInTheSameStep()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];

            var result = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 5));

            Assert.IsNotNull(result.Change, string.Join("; ", result.Blockers));
            var renumbered = Routes(Entry(10, 0, 0, 0) + Entry(11, 1, 0, 2) + Entry(20, 0, 1, 0), Entry(11, 0, 0, 1));
            Assert.AreEqual(renumbered, Text(session));
            AssertResolvesWithoutProblems(session);

            session.Undo(summary, result.Change);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(Original), session.Workspace.ReadAllBytes("signalroutes.cfg"));
            AssertResolvesWithoutProblems(session);
            session.Redo(summary, result.Change);
            Assert.AreEqual(renumbered, Text(session));
        }

        [Test]
        public void MovingWithinTheTileLeavesTheFileAlone()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];

            foreach (var id in new[] { 5, 11, 12 })
            {
                var record = tile.Objects.Single(item => item.Id == id);
                Assert.IsTrue(session.UpdateObject(tile, record, record.LocalX + 7.0, record.LocalY - 3.0, 1.0, 45.0));
            }

            Assert.IsTrue(session.Workspace.IsModified("tile_0_0.map"));
            Assert.IsFalse(session.Workspace.IsModified("signalroutes.cfg"));
            AssertResolvesWithoutProblems(session);
        }

        [Test]
        public void TrackThatChangesTileTakesItsEntriesAlong()
        {
            var session = OpenProject();
            var summary = session.Read();

            // The switch 11 moves into tile 1, behind the spline 20 (one path): its paths there are 1 and 2.
            var result = session.TransferObject(summary, summary.Tiles[0], summary.Tiles[0].Objects.Single(item => item.Id == 11), summary.Tiles[1], 150.0, 150.0);

            Assert.IsNotNull(result.Change, string.Join("; ", result.Blockers));
            var moved = Routes(Entry(10, 0, 0, 2) + Entry(11, 1, 1, 2) + Entry(20, 0, 1, 0), Entry(11, 0, 1, 1));
            Assert.AreEqual(moved, Text(session));
            AssertResolvesWithoutProblems(session);
            session.Undo(summary, result.Change);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(Original), session.Workspace.ReadAllBytes("signalroutes.cfg"));
            session.Redo(summary, result.Change);
            Assert.AreEqual(moved, Text(session));
            session.Undo(summary, result.Change);

            // An element before the track leaves the tile: the track's numbers in tile 0 move up, the spline 20 keeps its number in front of it.
            var lead = session.TransferObject(summary, summary.Tiles[0], summary.Tiles[0].Objects.Single(item => item.Id == 5), summary.Tiles[1], 10.0, 10.0);
            Assert.IsNotNull(lead.Change, string.Join("; ", lead.Blockers));
            Assert.AreEqual(Routes(Entry(10, 0, 0, 0) + Entry(11, 1, 0, 2) + Entry(20, 0, 1, 0), Entry(11, 0, 0, 1)), Text(session));
            AssertResolvesWithoutProblems(session);
        }

        [Test]
        public void TrackWhosePathsCannotBeNumberedInTheNewTileStays()
        {
            var session = OpenProject();
            var summary = session.Read();
            // The installation does not know the spline 20's file, so the paths in front of the switch in tile 1 cannot be counted.
            session.AssetPathCount = asset => asset.EndsWith("rail.sli", StringComparison.OrdinalIgnoreCase) ? (int?)null : PathCount(asset);

            var result = session.TransferObject(summary, summary.Tiles[0], summary.Tiles[0].Objects.Single(item => item.Id == 11), summary.Tiles[1], 150.0, 150.0);

            Assert.IsNull(result.Change);
            Assert.That(result.Blockers.Select(blocker => blocker.Kind), Has.Some.Contains("Signal route 0").And.Contains("element 11"));
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            Assert.IsFalse(session.Workspace.IsModified("tile_1_0.map"));
            Assert.IsFalse(session.Workspace.IsModified("signalroutes.cfg"));
        }

        [Test]
        public void ANewTypeRenumbersOrIsRefusedWhenTheRoutesPathIsGone()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];

            // The switch 5 in front becomes a plain track piece with one path: the track behind moves down by one.
            var change = session.ReplaceObject(summary, tile, tile.Objects.Single(item => item.Id == 5), @"Sceneryobjects\single.sco");
            Assert.AreEqual(Routes(Entry(10, 0, 0, 1) + Entry(11, 1, 0, 3) + Entry(20, 0, 1, 0), Entry(11, 0, 0, 2)), Text(session));
            AssertResolvesWithoutProblems(session);
            session.Undo(summary, change);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(Original), session.Workspace.ReadAllBytes("signalroutes.cfg"));

            // The switch 11 of the route as a single track has no path 1 any more.
            var tileBefore = session.Workspace.ReadAllBytes("tile_0_0.map");
            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.ReplaceObject(summary, tile, tile.Objects.Single(item => item.Id == 11), @"Sceneryobjects\single.sco"));
            StringAssert.Contains("Signal route 0 (signal 12) runs over path 1 of element 11", refused.Message);
            CollectionAssert.AreEqual(tileBefore, session.Workspace.ReadAllBytes("tile_0_0.map"));
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(Original), session.Workspace.ReadAllBytes("signalroutes.cfg"));
        }

        [Test]
        public void AnUnknownNumberOfPathsBeforeTheTrackRefusesTheStep()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            // The installation does not know the switch 5's file, so the track behind it cannot be numbered after it goes.
            session.AssetPathCount = asset => asset.EndsWith("switch.sco", StringComparison.OrdinalIgnoreCase) ? (int?)null : PathCount(asset);

            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 5)));
            StringAssert.Contains("Signal route 0 (signal 12) runs over element 10", refused.Message);
            StringAssert.Contains(@"Sceneryobjects\switch.sco", refused.Message);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            Assert.IsFalse(session.Workspace.IsModified("signalroutes.cfg"));

            // Without the installation the same: nothing is counted.
            session.AssetPathCount = null;
            refused = Assert.Throws<OmsiStepRefusedException>(() => session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 5)));
            StringAssert.Contains("without the OMSI installation", refused.Message);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));

            // An element behind every entry of the tile changes no path number: placed and deleted without a count.
            var placed = session.PlaceObject(summary, tile, @"Sceneryobjects\unknown.sco", 50.0, 50.0, 0.0, 0.0);
            var deleted = session.DeleteObject(summary, tile, placed.Record);
            Assert.IsNotNull(deleted.Change, string.Join("; ", deleted.Blockers));
            Assert.AreEqual(Original, Text(session));
        }

        [Test]
        public void RedoWritesTheStoredFilesWithoutWorkingTheRoutesOutAgain()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var result = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 5));
            var renumbered = Text(session);
            var files = result.Change.Files.Count;
            session.Undo(summary, result.Change);

            // Counted again now, the step would be refused; the redo writes what the first run kept.
            session.AssetPathCount = null;
            session.Redo(summary, result.Change);
            Assert.AreEqual(renumbered, Text(session));
            Assert.AreEqual(files, result.Change.Files.Count);
            session.Undo(summary, result.Change);

            // A step that left the routes alone does not take them along on its redo either.
            session.AssetPathCount = asset => asset.EndsWith("twin.sco", StringComparison.OrdinalIgnoreCase) ? 2 : PathCount(asset);
            var twin = session.ReplaceObject(summary, tile, tile.Objects.Single(item => item.Id == 5), @"Sceneryobjects\twin.sco");
            Assert.AreEqual(Original, Text(session));
            files = twin.Files.Count;
            session.Undo(summary, twin);
            session.AssetPathCount = asset => asset.EndsWith("twin.sco", StringComparison.OrdinalIgnoreCase) ? 1 : PathCount(asset);
            session.Redo(summary, twin);
            Assert.AreEqual(files, twin.Files.Count);
            Assert.AreEqual(Original, Text(session));

            // A routes file changed between undo and redo refuses the redo of a step that wrote it, as every other file does.
            session.Undo(summary, twin);
            session.AssetPathCount = PathCount;
            var again = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 5));
            session.Undo(summary, again.Change);
            session.Workspace.Save("signalroutes.cfg", Encoding.ASCII.GetBytes(Original + "\r\n"));
            Assert.Throws<OmsiStepRefusedException>(() => session.Redo(summary, again.Change));
            Assert.IsFalse(again.Change.IsApplied);
        }

        [Test]
        public void RevertingATileKeepsTheRoutesOrIsRefusedWhenTheirTrackWouldGo()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];

            // Nothing of the track moves: the routes file stays as it is.
            session.PlaceObject(summary, tile, @"Sceneryobjects\signal.sco", 60.0, 60.0, 0.0, 0.0);
            Assert.IsNotNull(session.RevertTile(summary, "tile_0_0.map"));
            Assert.IsFalse(session.Workspace.IsModified("signalroutes.cfg"));
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));

            // The original's switch 5 comes back in front of the track: its numbers follow in the same step, and go back with its undo.
            var deleted = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 5));
            var renumbered = Text(session);
            var revert = session.RevertTile(summary, "tile_0_0.map");
            Assert.AreEqual(Original, Text(session));
            AssertResolvesWithoutProblems(session);
            session.Undo(summary, revert);
            Assert.AreEqual(renumbered, Text(session));
            session.Redo(summary, revert);
            Assert.AreEqual(Original, Text(session));

            // A route over a piece the original lacks refuses the revert and changes nothing.
            var placed = session.PlaceObject(summary, summary.Tiles[0], @"Sceneryobjects\single.sco", 70.0, 70.0, 0.0, 0.0);
            var withNew = Routes(Entry(10, 0, 0, 2) + Entry(11, 1, 0, 4) + Entry(placed.Record.Id, 0, 0, 5) + Entry(20, 0, 1, 0), Entry(11, 0, 0, 3));
            session.Workspace.Save("signalroutes.cfg", Encoding.ASCII.GetBytes(withNew));
            var tileBefore = session.Workspace.ReadAllBytes("tile_0_0.map");
            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.RevertTile(summary, "tile_0_0.map"));
            StringAssert.Contains("element " + placed.Record.Id, refused.Message);
            CollectionAssert.AreEqual(tileBefore, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(withNew, Text(session));
        }

        [Test]
        public void PathNumbersBehindAnUnknownFileAreNotChecked()
        {
            var session = OpenProject();
            Func<string, int?> counts = asset => asset.EndsWith("rail.sli", StringComparison.OrdinalIgnoreCase) ? (int?)null : PathCount(asset);
            var map = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(session.Workspace), session.Read(), session.Workspace, counts);

            CollectionAssert.IsEmpty(map.Problems, string.Join("; ", map.Problems));
            var entries = map.Resolved[0].Entries;
            // 10 stands right after the switch 5 and is still numbered; 11 stands behind the unknown spline 10, as 20 does in tile 1.
            Assert.IsFalse(entries[0].PathNumberUnchecked);
            Assert.AreEqual(2, entries[0].ExpectedTilePath);
            Assert.IsTrue(entries[1].PathNumberUnchecked);
            Assert.IsNull(entries[1].ExpectedTilePath);
            Assert.IsFalse(entries[2].PathNumberUnchecked);
            Assert.AreEqual(0, entries[2].ExpectedTilePath);
        }

        [Test]
        public void TheMapCheckListsTheProblemsOfTheRoutes()
        {
            var source = new OmsiFolderSource(mapFolder);
            var summary = OmsiMapReader.Read(source);
            // Without the installation the paths go unchecked, which the section says.
            const string installation = null;

            var clean = OmsiMapCheck.Run(summary, source, installation).Sections.Single(section => section.Key == "Signal routes");
            Assert.AreEqual(OmsiCheckStatus.Passed, clean.Status, string.Join("; ", clean.Findings));
            Assert.AreEqual(1, clean.Findings.Count);

            File.WriteAllBytes(Path.Combine(mapFolder, "signalroutes.cfg"), Encoding.ASCII.GetBytes(Routes(Entry(10, 0, 1, 2) + Entry(99, 0, 0, 0), string.Empty)));
            var stale = OmsiMapCheck.Run(summary, source, installation).Sections.Single(section => section.Key == "Signal routes");
            Assert.AreEqual(OmsiCheckStatus.Problem, stale.Status);
            Assert.That(stale.Findings, Has.Some.Contains("element 10").And.Contains("stands on tile 0"));
            Assert.That(stale.Findings, Has.Some.Contains("element 99"));

            File.Delete(Path.Combine(mapFolder, "signalroutes.cfg"));
            var none = OmsiMapCheck.Run(summary, source, installation).Sections.Single(section => section.Key == "Signal routes");
            Assert.AreEqual(OmsiCheckStatus.Passed, none.Status);
            CollectionAssert.IsEmpty(none.Findings);
        }

        private void AssertResolvesWithoutProblems(OmsiMapSession session)
        {
            var map = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(session.Workspace), session.Read(), session.Workspace, PathCount);
            CollectionAssert.IsEmpty(map.Problems, string.Join("; ", map.Problems));
        }

        private static string Text(OmsiMapSession session)
        {
            return Encoding.ASCII.GetString(session.Workspace.ReadAllBytes("signalroutes.cfg"));
        }

        private static int? PathCount(string asset)
        {
            if (asset.EndsWith("rail.sli", StringComparison.OrdinalIgnoreCase) || asset.EndsWith("single.sco", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (asset.EndsWith("switch.sco", StringComparison.OrdinalIgnoreCase))
                return 2;
            return asset.EndsWith("signal.sco", StringComparison.OrdinalIgnoreCase) ? 0 : (int?)null;
        }

        private OmsiMapSession OpenProject()
        {
            var project = Path.Combine(Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName, "Projects", "Routes.omproj");
            var session = OmsiMapSession.OpenProject(Path.Combine(mapFolder, "global.cfg"), project);
            session.AssetPathCount = PathCount;
            return session;
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
