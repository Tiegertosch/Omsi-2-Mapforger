using System;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The heights of one map tile (<c>tile_x_y.map.terrain</c>): a 32-bit header of 60, then 61 × 61 single-precision
    /// heights in metres, one every 1/60 of the tile edge.
    /// </summary>
    public sealed class OmsiTerrain
    {
        public const int Resolution = 61;
        private const int Header = 60;

        private readonly float[] heights;

        private OmsiTerrain(float[] heights)
        {
            this.heights = heights;
        }

        /// <summary>Height at grid point (column, row); column 0 is the west edge and row 0 the south edge.</summary>
        public float this[int column, int row] => heights[row * Resolution + column];

        public static OmsiTerrain Parse(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length != 4 + Resolution * Resolution * 4 || BitConverter.ToInt32(bytes, 0) != Header)
                throw new InvalidDataException("Not an OMSI terrain file: expected the header 60 and 61 × 61 heights.");

            var heights = new float[Resolution * Resolution];
            Buffer.BlockCopy(bytes, 4, heights, 0, heights.Length * 4);
            return new OmsiTerrain(heights);
        }

        /// <summary>Bilinear height at a tile-local position; positions outside the tile use the nearest edge.</summary>
        public double HeightAt(double localX, double localY, double tileSizeMeters)
        {
            var step = tileSizeMeters / (Resolution - 1);
            var x = Math.Max(0.0, Math.Min(Resolution - 1, localX / step));
            var y = Math.Max(0.0, Math.Min(Resolution - 1, localY / step));
            var column = Math.Min(Resolution - 2, (int)x);
            var row = Math.Min(Resolution - 2, (int)y);
            var fx = x - column;
            var fy = y - row;
            var south = this[column, row] * (1 - fx) + this[column + 1, row] * fx;
            var north = this[column, row + 1] * (1 - fx) + this[column + 1, row + 1] * fx;
            return south * (1 - fy) + north * fy;
        }
    }
}
