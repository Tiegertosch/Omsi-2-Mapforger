using System;
using System.IO;
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

        /// <summary>Batch mode: reads and re-serializes every text file next to OMSI_MAP_CONFIG in memory.</summary>
        public static void VerifyRoundTripFromEnvironment()
        {
            var configPath = Environment.GetEnvironmentVariable("OMSI_MAP_CONFIG");
            if (string.IsNullOrWhiteSpace(configPath))
                throw new InvalidOperationException("Set the OMSI_MAP_CONFIG environment variable to a map's global.cfg path.");

            var report = OmsiRoundTripCheck.Run(Path.GetDirectoryName(Path.GetFullPath(configPath)));
            LogRoundTrip(report);
            EditorApplication.Exit(report.Passed ? 0 : 1);
        }

        [MenuItem("OMSI Map Editor/Import/Verify Lossless Round Trip...")]
        private static void VerifyRoundTripFromMenu()
        {
            var folder = EditorUtility.OpenFolderPanel("Select an OMSI map folder", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(folder))
                return;

            var report = OmsiRoundTripCheck.Run(folder);
            LogRoundTrip(report);
            EditorUtility.DisplayDialog("Lossless round trip",
                (report.Passed ? "Passed: " : "Failed: ") + report.ExactCount + " of " + report.FileCount +
                " text files are reproduced byte for byte. Nothing was written.", "OK");
        }

        private static void LogRoundTrip(OmsiRoundTripReport report)
        {
            Debug.Log("OMSI round trip: files=" + report.FileCount + " | byte-identical=" + report.ExactCount +
                      " | tile records=" + report.TileRecordCount + " | without layout=" + report.TileRecordsWithoutLayout +
                      " | incomplete=" + report.IncompleteTileRecords);
            foreach (var path in report.FilesNotExact)
                Debug.LogWarning("Not reproduced exactly: " + path);
            foreach (var problem in report.UnreadableFiles)
                Debug.LogWarning("Unreadable: " + problem);
        }
    }
}
