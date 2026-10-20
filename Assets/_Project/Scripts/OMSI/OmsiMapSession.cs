using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// An opened map: read-only when opened directly, editable through the project workspace when opened as a
    /// project. Edits are saved into the workspace immediately (ADR 009).
    /// </summary>
    public sealed class OmsiMapSession
    {
        private OmsiMapSession(string globalConfigPath, IOmsiFileSource source, OmsiMapWorkspace workspace, string projectFilePath)
        {
            GlobalConfigPath = globalConfigPath;
            Source = source;
            Workspace = workspace;
            ProjectFilePath = projectFilePath;
        }

        /// <summary>The original map's global.cfg.</summary>
        public string GlobalConfigPath { get; }

        public IOmsiFileSource Source { get; }

        /// <summary>The project's working copy, or null when the map is open read-only.</summary>
        public OmsiMapWorkspace Workspace { get; }

        /// <summary>The .omproj file, or null when the map is open without a project.</summary>
        public string ProjectFilePath { get; }

        public bool CanEdit => Workspace != null;

        private string GlobalConfigFileName => Path.GetFileName(GlobalConfigPath);

        public static OmsiMapSession OpenReadOnly(string globalConfigPath)
        {
            var configPath = CheckConfig(globalConfigPath);
            return new OmsiMapSession(configPath, new OmsiFolderSource(Path.GetDirectoryName(configPath)), null, null);
        }

        /// <summary>Opens the map with the workspace that belongs to the project file (<c>Name.workspace</c> next to it).</summary>
        public static OmsiMapSession OpenProject(string globalConfigPath, string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(projectFilePath))
                throw new ArgumentException("A project file path is required.", nameof(projectFilePath));
            var configPath = CheckConfig(globalConfigPath);
            var projectPath = Path.GetFullPath(projectFilePath);
            var workspace = new OmsiMapWorkspace(Path.GetDirectoryName(configPath), OmsiMapWorkspace.FolderForProject(projectPath));
            return new OmsiMapSession(configPath, workspace, workspace, projectPath);
        }

        public OmsiMapSummary Read()
        {
            return OmsiMapReader.Read(Source, GlobalConfigFileName);
        }

        public bool IsTileModified(string tileFileName)
        {
            return Workspace != null && Workspace.IsModified(OmsiRelativePath.Normalize(tileFileName));
        }

        public IReadOnlyList<string> ModifiedFiles()
        {
            return Workspace != null ? (IReadOnlyList<string>)Workspace.ModifiedFiles() : new string[0];
        }

        /// <summary>
        /// Sets an object's position and heading and saves the tile into the workspace. Values equal to the stored
        /// ones keep their original text. Returns false when nothing changed.
        /// </summary>
        public bool UpdateObject(string tileFileName, int objectId, double x, double y, double z, double headingDegrees)
        {
            var workspace = RequireWorkspace();
            var relative = OmsiRelativePath.Normalize(tileFileName);
            var tile = OmsiRecordFile.Parse(workspace.ReadAllBytes(relative), OmsiTileRecordLayouts.Instance);
            OmsiObjectView target = null;
            foreach (var view in OmsiTileRecords.Of<OmsiObjectView>(tile))
            {
                if (view.Id == objectId)
                {
                    target = view;
                    break;
                }
            }
            if (target == null)
                throw new InvalidOperationException("Object " + objectId + " was not found in " + tileFileName + ".");

            var changed = false;
            if (target.X != x) { target.X = x; changed = true; }
            if (target.Y != y) { target.Y = y; changed = true; }
            if (target.Z != z) { target.Z = z; changed = true; }
            if (target.HeadingDegrees != headingDegrees) { target.HeadingDegrees = headingDegrees; changed = true; }
            if (changed)
                workspace.Save(relative, tile);
            return changed;
        }

        /// <summary>
        /// Same as <see cref="UpdateObject(string,int,double,double,double,double)"/> and also updates the object in a
        /// summary read from this session, so the map does not have to be read again. The record receives the values
        /// as written to the file.
        /// </summary>
        public bool UpdateObject(OmsiTileSummary tile, OmsiObjectRecord record, double x, double y, double z, double headingDegrees)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            if (!UpdateObject(tile.FileName, record.Id, x, y, z, headingDegrees))
                return false;
            record.LocalX = AsWritten(x);
            record.LocalY = AsWritten(y);
            record.Height = AsWritten(z);
            record.HeadingDegrees = AsWritten(headingDegrees);
            return true;
        }

        private static double AsWritten(double value)
        {
            return double.Parse(OmsiNumberFormat.Format(value), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Discards the workspace copy of a tile, so the original is shown again.</summary>
        public void RevertTile(string tileFileName)
        {
            RequireWorkspace().Revert(OmsiRelativePath.Normalize(tileFileName));
        }

        private OmsiMapWorkspace RequireWorkspace()
        {
            if (Workspace == null)
                throw new InvalidOperationException("Save a Mapforge project before editing; the map is open read-only.");
            return Workspace;
        }

        private static string CheckConfig(string globalConfigPath)
        {
            if (string.IsNullOrWhiteSpace(globalConfigPath))
                throw new ArgumentException("Select an OMSI global.cfg file.", nameof(globalConfigPath));
            var configPath = Path.GetFullPath(globalConfigPath);
            if (!File.Exists(configPath))
                throw new FileNotFoundException("The selected global.cfg file does not exist.", configPath);
            return configPath;
        }
    }
}
