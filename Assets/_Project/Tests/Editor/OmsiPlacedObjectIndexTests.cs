using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiPlacedObjectIndexTests
    {
        private static OmsiTileSummary Tile(int x, int y, params (string path, int id)[] objects)
        {
            var tile = new OmsiTileSummary { X = x, Y = y, FileName = "tile_" + x + "_" + y + ".map", IsAvailable = true };
            foreach (var item in objects)
                tile.Objects.Add(new OmsiObjectRecord { AssetPath = item.path, Id = item.id });
            tile.ObjectCount = tile.Objects.Count;
            return tile;
        }

        private static List<OmsiTileSummary> SampleMap()
        {
            return new List<OmsiTileSummary>
            {
                Tile(1, 0, (@"Sceneryobjects\Generic\bench.sco", 30), (@"Sceneryobjects\Trees\oak.sco", 31)),
                Tile(0, -1, (@"Sceneryobjects\Generic\bench.sco", 12), (@"Sceneryobjects\Generic\bench.sco", 11),
                    (@"Sceneryobjects\Signs\stop.sco", 405)),
                Tile(0, 0),
                Tile(2, 3, (@"Sceneryobjects\Trees\oak.sco", 7), (@"Sceneryobjects\Generic\bench.sco", 100))
            };
        }

        private static string[] Describe(OmsiPlacedObjectIndex index, OmsiPlacedObjectRows rows, OmsiPlacedObjectGrouping grouping)
        {
            return rows.Rows.Select(row => row.Kind == OmsiPlacedObjectRowKind.Group
                ? (row.Expanded ? "- " : "+ ") + (grouping == OmsiPlacedObjectGrouping.ByType ? index.Types[row.Group].FileName : index.GroupKey(grouping, row.Group)) + " " + row.Count
                : index.Types[index.Entries[row.Entry].TypeIndex].FileName + "#" + index.Entries[row.Entry].Record.Id).ToArray();
        }

        [Test]
        public void GroupsByTypeMostUsedFirstAndOpensOnlyChosenGroups()
        {
            var index = OmsiPlacedObjectIndex.Build(SampleMap());

            Assert.AreEqual(7, index.ObjectCount);
            CollectionAssert.AreEqual(new[] { "bench", "oak", "stop" }, index.Types.Select(t => t.FileName));
            CollectionAssert.AreEqual(new[] { 4, 2, 1 }, index.Types.Select(t => t.Count));
            Assert.AreEqual(@"Sceneryobjects\Generic", index.Types[0].Folder);
            // Tiles without objects are left out; the others go by X, then Y.
            CollectionAssert.AreEqual(new[] { "0,-1", "1,0", "2,3" }, index.Tiles.Select(OmsiPlacedObjectIndex.TileKey));

            var closed = index.BuildRows(null, OmsiPlacedObjectGrouping.ByType);
            CollectionAssert.AreEqual(new[] { "+ bench 4", "+ oak 2", "+ stop 1" }, Describe(index, closed, OmsiPlacedObjectGrouping.ByType));
            Assert.AreEqual(7, closed.MatchCount);
            Assert.AreEqual(3, closed.GroupCount);

            // A bench group opened: tile by tile, then by ID.
            var open = index.BuildRows(null, OmsiPlacedObjectGrouping.ByType, new HashSet<string> { index.Types[0].Key });
            CollectionAssert.AreEqual(new[] { "- bench 4", "bench#11", "bench#12", "bench#30", "bench#100", "+ oak 2", "+ stop 1" },
                Describe(index, open, OmsiPlacedObjectGrouping.ByType));
        }

        [Test]
        public void GroupsByTileAndListsFlat()
        {
            var index = OmsiPlacedObjectIndex.Build(SampleMap());

            var byTile = index.BuildRows(null, OmsiPlacedObjectGrouping.ByTile, new HashSet<string> { "0,-1", "2,3" });
            CollectionAssert.AreEqual(new[] { "- 0,-1 3", "bench#11", "bench#12", "stop#405", "+ 1,0 2", "- 2,3 2", "bench#100", "oak#7" },
                Describe(index, byTile, OmsiPlacedObjectGrouping.ByTile));

            var flat = index.BuildRows(null, OmsiPlacedObjectGrouping.Flat);
            CollectionAssert.AreEqual(new[] { "bench#11", "bench#12", "bench#30", "bench#100", "oak#7", "oak#31", "stop#405" },
                Describe(index, flat, OmsiPlacedObjectGrouping.Flat));
            Assert.AreEqual(0, flat.GroupCount);
        }

        [Test]
        public void SortsTheSameEveryTimeAndKeepsFileOrderForEqualObjects()
        {
            // Two objects with the same ID in one tile (as a broken map may have) stay in file order.
            var first = Tile(0, 0, ("a.sco", 5), ("a.sco", 5), ("b.sco", 1));
            var index = OmsiPlacedObjectIndex.Build(new[] { first });
            var rows = index.BuildRows(null, OmsiPlacedObjectGrouping.Flat).Rows;
            Assert.AreSame(first.Objects[0], index.Entries[rows[0].Entry].Record);
            Assert.AreSame(first.Objects[1], index.Entries[rows[1].Entry].Record);
            // Equal counts go by name.
            var tie = OmsiPlacedObjectIndex.Build(new[] { Tile(0, 0, ("zebra.sco", 1), ("Apple.sco", 2)) });
            CollectionAssert.AreEqual(new[] { "Apple", "zebra" }, tie.Types.Select(t => t.FileName));
        }

        [Test]
        public void FiltersByNameFolderIdAndTileWithEveryWord()
        {
            var index = OmsiPlacedObjectIndex.Build(SampleMap());

            string[] Flat(string text) => Describe(index, index.BuildRows(new OmsiPlacedObjectFilter(text, null), OmsiPlacedObjectGrouping.Flat), OmsiPlacedObjectGrouping.Flat);

            CollectionAssert.AreEqual(new[] { "bench#11", "bench#12", "bench#30", "bench#100" }, Flat("BENCH"));
            CollectionAssert.AreEqual(new[] { "oak#7", "oak#31" }, Flat("trees"));
            CollectionAssert.AreEqual(new[] { "bench#11", "bench#12", "bench#30", "bench#100" }, Flat("sceneryobjects/generic"));
            // The start of an ID, not any part of it.
            CollectionAssert.AreEqual(new[] { "bench#30", "oak#31" }, Flat("3"));
            CollectionAssert.AreEqual(new[] { "stop#405" }, Flat("40"));
            CollectionAssert.IsEmpty(Flat("05"));
            // A tile, also written with a space after the comma.
            CollectionAssert.AreEqual(new[] { "bench#11", "bench#12", "stop#405" }, Flat("0,-1"));
            CollectionAssert.AreEqual(new[] { "bench#100", "oak#7" }, Flat("2, 3"));
            // Every word must match.
            CollectionAssert.AreEqual(new[] { "bench#11", "bench#12" }, Flat("bench 0,-1"));
            CollectionAssert.AreEqual(new[] { "bench#100" }, Flat("generic 10"));
            CollectionAssert.IsEmpty(Flat("bench nothing"));
            CollectionAssert.AreEqual(Flat(null), Flat("   "));
        }

        [Test]
        public void SearchWordsOpenEveryGroupWithMatchesAndDropTheOthers()
        {
            var index = OmsiPlacedObjectIndex.Build(SampleMap());

            var rows = index.BuildRows(new OmsiPlacedObjectFilter("0,-1", null), OmsiPlacedObjectGrouping.ByType);

            CollectionAssert.AreEqual(new[] { "- bench 2", "bench#11", "bench#12", "- stop 1", "stop#405" }, Describe(index, rows, OmsiPlacedObjectGrouping.ByType));
            Assert.AreEqual(3, rows.MatchCount);
            Assert.AreEqual(2, rows.GroupCount);
            var none = index.BuildRows(new OmsiPlacedObjectFilter("nothing", null), OmsiPlacedObjectGrouping.ByTile);
            CollectionAssert.IsEmpty(none.Rows);
            Assert.AreEqual(0, none.MatchCount);
        }

        [Test]
        public void FiltersByOriginOnceTheResolverIsKnown()
        {
            var index = OmsiPlacedObjectIndex.Build(SampleMap());
            Assert.IsFalse(index.HasOrigins);
            Assert.IsTrue(index.Types.All(t => t.Origin == null));
            // Before the installation is read an origin filter lets nothing through, the unfiltered list everything.
            var dlcOnly = new OmsiPlacedObjectFilter(null, new[] { OmsiContentOrigin.Dlc });
            Assert.AreEqual(0, index.BuildRows(dlcOnly, OmsiPlacedObjectGrouping.Flat).MatchCount);

            var asked = new List<string>();
            index.ResolveOrigins(path =>
            {
                asked.Add(path);
                return path.IndexOf("Trees", StringComparison.OrdinalIgnoreCase) >= 0 ? OmsiContentOrigin.Dlc :
                    path.IndexOf("Signs", StringComparison.OrdinalIgnoreCase) >= 0 ? OmsiContentOrigin.UserContent : OmsiContentOrigin.BaseGame;
            });

            Assert.IsTrue(index.HasOrigins);
            Assert.AreEqual(3, asked.Count, "once per type, not per object");
            Assert.AreEqual(OmsiContentOrigin.Dlc, index.Types.Single(t => t.FileName == "oak").Origin);
            CollectionAssert.AreEqual(new[] { "oak#7", "oak#31" },
                Describe(index, index.BuildRows(dlcOnly, OmsiPlacedObjectGrouping.Flat), OmsiPlacedObjectGrouping.Flat));
            var notBase = new OmsiPlacedObjectFilter("3", new[] { OmsiContentOrigin.Dlc, OmsiContentOrigin.UserContent });
            CollectionAssert.AreEqual(new[] { "oak#31" }, Describe(index, index.BuildRows(notBase, OmsiPlacedObjectGrouping.Flat), OmsiPlacedObjectGrouping.Flat));

            var built = OmsiPlacedObjectIndex.Build(SampleMap(), path => OmsiContentOrigin.BaseGame);
            Assert.IsTrue(built.HasOrigins);
            Assert.AreEqual(7, built.BuildRows(new OmsiPlacedObjectFilter(null, new[] { OmsiContentOrigin.BaseGame }), OmsiPlacedObjectGrouping.Flat).MatchCount);
        }

        [Test]
        public void CountsSpellingsOfOnePathAsOneType()
        {
            var tile = Tile(0, 0, (@"Sceneryobjects\Generic\Bench.sco", 1), ("sceneryobjects/generic/bench.sco", 2),
                (@"SCENERYOBJECTS\GENERIC\BENCH.SCO", 3), (@"Sceneryobjects\Generic\Bench.sco", 4));
            var asked = 0;
            var index = OmsiPlacedObjectIndex.Build(new[] { tile }, path =>
            {
                asked++;
                return OmsiContentOrigin.BaseGame;
            });

            Assert.AreEqual(1, index.Types.Count);
            Assert.AreEqual(4, index.Types[0].Count);
            Assert.AreEqual(1, asked);
            Assert.AreEqual(@"Sceneryobjects\Generic\Bench.sco", index.Types[0].AssetPath, "the first spelling is shown");
            Assert.AreEqual(@"sceneryobjects\generic\bench.sco", index.Types[0].Key);
            Assert.AreEqual("Bench", index.Types[0].FileName);
            Assert.AreEqual(4, index.BuildRows(new OmsiPlacedObjectFilter("generic/BENCH", null), OmsiPlacedObjectGrouping.Flat).MatchCount);
        }

        [Test]
        public void AnEmptyMapHasNoRows()
        {
            var index = OmsiPlacedObjectIndex.Build(new List<OmsiTileSummary> { Tile(0, 0), Tile(1, 0) });

            Assert.AreEqual(0, index.ObjectCount);
            Assert.AreEqual(0, index.Types.Count);
            Assert.AreEqual(0, index.Tiles.Count);
            foreach (OmsiPlacedObjectGrouping grouping in Enum.GetValues(typeof(OmsiPlacedObjectGrouping)))
                CollectionAssert.IsEmpty(index.BuildRows(new OmsiPlacedObjectFilter("x", null), grouping).Rows);
            Assert.AreEqual(0, OmsiPlacedObjectIndex.Build(new List<OmsiTileSummary>()).ObjectCount);
            Assert.Throws<ArgumentNullException>(() => OmsiPlacedObjectIndex.Build(null));
        }

        [Test]
        public void FindsTheEntryOfARecord()
        {
            var map = SampleMap();
            var index = OmsiPlacedObjectIndex.Build(map);
            var record = map[3].Objects[1];

            var entry = index.EntryOf(record);

            Assert.AreSame(record, index.Entries[entry].Record);
            Assert.AreSame(map[3], index.Entries[entry].Tile);
            Assert.AreEqual(-1, index.EntryOf(new OmsiObjectRecord { AssetPath = "x.sco", Id = 30 }));
            Assert.AreEqual(-1, index.EntryOf(null));
        }

        [Test]
        public void ARebuiltIndexFollowsPlacingAndDeletingInASession()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "mapforge-objectlist-" + Guid.NewGuid().ToString("N"));
            try
            {
                var original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
                Directory.CreateDirectory(original);
                Write(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "20", "", "[map]", "0", "0", "tile_0_0.map", "");
                Write(original, "tile_0_0.map", "[version]", "14", "",
                    "[object]", "0", "stop.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                    "[attachObj]", "0", "sign.sco", "8", "5", "0", "1", "0", "0", "0", "0", "",
                    "[object]", "0", "tree.sco", "6", "30", "40", "0", "0", "0", "0", "0", "");
                var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
                var summary = session.Read();
                var before = OmsiPlacedObjectIndex.Build(summary.Tiles);
                // The attached sign is no [object] record and not in the list.
                CollectionAssert.AreEquivalent(new[] { 5, 6 }, before.Entries.Select(e => e.Record.Id));

                var placed = session.PlaceObject(summary, summary.Tiles[0], "tree.sco", 50, 60, 0, 90);
                var afterPlacing = OmsiPlacedObjectIndex.Build(summary.Tiles);
                Assert.AreEqual(3, afterPlacing.ObjectCount);
                Assert.AreEqual("tree", afterPlacing.Types[0].FileName);
                Assert.AreEqual(2, afterPlacing.Types[0].Count);
                Assert.GreaterOrEqual(afterPlacing.EntryOf(placed.Record), 0);
                // The old index does not change by itself: it is rebuilt.
                Assert.AreEqual(2, before.ObjectCount);

                var deleted = session.DeleteObject(summary, summary.Tiles[0], summary.Tiles[0].Objects.Single(o => o.Id == 6));
                Assert.IsTrue(deleted.Succeeded, string.Join("; ", deleted.Blockers));
                var afterDeleting = OmsiPlacedObjectIndex.Build(summary.Tiles);
                CollectionAssert.AreEquivalent(new[] { 5, placed.Record.Id }, afterDeleting.Entries.Select(e => e.Record.Id));
                CollectionAssert.AreEqual(new[] { "+ stop 1", "+ tree 1" },
                    Describe(afterDeleting, afterDeleting.BuildRows(null, OmsiPlacedObjectGrouping.ByType), OmsiPlacedObjectGrouping.ByType));
            }
            finally
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void BuildsAndFiltersFortyThousandObjectsQuickly()
        {
            // 400 tiles with 100 objects each from 500 types, each path a new string as a reader would make it.
            var random = new Random(7);
            var tiles = new List<OmsiTileSummary>();
            var id = 1;
            for (var x = 0; x < 20; x++)
                for (var y = 0; y < 20; y++)
                {
                    var tile = new OmsiTileSummary { X = x, Y = -y, FileName = "t.map", IsAvailable = true };
                    for (var i = 0; i < 100; i++)
                    {
                        var type = random.Next(500);
                        tile.Objects.Add(new OmsiObjectRecord { AssetPath = string.Concat(@"Sceneryobjects\Folder", (type % 40).ToString(), @"\object_", type.ToString(), ".sco"), Id = id++ });
                    }
                    tiles.Add(tile);
                }

            OmsiPlacedObjectIndex.Build(tiles).BuildRows(new OmsiPlacedObjectFilter("folder3", null), OmsiPlacedObjectGrouping.ByType);
            var watch = Stopwatch.StartNew();
            var index = OmsiPlacedObjectIndex.Build(tiles, path => OmsiContentOrigin.BaseGame);
            var build = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var closed = index.BuildRows(null, OmsiPlacedObjectGrouping.ByType);
            var firstRows = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var filtered = index.BuildRows(new OmsiPlacedObjectFilter("object_12", null), OmsiPlacedObjectGrouping.ByType);
            var byName = index.BuildRows(new OmsiPlacedObjectFilter("folder3 1", null), OmsiPlacedObjectGrouping.ByType);
            var byId = index.BuildRows(new OmsiPlacedObjectFilter("3999", null), OmsiPlacedObjectGrouping.Flat);
            var filter = watch.Elapsed.TotalMilliseconds / 3;
            watch.Restart();
            var flat = index.BuildRows(null, OmsiPlacedObjectGrouping.Flat);
            var byTile = index.BuildRows(null, OmsiPlacedObjectGrouping.ByTile);
            var firstOtherGroupings = watch.Elapsed.TotalMilliseconds;
            Console.WriteLine("Placed object index, 40,000 objects: build " + build.ToString("0.0") + " ms, first rows by type " + firstRows.ToString("0.0") +
                              " ms, filter " + filter.ToString("0.0") + " ms each, flat and by tile first time " + firstOtherGroupings.ToString("0.0") + " ms");

            Assert.AreEqual(40000, index.ObjectCount);
            Assert.AreEqual(500, closed.GroupCount);
            Assert.AreEqual(500, closed.Rows.Count);
            Assert.AreEqual(40000, flat.Rows.Count);
            Assert.AreEqual(400, byTile.GroupCount);
            Assert.Greater(filtered.MatchCount, 0);
            Assert.Greater(byName.MatchCount, 0);
            // IDs 3999 and 39990 to 39999.
            Assert.AreEqual(11, byId.MatchCount);
            // Rough ceilings well above the measured values, so a slow test machine passes and an accidental O(n²) does not.
            Assert.Less(build, 500, "build");
            Assert.Less(filter, 200, "filter");
            Assert.Less(firstOtherGroupings, 1000, "first flat and by-tile rows");
        }

        private static void Write(string folder, string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(folder, name),
                OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
