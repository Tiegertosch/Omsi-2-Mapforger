using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One thing OpenStreetMap has: its tags and a point, a way, or a relation's ways (true for outer).</summary>
    public sealed class OmsiOsmElement
    {
        public Dictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public OmsiLatLon? Node { get; set; }
        public List<OmsiLatLon> Way { get; set; }
        public List<KeyValuePair<bool, List<OmsiLatLon>>> Relation { get; set; }

        public string Tag(string key)
        {
            return Tags.TryGetValue(key, out var value) ? value : string.Empty;
        }
    }

    /// <summary>
    /// The map as OpenStreetMap draws it, made from its data as openMapper makes it (`src/osm_map.rs`): streets with their
    /// names, houses, water, green, rails and the stops with a sign, drawn sharp for each tile and laid on it like a picture
    /// from the air. Nothing is built from it.
    /// </summary>
    public static class OmsiOsmMap
    {
        public const string SourceName = "OpenStreetMap map (vector)";
        public const string Attribution = "© OpenStreetMap contributors, ODbL";

        /// <summary>The Overpass query for everything the map shows within a box (south, west, north, east).</summary>
        public static string Query(double south, double west, double north, double east)
        {
            var b = string.Join(",", new[] { south, west, north, east }.Select6());
            return "[out:json][timeout:90];(" +
                   "way[\"highway\"](" + b + ");" +
                   "way[\"railway\"~\"^(rail|tram|light_rail)$\"](" + b + ");" +
                   "way[\"building\"](" + b + ");" +
                   "way[\"natural\"~\"^(water|wood|scrub|grassland|wetland|heath)$\"](" + b + ");" +
                   "way[\"waterway\"~\"^(river|stream|canal|riverbank|drain|ditch)$\"](" + b + ");" +
                   "way[\"landuse\"~\"^(forest|residential|commercial|retail|industrial|grass|meadow|village_green|recreation_ground|cemetery|orchard|allotments|farmland|farmyard|reservoir|basin)$\"](" + b + ");" +
                   "way[\"leisure\"~\"^(park|garden|pitch|playground|sports_centre|recreation_ground|nature_reserve)$\"](" + b + ");" +
                   "way[\"amenity\"=\"parking\"](" + b + ");" +
                   "rel[\"natural\"~\"^(water|wood)$\"](" + b + ");" +
                   "rel[\"landuse\"~\"^(forest|residential|grass|meadow)$\"](" + b + ");" +
                   "rel[\"leisure\"~\"^(park|garden)$\"](" + b + ");" +
                   "rel[\"building\"](" + b + ");" +
                   "rel[\"waterway\"=\"riverbank\"](" + b + ");" +
                   "node[\"highway\"=\"bus_stop\"](" + b + ");" +
                   "node[\"public_transport\"=\"platform\"][\"bus\"=\"yes\"](" + b + ");" +
                   "node[\"railway\"=\"tram_stop\"](" + b + ");" +
                   ");out geom;";
        }

        private static IEnumerable<string> Select6(this double[] values)
        {
            foreach (var value in values)
                yield return value.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        /// <exception cref="InvalidDataException">The answer is not Overpass's JSON.</exception>
        public static List<OmsiOsmElement> Parse(string json)
        {
            var elements = new List<OmsiOsmElement>();
            if (!(OmsiJson.Parse(json) is Dictionary<string, object> root) || !(root.TryGetValue("elements", out var list) && list is List<object> items))
                return elements;
            foreach (var item in items)
            {
                if (!(item is Dictionary<string, object> raw))
                    continue;
                var element = new OmsiOsmElement();
                if (raw.TryGetValue("tags", out var tags) && tags is Dictionary<string, object> tagMap)
                    foreach (var pair in tagMap)
                        if (pair.Value is string text)
                            element.Tags[pair.Key] = text;
                var type = raw.TryGetValue("type", out var kind) ? kind as string : null;
                switch (type)
                {
                    case "node":
                        if (raw.TryGetValue("lat", out var lat) && lat is double latitude && raw.TryGetValue("lon", out var lon) && lon is double longitude)
                            element.Node = new OmsiLatLon(latitude, longitude);
                        else
                            continue;
                        break;
                    case "way":
                        var way = Points(raw.TryGetValue("geometry", out var geometry) ? geometry : null);
                        if (way.Count < 2)
                            continue;
                        element.Way = way;
                        break;
                    case "relation":
                        var members = new List<KeyValuePair<bool, List<OmsiLatLon>>>();
                        if (raw.TryGetValue("members", out var memberList) && memberList is List<object> memberItems)
                            foreach (var member in memberItems)
                            {
                                if (!(member is Dictionary<string, object> m) || !(m.TryGetValue("type", out var memberType) && (memberType as string) == "way"))
                                    continue;
                                var points = Points(m.TryGetValue("geometry", out var memberGeometry) ? memberGeometry : null);
                                if (points.Count >= 2)
                                    members.Add(new KeyValuePair<bool, List<OmsiLatLon>>(!(m.TryGetValue("role", out var role) && (role as string) == "inner"), points));
                            }
                        if (members.Count == 0)
                            continue;
                        element.Relation = members;
                        break;
                    default:
                        continue;
                }
                elements.Add(element);
            }
            return elements;
        }

        private static List<OmsiLatLon> Points(object geometry)
        {
            var points = new List<OmsiLatLon>();
            if (!(geometry is List<object> list))
                return points;
            foreach (var item in list)
                if (item is Dictionary<string, object> point && point.TryGetValue("lat", out var lat) && lat is double latitude && point.TryGetValue("lon", out var lon) &&
                    lon is double longitude)
                    points.Add(new OmsiLatLon(latitude, longitude));
            return points;
        }

        /// <summary>Open pieces joined end to end into closed rings: a multipolygon's outline comes in pieces.</summary>
        public static List<List<OmsiLatLon>> Rings(List<List<OmsiLatLon>> pieces)
        {
            var rings = new List<List<OmsiLatLon>>();
            var left = new List<List<OmsiLatLon>>(pieces);
            while (left.Count > 0)
            {
                var current = new List<OmsiLatLon>(left[left.Count - 1]);
                left.RemoveAt(left.Count - 1);
                while (!(current.Count > 3 && Same(current[0], current[current.Count - 1])))
                {
                    var end = current[current.Count - 1];
                    var index = left.FindIndex(piece => Same(piece[0], end) || Same(piece[piece.Count - 1], end));
                    if (index < 0)
                        break;
                    var nextPiece = new List<OmsiLatLon>(left[index]);
                    left.RemoveAt(index);
                    if (!Same(nextPiece[0], end))
                        nextPiece.Reverse();
                    current.AddRange(nextPiece.GetRange(1, nextPiece.Count - 1));
                }
                if (current.Count > 3 && Same(current[0], current[current.Count - 1]))
                    rings.Add(current);
            }
            return rings;
        }

        private static bool Same(OmsiLatLon a, OmsiLatLon b)
        {
            return a.Latitude == b.Latitude && a.Longitude == b.Longitude;
        }

        /// <summary>How an area is coloured and the order it is drawn in, low first; null when it is not drawn.</summary>
        private static bool AreaStyle(OmsiOsmElement e, out int order, out uint fill, out uint? edge)
        {
            string landuse = e.Tag("landuse"), natural = e.Tag("natural"), leisure = e.Tag("leisure");
            edge = null;
            order = 0;
            fill = 0;
            bool Is(string value, params string[] options)
            {
                return Array.IndexOf(options, value) >= 0;
            }
            if (natural == "water" || e.Tag("waterway") == "riverbank" || Is(landuse, "reservoir", "basin"))
            {
                order = 4;
                fill = 0xaad3df;
            }
            else if (e.Tags.ContainsKey("building"))
            {
                order = 5;
                fill = 0xd9d0c9;
                edge = 0xbfb2a8;
            }
            else if (landuse == "forest" || natural == "wood")
            {
                order = 2;
                fill = 0xadd19e;
            }
            else if (Is(landuse, "grass", "meadow", "village_green", "recreation_ground", "cemetery", "orchard", "allotments") ||
                     Is(leisure, "park", "garden", "recreation_ground", "nature_reserve", "playground") || Is(natural, "scrub", "grassland", "heath", "wetland"))
            {
                order = 1;
                fill = 0xcdebb0;
            }
            else if (Is(leisure, "pitch", "sports_centre"))
            {
                order = 3;
                fill = 0xaae0cb;
                edge = 0x8ccfb3;
            }
            else if (e.Tag("amenity") == "parking")
            {
                order = 3;
                fill = 0xeeeeee;
                edge = 0xd6d6d6;
            }
            else if (landuse == "residential")
                fill = 0xe0dfdf;
            else if (Is(landuse, "commercial", "retail"))
                fill = 0xf2dad9;
            else if (landuse == "industrial")
                fill = 0xebdbe8;
            else if (Is(landuse, "farmland", "farmyard"))
                fill = 0xeef0d5;
            else
                return false;
            return true;
        }

        /// <summary>A road class: rank, fill, casing, width and the size of its name in metres; false when not drawn.</summary>
        private static bool RoadStyle(string highway, out int rank, out uint fill, out uint? casing, out double width, out double nameSize)
        {
            casing = null;
            switch (highway)
            {
                case "motorway": case "motorway_link": case "trunk": case "trunk_link":
                    rank = 9; fill = 0xe990a0; casing = 0xdc2a67; width = 12.0; nameSize = 3.4; return true;
                case "primary": case "primary_link":
                    rank = 8; fill = 0xfcd6a4; casing = 0xc09a5a; width = 10.0; nameSize = 3.2; return true;
                case "secondary": case "secondary_link":
                    rank = 7; fill = 0xf7fabf; casing = 0xb3b36f; width = 9.0; nameSize = 3.0; return true;
                case "tertiary": case "tertiary_link":
                    rank = 6; fill = 0xffffff; casing = 0x9d9d9d; width = 8.0; nameSize = 2.9; return true;
                case "residential": case "unclassified": case "living_street":
                    rank = 5; fill = 0xffffff; casing = 0xb5b5b5; width = 6.0; nameSize = 2.6; return true;
                case "service":
                    rank = 4; fill = 0xffffff; casing = 0xbdbdbd; width = 3.2; nameSize = 0.0; return true;
                case "pedestrian":
                    rank = 3; fill = 0xdddde8; casing = 0x9a9aa8; width = 4.5; nameSize = 2.4; return true;
                default:
                    rank = 0; fill = 0; width = 0; nameSize = 0; return false;
            }
        }

        /// <summary>A footpath, a track or a cycleway: thin and dashed.</summary>
        private static uint? PathStyle(string highway)
        {
            switch (highway)
            {
                case "footway": case "path": case "steps": case "bridleway": return 0xf8746b;
                case "cycleway": return 0x3e63e6;
                case "track": return 0x9c7a3c;
                default: return null;
            }
        }

        private sealed class Label
        {
            public List<IList<OmsiMapPoint>> Rings;
            public double Pixels;
            public uint Ink;
        }

        /// <summary>
        /// The map drawn, some pixels a side: a place's pixel from <paramref name="toPixel"/>, <paramref name="pixelsPerMetre"/>
        /// pixels to a metre. Names need a font; without one the map has none.
        /// </summary>
        public static OmsiRgbImage Draw(List<OmsiOsmElement> data, int size, Func<OmsiLatLon, OmsiMapPoint> toPixel, double pixelsPerMetre,
            OmsiTrueTypeFont font, CancellationToken cancel)
        {
            var ppm = pixelsPerMetre;
            var canvas = new OmsiCanvas(size, size, 0xf2efe9);
            List<OmsiMapPoint> Pixels(List<OmsiLatLon> points)
            {
                var result = new List<OmsiMapPoint>(points.Count);
                foreach (var point in points)
                    result.Add(toPixel(point));
                return result;
            }

            // Areas: land, green, water, houses.
            var areas = new List<KeyValuePair<int, KeyValuePair<KeyValuePair<uint, uint?>, List<IList<OmsiMapPoint>>>>>();
            foreach (var e in data)
            {
                if (!AreaStyle(e, out var order, out var fill, out var edge) || e.Tag("highway").Length > 0)
                    continue;
                List<List<OmsiLatLon>> rings;
                if (e.Way != null && e.Way.Count > 3 && Same(e.Way[0], e.Way[e.Way.Count - 1]))
                    rings = new List<List<OmsiLatLon>> { e.Way };
                else if (e.Relation != null)
                {
                    var pieces = new List<List<OmsiLatLon>>();
                    foreach (var member in e.Relation)
                        pieces.Add(member.Value);
                    rings = Rings(pieces);
                }
                else
                    continue;
                var shape = new List<IList<OmsiMapPoint>>();
                foreach (var ring in rings)
                    shape.Add(Pixels(ring));
                if (shape.Count > 0)
                    areas.Add(new KeyValuePair<int, KeyValuePair<KeyValuePair<uint, uint?>, List<IList<OmsiMapPoint>>>>(order,
                        new KeyValuePair<KeyValuePair<uint, uint?>, List<IList<OmsiMapPoint>>>(new KeyValuePair<uint, uint?>(fill, edge), shape)));
            }
            StableSort(areas, (a, b) => a.Key.CompareTo(b.Key));
            foreach (var area in areas)
            {
                canvas.Fill(area.Value.Value, area.Value.Key.Key, true);
                if (area.Value.Key.Value.HasValue)
                    foreach (var ring in area.Value.Value)
                        canvas.Stroke(ring, Math.Max(1.0, 0.3 * ppm), area.Value.Key.Value.Value, false, true);
            }
            cancel.ThrowIfCancellationRequested();

            // Waterways.
            foreach (var e in data)
            {
                if (e.Way == null)
                    continue;
                double width;
                switch (e.Tag("waterway"))
                {
                    case "river": width = 6.0; break;
                    case "canal": width = 5.0; break;
                    case "stream": case "drain": case "ditch": width = 1.6; break;
                    default: continue;
                }
                canvas.Stroke(Pixels(e.Way), width * ppm, 0xaad3df, true);
            }

            // Railways.
            foreach (var e in data)
            {
                if (e.Way == null || e.Tag("railway").Length == 0 || e.Tag("tunnel") == "yes")
                    continue;
                var line = Pixels(e.Way);
                var tram = e.Tag("railway") != "rail";
                canvas.Stroke(line, (tram ? 1.0 : 1.7) * ppm, tram ? 0x999999u : 0x808080u, false);
                if (!tram)
                    canvas.Stroke(line, 0.8 * ppm, 0xffffff, false, false, new[] { 4.0 * ppm, 4.0 * ppm });
            }

            // Footpaths, dashed.
            foreach (var e in data)
            {
                if (e.Way == null)
                    continue;
                var colour = PathStyle(e.Tag("highway"));
                if (colour.HasValue)
                    canvas.Stroke(Pixels(e.Way), Math.Max(1.5, 0.8 * ppm), colour.Value, false, false, new[] { 1.6 * ppm, 1.0 * ppm });
            }

            // Roads, the minor ones first, each class its casing and then its fill, so a bridge lies over a road.
            var roads = new List<KeyValuePair<int, OmsiOsmElement>>();
            foreach (var e in data)
            {
                if (e.Way == null || !RoadStyle(e.Tag("highway"), out var rank, out _, out _, out _, out _) || e.Tag("tunnel") == "yes" || e.Tag("area") == "yes")
                    continue;
                var layer = int.TryParse(e.Tag("layer"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var given) ? given : e.Tag("bridge") == "yes" ? 1 : 0;
                roads.Add(new KeyValuePair<int, OmsiOsmElement>(layer * 16 + rank, e));
            }
            StableSort(roads, (a, b) => a.Key.CompareTo(b.Key));
            for (var first = 0; first < roads.Count;)
            {
                var last = first;
                while (last < roads.Count && roads[last].Key == roads[first].Key)
                    last++;
                for (var pass = 0; pass < 2; pass++)
                    for (var index = first; index < last; index++)
                    {
                        var e = roads[index].Value;
                        RoadStyle(e.Tag("highway"), out _, out var fill, out var casing, out var width, out _);
                        var line = Pixels(e.Way);
                        if (pass == 0 && casing.HasValue)
                            canvas.Stroke(line, width * ppm + Math.Max(2.0, 1.2 * ppm), casing.Value, true);
                        else if (pass == 1)
                            canvas.Stroke(line, width * ppm, fill, true);
                    }
                first = last;
            }
            cancel.ThrowIfCancellationRequested();

            // Labels: the stops first, then the streets round them; no label lies on another.
            var limit = (double)size;
            var taken = new List<double[]>();
            var names = new List<KeyValuePair<string, OmsiMapPoint>>();
            bool Free(double[] box)
            {
                foreach (var other in taken)
                    if (!(box[2] < other[0] || box[0] > other[2] || box[3] < other[1] || box[1] > other[3]))
                        return false;
                return true;
            }
            var labels = new List<Label>();
            var signs = new List<KeyValuePair<OmsiMapPoint, bool>>();
            var radius = 1.9 * ppm;
            foreach (var e in data)
            {
                if (!e.Node.HasValue)
                    continue;
                var tram = e.Tag("railway") == "tram_stop";
                var at = toPixel(e.Node.Value);
                if (at.X < -radius || at.Y < -radius || at.X > limit + radius || at.Y > limit + radius)
                    continue;
                signs.Add(new KeyValuePair<OmsiMapPoint, bool>(at, tram));
                taken.Add(new[] { at.X - radius, at.Y - radius, at.X + radius, at.Y + radius });
                var name = e.Tag("name");
                if (font == null || name.Length == 0)
                    continue;
                var pixels = 2.5 * ppm;
                var outline = font.TextOutline(name, pixels, out var width);
                double x = at.X + radius * 1.3, y = at.Y + pixels * 0.35;
                var box = new[] { x, y - pixels * 0.8, x + width, y + pixels * 0.2 };
                if (box[0] >= 0 && box[2] <= limit && box[1] >= 0 && box[3] <= limit && Free(box))
                {
                    taken.Add(box);
                    labels.Add(new Label { Rings = Moved(outline, x, y, 0.0, 0.0, 0.0), Pixels = pixels, Ink = 0x0b4a9a });
                }
            }
            if (font != null)
            {
                var named = new List<KeyValuePair<int, OmsiOsmElement>>();
                foreach (var e in data)
                    if (e.Way != null && RoadStyle(e.Tag("highway"), out var rank, out _, out _, out _, out var nameSize) && nameSize > 0.0 && e.Tag("name").Length > 0 &&
                        e.Tag("tunnel") != "yes")
                        named.Add(new KeyValuePair<int, OmsiOsmElement>(rank, e));
                // The big roads get their names first.
                StableSort(named, (a, b) => b.Key.CompareTo(a.Key));
                foreach (var pair in named)
                {
                    var e = pair.Value;
                    RoadStyle(e.Tag("highway"), out _, out _, out _, out _, out var nameMetres);
                    var points = Pixels(e.Way);
                    var pixels = nameMetres * ppm;
                    var outline = font.TextOutline(e.Tag("name"), pixels, out var textWidth);
                    var start = 0;
                    var placed = 0;
                    while (start + 1 < points.Count && placed < 3)
                    {
                        // A stretch of the road as long as the name, going straight on.
                        var length = 0.0;
                        var end = start;
                        while (end + 1 < points.Count && length < textWidth * 1.15)
                        {
                            length += Distance(points[end + 1], points[end]);
                            end++;
                        }
                        if (length < textWidth * 1.15)
                            break;
                        var a = points[start];
                        var b = points[end];
                        var straight = Distance(a, b) > length * 0.96;
                        var middle = new OmsiMapPoint((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                        var margin = textWidth * 0.6 + pixels;
                        var inside = middle.X > margin && middle.Y > margin && middle.X < limit - margin && middle.Y < limit - margin;
                        if (straight && inside)
                        {
                            var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                            if (angle > Math.PI / 2)
                                angle -= Math.PI;
                            else if (angle < -Math.PI / 2)
                                angle += Math.PI;
                            double halfWidth = textWidth * 0.5 + 2.0, halfHeight = pixels * 0.5 + 2.0;
                            var extentX = halfWidth * Math.Abs(Math.Cos(angle)) + halfHeight * Math.Abs(Math.Sin(angle));
                            var extentY = halfWidth * Math.Abs(Math.Sin(angle)) + halfHeight * Math.Abs(Math.Cos(angle));
                            var box = new[] { middle.X - extentX, middle.Y - extentY, middle.X + extentX, middle.Y + extentY };
                            var name = e.Tag("name");
                            var near = names.Exists(other => other.Key == name && Distance(other.Value, middle) < 60.0 * ppm);
                            if (Free(box) && !near)
                            {
                                taken.Add(box);
                                names.Add(new KeyValuePair<string, OmsiMapPoint>(name, middle));
                                labels.Add(new Label { Rings = Moved(outline, middle.X, middle.Y, angle, -textWidth * 0.5, pixels * 0.35), Pixels = pixels, Ink = 0x303030 });
                                placed++;
                                // The next one a few names further on.
                                var skip = textWidth * 3.0;
                                var run = 0.0;
                                while (end + 1 < points.Count && run < skip)
                                {
                                    run += Distance(points[end + 1], points[end]);
                                    end++;
                                }
                                start = end;
                                continue;
                            }
                        }
                        start++;
                    }
                }
            }

            // The stops' signs, then the names over everything with a white halo.
            foreach (var sign in signs)
            {
                var at = sign.Key;
                var colour = sign.Value ? 0xd0021bu : 0x1d6fc4u;
                canvas.FillCircle(at.X, at.Y, radius, 0xffffff);
                canvas.FillCircle(at.X, at.Y, radius * 0.84, colour);
                // A bus or a tram seen from the front: a white body with a coloured windscreen.
                canvas.FillRect(at.X - 0.46 * radius, at.Y - 0.5 * radius, 0.92 * radius, 0.95 * radius, 0xffffff);
                canvas.FillRect(at.X - 0.34 * radius, at.Y - 0.38 * radius, 0.68 * radius, 0.36 * radius, colour);
            }
            foreach (var label in labels)
            {
                var halo = new List<IList<OmsiMapPoint>>();
                foreach (var ring in label.Rings)
                    halo.AddRange(OmsiCanvas.StrokeOutline(ring, label.Pixels * 0.28, true, true));
                canvas.Fill(halo, 0xffffff, false);
            }
            foreach (var label in labels)
                canvas.Fill(label.Rings, label.Ink, false);
            return canvas.ToImage();
        }

        /// <summary>Text rings moved by (dx, dy) in their own frame, turned by an angle and laid at a point.</summary>
        private static List<IList<OmsiMapPoint>> Moved(List<IList<OmsiMapPoint>> rings, double x, double y, double angle, double dx, double dy)
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var result = new List<IList<OmsiMapPoint>>(rings.Count);
            foreach (var ring in rings)
            {
                var moved = new List<OmsiMapPoint>(ring.Count);
                foreach (var point in ring)
                {
                    var px = point.X + dx;
                    var py = point.Y + dy;
                    moved.Add(new OmsiMapPoint(x + px * cos - py * sin, y + px * sin + py * cos));
                }
                result.Add(moved);
            }
            return result;
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        /// <summary>Sorts keeping the order of equal items, as Rust's sort does.</summary>
        private static void StableSort<T>(List<T> list, Comparison<T> comparison)
        {
            var indexed = new List<KeyValuePair<int, T>>(list.Count);
            for (var index = 0; index < list.Count; index++)
                indexed.Add(new KeyValuePair<int, T>(index, list[index]));
            indexed.Sort((a, b) =>
            {
                var result = comparison(a.Value, b.Value);
                return result != 0 ? result : a.Key.CompareTo(b.Key);
            });
            for (var index = 0; index < list.Count; index++)
                list[index] = indexed[index].Value;
        }

        /// <summary>The south, west, north and east edges of some tiles together, a little wider.</summary>
        public static double[] Box(OmsiGeoreference geo, IEnumerable<OmsiTileSummary> tiles, double tileSize)
        {
            double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
            foreach (var tile in tiles)
                foreach (var corner in new[] { 0.0, 0.5, 1.0 })
                    foreach (var other in new[] { 0.0, 0.5, 1.0 })
                    {
                        var at = geo.LatLon((tile.X + corner) * tileSize, (tile.Y + other) * tileSize);
                        south = Math.Min(south, at.Latitude);
                        west = Math.Min(west, at.Longitude);
                        north = Math.Max(north, at.Latitude);
                        east = Math.Max(east, at.Longitude);
                    }
            var padLatitude = (north - south) * 0.02;
            var padLongitude = (east - west) * 0.02;
            return new[] { south - padLatitude, west - padLongitude, north + padLatitude, east + padLongitude };
        }

        /// <summary>The picture of a map tile, some pixels a side, row 0 its north edge.</summary>
        public static OmsiRgbImage Build(OmsiGeoreference geo, int tileX, int tileY, double tileSize, int size, List<OmsiOsmElement> data, OmsiTrueTypeFont font,
            CancellationToken cancel)
        {
            return Draw(data, size, point =>
            {
                var local = geo.Local(tileX, tileY, point.Latitude, point.Longitude);
                return new OmsiMapPoint(local.X / tileSize * size, (1.0 - local.Y / tileSize) * size);
            }, size / tileSize, font, cancel);
        }
    }
}
