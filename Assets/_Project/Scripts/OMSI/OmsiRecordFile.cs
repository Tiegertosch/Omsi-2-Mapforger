using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One line of an OMSI text file with its original line ending ("\r\n", "\n", "\r" or none).</summary>
    public sealed class OmsiLine
    {
        public OmsiLine(string text, string ending)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Ending = ending ?? throw new ArgumentNullException(nameof(ending));
        }

        /// <summary>The line without its ending, exactly as stored.</summary>
        public string Text { get; internal set; }

        public string Ending { get; internal set; }
    }

    /// <summary>
    /// A section of an OMSI file: the header line such as <c>[object]</c>, the positional fields its layout defines,
    /// and the trailing blank lines and free text up to the next section. Sections without a known layout keep all
    /// of their lines in <see cref="Trailer"/>.
    /// </summary>
    public sealed class OmsiRecord
    {
        internal OmsiRecord(OmsiLine header, string name, bool hasKnownLayout)
        {
            Header = header;
            Name = name;
            HasKnownLayout = hasKnownLayout;
        }

        public OmsiLine Header { get; }

        /// <summary>The section name without brackets, for example <c>object</c>.</summary>
        public string Name { get; }

        public List<OmsiLine> Fields { get; } = new List<OmsiLine>();
        public List<OmsiLine> Trailer { get; } = new List<OmsiLine>();

        /// <summary>True when the section's field layout is known for the file's version.</summary>
        public bool HasKnownLayout { get; }

        /// <summary>False when the file ended or a count field was invalid before the layout was satisfied.</summary>
        public bool IsComplete { get; internal set; } = true;

        /// <summary>Returns a field value without surrounding whitespace.</summary>
        public string GetField(int index)
        {
            return Fields[index].Text.Trim();
        }

        /// <summary>Line ending for lines added to this record: the header's, or CRLF when it has none.</summary>
        private string NewLineEnding => Header.Ending.Length > 0 ? Header.Ending : "\r\n";

        /// <summary>Inserts a field line; used for structural changes such as the optional mirror line.</summary>
        public void InsertField(int index, string value)
        {
            CheckFieldText(value);
            Fields.Insert(index, new OmsiLine(value, NewLineEnding));
        }

        public void RemoveField(int index)
        {
            Fields.RemoveAt(index);
        }

        /// <summary>
        /// Replaces a count field and the lines it counts, for example an object's labels, and keeps the following
        /// fields in place.
        /// </summary>
        public void ReplaceCountedLines(int countIndex, IList<string> lines)
        {
            if (lines == null)
                throw new ArgumentNullException(nameof(lines));
            if (!TryGetInt(countIndex, out var oldCount) || oldCount < 0 || countIndex + 1 + oldCount > Fields.Count)
                throw new InvalidOperationException("Field " + countIndex + " of [" + Name + "] is not a valid count.");
            foreach (var line in lines)
                CheckFieldText(line);

            Fields.RemoveRange(countIndex + 1, oldCount);
            for (var index = 0; index < lines.Count; index++)
                Fields.Insert(countIndex + 1 + index, new OmsiLine(lines[index], NewLineEnding));
            SetField(countIndex, lines.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static void CheckFieldText(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException("A field value must not contain a line break.", nameof(value));
        }

        /// <summary>The last line of the record, or the header when it has no other lines.</summary>
        internal OmsiLine LastLine => Trailer.Count > 0 ? Trailer[Trailer.Count - 1] : Fields.Count > 0 ? Fields[Fields.Count - 1] : Header;

        /// <summary>Replaces a field value; the line keeps its original ending.</summary>
        public void SetField(int index, string value)
        {
            CheckFieldText(value);
            Fields[index].Text = value;
        }

        public bool TryGetInt(int index, out int value)
        {
            value = 0;
            return index >= 0 && index < Fields.Count &&
                   int.TryParse(GetField(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public bool TryGetDouble(int index, out double value)
        {
            value = 0.0;
            return index >= 0 && index < Fields.Count &&
                   double.TryParse(GetField(index), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Fields followed by trailer lines, trimmed; the view older readers use for sections without layout.</summary>
        public List<string> AllValues()
        {
            var values = new List<string>(Fields.Count + Trailer.Count);
            foreach (var line in Fields)
                values.Add(line.Text.Trim());
            foreach (var line in Trailer)
                values.Add(line.Text.Trim());
            return values;
        }

        internal void AppendTo(StringBuilder builder)
        {
            Append(builder, Header);
            foreach (var line in Fields)
                Append(builder, line);
            foreach (var line in Trailer)
                Append(builder, line);
        }

        internal static void Append(StringBuilder builder, OmsiLine line)
        {
            builder.Append(line.Text).Append(line.Ending);
        }
    }

    /// <summary>Supplies the number of positional field lines of a section, or null when the layout is unknown.</summary>
    public interface IOmsiRecordLayouts
    {
        /// <param name="name">Section name without brackets.</param>
        /// <param name="version">The file's [version] value seen so far, or -1.</param>
        /// <param name="lines">All lines of the file.</param>
        /// <param name="firstField">Index of the line after the section header.</param>
        /// <param name="isComplete">False when a count field needed to size the record is missing or invalid.</param>
        int? GetFieldCount(string name, int version, IReadOnlyList<OmsiLine> lines, int firstField, out bool isComplete);
    }

    /// <summary>
    /// An OMSI section file (map tiles, global.cfg, timetable and chrono files) that can be written back byte for
    /// byte. Records are read positionally, so a label that looks like a section header stays a field.
    /// </summary>
    public sealed class OmsiRecordFile
    {
        private OmsiRecordFile(OmsiTextEncoding encoding)
        {
            Encoding = encoding;
        }

        public OmsiTextEncoding Encoding { get; }

        /// <summary>Lines before the first section, such as "File created with OMSI-Editor".</summary>
        public List<OmsiLine> Preamble { get; } = new List<OmsiLine>();

        public List<OmsiRecord> Records { get; } = new List<OmsiRecord>();

        /// <summary>The [version] value, or -1 when the file has none.</summary>
        public int Version { get; private set; } = -1;

        /// <summary>
        /// True when writing the unchanged file reproduces the original bytes. This can only be false for text the
        /// decoder had to repair, such as an unpaired UTF-16 surrogate.
        /// </summary>
        public bool IsExactRoundTrip { get; private set; }

        /// <summary>The most common line ending, used for lines added later.</summary>
        public string DefaultLineEnding { get; private set; } = "\r\n";

        public static OmsiRecordFile Load(string path, IOmsiRecordLayouts layouts)
        {
            return Parse(File.ReadAllBytes(path), layouts);
        }

        public static OmsiRecordFile Parse(byte[] bytes, IOmsiRecordLayouts layouts)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            var text = OmsiTextFile.Decode(bytes, out var encoding);
            var file = new OmsiRecordFile(encoding);
            var lines = SplitLines(text);
            file.DefaultLineEnding = MostCommonEnding(lines);

            var index = 0;
            while (index < lines.Count && !TryGetSectionName(lines[index].Text, out _))
                file.Preamble.Add(lines[index++]);

            while (index < lines.Count)
            {
                TryGetSectionName(lines[index].Text, out var name);
                var header = lines[index++];
                var isComplete = true;
                var fieldCount = layouts?.GetFieldCount(name, file.Version, lines, index, out isComplete);
                var record = new OmsiRecord(header, name, fieldCount.HasValue);

                if (fieldCount.HasValue)
                {
                    var available = Math.Min(fieldCount.Value, lines.Count - index);
                    for (var field = 0; field < available; field++)
                        record.Fields.Add(lines[index++]);
                    record.IsComplete = isComplete && available == fieldCount.Value;
                }
                else
                {
                    record.IsComplete = isComplete;
                }

                while (index < lines.Count && !TryGetSectionName(lines[index].Text, out _))
                    record.Trailer.Add(lines[index++]);

                if (file.Version < 0 && string.Equals(name, "version", StringComparison.OrdinalIgnoreCase))
                {
                    var values = record.AllValues();
                    if (values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
                        file.Version = version;
                }

                file.Records.Add(record);
            }

            file.IsExactRoundTrip = ByteArraysEqual(bytes, file.ToBytes());
            return file;
        }

        /// <summary>
        /// Creates a record that uses this file's line ending, followed by blank lines. It is not part of the file
        /// until <see cref="InsertRecord"/> adds it.
        /// </summary>
        public OmsiRecord CreateRecord(string name, IEnumerable<string> fields, int blankLinesAfter = 1)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('[') >= 0 || name.IndexOf(']') >= 0)
                throw new ArgumentException("Expected a section name without brackets.", nameof(name));
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));

            var record = new OmsiRecord(new OmsiLine("[" + name + "]", DefaultLineEnding), name, true);
            foreach (var field in fields)
            {
                if (field == null || field.IndexOf('\r') >= 0 || field.IndexOf('\n') >= 0)
                    throw new ArgumentException("A field value must not be null or contain a line break.", nameof(fields));
                record.Fields.Add(new OmsiLine(field, DefaultLineEnding));
            }
            for (var line = 0; line < blankLinesAfter; line++)
                record.Trailer.Add(new OmsiLine(string.Empty, DefaultLineEnding));
            return record;
        }

        /// <summary>Inserts a record; a line before it that ended the file gets a line ending so the two stay apart.</summary>
        public void InsertRecord(int index, OmsiRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (Records.Contains(record))
                throw new InvalidOperationException("The record is already part of the file.");

            var previous = index > 0 ? Records[index - 1].LastLine : Preamble.Count > 0 ? Preamble[Preamble.Count - 1] : null;
            if (previous != null && previous.Ending.Length == 0)
                previous.Ending = DefaultLineEnding;
            Records.Insert(index, record);
        }

        public void RemoveRecord(OmsiRecord record)
        {
            if (!Records.Remove(record))
                throw new InvalidOperationException("The record is not part of the file.");
        }

        /// <summary>Serializes the file in its original encoding, line endings and record order.</summary>
        public byte[] ToBytes()
        {
            var builder = new StringBuilder();
            foreach (var line in Preamble)
                OmsiRecord.Append(builder, line);
            foreach (var record in Records)
                record.AppendTo(builder);
            return OmsiTextFile.Encode(builder.ToString(), Encoding);
        }

        /// <summary>True when the trimmed line is a section header such as [object].</summary>
        public static bool TryGetSectionName(string line, out string name)
        {
            var trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                name = trimmed.Substring(1, trimmed.Length - 2).Trim();
                return true;
            }

            name = null;
            return false;
        }

        private static List<OmsiLine> SplitLines(string text)
        {
            var lines = new List<OmsiLine>();
            var start = 0;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character != '\r' && character != '\n')
                    continue;

                var ending = character == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : character.ToString();
                lines.Add(new OmsiLine(text.Substring(start, index - start), ending));
                index += ending.Length - 1;
                start = index + 1;
            }

            if (start < text.Length)
                lines.Add(new OmsiLine(text.Substring(start), string.Empty));
            return lines;
        }

        private static string MostCommonEnding(List<OmsiLine> lines)
        {
            var counts = new Dictionary<string, int>();
            foreach (var line in lines)
            {
                if (line.Ending.Length == 0)
                    continue;
                counts.TryGetValue(line.Ending, out var count);
                counts[line.Ending] = count + 1;
            }

            var best = "\r\n";
            var bestCount = 0;
            foreach (var pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }

            return best;
        }

        private static bool ByteArraysEqual(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (var index = 0; index < first.Length; index++)
                if (first[index] != second[index])
                    return false;
            return true;
        }
    }
}
