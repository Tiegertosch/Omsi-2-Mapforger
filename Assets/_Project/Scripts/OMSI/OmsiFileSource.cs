using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Read access to the files of one OMSI map folder by relative path. The plain folder and the project workspace,
    /// which overlays edited files on the original map, both implement it.
    /// </summary>
    public interface IOmsiFileSource
    {
        /// <summary>Full path of the original map folder, for display and messages.</summary>
        string RootPath { get; }

        bool FileExists(string relativePath);

        byte[] ReadAllBytes(string relativePath);

        /// <summary>
        /// Relative paths of the files in a folder of the map ("" for the map folder itself), sorted and without
        /// duplicates. A missing folder yields no files.
        /// </summary>
        IReadOnlyList<string> EnumerateFiles(string relativeFolder, string searchPattern, bool recursive);
    }

    /// <summary>Reads a map folder directly.</summary>
    public sealed class OmsiFolderSource : IOmsiFileSource
    {
        public OmsiFolderSource(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("Select an OMSI map folder.", nameof(rootPath));
            RootPath = Path.GetFullPath(rootPath);
        }

        public string RootPath { get; }

        public bool FileExists(string relativePath)
        {
            return File.Exists(OmsiRelativePath.Resolve(RootPath, relativePath));
        }

        public byte[] ReadAllBytes(string relativePath)
        {
            return File.ReadAllBytes(OmsiRelativePath.Resolve(RootPath, relativePath));
        }

        public IReadOnlyList<string> EnumerateFiles(string relativeFolder, string searchPattern, bool recursive)
        {
            var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            OmsiRelativePath.AddFiles(RootPath, relativeFolder, searchPattern, recursive, result);
            return new List<string>(result);
        }
    }

    /// <summary>Normalizes the relative paths OMSI files use and keeps them inside their folder.</summary>
    public static class OmsiRelativePath
    {
        /// <summary>Converts OMSI's backslashes to the platform separator and rejects rooted or escaping paths.</summary>
        public static string Normalize(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("A relative file path is required.", nameof(relativePath));

            var normalized = relativePath.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
                throw new ArgumentException("Expected a path relative to the map folder: " + relativePath, nameof(relativePath));

            var parts = new List<string>();
            foreach (var part in normalized.Split(Path.DirectorySeparatorChar))
            {
                if (part.Length == 0 || part == ".")
                    continue;
                if (part == "..")
                    throw new ArgumentException("The path leaves the map folder: " + relativePath, nameof(relativePath));
                parts.Add(part);
            }

            if (parts.Count == 0)
                throw new ArgumentException("A relative file path is required.", nameof(relativePath));
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        /// <summary>Full path of a relative file inside a root folder.</summary>
        public static string Resolve(string rootPath, string relativePath)
        {
            return Path.Combine(rootPath, Normalize(relativePath));
        }

        /// <summary>Adds the relative paths of matching files below <paramref name="root"/> to <paramref name="result"/>.</summary>
        internal static void AddFiles(string root, string relativeFolder, string searchPattern, bool recursive, ISet<string> result)
        {
            var folder = string.IsNullOrEmpty(relativeFolder) ? root : Resolve(root, relativeFolder);
            if (!Directory.Exists(folder))
                return;
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var path in Directory.GetFiles(folder, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                result.Add(path.Substring(prefix.Length));
        }

        /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or lies inside it.</summary>
        public static bool IsSameOrInside(string path, string folder)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, fullFolder, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
