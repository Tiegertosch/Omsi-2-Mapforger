using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>An object folder the editor made under Sceneryobjects\Mapforge\&lt;project&gt; (ADR 026).</summary>
    public sealed class OmsiMadeObject
    {
        /// <summary>The folder, as a full path.</summary>
        public string FolderPath { get; internal set; }

        public string Name => Path.GetFileName(FolderPath);

        public long Bytes { get; internal set; }

        /// <summary>A map file or other place that still names the object, or null when nothing does.</summary>
        public string UsedBy { get; internal set; }

        public bool Used => UsedBy != null;
    }

    /// <summary>The objects made for one project and whether anything still names them.</summary>
    public sealed class OmsiMadeObjectScan
    {
        /// <summary>Sceneryobjects\Mapforge\&lt;project&gt; of the installation, as a full path.</summary>
        public string ProjectFolder { get; internal set; }

        public List<OmsiMadeObject> Objects { get; } = new List<OmsiMadeObject>();

        /// <summary>How many map files were read.</summary>
        public int ScannedFiles { get; internal set; }

        public List<OmsiMadeObject> Unused => Objects.FindAll(item => !item.Used);

        public long UnusedBytes
        {
            get
            {
                var bytes = 0L;
                foreach (var item in Unused)
                    bytes += item.Bytes;
                return bytes;
            }
        }
    }

    /// <summary>
    /// Finds and deletes objects the editor made for a project that no map names any more, such as junctions made again
    /// after an arm turned (ADR 035): those stay behind, as undo needs them and made files are never overwritten (ADR 026).
    /// An object counts as used while a tile file of any map of the installation names it, with exports and chrono
    /// variants, or one in the project's workspace, or something the session still holds, such as its undo history.
    /// </summary>
    public static class OmsiMadeObjects
    {
        /// <param name="workspaceFolder">The project's folder of edited map files, or null.</param>
        /// <param name="keep">
        /// Object files the session still needs, relative to the installation, or texts naming them, such as tile files an
        /// undo would bring back.
        /// </param>
        public static OmsiMadeObjectScan Find(string installationRoot, string projectName, string workspaceFolder, IEnumerable<string> keep)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            var root = Path.GetFullPath(installationRoot);
            var project = OmsiGeneratedObjectStore.SafeName(projectName, "Project");
            var scan = new OmsiMadeObjectScan { ProjectFolder = Path.Combine(root, OmsiGeneratedObjectStore.Folder, project) };
            if (!Directory.Exists(scan.ProjectFolder))
                return scan;

            // How a tile file names a file in a folder: its path from the installation, any case, either slash.
            var marks = new Dictionary<OmsiMadeObject, string>();
            foreach (var folder in Directory.GetDirectories(scan.ProjectFolder))
            {
                var item = new OmsiMadeObject { FolderPath = folder, Bytes = SizeOf(folder) };
                scan.Objects.Add(item);
                marks[item] = Mark(OmsiGeneratedObjectStore.Folder + "\\" + project + "\\" + item.Name + "\\");
            }
            if (keep != null)
                foreach (var path in keep)
                {
                    var kept = Mark(path ?? string.Empty);
                    foreach (var pair in marks)
                        if (pair.Key.UsedBy == null && kept.IndexOf(pair.Value, StringComparison.Ordinal) >= 0)
                            pair.Key.UsedBy = "the open project's undo history or placement";
                }

            var folders = new List<string> { Path.Combine(root, "maps") };
            if (!string.IsNullOrWhiteSpace(workspaceFolder))
                folders.Add(workspaceFolder);
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*.map", SearchOption.AllDirectories))
                {
                    if (scan.Objects.TrueForAll(item => item.Used))
                        return scan;
                    string text;
                    try
                    {
                        text = OmsiTextFile.Decode(File.ReadAllBytes(file));
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        // A file that cannot be read may name anything: nothing counts as unused then.
                        foreach (var item in scan.Objects)
                            item.UsedBy = item.UsedBy ?? "possibly " + file + ", which could not be read";
                        return scan;
                    }
                    scan.ScannedFiles++;
                    if (text.IndexOf("mapforge", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var named = Mark(text);
                    foreach (var pair in marks)
                        if (pair.Key.UsedBy == null && named.IndexOf(pair.Value, StringComparison.Ordinal) >= 0)
                            pair.Key.UsedBy = file;
                }
            }
            return scan;
        }

        /// <summary>
        /// Deletes the unused folders of a scan, only ever inside its project folder. Returns the names of those deleted;
        /// folders that could not be deleted are named in <paramref name="problems"/>.
        /// </summary>
        public static List<string> DeleteUnused(OmsiMadeObjectScan scan, List<string> problems)
        {
            if (scan == null)
                throw new ArgumentNullException(nameof(scan));
            var deleted = new List<string>();
            var inside = Path.GetFullPath(scan.ProjectFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var item in scan.Unused)
            {
                var folder = Path.GetFullPath(item.FolderPath);
                if (!folder.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(folder + " lies outside " + scan.ProjectFolder + ".");
                try
                {
                    Directory.Delete(folder, true);
                    deleted.Add(item.Name);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    problems?.Add(item.Name + ": " + exception.Message);
                }
            }
            return deleted;
        }

        private static string Mark(string text)
        {
            return text.Replace('/', '\\').ToLowerInvariant();
        }

        private static long SizeOf(string folder)
        {
            var bytes = 0L;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(file).Length;
            return bytes;
        }
    }
}
