using System;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The paint mask of one ground texture on one tile: <c>texture\map\tile_x_y.map.N.dds</c>, an 8-bit alpha DDS of 2^exponent pixels
    /// square (one byte of coverage per pixel, the first row at the north edge, the first column at the west edge). Files with mipmaps are
    /// read from their first level; a mask is written without them, as the base game's own Grundorf masks are.
    /// </summary>
    public sealed class OmsiGroundMask
    {
        /// <summary>Size of the DDS header OMSI's masks carry before the pixels.</summary>
        public const int HeaderBytes = 128;

        private const int FlagsWithoutMipmaps = 0x1007;
        private const int PixelFormatAlpha = 0x2;
        private const int CapsOfAlphaMask = 0x1002;

        public OmsiGroundMask(int size)
        {
            if (size < 1 || size > 4096)
                throw new ArgumentOutOfRangeException(nameof(size));
            Size = size;
            Alpha = new byte[size * size];
        }

        /// <summary>Pixels along one edge.</summary>
        public int Size { get; }

        /// <summary>Coverage per pixel, row after row from the north edge.</summary>
        public byte[] Alpha { get; }

        /// <summary>The mask in a file, or null when it is not a square 8-bit alpha DDS.</summary>
        public static OmsiGroundMask TryParse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderBytes || bytes[0] != 'D' || bytes[1] != 'D' || bytes[2] != 'S' || bytes[3] != ' ')
                return null;
            var height = Int32(bytes, 12);
            var width = Int32(bytes, 16);
            if (width != height || width < 1 || width > 4096)
                return null;
            // The pixel format: alpha only, eight bits.
            if ((Int32(bytes, 80) & PixelFormatAlpha) == 0 || Int32(bytes, 88) != 8)
                return null;
            if (bytes.Length < HeaderBytes + width * height)
                return null;
            var mask = new OmsiGroundMask(width);
            Buffer.BlockCopy(bytes, HeaderBytes, mask.Alpha, 0, width * height);
            return mask;
        }

        /// <summary>The mask of a file; fails with a message that names the file's trouble.</summary>
        public static OmsiGroundMask Parse(byte[] bytes, string name)
        {
            var mask = TryParse(bytes);
            if (mask == null)
                throw new InvalidDataException(name + " is not a square 8-bit alpha DDS and cannot be painted on.");
            return mask;
        }

        /// <summary>The bytes of the file: the header the base game's masks have, then the pixels.</summary>
        public byte[] ToBytes()
        {
            var bytes = new byte[HeaderBytes + Alpha.Length];
            bytes[0] = (byte)'D';
            bytes[1] = (byte)'D';
            bytes[2] = (byte)'S';
            bytes[3] = (byte)' ';
            SetInt32(bytes, 4, 124);
            SetInt32(bytes, 8, FlagsWithoutMipmaps);
            SetInt32(bytes, 12, Size);
            SetInt32(bytes, 16, Size);
            SetInt32(bytes, 76, 32);
            SetInt32(bytes, 80, PixelFormatAlpha);
            SetInt32(bytes, 88, 8);
            SetInt32(bytes, 104, 0xFF);
            SetInt32(bytes, 108, CapsOfAlphaMask);
            Buffer.BlockCopy(Alpha, 0, bytes, HeaderBytes, Alpha.Length);
            return bytes;
        }

        /// <summary>The exponent a size is the power of two of, or -1.</summary>
        public static int ExponentOf(int size)
        {
            for (var exponent = 0; exponent < 13; exponent++)
                if (1 << exponent == size)
                    return exponent;
            return -1;
        }

        private static int Int32(byte[] bytes, int offset)
        {
            return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        }

        private static void SetInt32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
