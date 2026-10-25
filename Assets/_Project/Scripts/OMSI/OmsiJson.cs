using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A small JSON reader for the answers of web services (OpenStreetMap's Overpass): objects become dictionaries,
    /// arrays lists, numbers doubles, true and false booleans, null null.
    /// </summary>
    public static class OmsiJson
    {
        /// <exception cref="InvalidDataException">The text is not JSON.</exception>
        public static object Parse(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            var position = 0;
            var value = Value(text, ref position);
            Space(text, ref position);
            if (position != text.Length)
                throw new InvalidDataException("JSON: unexpected text at " + position + ".");
            return value;
        }

        private static object Value(string text, ref int position)
        {
            Space(text, ref position);
            if (position >= text.Length)
                throw new InvalidDataException("JSON: the text ends too early.");
            switch (text[position])
            {
                case '{':
                    return Object(text, ref position);
                case '[':
                    return Array(text, ref position);
                case '"':
                    return String(text, ref position);
                case 't':
                    return Word(text, ref position, "true", true);
                case 'f':
                    return Word(text, ref position, "false", false);
                case 'n':
                    return Word(text, ref position, "null", null);
                default:
                    return Number(text, ref position);
            }
        }

        private static Dictionary<string, object> Object(string text, ref int position)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            position++;
            Space(text, ref position);
            if (position < text.Length && text[position] == '}')
            {
                position++;
                return result;
            }
            while (true)
            {
                Space(text, ref position);
                if (position >= text.Length || text[position] != '"')
                    throw new InvalidDataException("JSON: a name was expected at " + position + ".");
                var name = String(text, ref position);
                Space(text, ref position);
                Expect(text, ref position, ':');
                result[name] = Value(text, ref position);
                Space(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    continue;
                }
                Expect(text, ref position, '}');
                return result;
            }
        }

        private static List<object> Array(string text, ref int position)
        {
            var result = new List<object>();
            position++;
            Space(text, ref position);
            if (position < text.Length && text[position] == ']')
            {
                position++;
                return result;
            }
            while (true)
            {
                result.Add(Value(text, ref position));
                Space(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    continue;
                }
                Expect(text, ref position, ']');
                return result;
            }
        }

        private static string String(string text, ref int position)
        {
            position++;
            var result = new StringBuilder();
            while (position < text.Length)
            {
                var c = text[position++];
                if (c == '"')
                    return result.ToString();
                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }
                if (position >= text.Length)
                    break;
                var escape = text[position++];
                switch (escape)
                {
                    case 'n': result.Append('\n'); break;
                    case 't': result.Append('\t'); break;
                    case 'r': result.Append('\r'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'u':
                        if (position + 4 > text.Length)
                            throw new InvalidDataException("JSON: a broken \\u escape.");
                        result.Append((char)int.Parse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        position += 4;
                        break;
                    default: result.Append(escape); break;
                }
            }
            throw new InvalidDataException("JSON: a text is not closed.");
        }

        private static double Number(string text, ref int position)
        {
            var start = position;
            while (position < text.Length && "+-0123456789.eE".IndexOf(text[position]) >= 0)
                position++;
            if (position == start || !double.TryParse(text.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new InvalidDataException("JSON: unexpected text at " + start + ".");
            return number;
        }

        private static object Word(string text, ref int position, string word, object value)
        {
            if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0)
                throw new InvalidDataException("JSON: unexpected text at " + position + ".");
            position += word.Length;
            return value;
        }

        private static void Space(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
        }

        private static void Expect(string text, ref int position, char c)
        {
            if (position >= text.Length || text[position] != c)
                throw new InvalidDataException("JSON: '" + c + "' was expected at " + position + ".");
            position++;
        }
    }
}
