using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A chrono variant of a map: a folder below <c>Chrono</c> whose change lists apply from a start date to an end
    /// date, on top of the base map and the variants before it in name order.
    /// </summary>
    public sealed class OmsiChronoVariant
    {
        /// <summary>The folder relative to the map, such as <c>Chrono\0100_BaustelleNordspitze</c>.</summary>
        public string Folder { get; internal set; }

        /// <summary>The folder name, which orders the variants.</summary>
        public string Name { get; internal set; }

        /// <summary>The [name] of its English description, else of any description, else the folder name.</summary>
        public string Title { get; internal set; }

        /// <summary>First day the variant applies; null applies it from the start.</summary>
        public DateTime? Start { get; internal set; }

        /// <summary>Last day the variant applies; null keeps it.</summary>
        public DateTime? End { get; internal set; }

        /// <summary>Lines of the timetable it switches off, by the names of their .ttl files.</summary>
        public List<string> DeactivatedLines { get; } = new List<string>();

        /// <summary>Its change lists, relative to the map, such as <c>Chrono\0100_BaustelleNordspitze\tile_1_1.map</c>.</summary>
        public List<string> TileFiles { get; } = new List<string>();

        public bool IsActiveOn(DateTime date)
        {
            return (!Start.HasValue || date.Date >= Start.Value) && (!End.HasValue || date.Date <= End.Value);
        }

        public override string ToString()
        {
            return Title + " (" + (Start.HasValue ? Start.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "from the start") +
                   (End.HasValue ? " to " + End.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty) + ")";
        }
    }

    /// <summary>What applying chrono variants to a map did.</summary>
    public sealed class OmsiChronoOverlayReport
    {
        public List<OmsiChronoVariant> Variants { get; } = new List<OmsiChronoVariant>();
        public int AddedObjects { get; internal set; }
        public int AddedSplines { get; internal set; }
        public int DeletedElements { get; internal set; }
        public int ChangedTypes { get; internal set; }

        /// <summary>Selections of elements that neither the tile nor an earlier variant has.</summary>
        public int UnresolvedSelections { get; internal set; }
    }

    /// <summary>
    /// Reads the chrono variants of a map and lays the ones active on a date over a copy of the map, as OMSI does when it
    /// loads the map for that day: in name order, each change list adds elements to its tile, deletes selected ones and
    /// swaps the object file of selected objects. The base map's summary is left unchanged; the copy shares its
    /// unchanged records. Rules, labels and terrain marks of the change lists do not affect the overview and are skipped.
    /// </summary>
    public static class OmsiChrono
    {
        public const string Folder = "Chrono";

        /// <summary>The variants of a map in name order; a map without a Chrono folder has none.</summary>
        public static List<OmsiChronoVariant> ReadVariants(IOmsiFileSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var byFolder = new SortedDictionary<string, OmsiChronoVariant>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in source.EnumerateFiles(Folder, "*", true))
            {
                var parts = file.Split(Path.DirectorySeparatorChar);
                if (parts.Length < 3 || !string.Equals(parts[0], Folder, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!byFolder.TryGetValue(parts[1], out var variant))
                    byFolder[parts[1]] = variant = new OmsiChronoVariant
                    {
                        Folder = Path.Combine(Folder, parts[1]), Name = parts[1], Title = parts[1]
                    };
                if (parts.Length != 3)
                    continue;
                var name = parts[2];
                if (name.StartsWith("tile_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                    variant.TileFiles.Add(file);
                else if (string.Equals(name, "Chrono.cfg", StringComparison.OrdinalIgnoreCase))
                    ReadSettings(source.ReadAllBytes(file), variant);
                else if (name.StartsWith("Chrono_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dsc", StringComparison.OrdinalIgnoreCase))
                    ReadTitle(source.ReadAllBytes(file), name, variant);
            }
            return new List<OmsiChronoVariant>(byFolder.Values);
        }

        private static void ReadSettings(byte[] bytes, OmsiChronoVariant variant)
        {
            foreach (var record in OmsiRecordFile.Parse(bytes, null).Records)
            {
                var values = record.AllValues();
                switch (record.Name.ToLowerInvariant())
                {
                    case "startdate":
                        variant.Start = ParseDate(values);
                        break;
                    case "enddate":
                        variant.End = ParseDate(values);
                        break;
                    case "deactivate_lines":
                        if (values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                            for (var index = 1; index <= count && index < values.Count; index++)
                                if (values[index].Length > 0)
                                    variant.DeactivatedLines.Add(values[index]);
                        break;
                }
            }
        }

        private static DateTime? ParseDate(List<string> values)
        {
            return values.Count > 0 && DateTime.TryParseExact(values[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : (DateTime?)null;
        }

        /// <summary>English descriptions win; any other language is used until one is found.</summary>
        private static void ReadTitle(byte[] bytes, string fileName, OmsiChronoVariant variant)
        {
            var english = fileName.EndsWith("_ENG.dsc", StringComparison.OrdinalIgnoreCase);
            if (!english && !string.Equals(variant.Title, variant.Name, StringComparison.Ordinal))
                return;
            foreach (var record in OmsiRecordFile.Parse(bytes, null).Records)
            {
                if (!string.Equals(record.Name, "name", StringComparison.OrdinalIgnoreCase))
                    continue;
                var values = record.AllValues();
                var title = values.Find(value => value.Length > 0);
                if (!string.IsNullOrEmpty(title))
                    variant.Title = title;
                return;
            }
        }

        /// <summary>The variants that apply on a date, in the order OMSI applies them.</summary>
        public static List<OmsiChronoVariant> ActiveOn(IEnumerable<OmsiChronoVariant> variants, DateTime date)
        {
            var result = new List<OmsiChronoVariant>();
            foreach (var variant in variants)
                if (variant.IsActiveOn(date))
                    result.Add(variant);
            return result;
        }

        /// <summary>
        /// A copy of <paramref name="baseMap"/> with the change lists of <paramref name="variants"/> applied in their order.
        /// </summary>
        public static OmsiMapSummary Overlay(OmsiMapSummary baseMap, IOmsiFileSource source, IEnumerable<OmsiChronoVariant> variants,
            out OmsiChronoOverlayReport report)
        {
            if (baseMap == null)
                throw new ArgumentNullException(nameof(baseMap));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            report = new OmsiChronoOverlayReport();
            var map = Copy(baseMap);
            var tilesByFile = new Dictionary<string, OmsiTileSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in map.Tiles)
                tilesByFile[Path.GetFileName(tile.FileName)] = tile;

            // Rows along splines name their spline by its number in the tile. Change lists number on: a deleted spline keeps
            // its number and new splines follow the last one, so the numbers are kept here and resolved at the end.
            var splineSlots = new Dictionary<OmsiTileSummary, List<OmsiSplineRecord>>();
            foreach (var tile in map.Tiles)
                splineSlots.Add(tile, new List<OmsiSplineRecord>(tile.Splines));

            foreach (var variant in variants)
            {
                report.Variants.Add(variant);
                foreach (var file in variant.TileFiles)
                {
                    if (!tilesByFile.TryGetValue(Path.GetFileName(file), out var tile))
                        continue;
                    ApplyChanges(source, file, tile, splineSlots[tile], report);
                    var added = OmsiMapReader.ReadAddedElements(source, file, tile);
                    tile.Objects.AddRange(added.Objects);
                    tile.Splines.AddRange(added.Splines);
                    splineSlots[tile].AddRange(added.Splines);
                    tile.AttachedObjects.AddRange(added.AttachedObjects);
                    tile.SplineAttachments.AddRange(added.SplineAttachments);
                    report.AddedObjects += added.Objects.Count + added.AttachedObjects.Count;
                    report.AddedSplines += added.Splines.Count;
                }
            }

            foreach (var tile in map.Tiles)
                ResolveSplineNumbers(tile, splineSlots[tile], report);

            foreach (var tile in map.Tiles)
            {
                tile.ObjectCount = tile.Objects.Count;
                tile.SplineCount = tile.Splines.Count;
                tile.AttachmentCount = tile.AttachedObjects.Count + tile.SplineAttachments.Count;
            }
            map.ObjectCount = 0;
            map.SplineCount = 0;
            map.AttachmentCount = 0;
            foreach (var tile in map.Tiles)
            {
                map.ObjectCount += tile.ObjectCount;
                map.SplineCount += tile.SplineCount;
                map.AttachmentCount += tile.AttachmentCount;
            }
            return map;
        }

        /// <summary>Selections, deletions and type changes of one change list; new elements are added afterwards.</summary>
        private static void ApplyChanges(IOmsiFileSource source, string file, OmsiTileSummary tile, List<OmsiSplineRecord> splineSlots,
            OmsiChronoOverlayReport report)
        {
            object selected = null;
            foreach (var record in OmsiRecordFile.Parse(source.ReadAllBytes(file), OmsiTileRecordLayouts.Instance).Records)
            {
                switch (record.Name.ToLowerInvariant())
                {
                    case "selobject":
                        selected = record.TryGetInt(0, out var objectId) ? FindObject(tile, objectId) : null;
                        if (selected == null)
                            report.UnresolvedSelections++;
                        break;
                    case "selspline":
                        selected = record.TryGetInt(0, out var splineId) ? tile.Splines.Find(item => item.Id == splineId) : null;
                        if (selected == null)
                            report.UnresolvedSelections++;
                        break;
                    case "delete":
                        if (Remove(tile, selected))
                            report.DeletedElements++;
                        selected = null;
                        break;
                    case "typ":
                        var values = record.AllValues();
                        if (values.Count == 0 || values[0].Length == 0)
                            break;
                        if (selected is OmsiObjectRecord original)
                        {
                            var changed = Copy(original);
                            changed.AssetPath = values[0];
                            tile.Objects[tile.Objects.IndexOf(original)] = changed;
                            selected = changed;
                            report.ChangedTypes++;
                        }
                        else if (selected is OmsiSplineRecord spline)
                        {
                            var changed = Copy(spline);
                            changed.AssetPath = values[0];
                            tile.Splines[tile.Splines.IndexOf(spline)] = changed;
                            splineSlots[splineSlots.IndexOf(spline)] = changed;
                            selected = changed;
                            report.ChangedTypes++;
                        }
                        break;
                    case "object":
                    case "spline":
                    case "spline_h":
                    case "attachobj":
                    case "splineattachement":
                    case "splineattachement_repeater":
                        // A new element; commands after it, such as rules, concern it and not the earlier selection.
                        selected = null;
                        break;
                }
            }
        }

        /// <summary>
        /// Points rows along splines at their spline's position in the overlay's spline list. Rows on a deleted spline go
        /// with it; rows whose number is beyond the tile's splines are left as they are, as in the base map.
        /// </summary>
        private static void ResolveSplineNumbers(OmsiTileSummary tile, List<OmsiSplineRecord> splineSlots, OmsiChronoOverlayReport report)
        {
            if (splineSlots.Count == tile.Splines.Count)
                return;
            var positions = new Dictionary<OmsiSplineRecord, int>();
            for (var index = 0; index < tile.Splines.Count; index++)
                positions[tile.Splines[index]] = index;
            for (var index = tile.SplineAttachments.Count - 1; index >= 0; index--)
            {
                var row = tile.SplineAttachments[index];
                if (row.SplineIndex < 0 || row.SplineIndex >= splineSlots.Count)
                    continue;
                if (!positions.TryGetValue(splineSlots[row.SplineIndex], out var position))
                {
                    tile.SplineAttachments.RemoveAt(index);
                    report.DeletedElements++;
                }
                else if (position != row.SplineIndex)
                    tile.SplineAttachments[index] = Copy(row, position);
            }
        }

        private static OmsiSplineAttachmentRecord Copy(OmsiSplineAttachmentRecord record, int splineIndex)
        {
            return new OmsiSplineAttachmentRecord
            {
                AssetPath = record.AssetPath, Id = record.Id, SplineIndex = splineIndex, LateralOffset = record.LateralOffset, Height = record.Height,
                StartDistance = record.StartDistance, HeadingDegrees = record.HeadingDegrees, Interval = record.Interval, Range = record.Range,
                IsRepeater = record.IsRepeater
            };
        }

        private static object FindObject(OmsiTileSummary tile, int id)
        {
            return (object)tile.Objects.Find(item => item.Id == id) ?? (object)tile.AttachedObjects.Find(item => item.Id == id) ??
                   tile.SplineAttachments.Find(item => item.Id == id);
        }

        private static bool Remove(OmsiTileSummary tile, object element)
        {
            switch (element)
            {
                case OmsiObjectRecord item:
                    return tile.Objects.Remove(item);
                case OmsiAttachedObjectRecord item:
                    return tile.AttachedObjects.Remove(item);
                case OmsiSplineAttachmentRecord item:
                    return tile.SplineAttachments.Remove(item);
                case OmsiSplineRecord item:
                    return tile.Splines.Remove(item);
                default:
                    return false;
            }
        }

        private static OmsiObjectRecord Copy(OmsiObjectRecord record)
        {
            return new OmsiObjectRecord
            {
                AssetPath = record.AssetPath, Id = record.Id, LocalX = record.LocalX, LocalY = record.LocalY, Height = record.Height,
                HeadingDegrees = record.HeadingDegrees, PitchDegrees = record.PitchDegrees, BankDegrees = record.BankDegrees
            };
        }

        private static OmsiSplineRecord Copy(OmsiSplineRecord record)
        {
            return new OmsiSplineRecord
            {
                AssetPath = record.AssetPath, Id = record.Id, PreviousId = record.PreviousId, NextId = record.NextId, LocalX = record.LocalX,
                LocalY = record.LocalY, Height = record.Height, EndHeight = record.EndHeight, HeadingDegrees = record.HeadingDegrees,
                LengthMeters = record.LengthMeters, RadiusMeters = record.RadiusMeters, IsSplineH = record.IsSplineH, IsMirrored = record.IsMirrored,
                AlignsTerrain = record.AlignsTerrain
            };
        }

        /// <summary>The map with its own tile summaries and element lists; the element records are shared.</summary>
        private static OmsiMapSummary Copy(OmsiMapSummary map)
        {
            var copy = new OmsiMapSummary
            {
                Name = map.Name, FriendlyName = map.FriendlyName, Description = map.Description, RootPath = map.RootPath,
                GlobalConfigPath = map.GlobalConfigPath, TileSizeMeters = map.TileSizeMeters, UsesWorldCoordinates = map.UsesWorldCoordinates,
                NextIdCode = map.NextIdCode, HighestBaseTileId = map.HighestBaseTileId, TerrainTileCount = map.TerrainTileCount
            };
            copy.Warnings.AddRange(map.Warnings);
            foreach (var tile in map.Tiles)
            {
                var tileCopy = new OmsiTileSummary
                {
                    X = tile.X, Y = tile.Y, FileName = tile.FileName, IsAvailable = tile.IsAvailable, HasTerrain = tile.HasTerrain,
                    TerrainMinHeight = tile.TerrainMinHeight, TerrainMaxHeight = tile.TerrainMaxHeight
                };
                tileCopy.Objects.AddRange(tile.Objects);
                tileCopy.Splines.AddRange(tile.Splines);
                tileCopy.AttachedObjects.AddRange(tile.AttachedObjects);
                tileCopy.SplineAttachments.AddRange(tile.SplineAttachments);
                copy.Tiles.Add(tileCopy);
            }
            return copy;
        }

        /// <summary>
        /// The IDs of the elements change lists add, across all variants, so a check of the base map can tell references
        /// to them from references to nothing.
        /// </summary>
        public static HashSet<int> AddedIds(IOmsiFileSource source, OmsiMapSummary baseMap, IEnumerable<OmsiChronoVariant> variants)
        {
            var ids = new HashSet<int>();
            var tilesByFile = new Dictionary<string, OmsiTileSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in baseMap.Tiles)
                tilesByFile[Path.GetFileName(tile.FileName)] = tile;
            foreach (var variant in variants)
                foreach (var file in variant.TileFiles)
                {
                    var tile = tilesByFile.TryGetValue(Path.GetFileName(file), out var known)
                        ? known
                        : new OmsiTileSummary { FileName = Path.GetFileName(file) };
                    var added = OmsiMapReader.ReadAddedElements(source, file, tile);
                    foreach (var item in added.Objects)
                        ids.Add(item.Id);
                    foreach (var item in added.Splines)
                        ids.Add(item.Id);
                    foreach (var item in added.AttachedObjects)
                        ids.Add(item.Id);
                    foreach (var item in added.SplineAttachments)
                        ids.Add(item.Id);
                }
            return ids;
        }
    }
}
