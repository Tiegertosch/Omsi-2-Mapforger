using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A straight line segment in tile-local metres.</summary>
    public struct OmsiLineSegment
    {
        public readonly OmsiMapPoint Start;
        public readonly OmsiMapPoint End;

        public OmsiLineSegment(OmsiMapPoint start, OmsiMapPoint end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Builds every spline polyline of a map once and buckets the clipped pieces by the tile they cross.
    /// A spline that starts on one tile and runs into a neighbour appears in both tiles.
    /// </summary>
    public sealed class OmsiSplineTileIndex
    {
        private static readonly IReadOnlyList<OmsiLineSegment> NoSegments = Array.Empty<OmsiLineSegment>();
        private readonly Dictionary<long, List<OmsiLineSegment>> segmentsByTile = new Dictionary<long, List<OmsiLineSegment>>();

        public int SegmentCount { get; private set; }

        public static OmsiSplineTileIndex Build(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            var index = new OmsiSplineTileIndex();
            var tileSize = summary.TileSizeMeters;
            if (tileSize <= 0.0)
                return index;

            var mapTiles = new HashSet<long>();
            foreach (var tile in summary.Tiles)
                mapTiles.Add(Key(tile.X, tile.Y));

            foreach (var sourceTile in summary.Tiles)
            {
                foreach (var spline in sourceTile.Splines)
                {
                    var points = OmsiMapGeometry.BuildSplinePolyline(sourceTile, spline, tileSize);
                    for (var pointIndex = 1; pointIndex < points.Length; pointIndex++)
                        index.AddSegment(points[pointIndex - 1], points[pointIndex], tileSize, mapTiles);
                }
            }

            return index;
        }

        /// <summary>Returns the spline pieces inside one tile in tile-local metres, or an empty list.</summary>
        public IReadOnlyList<OmsiLineSegment> GetSegments(int tileX, int tileY)
        {
            return segmentsByTile.TryGetValue(Key(tileX, tileY), out var segments) ? segments : NoSegments;
        }

        private void AddSegment(OmsiMapPoint start, OmsiMapPoint end, double tileSize, HashSet<long> mapTiles)
        {
            var firstTileX = (int)Math.Floor(Math.Min(start.X, end.X) / tileSize);
            var lastTileX = (int)Math.Floor(Math.Max(start.X, end.X) / tileSize);
            var firstTileY = (int)Math.Floor(Math.Min(start.Y, end.Y) / tileSize);
            var lastTileY = (int)Math.Floor(Math.Max(start.Y, end.Y) / tileSize);

            for (var tileX = firstTileX; tileX <= lastTileX; tileX++)
            {
                for (var tileY = firstTileY; tileY <= lastTileY; tileY++)
                {
                    var key = Key(tileX, tileY);
                    if (!mapTiles.Contains(key) ||
                        !OmsiMapGeometry.TryClipLineToTile(start, end, tileX, tileY, tileSize, out var clippedStart, out var clippedEnd))
                        continue;

                    if (!segmentsByTile.TryGetValue(key, out var segments))
                    {
                        segments = new List<OmsiLineSegment>();
                        segmentsByTile.Add(key, segments);
                    }

                    segments.Add(new OmsiLineSegment(clippedStart, clippedEnd));
                    SegmentCount++;
                }
            }
        }

        private static long Key(int tileX, int tileY)
        {
            return ((long)tileX << 32) | (uint)tileY;
        }
    }
}
