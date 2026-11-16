using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What deleting a line took with it: how many trips and tracks, and the names of those files. The line file itself is not counted.</summary>
    public sealed class OmsiLineDeletedSummary
    {
        /// <summary>The names, without folder, of the removed files that are not the line file (<c>.ttl</c>), in the order of the change.</summary>
        public IReadOnlyList<string> FileNames { get; }

        public int TripCount { get; }
        public int TrackCount { get; }

        /// <param name="removedPaths">The map-relative paths a line deletion removed, such as <see cref="OmsiTerrainChange.RemovedFiles"/>.</param>
        public OmsiLineDeletedSummary(IEnumerable<string> removedPaths)
        {
            if (removedPaths == null)
                throw new ArgumentNullException(nameof(removedPaths));
            FileNames = removedPaths
                .Where(path => !path.EndsWith(".ttl", StringComparison.OrdinalIgnoreCase))
                .Select(path => path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1))
                .ToList();
            TripCount = FileNames.Count(name => name.EndsWith(".ttp", StringComparison.OrdinalIgnoreCase));
            TrackCount = FileNames.Count(name => name.EndsWith(".ttr", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The first names, at most <paramref name="limit"/>.</summary>
        public IReadOnlyList<string> FirstFileNames(int limit)
        {
            return FileNames.Take(Math.Max(0, limit)).ToList();
        }

        /// <summary>How many names <see cref="FirstFileNames"/> leaves out for the same limit.</summary>
        public int MoreFileCount(int limit)
        {
            return Math.Max(0, FileNames.Count - Math.Max(0, limit));
        }
    }
}
