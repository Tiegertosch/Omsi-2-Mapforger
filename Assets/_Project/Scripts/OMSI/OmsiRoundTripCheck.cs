using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Result of reading every text file of a map folder and serializing it again in memory.</summary>
    public sealed class OmsiRoundTripReport
    {
        public int FileCount { get; internal set; }
        public int ExactCount { get; internal set; }
        public int TileRecordCount { get; internal set; }
        public int TileRecordsWithoutLayout { get; internal set; }
        public int IncompleteTileRecords { get; internal set; }

        /// <summary>Files whose serialized bytes differ from the original, relative to the map folder.</summary>
        public List<string> FilesNotExact { get; } = new List<string>();

        /// <summary>Files that could not be read at all, with the reason.</summary>
        public List<string> UnreadableFiles { get; } = new List<string>();

        public bool Passed => FilesNotExact.Count == 0 && UnreadableFiles.Count == 0 && IncompleteTileRecords == 0;
    }

    /// <summary>
    /// Verifies that <see cref="OmsiRecordFile"/> reproduces every text file of a map byte for byte. Nothing is
    /// written; the check only compares in memory.
    /// </summary>
    public static class OmsiRoundTripCheck
    {
        private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".terrain", ".water", ".rdy", ".prt", ".osn", ".owt", ".bmp", ".dds", ".jpg", ".jpeg", ".png", ".tga"
        };

        public static OmsiRoundTripReport Run(string mapFolder)
        {
            if (string.IsNullOrWhiteSpace(mapFolder) || !Directory.Exists(mapFolder))
                throw new DirectoryNotFoundException("The map folder does not exist: " + mapFolder);

            var root = Path.GetFullPath(mapFolder);
            var report = new OmsiRoundTripReport();
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (BinaryExtensions.Contains(Path.GetExtension(path)))
                    continue;

                var relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var isTile = IsTileFile(path);
                byte[] bytes;
                OmsiRecordFile file;
                try
                {
                    bytes = File.ReadAllBytes(path);
                    file = OmsiRecordFile.Parse(bytes, isTile ? OmsiTileRecordLayouts.Instance : null);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    report.UnreadableFiles.Add(relative + ": " + exception.Message);
                    continue;
                }

                report.FileCount++;
                if (file.IsExactRoundTrip)
                    report.ExactCount++;
                else
                    report.FilesNotExact.Add(relative);

                if (!isTile)
                    continue;
                foreach (var record in file.Records)
                {
                    report.TileRecordCount++;
                    if (!record.HasKnownLayout)
                        report.TileRecordsWithoutLayout++;
                    if (!record.IsComplete)
                        report.IncompleteTileRecords++;
                }
            }

            return report;
        }

        private static bool IsTileFile(string path)
        {
            var name = Path.GetFileName(path);
            return name.StartsWith("tile_", StringComparison.OrdinalIgnoreCase) &&
                   name.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
        }
    }
}
