using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The shared Whole road helper: the chain through a spline, narrowed to its road type.</summary>
    public sealed class OmsiWholeRoadTests
    {
        private string folder;

        [TearDown]
        public void DeleteMap()
        {
            if (folder != null && Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        /// <summary>Splines 10, 11 and 12 form one chain (as in the attachment tests); 12 has the given profile; 13 stands alone.</summary>
        private OmsiMapSummary Map(string profileOfTwelve)
        {
            folder = Path.Combine(Path.GetTempPath(), "mapforge-wholeroad-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode("[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
            File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), OmsiTextFile.Encode(string.Join("\r\n",
                "[version]", "14", "",
                "[spline]", "0", "road.sli", "10", "0", "0", "100", "30", "100", "0", "20", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", "Road.SLI", "11", "10", "0", "100", "30", "120", "0", "20", "0", "0", "0", "0", "0", "0", "0", "20", "",
                "[spline]", "0", profileOfTwelve, "12", "0", "11", "100", "30", "160", "180", "20", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", "road.sli", "13", "0", "0", "500", "30", "500", "0", "20", "0", "0", "0", "0", "0", "0", "0", "0", "") + "\r\n",
                OmsiTextEncoding.Utf16LittleEndian));
            return OmsiMapReader.Read(Path.Combine(folder, "global.cfg"));
        }

        private static OmsiSplineRecord Spline(OmsiMapSummary summary, int id)
        {
            return summary.Tiles[0].Splines.Single(spline => spline.Id == id);
        }

        [Test]
        public void TakesEveryPieceOfTheChainWithTheSameProfileRegardlessOfCase()
        {
            var summary = Map("road.sli");
            var pieces = OmsiWholeRoad.Pieces(new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], Spline(summary, 11));
            CollectionAssert.AreEquivalent(new[] { 10, 11, 12 }, pieces.Select(pair => pair.Value.Id).ToArray());
            Assert.IsTrue(pieces.All(pair => ReferenceEquals(pair.Key, summary.Tiles[0])));
        }

        [Test]
        public void LeavesOutPiecesOfAnotherRoadTypeAndPiecesOfOtherChains()
        {
            var summary = Map("other.sli");
            var pieces = OmsiWholeRoad.Pieces(new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], Spline(summary, 10));
            CollectionAssert.AreEquivalent(new[] { 10, 11 }, pieces.Select(pair => pair.Value.Id).ToArray());
            var other = OmsiWholeRoad.Pieces(new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], Spline(summary, 12));
            Assert.AreEqual(new[] { 12 }, other.Select(pair => pair.Value.Id).ToArray());
        }

        [Test]
        public void AStandaloneSplineIsItsOwnWholeRoad()
        {
            var summary = Map("road.sli");
            var pieces = OmsiWholeRoad.Pieces(new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], Spline(summary, 13));
            Assert.AreEqual(new[] { 13 }, pieces.Select(pair => pair.Value.Id).ToArray());
        }

        [Test]
        public void SameAssetIgnoresSlashesCaseAndBlanksAndRejectsNull()
        {
            Assert.IsTrue(OmsiWholeRoad.SameAsset("Roads/Std\\a.sli", " roads\\std/A.SLI "));
            Assert.IsFalse(OmsiWholeRoad.SameAsset("a.sli", "b.sli"));
            Assert.IsFalse(OmsiWholeRoad.SameAsset(null, "a.sli"));
            Assert.IsFalse(OmsiWholeRoad.SameAsset(null, null));
        }

        [Test]
        public void RejectsMissingArguments()
        {
            var summary = Map("road.sli");
            var layout = new OmsiSplineAttachmentLayout(summary);
            Assert.Throws<ArgumentNullException>(() => OmsiWholeRoad.Pieces(null, summary.Tiles[0], Spline(summary, 10)));
            Assert.Throws<ArgumentNullException>(() => OmsiWholeRoad.Pieces(layout, null, Spline(summary, 10)));
            Assert.Throws<ArgumentNullException>(() => OmsiWholeRoad.Pieces(layout, summary.Tiles[0], null));
        }
    }
}
