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
    /// are visible, selects objects and, when editing is allowed, moves or rotates them by dragging, as the active
    /// <see cref="EditorTool"/> says. Ctrl+click and
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

        // Colours of the vector layer come from the design tokens: EditorShell.uss maps them to the custom properties
        // --map-* on .map-canvas. The values here are the token colours, used until the stylesheet is resolved.
        private static readonly CustomStyleProperty<Color> TileProperty = new CustomStyleProperty<Color>("--map-tile");
        private static readonly CustomStyleProperty<Color> TileBorderProperty = new CustomStyleProperty<Color>("--map-tile-border");
        private static readonly CustomStyleProperty<Color> MissingTileProperty = new CustomStyleProperty<Color>("--map-tile-missing");
        private static readonly CustomStyleProperty<Color> SelectedTileProperty = new CustomStyleProperty<Color>("--map-tile-selected");
        private static readonly CustomStyleProperty<Color> SplineProperty = new CustomStyleProperty<Color>("--map-spline");
        private static readonly CustomStyleProperty<Color> ObjectProperty = new CustomStyleProperty<Color>("--map-object");
        private static readonly CustomStyleProperty<Color> TreeProperty = new CustomStyleProperty<Color>("--map-tree");
        private static readonly CustomStyleProperty<Color> SelectionProperty = new CustomStyleProperty<Color>("--map-selection");
        private static readonly CustomStyleProperty<Color> PlacementProperty = new CustomStyleProperty<Color>("--map-placement");
        private static readonly CustomStyleProperty<Color> HandleProperty = new CustomStyleProperty<Color>("--map-handle");
        private static readonly CustomStyleProperty<Color> WireProperty = new CustomStyleProperty<Color>("--map-wire");
        private static readonly CustomStyleProperty<Color> FenceProperty = new CustomStyleProperty<Color>("--map-fence");
        private static readonly CustomStyleProperty<Color> RouteProperty = new CustomStyleProperty<Color>("--map-route");
        private static readonly CustomStyleProperty<Color> QuietRouteProperty = new CustomStyleProperty<Color>("--map-route-quiet");

        private Color tileColor = Hex(0x202630);
        private Color missingTileColor = Mix(Hex(0x202630), Hex(0xE06464), 0.3f);
        private Color tileBorderColor = Hex(0x414B59);
        private Color selectedTileColor = Hex(0xE6B450);
        private Color splineColor = Hex(0xF08A32, 0.95f);
        private Color quietSplineColor = Hex(0xF08A32, 0.5f);
        private Color32 objectColor = Hex(0x65A9D9, 0.95f);
        private Color headingColor = Hex(0x65A9D9, 0.55f);
        private Color selectedObjectColor = Hex(0xE06464);
        private Color outlineFillColor = Hex(0x65A9D9, 0.3f);
        private Color outlineStrokeColor = Hex(0x65A9D9, 0.85f);
        private Color treeFillColor = Hex(0x55B887, 0.3f);
        private Color treeStrokeColor = Hex(0x55B887, 0.7f);
        private Color placementColor = Hex(0x55B887);
        private Color placementFillColor = Hex(0x55B887, 0.25f);
        private Color boxFillColor = Hex(0xE06464, 0.12f);
        private Color handleFillColor = Hex(0x29313D);
        private Color wireColor = Hex(0xAEB8C5, 0.9f);
        private Color fenceColor = Hex(0x7E8A99, 0.95f);
        private Color routeColor = Hex(0xF2F4F7);
        private Color quietRouteColor = Hex(0xAEB8C5, 0.55f);
        private Color stopOutlineColor = Hex(0x171C23);

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

        /// <summary>The direction an axis arrow of the move tool moves the selection: east, north or up.</summary>
        private enum MoveAxis { None, East, North, Up }

        /// <summary>Arrows are this long on screen; their heads this large.</summary>
        private const float ArrowPixels = 80f;
        private const float ArrowHeadPixels = 11f;

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

            /// <summary>The convex hull, drawn instead of the contours when the object is only a few pixels large.</summary>
            public Vector2[] Hull;
        }

        /// <summary>Objects only this many pixels across are outlined by their hull; their contours would not show.</summary>
        private const float HullPixels = 12f;

        /// <summary>
        /// The objects of a tile with their outlines looked up once, and the map area they cover with their outlines, so
        /// a redraw skips tiles out of view instead of testing every object. Built again after edits and new outlines.
        /// </summary>
        private sealed class TileObjects
        {
            public OmsiTileSummary Tile;
            public Rect Bounds;
            public OmsiObjectRecord[] Records;

            /// <summary>The outline of each record, or null when it has none.</summary>
            public Footprint[] Footprints;
        }

        private readonly List<TileObjects> objectsByTile = new List<TileObjects>();
        private bool objectsByTileOutdated = true;

        private readonly Dictionary<string, Footprint> footprints = new Dictionary<string, Footprint>(StringComparer.OrdinalIgnoreCase);

        private enum DragMode { None, Pan, MovePending, Move, Rotate, Box, Orbit, OrbitPan, Turn }

        private sealed class SplinePath
        {
            public string AssetPath;
            public Vector2[] Points;
            public Rect Bounds;

            /// <summary>Overhead lines and fences are drawn as thin lines instead of road lines.</summary>
            public OmsiSplineLineKind LineKind;
        }

        private IReadOnlyDictionary<string, OmsiSplineLineKind> splineLineKinds = new Dictionary<string, OmsiSplineLineKind>();

        private readonly List<SplinePath> splines = new List<SplinePath>();

        /// <summary>A lane that timetable lines drive, once however many lines share it.</summary>
        private sealed class RouteLane
        {
            public Vector2[] Points;
            public Rect Bounds;
            public HashSet<int> Lines;
        }

        private sealed class RouteStop
        {
            public Vector2 Position;
            public string Name;
            public int ObjectId;
        }

        private readonly List<RouteLane> routeLanes = new List<RouteLane>();
        private readonly List<RouteStop> routeStops = new List<RouteStop>();
        private readonly List<HashSet<int>> routeLineStops = new List<HashSet<int>>();
        private readonly List<string> routeLineNames = new List<string>();
        private int highlightedLine = -1;
        private bool showRoutes;

        /// <summary>Stop names are written from this scale on, or further out for the stops of the highlighted line.</summary>
        private const float StopLabelMetersPerPixel = 1.2f;
        private const float HighlightedStopLabelMetersPerPixel = 6f;

        /// <summary>Average width of a character of a stop name at 12 px, for keeping names apart.</summary>
        private const float LabelCharacterPixels = 6.6f;

        /// <summary>Stops with the same name closer than this on screen get one name.</summary>
        private const float SameNamePixels = 80f;
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

        // Move tool: the arrow under the pointer and the one being dragged. A drag along an arrow keeps the offset
        // along its axis, in metres from where the drag started; the height offset applies to the whole selection.
        private MoveAxis hoveredArrow;
        private MoveAxis activeArrow;
        private Vector3 arrowOrigin;
        private float arrowStartParameter;
        private double previewHeightOffset;

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
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);

            angleLabel = new Label { pickingMode = PickingMode.Ignore };
            angleLabel.AddToClassList("map-angle-label");
            angleLabel.style.display = DisplayStyle.None;
            Add(angleLabel);
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            var style = evt.customStyle;
            if (style.TryGetValue(TileProperty, out var tile))
                tileColor = tile;
            if (style.TryGetValue(TileBorderProperty, out var border))
                tileBorderColor = border;
            if (style.TryGetValue(MissingTileProperty, out var missing))
                missingTileColor = Mix(tileColor, missing, 0.3f);
            if (style.TryGetValue(SelectedTileProperty, out var selectedTile))
                selectedTileColor = selectedTile;
            if (style.TryGetValue(SplineProperty, out var spline))
            {
                splineColor = WithAlpha(spline, 0.95f);
                quietSplineColor = WithAlpha(spline, 0.5f);
            }
            if (style.TryGetValue(ObjectProperty, out var objectColorValue))
            {
                objectColor = WithAlpha(objectColorValue, 0.95f);
                headingColor = WithAlpha(objectColorValue, 0.55f);
                outlineFillColor = WithAlpha(objectColorValue, 0.3f);
                outlineStrokeColor = WithAlpha(objectColorValue, 0.85f);
            }
            if (style.TryGetValue(TreeProperty, out var tree))
            {
                treeFillColor = WithAlpha(tree, 0.3f);
                treeStrokeColor = WithAlpha(tree, 0.7f);
            }
            if (style.TryGetValue(SelectionProperty, out var selected))
            {
                selectedObjectColor = selected;
                boxFillColor = WithAlpha(selected, 0.12f);
            }
            if (style.TryGetValue(PlacementProperty, out var placement))
            {
                placementColor = placement;
                placementFillColor = WithAlpha(placement, 0.25f);
            }
            if (style.TryGetValue(HandleProperty, out var handle))
                handleFillColor = handle;
            if (style.TryGetValue(WireProperty, out var wire))
                wireColor = WithAlpha(wire, 0.9f);
            if (style.TryGetValue(FenceProperty, out var fence))
                fenceColor = WithAlpha(fence, 0.95f);
            if (style.TryGetValue(RouteProperty, out var route))
                routeColor = route;
            if (style.TryGetValue(QuietRouteProperty, out var quietRoute))
                quietRouteColor = WithAlpha(quietRoute, 0.55f);
            if (style.TryGetValue(TileProperty, out var outline))
                stopOutlineColor = outline;
            MarkDirtyRepaint();
        }

        private static Color Hex(int rgb, float alpha = 1f)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color Mix(Color from, Color to, float amount)
        {
            var mixed = Color.Lerp(from, to, amount);
            mixed.a = 1f;
            return mixed;
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
        public event Action<IReadOnlyList<MapSelectionItem>, double, double, double> MovePreviewed;

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

        /// <summary>
        /// The active tool: dragging an object moves the selection freely with <see cref="EditorTool.Drag"/> and
        /// <see cref="EditorTool.Move"/>, which also shows axis arrows; the rotation handle and rings appear only with
        /// <see cref="EditorTool.Rotate"/>. Selecting works with every tool.
        /// </summary>
        public EditorTool Tool
        {
            get => tool;
            set
            {
                if (tool == value)
                    return;
                CancelDrag();
                tool = value;
                hoveredRing = RingAxis.None;
                hoveredArrow = MoveAxis.None;
                MarkDirtyRepaint();
            }
        }

        private EditorTool tool;

        private bool CanMove => AllowEditing && (tool == EditorTool.Drag || tool == EditorTool.Move);

        private bool HasArrows => AllowEditing && tool == EditorTool.Move && selection.Count > 0 && selectedObject != null;

        private bool CanRotate => AllowEditing && tool == EditorTool.Rotate;

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
            // Routes are relative to the origin of the map they were built for.
            SetRoutes(null);
            objectsByTileOutdated = true;
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

        /// <summary>Shows the stops and the lanes the timetable lines drive, in the view from above.</summary>
        public bool ShowRoutes
        {
            get => showRoutes;
            set
            {
                showRoutes = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Takes the routes of the loaded map; null removes them.</summary>
        public void SetRoutes(OmsiTimetableRoutes routes)
        {
            routeLanes.Clear();
            routeStops.Clear();
            routeLineStops.Clear();
            routeLineNames.Clear();
            highlightedLine = -1;
            if (routes != null && summary != null)
            {
                var lanes = new Dictionary<OmsiMapPoint[], RouteLane>();
                for (var line = 0; line < routes.Lines.Count; line++)
                {
                    routeLineNames.Add(routes.Lines[line].Name);
                    routeLineStops.Add(routes.Lines[line].StopIds);
                    foreach (var path in routes.Lines[line].Paths)
                    {
                        // Lines that share a lane share its points, so the lane is converted and drawn once.
                        if (!lanes.TryGetValue(path, out var lane))
                        {
                            var points = new Vector2[path.Length];
                            for (var index = 0; index < path.Length; index++)
                                points[index] = new Vector2((float)(path[index].X - originX), (float)(path[index].Y - originY));
                            lane = new RouteLane { Points = points, Bounds = Bounds(points), Lines = new HashSet<int>() };
                            lanes.Add(path, lane);
                            routeLanes.Add(lane);
                        }
                        lane.Lines.Add(line);
                    }
                }
                foreach (var stop in routes.Stops)
                    routeStops.Add(new RouteStop
                    {
                        Position = new Vector2((float)(stop.Position.X - originX), (float)(stop.Position.Y - originY)), Name = stop.Name, ObjectId = stop.ObjectId
                    });
            }
            MarkDirtyRepaint();
        }

        /// <summary>Draws one line above the others and names its stops further out; null or an unknown name shows all alike.</summary>
        public void HighlightLine(string name)
        {
            highlightedLine = name == null ? -1 : routeLineNames.IndexOf(name);
            MarkDirtyRepaint();
        }

        private static Rect Bounds(Vector2[] points)
        {
            var minimum = points[0];
            var maximum = points[0];
            foreach (var point in points)
            {
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        /// <summary>Draws again after objects changed: moved, turned, placed or deleted.</summary>
        public void Refresh()
        {
            objectsByTileOutdated = true;
            MarkDirtyRepaint();
        }

        /// <summary>The objects by tile, built again when objects or outlines changed since the last redraw.</summary>
        private List<TileObjects> ObjectsByTile()
        {
            if (!objectsByTileOutdated)
                return objectsByTile;
            objectsByTileOutdated = false;
            objectsByTile.Clear();
            if (summary == null)
                return objectsByTile;
            foreach (var tile in summary.Tiles)
            {
                if (tile.Objects.Count == 0)
                    continue;
                var group = new TileObjects
                {
                    Tile = tile, Records = tile.Objects.ToArray(), Footprints = new Footprint[tile.Objects.Count]
                };
                // Objects may lie outside their own tile, so the bounds follow the objects, not the tile rectangle.
                float minimumX = float.MaxValue, minimumY = float.MaxValue, maximumX = float.MinValue, maximumY = float.MinValue;
                for (var index = 0; index < group.Records.Length; index++)
                {
                    footprints.TryGetValue(group.Records[index].AssetPath, out var footprint);
                    group.Footprints[index] = footprint;
                    var position = ToMap(tile, group.Records[index].LocalX, group.Records[index].LocalY);
                    var radius = footprint?.Radius ?? 0f;
                    minimumX = Mathf.Min(minimumX, position.x - radius);
                    minimumY = Mathf.Min(minimumY, position.y - radius);
                    maximumX = Mathf.Max(maximumX, position.x + radius);
                    maximumY = Mathf.Max(maximumY, position.y + radius);
                }
                group.Bounds = Rect.MinMaxRect(minimumX, minimumY, maximumX, maximumY);
                objectsByTile.Add(group);
            }
            return objectsByTile;
        }

        /// <summary>
        /// True when a tile's objects may be in view. While objects are dragged they leave their tile's bounds, so then
        /// every tile counts.
        /// </summary>
        private bool MayBeVisible(TileObjects group, Rect visible)
        {
            return dragMode == DragMode.Move || activeArrow != MoveAxis.None || group.Bounds.Overlaps(visible);
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
                    footprint.Hull = new Vector2[hull.Length];
                    for (var index = 0; index < hull.Length; index++)
                        footprint.Hull[index] = new Vector2((float)hull[index].X, (float)hull[index].Y);
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
            objectsByTileOutdated = true;
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
                    splineLineKinds.TryGetValue(spline.AssetPath ?? string.Empty, out var lineKind);
                    splines.Add(new SplinePath { AssetPath = spline.AssetPath, Points = points, Bounds = bounds, LineKind = lineKind });
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
            var tilesDone = watch.Elapsed.TotalMilliseconds;
            DrawOutlines(painter, visible);
            var outlinesDone = watch.Elapsed.TotalMilliseconds;
            DrawSplines(painter, visible);
            var splinesDone = watch.Elapsed.TotalMilliseconds;
            DrawObjects(context, painter, visible);
            DrawRoutes(context, painter, visible);
            var objectsDone = watch.Elapsed.TotalMilliseconds;
            DrawSelection(painter);
            DrawArrows(painter);
            DrawPlacement(painter);
            DrawBox(painter);

            // Report slow redraws once per new maximum, so large maps can be diagnosed from the player log.
            var milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            if (milliseconds > 50f && milliseconds > slowestDrawMilliseconds * 1.5f)
            {
                slowestDrawMilliseconds = milliseconds;
                Debug.LogWarning("Map view redraw took " + milliseconds.ToString("0") + " ms at " + metersPerPixel.ToString("0.###") + " m/px: tiles " +
                                 tilesDone.ToString("0") + ", outlines " + (outlinesDone - tilesDone).ToString("0") + ", splines " +
                                 (splinesDone - outlinesDone).ToString("0") + ", objects " + (objectsDone - splinesDone).ToString("0") + ", rest " +
                                 (milliseconds - objectsDone).ToString("0") + " ms.");
            }
        }

        private void DrawTiles(Painter2D painter, Rect visible)
        {
            foreach (var tile in summary.Tiles)
            {
                var rect = TileRect(tile);
                if (!rect.Overlaps(visible) || tile.IsAvailable && TexturedView)
                    continue;
                painter.fillColor = tile.IsAvailable ? tileColor : missingTileColor;
                painter.BeginPath();
                AddRect(painter, rect);
                painter.Fill();
            }

            painter.strokeColor = tileBorderColor;
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
                painter.strokeColor = selectedTileColor;
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
                painter.fillColor = trees ? treeFillColor : outlineFillColor;
                painter.strokeColor = trees ? treeStrokeColor : outlineStrokeColor;
                painter.lineWidth = 1f;
                painter.BeginPath();
                var any = false;
                foreach (var group in ObjectsByTile())
                {
                    if (!MayBeVisible(group, visible))
                        continue;
                    for (var index = 0; index < group.Records.Length; index++)
                    {
                        var footprint = group.Footprints[index];
                        if (footprint == null || footprint.IsTree != trees || TexturedView && footprint.HasModel)
                            continue;
                        var item = group.Records[index];
                        var position = DrawnPosition(group.Tile, item);
                        var pixels = footprint.Radius / metersPerPixel;
                        if (pixels < 1.5f ||
                            position.x + footprint.Radius < visible.xMin || position.x - footprint.Radius > visible.xMax ||
                            position.y + footprint.Radius < visible.yMin || position.y - footprint.Radius > visible.yMax)
                            continue;
                        AddFootprint(painter, footprint, position, DrawnHeading(item), pixels < HullPixels);
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

        /// <param name="hullOnly">Draws the convex hull instead of the contours, for objects only a few pixels large.</param>
        private void AddFootprint(Painter2D painter, Footprint footprint, Vector2 position, double headingDegrees, bool hullOnly = false)
        {
            var heading = (float)(headingDegrees * Math.PI / 180.0);
            var sin = Mathf.Sin(heading);
            var cos = Mathf.Cos(heading);
            foreach (var points in hullOnly && footprint.Hull != null ? new[] { footprint.Hull } : footprint.Contours)
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

        /// <summary>
        /// Tells which spline types have no surface from above and are drawn as thin lines: overhead lines dashed, fences
        /// solid. The ground renderer knows them once it has read the spline profiles.
        /// </summary>
        public void SetSplineLineKinds(IReadOnlyDictionary<string, OmsiSplineLineKind> kinds)
        {
            splineLineKinds = kinds ?? new Dictionary<string, OmsiSplineLineKind>();
            foreach (var spline in splines)
            {
                splineLineKinds.TryGetValue(spline.AssetPath ?? string.Empty, out var lineKind);
                spline.LineKind = lineKind;
            }
            MarkDirtyRepaint();
        }

        private void DrawSplines(Painter2D painter, Rect visible)
        {
            DrawLineSplines(painter, visible, OmsiSplineLineKind.Fence);
            DrawLineSplines(painter, visible, OmsiSplineLineKind.Overhead);
            painter.strokeColor = TexturedView ? quietSplineColor : splineColor;
            painter.lineWidth = TexturedView ? 1.5f : Mathf.Clamp(6f / metersPerPixel, 1.5f, 6f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            foreach (var spline in splines)
            {
                if (!spline.Bounds.Overlaps(visible) || spline.LineKind != OmsiSplineLineKind.None)
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

        /// <summary>
        /// The lanes of the timetable lines over splines and objects, the highlighted line on top and wider, then the
        /// stops as dots and, close enough, their names.
        /// </summary>
        private void DrawRoutes(MeshGenerationContext context, Painter2D painter, Rect visible)
        {
            if (!showRoutes || routeStops.Count == 0 && routeLanes.Count == 0)
                return;

            var width = Mathf.Clamp(4f / metersPerPixel, 1.5f, 4f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            StrokeRouteLanes(painter, visible, false, highlightedLine >= 0 ? quietRouteColor : routeColor, width);
            if (highlightedLine >= 0)
            {
                StrokeRouteLanes(painter, visible, true, stopOutlineColor, width + 3f);
                StrokeRouteLanes(painter, visible, true, routeColor, width + 1f);
            }

            var highlightedStops = highlightedLine >= 0 ? routeLineStops[highlightedLine] : null;
            var radius = metersPerPixel < 2f ? 4f : 3f;
            var labels = new List<RouteStop>();
            painter.lineWidth = 1.5f;
            painter.strokeColor = stopOutlineColor;
            foreach (var stop in routeStops)
            {
                if (!visible.Contains(stop.Position))
                    continue;
                var onLine = highlightedStops != null && highlightedStops.Contains(stop.ObjectId);
                painter.fillColor = highlightedStops == null || onLine ? routeColor : quietRouteColor;
                painter.BeginPath();
                painter.Arc(MapToScreen(stop.Position), onLine ? radius + 1f : radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                painter.Stroke();
                if (metersPerPixel <= StopLabelMetersPerPixel || onLine && metersPerPixel <= HighlightedStopLabelMetersPerPixel)
                    labels.Add(stop);
            }

            // Names last, so lanes and dots do not cover them; a dark copy behind each keeps it readable on any ground.
            // A name that would overlap one already written is left out, and so is the second stop of a pair with the
            // same name, one for each direction.
            var written = new List<Rect>();
            var writtenNames = new List<KeyValuePair<string, Vector2>>();
            foreach (var stop in labels)
            {
                var screen = MapToScreen(stop.Position);
                var at = screen + new Vector2(radius + 4f, -8f);
                var area = new Rect(at.x, at.y, stop.Name.Length * LabelCharacterPixels, 15f);
                if (written.Exists(other => other.Overlaps(area)) ||
                    writtenNames.Exists(other => other.Key == stop.Name && (other.Value - screen).sqrMagnitude < SameNamePixels * SameNamePixels))
                    continue;
                written.Add(area);
                writtenNames.Add(new KeyValuePair<string, Vector2>(stop.Name, screen));
                context.DrawText(stop.Name, at + new Vector2(1f, 1f), 12f, stopOutlineColor, null);
                context.DrawText(stop.Name, at, 12f, routeColor, null);
            }
        }

        private void StrokeRouteLanes(Painter2D painter, Rect visible, bool highlighted, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            var any = false;
            foreach (var lane in routeLanes)
            {
                if (!lane.Bounds.Overlaps(visible) || highlighted && !lane.Lines.Contains(highlightedLine))
                    continue;
                any = true;
                var last = MapToScreen(lane.Points[0]);
                painter.MoveTo(last);
                for (var index = 1; index < lane.Points.Length; index++)
                {
                    var point = MapToScreen(lane.Points[index]);
                    if ((point - last).sqrMagnitude < 4f && index < lane.Points.Length - 1)
                        continue;
                    painter.LineTo(point);
                    last = point;
                }
            }
            if (any)
                painter.Stroke();
        }

        /// <summary>Overhead lines as dashes of 6 pixels with gaps of 4, fences as a solid line, both 1 pixel wide.</summary>
        private void DrawLineSplines(Painter2D painter, Rect visible, OmsiSplineLineKind kind)
        {
            const float dash = 6f;
            const float gap = 4f;
            var dashed = kind == OmsiSplineLineKind.Overhead;
            painter.strokeColor = dashed ? wireColor : fenceColor;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            var any = false;
            foreach (var spline in splines)
            {
                if (spline.LineKind != kind || !spline.Bounds.Overlaps(visible))
                    continue;
                any = true;
                var last = MapToScreen(spline.Points[0]);
                painter.MoveTo(last);
                // Distance into the current dash or gap, so the pattern runs on over the points of the line.
                var phase = 0f;
                for (var index = 1; index < spline.Points.Length; index++)
                {
                    var point = MapToScreen(spline.Points[index]);
                    if (!dashed)
                    {
                        painter.LineTo(point);
                        last = point;
                        continue;
                    }
                    var length = Vector2.Distance(last, point);
                    var travelled = 0f;
                    while (travelled < length)
                    {
                        var inDash = phase < dash;
                        var step = Mathf.Min(length - travelled, inDash ? dash - phase : dash + gap - phase);
                        travelled += step;
                        phase += step;
                        var at = Vector2.Lerp(last, point, travelled / length);
                        if (inDash)
                            painter.LineTo(at);
                        else
                            painter.MoveTo(at);
                        if (phase >= dash + gap)
                            phase = 0f;
                    }
                    last = point;
                }
            }
            if (any)
                painter.Stroke();
        }

        private void DrawObjects(MeshGenerationContext context, Painter2D painter, Rect visible)
        {
            var half = metersPerPixel < 0.2f ? 3f : metersPerPixel < 1f ? 2f : 1.5f;
            var showHeading = metersPerPixel < 0.6f;
            var markers = new List<Vector2>();
            // Far out many markers share a pixel; one each per 2 × 2 pixel cell looks the same and draws much less.
            var cellsAcross = (int)(layout.width / 2f) + 1;
            var cellsDown = (int)(layout.height / 2f) + 1;
            var occupied = metersPerPixel > 2f && cellsAcross > 0 && cellsDown > 0 ? new bool[cellsAcross * cellsDown] : null;
            if (showHeading)
            {
                painter.strokeColor = headingColor;
                painter.lineWidth = 1f;
                painter.BeginPath();
            }

            foreach (var group in ObjectsByTile())
            {
                if (!MayBeVisible(group, visible))
                    continue;
                for (var index = 0; index < group.Records.Length; index++)
                {
                    var footprint = group.Footprints[index];
                    if (TexturedView && footprint != null && footprint.HasModel)
                        continue;
                    var item = group.Records[index];
                    var map = DrawnPosition(group.Tile, item);
                    if (!visible.Contains(map))
                        continue;
                    var screen = MapToScreen(map);
                    if (occupied != null)
                    {
                        var cellX = (int)(screen.x / 2f);
                        var cellY = (int)(screen.y / 2f);
                        if (cellX >= 0 && cellY >= 0 && cellX < cellsAcross && cellY < cellsDown)
                        {
                            if (occupied[cellY * cellsAcross + cellX])
                                continue;
                            occupied[cellY * cellsAcross + cellX] = true;
                        }
                    }
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
            WriteQuads(context, markers, half, objectColor);
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

            painter.strokeColor = selectedObjectColor;
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

            if (CanRotate)
            {
                painter.fillColor = dragMode == DragMode.Rotate ? selectedObjectColor : handleFillColor;
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
            painter.strokeColor = placementColor;
            painter.lineWidth = 2f;
            if (footprints.TryGetValue(placementAsset, out var footprint))
            {
                painter.fillColor = placementFillColor;
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
            painter.fillColor = boxFillColor;
            painter.strokeColor = selectedObjectColor;
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

            if (!(evt.ctrlKey || evt.commandKey) && ArrowAt(position, out var arrow))
            {
                StartArrowDrag(arrow, evt.pointerId, position);
                evt.StopPropagation();
                return;
            }

            if (CanRotate && HasRotationHandle() &&
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
                if (CanMove)
                    StartObjectDrag(DragMode.MovePending, item, pointerId, position);
                else
                    ObjectSelected?.Invoke(tile, item);
            }
            else
            {
                // The shell replaces the selection with this object before the drag starts.
                ObjectSelected?.Invoke(tile, item);
                if (CanMove)
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
            if (!toggle && ArrowAt(position, out var arrow))
            {
                StartArrowDrag(arrow, pointerId, position);
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
                    var world = WorldPosition(tile, item);
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
            // A marker follows a move preview like a model does.
            var moving = dragMode == DragMode.Move && selectedRecords.Contains(item);
            var map = ToMap(tile, item.LocalX + (moving ? previewLocalX - dragStartLocalX : 0.0),
                item.LocalY + (moving ? previewLocalY - dragStartLocalY : 0.0));
            var height = item.Height + (moving ? previewHeightOffset : 0.0);
            return new Vector3(map.x, (GroundHeight != null ? GroundHeight(map) : 0f) + (float)height, map.y);
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

        private static Vector3 ArrowDirection(MoveAxis axis)
        {
            return axis == MoveAxis.East ? Vector3.right : axis == MoveAxis.North ? Vector3.forward : Vector3.up;
        }

        private static Color ArrowColor(MoveAxis axis)
        {
            // The colours of the rings: red east like the pitch axis, blue north like bank, green up like heading.
            return axis == MoveAxis.East ? PitchRingColor : axis == MoveAxis.North ? BankRingColor : HeadingRingColor;
        }

        private static string ArrowName(MoveAxis axis)
        {
            return axis == MoveAxis.East ? "X" : axis == MoveAxis.North ? "Y" : "Height";
        }

        /// <summary>
        /// The move tool's arrows on screen, from the primary selected object as it is drawn now: east and north from
        /// above, and up in 3D. Each runs from <paramref name="origin"/> to its tip.
        /// </summary>
        private List<KeyValuePair<MoveAxis, Vector2>> Arrows(out Vector2 origin)
        {
            var arrows = new List<KeyValuePair<MoveAxis, Vector2>>(3);
            origin = default;
            if (!HasArrows || summary == null || !hasView)
                return arrows;
            if (!ThreeDimensional)
            {
                origin = MapToScreen(DrawnPosition(PrimaryTile(), selectedObject));
                arrows.Add(new KeyValuePair<MoveAxis, Vector2>(MoveAxis.East, origin + new Vector2(ArrowPixels, 0f)));
                arrows.Add(new KeyValuePair<MoveAxis, Vector2>(MoveAxis.North, origin + new Vector2(0f, -ArrowPixels)));
                return arrows;
            }

            var camera = OrbitCamera();
            var world = WorldPosition(PrimaryTile(), selectedObject);
            var depth = camera.Depth(world);
            if (depth <= 1f || !camera.TryProject(world, out origin))
                return arrows;
            var length = ArrowPixels / camera.PixelsPerMeter(depth);
            foreach (var axis in new[] { MoveAxis.East, MoveAxis.North, MoveAxis.Up })
                if (camera.TryProject(world + ArrowDirection(axis) * length, out var tip))
                    arrows.Add(new KeyValuePair<MoveAxis, Vector2>(axis, tip));
            return arrows;
        }

        /// <summary>The arrow whose shaft or head lies within reach of an element position.</summary>
        private bool ArrowAt(Vector2 position, out MoveAxis axis)
        {
            axis = MoveAxis.None;
            var nearest = RingPickDistance;
            foreach (var arrow in Arrows(out var origin))
            {
                // The first part of the shaft stays free, so a click on the object itself does not grab an arrow.
                var start = Vector2.Lerp(origin, arrow.Value, 0.2f);
                var segment = arrow.Value - start;
                var along = segment.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(position - start, segment) / segment.sqrMagnitude) : 0f;
                var distance = Vector2.Distance(start + segment * along, position);
                if (distance >= nearest)
                    continue;
                nearest = distance;
                axis = arrow.Key;
            }
            return axis != MoveAxis.None;
        }

        private void StartArrowDrag(MoveAxis axis, int pointerId, Vector2 position)
        {
            StartObjectDrag(DragMode.Move, selectedObject, pointerId, position);
            activeArrow = axis;
            if (ThreeDimensional)
            {
                arrowOrigin = WorldPosition(PrimaryTile(), selectedObject);
                TryAxisParameter(OrbitCamera().RayThrough(position), out arrowStartParameter);
            }
            ShowDragLabel(position, ArrowName(axis) + " +0.00 m");
        }

        /// <summary>Where along the dragged arrow's axis a pointer ray passes closest, in metres from the arrow's origin.</summary>
        private bool TryAxisParameter(Ray ray, out float parameter)
        {
            parameter = 0f;
            var direction = ArrowDirection(activeArrow);
            var b = Vector3.Dot(direction, ray.direction);
            var denominator = 1f - b * b;
            // Looking along the axis, the pointer cannot say how far to go.
            if (denominator < 1e-3f)
                return false;
            var offset = arrowOrigin - ray.origin;
            parameter = (b * Vector3.Dot(ray.direction, offset) - Vector3.Dot(direction, offset)) / denominator;
            return true;
        }

        /// <summary>
        /// Follows the pointer along the dragged arrow: from above by the pointer's travel along the arrow, in 3D by the
        /// point of the axis nearest to the pointer's ray. Shift moves in whole metres.
        /// </summary>
        private void UpdateArrowMove(Vector2 position, bool snap)
        {
            double offset;
            if (ThreeDimensional)
            {
                if (!TryAxisParameter(OrbitCamera().RayThrough(position), out var parameter))
                    return;
                offset = parameter - arrowStartParameter;
            }
            else
            {
                var direction = activeArrow == MoveAxis.East ? Vector2.right : Vector2.down;
                offset = Vector2.Dot(position - dragStartPointer, direction) * metersPerPixel;
            }
            if (snap)
                offset = Math.Round(offset);
            previewLocalX = dragStartLocalX + (activeArrow == MoveAxis.East ? offset : 0.0);
            previewLocalY = dragStartLocalY + (activeArrow == MoveAxis.North ? offset : 0.0);
            previewHeightOffset = activeArrow == MoveAxis.Up ? offset : 0.0;
            ShowDragLabel(position, ArrowName(activeArrow) + " " + offset.ToString("+0.00;-0.00") + " m");
        }

        /// <summary>The move tool's arrows, the dragged one bright and the others faint while it is dragged.</summary>
        private void DrawArrows(Painter2D painter)
        {
            var arrows = Arrows(out var origin);
            var dragging = activeArrow != MoveAxis.None;
            foreach (var arrow in arrows)
            {
                var emphasised = dragging ? arrow.Key == activeArrow : arrow.Key == hoveredArrow;
                var colour = ArrowColor(arrow.Key);
                if (dragging && !emphasised)
                    colour.a = 0.35f;
                var tip = arrow.Value;
                var direction = (tip - origin).normalized;
                if (direction == Vector2.zero)
                    continue;
                var side = new Vector2(-direction.y, direction.x) * (ArrowHeadPixels * 0.5f);
                var neck = tip - direction * ArrowHeadPixels;
                painter.strokeColor = colour;
                painter.fillColor = colour;
                painter.lineWidth = emphasised ? 4f : 2.5f;
                painter.BeginPath();
                painter.MoveTo(origin);
                painter.LineTo(neck);
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(tip);
                painter.LineTo(neck + side);
                painter.LineTo(neck - side);
                painter.ClosePath();
                painter.Fill();
            }
        }

        /// <summary>
        /// Centre and radius of the rotation rings: at the pivot of the only selected object, around its model and at
        /// least <see cref="MinimumRingPixels"/> across on screen. False when there are no rings to show.
        /// </summary>
        private bool TryGetRings(MapOrbitCamera camera, out Vector3 centre, out float radius)
        {
            centre = default;
            radius = 0f;
            if (!CanRotate || selection.Count != 1 || selectedObject == null || ObjectRenderer == null ||
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
            ShowDragLabel(position, name + " " + value.ToString("0.0") + "°");
        }

        /// <summary>Shows what a drag changes, such as an angle or an offset, next to the pointer.</summary>
        private void ShowDragLabel(Vector2 position, string text)
        {
            angleLabel.text = text;
            angleLabel.style.left = position.x + 16f;
            angleLabel.style.top = position.y - 28f;
            angleLabel.style.display = DisplayStyle.Flex;
        }

        /// <summary>Highlights the ring or arrow under the pointer, so it is clear what a drag would take.</summary>
        private void UpdateHoveredHandles(Vector2 position)
        {
            var ring = RingAxis.None;
            if (ThreeDimensional && hasView && summary != null)
                RingAt(OrbitCamera(), position, out ring, out _);
            ArrowAt(position, out var arrow);
            if (ring == hoveredRing && arrow == hoveredArrow)
                return;
            hoveredRing = ring;
            hoveredArrow = arrow;
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
            painter.strokeColor = selectedObjectColor;
            for (var index = 0; index < selection.Count && index < 200; index++)
                if (ObjectRenderer.TryGetPlacement(selection[index].Record, out var frame, out var bounds))
                    DrawBoundingBox(painter, camera, frame, bounds);
            DrawArrows(painter);

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
            painter.strokeColor = selectedTileColor;
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
                painter.strokeColor = selected ? selectedObjectColor : (Color)objectColor;
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
            painter.strokeColor = placementColor;
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
            previewHeightOffset = 0.0;
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
                UpdateHoveredHandles(position);
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

            if (dragMode == DragMode.Move && activeArrow != MoveAxis.None)
            {
                UpdateArrowMove(position, evt.shiftKey);
            }
            else if (dragMode == DragMode.Move && ThreeDimensional)
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
                MovePreviewed?.Invoke(selection, previewLocalX - dragStartLocalX, previewLocalY - dragStartLocalY, previewHeightOffset);
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
                        Height = item.Record.Height + previewHeightOffset,
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
            activeArrow = MoveAxis.None;
            previewHeightOffset = 0.0;
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
                           (previewLocalY - dragStartLocalY).ToString("+0.00;-0.00") + "   height " +
                           previewHeightOffset.ToString("+0.00;-0.00") + "      " + view;
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
