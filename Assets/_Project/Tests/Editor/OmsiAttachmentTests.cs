using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiAttachmentTests
    {
        private string folder;

        [SetUp]
        public void CreateMap()
        {
            folder = Path.Combine(Path.GetTempPath(), "mapforge-attach-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode(
                "[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
            // Splines 10 and 11 run north, 20 m each, at height 30; 12 continues from 11's end but is stored the other
            // way round, from its own start 20 m further north back to the joint. 10 names no neighbour; 11 names 10.
            // A row of lamps on spline 11 (index 1) starts 5 m from the beginning of the chain, which is spline 10.
            File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), OmsiTextFile.Encode(string.Join("\r\n",
                "[version]", "14", "",
                "[object]", "0", "mast.sco", "1", "10", "10", "0", "90", "0", "0", "0", "",
                "[attachObj]", "0", "lamp.sco", "2", "1", "0", "1", "180", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "10", "0", "0", "100", "30", "100", "0", "20", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "11", "10", "0", "100", "30", "120", "0", "20", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "12", "0", "11", "100", "30", "160", "180", "20", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "3", "1", "4", "0.5", "5", "90", "0", "0", "10", "40", "0", "0", "",
                "[splineAttachement_repeater]", "0", "0", "2", "lamp.sco", "3", "2", "4", "0.5", "5", "90", "0", "0", "10", "40", "0", "0", "") + "\r\n",
                OmsiTextEncoding.Utf16LittleEndian));
        }

        [TearDown]
        public void DeleteMap()
        {
            Directory.Delete(folder, true);
        }

        [Test]
        public void ReadsAttachedObjectsAndSplineAttachments()
        {
            var tile = OmsiMapReader.Read(Path.Combine(folder, "global.cfg")).Tiles[0];

            var attached = tile.AttachedObjects.Single();
            Assert.AreEqual(2, attached.Id);
            Assert.AreEqual(1, attached.ParentId);
            Assert.AreEqual(1, attached.AttachPointIndex);
            Assert.AreEqual(180.0, attached.HeadingDegrees);
            Assert.AreEqual(2, tile.SplineAttachments.Count);
            var row = tile.SplineAttachments[0];
            Assert.IsFalse(row.IsRepeater);
            Assert.AreEqual(1, row.SplineIndex);
            Assert.AreEqual(4.0, row.LateralOffset);
            Assert.AreEqual(10.0, row.Interval);
            Assert.AreEqual(40.0, row.Range);
            Assert.IsTrue(tile.SplineAttachments[1].IsRepeater);
            Assert.AreEqual(2, tile.SplineAttachments[1].SplineIndex);
            Assert.AreEqual(3, tile.AttachmentCount);
        }

        [Test]
        public void PlacesRowsFromTheChainStartAcrossReversedNeighbours()
        {
            var summary = OmsiMapReader.Read(Path.Combine(folder, "global.cfg"));
            var tile = summary.Tiles[0];
            var layout = new OmsiSplineAttachmentLayout(summary);

            var lamps = layout.Place(tile, tile.SplineAttachments[0]);

            // 5, 15, 25, 35 and 45 m from the start of spline 10: the chain runs north through 10, 11 and the reversed 12.
            Assert.AreEqual(5, lamps.Count);
            CollectionAssert.AreEqual(new[] { 105.0, 115.0, 125.0, 135.0, 145.0 }, lamps.Select(lamp => Math.Round(lamp.WorldY, 6)).ToArray());
            // 4 m to the right of a northbound chain is east; the heading adds the attachment's 90 degrees.
            Assert.IsTrue(lamps.All(lamp => Math.Abs(lamp.WorldX - 104.0) < 1e-6));
            Assert.IsTrue(lamps.All(lamp => Math.Abs(lamp.HeadingDegrees - 90.0) < 1e-6));
            Assert.AreEqual(30.5, lamps[0].Height, 1e-9);
            CollectionAssert.IsEmpty(layout.Place(tile, tile.SplineAttachments[1]));
        }

        [Test]
        public void ReadsAttachmentPointSteps()
        {
            var sco = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(string.Join("\r\n",
                "[mesh]", "mast.o3d", "",
                "[new_attachment]", "", "attach_rot_z", "90", "", "attach_trans", "12", "0", "3.5", "",
                "[new_attachment]", "", "attach_trans", "0", "-12", "0", "")));

            Assert.AreEqual(2, sco.AttachPoints.Count);
            Assert.AreEqual(2, sco.AttachPoints[0].Steps.Count);
            Assert.AreEqual("attach_rot_z", sco.AttachPoints[0].Steps[0].Kind);
            Assert.AreEqual(90.0, sco.AttachPoints[0].Steps[0].X);
            Assert.AreEqual(3.5, sco.AttachPoints[0].Steps[1].Z);
            Assert.AreEqual(-12.0, sco.AttachPoints[1].Steps.Single().Y);
        }
    }
}
