using System;
using System.Globalization;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The terrain of one tile as a DirectX text mesh, and what it holds.</summary>
    public sealed class OmsiTerrainMeshExport
    {
        public byte[] Bytes { get; internal set; }
        public int Vertices { get; internal set; }
        public int Triangles { get; internal set; }

        /// <summary>The lowest and highest height written, in metres.</summary>
        public double Lowest { get; internal set; }

        public double Highest { get; internal set; }
    }

    /// <summary>
    /// Writes the terrain of a tile as a DirectX text mesh (<c>xof 0303txt</c>), to take into a 3D program (ADR 081): 61 × 61 points one
    /// sample apart, two triangles a cell, with normals and texture coordinates over the whole tile. The frame is OMSI's object frame:
    /// x east, y up, z north, the origin at the tile's south-west corner. Normals look across the border into the neighbouring tile when the
    /// grid has it, so tiles laid side by side show no seam in the light.
    /// </summary>
    public static class OmsiTerrainXExport
    {
        private const int Cells = OmsiTerrain.Resolution - 1;

        /// <param name="grid">The ground as it is shown, map-wide, so the neighbours are known.</param>
        public static OmsiTerrainMeshExport Build(OmsiTerrainGrid grid, OmsiTileSummary tile)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            long originX = (long)tile.X * Cells, originY = (long)tile.Y * Cells;
            var size = OmsiTerrain.Resolution;
            var heights = new double[size * size];
            double lowest = double.MaxValue, highest = double.MinValue;
            for (var row = 0; row < size; row++)
                for (var column = 0; column < size; column++)
                {
                    var height = grid.Get(originX + column, originY + row);
                    if (!height.HasValue)
                        throw new InvalidOperationException("Tile " + tile.X + ", " + tile.Y + " has no terrain to export.");
                    heights[row * size + column] = height.Value;
                    lowest = Math.Min(lowest, height.Value);
                    highest = Math.Max(highest, height.Value);
                }

            var step = grid.CellMeters;
            var text = new StringBuilder(size * size * 90);
            text.Append("xof 0303txt 0032\r\n\r\n");
            text.Append("// Terrain of tile ").Append(tile.X.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(tile.Y.ToString(CultureInfo.InvariantCulture))
                .Append(" written by OMSI MapWorks: x east, y up, z north, origin at the south-west corner.\r\n\r\n");
            text.Append("Mesh {\r\n").Append(size * size).Append(";\r\n");
            for (var row = 0; row < size; row++)
                for (var column = 0; column < size; column++)
                {
                    text.Append(Number(column * step)).Append(';').Append(Number(heights[row * size + column])).Append(';').Append(Number(row * step));
                    text.Append(row == size - 1 && column == size - 1 ? ";;\r\n" : ";,\r\n");
                }

            var triangles = Cells * Cells * 2;
            text.Append(triangles).Append(";\r\n");
            var written = 0;
            for (var row = 0; row < Cells; row++)
                for (var column = 0; column < Cells; column++)
                {
                    int southWest = row * size + column, southEast = southWest + 1, northWest = southWest + size, northEast = northWest + 1;
                    // Direct3D's front face is clockwise in its left-handed frame, so the face looks up when the corners run south-west, south-east, north-west.
                    Triangle(text, southWest, southEast, northWest, ++written == triangles);
                    Triangle(text, southEast, northEast, northWest, ++written == triangles);
                }

            text.Append("MeshNormals {\r\n").Append(size * size).Append(";\r\n");
            for (var row = 0; row < size; row++)
                for (var column = 0; column < size; column++)
                {
                    var slopeX = Slope(grid, heights, originX, originY, column, row, 1, 0, step);
                    var slopeZ = Slope(grid, heights, originX, originY, column, row, 0, 1, step);
                    var length = Math.Sqrt(slopeX * slopeX + 1.0 + slopeZ * slopeZ);
                    text.Append(Number(-slopeX / length)).Append(';').Append(Number(1.0 / length)).Append(';').Append(Number(-slopeZ / length));
                    text.Append(row == size - 1 && column == size - 1 ? ";;\r\n" : ";,\r\n");
                }
            text.Append(triangles).Append(";\r\n");
            written = 0;
            for (var row = 0; row < Cells; row++)
                for (var column = 0; column < Cells; column++)
                {
                    int southWest = row * size + column, southEast = southWest + 1, northWest = southWest + size, northEast = northWest + 1;
                    Triangle(text, southWest, southEast, northWest, ++written == triangles);
                    Triangle(text, southEast, northEast, northWest, ++written == triangles);
                }
            text.Append("}\r\n");

            // Texture coordinates over the tile like a picture of it: v counts down from the north edge.
            text.Append("MeshTextureCoords {\r\n").Append(size * size).Append(";\r\n");
            for (var row = 0; row < size; row++)
                for (var column = 0; column < size; column++)
                {
                    text.Append(Number((double)column / Cells)).Append(';').Append(Number(1.0 - (double)row / Cells));
                    text.Append(row == size - 1 && column == size - 1 ? ";;\r\n" : ";,\r\n");
                }
            text.Append("}\r\n");

            text.Append("MeshMaterialList {\r\n1;\r\n").Append(triangles).Append(";\r\n");
            for (var index = 0; index < triangles; index++)
                text.Append("0").Append(index == triangles - 1 ? ";;\r\n" : ",\r\n");
            text.Append("Material {\r\n0.800000;0.800000;0.800000;1.000000;;\r\n0.000000;\r\n0.000000;0.000000;0.000000;;\r\n0.000000;0.000000;0.000000;;\r\n}\r\n}\r\n");
            text.Append("}\r\n");

            return new OmsiTerrainMeshExport
            {
                Bytes = Encoding.ASCII.GetBytes(text.ToString()), Vertices = size * size, Triangles = triangles, Lowest = lowest, Highest = highest
            };
        }

        private static void Triangle(StringBuilder text, int first, int second, int third, bool last)
        {
            text.Append("3;").Append(first).Append(',').Append(second).Append(',').Append(third).Append(last ? ";;\r\n" : ";,\r\n");
        }

        /// <summary>The change of height per metre at a point, along the column or row axis, looking over the tile border where the grid has the neighbour.</summary>
        private static double Slope(OmsiTerrainGrid grid, double[] heights, long originX, long originY, int column, int row, int stepColumn, int stepRow, double step)
        {
            var size = OmsiTerrain.Resolution;
            double? Sample(int c, int r)
            {
                if (c >= 0 && c < size && r >= 0 && r < size)
                    return heights[r * size + c];
                var outside = grid.Get(originX + c, originY + r);
                return outside.HasValue ? outside.Value : (double?)null;
            }

            var before = Sample(column - stepColumn, row - stepRow);
            var after = Sample(column + stepColumn, row + stepRow);
            var here = heights[row * size + column];
            if (before.HasValue && after.HasValue)
                return (after.Value - before.Value) / (2.0 * step);
            if (after.HasValue)
                return (after.Value - here) / step;
            if (before.HasValue)
                return (here - before.Value) / step;
            return 0.0;
        }

        private static string Number(double value)
        {
            return value.ToString("0.000000", CultureInfo.InvariantCulture);
        }
    }
}
