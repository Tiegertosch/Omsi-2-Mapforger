using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A paint stroke in progress (ADR 091): the masks it touched, with their bytes from before. Each dab writes the touched masks into the
    /// workspace at once, so the map can show them; <see cref="OmsiMapSession.EndPaintStroke"/> keeps the stroke as one step.
    /// </summary>
    public sealed class OmsiPaintStroke
    {
        internal OmsiPaintStroke(List<OmsiGroundTexture> textures)
        {
            Textures = textures;
        }

        /// <summary>The ground textures of the map as they were when the stroke began.</summary>
        public List<OmsiGroundTexture> Textures { get; }

        internal sealed class MaskState
        {
            public OmsiTileSummary Tile;
            public string Path;
            public OmsiGroundMask Mask;
            public byte[] Before;
        }

        internal Dictionary<string, MaskState> Masks { get; } = new Dictionary<string, MaskState>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>The map's ground textures as the workspace holds them.</summary>
        public List<OmsiGroundTexture> ReadGroundTextures()
        {
            return OmsiGroundTextures.Read(RequireWorkspace(), GlobalConfigFileName);
        }

        /// <summary>Starts a paint stroke; nothing is kept until <see cref="EndPaintStroke"/>.</summary>
        public OmsiPaintStroke BeginPaintStroke()
        {
            return new OmsiPaintStroke(ReadGroundTextures());
        }

        /// <summary>
        /// The brush held at a position: painting a texture lays its coverage on and takes the textures drawn over it away (a higher number is
        /// drawn later), painting the first texture takes all others away, erasing takes the texture itself away, softening blurs its edge.
        /// Masks that do not exist yet are made at the size [groundtex] names. Returns the tiles touched.
        /// </summary>
        public List<OmsiTileSummary> ApplyGroundPaint(OmsiMapSummary summary, OmsiPaintStroke stroke, int layerIndex, OmsiPaintMode mode, OmsiPaintDab dab)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (stroke == null)
                throw new ArgumentNullException(nameof(stroke));
            var texture = stroke.Textures.Find(candidate => candidate.Index == layerIndex);
            if (texture == null)
                throw new InvalidOperationException("The map has no ground texture number " + layerIndex + ".");
            var workspace = RequireWorkspace();
            var size = summary.TileSizeMeters;
            var touched = new List<OmsiTileSummary>();
            foreach (var tile in summary.Tiles)
            {
                var originX = tile.X * size;
                var originY = tile.Y * size;
                if (!tile.IsAvailable || !OmsiGroundPaint.Reaches(originX, originY, size, dab))
                    continue;
                var changed = new List<OmsiPaintStroke.MaskState>();
                if (mode == OmsiPaintMode.Paint)
                {
                    if (texture.HasMask)
                    {
                        var state = MaskOf(workspace, stroke, tile, texture, true);
                        if (state != null && OmsiGroundPaint.Raise(state.Mask, originX, originY, size, dab))
                            changed.Add(state);
                    }
                    // What is painted over the texture would hide it: it gives way under the brush.
                    foreach (var above in stroke.Textures)
                    {
                        if (!above.HasMask || above.Index <= layerIndex)
                            continue;
                        var state = MaskOf(workspace, stroke, tile, above, false);
                        if (state != null && OmsiGroundPaint.Lower(state.Mask, originX, originY, size, dab))
                            changed.Add(state);
                    }
                }
                else if (texture.HasMask)
                {
                    // Erasing takes the texture away completely: the strength would only stop it half way, and holding the brush could not go on.
                    var erase = dab;
                    if (mode == OmsiPaintMode.Erase)
                        erase.Strength = 1.0;
                    var state = MaskOf(workspace, stroke, tile, texture, false);
                    if (state != null && (mode == OmsiPaintMode.Erase
                            ? OmsiGroundPaint.Lower(state.Mask, originX, originY, size, erase)
                            : OmsiGroundPaint.Soften(state.Mask, originX, originY, size, dab)))
                        changed.Add(state);
                }
                if (changed.Count == 0)
                    continue;
                foreach (var state in changed)
                {
                    stroke.Masks[state.Path] = state;
                    workspace.Save(state.Path, state.Mask.ToBytes());
                }
                touched.Add(tile);
            }
            return touched;
        }

        /// <summary>
        /// The masks of the stroke kept as one step, or null when nothing changed. The files were written while the stroke went on, so
        /// undoing writes the bytes from before (and removes a mask the stroke made).
        /// </summary>
        public OmsiTerrainChange EndPaintStroke(OmsiPaintStroke stroke)
        {
            if (stroke == null)
                throw new ArgumentNullException(nameof(stroke));
            var change = new OmsiTerrainChange { Painted = true };
            foreach (var state in stroke.Masks.Values)
            {
                var after = state.Mask.ToBytes();
                if (state.Before != null && SameBytes(state.Before, after))
                    continue;
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = state.Path, Before = state.Before, After = after });
                if (!change.Tiles.Contains(state.Tile))
                    change.Tiles.Add(state.Tile);
            }
            return change.Files.Count == 0 ? null : change;
        }

        /// <summary>
        /// Covers a whole tile with a texture (taking those drawn over it away), or takes the texture off the tile. Covering with the first
        /// texture clears every other. One step, or null when nothing changes.
        /// </summary>
        public OmsiTerrainChange CoverGroundTile(OmsiMapSummary summary, OmsiTileSummary tile, int layerIndex, bool covered)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            var stroke = BeginPaintStroke();
            var texture = stroke.Textures.Find(candidate => candidate.Index == layerIndex);
            if (texture == null)
                throw new InvalidOperationException("The map has no ground texture number " + layerIndex + ".");
            var workspace = RequireWorkspace();
            foreach (var other in stroke.Textures)
            {
                if (!other.HasMask)
                    continue;
                var lay = covered && other.Index == layerIndex;
                var clear = other.Index == layerIndex ? !covered : covered && (other.Index > layerIndex || layerIndex == 0);
                if (!lay && !clear)
                    continue;
                var state = MaskOf(workspace, stroke, tile, other, lay);
                if (state == null || !OmsiGroundPaint.Fill(state.Mask, (byte)(lay ? 255 : 0)))
                    continue;
                stroke.Masks[state.Path] = state;
                workspace.Save(state.Path, state.Mask.ToBytes());
            }
            return EndPaintStroke(stroke);
        }

        /// <summary>
        /// Adds a ground texture to the map: a [groundtex] after the last one, so the numbers of the masks painted so far stay as they are
        /// (ADR 091). The paths are relative to the OMSI installation. The map's ground is built anew for it.
        /// </summary>
        public OmsiTerrainChange AddGroundTexture(string texturePath, string detailPath, int maskSizeExponent, double repeats, double detailRepeats)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
                throw new ArgumentException("A ground texture needs a texture.", nameof(texturePath));
            var workspace = RequireWorkspace();
            var before = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(before, OmsiGlobalConfigLayouts.Instance);
            var last = config.Records.FindLastIndex(record => string.Equals(record.Name, "groundtex", StringComparison.OrdinalIgnoreCase));
            var insertAt = last >= 0 ? last + 1 : config.Records.Count;
            config.InsertRecord(insertAt, config.CreateRecord("groundtex", new[]
            {
                texturePath.Trim(), string.IsNullOrWhiteSpace(detailPath) ? string.Empty : detailPath.Trim(),
                Math.Max(1, Math.Min(12, maskSizeExponent)).ToString(CultureInfo.InvariantCulture),
                repeats.ToString("0.###", CultureInfo.InvariantCulture), detailRepeats.ToString("0.###", CultureInfo.InvariantCulture)
            }));
            var change = new OmsiTerrainChange { Painted = true, TexturesChanged = true };
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = GlobalConfigFileName, Before = before, After = config.ToBytes() });
            ApplyFiles(change.Files, false);
            return change;
        }

        /// <summary>The mask of a texture on a tile as the stroke holds it, read from the workspace; a new empty one when asked and there is none.</summary>
        private static OmsiPaintStroke.MaskState MaskOf(OmsiMapWorkspace workspace, OmsiPaintStroke stroke, OmsiTileSummary tile, OmsiGroundTexture texture,
            bool create)
        {
            var path = texture.MaskPath(tile.FileName);
            if (stroke.Masks.TryGetValue(path, out var known))
                return known;
            if (workspace.FileExists(path))
            {
                var bytes = workspace.ReadAllBytes(path);
                return new OmsiPaintStroke.MaskState { Tile = tile, Path = path, Mask = OmsiGroundMask.Parse(bytes, path), Before = bytes };
            }
            if (!create)
                return null;
            var size = 1 << Math.Max(1, Math.Min(12, texture.MaskSizeExponent));
            return new OmsiPaintStroke.MaskState { Tile = tile, Path = path, Mask = new OmsiGroundMask(size), Before = null };
        }
    }
}
