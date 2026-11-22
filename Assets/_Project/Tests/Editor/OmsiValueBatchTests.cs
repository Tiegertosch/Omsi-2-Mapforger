using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// Moves and turns of many objects at once (<see cref="OmsiMapSession.UpdateObjects"/>): one write of each file, the same bytes as single
    /// moves one after the other, all or none across tiles, and the undo and redo of a group made of them.
    /// </summary>
    public sealed class OmsiValueBatchTests
    {
        private const int ExtraObjects = 40;

        private string testRoot;
        private string original;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-batch-" + Guid.NewGuid().ToString("N"));
            original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            // An entry point names object 6 of tile 0, 0 by its index 1 and its ID.
            Write("global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "500", "",
                "[entrypoints]", "1", "1", "6", "0", "1", "0", "2", "0", "0", "0", "1", "0", "Depot", "",
                "[map]", "0", "0", "tile_0_0.map", "",
                "[map]", "1", "0", "tile_1_0.map", "",
                "[map]", "0", "1", "tile_0_1.map", "");
            var lines = new List<string> { "[version]", "14", "",
                "[object]", "0", "a.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[rule]", "0", "trafficdensity", "1.000", "0", "",
                "[object]", "0", "b.sco", "6", "30", "40", "0", "0", "0", "0", "0", "" };
            for (var index = 0; index < ExtraObjects; index++)
                lines.AddRange(new[] { "[object]", "0", "c.sco", (100 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (50 + index).ToString(System.Globalization.CultureInfo.InvariantCulture), "60", "0", "0", "0", "0", "0", "" });
            Write("tile_0_0.map", lines.ToArray());
            Write("tile_1_0.map", "[version]", "14", "",
                "[object]", "0", "d.sco", "7", "50", "60", "0", "0", "0", "0", "0", "",
                "[object]", "0", "d.sco", "8", "70", "80", "1.5", "45", "0", "0", "0", "");
            Write("tile_0_1.map", "[version]", "14", "",
                "[object]", "0", "e.sco", "30", "15", "25", "0", "0", "0", "0", "0", "",
                "[object]", "0", "e.sco", "31", "35", "45", "0", "0", "0", "0", "0", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void ABatchOnOneTileWritesTheSameBytesAsSingleMovesOneAfterTheOther()
        {
            var single = Open("Single");
            var batch = Open("Batch");
            var singleSummary = single.Read();
            var batchSummary = batch.Read();
            // Object 6 takes its entry point along; object 100 moves twice, the second time from where the first left it; object 101 stays.
            var moves = new[]
            {
                Move(6, 31.25, 41.5, 1, 90, 0, 0), Move(100, 51.123456789, 61, 0.5, 10, 2, -1), Move(101, 51, 60, 0, 0, 0, 0),
                Move(5, 11, 22, 0, 180, 0, 0), Move(100, 52, 62, 0.5, 20, 2, -1)
            };

            var expectedChanges = new List<bool>();
            foreach (var move in moves)
                expectedChanges.Add(single.UpdateObject(singleSummary.Tiles[0], Find(singleSummary, move.Id), move.Values[0], move.Values[1], move.Values[2],
                    move.Values[3], move.Values[4], move.Values[5]));
            var changes = batch.UpdateObjects(Edits(batchSummary, moves));

            CollectionAssert.AreEqual(new[] { true, true, false, true, true }, expectedChanges);
            CollectionAssert.AreEqual(expectedChanges, changes);
            AssertSameWorkspaces(single, batch);
            CollectionAssert.AreEquivalent(new[] { "global.cfg", "tile_0_0.map" }, batch.ModifiedFiles());
            foreach (var move in moves)
                CollectionAssert.AreEqual(Values(Find(singleSummary, move.Id)), Values(Find(batchSummary, move.Id)), "object " + move.Id);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(batch, batchSummary);
        }

        [Test]
        public void ABatchAcrossTilesWritesTheSameBytesAsSingleMovesAndPutsEveryTileBackTheSameWay()
        {
            var single = Open("Single");
            var batch = Open("Batch");
            var singleSummary = single.Read();
            var batchSummary = batch.Read();
            var moves = new[] { Move(7, 55, 65, 0, 0, 0, 0), Move(6, 32, 42, 0, 0, 0, 0), Move(30, 16, 26, 1, 0, 0, 0), Move(8, 70, 80, 1.5, 90, 0, 0), Move(102, 1, 2, 3, 4, 5, 6) };
            var back = moves.Select(move => Move(move.Id, Values(Find(batchSummary, move.Id)))).Reverse().ToArray();

            foreach (var step in new[] { moves, back })
            {
                foreach (var move in step)
                    single.UpdateObject(TileOf(singleSummary, move.Id), Find(singleSummary, move.Id), move.Values[0], move.Values[1], move.Values[2], move.Values[3],
                        move.Values[4], move.Values[5]);
                Assert.IsTrue(batch.UpdateObjects(Edits(batchSummary, step)).All(changed => changed));
                AssertSameWorkspaces(single, batch);
                OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(batch, batchSummary);
            }
            // Back in their places, the tiles are the original again.
            CollectionAssert.IsSubsetOf(batch.ModifiedFiles(), new[] { "global.cfg" });
        }

        [Test]
        public void ABatchWritesEachFileOnceWhateverTheNumberOfObjects()
        {
            var session = Open("Count");
            var summary = session.Read();
            var workspace = session.Workspace;

            int[] Count(int objects, double shift, bool withEntryPoint)
            {
                var moves = new List<ValueMove>();
                for (var index = 0; index < objects; index++)
                    moves.Add(Move(100 + index, 50 + index + shift, 60, 0, 0, 0, 0));
                if (withEntryPoint)
                    moves.Add(Move(6, 30 + shift, 40, 0, 0, 0, 0));
                var reads = workspace.ReadCount;
                var writes = workspace.WriteCount;
                Assert.IsTrue(session.UpdateObjects(Edits(summary, moves)).All(changed => changed));
                return new[] { workspace.ReadCount - reads, workspace.WriteCount - writes };
            }

            var one = Count(1, 1, false);
            var many = Count(ExtraObjects, 2, false);
            Assert.AreEqual(1, one[1], "One object writes its tile.");
            Assert.AreEqual(1, many[1], "Many objects on one tile write it once.");
            Assert.AreEqual(one[0], many[0], "The files are read as often for many objects as for one.");
            // With the entry point global.cfg is written too, once.
            Assert.AreEqual(2, Count(ExtraObjects, 3, true)[1]);
        }

        [Test]
        public void ABatchWhoseLastFileCannotBeWrittenLeavesEveryFileAndRecordAsTheyWere()
        {
            var session = Open("Failing");
            var summary = session.Read();
            var moves = new[] { Move(100, 1, 2, 0, 0, 0, 0), Move(7, 3, 4, 0, 0, 0, 0), Move(6, 32, 42, 0, 0, 0, 0), Move(30, 5, 6, 0, 0, 0, 0) };
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var before = moves.Select(move => Values(Find(summary, move.Id))).ToList();
            var raised = OmsiMapWorkspaceTests.WatchRollBackFailures(session);

            // The tiles are written in the order first edited, global.cfg last: its failure puts three tiles back.
            foreach (var blocked in new[] { "tile_0_1.map", "global.cfg", "tile_0_0.map" })
            {
                Exception failed;
                using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, blocked, false))
                    failed = Assert.Catch(() => session.UpdateObjects(Edits(summary, moves)));

                OmsiMapWorkspaceTests.AssertWriteFailure(failed);
                Assert.IsEmpty(raised, blocked);
                OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
                CollectionAssert.IsEmpty(session.ModifiedFiles(), blocked);
                for (var index = 0; index < moves.Length; index++)
                    CollectionAssert.AreEqual(before[index], Values(Find(summary, moves[index].Id)), blocked + ", object " + moves[index].Id);
                OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            }

            // Without the obstacle the very batch goes through.
            Assert.IsTrue(session.UpdateObjects(Edits(summary, moves)).All(changed => changed));
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void ABatchWithAnObjectMissingWritesNothing()
        {
            var session = Open("Missing");
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var ghost = new OmsiObjectRecord { AssetPath = "x.sco", Id = 99, LocalX = 1, LocalY = 1 };
            var edits = new List<OmsiObjectValueEdit>(Edits(summary, new[] { Move(100, 1, 2, 0, 0, 0, 0) }))
            {
                new OmsiObjectValueEdit(tile, ghost, 2, 2, 0, 0, 0, 0)
            };
            var writes = session.Workspace.WriteCount;

            Assert.Throws<InvalidOperationException>(() => session.UpdateObjects(edits));

            Assert.AreEqual(writes, session.Workspace.WriteCount);
            Assert.AreEqual(50.0, Find(summary, 100).LocalX, 1e-12);
        }

        [Test]
        public void ABatchWhosePuttingBackFailsReportsItOnce()
        {
            var session = Open("RollBack");
            var summary = session.Read();
            var moves = new[] { Move(5, 11, 21, 0, 0, 0, 0), Move(6, 32, 42, 0, 0, 0, 0) };
            var back = new[] { Move(5, Values(Find(summary, 5))), Move(6, Values(Find(summary, 6))) };
            Assert.IsTrue(session.UpdateObjects(Edits(summary, moves)).All(changed => changed));
            var moved = moves.Select(move => Values(Find(summary, move.Id))).ToList();
            var raised = OmsiMapWorkspaceTests.WatchRollBackFailures(session);

            OmsiRollBackFailedException failed;
            // Moving back gives the tile its original bytes, which takes its copy away and needs no save; putting the copy back needs one,
            // which cannot be made. global.cfg's copy is held open, so taking it away fails after the tile.
            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "tile_0_0.map", false))
            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "global.cfg", true))
                failed = Assert.Throws<OmsiRollBackFailedException>(() => session.UpdateObjects(Edits(summary, back)));

            Assert.AreEqual(1, raised.Count, "The failed roll-back is reported once.");
            Assert.AreSame(failed, raised[0]);
            // The records stay as before the call; the files may hold part of it, so the map has to be read again.
            for (var index = 0; index < moves.Length; index++)
                CollectionAssert.AreEqual(moved[index], Values(Find(summary, moves[index].Id)));
        }

        [Test]
        public void RunsJoinValuePartsNextToEachOtherOnly()
        {
            var units = StepGroup.Runs(new[] { "v1", "v2", "t", "v3", "t", "t", "v4", "v5", "v6" }, part => part.StartsWith("v", StringComparison.Ordinal));

            CollectionAssert.AreEqual(new[] { "v1 v2", "t", "v3", "t", "t", "v4 v5 v6" }, units.Select(unit => string.Join(" ", unit)));
            CollectionAssert.IsEmpty(StepGroup.Runs(new string[0], part => true));
        }

        [Test]
        public void UndoAndRedoOfAGroupOfValuePartsWriteEachFileOnceAndGiveTheSameBytesAsSingleParts()
        {
            var session = Open("Group");
            var single = Open("Single");
            var summary = session.Read();
            var singleSummary = single.Read();
            var parts = new List<Part>();
            foreach (var move in new[] { Move(100, 1, 2, 0, 0, 0, 0), Move(6, 32, 42, 0, 0, 0, 0), Move(7, 3, 4, 0, 90, 0, 0), Move(101, 5, 6, 0, 0, 0, 0) })
                parts.Add(new Part { Id = move.Id, Before = Values(Find(summary, move.Id)), After = move.Values });

            void ApplySingly(bool undo)
            {
                var order = new List<Part>(parts);
                if (undo)
                    order.Reverse();
                foreach (var part in order)
                {
                    var values = undo ? part.Before : part.After;
                    single.UpdateObject(TileOf(singleSummary, part.Id), Find(singleSummary, part.Id), values[0], values[1], values[2], values[3], values[4], values[5]);
                }
            }

            Assert.IsTrue(session.UpdateObjects(parts.Select(part => Edit(summary, part.Id, part.After)).ToList()).All(changed => changed));
            ApplySingly(false);
            AssertSameWorkspaces(single, session);

            foreach (var undo in new[] { true, false, true })
            {
                var writes = session.Workspace.WriteCount;
                ApplyGroup(session, summary, parts, undo);
                ApplySingly(undo);
                // Tile 0, 0, tile 1, 0 and global.cfg, once each.
                Assert.AreEqual(3, session.Workspace.WriteCount - writes, undo ? "undo" : "redo");
                AssertSameWorkspaces(single, session);
                foreach (var part in parts)
                    CollectionAssert.AreEqual(undo ? part.Before : part.After, Values(Find(summary, part.Id)));
                OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            }
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map"));
            Assert.IsFalse(session.Workspace.IsModified("tile_1_0.map"));
        }

        [Test]
        public void AnUndoOfAMixedGroupWhoseValuePartsFailTakesTheTransferBack()
        {
            var session = Open("Mixed");
            var summary = session.Read();
            var source = summary.Tiles[0];
            var target = summary.Tiles[1];
            var item = Find(summary, 5);
            // As the shell records a selection moved partly into another tile: object 5 into tile 1, 0, then 30 and 31 by values in tile 0, 1.
            var transfer = session.TransferObject(summary, source, item, target, 12.5, 40);
            Assert.IsTrue(transfer.Succeeded);
            var parts = new List<Part> { new Part { Change = transfer.Change } };
            foreach (var move in new[] { Move(30, 16, 26, 0, 0, 0, 0), Move(31, 36, 46, 0, 45, 0, 0) })
                parts.Add(new Part { Id = move.Id, Before = Values(Find(summary, move.Id)), After = move.Values });
            Assert.IsTrue(session.UpdateObjects(parts.Skip(1).Select(part => Edit(summary, part.Id, part.After)).ToList()).All(changed => changed));
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var sourceRecords = source.Objects.ToList();
            var targetRecords = target.Objects.ToList();
            var raised = OmsiMapWorkspaceTests.WatchRollBackFailures(session);
            var noted = new List<OmsiRollBackFailedException>();

            // The undo takes the value parts back first, as they come last; tile 0, 1 goes back to the original, but its copy is held open and
            // cannot be taken away, so nothing of them is written, and the transfer, not undone yet, stays.
            Exception failed;
            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "tile_0_1.map", true))
                failed = Assert.Catch(() => ApplyGroup(session, summary, parts, true, noted));
            OmsiMapWorkspaceTests.AssertWriteFailure(failed);
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            Assert.IsTrue(transfer.Change.IsApplied);

            // Value parts first in the group, so the undo takes the transfer back before them and has to redo it when they fail.
            var reordered = new List<Part> { parts[1], parts[2], parts[0] };
            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "tile_0_1.map", true))
                failed = Assert.Catch(() => ApplyGroup(session, summary, reordered, true, noted));

            OmsiMapWorkspaceTests.AssertWriteFailure(failed);
            Assert.IsEmpty(raised);
            Assert.IsEmpty(noted);
            Assert.IsTrue(transfer.Change.IsApplied, "The transfer undone before the failure was done again.");
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            CollectionAssert.AreEqual(sourceRecords, source.Objects);
            CollectionAssert.AreEqual(targetRecords, target.Objects);
            foreach (var part in parts.Skip(1))
                CollectionAssert.AreEqual(part.After, Values(Find(summary, part.Id)));
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);

            // Without the obstacle the undo goes through: every file is the original again.
            ApplyGroup(session, summary, reordered, true, noted);
            Assert.IsFalse(transfer.Change.IsApplied);
            CollectionAssert.IsEmpty(session.ModifiedFiles());
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
        }

        /// <summary>A part of a group as the shell keeps it: a transfer, or an object's values before and after.</summary>
        private sealed class Part
        {
            public OmsiObjectChange Change;
            public int Id;
            public double[] Before;
            public double[] After;
        }

        /// <summary>
        /// Undoes or redoes a group as the shell's ApplyStep does: the parts in reverse order for an undo, value parts next to each other
        /// written together, a run taken back last part first.
        /// </summary>
        private static void ApplyGroup(OmsiMapSession session, OmsiMapSummary summary, List<Part> parts, bool undo, List<OmsiRollBackFailedException> noted = null)
        {
            var order = new List<Part>(parts);
            if (undo)
                order.Reverse();
            var units = StepGroup.Runs(order, part => part.Change == null);

            void ApplyUnit(List<Part> unit, bool backwards)
            {
                if (unit.Count == 1 && unit[0].Change != null)
                {
                    if (backwards)
                        session.Undo(summary, unit[0].Change);
                    else
                        session.Redo(summary, unit[0].Change);
                    return;
                }
                session.UpdateObjects(unit.Select(part => Edit(summary, part.Id, backwards ? part.Before : part.After)).ToList());
            }

            StepGroup.Apply(units, unit => ApplyUnit(unit, undo), unit =>
            {
                var reversed = new List<Part>(unit);
                reversed.Reverse();
                ApplyUnit(reversed, !undo);
            }, unit => unit.Where(part => part.Change != null).Select(part => part.Change.IsApplied).ToList(), noted == null ? (Action<OmsiRollBackFailedException>)null : noted.Add);
        }

        private sealed class ValueMove
        {
            public int Id;
            public double[] Values;
        }

        private static ValueMove Move(int id, double x, double y, double z, double heading, double pitch, double bank)
        {
            return new ValueMove { Id = id, Values = new[] { x, y, z, heading, pitch, bank } };
        }

        private static ValueMove Move(int id, double[] values)
        {
            return new ValueMove { Id = id, Values = values };
        }

        private static List<OmsiObjectValueEdit> Edits(OmsiMapSummary summary, IEnumerable<ValueMove> moves)
        {
            return moves.Select(move => Edit(summary, move.Id, move.Values)).ToList();
        }

        private static OmsiObjectValueEdit Edit(OmsiMapSummary summary, int id, double[] values)
        {
            return new OmsiObjectValueEdit(TileOf(summary, id), Find(summary, id), values[0], values[1], values[2], values[3], values[4], values[5]);
        }

        private static OmsiTileSummary TileOf(OmsiMapSummary summary, int id)
        {
            return summary.Tiles.Single(tile => tile.Objects.Exists(item => item.Id == id));
        }

        private static OmsiObjectRecord Find(OmsiMapSummary summary, int id)
        {
            return TileOf(summary, id).Objects.Single(item => item.Id == id);
        }

        private static double[] Values(OmsiObjectRecord item)
        {
            return new[] { item.LocalX, item.LocalY, item.Height, item.HeadingDegrees, item.PitchDegrees, item.BankDegrees };
        }

        private OmsiMapSession Open(string projectName)
        {
            return OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", projectName + ".omproj"));
        }

        private static void AssertSameWorkspaces(OmsiMapSession expected, OmsiMapSession actual)
        {
            CollectionAssert.AreEqual(expected.ModifiedFiles(), actual.ModifiedFiles());
            foreach (var path in expected.ModifiedFiles())
                CollectionAssert.AreEqual(expected.Workspace.ReadAllBytes(path), actual.Workspace.ReadAllBytes(path), path);
        }

        private void Write(string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(original, name), OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
