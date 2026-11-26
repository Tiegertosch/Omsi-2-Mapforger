using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The list of signal routes and the geometry of one route (ADR 105, M1), on the small map of OmsiSignalRoutesTests.</summary>
    public sealed class OmsiSignalRouteListTests
    {
        private const string Head = "Signal Routes File\r\n\r\n";

        // Route "   4:" has signal 12 (an object), the spline 10, switch branch 1 of object 11 and the spline 20; route "   7:" is a branch of the switch only.
        // Route 3 (no number): signal 99 is not on the map, entry 98 is not either.
        private const string Routes = Head +
            "   4:\r\n[signalroute]\r\n1\r\n\r\n[signal]\r\n12\r\n2\r\n\r\n" +
            "[entry]\r\n10\r\n0\r\n0\r\n0\r\n\r\n[entry]\r\n11\r\n1\r\n0\r\n2\r\n\r\n[entry]\r\n20\r\n0\r\n1\r\n0\r\n\r\n" +
            "[distsignal]\r\n12\r\n\r\n[nextsignal]\r\n13\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n" +
            "   7:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n13\r\n1\r\n\r\n[entry]\r\n11\r\n0\r\n0\r\n1\r\n\r\n" +
            "[signalroute]\r\n0\r\n[signal]\r\n99\r\n1\r\n[entry]\r\n98\r\n0\r\n0\r\n0\r\n";

        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-signalroutelist-" + Guid.NewGuid().ToString("N"), "Signaltown");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\rail.sli", "10", "0", "0", "100", "50", "0", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\switch.sco", "11", "100", "100", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "12", "120", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "13", "200", "90", "0", "0", "0", "0", "0", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\rail.sli", "20", "0", "0", "0", "50", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "");
            File.WriteAllText(Path.Combine(mapFolder, "signalroutes.cfg"), Routes);
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(mapFolder).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private OmsiSignalRouteMap Resolve(out OmsiMapSummary summary)
        {
            var source = new OmsiFolderSource(mapFolder);
            summary = OmsiMapReader.Read(source);
            return OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(source), summary, source, PathCount);
        }

        [Test]
        public void RowsFollowTheFileAndCarryWhatThePopupShows()
        {
            var map = Resolve(out _);

            var rows = OmsiSignalRouteList.Rows(map);

            Assert.AreEqual(3, rows.Count);
            // The number of the file's own comment, and the place counted from 1 where there is none.
            Assert.AreEqual(new[] { 4, 7, 3 }, rows.Select(row => row.Number).ToArray());
            Assert.AreEqual(new[] { 0, 1, 2 }, rows.Select(row => row.RouteIndex).ToArray());
            var first = rows[0];
            Assert.AreEqual(12, first.SignalId);
            Assert.AreEqual("signal.sco", first.SignalFile);
            Assert.AreEqual(2, first.Aspect);
            Assert.AreEqual(3, first.PieceCount);
            Assert.AreEqual(40.0, first.SpeedLimit);
            Assert.AreEqual(12, first.DistantSignalId);
            Assert.AreEqual(13, first.NextSignalId);
            Assert.IsFalse(first.HasProblems);
            Assert.IsNull(rows[1].SpeedLimit);
            Assert.IsNull(rows[1].DistantSignalId);
        }

        [Test]
        public void ARouteListsItsFindingsAndTheFileKeepsTheOnesThatBelongToNoRoute()
        {
            var map = Resolve(out _);

            var rows = OmsiSignalRouteList.Rows(map);

            CollectionAssert.IsEmpty(rows[0].Problems);
            CollectionAssert.IsEmpty(rows[1].Problems);
            // Route 3: signal 99 and element 98 are not on the map.
            Assert.IsNull(rows[2].SignalFile);
            Assert.AreEqual(2, rows[2].Problems.Count, string.Join("; ", rows[2].Problems));
            StringAssert.Contains("99", rows[2].Problems[0]);
            StringAssert.Contains("98", rows[2].Problems[1]);
            Assert.IsTrue(rows[2].HasProblems);
            Assert.AreEqual(1, OmsiSignalRouteList.RoutesWithProblems(map));
            CollectionAssert.IsEmpty(OmsiSignalRouteList.FileProblems(map));

            // A section before the first route belongs to no route.
            File.WriteAllText(Path.Combine(mapFolder, "signalroutes.cfg"), Head + "[entry]\r\n1\r\n0\r\n0\r\n0\r\n\r\n" + Routes.Substring(Head.Length));
            var withStray = Resolve(out _);
            Assert.AreEqual(1, OmsiSignalRouteList.FileProblems(withStray).Count);
            Assert.AreEqual(3, OmsiSignalRouteList.Rows(withStray).Count);
            Assert.AreEqual(1, OmsiSignalRouteList.RoutesWithProblems(withStray));
        }

        [Test]
        public void TheListCanBeLimitedToOneSignal()
        {
            var map = Resolve(out _);

            Assert.AreEqual(new[] { 4 }, OmsiSignalRouteList.Rows(map, 12).Select(row => row.Number).ToArray());
            Assert.AreEqual(new[] { 7 }, OmsiSignalRouteList.Rows(map, 13).Select(row => row.Number).ToArray());
            CollectionAssert.IsEmpty(OmsiSignalRouteList.Rows(map, 11));
        }

        [Test]
        public void AMapWithoutTheFileHasAnEmptyList()
        {
            File.Delete(Path.Combine(mapFolder, "signalroutes.cfg"));

            var map = Resolve(out _);

            CollectionAssert.IsEmpty(OmsiSignalRouteList.Rows(map));
            CollectionAssert.IsEmpty(OmsiSignalRouteList.FileProblems(map));
            CollectionAssert.IsEmpty(map.Signals);
        }

        [Test]
        public void ThePathsOfTheEntriesBecomeTheLines()
        {
            var map = Resolve(out var summary);
            var route = map.Resolved[0];
            var spline = new OmsiMapPath { TileX = 0, TileY = 0, ElementId = 10, OnSpline = true, PathIndex = 0, Points = new[] { new OmsiMapPoint(0, 0), new OmsiMapPoint(5, 0) } };
            var branch = new OmsiMapPath { TileX = 0, TileY = 0, ElementId = 11, OnSpline = false, PathIndex = 1, Points = new[] { new OmsiMapPoint(1, 1), new OmsiMapPoint(2, 2) } };
            var otherBranch = new OmsiMapPath { TileX = 0, TileY = 0, ElementId = 11, OnSpline = false, PathIndex = 0, Points = new[] { new OmsiMapPoint(9, 9), new OmsiMapPoint(8, 8) } };
            var far = new OmsiMapPath { TileX = 1, TileY = 0, ElementId = 20, OnSpline = true, PathIndex = 0, Points = new[] { new OmsiMapPoint(300, 0), new OmsiMapPoint(310, 0) } };
            var tiles = new Dictionary<int, IReadOnlyList<OmsiMapPath>>
            {
                { 0, new List<OmsiMapPath> { spline, branch, otherBranch } },
                { 1, new List<OmsiMapPath> { far } }
            };

            var shape = OmsiSignalRouteList.Shape(route, summary, index => tiles.TryGetValue(index, out var list) ? list : null);

            // Only the branch the route names, not the switch's other one.
            Assert.AreEqual(3, shape.Lines.Count);
            Assert.AreSame(spline.Points, shape.Lines[0]);
            Assert.AreSame(branch.Points, shape.Lines[1]);
            Assert.AreSame(far.Points, shape.Lines[2]);
            CollectionAssert.IsEmpty(shape.Points);
            Assert.AreEqual(0, shape.Missing);
            Assert.AreEqual(new[] { 0, 1 }, OmsiSignalRouteList.TilesOf(route).ToArray());
        }

        [Test]
        public void WithoutPathsASplineShowsItsCentreLineAndAnObjectItsPlace()
        {
            var map = Resolve(out var summary);

            var shape = OmsiSignalRouteList.Shape(map.Resolved[0], summary, null);

            // Splines 10 and 20 are drawn from their curve, the switch as a point at its origin.
            Assert.AreEqual(2, shape.Lines.Count);
            Assert.GreaterOrEqual(shape.Lines[0].Length, 2);
            Assert.AreEqual(100.0, shape.Lines[0][0].X, 1e-6);
            Assert.AreEqual(1, shape.Points.Count);
            Assert.AreEqual(100.0, shape.Points[0].X, 1e-9);
            Assert.AreEqual(100.0, shape.Points[0].Y, 1e-9);
            Assert.IsFalse(shape.IsEmpty);
        }

        [Test]
        public void EntriesTheMapDoesNotHaveAreCountedAndTheSameBranchIsDrawnOnce()
        {
            var map = Resolve(out var summary);

            var missing = OmsiSignalRouteList.Shape(map.Resolved[2], summary, null);

            Assert.AreEqual(1, missing.Missing);
            Assert.IsTrue(missing.IsEmpty);

            // A switch listed twice for the same branch.
            File.WriteAllText(Path.Combine(mapFolder, "signalroutes.cfg"), Head +
                "[signalroute]\r\n0\r\n[signal]\r\n12\r\n1\r\n[entry]\r\n11\r\n0\r\n0\r\n1\r\n[entry]\r\n11\r\n0\r\n0\r\n1\r\n");
            var twice = Resolve(out summary);
            Assert.AreEqual(1, OmsiSignalRouteList.Shape(twice.Resolved[0], summary, null).Points.Count);
        }

        [Test]
        public void TheBoxTakesInTheTrackAndTheSignals()
        {
            var map = Resolve(out var summary);
            var shape = OmsiSignalRouteList.Shape(map.Resolved[0], summary, null);
            var places = new[] { map.Resolved[0].Signal.World, map.Resolved[0].NextSignal.World };

            Assert.IsTrue(OmsiSignalRouteList.Bounds(shape, places, out var min, out var max));

            // The signal 13 at x 200 is the east end, the spline 20 in tile 1 (x 300 and more) goes further.
            Assert.AreEqual(100.0, min.X, 1e-6);
            Assert.Greater(max.X, 300.0);
            Assert.GreaterOrEqual(max.Y, 90.0);
            Assert.IsFalse(OmsiSignalRouteList.Bounds(null, null, out _, out _));
            Assert.IsTrue(OmsiSignalRouteList.Bounds(null, new[] { new OmsiMapPoint(3, 4) }, out min, out max));
            Assert.AreEqual(3.0, min.X);
            Assert.AreEqual(4.0, max.Y);
        }

        private static int? PathCount(string asset)
        {
            if (asset.EndsWith("rail.sli", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (asset.EndsWith("switch.sco", StringComparison.OrdinalIgnoreCase))
                return 2;
            return asset.EndsWith("signal.sco", StringComparison.OrdinalIgnoreCase) ? 0 : (int?)null;
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
