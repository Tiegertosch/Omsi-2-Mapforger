using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmsiMapEditor.OMSI.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Editor
{
    /// <summary>
    /// Validates and builds the Windows desktop player. Every build checks that no player file is a copy of an OMSI file;
    /// files that were not reviewed for OMSI content (ADR 005) are a warning in a normal build and stop a release build.
    /// </summary>
    public static class WindowsPlayerBuild
    {
        private const string StartupScenePath = "Assets/_Project/Scenes/EditorStartup.unity";
        private const string OutputPath = "Builds/Windows/OMSIMapEditor.exe";

        [MenuItem("OMSI Map Editor/Build/Windows Player")]
        public static void Build()
        {
            Build(false);
        }

        /// <summary>A player to hand out: every player file must be reviewed in its current form.</summary>
        [MenuItem("OMSI Map Editor/Build/Windows Player (Release)")]
        public static void BuildRelease()
        {
            Build(true);
        }

        /// <summary>Records the player files as reviewed, after a person checked that none of them is OMSI content.</summary>
        [MenuItem("OMSI Map Editor/Build/Record Reviewed Player Assets...")]
        public static void RecordReviewedPlayerAssets()
        {
            var paths = OmsiContentBoundary.GetPlayerAssetPaths(EnabledScenes()).ToList();
            var review = OmsiContentBoundary.ReviewPlayerAssets(paths);
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Record reviewed player assets",
                    (review.IsClean && review.RemovedFiles.Count == 0
                        ? "All player files are already reviewed."
                        : "Check these files for OMSI content, converted or not, before recording them:\n\n" + Shorten(review.ToString())),
                    "Record", "Cancel"))
                return;
            var count = OmsiContentBoundary.RecordReviewedPlayerAssets(paths);
            Debug.Log("Recorded " + count + " reviewed player files in " + OmsiContentBoundary.ReviewListPath +
                      (review.IsClean ? "." : ":\n" + review));
        }

        private static string Shorten(string text)
        {
            var lines = text.Split('\n');
            return lines.Length <= 25 ? text : string.Join("\n", lines.Take(25)) + "\n... and " + (lines.Length - 25) + " more";
        }

        private static string[] EnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private static void Build(bool release)
        {
            var scenes = EnabledScenes();
            if (!scenes.Contains(StartupScenePath))
                throw new InvalidOperationException("The startup scene is missing from the enabled build scenes.");

            if (!OmsiContentBoundary.AreEditorToolsExcludedFromPlayer())
                throw new InvalidOperationException("Player build blocked: editor-only OMSI tools must remain excluded from the player.");

            var playerAssetPaths = OmsiContentBoundary.GetPlayerAssetPaths(scenes).ToList();
            var skippedSourcePaths = new List<string>();
            var copiedOmsiFile = OmsiContentBoundary.FindCopiedInstallFile(playerAssetPaths, skippedSourcePaths);
            foreach (var skippedPath in skippedSourcePaths)
                Debug.LogWarning("OMSI content check could not read and skipped: " + skippedPath);
            if (!string.IsNullOrEmpty(copiedOmsiFile))
            {
                throw new InvalidOperationException("Player build blocked: a project asset is byte-identical to a file in the remembered OMSI installation: " + copiedOmsiFile);
            }

            var review = OmsiContentBoundary.ReviewPlayerAssets(playerAssetPaths);
            if (!review.IsClean)
            {
                var message = (review.NewFiles.Count + review.ChangedFiles.Count) + " player file(s) were not reviewed for OMSI content in their current form; " +
                              "check them and choose OMSI Map Editor > Build > Record Reviewed Player Assets:\n" + review;
                if (release)
                    throw new InvalidOperationException("Release build blocked: " + message);
                Debug.LogWarning(message);
            }
            else if (review.RemovedFiles.Count > 0)
                Debug.Log("Reviewed player files no longer used:\n" + review);

            var startupScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartupScenePath);
            if (startupScene == null)
            {
                throw new InvalidOperationException("The editor startup scene could not be imported.");
            }

            var shell = Resources.Load<VisualTreeAsset>("EditorShell");
            if (shell == null)
            {
                throw new InvalidOperationException("EditorShell.uxml was not imported as a UI Toolkit resource.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Windows player build failed: " + report.summary.result);
            }

            Debug.Log((release ? "Windows release player built: " : "Windows player built: ") + Path.GetFullPath(OutputPath));
        }
    }
}
