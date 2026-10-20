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
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var index = new OmsiReferenceIndex();
            var config = OmsiRecordFile.Parse(source.ReadAllBytes(globalConfigRelativePath), OmsiGlobalConfigLayouts.Instance);
            foreach (var record in config.Records)
            {
                if (string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase) && record.Fields.Count == 3)
                    index.AddTile(source, record.GetField(2));
                else if (string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase) && record.HasKnownLayout)
                    for (var field = 2; field < record.Fields.Count; field += 12)
                        index.AddIfInt(record, field, globalConfigRelativePath, "entry point");
            }

            foreach (var chronoTile in source.EnumerateFiles("Chrono", "tile_*.map", true))
            {
                var tile = OmsiRecordFile.Parse(source.ReadAllBytes(chronoTile), OmsiTileRecordLayouts.Instance);
                foreach (var record in tile.Records)
                    if (string.Equals(record.Name, "selobject", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Name, "selspline", StringComparison.OrdinalIgnoreCase))
                        index.AddIfInt(record, 0, chronoTile, "chrono selection");
            }

            index.AddTimetables(source, "TTData");
            foreach (var chronoTimetable in source.EnumerateFiles("Chrono", "*", true))
                if (chronoTimetable.IndexOf(Path.DirectorySeparatorChar + "TTData" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                    index.AddTimetableFile(source, chronoTimetable);

            return index;
        }

        private void AddTile(IOmsiFileSource source, string tileName)
        {
            string relative;
            try
            {
                relative = OmsiRelativePath.Normalize(tileName);
            }
            catch (ArgumentException)
            {
                return;
            }
            if (!source.FileExists(relative))
                return;

            var tile = OmsiRecordFile.Parse(source.ReadAllBytes(relative), OmsiTileRecordLayouts.Instance);
            var splineIds = new List<int>();
            foreach (var view in OmsiTileRecords.Of<OmsiSplineView>(tile))
                splineIds.Add(view.Id);

            foreach (var record in tile.Records)
            {
                switch (OmsiTileRecords.Create(tile, record))
                {
                    case OmsiAttachedObjectView attached:
                        Add(attached.ParentId, relative, "attached object parent");
                        break;
                    case OmsiSplineAttachmentView attachment:
                        if (attachment.SplineIndex >= 0 && attachment.SplineIndex < splineIds.Count)
                            Add(splineIds[attachment.SplineIndex], relative, attachment.IsRepeater ? "repeated attachment on spline" : "attachment on spline");
                        if (attachment.IsRepeater)
                            Add(attachment.Id, relative, "repeater of attachment");
                        break;
                    case OmsiSplineView spline:
                        if (spline.PreviousId != 0)
                            Add(spline.PreviousId, relative, "spline neighbour");
                        if (spline.NextId != 0)
                            Add(spline.NextId, relative, "spline neighbour");
                        break;
                    default:
                        if (string.Equals(record.Name, "varparent", StringComparison.OrdinalIgnoreCase))
                            AddIfInt(record, 0, relative, "variable parent");
                        break;
                }
            }
        }

        private void AddTimetables(IOmsiFileSource source, string folder)
        {
            foreach (var file in source.EnumerateFiles(folder, "*", false))
                AddTimetableFile(source, file);
        }

        private void AddTimetableFile(IOmsiFileSource source, string relative)
        {
            var name = Path.GetFileName(relative);
            var extension = Path.GetExtension(relative).ToLowerInvariant();
            var isBusStops = string.Equals(name, "Busstops.cfg", StringComparison.OrdinalIgnoreCase);
            var isStationLinks = string.Equals(name, "StnLinks.cfg", StringComparison.OrdinalIgnoreCase);
            if (!isBusStops && !isStationLinks && extension != ".ttr" && extension != ".ttp")
                return;

            var file = OmsiRecordFile.Parse(source.ReadAllBytes(relative), null);
            foreach (var record in file.Records)
            {
                var values = record.AllValues();
                switch (record.Name.ToLowerInvariant())
                {
                    case "busstop" when isBusStops:
                        AddIfInt(values, 2, relative, "bus stop");
                        break;
                    case "stnlink" when isStationLinks:
                        AddIfInt(values, 1, relative, "station link");
                        AddIfInt(values, 2, relative, "station link");
                        break;
                    case "stnlink_entry" when isStationLinks:
                        AddIfInt(values, 0, relative, "station link path");
                        break;
                    case "track_entry":
                        AddIfInt(values, 0, relative, "track entry");
                        break;
                    case "station_typ2":
                    case "station":
                        AddIfInt(values, 0, relative, "trip stop");
                        break;
                }
            }
        }

        private void AddIfInt(OmsiRecord record, int field, string file, string kind)
        {
            if (record.TryGetInt(field, out var id))
                Add(id, file, kind);
        }

        private void AddIfInt(List<string> values, int index, string file, string kind)
        {
            if (index < values.Count && int.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                Add(id, file, kind);
        }

        private void Add(int id, string file, string kind)
        {
            if (!referencesById.TryGetValue(id, out var list))
            {
                list = new List<OmsiReference>();
                referencesById.Add(id, list);
            }
            list.Add(new OmsiReference(id, file, kind));
            ReferenceCount++;
        }
    }
}
