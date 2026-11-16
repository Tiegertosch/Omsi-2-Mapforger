using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The pieces of a whole road: the chain of splines through a spline, from junction or free end to junction or free end, that have its road type.
    /// One helper for Whole road in the inspector and for the traffic rules (ADR 050, ADR 099).
    /// </summary>
    public static class OmsiWholeRoad
    {
        /// <summary>True when two spline profile paths name the same file, whatever the slash, case or surrounding blanks.</summary>
        public static bool SameAsset(string first, string second)
        {
            return first != null && second != null &&
                   string.Equals(first.Trim().Replace('/', '\\'), second.Trim().Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The splines of the chain through the given one that have its profile, in chain order, with their tiles. The given spline is always among them,
        /// even when the chain does not list it.
        /// </summary>
        public static List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> Pieces(OmsiSplineAttachmentLayout layout, OmsiTileSummary tile, OmsiSplineRecord spline)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            var pieces = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
            foreach (var segment in layout.Chain(tile, spline))
                if (SameAsset(segment.Spline.AssetPath, spline.AssetPath))
                    pieces.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(segment.Tile, segment.Spline));
            if (!pieces.Exists(pair => ReferenceEquals(pair.Value, spline)))
                pieces.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(tile, spline));
            return pieces;
        }
    }
}
