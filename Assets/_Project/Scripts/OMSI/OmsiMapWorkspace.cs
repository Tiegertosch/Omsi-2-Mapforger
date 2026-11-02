using System;
using System.Collections.Generic;
using System.IO;

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

        public bool FileExists(string relativePath)
        {
            return File.Exists(WorkspaceFile(relativePath)) || File.Exists(OmsiRelativePath.Resolve(RootPath, relativePath));
        }

        public byte[] ReadAllBytes(string relativePath)
        {
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
            return File.Exists(WorkspaceFile(relativePath));
        }

        /// <summary>Relative paths of all edited files, sorted.</summary>
        public List<string> ModifiedFiles()
        {
            var result = new List<string>();
            if (!Directory.Exists(WorkspacePath))
                return result;

            foreach (var path in Directory.GetFiles(WorkspacePath, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".mapforge-tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(path.Substring(WorkspacePath.Length).TrimStart(Path.DirectorySeparatorChar));
            }

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
