using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>A selected object and the tile that holds it.</summary>
    internal sealed class MapSelectionItem
    {
        public OmsiTileSummary Tile;
        public OmsiObjectRecord Record;
    }

    /// <summary>A finished drag of an object: its new position and orientation in tile-local values.</summary>
    internal sealed class MapObjectEdit
    {
        public OmsiTileSummary Tile;
        public OmsiObjectRecord Record;
        public double LocalX;
        public double LocalY;
        public double Height;
        public double HeadingDegrees;
        public double PitchDegrees;
        public double BankDegrees;
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
    /// are visible, selects objects and, when editing is allowed, moves and rotates them by dragging. Ctrl+click and
    /// Ctrl+drag select several objects, which then move together. In placement mode a preview of the chosen object
    /// follows the pointer and a click reports where it should go.
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
        private static readonly Color QuietSplineColor = new Color(0.95f, 0.71f, 0.30f, 0.5f);
        private static readonly Color32 ObjectColor = new Color(0.56f, 0.82f, 0.94f, 0.95f);
        private static readonly Color HeadingColor = new Color(0.56f, 0.82f, 0.94f, 0.55f);
        private static readonly Color SelectedObjectColor = new Color(1f, 0.36f, 0.42f, 1f);
        private static readonly Color OutlineFillColor = new Color(0.42f, 0.55f, 0.66f, 0.38f);
        private static readonly Color OutlineStrokeColor = new Color(0.62f, 0.78f, 0.9f, 0.85f);
        private static readonly Color TreeFillColor = new Color(0.32f, 0.62f, 0.36f, 0.32f);
        private static readonly Color TreeStrokeColor = new Color(0.45f, 0.78f, 0.5f, 0.7f);
        private static readonly Color PlacementColor = new Color(0.47f, 0.9f, 0.6f, 1f);
        private static readonly Color PlacementFillColor = new Color(0.47f, 0.9f, 0.6f, 0.25f);
        private static readonly Color BoxFillColor = new Color(1f, 0.36f, 0.42f, 0.12f);

        // Rotation rings in the 3D view, coloured like the X, Y and Z axes they turn about.
        private static readonly Color PitchRingColor = new Color(0.94f, 0.33f, 0.33f, 1f);
        private static readonly Color HeadingRingColor = new Color(0.42f, 0.85f, 0.42f, 1f);
        private static readonly Color BankRingColor = new Color(0.36f, 0.58f, 1f, 1f);
        private const float RingPickDistance = 7f;
        private const int RingSegments = 96;

        /// <summary>Rings stay at least this large on screen, so small objects can still be turned.</summary>
        private const float MinimumRingPixels = 60f;

        /// <summary>
        /// The angle a rotation ring changes. The rings form a gimbal in OMSI's order: heading turns about the vertical,
        /// pitch about the right axis after heading, bank about the forward axis after heading and pitch, so each ring
        /// changes exactly one stored angle.
        /// </summary>
        private enum RingAxis { None, Pitch, Heading, Bank }

        /// <summary>Outlines are drawn from this scale on; further out the map shows markers only.</summary>
        private const float OutlineMetersPerPixel = 1.5f;

        /// <summary>
        /// An outline in the object frame with its extent, for culling and picking. Outer contours run
        /// counter-clockwise and holes clockwise, so a non-zero fill leaves the holes open.
        /// </summary>
        private sealed class Footprint
        {
            public Vector2[][] Contours;
            public float Radius;
            public float Area;
            public bool IsTree;

            /// <summary>True when the textured view draws a model of this kind, so marker and outline can stay away.</summary>
            public bool HasModel;
        }

        private readonly Dictionary<string, Footprint> footprints = new Dictionary<string, Footprint>(StringComparer.OrdinalIgnoreCase);

        private enum DragMode { None, Pan, MovePending, Move, Rotate, Box, Orbit, OrbitPan, Turn }

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

        // The selection; the primary object is the last one and the only one with a rotation handle.
        private OmsiObjectRecord selectedObject;
        private readonly List<MapSelectionItem> selection = new List<MapSelectionItem>();
        private readonly HashSet<OmsiObjectRecord> selectedRecords = new HashSet<OmsiObjectRecord>();

        // A click on a member of a larger selection keeps the group for dragging; without a drag it selects only it.
        private MapSelectionItem clickedMember;

        private DragMode dragMode;
        private int dragPointerId = -1;
        private Vector2 dragStartPointer;
        private Vector2 dragLastPointer;
        private double dragStartLocalX;
        private double dragStartLocalY;
        private double previewLocalX;
        private double previewLocalY;
        private double previewHeading;
        private double previewPitch;
        private double previewBank;

        // 3D view: a move slides over the horizontal plane through the grabbed point; a turn follows the pointer
        // around the ring's centre, adding up the angle step by step so it may go round more than once.
        private Vector3 moveGrabPoint;
        private RingAxis hoveredRing;
        private RingAxis activeRing;
        private Vector3 turnCentre;
        private Vector3 turnAxis;
        private Vector3 turnLastVector;
        private double turnStartAngle;
        private double turnAngle;
        private readonly Label angleLabel;

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

            angleLabel = new Label { pickingMode = PickingMode.Ignore };
            angleLabel.AddToClassList("map-angle-label");
            angleLabel.style.display = DisplayStyle.None;
            Add(angleLabel);
        }

        /// <summary>An object was clicked; the tile and record identify it.</summary>
        public event Action<OmsiTileSummary, OmsiObjectRecord> ObjectSelected;

        /// <summary>An object was Ctrl+clicked to add it to the selection or take it out.</summary>
        public event Action<OmsiTileSummary, OmsiObjectRecord> ObjectToggled;

        /// <summary>A box was dragged with Ctrl; the objects inside it, to add to the selection.</summary>
        public event Action<List<MapSelectionItem>> ObjectsBoxSelected;

        /// <summary>Empty space inside a tile was clicked.</summary>
        public event Action<OmsiTileSummary> TileClicked;

        /// <summary>The selection was moved, or its object rotated, by dragging and the pointer was released.</summary>
        public event Action<List<MapObjectEdit>> ObjectsEdited;

        /// <summary>The selection is being dragged by this many metres east and north; the edit follows on release.</summary>
        public event Action<IReadOnlyList<MapSelectionItem>, double, double> MovePreviewed;

        /// <summary>The selected object is being turned to this heading; the edit follows on release.</summary>
        public event Action<OmsiTileSummary, OmsiObjectRecord, double> HeadingPreviewed;

        /// <summary>The selected object is being turned on a ring in the 3D view; heading, pitch and bank follow.</summary>
        public event Action<OmsiTileSummary, OmsiObjectRecord, double, double, double> OrientationPreviewed;

        /// <summary>A move or turn ended, whether it was released or cancelled.</summary>
        public event Action PreviewEnded;

        /// <summary>The map was left-clicked in placement mode.</summary>
        public event Action<MapPlacement> PlacementRequested;

        /// <summary>
        /// The placement preview changed: the object, its map position under the pointer and its heading; a null
        /// position when the pointer is off the map or placement ended.
        /// </summary>
        public event Action<string, Vector2?, double> PlacementPreviewed;

        /// <summary>Describes the position under the pointer and the scale, for an overlay label.</summary>
        public event Action<string> PointerInfoChanged;

        /// <summary>When false, objects can be selected but not dragged.</summary>
        public bool AllowEditing { get; set; }

        public float MetersPerPixel => metersPerPixel;

        /// <summary>
        /// True in the 3D view: the right button turns the camera around <see cref="OrbitTarget"/> and the middle one
        /// moves it. The left button does what it does from above: it selects objects by their models or markers,
        /// Ctrl+click and Ctrl+drag select several, dragging moves them over the ground, rings turn a single object, and
        /// in placement mode a click places on the ground. Zoom, fitting and centring move the camera. The element draws
        /// the selected tile, markers, selection, rings, placement preview and selection box over the scene.
        /// </summary>
        public bool ThreeDimensional { get; private set; }

        /// <summary>The object models of the 3D view, for picking objects and placing the rings; null leaves 3D editing off.</summary>
        public MapObjectRenderer ObjectRenderer { get; set; }

        /// <summary>Ground height at a map position, for the point the 3D camera looks at.</summary>
        public Func<Vector2, float> GroundHeight { get; set; }

        /// <summary>The point the 3D camera looks at, in map-relative metres.</summary>
        public Vector2 OrbitTarget { get; private set; }

        /// <summary>Heading of the 3D camera in degrees, clockwise from north.</summary>
        public float OrbitYaw { get; private set; }

        /// <summary>Tilt of the 3D camera below the horizon in degrees.</summary>
        public float OrbitPitch { get; private set; } = 40f;

        public float OrbitDistance { get; private set; } = 300f;

        /// <summary>Switches to the 3D view, looking at the centre of the map view from a distance that shows about as much.</summary>
        public void EnterOrbitView()
        {
            CancelDrag();
            ThreeDimensional = true;
            OrbitTarget = center;
            OrbitDistance = Mathf.Clamp(layout.height * metersPerPixel, 20f, 15000f);
            OrbitPitch = 40f;
            OrbitYaw = 0f;
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        /// <summary>Sets the 3D camera directly, for example from the command line.</summary>
        public void SetOrbit(float yawDegrees, float pitchDegrees, float distance)
        {
            OrbitYaw = (yawDegrees % 360f + 360f) % 360f;
            OrbitPitch = Mathf.Clamp(pitchDegrees, 5f, 89f);
            OrbitDistance = Mathf.Clamp(distance, 5f, 15000f);
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        /// <summary>Returns to the map from above, centred where the 3D camera looked.</summary>
        public void LeaveOrbitView()
        {
            CancelDrag();
            ThreeDimensional = false;
            center = OrbitTarget;
            hasView = true;
            isFitted = false;
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Pixels at the left, top, right and bottom edge that floating panels cover; fitting the map leaves them out.
        /// </summary>
        public Vector4 FitInsets
        {
            get => fitInsets;
            set
            {
                if (value == fitInsets)
                    return;
                fitInsets = value;
                // A fitted map follows panels that open, close or get their size after the map was fitted.
                if (isFitted && hasView && !ThreeDimensional)
                    FitToMap();
            }
        }

        private Vector4 fitInsets;

        /// <summary>Map-relative metres at the centre of the view; world metres minus <see cref="OriginX"/> and <see cref="OriginY"/>.</summary>
        public Vector2 Center => center;

        /// <summary>World metres of the map's lowest tile corner, which the view draws relative to.</summary>
        public double OriginX => originX;

        public double OriginY => originY;

        public bool HasView => hasView && summary != null;

        /// <summary>
        /// True while ground, splines and objects are drawn textured below the view. The view then leaves tiles unfilled,
        /// draws spline lines thin and faint, and leaves out marker and outline of objects that show a model; objects
        /// without a model keep them, and the selection is drawn as always.
        /// </summary>
        public bool TexturedView { get; set; }

        /// <summary>Tells whether the textured view draws a model for an object file; null when no models are known.</summary>
        public Func<string, bool> HasModel
        {
            get => hasModel;
            set
            {
                hasModel = value;
                UpdateModelFlags();
            }
        }

        private Func<string, bool> hasModel;

        /// <summary>Takes newly loaded models into account.</summary>
        public void UpdateModelFlags()
        {
            foreach (var pair in footprints)
                pair.Value.HasModel = hasModel != null && hasModel(pair.Key);
            MarkDirtyRepaint();
        }

        public bool IsPlacing => placementAsset != null;

        /// <summary>Heading of the placement preview in degrees.</summary>
        public double PlacementHeading
        {
            get => placementHeading;
            set
            {
                placementHeading = (value % 360.0 + 360.0) % 360.0;
                NotifyPlacementPreview();
                MarkDirtyRepaint();
            }
        }

        private void NotifyPlacementPreview()
        {
            PlacementPreviewed?.Invoke(placementAsset, placementAsset != null ? placementPointer : null, placementHeading);
        }

        /// <summary>Starts placement mode: left clicks report positions instead of selecting objects.</summary>
        public void BeginPlacement(string assetPath, double headingDegrees)
        {
            CancelDrag();
            placementAsset = assetPath;
            PlacementHeading = headingDegrees;
        }

        /// <summary>Puts the placement preview at the centre of the view, as if the pointer were there; for command-line checks.</summary>
        public void PointPlacementAtCenter()
        {
            SetPlacementPointer(layout.size * 0.5f);
        }

        public void EndPlacement()
        {
            placementAsset = null;
            placementPointer = null;
            NotifyPlacementPreview();
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

        /// <param name="tile">The highlighted tile.</param>
        /// <param name="items">The selected objects; the last one is the primary object.</param>
        public void SetSelection(OmsiTileSummary tile, IReadOnlyList<MapSelectionItem> items)
        {
            selectedTile = tile;
            selection.Clear();
            selectedRecords.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    selection.Add(item);
                    selectedRecords.Add(item.Record);
                }
            }
            selectedObject = selection.Count > 0 ? selection[selection.Count - 1].Record : null;
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
                    var hull = pair.Value?.Points;
                    if (hull == null || hull.Length < 2)
                        continue;
                    var contours = pair.Value.Contours.Count > 0 ? pair.Value.Contours : new[] { hull };
                    var footprint = new Footprint { Contours = new Vector2[contours.Count][], IsTree = pair.Value.Source == OmsiOutlineSource.Tree };
                    var signedArea = 0f;
                    for (var contour = 0; contour < contours.Count; contour++)
                    {
                        var points = new Vector2[contours[contour].Length];
                        for (var index = 0; index < points.Length; index++)
                        {
                            points[index] = new Vector2((float)contours[contour][index].X, (float)contours[contour][index].Y);
                            footprint.Radius = Mathf.Max(footprint.Radius, points[index].magnitude);
                        }
                        footprint.Contours[contour] = points;
                        signedArea += PolygonArea(points);
                    }
                    // Holes count negative, so this is the covered area.
                    footprint.Area = Mathf.Abs(signedArea);
                    footprint.HasModel = hasModel != null && hasModel(pair.Key);
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

        /// <summary>Shows the whole map: from above fitted to the view, in 3D from a distance that takes it in.</summary>
        public void FitToMap()
        {
            if (ThreeDimensional)
            {
                if (summary == null || mapBounds.width <= 0f)
                    return;
                OrbitTarget = mapBounds.center;
                SetOrbit(OrbitYaw, Mathf.Max(OrbitPitch, 40f), Mathf.Max(mapBounds.width, mapBounds.height) * 1.2f);
                return;
            }

            var size = layout.size;
            if (summary == null || mapBounds.width <= 0f || float.IsNaN(size.x) || size.x < 10f || size.y < 10f)
            {
                hasView = false;
                return;
            }

            // Fit into the part of the view that panels leave free and centre the map there.
            const float margin = 24f;
            var free = new Vector2(Mathf.Max(50f, size.x - FitInsets.x - FitInsets.z), Mathf.Max(50f, size.y - FitInsets.y - FitInsets.w));
            metersPerPixel = Mathf.Max(mapBounds.width / Mathf.Max(1f, free.x - margin * 2f),
                mapBounds.height / Mathf.Max(1f, free.y - margin * 2f));
            center = mapBounds.center + new Vector2((FitInsets.z - FitInsets.x) * 0.5f, (FitInsets.y - FitInsets.w) * 0.5f) * metersPerPixel;
            hasView = true;
            isFitted = true;
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        /// <summary>Zooms around the element centre, in 3D towards the point the camera looks at; a factor above 1 zooms in.</summary>
        public void Zoom(float factor)
        {
            if (ThreeDimensional)
            {
                if (factor > 0f)
                    SetOrbit(OrbitYaw, OrbitPitch, OrbitDistance / factor);
                return;
            }
            ZoomAround(layout.size * 0.5f, factor);
        }

        /// <summary>Centres the view on a tile-local position at the given scale.</summary>
        public void ShowArea(OmsiTileSummary tile, double localX, double localY, float viewMetersPerPixel)
        {
            if (tile == null || viewMetersPerPixel <= 0f)
                return;
            center = ToMap(tile, localX, localY);
            metersPerPixel = viewMetersPerPixel;
            hasView = true;
            isFitted = false;
            NotifyPointerInfo(null);
            MarkDirtyRepaint();
        }

        /// <summary>Centres the view on an object, or on a tile without one; in 3D the camera turns to it and comes close to an object.</summary>
        public void CenterOn(OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (tile == null)
                return;
            if (ThreeDimensional)
            {
                OrbitTarget = item != null ? ToMap(tile, item.LocalX, item.LocalY) : ToMap(tile, tileSize * 0.5, tileSize * 0.5);
                SetOrbit(OrbitYaw, OrbitPitch, item != null ? Mathf.Min(OrbitDistance, 60f) : Mathf.Max(OrbitDistance, (float)tileSize * 1.2f));
                return;
            }
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
            if (ThreeDimensional)
            {
                Draw3D(context.painter2D);
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var visible = VisibleMapRect(16f);
            var painter = context.painter2D;
            DrawTiles(painter, visible);
            DrawOutlines(painter, visible);
            DrawSplines(painter, visible);
            DrawObjects(context, painter, visible);
            DrawSelection(painter);
            DrawPlacement(painter);
            DrawBox(painter);

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
                if (!rect.Overlaps(visible) || tile.IsAvailable && TexturedView)
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
                        if (!footprints.TryGetValue(item.AssetPath, out var footprint) || footprint.IsTree != trees ||
                            TexturedView && footprint.HasModel)
                            continue;
                        var position = DrawnPosition(tile, item);
                        if (footprint.Radius / metersPerPixel < 1.5f ||
                            position.x + footprint.Radius < visible.xMin || position.x - footprint.Radius > visible.xMax ||
                            position.y + footprint.Radius < visible.yMin || position.y - footprint.Radius > visible.yMax)
                            continue;
                        AddFootprint(painter, footprint, position, DrawnHeading(item));
                        any = true;
                    }
                }
                if (any)
                {
                    if (!TexturedView)
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
            foreach (var points in footprint.Contours)
            {
                painter.MoveTo(MapToScreen(ObjectToMap(points[0], position, sin, cos)));
                for (var index = 1; index < points.Length; index++)
                    painter.LineTo(MapToScreen(ObjectToMap(points[index], position, sin, cos)));
                if (points.Length > 2)
                    painter.ClosePath();
            }
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
            painter.strokeColor = TexturedView ? QuietSplineColor : SplineColor;
            painter.lineWidth = TexturedView ? 1.5f : Mathf.Clamp(6f / metersPerPixel, 1.5f, 6f);
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
                    if (TexturedView && footprints.TryGetValue(item.AssetPath, out var footprint) && footprint.HasModel)
                        continue;
                    var map = DrawnPosition(tile, item);
                    if (!visible.Contains(map))
                        continue;
                    var screen = MapToScreen(map);
                    markers.Add(screen);
                    if (showHeading)
                    {
                        var heading = (float)(DrawnHeading(item) * Math.PI / 180.0);
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
            if (selection.Count == 0)
                return;

            painter.strokeColor = SelectedObjectColor;
            painter.lineWidth = 2f;
            painter.BeginPath();
            foreach (var item in selection)
            {
                var position = DrawnPosition(item.Tile, item.Record);
                if (footprints.TryGetValue(item.Record.AssetPath, out var footprint))
                    AddFootprint(painter, footprint, position, DrawnHeading(item.Record));
                var screen = MapToScreen(position);
                painter.MoveTo(screen + new Vector2(7f, 0f));
                painter.Arc(screen, 7f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            }
            painter.Stroke();
            if (!HasRotationHandle())
                return;

            var objectScreen = SelectedObjectScreen();
            var handle = HandleScreen(objectScreen);
            painter.BeginPath();
            painter.MoveTo(objectScreen);
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
            if (ThreeDimensional)
            {
                var ground = screen.HasValue && hasView && summary != null ? GroundPoint(OrbitCamera().RayThrough(screen.Value)) : null;
                placementPointer = ground.HasValue ? new Vector2(ground.Value.x, ground.Value.z) : (Vector2?)null;
            }
            else
            {
                placementPointer = screen.HasValue ? ScreenToMap(screen.Value) : (Vector2?)null;
            }
            NotifyPlacementPreview();
            MarkDirtyRepaint();
        }

        private void DrawBox(Painter2D painter)
        {
            if (dragMode != DragMode.Box)
                return;
            var rect = Rect.MinMaxRect(Mathf.Min(dragStartPointer.x, dragLastPointer.x), Mathf.Min(dragStartPointer.y, dragLastPointer.y),
                Mathf.Max(dragStartPointer.x, dragLastPointer.x), Mathf.Max(dragStartPointer.y, dragLastPointer.y));
            painter.fillColor = BoxFillColor;
            painter.strokeColor = SelectedObjectColor;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        /// <summary>Where an object is drawn: shifted by the drag while it belongs to the moved selection.</summary>
        private Vector2 DrawnPosition(OmsiTileSummary tile, OmsiObjectRecord item)
        {
            return dragMode == DragMode.Move && selectedRecords.Contains(item)
                ? ToMap(tile, item.LocalX + previewLocalX - dragStartLocalX, item.LocalY + previewLocalY - dragStartLocalY)
                : ToMap(tile, item.LocalX, item.LocalY);
        }

        private double DrawnHeading(OmsiObjectRecord item)
        {
            return dragMode == DragMode.Rotate && ReferenceEquals(item, selectedObject) ? previewHeading : item.HeadingDegrees;
        }

        /// <summary>Only a single selected object can be rotated by its handle.</summary>
        private bool HasRotationHandle()
        {
            return selection.Count == 1 && selectedObject != null && selectedTile != null;
        }

        private Vector2 SelectedObjectScreen()
        {
            return MapToScreen(DrawnPosition(selectedTile, selectedObject));
        }

        private Vector2 HandleScreen(Vector2 objectScreen)
        {
            var heading = (float)(DrawnHeading(selectedObject) * Math.PI / 180.0);
            return objectScreen + new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading)) * HandleDistance;
        }

        private bool IsDraggingObject()
        {
            return dragMode == DragMode.Move || dragMode == DragMode.Rotate || dragMode == DragMode.Turn;
        }

        /// <summary>The tile of the primary selected object.</summary>
        private OmsiTileSummary PrimaryTile()
        {
            return selection.Count > 0 ? selection[selection.Count - 1].Tile : selectedTile;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (summary == null || !hasView || dragMode != DragMode.None)
                return;

            Focus();
            var position = (Vector2)evt.localPosition;
            if (ThreeDimensional)
            {
                // Right button turns the camera, the middle one moves it; the left one edits.
                if (evt.button == 1 || evt.button == 2)
                    BeginDrag(evt.button == 1 ? DragMode.Orbit : DragMode.OrbitPan, evt.pointerId, position);
                else if (evt.button == 0)
                    PointerDown3D(position, evt.pointerId, evt.ctrlKey || evt.commandKey, evt.shiftKey);
                evt.StopPropagation();
                return;
            }
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
                RequestPlacement(ScreenToMap(position), evt.shiftKey);
                evt.StopPropagation();
                return;
            }

            if (AllowEditing && HasRotationHandle() &&
                Vector2.Distance(position, HandleScreen(SelectedObjectScreen())) <= HandleRadius + 3f)
            {
                StartObjectDrag(DragMode.Rotate, selectedObject, evt.pointerId, position);
                evt.StopPropagation();
                return;
            }

            var toggle = evt.ctrlKey || evt.commandKey;
            if (TryPickObject(position, out var tile, out var item))
            {
                SelectPicked(tile, item, toggle, evt.pointerId, position);
                evt.StopPropagation();
                return;
            }

            if (toggle)
            {
                BeginDrag(DragMode.Box, evt.pointerId, position);
                evt.StopPropagation();
                return;
            }

            var clickedTile = TileAt(ScreenToMap(position));
            if (clickedTile != null)
                TileClicked?.Invoke(clickedTile);
            BeginDrag(DragMode.Pan, evt.pointerId, position);
            evt.StopPropagation();
        }

        /// <summary>Reports a placement click at a map position; outside the map the tile is null.</summary>
        private void RequestPlacement(Vector2 map, bool keepPlacing)
        {
            var target = TileAt(map);
            var placement = new MapPlacement { Tile = target, KeepPlacing = keepPlacing };
            if (target != null)
            {
                var rect = TileRect(target);
                // Centimetres are precise enough for a click and keep the written numbers short.
                placement.LocalX = Math.Round(map.x - rect.xMin, 2);
                placement.LocalY = Math.Round(map.y - rect.yMin, 2);
            }
            PlacementRequested?.Invoke(placement);
        }

        /// <summary>
        /// A click on an object: Ctrl adds or removes it, a member of a larger selection keeps the group for dragging,
        /// any other object becomes the selection. With editing allowed, dragging moves the selection.
        /// </summary>
        private void SelectPicked(OmsiTileSummary tile, OmsiObjectRecord item, bool toggle, int pointerId, Vector2 position)
        {
            if (toggle)
            {
                ObjectToggled?.Invoke(tile, item);
            }
            else if (selection.Count > 1 && selectedRecords.Contains(item))
            {
                clickedMember = new MapSelectionItem { Tile = tile, Record = item };
                if (AllowEditing)
                    StartObjectDrag(DragMode.MovePending, item, pointerId, position);
                else
                    ObjectSelected?.Invoke(tile, item);
            }
            else
            {
                // The shell replaces the selection with this object before the drag starts.
                ObjectSelected?.Invoke(tile, item);
                if (AllowEditing)
                    StartObjectDrag(DragMode.MovePending, item, pointerId, position);
            }
            MarkDirtyRepaint();
        }

        /// <summary>
        /// A left click in the 3D view, like one from above: in placement mode it places on the ground; on a rotation
        /// ring it starts turning the selected object; on an object, by its model or its marker, it selects it and may
        /// start moving it; Ctrl on empty space starts a selection box; otherwise it selects the tile there.
        /// </summary>
        private void PointerDown3D(Vector2 position, int pointerId, bool toggle, bool keepPlacing)
        {
            var camera = OrbitCamera();
            var ray = camera.RayThrough(position);
            var ground = GroundPoint(ray);
            if (placementAsset != null)
            {
                if (ground.HasValue)
                    RequestPlacement(new Vector2(ground.Value.x, ground.Value.z), keepPlacing);
                return;
            }

            if (!toggle && RingAt(camera, position, out var ring, out var ringPoint))
            {
                StartTurn(ring, ringPoint, pointerId, position);
                return;
            }

            if (TryPick3D(camera, position, out var tile, out var item, out var grabPoint))
            {
                moveGrabPoint = grabPoint;
                SelectPicked(tile, item, toggle, pointerId, position);
                return;
            }

            if (toggle)
            {
                BeginDrag(DragMode.Box, pointerId, position);
                return;
            }
            var clickedTile = ground.HasValue ? TileAt(new Vector2(ground.Value.x, ground.Value.z)) : null;
            if (clickedTile != null)
                TileClicked?.Invoke(clickedTile);
        }

        /// <summary>Objects without a model show a marker in 3D up to this distance from the camera.</summary>
        private const float MarkerReach = 400f;

        /// <summary>
        /// The object under an element position in 3D: the nearest model along the ray, or a marker of an object
        /// without model within <see cref="PickRadius"/> pixels when it lies in front of that model.
        /// </summary>
        private bool TryPick3D(MapOrbitCamera camera, Vector2 position, out OmsiTileSummary tile, out OmsiObjectRecord item,
            out Vector3 grabPoint)
        {
            tile = null;
            item = null;
            grabPoint = default;
            var ray = camera.RayThrough(position);
            var modelDistance = float.MaxValue;
            if (ObjectRenderer != null && ObjectRenderer.TryPick(ray, out tile, out item, out modelDistance))
                grabPoint = ray.GetPoint(modelDistance);

            var nearest = PickRadius;
            foreach (var marker in Markers(camera))
            {
                var distance = Vector2.Distance(marker.Screen, position);
                var depth = Vector3.Distance(camera.Position, marker.World);
                if (distance > nearest || depth > modelDistance)
                    continue;
                nearest = distance;
                tile = marker.Tile;
                item = marker.Record;
                grabPoint = marker.World;
            }
            return item != null;
        }

        private struct Marker
        {
            public OmsiTileSummary Tile;
            public OmsiObjectRecord Record;
            public Vector3 World;
            public Vector2 Screen;
        }

        /// <summary>
        /// Objects without a model that the 3D view shows as markers: on the ground under their position plus their
        /// height, within <see cref="MarkerReach"/> of the camera and in front of it.
        /// </summary>
        private IEnumerable<Marker> Markers(MapOrbitCamera camera)
        {
            if (summary == null || hasModel == null)
                yield break;
            var eye = new Vector2(camera.Position.x, camera.Position.z);
            foreach (var tile in summary.Tiles)
            {
                foreach (var item in tile.Objects)
                {
                    var map = ToMap(tile, item.LocalX, item.LocalY);
                    if ((map - eye).sqrMagnitude > MarkerReach * MarkerReach || hasModel(item.AssetPath))
                        continue;
                    var world = new Vector3(map.x, (GroundHeight != null ? GroundHeight(map) : 0f) + (float)item.Height, map.y);
                    if (camera.TryProject(world, out var screen))
                        yield return new Marker { Tile = tile, Record = item, World = world, Screen = screen };
                }
            }
        }

        /// <summary>
        /// Where an object stands in 3D: its drawn frame when it has a model, else its marker position on the ground.
        /// </summary>
        private Vector3 WorldPosition(OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (ObjectRenderer != null && ObjectRenderer.TryGetPlacement(item, out var frame, out _))
                return frame.GetColumn(3);
            var map = ToMap(tile, item.LocalX, item.LocalY);
            return new Vector3(map.x, (GroundHeight != null ? GroundHeight(map) : 0f) + (float)item.Height, map.y);
        }

        /// <summary>Objects whose position appears inside the box between two element positions in 3D.</summary>
        private List<MapSelectionItem> ObjectsInBox3D(Vector2 first, Vector2 second)
        {
            var box = Rect.MinMaxRect(Mathf.Min(first.x, second.x), Mathf.Min(first.y, second.y), Mathf.Max(first.x, second.x),
                Mathf.Max(first.y, second.y));
            var camera = OrbitCamera();
            var result = new List<MapSelectionItem>();
            foreach (var tile in summary.Tiles)
                foreach (var item in tile.Objects)
                    if (camera.TryProject(WorldPosition(tile, item), out var screen) && box.Contains(screen))
                        result.Add(new MapSelectionItem { Tile = tile, Record = item });
            return result;
        }

        /// <summary>The 3D camera as the ground renderer places it, looking at the ground under <see cref="OrbitTarget"/>.</summary>
        private MapOrbitCamera OrbitCamera()
        {
            var height = GroundHeight != null ? GroundHeight(OrbitTarget) : 0f;
            return new MapOrbitCamera(new Vector3(OrbitTarget.x, height, OrbitTarget.y), OrbitYaw, OrbitPitch, OrbitDistance, layout.size);
        }

        /// <summary>Where a ray meets the ground, found by stepping between ground heights; null when it points upwards.</summary>
        private Vector3? GroundPoint(Ray ray)
        {
            var height = GroundHeight != null ? GroundHeight(OrbitTarget) : 0f;
            Vector3 point = default;
            for (var step = 0; step < 4; step++)
            {
                if (!TryIntersectHorizontal(ray, height, out point))
                    return null;
                if (GroundHeight == null)
                    break;
                height = GroundHeight(new Vector2(point.x, point.z));
            }
            return point;
        }

        /// <summary>Where a ray meets the horizontal plane at a height, in front of the camera and within reach.</summary>
        private static bool TryIntersectHorizontal(Ray ray, float height, out Vector3 point)
        {
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
                return false;
            var distance = (height - ray.origin.y) / ray.direction.y;
            if (distance <= 0f || distance > 20000f)
                return false;
            point = ray.GetPoint(distance);
            return true;
        }

        /// <summary>
        /// Centre and radius of the rotation rings: at the pivot of the only selected object, around its model and at
        /// least <see cref="MinimumRingPixels"/> across on screen. False when there are no rings to show.
        /// </summary>
        private bool TryGetRings(MapOrbitCamera camera, out Vector3 centre, out float radius)
        {
            centre = default;
            radius = 0f;
            if (!AllowEditing || selection.Count != 1 || selectedObject == null || ObjectRenderer == null ||
                dragMode == DragMode.Move || !ObjectRenderer.TryGetPlacement(selectedObject, out var frame, out var bounds))
                return false;
            centre = frame.GetColumn(3);
            var depth = camera.Depth(centre);
            if (depth <= 1f)
                return false;
            var reach = Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x), Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z));
            radius = Mathf.Max(reach * 1.15f, MinimumRingPixels / camera.PixelsPerMeter(depth));
            return true;
        }

        /// <summary>All three rings; objects that join splines, like crossings, keep pitch and bank and get only heading.</summary>
        private IEnumerable<RingAxis> VisibleRings()
        {
            yield return RingAxis.Heading;
            if (!ObjectRenderer.HasSplineHelper(selectedObject.AssetPath))
            {
                yield return RingAxis.Pitch;
                yield return RingAxis.Bank;
            }
        }

        /// <summary>The selected object's orientation, as previewed while a ring is dragged.</summary>
        private void CurrentOrientation(out double heading, out double pitch, out double bank)
        {
            var turning = dragMode == DragMode.Turn;
            heading = turning ? previewHeading : selectedObject.HeadingDegrees;
            pitch = turning ? previewPitch : selectedObject.PitchDegrees;
            bank = turning ? previewBank : selectedObject.BankDegrees;
        }

        /// <summary>
        /// The axis a ring turns about, in the order OMSI applies the angles: heading about the vertical, pitch about
        /// the right axis after heading, bank about the forward axis after heading and pitch.
        /// </summary>
        private static Vector3 RingNormal(RingAxis ring, double heading, double pitch)
        {
            switch (ring)
            {
                case RingAxis.Pitch:
                    return Quaternion.Euler(0f, (float)heading, 0f) * Vector3.right;
                case RingAxis.Bank:
                    return Quaternion.Euler((float)pitch, (float)heading, 0f) * Vector3.forward;
                default:
                    return Vector3.up;
            }
        }

        private static Vector3[] RingPoints(Vector3 centre, Vector3 normal, float radius)
        {
            var u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            var v = Vector3.Cross(normal, u);
            var points = new Vector3[RingSegments + 1];
            for (var index = 0; index <= RingSegments; index++)
            {
                var angle = 2f * Mathf.PI * index / RingSegments;
                points[index] = centre + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        /// <summary>The ring nearest to an element position within reach, and the point on it under the pointer.</summary>
        private bool RingAt(MapOrbitCamera camera, Vector2 position, out RingAxis ring, out Vector3 point)
        {
            ring = RingAxis.None;
            point = default;
            if (!TryGetRings(camera, out var centre, out var radius))
                return false;
            CurrentOrientation(out var heading, out var pitch, out _);
            var nearest = RingPickDistance;
            foreach (var axis in VisibleRings())
            {
                var points = RingPoints(centre, RingNormal(axis, heading, pitch), radius);
                for (var index = 0; index < RingSegments; index++)
                {
                    if (!camera.TryProject(points[index], out var a) || !camera.TryProject(points[index + 1], out var b))
                        continue;
                    var segment = b - a;
                    var along = segment.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(position - a, segment) / segment.sqrMagnitude) : 0f;
                    var distance = Vector2.Distance(a + segment * along, position);
                    if (distance >= nearest)
                        continue;
                    nearest = distance;
                    ring = axis;
                    point = Vector3.Lerp(points[index], points[index + 1], along);
                }
            }
            return ring != RingAxis.None;
        }

        private void StartTurn(RingAxis ring, Vector3 ringPoint, int pointerId, Vector2 position)
        {
            ObjectRenderer.TryGetPlacement(selectedObject, out var frame, out _);
            previewHeading = selectedObject.HeadingDegrees;
            previewPitch = selectedObject.PitchDegrees;
            previewBank = selectedObject.BankDegrees;
            activeRing = ring;
            turnCentre = frame.GetColumn(3);
            turnAxis = RingNormal(ring, previewHeading, previewPitch);
            turnLastVector = Vector3.ProjectOnPlane(ringPoint - turnCentre, turnAxis);
            turnStartAngle = ring == RingAxis.Pitch ? previewPitch : ring == RingAxis.Bank ? previewBank : previewHeading;
            turnAngle = turnStartAngle;
            BeginDrag(DragMode.Turn, pointerId, position);
            ShowAngle(position);
        }

        /// <summary>
        /// Follows the pointer around the ring: where its ray meets the ring's plane, the angle from the last such point
        /// adds to the turn. Shift rounds the angle to 15° steps.
        /// </summary>
        private void UpdateTurn(Vector2 position, bool snap)
        {
            var ray = OrbitCamera().RayThrough(position);
            // Seen edge-on, the plane gives no useful point; the turn waits until the view or pointer allows it.
            if (Mathf.Abs(Vector3.Dot(ray.direction, turnAxis)) < 0.02f || !new Plane(turnAxis, turnCentre).Raycast(ray, out var enter) ||
                enter > 20000f)
                return;
            var current = ray.GetPoint(enter) - turnCentre;
            if (current.sqrMagnitude < 1e-6f)
                return;
            turnAngle += Vector3.SignedAngle(turnLastVector, current, turnAxis);
            turnLastVector = current;
            var angle = snap ? Math.Round(turnAngle / 15.0) * 15.0 : turnAngle;
            switch (activeRing)
            {
                case RingAxis.Pitch:
                    previewPitch = HalfTurn(angle);
                    break;
                case RingAxis.Bank:
                    previewBank = HalfTurn(angle);
                    break;
                default:
                    previewHeading = (angle % 360.0 + 360.0) % 360.0;
                    break;
            }
            ShowAngle(position);
        }

        /// <summary>An angle between -180° and 180°.</summary>
        private static double HalfTurn(double degrees)
        {
            return degrees - 360.0 * Math.Floor((degrees + 180.0) / 360.0);
        }

        /// <summary>Shows the angle of the ring being dragged next to the pointer.</summary>
        private void ShowAngle(Vector2 position)
        {
            var name = activeRing == RingAxis.Pitch ? "Pitch" : activeRing == RingAxis.Bank ? "Bank" : "Heading";
            var value = activeRing == RingAxis.Pitch ? previewPitch : activeRing == RingAxis.Bank ? previewBank : previewHeading;
            angleLabel.text = name + " " + value.ToString("0.0") + "°";
            angleLabel.style.left = position.x + 16f;
            angleLabel.style.top = position.y - 28f;
            angleLabel.style.display = DisplayStyle.Flex;
        }

        private void UpdateHoveredRing(Vector2 position)
        {
            var ring = RingAxis.None;
            if (ThreeDimensional && hasView && summary != null)
                RingAt(OrbitCamera(), position, out ring, out _);
            if (ring == hoveredRing)
                return;
            hoveredRing = ring;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// The 3D view's overlay: the selected tile's border on the ground, markers of objects without a model, boxes
        /// around the selected objects, the rotation rings, the placement preview and a selection box.
        /// </summary>
        private void Draw3D(Painter2D painter)
        {
            if (ObjectRenderer == null || layout.width < 1f || layout.height < 1f)
                return;
            var camera = OrbitCamera();
            DrawTileBorder3D(painter, camera);
            DrawMarkers3D(painter, camera);
            DrawPlacement3D(painter, camera);
            DrawBox(painter);
            painter.lineWidth = 1.5f;
            painter.strokeColor = SelectedObjectColor;
            for (var index = 0; index < selection.Count && index < 200; index++)
                if (ObjectRenderer.TryGetPlacement(selection[index].Record, out var frame, out var bounds))
                    DrawBoundingBox(painter, camera, frame, bounds);

            if (!TryGetRings(camera, out var centre, out var radius))
                return;
            CurrentOrientation(out var heading, out var pitch, out _);
            var turning = dragMode == DragMode.Turn;
            foreach (var axis in VisibleRings())
            {
                var colour = axis == RingAxis.Pitch ? PitchRingColor : axis == RingAxis.Bank ? BankRingColor : HeadingRingColor;
                var emphasised = turning ? axis == activeRing : axis == hoveredRing;
                if (turning && !emphasised)
                    colour.a = 0.35f;
                painter.strokeColor = colour;
                painter.lineWidth = emphasised ? 4f : 2.5f;
                StrokeWorldLine(painter, camera, RingPoints(centre, RingNormal(axis, heading, pitch), radius));
            }
        }

        /// <summary>A world point on the ground at a map position, lifted a little so it is not hidden in the terrain.</summary>
        private Vector3 OnGround(Vector2 map, float lift)
        {
            return new Vector3(map.x, (GroundHeight != null ? GroundHeight(map) : 0f) + lift, map.y);
        }

        /// <summary>Draws a line through world points, leaving gaps where points lie behind the camera.</summary>
        private static void StrokeWorldLine(Painter2D painter, MapOrbitCamera camera, IEnumerable<Vector3> points)
        {
            painter.BeginPath();
            var open = false;
            foreach (var point in points)
            {
                if (!camera.TryProject(point, out var screen))
                {
                    open = false;
                    continue;
                }
                if (open)
                    painter.LineTo(screen);
                else
                    painter.MoveTo(screen);
                open = true;
            }
            painter.Stroke();
        }

        /// <summary>The selected tile's border, following the ground every few metres.</summary>
        private void DrawTileBorder3D(Painter2D painter, MapOrbitCamera camera)
        {
            if (selectedTile == null)
                return;
            var rect = TileRect(selectedTile);
            const int steps = 40;
            var corners = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) };
            var points = new List<Vector3>(steps * 4 + 1);
            for (var side = 0; side < 4; side++)
                for (var step = 0; step < steps; step++)
                    points.Add(OnGround(Vector2.Lerp(corners[side], corners[(side + 1) % 4], (float)step / steps), 0.3f));
            points.Add(points[0]);
            painter.strokeColor = SelectedTileColor;
            painter.lineWidth = 2f;
            StrokeWorldLine(painter, camera, points);
        }

        /// <summary>Objects without a model: a dot and a short line in their heading, red when selected.</summary>
        private void DrawMarkers3D(Painter2D painter, MapOrbitCamera camera)
        {
            painter.lineWidth = 1.5f;
            foreach (var marker in Markers(camera))
            {
                var selected = selectedRecords.Contains(marker.Record);
                painter.strokeColor = selected ? SelectedObjectColor : (Color)ObjectColor;
                painter.fillColor = painter.strokeColor;
                painter.BeginPath();
                painter.Arc(marker.Screen, selected ? 5f : 3.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                var heading = (float)(marker.Record.HeadingDegrees * Math.PI / 180.0);
                var ahead = marker.World + new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * 1.5f;
                if (camera.TryProject(ahead, out var tip))
                {
                    painter.BeginPath();
                    painter.MoveTo(marker.Screen);
                    painter.LineTo(tip);
                    painter.Stroke();
                }
            }
        }

        /// <summary>The placement preview on the ground under the pointer: the outline in its heading and a heading line.</summary>
        private void DrawPlacement3D(Painter2D painter, MapOrbitCamera camera)
        {
            if (placementAsset == null || !placementPointer.HasValue)
                return;
            var position = placementPointer.Value;
            var heading = (float)(placementHeading * Math.PI / 180.0);
            var sin = Mathf.Sin(heading);
            var cos = Mathf.Cos(heading);
            painter.strokeColor = PlacementColor;
            painter.lineWidth = 2f;
            if (footprints.TryGetValue(placementAsset, out var footprint))
            {
                foreach (var contour in footprint.Contours)
                {
                    var points = new List<Vector3>(contour.Length + 1);
                    foreach (var local in contour)
                        points.Add(OnGround(ObjectToMap(local, position, sin, cos), 0.2f));
                    if (points.Count > 0)
                        points.Add(points[0]);
                    StrokeWorldLine(painter, camera, points);
                }
            }
            var length = Mathf.Max(2f, footprint?.Radius ?? 0f);
            StrokeWorldLine(painter, camera, new[] { OnGround(position, 0.2f), OnGround(position + new Vector2(sin, cos) * length, 0.2f) });
        }

        private static readonly int[] BoxEdges = { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };

        private static void DrawBoundingBox(Painter2D painter, MapOrbitCamera camera, Matrix4x4 frame, Bounds bounds)
        {
            var corners = new Vector2[8];
            var visible = new bool[8];
            for (var index = 0; index < 8; index++)
            {
                var local = new Vector3((index & 1) == 0 ? bounds.min.x : bounds.max.x, (index & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (index & 4) == 0 ? bounds.min.z : bounds.max.z);
                visible[index] = camera.TryProject(frame.MultiplyPoint3x4(local), out corners[index]);
            }
            painter.BeginPath();
            for (var edge = 0; edge < BoxEdges.Length; edge += 2)
            {
                var a = BoxEdges[edge];
                var b = BoxEdges[edge + 1];
                if (!visible[a] || !visible[b])
                    continue;
                painter.MoveTo(corners[a]);
                painter.LineTo(corners[b]);
            }
            painter.Stroke();
        }

        /// <param name="anchor">The object under the pointer; the whole selection moves by its offset.</param>
        private void StartObjectDrag(DragMode mode, OmsiObjectRecord anchor, int pointerId, Vector2 position)
        {
            dragStartLocalX = anchor.LocalX;
            dragStartLocalY = anchor.LocalY;
            previewLocalX = anchor.LocalX;
            previewLocalY = anchor.LocalY;
            previewHeading = anchor.HeadingDegrees;
            previewPitch = anchor.PitchDegrees;
            previewBank = anchor.BankDegrees;
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
                UpdateHoveredRing(position);
                NotifyPointerInfo(position);
                return;
            }

            switch (dragMode)
            {
                case DragMode.Orbit:
                    var turn = position - dragLastPointer;
                    SetOrbit(OrbitYaw + turn.x * 0.3f, OrbitPitch + turn.y * 0.3f, OrbitDistance);
                    break;
                case DragMode.OrbitPan:
                    // The ground follows the pointer: forward and right of the camera on the map.
                    var shift = (position - dragLastPointer) * OrbitDistance * 0.0015f;
                    var yaw = OrbitYaw * Mathf.Deg2Rad;
                    var forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
                    var right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
                    OrbitTarget += forward * shift.y - right * shift.x;
                    break;
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

            if (dragMode == DragMode.Move && ThreeDimensional)
            {
                // The grabbed point slides over its horizontal plane; the objects keep their height over the ground.
                if (TryIntersectHorizontal(OrbitCamera().RayThrough(position), moveGrabPoint.y, out var point))
                {
                    previewLocalX = dragStartLocalX + (point.x - moveGrabPoint.x);
                    previewLocalY = dragStartLocalY + (point.z - moveGrabPoint.z);
                }
            }
            else if (dragMode == DragMode.Move)
            {
                var moved = position - dragStartPointer;
                previewLocalX = dragStartLocalX + moved.x * metersPerPixel;
                previewLocalY = dragStartLocalY - moved.y * metersPerPixel;
            }
            else if (dragMode == DragMode.Turn)
            {
                UpdateTurn(position, evt.shiftKey);
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
            if (dragMode == DragMode.Move)
                MovePreviewed?.Invoke(selection, previewLocalX - dragStartLocalX, previewLocalY - dragStartLocalY);
            else if (dragMode == DragMode.Rotate)
                HeadingPreviewed?.Invoke(selectedTile, selectedObject, previewHeading);
            else if (dragMode == DragMode.Turn)
                OrientationPreviewed?.Invoke(PrimaryTile(), selectedObject, previewHeading, previewPitch, previewBank);
            NotifyPointerInfo(position);
            MarkDirtyRepaint();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (dragMode == DragMode.None || evt.pointerId != dragPointerId)
                return;

            var finished = dragMode;
            List<MapObjectEdit> edits = null;
            if (finished == DragMode.Move)
            {
                edits = new List<MapObjectEdit>();
                foreach (var item in selection)
                {
                    edits.Add(new MapObjectEdit
                    {
                        Tile = item.Tile,
                        Record = item.Record,
                        LocalX = item.Record.LocalX + previewLocalX - dragStartLocalX,
                        LocalY = item.Record.LocalY + previewLocalY - dragStartLocalY,
                        Height = item.Record.Height,
                        HeadingDegrees = item.Record.HeadingDegrees,
                        PitchDegrees = item.Record.PitchDegrees,
                        BankDegrees = item.Record.BankDegrees
                    });
                }
            }
            else if (finished == DragMode.Rotate)
            {
                edits = new List<MapObjectEdit>
                {
                    new MapObjectEdit
                    {
                        Tile = selectedTile, Record = selectedObject, LocalX = selectedObject.LocalX, LocalY = selectedObject.LocalY,
                        Height = selectedObject.Height, HeadingDegrees = previewHeading,
                        PitchDegrees = selectedObject.PitchDegrees, BankDegrees = selectedObject.BankDegrees
                    }
                };
            }
            else if (finished == DragMode.Turn)
            {
                edits = new List<MapObjectEdit>
                {
                    new MapObjectEdit
                    {
                        Tile = PrimaryTile(), Record = selectedObject, LocalX = selectedObject.LocalX, LocalY = selectedObject.LocalY,
                        Height = selectedObject.Height, HeadingDegrees = previewHeading, PitchDegrees = previewPitch, BankDegrees = previewBank
                    }
                };
            }
            var boxed = finished != DragMode.Box ? null
                : ThreeDimensional ? ObjectsInBox3D(dragStartPointer, dragLastPointer)
                : ObjectsInBox(dragStartPointer, dragLastPointer);
            var member = finished == DragMode.MovePending ? clickedMember : null;
            EndDrag();

            if (edits != null)
                ObjectsEdited?.Invoke(edits);
            if (boxed != null)
                ObjectsBoxSelected?.Invoke(boxed);
            if (member != null)
                ObjectSelected?.Invoke(member.Tile, member.Record);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        /// <summary>Objects whose position lies inside the box between two screen points.</summary>
        private List<MapSelectionItem> ObjectsInBox(Vector2 first, Vector2 second)
        {
            var a = ScreenToMap(first);
            var b = ScreenToMap(second);
            var box = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            var result = new List<MapSelectionItem>();
            foreach (var tile in summary.Tiles)
                foreach (var item in tile.Objects)
                    if (box.Contains(ToMap(tile, item.LocalX, item.LocalY)))
                        result.Add(new MapSelectionItem { Tile = tile, Record = item });
            return result;
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
            var previewed = IsDraggingObject();
            dragMode = DragMode.None;
            dragPointerId = -1;
            clickedMember = null;
            activeRing = RingAxis.None;
            angleLabel.style.display = DisplayStyle.None;
            if (pointerId >= 0 && this.HasPointerCapture(pointerId))
                this.ReleasePointer(pointerId);
            if (previewed)
                PreviewEnded?.Invoke();
        }

        private void OnWheel(WheelEvent evt)
        {
            if (summary == null || !hasView)
                return;
            if (ThreeDimensional)
            {
                SetOrbit(OrbitYaw, OrbitPitch, OrbitDistance * (evt.delta.y > 0f ? 1.15f : 1f / 1.15f));
                evt.StopPropagation();
                return;
            }
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
            // The 3D camera keeps its place when the view changes size.
            if ((!hasView || isFitted) && summary != null && !ThreeDimensional)
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
                    if (Contains(footprint.Contours, local))
                    {
                        smallest = footprint.Area;
                        pickedTile = tile;
                        pickedItem = item;
                    }
                }
            }
            return pickedItem != null;
        }

        /// <summary>Even-odd test over all contours, so a point in a courtyard is outside.</summary>
        private static bool Contains(Vector2[][] contours, Vector2 point)
        {
            var inside = false;
            foreach (var polygon in contours)
            {
                if (polygon.Length < 3)
                    continue;
                for (int index = 0, previous = polygon.Length - 1; index < polygon.Length; previous = index++)
                {
                    if ((polygon[index].y > point.y) != (polygon[previous].y > point.y) &&
                        point.x < (polygon[previous].x - polygon[index].x) * (point.y - polygon[index].y) /
                        (polygon[previous].y - polygon[index].y) + polygon[index].x)
                        inside = !inside;
                }
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

        /// <summary>The tile and tile-local position of a map position, or that it lies outside the map.</summary>
        private string DescribeMapPosition(Vector2? map)
        {
            var tile = map.HasValue ? TileAt(map.Value) : null;
            return tile == null
                ? "outside the map"
                : "Tile " + tile.X + ", " + tile.Y + "   x " + (map.Value.x - TileRect(tile).xMin).ToString("0.0") +
                  "   y " + (map.Value.y - TileRect(tile).yMin).ToString("0.0");
        }

        private void NotifyPointerInfo(Vector2? screen)
        {
            if (PointerInfoChanged == null || summary == null)
                return;

            if (ThreeDimensional)
            {
                var view = "3D view   distance " + OrbitDistance.ToString("0") + " m   heading " + OrbitYaw.ToString("0") + "°   tilt " +
                           OrbitPitch.ToString("0") + "°";
                if (dragMode == DragMode.Turn)
                    view = "#" + selectedObject.Id + "   heading " + previewHeading.ToString("0.0") + "°   pitch " + previewPitch.ToString("0.0") +
                           "°   bank " + previewBank.ToString("0.0") + "°      " + view;
                else if (dragMode == DragMode.Move)
                    view = (selection.Count > 1 ? selection.Count + " objects" : "#" + selectedObject.Id) + "   moved x " +
                           (previewLocalX - dragStartLocalX).ToString("+0.00;-0.00") + "   y " +
                           (previewLocalY - dragStartLocalY).ToString("+0.00;-0.00") + "      " + view;
                else if (screen.HasValue && hasView)
                {
                    var ground = GroundPoint(OrbitCamera().RayThrough(screen.Value));
                    view = DescribeMapPosition(ground.HasValue ? new Vector2(ground.Value.x, ground.Value.z) : (Vector2?)null) + "      " + view;
                }
                PointerInfoChanged(view);
                return;
            }
            var scale = "1 px = " + (metersPerPixel >= 1f ? metersPerPixel.ToString("0.#") : metersPerPixel.ToString("0.###")) + " m";
            if (!screen.HasValue)
            {
                PointerInfoChanged(scale);
                return;
            }

            var text = DescribeMapPosition(ScreenToMap(screen.Value));
            if (dragMode == DragMode.Move && selection.Count > 1)
                text = selection.Count + " objects   moved x " + (previewLocalX - dragStartLocalX).ToString("+0.00;-0.00") +
                       "   y " + (previewLocalY - dragStartLocalY).ToString("+0.00;-0.00");
            else if (IsDraggingObject())
                text = "#" + selectedObject.Id + "   x " + previewLocalX.ToString("0.00") + "   y " + previewLocalY.ToString("0.00") +
                       "   heading " + previewHeading.ToString("0.0") + "°";
            PointerInfoChanged(text + "      " + scale);
        }
    }
}
