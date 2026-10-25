using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// One of the user's own pictures laid on tiles (a scan, a screenshot, an old plan), as openMapper lays them
    /// (`src/geo/own.rs`): no place on the Earth needed. It covers a rectangle of tiles, stretched to it, and can be turned,
    /// mirrored, moved and scaled by eye; each tile shows its own piece of it.
    /// </summary>
    public sealed class OmsiOwnPicture
    {
        /// <summary>A name without spaces, also the start of its file's name.</summary>
        public string Id { get; set; }

        /// <summary>The copy's file name in the project's Mapforge\Pictures folder.</summary>
        public string File { get; set; }

        /// <summary>The tiles it covers, both ends included.</summary>
        public int West { get; set; }
        public int South { get; set; }
        public int East { get; set; }
        public int North { get; set; }

        /// <summary>Turned clockwise in degrees.</summary>
        public double Rotation { get; set; }

        public bool MirrorX { get; set; }
        public bool MirrorY { get; set; }

        /// <summary>Moved east and north in metres.</summary>
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }

        /// <summary>Size against the tiles' rectangle, 1 for stretched to it.</summary>
        public double Scale { get; set; } = 1.0;

        public bool Covers(int tileX, int tileY)
        {
            return tileX >= West && tileX <= East && tileY >= South && tileY <= North;
        }

        public IEnumerable<KeyValuePair<int, int>> Tiles()
        {
            for (var y = South; y <= North; y++)
                for (var x = West; x <= East; x++)
                    yield return new KeyValuePair<int, int>(x, y);
        }

        /// <summary>
        /// The picture's middle in world metres, its width and height and its turn in radians clockwise. A quarter turn
        /// swaps the sides, so the turned picture still fills the rectangle.
        /// </summary>
        public void Frame(double tileSize, out double middleX, out double middleY, out double width, out double height, out double turn)
        {
            double lowX = West * tileSize, lowY = South * tileSize, highX = (East + 1) * tileSize, highY = (North + 1) * tileSize;
            var quarter = (long)Math.Round(Rotation / 90.0);
            var odd = ((quarter % 2) + 2) % 2 == 1;
            var scale = Math.Max(1e-3, Scale);
            width = (odd ? highY - lowY : highX - lowX) * scale;
            height = (odd ? highX - lowX : highY - lowY) * scale;
            middleX = (lowX + highX) * 0.5 + OffsetX;
            middleY = (lowY + highY) * 0.5 + OffsetY;
            turn = Rotation * Math.PI / 180.0;
        }

        /// <summary>Where a world point falls in the picture: (0, 0) its top left corner, (1, 1) its bottom right; false outside.</summary>
        public bool Uv(double tileSize, double x, double y, out double u, out double v)
        {
            Frame(tileSize, out var middleX, out var middleY, out var width, out var height, out var turn);
            return Uv(middleX, middleY, width, height, Math.Sin(turn), Math.Cos(turn), x, y, out u, out v);
        }

        private bool Uv(double middleX, double middleY, double width, double height, double sin, double cos, double x, double y, out double u, out double v)
        {
            var dx = x - middleX;
            var dy = y - middleY;
            // Into the picture's own axes: it was turned clockwise, so turn back.
            var localX = dx * cos - dy * sin;
            var localY = dx * sin + dy * cos;
            u = localX / width + 0.5;
            v = 0.5 - localY / height;
            if (MirrorX)
                u = 1.0 - u;
            if (MirrorY)
                v = 1.0 - v;
            return u >= 0.0 && u <= 1.0 && v >= 0.0 && v <= 1.0;
        }

        /// <summary>Pixels a side of a tile's piece that keep the picture's own detail, in steps of 256 from 256 to a maximum.</summary>
        public int PieceSize(double tileSize, int pictureWidth, int maximum = 4096)
        {
            Frame(tileSize, out _, out _, out var width, out _, out _);
            var pixelsPerMetre = pictureWidth / Math.Max(1e-3, width);
            return (int)Math.Max(256, Math.Min(maximum, Math.Ceiling(tileSize * pixelsPerMetre / 256.0) * 256));
        }

        /// <summary>
        /// A tile's piece of the picture (RGBA, rows from the top), some pixels a side, row 0 the tile's north edge;
        /// transparent where the picture does not reach.
        /// </summary>
        public OmsiImage Cut(OmsiImage picture, int tileX, int tileY, double tileSize, int size)
        {
            if (picture == null || picture.Channels != 4)
                throw new ArgumentException("An RGBA picture is required.", nameof(picture));
            Frame(tileSize, out var middleX, out var middleY, out var width, out var height, out var turn);
            var sin = Math.Sin(turn);
            var cos = Math.Cos(turn);
            var pixels = new byte[size * size * 4];
            Parallel.For(0, size, row =>
            {
                for (var column = 0; column < size; column++)
                {
                    var x = tileX * tileSize + (column + 0.5) / size * tileSize;
                    var y = tileY * tileSize + (1.0 - (row + 0.5) / size) * tileSize;
                    if (Uv(middleX, middleY, width, height, sin, cos, x, y, out var u, out var v))
                        Bilinear(picture, u * picture.Width - 0.5, v * picture.Height - 0.5, pixels, (row * size + column) * 4);
                }
            });
            return new OmsiImage(size, size, 4, pixels);
        }

        private static void Bilinear(OmsiImage picture, double fx, double fy, byte[] into, int offset)
        {
            var ix = (int)Math.Floor(fx);
            var iy = (int)Math.Floor(fy);
            var u = fx - ix;
            var v = fy - iy;
            int At(int x, int y)
            {
                return (Math.Max(0, Math.Min(picture.Height - 1, y)) * picture.Width + Math.Max(0, Math.Min(picture.Width - 1, x))) * 4;
            }
            int a = At(ix, iy), b = At(ix + 1, iy), c = At(ix, iy + 1), d = At(ix + 1, iy + 1);
            var source = picture.Pixels;
            for (var channel = 0; channel < 4; channel++)
            {
                var top = source[a + channel] + (source[b + channel] - source[a + channel]) * u;
                var bottom = source[c + channel] + (source[d + channel] - source[c + channel]) * u;
                into[offset + channel] = (byte)Math.Round(top + (bottom - top) * v);
            }
        }

        public OmsiOwnPicture Clone()
        {
            return (OmsiOwnPicture)MemberwiseClone();
        }

        /// <summary>Back to stretched over its rectangle, unturned and unmirrored.</summary>
        public void ResetFit()
        {
            Rotation = 0.0;
            MirrorX = MirrorY = false;
            OffsetX = OffsetY = 0.0;
            Scale = 1.0;
        }
    }

    /// <summary>
    /// The project's own pictures, kept in the workspace under Mapforge\Pictures (never exported): the copies of the
    /// files and pictures.txt with one "picture" line for each.
    /// </summary>
    public static class OmsiOwnPictures
    {
        public const string Folder = OmsiMapExporter.ProjectDataFolder + "\\Pictures";
        public const string ListPath = Folder + "\\pictures.txt";

        /// <summary>The picture files openMapper takes that the editor can read.</summary>
        public static readonly string[] Extensions = { "jpg", "jpeg", "png", "bmp", "tga" };

        public static List<OmsiOwnPicture> Parse(string text)
        {
            var pictures = new List<OmsiOwnPicture>();
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 13 || parts[0] != "picture")
                    continue;
                int Whole(int index)
                {
                    return int.Parse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                double Number(int index)
                {
                    return double.Parse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                try
                {
                    pictures.Add(new OmsiOwnPicture
                    {
                        Id = parts[1], File = parts[2], West = Whole(3), South = Whole(4), East = Whole(5), North = Whole(6), Rotation = Number(7),
                        MirrorX = parts[8] == "1", MirrorY = parts[9] == "1", OffsetX = Number(10), OffsetY = Number(11), Scale = Number(12)
                    });
                }
                catch (FormatException)
                {
                    // A damaged line is left out; the others stay.
                }
            }
            return pictures;
        }

        public static string Write(IEnumerable<OmsiOwnPicture> pictures)
        {
            var text = new StringBuilder();
            foreach (var picture in pictures)
            {
                text.Append("picture ").Append(picture.Id).Append(' ').Append(picture.File);
                foreach (var value in new double[] { picture.West, picture.South, picture.East, picture.North, picture.Rotation })
                    text.Append(' ').Append(value.ToString("R", CultureInfo.InvariantCulture));
                text.Append(picture.MirrorX ? " 1" : " 0").Append(picture.MirrorY ? " 1" : " 0");
                foreach (var value in new[] { picture.OffsetX, picture.OffsetY, picture.Scale })
                    text.Append(' ').Append(value.ToString("R", CultureInfo.InvariantCulture));
                text.Append('\n');
            }
            return text.ToString();
        }

        /// <summary>A new picture's name, from its first tile, not yet taken.</summary>
        public static string NewId(IEnumerable<OmsiOwnPicture> pictures, int west, int south)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var picture in pictures)
                taken.Add(picture.Id);
            var id = "own_" + west.ToString(CultureInfo.InvariantCulture) + "_" + south.ToString(CultureInfo.InvariantCulture);
            var number = 2;
            var candidate = id;
            while (taken.Contains(candidate))
                candidate = id + "_" + (number++).ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        /// <summary>
        /// The picture a tile shows: of those covering it the one added last, as a new picture takes over the tiles it
        /// covers; null for none.
        /// </summary>
        public static OmsiOwnPicture On(IReadOnlyList<OmsiOwnPicture> pictures, int tileX, int tileY)
        {
            for (var index = pictures.Count - 1; index >= 0; index--)
                if (pictures[index].Covers(tileX, tileY))
                    return pictures[index];
            return null;
        }
    }
}
