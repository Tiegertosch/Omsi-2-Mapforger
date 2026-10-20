using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Byte-level text encoding of an OMSI file, kept so the file can be written back unchanged.</summary>
    public enum OmsiTextEncoding
    {
        /// <summary>UTF-16 little endian with byte order mark; what the OMSI editor writes for maps.</summary>
        Utf16LittleEndian,
        Utf16BigEndian,
        Utf8WithByteOrderMark,
        Utf8,
        Windows1252
    }

    /// <summary>
    /// Reads OMSI text files independent of the runtime's default code page.
    /// OMSI writes UTF-16 LE with a byte order mark; older or hand-edited files are UTF-8 or Windows-1252.
    /// </summary>
    public static class OmsiTextFile
    {
        // Windows-1252 differs from ISO-8859-1 only in 0x80-0x9F. The five undefined bytes (0x81, 0x8D, 0x8F, 0x90,
        // 0x9D) map to the C1 control character with the same value, as Windows does, so decoding is reversible.
        private static readonly char[] Windows1252HighControlRange =
        {
            '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡',
            'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
            '\u0090', '‘', '’', '“', '”', '•', '–', '—',
            '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ'
        };

        private static readonly byte[] Utf8ByteOrderMark = { 0xEF, 0xBB, 0xBF };
        private static readonly byte[] Utf16LittleEndianByteOrderMark = { 0xFF, 0xFE };
        private static readonly byte[] Utf16BigEndianByteOrderMark = { 0xFE, 0xFF };

        public static string[] ReadAllLines(string path)
        {
            return SplitLines(Decode(File.ReadAllBytes(path)));
        }

        /// <summary>Decodes bytes using a byte order mark, then strict UTF-8, then Windows-1252.</summary>
        public static string Decode(byte[] bytes)
        {
            return Decode(bytes, out _);
        }

        /// <summary>Decodes bytes and reports which encoding was used, so <see cref="Encode"/> can reproduce them.</summary>
        public static string Decode(byte[] bytes, out OmsiTextEncoding encoding)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (StartsWith(bytes, Utf8ByteOrderMark))
            {
                encoding = OmsiTextEncoding.Utf8WithByteOrderMark;
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            if (StartsWith(bytes, Utf16LittleEndianByteOrderMark))
            {
                encoding = OmsiTextEncoding.Utf16LittleEndian;
                return new UnicodeEncoding(false, false).GetString(bytes, 2, bytes.Length - 2);
            }
            if (StartsWith(bytes, Utf16BigEndianByteOrderMark))
            {
                encoding = OmsiTextEncoding.Utf16BigEndian;
                return new UnicodeEncoding(true, false).GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                var text = new UTF8Encoding(false, true).GetString(bytes);
                encoding = OmsiTextEncoding.Utf8;
                return text;
            }
            catch (DecoderFallbackException)
            {
                encoding = OmsiTextEncoding.Windows1252;
                return DecodeWindows1252(bytes);
            }
        }

        /// <summary>Encodes text in the given encoding, including its byte order mark.</summary>
        public static byte[] Encode(string text, OmsiTextEncoding encoding)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            switch (encoding)
            {
                case OmsiTextEncoding.Utf16LittleEndian:
                    return Concat(Utf16LittleEndianByteOrderMark, new UnicodeEncoding(false, false).GetBytes(text));
                case OmsiTextEncoding.Utf16BigEndian:
                    return Concat(Utf16BigEndianByteOrderMark, new UnicodeEncoding(true, false).GetBytes(text));
                case OmsiTextEncoding.Utf8WithByteOrderMark:
                    return Concat(Utf8ByteOrderMark, new UTF8Encoding(false).GetBytes(text));
                case OmsiTextEncoding.Utf8:
                    return new UTF8Encoding(false).GetBytes(text);
                case OmsiTextEncoding.Windows1252:
                    return EncodeWindows1252(text);
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown OMSI text encoding.");
            }
        }

        private static string DecodeWindows1252(byte[] bytes)
        {
            var characters = new char[bytes.Length];
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = bytes[index];
                characters[index] = value >= 0x80 && value <= 0x9F
                    ? Windows1252HighControlRange[value - 0x80]
                    : (char)value;
            }

            return new string(characters);
        }

        private static byte[] EncodeWindows1252(string text)
        {
            var bytes = new byte[text.Length];
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character < 0x80 || (character >= 0xA0 && character <= 0xFF))
                {
                    bytes[index] = (byte)character;
                    continue;
                }

                var position = Array.IndexOf(Windows1252HighControlRange, character);
                if (position < 0)
                    throw new ArgumentException("The character U+" + ((int)character).ToString("X4") +
                                                " cannot be written in Windows-1252.", nameof(text));
                bytes[index] = (byte)(0x80 + position);
            }

            return bytes;
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (bytes.Length < prefix.Length)
                return false;
            for (var index = 0; index < prefix.Length; index++)
                if (bytes[index] != prefix[index])
                    return false;
            return true;
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private static string[] SplitLines(string text)
        {
            var lines = new List<string>();
            var start = 0;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character != '\r' && character != '\n')
                    continue;

                lines.Add(text.Substring(start, index - start));
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                start = index + 1;
            }

            if (start < text.Length)
                lines.Add(text.Substring(start));
            return lines.ToArray();
        }
    }
}
