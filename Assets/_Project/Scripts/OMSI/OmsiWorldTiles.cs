using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The tiles of <c>[worldcoordinates]</c> maps: the tiles of the Web Mercator projection at zoom 16, as web maps
    /// number them, counted east from Greenwich and north from the equator. A tile is about 611.5 m across at the
    /// equator and shrinks with the cosine of the latitude, so Berlin-Spandau's tiles are about 371.9 m. Positions in a
    /// tile are metres at the scale of their own latitude.
    /// </summary>
    public static class OmsiWorldTiles
    {
        public const int Zoom = 16;

        /// <summary>The length of the equator of the WGS 84 ellipsoid, which Web Mercator uses as a sphere.</summary>
        public const double EquatorMeters = 40075016.685578488;

        private const double TilesAround = 1 << Zoom;

        /// <summary>Latitude in degrees of a row, counted in tiles north of the equator; fractions lie within a tile.</summary>
        public static double LatitudeDegrees(double row)
        {
            return Math.Atan(Math.Sinh(row * 2.0 * Math.PI / TilesAround)) * 180.0 / Math.PI;
        }

        /// <summary>Longitude in degrees of a column, counted in tiles east of Greenwich.</summary>
        public static double LongitudeDegrees(double column)
        {
            return column * 360.0 / TilesAround;
        }

        /// <summary>The edge of a tile in metres at the latitude of a row.</summary>
        public static double EdgeMeters(double row)
        {
            return EquatorMeters * Math.Cos(LatitudeDegrees(row) * Math.PI / 180.0) / TilesAround;
        }

        /// <summary>
        /// The edge for drawing a whole map as one even grid: the edge in the middle of the map's middle row. A flat grid
        /// cannot follow the change of scale between rows, so tiles far from the middle row meet a few decimetres apart;
        /// the geometry itself is exact with <see cref="ToGrid"/>.
        /// </summary>
        public static double GridEdgeMeters(IEnumerable<int> rows)
        {
            var sorted = new List<int>(rows);
            if (sorted.Count == 0)
                return EdgeMeters(0.5);
            sorted.Sort();
            return EdgeMeters(sorted[sorted.Count / 2] + 0.5);
        }

        /// <summary>
        /// A tile-local position as fractional grid column and row. North of the tile corner the scale changes with the
        /// latitude, east of it the scale of the position's own latitude applies.
        /// </summary>
        public static void ToGrid(int tileX, int tileY, double localX, double localY, out double column, out double row)
        {
            // Two steps of the midpoint rule; the scale changes by less than 0.01 % within a tile.
            var first = tileY + localY / EdgeMeters(tileY);
            row = tileY + localY / EdgeMeters(0.5 * (tileY + first));
            column = tileX + localX / EdgeMeters(row);
        }

        /// <summary>The distance in metres between two tile-local positions of a world-coordinate map.</summary>
        public static double DistanceMeters(int firstTileX, int firstTileY, double firstX, double firstY, int secondTileX, int secondTileY,
            double secondX, double secondY)
        {
            ToGrid(firstTileX, firstTileY, firstX, firstY, out var firstColumn, out var firstRow);
            ToGrid(secondTileX, secondTileY, secondX, secondY, out var secondColumn, out var secondRow);
            var scale = EdgeMeters(0.5 * (firstRow + secondRow));
            var east = (secondColumn - firstColumn) * scale;
            var north = (secondRow - firstRow) * scale;
            return Math.Sqrt(east * east + north * north);
        }

        /// <summary>Latitude and longitude of the south-west corner of a tile, as text such as "52.5312° N, 13.1903° E".</summary>
        public static string DescribeCorner(int tileX, int tileY)
        {
            var latitude = LatitudeDegrees(tileY);
            var longitude = LongitudeDegrees(tileX);
            return Math.Abs(latitude).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "° " + (latitude >= 0 ? "N" : "S") + ", " +
                   Math.Abs(longitude).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "° " + (longitude >= 0 ? "E" : "W");
        }
    }
}
