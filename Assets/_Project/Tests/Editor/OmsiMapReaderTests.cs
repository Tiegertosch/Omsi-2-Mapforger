using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiMapReaderTests
    {
        private string mapFolder;

        [SetUp]
        public void CreateMapFolder()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-tests-" + Guid.NewGuid().ToString("N"), "maps", "Testmap");
            Directory.CreateDirectory(mapFolder);
        }

        [TearDown]
        public void DeleteMapFolder()
        {
            var testRoot = Path.GetDirectoryName(Path.GetDirectoryName(mapFolder));
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void ReadsMapIdentityTilesRecordsAndTerrain()
        {
            WriteOmsiText("global.cfg",
                "File created with a test",
                "",
                "[name]", "Testmap", "",
                "[friendlyname]", "Test Map (Ü)", "",
                "[description]", "Line one", "Line two", "[end]", "",
                "[map]", "0", "0", "tile_0_0.map", "",
                "[map]", "1", "0", "tile_1_0.map", "");
            WriteOmsiText("tile_0_0.map",
                "[version]", "14", "",
                "[object]", "0", @"Sceneryobjects\Trees\tree.sco", "17", "25.5", "243.25", "1.5", "90", "0", "0", "4", "", "",
                "Object Nr. 1",
                "[spline]", "0", @"Splines\road.sli", "42", "41", "0", "18.25", "2.5", "147", "445.6", "143.18", "-120", "", "",
                "[splineAttachement]", "0", "1", "");
            WriteTerrain("tile_0_0.map.terrain");

            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));

            Assert.AreEqual("Testmap", summary.Name);
            Assert.AreEqual("Test Map (Ü)", summary.FriendlyName);
            Assert.AreEqual("Line one" + Environment.NewLine + "Line two", summary.Description);
            Assert.AreEqual(2, summary.Tiles.Count);
            Assert.AreEqual(1, summary.ObjectCount);
            Assert.AreEqual(1, summary.SplineCount);
            Assert.AreEqual(1, summary.AttachmentCount);
            Assert.AreEqual(1, summary.TerrainTileCount);

            var tile = summary.Tiles[0];
            var item = tile.Objects.Single();
            Assert.AreEqual(17, item.Id);
            Assert.AreEqual(@"Sceneryobjects\Trees\tree.sco", item.AssetPath);
            Assert.AreEqual(25.5, item.LocalX, 1e-9);
            Assert.AreEqual(243.25, item.LocalY, 1e-9);
            Assert.AreEqual(1.5, item.Height, 1e-9);
            Assert.AreEqual(90.0, item.HeadingDegrees, 1e-9);

            var spline = tile.Splines.Single();
            Assert.AreEqual(42, spline.Id);
            Assert.AreEqual(18.25, spline.LocalX, 1e-9);
            Assert.AreEqual(2.5, spline.Height, 1e-9);
            Assert.AreEqual(147.0, spline.LocalY, 1e-9);
            Assert.AreEqual(445.6, spline.HeadingDegrees, 1e-9);
            Assert.AreEqual(143.18, spline.LengthMeters, 1e-9);
            Assert.AreEqual(-120.0, spline.RadiusMeters, 1e-9);

            Assert.IsTrue(tile.HasTerrain);
            Assert.AreEqual(0f, tile.TerrainMinHeight, 1e-4f);
            Assert.AreEqual(37.2f, tile.TerrainMaxHeight, 1e-4f);

            Assert.IsFalse(summary.Tiles[1].IsAvailable);
            Assert.IsTrue(summary.Warnings.Any(warning => warning.Contains("Map tile not found: tile_1_0.map")));
        }

        [Test]
        public void ReportsRecordWithBlankFieldInsteadOfShiftingLaterFields()
        {
            WriteOmsiText("global.cfg", "[map]", "0", "0", "tile_0_0.map", "");
            // The X field is blank. Skipping it would silently read Y as X and the height as Y.
            WriteOmsiText("tile_0_0.map",
                "[object]", "0", @"Sceneryobjects\a.sco", "18", "", "10", "0", "0", "0", "0", "");

            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));

            Assert.AreEqual(1, summary.Tiles[0].ObjectCount);
            Assert.AreEqual(0, summary.Tiles[0].Objects.Count);
            Assert.IsTrue(summary.Warnings.Any(warning => warning.StartsWith("Could not decode an [object]", StringComparison.Ordinal)));
        }

        [Test]
        public void IgnoresTilePathsOutsideTheMapFolder()
        {
            WriteOmsiText("global.cfg", "[map]", "0", "0", @"..\outside.map", "");

            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));

            Assert.AreEqual(0, summary.Tiles.Count);
            Assert.IsTrue(summary.Warnings.Any(warning => warning.Contains("outside the selected map folder")));
        }

        [Test]
        public void WarnsAboutWorldCoordinateMaps()
        {
            WriteOmsiText("global.cfg", "[worldcoordinates]", "", "[map]", "0", "0", "tile_0_0.map", "");
            WriteOmsiText("tile_0_0.map", "[version]", "14", "");

            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));

            Assert.IsTrue(summary.Warnings.Any(warning => warning.Contains("[worldcoordinates]")));
        }

        [Test]
        public void ReadsWindows1252TileFiles()
        {
            WriteOmsiText("global.cfg", "[map]", "0", "0", "tile_0_0.map", "");
            var ansi = "[object]\r\n0\r\nSceneryobjects\\Straße.sco\r\n1\r\n1\r\n2\r\n0\r\n0\r\n0\r\n0\r\n";
            File.WriteAllBytes(Path.Combine(mapFolder, "tile_0_0.map"), ansi.Select(character => (byte)character).ToArray());

            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));

            Assert.AreEqual("Sceneryobjects\\Straße.sco", summary.Tiles[0].Objects.Single().AssetPath);
        }

        [Test]
        public void RejectsMissingConfiguration()
        {
            Assert.Throws<FileNotFoundException>(() => OmsiMapReader.Read(Path.Combine(mapFolder, "missing.cfg")));
            Assert.Throws<ArgumentException>(() => OmsiMapReader.Read(" "));
        }

        /// <summary>Writes UTF-16 LE with byte order mark and CRLF, the format the OMSI editor produces.</summary>
        private void WriteOmsiText(string fileName, params string[] lines)
        {
            File.WriteAllText(Path.Combine(mapFolder, fileName), string.Join("\r\n", lines) + "\r\n", new UnicodeEncoding(false, true));
        }

        private void WriteTerrain(string fileName)
        {
            using (var writer = new BinaryWriter(File.Create(Path.Combine(mapFolder, fileName))))
            {
                writer.Write(60);
                for (var index = 0; index < 61 * 61; index++)
                    writer.Write(index * 0.01f);
            }
        }
    }
}
