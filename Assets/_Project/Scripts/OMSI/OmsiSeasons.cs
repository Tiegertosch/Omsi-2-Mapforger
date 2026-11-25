using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The kind of an [addseason] entry. Spring to snowy winter are the values the installed maps use; summer is the default of days
    /// no entry covers and is also accepted as 0 (OMSI's own editor writes it so); 5 is read as dry summer, as OmsiNewMap names it.
    /// </summary>
    public static class OmsiSeasonKind
    {
        public const int Summer = 0;
        public const int Spring = 1;
        public const int Autumn = 2;
        public const int Winter = 3;
        public const int SnowyWinter = 4;
        public const int DrySummer = 5;

        /// <summary>The kinds the editor offers for a new entry.</summary>
        public static readonly int[] Offered = { Summer, Spring, Autumn, Winter, SnowyWinter };

        public static bool IsKnown(int kind)
        {
            return kind >= Summer && kind <= DrySummer;
        }
    }

    /// <summary>One [addseason] of global.cfg: kind and the first and last day of the year it covers.</summary>
    public struct OmsiSeasonEntry : IEquatable<OmsiSeasonEntry>
    {
        public OmsiSeasonEntry(int kind, int firstDay, int lastDay)
        {
            Kind = kind;
            FirstDay = firstDay;
            LastDay = lastDay;
        }

        public int Kind { get; set; }
        public int FirstDay { get; set; }
        public int LastDay { get; set; }

        public bool Equals(OmsiSeasonEntry other)
        {
            return Kind == other.Kind && FirstDay == other.FirstDay && LastDay == other.LastDay;
        }

        public override bool Equals(object obj)
        {
            return obj is OmsiSeasonEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Kind * 397 ^ FirstDay) * 397 ^ LastDay;
        }
    }

    public enum OmsiSeasonProblem
    {
        /// <summary>The kind is negative.</summary>
        NegativeKind,

        /// <summary>The kind is not one the editor knows; OMSI may still read it.</summary>
        UnknownKind,

        /// <summary>A day lies outside 0 to <see cref="OmsiSeasons.MaximumDay"/>.</summary>
        DayOutOfRange,

        /// <summary>The last day lies before the first.</summary>
        LastBeforeFirst,

        /// <summary>The entry shares days with <see cref="OmsiSeasonFinding.Other"/> beyond a single day where one ends and the next starts.</summary>
        Overlap
    }

    /// <summary>A finding about the list of seasons; errors keep the list from being written, the others are only reported.</summary>
    public sealed class OmsiSeasonFinding
    {
        public OmsiSeasonFinding(int index, OmsiSeasonProblem problem, int other = -1)
        {
            Index = index;
            Problem = problem;
            Other = other;
        }

        /// <summary>The entry (zero-based) the finding is about.</summary>
        public int Index { get; }

        public OmsiSeasonProblem Problem { get; }

        /// <summary>The other entry of an overlap, or -1.</summary>
        public int Other { get; }

        public bool IsError => Problem == OmsiSeasonProblem.NegativeKind || Problem == OmsiSeasonProblem.DayOutOfRange ||
                               Problem == OmsiSeasonProblem.LastBeforeFirst;
    }

    /// <summary>
    /// Reads, checks and writes the [addseason] entries of global.cfg (feature parity I). A season runs from its first to its last day of
    /// a year; the installed maps count days 0 to 370 and let one season end on the day the next begins (38 and 38), which is not an overlap.
    /// Days no entry covers are summer. Writing changes only the records that differ.
    /// </summary>
    public static class OmsiSeasons
    {
        public const string SectionName = "addseason";

        /// <summary>The last day number the installed maps use (Grundorf ends its winter on 370).</summary>
        public const int MaximumDay = 370;

        /// <summary>The entries of the file in order; a record that is not three whole numbers is skipped.</summary>
        public static List<OmsiSeasonEntry> Read(OmsiRecordFile config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            var result = new List<OmsiSeasonEntry>();
            foreach (var record in config.Records)
                if (IsSeason(record) && TryRead(record, out var entry))
                    result.Add(entry);
            return result;
        }

        /// <summary>Findings about a list: errors first by entry, then the overlaps.</summary>
        public static List<OmsiSeasonFinding> Check(IList<OmsiSeasonEntry> entries)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            var findings = new List<OmsiSeasonFinding>();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry.Kind < 0)
                    findings.Add(new OmsiSeasonFinding(index, OmsiSeasonProblem.NegativeKind));
                else if (!OmsiSeasonKind.IsKnown(entry.Kind))
                    findings.Add(new OmsiSeasonFinding(index, OmsiSeasonProblem.UnknownKind));
                if (entry.FirstDay < 0 || entry.FirstDay > MaximumDay || entry.LastDay < 0 || entry.LastDay > MaximumDay)
                    findings.Add(new OmsiSeasonFinding(index, OmsiSeasonProblem.DayOutOfRange));
                else if (entry.LastDay < entry.FirstDay)
                    findings.Add(new OmsiSeasonFinding(index, OmsiSeasonProblem.LastBeforeFirst));
            }
            for (var first = 0; first < entries.Count; first++)
                for (var second = first + 1; second < entries.Count; second++)
                    if (Overlaps(entries[first], entries[second]))
                        findings.Add(new OmsiSeasonFinding(second, OmsiSeasonProblem.Overlap, first));
            return findings;
        }

        /// <summary>The first error of a list as a sentence, or null when it can be written.</summary>
        public static string FirstError(IList<OmsiSeasonEntry> entries)
        {
            foreach (var finding in Check(entries))
                if (finding.IsError)
                    return "Season " + (finding.Index + 1) + ": " + Describe(finding.Problem) + ".";
            return null;
        }

        /// <summary>
        /// Two seasons overlap when they share more than the one day where one ends and the other starts; entries with a bad range
        /// are left to their own finding.
        /// </summary>
        public static bool Overlaps(OmsiSeasonEntry a, OmsiSeasonEntry b)
        {
            if (a.LastDay < a.FirstDay || b.LastDay < b.FirstDay)
                return false;
            if (a.FirstDay == a.LastDay && b.FirstDay == b.LastDay)
                return a.FirstDay == b.FirstDay;
            return a.FirstDay < b.LastDay && b.FirstDay < a.LastDay;
        }

        /// <summary>
        /// Makes the [addseason] records say <paramref name="entries"/>: records that already match stay as they are, changed ones get
        /// the changed fields (and a fresh comment above), missing ones are added after the last, surplus ones are removed with their comment.
        /// Returns false when nothing had to change.
        /// </summary>
        public static bool Apply(OmsiRecordFile config, IList<OmsiSeasonEntry> entries)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            var error = FirstError(entries);
            if (error != null)
                throw new ArgumentException(error, nameof(entries));

            // Only well-formed records take part, so a damaged one is never overwritten by guesswork.
            var records = new List<OmsiRecord>();
            foreach (var record in config.Records)
                if (IsSeason(record) && TryRead(record, out _))
                    records.Add(record);
            // Match entries to records by what they say, so deleting from the middle removes that very record
            // and the others stay byte for byte (with their own comments); gaps are paired in order as edits.
            var currents = new List<OmsiSeasonEntry>();
            foreach (var record in records)
            {
                TryRead(record, out var read);
                currents.Add(read);
            }
            var assigned = Align(currents, entries, records);
            var changed = false;
            var kept = new HashSet<OmsiRecord>();
            foreach (var record in assigned)
                if (record != null)
                    kept.Add(record);
            foreach (var record in records)
                if (!kept.Contains(record))
                {
                    RemoveWithComment(config, record);
                    changed = true;
                }
            OmsiRecord previous = null;
            for (var index = 0; index < entries.Count; index++)
            {
                var target = assigned[index];
                if (target == null)
                {
                    InsertAfter(config, previous, entries[index], out target);
                    changed = true;
                }
                else
                {
                    TryRead(target, out var current);
                    if (!current.Equals(entries[index]))
                    {
                        SetIfDifferent(target, 0, entries[index].Kind);
                        SetIfDifferent(target, 1, entries[index].FirstDay);
                        SetIfDifferent(target, 2, entries[index].LastDay);
                        if (current.Kind != entries[index].Kind)
                            ReplaceComment(config, target, entries[index].Kind);
                        changed = true;
                    }
                }
                previous = target;
            }
            return changed;
        }

        /// <summary>
        /// For each wanted entry the record it keeps (identical ones aligned by longest common subsequence), a record of a gap
        /// that is rewritten for it, or null when a record has to be added. Records left over are the ones to remove.
        /// </summary>
        private static OmsiRecord[] Align(List<OmsiSeasonEntry> currents, IList<OmsiSeasonEntry> wanted, List<OmsiRecord> records)
        {
            var n = currents.Count;
            var m = wanted.Count;
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    lcs[i, j] = currents[i].Equals(wanted[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            var result = new OmsiRecord[m];
            var gapStart = 0;
            var wantedStart = 0;
            var x = 0;
            var y = 0;
            while (x < n && y < m)
            {
                if (currents[x].Equals(wanted[y]))
                {
                    PairGap(result, records, gapStart, x, wantedStart, y);
                    result[y] = records[x];
                    x++;
                    y++;
                    gapStart = x;
                    wantedStart = y;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
                    x++;
                else
                    y++;
            }
            PairGap(result, records, gapStart, n, wantedStart, m);
            return result;
        }

        private static void PairGap(OmsiRecord[] result, List<OmsiRecord> records, int recordFrom, int recordTo, int wantedFrom, int wantedTo)
        {
            var count = Math.Min(recordTo - recordFrom, wantedTo - wantedFrom);
            for (var offset = 0; offset < count; offset++)
                result[wantedFrom + offset] = records[recordFrom + offset];
        }

        /// <summary>The name of a season kind as OMSI's editor writes it above an entry.</summary>
        public static string CommentFor(int kind)
        {
            switch (kind)
            {
                case OmsiSeasonKind.Spring:
                    return "Spring:";
                case OmsiSeasonKind.Autumn:
                    return "Autumn:";
                case OmsiSeasonKind.Winter:
                    return "Winter:";
                case OmsiSeasonKind.SnowyWinter:
                    return "Winter (snow):";
                case OmsiSeasonKind.DrySummer:
                    return "Summer (dry):";
                default:
                    return "Summer:";
            }
        }

        internal static bool IsSeasonComment(string text)
        {
            var trimmed = text.Trim();
            return trimmed.EndsWith(":", StringComparison.Ordinal) &&
                   (trimmed.StartsWith("Winter", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Spring", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Autumn", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Summer", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Fall", StringComparison.OrdinalIgnoreCase));
        }

        private static string Describe(OmsiSeasonProblem problem)
        {
            switch (problem)
            {
                case OmsiSeasonProblem.NegativeKind:
                    return "the kind must not be negative";
                case OmsiSeasonProblem.DayOutOfRange:
                    return "the days lie between 0 and " + MaximumDay;
                case OmsiSeasonProblem.LastBeforeFirst:
                    return "the last day lies before the first";
                case OmsiSeasonProblem.UnknownKind:
                    return "the kind is not known";
                default:
                    return "the season overlaps another";
            }
        }

        private static bool IsSeason(OmsiRecord record)
        {
            return string.Equals(record.Name, SectionName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryRead(OmsiRecord record, out OmsiSeasonEntry entry)
        {
            entry = default;
            if (record.Fields.Count != 3 || !record.TryGetInt(0, out var kind) || !record.TryGetInt(1, out var first) || !record.TryGetInt(2, out var last))
                return false;
            entry = new OmsiSeasonEntry(kind, first, last);
            return true;
        }

        private static void SetIfDifferent(OmsiRecord record, int field, int value)
        {
            if (record.TryGetInt(field, out var now) && now == value)
                return;
            record.SetField(field, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void InsertAfter(OmsiRecordFile config, OmsiRecord previousSeason, OmsiSeasonEntry entry, out OmsiRecord record)
        {
            int insertAt;
            if (previousSeason != null)
                insertAt = config.Records.IndexOf(previousSeason) + 1;
            else
            {
                // OMSI's maps keep the seasons after the ground textures and before the traffic densities.
                insertAt = config.Records.FindLastIndex(candidate => string.Equals(candidate.Name, SectionName, StringComparison.OrdinalIgnoreCase)) + 1;
                if (insertAt <= 0)
                    insertAt = config.Records.FindLastIndex(candidate => string.Equals(candidate.Name, "groundtex", StringComparison.OrdinalIgnoreCase)) + 1;
                if (insertAt <= 0)
                {
                    var density = config.Records.FindIndex(candidate => candidate.Name.StartsWith("trafficdensity", StringComparison.OrdinalIgnoreCase));
                    insertAt = density >= 0 ? density : config.Records.Count;
                }
            }
            if (insertAt > 0)
                config.Records[insertAt - 1].Trailer.Add(new OmsiLine(" " + CommentFor(entry.Kind), config.DefaultLineEnding));
            record = config.CreateRecord(SectionName, new[]
            {
                entry.Kind.ToString(CultureInfo.InvariantCulture), entry.FirstDay.ToString(CultureInfo.InvariantCulture),
                entry.LastDay.ToString(CultureInfo.InvariantCulture)
            });
            config.InsertRecord(insertAt, record);
        }

        /// <summary>The comment line above a season is the last line of the trailer of the record before it.</summary>
        private static void ReplaceComment(OmsiRecordFile config, OmsiRecord season, int kind)
        {
            var index = config.Records.IndexOf(season);
            if (index <= 0)
                return;
            var trailer = config.Records[index - 1].Trailer;
            if (trailer.Count > 0 && IsSeasonComment(trailer[trailer.Count - 1].Text))
                trailer[trailer.Count - 1].Text = " " + CommentFor(kind);
        }

        /// <summary>
        /// Removes the record and only its own comment line; blank lines and foreign comments in its trailer
        /// (for example a note above the next section) move to the record before it.
        /// </summary>
        private static void RemoveWithComment(OmsiRecordFile config, OmsiRecord season)
        {
            var index = config.Records.IndexOf(season);
            if (index < 0)
                return;
            var target = index > 0 ? config.Records[index - 1].Trailer : config.Preamble;
            var removedComment = false;
            if (index > 0 && target.Count > 0 && IsSeasonComment(target[target.Count - 1].Text))
            {
                target.RemoveAt(target.Count - 1);
                removedComment = true;
            }
            var skipBlanks = removedComment && target.Count > 0 && target[target.Count - 1].Text.Trim().Length == 0;
            foreach (var line in season.Trailer)
            {
                if (skipBlanks && line.Text.Trim().Length == 0)
                    continue;
                skipBlanks = false;
                target.Add(line);
            }
            config.Records.Remove(season);
        }
    }
}
