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
        Transfer,

        /// <summary>The object got another object file, keeping its ID and place, such as a junction made again.</summary>
        Replacement,

        /// <summary>
        /// A chrono variant took an object of the base map or of an earlier variant away and added a copy with a new ID
        /// where it was moved or turned to; chrono change lists cannot move what they did not add.
        /// </summary>
        Copy,

        /// <summary>The [rule] records after the object changed, such as the right of way of its paths (ADR 047).</summary>
        Rules
    }

    /// <summary>
    /// A placed, deleted, transferred or replaced object. It keeps the bytes of every file it changed, before and after, so undo
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
        public bool ObjectExists => Kind == OmsiObjectChangeKind.Deletion
            ? !IsApplied
            : Kind == OmsiObjectChangeKind.Transfer || Kind == OmsiObjectChangeKind.Replacement || Kind == OmsiObjectChangeKind.Copy ||
              Kind == OmsiObjectChangeKind.Rules || IsApplied;

        /// <summary>The object in the summary in the current state of the change: for an undone copy the original.</summary>
        public OmsiObjectRecord CurrentRecord => Kind == OmsiObjectChangeKind.Copy && !IsApplied ? ReplacedRecord : Record;

        /// <summary>For a copy the object it replaces; null for other changes.</summary>
        public OmsiObjectRecord ReplacedRecord { get; internal set; }

        /// <summary>Attached objects a copy added on the copied object, in place of <see cref="RemovedAttachedObjects"/>.</summary>
        internal List<OmsiAttachedObjectRecord> AddedAttachedObjects { get; } = new List<OmsiAttachedObjectRecord>();

        /// <summary>
        /// References to the objects a copy replaced that name their IDs and do not follow to the copy, such as bus stops
        /// and timetables; the copy is saved anyway.
        /// </summary>
        public List<OmsiReference> StaleReferences { get; } = new List<OmsiReference>();

        /// <summary>The tile that holds the object in the current state of the change.</summary>
        public OmsiTileSummary CurrentTile => Kind == OmsiObjectChangeKind.Transfer && IsApplied ? TargetTile : Tile;

        /// <summary>The record's position in <see cref="OmsiTileSummary.Objects"/> of <see cref="Tile"/> while it is there.</summary>
        internal int ListIndex { get; }

        internal int TargetListIndex { get; set; }

        /// <summary>Tile-local position before and after a transfer.</summary>
        internal double[] PositionBefore { get; set; }

        internal double[] PositionAfter { get; set; }

        /// <summary>Attached objects a transfer took along into the target tile.</summary>
        internal List<OmsiAttachedObjectRecord> MovedAttachedObjects { get; } = new List<OmsiAttachedObjectRecord>();

        /// <summary>Attached objects a deletion took off with the object.</summary>
        internal List<OmsiAttachedObjectRecord> RemovedAttachedObjects { get; } = new List<OmsiAttachedObjectRecord>();

        /// <summary>How many attached objects a deletion took off with the object.</summary>
        public int RemovedAttachedObjectCount => RemovedAttachedObjects.Count;

        /// <summary>The object file before and after a replacement; null for other changes.</summary>
        public string AssetBefore { get; internal set; }

        public string AssetAfter { get; internal set; }

        /// <summary>The object's rules before and after a change of its rules; null for other changes.</summary>
        internal List<OmsiPathRule> RulesBefore { get; set; }

        internal List<OmsiPathRule> RulesAfter { get; set; }

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
        /// Sets an object's position and heading and saves the tile into the workspace; entry points on the object follow
        /// it in global.cfg. Values equal to the stored ones keep their original text. Returns false when nothing changed.
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

            var oldX = target.X;
            var oldY = target.Y;
            var oldZ = target.Z;
            var oldHeading = target.HeadingDegrees;
            // Entry points name base tiles only; objects of a chrono change list have none.
            var isChronoFile = IsChronoFile(relative);
            var changed = false;
            if (target.X != x) { target.X = x; changed = true; }
            if (target.Y != y) { target.Y = y; changed = true; }
            if (target.Z != z) { target.Z = z; changed = true; }
            if (target.HeadingDegrees != headingDegrees) { target.HeadingDegrees = headingDegrees; changed = true; }
            if (pitchDegrees.HasValue && target.PitchDegrees != pitchDegrees.Value) { target.PitchDegrees = pitchDegrees.Value; changed = true; }
            if (bankDegrees.HasValue && target.BankDegrees != bankDegrees.Value) { target.BankDegrees = bankDegrees.Value; changed = true; }
            if (!changed)
                return false;
            if (!isChronoFile)
            {
                var config = OmsiRecordFile.Parse(workspace.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
                if (config.Records.Exists(record => string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase)) &&
                    OmsiTileEditing.FollowEntryPoints(tile, target, TileIndexOf(config, relative), config, oldX, oldY, oldZ, oldHeading) > 0)
                    workspace.Save(GlobalConfigFileName, config);
            }
            workspace.Save(relative, tile);
            return true;
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

            if (!UpdateObject(FileHolding(tile, record), record.Id, x, y, z, headingDegrees, pitchDegrees, bankDegrees))
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
            // While a chrono variant is edited, new objects go into its change list for the tile.
            var tilePath = EditedVariant != null ? ChronoFileOf(tile) : OmsiRelativePath.Normalize(tile.FileName);
            var tileBefore = workspace.FileExists(tilePath) ? workspace.ReadAllBytes(tilePath) : null;
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var tileFile = tileBefore != null ? OmsiRecordFile.Parse(tileBefore, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
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
            if (EditedVariant != null)
                AddToEditedVariant(record, tilePath);
            else
                summary.HighestBaseTileId = Math.Max(summary.HighestBaseTileId, record.Id);
            summary.NextIdCode = ids.NextFreeId;
            return change;
        }

        /// <summary>
        /// Deletes an object with the rules and variables that follow it, and the attached objects hanging on it, and keeps
        /// the entry point indices of global.cfg valid. Nothing changes while tiles, chrono patches, entry points or
        /// timetables still refer to one of them; those references are returned instead.
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
            if (EditedVariant != null)
                return DeleteInEditedVariant(summary, tile, record, references);

            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var tileBefore = workspace.ReadAllBytes(tilePath);
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var tileFile = OmsiRecordFile.Parse(tileBefore, OmsiTileRecordLayouts.Instance);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            var view = FindObject(tileFile, record.Id, tile.FileName);
            var family = OmsiTileEditing.Family(tileFile, view);

            references = references ?? BuildReferenceIndex();
            var result = OmsiTileEditing.RemoveWithFamily(tileFile, view, tilePath, TileIndexOf(config, tilePath), config, references);
            if (!result.Removed)
                return new OmsiObjectChangeResult(null, result.Blockers);

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Deletion, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex);
            foreach (var member in family)
                if (member is OmsiAttachedObjectView attachedView)
                {
                    var attached = tile.AttachedObjects.Find(item => item.Id == attachedView.Id);
                    if (attached != null)
                        change.RemovedAttachedObjects.Add(attached);
                }
            AddFile(change, tilePath, tileBefore, tileFile.ToBytes());
            if (result.ShiftedEntryPointIndices > 0)
                AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
            WriteFiles(change, false);
            RemoveFromSummary(summary, change);
            return new OmsiObjectChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>
        /// Moves an object into another tile at a position in that tile, with its rules, the attached objects hanging on it
        /// and the same IDs (ADR 012). What names them by tile follows: entry points get the new tile, index and position,
        /// and chrono selections move into the chrono file of the new tile in each variant. Variable parents name IDs and
        /// reach into neighbouring tiles, so a [varparent] that would end up further away keeps the object in its tile, as
        /// do references that cannot follow, such as an attached object a chrono variant adds; they are returned instead.
        /// </summary>
        /// <param name="references">An index of the current files, shared by several transfers; null builds one.</param>
        public OmsiObjectChangeResult TransferObject(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record,
            OmsiTileSummary target, double x, double y, OmsiReferenceIndex references = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
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
            if (EditedVariant != null)
                throw new InvalidOperationException("Objects of a chrono variant stay in their tile; end editing the variant to move objects between tiles.");

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

            var ids = new HashSet<int>();
            foreach (var member in OmsiTileEditing.Family(tileFile, view))
                ids.Add(member is OmsiAttachedObjectView attached ? attached.Id : view.Id);
            var chronoTiles = new List<string>();
            foreach (var file in workspace.EnumerateFiles("Chrono", Path.GetFileName(tilePath), true))
                chronoTiles.Add(file);
            references = references ?? BuildReferenceIndex();
            var blockers = new List<OmsiReference>();
            foreach (var id in ids)
                foreach (var reference in references.To(id))
                {
                    switch (reference.Kind)
                    {
                        case "attached object parent":
                            // Attached objects in the tile move along; elsewhere they could not follow.
                            if (!SamePath(reference.File, tilePath))
                                blockers.Add(reference);
                            break;
                        case "variable parent":
                            var holder = summary.Tiles.Find(candidate => SamePath(reference.File, OmsiRelativePath.Normalize(candidate.FileName)));
                            if (holder == null || !Neighbours(holder, target))
                                blockers.Add(new OmsiReference(id, reference.File, "variable parent out of reach"));
                            break;
                        case "chrono selection":
                            if (!chronoTiles.Exists(file => SamePath(reference.File, file)))
                                blockers.Add(reference);
                            break;
                    }
                }
            // A chrono variant may hang objects of its own on one that moves; they would stay behind in the old tile.
            var chronoFiles = new List<KeyValuePair<string, OmsiRecordFile>>();
            foreach (var file in chronoTiles)
            {
                var chrono = OmsiRecordFile.Parse(workspace.ReadAllBytes(file), OmsiTileRecordLayouts.Instance);
                foreach (var attached in OmsiTileRecords.Of<OmsiAttachedObjectView>(chrono))
                    if (ids.Contains(attached.ParentId))
                        blockers.Add(new OmsiReference(attached.ParentId, file, "attached object of a chrono variant"));
                chronoFiles.Add(new KeyValuePair<string, OmsiRecordFile>(file, chrono));
            }

            var result = OmsiTileEditing.MoveObjectToTile(tileFile, view, TileIndexOf(config, tilePath), targetFile, TileIndexOf(config, targetPath), x, y,
                config, blockers, parent => TileOf(summary, parent) is OmsiTileSummary parentTile && Neighbours(parentTile, target));
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
            if (result.ShiftedEntryPointIndices > 0 || result.MovedEntryPoints > 0)
                AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
            foreach (var pair in chronoFiles)
            {
                var targetChronoPath = Path.Combine(Path.GetDirectoryName(pair.Key) ?? string.Empty, Path.GetFileName(targetPath));
                var targetChronoBefore = workspace.FileExists(targetChronoPath) ? workspace.ReadAllBytes(targetChronoPath) : null;
                var targetChrono = targetChronoBefore != null
                    ? OmsiRecordFile.Parse(targetChronoBefore, OmsiTileRecordLayouts.Instance)
                    : NewChronoTile(pair.Value);
                if (OmsiTileEditing.MoveChronoSelections(pair.Value, targetChrono, ids) == 0)
                    continue;
                AddFile(change, pair.Key, workspace.ReadAllBytes(pair.Key), pair.Value.ToBytes());
                AddFile(change, targetChronoPath, targetChronoBefore, targetChrono.ToBytes());
            }
            WriteFiles(change, false);
            MoveInSummary(change, true);
            foreach (var attached in tile.AttachedObjects.FindAll(candidate => ids.Contains(candidate.Id)))
            {
                tile.AttachedObjects.Remove(attached);
                target.AttachedObjects.Add(attached);
                change.MovedAttachedObjects.Add(attached);
            }
            return new OmsiObjectChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>An empty chrono tile file like another: its encoding and version, without changes.</summary>
        private static OmsiRecordFile NewChronoTile(OmsiRecordFile like)
        {
            var text = "File created with OMSI MapWorks on " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
                       "\r\n\r\n[version]\r\n" + (like.Version > 0 ? like.Version : 14).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n\r\n";
            return OmsiRecordFile.Parse(OmsiTextFile.Encode(text, like.Encoding), OmsiTileRecordLayouts.Instance);
        }

        /// <summary>True when two tiles are the same or touch, also at a corner.</summary>
        private static bool Neighbours(OmsiTileSummary first, OmsiTileSummary second)
        {
            return Math.Abs(first.X - second.X) <= 1 && Math.Abs(first.Y - second.Y) <= 1;
        }

        /// <summary>The tile holding an element by ID: an object, attached object, spline or object along a spline; or null.</summary>
        private static OmsiTileSummary TileOf(OmsiMapSummary summary, int id)
        {
            foreach (var candidate in summary.Tiles)
                if (candidate.Objects.Exists(item => item.Id == id) || candidate.AttachedObjects.Exists(item => item.Id == id) ||
                    candidate.Splines.Exists(spline => spline.Id == id) || candidate.SplineAttachments.Exists(item => item.Id == id))
                    return candidate;
            return null;
        }

        /// <summary>
        /// Gives a placed object another object file and keeps everything else: its ID, place, orientation and the rules
        /// that follow it. Used to put a junction made again in place of the one it replaces.
        /// </summary>
        public OmsiObjectChange ReplaceObject(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record, string assetPath)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("An object file is required.", nameof(assetPath));

            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var tileBefore = workspace.ReadAllBytes(tilePath);
            var tileFile = OmsiRecordFile.Parse(tileBefore, OmsiTileRecordLayouts.Instance);
            var view = FindObject(tileFile, record.Id, tile.FileName);
            view.AssetPath = assetPath;

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Replacement, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex)
            {
                AssetBefore = record.AssetPath,
                AssetAfter = view.AssetPath
            };
            AddFile(change, tilePath, tileBefore, tileFile.ToBytes());
            WriteFiles(change, false);
            record.AssetPath = change.AssetAfter;
            return change;
        }

        /// <summary>Every reference to an ID in the current files; build it once for a group of deletions or transfers.</summary>
        public OmsiReferenceIndex BuildReferenceIndex()
        {
            return OmsiReferenceIndex.Build(RequireWorkspace(), GlobalConfigFileName);
        }

        /// <summary>
        /// Takes back a placement, deletion, transfer or replacement: writes the files as they were before and updates the summary. Fails
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
            else if (change.Kind == OmsiObjectChangeKind.Replacement)
                change.Record.AssetPath = change.AssetBefore;
            else if (change.Kind == OmsiObjectChangeKind.Rules)
                SetRules(change.Record, change.RulesBefore);
            else if (change.Kind == OmsiObjectChangeKind.Copy)
                SwapCopyInSummary(change, false);
            else
                MoveInSummary(change, false);
            change.IsApplied = false;
        }

        /// <summary>Applies an undone change again.</summary>
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
            else if (change.Kind == OmsiObjectChangeKind.Replacement)
                change.Record.AssetPath = change.AssetAfter;
            else if (change.Kind == OmsiObjectChangeKind.Rules)
                SetRules(change.Record, change.RulesAfter);
            else if (change.Kind == OmsiObjectChangeKind.Copy)
                SwapCopyInSummary(change, true);
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
            ApplyFiles(change.Files, undo);
        }

        private static void AddToSummary(OmsiMapSummary summary, OmsiObjectChange change)
        {
            change.Tile.Objects.Insert(Math.Min(change.ListIndex, change.Tile.Objects.Count), change.Record);
            change.Tile.ObjectCount++;
            if (summary != null)
                summary.ObjectCount++;
            foreach (var attached in change.RemovedAttachedObjects)
                if (!change.Tile.AttachedObjects.Contains(attached))
                {
                    change.Tile.AttachedObjects.Add(attached);
                    change.Tile.AttachmentCount++;
                    if (summary != null)
                        summary.AttachmentCount++;
                }
        }

        private static void RemoveFromSummary(OmsiMapSummary summary, OmsiObjectChange change)
        {
            foreach (var attached in change.RemovedAttachedObjects)
                if (change.Tile.AttachedObjects.Remove(attached))
                {
                    change.Tile.AttachmentCount--;
                    if (summary != null)
                        summary.AttachmentCount--;
                }
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
            foreach (var attached in change.MovedAttachedObjects)
                if (from.AttachedObjects.Remove(attached))
                    to.AttachedObjects.Add(attached);
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
            // Without its file the map would still list an added tile; deleting it takes it out of the list.
            if (IsTileAdded(tilePath))
                throw new InvalidOperationException(tilePath + " was added in this project and has no original; delete the tile instead.");
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
                throw new InvalidOperationException("Save a MapWorks project before editing; the map is open read-only.");
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
