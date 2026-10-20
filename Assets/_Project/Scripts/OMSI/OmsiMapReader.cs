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
            public readonly List<string> Lines = new List<string>();
        }

        public static OmsiMapSummary Read(string globalConfigPath)
        {
            if (string.IsNullOrWhiteSpace(globalConfigPath))
                throw new ArgumentException("Select an OMSI global.cfg file.", nameof(globalConfigPath));

            var fullConfigPath = Path.GetFullPath(globalConfigPath);
            if (!File.Exists(fullConfigPath))
                throw new FileNotFoundException("The selected global.cfg file does not exist.", fullConfigPath);

            var rootPath = Path.GetDirectoryName(fullConfigPath);
            var sections = ReadSections(fullConfigPath);
            var summary = new OmsiMapSummary
            {
                RootPath = rootPath,
                GlobalConfigPath = fullConfigPath,
                Name = FirstValue(sections, "name"),
                FriendlyName = FirstValue(sections, "friendlyname"),
                Description = SectionText(sections, "description")
            };
            if (HasSection(sections, "worldcoordinates"))
            {
                summary.UsesWorldCoordinates = true;
                summary.TileSizeMeters = OmsiMapSummary.WorldCoordinateTileSizeMeters;
            }

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

                var relativeTilePath = values[2].Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var fullTilePath = Path.GetFullPath(Path.Combine(rootPath, relativeTilePath));
                if (!IsWithinDirectory(rootPath, fullTilePath))
                {
                    summary.Warnings.Add("Ignored a tile path outside the selected map folder: " + values[2]);
                    continue;
                }

                var tile = new OmsiTileSummary
                {
                    X = tileX,
                    Y = tileY,
                    FileName = values[2],
                    IsAvailable = File.Exists(fullTilePath)
                };

                if (tile.IsAvailable)
                {
                    ReadTile(fullTilePath, tile, summary);
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

            return summary;
        }

        /// <summary>Oldest tile version whose [spline] records carry separate previous and next IDs.</summary>
        private const int FirstVersionWithSplineNeighbourIds = 11;

        private static void ReadTile(string tilePath, OmsiTileSummary tile, OmsiMapSummary summary)
        {
            var sections = ReadSections(tilePath);
            var version = ReadTileVersion(sections);
            var decodeSplines = version < 0 || version >= FirstVersionWithSplineNeighbourIds;
            var skippedOldSplines = 0;
            foreach (var section in sections)
            {
                if (string.Equals(section.Name, "object", StringComparison.OrdinalIgnoreCase))
                {
                    tile.ObjectCount++;
                    if (TryReadObject(section, out var objectRecord))
                        tile.Objects.Add(objectRecord);
                    else
                        summary.Warnings.Add("Could not decode an [object] placement in " + tile.FileName + ".");
                }
                else if (string.Equals(section.Name, "spline", StringComparison.OrdinalIgnoreCase))
                {
                    tile.SplineCount++;
                    if (!decodeSplines)
                        skippedOldSplines++;
                    else if (TryReadSpline(section, out var splineRecord))
                        tile.Splines.Add(splineRecord);
                    else
                        summary.Warnings.Add("Could not decode a [spline] placement in " + tile.FileName + ".");
                }
                else if (string.Equals(section.Name, "attachObj", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(section.Name, "splineAttachement", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(section.Name, "splineAttachement_repeater", StringComparison.OrdinalIgnoreCase))
                    tile.AttachmentCount++;
            }

            if (skippedOldSplines > 0)
                summary.Warnings.Add(tile.FileName + " uses tile version " + version + "; its " + skippedOldSplines +
                                     " spline(s) use an older field layout and were counted but not decoded.");

            var terrainPath = tilePath + ".terrain";
            if (TryReadTerrainRange(terrainPath, out var minHeight, out var maxHeight))
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

        private static bool TryReadObject(Section section, out OmsiObjectRecord record)
        {
            record = null;
            var values = Values(section);
            if (values.Count < 9 || !TryDouble(values, 3, out var x) || !TryDouble(values, 4, out var y) ||
                !TryDouble(values, 5, out var height) || !TryDouble(values, 6, out var heading) ||
                !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return false;

            record = new OmsiObjectRecord
            {
                AssetPath = values[1],
                Id = id,
                LocalX = x,
                LocalY = y,
                Height = height,
                HeadingDegrees = heading
            };
            return true;
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

            record = new OmsiSplineRecord
            {
                AssetPath = values[1],
                Id = id,
                PreviousId = previousId,
                NextId = nextId,
                LocalX = x,
                LocalY = y,
                Height = height,
                HeadingDegrees = heading,
                LengthMeters = length,
                RadiusMeters = radius
            };
            return true;
        }

        private static bool TryReadTerrainRange(string path, out float minHeight, out float maxHeight)
        {
            minHeight = 0f;
            maxHeight = 0f;
            const int terrainResolution = 61;
            const int terrainHeader = 60;
            const int terrainValueCount = terrainResolution * terrainResolution;
            const int expectedLength = sizeof(int) + terrainValueCount * sizeof(float);

            if (!File.Exists(path))
                return false;

            using (var stream = File.OpenRead(path))
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

        private static List<Section> ReadSections(string path)
        {
            var result = new List<Section>();
            Section current = null;
            foreach (var rawLine in OmsiTextFile.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    current = new Section { Name = line.Substring(1, line.Length - 2).Trim() };
                    result.Add(current);
                }
                else if (current != null)
                {
                    current.Lines.Add(rawLine);
                }
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

        private static bool IsWithinDirectory(string directory, string path)
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
