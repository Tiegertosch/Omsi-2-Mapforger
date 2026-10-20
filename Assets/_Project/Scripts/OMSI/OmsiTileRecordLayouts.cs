using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Field layouts of map tile sections (base tiles and chrono patches) as verified in
    /// Documentation/TileRecordInventory.md. Sections or versions without a verified layout return null and keep
    /// all of their lines as free text, which is still written back unchanged.
    /// </summary>
    public sealed class OmsiTileRecordLayouts : IOmsiRecordLayouts
    {
        public static readonly OmsiTileRecordLayouts Instance = new OmsiTileRecordLayouts();

        private static readonly Dictionary<string, int> FixedFieldCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "version", 1 },
            { "rule", 4 },
            { "kill_rule", 4 },
            { "varparent", 1 },
            { "spline_terrain_align", 0 },
            { "spline_terrain_align_2", 1 },
            { "terrain", 0 },
            { "variable_terrain", 0 },
            { "variable_terrainlightmap", 0 },
            { "water", 0 },
            // Chrono patch commands.
            { "selobject", 1 },
            { "selspline", 1 },
            { "delete", 0 },
            { "typ", 1 },
            { "relabel", 2 }
        };

        public int? GetFieldCount(string name, int version, IReadOnlyList<OmsiLine> lines, int firstField, out bool isComplete)
        {
            isComplete = true;
            if (FixedFieldCounts.TryGetValue(name, out var fixedCount))
                return fixedCount;

            switch (name.ToLowerInvariant())
            {
                case "object":
                    // Detail level (from version 9), path, ID, x, y, z, heading, pitch, bank, label count, labels.
                    return version >= 9 ? CountedFields(lines, firstField, 9, out isComplete) : (int?)null;
                case "attachobj":
                    // Parent, instance, attach point, heading, pitch, bank (pitch and bank from version 12), labels.
                    return version >= 12 ? CountedFields(lines, firstField, 9, out isComplete) : (int?)null;
                case "splineattachement":
                    return CountedFields(lines, firstField, 13, out isComplete);
                case "splineattachement_repeater":
                    // Two extra leading fields: master tile index and first object number.
                    return CountedFields(lines, firstField, 15, out isComplete);
                case "spline":
                    if (version >= 14)
                        return WithOptionalMirror(lines, firstField, 18);
                    if (version >= 11)
                        return WithOptionalMirror(lines, firstField, 16);
                    return null;
                case "spline_h":
                    // [spline] plus the height difference as field 13.
                    if (version >= 14)
                        return WithOptionalMirror(lines, firstField, 19);
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>Fixed fields, then a count field, then that many lines (labels or strings).</summary>
        private static int? CountedFields(IReadOnlyList<OmsiLine> lines, int firstField, int countIndex, out bool isComplete)
        {
            var countLine = firstField + countIndex;
            if (countLine < lines.Count &&
                int.TryParse(lines[countLine].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) &&
                count >= 0)
            {
                isComplete = true;
                return countIndex + 1 + count;
            }

            // Without a valid count the record's end is unknown; keep its lines as free text.
            isComplete = false;
            return null;
        }

        private static int WithOptionalMirror(IReadOnlyList<OmsiLine> lines, int firstField, int fieldCount)
        {
            var mirrorLine = firstField + fieldCount;
            return mirrorLine < lines.Count && string.Equals(lines[mirrorLine].Text.Trim(), "mirror", StringComparison.Ordinal)
                ? fieldCount + 1
                : fieldCount;
        }
    }
}
