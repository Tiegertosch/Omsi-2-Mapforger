using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Outcome of removing an element from a tile.</summary>
    public sealed class OmsiRemovalResult
    {
        public bool Removed { get; internal set; }

        /// <summary>References that prevented the removal; the files were left unchanged.</summary>
        public List<OmsiReference> Blockers { get; } = new List<OmsiReference>();

        /// <summary>The element and the rule, variable and terrain records that belonged to it.</summary>
        public int RemovedRecordCount { get; internal set; }

        public int ShiftedAttachmentSplineIndices { get; internal set; }
        public int ShiftedEntryPointIndices { get; internal set; }
    }

    /// <summary>
    /// Structural edits of a map tile that keep the tile's position-based references valid: spline indices of
    /// attachments in the same tile and object indices of entry points in global.cfg.
    /// </summary>
    public static class OmsiTileEditing
    {
        /// <summary>Records that modify the element written just before them.</summary>
        private static readonly HashSet<string> ModifierSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rule", "kill_rule", "varparent", "spline_terrain_align", "spline_terrain_align_2"
        };

        private static readonly HashSet<string> PlacedObjectSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "object", "attachObj", "splineAttachement", "splineAttachement_repeater"
        };

        private const int EntryPointFieldCount = 12;

        /// <summary>
        /// Appends a new [object] at the end of the tile, so no existing index changes. The ID comes from the
        /// allocator; apply the allocator to global.cfg before saving it.
        /// </summary>
        public static OmsiObjectView AppendObject(OmsiRecordFile tile, OmsiIdAllocator ids, string assetPath,
            double x, double y, double z, double headingDegrees, IList<string> labels = null)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            if (tile.Version < 9)
                throw new InvalidOperationException("Objects can only be added to tiles of version 9 or later.");
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("An object file path is required.", nameof(assetPath));

            labels = labels ?? new string[0];
            var fields = new List<string>
            {
                "0", assetPath.Trim(), OmsiNumberFormat.Format(ids.Allocate()),
                OmsiNumberFormat.Format(x), OmsiNumberFormat.Format(y), OmsiNumberFormat.Format(z),
                OmsiNumberFormat.Format(headingDegrees), "0", "0",
                labels.Count.ToString(CultureInfo.InvariantCulture)
            };
            fields.AddRange(labels);

            var record = tile.CreateRecord("object", fields);
            tile.InsertRecord(tile.Records.Count, record);
            return (OmsiObjectView)OmsiTileRecords.Create(tile, record);
        }

        /// <summary>
        /// Removes an element and the records that modify it. Nothing changes when other records still refer to the
        /// element; the blocking references are returned instead.
        /// </summary>
        /// <param name="tile">The tile that holds the element.</param>
        /// <param name="element">A view created from a record of <paramref name="tile"/>.</param>
        /// <param name="tileIndex">The tile's position in global.cfg's [map] list.</param>
        /// <param name="globalConfig">global.cfg read with <see cref="OmsiGlobalConfigLayouts"/>; its entry points are updated.</param>
        /// <param name="references">An index built from the current files.</param>
        public static OmsiRemovalResult Remove(OmsiRecordFile tile, OmsiTileRecordView element, int tileIndex,
            OmsiRecordFile globalConfig, OmsiReferenceIndex references)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (globalConfig == null)
                throw new ArgumentNullException(nameof(globalConfig));
            if (references == null)
                throw new ArgumentNullException(nameof(references));

            var position = tile.Records.IndexOf(element.Record);
            if (position < 0)
                throw new ArgumentException("The element does not belong to this tile.", nameof(element));

            var result = new OmsiRemovalResult();
            var isRepeater = element is OmsiSplineAttachmentView attachmentView && attachmentView.IsRepeater;
            if (!isRepeater && TryGetId(element, out var id))
                result.Blockers.AddRange(references.To(id));

            var entryPoints = FindEntryPoints(globalConfig);
            var placedIndex = PlacedIndexOf(tile, position);
            if (placedIndex >= 0 && entryPoints != null)
            {
                for (var field = 1; field + EntryPointFieldCount <= entryPoints.Fields.Count; field += EntryPointFieldCount)
                {
                    if (entryPoints.TryGetInt(field, out var objectIndex) && objectIndex == placedIndex &&
                        entryPoints.TryGetInt(field + 10, out var entryTile) && entryTile == tileIndex &&
                        !ContainsEntryPointBlocker(result, entryPoints, field))
                        result.Blockers.Add(new OmsiReference(placedIndex, "global.cfg", "entry point at object index " + placedIndex));
                }
            }
            if (result.Blockers.Count > 0)
                return result;

            // Index maintenance first, while the positions still describe the unchanged tile.
            if (element is OmsiSplineView)
                result.ShiftedAttachmentSplineIndices = ShiftAttachmentSplineIndices(tile, SplineIndexOf(tile, position));
            if (placedIndex >= 0 && entryPoints != null)
                result.ShiftedEntryPointIndices = ShiftEntryPoints(entryPoints, tileIndex, placedIndex);

            var count = 1;
            while (position + count < tile.Records.Count && ModifierSections.Contains(tile.Records[position + count].Name))
                count++;
            for (var removed = 0; removed < count; removed++)
                tile.RemoveRecord(tile.Records[position]);

            result.RemovedRecordCount = count;
            result.Removed = true;
            return result;
        }

        private static bool ContainsEntryPointBlocker(OmsiRemovalResult result, OmsiRecord entryPoints, int field)
        {
            // An entry point that names the object by ID is already reported by the reference index.
            return entryPoints.TryGetInt(field + 1, out var objectId) &&
                   result.Blockers.Exists(reference => reference.TargetId == objectId && reference.Kind == "entry point");
        }

        private static bool TryGetId(OmsiTileRecordView element, out int id)
        {
            switch (element)
            {
                case OmsiObjectView item:
                    id = item.Id;
                    return true;
                case OmsiAttachedObjectView attached:
                    id = attached.Id;
                    return true;
                case OmsiSplineAttachmentView attachment:
                    id = attachment.Id;
                    return true;
                case OmsiSplineView spline:
                    id = spline.Id;
                    return true;
                default:
                    id = 0;
                    return false;
            }
        }

        /// <summary>Position among placed objects (objects, attachments and repeaters), or -1 for other records.</summary>
        private static int PlacedIndexOf(OmsiRecordFile tile, int position)
        {
            if (!PlacedObjectSections.Contains(tile.Records[position].Name))
                return -1;
            var index = 0;
            for (var record = 0; record < position; record++)
                if (PlacedObjectSections.Contains(tile.Records[record].Name))
                    index++;
            return index;
        }

        private static int SplineIndexOf(OmsiRecordFile tile, int position)
        {
            var index = 0;
            for (var record = 0; record < position; record++)
                if (IsSpline(tile.Records[record]))
                    index++;
            return index;
        }

        private static bool IsSpline(OmsiRecord record)
        {
            return string.Equals(record.Name, "spline", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(record.Name, "spline_h", StringComparison.OrdinalIgnoreCase);
        }

        private static int ShiftAttachmentSplineIndices(OmsiRecordFile tile, int removedSplineIndex)
        {
            var shifted = 0;
            foreach (var attachment in OmsiTileRecords.Of<OmsiSplineAttachmentView>(tile))
            {
                if (attachment.SplineIndex > removedSplineIndex)
                {
                    attachment.SplineIndex--;
                    shifted++;
                }
            }
            return shifted;
        }

        private static OmsiRecord FindEntryPoints(OmsiRecordFile globalConfig)
        {
            foreach (var record in globalConfig.Records)
            {
                if (!string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!record.HasKnownLayout || !record.IsComplete)
                    throw new InvalidOperationException("Read global.cfg with OmsiGlobalConfigLayouts before editing tiles.");
                return record;
            }
            return null;
        }

        private static int ShiftEntryPoints(OmsiRecord entryPoints, int tileIndex, int removedPlacedIndex)
        {
            var shifted = 0;
            for (var field = 1; field + EntryPointFieldCount <= entryPoints.Fields.Count; field += EntryPointFieldCount)
            {
                if (entryPoints.TryGetInt(field + 10, out var entryTile) && entryTile == tileIndex &&
                    entryPoints.TryGetInt(field, out var objectIndex) && objectIndex > removedPlacedIndex)
                {
                    entryPoints.SetField(field, OmsiNumberFormat.Format(objectIndex - 1));
                    shifted++;
                }
            }
            return shifted;
        }
    }
}
