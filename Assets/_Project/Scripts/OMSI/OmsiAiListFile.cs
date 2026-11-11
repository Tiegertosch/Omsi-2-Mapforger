using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One vehicle of a depot's type group: fleet number, plate, repaint and the first and last day it runs (YYYYMMDD), each possibly empty.</summary>
    public sealed class OmsiDepotVehicle
    {
        public string Number { get; set; } = string.Empty;
        public string Registration { get; set; } = string.Empty;
        public string Paint { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;

        /// <summary>The line as OMSI's editor writes it: tab-separated, as far as the last field that is not empty (at least the number and the plate).</summary>
        internal string ToLine()
        {
            var fields = new[] { Number, Registration, Paint, From, To };
            var last = fields.Length - 1;
            while (last > 1 && fields[last].Length == 0)
                last--;
            return string.Join("\t", fields.Take(last + 1));
        }

        internal static OmsiDepotVehicle Parse(string line)
        {
            var parts = line.Split('\t');
            string At(int index) => index < parts.Length ? parts[index] : string.Empty;
            return new OmsiDepotVehicle { Number = At(0).Trim(), Registration = At(1).Trim(), Paint = At(2).Trim(), From = At(3).Trim(), To = At(4).Trim() };
        }
    }

    /// <summary>A vehicle of a plain group (<c>[aigroup_2]</c>): its file and how often random traffic picks it.</summary>
    public sealed class OmsiAiPoolVehicle
    {
        public string File { get; set; } = string.Empty;
        public string Weight { get; set; } = string.Empty;
    }

    /// <summary>A depot's vehicle type: the vehicle file and the buses of that type with their numbers.</summary>
    public sealed class OmsiAiTypeGroup
    {
        public string File { get; set; } = string.Empty;
        public List<OmsiDepotVehicle> Vehicles { get; } = new List<OmsiDepotVehicle>();
    }

    /// <summary>A vehicle group of <c>ailists.cfg</c>: a depot (<c>[aigroup_depot]</c>, its type groups name the buses) or a plain pool (<c>[aigroup_2]</c>).</summary>
    public sealed class OmsiAiGroup
    {
        public string Name { get; set; } = string.Empty;
        public string Hof { get; set; } = string.Empty;
        public bool IsDepot { get; set; }
        public List<OmsiAiPoolVehicle> PoolVehicles { get; } = new List<OmsiAiPoolVehicle>();
        public List<OmsiAiTypeGroup> TypeGroups { get; } = new List<OmsiAiTypeGroup>();
    }

    /// <summary>
    /// <c>ailists.cfg</c> for editing (ADR 095): the vehicle groups tours name. A depot is a header (group name, depot file name) followed by type groups, each
    /// <c>[aigroup_depot_typgroup_2]</c> with a vehicle file and one tab-separated line per bus up to its own <c>[end]</c>; a pool is a name, an optional depot line
    /// and vehicle files with a weight up to <c>[end]</c>. Every edit changes only the lines it names; the rest of the file stays byte for byte.
    /// </summary>
    public sealed class OmsiAiListFile
    {
        private const string Pool = "aigroup_2";
        private const string Depot = "aigroup_depot";
        private const string TypeGroup = "aigroup_depot_typgroup_2";
        private const string End = "end";

        private readonly OmsiRecordFile file;

        private OmsiAiListFile(OmsiRecordFile file)
        {
            this.file = file;
        }

        /// <summary>Reads a file, or starts an empty one when <paramref name="bytes"/> is null.</summary>
        public static OmsiAiListFile Parse(byte[] bytes)
        {
            return new OmsiAiListFile(OmsiRecordFile.Parse(bytes ?? new byte[0], null));
        }

        public byte[] ToBytes()
        {
            return file.ToBytes();
        }

        /// <summary>The groups as the file has them now.</summary>
        public List<OmsiAiGroup> Groups => Spans().Select(Read).ToList();

        private sealed class Span
        {
            public int Header;
            /// <summary>Indexes of the type groups' records, each followed by its [end] record.</summary>
            public List<int> Types = new List<int>();
            public int EndIndex = -1;
            public bool IsDepot;
        }

        private static bool Is(OmsiRecord record, string name)
        {
            return string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        private List<Span> Spans()
        {
            var result = new List<Span>();
            Span current = null;
            for (var index = 0; index < file.Records.Count; index++)
            {
                var record = file.Records[index];
                if (Is(record, Pool) || Is(record, Depot))
                {
                    current = new Span { Header = index, IsDepot = Is(record, Depot) };
                    result.Add(current);
                }
                else if (current == null)
                {
                    continue;
                }
                else if (current.IsDepot && Is(record, TypeGroup))
                {
                    current.Types.Add(index);
                }
                else if (Is(record, End))
                {
                    if (!current.IsDepot)
                    {
                        current.EndIndex = index;
                        current = null;
                    }
                }
                else
                {
                    current = null;
                }
            }
            return result;
        }

        private static bool LooksLikeVehicle(string line)
        {
            var lower = line.ToLowerInvariant();
            return lower.Contains(".bus") || lower.Contains(".ovh") || lower.Contains(".sco");
        }

        private static string Text(OmsiRecord record, int index)
        {
            return index < record.Trailer.Count ? record.Trailer[index].Text.Trim() : string.Empty;
        }

        /// <summary>The line of a pool that holds the depot name, or -1 when the pool goes straight to its vehicles.</summary>
        private static int PoolHofLine(OmsiRecord header)
        {
            return header.Trailer.Count > 1 && !LooksLikeVehicle(header.Trailer[1].Text) ? 1 : -1;
        }

        /// <summary>The indexes of the non-blank lines of a record from <paramref name="first"/> on.</summary>
        private static List<int> FilledLines(OmsiRecord record, int first)
        {
            var result = new List<int>();
            for (var index = first; index < record.Trailer.Count; index++)
                if (record.Trailer[index].Text.Trim().Length > 0)
                    result.Add(index);
            return result;
        }

        private static int FirstVehicleLine(OmsiRecord header)
        {
            return PoolHofLine(header) >= 0 ? 2 : 1;
        }

        private OmsiAiGroup Read(Span span)
        {
            var header = file.Records[span.Header];
            var group = new OmsiAiGroup { IsDepot = span.IsDepot, Name = Text(header, 0) };
            if (span.IsDepot)
            {
                group.Hof = Text(header, 1);
                foreach (var typeIndex in span.Types)
                {
                    var record = file.Records[typeIndex];
                    var type = new OmsiAiTypeGroup { File = Text(record, 0) };
                    foreach (var line in FilledLines(record, 1))
                        type.Vehicles.Add(OmsiDepotVehicle.Parse(record.Trailer[line].Text));
                    group.TypeGroups.Add(type);
                }
                return group;
            }
            var hofLine = PoolHofLine(header);
            if (hofLine >= 0)
                group.Hof = Text(header, hofLine);
            foreach (var line in FilledLines(header, FirstVehicleLine(header)))
            {
                var text = header.Trailer[line].Text.TrimEnd();
                var tab = text.IndexOf('\t');
                group.PoolVehicles.Add(tab < 0
                    ? new OmsiAiPoolVehicle { File = text.Trim() }
                    : new OmsiAiPoolVehicle { File = text.Substring(0, tab).Trim(), Weight = text.Substring(tab + 1).Trim() });
            }
            return group;
        }

        // ---- editing ----

        private static string Clean(string text, string what)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)
                throw new ArgumentException(what + " must not contain a tab or a line break.");
            return value;
        }

        private static string Required(string text, string what)
        {
            var value = Clean(text, what);
            if (value.Length == 0)
                throw new ArgumentException(what + " must not be empty.");
            return value;
        }

        private static string Date(string text, string what)
        {
            var value = Clean(text, what);
            if (value.Length > 0 && (value.Length != 8 || !value.All(character => character >= '0' && character <= '9')))
                throw new ArgumentException(what + " must be a day as YYYYMMDD or empty.");
            return value;
        }

        private OmsiLine NewLine(string text)
        {
            return new OmsiLine(text, file.DefaultLineEnding);
        }

        private OmsiRecord NewRecord(string name, params string[] lines)
        {
            var record = file.CreateRecord(name, new string[0], 0);
            foreach (var line in lines)
                record.Trailer.Add(NewLine(line));
            return record;
        }

        private Span SpanAt(int group)
        {
            var spans = Spans();
            if (group < 0 || group >= spans.Count)
                throw new ArgumentOutOfRangeException(nameof(group));
            return spans[group];
        }

        private void CheckName(string name, int except)
        {
            var groups = Groups;
            for (var index = 0; index < groups.Count; index++)
                if (index != except && string.Equals(groups[index].Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The file has a group named " + name + " already.");
        }

        private int EndOfFile()
        {
            return file.Records.Count;
        }

        private void AppendRecords(params OmsiRecord[] records)
        {
            foreach (var record in records)
                file.InsertRecord(EndOfFile(), record);
        }

        /// <summary>Adds a plain group of vehicles that random traffic and tours can use.</summary>
        public int AddPool(string name)
        {
            name = Required(name, "The name");
            CheckName(name, -1);
            AppendRecords(NewRecord(Pool, name, string.Empty), NewRecord(End, string.Empty));
            return Spans().Count - 1;
        }

        /// <summary>Adds a depot; its buses come with the type groups added to it.</summary>
        public int AddDepot(string name, string hof)
        {
            name = Required(name, "The name");
            hof = Required(hof, "The depot");
            CheckName(name, -1);
            AppendRecords(NewRecord(Depot, name, hof, string.Empty));
            return Spans().Count - 1;
        }

        /// <summary>Takes a group out of the file with all that belongs to it.</summary>
        public void RemoveGroup(int group)
        {
            var span = SpanAt(group);
            var last = span.IsDepot ? (span.Types.Count > 0 ? AfterType(span.Types[span.Types.Count - 1]) - 1 : span.Header) : span.EndIndex;
            if (last < 0)
                last = span.Header;
            for (var index = last; index >= span.Header; index--)
                file.RemoveRecord(file.Records[index]);
        }

        public void SetName(int group, string name)
        {
            name = Required(name, "The name");
            CheckName(name, group);
            SetLine(file.Records[SpanAt(group).Header], 0, name);
        }

        /// <summary>Sets the depot file name of a depot (or of a pool that has such a line).</summary>
        public void SetHof(int group, string hof)
        {
            var span = SpanAt(group);
            var header = file.Records[span.Header];
            hof = span.IsDepot ? Required(hof, "The depot") : Clean(hof, "The depot");
            if (!span.IsDepot && PoolHofLine(header) < 0)
                throw new InvalidOperationException("This group has no depot line.");
            SetLine(header, 1, hof);
        }

        private void SetLine(OmsiRecord record, int index, string text)
        {
            while (record.Trailer.Count <= index)
                record.Trailer.Add(NewLine(string.Empty));
            record.Trailer[index].Text = text;
        }

        // ---- depots ----

        /// <summary>The index behind a type group's record and the [end] that closes it.</summary>
        private int AfterType(int typeIndex)
        {
            return typeIndex + 1 < file.Records.Count && Is(file.Records[typeIndex + 1], End) ? typeIndex + 2 : typeIndex + 1;
        }

        private OmsiRecord TypeRecord(int group, int type)
        {
            var span = SpanAt(group);
            if (!span.IsDepot || type < 0 || type >= span.Types.Count)
                throw new ArgumentOutOfRangeException(nameof(type));
            return file.Records[span.Types[type]];
        }

        /// <summary>Adds a vehicle type to a depot; the type group closes with its own [end].</summary>
        public int AddTypeGroup(int group, string vehicleFile)
        {
            vehicleFile = Required(vehicleFile, "The vehicle file");
            var span = SpanAt(group);
            if (!span.IsDepot)
                throw new InvalidOperationException("Only a depot has type groups.");
            var at = span.Types.Count > 0 ? AfterType(span.Types[span.Types.Count - 1]) : span.Header + 1;
            var type = NewRecord(TypeGroup, vehicleFile);
            var end = NewRecord(End, string.Empty);
            file.InsertRecord(at, type);
            file.InsertRecord(at + 1, end);
            return span.Types.Count;
        }

        public void RemoveTypeGroup(int group, int type)
        {
            var record = TypeRecord(group, type);
            var index = file.Records.IndexOf(record);
            if (index + 1 < file.Records.Count && Is(file.Records[index + 1], End))
                file.RemoveRecord(file.Records[index + 1]);
            file.RemoveRecord(record);
        }

        public void SetTypeFile(int group, int type, string vehicleFile)
        {
            SetLine(TypeRecord(group, type), 0, Required(vehicleFile, "The vehicle file"));
        }

        /// <summary>Adds a bus behind the others of its type group.</summary>
        public int AddVehicle(int group, int type, OmsiDepotVehicle vehicle)
        {
            var record = TypeRecord(group, type);
            var line = Format(vehicle);
            var filled = FilledLines(record, 1);
            record.Trailer.Insert(filled.Count > 0 ? filled[filled.Count - 1] + 1 : 1, NewLine(line));
            return filled.Count;
        }

        public void SetVehicle(int group, int type, int index, OmsiDepotVehicle vehicle)
        {
            var record = TypeRecord(group, type);
            var filled = FilledLines(record, 1);
            if (index < 0 || index >= filled.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            record.Trailer[filled[index]].Text = Format(vehicle);
        }

        public void RemoveVehicle(int group, int type, int index)
        {
            var record = TypeRecord(group, type);
            var filled = FilledLines(record, 1);
            if (index < 0 || index >= filled.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            record.Trailer.RemoveAt(filled[index]);
        }

        private static string Format(OmsiDepotVehicle vehicle)
        {
            if (vehicle == null)
                throw new ArgumentNullException(nameof(vehicle));
            return new OmsiDepotVehicle
            {
                Number = Required(vehicle.Number, "The fleet number"), Registration = Clean(vehicle.Registration, "The plate"), Paint = Clean(vehicle.Paint, "The repaint"),
                From = Date(vehicle.From, "The first day"), To = Date(vehicle.To, "The last day")
            }.ToLine();
        }

        // ---- pools ----

        private OmsiRecord PoolRecord(int group)
        {
            var span = SpanAt(group);
            if (span.IsDepot)
                throw new InvalidOperationException("A depot has type groups, not vehicles.");
            return file.Records[span.Header];
        }

        /// <summary>Adds a vehicle to a pool; <paramref name="weight"/> is how often traffic picks it among the others.</summary>
        public int AddPoolVehicle(int group, string vehicleFile, string weight)
        {
            var record = PoolRecord(group);
            var line = PoolLine(vehicleFile, weight);
            if (PoolHofLine(record) < 0 && record.Trailer.Count < 2)
                record.Trailer.Add(NewLine(string.Empty));
            var filled = FilledLines(record, FirstVehicleLine(record));
            record.Trailer.Insert(filled.Count > 0 ? filled[filled.Count - 1] + 1 : record.Trailer.Count, NewLine(line));
            return filled.Count;
        }

        public void SetPoolVehicle(int group, int index, string vehicleFile, string weight)
        {
            var record = PoolRecord(group);
            var filled = FilledLines(record, FirstVehicleLine(record));
            if (index < 0 || index >= filled.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            record.Trailer[filled[index]].Text = PoolLine(vehicleFile, weight);
        }

        public void RemovePoolVehicle(int group, int index)
        {
            var record = PoolRecord(group);
            var filled = FilledLines(record, FirstVehicleLine(record));
            if (index < 0 || index >= filled.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            record.Trailer.RemoveAt(filled[index]);
        }

        private static string PoolLine(string vehicleFile, string weight)
        {
            vehicleFile = Required(vehicleFile, "The vehicle file");
            weight = Clean(weight, "The weight");
            if (weight.Length > 0 && !double.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                throw new ArgumentException("The weight must be a number.");
            return weight.Length == 0 ? vehicleFile : vehicleFile + "\t" + weight;
        }
    }
}
