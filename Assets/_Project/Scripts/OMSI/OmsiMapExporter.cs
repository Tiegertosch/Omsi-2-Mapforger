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
    }

    /// <summary>
    /// Writes a workspace as a new map folder that OMSI can load next to the original: the original files, the
    /// edited files on top, and a new [friendlyname]. It never writes into the original map folder and only
    /// replaces a folder that an earlier Mapforge export created.
    /// </summary>
    public static class OmsiMapExporter
    {
        /// <summary>Marker file that identifies a folder as a Mapforge export.</summary>
        public const string MarkerFileName = "mapforge-export.txt";

        private const string TemporarySuffix = ".mapforge-export-tmp";

        /// <summary>Suggests a sibling folder of the original map, for example "Grundorf (Mapforge)".</summary>
        public static string SuggestTargetFolder(OmsiMapWorkspace workspace)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));
            var parent = Path.GetDirectoryName(workspace.RootPath);
            return Path.Combine(parent, Path.GetFileName(workspace.RootPath) + " (Mapforge)");
        }

        /// <summary>True when the folder does not exist yet or holds an earlier Mapforge export.</summary>
        public static bool CanWriteTo(OmsiMapWorkspace workspace, string targetFolder, out string reason)
        {
            reason = null;
            var target = Path.GetFullPath(targetFolder);
            if (OmsiRelativePath.IsSameOrInside(target, workspace.RootPath) || OmsiRelativePath.IsSameOrInside(workspace.RootPath, target))
                reason = "The export folder must be separate from the original map folder.";
            else if (OmsiRelativePath.IsSameOrInside(target, workspace.WorkspacePath) || OmsiRelativePath.IsSameOrInside(workspace.WorkspacePath, target))
                reason = "The export folder must be separate from the project workspace.";
            else if (Directory.Exists(target) && !File.Exists(Path.Combine(target, MarkerFileName)))
                reason = "The folder already exists and was not created by a Mapforge export: " + target;
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
            var temporary = target + TemporarySuffix;
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);

            var report = new OmsiMapExportReport { TargetFolder = target };
            try
            {
                CopyFolder(workspace.RootPath, temporary, report);
                foreach (var relative in workspace.ModifiedFiles())
                {
                    var destination = Path.Combine(temporary, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.WriteAllBytes(destination, workspace.ReadAllBytes(relative));
                    report.EditedFiles.Add(relative);
                }

                SetFriendlyName(Path.Combine(temporary, "global.cfg"), friendlyName.Trim());
                File.WriteAllText(Path.Combine(temporary, MarkerFileName),
                    "Exported by OMSI Mapforge on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
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
