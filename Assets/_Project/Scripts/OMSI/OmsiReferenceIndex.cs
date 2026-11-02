using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One place in a map's files that refers to an element ID.</summary>
    public sealed class OmsiReference
    {
        public OmsiReference(int targetId, string file, string kind)
        {
            TargetId = targetId;
            File = file;
            Kind = kind;
        }

        public int TargetId { get; }

        /// <summary>Relative path of the file that holds the reference.</summary>
        public string File { get; }

        /// <summary>Short description such as "spline neighbour" or "bus stop".</summary>
        public string Kind { get; }

        public override string ToString()
        {
            return Kind + " in " + File;
        }
    }

    /// <summary>
    /// What each file of a map contributed to a reference index, kept between builds so that files that did not change
    /// are not read again. Only a workspace can tell whether a file changed; other sources are read in full.
    /// </summary>
    public sealed class OmsiReferenceCache
    {
        internal readonly Dictionary<string, KeyValuePair<string, List<OmsiReference>>> Files =
            new Dictionary<string, KeyValuePair<string, List<OmsiReference>>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every reference to an element ID across a map: tiles, chrono patches, entry points and timetable data. IDs are
    /// unique map-wide, so a reference names its target by ID alone. Build the index again after edits.
    /// </summary>
    public sealed class OmsiReferenceIndex
    {
        private readonly Dictionary<int, List<OmsiReference>> referencesById = new Dictionary<int, List<OmsiReference>>();
        private static readonly IReadOnlyList<OmsiReference> None = new OmsiReference[0];

        public int ReferenceCount { get; private set; }

        public IReadOnlyList<OmsiReference> To(int id)
        {
            return referencesById.TryGetValue(id, out var references) ? references : None;
        }

        public static OmsiReferenceIndex Build(IOmsiFileSource source, string globalConfigRelativePath = "global.cfg")
        {
            return Build(source, globalConfigRelativePath, null);
        }

        /// <param name="cache">
        /// The files read for an earlier index of the same source; files of a workspace that have not changed since are
        /// taken from it instead of being read again. Null reads every file.
        /// </param>
        public static OmsiReferenceIndex Build(IOmsiFileSource source, string globalConfigRelativePath, OmsiReferenceCache cache)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var index = new OmsiReferenceIndex();
            var config = OmsiRecordFile.Parse(source.ReadAllBytes(globalConfigRelativePath), OmsiGlobalConfigLayouts.Instance);
            var entryPoints = new List<OmsiReference>();
            foreach (var record in config.Records)
            {
                if (string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase) && record.Fields.Count == 3)
                {
                    string relative;
                    try
                    {
                        relative = OmsiRelativePath.Normalize(record.GetField(2));
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                    if (source.FileExists(relative))
                        index.AddAll(Read(source, relative, cache, TileReferences));
                }
                else if (string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase) && record.HasKnownLayout)
                {
                    entryPoints.Clear();
                    for (var field = 2; field < record.Fields.Count; field += 12)
                        AddIfInt(entryPoints, record, field, globalConfigRelativePath, "entry point");
                    index.AddAll(entryPoints);
                }
            }

            foreach (var chronoTile in source.EnumerateFiles("Chrono", "tile_*.map", true))
                index.AddAll(Read(source, chronoTile, cache, ChronoReferences));

            foreach (var file in source.EnumerateFiles("TTData", "*", false))
                index.AddAll(Read(source, file, cache, TimetableReferences));
            foreach (var chronoTimetable in source.EnumerateFiles("Chrono", "*", true))
                if (chronoTimetable.IndexOf(Path.DirectorySeparatorChar + "TTData" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                    index.AddAll(Read(source, chronoTimetable, cache, TimetableReferences));

            return index;
        }

        /// <summary>The references of one file, from the cache when the workspace says the file is still the same.</summary>
        private static List<OmsiReference> Read(IOmsiFileSource source, string relative, OmsiReferenceCache cache,
            Func<IOmsiFileSource, string, List<OmsiReference>> read)
        {
            var stamp = cache != null && source is OmsiMapWorkspace workspace ? workspace.Stamp(relative) : null;
            if (stamp != null && cache.Files.TryGetValue(relative, out var known) && known.Key == stamp)
                return known.Value;
            var references = read(source, relative);
            if (stamp != null)
                cache.Files[relative] = new KeyValuePair<string, List<OmsiReference>>(stamp, references);
            return references;
        }

        private static List<OmsiReference> TileReferences(IOmsiFileSource source, string relative)
        {
            var references = new List<OmsiReference>();
            var tile = OmsiRecordFile.Parse(source.ReadAllBytes(relative), OmsiTileRecordLayouts.Instance);
            var splineIds = new List<int>();
            foreach (var view in OmsiTileRecords.Of<OmsiSplineView>(tile))
                splineIds.Add(view.Id);

            foreach (var record in tile.Records)
            {
                switch (OmsiTileRecords.Create(tile, record))
                {
                    case OmsiAttachedObjectView attached:
                        Add(references, attached.ParentId, relative, "attached object parent");
                        break;
                    case OmsiSplineAttachmentView attachment:
                        if (attachment.SplineIndex >= 0 && attachment.SplineIndex < splineIds.Count)
                            Add(references, splineIds[attachment.SplineIndex], relative, attachment.IsRepeater ? "repeated attachment on spline" : "attachment on spline");
                        if (attachment.IsRepeater)
                            Add(references, attachment.Id, relative, "repeater of attachment");
                        break;
                    case OmsiSplineView spline:
                        if (spline.PreviousId != 0)
                            Add(references, spline.PreviousId, relative, "spline neighbour");
                        if (spline.NextId != 0)
                            Add(references, spline.NextId, relative, "spline neighbour");
                        break;
                    default:
                        if (string.Equals(record.Name, "varparent", StringComparison.OrdinalIgnoreCase))
                            AddIfInt(references, record, 0, relative, "variable parent");
                        break;
                }
            }
            return references;
        }

        private static List<OmsiReference> ChronoReferences(IOmsiFileSource source, string relative)
        {
            var references = new List<OmsiReference>();
            var tile = OmsiRecordFile.Parse(source.ReadAllBytes(relative), OmsiTileRecordLayouts.Instance);
            foreach (var record in tile.Records)
                if (string.Equals(record.Name, "selobject", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.Name, "selspline", StringComparison.OrdinalIgnoreCase))
                    AddIfInt(references, record, 0, relative, "chrono selection");
            return references;
        }

        private static List<OmsiReference> TimetableReferences(IOmsiFileSource source, string relative)
        {
            var references = new List<OmsiReference>();
            var name = Path.GetFileName(relative);
            var extension = Path.GetExtension(relative).ToLowerInvariant();
            var isBusStops = string.Equals(name, "Busstops.cfg", StringComparison.OrdinalIgnoreCase);
            var isStationLinks = string.Equals(name, "StnLinks.cfg", StringComparison.OrdinalIgnoreCase);
            if (!isBusStops && !isStationLinks && extension != ".ttr" && extension != ".ttp")
                return references;

            var file = OmsiRecordFile.Parse(source.ReadAllBytes(relative), null);
            foreach (var record in file.Records)
            {
                var values = record.AllValues();
                switch (record.Name.ToLowerInvariant())
                {
                    case "busstop" when isBusStops:
                        AddIfInt(references, values, 2, relative, "bus stop");
                        break;
                    case "stnlink" when isStationLinks:
                        AddIfInt(references, values, 1, relative, "station link");
                        AddIfInt(references, values, 2, relative, "station link");
                        break;
                    case "stnlink_entry" when isStationLinks:
                        AddIfInt(references, values, 0, relative, "station link path");
                        break;
                    case "track_entry":
                        AddIfInt(references, values, 0, relative, "track entry");
                        break;
                    case "station_typ2":
                    case "station":
                        AddIfInt(references, values, 0, relative, "trip stop");
                        break;
                }
            }
            return references;
        }

        private static void AddIfInt(List<OmsiReference> references, OmsiRecord record, int field, string file, string kind)
        {
            if (record.TryGetInt(field, out var id))
                Add(references, id, file, kind);
        }

        private static void AddIfInt(List<OmsiReference> references, List<string> values, int index, string file, string kind)
        {
            if (index < values.Count && int.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                Add(references, id, file, kind);
        }

        private static void Add(List<OmsiReference> references, int id, string file, string kind)
        {
            references.Add(new OmsiReference(id, file, kind));
        }

        private void AddAll(List<OmsiReference> references)
        {
            foreach (var reference in references)
            {
                if (!referencesById.TryGetValue(reference.TargetId, out var list))
                {
                    list = new List<OmsiReference>();
                    referencesById.Add(reference.TargetId, list);
                }
                list.Add(reference);
            }
            ReferenceCount += references.Count;
        }
    }
}
