using System;
using System.Globalization;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The heights a height map's black and white stand for, as its text file beside it names them.</summary>
    public struct OmsiHeightRange
    {
        public double Lowest;
        public double Highest;
    }

    /// <summary>A height map written: its size in pixels and the heights black and white stand for.</summary>
    public sealed class OmsiHeightMapExport
    {
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public OmsiHeightRange Range { get; internal set; }

        /// <summary>The 16-bit grey PNG.</summary>
        public byte[] Png { get; internal set; }

        /// <summary>The text file beside it: lowest and highest height, metres per pixel and the tiles.</summary>
        public string Note { get; internal set; }
    }

    /// <summary>
    /// The map's ground as a grey picture and back, as openMapper's height map does (`src/heightmap.rs`), so the two can
    /// pass height maps to each other: a 16-bit grey PNG over all the map's tiles, one pixel per terrain sample, north up,
    /// its lowest and highest height in a text file beside it; any grey picture laid back over the tiles, stretched to
    /// their outer edges.
    /// </summary>
    public static class OmsiHeightMap
    {
        private const int Cells = OmsiTerrain.Resolution - 1;

        /// <summary>The map's rectangle of tiles with terrain: the lowest and highest tile numbers.</summary>
        public static bool Extent(OmsiMapSummary summary, out int minX, out int minY, out int maxX, out int maxY)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            minX = minY = int.MaxValue;
            maxX = maxY = int.MinValue;
            foreach (var tile in summary.Tiles)
            {
                if (!tile.IsAvailable || !tile.HasTerrain)
                    continue;
                minX = Math.Min(minX, tile.X);
                minY = Math.Min(minY, tile.Y);
                maxX = Math.Max(maxX, tile.X);
                maxY = Math.Max(maxY, tile.Y);
            }
            return minX <= maxX;
        }

        /// <summary>
        /// The ground of the grid as a 16-bit grey picture: lowest black, highest white (a metre apart at least), where
        /// the map has no tile the lowest height.
        /// </summary>
        public static OmsiHeightMapExport Export(OmsiMapSummary summary, OmsiTerrainGrid grid, string mapName)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (!Extent(summary, out var minX, out var minY, out var maxX, out var maxY))
                throw new InvalidOperationException("The map has no terrain.");
            var width = (maxX - minX + 1) * Cells + 1;
            var height = (maxY - minY + 1) * Cells + 1;
            var values = new float?[width * height];
            var lowest = float.MaxValue;
            var highest = float.MinValue;
            for (var py = 0; py < height; py++)
                for (var px = 0; px < width; px++)
                {
                    // Row 0 is the north edge.
                    var value = grid.Get((long)minX * Cells + px, ((long)maxY + 1) * Cells - py);
                    values[py * width + px] = value;
                    if (!value.HasValue)
                        continue;
                    lowest = Math.Min(lowest, value.Value);
                    highest = Math.Max(highest, value.Value);
                }
            if (lowest > highest)
                throw new InvalidOperationException("The map has no terrain.");
            if (highest - lowest < 0.01f)
                highest = lowest + 1f;
            var span = highest - lowest;
            var pixels = new ushort[width * height];
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = (ushort)Math.Round(Math.Max(0.0, Math.Min(1.0, ((values[index] ?? lowest) - lowest) / span)) * 65535.0);
            var note = new StringBuilder();
            note.Append("OMSI MapWorks height map of ").Append(mapName ?? string.Empty).Append('\n');
            note.Append("lowest ").Append(lowest.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            note.Append("highest ").Append(highest.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            note.Append("metres per pixel ").Append(grid.CellMeters.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            note.Append("tiles ").Append(minX).Append(' ').Append(minY).Append(" to ").Append(maxX).Append(' ').Append(maxY).Append('\n');
            return new OmsiHeightMapExport
            {
                Width = width, Height = height, Range = new OmsiHeightRange { Lowest = lowest, Highest = highest }, Png = OmsiPng.EncodeGrey16(width, height, pixels),
                Note = note.ToString()
            };
        }

        /// <summary>The heights a height map's text file names (its "lowest" and "highest" lines), or null.</summary>
        public static OmsiHeightRange? ReadRange(string note)
        {
            if (string.IsNullOrEmpty(note))
                return null;
            double? lowest = null, highest = null;
            foreach (var line in note.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("lowest", StringComparison.Ordinal) &&
                    double.TryParse(trimmed.Substring(6).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var low))
                    lowest = low;
                else if (trimmed.StartsWith("highest", StringComparison.Ordinal) &&
                         double.TryParse(trimmed.Substring(7).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
                    highest = high;
            }
            return lowest.HasValue && highest.HasValue ? new OmsiHeightRange { Lowest = lowest.Value, Highest = highest.Value } : (OmsiHeightRange?)null;
        }

        /// <summary>
        /// A picture laid over all the map's tiles with terrain, its edges on the map's outer edges: black at the lowest
        /// height, white at the highest, taking the ground's place or added to it. Every sample is set once, the edges
        /// between tiles in both.
        /// </summary>
        public static void Import(OmsiMapSummary summary, OmsiTerrainGrid grid, OmsiGreyPicture picture, OmsiHeightRange range, bool add)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (picture == null)
                throw new ArgumentNullException(nameof(picture));
            if (!Extent(summary, out var minX, out var minY, out var maxX, out var maxY))
                return;
            var columns = (long)(maxX - minX + 1) * Cells;
            var rows = (long)(maxY - minY + 1) * Cells;
            var span = range.Highest - range.Lowest;
            for (var gy = (long)minY * Cells; gy <= ((long)maxY + 1) * Cells; gy++)
                for (var gx = (long)minX * Cells; gx <= ((long)maxX + 1) * Cells; gx++)
                {
                    var current = grid.Get(gx, gy);
                    if (!current.HasValue)
                        continue;
                    var u = (gx - (long)minX * Cells) / (double)columns;
                    var v = 1.0 - (gy - (long)minY * Cells) / (double)rows;
                    var z = range.Lowest + picture.At(u, v) * span;
                    grid.Set(gx, gy, (float)(add ? current.Value + z : z));
                }
        }
    }
}
