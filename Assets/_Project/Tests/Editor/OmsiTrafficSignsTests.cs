using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The signs of the traffic rules tool as the files hold them (ADR 098, M4): the right of way of an arm and the rules of a road piece.</summary>
    public sealed class OmsiTrafficSignsTests
    {
        // A T junction: the main road runs east and west, the side road comes in from the south (as in OmsiJunctionPrioritiesTests).
        private static readonly OmsiSceneryObject Tee = OmsiSceneryObject.Parse(Encoding.UTF8.GetBytes(string.Join("\r\n",
            Path("15", "2", "270", "0", "30", "0"),
            Path("-15", "-2", "90", "0", "30", "0"),
            Path("15", "2", "270", "-10", "15.708", "2"),
            Path("2", "-15", "0", "-12", "18.85", "2"),
            Path("2", "-15", "0", "10", "15.708", "3")) + "\r\n"));

        // Footpath, road, road, footpath: the lanes for vehicles are 1 and 2.
        private const string RoadProfile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[path]\r\n1\r\n-5.5\r\n0.25\r\n1\r\n2\r\n[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n[path]\r\n1\r\n5.5\r\n0.25\r\n1\r\n2\r\n";

        private const string FootpathProfile =
            "[texture]\r\nside.bmp\r\n[profile]\r\n1\r\n[profilepnt]\r\n-1\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n1\r\n0.1\r\n1\r\n0.1\r\n[path]\r\n1\r\n0\r\n0.1\r\n1.4\r\n2\r\n";

        private string mapFolder;

        private static string Path(string x, string y, string heading, string radius, string length, string blinker)
        {
            return string.Join("\r\n", "[path]", x, y, "0", heading, radius, length, "0", "0", "0", "3", "0", blinker);
        }

        private static OmsiSplineProfile Profile(string assetPath)
        {
            if (assetPath == @"Splines\road.sli")
                return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(RoadProfile), assetPath);
            if (assetPath == @"Splines\walk.sli")
                return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(FootpathProfile), assetPath);
            return null;
        }

        [SetUp]
        public void CreateMap()
        {
            mapFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapforge-traffic-" + Guid.NewGuid().ToString("N"), "maps", "Trafficton");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "");
            // Spline 10 carries a speed limit on lane 1, a traffic density, a no_cars on lane 2 and a priority; spline 11 is the road's next piece; spline 12 is a footpath.
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "10", "0", "0", "100", "2", "100", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[rule]", "1", "speedlimit", "30.000", "0", "",
                "[rule]", "1", "trafficdensity", "0.5", "0", "",
                "[rule]", "2", "no_cars", "0", "0", "",
                "[rule]", "2", "priority", "64", "0", "",
                "[spline]", "0", @"Splines\road.sli", "11", "0", "0", "100", "52", "100", "0", "30", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\walk.sli", "12", "0", "0", "150", "2", "100", "0", "30", "0", "0", "0", "0", "0", "0", "0", "0", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        // ---- Right of way ----

        [Test]
        public void ThePrioritySignWritesTheMainRoadAndYieldTheLowPriority()
        {
            var arm = Arm(0);

            var road = OmsiTrafficSigns.Priorities(arm, OmsiRightOfWaySign.PriorityRoad);
            foreach (var index in arm.Paths)
                Assert.AreEqual(arm.MainPaths.Contains(index) ? (double?)OmsiJunctionPriorities.MainPriority : null, road[index]);

            Assert.IsTrue(OmsiTrafficSigns.Priorities(arm, OmsiRightOfWaySign.Yield).Values.All(value => value == OmsiJunctionPriorities.YieldPriority));
            Assert.IsTrue(OmsiTrafficSigns.Priorities(arm, OmsiRightOfWaySign.Normal).Values.All(value => !value.HasValue));
            CollectionAssert.AreEquivalent(arm.Paths, OmsiTrafficSigns.Priorities(arm, OmsiRightOfWaySign.Yield).Keys);
        }

        [Test]
        public void TheLevelsOfTheSignsAreTheOnesTheFilesCanTell()
        {
            Assert.AreEqual(OmsiPriorityLevel.Main, OmsiTrafficSigns.LevelOf(OmsiRightOfWaySign.PriorityRoad));
            Assert.AreEqual(OmsiPriorityLevel.Yield, OmsiTrafficSigns.LevelOf(OmsiRightOfWaySign.Yield));
            Assert.AreEqual(OmsiPriorityLevel.Normal, OmsiTrafficSigns.LevelOf(OmsiRightOfWaySign.Normal));
        }

        [Test]
        public void ReadBackShowsPriorityRoadYieldOrNormal()
        {
            foreach (var sign in new[] { OmsiRightOfWaySign.Normal, OmsiRightOfWaySign.PriorityRoad, OmsiRightOfWaySign.Yield })
            {
                var arm = Arm(1);
                var rules = new List<OmsiPathRule>();
                foreach (var pair in OmsiTrafficSigns.Priorities(arm, sign))
                    if (pair.Value.HasValue)
                        rules.Add(new OmsiPathRule(pair.Key, "priority", pair.Value.Value));
                var read = OmsiJunctionPriorities.Arms(Tee, rules)[1];

                Assert.AreEqual(sign, OmsiTrafficSigns.SignOf(read), sign.ToString());
            }
        }

        [Test]
        public void ASignDropsAsTheSameWriteTheOldCyclingMade()
        {
            // Normal, priority road, give way: what a click without an armed sign cycles through writes what the signs write.
            var arm = Arm(0);
            arm.Level = OmsiPriorityLevel.Main;
            var cycled = OmsiJunctionPriorities.Priorities(new[] { arm });
            CollectionAssert.AreEquivalent(cycled, OmsiTrafficSigns.Priorities(arm, OmsiRightOfWaySign.PriorityRoad));
            arm.Level = OmsiPriorityLevel.Yield;
            CollectionAssert.AreEquivalent(OmsiJunctionPriorities.Priorities(new[] { arm }), OmsiTrafficSigns.Priorities(arm, OmsiRightOfWaySign.Yield));
            arm.Level = OmsiPriorityLevel.Normal;
            CollectionAssert.AreEquivalent(OmsiJunctionPriorities.Priorities(new[] { arm }), OmsiTrafficSigns.Priorities(arm, OmsiRightOfWaySign.Normal));
        }

        [Test]
        public void TheSignsOfTheTilesAreDrawnForTheSignsOfTheRules()
        {
            Assert.AreEqual(TrafficSignKind.PriorityRoad, TrafficSignPainter.Of(OmsiRightOfWaySign.PriorityRoad));
            Assert.AreEqual(TrafficSignKind.Yield, TrafficSignPainter.Of(OmsiRightOfWaySign.Yield));
            Assert.AreEqual(TrafficSignKind.Normal, TrafficSignPainter.Of(OmsiRightOfWaySign.Normal));
        }

        // ---- Speeds ----

        [Test]
        public void ASpeedTypedInKmhIsReadWithAPointOrACommaAndNothingElseIsTaken()
        {
            Assert.IsTrue(OmsiTrafficSigns.TryParseKmh("50", out var fifty));
            Assert.AreEqual(50.0, fifty);
            Assert.IsTrue(OmsiTrafficSigns.TryParseKmh(" 37,5 ", out var half));
            Assert.AreEqual(37.5, half);
            Assert.IsTrue(OmsiTrafficSigns.TryParseKmh("250", out _));
            foreach (var bad in new[] { "", "  ", "0", "-30", "251", "fast", "NaN", "Infinity", "1e9" })
                Assert.IsFalse(OmsiTrafficSigns.TryParseKmh(bad, out _), bad);
        }

        [Test]
        public void TheLanesForVehiclesAreTheOnesOfKindZero()
        {
            CollectionAssert.AreEqual(new[] { 1, 2 }, OmsiTrafficSigns.RoadLanes(Profile(@"Splines\road.sli")));
            CollectionAssert.IsEmpty(OmsiTrafficSigns.RoadLanes(Profile(@"Splines\walk.sli")));
            CollectionAssert.IsEmpty(OmsiTrafficSigns.RoadLanes(null));
        }

        [Test]
        public void ASpeedGoesOnEveryRoadLaneOfEveryPieceAndFootpathsAreLeftOut()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var pieces = tile.Splines.Select(spline => new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(tile, spline)).ToList();

            var edits = OmsiTrafficSigns.Edits(pieces, Profile, OmsiRoadRuleKind.SpeedLimit, 50.0);

            Assert.AreEqual(2, edits.Edits.Count);
            Assert.AreEqual(1, edits.Skipped);
            foreach (var edit in edits.Edits)
            {
                CollectionAssert.AreEqual(new[] { 1, 2 }, edit.Paths);
                CollectionAssert.AreEqual(new[] { "speedlimit" }, edit.Names);
                CollectionAssert.AreEqual(new[] { "1 speedlimit 50", "2 speedlimit 50" }, edit.Rules.Select(rule => rule.PathIndex + " " + rule.Name + " " + rule.Value));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => OmsiTrafficSigns.Edits(pieces, Profile, OmsiRoadRuleKind.SpeedLimit, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => OmsiTrafficSigns.Edits(pieces, Profile, OmsiRoadRuleKind.SpeedLimit, 300.0));
        }

        [Test]
        public void SettingASpeedReplacesOnlyTheSpeedAndKeepsTheOtherRulesAndUndoRestoresTheBytes()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var before = session.Workspace.ReadAllBytes("tile_0_0.map");

            var change = session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10, 11), Profile, OmsiRoadRuleKind.SpeedLimit, 50.0).Edits);

            Assert.IsNotNull(change);
            var first = tile.Splines.Single(spline => spline.Id == 10);
            CollectionAssert.AreEquivalent(new[] { "1 trafficdensity", "2 no_cars", "2 priority", "1 speedlimit", "2 speedlimit" },
                first.Rules.Select(rule => rule.PathIndex + " " + rule.Name));
            Assert.AreEqual(50.0, OmsiTrafficSigns.SpeedOf(first.Rules));
            Assert.AreEqual(50.0, OmsiTrafficSigns.SpeedOf(tile.Splines.Single(spline => spline.Id == 11).Rules));
            // Written again, nothing changes.
            Assert.IsNull(session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10, 11), Profile, OmsiRoadRuleKind.SpeedLimit, 50.0).Edits));

            session.Undo(summary, change);
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(30.0, OmsiTrafficSigns.SpeedOf(tile.Splines.Single(spline => spline.Id == 10).Rules));
            session.Redo(summary, change);
            Assert.AreEqual(50.0, OmsiTrafficSigns.SpeedOf(tile.Splines.Single(spline => spline.Id == 10).Rules));
        }

        [Test]
        public void TheOvertakingBanAndTheEraserOfTheLimitTakeOnlyTheirOwnRules()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];

            var ban = session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10), Profile, OmsiRoadRuleKind.OvertakingBan, 0.0).Edits);
            Assert.IsNotNull(ban);
            var piece = tile.Splines.Single(spline => spline.Id == 10);
            Assert.IsTrue(OmsiTrafficSigns.HasOvertakingBan(piece.Rules));
            // The speed limit of lane 1 stays: the ban replaces only overtaking_prohib.
            Assert.AreEqual(30.0, OmsiTrafficSigns.SpeedOf(piece.Rules));

            // The eraser of the limit takes the limit and leaves the ban.
            var limitCleared = session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10), Profile, OmsiRoadRuleKind.ClearSpeedLimit, 0.0).Edits);
            Assert.IsNotNull(limitCleared);
            piece = tile.Splines.Single(spline => spline.Id == 10);
            Assert.IsNull(OmsiTrafficSigns.SpeedOf(piece.Rules));
            Assert.IsTrue(OmsiTrafficSigns.HasOvertakingBan(piece.Rules));

            // The eraser of the ban takes the ban and leaves the other rules.
            var cleared = session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10), Profile, OmsiRoadRuleKind.ClearOvertakingBan, 0.0).Edits);
            Assert.IsNotNull(cleared);
            piece = tile.Splines.Single(spline => spline.Id == 10);
            Assert.IsFalse(OmsiTrafficSigns.HasOvertakingBan(piece.Rules));
            Assert.IsNull(OmsiTrafficSigns.SpeedOf(piece.Rules));
            CollectionAssert.AreEquivalent(new[] { "1 trafficdensity", "2 no_cars", "2 priority" }, piece.Rules.Select(rule => rule.PathIndex + " " + rule.Name));

            session.Undo(summary, cleared);
            session.Undo(summary, limitCleared);
            session.Undo(summary, ban);
            Assert.AreEqual(30.0, OmsiTrafficSigns.SpeedOf(tile.Splines.Single(spline => spline.Id == 10).Rules));
            Assert.IsFalse(OmsiTrafficSigns.HasOvertakingBan(tile.Splines.Single(spline => spline.Id == 10).Rules));
        }

        [Test]
        public void ThePieceShowsTheSpeedOfItsLowestLaneAndAKillRuleTakesItAway()
        {
            var rules = new List<OmsiPathRule>
            {
                new OmsiPathRule(2, "speedlimit", 70), new OmsiPathRule(1, "speedlimit", 50), new OmsiPathRule(1, "overtaking_prohib", 0)
            };
            Assert.AreEqual(50.0, OmsiTrafficSigns.SpeedOf(rules));
            Assert.IsTrue(OmsiTrafficSigns.HasOvertakingBan(rules));
            Assert.IsNull(OmsiTrafficSigns.SpeedOf(null));
            Assert.IsFalse(OmsiTrafficSigns.HasOvertakingBan(null));
            Assert.IsNull(OmsiTrafficSigns.SpeedOf(new[] { new OmsiPathRule(1, "trafficdensity", 1) }));
        }

        [Test]
        public void LookGivesWhatSpeedOfAndHasOvertakingBanGiveWithTheLastRuleOfAPathWinning()
        {
            var rules = new List<OmsiPathRule>
            {
                new OmsiPathRule(1, "speedlimit", 30), new OmsiPathRule(2, "speedlimit", 70), new OmsiPathRule(1, "SpeedLimit", 50), new OmsiPathRule(3, "overtaking_prohib", 0)
            };
            OmsiTrafficSigns.Look(rules, out var kmh, out var ban);
            Assert.AreEqual(50.0, kmh);
            Assert.IsTrue(ban);
            Assert.AreEqual(OmsiTrafficSigns.SpeedOf(rules), kmh);
            Assert.AreEqual(OmsiTrafficSigns.HasOvertakingBan(rules), ban);

            OmsiTrafficSigns.Look(new List<OmsiPathRule> { new OmsiPathRule(1, "trafficdensity", 1) }, out kmh, out ban);
            Assert.IsNull(kmh);
            Assert.IsFalse(ban);
            OmsiTrafficSigns.Look(null, out kmh, out ban);
            Assert.IsNull(kmh);
            Assert.IsFalse(ban);
        }

        // Spline 10: a limit on lane 1 and a ban on lane 2, both taken away by a [kill_rule] (a limit and a ban the road type or a variant brings).
        private void WriteKilledTile()
        {
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "10", "0", "0", "100", "2", "100", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[kill_rule]", "1", "speedlimit", "0", "0", "",
                "[kill_rule]", "2", "overtaking_prohib", "0", "0", "",
                "[rule]", "2", "no_cars", "0", "0", "");
        }

        [Test]
        public void TheErasersKeepTheKillRulesThatCancelAnInheritedLimitOrBan()
        {
            WriteKilledTile();
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var before = session.Workspace.ReadAllBytes("tile_0_0.map");

            session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10), Profile, OmsiRoadRuleKind.ClearSpeedLimit, 0.0).Edits);
            session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10), Profile, OmsiRoadRuleKind.ClearOvertakingBan, 0.0).Edits);

            // Nothing to take away: the kill rules stay, so the file is unchanged and the limit and the ban stay cancelled.
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes("tile_0_0.map"));
            var piece = tile.Splines.Single(spline => spline.Id == 10);
            Assert.AreEqual(2, piece.Rules.Count(rule => rule.Kills));
            Assert.IsNull(OmsiTrafficSigns.SpeedOf(piece.Rules));
            Assert.IsFalse(OmsiTrafficSigns.HasOvertakingBan(piece.Rules));
        }

        [Test]
        public void SettingASpeedStillTakesAKillRuleOfTheSameNameAwayAsTheInspectorDoes()
        {
            WriteKilledTile();
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];

            Assert.IsNotNull(session.SetSplinePathRules(summary, OmsiTrafficSigns.Edits(Pieces(tile, 10), Profile, OmsiRoadRuleKind.SpeedLimit, 50.0).Edits));

            var piece = tile.Splines.Single(spline => spline.Id == 10);
            // The kill rule of the limit is replaced by the limit itself; the one of the ban (another name) stays.
            CollectionAssert.AreEquivalent(new[] { "1 speedlimit", "2 speedlimit" }, piece.Rules.Where(rule => rule.Name == "speedlimit").Select(rule => rule.PathIndex + " " + rule.Name));
            Assert.IsFalse(piece.Rules.Any(rule => rule.Kills && rule.Name == "speedlimit"));
            Assert.IsTrue(piece.Rules.Any(rule => rule.Kills && rule.Name == "overtaking_prohib"));
            Assert.AreEqual(50.0, OmsiTrafficSigns.SpeedOf(piece.Rules));
        }

        private static List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> Pieces(OmsiTileSummary tile, params int[] ids)
        {
            return ids.Select(id => new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(tile, tile.Splines.Single(spline => spline.Id == id))).ToList();
        }

        private static OmsiPriorityArm Arm(int index)
        {
            return OmsiJunctionPriorities.Arms(Tee, null)[index];
        }

        private OmsiMapSession OpenProject()
        {
            var project = System.IO.Path.Combine(Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName, "Projects", "Traffic.omproj");
            return OmsiMapSession.OpenProject(System.IO.Path.Combine(mapFolder, "global.cfg"), project);
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(System.IO.Path.Combine(mapFolder, relative), bytes);
        }
    }

    /// <summary>Which tab a tile stands in (ADR 099): the overtaking ban is a road rule but lies on the first tab.</summary>
    public sealed class TrafficItemTabTests
    {
        [Test]
        public void TheSpeedsAndTheirEraserAreOnTheSpeedTabAndTheRestIsOnTheFirstTab()
        {
            Assert.AreEqual(TrafficTab.Speed, new TrafficItem { Kind = TrafficItemKind.Speed, Kmh = 50 }.Tab);
            Assert.AreEqual(TrafficTab.Speed, new TrafficItem { Kind = TrafficItemKind.CustomSpeed }.Tab);
            Assert.AreEqual(TrafficTab.Speed, new TrafficItem { Kind = TrafficItemKind.ClearSpeed }.Tab);
            Assert.AreEqual(TrafficTab.Rules, new TrafficItem { Kind = TrafficItemKind.OvertakingBan }.Tab);
            Assert.AreEqual(TrafficTab.Rules, new TrafficItem { Kind = TrafficItemKind.ClearOvertaking }.Tab);
            Assert.AreEqual(TrafficTab.Rules, new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.Yield }.Tab);
        }

        [Test]
        public void TheBanAndItsEraserGoOnRoadPiecesAndASpeedIsNoEraser()
        {
            Assert.IsTrue(new TrafficItem { Kind = TrafficItemKind.OvertakingBan }.OnRoad);
            Assert.IsTrue(new TrafficItem { Kind = TrafficItemKind.ClearOvertaking }.OnRoad);
            Assert.IsFalse(new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.PriorityRoad }.OnRoad);
            Assert.IsTrue(new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.Normal }.IsEraser);
            Assert.IsTrue(new TrafficItem { Kind = TrafficItemKind.ClearSpeed }.IsEraser);
            // A speed carries the default sign (Normal) and must not count as the eraser of the arms.
            Assert.IsFalse(new TrafficItem { Kind = TrafficItemKind.Speed, Kmh = 50 }.IsEraser);
            Assert.IsFalse(new TrafficItem { Kind = TrafficItemKind.OvertakingBan }.IsEraser);
        }
    }

    /// <summary>The order of Esc inside the traffic rules tool (ADR 098, M4).</summary>
    public sealed class TrafficEscapeTests
    {
        [Test]
        public void ADragGoesBackFirstThenTheArmedSignIsLetGoAndThenTheToolEnds()
        {
            Assert.AreEqual(TrafficEscapeStep.CancelDrag, TrafficEscape.Next(true, true));
            Assert.AreEqual(TrafficEscapeStep.CancelDrag, TrafficEscape.Next(true, false));
            Assert.AreEqual(TrafficEscapeStep.DisarmSign, TrafficEscape.Next(false, true));
            Assert.AreEqual(TrafficEscapeStep.LeaveTool, TrafficEscape.Next(false, false));
        }
    }
}
