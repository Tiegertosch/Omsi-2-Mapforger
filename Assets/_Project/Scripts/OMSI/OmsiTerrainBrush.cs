using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What a terrain brush does to the ground under it (openMapper's Terrain tool).</summary>
    public enum OmsiTerrainBrushMode
    {
        Raise,
        Lower,
        Smooth,

        /// <summary>Towards one height: where the stroke started, or a height typed.</summary>
        Flatten,

        /// <summary>Towards a fixed pattern of rolls, the dips and rises of a natural field.</summary>
        Roughen,

        /// <summary>A lake or a river bed: dug towards a depth under the water's surface, never filled.</summary>
        Water
    }

    /// <summary>A point along a road, where the ground must stay below it: its place, height and half width.</summary>
    public struct OmsiRoadPoint
    {
        public double X;
        public double Y;
        public double Height;
        public double HalfWidth;
    }

    /// <summary>
    /// The terrain of several tiles as one grid over the map: sample (gx, gy) lies at gx and gy times the cell size from
    /// the map's origin, tile (x, y) holds samples x·60 to x·60+60. A sample on a tile's edge belongs to both tiles and
    /// is set in both, so tiles meet without a step (openMapper, `world_sample`).
    /// </summary>
    public sealed class OmsiTerrainGrid
    {
        private const int Cells = OmsiTerrain.Resolution - 1;

        private readonly Dictionary<long, OmsiTileSummary> tiles = new Dictionary<long, OmsiTileSummary>();
        private readonly Dictionary<OmsiTileSummary, float[]> heights = new Dictionary<OmsiTileSummary, float[]>();
        private readonly Func<OmsiTileSummary, float[]> load;

        /// <param name="load">Reads a tile's 61 × 61 heights, row by row from the south; null leaves the tile out.</param>
        public OmsiTerrainGrid(OmsiMapSummary summary, Func<OmsiTileSummary, float[]> load)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            this.load = load ?? throw new ArgumentNullException(nameof(load));
            TileSizeMeters = summary.TileSizeMeters;
            foreach (var tile in summary.Tiles)
                if (tile.IsAvailable && tile.HasTerrain)
                    tiles[Key(tile.X, tile.Y)] = tile;
        }

        public double TileSizeMeters { get; }

        /// <summary>The distance between samples.</summary>
        public double CellMeters => TileSizeMeters / Cells;

        /// <summary>The tiles whose heights were read so far, with them.</summary>
        public IReadOnlyDictionary<OmsiTileSummary, float[]> Loaded => heights;

        /// <summary>The tiles a sample was set in since the grid was made.</summary>
        public HashSet<OmsiTileSummary> Changed { get; } = new HashSet<OmsiTileSummary>();

        /// <summary>The height of a sample, from whichever tile holds it; null where the map has no terrain.</summary>
        public float? Get(long gx, long gy)
        {
            foreach (var tx in Owners(gx))
                foreach (var ty in Owners(gy))
                {
                    var data = Heights(tx, ty, out _);
                    if (data != null)
                        return data[(gy - ty * Cells) * OmsiTerrain.Resolution + (gx - tx * Cells)];
                }
            return null;
        }

        /// <summary>Sets a sample in every tile that holds it.</summary>
        public void Set(long gx, long gy, float height)
        {
            foreach (var tx in Owners(gx))
                foreach (var ty in Owners(gy))
                {
                    var data = Heights(tx, ty, out var tile);
                    if (data == null)
                        continue;
                    data[(gy - ty * Cells) * OmsiTerrain.Resolution + (gx - tx * Cells)] = height;
                    Changed.Add(tile);
                }
        }

        /// <summary>The ground's height at a world position, between the samples around it; null outside the terrain.</summary>
        public double? HeightAt(double x, double y)
        {
            var fx = x / CellMeters;
            var fy = y / CellMeters;
            var gx = (long)Math.Floor(fx);
            var gy = (long)Math.Floor(fy);
            var a = Get(gx, gy);
            var b = Get(gx + 1, gy);
            var c = Get(gx, gy + 1);
            var d = Get(gx + 1, gy + 1);
            if (!a.HasValue || !b.HasValue || !c.HasValue || !d.HasValue)
                return a;
            var u = fx - gx;
            var v = fy - gy;
            return (a.Value * (1 - u) + b.Value * u) * (1 - v) + (c.Value * (1 - u) + d.Value * u) * v;
        }

        /// <summary>The tiles along one axis that hold a sample: two on an edge between tiles, else one.</summary>
        private static IEnumerable<long> Owners(long g)
        {
            var tile = FloorDiv(g, Cells);
            yield return tile;
            if (g - tile * Cells == 0)
                yield return tile - 1;
        }

        private float[] Heights(long tx, long ty, out OmsiTileSummary tile)
        {
            if (!tiles.TryGetValue(Key(tx, ty), out tile))
                return null;
            if (!heights.TryGetValue(tile, out var data))
            {
                data = load(tile);
                if (data != null && data.Length != OmsiTerrain.Resolution * OmsiTerrain.Resolution)
                    data = null;
                heights[tile] = data;
            }
            return data;
        }

        private static long FloorDiv(long value, long divisor)
        {
            var quotient = value / divisor;
            return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
        }

        private static long Key(long x, long y)
        {
            return (x << 32) ^ (y & 0xffffffffL);
        }
    }

    /// <summary>
    /// The ground shaped by hand as openMapper's Terrain tool does (`brush_terrain`, `ramp` and `rolls` in its
    /// `src/editor.rs`), so maps behave alike in openOMSI: a round brush with a soft rim held over the ground, and a ramp
    /// between two points. The ground never rises through a road beside it.
    /// </summary>
    public static class OmsiTerrainBrush
    {
        /// <summary>The ground stays this far below a road beside it, so its triangles do not show through.</summary>
        private const double BelowRoadMeters = 0.12;

        /// <summary>
        /// The brush held over (x, y) for <paramref name="seconds"/>: every sample within the radius changes by the mode,
        /// weighted (1 − (d/r)²)² towards the rim. Smoothing reads the neighbours as they were before this step.
        /// </summary>
        /// <param name="strength">0.05 to 1; raising at full strength lifts the middle 12 m a second.</param>
        /// <param name="flattenTo">The height Flatten works towards.</param>
        /// <param name="roads">Points along the roads near the brush; the ground stays below them.</param>
        /// <param name="waterBed">The height the Water brush digs towards: the water's surface less its depth.</param>
        public static void Stroke(OmsiTerrainGrid grid, OmsiTerrainBrushMode mode, double x, double y, double radius, double strength, double seconds,
            double flattenTo, IList<OmsiRoadPoint> roads, double waterBed = 0.0)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (!(radius > 0.0) || !(seconds > 0.0))
                return;
            var cell = grid.CellMeters;
            var changes = new List<KeyValuePair<KeyValuePair<long, long>, float>>();
            for (var gy = (long)Math.Floor((y - radius) / cell); gy <= (long)Math.Ceiling((y + radius) / cell); gy++)
                for (var gx = (long)Math.Floor((x - radius) / cell); gx <= (long)Math.Ceiling((x + radius) / cell); gx++)
                {
                    var px = gx * cell;
                    var py = gy * cell;
                    var distance = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (distance > radius)
                        continue;
                    var current = grid.Get(gx, gy);
                    if (!current.HasValue)
                        continue;
                    var falloff = 1.0 - distance / radius * (distance / radius);
                    var weight = falloff * falloff;
                    var height = (double)current.Value;
                    double next;
                    switch (mode)
                    {
                        case OmsiTerrainBrushMode.Raise:
                            next = height + strength * 12.0 * seconds * weight;
                            break;
                        case OmsiTerrainBrushMode.Lower:
                            next = height - strength * 12.0 * seconds * weight;
                            break;
                        case OmsiTerrainBrushMode.Smooth:
                            var sum = 0.0;
                            var count = 0;
                            for (var dy = -1; dy <= 1; dy++)
                                for (var dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0)
                                        continue;
                                    var neighbour = grid.Get(gx + dx, gy + dy);
                                    if (!neighbour.HasValue)
                                        continue;
                                    sum += neighbour.Value;
                                    count++;
                                }
                            next = count > 0 ? height + (sum / count - height) * Math.Min(1.0, strength * 6.0 * seconds * weight) : height;
                            break;
                        case OmsiTerrainBrushMode.Flatten:
                            next = height + (flattenTo - height) * Math.Min(1.0, strength * 8.0 * seconds * weight);
                            break;
                        case OmsiTerrainBrushMode.Water:
                            // Dug towards the bed, never filled; the banks ease out with the brush's rim.
                            next = height > waterBed ? height + (waterBed - height) * Math.Min(1.0, strength * 8.0 * seconds * weight) : height;
                            break;
                        default:
                            // Towards rolls that stay put wherever the brush goes, so holding it longer shapes them more.
                            next = height + Rolls(px, py) * strength * 3.0 * seconds * weight;
                            break;
                    }
                    changes.Add(new KeyValuePair<KeyValuePair<long, long>, float>(new KeyValuePair<long, long>(gx, gy), (float)next));
                }
            foreach (var change in changes)
                grid.Set(change.Key.Key, change.Key.Value, BelowRoads(grid, change.Key.Key, change.Key.Value, change.Value, roads));
        }

        /// <summary>
        /// The ground from a to b made one even slope from a's height to b's, as wide as the brush: fully within 60 % of its
        /// radius, easing out to its rim. Returns the gradient in percent.
        /// </summary>
        public static double Ramp(OmsiTerrainGrid grid, double ax, double ay, double aHeight, double bx, double by, double bHeight, double radius)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            var core = radius * 0.6;
            var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            var cell = grid.CellMeters;
            var changes = new List<KeyValuePair<KeyValuePair<long, long>, float>>();
            for (var gy = (long)Math.Floor((Math.Min(ay, by) - radius) / cell); gy <= (long)Math.Ceiling((Math.Max(ay, by) + radius) / cell); gy++)
                for (var gx = (long)Math.Floor((Math.Min(ax, bx) - radius) / cell); gx <= (long)Math.Ceiling((Math.Max(ax, bx) + radius) / cell); gx++)
                {
                    var px = gx * cell;
                    var py = gy * cell;
                    var t = length > 1e-6 ? Math.Max(0.0, Math.Min(1.0, ((px - ax) * (bx - ax) + (py - ay) * (by - ay)) / (length * length))) : 0.0;
                    var nearX = ax + (bx - ax) * t;
                    var nearY = ay + (by - ay) * t;
                    var distance = Math.Sqrt((px - nearX) * (px - nearX) + (py - nearY) * (py - nearY));
                    if (distance > radius)
                        continue;
                    var current = grid.Get(gx, gy);
                    if (!current.HasValue)
                        continue;
                    var target = aHeight + (bHeight - aHeight) * t;
                    double weight;
                    if (distance <= core)
                        weight = 1.0;
                    else
                    {
                        var s = 1.0 - (distance - core) / (radius - core);
                        weight = s * s * (3.0 - 2.0 * s);
                    }
                    changes.Add(new KeyValuePair<KeyValuePair<long, long>, float>(new KeyValuePair<long, long>(gx, gy),
                        (float)(current.Value + (target - current.Value) * weight)));
                }
            foreach (var change in changes)
                grid.Set(change.Key.Key, change.Key.Value, change.Value);
            return length > 1e-6 ? (bHeight - aHeight) / length * 100.0 : 0.0;
        }

        /// <summary>The ground eases from a levelled outline back into the ground around over this distance.</summary>
        private const double LevelMarginMeters = 3.0;

        /// <summary>
        /// The ground under an outline made level (openMapper, `level_ground`): within the rectangle from
        /// <paramref name="min"/> to <paramref name="max"/> in the frame of something standing at (x, y) turned by the
        /// heading (x right, y forward), the ground lies at the height; over the next 3 m it eases back into the ground.
        /// </summary>
        public static void Level(OmsiTerrainGrid grid, double x, double y, double headingDegrees, OmsiMapPoint min, OmsiMapPoint max, double height)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            var heading = headingDegrees * Math.PI / 180.0;
            var sin = Math.Sin(heading);
            var cos = Math.Cos(heading);
            var reach = Math.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y)) / 2.0 + LevelMarginMeters +
                        Math.Sqrt((min.X + max.X) * (min.X + max.X) + (min.Y + max.Y) * (min.Y + max.Y)) / 2.0;
            var cell = grid.CellMeters;
            var changes = new List<KeyValuePair<KeyValuePair<long, long>, float>>();
            for (var gy = (long)Math.Floor((y - reach) / cell); gy <= (long)Math.Ceiling((y + reach) / cell); gy++)
                for (var gx = (long)Math.Floor((x - reach) / cell); gx <= (long)Math.Ceiling((x + reach) / cell); gx++)
                {
                    // Into the frame of the object: forward is the heading, clockwise from north; right is a quarter turn on.
                    var dx = gx * cell - x;
                    var dy = gy * cell - y;
                    var right = dx * cos - dy * sin;
                    var forward = dx * sin + dy * cos;
                    var outsideX = Math.Max(0.0, Math.Max(min.X - right, right - max.X));
                    var outsideY = Math.Max(0.0, Math.Max(min.Y - forward, forward - max.Y));
                    var outside = Math.Sqrt(outsideX * outsideX + outsideY * outsideY);
                    if (outside >= LevelMarginMeters)
                        continue;
                    var current = grid.Get(gx, gy);
                    if (!current.HasValue)
                        continue;
                    var t = outside / LevelMarginMeters;
                    var weight = 1.0 - t * t * (3.0 - 2.0 * t);
                    changes.Add(new KeyValuePair<KeyValuePair<long, long>, float>(new KeyValuePair<long, long>(gx, gy),
                        (float)(current.Value + (height - current.Value) * weight)));
                }
            foreach (var change in changes)
                grid.Set(change.Key.Key, change.Key.Value, change.Value);
        }

        /// <summary>Rolls of natural ground at a place: smooth value noise about 40 m and 12 m across, between −1 and 1.</summary>
        public static double Rolls(double x, double y)
        {
            return Noise(x / 40.0, y / 40.0) * 0.7 + Noise(x / 12.0 + 17.3, y / 12.0 + 17.3) * 0.3;
        }

        /// <summary>
        /// Points every metre along the visible roads near a place, with their height and half width: splines whose
        /// profile lies on the ground, not wires, fences or helpers.
        /// </summary>
        public static List<OmsiRoadPoint> RoadPointsNear(OmsiMapSummary summary, OmsiSplineProfileCatalog profiles, double x, double y, double within)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var result = new List<OmsiRoadPoint>();
            if (profiles == null)
                return result;
            var size = summary.TileSizeMeters;
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    var startX = tile.X * size + spline.LocalX;
                    var startY = tile.Y * size + spline.LocalY;
                    if (Math.Sqrt((startX - x) * (startX - x) + (startY - y) * (startY - y)) > spline.LengthMeters + within + 20.0)
                        continue;
                    var profile = profiles.Get(spline.AssetPath);
                    if (profile == null || profile.EditorOnly || profile.LineKind != OmsiSplineLineKind.None)
                        continue;
                    var halfWidth = Math.Max(profile.RightEdge, -profile.LeftEdge);
                    if (!(halfWidth > 0.0))
                        continue;
                    var steps = Math.Max(1, (int)Math.Ceiling(spline.LengthMeters));
                    for (var step = 0; step <= steps; step++)
                    {
                        var along = spline.LengthMeters * step / steps;
                        OmsiMapGeometry.GetLocalFrameAt(spline, along, out var local, out _);
                        var px = tile.X * size + local.X;
                        var py = tile.Y * size + local.Y;
                        if (Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y)) <= within + halfWidth)
                            result.Add(new OmsiRoadPoint { X = px, Y = py, Height = spline.HeightAt(along), HalfWidth = halfWidth });
                    }
                }
            return result;
        }

        /// <summary>
        /// A new height for a sample beside a road: no higher than 12 cm under the road, unless the ground already was;
        /// ground that already stood there stays.
        /// </summary>
        private static float BelowRoads(OmsiTerrainGrid grid, long gx, long gy, float height, IList<OmsiRoadPoint> roads)
        {
            if (roads == null || roads.Count == 0)
                return height;
            var cell = grid.CellMeters;
            var px = gx * cell;
            var py = gy * cell;
            double? cap = null;
            foreach (var road in roads)
            {
                var distance = Math.Sqrt((road.X - px) * (road.X - px) + (road.Y - py) * (road.Y - py));
                if (distance <= road.HalfWidth + cell * Math.Sqrt(2.0) + 0.1)
                    cap = Math.Min(cap ?? double.MaxValue, road.Height - BelowRoadMeters);
            }
            if (!cap.HasValue)
                return height;
            var before = grid.Get(gx, gy) ?? height;
            return (float)Math.Min(height, Math.Max(cap.Value, before));
        }

        private static double Noise(double x, double y)
        {
            var x0 = Math.Floor(x);
            var y0 = Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            var sx = fx * fx * (3.0 - 2.0 * fx);
            var sy = fy * fy * (3.0 - 2.0 * fy);
            var ix = (long)x0;
            var iy = (long)y0;
            var a = Hash(ix, iy) + (Hash(ix + 1, iy) - Hash(ix, iy)) * sx;
            var b = Hash(ix, iy + 1) + (Hash(ix + 1, iy + 1) - Hash(ix, iy + 1)) * sx;
            return a + (b - a) * sy;
        }

        /// <summary>A value between −1 and 1 for each grid point, the same as openMapper's.</summary>
        private static double Hash(long x, long y)
        {
            unchecked
            {
                var h = (ulong)x * 0x9e3779b97f4a7c15UL ^ (ulong)y * 0xc2b2ae3d27d4eb4fUL;
                h ^= h >> 31;
                h *= 0xff51afd7ed558ccdUL;
                h ^= h >> 33;
                return h % 20001UL / 10000.0 - 1.0;
            }
        }
    }
}
