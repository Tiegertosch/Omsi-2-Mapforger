using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Reads TIFF pictures as height maps, as openMapper takes them through its picture library: one picture (the first),
    /// in strips or tiles, uncompressed, LZW, Deflate or PackBits, with or without a predictor; grey, RGB or a palette;
    /// 8, 16 or 32 bits a sample as unsigned or signed numbers or floats. A picture of real heights (signed or float
    /// samples, as GDAL and the geoportals write digital elevation models) brings its range in metres along; GDAL's
    /// no-data value is left out of it.
    /// </summary>
    public static class OmsiTiff
    {
        public static bool IsTiff(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 8 &&
                   (bytes[0] == 'I' && bytes[1] == 'I' && bytes[2] == 42 && bytes[3] == 0 || bytes[0] == 'M' && bytes[1] == 'M' && bytes[2] == 0 && bytes[3] == 42);
        }

        /// <summary>The picture as grey values from 0 to 1; <paramref name="heights"/> the range of real heights in metres, or null for a picture of brightness.</summary>
        /// <exception cref="InvalidDataException">Not a TIFF this reader can read.</exception>
        public static OmsiGreyPicture Decode(byte[] bytes, out OmsiHeightRange? heights)
        {
            heights = null;
            if (!IsTiff(bytes))
                throw new InvalidDataException(bytes != null && bytes.Length >= 4 && bytes[2] == 43 ? "BigTIFF pictures cannot be read; save it as a normal TIFF." : "The file is not a TIFF picture.");
            var reader = new Reader(bytes, bytes[0] == 'I');
            var tags = reader.Directory(reader.U32(4));
            int Single(int tag, int fallback)
            {
                return tags.TryGetValue(tag, out var values) && values.Length > 0 ? (int)values[0] : fallback;
            }
            var width = Single(256, 0);
            var height = Single(257, 0);
            if (width < 1 || height < 1)
                throw new InvalidDataException("The TIFF picture has no size.");
            var samples = Single(277, 1);
            var bits = tags.TryGetValue(258, out var bitList) && bitList.Length > 0 ? (int)bitList[0] : 1;
            var compression = Single(259, 1);
            var photometric = Single(262, 1);
            var planar = Single(284, 1);
            var predictor = Single(317, 1);
            var format = Single(339, 1);
            if (bits != 8 && bits != 16 && bits != 32 && !(bits == 64 && format == 3))
                throw new InvalidDataException("TIFF pictures of " + bits + " bits a sample cannot be read.");
            var bytesPerSample = bits / 8;
            // Planar pictures keep each sample in its own plane: the first is enough for a height map.
            var stored = planar == 2 ? 1 : samples;
            var pixelBytes = stored * bytesPerSample;

            bool tiled = tags.ContainsKey(322);
            int blockWidth = tiled ? Single(322, width) : width;
            int blockHeight = tiled ? Single(323, height) : Single(278, height);
            blockHeight = Math.Max(1, Math.Min(blockHeight, tiled ? blockHeight : height));
            if (!tags.TryGetValue(tiled ? 324 : 273, out var offsets) || !tags.TryGetValue(tiled ? 325 : 279, out var counts))
                throw new InvalidDataException("The TIFF picture has no data.");
            var across = tiled ? (width + blockWidth - 1) / blockWidth : 1;
            var down = (height + blockHeight - 1) / blockHeight;

            if ((long)width * height * pixelBytes > 1L << 30)
                throw new InvalidDataException("The TIFF picture is too large.");
            var raw = new byte[width * height * pixelBytes];
            for (var block = 0; block < Math.Min(offsets.Length, across * down); block++)
            {
                var offset = (int)offsets[block];
                var count = (int)Math.Min(counts[block], bytes.Length - offset);
                if (offset < 0 || count <= 0)
                    continue;
                var data = new byte[count];
                Array.Copy(bytes, offset, data, 0, count);
                var rowBytes = blockWidth * pixelBytes;
                var expected = rowBytes * blockHeight;
                var plain = Unpack(data, compression, expected);
                Unpredict(plain, predictor, blockWidth, blockHeight, stored, bytesPerSample, reader.Little);
                var left = (block % across) * blockWidth;
                var top = (block / across) * blockHeight;
                for (var row = 0; row < blockHeight && top + row < height; row++)
                {
                    var columns = Math.Min(blockWidth, width - left);
                    var from = row * rowBytes;
                    if (from + columns * pixelBytes > plain.Length)
                        break;
                    Array.Copy(plain, from, raw, ((top + row) * width + left) * pixelBytes, columns * pixelBytes);
                }
            }

            // The samples as numbers: one a pixel, the brightness of colour pictures.
            uint[] palette = null;
            if (photometric == 3 && tags.TryGetValue(320, out var map))
                palette = map;
            var values = new double[width * height];
            for (var pixel = 0; pixel < values.Length; pixel++)
            {
                var at = pixel * pixelBytes;
                double Sample(int index)
                {
                    return reader.Number(raw, at + index * bytesPerSample, bits, format);
                }
                double value;
                if (palette != null)
                {
                    var entry = (int)Sample(0);
                    var colours = palette.Length / 3;
                    value = entry < colours ? (0.2126 * palette[entry] + 0.7152 * palette[colours + entry] + 0.0722 * palette[2 * colours + entry]) / 65535.0 : 0.0;
                }
                else if (photometric == 2 && stored >= 3)
                    value = 0.2126 * Sample(0) + 0.7152 * Sample(1) + 0.0722 * Sample(2);
                else
                    value = Sample(0);
                values[pixel] = value;
            }

            var real = format == 2 || format == 3;
            double? noData = null;
            if (reader.Text(42113) is string said &&
                double.TryParse(said.Trim('\0', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var given))
                noData = given;
            double low = double.MaxValue, high = double.MinValue;
            foreach (var value in values)
                if (!double.IsNaN(value) && !(noData.HasValue && Math.Abs(value - noData.Value) < 1e-6) && Math.Abs(value) < 1e7)
                {
                    low = Math.Min(low, value);
                    high = Math.Max(high, value);
                }
            if (low > high)
                low = high = 0.0;
            double scaleLow, scaleHigh;
            if (real)
            {
                // Heights in metres: the picture spans its own range.
                scaleLow = low;
                scaleHigh = Math.Max(high, low + 0.01);
                heights = new OmsiHeightRange { Lowest = scaleLow, Highest = scaleHigh };
            }
            else
            {
                scaleLow = 0.0;
                scaleHigh = palette != null ? 1.0 : Math.Pow(2.0, bits) - 1.0;
            }
            var grey = new float[values.Length];
            for (var pixel = 0; pixel < values.Length; pixel++)
            {
                var value = values[pixel];
                if (double.IsNaN(value) || noData.HasValue && Math.Abs(value - noData.Value) < 1e-6 || Math.Abs(value) >= 1e7)
                    value = scaleLow;
                grey[pixel] = (float)Math.Max(0.0, Math.Min(1.0, (value - scaleLow) / (scaleHigh - scaleLow)));
            }
            if (photometric == 0 && !real)
                for (var pixel = 0; pixel < grey.Length; pixel++)
                    grey[pixel] = 1f - grey[pixel];
            return new OmsiGreyPicture(width, height, grey, bits <= 8 && !real);
        }

        private static byte[] Unpack(byte[] data, int compression, int expected)
        {
            switch (compression)
            {
                case 1:
                    return data;
                case 5:
                    return Lzw(data, expected);
                case 8:
                case 32946:
                    return OmsiPng.Inflate(data);
                case 32773:
                    return PackBits(data, expected);
                default:
                    throw new InvalidDataException("TIFF compression " + compression + " cannot be read; save it uncompressed, LZW or Deflate.");
            }
        }

        /// <summary>TIFF's LZW: codes from 9 to 12 bits, most significant bit first, the code width growing one early.</summary>
        private static byte[] Lzw(byte[] data, int expected)
        {
            const int clear = 256, end = 257;
            var output = new List<byte>(expected);
            var table = new List<byte[]>(4096);
            void Reset()
            {
                table.Clear();
                for (var index = 0; index < 256; index++)
                    table.Add(new[] { (byte)index });
                table.Add(null);
                table.Add(null);
            }
            Reset();
            var width = 9;
            var bitPosition = 0L;
            byte[] previous = null;
            while (true)
            {
                if ((bitPosition + width + 7) / 8 > data.Length + 1)
                    break;
                var code = 0;
                for (var bit = 0; bit < width; bit++)
                {
                    var position = bitPosition + bit;
                    var index = (int)(position >> 3);
                    var value = index < data.Length ? (data[index] >> (7 - (int)(position & 7))) & 1 : 0;
                    code = (code << 1) | value;
                }
                bitPosition += width;
                if (code == end)
                    break;
                if (code == clear)
                {
                    Reset();
                    width = 9;
                    previous = null;
                    continue;
                }
                byte[] entry;
                if (code < table.Count && table[code] != null)
                    entry = table[code];
                else if (previous != null && code == table.Count)
                {
                    entry = new byte[previous.Length + 1];
                    Array.Copy(previous, entry, previous.Length);
                    entry[previous.Length] = previous[0];
                }
                else
                    throw new InvalidDataException("The TIFF picture's LZW data is damaged.");
                output.AddRange(entry);
                if (previous != null && table.Count < 4096)
                {
                    var added = new byte[previous.Length + 1];
                    Array.Copy(previous, added, previous.Length);
                    added[previous.Length] = entry[0];
                    table.Add(added);
                }
                previous = entry;
                // One early: the next code may already be one bit wider.
                if (table.Count + 1 >= 1 << width && width < 12)
                    width++;
            }
            return output.ToArray();
        }

        private static byte[] PackBits(byte[] data, int expected)
        {
            var output = new List<byte>(expected);
            for (var index = 0; index < data.Length && output.Count < expected;)
            {
                var header = (sbyte)data[index++];
                if (header >= 0)
                {
                    for (var copy = 0; copy <= header && index < data.Length; copy++)
                        output.Add(data[index++]);
                }
                else if (header != -128 && index < data.Length)
                {
                    var value = data[index++];
                    for (var copy = 0; copy < 1 - header; copy++)
                        output.Add(value);
                }
            }
            return output.ToArray();
        }

        /// <summary>Undoes the horizontal predictor (2) or the floating point predictor (3) of each row of a block.</summary>
        private static void Unpredict(byte[] data, int predictor, int width, int height, int samples, int bytesPerSample, bool little)
        {
            if (predictor == 1)
                return;
            var rowBytes = width * samples * bytesPerSample;
            for (var row = 0; row < height && (row + 1) * rowBytes <= data.Length; row++)
            {
                var start = row * rowBytes;
                if (predictor == 2)
                {
                    if (bytesPerSample == 1)
                    {
                        for (var index = samples; index < rowBytes; index++)
                            data[start + index] = (byte)(data[start + index] + data[start + index - samples]);
                    }
                    else if (bytesPerSample == 2)
                    {
                        for (var index = samples; index < width * samples; index++)
                        {
                            var at = start + index * 2;
                            var before = start + (index - samples) * 2;
                            int value = little ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1];
                            int previous = little ? data[before] | (data[before + 1] << 8) : (data[before] << 8) | data[before + 1];
                            var sum = (value + previous) & 0xFFFF;
                            data[at] = (byte)(little ? sum : sum >> 8);
                            data[at + 1] = (byte)(little ? sum >> 8 : sum);
                        }
                    }
                    else
                    {
                        for (var index = samples; index < width * samples; index++)
                        {
                            var at = start + index * 4;
                            var before = start + (index - samples) * 4;
                            uint Read(int position)
                            {
                                return little
                                    ? (uint)(data[position] | (data[position + 1] << 8) | (data[position + 2] << 16) | (data[position + 3] << 24))
                                    : (uint)((data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3]);
                            }
                            var sum = unchecked(Read(at) + Read(before));
                            for (var b = 0; b < 4; b++)
                                data[at + b] = (byte)(little ? sum >> (8 * b) : sum >> (8 * (3 - b)));
                        }
                    }
                }
                else if (predictor == 3)
                {
                    // Bytes differenced along the row, then put back from planes (most significant first) into samples.
                    for (var index = samples; index < rowBytes; index++)
                        data[start + index] = (byte)(data[start + index] + data[start + index - samples]);
                    var ordered = new byte[rowBytes];
                    var count = width * samples;
                    for (var sample = 0; sample < count; sample++)
                        for (var b = 0; b < bytesPerSample; b++)
                            ordered[sample * bytesPerSample + (little ? bytesPerSample - 1 - b : b)] = data[start + b * count + sample];
                    Array.Copy(ordered, 0, data, start, rowBytes);
                }
                else
                    throw new InvalidDataException("TIFF predictor " + predictor + " cannot be read.");
            }
        }

        private sealed class Reader
        {
            private readonly byte[] bytes;
            private readonly Dictionary<int, string> texts = new Dictionary<int, string>();

            public Reader(byte[] bytes, bool little)
            {
                this.bytes = bytes;
                Little = little;
            }

            public bool Little { get; }

            public ushort U16(long at)
            {
                Check(at, 2);
                return Little ? (ushort)(bytes[at] | (bytes[at + 1] << 8)) : (ushort)((bytes[at] << 8) | bytes[at + 1]);
            }

            public uint U32(long at)
            {
                Check(at, 4);
                return Little
                    ? (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24))
                    : (uint)((bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3]);
            }

            private void Check(long at, int length)
            {
                if (at < 0 || at + length > bytes.Length)
                    throw new InvalidDataException("The TIFF picture is cut short.");
            }

            public string Text(int tag)
            {
                return texts.TryGetValue(tag, out var text) ? text : null;
            }

            /// <summary>A directory's tags with their numbers; ASCII tags are kept as text as well.</summary>
            public Dictionary<int, uint[]> Directory(uint at)
            {
                var tags = new Dictionary<int, uint[]>();
                var count = U16(at);
                for (var index = 0; index < count; index++)
                {
                    var entry = at + 2 + index * 12L;
                    var tag = U16(entry);
                    var type = U16(entry + 2);
                    var number = (int)U32(entry + 4);
                    var size = type == 3 || type == 8 ? 2 : type == 4 || type == 9 ? 4 : type == 16 ? 8 : 1;
                    var place = number * size <= 4 ? entry + 8 : U32(entry + 8);
                    if (type == 2)
                    {
                        Check(place, number);
                        texts[tag] = System.Text.Encoding.ASCII.GetString(bytes, (int)place, number);
                        tags[tag] = new uint[0];
                        continue;
                    }
                    if (number > 1 << 24)
                        continue;
                    var values = new uint[number];
                    for (var value = 0; value < number; value++)
                        values[value] = size == 2 ? U16(place + value * 2L) : size == 4 ? U32(place + value * 4L) : size == 8 ? U32(place + value * 8L + (Little ? 0 : 4)) : bytes[place + value];
                    tags[tag] = values;
                }
                return tags;
            }

            /// <summary>A sample's number: unsigned (1), signed (2) or float (3).</summary>
            public double Number(byte[] raw, int at, int bits, int format)
            {
                switch (bits)
                {
                    case 8:
                        return format == 2 ? (sbyte)raw[at] : raw[at];
                    case 16:
                    {
                        var value = Little ? raw[at] | (raw[at + 1] << 8) : (raw[at] << 8) | raw[at + 1];
                        return format == 2 ? (short)value : value;
                    }
                    case 32:
                    {
                        var word = Little
                            ? (uint)(raw[at] | (raw[at + 1] << 8) | (raw[at + 2] << 16) | (raw[at + 3] << 24))
                            : (uint)((raw[at] << 24) | (raw[at + 1] << 16) | (raw[at + 2] << 8) | raw[at + 3]);
                        if (format == 3)
                            return BitConverter.ToSingle(BitConverter.GetBytes(word), 0);
                        return format == 2 ? (int)word : word;
                    }
                    default:
                    {
                        var eight = new byte[8];
                        for (var b = 0; b < 8; b++)
                            eight[b] = raw[at + (Little ? b : 7 - b)];
                        return BitConverter.ToDouble(eight, 0);
                    }
                }
            }
        }
    }
}
