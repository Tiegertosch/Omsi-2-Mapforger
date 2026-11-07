using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiChronoRulesTests
    {
        private string variantTile;
        private string testRoot;
        private string original;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-chronorules-" + Guid.NewGuid().ToString("N"));
            original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "40", "", "[map]", "0", "0", "tile_0_0.map", "");
            // Object 7 has no rules. Object 9 has a speed limit on path 0 and a traffic density for the first group and for group 4.
            // Spline 30 has a priority and a speed limit on path 1; spline 31 has none.
            Write(original, "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "stop.sco", "7", "10", "20", "0", "0", "0", "0", "0", "",
                "[object]", "0", "junction.sco", "9", "30", "40", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "speedlimit", "50.000", "0", "",
                "[rule]", "0", "trafficdensity", "1.000", "0", "",
                "[rule]", "0", "trafficdensity", "3.000", "4", "",
                "[spline]", "0", @"Splines\road.sli", "30", "0", "31", "0", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "priority", "192", "0", "",
                "[rule]", "1", "speedlimit", "50.000", "0", "",
                "[spline]", "0", @"Splines\road.sli", "31", "30", "0", "50", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "");
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void TheOverlayAppliesRulesLikeOpenOmsi()
        {
            var folder = Path.Combine(original, "Chrono", "0100_Rules");
            Directory.CreateDirectory(folder);
            Write(folder, "Chrono.cfg", "[startdate]", "19920401", "", "[enddate]", "19920414", "");
            // The kill takes away the density of the first group whatever its value; the density of group 4 stays.
            Write(folder, "tile_0_0.map", "[version]", "14", "",
                "[selobject]", "9", "", "[kill_rule]", "0", "trafficdensity", "0.500", "0", "", "[rule]", "0", "trafficdensity", "2.000", "0", "",
                "[selobject]", "7", "", "[rule]", "2", "speedlimit", "30", "0", "",
                "[selspline]", "30", "", "[typ]", @"Splines\road2.sli", "", "[kill_rule]", "1", "speedlimit", "99", "0", "", "[rule]", "1", "speedlimit", "70", "0", "",
                "[selobject]", "424242", "", "[rule]", "0", "speedlimit", "10", "0", "");

            var baseMap = session.Read();
            var overlay = OmsiChrono.Overlay(baseMap, session.Workspace, OmsiChrono.ReadVariants(session.Workspace), out var report);
            var tile = overlay.Tiles[0];

            Assert.AreEqual(3, report.AddedRules);
            Assert.AreEqual(2, report.KilledRules);
            Assert.AreEqual(1, report.UnresolvedSelections);
            CollectionAssert.AreEquivalent(new[] { "0 speedlimit 50 0", "0 trafficdensity 3 4", "0 trafficdensity 2 0" }, Describe(tile.Objects.Single(item => item.Id == 9).Rules));
            CollectionAssert.AreEqual(new[] { "2 speedlimit 30 0" }, Describe(tile.Objects.Single(item => item.Id == 7).Rules));
            var spline = tile.Splines.Single(item => item.Id == 30);
            Assert.AreEqual(@"Splines\road2.sli", spline.AssetPath);
            CollectionAssert.AreEquivalent(new[] { "0 priority 192 0", "1 speedlimit 70 0" }, Describe(spline.Rules));
            // The base map keeps what it had.
            Assert.AreEqual(3, baseMap.Tiles[0].Objects.Single(item => item.Id == 9).Rules.Count);
            Assert.AreEqual(0, baseMap.Tiles[0].Objects.Single(item => item.Id == 7).Rules.Count);
            CollectionAssert.AreEquivalent(new[] { "0 priority 192 0", "1 speedlimit 50 0" }, Describe(baseMap.Tiles[0].Splines.Single(item => item.Id == 30).Rules));
        }

        [Test]
        public void ARuleOfAnObjectIsWrittenAsCommandsInTheVariantAndTheBaseMapStays()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            var baseTile = ReadBytes("tile_0_0.map");
            var record = tile.Objects.Single(item => item.Id == 9);

            var change = session.SetObjectPathRules(tile, record, new[] { 0 }, OmsiMapSession.TrafficRuleNames.ToList(),
                new[] { new OmsiPathRule(0, "speedlimit", 30) });

            Assert.IsNotNull(change);
            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(variantTile));
            StringAssert.Contains("[selobject]\r\n9\r\n\r\n", text);
            // The speed limit is replaced; with every traffic rule named and no density given, the density of the first group goes too (the other group stays).
            StringAssert.Contains("[kill_rule]\r\n0\r\nspeedlimit\r\n50.000\r\n0\r\n\r\n[rule]\r\n0\r\nspeedlimit\r\n30.000\r\n0\r\n", text);
            StringAssert.Contains("[kill_rule]\r\n0\r\ntrafficdensity\r\n1.000\r\n0\r\n", text);
            Assert.AreEqual(1, Count(text, "[rule]"));
            CollectionAssert.AreEqual(baseTile, ReadBytes("tile_0_0.map"));
            // The copy took the record's place; the base map's own record is as it was.
            var now = tile.Objects.Single(item => item.Id == 9);
            Assert.AreNotSame(record, now);
            Assert.AreSame(now, change.CurrentRecord);
            CollectionAssert.AreEquivalent(new[] { "0 trafficdensity 3 4", "0 speedlimit 30 0" }, Describe(now.Rules));
            CollectionAssert.AreEquivalent(new[] { "0 speedlimit 50 0", "0 trafficdensity 1 0", "0 trafficdensity 3 4" }, Describe(record.Rules));
            CollectionAssert.AreEquivalent(new[] { "0 speedlimit 50 0", "0 trafficdensity 1 0", "0 trafficdensity 3 4" },
                Describe(session.Read().Tiles[0].Objects.Single(item => item.Id == 9).Rules));
            AssertOverlayEquals(made, tile);

            session.Undo(map, change);
            CollectionAssert.AreEquivalent(new[] { "0 speedlimit 50 0", "0 trafficdensity 1 0", "0 trafficdensity 3 4" },
                Describe(tile.Objects.Single(item => item.Id == 9).Rules));
            Assert.IsFalse(session.Workspace.FileExists(variantTile));
            AssertOverlayEquals(made, tile);

            session.Redo(map, change);
            CollectionAssert.AreEquivalent(new[] { "0 trafficdensity 3 4", "0 speedlimit 30 0" }, Describe(tile.Objects.Single(item => item.Id == 9).Rules));
            AssertOverlayEquals(made, tile);
        }

        [Test]
        public void EditingTheSameRulesAgainReplacesTheirCommandsInsteadOfPilingUpMore()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            var names = new List<string> { "speedlimit" };

            session.SetObjectPathRules(tile, tile.Objects.Single(item => item.Id == 9), new[] { 0 }, names, new[] { new OmsiPathRule(0, "speedlimit", 30) });
            session.SetObjectPathRules(tile, tile.Objects.Single(item => item.Id == 9), new[] { 0 }, names, new[] { new OmsiPathRule(0, "speedlimit", 70) });

            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(variantTile));
            Assert.AreEqual(1, Count(text, "[selobject]"));
            Assert.AreEqual(1, Count(text, "[kill_rule]"));
            Assert.AreEqual(1, Count(text, "[rule]"));
            StringAssert.Contains("70.000", text);
            AssertOverlayEquals(made, tile);

            // Taking the speed limit away leaves only the kill.
            session.SetObjectPathRules(tile, tile.Objects.Single(item => item.Id == 9), new[] { 0 }, names, new OmsiPathRule[0]);
            text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(variantTile));
            Assert.AreEqual(1, Count(text, "[kill_rule]"));
            Assert.AreEqual(0, Count(text, "[rule]"));
            AssertOverlayEquals(made, tile);
            CollectionAssert.AreEquivalent(new[] { "0 trafficdensity 1 0", "0 trafficdensity 3 4" }, Describe(tile.Objects.Single(item => item.Id == 9).Rules));
        }

        [Test]
        public void TheSameRulesChangeNothing()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var tile = Edit(made).Tiles[0];

            var change = session.SetObjectPathRules(tile, tile.Objects.Single(item => item.Id == 9), new[] { 0 }, new[] { "speedlimit" },
                new[] { new OmsiPathRule(0, "speedlimit", 50) });

            Assert.IsNull(change);
            Assert.IsFalse(session.Workspace.FileExists(variantTile));
        }

        [Test]
        public void TheRightOfWayOfAnObjectIsAVariantCommandToo()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];

            var change = session.SetPriorities(tile, tile.Objects.Single(item => item.Id == 7), new Dictionary<int, double?> { { 1, 64.0 }, { 2, null } });

            Assert.IsNotNull(change);
            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(variantTile));
            StringAssert.Contains("[selobject]\r\n7\r\n\r\n[rule]\r\n1\r\npriority\r\n64\r\n0\r\n", text);
            CollectionAssert.AreEqual(new[] { "1 priority 64 0" }, Describe(tile.Objects.Single(item => item.Id == 7).Rules));
            AssertOverlayEquals(made, tile);
            session.Undo(map, change);
            Assert.AreEqual(0, tile.Objects.Single(item => item.Id == 7).Rules.Count);
        }

        [Test]
        public void ARuleOfAnObjectTheVariantAddedItselfIsEditedInItsOwnRecord()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            var placed = session.PlaceObject(map, tile, "junction.sco", 50, 60, 0, 90);

            var change = session.SetObjectPathRules(tile, placed.Record, new[] { 3 }, OmsiMapSession.TrafficRuleNames.ToList(),
                new[] { new OmsiPathRule(3, "no_cars", 0) });

            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(variantTile));
            StringAssert.DoesNotContain("[selobject]", text);
            StringAssert.Contains("[rule]\r\n3\r\nno_cars\r\n0\r\n0\r\n", text);
            Assert.AreSame(placed.Record, change.CurrentRecord);
            CollectionAssert.AreEqual(new[] { "3 no_cars 0 0" }, Describe(placed.Record.Rules));
            session.Undo(map, change);
            Assert.AreEqual(0, placed.Record.Rules.Count);
            session.Redo(map, change);
            CollectionAssert.AreEqual(new[] { "3 no_cars 0 0" }, Describe(placed.Record.Rules));
        }

        [Test]
        public void AMovedCopyInAVariantKeepsTheRulesTheVariantGaveTheOriginal()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            session.SetObjectPathRules(tile, tile.Objects.Single(item => item.Id == 9), new[] { 0 }, new List<string> { "speedlimit" },
                new[] { new OmsiPathRule(0, "speedlimit", 30) });

            var result = session.CopyInEditedVariant(map, tile, tile.Objects.Single(item => item.Id == 9), 70, 80, 0, 0, 0, 0);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Blockers));
            var moved = tile.Objects.Single(item => item.AssetPath == "junction.sco");
            Assert.AreNotEqual(9, moved.Id);
            CollectionAssert.AreEquivalent(new[] { "0 trafficdensity 1 0", "0 trafficdensity 3 4", "0 speedlimit 30 0" }, Describe(moved.Rules));
            // The change list says the same: the rules of the copy are the effective ones, not the original file's.
            AssertOverlayEquals(made, tile);
        }

        [Test]
        public void ARuleEditNeedsTheCurrentRecordOfTheObject()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var tile = Edit(made).Tiles[0];
            var old = tile.Objects.Single(item => item.Id == 9);
            session.SetObjectPathRules(tile, old, new[] { 0 }, new List<string> { "speedlimit" }, new[] { new OmsiPathRule(0, "speedlimit", 30) });

            Assert.Throws<ArgumentException>(() => session.SetObjectPathRules(tile, old, new[] { 0 }, new List<string> { "speedlimit" },
                new[] { new OmsiPathRule(0, "speedlimit", 70) }));
        }

        [Test]
        public void TheRulesOfSplinesGoIntoTheVariantAndUndoPutsTheSharedRecordsBack()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            var first = tile.Splines.Single(item => item.Id == 30);
            var second = tile.Splines.Single(item => item.Id == 31);
            var edits = new List<OmsiSplinePathRules>();
            foreach (var spline in new[] { first, second })
            {
                var edit = new OmsiSplinePathRules { Tile = tile, Spline = spline, Paths = new[] { 1 }, Names = OmsiMapSession.TrafficRuleNames.ToList() };
                edit.Rules.Add(new OmsiPathRule(1, "speedlimit", 30));
                edits.Add(edit);
            }

            var change = session.SetSplinePathRules(map, edits);

            Assert.IsNotNull(change);
            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(variantTile));
            StringAssert.Contains("[selspline]\r\n30\r\n\r\n[kill_rule]\r\n1\r\nspeedlimit\r\n50.000\r\n0\r\n\r\n[rule]\r\n1\r\nspeedlimit\r\n30.000\r\n0\r\n", text);
            StringAssert.Contains("[selspline]\r\n31\r\n\r\n[rule]\r\n1\r\nspeedlimit\r\n30.000\r\n0\r\n", text);
            // Two splines stay two splines in the same places, and the base map's records are untouched.
            Assert.AreEqual(2, tile.Splines.Count);
            Assert.AreEqual(30, tile.Splines[0].Id);
            CollectionAssert.AreEquivalent(new[] { "0 priority 192 0", "1 speedlimit 30 0" }, Describe(tile.Splines[0].Rules));
            CollectionAssert.AreEquivalent(new[] { "0 priority 192 0", "1 speedlimit 50 0" }, Describe(first.Rules));
            Assert.AreEqual(0, second.Rules.Count);
            AssertOverlayEquals(made, tile);

            session.Undo(map, change);
            Assert.AreSame(first, tile.Splines[0]);
            Assert.AreSame(second, tile.Splines[1]);
            Assert.IsFalse(session.Workspace.FileExists(variantTile));

            session.Redo(map, change);
            CollectionAssert.AreEquivalent(new[] { "0 priority 192 0", "1 speedlimit 30 0" }, Describe(tile.Splines[0].Rules));
            AssertOverlayEquals(made, tile);
        }

        private static List<string> Describe(IEnumerable<OmsiPathRule> rules)
        {
            return rules.Where(rule => !rule.Kills).Select(rule => rule.PathIndex + " " + rule.Name + " " + rule.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                                                                    " " + rule.Extra.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        }

        /// <summary>Lays the variants up to <paramref name="variant"/> over the base map and edits the variant there.</summary>
        private OmsiMapSummary Edit(OmsiChronoVariant variant)
        {
            variantTile = Path.Combine(variant.Folder, "tile_0_0.map");
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var day = variant.FirstDay ?? DateTime.Today;
            var upTo = variants.FindAll(v => string.CompareOrdinal(v.Name, variant.Name) <= 0 && v.IsActiveOn(day));
            var map = OmsiChrono.Overlay(session.Read(), session.Workspace, upTo, out var report);
            session.EditChronoVariant(report.Variants.Single(v => v.Name == variant.Name), report);
            return map;
        }

        /// <summary>
        /// The overlay computed again from the files has the rules the editor shows: this is the check that the commands the
        /// editor writes and the overlay that reads them mean the same.
        /// </summary>
        private void AssertOverlayEquals(OmsiChronoVariant variant, OmsiTileSummary shown)
        {
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var day = variant.FirstDay ?? DateTime.Today;
            var upTo = variants.FindAll(v => string.CompareOrdinal(v.Name, variant.Name) <= 0 && v.IsActiveOn(day));
            var fresh = OmsiChrono.Overlay(session.Read(), session.Workspace, upTo, out _).Tiles[0];
            foreach (var item in shown.Objects)
            {
                var same = fresh.Objects.SingleOrDefault(other => other.Id == item.Id);
                Assert.IsNotNull(same, "object " + item.Id + " is in the overlay computed again");
                CollectionAssert.AreEquivalent(Describe(item.Rules), Describe(same.Rules), "object " + item.Id);
            }
            foreach (var spline in shown.Splines)
                CollectionAssert.AreEquivalent(Describe(spline.Rules), Describe(fresh.Splines.Single(other => other.Id == spline.Id).Rules), "spline " + spline.Id);
        }

        private byte[] ReadBytes(string relativePath)
        {
            return session.Workspace.ReadAllBytes(relativePath);
        }

        private static int Count(string text, string part)
        {
            var count = 0;
            for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        private static void Write(string folder, string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(folder, name),
                OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
