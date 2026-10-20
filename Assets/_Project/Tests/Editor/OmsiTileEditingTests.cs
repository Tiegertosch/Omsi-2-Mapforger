using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTileEditingTests
    {
        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-editing-" + Guid.NewGuid().ToString("N"), "Testmap");
            Directory.CreateDirectory(Path.Combine(mapFolder, "TTData"));
            Directory.CreateDirectory(Path.Combine(mapFolder, "Chrono", "0100_Test"));
            // Entry points: object index 4 in tile 0 (object 40) and object index 0 in tile 1.
            WriteUtf16("global.cfg", "[friendlyname]", "Test", "", "[NextIDCode]", "100", "",
                "[entrypoints]", "2",
                "4", "40", "0", "1", "0", "2", "0", "0", "0", "1", "0", "Depot",
                "0", "90", "0", "1", "0", "2", "0", "0", "0", "1", "1", "Other tile", "",
                "[map]", "0", "0", "tile_0_0.map", "",
                "[map]", "1", "0", "tile_1_0.map", "");
            WriteUtf16("tile_0_0.map",
                "[version]", "14", "",
                "[object]", "0", "tree.sco", "10", "1", "1", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "trafficdensity", "1.000", "0", "",
                "[varparent]", "20", "",
                "[object]", "0", "crossing.sco", "20", "5", "5", "0", "0", "0", "0", "0", "",
                "[attachObj]", "0", "plate.sco", "30", "20", "0", "1", "0", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "50", "0", "51", "0", "0", "0", "90", "10", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "51", "50", "0", "10", "0", "0", "90", "10", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", "path.sli", "52", "0", "0", "0", "50", "0", "0", "10", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "60", "2", "1", "0", "0", "0", "0", "0", "10", "100", "0", "0", "",
                "[object]", "0", "entrypoint_bus.sco", "40", "1", "2", "0", "0", "0", "0", "0", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "", "[object]", "0", "entrypoint_bus.sco", "90", "1", "2", "0", "0", "0", "0", "0", "");
            WriteUtf8(Path.Combine("TTData", "Busstops.cfg"), "[busstop]", "Stop", "1", "90", "0.0", "0", "0", "");
            WriteUtf8(Path.Combine("TTData", "Line.ttr"), "[track_entry]", "51", "0", "0", "3", "10.0", "0", "");
            WriteUtf16(Path.Combine("Chrono", "0100_Test", "tile_0_0.map"), "[version]", "14", "", "[selobject]", "10", "", "[delete]", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            Directory.Delete(Path.GetDirectoryName(mapFolder), true);
        }

        [Test]
        public void IndexesReferencesFromTilesChronoEntryPointsAndTimetables()
        {
            var index = OmsiReferenceIndex.Build(new OmsiFolderSource(mapFolder));

            CollectionAssert.AreEquivalent(new[] { "chrono selection" }, index.To(10).Select(reference => reference.Kind));
            CollectionAssert.AreEquivalent(new[] { "attached object parent", "variable parent" }, index.To(20).Select(reference => reference.Kind));
            CollectionAssert.AreEquivalent(new[] { "spline neighbour", "track entry" }, index.To(51).Select(reference => reference.Kind));
            CollectionAssert.AreEquivalent(new[] { "attachment on spline" }, index.To(52).Select(reference => reference.Kind));
            CollectionAssert.AreEquivalent(new[] { "entry point" }, index.To(40).Select(reference => reference.Kind));
            CollectionAssert.AreEquivalent(new[] { "entry point", "bus stop" }, index.To(90).Select(reference => reference.Kind));
            Assert.AreEqual(0, index.To(30).Count);
        }

        [Test]
        public void RemovesAnUnreferencedObjectWithItsModifiersAndShiftsEntryPoints()
        {
            var source = new OmsiFolderSource(mapFolder);
            var tile = Load("tile_0_0.map", OmsiTileRecordLayouts.Instance);
            var config = Load("global.cfg", OmsiGlobalConfigLayouts.Instance);
            var plate = OmsiTileRecords.Of<OmsiAttachedObjectView>(tile).Single();

            var result = OmsiTileEditing.Remove(tile, plate, 0, config, OmsiReferenceIndex.Build(source));

            Assert.IsTrue(result.Removed);
            Assert.AreEqual(1, result.RemovedRecordCount);
            Assert.AreEqual(1, result.ShiftedEntryPointIndices);
            var entryPoints = config.Records.Single(record => record.Name == "entrypoints");
            Assert.AreEqual("3", entryPoints.GetField(1));
            Assert.AreEqual("0", entryPoints.GetField(13));
            Assert.IsFalse(OmsiTileRecords.Of<OmsiAttachedObjectView>(Reparse(tile)).Any());
        }

        [Test]
        public void RemovesTheRulesAndVariablesThatFollowAnElement()
        {
            // Object 10 has a [rule] and a [varparent] after it; only the chrono patch refers to it.
            var tile = Load("tile_0_0.map", OmsiTileRecordLayouts.Instance);
            var config = Load("global.cfg", OmsiGlobalConfigLayouts.Instance);
            var tree = OmsiTileRecords.Of<OmsiObjectView>(tile).First();
            var noReferences = OmsiReferenceIndex.Build(new OmsiFolderSource(mapFolder));
            Assert.AreEqual(1, OmsiTileEditing.Remove(tile, tree, 0, config, noReferences).Blockers.Count);

            File.Delete(Path.Combine(mapFolder, "Chrono", "0100_Test", "tile_0_0.map"));
            var result = OmsiTileEditing.Remove(tile, tree, 0, config, OmsiReferenceIndex.Build(new OmsiFolderSource(mapFolder)));

            Assert.IsTrue(result.Removed);
            Assert.AreEqual(3, result.RemovedRecordCount);
            CollectionAssert.AreEqual(new[] { "version", "object", "attachObj", "spline", "spline", "spline", "splineAttachement", "object" },
                Reparse(tile).Records.Select(record => record.Name).ToArray());
            Assert.AreEqual("3", config.Records.Single(record => record.Name == "entrypoints").GetField(1));
        }

        [Test]
        public void RefusesToRemoveReferencedElementsAndLeavesFilesUnchanged()
        {
            var tile = Load("tile_0_0.map", OmsiTileRecordLayouts.Instance);
            var config = Load("global.cfg", OmsiGlobalConfigLayouts.Instance);
            var tileBytes = tile.ToBytes();
            var configBytes = config.ToBytes();
            var index = OmsiReferenceIndex.Build(new OmsiFolderSource(mapFolder));

            var crossing = OmsiTileEditing.Remove(tile, OmsiTileRecords.Of<OmsiObjectView>(tile).Single(o => o.Id == 20), 0, config, index);
            var entryPoint = OmsiTileEditing.Remove(tile, OmsiTileRecords.Of<OmsiObjectView>(tile).Single(o => o.Id == 40), 0, config, index);
            var trackSpline = OmsiTileEditing.Remove(tile, OmsiTileRecords.Of<OmsiSplineView>(tile).Single(s => s.Id == 51), 0, config, index);

            Assert.IsFalse(crossing.Removed);
            Assert.AreEqual(2, crossing.Blockers.Count);
            Assert.AreEqual("entry point", entryPoint.Blockers.Single().Kind);
            CollectionAssert.AreEquivalent(new[] { "spline neighbour", "track entry" }, trackSpline.Blockers.Select(blocker => blocker.Kind));
            CollectionAssert.AreEqual(tileBytes, tile.ToBytes());
            CollectionAssert.AreEqual(configBytes, config.ToBytes());
        }

        [Test]
        public void RemovingASplineShiftsTheSplineIndexOfLaterAttachments()
        {
            // Remove the unreferenced first spline after detaching its neighbour reference.
            var tile = Load("tile_0_0.map", OmsiTileRecordLayouts.Instance);
            var config = Load("global.cfg", OmsiGlobalConfigLayouts.Instance);
            OmsiTileRecords.Of<OmsiSplineView>(tile).Single(s => s.Id == 51).PreviousId = 0;
            File.WriteAllBytes(Path.Combine(mapFolder, "tile_0_0.map"), tile.ToBytes());

            var result = OmsiTileEditing.Remove(tile, OmsiTileRecords.Of<OmsiSplineView>(tile).Single(s => s.Id == 50), 0, config,
                OmsiReferenceIndex.Build(new OmsiFolderSource(mapFolder)));

            Assert.IsTrue(result.Removed);
            Assert.AreEqual(1, result.ShiftedAttachmentSplineIndices);
            var attachment = OmsiTileRecords.Of<OmsiSplineAttachmentView>(Reparse(tile)).Single();
            Assert.AreEqual(1, attachment.SplineIndex);
            Assert.AreEqual(52, OmsiTileRecords.Of<OmsiSplineView>(Reparse(tile)).ElementAt(attachment.SplineIndex).Id);
        }

        [Test]
        public void AppendsObjectsWithNewIdsWithoutMovingExistingIndices()
        {
            var tile = Load("tile_0_0.map", OmsiTileRecordLayouts.Instance);
            var ids = OmsiIdAllocator.ForMap(new OmsiFolderSource(mapFolder));
            var before = tile.ToBytes();

            var added = OmsiTileEditing.AppendObject(tile, ids, @"Sceneryobjects\new.sco", 12.5, 7.25, 0, 45, new[] { "A", "" });

            Assert.AreEqual(100, added.Id);
            var reread = OmsiTileRecords.Of<OmsiObjectView>(Reparse(tile)).Last();
            Assert.AreEqual(100, reread.Id);
            Assert.AreEqual(12.5, reread.X, 1e-12);
            Assert.AreEqual(45.0, reread.HeadingDegrees, 1e-12);
            CollectionAssert.AreEqual(new[] { "A", "" }, reread.Labels);
            var text = OmsiTextFile.Decode(tile.ToBytes());
            StringAssert.StartsWith(OmsiTextFile.Decode(before), text);
            Assert.AreEqual(101, ids.NextFreeId);
        }

        [Test]
        public void ChangesLabelCountsAndTheMirrorLineStructurally()
        {
            var tile = Load("tile_0_0.map", OmsiTileRecordLayouts.Instance);
            var tree = OmsiTileRecords.Of<OmsiObjectView>(tile).First();
            var spline = OmsiTileRecords.Of<OmsiSplineView>(tile).First();

            tree.SetLabels(new[] { "13N", "", "92" });
            spline.IsMirrored = true;
            var reread = Reparse(tile);

            CollectionAssert.AreEqual(new[] { "13N", "", "92" }, OmsiTileRecords.Of<OmsiObjectView>(reread).First().Labels);
            Assert.IsTrue(OmsiTileRecords.Of<OmsiSplineView>(reread).First().IsMirrored);
            Assert.AreEqual("rule", reread.Records[2].Name);

            OmsiTileRecords.Of<OmsiObjectView>(reread).First().SetLabels(new string[0]);
            OmsiTileRecords.Of<OmsiSplineView>(reread).First().IsMirrored = false;
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(mapFolder, "tile_0_0.map")), reread.ToBytes());
        }

        [Test]
        public void InsertingAfterAFileWithoutFinalLineBreakKeepsRecordsApart()
        {
            var file = OmsiRecordFile.Parse(Encoding.UTF8.GetBytes("[busstop]\r\nA"), null);

            file.InsertRecord(file.Records.Count, file.CreateRecord("busstop", new[] { "B" }, 0));

            Assert.AreEqual("[busstop]\r\nA\r\n[busstop]\r\nB\r\n", Encoding.UTF8.GetString(file.ToBytes()));
            Assert.Throws<ArgumentException>(() => file.CreateRecord("[bad]", new string[0]));
            Assert.Throws<ArgumentException>(() => file.CreateRecord("busstop", new[] { "a\nb" }));
        }

        private OmsiRecordFile Load(string name, IOmsiRecordLayouts layouts)
        {
            return OmsiRecordFile.Load(Path.Combine(mapFolder, name), layouts);
        }

        private static OmsiRecordFile Reparse(OmsiRecordFile file)
        {
            var reread = OmsiRecordFile.Parse(file.ToBytes(), OmsiTileRecordLayouts.Instance);
            Assert.IsTrue(reread.Records.All(record => record.IsComplete));
            return reread;
        }

        private void WriteUtf16(string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(mapFolder, name), OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }

        private void WriteUtf8(string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(mapFolder, name), Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));
        }
    }
}
