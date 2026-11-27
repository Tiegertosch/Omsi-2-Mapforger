using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The switch marks of a route shape (ADR 105, M5): where a route over a switch object passes it, and which way the track runs there.</summary>
    public sealed class OmsiSignalRouteSwitchMarkTests
    {
        private const string Head = "Signal Routes File\r\n\r\n";

        // Two positions: path 0 straight 20 m (position 0), path 1 a curve of 20 m (position 1), path 2 a piece without a position.
        private const string SwitchObject =
            "[switch]\r\n2\r\n\r\n" +
            "[path]\r\n0\r\n0\r\n0\r\n0\r\n0\r\n20\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n\r\n[switchdir]\r\n0\r\n\r\n" +
            "[path]\r\n0\r\n0\r\n0\r\n0\r\n100\r\n20\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n\r\n[switchdir]\r\n1\r\n\r\n" +
            "[path]\r\n50\r\n0\r\n0\r\n0\r\n0\r\n5\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n";

        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-switchmark-" + Guid.NewGuid().ToString("N"), "Switchtown");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "");
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[object]", "0", @"Sceneryobjects\switch.sco", "11", "100", "150", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "12", "120", "90", "0", "0", "0", "0", "0", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(mapFolder).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private OmsiSignalRouteShape ShapeOfRouteOver(int switchPath, out OmsiMapPath path)
        {
            File.WriteAllText(Path.Combine(mapFolder, "signalroutes.cfg"), Head +
                "   1:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n12\r\n1\r\n\r\n[entry]\r\n11\r\n" + switchPath + "\r\n0\r\n1\r\n");
            var source = new OmsiFolderSource(mapFolder);
            var summary = OmsiMapReader.Read(source);
            var map = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(source), summary, source, asset =>
                asset.EndsWith("switch.sco", StringComparison.OrdinalIgnoreCase) ? 3 : (int?)0);
            var switchObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject));
            var paths = OmsiMapPaths.OfTile(summary, summary.Tiles[0], asset => asset.EndsWith("switch.sco", StringComparison.OrdinalIgnoreCase) ? switchObject : null, null);
            path = paths.Single(item => item.ElementId == 11 && item.PathIndex == switchPath);
            return OmsiSignalRouteList.Shape(map.Resolved[0], summary, index => index == 0 ? paths : null);
        }

        [Test]
        public void ARouteOverASwitchBranchMarksTheMiddleOfThatBranchWithItsPositionAndDirection()
        {
            var shape = ShapeOfRouteOver(1, out var branch);

            Assert.AreEqual(1, shape.Lines.Count);
            Assert.AreEqual(1, shape.SwitchMarks.Count);
            Assert.AreEqual(1, shape.SwitchMarkAhead.Count);
            var mark = shape.SwitchMarks[0];
            Assert.AreEqual(1, mark.Value);
            var middle = branch.Points.Length / 2;
            Assert.AreEqual(branch.Points[middle].X, mark.Key.X, 1e-9);
            Assert.AreEqual(branch.Points[middle].Y, mark.Key.Y, 1e-9);
            // The mark lies on the branch, 150 m north of the tile origin where the switch stands, and the next point gives the direction.
            Assert.Greater(mark.Key.Y, 150.0);
            Assert.Less(mark.Key.Y, 171.0);
            Assert.AreEqual(branch.Points[middle + 1].X, shape.SwitchMarkAhead[0].X, 1e-9);
            Assert.AreEqual(branch.Points[middle + 1].Y, shape.SwitchMarkAhead[0].Y, 1e-9);
            Assert.AreNotEqual(mark.Key, shape.SwitchMarkAhead[0]);
        }

        [Test]
        public void AnotherBranchHasItsOwnPositionAndABranchWithoutPositionHasNoMark()
        {
            var straight = ShapeOfRouteOver(0, out _);
            Assert.AreEqual(0, straight.SwitchMarks.Single().Value);

            var plain = ShapeOfRouteOver(2, out _);
            Assert.AreEqual(1, plain.Lines.Count);
            CollectionAssert.IsEmpty(plain.SwitchMarks);
            CollectionAssert.IsEmpty(plain.SwitchMarkAhead);
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
