using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSplineProfileTests
    {
        /// <summary>A sidewalk with a kerb face and one lane, as StreetCreator writes them, with comments in between.</summary>
        private const string Street = "File created with StreetCreator v1.000\r\n\r\n" +
                                      "[texture]\r\nstr_side1.bmp\r\n\r\n" +
                                      "[texture]\r\nzaun.tga\r\n[matl_alpha]\r\n1\r\nThe [profile]-command starts a profile.\r\n\r\n" +
                                      "Left sidewalk:\r\n[profile]\r\n0\r\n\r\n" +
                                      "[profilepnt]\r\n-4\r\n0.25\r\n0.2\r\n0.2\r\n\r\n" +
                                      "[profilepnt]\r\n-2\r\n0.25\r\n0.9\r\n0.2\r\n\r\n" +
                                      "[profilepnt]\r\n-2\r\n0.1\r\n0.95\r\n0.2\r\n\r\n" +
                                      "Lane:\r\n[profile]\r\n1\r\n\r\n" +
                                      "[profilepnt]\r\n-2\r\n0.1\r\n0\r\n0.5\r\n\r\n" +
                                      "[profilepnt]\r\n1\r\n0.1\r\n1\r\n0.5\r\n\r\n" +
                                      "[profile]\r\n5\r\n\r\n[profilepnt]\r\n0\r\n0\r\n0\r\n0\r\n";

        [Test]
        public void ReadsTexturesAlphaAndProfilesNextToTheFile()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Street), @"Splines\Test\street.sli");

            Assert.AreEqual(2, profile.Textures.Count);
            Assert.AreEqual(Path.Combine(@"Splines\Test", "texture", "str_side1.bmp"), profile.Textures[0].Path);
            Assert.IsFalse(profile.Textures[0].UsesAlpha);
            Assert.IsTrue(profile.Textures[1].UsesAlpha);
            // The profile with a single point is dropped.
            Assert.AreEqual(2, profile.Strips.Count);
            Assert.AreEqual(3, profile.Strips[0].Points.Count);
            Assert.AreEqual(0.25, profile.Strips[0].Points[0].Height);
            Assert.AreEqual(0.5, profile.Strips[1].Points[1].VPerMeter);
            Assert.IsFalse(profile.EditorOnly);
            Assert.IsTrue(OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes("Invisible Street\r\n[onlyeditor]\r\n" + Street), "invis.sli").EditorOnly);
        }

        [Test]
        public void ExtrudesStripsAlongStraightSplinesAndSkipsKerbFaces()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Street), @"Splines\Test\street.sli");
            // Heading 90 runs east; 10 m long, rising 1 m.
            var spline = new OmsiSplineRecord { LocalX = 100, LocalY = 50, Height = 30, EndHeight = 31, HeadingDegrees = 90, LengthMeters = 10 };

            var meshes = OmsiSplineSurface.Build(spline, profile);

            Assert.AreEqual(2, meshes.Count);
            var sidewalk = meshes.Single(mesh => mesh.TextureIndex == 0);
            // Only the flat part: one band of two quads' worth of vertices for a straight spline.
            Assert.AreEqual(4, sidewalk.VertexCount);
            Assert.AreEqual(6, sidewalk.Triangles.Count);
            // Left of an eastbound spline is north: x -4 lies at y 54.
            Assert.AreEqual(100.0, sidewalk.Positions[0], 1e-9);
            Assert.AreEqual(30.25, sidewalk.Positions[1], 1e-9);
            Assert.AreEqual(54.0, sidewalk.Positions[2], 1e-9);
            // End of the band: 10 m east, 1 m higher; V = 10 m × 0.2.
            Assert.AreEqual(110.0, sidewalk.Positions[6], 1e-9);
            Assert.AreEqual(31.25, sidewalk.Positions[7], 1e-9);
            Assert.AreEqual(2.0, sidewalk.Uvs[5], 1e-9);

            // For the 3D view the kerb face comes along: a second band, from 0.25 m down to 0.1 m at x -2.
            var withUpright = OmsiSplineSurface.Build(spline, profile, true).Single(mesh => mesh.TextureIndex == 0);
            Assert.AreEqual(8, withUpright.VertexCount);
            Assert.AreEqual(30.1, withUpright.Positions[4 * 3 + 4], 1e-9);
        }

        [Test]
        public void TellsOverheadWiresAndFencesFromSplinesWithASurface()
        {
            const string wire = "[texture]\r\nkabel.tga\r\n[profile]\r\n0\r\n[profilepnt]\r\n0\r\n5.3\r\n0\r\n1\r\n[profilepnt]\r\n0\r\n7\r\n1\r\n1\r\n";
            const string fence = "[texture]\r\nzaun.tga\r\n[profile]\r\n0\r\n[profilepnt]\r\n0\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n0\r\n2.2\r\n1\r\n1\r\n";
            Assert.AreEqual(OmsiSplineLineKind.Overhead, OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(wire), "kabel.sli").LineKind);
            Assert.AreEqual(OmsiSplineLineKind.Fence, OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(fence), "zaun.sli").LineKind);
            Assert.AreEqual(OmsiSplineLineKind.None, OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Street), "street.sli").LineKind);
            // A brick wall 0.3 m thick with its cap on top counts as a fence; a kerb of the same width does not.
            const string wall = "[texture]\r\nwall.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n-0.15\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n-0.15\r\n2\r\n1\r\n1\r\n" +
                                "[profile]\r\n0\r\n[profilepnt]\r\n-0.15\r\n2\r\n0\r\n1\r\n[profilepnt]\r\n0.15\r\n2\r\n1\r\n1\r\n";
            const string kerb = "[texture]\r\nkerb.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n-0.15\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n-0.15\r\n0.15\r\n1\r\n1\r\n" +
                                "[profile]\r\n0\r\n[profilepnt]\r\n-0.15\r\n0.15\r\n0\r\n1\r\n[profilepnt]\r\n0.15\r\n0.15\r\n1\r\n1\r\n";
            Assert.AreEqual(OmsiSplineLineKind.Fence, OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(wall), "wall.sli").LineKind);
            Assert.AreEqual(OmsiSplineLineKind.None, OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(kerb), "kerb.sli").LineKind);
            Assert.AreEqual(OmsiSplineLineKind.None, OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes("[onlyeditor]\r\n" + fence), "path.sli").LineKind);
        }

        [Test]
        public void MirroredSplinesTurnTheirCrossSectionOverAndCurvesAreSampled()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Street), @"Splines\Test\street.sli");
            var mirrored = new OmsiSplineRecord { HeadingDegrees = 0, LengthMeters = 10, IsMirrored = true };
            // A quarter circle with radius 20 to the right.
            var curve = new OmsiSplineRecord { HeadingDegrees = 0, LengthMeters = Math.PI * 10, RadiusMeters = 20 };

            var sidewalk = OmsiSplineSurface.Build(mirrored, profile).Single(mesh => mesh.TextureIndex == 0);
            var lane = OmsiSplineSurface.Build(curve, profile).Single(mesh => mesh.TextureIndex == 1);

            // Northbound and mirrored: x -4 now lies 4 m east.
            Assert.AreEqual(4.0, sidewalk.Positions[0], 1e-9);
            // 90° in steps of at most 2°: 45 steps, 46 pairs of vertices.
            Assert.AreEqual(92, lane.VertexCount);
            // The last right-edge point (x 1) faces south after turning east: 20 m east, 20 m north, then 1 m south.
            var last = lane.VertexCount - 1;
            Assert.AreEqual(20.0, lane.Positions[last * 3], 1e-6);
            Assert.AreEqual(19.0, lane.Positions[last * 3 + 2], 1e-6);
        }

        [Test]
        public void ReadsTheMirrorFlagAndEndHeightOfTileSplines()
        {
            var folder = Path.Combine(Path.GetTempPath(), "mapforge-sli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode("[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
                File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), OmsiTextFile.Encode(string.Join("\r\n",
                    "[version]", "14", "",
                    "[spline]", "0", "road.sli", "1", "0", "0", "0", "30", "0", "0", "100", "0", "2", "4", "0", "0", "0", "0", "0", "mirror", "",
                    "[spline]", "0", "road.sli", "2", "0", "0", "0", "30", "0", "0", "10", "0", "0", "0", "0", "0", "0", "0", "0", "",
                    "Spline mirror note") + "\r\n", OmsiTextEncoding.Utf16LittleEndian));

                var splines = OmsiMapReader.Read(Path.Combine(folder, "global.cfg")).Tiles[0].Splines;

                Assert.IsTrue(splines[0].IsMirrored);
                Assert.AreEqual(33.0, splines[0].EndHeight, 1e-9);
                Assert.IsFalse(splines[1].IsMirrored);
                Assert.AreEqual(30.0, splines[1].EndHeight, 1e-9);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
