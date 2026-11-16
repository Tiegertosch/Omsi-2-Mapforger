using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>What a line deletion took with it, as the status line reports it.</summary>
    public sealed class OmsiLineDeletedSummaryTests
    {
        [Test]
        public void CountsTripsAndTracksAndSkipsTheLineFile()
        {
            var summary = new OmsiLineDeletedSummary(new[]
            {
                "TTData\\Line1.ttl", "TTData\\A.ttp", "TTData\\B.TTP", "TTData\\A.ttr", "TTData\\Other.txt"
            });
            Assert.AreEqual(2, summary.TripCount);
            Assert.AreEqual(1, summary.TrackCount);
            CollectionAssert.AreEqual(new[] { "A.ttp", "B.TTP", "A.ttr", "Other.txt" }, summary.FileNames);
        }

        [Test]
        public void SkipsTheLineFileInAnyLetterCase()
        {
            var summary = new OmsiLineDeletedSummary(new[] { "TTData\\Line1.TTL" });
            Assert.AreEqual(0, summary.FileNames.Count);
            Assert.AreEqual(0, summary.TripCount);
        }

        [Test]
        public void AnEmptyRemovalListHasNoFilesNoCountsAndNoRemainder()
        {
            var summary = new OmsiLineDeletedSummary(Array.Empty<string>());
            Assert.AreEqual(0, summary.FileNames.Count);
            Assert.AreEqual(0, summary.TripCount);
            Assert.AreEqual(0, summary.TrackCount);
            Assert.AreEqual(0, summary.FirstFileNames(5).Count);
            Assert.AreEqual(0, summary.MoreFileCount(5));
        }

        [Test]
        public void ListsAtMostTheLimitAndCountsTheRest()
        {
            var paths = Enumerable.Range(1, 8).Select(index => "TTData\\T" + index + ".ttp").ToList();
            var summary = new OmsiLineDeletedSummary(paths);
            CollectionAssert.AreEqual(new[] { "T1.ttp", "T2.ttp", "T3.ttp", "T4.ttp", "T5.ttp" }, summary.FirstFileNames(5));
            Assert.AreEqual(3, summary.MoreFileCount(5));
            Assert.AreEqual(8, summary.FileNames.Count, "the log has them all");
            Assert.AreEqual(8, summary.TripCount);
        }

        [Test]
        public void ExactlyTheLimitLeavesNoRemainder()
        {
            var summary = new OmsiLineDeletedSummary(Enumerable.Range(1, 5).Select(index => "TTData\\T" + index + ".ttp"));
            Assert.AreEqual(5, summary.FirstFileNames(5).Count);
            Assert.AreEqual(0, summary.MoreFileCount(5));
        }

        [Test]
        public void FileNamesCarryNoFolderWhateverTheSeparator()
        {
            var summary = new OmsiLineDeletedSummary(new[] { "TTData\\Sub\\A.ttp", "TTData/B.ttr", "C.ttp" });
            CollectionAssert.AreEqual(new[] { "A.ttp", "B.ttr", "C.ttp" }, summary.FileNames);
        }

        [Test]
        public void RejectsMissingList()
        {
            Assert.Throws<ArgumentNullException>(() => new OmsiLineDeletedSummary(null));
        }
    }
}
