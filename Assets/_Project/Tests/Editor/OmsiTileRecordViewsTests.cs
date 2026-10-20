using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTileRecordViewsTests
    {
        private static readonly string[] Version14Tile =
        {
            "[version]", "14", "",
            "[object]", "0", @"Sceneryobjects\Generic\bus_stop.sco", "108", "25.5", "243.25", "1.5", "90", "0", "0", "2", "13N", "", "",
            "Object Nr. 1",
            "[attachObj]", "0", @"Sceneryobjects\plate.sco", "109", "108", "0", "1", "0", "0", "0", "1", "31", "",
            "[spline]", "0", @"Splines\road.sli", "12", "0", "13", "122.707653733384", "0", "228.94386736844", "407.409774380974", "50", "0",
            "1", "-1", "0.5", "0", "0", "0", "50", "mirror", "",
            "[spline_h]", "0", @"Splines\rail.sli", "13", "12", "0", "159.5", "30", "262.8", "47.4", "80", "-100",
            "0", "0", "-0.25", "0", "0", "0", "0", "0", "",
            "[splineAttachement]", "1", @"Sceneryobjects\lamp.sco", "200", "0", "7.5", "0", "20", "180", "0", "0", "40", "1000", "0", "1", "LBL", "",
            "[splineAttachement_repeater]", "0", "3", "19", @"Sceneryobjects\lamp.sco", "200", "1", "7.5", "0", "20", "180", "0", "0", "40", "1000", "0", "0", "",
            "[rule]", "0", "trafficdensity", "1.000", "0", ""
        };

        [Test]
        public void NumberFormatMatchesTheOmsiEditor()
        {
            Assert.AreEqual("122.707653733384", OmsiNumberFormat.Format(122.707653733384));
            Assert.AreEqual("299.999995292025", OmsiNumberFormat.Format(299.999995292025));
            Assert.AreEqual("-3.59590604939723E-6", OmsiNumberFormat.Format(-3.59590604939723E-6));
            Assert.AreEqual("1.42108605124968E-12", OmsiNumberFormat.Format(1.42108605124968E-12));
            Assert.AreEqual("1E20", OmsiNumberFormat.Format(1e20));
            Assert.AreEqual("1.33514401335476E-5", OmsiNumberFormat.Format(1.33514401335476E-5));
            Assert.AreEqual("300", OmsiNumberFormat.Format(300.0));
            Assert.AreEqual("0", OmsiNumberFormat.Format(-0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => OmsiNumberFormat.Format(double.NaN));
        }

        [Test]
        public void ReadsObjectsAndAttachedObjects()
        {
            var file = Parse(Version14Tile);

            var item = OmsiTileRecords.Of<OmsiObjectView>(file).Single();
            Assert.AreEqual(108, item.Id);
            Assert.AreEqual(@"Sceneryobjects\Generic\bus_stop.sco", item.AssetPath);
            Assert.AreEqual(25.5, item.X, 1e-12);
            Assert.AreEqual(243.25, item.Y, 1e-12);
            Assert.AreEqual(1.5, item.Z, 1e-12);
            Assert.AreEqual(90.0, item.HeadingDegrees, 1e-12);
            CollectionAssert.AreEqual(new[] { "13N", "" }, item.Labels);

            var attached = OmsiTileRecords.Of<OmsiAttachedObjectView>(file).Single();
            Assert.AreEqual(108, attached.ParentId);
            Assert.AreEqual(1, attached.AttachPointIndex);
            CollectionAssert.AreEqual(new[] { "31" }, attached.Labels);
        }

        [Test]
        public void ReadsBothSplineKindsWithTheirShiftedFields()
        {
            var splines = OmsiTileRecords.Of<OmsiSplineView>(Parse(Version14Tile)).ToList();
            var road = splines[0];
            var rail = splines[1];

            Assert.IsFalse(road.IsSplineH);
            Assert.AreEqual(13, road.NextId);
            Assert.AreEqual(50.0, road.LengthMeters, 1e-12);
            Assert.AreEqual(0.5, road.CantStart, 1e-12);
            Assert.AreEqual(0.0, road.SkewStart.Value, 1e-12);
            Assert.AreEqual(50.0, road.AlignmentLength, 1e-12);
            Assert.IsTrue(road.IsMirrored);
            // A [spline] derives its height change from the gradients: 50 m * (1 % - 1 %) / 2.
            Assert.AreEqual(0.0, road.HeightDifference, 1e-12);

            Assert.IsTrue(rail.IsSplineH);
            Assert.AreEqual(-100.0, rail.RadiusMeters, 1e-12);
            Assert.AreEqual(-0.25, rail.HeightDifference, 1e-12);
            Assert.AreEqual(29.75, rail.EndHeight, 1e-12);
            Assert.AreEqual(0.0, rail.CantStart, 1e-12);
            Assert.IsFalse(rail.IsMirrored);
        }

        [Test]
        public void ReadsOlderSplinesWithoutSkew()
        {
            var file = Parse("[version]", "12", "",
                "[spline]", "0", "road.sli", "5", "0", "0", "1", "2", "3", "90", "10", "0", "0", "0", "0.2", "0.3", "10", "");

            var spline = OmsiTileRecords.Of<OmsiSplineView>(file).Single();

            Assert.AreEqual(0.2, spline.CantStart, 1e-12);
            Assert.AreEqual(0.3, spline.CantEnd, 1e-12);
            Assert.IsNull(spline.SkewStart);
            Assert.AreEqual(10.0, spline.AlignmentLength, 1e-12);
        }

        [Test]
        public void ReadsAttachmentsAndRepeatersWithTheirOffset()
        {
            var attachments = OmsiTileRecords.Of<OmsiSplineAttachmentView>(Parse(Version14Tile)).ToList();
            var master = attachments[0];
            var repeater = attachments[1];

            Assert.IsFalse(master.IsRepeater);
            Assert.AreEqual(200, master.Id);
            Assert.AreEqual(0, master.SplineIndex);
            Assert.AreEqual(7.5, master.LateralOffset, 1e-12);
            Assert.AreEqual(40.0, master.Interval, 1e-12);
            CollectionAssert.AreEqual(new[] { "LBL" }, master.Strings);
            Assert.Throws<InvalidOperationException>(() => _ = master.MasterTileIndex);

            Assert.IsTrue(repeater.IsRepeater);
            Assert.AreEqual(3, repeater.MasterTileIndex);
            Assert.AreEqual(19, repeater.FirstObjectNumber);
            Assert.AreEqual(200, repeater.Id);
            Assert.AreEqual(1, repeater.SplineIndex);
            Assert.AreEqual(20.0, repeater.StartDistance, 1e-12);
        }

        [Test]
        public void SettersChangeOneLineInOmsiNumberFormat()
        {
            var original = Utf16(Version14Tile);
            var file = OmsiRecordFile.Parse(original, OmsiTileRecordLayouts.Instance);
            var item = OmsiTileRecords.Of<OmsiObjectView>(file).Single();

            item.X = 30.125;
            item.SetLabel(1, "92");

            var expected = string.Join("\r\n", Version14Tile).Replace("\r\n25.5\r\n", "\r\n30.125\r\n").Replace("\r\n13N\r\n\r\n", "\r\n13N\r\n92\r\n") + "\r\n";
            Assert.AreEqual(expected, OmsiTextFile.Decode(file.ToBytes()));
            Assert.Throws<ArgumentOutOfRangeException>(() => item.SetLabel(2, "x"));
            Assert.Throws<InvalidOperationException>(() => OmsiTileRecords.Of<OmsiSplineView>(file).First().HeightDifference = 1.0);
        }

        [Test]
        public void CreatesViewsOnlyForCompleteRecordsWithVerifiedLayouts()
        {
            var file = Parse(Version14Tile);

            Assert.IsNull(OmsiTileRecords.Create(file, file.Records.Single(record => record.Name == "rule")));
            Assert.IsNull(OmsiTileRecords.Create(file, file.Records[0]));
            CollectionAssert.AreEqual(new[] { 108, 109, 12, 13, 200 }, OmsiTileRecords.DefinedIds(file).ToArray());
        }

        [Test]
        public void AllocatesAboveTheStoredNextIdAndTheHighestId()
        {
            var fresh = new OmsiIdAllocator(8978, new[] { 5, 8977 });
            var stale = new OmsiIdAllocator(92, new[] { 1, 92, 94 });

            Assert.IsFalse(fresh.IsNextIdCodeStale);
            Assert.AreEqual(8978, fresh.Allocate());
            Assert.AreEqual(8979, fresh.Allocate());
            Assert.AreEqual(8980, fresh.NextFreeId);

            Assert.IsTrue(stale.IsNextIdCodeStale);
            Assert.AreEqual(94, stale.HighestUsedId);
            Assert.AreEqual(95, stale.Allocate());
            Assert.IsTrue(stale.IsInUse(95));
            Assert.AreEqual(1, new OmsiIdAllocator(0, new int[0]).Allocate());
        }

        [Test]
        public void ForMapReadsBaseAndChronoTilesAndUpdatesNextIdCode()
        {
            var folder = Path.Combine(Path.GetTempPath(), "mapforge-ids-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "Chrono", "0100_Test"));
            try
            {
                var config = new[] { "[name]", "Test", "", "[NextIDCode]", "150", "", "[map]", "0", "0", "tile_0_0.map", "" };
                File.WriteAllBytes(Path.Combine(folder, "global.cfg"), Utf16(config));
                File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), Utf16(Version14Tile));
                File.WriteAllBytes(Path.Combine(folder, "Chrono", "0100_Test", "tile_0_0.map"),
                    Utf16("[version]", "14", "", "[object]", "0", "a.sco", "160", "1", "2", "0", "0", "0", "0", "0", ""));

                var allocator = OmsiIdAllocator.ForMap(Path.Combine(folder, "global.cfg"));

                // Base tile IDs 12, 13, 108, 109 and 200 plus chrono ID 160; the repeater reuses 200.
                Assert.AreEqual(150, allocator.NextIdCode);
                Assert.AreEqual(200, allocator.HighestUsedId);
                Assert.AreEqual(6, allocator.UsedIdCount);
                Assert.IsTrue(allocator.IsInUse(160));
                Assert.IsTrue(allocator.IsNextIdCodeStale);
                Assert.AreEqual(201, allocator.Allocate());

                var globalConfig = OmsiRecordFile.Parse(Utf16(config), OmsiGlobalConfigLayouts.Instance);
                allocator.ApplyTo(globalConfig);
                Assert.AreEqual(string.Join("\r\n", config).Replace("\r\n150\r\n", "\r\n202\r\n") + "\r\n",
                    OmsiTextFile.Decode(globalConfig.ToBytes()));
                Assert.Throws<InvalidOperationException>(() =>
                    allocator.ApplyTo(OmsiRecordFile.Parse(Utf16("[name]", "Test"), OmsiGlobalConfigLayouts.Instance)));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static OmsiRecordFile Parse(params string[] lines)
        {
            return OmsiRecordFile.Parse(Utf16(lines), OmsiTileRecordLayouts.Instance);
        }

        private static byte[] Utf16(params string[] lines)
        {
            return OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian);
        }
    }
}
