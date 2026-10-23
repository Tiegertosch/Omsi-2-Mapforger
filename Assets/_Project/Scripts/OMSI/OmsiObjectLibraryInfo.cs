using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The scenery objects of an OMSI installation for the object library: every <c>.sco</c> below
    /// <c>Sceneryobjects</c> relative to the installation and sorted, the groups each one lists, where the files come
    /// from and the previews the map editor nEditor caches. Scanning reads every object file once, so it belongs on a worker thread.
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

        public OmsiContentOrigins Origins { get; private set; }

        public NEditorThumbnails Thumbnails { get; private set; }

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
                        var groups = OmsiSceneryObjectGroups.Read(File.ReadAllBytes(path));
                        if (groups.Count > 0)
                            catalog.Groups[relative] = groups;
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        // The object stays in the library without groups.
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
            catalog.Thumbnails = NEditorThumbnails.ForInstallation(installationRoot);
            return catalog;
        }

        /// <summary>A catalog without objects, for an installation that could not be listed.</summary>
        public static OmsiInstallationCatalog Empty(string reason)
        {
            return new OmsiInstallationCatalog
            {
                Origins = OmsiContentOrigins.FoldersOnly(reason),
                Thumbnails = NEditorThumbnails.None()
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

    /// <summary>
    /// Preview images that nEditor, a third-party map editor for OMSI, caches for scenery objects in
    /// <c>Sceneryobjects\nEditorThumbnails</c>. Each is a 256 × 256 PNG named after the object file with dots replaced by
    /// underscores, followed by the MD5 of the object's path relative to the installation, such as
    /// <c>30er_reihensiedlung01_sco</c> + MD5 of <c>Sceneryobjects\Ruede\30er_reihensiedlung01.sco</c>. OMSI itself makes
    /// no previews, so only installations where nEditor ran have them.
    /// </summary>
    public sealed class NEditorThumbnails
    {
        public const string FolderName = "nEditorThumbnails";

        private readonly string folder;
        private readonly HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private NEditorThumbnails(string folder)
        {
            this.folder = folder;
        }

        /// <summary>How many preview images the folder holds.</summary>
        public int Count => files.Count;

        /// <summary>No previews at all.</summary>
        public static NEditorThumbnails None()
        {
            return new NEditorThumbnails(string.Empty);
        }

        /// <summary>Lists the preview folder of an installation once; without the folder no object has a preview.</summary>
        public static NEditorThumbnails ForInstallation(string installationRoot)
        {
            var folder = Path.Combine(installationRoot, "Sceneryobjects", FolderName);
            var thumbnails = new NEditorThumbnails(folder);
            if (Directory.Exists(folder))
                foreach (var path in Directory.EnumerateFiles(folder, "*.png"))
                    thumbnails.files.Add(Path.GetFileName(path));
            return thumbnails;
        }

        /// <summary>The file name OMSI gives the preview of an object, such as <c>bench_sco</c> plus 32 hex digits and <c>.png</c>.</summary>
        public static string FileNameFor(string relativeObjectPath)
        {
            var path = relativeObjectPath.Replace('/', '\\').TrimStart('\\');
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(PathBytes(path));
                var name = new StringBuilder(Path.GetFileName(path).Replace('.', '_'), 80);
                foreach (var value in hash)
                    name.Append(value.ToString("x2"));
                return name.Append(".png").ToString();
            }
        }

        /// <summary>The full path of an object's preview, or null when OMSI has not made one.</summary>
        public string PathFor(string relativeObjectPath)
        {
            if (string.IsNullOrEmpty(relativeObjectPath) || files.Count == 0)
                return null;
            var name = FileNameFor(relativeObjectPath);
            return files.Contains(name) ? Path.Combine(folder, name) : null;
        }

        /// <summary>OMSI hashes the path as Windows-1252 text; a path outside that code page is hashed as UTF-8.</summary>
        private static byte[] PathBytes(string path)
        {
            try
            {
                return OmsiTextFile.Encode(path, OmsiTextEncoding.Windows1252);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8.GetBytes(path);
            }
        }
    }
}
