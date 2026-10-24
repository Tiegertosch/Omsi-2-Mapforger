using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiGeneratedObjectTests
    {
        private const string ProfilePath = @"Splines\Test\road.sli";

        // 14 m wide: pavements 3 m wide and 0.25 m high, kerb faces, an 8 m carriageway at 0.1 m; a lane each way and a
        // footpath on each pavement used both ways.
        private const string Profile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-7\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.1\r\n1\r\n0.2\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.1\r\n0\r\n0.2\r\n[profilepnt]\r\n4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n7\r\n0.25\r\n1\r\n0.2\r\n" +
            "[path]\r\n1\r\n-5.5\r\n0.25\r\n1\r\n2\r\n[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n[path]\r\n1\r\n5.5\r\n0.25\r\n1\r\n2\r\n";

        private string installation;

        [SetUp]
        public void CreateInstallation()
        {
            installation = Path.Combine(Path.GetTempPath(), "mapforge-generated-" + Guid.NewGuid().ToString("N"));
            var textures = Path.Combine(installation, "Splines", "Test", "texture");
            Directory.CreateDirectory(textures);
            File.WriteAllText(Path.Combine(installation, "Splines", "Test", "road.sli"), Profile);
            File.WriteAllBytes(Path.Combine(textures, "side.bmp"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(textures, "asphalt.bmp"), new byte[] { 2 });
            File.WriteAllBytes(Path.Combine(textures, "asphalt.dds"), new byte[] { 3 });
        }

        [TearDown]
        public void RemoveInstallation()
        {
            if (Directory.Exists(installation))
                Directory.Delete(installation, true);
        }

        [Test]
        public void AStraightPieceReadsBackWithItsConnectionPointsLanesAndSurfaces()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);

            var piece = OmsiJunctionGenerator.StraightPiece(ProfilePath, profile, 12, "Test piece");

            var sco = OmsiSceneryObject.Parse(OmsiTextFile.Encode(piece.Sco, OmsiTextEncoding.Windows1252));
            Assert.AreEqual(2, sco.SplineHelpers.Count);
            Assert.AreEqual(ProfilePath, sco.SplineHelpers[0].SplinePath);
            Assert.AreEqual(-6.0, sco.SplineHelpers[0].Y, 1e-9);
            Assert.AreEqual(180.0, sco.SplineHelpers[0].HeadingDegrees, 1e-9);
            Assert.AreEqual(6.0, sco.SplineHelpers[1].Y, 1e-9);
            Assert.AreEqual(0.0, sco.SplineHelpers[1].HeadingDegrees, 1e-9);
            Assert.AreEqual(4, sco.Paths.Count);
            // The lane against the profile runs backwards from the far end, the one along it forwards from the near end.
            var backwards = sco.Paths.Single(path => Math.Abs(path.X + 2.0) < 1e-9);
            Assert.AreEqual(6.0, backwards.Y, 1e-9);
            Assert.AreEqual(180.0, backwards.HeadingDegrees, 1e-9);
            Assert.AreEqual(12.0, backwards.LengthMeters, 1e-9);
            var forwards = sco.Paths.Single(path => Math.Abs(path.X - 2.0) < 1e-9);
            Assert.AreEqual(-6.0, forwards.Y, 1e-9);
            Assert.AreEqual(0.0, forwards.HeadingDegrees, 1e-9);
            Assert.AreEqual(3.6, forwards.WidthMeters, 1e-9);
            Assert.AreEqual("model.x", sco.Meshes.Single().FileName);

            var mesh = OmsiXMesh.Parse(Encoding.UTF8.GetBytes(piece.Model));
            CollectionAssert.AreEqual(new[] { "side.bmp", "asphalt.bmp" }, mesh.Materials.Select(material => material.Texture).ToArray());
            Assert.Greater(mesh.TriangleIndices.Count, 0);
            Assert.AreEqual(-7.0, mesh.Positions.Min(position => position[0]), 1e-5);
            Assert.AreEqual(7.0, mesh.Positions.Max(position => position[0]), 1e-5);
            Assert.AreEqual(-6.0, mesh.Positions.Min(position => position[2]), 1e-5);
            Assert.AreEqual(6.0, mesh.Positions.Max(position => position[2]), 1e-5);
            // The carriageway faces up, as OMSI shows only the front of a face.
            for (var index = 0; index < mesh.Positions.Count; index++)
                if (Math.Abs(mesh.Positions[index][1] - 0.1) < 1e-4 && Math.Abs(mesh.Positions[index][0]) < 3.9)
                    Assert.Greater(mesh.Normals[index][1], 0.99f);
            CollectionAssert.AreEqual(new[] { @"Splines\Test\texture\side.bmp", @"Splines\Test\texture\asphalt.bmp" }, piece.Textures);
        }

        [Test]
        public void TheStoreKeepsEachObjectInItsOwnFolderUnderMapforgeAndNeverOverwrites()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var piece = OmsiJunctionGenerator.StraightPiece(ProfilePath, profile, 12, "Test piece");

            var first = OmsiGeneratedObjectStore.Write(installation, "My: Project", "Test piece", piece);
            var second = OmsiGeneratedObjectStore.Write(installation, "My: Project", "Test piece", piece);

            Assert.AreEqual(@"Sceneryobjects\Mapforge\My_ Project\Test piece\Test piece.sco", first);
            Assert.AreEqual(@"Sceneryobjects\Mapforge\My_ Project\Test piece_2\Test piece_2.sco", second);
            var folder = Path.Combine(installation, "Sceneryobjects", "Mapforge", "My_ Project", "Test piece");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "model", "model.x")));
            CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(folder, "texture", "side.bmp")));
            CollectionAssert.AreEqual(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(folder, "texture", "asphalt.dds")));

            // The editor reads the stored object like any other: its connection points turn with it.
            var ports = new OmsiJunctionCatalog(installation).Ports(new OmsiTileSummary(),
                new OmsiObjectRecord { AssetPath = first, LocalX = 100, LocalY = 100, HeadingDegrees = 90 }, 300);
            Assert.AreEqual(2, ports.Count);
            Assert.AreEqual(94.0, ports[0].Position.X, 1e-9);
            Assert.AreEqual(270.0, ports[0].OutwardHeadingDegrees, 1e-9);
            Assert.AreEqual(106.0, ports[1].Position.X, 1e-9);
        }
    }
}
