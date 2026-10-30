using System;
using System.IO;
using OmsiMapEditor.Core;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Maps made with MapWorks are the user's own (ADR 052): they open for editing straight away with a project MapWorks
    /// keeps for them under Documents\OMSI MapWorks\Projects, and their export goes back into their own folder. Maps of
    /// the installation keep the way of ADR 009: a project saved by hand and an export into a folder of its own.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>Where MapWorks keeps the projects of maps made with it.</summary>
        private static string OwnProjectsFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OMSI MapWorks", "Projects");
        }

        /// <summary>
        /// The project of a map made with MapWorks: a recently opened one that points to it, so edits made before keep
        /// showing, else the one in <see cref="OwnProjectsFolder"/>, else a new one there named after the map, with a number
        /// when that name is taken by another map.
        /// </summary>
        private static string OwnMapProject(string globalConfigPath, string name)
        {
            var map = Path.GetFullPath(globalConfigPath);
            bool PointsHere(string project)
            {
                var points = ProjectMap(project);
                return points != null && string.Equals(Path.GetFullPath(points), map, StringComparison.OrdinalIgnoreCase);
            }
            foreach (var entry in RecentFiles.Load())
                if (entry.IsProject && File.Exists(entry.Path) && PointsHere(entry.Path))
                    return entry.Path;
            var folder = OwnProjectsFolder();
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.GetFiles(folder, "*." + WindowsFileDialog.ProjectExtension))
                if (PointsHere(file))
                    return file;
            var baseName = SanitizeFileName(name);
            var path = Path.Combine(folder, baseName + "." + WindowsFileDialog.ProjectExtension);
            for (var number = 2; File.Exists(path) || Directory.Exists(OmsiMapWorkspace.FolderForProject(path)); number++)
                path = Path.Combine(folder, baseName + " (" + number + ")." + WindowsFileDialog.ProjectExtension);
            var project = new MapforgeProject { projectName = Path.GetFileNameWithoutExtension(path), globalConfigPath = map };
            File.WriteAllText(path, JsonUtility.ToJson(project, true), new System.Text.UTF8Encoding(false));
            return path;
        }

        /// <summary>Opens a map made with MapWorks with its project, ready to edit; false with the reason shown when it cannot.</summary>
        private bool OpenOwnMap(string globalConfigPath, string name)
        {
            string projectPath;
            try
            {
                projectPath = OwnMapProject(globalConfigPath, name);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("The project of this map could not be made in " + OwnProjectsFolder() + ": " + exception.Message);
                return false;
            }
            LoadMap(globalConfigPath, Path.GetFileNameWithoutExtension(projectPath), projectPath, false);
            RememberCurrent();
            return true;
        }

        /// <summary>True when the open map was made with MapWorks, so its export goes into its own folder.</summary>
        private bool ExportsInPlace()
        {
            return session?.Workspace != null && OmsiNewMap.IsMadeHere(session.Workspace.RootPath);
        }
    }
}
