using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What a paste did, in the order it was done: undo it in reverse order, redo it forwards.</summary>
    public sealed class OmsiPasteResult
    {
        /// <summary>Every <see cref="OmsiObjectChange"/> (an object put down) and <see cref="OmsiAttachedObjectChange"/> (one hung on it), in order.</summary>
        public List<object> Changes { get; } = new List<object>();

        /// <summary>The objects put down, each with the tile it stands in.</summary>
        public List<OmsiObjectChange> Placed { get; } = new List<OmsiObjectChange>();

        /// <summary>Objects hung on others in the copies; in a chrono variant none are, as ADR 042 keeps them in the base map.</summary>
        public int AttachedPasted { get; internal set; }

        public int AttachedLeftOut { get; internal set; }

        /// <summary>Rules of copies that were left out for the same reason.</summary>
        public int RulesLeftOut { get; internal set; }
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Puts copied objects down at their targets, each with its labels, pitch and bank, its rules and the objects hung on it,
        /// all with new IDs. It happens all or not at all: every target is checked before anything is written, and when a write still
        /// fails, what was done is taken back in reverse order before the exception goes on, so no file is left half pasted; when taking
        /// it back fails too, <see cref="OmsiRollBackFailedException"/> reports both and the map must be read again. In a chrono
        /// variant only the objects themselves are put down (with labels, pitch and bank), as attached objects and rules belong to the
        /// base map there.
        /// </summary>
        /// <param name="hasAbsoluteHeight">Whether an object file takes its height as written (junctions, [absheight]) instead of above the ground; may be null.</param>
        /// <param name="groundAt">The terrain height at a world position, or null where there is none; may be null.</param>
        public OmsiPasteResult PasteObjects(OmsiMapSummary summary, IReadOnlyList<OmsiPasteTarget> targets, Func<string, bool> hasAbsoluteHeight = null,
            Func<double, double, double?> groundAt = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));
            var workspace = RequireWorkspace();
            var size = summary.TileSizeMeters;
            var inVariant = EditedVariant != null;

            // Check everything first.
            var tiles = new OmsiTileSummary[targets.Count];
            var versions = new Dictionary<OmsiTileSummary, int>();
            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                var tileX = (int)Math.Floor(target.WorldX / size);
                var tileY = (int)Math.Floor(target.WorldY / size);
                var tile = summary.Tiles.Find(candidate => candidate.X == tileX && candidate.Y == tileY);
                if (tile == null)
                    throw new InvalidOperationException("There is no tile at " + tileX + ", " + tileY + " for " + target.Item.AssetPath + ".");
                if (!tile.IsAvailable)
                    throw new InvalidOperationException("The tile file " + tile.FileName + " is missing.");
                tiles[index] = tile;
                if (!versions.TryGetValue(tile, out var version))
                {
                    version = OmsiRecordFile.Parse(workspace.ReadAllBytes(OmsiRelativePath.Normalize(tile.FileName)), OmsiTileRecordLayouts.Instance).Version;
                    versions[tile] = version;
                }
                if (version < 9)
                    throw new InvalidOperationException("Objects can only be added to tiles of version 9 or later (" + tile.FileName + ").");
                if (!inVariant && target.Item.Attached.Count > 0 && version < 12)
                    throw new InvalidOperationException("Objects can only be attached in tiles of version 12 or later (" + tile.FileName + ").");
            }

            var result = new OmsiPasteResult();
            var undo = new List<Action>();
            try
            {
                for (var index = 0; index < targets.Count; index++)
                {
                    var target = targets[index];
                    var item = target.Item;
                    var tile = tiles[index];
                    var height = item.Height;
                    if (hasAbsoluteHeight != null && hasAbsoluteHeight(item.AssetPath) && groundAt != null && item.SourceGround.HasValue)
                    {
                        var ground = groundAt(target.WorldX, target.WorldY);
                        if (ground.HasValue)
                            height = item.Height + (ground.Value - item.SourceGround.Value);
                    }
                    var change = PlaceObject(summary, tile, item.AssetPath, target.WorldX - tile.X * size, target.WorldY - tile.Y * size, height,
                        target.HeadingDegrees, item.PitchDegrees, item.BankDegrees, new List<string>(item.Labels), inVariant ? null : item.Rules);
                    undo.Add(() => Undo(summary, change));
                    result.Changes.Add(change);
                    result.Placed.Add(change);
                    if (inVariant)
                    {
                        result.RulesLeftOut += item.Rules.Count;
                        result.AttachedLeftOut += CountAttached(item.Attached);
                        continue;
                    }
                    AttachAll(summary, tile, change.Record.Id, item.Attached, result, undo);
                }
            }
            catch (Exception exception)
            {
                // The failure is thrown as it was; when taking back what was pasted fails too, both are reported together.
                RollBackAfter(exception, () =>
                {
                    for (var index = undo.Count - 1; index >= 0; index--)
                    {
                        undo[index]();
                        undo.RemoveAt(index);
                    }
                });
                throw;
            }
            return result;
        }

        private void AttachAll(OmsiMapSummary summary, OmsiTileSummary tile, int parentId, List<OmsiPasteAttached> list, OmsiPasteResult result, List<Action> undo)
        {
            foreach (var attached in list)
            {
                var change = AttachObject(summary, tile, parentId, attached.PointIndex, attached.AssetPath, attached.HeadingDegrees, attached.PitchDegrees,
                    attached.BankDegrees, new List<string>(attached.Labels));
                undo.Add(() => Undo(summary, change));
                result.Changes.Add(change);
                result.AttachedPasted++;
                var id = change.Added[0].Value.Id;
                AttachAll(summary, tile, id, attached.Children, result, undo);
            }
        }

        private static int CountAttached(List<OmsiPasteAttached> list)
        {
            var count = 0;
            foreach (var item in list)
                count += 1 + CountAttached(item.Children);
            return count;
        }
    }
}
