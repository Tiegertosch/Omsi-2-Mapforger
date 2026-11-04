using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A new object file or new labels for an object or an attached object (ADR 060). In the map's own tiles the record
    /// is edited. In a chrono variant the elements the variant added itself are edited in its change list; any other
    /// element gets a [selobject] with a [typ] or [relabel] there, as the stock variants do, and the base map stays as it was.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>Records of a change list that act on the element selected just before them.</summary>
        private static readonly HashSet<string> SelectionCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "typ", "relabel", "rule", "kill_rule", "delete"
        };

        /// <summary>
        /// Gives an object or attached object another object file and keeps its ID, place, orientation and labels. One undo
        /// step; null when the file is the same.
        /// </summary>
        /// <param name="element">An <see cref="OmsiObjectRecord"/> or <see cref="OmsiAttachedObjectRecord"/> of <paramref name="tile"/>.</param>
        public OmsiObjectChange SetElementType(OmsiTileSummary tile, object element, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("An object file is required.", nameof(assetPath));
            return ChangeElement(tile, element, assetPath.Trim(), null);
        }

        /// <summary>
        /// Replaces all labels of an object or attached object, such as the line number on a bus stop's plate. One undo step;
        /// null when they are the same.
        /// </summary>
        public OmsiObjectChange SetElementLabels(OmsiTileSummary tile, object element, IReadOnlyList<string> labels)
        {
            if (labels == null)
                throw new ArgumentNullException(nameof(labels));
            if (labels.Count == 0)
                throw new ArgumentException("An object keeps at least the labels it has; give them all.", nameof(labels));
            foreach (var text in labels)
                if (text == null || text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0)
                    throw new ArgumentException("A label must be one line.", nameof(labels));
            return ChangeElement(tile, element, null, labels);
        }

        private OmsiObjectChange ChangeElement(OmsiTileSummary tile, object element, string assetPath, IReadOnlyList<string> labels)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            var objectRecord = element as OmsiObjectRecord;
            var attachedRecord = element as OmsiAttachedObjectRecord;
            if (objectRecord == null && attachedRecord == null)
                throw new ArgumentException("Pass an object or an attached object.", nameof(element));
            if (objectRecord != null ? !tile.Objects.Contains(objectRecord) : !tile.AttachedObjects.Contains(attachedRecord))
                throw new ArgumentException("The element is not in the tile.", nameof(element));

            var id = objectRecord?.Id ?? attachedRecord.Id;
            var currentPath = objectRecord?.AssetPath ?? attachedRecord.AssetPath;
            var currentLabels = objectRecord?.Labels ?? attachedRecord.Labels;
            if ((assetPath == null || string.Equals(assetPath, currentPath, StringComparison.Ordinal)) &&
                (labels == null || labels.SequenceEqual(currentLabels)))
                return null;

            var workspace = RequireWorkspace();
            var viaSelection = EditedVariant != null && !IsInEditedVariant(element);
            var path = EditedVariant == null ? OmsiRelativePath.Normalize(tile.FileName)
                : viaSelection ? ChronoFileOf(tile) : chronoOrigins[element].File;
            var before = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
            var file = before != null ? OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
            if (viaSelection)
                WriteSelection(file, id, assetPath, labels);
            else
                EditRecord(file, id, attachedRecord != null, path, assetPath, labels);
            var after = file.ToBytes();
            if (before != null && before.SequenceEqual(after))
                return null;

            var root = RootObjectOf(tile, element);
            var listIndex = root == null ? -1 : tile.Objects.IndexOf(root);
            var change = new OmsiObjectChange(assetPath != null ? OmsiObjectChangeKind.Retype : OmsiObjectChangeKind.Labels, tile, root,
                listIndex < 0 ? tile.Objects.Count : listIndex)
            {
                ElementId = id,
                ElementBefore = element,
                AssetBefore = currentPath,
                AssetAfter = assetPath ?? currentPath,
                LabelsBefore = currentLabels,
                LabelsAfter = labels ?? currentLabels
            };
            // The overlay of a chrono variant shares the base map's records, so the base map's own stay untouched and a copy takes their place.
            change.ElementAfter = viaSelection ? CopyElement(element, change) : element;
            AddFile(change, path, before, after);
            WriteFiles(change, false);
            ApplyElementChange(change, true);
            if (viaSelection && !EditedVariant.TileFiles.Exists(known => string.Equals(known, path, StringComparison.OrdinalIgnoreCase)))
                EditedVariant.TileFiles.Add(path);
            return change;
        }

        /// <summary>Edits the record of an element in the file that holds it: a tile, or the variant's change list for its own element.</summary>
        private static void EditRecord(OmsiRecordFile file, int id, bool attached, string path, string assetPath, IReadOnlyList<string> labels)
        {
            OmsiTileRecordView view = null;
            if (!attached)
                view = FindObject(file, id, path);
            else
                foreach (var candidate in OmsiTileRecords.Of<OmsiAttachedObjectView>(file))
                    if (candidate.Id == id)
                    {
                        view = candidate;
                        break;
                    }
            if (view == null)
                throw new InvalidOperationException("Attached object " + id + " was not found in " + path + ".");

            if (assetPath != null)
            {
                if (view is OmsiObjectView objectView)
                    objectView.AssetPath = assetPath;
                else
                    ((OmsiAttachedObjectView)view).AssetPath = assetPath;
            }
            if (labels == null)
                return;
            if (!view.Record.HasKnownLayout)
                throw new InvalidOperationException("The tile version of " + path + " is too old to edit labels.");
            if (view is OmsiObjectView labelled)
                labelled.SetLabels(labels.ToList());
            else
                ((OmsiAttachedObjectView)view).SetLabels(labels.ToList());
        }

        /// <summary>
        /// Writes the change into a change list for an element it did not add: [selobject] with the [typ] or [relabel] after
        /// it. The last block that selects the element is changed when there is one, so editing it again replaces the
        /// earlier command instead of piling up another.
        /// </summary>
        private static void WriteSelection(OmsiRecordFile file, int id, string assetPath, IReadOnlyList<string> labels)
        {
            var idText = id.ToString(CultureInfo.InvariantCulture);
            var selectAt = -1;
            for (var index = file.Records.Count - 1; index >= 0 && selectAt < 0; index--)
                if (string.Equals(file.Records[index].Name, "selobject", StringComparison.OrdinalIgnoreCase) && file.Records[index].Fields.Count > 0 &&
                    file.Records[index].Fields[0].Text.Trim() == idText)
                    selectAt = index;
            var blockEnd = selectAt + 1;
            var deleted = false;
            if (selectAt >= 0)
                for (; blockEnd < file.Records.Count && SelectionCommands.Contains(file.Records[blockEnd].Name); blockEnd++)
                    deleted |= string.Equals(file.Records[blockEnd].Name, "delete", StringComparison.OrdinalIgnoreCase);
            if (selectAt < 0 || deleted)
            {
                selectAt = file.Records.Count;
                file.InsertRecord(selectAt, file.CreateRecord("selobject", new[] { idText }));
                blockEnd = selectAt + 1;
            }

            for (var index = blockEnd - 1; index > selectAt; index--)
            {
                var name = file.Records[index].Name;
                if ((assetPath != null && string.Equals(name, "typ", StringComparison.OrdinalIgnoreCase)) ||
                    (labels != null && string.Equals(name, "relabel", StringComparison.OrdinalIgnoreCase)))
                    file.RemoveRecord(file.Records[index]);
            }
            var insertAt = selectAt + 1;
            if (assetPath != null)
                file.InsertRecord(insertAt++, file.CreateRecord("typ", new[] { assetPath }));
            if (labels != null)
            {
                var lines = new List<string> { labels.Count.ToString(CultureInfo.InvariantCulture) };
                lines.AddRange(labels);
                file.InsertRecord(insertAt, file.CreateRecord("relabel", lines));
            }
        }

        /// <summary>The object that stands for an element in the inspector: itself, or for an attached object the one it hangs on at the end of the chain.</summary>
        private static OmsiObjectRecord RootObjectOf(OmsiTileSummary tile, object element)
        {
            if (element is OmsiObjectRecord objectRecord)
                return objectRecord;
            var parentId = ((OmsiAttachedObjectRecord)element).ParentId;
            for (var steps = 0; steps < 64; steps++)
            {
                var parent = tile.Objects.Find(item => item.Id == parentId);
                if (parent != null)
                    return parent;
                var attached = tile.AttachedObjects.Find(item => item.Id == parentId);
                if (attached == null)
                    return null;
                parentId = attached.ParentId;
            }
            return null;
        }

        /// <summary>A copy of an element with the new file and labels of the change.</summary>
        private static object CopyElement(object element, OmsiObjectChange change)
        {
            if (element is OmsiObjectRecord item)
            {
                var copy = new OmsiObjectRecord
                {
                    AssetPath = change.AssetAfter, Id = item.Id, LocalX = item.LocalX, LocalY = item.LocalY, Height = item.Height,
                    HeadingDegrees = item.HeadingDegrees, PitchDegrees = item.PitchDegrees, BankDegrees = item.BankDegrees,
                    Labels = change.LabelsAfter
                };
                copy.Rules.AddRange(item.Rules);
                return copy;
            }
            var attached = (OmsiAttachedObjectRecord)element;
            return new OmsiAttachedObjectRecord
            {
                AssetPath = change.AssetAfter, Id = attached.Id, ParentId = attached.ParentId, AttachPointIndex = attached.AttachPointIndex,
                HeadingDegrees = attached.HeadingDegrees, PitchDegrees = attached.PitchDegrees, BankDegrees = attached.BankDegrees,
                Labels = change.LabelsAfter
            };
        }

        /// <summary>Puts the after state of a new type or new labels into the summary, or the before state when undoing.</summary>
        private void ApplyElementChange(OmsiObjectChange change, bool forward)
        {
            var from = forward ? change.ElementBefore : change.ElementAfter;
            var to = forward ? change.ElementAfter : change.ElementBefore;
            if (ReferenceEquals(from, to))
            {
                var asset = forward ? change.AssetAfter : change.AssetBefore;
                var labels = forward ? change.LabelsAfter : change.LabelsBefore;
                if (to is OmsiObjectRecord item)
                {
                    item.AssetPath = asset;
                    item.Labels = labels;
                }
                else
                {
                    var attached = (OmsiAttachedObjectRecord)to;
                    attached.AssetPath = asset;
                    attached.Labels = labels;
                }
                return;
            }

            var tile = change.Tile;
            if (from is OmsiObjectRecord fromObject)
            {
                var at = tile.Objects.IndexOf(fromObject);
                if (at >= 0)
                    tile.Objects[at] = (OmsiObjectRecord)to;
            }
            else
            {
                var at = tile.AttachedObjects.IndexOf((OmsiAttachedObjectRecord)from);
                if (at >= 0)
                    tile.AttachedObjects[at] = (OmsiAttachedObjectRecord)to;
            }
            if (chronoOrigins != null && chronoOrigins.TryGetValue(from, out var origin))
                chronoOrigins[to] = origin;
        }
    }
}
