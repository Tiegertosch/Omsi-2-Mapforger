using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Reads OMSI text files independent of the runtime's default code page.
    /// OMSI writes UTF-16 LE with a byte order mark; older or hand-edited files are UTF-8 or Windows-1252.
    /// </summary>
    public static class OmsiTextFile
    {
        // Windows-1252 differs from ISO-8859-1 only in 0x80-0x9F. Undefined bytes map to U+FFFD.
        private static readonly char[] Windows1252HighControlRange =
        {
            '€', '�', '‚', 'ƒ', '„', '…', '†', '‡',
            'ˆ', '‰', 'Š', '‹', 'Œ', '�', 'Ž', '�',
            '�', '‘', '’', '“', '”', '•', '–', '—',
            '˜', '™', 'š', '›', 'œ', '�', 'ž', 'Ÿ'
        };

        public static string[] ReadAllLines(string path)
        {
            return SplitLines(Decode(File.ReadAllBytes(path)));
        }

        /// <summary>Decodes bytes using a byte order mark, then strict UTF-8, then Windows-1252.</summary>
        public static string Decode(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new UnicodeEncoding(false, false).GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new UnicodeEncoding(true, false).GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return DecodeWindows1252(bytes);
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
