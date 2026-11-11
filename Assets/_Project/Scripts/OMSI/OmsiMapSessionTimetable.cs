using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    public sealed partial class OmsiMapSession
    {
        private const string TimetableFolder = "TTData";

        /// <summary>The names of the line files of the map's TTData folder, as the project copy has them.</summary>
        public List<string> TimetableLineNames()
        {
            var workspace = RequireWorkspace();
            return workspace.EnumerateFiles(TimetableFolder, "*.ttl", false)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Opens a line file for editing; <paramref name="line"/> is its name without .ttl.</summary>
        public OmsiLineFile ReadLineFile(string line)
        {
            return OmsiLineFile.Parse(RequireWorkspace().ReadAllBytes(TimetablePath(line, ".ttl")));
        }

        /// <summary>Opens a trip file for editing, or null when the map has none of that name.</summary>
        public OmsiTripFile ReadTripFile(string trip)
        {
            var path = TimetablePath(trip, ".ttp");
            var workspace = RequireWorkspace();
            return workspace.FileExists(path) ? OmsiTripFile.Parse(workspace.ReadAllBytes(path)) : null;
        }

        /// <summary>Writes an edited line file into the project copy.</summary>
        public void WriteLineFile(string line, OmsiLineFile file)
        {
            RequireWorkspace().Save(TimetablePath(line, ".ttl"), file.ToBytes());
        }

        /// <summary>Writes an edited trip file into the project copy.</summary>
        public void WriteTripFile(string trip, OmsiTripFile file)
        {
            RequireWorkspace().Save(TimetablePath(trip, ".ttp"), file.ToBytes());
        }

        private static string TimetablePath(string name, string extension)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
                throw new ArgumentException("Expected the name of a timetable file without a folder.", nameof(name));
            return TimetableFolder + "\\" + name + extension;
        }
    }
}
