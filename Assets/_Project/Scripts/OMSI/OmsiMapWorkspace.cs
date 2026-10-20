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
            if (File.Exists(edited))
                File.Delete(edited);
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
