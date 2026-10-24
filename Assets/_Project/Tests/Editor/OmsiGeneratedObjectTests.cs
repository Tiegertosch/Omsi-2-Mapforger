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
        public void ATeeJunctionHasRoundedCornersArmsMatchingTheRoadAndLanesForEveryWayThrough()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile })
                .ToList();

            var layout = OmsiJunctionLayout.Build(arms, @"Splines\Test\texture\plain.bmp");

            // The kerbs of neighbouring arms meet 4 m out and are rounded with 6 m, which reaches 10 m along each; the
            // pavements' outer edges with 3 m reach as far. Every arm ends 2 m beyond, 12 m from the centre.
            Assert.IsTrue(layout.Arms.All(arm => Math.Abs(arm.Length - 12.0) < 1e-9), string.Join(", ", layout.Arms.Select(arm => arm.Length)));
            var road = layout.Meshes.Single(mesh => mesh.Texture == "plain.bmp");
            // Pavements and kerb faces share a texture here but stay separate strips.
            var sides = layout.Meshes.Where(mesh => mesh.Texture == "side.bmp").ToList();
            Assert.AreEqual(2, sides.Count);
            var pavement = sides[0];
            Assert.AreEqual(@"Splines\Test\texture\plain.bmp", layout.Textures["plain.bmp"]);
            // At each connection point the cross-section is the road's: carriageway edges at 0.1 m, pavement edges at 0.25 m.
            Assert.IsTrue(HasVertex(road, 0, 12, -4, 0.1) && HasVertex(road, 0, 12, 4, 0.1));
            Assert.IsTrue(HasVertex(pavement, 12, 0, 0, 0.25) || HasVertex(pavement, 12, -7, 0, 0.25));
            // The centre and the side arm are carriageway; the pavement runs along the straight side.
            Assert.IsTrue(Covers(road, 0, 0));
            Assert.IsTrue(Covers(road, 0, 10));
            Assert.IsFalse(Covers(road, 0, -5.5));
            Assert.IsTrue(Covers(pavement, 0, -5.5));
            Assert.IsFalse(Covers(road, 6, 6));
            // Round the corner between the side arm and the arm to its right the pavement is a band between the kerb,
            // rounded with 6 m about 10, 10, and its outer edge, rounded with 3 m.
            Assert.IsTrue(Covers(pavement, 6.82, 6.82));
            Assert.IsFalse(Covers(pavement, 8.5, 8.5));
            Assert.IsFalse(Covers(pavement, 5.2, 5.2));
            Assert.IsTrue(road.Triangles.Count > 0 && pavement.Triangles.Count > 0);

            // A lane from every way in to every way out on another arm, each chain starting at its lane and ending at the
            // other, with blinkers for left and right turns and none straight on.
            var lanes = layout.Paths.Where(path => path.Kind == 0).ToList();
            foreach (var from in layout.Arms)
                foreach (var to in layout.Arms.Where(arm => arm != from))
                {
                    var start = Along(from, from.Length, -2);
                    var end = Along(to, to.Length, 2);
                    Assert.IsTrue(lanes.Any(path => Math.Abs(path.X - start[0]) < 1e-6 && Math.Abs(path.Y - start[1]) < 1e-6),
                        "no lane from " + from.HeadingDegrees + " to " + to.HeadingDegrees);
                    Assert.IsTrue(lanes.Any(path => EndsAt(path, end[0], end[1])), "no lane into " + to.HeadingDegrees + " from " + from.HeadingDegrees);
                }
            CollectionAssert.IsSubsetOf(new[] { 0, 2, 3 }, lanes.Select(path => path.Blinker).Distinct().ToArray());
            // Footpaths round all three corners, walked both ways.
            Assert.IsTrue(layout.Paths.Where(path => path.Kind == 1).All(path => path.Direction == 2));
            Assert.GreaterOrEqual(layout.Paths.Count(path => path.Kind == 1), 3);
        }

        [Test]
        public void ACobbledCarriagewayKeepsItsTextureLaidAsTheProfileLaysIt()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile.Replace("asphalt.bmp", "cobbles.bmp")), ProfilePath);
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile });

            var layout = OmsiJunctionLayout.Build(arms, @"Splines\Test\texture\plain.bmp");

            var road = layout.Meshes.Single(mesh => mesh.Texture == "cobbles.bmp");
            Assert.IsFalse(layout.Textures.ContainsKey("plain.bmp"));
            // The profile lays u from 0 at its left kerb to 1 at its right one and v at 0.1 per metre. The carriageway follows
            // the road going straight through, the arm heading right (90°): its end at its left kerb, 12 m right and 4 m
            // ahead, has u 0 and v 1.2.
            var found = false;
            for (var index = 0; index < road.Positions.Count / 3; index++)
                if (Math.Abs(road.Positions[index * 3] - 12.0) < 1e-6 && Math.Abs(road.Positions[index * 3 + 2] - 4.0) < 1e-6)
                {
                    Assert.AreEqual(0.0, road.Uvs[index * 2], 1e-9);
                    Assert.AreEqual(1.2, road.Uvs[index * 2 + 1], 1e-9);
                    found = true;
                }
            Assert.IsTrue(found);
        }

        // A narrower cobbled street: a 6 m carriageway of cobbles, pavements 2 m wide and 0.15 m high, a lane each way.
        private const string Cobbled =
            "[texture]\r\nside.bmp\r\n[texture]\r\ncobbles.bmp\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-5\r\n0.15\r\n0\r\n0.2\r\n[profilepnt]\r\n-3\r\n0.15\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-3\r\n0.15\r\n0\r\n0.2\r\n[profilepnt]\r\n-3\r\n0.1\r\n1\r\n0.2\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-3\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n3\r\n0.1\r\n1\r\n0.1\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n3\r\n0.1\r\n0\r\n0.2\r\n[profilepnt]\r\n3\r\n0.15\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n3\r\n0.15\r\n0\r\n0.2\r\n[profilepnt]\r\n5\r\n0.15\r\n1\r\n0.2\r\n" +
            "[path]\r\n0\r\n-1.5\r\n0.1\r\n3\r\n1\r\n[path]\r\n0\r\n1.5\r\n0.1\r\n3\r\n0\r\n";

        [Test]
        public void AJunctionOfTwoRoadsKeepsEachSurfaceAndBlendsThePavementsRoundTheCorners()
        {
            var road = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var street = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Cobbled), @"Splines\Test\street.sli");
            var arms = new[]
            {
                new OmsiJunctionArm { HeadingDegrees = 90, ProfilePath = ProfilePath, Profile = road, Main = true },
                new OmsiJunctionArm { HeadingDegrees = 270, ProfilePath = ProfilePath, Profile = road, Main = true },
                new OmsiJunctionArm { HeadingDegrees = 0, ProfilePath = @"Splines\Test\street.sli", Profile = street }
            };

            var layout = OmsiJunctionLayout.Build(arms, @"Splines\Test\texture\plain.bmp");

            // The road's asphalt runs across up to its kerb 4 m north; the street keeps its cobbles from there.
            var asphalt = layout.Meshes.Single(mesh => mesh.Texture == "plain.bmp");
            var cobbles = layout.Meshes.Single(mesh => mesh.Texture == "cobbles.bmp");
            Assert.IsTrue(Covers(asphalt, 0, 0) && Covers(asphalt, 0, 3.5));
            Assert.IsFalse(Covers(asphalt, 0, 4.5));
            Assert.IsTrue(Covers(cobbles, 0, 4.5) && Covers(cobbles, 0, 9));
            Assert.IsFalse(Covers(cobbles, 0, 3.5));
            // The street's end has its own cross-section: cobbles 3 m either side, its pavement 0.15 m high.
            var side = layout.Arms.Single(arm => arm.HeadingDegrees == 0);
            Assert.IsTrue(HasVertex(cobbles, 0, side.Length, -3, 0.1) && HasVertex(cobbles, 0, side.Length, 3, 0.1));
            var pavement = layout.Meshes.First(mesh => mesh.Texture == "side.bmp");
            Assert.IsTrue(HasVertex(pavement, 0, side.Length, 5, 0.15));
            Assert.IsTrue(HasVertex(pavement, 0, side.Length, -5, 0.15));
            var east = layout.Arms.Single(arm => arm.HeadingDegrees == 90);
            Assert.IsTrue(HasVertex(pavement, east.Length, 7, 0, 0.25) || HasVertex(pavement, east.Length, -7, 0, 0.25));
            // Each arm's lanes come from its own profile.
            var lanes = layout.Paths.Where(path => path.Kind == 0).ToList();
            var into = Along(side, side.Length, -1.5);
            Assert.IsTrue(lanes.Any(path => Math.Abs(path.X - into[0]) < 1e-6 && Math.Abs(path.Y - into[1]) < 1e-6));
        }

        [Test]
        public void AGeneratedJunctionReadsBackWithAConnectionPointAtTheEndOfEachArm()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var arms = new[] { 0.0, 90.0, 180.0, 270.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile });

            var junction = OmsiJunctionGenerator.Junction(arms, null, "Crossroads");

            var sco = OmsiSceneryObject.Parse(OmsiTextFile.Encode(junction.Sco, OmsiTextEncoding.Windows1252));
            Assert.AreEqual(4, sco.SplineHelpers.Count);
            foreach (var helper in sco.SplineHelpers)
            {
                var radians = helper.HeadingDegrees * Math.PI / 180.0;
                Assert.AreEqual(12.0, Math.Sqrt(helper.X * helper.X + helper.Y * helper.Y), 1e-6);
                Assert.AreEqual(Math.Sin(radians) * 12.0, helper.X, 1e-6);
                Assert.AreEqual(Math.Cos(radians) * 12.0, helper.Y, 1e-6);
            }
            Assert.Greater(sco.Paths.Count, 12);
            var mesh = OmsiXMesh.Parse(Encoding.UTF8.GetBytes(junction.Model));
            // Without a plain texture the carriageway takes the profile's own.
            CollectionAssert.Contains(mesh.Materials.Select(material => material.Texture).ToArray(), "asphalt.bmp");
            for (var index = 0; index < mesh.Positions.Count; index++)
                if (Math.Abs(mesh.Positions[index][1] - 0.1) < 1e-4 && Math.Abs(mesh.Positions[index][0]) < 3.9 && Math.Abs(mesh.Positions[index][2]) < 3.9)
                    Assert.Greater(mesh.Normals[index][1], 0.99f);
        }

        private static double[] Along(OmsiJunctionArm arm, double along, double right)
        {
            var radians = arm.HeadingDegrees * Math.PI / 180.0;
            return new[] { Math.Sin(radians) * along + Math.Cos(radians) * right, Math.Cos(radians) * along - Math.Sin(radians) * right };
        }

        private static bool EndsAt(OmsiJunctionPath path, double x, double y)
        {
            var piece = new OmsiSplinePiece { StartX = path.X, StartY = path.Y, HeadingDegrees = path.HeadingDegrees, LengthMeters = path.LengthMeters, RadiusMeters = path.RadiusMeters };
            piece.PointAt(path.LengthMeters, out var endX, out var endY, out _);
            return Math.Abs(endX - x) < 1e-4 && Math.Abs(endY - y) < 1e-4;
        }

        private static bool HasVertex(OmsiGeneratedMesh mesh, double along, double alongY, double right, double height)
        {
            // Points are given as (x, forward); "along" lists x, "alongY" forward, "right" an extra x offset.
            for (var index = 0; index < mesh.Positions.Count; index += 3)
                if (Math.Abs(mesh.Positions[index] - (along + right)) < 1e-6 && Math.Abs(mesh.Positions[index + 2] - alongY) < 1e-6 &&
                    Math.Abs(mesh.Positions[index + 1] - height) < 1e-6)
                    return true;
            return false;
        }

        private static bool Covers(OmsiGeneratedMesh mesh, double x, double y)
        {
            for (var index = 0; index < mesh.Triangles.Count; index += 3)
            {
                double[] P(int vertex) => new[] { mesh.Positions[vertex * 3], mesh.Positions[vertex * 3 + 2] };
                var a = P(mesh.Triangles[index]);
                var b = P(mesh.Triangles[index + 1]);
                var c = P(mesh.Triangles[index + 2]);
                double Side(double[] u, double[] v) => (v[0] - u[0]) * (y - u[1]) - (v[1] - u[1]) * (x - u[0]);
                var d1 = Side(a, b);
                var d2 = Side(b, c);
                var d3 = Side(c, a);
                if ((d1 >= -1e-9 && d2 >= -1e-9 && d3 >= -1e-9) || (d1 <= 1e-9 && d2 <= 1e-9 && d3 <= 1e-9))
                    return true;
            }
            return false;
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
