using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiVarParentTests
    {
        private const string Crossing = @"Sceneryobjects\Test\crossing.sco";
        private const string Light = @"Sceneryobjects\Test\light.sco";

        private string testRoot;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-varparent-" + Guid.NewGuid().ToString("N"));
            var original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            File.WriteAllBytes(Path.Combine(original, "global.cfg"), OmsiTextFile.Encode(
                "[name]\r\nTestmap\r\n\r\n[NextIDCode]\r\n40\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n\r\n[map]\r\n1\r\n0\r\ntile_1_0.map\r\n\r\n[map]\r\n3\r\n0\r\ntile_3_0.map\r\n",
                OmsiTextEncoding.Utf16LittleEndian));
            // Tile 0, 0: a crossing (1), a light that names it (2, with a rule after the [varparent]), a light without parent (4) and a head
            // hung on the light (3). Tile 1, 0 beside it holds object 5; tile 3, 0 is two tiles away and holds object 9.
            File.WriteAllBytes(Path.Combine(original, "tile_0_0.map"), OmsiTextFile.Encode(
                "[version]\r\n14\r\n\r\n" +
                "[object]\r\n0\r\n" + Crossing + "\r\n1\r\n100\r\n100\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n" +
                "[object]\r\n0\r\n" + Light + "\r\n2\r\n110\r\n100\r\n0\r\n0\r\n0\r\n0\r\n1\r\n0\r\n\r\n[varparent]\r\n1\r\n\r\n[rule]\r\n0\r\npriority\r\n192\r\n0\r\n\r\n" +
                "[attachObj]\r\n0\r\n" + Light + "\r\n3\r\n2\r\n0\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n" +
                "[object]\r\n0\r\n" + Light + "\r\n4\r\n120\r\n100\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            File.WriteAllBytes(Path.Combine(original, "tile_1_0.map"), OmsiTextFile.Encode(
                "[version]\r\n14\r\n\r\n[object]\r\n0\r\n" + Crossing + "\r\n5\r\n50\r\n50\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            File.WriteAllBytes(Path.Combine(original, "tile_3_0.map"), OmsiTextFile.Encode(
                "[version]\r\n14\r\n\r\n[object]\r\n0\r\n" + Crossing + "\r\n9\r\n50\r\n50\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        private string TileText()
        {
            return OmsiTextFile.Decode(session.Workspace.ReadAllBytes("tile_0_0.map")).Replace("\r\n", "\n");
        }

        [Test]
        public void TheReaderGivesObjectsAndAttachedObjectsTheirVariableParent()
        {
            var tile = session.Read().Tiles[0];

            Assert.IsNull(tile.Objects[0].VarParentId);
            Assert.AreEqual(1, tile.Objects[1].VarParentId);
            Assert.AreEqual(1, tile.Objects[1].Rules.Count, "The rule after the [varparent] still belongs to the light.");
            Assert.IsNull(tile.AttachedObjects[0].VarParentId);
            Assert.IsNull(tile.Objects[2].VarParentId);
        }

        [Test]
        public void SettingOneWritesTheRecordRightAfterTheObjectAndOneUndoStepTakesItBack()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var light = tile.Objects[2];
            var before = session.Workspace.ReadAllBytes("tile_0_0.map");

            var change = session.SetVarParent(summary, tile, light, 1);

            Assert.IsNotNull(change);
            Assert.AreEqual(1, light.VarParentId);
            StringAssert.Contains("\n" + Light + "\n4\n120\n100\n0\n0\n0\n0\n0\n\n[varparent]\n1\n", TileText());
            var check = OmsiMapCheck.Run(summary, session.Source, null).Sections.Single(section => section.Key == "Variable parents");
            Assert.AreEqual(OmsiCheckStatus.Passed, check.Status, string.Join("; ", check.Findings));

            session.Undo(summary, change);
            Assert.IsNull(light.VarParentId);
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes("tile_0_0.map"));

            session.Redo(summary, change);
            Assert.AreEqual(1, light.VarParentId);
            StringAssert.Contains("[varparent]\n1\n", TileText());
        }

        [Test]
        public void ChangingItKeepsOneRecordAndTheRulesAfterIt()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var light = tile.Objects[1];

            var change = session.SetVarParent(summary, tile, light, 5);

            Assert.AreEqual(5, light.VarParentId, "A crossing in the tile next door is reachable.");
            var text = TileText();
            Assert.AreEqual(1, text.Split(new[] { "[varparent]" }, StringSplitOptions.None).Length - 1, "One record only: the light under test had one, the others none.");
            StringAssert.DoesNotContain("[varparent]\n1\n", text);
            StringAssert.Contains("[varparent]\n5\n\n[rule]\n0\npriority\n192\n0\n", text);
            Assert.AreEqual(1, light.Rules.Count);

            session.Undo(summary, change);
            Assert.AreEqual(1, light.VarParentId);
            StringAssert.Contains("[varparent]\n1\n", TileText());
        }

        [Test]
        public void TakingItAwayRemovesTheRecordAndLeavesTheRule()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var light = tile.Objects[1];

            var change = session.SetVarParent(summary, tile, light, null);

            Assert.IsNull(light.VarParentId);
            StringAssert.DoesNotContain("[varparent]", TileText());
            StringAssert.Contains("[rule]\n0\npriority\n192\n0\n", TileText());

            session.Undo(summary, change);
            Assert.AreEqual(1, light.VarParentId);
            StringAssert.Contains("[varparent]\n1\n", TileText());
        }

        [Test]
        public void AnAttachedObjectGetsItsOwnVariableParent()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var head = tile.AttachedObjects[0];

            var change = session.SetVarParent(summary, tile, head, 1);

            Assert.AreEqual(1, head.VarParentId);
            StringAssert.Contains("[attachObj]\n0\n" + Light + "\n3\n2\n0\n0\n0\n0\n0\n0\n\n[varparent]\n1\n", TileText());
            Assert.AreEqual(1, tile.Objects[1].VarParentId, "The light the head hangs on keeps its own.");

            session.Undo(summary, change);
            Assert.IsNull(head.VarParentId);
            StringAssert.DoesNotContain("[attachObj]\n0\n" + Light + "\n3\n2\n0\n0\n0\n0\n0\n0\n\n[varparent]", TileText());
        }

        [Test]
        public void TheSameParentIsNoChange()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];

            Assert.IsNull(session.SetVarParent(summary, tile, tile.Objects[1], 1));
            Assert.IsNull(session.SetVarParent(summary, tile, tile.Objects[2], null));
            CollectionAssert.IsEmpty(session.ModifiedFiles());
        }

        [Test]
        public void ARefusalSaysWhyAndChangesNothing()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var light = tile.Objects[2];

            var self = Assert.Throws<ArgumentException>(() => session.SetVarParent(summary, tile, light, 4));
            StringAssert.Contains("own variable parent", self.Message);
            var missing = Assert.Throws<ArgumentException>(() => session.SetVarParent(summary, tile, light, 777));
            StringAssert.Contains("777", missing.Message);
            var far = Assert.Throws<ArgumentException>(() => session.SetVarParent(summary, tile, light, 9));
            StringAssert.Contains("next to it", far.Message);
            Assert.IsNull(light.VarParentId);
            CollectionAssert.IsEmpty(session.ModifiedFiles());
        }

        [Test]
        public void TheParentCannotBeDeletedWhileALightNamesIt()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];

            var result = session.DeleteObject(summary, tile, tile.Objects[0]);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Blockers.Any(blocker => blocker.Kind == "variable parent"));
        }

        [Test]
        public void ACopyOfAnObjectIsAStandaloneObjectWithoutTheVariableParent()
        {
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var data = OmsiClipboardData.Capture(summary, new[] { new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, tile.Objects[1]) });

            var result = session.PasteObjects(summary, data.PlaceAt(data.CentreX + 20, data.CentreY, 0));

            Assert.IsNotNull(result);
            Assert.AreEqual(4, tile.Objects.Count);
            Assert.IsNull(tile.Objects[tile.Objects.Count - 1].VarParentId);
        }
    }
}
