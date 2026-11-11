using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A project's working copy of an OMSI map. Edited files are saved into the workspace folder and read from there;
    /// every other file is read from the original map, which is never written. See ADR 009.
    /// </summary>
    public sealed class OmsiMapWorkspace : IOmsiFileSource
    {
        // Counts the saves and reverts of each file, as file times may not tell two quick saves apart.
        private readonly Dictionary<string, long> versions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private long lastVersion;
        // Files of the original map this project takes away (a deleted line): listed in the project data folder, the original stays as it is.
        private HashSet<string> deleted;

        public OmsiMapWorkspace(string sourceMapFolder, string workspaceFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceMapFolder) || !Directory.Exists(sourceMapFolder))
                throw new DirectoryNotFoundException("The original map folder does not exist: " + sourceMapFolder);
            if (string.IsNullOrWhiteSpace(workspaceFolder))
                throw new ArgumentException("A workspace folder is required.", nameof(workspaceFolder));

            RootPath = Path.GetFullPath(sourceMapFolder);
            WorkspacePath = Path.GetFullPath(workspaceFolder);
            if (OmsiRelativePath.IsSameOrInside(WorkspacePath, RootPath) || OmsiRelativePath.IsSameOrInside(RootPath, WorkspacePath))
                throw new ArgumentException("The workspace must be outside the original map folder and must not contain it.", nameof(workspaceFolder));
        }

        /// <summary>The original map folder; read only.</summary>
        public string RootPath { get; }

        /// <summary>The project's folder holding edited files.</summary>
        public string WorkspacePath { get; }

        /// <summary>Workspace folder for a project file: <c>Name.omproj</c> uses <c>Name.workspace</c> next to it.</summary>
        public static string FolderForProject(string projectFilePath)
        {
            var fullPath = Path.GetFullPath(projectFilePath);
            return Path.Combine(Path.GetDirectoryName(fullPath), Path.GetFileNameWithoutExtension(fullPath) + ".workspace");
        }

        private string DeletedListPath => Path.Combine(WorkspacePath, OmsiMapExporter.ProjectDataFolder, "deleted.txt");

        private HashSet<string> Deleted()
        {
            if (deleted != null)
                return deleted;
            deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(DeletedListPath))
                foreach (var line in File.ReadAllLines(DeletedListPath))
                    if (line.Trim().Length > 0)
                        deleted.Add(OmsiRelativePath.Normalize(line));
            return deleted;
        }

        private void SaveDeleted()
        {
            var path = DeletedListPath;
            if (Deleted().Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, Deleted().OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>True when the project takes this file of the original map away.</summary>
        public bool IsDeleted(string relativePath)
        {
            return Deleted().Contains(OmsiRelativePath.Normalize(relativePath));
        }

        /// <summary>Relative paths of the original map's files this project takes away, sorted.</summary>
        public List<string> DeletedFiles()
        {
            return Deleted().OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Takes a file away: an edited or made copy goes, and a file the original map has is no longer read or exported. The original is not touched.
        /// A later <see cref="Save(string, byte[])"/> brings the file back.
        /// </summary>
        public void Delete(string relativePath)
        {
            var edited = WorkspaceFile(relativePath);
            Count(edited);
            if (File.Exists(edited))
                File.Delete(edited);
            if (File.Exists(OmsiRelativePath.Resolve(RootPath, relativePath)) && Deleted().Add(OmsiRelativePath.Normalize(relativePath)))
                SaveDeleted();
        }

        private void Restore(string relativePath)
        {
            if (Deleted().Remove(OmsiRelativePath.Normalize(relativePath)))
                SaveDeleted();
        }

        public bool FileExists(string relativePath)
        {
            if (IsDeleted(relativePath))
                return false;
            return File.Exists(WorkspaceFile(relativePath)) || File.Exists(OmsiRelativePath.Resolve(RootPath, relativePath));
        }

        public byte[] ReadAllBytes(string relativePath)
        {
            if (IsDeleted(relativePath))
                throw new FileNotFoundException("The project took this file away.", relativePath);
            var edited = WorkspaceFile(relativePath);
            return File.ReadAllBytes(File.Exists(edited) ? edited : OmsiRelativePath.Resolve(RootPath, relativePath));
        }

        /// <summary>Files of the original map and the workspace together; edited copies are listed once.</summary>
        public IReadOnlyList<string> EnumerateFiles(string relativeFolder, string searchPattern, bool recursive)
        {
            var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            OmsiRelativePath.AddFiles(RootPath, relativeFolder, searchPattern, recursive, result);
            OmsiRelativePath.AddFiles(WorkspacePath, relativeFolder, searchPattern, recursive, result);
            result.RemoveWhere(path => path.EndsWith(".mapforge-tmp", StringComparison.OrdinalIgnoreCase));
            if (Deleted().Count > 0)
                result.RemoveWhere(path => Deleted().Contains(OmsiRelativePath.Normalize(path)));
            return new List<string>(result);
        }

        /// <summary>A file as the original map has it, whatever the workspace holds; null when the original has no such file.</summary>
        public byte[] ReadOriginalBytes(string relativePath)
        {
            var original = OmsiRelativePath.Resolve(RootPath, relativePath);
            return File.Exists(original) ? File.ReadAllBytes(original) : null;
        }

        /// <summary>
        /// Text that changes whenever the file read for a path changes: by a save or revert through this workspace, or on
        /// disk in size or time. Null when there is no such file. Lets readers keep what they read from unchanged files.
        /// </summary>
        public string Stamp(string relativePath)
        {
            if (IsDeleted(relativePath))
                return null;
            var edited = WorkspaceFile(relativePath);
            var inWorkspace = File.Exists(edited);
            var file = new FileInfo(inWorkspace ? edited : OmsiRelativePath.Resolve(RootPath, relativePath));
            if (!file.Exists)
                return null;
            long version;
            lock (versions)
                versions.TryGetValue(edited, out version);
            return (inWorkspace ? "w" : "o") + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public bool IsModified(string relativePath)
        {
            return File.Exists(WorkspaceFile(relativePath)) || IsDeleted(relativePath);
        }

        /// <summary>Relative paths of all edited files, sorted.</summary>
        public List<string> ModifiedFiles()
        {
            var result = new List<string>();
            if (!Directory.Exists(WorkspacePath))
                return result;

            foreach (var path in Directory.GetFiles(WorkspacePath, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".mapforge-tmp", StringComparison.OrdinalIgnoreCase) || string.Equals(path, DeletedListPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(path.Substring(WorkspacePath.Length).TrimStart(Path.DirectorySeparatorChar));
            }
            result.AddRange(Deleted().Where(path => !result.Contains(path, StringComparer.OrdinalIgnoreCase)));

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// Saves an edited file into the workspace. The original map is not touched. Content identical to the
        /// original removes the workspace copy instead, so undoing every edit leaves nothing behind.
        /// </summary>
        public void Save(string relativePath, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            Restore(relativePath);
            var original = OmsiRelativePath.Resolve(RootPath, relativePath);
            if (File.Exists(original) && SameBytes(File.ReadAllBytes(original), bytes))
            {
                Revert(relativePath);
                return;
            }

            var target = WorkspaceFile(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            // Write next to the target first so an interrupted save never leaves a half-written file.
            var temporary = target + ".mapforge-tmp";
            File.WriteAllBytes(temporary, bytes);
            Count(target);
            if (File.Exists(target))
                File.Delete(target);
            File.Move(temporary, target);
        }

        public void Save(string relativePath, OmsiRecordFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            Save(relativePath, file.ToBytes());
        }

        /// <summary>Discards the edited copy of a file, so the original is read again.</summary>
        public void Revert(string relativePath)
        {
            var edited = WorkspaceFile(relativePath);
            Count(edited);
            Restore(relativePath);
            if (File.Exists(edited))
                File.Delete(edited);
        }

        private void Count(string workspaceFile)
        {
            lock (versions)
                versions[workspaceFile] = ++lastVersion;
        }

        private static bool SameBytes(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (var index = 0; index < first.Length; index++)
                if (first[index] != second[index])
                    return false;
            return true;
        }

        private string WorkspaceFile(string relativePath)
        {
            return OmsiRelativePath.Resolve(WorkspacePath, relativePath);
        }
    }
}
