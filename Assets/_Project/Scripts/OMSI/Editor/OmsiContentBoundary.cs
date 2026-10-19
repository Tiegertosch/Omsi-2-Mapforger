using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OmsiMapEditor.OMSI.Editor
{
    /// <summary>Remembers the external map source location and blocks byte-identical source files from player builds.</summary>
    public static class OmsiContentBoundary
    {
        private const string InstallRootPreference = "OmsiMapEditor.ReadOnlySourceInstallRoot";
        private const string EditorToolsDefinitionPath = "Assets/_Project/Scripts/OMSI/Editor/Editor.OMSI.Editor.asmdef";

        [Serializable]
        private sealed class AssemblyDefinitionInfo
        {
            public string[] includePlatforms;
        }

        public static void RememberSourcePath(string mapFolder)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(mapFolder));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Omsi.exe")))
                {
                    EditorPrefs.SetString(InstallRootPreference, directory.FullName);
                    return;
                }
                directory = directory.Parent;
            }
        }

        /// <summary>Returns a project asset that is byte-identical to a file in the remembered OMSI install, if any.</summary>
        /// <param name="playerAssetPaths">Project-relative asset paths that can enter the player.</param>
        /// <param name="skippedPaths">Receives install folders or files that could not be read and were not compared.</param>
        public static string FindCopiedInstallFile(IEnumerable<string> playerAssetPaths, List<string> skippedPaths)
        {
            var installRoot = EditorPrefs.GetString(InstallRootPreference, string.Empty);
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                return string.Empty;

            var assetsBySize = new Dictionary<long, List<string>>();
            foreach (var assetPath in playerAssetPaths)
            {
                var normalizedPath = assetPath.Replace('\\', '/');
                if (!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    AssetDatabase.IsValidFolder(normalizedPath) || IsCodeOrMetadata(normalizedPath))
                    continue;

                var fullPath = Path.GetFullPath(normalizedPath);
                if (!File.Exists(fullPath))
                    continue;

                var length = new FileInfo(fullPath).Length;
                if (!assetsBySize.TryGetValue(length, out var paths))
                {
                    paths = new List<string>();
                    assetsBySize.Add(length, paths);
                }
                paths.Add(fullPath);
            }

            if (assetsBySize.Count == 0)
                return string.Empty;

            // Only install files whose size matches a player asset are hashed; each asset is hashed at most once.
            var assetHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourceFile in EnumerateFiles(installRoot, skippedPaths))
            {
                if (!assetsBySize.TryGetValue(sourceFile.Length, out var candidates))
                    continue;

                string sourceHash;
                try
                {
                    sourceHash = HashFile(sourceFile.FullName);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    skippedPaths?.Add(sourceFile.FullName);
                    continue;
                }

                foreach (var assetPath in candidates)
                {
                    if (!assetHashes.TryGetValue(assetPath, out var assetHash))
                    {
                        assetHash = HashFile(assetPath);
                        assetHashes.Add(assetPath, assetHash);
                    }

                    if (string.Equals(assetHash, sourceHash, StringComparison.Ordinal))
                        return assetPath;
                }
            }

            return string.Empty;
        }

        /// <summary>Returns project assets that can enter a player through scenes, runtime data folders, or render settings.</summary>
        public static IEnumerable<string> GetPlayerAssetPaths(IEnumerable<string> buildScenes)
        {
            var playerAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scenes = buildScenes == null ? Array.Empty<string>() : buildScenes.ToArray();
            if (scenes.Length > 0)
                foreach (var assetPath in AssetDatabase.GetDependencies(scenes, true))
                    playerAssets.Add(assetPath);

            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                var normalizedPath = assetPath.Replace('\\', '/');
                if (IsPlayerDataFolder(normalizedPath))
                    AddDependencies(normalizedPath, playerAssets);
            }

            AddDependencies(AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline), playerAssets);
            AddDependencies(AssetDatabase.GetAssetPath(QualitySettings.renderPipeline), playerAssets);
            foreach (var settingsAsset in new[]
                     {
                         "Assets/DefaultVolumeProfile.asset",
                         "Assets/UniversalRenderPipelineGlobalSettings.asset",
                         "Assets/_Project/Settings/EditorPipeline.asset",
                         "Assets/_Project/Settings/EditorRenderer.asset"
                     })
                AddDependencies(settingsAsset, playerAssets);

            return playerAssets;
        }

        /// <summary>
        /// True when the editor-only OMSI tools assembly (overview window, batch entry point, build checks)
        /// targets the Editor platform only. The platform-neutral reader in Editor.OMSI is allowed in the player.
        /// </summary>
        public static bool AreEditorToolsExcludedFromPlayer()
        {
            if (!File.Exists(EditorToolsDefinitionPath))
                return false;

            var definition = JsonUtility.FromJson<AssemblyDefinitionInfo>(File.ReadAllText(EditorToolsDefinitionPath));
            return definition != null && definition.includePlatforms != null &&
                   definition.includePlatforms.Length == 1 && definition.includePlatforms[0] == "Editor";
        }

        private static IEnumerable<FileInfo> EnumerateFiles(string root, List<string> skippedPaths)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                FileInfo[] files;
                DirectoryInfo[] subdirectories;
                try
                {
                    files = directory.GetFiles();
                    subdirectories = directory.GetDirectories();
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    skippedPaths?.Add(directory.FullName);
                    continue;
                }

                foreach (var file in files)
                    yield return file;
                foreach (var subdirectory in subdirectories)
                    if ((subdirectory.Attributes & FileAttributes.ReparsePoint) == 0)
                        pending.Push(subdirectory);
            }
        }

        private static void AddDependencies(string assetPath, HashSet<string> results)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
                return;
            foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
                results.Add(dependency);
        }

        private static bool IsPlayerDataFolder(string assetPath)
        {
            return assetPath.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   assetPath.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.IndexOf("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCodeOrMetadata(string assetPath)
        {
            var extension = Path.GetExtension(assetPath);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static string HashFile(string path)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
