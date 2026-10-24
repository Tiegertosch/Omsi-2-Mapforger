using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>How the terrain is fitted to a road.</summary>
    public sealed class OmsiTerrainShapeSettings
    {
        /// <summary>
        /// Beside the road the terrain stays at the height of the profile's edge, such as the top of a kerb, for this many
        /// metres before slopes begin, and at least one diagonal of the terrain grid: every grid triangle that reaches over
        /// the pavement then lies flat, rather than rising through it towards a slope. OMSI's own aligned roads in
        /// Berlin-Spandau keep about 10 m flat.
        /// </summary>
        public double ShoulderMeters { get; set; } = 2.0;

        /// <summary>
        /// Under the road and on the shoulder the terrain lies this far below the profile's edge, so where it reaches under
        /// the pavement it does not flicker with it; OMSI closes its cut up to the edge with a step this small.
        /// </summary>
        public double BelowEdgeMeters { get; set; } = 0.05;

        /// <summary>Embankments and cuttings rise one metre over this many metres from the shoulder (1:1.5).</summary>
        public double SlopeRatio { get; set; } = 1.5;

        /// <summary>Terrain further than this from the road edge is never changed.</summary>
        public double MaximumReachMeters { get; set; } = 40.0;
    }

    /// <summary>A spline the terrain is fitted to, in world metres, with the cross-section of its profile.</summary>
    public sealed class OmsiShapedRoad
    {
        public OmsiShapedRoad(OmsiTileSummary tile, OmsiSplineRecord spline, OmsiSplineProfile profile, double tileSizeMeters)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            Spline = spline ?? throw new ArgumentNullException(nameof(spline));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            // A mirrored profile is turned over: its left edge lies on the right.
            Left = spline.IsMirrored ? -profile.RightEdge : profile.LeftEdge;
            Right = spline.IsMirrored ? -profile.LeftEdge : profile.RightEdge;
            EdgeHeight = profile.EdgeHeight;
            var count = Math.Max(2, (int)Math.Ceiling(spline.LengthMeters));
            Points = new OmsiMapPoint[count + 1];
            Headings = new double[count + 1];
            for (var index = 0; index <= count; index++)
            {
                OmsiMapGeometry.GetLocalFrameAt(spline, spline.LengthMeters * index / count, out var position, out var heading);
                Points[index] = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, position.X, position.Y, tileSizeMeters);
                Headings[index] = heading;
            }
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var point in Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public OmsiSplineRecord Spline { get; }

        /// <summary>The edges of the cross-section, metres right of the spline; the left edge is negative.</summary>
        public double Left { get; }

        public double Right { get; }

        public double EdgeHeight { get; }

        /// <summary>The centre line, one point per metre or less.</summary>
        internal OmsiMapPoint[] Points { get; }

        internal double[] Headings { get; }

        internal double MinX { get; }
        internal double MinY { get; }
        internal double MaxX { get; }
        internal double MaxY { get; }

        public double HalfWidth => Math.Max(-Left, Right);
    }

    /// <summary>
    /// Fits the terrain heights of a tile to roads without touching anything else: under a road and on a shoulder beside it
    /// the terrain lies just below the height of the profile's edge, such as the top of a kerb, as OMSI's editor leaves it; OMSI
    /// cuts the terrain away under roads marked with [spline_terrain_align_2] and closes the cut up to the road edge, so
    /// ground lower than the edge would show there as a wall. Further out the terrain is kept within a slope from the
    /// shoulder, which cuts into higher ground and builds an embankment over lower ground, and beyond it stays as it was. A
    /// grid point under any road takes that road's height, so slopes of neighbouring pieces never lift or lower it; the
    /// result depends only on the base terrain and the roads.
    /// </summary>
    public static class OmsiTerrainShaping
    {
        /// <summary>
        /// Fits <paramref name="heights"/>, the 61 × 61 grid of tile (<paramref name="tileX"/>, <paramref name="tileY"/>),
        /// to the roads.
        /// </summary>
        /// <returns>The number of grid points that changed.</returns>
        public static int Shape(float[] heights, int tileX, int tileY, double tileSize, IEnumerable<OmsiShapedRoad> roads, OmsiTerrainShapeSettings settings)
        {
            if (heights == null || heights.Length != OmsiTerrain.Resolution * OmsiTerrain.Resolution)
                throw new ArgumentException("A terrain has 61 × 61 heights.", nameof(heights));
            if (roads == null)
                throw new ArgumentNullException(nameof(roads));
            settings = settings ?? new OmsiTerrainShapeSettings();
            var list = new List<OmsiShapedRoad>(roads);
            var step = tileSize / (OmsiTerrain.Resolution - 1);
            var shoulder = Math.Max(settings.ShoulderMeters, step * Math.Sqrt(2.0));
            var changed = 0;
            var near = new List<Place>();
            for (var row = 0; row < OmsiTerrain.Resolution; row++)
            {
                var y = tileY * tileSize + row * step;
                for (var column = 0; column < OmsiTerrain.Resolution; column++)
                {
                    var x = tileX * tileSize + column * step;
                    near.Clear();
                    foreach (var road in list)
                    {
                        var reach = road.HalfWidth + settings.MaximumReachMeters;
                        if (x < road.MinX - reach || x > road.MaxX + reach || y < road.MinY - reach || y > road.MaxY + reach)
                            continue;
                        if (Locate(road, x, y, settings, out var place))
                            near.Add(place);
                    }
                    if (near.Count == 0)
                        continue;
                    var index = row * OmsiTerrain.Resolution + column;
                    var height = (double)heights[index];
                    // Under a road: the height of the road it lies deepest inside.
                    Place under = null;
                    foreach (var place in near)
                        if (place.Outside <= 0.0 && (under == null || place.Outside < under.Outside))
                            under = place;
                    if (under != null)
                        height = under.EdgeHeight - settings.BelowEdgeMeters;
                    else
                        foreach (var place in near)
                        {
                            var level = place.EdgeHeight - settings.BelowEdgeMeters;
                            var spread = Math.Max(0.0, place.Outside - shoulder) / settings.SlopeRatio;
                            height = Math.Max(level - spread, Math.Min(level + spread, height));
                        }
                    if (Math.Abs(height - heights[index]) > 1e-4)
                    {
                        heights[index] = (float)height;
                        changed++;
                    }
                }
            }
            return changed;
        }

        /// <summary>Where a grid point lies relative to one road.</summary>
        private sealed class Place
        {
            /// <summary>Metres outside the road's edge; zero or less under the road.</summary>
            public double Outside;

            public double RoadHeight;
            public double EdgeHeight;
        }

        private static bool Locate(OmsiShapedRoad road, double x, double y, OmsiTerrainShapeSettings settings, out Place place)
        {
            place = null;
            if (!Nearest(road, x, y, out var along, out var lateral, out var beyondEnd))
                return false;
            var roadHeight = road.Spline.HeightAt(along);
            // Past the ends the road edge is the end itself, so the slopes round off around it.
            var edge = beyondEnd ? 0.0 : lateral >= 0 ? road.Right : -road.Left;
            var outside = Math.Abs(lateral) - edge;
            if (outside > settings.MaximumReachMeters)
                return false;
            place = new Place { Outside = outside, RoadHeight = roadHeight, EdgeHeight = roadHeight + road.EdgeHeight };
            return true;
        }

        /// <summary>
        /// The nearest point of the centre line: the distance along the spline, the signed distance to its right, and
        /// whether the nearest point is an end with the position beyond it.
        /// </summary>
        private static bool Nearest(OmsiShapedRoad road, double x, double y, out double along, out double lateral, out bool beyondEnd)
        {
            along = 0.0;
            lateral = 0.0;
            beyondEnd = false;
            var best = double.MaxValue;
            var points = road.Points;
            var segmentLength = road.Spline.LengthMeters / (points.Length - 1);
            for (var index = 0; index + 1 < points.Length; index++)
            {
                var ax = points[index].X;
                var ay = points[index].Y;
                var dx = points[index + 1].X - ax;
                var dy = points[index + 1].Y - ay;
                var lengthSquared = dx * dx + dy * dy;
                var t = lengthSquared < 1e-12 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((x - ax) * dx + (y - ay) * dy) / lengthSquared));
                var px = ax + dx * t;
                var py = ay + dy * t;
                var distance = (x - px) * (x - px) + (y - py) * (y - py);
                if (distance >= best)
                    continue;
                best = distance;
                along = (index + t) * segmentLength;
                // Right of the travel direction is positive.
                var length = Math.Sqrt(lengthSquared);
                var side = length < 1e-9 ? 0.0 : ((x - ax) * dy - (y - ay) * dx) / length;
                lateral = (side < 0.0 ? -1.0 : 1.0) * Math.Sqrt(distance);
                beyondEnd = index == 0 && t <= 0.0 && ((x - ax) * dx + (y - ay) * dy) < 0.0 ||
                            index + 2 == points.Length && t >= 1.0 && ((x - points[index + 1].X) * dx + (y - points[index + 1].Y) * dy) > 0.0;
            }
            return best < double.MaxValue;
        }
    }
}
