using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiChronoSplinesTests
    {
        private string variantTile;
        private string testRoot;
        private string original;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-chronosplines-" + Guid.NewGuid().ToString("N"));
            original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "80", "", "[map]", "0", "0", "tile_0_0.map", "");
            // Splines 30, 31 (joined) and 32 in this order; a row of lamps along each of them, by the spline's place in the tile.
            Write(original, "tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "30", "0", "31", "0", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[rule]", "1", "speedlimit", "50.000", "0", "",
                "[spline]", "0", @"Splines\road.sli", "31", "30", "0", "50", "0", "0", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "32", "0", "0", "0", "0", "20", "90", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "50", "0", "1", "0", "0", "0", "0", "0", "10", "50", "1", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "51", "1", "1", "0", "0", "0", "0", "0", "10", "50", "1", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "52", "2", "1", "0", "0", "0", "0", "0", "10", "50", "1", "0", "");
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void ANewTypeIsATypCommandAndTheBaseMapAndTheSharedRecordStay()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            var baseTile = ReadBytes("tile_0_0.map");
            var spline = tile.Splines.Single(item => item.Id == 31);

            var change = session.SetSplineTypeInVariant(tile, spline, @"Splines\works.sli");

            Assert.IsNotNull(change);
            var text = OmsiTextFile.Decode(ReadBytes(variantTile));
            StringAssert.Contains("[selspline]\r\n31\r\n\r\n[typ]\r\nSplines\\works.sli\r\n", text);
            CollectionAssert.AreEqual(baseTile, ReadBytes("tile_0_0.map"));
            Assert.AreEqual(@"Splines\works.sli", tile.Splines[1].AssetPath);
            Assert.AreEqual(@"Splines\road.sli", spline.AssetPath);
            Assert.AreEqual(@"Splines\road.sli", session.Read().Tiles[0].Splines[1].AssetPath);
            AssertOverlayEquals(made, map);

            session.Undo(map, change);
            Assert.AreSame(spline, tile.Splines[1]);
            Assert.IsFalse(session.Workspace.FileExists(variantTile));
            AssertOverlayEquals(made, map);
            session.Redo(map, change);
            Assert.AreEqual(@"Splines\works.sli", tile.Splines[1].AssetPath);
            AssertOverlayEquals(made, map);
        }

        [Test]
        public void ChoosingAnotherTypeAgainReplacesTheTypCommandAndTheSameTypeChangesNothing()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];

            session.SetSplineTypeInVariant(tile, tile.Splines[1], @"Splines\works.sli");
            session.SetSplineTypeInVariant(tile, tile.Splines[1], @"Splines\wall.sli");

            var text = OmsiTextFile.Decode(ReadBytes(variantTile));
            Assert.AreEqual(1, Count(text, "[typ]"));
            StringAssert.Contains("wall.sli", text);
            AssertOverlayEquals(made, map);
            Assert.IsNull(session.SetSplineTypeInVariant(tile, tile.Splines[1], @"Splines\wall.sli"));
        }

        [Test]
        public void ASplineLeavesWithTheRowsAlongItAndTheRowsOfLaterSplinesFollowTheirSplines()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            var baseTile = ReadBytes("tile_0_0.map");
            var splines = new List<OmsiSplineRecord>(tile.Splines);
            var rows = new List<OmsiSplineAttachmentRecord>(tile.SplineAttachments);
            var splineCount = map.SplineCount;
            var rowCount = map.AttachmentCount;

            var change = session.DeleteSplineInVariant(map, tile, tile.Splines.Single(item => item.Id == 31));

            var text = OmsiTextFile.Decode(ReadBytes(variantTile));
            StringAssert.Contains("[selspline]\r\n31\r\n\r\n[delete]\r\n", text);
            CollectionAssert.AreEqual(baseTile, ReadBytes("tile_0_0.map"));
            CollectionAssert.AreEqual(new[] { 30, 32 }, tile.Splines.Select(item => item.Id).ToArray());
            Assert.AreEqual(1, change.RemovedRowCount);
            // Row 52 lay along spline 32, which now is the second spline of the tile.
            CollectionAssert.AreEqual(new[] { "50>30", "52>32" }, RowsOf(tile));
            // Spline 30 was joined to it and has a free end now; the base map's record keeps the ID.
            Assert.AreEqual(0, tile.Splines.Single(item => item.Id == 30).NextId);
            Assert.AreEqual(31, splines.Single(item => item.Id == 30).NextId);
            Assert.AreEqual(splineCount - 1, map.SplineCount);
            Assert.AreEqual(rowCount - 1, map.AttachmentCount);
            Assert.AreEqual(2, tile.SplineCount);
            AssertOverlayEquals(made, map);

            session.Undo(map, change);
            CollectionAssert.AreEqual(splines, tile.Splines);
            Assert.AreEqual(31, tile.Splines.Single(item => item.Id == 30).NextId);
            CollectionAssert.AreEqual(rows, tile.SplineAttachments);
            Assert.AreEqual(splineCount, map.SplineCount);
            Assert.AreEqual(rowCount, map.AttachmentCount);
            Assert.IsFalse(session.Workspace.FileExists(variantTile));
            AssertOverlayEquals(made, map);

            session.Redo(map, change);
            CollectionAssert.AreEqual(new[] { "50>30", "52>32" }, RowsOf(tile));
            AssertOverlayEquals(made, map);
        }

        [Test]
        public void TwoSplinesLeavingOneAfterAnotherAndComingBackInReverse()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];

            var first = session.DeleteSplineInVariant(map, tile, tile.Splines.Single(item => item.Id == 30));
            var second = session.DeleteSplineInVariant(map, tile, tile.Splines.Single(item => item.Id == 32));

            CollectionAssert.AreEqual(new[] { 31 }, tile.Splines.Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "51>31" }, RowsOf(tile));
            AssertOverlayEquals(made, map);

            session.Undo(map, second);
            CollectionAssert.AreEqual(new[] { 31, 32 }, tile.Splines.Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "51>31", "52>32" }, RowsOf(tile));
            session.Undo(map, first);
            CollectionAssert.AreEqual(new[] { 30, 31, 32 }, tile.Splines.Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "50>30", "51>31", "52>32" }, RowsOf(tile));
            AssertOverlayEquals(made, map);
        }

        [Test]
        public void ATypeChangeThenADeleteOfTheSameSplineLeavesNoSpline()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];

            var retyped = session.SetSplineTypeInVariant(tile, tile.Splines[1], @"Splines\works.sli");
            var deleted = session.DeleteSplineInVariant(map, tile, tile.Splines[1]);

            CollectionAssert.AreEqual(new[] { 30, 32 }, tile.Splines.Select(item => item.Id).ToArray());
            AssertOverlayEquals(made, map);
            session.Undo(map, deleted);
            Assert.AreEqual(@"Splines\works.sli", tile.Splines[1].AssetPath);
            session.Undo(map, retyped);
            Assert.AreEqual(@"Splines\road.sli", tile.Splines[1].AssetPath);
            AssertOverlayEquals(made, map);
        }

        [Test]
        public void ASplineTheVariantAddedItselfIsChangedByCommandsInTheSameChangeList()
        {
            var folder = Path.Combine(original, "Chrono", "0100_Own");
            Directory.CreateDirectory(folder);
            Write(folder, "Chrono.cfg", "[startdate]", "19920401", "", "[enddate]", "19920414", "");
            Write(folder, "tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\wall.sli", "60", "0", "0", "0", "0", "0", "90", "20", "0", "0", "0", "0", "0", "0", "0", "0", "");
            var own = OmsiChrono.ReadVariants(session.Workspace).Single();
            var map = Edit(own);
            var tile = map.Tiles[0];
            Assert.AreEqual(4, tile.Splines.Count);

            var retyped = session.SetSplineTypeInVariant(tile, tile.Splines.Single(item => item.Id == 60), @"Splines\gone.sli");
            AssertOverlayEquals(own, map);
            Assert.AreEqual(@"Splines\gone.sli", tile.Splines.Single(item => item.Id == 60).AssetPath);
            var deleted = session.DeleteSplineInVariant(map, tile, tile.Splines.Single(item => item.Id == 60));

            CollectionAssert.AreEqual(new[] { 30, 31, 32 }, tile.Splines.Select(item => item.Id).ToArray());
            AssertOverlayEquals(own, map);
            session.Undo(map, deleted);
            session.Undo(map, retyped);
            Assert.AreEqual(@"Splines\wall.sli", tile.Splines.Single(item => item.Id == 60).AssetPath);
            AssertOverlayEquals(own, map);
        }

        [Test]
        public void ARoadBuiltInAVariantIsAStandaloneChainInItsChangeListAndUndoTakesItAwayAgain()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            var baseTile = ReadBytes("tile_0_0.map");
            var baseConfig = ReadBytes("global.cfg");
            var splines = new List<OmsiSplineRecord>(tile.Splines);
            var plan = RoadPlan(100, 100, 100, 160);
            Assert.AreEqual(3, plan.Pieces.Count);

            var change = session.CreateRoadInVariant(map, plan, @"Splines\road.sli");

            Assert.AreEqual(3, change.SplineIds.Count);
            CollectionAssert.AreEqual(baseTile, ReadBytes("tile_0_0.map"));
            var added = tile.Splines.Skip(3).ToList();
            CollectionAssert.AreEqual(change.SplineIds, added.Select(item => item.Id).ToList());
            // One chain: nothing before the first piece or after the last, and the chain offsets add up.
            Assert.AreEqual(0, added[0].PreviousId);
            Assert.AreEqual(added[1].Id, added[0].NextId);
            Assert.AreEqual(added[2].Id, added[1].NextId);
            Assert.AreEqual(0, added[2].NextId);
            Assert.AreEqual(0.0, added[0].ChainOffset.Value, 1e-6);
            Assert.AreEqual(20.0, added[1].ChainOffset.Value, 1e-6);
            Assert.AreEqual(40.0, added[2].ChainOffset.Value, 1e-6);
            Assert.AreEqual(80 + 3, int.Parse(System.Text.RegularExpressions.Regex.Match(OmsiTextFile.Decode(ReadBytes("global.cfg")), @"\[NextIDCode\]\r\n(\d+)").Groups[1].Value));
            Assert.AreEqual(map.SplineCount, 6);
            AssertOverlayEquals(made, map);

            session.Undo(map, change);
            CollectionAssert.AreEqual(splines, tile.Splines);
            Assert.IsFalse(session.Workspace.FileExists(variantTile));
            CollectionAssert.AreEqual(baseConfig, ReadBytes("global.cfg"));
            Assert.AreEqual(3, map.SplineCount);
            AssertOverlayEquals(made, map);
            session.Redo(map, change);
            Assert.AreEqual(6, tile.Splines.Count);
            AssertOverlayEquals(made, map);
        }

        [Test]
        public void ARoadAfterADeletionInTheSameTileAndThenATypeAndADeleteOnItsPieces()
        {
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var tile = map.Tiles[0];
            session.DeleteSplineInVariant(map, tile, tile.Splines.Single(item => item.Id == 31));

            var road = session.CreateRoadInVariant(map, RoadPlan(100, 100, 100, 140), @"Splines\road.sli");
            AssertOverlayEquals(made, map);
            Assert.AreEqual(2, road.SplineIds.Count);
            CollectionAssert.AreEqual(new[] { 30, 32, road.SplineIds[0], road.SplineIds[1] }, tile.Splines.Select(item => item.Id).ToArray());

            var piece = tile.Splines.Single(item => item.Id == road.SplineIds[0]);
            session.SetSplineTypeInVariant(tile, piece, @"Splines\works.sli");
            AssertOverlayEquals(made, map);
            session.DeleteSplineInVariant(map, tile, tile.Splines.Single(item => item.Id == road.SplineIds[1]));
            AssertOverlayEquals(made, map);
            // The first piece lost its neighbour on the deleted piece's side.
            Assert.AreEqual(0, tile.Splines.Single(item => item.Id == road.SplineIds[0]).NextId);
        }

        [Test]
        public void ARoadCannotBeBuiltInAVariantWithoutOneOrFromAPlanWithErrors()
        {
            var map = session.Read();
            Assert.Throws<InvalidOperationException>(() => session.CreateRoadInVariant(map, RoadPlan(100, 100, 100, 160), @"Splines\road.sli"));
            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var shown = Edit(made);
            Assert.Throws<InvalidOperationException>(() => session.CreateRoadInVariant(shown, RoadPlan(1000, 100, 1000, 160), @"Splines\road.sli"));
        }

        [Test]
        public void ASplineOfTheBaseMapCannotBeChangedWithoutAnEditedVariantOrWithAStaleRecord()
        {
            var tile = session.Read().Tiles[0];
            Assert.Throws<InvalidOperationException>(() => session.SetSplineTypeInVariant(tile, tile.Splines[0], @"Splines\works.sli"));

            var made = session.CreateChronoVariant("Edit", new DateTime(1993, 5, 1), null);
            var map = Edit(made);
            var shared = map.Tiles[0].Splines[1];
            session.SetSplineTypeInVariant(map.Tiles[0], shared, @"Splines\works.sli");
            Assert.Throws<ArgumentException>(() => session.SetSplineTypeInVariant(map.Tiles[0], shared, @"Splines\wall.sli"));
        }

        private static string Describe(OmsiSplineRecord item)
        {
            return item.Id + " " + item.AssetPath + " " + item.PreviousId + ">" + item.NextId + " at " + item.LocalX.ToString("F3") + "," + item.LocalY.ToString("F3") + " h" +
                   item.Height.ToString("F3") + " " + item.HeadingDegrees.ToString("F3") + " L" + item.LengthMeters.ToString("F3") + " chain " +
                   (item.ChainOffset.HasValue ? item.ChainOffset.Value.ToString("F3") : "none") + " " + item.Rules.Count;
        }

        private sealed class FlatGround : IOmsiTerrainHeights
        {
            public bool TryGetHeight(double worldX, double worldY, out double value)
            {
                value = 3.0;
                return true;
            }
        }

        private static OmsiRoadPlan RoadPlan(double fromX, double fromY, double toX, double toY)
        {
            return OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = fromX, StartY = fromY, EndX = toX, EndY = toY, MaximumPieceLength = 25 }, new FlatGround());
        }

        private static List<string> RowsOf(OmsiTileSummary tile)
        {
            return tile.SplineAttachments.Select(row => row.Id + ">" + tile.Splines[row.SplineIndex].Id).ToList();
        }

        /// <summary>Lays the variants up to <paramref name="variant"/> over the base map and edits the variant there.</summary>
        private OmsiMapSummary Edit(OmsiChronoVariant variant)
        {
            variantTile = Path.Combine(variant.Folder, "tile_0_0.map");
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var day = variant.FirstDay ?? DateTime.Today;
            var upTo = variants.FindAll(v => string.CompareOrdinal(v.Name, variant.Name) <= 0 && v.IsActiveOn(day));
            var map = OmsiChrono.Overlay(session.Read(), session.Workspace, upTo, out var report);
            session.EditChronoVariant(report.Variants.Single(v => v.Name == variant.Name), report);
            return map;
        }

        /// <summary>
        /// The overlay computed again from the files equals what the editor shows: the splines and their order, their profiles, the
        /// rows and the spline each row lies along, and the counts.
        /// </summary>
        private void AssertOverlayEquals(OmsiChronoVariant variant, OmsiMapSummary shown)
        {
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var day = variant.FirstDay ?? DateTime.Today;
            var upTo = variants.FindAll(v => string.CompareOrdinal(v.Name, variant.Name) <= 0 && v.IsActiveOn(day));
            var fresh = OmsiChrono.Overlay(session.Read(), session.Workspace, upTo, out _);
            var now = shown.Tiles[0];
            var again = fresh.Tiles[0];
            CollectionAssert.AreEqual(again.Splines.Select(Describe).ToList(), now.Splines.Select(Describe).ToList());
            CollectionAssert.AreEqual(RowsOf(again), RowsOf(now));
            Assert.AreEqual(fresh.SplineCount, shown.SplineCount, "spline count of the summary");
            Assert.AreEqual(fresh.AttachmentCount, shown.AttachmentCount, "attachment count of the summary");
            Assert.AreEqual(again.SplineCount, now.SplineCount, "spline count of the tile");
            Assert.AreEqual(again.AttachmentCount, now.AttachmentCount, "attachment count of the tile");
        }

        private byte[] ReadBytes(string relativePath)
        {
            return session.Workspace.ReadAllBytes(relativePath);
        }

        private static int Count(string text, string part)
        {
            var count = 0;
            for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        private static void Write(string folder, string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(folder, name),
                OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
