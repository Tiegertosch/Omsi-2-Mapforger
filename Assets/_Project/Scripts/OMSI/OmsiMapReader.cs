using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Reads the map identity, tile list, and basic record counts from an OMSI global.cfg.</summary>
    public static class OmsiMapReader
    {
        private sealed class Section
        {
            public string Name;

            /// <summary>Field lines first, then the free text after the record.</summary>
            public readonly List<string> Lines = new List<string>();

            /// <summary>Number of field lines with a verified layout; 0 when the layout is unknown.</summary>
            public int FieldCount;
        }

        public static OmsiMapSummary Read(string globalConfigPath)
        {
            if (string.IsNullOrWhiteSpace(globalConfigPath))
                throw new ArgumentException("Select an OMSI global.cfg file.", nameof(globalConfigPath));

            var fullConfigPath = Path.GetFullPath(globalConfigPath);
            if (!File.Exists(fullConfigPath))
                throw new FileNotFoundException("The selected global.cfg file does not exist.", fullConfigPath);

            return Read(new OmsiFolderSource(Path.GetDirectoryName(fullConfigPath)), Path.GetFileName(fullConfigPath));
        }

        /// <summary>
        /// Reads a map through a file source, for example a project workspace that overlays edited files on the
        /// original map. <see cref="OmsiMapSummary.RootPath"/> stays the original map folder.
        /// </summary>
        public static OmsiMapSummary Read(IOmsiFileSource source, string globalConfigRelativePath = "global.cfg")
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (!source.FileExists(globalConfigRelativePath))
                throw new FileNotFoundException("The map has no " + globalConfigRelativePath + ".",
                    Path.Combine(source.RootPath, globalConfigRelativePath));

            var sections = ReadSections(source, globalConfigRelativePath, OmsiGlobalConfigLayouts.Instance);
            var summary = new OmsiMapSummary
            {
                RootPath = source.RootPath,
                GlobalConfigPath = OmsiRelativePath.Resolve(source.RootPath, globalConfigRelativePath),
                Name = FirstValue(sections, "name"),
                FriendlyName = FirstValue(sections, "friendlyname"),
                Description = SectionText(sections, "description")
            };
            summary.UsesWorldCoordinates = HasSection(sections, "worldcoordinates");

            foreach (var mapSection in sections)
            {
                if (!string.Equals(mapSection.Name, "map", StringComparison.OrdinalIgnoreCase))
                    continue;

                var values = Values(mapSection);
                if (values.Count < 3 || string.IsNullOrWhiteSpace(values[2]) ||
                    !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileX) ||
                    !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileY))
                {
                    summary.Warnings.Add("Ignored a malformed [map] entry in global.cfg.");
                    continue;
                }

                string relativeTilePath;
                try
                {
                    relativeTilePath = OmsiRelativePath.Normalize(values[2]);
                }
                catch (ArgumentException)
                {
                    summary.Warnings.Add("Ignored a tile path outside the selected map folder: " + values[2]);
                    continue;
                }

                var tile = new OmsiTileSummary
                {
                    X = tileX,
                    Y = tileY,
                    FileName = values[2],
                    IsAvailable = source.FileExists(relativeTilePath)
                };

                if (tile.IsAvailable)
                {
                    ReadTile(source, relativeTilePath, tile, summary);
                    summary.ObjectCount += tile.ObjectCount;
                    summary.SplineCount += tile.SplineCount;
                    summary.AttachmentCount += tile.AttachmentCount;
                }
                else
                {
                    summary.Warnings.Add("Map tile not found: " + values[2]);
                }

                summary.Tiles.Add(tile);
            }

            if (summary.Tiles.Count == 0)
                summary.Warnings.Add("No valid [map] tile entries were found in global.cfg.");
            if (summary.UsesWorldCoordinates)
                summary.TileSizeMeters = OmsiWorldTiles.GridEdgeMeters(summary.Tiles.ConvertAll(tile => tile.Y));

            if (int.TryParse(FirstValue(sections, "NextIDCode"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nextIdCode))
            {
                summary.NextIdCode = nextIdCode;
                if (nextIdCode <= summary.HighestBaseTileId)
                    summary.Warnings.Add("global.cfg [NextIDCode] " + nextIdCode + " is not above the highest ID " +
                                         summary.HighestBaseTileId + "; new IDs must start at " + (summary.HighestBaseTileId + 1) + ".");
            }

            return summary;
        }

        /// <summary>Oldest tile version whose [spline] records carry separate previous and next IDs.</summary>
        private const int FirstVersionWithSplineNeighbourIds = 11;

        /// <summary>
        /// The elements a file adds in the tile record format, such as a chrono change list: objects, splines, attached
        /// objects and rows along splines, in a new tile summary with the position and file name of
        /// <paramref name="tile"/>. Selections and other commands of the file are left to the caller.
        /// </summary>
        internal static OmsiTileSummary ReadAddedElements(IOmsiFileSource source, string path, OmsiTileSummary tile)
        {
            var added = new OmsiTileSummary { X = tile.X, Y = tile.Y, FileName = tile.FileName, IsAvailable = true };
            ReadTileRecords(source, path, added, new OmsiMapSummary());
            return added;
        }

        private static void ReadTile(IOmsiFileSource source, string tilePath, OmsiTileSummary tile, OmsiMapSummary summary)
        {
            ReadTileRecords(source, tilePath, tile, summary);
            ReadTerrain(source, tilePath, tile, summary);
        }

        private static void ReadTileRecords(IOmsiFileSource source, string tilePath, OmsiTileSummary tile, OmsiMapSummary summary)
        {
            var sections = ReadSections(source, tilePath, OmsiTileRecordLayouts.Instance);
            var version = ReadTileVersion(sections);
            var decodeSplines = version < 0 || version >= FirstVersionWithSplineNeighbourIds;
            var skippedOldSplines = 0;
            // Terrain alignment applies to the spline right before it; another placed element ends that.
            OmsiSplineRecord lastSpline = null;
            // Rules belong to the object or spline right before them; another placed element ends that.
            List<OmsiPathRule> lastRules = null;
            var hasWater = false;
            foreach (var section in sections)
            {
                TrackHighestId(section, summary);
                if (string.Equals(section.Name, "water", StringComparison.OrdinalIgnoreCase))
                    hasWater = true;
                if (string.Equals(section.Name, "rule", StringComparison.OrdinalIgnoreCase) || string.Equals(section.Name, "kill_rule", StringComparison.OrdinalIgnoreCase))
                {
                    if (lastRules != null && TryReadRule(section, out var rule))
                        lastRules.Add(rule);
                    continue;
                }
                if (PlacedSections.Contains(section.Name))
                    lastRules = null;
                if ((string.Equals(section.Name, "spline_terrain_align", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(section.Name, "spline_terrain_align_2", StringComparison.OrdinalIgnoreCase)) && lastSpline != null)
                {
                    lastSpline.AlignsTerrain = true;
                    continue;
                }
                if (PlacedSections.Contains(section.Name))
                    lastSpline = null;
                if (string.Equals(section.Name, "object", StringComparison.OrdinalIgnoreCase))
                {
                    tile.ObjectCount++;
                    if (TryReadObject(section, out var objectRecord))
                    {
                        tile.Objects.Add(objectRecord);
                        lastRules = objectRecord.Rules;
                    }
                    else
                        summary.Warnings.Add("Could not decode an [object] placement in " + tile.FileName + ".");
                }
                else if (string.Equals(section.Name, "spline", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(section.Name, "spline_h", StringComparison.OrdinalIgnoreCase))
                {
                    tile.SplineCount++;
                    if (!decodeSplines)
                        skippedOldSplines++;
                    else if (TryReadSpline(section, out var splineRecord))
                    {
                        tile.Splines.Add(splineRecord);
                        lastSpline = splineRecord;
                        lastRules = splineRecord.Rules;
                    }
                    else
                        summary.Warnings.Add("Could not decode a [" + section.Name + "] placement in " + tile.FileName + ".");
                }
                else if (string.Equals(section.Name, "attachObj", StringComparison.OrdinalIgnoreCase))
                {
                    tile.AttachmentCount++;
                    if (version >= 12 && TryReadAttachedObject(section, out var attached))
                        tile.AttachedObjects.Add(attached);
                }
                else if (string.Equals(section.Name, "splineAttachement", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(section.Name, "splineAttachement_repeater", StringComparison.OrdinalIgnoreCase))
                {
                    tile.AttachmentCount++;
                    if (TryReadSplineAttachment(section, out var attachment))
                        tile.SplineAttachments.Add(attachment);
                }
            }

            tile.Water = hasWater ? ReadWater(source, tilePath) : null;
            if (skippedOldSplines > 0)
                summary.Warnings.Add(tile.FileName + " uses tile version " + version + "; its " + skippedOldSplines +
                                     " spline(s) use an older field layout and were counted but not decoded.");
        }

        /// <summary>
        /// The water surface of a tile with [water]: the four corner heights of tile.map.water when it holds one surface,
        /// else −5 m at every corner, as Omsi.exe and openOMSI take it.
        /// </summary>
        internal static float[] ReadWater(IOmsiFileSource source, string tilePath)
        {
            var path = tilePath + ".water";
            if (source.FileExists(path))
            {
                var bytes = source.ReadAllBytes(path);
                if (bytes.Length >= 20 && BitConverter.ToUInt32(bytes, 0) == 1)
                    return new[] { BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8), BitConverter.ToSingle(bytes, 12), BitConverter.ToSingle(bytes, 16) };
            }
            return new[] { -5f, -5f, -5f, -5f };
        }

        private static void ReadTerrain(IOmsiFileSource source, string tilePath, OmsiTileSummary tile, OmsiMapSummary summary)
        {
            var terrainPath = tilePath + ".terrain";
            if (source.FileExists(terrainPath) && TryReadTerrainRange(source.ReadAllBytes(terrainPath), out var minHeight, out var maxHeight))
            {
                tile.HasTerrain = true;
                tile.TerrainMinHeight = minHeight;
                tile.TerrainMaxHeight = maxHeight;
                summary.TerrainTileCount++;
            }
            else
            {
                summary.Warnings.Add("Terrain data missing or not recognized for " + tile.FileName + ".");
            }
        }

        private static bool TryReadAttachedObject(Section section, out OmsiAttachedObjectRecord record)
        {
            record = null;
            var values = Values(section);
            if (values.Count < 9 || !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                !int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent) ||
                !int.TryParse(values[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var point) ||
                !TryDouble(values, 6, out var heading))
                return false;
            TryDouble(values, 7, out var pitch);
            TryDouble(values, 8, out var bank);
            record = new OmsiAttachedObjectRecord
            {
                AssetPath = values[1], Id = id, ParentId = parent, AttachPointIndex = point, HeadingDegrees = heading,
                PitchDegrees = pitch, BankDegrees = bank
            };
            return true;
        }

        /// <summary>A repeater has two more leading fields: the master's tile index and the number of its first object.</summary>
        private static bool TryReadSplineAttachment(Section section, out OmsiSplineAttachmentRecord record)
        {
            record = null;
            var values = Values(section);
            var repeater = string.Equals(section.Name, "splineAttachement_repeater", StringComparison.OrdinalIgnoreCase);
            var shift = repeater ? 2 : 0;
            if (values.Count < 12 + shift || !int.TryParse(values[2 + shift], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                !int.TryParse(values[3 + shift], NumberStyles.Integer, CultureInfo.InvariantCulture, out var spline) ||
                !TryDouble(values, 4 + shift, out var offset) || !TryDouble(values, 5 + shift, out var height) ||
                !TryDouble(values, 6 + shift, out var start) || !TryDouble(values, 7 + shift, out var heading) ||
                !TryDouble(values, 10 + shift, out var interval) || !TryDouble(values, 11 + shift, out var range))
                return false;
            record = new OmsiSplineAttachmentRecord
            {
                AssetPath = values[1 + shift], Id = id, SplineIndex = spline, LateralOffset = offset, Height = height,
                StartDistance = start, HeadingDegrees = heading, Interval = interval, Range = range, IsRepeater = repeater
            };
            return true;
        }

        private static bool TryReadObject(Section section, out OmsiObjectRecord record)
        {
            record = null;
            var values = Values(section);
            if (values.Count < 9 || !TryDouble(values, 3, out var x) || !TryDouble(values, 4, out var y) ||
                !TryDouble(values, 5, out var height) || !TryDouble(values, 6, out var heading) ||
                !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return false;
            TryDouble(values, 7, out var pitch);
            TryDouble(values, 8, out var bank);

            record = new OmsiObjectRecord
            {
                AssetPath = values[1],
                Id = id,
                LocalX = x,
                LocalY = y,
                Height = height,
                HeadingDegrees = heading,
                PitchDegrees = pitch,
                BankDegrees = bank
            };
            return true;
        }

        /// <summary>A [rule] or [kill_rule]: path index, name, value and the extra number.</summary>
        private static bool TryReadRule(Section section, out OmsiPathRule rule)
        {
            rule = null;
            var values = Values(section);
            if (values.Count < 3 || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var path) || !TryDouble(values, 2, out var value))
                return false;
            var extra = 0.0;
            if (values.Count > 3)
                TryDouble(values, 3, out extra);
            rule = new OmsiPathRule
            {
                PathIndex = path, Name = values[1], Value = value, Extra = extra, Kills = string.Equals(section.Name, "kill_rule", StringComparison.OrdinalIgnoreCase)
            };
            return true;
        }

        private static readonly HashSet<string> PlacedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "object", "attachObj", "splineAttachement", "splineAttachement_repeater", "spline", "spline_h"
        };

        private static readonly HashSet<string> SectionsDefiningIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "object", "attachObj", "splineAttachement", "spline", "spline_h"
        };

        private static void TrackHighestId(Section section, OmsiMapSummary summary)
        {
            if (!SectionsDefiningIds.Contains(section.Name))
                return;
            var values = Values(section);
            if (values.Count > 2 && int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, id);
        }

        /// <summary>Returns the [version] value of a tile file, or -1 when the file has none.</summary>
        private static int ReadTileVersion(List<Section> sections)
        {
            foreach (var section in sections)
            {
                if (!string.Equals(section.Name, "version", StringComparison.OrdinalIgnoreCase))
                    continue;
                var values = Values(section);
                return values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                    ? version
                    : -1;
            }

            return -1;
        }

        private static bool TryReadSpline(Section section, out OmsiSplineRecord record)
        {
            record = null;
            var values = Values(section);
            if (values.Count < 11 || !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                !int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var previousId) ||
                !int.TryParse(values[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nextId) ||
                !TryDouble(values, 5, out var x) || !TryDouble(values, 6, out var height) ||
                !TryDouble(values, 7, out var y) || !TryDouble(values, 8, out var heading) ||
                !TryDouble(values, 9, out var length) || !TryDouble(values, 10, out var radius))
                return false;

            var isSplineH = string.Equals(section.Name, "spline_h", StringComparison.OrdinalIgnoreCase);
            var heightDifference = 0.0;
            var startGradient = 0.0;
            var endGradient = 0.0;
            var gradients = values.Count > 12 && TryDouble(values, 11, out startGradient) && TryDouble(values, 12, out endGradient);
            if (isSplineH && values.Count > 13 && TryDouble(values, 13, out var difference))
                heightDifference = difference;
            else if (!isSplineH && values.Count > 12 && TryDouble(values, 11, out var gradientStart) && TryDouble(values, 12, out var gradientEnd))
                heightDifference = length * (gradientStart + gradientEnd) / 200.0;

            record = new OmsiSplineRecord
            {
                AssetPath = values[1],
                Id = id,
                PreviousId = previousId,
                NextId = nextId,
                LocalX = x,
                LocalY = y,
                Height = height,
                EndHeight = height + heightDifference,
                GradientStartPercent = gradients ? startGradient : 0.0,
                GradientEndPercent = gradients ? endGradient : 0.0,
                HeadingDegrees = heading,
                LengthMeters = length,
                RadiusMeters = radius,
                IsSplineH = isSplineH,
                IsMirrored = section.FieldCount > 0 && values[section.FieldCount - 1] == "mirror"
            };
            // The chain offset is the last number: field 15 in versions 11 and 12, 17 from 14 on, one later in [spline_h].
            var last = record.IsMirrored ? section.FieldCount - 2 : section.FieldCount - 1;
            if (last >= (isSplineH ? 16 : 15) && TryDouble(values, last, out var chainOffset))
                record.ChainOffset = chainOffset;
            return true;
        }

        private static bool TryReadTerrainRange(byte[] bytes, out float minHeight, out float maxHeight)
        {
            minHeight = 0f;
            maxHeight = 0f;
            const int terrainResolution = 61;
            const int terrainHeader = 60;
            const int terrainValueCount = terrainResolution * terrainResolution;
            const int expectedLength = sizeof(int) + terrainValueCount * sizeof(float);

            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length != expectedLength || reader.ReadInt32() != terrainHeader)
                    return false;

                minHeight = float.PositiveInfinity;
                maxHeight = float.NegativeInfinity;
                for (var i = 0; i < terrainValueCount; i++)
                {
                    var value = reader.ReadSingle();
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        return false;
                    minHeight = Math.Min(minHeight, value);
                    maxHeight = Math.Max(maxHeight, value);
                }
            }

            return true;
        }

        private static bool TryDouble(List<string> values, int index, out double result)
        {
            return double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Reads a file's sections through <see cref="OmsiRecordFile"/>. With tile layouts, records are read
        /// positionally, so a label that looks like a section header stays part of its record.
        /// </summary>
        private static List<Section> ReadSections(IOmsiFileSource source, string relativePath, IOmsiRecordLayouts layouts)
        {
            var file = OmsiRecordFile.Parse(source.ReadAllBytes(relativePath), layouts);
            var result = new List<Section>(file.Records.Count);
            foreach (var record in file.Records)
            {
                var section = new Section { Name = record.Name, FieldCount = record.HasKnownLayout ? record.Fields.Count : 0 };
                foreach (var line in record.Fields)
                    section.Lines.Add(line.Text);
                foreach (var line in record.Trailer)
                    section.Lines.Add(line.Text);
                result.Add(section);
            }

            return result;
        }

        private static string FirstValue(List<Section> sections, string sectionName)
        {
            foreach (var section in sections)
            {
                if (!string.Equals(section.Name, sectionName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var values = Values(section);
                if (values.Count > 0)
                    return values[0];
            }

            return string.Empty;
        }

        private static string SectionText(List<Section> sections, string sectionName)
        {
            foreach (var section in sections)
                if (string.Equals(section.Name, sectionName, StringComparison.OrdinalIgnoreCase))
                    return string.Join(Environment.NewLine, section.Lines).Trim();
            return string.Empty;
        }

        private static bool HasSection(List<Section> sections, string sectionName)
        {
            foreach (var section in sections)
                if (string.Equals(section.Name, sectionName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Returns the section lines in file order. OMSI records are positional: every line directly below the
        /// header is one field, so blank lines are kept to avoid shifting later fields into the wrong slot.
        /// Text after the record (OMSI writes labels such as "Object Nr. 1") is never addressed by index.
        /// </summary>
        private static List<string> Values(Section section)
        {
            var result = new List<string>(section.Lines.Count);
            foreach (var rawLine in section.Lines)
                result.Add(rawLine.Trim());
            return result;
        }
    }
}
