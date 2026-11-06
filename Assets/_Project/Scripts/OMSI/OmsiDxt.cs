using System;
using System.Threading.Tasks;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// DXT1 compression of an RGB picture with its mip levels, done by hand so that it can run on a worker thread: Unity's own
    /// <c>Texture2D.Compress</c> works on the main thread only and takes about 0.4 s for a 3,300 px picture, which showed as a stutter
    /// whenever a tile's full picture came into view (ADR 062). The output is what Unity keeps for a DXT1 texture with a full mip
    /// chain: the levels one after the other, each as rows of 4 × 4 blocks of 8 bytes in memory order from the first row of pixels.
    /// </summary>
    public static class OmsiDxt
    {
        /// <summary>Unity's <c>TextureFormat.DXT1</c>.</summary>
        public const int Dxt1Format = 10;

        // The rows of blocks of a level are shared out over a few cores (never more than four, and never more than half of them, so the
        // main thread keeps its own).
        private static readonly ParallelOptions Cores = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)) };

        [ThreadStatic] private static byte[] scratchA;
        [ThreadStatic] private static byte[] scratchB;

        /// <summary>Levels of a full mip chain: down to 1 × 1, halving the larger side each time as Unity does.</summary>
        public static int MipCount(int width, int height)
        {
            var count = 1;
            for (var side = Math.Max(width, height); side > 1; side >>= 1)
                count++;
            return count;
        }

        /// <summary>The bytes of one level: blocks of 4 × 4 pixels, at least one block each way.</summary>
        public static int LevelSize(int width, int height)
        {
            return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8;
        }

        public static int TotalSize(int width, int height, int mipCount)
        {
            var total = 0;
            for (var level = 0; level < mipCount; level++)
            {
                total += LevelSize(width, height);
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
            return total;
        }

        /// <summary>
        /// All levels of a picture as DXT1. <paramref name="rgb"/> holds 3 bytes a pixel, rows in the order the texture has them
        /// (for Unity: the bottom row first).
        /// </summary>
        public static byte[] EncodeWithMips(byte[] rgb, int width, int height, out int mipCount)
        {
            if (rgb == null)
                throw new ArgumentNullException(nameof(rgb));
            if (width < 1 || height < 1 || rgb.Length < width * height * 3)
                throw new ArgumentException("The picture does not have " + width + " × " + height + " pixels.");
            mipCount = MipCount(width, height);
            var output = new byte[TotalSize(width, height, mipCount)];
            var offset = 0;
            var source = rgb;
            var levelWidth = width;
            var levelHeight = height;
            // Each level is made from the one before: two buffers take turns, kept for the next picture of the thread.
            var next = Math.Max(1, width / 2) * Math.Max(1, height / 2) * 3;
            if (scratchA == null || scratchA.Length < next)
                scratchA = new byte[next];
            if (scratchB == null || scratchB.Length < next)
                scratchB = new byte[next];
            var target = scratchA;
            for (var level = 0; level < mipCount; level++)
            {
                EncodeLevel(source, levelWidth, levelHeight, output, offset);
                offset += LevelSize(levelWidth, levelHeight);
                if (level + 1 == mipCount)
                    break;
                var nextWidth = Math.Max(1, levelWidth / 2);
                var nextHeight = Math.Max(1, levelHeight / 2);
                Halve(source, levelWidth, levelHeight, target);
                source = target;
                target = ReferenceEquals(target, scratchA) ? scratchB : scratchA;
                levelWidth = nextWidth;
                levelHeight = nextHeight;
            }
            return output;
        }

        /// <summary>A level at half the size: each pixel the mean of the 2 × 2 it covers (the last row or column counted twice when the side is odd).</summary>
        private static void Halve(byte[] source, int width, int height, byte[] target)
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);
            Action<int> halveRow = y =>
            {
                var write = y * nextWidth * 3;
                var row0 = Math.Min(2 * y, height - 1) * width;
                var row1 = Math.Min(2 * y + 1, height - 1) * width;
                for (var x = 0; x < nextWidth; x++)
                {
                    var x0 = Math.Min(2 * x, width - 1);
                    var x1 = Math.Min(2 * x + 1, width - 1);
                    var a = (row0 + x0) * 3;
                    var b = (row0 + x1) * 3;
                    var c = (row1 + x0) * 3;
                    var d = (row1 + x1) * 3;
                    for (var channel = 0; channel < 3; channel++)
                        target[write++] = (byte)((source[a + channel] + source[b + channel] + source[c + channel] + source[d + channel] + 2) >> 2);
                }
            };
            if (nextHeight >= 64)
                Parallel.For(0, nextHeight, Cores, halveRow);
            else
                for (var y = 0; y < nextHeight; y++)
                    halveRow(y);
        }

        private static void EncodeLevel(byte[] source, int width, int height, byte[] output, int offset)
        {
            var blocksX = Math.Max(1, (width + 3) / 4);
            var blocksY = Math.Max(1, (height + 3) / 4);
            Action<int> encodeRow = blockY =>
            {
                var pixels = new int[48];
                var work = new int[24];
                var at = offset + blockY * blocksX * 8;
                for (var blockX = 0; blockX < blocksX; blockX++)
                {
                    for (var index = 0; index < 16; index++)
                    {
                        // A block past the edge repeats the last pixels.
                        var x = Math.Min(blockX * 4 + (index & 3), width - 1);
                        var y = Math.Min(blockY * 4 + (index >> 2), height - 1);
                        var from = (y * width + x) * 3;
                        pixels[index * 3] = source[from];
                        pixels[index * 3 + 1] = source[from + 1];
                        pixels[index * 3 + 2] = source[from + 2];
                    }
                    EncodeBlock(pixels, work, output, at);
                    at += 8;
                }
            };
            if (blocksY >= 32)
                Parallel.For(0, blocksY, Cores, encodeRow);
            else
                for (var blockY = 0; blockY < blocksY; blockY++)
                    encodeRow(blockY);
        }

        /// <summary>
        /// One block: endpoints from the bounding box of its colours (pulled in a little, as stb_dxt does), then one round of
        /// least squares on the indices they give; the better of the two is written.
        /// </summary>
        private static void EncodeBlock(int[] pixels, int[] work, byte[] output, int offset)
        {
            int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            for (var index = 0; index < 16; index++)
            {
                var r = pixels[index * 3];
                var g = pixels[index * 3 + 1];
                var b = pixels[index * 3 + 2];
                if (r < minR) minR = r;
                if (g < minG) minG = g;
                if (b < minB) minB = b;
                if (r > maxR) maxR = r;
                if (g > maxG) maxG = g;
                if (b > maxB) maxB = b;
            }
            var insetR = (maxR - minR) >> 4;
            var insetG = (maxG - minG) >> 4;
            var insetB = (maxB - minB) >> 4;
            var first = Pack(maxR - insetR, maxG - insetG, maxB - insetB);
            var second = Pack(minR + insetR, minG + insetG, minB + insetB);
            uint indices;
            var error = Match(pixels, first, second, out indices, work);
            // A block that the bounding box already gives within about one level a pixel is left as it is.
            if (error > 64 && first != second)
            {
                // Least squares for the two endpoints, given which of the four colours each pixel took.
                double aa = 0, ab = 0, bb = 0, rA = 0, gA = 0, bA = 0, rB = 0, gB = 0, bBlue = 0;
                for (var index = 0; index < 16; index++)
                {
                    var step = (int)((indices >> (2 * index)) & 3);
                    // Index 0 is the first colour, 1 the second, 2 two thirds first, 3 one third first.
                    var weightB = step == 0 ? 0.0 : step == 1 ? 1.0 : step == 2 ? 1.0 / 3.0 : 2.0 / 3.0;
                    var weightA = 1.0 - weightB;
                    aa += weightA * weightA;
                    ab += weightA * weightB;
                    bb += weightB * weightB;
                    var r = pixels[index * 3];
                    var g = pixels[index * 3 + 1];
                    var b = pixels[index * 3 + 2];
                    rA += weightA * r;
                    gA += weightA * g;
                    bA += weightA * b;
                    rB += weightB * r;
                    gB += weightB * g;
                    bBlue += weightB * b;
                }
                var determinant = aa * bb - ab * ab;
                if (Math.Abs(determinant) > 1e-6)
                {
                    var refinedFirst = Pack(Clamp((bb * rA - ab * rB) / determinant), Clamp((bb * gA - ab * gB) / determinant), Clamp((bb * bA - ab * bBlue) / determinant));
                    var refinedSecond = Pack(Clamp((aa * rB - ab * rA) / determinant), Clamp((aa * gB - ab * gA) / determinant), Clamp((aa * bBlue - ab * bA) / determinant));
                    uint refinedIndices;
                    var refinedError = Match(pixels, refinedFirst, refinedSecond, out refinedIndices, work);
                    if (refinedError < error)
                    {
                        first = refinedFirst;
                        second = refinedSecond;
                        indices = refinedIndices;
                        error = refinedError;
                    }
                }
            }
            // Ordered so that the first colour is the larger one: four colours, none of them transparent. When both are the same
            // colour every index is 0 and the block is that colour.
            if (first < second)
            {
                var swap = first;
                first = second;
                second = swap;
                indices = SwapEndpoints(indices);
            }
            output[offset] = (byte)first;
            output[offset + 1] = (byte)(first >> 8);
            output[offset + 2] = (byte)second;
            output[offset + 3] = (byte)(second >> 8);
            output[offset + 4] = (byte)indices;
            output[offset + 5] = (byte)(indices >> 8);
            output[offset + 6] = (byte)(indices >> 16);
            output[offset + 7] = (byte)(indices >> 24);
        }

        /// <summary>Swapping the two colours of a block turns index 0 into 1 and 2 into 3 (and back).</summary>
        private static uint SwapEndpoints(uint indices)
        {
            uint result = 0;
            for (var index = 0; index < 16; index++)
            {
                var step = (indices >> (2 * index)) & 3;
                step = step == 0 ? 1u : step == 1 ? 0u : step == 2 ? 3u : 2u;
                result |= step << (2 * index);
            }
            return result;
        }

        private static int Clamp(double value)
        {
            return value < 0.0 ? 0 : value > 255.0 ? 255 : (int)Math.Round(value);
        }

        private static ushort Pack(int r, int g, int b)
        {
            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));
            return (ushort)(((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255));
        }

        /// <summary>The four colours of a block as the card builds them, the endpoints and the two between, for <c>first</c> larger than <c>second</c>.</summary>
        private static void Palette(ushort first, ushort second, int[] palette)
        {
            Expand(first, palette, 0);
            Expand(second, palette, 3);
            for (var channel = 0; channel < 3; channel++)
            {
                palette[6 + channel] = (2 * palette[channel] + palette[3 + channel]) / 3;
                palette[9 + channel] = (palette[channel] + 2 * palette[3 + channel]) / 3;
            }
        }

        private static void Expand(ushort color, int[] palette, int at)
        {
            var r = (color >> 11) & 31;
            var g = (color >> 5) & 63;
            var b = color & 31;
            palette[at] = (r << 3) | (r >> 2);
            palette[at + 1] = (g << 2) | (g >> 4);
            palette[at + 2] = (b << 3) | (b >> 2);
        }

        /// <summary>
        /// Gives every pixel the nearest of the four colours; returns the summed squared error. The four colours lie on one line, so the
        /// nearest follows from where the pixel projects onto it.
        /// </summary>
        private static int Match(int[] pixels, ushort first, ushort second, out uint indices, int[] work)
        {
            // The card builds the palette from the larger endpoint first, so the colours are laid out that way here too.
            var larger = first >= second ? first : second;
            var smaller = first >= second ? second : first;
            var swapped = first < second;
            Palette(larger, smaller, work);
            var dirR = work[3] - work[0];
            var dirG = work[4] - work[1];
            var dirB = work[5] - work[2];
            var length = dirR * dirR + dirG * dirG + dirB * dirB;
            indices = 0;
            var error = 0;
            for (var index = 0; index < 16; index++)
            {
                var r = pixels[index * 3];
                var g = pixels[index * 3 + 1];
                var b = pixels[index * 3 + 2];
                var best = 0;
                if (length > 0)
                {
                    // 0 at the larger colour, 3 at the smaller; along the line the indices run 0, 2, 3, 1.
                    var along = ((r - work[0]) * dirR + (g - work[1]) * dirG + (b - work[2]) * dirB) * 3;
                    var step = along <= 0 ? 0 : along >= 3 * length ? 3 : (2 * along + length) / (2 * length);
                    best = step == 0 ? 0 : step == 1 ? 2 : step == 2 ? 3 : 1;
                }
                var dr = r - work[best * 3];
                var dg = g - work[best * 3 + 1];
                var db = b - work[best * 3 + 2];
                error += dr * dr + dg * dg + db * db;
                // Asked for in the given order: with the endpoints swapped, 0 and 1, 2 and 3 trade places.
                if (swapped)
                    best = best == 0 ? 1 : best == 1 ? 0 : best == 2 ? 3 : 2;
                indices |= (uint)best << (2 * index);
            }
            return error;
        }
    }
}
