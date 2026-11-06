using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A position on the Earth in degrees, WGS 84.</summary>
    public struct OmsiLatLon
    {
        public OmsiLatLon(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude;
        public double Longitude;

        private static readonly Regex Number = new Regex(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads a latitude and a longitude as web maps copy them: "52.5200, 13.4050", "52.52 13.405", "52,52; 13,405" or
        /// "52.52° N, 13.405° E" (S and W make a number negative). Exactly two numbers, the latitude first and within ±90°, the
        /// longitude within ±180°; anything else is not a position.
        /// </summary>
        public static bool TryParse(string text, out OmsiLatLon position)
        {
            position = default(OmsiLatLon);
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var matches = Number.Matches(text);
            if (matches.Count != 2)
                return false;
            var values = new double[2];
            for (var index = 0; index < 2; index++)
                if (!double.TryParse(matches[index].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
                    return false;
            // A letter after a number names its direction: S and W lie on the other side of the equator and of Greenwich.
            for (var index = 0; index < 2; index++)
            {
                var end = matches[index].Index + matches[index].Length;
                var next = index == 0 ? matches[1].Index : text.Length;
                var between = text.Substring(end, next - end).ToUpperInvariant();
                if (between.IndexOf('S') >= 0 || between.IndexOf('W') >= 0)
                    values[index] = -Math.Abs(values[index]);
            }
            if (Math.Abs(values[0]) > 90.0 || Math.Abs(values[1]) > 180.0)
                return false;
            position = new OmsiLatLon(values[0], values[1]);
            return true;
        }
    }

    /// <summary>
    /// Where a map lies on the Earth, as openMapper places it (`src/geo/mod.rs`): a [worldcoordinates] map by its tiles,
    /// which are the Web Mercator tiles of zoom 16; any other map by an anchor, the middle of one tile at a latitude and
    /// longitude, with the map's north turned clockwise from true north. A placed map's metres are true ground metres
    /// along the meridians and the parallels (WGS 84), so a road 300 m long is 300 m long in the imagery too.
    /// </summary>
    public sealed class OmsiGeoreference
    {
        // WGS 84, the ellipsoid of the imagery's latitudes and longitudes.
        private const double Axis = 6378137.0;
        private const double Flattening = 1.0 / 298.257223563;
        private const double Eccentricity2 = Flattening * (2.0 - Flattening);
        private const double MaxMercatorLatitude = 85.05112878;

        private readonly bool world;
        private readonly double tileSize;
        private readonly double anchorX;
        private readonly double anchorY;
        private readonly double latitude;
        private readonly double longitude;
        private readonly double rotation;
        private readonly double scale;

        private OmsiGeoreference(bool world, double tileSize, double anchorX, double anchorY, double latitude, double longitude, double rotation, double scale)
        {
            this.world = world;
            this.tileSize = tileSize;
            this.anchorX = anchorX;
            this.anchorY = anchorY;
            this.latitude = latitude;
            this.longitude = longitude;
            this.rotation = rotation;
            this.scale = scale;
        }

        /// <summary>A [worldcoordinates] map: its tiles are Web Mercator tiles of zoom 16 (ADR 019).</summary>
        public static OmsiGeoreference ForWorldCoordinates(double tileSize)
        {
            return new OmsiGeoreference(true, tileSize, 0, 0, 0, 0, 0, 1);
        }

        /// <summary>
        /// A map placed by hand: the reference point (the anchor tile's middle, moved by the anchor's offset) lies at the latitude
        /// and longitude, the map's north turned by the rotation, and one metre of the map is <see cref="OmsiGeoAnchor.Scale"/> metres of ground.
        /// </summary>
        public static OmsiGeoreference Placed(OmsiGeoAnchor anchor, double tileSize)
        {
            var scale = anchor.Scale > 0.0 && !double.IsNaN(anchor.Scale) && !double.IsInfinity(anchor.Scale) ? anchor.Scale : 1.0;
            return new OmsiGeoreference(false, tileSize, (anchor.TileX + 0.5) * tileSize + anchor.OffsetX, (anchor.TileY + 0.5) * tileSize + anchor.OffsetY,
                anchor.Latitude, anchor.Longitude, anchor.RotationDegrees, scale);
        }

        /// <summary>The placement of a map: by its world coordinates, or by its anchor; null when it is not placed.</summary>
        public static OmsiGeoreference Of(OmsiMapSummary summary, OmsiGeoAnchor anchor)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (summary.UsesWorldCoordinates)
                return ForWorldCoordinates(summary.TileSizeMeters);
            return anchor != null ? Placed(anchor, summary.TileSizeMeters) : null;
        }

        public bool IsWorldCoordinates => world;

        /// <summary>Latitude and longitude of a world point (metres east and north of the map's origin).</summary>
        public OmsiLatLon LatLon(double x, double y)
        {
            if (world)
            {
                var mercator = Mercator(x, y);
                return LatLonOfMercator(mercator.X, mercator.Y);
            }
            var dx = x - anchorX;
            var dy = y - anchorY;
            var radians = rotation * Math.PI / 180.0;
            var east = (dx * Math.Cos(radians) + dy * Math.Sin(radians)) * scale;
            var north = (-dx * Math.Sin(radians) + dy * Math.Cos(radians)) * scale;
            var phi0 = latitude * Math.PI / 180.0;
            // The meridian arc over a few kilometres: its radius at the middle latitude.
            var phi = phi0 + north / MeridianRadius(phi0);
            for (var pass = 0; pass < 4; pass++)
                phi = phi0 + north / MeridianRadius((phi0 + phi) * 0.5);
            var lambda = east / (PrimeRadius(phi) * Math.Cos(phi));
            return new OmsiLatLon(phi * 180.0 / Math.PI, longitude + lambda * 180.0 / Math.PI);
        }

        /// <summary>A position moved by metres east and north on the ground (a few kilometres at most, WGS 84).</summary>
        public static OmsiLatLon Moved(OmsiLatLon position, double eastMeters, double northMeters)
        {
            var phi = position.Latitude * Math.PI / 180.0;
            var north = northMeters / MeridianRadius(phi);
            var after = phi + north;
            var east = eastMeters / (PrimeRadius(after) * Math.Cos(after));
            return new OmsiLatLon(after * 180.0 / Math.PI, position.Longitude + east * 180.0 / Math.PI);
        }

        /// <summary>
        /// The placement a stamp of pictures names (<see cref="Stamp"/>), so a picture made for an earlier place can be laid
        /// where it belongs under the present one; null for a stamp that is not a placement by hand.
        /// </summary>
        public static OmsiGeoreference FromStamp(string stamp, double tileSize)
        {
            if (string.IsNullOrWhiteSpace(stamp) || stamp.StartsWith("world", StringComparison.Ordinal))
                return null;
            var parts = stamp.Trim().Split(' ');
            if (parts.Length != 5 && parts.Length != 6)
                return null;
            var values = new double[parts.Length];
            for (var index = 0; index < parts.Length; index++)
                if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]) || double.IsNaN(values[index]) || double.IsInfinity(values[index]))
                    return null;
            var scale = parts.Length == 6 ? values[5] : 1.0;
            return scale > 0.0 ? new OmsiGeoreference(false, tileSize, values[0], values[1], values[2], values[3], values[4], scale) : null;
        }

        /// <summary>
        /// How a picture cut for one placement of a map has to be turned, scaled and moved to lie right under another:
        /// a map point m goes to <c>scale · R(rotation) · m + (moveX, moveY)</c>, R turning counter-clockwise. The two places
        /// differ only by a similarity (to within decimetres over a few kilometres), found from two points. False when either
        /// is a [worldcoordinates] map.
        /// </summary>
        public static bool Transition(OmsiGeoreference from, OmsiGeoreference to, out double scale, out double rotationDegrees, out double moveX, out double moveY)
        {
            scale = 1.0;
            rotationDegrees = 0.0;
            moveX = 0.0;
            moveY = 0.0;
            if (from == null || to == null || from.world || to.world)
                return false;
            const double Reach = 1000.0;
            var origin = from.LatLon(from.anchorX, from.anchorY);
            var a = to.World(origin.Latitude, origin.Longitude);
            var onward = from.LatLon(from.anchorX + Reach, from.anchorY);
            var b = to.World(onward.Latitude, onward.Longitude);
            var dx = (b.X - a.X) / Reach;
            var dy = (b.Y - a.Y) / Reach;
            scale = Math.Sqrt(dx * dx + dy * dy);
            if (scale < 1e-9)
                return false;
            rotationDegrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            var radians = rotationDegrees * Math.PI / 180.0;
            moveX = a.X - scale * (from.anchorX * Math.Cos(radians) - from.anchorY * Math.Sin(radians));
            moveY = a.Y - scale * (from.anchorX * Math.Sin(radians) + from.anchorY * Math.Cos(radians));
            return true;
        }

        /// <summary>The Web Mercator position of a world point: x east and y south, 0 to 1 over the whole world.</summary>
        public OmsiMapPoint Mercator(double x, double y)
        {
            if (world)
            {
                var tileX = (int)Math.Floor(x / tileSize);
                var tileY = (int)Math.Floor(y / tileSize);
                OmsiWorldTiles.ToGrid(tileX, tileY, x - tileX * tileSize, y - tileY * tileSize, out var column, out var row);
                return new OmsiMapPoint((column + 32768.0) / 65536.0, (32768.0 - row) / 65536.0);
            }
            var position = LatLon(x, y);
            return MercatorOf(position.Latitude, position.Longitude);
        }

        /// <summary>The world point at a latitude and longitude of a map placed by hand (the inverse of <see cref="LatLon"/>).</summary>
        public OmsiMapPoint World(double lat, double lon)
        {
            if (world)
                throw new InvalidOperationException("A map with world coordinates is placed by its tiles.");
            var phi0 = latitude * Math.PI / 180.0;
            var phi = lat * Math.PI / 180.0;
            var north = (phi - phi0) * MeridianRadius((phi0 + phi) * 0.5);
            var east = (lon - longitude) * Math.PI / 180.0 * PrimeRadius(phi) * Math.Cos(phi);
            east /= scale;
            north /= scale;
            var radians = rotation * Math.PI / 180.0;
            return new OmsiMapPoint(anchorX + east * Math.Cos(radians) - north * Math.Sin(radians), anchorY + east * Math.Sin(radians) + north * Math.Cos(radians));
        }

        /// <summary>
        /// The same placement with the reference point moved to a map point: the anchor takes that point's tile and offset and the
        /// latitude and longitude it has now, so choosing a reference point never moves the map. Null for a [worldcoordinates] map.
        /// </summary>
        public OmsiGeoAnchor AnchorAt(double x, double y)
        {
            if (world)
                return null;
            var position = LatLon(x, y);
            var tileX = (int)Math.Floor(x / tileSize);
            var tileY = (int)Math.Floor(y / tileSize);
            return new OmsiGeoAnchor
            {
                TileX = tileX, TileY = tileY, OffsetX = x - (tileX + 0.5) * tileSize, OffsetY = y - (tileY + 0.5) * tileSize,
                Latitude = position.Latitude, Longitude = position.Longitude, RotationDegrees = rotation, Scale = scale
            };
        }

        /// <summary>
        /// The placement that puts two map points at two latitudes and longitudes: the first point becomes the reference point; the
        /// turn comes from the direction between the points and, with <paramref name="fitScale"/>, the scale from their distance
        /// (otherwise the scale stays 1 and the map is turned about the first point). Null when the two map points or the two
        /// positions coincide.
        /// </summary>
        public static OmsiGeoAnchor FitTwoPoints(double tileSize, OmsiMapPoint firstMap, OmsiLatLon firstGround, OmsiMapPoint secondMap, OmsiLatLon secondGround, bool fitScale)
        {
            var mx = secondMap.X - firstMap.X;
            var my = secondMap.Y - firstMap.Y;
            // The second position in metres east and north of the first, with the same ellipsoid arithmetic the placement uses.
            var phi1 = firstGround.Latitude * Math.PI / 180.0;
            var phi2 = secondGround.Latitude * Math.PI / 180.0;
            var middle = (phi1 + phi2) * 0.5;
            var east = (secondGround.Longitude - firstGround.Longitude) * Math.PI / 180.0 * PrimeRadius(middle) * Math.Cos(middle);
            var north = (phi2 - phi1) * MeridianRadius(middle);
            var mapLength = Math.Sqrt(mx * mx + my * my);
            var groundLength = Math.Sqrt(east * east + north * north);
            if (mapLength < 1e-6 || groundLength < 1e-6)
                return null;
            // east + i north = scale * e^(-i rotation) * (mx + i my): the turn is the angle from the ground vector to the map's.
            var rotation = (Math.Atan2(my, mx) - Math.Atan2(north, east)) * 180.0 / Math.PI;
            rotation = ((rotation + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            var tileX = (int)Math.Floor(firstMap.X / tileSize);
            var tileY = (int)Math.Floor(firstMap.Y / tileSize);
            return new OmsiGeoAnchor
            {
                TileX = tileX, TileY = tileY, OffsetX = firstMap.X - (tileX + 0.5) * tileSize, OffsetY = firstMap.Y - (tileY + 0.5) * tileSize,
                Latitude = firstGround.Latitude, Longitude = firstGround.Longitude, RotationDegrees = rotation, Scale = fitScale ? groundLength / mapLength : 1.0
            };
        }

        /// <summary>A latitude and longitude in metres east and north of a tile's south-west corner, for either kind of map.</summary>
        public OmsiMapPoint Local(int tileX, int tileY, double lat, double lon)
        {
            if (!world)
            {
                var point = World(lat, lon);
                return new OmsiMapPoint(point.X - tileX * tileSize, point.Y - tileY * tileSize);
            }
            var mercator = MercatorOf(lat, lon);
            OmsiWorldTiles.FromGrid(tileX, tileY, mercator.X * 65536.0 - 32768.0, 32768.0 - mercator.Y * 65536.0, out var localX, out var localY);
            return new OmsiMapPoint(localX, localY);
        }

        /// <summary>Text that changes when the placement does: imagery made for another placement is out of date.</summary>
        public string Stamp =>
            world ? "world " + tileSize.ToString("R", CultureInfo.InvariantCulture)
                : string.Join(" ", anchorX.ToString("R", CultureInfo.InvariantCulture), anchorY.ToString("R", CultureInfo.InvariantCulture),
                    latitude.ToString("R", CultureInfo.InvariantCulture), longitude.ToString("R", CultureInfo.InvariantCulture), rotation.ToString("R", CultureInfo.InvariantCulture)) +
                  // A scale other than 1 is added last, so the stamps of pictures made before there was a scale stay valid.
                  (scale == 1.0 ? string.Empty : " " + scale.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>Web Mercator of a latitude and longitude: x east and y south, 0 to 1 over the whole world.</summary>
        public static OmsiMapPoint MercatorOf(double lat, double lon)
        {
            var phi = Math.Max(-MaxMercatorLatitude, Math.Min(MaxMercatorLatitude, lat)) * Math.PI / 180.0;
            return new OmsiMapPoint((lon + 180.0) / 360.0, (1.0 - Math.Log(Math.Tan(phi) + 1.0 / Math.Cos(phi)) / Math.PI) / 2.0);
        }

        public static OmsiLatLon LatLonOfMercator(double x, double y)
        {
            var lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y))) * 180.0 / Math.PI;
            return new OmsiLatLon(lat, x * 360.0 - 180.0);
        }

        /// <summary>Metres on the ground a Web Mercator pixel of a zoom covers at a latitude.</summary>
        public static double MetresPerPixel(double lat, int zoom)
        {
            return 40075016.686 * Math.Cos(lat * Math.PI / 180.0) / (256.0 * (1L << zoom));
        }

        private static double MeridianRadius(double phi)
        {
            var sin = Math.Sin(phi);
            return Axis * (1.0 - Eccentricity2) / Math.Pow(1.0 - Eccentricity2 * sin * sin, 1.5);
        }

        private static double PrimeRadius(double phi)
        {
            var sin = Math.Sin(phi);
            return Axis / Math.Sqrt(1.0 - Eccentricity2 * sin * sin);
        }
    }

    /// <summary>
    /// Where a map without world coordinates lies: a reference point at a latitude and longitude, the map's north turned and its
    /// metres scaled. The reference point is the middle of one tile, moved by an offset in metres.
    /// </summary>
    public sealed class OmsiGeoAnchor
    {
        public int TileX { get; set; }
        public int TileY { get; set; }

        /// <summary>Metres east of the anchor tile's middle: the reference point's place in the tile.</summary>
        public double OffsetX { get; set; }

        /// <summary>Metres north of the anchor tile's middle.</summary>
        public double OffsetY { get; set; }

        /// <summary>Metres of ground one metre of the map covers; 1 for a map drawn true to scale.</summary>
        public double Scale { get; set; } = 1.0;
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        /// <summary>How far the map's north is turned from true north, clockwise.</summary>
        public double RotationDegrees { get; set; }
    }

    /// <summary>How terrain heights from the world are laid on the map (openMapper's terrain options).</summary>
    public sealed class OmsiGeoHeightOptions
    {
        /// <summary>The anchor tile's mean height becomes 0 m, rather than the height above the sea.</summary>
        public bool Relative { get; set; } = true;

        /// <summary>Added to every height.</summary>
        public double Offset { get; set; }

        /// <summary>The reach of the smoothing kernel in metres; 0 for none.</summary>
        public double Smoothing { get; set; } = 10.0;

        /// <summary>
        /// The height relative heights took off the first time, kept so that tiles loaded later meet the earlier ones;
        /// null to measure it at the middle tile.
        /// </summary>
        public double? Base { get; set; }
    }

    /// <summary>
    /// The project's own placement on the Earth and imagery settings, kept in the workspace beside the map's files under
    /// Mapforge\geo.txt, which is never exported; one "key value" per line.
    /// </summary>
    public sealed class OmsiGeoSettings
    {
        public const string RelativePath = OmsiMapExporter.ProjectDataFolder + "\\geo.txt";

        public OmsiGeoAnchor Anchor { get; set; }
        public OmsiGeoHeightOptions Heights { get; } = new OmsiGeoHeightOptions();

        /// <summary>How opaque the imagery lies on the ground, 0 to 1.</summary>
        public double ImageryOpacity { get; set; } = 1.0;

        public bool ShowImagery { get; set; } = true;

        /// <summary>The imagery source chosen by name, or null for the sharpest at the map's middle.</summary>
        public string Source { get; set; }

        /// <summary>Loads take the terrain heights too.</summary>
        public bool WithTerrain { get; set; }

        /// <summary>A tile added to the map gets its pictures (and heights, with <see cref="WithTerrain"/>) by itself.</summary>
        public bool AutoNewTiles { get; set; }

        /// <summary>The edge of a tile's picture: 0 for every pixel of the source up to 4096, or 1024, 2048 or 4096.</summary>
        public int PictureSize { get; set; }

        /// <summary>
        /// Tiles whose picture is hidden or lies less opaque than the others, by tile (x in the high, y in the low 32 bits):
        /// a factor on <see cref="ImageryOpacity"/> from 0 to 1, 0 for hidden.
        /// </summary>
        public Dictionary<long, double> TileOpacity { get; } = new Dictionary<long, double>();

        public static long TileKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        /// <summary>A tile's factor on the opacity, 1 unless it was changed.</summary>
        public double OpacityOf(int x, int y)
        {
            return TileOpacity.TryGetValue(TileKey(x, y), out var factor) ? factor : 1.0;
        }

        public void SetOpacityOf(int x, int y, double factor)
        {
            factor = Math.Max(0.0, Math.Min(1.0, factor));
            if (factor >= 1.0)
                TileOpacity.Remove(TileKey(x, y));
            else
                TileOpacity[TileKey(x, y)] = factor;
        }

        public static OmsiGeoSettings Parse(string text)
        {
            var settings = new OmsiGeoSettings();
            if (string.IsNullOrEmpty(text))
                return settings;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                var space = trimmed.IndexOf(' ');
                if (space > 0)
                    values[trimmed.Substring(0, space)] = trimmed.Substring(space + 1).Trim();
            }
            double Number(string key, double fallback)
            {
                return values.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
            }
            if (values.ContainsKey("latitude") && values.ContainsKey("longitude"))
                settings.Anchor = new OmsiGeoAnchor
                {
                    TileX = (int)Number("tilex", 0), TileY = (int)Number("tiley", 0), Latitude = Number("latitude", 0), Longitude = Number("longitude", 0),
                    RotationDegrees = Number("rotation", 0), OffsetX = Number("offsetx", 0), OffsetY = Number("offsety", 0), Scale = Number("scale", 1)
                };
            settings.Heights.Relative = !values.TryGetValue("relative", out var relative) || relative != "0";
            settings.Heights.Offset = Number("offset", 0);
            settings.Heights.Smoothing = Number("smoothing", 10);
            if (values.ContainsKey("base"))
                settings.Heights.Base = Number("base", 0);
            settings.ImageryOpacity = Math.Max(0.0, Math.Min(1.0, Number("opacity", 1)));
            settings.ShowImagery = !values.TryGetValue("show", out var show) || show != "0";
            if (values.TryGetValue("source", out var source) && source.Length > 0)
                settings.Source = source;
            settings.WithTerrain = values.TryGetValue("terrain", out var terrain) && terrain == "1";
            settings.AutoNewTiles = values.TryGetValue("autonew", out var autoNew) && autoNew == "1";
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split(' ');
                if (parts.Length == 4 && parts[0] == "tile" && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileX) &&
                    int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileY) &&
                    double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
                    settings.SetOpacityOf(tileX, tileY, factor);
            }
            var size = (int)Number("picture", 0);
            settings.PictureSize = size == 1024 || size == 2048 || size == 4096 ? size : 0;
            return settings;
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            void Line(string key, double value)
            {
                text.Append(key).Append(' ').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            if (Anchor != null)
            {
                Line("tilex", Anchor.TileX);
                Line("tiley", Anchor.TileY);
                Line("latitude", Anchor.Latitude);
                Line("longitude", Anchor.Longitude);
                Line("rotation", Anchor.RotationDegrees);
                // Written only when they differ from the defaults, so a project keeps its old file until the reference point or scale is used.
                if (Anchor.OffsetX != 0.0)
                    Line("offsetx", Anchor.OffsetX);
                if (Anchor.OffsetY != 0.0)
                    Line("offsety", Anchor.OffsetY);
                if (Anchor.Scale != 1.0)
                    Line("scale", Anchor.Scale);
            }
            Line("relative", Heights.Relative ? 1 : 0);
            Line("offset", Heights.Offset);
            Line("smoothing", Heights.Smoothing);
            if (Heights.Base.HasValue)
                Line("base", Heights.Base.Value);
            Line("opacity", ImageryOpacity);
            Line("show", ShowImagery ? 1 : 0);
            Line("terrain", WithTerrain ? 1 : 0);
            Line("autonew", AutoNewTiles ? 1 : 0);
            Line("picture", PictureSize);
            foreach (var pair in TileOpacity)
                text.Append("tile ").Append(((int)(pair.Key >> 32)).ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(((int)(pair.Key & 0xffffffffL)).ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(pair.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            if (!string.IsNullOrEmpty(Source))
                text.Append("source ").Append(Source).Append('\n');
            return text.ToString();
        }
    }

    /// <summary>
    /// The user's keys for sources that need one and the user's own tile servers, kept only on this computer (never in a
    /// project, so a key is not shared with it), one tab-separated line each: "key", name, key; "source", name, kind
    /// (xyz or wms), finest zoom, URL, attribution.
    /// </summary>
    public sealed class OmsiImageryPreferences
    {
        public Dictionary<string, string> Keys { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<OmsiImagerySource> Sources { get; } = new List<OmsiImagerySource>();

        /// <summary>Where they are kept unless said otherwise: the user's local application data.</summary>
        public static string DefaultPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMSI MapWorks", "imagery-sources.txt");

        /// <summary>The key entered for a source, or null.</summary>
        public string Key(string sourceName)
        {
            return sourceName != null && Keys.TryGetValue(sourceName, out var key) && !string.IsNullOrWhiteSpace(key) ? key.Trim() : null;
        }

        /// <summary>Whether a source can be asked: it has its key if it needs one.</summary>
        public bool Usable(OmsiImagerySource source)
        {
            return source != null && (!source.NeedsKey || Key(source.Name) != null);
        }

        /// <summary>The editor's sources, then the user's own that do not share a name with one of them.</summary>
        public List<OmsiImagerySource> All()
        {
            var all = new List<OmsiImagerySource>(OmsiImagerySource.Imagery);
            foreach (var source in Sources)
                if (!all.Exists(item => item.Name == source.Name))
                    all.Add(source);
            return all;
        }

        public static OmsiImageryPreferences Parse(string text)
        {
            var preferences = new OmsiImageryPreferences();
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length == 3 && parts[0] == "key" && parts[1].Length > 0)
                    preferences.Keys[parts[1]] = parts[2];
                else if (parts.Length == 6 && parts[0] == "source" && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom))
                {
                    try
                    {
                        preferences.Sources.Add(OmsiImagerySource.Own(parts[1], parts[4], parts[5], zoom, parts[2] == "wms"));
                    }
                    catch (ArgumentException)
                    {
                        // A broken line is left out; the others stay.
                    }
                }
            }
            return preferences;
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            string Clean(string value)
            {
                return (value ?? string.Empty).Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
            }
            foreach (var pair in Keys)
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    text.Append("key\t").Append(Clean(pair.Key)).Append('\t').Append(Clean(pair.Value.Trim())).Append('\n');
            foreach (var source in Sources)
                text.Append("source\t").Append(Clean(source.Name)).Append('\t').Append(source.Kind == OmsiImageryKind.Wms ? "wms" : "xyz").Append('\t')
                    .Append(source.MaxZoom.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Clean(source.Url)).Append('\t').Append(Clean(source.Attribution)).Append('\n');
            return text.ToString();
        }
    }

    /// <summary>How a source's tiles are asked for.</summary>
    public enum OmsiImageryKind
    {
        /// <summary>Pictures at {z}/{x}/{y} (JPEG or PNG; WMTS in the Web Mercator grid too).</summary>
        Imagery,

        /// <summary>A WMS or ArcGIS export: each Web Mercator tile asked for by its box in EPSG:3857 metres.</summary>
        Wms,

        /// <summary>Heights as Terrarium PNGs: R · 256 + G + B / 256 − 32768 m.</summary>
        Terrarium,

        /// <summary>The map drawn from OpenStreetMap's data (<see cref="OmsiOsmMap"/>), not fetched as tiles.</summary>
        OsmMap
    }

    /// <summary>
    /// A tile server that needs no key, as openMapper knows them (`src/geo/sources.rs`): Esri World Imagery for the whole
    /// world, official orthophotos of some countries and states, and the AWS terrain tiles for heights.
    /// </summary>
    public sealed class OmsiImagerySource
    {
        public string Name { get; private set; }

        /// <summary>{z}, {x}, {y} and for a WMS {bbox}, {width} and {height} are filled in.</summary>
        public string Url { get; private set; }

        public string Attribution { get; private set; }

        /// <summary>Where the source's terms are.</summary>
        public string Terms { get; private set; }

        public int MaxZoom { get; private set; }
        public OmsiImageryKind Kind { get; private set; }

        /// <summary>West, south, east, north in degrees; null for the whole world.</summary>
        public double[] Bounds { get; private set; }

        /// <summary>The pictures' own ground resolution in metres a pixel; 0 for as fine as the zoom says.</summary>
        public double Resolution { get; private set; }

        /// <summary>Pixels a side of each tile asked for.</summary>
        public int TilePixels { get; private set; } = 256;

        /// <summary>An official orthophoto, preferred where it has the place.</summary>
        public bool IsOrthophoto { get; private set; }

        /// <summary>Works only with the user's own key, filled in for {key}.</summary>
        public bool NeedsKey { get; private set; }

        /// <summary>Added by the user, not part of the editor.</summary>
        public bool IsOwn { get; private set; }

        /// <summary>A tile server of the user's: XYZ or WMTS tiles at {z}/{x}/{y}, or a WMS asked by {bbox}.</summary>
        public static OmsiImagerySource Own(string name, string url, string attribution, int maxZoom, bool wms)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("A tile server needs a name and a URL.");
            return new OmsiImagerySource
            {
                Name = name.Trim(), Url = url.Trim(), Attribution = string.IsNullOrWhiteSpace(attribution) ? name.Trim() : attribution.Trim(), Terms = string.Empty,
                MaxZoom = Math.Max(1, Math.Min(22, maxZoom)), Kind = wms ? OmsiImageryKind.Wms : OmsiImageryKind.Imagery, TilePixels = wms ? 512 : 256, IsOwn = true,
                NeedsKey = url.Contains("{key}")
            };
        }

        private static OmsiImagerySource Keyed(string name, string url, string attribution, int maxZoom, string link, int pixels)
        {
            return new OmsiImagerySource { Name = name, Url = url, Attribution = attribution, Terms = link, MaxZoom = maxZoom, NeedsKey = true, TilePixels = pixels };
        }

        public const string EsriName = "Esri World Imagery";
        public const string TerrainName = "AWS Terrain Tiles";

        private static string Wms(string baseUrl, string layer)
        {
            return baseUrl + "?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=" + layer + "&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={width}&HEIGHT={height}&FORMAT=image/jpeg";
        }

        private static OmsiImagerySource Ortho(string name, string url, string attribution, string terms, double[] bounds, double resolution, bool wms)
        {
            return new OmsiImagerySource
            {
                Name = name, Url = url, Attribution = attribution, Terms = terms, Bounds = bounds, Resolution = resolution, IsOrthophoto = true,
                Kind = wms ? OmsiImageryKind.Wms : OmsiImageryKind.Imagery, TilePixels = wms ? 512 : 256, MaxZoom = wms ? 20 : 21
            };
        }

        /// <summary>The sources of pictures from the air, none of them needing a key.</summary>
        public static IReadOnlyList<OmsiImagerySource> Imagery { get; } = new[]
        {
            new OmsiImagerySource
            {
                Name = EsriName, Url = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}?blankTile=false",
                Attribution = "Imagery © Esri, Maxar, Earthstar Geographics, and the GIS User Community", Terms = "https://www.esri.com/en-us/legal/terms/full-master-agreement",
                MaxZoom = 21
            },
            Keyed("Mapbox Satellite", "https://api.mapbox.com/v4/mapbox.satellite/{z}/{x}/{y}@2x.jpg90?access_token={key}", "© Mapbox © OpenStreetMap contributors © Maxar", 21,
                "https://account.mapbox.com/access-tokens/", 512),
            Keyed("MapTiler Satellite", "https://api.maptiler.com/tiles/satellite-v2/{z}/{x}/{y}.jpg?key={key}", "© MapTiler © OpenStreetMap contributors", 20,
                "https://cloud.maptiler.com/account/keys/", 256),
            Ortho("Berlin TrueDOP 2024", Wms("https://gdi.berlin.de/services/wms/truedop_2024", "truedop_2024"), "© Geoportal Berlin, TrueDOP 2024, dl-de/zero-2.0",
                "https://www.govdata.de/dl-de/zero-2-0", new[] { 13.08, 52.33, 13.77, 52.68 }, 0.2, true),
            Ortho("Brandenburg DOP20", Wms("https://isk.geobasis-bb.de/mapproxy/dop20c/service/wms", "bebb_dop20c"), "© GeoBasis-DE/LGB, dl-de/by-2-0",
                "https://www.govdata.de/dl-de/by-2-0", new[] { 11.26, 51.35, 14.77, 53.56 }, 0.2, true),
            Ortho("NRW DOP10", Wms("https://www.wms.nrw.de/geobasis/wms_nw_dop", "nw_dop_rgb"), "© Geobasis NRW, dl-de/zero-2.0", "https://www.govdata.de/dl-de/zero-2-0",
                new[] { 5.86, 50.32, 9.47, 52.53 }, 0.1, true),
            Ortho("Bayern DOP40", Wms("https://geoservices.bayern.de/od/wms/dop/v1/dop40", "by_dop40c"), "© Bayerische Vermessungsverwaltung, CC BY 4.0",
                "https://creativecommons.org/licenses/by/4.0/", new[] { 8.97, 47.27, 13.84, 50.57 }, 0.4, true),
            Ortho("Niedersachsen DOP20", Wms("https://opendata.lgln.niedersachsen.de/doorman/noauth/dop_wms", "ni_dop20"), "LGLN (2025), CC BY 4.0",
                "https://creativecommons.org/licenses/by/4.0/", new[] { 6.65, 51.29, 11.6, 53.9 }, 0.2, true),
            Ortho("PDOK Luchtfoto (NL)", "https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0/Actueel_orthoHR/EPSG:3857/{z}/{x}/{y}.jpeg",
                "Luchtfoto © Beeldmateriaal Nederland / PDOK, CC BY 4.0", "https://www.pdok.nl/", new[] { 3.2, 50.75, 7.22, 53.7 }, 0.08, false),
            Ortho("swisstopo SWISSIMAGE", "https://wmts.geo.admin.ch/1.0.0/ch.swisstopo.swissimage/default/current/3857/{z}/{x}/{y}.jpeg", "© swisstopo",
                "https://www.geo.admin.ch/en/general-terms-of-use-fsdi", new[] { 5.95, 45.82, 10.49, 47.81 }, 0.1, false),
            Ortho("basemap.at Orthofoto", "https://maps.wien.gv.at/basemap/bmaporthofoto30cm/normal/google3857/{z}/{y}/{x}.jpeg", "Datenquelle: basemap.at, CC BY 4.0",
                "https://basemap.at/", new[] { 9.53, 46.37, 17.16, 49.02 }, 0.29, false),
            Ortho("IGN Ortho (France)",
                "https://data.geopf.fr/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=ORTHOIMAGERY.ORTHOPHOTOS&STYLE=normal&TILEMATRIXSET=PM&TILEMATRIX={z}&TILEROW={y}&TILECOL={x}&FORMAT=image/jpeg",
                "© IGN, Licence Ouverte Etalab 2.0", "https://www.etalab.gouv.fr/licence-ouverte-open-licence/", new[] { -5.2, 41.3, 9.6, 51.1 }, 0.2, false),
            Ortho("New York State Orthos",
                "https://orthos.its.ny.gov/arcgis/rest/services/wms/Latest/MapServer/export?bbox={bbox}&bboxSR=3857&imageSR=3857&size={width},{height}&format=jpg&f=image",
                "NYS ITS Geospatial Services", "https://gis.ny.gov/orthoimagery", new[] { -79.77, 40.49, -71.85, 45.02 }, 0.15, true)
        };

        /// <summary>Terrain heights for the whole world, from the AWS Open Data terrain tiles.</summary>
        public static OmsiImagerySource Terrain { get; } = new OmsiImagerySource
        {
            Name = TerrainName, Url = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png", Kind = OmsiImageryKind.Terrarium, MaxZoom = 15,
            Attribution = "Terrain: Mapzen Terrain Tiles on AWS Open Data (SRTM, USGS 3DEP, GMTED2010, ETOPO1, Copernicus EU-DEM and others)",
            Terms = "https://github.com/tilezen/joerd/blob/master/docs/attribution.md"
        };

        /// <summary>The map as OpenStreetMap draws it, drawn here from its data at zoom 19 detail.</summary>
        public static OmsiImagerySource OsmMap { get; } = new OmsiImagerySource
        {
            Name = OmsiOsmMap.SourceName, Url = string.Empty, Kind = OmsiImageryKind.OsmMap, MaxZoom = 19, Attribution = OmsiOsmMap.Attribution,
            Terms = "https://www.openstreetmap.org/copyright"
        };

        /// <summary>The request for one tile, with the user's key where the source takes one.</summary>
        public string TileUrl(int zoom, int x, int y, string key = null)
        {
            const double r = 20037508.342789244;
            var step = 2.0 * r / (1L << zoom);
            var box = string.Join(",", (-r + x * step).ToString("0.000", CultureInfo.InvariantCulture), (r - (y + 1.0) * step).ToString("0.000", CultureInfo.InvariantCulture),
                (-r + (x + 1.0) * step).ToString("0.000", CultureInfo.InvariantCulture), (r - y * step).ToString("0.000", CultureInfo.InvariantCulture));
            var pixels = TilePixels.ToString(CultureInfo.InvariantCulture);
            return Url.Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture)).Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
                .Replace("{y}", y.ToString(CultureInfo.InvariantCulture)).Replace("{bbox}", box).Replace("{width}", pixels).Replace("{height}", pixels)
                .Replace("{key}", Uri.EscapeDataString(key ?? string.Empty));
        }

        /// <summary>Whether the source may have imagery at a place.</summary>
        public bool Covers(double lat, double lon)
        {
            return Bounds == null || lon >= Bounds[0] && lon <= Bounds[2] && lat >= Bounds[1] && lat <= Bounds[3];
        }

        /// <summary>The finest zoom worth asking for at a latitude: a finer one than the source's own resolution is only enlarged.</summary>
        public int UsefulZoom(double lat)
        {
            if (Resolution <= 0.0)
                return MaxZoom;
            var atZero = OmsiGeoreference.MetresPerPixel(lat, 0) * 256.0 / TilePixels;
            return (int)Math.Max(1.0, Math.Min(MaxZoom, Math.Floor(Math.Log(atZero / Resolution, 2.0) + 0.7)));
        }

        /// <summary>Ground metres a pixel of the source's tiles of a zoom covers at a latitude, no finer than its own resolution.</summary>
        public double MetresPerPixel(double lat, int zoom)
        {
            return Math.Max(OmsiGeoreference.MetresPerPixel(lat, zoom) * 256.0 / TilePixels, Resolution);
        }

        /// <summary>A folder name for the disk cache.</summary>
        public string Slug
        {
            get
            {
                var text = new StringBuilder();
                foreach (var c in Name)
                    text.Append(char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '-');
                var parts = text.ToString().Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 0 ? "source" : string.Join("-", parts);
            }
        }

        /// <summary>
        /// Sources in the order to prefer them at a place, given each one's detail there in metres a pixel (null when not
        /// known): the finest first, a centimetre either way counting the same, then official orthophotos, Mapbox, MapTiler,
        /// Esri and the user's own servers.
        /// </summary>
        public static List<KeyValuePair<OmsiImagerySource, double?>> Rank(IEnumerable<KeyValuePair<OmsiImagerySource, double?>> found)
        {
            var list = new List<KeyValuePair<OmsiImagerySource, double?>>(found);
            long Centimetres(double? metres)
            {
                return metres.HasValue ? (long)Math.Round(metres.Value * 100.0) : long.MaxValue;
            }
            int Preference(OmsiImagerySource source)
            {
                return source.IsOrthophoto ? 0 : source.Name.StartsWith("Mapbox", StringComparison.Ordinal) ? 2 :
                    source.Name.StartsWith("MapTiler", StringComparison.Ordinal) ? 3 : source.Name == EsriName ? 4 : 5;
            }
            list.Sort((a, b) =>
            {
                var byDetail = Centimetres(a.Value).CompareTo(Centimetres(b.Value));
                return byDetail != 0 ? byDetail : Preference(a.Key).CompareTo(Preference(b.Key));
            });
            return list;
        }

        /// <summary>A Terrarium pixel's height in metres.</summary>
        public static float TerrariumHeight(byte red, byte green, byte blue)
        {
            return red * 256f + green + blue / 256f - 32768f;
        }
    }
}
