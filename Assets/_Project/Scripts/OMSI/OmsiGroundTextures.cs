using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// One [groundtex] entry of global.cfg: a ground texture the map can be painted with. The first entry covers the
    /// whole map; every further one is painted through a mask per tile.
    /// </summary>
    public sealed class OmsiGroundTexture
    {
        /// <summary>Position in global.cfg; also the number in the mask file names.</summary>
        public int Index { get; internal set; }

        /// <summary>Path relative to the OMSI installation.</summary>
        public string TexturePath { get; internal set; }

        /// <summary>Path of the detail texture relative to the OMSI installation; multiplied onto the texture.</summary>
        public string DetailTexturePath { get; internal set; }

        /// <summary>The mask is 2^MaskSizeExponent pixels square.</summary>
        public int MaskSizeExponent { get; internal set; }

        /// <summary>How often the texture repeats across a tile.</summary>
        public double Repeats { get; internal set; }

        /// <summary>How often the detail texture repeats across a tile.</summary>
        public double DetailRepeats { get; internal set; }

        public bool HasMask => Index > 0;

        /// <summary>
        /// The mask of a tile relative to the map folder, <c>texture\map\tile_x_y.map.Index.dds</c>: one byte of
        /// coverage per pixel, first row at the north edge.
        /// </summary>
        public string MaskPath(string tileFileName)
        {
            if (!HasMask)
                throw new InvalidOperationException("The first ground texture covers the whole map and has no mask.");
            return Path.Combine("texture", "map", Path.GetFileName(OmsiRelativePath.Normalize(tileFileName)) + "." +
                                              Index.ToString(CultureInfo.InvariantCulture) + ".dds");
        }
    }

    public static class OmsiGroundTextures
    {
        /// <summary>The [groundtex] entries of global.cfg in order; entries with unreadable numbers are skipped.</summary>
        public static List<OmsiGroundTexture> Read(IOmsiFileSource source, string globalConfigRelativePath = "global.cfg")
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var config = OmsiRecordFile.Parse(source.ReadAllBytes(globalConfigRelativePath), OmsiGlobalConfigLayouts.Instance);
            var result = new List<OmsiGroundTexture>();
            var index = 0;
            foreach (var record in config.Records)
            {
                if (!string.Equals(record.Name, "groundtex", StringComparison.OrdinalIgnoreCase))
                    continue;
                // The mask numbers count every entry, also one that cannot be read.
                var position = index++;
                if (record.Fields.Count < 5 || !record.TryGetInt(2, out var exponent) ||
                    !record.TryGetDouble(3, out var repeats) || !record.TryGetDouble(4, out var detailRepeats))
                    continue;
                result.Add(new OmsiGroundTexture
                {
                    Index = position,
                    TexturePath = record.GetField(0).Trim(),
                    DetailTexturePath = record.GetField(1).Trim(),
                    MaskSizeExponent = exponent,
                    Repeats = repeats,
                    DetailRepeats = detailRepeats
                });
            }
            return result;
        }
    }
}
