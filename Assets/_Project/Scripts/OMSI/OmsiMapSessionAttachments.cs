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

    public sealed partial class OmsiMapSession
    {
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
