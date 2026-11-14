using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

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
        public void SavingTheOriginalsBytesCountsOneNewVersionWithOrWithoutAnEditedCopy()
        {
            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            var originalBytes = File.ReadAllBytes(Path.Combine(original, "tile_0_0.map"));

            // Without an edited copy the file stays the original's, but its version moves on by one, so readers look again (ADR 057).
            var stamp = workspace.Stamp("tile_0_0.map");
            var writes = workspace.WriteCount;
            workspace.Save("tile_0_0.map", originalBytes);
            Assert.IsFalse(workspace.IsModified("tile_0_0.map"));
            StringAssert.StartsWith("o", workspace.Stamp("tile_0_0.map"));
            Assert.AreEqual(StampVersion(stamp) + 1, StampVersion(workspace.Stamp("tile_0_0.map")));
            Assert.AreEqual(writes + 1, workspace.WriteCount);

            // With an edited copy the copy goes, and the version moves on by one as well.
            var edited = (byte[])originalBytes.Clone();
            edited[edited.Length - 1] ^= 1;
            workspace.Save("tile_0_0.map", edited);
            Assert.IsTrue(workspace.IsModified("tile_0_0.map"));
            stamp = workspace.Stamp("tile_0_0.map");
            writes = workspace.WriteCount;
            workspace.Save("tile_0_0.map", originalBytes);
            Assert.IsFalse(workspace.IsModified("tile_0_0.map"));
            StringAssert.StartsWith("o", workspace.Stamp("tile_0_0.map"));
            Assert.AreEqual(StampVersion(stamp) + 1, StampVersion(workspace.Stamp("tile_0_0.map")));
            Assert.AreEqual(writes + 1, workspace.WriteCount);
            CollectionAssert.AreEqual(originalBytes, workspace.ReadAllBytes("tile_0_0.map"));
        }

        /// <summary>The version counted in a stamp of <see cref="OmsiMapWorkspace.Stamp"/>: the number after its first letter.</summary>
        private static long StampVersion(string stamp)
        {
            return long.Parse(stamp.Substring(1, stamp.IndexOf(':') - 1), System.Globalization.CultureInfo.InvariantCulture);
        }

        [Test]
        public void RoadsOfAMapMadeHereStillShapeTheTerrainAfterAnExportAndReopening()
        {
            var made = OmsiNewMap.Create(Path.Combine(original, "global.cfg"), "Own map", "Own Map", new DateTime(2026, 10, 30));
            var projectPath = Path.Combine(testRoot, "Projects", "Own.omproj");
            var session = OmsiMapSession.OpenProject(made.GlobalConfigPath, projectPath);
            var summary = session.Read();
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 50, StartY = 150, StartHeight = 0, EndX = 250, EndY = 150, EndHeight = 0, HeightMode = OmsiRoadHeightMode.Manual
            }, null);
            session.CreateRoad(summary, plan, @"Splines\road.sli", null, null, true);
            Assert.IsTrue(session.ShapesTerrain(summary.Tiles[0].Splines.Last()));

            // After the export the map folder holds the road; it is still one built here, so a new fit takes it into account.
            OmsiMapExporter.Export(session.Workspace, made.MapFolder, "Own Map");
            var reopened = OmsiMapSession.OpenProject(made.GlobalConfigPath, projectPath);
            var again = reopened.Read();
            Assert.IsTrue(reopened.ShapesTerrain(again.Tiles[0].Splines.Last()));
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
        public void AFileOfTheOriginalMapCanBeTakenAwayWithoutTouchingTheOriginal()
        {
            var workspace = new OmsiMapWorkspace(original, workspaceFolder);
            workspace.Delete("texture/gras.bmp");

            Assert.IsFalse(workspace.FileExists("texture/gras.bmp"));
            Assert.IsTrue(workspace.IsModified("texture/gras.bmp"));
            Assert.IsNull(workspace.Stamp("texture/gras.bmp"));
            Assert.Throws<FileNotFoundException>(() => workspace.ReadAllBytes("texture/gras.bmp"));
            CollectionAssert.DoesNotContain(workspace.EnumerateFiles("texture", "*.bmp", false).Select(path => path.Replace('/', '\\')), "texture\\gras.bmp");
            CollectionAssert.Contains(workspace.ModifiedFiles(), Path.Combine("texture", "gras.bmp"));
            Assert.IsTrue(File.Exists(Path.Combine(original, "texture", "gras.bmp")));
            // The project remembers it.
            var again = new OmsiMapWorkspace(original, workspaceFolder);
            Assert.IsFalse(again.FileExists("texture/gras.bmp"));

            // The export leaves the file out.
            var target = OmsiMapExporter.SuggestTargetFolder(workspace);
            var report = OmsiMapExporter.Export(workspace, target, "Test Map (MapWorks)");
            Assert.IsFalse(File.Exists(Path.Combine(target, "texture", "gras.bmp")));
            CollectionAssert.Contains(report.EditedFiles, Path.Combine("texture", "gras.bmp"));

            // Saving the file brings it back; so does reverting.
            workspace.Save("texture/gras.bmp", new byte[] { 9 });
            Assert.IsTrue(workspace.FileExists("texture/gras.bmp"));
            CollectionAssert.AreEqual(new byte[] { 9 }, workspace.ReadAllBytes("texture/gras.bmp"));
            workspace.Delete("texture/gras.bmp");
            workspace.Revert("texture/gras.bmp");
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, workspace.ReadAllBytes("texture/gras.bmp"));
            Assert.IsFalse(workspace.IsModified("texture/gras.bmp"));
            // A file only the project made simply goes.
            workspace.Save("texture/new.bmp", new byte[] { 4 });
            workspace.Delete("texture/new.bmp");
            Assert.IsFalse(workspace.FileExists("texture/new.bmp"));
            Assert.IsFalse(workspace.IsDeleted("texture/new.bmp"));
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
        public void AMapMadeWithMapWorksIsExportedIntoItsOwnFolderAndOthersAreNot()
        {
            // A map of the installation stays untouched: its own folder is no export target.
            var foreignWorkspace = new OmsiMapWorkspace(original, workspaceFolder);
            Assert.IsFalse(OmsiNewMap.IsMadeHere(original));
            Assert.AreEqual(Path.Combine(mapsFolder, "Testmap (MapWorks)"), OmsiMapExporter.SuggestTargetFolder(foreignWorkspace));
            Assert.IsFalse(OmsiMapExporter.CanWriteTo(foreignWorkspace, original, out var reason));
            StringAssert.Contains("Only maps made with MapWorks", reason);

            // A map made here is the user's own: it carries the marker, and its export goes into its folder.
            var made = OmsiNewMap.Create(Path.Combine(original, "global.cfg"), "Own map", "Own Map", new DateTime(2026, 10, 30));
            Assert.IsTrue(File.Exists(Path.Combine(made.MapFolder, OmsiNewMap.MadeMarkerFileName)));
            Assert.IsTrue(OmsiNewMap.IsMadeHere(made.MapFolder));
            var workspace = new OmsiMapWorkspace(made.MapFolder, Path.Combine(testRoot, "Projects", "Own map.workspace"));
            Assert.AreEqual(made.MapFolder, OmsiMapExporter.SuggestTargetFolder(workspace));
            Assert.IsTrue(OmsiMapExporter.CanWriteTo(workspace, made.MapFolder, out reason), reason);
            var tile = OmsiRecordFile.Parse(workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            OmsiTileRecords.Of<OmsiObjectView>(tile).First().HeadingDegrees = 90.0;
            workspace.Save("tile_0_0.map", tile);
            workspace.Save(@"Mapforge\geo.txt", new byte[] { 7 });

            var report = OmsiMapExporter.Export(workspace, made.MapFolder, "Own Map");

            Assert.IsTrue(report.InPlace);
            Assert.AreEqual("Own Map", OmsiMapReader.Read(made.GlobalConfigPath).FriendlyName);
            Assert.AreEqual(90.0, OmsiMapReader.Read(made.GlobalConfigPath).Tiles[0].Objects[0].HeadingDegrees, 1e-12);
            Assert.IsFalse(File.Exists(Path.Combine(made.MapFolder, OmsiMapExporter.MarkerFileName)));
            Assert.IsTrue(OmsiNewMap.IsMadeHere(made.MapFolder));
            Assert.IsFalse(Directory.Exists(made.MapFolder + ".mapforge-export-tmp") || Directory.Exists(made.MapFolder + ".mapforge-export-old"));
            // The map holds the edits now; the workspace keeps only the project's own data.
            CollectionAssert.AreEqual(new[] { Path.Combine("Mapforge", "geo.txt") }, workspace.ModifiedFiles());
            Assert.AreEqual(90.0, OmsiMapReader.Read(workspace).Tiles[0].Objects[0].HeadingDegrees, 1e-12);

            // A map made before the marker file is known by the mark in its signalroutes.cfg.
            File.Delete(Path.Combine(made.MapFolder, OmsiNewMap.MadeMarkerFileName));
            Assert.IsTrue(OmsiNewMap.IsMadeHere(made.MapFolder));
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

            Assert.Throws<OmsiStepRefusedException>(() => session.Undo(summary, change));

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
        public void TakesEntryPointsAlongAndKeepsObjectsWhoseVariableParentWouldBeOutOfReach()
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

            // The variable parent 99 lies nowhere, so it cannot be told to be within reach.
            var withVariableParent = session.TransferObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[0], summary.Tiles[1], 1, 1);
            Assert.AreEqual("variable parent of the object out of reach", withVariableParent.Blockers.Single().Kind);

            // Object 6 carries the entry point: it names tile 1 and the place after object 7 there. It lay far from the
            // object, stale, so it goes onto the object.
            var entryPoint = session.TransferObject(summary, summary.Tiles[0], summary.Tiles[0].Objects[1], summary.Tiles[1], 1, 1);
            Assert.IsTrue(entryPoint.Succeeded);
            var entries = EntryPoints(session.Workspace);
            Assert.AreEqual("1", entries.GetField(1));
            Assert.AreEqual("1", entries.GetField(11));
            Assert.AreEqual(1.0, Number(entries.GetField(4)), 1e-9);
            Assert.AreEqual(1.0, Number(entries.GetField(6)), 1e-9);

            // A bus stop names its object by ID only; object 7 leaving tile 1 moves the entry point there up by one.
            var busStop = session.TransferObject(summary, summary.Tiles[1], summary.Tiles[1].Objects[0], summary.Tiles[0], 2, 2);
            Assert.IsTrue(busStop.Succeeded);
            Assert.AreEqual("0", EntryPoints(session.Workspace).GetField(1));

            session.Undo(summary, busStop.Change);
            session.Undo(summary, entryPoint.Change);
            CollectionAssert.IsEmpty(session.ModifiedFiles());
        }

        [Test]
        public void TakesAttachedObjectsAndChronoSelectionsAlongIntoTheNewTile()
        {
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "20", "",
                "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "", "[map]", "2", "0", "tile_2_0.map", "");
            // Object 5 carries attached object 8, which carries 9; object 6 takes its variables from 5.
            Write(original, "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "a.sco", "5", "290", "10", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "trafficdensity", "1.000", "0", "",
                "[attachObj]", "0", "sign.sco", "8", "5", "0", "0", "0", "0", "0", "0", "",
                "[attachObj]", "0", "light.sco", "9", "8", "0", "0", "0", "0", "0", "0", "",
                "[object]", "0", "b.sco", "6", "10", "10", "0", "0", "0", "0", "0", "",
                "[varparent]", "5", "");
            Write(original, "tile_1_0.map", "[version]", "14", "", "[object]", "0", "c.sco", "7", "50", "60", "0", "0", "0", "0", "0", "");
            Write(original, "tile_2_0.map", "[version]", "14", "");
            // A chrono variant changes 5 and deletes 6 in tile 0.
            var chrono = Path.Combine(original, "Chrono", "0100_Works");
            Directory.CreateDirectory(chrono);
            Write(chrono, "tile_0_0.map", "[version]", "14", "",
                "[selobject]", "5", "", "[rule]", "0", "trafficdensity", "0.000", "0", "",
                "[selobject]", "6", "", "[delete]", "");
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var item = summary.Tiles[0].Objects.Single(o => o.Id == 5);

            var result = session.TransferObject(summary, summary.Tiles[0], item, summary.Tiles[1], 5, 10);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Blockers));
            CollectionAssert.AreEquivalent(new[] { 8, 9 }, summary.Tiles[1].AttachedObjects.Select(attached => attached.Id));
            CollectionAssert.IsEmpty(summary.Tiles[0].AttachedObjects);
            var reread = session.Read();
            CollectionAssert.AreEqual(new[] { 7, 5 }, reread.Tiles[1].Objects.Select(o => o.Id));
            CollectionAssert.AreEqual(new[] { 8, 9 }, reread.Tiles[1].AttachedObjects.Select(attached => attached.Id));
            CollectionAssert.AreEqual(new[] { 6 }, reread.Tiles[0].Objects.Select(o => o.Id));
            // The selection of 5 with its rule moved into the chrono file of tile 1, which the variant did not have.
            var movedChrono = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(@"Chrono\0100_Works\tile_1_0.map"));
            StringAssert.Contains("[selobject]\r\n5\r\n", movedChrono);
            StringAssert.Contains("0.000", movedChrono);
            var leftChrono = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(@"Chrono\0100_Works\tile_0_0.map"));
            StringAssert.DoesNotContain("[selobject]\r\n5\r\n", leftChrono);
            StringAssert.Contains("[delete]", leftChrono);

            // On into tile 2, object 6 in tile 0 would take its variables from two tiles away.
            var further = session.TransferObject(summary, summary.Tiles[1], item, summary.Tiles[2], 5, 10);
            Assert.AreEqual("variable parent out of reach", further.Blockers.Single().Kind);

            session.Undo(summary, result.Change);
            CollectionAssert.AreEquivalent(new[] { 8, 9 }, summary.Tiles[0].AttachedObjects.Select(attached => attached.Id));
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            session.Redo(summary, result.Change);
            Assert.IsTrue(session.Workspace.FileExists(@"Chrono\0100_Works\tile_1_0.map"));
        }

        [Test]
        public void EntryPointsStayOnTheirObjectWhenItMovesOrTurnsInItsTile()
        {
            UseMapWithEntryPoint();
            // The entry point lies 0.5 m east of object 6 at 30, 40, facing north as the object does.
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "10", "",
                "[entrypoints]", "1", "1", "6", "0", "30.5", "2", "40", "0", "0", "0", "1", "0", "Depot", "",
                "[map]", "0", "0", "tile_0_0.map", "");
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects.Single(o => o.Id == 6);

            // Turned a quarter clockwise and 10 m north: the entry point turns round the object, from east of it to south.
            session.UpdateObject(tile, item, 30, 50, 1, 90);
            var entries = EntryPoints(session.Workspace);
            Assert.AreEqual(30.0, Number(entries.GetField(4)), 1e-9);
            Assert.AreEqual(3.0, Number(entries.GetField(5)), 1e-9);
            Assert.AreEqual(49.5, Number(entries.GetField(6)), 1e-9);
            Assert.AreEqual(90.0, 2.0 * Math.Atan2(Number(entries.GetField(8)), Number(entries.GetField(10))) * 180.0 / Math.PI, 1e-3);

            // Back as it was, and nothing is left changed.
            session.UpdateObject(tile, item, 30, 40, 0, 0);
            entries = EntryPoints(session.Workspace);
            Assert.AreEqual(30.5, Number(entries.GetField(4)), 1e-9);
            Assert.AreEqual(40.0, Number(entries.GetField(6)), 1e-9);
            Assert.AreEqual(0.0, Number(entries.GetField(8)), 1e-9);
        }

        [Test]
        public void RevertingATileIsAStepThatUndoTakesBackByteForByteKeepingTheStepsBefore()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var originalTile = File.ReadAllBytes(Path.Combine(original, "tile_0_0.map"));
            var placement = session.PlaceObject(summary, tile, @"Sceneryobjects\new.sco", 100.5, 50.25, 0, 90);
            var moved = tile.Objects[0];
            Assert.IsTrue(session.UpdateObject(tile, moved, 15, 20, 0, 0));
            var filesBefore = WorkspaceFiles(session.Workspace);
            var modifiedBefore = session.ModifiedFiles().ToList();

            var revert = session.RevertTile(summary, "tile_0_0.map");

            Assert.IsNotNull(revert);
            Assert.AreSame(tile, revert.Tile);
            Assert.AreEqual(0, revert.X);
            Assert.AreEqual(0, revert.Y);
            // Only the tile was written: global.cfg keeps the raised [NextIDCode], as reverting a tile always did.
            CollectionAssert.AreEquivalent(new[] { "tile_0_0.map" }, revert.FilePaths().ToArray());
            CollectionAssert.AreEqual(new[] { "global.cfg" }, session.ModifiedFiles());
            CollectionAssert.AreEqual(originalTile, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(1, tile.Objects.Count);
            Assert.AreEqual(10.0, tile.Objects[0].LocalX, 1e-12);
            Assert.AreEqual(1, summary.ObjectCount);
            AssertSummaryMatchesFiles(session, summary);
            AssertSameReferences(session);

            session.Undo(summary, revert);

            AssertSameFiles(filesBefore, WorkspaceFiles(session.Workspace));
            CollectionAssert.AreEquivalent(modifiedBefore, session.ModifiedFiles());
            Assert.IsFalse(revert.IsApplied);
            // The very records the steps before hold are back in the tile.
            Assert.AreSame(moved, tile.Objects[0]);
            Assert.AreSame(placement.Record, tile.Objects[1]);
            Assert.AreEqual(15.0, moved.LocalX, 1e-12);
            AssertSummaryMatchesFiles(session, summary);
            AssertSameReferences(session);

            // The steps before the revert are undone as the shell does it: the move by its values, then the placement.
            Assert.IsTrue(session.UpdateObject(tile, tile.Objects.Single(item => item.Id == 5), 10, 20, 0, 0));
            session.Undo(summary, placement);
            Assert.AreEqual(1, tile.Objects.Count);
            Assert.AreEqual(1, summary.ObjectCount);
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            CollectionAssert.AreEqual(originalTile, session.Workspace.ReadAllBytes("tile_0_0.map"));
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void ARevertDoneAgainIsTheSameAsTheFirstAndStepsAfterItStillWork()
        {
            UseTwoTiles();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var deletion = session.DeleteObject(summary, tile, tile.Objects[0]);
            Assert.IsTrue(deletion.Succeeded);
            var revert = session.RevertTile(summary, "tile_0_0.map");
            var filesAfter = WorkspaceFiles(session.Workspace);
            var modifiedAfter = session.ModifiedFiles().ToList();
            var recordsAfter = tile.Objects.ToList();
            // The original's entry point numbers are back, and its object 5.
            Assert.AreEqual("1", EntryPoints(session.Workspace).GetField(1));
            CollectionAssert.AreEqual(new[] { 5, 6 }, tile.Objects.Select(item => item.Id).ToArray());

            // A step made after the revert holds the records the revert put in.
            var later = session.PlaceObject(summary, tile, @"Sceneryobjects\new.sco", 1, 2, 0, 0);
            session.Undo(summary, later);
            session.Undo(summary, revert);
            CollectionAssert.AreEqual(new[] { 6 }, tile.Objects.Select(item => item.Id).ToArray());
            Assert.AreEqual("0", EntryPoints(session.Workspace).GetField(1));
            AssertSummaryMatchesFiles(session, summary);

            session.Redo(summary, revert);

            AssertSameFiles(filesAfter, WorkspaceFiles(session.Workspace));
            CollectionAssert.AreEquivalent(modifiedAfter, session.ModifiedFiles());
            Assert.IsTrue(revert.IsApplied);
            CollectionAssert.AreEqual(recordsAfter, tile.Objects, "Redo puts back the records of the first revert, not new ones.");
            AssertSummaryMatchesFiles(session, summary);
            session.Redo(summary, later);
            Assert.AreSame(later.Record, tile.Objects.Last());
            AssertSummaryMatchesFiles(session, summary);
            AssertSameReferences(session);

            // Back to the start, step by step.
            session.Undo(summary, later);
            session.Undo(summary, revert);
            session.Undo(summary, deletion.Change);
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void ATileAddedInTheProjectIsNotRevertedAndNothingIsWritten()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            session.AddTile(summary, 1, 0);
            Assert.IsTrue(session.IsTileAdded("tile_1_0.map"));
            var files = WorkspaceFiles(session.Workspace);

            var refused = Assert.Throws<InvalidOperationException>(() => session.RevertTile(OmsiMapReader.Read(session.Source), "tile_1_0.map"));

            StringAssert.Contains("was added in this project", refused.Message);
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
        }

        [Test]
        public void ARevertThatFailsHalfWayLeavesEveryFileAsItWas()
        {
            // The original folder holds a tile file its global.cfg does not list; the project adds the tile there, so it has an
            // original, but the original map has no entry points to give it back.
            Write(original, "tile_1_0.map", "[version]", "14", "", "[object]", "0", "c.sco", "7", "50", "60", "0", "0", "0", "0", "0", "");
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            session.AddTile(summary, 1, 0);
            summary = session.Read();
            Assert.IsFalse(session.IsTileAdded("tile_1_0.map"));
            var files = WorkspaceFiles(session.Workspace);
            var tile = summary.Tiles.Single(candidate => candidate.X == 1);
            var records = tile.Objects.ToList();
            var stamp = session.Workspace.Stamp("tile_1_0.map");

            var failed = Assert.Throws<InvalidOperationException>(() => session.RevertTile(summary, "tile_1_0.map"));

            // The project's global.cfg lists the tile, so the revert got as far as writing it; the original's lacks it, which fails the
            // entry points after that. The workspace counted the write and the one that put the tile back, with the same bytes.
            StringAssert.Contains("is not listed in global.cfg", failed.Message);
            Assert.AreNotEqual(stamp, session.Workspace.Stamp("tile_1_0.map"), "The revert wrote the tile before it failed.");
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            CollectionAssert.AreEqual(records, tile.Objects);
        }

        [Test]
        public void RedoingARevertIsRefusedWithoutWritingWhenAFileChangedAfterTheUndo()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            Assert.IsTrue(session.UpdateObject(tile, tile.Objects[0], 15, 20, 0, 0));
            var revert = session.RevertTile(summary, "tile_0_0.map");
            session.Undo(summary, revert);
            var shown = tile.Objects.Single();
            // An edit outside the history after the undo.
            Assert.IsTrue(session.UpdateObject(tile, shown, 12, 20, 0, 0));
            var files = WorkspaceFiles(session.Workspace);
            var raised = WatchRollBackFailures(session);

            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.Redo(summary, revert));

            StringAssert.Contains("tile_0_0.map was changed in the meantime", refused.Message);
            Assert.IsEmpty(raised, "A refused step wrote nothing, so no failed roll-back is reported.");
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            Assert.IsFalse(revert.IsApplied);
            Assert.AreSame(shown, tile.Objects.Single());
            Assert.AreEqual(12.0, shown.LocalX, 1e-12);
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void AMoveAfterARevertThatIsUndoneLandsOnTheOriginalSoTheRevertIsUndoneByteForByte()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            Assert.IsTrue(session.UpdateObject(tile, tile.Objects[0], 15, 20, 0, 0));
            var filesBefore = WorkspaceFiles(session.Workspace);
            var revert = session.RevertTile(summary, "tile_0_0.map");
            var item = tile.Objects.Single();

            // The move and its undo as the shell does them: by values, the undo writing the values before.
            Assert.IsTrue(session.UpdateObject(tile, item, 25, 20, 0, 0));
            Assert.IsTrue(session.UpdateObject(tile, item, 10, 20, 0, 0));
            // The original writes its numbers as the editor does, so the undo gives its bytes, and the copy goes.
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));

            session.Undo(summary, revert);

            AssertSameFiles(filesBefore, WorkspaceFiles(session.Workspace));
            Assert.AreEqual(15.0, tile.Objects.Single().LocalX, 1e-12);
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void AMoveAfterARevertThatIsUndoneInOtherNumbersThanTheOriginalsRefusesTheRevertsUndo()
        {
            // The original writes 10.000 where the editor writes 10.
            Write(original, "tile_0_0.map", "[version]", "14", "", "[object]", "0", "a.sco", "5", "10.000", "20", "0", "0", "0", "0", "0", "");
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            Assert.IsTrue(session.UpdateObject(tile, tile.Objects[0], 15, 20, 0, 0));
            var revert = session.RevertTile(summary, "tile_0_0.map");
            var item = tile.Objects.Single();
            Assert.IsTrue(session.UpdateObject(tile, item, 25, 20, 0, 0));
            Assert.IsTrue(session.UpdateObject(tile, item, 10, 20, 0, 0));
            // The same place, in other bytes: the copy stays.
            Assert.IsTrue(session.Workspace.IsModified("tile_0_0.map"));
            StringAssert.Contains("\r\n10\r\n", OmsiTextFile.Decode(session.Workspace.ReadAllBytes("tile_0_0.map")));
            var files = WorkspaceFiles(session.Workspace);

            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.Undo(summary, revert));

            StringAssert.Contains("tile_0_0.map was changed in the meantime", refused.Message);
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            Assert.IsTrue(revert.IsApplied);
            Assert.AreSame(item, tile.Objects.Single());
        }

        [Test]
        public void ARefusedUndoOfARevertDropsTheOlderStepsAndKeepsTheRedoStack()
        {
            // Placed, moved, reverted, then an edit undone, then the revert's undo refused: the shell took the revert off the stack.
            var undo = new List<string> { "place", "move" };
            var redo = new List<string> { "edit after the revert" };

            Assert.AreEqual(2, OmsiStepHistory.DropStepsBeyondRefusedRevert(undo));

            CollectionAssert.IsEmpty(undo);
            CollectionAssert.AreEqual(new[] { "edit after the revert" }, redo);
        }

        [Test]
        public void ARefusedRedoOfARevertDropsTheNewerStepsAndKeepsTheUndoStack()
        {
            // Placed, reverted, two edits; all three undone, then the revert's redo refused: the steps under it were made on top of it.
            var undo = new List<string> { "place" };
            var redo = new List<string> { "second edit", "first edit" };

            Assert.AreEqual(2, OmsiStepHistory.DropStepsBeyondRefusedRevert(redo));

            CollectionAssert.IsEmpty(redo);
            CollectionAssert.AreEqual(new[] { "place" }, undo);
            Assert.AreEqual(0, OmsiStepHistory.DropStepsBeyondRefusedRevert(redo));
            Assert.Throws<ArgumentNullException>(() => OmsiStepHistory.DropStepsBeyondRefusedRevert<string>(null));
        }

        [Test]
        public void UndoingARevertIsRefusedWithoutWritingWhenAFileChangedSince()
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            Assert.IsTrue(session.UpdateObject(tile, tile.Objects[0], 15, 20, 0, 0));
            var revert = session.RevertTile(summary, "tile_0_0.map");
            var shown = tile.Objects[0];
            // An edit outside the history, such as a save of the tile by another way.
            Assert.IsTrue(session.UpdateObject(tile, shown, 12, 20, 0, 0));
            var files = WorkspaceFiles(session.Workspace);

            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.Undo(summary, revert));

            StringAssert.Contains("tile_0_0.map was changed in the meantime", refused.Message);
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            Assert.IsTrue(revert.IsApplied);
            Assert.AreSame(shown, tile.Objects.Single());
            Assert.AreEqual(12.0, shown.LocalX, 1e-12);
        }

        [Test]
        public void AnUndoOfARevertWhoseWriteFailsHalfWayPutsTheFilesBackAndCanBeDoneLater()
        {
            UseTwoTiles();
            var session = RevertedTileWithEntryPoint(out var summary, out var revert);
            var tile = summary.Tiles[0];
            var originalFolder = FolderFiles(original);
            var files = WorkspaceFiles(session.Workspace);
            var records = tile.Objects.ToList();
            var tileStamp = session.Workspace.Stamp("tile_0_0.map");

            Exception failed;
            // global.cfg is written after the tile; its save cannot write the file it moves into place.
            using (BlockWrites(session.Workspace, "global.cfg", false))
                failed = Assert.Catch(() => session.Undo(summary, revert));

            AssertWriteFailure(failed);
            Assert.AreNotEqual(tileStamp, session.Workspace.Stamp("tile_0_0.map"), "The undo wrote the tile before it failed.");
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            AssertSameFiles(originalFolder, FolderFiles(original));
            Assert.IsTrue(revert.IsApplied);
            AssertSameRecords(records, tile.Objects);
            AssertSummaryMatchesFiles(session, summary);

            // Without the obstacle the very step succeeds.
            session.Undo(summary, revert);

            Assert.IsFalse(revert.IsApplied);
            CollectionAssert.AreEqual(new[] { 6 }, tile.Objects.Select(item => item.Id).ToArray());
            Assert.AreEqual("0", EntryPoints(session.Workspace).GetField(1));
            AssertSummaryMatchesFiles(session, summary);
            AssertSameFiles(originalFolder, FolderFiles(original));
        }

        [Test]
        public void ARedoOfARevertWhoseWriteFailsHalfWayPutsTheFilesBackAndCanBeDoneLater()
        {
            UseTwoTiles();
            var session = RevertedTileWithEntryPoint(out var summary, out var revert);
            var tile = summary.Tiles[0];
            var filesReverted = WorkspaceFiles(session.Workspace);
            session.Undo(summary, revert);
            var originalFolder = FolderFiles(original);
            var files = WorkspaceFiles(session.Workspace);
            var records = tile.Objects.ToList();
            var tileStamp = session.Workspace.Stamp("tile_0_0.map");

            var raised = WatchRollBackFailures(session);
            Exception failed;
            using (BlockWrites(session.Workspace, "global.cfg", true))
                failed = Assert.Catch(() => session.Redo(summary, revert));

            AssertWriteFailure(failed);
            Assert.IsEmpty(raised, "The files were put back, so no failed roll-back is reported.");
            Assert.AreNotEqual(tileStamp, session.Workspace.Stamp("tile_0_0.map"), "The redo wrote the tile before it failed.");
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            AssertSameFiles(originalFolder, FolderFiles(original));
            Assert.IsFalse(revert.IsApplied);
            AssertSameRecords(records, tile.Objects);
            AssertSummaryMatchesFiles(session, summary);

            session.Redo(summary, revert);

            Assert.IsTrue(revert.IsApplied);
            AssertSameFiles(filesReverted, WorkspaceFiles(session.Workspace));
            CollectionAssert.AreEqual(new[] { 5, 6 }, tile.Objects.Select(item => item.Id).ToArray());
            AssertSummaryMatchesFiles(session, summary);
            AssertSameFiles(originalFolder, FolderFiles(original));
        }

        [Test]
        public void ARedoOfARevertWhoseRollBackFailsReportsBothFailuresTheStepsFirst()
        {
            UseTwoTiles();
            var session = RevertedTileWithEntryPoint(out var summary, out var revert);
            var tile = summary.Tiles[0];
            session.Undo(summary, revert);
            var originalFolder = FolderFiles(original);
            var records = tile.Objects.ToList();

            var raised = WatchRollBackFailures(session);
            OmsiRollBackFailedException failed;
            // The redo takes the tile's copy away, which needs no save; putting the copy back does, and cannot.
            using (BlockWrites(session.Workspace, "tile_0_0.map", false))
            using (BlockWrites(session.Workspace, "global.cfg", true))
                failed = Assert.Throws<OmsiRollBackFailedException>(() => session.Redo(summary, revert));

            Assert.AreEqual(1, raised.Count, "The failed roll-back is reported once.");
            Assert.AreSame(failed, raised[0]);

            var both = (AggregateException)failed.InnerException;
            Assert.AreEqual(2, both.InnerExceptions.Count);
            AssertWriteFailure(both.InnerExceptions[0]);
            AssertWriteFailure(both.InnerExceptions[1]);
            StringAssert.StartsWith(both.InnerExceptions[0].Message, failed.Message);
            StringAssert.Contains("Putting the files back as they were failed as well", failed.Message);
            // Part of the step stayed in the files; the summary was left alone, so the map has to be read again.
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            Assert.IsFalse(revert.IsApplied);
            AssertSameRecords(records, tile.Objects);
            AssertSameFiles(originalFolder, FolderFiles(original));
        }

        [Test]
        public void RevertUndoAndRedoLeaveTheOriginalMapAsItWas()
        {
            UseTwoTiles();
            var originalFolder = FolderFiles(original);
            var session = RevertedTileWithEntryPoint(out var summary, out var revert);
            AssertSameFiles(originalFolder, FolderFiles(original));

            session.Undo(summary, revert);
            AssertSameFiles(originalFolder, FolderFiles(original));

            session.Redo(summary, revert);
            AssertSameFiles(originalFolder, FolderFiles(original));
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void AMoveWithItsEntryPointWritesTheSameBytesAsSavingBothFilesAndUndoAndRedoToo()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects.Single(o => o.Id == 6);
            var originalFolder = FolderFiles(original);

            // The move, its undo and its redo as the shell does them, each checked against the bytes of the old way: both files parsed,
            // changed and saved one after the other.
            foreach (var values in new[] { new[] { 30.25, 50.0, 1.0, 90.0 }, new[] { 30.0, 40.0, 0.0, 0.0 }, new[] { 30.25, 50.0, 1.0, 90.0 } })
            {
                var expected = BytesOfOldMove(session.Workspace, 6, values);
                Assert.IsTrue(session.UpdateObject(tile, item, values[0], values[1], values[2], values[3]));
                CollectionAssert.AreEqual(expected[0], session.Workspace.ReadAllBytes("tile_0_0.map"));
                CollectionAssert.AreEqual(expected[1], session.Workspace.ReadAllBytes("global.cfg"));
                AssertSummaryMatchesFiles(session, summary);
            }
            CollectionAssert.AreEquivalent(new[] { "global.cfg", "tile_0_0.map" }, session.ModifiedFiles());

            // Back in its place, the tile has its original bytes again, so its copy goes.
            Assert.IsTrue(session.UpdateObject(tile, item, 30, 40, 0, 0));
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            AssertSameFiles(originalFolder, FolderFiles(original));
        }

        [Test]
        public void AMoveWhoseTileCannotBeWrittenLeavesFilesAndRecordAsTheyWere()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects.Single(o => o.Id == 6);
            var files = WorkspaceFiles(session.Workspace);
            var originalFolder = FolderFiles(original);
            var values = ValuesOf(item);
            var raised = WatchRollBackFailures(session);

            Exception failed;
            // The tile is written first, so nothing is written at all.
            using (BlockWrites(session.Workspace, "tile_0_0.map", false))
                failed = Assert.Catch(() => session.UpdateObject(tile, item, 30, 50, 1, 90));

            AssertWriteFailure(failed);
            Assert.IsEmpty(raised);
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            AssertSameFiles(originalFolder, FolderFiles(original));
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            CollectionAssert.AreEqual(values, ValuesOf(item));
            AssertSummaryMatchesFiles(session, summary);

            // Without the obstacle the very move succeeds.
            Assert.IsTrue(session.UpdateObject(tile, item, 30, 50, 1, 90));
            Assert.AreEqual(50.0, item.LocalY, 1e-12);
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void AMoveWhoseEntryPointCannotBeWrittenPutsTheTileBackAndLeavesTheRecordAsItWas()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects.Single(o => o.Id == 6);
            var files = WorkspaceFiles(session.Workspace);
            var originalFolder = FolderFiles(original);
            var values = ValuesOf(item);
            var tileStamp = session.Workspace.Stamp("tile_0_0.map");
            var raised = WatchRollBackFailures(session);

            Exception failed;
            using (BlockWrites(session.Workspace, "global.cfg", false))
                failed = Assert.Catch(() => session.UpdateObject(tile, item, 30, 50, 1, 90));

            AssertWriteFailure(failed);
            Assert.IsEmpty(raised, "The tile was put back, so no failed roll-back is reported.");
            Assert.AreNotEqual(tileStamp, session.Workspace.Stamp("tile_0_0.map"), "The move wrote the tile before it failed.");
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            AssertSameFiles(originalFolder, FolderFiles(original));
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            CollectionAssert.AreEqual(values, ValuesOf(item));
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void AnUndoOfAMoveWhoseWriteFailsPutsTheFilesBackAndCanBeDoneLater()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects.Single(o => o.Id == 6);
            Assert.IsTrue(session.UpdateObject(tile, item, 30, 50, 1, 90));
            var files = WorkspaceFiles(session.Workspace);
            var values = ValuesOf(item);
            var raised = WatchRollBackFailures(session);

            Exception failed;
            // The undo gives the tile its original bytes, which takes its copy away; global.cfg's copy is held open, so taking it away fails.
            using (BlockWrites(session.Workspace, "global.cfg", true))
                failed = Assert.Catch(() => session.UpdateObject(tile, item, 30, 40, 0, 0));

            AssertWriteFailure(failed);
            Assert.IsEmpty(raised);
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            CollectionAssert.AreEqual(values, ValuesOf(item));
            AssertSummaryMatchesFiles(session, summary);

            Assert.IsTrue(session.UpdateObject(tile, item, 30, 40, 0, 0));
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            var undone = WorkspaceFiles(session.Workspace);
            var undoneValues = ValuesOf(item);

            // The redo, with global.cfg written after the tile failing: the tile goes back to the original.
            using (BlockWrites(session.Workspace, "global.cfg", false))
                failed = Assert.Catch(() => session.UpdateObject(tile, item, 30, 50, 1, 90));

            AssertWriteFailure(failed);
            Assert.IsEmpty(raised);
            AssertSameFiles(undone, WorkspaceFiles(session.Workspace));
            CollectionAssert.AreEqual(undoneValues, ValuesOf(item));
            AssertSummaryMatchesFiles(session, summary);

            Assert.IsTrue(session.UpdateObject(tile, item, 30, 50, 1, 90));
            CollectionAssert.AreEqual(values, ValuesOf(item));
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void AnUndoOfAMoveWhoseRollBackFailsReportsItOnce()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var item = tile.Objects.Single(o => o.Id == 6);
            Assert.IsTrue(session.UpdateObject(tile, item, 30, 50, 1, 90));
            var originalFolder = FolderFiles(original);
            var values = ValuesOf(item);
            var raised = WatchRollBackFailures(session);

            OmsiRollBackFailedException failed;
            // The undo takes the tile's copy away, which needs no save; putting the copy back does, and cannot. global.cfg fails after it.
            using (BlockWrites(session.Workspace, "tile_0_0.map", false))
            using (BlockWrites(session.Workspace, "global.cfg", true))
                failed = Assert.Throws<OmsiRollBackFailedException>(() => session.UpdateObject(tile, item, 30, 40, 0, 0));

            Assert.AreEqual(1, raised.Count, "The failed roll-back is reported once.");
            Assert.AreSame(failed, raised[0]);
            var both = (AggregateException)failed.InnerException;
            AssertWriteFailure(both.InnerExceptions[0]);
            AssertWriteFailure(both.InnerExceptions[1]);
            // The tile stayed as the undo wrote it; the record was left alone, so the map has to be read again.
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            CollectionAssert.AreEqual(values, ValuesOf(item));
            AssertSameFiles(originalFolder, FolderFiles(original));
        }

        [Test]
        public void AGroupOfMovesWhoseSecondPartFailsTakesTheFirstBack()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var first = tile.Objects.Single(o => o.Id == 5);
            var second = tile.Objects.Single(o => o.Id == 6);
            var parts = new[]
            {
                new ValuePart { Record = first, Before = ValuesOf(first), After = new[] { 15.0, 20.0, 0.0, 0.0, 0.0, 0.0 } },
                // Object 6 has an entry point, so its part writes global.cfg after the tile.
                new ValuePart { Record = second, Before = ValuesOf(second), After = new[] { 30.0, 50.0, 1.0, 90.0, 0.0, 0.0 } }
            };
            var files = WorkspaceFiles(session.Workspace);
            var raised = WatchRollBackFailures(session);
            var noted = new List<OmsiRollBackFailedException>();

            Exception failed;
            // As the shell's undo and redo do a group: a value part has no applied state, since it writes all or nothing.
            using (BlockWrites(session.Workspace, "global.cfg", false))
                failed = Assert.Catch(() => StepGroup.Apply(parts, part => Move(session, tile, part.Record, part.After),
                    part => Move(session, tile, part.Record, part.Before), part => new List<bool>(), noted.Add));

            AssertWriteFailure(failed);
            Assert.IsEmpty(raised);
            Assert.IsEmpty(noted);
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            CollectionAssert.AreEqual(parts[0].Before, ValuesOf(first));
            CollectionAssert.AreEqual(parts[1].Before, ValuesOf(second));
            AssertSummaryMatchesFiles(session, summary);

            StepGroup.Apply(parts, part => Move(session, tile, part.Record, part.After), part => Move(session, tile, part.Record, part.Before),
                part => new List<bool>(), noted.Add);
            CollectionAssert.AreEqual(parts[0].After, ValuesOf(first));
            CollectionAssert.AreEqual(parts[1].After, ValuesOf(second));
            AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void ATransferWhoseTargetCannotBeWrittenLeavesFilesAndSummaryAsTheyWere()
        {
            UseTwoTiles();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var source = summary.Tiles[0];
            var target = summary.Tiles[1];
            var item = source.Objects.Single(o => o.Id == 5);
            var sourceRecords = source.Objects.ToList();
            var targetRecords = target.Objects.ToList();
            var files = WorkspaceFiles(session.Workspace);
            var sourceStamp = session.Workspace.Stamp("tile_0_0.map");
            var raised = WatchRollBackFailures(session);

            Exception failed;
            // The source tile is written first, then the target fails.
            using (BlockWrites(session.Workspace, "tile_1_0.map", false))
                failed = Assert.Catch(() => session.TransferObject(summary, source, item, target, 12.5, 40));

            AssertWriteFailure(failed);
            Assert.IsEmpty(raised);
            Assert.AreNotEqual(sourceStamp, session.Workspace.Stamp("tile_0_0.map"), "The transfer wrote the source tile before it failed.");
            AssertSameFiles(files, WorkspaceFiles(session.Workspace));
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            AssertSameRecords(sourceRecords, source.Objects);
            AssertSameRecords(targetRecords, target.Objects);
            Assert.AreEqual(10.0, item.LocalX, 1e-12);
            AssertSummaryMatchesFiles(session, summary);

            Assert.IsTrue(session.TransferObject(summary, source, item, target, 12.5, 40).Succeeded);
            AssertSummaryMatchesFiles(session, summary);
        }

        private sealed class ValuePart
        {
            public OmsiObjectRecord Record;
            public double[] Before;
            public double[] After;
        }

        private static void Move(OmsiMapSession session, OmsiTileSummary tile, OmsiObjectRecord record, double[] values)
        {
            session.UpdateObject(tile, record, values[0], values[1], values[2], values[3], values[4], values[5]);
        }

        private static double[] ValuesOf(OmsiObjectRecord item)
        {
            return new[] { item.LocalX, item.LocalY, item.Height, item.HeadingDegrees, item.PitchDegrees, item.BankDegrees };
        }

        /// <summary>
        /// The bytes of tile_0_0.map and global.cfg after moving an object as UpdateObject did before it wrote all or none: both files
        /// parsed, the object's changed values set, its entry points followed.
        /// </summary>
        private static byte[][] BytesOfOldMove(OmsiMapWorkspace workspace, int id, double[] values)
        {
            var tile = OmsiRecordFile.Parse(workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            var view = OmsiTileRecords.Of<OmsiObjectView>(tile).Single(candidate => candidate.Id == id);
            var oldX = view.X;
            var oldY = view.Y;
            var oldZ = view.Z;
            var oldHeading = view.HeadingDegrees;
            if (view.X != values[0])
                view.X = values[0];
            if (view.Y != values[1])
                view.Y = values[1];
            if (view.Z != values[2])
                view.Z = values[2];
            if (view.HeadingDegrees != values[3])
                view.HeadingDegrees = values[3];
            var config = OmsiRecordFile.Parse(workspace.ReadAllBytes("global.cfg"), OmsiGlobalConfigLayouts.Instance);
            OmsiTileEditing.FollowEntryPoints(tile, view, 0, config, oldX, oldY, oldZ, oldHeading);
            return new[] { tile.ToBytes(), config.ToBytes() };
        }

        /// <summary>
        /// The map of <see cref="UseTwoTiles"/>; object 5 of tile 0 deleted, which moves the entry point of object 6 to its place 0, then tile 0 reverted: the revert
        /// writes the tile first and global.cfg after it.
        /// </summary>
        private OmsiMapSession RevertedTileWithEntryPoint(out OmsiMapSummary summary, out OmsiTileRevertChange revert)
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            summary = session.Read();
            var tile = summary.Tiles[0];
            Assert.IsTrue(session.DeleteObject(summary, tile, tile.Objects[0]).Succeeded);
            Assert.AreEqual("0", EntryPoints(session.Workspace).GetField(1));
            revert = session.RevertTile(summary, "tile_0_0.map");
            CollectionAssert.AreEqual(new[] { "tile_0_0.map", "global.cfg" }, revert.FilePaths().ToArray());
            return session;
        }

        /// <summary>
        /// Makes every write of a workspace file fail until disposed: a folder takes the place of the file a save writes first, and with
        /// <paramref name="holdCopy"/> the edited copy is held open, so taking it away fails too. Reading the file still works.
        /// </summary>
        internal static IDisposable BlockWrites(OmsiMapWorkspace workspace, string relativePath, bool holdCopy)
        {
            var copy = Path.Combine(workspace.WorkspacePath, relativePath);
            var temporary = copy + ".mapforge-tmp";
            Directory.CreateDirectory(temporary);
            FileStream held = null;
            if (holdCopy)
            {
                Assert.IsTrue(File.Exists(copy), relativePath + " has an edited copy to hold.");
                held = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            return new Obstacle(temporary, held);
        }

        private sealed class Obstacle : IDisposable
        {
            private readonly string folder;
            private readonly FileStream held;

            public Obstacle(string folder, FileStream held)
            {
                this.folder = folder;
                this.held = held;
            }

            public void Dispose()
            {
                held?.Dispose();
                Directory.Delete(folder, true);
            }
        }

        /// <summary>Collects every <see cref="OmsiMapSession.RollBackFailed"/> the session raises from now on.</summary>
        internal static List<OmsiRollBackFailedException> WatchRollBackFailures(OmsiMapSession session)
        {
            var raised = new List<OmsiRollBackFailedException>();
            session.RollBackFailed += raised.Add;
            return raised;
        }

        /// <summary>A failure of the file system, not a refusal of the step or a failed roll-back.</summary>
        internal static void AssertWriteFailure(Exception failure)
        {
            Assert.IsNotNull(failure);
            Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>(), failure.ToString());
        }

        private static void AssertSameRecords<T>(List<T> expected, List<T> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (var index = 0; index < expected.Count; index++)
                Assert.AreSame(expected[index], actual[index], "record " + index);
        }

        /// <summary>Every file of a folder and its subfolders, by its path relative to it.</summary>
        internal static Dictionary<string, byte[]> FolderFiles(string folder)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                files[path.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar)] = File.ReadAllBytes(path);
            return files;
        }

        /// <summary>Every file of the workspace folder, by its path relative to it, including the project's own data.</summary>
        internal static Dictionary<string, byte[]> WorkspaceFiles(OmsiMapWorkspace workspace)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(workspace.WorkspacePath))
                return files;
            foreach (var path in Directory.GetFiles(workspace.WorkspacePath, "*", SearchOption.AllDirectories))
                files[path.Substring(workspace.WorkspacePath.Length).TrimStart(Path.DirectorySeparatorChar)] = File.ReadAllBytes(path);
            return files;
        }

        internal static void AssertSameFiles(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
        {
            CollectionAssert.AreEquivalent(expected.Keys, actual.Keys);
            foreach (var pair in expected)
                CollectionAssert.AreEqual(pair.Value, actual[pair.Key], pair.Key);
        }

        /// <summary>The summary kept up to date in place says what reading the files anew says, tile by tile.</summary>
        internal static void AssertSummaryMatchesFiles(OmsiMapSession session, OmsiMapSummary summary)
        {
            var fresh = session.Read();
            Assert.AreEqual(fresh.ObjectCount, summary.ObjectCount, "objects of the map");
            Assert.AreEqual(fresh.SplineCount, summary.SplineCount, "splines of the map");
            Assert.AreEqual(fresh.AttachmentCount, summary.AttachmentCount, "attachments of the map");
            Assert.AreEqual(fresh.Tiles.Count, summary.Tiles.Count);
            for (var index = 0; index < fresh.Tiles.Count; index++)
            {
                var expected = fresh.Tiles[index];
                var actual = summary.Tiles[index];
                var name = actual.FileName;
                Assert.AreEqual(expected.ObjectCount, actual.ObjectCount, name);
                Assert.AreEqual(expected.SplineCount, actual.SplineCount, name);
                Assert.AreEqual(expected.AttachmentCount, actual.AttachmentCount, name);
                CollectionAssert.AreEqual(expected.Objects.Select(item => item.Id + " " + item.AssetPath + " " + item.LocalX + " " + item.LocalY + " " + item.HeadingDegrees),
                    actual.Objects.Select(item => item.Id + " " + item.AssetPath + " " + item.LocalX + " " + item.LocalY + " " + item.HeadingDegrees), name);
                CollectionAssert.AreEqual(expected.Splines.Select(spline => spline.Id + " " + spline.LocalX + " " + spline.LocalY + " " + spline.AlignsTerrain),
                    actual.Splines.Select(spline => spline.Id + " " + spline.LocalX + " " + spline.LocalY + " " + spline.AlignsTerrain), name);
                CollectionAssert.AreEqual(expected.AttachedObjects.Select(item => item.Id), actual.AttachedObjects.Select(item => item.Id), name);
                CollectionAssert.AreEqual(expected.SplineAttachments.Select(item => item.Id), actual.SplineAttachments.Select(item => item.Id), name);
                Assert.AreEqual(expected.TerrainMinHeight, actual.TerrainMinHeight, name);
                Assert.AreEqual(expected.TerrainMaxHeight, actual.TerrainMaxHeight, name);
                Assert.AreEqual(expected.Water, actual.Water, name);
            }
        }

        private static double Number(string text)
        {
            return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
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

        [Test]
        public void ReferenceIndexOfASessionFollowsEveryChangeOfItsFiles()
        {
            UseMapWithEntryPoint();
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            AssertSameReferences(session);

            Assert.AreEqual("entry point", session.DeleteObject(summary, tile, tile.Objects[1]).Blockers.Single().Kind);
            var placed = session.PlaceObject(summary, tile, @"Sceneryobjects\new.sco", 1, 2, 0, 0);
            AssertSameReferences(session);
            session.Undo(summary, placed);
            AssertSameReferences(session);

            // Two saves of the same length with the same file time, as on file systems with coarse times: only the
            // workspace's count of saves tells them apart.
            var head = "[version]\r\n14\r\n\r\n[object]\r\n0\r\na.sco\r\n5\r\n10\r\n20\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n[varparent]\r\n";
            var edited = Path.Combine(workspaceFolder, "tile_0_0.map");
            session.Workspace.Save("tile_0_0.map", OmsiTextFile.Encode(head + "5\r\n", OmsiTextEncoding.Utf16LittleEndian));
            var savedAt = File.GetLastWriteTimeUtc(edited);
            Assert.AreEqual("variable parent", session.BuildReferenceIndex().To(5).Single().Kind);
            session.Workspace.Save("tile_0_0.map", OmsiTextFile.Encode(head + "6\r\n", OmsiTextEncoding.Utf16LittleEndian));
            File.SetLastWriteTimeUtc(edited, savedAt);
            var index = session.BuildReferenceIndex();
            CollectionAssert.IsEmpty(index.To(5));
            Assert.That(index.To(6).Select(reference => reference.Kind), Has.Member("variable parent"));
            AssertSameReferences(session);

            // A file changed outside the editor is read again as well.
            session.Workspace.Revert("tile_0_0.map");
            Write(original, "tile_0_0.map", "[version]", "14", "", "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[varparent]", "5", "");
            Assert.AreEqual("variable parent", session.BuildReferenceIndex().To(5).Single().Kind);
            AssertSameReferences(session);
        }

        /// <summary>The session's index, built from what it kept, equals an index read anew from every file.</summary>
        private static void AssertSameReferences(OmsiMapSession session)
        {
            var kept = session.BuildReferenceIndex();
            var fresh = OmsiReferenceIndex.Build(session.Workspace, "global.cfg");
            Assert.AreEqual(fresh.ReferenceCount, kept.ReferenceCount);
            for (var id = 0; id <= 20; id++)
                CollectionAssert.AreEqual(fresh.To(id).Select(reference => reference.ToString()), kept.To(id).Select(reference => reference.ToString()), "ID " + id);
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
