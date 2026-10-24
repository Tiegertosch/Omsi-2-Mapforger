using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The scenery objects of an OMSI installation for the object library: every <c>.sco</c> below
    /// <c>Sceneryobjects</c> relative to the installation and sorted, the groups each one lists, where the files come
    /// from and the tab of the object menu each belongs to. Scanning reads every object file once, so it belongs on a worker thread.
    /// </summary>
    public sealed class OmsiInstallationCatalog
    {
        private OmsiInstallationCatalog()
        {
        }

        public List<string> Objects { get; private set; } = new List<string>();

        /// <summary>The spline profiles (.sli) below <c>Splines</c>, relative to the installation, for building roads.</summary>
        public List<string> SplineProfiles { get; } = new List<string>();

        /// <summary>The <c>[groups]</c> of each object that lists any, by object path.</summary>
        public Dictionary<string, List<string>> Groups { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The tab of the object menu each object belongs to (ADR 030).</summary>
        public Dictionary<string, OmsiObjectCategory> Categories { get; } = new Dictionary<string, OmsiObjectCategory>(StringComparer.OrdinalIgnoreCase);

        public OmsiContentOrigins Origins { get; private set; }


        /// <summary>Lists and reads the objects of an installation; unreadable object files simply have no groups.</summary>
        public static OmsiInstallationCatalog Scan(string installationRoot, IEnumerable<string> steamRoots)
        {
            var catalog = new OmsiInstallationCatalog();
            var folder = Path.Combine(installationRoot, "Sceneryobjects");
            if (Directory.Exists(folder))
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*.sco", SearchOption.AllDirectories))
                {
                    var relative = path.Substring(installationRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    catalog.Objects.Add(relative);
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        var groups = OmsiSceneryObjectGroups.Read(bytes);
                        if (groups.Count > 0)
                            catalog.Groups[relative] = groups;
                        OmsiObjectCategories.ReadMarks(OmsiTextFile.Decode(bytes), out var isTree, out var hasSplineHelper);
                        catalog.Categories[relative] = OmsiObjectCategories.Of(relative, groups, isTree, hasSplineHelper);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        // The object stays in the library without groups, in the tab its folder suggests.
                        catalog.Categories[relative] = OmsiObjectCategories.Of(relative, null, false, false);
                    }
                }
                catalog.Objects.Sort(StringComparer.OrdinalIgnoreCase);
            }
            var splines = Path.Combine(installationRoot, "Splines");
            if (Directory.Exists(splines))
            {
                foreach (var path in Directory.EnumerateFiles(splines, "*.sli", SearchOption.AllDirectories))
                    catalog.SplineProfiles.Add(path.Substring(installationRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                catalog.SplineProfiles.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catalog.Origins = OmsiContentOrigins.ForInstallation(installationRoot, steamRoots);
            return catalog;
        }

        /// <summary>A catalog without objects, for an installation that could not be listed.</summary>
        public static OmsiInstallationCatalog Empty(string reason)
        {
            return new OmsiInstallationCatalog
            {
                Origins = OmsiContentOrigins.FoldersOnly(reason)
            };
        }
    }

    /// <summary>
    /// The groups a scenery object lists in its <c>[groups]</c> section, such as <c>Buildings</c>, <c>Residential</c> and
    /// <c>Row House</c>. An object belongs to each of them; authors keep them in their own way, so they complement the
    /// folders rather than replace them.
    /// </summary>
    public static class OmsiSceneryObjectGroups
    {
        /// <summary>Reads the groups of a <c>.sco</c> file; an object without <c>[groups]</c> has none.</summary>
        public static List<string> Read(byte[] bytes)
        {
            var groups = new List<string>();
            var lines = OmsiTextFile.Decode(bytes).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (!string.Equals(lines[index].Trim(), "[groups]", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (index + 1 >= lines.Length || !int.TryParse(lines[index + 1].Trim(), out var count) || count < 0)
                    continue;
                for (var offset = 0; offset < count && index + 2 + offset < lines.Length; offset++)
                {
                    var name = lines[index + 2 + offset].Trim();
                    if (name.Length == 0 || name.StartsWith("[", StringComparison.Ordinal))
                        break;
                    if (!groups.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                        groups.Add(name);
                }
            }
            return groups;
        }
    }
}
