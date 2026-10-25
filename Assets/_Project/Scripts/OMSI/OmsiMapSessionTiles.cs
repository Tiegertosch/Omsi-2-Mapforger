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

    public sealed partial class OmsiMapSession
    {
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

        /// <summary>Writes the files of a tile step; a file without content goes from the workspace, as the original map lacks it.</summary>
        private void ApplyFiles(List<OmsiObjectChange.FileState> files, bool undo)
        {
            var workspace = RequireWorkspace();
            foreach (var file in files)
            {
                var expected = undo ? file.After : file.Before;
                var actual = Existing(workspace, file.RelativePath);
                if (expected == null ? actual != null : actual == null || !SameBytes(actual, expected))
                    throw new InvalidOperationException(file.RelativePath + " was changed in the meantime; the step cannot be " + (undo ? "undone." : "applied."));
            }
            foreach (var file in files)
            {
                var target = undo ? file.Before : file.After;
                if (target != null)
                    workspace.Save(file.RelativePath, target);
                else if (workspace.ReadOriginalBytes(file.RelativePath) == null)
                    workspace.Revert(file.RelativePath);
                else
                    throw new InvalidOperationException(file.RelativePath + " belongs to the original map and cannot be removed.");
            }
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
