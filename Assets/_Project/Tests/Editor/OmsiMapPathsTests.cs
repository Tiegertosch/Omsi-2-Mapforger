using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiMapPathsTests
    {
        // A two-way road: lane 0 runs along the spline 2 m to its right, lane 1 against it 2 m to its left.
        private static readonly OmsiSplineProfile Road = OmsiSplineProfile.Parse(
            Encoding.UTF8.GetBytes("[path]\r\n0\r\n2\r\n0\r\n3\r\n0\r\n[path]\r\n0\r\n-2\r\n0\r\n3\r\n1\r\n"), @"Splines\road.sli");

        // A crossing with one path 10 m straight ahead, 3 m wide, one way, with the right indicator.
        private static readonly OmsiSceneryObject Crossing = OmsiSceneryObject.Parse(
            Encoding.UTF8.GetBytes("[path]\r\n0\r\n0\r\n0\r\n0\r\n0\r\n10\r\n0\r\n0\r\n0\r\n3\r\n0\r\n3\r\n"));

        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-paths-" + Guid.NewGuid().ToString("N"), "Testmap");
            Directory.CreateDirectory(mapFolder);
            WriteTile("global.cfg", "[map]", "0", "0", "tile_0_0.map", "");
            // Splines 30 and 31 run east one after the other from 0, 0; spline 32 runs east from 0, 20, mirrored (x, height, y).
            WriteTile("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "30", "0", "31", "0", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "priority", "192", "0", "",
                "[spline]", "0", @"Splines\road.sli", "31", "30", "0", "50", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "32", "0", "0", "0", "0", "20", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "mirror", "",
                "[object]", "0", @"Sceneryobjects\crossing.sco", "12", "150", "150", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "speedlimit", "30", "0", "",
                "[kill_rule]", "0", "speedlimit", "30", "0", "",
                "[rule]", "0", "trafficdensity", "0.5", "2", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(mapFolder).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void PathsLieBesideSplinesAndInObjectsWithTheirRules()
        {
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var tile = summary.Tiles[0];

            var rules = tile.Splines.Single(spline => spline.Id == 30).Rules;
            Assert.AreEqual("priority", rules.Single().Name);
            Assert.AreEqual(192.0, rules.Single().Value);
            Assert.AreEqual(3, tile.Objects.Single().Rules.Count);
            Assert.IsEmpty(tile.Splines.Single(spline => spline.Id == 31).Rules);

            var paths = OmsiMapPaths.OfTile(summary, tile, asset => asset.EndsWith("crossing.sco", StringComparison.OrdinalIgnoreCase) ? Crossing : null,
                asset => asset.EndsWith("road.sli", StringComparison.OrdinalIgnoreCase) ? Road : null);

            Assert.AreEqual(7, paths.Count);
            var right = paths.Single(path => path.ElementId == 30 && path.PathIndex == 0);
            Assert.IsTrue(right.OnSpline);
            Assert.AreEqual(0, right.Direction);
            Assert.AreEqual(0.0, right.Points[0].X, 1e-9);
            Assert.AreEqual(-2.0, right.Points[0].Y, 1e-9);
            Assert.AreEqual(50.0, right.Points.Last().X, 1e-9);
            Assert.AreEqual(192.0, right.Rule("priority"));
            Assert.IsNull(paths.Single(path => path.ElementId == 30 && path.PathIndex == 1).Rule("priority"));

            // Mirrored: lane 0 lies on the left and runs against the spline.
            var mirrored = paths.Single(path => path.ElementId == 32 && path.PathIndex == 0);
            Assert.AreEqual(22.0, mirrored.Points[0].Y, 1e-9);
            Assert.AreEqual(1, mirrored.Direction);

            var crossing = paths.Single(path => path.ElementId == 12);
            Assert.IsFalse(crossing.OnSpline);
            Assert.AreEqual(3, crossing.Blinker);
            Assert.AreEqual(150.0, crossing.Points[0].Y, 1e-9);
            Assert.AreEqual(160.0, crossing.Points.Last().Y, 1e-9);
            // The speed limit is taken away again; the density stays.
            Assert.IsNull(crossing.Rule("speedlimit"));
            Assert.AreEqual(0.5, crossing.Rule("trafficdensity"));
        }

        [Test]
        public void EndsNoOtherPathJoinsAreOpen()
        {
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var paths = OmsiMapPaths.OfTile(summary, summary.Tiles[0], asset => asset.EndsWith("crossing.sco", StringComparison.OrdinalIgnoreCase) ? Crossing : null,
                asset => asset.EndsWith("road.sli", StringComparison.OrdinalIgnoreCase) ? Road : null);

            OmsiMapPaths.FindOpenEnds(paths);

            OmsiMapPath Lane(int id, int index) => paths.Single(path => path.ElementId == id && path.PathIndex == index);
            // Eastbound: spline 30's lane goes on into spline 31's at x = 50.
            Assert.IsTrue(Lane(30, 0).OpenStart);
            Assert.IsFalse(Lane(30, 0).OpenEnd);
            Assert.IsFalse(Lane(31, 0).OpenStart);
            Assert.IsTrue(Lane(31, 0).OpenEnd);
            // Westbound the other way round: spline 31's lane feeds spline 30's at x = 50.
            Assert.IsFalse(Lane(30, 1).OpenEnd);
            Assert.IsTrue(Lane(30, 1).OpenStart);
            Assert.IsFalse(Lane(31, 1).OpenStart);
            // Nothing joins the crossing's path or the mirrored spline's.
            Assert.IsTrue(paths.Single(path => path.ElementId == 12).OpenStart);
            Assert.IsTrue(paths.Single(path => path.ElementId == 12).OpenEnd);
            Assert.IsTrue(Lane(32, 0).OpenStart && Lane(32, 0).OpenEnd);
        }

        private void WriteTile(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
