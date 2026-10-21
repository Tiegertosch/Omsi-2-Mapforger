using System;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A decoded texture: rows from top to bottom, either one byte (grey or alpha) or four bytes RGBA per pixel.</summary>
    public sealed class OmsiImage
    {
        public OmsiImage(int width, int height, int channels, byte[] pixels)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "An image needs a positive size.");
            if (channels != 1 && channels != 4)
                throw new ArgumentOutOfRangeException(nameof(channels), "An image has one or four channels.");
            if (pixels == null || pixels.Length != width * height * channels)
                throw new ArgumentException("The pixel data does not match the size.", nameof(pixels));
            Width = width;
            Height = height;
            Channels = channels;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>1 for grey or alpha masks, 4 for RGBA.</summary>
        public int Channels { get; }

        public byte[] Pixels { get; }
    }

    /// <summary>
    /// Decodes the texture formats OMSI uses that the engine cannot load itself: BMP, TGA and DDS (DXT1, DXT3, DXT5
    /// and uncompressed). JPEG and PNG are left to the caller.
    /// </summary>
    public static class OmsiImageReader
    {
        /// <summary>Returns false for formats this reader does not handle, such as JPEG and PNG.</summary>
        /// <exception cref="InvalidDataException">The file looks like a handled format but is damaged or uses an unsupported variant.</exception>
        public static bool TryDecode(byte[] bytes, out OmsiImage image)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            image = null;
            if (bytes.Length >= 4 && bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S' && bytes[3] == ' ')
                image = DecodeDds(bytes);
            else if (bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M')
                image = DecodeBmp(bytes);
            else if (LooksLikeTga(bytes))
                image = DecodeTga(bytes);
            return image != null;
        }

        private static int Int32(byte[] bytes, int offset)
        {
            Need(bytes, offset + 4);
            return BitConverter.ToInt32(bytes, offset);
        }

        private static int UInt16(byte[] bytes, int offset)
        {
            Need(bytes, offset + 2);
            return bytes[offset] | bytes[offset + 1] << 8;
        }

        private static void Need(byte[] bytes, long length)
        {
            if (length > bytes.Length)
                throw new InvalidDataException("The image file ends early.");
        }

        private static void CheckSize(long width, long height)
        {
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
                throw new InvalidDataException("Unsupported image size " + width + " × " + height + ".");
        }

        // ---------------------------------------------------------------- BMP

        private static OmsiImage DecodeBmp(byte[] bytes)
        {
            var dataOffset = Int32(bytes, 10);
            var headerSize = Int32(bytes, 14);
            var width = Int32(bytes, 18);
            var rawHeight = Int32(bytes, 22);
            var bits = UInt16(bytes, 28);
            var compression = Int32(bytes, 30);
            var topDown = rawHeight < 0;
            var height = Math.Abs(rawHeight);
            CheckSize(width, height);
            if (compression != 0 && !(compression == 3 && (bits == 16 || bits == 32)))
                throw new InvalidDataException("Compressed BMP files are not supported.");

            uint redMask = 0x00FF0000, greenMask = 0x0000FF00, blueMask = 0x000000FF, alphaMask = 0;
            if (bits == 16)
            {
                redMask = 0x7C00;
                greenMask = 0x03E0;
                blueMask = 0x001F;
            }
            if (compression == 3)
            {
                // The masks follow a 40-byte header or are part of a larger one.
                redMask = (uint)Int32(bytes, 54);
                greenMask = (uint)Int32(bytes, 58);
                blueMask = (uint)Int32(bytes, 62);
                alphaMask = headerSize >= 56 ? (uint)Int32(bytes, 66) : 0;
            }

            byte[] palette = null;
            if (bits <= 8)
            {
                var colours = Int32(bytes, 46);
                if (colours == 0)
                    colours = 1 << bits;
                palette = new byte[colours * 4];
                var paletteStart = 14 + headerSize;
                Need(bytes, paletteStart + palette.Length);
                Array.Copy(bytes, paletteStart, palette, 0, palette.Length);
            }
            else if (bits != 16 && bits != 24 && bits != 32)
            {
                throw new InvalidDataException("Unsupported BMP colour depth " + bits + ".");
            }

            var stride = (width * bits + 31) / 32 * 4;
            Need(bytes, dataOffset + (long)stride * height);
            var pixels = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
            {
                // Rows are stored bottom up unless the height is negative.
                var source = dataOffset + (topDown ? row : height - 1 - row) * stride;
                var target = row * width * 4;
                for (var x = 0; x < width; x++, target += 4)
                {
                    switch (bits)
                    {
                        case 24:
                            pixels[target] = bytes[source + x * 3 + 2];
                            pixels[target + 1] = bytes[source + x * 3 + 1];
                            pixels[target + 2] = bytes[source + x * 3];
                            pixels[target + 3] = 255;
                            break;
                        case 32 when compression == 0:
                            pixels[target] = bytes[source + x * 4 + 2];
                            pixels[target + 1] = bytes[source + x * 4 + 1];
                            pixels[target + 2] = bytes[source + x * 4];
                            pixels[target + 3] = 255;
                            break;
                        case 32:
                        case 16:
                            var value = bits == 32 ? (uint)BitConverter.ToInt32(bytes, source + x * 4) : (uint)UInt16(bytes, source + x * 2);
                            WriteMasked(pixels, target, value, redMask, greenMask, blueMask, alphaMask);
                            break;
                        default:
                            var index = PaletteIndex(bytes, source, x, bits);
                            if (index * 4 + 3 >= palette.Length)
                                throw new InvalidDataException("A BMP pixel points outside its palette.");
                            pixels[target] = palette[index * 4 + 2];
                            pixels[target + 1] = palette[index * 4 + 1];
                            pixels[target + 2] = palette[index * 4];
                            pixels[target + 3] = 255;
                            break;
                    }
                }
            }
            return new OmsiImage(width, height, 4, pixels);
        }

        private static int PaletteIndex(byte[] bytes, int rowStart, int x, int bits)
        {
            var bitOffset = x * bits;
            var value = bytes[rowStart + bitOffset / 8];
            return (value >> (8 - bits - bitOffset % 8)) & ((1 << bits) - 1);
        }

        /// <summary>Extracts channels by bit mask and scales each to eight bits; no alpha mask means opaque.</summary>
        private static void WriteMasked(byte[] pixels, int target, uint value, uint red, uint green, uint blue, uint alpha)
        {
            pixels[target] = Channel(value, red);
            pixels[target + 1] = Channel(value, green);
            pixels[target + 2] = Channel(value, blue);
            pixels[target + 3] = alpha == 0 ? (byte)255 : Channel(value, alpha);
        }

        private static byte Channel(uint value, uint mask)
        {
            if (mask == 0)
                return 0;
            var shift = 0;
            while ((mask >> shift & 1) == 0)
                shift++;
            var maximum = mask >> shift;
            return (byte)(((value & mask) >> shift) * 255 / maximum);
        }

        // ---------------------------------------------------------------- TGA

        private static bool LooksLikeTga(byte[] bytes)
        {
            if (bytes.Length < 18 || bytes[1] != 0)
                return false;
            var type = bytes[2];
            var depth = bytes[16];
            return (type == 2 || type == 10) && (depth == 24 || depth == 32) || (type == 3 || type == 11) && depth == 8;
        }

        private static OmsiImage DecodeTga(byte[] bytes)
        {
            var idLength = bytes[0];
            var type = bytes[2];
            var width = UInt16(bytes, 12);
            var height = UInt16(bytes, 14);
            var depth = bytes[16];
            var topDown = (bytes[17] & 0x20) != 0;
            CheckSize(width, height);

            var bytesPerPixel = depth / 8;
            var count = width * height;
            var raw = new byte[count * bytesPerPixel];
            var position = 18 + idLength;
            if (type == 2 || type == 3)
            {
                Need(bytes, position + (long)raw.Length);
                Array.Copy(bytes, position, raw, 0, raw.Length);
            }
            else
            {
                // Run-length packets: a header byte, then one repeated pixel or a run of literal pixels.
                var written = 0;
                while (written < raw.Length)
                {
                    Need(bytes, position + 1);
                    var header = bytes[position++];
                    var pixelsInPacket = (header & 0x7F) + 1;
                    if (written + pixelsInPacket * bytesPerPixel > raw.Length)
                        throw new InvalidDataException("A TGA run goes past the end of the image.");
                    if ((header & 0x80) != 0)
                    {
                        Need(bytes, position + bytesPerPixel);
                        for (var pixel = 0; pixel < pixelsInPacket; pixel++)
                            Array.Copy(bytes, position, raw, written + pixel * bytesPerPixel, bytesPerPixel);
                        position += bytesPerPixel;
                    }
                    else
                    {
                        Need(bytes, position + pixelsInPacket * bytesPerPixel);
                        Array.Copy(bytes, position, raw, written, pixelsInPacket * bytesPerPixel);
                        position += pixelsInPacket * bytesPerPixel;
                    }
                    written += pixelsInPacket * bytesPerPixel;
                }
            }

            var channels = bytesPerPixel == 1 ? 1 : 4;
            var pixels = new byte[count * channels];
            for (var row = 0; row < height; row++)
            {
                // Rows are stored bottom up unless the descriptor says otherwise.
                var sourceRow = topDown ? row : height - 1 - row;
                for (var x = 0; x < width; x++)
                {
                    var source = (sourceRow * width + x) * bytesPerPixel;
                    var target = (row * width + x) * channels;
                    if (channels == 1)
                    {
                        pixels[target] = raw[source];
                        continue;
                    }
                    pixels[target] = raw[source + 2];
                    pixels[target + 1] = raw[source + 1];
                    pixels[target + 2] = raw[source];
                    pixels[target + 3] = bytesPerPixel == 4 ? raw[source + 3] : (byte)255;
                }
            }
            return new OmsiImage(width, height, channels, pixels);
        }

        // ---------------------------------------------------------------- DDS

        private const int DdpfAlphaPixels = 0x1;
        private const int DdpfAlpha = 0x2;
        private const int DdpfFourCc = 0x4;
        private const int DdpfRgb = 0x40;
        private const int DdpfLuminance = 0x20000;

        /// <summary>Decodes the top mip level. Alpha-only and luminance surfaces become one-channel images.</summary>
        private static OmsiImage DecodeDds(byte[] bytes)
        {
            Need(bytes, 128);
            var height = Int32(bytes, 12);
            var width = Int32(bytes, 16);
            CheckSize(width, height);
            var flags = Int32(bytes, 80);
            var fourCc = Int32(bytes, 84);
            var bits = Int32(bytes, 88);
            var data = 128;

            if ((flags & DdpfFourCc) != 0)
            {
                switch (fourCc)
                {
                    case 0x31545844: // DXT1
                        return DecodeBlocks(bytes, data, width, height, 8, DecodeDxt1Block);
                    case 0x33545844: // DXT3
                        return DecodeBlocks(bytes, data, width, height, 16, DecodeDxt3Block);
                    case 0x35545844: // DXT5
                        return DecodeBlocks(bytes, data, width, height, 16, DecodeDxt5Block);
                    default:
                        throw new InvalidDataException("Unsupported DDS compression '" + FourCcText(fourCc) + "'.");
                }
            }

            if (bits != 8 && bits != 16 && bits != 24 && bits != 32)
                throw new InvalidDataException("Unsupported DDS bit depth " + bits + ".");
            var red = (uint)Int32(bytes, 92);
            var green = (uint)Int32(bytes, 96);
            var blue = (uint)Int32(bytes, 100);
            var alpha = (uint)Int32(bytes, 104);
            var bytesPerPixel = bits / 8;
            Need(bytes, data + (long)width * height * bytesPerPixel);

            var singleChannel = (flags & DdpfAlpha) != 0 && (flags & DdpfRgb) == 0 ||
                                (flags & DdpfLuminance) != 0 && (flags & DdpfAlphaPixels) == 0;
            if (singleChannel && bits == 8)
            {
                var plain = new byte[width * height];
                Array.Copy(bytes, data, plain, 0, plain.Length);
                return new OmsiImage(width, height, 1, plain);
            }
            if ((flags & DdpfLuminance) != 0)
            {
                // Luminance with or without alpha: grey copied into red, green and blue.
                green = red;
                blue = red;
            }

            var pixels = new byte[width * height * 4];
            for (var index = 0; index < width * height; index++)
            {
                uint value = 0;
                for (var part = 0; part < bytesPerPixel; part++)
                    value |= (uint)bytes[data + index * bytesPerPixel + part] << (8 * part);
                WriteMasked(pixels, index * 4, value, red, green, blue, (flags & (DdpfAlphaPixels | DdpfAlpha)) != 0 ? alpha : 0);
            }
            return new OmsiImage(width, height, 4, pixels);
        }

        private static string FourCcText(int fourCc)
        {
            return new string(new[] { (char)(fourCc & 0xFF), (char)(fourCc >> 8 & 0xFF), (char)(fourCc >> 16 & 0xFF), (char)(fourCc >> 24 & 0xFF) });
        }

        private delegate void BlockDecoder(byte[] bytes, int offset, byte[] block);

        /// <summary>Decodes 4 × 4 pixel blocks into RGBA; partial blocks at the right and bottom edge are cropped.</summary>
        private static OmsiImage DecodeBlocks(byte[] bytes, int data, int width, int height, int blockSize, BlockDecoder decode)
        {
            var blocksWide = (width + 3) / 4;
            var blocksHigh = (height + 3) / 4;
            Need(bytes, data + (long)blocksWide * blocksHigh * blockSize);
            var pixels = new byte[width * height * 4];
            var block = new byte[64];
            for (var blockY = 0; blockY < blocksHigh; blockY++)
            {
                for (var blockX = 0; blockX < blocksWide; blockX++)
                {
                    decode(bytes, data + (blockY * blocksWide + blockX) * blockSize, block);
                    for (var y = 0; y < 4 && blockY * 4 + y < height; y++)
                        for (var x = 0; x < 4 && blockX * 4 + x < width; x++)
                            Array.Copy(block, (y * 4 + x) * 4, pixels, ((blockY * 4 + y) * width + blockX * 4 + x) * 4, 4);
                }
            }
            return new OmsiImage(width, height, 4, pixels);
        }

        /// <summary>Two RGB565 end colours and two bits per pixel; with the first colour not above the second, index 3 is transparent.</summary>
        private static void DecodeColourBlock(byte[] bytes, int offset, byte[] block, bool allowTransparent)
        {
            var first = UInt16(bytes, offset);
            var second = UInt16(bytes, offset + 2);
            var palette = new int[16];
            Expand565(first, palette, 0);
            Expand565(second, palette, 4);
            var fourColours = first > second || !allowTransparent;
            for (var channel = 0; channel < 3; channel++)
            {
                if (fourColours)
                {
                    palette[8 + channel] = (2 * palette[channel] + palette[4 + channel]) / 3;
                    palette[12 + channel] = (palette[channel] + 2 * palette[4 + channel]) / 3;
                }
                else
                {
                    palette[8 + channel] = (palette[channel] + palette[4 + channel]) / 2;
                    palette[12 + channel] = 0;
                }
            }
            palette[3] = palette[7] = palette[11] = 255;
            palette[15] = fourColours ? 255 : 0;

            var indices = (uint)Int32(bytes, offset + 4);
            for (var pixel = 0; pixel < 16; pixel++)
            {
                var index = (int)(indices >> (2 * pixel) & 3);
                for (var channel = 0; channel < 4; channel++)
                    block[pixel * 4 + channel] = (byte)palette[index * 4 + channel];
            }
        }

        private static void Expand565(int value, int[] palette, int offset)
        {
            palette[offset] = (value >> 11 & 31) * 255 / 31;
            palette[offset + 1] = (value >> 5 & 63) * 255 / 63;
            palette[offset + 2] = (value & 31) * 255 / 31;
        }

        private static void DecodeDxt1Block(byte[] bytes, int offset, byte[] block)
        {
            DecodeColourBlock(bytes, offset, block, true);
        }

        /// <summary>Four bits of explicit alpha per pixel, then a DXT1 colour block.</summary>
        private static void DecodeDxt3Block(byte[] bytes, int offset, byte[] block)
        {
            DecodeColourBlock(bytes, offset + 8, block, false);
            for (var pixel = 0; pixel < 16; pixel++)
            {
                var nibble = bytes[offset + pixel / 2] >> (pixel % 2 * 4) & 15;
                block[pixel * 4 + 3] = (byte)(nibble * 17);
            }
        }

        /// <summary>Two alpha end values with six or eight interpolated steps and three bits per pixel, then a colour block.</summary>
        private static void DecodeDxt5Block(byte[] bytes, int offset, byte[] block)
        {
            DecodeColourBlock(bytes, offset + 8, block, false);
            int first = bytes[offset], second = bytes[offset + 1];
            var alphas = new int[8];
            alphas[0] = first;
            alphas[1] = second;
            if (first > second)
            {
                for (var step = 1; step < 7; step++)
                    alphas[step + 1] = ((7 - step) * first + step * second) / 7;
            }
            else
            {
                for (var step = 1; step < 5; step++)
                    alphas[step + 1] = ((5 - step) * first + step * second) / 5;
                alphas[6] = 0;
                alphas[7] = 255;
            }

            ulong bits = 0;
            for (var index = 0; index < 6; index++)
                bits |= (ulong)bytes[offset + 2 + index] << (8 * index);
            for (var pixel = 0; pixel < 16; pixel++)
                block[pixel * 4 + 3] = (byte)alphas[(int)(bits >> (3 * pixel) & 7)];
        }
    }
}
