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

            var report = OmsiMapExporter.Export(workspace, target, "Test Map (Mapforge)");

            Assert.AreEqual(Path.Combine(mapsFolder, "Testmap (Mapforge)"), target);
            Assert.AreEqual(3, report.CopiedFileCount);
            CollectionAssert.AreEqual(new[] { "tile_0_0.map" }, report.EditedFiles);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(target, "texture", "gras.bmp")));
            var exported = OmsiMapReader.Read(Path.Combine(target, "global.cfg"));
            Assert.AreEqual("Test Map (Mapforge)", exported.FriendlyName);
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
            StringAssert.Contains("not created by a Mapforge export", reason);
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

        private static void Write(string folder, string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(folder, name),
                OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
