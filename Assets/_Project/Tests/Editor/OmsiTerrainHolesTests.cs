using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTerrainHolesTests
    {
        private string installation;

        [SetUp]
        public void CreateInstallation()
        {
            installation = Path.Combine(Path.GetTempPath(), "mapforge-holes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Kreuz", "model"));
            Directory.CreateDirectory(Path.Combine(installation, "Splines", "Rail"));
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Kreuz", "crossing.sco"),
                "[mesh]\r\ncrossing.o3d\r\n\r\n[terrainhole]\r\ncrossing_hole.x\r\n");
            // A square of 10 m from x -5 to 5 and z 0 to 10 in the object frame, at height 0.
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Kreuz", "model", "crossing_hole.x"),
                "xof 0303txt 0032\nMesh Hole {\n  4; -5;0;0;, 5;0;0;, 5;0;10;, -5;0;10;;\n  1; 4;0,1,2,3;;\n}\n");
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Kreuz", "plain.sco"), "[mesh]\r\nplain.o3d\r\n");
            File.WriteAllText(Path.Combine(installation, "Splines", "Rail", "cutting.sli"),
                "[texture]\r\nballast.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-2\r\n0\r\n0\r\n0.1\r\n\r\n[profilepnt]\r\n2\r\n0\r\n1\r\n0.1\r\n\r\n" +
                "[terrainholeprofile]\r\n\r\n[terrainholeprofilepnt]\r\n-3\r\n0\r\n0\r\n\r\n[terrainholeprofilepnt]\r\n3\r\n0\r\n0\r\n");
        }

        [TearDown]
        public void DeleteInstallation()
        {
            if (Directory.Exists(installation))
                Directory.Delete(installation, true);
        }

        [Test]
        public void MarksThePixelsInsideATriangle()
        {
            var mask = new OmsiTerrainHoleMask(0, 0);
            // Half of the tile: the triangle below the diagonal from the south-west to the north-east corner.
            mask.AddTriangle(0, 0, 300, 0, 300, 300, 300);
            var resolution = OmsiTerrainHoleMask.Resolution;
            Assert.AreEqual(255, mask.Pixels[10 * resolution + 400]);
            Assert.AreEqual(0, mask.Pixels[400 * resolution + 10]);
            Assert.That(mask.HolePixelCount, Is.InRange(resolution * resolution / 2 - resolution, resolution * resolution / 2 + resolution));

            // Shrinking by 3 m frees a band along the diagonal but not along the tile edges, where a hole may go on.
            var before = mask.HolePixelCount;
            mask.Shrink(3, 300);
            var pixel = 300.0 / resolution;
            byte At(double x, double y) => mask.Pixels[(int)(y / pixel) * resolution + (int)(x / pixel)];
            Assert.AreEqual(0, At(150, 148));
            Assert.AreEqual(255, At(150, 140));
            Assert.AreEqual(255, At(299.9, 0.1));
            Assert.Less(mask.HolePixelCount, before);
        }

        [Test]
        public void TurnsLikeOmsiObjects()
        {
            OmsiTerrainHoleCatalog.Rotate(0, 0, 10, 90, 0, 0, out var east, out var north);
            Assert.AreEqual(10, east, 1e-9);
            Assert.AreEqual(0, north, 1e-9);
            OmsiTerrainHoleCatalog.Rotate(1, 0, 0, 90, 0, 0, out east, out north);
            Assert.AreEqual(0, east, 1e-9);
            Assert.AreEqual(-1, north, 1e-9);
            // Positive pitch lowers the front, so a point ahead comes closer seen from above.
            OmsiTerrainHoleCatalog.Rotate(0, 0, 10, 0, 60, 0, out east, out north);
            Assert.AreEqual(5, north, 1e-9);
        }

        [Test]
        public void CutsUnderObjectsWithHoleMeshesAndAlongHoleSplinesIntoEveryTileTheyReach()
        {
            var catalog = new OmsiTerrainHoleCatalog(installation);
            var placements = new List<OmsiHolePlacement>
            {
                // Turned east at the east edge of tile 0, 0, so the square reaches into tile 1, 0.
                new OmsiHolePlacement { TileX = 0, TileY = 0, AssetPath = @"Sceneryobjects\Kreuz\crossing.sco", LocalX = 295, LocalY = 150, HeadingDegrees = 90 },
                new OmsiHolePlacement { TileX = 0, TileY = 0, AssetPath = @"Sceneryobjects\Kreuz\plain.sco", LocalX = 50, LocalY = 50 }
            };
            var spline = new OmsiSplineRecord { AssetPath = @"Splines\Rail\cutting.sli", LocalX = 100, LocalY = 0, HeadingDegrees = 0, LengthMeters = 100 };
            var splines = new List<KeyValuePair<long, OmsiSplineRecord>> { new KeyValuePair<long, OmsiSplineRecord>(OmsiTerrainHoleCatalog.TileKey(0, 1), spline) };
            var available = new HashSet<long> { OmsiTerrainHoleCatalog.TileKey(0, 0), OmsiTerrainHoleCatalog.TileKey(1, 0), OmsiTerrainHoleCatalog.TileKey(0, 1) };

            var masks = catalog.Build(placements, splines, new OmsiSplineProfileCatalog(installation), available, 300);

            CollectionAssert.IsEmpty(catalog.Problems);
            Assert.AreEqual(3, masks.Count);
            var resolution = OmsiTerrainHoleMask.Resolution;
            double pixel = 300.0 / resolution;
            byte At(OmsiTerrainHoleMask mask, double x, double y) => mask.Pixels[(int)(y / pixel) * resolution + (int)(x / pixel)];
            var home = masks[OmsiTerrainHoleCatalog.TileKey(0, 0)];
            var east = masks[OmsiTerrainHoleCatalog.TileKey(1, 0)];
            // Heading 90: the square covers x 295 to 305 and y 145 to 155.
            Assert.AreEqual(255, At(home, 298, 150));
            Assert.AreEqual(0, At(home, 298, 158));
            Assert.AreEqual(255, At(east, 3, 150));
            Assert.AreEqual(0, At(east, 7, 150));
            Assert.AreEqual(0, At(home, 50, 50));
            // The cutting runs north from x 100, y 0 of tile 0, 1 and cuts 3 m to each side.
            var cutting = masks[OmsiTerrainHoleCatalog.TileKey(0, 1)];
            Assert.AreEqual(255, At(cutting, 98, 50));
            Assert.AreEqual(255, At(cutting, 102, 90));
            Assert.AreEqual(0, At(cutting, 105, 50));
        }

        [Test]
        public void ReadsHoleSectionsOfObjectsAndProfiles()
        {
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes("[mesh]\r\na.o3d\r\n\r\n[terrainhole]\r\na_hole.o3d\r\n"));
            CollectionAssert.AreEqual(new[] { "a_hole.o3d" }, sceneryObject.TerrainHoleMeshes);
            var profile = OmsiSplineProfile.Parse(File.ReadAllBytes(Path.Combine(installation, "Splines", "Rail", "cutting.sli")), @"Splines\Rail\cutting.sli");
            Assert.AreEqual(1, profile.TerrainHoles.Count);
            CollectionAssert.AreEqual(new[] { -3.0, 3.0 }, profile.TerrainHoles[0]);
            Assert.AreEqual(1, profile.Strips.Count);
        }
    }
}
