using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiChronoEditingTests
    {
        private string testRoot;
        private string original;
        private OmsiMapSession session;
        private OmsiMapSummary baseMap;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-chrono-" + Guid.NewGuid().ToString("N"));
            original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(Path.Combine(original, "TTData"));
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "20", "", "[map]", "0", "0", "tile_0_0.map", "");
            // Object 5 is a bus stop with a rule and sign 8 hanging on it; object 6 stands beside it.
            Write(original, "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "stop.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "trafficdensity", "1.000", "0", "",
                "[attachObj]", "0", "sign.sco", "8", "5", "0", "1", "0", "0", "0", "0", "",
                "[object]", "0", "tree.sco", "6", "30", "40", "0", "0", "0", "0", "0", "");
            Write(Path.Combine(original, "TTData"), "Busstops.cfg", "[busstop]", "Nordspitze", "0", "5", "0", "0", "0", "");
            // An existing variant sets a rule on object 6.
            var works = Path.Combine(original, "Chrono", "0100_Works");
            Directory.CreateDirectory(works);
            Write(works, "Chrono.cfg", "[startdate]", "19920401", "", "[enddate]", "19920414", "");
            Write(works, "tile_0_0.map", "[version]", "14", "", "[selobject]", "6", "", "[rule]", "0", "trafficdensity", "0.000", "0", "");
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            baseMap = session.Read();
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void MakesAVariantAfterTheOthersAndDeletesItAgain()
        {
            var made = session.CreateChronoVariant("Road works: east", new DateTime(1993, 5, 1), new DateTime(1993, 6, 30));

            Assert.AreEqual("0200_Road works_ east", made.Name);
            Assert.AreEqual("Road works: east", made.Title);
            Assert.AreEqual(new DateTime(1993, 5, 1), made.Start);
            Assert.AreEqual(new DateTime(1993, 6, 30), made.End);
            Assert.IsTrue(session.IsMadeInProject(made));
            Assert.IsFalse(session.IsMadeInProject(OmsiChrono.ReadVariants(session.Workspace)[0]));
            CollectionAssert.AreEqual(new[] { "0100_Works", "0200_Road works_ east" }, OmsiChrono.ReadVariants(session.Workspace).Select(v => v.Name));
            Assert.IsFalse(File.Exists(Path.Combine(original, "Chrono", "0200_Road works_ east", "Chrono.cfg")));

            session.DeleteChronoVariant(made);
            CollectionAssert.AreEqual(new[] { "0100_Works" }, OmsiChrono.ReadVariants(session.Workspace).Select(v => v.Name));
            Assert.Throws<InvalidOperationException>(() => session.DeleteChronoVariant(OmsiChrono.ReadVariants(session.Workspace)[0]));
        }

        [Test]
        public void AVariantWithoutAnyDateNeverApplies()
        {
            var works = Path.Combine(original, "Chrono", "0300_Undated");
            Directory.CreateDirectory(works);
            Write(works, "Chrono.cfg", "[moneysystem]", @"Money\DM\DM_91.cfg", "");
            var undated = OmsiChrono.ReadVariants(session.Workspace).Single(v => v.Name == "0300_Undated");

            Assert.IsFalse(undated.IsActiveOn(new DateTime(1992, 4, 5)));
            Assert.IsNull(undated.FirstDay);
        }

        [Test]
        public void PlacesMovesAndDeletesObjectsInTheVariantsChangeList()
        {
            var made = session.CreateChronoVariant("Works", new DateTime(1993, 5, 1), null);
            var tile = Edit(made, out _);
            var tileBytes = session.Workspace.ReadAllBytes("tile_0_0.map");

            var placed = session.PlaceObject(baseMap, tile, "barrier.sco", 50, 60, 0, 90);

            Assert.AreEqual(20, placed.Record.Id);
            Assert.IsTrue(session.IsInEditedVariant(placed.Record));
            CollectionAssert.AreEqual(tileBytes, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));
            var changeList = @"Chrono\0200_Works\tile_0_0.map";
            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(changeList), out var encoding);
            Assert.AreEqual(OmsiTextEncoding.Utf16LittleEndian, encoding);
            StringAssert.Contains("[version]\r\n14\r\n", text);
            StringAssert.Contains("[object]\r\n0\r\nbarrier.sco\r\n20\r\n50\r\n60\r\n", text);
            StringAssert.Contains("[NextIDCode]\r\n21", OmsiTextFile.Decode(session.Workspace.ReadAllBytes("global.cfg")));

            // Moving its own object writes it in the change list, not the tile.
            Assert.IsTrue(session.UpdateObject(tile, placed.Record, 55, 65, 0, 180));
            StringAssert.Contains("[object]\r\n0\r\nbarrier.sco\r\n20\r\n55\r\n65\r\n", OmsiTextFile.Decode(session.Workspace.ReadAllBytes(changeList)));
            CollectionAssert.AreEqual(tileBytes, session.Workspace.ReadAllBytes("tile_0_0.map"));
            // Objects of the base map cannot be moved there.
            Assert.Throws<InvalidOperationException>(() => session.UpdateObject(tile, tile.Objects.Single(o => o.Id == 6), 1, 2, 0, 0));

            var overlay = Overlay(made);
            Assert.AreEqual(55.0, overlay.Tiles[0].Objects.Single(o => o.Id == 20).LocalX, 1e-9);

            // Deleting it takes it out of the change list again.
            var deleted = session.DeleteObject(baseMap, tile, placed.Record);
            Assert.IsTrue(deleted.Succeeded, string.Join("; ", deleted.Blockers));
            StringAssert.DoesNotContain("barrier.sco", OmsiTextFile.Decode(session.Workspace.ReadAllBytes(changeList)));
            Assert.IsFalse(tile.Objects.Exists(o => o.Id == 20));

            // Undone in turn, the move first as the editor's history does it.
            session.Undo(baseMap, deleted.Change);
            Assert.IsTrue(session.UpdateObject(tile, placed.Record, 50, 60, 0, 90));
            session.Undo(baseMap, placed);
            Assert.IsFalse(session.Workspace.FileExists(changeList));
            Assert.IsFalse(tile.Objects.Exists(o => o.Id == 20));
            session.Redo(baseMap, placed);
            Assert.IsTrue(session.Workspace.FileExists(changeList));
        }

        [Test]
        public void TakesAwayAnObjectOfTheBaseMapWithWhatHangsOnIt()
        {
            var made = session.CreateChronoVariant("Works", new DateTime(1993, 5, 1), null);
            var tile = Edit(made, out _);
            var stop = tile.Objects.Single(o => o.Id == 5);

            var result = session.DeleteObject(baseMap, tile, stop);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Blockers));
            Assert.AreEqual(1, result.Change.RemovedAttachedObjectCount);
            CollectionAssert.AreEqual(new[] { 6 }, tile.Objects.Select(o => o.Id));
            CollectionAssert.IsEmpty(tile.AttachedObjects);
            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(@"Chrono\0200_Works\tile_0_0.map"));
            StringAssert.Contains("[selobject]\r\n8\r\n\r\n[delete]\r\n", text);
            StringAssert.Contains("[selobject]\r\n5\r\n\r\n[delete]\r\n", text);
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));

            // The map with the variant lacks both; the base map keeps them.
            var overlay = Overlay(made);
            CollectionAssert.AreEqual(new[] { 6 }, overlay.Tiles[0].Objects.Select(o => o.Id));
            CollectionAssert.IsEmpty(overlay.Tiles[0].AttachedObjects);
            CollectionAssert.AreEqual(new[] { 5, 6 }, session.Read().Tiles[0].Objects.Select(o => o.Id));

            session.Undo(baseMap, result.Change);
            CollectionAssert.AreEquivalent(new[] { 5, 6 }, tile.Objects.Select(o => o.Id));
            CollectionAssert.AreEqual(new[] { 8 }, tile.AttachedObjects.Select(a => a.Id));
            Assert.IsFalse(session.Workspace.FileExists(@"Chrono\0200_Works\tile_0_0.map"));
        }

        [Test]
        public void MovesAnObjectOfTheBaseMapAsACopyWithNewIds()
        {
            var made = session.CreateChronoVariant("Works", new DateTime(1993, 5, 1), null);
            var tile = Edit(made, out _);
            var stop = tile.Objects.Single(o => o.Id == 5);

            var result = session.CopyInEditedVariant(baseMap, tile, stop, 12, 25, 0.5, 45, 0, 0);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Blockers));
            var copy = result.Change.Record;
            Assert.AreEqual(20, copy.Id);
            Assert.AreEqual("stop.sco", copy.AssetPath);
            Assert.AreEqual(12.0, copy.LocalX, 1e-9);
            Assert.AreSame(stop, result.Change.ReplacedRecord);
            Assert.IsTrue(session.IsInEditedVariant(copy));
            CollectionAssert.AreEqual(new[] { 20, 6 }, tile.Objects.Select(o => o.Id));
            Assert.AreEqual(21, tile.AttachedObjects.Single().Id);
            Assert.AreEqual(20, tile.AttachedObjects.Single().ParentId);
            // The bus stop still names the old object; the copy is saved anyway and the reference is reported.
            Assert.AreEqual(5, result.Change.StaleReferences.Single().TargetId);

            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(@"Chrono\0200_Works\tile_0_0.map"));
            StringAssert.Contains("[selobject]\r\n5\r\n\r\n[delete]\r\n", text);
            StringAssert.Contains("[selobject]\r\n8\r\n\r\n[delete]\r\n", text);
            StringAssert.Contains("[object]\r\n0\r\nstop.sco\r\n20\r\n12\r\n25\r\n0.5\r\n45\r\n", text);
            StringAssert.Contains("[rule]\r\n0\r\ntrafficdensity\r\n1.000\r\n0\r\n", text);
            StringAssert.Contains("[attachObj]\r\n0\r\nsign.sco\r\n21\r\n20\r\n0\r\n1\r\n", text);

            var overlay = Overlay(made);
            CollectionAssert.AreEquivalent(new[] { 6, 20 }, overlay.Tiles[0].Objects.Select(o => o.Id));
            Assert.AreEqual(20, overlay.Tiles[0].AttachedObjects.Single().ParentId);

            // The copy is the variant's own now, so it moves in place.
            Assert.IsTrue(session.UpdateObject(tile, copy, 14, 25, 0.5, 45));
            Assert.IsTrue(session.UpdateObject(tile, copy, 12, 25, 0.5, 45));

            session.Undo(baseMap, result.Change);
            CollectionAssert.AreEqual(new[] { 5, 6 }, tile.Objects.Select(o => o.Id));
            Assert.AreSame(stop, result.Change.CurrentRecord);
            Assert.AreEqual(8, tile.AttachedObjects.Single().Id);
        }

        [Test]
        public void ObjectsOfAnEarlierVariantAreReplacedThroughTheirOwnChangeList()
        {
            Write(Path.Combine(original, "Chrono", "0100_Works"), "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "cone.sco", "15", "70", "80", "0", "0", "0", "0", "0", "");
            var made = session.CreateChronoVariant("Later works", new DateTime(1992, 4, 10), null);
            var tile = Edit(made, out var report);
            var cone = tile.Objects.Single(o => o.Id == 15);
            Assert.AreEqual("0100_Works", session.OriginOf(cone).Name);
            Assert.IsFalse(session.IsInEditedVariant(cone));

            var result = session.CopyInEditedVariant(baseMap, tile, cone, 75, 80, 0, 0, 0, 0);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Blockers));
            var overlay = Overlay(made);
            Assert.IsFalse(overlay.Tiles[0].Objects.Exists(o => o.Id == 15));
            Assert.AreEqual(75.0, overlay.Tiles[0].Objects.Single(o => o.AssetPath == "cone.sco").LocalX, 1e-9);
            Assert.IsNotNull(report);
        }

        /// <summary>Lays the variants up to <paramref name="variant"/> over the base map and edits the variant there.</summary>
        private OmsiTileSummary Edit(OmsiChronoVariant variant, out OmsiChronoOverlayReport report)
        {
            baseMap = OverlayWithReport(variant, out report);
            session.EditChronoVariant(report.Variants.Single(v => v.Name == variant.Name), report);
            return baseMap.Tiles[0];
        }

        private OmsiMapSummary Overlay(OmsiChronoVariant variant)
        {
            return OverlayWithReport(variant, out _);
        }

        private OmsiMapSummary OverlayWithReport(OmsiChronoVariant variant, out OmsiChronoOverlayReport report)
        {
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var day = variant.FirstDay ?? DateTime.Today;
            var upTo = variants.FindAll(v => string.CompareOrdinal(v.Name, variant.Name) <= 0 && v.IsActiveOn(day));
            return OmsiChrono.Overlay(session.Read(), session.Workspace, upTo, out report);
        }

        private static void Write(string folder, string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(folder, name),
                OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
