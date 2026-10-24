using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    public enum OmsiCheckStatus
    {
        /// <summary>Nothing to report.</summary>
        Passed,

        /// <summary>Worth knowing, but OMSI copes with it.</summary>
        Note,

        /// <summary>Probably a mistake in the map data.</summary>
        Problem
    }

    /// <summary>One topic of the map check, such as spline connections or the timetable.</summary>
    public sealed class OmsiCheckSection
    {
        public OmsiCheckSection(string title)
        {
            Title = title;
        }

        public string Title { get; }
        public OmsiCheckStatus Status { get; set; }

        /// <summary>One sentence on the outcome.</summary>
        public string Summary { get; set; } = string.Empty;

        public List<string> Findings { get; } = new List<string>();

        /// <summary>A tile worth looking at first, such as the one with the largest spline gap, or null.</summary>
        public OmsiTileSummary Tile { get; set; }
    }

    /// <summary>The outcome of a map check, section by section.</summary>
    public sealed class OmsiMapCheckReport
    {
        public List<OmsiCheckSection> Sections { get; } = new List<OmsiCheckSection>();

        public int ProblemCount
        {
            get
            {
                var count = 0;
                foreach (var section in Sections)
                    if (section.Status == OmsiCheckStatus.Problem)
                        count++;
                return count;
            }
        }

        /// <summary>The report as plain text, for copying into a message or an issue.</summary>
        public string ToText(string mapName)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("Map check of " + mapName);
            foreach (var section in Sections)
            {
                text.AppendLine();
                text.AppendLine(section.Title + " - " + section.Status + ": " + section.Summary);
                foreach (var finding in section.Findings)
                    text.AppendLine("  - " + finding);
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// Checks a map for what the format study suggests: import warnings, spline connections, map-wide unique IDs and the
    /// ID counter, entry points on their object, attachments on existing parents and splines, variable parents that
    /// exist, object and spline files the installation has, the connection points of junction objects, the timetable and
    /// the selections of the chrono variants.
    /// Reads the map files again, so it belongs on a worker thread; nothing is written.
    /// </summary>
    public static class OmsiMapCheck
    {
        /// <summary>At most this many findings are listed per section; the summary gives the full count.</summary>
        public const int MaximumFindings = 200;

        public static OmsiMapCheckReport Run(OmsiMapSummary summary, IOmsiFileSource source, string installationRoot,
            string globalConfigRelativePath = "global.cfg")
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var report = new OmsiMapCheckReport();
            report.Sections.Add(Import(summary));
            report.Sections.Add(SplineConnections(summary));
            var tiles = ReadTiles(summary, source);
            report.Sections.Add(Ids(summary, tiles));
            report.Sections.Add(EntryPoints(summary, source, globalConfigRelativePath, tiles));
            report.Sections.Add(Attachments(summary, tiles));
            report.Sections.Add(VariableParents(summary, tiles));
            if (!string.IsNullOrEmpty(installationRoot))
            {
                report.Sections.Add(Files(summary, installationRoot));
                report.Sections.Add(Junctions(summary, installationRoot));
            }
            var variants = OmsiChrono.ReadVariants(source);
            report.Sections.Add(Timetable(summary, source, variants));
            report.Sections.Add(Chrono(summary, source, variants));
            return report;
        }

        private static OmsiCheckSection Import(OmsiMapSummary summary)
        {
            var section = new OmsiCheckSection("Import");
            foreach (var warning in summary.Warnings)
                Add(section, warning);
            section.Status = summary.Warnings.Count == 0 ? OmsiCheckStatus.Passed : OmsiCheckStatus.Note;
            section.Summary = summary.Warnings.Count == 0
                ? "All " + summary.Tiles.Count + " tiles read without warnings."
                : summary.Warnings.Count + " warning(s) while reading " + summary.Tiles.Count + " tiles.";
            return section;
        }

        private static OmsiCheckSection SplineConnections(OmsiMapSummary summary)
        {
            var section = new OmsiCheckSection("Spline connections");
            var continuity = OmsiSplineContinuity.Check(summary);
            foreach (var gap in continuity.Gaps)
                Add(section, "splines " + gap.SplineId + " and " + gap.NeighbourId + " are " + gap.DistanceMeters.ToString("0.00", CultureInfo.InvariantCulture) +
                             " m apart in tile " + gap.Tile.X + ", " + gap.Tile.Y + (gap.IsMutual ? string.Empty : "; only one of them names the other"));
            if (continuity.Gaps.Count > 0)
                section.Tile = continuity.Gaps[0].Tile;
            section.Status = continuity.Gaps.Count > 0 || continuity.MissingNeighbourCount > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = continuity.ConnectedCount + " of " + continuity.LinkCount + " links meet within " +
                              continuity.ToleranceMeters.ToString("0.0#", CultureInfo.InvariantCulture) + " m" +
                              (continuity.MissingNeighbourCount > 0 ? "; " + continuity.MissingNeighbourCount + " link(s) name splines the map does not have" : string.Empty) +
                              ".";
            return section;
        }

        /// <summary>The record file of each available tile, by its index in the map list; unreadable tiles are left out.</summary>
        private static Dictionary<int, OmsiRecordFile> ReadTiles(OmsiMapSummary summary, IOmsiFileSource source)
        {
            var tiles = new Dictionary<int, OmsiRecordFile>();
            for (var index = 0; index < summary.Tiles.Count; index++)
            {
                var tile = summary.Tiles[index];
                if (!tile.IsAvailable)
                    continue;
                try
                {
                    tiles[index] = OmsiRecordFile.Parse(source.ReadAllBytes(OmsiRelativePath.Normalize(tile.FileName)), OmsiTileRecordLayouts.Instance);
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
                {
                    // The import section already names tiles that could not be read.
                }
            }
            return tiles;
        }

        private static OmsiCheckSection Ids(OmsiMapSummary summary, Dictionary<int, OmsiRecordFile> tiles)
        {
            var section = new OmsiCheckSection("IDs");
            var seen = new Dictionary<int, OmsiTileSummary>();
            var duplicates = 0;
            var highest = 0;
            foreach (var pair in tiles)
            {
                var tile = summary.Tiles[pair.Key];
                foreach (var id in OmsiTileRecords.DefinedIds(pair.Value))
                {
                    highest = Math.Max(highest, id);
                    if (seen.TryGetValue(id, out var first))
                    {
                        duplicates++;
                        Add(section, "ID " + id + " is used in tile " + first.X + ", " + first.Y + " and again in tile " + tile.X + ", " + tile.Y);
                        section.Tile = section.Tile ?? tile;
                    }
                    else
                    {
                        seen[id] = tile;
                    }
                }
            }
            var stale = summary.NextIdCode.HasValue && summary.NextIdCode.Value <= highest;
            if (stale)
                Add(section, "[NextIDCode] is " + summary.NextIdCode.Value + ", but ID " + highest + " is already used; new IDs continue above " + highest);
            if (!summary.NextIdCode.HasValue)
                Add(section, "global.cfg has no [NextIDCode]");
            section.Status = duplicates > 0 ? OmsiCheckStatus.Problem : stale || !summary.NextIdCode.HasValue ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = seen.Count + " IDs in the base tiles, " + (duplicates == 0 ? "each used once" : duplicates + " used more than once") +
                              (stale ? "; the ID counter is behind." : ".");
            return section;
        }

        private static OmsiCheckSection EntryPoints(OmsiMapSummary summary, IOmsiFileSource source, string globalConfigRelativePath,
            Dictionary<int, OmsiRecordFile> tiles)
        {
            var section = new OmsiCheckSection("Entry points");
            var config = OmsiRecordFile.Parse(source.ReadAllBytes(globalConfigRelativePath), OmsiGlobalConfigLayouts.Instance);
            var count = 0;
            var wrong = 0;
            foreach (var record in config.Records)
            {
                if (!string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase) || !record.HasKnownLayout)
                    continue;
                for (var field = 1; field + 11 < record.Fields.Count; field += 12)
                {
                    count++;
                    var name = record.GetField(field + 11);
                    if (!record.TryGetInt(field, out var placedIndex) || !record.TryGetInt(field + 1, out var id) || !record.TryGetInt(field + 10, out var tileIndex))
                    {
                        wrong++;
                        Add(section, "entry point \"" + name + "\" has fields that are no numbers");
                        continue;
                    }
                    if (!tiles.TryGetValue(tileIndex, out var tile))
                    {
                        wrong++;
                        Add(section, "entry point \"" + name + "\" lies in tile index " + tileIndex + ", which the map does not have or could not read");
                        continue;
                    }
                    var position = FindPlaced(tile, id, out var actualIndex, out var heading);
                    if (position < 0)
                    {
                        wrong++;
                        Add(section, "entry point \"" + name + "\" names object " + id + ", which tile " + summary.Tiles[tileIndex].X + ", " +
                                     summary.Tiles[tileIndex].Y + " does not contain");
                        section.Tile = section.Tile ?? summary.Tiles[tileIndex];
                        continue;
                    }
                    if (actualIndex != placedIndex)
                    {
                        wrong++;
                        Add(section, "entry point \"" + name + "\" counts object " + id + " as number " + placedIndex + " of its tile; it is number " + actualIndex);
                    }
                    if (heading.HasValue && record.TryGetDouble(field + 7, out var qy) && record.TryGetDouble(field + 9, out var qw))
                    {
                        var angle = 2.0 * Math.Atan2(qy, qw) * 180.0 / Math.PI;
                        var difference = Math.Abs(((angle - heading.Value) % 360.0 + 540.0) % 360.0 - 180.0);
                        if (difference > 5.0)
                            Add(section, "entry point \"" + name + "\" faces " + difference.ToString("0", CultureInfo.InvariantCulture) + "° away from its object");
                    }
                }
            }
            section.Status = wrong > 0 ? OmsiCheckStatus.Problem : section.Findings.Count > 0 ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = count == 0
                ? "The map has no entry points; OMSI needs one to start a bus on it."
                : count - wrong + " of " + count + " entry points lie on their object.";
            if (count == 0)
                section.Status = OmsiCheckStatus.Note;
            return section;
        }

        /// <summary>The record position of a placed object by ID, its number among the placed objects and its heading.</summary>
        private static int FindPlaced(OmsiRecordFile tile, int id, out int placedIndex, out double? heading)
        {
            placedIndex = 0;
            heading = null;
            for (var position = 0; position < tile.Records.Count; position++)
            {
                var record = tile.Records[position];
                if (!OmsiTileEditing.PlacedObjectSections.Contains(record.Name))
                    continue;
                switch (OmsiTileRecords.Create(tile, record))
                {
                    case OmsiObjectView item when item.Id == id:
                        heading = item.HeadingDegrees;
                        return position;
                    case OmsiAttachedObjectView attached when attached.Id == id:
                        return position;
                    case OmsiSplineAttachmentView attachment when attachment.Id == id:
                        return position;
                }
                placedIndex++;
            }
            return -1;
        }

        private static OmsiCheckSection Attachments(OmsiMapSummary summary, Dictionary<int, OmsiRecordFile> tiles)
        {
            var section = new OmsiCheckSection("Attachments");
            var masters = new HashSet<int>();
            var repeaters = new List<KeyValuePair<int, OmsiTileSummary>>();
            var attachedCount = 0;
            var rowCount = 0;
            foreach (var pair in tiles)
            {
                var tile = summary.Tiles[pair.Key];
                var earlier = new HashSet<int>();
                var splines = 0;
                foreach (var record in pair.Value.Records)
                {
                    switch (OmsiTileRecords.Create(pair.Value, record))
                    {
                        case OmsiObjectView item:
                            earlier.Add(item.Id);
                            break;
                        case OmsiSplineView _:
                            splines++;
                            break;
                        case OmsiAttachedObjectView attached:
                            attachedCount++;
                            if (!earlier.Contains(attached.ParentId))
                            {
                                Add(section, "attached object " + attached.Id + " in tile " + tile.X + ", " + tile.Y + " hangs on " + attached.ParentId +
                                             ", which is not written before it in the same tile");
                                section.Tile = section.Tile ?? tile;
                            }
                            earlier.Add(attached.Id);
                            break;
                        case OmsiSplineAttachmentView attachment:
                            rowCount++;
                            if (attachment.SplineIndex < 0 || attachment.SplineIndex >= TileSplineCount(pair.Value))
                            {
                                Add(section, (attachment.IsRepeater ? "continued row " : "row ") + attachment.Id + " in tile " + tile.X + ", " + tile.Y +
                                             " lies on spline number " + attachment.SplineIndex + ", but the tile has " + TileSplineCount(pair.Value) + " splines");
                                section.Tile = section.Tile ?? tile;
                            }
                            if (attachment.IsRepeater)
                                repeaters.Add(new KeyValuePair<int, OmsiTileSummary>(attachment.Id, tile));
                            else
                                masters.Add(attachment.Id);
                            // Objects along splines carry attached objects too, such as signs on a row of posts.
                            earlier.Add(attachment.Id);
                            break;
                    }
                }
            }
            foreach (var pair in repeaters)
                if (!masters.Contains(pair.Key))
                    Add(section, "continued row " + pair.Key + " in tile " + pair.Value.X + ", " + pair.Value.Y + " has no row it continues");
            section.Status = section.Findings.Count > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = attachedCount + " attached objects and " + rowCount + " rows along splines" +
                              (section.Findings.Count == 0 ? ", all on existing parents and splines." : "; " + section.Findings.Count + " without their parent or spline.");
            return section;
        }

        private static int TileSplineCount(OmsiRecordFile tile)
        {
            var count = 0;
            foreach (var record in tile.Records)
                if (string.Equals(record.Name, "spline", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Name, "spline_h", StringComparison.OrdinalIgnoreCase))
                    count++;
            return count;
        }

        private static OmsiCheckSection VariableParents(OmsiMapSummary summary, Dictionary<int, OmsiRecordFile> tiles)
        {
            var section = new OmsiCheckSection("Variable parents");
            var ids = new HashSet<int>();
            foreach (var tile in tiles.Values)
                foreach (var id in OmsiTileRecords.DefinedIds(tile))
                    ids.Add(id);
            var count = 0;
            var missing = 0;
            foreach (var pair in tiles)
            {
                var missingPerParent = new Dictionary<int, int>();
                foreach (var record in pair.Value.Records)
                {
                    if (!string.Equals(record.Name, "varparent", StringComparison.OrdinalIgnoreCase) || !record.TryGetInt(0, out var parent))
                        continue;
                    count++;
                    if (ids.Contains(parent))
                        continue;
                    missing++;
                    missingPerParent.TryGetValue(parent, out var elements);
                    missingPerParent[parent] = elements + 1;
                }

                var tile = summary.Tiles[pair.Key];
                foreach (var parent in missingPerParent)
                {
                    Add(section, (parent.Value == 1 ? "an element of tile " + tile.X + ", " + tile.Y + " takes its"
                            : parent.Value + " elements of tile " + tile.X + ", " + tile.Y + " take their") +
                        " variables from " + parent.Key + ", which the map does not have");
                    section.Tile = section.Tile ?? tile;
                }
            }
            section.Status = missing > 0 ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = count == 0 ? "No element takes its variables from another." : count - missing + " of " + count + " variable parents exist.";
            return section;
        }

        private static OmsiCheckSection Files(OmsiMapSummary summary, string installationRoot)
        {
            var section = new OmsiCheckSection("Object and spline files");
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in summary.Tiles)
            {
                foreach (var item in tile.Objects)
                    paths.Add(item.AssetPath);
                foreach (var item in tile.AttachedObjects)
                    paths.Add(item.AssetPath);
                foreach (var item in tile.SplineAttachments)
                    paths.Add(item.AssetPath);
                foreach (var item in tile.Splines)
                    paths.Add(item.AssetPath);
            }
            var missing = 0;
            foreach (var path in paths)
            {
                bool exists;
                try
                {
                    exists = File.Exists(OmsiRelativePath.Resolve(installationRoot, path));
                }
                catch (ArgumentException)
                {
                    exists = false;
                }
                if (exists)
                    continue;
                missing++;
                Add(section, path + " is not in the OMSI installation");
            }
            section.Status = missing > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = paths.Count - missing + " of " + paths.Count + " object and spline files are installed.";
            return section;
        }

        /// <summary>
        /// The connection points of junction objects with their state (ADR 024): invalid ones first, then those to review,
        /// then free ones, which may be meant as open ends.
        /// </summary>
        private static OmsiCheckSection Junctions(OmsiMapSummary summary, string installationRoot)
        {
            var section = new OmsiCheckSection("Junctions");
            var checks = OmsiJunctionCatalog.Check(summary, new OmsiJunctionCatalog(installationRoot).AllPorts(summary));
            var objects = new HashSet<OmsiObjectRecord>();
            var counts = new Dictionary<OmsiJunctionPortState, int>();
            foreach (OmsiJunctionPortState state in Enum.GetValues(typeof(OmsiJunctionPortState)))
                counts[state] = 0;
            foreach (var check in checks)
            {
                objects.Add(check.Port.Object);
                counts[check.State]++;
            }
            foreach (var state in new[] { OmsiJunctionPortState.Invalid, OmsiJunctionPortState.NeedsReview, OmsiJunctionPortState.Unconnected })
                foreach (var check in checks)
                {
                    if (check.State != state)
                        continue;
                    section.Tile = section.Tile ?? check.Port.Tile;
                    Add(section, (state == OmsiJunctionPortState.Invalid ? "invalid: " : state == OmsiJunctionPortState.NeedsReview ? "review: " : "free: ") +
                                 "#" + check.Port.Object.Id + " " + Path.GetFileNameWithoutExtension(check.Port.Object.AssetPath ?? string.Empty) + " in tile " +
                                 check.Port.Tile.X + ", " + check.Port.Tile.Y + ": " + check.Message);
                }
            section.Status = counts[OmsiJunctionPortState.Invalid] > 0 ? OmsiCheckStatus.Problem
                : counts[OmsiJunctionPortState.NeedsReview] > 0 || counts[OmsiJunctionPortState.Unconnected] > 0 ? OmsiCheckStatus.Note
                : OmsiCheckStatus.Passed;
            section.Summary = checks.Count == 0
                ? "No object of the map has connection points."
                : counts[OmsiJunctionPortState.Connected] + " of " + checks.Count + " connection points of " + objects.Count + " junction objects are connected; " +
                  counts[OmsiJunctionPortState.NeedsReview] + " to review, " + counts[OmsiJunctionPortState.Invalid] + " invalid, " +
                  counts[OmsiJunctionPortState.Unconnected] + " free.";
            return section;
        }

        private static OmsiCheckSection Timetable(OmsiMapSummary summary, IOmsiFileSource source, List<OmsiChronoVariant> variants)
        {
            var section = new OmsiCheckSection("Timetable");
            var timetable = OmsiTimetable.Read(source);
            if (timetable.IsEmpty)
            {
                section.Status = OmsiCheckStatus.Passed;
                section.Summary = "The map has no timetable.";
                return section;
            }
            var chronoIds = variants.Count > 0 ? OmsiChrono.AddedIds(source, summary, variants) : null;
            var report = OmsiTimetableCheck.Check(timetable, summary, chronoIds);
            foreach (var problem in report.Problems)
                Add(section, problem.ToString());
            section.Status = report.Problems.Count > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = report.BusStopCount + " stops, " + report.StationLinkCount + " station links, " + report.TrackCount + " tracks, " +
                              report.TripCount + " trips, " + report.LineCount + " lines with " + report.TourCount + " tours and " + report.ScheduledTripCount +
                              " scheduled trips; " + (report.Problems.Count == 0 ? "all references hold." : report.Problems.Count + " finding(s).");
            return section;
        }

        private static OmsiCheckSection Chrono(OmsiMapSummary summary, IOmsiFileSource source, List<OmsiChronoVariant> variants)
        {
            var section = new OmsiCheckSection("Chrono variants");
            if (variants.Count == 0)
            {
                section.Status = OmsiCheckStatus.Passed;
                section.Summary = "The map has no chrono variants.";
                return section;
            }

            OmsiChrono.Overlay(summary, source, variants, out var report);
            foreach (var variant in variants)
                if (variant.Start.HasValue && variant.End.HasValue && variant.End.Value < variant.Start.Value)
                    Add(section, variant.Name + " ends before it starts");
            if (report.UnresolvedSelections > 0)
                Add(section, report.UnresolvedSelections + " selection(s) of change lists name elements that neither the tile nor an earlier variant has");
            section.Status = section.Findings.Count > 0 ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = variants.Count + " variant(s); all together they add " + report.AddedObjects + " objects and " + report.AddedSplines +
                              " splines, delete " + report.DeletedElements + " element(s) and change the type of " + report.ChangedTypes + ".";
            return section;
        }

        private static void Add(OmsiCheckSection section, string finding)
        {
            if (section.Findings.Count < MaximumFindings)
                section.Findings.Add(finding);
            else if (section.Findings.Count == MaximumFindings)
                section.Findings.Add("and more");
        }
    }
}
