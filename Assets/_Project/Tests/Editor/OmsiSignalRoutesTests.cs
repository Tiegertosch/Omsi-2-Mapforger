using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSignalRoutesTests
    {
        private const string Head = "-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.2.032\r\nDate: 05.10.2015\r\n\r\n";

        // Route 0: signal 12 with aspect 2 over the spline 10 and branch 1 of the switch 11 on tile 0 and the spline 20 on tile 1, with irregular blank
        // lines as OMSI writes them. Route 1: the same signal, aspect 1, over branch 0 of the switch.
        private const string TwoRoutes = Head +
            "   0:\r\n[signalroute]\r\n1\r\n\r\n[signal]\r\n12\r\n2\r\n\r\n" +
            "[entry]\r\n10\r\n0\r\n0\r\n0\r\n\r\n[entry]\r\n11\r\n1\r\n0\r\n2\r\n\r\n[entry]\r\n20\r\n0\r\n1\r\n0\r\n\r\n\r\n" +
            "[distsignal]\r\n12\r\n\r\n\r\n[nextsignal]\r\n13\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n\r\n" +
            "   1:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n12\r\n1\r\n\r\n[entry]\r\n11\r\n0\r\n0\r\n1\r\n\r\n\r\n\r\n";

        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-signalroutes-" + Guid.NewGuid().ToString("N"), "Signaltown");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            // Tile 0: the spline 10 (one path), the switch 11 (two paths) and the signals 12 and 13 (none), so the paths of the tile are 10/0, 11/0, 11/1.
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\rail.sli", "10", "0", "0", "100", "50", "0", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\switch.sco", "11", "100", "100", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "12", "120", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "13", "200", "90", "0", "0", "0", "0", "0", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\rail.sli", "20", "0", "0", "0", "50", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(mapFolder).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void TheFileWritesBackByteForByte()
        {
            var bytes = Encoding.ASCII.GetBytes(TwoRoutes);

            var routes = OmsiSignalRoutes.Parse(bytes);

            Assert.IsTrue(routes.File.IsExactRoundTrip);
            CollectionAssert.AreEqual(bytes, routes.File.ToBytes());
            // A header only, as maps made here have, and a file with bare line feeds and no final line ending.
            foreach (var text in new[] { Head, "Signal Routes File\n\n   0:\n[signalroute]\n0\n[signal]\n5\n1" })
                CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(text), OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes(text)).File.ToBytes());
        }

        [Test]
        public void EveryFieldReads()
        {
            var routes = OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes(TwoRoutes));

            CollectionAssert.IsEmpty(routes.Problems);
            Assert.AreEqual(2, routes.Routes.Count);
            var first = routes.Routes[0];
            Assert.AreEqual(0, first.Index);
            Assert.AreEqual(0, first.Number);
            Assert.AreEqual(1, first.Kind);
            Assert.AreEqual(12, first.SignalId);
            Assert.AreEqual(2, first.Aspect);
            Assert.AreEqual(new[] { 10, 11, 20 }, first.Entries.Select(entry => entry.ElementId).ToArray());
            Assert.AreEqual(new[] { 0, 1, 0 }, first.Entries.Select(entry => entry.Path).ToArray());
            Assert.AreEqual(new[] { 0, 0, 1 }, first.Entries.Select(entry => entry.TileIndex).ToArray());
            Assert.AreEqual(new[] { 0, 2, 0 }, first.Entries.Select(entry => entry.TilePath).ToArray());
            Assert.IsTrue(first.Entries.All(entry => entry.IsValid));
            Assert.AreEqual(12, first.DistantSignalId);
            Assert.AreEqual(13, first.NextSignalId);
            Assert.AreEqual(40.0, first.SpeedLimit);
            // The records of the route, for editing later.
            Assert.AreEqual(new[] { "signalroute", "signal", "entry", "entry", "entry", "distsignal", "nextsignal", "speedlimit" },
                first.Records.Select(record => record.Name).ToArray());
            Assert.AreSame(first.Records[1], first.SignalRecord);
            Assert.AreSame(first.Records[3], first.Entries[1].Record);
            Assert.AreSame(first.Records[7], first.SpeedLimitRecord);

            var second = routes.Routes[1];
            Assert.AreEqual(1, second.Number);
            Assert.AreEqual(0, second.Kind);
            Assert.AreEqual(1, second.Aspect);
            Assert.IsNull(second.DistantSignalId);
            Assert.IsNull(second.NextSignalId);
            Assert.IsNull(second.SpeedLimit);
            Assert.IsNull(second.SpeedLimitRecord);
            Assert.AreEqual(new[] { 12 }, routes.SignalIds().ToArray());
            Assert.AreEqual(2, routes.RoutesOfSignal(12).Count);
            CollectionAssert.IsEmpty(routes.RoutesOfSignal(13));
        }

        [Test]
        public void AMissingOrEmptyFileHasNoRoutes()
        {
            var missing = OmsiSignalRoutes.Read(new OmsiFolderSource(mapFolder));
            Assert.IsNull(missing.File);
            CollectionAssert.IsEmpty(missing.Routes);
            CollectionAssert.IsEmpty(missing.Problems);

            foreach (var text in new[] { string.Empty, Head })
            {
                var routes = OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes(text));
                CollectionAssert.IsEmpty(routes.Routes);
                CollectionAssert.IsEmpty(routes.Problems);
            }

            File.WriteAllText(Path.Combine(mapFolder, "signalroutes.cfg"), TwoRoutes);
            Assert.AreEqual(2, OmsiSignalRoutes.Read(new OmsiFolderSource(mapFolder)).Routes.Count);
        }

        [Test]
        public void BrokenLinesBecomeProblemsAndStayInTheFile()
        {
            var text = Head +
                "[entry]\r\n1\r\n0\r\n0\r\n0\r\n\r\n" +
                "   0:\r\n[signalroute]\r\n0\r\n[signal]\r\nabc\r\n1\r\n[signal]\r\n7\r\n1\r\n[entry]\r\n10\r\nx\r\n0\r\n0\r\n[speedlimit]\r\nfast\r\n[other]\r\nkept\r\n" +
                "   1:\r\n[signalroute]\r\n0\r\n[entry]\r\n10\r\n0\r\n";
            var bytes = Encoding.ASCII.GetBytes(text);

            var routes = OmsiSignalRoutes.Parse(bytes);

            CollectionAssert.AreEqual(bytes, routes.File.ToBytes());
            Assert.AreEqual(2, routes.Routes.Count);
            var kinds = routes.Problems.Select(problem => problem.Kind).ToList();
            CollectionAssert.AreEquivalent(new[]
            {
                OmsiSignalRouteProblemKind.OutsideRoute, OmsiSignalRouteProblemKind.InvalidValue, OmsiSignalRouteProblemKind.DuplicateSection,
                OmsiSignalRouteProblemKind.InvalidValue, OmsiSignalRouteProblemKind.InvalidValue, OmsiSignalRouteProblemKind.Incomplete,

                OmsiSignalRouteProblemKind.MissingSignal
            }, kinds, string.Join("; ", routes.Problems));
            Assert.AreEqual(1, routes.Problems.Single(problem => problem.Kind == OmsiSignalRouteProblemKind.MissingSignal).RouteIndex);
            var first = routes.Routes[0];
            Assert.IsNull(first.SignalId);
            Assert.AreEqual(1, first.Aspect);
            Assert.IsFalse(first.Entries[0].IsValid);
            Assert.IsNull(first.SpeedLimit);
            Assert.IsNotNull(first.SpeedLimitRecord);
            // The unknown section stays with the route it stands in.
            Assert.AreEqual("other", first.Records.Last().Name);
            Assert.AreEqual(0, routes.Problems.Single(problem => problem.Kind == OmsiSignalRouteProblemKind.DuplicateSection).RouteIndex);
            var incomplete = routes.Problems.Single(problem => problem.Kind == OmsiSignalRouteProblemKind.Incomplete);
            Assert.AreEqual(1, incomplete.RouteIndex);
            Assert.IsFalse(routes.Routes[1].Entries[0].IsValid);
        }

        [Test]
        public void RoutesFindTheirSignalsAndTrackOnTheMap()
        {
            File.WriteAllText(Path.Combine(mapFolder, "signalroutes.cfg"), TwoRoutes);
            var source = new OmsiFolderSource(mapFolder);
            var summary = OmsiMapReader.Read(source);

            var map = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(source), summary, source, PathCount);

            CollectionAssert.IsEmpty(map.Problems, string.Join("; ", map.Problems));
            Assert.AreEqual(2, map.Resolved.Count);
            Assert.AreEqual(1, map.Signals.Count);
            var signal = map.Signals[0];
            Assert.AreEqual(12, signal.Id);
            Assert.AreEqual(OmsiMapElementKind.Object, signal.Kind);
            Assert.AreEqual(0, signal.TileIndex);
            Assert.AreEqual(120.0, signal.World.X, 1e-9);
            Assert.AreEqual(90.0, signal.World.Y, 1e-9);

            var route = map.Resolved[0];
            Assert.AreSame(signal, route.Signal);
            Assert.AreSame(signal, route.DistantSignal);
            Assert.AreEqual(13, route.NextSignal.Id);
            Assert.AreEqual(new[] { 0, 1 }, route.TileIndices.ToArray());
            Assert.IsTrue(route.Entries.All(entry => entry.IsConsistent));
            Assert.AreEqual(new int?[] { 0, 2, 0 }, route.Entries.Select(entry => entry.ExpectedTilePath).ToArray());
            Assert.AreEqual(OmsiMapElementKind.Spline, route.Entries[0].Element.Kind);
            Assert.IsNotNull(route.Entries[0].Element.Spline);
            Assert.AreEqual(OmsiMapElementKind.Object, route.Entries[1].Element.Kind);
            // The middle of spline 20, which runs 50 m east from the west edge of tile 1.
            Assert.AreEqual(325.0, route.Entries[2].Element.World.X, 1e-6);
            Assert.AreEqual(1, route.Entries[2].Element.TileX);
        }

        [Test]
        public void IdsAndNumbersThatDoNotMatchTheMapAreProblems()
        {
            File.WriteAllText(Path.Combine(mapFolder, "signalroutes.cfg"), Head +
                "   0:\r\n[signalroute]\r\n0\r\n[signal]\r\n99\r\n1\r\n" +
                // Unknown element, element on another tile, path number stale, path beyond the switch's two, tile the map does not have.
                "[entry]\r\n98\r\n0\r\n0\r\n0\r\n[entry]\r\n20\r\n0\r\n0\r\n0\r\n[entry]\r\n11\r\n1\r\n0\r\n5\r\n[entry]\r\n11\r\n2\r\n0\r\n3\r\n[entry]\r\n10\r\n0\r\n9\r\n0\r\n" +
                "[distsignal]\r\n97\r\n[nextsignal]\r\n96\r\n");
            var source = new OmsiFolderSource(mapFolder);

            var map = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(source), OmsiMapReader.Read(source), source, PathCount);

            Assert.AreEqual(new[]
            {
                OmsiSignalRouteProblemKind.UnknownSignal, OmsiSignalRouteProblemKind.UnknownDistantSignal, OmsiSignalRouteProblemKind.UnknownNextSignal,
                OmsiSignalRouteProblemKind.UnknownElement, OmsiSignalRouteProblemKind.ElementOnOtherTile, OmsiSignalRouteProblemKind.PathNumberMismatch,
                OmsiSignalRouteProblemKind.PathOutOfRange, OmsiSignalRouteProblemKind.UnknownTile
            }, map.Problems.Select(problem => problem.Kind).ToArray(), string.Join("; ", map.Problems));
            Assert.AreEqual(new[] { 0, 1, 2, 3, 4 }, map.Problems.Skip(3).Select(problem => problem.EntryIndex).ToArray());
            CollectionAssert.IsEmpty(map.Signals);
            var entries = map.Resolved[0].Entries;
            Assert.IsNull(entries[0].Element);
            // The element on another tile is still found, so a view can show where it is.
            Assert.AreEqual(1, entries[1].Element.TileIndex);
            Assert.AreEqual(2, entries[2].ExpectedTilePath);
            Assert.IsTrue(entries.All(entry => !entry.IsConsistent));
            StringAssert.Contains("the tile gives it 2", map.Problems[5].Message);

            // Without the installation the path checks are left out.
            var withoutChecks = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(source), OmsiMapReader.Read(source), null, null);
            Assert.IsFalse(withoutChecks.Problems.Any(problem => problem.Kind == OmsiSignalRouteProblemKind.PathNumberMismatch ||
                                                            problem.Kind == OmsiSignalRouteProblemKind.PathOutOfRange));
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
