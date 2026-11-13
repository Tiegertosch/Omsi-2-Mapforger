using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A tile added or deleted (ADR 031): the bytes of every file before and after, null where a file did not exist, so
    /// the step can be taken back and done again exactly.
    /// </summary>
    public sealed class OmsiTileChange
    {
        internal OmsiTileChange(bool added, int x, int y)
        {
            Added = added;
            X = x;
            Y = y;
        }

        public bool Added { get; }
        public int X { get; }
        public int Y { get; }
        public bool IsApplied { get; internal set; } = true;
        internal List<OmsiObjectChange.FileState> Files { get; } = new List<OmsiObjectChange.FileState>();

        /// <summary>The text of every tile file the change wrote, before and after, such as to see what undoing it brings back.</summary>
        public IEnumerable<string> TileTexts()
        {
            foreach (var file in Files)
            {
                if (!file.RelativePath.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (file.Before != null)
                    yield return OmsiTextFile.Decode(file.Before);
                if (file.After != null)
                    yield return OmsiTextFile.Decode(file.After);
            }
        }
    }

    /// <summary>What deleting a tile did, or what stops it.</summary>
    public sealed class OmsiTileDeletionResult
    {
        internal OmsiTileDeletionResult(OmsiTileChange change, IReadOnlyList<string> blockers)
        {
            Change = change;
            Blockers = blockers;
        }

        public OmsiTileChange Change { get; }

        /// <summary>Why the tile stays, one sentence each, such as an entry point on it.</summary>
        public IReadOnlyList<string> Blockers { get; }

        public bool Succeeded => Change != null;
    }

    /// <summary>
    /// The edits of a tile discarded: the bytes before and after of every file the revert wrote (the tile, global.cfg for its
    /// entry points, the terrain fitted again around it), and the records of the tile on the side not shown. Undo and redo
    /// swap those records into the same tile summary, so every older and newer step finds the records it holds.
    /// </summary>
    public sealed class OmsiTileRevertChange
    {
        internal OmsiTileRevertChange(string tileFileName, int x, int y)
        {
            TileFileName = tileFileName;
            X = x;
            Y = y;
        }

        /// <summary>The tile's file, relative to the map folder.</summary>
        public string TileFileName { get; }

        public int X { get; }
        public int Y { get; }
        public bool IsApplied { get; internal set; } = true;

        /// <summary>The reverted tile in the summary given to the revert; null when it was given none.</summary>
        public OmsiTileSummary Tile { get; internal set; }

        /// <summary>The tiles of that summary whose terrain the revert fitted again, to draw again.</summary>
        public List<OmsiTileSummary> TerrainTiles { get; } = new List<OmsiTileSummary>();

        /// <summary>The tile's records in the state that is not shown: the edited ones while the revert is in effect, else the original's.</summary>
        internal OmsiTileContent Hidden { get; set; }

        internal List<OmsiObjectChange.FileState> Files { get; } = new List<OmsiObjectChange.FileState>();

        /// <summary>The relative paths of the files the revert wrote.</summary>
        public IEnumerable<string> FilePaths()
        {
            foreach (var file in Files)
                yield return file.RelativePath;
        }

        /// <summary>The text of the tile file before and after, such as to see what undoing the revert brings back.</summary>
        public IEnumerable<string> TileTexts()
        {
            foreach (var file in Files)
            {
                if (!file.RelativePath.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (file.Before != null)
                    yield return OmsiTextFile.Decode(file.Before);
                if (file.After != null)
                    yield return OmsiTextFile.Decode(file.After);
            }
        }
    }

    /// <summary>
    /// A step that was refused because a file it writes was changed since, in the workspace or by another way: nothing was written, files
    /// and summary are as before the call. Trying again gives the same answer.
    /// </summary>
    public sealed class OmsiStepRefusedException : InvalidOperationException
    {
        public OmsiStepRefusedException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// A step that failed, and putting back what it had written failed as well: the workspace may hold part of the step, so files and the
    /// summary in memory may disagree; read the map again. The inner <see cref="AggregateException"/> holds the step's failure first, then
    /// the roll-back's.
    /// </summary>
    public sealed class OmsiRollBackFailedException : InvalidOperationException
    {
        public OmsiRollBackFailedException(string message, AggregateException failures)
            : base(message, failures)
        {
        }
    }

    /// <summary>The records of one tile and their counts, kept aside so they can be put back into the very tile summary.</summary>
    internal sealed class OmsiTileContent
    {
        private readonly List<OmsiObjectRecord> objects;
        private readonly List<OmsiSplineRecord> splines;
        private readonly List<OmsiAttachedObjectRecord> attachedObjects;
        private readonly List<OmsiSplineAttachmentRecord> splineAttachments;
        private readonly int objectCount;
        private readonly int splineCount;
        private readonly int attachmentCount;

        private OmsiTileContent(OmsiTileSummary tile)
        {
            objects = new List<OmsiObjectRecord>(tile.Objects);
            splines = new List<OmsiSplineRecord>(tile.Splines);
            attachedObjects = new List<OmsiAttachedObjectRecord>(tile.AttachedObjects);
            splineAttachments = new List<OmsiSplineAttachmentRecord>(tile.SplineAttachments);
            objectCount = tile.ObjectCount;
            splineCount = tile.SplineCount;
            attachmentCount = tile.AttachmentCount;
        }

        public static OmsiTileContent Of(OmsiTileSummary tile)
        {
            return new OmsiTileContent(tile);
        }

        /// <summary>Puts these records into <paramref name="tile"/>, keeping the map's totals right, and returns the ones it held.</summary>
        public OmsiTileContent SwapInto(OmsiMapSummary summary, OmsiTileSummary tile)
        {
            var shown = Of(tile);
            if (summary != null)
            {
                summary.ObjectCount += objectCount - tile.ObjectCount;
                summary.SplineCount += splineCount - tile.SplineCount;
                summary.AttachmentCount += attachmentCount - tile.AttachmentCount;
            }
            tile.Objects.Clear();
            tile.Objects.AddRange(objects);
            tile.Splines.Clear();
            tile.Splines.AddRange(splines);
            tile.AttachedObjects.Clear();
            tile.AttachedObjects.AddRange(attachedObjects);
            tile.SplineAttachments.Clear();
            tile.SplineAttachments.AddRange(splineAttachments);
            tile.ObjectCount = objectCount;
            tile.SplineCount = splineCount;
            tile.AttachmentCount = attachmentCount;
            return shown;
        }
    }

    /// <summary>Rules for the editor's undo and redo stacks that hold whatever their steps are; the last item of a stack is its top.</summary>
    public static class OmsiStepHistory
    {
        /// <summary>
        /// Clears the history beyond a revert of a tile whose undo or redo was refused, as a file it wrote changed since: the revert
        /// stays as it is for good. <paramref name="from"/> is the stack the refused step was taken from, already without it; every
        /// step left there goes, as each needs the revert the other way. A refused undo drops the older steps, which hold the tile's
        /// records from before the revert; a refused redo drops the newer steps, made on top of the revert. The other stack stays:
        /// its steps were made, or undone, in the state that is shown, so they still apply. Returns the number of steps dropped.
        /// </summary>
        public static int DropStepsBeyondRefusedRevert<T>(List<T> from)
        {
            if (from == null)
                throw new ArgumentNullException(nameof(from));
            var dropped = from.Count;
            from.Clear();
            return dropped;
        }
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// The step of a revert of <paramref name="tilePath"/>, with the files it may write: the tile, global.cfg, and the terrain of
        /// the tile and the tiles beside it, which the terrain fit around it rewrites.
        /// </summary>
        private OmsiTileRevertChange RevertedFiles(OmsiRecordFile config, string tilePath, out List<string> touched)
        {
            var maps = config.Records.FindAll(record => string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase) && record.Fields.Count == 3);
            var own = maps.Find(record => SamePath(record.GetField(2), tilePath));
            if (own == null || !own.TryGetInt(0, out var x) || !own.TryGetInt(1, out var y))
                throw new InvalidOperationException(tilePath + " is not listed in global.cfg.");
            touched = new List<string> { tilePath, GlobalConfigFileName };
            foreach (var record in maps)
            {
                if (!record.TryGetInt(0, out var otherX) || !record.TryGetInt(1, out var otherY) || Math.Abs(otherX - x) > 1 || Math.Abs(otherY - y) > 1)
                    continue;
                string terrainPath;
                try
                {
                    terrainPath = OmsiRelativePath.Normalize(record.GetField(2)) + ".terrain";
                }
                catch (ArgumentException)
                {
                    // The reader ignores such an entry too, so no terrain of it is fitted.
                    continue;
                }
                if (!touched.Exists(path => string.Equals(path, terrainPath, StringComparison.OrdinalIgnoreCase)))
                    touched.Add(terrainPath);
            }
            return new OmsiTileRevertChange(tilePath, x, y);
        }

        /// <summary>True when two states of a file are alike: both missing, or the same bytes.</summary>
        private static bool SameState(byte[] first, byte[] second)
        {
            if (first == null || second == null)
                return first == null && second == null;
            return SameBytes(first, second);
        }

        /// <summary>
        /// Reads what the tile in the summary shows after the revert: the records read again from its file, and the ground and water of it
        /// and of the tiles whose terrain was fitted again. Changes nothing yet; the action returned puts it all in without reading a file,
        /// keeping the records the tile held in the step. Null when there is no summary, or no such tile in it, to keep up to date.
        /// </summary>
        private Action ReadRevertPresentation(OmsiMapSummary summary, OmsiTileRevertChange change)
        {
            if (summary == null)
                return null;
            var tile = summary.Tiles.Find(candidate => SamePath(candidate.FileName, change.TileFileName));
            if (tile == null)
                return null;
            var terrainTiles = new List<OmsiTileSummary>();
            foreach (var file in change.Files)
            {
                if (!file.RelativePath.EndsWith(".terrain", StringComparison.OrdinalIgnoreCase))
                    continue;
                var terrainTile = summary.Tiles.Find(candidate =>
                    string.Equals(OmsiRelativePath.Normalize(candidate.FileName) + ".terrain", file.RelativePath, StringComparison.OrdinalIgnoreCase));
                if (terrainTile != null && !terrainTiles.Contains(terrainTile))
                    terrainTiles.Add(terrainTile);
            }
            var workspace = RequireWorkspace();
            var fresh = OmsiMapReader.ReadAddedElements(workspace, change.TileFileName, tile);
            var shownTiles = new List<OmsiTileSummary> { tile };
            foreach (var terrainTile in terrainTiles)
                if (!shownTiles.Contains(terrainTile))
                    shownTiles.Add(terrainTile);
            var ground = TileGround.ReadAll(workspace, shownTiles);
            return () =>
            {
                change.Tile = tile;
                change.TerrainTiles.AddRange(terrainTiles);
                change.Hidden = OmsiTileContent.Of(fresh).SwapInto(summary, tile);
                TileGround.ApplyAll(ground);
            };
        }

        /// <summary>
        /// Takes back a revert of a tile: writes every file as it was before, and puts the records the tile held then back into its
        /// summary, so older steps find their records again. Failures:
        /// <see cref="OmsiStepRefusedException"/> when a file was changed since: nothing was written, files, summary and
        /// <see cref="OmsiTileRevertChange.IsApplied"/> are as before, and the undo can never succeed;
        /// <see cref="OmsiRollBackFailedException"/> when writing or reading failed and putting the files back failed too: the files
        /// may hold part of the step while summary and <see cref="OmsiTileRevertChange.IsApplied"/> are as before, so the map must be read again;
        /// any other exception: writing or reading failed and every file written was put back, so files, summary and
        /// <see cref="OmsiTileRevertChange.IsApplied"/> are as before and the undo can be tried again.
        /// </summary>
        public void Undo(OmsiMapSummary summary, OmsiTileRevertChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (!change.IsApplied)
                throw new InvalidOperationException("The revert has already been undone.");
            ApplyFiles(change.Files, true);
            SwapRevertedTile(summary, change, true);
            change.IsApplied = false;
        }

        /// <summary>
        /// Reverts the tile again after an undo, with the same records as the first time, so newer steps find theirs. Fails with the same
        /// three kinds of failure as <see cref="Undo(OmsiMapSummary, OmsiTileRevertChange)"/>, which leave the same states behind.
        /// </summary>
        public void Redo(OmsiMapSummary summary, OmsiTileRevertChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (change.IsApplied)
                throw new InvalidOperationException("The revert is already applied.");
            ApplyFiles(change.Files, false);
            SwapRevertedTile(summary, change, false);
            change.IsApplied = true;
        }

        /// <param name="undo">Whether the files were just written as before the revert; a failure writes them back the other way.</param>
        private void SwapRevertedTile(OmsiMapSummary summary, OmsiTileRevertChange change, bool undo)
        {
            if (change.Tile == null || change.Hidden == null)
                return;
            List<TileGround> ground;
            try
            {
                ground = TileGround.ReadAll(RequireWorkspace(), RevertedTiles(change));
            }
            catch (Exception failure)
            {
                RollBackAfter(failure, () => ApplyFiles(change.Files, !undo));
                throw;
            }
            change.Hidden = change.Hidden.SwapInto(summary, change.Tile);
            TileGround.ApplyAll(ground);
        }

        /// <summary>
        /// Takes back what a step wrote before it failed. When that fails as well, both errors are reported as an
        /// <see cref="OmsiRollBackFailedException"/>: the message begins with the step's and names the failed roll-back, and the inner
        /// exception holds both, the step's first.
        /// </summary>
        private static void RollBackAfter(Exception failure, Action rollBack)
        {
            try
            {
                rollBack();
            }
            catch (Exception rollBackFailure)
            {
                throw new OmsiRollBackFailedException(failure.Message + " Putting the files back as they were failed as well, so the workspace may hold part of the step: " +
                                                      rollBackFailure.Message, new AggregateException(failure, rollBackFailure));
            }
        }

        /// <summary>
        /// The range of ground heights and the water of a tile as its files hold them, read first and put into the tile summary later,
        /// so a failed read changes nothing. Reads what <see cref="ReadWaterOf"/> reads.
        /// </summary>
        private sealed class TileGround
        {
            private OmsiTileSummary tile;
            private bool hasRange;
            private float minHeight;
            private float maxHeight;
            private float[] water;

            public static List<TileGround> ReadAll(OmsiMapWorkspace workspace, IEnumerable<OmsiTileSummary> tiles)
            {
                var states = new List<TileGround>();
                foreach (var tile in tiles)
                {
                    var tilePath = OmsiRelativePath.Normalize(tile.FileName);
                    var terrainPath = tilePath + ".terrain";
                    var state = new TileGround { tile = tile };
                    if (workspace.FileExists(terrainPath) && OmsiMapReader.TryReadTerrainRange(workspace.ReadAllBytes(terrainPath), out var min, out var max))
                    {
                        state.hasRange = true;
                        state.minHeight = min;
                        state.maxHeight = max;
                    }
                    var file = OmsiRecordFile.Parse(workspace.ReadAllBytes(tilePath), OmsiTileRecordLayouts.Instance);
                    state.water = file.Records.Exists(record => string.Equals(record.Name, "water", StringComparison.OrdinalIgnoreCase))
                        ? OmsiMapReader.ReadWater(workspace, tilePath)
                        : null;
                    states.Add(state);
                }
                return states;
            }

            public static void ApplyAll(List<TileGround> states)
            {
                foreach (var state in states)
                {
                    if (state.hasRange)
                    {
                        state.tile.TerrainMinHeight = state.minHeight;
                        state.tile.TerrainMaxHeight = state.maxHeight;
                    }
                    state.tile.Water = state.water;
                }
            }
        }

        private static List<OmsiTileSummary> RevertedTiles(OmsiTileRevertChange change)
        {
            var tiles = new List<OmsiTileSummary> { change.Tile };
            foreach (var tile in change.TerrainTiles)
                if (!tiles.Contains(tile))
                    tiles.Add(tile);
            return tiles;
        }

        /// <summary>
        /// Adds an empty tile at a free place of the grid: a tile file as OMSI writes one, its terrain blended from the edges
        /// of the tiles beside it so the ground runs on without a step, and a [map] entry at the end of global.cfg's list,
        /// so no tile index changes. Everything goes to the workspace; the original map is not touched.
        /// </summary>
        public OmsiTileChange AddTile(OmsiMapSummary summary, int x, int y)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var workspace = RequireWorkspace();
            if (summary.Tiles.Exists(tile => tile.X == x && tile.Y == y))
                throw new InvalidOperationException("The map already has tile " + x + ", " + y + ".");
            var config = OmsiRecordFile.Parse(workspace.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            var maps = config.Records.FindAll(record => string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase));
            if (maps.Exists(record => record.TryGetInt(0, out var mapX) && record.TryGetInt(1, out var mapY) && mapX == x && mapY == y))
                throw new InvalidOperationException("global.cfg already lists tile " + x + ", " + y + ".");

            var fileName = "tile_" + x.ToString(CultureInfo.InvariantCulture) + "_" + y.ToString(CultureInfo.InvariantCulture) + ".map";
            var change = new OmsiTileChange(true, x, y);
            var insertAt = maps.Count > 0 ? config.Records.IndexOf(maps[maps.Count - 1]) + 1 : config.Records.Count;
            var configBefore = config.ToBytes();
            config.InsertRecord(insertAt, config.CreateRecord("map", new[]
            {
                x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture), fileName
            }));
            Record(change, workspace, GlobalConfigFileName, configBefore, config.ToBytes());
            Record(change, workspace, fileName, Existing(workspace, fileName), OmsiNewMap.CreateEmptyTile(DateTime.Now));
            Record(change, workspace, fileName + ".terrain", Existing(workspace, fileName + ".terrain"),
                OmsiTerrain.Encode(BlendedTerrain(summary, workspace, x, y)));
            ApplyFiles(change.Files, false);
            return change;
        }

        /// <summary>
        /// Deletes a tile from global.cfg's list. The tile stays when anything outside it needs it: an entry point on it,
        /// elements of other tiles, timetables or signal routes naming its elements, or chrono variants changing it, as
        /// these would break. Splines of other tiles joined to its splines get an open end. Tile indices after it move down
        /// by one wherever they are written: entry points, repeated rows along splines, timetables and signal routes, also
        /// those of chrono variants. Chains that ran through it get their new offsets beside it, and the rows along them
        /// stay where they stood. Its files stay where they are, unlisted, as OMSI ignores them.
        /// </summary>
        public OmsiTileDeletionResult DeleteTile(OmsiMapSummary summary, OmsiTileSummary tile)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            var workspace = RequireWorkspace();
            var blockers = new List<string>();
            var config = OmsiRecordFile.Parse(workspace.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            var maps = config.Records.FindAll(record => string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase) && record.Fields.Count == 3);
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var deleted = maps.FindIndex(record => SamePath(record.GetField(2), tilePath));
            if (deleted < 0)
                throw new InvalidOperationException(tile.FileName + " is not listed in global.cfg.");
            if (maps.Count == 1)
                return new OmsiTileDeletionResult(null, new[] { "It is the only tile of the map." });

            // Elements of the tile that something outside it needs.
            var ids = new HashSet<int>();
            foreach (var item in tile.Objects)
                ids.Add(item.Id);
            foreach (var item in tile.AttachedObjects)
                ids.Add(item.Id);
            foreach (var item in tile.Splines)
                ids.Add(item.Id);
            foreach (var item in tile.SplineAttachments)
                ids.Add(item.Id);
            var references = BuildReferenceIndex();
            var neighbourFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
                foreach (var reference in references.To(id))
                {
                    if (SamePath(reference.File, tilePath))
                        continue;
                    if (reference.Kind == SplineNeighbourReference)
                        neighbourFiles.Add(reference.File);
                    else
                        AddBlocker(blockers, "#" + id + " is needed by " + reference);
                }
            foreach (var chrono in workspace.EnumerateFiles("Chrono", Path.GetFileName(tilePath), true))
                AddBlocker(blockers, "The chrono variant " + Path.GetFileName(Path.GetDirectoryName(chrono)) + " changes this tile.");

            var files = new EditedTileFiles(workspace);
            var change = new OmsiTileChange(false, tile.X, tile.Y);

            // global.cfg: the entry, and entry points after it.
            var editedConfig = files.Get(GlobalConfigFileName, OmsiGlobalConfigLayouts.Instance);
            var editedMaps = editedConfig.Records.FindAll(record => string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase) && record.Fields.Count == 3);
            editedConfig.RemoveRecord(editedMaps[deleted]);
            var entryPoints = editedConfig.Records.Find(record => string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase) && record.HasKnownLayout);
            if (entryPoints != null)
                for (var field = 1; field + EntryPointFields <= entryPoints.Fields.Count; field += EntryPointFields)
                    ShiftField(entryPoints, field + 10, deleted, blockers, "An entry point (" + EntryPointName(entryPoints, field) + ") lies on this tile.");

            // Tiles and chrono change lists: repeated rows name their master's tile; spline neighbours in this tile go.
            var tileFiles = new List<string>();
            foreach (var record in maps)
                if (!SamePath(record.GetField(2), tilePath))
                    tileFiles.Add(OmsiRelativePath.Normalize(record.GetField(2)));
            tileFiles.AddRange(workspace.EnumerateFiles("Chrono", "tile_*.map", true));
            var tileSplines = new HashSet<int>();
            foreach (var spline in tile.Splines)
                tileSplines.Add(spline.Id);
            foreach (var path in tileFiles)
            {
                if (!workspace.FileExists(path))
                    continue;
                var file = files.Get(path, OmsiTileRecordLayouts.Instance);
                foreach (var record in file.Records)
                {
                    if (string.Equals(record.Name, "splineAttachement_repeater", StringComparison.OrdinalIgnoreCase))
                        ShiftField(record, 1, deleted, blockers, "A row along a spline in " + path + " continues a row of this tile.");
                    else if (neighbourFiles.Contains(path) && OmsiTileRecords.Create(file, record) is OmsiSplineView spline)
                    {
                        if (tileSplines.Contains(spline.PreviousId))
                            spline.PreviousId = 0;
                        if (tileSplines.Contains(spline.NextId))
                            spline.NextId = 0;
                    }
                }
            }

            // Timetables, also of chrono variants, and signal routes.
            var timetables = new List<string>(workspace.EnumerateFiles("TTData", "*", false));
            foreach (var path in workspace.EnumerateFiles("Chrono", "*", true))
                if (path.IndexOf(Path.DirectorySeparatorChar + "TTData" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                    timetables.Add(path);
            foreach (var path in timetables)
                ShiftTimetable(files, path, deleted, blockers);
            if (workspace.FileExists("signalroutes.cfg"))
            {
                var routes = files.Get("signalroutes.cfg", null);
                foreach (var record in routes.Records)
                {
                    if (string.Equals(record.Name, "entry", StringComparison.OrdinalIgnoreCase))
                        ShiftValue(record, 2, deleted, blockers, "A signal route runs over this tile.");
                    else if (string.Equals(record.Name, "signal", StringComparison.OrdinalIgnoreCase))
                        ShiftValue(record, 1, deleted, blockers, "A signal of a signal route stands on this tile.");
                }
            }

            if (blockers.Count > 0)
                return new OmsiTileDeletionResult(null, blockers);
            // Where every chain begins and every row counts from, before the tile goes.
            var chains = Chains(summary);
            files.Collect(change.Files);
            ApplyFiles(change.Files, false);
            KeepChainsAfterDeletion(change, chains);
            return new OmsiTileDeletionResult(change, new string[0]);
        }

        /// <summary>
        /// A chain that ran through the deleted tile now begins, or ends, at its edge: the splines of the tiles beside it
        /// get their new chain offsets, and the rows along them move their start by as much, so their objects stay where
        /// they stood (ADR 038). The files join the deletion's step.
        /// </summary>
        private void KeepChainsAfterDeletion(OmsiTileChange change, ChainState before)
        {
            var kept = new OmsiSplineChange(OmsiSplineChangeKind.ChainOffsets);
            KeepChains(Read(), kept, before);
            foreach (var file in kept.Files)
            {
                var state = change.Files.Find(item => string.Equals(item.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase));
                if (state != null)
                    state.After = file.After;
                else
                    change.Files.Add(file);
            }
        }

        /// <summary>Takes back an added or deleted tile, or does it again; fails without writing when a file changed since.</summary>
        public void ApplyTileChange(OmsiTileChange change, bool undo)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (change.IsApplied != undo)
                throw new InvalidOperationException(undo ? "The tile step is already taken back." : "The tile step is already done.");
            ApplyFiles(change.Files, undo);
            change.IsApplied = !undo;
        }

        /// <summary>A tile this project added: the workspace has its file, the original map does not.</summary>
        public bool IsTileAdded(string tileFileName)
        {
            var path = OmsiRelativePath.Normalize(tileFileName);
            return Workspace != null && Workspace.IsModified(path) && Workspace.ReadOriginalBytes(path) == null;
        }

        private const int EntryPointFields = 12;

        private static string EntryPointName(OmsiRecord entryPoints, int field)
        {
            return field + 11 < entryPoints.Fields.Count ? entryPoints.GetField(field + 11) : "?";
        }

        private static void ShiftTimetable(EditedTileFiles files, string path, int deleted, List<string> blockers)
        {
            var name = Path.GetFileName(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var busStops = string.Equals(name, "Busstops.cfg", StringComparison.OrdinalIgnoreCase);
            var stationLinks = string.Equals(name, "StnLinks.cfg", StringComparison.OrdinalIgnoreCase);
            if (!busStops && !stationLinks && extension != ".ttr" && extension != ".ttp")
                return;
            var file = files.Get(path, null);
            foreach (var record in file.Records)
            {
                var kind = record.Name.ToLowerInvariant();
                if (kind == "busstop" && busStops)
                    ShiftValue(record, 1, deleted, blockers, "A bus stop of the timetable stands on this tile.");
                else if (kind == "stnlink_entry" && stationLinks)
                    ShiftValue(record, 2, deleted, blockers, "A station link of the timetable runs over this tile.");
                else if (kind == "track_entry")
                    ShiftValue(record, 2, deleted, blockers, "A track of the timetable (" + name + ") runs over this tile.");
                else if (kind == "station")
                    ShiftValue(record, 3, deleted, blockers, "A trip of the timetable (" + name + ") stops on this tile.");
            }
        }

        /// <summary>Moves a tile index in a field down by one when it lies after the deleted tile; on the deleted tile it blocks.</summary>
        private static void ShiftField(OmsiRecord record, int field, int deleted, List<string> blockers, string blocker)
        {
            if (field >= record.Fields.Count || !record.TryGetInt(field, out var index))
                return;
            if (index == deleted)
                AddBlocker(blockers, blocker);
            else if (index > deleted)
                record.SetField(field, OmsiNumberFormat.Format(index - 1));
        }

        /// <summary>The same for the value at a position among a record's fields and following lines, as records without a layout hold them.</summary>
        private static void ShiftValue(OmsiRecord record, int position, int deleted, List<string> blockers, string blocker)
        {
            var line = position < record.Fields.Count ? record.Fields[position]
                : position - record.Fields.Count < record.Trailer.Count ? record.Trailer[position - record.Fields.Count] : null;
            if (line == null || !int.TryParse(line.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                return;
            if (index == deleted)
                AddBlocker(blockers, blocker);
            else if (index > deleted)
                line.Text = (index - 1).ToString(CultureInfo.InvariantCulture);
        }

        private static void AddBlocker(List<string> blockers, string blocker)
        {
            if (!blockers.Contains(blocker))
                blockers.Add(blocker);
        }

        /// <summary>
        /// Terrain for a new tile: its edges take the heights of the tiles beside it, the inside blends them by the distance
        /// to each edge, so the ground runs on without a step. Without terrain beside it, flat at 0 m.
        /// </summary>
        private float[] BlendedTerrain(OmsiMapSummary summary, IOmsiFileSource source, int x, int y)
        {
            const int n = OmsiTerrain.Resolution;
            float[] Neighbour(int dx, int dy)
            {
                var tile = summary.Tiles.Find(candidate => candidate.X == x + dx && candidate.Y == y + dy && candidate.IsAvailable && candidate.HasTerrain);
                if (tile == null)
                    return null;
                var path = OmsiRelativePath.Normalize(tile.FileName) + ".terrain";
                return source.FileExists(path) ? OmsiTerrain.Parse(source.ReadAllBytes(path)).CopyHeights() : null;
            }
            var west = Neighbour(-1, 0);
            var east = Neighbour(1, 0);
            var south = Neighbour(0, -1);
            var north = Neighbour(0, 1);
            var heights = new float[n * n];
            for (var row = 0; row < n; row++)
                for (var column = 0; column < n; column++)
                {
                    double sum = 0.0, weights = 0.0;
                    // Rows run from south to north, columns from west to east; neighbours share their edge rows and columns.
                    void Add(float[] edge, int edgeRow, int edgeColumn, int distance)
                    {
                        if (edge == null)
                            return;
                        if (distance == 0)
                        {
                            sum = edge[edgeRow * n + edgeColumn] * 1e9;
                            weights = 1e9;
                            return;
                        }
                        if (weights >= 1e9)
                            return;
                        var weight = 1.0 / distance;
                        sum += edge[edgeRow * n + edgeColumn] * weight;
                        weights += weight;
                    }
                    Add(west, row, n - 1, column);
                    Add(east, row, 0, n - 1 - column);
                    Add(south, n - 1, column, row);
                    Add(north, 0, column, n - 1 - row);
                    heights[row * n + column] = weights > 0.0 ? (float)(sum / weights) : 0f;
                }
            return heights;
        }

        private static byte[] Existing(IOmsiFileSource source, string path)
        {
            return source.FileExists(path) ? source.ReadAllBytes(path) : null;
        }

        private static void Record(OmsiTileChange change, OmsiMapWorkspace workspace, string path, byte[] before, byte[] after)
        {
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = before, After = after });
        }

        /// <summary>
        /// Writes the files of a step, all or none; a file without content goes from the workspace, as the original map lacks it. Throws
        /// <see cref="OmsiStepRefusedException"/> without writing when a file is no longer as the step left it. When a write fails, the
        /// files written so far are put back and the failure is thrown as it was, so the workspace is as before the call; when putting
        /// them back fails too, <see cref="OmsiRollBackFailedException"/> reports both.
        /// </summary>
        private void ApplyFiles(List<OmsiObjectChange.FileState> files, bool undo)
        {
            var workspace = RequireWorkspace();
            foreach (var file in files)
            {
                var expected = undo ? file.After : file.Before;
                var actual = Existing(workspace, file.RelativePath);
                if (!SameState(expected, actual))
                    throw new OmsiStepRefusedException(file.RelativePath + " was changed in the meantime; the step cannot be " + (undo ? "undone." : "applied."));
            }
            // A file counts as touched before its write starts: a save that fails can already have taken it off the list of deleted files.
            var touched = new List<OmsiObjectChange.FileState>();
            try
            {
                foreach (var file in files)
                {
                    touched.Add(file);
                    WriteState(workspace, file.RelativePath, undo ? file.Before : file.After);
                }
            }
            catch (Exception failure)
            {
                RollBackAfter(failure, () => PutBack(workspace, touched, undo));
                throw;
            }
        }

        /// <summary>
        /// Puts the files a step began to write back as the step found them, last first; a file still as it was is left alone, so one whose
        /// write failed before changing anything needs no write. Tries every file; throws the failure, or all of them, at the end.
        /// </summary>
        private static void PutBack(OmsiMapWorkspace workspace, List<OmsiObjectChange.FileState> touched, bool undo)
        {
            var failures = new List<Exception>();
            for (var index = touched.Count - 1; index >= 0; index--)
            {
                var file = touched[index];
                var before = undo ? file.After : file.Before;
                try
                {
                    if (!SameState(before, Existing(workspace, file.RelativePath)))
                        WriteState(workspace, file.RelativePath, before);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            if (failures.Count == 1)
                throw failures[0];
            if (failures.Count > 1)
                throw new AggregateException(failures);
        }

        /// <summary>Gives a file of the workspace these bytes; null takes it away, as the original map lacks it or as the project deletes it.</summary>
        private static void WriteState(OmsiMapWorkspace workspace, string relativePath, byte[] bytes)
        {
            if (bytes != null)
                workspace.Save(relativePath, bytes);
            else if (workspace.ReadOriginalBytes(relativePath) == null)
                workspace.Revert(relativePath);
            else
                workspace.Delete(relativePath);
        }

        /// <summary>Files read once for a tile step, with their bytes before it; the encoding of each stays as it is.</summary>
        private sealed class EditedTileFiles
        {
            private readonly OmsiMapWorkspace workspace;
            private readonly Dictionary<string, OmsiRecordFile> files = new Dictionary<string, OmsiRecordFile>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, byte[]> before = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> order = new List<string>();

            public EditedTileFiles(OmsiMapWorkspace workspace)
            {
                this.workspace = workspace;
            }

            public OmsiRecordFile Get(string path, IOmsiRecordLayouts layouts)
            {
                if (files.TryGetValue(path, out var file))
                    return file;
                var bytes = workspace.ReadAllBytes(path);
                file = OmsiRecordFile.Parse(bytes, layouts);
                files[path] = file;
                before[path] = bytes;
                order.Add(path);
                return file;
            }

            public void Collect(List<OmsiObjectChange.FileState> states)
            {
                foreach (var path in order)
                {
                    var after = files[path].ToBytes();
                    if (!SameBytes(before[path], after))
                        states.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = before[path], After = after });
                }
            }
        }
    }
}
