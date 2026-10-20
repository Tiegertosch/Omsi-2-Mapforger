using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Field layouts of global.cfg sections as verified in Documentation/TileRecordInventory.md. Free-text sections
    /// such as [description] and unknown sections return null and keep their lines as free text.
    /// </summary>
    public sealed class OmsiGlobalConfigLayouts : IOmsiRecordLayouts
    {
        public static readonly OmsiGlobalConfigLayouts Instance = new OmsiGlobalConfigLayouts();

        private const int EntryPointFieldCount = 12;

        private static readonly Dictionary<string, int> FixedFieldCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "name", 1 },
            { "friendlyname", 1 },
            { "version", 1 },
            { "NextIDCode", 1 },
            { "worldcoordinates", 0 },
            { "dynhelperactive", 0 },
            { "realrail", 0 },
            { "backgroundimage", 6 },
            { "mapcam", 8 },
            { "moneysystem", 1 },
            { "ticketpack", 1 },
            { "repair_time_min", 1 },
            { "years", 2 },
            { "realyearoffset", 1 },
            { "standarddepot", 1 },
            { "groundtex", 5 },
            { "addseason", 3 },
            { "trafficdensity_road", 2 },
            { "trafficdensity_passenger", 2 },
            { "map", 3 }
        };

        public int? GetFieldCount(string name, int version, IReadOnlyList<OmsiLine> lines, int firstField, out bool isComplete)
        {
            isComplete = true;
            if (FixedFieldCounts.TryGetValue(name, out var count))
                return count;
            if (!string.Equals(name, "entrypoints", StringComparison.OrdinalIgnoreCase))
                return null;

            // A count, then twelve fields per entry point.
            if (firstField < lines.Count &&
                int.TryParse(lines[firstField].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var entries) &&
                entries >= 0)
                return 1 + entries * EntryPointFieldCount;

            isComplete = false;
            return null;
        }
    }
}
