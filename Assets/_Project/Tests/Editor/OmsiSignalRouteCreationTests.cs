using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Creating signal routes as undoable steps (ADR 105, M4).</summary>
    public sealed class OmsiSignalRouteCreationTests
    {
        private const string Head = "-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.2.032\r\nDate: 05.10.2015\r\n\r\n";
        private const string NoOptional = "\r\n\r\n\r\n";

        private static string Route(int number, int kind, int signal, int aspect)
        {
            return "   " + number + ":\r\n[signalroute]\r\n" + kind + "\r\n\r\n[signal]\r\n" + signal + "\r\n" + aspect + "\r\n\r\n";
        }

        private static string Entry(int element, int path, int tile, int tilePath)
        {
            return "[entry]\r\n" + element + "\r\n" + path + "\r\n" + tile + "\r\n" + tilePath + "\r\n\r\n";
        }

        // Tile 0: the rails 10 (from (50, 0) northwards, 100 m), 20 (on from 10) and 30 (apart), then the signals 12, 13, 14 and 16 and the lamp 15.
        private static readonly string Original = Head + Route(0, 0, 12, 1) + Entry(10, 0, 0, 0) + NoOptional + Route(1, 1, 13, 1) + Entry(20, 0, 0, 1) + NoOptional;

        private string root;
        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-routecreation-" + Guid.NewGuid().ToString("N"));
            mapFolder = Path.Combine(root, "maps", "Signaltown");
            Directory.CreateDirectory(mapFolder);
            Directory.CreateDirectory(Path.Combine(root, "Splines"));
            // A rail profile both ways, as the stock rails have it.
            File.WriteAllBytes(Path.Combine(root, "Splines", "rail.sli"), Encoding.UTF8.GetBytes("[path]\r\n2\r\n0\r\n0.449\r\n0\r\n2\r\n"));
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "");
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\rail.sli", "10", "0", "20", "50", "0", "0", "0", "100", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\rail.sli", "20", "10", "0", "50", "0", "100", "0", "100", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\rail.sli", "30", "0", "0", "200", "0", "0", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "12", "120", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "13", "200", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "14", "53", "195", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\lamp.sco", "15", "260", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "16", "53", "5", "0", "0", "0", "0", "0", "");
            WriteRoutes(Original);
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void TheSearchFindsTheTrackAndTheRouteGoesAtTheEndWithTheNextNumber()
        {
            var session = OpenProject();
            var search = session.FindSignalRouteWays(16, OmsiSignalRouteTarget.Signal(14));
            Assert.IsTrue(search.Found, search.Message);
            var way = search.Ways.Single();
            CollectionAssert.AreEqual(new[] { 10, 20 }, way.Entries.Select(entry => entry.ElementId).ToArray());
            Assert.AreEqual(14, way.EndSignalId);

            var draft = new OmsiSignalRouteDraft { NextSignal = 14, SpeedLimit = 40.0 };
            draft.Entries.AddRange(way.Entries);
            var change = session.CreateSignalRoute(16, draft);

            Assert.AreEqual(OmsiSignalRouteChangeKind.Creation, change.Kind);
            Assert.AreEqual(2, change.RouteIndex);
            Assert.IsTrue(change.IsApplied);
            var created = Original + Route(2, 0, 16, 2) + Entry(10, 0, 0, 0) + Entry(20, 0, 0, 1).Replace("1\r\n\r\n", "1\r\n") +
                          "\r\n\r\n\r\n[nextsignal]\r\n14\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n\r\n";
            Assert.AreEqual(created, Text(session));
            var routes = OmsiSignalRoutes.Read(session.Workspace);
            Assert.AreEqual(3, routes.Routes.Count);
            Assert.AreEqual(2, routes.Routes[2].Number);
            Assert.AreEqual(2, routes.Routes[2].Aspect);
            AssertResolvesWithoutProblems(session);

            session.Undo(change);
            Assert.AreEqual(Original, Text(session));
            Assert.IsFalse(change.IsApplied);
            session.Redo(change);
            Assert.AreEqual(created, Text(session));
            Assert.Throws<OmsiStepRefusedException>(() => session.Redo(change));
        }

        [Test]
        public void TheOptionalSectionsAndBlankLinesFollowTheOmsiEditor()
        {
            var session = OpenProject();
            var all = Draft(10, 20);
            all.Kind = 1;
            all.DistantSignal = 12;
            all.NextSignal = 14;
            all.SpeedLimit = 60.0;
            session.CreateSignalRoute(16, all);
            StringAssert.EndsWith(Route(2, 1, 16, 2) + Entry(10, 0, 0, 0) + "[entry]\r\n20\r\n0\r\n0\r\n1\r\n" +
                                  "\r\n\r\n[distsignal]\r\n12\r\n\r\n\r\n[nextsignal]\r\n14\r\n\r\n[speedlimit]\r\n60.000\r\n\r\n\r\n", Text(session));

            // Without any, the route ends with the four blank lines the OMSI editor writes; an aspect given wins.
            var plain = Draft(10);
            plain.Aspect = 3;
            session.CreateSignalRoute(16, plain);
            StringAssert.EndsWith(Route(3, 0, 16, 3) + "[entry]\r\n10\r\n0\r\n0\r\n0\r\n" + "\r\n\r\n\r\n\r\n", Text(session));
            AssertResolvesWithoutProblems(session);
        }

        [Test]
        public void AMapWithoutTheFileGetsOneAndUndoTakesItAway()
        {
            File.Delete(Path.Combine(mapFolder, "signalroutes.cfg"));
            var session = OpenProject();
            var change = session.CreateSignalRoute(16, Draft(10, 20));

            var bytes = session.Workspace.ReadAllBytes("signalroutes.cfg");
            var text = Encoding.ASCII.GetString(bytes);
            StringAssert.StartsWith("-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nWritten by OMSI MapWorks\r\nDate: ", text);
            StringAssert.EndsWith("\r\n\r\n   0:\r\n" + "[signalroute]\r\n0\r\n\r\n[signal]\r\n16\r\n1\r\n\r\n" + Entry(10, 0, 0, 0) + "[entry]\r\n20\r\n0\r\n0\r\n1\r\n\r\n\r\n\r\n\r\n", text);
            Assert.AreNotEqual(0xFF, bytes[0]);
            Assert.AreEqual(-1, text.IndexOf("Created with OMSI MapWorks", StringComparison.Ordinal), "an export would count as a map made with MapWorks");
            Assert.AreEqual(1, OmsiSignalRoutes.Read(session.Workspace).Routes.Count);
            AssertResolvesWithoutProblems(session);

            session.Undo(change);
            Assert.IsFalse(session.Workspace.FileExists("signalroutes.cfg"));
            session.Redo(change);
            CollectionAssert.AreEqual(bytes, session.Workspace.ReadAllBytes("signalroutes.cfg"));
        }

        [Test]
        public void NumbersThatDoNotFollowThePlaceGoOnFromTheHighest()
        {
            WriteRoutes(Original.Replace("   0:", "   5:").Replace("   1:", "   7:"));
            var session = OpenProject();
            session.CreateSignalRoute(16, Draft(10));
            Assert.AreEqual(8, OmsiSignalRoutes.Read(session.Workspace).Routes[2].Number);

            // A file whose last line has no line break gets one before the new route.
            WriteRoutes(Original.TrimEnd('\r', '\n'));
            session = OpenProject("Unended");
            session.CreateSignalRoute(16, Draft(10));
            StringAssert.Contains(Entry(20, 0, 0, 1).TrimEnd('\r', '\n') + "\r\n   2:\r\n[signalroute]", Text(session));
            Assert.AreEqual(3, OmsiSignalRoutes.Read(session.Workspace).Routes.Count);
        }

        [Test]
        public void TheNewRouteProtectsItsTrackAndSignal()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var free = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 16));
            Assert.IsNotNull(free.Change, string.Join("; ", free.Blockers));
            session.Undo(summary, free.Change);

            var change = session.CreateSignalRoute(16, Draft(10, 20));
            summary = session.Read();
            var refused = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects.Single(item => item.Id == 16));
            Assert.IsNull(refused.Change);
            StringAssert.Contains("signal route 2", string.Join("; ", refused.Blockers));
            Assert.IsTrue(session.BuildReferenceIndex().To(20).Any(reference => reference.ToString().Contains("track of signal route 2")),
                string.Join("; ", session.BuildReferenceIndex().To(20)));

            session.Undo(change);
            summary = session.Read();
            Assert.IsNotNull(session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects.Single(item => item.Id == 16)).Change);
        }

        [Test]
        public void WrongRoutesAreRefusedAndTheFileStays()
        {
            var session = OpenProject();
            Assert.Throws<ArgumentNullException>(() => session.CreateSignalRoute(16, null));
            Assert.That(() => session.CreateSignalRoute(16, new OmsiSignalRouteDraft()), Throws.ArgumentException.With.Message.Contains("at least one track piece"));
            Assert.That(() => session.CreateSignalRoute(99, Draft(10)), Throws.ArgumentException.With.Message.Contains("no element 99"));
            Assert.That(() => session.CreateSignalRoute(10, Draft(10)), Throws.ArgumentException.With.Message.Contains("is a spline"));
            Assert.That(() => session.CreateSignalRoute(15, Draft(10)), Throws.ArgumentException.With.Message.Contains("is not a signal"));
            Assert.That(() => session.CreateSignalRoute(16, WithKind(-1)), Throws.ArgumentException.With.Message.Contains("kind"));
            foreach (var speed in new[] { 0.0, double.NaN, 501.0 })
            {
                var draft = Draft(10);
                draft.SpeedLimit = speed;
                Assert.That(() => session.CreateSignalRoute(16, draft), Throws.ArgumentException.With.Message.Contains("speed limit"), speed.ToString());
            }
            var badNext = Draft(10);
            badNext.NextSignal = 15;
            Assert.That(() => session.CreateSignalRoute(16, badNext), Throws.ArgumentException.With.Message.Contains("next signal"));

            Assert.That(() => session.CreateSignalRoute(16, Draft(10, 30)), Throws.ArgumentException.With.Message.Contains("does not join on to entry 0"));
            Assert.That(() => session.CreateSignalRoute(16, Draft(10, 10)), Throws.ArgumentException.With.Message.Contains("twice"));
            var wrongPath = new OmsiSignalRouteDraft();
            wrongPath.Entries.Add(new OmsiSignalRouteDraftEntry(10, 1, 0));
            Assert.That(() => session.CreateSignalRoute(16, wrongPath), Throws.ArgumentException.With.Message.Contains("has 1 path(s)"));
            var wrongTile = new OmsiSignalRouteDraft();
            wrongTile.Entries.Add(new OmsiSignalRouteDraftEntry(10, 0, 3));
            Assert.That(() => session.CreateSignalRoute(16, wrongTile), Throws.ArgumentException.With.Message.Contains("no tile 3"));
            Assert.That(() => session.CreateSignalRoute(16, Draft(12)), Throws.ArgumentException.With.Message.Contains("has 0 path(s)"));

            // A path count that cannot be known refuses the step.
            session.AssetPathCount = asset => null;
            Assert.Throws<OmsiStepRefusedException>(() => session.CreateSignalRoute(16, Draft(10)));
            session.AssetPathCount = null;
            Assert.That(() => session.CreateSignalRoute(16, Draft(10)), Throws.TypeOf<OmsiStepRefusedException>().With.Message.Contains("installation"));
            Assert.IsFalse(session.Workspace.IsModified("signalroutes.cfg"));
        }

        [Test]
        public void TheTrackOfARouteCanBeReplacedAndTheRestStays()
        {
            var session = OpenProject();
            var change = session.ReplaceSignalRouteTrack(0, Draft(10, 20).Entries);
            Assert.AreEqual(OmsiSignalRouteChangeKind.Edit, change.Kind);
            Assert.AreEqual(Head + Route(0, 0, 12, 1) + Entry(10, 0, 0, 0) + Entry(20, 0, 0, 1) + NoOptional + Route(1, 1, 13, 1) + Entry(20, 0, 0, 1) + NoOptional, Text(session));
            AssertResolvesWithoutProblems(session);

            // The last route, at the end of the file, and back to a single piece.
            session.ReplaceSignalRouteTrack(1, Draft(10, 20).Entries);
            session.ReplaceSignalRouteTrack(1, Draft(20).Entries);
            Assert.AreEqual(Head + Route(0, 0, 12, 1) + Entry(10, 0, 0, 0) + Entry(20, 0, 0, 1) + NoOptional + Route(1, 1, 13, 1) + Entry(20, 0, 0, 1) + NoOptional, Text(session));
            Assert.IsNull(session.ReplaceSignalRouteTrack(1, Draft(20).Entries));

            Assert.That(() => session.ReplaceSignalRouteTrack(0, Draft(10, 30).Entries), Throws.ArgumentException.With.Message.Contains("does not join"));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.ReplaceSignalRouteTrack(2, Draft(10).Entries));
            // Route 1 is back as it was after the first step, so that step undoes to the original bytes.
            session.Undo(change);
            Assert.AreEqual(Original, Text(session));
        }

        [Test]
        public void TheCallersRailsAreUsedForTheCheck()
        {
            var session = OpenProject();
            // Rails that join 10 straight on to 30.
            var rails = OmsiPathNetwork.Build(new[] { Rail(10, 50, 0, 50, 100), Rail(30, 50, 100, 50, 150) }, 2);
            Assert.IsNotNull(session.CreateSignalRoute(16, Draft(10, 30), null, rails));
            Assert.AreEqual(3, OmsiSignalRoutes.Read(session.Workspace).Routes.Count);
        }

        private static OmsiMapPath Rail(int id, double x0, double y0, double x1, double y1)
        {
            return new OmsiMapPath
            {
                TileX = 0, TileY = 0, ElementId = id, PathIndex = 0, OnSpline = true, Kind = 2, Direction = 2, LengthMeters = Math.Abs(y1 - y0) + Math.Abs(x1 - x0),
                Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint((x0 + x1) / 2.0, (y0 + y1) / 2.0), new OmsiMapPoint(x1, y1) }
            };
        }

        private static OmsiSignalRouteDraft Draft(params int[] elements)
        {
            var draft = new OmsiSignalRouteDraft();
            foreach (var element in elements)
                draft.Entries.Add(new OmsiSignalRouteDraftEntry(element, 0, 0));
            return draft;
        }

        private static OmsiSignalRouteDraft WithKind(int kind)
        {
            var draft = Draft(10);
            draft.Kind = kind;
            return draft;
        }

        private void AssertResolvesWithoutProblems(OmsiMapSession session)
        {
            var map = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(session.Workspace), session.Read(), session.Workspace, PathCount);
            CollectionAssert.IsEmpty(map.Problems, string.Join("; ", map.Problems));
            Assert.IsTrue(map.Resolved.SelectMany(route => route.Entries).All(entry => entry.ExpectedTilePath.HasValue));
        }

        private static string Text(OmsiMapSession session)
        {
            return Encoding.ASCII.GetString(session.Workspace.ReadAllBytes("signalroutes.cfg"));
        }

        private static int? PathCount(string asset)
        {
            return asset.EndsWith("rail.sli", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private OmsiMapSession OpenProject(string name = "Routes")
        {
            var project = Path.Combine(root, "Projects", name + ".omproj");
            var session = OmsiMapSession.OpenProject(Path.Combine(mapFolder, "global.cfg"), project);
            session.AssetPathCount = PathCount;
            session.IsSignalAsset = asset => asset.EndsWith("signal.sco", StringComparison.OrdinalIgnoreCase);
            return session;
        }

        private void WriteRoutes(string text)
        {
            File.WriteAllBytes(Path.Combine(mapFolder, "signalroutes.cfg"), Encoding.ASCII.GetBytes(text));
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
