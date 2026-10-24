using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTileTests
    {
        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-tiles-" + Guid.NewGuid().ToString("N"), "maps", "Tiletown");
            Directory.CreateDirectory(Path.Combine(mapFolder, "TTData"));
            // Two tiles side by side; an entry point on tile index 1, object 0.
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "",
                "[entrypoints]", "1", "0", "20", "0", "150", "0", "150", "0", "0", "0", "1", "1", "Start", "",
                "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            // Spline 10 in tile 0, 0 runs on into spline 30 in tile 1, 0, which names it back.
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "10", "0", "30", "250", "0", "100", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "",
                "[object]", "0", @"Sceneryobjects\entry.sco", "20", "150", "150", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "30", "10", "0", "0", "0", "100", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "");
            // A bus stop at the object of tile index 1, and a track over tile index 1.
            File.WriteAllText(Path.Combine(mapFolder, "TTData", "Busstops.cfg"), "[busstop]\r\nTown\r\n1\r\n20\r\n\r\n");
            File.WriteAllText(Path.Combine(mapFolder, "TTData", "Line.ttr"), "[track_entry]\r\n30\r\n0\r\n1\r\n0\r\n50\r\n\r\n");
            // The terrain rises 5 % to the east: 0.05 m per metre of world x.
            WriteTerrain("tile_0_0.map.terrain", 0);
            WriteTerrain("tile_1_0.map.terrain", 1);
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void AnAddedTileIsListedLastWithTerrainRunningOnFromItsNeighboursAndCanBeTakenBack()
        {
            var session = OpenProject();
            var summary = session.Read();
            var originalConfig = File.ReadAllBytes(Path.Combine(mapFolder, "global.cfg"));

            // North of tile 0, 0 and west of nothing: its south edge takes tile 0, 0's north edge, which rises to the east.
            var change = session.AddTile(summary, 0, 1);

            var read = session.Read();
            Assert.AreEqual(3, read.Tiles.Count);
            Assert.AreEqual("tile_0_1.map", Path.GetFileName(read.Tiles[2].FileName));
            Assert.AreEqual(0, read.Tiles[2].X);
            Assert.AreEqual(1, read.Tiles[2].Y);
            var terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_1.map.terrain"));
            Assert.AreEqual(0.05 * 100, terrain[20, 0], 1e-4);
            Assert.AreEqual(0.05 * 100, terrain[20, 40], 1e-4);
            Assert.AreEqual(0.05 * 300, terrain[60, 60], 1e-4);
            Assert.Throws<InvalidOperationException>(() => session.AddTile(read, 0, 1));
            // The original map is never written.
            CollectionAssert.AreEqual(originalConfig, File.ReadAllBytes(Path.Combine(mapFolder, "global.cfg")));
            Assert.IsFalse(File.Exists(Path.Combine(mapFolder, "tile_0_1.map")));

            session.ApplyTileChange(change, true);
            Assert.AreEqual(2, session.Read().Tiles.Count);
            Assert.IsFalse(session.Workspace.IsModified("global.cfg"));
            Assert.IsFalse(session.Workspace.FileExists("tile_0_1.map"));
            session.ApplyTileChange(change, false);
            Assert.AreEqual(3, session.Read().Tiles.Count);
        }

        [Test]
        public void DeletingATileMovesLaterTileIndicesDownAndOpensSplinesJoinedToIt()
        {
            var session = OpenProject();
            var summary = session.Read();

            var result = session.DeleteTile(summary, summary.Tiles[0]);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Blockers));
            var read = session.Read();
            Assert.AreEqual(1, read.Tiles.Count);
            Assert.AreEqual("tile_1_0.map", Path.GetFileName(read.Tiles[0].FileName));
            // Tile 1, 0 is index 0 now, wherever an index names it.
            var config = OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("global.cfg"), OmsiGlobalConfigLayouts.Instance);
            Assert.AreEqual("0", config.Records.Single(record => record.Name == "entrypoints").GetField(11));
            StringAssert.Contains("Town\r\n0\r\n20", Encoding.ASCII.GetString(session.Workspace.ReadAllBytes(@"TTData\Busstops.cfg")));
            StringAssert.Contains("30\r\n0\r\n0\r\n0\r\n50", Encoding.ASCII.GetString(session.Workspace.ReadAllBytes(@"TTData\Line.ttr")));
            // Spline 30 no longer names spline 10 of the deleted tile.
            Assert.AreEqual(0, read.Tiles[0].Splines.Single().PreviousId);

            session.ApplyTileChange(result.Change, true);
            Assert.AreEqual(2, session.Read().Tiles.Count);
            Assert.IsFalse(session.Workspace.IsModified("global.cfg"));
            Assert.IsFalse(session.Workspace.IsModified(@"TTData\Busstops.cfg"));
        }

        [Test]
        public void ATileStaysWhileSomethingOutsideNeedsIt()
        {
            var session = OpenProject();
            var summary = session.Read();

            // Tile 1, 0 carries the entry point, and the timetable names its object and spline.
            var result = session.DeleteTile(summary, summary.Tiles[1]);

            Assert.IsFalse(result.Succeeded);
            Assert.That(result.Blockers, Has.Some.Contains("entry point (Start)"));
            Assert.That(result.Blockers, Has.Some.Contains("#20 is needed by bus stop"));
            Assert.That(result.Blockers, Has.Some.Contains("track of the timetable"));
            Assert.IsFalse(session.Workspace.IsModified("global.cfg"));
            // The last tile always stays.
            session.DeleteTile(summary, summary.Tiles[0]);
            Assert.AreEqual("It is the only tile of the map.", session.DeleteTile(session.Read(), session.Read().Tiles[0]).Blockers.Single());
        }

        private OmsiMapSession OpenProject()
        {
            var project = Path.Combine(Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName, "Projects", "Tiles.omproj");
            return OmsiMapSession.OpenProject(Path.Combine(mapFolder, "global.cfg"), project);
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }

        private void WriteTerrain(string relative, int tileX)
        {
            var bytes = new byte[4 + OmsiTerrain.Resolution * OmsiTerrain.Resolution * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(60), 0, bytes, 0, 4);
            for (var row = 0; row < OmsiTerrain.Resolution; row++)
                for (var column = 0; column < OmsiTerrain.Resolution; column++)
                {
                    var worldX = tileX * 300.0 + column * 5.0;
                    Buffer.BlockCopy(BitConverter.GetBytes((float)(0.05 * worldX)), 0, bytes, 4 + (row * OmsiTerrain.Resolution + column) * 4, 4);
                }
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
