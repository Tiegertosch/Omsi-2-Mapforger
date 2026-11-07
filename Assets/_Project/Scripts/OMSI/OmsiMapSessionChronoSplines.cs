using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The spline and row lists of a tile before and after a change that took a spline out, to swap them back and forth.</summary>
    internal sealed class OmsiSplineListSwap
    {
        public OmsiTileSummary Tile;
        public List<OmsiSplineRecord> SplinesBefore;
        public List<OmsiSplineRecord> SplinesAfter;
        public List<OmsiSplineAttachmentRecord> RowsBefore;
        public List<OmsiSplineAttachmentRecord> RowsAfter;
    }

    /// <summary>
    /// A new road type for a spline, or taking a spline away, while a chrono variant is edited (ADR 065). Both are commands in
    /// the variant's change list, as the stock variants write them: <c>[selspline] id</c> with <c>[typ] file</c> or
    /// <c>[delete]</c>. Splines joined to a deleted spline keep their neighbour IDs, as in the stock variants, and the base map stays as it was.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Gives a spline another profile in the edited variant, such as road works instead of the road. The spline keeps its place and
        /// its rules (their path numbers belong to the old profile). One undo step; null when the profile is the same. A copy of the
        /// record takes the shared one's place in the tile.
        /// </summary>
        public OmsiSplineChange SetSplineTypeInVariant(OmsiTileSummary tile, OmsiSplineRecord spline, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("A profile is required.", nameof(assetPath));
            var path = RequireSplineInVariant(tile, spline);
            assetPath = assetPath.Trim();
            if (string.Equals(assetPath, spline.AssetPath, StringComparison.Ordinal))
                return null;

            var workspace = RequireWorkspace();
            var before = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
            var file = before != null ? OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
            OpenSelectionBlock(file, "selspline", spline.Id, out var selectAt, out var blockEnd);
            // A type chosen before is replaced, not piled up.
            for (var index = blockEnd - 1; index > selectAt; index--)
                if (string.Equals(file.Records[index].Name, "typ", StringComparison.OrdinalIgnoreCase))
                    file.RemoveRecord(file.Records[index]);
            file.InsertRecord(selectAt + 1, file.CreateRecord("typ", new[] { assetPath }));

            var change = new OmsiSplineChange(OmsiSplineChangeKind.Retype);
            change.SplineIds.Add(spline.Id);
            change.Tiles.Add(tile);
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = before, After = file.ToBytes() });
            var copy = OmsiChrono.Copy(spline);
            copy.AssetPath = assetPath;
            change.Swaps.Add(new OmsiSplineSwap { Tile = tile, Before = spline, After = copy });
            change.InVariant = true;
            ApplyFiles(change.Files, false);
            SwapSplines(change, true);
            RegisterChronoFile(path);
            if (spline.Rules.Count > 0)
                change.Notes.Add("Its rules name paths by number; check them if the new type has other lanes.");
            return change;
        }

        /// <summary>
        /// Takes a spline away in the edited variant. The rows of objects along it go with it, and rows along later splines of the
        /// tile follow their splines' new places; splines joined to it keep their neighbour IDs. Nothing is blocked: what else
        /// names the spline, such as timetables, is listed on the change. One undo step.
        /// </summary>
        public OmsiSplineChange DeleteSplineInVariant(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineRecord spline, OmsiReferenceIndex references = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var path = RequireSplineInVariant(tile, spline);
            var workspace = RequireWorkspace();
            var before = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
            var file = before != null ? OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
            OpenSelectionBlock(file, "selspline", spline.Id, out _, out var blockEnd);
            file.InsertRecord(blockEnd, file.CreateRecord("delete", new string[0]));

            var change = new OmsiSplineChange(OmsiSplineChangeKind.VariantDeletion);
            change.SplineIds.Add(spline.Id);
            change.Tiles.Add(tile);
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = before, After = file.ToBytes() });

            // What the overlay does with the rows of a tile whose spline goes: rows on it go, the others follow their splines' places.
            var at = tile.Splines.IndexOf(spline);
            var swap = new OmsiSplineListSwap
            {
                Tile = tile,
                SplinesBefore = new List<OmsiSplineRecord>(tile.Splines),
                SplinesAfter = new List<OmsiSplineRecord>(tile.Splines),
                RowsBefore = new List<OmsiSplineAttachmentRecord>(tile.SplineAttachments),
                RowsAfter = new List<OmsiSplineAttachmentRecord>()
            };
            swap.SplinesAfter.RemoveAt(at);
            foreach (var row in tile.SplineAttachments)
            {
                if (row.SplineIndex == at)
                {
                    change.RemovedRowCount++;
                    continue;
                }
                if (row.SplineIndex > at)
                {
                    var moved = OmsiChrono.Copy(row, row.SplineIndex - 1);
                    if (chronoOrigins != null && chronoOrigins.TryGetValue(row, out var origin))
                        chronoOrigins[moved] = origin;
                    swap.RowsAfter.Add(moved);
                }
                else
                    swap.RowsAfter.Add(row);
            }
            change.ListSwaps.Add(swap);
            change.InVariant = true;

            // The splines next to it have a free end there, as the overlay shows them; their copies go into the lists of their tiles.
            foreach (var holder in summary.Tiles)
                for (var index = 0; index < holder.Splines.Count; index++)
                {
                    var other = holder.Splines[index];
                    if (ReferenceEquals(other, spline) || other.PreviousId != spline.Id && other.NextId != spline.Id)
                        continue;
                    var freed = OmsiChrono.Copy(other);
                    if (freed.PreviousId == spline.Id)
                        freed.PreviousId = 0;
                    if (freed.NextId == spline.Id)
                        freed.NextId = 0;
                    if (chronoOrigins != null && chronoOrigins.TryGetValue(other, out var neighbourOrigin))
                        chronoOrigins[freed] = neighbourOrigin;
                    var target = change.ListSwaps.Find(candidate => ReferenceEquals(candidate.Tile, holder));
                    if (target == null)
                    {
                        target = new OmsiSplineListSwap
                        {
                            Tile = holder,
                            SplinesBefore = new List<OmsiSplineRecord>(holder.Splines),
                            SplinesAfter = new List<OmsiSplineRecord>(holder.Splines),
                            RowsBefore = new List<OmsiSplineAttachmentRecord>(holder.SplineAttachments),
                            RowsAfter = new List<OmsiSplineAttachmentRecord>(holder.SplineAttachments)
                        };
                        change.ListSwaps.Add(target);
                    }
                    target.SplinesAfter[target.SplinesAfter.IndexOf(other)] = freed;
                    if (!change.Tiles.Contains(holder))
                        change.Tiles.Add(holder);
                }

            // What else names the spline; the variant's own selections of it do not count.
            references = references ?? BuildReferenceIndex();
            var own = ChronoFileOf(tile);
            foreach (var reference in references.To(spline.Id))
                if (reference.Kind != SplineNeighbourReference && !(reference.Kind == "chrono selection" && SamePath(reference.File, own)))
                    change.References.Add(reference);

            ApplyFiles(change.Files, false);
            ApplySplineLists(summary, change, true);
            RegisterChronoFile(path);
            return change;
        }

        /// <summary>
        /// Builds a planned road as new splines in the edited variant (ADR 066): each piece as a [spline] at the end of the variant's
        /// change list for the tile its start lies in, with new map-wide IDs and the pieces linked to each other, as the stock variants
        /// carry their new roads. The road is standalone: it is not linked to a spline of the base map or of another variant by ID
        /// (their records stay as they are; openOMSI joins lanes that meet), has no junction objects, and the terrain is not fitted to it.
        /// One undo step.
        /// </summary>
        public OmsiSplineChange CreateRoadInVariant(OmsiMapSummary summary, OmsiRoadPlan plan, string assetPath)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("A spline profile (.sli) is required.", nameof(assetPath));
            if (EditedVariant == null)
                throw new InvalidOperationException("Choose a chrono variant to edit first.");
            if (plan.HasErrors || plan.Pieces.Count == 0)
                throw new InvalidOperationException("The road cannot be built: " +
                                                    (plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error)?.Message ?? "it has no pieces."));

            var workspace = RequireWorkspace();
            var files = new Dictionary<string, OmsiRecordFile>(StringComparer.OrdinalIgnoreCase);
            var befores = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            var configFiles = new EditedFiles(workspace);
            var config = configFiles.Config(GlobalConfigFileName);
            ids = ids ?? OmsiIdAllocator.ForMap(workspace, GlobalConfigFileName);

            OmsiRecordFile ChangeListOf(OmsiTileSummary tile, out string path)
            {
                path = ChronoFileOf(tile);
                if (files.TryGetValue(path, out var known))
                    return known;
                var bytes = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
                var created = bytes != null ? OmsiRecordFile.Parse(bytes, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
                files.Add(path, created);
                befores.Add(path, bytes);
                order.Add(path);
                return created;
            }

            var tiles = new List<OmsiTileSummary>();
            var pieceIds = new List<int>();
            foreach (var piece in plan.Pieces)
            {
                var tile = TileAt(summary, piece.StartX, piece.StartY);
                if (tile == null)
                    throw new InvalidOperationException("Part of the road starts outside the map's tiles.");
                if (!tiles.Contains(tile))
                    tiles.Add(tile);
                var tileFile = ChangeListOf(tile, out _);
                if (tileFile.Version >= 0 && tileFile.Version < 11)
                    throw new InvalidOperationException("Splines can only be added to tiles of version 11 or later; " + tile.FileName + " has version " +
                                                        tileFile.Version + ".");
                pieceIds.Add(ids.Allocate());
            }

            // The chain offset of a spline is the length of the chain before it, so the pieces run on in one chain.
            var chainOffset = 0.0;
            for (var index = 0; index < plan.Pieces.Count; index++)
            {
                var piece = plan.Pieces[index];
                var tile = TileAt(summary, piece.StartX, piece.StartY);
                var tileFile = ChangeListOf(tile, out _);
                var fields = new List<string>
                {
                    "0", assetPath.Trim(), OmsiNumberFormat.Format(pieceIds[index]),
                    OmsiNumberFormat.Format(index > 0 ? pieceIds[index - 1] : 0), OmsiNumberFormat.Format(index + 1 < pieceIds.Count ? pieceIds[index + 1] : 0),
                    OmsiNumberFormat.Format(piece.StartX - tile.X * summary.TileSizeMeters), OmsiNumberFormat.Format(piece.StartHeight),
                    OmsiNumberFormat.Format(piece.StartY - tile.Y * summary.TileSizeMeters), OmsiNumberFormat.Format(piece.HeadingDegrees),
                    OmsiNumberFormat.Format(piece.LengthMeters), OmsiNumberFormat.Format(piece.RadiusMeters),
                    OmsiNumberFormat.Format(piece.GradientStartPercent), OmsiNumberFormat.Format(piece.GradientEndPercent), "0", "0"
                };
                if (tileFile.Version < 0 || tileFile.Version >= 14)
                    fields.AddRange(new[] { "0", "0" });
                fields.Add(OmsiNumberFormat.Format(chainOffset));
                chainOffset += piece.LengthMeters;
                tileFile.InsertRecord(tileFile.Records.Count, tileFile.CreateRecord("spline", fields));
            }
            ids.ApplyTo(config);

            var change = new OmsiSplineChange(OmsiSplineChangeKind.Creation) { InVariant = true };
            change.SplineIds.AddRange(pieceIds);
            change.Tiles.AddRange(tiles);
            foreach (var path in order)
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = befores[path], After = files[path].ToBytes() });
            configFiles.Collect(change.Files);
            ApplyFiles(change.Files, false);

            // The new splines as the overlay reads them from the change list, at the end of their tiles' lists.
            foreach (var tile in tiles)
            {
                var path = ChronoFileOf(tile);
                var added = OmsiMapReader.ReadAddedElements(workspace, path, tile);
                var swap = new OmsiSplineListSwap
                {
                    Tile = tile,
                    SplinesBefore = new List<OmsiSplineRecord>(tile.Splines),
                    SplinesAfter = new List<OmsiSplineRecord>(tile.Splines),
                    RowsBefore = new List<OmsiSplineAttachmentRecord>(tile.SplineAttachments),
                    RowsAfter = new List<OmsiSplineAttachmentRecord>(tile.SplineAttachments)
                };
                foreach (var fresh in added.Splines)
                    if (pieceIds.Contains(fresh.Id))
                    {
                        swap.SplinesAfter.Add(fresh);
                        AddToEditedVariant(fresh, path);
                    }
                change.ListSwaps.Add(swap);
            }
            ApplySplineLists(summary, change, true);
            summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, pieceIds[pieceIds.Count - 1]);
            summary.NextIdCode = ids.NextFreeId;
            return change;
        }

        /// <summary>The variant's change list for the tile of a spline that is shown in the edited variant's overlay.</summary>
        private string RequireSplineInVariant(OmsiTileSummary tile, OmsiSplineRecord spline)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            if (EditedVariant == null)
                throw new InvalidOperationException("Choose a chrono variant to edit first.");
            if (!tile.Splines.Contains(spline))
                throw new ArgumentException("Spline " + spline.Id + " is not in the tile; it may have been replaced by a newer record.", nameof(spline));
            return ChronoFileOf(tile);
        }

        /// <summary>Puts the spline and row lists a deletion made into the tiles, or the ones before it when undoing, and the counts with them.</summary>
        private static void ApplySplineLists(OmsiMapSummary summary, OmsiSplineChange change, bool forward)
        {
            foreach (var swap in change.ListSwaps)
            {
                var splines = forward ? swap.SplinesAfter : swap.SplinesBefore;
                var rows = forward ? swap.RowsAfter : swap.RowsBefore;
                var splineDelta = splines.Count - swap.Tile.Splines.Count;
                var rowDelta = rows.Count - swap.Tile.SplineAttachments.Count;
                swap.Tile.Splines.Clear();
                swap.Tile.Splines.AddRange(splines);
                swap.Tile.SplineAttachments.Clear();
                swap.Tile.SplineAttachments.AddRange(rows);
                swap.Tile.SplineCount = swap.Tile.Splines.Count;
                swap.Tile.AttachmentCount += rowDelta;
                if (summary != null)
                {
                    summary.SplineCount += splineDelta;
                    summary.AttachmentCount += rowDelta;
                }
            }
        }
    }
}
