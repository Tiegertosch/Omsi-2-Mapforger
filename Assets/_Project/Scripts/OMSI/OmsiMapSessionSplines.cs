using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One end of a placed spline, where a new road can join it.</summary>
    public sealed class OmsiSplineEnd
    {
        public OmsiSplineEnd(OmsiTileSummary tile, OmsiSplineRecord spline, bool atEnd)
        {
            Tile = tile ?? throw new ArgumentNullException(nameof(tile));
            Spline = spline ?? throw new ArgumentNullException(nameof(spline));
            AtEnd = atEnd;
        }

        public OmsiTileSummary Tile { get; }
        public OmsiSplineRecord Spline { get; }

        /// <summary>True for the end of the spline, false for its start.</summary>
        public bool AtEnd { get; }

        /// <summary>True when no spline is connected here by ID.</summary>
        public bool IsFree => (AtEnd ? Spline.NextId : Spline.PreviousId) == 0;

        public double Height => AtEnd ? Spline.EndHeight : Spline.Height;

        /// <summary>The world position of this end.</summary>
        public OmsiMapPoint Position(double tileSizeMeters)
        {
            return OmsiMapGeometry.GetSplineEndpoint(Tile, Spline, tileSizeMeters, AtEnd);
        }

        /// <summary>The heading a road continuing from this end leaves with: onwards at the end, backwards at the start.</summary>
        public double OutwardHeadingDegrees
        {
            get
            {
                if (!AtEnd)
                    return Spline.HeadingDegrees + 180.0;
                OmsiMapGeometry.GetLocalFrameAt(Spline, Spline.LengthMeters, out _, out var heading);
                return heading * 180.0 / Math.PI;
            }
        }

        /// <summary>The nearest end without a connected spline within <paramref name="maximumDistance"/> metres, or null.</summary>
        public static OmsiSplineEnd FindFree(OmsiMapSummary summary, double worldX, double worldY, double maximumDistance)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            OmsiSplineEnd best = null;
            var bestDistance = maximumDistance;
            foreach (var tile in summary.Tiles)
            {
                foreach (var spline in tile.Splines)
                {
                    for (var end = 0; end < 2; end++)
                    {
                        var candidate = new OmsiSplineEnd(tile, spline, end == 1);
                        if (!candidate.IsFree)
                            continue;
                        var position = candidate.Position(summary.TileSizeMeters);
                        var distance = Math.Sqrt((position.X - worldX) * (position.X - worldX) + (position.Y - worldY) * (position.Y - worldY));
                        if (distance <= bestDistance)
                        {
                            bestDistance = distance;
                            best = candidate;
                        }
                    }
                }
            }
            return best;
        }
    }

    public enum OmsiSplineChangeKind
    {
        Creation,
        Deletion
    }

    /// <summary>
    /// Created or deleted splines, with the bytes of every file before and after, so undo and redo restore exactly what
    /// the change wrote, including the neighbour IDs of joined splines.
    /// </summary>
    public sealed class OmsiSplineChange
    {
        internal OmsiSplineChange(OmsiSplineChangeKind kind)
        {
            Kind = kind;
        }

        public OmsiSplineChangeKind Kind { get; }

        /// <summary>IDs of the splines created or deleted.</summary>
        public List<int> SplineIds { get; } = new List<int>();

        /// <summary>True while the change is in effect, false after it was undone.</summary>
        public bool IsApplied { get; internal set; } = true;

        /// <summary>The tiles whose splines changed, to draw again.</summary>
        public List<OmsiTileSummary> Tiles { get; } = new List<OmsiTileSummary>();

        internal List<OmsiObjectChange.FileState> Files { get; } = new List<OmsiObjectChange.FileState>();
    }

    /// <summary>Outcome of deleting a spline: the change, or the references that prevented it.</summary>
    public sealed class OmsiSplineChangeResult
    {
        internal OmsiSplineChangeResult(OmsiSplineChange change, IReadOnlyList<OmsiReference> blockers)
        {
            Change = change;
            Blockers = blockers;
        }

        public OmsiSplineChange Change { get; }
        public IReadOnlyList<OmsiReference> Blockers { get; }
        public bool Succeeded => Change != null;
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>The kind of reference a spline's neighbours hold to it; deleting the spline opens those ends instead.</summary>
        private const string SplineNeighbourReference = "spline neighbour";

        /// <summary>
        /// Builds a planned road as new splines of the profile <paramref name="assetPath"/>: each piece at the end of the
        /// tile its start lies in, with new map-wide IDs, the pieces linked to each other and to the joined splines by
        /// their neighbour IDs, and [NextIDCode] raised. Existing indices do not change.
        /// </summary>
        /// <param name="startJoin">A free spline end the road starts from, or null.</param>
        /// <param name="endJoin">A free spline end the road runs into, or null.</param>
        public OmsiSplineChange CreateRoad(OmsiMapSummary summary, OmsiRoadPlan plan, string assetPath, OmsiSplineEnd startJoin, OmsiSplineEnd endJoin)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("A spline profile (.sli) is required.", nameof(assetPath));
            if (plan.HasErrors || plan.Pieces.Count == 0)
                throw new InvalidOperationException("The road cannot be built: " +
                                                    (plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error)?.Message ?? "it has no pieces."));
            if (startJoin != null && !startJoin.IsFree || endJoin != null && !endJoin.IsFree)
                throw new InvalidOperationException("A spline end the road joins is already connected.");
            if (startJoin != null && endJoin != null && ReferenceEquals(startJoin.Spline, endJoin.Spline) && startJoin.AtEnd == endJoin.AtEnd)
                throw new InvalidOperationException("A road cannot start and end at the same spline end.");

            var workspace = RequireWorkspace();
            var files = new EditedFiles(workspace);
            var config = files.Config(GlobalConfigFileName);
            ids = ids ?? OmsiIdAllocator.ForMap(workspace, GlobalConfigFileName);

            var tiles = new List<OmsiTileSummary>();
            var pieceIds = new List<int>();
            foreach (var piece in plan.Pieces)
            {
                var tile = TileAt(summary, piece.StartX, piece.StartY);
                if (tile == null)
                    throw new InvalidOperationException("Part of the road starts outside the map's tiles.");
                if (!tiles.Contains(tile))
                    tiles.Add(tile);
                var tileFile = files.Tile(tile.FileName);
                if (tileFile.Version >= 0 && tileFile.Version < 11)
                    throw new InvalidOperationException("Splines can only be added to tiles of version 11 or later; " + tile.FileName + " has version " +
                                                        tileFile.Version + ".");
                pieceIds.Add(ids.Allocate());
            }

            for (var index = 0; index < plan.Pieces.Count; index++)
            {
                var piece = plan.Pieces[index];
                var tile = TileAt(summary, piece.StartX, piece.StartY);
                var tileFile = files.Tile(tile.FileName);
                var previous = index > 0 ? pieceIds[index - 1] : startJoin?.Spline.Id ?? 0;
                var next = index + 1 < pieceIds.Count ? pieceIds[index + 1] : endJoin?.Spline.Id ?? 0;
                var fields = new List<string>
                {
                    "0", assetPath.Trim(), OmsiNumberFormat.Format(pieceIds[index]), OmsiNumberFormat.Format(previous), OmsiNumberFormat.Format(next),
                    OmsiNumberFormat.Format(piece.StartX - tile.X * summary.TileSizeMeters), OmsiNumberFormat.Format(piece.StartHeight),
                    OmsiNumberFormat.Format(piece.StartY - tile.Y * summary.TileSizeMeters), OmsiNumberFormat.Format(piece.HeadingDegrees),
                    OmsiNumberFormat.Format(piece.LengthMeters), OmsiNumberFormat.Format(piece.RadiusMeters),
                    OmsiNumberFormat.Format(piece.GradientStartPercent), OmsiNumberFormat.Format(piece.GradientEndPercent), "0", "0"
                };
                // Version 14 adds the skew at start and end before the alignment length.
                if (tileFile.Version < 0 || tileFile.Version >= 14)
                    fields.AddRange(new[] { "0", "0" });
                fields.Add("0");
                tileFile.InsertRecord(tileFile.Records.Count, tileFile.CreateRecord("spline", fields));
            }

            if (startJoin != null)
                Link(files, startJoin, pieceIds[0], tiles);
            if (endJoin != null)
                Link(files, endJoin, pieceIds[pieceIds.Count - 1], tiles);
            ids.ApplyTo(config);

            var change = new OmsiSplineChange(OmsiSplineChangeKind.Creation);
            change.SplineIds.AddRange(pieceIds);
            change.Tiles.AddRange(tiles);
            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, pieceIds[pieceIds.Count - 1]);
            summary.NextIdCode = ids.NextFreeId;
            return change;
        }

        /// <summary>
        /// Deletes a spline with the rules that follow it. Splines joined to it keep their place and get an open end.
        /// Nothing changes while objects ride on it or timetables or chrono changes name it; those references are returned.
        /// </summary>
        /// <param name="references">An index of the current files; null builds one.</param>
        public OmsiSplineChangeResult DeleteSpline(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineRecord spline,
            OmsiReferenceIndex references = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));

            var workspace = RequireWorkspace();
            references = references ?? BuildReferenceIndex();
            var blockers = new List<OmsiReference>();
            var neighbourFiles = new List<string>();
            foreach (var reference in references.To(spline.Id))
            {
                if (reference.Kind == SplineNeighbourReference)
                {
                    if (!neighbourFiles.Contains(reference.File))
                        neighbourFiles.Add(reference.File);
                }
                else
                    blockers.Add(reference);
            }
            if (blockers.Count > 0)
                return new OmsiSplineChangeResult(null, blockers);

            var files = new EditedFiles(workspace);
            var config = files.Config(GlobalConfigFileName);
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var tileFile = files.Tile(tile.FileName);
            OmsiSplineView view = null;
            foreach (var candidate in OmsiTileRecords.Of<OmsiSplineView>(tileFile))
                if (candidate.Id == spline.Id)
                    view = candidate;
            if (view == null)
                throw new InvalidOperationException("Spline " + spline.Id + " was not found in " + tile.FileName + ".");

            var change = new OmsiSplineChange(OmsiSplineChangeKind.Deletion);
            change.SplineIds.Add(spline.Id);
            change.Tiles.Add(tile);
            foreach (var file in neighbourFiles)
            {
                foreach (var neighbour in OmsiTileRecords.Of<OmsiSplineView>(files.Tile(file)))
                {
                    if (neighbour.Id == spline.Id)
                        continue;
                    if (neighbour.PreviousId == spline.Id)
                        neighbour.PreviousId = 0;
                    if (neighbour.NextId == spline.Id)
                        neighbour.NextId = 0;
                }
                var neighbourTile = summary.Tiles.Find(candidate => SamePath(candidate.FileName, file));
                if (neighbourTile != null && !change.Tiles.Contains(neighbourTile))
                    change.Tiles.Add(neighbourTile);
            }

            var result = OmsiTileEditing.RemoveWithBlockers(tileFile, view, TileIndexOf(config, tilePath), config, blockers);
            if (!result.Removed)
                return new OmsiSplineChangeResult(null, result.Blockers);

            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            return new OmsiSplineChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>Takes back created or deleted splines; fails without writing when a file was changed since.</summary>
        public void Undo(OmsiMapSummary summary, OmsiSplineChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (!change.IsApplied)
                throw new InvalidOperationException("The change has already been undone.");
            WriteFiles(change.Files, true);
            change.IsApplied = false;
            RefreshSplines(summary, change);
        }

        /// <summary>Applies undone spline changes again.</summary>
        public void Redo(OmsiMapSummary summary, OmsiSplineChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (change.IsApplied)
                throw new InvalidOperationException("The change is already applied.");
            WriteFiles(change.Files, false);
            change.IsApplied = true;
            RefreshSplines(summary, change);
        }

        private static void Link(EditedFiles files, OmsiSplineEnd join, int newId, List<OmsiTileSummary> tiles)
        {
            foreach (var view in OmsiTileRecords.Of<OmsiSplineView>(files.Tile(join.Tile.FileName)))
            {
                if (view.Id != join.Spline.Id)
                    continue;
                if (join.AtEnd)
                    view.NextId = newId;
                else
                    view.PreviousId = newId;
                if (!tiles.Contains(join.Tile))
                    tiles.Add(join.Tile);
                return;
            }
            throw new InvalidOperationException("Spline " + join.Spline.Id + " was not found in " + join.Tile.FileName + ".");
        }

        /// <summary>The available tile whose square holds a world position, or null.</summary>
        private static OmsiTileSummary TileAt(OmsiMapSummary summary, double worldX, double worldY)
        {
            var x = (int)Math.Floor(worldX / summary.TileSizeMeters);
            var y = (int)Math.Floor(worldY / summary.TileSizeMeters);
            return summary.Tiles.Find(tile => tile.X == x && tile.Y == y && tile.IsAvailable);
        }

        /// <summary>Reads the splines and rows along them of the changed tiles again from the workspace.</summary>
        private void RefreshSplines(OmsiMapSummary summary, OmsiSplineChange change)
        {
            var workspace = RequireWorkspace();
            foreach (var tile in change.Tiles)
            {
                var fresh = OmsiMapReader.ReadAddedElements(workspace, OmsiRelativePath.Normalize(tile.FileName), tile);
                if (summary != null)
                {
                    summary.SplineCount += fresh.Splines.Count - tile.Splines.Count;
                    summary.AttachmentCount += fresh.SplineAttachments.Count - tile.SplineAttachments.Count;
                }
                tile.AttachmentCount += fresh.SplineAttachments.Count - tile.SplineAttachments.Count;
                tile.Splines.Clear();
                tile.Splines.AddRange(fresh.Splines);
                tile.SplineAttachments.Clear();
                tile.SplineAttachments.AddRange(fresh.SplineAttachments);
                tile.SplineCount = tile.Splines.Count;
            }
        }

        private void WriteFiles(List<OmsiObjectChange.FileState> files, bool undo)
        {
            var workspace = RequireWorkspace();
            foreach (var file in files)
                if (!SameBytes(workspace.ReadAllBytes(file.RelativePath), undo ? file.After : file.Before))
                    throw new InvalidOperationException(file.RelativePath + " was changed in the meantime; the step cannot be " +
                                                        (undo ? "undone." : "applied."));
            foreach (var file in files)
                workspace.Save(file.RelativePath, undo ? file.Before : file.After);
        }

        /// <summary>Tile files and global.cfg read once for an edit, with their bytes before it.</summary>
        private sealed class EditedFiles
        {
            private readonly OmsiMapWorkspace workspace;
            private readonly Dictionary<string, OmsiRecordFile> files = new Dictionary<string, OmsiRecordFile>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, byte[]> before = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> order = new List<string>();

            public EditedFiles(OmsiMapWorkspace workspace)
            {
                this.workspace = workspace;
            }

            public OmsiRecordFile Tile(string fileName)
            {
                return Get(OmsiRelativePath.Normalize(fileName), OmsiTileRecordLayouts.Instance);
            }

            public OmsiRecordFile Config(string fileName)
            {
                return Get(fileName, OmsiGlobalConfigLayouts.Instance);
            }

            private OmsiRecordFile Get(string relativePath, IOmsiRecordLayouts layouts)
            {
                if (files.TryGetValue(relativePath, out var file))
                    return file;
                var bytes = workspace.ReadAllBytes(relativePath);
                file = OmsiRecordFile.Parse(bytes, layouts);
                files.Add(relativePath, file);
                before.Add(relativePath, bytes);
                order.Add(relativePath);
                return file;
            }

            /// <summary>Every file whose bytes the edit changed, before and after.</summary>
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
