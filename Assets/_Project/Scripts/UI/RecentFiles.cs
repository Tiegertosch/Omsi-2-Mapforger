using System;
using System.Collections.Generic;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>A project or map opened before, for the welcome screen.</summary>
    internal sealed class RecentFile
    {
        public bool IsProject;
        public string Path;
        public string Name;
    }

    /// <summary>
    /// The projects and maps opened last, newest first, kept as a user preference across sessions. A file opened again
    /// moves to the top; the list keeps <see cref="Limit"/> entries.
    /// </summary>
    internal static class RecentFiles
    {
        public const int Limit = 8;
        private const string Key = "Mapforge.RecentFiles";

        public static List<RecentFile> Load()
        {
            var result = new List<RecentFile>();
            foreach (var line in PlayerPrefs.GetString(Key, string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length == 3 && parts[1].Length > 0)
                    result.Add(new RecentFile { IsProject = parts[0] == "project", Path = parts[1], Name = parts[2] });
            }
            return result;
        }

        /// <param name="path">The project file, or the global.cfg of a map opened without a project.</param>
        public static void Add(bool isProject, string path, string name)
        {
            var entries = Load();
            entries.RemoveAll(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, new RecentFile { IsProject = isProject, Path = path, Name = name });
            if (entries.Count > Limit)
                entries.RemoveRange(Limit, entries.Count - Limit);
            var lines = new List<string>(entries.Count);
            foreach (var entry in entries)
                lines.Add((entry.IsProject ? "project" : "map") + "\t" + entry.Path + "\t" + Clean(entry.Name));
            PlayerPrefs.SetString(Key, string.Join("\n", lines));
            PlayerPrefs.Save();
        }

        private static string Clean(string text)
        {
            return (text ?? string.Empty).Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
