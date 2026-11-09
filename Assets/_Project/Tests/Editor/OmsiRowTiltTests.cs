using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiRowTiltTests
    {
        private string folder;

        [SetUp]
        public void CreateMap()
        {
            folder = Path.Combine(Path.GetTempPath(), "mapforge-tilt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode("[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
            // One spline, 20 m north at height 30, rising 10 % from start to end and leaning 5 % (the right side lower); two rows on it: one with the
            // tilt flag 4 m to its right, one without, and one with the flag 11 m to its right, beyond the 10 m the cant acts over.
            File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), OmsiTextFile.Encode(string.Join("\r\n",
                "[version]", "14", "",
                "[spline]", "0", "road.sli", "10", "0", "0", "100", "30", "100", "0", "20", "0", "10", "10", "5", "5", "0", "0", "0", "",
                "[splineAttachement]", "0", "post.sco", "3", "0", "4", "0", "5", "90", "0", "0", "10", "10", "1", "0", "",
                "[splineAttachement]", "0", "post.sco", "4", "0", "4", "0", "5", "90", "0", "0", "10", "10", "0", "0", "",
                "[splineAttachement]", "0", "post.sco", "5", "0", "11", "0", "5", "90", "0", "0", "10", "10", "1", "0", "") + "\r\n",
                OmsiTextEncoding.Utf16LittleEndian));
        }

        [TearDown]
        public void DeleteMap()
        {
            Directory.Delete(folder, true);
        }

        [Test]
        public void TheTiltFlagIsReadFromTheRecord()
        {
            var tile = OmsiMapReader.Read(Path.Combine(folder, "global.cfg")).Tiles[0];
            CollectionAssert.AreEqual(new[] { true, false, true }, tile.SplineAttachments.Select(row => row.Tilt).ToArray());
        }

        [Test]
        public void ObjectsWithTheFlagLieInTheRoadAndOthersStandUpright()
        {
            var summary = OmsiMapReader.Read(Path.Combine(folder, "global.cfg"));
            var tile = summary.Tiles[0];
            var layout = new OmsiSplineAttachmentLayout(summary);

            var tilted = layout.Place(tile, tile.SplineAttachments[0]).First();
            // The road rises 10 %, so the front lifts by atan(0.1); OMSI's pitch is negative then. The cant of 5 % lowers the right side, bank is negative.
            Assert.AreEqual(-Math.Atan(0.10) * 180.0 / Math.PI, tilted.PitchDegrees, 1e-6);
            Assert.AreEqual(-Math.Atan(0.05) * 180.0 / Math.PI, tilted.BankDegrees, 1e-6);
            // The row's own 90 degrees turn the object inside the road's frame; the frame itself runs north.
            Assert.AreEqual(90.0, tilted.TurnDegrees, 1e-9);
            Assert.AreEqual(90.0, tilted.HeadingDegrees, 1e-9);

            var upright = layout.Place(tile, tile.SplineAttachments[1]).First();
            Assert.AreEqual(0.0, upright.PitchDegrees);
            Assert.AreEqual(0.0, upright.BankDegrees);
            Assert.AreEqual(0.0, upright.TurnDegrees);
            Assert.AreEqual(90.0, upright.HeadingDegrees, 1e-9);

            // Beyond the width the cant acts over the object follows the gradient only.
            var outside = layout.Place(tile, tile.SplineAttachments[2]).First();
            Assert.AreEqual(tilted.PitchDegrees, outside.PitchDegrees, 1e-6);
            Assert.AreEqual(0.0, outside.BankDegrees);
        }

        [Test]
        public void ARowWrittenWithTheFlagKeepsItAndOneChangedBackLosesIt()
        {
            var request = new OmsiAttachmentRowRequest { AssetPath = "post.sco", LateralOffset = 4.0, Interval = 10.0, Tilt = true };
            var part = new OmsiAttachmentRowPart { SplineIndex = 0, StartDistance = 5.0, LateralOffset = 4.0, HeadingDegrees = 0.0, Range = 10.0, Count = 2 };
            var record = OmsiAttachmentRows.Record(part, request);
            Assert.IsTrue(record.Tilt);

            var tile = OmsiRecordFile.Parse(OmsiTextFile.Encode("[version]\r\n14\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian), OmsiTileRecordLayouts.Instance);
            var view = OmsiTileEditing.AppendSplineAttachment(tile, new OmsiIdAllocator(20, new int[0]), record);
            Assert.AreEqual(1, view.TiltFlag);
            var again = OmsiRecordFile.Parse(tile.ToBytes(), OmsiTileRecordLayouts.Instance);
            Assert.AreEqual(1, OmsiTileRecords.Of<OmsiSplineAttachmentView>(again).Single().TiltFlag);

            var plain = new OmsiAttachmentRowRequest { AssetPath = "post.sco", LateralOffset = 4.0, Interval = 10.0, Tilt = false };
            Assert.IsFalse(OmsiAttachmentRows.Same(request, plain), "Switching the flag is a change of the row.");
            Assert.IsFalse(OmsiAttachmentRows.Record(part, plain).Tilt);
        }
    }
}
