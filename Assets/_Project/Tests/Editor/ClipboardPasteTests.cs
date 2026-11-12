using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The turns rolled before copies are put down (ADR 098, M1): Ctrl+V and "Paste here" prepare the clipboard the same way, so turns
    /// rolled for an earlier clipboard never reach a later one with another number of copies.
    /// </summary>
    public sealed class ClipboardPasteTests
    {
        private string testRoot;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-paste-" + Guid.NewGuid().ToString("N"));
            var original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            File.WriteAllBytes(Path.Combine(original, "global.cfg"), OmsiTextFile.Encode(
                "[name]\r\nTestmap\r\n\r\n[NextIDCode]\r\n40\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
            var tile = new StringBuilder("[version]\r\n14\r\n\r\n");
            for (var id = 1; id <= 5; id++)
                tile.Append("[object]\r\n0\r\nSceneryobjects\\Test\\tree.sco\r\n" + id + "\r\n" + (100 + id * 10) + "\r\n100\r\n0\r\n" + id * 10 + "\r\n0\r\n0\r\n0\r\n\r\n");
            File.WriteAllBytes(Path.Combine(original, "tile_0_0.map"), OmsiTextFile.Encode(tile.ToString(), OmsiTextEncoding.Utf16LittleEndian));
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        private static OmsiClipboardData Copy(OmsiMapSummary summary, int count)
        {
            var tile = summary.Tiles[0];
            return OmsiClipboardData.Capture(summary, tile.Objects.Take(count).Select(item => new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, item)));
        }

        [Test]
        public void TurnsAreRolledAgainForEachClipboardSoFiveCopiesGetFiveNewTurns()
        {
            var summary = session.Read();
            var rolled = 0;
            Func<double> roll = () => ++rolled * 7.0;

            // Ctrl+V with random headings on: three copies, three turns.
            var three = Copy(summary, 3);
            var earlier = EditorShellBootstrap.PasteTurns(three.Items.Count, true, roll);
            Assert.AreEqual(3, earlier.Count);

            // Five copied afterwards: the turns of the three do not fit them, which is what "Paste here" ran into before.
            var five = Copy(summary, 5);
            Assert.Throws<ArgumentException>(() => five.PlaceAt(200, 200, 0, earlier));

            // Prepared again, as "Paste here" now does: five turns of their own, none of them one of the earlier.
            var turns = EditorShellBootstrap.PasteTurns(five.Items.Count, true, roll);
            Assert.AreEqual(5, turns.Count);
            CollectionAssert.IsEmpty(turns.Intersect(earlier));
            var targets = five.PlaceAt(200, 200, 0, turns);
            Assert.AreEqual(5, targets.Count);
            for (var index = 0; index < targets.Count; index++)
                Assert.AreEqual((five.Items[index].HeadingDegrees + turns[index]) % 360.0, targets[index].HeadingDegrees, 1e-9);
        }

        [Test]
        public void WithRandomHeadingsOffThereAreNoTurnsAndTheCopiesKeepTheirHeadings()
        {
            var summary = session.Read();
            var five = Copy(summary, 5);
            var rolled = false;

            var turns = EditorShellBootstrap.PasteTurns(five.Items.Count, false, () =>
            {
                rolled = true;
                return 90.0;
            });

            Assert.IsNull(turns);
            Assert.IsFalse(rolled);
            var targets = five.PlaceAt(200, 200, 0, turns);
            for (var index = 0; index < targets.Count; index++)
                Assert.AreEqual(five.Items[index].HeadingDegrees, targets[index].HeadingDegrees, 1e-9);
        }
    }
}
