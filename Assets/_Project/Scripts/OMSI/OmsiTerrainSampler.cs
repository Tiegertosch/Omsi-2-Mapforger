using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Terrain heights at world positions; a position without terrain is reported, never taken as height 0.</summary>
    public interface IOmsiTerrainHeights
    {
        /// <summary>
        /// The terrain height in metres at a world position, east and north in metres from tile 0, 0 as
        /// <see cref="OmsiMapGeometry.TileLocalToWorld"/> counts them; false where no tile with terrain lies.
        /// </summary>
        bool TryGetHeight(double worldX, double worldY, out double height);
    }

    /// <summary>
    /// Reads the terrain of a map's tiles on first use and interpolates heights across tile borders. The terrain files
    /// are only read; roads keep their own heights and never change the terrain.
    /// </summary>
    public sealed class OmsiTerrainSampler : IOmsiTerrainHeights
    {
        private readonly IOmsiFileSource source;
        private readonly double tileSize;
        private readonly Dictionary<long, OmsiTileSummary> tiles = new Dictionary<long, OmsiTileSummary>();
        private readonly Dictionary<long, OmsiTerrain> terrains = new Dictionary<long, OmsiTerrain>();

        public OmsiTerrainSampler(OmsiMapSummary summary, IOmsiFileSource source)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            tileSize = summary.TileSizeMeters;
            foreach (var tile in summary.Tiles)
                if (tile.IsAvailable && tile.HasTerrain)
                    tiles[Key(tile.X, tile.Y)] = tile;
        }

        public bool TryGetHeight(double worldX, double worldY, out double height)
        {
            height = 0.0;
            if (double.IsNaN(worldX) || double.IsNaN(worldY) || double.IsInfinity(worldX) || double.IsInfinity(worldY))
                return false;
            var tileX = (int)Math.Floor(worldX / tileSize);
            var tileY = (int)Math.Floor(worldY / tileSize);
            var terrain = TerrainOf(tileX, tileY);
            if (terrain == null)
                return false;
            height = terrain.HeightAt(worldX - tileX * tileSize, worldY - tileY * tileSize, tileSize);
            return true;
        }

        private OmsiTerrain TerrainOf(int tileX, int tileY)
        {
            var key = Key(tileX, tileY);
            if (terrains.TryGetValue(key, out var cached))
                return cached;
            OmsiTerrain terrain = null;
            if (tiles.TryGetValue(key, out var tile))
            {
                try
                {
                    terrain = OmsiTerrain.Parse(source.ReadAllBytes(OmsiRelativePath.Normalize(tile.FileName) + ".terrain"));
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException)
                {
                    // The reader already warned about this tile's terrain; positions on it count as without terrain.
                }
            }
            terrains.Add(key, terrain);
            return terrain;
        }

        private static long Key(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }
    }
}
