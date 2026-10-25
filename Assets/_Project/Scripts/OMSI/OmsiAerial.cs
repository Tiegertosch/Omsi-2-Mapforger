using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A picture as 8-bit red, green and blue, row 0 at the top.</summary>
    public sealed class OmsiRgbImage
    {
        public OmsiRgbImage(int width, int height, byte[] rgb)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (rgb == null || rgb.Length != width * height * 3)
                throw new ArgumentException("Three bytes per pixel are required.", nameof(rgb));
            Width = width;
            Height = height;
            Rgb = rgb;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Rgb { get; }

        /// <summary>The picture at most some pixels a side, each new pixel the mean of the old ones it covers; itself when it is small enough.</summary>
        public OmsiRgbImage Reduced(int maximum)
        {
            if (Width <= maximum && Height <= maximum)
                return this;
            var scale = Math.Max(Width, Height) / (double)maximum;
            var width = Math.Max(1, (int)Math.Round(Width / scale));
            var height = Math.Max(1, (int)Math.Round(Height / scale));
            var rgb = new byte[width * height * 3];
            Parallel.For(0, height, y =>
            {
                int top = (int)(y * (double)Height / height), bottom = Math.Max(top + 1, (int)((y + 1) * (double)Height / height));
                for (var x = 0; x < width; x++)
                {
                    int left = (int)(x * (double)Width / width), right = Math.Max(left + 1, (int)((x + 1) * (double)Width / width));
                    int red = 0, green = 0, blue = 0, count = 0;
                    for (var sy = top; sy < bottom; sy++)
                        for (var sx = left; sx < right; sx++)
                        {
                            var index = (sy * Width + sx) * 3;
                            red += Rgb[index];
                            green += Rgb[index + 1];
                            blue += Rgb[index + 2];
                            count++;
                        }
                    var target = (y * width + x) * 3;
                    rgb[target] = (byte)((red + count / 2) / count);
                    rgb[target + 1] = (byte)((green + count / 2) / count);
                    rgb[target + 2] = (byte)((blue + count / 2) / count);
                }
            });
            return new OmsiRgbImage(width, height, rgb);
        }
    }

    /// <summary>A place found by name.</summary>
    public sealed class OmsiPlace
    {
        public string Name { get; internal set; }
        public double Latitude { get; internal set; }
        public double Longitude { get; internal set; }
    }

    /// <summary>
    /// Downloads as openMapper makes them (`src/geo/fetch.rs`): tiles through a disk cache, so none is fetched twice, at
    /// most four requests at once, each with the editor's own User-Agent, a dropped connection or a busy server tried
    /// again twice; and places looked up by name with OpenStreetMap's Nominatim, at most one request a second.
    /// </summary>
    public sealed class OmsiTileFetcher
    {
        public const string UserAgent = "OMSI-MapWorks/1.0 (+https://github.com/Tiegertosch/Omsi-2-Mapforger)";

        private static readonly HttpClient Client = CreateClient(30);
        private static readonly HttpClient SlowClient = CreateClient(150);
        private static readonly object OverpassLock = new object();
        private static readonly string[] OverpassServers = { "https://overpass-api.de/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter" };
        private static readonly SemaphoreSlim Slots = new SemaphoreSlim(4);
        private static readonly object SearchLock = new object();
        private static DateTime lastSearch = DateTime.MinValue;

        private readonly string cacheRoot;
        private readonly Func<string, CancellationToken, KeyValuePair<int, byte[]>> get;
        private readonly Func<string, CancellationToken, KeyValuePair<int, byte[]>> slowGet;

        /// <summary>The user's key for a source by its name, or null; a key never shows in a message.</summary>
        public Func<string, string> Keys { get; set; }

        /// <param name="cacheRoot">Where downloaded tiles are kept.</param>
        /// <param name="get">The HTTP GET of a URL as status and body; null for the network.</param>
        public OmsiTileFetcher(string cacheRoot, Func<string, CancellationToken, KeyValuePair<int, byte[]>> get = null)
        {
            this.cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
            this.get = get ?? Download;
            slowGet = get ?? DownloadSlowly;
        }

        /// <summary>Where tiles are kept unless said otherwise: the user's local application data.</summary>
        public static string DefaultCacheRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMSI MapWorks", "Cache", "Tiles");

        /// <summary>One tile's file (JPEG or PNG), from the cache when it is there; null where the source has nothing.</summary>
        public byte[] Tile(OmsiImagerySource source, int zoom, int x, int y, CancellationToken cancel)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var folder = Path.Combine(cacheRoot, source.Slug, zoom.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture));
            var data = Path.Combine(folder, y.ToString(CultureInfo.InvariantCulture) + ".tile");
            var none = Path.Combine(folder, y.ToString(CultureInfo.InvariantCulture) + ".none");
            if (File.Exists(data))
            {
                var kept = File.ReadAllBytes(data);
                // A page a server sent in place of a picture, kept by an older version, is asked for again.
                if (IsPicture(kept))
                    return kept;
                File.Delete(data);
            }
            if (File.Exists(none))
                return null;
            var key = source.NeedsKey ? Keys?.Invoke(source.Name) : null;
            if (source.NeedsKey && string.IsNullOrEmpty(key))
                throw new IOException(source.Name + ": no key entered.");
            var answer = GetWithRetries(source.TileUrl(zoom, x, y, key), cancel);
            Directory.CreateDirectory(folder);
            switch (answer.Key)
            {
                case 200 when answer.Value.Length > 0 && !IsPicture(answer.Value):
                    // A maintenance page or a WMS error with "200 OK": not kept, so it is asked for again later.
                    throw new IOException(source.Name + ": the server sent no picture" + Said(answer.Value) + ".");
                case 200 when answer.Value.Length > 0 && !(source.Kind != OmsiImageryKind.Terrarium && IsPlaceholder(answer.Value)):
                    Keep(data, answer.Value);
                    return answer.Value;
                case 200:
                case 204:
                case 404:
                    Keep(none, new byte[0]);
                    return null;
                case 401:
                case 403:
                    throw new IOException(source.Name + ": the server refused the key (HTTP " + answer.Key + ").");
                case 429:
                    throw new IOException(source.Name + ": too many requests, try again later.");
                default:
                    throw new IOException(source.Name + ": HTTP " + answer.Key + ".");
            }
        }

        /// <summary>
        /// The finest zoom a source really has at a place: the highest at which its tile there is a picture, tried from the
        /// finest worth asking for down to 10; null where it has none. The answers stay in the cache.
        /// </summary>
        public int? FinestZoom(OmsiImagerySource source, double lat, double lon, CancellationToken cancel)
        {
            if (!source.Covers(lat, lon))
                return null;
            var mercator = OmsiGeoreference.MercatorOf(lat, lon);
            for (var zoom = source.UsefulZoom(lat); zoom >= 10; zoom--)
            {
                var count = 1L << zoom;
                if (Tile(source, zoom, (int)(mercator.X * count), (int)(mercator.Y * count), cancel) != null)
                    return zoom;
            }
            return null;
        }

        /// <summary>Places by name from OpenStreetMap's Nominatim, at most one request a second as its policy asks.</summary>
        public List<OmsiPlace> Search(string query, CancellationToken cancel)
        {
            lock (SearchLock)
            {
                var wait = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - lastSearch);
                if (wait > TimeSpan.Zero)
                    Thread.Sleep(wait);
                lastSearch = DateTime.UtcNow;
            }
            var answer = GetWithRetries("https://nominatim.openstreetmap.org/search?format=json&limit=8&accept-language=de,en&q=" + Uri.EscapeDataString(query), cancel);
            if (answer.Key != 200)
                throw new IOException("Nominatim: HTTP " + answer.Key + ".");
            return ParsePlaces(Encoding.UTF8.GetString(answer.Value));
        }

        /// <summary>
        /// What OpenStreetMap has for an Overpass query, one request at a time: the main server first, then a mirror when
        /// it is busy. A whole answer is kept a week beside the tiles; one cut short or empty is not kept.
        /// </summary>
        public string Overpass(string query, CancellationToken cancel)
        {
            lock (OverpassLock)
            {
                var folder = Path.Combine(Path.GetDirectoryName(cacheRoot) ?? cacheRoot, "osm");
                var hash = 0xcbf29ce484222325UL;
                foreach (var value in Encoding.UTF8.GetBytes(query))
                    hash = unchecked((hash ^ value) * 0x100000001b3UL);
                var path = Path.Combine(folder, hash.ToString("x16", CultureInfo.InvariantCulture) + ".json");
                if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7))
                    return File.ReadAllText(path, Encoding.UTF8);
                string problem = null;
                foreach (var server in OverpassServers)
                {
                    cancel.ThrowIfCancellationRequested();
                    KeyValuePair<int, byte[]> answer;
                    try
                    {
                        answer = slowGet(server + "?data=" + Uri.EscapeDataString(query), cancel);
                    }
                    catch (Exception exception) when (exception is HttpRequestException || exception is IOException || exception is TaskCanceledException && !cancel.IsCancellationRequested)
                    {
                        problem = "No connection to OpenStreetMap: are you online? (" + exception.Message + ")";
                        continue;
                    }
                    if (answer.Key == 200)
                    {
                        var text = Encoding.UTF8.GetString(answer.Value);
                        if (!text.Contains("\"remark\"") && text.Contains("\"elements\"") && !Regex.IsMatch(text, "\"elements\"\\s*:\\s*\\[\\s*\\]"))
                        {
                            Directory.CreateDirectory(folder);
                            Keep(path, Encoding.UTF8.GetBytes(text));
                        }
                        return text;
                    }
                    if (answer.Key == 429 || answer.Key >= 502 && answer.Key <= 504)
                        problem = "OpenStreetMap's servers are busy: try again in a minute.";
                    else
                        throw new IOException("Overpass: HTTP " + answer.Key + ".");
                }
                throw new IOException(problem ?? "OpenStreetMap did not answer.");
            }
        }

        /// <summary>Nominatim's answer: its places' display names, latitudes and longitudes.</summary>
        public static List<OmsiPlace> ParsePlaces(string json)
        {
            var places = new List<OmsiPlace>();
            foreach (Match item in Regex.Matches(json ?? string.Empty, @"\{[^{}]*\}"))
            {
                var name = Regex.Match(item.Value, "\"display_name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                var lat = Regex.Match(item.Value, "\"lat\"\\s*:\\s*\"?(-?[0-9.]+)");
                var lon = Regex.Match(item.Value, "\"lon\"\\s*:\\s*\"?(-?[0-9.]+)");
                if (!name.Success || !lat.Success || !lon.Success)
                    continue;
                places.Add(new OmsiPlace
                {
                    Name = Regex.Unescape(name.Groups[1].Value), Latitude = double.Parse(lat.Groups[1].Value, CultureInfo.InvariantCulture),
                    Longitude = double.Parse(lon.Groups[1].Value, CultureInfo.InvariantCulture)
                });
            }
            return places;
        }

        /// <summary>Whether a file is a picture the editor reads: JPEG or PNG.</summary>
        public static bool IsPicture(byte[] bytes)
        {
            return bytes != null && bytes.Length > 8 &&
                   (bytes[0] == 0xFF && bytes[1] == 0xD8 || bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G');
        }

        /// <summary>What a server's page or error says, in a few words: its title, or a WMS ServiceException's text.</summary>
        private static string Said(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var said = Regex.Match(text, @"<title>\s*([^<]*?)\s*</title>", RegexOptions.IgnoreCase);
            if (!said.Success)
                said = Regex.Match(text, @"<ServiceException[^>]*>\s*([^<]*?)\s*</ServiceException>", RegexOptions.IgnoreCase);
            if (!said.Success)
                return string.Empty;
            var words = said.Groups[1].Value.Trim();
            return words.Length == 0 ? string.Empty : " (" + (words.Length > 120 ? words.Substring(0, 120) + "..." : words) + ")";
        }

        /// <summary>Esri's "no imagery here" picture, byte for byte the same everywhere.</summary>
        private static bool IsPlaceholder(byte[] bytes)
        {
            if (bytes.Length != 2521)
                return false;
            var hash = 0xcbf29ce484222325UL;
            foreach (var value in bytes)
                hash = unchecked((hash ^ value) * 0x100000001b3UL);
            return hash == 0x484ab5da02f7864fUL;
        }

        private KeyValuePair<int, byte[]> GetWithRetries(string url, CancellationToken cancel)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                if (attempt > 0)
                    Thread.Sleep(500 * attempt * attempt);
                try
                {
                    var answer = get(url, cancel);
                    if (answer.Key != 429 && answer.Key < 500)
                        return answer;
                    last = new IOException("HTTP " + answer.Key);
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is IOException || exception is TaskCanceledException && !cancel.IsCancellationRequested)
                {
                    last = exception;
                }
            }
            throw new IOException("No connection to the server: are you online? (" + last?.Message + ")", last);
        }

        private static KeyValuePair<int, byte[]> DownloadSlowly(string url, CancellationToken cancel)
        {
            using (var response = SlowClient.GetAsync(url, cancel).GetAwaiter().GetResult())
                return new KeyValuePair<int, byte[]>((int)response.StatusCode, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
        }

        private static KeyValuePair<int, byte[]> Download(string url, CancellationToken cancel)
        {
            Slots.Wait(cancel);
            try
            {
                using (var response = Client.GetAsync(url, cancel).GetAwaiter().GetResult())
                    return new KeyValuePair<int, byte[]>((int)response.StatusCode, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            }
            finally
            {
                Slots.Release();
            }
        }

        private static HttpClient CreateClient(int seconds)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        private static void Keep(string path, byte[] bytes)
        {
            var temporary = path + ".part";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
        }
    }

    /// <summary>A source's tiles over an area side by side, read at Web Mercator positions (openMapper's `Mosaic`).</summary>
    public sealed class OmsiMosaic
    {
        /// <summary>How many zooms up a missing tile is looked for, its part then enlarged.</summary>
        private const int Up = 5;

        private readonly int zoom;
        private readonly int x0;
        private readonly int y0;
        private readonly int columns;
        private readonly int rows;
        private readonly int pixels;
        private readonly byte[] rgb;

        private OmsiMosaic(int zoom, int x0, int y0, int columns, int rows, int pixels, byte[] rgb)
        {
            this.zoom = zoom;
            this.x0 = x0;
            this.y0 = y0;
            this.columns = columns;
            this.rows = rows;
            this.pixels = pixels;
            this.rgb = rgb;
        }

        /// <summary>Some tiles came from a coarser zoom, as the source had nothing finer there.</summary>
        public bool Coarser { get; private set; }

        /// <summary>
        /// The tiles of a zoom covering Web Mercator <paramref name="low"/> to <paramref name="high"/>, six at a time.
        /// <paramref name="heights"/>: Terrarium heights, whose coarser tiles are enlarged without blending their codes.
        /// </summary>
        public static OmsiMosaic Fetch(int zoom, OmsiMapPoint low, OmsiMapPoint high, bool heights, Func<int, int, int, OmsiRgbImage> tile, CancellationToken cancel)
        {
            var count = 1L << zoom;
            int Cell(double value)
            {
                return (int)Math.Min(count - 1, Math.Max(0.0, Math.Floor(value * count)));
            }
            var x0 = Cell(low.X);
            var y0 = Cell(low.Y);
            var columns = Cell(high.X) - x0 + 1;
            var rows = Cell(high.Y) - y0 + 1;
            if (columns * rows > 40 * 40)
                throw new InvalidOperationException("The area is too large for this zoom.");
            var empty = heights ? new byte[] { 128, 0, 0 } : new byte[] { 128, 128, 128 };
            var first = One(zoom, x0, y0, heights, tile, out var firstCoarser);
            var pixels = first?.Width ?? 256;
            var mosaic = new OmsiMosaic(zoom, x0, y0, columns, rows, pixels, new byte[columns * pixels * rows * pixels * 3]);
            for (var index = 0; index < mosaic.rgb.Length; index += 3)
                Array.Copy(empty, 0, mosaic.rgb, index, 3);
            mosaic.Place(0, 0, first, firstCoarser);
            var jobs = new List<KeyValuePair<int, int>>();
            for (var j = 0; j < rows; j++)
                for (var i = 0; i < columns; i++)
                    if (i != 0 || j != 0)
                        jobs.Add(new KeyValuePair<int, int>(i, j));
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancel }, job =>
            {
                var picture = One(zoom, x0 + job.Key, y0 + job.Value, heights, tile, out var coarser);
                lock (mosaic)
                    mosaic.Place(job.Key, job.Value, picture, coarser);
            });
            return mosaic;
        }

        private void Place(int i, int j, OmsiRgbImage picture, bool coarser)
        {
            if (picture == null)
            {
                Coarser = true;
                return;
            }
            Coarser |= coarser;
            if (picture.Width != pixels || picture.Height != pixels)
                picture = Resize(picture, 0, 0, picture.Width, picture.Height, pixels, false);
            var width = columns * pixels;
            for (var row = 0; row < pixels; row++)
                Array.Copy(picture.Rgb, row * pixels * 3, rgb, ((j * pixels + row) * width + i * pixels) * 3, pixels * 3);
        }

        /// <summary>One tile, or the part of a coarser one enlarged where the source has none; null where it has nothing.</summary>
        private static OmsiRgbImage One(int zoom, int x, int y, bool heights, Func<int, int, int, OmsiRgbImage> tile, out bool coarser)
        {
            for (var up = 0; up <= Math.Min(Up, zoom); up++)
            {
                var picture = tile(zoom - up, x >> up, y >> up);
                if (picture == null)
                    continue;
                coarser = up > 0;
                if (up == 0)
                    return picture;
                var part = Math.Max(1, picture.Width >> up);
                var mask = (1 << up) - 1;
                return Resize(picture, (x & mask) * part, (y & mask) * part, part, part, picture.Width, heights);
            }
            coarser = true;
            return null;
        }

        /// <summary>A part of a picture brought to a size: nearest pixel for height codes, bilinear for colours.</summary>
        private static OmsiRgbImage Resize(OmsiRgbImage source, int left, int top, int width, int height, int size, bool nearest)
        {
            var result = new byte[size * size * 3];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var fx = left + (x + 0.5) * width / size - 0.5;
                    var fy = top + (y + 0.5) * height / size - 0.5;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        double value;
                        if (nearest)
                            value = source.Rgb[(Clamp((int)Math.Round(fy), source.Height) * source.Width + Clamp((int)Math.Round(fx), source.Width)) * 3 + channel];
                        else
                        {
                            var ix = (int)Math.Floor(fx);
                            var iy = (int)Math.Floor(fy);
                            var u = fx - ix;
                            var v = fy - iy;
                            double At(int px, int py)
                            {
                                return source.Rgb[(Clamp(py, source.Height) * source.Width + Clamp(px, source.Width)) * 3 + channel];
                            }
                            var topValue = At(ix, iy) + (At(ix + 1, iy) - At(ix, iy)) * u;
                            var bottomValue = At(ix, iy + 1) + (At(ix + 1, iy + 1) - At(ix, iy + 1)) * u;
                            value = topValue + (bottomValue - topValue) * v;
                        }
                        result[(y * size + x) * 3 + channel] = (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
                    }
                }
            return new OmsiRgbImage(size, size, result);
        }

        private static int Clamp(int value, int count)
        {
            return Math.Max(0, Math.Min(count - 1, value));
        }

        /// <summary>The four pixels around a Web Mercator position and how far between them.</summary>
        private void Corners(OmsiMapPoint mercator, out int a, out int b, out int c, out int d, out double u, out double v)
        {
            var scale = (double)(1L << zoom) * pixels;
            var fx = mercator.X * scale - (double)x0 * pixels - 0.5;
            var fy = mercator.Y * scale - (double)y0 * pixels - 0.5;
            var ix = (long)Math.Floor(fx);
            var iy = (long)Math.Floor(fy);
            u = fx - ix;
            v = fy - iy;
            var width = columns * pixels;
            var height = rows * pixels;
            int At(long x, long y)
            {
                return (int)(Math.Max(0, Math.Min(height - 1, y)) * width + Math.Max(0, Math.Min(width - 1, x)));
            }
            a = At(ix, iy);
            b = At(ix + 1, iy);
            c = At(ix, iy + 1);
            d = At(ix + 1, iy + 1);
        }

        /// <summary>The colour at a Web Mercator position, bilinear.</summary>
        public void Colour(OmsiMapPoint mercator, byte[] into, int offset)
        {
            Corners(mercator, out var a, out var b, out var c, out var d, out var u, out var v);
            for (var channel = 0; channel < 3; channel++)
            {
                var top = rgb[a * 3 + channel] + (rgb[b * 3 + channel] - rgb[a * 3 + channel]) * u;
                var bottom = rgb[c * 3 + channel] + (rgb[d * 3 + channel] - rgb[c * 3 + channel]) * u;
                into[offset + channel] = (byte)Math.Max(0, Math.Min(255, Math.Round(top + (bottom - top) * v)));
            }
        }

        /// <summary>The Terrarium height at a Web Mercator position, bilinear between the pixels' heights.</summary>
        public double Height(OmsiMapPoint mercator)
        {
            Corners(mercator, out var a, out var b, out var c, out var d, out var u, out var v);
            double At(int pixel)
            {
                return OmsiImagerySource.TerrariumHeight(rgb[pixel * 3], rgb[pixel * 3 + 1], rgb[pixel * 3 + 2]);
            }
            var top = At(a) + (At(b) - At(a)) * u;
            var bottom = At(c) + (At(d) - At(c)) * u;
            return top + (bottom - top) * v;
        }
    }

    /// <summary>
    /// A map tile's picture from the air and its terrain heights from the world, as openMapper makes them
    /// (`src/geo/imagery.rs`, `src/geo/elevation.rs`): each pixel and each terrain point read at its own world position,
    /// so two tiles side by side meet without a seam.
    /// </summary>
    public static class OmsiAerial
    {
        /// <summary>The Web Mercator box of a map tile, a little wider for the pixels at its rim.</summary>
        public static void TileBox(OmsiGeoreference geo, int tileX, int tileY, double tileSize, double reach, out OmsiMapPoint low, out OmsiMapPoint high)
        {
            double lowX = double.MaxValue, lowY = double.MaxValue, highX = double.MinValue, highY = double.MinValue;
            for (var i = 0; i <= 8; i++)
                for (var j = 0; j <= 8; j++)
                {
                    if (i != 0 && i != 8 && j != 0 && j != 8)
                        continue;
                    foreach (var dx in new[] { -reach, reach })
                        foreach (var dy in new[] { -reach, reach })
                        {
                            var point = geo.Mercator(tileX * tileSize + i * tileSize / 8.0 + dx, tileY * tileSize + j * tileSize / 8.0 + dy);
                            lowX = Math.Min(lowX, point.X);
                            lowY = Math.Min(lowY, point.Y);
                            highX = Math.Max(highX, point.X);
                            highY = Math.Max(highY, point.Y);
                        }
                }
            var padX = (highX - lowX) * 0.01;
            var padY = (highY - lowY) * 0.01;
            low = new OmsiMapPoint(lowX - padX, lowY - padY);
            high = new OmsiMapPoint(highX + padX, highY + padY);
        }

        /// <summary>The edge of a tile's picture for imagery of some metres a pixel: every source pixel kept, in steps of 256, 512 to 4096.</summary>
        public static int ImageSize(double tileSize, double metresPerPixel)
        {
            return (int)Math.Max(512, Math.Min(4096, Math.Ceiling(tileSize / Math.Max(0.01, metresPerPixel) / 256.0) * 256));
        }

        /// <summary>The picture of a map tile, some pixels a side, row 0 its north edge.</summary>
        public static OmsiRgbImage Render(OmsiGeoreference geo, int tileX, int tileY, double tileSize, int size, OmsiMosaic mosaic)
        {
            // The exact position every 16 pixels; between them the mapping is straight to far below a pixel.
            const int step = 16;
            var points = (size + step - 1) / step + 1;
            var grid = new OmsiMapPoint[points * points];
            for (var j = 0; j < points; j++)
                for (var i = 0; i < points; i++)
                    grid[j * points + i] = geo.Mercator(tileX * tileSize + (double)(i * step) / size * tileSize,
                        tileY * tileSize + (1.0 - (double)(j * step) / size) * tileSize);
            var rgb = new byte[size * size * 3];
            Parallel.For(0, size, y =>
            {
                var fy = (y + 0.5) / step;
                var gy = Math.Min((int)Math.Floor(fy), points - 2);
                var v = fy - gy;
                for (var x = 0; x < size; x++)
                {
                    var fx = (x + 0.5) / step;
                    var gx = Math.Min((int)Math.Floor(fx), points - 2);
                    var u = fx - gx;
                    var a = grid[gy * points + gx];
                    var b = grid[gy * points + gx + 1];
                    var c = grid[(gy + 1) * points + gx];
                    var d = grid[(gy + 1) * points + gx + 1];
                    var topX = a.X + (b.X - a.X) * u;
                    var topY = a.Y + (b.Y - a.Y) * u;
                    var bottomX = c.X + (d.X - c.X) * u;
                    var bottomY = c.Y + (d.Y - c.Y) * u;
                    mosaic.Colour(new OmsiMapPoint(topX + (bottomX - topX) * v, topY + (bottomY - topY) * v), rgb, (y * size + x) * 3);
                }
            });
            return new OmsiRgbImage(size, size, rgb);
        }

        /// <summary>The coarsest zoom whose pixels are no larger than a spacing at a latitude, at most a maximum.</summary>
        public static int HeightZoom(double lat, double spacing, int max)
        {
            for (var zoom = 0; zoom <= max; zoom++)
                if (OmsiGeoreference.MetresPerPixel(lat, zoom) <= spacing)
                    return zoom;
            return max;
        }

        /// <summary>
        /// The heights above the sea of a map tile's 61 × 61 terrain points, row 0 its south edge, each smoothed with a
        /// 3 × 3 binomial kernel of some metres' reach (0 for none). A point two tiles share is read at the same place from
        /// both, so they get the same height.
        /// </summary>
        public static float[] SampleHeights(OmsiGeoreference geo, int tileX, int tileY, double tileSize, int zoom, double smoothing,
            Func<int, int, int, OmsiRgbImage> tile, CancellationToken cancel)
        {
            const int cells = OmsiTerrain.Resolution - 1;
            TileBox(geo, tileX, tileY, tileSize, Math.Max(0.0, smoothing) + tileSize / cells, out var low, out var high);
            var mosaic = OmsiMosaic.Fetch(zoom, low, high, true, tile, cancel);
            var heights = new float[OmsiTerrain.Resolution * OmsiTerrain.Resolution];
            for (var row = 0; row < OmsiTerrain.Resolution; row++)
                for (var column = 0; column < OmsiTerrain.Resolution; column++)
                {
                    var x = ((long)tileX * cells + column) * tileSize / cells;
                    var y = ((long)tileY * cells + row) * tileSize / cells;
                    heights[row * OmsiTerrain.Resolution + column] = (float)Smoothed(x, y, smoothing, (px, py) => mosaic.Height(geo.Mercator(px, py)));
                }
            return heights;
        }

        /// <summary>A height smoothed with a 3 × 3 binomial kernel, its points half the reach apart.</summary>
        public static double Smoothed(double x, double y, double reach, Func<double, double, double> at)
        {
            if (reach <= 0.0)
                return at(x, y);
            var weights = new[] { 1.0, 2.0, 1.0 };
            var sum = 0.0;
            for (var j = 0; j < 3; j++)
                for (var i = 0; i < 3; i++)
                    sum += at(x + (i - 1) * reach * 0.5, y + (j - 1) * reach * 0.5) * weights[i] * weights[j];
            return sum / 16.0;
        }
    }

    /// <summary>What the imagery of a map tile was made from, kept beside its picture.</summary>
    public sealed class OmsiImageryMeta
    {
        public string Source { get; set; }
        public int Zoom { get; set; }
        public string Stamp { get; set; }

        /// <summary>Some of it came from a coarser zoom.</summary>
        public bool Coarser { get; set; }

        public override string ToString()
        {
            return "source " + Source + "\nzoom " + Zoom.ToString(CultureInfo.InvariantCulture) + "\nstamp " + Stamp + "\ncoarser " + (Coarser ? 1 : 0) + "\n";
        }

        public static OmsiImageryMeta Parse(string text)
        {
            var meta = new OmsiImageryMeta();
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var trimmed = line.Trim();
                var space = trimmed.IndexOf(' ');
                if (space <= 0)
                    continue;
                var value = trimmed.Substring(space + 1);
                switch (trimmed.Substring(0, space))
                {
                    case "source": meta.Source = value; break;
                    case "zoom": meta.Zoom = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom) ? zoom : 0; break;
                    case "stamp": meta.Stamp = value; break;
                    case "coarser": meta.Coarser = value == "1"; break;
                }
            }
            return meta;
        }
    }

    /// <summary>
    /// Imagery and heights fetched for tiles in the background, as openMapper's geo job: one source and one zoom for all
    /// tiles, the sharpest source at the map's middle unless one is chosen, each tile tried again twice before it is given
    /// up. What comes out is handed on through the callbacks, on the job's thread.
    /// </summary>
    public sealed class OmsiAerialJob
    {
        public OmsiGeoreference Geo { get; set; }
        public double TileSize { get; set; }

        /// <summary>The tiles to fetch for, the middle first: its mean height is taken off for relative heights.</summary>
        public List<OmsiTileSummary> Tiles { get; } = new List<OmsiTileSummary>();

        /// <summary>The sources to choose the sharpest from, or one to take; empty for no imagery.</summary>
        public List<OmsiImagerySource> ImagerySources { get; } = new List<OmsiImagerySource>();

        /// <summary>Terrain heights too, with these options; null for none.</summary>
        public OmsiGeoHeightOptions Heights { get; set; }

        public OmsiTileFetcher Fetcher { get; set; }

        /// <summary>The largest edge of a tile's picture, however fine the source.</summary>
        public int MaxImageSize { get; set; } = 4096;

        /// <summary>The edge of every tile's picture; 0 for as many pixels as the source has, up to <see cref="MaxImageSize"/>.</summary>
        public int PictureSize { get; set; }

        /// <summary>The letters for the names on the drawn OpenStreetMap map; null for a map without names.</summary>
        public OmsiTrueTypeFont Font { get; set; }

        /// <summary>Turns a picture's file (JPEG or PNG) into pixels; null where it cannot.</summary>
        public Func<byte[], OmsiRgbImage> Decode { get; set; }

        public Action<string> Status { get; set; }
        public Action<OmsiTileSummary, OmsiRgbImage, OmsiImageryMeta> ImageReady { get; set; }

        /// <summary>A tile's heights, offsets applied, 61 × 61 from the south.</summary>
        public Action<OmsiTileSummary, float[]> HeightsReady { get; set; }

        public Action<OmsiTileSummary, string> TileFailed { get; set; }

        /// <summary>The source and zoom chosen, and its detail in metres a pixel.</summary>
        public OmsiImagerySource ChosenSource { get; private set; }

        /// <summary>Why sources were passed over in looking for the sharpest: those that did not answer properly.</summary>
        public List<string> PassedOver { get; } = new List<string>();

        /// <summary>The height relative heights take off: the options' own, or the middle tile's mean height measured.</summary>
        public double? Base { get; private set; }

        public void Run(CancellationToken cancel)
        {
            if (Tiles.Count == 0)
                return;
            var middleTile = Tiles[0];
            var middle = Geo.LatLon((middleTile.X + 0.5) * TileSize, (middleTile.Y + 0.5) * TileSize);

            OmsiImagerySource source = null;
            var zoom = 0;
            var size = 0;
            List<OmsiOsmElement> osm = null;
            if (ImagerySources.Count == 1 && ImagerySources[0].Kind == OmsiImageryKind.OsmMap)
            {
                // One request for all the tiles, not one for each; if it does not answer, nothing more is asked.
                source = ChosenSource = ImagerySources[0];
                zoom = source.MaxZoom;
                size = PictureSize > 0 ? PictureSize : Math.Min(MaxImageSize, OmsiAerial.ImageSize(TileSize, OmsiGeoreference.MetresPerPixel(middle.Latitude, zoom)));
                Status?.Invoke("Asking OpenStreetMap for the streets, houses and stops: this can take a minute for a large area; the answer is then kept for a week...");
                var box = OmsiOsmMap.Box(Geo, Tiles, TileSize);
                osm = OmsiOsmMap.Parse(Fetcher.Overpass(OmsiOsmMap.Query(box[0], box[1], box[2], box[3]), cancel));
                Status?.Invoke(source.Name + ": " + osm.Count + " things from OpenStreetMap, " + size + " px a tile");
            }
            else if (ImagerySources.Count > 0)
            {
                Status?.Invoke("Looking for the sharpest imagery here...");
                var found = new List<KeyValuePair<OmsiImagerySource, double?>>();
                var zooms = new Dictionary<OmsiImagerySource, int>();
                var silent = new List<string>();
                foreach (var candidate in ImagerySources)
                {
                    int? finest;
                    try
                    {
                        finest = Fetcher.FinestZoom(candidate, middle.Latitude, middle.Longitude, cancel);
                    }
                    catch (IOException exception) when (ImagerySources.Count > 1)
                    {
                        // A source that does not answer properly is passed over; the others may.
                        silent.Add(exception.Message);
                        continue;
                    }
                    if (!finest.HasValue)
                        continue;
                    zooms[candidate] = finest.Value;
                    found.Add(new KeyValuePair<OmsiImagerySource, double?>(candidate, candidate.MetresPerPixel(middle.Latitude, finest.Value)));
                }
                var ranked = OmsiImagerySource.Rank(found);
                if (ranked.Count == 0)
                    throw new InvalidOperationException("No source has imagery here." + (silent.Count > 0 ? " " + string.Join(" ", silent) : string.Empty));
                PassedOver.AddRange(silent);
                source = ranked[0].Key;
                zoom = zooms[source];
                size = PictureSize > 0 ? PictureSize : Math.Min(MaxImageSize, OmsiAerial.ImageSize(TileSize, source.MetresPerPixel(middle.Latitude, zoom)));
                ChosenSource = source;
                Status?.Invoke(source.Name + ", zoom " + zoom + ", " + source.MetresPerPixel(middle.Latitude, zoom).ToString("0.00", CultureInfo.InvariantCulture) +
                               " m a pixel, " + size + " px a tile" + (silent.Count > 0 ? "; passed over: " + string.Join(" ", silent) : string.Empty));
            }

            var heightZoom = 0;
            if (Heights != null)
            {
                heightZoom = OmsiAerial.HeightZoom(middle.Latitude, TileSize / (OmsiTerrain.Resolution - 1), OmsiImagerySource.Terrain.MaxZoom);
                if (Heights.Relative && Heights.Base.HasValue)
                    Base = Heights.Base;
                else if (Heights.Relative)
                {
                    var raw = OmsiAerial.SampleHeights(Geo, middleTile.X, middleTile.Y, TileSize, heightZoom, 0.0, TerrainTile(cancel), cancel);
                    var sum = 0.0;
                    foreach (var height in raw)
                        sum += height;
                    Base = Math.Round(sum / raw.Length * 100.0) / 100.0;
                }
            }

            var done = 0;
            foreach (var tile in Tiles)
            {
                cancel.ThrowIfCancellationRequested();
                Status?.Invoke("Tile " + tile.X + ", " + tile.Y + " (" + (done + 1) + " of " + Tiles.Count + ")");
                Exception last = null;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (attempt > 0)
                            Thread.Sleep(800 * attempt);
                        if (osm != null)
                        {
                            var picture = OmsiOsmMap.Build(Geo, tile.X, tile.Y, TileSize, size, osm, Font, cancel);
                            ImageReady?.Invoke(tile, picture, new OmsiImageryMeta { Source = source.Name, Zoom = zoom, Stamp = Geo.Stamp });
                        }
                        else if (source != null)
                        {
                            TileFor(source, zoom, size, tile, cancel);
                        }
                        if (Heights != null)
                        {
                            var raw = OmsiAerial.SampleHeights(Geo, tile.X, tile.Y, TileSize, heightZoom, Heights.Smoothing, TerrainTile(cancel), cancel);
                            for (var index = 0; index < raw.Length; index++)
                                raw[index] = (float)(raw[index] - (Base ?? 0.0) + Heights.Offset);
                            HeightsReady?.Invoke(tile, raw);
                        }
                        last = null;
                        break;
                    }
                    catch (Exception exception) when (!(exception is OperationCanceledException))
                    {
                        last = exception;
                    }
                }
                if (last != null)
                    TileFailed?.Invoke(tile, last.Message);
                done++;
            }
        }

        private void TileFor(OmsiImagerySource source, int zoom, int size, OmsiTileSummary tile, CancellationToken cancel)
        {
            OmsiAerial.TileBox(Geo, tile.X, tile.Y, TileSize, 0.0, out var low, out var high);
            var mosaic = OmsiMosaic.Fetch(zoom, low, high, false, (z, x, y) =>
            {
                var bytes = Fetcher.Tile(source, z, x, y, cancel);
                return bytes != null ? Decode(bytes) : null;
            }, cancel);
            var picture = OmsiAerial.Render(Geo, tile.X, tile.Y, TileSize, size, mosaic);
            ImageReady?.Invoke(tile, picture, new OmsiImageryMeta { Source = source.Name, Zoom = zoom, Stamp = Geo.Stamp, Coarser = mosaic.Coarser });
        }

        private Func<int, int, int, OmsiRgbImage> TerrainTile(CancellationToken cancel)
        {
            return (z, x, y) =>
            {
                var bytes = Fetcher.Tile(OmsiImagerySource.Terrain, z, x, y, cancel);
                return bytes != null ? OmsiPng.DecodeRgb(bytes) : null;
            };
        }
    }
}
