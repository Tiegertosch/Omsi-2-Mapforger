using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;

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
        public OmsiCheckSection(string title) : this(title, title)
        {
        }

        /// <param name="key">The English name of the topic, which stays the same in every language (the command line finds a topic by it).</param>
        /// <param name="title">The name in the language in use.</param>
        public OmsiCheckSection(string key, string title)
        {
            Key = key;
            Title = title;
        }

        public string Key { get; }

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

        private static string StatusName(OmsiCheckStatus status)
        {
            return Loc.T(status == OmsiCheckStatus.Passed ? "mapcheck.status.passed" : status == OmsiCheckStatus.Note ? "mapcheck.status.note" : "mapcheck.status.problem"); // i18n:keys
        }

        /// <summary>The report as plain text, for copying into a message or an issue.</summary>
        public string ToText(string mapName)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(Loc.T("mapcheck.report.title", ("name", mapName)));
            foreach (var section in Sections)
            {
                text.AppendLine();
                text.AppendLine(Loc.T("mapcheck.report.line", ("title", section.Title), ("status", StatusName(section.Status)), ("summary", section.Summary)));
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
                report.Sections.Add(Parking(summary, source, installationRoot));
            }
            var variants = OmsiChrono.ReadVariants(source);
            report.Sections.Add(Timetable(summary, source, variants));
            report.Sections.Add(Chrono(summary, source, variants));
            return report;
        }

        private static OmsiCheckSection Import(OmsiMapSummary summary)
        {
            var section = new OmsiCheckSection("Import", Loc.T("mapcheck.title.import"));
            foreach (var warning in summary.Warnings)
                Add(section, warning);
            section.Status = summary.Warnings.Count == 0 ? OmsiCheckStatus.Passed : OmsiCheckStatus.Note;
            section.Summary = summary.Warnings.Count == 0
                ? Loc.T("mapcheck.import.ok", ("tiles", summary.Tiles.Count))
                : Loc.T("mapcheck.import.warnings", ("warnings", summary.Warnings.Count), ("tiles", summary.Tiles.Count));
            return section;
        }

        private static OmsiCheckSection SplineConnections(OmsiMapSummary summary)
        {
            var section = new OmsiCheckSection("Spline connections", Loc.T("mapcheck.title.splines"));
            var continuity = OmsiSplineContinuity.Check(summary);
            foreach (var gap in continuity.Gaps)
                Add(section, Loc.T("mapcheck.splines.gap", ("id", gap.SplineId), ("neighbour", gap.NeighbourId), ("meters", gap.DistanceMeters), ("x", gap.Tile.X), ("y", gap.Tile.Y),
                    ("mutual", gap.IsMutual ? "yes" : "no")));
            if (continuity.Gaps.Count > 0)
                section.Tile = continuity.Gaps[0].Tile;
            section.Status = continuity.Gaps.Count > 0 || continuity.MissingNeighbourCount > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = Loc.T("mapcheck.splines.summary", ("connected", continuity.ConnectedCount), ("links", continuity.LinkCount), ("tolerance", continuity.ToleranceMeters),
                ("missing", continuity.MissingNeighbourCount));
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
            var section = new OmsiCheckSection("IDs", Loc.T("mapcheck.title.ids"));
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
                        Add(section, Loc.T("mapcheck.ids.duplicate", ("id", id), ("x1", first.X), ("y1", first.Y), ("x2", tile.X), ("y2", tile.Y)));
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
                Add(section, Loc.T("mapcheck.ids.stale", ("next", summary.NextIdCode.Value), ("highest", highest)));
            if (!summary.NextIdCode.HasValue)
                Add(section, Loc.T("mapcheck.ids.missing"));
            section.Status = duplicates > 0 ? OmsiCheckStatus.Problem : stale || !summary.NextIdCode.HasValue ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = Loc.T("mapcheck.ids.summary", ("ids", seen.Count), ("duplicates", duplicates), ("stale", stale ? "yes" : "no"));
            return section;
        }

        private static OmsiCheckSection EntryPoints(OmsiMapSummary summary, IOmsiFileSource source, string globalConfigRelativePath,
            Dictionary<int, OmsiRecordFile> tiles)
        {
            var section = new OmsiCheckSection("Entry points", Loc.T("mapcheck.title.entry"));
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
                        Add(section, Loc.T("mapcheck.entry.nan", ("name", name)));
                        continue;
                    }
                    if (!tiles.TryGetValue(tileIndex, out var tile))
                    {
                        wrong++;
                        Add(section, Loc.T("mapcheck.entry.no_tile", ("name", name), ("index", tileIndex)));
                        continue;
                    }
                    var position = FindPlaced(tile, id, out var actualIndex, out var heading);
                    if (position < 0)
                    {
                        wrong++;
                        Add(section, Loc.T("mapcheck.entry.no_object", ("name", name), ("id", id), ("x", summary.Tiles[tileIndex].X), ("y", summary.Tiles[tileIndex].Y)));
                        section.Tile = section.Tile ?? summary.Tiles[tileIndex];
                        continue;
                    }
                    if (actualIndex != placedIndex)
                    {
                        wrong++;
                        Add(section, Loc.T("mapcheck.entry.number", ("name", name), ("id", id), ("placed", placedIndex), ("actual", actualIndex)));
                    }
                    if (heading.HasValue && record.TryGetDouble(field + 7, out var qy) && record.TryGetDouble(field + 9, out var qw))
                    {
                        var angle = 2.0 * Math.Atan2(qy, qw) * 180.0 / Math.PI;
                        var difference = Math.Abs(((angle - heading.Value) % 360.0 + 540.0) % 360.0 - 180.0);
                        if (difference > 5.0)
                            Add(section, Loc.T("mapcheck.entry.heading", ("name", name), ("degrees", difference)));
                    }
                }
            }
            section.Status = wrong > 0 ? OmsiCheckStatus.Problem : section.Findings.Count > 0 ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = count == 0 ? Loc.T("mapcheck.entry.none") : Loc.T("mapcheck.entry.summary", ("ok", count - wrong), ("count", count));
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
            var section = new OmsiCheckSection("Attachments", Loc.T("mapcheck.title.attach"));
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
                                Add(section, Loc.T("mapcheck.attach.parent", ("id", attached.Id), ("x", tile.X), ("y", tile.Y), ("parent", attached.ParentId)));
                                section.Tile = section.Tile ?? tile;
                            }
                            earlier.Add(attached.Id);
                            break;
                        case OmsiSplineAttachmentView attachment:
                            rowCount++;
                            if (attachment.SplineIndex < 0 || attachment.SplineIndex >= TileSplineCount(pair.Value))
                            {
                                Add(section, Loc.T("mapcheck.attach.spline", ("kind", attachment.IsRepeater ? "repeater" : "row"), ("id", attachment.Id), ("x", tile.X), ("y", tile.Y),
                                    ("index", attachment.SplineIndex), ("count", TileSplineCount(pair.Value))));
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
                    Add(section, Loc.T("mapcheck.attach.no_master", ("id", pair.Key), ("x", pair.Value.X), ("y", pair.Value.Y)));
            section.Status = section.Findings.Count > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = Loc.T("mapcheck.attach.summary", ("attached", attachedCount), ("rows", rowCount), ("ok", section.Findings.Count == 0 ? "yes" : "no"), ("count", section.Findings.Count));
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
            var section = new OmsiCheckSection("Variable parents", Loc.T("mapcheck.title.varparents"));
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
                    Add(section, Loc.T("mapcheck.varparents.missing", ("count", parent.Value), ("x", tile.X), ("y", tile.Y), ("parent", parent.Key)));
                    section.Tile = section.Tile ?? tile;
                }
            }
            section.Status = missing > 0 ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = count == 0 ? Loc.T("mapcheck.varparents.none") : Loc.T("mapcheck.varparents.summary", ("ok", count - missing), ("count", count));
            return section;
        }

        private static OmsiCheckSection Files(OmsiMapSummary summary, string installationRoot)
        {
            var section = new OmsiCheckSection("Object and spline files", Loc.T("mapcheck.title.files"));
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
                Add(section, Loc.T("mapcheck.files.missing", ("path", path)));
            }
            section.Status = missing > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = Loc.T("mapcheck.files.summary", ("ok", paths.Count - missing), ("count", paths.Count));
            return section;
        }

        /// <summary>
        /// The connection points of junction objects with their state (ADR 024): invalid ones first, then those to review,
        /// then free ones, which may be meant as open ends.
        /// </summary>
        private static OmsiCheckSection Junctions(OmsiMapSummary summary, string installationRoot)
        {
            var section = new OmsiCheckSection("Junctions", Loc.T("mapcheck.title.junctions"));
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
                    Add(section, Loc.T("mapcheck.junctions.finding", ("state", state == OmsiJunctionPortState.Invalid ? "invalid" : state == OmsiJunctionPortState.NeedsReview ? "review" : "free"),
                        ("id", check.Port.Object.Id), ("name", Path.GetFileNameWithoutExtension(check.Port.Object.AssetPath ?? string.Empty)), ("x", check.Port.Tile.X), ("y", check.Port.Tile.Y),
                        ("message", check.Message)));
                }
            section.Status = counts[OmsiJunctionPortState.Invalid] > 0 ? OmsiCheckStatus.Problem
                : counts[OmsiJunctionPortState.NeedsReview] > 0 || counts[OmsiJunctionPortState.Unconnected] > 0 ? OmsiCheckStatus.Note
                : OmsiCheckStatus.Passed;
            section.Summary = checks.Count == 0
                ? Loc.T("mapcheck.junctions.none")
                : Loc.T("mapcheck.junctions.summary", ("connected", counts[OmsiJunctionPortState.Connected]), ("count", checks.Count), ("objects", objects.Count),
                    ("review", counts[OmsiJunctionPortState.NeedsReview]), ("invalid", counts[OmsiJunctionPortState.Invalid]), ("free", counts[OmsiJunctionPortState.Unconnected]));
            return section;
        }

        /// <summary>
        /// The car park objects of the map, each judged as openOMSI does before a car parks in it (<see cref="OmsiParkingCheck"/>), and
        /// the parking lists they draw their cars from.
        /// </summary>
        private static OmsiCheckSection Parking(OmsiMapSummary summary, IOmsiFileSource source, string installationRoot)
        {
            var section = new OmsiCheckSection("Parking", Loc.T("mapcheck.title.parking"));
            var size = summary.TileSizeMeters;
            var isCarPark = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var spaces = new List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>>();
            foreach (var tile in summary.Tiles)
                foreach (var record in tile.Objects)
                {
                    if (string.IsNullOrEmpty(record.AssetPath))
                        continue;
                    if (!isCarPark.TryGetValue(record.AssetPath, out var park))
                    {
                        park = false;
                        try
                        {
                            var file = OmsiRelativePath.Resolve(installationRoot, record.AssetPath);
                            park = File.Exists(file) && OmsiTextFile.Decode(File.ReadAllBytes(file)).IndexOf("[carpark_p]", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
                        {
                            // A file that cannot be read is no car park here; the files section names it.
                        }
                        isCarPark[record.AssetPath] = park;
                    }
                    if (park)
                        spaces.Add(new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, record));
                }
            if (spaces.Count == 0)
            {
                section.Status = OmsiCheckStatus.Passed;
                section.Summary = Loc.T("mapcheck.parking.none");
                return section;
            }

            // The road paths of the tiles with spaces and their neighbours.
            var scenery = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);
            var profiles = new OmsiSplineProfileCatalog(installationRoot);
            Func<string, OmsiSceneryObject> sceneryOf = asset =>
            {
                if (string.IsNullOrEmpty(asset))
                    return null;
                if (!scenery.TryGetValue(asset, out var found))
                {
                    found = null;
                    try
                    {
                        var file = OmsiRelativePath.Resolve(installationRoot, asset);
                        if (File.Exists(file))
                            found = OmsiSceneryObject.Parse(File.ReadAllBytes(file));
                    }
                    catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException ||
                                                      exception is FormatException)
                    {
                        // Unreadable objects have no paths.
                    }
                    scenery[asset] = found;
                }
                return found;
            };
            var pathsOfTile = new Dictionary<OmsiTileSummary, List<OmsiMapPath>>();
            Func<OmsiTileSummary, List<OmsiMapPath>> pathsOf = tile =>
            {
                if (!pathsOfTile.TryGetValue(tile, out var paths))
                {
                    paths = tile.IsAvailable ? OmsiMapPaths.OfTile(summary, tile, sceneryOf, profiles.Get) : new List<OmsiMapPath>();
                    pathsOfTile[tile] = paths;
                }
                return paths;
            };

            var usable = 0;
            var alongRoads = 0;
            var lists = new Dictionary<int, int>();
            foreach (var pair in spaces)
            {
                var tile = pair.Key;
                var record = pair.Value;
                var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX, record.LocalY, size);
                var near = new List<OmsiMapPath>();
                foreach (var other in summary.Tiles)
                    if (Math.Abs(other.X - tile.X) <= 1 && Math.Abs(other.Y - tile.Y) <= 1)
                        near.AddRange(pathsOf(other));
                var verdict = OmsiParkingCheck.Judge(world.X, world.Y, record.HeadingDegrees, near);
                // A space far from every road lane belongs to a car park of its own, where no car is meant to park in: not judged.
                if (verdict.Fault == OmsiParkingFault.NoLane || (verdict.Fault == OmsiParkingFault.Side && Math.Abs(verdict.Value) > OmsiParkingCheck.RoadsideMeters))
                    continue;
                alongRoads++;
                if (verdict.Fault == OmsiParkingFault.None)
                {
                    usable++;
                }
                else
                {
                    section.Tile = section.Tile ?? tile;
                    Add(section, Loc.T("mapcheck.parking." + verdict.Fault.ToString().ToLowerInvariant(), ("id", record.Id), ("x", tile.X), ("y", tile.Y), // i18n:keys
                        ("value", verdict.Value)));
                }
                var list = 0;
                if (record.Labels.Count > 0 && int.TryParse(record.Labels[0].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number) &&
                    number > 0)
                    list = number;
                if (!lists.ContainsKey(list))
                    lists[list] = record.Id;
            }

            // Every list the spaces name must exist and name cars that have AI cars beside them.
            var listProblems = 0;
            foreach (var pair in lists)
            {
                var file = OmsiParkList.FileName(pair.Key);
                if (!source.FileExists(file))
                {
                    listProblems++;
                    Add(section, Loc.T(pair.Key == 0 ? "mapcheck.parking.no_list" : "mapcheck.parking.list_missing", ("file", file), ("id", pair.Value))); // i18n:keys
                    continue;
                }
                foreach (var entry in OmsiParkList.Parse(source.ReadAllBytes(file)))
                    if (!File.Exists(OmsiRelativePath.Resolve(installationRoot, entry)))
                    {
                        listProblems++;
                        Add(section, Loc.T("mapcheck.parking.car", ("file", file), ("car", entry)));
                    }
            }
            section.Status = alongRoads - usable > 0 || listProblems > 0 ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = Loc.T("mapcheck.parking.summary", ("count", alongRoads), ("usable", usable), ("problems", listProblems));
            return section;
        }

        private static OmsiCheckSection Timetable(OmsiMapSummary summary, IOmsiFileSource source, List<OmsiChronoVariant> variants)
        {
            var section = new OmsiCheckSection("Timetable", Loc.T("mapcheck.title.timetable"));
            var timetable = OmsiTimetable.Read(source);
            if (timetable.IsEmpty)
            {
                section.Status = OmsiCheckStatus.Passed;
                section.Summary = Loc.T("mapcheck.timetable.none");
                return section;
            }
            var chronoIds = variants.Count > 0 ? OmsiChrono.AddedIds(source, summary, variants) : null;
            var report = OmsiTimetableCheck.Check(timetable, summary, chronoIds);
            foreach (var problem in report.Problems)
                Add(section, problem.ToString());
            section.Status = report.Problems.Count > 0 ? OmsiCheckStatus.Problem : OmsiCheckStatus.Passed;
            section.Summary = Loc.T("mapcheck.timetable.summary", ("stops", report.BusStopCount), ("links", report.StationLinkCount), ("tracks", report.TrackCount), ("trips", report.TripCount),
                ("lines", report.LineCount), ("tours", report.TourCount), ("scheduled", report.ScheduledTripCount), ("findings", report.Problems.Count));
            return section;
        }

        private static OmsiCheckSection Chrono(OmsiMapSummary summary, IOmsiFileSource source, List<OmsiChronoVariant> variants)
        {
            var section = new OmsiCheckSection("Chrono variants", Loc.T("mapcheck.title.chrono"));
            if (variants.Count == 0)
            {
                section.Status = OmsiCheckStatus.Passed;
                section.Summary = Loc.T("mapcheck.chrono.none");
                return section;
            }

            OmsiChrono.Overlay(summary, source, variants, out var report);
            foreach (var variant in variants)
                if (variant.Start.HasValue && variant.End.HasValue && variant.End.Value < variant.Start.Value)
                    Add(section, Loc.T("mapcheck.chrono.reversed", ("name", variant.Name)));
            if (report.UnresolvedSelections > 0)
                Add(section, Loc.T("mapcheck.chrono.unresolved", ("count", report.UnresolvedSelections)));
            section.Status = section.Findings.Count > 0 ? OmsiCheckStatus.Note : OmsiCheckStatus.Passed;
            section.Summary = Loc.T("mapcheck.chrono.summary", ("count", variants.Count), ("objects", report.AddedObjects), ("splines", report.AddedSplines), ("deleted", report.DeletedElements),
                ("changed", report.ChangedTypes));
            return section;
        }

        private static void Add(OmsiCheckSection section, string finding)
        {
            if (section.Findings.Count < MaximumFindings)
                section.Findings.Add(finding);
            else if (section.Findings.Count == MaximumFindings)
                section.Findings.Add(Loc.T("mapcheck.and_more"));
        }
    }
}
