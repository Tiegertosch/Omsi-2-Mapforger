using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmsiMapEditor.Core.Localization
{
    public enum LocaleIssueSeverity
    {
        Warning,
        Error
    }

    /// <summary>A finding about a language file: where it is and what is wrong with it.</summary>
    public sealed class LocaleIssue
    {
        public LocaleIssue(LocaleIssueSeverity severity, string file, int line, string key, string message)
        {
            Severity = severity;
            File = file ?? string.Empty;
            Line = line;
            Key = key ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public LocaleIssueSeverity Severity { get; }
        public string File { get; }

        /// <summary>The line in the file, from 1; 0 when the finding does not belong to one line.</summary>
        public int Line { get; }

        public string Key { get; }
        public string Message { get; }

        public override string ToString()
        {
            return (Severity == LocaleIssueSeverity.Error ? "error" : "warning") + ": " + File + (Line > 0 ? "(" + Line + ")" : string.Empty) + ": " +
                   (Key.Length > 0 ? "[" + Key + "] " : string.Empty) + Message;
        }
    }

    /// <summary>One key with its text and the line it stands on.</summary>
    public sealed class LocaleEntry
    {
        public LocaleEntry(string key, string value, int line)
        {
            Key = key;
            Value = value;
            Line = line;
        }

        public string Key { get; }
        public string Value { get; }
        public int Line { get; }
    }

    /// <summary>
    /// A language file as the program reads it: one JSON object whose values are all strings. Reads without Unity, reports the
    /// line of every finding, and sees duplicate keys, which Unity's JsonUtility cannot. A syntax error ends the reading; what
    /// was read before it stays.
    /// </summary>
    public sealed class LocaleJson
    {
        private readonly string text;
        private readonly string file;
        private int position;
        private int line = 1;

        private LocaleJson(string text, string file)
        {
            this.text = text ?? string.Empty;
            this.file = file;
        }

        /// <summary>The entries in file order; a key that stands twice is in the list twice.</summary>
        public List<LocaleEntry> Entries { get; } = new List<LocaleEntry>();

        public List<LocaleIssue> Issues { get; } = new List<LocaleIssue>();

        public bool HasErrors => Issues.Exists(issue => issue.Severity == LocaleIssueSeverity.Error);

        public static LocaleJson Parse(string text, string file)
        {
            var json = new LocaleJson(text, file);
            json.ReadDocument();
            return json;
        }

        private void ReadDocument()
        {
            if (text.Length > 0 && text[0] == '﻿')
                position = 1;
            SkipSpace();
            if (!Expect('{'))
                return;
            SkipSpace();
            if (Peek() == '}')
            {
                position++;
                CheckEnd();
                return;
            }
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            while (true)
            {
                SkipSpace();
                var keyLine = line;
                if (Peek() != '"')
                {
                    Error(keyLine, null, "a key in quotes was expected" + Near());
                    return;
                }
                if (!ReadString(out var key))
                    return;
                SkipSpace();
                if (!Expect(':'))
                    return;
                SkipSpace();
                if (Peek() == '"')
                {
                    if (!ReadString(out var value))
                        return;
                    if (seen.TryGetValue(key, out var first))
                        Error(keyLine, key, "the key is defined twice (first on line " + first + ")");
                    else
                        seen[key] = keyLine;
                    Entries.Add(new LocaleEntry(key, value, keyLine));
                }
                else
                {
                    Error(keyLine, key, "the value must be a text in quotes, no number, list or nested object");
                    if (!SkipValue())
                        return;
                }
                SkipSpace();
                var next = Peek();
                if (next == ',')
                {
                    position++;
                    continue;
                }
                if (next == '}')
                {
                    position++;
                    CheckEnd();
                    return;
                }
                Error(line, null, "',' or '}' was expected" + Near());
                return;
            }
        }

        private void CheckEnd()
        {
            SkipSpace();
            if (position < text.Length)
                Error(line, null, "unexpected text after the closing '}'" + Near());
        }

        private char Peek()
        {
            return position < text.Length ? text[position] : '\0';
        }

        private bool Expect(char expected)
        {
            if (Peek() == expected)
            {
                position++;
                return true;
            }
            Error(line, null, "'" + expected + "' was expected" + Near());
            return false;
        }

        private string Near()
        {
            if (position >= text.Length)
                return " but the file ends";
            var length = Math.Min(12, text.Length - position);
            return " near \"" + text.Substring(position, length).Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private void SkipSpace()
        {
            while (position < text.Length)
            {
                var c = text[position];
                if (c == '\n')
                    line++;
                else if (c != ' ' && c != '\t' && c != '\r')
                    break;
                position++;
            }
        }

        private bool ReadString(out string value)
        {
            value = null;
            var startLine = line;
            position++;
            var builder = new StringBuilder();
            while (position < text.Length)
            {
                var c = text[position++];
                if (c == '"')
                {
                    value = builder.ToString();
                    return true;
                }
                if (c == '\n' || c == '\r')
                {
                    Error(startLine, null, "a text is not closed before the end of the line");
                    return false;
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (position >= text.Length)
                    break;
                var escaped = text[position++];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (position + 4 > text.Length ||
                            !int.TryParse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        {
                            Error(line, null, "\\u must be followed by four hexadecimal digits");
                            return false;
                        }
                        builder.Append((char)code);
                        position += 4;
                        break;
                    default:
                        Error(line, null, "the escape \\" + escaped + " is not valid JSON");
                        return false;
                }
            }
            Error(startLine, null, "a text is not closed before the end of the file");
            return false;
        }

        /// <summary>Skips a value that is not a text, only to go on reading after it.</summary>
        private bool SkipValue()
        {
            var depth = 0;
            while (position < text.Length)
            {
                var c = text[position];
                if (c == '"')
                {
                    if (!ReadString(out _))
                        return false;
                    continue;
                }
                if (c == '{' || c == '[')
                    depth++;
                else if (c == '}' || c == ']')
                {
                    if (depth == 0)
                        return true;
                    depth--;
                }
                else if (c == ',' && depth == 0)
                    return true;
                else if (c == '\n')
                    line++;
                position++;
            }
            Error(line, null, "the file ends inside a value");
            return false;
        }

        private void Error(int atLine, string key, string message)
        {
            Issues.Add(new LocaleIssue(LocaleIssueSeverity.Error, file, atLine, key, message));
        }
    }
}
