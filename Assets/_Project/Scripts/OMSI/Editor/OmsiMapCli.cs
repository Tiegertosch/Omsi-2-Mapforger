using System;
using OmsiMapEditor.OMSI;
using UnityEditor;
using UnityEngine;

namespace OmsiMapEditor.OMSI.Editor
{
    /// <summary>Unity batch-mode entry point for inspecting an existing map without changing it.</summary>
    public static class OmsiMapCli
    {
        [MenuItem("OMSI Map Editor/Import/Log Map Summary (CLI)")]
        public static void InspectFromEnvironment()
        {
            var configPath = Environment.GetEnvironmentVariable("OMSI_MAP_CONFIG");
            if (string.IsNullOrWhiteSpace(configPath))
                throw new InvalidOperationException("Set the OMSI_MAP_CONFIG environment variable to a map's global.cfg path.");

            var summary = OmsiMapReader.Read(configPath);
            OmsiContentBoundary.RememberSourcePath(summary.RootPath);
            Debug.Log("OMSI map read: " + summary.Name + " | tiles=" + summary.Tiles.Count +
                      " | objects=" + summary.ObjectCount + " | splines=" + summary.SplineCount +
                      " | attachments=" + summary.AttachmentCount + " | terrain=" + summary.TerrainTileCount + "/" + summary.Tiles.Count);
            foreach (var warning in summary.Warnings)
                Debug.LogWarning(warning);

            EditorApplication.Exit(summary.Warnings.Count == 0 ? 0 : 1);
        }
    }
}
