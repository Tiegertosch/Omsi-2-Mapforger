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
            // way round, from its own start 20 m further north back to the joint. 10 names no neighbour; 11 names 10 and
            // stores that its chain began 20 m before it; 12 begins its own chain. A row of lamps on spline 10 (index 0)
            // starts 5 m along the chain; a continuation on spline 12 (index 2) counts from 12's start.
            File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), OmsiTextFile.Encode(string.Join("\r\n",
                "[version]", "14", "",
                "[object]", "0", "mast.sco", "1", "10", "10", "0", "90", "0", "0", "0", "",
                "[attachObj]", "0", "lamp.sco", "2", "1", "0", "1", "180", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "10", "0", "0", "100", "30", "100", "0", "20", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "11", "10", "0", "100", "30", "120", "0", "20", "0", "0", "0", "0", "0", "0", "0", "20", "",
                "[spline]", "0", "road.sli", "12", "0", "11", "100", "30", "160", "180", "20", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "3", "0", "4", "0.5", "5", "90", "0", "0", "10", "40", "0", "0", "",
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
            Assert.AreEqual(0, row.SplineIndex);
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
            Assert.AreSame(tile.Splines[2], lamps[4].Spline);
            Assert.AreEqual(15.0, lamps[4].AlongSpline, 1e-9);

            // The continuation lays the row on as openOMSI does, from 12's own start south, 4 m to its right, the west.
            var continued = layout.Place(tile, tile.SplineAttachments[1]);
            CollectionAssert.AreEqual(new[] { 155.0, 145.0, 135.0, 125.0, 115.0 }, continued.Select(lamp => Math.Round(lamp.WorldY, 6)).ToArray());
            Assert.IsTrue(continued.All(lamp => Math.Abs(lamp.WorldX - 96.0) < 1e-6));

            // A record on spline 11 whose start lies before 11, 20 m into its chain, is not drawn, as in Omsi.exe.
            var early = new OmsiSplineAttachmentRecord { AssetPath = "lamp.sco", SplineIndex = 1, LateralOffset = 4, StartDistance = 5, Interval = 10, Range = 40 };
            CollectionAssert.IsEmpty(layout.Place(tile, early));
            early.StartDistance = 25;
            Assert.AreEqual(125.0, layout.Place(tile, early)[0].WorldY, 1e-6);
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
