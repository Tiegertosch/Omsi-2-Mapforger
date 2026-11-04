using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.World
{
    /// <summary>
    /// Placements by tile: each tile keeps the instances its objects, attached objects and rows give each kind until its
    /// records, the terrain under its objects or the kinds it waits for change, so an edit builds only the tiles it
    /// touched again. Each kind draws its tiles' instances in map order, objects before rows as before, and leaves out
    /// tiles whose box is outside the camera's view.
    /// </summary>
    public sealed partial class MapObjectRenderer
    {
        /// <summary>The instances one tile gives a kind, from its objects and attached objects or from its rows along splines.</summary>
        private sealed class Segment
        {
            public KindData Kind;
            public TilePlacements Owner;
            public bool Row;
            public Matrix4x4[] Instances = new Matrix4x4[0];
            public Matrix4x4[] CrossInstances = new Matrix4x4[0];
            public int Count;
        }

        /// <summary>A placed object, attached object or first object of a row that has attachment points, with its frame.</summary>
        private sealed class AttachParent
        {
            public OmsiTileSummary Tile;
            public int Id;
            public string AssetPath;
            public Matrix4x4 Frame;
            public OmsiObjectModel Model;
        }

        /// <summary>What one tile puts on the map, and what it was built from.</summary>
        private sealed class TilePlacements
        {
            public OmsiTileSummary Tile;
            public readonly Snapshot Records = new Snapshot();
            public readonly List<Segment> Objects = new List<Segment>();
            public readonly List<Segment> Rows = new List<Segment>();

            /// <summary>The tile's records in <see cref="instanceOf"/>.</summary>
            public readonly List<OmsiObjectRecord> Placed = new List<OmsiObjectRecord>();

            public readonly List<AttachParent> AttachParents = new List<AttachParent>();

            /// <summary>The terrain tiles its objects stand on.</summary>
            public readonly HashSet<long> GroundTiles = new HashSet<long>();

            /// <summary>Kinds its objects use that are not read yet; the tile is built again once they are.</summary>
            public readonly HashSet<string> MissingKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public Bounds ObjectBounds;
            public bool HasObjectBounds;
            public Bounds RowBounds;
            public bool HasRowBounds;

            /// <summary>Built again at the next update, such as after a drag previewed its objects elsewhere.</summary>
            public bool Stale = true;

            public bool Visible;
            public int Pass;
        }

        /// <summary>
        /// The values something was built from, kept by value and by reference; describing them again tells whether one
        /// changed, without a hash that two different states could share.
        /// </summary>
        private sealed class Snapshot
        {
            private object[] references = new object[16];
            private double[] values = new double[16];
            private int referenceCount = -1;
            private int valueCount;
            private int referenceAt;
            private int valueAt;
            private bool writing;
            private bool differs;

            /// <summary>Starts comparing with the kept values, or with <paramref name="write"/> keeping new ones.</summary>
            public void Begin(bool write)
            {
                writing = write;
                differs = !write && referenceCount < 0;
                referenceAt = 0;
                valueAt = 0;
            }

            public void Add(object reference)
            {
                if (writing)
                {
                    if (referenceAt == references.Length)
                        Array.Resize(ref references, references.Length * 2);
                    references[referenceAt++] = reference;
                }
                else if (!differs)
                    differs = referenceAt >= referenceCount || !ReferenceEquals(references[referenceAt++], reference);
            }

            public void Add(double value)
            {
                if (writing)
                {
                    if (valueAt == values.Length)
                        Array.Resize(ref values, values.Length * 2);
                    values[valueAt++] = value;
                }
                else if (!differs)
                    differs = valueAt >= valueCount || values[valueAt++] != value;
            }

            /// <summary>After comparing, true when everything was the same; after writing, the new values are kept.</summary>
            public bool End()
            {
                if (!writing)
                    return !differs && referenceAt == referenceCount && valueAt == valueCount;
                if (referenceCount > referenceAt)
                    Array.Clear(references, referenceAt, referenceCount - referenceAt);
                referenceCount = referenceAt;
                valueCount = valueAt;
                writing = false;
                return true;
            }
        }

        private readonly Dictionary<OmsiTileSummary, TilePlacements> tilePlacements = new Dictionary<OmsiTileSummary, TilePlacements>();

        // Objects along splines by tile, laid out again only when splines or rows changed.
        private Dictionary<OmsiTileSummary, List<KeyValuePair<string, OmsiPlacedAttachment>>> rowsByTile;
        private Snapshot rowRecords = new Snapshot();

        // Kinds uploaded and terrain read again since the last update; the tiles that wait for them are built again.
        private readonly HashSet<string> addedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> changedGround = new HashSet<long>();

        // Each path text looked up once per update; comparing without case costs much more in Mono.
        private readonly Dictionary<string, KindData> kindOfText = new Dictionary<string, KindData>(StringComparer.Ordinal);
        private readonly HashSet<string> unrequestedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<KindData, Segment> segmentOf = new Dictionary<KindData, Segment>();
        private readonly Dictionary<int, KeyValuePair<Matrix4x4, OmsiObjectModel>> framesById = new Dictionary<int, KeyValuePair<Matrix4x4, OmsiObjectModel>>();
        private readonly List<TilePlacements> rebuild = new List<TilePlacements>();
        private readonly List<OmsiTileSummary> goneTiles = new List<OmsiTileSummary>();
        private readonly Plane[] frustum = new Plane[6];
        private int placementPass;

        /// <summary>Builds the placements of a tile again at the next refresh, as after an edit of one of its objects.</summary>
        public void InvalidateTile(OmsiTileSummary tile)
        {
            if (tile != null && tilePlacements.TryGetValue(tile, out var placements))
                placements.Stale = true;
        }

        private void ClearPlacements()
        {
            tilePlacements.Clear();
            rowsByTile = null;
            rowFirsts = null;
            rowRecords = new Snapshot();
            addedKinds.Clear();
            changedGround.Clear();
            instanceOf.Clear();
        }

        /// <summary>
        /// Instance matrices for every placement: position on the map, standing on the terrain unless absolute, turned
        /// clockwise by the heading as OMSI does. Only tiles that changed are built again. Unknown kinds, such as newly
        /// placed ones, are read first.
        /// </summary>
        private void UpdatePlacements()
        {
            kindOfText.Clear();
            unrequestedKinds.Clear();
            var rowsLaid = LayRows();

            // Records leave instanceOf before any tile adds its own, as an object may have moved between two changed tiles.
            placementPass++;
            rebuild.Clear();
            foreach (var tile in summary.Tiles)
            {
                if (!tilePlacements.TryGetValue(tile, out var placements))
                    tilePlacements[tile] = placements = new TilePlacements { Tile = tile };
                placements.Pass = placementPass;
                List<KeyValuePair<int, KeyValuePair<string, OmsiPlacedAttachment>>> firsts = null;
                rowFirsts?.TryGetValue(tile, out firsts);
                if (!placements.Stale && !WaitsFor(placements))
                {
                    placements.Records.Begin(false);
                    DescribeTile(placements.Records, tile, firsts);
                    if (placements.Records.End())
                        continue;
                }
                foreach (var item in placements.Placed)
                    instanceOf.Remove(item);
                rebuild.Add(placements);
            }
            goneTiles.Clear();
            foreach (var pair in tilePlacements)
                if (pair.Value.Pass != placementPass)
                    goneTiles.Add(pair.Key);
            foreach (var tile in goneTiles)
            {
                foreach (var item in tilePlacements[tile].Placed)
                    instanceOf.Remove(item);
                tilePlacements.Remove(tile);
            }

            foreach (var placements in rebuild)
            {
                List<KeyValuePair<int, KeyValuePair<string, OmsiPlacedAttachment>>> firsts = null;
                rowFirsts?.TryGetValue(placements.Tile, out firsts);
                BuildTile(placements, firsts);
                placements.Records.Begin(true);
                DescribeTile(placements.Records, placements.Tile, firsts);
                placements.Records.End();
            }
            var rowsBuilt = rowsLaid || addedKinds.Count > 0;
            if (rowsBuilt)
                BuildRows();
            if (rebuild.Count > 0 || goneTiles.Count > 0 || rowsBuilt)
                Assemble();
            addedKinds.Clear();
            changedGround.Clear();

            if (unrequestedKinds.Count > 0 && loading == null)
            {
                var missing = new List<string>(unrequestedKinds);
                requestedKinds.UnionWith(missing);
                loadingGeneration = generation;
                var root = installationRoot;
                loading = System.Threading.Tasks.Task.Run(() =>
                {
                    var data = new LoadData();
                    ReadKinds(missing, root, data);
                    return data;
                });
                placementsDirty = true;
            }
        }

        /// <summary>True when the terrain under a tile's objects was read again or a kind it waits for arrived.</summary>
        private bool WaitsFor(TilePlacements placements)
        {
            if (changedGround.Count > 0)
                foreach (var key in placements.GroundTiles)
                    if (changedGround.Contains(key))
                        return true;
            if (addedKinds.Count > 0)
                foreach (var assetPath in placements.MissingKinds)
                    if (addedKinds.Contains(assetPath))
                        return true;
            return false;
        }

        /// <summary>Everything a tile's object instances depend on besides terrain and kinds.</summary>
        private static void DescribeTile(Snapshot snapshot, OmsiTileSummary tile,
            List<KeyValuePair<int, KeyValuePair<string, OmsiPlacedAttachment>>> firsts)
        {
            snapshot.Add(tile.X);
            snapshot.Add(tile.Y);
            snapshot.Add(tile.Objects.Count);
            foreach (var item in tile.Objects)
            {
                snapshot.Add(item);
                snapshot.Add(item.AssetPath);
                snapshot.Add(item.Id);
                snapshot.Add(item.LocalX);
                snapshot.Add(item.LocalY);
                snapshot.Add(item.Height);
                snapshot.Add(item.HeadingDegrees);
                snapshot.Add(item.PitchDegrees);
                snapshot.Add(item.BankDegrees);
            }
            snapshot.Add(tile.AttachedObjects.Count);
            foreach (var attached in tile.AttachedObjects)
            {
                snapshot.Add(attached);
                snapshot.Add(attached.AssetPath);
                snapshot.Add(attached.Id);
                snapshot.Add(attached.ParentId);
                snapshot.Add(attached.AttachPointIndex);
                snapshot.Add(attached.HeadingDegrees);
                snapshot.Add(attached.PitchDegrees);
                snapshot.Add(attached.BankDegrees);
            }
            snapshot.Add(firsts?.Count ?? 0);
            if (firsts == null)
                return;
            foreach (var first in firsts)
            {
                var placed = first.Value.Value;
                snapshot.Add(first.Key);
                snapshot.Add(first.Value.Key);
                snapshot.Add(placed.WorldX);
                snapshot.Add(placed.WorldY);
                snapshot.Add(placed.Height);
                snapshot.Add(placed.HeadingDegrees);
            }
        }

        /// <summary>Everything the layout of rows along splines depends on: the splines and the row records of all tiles.</summary>
        private static void DescribeRows(Snapshot snapshot, OmsiMapSummary map)
        {
            snapshot.Add(map.Tiles.Count);
            foreach (var tile in map.Tiles)
            {
                snapshot.Add(tile);
                snapshot.Add(tile.X);
                snapshot.Add(tile.Y);
                snapshot.Add(tile.Splines.Count);
                foreach (var spline in tile.Splines)
                {
                    snapshot.Add(spline);
                    snapshot.Add(spline.Id);
                    snapshot.Add(spline.PreviousId);
                    snapshot.Add(spline.NextId);
                    snapshot.Add(spline.LocalX);
                    snapshot.Add(spline.LocalY);
                    snapshot.Add(spline.Height);
                    snapshot.Add(spline.EndHeight);
                    snapshot.Add(spline.GradientStartPercent);
                    snapshot.Add(spline.GradientEndPercent);
                    snapshot.Add(spline.HeadingDegrees);
                    snapshot.Add(spline.LengthMeters);
                    snapshot.Add(spline.RadiusMeters);
                    snapshot.Add(spline.IsSplineH ? 1 : 0);
                    snapshot.Add(spline.IsMirrored ? 1 : 0);
                    snapshot.Add(spline.ChainOffset ?? double.MinValue);
                }
                snapshot.Add(tile.SplineAttachments.Count);
                foreach (var row in tile.SplineAttachments)
                {
                    snapshot.Add(row);
                    snapshot.Add(row.AssetPath);
                    snapshot.Add(row.Id);
                    snapshot.Add(row.SplineIndex);
                    snapshot.Add(row.LateralOffset);
                    snapshot.Add(row.Height);
                    snapshot.Add(row.StartDistance);
                    snapshot.Add(row.HeadingDegrees);
                    snapshot.Add(row.Interval);
                    snapshot.Add(row.Range);
                    snapshot.Add(row.IsRepeater ? 1 : 0);
                }
            }
        }

        /// <summary>Lays out the rows along splines when splines or rows changed; true when it did.</summary>
        private bool LayRows()
        {
            if (rowsByTile != null)
            {
                rowRecords.Begin(false);
                DescribeRows(rowRecords, summary);
                if (rowRecords.End())
                    return false;
            }
            rowsByTile = new Dictionary<OmsiTileSummary, List<KeyValuePair<string, OmsiPlacedAttachment>>>();
            rowFirsts = new Dictionary<OmsiTileSummary, List<KeyValuePair<int, KeyValuePair<string, OmsiPlacedAttachment>>>>();
            var layout = new OmsiSplineAttachmentLayout(summary);
            foreach (var tile in summary.Tiles)
                foreach (var row in tile.SplineAttachments)
                {
                    var placedRow = layout.Place(tile, row);
                    if (placedRow.Count > 0)
                    {
                        if (!rowsByTile.TryGetValue(tile, out var rows))
                            rowsByTile[tile] = rows = new List<KeyValuePair<string, OmsiPlacedAttachment>>();
                        foreach (var placed in placedRow)
                            rows.Add(new KeyValuePair<string, OmsiPlacedAttachment>(row.AssetPath, placed));
                    }
                    // A continuation shares its row's ID: only the row's own record carries objects.
                    if (row.IsRepeater || placedRow.Count == 0)
                        continue;
                    if (!rowFirsts.TryGetValue(tile, out var firsts))
                        rowFirsts[tile] = firsts = new List<KeyValuePair<int, KeyValuePair<string, OmsiPlacedAttachment>>>();
                    firsts.Add(new KeyValuePair<int, KeyValuePair<string, OmsiPlacedAttachment>>(row.Id,
                        new KeyValuePair<string, OmsiPlacedAttachment>(row.AssetPath, placedRow[0])));
                }
            rowRecords.Begin(true);
            DescribeRows(rowRecords, summary);
            rowRecords.End();
            return true;
        }

        /// <summary>The kind drawn for a path, or null; a kind not read yet is noted for the tile and read later.</summary>
        private KindData KindOf(string assetPath, HashSet<string> missingKinds)
        {
            if (!kindOfText.TryGetValue(assetPath, out var kind))
            {
                kinds.TryGetValue(assetPath, out kind);
                kindOfText[assetPath] = kind;
                if (kind == null && !requestedKinds.Contains(assetPath))
                    unrequestedKinds.Add(assetPath);
            }
            if (kind == null)
                missingKinds?.Add(assetPath);
            return kind;
        }

        private void BuildTile(TilePlacements placements, List<KeyValuePair<int, KeyValuePair<string, OmsiPlacedAttachment>>> firsts)
        {
            var tile = placements.Tile;
            placements.Objects.Clear();
            placements.Placed.Clear();
            placements.AttachParents.Clear();
            placements.GroundTiles.Clear();
            placements.MissingKinds.Clear();
            placements.HasObjectBounds = false;
            placements.Stale = false;
            segmentOf.Clear();
            framesById.Clear();
            foreach (var item in tile.Objects)
            {
                var kind = KindOf(item.AssetPath, placements.MissingKinds);
                if (kind == null || kind.Mesh == null)
                    continue;
                var frame = PlacementFrame(kind.Model, tile, item.LocalX, item.LocalY, item.Height, item.HeadingDegrees,
                    item.PitchDegrees, item.BankDegrees);
                if (!kind.Model.HasAbsoluteHeight)
                    placements.GroundTiles.Add(TileKey((int)Math.Floor((tile.X * tileSize + item.LocalX) / tileSize),
                        (int)Math.Floor((tile.Y * tileSize + item.LocalY) / tileSize)));
                framesById[item.Id] = new KeyValuePair<Matrix4x4, OmsiObjectModel>(frame, kind.Model);
                RememberAttachParent(placements, item.Id, item.AssetPath, frame, kind.Model);
                var segment = SegmentOf(placements, kind, false);
                instanceOf[item] = new Placement { Kind = kind, Segment = segment, Index = Add(segment, frame), Tile = tile, Frame = frame };
                placements.Placed.Add(item);
            }

            // The first object of a row can carry objects too.
            if (firsts != null)
                foreach (var first in firsts)
                {
                    var rowKind = KindOf(first.Value.Key, null);
                    if (rowKind == null || rowKind.Mesh == null)
                        continue;
                    var placed = first.Value.Value;
                    var position = new Vector3((float)(placed.WorldX - originX), (float)placed.Height, (float)(placed.WorldY - originY));
                    var frame = Matrix4x4.TRS(position, Quaternion.Euler(0f, (float)placed.HeadingDegrees, 0f), Vector3.one);
                    framesById[first.Key] = new KeyValuePair<Matrix4x4, OmsiObjectModel>(frame, rowKind.Model);
                    RememberAttachParent(placements, first.Key, first.Value.Key, frame, rowKind.Model);
                }

            // Attached objects hang at a point of their parent, which comes earlier in the tile; in file order an
            // attached parent is placed before its children.
            foreach (var attached in tile.AttachedObjects)
            {
                var kind = KindOf(attached.AssetPath, placements.MissingKinds);
                if (!framesById.TryGetValue(attached.ParentId, out var parent) || attached.AttachPointIndex < 0 ||
                    attached.AttachPointIndex >= parent.Value.AttachPoints.Count || kind == null || kind.Mesh == null)
                    continue;
                var frame = parent.Key * AttachMatrix(parent.Value.AttachPoints[attached.AttachPointIndex]) *
                            Matrix4x4.Rotate(Orientation(attached.HeadingDegrees, attached.PitchDegrees, attached.BankDegrees));
                framesById[attached.Id] = new KeyValuePair<Matrix4x4, OmsiObjectModel>(frame, kind.Model);
                RememberAttachParent(placements, attached.Id, attached.AssetPath, frame, kind.Model);
                Add(SegmentOf(placements, kind, false), frame);
            }
        }

        /// <summary>The instances of rows along splines, by tile; built again after rows were laid out or kinds arrived.</summary>
        private void BuildRows()
        {
            foreach (var placements in tilePlacements.Values)
            {
                placements.Rows.Clear();
                placements.HasRowBounds = false;
            }
            foreach (var pair in rowsByTile)
            {
                if (!tilePlacements.TryGetValue(pair.Key, out var placements))
                    continue;
                segmentOf.Clear();
                foreach (var row in pair.Value)
                {
                    var kind = KindOf(row.Key, null);
                    if (kind == null || kind.Mesh == null)
                        continue;
                    var placed = row.Value;
                    var position = new Vector3((float)(placed.WorldX - originX), (float)placed.Height, (float)(placed.WorldY - originY));
                    Add(SegmentOf(placements, kind, true), Matrix4x4.TRS(position, Quaternion.Euler(0f, (float)placed.HeadingDegrees, 0f), Vector3.one));
                }
            }
        }

        private void RememberAttachParent(TilePlacements placements, int id, string assetPath, Matrix4x4 frame, OmsiObjectModel model)
        {
            if (model.AttachPoints.Count == 0)
                return;
            placements.AttachParents.Add(new AttachParent { Tile = placements.Tile, Id = id, AssetPath = assetPath, Frame = frame, Model = model });
        }

        /// <summary>The segment of a kind in the tile being built; <see cref="segmentOf"/> holds those of this tile.</summary>
        private Segment SegmentOf(TilePlacements placements, KindData kind, bool row)
        {
            if (segmentOf.TryGetValue(kind, out var segment))
                return segment;
            segment = new Segment { Kind = kind, Owner = placements, Row = row };
            (row ? placements.Rows : placements.Objects).Add(segment);
            segmentOf[kind] = segment;
            return segment;
        }

        /// <summary>Adds an instance standing in this frame and returns its index in the segment.</summary>
        private static int Add(Segment segment, Matrix4x4 frame)
        {
            if (segment.Count == segment.Instances.Length)
            {
                var size = Math.Max(4, segment.Count * 2);
                Array.Resize(ref segment.Instances, size);
                if (segment.Kind.CrossMaterial != null)
                    Array.Resize(ref segment.CrossInstances, size);
            }
            SetFromFrame(segment, segment.Count, frame);
            Grow(segment, frame);
            return segment.Count++;
        }

        /// <summary>Widens the tile's box by a sphere around the kind's model in this frame.</summary>
        private static void Grow(Segment segment, Matrix4x4 frame)
        {
            var kind = segment.Kind;
            if (!kind.Extent.HasValue)
                kind.Extent = LocalBounds(kind);
            var extent = kind.Extent.Value;
            var size = extent.extents.magnitude * 2f;
            var box = new Bounds(frame.MultiplyPoint3x4(extent.center), new Vector3(size, size, size));
            var owner = segment.Owner;
            if (segment.Row)
            {
                if (owner.HasRowBounds)
                    owner.RowBounds.Encapsulate(box);
                else
                    owner.RowBounds = box;
                owner.HasRowBounds = true;
            }
            else
            {
                if (owner.HasObjectBounds)
                    owner.ObjectBounds.Encapsulate(box);
                else
                    owner.ObjectBounds = box;
                owner.HasObjectBounds = true;
            }
        }

        private static void SetFromFrame(Segment segment, int index, Matrix4x4 frame)
        {
            segment.Instances[index] = InstanceMatrix(segment.Kind.Model, frame);
            if (segment.Kind.CrossMaterial != null)
                segment.CrossInstances[index] = CrossMatrix(segment.Kind.Model, frame);
        }

        /// <summary>Gives every kind its segments in map order: the tiles' objects first, then their rows.</summary>
        private void Assemble()
        {
            foreach (var kind in kinds.Values)
            {
                kind.Segments.Clear();
                kind.Dirty = true;
            }
            foreach (var tile in summary.Tiles)
                if (tilePlacements.TryGetValue(tile, out var placements))
                    foreach (var segment in placements.Objects)
                        segment.Kind.Segments.Add(segment);
            foreach (var tile in summary.Tiles)
                if (tilePlacements.TryGetValue(tile, out var placements))
                    foreach (var segment in placements.Rows)
                        segment.Kind.Segments.Add(segment);
        }

        /// <summary>Marks the kinds of tiles that came into or left the camera's view; called with this frame's camera.</summary>
        private void UpdateVisibility()
        {
            GeometryUtility.CalculateFrustumPlanes(targetCamera, frustum);
            foreach (var placements in tilePlacements.Values)
            {
                var visible = (placements.HasObjectBounds && GeometryUtility.TestPlanesAABB(frustum, placements.ObjectBounds)) ||
                              (placements.HasRowBounds && GeometryUtility.TestPlanesAABB(frustum, placements.RowBounds));
                if (visible == placements.Visible)
                    continue;
                placements.Visible = visible;
                foreach (var segment in placements.Objects)
                    segment.Kind.Dirty = true;
                foreach (var segment in placements.Rows)
                    segment.Kind.Dirty = true;
            }
        }

        /// <summary>Copies the instances of a kind's visible tiles together, in their order, for drawing.</summary>
        private static void Gather(KindData kind)
        {
            kind.Dirty = false;
            var count = 0;
            foreach (var segment in kind.Segments)
                if (segment.Owner.Visible)
                    count += segment.Count;
            if (kind.Instances.Length < count)
                kind.Instances = new Matrix4x4[count];
            if (kind.CrossMaterial != null && kind.CrossInstances.Length < count)
                kind.CrossInstances = new Matrix4x4[count];
            var at = 0;
            foreach (var segment in kind.Segments)
            {
                if (!segment.Owner.Visible)
                    continue;
                Array.Copy(segment.Instances, 0, kind.Instances, at, segment.Count);
                if (kind.CrossMaterial != null)
                    Array.Copy(segment.CrossInstances, 0, kind.CrossInstances, at, segment.Count);
                at += segment.Count;
            }
            kind.InstanceCount = count;
        }
    }
}
