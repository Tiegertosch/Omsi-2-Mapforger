using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Hands out new element IDs for a map. IDs are unique map-wide across objects, attachments and splines, so a new
    /// ID starts above both [NextIDCode] and the highest ID in use. The second bound matters: Stadtückheim stores
    /// [NextIDCode] 92 although ID 92 already exists.
    /// </summary>
    public sealed class OmsiIdAllocator
    {
        private readonly HashSet<int> usedIds;

        public OmsiIdAllocator(int nextIdCode, IEnumerable<int> usedIds)
        {
            if (usedIds == null)
                throw new ArgumentNullException(nameof(usedIds));

            this.usedIds = new HashSet<int>(usedIds);
            NextIdCode = nextIdCode;
            foreach (var id in this.usedIds)
                HighestUsedId = Math.Max(HighestUsedId, id);
            NextFreeId = Math.Max(Math.Max(nextIdCode, HighestUsedId + 1), 1);
        }

        /// <summary>The value stored in global.cfg when the map was read.</summary>
        public int NextIdCode { get; }

        public int HighestUsedId { get; }

        /// <summary>True when global.cfg's [NextIDCode] would hand out an ID that already exists.</summary>
        public bool IsNextIdCodeStale => NextIdCode <= HighestUsedId;

        /// <summary>The ID <see cref="Allocate"/> returns next; also the value to store as [NextIDCode] afterwards.</summary>
        public int NextFreeId { get; private set; }

        public int UsedIdCount => usedIds.Count;

        public bool IsInUse(int id)
        {
            return usedIds.Contains(id);
        }

        /// <summary>Returns a new ID that is unused and higher than every ID handed out or read before.</summary>
        public int Allocate()
        {
            while (usedIds.Contains(NextFreeId))
                NextFreeId++;
            if (NextFreeId == int.MaxValue)
                throw new InvalidOperationException("No further OMSI IDs are available.");

            var id = NextFreeId++;
            usedIds.Add(id);
            return id;
        }

        /// <summary>Writes <see cref="NextFreeId"/> into the [NextIDCode] record of a global.cfg read with its layouts.</summary>
        public void ApplyTo(OmsiRecordFile globalConfig)
        {
            if (globalConfig == null)
                throw new ArgumentNullException(nameof(globalConfig));

            foreach (var record in globalConfig.Records)
            {
                if (!string.Equals(record.Name, "NextIDCode", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!record.HasKnownLayout || record.Fields.Count != 1)
                    throw new InvalidOperationException("Read global.cfg with OmsiGlobalConfigLayouts before updating [NextIDCode].");
                record.SetField(0, OmsiNumberFormat.Format(NextFreeId));
                return;
            }

            throw new InvalidOperationException("global.cfg has no [NextIDCode] section.");
        }

        /// <summary>
        /// Reads [NextIDCode] and the IDs of every base tile listed in global.cfg and of every chrono tile. Files are
        /// only read.
        /// </summary>
        public static OmsiIdAllocator ForMap(string globalConfigPath)
        {
            var configPath = Path.GetFullPath(globalConfigPath);
            return ForMap(new OmsiFolderSource(Path.GetDirectoryName(configPath)), Path.GetFileName(configPath));
        }

        /// <summary>
        /// Same as <see cref="ForMap(string)"/> through a file source, so a workspace's edited tiles count. Chrono
        /// tiles are always read from the original map folder because the workspace does not list them.
        /// </summary>
        public static OmsiIdAllocator ForMap(IOmsiFileSource source, string globalConfigRelativePath = "global.cfg")
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var config = OmsiRecordFile.Parse(source.ReadAllBytes(globalConfigRelativePath), OmsiGlobalConfigLayouts.Instance);

            var nextIdCode = 0;
            var tileFiles = new List<string>();
            foreach (var record in config.Records)
            {
                if (string.Equals(record.Name, "NextIDCode", StringComparison.OrdinalIgnoreCase) && record.TryGetInt(0, out var value))
                    nextIdCode = value;
                else if (string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase) && record.Fields.Count == 3)
                    tileFiles.Add(record.GetField(2));
            }

            var chronoFolder = Path.Combine(source.RootPath, "Chrono");
            if (Directory.Exists(chronoFolder))
                foreach (var path in Directory.GetFiles(chronoFolder, "tile_*.map", SearchOption.AllDirectories))
                    tileFiles.Add(path.Substring(source.RootPath.Length).TrimStart(Path.DirectorySeparatorChar));

            var ids = new List<int>();
            foreach (var tile in tileFiles)
            {
                string relative;
                try
                {
                    relative = OmsiRelativePath.Normalize(tile);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (source.FileExists(relative))
                    ids.AddRange(OmsiTileRecords.DefinedIds(OmsiRecordFile.Parse(source.ReadAllBytes(relative), OmsiTileRecordLayouts.Instance)));
            }

            return new OmsiIdAllocator(nextIdCode, ids);
        }
    }
}
