using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiEntryPointTests
    {
        private const string EntryObject = @"Sceneryobjects\Generic\entrypoint_bus.sco";

        private string testRoot;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-entry-" + Guid.NewGuid().ToString("N"));
            var original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            // Two entry points of one depot on two objects of the tile; the first field of each is the object's place among the objects.
            File.WriteAllBytes(Path.Combine(original, "global.cfg"), OmsiTextFile.Encode(
                "[name]\r\nTestmap\r\n\r\n[friendlyname]\r\nTest\r\n\r\n[description]\r\nText\r\n[end]\r\n\r\n[NextIDCode]\r\n20\r\n\r\n" +
                "[entrypoints]\r\n2\r\n0\r\n1\r\n0\r\n10.000\r\n0.000\r\n20.000\r\n0.000000\r\n0.000000\r\n0.000000\r\n1.000000\r\n0\r\nDepot\r\n" +
                "1\r\n2\r\n0\r\n30.000\r\n0.000\r\n40.000\r\n0.000000\r\n0.000000\r\n0.000000\r\n1.000000\r\n0\r\nDepot\r\n\r\n" +
                "[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
            File.WriteAllBytes(Path.Combine(original, "tile_0_0.map"), OmsiTextFile.Encode(
                "[version]\r\n14\r\n\r\n" +
                "[object]\r\n0\r\n" + EntryObject + "\r\n1\r\n10\r\n20\r\n0\r\n0\r\n0\r\n0\r\n1\r\nDepot\r\n\r\n" +
                "[object]\r\n0\r\n" + EntryObject + "\r\n2\r\n30\r\n40\r\n0\r\n0\r\n0\r\n0\r\n1\r\nDepot\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        private byte[] Bytes(string file)
        {
            return session.Workspace.ReadAllBytes(file);
        }

        private void AssertEntryCheckPasses(OmsiMapSummary summary)
        {
            var section = OmsiMapCheck.Run(summary, session.Source, null).Sections.Single(candidate => candidate.Key == "Entry points");
            Assert.AreEqual(OmsiCheckStatus.Passed, section.Status, string.Join("; ", section.Findings));
        }

        [Test]
        public void AnEntryPointIsAnObjectWithItsNameAndALineInOneUndoStep()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");

            var change = session.AddEntryPoint(summary, tile, "Garage", 100.0, 120.0, 5.5, 90.0);

            var entries = session.ReadEntryPoints(summary);
            Assert.AreEqual(3, entries.Count);
            var made = entries.Last();
            Assert.AreEqual("Garage", made.Name);
            Assert.AreEqual(change.Record.Id, made.ObjectId);
            Assert.AreSame(tile, made.Tile);
            Assert.AreEqual(5.5, made.Height, 1e-9);
            Assert.AreEqual(90.0, made.HeadingDegrees, 1e-3);
            Assert.AreEqual(EntryObject, made.Object.AssetPath);
            Assert.AreEqual("Garage", made.Object.Labels[0]);
            Assert.AreEqual(0.0, made.Object.Height, "The object stands on the terrain; the line holds the ground height.");
            AssertEntryCheckPasses(summary);

            session.Undo(summary, change);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);

            session.Redo(summary, change);
            Assert.AreEqual(3, session.ReadEntryPoints(summary).Count);
            AssertEntryCheckPasses(summary);
        }

        [Test]
        public void DeletingAnEntryPointTakesItsObjectAndKeepsTheOthersNumbersRight()
        {
            var summary = session.Read();
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");
            var first = session.ReadEntryPoints(summary)[0];

            var result = session.DeleteEntryPoint(summary, first);

            Assert.IsNotNull(result.Change);
            var left = session.ReadEntryPoints(summary);
            Assert.AreEqual(1, left.Count);
            Assert.AreEqual(2, left[0].ObjectId);
            Assert.AreEqual(1, summary.Tiles[0].Objects.Count);
            // The second object is the first one now, and the line says so.
            AssertEntryCheckPasses(summary);
            var last = Assert.Throws<InvalidOperationException>(() => session.DeleteEntryPoint(summary, left[0]));
            StringAssert.Contains("at least one", last.Message);

            session.Undo(summary, result.Change);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);
        }

        [Test]
        public void RenamingChangesTheLineAndTheLabelOfTheObject()
        {
            var summary = session.Read();
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");
            var entry = session.ReadEntryPoints(summary)[1];

            var change = session.RenameEntryPoint(summary, entry, "  Night depot ");

            var renamed = session.ReadEntryPoints(summary)[1];
            Assert.AreEqual("Night depot", renamed.Name);
            Assert.AreEqual("Night depot", renamed.Object.Labels[0]);
            Assert.AreEqual("Depot", session.ReadEntryPoints(summary)[0].Name, "The other entry point keeps its name.");
            AssertEntryCheckPasses(summary);
            Assert.IsNull(session.RenameEntryPoint(summary, renamed, "Night depot"), "The same name changes nothing.");

            session.Undo(summary, change);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            Assert.AreEqual("Depot", session.ReadEntryPoints(summary)[1].Name);
            Assert.AreEqual("Depot", session.ReadEntryPoints(summary)[1].Object.Labels[0]);
        }

        [Test]
        public void TheEditorOnlyBoxOfAnEntryPointObjectIsDrawnSeeThroughAndOtherEditorMeshesAreLeftAlone()
        {
            var entry = OmsiSceneryObject.Parse(System.Text.Encoding.ASCII.GetBytes("[entrypoint]\r\n\r\n[onlyeditor]\r\n\r\n[mesh]\r\nentrypoint_bus.x\r\n"));
            Assert.IsTrue(entry.IsEntryPoint);
            Assert.IsTrue(entry.Meshes[0].EditorOnly);
            Assert.AreEqual(OmsiSceneryObject.EntryPointOpacity, entry.Meshes[0].EditorOpacity);

            var other = OmsiSceneryObject.Parse(System.Text.Encoding.ASCII.GetBytes("[onlyeditor]\r\n\r\n[mesh]\r\nmarker.x\r\n\r\n[mesh]\r\nwall.x\r\n"));
            Assert.IsFalse(other.IsEntryPoint);
            Assert.AreEqual(1f, other.Meshes[0].EditorOpacity);
            Assert.AreEqual(1f, other.Meshes[1].EditorOpacity);
        }

        [Test]
        public void ANameIsRequiredAndStaysOneLineWithoutABracket()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            Assert.Throws<ArgumentException>(() => session.AddEntryPoint(summary, tile, "  ", 1.0, 1.0, 0.0, 0.0));
            Assert.Throws<ArgumentException>(() => session.AddEntryPoint(summary, tile, "[map]", 1.0, 1.0, 0.0, 0.0));
            Assert.Throws<ArgumentException>(() => session.AddEntryPoint(summary, tile, "two\r\nlines", 1.0, 1.0, 0.0, 0.0));
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);
        }

        [Test]
        public void AnEntryPointPutDownNextToTheOnlyOneMakesTheOriginalDeletableAndUndoIsByteIdentical()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            // Leave one entry point, as a map made with the editor starts with: the library path used to put a second object down without a line.
            session.DeleteEntryPoint(summary, session.ReadEntryPoints(summary)[1]);
            var original = session.ReadEntryPoints(summary).Single();
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");

            var name = session.UniqueEntryName(summary, "Start");
            var change = session.AddEntryPoint(summary, tile, name, 60.0, 70.0, 1.5, 0.0);

            var entries = session.ReadEntryPoints(summary);
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("Start", entries[1].Name);
            AssertEntryCheckPasses(summary);
            session.Undo(summary, change);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            session.Redo(summary, change);

            var deleted = session.DeleteEntryPoint(summary, session.ReadEntryPoints(summary).Single(entry => entry.ObjectId == original.ObjectId));

            Assert.IsNotNull(deleted.Change, "With a second entry point registered the original can go.");
            Assert.AreEqual(new[] { change.Record.Id }, session.ReadEntryPoints(summary).Select(entry => entry.ObjectId).ToArray());
            AssertEntryCheckPasses(summary);
        }

        [Test]
        public void AnOrdinaryObjectLeavesTheEntryPointListAlone()
        {
            var summary = session.Read();
            var before = session.ReadEntryPoints(summary).Select(entry => entry.Name + " " + entry.ObjectId + " " + entry.TileIndex).ToArray();

            var change = session.PlaceObject(summary, summary.Tiles[0], @"Sceneryobjects\Generic\tree.sco", 50.0, 60.0, 0.0, 0.0);

            CollectionAssert.AreEqual(before, session.ReadEntryPoints(summary).Select(entry => entry.Name + " " + entry.ObjectId + " " + entry.TileIndex).ToArray());
            Assert.IsNull(session.EntryPointOf(summary, summary.Tiles[0], change.Record));
        }

        [Test]
        public void ANameThatIsTakenGetsTheNextNumberAndAFreeOneStays()
        {
            var summary = session.Read();
            Assert.AreEqual("Start", session.UniqueEntryName(summary, "Start"));
            Assert.AreEqual("Start", session.UniqueEntryName(summary, "  "));
            Assert.AreEqual("Start", session.UniqueEntryName(summary, "[bad"));
            Assert.AreEqual("Depot 2", session.UniqueEntryName(summary, "Depot"));
            session.AddEntryPoint(summary, summary.Tiles[0], "Depot 2", 100.0, 120.0, 0.0, 0.0);
            Assert.AreEqual("Depot 3", session.UniqueEntryName(summary, "Depot"));
            Assert.AreEqual("depot 3", session.UniqueEntryName(summary, "depot"), "Names count as taken regardless of case, and the wanted spelling is kept.");
        }

        [Test]
        public void ACopyOfAnEntryPointObjectGetsItsOwnLineAndNameInOneUndoStep()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");
            var data = OmsiClipboardData.Capture(summary, new[] { new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, tile.Objects[0]) });

            var result = session.PasteObjects(summary, data.PlaceAt(data.CentreX + 20, data.CentreY, 0));

            var entries = session.ReadEntryPoints(summary);
            Assert.AreEqual(3, entries.Count, "The copy is registered, or openOMSI would take it up under a name of its own choosing.");
            Assert.AreEqual("Depot 2", entries[2].Name);
            Assert.AreEqual(result.Placed[0].Record.Id, entries[2].ObjectId);
            Assert.AreEqual("Depot 2", entries[2].Object.Labels[0]);
            Assert.AreEqual("Depot", entries[0].Name);
            AssertEntryCheckPasses(summary);

            session.Undo(summary, result.Placed[0]);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);
            session.Redo(summary, result.Placed[0]);
            Assert.AreEqual(3, session.ReadEntryPoints(summary).Count);
            AssertEntryCheckPasses(summary);
        }

        [Test]
        public void CopiesOfSeveralEntryPointObjectsGetDifferentNamesAndOneLineEach()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var data = OmsiClipboardData.Capture(summary, tile.Objects.Select(item => new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, item)));

            var result = session.PasteObjects(summary, data.PlaceAt(data.CentreX + 20, data.CentreY, 0));

            var names = session.ReadEntryPoints(summary).Select(entry => entry.Name).ToArray();
            Assert.AreEqual(new[] { "Depot", "Depot", "Depot 2", "Depot 3" }, names);
            Assert.AreEqual(2, result.Placed.Count);
            AssertEntryCheckPasses(summary);
            for (var index = result.Placed.Count - 1; index >= 0; index--)
                session.Undo(summary, result.Placed[index]);
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);
            AssertEntryCheckPasses(summary);
        }

        [Test]
        public void AMapWithoutAnEntryPointListRefusesNewEntryPointsAndCopiesOfThemWithoutWriting()
        {
            var folder = Path.Combine(testRoot, "OMSI 2", "maps", "NoList");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode(
                "[name]\r\nNoList\r\n\r\n[friendlyname]\r\nTest\r\n\r\n[description]\r\nText\r\n[end]\r\n\r\n[NextIDCode]\r\n20\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n",
                OmsiTextEncoding.Utf16LittleEndian));
            File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), OmsiTextFile.Encode(
                "[version]\r\n14\r\n\r\n[object]\r\n0\r\n" + EntryObject + "\r\n1\r\n10\r\n20\r\n0\r\n0\r\n0\r\n0\r\n1\r\nDepot\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            var bare = OmsiMapSession.OpenProject(Path.Combine(folder, "global.cfg"), Path.Combine(testRoot, "Projects", "NoList.omproj"));
            var summary = bare.Read();
            var tile = summary.Tiles[0];
            var configBefore = bare.Workspace.ReadAllBytes("global.cfg");
            var tileBefore = bare.Workspace.ReadAllBytes("tile_0_0.map");

            Assert.IsFalse(bare.CanRegisterEntryPoints(out var reason));
            StringAssert.Contains("[entrypoints]", reason);
            Assert.Throws<InvalidOperationException>(() => bare.AddEntryPoint(summary, tile, "Start", 1.0, 1.0, 0.0, 0.0));
            var data = OmsiClipboardData.Capture(summary, new[] { new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, tile.Objects[0]) });
            var refused = Assert.Throws<InvalidOperationException>(() => bare.PasteObjects(summary, data.PlaceAt(data.CentreX + 20, data.CentreY, 0)));
            StringAssert.Contains("entry point", refused.Message);

            CollectionAssert.AreEqual(configBefore, bare.Workspace.ReadAllBytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, bare.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(1, tile.Objects.Count);
        }

        [Test]
        public void AWriteErrorOnGlobalCfgWhileMakingAnEntryPointLeavesTileAndGlobalCfgAsTheyWere()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            // An edited copy of global.cfg exists after a first entry point; holding it open makes the next write to it fail.
            session.AddEntryPoint(summary, tile, "First", 100.0, 120.0, 0.0, 0.0);
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);

            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "global.cfg", true))
            {
                var failure = Assert.Catch(() => session.AddEntryPoint(summary, tile, "Second", 130.0, 120.0, 0.0, 0.0));
                OmsiMapWorkspaceTests.AssertWriteFailure(failure);
            }

            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            Assert.AreEqual(3, session.ReadEntryPoints(summary).Count);
        }

        [Test]
        public void APasteThatFailsAfterTheFirstEntryPointIsTakenBackAndLeavesTileAndGlobalCfgByteIdentical()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");
            var data = OmsiClipboardData.Capture(summary, tile.Objects.Select(item => new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, item)));
            var calls = 0;
            var failure = new IOException("The ground could not be read.");

            // The first copy is put down and listed; reading the ground for the second fails after its object is in the tile.
            var thrown = Assert.Throws<IOException>(() => session.PasteObjects(summary, data.PlaceAt(data.CentreX + 20, data.CentreY, 0), null, (x, y) =>
            {
                if (++calls == 2)
                    throw failure;
                return 0.0;
            }));

            Assert.AreSame(failure, thrown);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);
        }

        [Test]
        public void AChronoVariantRefusesToPasteAnEntryPointWithoutWriting()
        {
            var variant = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var map = OmsiChrono.Overlay(session.Read(), session.Workspace, variants.FindAll(v => v.IsActiveOn(variant.FirstDay ?? DateTime.Today)), out var report);
            session.EditChronoVariant(report.Variants.Single(v => v.Name == variant.Name), report);
            var tile = map.Tiles[0];
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var data = OmsiClipboardData.Capture(map, new[] { new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, tile.Objects[0]) });

            var refused = Assert.Throws<InvalidOperationException>(() => session.PasteObjects(map, data.PlaceAt(data.CentreX + 20, data.CentreY, 0)));

            StringAssert.Contains("chrono", refused.Message);
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
        }

        [Test]
        public void AnEntryPointHungOnAnotherObjectRefusesTheWholePasteWithoutWriting()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var parent = new OmsiPasteItem { AssetPath = @"Sceneryobjects\Generic	ree.sco", WorldX = 50, WorldY = 60 };
            var child = new OmsiPasteAttached { AssetPath = @"Sceneryobjects\Generic\lamp.sco" };
            child.Children.Add(new OmsiPasteAttached { AssetPath = EntryObject });
            parent.Attached.Add(child);
            var ordinary = new OmsiPasteItem { AssetPath = @"Sceneryobjects\Generic	ree.sco", WorldX = 70, WorldY = 60 };
            var targets = new[]
            {
                new OmsiPasteTarget { Item = ordinary, WorldX = 70, WorldY = 60 },
                new OmsiPasteTarget { Item = parent, WorldX = 50, WorldY = 60 }
            };

            var refused = Assert.Throws<InvalidOperationException>(() => session.PasteObjects(summary, targets));

            StringAssert.Contains("hung on another object", refused.Message);
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            Assert.AreEqual(2, tile.Objects.Count);
        }

        [Test]
        public void APasteWithoutAGroundLookupWritesTheGroundOfTheMapOrTheObjectsOwnHeightIntoTheLine()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var data = OmsiClipboardData.Capture(summary, new[] { new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, tile.Objects[0]) });
            data.Items[0].Height = 4.25;

            var result = session.PasteObjects(summary, data.PlaceAt(data.CentreX + 20, data.CentreY, 0));

            var made = session.ReadEntryPoints(summary).Single(entry => entry.ObjectId == result.Placed[0].Record.Id);
            var sampler = new OmsiTerrainSampler(summary, session.Source);
            var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, made.X, made.Y, summary.TileSizeMeters);
            var expected = sampler.TryGetHeight(world.X, world.Y, out var ground) ? ground : 4.25;
            Assert.AreEqual(expected, made.Height, 1e-9, "Not 0 by default: the terrain under the copy, or the copied height where the map has no terrain there.");
            Assert.AreEqual(0.0, made.Object.Height, "The object itself stays on the terrain.");
        }

        private static bool IsEntryAsset(string asset)
        {
            return asset.IndexOf("entrypoint", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private const string TreeObject = @"Sceneryobjects\Generic\tree.sco";

        [Test]
        public void ChangingAnObjectsTypeToAnEntryPointRegistersItInOneUndoStep()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var tree = session.PlaceObject(summary, tile, TreeObject, 50.0, 60.0, 0.0, 45.0).Record;
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");

            Assert.IsNull(session.EntryPointTypeProblem(summary, tile, tree, EntryObject, IsEntryAsset));
            var change = session.SetElementType(summary, tile, tree, EntryObject, IsEntryAsset, "  Garage ");

            var made = session.ReadEntryPoints(summary).Last();
            Assert.AreEqual(3, session.ReadEntryPoints(summary).Count);
            Assert.AreEqual("Garage", made.Name);
            Assert.AreEqual(tree.Id, made.ObjectId);
            Assert.AreEqual(50.0, made.X, 1e-9);
            Assert.AreEqual(60.0, made.Y, 1e-9);
            Assert.AreEqual(45.0, made.HeadingDegrees, 1e-3);
            Assert.AreEqual(EntryObject, made.Object.AssetPath);
            Assert.AreEqual("Garage", made.Object.Labels[0]);
            AssertEntryCheckPasses(summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            var configAfter = Bytes("global.cfg");
            var tileAfter = Bytes("tile_0_0.map");

            session.Undo(summary, change);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);
            Assert.AreEqual(TreeObject, tree.AssetPath);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);

            session.Redo(summary, change);
            CollectionAssert.AreEqual(configAfter, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileAfter, Bytes("tile_0_0.map"));
            Assert.AreEqual(3, session.ReadEntryPoints(summary).Count);
            AssertEntryCheckPasses(summary);
        }

        [Test]
        public void ChangingAnEntryPointsTypeToAnOrdinaryObjectRemovesItsLineInTheSameStep()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var configBefore = Bytes("global.cfg");
            var tileBefore = Bytes("tile_0_0.map");
            var second = tile.Objects[1];

            var change = session.SetElementType(summary, tile, second, TreeObject, IsEntryAsset, null);

            Assert.AreEqual(new[] { 1 }, session.ReadEntryPoints(summary).Select(entry => entry.ObjectId).ToArray());
            Assert.AreEqual(TreeObject, second.AssetPath);
            AssertEntryCheckPasses(summary);
            var configAfter = Bytes("global.cfg");

            session.Undo(summary, change);
            CollectionAssert.AreEqual(configBefore, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileBefore, Bytes("tile_0_0.map"));
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);
            session.Redo(summary, change);
            CollectionAssert.AreEqual(configAfter, Bytes("global.cfg"));
            Assert.AreEqual(1, session.ReadEntryPoints(summary).Count);
        }

        [Test]
        public void TheLastEntryPointKeepsItsTypeAndNothingIsWritten()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            session.DeleteEntryPoint(summary, session.ReadEntryPoints(summary)[1]);
            var last = tile.Objects.Single();
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);

            StringAssert.Contains("at least one", session.EntryPointTypeProblem(summary, tile, last, TreeObject, IsEntryAsset));
            var refused = Assert.Throws<InvalidOperationException>(() => session.SetElementType(summary, tile, last, TreeObject, IsEntryAsset, null));

            StringAssert.Contains("at least one", refused.Message);
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            Assert.AreEqual(EntryObject, last.AssetPath);
            Assert.AreEqual(1, session.ReadEntryPoints(summary).Count);
        }

        [Test]
        public void ChangingBetweenTwoEntryPointTypesKeepsTheLine()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var first = tile.Objects[0];
            var lineBefore = session.ReadEntryPoints(summary)[0];

            var change = session.SetElementType(summary, tile, first, @"Sceneryobjects\Generic\entrypoint_truck.sco", IsEntryAsset, null);

            Assert.IsNotNull(change);
            var entries = session.ReadEntryPoints(summary);
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(lineBefore.Name, entries[0].Name);
            Assert.AreEqual(lineBefore.ObjectId, entries[0].ObjectId);
            Assert.AreEqual(@"Sceneryobjects\Generic\entrypoint_truck.sco", entries[0].Object.AssetPath);
            AssertEntryCheckPasses(summary);
            session.Undo(summary, change);
            Assert.AreEqual(EntryObject, first.AssetPath);
        }

        [Test]
        public void AWriteErrorOnGlobalCfgWhileChangingToAnEntryPointLeavesTheFilesAsTheyWere()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var tree = session.PlaceObject(summary, tile, TreeObject, 50.0, 60.0, 0.0, 0.0).Record;
            // An edited copy of global.cfg exists after a first entry point; holding it open makes the next write to it fail.
            session.AddEntryPoint(summary, tile, "First", 100.0, 120.0, 0.0, 0.0);
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);

            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "global.cfg", true))
            {
                var failure = Assert.Catch(() => session.SetElementType(summary, tile, tree, EntryObject, IsEntryAsset, "Second"));
                OmsiMapWorkspaceTests.AssertWriteFailure(failure);
            }

            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            Assert.AreEqual(TreeObject, tree.AssetPath);
            Assert.AreEqual(3, session.ReadEntryPoints(summary).Count);
        }

        [Test]
        public void AWriteErrorOnGlobalCfgWhileLeavingEntryPointStatusLeavesTheFilesAsTheyWere()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            session.AddEntryPoint(summary, tile, "First", 100.0, 120.0, 0.0, 0.0);
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var second = tile.Objects[1];

            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "global.cfg", true))
            {
                var failure = Assert.Catch(() => session.SetElementType(summary, tile, second, TreeObject, IsEntryAsset, null));
                OmsiMapWorkspaceTests.AssertWriteFailure(failure);
            }

            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            Assert.AreEqual(EntryObject, second.AssetPath);
        }

        [Test]
        public void ANewEntryPointNeedsAGoodNameAndNothingIsWrittenWithout()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var tree = session.PlaceObject(summary, tile, TreeObject, 50.0, 60.0, 0.0, 0.0).Record;
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);

            Assert.Throws<ArgumentException>(() => session.SetElementType(summary, tile, tree, EntryObject, IsEntryAsset, "  "));
            Assert.Throws<ArgumentException>(() => session.SetElementType(summary, tile, tree, EntryObject, IsEntryAsset, "[map]"));

            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            Assert.AreEqual(TreeObject, tree.AssetPath);
        }

        [Test]
        public void AChronoVariantRefusesTheTypeChangeThatWouldMakeOrTakeAnEntryPointWithoutWriting()
        {
            var summary = session.Read();
            var treeInBase = session.PlaceObject(summary, summary.Tiles[0], TreeObject, 50.0, 60.0, 0.0, 0.0).Record;
            var variant = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var map = OmsiChrono.Overlay(session.Read(), session.Workspace, variants.FindAll(v => v.IsActiveOn(variant.FirstDay ?? DateTime.Today)), out var report);
            session.EditChronoVariant(report.Variants.Single(v => v.Name == variant.Name), report);
            var tile = map.Tiles[0];
            var tree = tile.Objects.Single(item => item.Id == treeInBase.Id);
            var entry = tile.Objects.First(item => item.AssetPath == EntryObject);
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);

            StringAssert.Contains("chrono", session.EntryPointTypeProblem(map, tile, tree, EntryObject, IsEntryAsset));
            StringAssert.Contains("chrono", session.EntryPointTypeProblem(map, tile, entry, TreeObject, IsEntryAsset));
            Assert.Throws<InvalidOperationException>(() => session.SetElementType(map, tile, tree, EntryObject, IsEntryAsset, "Garage"));
            Assert.Throws<InvalidOperationException>(() => session.SetElementType(map, tile, entry, TreeObject, IsEntryAsset, null));

            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
        }

        [Test]
        public void AMapWithoutAnEntryPointListRefusesTheTypeChangeToAnEntryPoint()
        {
            var folder = Path.Combine(testRoot, "OMSI 2", "maps", "NoList");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode(
                "[name]\r\nNoList\r\n\r\n[friendlyname]\r\nTest\r\n\r\n[description]\r\nText\r\n[end]\r\n\r\n[NextIDCode]\r\n20\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n",
                OmsiTextEncoding.Utf16LittleEndian));
            File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), OmsiTextFile.Encode(
                "[version]\r\n14\r\n\r\n[object]\r\n0\r\n" + TreeObject + "\r\n1\r\n10\r\n20\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            var bare = OmsiMapSession.OpenProject(Path.Combine(folder, "global.cfg"), Path.Combine(testRoot, "Projects", "NoList.omproj"));
            var summary = bare.Read();
            var tile = summary.Tiles[0];
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(bare.Workspace);

            StringAssert.Contains("[entrypoints]", bare.EntryPointTypeProblem(summary, tile, tile.Objects[0], EntryObject, IsEntryAsset));
            Assert.Throws<InvalidOperationException>(() => bare.SetElementType(summary, tile, tile.Objects[0], EntryObject, IsEntryAsset, "Start"));

            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(bare.Workspace));
            Assert.AreEqual(TreeObject, tile.Objects[0].AssetPath);
        }

        [Test]
        public void RevertingTheTileTakesAnEntryPointMadeInItAwayAndBringsBackADeletedOne()
        {
            var summary = session.Read();
            var configBefore = Bytes("global.cfg");
            session.AddEntryPoint(summary, summary.Tiles[0], "Garage", 100.0, 120.0, 0.0, 0.0);
            session.DeleteEntryPoint(summary, session.ReadEntryPoints(summary)[0]);
            Assert.AreEqual(2, session.ReadEntryPoints(summary).Count);

            session.RevertTile("tile_0_0.map");

            var after = session.ReadEntryPoints(OmsiMapReader.Read(session.Source));
            Assert.AreEqual(new[] { 1, 2 }, after.Select(entry => entry.ObjectId).ToArray());
            AssertEntryCheckPasses(OmsiMapReader.Read(session.Source));
        }

        [Test]
        public void UndoingTheRevertBringsTheEditedEntryPointsBackAndRedoTheOriginalsAgain()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            session.AddEntryPoint(summary, tile, "Garage", 100.0, 120.0, 0.0, 0.0);
            session.DeleteEntryPoint(summary, session.ReadEntryPoints(summary)[0]);
            var edited = session.ReadEntryPoints(summary).Select(entry => entry.Name + " " + entry.ObjectId).ToArray();
            var configEdited = Bytes("global.cfg");
            var tileEdited = Bytes("tile_0_0.map");
            var modifiedEdited = session.ModifiedFiles().ToList();

            var revert = session.RevertTile(summary, "tile_0_0.map");

            CollectionAssert.AreEquivalent(new[] { "global.cfg", "tile_0_0.map" }, revert.FilePaths().ToArray());
            Assert.AreEqual(new[] { 1, 2 }, session.ReadEntryPoints(summary).Select(entry => entry.ObjectId).ToArray());
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            AssertEntryCheckPasses(summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            var configReverted = Bytes("global.cfg");

            session.Undo(summary, revert);

            CollectionAssert.AreEqual(configEdited, Bytes("global.cfg"));
            CollectionAssert.AreEqual(tileEdited, Bytes("tile_0_0.map"));
            CollectionAssert.AreEquivalent(modifiedEdited, session.ModifiedFiles());
            CollectionAssert.AreEqual(edited, session.ReadEntryPoints(summary).Select(entry => entry.Name + " " + entry.ObjectId).ToArray());
            AssertEntryCheckPasses(summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);

            session.Redo(summary, revert);

            CollectionAssert.AreEqual(configReverted, Bytes("global.cfg"));
            Assert.AreEqual(new[] { 1, 2 }, session.ReadEntryPoints(summary).Select(entry => entry.ObjectId).ToArray());
            AssertEntryCheckPasses(summary);
        }

        [Test]
        public void UndoingARevertIsRefusedWithoutWritingWhenGlobalCfgWasSavedOutsideTheHistorySince()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            session.AddEntryPoint(summary, tile, "Garage", 100.0, 120.0, 0.0, 0.0);
            var revert = session.RevertTile(summary, "tile_0_0.map");
            CollectionAssert.Contains(revert.FilePaths().ToArray(), "global.cfg");
            // The traffic curves, like the map settings, save global.cfg without a step of the history.
            Assert.IsTrue(session.WriteTrafficCurves(new[] { new KeyValuePair<double, double>(0, 0.5), new KeyValuePair<double, double>(12, 1.5) }, null));
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var records = tile.Objects.ToList();

            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.Undo(summary, revert));

            StringAssert.Contains("global.cfg was changed in the meantime", refused.Message);
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            Assert.IsTrue(revert.IsApplied);
            Assert.AreEqual(records.Count, tile.Objects.Count);
            for (var index = 0; index < records.Count; index++)
                Assert.AreSame(records[index], tile.Objects[index]);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void RedoingARevertIsRefusedWithoutWritingWhenGlobalCfgWasSavedOutsideTheHistoryAfterTheUndo()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            session.AddEntryPoint(summary, tile, "Garage", 100.0, 120.0, 0.0, 0.0);
            var revert = session.RevertTile(summary, "tile_0_0.map");
            CollectionAssert.Contains(revert.FilePaths().ToArray(), "global.cfg");
            session.Undo(summary, revert);
            Assert.IsTrue(session.WriteTrafficCurves(new[] { new KeyValuePair<double, double>(0, 0.5), new KeyValuePair<double, double>(12, 1.5) }, null));
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var records = tile.Objects.ToList();

            var refused = Assert.Throws<OmsiStepRefusedException>(() => session.Redo(summary, revert));

            StringAssert.Contains("global.cfg was changed in the meantime", refused.Message);
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            Assert.IsFalse(revert.IsApplied);
            Assert.AreEqual(records.Count, tile.Objects.Count);
            for (var index = 0; index < records.Count; index++)
                Assert.AreSame(records[index], tile.Objects[index]);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
        }
    }
}
