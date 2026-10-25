using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Rows of objects along roads added or deleted: the bytes of every file before and after, so the step can be taken
    /// back and done again exactly, and the rows it added or removed.
    /// </summary>
    public sealed class OmsiAttachmentChange
    {
        internal OmsiAttachmentChange()
        {
        }

        public bool IsApplied { get; internal set; } = true;

        /// <summary>The rows the change added, with their tiles.</summary>
        public List<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>> Added { get; } = new List<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>>();

        /// <summary>The rows the change removed, with their tiles.</summary>
        public List<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>> Removed { get; } = new List<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>>();

        internal List<OmsiObjectChange.FileState> Files { get; } = new List<OmsiObjectChange.FileState>();
    }

    /// <summary>What deleting a row did, or the references that keep it.</summary>
    public sealed class OmsiAttachmentDeletionResult
    {
        internal OmsiAttachmentDeletionResult(OmsiAttachmentChange change, IReadOnlyList<OmsiReference> blockers)
        {
            Change = change;
            Blockers = blockers;
        }

        public OmsiAttachmentChange Change { get; }
        public IReadOnlyList<OmsiReference> Blockers { get; }
        public bool Succeeded => Change != null;
    }

    /// <summary>What changing a row did, or why it stayed as it was: references to records it would remove, or a problem of the plan.</summary>
    public sealed class OmsiAttachmentEditResult
    {
        internal OmsiAttachmentEditResult(OmsiAttachmentChange change, IReadOnlyList<OmsiReference> blockers, string problem)
        {
            Change = change;
            Blockers = blockers;
            Problem = problem;
        }

        public OmsiAttachmentChange Change { get; }
        public IReadOnlyList<OmsiReference> Blockers { get; }
        public string Problem { get; }
        public bool Succeeded => Change != null;
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Changes a row of the map (<see cref="OmsiAttachmentRows.RowOf"/>) to new values: plans it again over the same
        /// stretch of road and writes the parts into its records tile by tile in order, keeping their IDs; further parts go
        /// to the end of their tile with a new ID, records left over are removed unless something names them. A row
        /// continued by [splineAttachement_repeater] gets the values in its record and every continuation instead.
        /// </summary>
        public OmsiAttachmentEditResult ChangeAttachmentRow(OmsiMapSummary summary, OmsiAttachmentRow row, OmsiAttachmentRowRequest request)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.AssetPath))
                throw new ArgumentException("An object file path is required.", nameof(request));

            var workspace = RequireWorkspace();
            var change = new OmsiAttachmentChange();
            var files = new Dictionary<string, KeyValuePair<byte[], OmsiRecordFile>>(StringComparer.OrdinalIgnoreCase);
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            var configChanged = false;
            var allocated = false;

            if (row.Continues)
            {
                var id = row.Records[0].Value.Id;
                foreach (var tile in summary.Tiles)
                    foreach (var record in tile.SplineAttachments)
                    {
                        if (record.Id != id)
                            continue;
                        var changed = OmsiAttachmentRows.Changed(record, request);
                        Write(FindAttachment(Open(files, workspace, tile), tile, record), changed);
                        change.Removed.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(tile, record));
                        change.Added.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(tile, changed));
                    }
            }
            else
            {
                var plan = OmsiAttachmentRows.Replan(summary, new OmsiSplineAttachmentLayout(summary), row, request);
                if (plan.Problem != null)
                    return new OmsiAttachmentEditResult(null, new OmsiReference[0], plan.Problem);
                if (plan.Parts.Count == 0)
                    return new OmsiAttachmentEditResult(null, new OmsiReference[0], "The row would have no objects.");
                var left = new List<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>>(row.Records);
                foreach (var part in plan.Parts)
                {
                    var record = OmsiAttachmentRows.Record(part, request);
                    var file = Open(files, workspace, part.Tile);
                    var index = left.FindIndex(item => ReferenceEquals(item.Key, part.Tile));
                    if (index >= 0)
                    {
                        record.Id = left[index].Value.Id;
                        Write(FindAttachment(file, part.Tile, left[index].Value), record);
                        change.Removed.Add(left[index]);
                        left.RemoveAt(index);
                    }
                    else
                    {
                        ids = ids ?? OmsiIdAllocator.ForMap(workspace, GlobalConfigFileName);
                        record.Id = OmsiTileEditing.AppendSplineAttachment(file, ids, record).Id;
                        allocated = true;
                    }
                    change.Added.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(part.Tile, record));
                }
                OmsiReferenceIndex references = null;
                foreach (var old in left)
                {
                    references = references ?? BuildReferenceIndex();
                    var file = Open(files, workspace, old.Key);
                    var result = OmsiTileEditing.Remove(file, FindAttachment(file, old.Key, old.Value), TileIndexOf(config, OmsiRelativePath.Normalize(old.Key.FileName)),
                        config, references);
                    if (!result.Removed)
                        return new OmsiAttachmentEditResult(null, result.Blockers, null);
                    configChanged |= result.ShiftedEntryPointIndices > 0;
                    change.Removed.Add(old);
                }
                if (allocated)
                {
                    ids.ApplyTo(config);
                    configChanged = true;
                }
            }

            foreach (var pair in files)
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = pair.Key, Before = pair.Value.Key, After = pair.Value.Value.ToBytes() });
            if (configChanged)
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = GlobalConfigFileName, Before = configBefore, After = config.ToBytes() });
            ApplyFiles(change.Files, false);
            ApplyRows(summary, change, true);
            foreach (var pair in change.Added)
                summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, pair.Value.Id);
            if (allocated)
                summary.NextIdCode = ids.NextFreeId;
            return new OmsiAttachmentEditResult(change, new OmsiReference[0], null);
        }

        /// <summary>A tile's file read from the workspace once per change, with its bytes before.</summary>
        private static OmsiRecordFile Open(Dictionary<string, KeyValuePair<byte[], OmsiRecordFile>> files, OmsiMapWorkspace workspace, OmsiTileSummary tile)
        {
            var path = OmsiRelativePath.Normalize(tile.FileName);
            if (!files.TryGetValue(path, out var file))
            {
                var bytes = workspace.ReadAllBytes(path);
                file = new KeyValuePair<byte[], OmsiRecordFile>(bytes, OmsiRecordFile.Parse(bytes, OmsiTileRecordLayouts.Instance));
                files[path] = file;
            }
            return file.Value;
        }

        private static OmsiSplineAttachmentView FindAttachment(OmsiRecordFile file, OmsiTileSummary tile, OmsiSplineAttachmentRecord record)
        {
            foreach (var view in OmsiTileRecords.Of<OmsiSplineAttachmentView>(file))
                if (view.IsRepeater == record.IsRepeater && view.Id == record.Id)
                    return view;
            throw new InvalidOperationException("Row " + record.Id + " was not found in " + tile.FileName + ".");
        }

        /// <summary>Writes a row's values into its record; the detail level, pitch, bank, tilt and texts stay.</summary>
        private static void Write(OmsiSplineAttachmentView view, OmsiSplineAttachmentRecord record)
        {
            view.AssetPath = record.AssetPath;
            view.SplineIndex = record.SplineIndex;
            view.LateralOffset = record.LateralOffset;
            view.Height = record.Height;
            view.StartDistance = record.StartDistance;
            view.HeadingDegrees = record.HeadingDegrees;
            view.Interval = record.Interval;
            view.Range = record.Range;
        }

        /// <summary>
        /// Writes a planned row (<see cref="OmsiAttachmentRows.Plan"/>): one [splineAttachement] per part at the end of its
        /// tile with a new map-wide ID, and [NextIDCode] raised in global.cfg.
        /// </summary>
        public OmsiAttachmentChange AddAttachmentRow(OmsiMapSummary summary, OmsiAttachmentRowPlan plan)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (plan.Problem != null)
                throw new InvalidOperationException(plan.Problem);
            if (plan.Parts.Count == 0)
                throw new InvalidOperationException("The row has no objects.");

            var workspace = RequireWorkspace();
            ids = ids ?? OmsiIdAllocator.ForMap(workspace, GlobalConfigFileName);
            var change = new OmsiAttachmentChange();
            var files = new Dictionary<string, KeyValuePair<byte[], OmsiRecordFile>>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in plan.Parts)
            {
                var tilePath = OmsiRelativePath.Normalize(part.Tile.FileName);
                if (!files.TryGetValue(tilePath, out var file))
                {
                    var bytes = workspace.ReadAllBytes(tilePath);
                    file = new KeyValuePair<byte[], OmsiRecordFile>(bytes, OmsiRecordFile.Parse(bytes, OmsiTileRecordLayouts.Instance));
                    files[tilePath] = file;
                }
                var view = OmsiTileEditing.AppendSplineAttachment(file.Value, ids, OmsiAttachmentRows.Record(part, plan.Request));
                change.Added.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(part.Tile, new OmsiSplineAttachmentRecord
                {
                    AssetPath = view.AssetPath, Id = view.Id, SplineIndex = view.SplineIndex, LateralOffset = view.LateralOffset, Height = view.Height,
                    StartDistance = view.StartDistance, HeadingDegrees = view.HeadingDegrees, Interval = view.Interval, Range = view.Range
                }));
            }
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            ids.ApplyTo(config);
            foreach (var pair in files)
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = pair.Key, Before = pair.Value.Key, After = pair.Value.Value.ToBytes() });
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = GlobalConfigFileName, Before = configBefore, After = config.ToBytes() });
            ApplyFiles(change.Files, false);
            ApplyRows(summary, change, true);
            foreach (var pair in change.Added)
                summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, pair.Value.Id);
            summary.NextIdCode = ids.NextFreeId;
            return change;
        }

        /// <summary>
        /// Deletes a row of objects along a road. Nothing changes while timetables, chrono changes or other records name
        /// it, such as a bus stop laid along a road; those references are returned.
        /// </summary>
        public OmsiAttachmentDeletionResult DeleteAttachmentRow(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineAttachmentRecord row)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            if (row.IsRepeater)
                throw new InvalidOperationException("A continuation of a row on another tile is deleted with its row.");

            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var tileBefore = workspace.ReadAllBytes(tilePath);
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var tileFile = OmsiRecordFile.Parse(tileBefore, OmsiTileRecordLayouts.Instance);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            OmsiSplineAttachmentView view = null;
            foreach (var candidate in OmsiTileRecords.Of<OmsiSplineAttachmentView>(tileFile))
                if (!candidate.IsRepeater && candidate.Id == row.Id)
                    view = candidate;
            if (view == null)
                throw new InvalidOperationException("Row " + row.Id + " was not found in " + tile.FileName + ".");

            var result = OmsiTileEditing.Remove(tileFile, view, TileIndexOf(config, tilePath), config, BuildReferenceIndex());
            if (!result.Removed)
                return new OmsiAttachmentDeletionResult(null, result.Blockers);
            var change = new OmsiAttachmentChange();
            change.Removed.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(tile, row));
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = tilePath, Before = tileBefore, After = tileFile.ToBytes() });
            if (result.ShiftedEntryPointIndices > 0)
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = GlobalConfigFileName, Before = configBefore, After = config.ToBytes() });
            ApplyFiles(change.Files, false);
            ApplyRows(summary, change, true);
            return new OmsiAttachmentDeletionResult(change, new OmsiReference[0]);
        }

        /// <summary>Takes back rows added or deleted: writes the files as they were, unless one was changed since.</summary>
        public void Undo(OmsiMapSummary summary, OmsiAttachmentChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (!change.IsApplied)
                throw new InvalidOperationException("The change has already been undone.");
            ApplyFiles(change.Files, true);
            ApplyRows(summary, change, false);
            change.IsApplied = false;
        }

        /// <summary>Applies an undone change of rows again.</summary>
        public void Redo(OmsiMapSummary summary, OmsiAttachmentChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (change.IsApplied)
                throw new InvalidOperationException("The change is already applied.");
            ApplyFiles(change.Files, false);
            ApplyRows(summary, change, true);
            change.IsApplied = true;
        }

        /// <summary>Adds and removes the rows of a change in the summary, or the other way round when taking it back.</summary>
        private static void ApplyRows(OmsiMapSummary summary, OmsiAttachmentChange change, bool forward)
        {
            foreach (var pair in forward ? change.Added : change.Removed)
                if (!pair.Key.SplineAttachments.Contains(pair.Value))
                {
                    pair.Key.SplineAttachments.Add(pair.Value);
                    pair.Key.AttachmentCount++;
                    if (summary != null)
                        summary.AttachmentCount++;
                }
            foreach (var pair in forward ? change.Removed : change.Added)
                if (pair.Key.SplineAttachments.Remove(pair.Value))
                {
                    pair.Key.AttachmentCount--;
                    if (summary != null)
                        summary.AttachmentCount--;
                }
        }
    }
}
