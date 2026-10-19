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
    /// <summary>Validates and builds the initial Windows desktop player.</summary>
    public static class WindowsPlayerBuild
    {
        private const string StartupScenePath = "Assets/_Project/Scenes/EditorStartup.unity";
        private const string OutputPath = "Builds/Windows/OMSIMapEditor.exe";

        [MenuItem("OMSI Map Editor/Build/Windows Player")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (!scenes.Contains(StartupScenePath))
                throw new InvalidOperationException("The startup scene is missing from the enabled build scenes.");

            if (!OmsiContentBoundary.AreEditorToolsExcludedFromPlayer())
                throw new InvalidOperationException("Player build blocked: editor-only OMSI tools must remain excluded from the player.");

            var skippedSourcePaths = new List<string>();
            var copiedOmsiFile = OmsiContentBoundary.FindCopiedInstallFile(
                OmsiContentBoundary.GetPlayerAssetPaths(scenes), skippedSourcePaths);
            foreach (var skippedPath in skippedSourcePaths)
                Debug.LogWarning("OMSI content check could not read and skipped: " + skippedPath);
            if (!string.IsNullOrEmpty(copiedOmsiFile))
            {
                throw new InvalidOperationException("Player build blocked: a project asset is byte-identical to a file in the remembered OMSI installation: " + copiedOmsiFile);
            }

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

            Debug.Log("Windows player built: " + Path.GetFullPath(OutputPath));
        }
    }
}
