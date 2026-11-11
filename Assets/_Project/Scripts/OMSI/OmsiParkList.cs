using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The parking list of a map (<c>parklist_p.txt</c>): one parked car object per line, from which openOMSI fills each car park
    /// object at random. A car named twice is drawn twice as often.
    /// </summary>
    public static class OmsiParkList
    {
        /// <summary>The file of list <paramref name="index"/>: <c>parklist_p.txt</c> for 0, else <c>parklist_p_n.txt</c>.</summary>
        public static string FileName(int index)
        {
            return index <= 0 ? "parklist_p.txt" : "parklist_p_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt";
        }

        /// <summary>True when a list entry names a file of the installation that has an AI car (<c>ai_*.bus</c>) in its folder.</summary>
        public static bool HasAiCar(string installationRoot, string entry)
        {
            try
            {
                var file = OmsiRelativePath.Resolve(installationRoot, entry);
                var folder = System.IO.Path.GetDirectoryName(file);
                return System.IO.File.Exists(file) && System.IO.Directory.GetFiles(folder, "ai_*.bus").Length > 0;
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>The entries of a list file: its non-empty lines, trimmed.</summary>
        public static List<string> Parse(byte[] bytes)
        {
            var result = new List<string>();
            if (bytes == null)
                return result;
            foreach (var line in OmsiTextFile.Decode(bytes).Split('\n'))
            {
                var entry = line.Trim();
                if (entry.Length > 0)
                    result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// The cars to offer: every parked car of the installation that has an AI car beside it, then the entries of the current list
        /// that are not among them (a car of an add-on that may be missing here), each once.
        /// </summary>
        public static List<string> Candidates(string installationRoot, IList<string> current)
        {
            var result = OmsiParkingRow.DefaultParkList(installationRoot);
            var seen = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in current ?? new string[0])
                if (seen.Add(entry))
                    result.Add(entry);
            return result;
        }

        /// <summary>
        /// The list with exactly the chosen cars: a car that was in the list keeps as many lines as it had (its weight), a new one
        /// gets one; the order follows <paramref name="chosen"/>.
        /// </summary>
        public static List<string> Merge(IList<string> current, IEnumerable<string> chosen)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in current ?? new string[0])
                counts[entry] = counts.TryGetValue(entry, out var count) ? count + 1 : 1;
            var result = new List<string>();
            foreach (var entry in chosen)
                for (var copy = 0; copy < (counts.TryGetValue(entry, out var weight) ? weight : 1); copy++)
                    result.Add(entry);
            return result;
        }
    }
}
