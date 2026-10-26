using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Editing a chrono variant (ADR 042). While one is chosen, placed objects go into its change list for their tile and
    /// its own objects move, turn and go there. Objects of the base map or of earlier variants are taken away with
    /// [selobject] and [delete]; one that is moved or turned is replaced by a copy with a new ID, because change lists
    /// cannot move what they did not add.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        // Where the elements of the overlay being edited come from; null while the map's tiles are edited.
        private Dictionary<object, OmsiChronoOrigin> chronoOrigins;

        /// <summary>The chrono variant that edits go into, or null while they go into the map's tiles.</summary>
        public OmsiChronoVariant EditedVariant { get; private set; }

        /// <summary>
        /// Sends object edits into <paramref name="variant"/>; <paramref name="overlay"/> is the report of the overlay the
        /// edits are made on, which must include the variant. Null returns to editing the map's tiles.
        /// </summary>
        public void EditChronoVariant(OmsiChronoVariant variant, OmsiChronoOverlayReport overlay)
        {
            if (variant == null)
            {
                EditedVariant = null;
                chronoOrigins = null;
                return;
            }
            RequireWorkspace();
            if (overlay == null)
                throw new ArgumentNullException(nameof(overlay));
            if (!overlay.Variants.Contains(variant))
                throw new ArgumentException("The overlay must include the variant.", nameof(overlay));
            EditedVariant = variant;
            chronoOrigins = overlay.Origins;
        }

        /// <summary>The variant that adds an element of the overlay being edited, or null for the base map.</summary>
        public OmsiChronoVariant OriginOf(object element)
        {
            return chronoOrigins != null && element != null && chronoOrigins.TryGetValue(element, out var origin) ? origin.Variant : null;
        }

        /// <summary>True when the edited variant adds the element itself, so it can move and turn it.</summary>
        public bool IsInEditedVariant(object element)
        {
            return EditedVariant != null && ReferenceEquals(OriginOf(element), EditedVariant);
        }

        /// <summary>True when every file of the variant was made in this project, so it can be deleted again.</summary>
        public bool IsMadeInProject(OmsiChronoVariant variant)
        {
            if (Workspace == null || variant == null)
                return false;
            var files = Workspace.EnumerateFiles(variant.Folder, "*", true);
            foreach (var file in files)
                if (Workspace.ReadOriginalBytes(file) != null)
                    return false;
            return files.Count > 0;
        }

        /// <summary>
        /// Makes a chrono variant that applies from <paramref name="start"/> to <paramref name="end"/>, after the map's
        /// variants: a folder with Chrono.cfg and an English description that names it.
        /// </summary>
        public OmsiChronoVariant CreateChronoVariant(string title, DateTime start, DateTime? end)
        {
            var workspace = RequireWorkspace();
            title = title?.Trim();
            if (string.IsNullOrEmpty(title))
                throw new ArgumentException("Give the variant a name.", nameof(title));
            if (title.IndexOf('\r') >= 0 || title.IndexOf('\n') >= 0)
                throw new ArgumentException("The name must fit on one line.", nameof(title));
            if (end.HasValue && end.Value.Date < start.Date)
                throw new ArgumentException("The variant must end on or after its first day.", nameof(end));

            var name = OmsiChrono.NewVariantName(OmsiChrono.ReadVariants(workspace), title);
            var folder = Path.Combine(OmsiChrono.Folder, name);
            var settings = new StringBuilder("\r\n\r\n[startdate]\r\n" + Date(start) + "\r\n\r\n");
            if (end.HasValue)
                settings.Append("[enddate]\r\n" + Date(end.Value) + "\r\n\r\n");
            // As Grundorf's variant: the settings in Windows-1252; the description holds any name, so it is UTF-16 as the tiles.
            workspace.Save(Path.Combine(folder, "Chrono.cfg"), OmsiTextFile.Encode(settings.ToString(), OmsiTextEncoding.Windows1252));
            workspace.Save(Path.Combine(folder, "Chrono_ENG.dsc"),
                OmsiTextFile.Encode("\r\n\r\n[name]\r\n" + title + "\r\n\r\n[description]\r\n\r\n[end]\r\n", OmsiTextEncoding.Utf16LittleEndian));
            return OmsiChrono.ReadVariants(workspace).Find(variant => string.Equals(variant.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string Date(DateTime date)
        {
            return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        /// <summary>Deletes a variant made in this project with all its files; variants of the original map stay.</summary>
        public void DeleteChronoVariant(OmsiChronoVariant variant)
        {
            if (variant == null)
                throw new ArgumentNullException(nameof(variant));
            var workspace = RequireWorkspace();
            if (!IsMadeInProject(variant))
                throw new InvalidOperationException(variant.Title + " belongs to the original map and cannot be deleted.");
            if (ReferenceEquals(variant, EditedVariant))
                throw new InvalidOperationException("Stop editing " + variant.Title + " before deleting it.");
            foreach (var file in workspace.EnumerateFiles(variant.Folder, "*", true))
                workspace.Revert(file);
        }

        /// <summary>
        /// Replaces an object of the base map or of an earlier variant, with the attached objects hanging on it, by a copy
        /// with new IDs at a new place in the tile: the edited variant takes the originals away and adds the copies with
        /// their rules. References that name the old IDs, such as bus stops, stay and are listed on the change. Nothing
        /// changes while objects of other change lists hang on it; those are returned instead.
        /// </summary>
        public OmsiObjectChangeResult CopyInEditedVariant(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record,
            double x, double y, double z, double headingDegrees, double pitchDegrees, double bankDegrees, OmsiReferenceIndex references = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (EditedVariant == null)
                throw new InvalidOperationException("Choose a chrono variant to edit first.");
            if (IsInEditedVariant(record))
                throw new InvalidOperationException("Object " + record.Id + " belongs to the edited variant; move it instead.");

            var workspace = RequireWorkspace();
            var path = ChronoFileOf(tile);
            var before = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
            var file = before != null ? OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
            var sourcePath = chronoOrigins.TryGetValue(record, out var origin) ? origin.File : OmsiRelativePath.Normalize(tile.FileName);
            var source = OmsiRecordFile.Parse(workspace.ReadAllBytes(sourcePath), OmsiTileRecordLayouts.Instance);
            var family = OmsiTileEditing.Family(source, FindObject(source, record.Id, sourcePath));
            var familyIds = new HashSet<int>();
            foreach (var member in family)
                familyIds.Add(IdOf(member));

            var blockers = new List<OmsiReference>();
            if (source.Version != file.Version)
                blockers.Add(new OmsiReference(record.Id, path, "change list of tile version " + file.Version + " for an object of version " + source.Version));
            foreach (var other in workspace.EnumerateFiles(OmsiChrono.Folder, Path.GetFileName(path), true))
            {
                if (SamePath(other, sourcePath))
                    continue;
                var chrono = SamePath(other, path) ? file : OmsiRecordFile.Parse(workspace.ReadAllBytes(other), OmsiTileRecordLayouts.Instance);
                foreach (var attached in OmsiTileRecords.Of<OmsiAttachedObjectView>(chrono))
                    if (familyIds.Contains(attached.ParentId))
                        blockers.Add(new OmsiReference(attached.ParentId, other, "attached object of a chrono variant"));
            }
            if (blockers.Count > 0)
                return new OmsiObjectChangeResult(null, blockers);

            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            ids = ids ?? OmsiIdAllocator.ForMap(workspace, GlobalConfigFileName);
            foreach (var member in family)
                AppendDeletion(file, IdOf(member));
            var newIds = new Dictionary<int, int>();
            OmsiObjectRecord copy = null;
            var attachedCopies = new List<OmsiAttachedObjectRecord>();
            foreach (var member in family)
            {
                var created = OmsiTileRecords.Create(file, AppendCopies(file, OmsiTileEditing.WithModifiers(source, member.Record)));
                if (created is OmsiObjectView objectView)
                {
                    objectView.Id = newIds[record.Id] = ids.Allocate();
                    objectView.AssetPath = record.AssetPath;
                    objectView.X = x;
                    objectView.Y = y;
                    objectView.Z = z;
                    objectView.HeadingDegrees = headingDegrees;
                    objectView.PitchDegrees = pitchDegrees;
                    objectView.BankDegrees = bankDegrees;
                    copy = new OmsiObjectRecord
                    {
                        AssetPath = objectView.AssetPath, Id = objectView.Id, LocalX = objectView.X, LocalY = objectView.Y, Height = objectView.Z,
                        HeadingDegrees = objectView.HeadingDegrees, PitchDegrees = objectView.PitchDegrees, BankDegrees = objectView.BankDegrees
                    };
                }
                else if (created is OmsiAttachedObjectView attachedView)
                {
                    var oldId = attachedView.Id;
                    attachedView.Id = newIds[oldId] = ids.Allocate();
                    attachedView.ParentId = newIds[attachedView.ParentId];
                    attachedCopies.Add(new OmsiAttachedObjectRecord
                    {
                        AssetPath = attachedView.AssetPath, Id = attachedView.Id, ParentId = attachedView.ParentId, AttachPointIndex = attachedView.AttachPointIndex,
                        HeadingDegrees = attachedView.HeadingDegrees, PitchDegrees = attachedView.PitchDegrees, BankDegrees = attachedView.BankDegrees
                    });
                }
            }
            ids.ApplyTo(config);

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Copy, tile, copy, listIndex < 0 ? tile.Objects.Count : listIndex) { ReplacedRecord = record };
            change.RemovedAttachedObjects.AddRange(tile.AttachedObjects.FindAll(attached => familyIds.Contains(attached.Id)));
            change.AddedAttachedObjects.AddRange(attachedCopies);
            references = references ?? BuildReferenceIndex();
            foreach (var id in familyIds)
                foreach (var reference in references.To(id))
                    if (reference.Kind != "attached object parent" && !(reference.Kind == "chrono selection" && SamePath(reference.File, path)))
                        change.StaleReferences.Add(reference);
            AddFile(change, path, before, file.ToBytes());
            AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
            WriteFiles(change, false);
            SwapCopyInSummary(change, true);
            AddToEditedVariant(copy, path);
            foreach (var attached in attachedCopies)
                AddToEditedVariant(attached, path);
            summary.NextIdCode = ids.NextFreeId;
            return new OmsiObjectChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>
        /// Deletes an object while a variant is edited: one the variant adds goes from its change list with the attached
        /// objects on it, unless something else refers to it; any other is taken away by [selobject] and [delete], and so
        /// are the attached objects hanging on it.
        /// </summary>
        private OmsiObjectChangeResult DeleteInEditedVariant(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record,
            OmsiReferenceIndex references)
        {
            var workspace = RequireWorkspace();
            var path = ChronoFileOf(tile);
            var before = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
            var file = before != null ? OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
            var config = OmsiRecordFile.Parse(workspace.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            var hanging = HangingOn(tile, record.Id);
            if (IsInEditedVariant(record))
            {
                // Change lists hold no entry points, so no tile index applies.
                var result = OmsiTileEditing.RemoveWithFamily(file, FindObject(file, record.Id, path), path, -1, config, references ?? BuildReferenceIndex());
                if (!result.Removed)
                    return new OmsiObjectChangeResult(null, result.Blockers);
            }
            else
            {
                foreach (var attached in hanging)
                {
                    if (!IsInEditedVariant(attached))
                    {
                        AppendDeletion(file, attached.Id);
                        continue;
                    }
                    foreach (var view in new List<OmsiAttachedObjectView>(OmsiTileRecords.Of<OmsiAttachedObjectView>(file)))
                        if (view.Id == attached.Id)
                            OmsiTileEditing.RemoveWithBlockers(file, view, -1, config, null);
                }
                AppendDeletion(file, record.Id);
            }

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Deletion, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex);
            change.RemovedAttachedObjects.AddRange(hanging);
            AddFile(change, path, before, file.ToBytes());
            WriteFiles(change, false);
            RemoveFromSummary(summary, change);
            return new OmsiObjectChangeResult(change, new OmsiReference[0]);
        }

        /// <summary>The attached objects of the tile hanging on an object, directly or through another.</summary>
        private static List<OmsiAttachedObjectRecord> HangingOn(OmsiTileSummary tile, int id)
        {
            var ids = new HashSet<int> { id };
            var hanging = new List<OmsiAttachedObjectRecord>();
            for (var found = true; found;)
            {
                found = false;
                foreach (var attached in tile.AttachedObjects)
                    if (ids.Contains(attached.ParentId) && ids.Add(attached.Id))
                    {
                        hanging.Add(attached);
                        found = true;
                    }
            }
            return hanging;
        }

        /// <summary>Takes an element away in a change list: it is selected by ID and deleted.</summary>
        private static void AppendDeletion(OmsiRecordFile file, int id)
        {
            file.InsertRecord(file.Records.Count, file.CreateRecord("selobject", new[] { id.ToString(CultureInfo.InvariantCulture) }));
            file.InsertRecord(file.Records.Count, file.CreateRecord("delete", new string[0]));
        }

        /// <summary>Appends copies of records from another file at the end of <paramref name="file"/>; returns the first copy.</summary>
        private static OmsiRecord AppendCopies(OmsiRecordFile file, List<OmsiRecord> records)
        {
            OmsiRecord first = null;
            foreach (var record in records)
            {
                var values = new List<string>();
                foreach (var line in record.Fields)
                    values.Add(line.Text.Trim());
                // A record of unknown layout holds its values after the header, up to the blank line that ends it.
                if (!record.HasKnownLayout)
                {
                    var last = record.Trailer.Count;
                    while (last > 0 && record.Trailer[last - 1].Text.Trim().Length == 0)
                        last--;
                    for (var index = 0; index < last; index++)
                        values.Add(record.Trailer[index].Text.Trim());
                }
                var copy = file.CreateRecord(record.Name, values);
                file.InsertRecord(file.Records.Count, copy);
                first = first ?? copy;
            }
            return first;
        }

        private static int IdOf(OmsiTileRecordView member)
        {
            return member is OmsiAttachedObjectView attached ? attached.Id : ((OmsiObjectView)member).Id;
        }

        /// <summary>Puts a copy in place of the object it replaced in the summary, or back.</summary>
        private static void SwapCopyInSummary(OmsiObjectChange change, bool forward)
        {
            var tile = change.Tile;
            var from = forward ? change.ReplacedRecord : change.Record;
            var to = forward ? change.Record : change.ReplacedRecord;
            var index = tile.Objects.IndexOf(from);
            if (index >= 0)
                tile.Objects[index] = to;
            else if (!tile.Objects.Contains(to))
                tile.Objects.Insert(Math.Min(change.ListIndex, tile.Objects.Count), to);
            foreach (var attached in forward ? change.RemovedAttachedObjects : change.AddedAttachedObjects)
                tile.AttachedObjects.Remove(attached);
            foreach (var attached in forward ? change.AddedAttachedObjects : change.RemovedAttachedObjects)
                if (!tile.AttachedObjects.Contains(attached))
                    tile.AttachedObjects.Add(attached);
        }

        /// <summary>The file that holds an object: its tile, or while a variant is edited, the variant's change list.</summary>
        private string FileHolding(OmsiTileSummary tile, object element)
        {
            if (EditedVariant == null)
                return OmsiRelativePath.Normalize(tile.FileName);
            if (IsInEditedVariant(element))
                return chronoOrigins[element].File;
            throw new InvalidOperationException("The object belongs to " + (OriginOf(element)?.Title ?? "the base map") +
                                                "; the edited variant can only replace it with a copy.");
        }

        /// <summary>The edited variant's change list for a tile, whether it exists yet or not.</summary>
        private string ChronoFileOf(OmsiTileSummary tile)
        {
            return Path.Combine(EditedVariant.Folder, Path.GetFileName(OmsiRelativePath.Normalize(tile.FileName)));
        }

        private static bool IsChronoFile(string relativePath)
        {
            return relativePath.StartsWith(OmsiChrono.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>An empty change list for a tile, in the encoding and version of the tile's own file.</summary>
        private OmsiRecordFile NewChronoTileFor(OmsiTileSummary tile)
        {
            return NewChronoTile(OmsiRecordFile.Parse(RequireWorkspace().ReadAllBytes(OmsiRelativePath.Normalize(tile.FileName)), OmsiTileRecordLayouts.Instance));
        }

        private void AddToEditedVariant(object element, string file)
        {
            chronoOrigins[element] = new OmsiChronoOrigin(EditedVariant, file);
            if (!EditedVariant.TileFiles.Exists(known => string.Equals(known, file, StringComparison.OrdinalIgnoreCase)))
                EditedVariant.TileFiles.Add(file);
        }
    }
}
