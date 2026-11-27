using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiRailCheckTests
    {
        // One track in the middle, used both ways.
        private const string RailProfile = "[path]\r\n2\r\n0\r\n0\r\n1.5\r\n2\r\n";

        // A switch with two positions: path 0 straight 20 m (position 0), path 1 a curve of 20 m (position 1), path 2 a piece without a position, standing apart.
        private const string SwitchObject =
            "[switch]\r\n2\r\n\r\n" +
            "[path]\r\n0\r\n0\r\n0\r\n0\r\n0\r\n20\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n\r\n[switchdir]\r\n0\r\n\r\n" +
            "[path]\r\n0\r\n0\r\n0\r\n0\r\n100\r\n20\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n\r\n[switchdir]\r\n1\r\n\r\n" +
            "[path]\r\n50\r\n0\r\n0\r\n0\r\n0\r\n5\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n";

        private const string Head = "Signal Routes File\r\n\r\n";

        private string root;
        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-rail-" + Guid.NewGuid().ToString("N"));
            mapFolder = Path.Combine(root, "maps", "Railtown");
            Directory.CreateDirectory(mapFolder);
            Directory.CreateDirectory(Path.Combine(root, "Splines"));
            Directory.CreateDirectory(Path.Combine(root, "Sceneryobjects"));
            File.WriteAllText(Path.Combine(root, "Splines", "rail.sli"), RailProfile);
            File.WriteAllText(Path.Combine(root, "Sceneryobjects", "switch.sco"), SwitchObject);
            Write("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "");
            // Spline (id, previous, next, x, height, y, heading, length, radius ...); heading 0 runs north, 90 east.
            Write("tile_0_0.map", "[version]", "14", "",
                // A switch: spline 10 runs north into switch 11, spline 12 goes on from its straight branch, its curve leads nowhere.
                Spline(10, 100, 100, 0, 50),
                "[object]", "0", @"Sceneryobjects\switch.sco", "11", "100", "150", "0", "0", "0", "0", "0", "",
                Spline(12, 100, 170, 0, 50),
                // At the edge: spline 19 leads into spline 20, which runs on east out of the map.
                Spline(19, 200, 50, 90, 50),
                Spline(20, 250, 50, 90, 60),
                // A gap: spline 31 starts 3 m after spline 30 ends.
                Spline(30, 20, 250, 90, 30),
                Spline(31, 53, 250, 90, 30),
                // A branch without a switch: spline 40 runs on into spline 41 and into spline 42, which starts 0.8 m beside it.
                Spline(40, 200, 200, 0, 30),
                Spline(41, 200, 230, 0, 30),
                Spline(42, 200.8, 230, 5, 30));
            // Route 0 over spline 10 and both branches of the switch; route 1 over spline 20; route 2 over spline 30.
            Write("signalroutes.cfg", Head +
                "   0:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n90\r\n1\r\n\r\n[entry]\r\n10\r\n0\r\n0\r\n0\r\n\r\n[entry]\r\n11\r\n0\r\n0\r\n1\r\n\r\n[entry]\r\n11\r\n1\r\n0\r\n2\r\n\r\n" +
                "   1:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n91\r\n1\r\n\r\n[entry]\r\n20\r\n0\r\n0\r\n7\r\n\r\n" +
                "   2:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n92\r\n1\r\n\r\n[entry]\r\n30\r\n0\r\n0\r\n8\r\n\r\n");
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void SwitchAndSwitchDirectionsRead()
        {
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject));

            Assert.IsTrue(sceneryObject.IsSwitch);
            Assert.AreEqual(2, sceneryObject.SwitchPositions);
            Assert.AreEqual(3, sceneryObject.Paths.Count);
            Assert.AreEqual(0, sceneryObject.Paths[0].SwitchPosition);
            Assert.AreEqual(1, sceneryObject.Paths[1].SwitchPosition);
            Assert.IsNull(sceneryObject.Paths[2].SwitchPosition);
            Assert.AreEqual(0, sceneryObject.StraySwitchDirections);
            Assert.IsFalse(OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(RailProfileObject())).IsSwitch);

            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var paths = Paths(summary);
            var branches = paths.Where(path => path.ElementId == 11).OrderBy(path => path.PathIndex).ToList();
            Assert.IsTrue(branches.All(path => path.OnSwitch && path.Kind == 2));
            CollectionAssert.AreEqual(new int?[] { 0, 1, null }, branches.Select(path => path.SwitchPosition).ToArray());
            Assert.IsFalse(paths.Single(path => path.ElementId == 10).OnSwitch);
        }

        [Test]
        public void SwitchCountMustMatchTheDistinctPositions()
        {
            var findings = new List<OmsiRailFinding>();
            OmsiRailCheck.CheckSceneryObject("good.sco", OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject)), findings);
            CollectionAssert.IsEmpty(findings);

            OmsiRailCheck.CheckSceneryObject("three.sco", OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject.Replace("[switch]\r\n2", "[switch]\r\n3"))), findings);
            var three = findings.Single();
            Assert.AreEqual(OmsiRailFindingKind.SwitchPositionCount, three.Kind);
            Assert.IsTrue(three.IsError);
            Assert.AreEqual(3, three.Count);
            CollectionAssert.AreEqual(new[] { 0, 1 }, three.Numbers);

            // Two positions, but one of them is 2: as many as [switch] gives, yet not below it.
            findings.Clear();
            OmsiRailCheck.CheckSceneryObject("high.sco", OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject.Replace("[switchdir]\r\n1", "[switchdir]\r\n2"))), findings);
            Assert.AreEqual("high.sco", findings.Single().AssetPath);
            CollectionAssert.AreEqual(new[] { 0, 2 }, findings.Single().Numbers);

            // A switch without any [switchdir].
            findings.Clear();
            OmsiRailCheck.CheckSceneryObject("none.sco", OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject.Replace("[switchdir]", "[nothing]"))), findings);
            Assert.AreEqual(OmsiRailFindingKind.SwitchPositionCount, findings.Single().Kind);
        }

        [Test]
        public void SwitchDirectionBeforeTheFirstPathBelongsToNoPath()
        {
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes("[switchdir]\r\n0\r\n\r\n" + SwitchObject));
            Assert.AreEqual(1, sceneryObject.StraySwitchDirections);
            Assert.AreEqual(0, sceneryObject.Paths[0].SwitchPosition);

            var findings = new List<OmsiRailFinding>();
            OmsiRailCheck.CheckSceneryObject("stray.sco", sceneryObject, findings);
            var finding = findings.Single();
            Assert.AreEqual(OmsiRailFindingKind.SwitchDirectionBeforePath, finding.Kind);
            Assert.IsTrue(finding.IsError);
            Assert.AreEqual(1, finding.Count);

            // Also in an object that is no switch.
            findings.Clear();
            OmsiRailCheck.CheckSceneryObject("plain.sco", OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes("[switchdir]\r\n1\r\n\r\n" + RailProfileObject())), findings);
            Assert.AreEqual(OmsiRailFindingKind.SwitchDirectionBeforePath, findings.Single().Kind);
        }

        [Test]
        public void OpenRailEndsAreReportedWhereRoutesRunAndNotAtTheMapEdge()
        {
            var findings = Check();
            var open = findings.Where(finding => finding.Kind == OmsiRailFindingKind.OpenRailEnd).ToList();

            // Spline 10 starts open and route 0 runs over it: no rail end near, the end of the track.
            var start = open.Single(finding => finding.ElementId == 10);
            Assert.AreEqual(0, start.RouteIndex);
            Assert.IsNull(start.NearestMeters);
            Assert.AreEqual(100.0, start.LocalX, 1e-6);
            Assert.AreEqual(100.0, start.LocalY, 1e-6);
            // Spline 30 ends 3 m before spline 31: a gap.
            var gap = open.Single(finding => finding.ElementId == 30 && finding.LocalX > 40.0);
            Assert.AreEqual(2, gap.RouteIndex);
            Assert.AreEqual(3.0, gap.NearestMeters.Value, 1e-6);
            Assert.AreEqual(31, gap.Nearest.ElementId);
            // Spline 12 ends open, but no route runs over it; spline 20 ends outside the map.
            Assert.IsFalse(open.Any(finding => finding.ElementId == 12 || finding.ElementId == 20 || finding.ElementId == 19));
            // The curve of the switch ends 2 m beside spline 12, and route 0 runs over it; the start of spline 30 is open as well.
            var curve = open.Single(finding => finding.ElementId == 11);
            Assert.AreEqual(1, curve.PathIndex);
            Assert.AreEqual(12, curve.Nearest.ElementId);
            Assert.AreEqual(4, open.Count);
        }

        [Test]
        public void SwitchWithAnOpenBranchIsOneFinding()
        {
            var findings = Check();

            var open = findings.Single(finding => finding.Kind == OmsiRailFindingKind.OpenSwitchBranch);
            Assert.AreEqual(11, open.ElementId);
            Assert.AreEqual(@"Sceneryobjects\switch.sco", open.AssetPath);
            // The curve ends open; path 2 stands apart with both ends open.
            Assert.AreEqual(3, open.Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, open.Numbers);
            Assert.IsFalse(open.IsError);
        }

        [Test]
        public void RouteOverTwoPositionsOfOneSwitchIsReported()
        {
            var findings = Check();

            var both = findings.Single(finding => finding.Kind == OmsiRailFindingKind.RouteInTwoPositions);
            Assert.AreEqual(0, both.RouteIndex);
            Assert.AreEqual(11, both.ElementId);
            CollectionAssert.AreEqual(new[] { 0, 1 }, both.Numbers);

            // Over the curve and the piece after it, which has no position, the route is fine.
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var routes = OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes(Head +
                "   0:\r\n[signalroute]\r\n0\r\n\r\n[signal]\r\n90\r\n1\r\n\r\n[entry]\r\n11\r\n1\r\n0\r\n2\r\n\r\n[entry]\r\n11\r\n2\r\n0\r\n3\r\n\r\n"));
            Assert.IsFalse(OmsiRailCheck.CheckTrack(summary, Paths(summary), routes).Any(finding => finding.Kind == OmsiRailFindingKind.RouteInTwoPositions));
        }

        [Test]
        public void BranchWithoutSwitchIsReportedOnceAndNotAtASwitch()
        {
            var findings = Check();

            var branch = findings.Single(finding => finding.Kind == OmsiRailFindingKind.BranchWithoutSwitch);
            Assert.AreEqual(40, branch.From.ElementId);
            CollectionAssert.AreEquivalent(new[] { 41, 42 }, branch.Branches.Select(path => path.ElementId));
            Assert.AreEqual(0.8, branch.StartOffsetMeters, 1e-6);
            Assert.Greater(branch.StartOffsetMeters, OmsiRailCheck.OffsetMeters);
        }

        [Test]
        public void MapCheckListsTheRailFindings()
        {
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));

            var section = OmsiMapCheck.Run(summary, new OmsiFolderSource(mapFolder), root).Sections.Single(candidate => candidate.Key == "Rail paths");

            Assert.AreEqual(OmsiCheckStatus.Note, section.Status);
            // Four open ends, the switch, the route over both positions and the branch.
            Assert.AreEqual(7, section.Findings.Count, string.Join("\n", section.Findings));
            Assert.AreEqual(0, section.Tile.X);
        }

        [Test]
        public void UnreadableSwitchCountIsItsOwnError()
        {
            foreach (var text in new[] { "[switch]\r\nabc", "[switch]\r\n0", "[switch]\r\n-1" })
            {
                var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject.Replace("[switch]\r\n2", text)));
                Assert.IsTrue(sceneryObject.IsSwitch, text);
                Assert.AreEqual(0, sceneryObject.SwitchPositions, text);
                var findings = new List<OmsiRailFinding>();
                OmsiRailCheck.CheckSceneryObject("bad.sco", sceneryObject, findings);
                var finding = findings.Single();
                Assert.AreEqual(OmsiRailFindingKind.SwitchCountUnreadable, finding.Kind, text);
                Assert.IsTrue(finding.IsError);
                Assert.AreEqual("bad.sco", finding.AssetPath);
            }

            // [switch] as the last line, without a number.
            var missing = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(RailProfileObject() + "\r\n[switch]\r\n"));
            Assert.AreEqual(0, missing.SwitchPositions);
            var none = new List<OmsiRailFinding>();
            OmsiRailCheck.CheckSceneryObject("missing.sco", missing, none);
            Assert.AreEqual(OmsiRailFindingKind.SwitchCountUnreadable, none.Single().Kind);
        }

        [Test]
        public void SwitchDirectionAfterAnUnreadablePathIsDiscarded()
        {
            var text = SwitchObject + "\r\n[path]\r\nabc\r\n0\r\n0\r\n0\r\n0\r\n5\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n\r\n[switchdir]\r\n1\r\n";
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(text));

            Assert.AreEqual(3, sceneryObject.Paths.Count);
            Assert.IsNull(sceneryObject.Paths[2].SwitchPosition);
            Assert.AreEqual(0, sceneryObject.StraySwitchDirections);
        }

        [Test]
        public void BranchWithoutOffsetIsReportedOncePerPlaceAndHeading()
        {
            Write("tile_0_0.map", "[version]", "14", "",
                // Splines 40 and 43 lie on each other and both run on into 41 and 42, which start at the same point.
                Spline(40, 200, 200, 0, 30),
                Spline(43, 200, 200, 0, 30),
                Spline(41, 200, 230, 0, 30),
                Spline(42, 200, 230, 5, 30));
            var findings = Check().Where(finding => finding.Kind == OmsiRailFindingKind.BranchWithoutSwitch).ToList();

            // One finding going north (from 40 or 43) and one going south (from 41 or 42), not one per lane.
            Assert.AreEqual(2, findings.Count, string.Join(", ", findings.Select(finding => finding.From.ElementId)));
            var north = findings.Single(finding => finding.From.ElementId == 40 || finding.From.ElementId == 43);
            CollectionAssert.AreEquivalent(new[] { 41, 42 }, north.Branches.Select(path => path.ElementId));
            Assert.AreEqual(0.0, north.StartOffsetMeters, 1e-6);

            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var section = OmsiMapCheck.Run(summary, new OmsiFolderSource(mapFolder), root).Sections.Single(candidate => candidate.Key == "Rail paths");
            Assert.IsFalse(section.Findings.Any(finding => finding.ToString().Contains("1.5 m")), string.Join("\n", section.Findings));
        }

        [Test]
        public void TrackRunningIntoASwitchIsNoBranchWithoutSwitch()
        {
            // Spline 10 runs into both branches of switch 11.
            Assert.IsFalse(Check().Any(finding => finding.Kind == OmsiRailFindingKind.BranchWithoutSwitch && finding.From.ElementId == 10));
        }

        [Test]
        public void OpenSwitchEndsOutsideTheMapAreLeftOut()
        {
            // A switch without the piece standing apart, at the east edge: spline 50 runs into it, both branches leave the map.
            var sceneryText = SwitchObject.Substring(0, SwitchObject.LastIndexOf("[path]", StringComparison.Ordinal));
            Write("tile_0_0.map", "[version]", "14", "",
                Spline(50, 240, 150, 90, 50),
                "[object]", "0", @"Sceneryobjects\switch.sco", "11", "290", "150", "0", "90", "0", "0", "0", "");
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var switchObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(sceneryText));
            var rail = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(RailProfile), @"Splines\rail.sli");
            var paths = summary.Tiles.SelectMany(tile => OmsiMapPaths.OfTile(summary, tile, asset => switchObject, asset => rail)).ToList();

            var findings = OmsiRailCheck.CheckTrack(summary, paths, null);
            // The ends are open, but they lie in tile 1, 0, which the map does not have.
            Assert.IsTrue(paths.Where(path => path.OnSwitch).All(path => !path.OpenStart && path.OpenEnd));
            Assert.IsFalse(findings.Any(finding => finding.Kind == OmsiRailFindingKind.OpenSwitchBranch), string.Join("; ", paths.Select(path => path.ElementId + "/" + path.PathIndex + " " + path.Points[0].X + "," + path.Points[0].Y + " -> " + path.Points[path.Points.Length - 1].X + "," + path.Points[path.Points.Length - 1].Y + " " + path.OpenStart + path.OpenEnd)));
        }

        [Test]
        public void RailSectionPassesWithoutRailPathsAndIsAProblemForSwitchFaults()
        {
            Write("tile_0_0.map", "[version]", "14", "");
            File.Delete(Path.Combine(mapFolder, "signalroutes.cfg"));
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            var empty = OmsiMapCheck.Run(summary, new OmsiFolderSource(mapFolder), root).Sections.Single(candidate => candidate.Key == "Rail paths");
            Assert.AreEqual(OmsiCheckStatus.Passed, empty.Status);
            CollectionAssert.IsEmpty(empty.Findings);

            foreach (var sceneryText in new[] { SwitchObject.Replace("[switch]\r\n2", "[switch]\r\n3"), "[switchdir]\r\n0\r\n\r\n" + SwitchObject })
            {
                File.WriteAllText(Path.Combine(root, "Sceneryobjects", "switch.sco"), sceneryText);
                Write("tile_0_0.map", "[version]", "14", "",
                    "[object]", "0", @"Sceneryobjects\switch.sco", "11", "100", "150", "0", "0", "0", "0", "0", "");
                summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
                var section = OmsiMapCheck.Run(summary, new OmsiFolderSource(mapFolder), root).Sections.Single(candidate => candidate.Key == "Rail paths");
                Assert.AreEqual(OmsiCheckStatus.Problem, section.Status, string.Join("\n", section.Findings));
            }
        }

        private List<OmsiRailFinding> Check()
        {
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            return OmsiRailCheck.CheckTrack(summary, Paths(summary), OmsiSignalRoutes.Read(new OmsiFolderSource(mapFolder)));
        }

        private static List<OmsiMapPath> Paths(OmsiMapSummary summary)
        {
            var switchObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(SwitchObject));
            var rail = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(RailProfile), @"Splines\rail.sli");
            return summary.Tiles.SelectMany(tile => OmsiMapPaths.OfTile(summary, tile, asset => switchObject, asset => rail)).ToList();
        }

        /// <summary>An object with one straight rail path and no switch.</summary>
        private static string RailProfileObject()
        {
            return "[path]\r\n0\r\n0\r\n0\r\n0\r\n0\r\n20\r\n0\r\n0\r\n2\r\n1.5\r\n2\r\n0\r\n";
        }

        private static string Spline(int id, double x, double y, double heading, double length)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return string.Join("\r\n", "[spline]", "0", @"Splines\rail.sli", id.ToString(culture), "0", "0", x.ToString(culture), "0", y.ToString(culture),
                heading.ToString(culture), length.ToString(culture), "0", "0", "0", "0", "0", "0", "0", "0", "");
        }

        private void Write(string relative, params string[] lines)
        {
            File.WriteAllText(Path.Combine(mapFolder, relative), string.Join("\r\n", lines) + "\r\n");
        }
    }
}
