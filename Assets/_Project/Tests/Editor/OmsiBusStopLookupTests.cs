using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Finding out whether an object is a bus stop only reads, so it works on a map open without a project too.</summary>
    public sealed class OmsiBusStopLookupTests
    {
        private string root;
        private string map;

        [SetUp]
        public void CreateMap()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-stoplookup-" + Guid.NewGuid().ToString("N"));
            map = Path.Combine(root, "maps", "Stoptown");
            Directory.CreateDirectory(map);
            Utf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "");
            Utf16("tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "Sceneryobjects\\a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[object]", "0", "Sceneryobjects\\Bus_Stop.sco", "6", "30", "40", "0", "0", "0", "0", "0", "",
                "[object]", "0", "Sceneryobjects\\shelter.sco", "7", "50", "60", "0", "0", "0", "0", "0", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void APlainObjectIsNoStopOnAMapOpenWithoutAProject()
        {
            WriteStopList();
            var session = OmsiMapSession.OpenReadOnly(Path.Combine(map, "global.cfg"));
            var summary = session.Read();
            var tile = summary.Tiles[0];

            Assert.IsFalse(session.CanEdit);
            Assert.IsFalse(session.IsBusStop(summary, tile, Find(tile, 5)));
            Assert.IsNull(session.BusStopName(summary, tile, Find(tile, 5)));
        }

        [Test]
        public void ABusStopObjectIsAStopWithoutAProject()
        {
            var session = OmsiMapSession.OpenReadOnly(Path.Combine(map, "global.cfg"));
            var summary = session.Read();
            var tile = summary.Tiles[0];

            Assert.IsTrue(session.IsBusStop(summary, tile, Find(tile, 6)));
        }

        [Test]
        public void AnObjectTheStopListNamesIsAStopWithItsNameWithoutAProject()
        {
            WriteStopList();
            var session = OmsiMapSession.OpenReadOnly(Path.Combine(map, "global.cfg"));
            var summary = session.Read();
            var tile = summary.Tiles[0];

            Assert.IsTrue(session.IsBusStop(summary, tile, Find(tile, 7)));
            Assert.AreEqual("Market", session.BusStopName(summary, tile, Find(tile, 7)));
        }

        [Test]
        public void AMapWithoutAStopListHasNoListedStopsWithoutAProject()
        {
            var session = OmsiMapSession.OpenReadOnly(Path.Combine(map, "global.cfg"));
            var summary = session.Read();
            var tile = summary.Tiles[0];

            Assert.IsFalse(Directory.Exists(Path.Combine(map, "TTData")));
            Assert.IsFalse(session.IsBusStop(summary, tile, Find(tile, 7)));
            Assert.IsNull(session.BusStopName(summary, tile, Find(tile, 7)));
            Assert.IsNull(session.BusStopName(summary, null, Find(tile, 7)));
            Assert.IsNull(session.BusStopName(summary, tile, null));
        }

        [Test]
        public void TheStopsOfAProjectStillComeFromItsCopy()
        {
            WriteStopList();
            var session = OmsiMapSession.OpenProject(Path.Combine(map, "global.cfg"), Path.Combine(root, "Projects", "Stops.omproj"));
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var originalList = File.ReadAllBytes(Path.Combine(map, "TTData", "Busstops.cfg"));

            Assert.IsNotNull(session.SetBusStopName(summary, tile, Find(tile, 5), "Church"));

            Assert.IsTrue(session.IsBusStop(summary, tile, Find(tile, 5)));
            Assert.AreEqual("Church", session.BusStopName(summary, tile, Find(tile, 5)));
            Assert.AreEqual("Market", session.BusStopName(summary, tile, Find(tile, 7)));
            CollectionAssert.AreEqual(originalList, File.ReadAllBytes(Path.Combine(map, "TTData", "Busstops.cfg")));
            // A map open without a project reads its own list, which the project did not change.
            var readOnly = OmsiMapSession.OpenReadOnly(Path.Combine(map, "global.cfg"));
            var readOnlySummary = readOnly.Read();
            Assert.IsNull(readOnly.BusStopName(readOnlySummary, readOnlySummary.Tiles[0], Find(readOnlySummary.Tiles[0], 5)));
            Assert.Throws<InvalidOperationException>(() => readOnly.SetBusStopName(readOnlySummary, readOnlySummary.Tiles[0], Find(readOnlySummary.Tiles[0], 5), "Church"));
        }

        private static OmsiObjectRecord Find(OmsiTileSummary tile, int id)
        {
            return tile.Objects.Single(each => each.Id == id);
        }

        private void WriteStopList()
        {
            Directory.CreateDirectory(Path.Combine(map, "TTData"));
            var stops = new[] { new OmsiTripStopChoice { Name = "Market", TileIndex = 0, ObjectId = 7 } };
            File.WriteAllBytes(Path.Combine(map, "TTData", "Busstops.cfg"), OmsiTimetableWriter.AppendBusStops(null, stops));
        }

        private void Utf16(string relative, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(map, relative), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray());
        }
    }
}
