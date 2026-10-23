using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    public enum OmsiObjectChangeKind
    {
        Placement,
        Deletion,

        /// <summary>The object moved into another tile, keeping its ID.</summary>
        Transfer
    }

    /// <summary>
    /// A placed, deleted or transferred object. It keeps the bytes of every file it changed, before and after, so undo
    /// and redo restore exactly what the change wrote, including rules and variables that went with the object.
    /// </summary>
    public sealed class OmsiObjectChange
    {
        internal sealed class FileState
        {
            public string RelativePath;
            public byte[] Before;
            public byte[] After;
        }

        internal OmsiObjectChange(OmsiObjectChangeKind kind, OmsiTileSummary tile, OmsiObjectRecord record, int listIndex)
        {
            Kind = kind;
            Tile = tile;
            Record = record;
            ListIndex = listIndex;
        }

        public OmsiObjectChangeKind Kind { get; }

        /// <summary>The tile that holds the object, for a transfer the tile it left.</summary>
        public OmsiTileSummary Tile { get; }

        /// <summary>The tile a transferred object moved into; null for other changes.</summary>
        public OmsiTileSummary TargetTile { get; internal set; }

        public OmsiObjectRecord Record { get; }

        /// <summary>True while the change is in effect, false after it was undone.</summary>
        public bool IsApplied { get; internal set; } = true;

        /// <summary>True when the object exists in the summary in the current state of the change.</summary>
        public bool ObjectExists => Kind == OmsiObjectChangeKind.Deletion ? !IsApplied : Kind == OmsiObjectChangeKind.Transfer || IsApplied;

        /// <summary>The tile that holds the object in the current state of the change.</summary>
        public OmsiTileSummary CurrentTile => Kind == OmsiObjectChangeKind.Transfer && IsApplied ? TargetTile : Tile;

        /// <summary>The record's position in <see cref="OmsiTileSummary.Objects"/> of <see cref="Tile"/> while it is there.</summary>
        internal int ListIndex { get; }

        internal int TargetListIndex { get; set; }

        /// <summary>Tile-local position before and after a transfer.</summary>
        internal double[] PositionBefore { get; set; }

        internal double[] PositionAfter { get; set; }

        internal List<FileState> Files { get; } = new List<FileState>();
    }

    /// <summary>Outcome of deleting or transferring an object: the change, or the references that prevented it.</summary>
    public sealed class OmsiObjectChangeResult
    {
        internal OmsiObjectChangeResult(OmsiObjectChange change, IReadOnlyList<OmsiReference> blockers)
        {
            Change = change;
            Blockers = blockers;
        }

        /// <summary>The applied change, or null when nothing changed.</summary>
        public OmsiObjectChange Change { get; }

        /// <summary>References that prevented the change; the files were left unchanged.</summary>
        public IReadOnlyList<OmsiReference> Blockers { get; }

        public bool Succeeded => Change != null;
    }

    /// <summary>
    /// An opened map: read-only when opened directly, editable through the project workspace when opened as a
    /// project. Edits are saved into the workspace immediately (ADR 009).
    /// </summary>
    public sealed partial class OmsiMapSession
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
            return UpdateObject(tileFileName, objectId, x, y, z, headingDegrees, null, null);
        }

        /// <summary>Like the method above and also sets pitch and bank.</summary>
        public bool UpdateObject(string tileFileName, int objectId, double x, double y, double z, double headingDegrees,
            double pitchDegrees, double bankDegrees)
        {
            return UpdateObject(tileFileName, objectId, x, y, z, headingDegrees, (double?)pitchDegrees, bankDegrees);
        }

        /// <param name="pitchDegrees">Null keeps the stored pitch; the same for bank.</param>
        private bool UpdateObject(string tileFileName, int objectId, double x, double y, double z, double headingDegrees,
            double? pitchDegrees, double? bankDegrees)
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
            if (pitchDegrees.HasValue && target.PitchDegrees != pitchDegrees.Value) { target.PitchDegrees = pitchDegrees.Value; changed = true; }
            if (bankDegrees.HasValue && target.BankDegrees != bankDegrees.Value) { target.BankDegrees = bankDegrees.Value; changed = true; }
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
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            return UpdateObject(tile, record, x, y, z, headingDegrees, record.PitchDegrees, record.BankDegrees);
        }

        /// <summary>Like the method above and also sets pitch and bank.</summary>
        public bool UpdateObject(OmsiTileSummary tile, OmsiObjectRecord record, double x, double y, double z, double headingDegrees,
            double pitchDegrees, double bankDegrees)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            if (!UpdateObject(tile.FileName, record.Id, x, y, z, headingDegrees, pitchDegrees, bankDegrees))
                return false;
            record.LocalX = AsWritten(x);
            record.LocalY = AsWritten(y);
            record.Height = AsWritten(z);
            record.HeadingDegrees = AsWritten(headingDegrees);
            record.PitchDegrees = AsWritten(pitchDegrees);
            record.BankDegrees = AsWritten(bankDegrees);
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
                HeadingDegrees = view.HeadingDegrees,
                PitchDegrees = view.PitchDegrees,
                BankDegrees = view.BankDegrees
            };
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Placement, tile, record, tile.Objects.Count);
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
        /// <param name="references">An index of the current files, shared by several deletions; null builds one.</param>
        public OmsiObjectChangeResult DeleteObject(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record,
            OmsiReferenceIndex references = null)
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

            references = references ?? BuildReferenceIndex();
            var result = OmsiTileEditing.Remove(tileFile, view, TileIndexOf(config, tilePath), config, references);
            if (!result.Removed)
                return new OmsiObjectChangeResult(null, result.Blockers);

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Deletion, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex);
            AddFile(change, tilePath, tileBefore, tileFile.ToBytes());
            if (result.ShiftedEntryPointIndices > 0)
                AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
            WriteFiles(change, false);
            RemoveFromSummary(summary, change);
            return new OmsiObjectChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>
        /// Moves an object into another tile at a position in that tile, with its rules and the same ID. Entry point
        /// indices of the source tile stay valid. Nothing changes while references tie the object to its tile; they
        /// are returned instead.
        /// </summary>
        /// <param name="references">An index of the current files, shared by several transfers; null builds one.</param>
        public OmsiObjectChangeResult TransferObject(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record,
            OmsiTileSummary target, double x, double y, OmsiReferenceIndex references = null)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (ReferenceEquals(tile, target))
                throw new ArgumentException("The target must be another tile.", nameof(target));
            if (!target.IsAvailable)
                throw new InvalidOperationException("The tile file " + target.FileName + " is missing.");

            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var targetPath = OmsiRelativePath.Normalize(target.FileName);
            var tileBefore = workspace.ReadAllBytes(tilePath);
            var targetBefore = workspace.ReadAllBytes(targetPath);
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var tileFile = OmsiRecordFile.Parse(tileBefore, OmsiTileRecordLayouts.Instance);
            var targetFile = OmsiRecordFile.Parse(targetBefore, OmsiTileRecordLayouts.Instance);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            var view = FindObject(tileFile, record.Id, tile.FileName);

            references = references ?? BuildReferenceIndex();
            var result = OmsiTileEditing.MoveObjectToTile(tileFile, view, TileIndexOf(config, tilePath), targetFile, x, y, config, references);
            if (!result.Removed)
                return new OmsiObjectChangeResult(null, result.Blockers);

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Transfer, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex)
            {
                TargetTile = target,
                TargetListIndex = target.Objects.Count,
                PositionBefore = new[] { record.LocalX, record.LocalY },
                PositionAfter = new[] { AsWritten(x), AsWritten(y) }
            };
            AddFile(change, tilePath, tileBefore, tileFile.ToBytes());
            AddFile(change, targetPath, targetBefore, targetFile.ToBytes());
            if (result.ShiftedEntryPointIndices > 0)
                AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
            WriteFiles(change, false);
            MoveInSummary(change, true);
            return new OmsiObjectChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>Every reference to an ID in the current files; build it once for a group of deletions or transfers.</summary>
        public OmsiReferenceIndex BuildReferenceIndex()
        {
            return OmsiReferenceIndex.Build(RequireWorkspace(), GlobalConfigFileName);
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
            if (change.Kind == OmsiObjectChangeKind.Placement)
                RemoveFromSummary(summary, change);
            else if (change.Kind == OmsiObjectChangeKind.Deletion)
                AddToSummary(summary, change);
            else
                MoveInSummary(change, false);
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
            if (change.Kind == OmsiObjectChangeKind.Placement)
                AddToSummary(summary, change);
            else if (change.Kind == OmsiObjectChangeKind.Deletion)
                RemoveFromSummary(summary, change);
            else
                MoveInSummary(change, true);
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

        /// <summary>Moves a transferred record between the tiles of the summary; the map total stays the same.</summary>
        private static void MoveInSummary(OmsiObjectChange change, bool forward)
        {
            var from = forward ? change.Tile : change.TargetTile;
            var to = forward ? change.TargetTile : change.Tile;
            if (from.Objects.Remove(change.Record))
                from.ObjectCount--;
            var position = forward ? change.PositionAfter : change.PositionBefore;
            change.Record.LocalX = position[0];
            change.Record.LocalY = position[1];
            to.Objects.Insert(Math.Min(forward ? change.TargetListIndex : change.ListIndex, to.Objects.Count), change.Record);
            to.ObjectCount++;
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
            // Roads of this tile may have fitted the terrain here and around; it follows the roads that remain.
            if (workspace.ModifiedFiles().Exists(path => path.EndsWith(".terrain", StringComparison.OrdinalIgnoreCase)))
            {
                var summary = Read();
                var tile = summary.Tiles.Find(candidate => SamePath(candidate.FileName, tilePath));
                if (tile != null)
                    RefitTerrainAround(summary, tile);
            }
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
