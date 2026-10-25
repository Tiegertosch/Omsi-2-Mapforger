using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiMapWorkspaceTests
    {
        private string testRoot;
        private string mapsFolder;
        private string original;
        private string workspaceFolder;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-workspace-" + Guid.NewGuid().ToString("N"));
            mapsFolder = Path.Combine(testRoot, "OMSI 2", "maps");
            original = Path.Combine(mapsFolder, "Testmap");
            workspaceFolder = Path.Combine(testRoot, "Projects", "Test.workspace");
            Directory.CreateDirectory(Path.Combine(original, "texture"));
            Write(original, "global.cfg", "[name]", "Testmap", "", "[friendlyname]", "Test Map", "", "[NextIDCode]", "10", "",
                "[map]", "0", "0", "tile_0_0.map", "");
            Write(original, "tile_0_0.map", "[version]", "14", "", "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "");
            File.WriteAllBytes(Path.Combine(original, "texture", "gras.bmp"), new byte[] { 1, 2, 3 });
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void ReadsOriginalFilesUntilAnEditedCopyIsSaved()
        {
            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            var originalBytes = File.ReadAllBytes(Path.Combine(original, "tile_0_0.map"));
            var tile = OmsiRecordFile.Parse(workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            OmsiTileRecords.Of<OmsiObjectView>(tile).Single().X = 42.5;

            Assert.IsFalse(workspace.IsModified("tile_0_0.map"));
            workspace.Save("tile_0_0.map", tile);

            Assert.IsTrue(workspace.IsModified("tile_0_0.map"));
            CollectionAssert.AreEqual(new[] { "tile_0_0.map" }, workspace.ModifiedFiles());
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(Path.Combine(original, "tile_0_0.map")));
            Assert.AreEqual(42.5, OmsiMapReader.Read(workspace).Tiles[0].Objects[0].LocalX, 1e-12);
            Assert.AreEqual(10.0, OmsiMapReader.Read(Path.Combine(original, "global.cfg")).Tiles[0].Objects[0].LocalX, 1e-12);

            workspace.Revert("tile_0_0.map");
            Assert.IsFalse(workspace.IsModified("tile_0_0.map"));
            Assert.AreEqual(10.0, OmsiMapReader.Read(workspace).Tiles[0].Objects[0].LocalX, 1e-12);
        }

        [Test]
        public void RejectsWorkspacesAndPathsThatCouldTouchTheOriginal()
        {
            Assert.Throws<ArgumentException>(() => new OmsiMapWorkspace(original, Path.Combine(original, "edits")));
            Assert.Throws<ArgumentException>(() => new OmsiMapWorkspace(original, mapsFolder));
            Assert.Throws<DirectoryNotFoundException>(() => new OmsiMapWorkspace(Path.Combine(mapsFolder, "Missing"), workspaceFolder));

            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            Assert.Throws<ArgumentException>(() => workspace.Save(@"..\escape.map", new byte[0]));
            Assert.Throws<ArgumentException>(() => workspace.Save(Path.Combine(original, "tile_0_0.map"), new byte[0]));
            Assert.AreEqual(Path.Combine(testRoot, "Projects", "Test.workspace"),
                OmsiMapWorkspace.FolderForProject(Path.Combine(testRoot, "Projects", "Test.omproj")));
        }

        [Test]
        public void ExportsOriginalPlusEditsAsASeparateMap()
        {
            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            var tile = OmsiRecordFile.Parse(workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            OmsiTileRecords.Of<OmsiObjectView>(tile).Single().HeadingDegrees = 90.0;
            workspace.Save("tile_0_0.map", tile);
            var target = OmsiMapExporter.SuggestTargetFolder(workspace);

            var report = OmsiMapExporter.Export(workspace, target, "Test Map (MapWorks)");

            Assert.AreEqual(Path.Combine(mapsFolder, "Testmap (MapWorks)"), target);
            Assert.AreEqual(3, report.CopiedFileCount);
            CollectionAssert.AreEqual(new[] { "tile_0_0.map" }, report.EditedFiles);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(target, "texture", "gras.bmp")));
            var exported = OmsiMapReader.Read(Path.Combine(target, "global.cfg"));
            Assert.AreEqual("Test Map (MapWorks)", exported.FriendlyName);
            Assert.AreEqual("Testmap", exported.Name);
            Assert.AreEqual(90.0, exported.Tiles[0].Objects[0].HeadingDegrees, 1e-12);
            Assert.AreEqual("Test Map", OmsiMapReader.Read(Path.Combine(original, "global.cfg")).FriendlyName);
            Assert.IsTrue(File.Exists(Path.Combine(target, OmsiMapExporter.MarkerFileName)));
            Assert.IsFalse(Directory.Exists(target + ".mapforge-export-tmp"));
        }

        [Test]
        public void ReplacesOnlyEarlierExports()
        {
            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            var target = OmsiMapExporter.SuggestTargetFolder(workspace);
            OmsiMapExporter.Export(workspace, target, "First");
            File.WriteAllText(Path.Combine(target, "stale.txt"), "left over");

            var second = OmsiMapExporter.Export(workspace, target, "Second");

            Assert.IsTrue(second.ReplacedPreviousExport);
            Assert.IsFalse(File.Exists(Path.Combine(target, "stale.txt")));
            Assert.AreEqual("Second", OmsiMapReader.Read(Path.Combine(target, "global.cfg")).FriendlyName);

            var foreign = Path.Combine(mapsFolder, "Other map");
            Directory.CreateDirectory(foreign);
            Assert.IsFalse(OmsiMapExporter.CanWriteTo(workspace, foreign, out var reason));
            StringAssert.Contains("not created by a MapWorks export", reason);
            Assert.Throws<InvalidOperationException>(() => OmsiMapExporter.Export(workspace, foreign, "X"));
            Assert.Throws<InvalidOperationException>(() => OmsiMapExporter.Export(workspace, original, "X"));
            Assert.Throws<InvalidOperationException>(() => OmsiMapExporter.Export(workspace, Path.Combine(original, "copy"), "X"));
        }

        [Test]
        public void IdAllocatorCountsEditedTilesInTheWorkspace()
        {
            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            var tile = OmsiRecordFile.Parse(workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            OmsiTileRecords.Of<OmsiObjectView>(tile).Single().Id = 25;
            workspace.Save("tile_0_0.map", tile);

            Assert.AreEqual(26, OmsiIdAllocator.ForMap(workspace).NextFreeId);
            Assert.AreEqual(10, OmsiIdAllocator.ForMap(Path.Combine(original, "global.cfg")).NextFreeId);
        }

        [Test]
        public void ReadOnlySessionsCannotEdit()
        {
            var session = OmsiMapSession.OpenReadOnly(Path.Combine(original, "global.cfg"));

            Assert.IsFalse(session.CanEdit);
            Assert.AreEqual(1, session.Read().ObjectCount);
            Assert.Throws<InvalidOperationException>(() => session.UpdateObject("tile_0_0.map", 5, 1, 2, 0, 0));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));
        }

        [Test]
        public void ProjectSessionsSaveObjectEditsIntoTheWorkspace()
        {
            var project = Path.Combine(testRoot, "Projects", "Test.omproj");
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), project);
            var originalBytes = File.ReadAllBytes(Path.Combine(original, "tile_0_0.map"));

            Assert.IsFalse(session.UpdateObject("tile_0_0.map", 5, 10, 20, 0, 0));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));
            Assert.IsTrue(session.UpdateObject("tile_0_0.map", 5, 12.5, 20, 0, 270));
            Assert.Throws<InvalidOperationException>(() => session.UpdateObject("tile_0_0.map", 99, 0, 0, 0, 0));

            Assert.IsTrue(session.IsTileModified("tile_0_0.map"));
            CollectionAssert.AreEqual(new[] { "tile_0_0.map" }, session.ModifiedFiles());
            var moved = session.Read().Tiles[0].Objects[0];
            Assert.AreEqual(12.5, moved.LocalX, 1e-12);
            Assert.AreEqual(270.0, moved.HeadingDegrees, 1e-12);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(Path.Combine(original, "tile_0_0.map")));
            Assert.IsTrue(File.Exists(Path.Combine(testRoot, "Projects", "Test.workspace", "tile_0_0.map")));

            var reopened = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), project);
            Assert.AreEqual(12.5, reopened.Read().Tiles[0].Objects[0].LocalX, 1e-12);
            reopened.RevertTile("tile_0_0.map");
            Assert.AreEqual(10.0, reopened.Read().Tiles[0].Objects[0].LocalX, 1e-12);
        }

        [Test]
        public void SavingContentEqualToTheOriginalRemovesTheWorkspaceCopy()
        {
            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            var tile = OmsiRecordFile.Parse(workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            var item = OmsiTileRecords.Of<OmsiObjectView>(tile).Single();
            item.X = 11;
            workspace.Save("tile_0_0.map", tile);
            Assert.IsTrue(workspace.IsModified("tile_0_0.map"));

            item.X = 10;
            workspace.Save("tile_0_0.map", tile);

            Assert.IsFalse(workspace.IsModified("tile_0_0.map"));
            CollectionAssert.IsEmpty(workspace.ModifiedFiles());
        }

        [Test]
        public void SessionUpdatesTheObjectInTheReadSummary()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects[0];

            Assert.IsTrue(session.UpdateObject(tile, item, 10.123456789012345678, 21, 0.5, 45));

            Assert.AreEqual(10.1234567890123, item.LocalX, 1e-15);
            Assert.AreEqual(21.0, item.LocalY, 1e-12);
            Assert.AreEqual(0.5, item.Height, 1e-12);
            Assert.AreEqual(45.0, item.HeadingDegrees, 1e-12);
            Assert.AreEqual(item.LocalX, session.Read().Tiles[0].Objects[0].LocalX, 0.0);

            Assert.IsTrue(session.UpdateObject(tile, item, 10, 20, 0, 0));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));
        }

        [Test]
        public void SessionSetsPitchAndBankAndKeepsThemWhenOnlyHeadingChanges()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects[0];
            Assert.AreEqual(0.0, item.PitchDegrees, 0.0);
            Assert.AreEqual(0.0, item.BankDegrees, 0.0);

            Assert.IsTrue(session.UpdateObject(tile, item, 10, 20, 0, 0, -1.5, 6));
            Assert.AreEqual(-1.5, item.PitchDegrees, 1e-12);
            Assert.AreEqual(6.0, item.BankDegrees, 1e-12);

            // Heading-only edits, such as dragging the handle in the view from above, keep the tilt.
            Assert.IsTrue(session.UpdateObject(tile, item, 10, 20, 0, 90));
            Assert.IsTrue(session.UpdateObject("tile_0_0.map", 5, 10, 20, 0, 45));
            var read = session.Read().Tiles[0].Objects[0];
            Assert.AreEqual(45.0, read.HeadingDegrees, 1e-12);
            Assert.AreEqual(-1.5, read.PitchDegrees, 1e-12);
            Assert.AreEqual(6.0, read.BankDegrees, 1e-12);

            Assert.IsTrue(session.UpdateObject(tile, item, 10, 20, 0, 0, 0, 0));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));
        }

        [Test]
        public void PlacesObjectsWithANewIdAndUndoesThemCompletely()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];

            var change = session.PlaceObject(summary, tile, @"Sceneryobjects\new.sco", 100.5, 50.25, 0, 90);

            Assert.AreEqual(10, change.Record.Id);
            Assert.AreEqual(2, tile.Objects.Count);
            Assert.AreEqual(2, tile.ObjectCount);
            Assert.AreEqual(2, summary.ObjectCount);
            Assert.AreEqual(11, summary.NextIdCode);
            CollectionAssert.AreEqual(new[] { "global.cfg", "tile_0_0.map" }, session.ModifiedFiles());
            var reread = session.Read();
            Assert.AreEqual(11, reread.NextIdCode);
            var placed = reread.Tiles[0].Objects.Single(item => item.Id == 10);
            Assert.AreEqual(@"Sceneryobjects\new.sco", placed.AssetPath);
            Assert.AreEqual(100.5, placed.LocalX, 1e-12);
            Assert.AreEqual(90.0, placed.HeadingDegrees, 1e-12);

            session.Undo(summary, change);
            Assert.AreEqual(1, tile.Objects.Count);
            Assert.AreEqual(1, summary.ObjectCount);
            CollectionAssert.IsEmpty(session.ModifiedFiles());

            session.Redo(summary, change);
            Assert.AreSame(change.Record, tile.Objects[1]);
            Assert.AreEqual(10, session.Read().Tiles[0].Objects[1].Id);

            // An undone ID is not handed out again.
            Assert.AreEqual(11, session.PlaceObject(summary, tile, @"Sceneryobjects\new.sco", 1, 1, 0, 0).Record.Id);
        }

        [Test]
        public void DeletesObjectsAndKeepsEntryPointsOnTheirObject()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];

            var deletion = session.DeleteObject(summary, tile, tile.Objects[0]);

            Assert.IsTrue(deletion.Succeeded);
            Assert.AreEqual(1, tile.Objects.Count);
            Assert.AreEqual(6, tile.Objects[0].Id);
            Assert.AreEqual(1, summary.ObjectCount);
            Assert.AreEqual("0", EntryPoints(session.Workspace).GetField(1));
            var tileText = OmsiTextFile.Decode(session.Workspace.ReadAllBytes("tile_0_0.map"));
            StringAssert.DoesNotContain("[rule]", tileText);
            StringAssert.DoesNotContain("a.sco", tileText);

            session.Undo(summary, deletion.Change);
            Assert.AreEqual(5, tile.Objects[0].Id);
            Assert.AreEqual(2, summary.ObjectCount);
            CollectionAssert.IsEmpty(session.ModifiedFiles());

            session.Redo(summary, deletion.Change);
            Assert.AreEqual("0", EntryPoints(session.Workspace).GetField(1));
            session.RevertTile("tile_0_0.map");
            Assert.AreEqual("1", EntryPoints(session.Workspace).GetField(1));
            CollectionAssert.IsEmpty(session.ModifiedFiles());
        }

        [Test]
        public void RefusesToDeleteReferencedObjects()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];

            var deletion = session.DeleteObject(summary, tile, tile.Objects[1]);

            Assert.IsFalse(deletion.Succeeded);
            Assert.AreEqual("entry point", deletion.Blockers.Single().Kind);
            Assert.AreEqual(2, tile.Objects.Count);
            CollectionAssert.IsEmpty(session.ModifiedFiles());
        }

        [Test]
        public void UndoRefusesToOverwriteLaterChanges()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var change = session.PlaceObject(summary, tile, @"Sceneryobjects\new.sco", 1, 2, 0, 0);
            session.UpdateObject(tile, tile.Objects[0], 15, 20, 0, 0);
            var edited = session.Workspace.ReadAllBytes("tile_0_0.map");

            Assert.Throws<InvalidOperationException>(() => session.Undo(summary, change));

            CollectionAssert.AreEqual(edited, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(2, tile.Objects.Count);
            Assert.IsTrue(change.IsApplied);
        }

        [Test]
        public void TransfersObjectsIntoAnotherTileWithTheirRulesAndBack()
        {
            UseTwoTiles();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var source = summary.Tiles[0];
            var target = summary.Tiles[1];
            var item = source.Objects.Single(o => o.Id == 5);

            var result = session.TransferObject(summary, source, item, target, 12.5, 40);

            Assert.IsTrue(result.Succeeded);
            Assert.AreSame(target, result.Change.CurrentTile);
            CollectionAssert.DoesNotContain(source.Objects, item);
            Assert.AreSame(item, target.Objects.Last());
            Assert.AreEqual(12.5, item.LocalX, 1e-12);
            Assert.AreEqual(3, summary.ObjectCount);
            // The later object's entry point moves down by one in the source tile.
            Assert.AreEqual("0", EntryPoints(session.Workspace).GetField(1));
            var reread = session.Read();
            Assert.AreEqual(new[] { 6 }, reread.Tiles[0].Objects.Select(o => o.Id).ToArray());
            Assert.AreEqual(new[] { 7, 5 }, reread.Tiles[1].Objects.Select(o => o.Id).ToArray());
            var targetText = OmsiTextFile.Decode(session.Workspace.ReadAllBytes("tile_1_0.map"));
            StringAssert.Contains("trafficdensity", targetText);
            StringAssert.DoesNotContain("trafficdensity", OmsiTextFile.Decode(session.Workspace.ReadAllBytes("tile_0_0.map")));

            session.Undo(summary, result.Change);
            Assert.AreSame(source, result.Change.CurrentTile);
            Assert.AreSame(item, source.Objects[0]);
            Assert.AreEqual(10.0, item.LocalX, 1e-12);
            CollectionAssert.IsEmpty(session.ModifiedFiles());
        }

        [Test]
        public void KeepsObjectsInTheirTileWhileTileBoundReferencesNameThem()
        {
            UseTwoTiles();
            File.WriteAllBytes(Path.Combine(original, "tile_0_0.map"), OmsiTextFile.Encode(string.Join("\r\n",
                "[version]", "14", "",
                "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[varparent]", "99", "",
                "[object]", "0", "b.sco", "6", "30", "40", "0", "0", "0", "0", "0", "") + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
            Directory.CreateDirectory(Path.Combine(original, "TTData"));
            File.WriteAllText(Path.Combine(original, "TTData", "Busstops.cfg"), "[busstop]\r\nStop\r\n1\r\n7\r\n0.0\r\n0\r\n0\r\n");
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();

            var withVariableParent = session.TransferObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0], summary.Tiles[1], 1, 1);
            var entryPoint = session.TransferObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[1], summary.Tiles[1], 1, 1);
            var busStop = session.TransferObject(summary, summary.Tiles[1], summary.Tiles[1].Objects[0], summary.Tiles[0], 2, 2);

            Assert.AreEqual("variable parent of the object", withVariableParent.Blockers.Single().Kind);
            Assert.AreEqual("entry point", entryPoint.Blockers.Single().Kind);
            Assert.IsTrue(busStop.Succeeded);
            CollectionAssert.AreEqual(new[] { "tile_0_0.map", "tile_1_0.map" }, session.ModifiedFiles());
        }

        /// <summary>Tile 0 as in <see cref="UseMapWithEntryPoint"/>; tile 1 to its right holds object 7.</summary>
        private void UseTwoTiles()
        {
            UseMapWithEntryPoint();
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "10", "",
                "[entrypoints]", "1", "1", "6", "0", "1", "0", "2", "0", "0", "0", "1", "0", "Depot", "",
                "[map]", "0", "0", "tile_0_0.map", "",
                "[map]", "1", "0", "tile_1_0.map", "");
            Write(original, "tile_1_0.map", "[version]", "14", "",
                "[object]", "0", "c.sco", "7", "50", "60", "0", "0", "0", "0", "0", "");
        }

        /// <summary>Objects 5 (with a rule) and 6; an entry point names object 6 by index 1 and by ID.</summary>
        private void UseMapWithEntryPoint()
        {
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "10", "",
                "[entrypoints]", "1", "1", "6", "0", "1", "0", "2", "0", "0", "0", "1", "0", "Depot", "",
                "[map]", "0", "0", "tile_0_0.map", "");
            Write(original, "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "trafficdensity", "1.000", "0", "",
                "[object]", "0", "b.sco", "6", "30", "40", "0", "0", "0", "0", "0", "");
        }

        private static OmsiRecord EntryPoints(IOmsiFileSource source)
        {
            return OmsiRecordFile.Parse(source.ReadAllBytes("global.cfg"), OmsiGlobalConfigLayouts.Instance)
                .Records.Single(record => record.Name == "entrypoints");
        }

        private static void Write(string folder, string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(folder, name),
                OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
