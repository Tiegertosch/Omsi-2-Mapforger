using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Result of exporting a workspace as a separate OMSI map folder.</summary>
    public sealed class OmsiMapExportReport
    {
        public string TargetFolder { get; internal set; }
        public int CopiedFileCount { get; internal set; }
        public List<string> EditedFiles { get; } = new List<string>();
        public bool ReplacedPreviousExport { get; internal set; }

        /// <summary>True when the export went into the map's own folder, a map made with MapWorks (ADR 052).</summary>
        public bool InPlace { get; internal set; }
    }

    /// <summary>
    /// Writes a workspace as a new map folder that OMSI can load next to the original: the original files, the
    /// edited files on top, and a new [friendlyname]. It only replaces a folder that an earlier Mapforge export created,
    /// and writes into the original map folder only for a map made with MapWorks, which is the user's own (ADR 052).
    /// </summary>
    public static class OmsiMapExporter
    {
        /// <summary>Marker file that identifies a folder as a Mapforge export.</summary>
        public const string MarkerFileName = "mapforge-export.txt";

        /// <summary>The workspace folder of the project's own data, such as the ground shaped by hand; never exported.</summary>
        public const string ProjectDataFolder = "Mapforge";

        private const string TemporarySuffix = ".mapforge-export-tmp";

        private const string ReplacedSuffix = ".mapforge-export-old";

        /// <summary>
        /// Suggests where to export: a map made with MapWorks into its own folder (ADR 052), any other map into a sibling
        /// folder of the original, for example "Grundorf (MapWorks)".
        /// </summary>
        public static string SuggestTargetFolder(OmsiMapWorkspace workspace)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));
            if (OmsiNewMap.IsMadeHere(workspace.RootPath))
                return workspace.RootPath;
            var parent = Path.GetDirectoryName(workspace.RootPath);
            return Path.Combine(parent, Path.GetFileName(workspace.RootPath) + " (MapWorks)");
        }

        /// <summary>True when the export goes into the map's own folder, as only a map made with MapWorks may.</summary>
        public static bool IsInPlace(OmsiMapWorkspace workspace, string targetFolder)
        {
            return string.Equals(Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(workspace.RootPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when the folder does not exist yet, holds an earlier Mapforge export, or is the own folder of a map made
        /// with MapWorks.
        /// </summary>
        public static bool CanWriteTo(OmsiMapWorkspace workspace, string targetFolder, out string reason)
        {
            reason = null;
            var target = Path.GetFullPath(targetFolder);
            if (IsInPlace(workspace, target))
            {
                if (!OmsiNewMap.IsMadeHere(workspace.RootPath))
                    reason = "Only maps made with MapWorks are exported into their own folder; this map needs a folder of its own.";
                else if (OmsiRelativePath.IsSameOrInside(workspace.WorkspacePath, target))
                    reason = "The project workspace lies inside the map folder; save the project elsewhere first.";
            }
            else if (OmsiRelativePath.IsSameOrInside(target, workspace.RootPath) || OmsiRelativePath.IsSameOrInside(workspace.RootPath, target))
                reason = "The export folder must be separate from the original map folder.";
            else if (OmsiRelativePath.IsSameOrInside(target, workspace.WorkspacePath) || OmsiRelativePath.IsSameOrInside(workspace.WorkspacePath, target))
                reason = "The export folder must be separate from the project workspace.";
            else if (Directory.Exists(target) && !File.Exists(Path.Combine(target, MarkerFileName)))
                reason = "The folder already exists and was not created by a MapWorks export: " + target;
            return reason == null;
        }

        public static OmsiMapExportReport Export(OmsiMapWorkspace workspace, string targetFolder, string friendlyName)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));
            if (string.IsNullOrWhiteSpace(friendlyName))
                throw new ArgumentException("A display name for the exported map is required.", nameof(friendlyName));
            if (!CanWriteTo(workspace, targetFolder, out var reason))
                throw new InvalidOperationException(reason);

            var target = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar);
            var inPlace = IsInPlace(workspace, target);
            var temporary = target + TemporarySuffix;
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);

            var report = new OmsiMapExportReport { TargetFolder = target, InPlace = inPlace };
            try
            {
                CopyFolder(workspace.RootPath, temporary, report);
                foreach (var relative in workspace.ModifiedFiles())
                {
                    // The project's own data, such as the ground shaped by hand, is no part of the map.
                    if (relative.Replace('/', '\\').StartsWith(ProjectDataFolder + "\\", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var destination = Path.Combine(temporary, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.WriteAllBytes(destination, workspace.ReadAllBytes(relative));
                    report.EditedFiles.Add(relative);
                }

                SetFriendlyName(Path.Combine(temporary, "global.cfg"), friendlyName.Trim());
                if (inPlace)
                {
                    ReplaceOwnFolder(target, temporary);
                    // The map now holds the edits; the workspace keeps only the project's own data.
                    foreach (var relative in report.EditedFiles)
                        workspace.Revert(relative);
                    return report;
                }
                File.WriteAllText(Path.Combine(temporary, MarkerFileName),
                    "Exported by OMSI MapWorks on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    "Original map: " + workspace.RootPath + Environment.NewLine +
                    "Edited files: " + report.EditedFiles.Count + Environment.NewLine);

                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                    report.ReplacedPreviousExport = true;
                }
                Directory.Move(temporary, target);
            }
            catch
            {
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, true);
                throw;
            }

            return report;
        }

        /// <summary>
        /// Puts the map built beside its own folder in that folder's place: the old folder is moved aside first and only
        /// deleted once the new one stands; when the new one cannot be moved in, the old one goes back.
        /// </summary>
        private static void ReplaceOwnFolder(string target, string built)
        {
            var aside = target + ReplacedSuffix;
            if (Directory.Exists(aside))
                Directory.Delete(aside, true);
            Directory.Move(target, aside);
            try
            {
                Directory.Move(built, target);
            }
            catch
            {
                Directory.Move(aside, target);
                throw;
            }
            Directory.Delete(aside, true);
        }

        private static void CopyFolder(string source, string destination, OmsiMapExportReport report)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
                report.CopiedFileCount++;
            }
            foreach (var folder in Directory.GetDirectories(source))
                CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)), report);
        }

        private static void SetFriendlyName(string globalConfigPath, string friendlyName)
        {
            var config = OmsiRecordFile.Load(globalConfigPath, OmsiGlobalConfigLayouts.Instance);
            foreach (var record in config.Records)
            {
                if (string.Equals(record.Name, "friendlyname", StringComparison.OrdinalIgnoreCase) && record.Fields.Count == 1)
                {
                    record.SetField(0, friendlyName);
                    File.WriteAllBytes(globalConfigPath, config.ToBytes());
                    return;
                }
            }

            throw new InvalidDataException("global.cfg has no [friendlyname] section to rename the exported map.");
        }
    }
}
