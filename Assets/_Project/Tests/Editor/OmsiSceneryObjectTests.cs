using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSceneryObjectTests
    {
        private string installation;

        [SetUp]
        public void CreateInstallation()
        {
            installation = Path.Combine(Path.GetTempPath(), "mapforge-sco-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Test", "model"));
        }

        [TearDown]
        public void DeleteInstallation()
        {
            Directory.Delete(installation, true);
        }

        [Test]
        public void ReadsOlderO3dFilesWithShortCounts()
        {
            var mesh = OmsiO3dMesh.Parse(O3d(1, 0, null, new[] { V(0, 0, 0), V(4, 0, 0), V(0, 0, 2) }, new[] { 0, 1, 2 }));

            Assert.AreEqual(1, mesh.Version);
            Assert.IsFalse(mesh.WasScrambled);
            Assert.AreEqual(3, mesh.Vertices.Count);
            Assert.AreEqual(4f, mesh.Vertices[1].X);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, mesh.TriangleIndices);
        }

        [Test]
        public void ReadsLongIndicesAndOpenKeysWithoutUnscrambling()
        {
            var vertices = new[] { V(2, 1, 3), V(5, 6, 7), V(8, 9, 10) };
            var longIndices = OmsiO3dMesh.Parse(O3d(3, 1, null, vertices, new[] { 0, 1, 2 }));
            var openKey = OmsiO3dMesh.Parse(O3d(5, 0, 0xFFFFFFFFu, vertices, new[] { 0, 1, 2 }));

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, longIndices.TriangleIndices);
            Assert.IsFalse(openKey.WasScrambled);
            Assert.AreEqual(2f, openKey.Vertices[0].X);
            Assert.AreEqual(1f, openKey.Vertices[0].Y);
        }

        [Test]
        public void UnscramblesKeyZeroFiles()
        {
            // With key 0 the first vertex gets state 0: x and y swap; all normal components are negated, then y and z swap.
            var stored = new MeshVertex { X = 2, Y = 1, Z = 3, NX = 0.1f, NY = 0.2f, NZ = 0.3f, U = 0.5f, V = 0.25f };

            var mesh = OmsiO3dMesh.Parse(O3d(5, 0, 0u, new[] { stored }, new int[0]));

            Assert.IsTrue(mesh.WasScrambled);
            var vertex = mesh.Vertices[0];
            Assert.AreEqual(1f, vertex.X);
            Assert.AreEqual(2f, vertex.Y);
            Assert.AreEqual(3f, vertex.Z);
            Assert.AreEqual(-0.1f, vertex.NormalX, 1e-6f);
            Assert.AreEqual(-0.3f, vertex.NormalY, 1e-6f);
            Assert.AreEqual(-0.2f, vertex.NormalZ, 1e-6f);
            Assert.AreEqual(0.5f, vertex.U, 1e-6f);
        }

        [Test]
        public void SkipsUnknownBytesDropsBrokenTrianglesAndRejectsBadFiles()
        {
            var bytes = O3d(1, 0, null, new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1) }, new[] { 0, 1, 2, 0, 1, 9 });
            var withJunk = bytes.Concat(new byte[] { 0x01, 0x02 }).ToArray();

            var mesh = OmsiO3dMesh.Parse(withJunk);

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, mesh.TriangleIndices);
            Assert.Throws<InvalidDataException>(() => OmsiO3dMesh.Parse(new byte[] { 1, 2, 3 }));
            Assert.Throws<InvalidDataException>(() => OmsiO3dMesh.Parse(bytes.Take(12).ToArray()));
        }

        [Test]
        public void ReadsTextXMeshesWithFrameTransformsAndPolygons()
        {
            const string text = "xof 0303txt 0032\n" +
                                "template Dummy { <00000000-0000-0000-0000-000000000000> DWORD n; }\n" +
                                "Frame Root {\n" +
                                "  FrameTransformMatrix { 1,0,0,0, 0,0,1,0, 0,1,0,0, 10,0,0,1;; }\n" +
                                "  Frame Child {\n" +
                                "    FrameTransformMatrix { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,5,1;; }\n" +
                                "    Mesh Plane {\n" +
                                "      4; 0;0;0;, 2;0;0;, 2;1;0;, 0;1;0;;\n" +
                                "      1; 4;0,1,2,3;;\n" +
                                "      MeshMaterialList { 1; 1; 0;; { SomeMaterial } }\n" +
                                "    }\n" +
                                "  }\n" +
                                "}\n";

            var mesh = OmsiXMesh.Parse(Encoding.ASCII.GetBytes(text));

            Assert.AreEqual(4, mesh.Positions.Count);
            // Child: z += 5, then root: swap y and z, x += 10.
            CollectionAssert.AreEqual(new[] { 12f, 5f, 1f }, mesh.Positions[2]);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 2, 3 }, mesh.TriangleIndices);
            Assert.Throws<InvalidDataException>(() => OmsiXMesh.Parse(Encoding.ASCII.GetBytes("xof 0303bin 0032")));
        }

        [Test]
        public void ReadsXMaterialsNormalsAndTextureCoordinates()
        {
            // Two faces: the first uses a material defined at the top and referenced by name, the second an inline one.
            const string text = "xof 0303txt 0032\n" +
                                "Material Roof { 0.5;0.5;0.5;1.0;; 0.0; 0.0;0.0;0.0;; 0.0;0.0;0.0;; TextureFilename { \"roof.bmp\"; } }\n" +
                                "Mesh Box {\n" +
                                "  4; 0;0;0;, 1;0;0;, 1;0;1;, 0;0;1;;\n" +
                                "  2; 3;0,1,2;, 3;0,2,3;;\n" +
                                "  MeshMaterialList { 2; 2; 0, 1;;\n" +
                                "    { Roof }\n" +
                                "    Material Wall { 1.0;0.25;0.0;1.0;; 5.0; 0.0;0.0;0.0;; 0.0;0.0;0.0;; }\n" +
                                "  }\n" +
                                "  MeshNormals { 1; 0;1;0;; 2; 3;0,0,0;, 3;0,0,0;; }\n" +
                                "  MeshTextureCoords { 4; 0;0;, 1;0;, 1;1;, 0;1;; }\n" +
                                "}\n";

            var mesh = OmsiXMesh.Parse(Encoding.ASCII.GetBytes(text));

            Assert.AreEqual(2, mesh.Materials.Count);
            Assert.AreEqual("roof.bmp", mesh.Materials[0].Texture);
            Assert.AreEqual(0.25f, mesh.Materials[1].DiffuseGreen);
            Assert.AreEqual(string.Empty, mesh.Materials[1].Texture);
            CollectionAssert.AreEqual(new[] { 0, 1 }, mesh.TriangleMaterials);
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, mesh.Normals[2]);
            CollectionAssert.AreEqual(new[] { 1f, 1f }, mesh.Uvs[2]);
        }

        [Test]
        public void ParsesTheFootprintSectionsOfASceneryObject()
        {
            var sco = OmsiSceneryObject.Parse(Encoding.UTF8.GetBytes(string.Join("\r\n",
                "[friendlyname]", "Test", "",
                "[onlyeditor]", "", "[mesh]", "helper.x", "",
                "[mesh]", "house.o3d", "",
                "[collision_mesh]", "house_coll.o3d", "",
                "[boundingbox]", "4", "6", "3", "0", "1", "1.5", "",
                "[path]", "1", "2", "0", "90", "0", "8", "0", "0", "0", "3", "0", "0", "",
                "[splinehelper]", @"Splines\road.sli", "5", "0.2", "-3", "90", "0", "0", "",
                "[absheight]", "")));

            Assert.AreEqual(2, sco.Meshes.Count);
            Assert.IsTrue(sco.Meshes[0].EditorOnly);
            Assert.IsFalse(sco.Meshes[1].EditorOnly);
            CollectionAssert.AreEqual(new[] { "house_coll.o3d" }, sco.CollisionMeshes);
            CollectionAssert.AreEqual(new[] { 4.0, 6, 3, 0, 1, 1.5 }, sco.BoundingBox);
            Assert.AreEqual(3.0, sco.Paths.Single().WidthMeters);
            Assert.AreEqual(90.0, sco.Paths.Single().HeadingDegrees);
            var helper = sco.SplineHelpers.Single();
            Assert.AreEqual(5.0, helper.X);
            Assert.AreEqual(0.2, helper.Height);
            Assert.AreEqual(-3.0, helper.Y);
            Assert.IsTrue(sco.HasSplineHelper);
            Assert.IsTrue(sco.HasAbsoluteHeight);
        }

        [Test]
        public void BuildsOutlinesFromMeshesTreesBoundingBoxesAndPaths()
        {
            // The mesh is 4 m wide (X) and 2 m deep (Z) with a roof point at Y = 5 that must not affect the footprint.
            File.WriteAllBytes(Model("house.o3d"), O3d(1, 0, null, new[] { V(-2, 0, -1), V(2, 0, -1), V(2, 5, 1), V(-2, 0, 1) }, new[] { 0, 1, 2, 0, 2, 3 }));
            WriteSco("house.sco", "[mesh]", "house.o3d");
            WriteSco("tree.sco", "[tree]", "leaf.tga", "10", "14", "0.4", "0.6", "", "[mesh]", "treehelper.x");
            WriteSco("box.sco", "[mesh]", "missing.o3d", "", "[boundingbox]", "4", "6", "3", "1", "2", "1.5");
            WriteSco("crossing.sco", "[path]", "0", "0", "0", "0", "0", "10", "0", "0", "0", "2", "0", "0");
            WriteSco("empty.sco", "[friendlyname]", "Nothing");
            var catalog = new OmsiObjectOutlineCatalog(installation);

            var house = catalog.Get(@"Sceneryobjects\Test\house.sco");
            var tree = catalog.Get(@"Sceneryobjects\Test\tree.sco");
            var box = catalog.Get(@"Sceneryobjects\Test\box.sco");
            var crossing = catalog.Get(@"Sceneryobjects\Test\crossing.sco");
            var empty = catalog.Get(@"Sceneryobjects\Test\empty.sco");
            var missing = catalog.Get(@"Sceneryobjects\Test\gone.sco");

            Assert.AreEqual(OmsiOutlineSource.Mesh, house.Source);
            Assert.AreEqual(4, house.Points.Length);
            Assert.AreEqual(-2.0, house.Points.Min(p => p.X), 1e-6);
            Assert.AreEqual(1.0, house.Points.Max(p => p.Y), 1e-6);
            Assert.AreEqual(OmsiOutlineSource.Tree, tree.Source);
            // Mean height 12 m times mean ratio 0.5 is a 6 m crown, so a 3 m radius.
            Assert.AreEqual(3.0, tree.Points.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)), 1e-6);
            Assert.AreEqual(OmsiOutlineSource.BoundingBox, box.Source);
            Assert.AreEqual(-1.0, box.Points.Min(p => p.X), 1e-9);
            Assert.AreEqual(5.0, box.Points.Max(p => p.Y), 1e-9);
            StringAssert.Contains("missing.o3d", box.Problem);
            Assert.AreEqual(OmsiOutlineSource.Paths, crossing.Source);
            Assert.AreEqual(-1.0, crossing.Points.Min(p => p.X), 1e-6);
            Assert.AreEqual(10.0, crossing.Points.Max(p => p.Y), 1e-6);
            Assert.AreEqual(OmsiOutlineSource.None, empty.Source);
            Assert.AreEqual(0, empty.Points.Length);
            StringAssert.Contains("not found", missing.Problem);
            Assert.AreSame(house, catalog.Get(@"SCENERYOBJECTS\test\HOUSE.sco"));
        }

        [Test]
        public void ConvexHullIsCounterClockwiseAndDropsInnerPoints()
        {
            var hull = OmsiConvexHull.Compute(new List<OmsiMapPoint>
            {
                new OmsiMapPoint(0, 0), new OmsiMapPoint(2, 0), new OmsiMapPoint(1, 1), new OmsiMapPoint(2, 2), new OmsiMapPoint(0, 2), new OmsiMapPoint(2, 2)
            });

            Assert.AreEqual(4, hull.Length);
            var area = 0.0;
            for (int index = 0, previous = hull.Length - 1; index < hull.Length; previous = index++)
                area += hull[previous].X * hull[index].Y - hull[index].X * hull[previous].Y;
            Assert.AreEqual(8.0, area, 1e-9);
            Assert.AreEqual(0, OmsiConvexHull.Compute(new List<OmsiMapPoint>()).Length);
        }

        [Test]
        public void TracesLShapedFootprintsInsteadOfTheirHull()
        {
            // 10 × 4 m along x and 4 × 10 m along y: 64 m², while the hull would cover 82 m².
            var corners = Rectangle(0, 0, 10, 4).Concat(Rectangle(0, 0, 4, 10)).ToList();

            var contours = OmsiFootprintTracer.Trace(corners);

            Assert.AreEqual(1, contours.Count);
            Assert.AreEqual(64.0, OmsiFootprintTracer.SignedArea(contours[0]), 2.0);
            Assert.AreEqual(6, contours[0].Length);
            Assert.AreEqual(0.0, contours[0].Min(p => p.X), OmsiFootprintTracer.FinestCellSize);
            Assert.AreEqual(10.0, contours[0].Max(p => p.Y), OmsiFootprintTracer.FinestCellSize);
        }

        [Test]
        public void KeepsCourtyardsAsClockwiseHoles()
        {
            // A 20 m square around a 10 m courtyard.
            var corners = Rectangle(0, 0, 20, 5).Concat(Rectangle(0, 15, 20, 20))
                .Concat(Rectangle(0, 5, 5, 15)).Concat(Rectangle(15, 5, 20, 15)).ToList();

            var areas = OmsiFootprintTracer.Trace(corners).Select(contour => OmsiFootprintTracer.SignedArea(contour)).OrderBy(area => area).ToList();

            Assert.AreEqual(2, areas.Count);
            Assert.AreEqual(-100.0, areas[0], 3.0);
            Assert.AreEqual(400.0, areas[1], 3.0);
        }

        [Test]
        public void WallsSeenFromAboveAndCornerContactsStaySeparateThinParts()
        {
            // A fence: two vertical triangles that collapse onto the line from (0, 0) to (10, 0) seen from above.
            var fence = new List<OmsiMapPoint>
            {
                new OmsiMapPoint(0, 0), new OmsiMapPoint(10, 0), new OmsiMapPoint(10, 0),
                new OmsiMapPoint(0, 0), new OmsiMapPoint(10, 0), new OmsiMapPoint(0, 0)
            };
            var touching = Rectangle(0, 0, 2, 2).Concat(Rectangle(2, 2, 4, 4)).ToList();

            var fenceContours = OmsiFootprintTracer.Trace(fence);
            var touchingContours = OmsiFootprintTracer.Trace(touching);

            Assert.AreEqual(1, fenceContours.Count);
            Assert.That(OmsiFootprintTracer.SignedArea(fenceContours[0]), Is.InRange(1.0, 10.0 * 0.5));
            Assert.AreEqual(2, touchingContours.Count);
            Assert.IsTrue(touchingContours.All(contour => Math.Abs(OmsiFootprintTracer.SignedArea(contour) - 4.0) < 0.5));
            CollectionAssert.IsEmpty(OmsiFootprintTracer.Trace(new List<OmsiMapPoint>()));
        }

        [Test]
        public void MeshAndPathOutlinesFollowTheShapeAndKeepTheHull()
        {
            // An L-shaped flat roof from two quads, and a crossing of two lanes without a model.
            File.WriteAllBytes(Model("corner.o3d"), O3d(1, 0, null,
                new[] { V(0, 3, 0), V(10, 3, 0), V(10, 3, 4), V(0, 3, 4), V(0, 3, 10), V(4, 3, 10), V(4, 3, 4) },
                new[] { 0, 1, 2, 0, 2, 3, 0, 3, 6, 3, 4, 5, 3, 5, 6 }));
            WriteSco("corner.sco", "[mesh]", "corner.o3d");
            WriteSco("cross.sco",
                "[path]", "0", "-10", "0", "0", "0", "20", "0", "0", "0", "3", "0", "0", "",
                "[path]", "-10", "0", "0", "90", "0", "20", "0", "0", "0", "3", "0", "0");
            var catalog = new OmsiObjectOutlineCatalog(installation);

            var corner = catalog.Get(@"Sceneryobjects\Test\corner.sco");
            var cross = catalog.Get(@"Sceneryobjects\Test\cross.sco");

            Assert.AreEqual(5, corner.Points.Length);
            Assert.AreEqual(1, corner.Contours.Count);
            Assert.AreEqual(64.0, OmsiFootprintTracer.SignedArea(corner.Contours[0]), 2.0);
            Assert.AreEqual(OmsiOutlineSource.Paths, cross.Source);
            Assert.AreEqual(1, cross.Contours.Count);
            // Two 20 × 3 m lanes crossing: 111 m², not the 20 × 20 m hull.
            Assert.AreEqual(111.0, OmsiFootprintTracer.SignedArea(cross.Contours[0]), 4.0);
            Assert.AreEqual(12, cross.Contours[0].Length);
        }

        [Test]
        public void CrossedUprightPlanesOfPlantsBecomeTheirHull()
        {
            // Two 6 m wide, 6 m high planes crossing at the origin: a common way to model a bush.
            File.WriteAllBytes(Model("bush.o3d"), O3d(1, 0, null,
                new[] { V(-3, 0, 0), V(3, 0, 0), V(3, 6, 0), V(-3, 6, 0), V(0, 0, -3), V(0, 0, 3), V(0, 6, 3), V(0, 6, -3) },
                new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }));
            WriteSco("bush.sco", "[mesh]", "bush.o3d");

            var bush = new OmsiObjectOutlineCatalog(installation).Get(@"Sceneryobjects\Test\bush.sco");

            Assert.AreEqual(1, bush.Contours.Count);
            Assert.AreEqual(18.0, OmsiFootprintTracer.SignedArea(bush.Contours[0]), 2.0);
        }

        /// <summary>Two triangles covering the rectangle from (x0, y0) to (x1, y1).</summary>
        private static IEnumerable<OmsiMapPoint> Rectangle(double x0, double y0, double x1, double y1)
        {
            return new[]
            {
                new OmsiMapPoint(x0, y0), new OmsiMapPoint(x1, y0), new OmsiMapPoint(x1, y1),
                new OmsiMapPoint(x0, y0), new OmsiMapPoint(x1, y1), new OmsiMapPoint(x0, y1)
            };
        }

        [Test]
        public void BuildsModelsFromTheVisibleMeshesWithTheirMaterials()
        {
            // Two triangles with different materials; the second material's texture exists only as .dds.
            File.WriteAllBytes(Model("shop.o3d"), O3d(1, 0, null, new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1) },
                new[] { 0, 1, 2, 1, 3, 2 }, new[] { 0, 1 }, new[] { "wall.bmp", "glass.bmp" }));
            File.WriteAllBytes(Model("shop_low.o3d"), O3d(1, 0, null, new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1) }, new[] { 0, 1, 2 }));
            File.WriteAllBytes(Model("lamp.o3d"), O3d(1, 0, null, new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1) }, new[] { 0, 1, 2 }));
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Test", "texture"));
            File.WriteAllBytes(Path.Combine(installation, "Sceneryobjects", "Test", "texture", "wall.bmp"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(installation, "Sceneryobjects", "Test", "texture", "glass.dds"), new byte[] { 1 });
            WriteSco("shop.sco",
                "[absheight]", "",
                "[LOD]", "0.3", "",
                "[mesh]", "shop.o3d", "", "[matl]", "glass.bmp", "0", "", "[matl_alpha]", "1", "",
                "[mesh]", "lamp.o3d", "", "[visible]", "red", "1", "",
                "[LOD]", "0.05", "",
                "[mesh]", "shop_low.o3d");

            var catalog = new OmsiObjectModelCatalog(installation);
            var shop = catalog.Get(@"Sceneryobjects\Test\shop.sco");

            Assert.IsTrue(shop.HasAbsoluteHeight);
            // Only the first level; the red lamp is off by default.
            Assert.AreEqual(4, shop.VertexCount);
            Assert.AreEqual(2, shop.Materials.Count);
            Assert.AreEqual(Path.Combine(@"Sceneryobjects\Test", "texture", "wall.bmp"), shop.Materials[0].TexturePath);
            Assert.AreEqual(Path.Combine(@"Sceneryobjects\Test", "texture", "glass.dds"), shop.Materials[1].TexturePath);
            Assert.AreEqual(0, shop.Materials[0].AlphaMode);
            Assert.AreEqual(1, shop.Materials[1].AlphaMode);
            Assert.AreEqual(0.5f, shop.Materials[0].Red);
            CollectionAssert.AreEqual(new[] { 1, 3, 2 }, shop.Triangles[1]);
            CollectionAssert.IsEmpty(catalog.Problems);
        }

        [Test]
        public void ReadsTerrainHeightsFromTheSouthWestCorner()
        {
            var bytes = new byte[4 + 61 * 61 * 4];
            BitConverter.GetBytes(60).CopyTo(bytes, 0);
            // Height grows by 1 m per column eastwards and by 10 m per row northwards.
            for (var row = 0; row < 61; row++)
                for (var column = 0; column < 61; column++)
                    BitConverter.GetBytes((float)(column + 10 * row)).CopyTo(bytes, 4 + (row * 61 + column) * 4);

            var terrain = OmsiTerrain.Parse(bytes);

            Assert.AreEqual(0.0, terrain.HeightAt(0, 0, 300), 1e-9);
            Assert.AreEqual(2.5 + 10 * 1.5, terrain.HeightAt(12.5, 7.5, 300), 1e-5);
            Assert.AreEqual(60.0 + 600.0, terrain.HeightAt(400, 400, 300), 1e-9);
            Assert.Throws<InvalidDataException>(() => OmsiTerrain.Parse(new byte[12]));
        }

        [Test]
        public void ReadsCompressedDdsLevelsWithinASizeLimitAndReducesImages()
        {
            // DXT1, 8 × 8 with two mip levels: 4 blocks, then 1.
            var bytes = new byte[128 + 32 + 8];
            Encoding.ASCII.GetBytes("DDS ").CopyTo(bytes, 0);
            BitConverter.GetBytes(0x20000).CopyTo(bytes, 8);
            BitConverter.GetBytes(8).CopyTo(bytes, 12);
            BitConverter.GetBytes(8).CopyTo(bytes, 16);
            BitConverter.GetBytes(2).CopyTo(bytes, 28);
            BitConverter.GetBytes(0x4).CopyTo(bytes, 80);
            BitConverter.GetBytes(0x31545844).CopyTo(bytes, 84);
            bytes[128 + 32] = 7;

            Assert.IsTrue(OmsiImageReader.TryReadCompressed(bytes, 1024, out var full));
            Assert.IsTrue(OmsiImageReader.TryReadCompressed(bytes, 4, out var reduced));

            Assert.AreEqual(2, full.Levels.Count);
            Assert.AreEqual(8, full.Width);
            Assert.AreEqual(1, reduced.Levels.Count);
            Assert.AreEqual(4, reduced.Width);
            Assert.AreEqual(7, reduced.Levels[0][0]);
            var image = new OmsiImage(2, 2, 1, new byte[] { 0, 10, 20, 30 }).Reduce(1);
            Assert.AreEqual(1, image.Width);
            Assert.AreEqual(15, image.Pixels[0]);
        }

        private struct MeshVertex
        {
            public float X, Y, Z, NX, NY, NZ, U, V;
        }

        private static MeshVertex V(float x, float y, float z)
        {
            return new MeshVertex { X = x, Y = y, Z = z, NY = 1 };
        }

        /// <summary>Writes an .o3d file: header, vertices, triangles, optional materials and an identity matrix.</summary>
        private static byte[] O3d(byte version, byte flags, uint? key, MeshVertex[] vertices, int[] triangleIndices,
            int[] triangleMaterials = null, string[] materialTextures = null)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)0x84);
                writer.Write((byte)0x19);
                writer.Write(version);
                if (version >= 3)
                    writer.Write(flags);
                if (version >= 4)
                    writer.Write(key ?? 0u);
                var wide = version >= 3;
                writer.Write((byte)0x17);
                if (wide) writer.Write((uint)vertices.Length); else writer.Write((ushort)vertices.Length);
                foreach (var v in vertices)
                    foreach (var value in new[] { v.X, v.Y, v.Z, v.NX, v.NY, v.NZ, v.U, v.V })
                        writer.Write(value);
                writer.Write((byte)0x49);
                var triangles = triangleIndices.Length / 3;
                if (wide) writer.Write((uint)triangles); else writer.Write((ushort)triangles);
                for (var index = 0; index < triangleIndices.Length; index++)
                {
                    if ((flags & 1) != 0) writer.Write((uint)triangleIndices[index]); else writer.Write((ushort)triangleIndices[index]);
                    if (index % 3 == 2)
                        writer.Write((ushort)(triangleMaterials?[index / 3] ?? 0));
                }
                if (materialTextures != null)
                {
                    writer.Write((byte)0x26);
                    writer.Write((ushort)materialTextures.Length);
                    foreach (var texture in materialTextures)
                    {
                        foreach (var value in new[] { 0.5f, 0.25f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f })
                            writer.Write(value);
                        writer.Write((byte)texture.Length);
                        writer.Write(Encoding.ASCII.GetBytes(texture));
                    }
                }
                writer.Write((byte)0x79);
                for (var index = 0; index < 16; index++)
                    writer.Write(index % 5 == 0 ? 1f : 0f);
                return stream.ToArray();
            }
        }

        private string Model(string name)
        {
            return Path.Combine(installation, "Sceneryobjects", "Test", "model", name);
        }

        private void WriteSco(string name, params string[] lines)
        {
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Test", name), string.Join("\r\n", lines) + "\r\n");
        }
    }
}
