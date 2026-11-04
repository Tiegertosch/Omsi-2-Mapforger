using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiChronoLabelsTests
    {
        private string testRoot;
        private string original;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-labels-" + Guid.NewGuid().ToString("N"));
            original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "20", "", "[map]", "0", "0", "tile_0_0.map", "");
            // Object 5 is a bus stop with two labels and a rule, sign 8 hangs on it with one label, object 6 has none, and
            // row 9 has no label either.
            Write(original, "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "stop.sco", "5", "10", "20", "0", "0", "0", "0", "2", "U Rathaus", "  30 ", "",
                "[rule]", "0", "trafficdensity", "1.000", "0", "",
                "[attachObj]", "0", "sign.sco", "8", "5", "0", "1", "0", "0", "0", "1", "13", "",
                "[object]", "0", "tree.sco", "6", "30", "40", "0", "0", "0", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "9", "0", "1", "0", "0", "0", "0", "0", "10", "50", "1", "0", "");
            var signs = Path.Combine(original, "Chrono", "0100_Signs");
            Directory.CreateDirectory(signs);
            Write(signs, "Chrono.cfg", "[startdate]", "19920401", "", "[enddate]", "19920414", "");
            // The plate gets another file and the labels "13N" and "Z"; the stop gets one label; a row and the tree swap
            // their file. [typ] keeps the labels. The last selection names nothing.
            Write(signs, "tile_0_0.map", "[version]", "14", "",
                "[selobject]", "8", "", "[typ]", "sign2.sco", "", "[relabel]", "2", "13N", "Z", "",
                "[selobject]", "5", "", "[relabel]", "1", "Only", "",
                "[selobject]", "9", "", "[typ]", "lamp2.sco", "",
                "[selobject]", "6", "", "[typ]", "oak.sco", "",
                "[selobject]", "999", "", "[relabel]", "1", "Lost", "");
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void ReadsTheLabelsOfObjectsAndAttachedObjectsAsWritten()
        {
            var tile = session.Read().Tiles[0];

            CollectionAssert.AreEqual(new[] { "U Rathaus", "  30 " }, tile.Objects.Single(item => item.Id == 5).Labels);
            CollectionAssert.AreEqual(new[] { "13" }, tile.AttachedObjects.Single().Labels);
            Assert.AreEqual(0, tile.Objects.Single(item => item.Id == 6).Labels.Count);
        }

        [Test]
        public void EqualLabelTextsOfManyObjectsAreKeptOnce()
        {
            Write(original, "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "tree.sco", "1", "0", "0", "0", "0", "0", "0", "1", "Textures\\oak.bmp", "",
                "[object]", "0", "tree.sco", "2", "1", "0", "0", "0", "0", "0", "1", "Textures\\oak.bmp", "");

            var tile = OmsiMapSession.OpenReadOnly(Path.Combine(original, "global.cfg")).Read().Tiles[0];

            Assert.AreSame(tile.Objects[0].Labels[0], tile.Objects[1].Labels[0]);
        }

        [Test]
        public void TheOverlayChangesTheTypeAndTheLabelsOfAttachedObjectsAndRows()
        {
            var overlay = Overlay(out var report);
            var tile = overlay.Tiles[0];

            var plate = tile.AttachedObjects.Single();
            Assert.AreEqual("sign2.sco", plate.AssetPath);
            CollectionAssert.AreEqual(new[] { "13N", "Z" }, plate.Labels);
            Assert.AreEqual(5, plate.ParentId);
            Assert.AreEqual(1, plate.AttachPointIndex);
            Assert.AreEqual("lamp2.sco", tile.SplineAttachments.Single().AssetPath);
            Assert.AreEqual(9, tile.SplineAttachments.Single().Id);
            Assert.AreEqual("oak.sco", tile.Objects.Single(item => item.Id == 6).AssetPath);
            Assert.AreEqual(3, report.ChangedTypes);
            Assert.AreEqual(2, report.ChangedLabels);
            Assert.AreEqual(1, report.UnresolvedSelections);
        }

        [Test]
        public void ARelabelReplacesAllLabelsAndATypeChangeKeepsThemWithTheRules()
        {
            var overlay = Overlay(out _);
            var stop = overlay.Tiles[0].Objects.Single(item => item.Id == 5);

            CollectionAssert.AreEqual(new[] { "Only" }, stop.Labels);
            Assert.AreEqual(1, stop.Rules.Count);

            // Typing the stop and not relabelling it leaves its labels and its rule.
            Write(Path.Combine(original, "Chrono", "0100_Signs"), "tile_0_0.map", "[version]", "14", "", "[selobject]", "5", "", "[typ]", "stop2.sco", "");
            var retyped = Overlay(out _).Tiles[0].Objects.Single(item => item.Id == 5);
            Assert.AreEqual("stop2.sco", retyped.AssetPath);
            CollectionAssert.AreEqual(new[] { "U Rathaus", "  30 " }, retyped.Labels);
            Assert.AreEqual(1, retyped.Rules.Count);
        }

        [Test]
        public void TheBaseMapStaysAsItWasAndEmptyLabelsAndTheirCountAreKept()
        {
            var baseMap = session.Read();
            Write(Path.Combine(original, "Chrono", "0100_Signs"), "tile_0_0.map", "[version]", "14", "",
                "[selobject]", "5", "", "[relabel]", "4", "A", "", "", "", "");
            var changed = OmsiChrono.Overlay(baseMap, session.Workspace, OmsiChrono.ReadVariants(session.Workspace), out _).Tiles[0].Objects.Single(item => item.Id == 5);

            CollectionAssert.AreEqual(new[] { "A", "", "", "" }, changed.Labels);
            CollectionAssert.AreEqual(new[] { "U Rathaus", "  30 " }, baseMap.Tiles[0].Objects.Single(item => item.Id == 5).Labels);
        }

        [Test]
        public void ChangesTheTypeAndLabelsInTheMapsOwnTileAndTakesThemBack()
        {
            var tileBytes = File.ReadAllBytes(Path.Combine(original, "tile_0_0.map"));
            var map = session.Read();
            var tile = map.Tiles[0];
            var stop = tile.Objects.Single(item => item.Id == 5);
            var plate = tile.AttachedObjects.Single();

            var retyped = session.SetElementType(tile, plate, "sign3.sco");
            var relabelled = session.SetElementLabels(tile, stop, new[] { "Spandau", "10", "extra" });

            Assert.AreEqual(OmsiObjectChangeKind.Retype, retyped.Kind);
            Assert.AreEqual(8, retyped.ElementId);
            Assert.IsTrue(retyped.ElementIsAttached);
            Assert.AreSame(stop, retyped.Record, "an attached object belongs to the object it hangs on");
            Assert.AreEqual("sign3.sco", plate.AssetPath);
            CollectionAssert.AreEqual(new[] { "13" }, plate.Labels);
            CollectionAssert.AreEqual(new[] { "Spandau", "10", "extra" }, stop.Labels);
            Assert.AreEqual("stop.sco", stop.AssetPath);
            CollectionAssert.AreEqual(tileBytes, File.ReadAllBytes(Path.Combine(original, "tile_0_0.map")), "the original map is never written");

            // The workspace file says the same: read the map again.
            var again = session.Read().Tiles[0];
            Assert.AreEqual("sign3.sco", again.AttachedObjects.Single().AssetPath);
            CollectionAssert.AreEqual(new[] { "Spandau", "10", "extra" }, again.Objects.Single(item => item.Id == 5).Labels);
            StringAssert.Contains("[attachObj]\r\n0\r\nsign3.sco\r\n8\r\n5\r\n", OmsiTextFile.Decode(session.Workspace.ReadAllBytes("tile_0_0.map")));
            Assert.IsNull(session.SetElementType(tile, plate, "sign3.sco"), "the same file changes nothing");

            session.Undo(map, relabelled);
            session.Undo(map, retyped);
            Assert.AreEqual("sign.sco", plate.AssetPath);
            CollectionAssert.AreEqual(new[] { "U Rathaus", "  30 " }, stop.Labels);
            CollectionAssert.AreEqual(tileBytes, session.Workspace.ReadAllBytes("tile_0_0.map"));
            session.Redo(map, retyped);
            session.Redo(map, relabelled);
            Assert.AreEqual("sign3.sco", plate.AssetPath);
            Assert.AreEqual(3, stop.Labels.Count);
        }

        [Test]
        public void ANewTypeOrLabelsOfAnElementOfTheBaseMapGoIntoTheVariantAndLeaveTheBaseMapAlone()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made, out _);
            var tile = map.Tiles[0];
            var plate = tile.AttachedObjects.Single();
            var changeList = @"Chrono\0200_Edit\tile_0_0.map";
            var tileBytes = session.Workspace.ReadAllBytes("tile_0_0.map");

            var retyped = session.SetElementType(tile, plate, "sign3.sco");
            var shown = tile.AttachedObjects.Single();
            Assert.AreNotSame(plate, shown, "a copy takes the shared record's place");
            Assert.AreEqual("sign3.sco", shown.AssetPath);
            Assert.AreEqual("sign.sco", plate.AssetPath);
            Assert.AreEqual("sign.sco", session.Read().Tiles[0].AttachedObjects.Single().AssetPath);
            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(changeList));
            StringAssert.Contains("[selobject]\r\n8\r\n\r\n[typ]\r\nsign3.sco\r\n", text);
            CollectionAssert.AreEqual(tileBytes, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));

            // Later edits of the same element change its block and do not pile up.
            var relabelled = session.SetElementLabels(tile, tile.AttachedObjects.Single(), new[] { "15" });
            var typedAgain = session.SetElementType(tile, tile.AttachedObjects.Single(), "sign4.sco");
            text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(changeList));
            Assert.AreEqual(1, Count(text, "[selobject]\r\n8\r\n"));
            Assert.AreEqual(1, Count(text, "[typ]\r\n"));
            StringAssert.Contains("[typ]\r\nsign4.sco\r\n", text);
            StringAssert.DoesNotContain("sign3.sco", text);
            StringAssert.Contains("[relabel]\r\n1\r\n15\r\n", text);

            // A fresh overlay shows it, and so does a read of the variant by openOMSI's rules.
            var overlaid = OmsiChrono.Overlay(session.Read(), session.Workspace, OmsiChrono.ReadVariants(session.Workspace).FindAll(v => v.Name == made.Name), out _);
            Assert.AreEqual("sign4.sco", overlaid.Tiles[0].AttachedObjects.Single().AssetPath);
            CollectionAssert.AreEqual(new[] { "15" }, overlaid.Tiles[0].AttachedObjects.Single().Labels);

            session.Undo(map, typedAgain);
            session.Undo(map, relabelled);
            session.Undo(map, retyped);
            Assert.IsFalse(session.Workspace.FileExists(changeList));
            Assert.AreSame(plate, tile.AttachedObjects.Single());
            session.Redo(map, retyped);
            Assert.AreEqual("sign3.sco", tile.AttachedObjects.Single().AssetPath);
        }

        [Test]
        public void ANewTypeOrLabelsOfAnObjectTheVariantAddedItselfChangeItsOwnRecord()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made, out _);
            var tile = map.Tiles[0];
            var placed = session.PlaceObject(map, tile, "barrier.sco", 50, 60, 0, 90);

            var retyped = session.SetElementType(tile, placed.Record, "barrier2.sco");
            var relabelled = session.SetElementLabels(tile, placed.Record, new[] { "A" });

            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(@"Chrono\0200_Edit\tile_0_0.map"));
            StringAssert.Contains("[object]\r\n0\r\nbarrier2.sco\r\n20\r\n50\r\n60\r\n", text);
            StringAssert.Contains("\r\n1\r\nA\r\n", text);
            StringAssert.DoesNotContain("[selobject]", text);
            Assert.AreEqual("barrier2.sco", placed.Record.AssetPath);
            CollectionAssert.AreEqual(new[] { "A" }, placed.Record.Labels);
            Assert.AreSame(placed.Record, relabelled.CurrentRecord);

            session.Undo(map, relabelled);
            session.Undo(map, retyped);
            Assert.AreEqual("barrier.sco", placed.Record.AssetPath);
            Assert.AreEqual(0, placed.Record.Labels.Count);
        }

        [Test]
        public void MovingAnObjectInAVariantTakesItsNewLabelsAndTheNewTypeOfWhatHangsOnItAlong()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made, out _);
            var tile = map.Tiles[0];
            session.SetElementType(tile, tile.AttachedObjects.Single(), "sign3.sco");
            session.SetElementLabels(tile, tile.AttachedObjects.Single(), new[] { "15" });
            session.SetElementLabels(tile, tile.Objects.Single(item => item.Id == 5), new[] { "Nordspitze", "20" });

            var result = session.CopyInEditedVariant(map, tile, tile.Objects.Single(item => item.Id == 5), 70, 80, 0, 0, 0, 0);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Blockers));
            var overlaid = OmsiChrono.Overlay(session.Read(), session.Workspace, OmsiChrono.ReadVariants(session.Workspace).FindAll(v => v.Name == made.Name), out _);
            var moved = overlaid.Tiles[0].Objects.Single(item => item.AssetPath == "stop.sco");
            CollectionAssert.AreEqual(new[] { "Nordspitze", "20" }, moved.Labels);
            Assert.AreEqual(70.0, moved.LocalX, 1e-9);
            var hanging = overlaid.Tiles[0].AttachedObjects.Single();
            Assert.AreEqual(moved.Id, hanging.ParentId);
            Assert.AreEqual("sign3.sco", hanging.AssetPath);
            CollectionAssert.AreEqual(new[] { "15" }, hanging.Labels);
            // What the editor shows right after the move is the same.
            CollectionAssert.AreEqual(new[] { "Nordspitze", "20" }, tile.Objects.Single(item => item.AssetPath == "stop.sco").Labels);
            Assert.AreEqual("sign3.sco", tile.AttachedObjects.Single().AssetPath);
            CollectionAssert.AreEqual(new[] { "15" }, tile.AttachedObjects.Single().Labels);
        }

        [Test]
        public void ANewTypeCanStillBeUndoneAfterTheObjectWasMovedAndMovedBack()
        {
            var map = session.Read();
            var tile = map.Tiles[0];
            var stop = tile.Objects.Single(item => item.Id == 5);
            var tree = tile.Objects.Single(item => item.Id == 6);
            var plate = tile.AttachedObjects.Single();
            var tileBytes = session.Workspace.ReadAllBytes("tile_0_0.map");

            // The plate hangs on the stop; the editor's value-based undo of a move writes the old values again.
            var retypedPlate = session.SetElementType(tile, plate, "sign3.sco");
            var retypedTree = session.SetElementType(tile, tree, "oak.sco");
            Assert.IsTrue(session.UpdateObject(tile, stop, stop.LocalX + 5, stop.LocalY + 2, stop.Height, stop.HeadingDegrees));
            Assert.IsTrue(session.UpdateObject(tile, tree, tree.LocalX + 5, tree.LocalY, tree.Height, tree.HeadingDegrees));
            Assert.IsTrue(session.UpdateObject(tile, stop, 10, 20, 0, 0));
            Assert.IsTrue(session.UpdateObject(tile, tree, 30, 40, 0, 0));

            Assert.DoesNotThrow(() => session.Undo(map, retypedTree));
            Assert.DoesNotThrow(() => session.Undo(map, retypedPlate));
            CollectionAssert.AreEqual(tileBytes, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual("sign.sco", plate.AssetPath);
            Assert.AreEqual("tree.sco", tree.AssetPath);
        }

        [Test]
        public void TheOverlayLeavesTheBaseMapsRecordsOfEveryKindAlone()
        {
            var baseMap = session.Read();
            var tile = baseMap.Tiles[0];

            // The fixture's variant changes the type of an object, an attached object and a row, and the labels of two.
            var overlaid = OmsiChrono.Overlay(baseMap, session.Workspace, OmsiChrono.ReadVariants(session.Workspace), out _);
            Assert.AreEqual("sign2.sco", overlaid.Tiles[0].AttachedObjects.Single().AssetPath);

            Assert.AreEqual("sign.sco", tile.AttachedObjects.Single().AssetPath);
            CollectionAssert.AreEqual(new[] { "13" }, tile.AttachedObjects.Single().Labels);
            Assert.AreEqual("lamp.sco", tile.SplineAttachments.Single().AssetPath);
            Assert.AreEqual("tree.sco", tile.Objects.Single(item => item.Id == 6).AssetPath);
            CollectionAssert.AreEqual(new[] { "U Rathaus", "  30 " }, tile.Objects.Single(item => item.Id == 5).Labels);
            Assert.AreEqual("sign.sco", session.Read().Tiles[0].AttachedObjects.Single().AssetPath);
        }

        [Test]
        public void ALabelThatTheTilesEncodingCannotHoldIsRefusedBeforeAnythingIsWritten()
        {
            // A base tile in Windows-1252 (its "ß" is no valid UTF-8, so it is read as 1252) holds "ß" and "é" but not "ł".
            var tile1252 = string.Join("\r\n", "[version]", "14", "",
                "[object]", "0", "stop.sco", "5", "10", "20", "0", "0", "0", "0", "1", "Galenstraße", "") + "\r\n";
            File.WriteAllBytes(Path.Combine(original, "tile_0_0.map"), OmsiTextFile.Encode(tile1252, OmsiTextEncoding.Windows1252));
            var map = session.Read();
            var tile = map.Tiles[0];
            var stop = tile.Objects.Single();
            var before = session.Workspace.ReadAllBytes("tile_0_0.map");

            var refused = Assert.Throws<ArgumentException>(() => session.SetElementLabels(tile, stop, new[] { "Łódź" }));
            StringAssert.Contains("Windows-1252", refused.Message);
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));
            CollectionAssert.AreEqual(new[] { "Galenstraße" }, stop.Labels);

            Assert.IsNotNull(session.SetElementLabels(tile, stop, new[] { "Hauptstraße é" }));
            CollectionAssert.AreEqual(new[] { "Hauptstraße é" }, stop.Labels);
            var written = session.Workspace.ReadAllBytes("tile_0_0.map");
            Assert.IsTrue(written.Contains((byte)0xDF), "ß is one byte in Windows-1252");
            Assert.IsTrue(written.Contains((byte)0xE9), "é is one byte in Windows-1252");
        }

        [Test]
        public void RefusesElementsThatAreNotInTheTileAndEmptyInput()
        {
            var tile = session.Read().Tiles[0];

            Assert.Throws<ArgumentException>(() => session.SetElementType(tile, new OmsiObjectRecord { Id = 99, AssetPath = "x.sco" }, "y.sco"));
            Assert.Throws<ArgumentException>(() => session.SetElementType(tile, "not an element", "y.sco"));
            Assert.Throws<ArgumentException>(() => session.SetElementType(tile, tile.Objects[0], " "));
            Assert.Throws<ArgumentException>(() => session.SetElementLabels(tile, tile.Objects[0], new string[0]));
            Assert.Throws<ArgumentException>(() => session.SetElementLabels(tile, tile.Objects[0], new[] { "two\r\nlines" }));
            Assert.IsNull(session.SetElementLabels(tile, tile.Objects[0], tile.Objects[0].Labels));
        }

        private OmsiMapSummary Overlay(out OmsiChronoOverlayReport report)
        {
            return OmsiChrono.Overlay(session.Read(), session.Workspace, OmsiChrono.ReadVariants(session.Workspace), out report);
        }

        /// <summary>Lays the variants up to <paramref name="variant"/> over the base map and edits the variant there.</summary>
        private OmsiMapSummary Edit(OmsiChronoVariant variant, out OmsiChronoOverlayReport report)
        {
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var day = variant.FirstDay ?? DateTime.Today;
            var upTo = variants.FindAll(v => string.CompareOrdinal(v.Name, variant.Name) <= 0 && v.IsActiveOn(day));
            var map = OmsiChrono.Overlay(session.Read(), session.Workspace, upTo, out report);
            session.EditChronoVariant(report.Variants.Single(v => v.Name == variant.Name), report);
            return map;
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
