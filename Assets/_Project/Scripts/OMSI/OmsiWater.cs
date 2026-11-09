using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Where the water of a map is, from OpenStreetMap, as a raster over a box of the Earth: lakes, docks and riverbanks as
    /// areas, and the sea as the side of a coastline (OpenStreetMap keeps land on the left of a coastline's direction, so the sea
    /// lies on its right). A coastline needs no closed ring: each row of the raster takes its state from the nearest coastline
    /// crossing along the row, so pieces cut off by the box work. Cells that touch are one <em>body</em> of water.
    /// </summary>
    public sealed class OmsiWaterMask
    {
        /// <summary>A body of fewer cells than this is a puddle of the data and is left out.</summary>
        private const int MinimumCells = 6;

        private readonly double originX;
        private readonly double originY;
        private readonly double cell;
        private readonly int columns;
        private readonly int rows;
        private readonly int[] bodies;

        private OmsiWaterMask(double originX, double originY, double cell, int columns, int rows, int[] bodies, int bodyCount, double cellMetres)
        {
            this.originX = originX;
            this.originY = originY;
            this.cell = cell;
            this.columns = columns;
            this.rows = rows;
            this.bodies = bodies;
            BodyCount = bodyCount;
            CellMetres = cellMetres;
        }

        /// <summary>How many separate bodies of water the mask holds.</summary>
        public int BodyCount { get; }

        /// <summary>The ground edge of a raster cell in metres.</summary>
        public double CellMetres { get; }

        public int CellCount => bodies.Length;

        /// <summary>An element that is water as an area: a lake, a dock, a riverbank, a reservoir.</summary>
        public static bool IsWaterArea(OmsiOsmElement element)
        {
            var landuse = element.Tag("landuse");
            return (element.Way != null || element.Relation != null) &&
                   (element.Tag("natural") == "water" || element.Tag("waterway") == "riverbank" || landuse == "reservoir" || landuse == "basin");
        }

        public static bool IsCoastline(OmsiOsmElement element)
        {
            return element.Way != null && element.Tag("natural") == "coastline";
        }

        /// <summary>
        /// The water of OpenStreetMap's elements within a box (south, west, north, east in degrees) at cells of
        /// <paramref name="minimumCellMetres"/>, or coarser where the box would need more than <paramref name="maximumEdge"/> cells on a side.
        /// </summary>
        public static OmsiWaterMask Build(IEnumerable<OmsiOsmElement> elements, double south, double west, double north, double east,
            int maximumEdge = 2048, double minimumCellMetres = 2.0)
        {
            if (elements == null)
                throw new ArgumentNullException(nameof(elements));
            var low = OmsiGeoreference.MercatorOf(north, west);
            var high = OmsiGeoreference.MercatorOf(south, east);
            var width = high.X - low.X;
            var height = high.Y - low.Y;
            if (!(width > 0.0) || !(height > 0.0))
                throw new ArgumentException("The box is empty.");
            var metresPerUnit = 40075016.686 * Math.Cos((south + north) * 0.5 * Math.PI / 180.0);
            var cellMetres = Math.Max(minimumCellMetres, Math.Max(width, height) * metresPerUnit / maximumEdge);
            var cell = cellMetres / metresPerUnit;
            var columns = Math.Max(1, (int)Math.Ceiling(width / cell));
            var rows = Math.Max(1, (int)Math.Ceiling(height / cell));
            var water = new bool[columns * rows];

            OmsiMapPoint ToGrid(OmsiLatLon at)
            {
                var mercator = OmsiGeoreference.MercatorOf(at.Latitude, at.Longitude);
                return new OmsiMapPoint((mercator.X - low.X) / cell, (mercator.Y - low.Y) / cell);
            }
            List<OmsiMapPoint> Ring(List<OmsiLatLon> points)
            {
                var ring = new List<OmsiMapPoint>(points.Count);
                foreach (var point in points)
                    ring.Add(ToGrid(point));
                return ring;
            }

            var coastRows = new List<KeyValuePair<double, bool>>[rows];
            foreach (var element in elements)
            {
                if (IsCoastline(element))
                {
                    AddCoastline(coastRows, rows, Ring(element.Way));
                    continue;
                }
                if (!IsWaterArea(element))
                    continue;
                var rings = new List<List<OmsiMapPoint>>();
                if (element.Way != null)
                {
                    // A way that is not closed is no area.
                    if (element.Way.Count > 3 && element.Way[0].Latitude == element.Way[element.Way.Count - 1].Latitude &&
                        element.Way[0].Longitude == element.Way[element.Way.Count - 1].Longitude)
                        rings.Add(Ring(element.Way));
                }
                else
                {
                    var pieces = new List<List<OmsiLatLon>>();
                    foreach (var member in element.Relation)
                        pieces.Add(member.Value);
                    foreach (var ring in OmsiOsmMap.Rings(pieces))
                        rings.Add(Ring(ring));
                }
                if (rings.Count > 0)
                    FillRings(water, columns, rows, rings);
            }
            FillCoast(water, columns, coastRows);
            return Label(water, columns, rows, low.X, low.Y, cell, cellMetres);
        }

        /// <summary>The crossings of a coastline with the rows of the raster: where, and whether the sea lies east of it (it heads north).</summary>
        private static void AddCoastline(List<KeyValuePair<double, bool>>[] events, int rows, List<OmsiMapPoint> line)
        {
            for (var index = 0; index + 1 < line.Count; index++)
            {
                var a = line[index];
                var b = line[index + 1];
                if (a.Y == b.Y)
                    continue;
                // Mercator's y points south: a segment that heads north has y falling, and the sea is on its right, to the east.
                var seaEast = b.Y < a.Y;
                var from = Math.Max(0, (int)Math.Ceiling(Math.Min(a.Y, b.Y) - 0.5));
                var to = Math.Min(rows - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) - 0.5) - 1);
                for (var row = from; row <= to; row++)
                {
                    var x = a.X + (row + 0.5 - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    (events[row] ?? (events[row] = new List<KeyValuePair<double, bool>>())).Add(new KeyValuePair<double, bool>(x, seaEast));
                }
            }
        }

        /// <summary>Marks the cells inside some rings, a ring inside another counting as a hole (even and odd crossings).</summary>
        private static void FillRings(bool[] water, int columns, int rows, List<List<OmsiMapPoint>> rings)
        {
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var ring in rings)
                foreach (var point in ring)
                {
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }
            var first = Math.Max(0, (int)Math.Ceiling(minY - 0.5));
            var last = Math.Min(rows - 1, (int)Math.Floor(maxY - 0.5));
            if (last < first)
                return;
            var crossings = new List<double>[last - first + 1];
            foreach (var ring in rings)
                for (var index = 0; index + 1 < ring.Count; index++)
                {
                    var a = ring[index];
                    var b = ring[index + 1];
                    if (a.Y == b.Y)
                        continue;
                    var from = Math.Max(first, (int)Math.Ceiling(Math.Min(a.Y, b.Y) - 0.5));
                    var to = Math.Min(last, (int)Math.Ceiling(Math.Max(a.Y, b.Y) - 0.5) - 1);
                    for (var row = from; row <= to; row++)
                        (crossings[row - first] ?? (crossings[row - first] = new List<double>())).Add(a.X + (row + 0.5 - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
            for (var row = first; row <= last; row++)
            {
                var list = crossings[row - first];
                if (list == null)
                    continue;
                list.Sort();
                for (var pair = 0; pair + 1 < list.Count; pair += 2)
                {
                    var from = Math.Max(0, (int)Math.Ceiling(list[pair] - 0.5));
                    var to = Math.Min(columns - 1, (int)Math.Ceiling(list[pair + 1] - 0.5) - 1);
                    for (var column = from; column <= to; column++)
                        water[row * columns + column] = true;
                }
            }
        }

        /// <summary>
        /// The sea of each row that a coastline crosses: east of a crossing that heads north, west of one that heads south, up to
        /// the middle between it and the next crossing, so each part of a row takes its state from the nearer crossing.
        /// </summary>
        private static void FillCoast(bool[] water, int columns, List<KeyValuePair<double, bool>>[] events)
        {
            for (var row = 0; row < events.Length; row++)
            {
                var list = events[row];
                if (list == null)
                    continue;
                list.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (var index = 0; index < list.Count; index++)
                {
                    var at = list[index].Key;
                    if (list[index].Value)
                        MarkRow(water, columns, row, at, index == list.Count - 1 ? double.PositiveInfinity : (at + list[index + 1].Key) * 0.5);
                    else
                        MarkRow(water, columns, row, index == 0 ? double.NegativeInfinity : (list[index - 1].Key + at) * 0.5, at);
                }
            }
        }

        /// <summary>Marks the cells of a row whose middles lie from <paramref name="from"/> up to but not including <paramref name="to"/>.</summary>
        private static void MarkRow(bool[] water, int columns, int row, double from, double to)
        {
            var first = double.IsNegativeInfinity(from) ? 0 : Math.Max(0, (int)Math.Ceiling(from - 0.5));
            var last = double.IsPositiveInfinity(to) ? columns - 1 : Math.Min(columns - 1, (int)Math.Ceiling(to - 0.5) - 1);
            for (var column = first; column <= last; column++)
                water[row * columns + column] = true;
        }

        /// <summary>Names the bodies: cells that share an edge are one, and a body of a few cells is dropped.</summary>
        private static OmsiWaterMask Label(bool[] water, int columns, int rows, double originX, double originY, double cell, double cellMetres)
        {
            var bodies = new int[water.Length];
            for (var index = 0; index < bodies.Length; index++)
                bodies[index] = -1;
            var count = 0;
            var stack = new List<int>();
            var members = new List<int>();
            for (var start = 0; start < water.Length; start++)
            {
                if (!water[start] || bodies[start] != -1)
                    continue;
                members.Clear();
                stack.Clear();
                stack.Add(start);
                bodies[start] = int.MaxValue;
                while (stack.Count > 0)
                {
                    var current = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    members.Add(current);
                    var column = current % columns;
                    var row = current / columns;
                    void Visit(int next)
                    {
                        if (water[next] && bodies[next] == -1)
                        {
                            bodies[next] = int.MaxValue;
                            stack.Add(next);
                        }
                    }
                    if (column > 0)
                        Visit(current - 1);
                    if (column < columns - 1)
                        Visit(current + 1);
                    if (row > 0)
                        Visit(current - columns);
                    if (row < rows - 1)
                        Visit(current + columns);
                }
                var id = members.Count >= MinimumCells ? count++ : -2;
                foreach (var member in members)
                    bodies[member] = id;
            }
            for (var index = 0; index < bodies.Length; index++)
                if (bodies[index] == -2)
                    bodies[index] = -1;
            return new OmsiWaterMask(originX, originY, cell, columns, rows, bodies, count, cellMetres);
        }

        /// <summary>The body of water at a Web Mercator position, or -1 for land and for a place outside the box.</summary>
        public int BodyAt(double mercatorX, double mercatorY)
        {
            var column = (int)Math.Floor((mercatorX - originX) / cell);
            var row = (int)Math.Floor((mercatorY - originY) / cell);
            if (column < 0 || row < 0 || column >= columns || row >= rows)
                return -1;
            return bodies[row * columns + column];
        }

        /// <summary>
        /// Walks the cells whose middles lie in a Web Mercator box: <paramref name="visit"/> gets the cell's number, the body for a
        /// cell of water, or for a land cell beside water the body it borders with <c>shore</c> set, and the cell's middle.
        /// </summary>
        public void ForCellsIn(OmsiMapPoint low, OmsiMapPoint high, Action<int, int, bool, OmsiMapPoint> visit)
        {
            var firstColumn = Math.Max(0, (int)Math.Ceiling((low.X - originX) / cell - 0.5));
            var lastColumn = Math.Min(columns - 1, (int)Math.Ceiling((high.X - originX) / cell - 0.5) - 1);
            var firstRow = Math.Max(0, (int)Math.Ceiling((low.Y - originY) / cell - 0.5));
            var lastRow = Math.Min(rows - 1, (int)Math.Ceiling((high.Y - originY) / cell - 0.5) - 1);
            for (var row = firstRow; row <= lastRow; row++)
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var index = row * columns + column;
                    var body = bodies[index];
                    var shore = false;
                    if (body < 0)
                    {
                        body = Neighbour(column - 1, row);
                        if (body < 0)
                            body = Neighbour(column + 1, row);
                        if (body < 0)
                            body = Neighbour(column, row - 1);
                        if (body < 0)
                            body = Neighbour(column, row + 1);
                        if (body < 0)
                            continue;
                        shore = true;
                    }
                    visit(index, body, shore, new OmsiMapPoint(originX + (column + 0.5) * cell, originY + (row + 0.5) * cell));
                }
        }

        /// <summary>Whether any cell of water has its middle in a Web Mercator box.</summary>
        public bool AnyWaterIn(OmsiMapPoint low, OmsiMapPoint high)
        {
            var firstColumn = Math.Max(0, (int)Math.Ceiling((low.X - originX) / cell - 0.5));
            var lastColumn = Math.Min(columns - 1, (int)Math.Ceiling((high.X - originX) / cell - 0.5) - 1);
            var firstRow = Math.Max(0, (int)Math.Ceiling((low.Y - originY) / cell - 0.5));
            var lastRow = Math.Min(rows - 1, (int)Math.Ceiling((high.Y - originY) / cell - 0.5) - 1);
            for (var row = firstRow; row <= lastRow; row++)
                for (var column = firstColumn; column <= lastColumn; column++)
                    if (bodies[row * columns + column] >= 0)
                        return true;
            return false;
        }

        private int Neighbour(int column, int row)
        {
            return column < 0 || row < 0 || column >= columns || row >= rows ? -1 : bodies[row * columns + column];
        }
    }

    /// <summary>
    /// The height of the water surface of each body of a <see cref="OmsiWaterMask"/>, from the heights of the world around it. The
    /// world's elevation data is often wrong over water (flat blocks tens of metres too high, water-filling artefacts of the
    /// source), so the surface is taken from the edge: no higher than the lower tenth of the heights just on the land side
    /// (beaches, slipways), and no higher than the lower quarter of what the data says inside. One level a body, the same for every
    /// tile, so tiles side by side meet.
    /// </summary>
    public sealed class OmsiWaterLevels
    {
        /// <summary>How far under the surface the bed of a body lies in metres, as the terrain tool digs a lake.</summary>
        public const double BedDepth = 1.5;

        private const double ShorePercentile = 0.10;
        private const double InsidePercentile = 0.25;

        private readonly OmsiWaterMask mask;
        private readonly Samples[] shore;
        private readonly Samples[] inside;
        private readonly bool[] seenInside;
        private readonly bool[] seenShore;
        private double?[] surfaces;

        public OmsiWaterLevels(OmsiWaterMask mask)
        {
            this.mask = mask ?? throw new ArgumentNullException(nameof(mask));
            shore = new Samples[mask.BodyCount];
            inside = new Samples[mask.BodyCount];
            for (var body = 0; body < mask.BodyCount; body++)
            {
                shore[body] = new Samples();
                inside[body] = new Samples();
            }
            seenInside = new bool[mask.CellCount];
            seenShore = new bool[mask.CellCount];
        }

        /// <summary>Reads the heights of the water and its shore inside a Web Mercator box; every cell is read once however many boxes hold it.</summary>
        public void Add(OmsiMapPoint low, OmsiMapPoint high, Func<OmsiMapPoint, double> height)
        {
            if (surfaces != null)
                throw new InvalidOperationException("The levels are decided.");
            mask.ForCellsIn(low, high, (index, body, isShore, middle) =>
            {
                var seen = isShore ? seenShore : seenInside;
                if (seen[index])
                    return;
                seen[index] = true;
                (isShore ? shore : inside)[body].Add((float)height(middle));
            });
        }

        /// <summary>Decides the surface of every body that has any heights.</summary>
        public void Finish()
        {
            surfaces = new double?[mask.BodyCount];
            for (var body = 0; body < surfaces.Length; body++)
            {
                var edge = shore[body].Percentile(ShorePercentile);
                var within = inside[body].Percentile(InsidePercentile);
                if (edge.HasValue && within.HasValue)
                    surfaces[body] = Math.Min(edge.Value, within.Value);
                else
                    surfaces[body] = edge ?? within;
            }
        }

        /// <summary>The height of a body's water surface, or null when no heights were read for it.</summary>
        public double? Surface(int body)
        {
            if (surfaces == null)
                throw new InvalidOperationException("The levels are not decided yet.");
            return body >= 0 && body < surfaces.Length ? surfaces[body] : null;
        }

        /// <summary>The height the ground of a body of water is laid to: <see cref="BedDepth"/> under its surface.</summary>
        public double? Bed(int body)
        {
            var surface = Surface(body);
            return surface.HasValue ? surface.Value - BedDepth : (double?)null;
        }

        /// <summary>The surfaces of the bodies that have one, in metres.</summary>
        public List<double> Decided()
        {
            var list = new List<double>();
            for (var body = 0; body < surfaces.Length; body++)
                if (surfaces[body].HasValue)
                    list.Add(surfaces[body].Value);
            return list;
        }

        /// <summary>Some thousands of heights at most, thinned evenly (every second one is dropped when it grows full).</summary>
        private sealed class Samples
        {
            private const int Capacity = 4096;
            private readonly List<float> values = new List<float>();
            private int stride = 1;
            private int counter;

            public void Add(float value)
            {
                if (counter++ % stride != 0)
                    return;
                values.Add(value);
                if (values.Count < Capacity)
                    return;
                var kept = new List<float>(Capacity / 2);
                for (var index = 0; index < values.Count; index += 2)
                    kept.Add(values[index]);
                values.Clear();
                values.AddRange(kept);
                stride *= 2;
            }

            public double? Percentile(double fraction)
            {
                if (values.Count == 0)
                    return null;
                var sorted = values.ToArray();
                Array.Sort(sorted);
                return sorted[(int)Math.Floor((sorted.Length - 1) * fraction)];
            }
        }
    }
}
