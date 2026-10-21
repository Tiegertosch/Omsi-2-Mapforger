using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A placed or deleted object. It keeps the bytes of every file it changed, before and after, so undo and redo
    /// restore exactly what the change wrote, including rules and variables that were removed with an object.
    /// </summary>
    public sealed class OmsiObjectChange
    {
        internal sealed class FileState
        {
            public string RelativePath;
            public byte[] Before;
            public byte[] After;
        }

        internal OmsiObjectChange(OmsiTileSummary tile, OmsiObjectRecord record, bool isPlacement, int listIndex)
        {
            Tile = tile;
            Record = record;
            IsPlacement = isPlacement;
            ListIndex = listIndex;
        }

        public OmsiTileSummary Tile { get; }
        public OmsiObjectRecord Record { get; }

        /// <summary>True for a new object, false for a deleted one.</summary>
        public bool IsPlacement { get; }

        /// <summary>True while the change is in effect, false after it was undone.</summary>
        public bool IsApplied { get; internal set; } = true;

        /// <summary>The record's position in <see cref="OmsiTileSummary.Objects"/> while it exists.</summary>
        internal int ListIndex { get; }

        internal List<FileState> Files { get; } = new List<FileState>();
    }

    /// <summary>Outcome of deleting an object: the change, or the references that prevented it.</summary>
    public sealed class OmsiObjectDeletion
    {
        internal OmsiObjectDeletion(OmsiObjectChange change, IReadOnlyList<OmsiReference> blockers)
        {
            Change = change;
            Blockers = blockers;
        }

        /// <summary>The applied change, or null when the object was not deleted.</summary>
        public OmsiObjectChange Change { get; }

        /// <summary>References that still name the object; the files were left unchanged.</summary>
        public IReadOnlyList<OmsiReference> Blockers { get; }

        public bool Deleted => Change != null;
    }

    /// <summary>
    /// An opened map: read-only when opened directly, editable through the project workspace when opened as a
    /// project. Edits are saved into the workspace immediately (ADR 009).
    /// </summary>
    public sealed class OmsiMapSession
    {
        // Read once on the first placement; it remembers every ID handed out since, also of undone placements.
        private OmsiIdAllocator ids;

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

        /// <summary>
        /// Appends a new object to a tile with a new map-wide ID, raises [NextIDCode] in global.cfg and adds the
        /// object to the summary. Existing indices do not change, so nothing else has to be updated.
        /// </summary>
        public OmsiObjectChange PlaceObject(OmsiMapSummary summary, OmsiTileSummary tile, string assetPath,
            double x, double y, double z, double headingDegrees)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (!tile.IsAvailable)
                throw new InvalidOperationException("The tile file " + tile.FileName + " is missing.");

            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var tileBefore = workspace.ReadAllBytes(tilePath);
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var tileFile = OmsiRecordFile.Parse(tileBefore, OmsiTileRecordLayouts.Instance);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);

            ids = ids ?? OmsiIdAllocator.ForMap(workspace, GlobalConfigFileName);
            var view = OmsiTileEditing.AppendObject(tileFile, ids, assetPath, x, y, z, headingDegrees);
            ids.ApplyTo(config);

            var record = new OmsiObjectRecord
            {
                AssetPath = view.AssetPath,
                Id = view.Id,
                LocalX = view.X,
                LocalY = view.Y,
                Height = view.Z,
                HeadingDegrees = view.HeadingDegrees
            };
            var change = new OmsiObjectChange(tile, record, true, tile.Objects.Count);
            AddFile(change, tilePath, tileBefore, tileFile.ToBytes());
            AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
            WriteFiles(change, false);
            AddToSummary(summary, change);
            summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, record.Id);
            summary.NextIdCode = ids.NextFreeId;
            return change;
        }

        /// <summary>
        /// Deletes an object with the rules and variables that follow it and keeps the entry point indices of
        /// global.cfg valid. Nothing changes while tiles, chrono patches, entry points or timetables still refer to the
        /// object; those references are returned instead.
        /// </summary>
        public OmsiObjectDeletion DeleteObject(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var tileBefore = workspace.ReadAllBytes(tilePath);
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var tileFile = OmsiRecordFile.Parse(tileBefore, OmsiTileRecordLayouts.Instance);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            var view = FindObject(tileFile, record.Id, tile.FileName);

            var references = OmsiReferenceIndex.Build(workspace, GlobalConfigFileName);
            var result = OmsiTileEditing.Remove(tileFile, view, TileIndexOf(config, tilePath), config, references);
            if (!result.Removed)
                return new OmsiObjectDeletion(null, result.Blockers);

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(tile, record, false, listIndex < 0 ? tile.Objects.Count : listIndex);
            AddFile(change, tilePath, tileBefore, tileFile.ToBytes());
            if (result.ShiftedEntryPointIndices > 0)
                AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
            WriteFiles(change, false);
            RemoveFromSummary(summary, change);
            return new OmsiObjectDeletion(change, new OmsiReference[0]);
        }

        /// <summary>
        /// Takes back a placement or deletion: writes the files as they were before and updates the summary. Fails
        /// without writing anything when a file was changed since, so later edits are never overwritten.
        /// </summary>
        public void Undo(OmsiMapSummary summary, OmsiObjectChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (!change.IsApplied)
                throw new InvalidOperationException("The change has already been undone.");
            WriteFiles(change, true);
            if (change.IsPlacement)
                RemoveFromSummary(summary, change);
            else
                AddToSummary(summary, change);
            change.IsApplied = false;
        }

        /// <summary>Applies an undone placement or deletion again.</summary>
        public void Redo(OmsiMapSummary summary, OmsiObjectChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (change.IsApplied)
                throw new InvalidOperationException("The change is already applied.");
            WriteFiles(change, false);
            if (change.IsPlacement)
                AddToSummary(summary, change);
            else
                RemoveFromSummary(summary, change);
            change.IsApplied = true;
        }

        private static void AddFile(OmsiObjectChange change, string relativePath, byte[] before, byte[] after)
        {
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = relativePath, Before = before, After = after });
        }

        /// <summary>Writes the after state, or the before state when undoing, after checking every file first.</summary>
        private void WriteFiles(OmsiObjectChange change, bool undo)
        {
            var workspace = RequireWorkspace();
            foreach (var file in change.Files)
                if (!SameBytes(workspace.ReadAllBytes(file.RelativePath), undo ? file.After : file.Before))
                    throw new InvalidOperationException(file.RelativePath + " was changed in the meantime; the step cannot be " +
                                                        (undo ? "undone." : "applied."));
            foreach (var file in change.Files)
                workspace.Save(file.RelativePath, undo ? file.Before : file.After);
        }

        private static void AddToSummary(OmsiMapSummary summary, OmsiObjectChange change)
        {
            change.Tile.Objects.Insert(Math.Min(change.ListIndex, change.Tile.Objects.Count), change.Record);
            change.Tile.ObjectCount++;
            if (summary != null)
                summary.ObjectCount++;
        }

        private static void RemoveFromSummary(OmsiMapSummary summary, OmsiObjectChange change)
        {
            if (!change.Tile.Objects.Remove(change.Record))
                return;
            change.Tile.ObjectCount--;
            if (summary != null)
                summary.ObjectCount--;
        }

        private static OmsiObjectView FindObject(OmsiRecordFile tile, int objectId, string tileFileName)
        {
            foreach (var view in OmsiTileRecords.Of<OmsiObjectView>(tile))
                if (view.Id == objectId)
                    return view;
            throw new InvalidOperationException("Object " + objectId + " was not found in " + tileFileName + ".");
        }

        /// <summary>The tile's position in global.cfg's [map] list, which entry points use to name their tile.</summary>
        private static int TileIndexOf(OmsiRecordFile config, string tilePath)
        {
            var index = 0;
            foreach (var record in config.Records)
            {
                if (!string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (record.Fields.Count == 3 && SamePath(record.GetField(2), tilePath))
                    return index;
                index++;
            }
            throw new InvalidOperationException(tilePath + " is not listed in global.cfg.");
        }

        private static bool SamePath(string omsiPath, string relativePath)
        {
            try
            {
                return string.Equals(OmsiRelativePath.Normalize(omsiPath), relativePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool SameBytes(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (var index = 0; index < first.Length; index++)
                if (first[index] != second[index])
                    return false;
            return true;
        }

        /// <summary>
        /// Discards the workspace copy of a tile, so the original is shown again. Entry points of that tile get their
        /// original object indices back, because deleting objects in the tile may have shifted them.
        /// </summary>
        public void RevertTile(string tileFileName)
        {
            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tileFileName);
            workspace.Revert(tilePath);
            if (workspace.IsModified(GlobalConfigFileName))
                RestoreEntryPoints(workspace, tilePath);
        }

        private void RestoreEntryPoints(OmsiMapWorkspace workspace, string tilePath)
        {
            const int entryPointFieldCount = 12;
            var original = OmsiRecordFile.Load(GlobalConfigPath, OmsiGlobalConfigLayouts.Instance);
            var config = OmsiRecordFile.Parse(workspace.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            var tileIndex = TileIndexOf(original, tilePath);
            var originalEntries = original.Records.Find(record => string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase));
            var entries = config.Records.Find(record => string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase));
            if (originalEntries == null || entries == null || originalEntries.Fields.Count != entries.Fields.Count)
                return;

            for (var field = 1; field + entryPointFieldCount <= entries.Fields.Count; field += entryPointFieldCount)
                if (originalEntries.TryGetInt(field + 10, out var entryTile) && entryTile == tileIndex)
                    entries.SetField(field, originalEntries.GetField(field));
            workspace.Save(GlobalConfigFileName, config);
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
