using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Editing and deleting signal routes as undoable steps (ADR 105, M3).</summary>
    public sealed class OmsiSignalRouteEditingTests
    {
        private const string Head = "-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.2.032\r\nDate: 05.10.2015\r\n\r\n";

        // Tile 0: the spline 10 (path 0), the switch 11 (paths 1 and 2), the signals 12, 13 and 14 and the lamp 15 (no signal).
        private static string Route(int number, int kind, int signal, int aspect)
        {
            return "   " + number + ":\r\n[signalroute]\r\n" + kind + "\r\n\r\n[signal]\r\n" + signal + "\r\n" + aspect + "\r\n\r\n";
        }

        private static string Entry(int element, int path, int tile, int tilePath)
        {
            return "[entry]\r\n" + element + "\r\n" + path + "\r\n" + tile + "\r\n" + tilePath + "\r\n\r\n";
        }

        private const string NoOptional = "\r\n\r\n\r\n";
        private const string AllOptional = "\r\n[distsignal]\r\n14\r\n\r\n\r\n[nextsignal]\r\n12\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n\r\n";

        private static readonly string Body0 = Route(0, 0, 12, 1) + Entry(10, 0, 0, 0);
        private static readonly string Body1 = Route(1, 1, 13, 2) + Entry(11, 0, 0, 1);
        private static readonly string Body2 = Route(2, 0, 14, 1) + Entry(11, 1, 0, 2);
        private static readonly string Original = Head + Body0 + NoOptional + Body1 + AllOptional + Body2 + NoOptional;

        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-routeediting-" + Guid.NewGuid().ToString("N"), "maps", "Signaltown");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "");
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\rail.sli", "10", "0", "0", "100", "50", "0", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\switch.sco", "11", "100", "100", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "12", "120", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "13", "200", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\signal.sco", "14", "250", "90", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\lamp.sco", "15", "260", "90", "0", "0", "0", "0", "0", "");
            WriteRoutes(Original);
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void TheUntouchedFileReadsBackExactly()
        {
            var routes = OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes(Original));
            Assert.IsTrue(routes.File.IsExactRoundTrip);
            Assert.AreEqual(3, routes.Routes.Count);
            CollectionAssert.IsEmpty(routes.Problems);
            AssertResolvesWithoutProblems(OpenProject());
        }

        [Test]
        public void KindAndAspectChangeInPlace()
        {
            var session = OpenProject();

            var change = session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { Kind = 1, Aspect = 3 });

            Assert.IsNotNull(change);
            Assert.AreEqual(OmsiSignalRouteChangeKind.Edit, change.Kind);
            Assert.AreEqual(0, change.RouteIndex);
            Assert.AreEqual(Head + Route(0, 1, 12, 3) + Entry(10, 0, 0, 0) + NoOptional + Body1 + AllOptional + Body2 + NoOptional, Text(session));
            // The same values again change nothing.
            Assert.IsNull(session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { Kind = 1, Aspect = 3 }));
            Assert.IsNull(session.UpdateSignalRoute(0, new OmsiSignalRouteEdit()));
        }

        [Test]
        public void DistantAndNextSignalComeAndGoWhereTheOmsiEditorWritesThem()
        {
            var session = OpenProject();

            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { DistantSignal = OmsiSignalRouteFieldEdit<int>.Set(13) }, session.Read());
            Assert.AreEqual(Head + Body0 + "\r\n[distsignal]\r\n13\r\n\r\n\r\n\r\n" + Body1 + AllOptional + Body2 + NoOptional, Text(session));

            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { NextSignal = OmsiSignalRouteFieldEdit<int>.Set(14) });
            Assert.AreEqual(Head + Body0 + "\r\n[distsignal]\r\n13\r\n\r\n\r\n[nextsignal]\r\n14\r\n\r\n\r\n" + Body1 + AllOptional + Body2 + NoOptional, Text(session));

            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { DistantSignal = OmsiSignalRouteFieldEdit<int>.Remove });
            Assert.AreEqual(Head + Body0 + "\r\n\r\n[nextsignal]\r\n14\r\n\r\n\r\n" + Body1 + AllOptional + Body2 + NoOptional, Text(session));

            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { NextSignal = OmsiSignalRouteFieldEdit<int>.Remove });
            Assert.AreEqual(Original, Text(session));

            // A new ID of a section that stays is written in place.
            session.UpdateSignalRoute(1, new OmsiSignalRouteEdit { DistantSignal = OmsiSignalRouteFieldEdit<int>.Set(12), NextSignal = OmsiSignalRouteFieldEdit<int>.Set(14) });
            Assert.AreEqual(Head + Body0 + NoOptional + Body1 + AllOptional.Replace("14\r\n\r\n\r\n[next", "12\r\n\r\n\r\n[next").Replace("[nextsignal]\r\n12", "[nextsignal]\r\n14") +
                            Body2 + NoOptional, Text(session));
            AssertResolvesWithoutProblems(session);
        }

        [Test]
        public void SpeedLimitKeepsTheAspectInStep()
        {
            var session = OpenProject();

            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { SpeedLimit = OmsiSignalRouteFieldEdit<double>.Set(40.0) });
            Assert.AreEqual(Head + Route(0, 0, 12, 2) + Entry(10, 0, 0, 0) + "\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n\r\n" + Body1 + AllOptional + Body2 + NoOptional, Text(session));
            var route = OmsiSignalRoutes.Read(session.Workspace).Routes[0];
            Assert.AreEqual(2, route.Aspect);
            Assert.AreEqual(40.0, route.SpeedLimit);

            // A new limit is written in place, with three decimals like the OMSI editor.
            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { SpeedLimit = OmsiSignalRouteFieldEdit<double>.Set(25.5) });
            StringAssert.Contains("[speedlimit]\r\n25.500\r\n", Text(session));

            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { SpeedLimit = OmsiSignalRouteFieldEdit<double>.Remove });
            Assert.AreEqual(Original, Text(session));

            // Without its limit, route 1 shows clear (1); an aspect given with it wins.
            session.UpdateSignalRoute(1, new OmsiSignalRouteEdit { SpeedLimit = OmsiSignalRouteFieldEdit<double>.Remove });
            Assert.AreEqual(Head + Body0 + NoOptional + Route(1, 1, 13, 1) + Entry(11, 0, 0, 1) + "\r\n[distsignal]\r\n14\r\n\r\n\r\n[nextsignal]\r\n12\r\n\r\n\r\n" + Body2 + NoOptional,
                Text(session));
            session.UpdateSignalRoute(2, new OmsiSignalRouteEdit { SpeedLimit = OmsiSignalRouteFieldEdit<double>.Set(60.0), Aspect = 1 });
            var third = OmsiSignalRoutes.Read(session.Workspace).Routes[2];
            Assert.AreEqual(1, third.Aspect);
            Assert.AreEqual(60.0, third.SpeedLimit);
        }

        [Test]
        public void EverySectionOfARouteGoesAndComesBackExactly()
        {
            var session = OpenProject();
            var all = OmsiSignalRouteFieldEdit<int>.Remove;

            session.UpdateSignalRoute(1, new OmsiSignalRouteEdit { DistantSignal = all, NextSignal = all, SpeedLimit = OmsiSignalRouteFieldEdit<double>.Remove });
            Assert.AreEqual(Head + Body0 + NoOptional + Route(1, 1, 13, 1) + Entry(11, 0, 0, 1) + NoOptional + Body2 + NoOptional, Text(session));

            session.UpdateSignalRoute(1, new OmsiSignalRouteEdit
            {
                DistantSignal = OmsiSignalRouteFieldEdit<int>.Set(14), NextSignal = OmsiSignalRouteFieldEdit<int>.Set(12), SpeedLimit = OmsiSignalRouteFieldEdit<double>.Set(40.0)
            });
            Assert.AreEqual(Original, Text(session));
        }

        [Test]
        public void ValuesAndSignalsAreChecked()
        {
            var session = OpenProject();

            Assert.Throws<ArgumentOutOfRangeException>(() => session.UpdateSignalRoute(3, new OmsiSignalRouteEdit { Kind = 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.UpdateSignalRoute(-1, new OmsiSignalRouteEdit { Kind = 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.DeleteSignalRoute(3));
            Assert.Throws<ArgumentNullException>(() => session.UpdateSignalRoute(0, null));
            Assert.That(() => session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { Kind = -1 }), Throws.ArgumentException.With.Message.Contains("kind"));
            Assert.That(() => session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { Aspect = 0 }), Throws.ArgumentException.With.Message.Contains("aspect"));
            foreach (var speed in new[] { 0.0, -10.0, double.NaN, double.PositiveInfinity, 501.0 })
                Assert.That(() => session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { SpeedLimit = OmsiSignalRouteFieldEdit<double>.Set(speed) }),
                    Throws.ArgumentException.With.Message.Contains("speed limit"), speed.ToString());
            Assert.That(() => session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { DistantSignal = OmsiSignalRouteFieldEdit<int>.Set(99) }),
                Throws.ArgumentException.With.Message.EqualTo("Signal route 0 (signal 12): the map has no element 99 for the distant signal."));
            Assert.That(() => session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { NextSignal = OmsiSignalRouteFieldEdit<int>.Set(10) }),
                Throws.ArgumentException.With.Message.Contains("is a spline"));
            Assert.That(() => session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { NextSignal = OmsiSignalRouteFieldEdit<int>.Set(15) }),
                Throws.ArgumentException.With.Message.Contains(@"element 15 (Sceneryobjects\lamp.sco) is not a signal"));

            // An object file that cannot be read leaves only the ID to check.
            session.IsSignalAsset = asset => null;
            Assert.IsNotNull(session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { NextSignal = OmsiSignalRouteFieldEdit<int>.Set(15) }));
            session.IsSignalAsset = null;
            Assert.IsNotNull(session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { NextSignal = OmsiSignalRouteFieldEdit<int>.Set(11) }));
        }

        [Test]
        public void RefusedEditsLeaveTheFileAlone()
        {
            var session = OpenProject();
            Assert.Throws<ArgumentException>(() => session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { Kind = 1, DistantSignal = OmsiSignalRouteFieldEdit<int>.Set(99) }));
            Assert.IsFalse(session.Workspace.IsModified("signalroutes.cfg"));

            // A route with a section twice is left for editing by hand.
            WriteRoutes(Head + Body0 + "\r\n[speedlimit]\r\n40.000\r\n\r\n[speedlimit]\r\n50.000\r\n\r\n" + Body1 + AllOptional);
            var twice = OpenProject("Twice");
            Assert.That(() => twice.UpdateSignalRoute(0, new OmsiSignalRouteEdit { SpeedLimit = OmsiSignalRouteFieldEdit<double>.Remove }),
                Throws.TypeOf<OmsiStepRefusedException>().With.Message.Contains("twice"));
            Assert.IsFalse(twice.Workspace.IsModified("signalroutes.cfg"));

            File.Delete(Path.Combine(mapFolder, "signalroutes.cfg"));
            var none = OpenProject("None");
            Assert.Throws<OmsiStepRefusedException>(() => none.DeleteSignalRoute(0));
        }

        [Test]
        public void DeletingTheFirstMiddleOrLastRouteNumbersTheRestOn()
        {
            var session = OpenProject("First");
            var first = session.DeleteSignalRoute(0);
            Assert.AreEqual(OmsiSignalRouteChangeKind.Deletion, first.Kind);
            Assert.AreEqual(Head + Body1.Replace("   1:", "   0:") + AllOptional + Body2.Replace("   2:", "   1:") + NoOptional, Text(session));
            AssertResolvesWithoutProblems(session);

            session = OpenProject("Middle");
            session.DeleteSignalRoute(1);
            Assert.AreEqual(Head + Body0 + NoOptional + Body2.Replace("   2:", "   1:") + NoOptional, Text(session));

            session = OpenProject("Last");
            session.DeleteSignalRoute(2);
            Assert.AreEqual(Head + Body0 + NoOptional + Body1 + AllOptional, Text(session));
            AssertResolvesWithoutProblems(session);

            session.DeleteSignalRoute(0);
            session.DeleteSignalRoute(0);
            Assert.AreEqual(Head, Text(session));
            Assert.AreEqual(0, OmsiSignalRoutes.Read(session.Workspace).Routes.Count);
        }

        [Test]
        public void NumbersThatDoNotFollowThePlaceStay()
        {
            // Numbers written by another tool, not 0, 1, 2 …, are only comments; MapWorks leaves them.
            var odd = Head + Body0.Replace("   0:", "   5:") + NoOptional + Body1.Replace("   1:", "   7:") + AllOptional + Body2.Replace("   2:", "   9:") + NoOptional;
            WriteRoutes(odd);
            var session = OpenProject();
            session.DeleteSignalRoute(1);
            Assert.AreEqual(Head + Body0.Replace("   0:", "   5:") + NoOptional + Body2.Replace("   2:", "   9:") + NoOptional, Text(session));
        }

        [Test]
        public void UndoAndRedoWriteTheStoredFilesExactly()
        {
            var session = OpenProject();
            var edit = session.UpdateSignalRoute(1, new OmsiSignalRouteEdit { Kind = 0, SpeedLimit = OmsiSignalRouteFieldEdit<double>.Remove });
            var edited = session.Workspace.ReadAllBytes("signalroutes.cfg");
            var deletion = session.DeleteSignalRoute(0);
            var deleted = session.Workspace.ReadAllBytes("signalroutes.cfg");

            session.Undo(deletion);
            CollectionAssert.AreEqual(edited, session.Workspace.ReadAllBytes("signalroutes.cfg"));
            session.Undo(edit);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(Original), session.Workspace.ReadAllBytes("signalroutes.cfg"));
            Assert.IsFalse(edit.IsApplied);
            Assert.Throws<OmsiStepRefusedException>(() => session.Undo(edit));

            session.Redo(edit);
            CollectionAssert.AreEqual(edited, session.Workspace.ReadAllBytes("signalroutes.cfg"));
            session.Redo(deletion);
            CollectionAssert.AreEqual(deleted, session.Workspace.ReadAllBytes("signalroutes.cfg"));
            Assert.Throws<OmsiStepRefusedException>(() => session.Redo(deletion));

            // A file changed since refuses the undo and stays.
            session.UpdateSignalRoute(0, new OmsiSignalRouteEdit { Kind = 1 });
            var now = session.Workspace.ReadAllBytes("signalroutes.cfg");
            Assert.Throws<OmsiStepRefusedException>(() => session.Undo(deletion));
            CollectionAssert.AreEqual(now, session.Workspace.ReadAllBytes("signalroutes.cfg"));
            Assert.IsTrue(deletion.IsApplied);
        }

        [Test]
        public void DeletingARouteFreesItsTrackAndSignals()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            Assert.IsNull(session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 13)).Change);

            // Route 1 is the only one with signal 13; it also names 14 and 12 as distant and next signal.
            var deletion = session.DeleteSignalRoute(1);
            var references = session.BuildReferenceIndex();
            Assert.IsFalse(references.To(13).Any(), string.Join("; ", references.To(13)));

            var result = session.DeleteObject(summary, tile, tile.Objects.Single(item => item.Id == 13));
            Assert.IsNotNull(result.Change, string.Join("; ", result.Blockers));
            session.Undo(summary, result.Change);

            // After undoing the deletion of the route, the signal is protected again.
            session.Undo(deletion);
            summary = session.Read();
            Assert.IsNull(session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects.Single(item => item.Id == 13)).Change);
        }

        private void AssertResolvesWithoutProblems(OmsiMapSession session)
        {
            var map = OmsiSignalRouteMap.Resolve(OmsiSignalRoutes.Read(session.Workspace), session.Read(), session.Workspace, PathCount);
            CollectionAssert.IsEmpty(map.Problems, string.Join("; ", map.Problems));
        }

        private static string Text(OmsiMapSession session)
        {
            return Encoding.ASCII.GetString(session.Workspace.ReadAllBytes("signalroutes.cfg"));
        }

        private static int? PathCount(string asset)
        {
            if (asset.EndsWith("rail.sli", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (asset.EndsWith("switch.sco", StringComparison.OrdinalIgnoreCase))
                return 2;
            return 0;
        }

        private OmsiMapSession OpenProject(string name = "Routes")
        {
            var project = Path.Combine(Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName, "Projects", name + ".omproj");
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
