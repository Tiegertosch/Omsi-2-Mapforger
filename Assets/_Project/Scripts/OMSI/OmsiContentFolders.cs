using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The folders below <c>Sceneryobjects</c> and <c>Splines</c> that the user left out of the object and road menus
    /// (ADR 084), for each OMSI installation, kept only on this computer: one tab-separated line each, installation folder
    /// and folder relative to it (such as <c>Sceneryobjects\Trees_MC</c>). Listing the installation does not enter a folder
    /// that is left out. The editor's own folder <c>Mapforge</c> is not one of them; the project folders inside it
    /// (<c>Sceneryobjects\Mapforge\Grundorf</c>), with the road types, junctions and objects made for each project, are.
    /// </summary>
    public sealed class OmsiContentFolders
    {
        public const string SceneryObjectsFolder = "Sceneryobjects";
        public const string SplinesFolder = "Splines";

        /// <summary>The editor's own folder below <c>Sceneryobjects</c> and <c>Splines</c>, with a folder for each project.</summary>
        public const string EditorFolder = "Mapforge";

        private readonly Dictionary<string, HashSet<string>> excluded = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where they are kept unless said otherwise: the user's local application data.</summary>
        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMSI MapWorks", "content-folders.txt");

        /// <summary>The folders left out for an installation, relative to it; empty when none are.</summary>
        public HashSet<string> ExcludedFor(string installationRoot)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(installationRoot) && excluded.TryGetValue(RootKey(installationRoot), out var folders))
                result.UnionWith(folders);
            return result;
        }

        /// <summary>
        /// Sets the folders left out for an installation, replacing those set before. Folders that cannot be left out
        /// (see <see cref="Normalize"/>) are dropped.
        /// </summary>
        public void SetExcluded(string installationRoot, IEnumerable<string> folders)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders ?? Array.Empty<string>())
            {
                var normal = Normalize(folder);
                if (normal != null)
                    set.Add(normal);
            }
            var key = RootKey(installationRoot);
            if (set.Count == 0)
                excluded.Remove(key);
            else
                excluded[key] = set;
        }

        /// <summary>
        /// The folder in the form it is kept, <c>Sceneryobjects\Name</c>, <c>Splines\Name</c> or a project folder of the
        /// editor such as <c>Sceneryobjects\Mapforge\Grundorf</c>; null for anything else, the editor's folder itself included.
        /// </summary>
        public static string Normalize(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return null;
            var parts = folder.Trim().Replace('/', '\\').Trim('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3)
                return null;
            foreach (var part in parts)
                if (part.Trim().Length == 0 || part == "." || part == "..")
                    return null;
            string top;
            if (string.Equals(parts[0], SceneryObjectsFolder, StringComparison.OrdinalIgnoreCase))
                top = SceneryObjectsFolder;
            else if (string.Equals(parts[0], SplinesFolder, StringComparison.OrdinalIgnoreCase))
                top = SplinesFolder;
            else
                return null;
            var isEditor = string.Equals(parts[1], EditorFolder, StringComparison.OrdinalIgnoreCase);
            if (parts.Length == 2)
                return isEditor ? null : top + "\\" + parts[1];
            return isEditor ? top + "\\" + EditorFolder + "\\" + parts[2] : null;
        }

        /// <summary>The folder a project's made road types, junctions and objects go to below a top folder, as kept here.</summary>
        public static string ProjectFolder(string top, string projectName)
        {
            return Normalize(top + "\\" + EditorFolder + "\\" + OmsiGeneratedObjectStore.SafeName(projectName, "Project"));
        }

        /// <summary>
        /// The folders below <c>Sceneryobjects</c> or <c>Splines</c> of an installation that can be left out, sorted by
        /// name: those right below it, and in place of <c>Mapforge</c> the project folders inside it. Reads only these
        /// folder levels.
        /// </summary>
        public static List<string> FoldersOf(string installationRoot, string top)
        {
            var result = new List<string>();
            var folder = Path.Combine(installationRoot ?? string.Empty, top);
            if (string.IsNullOrWhiteSpace(installationRoot) || !Directory.Exists(folder))
                return result;
            foreach (var path in Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                if (string.Equals(name, EditorFolder, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var project in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
                    {
                        var projectFolder = Normalize(top + "\\" + EditorFolder + "\\" + Path.GetFileName(project));
                        if (projectFolder != null)
                            result.Add(projectFolder);
                    }
                    continue;
                }
                var normal = Normalize(top + "\\" + name);
                if (normal != null)
                    result.Add(normal);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// The files below <c>Sceneryobjects</c> or <c>Splines</c> of an installation that match a pattern, without
        /// entering the folders left out: the files right in the top folder, then each folder below it that is not left out
        /// (inside <c>Mapforge</c> each project folder that is not).
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(string installationRoot, string top, string pattern, ICollection<string> excludedFolders)
        {
            var folder = Path.Combine(installationRoot, top);
            if (!Directory.Exists(folder))
                yield break;
            foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
                yield return file;
            foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, EditorFolder, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                        yield return file;
                    foreach (var project in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (IsExcluded(top + "\\" + EditorFolder + "\\" + Path.GetFileName(project), excludedFolders))
                            continue;
                        foreach (var file in Directory.EnumerateFiles(project, pattern, SearchOption.AllDirectories))
                            yield return file;
                    }
                    continue;
                }
                if (IsExcluded(top + "\\" + name, excludedFolders))
                    continue;
                foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
                    yield return file;
            }
        }

        private static bool IsExcluded(string folder, ICollection<string> excludedFolders)
        {
            var name = Normalize(folder);
            return name != null && excludedFolders != null && excludedFolders.Contains(name);
        }

        /// <summary>Reads the file; a missing or unreadable file means nothing is left out, broken lines are skipped.</summary>
        public static OmsiContentFolders Load(string path)
        {
            try
            {
                return File.Exists(path) ? Parse(OmsiTextFile.Decode(File.ReadAllBytes(path))) : new OmsiContentFolders();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new OmsiContentFolders();
            }
        }

        /// <summary>Writes the file through a temporary file, so that a broken write leaves the old one.</summary>
        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, new UTF8Encoding(false).GetBytes(ToString()));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }

        public static OmsiContentFolders Parse(string text)
        {
            var result = new OmsiContentFolders();
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                    continue;
                var folder = Normalize(parts[1]);
                if (folder == null)
                    continue;
                var key = RootKey(parts[0]);
                if (!result.excluded.TryGetValue(key, out var set))
                    result.excluded[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(folder);
            }
            return result;
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            var roots = new List<string>(excluded.Keys);
            roots.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var folders = new List<string>(excluded[root]);
                folders.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in folders)
                    text.Append(root).Append('\t').Append(folder).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>An installation folder as it is kept: backslashes, no separator at the end.</summary>
        private static string RootKey(string installationRoot)
        {
            return installationRoot.Trim().Replace('/', '\\').TrimEnd('\\');
        }
    }
}
