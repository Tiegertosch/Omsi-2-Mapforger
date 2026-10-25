using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A picture read as grey values from 0 to 1, row 0 at the top.</summary>
    public sealed class OmsiGreyPicture
    {
        public OmsiGreyPicture(int width, int height, float[] values, bool coarse)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (values == null || values.Length != width * height)
                throw new ArgumentException("One value per pixel is required.", nameof(values));
            Width = width;
            Height = height;
            Values = values;
            Coarse = coarse;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Row by row from the top, left to right.</summary>
        public float[] Values { get; }

        /// <summary>Fewer than 16 bits a channel: the heights it gives step, and smoothing helps.</summary>
        public bool Coarse { get; }

        /// <summary>Passes of a 3 × 3 average, as openMapper smooths a picture before laying it on the ground.</summary>
        public void Smooth(int passes)
        {
            for (var pass = 0; pass < passes; pass++)
            {
                var source = (float[])Values.Clone();
                for (var y = 0; y < Height; y++)
                    for (var x = 0; x < Width; x++)
                    {
                        var sum = 0.0;
                        var count = 0;
                        for (var yy = Math.Max(0, y - 1); yy <= Math.Min(Height - 1, y + 1); yy++)
                            for (var xx = Math.Max(0, x - 1); xx <= Math.Min(Width - 1, x + 1); xx++)
                            {
                                sum += source[yy * Width + xx];
                                count++;
                            }
                        Values[y * Width + x] = (float)(sum / count);
                    }
            }
        }

        /// <summary>The value at (u, v), 0 to 1 across from the left and down from the top, between the pixels.</summary>
        public double At(double u, double v)
        {
            var fx = Math.Max(0.0, Math.Min(1.0, u)) * (Width - 1);
            var fy = Math.Max(0.0, Math.Min(1.0, v)) * (Height - 1);
            var x0 = (int)Math.Floor(fx);
            var y0 = (int)Math.Floor(fy);
            var x1 = Math.Min(x0 + 1, Width - 1);
            var y1 = Math.Min(y0 + 1, Height - 1);
            var ax = fx - x0;
            var ay = fy - y0;
            var top = Values[y0 * Width + x0] * (1.0 - ax) + Values[y0 * Width + x1] * ax;
            var bottom = Values[y1 * Width + x0] * (1.0 - ax) + Values[y1 * Width + x1] * ax;
            return top * (1.0 - ay) + bottom * ay;
        }
    }

    /// <summary>
    /// Reads and writes PNG pictures as grey values, for height maps: 16-bit grey is written; grey, grey with alpha, RGB,
    /// RGBA (8 or 16 bits) and 8-bit palettes are read, colours as their luma (Rec. 709, as openMapper's image library
    /// takes them). Interlaced pictures are not read.
    /// </summary>
    public static class OmsiPng
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static uint[] crcTable;

        /// <summary>Writes 16-bit grey pixels, row by row from the top.</summary>
        public static byte[] EncodeGrey16(int width, int height, ushort[] pixels)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (pixels == null || pixels.Length != width * height)
                throw new ArgumentException("One value per pixel is required.", nameof(pixels));
            var raw = new byte[height * (1 + width * 2)];
            for (var y = 0; y < height; y++)
            {
                var row = y * (1 + width * 2);
                raw[row] = 0;
                for (var x = 0; x < width; x++)
                {
                    var value = pixels[y * width + x];
                    raw[row + 1 + x * 2] = (byte)(value >> 8);
                    raw[row + 2 + x * 2] = (byte)value;
                }
            }
            using (var output = new MemoryStream())
            {
                output.Write(Signature, 0, Signature.Length);
                var header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 16;
                header[9] = 0;
                WriteChunk(output, "IHDR", header);
                WriteChunk(output, "IDAT", Zlib(raw));
                WriteChunk(output, "IEND", new byte[0]);
                return output.ToArray();
            }
        }

        /// <summary>Reads a PNG as grey values from 0 to 1.</summary>
        public static OmsiGreyPicture DecodeGrey(byte[] bytes)
        {
            float[] values = null;
            var width = 0;
            var depth = Read(bytes, (w, h) =>
            {
                width = w;
                values = new float[w * h];
            }, (x, y, red, green, blue) => values[y * width + x] = (float)(0.2126 * red + 0.7152 * green + 0.0722 * blue), out var height);
            return new OmsiGreyPicture(width, height, values, depth < 16);
        }

        /// <summary>Reads a PNG as 8-bit red, green and blue, as Terrarium height tiles are kept.</summary>
        public static OmsiRgbImage DecodeRgb(byte[] bytes)
        {
            byte[] rgb = null;
            var width = 0;
            Read(bytes, (w, h) =>
            {
                width = w;
                rgb = new byte[w * h * 3];
            }, (x, y, red, green, blue) =>
            {
                var index = (y * width + x) * 3;
                rgb[index] = (byte)Math.Round(red * 255.0);
                rgb[index + 1] = (byte)Math.Round(green * 255.0);
                rgb[index + 2] = (byte)Math.Round(blue * 255.0);
            }, out var height);
            return new OmsiRgbImage(width, height, rgb);
        }

        /// <summary>Reads a PNG pixel by pixel as red, green and blue from 0 to 1, rows from the top; returns its bits a channel.</summary>
        private static int Read(byte[] bytes, Action<int, int> size, Action<int, int, double, double, double> pixel, out int height)
        {
            if (bytes == null || bytes.Length < Signature.Length + 12)
                throw new InvalidDataException("The file is too short for a PNG picture.");
            for (var index = 0; index < Signature.Length; index++)
                if (bytes[index] != Signature[index])
                    throw new InvalidDataException("The file is not a PNG picture.");

            int width = 0, depth = 0, colour = -1;
            height = 0;
            byte[] palette = null;
            var data = new MemoryStream();
            var position = Signature.Length;
            while (position + 8 <= bytes.Length)
            {
                var length = (int)ReadBigEndian(bytes, position);
                var type = System.Text.Encoding.ASCII.GetString(bytes, position + 4, 4);
                var start = position + 8;
                if (length < 0 || start + length > bytes.Length)
                    throw new InvalidDataException("A PNG chunk runs past the end of the file.");
                switch (type)
                {
                    case "IHDR":
                        width = (int)ReadBigEndian(bytes, start);
                        height = (int)ReadBigEndian(bytes, start + 4);
                        depth = bytes[start + 8];
                        colour = bytes[start + 9];
                        if (bytes[start + 12] != 0)
                            throw new InvalidDataException("Interlaced PNG pictures are not supported; save it without interlacing.");
                        break;
                    case "PLTE":
                        palette = new byte[length];
                        Array.Copy(bytes, start, palette, 0, length);
                        break;
                    case "IDAT":
                        data.Write(bytes, start, length);
                        break;
                }
                position = start + length + 4;
                if (type == "IEND")
                    break;
            }
            if (width < 1 || height < 1)
                throw new InvalidDataException("The PNG picture has no size.");
            int channels;
            switch (colour)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: throw new InvalidDataException("The PNG colour type " + colour + " is not supported.");
            }
            if (depth != 8 && depth != 16 || colour == 3 && depth != 8)
                throw new InvalidDataException("PNG pictures with " + depth + " bits a channel are not supported; use 8 or 16.");
            if (colour == 3 && palette == null)
                throw new InvalidDataException("The PNG picture has no palette.");

            var bytesPerPixel = channels * depth / 8;
            var stride = width * bytesPerPixel;
            var raw = Inflate(data.ToArray());
            if (raw.Length < height * (stride + 1))
                throw new InvalidDataException("The PNG picture's data is incomplete.");
            size(width, height);
            var previous = new byte[stride];
            var current = new byte[stride];
            for (var y = 0; y < height; y++)
            {
                var row = y * (stride + 1);
                Unfilter(raw[row], raw, row + 1, current, previous, bytesPerPixel);
                for (var x = 0; x < width; x++)
                {
                    Colour(current, x * bytesPerPixel, colour, depth, palette, out var red, out var green, out var blue);
                    pixel(x, y, red, green, blue);
                }
                var swap = previous;
                previous = current;
                current = swap;
            }
            return depth;
        }

        private static void Colour(byte[] row, int offset, int colour, int depth, byte[] palette, out double red, out double green, out double blue)
        {
            double Sample(int channel)
            {
                return depth == 16 ? (row[offset + channel * 2] << 8 | row[offset + channel * 2 + 1]) / 65535.0 : row[offset + channel] / 255.0;
            }
            switch (colour)
            {
                case 0:
                case 4:
                    red = green = blue = Sample(0);
                    return;
                case 3:
                    var entry = row[offset] * 3;
                    if (entry + 2 >= palette.Length)
                    {
                        red = green = blue = 0.0;
                        return;
                    }
                    red = palette[entry] / 255.0;
                    green = palette[entry + 1] / 255.0;
                    blue = palette[entry + 2] / 255.0;
                    return;
                default:
                    red = Sample(0);
                    green = Sample(1);
                    blue = Sample(2);
                    return;
            }
        }

        private static void Unfilter(byte filter, byte[] raw, int start, byte[] current, byte[] previous, int bytesPerPixel)
        {
            for (var index = 0; index < current.Length; index++)
            {
                var left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
                var up = previous[index];
                var upLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : 0;
                int predictor;
                switch (filter)
                {
                    case 0: predictor = 0; break;
                    case 1: predictor = left; break;
                    case 2: predictor = up; break;
                    case 3: predictor = (left + up) / 2; break;
                    case 4: predictor = Paeth(left, up, upLeft); break;
                    default: throw new InvalidDataException("The PNG picture uses an unknown filter " + filter + ".");
                }
                current[index] = (byte)(raw[start + index] + predictor);
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        /// <summary>A zlib stream's data, as PNG and TIFF's Deflate keep it.</summary>
        internal static byte[] Inflate(byte[] zlib)
        {
            if (zlib.Length < 2)
                throw new InvalidDataException("The PNG picture has no data.");
            // A zlib stream: two header bytes, raw deflate, four bytes of checksum.
            using (var input = new MemoryStream(zlib, 2, zlib.Length - 2))
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                inflate.CopyTo(output);
                return output.ToArray();
            }
        }

        private static byte[] Zlib(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                    deflate.Write(raw, 0, raw.Length);
                uint a = 1, b = 0;
                foreach (var value in raw)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }
                var checksum = new byte[4];
                WriteBigEndian(checksum, 0, b << 16 | a);
                output.Write(checksum, 0, 4);
                return output.ToArray();
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var header = new byte[8];
            WriteBigEndian(header, 0, (uint)data.Length);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            Array.Copy(typeBytes, 0, header, 4, 4);
            output.Write(header, 0, 8);
            output.Write(data, 0, data.Length);
            var crc = new List<byte>(typeBytes);
            crc.AddRange(data);
            var checksum = new byte[4];
            WriteBigEndian(checksum, 0, Crc(crc));
            output.Write(checksum, 0, 4);
        }

        private static uint Crc(List<byte> data)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    var c = n;
                    for (var k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }
            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
                crc = crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint ReadBigEndian(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
        }

        private static void WriteBigEndian(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }
}
