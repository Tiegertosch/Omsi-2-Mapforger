using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A new texture offset and mirroring for a placed spline, in the tile that holds it.</summary>
    public sealed class OmsiSplineLook
    {
        public OmsiTileSummary Tile { get; set; }
        public OmsiSplineRecord Spline { get; set; }

        /// <summary>Whether the cross-section is turned over (the record's trailing <c>mirror</c> line).</summary>
        public bool Mirrored { get; set; }

        /// <summary>
        /// How far along its chain the spline begins, which is also where its textures start and where rows along it start counting;
        /// null leaves it as it is.
        /// </summary>
        public double? TextureOffset { get; set; }
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Writes mirroring and the texture offset into placed splines, keeping every other field and the splines in their tiles. One change
        /// for undo; null when nothing differs. A texture offset is only written where the record has the field (tile version 11 on).
        /// </summary>
        public OmsiSplineChange UpdateSplineLook(OmsiMapSummary summary, IEnumerable<OmsiSplineLook> looks)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (looks == null)
                throw new ArgumentNullException(nameof(looks));
            var files = new EditedFiles(RequireWorkspace());
            var change = new OmsiSplineChange(OmsiSplineChangeKind.Geometry);
            foreach (var look in looks)
            {
                if (look.TextureOffset.HasValue && (double.IsNaN(look.TextureOffset.Value) || double.IsInfinity(look.TextureOffset.Value)))
                    throw new ArgumentException("The texture offset of spline " + look.Spline.Id + " must be a number.", nameof(looks));
                OmsiSplineView view = null;
                foreach (var candidate in OmsiTileRecords.Of<OmsiSplineView>(files.Tile(look.Tile.FileName)))
                    if (candidate.Id == look.Spline.Id)
                        view = candidate;
                if (view == null)
                    throw new InvalidOperationException("Spline " + look.Spline.Id + " was not found in " + look.Tile.FileName + ".");
                var differs = false;
                if (view.IsMirrored != look.Mirrored)
                {
                    view.IsMirrored = look.Mirrored;
                    differs = true;
                }
                if (look.TextureOffset.HasValue && look.Spline.ChainOffset.HasValue && Math.Abs(view.ChainOffset - look.TextureOffset.Value) >= 0.0005)
                {
                    view.ChainOffset = look.TextureOffset.Value;
                    differs = true;
                }
                if (!differs)
                    continue;
                change.SplineIds.Add(look.Spline.Id);
                if (!change.Tiles.Contains(look.Tile))
                    change.Tiles.Add(look.Tile);
            }
            if (change.SplineIds.Count == 0)
                return null;
            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            return change;
        }
    }
}
