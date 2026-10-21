using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>A finished drag of an object: its new position and heading in tile-local values.</summary>
    internal sealed class MapObjectEdit
    {
        public OmsiTileSummary Tile;
        public OmsiObjectRecord Record;
        public double LocalX;
        public double LocalY;
        public double HeadingDegrees;
    }

    /// <summary>A click while placing: the tile under the pointer (null outside the map) and the tile-local position.</summary>
    internal sealed class MapPlacement
    {
        public OmsiTileSummary Tile;
        public double LocalX;
        public double LocalY;

        /// <summary>Shift was held, so placing should continue with the same object.</summary>
        public bool KeepPlacing;
    }

    /// <summary>
    /// Continuous top-down view of a whole map in world metres. Pans and zooms, draws tiles, splines and objects that
    /// are visible, selects objects and, when editing is allowed, moves and rotates them by dragging. In placement
    /// mode a preview of the chosen object follows the pointer and a click reports where it should go.
    /// </summary>
    internal sealed class MapCanvasElement : VisualElement
    {
        private const float PickRadius = 8f;
        private const float HandleDistance = 30f;
        private const float HandleRadius = 6f;
        private const float DragThreshold = 4f;
        private const int QuadsPerMesh = 10000;

        private static readonly Color TileColor = new Color(0.115f, 0.14f, 0.165f, 1f);
        private static readonly Color MissingTileColor = new Color(0.28f, 0.13f, 0.14f, 1f);
        private static readonly Color TileBorderColor = new Color(0.25f, 0.31f, 0.37f, 1f);
        private static readonly Color SelectedTileColor = new Color(0.88f, 0.69f, 0.33f, 1f);
        private static readonly Color SplineColor = new Color(0.95f, 0.71f, 0.30f, 0.95f);
        private static readonly Color32 ObjectColor = new Color(0.56f, 0.82f, 0.94f, 0.95f);
        private static readonly Color HeadingColor = new Color(0.56f, 0.82f, 0.94f, 0.55f);
        private static readonly Color SelectedObjectColor = new Color(1f, 0.36f, 0.42f, 1f);
        private static readonly Color OutlineFillColor = new Color(0.42f, 0.55f, 0.66f, 0.38f);
        private static readonly Color OutlineStrokeColor = new Color(0.62f, 0.78f, 0.9f, 0.85f);
        private static readonly Color TreeFillColor = new Color(0.32f, 0.62f, 0.36f, 0.32f);
        private static readonly Color TreeStrokeColor = new Color(0.45f, 0.78f, 0.5f, 0.7f);
        private static readonly Color PlacementColor = new Color(0.47f, 0.9f, 0.6f, 1f);
        private static readonly Color PlacementFillColor = new Color(0.47f, 0.9f, 0.6f, 0.25f);

        /// <summary>Outlines are drawn from this scale on; further out the map shows markers only.</summary>
        private const float OutlineMetersPerPixel = 1.5f;

        /// <summary>An outline in the object frame with its extent, for culling and picking.</summary>
        private sealed class Footprint
        {
            public Vector2[] Points;
            public float Radius;
            public float Area;
            public bool IsTree;
        }

        private readonly Dictionary<string, Footprint> footprints = new Dictionary<string, Footprint>(StringComparer.OrdinalIgnoreCase);

        private enum DragMode { None, Pan, MovePending, Move, Rotate }

        private sealed class SplinePath
        {
            public Vector2[] Points;
            public Rect Bounds;
        }

        private readonly List<SplinePath> splines = new List<SplinePath>();
        private OmsiMapSummary summary;
        private double tileSize = 300.0;
        private double originX;
        private double originY;
        private Rect mapBounds;

        // View: map-relative metres at the centre of the element and metres per pixel.
        private Vector2 center;
        private float metersPerPixel = 1f;
        private bool hasView;

        // True until the user pans or zooms; a fitted view refits when the element changes size.
        private bool isFitted;
        private float slowestDrawMilliseconds;

        private OmsiTileSummary selectedTile;
        private OmsiObjectRecord selectedObject;

        private DragMode dragMode;
        private int dragPointerId = -1;
        private Vector2 dragStartPointer;
        private Vector2 dragLastPointer;
        private double dragStartLocalX;
        private double dragStartLocalY;
        private double previewLocalX;
        private double previewLocalY;
        private double previewHeading;

        // Placement mode: the object to place and the map position under the pointer, if any.
        private string placementAsset;
        private double placementHeading;
        private Vector2? placementPointer;

        public MapCanvasElement()
        {
            AddToClassList("map-canvas");
            focusable = true;
            pickingMode = PickingMode.Position;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => CancelDrag());
            RegisterCallback<PointerLeaveEvent>(_ => SetPlacementPointer(null));
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        /// <summary>An object was clicked; the tile and record identify it.</summary>
        public event Action<OmsiTileSummary, OmsiObjectRecord> ObjectSelected;

        /// <summary>Empty space inside a tile was clicked.</summary>
        public event Action<OmsiTileSummary> TileClicked;

        /// <summary>An object was moved or rotated by dragging and the pointer was released.</summary>
        public event Action<MapObjectEdit> ObjectEdited;

        /// <summary>The map was left-clicked in placement mode.</summary>
        public event Action<MapPlacement> PlacementRequested;

        /// <summary>Describes the position under the pointer and the scale, for an overlay label.</summary>
        public event Action<string> PointerInfoChanged;

        /// <summary>When false, objects can be selected but not dragged.</summary>
        public bool AllowEditing { get; set; }

        public float MetersPerPixel => metersPerPixel;

        public bool IsPlacing => placementAsset != null;

        /// <summary>Heading of the placement preview in degrees.</summary>
        public double PlacementHeading
        {
            get => placementHeading;
            set
            {
                placementHeading = (value % 360.0 + 360.0) % 360.0;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Starts placement mode: left clicks report positions instead of selecting objects.</summary>
        public void BeginPlacement(string assetPath, double headingDegrees)
        {
            CancelDrag();
            placementAsset = assetPath;
            PlacementHeading = headingDegrees;
        }

        public void EndPlacement()
        {
            placementAsset = null;
            placementPointer = null;
            MarkDirtyRepaint();
        }

        public void SetMap(OmsiMapSummary map, bool keepView)
        {
            summary = map;
            tileSize = map?.TileSizeMeters ?? 300.0;
            CancelDrag();
            BuildCaches();
            if (!keepView || !hasView)
                FitToMap();
            MarkDirtyRepaint();
        }

        public void SetSelection(OmsiTileSummary tile, OmsiObjectRecord item)
        {
            selectedTile = tile;
            selectedObject = item;
            MarkDirtyRepaint();
        }

        public void Refresh()
        {
            MarkDirtyRepaint();
        }

        /// <summary>Object outlines by asset path, as the outline catalog computed them; null clears them.</summary>
        public void SetOutlines(IReadOnlyDictionary<string, OmsiObjectOutline> outlines)
        {
            footprints.Clear();
            if (outlines != null)
            {
                foreach (var pair in outlines)
                {
                    var points = pair.Value?.Points;
                    if (points == null || points.Length < 2)
                        continue;
                    var footprint = new Footprint { Points = new Vector2[points.Length], IsTree = pair.Value.Source == OmsiOutlineSource.Tree };
                    for (var index = 0; index < points.Length; index++)
                    {
                        footprint.Points[index] = new Vector2((float)points[index].X, (float)points[index].Y);
                        footprint.Radius = Mathf.Max(footprint.Radius, footprint.Points[index].magnitude);
                    }
                    footprint.Area = Mathf.Abs(PolygonArea(footprint.Points));
                    footprints[pair.Key] = footprint;
                }
            }
            MarkDirtyRepaint();
        }

        public int OutlineCount => footprints.Count;

        private static float PolygonArea(Vector2[] points)
        {
            var area = 0f;
            for (int index = 0, previous = points.Length - 1; index < points.Length; previous = index++)
                area += points[previous].x * points[index].y - points[index].x * points[previous].y;
            return area * 0.5f;
        }

        /// <summary>
        /// Object frame to map: OMSI headings turn clockwise, so a local point (x right, y forward) rotates by
        /// -heading around the object position.
        /// </summary>
        private static Vector2 ObjectToMap(Vector2 local, Vector2 position, float sin, float cos)
        {
            return new Vector2(position.x + local.x * cos + local.y * sin, position.y - local.x * sin + local.y * cos);
        }

        public void FitToMap()
        {
            var size = layout.size;
            if (summary == null || mapBounds.width <= 0f || float.IsNaN(size.x) || size.x < 10f || size.y < 10f)
            {
                hasView = false;
                return;
            }

            const float margin = 24f;
            center = mapBounds.center;
            metersPerPixel = Mathf.Max(mapBounds.width / Mathf.Max(1f, size.x - margin * 2f),
                mapBounds.height / Mathf.Max(1f, size.y - margin * 2f));
            hasView = true;
            isFitted = true;
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        /// <summary>Zooms around the element centre; a factor above 1 zooms in.</summary>
        public void Zoom(float factor)
        {
            ZoomAround(layout.size * 0.5f, factor);
        }

        public void CenterOn(OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (tile == null)
                return;
            center = item != null
                ? ToMap(tile, item.LocalX, item.LocalY)
                : ToMap(tile, tileSize * 0.5, tileSize * 0.5);
            if (item != null && metersPerPixel > 0.5f)
                metersPerPixel = 0.5f;
            hasView = true;
            isFitted = false;
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        private void BuildCaches()
        {
            splines.Clear();
            mapBounds = Rect.zero;
            if (summary == null || summary.Tiles.Count == 0)
                return;

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            foreach (var tile in summary.Tiles)
            {
                minX = Math.Min(minX, tile.X);
                minY = Math.Min(minY, tile.Y);
                maxX = Math.Max(maxX, tile.X);
                maxY = Math.Max(maxY, tile.Y);
            }

            // Draw relative to the lowest tile corner; world-coordinate maps reach about 900 km, beyond float precision.
            originX = minX * tileSize;
            originY = minY * tileSize;
            mapBounds = new Rect(0f, 0f, (float)((maxX - minX + 1) * tileSize), (float)((maxY - minY + 1) * tileSize));

            foreach (var tile in summary.Tiles)
            {
                foreach (var spline in tile.Splines)
                {
                    var world = OmsiMapGeometry.BuildSplinePolyline(tile, spline, tileSize);
                    if (world.Length < 2)
                        continue;
                    var points = new Vector2[world.Length];
                    var bounds = new Rect((float)(world[0].X - originX), (float)(world[0].Y - originY), 0f, 0f);
                    for (var index = 0; index < world.Length; index++)
                    {
                        points[index] = new Vector2((float)(world[index].X - originX), (float)(world[index].Y - originY));
                        bounds.xMin = Mathf.Min(bounds.xMin, points[index].x);
                        bounds.yMin = Mathf.Min(bounds.yMin, points[index].y);
                        bounds.xMax = Mathf.Max(bounds.xMax, points[index].x);
                        bounds.yMax = Mathf.Max(bounds.yMax, points[index].y);
                    }
                    splines.Add(new SplinePath { Points = points, Bounds = bounds });
                }
            }
        }

        private Vector2 ToMap(OmsiTileSummary tile, double localX, double localY)
        {
            return new Vector2((float)(tile.X * tileSize + localX - originX), (float)(tile.Y * tileSize + localY - originY));
        }

        private Vector2 MapToScreen(Vector2 map)
        {
            var size = layout.size;
            return new Vector2(size.x * 0.5f + (map.x - center.x) / metersPerPixel,
                size.y * 0.5f - (map.y - center.y) / metersPerPixel);
        }

        private Vector2 ScreenToMap(Vector2 screen)
        {
            var size = layout.size;
            return new Vector2(center.x + (screen.x - size.x * 0.5f) * metersPerPixel,
                center.y - (screen.y - size.y * 0.5f) * metersPerPixel);
        }

        /// <summary>Visible map area in map-relative metres, slightly enlarged so markers at the edge are drawn.</summary>
        private Rect VisibleMapRect(float marginPixels)
        {
            var size = layout.size;
            var topLeft = ScreenToMap(new Vector2(-marginPixels, -marginPixels));
            var bottomRight = ScreenToMap(new Vector2(size.x + marginPixels, size.y + marginPixels));
            return Rect.MinMaxRect(topLeft.x, bottomRight.y, bottomRight.x, topLeft.y);
        }

        private Rect TileRect(OmsiTileSummary tile)
        {
            return new Rect((float)(tile.X * tileSize - originX), (float)(tile.Y * tileSize - originY), (float)tileSize, (float)tileSize);
        }

        private void Draw(MeshGenerationContext context)
        {
            if (summary == null || !hasView)
                return;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var visible = VisibleMapRect(16f);
            var painter = context.painter2D;
            DrawTiles(painter, visible);
            DrawOutlines(painter, visible);
            DrawSplines(painter, visible);
            DrawObjects(context, painter, visible);
            DrawSelection(painter);
            DrawPlacement(painter);

            // Report slow redraws once per new maximum, so large maps can be diagnosed from the player log.
            var milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            if (milliseconds > 50f && milliseconds > slowestDrawMilliseconds * 1.5f)
            {
                slowestDrawMilliseconds = milliseconds;
                Debug.LogWarning("Map view redraw took " + milliseconds.ToString("0") + " ms at " + metersPerPixel.ToString("0.###") + " m/px.");
            }
        }

        private void DrawTiles(Painter2D painter, Rect visible)
        {
            foreach (var tile in summary.Tiles)
            {
                var rect = TileRect(tile);
                if (!rect.Overlaps(visible))
                    continue;
                painter.fillColor = tile.IsAvailable ? TileColor : MissingTileColor;
                painter.BeginPath();
                AddRect(painter, rect);
                painter.Fill();
            }

            painter.strokeColor = TileBorderColor;
            painter.lineWidth = 1f;
            painter.BeginPath();
            foreach (var tile in summary.Tiles)
            {
                var rect = TileRect(tile);
                if (rect.Overlaps(visible))
                    AddRect(painter, rect);
            }
            painter.Stroke();

            if (selectedTile != null)
            {
                painter.strokeColor = SelectedTileColor;
                painter.lineWidth = 2f;
                painter.BeginPath();
                AddRect(painter, TileRect(selectedTile));
                painter.Stroke();
            }
        }

        private void DrawOutlines(Painter2D painter, Rect visible)
        {
            if (footprints.Count == 0 || metersPerPixel > OutlineMetersPerPixel)
                return;

            // Two passes so trees and other objects can be filled in one path each.
            for (var pass = 0; pass < 2; pass++)
            {
                var trees = pass == 0;
                painter.fillColor = trees ? TreeFillColor : OutlineFillColor;
                painter.strokeColor = trees ? TreeStrokeColor : OutlineStrokeColor;
                painter.lineWidth = 1f;
                painter.BeginPath();
                var any = false;
                foreach (var tile in summary.Tiles)
                {
                    foreach (var item in tile.Objects)
                    {
                        if (!footprints.TryGetValue(item.AssetPath, out var footprint) || footprint.IsTree != trees)
                            continue;
                        var dragging = ReferenceEquals(item, selectedObject) && IsDraggingObject();
                        var position = dragging ? ToMap(tile, previewLocalX, previewLocalY) : ToMap(tile, item.LocalX, item.LocalY);
                        if (footprint.Radius / metersPerPixel < 1.5f ||
                            position.x + footprint.Radius < visible.xMin || position.x - footprint.Radius > visible.xMax ||
                            position.y + footprint.Radius < visible.yMin || position.y - footprint.Radius > visible.yMax)
                            continue;
                        AddFootprint(painter, footprint, position, dragging ? previewHeading : item.HeadingDegrees);
                        any = true;
                    }
                }
                if (any)
                {
                    painter.Fill();
                    painter.Stroke();
                }
            }
        }

        private void AddFootprint(Painter2D painter, Footprint footprint, Vector2 position, double headingDegrees)
        {
            var heading = (float)(headingDegrees * Math.PI / 180.0);
            var sin = Mathf.Sin(heading);
            var cos = Mathf.Cos(heading);
            painter.MoveTo(MapToScreen(ObjectToMap(footprint.Points[0], position, sin, cos)));
            for (var index = 1; index < footprint.Points.Length; index++)
                painter.LineTo(MapToScreen(ObjectToMap(footprint.Points[index], position, sin, cos)));
            if (footprint.Points.Length > 2)
                painter.ClosePath();
        }

        private void AddRect(Painter2D painter, Rect rect)
        {
            var a = MapToScreen(new Vector2(rect.xMin, rect.yMin));
            var b = MapToScreen(new Vector2(rect.xMax, rect.yMin));
            var c = MapToScreen(new Vector2(rect.xMax, rect.yMax));
            var d = MapToScreen(new Vector2(rect.xMin, rect.yMax));
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.LineTo(c);
            painter.LineTo(d);
            painter.ClosePath();
        }

        private void DrawSplines(Painter2D painter, Rect visible)
        {
            painter.strokeColor = SplineColor;
            painter.lineWidth = Mathf.Clamp(6f / metersPerPixel, 1.5f, 6f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            foreach (var spline in splines)
            {
                if (!spline.Bounds.Overlaps(visible))
                    continue;
                var last = MapToScreen(spline.Points[0]);
                painter.MoveTo(last);
                for (var index = 1; index < spline.Points.Length; index++)
                {
                    var point = MapToScreen(spline.Points[index]);
                    // Skip points that would land within two pixels of the previous one; always keep the end.
                    if ((point - last).sqrMagnitude < 4f && index < spline.Points.Length - 1)
                        continue;
                    painter.LineTo(point);
                    last = point;
                }
            }
            painter.Stroke();
        }

        private void DrawObjects(MeshGenerationContext context, Painter2D painter, Rect visible)
        {
            var half = metersPerPixel < 0.2f ? 3f : metersPerPixel < 1f ? 2f : 1.5f;
            var showHeading = metersPerPixel < 0.6f;
            var markers = new List<Vector2>();
            if (showHeading)
            {
                painter.strokeColor = HeadingColor;
                painter.lineWidth = 1f;
                painter.BeginPath();
            }

            foreach (var tile in summary.Tiles)
            {
                if (tile.Objects.Count == 0)
                    continue;
                // Objects may lie outside their own tile, so test each object, not just the tile rectangle.
                foreach (var item in tile.Objects)
                {
                    var map = ReferenceEquals(item, selectedObject) && IsDraggingObject()
                        ? ToMap(tile, previewLocalX, previewLocalY)
                        : ToMap(tile, item.LocalX, item.LocalY);
                    if (!visible.Contains(map))
                        continue;
                    var screen = MapToScreen(map);
                    markers.Add(screen);
                    if (showHeading)
                    {
                        var heading = (float)(item.HeadingDegrees * Math.PI / 180.0);
                        painter.MoveTo(screen);
                        painter.LineTo(screen + new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading)) * 7f);
                    }
                }
            }

            if (showHeading)
                painter.Stroke();
            WriteQuads(context, markers, half, ObjectColor);
        }

        /// <summary>Square markers written straight into meshes; much cheaper than vector circles for thousands of objects.</summary>
        private static void WriteQuads(MeshGenerationContext context, List<Vector2> centers, float half, Color32 color)
        {
            for (var start = 0; start < centers.Count; start += QuadsPerMesh)
            {
                var count = Math.Min(QuadsPerMesh, centers.Count - start);
                var mesh = context.Allocate(count * 4, count * 6);
                for (var index = 0; index < count; index++)
                {
                    var point = centers[start + index];
                    mesh.SetNextVertex(new Vertex { position = new Vector3(point.x - half, point.y - half, Vertex.nearZ), tint = color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(point.x + half, point.y - half, Vertex.nearZ), tint = color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(point.x + half, point.y + half, Vertex.nearZ), tint = color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(point.x - half, point.y + half, Vertex.nearZ), tint = color });
                    var first = (ushort)(index * 4);
                    mesh.SetNextIndex(first);
                    mesh.SetNextIndex((ushort)(first + 1));
                    mesh.SetNextIndex((ushort)(first + 2));
                    mesh.SetNextIndex(first);
                    mesh.SetNextIndex((ushort)(first + 2));
                    mesh.SetNextIndex((ushort)(first + 3));
                }
            }
        }

        private void DrawSelection(Painter2D painter)
        {
            if (selectedObject == null || selectedTile == null)
                return;

            var screen = SelectedObjectScreen();
            var handle = HandleScreen(screen);
            painter.strokeColor = SelectedObjectColor;
            painter.lineWidth = 2f;
            if (footprints.TryGetValue(selectedObject.AssetPath, out var selectedFootprint))
            {
                var dragging = IsDraggingObject();
                painter.BeginPath();
                AddFootprint(painter, selectedFootprint,
                    dragging ? ToMap(selectedTile, previewLocalX, previewLocalY) : ToMap(selectedTile, selectedObject.LocalX, selectedObject.LocalY),
                    dragging ? previewHeading : selectedObject.HeadingDegrees);
                painter.Stroke();
            }
            painter.BeginPath();
            painter.Arc(screen, 7f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(screen);
            painter.LineTo(handle);
            painter.Stroke();

            if (AllowEditing)
            {
                painter.fillColor = dragMode == DragMode.Rotate ? SelectedObjectColor : new Color(0.12f, 0.14f, 0.17f, 1f);
                painter.BeginPath();
                painter.Arc(handle, HandleRadius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                painter.Stroke();
            }
        }

        private void DrawPlacement(Painter2D painter)
        {
            if (placementAsset == null || !placementPointer.HasValue)
                return;

            var position = placementPointer.Value;
            var screen = MapToScreen(position);
            painter.strokeColor = PlacementColor;
            painter.lineWidth = 2f;
            if (footprints.TryGetValue(placementAsset, out var footprint))
            {
                painter.fillColor = PlacementFillColor;
                painter.BeginPath();
                AddFootprint(painter, footprint, position, placementHeading);
                painter.Fill();
                painter.Stroke();
            }
            var heading = (float)(placementHeading * Math.PI / 180.0);
            painter.BeginPath();
            painter.Arc(screen, 5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.MoveTo(screen);
            painter.LineTo(screen + new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading)) * HandleDistance);
            painter.Stroke();
        }

        private void SetPlacementPointer(Vector2? screen)
        {
            if (placementAsset == null)
                return;
            placementPointer = screen.HasValue ? ScreenToMap(screen.Value) : (Vector2?)null;
            MarkDirtyRepaint();
        }

        private Vector2 SelectedObjectScreen()
        {
            return IsDraggingObject()
                ? MapToScreen(ToMap(selectedTile, previewLocalX, previewLocalY))
                : MapToScreen(ToMap(selectedTile, selectedObject.LocalX, selectedObject.LocalY));
        }

        private Vector2 HandleScreen(Vector2 objectScreen)
        {
            var headingDegrees = dragMode == DragMode.Rotate || dragMode == DragMode.Move ? previewHeading : selectedObject.HeadingDegrees;
            var heading = (float)(headingDegrees * Math.PI / 180.0);
            return objectScreen + new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading)) * HandleDistance;
        }

        private bool IsDraggingObject()
        {
            return dragMode == DragMode.Move || dragMode == DragMode.Rotate;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (summary == null || !hasView || dragMode != DragMode.None)
                return;

            Focus();
            var position = (Vector2)evt.localPosition;
            if (evt.button == 1 || evt.button == 2)
            {
                BeginDrag(DragMode.Pan, evt.pointerId, position);
                evt.StopPropagation();
                return;
            }
            if (evt.button != 0)
                return;

            if (placementAsset != null)
            {
                var map = ScreenToMap(position);
                var target = TileAt(map);
                var placement = new MapPlacement { Tile = target, KeepPlacing = evt.shiftKey };
                if (target != null)
                {
                    var rect = TileRect(target);
                    // Centimetres are precise enough for a click and keep the written numbers short.
                    placement.LocalX = Math.Round(map.x - rect.xMin, 2);
                    placement.LocalY = Math.Round(map.y - rect.yMin, 2);
                }
                PlacementRequested?.Invoke(placement);
                evt.StopPropagation();
                return;
            }

            if (AllowEditing && selectedObject != null &&
                Vector2.Distance(position, HandleScreen(SelectedObjectScreen())) <= HandleRadius + 3f)
            {
                StartObjectDrag(DragMode.Rotate, evt.pointerId, position);
                evt.StopPropagation();
                return;
            }

            if (TryPickObject(position, out var tile, out var item))
            {
                selectedTile = tile;
                selectedObject = item;
                ObjectSelected?.Invoke(tile, item);
                if (AllowEditing)
                    StartObjectDrag(DragMode.MovePending, evt.pointerId, position);
                MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }

            var clickedTile = TileAt(ScreenToMap(position));
            if (clickedTile != null)
                TileClicked?.Invoke(clickedTile);
            BeginDrag(DragMode.Pan, evt.pointerId, position);
            evt.StopPropagation();
        }

        private void StartObjectDrag(DragMode mode, int pointerId, Vector2 position)
        {
            dragStartLocalX = selectedObject.LocalX;
            dragStartLocalY = selectedObject.LocalY;
            previewLocalX = selectedObject.LocalX;
            previewLocalY = selectedObject.LocalY;
            previewHeading = selectedObject.HeadingDegrees;
            BeginDrag(mode, pointerId, position);
        }

        private void BeginDrag(DragMode mode, int pointerId, Vector2 position)
        {
            dragMode = mode;
            dragPointerId = pointerId;
            dragStartPointer = position;
            dragLastPointer = position;
            this.CapturePointer(pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            var position = (Vector2)evt.localPosition;
            if (dragMode == DragMode.None || evt.pointerId != dragPointerId)
            {
                SetPlacementPointer(position);
                NotifyPointerInfo(position);
                return;
            }

            switch (dragMode)
            {
                case DragMode.Pan:
                    var delta = position - dragLastPointer;
                    center += new Vector2(-delta.x, delta.y) * metersPerPixel;
                    isFitted = false;
                    break;
                case DragMode.MovePending:
                    if (Vector2.Distance(position, dragStartPointer) >= DragThreshold)
                        dragMode = DragMode.Move;
                    break;
            }

            if (dragMode == DragMode.Move)
            {
                var moved = position - dragStartPointer;
                previewLocalX = dragStartLocalX + moved.x * metersPerPixel;
                previewLocalY = dragStartLocalY - moved.y * metersPerPixel;
            }
            else if (dragMode == DragMode.Rotate)
            {
                var direction = position - SelectedObjectScreen();
                if (direction.sqrMagnitude > 4f)
                {
                    var heading = Math.Atan2(direction.x, -direction.y) * 180.0 / Math.PI;
                    if (evt.shiftKey)
                        heading = Math.Round(heading / 15.0) * 15.0;
                    previewHeading = (heading % 360.0 + 360.0) % 360.0;
                }
            }

            dragLastPointer = position;
            NotifyPointerInfo(position);
            MarkDirtyRepaint();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (dragMode == DragMode.None || evt.pointerId != dragPointerId)
                return;

            var finished = dragMode;
            var edit = finished == DragMode.Move || finished == DragMode.Rotate
                ? new MapObjectEdit
                {
                    Tile = selectedTile,
                    Record = selectedObject,
                    LocalX = previewLocalX,
                    LocalY = previewLocalY,
                    HeadingDegrees = previewHeading
                }
                : null;
            EndDrag();
            if (edit != null)
                ObjectEdited?.Invoke(edit);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void CancelDrag()
        {
            if (dragMode == DragMode.None)
                return;
            EndDrag();
            MarkDirtyRepaint();
        }

        private void EndDrag()
        {
            // Reset first: releasing the capture raises PointerCaptureOutEvent, which must find no drag in progress.
            var pointerId = dragPointerId;
            dragMode = DragMode.None;
            dragPointerId = -1;
            if (pointerId >= 0 && this.HasPointerCapture(pointerId))
                this.ReleasePointer(pointerId);
        }

        private void OnWheel(WheelEvent evt)
        {
            if (summary == null || !hasView)
                return;
            ZoomAround((Vector2)evt.localMousePosition, evt.delta.y > 0f ? 1f / 1.25f : 1.25f);
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && dragMode != DragMode.None)
            {
                CancelDrag();
                evt.StopPropagation();
            }
        }

        private void ZoomAround(Vector2 screen, float factor)
        {
            if (summary == null || !hasView || factor <= 0f)
                return;

            var anchor = ScreenToMap(screen);
            var maximum = Mathf.Max(mapBounds.width, mapBounds.height) / 50f;
            metersPerPixel = Mathf.Clamp(metersPerPixel / factor, 0.01f, Mathf.Max(0.05f, maximum));
            // Keep the point under the cursor in place.
            var after = ScreenToMap(screen);
            center += anchor - after;
            isFitted = false;
            NotifyPointerInfo(screen);
            MarkDirtyRepaint();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if ((!hasView || isFitted) && summary != null)
                FitToMap();
            MarkDirtyRepaint();
        }

        private bool TryPickObject(Vector2 screen, out OmsiTileSummary pickedTile, out OmsiObjectRecord pickedItem)
        {
            pickedTile = null;
            pickedItem = null;
            var nearest = PickRadius;
            var map = ScreenToMap(screen);
            var reach = PickRadius * metersPerPixel;
            foreach (var tile in summary.Tiles)
            {
                foreach (var item in tile.Objects)
                {
                    var position = ToMap(tile, item.LocalX, item.LocalY);
                    if (Mathf.Abs(position.x - map.x) > reach || Mathf.Abs(position.y - map.y) > reach)
                        continue;
                    var distance = Vector2.Distance(MapToScreen(position), screen);
                    if (distance <= nearest)
                    {
                        nearest = distance;
                        pickedTile = tile;
                        pickedItem = item;
                    }
                }
            }
            if (pickedItem != null || footprints.Count == 0 || metersPerPixel > OutlineMetersPerPixel)
                return pickedItem != null;

            // No marker nearby: take the smallest outline that contains the point.
            var smallest = float.MaxValue;
            foreach (var tile in summary.Tiles)
            {
                foreach (var item in tile.Objects)
                {
                    if (!footprints.TryGetValue(item.AssetPath, out var footprint) || footprint.Area >= smallest)
                        continue;
                    var position = ToMap(tile, item.LocalX, item.LocalY);
                    if (Mathf.Abs(position.x - map.x) > footprint.Radius || Mathf.Abs(position.y - map.y) > footprint.Radius)
                        continue;
                    var heading = (float)(item.HeadingDegrees * Math.PI / 180.0);
                    // Map to object frame: the inverse rotation of ObjectToMap.
                    var dx = map.x - position.x;
                    var dy = map.y - position.y;
                    var local = new Vector2(dx * Mathf.Cos(heading) - dy * Mathf.Sin(heading), dx * Mathf.Sin(heading) + dy * Mathf.Cos(heading));
                    if (footprint.Points.Length > 2 && Contains(footprint.Points, local))
                    {
                        smallest = footprint.Area;
                        pickedTile = tile;
                        pickedItem = item;
                    }
                }
            }
            return pickedItem != null;
        }

        private static bool Contains(Vector2[] polygon, Vector2 point)
        {
            var inside = false;
            for (int index = 0, previous = polygon.Length - 1; index < polygon.Length; previous = index++)
            {
                if ((polygon[index].y > point.y) != (polygon[previous].y > point.y) &&
                    point.x < (polygon[previous].x - polygon[index].x) * (point.y - polygon[index].y) /
                    (polygon[previous].y - polygon[index].y) + polygon[index].x)
                    inside = !inside;
            }
            return inside;
        }

        private OmsiTileSummary TileAt(Vector2 map)
        {
            foreach (var tile in summary.Tiles)
                if (TileRect(tile).Contains(map))
                    return tile;
            return null;
        }

        private void NotifyPointerInfo(Vector2? screen)
        {
            if (PointerInfoChanged == null || summary == null)
                return;

            var scale = "1 px = " + (metersPerPixel >= 1f ? metersPerPixel.ToString("0.#") : metersPerPixel.ToString("0.###")) + " m";
            if (!screen.HasValue)
            {
                PointerInfoChanged(scale);
                return;
            }

            var map = ScreenToMap(screen.Value);
            var tile = TileAt(map);
            var text = tile == null
                ? "outside the map"
                : "Tile " + tile.X + ", " + tile.Y + "   x " + (map.x - TileRect(tile).xMin).ToString("0.0") +
                  "   y " + (map.y - TileRect(tile).yMin).ToString("0.0");
            if (IsDraggingObject())
                text = "#" + selectedObject.Id + "   x " + previewLocalX.ToString("0.00") + "   y " + previewLocalY.ToString("0.00") +
                       "   heading " + previewHeading.ToString("0.0") + "°";
            PointerInfoChanged(text + "      " + scale);
        }
    }
}
