using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiJunctionPrioritiesTests
    {
        // A T junction: the main road runs east and west, the side road comes in from the south. Fields of a [path]: x, y,
        // z, heading, radius (positive turns right), length, gradients, kind, width, direction, indicator.
        private static readonly OmsiSceneryObject Tee = OmsiSceneryObject.Parse(Encoding.UTF8.GetBytes(string.Join("\r\n",
            Path("15", "2", "270", "0", "30", "0"),
            Path("-15", "-2", "90", "0", "30", "0"),
            Path("15", "2", "270", "-10", "15.708", "2"),
            Path("2", "-15", "0", "-12", "18.85", "2"),
            Path("2", "-15", "0", "10", "15.708", "3")) + "\r\n"));

        private string testRoot;
        private string original;

        private static string Path(string x, string y, string heading, string radius, string length, string blinker)
        {
            return string.Join("\r\n", "[path]", x, y, "0", heading, radius, length, "0", "0", "0", "3", "0", blinker);
        }

        [SetUp]
        public void CreateMap()
        {
            testRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapforge-priorities-" + Guid.NewGuid().ToString("N"));
            original = System.IO.Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            Write("global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "20", "", "[map]", "0", "0", "tile_0_0.map", "");
            // Junction 5 lets lane 3 give way, takes lane 4's priority away and keeps trucks off lane 0.
            Write("tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "tee.sco", "5", "100", "100", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "trucks", "0", "0", "",
                "[rule]", "3", "priority", "64", "0", "",
                "[kill_rule]", "4", "priority", "128", "0", "",
                "[object]", "0", "tree.sco", "6", "30", "40", "0", "0", "0", "0", "0", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void PathsFallIntoArmsByWhereTrafficComesIn()
        {
            var rules = new List<OmsiPathRule>
            {
                new OmsiPathRule { PathIndex = 0, Name = "priority", Value = 192 }, new OmsiPathRule { PathIndex = 3, Name = "priority", Value = 64 }
            };

            var arms = OmsiJunctionPriorities.Arms(Tee, rules);

            Assert.AreEqual(3, arms.Count);
            var east = arms.Single(arm => arm.Paths.Contains(0));
            var west = arms.Single(arm => arm.Paths.Contains(1));
            var south = arms.Single(arm => arm.Paths.Contains(3));
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, east.Paths);
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, south.Paths);
            Assert.AreEqual(15.0, east.X, 1e-6);
            // A main road's right of way covers straight on and turning right, not turning left across the oncoming traffic.
            CollectionAssert.AreEqual(new[] { 0 }, east.MainPaths);
            CollectionAssert.AreEqual(new[] { 4 }, south.MainPaths);
            Assert.AreEqual(OmsiPriorityLevel.Main, east.Level);
            Assert.AreEqual(OmsiPriorityLevel.Normal, west.Level);
            Assert.AreEqual(OmsiPriorityLevel.Yield, south.Level);

            west.Level = OmsiJunctionPriorities.Next(west.Level);
            Assert.AreEqual(OmsiPriorityLevel.Main, west.Level);
            var priorities = OmsiJunctionPriorities.Priorities(arms);
            Assert.AreEqual(192.0, priorities[0]);
            Assert.IsNull(priorities[2]);
            Assert.AreEqual(192.0, priorities[1]);
            Assert.AreEqual(64.0, priorities[3]);
            Assert.AreEqual(64.0, priorities[4]);
            Assert.AreEqual(OmsiPriorityLevel.Normal, OmsiJunctionPriorities.Next(OmsiPriorityLevel.Yield));

            // One road path alone is no junction.
            var single = OmsiSceneryObject.Parse(Encoding.UTF8.GetBytes(Path("0", "0", "0", "0", "10", "0") + "\r\n"));
            Assert.IsEmpty(OmsiJunctionPriorities.Arms(single, null));
        }

        [Test]
        public void AMadeJunctionGivesTheMainRoadTheRightOfWay()
        {
            // An 8 m carriageway with a lane each way, as in the markings tests.
            const string profileText =
                "[texture]\r\nasphalt.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
                "[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n";
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(profileText), @"Splines\Test\road.sli");
            // East and west are the main road, the side road comes in from the north.
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm
            {
                HeadingDegrees = heading, ProfilePath = @"Splines\Test\road.sli", Profile = profile, Main = heading != 0.0
            }).ToList();
            var made = OmsiJunctionGenerator.Junction(arms, null, "Tee");
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.UTF8.GetBytes(made.Sco));

            var priorities = OmsiJunctionPriorities.ForMadeJunction(sceneryObject, arms);

            var found = OmsiJunctionPriorities.Arms(sceneryObject, null);
            Assert.AreEqual(3, found.Count);
            // Traffic from the north comes in heading south and gives way on every path.
            var side = found.Single(arm => Math.Abs(OmsiRoadPlanner.Normalize(arm.InwardDegrees - 180.0)) < 10.0);
            Assert.IsTrue(side.Paths.All(path => priorities[path] == 64.0));
            foreach (var main in found.Where(arm => !ReferenceEquals(arm, side)))
            {
                Assert.IsTrue(main.MainPaths.All(path => priorities[path] == 192.0));
                Assert.IsTrue(main.Paths.Except(main.MainPaths).All(path => priorities[path] == null));
                Assert.IsNotEmpty(main.MainPaths);
            }

            // Without a main road no path gets a rule.
            foreach (var arm in arms)
                arm.Main = false;
            Assert.IsTrue(OmsiJunctionPriorities.ForMadeJunction(sceneryObject, arms).Values.All(value => value == null));
        }

        [Test]
        public void PrioritiesAreWrittenAfterTheObjectAndTakenBackByUndo()
        {
            var session = OmsiMapSession.OpenProject(System.IO.Path.Combine(original, "global.cfg"), System.IO.Path.Combine(testRoot, "Projects", "Test.omproj"));
            var map = session.Read();
            var tile = map.Tiles[0];
            var junction = tile.Objects.Single(item => item.Id == 5);
            Assert.AreEqual(3, junction.Rules.Count);

            var change = session.SetPriorities(tile, junction, new Dictionary<int, double?> { { 1, 192.0 }, { 3, null }, { 4, 64.0 } });

            Assert.AreEqual(OmsiObjectChangeKind.Rules, change.Kind);
            Assert.IsTrue(change.ObjectExists);
            // The new rules lie right after the object; the trucks rule stays, the old priority and its kill_rule are gone.
            CollectionAssert.AreEqual(new[] { "1 priority 192", "4 priority 64", "0 trucks 0" }, junction.Rules.Select(Describe));
            var read = session.Read().Tiles[0].Objects.Single(item => item.Id == 5);
            CollectionAssert.AreEqual(junction.Rules.Select(Describe), read.Rules.Select(Describe));
            Assert.AreEqual(0, read.Rules.Count(rule => rule.Kills));
            Assert.AreEqual(1, session.Read().Tiles[0].Objects.Count(item => item.Id == 6));
            // The same again changes nothing.
            Assert.IsNull(session.SetPriorities(tile, junction, new Dictionary<int, double?> { { 1, 192.0 }, { 4, 64.0 } }));

            session.Undo(map, change);

            CollectionAssert.AreEqual(new[] { "0 trucks 0", "3 priority 64", "4 priority 128" }, junction.Rules.Select(Describe));
            var undone = session.Read().Tiles[0].Objects.Single(item => item.Id == 5);
            Assert.IsTrue(undone.Rules.Single(rule => rule.PathIndex == 4).Kills);
            session.Redo(map, change);
            Assert.AreEqual(3, junction.Rules.Count);
            Assert.AreEqual(192.0, junction.Rules[0].Value);
        }

        [Test]
        public void TrafficRulesOfPathsAreReplacedAfterTheOtherRulesAndTakenBackByUndo()
        {
            var session = OmsiMapSession.OpenProject(System.IO.Path.Combine(original, "global.cfg"), System.IO.Path.Combine(testRoot, "Projects", "Test.omproj"));
            var map = session.Read();
            var tile = map.Tiles[0];
            var junction = tile.Objects.Single(item => item.Id == 5);

            // Paths 0 and 1: a speed limit, and path 1 kept for buses; the trucks rule of path 0 goes, the priorities stay.
            var change = session.SetObjectPathRules(tile, junction, new[] { 0, 1 }, OmsiMapSession.TrafficRuleNames.ToList(),
                new[] { new OmsiPathRule(0, "speedlimit", 30), new OmsiPathRule(1, "no_cars", 0), new OmsiPathRule(1, "bus", 0) });

            Assert.AreEqual(OmsiObjectChangeKind.Rules, change.Kind);
            CollectionAssert.AreEqual(new[] { "3 priority 64", "4 priority 128", "0 speedlimit 30", "1 no_cars 0", "1 bus 0" }, junction.Rules.Select(Describe));
            var read = session.Read().Tiles[0].Objects.Single(item => item.Id == 5);
            CollectionAssert.AreEqual(junction.Rules.Select(Describe), read.Rules.Select(Describe));
            Assert.IsTrue(read.Rules.Single(rule => rule.PathIndex == 4).Kills);
            var text = OmsiTextFile.Decode(File.ReadAllBytes(System.IO.Path.Combine(testRoot, "Projects", "Test.workspace", "tile_0_0.map")));
            StringAssert.Contains("[rule]\r\n0\r\nspeedlimit\r\n30.000\r\n0", text);
            StringAssert.Contains("[rule]\r\n1\r\nbus\r\n0\r\n0", text);
            // The tree after the junction keeps its place.
            Assert.AreEqual(1, session.Read().Tiles[0].Objects.Count(item => item.Id == 6));
            // The same again changes nothing.
            Assert.IsNull(session.SetObjectPathRules(tile, junction, new[] { 0, 1 }, OmsiMapSession.TrafficRuleNames.ToList(),
                new[] { new OmsiPathRule(0, "speedlimit", 30), new OmsiPathRule(1, "no_cars", 0), new OmsiPathRule(1, "bus", 0) }));

            session.Undo(map, change);
            CollectionAssert.AreEqual(new[] { "0 trucks 0", "3 priority 64", "4 priority 128" }, junction.Rules.Select(Describe));
            session.Redo(map, change);
            Assert.AreEqual(5, junction.Rules.Count);
        }

        [Test]
        public void TrafficDensitiesOfOtherGroupsStayWhenThePathsRulesAreSet()
        {
            Write("tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "tee.sco", "5", "100", "100", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "trafficdensity", "0.500", "0", "",
                "[rule]", "0", "trafficdensity", "3.000", "4", "");
            var session = OmsiMapSession.OpenProject(System.IO.Path.Combine(original, "global.cfg"), System.IO.Path.Combine(testRoot, "Projects", "Test.omproj"));
            var map = session.Read();
            var junction = map.Tiles[0].Objects.Single(item => item.Id == 5);

            session.SetObjectPathRules(map.Tiles[0], junction, new[] { 0 }, OmsiMapSession.TrafficRuleNames.ToList(), new[] { new OmsiPathRule(0, "trafficdensity", 0.1) });

            CollectionAssert.AreEqual(new[] { "0 trafficdensity 3", "0 trafficdensity 0.1" },
                junction.Rules.Select(rule => rule.PathIndex + " " + rule.Name + " " + rule.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.AreEqual(4.0, junction.Rules[0].Extra);
        }

        private static string Describe(OmsiPathRule rule)
        {
            return rule.PathIndex + " " + rule.Name + " " + rule.Value;
        }

        private void Write(string name, params string[] lines)
        {
            File.WriteAllBytes(System.IO.Path.Combine(original, name), OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
