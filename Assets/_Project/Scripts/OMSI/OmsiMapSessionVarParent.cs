using System;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The [varparent] of an object or attached object (ADR 082): the element whose data it shows or runs. A traffic light names its
    /// crossing, a departure display its bus stop. The record sits right after the element it belongs to.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>Oldest tile version whose [varparent] holds one ID.</summary>
        private const int FirstVersionWithVarParent = 6;

        /// <summary>
        /// Sets the variable parent of an object or attached object, or takes it away with null. One undo step; null when the element
        /// already has it. The parent must be another object, attached object or row of objects of the tile or of a tile next to it,
        /// as OMSI keeps the reach of a [varparent] to one tile. Not in a chrono variant, whose change lists have no such command.
        /// </summary>
        /// <param name="element">An <see cref="OmsiObjectRecord"/> or <see cref="OmsiAttachedObjectRecord"/> of <paramref name="tile"/>.</param>
        public OmsiObjectChange SetVarParent(OmsiMapSummary summary, OmsiTileSummary tile, object element, int? parentId)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            var objectRecord = element as OmsiObjectRecord;
            var attachedRecord = element as OmsiAttachedObjectRecord;
            if (objectRecord == null && attachedRecord == null)
                throw new ArgumentException("Pass an object or an attached object.", nameof(element));
            if (objectRecord != null ? !tile.Objects.Contains(objectRecord) : !tile.AttachedObjects.Contains(attachedRecord))
                throw new ArgumentException("The element is not in the tile.", nameof(element));
            if (EditedVariant != null)
                throw new InvalidOperationException("A chrono variant cannot change variable parents; choose the base map first.");

            var id = objectRecord?.Id ?? attachedRecord.Id;
            var current = objectRecord != null ? objectRecord.VarParentId : attachedRecord.VarParentId;
            if (current == parentId)
                return null;
            if (parentId.HasValue)
            {
                if (parentId.Value == id)
                    throw new ArgumentException("An element cannot be its own variable parent.", nameof(parentId));
                if (!ParentReachableFrom(summary, tile, parentId.Value))
                    throw new ArgumentException("No object, attached object or row of objects with ID " + parentId.Value +
                                                " lies in tile " + tile.X + ", " + tile.Y + " or next to it.", nameof(parentId));
            }

            var workspace = RequireWorkspace();
            var path = OmsiRelativePath.Normalize(tile.FileName);
            var before = workspace.ReadAllBytes(path);
            var file = OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance);
            if (file.Version >= 0 && file.Version < FirstVersionWithVarParent)
                throw new InvalidOperationException("Tile " + tile.X + ", " + tile.Y + " has version " + file.Version +
                                                    ", which writes a [varparent] in a form MapWorks does not know.");
            var record = FindElementRecord(file, id, attachedRecord != null, path);
            var at = file.Records.IndexOf(record);
            // The record belongs to the element and goes right after it, where the stock maps write it.
            for (var index = at + 1; index < file.Records.Count && RuleSections.Contains(file.Records[index].Name);)
                if (string.Equals(file.Records[index].Name, "varparent", StringComparison.OrdinalIgnoreCase))
                    file.RemoveRecord(file.Records[index]);
                else
                    index++;
            if (parentId.HasValue)
                file.InsertRecord(at + 1, file.CreateRecord("varparent", new[] { parentId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            var after = file.ToBytes();
            if (before.SequenceEqual(after))
                return null;

            var root = RootObjectOf(tile, element);
            var listIndex = root == null ? -1 : tile.Objects.IndexOf(root);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.VarParent, tile, root, listIndex < 0 ? tile.Objects.Count : listIndex)
            {
                ElementId = id,
                ElementBefore = element,
                ElementAfter = element,
                VarParentBefore = current,
                VarParentAfter = parentId
            };
            AddFile(change, path, before, after);
            WriteFiles(change, false);
            ApplyElementChange(change, true);
            return change;
        }

        /// <summary>The element a variable parent would name, found in the tile or in a tile next to it; null when there is none.</summary>
        public object FindVarParent(OmsiMapSummary summary, OmsiTileSummary tile, int parentId)
        {
            if (summary == null || tile == null)
                return null;
            foreach (var candidate in summary.Tiles)
            {
                if (Math.Abs(candidate.X - tile.X) > 1 || Math.Abs(candidate.Y - tile.Y) > 1)
                    continue;
                var found = candidate.Objects.Find(item => item.Id == parentId);
                if (found != null)
                    return found;
                var attached = candidate.AttachedObjects.Find(item => item.Id == parentId);
                if (attached != null)
                    return attached;
                var row = candidate.SplineAttachments.Find(item => item.Id == parentId);
                if (row != null)
                    return row;
            }
            return null;
        }

        private bool ParentReachableFrom(OmsiMapSummary summary, OmsiTileSummary tile, int parentId)
        {
            return FindVarParent(summary, tile, parentId) != null;
        }

        /// <summary>The record of an object or an attached object in a tile file.</summary>
        private static OmsiRecord FindElementRecord(OmsiRecordFile file, int id, bool attached, string path)
        {
            if (!attached)
                return FindObject(file, id, path).Record;
            foreach (var candidate in OmsiTileRecords.Of<OmsiAttachedObjectView>(file))
                if (candidate.Id == id)
                    return candidate.Record;
            throw new InvalidOperationException("Attached object " + id + " was not found in " + path + ".");
        }
    }
}
