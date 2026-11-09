using System;
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
    }
}
