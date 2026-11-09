using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTerrainXExportTests
    {
        private string testRoot;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-terrainx-" + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode(
                "[name]\r\nTestmap\r\n\r\n[NextIDCode]\r\n20\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n\r\n[map]\r\n1\r\n0\r\ntile_1_0.map\r\n\r\n[map]\r\n2\r\n0\r\ntile_2_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
            foreach (var name in new[] { "tile_0_0.map", "tile_1_0.map", "tile_2_0.map" })
                File.WriteAllBytes(Path.Combine(folder, name), OmsiTextFile.Encode("[version]\r\n14\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            // Tile 0, 0 rises half a metre every sample to the east (a tenth, as the samples are 5 m apart); tile 1, 0 beside it is flat at 30 m.
            var ramp = new float[OmsiTerrain.Resolution * OmsiTerrain.Resolution];
            var flat = new float[ramp.Length];
            for (var row = 0; row < OmsiTerrain.Resolution; row++)
                for (var column = 0; column < OmsiTerrain.Resolution; column++)
                {
                    ramp[row * OmsiTerrain.Resolution + column] = column * 0.5f;
                    flat[row * OmsiTerrain.Resolution + column] = 30f;
                }
            File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map.terrain"), OmsiTerrain.Encode(ramp));
            File.WriteAllBytes(Path.Combine(folder, "tile_1_0.map.terrain"), OmsiTerrain.Encode(flat));
            session = OmsiMapSession.OpenProject(Path.Combine(folder, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void TheTileBecomesAMeshOfThe61By61PointsThatOurOwnReaderUnderstands()
        {
            var summary = session.Read();
            var export = session.ExportTerrainMesh(summary, summary.Tiles[0]);

            Assert.AreEqual(3721, export.Vertices);
            Assert.AreEqual(7200, export.Triangles);
            Assert.AreEqual(0.0, export.Lowest, 1e-6);
            Assert.AreEqual(30.0, export.Highest, 1e-6);
            StringAssert.StartsWith("xof 0303txt 0032", System.Text.Encoding.ASCII.GetString(export.Bytes, 0, 16));

            var mesh = OmsiXMesh.Parse(export.Bytes);
            Assert.AreEqual(3721, mesh.Positions.Count);
            Assert.AreEqual(7200 * 3, mesh.TriangleIndices.Count);
            // x east, y up, z north from the south-west corner; the tile is 300 m across with 60 cells.
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, mesh.Positions[0]);
            var northEast = mesh.Positions[3720];
            Assert.AreEqual(300.0, northEast[0], 1e-3);
            Assert.AreEqual(30.0, northEast[1], 1e-3);
            Assert.AreEqual(300.0, northEast[2], 1e-3);
            // Texture coordinates run over the tile like a picture of it, the north edge on top.
            Assert.AreEqual(0.0, mesh.Uvs[3720][1], 1e-6);
            Assert.AreEqual(1.0, mesh.Uvs[0][1], 1e-6);
        }

        [Test]
        public void EveryTriangleFacesUpAndTheNormalsFollowTheSlopeAcrossTheTileBorder()
        {
            var summary = session.Read();
            var mesh = OmsiXMesh.Parse(session.ExportTerrainMesh(summary, summary.Tiles[0]).Bytes);

            // The front face of a Direct3D triangle looks the other way than the cross product of its edges (OmsiObjectModel does the same).
            for (var index = 0; index + 2 < mesh.TriangleIndices.Count; index += 3)
            {
                var a = mesh.Positions[mesh.TriangleIndices[index]];
                var b = mesh.Positions[mesh.TriangleIndices[index + 1]];
                var c = mesh.Positions[mesh.TriangleIndices[index + 2]];
                var up = -((b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2]));
                Assert.Greater(up, 0.0, "triangle " + index / 3 + " does not face up");
            }

            // In the middle the slope of 0.1 tilts the normal to the west.
            var middle = mesh.Normals[30 * 61 + 30];
            Assert.AreEqual(-0.1 / Math.Sqrt(1.01), middle[0], 1e-4);
            Assert.AreEqual(1.0 / Math.Sqrt(1.01), middle[1], 1e-4);
            // At the east border the flat neighbour is looked at: the height rises 0.5 m over the 10 m from the sample before to the one beyond.
            var border = mesh.Normals[30 * 61 + 60];
            Assert.AreEqual(-0.05 / Math.Sqrt(1.0025), border[0], 1e-4);
        }

        [Test]
        public void ATileWithoutTerrainIsRefused()
        {
            var summary = session.Read();
            // Tile 2, 0 has no terrain file.
            Assert.Throws<InvalidOperationException>(() => session.ExportTerrainMesh(summary, summary.Tiles[2]));
        }
    }
}
