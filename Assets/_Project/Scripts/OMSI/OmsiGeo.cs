using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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

        private OmsiGeoreference(bool world, double tileSize, double anchorX, double anchorY, double latitude, double longitude, double rotation)
        {
            this.world = world;
            this.tileSize = tileSize;
            this.anchorX = anchorX;
            this.anchorY = anchorY;
            this.latitude = latitude;
            this.longitude = longitude;
            this.rotation = rotation;
        }

        /// <summary>A [worldcoordinates] map: its tiles are Web Mercator tiles of zoom 16 (ADR 019).</summary>
        public static OmsiGeoreference ForWorldCoordinates(double tileSize)
        {
            return new OmsiGeoreference(true, tileSize, 0, 0, 0, 0, 0);
        }

        /// <summary>A map placed by hand: the middle of the anchor tile lies at the latitude and longitude, its north turned by the rotation.</summary>
        public static OmsiGeoreference Placed(OmsiGeoAnchor anchor, double tileSize)
        {
            return new OmsiGeoreference(false, tileSize, (anchor.TileX + 0.5) * tileSize, (anchor.TileY + 0.5) * tileSize, anchor.Latitude, anchor.Longitude,
                anchor.RotationDegrees);
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
            var east = dx * Math.Cos(radians) + dy * Math.Sin(radians);
            var north = -dx * Math.Sin(radians) + dy * Math.Cos(radians);
            var phi0 = latitude * Math.PI / 180.0;
            // The meridian arc over a few kilometres: its radius at the middle latitude.
            var phi = phi0 + north / MeridianRadius(phi0);
            for (var pass = 0; pass < 4; pass++)
                phi = phi0 + north / MeridianRadius((phi0 + phi) * 0.5);
            var lambda = east / (PrimeRadius(phi) * Math.Cos(phi));
            return new OmsiLatLon(phi * 180.0 / Math.PI, longitude + lambda * 180.0 / Math.PI);
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
            var radians = rotation * Math.PI / 180.0;
            return new OmsiMapPoint(anchorX + east * Math.Cos(radians) - north * Math.Sin(radians), anchorY + east * Math.Sin(radians) + north * Math.Cos(radians));
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
                    latitude.ToString("R", CultureInfo.InvariantCulture), longitude.ToString("R", CultureInfo.InvariantCulture), rotation.ToString("R", CultureInfo.InvariantCulture));

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

    /// <summary>Where a map without world coordinates lies: the middle of one tile at a latitude and longitude, its north turned.</summary>
    public sealed class OmsiGeoAnchor
    {
        public int TileX { get; set; }
        public int TileY { get; set; }
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
                    RotationDegrees = Number("rotation", 0)
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
            if (!string.IsNullOrEmpty(Source))
                text.Append("source ").Append(Source).Append('\n');
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

        /// <summary>The request for one tile.</summary>
        public string TileUrl(int zoom, int x, int y)
        {
            const double r = 20037508.342789244;
            var step = 2.0 * r / (1L << zoom);
            var box = string.Join(",", (-r + x * step).ToString("0.000", CultureInfo.InvariantCulture), (r - (y + 1.0) * step).ToString("0.000", CultureInfo.InvariantCulture),
                (-r + (x + 1.0) * step).ToString("0.000", CultureInfo.InvariantCulture), (r - y * step).ToString("0.000", CultureInfo.InvariantCulture));
            var pixels = TilePixels.ToString(CultureInfo.InvariantCulture);
            return Url.Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture)).Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
                .Replace("{y}", y.ToString(CultureInfo.InvariantCulture)).Replace("{bbox}", box).Replace("{width}", pixels).Replace("{height}", pixels);
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
        /// known): the finest first, a centimetre either way counting the same, then official orthophotos, then Esri.
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
                return source.IsOrthophoto ? 0 : source.Name == EsriName ? 4 : 5;
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
