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

        /// <summary>Entry points that followed a moved object into its new tile.</summary>
        public int MovedEntryPoints { get; internal set; }

        /// <summary>Attached objects that moved along with their parent.</summary>
        public int MovedAttachedObjects { get; internal set; }
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

        internal static readonly HashSet<string> PlacedObjectSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "object", "attachObj", "splineAttachement", "splineAttachement_repeater"
        };

        /// <summary>Chrono commands that change the element selected or added just before them.</summary>
        private static readonly HashSet<string> ChronoCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "delete", "typ", "relabel", "rule", "kill_rule"
        };

        private const int EntryPointFieldCount = 12;

        /// <summary>
        /// An entry point lies at most about 1 m beside its object in real maps (TileRecordInventory); one further away is
        /// stale and is put back on the object when it moves.
        /// </summary>
        private const double EntryPointReachMeters = 2.0;

        /// <summary>An entry point whose angle differs more from its object's heading is stale and takes the heading.</summary>
        private const double EntryPointTurnToleranceDegrees = 10.0;

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
        /// Appends a new [attachObj] at the end of the tile, after its parent as OMSI needs it, so no existing index
        /// changes; it hangs on attachment point <paramref name="pointIndex"/> of the parent, turned by the heading. A row
        /// along a road ([splineAttachement]) can be the parent too: the object hangs on its first object, as openOMSI and
        /// Omsi.exe read it (instance 0; later objects of a row are refused). The ID comes from the allocator; apply the
        /// allocator to global.cfg before saving it.
        /// </summary>
        public static OmsiAttachedObjectView AppendAttachedObject(OmsiRecordFile tile, OmsiIdAllocator ids, string assetPath, int parentId, int pointIndex,
            double headingDegrees)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            if (tile.Version < 12)
                throw new InvalidOperationException("Objects can only be attached in tiles of version 12 or later.");
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("An object file path is required.", nameof(assetPath));
            if (pointIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pointIndex));
            var parentFound = false;
            foreach (var record in tile.Records)
                if (OmsiTileRecords.Create(tile, record) is OmsiTileRecordView view && TryGetId(view, out var id) && id == parentId &&
                    (view is OmsiObjectView || view is OmsiAttachedObjectView || view is OmsiSplineAttachmentView row && !row.IsRepeater))
                    parentFound = true;
            if (!parentFound)
                throw new InvalidOperationException("Object or row " + parentId + " is not in this tile.");

            var fields = new List<string>
            {
                "0", assetPath.Trim(), OmsiNumberFormat.Format(ids.Allocate()), OmsiNumberFormat.Format(parentId), "0",
                pointIndex.ToString(CultureInfo.InvariantCulture), OmsiNumberFormat.Format(headingDegrees), "0", "0", "0"
            };
            var created = tile.CreateRecord("attachObj", fields);
            tile.InsertRecord(tile.Records.Count, created);
            return (OmsiAttachedObjectView)OmsiTileRecords.Create(tile, created);
        }

        /// <summary>
        /// Removes an object or attached object together with the attached objects hanging on it, directly or through
        /// another, each with its rules and variables. Their references to each other in this tile do not count; any other
        /// reference to one of them keeps all, and is returned instead.
        /// </summary>
        /// <param name="tilePath">The tile's relative path, to tell references from within it.</param>
        public static OmsiRemovalResult RemoveWithFamily(OmsiRecordFile tile, OmsiTileRecordView element, string tilePath, int tileIndex,
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
            var family = Family(tile, element);
            var total = new OmsiRemovalResult();
            foreach (var member in family)
                if (TryGetId(member, out var id))
                    foreach (var reference in references.To(id))
                        if (reference.Kind != "attached object parent" || !string.Equals(OmsiRelativePath.Normalize(reference.File), tilePath, StringComparison.OrdinalIgnoreCase))
                            total.Blockers.Add(reference);
            if (total.Blockers.Count > 0)
                return total;
            // Children first, so every parent is still there while its children go.
            for (var index = family.Count - 1; index >= 0; index--)
            {
                var result = Detach(tile, family[index], tileIndex, globalConfig, new List<OmsiReference>(), null);
                if (!result.Removed)
                    return result;
                total.RemovedRecordCount += result.RemovedRecordCount;
                total.ShiftedEntryPointIndices += result.ShiftedEntryPointIndices;
            }
            total.Removed = true;
            return total;
        }

        /// <summary>
        /// Appends a new [splineAttachement] at the end of the tile, so no existing index changes. The ID comes from the
        /// allocator; apply the allocator to global.cfg before saving it.
        /// </summary>
        public static OmsiSplineAttachmentView AppendSplineAttachment(OmsiRecordFile tile, OmsiIdAllocator ids, OmsiSplineAttachmentRecord row)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            if (tile.Version < 9)
                throw new InvalidOperationException("Objects can only be added to tiles of version 9 or later.");
            if (string.IsNullOrWhiteSpace(row.AssetPath))
                throw new ArgumentException("An object file path is required.", nameof(row));

            var fields = new List<string>
            {
                "0", row.AssetPath.Trim(), OmsiNumberFormat.Format(ids.Allocate()), row.SplineIndex.ToString(CultureInfo.InvariantCulture),
                OmsiNumberFormat.Format(row.LateralOffset), OmsiNumberFormat.Format(row.Height), OmsiNumberFormat.Format(row.StartDistance),
                OmsiNumberFormat.Format(row.HeadingDegrees), "0", "0", OmsiNumberFormat.Format(row.Interval), OmsiNumberFormat.Format(row.Range), "0", "0"
            };
            var record = tile.CreateRecord("splineAttachement", fields);
            tile.InsertRecord(tile.Records.Count, record);
            return (OmsiSplineAttachmentView)OmsiTileRecords.Create(tile, record);
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

            var blockers = new List<OmsiReference>();
            var isRepeater = element is OmsiSplineAttachmentView attachmentView && attachmentView.IsRepeater;
            if (!isRepeater && TryGetId(element, out var id))
                blockers.AddRange(references.To(id));
            return Detach(tile, element, tileIndex, globalConfig, blockers, null);
        }

        /// <summary>
        /// Removes an element and the records that modify it unless <paramref name="blockers"/> name references that
        /// still need it; the caller decides which references to the element it resolves itself, such as the
        /// neighbour IDs of a deleted spline.
        /// </summary>
        internal static OmsiRemovalResult RemoveWithBlockers(OmsiRecordFile tile, OmsiTileRecordView element, int tileIndex,
            OmsiRecordFile globalConfig, IEnumerable<OmsiReference> blockers)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (globalConfig == null)
                throw new ArgumentNullException(nameof(globalConfig));
            return Detach(tile, element, tileIndex, globalConfig, new List<OmsiReference>(blockers ?? new OmsiReference[0]), null);
        }

        /// <summary>The object and every attached object hanging on it, directly or through another, in file order.</summary>
        public static List<OmsiTileRecordView> Family(OmsiRecordFile tile, OmsiTileRecordView item)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (!TryGetId(item, out var id))
                throw new ArgumentException("Only objects and attached objects carry others.", nameof(item));
            var ids = new HashSet<int> { id };
            var family = new List<OmsiTileRecordView> { item };
            // Attached objects stand after their parent in the same tile (TileRecordInventory).
            for (var position = tile.Records.IndexOf(item.Record) + 1; position > 0 && position < tile.Records.Count; position++)
                if (OmsiTileRecords.Create(tile, tile.Records[position]) is OmsiAttachedObjectView attached && ids.Contains(attached.ParentId))
                {
                    ids.Add(attached.Id);
                    family.Add(attached);
                }
            return family;
        }

        /// <summary>
        /// Moves an [object] with its rules, and the attached objects hanging on it, to the end of another tile in their
        /// order and sets the object's position there; attached objects sit on it and need none. IDs stay, so timetables,
        /// bus stops, chrono selections and variable parents keep finding them by ID. Entry points that name one of them by
        /// index follow into the target tile and stay on the object (<see cref="FollowEntryPoints"/>). Nothing changes while
        /// <paramref name="blockers"/> tie the object to its tile, or one of the moved records has a [varparent] whose
        /// parent <paramref name="parentReachable"/> says would be out of reach, or a modifier of unverified layout; those
        /// are returned instead.
        /// </summary>
        /// <param name="source">The tile that holds the object.</param>
        /// <param name="item">A view created from a record of <paramref name="source"/>.</param>
        /// <param name="sourceTileIndex">The source tile's position in global.cfg's [map] list.</param>
        /// <param name="target">The tile that receives the object.</param>
        /// <param name="targetTileIndex">The target tile's position in global.cfg's [map] list.</param>
        /// <param name="x">Position in the target tile.</param>
        /// <param name="y">Position in the target tile.</param>
        public static OmsiRemovalResult MoveObjectToTile(OmsiRecordFile source, OmsiObjectView item, int sourceTileIndex,
            OmsiRecordFile target, int targetTileIndex, double x, double y, OmsiRecordFile globalConfig, IEnumerable<OmsiReference> blockers,
            Func<int, bool> parentReachable)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (ReferenceEquals(source, target))
                throw new ArgumentException("The target must be another tile.", nameof(target));
            if (globalConfig == null)
                throw new ArgumentNullException(nameof(globalConfig));
            if (target.Version < 9)
                throw new InvalidOperationException("Objects can only be moved into tiles of version 9 or later.");

            var family = Family(source, item);
            var result = new OmsiRemovalResult();
            result.Blockers.AddRange(blockers ?? new OmsiReference[0]);
            foreach (var member in family)
            {
                TryGetId(member, out var memberId);
                for (var next = source.Records.IndexOf(member.Record) + 1; next < source.Records.Count && ModifierSections.Contains(source.Records[next].Name); next++)
                {
                    var modifier = source.Records[next];
                    if (string.Equals(modifier.Name, "varparent", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!modifier.TryGetInt(0, out var parent) || parentReachable == null || !parentReachable(parent))
                            result.Blockers.Add(new OmsiReference(memberId, "its tile", "variable parent of the object out of reach"));
                    }
                    else if (!modifier.HasKnownLayout || !modifier.IsComplete)
                        result.Blockers.Add(new OmsiReference(memberId, "its tile", "[" + modifier.Name + "] without verified layout"));
                }
            }
            if (result.Blockers.Count > 0)
                return result;

            // Entry points follow while the indices still describe both tiles unchanged: the moved records come after
            // everything placed in the target, in their order.
            var entryPoints = FindEntryPoints(globalConfig);
            if (entryPoints != null)
            {
                var placedInTarget = 0;
                foreach (var record in target.Records)
                    if (PlacedObjectSections.Contains(record.Name))
                        placedInTarget++;
                for (var member = 0; member < family.Count; member++)
                {
                    var placed = PlacedIndexOf(source, source.Records.IndexOf(family[member].Record));
                    for (var field = 1; field + EntryPointFieldCount <= entryPoints.Fields.Count; field += EntryPointFieldCount)
                    {
                        if (!entryPoints.TryGetInt(field + 10, out var entryTile) || entryTile != sourceTileIndex ||
                            !entryPoints.TryGetInt(field, out var objectIndex) || objectIndex != placed)
                            continue;
                        entryPoints.SetField(field, OmsiNumberFormat.Format(placedInTarget + member));
                        entryPoints.SetField(field + 10, OmsiNumberFormat.Format(targetTileIndex));
                        // Its position is counted in the tile it names: from the object in the old tile to the object in the new one.
                        FollowObject(entryPoints, field, item.X, item.Y, item.Z, item.HeadingDegrees, x, y, item.Z, item.HeadingDegrees);
                        result.MovedEntryPoints++;
                    }
                }
            }

            // Taken out from the last, so the positions of those before stay; each takes its rules along.
            var moved = new List<List<OmsiRecord>>();
            for (var member = family.Count - 1; member >= 0; member--)
            {
                var records = new List<OmsiRecord>();
                var detached = Detach(source, family[member], sourceTileIndex, globalConfig, new List<OmsiReference>(), records);
                if (!detached.Removed)
                    throw new InvalidOperationException("Element " + family[member].Record.Name + " could not be taken out of its tile.");
                result.ShiftedEntryPointIndices += detached.ShiftedEntryPointIndices;
                result.RemovedRecordCount += detached.RemovedRecordCount;
                moved.Insert(0, records);
            }

            // Copies with the target's line endings; the free text after a record, such as "Object Nr. 5", stays behind.
            var first = target.Records.Count;
            foreach (var records in moved)
                foreach (var record in records)
                {
                    var fields = new List<string>();
                    foreach (var field in record.Fields)
                        fields.Add(field.Text);
                    target.InsertRecord(target.Records.Count, target.CreateRecord(record.Name, fields));
                }
            var view = (OmsiObjectView)OmsiTileRecords.Create(target, target.Records[first]);
            view.X = x;
            view.Y = y;
            result.MovedAttachedObjects = family.Count - 1;
            result.Removed = true;
            return result;
        }

        /// <summary>
        /// Keeps the entry points that name an object on it after it moved or turned within its tile, as OMSI starts the bus
        /// at the entry point's own position and angle. Returns how many changed.
        /// </summary>
        /// <param name="item">The object with its new values, a view created from a record of <paramref name="tile"/>.</param>
        /// <param name="tileIndex">The tile's position in global.cfg's [map] list.</param>
        public static int FollowEntryPoints(OmsiRecordFile tile, OmsiObjectView item, int tileIndex, OmsiRecordFile globalConfig, double oldX, double oldY,
            double oldZ, double oldHeadingDegrees)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (globalConfig == null)
                throw new ArgumentNullException(nameof(globalConfig));
            var entryPoints = FindEntryPoints(globalConfig);
            if (entryPoints == null)
                return 0;
            var placed = PlacedIndexOf(tile, tile.Records.IndexOf(item.Record));
            var followed = 0;
            for (var field = 1; field + EntryPointFieldCount <= entryPoints.Fields.Count; field += EntryPointFieldCount)
            {
                if (!entryPoints.TryGetInt(field + 10, out var entryTile) || entryTile != tileIndex ||
                    !entryPoints.TryGetInt(field, out var objectIndex) || objectIndex != placed)
                    continue;
                FollowObject(entryPoints, field, oldX, oldY, oldZ, oldHeadingDegrees, item.X, item.Y, item.Z, item.HeadingDegrees);
                followed++;
            }
            return followed;
        }

        /// <summary>
        /// Moves one entry point as its object moved: it keeps its place beside the object, turned with it, and its angle
        /// relative to the object's heading; one stale by more than <see cref="EntryPointReachMeters"/> or
        /// <see cref="EntryPointTurnToleranceDegrees"/> goes onto the object and takes its heading.
        /// </summary>
        private static void FollowObject(OmsiRecord entryPoints, int field, double oldX, double oldY, double oldZ, double oldHeading, double newX, double newY,
            double newZ, double newHeading)
        {
            if (!entryPoints.TryGetDouble(field + 3, out var entryX) || !entryPoints.TryGetDouble(field + 4, out var entryZ) ||
                !entryPoints.TryGetDouble(field + 5, out var entryY))
                return;
            var turn = (newHeading - oldHeading) * Math.PI / 180.0;
            var offsetX = entryX - oldX;
            var offsetY = entryY - oldY;
            if (offsetX * offsetX + offsetY * offsetY > EntryPointReachMeters * EntryPointReachMeters)
            {
                offsetX = 0.0;
                offsetY = 0.0;
            }
            // Headings turn clockwise from north, so the offset turns with them.
            entryPoints.SetField(field + 3, OmsiNumberFormat.Format(newX + offsetX * Math.Cos(turn) + offsetY * Math.Sin(turn)));
            entryPoints.SetField(field + 5, OmsiNumberFormat.Format(newY - offsetX * Math.Sin(turn) + offsetY * Math.Cos(turn)));
            entryPoints.SetField(field + 4, OmsiNumberFormat.Format(entryZ + newZ - oldZ));

            // The angle is a quaternion about the vertical axis, 2 atan2(y, w) the heading (TileRecordInventory).
            if (!entryPoints.TryGetDouble(field + 6, out var qx) || !entryPoints.TryGetDouble(field + 7, out var qy) ||
                !entryPoints.TryGetDouble(field + 8, out var qz) || !entryPoints.TryGetDouble(field + 9, out var qw) ||
                Math.Abs(qx) > 1e-6 || Math.Abs(qz) > 1e-6)
                return;
            var angle = 2.0 * Math.Atan2(qy, qw) * 180.0 / Math.PI;
            var relative = OmsiRoadPlanner.Normalize(angle - oldHeading);
            var newAngle = Math.Abs(relative) > EntryPointTurnToleranceDegrees ? newHeading : newHeading + relative;
            var half = newAngle * Math.PI / 360.0;
            entryPoints.SetField(field + 7, OmsiNumberFormat.Format(Math.Sin(half)));
            entryPoints.SetField(field + 9, OmsiNumberFormat.Format(Math.Cos(half)));
        }

        /// <summary>
        /// Moves chrono selections of elements from one chrono tile file to another (TimetableChronoInventory): each
        /// [selobject] naming one of the IDs with the commands after it, up to the next selection or new element, goes to
        /// the end of <paramref name="to"/> in its order. Returns how many selections moved.
        /// </summary>
        public static int MoveChronoSelections(OmsiRecordFile from, OmsiRecordFile to, ICollection<int> ids)
        {
            if (from == null)
                throw new ArgumentNullException(nameof(from));
            if (to == null)
                throw new ArgumentNullException(nameof(to));
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            var blocks = new List<OmsiRecord>();
            var count = 0;
            for (var position = 0; position < from.Records.Count; position++)
            {
                var record = from.Records[position];
                if (!string.Equals(record.Name, "selobject", StringComparison.OrdinalIgnoreCase) || !record.TryGetInt(0, out var id) || !ids.Contains(id))
                    continue;
                count++;
                blocks.Add(record);
                while (position + 1 < from.Records.Count && ChronoCommands.Contains(from.Records[position + 1].Name))
                    blocks.Add(from.Records[++position]);
            }
            foreach (var record in blocks)
            {
                from.RemoveRecord(record);
                var fields = new List<string>();
                foreach (var field in record.Fields)
                    fields.Add(field.Text);
                to.InsertRecord(to.Records.Count, to.CreateRecord(record.Name, fields));
            }
            return count;
        }

        /// <summary>
        /// Removes an element and the records that modify it unless there are blockers or an entry point names it by
        /// index. Index maintenance happens first, while the positions still describe the unchanged tile.
        /// </summary>
        private static OmsiRemovalResult Detach(OmsiRecordFile tile, OmsiTileRecordView element, int tileIndex,
            OmsiRecordFile globalConfig, List<OmsiReference> blockers, List<OmsiRecord> removedRecords)
        {
            var position = tile.Records.IndexOf(element.Record);
            if (position < 0)
                throw new ArgumentException("The element does not belong to this tile.", nameof(element));

            var result = new OmsiRemovalResult();
            result.Blockers.AddRange(blockers);

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

            if (element is OmsiSplineView)
                result.ShiftedAttachmentSplineIndices = ShiftAttachmentSplineIndices(tile, SplineIndexOf(tile, position));
            if (placedIndex >= 0 && entryPoints != null)
                result.ShiftedEntryPointIndices = ShiftEntryPoints(entryPoints, tileIndex, placedIndex);

            var count = 1;
            while (position + count < tile.Records.Count && ModifierSections.Contains(tile.Records[position + count].Name))
                count++;
            for (var removed = 0; removed < count; removed++)
            {
                removedRecords?.Add(tile.Records[position]);
                tile.RemoveRecord(tile.Records[position]);
            }

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
