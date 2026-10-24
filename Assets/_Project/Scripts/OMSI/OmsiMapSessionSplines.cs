using System;
using System.Collections.Generic;
using System.IO;

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

        /// <summary>The gradient a road continuing from this end starts with, in percent: onwards at the end, backwards at the start.</summary>
        public double OutwardGradientPercent => AtEnd ? Spline.GradientAtPercent(Spline.LengthMeters) : -Spline.GradientAtPercent(0.0);

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
        Deletion,

        /// <summary>The terrain was fitted to splines, or no longer is.</summary>
        TerrainAlignment,

        /// <summary>Splines got a new position, shape or heights.</summary>
        Geometry,

        /// <summary>A spline was split in two at a point along it.</summary>
        Split
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

        /// <summary>The tiles whose terrain was fitted again by the last apply, undo or redo, to draw again.</summary>
        public List<OmsiTileSummary> TerrainTiles { get; } = new List<OmsiTileSummary>();

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

        // Splines with IDs from here on were built in this editor; only their terrain alignment is computed here.
        private int? firstNewId;
        private OmsiSplineProfileCatalog shapeProfiles;
        private OmsiJunctionCatalog shapeJunctions;
        private OmsiObjectOutlineCatalog shapeOutlines;

        /// <summary>How the terrain is fitted to roads built with terrain alignment.</summary>
        public OmsiTerrainShapeSettings TerrainShape { get; } = new OmsiTerrainShapeSettings();

        /// <summary>
        /// Builds a planned road as new splines of the profile <paramref name="assetPath"/>: each piece at the end of the
        /// tile its start lies in, with new map-wide IDs, the pieces linked to each other and to the joined splines by
        /// their neighbour IDs, and [NextIDCode] raised. Existing indices do not change.
        /// </summary>
        /// <param name="startJoin">A free spline end the road starts from, or null.</param>
        /// <param name="endJoin">A free spline end the road runs into, or null.</param>
        /// <param name="alignTerrain">
        /// Marks the splines with [spline_terrain_align_2] 1, as OMSI's editor marks roads it fitted the terrain to, and fits
        /// the terrain around them; the original terrain stays in the original map.
        /// </param>
        public OmsiSplineChange CreateRoad(OmsiMapSummary summary, OmsiRoadPlan plan, string assetPath, OmsiSplineEnd startJoin, OmsiSplineEnd endJoin,
            bool alignTerrain = false)
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
                if (alignTerrain)
                    tileFile.InsertRecord(tileFile.Records.Count, tileFile.CreateRecord("spline_terrain_align_2", new[] { "1" }));
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
            ReshapeTerrain(summary, change);
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

            // The terrain around the deleted spline returns to what the other roads leave of the original.
            var shaped = ShapesTerrain(spline);
            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            if (shaped)
                ReshapeTerrain(summary, change);
            return new OmsiSplineChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>
        /// Splits a spline in two at <paramref name="distance"/> metres along it. The spline keeps its ID, its start and
        /// every field and ends there; a new spline appended to the tile that holds the point carries on to the old end with
        /// the same profile, curve, gradients, cant and terrain alignment, so the road looks as before. The neighbour at the
        /// old end is joined to the new spline. Nothing changes while objects ride on the spline or timetables or chrono
        /// changes name it, since their distances along it would no longer fit; those references are returned.
        /// </summary>
        public OmsiSplineChangeResult SplitSpline(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineRecord spline, double distance,
            OmsiReferenceIndex references = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            if (!(distance >= OmsiRoadPlanner.MinimumLength && distance <= spline.LengthMeters - OmsiRoadPlanner.MinimumLength))
                throw new InvalidOperationException("A spline can only be split at least " + OmsiNumberFormat.Format(OmsiRoadPlanner.MinimumLength) +
                                                    " m from its ends.");
            if (spline.AlignsTerrain && !ShapesTerrain(spline))
                throw new InvalidOperationException("Splines of the original map with terrain alignment cannot be split: the new part would fit the terrain " +
                                                    "as a road built here, and the original map's roads keep their terrain.");

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

            var size = summary.TileSizeMeters;
            var files = new EditedFiles(workspace);
            var config = files.Config(GlobalConfigFileName);
            var tileFile = files.Tile(tile.FileName);
            OmsiSplineView first = null;
            foreach (var candidate in OmsiTileRecords.Of<OmsiSplineView>(tileFile))
                if (candidate.Id == spline.Id)
                    first = candidate;
            if (first == null)
                throw new InvalidOperationException("Spline " + spline.Id + " was not found in " + tile.FileName + ".");

            // The point, heading, height, gradient and cant where the spline is split.
            var length = spline.LengthMeters;
            OmsiMapGeometry.GetLocalFrameAt(spline, distance, out var local, out var headingRadians);
            var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, local.X, local.Y, size);
            var target = TileAt(summary, world.X, world.Y) ?? tile;
            var targetFile = files.Tile(target.FileName);
            if (targetFile.Version >= 0 && targetFile.Version < 11)
                throw new InvalidOperationException("Splines can only be added to tiles of version 11 or later; " + target.FileName + " has version " +
                                                    targetFile.Version + ".");
            var cantStart = first.CantStart;
            var cantEnd = first.CantEnd;
            var cant = cantStart + (cantEnd - cantStart) * distance / length;
            var skewEnd = first.SkewEnd;
            var oldEnd = new OmsiSplineEnd(tile, spline, true).Position(size);
            var oldNext = spline.NextId;

            ids = ids ?? OmsiIdAllocator.ForMap(workspace, GlobalConfigFileName);
            var newId = ids.Allocate();

            // The second part: a copy of the record, so profile, mirror and every unknown field stay, then its own values.
            var copy = targetFile.CreateRecord(first.Record.Name, FieldsOf(first.Record));
            targetFile.InsertRecord(targetFile.Records.Count, copy);
            var second = (OmsiSplineView)OmsiTileRecords.Create(targetFile, copy);
            var alignment = FollowingAlignment(tileFile, first.Record);
            if (alignment != null)
                targetFile.InsertRecord(targetFile.Records.Count, targetFile.CreateRecord(alignment.Name, FieldsOf(alignment)));
            second.Id = newId;
            second.PreviousId = spline.Id;
            second.NextId = oldNext;
            second.X = world.X - target.X * size;
            second.Y = world.Y - target.Y * size;
            second.Height = spline.HeightAt(distance);
            second.HeadingDegrees = Normalize360(headingRadians * 180.0 / Math.PI);
            second.LengthMeters = length - distance;
            second.GradientStartPercent = spline.GradientAtPercent(distance);
            second.GradientEndPercent = spline.GradientAtPercent(length);
            second.CantStart = cant;
            second.CantEnd = cantEnd;
            if (skewEnd.HasValue)
                second.SkewStart = 0.0;
            if (second.IsSplineH)
                second.HeightDifference = spline.HeightAt(length) - spline.HeightAt(distance);

            // The first part ends at the point. Its gradients take the evenly spread correction of a [spline_h] in.
            var startGradient = spline.GradientAtPercent(0.0);
            var splitGradient = spline.GradientAtPercent(distance);
            var splitHeight = spline.HeightAt(distance);
            first.LengthMeters = distance;
            first.GradientStartPercent = startGradient;
            first.GradientEndPercent = splitGradient;
            first.NextId = newId;
            first.CantEnd = cant;
            if (skewEnd.HasValue)
                first.SkewEnd = 0.0;
            if (first.IsSplineH)
                first.HeightDifference = splitHeight - spline.Height;

            var change = new OmsiSplineChange(OmsiSplineChangeKind.Split);
            change.SplineIds.Add(spline.Id);
            change.SplineIds.Add(newId);
            change.Tiles.Add(tile);
            if (!change.Tiles.Contains(target))
                change.Tiles.Add(target);

            // The neighbour at the old end now meets the new spline, at whichever of its ends lies there.
            if (oldNext != 0)
                foreach (var file in neighbourFiles)
                {
                    var neighbourTile = summary.Tiles.Find(candidate => SamePath(candidate.FileName, file));
                    foreach (var neighbour in OmsiTileRecords.Of<OmsiSplineView>(files.Tile(file)))
                    {
                        if (neighbour.Id != oldNext || ReferenceEquals(neighbour.Record, first.Record))
                            continue;
                        var record = neighbourTile?.Splines.Find(candidate => candidate.Id == oldNext);
                        var startNear = record == null || Distance(new OmsiSplineEnd(neighbourTile, record, false).Position(size), oldEnd) <=
                                        Distance(new OmsiSplineEnd(neighbourTile, record, true).Position(size), oldEnd);
                        if (neighbour.PreviousId == spline.Id && (startNear || neighbour.NextId != spline.Id))
                            neighbour.PreviousId = newId;
                        else if (neighbour.NextId == spline.Id)
                            neighbour.NextId = newId;
                        if (neighbourTile != null && !change.Tiles.Contains(neighbourTile))
                            change.Tiles.Add(neighbourTile);
                    }
                }
            ids.ApplyTo(config);

            var shaped = ShapesTerrain(spline);
            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            if (shaped)
                ReshapeTerrain(summary, change);
            summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, newId);
            summary.NextIdCode = ids.NextFreeId;
            return new OmsiSplineChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>The [spline_terrain_align] record right after a spline, past its rules, or null.</summary>
        private static OmsiRecord FollowingAlignment(OmsiRecordFile file, OmsiRecord spline)
        {
            for (var index = file.Records.IndexOf(spline) + 1; index < file.Records.Count; index++)
            {
                var name = file.Records[index].Name;
                if (IsAlignment(name))
                    return file.Records[index];
                if (!IsModifier(name))
                    return null;
            }
            return null;
        }

        private static List<string> FieldsOf(OmsiRecord record)
        {
            var fields = new List<string>(record.Fields.Count);
            for (var index = 0; index < record.Fields.Count; index++)
                fields.Add(record.GetField(index));
            return fields;
        }

        private static double Normalize360(double degrees)
        {
            return (degrees % 360.0 + 360.0) % 360.0;
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        /// <summary>
        /// Writes new geometry into placed splines: position, heading, length, radius, start height and gradients, keeping
        /// every other field, their IDs and the rows along them. The splines stay in their tiles. The terrain fitted to
        /// roads built here follows.
        /// </summary>
        public OmsiSplineChange UpdateSplines(OmsiMapSummary summary, IEnumerable<OmsiSplineGeometry> geometries)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (geometries == null)
                throw new ArgumentNullException(nameof(geometries));
            var files = new EditedFiles(RequireWorkspace());
            var change = new OmsiSplineChange(OmsiSplineChangeKind.Geometry);
            foreach (var geometry in geometries)
            {
                if (!(geometry.LengthMeters >= OmsiRoadPlanner.MinimumLength) || double.IsNaN(geometry.LocalX) || double.IsNaN(geometry.LocalY) ||
                    double.IsNaN(geometry.Height) || double.IsNaN(geometry.HeadingDegrees) || double.IsNaN(geometry.RadiusMeters) ||
                    double.IsInfinity(geometry.LengthMeters))
                    throw new ArgumentException("Spline " + geometry.Spline.Id + " would get invalid geometry.", nameof(geometries));
                OmsiSplineView view = null;
                foreach (var candidate in OmsiTileRecords.Of<OmsiSplineView>(files.Tile(geometry.Tile.FileName)))
                    if (candidate.Id == geometry.Spline.Id)
                        view = candidate;
                if (view == null)
                    throw new InvalidOperationException("Spline " + geometry.Spline.Id + " was not found in " + geometry.Tile.FileName + ".");
                view.X = geometry.LocalX;
                view.Y = geometry.LocalY;
                view.Height = geometry.Height;
                view.HeadingDegrees = geometry.HeadingDegrees;
                view.LengthMeters = geometry.LengthMeters;
                view.RadiusMeters = geometry.RadiusMeters;
                view.GradientStartPercent = geometry.GradientStartPercent;
                view.GradientEndPercent = geometry.GradientEndPercent;
                if (view.IsSplineH)
                    view.HeightDifference = geometry.LengthMeters * (geometry.GradientStartPercent + geometry.GradientEndPercent) / 200.0;
                change.SplineIds.Add(geometry.Spline.Id);
                if (!change.Tiles.Contains(geometry.Tile))
                    change.Tiles.Add(geometry.Tile);
            }
            if (change.SplineIds.Count == 0)
                return null;
            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            ReshapeTerrain(summary, change);
            return change;
        }

        /// <summary>
        /// Fits the terrain to splines built in this editor, or stops fitting it: adds or removes [spline_terrain_align_2]
        /// after each of them and computes the terrain around them anew. Splines of the original map are left as they are.
        /// </summary>
        /// <returns>The change, or null when no spline needed a change.</returns>
        public OmsiSplineChange SetTerrainAlignment(OmsiMapSummary summary, IEnumerable<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> splines, bool align)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (splines == null)
                throw new ArgumentNullException(nameof(splines));
            var workspace = RequireWorkspace();
            var files = new EditedFiles(workspace);
            var change = new OmsiSplineChange(OmsiSplineChangeKind.TerrainAlignment);
            foreach (var pair in splines)
            {
                if (pair.Value.Id < FirstNewId() || pair.Value.AlignsTerrain == align)
                    continue;
                var tileFile = files.Tile(pair.Key.FileName);
                var position = tileFile.Records.FindIndex(record =>
                    (record.Name.Equals("spline", StringComparison.OrdinalIgnoreCase) || record.Name.Equals("spline_h", StringComparison.OrdinalIgnoreCase)) &&
                    record.TryGetInt(2, out var id) && id == pair.Value.Id);
                if (position < 0)
                    throw new InvalidOperationException("Spline " + pair.Value.Id + " was not found in " + pair.Key.FileName + ".");
                if (align)
                    tileFile.InsertRecord(position + 1, tileFile.CreateRecord("spline_terrain_align_2", new[] { "1" }));
                else
                {
                    // The alignment records among the records that belong to the spline, such as its rules.
                    var index = position + 1;
                    while (index < tileFile.Records.Count && (IsAlignment(tileFile.Records[index].Name) || IsModifier(tileFile.Records[index].Name)))
                    {
                        if (IsAlignment(tileFile.Records[index].Name))
                            tileFile.RemoveRecord(tileFile.Records[index]);
                        else
                            index++;
                    }
                }
                change.SplineIds.Add(pair.Value.Id);
                if (!change.Tiles.Contains(pair.Key))
                    change.Tiles.Add(pair.Key);
            }
            if (change.SplineIds.Count == 0)
                return null;
            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            ReshapeTerrain(summary, change);
            return change;
        }

        private static bool IsAlignment(string name)
        {
            return name.Equals("spline_terrain_align", StringComparison.OrdinalIgnoreCase) || name.Equals("spline_terrain_align_2", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Records that belong to the element before them, such as rules.</summary>
        private static bool IsModifier(string name)
        {
            return name.Equals("rule", StringComparison.OrdinalIgnoreCase) || name.Equals("kill_rule", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("varparent", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The road a spline belongs to: it and the splines joined to it by neighbour IDs, in both directions, as far as
        /// they were built in this editor.
        /// </summary>
        public List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> BuiltRoadOf(OmsiMapSummary summary, OmsiSplineRecord spline)
        {
            var byId = new Dictionary<int, KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
            foreach (var tile in summary.Tiles)
                foreach (var candidate in tile.Splines)
                    byId[candidate.Id] = new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(tile, candidate);
            var result = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
            var seen = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(spline.Id);
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (!seen.Add(id) || id < FirstNewId() || !byId.TryGetValue(id, out var pair))
                    continue;
                result.Add(pair);
                if (pair.Value.PreviousId != 0)
                    pending.Push(pair.Value.PreviousId);
                if (pair.Value.NextId != 0)
                    pending.Push(pair.Value.NextId);
            }
            return result;
        }

        /// <summary>
        /// Fits the terrain anew around every road built here with terrain alignment, such as after the way of fitting
        /// changed. The original terrain stays in the original map.
        /// </summary>
        /// <returns>The tiles whose terrain changed.</returns>
        public List<OmsiTileSummary> RefitAllTerrain(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var change = new OmsiSplineChange(OmsiSplineChangeKind.TerrainAlignment);
            foreach (var tile in summary.Tiles)
                if (tile.Splines.Exists(ShapesTerrain))
                    change.Tiles.Add(tile);
            ReshapeTerrain(summary, change);
            return new List<OmsiTileSummary>(change.TerrainTiles);
        }

        /// <summary>Computes the terrain of a tile and its neighbours anew from the roads the map has now.</summary>
        private void RefitTerrainAround(OmsiMapSummary summary, OmsiTileSummary tile)
        {
            var change = new OmsiSplineChange(OmsiSplineChangeKind.TerrainAlignment);
            change.Tiles.Add(tile);
            ReshapeTerrain(summary, change);
        }

        /// <summary>True for splines built in this editor, whose terrain alignment can be switched.</summary>
        public bool IsBuiltHere(OmsiSplineRecord spline)
        {
            return spline != null && Workspace != null && spline.Id >= FirstNewId();
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
            ReshapeTerrain(summary, change);
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
            ReshapeTerrain(summary, change);
        }

        /// <summary>True for splines built in this editor with terrain alignment; their terrain is computed here.</summary>
        public bool ShapesTerrain(OmsiSplineRecord spline)
        {
            return spline != null && spline.AlignsTerrain && spline.Id >= FirstNewId();
        }

        /// <summary>The first ID the original map does not use: splines from here on were built in this editor.</summary>
        private int FirstNewId()
        {
            if (!firstNewId.HasValue)
            {
                var original = new OmsiFolderSource(RequireWorkspace().RootPath);
                firstNewId = OmsiIdAllocator.ForMap(original, GlobalConfigFileName).NextFreeId;
            }
            return firstNewId.Value;
        }

        /// <summary>
        /// Fits the terrain of the changed tiles and their neighbours anew: the original terrain of each tile, fitted to
        /// every road built here with terrain alignment that reaches it. The result replaces the workspace copy, which
        /// disappears again when it equals the original. Records the tiles whose terrain changed in the change.
        /// </summary>
        private void ReshapeTerrain(OmsiMapSummary summary, OmsiSplineChange change)
        {
            change.TerrainTiles.Clear();
            if (summary == null)
                return;
            var workspace = RequireWorkspace();
            var size = summary.TileSizeMeters;
            var reach = TerrainShape.MaximumReachMeters + TerrainShape.ShoulderMeters + 30.0;
            var roads = new List<OmsiShapedRoad>();
            var junctions = new List<OmsiShapedJunction>();
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    if (!ShapesTerrain(spline))
                        continue;
                    var profile = ShapeCatalogs().Get(spline.AssetPath);
                    if (profile == null || profile.RightEdge - profile.LeftEdge <= 0.0)
                        continue;
                    var startPort = PortAt(summary, new OmsiSplineEnd(tile, spline, false));
                    var endPort = PortAt(summary, new OmsiSplineEnd(tile, spline, true));
                    roads.Add(new OmsiShapedRoad(tile, spline, profile, size) { StartsAtObject = startPort != null, EndsAtObject = endPort != null });
                    foreach (var port in new[] { startPort, endPort })
                        if (port != null && port.Object.Id >= FirstNewId() && !junctions.Exists(junction => ReferenceEquals(junction.Object, port.Object)))
                        {
                            var junction = ShapedJunction(port.Tile, port.Object, size);
                            if (junction != null)
                                junctions.Add(junction);
                        }
                }
            roads.Sort((left, right) => left.Spline.Id.CompareTo(right.Spline.Id));
            junctions.Sort((left, right) => left.Object.Id.CompareTo(right.Object.Id));

            var targets = new List<OmsiTileSummary>();
            foreach (var changed in change.Tiles)
                foreach (var tile in summary.Tiles)
                    if (Math.Abs(tile.X - changed.X) <= 1 && Math.Abs(tile.Y - changed.Y) <= 1 && tile.IsAvailable && tile.HasTerrain && !targets.Contains(tile))
                        targets.Add(tile);

            foreach (var tile in targets)
            {
                var path = OmsiRelativePath.Normalize(tile.FileName) + ".terrain";
                var original = workspace.ReadOriginalBytes(path);
                if (original == null)
                    continue;
                var heights = OmsiTerrain.Parse(original).CopyHeights();
                var west = tile.X * size;
                var south = tile.Y * size;
                var nearby = roads.FindAll(road => road.MaxX + road.HalfWidth + reach >= west && road.MinX - road.HalfWidth - reach <= west + size &&
                                                   road.MaxY + road.HalfWidth + reach >= south && road.MinY - road.HalfWidth - reach <= south + size);
                var nearbyJunctions = junctions.FindAll(junction => junction.OriginX + junction.Reach + reach >= west && junction.OriginX - junction.Reach - reach <= west + size &&
                                                                    junction.OriginY + junction.Reach + reach >= south && junction.OriginY - junction.Reach - reach <= south + size);
                OmsiTerrainShaping.Shape(heights, tile.X, tile.Y, size, nearby, TerrainShape, nearbyJunctions);
                var bytes = OmsiTerrain.Encode(heights);
                if (SameBytes(bytes, workspace.ReadAllBytes(path)))
                    continue;
                workspace.Save(path, bytes);
                change.TerrainTiles.Add(tile);
            }
        }

        /// <summary>The spline profiles of the installation, with the connection points and outlines of its objects alongside.</summary>
        private OmsiSplineProfileCatalog ShapeCatalogs()
        {
            if (shapeProfiles == null)
            {
                var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(Path.GetDirectoryName(Path.GetFullPath(GlobalConfigPath)));
                shapeProfiles = new OmsiSplineProfileCatalog(installation);
                shapeJunctions = new OmsiJunctionCatalog(installation);
                shapeOutlines = new OmsiObjectOutlineCatalog(installation);
            }
            return shapeProfiles;
        }

        /// <summary>
        /// A junction object to fit the terrain to: its footprint and the highest kerb among the profiles it suggests at its
        /// connection points; null when its model gives no footprint.
        /// </summary>
        private OmsiShapedJunction ShapedJunction(OmsiTileSummary tile, OmsiObjectRecord item, double tileSize)
        {
            var outline = shapeOutlines.Get(item.AssetPath);
            if (outline.Contours.Count == 0)
                return null;
            var edge = 0.0;
            foreach (var helper in shapeJunctions.Helpers(item.AssetPath))
            {
                var profile = shapeProfiles.Get(helper.SplinePath);
                if (profile != null)
                    edge = Math.Max(edge, helper.Height + profile.EdgeHeight);
            }
            return new OmsiShapedJunction(tile, item, outline.Contours, edge, tileSize);
        }

        /// <summary>
        /// Where the editor-placed junction objects lie that roads built here with terrain alignment join, as text: when it
        /// changes, such as after a junction was moved, placed or deleted, the terrain has to be fitted again.
        /// </summary>
        public string JunctionTerrainState(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (Workspace == null)
                return string.Empty;
            var text = new System.Text.StringBuilder();
            foreach (var tile in summary.Tiles)
                foreach (var item in tile.Objects)
                {
                    if (item.Id < FirstNewId())
                        continue;
                    ShapeCatalogs();
                    if (shapeJunctions.Helpers(item.AssetPath).Count == 0)
                        continue;
                    text.Append(item.Id).Append(' ').Append(tile.X).Append(' ').Append(tile.Y).Append(' ')
                        .Append(OmsiNumberFormat.Format(item.LocalX)).Append(' ').Append(OmsiNumberFormat.Format(item.LocalY)).Append(' ')
                        .Append(OmsiNumberFormat.Format(item.Height)).Append(' ').Append(OmsiNumberFormat.Format(item.HeadingDegrees)).Append(' ')
                        .Append(OmsiNumberFormat.Format(item.PitchDegrees)).Append(' ').Append(OmsiNumberFormat.Format(item.BankDegrees)).Append(';');
                }
            return text.ToString();
        }

        /// <summary>The connection point of a placed object, such as a junction, that a spline end lies on (ADR 024); or null.</summary>
        private OmsiJunctionPort PortAt(OmsiMapSummary summary, OmsiSplineEnd end)
        {
            ShapeCatalogs();
            var size = summary.TileSizeMeters;
            var position = end.Position(size);
            var tileX = (int)Math.Floor(position.X / size);
            var tileY = (int)Math.Floor(position.Y / size);
            var tolerance = new OmsiJunctionTolerances().PositionMeters;
            foreach (var tile in summary.Tiles)
            {
                if (Math.Abs(tile.X - tileX) > 1 || Math.Abs(tile.Y - tileY) > 1)
                    continue;
                foreach (var item in tile.Objects)
                    foreach (var port in shapeJunctions.Ports(tile, item, size))
                    {
                        var dx = port.Position.X - position.X;
                        var dy = port.Position.Y - position.Y;
                        if (dx * dx + dy * dy <= tolerance * tolerance)
                            return port;
                    }
            }
            return null;
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
