using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.Core;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>Creates the UI Toolkit shell when the player starts and connects it to the opened map.</summary>
    public sealed partial class EditorShellBootstrap : MonoBehaviour
    {
        /// <summary>
        /// One object edit: either changed values, found again by tile coordinates and ID, or a placement or
        /// deletion that the session takes back file by file.
        /// </summary>
        private sealed class ObjectEditStep
        {
            public int TileX;
            public int TileY;
            public int ObjectId;
            public double[] Before;
            public double[] After;
            public OmsiObjectChange Change;

            /// <summary>Splines built or deleted; undone and redone as one step.</summary>
            public OmsiSplineChange SplineChange;

            /// <summary>Several steps done together, such as moving a selection; undone in reverse order.</summary>
            public List<ObjectEditStep> Group;

            /// <summary>What the step did, for the status bar, when the parts do not say it well; or null.</summary>
            public string Description;

            /// <summary>A tile added or deleted; the map is read again, so it stays the only step in the history.</summary>
            public OmsiTileChange TileChange;

            /// <summary>Rows of objects along roads laid or deleted.</summary>
            public OmsiAttachmentChange AttachmentChange;

            /// <summary>Objects hung on attachment points or taken off them.</summary>
            public OmsiAttachedObjectChange AttachedChange;

            /// <summary>Ground shaped with the terrain tool.</summary>
            public OmsiTerrainChange TerrainChange;

            /// <summary>A road type of the road builder changed or deleted in the installation (ADR 049).</summary>
            public OmsiRoadTypeChange RoadTypeChange;

            /// <summary>
            /// The edits of a tile discarded. The tile's records are swapped in place, so the history before and after it stays;
            /// it is always a step of its own, never part of a group.
            /// </summary>
            public OmsiTileRevertChange TileRevert;
        }

        private const int HistoryLimit = 200;
        private const double PlacementRotationStep = 15.0;

        /// <summary>A junction being placed snaps onto a road whose centre line passes this close to the pointer.</summary>
        private const double JunctionSnapMeters = 4.0;

        /// <summary>Plain asphalt without markings from the base game, for the carriageway of generated junctions.</summary>
        private const string PlainAsphalt = @"Splines\Marcel\texture\str_asphdrk.bmp";

        private OmsiMapSummary currentSummary;
        private readonly Dictionary<OmsiTileSummary, Button> browserEntries = new Dictionary<OmsiTileSummary, Button>();
        private readonly List<ObjectEditStep> undoSteps = new List<ObjectEditStep>();
        private readonly List<ObjectEditStep> redoSteps = new List<ObjectEditStep>();
        private Label statusLabel;
        private string statusKey;
        private (string Name, object Value)[] statusArguments;
        private Label mapStateLabel;
        private Label mapTitleLabel;
        private Label pointerInfoLabel;
        private Label selectionInfoLabel;
        private VisualElement emptyState;
        private VisualElement workspaceElement;
        private VisualElement inspectorFrame;
        private Button inspectorCollapseButton;
        private VisualElement toolbarHost;
        private MapToolbar toolbar;
        private EditorTool activeTool = EditorTool.Drag;
        private Label mapHintLabel;
        private Label zoomLabel;
        private Button checkMapMenuItem;
        private VisualElement projectPanel;
        private VisualElement inspectorPanel;
        private MapCanvasElement mapCanvas;
        private MapGroundRenderer groundRenderer;
        private MapObjectRenderer objectRenderer;
        private VisualElement rootElement;
        private Button groundToggleButton;
        private Button view3DButton;
        private Button routesToggleButton;

        // Stops and line routes of the timetable, built in the background after loading and after edits.
        private OmsiTimetableRoutes routes;
        private Task<OmsiTimetableRoutes> routesTask;
        private int routesGeneration;
        private string highlightedLine;
        private readonly Dictionary<string, Button> lineEntries = new Dictionary<string, Button>(StringComparer.Ordinal);

        // Road tool: the road being drawn starts at roadStart, continuing a spline end when roadStartJoin is set. Each
        // click builds the road to the click as splines and continues from its end; Esc ends the road.
        private RoadToolPanel roadPanel;
        private bool roadStarted;
        private OmsiMapPoint roadStart;
        private double? roadStartHeading;
        private double? roadStartHeight;
        private OmsiSplineEnd roadStartJoin;

        // A Curved road's control point once clicked, and the direction right angles are taken from at a free start: a
        // road near it, else north (ADR 043).
        private OmsiMapPoint? roadControl;
        private double roadReferenceHeading;

        /// <summary>A free start takes its right angles from a road this close.</summary>
        private const double RoadReferenceReach = 40.0;

        // False while a road is built with junctions or transitions, whose parts keep the corner they start at.
        private bool roundCorners = true;

        // The free end of the last parallel road on each side (-1 left, 1 right), by spline ID and end, so the next part
        // joins it; empty at a road's start (ADR 043).
        private readonly Dictionary<int, KeyValuePair<int, bool>> parallelEnds = new Dictionary<int, KeyValuePair<int, bool>>();

        // The splines of the parallel roads of the part before: the road goes on beside them, so it never makes junctions
        // with them, and they are cut back for a rounded corner instead.
        private readonly HashSet<int> parallelSplineIds = new HashSet<int>();

        // Guide lines of the free spline ends and connection points, for the connection points they were read with; null
        // reads them again. The lines the pointer last snapped to, to draw.
        private List<OmsiGuideLine> roadGuideLines;
        private List<OmsiJunctionPort> roadGuidePorts;
        private readonly List<OmsiGuideLine> roadGuideHits = new List<OmsiGuideLine>();

        /// <summary>Guide lines count whose origin lies this close to the pointer.</summary>
        private const double RoadGuideReach = 250.0;

        /// <summary>A guide line is drawn on this far past the point it gave.</summary>
        private const double RoadGuideOvershoot = 25.0;

        // The connection point of a junction object the road starts from, or null.
        private OmsiJunctionPort roadStartPort;

        // While the road tool is on: every connection point of the map and what it is joined to; null rebuilds them.
        private List<OmsiJunctionPort> roadPorts;
        private List<OmsiJunctionPortCheck> roadPortChecks;

        // The connection points of the scenery objects of the open map's installation.
        private OmsiJunctionCatalog junctionCatalog;
        private OmsiSplineProfileCatalog roadProfiles;
        // Where the editor-placed junctions lay when the terrain was last fitted to them; null until the first edit.
        private string junctionTerrainState;
        // The same junctions one by one, for fitting only around those that changed; null when the terrain may not be fitted to them.
        private IReadOnlyList<OmsiJunctionTerrainPlace> junctionTerrainPlaces;
        private string roadProfilesRoot;
        // Where the selected object is while it is dragged or turned, as in Values(), so its connection points follow; null otherwise.
        private double[] junctionPreview;
        private string junctionCatalogRoot;
        private OmsiMapPoint? roadPointer;
        private OmsiTerrainSampler roadTerrain;

        /// <summary>A road snaps to a free spline end within this many pixels.</summary>
        private const float RoadSnapPixels = 12f;

        // The end of the selected spline being dragged with its handle.
        private OmsiSplineEnd draggedSplineEnd;

        // True while the middle handle of the dragged spline bends it, rather than one of its ends moving.
        private bool bendingSpline;

        // The row tool (ADR 037): its options beside the object library, the object it lays, how the map's rows lie, the
        // rows laid out for picking, the road a first click started a row on, the row selected for changing or deleting
        // and the row last under the pointer.
        private RowToolOptions rowOptions;
        private string rowAsset;
        private OmsiSplineAttachmentLayout rowLayout;
        private List<KeyValuePair<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>, List<OmsiPlacedAttachment>>> rowObjects;
        private OmsiSplinePoint rowStart;
        private double rowStartAlong;
        private OmsiAttachmentRow selectedRow;
        private OmsiAttachmentRow hoveredRow;

        // The terrain tool (ADR 040): its options, the stroke while the button is held, the time since its last step and
        // where a ramp starts.
        private TerrainToolOptions terrainOptions;
        private OmsiTerrainStroke terrainStroke;
        private double terrainSince;
        private OmsiMapPoint? terrainRampStart;

        // A height map chosen for import, waiting for its settings.
        private OmsiGreyPicture pendingHeightMap;

        /// <summary>A held brush works this often, so the map can draw the ground between steps.</summary>
        private const double TerrainStepSeconds = 1.0 / 15.0;

        // Where a row was just laid; no new row is shown there until the pointer moves, so a second click cannot lay it twice.
        private OmsiMapPoint? rowLaidAt;

        /// <summary>A row snaps to a road within this many pixels, or at least <see cref="RowSnapMeters"/>, so pointing at a pavement finds its road.</summary>
        private const float RowSnapPixels = 24f;
        private const double RowSnapMeters = 15.0;

        // The junction made here whose arm is being turned at its handle, and that arm; null and -1 otherwise.
        private OmsiJunctionRebuild armRebuild;
        private int draggedArm = -1;

        // Junctions made here as read from their files, by installation and object file; null for other objects.
        private readonly Dictionary<string, OmsiGeneratedJunction> madeJunctions = new Dictionary<string, OmsiGeneratedJunction>(StringComparer.OrdinalIgnoreCase);

        // A spline clicked in the map; it is shown in the inspector and can be deleted.
        private OmsiTileSummary selectedSplineTile;
        private OmsiSplineRecord selectedSpline;

        // Chrono view: the map as OMSI loads it on a date, with the variants active then laid over the base map. It is
        // read-only, unless a variant is edited (ADR 042); the base map stays in baseSummary and returns unchanged.
        private OmsiMapSummary baseSummary;
        private List<OmsiChronoVariant> chronoVariants = new List<OmsiChronoVariant>();
        private DateTime? chronoDate;
        private Task<OmsiMapSummary> chronoTask;
        private int chronoGeneration;
        private TextField chronoDateField;

        // The variant whose objects are edited in the chrono view, or null; the form for a new variant while it is open.
        private OmsiChronoVariant editedVariant;
        private bool newVariantFormOpen;

        // A variant made in this project that was clicked once for deletion; the second click deletes it.
        private OmsiChronoVariant variantToDelete;

        /// <summary>
        /// Edits are possible: a project is open, the base map is shown, not a chrono date, and no map check or chrono
        /// overlay is reading the map's lists on a worker thread.
        /// </summary>
        private bool CanEdit => session != null && session.CanEdit && chronoDate == null && !ReadingMapInBackground;

        /// <summary>
        /// Objects can be placed, moved, turned and deleted: the base map can be edited, or a chrono variant is edited;
        /// roads, rows, the ground and tiles stay with the base map.
        /// </summary>
        private bool CanEditObjects => CanEdit || EditingVariant && !ReadingMapInBackground;

        /// <summary>
        /// The rules of objects and splines can be changed: in the base map, or in a chrono variant being edited, where they
        /// become [rule] and [kill_rule] commands of the variant (ADR 064). The lanes tool and the road builder stay with the base map.
        /// </summary>
        private bool CanEditRules => CanEdit || EditingVariant && !ReadingMapInBackground;

        private bool EditingVariant => editedVariant != null && chronoDate != null && session != null && session.EditedVariant != null;

        private bool ReadingMapInBackground => chronoTask != null || checkTask != null;

        // A routes build after edits waits for a pause, so a burst of edits or undos starts only one.
        private IVisualElementScheduledItem routesRebuild;

        // Textures were switched off in the view from above; the 3D view shows them and returns to that choice.
        private bool texturesHiddenAbove;
        private OmsiTileSummary selectedTile;
        private OmsiObjectRecord selectedObject;

        // All selected objects; selectedObject is the last of them.
        private readonly List<MapSelectionItem> selection = new List<MapSelectionItem>();
        private string currentProjectName = string.Empty;
        private OmsiMapSession session;
        private VisualElement exportDialog;
        private TextField exportFolderField;
        private TextField exportNameField;
        private VisualElement checkDialog;
        private Label checkSummaryLabel;
        private ScrollView checkSections;
        private Task<OmsiMapCheckReport> checkTask;
        private OmsiMapCheckReport lastCheck;

        // The objects made for the project as the last check found them, and the topic showing them.
        private OmsiMadeObjectScan madeObjects;
        private OmsiCheckSection madeObjectsSection;
        private List<OmsiChainOffsetFinding> staleChainOffsets;
        private OmsiCheckSection chainOffsetsSection;
        private VisualElement newMapDialog;
        private NewMapDialog newMapForm;
        private Label exportTargetLabel;
        private Button exportConfirmButton;
        private VisualElement editMenu;
        private VisualElement objectsPanelHost;
        private ObjectLibraryPanel objectsPanel;
        private bool objectsPanelOpen;
        private Label browserCountsLabel;

        // Scenery objects to place: the .sco files of the OMSI installation, listed once in the background.
        private readonly Dictionary<string, int> usedAssetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private OmsiInstallationCatalog installationCatalog;
        private Task<OmsiInstallationCatalog> installationScan;

        // Where the installation's files come from: base game, DLC or user content; null until the scan is done.
        private OmsiContentOrigins contentOrigins;
        private string installationScanRoot;
        private string placementAsset;
        private bool placementHasAbsoluteHeight;
        // How the junction being placed would sit in the road under the pointer; null when it places freely.
        private OmsiJunctionInsertionPlan placementPlan;
        private string placementSnapMessage;

        // The free attachment point the object being placed snapped onto, or null.
        private MapAttachSpot placementAttach;

        /// <summary>An object being placed snaps onto a free attachment point this close to the pointer, or at least <see cref="AttachSnapMeters"/>.</summary>
        private const float AttachSnapPixels = 16f;
        private const double AttachSnapMeters = 1.0;

        // Object outlines are computed in the background and kept per OMSI installation.
        private readonly Dictionary<string, OmsiObjectOutline> outlineCache = new Dictionary<string, OmsiObjectOutline>(StringComparer.OrdinalIgnoreCase);
        private string outlineInstallationRoot;
        private int outlineBuildGeneration;
        private bool outlinesBuilding;

        private void Awake()
        {
            // The interface language comes first: everything built after this asks it for its texts.
            LocalizationService.Initialize(Environment.GetCommandLineArgs());
            var uiObject = new GameObject("Editor UI");
            uiObject.SetActive(false);
            var document = uiObject.AddComponent<UIDocument>();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.scale = LoadSettings().@interface.scale;
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("EditorRuntimeTheme");
            if (panelSettings.themeStyleSheet == null)
                Debug.LogError("EditorRuntimeTheme.tss was not found in Resources.");
            document.panelSettings = panelSettings;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("EditorShell");
            if (document.visualTreeAsset == null)
            {
                Debug.LogError("EditorShell.uxml was not found in Resources.");
                return;
            }
            uiObject.SetActive(true);

            var root = document.rootVisualElement;
            rootElement = root;
            SetUpLocalization(root);
            SetUpSettings(root);
            SetUpContentFolders(root);
            SetUpParkList(root);
            SetUpTimetable(root);
            SetUpVehicles(root);
            SetUpTrafficCurves(root);
            root.style.flexGrow = 1f;
            root.style.width = Length.Percent(100f);
            root.style.height = Length.Percent(100f);
            var fileButton = root.Q<Button>("file-menu-button");
            var openMapMenuItem = root.Q<Button>("open-map-menu-item");
            var openProjectMenuItem = root.Q<Button>("open-project-menu-item");
            var saveProjectMenuItem = root.Q<Button>("save-project-menu-item");
            var exportMenuItem = root.Q<Button>("export-menu-item");
            exportDialog = root.Q<VisualElement>("export-dialog");
            RegisterOverlay(exportDialog, true);
            exportFolderField = root.Q<TextField>("export-folder-field");
            exportNameField = root.Q<TextField>("export-name-field");
            exportTargetLabel = root.Q<Label>("export-target-label");
            exportConfirmButton = root.Q<Button>("export-confirm-button");
            var exportCancelButton = root.Q<Button>("export-cancel-button");
            checkDialog = root.Q<VisualElement>("check-dialog");
            RegisterOverlay(checkDialog, true);
            checkSummaryLabel = root.Q<Label>("check-summary");
            checkSections = root.Q<ScrollView>("check-sections");
            var checkCopyButton = root.Q<Button>("check-copy-button");
            var checkCloseButton = root.Q<Button>("check-close-button");
            if (checkCopyButton != null)
                checkCopyButton.clicked += () =>
                {
                    if (lastCheck == null || currentSummary == null)
                        return;
                    GUIUtility.systemCopyBuffer = lastCheck.ToText(currentSummary.Name);
                    SetStatusT("mapcheck.copied");
                };
            if (checkCloseButton != null)
                checkCloseButton.clicked += () => HidePopup(checkDialog);
            var newMapMenuItem = root.Q<Button>("new-map-menu-item");
            newMapDialog = root.Q<VisualElement>("new-map-dialog");
            RegisterOverlay(newMapDialog, true);
            if (newMapDialog != null)
            {
                newMapForm = new NewMapDialog();
                newMapForm.Cancelled += () => HidePopup(newMapDialog);
                newMapForm.CreateRequested += CreateNewMap;
                newMapForm.SettingsRequested += ApplyMapSettings;
                newMapDialog.Add(newMapForm);
            }
            var fileMenu = root.Q<VisualElement>("file-menu");
            var helpMenu = root.Q<VisualElement>("help-menu");
            var viewMenu = root.Q<VisualElement>("view-menu");
            editMenu = root.Q<VisualElement>("edit-menu");
            var inspectorMenuItem = root.Q<Button>("inspector-menu-item");
            var placeObjectMenuItem = root.Q<Button>("place-object-menu-item");
            var deleteObjectMenuItem = root.Q<Button>("delete-object-menu-item");
            var refitTerrainMenuItem = root.Q<Button>("refit-terrain-menu-item");
            var testJunctionMenuItem = root.Q<Button>("test-junction-menu-item");
            var teeJunctionMenuItem = root.Q<Button>("tee-junction-menu-item");
            var crossJunctionMenuItem = root.Q<Button>("cross-junction-menu-item");
            var undoMenuItem = root.Q<Button>("undo-menu-item");
            var redoMenuItem = root.Q<Button>("redo-menu-item");
            objectsPanelHost = root.Q<VisualElement>("tool-panel-host");
            if (objectsPanelHost != null)
            {
                objectsPanel = new ObjectLibraryPanel();
                objectsPanel.ObjectChosen += path =>
                {
                    if (PickingElementType)
                        EndElementTypePick(path);
                    else if (pickingRowObject)
                        EndRowObjectPick(path);
                    else if (activeTool == EditorTool.Rows)
                        ChooseRowObject(path);
                    else
                        StartPlacement(path);
                };
                rowOptions = new RowToolOptions();
                rowOptions.SettingsChanged += () =>
                {
                    if (activeTool == EditorTool.Rows)
                        UpdateRowPreview();
                };
                rowOptions.Committed += () =>
                {
                    if (activeTool == EditorTool.Rows && selectedRow != null)
                        ChangeSelectedRow(null);
                };
                objectsPanel.CloseRequested += () =>
                {
                    if (PickingElementType)
                        EndElementTypePick(null);
                    else if (pickingRowObject)
                        EndRowObjectPick(null);
                    else
                        ShowObjectsPanel(false);
                };
                objectsPanel.PreviewRequested += path => objectRenderer?.RequestPreview(path);
                objectsPanel.HeadingChanged += heading =>
                {
                    if (mapCanvas != null)
                        mapCanvas.PlacementHeading = heading;
                };
                objectsPanel.RandomHeadingChanged += _ => RandomHeadingToggled();
                objectsPanel.HintChanged += (text, warning) => ShowToolHint(objectsPanel, text, warning);
                objectsPanelHost.Add(objectsPanel);
                roadPanel = new RoadToolPanel();
                roadPanel.style.display = DisplayStyle.None;
                roadPanel.CloseRequested += () =>
                {
                    if (PickingSplineType)
                        EndSplineTypePick(null);
                    else
                        SetTool(EditorTool.Drag);
                };
                roadPanel.ProfilePicked += path => EndSplineTypePick(path);
                roadPanel.HintChanged += (text, warning) => ShowToolHint(roadPanel, text, warning);
                roadPanel.SettingsChanged += UpdateRoadPreview;
                roadPanel.PathOptionsChanged += ApplyPathOptions;
                // Another mode starts the next part anew; a control point belongs to Curved.
                roadPanel.ShapeChanged += () => roadControl = null;
                objectsPanelHost.Add(roadPanel);
                CreateRoadBuilder();
                terrainOptions = new TerrainToolOptions();
                terrainOptions.style.display = DisplayStyle.None;
                terrainOptions.SettingsChanged += () =>
                {
                    if (activeTool == EditorTool.Terrain)
                        ShowTerrainBrush();
                };
                terrainOptions.CloseRequested += () => SetTool(EditorTool.Drag);
                terrainOptions.HintChanged += (text, warning) => ShowToolHint(terrainOptions, text, warning);
                terrainOptions.ExportRequested += () => ExportHeightMap(null);
                terrainOptions.ImportRequested += () => ChooseHeightMap(null);
                terrainOptions.ImportConfirmed += LayHeightMap;
                ConnectGroundPaint();
                objectsPanelHost.Add(terrainOptions);
                CreateLineTool(objectsPanelHost);
                CreateMarkingOptions();
                CreatePriorityOptions();
                CreateLaneOptions();
                CreateSignalOptions();
            }
            statusLabel = root.Q<Label>("status-message");
            toolHintLabel = root.Q<Label>("tool-hint");
            mapStateLabel = root.Q<Label>("map-state");
            SetStatusT("status.ready");
            if (mapStateLabel != null)
                mapStateLabel.text = Loc.T("status.no_map");
            mapTitleLabel = root.Q<Label>("map-title");
            mapHintLabel = root.Q<Label>("map-hint");
            pointerInfoLabel = root.Q<Label>("pointer-info");
            selectionInfoLabel = root.Q<Label>("selection-info");
            emptyState = root.Q<VisualElement>("empty-state");
            if (emptyState != null)
            {
                welcomeScreen = new WelcomeScreen();
                welcomeScreen.OpenProjectRequested += OpenProject;
                welcomeScreen.OpenMapRequested += OpenMap;
                welcomeScreen.NewMapRequested += () => OpenNewMapDialog(null);
                welcomeScreen.RecentChosen += OpenRecent;
                emptyState.Add(welcomeScreen);
            }
            workspaceElement = root.Q<VisualElement>("workspace");
            inspectorFrame = root.Q<VisualElement>("inspector-frame");
            inspectorCollapseButton = root.Q<Button>("inspector-collapse-button");
            zoomLabel = root.Q<Label>("zoom-label");
            projectPanel = root.Q<VisualElement>("project-panel");
            inspectorPanel = root.Q<VisualElement>("inspector-panel");
            var editButton = root.Q<Button>("edit-menu-button");
            var viewButton = root.Q<Button>("view-menu-button");
            checkMapMenuItem = root.Q<Button>("check-map-menu-item");
            var roadBuilderMenuItem = root.Q<Button>("road-builder-menu-item");
            var helpButton = root.Q<Button>("help-menu-button");
            var helpCloseButton = root.Q<Button>("help-menu-close-button");
            var fitMapButton = root.Q<Button>("fit-map-button");
            var zoomOutButton = root.Q<Button>("zoom-out-button");
            var zoomInButton = root.Q<Button>("zoom-in-button");
            var centerSelectionButton = root.Q<Button>("center-selection-button");
            groundToggleButton = root.Q<Button>("ground-toggle-button");
            view3DButton = root.Q<Button>("view3d-button");
            routesToggleButton = root.Q<Button>("routes-toggle-button");

            var mapView = root.Q<VisualElement>("map-view");
            if (mapView != null)
            {
                mapCanvas = new MapCanvasElement();
                mapCanvas.PlacementSnapper = SnapPlacement;
                mapCanvas.ObjectSelected += (tile, item) => SelectObject(tile, item, false);
                mapCanvas.TileClicked += tile => SelectTile(tile, false);
                mapCanvas.ContextRequested += ShowContextMenu;
                mapCanvas.TileAddRequested += AddTile;
                mapCanvas.ObjectToggled += ToggleObject;
                mapCanvas.ObjectsBoxSelected += AddToSelection;
                mapCanvas.ObjectsEdited += CommitEdits;
                mapCanvas.PlacementRequested += PlaceChosenObject;
                mapCanvas.SplineClicked += SelectSpline;
                mapCanvas.SplineToggled += ToggleSpline;
                mapCanvas.RoadPointerMoved += (x, y) =>
                {
                    roadPointer = new OmsiMapPoint(x, y);
                    if (rowLaidAt.HasValue && Math.Abs(x - rowLaidAt.Value.X) + Math.Abs(y - rowLaidAt.Value.Y) > 1.0)
                        rowLaidAt = null;
                    // The tool table says which tool follows the pointer (EditorShellTools.cs).
                    OnToolPointerMoved();
                };
                mapCanvas.RoadReleased += OnToolReleased;
                mapCanvas.RoadClicked += OnToolClicked;
                mapCanvas.SplineEndDragStarted += handle =>
                {
                    bendingSpline = handle == SplineHandleKind.Curve;
                    draggedSplineEnd = selectedSpline != null && selectedSplineTile != null
                        ? new OmsiSplineEnd(selectedSplineTile, selectedSpline, handle == SplineHandleKind.End)
                        : null;
                };
                mapCanvas.SplineEndDragged += (x, y, alt) => PreviewSplineEnd(x, y, false, alt);
                mapCanvas.SplineEndDropped += (x, y, alt) => PreviewSplineEnd(x, y, true, alt);
                mapCanvas.SplineEndDragCancelled += () =>
                {
                    draggedSplineEnd = null;
                    mapCanvas.ClearRoadPreview();
                    SetStatusT(bendingSpline ? "spline.cancel_bend" : "spline.cancel_move") /* i18n:keys */;
                };
                mapCanvas.ArmDragStarted += StartArmTurn;
                mapCanvas.ArmDragged += (x, y, alt) => PreviewArmTurn(x, y, false, alt);
                mapCanvas.ArmDropped += (x, y, alt) => PreviewArmTurn(x, y, true, alt);
                mapCanvas.ArmDragCancelled += () =>
                {
                    armRebuild = null;
                    mapCanvas.ClearRoadPreview();
                    SetStatusT("junction.turn_cancelled");
                };
                mapCanvas.PointerInfoChanged += text =>
                {
                    if (pointerInfoLabel != null)
                        pointerInfoLabel.text = text;
                    UpdateZoomLabel();
                };
                mapView.Add(mapCanvas);
            }

            toolbarHost = root.Q<VisualElement>("tool-bar-host");
            if (toolbarHost != null)
            {
                toolbar = new MapToolbar();
                toolbar.ToolChosen += SetTool;
                toolbar.DeleteRequested += DeleteSelectedObjects;
                toolbar.ObjectsRequested += () => ShowObjectsPanel(!objectsPanelOpen);
                toolbarHost.Add(toolbar);
            }

            DecorateButton(fitMapButton, "maximize", null);
            DecorateButton(zoomOutButton, "minus", null);
            DecorateButton(zoomInButton, "plus", null);
            DecorateButton(centerSelectionButton, "crosshair", null);
            DecorateButton(groundToggleButton, "layers", "view.textures");
            DecorateButton(view3DButton, "rotate-3d", "view.3d");
            DecorateButton(routesToggleButton, "route", "view.routes");
            CreatePathsButton();
            SetUpBottomBar(root);
            SetUpTooltips(root);
            DecorateButton(inspectorCollapseButton, "x", null);
            AddPanelIcon(inspectorFrame, "sliders-horizontal");

            if (inspectorCollapseButton != null)
                inspectorCollapseButton.clicked += () => ShowInspector(false);
            if (inspectorMenuItem != null)
                inspectorMenuItem.clicked += () =>
                {
                    HidePopup(viewMenu);
                    ShowInspector(!inspectorShown);
                };
            InitializeSatellite(root, viewMenu);

            if (fileButton != null)
                fileButton.clicked += () => TogglePopup(fileMenu, helpMenu, editMenu, viewMenu);
            if (openMapMenuItem != null)
                openMapMenuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenMap();
                };
            if (newMapMenuItem != null)
                newMapMenuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenNewMapDialog(null);
                };
            if (openProjectMenuItem != null)
                openProjectMenuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenProject();
                };
            if (saveProjectMenuItem != null)
                saveProjectMenuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    SaveProjectAs();
                };
            if (exportMenuItem != null)
                exportMenuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenExportDialog();
                };
            exportFolderField?.RegisterValueChangedCallback(_ => UpdateExportTarget());
            exportNameField?.RegisterValueChangedCallback(_ => UpdateExportTarget());
            if (exportConfirmButton != null)
                exportConfirmButton.clicked += ExportMap;
            if (exportCancelButton != null)
                exportCancelButton.clicked += () => HidePopup(exportDialog);
            if (fitMapButton != null)
                fitMapButton.clicked += FitMap;
            if (zoomOutButton != null)
                zoomOutButton.clicked += () => ChangeZoom(1f / 1.5f);
            if (zoomInButton != null)
                zoomInButton.clicked += () => ChangeZoom(1.5f);
            if (centerSelectionButton != null)
                centerSelectionButton.clicked += CenterSelection;
            if (groundToggleButton != null)
                groundToggleButton.clicked += ToggleGround;
            if (view3DButton != null)
                view3DButton.clicked += ToggleThreeDimensional;
            if (routesToggleButton != null)
            {
                routesToggleButton.SetEnabled(false);
                routesToggleButton.clicked += () => ShowRoutes(mapCanvas != null && !mapCanvas.ShowRoutes);
            }

            if (viewButton != null)
                viewButton.clicked += () => TogglePopup(viewMenu, fileMenu, helpMenu, editMenu);
            if (editButton != null)
                editButton.clicked += () => TogglePopup(editMenu, fileMenu, helpMenu, viewMenu);
            if (placeObjectMenuItem != null)
                placeObjectMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    ShowObjectsPanel(true);
                };
            if (deleteObjectMenuItem != null)
                deleteObjectMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    DeleteSelectedObjects();
                };
            if (refitTerrainMenuItem != null)
                refitTerrainMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    RefitRoadTerrain();
                };
            if (testJunctionMenuItem != null)
                testJunctionMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    CreateTestJunction();
                };
            if (teeJunctionMenuItem != null)
                teeJunctionMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    CreateTestJunction(null, 3);
                };
            if (crossJunctionMenuItem != null)
                crossJunctionMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    CreateTestJunction(null, 4);
                };
            if (undoMenuItem != null)
                undoMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    StepHistory(undoSteps, redoSteps, true);
                };
            if (redoMenuItem != null)
                redoMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    StepHistory(redoSteps, undoSteps, false);
                };
            if (checkMapMenuItem != null)
            {
                checkMapMenuItem.SetEnabled(false);
                checkMapMenuItem.clicked += () =>
                {
                    HidePopup(viewMenu);
                    ValidateLoadedMap();
                };
            }
            if (roadBuilderMenuItem != null)
                roadBuilderMenuItem.clicked += () =>
                {
                    HidePopup(viewMenu);
                    if (currentSummary != null)
                        ShowRoadBuilder(true);
                };
            if (helpButton != null)
                helpButton.clicked += () => TogglePopup(helpMenu, fileMenu, editMenu, viewMenu);
            if (helpCloseButton != null)
                helpCloseButton.clicked += () => HidePopup(helpMenu);
            InitializeBugReport(root, helpMenu);
            InitializeUpdates(root, helpMenu);

            // Shortcuts work anywhere except inside a text field, which keeps its own text undo and Delete key.
            root.RegisterCallback<KeyDownEvent>(OnShortcut, TrickleDown.TrickleDown);

            CreateBackgroundCamera();
            groundRenderer = new GameObject("Map Ground").AddComponent<MapGroundRenderer>();
            groundRenderer.Loaded += report =>
            {
                SetStatus(report);
                mapCanvas?.SetSplineLineKinds(groundRenderer.SplineLineKinds);
            };
            objectRenderer = groundRenderer.gameObject.AddComponent<MapObjectRenderer>();
            objectRenderer.SetCamera(groundRenderer.ViewCamera);
            objectRenderer.SetGround(groundRenderer);
            objectRenderer.Loaded += SetStatus;
            objectRenderer.ModelsChanged += () => mapCanvas?.UpdateModelFlags();
            if (mapCanvas != null)
            {
                mapCanvas.HasModel = objectRenderer.HasModel;
                mapCanvas.MovePreviewed += (items, offsetX, offsetY, offsetHeight) =>
                {
                    var objects = new List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>>(items.Count);
                    foreach (var item in items)
                        objects.Add(new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(item.Tile, item.Record));
                    objectRenderer.PreviewMove(objects, offsetX, offsetY, offsetHeight);
                };
                mapCanvas.HeadingPreviewed += objectRenderer.PreviewHeading;
                mapCanvas.OrientationPreviewed += objectRenderer.PreviewOrientation;
                mapCanvas.PlacementPreviewed += (asset, position, heading, height) =>
                {
                    objectRenderer.SetPlacementPreview(asset, position, heading, height);
                    // Hanging on a point, the preview takes the point's frame, turned by R against it.
                    if (placementAttach != null)
                        objectRenderer.SetPlacementFrame(placementAttach.Frame * Matrix4x4.Rotate(MapObjectRenderer.Orientation(mapCanvas.PlacementHeading, 0.0, 0.0)),
                            placementAttach.WorldX, placementAttach.WorldY);
                    else
                        objectRenderer.SetPlacementFrame(null, 0.0, 0.0);
                };
                mapCanvas.GroupPlacementPreviewed += ShowGroupPreview;
                mapCanvas.PreviewEnded += objectRenderer.EndPreview;
                mapCanvas.MovePreviewed += (items, offsetX, offsetY, offsetHeight) => PreviewJunctionMarkers(item => new[]
                {
                    item.LocalX + offsetX, item.LocalY + offsetY, item.Height + offsetHeight, item.HeadingDegrees, item.PitchDegrees, item.BankDegrees
                });
                mapCanvas.HeadingPreviewed += (tile, record, heading) => PreviewJunctionMarkers(item => new[]
                {
                    item.LocalX, item.LocalY, item.Height, heading, item.PitchDegrees, item.BankDegrees
                });
                mapCanvas.OrientationPreviewed += (tile, record, heading, pitch, bank) => PreviewJunctionMarkers(item => new[]
                {
                    item.LocalX, item.LocalY, item.Height, heading, pitch, bank
                });
                mapCanvas.GroupTurnPreviewed += edits =>
                {
                    foreach (var edit in edits)
                        objectRenderer.PreviewPlacement(edit.Tile, edit.Record, edit.LocalX, edit.LocalY, edit.Height, edit.HeadingDegrees,
                            edit.PitchDegrees, edit.BankDegrees);
                    PreviewJunctionMarkers(item =>
                    {
                        var edit = edits.Find(candidate => ReferenceEquals(candidate.Record, item));
                        return edit == null ? null : new[] { edit.LocalX, edit.LocalY, edit.Height, edit.HeadingDegrees, edit.PitchDegrees, edit.BankDegrees };
                    });
                };
                mapCanvas.PreviewEnded += () => PreviewJunctionMarkers(null);
                mapCanvas.ObjectRenderer = objectRenderer;
                objectRenderer.PreviewReady += (path, image) => objectsPanel?.SetPreview(path, image);
                objectRenderer.PreviewInUse = path => objectsPanel != null && objectsPanel.ShowsPreview(path);
                mapCanvas.GroundHeight = groundRenderer.HeightAt;
                mapCanvas.GroundVersion = () => groundRenderer.TerrainVersion;
            }
        }

        /// <summary>Keeps the ground camera on the screen rectangle and view of the map view.</summary>
        private void LateUpdate()
        {
            if (benchmarkFrames != null && Time.frameCount > benchmarkOpenFrame)
                benchmarkFrames.Add(Time.unscaledDeltaTime * 1000.0);
            RefreshToolHint();
            ReloadAfterPendingRollBack();
            if (groundRenderer == null || mapCanvas == null || rootElement == null)
                return;
            TickSignalPreview(Time.unscaledDeltaTime);
            PumpAerial();
            TickTerrain();
            var panelWidth = rootElement.worldBound.width;
            mapCanvas.FitInsets = FreeMapInsets();
            if (!mapCanvas.HasView || float.IsNaN(panelWidth) || panelWidth <= 0f)
            {
                groundRenderer.SetView(Rect.zero, Vector2.zero, 0f);
                return;
            }

            // Panel units to screen pixels; screen rectangles start at the bottom left.
            var scale = Screen.width / panelWidth;
            var bounds = mapCanvas.worldBound;
            var screenRect = new Rect(bounds.x * scale, Screen.height - bounds.yMax * scale, bounds.width * scale, bounds.height * scale);
            if (mapCanvas.ThreeDimensional)
            {
                var target = mapCanvas.OrbitTarget;
                groundRenderer.SetOrbitView(screenRect, new Vector3(target.x, groundRenderer.HeightAt(target), target.y), mapCanvas.OrbitYaw,
                    mapCanvas.OrbitPitch, mapCanvas.OrbitDistance);
                // Selection boxes and rings follow models that load, previews and undo, which arrive after the view drew.
                if (selection.Count > 0)
                    mapCanvas.Refresh();
                // The wheel and the camera drags change the distance inside the map view.
                UpdateZoomLabel();
            }
            else
            {
                groundRenderer.SetView(screenRect, mapCanvas.Center, bounds.height * mapCanvas.MetersPerPixel);
            }
        }

        /// <summary>Switches between editing the map from above and looking at it in 3D.</summary>
        private void ToggleThreeDimensional()
        {
            if (mapCanvas == null || groundRenderer == null || currentSummary == null)
                return;
            // The 3D view consists of the textured scene, so it shows textures and keeps the choice from above for later.
            if (mapCanvas.ThreeDimensional)
            {
                mapCanvas.LeaveOrbitView();
                if (texturesHiddenAbove)
                    SetTextures(false);
            }
            else
            {
                texturesHiddenAbove = !groundRenderer.GroundVisible;
                if (texturesHiddenAbove)
                    SetTextures(true);
                mapCanvas.EnterOrbitView();
            }
            groundToggleButton?.SetEnabled(!mapCanvas.ThreeDimensional);
            routesToggleButton?.SetEnabled(routes != null && !mapCanvas.ThreeDimensional);
            pathsToggleButton?.SetEnabled(!mapCanvas.ThreeDimensional);
            groundRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            if (objectRenderer != null)
                objectRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            view3DButton?.EnableInClassList("view-control-active", mapCanvas.ThreeDimensional);
            UpdateMapHeader();
            UpdateZoomLabel();
            SetStatusT(mapCanvas.ThreeDimensional ? (CanEdit ? "view.status.3d_edit" : "view.status.3d") : "view.status.top") /* i18n:keys */;
        }

        private void ToggleGround()
        {
            if (groundRenderer == null || mapCanvas == null || mapCanvas.ThreeDimensional)
                return;
            SetTextures(!groundRenderer.GroundVisible);
            SetStatusT(groundRenderer.GroundVisible ? "view.status.ground_shown" : "view.status.ground_hidden") /* i18n:keys */;
        }

        private void SetTextures(bool visible)
        {
            groundRenderer.GroundVisible = visible;
            if (objectRenderer != null)
                objectRenderer.ObjectsVisible = visible;
            mapCanvas.TexturedView = visible;
            mapCanvas.Refresh();
            groundToggleButton?.EnableInClassList("view-control-active", visible);
        }

        private void OpenMap()
        {
            try
            {
                var path = WindowsFileDialog.OpenFile(Loc.T("filedialog.open_config"), WindowsFileDialog.GlobalConfigFilter);
                if (string.IsNullOrEmpty(path))
                    return;

                // A map made with MapWorks opens with its project, ready to edit (ADR 052).
                var folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (OmsiNewMap.IsMadeHere(folder))
                {
                    OpenOwnMap(path, Path.GetFileName(folder), "Could not read map: ",
                        () => SetStatusT("map.opened_own", ("folder", folder)));
                    return;
                }
                OpenMapInBackground(path, string.Empty, null, "Could not read map: ", RememberCurrent);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("map.read_failed", ("reason", exception.Message));
            }
        }

        /// <summary>
        /// Command-line automation, so a check needs no clicks: <c>-openMap global.cfg</c> or <c>-openProject file.omproj</c>,
        /// <c>-view tileX tileY x y metresPerPixel</c>, <c>-orbit yaw pitch distance</c> for the 3D view of that place,
        /// <c>-select objectId</c>, <c>-tool drag|move|rotate</c>, <c>-plain</c> for plain tiles without textures,
        /// <c>-check</c> to run the map check and show its report,
        /// <c>-objects</c> to open the object library, with <c>-objectsTab library|groups|inmap|favorites</c>,
        /// <c>-objectsAt folder-or-group</c> and <c>-objectsSearch text</c>,
        /// <c>-fold project|inspector|both</c> to collapse panels,
        /// <c>-place Sceneryobjects\...\object.sco</c> with the
        /// preview at the centre of the view,
        /// <c>-newMap [template global.cfg]</c> without a map to show the new-map dialog,
        /// <c>-road tileX tileY x1 y1 x2 y2 ...</c> to click road points with the road tool (the last point is only
        /// pointed at unless <c>-roadBuild</c> follows, <c>-roadMode follow|smooth|manual</c> chooses the heights),
        /// <c>-selectSpline id</c>, <c>-moveSplineEnd id start|end tileX tileY x y</c> to drag a spline end there (built with
        /// <c>-moveCommit</c>),
        /// <c>-screenshot file.png</c>, taken once ground, objects and outlines are loaded, and
        /// <c>-quit</c>.
        /// </summary>
        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            string Argument(string name, int offset)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + offset < args.Length ? args[index + offset] : null;
            }

            var mapPath = Argument("-openMap", 1);
            var projectPath = Argument("-openProject", 1);
            var quit = Array.IndexOf(args, "-quit") >= 0;
            RunLocalizationArguments(args);
            // A session that ended without quitting asks for a bug report (ADR 053); -bugReport [crash] opens the form for a
            // check, with -bugReportTitle and -bugReportSend, which then only writes the files and logs the address.
            var crashed = BeginSession(quit);
            var bugReportArgument = Argument("-bugReport", 1);
            if (crashed || Array.IndexOf(args, "-bugReport") >= 0)
            {
                OpenBugReport(crashed || bugReportArgument == "crash");
                if (Argument("-bugReportTitle", 1) is string bugTitle)
                    bugReportForm?.SetTitle(bugTitle);
                if (Array.IndexOf(args, "-bugReportSend") >= 0)
                {
                    bugReportDryRun = quit || Argument("-screenshot", 1) != null;
                    SendBugReport();
                }
            }
            // Updates from GitHub's releases (ADR 054); after a crash the report comes first.
            StartUpdates(args, quit, crashed);
            // -createMap <template global.cfg> <folder> <name> makes a new map as the dialog does, and opens it (ADR 052).
            var createTemplate = mapPath == null && projectPath == null ? Argument("-createMap", 1) : null;
            if (createTemplate != null)
            {
                CreateNewMap(createTemplate, Argument("-createMap", 2), Argument("-createMap", 3), OmsiNewMapOptions.FromTemplate(createTemplate));
                while (OpeningMap)
                    yield return null;
                Debug.Log("Create map: " + statusLabel?.text);
            }
            else if (mapPath == null && projectPath == null)
            {
                if (Array.IndexOf(args, "-newMap") >= 0)
                    OpenNewMapDialog(Argument("-newMap", 1) is string template && !template.StartsWith("-", StringComparison.Ordinal) ? template : null);
                // Without a map a screenshot shows the welcome screen.
                var welcomeShot = Argument("-screenshot", 1);
                if (welcomeShot == null)
                    yield break;
                // A check for updates answers first, so the shot shows its outcome.
                var welcomeStarted = Time.realtimeSinceStartup;
                while (updateCancellation != null && Time.realtimeSinceStartup - welcomeStarted < 30f)
                    yield return null;
                for (var frame = 0; frame < 10; frame++)
                    yield return null;
                yield return new WaitForEndOfFrame();
                SaveScreenshot(welcomeShot);
                if (quit)
                    Application.Quit();
                yield break;
            }

            benchmarkOpenStartedAt = Time.realtimeSinceStartup;
            if (Array.IndexOf(args, "-benchmark") >= 0)
            {
                benchmarkFrames = new List<double>();
                benchmarkOpenFrame = Time.frameCount;
            }
            if (projectPath != null)
                OpenProjectFile(projectPath, false);
            else if (mapPath != null)
            {
                try
                {
                    // A map made with MapWorks opens with its project, as from the File menu.
                    var mapFolder = Path.GetDirectoryName(Path.GetFullPath(mapPath));
                    if (OmsiNewMap.IsMadeHere(mapFolder))
                        OpenOwnMap(mapPath, Path.GetFileName(mapFolder), "Could not read map: ", null);
                    else
                        OpenMapInBackground(mapPath, string.Empty, null, "Could not read map: ", null);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            while (OpeningMap)
                yield return null;
            benchmarkOpenFinishedAt = Time.realtimeSinceStartup;
            // Two frames for the layout, so the view has its size.
            yield return null;
            yield return null;
            if (currentSummary == null)
            {
                if (quit)
                    Application.Quit(1);
                yield break;
            }

            if (Argument("-view", 5) != null && int.TryParse(Argument("-view", 1), out var tileX) && int.TryParse(Argument("-view", 2), out var tileY) &&
                double.TryParse(Argument("-view", 3), NumberStyles.Float, CultureInfo.InvariantCulture, out var localX) &&
                double.TryParse(Argument("-view", 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var localY) &&
                float.TryParse(Argument("-view", 5), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            {
                var tile = currentSummary.Tiles.Find(candidate => candidate.X == tileX && candidate.Y == tileY);
                mapCanvas?.ShowArea(tile, localX, localY, scale);
                UpdateZoomLabel();
            }

            if (Argument("-orbit", 3) != null &&
                float.TryParse(Argument("-orbit", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var yaw) &&
                float.TryParse(Argument("-orbit", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch) &&
                float.TryParse(Argument("-orbit", 3), NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
            {
                ToggleThreeDimensional();
                mapCanvas?.SetOrbit(yaw, pitch, distance);
            }

            // -chronoNew <name> <yyyy-MM-dd> [<yyyy-MM-dd>] makes a chrono variant and edits it; -chronoEdit <folder or name> edits
            // one. Both come before selecting and moving, which then work in the variant; the status goes to the log as "Chrono edit:".
            var newVariantName = Argument("-chronoNew", 1);
            var editVariantName = Argument("-chronoEdit", 1);
            if (newVariantName != null && TryParseDay(Argument("-chronoNew", 2), out var newVariantStart))
                CreateChronoVariant(newVariantName, newVariantStart, TryParseDay(Argument("-chronoNew", 3), out var newVariantEnd) ? newVariantEnd : (DateTime?)null);
            else if (editVariantName != null)
            {
                var variant = chronoVariants.Find(candidate => string.Equals(candidate.Name, editVariantName, StringComparison.OrdinalIgnoreCase) ||
                                                               string.Equals(candidate.Title, editVariantName, StringComparison.OrdinalIgnoreCase));
                if (variant != null)
                    EditChronoVariant(variant);
                else
                    Debug.LogWarning("-chronoEdit: the map has no chrono variant " + editVariantName + ".");
            }
            if (newVariantName != null || editVariantName != null)
            {
                var chronoStarted = Time.realtimeSinceStartup;
                while (chronoTask != null && Time.realtimeSinceStartup - chronoStarted < 120f)
                    yield return null;
                Debug.Log("Chrono edit: " + statusLabel?.text);
            }

            RunRandomHeadingArguments(args, false);

            // Several IDs select several objects, as Ctrl+click does.
            for (var offset = 1; int.TryParse(Argument("-select", offset), NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectId); offset++)
            {
                foreach (var tile in currentSummary.Tiles)
                {
                    var item = tile.Objects.Find(candidate => candidate.Id == selectId);
                    if (item == null)
                        continue;
                    if (offset == 1)
                        SelectObject(tile, item, false);
                    else
                        ToggleObject(tile, item);
                    break;
                }
            }

            var toolName = Argument("-tool", 1);
            if (toolName != null && Enum.TryParse<EditorTool>(toolName, true, out var chosenTool))
                SetTool(chosenTool);
            if (mapCanvas != null && Argument("-localArrows", 0) != null)
                mapCanvas.LocalArrowAxes = true;

            RunRandomHeadingArguments(args, true);

            if (double.TryParse(Argument("-turnSelection", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var turnDegrees) &&
                mapCanvas != null && !mapCanvas.TurnSelection(turnDegrees))
                Debug.LogWarning("-turnSelection needs a project and at least two selected objects.");

            // -moveSelection <dx> <dy>: moves the selected objects by metres east and north and saves, as dragging does.
            if (double.TryParse(Argument("-moveSelection", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var moveEast) &&
                double.TryParse(Argument("-moveSelection", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var moveNorth) && selection.Count > 0)
            {
                var edits = new List<MapObjectEdit>();
                foreach (var item in selection)
                    edits.Add(new MapObjectEdit
                    {
                        Tile = item.Tile, Record = item.Record, LocalX = item.Record.LocalX + moveEast, LocalY = item.Record.LocalY + moveNorth,
                        Height = item.Record.Height, HeadingDegrees = item.Record.HeadingDegrees, PitchDegrees = item.Record.PitchDegrees,
                        BankDegrees = item.Record.BankDegrees
                    });
                CommitEdits(edits);
                Debug.Log("Move selection: " + statusLabel?.text);
            }

            RunClipboardArguments(args);
            RunMapSettingsArguments(args);
            RunContentFolderArguments(args);
            RunParkListArguments(args);
            RunTimetableArguments(Argument, args);
            RunBusStopArguments(Argument, args);
            RunVehicleArguments(Argument, args);
            RunLineArguments(Argument, args);
            RunEditTripArguments(Argument, args);
            RunTrafficCurveArguments(args);
            RunSignalArguments(args);
            RunProjectTabArguments(args);
            RunEntryPointArguments(args);
            RunTerrainXArguments(args);

            if (Array.IndexOf(args, "-deleteSelection") >= 0 && selection.Count > 0)
            {
                DeleteSelectedObjects();
                Debug.Log("Delete selection: " + statusLabel?.text);
            }

            // -revertTile [x y] discards the edits of that tile, or of the selected one, as one step of the history, which -elementUndo
            // and -elementRedo below take back and do again; the status goes to the log as "Revert:". One number alone, or values that are
            // not whole numbers, warn and revert nothing.
            if (Array.IndexOf(args, "-revertTile") >= 0)
            {
                var revertKnown = true;
                // No value means the selected tile: the end of the arguments, or the next one (tile numbers may be negative).
                var revertFirst = Argument("-revertTile", 1);
                var revertNamesTile = revertFirst != null &&
                                      !(revertFirst.StartsWith("-", StringComparison.Ordinal) && !int.TryParse(revertFirst, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));
                var revertX = 0;
                var revertY = 0;
                var revertParsed = revertNamesTile && int.TryParse(revertFirst, NumberStyles.Integer, CultureInfo.InvariantCulture, out revertX) &&
                                   int.TryParse(Argument("-revertTile", 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out revertY);
                if (revertNamesTile && !revertParsed)
                {
                    revertKnown = false;
                    Debug.LogWarning("-revertTile needs two whole numbers x y, or none for the selected tile; nothing is reverted.");
                }
                else if (revertParsed)
                {
                    var revertTile = currentSummary.Tiles.Find(tile => tile.X == revertX && tile.Y == revertY);
                    revertKnown = revertTile != null;
                    if (revertKnown)
                        SelectTile(revertTile, false);
                    else
                        Debug.LogWarning("-revertTile: the map has no tile " + revertX + ", " + revertY + "; nothing is reverted.");
                }
                if (revertKnown)
                {
                    // Wait for the ground, so the terrain fitted again is drawn into a loaded map as after a click.
                    var groundStarted = Time.realtimeSinceStartup;
                    while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - groundStarted < 300f)
                        yield return null;
                    RevertSelectedTile();
                    Debug.Log("Revert: " + statusLabel?.text);
                }
            }

            // -retype <file.sco> and -relabel <line|line|...> change the selected object, or with -elementId <ID> the attached
            // object with that ID in its tile (ADR 060); -pickType opens the library for it, -elementUndo and -elementRedo step through the
            // history; the status goes to the log as "Retype:", "Relabel:", "Pick type:", "Undo:" and "Redo:".
            // -varParent <ID|none> sets or takes away the variable parent of the selected object (or with -elementId of an attached one; ADR 082).
            if (Array.IndexOf(args, "-varParent") >= 0)
                RunVarParentArguments(args);
            if (Array.IndexOf(args, "-retype") >= 0 || Array.IndexOf(args, "-relabel") >= 0 || Array.IndexOf(args, "-pickType") >= 0 ||
                Array.IndexOf(args, "-elementUndo") >= 0 || Array.IndexOf(args, "-elementRedo") >= 0)
                RunElementArguments(args);

            // -markings show|<from-to:style,...> opens the markings tool on the selected junction and draws the lines (ADR 044).
            var markingArgument = Argument("-markings", 1);
            if (markingArgument != null)
                RunMarkingArgument(markingArgument);

            // -priorities [n ...] opens the priority tool on the selected junction and clicks the signs given (ADR 047).
            var priorityAt = Array.IndexOf(args, "-priorities");
            if (priorityAt >= 0)
            {
                var signs = new List<string>();
                for (var index = priorityAt + 1; index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal); index++)
                    signs.Add(args[index]);
                RunPriorityArgument(signs);
            }

            // -lanes [from>to ...|every] opens the lanes tool on the selected junction and adds or takes away the ways (ADR 048).
            var lanesAt = Array.IndexOf(args, "-lanes");
            if (lanesAt >= 0)
            {
                var ways = new List<string>();
                for (var index = lanesAt + 1; index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal); index++)
                    ways.Add(args[index]);
                RunLaneArgument(ways);
            }

            // -turnLane n [taper storage|off] gives arm n of the selected junction a left-turn lane or takes it away (ADR 051).
            var turnLaneAt = Array.IndexOf(args, "-turnLane");
            if (turnLaneAt >= 0)
            {
                var words = new List<string>();
                for (var index = turnLaneAt + 1; index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal); index++)
                    words.Add(args[index]);
                RunTurnLaneArgument(words);
            }

            // -crossing n [distance width|off] gives arm n of the selected junction a pedestrian crossing or takes it away.
            var crossingAt = Array.IndexOf(args, "-crossing");
            if (crossingAt >= 0)
            {
                var words = new List<string>();
                for (var index = crossingAt + 1; index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal); index++)
                    words.Add(args[index]);
                RunCrossingArgument(words);
            }

            // -exportMap exports the map as the export dialog suggests (ADR 052); the status goes to the log as "Export:".
            if (Array.IndexOf(args, "-exportMap") >= 0)
            {
                OpenExportDialog();
                if (session != null && session.CanEdit)
                    ExportMap();
                Debug.Log("Export: " + statusLabel?.text);
            }

            if (Array.IndexOf(args, "-plain") >= 0 && groundRenderer != null && mapCanvas != null && !mapCanvas.ThreeDimensional)
                SetTextures(false);

            if (Array.IndexOf(args, "-check") >= 0)
            {
                ValidateLoadedMap();
                // -checkTopic <title>: scrolls the report to that topic once it is shown.
                var topic = Argument("-checkTopic", 1);
                if (topic != null)
                {
                    var checkStarted = Time.realtimeSinceStartup;
                    while (checkTask != null && Time.realtimeSinceStartup - checkStarted < 300f)
                        yield return null;
                    yield return null;
                    foreach (var box in checkSections.Children())
                        if (box.userData is string topicKey && (string.Equals(topicKey, topic, StringComparison.OrdinalIgnoreCase) || box.Q<Label>(className: "check-section-title")?.text == topic))
                            checkSections.schedule.Execute(() => checkSections.ScrollTo(box)).StartingIn(100);
                }
            }

            if (DateTime.TryParseExact(Argument("-chrono", 1) ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var chronoArgument))
            {
                ShowChronoDate(chronoArgument);
                var chronoStarted = Time.realtimeSinceStartup;
                while (chronoTask != null && Time.realtimeSinceStartup - chronoStarted < 120f)
                    yield return null;
            }

            if (Array.IndexOf(args, "-paths") >= 0)
            {
                ShowPaths(true);
                var pathsStarted = Time.realtimeSinceStartup;
                while (pathsTask != null && Time.realtimeSinceStartup - pathsStarted < 120f)
                    yield return null;
                // "-paths open" goes to the first open end, a number to that one.
                var openEnd = Argument("-paths", 1);
                if (openEnd == "open" || int.TryParse(openEnd, out _))
                    for (var step = int.TryParse(openEnd, out var count) ? count : 1; step > 0; step--)
                        NextOpenEnd();
                Debug.Log("Paths: " + statusLabel?.text);
                // -pathRules all|n[,n] km/h|- density|- cars buses trucks (0 or 1): the traffic rules of the selected element's
                // road lanes, as the inspector's Apply writes them; "Path rules:" in the log.
                var rulesAt = Array.IndexOf(args, "-pathRules");
                if (rulesAt >= 0 && rulesAt + 6 < args.Length)
                {
                    // A spline to change the rules of is chosen here, as -selectSpline is read only further on.
                    if (int.TryParse(Argument("-selectSpline", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rulesSpline))
                        foreach (var tile in currentSummary.Tiles)
                            if (tile.Splines.Find(spline => spline.Id == rulesSpline) is OmsiSplineRecord found)
                                SelectSpline(tile, found);
                    // -wholeRoad chooses every piece of the selected spline's road, as Whole road in the inspector does.
                    if (Array.IndexOf(args, "-wholeRoad") >= 0 && selectedSpline != null)
                    {
                        var wholeRoad = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
                        foreach (var segment in new OmsiSplineAttachmentLayout(currentSummary).Chain(selectedSplineTile, selectedSpline))
                            if (SameAsset(segment.Spline.AssetPath, selectedSpline.AssetPath))
                                wholeRoad.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(segment.Tile, segment.Spline));
                        SetMoreSplines(wholeRoad);
                    }
                    RunPathRulesArgument(args, rulesAt + 1);
                    RenderInspector();
                    Debug.Log("Path rules: " + statusLabel?.text);
                }
            }

            // -roadBuilder ["lanes"] [-roadBuilderSelect n] [-roadBuilderSave <name>] opens the road builder (ADR 049); a saved
            // road type is then chosen for -road. -roadBuilderEdit <Splines\Mapforge\...\x.sli> opens a road type made with it,
            // -roadBuilderReplace saves it over itself and -roadBuilderDelete deletes it; -roadBuilderFrom <x.sli> takes a
            // profile apart into lanes.
            var builderAt = Array.IndexOf(args, "-roadBuilder");
            if (builderAt >= 0)
            {
                SetTool(EditorTool.Road);
                var scanStarted = Time.realtimeSinceStartup;
                while (installationScan != null && !installationScan.IsCompleted && Time.realtimeSinceStartup - scanStarted < 120f)
                    yield return null;
                yield return null;
                var builderLanes = builderAt + 1 < args.Length && !args[builderAt + 1].StartsWith("-", StringComparison.Ordinal) ? args[builderAt + 1] : null;
                RunRoadBuilderArgument(builderLanes, Argument("-roadBuilderSelect", 1), Argument("-roadBuilderLine", 1), Argument("-roadBuilderSave", 1),
                    Argument("-roadBuilderEdit", 1), Argument("-roadBuilderFrom", 1), Array.IndexOf(args, "-roadBuilderReplace") >= 0,
                    Array.IndexOf(args, "-roadBuilderDelete") >= 0);
                var deleteStarted = Time.realtimeSinceStartup;
                while (roadTypeUsers != null && Time.realtimeSinceStartup - deleteStarted < 120f)
                    yield return null;
                if (Array.IndexOf(args, "-roadBuilderDelete") >= 0)
                    Debug.Log("Road builder: " + statusLabel?.text);
                // -undo [n] undoes the last n steps, as Ctrl+Z does; the status goes to the log as "Undo:".
                if (Array.IndexOf(args, "-undo") >= 0)
                    for (var undo = int.TryParse(Argument("-undo", 1), out var undoCount) ? undoCount : 1; undo > 0; undo--)
                    {
                        StepHistory(undoSteps, redoSteps, true);
                        Debug.Log("Undo: " + statusLabel?.text);
                    }
                // -roadBuilderSurfaces opens the surface picker of the lane picked and waits for its textures and previews.
                if (Array.IndexOf(args, "-roadBuilderSurfaces") >= 0 && roadBuilder != null)
                {
                    roadBuilder.ShowSurfaces();
                    var surfacesStarted = Time.realtimeSinceStartup;
                    while (!roadBuilder.SurfacesReady && Time.realtimeSinceStartup - surfacesStarted < 60f)
                        yield return null;
                    for (var frame = 0; frame < 60; frame++)
                        yield return null;
                }
            }

            var roadAt = Array.IndexOf(args, "-road");
            if (roadAt >= 0 && int.TryParse(Argument("-road", 1), out var roadTileX) && int.TryParse(Argument("-road", 2), out var roadTileY))
            {
                var points = new List<OmsiMapPoint>();
                for (var index = roadAt + 3; index + 1 < args.Length; index += 2)
                {
                    if (!double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var roadLocalX) ||
                        !double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var roadLocalY))
                        break;
                    points.Add(OmsiMapGeometry.TileLocalToWorld(roadTileX, roadTileY, roadLocalX, roadLocalY, currentSummary.TileSizeMeters));
                }
                // Wait for the ground, so the roads are built into a loaded map as when clicking.
                var groundStarted = Time.realtimeSinceStartup;
                while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - groundStarted < 300f)
                    yield return null;
                SetTool(EditorTool.Road);
                if (roadPanel != null && Enum.TryParse<OmsiRoadHeightMode>(Argument("-roadMode", 1) == "follow" ? "FollowTerrain" : Argument("-roadMode", 1) ?? string.Empty, true,
                        out var roadMode))
                    roadPanel.SetHeightMode(roadMode);
                // -roadShape straight|curved|free and -roadSnap none|all or a list such as roads,angles,guides (ADR 043).
                // -roadCrossing: a click on a road puts a pedestrian crossing piece into it (ADR 086, step 5).
                if (roadPanel != null && Array.IndexOf(args, "-roadCrossing") >= 0)
                    roadPanel.CrossingMode = true;
                // -roadParking: two clicks on a road put a row of parking spaces along it (ADR 087).
                if (roadPanel != null && Array.IndexOf(args, "-roadParking") >= 0)
                    roadPanel.ParkingMode = true;
                if (roadPanel != null && Enum.TryParse<OmsiRoadShape>(Argument("-roadShape", 1) ?? string.Empty, true, out var roadShape))
                    roadPanel.SetShape(roadShape);
                var roadSnap = Argument("-roadSnap", 1);
                if (roadPanel != null && roadSnap != null)
                {
                    var snaps = new List<string>(roadSnap.ToLowerInvariant().Split(','));
                    var all = snaps.Contains("all");
                    roadPanel.SetSnapping(all || snaps.Contains("roads"), all || snaps.Contains("angles"), all || snaps.Contains("guides"));
                }
                // -roadParallel left|right|both [distance] and -roadOpposite.
                var roadParallel = Argument("-roadParallel", 1);
                if (roadPanel != null && roadParallel != null)
                {
                    roadPanel.SetParallel(roadParallel == "left" || roadParallel == "both", roadParallel == "right" || roadParallel == "both");
                    roadPanel.SetParallelOptions(
                        double.TryParse(Argument("-roadParallel", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var parallelDistance) ? parallelDistance : 15.0,
                        Array.IndexOf(args, "-roadOpposite") >= 0);
                }
                // The profile list fills once the installation is listed.
                var scanStarted = Time.realtimeSinceStartup;
                while (installationScan != null && !installationScan.IsCompleted && Time.realtimeSinceStartup - scanStarted < 120f)
                    yield return null;
                yield return null;
                roadPanel?.ChooseProfile(Argument("-roadProfile", 1));
                // -roadPathWidth metres [-roadPathBoth]: the width and direction of the path type to draw (ADR 089).
                if (roadPanel != null && double.TryParse(Argument("-roadPathWidth", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pathWidth))
                {
                    yield return null;
                    roadPanel.SetPathOptions(pathWidth, Array.IndexOf(args, "-roadPathBoth") >= 0);
                    ApplyPathOptions();
                    Debug.Log("Path type: " + statusLabel?.text);
                }
                var roadTab = Argument("-roadTab", 1);
                if (roadTab != null)
                    roadPanel?.ShowTab(roadTab);
                var build = Array.IndexOf(args, "-roadBuild") >= 0;
                for (var index = 0; index < points.Count; index++)
                {
                    roadPointer = points[index];
                    if (index < points.Count - 1 || build)
                    {
                        BuildRoadTo(points[index].X, points[index].Y);
                        // Later messages replace the status, so a check by command line reads each click's outcome in the log.
                        Debug.Log("Road click " + (index + 1) + ": " + statusLabel?.text);
                    }
                    else
                    {
                        UpdateRoadPreview();
                    }
                }
            }

            if (Array.IndexOf(args, "-refitTerrain") >= 0)
            {
                var groundStarted = Time.realtimeSinceStartup;
                while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - groundStarted < 300f)
                    yield return null;
                RefitRoadTerrain();
            }

            if (int.TryParse(Argument("-addTile", 1), out var addX) && int.TryParse(Argument("-addTile", 2), out var addY))
                AddTile(addX, addY);
            if (int.TryParse(Argument("-deleteTile", 1), out var deleteX) && int.TryParse(Argument("-deleteTile", 2), out var deleteY))
            {
                var doomed = currentSummary.Tiles.Find(tile => tile.X == deleteX && tile.Y == deleteY);
                if (doomed != null)
                    DeleteTile(doomed);
            }
            if (int.TryParse(Argument("-hoverTileAdder", 1), out var hoverX) && int.TryParse(Argument("-hoverTileAdder", 2), out var hoverY))
                mapCanvas?.HoverTileAdder(hoverX, hoverY);

            if (int.TryParse(Argument("-selectSpline", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var splineId))
            {
                var splineTile = currentSummary.Tiles.Find(tile => tile.Splines.Exists(spline => spline.Id == splineId));
                // Chosen already for -pathRules, it keeps the splines chosen with it.
                if (splineTile != null && selectedSpline?.Id != splineId)
                    SelectSpline(splineTile, splineTile.Splines.Find(spline => spline.Id == splineId));
                if (selectedSpline != null &&
                    double.TryParse(Argument("-splineHeights", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var startHeight) &&
                    double.TryParse(Argument("-splineHeights", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var endHeight))
                    SetSplineHeights(startHeight, endHeight);
                if (selectedSpline != null)
                {
                    RunSplineCrossArguments(args);
                    RunSplineLookArguments(args);
                }
            }

            // -splineType, -splinePick, -splineDelete, -splineUndo and -splineRedo work on the spline chosen above in a variant being edited (ADR 065).
            if (EditingVariant && (Array.IndexOf(args, "-splineType") >= 0 || Array.IndexOf(args, "-splinePick") >= 0 || Array.IndexOf(args, "-splineDelete") >= 0 ||
                                   Array.IndexOf(args, "-splineUndo") >= 0 || Array.IndexOf(args, "-splineRedo") >= 0))
                RunSplineVariantArguments(args);
            // -linesOff A|B|C makes the variant being edited switch off these lines, "-" none (ADR 067).
            if (EditingVariant && Array.IndexOf(args, "-linesOff") >= 0)
                RunLinesArgument(args);
            // -chronoDates yyyy-MM-dd [yyyy-MM-dd|-] gives the variant being edited a first and a last day (ADR 075).
            if (EditingVariant && Array.IndexOf(args, "-chronoDates") >= 0)
                RunChronoDatesArgument(args);

            if (int.TryParse(Argument("-moveSplineEnd", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var moveId) &&
                int.TryParse(Argument("-moveSplineEnd", 3), out var moveTileX) && int.TryParse(Argument("-moveSplineEnd", 4), out var moveTileY) &&
                double.TryParse(Argument("-moveSplineEnd", 5), NumberStyles.Float, CultureInfo.InvariantCulture, out var moveX) &&
                double.TryParse(Argument("-moveSplineEnd", 6), NumberStyles.Float, CultureInfo.InvariantCulture, out var moveY))
            {
                var moveTile = currentSummary.Tiles.Find(tile => tile.Splines.Exists(spline => spline.Id == moveId));
                if (moveTile != null)
                {
                    SelectSpline(moveTile, moveTile.Splines.Find(spline => spline.Id == moveId));
                    bendingSpline = Argument("-moveSplineEnd", 2) == "curve";
                    draggedSplineEnd = new OmsiSplineEnd(moveTile, selectedSpline, Argument("-moveSplineEnd", 2) == "end");
                    var target = OmsiMapGeometry.TileLocalToWorld(moveTileX, moveTileY, moveX, moveY, currentSummary.TileSizeMeters);
                    PreviewSplineEnd(target.X, target.Y, Array.IndexOf(args, "-moveCommit") >= 0, Array.IndexOf(args, "-moveAlt") >= 0);
                }
            }

            // -rows <object> <tile x> <tile y> <x> <y> [-rowsLay]: points the row tool at a place with an object chosen, and
            // with -rowsLay clicks there; -rowsBoth lays both sides, -rowsEvery <m> sets the distance between objects, -rowsHeading <°> turns them.
            var rowsObject = Argument("-rows", 1);
            if (rowsObject != null && int.TryParse(Argument("-rows", 2), out var rowsTileX) && int.TryParse(Argument("-rows", 3), out var rowsTileY) &&
                double.TryParse(Argument("-rows", 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var rowsX) &&
                double.TryParse(Argument("-rows", 5), NumberStyles.Float, CultureInfo.InvariantCulture, out var rowsY))
            {
                var groundStarted = Time.realtimeSinceStartup;
                while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - groundStarted < 300f)
                    yield return null;
                SetTool(EditorTool.Rows);
                rowOptions?.Apply(Array.IndexOf(args, "-rowsBoth") >= 0, Argument("-rowsEvery", 1), Argument("-rowsHeading", 1));
                if (Array.IndexOf(args, "-rowsTilt") >= 0)
                    rowOptions?.SetTilt(true);
                ChooseRowObject(rowsObject);
                var rowsAt = OmsiMapGeometry.TileLocalToWorld(rowsTileX, rowsTileY, rowsX, rowsY, currentSummary.TileSizeMeters);
                roadPointer = rowsAt;
                UpdateRowPreview();
                if (Array.IndexOf(args, "-rowsLay") >= 0)
                    ClickRow(rowsAt.X, rowsAt.Y);
                Debug.Log("Rows: " + statusLabel?.text);
                // -rowsChange <every> [<object>]: selects the row just laid and changes its distance between objects, and its object.
                var rowsChange = Argument("-rowsChange", 1);
                if (rowsChange != null && rowLaidAt.HasValue)
                {
                    KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>? laid = null;
                    foreach (var tile in currentSummary.Tiles)
                        foreach (var row in tile.SplineAttachments)
                            if (!row.IsRepeater && (!laid.HasValue || row.Id > laid.Value.Value.Id))
                                laid = new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(tile, row);
                    rowLaidAt = null;
                    if (laid.HasValue)
                    {
                        SelectRow(laid.Value);
                        rowOptions.Apply(false, rowsChange, null);
                        ChangeSelectedRow(Argument("-rowsChange", 2) is string swap && !swap.StartsWith("-", StringComparison.Ordinal) ? swap : null);
                    }
                    Debug.Log("Rows changed: " + statusLabel?.text);
                }
            }

            // -heightmapExport <file.png>: writes the height map; -heightmapImport <file.png> [-heightmapAdd] [-heightmapSmooth n]
            // [-heightmapLowest m] [-heightmapHighest m]: lays a picture over the map as the import's settings would.
            var heightmapExport = Argument("-heightmapExport", 1);
            var heightmapImport = Argument("-heightmapImport", 1);
            if (heightmapExport != null || heightmapImport != null)
            {
                var heightWait = Time.realtimeSinceStartup;
                while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - heightWait < 300f)
                    yield return null;
                SetTool(EditorTool.Terrain);
                if (heightmapExport != null)
                {
                    ExportHeightMap(heightmapExport);
                    Debug.Log("Height map: " + statusLabel?.text);
                }
                if (heightmapImport != null)
                {
                    ChooseHeightMap(heightmapImport);
                    terrainOptions?.ApplyImport(Array.IndexOf(args, "-heightmapAdd") >= 0,
                        int.TryParse(Argument("-heightmapSmooth", 1), out var smoothPasses) ? smoothPasses : (int?)null);
                    if (terrainOptions != null && pendingHeightMap != null &&
                        (Argument("-heightmapLowest", 1) != null || Argument("-heightmapHighest", 1) != null))
                    {
                        var current = terrainOptions.ImportRange ?? new OmsiHeightRange { Lowest = 0, Highest = 100 };
                        terrainOptions.ShowImport(Path.GetFileName(heightmapImport), string.Empty, new OmsiHeightRange
                        {
                            Lowest = double.TryParse(Argument("-heightmapLowest", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var low) ? low : current.Lowest,
                            Highest = double.TryParse(Argument("-heightmapHighest", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var high) ? high : current.Highest
                        }, pendingHeightMap.Coarse);
                        terrainOptions.ApplyImport(Array.IndexOf(args, "-heightmapAdd") >= 0,
                            int.TryParse(Argument("-heightmapSmooth", 1), out var smoothAgain) ? smoothAgain : (int?)null);
                    }
                    LayHeightMap();
                    Debug.Log("Height map: " + statusLabel?.text);
                }
            }

            // -levelGround: levels the ground under the objects chosen with -select, once the ground is drawn.
            if (Array.IndexOf(args, "-levelGround") >= 0)
            {
                var levelWait = Time.realtimeSinceStartup;
                while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - levelWait < 300f)
                    yield return null;
                LevelSelectedGround();
                Debug.Log("Level ground: " + statusLabel?.text);
            }

            // -terrain <raise|lower|smooth|flatten|roughen> <tile x> <tile y> <x> <y> [-terrainRadius m] [-terrainStrength s] [-terrainSeconds s]:
            // holds the brush there for some seconds; -terrain ramp <tile x> <tile y> <x1> <y1> <x2> <y2> lays a ramp.
            var terrainMode = Argument("-terrain", 1);
            if (terrainMode != null && int.TryParse(Argument("-terrain", 2), out var terrainTileX) && int.TryParse(Argument("-terrain", 3), out var terrainTileY) &&
                double.TryParse(Argument("-terrain", 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var terrainX) &&
                double.TryParse(Argument("-terrain", 5), NumberStyles.Float, CultureInfo.InvariantCulture, out var terrainY))
            {
                var groundWait = Time.realtimeSinceStartup;
                while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - groundWait < 300f)
                    yield return null;
                SetTool(EditorTool.Terrain);
                terrainOptions?.Apply(terrainMode, Argument("-terrainRadius", 1), Argument("-terrainStrength", 1));
                ApplyPaintArguments(Argument);
                var terrainTileSize = currentSummary.TileSizeMeters;
                var terrainAt = OmsiMapGeometry.TileLocalToWorld(terrainTileX, terrainTileY, terrainX, terrainY, terrainTileSize);
                roadPointer = terrainAt;
                if (terrainOptions != null && !terrainOptions.Mode.HasValue &&
                    double.TryParse(Argument("-terrain", 6), NumberStyles.Float, CultureInfo.InvariantCulture, out var rampX) &&
                    double.TryParse(Argument("-terrain", 7), NumberStyles.Float, CultureInfo.InvariantCulture, out var rampY))
                {
                    ClickRamp(terrainAt.X, terrainAt.Y);
                    var rampEnd = OmsiMapGeometry.TileLocalToWorld(terrainTileX, terrainTileY, rampX, rampY, terrainTileSize);
                    roadPointer = rampEnd;
                    ClickRamp(rampEnd.X, rampEnd.Y);
                }
                else
                {
                    PressTerrain(terrainAt.X, terrainAt.Y);
                    var held = double.TryParse(Argument("-terrainSeconds", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var heldSeconds) ? heldSeconds : 1.0;
                    var pressed = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - pressed < held)
                        yield return null;
                    ReleaseTerrain();
                }
                ShowTerrainBrush();
                Debug.Log("Terrain: " + statusLabel?.text);
            }

            // -turnArm <object ID> <arm from 1> <heading in the object>: drags that arm handle of a junction made here.
            if (int.TryParse(Argument("-turnArm", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var armObjectId) &&
                int.TryParse(Argument("-turnArm", 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var armNumber) &&
                double.TryParse(Argument("-turnArm", 3), NumberStyles.Float, CultureInfo.InvariantCulture, out var armHeading))
            {
                var armTile = currentSummary.Tiles.Find(tile => tile.Objects.Exists(item => item.Id == armObjectId));
                var armObject = armTile?.Objects.Find(item => item.Id == armObjectId);
                if (armObject != null)
                {
                    // Wait for the ground, so the roads are rebuilt into a loaded map as when dragging.
                    var groundStarted = Time.realtimeSinceStartup;
                    while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - groundStarted < 300f)
                        yield return null;
                    RefreshAfterChange(new[] { new MapSelectionItem { Tile = armTile, Record = armObject } });
                    StartArmTurn(armNumber - 1);
                    var centre = OmsiMapGeometry.TileLocalToWorld(armTile.X, armTile.Y, armObject.LocalX, armObject.LocalY, currentSummary.TileSizeMeters);
                    var radians = (armObject.HeadingDegrees + armHeading) * Math.PI / 180.0;
                    PreviewArmTurn(centre.X + Math.Sin(radians) * 20.0, centre.Y + Math.Cos(radians) * 20.0, Array.IndexOf(args, "-turnCommit") >= 0, true);
                    Debug.Log("Turn arm: " + statusLabel?.text);
                }
            }

            if (Array.IndexOf(args, "-routes") >= 0)
            {
                var routesStarted = Time.realtimeSinceStartup;
                while (routesTask != null && Time.realtimeSinceStartup - routesStarted < 120f)
                    yield return null;
                ShowRoutes(true);
                var lineName = Argument("-routes", 1);
                if (lineName != null && !lineName.StartsWith("-", StringComparison.Ordinal))
                    HighlightLine(lineName);
            }

            if (Array.IndexOf(args, "-objects") >= 0)
            {
                ShowObjectsPanel(true);
                objectsPanel?.ShowState(Argument("-objectsTab", 1), Argument("-objectsAt", 1), Argument("-objectsSearch", 1));
            }

            // -fold closes the popup of the bar and the inspector, which open only on demand anyway (ADR 098).
            var fold = Argument("-fold", 1);
            if (fold == "project" || fold == "both")
                ShowBarPopup(false);
            if (fold == "inspector" || fold == "both")
                ShowInspector(false);

            var placeAsset = Argument("-place", 1);
            if (placeAsset != null)
            {
                // Objects drawn first, so the preview can snap onto their attachment points.
                var objectsStarted = Time.realtimeSinceStartup;
                while (objectRenderer != null && objectRenderer.IsLoading && Time.realtimeSinceStartup - objectsStarted < 300f)
                    yield return null;
                for (var frame = 0; frame < 3; frame++)
                    yield return null;
                StartPlacement(placeAsset);
                mapCanvas?.PointPlacementAtCenter();
                if (Array.IndexOf(args, "-placeCommit") >= 0)
                    mapCanvas?.ClickPlacement();
                Debug.Log("Place: " + statusLabel?.text);
            }
            var testProfile = Argument("-testJunction", 1);
            if (testProfile != null)
            {
                var armsArgument = Argument("-testJunction", 2);
                CreateTestJunction(testProfile, armsArgument == "3" ? 3 : armsArgument == "4" ? 4 : 0);
                mapCanvas?.PointPlacementAtCenter();
                if (Array.IndexOf(args, "-placeCommit") >= 0)
                    mapCanvas?.ClickPlacement();
            }

            yield return RunSatelliteArguments(args);

            // -benchmark <report.txt> measures loading, redraws, frames, picking and refreshes (PerformanceBaseline.md).
            if (Argument("-benchmark", 1) is string benchmarkReport)
                yield return RunBenchmark(benchmarkReport);

            var screenshot = Argument("-screenshot", 1);
            if (screenshot != null)
            {
                var started = Time.realtimeSinceStartup;
                // Paths read again after an edit come in after the 300 ms the rebuild waits.
                while ((groundRenderer.IsLoading || objectRenderer.IsLoading || objectRenderer.PreviewsPending && Array.IndexOf(args, "-noPreviewWait") < 0 || outlinesBuilding || checkTask != null ||
                        (installationScan != null && !installationScan.IsCompleted) || pathsShown && (routesRebuild != null || pathsTask != null) || updateCancellation != null) &&
                       Time.realtimeSinceStartup - started < 300f)
                    yield return null;
                for (var frame = 0; frame < 10; frame++)
                    yield return null;
                RunPickArguments(args);
                // -tooltipAt x y shows the tooltip there as hovering would (ADR 098, stage 2).
                RunTooltipArguments(args);
                // -inspector opens the inspector, -contextMenu id the context menu of an object (ADR 098).
                RunContextMenuArguments(args);
                if (Array.IndexOf(args, "-contextMenu") >= 0)
                    for (var frame = 0; frame < 3; frame++)
                        yield return null;
                // A chosen entry opens the inspector and scrolls to its group a moment later.
                if (Array.IndexOf(args, "-contextChoose") >= 0)
                    yield return new WaitForSecondsRealtime(0.6f);
                // -inspectorFind <text> opens the tile tab and types the text into its search.
                var inspectorFind = Argument("-inspectorFind", 1);
                if (inspectorFind != null && selectedTile != null)
                {
                    ShowInspector(true);
                    inspectorTab = InspectorTab.Tile;
                    tileFilter = inspectorFind;
                    RenderInspector();
                    for (var frame = 0; frame < 5; frame++)
                        yield return null;
                }
                // -inspectorTo <heading> scrolls the inspector to the section of that heading, such as "TRAFFIC RULES".
                var inspectorTo = Argument("-inspectorTo", 1);
                if (inspectorTo != null)
                {
                    if (!inspectorShown)
                    {
                        ShowInspector(true);
                        for (var frame = 0; frame < 3; frame++)
                            yield return null;
                    }
                    // A heading is a section heading or the title of a group; the tile tab holds the lists, so look there too.
                    Func<Label> findSection = () => rootElement?.Q<ScrollView>(className: "inspector-records")?.Query<Label>()
                        .Where(label => (label.ClassListContains("inspector-section-heading") || label.ClassListContains("inspector-group-title")) &&
                                        label.text.StartsWith(inspectorTo, StringComparison.OrdinalIgnoreCase)).First();
                    var section = findSection();
                    if (section == null && selectedTile != null && inspectorTab == InspectorTab.Selection)
                    {
                        ShowInspectorTab(InspectorTab.Tile);
                        for (var frame = 0; frame < 3; frame++)
                            yield return null;
                        section = findSection();
                    }
                    var records = rootElement?.Q<ScrollView>(className: "inspector-records");
                    if (section != null)
                    {
                        // The group the title belongs to opens first, then the layout settles.
                        (section.parent?.userData as Action<bool>)?.Invoke(true);
                        for (var frame = 0; frame < 3; frame++)
                            yield return null;
                        records.scrollOffset = new Vector2(0f, Mathf.Max(0f, section.worldBound.y - records.contentContainer.worldBound.y));
                    }
                    else
                        Debug.LogWarning("Inspector: no section \"" + inspectorTo + "\".");
                    for (var frame = 0; frame < 3; frame++)
                        yield return null;
                    // -inspectorRedraw draws it again, as a click in it does; it should stay where it was scrolled to.
                    if (Array.IndexOf(args, "-inspectorRedraw") >= 0)
                    {
                        RenderInspector();
                        for (var frame = 0; frame < 5; frame++)
                            yield return null;
                    }
                }
                // At the end of the frame the screen holds the cameras and the UI on top.
                yield return new WaitForEndOfFrame();
                SaveScreenshot(screenshot);
            }
            if (quit)
                Application.Quit();
        }

        /// <summary>Saves the screen as it is at the end of the frame, cameras and UI together, as a PNG file.</summary>
        private void SaveScreenshot(string path)
        {
            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), shot.EncodeToPNG());
            Destroy(shot);
            Debug.Log("Screenshot saved: " + Path.GetFullPath(path));
        }

        private void OpenProject()
        {
            var projectPath = WindowsFileDialog.OpenFile(Loc.T("filedialog.open_project"), WindowsFileDialog.ProjectFilter);
            if (!string.IsNullOrEmpty(projectPath))
                OpenProjectFile(projectPath);
        }

        /// <param name="remember">Adds the project to the recent files; command-line checks leave them alone.</param>
        private void OpenProjectFile(string projectPath, bool remember = true)
        {
            try
            {
                var fullProjectPath = Path.GetFullPath(projectPath);
                var project = JsonUtility.FromJson<MapforgeProject>(File.ReadAllText(fullProjectPath));
                if (project == null)
                    throw new InvalidDataException("The selected file does not contain a MapWorks project.");
                if (project.formatVersion != MapforgeProject.CurrentFormatVersion)
                    throw new InvalidDataException("This MapWorks project uses an unsupported format version: " + project.formatVersion + ".");
                if (string.IsNullOrWhiteSpace(project.globalConfigPath))
                    throw new InvalidDataException("The project does not contain an OMSI global.cfg path.");

                var projectName = string.IsNullOrWhiteSpace(project.projectName)
                    ? Path.GetFileNameWithoutExtension(fullProjectPath)
                    : project.projectName;
                OpenMapInBackground(project.globalConfigPath, projectName, fullProjectPath, "Could not open project: ", remember ? (Action)RememberCurrent : null);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("map.project_failed", ("reason", exception.Message));
            }
        }

        /// <summary>Opens a project or map from the welcome screen's recent files.</summary>
        private void OpenRecent(RecentFile entry)
        {
            if (entry.IsProject)
            {
                OpenProjectFile(entry.Path);
                return;
            }
            try
            {
                OpenMapInBackground(entry.Path, string.Empty, null, "Could not open map: ", RememberCurrent);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("map.open_failed", ("reason", exception.Message));
            }
        }

        /// <summary>Puts the open project, or the open map without a project, at the top of the recent files.</summary>
        private void RememberCurrent()
        {
            if (session == null || currentSummary == null)
                return;
            var mapName = string.IsNullOrWhiteSpace(currentSummary.FriendlyName) ? currentSummary.Name : currentSummary.FriendlyName;
            if (!string.IsNullOrEmpty(session.ProjectFilePath))
                RecentFiles.Add(true, session.ProjectFilePath, currentProjectName + "  ·  " + mapName);
            else
                RecentFiles.Add(false, currentSummary.GlobalConfigPath, mapName);
        }

        private void SaveProjectAs()
        {
            if (currentSummary == null)
            {
                SetStatusT("map.open_first");
                return;
            }

            try
            {
                var suggestedName = string.IsNullOrWhiteSpace(currentProjectName)
                    ? (string.IsNullOrWhiteSpace(currentSummary.Name) ? "OMSI Map" : currentSummary.Name)
                    : currentProjectName;
                var projectPath = WindowsFileDialog.SaveFile(Loc.T("filedialog.save_project"), WindowsFileDialog.ProjectFilter,
                    WindowsFileDialog.ProjectExtension, SanitizeFileName(suggestedName) + "." + WindowsFileDialog.ProjectExtension);
                if (string.IsNullOrEmpty(projectPath))
                    return;

                var fullProjectPath = Path.GetFullPath(projectPath);
                var project = new MapforgeProject
                {
                    projectName = Path.GetFileNameWithoutExtension(fullProjectPath),
                    globalConfigPath = currentSummary.GlobalConfigPath
                };
                File.WriteAllText(fullProjectPath, JsonUtility.ToJson(project, true), new System.Text.UTF8Encoding(false));
                // From now on the map is read through the project's workspace, which also receives the edits.
                LoadMap(currentSummary.GlobalConfigPath, project.projectName, fullProjectPath, true);
                RememberCurrent();
                SetStatusT("map.project_saved", ("path", fullProjectPath), ("workspace", session.Workspace.WorkspacePath));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("map.save_failed", ("reason", exception.Message));
            }
        }

        /// <param name="projectFilePath">The project whose workspace is used; null opens the map read-only.</param>
        /// <param name="keepView">Keeps the view, selected tile and selected object, for example after adding or deleting a tile.</param>
        private void LoadMap(string globalConfigPath, string projectName, string projectFilePath, bool keepView)
        {
            // A terrain or paint stroke still held is finished before the map is read, so the reading sees what it wrote.
            if (session != null && currentSummary != null && activeTool == EditorTool.Terrain)
                ReleaseTerrain();
            ShowMap(ReadMap(globalConfigPath, projectFilePath), projectName, keepView);
        }

        /// <summary>Shows a map read by <see cref="ReadMap"/> in the views, with a new history.</summary>
        private void ShowMap(MapReading reading, string projectName, bool keepView)
        {
            var summary = reading.Summary;
            // A terrain or paint stroke still held belongs to the session that is leaving: it is finished there, before the new one takes over.
            if (session != null && currentSummary != null && activeTool == EditorTool.Terrain)
                ReleaseTerrain();
            UseSession(reading.Session);
            junctionTerrainState = null;
            junctionTerrainPlaces = null;
            baseSummary = summary;
            chronoDate = null;
            chronoGeneration++;
            chronoTask = null;
            editedVariant = null;
            newVariantFormOpen = false;
            variantToDelete = null;
            chronoVariants = reading.ChronoVariants;
            currentProjectName = projectName ?? string.Empty;
            undoSteps.Clear();
            redoSteps.Clear();
            StopPlacement();
            ShowObjectsPanel(false);
            routes = null;
            highlightedLine = null;
            routesToggleButton?.SetEnabled(false);
            PresentSummary(summary, keepView);
            LoadStoredImagery(reading.ImageryKey);
            StartRoutesBuild(summary);
            ResetPaths(summary);
            UpdateMapHeader();
            RenderProjectBrowser();
            RenderInspector();
            UpdateMapStateLabel();
            SetStatusT("map.loaded", ("name", currentSummary.Name), ("tiles", currentSummary.Tiles.Count), ("mode", session.CanEdit ? "project" : "readonly"));
            checkMapMenuItem?.SetEnabled(true);
        }

        /// <summary>
        /// Shows a summary of the session's map in the views: the base map or a chrono overlay. The selected tile and
        /// object are found again by position and ID when <paramref name="keepView"/> is set.
        /// </summary>
        private void PresentSummary(OmsiMapSummary summary, bool keepView)
        {
            // Picks waiting for a click and the context menu hold records of the summary shown, which is replaced now.
            LeaveTransientModes();
            // Roads, markings, right of way, signals and lanes hold records of the summary shown; another map or a chrono view ends
            // them and the spline selection (EditorToolRules.EndsOnEveryView).
            if (EditorToolRules.EndsOnEveryView(activeTool))
                EndToolForView();
            // Rows, terrain and line keep panels and previews of their own; a view where they cannot be used ends them too. A terrain
            // stroke still held has saved the heights it showed with every tick: ending the tool finishes it on the summary it was
            // begun on, which is still the current one here, so that the files are whole and the step can be undone.
            if (mapCanvas != null && activeTool != EditorTool.Drag && OnlySelectingWith(activeTool))
                EndToolForView();
            ClearSelectedSpline();
            var previousTile = selectedTile;
            var previousObjectId = selectedObject?.Id;
            currentSummary = summary;
            // The base map and each chrono view have roads and junctions of their own: the terrain was fitted to the junctions of the
            // summary shown before, so the next fit after a junction changed fits the whole map (RefitTerrainAtJunctions).
            junctionTerrainPlaces = null;
            selectedTile = null;
            selectedObject = null;
            if (keepView && previousTile != null)
                selectedTile = summary.Tiles.Find(tile => tile.X == previousTile.X && tile.Y == previousTile.Y);
            if (selectedTile != null && previousObjectId.HasValue)
                selectedObject = selectedTile.Objects.Find(item => item.Id == previousObjectId.Value);
            if (selectedTile == null)
                selectedTile = summary.Tiles.Count > 0 ? summary.Tiles[0] : null;
            selection.Clear();
            if (selectedObject != null)
                selection.Add(new MapSelectionItem { Tile = selectedTile, Record = selectedObject });

            if (mapCanvas != null)
            {
                mapCanvas.AllowEditing = CanEditObjects;
                mapCanvas.AllowSplineEditing = CanEdit;
                // A map without a project or a chrono view can only be looked at; selecting is the only tool then. A
                // variant being edited keeps the object tools.
                if (OnlySelectingWith(activeTool))
                    activeTool = EditorTool.Drag;
                mapCanvas.Tool = activeTool;
                mapCanvas.SetMap(summary, keepView);
                mapCanvas.SetSelection(selectedTile, selection);
                mapCanvas.TexturedView = groundRenderer != null && groundRenderer.GroundVisible;
                var installationRoot = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
                groundRenderer?.Load(summary, session.Source, Path.GetFileName(session.GlobalConfigPath), installationRoot,
                    mapCanvas.OriginX, mapCanvas.OriginY);
                groundRenderer?.RefreshWater(summary);
                objectRenderer?.Load(summary, session.Source, installationRoot, mapCanvas.OriginX, mapCanvas.OriginY);
            }
            StartOutlineBuild(summary);
            UpdateToolbar();
        }

        /// <summary>
        /// Whether the view shown leaves only selecting with this tool: a map without a project or a chrono view; a variant being edited
        /// keeps the object tools.
        /// </summary>
        private bool OnlySelectingWith(EditorTool tool)
        {
            // In a variant being edited Move, Rotate, Priorities and Road keep working; Drag only ever selects.
            return !CanEditObjects || !CanEdit && (tool == EditorTool.Drag || !EditorToolRules.NeedsObjectEditingOnly(tool));
        }

        private static List<OmsiChronoVariant> ReadChronoVariants(IOmsiFileSource source)
        {
            try
            {
                return OmsiChrono.ReadVariants(source);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
            {
                Debug.LogWarning("Chrono variants could not be read: " + exception.Message);
                return new List<OmsiChronoVariant>();
            }
        }

        /// <summary>Why the map cannot be changed now; <paramref name="actionKey"/> is a key <c>readonly.action.*</c> naming what was tried.</summary>
        private string ReadOnlyMessage(string actionKey)
        {
            var action = Loc.T(actionKey); // i18n:keys
            if (session != null && session.CanEdit && (chronoDate == null || EditingVariant) && ReadingMapInBackground)
                return Loc.T("readonly.reading", ("action", action));
            if (EditingVariant)
                return Loc.T("readonly.variant", ("title", editedVariant.Title), ("action", action));
            return chronoDate != null
                ? Loc.T("readonly.chrono", ("date", chronoDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("action", action))
                : Loc.T("readonly.map", ("action", action));
        }

        /// <summary>
        /// Shows the map as OMSI loads it on <paramref name="date"/>: the chrono variants active then are laid over a copy
        /// of the base map on a worker thread, and the timetable of the date drives the routes. Null returns to the base
        /// map. The chrono view is read-only; the base map and its edits are not touched.
        /// </summary>
        private void ShowChronoDate(DateTime? date)
        {
            ShowChrono(date, null);
        }

        /// <summary>
        /// Edits a chrono variant (ADR 042): the map is shown on the variant's first day with the variants up to it in name
        /// order that apply then, the ones it builds on; later ones stay away. Objects placed, moved, turned or deleted go
        /// into the variant until Base map ends editing.
        /// </summary>
        private void EditChronoVariant(OmsiChronoVariant variant)
        {
            if (session == null || !session.CanEdit)
            {
                SetStatusT("chrono.status.save_to_edit");
                return;
            }
            if (variant?.FirstDay == null)
            {
                SetStatusT("chrono.status.no_date", ("title", variant?.Title ?? string.Empty));
                return;
            }
            ShowChrono(variant.FirstDay, variant);
        }

        /// <summary>Makes a chrono variant after the map's others and starts editing it.</summary>
        private void CreateChronoVariant(string title, DateTime start, DateTime? end)
        {
            if (session == null || !session.CanEdit)
            {
                SetStatusT("chrono.status.save_to_make");
                return;
            }
            OmsiChronoVariant made;
            try
            {
                LeaveEditedVariant();
                made = session.CreateChronoVariant(title, start, end);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException)
            {
                SetStatusT("chrono.status.make_failed", ("reason", exception.Message));
                return;
            }
            newVariantFormOpen = false;
            chronoVariants = ReadChronoVariants(session.Source);
            var listed = chronoVariants.Find(candidate => string.Equals(candidate.Name, made.Name, StringComparison.OrdinalIgnoreCase));
            Debug.Log("Chrono variant made: " + made.Folder);
            EditChronoVariant(listed ?? made);
        }

        /// <summary>Deletes a variant made in this project after a second click; variants of the original map stay.</summary>
        private void DeleteChronoVariant(OmsiChronoVariant variant)
        {
            if (!ReferenceEquals(variantToDelete, variant))
            {
                variantToDelete = variant;
                RenderProjectBrowser();
                SetStatusT("chrono.status.confirm_delete", ("title", variant.Title));
                return;
            }
            variantToDelete = null;
            try
            {
                session.DeleteChronoVariant(variant);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                SetStatusT("chrono.status.delete_failed", ("reason", exception.Message));
                return;
            }
            chronoVariants = ReadChronoVariants(session.Source);
            if (chronoDate != null)
                ShowChronoDate(chronoDate);
            else
                RenderProjectBrowser();
            SetStatusT("chrono.status.deleted", ("title", variant.Title));
        }

        /// <summary>
        /// Ends editing a variant: edits go to the base map again. The history is cleared, as its steps belong to the
        /// variant's view; the variants are read again, as edits may have added change lists.
        /// </summary>
        private void LeaveEditedVariant()
        {
            if (editedVariant == null)
                return;
            // A road type being chosen for a spline of the variant ends with it.
            EndSplineTypePick(null);
            lineCandidates = null;
            editedVariant = null;
            session?.EditChronoVariant(null, null);
            // Edits go to the base map again; the next fit after a junction changed fits the whole map (RefitTerrainAtJunctions).
            junctionTerrainPlaces = null;
            undoSteps.Clear();
            redoSteps.Clear();
            if (session != null)
                chronoVariants = ReadChronoVariants(session.Source);
        }

        private static bool TryParseDay(string text, out DateTime day)
        {
            return DateTime.TryParseExact(text?.Trim() ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        /// <param name="edit">The variant to edit on <paramref name="date"/>, or null for a read-only view.</param>
        private void ShowChrono(DateTime? date, OmsiChronoVariant edit)
        {
            if (session == null || baseSummary == null)
                return;
            var generation = ++chronoGeneration;
            projectTab = ProjectTab.Chrono;
            StopPlacement();
            ShowObjectsPanel(false);
            var wasEditing = editedVariant != null;
            LeaveEditedVariant();
            if (date == null)
            {
                chronoTask = null;
                if (chronoDate == null)
                    return;
                chronoDate = null;
                PresentChrono(baseSummary, null);
                SetStatusT(session.CanEdit ? "chrono.status.base_editable" : "chrono.status.base", ("wasEditing", wasEditing ? "yes" : "no")) /* i18n:keys */;
                return;
            }

            // Leaving a variant read the variants again; the one to edit is found by its folder.
            if (edit != null)
                edit = chronoVariants.Find(variant => string.Equals(variant.Name, edit.Name, StringComparison.OrdinalIgnoreCase)) ?? edit;
            var editIndex = edit != null ? chronoVariants.IndexOf(edit) : -1;
            var active = edit == null
                ? OmsiChrono.ActiveOn(chronoVariants, date.Value)
                : chronoVariants.FindAll(variant => chronoVariants.IndexOf(variant) <= editIndex && variant.IsActiveOn(date.Value));
            var source = session.Source;
            var baseMap = baseSummary;
            var dateText = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            OmsiChronoOverlayReport report = null;
            var task = Task.Run(() => OmsiChrono.Overlay(baseMap, source, active, out report));
            chronoTask = task;
            UpdateToolbar();
            SetStatusT("chrono.status.laying", ("count", active.Count), ("date", dateText));

            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                if (generation != chronoGeneration)
                    return;
                chronoTask = null;
                UpdateToolbar();
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatusT("chrono.status.apply_failed", ("date", dateText), ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    return;
                }
                chronoDate = date.Value.Date;
                StopPlacement();
                ShowObjectsPanel(false);
                if (edit != null)
                {
                    editedVariant = edit;
                    session.EditChronoVariant(edit, report);
                    // Steps of the view before refer to other records; they cannot be undone here.
                    undoSteps.Clear();
                    redoSteps.Clear();
                    PresentChrono(task.Result, active);
                    SetStatusT("chrono.status.editing", ("title", edit.Title), ("date", dateText), ("earlier", active.Count - 1));
                    return;
                }
                PresentChrono(task.Result, active);
                SetStatusT("chrono.status.view", ("date", dateText), ("active", active.Count), ("objects", report.AddedObjects), ("splines", report.AddedSplines),
                          ("removed", report.DeletedElements), ("swapped", report.ChangedTypes), ("relabelled", report.ChangedLabels), ("unresolved", report.UnresolvedSelections));
            }).Every(100);
        }

        private void PresentChrono(OmsiMapSummary summary, List<OmsiChronoVariant> variants)
        {
            PresentSummary(summary, true);
            StartRoutesBuild(summary, variants);
            ResetPaths(summary);
            UpdateMapHeader();
            RenderProjectBrowser();
            RenderInspector();
            UpdateMapStateLabel();
        }

        /// <summary>The date field of the chrono section, or the status bar's complaint when it holds no date.</summary>
        private void ShowChronoDateFromField()
        {
            var text = chronoDateField?.value?.Trim() ?? string.Empty;
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                SetStatusT("chrono.status.date_format");
                return;
            }
            ShowChronoDate(date);
        }

        /// <summary>
        /// Computes the outlines of every object kind the map uses on a worker thread; meshes are read from the OMSI
        /// installation. The map stays usable meanwhile and shows outlines once they are ready.
        /// </summary>
        private void StartOutlineBuild(OmsiMapSummary summary)
        {
            if (mapCanvas == null)
                return;

            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            if (!string.Equals(root, outlineInstallationRoot, StringComparison.OrdinalIgnoreCase))
            {
                outlineCache.Clear();
                outlineInstallationRoot = root;
            }

            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in summary.Tiles)
                foreach (var item in tile.Objects)
                    if (kinds.Add(item.AssetPath) && !outlineCache.ContainsKey(item.AssetPath))
                        missing.Add(item.AssetPath);
            if (missing.Count == 0)
            {
                mapCanvas.SetOutlines(outlineCache);
                return;
            }

            var generation = ++outlineBuildGeneration;
            outlinesBuilding = true;
            // In folder order, so a hard disk reads the files with shorter seeks.
            var paths = new List<string>(missing);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            var task = Task.Run(() =>
            {
                var catalog = new OmsiObjectOutlineCatalog(root);
                var result = new Dictionary<string, OmsiObjectOutline>(StringComparer.OrdinalIgnoreCase);
                foreach (var assetPath in paths)
                    result[assetPath] = catalog.Get(assetPath);
                return result;
            });

            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                // A newer map replaced this one while the outlines were computed.
                if (generation != outlineBuildGeneration)
                    return;
                outlinesBuilding = false;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatusT("outlines.failed", ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    return;
                }

                foreach (var pair in task.Result)
                    outlineCache[pair.Key] = pair.Value;
                mapCanvas.SetOutlines(outlineCache);
                var withOutline = 0;
                foreach (var kind in kinds)
                    if (outlineCache.TryGetValue(kind, out var outline) && outline.Points.Length >= 2)
                        withOutline++;
                SetStatusT("outlines.ready", ("count", withOutline), ("total", kinds.Count));
            }).Every(150);
        }

        /// <summary>
        /// Reads the timetable and finds where its lines run, on a worker thread: stops at their objects, lanes over the
        /// splines and objects they drive. The previous routes stay on screen until the new ones are ready.
        /// </summary>
        /// <param name="variants">The chrono variants whose timetables apply, or null for the base timetable.</param>
        private void StartRoutesBuild(OmsiMapSummary summary, List<OmsiChronoVariant> variants = null)
        {
            if (mapCanvas == null || session == null)
                return;
            var source = session.Source;
            var installationRoot = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var generation = ++routesGeneration;
            var task = Task.Run(() =>
            {
                var timetable = variants == null ? OmsiTimetable.Read(source) : OmsiTimetable.ReadForVariants(source, variants);
                return timetable.BusStops.Count == 0 && timetable.Lines.Count == 0 ? null : OmsiTimetableRoutes.Build(timetable, summary, installationRoot);
            });
            routesTask = task;

            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                // A newer map or edit started another build meanwhile.
                if (generation != routesGeneration)
                    return;
                routesTask = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatusT("routes.failed", ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    return;
                }
                // The line list changes with the first routes and with chrono dates that switch lines off.
                var linesChanged = routes == null || task.Result == null || !SameLines(routes, task.Result);
                routes = task.Result;
                mapCanvas.SetRoutes(routes);
                mapCanvas.HighlightLine(highlightedLine);
                routesToggleButton?.SetEnabled(routes != null && !mapCanvas.ThreeDimensional);
                if (routes == null)
                    ShowRoutes(false);
                if (linesChanged)
                    RenderProjectBrowser();
            }).Every(150);
        }

        private static bool SameLines(OmsiTimetableRoutes left, OmsiTimetableRoutes right)
        {
            if (left.Lines.Count != right.Lines.Count)
                return false;
            for (var index = 0; index < left.Lines.Count; index++)
                if (left.Lines[index].Name != right.Lines[index].Name || left.Lines[index].StopIds.Count != right.Lines[index].StopIds.Count ||
                    left.Lines[index].ScheduledTripCount != right.Lines[index].ScheduledTripCount)
                    return false;
            return true;
        }

        // Splines chosen with Ctrl+click besides the selected one, such as the other pieces of a road whose traffic rules are set.
        private readonly List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> moreSplines = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();

        /// <summary>A spline clicked with Ctrl joins the selected splines, or leaves them; without a selected spline it is selected.</summary>
        private void ToggleSpline(OmsiTileSummary tile, OmsiSplineRecord spline)
        {
            if (tile == null || spline == null)
                return;
            if (selectedSpline == null)
            {
                SelectSpline(tile, spline);
                return;
            }
            if (ReferenceEquals(spline, selectedSpline))
                return;
            var index = moreSplines.FindIndex(pair => ReferenceEquals(pair.Value, spline));
            if (index >= 0)
                moreSplines.RemoveAt(index);
            else
                moreSplines.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(tile, spline));
            mapCanvas?.SetMoreSelectedSplines(moreSplines.ConvertAll(pair => pair.Value));
            RenderInspector();
            SetStatusT("spline.status.many", ("count", moreSplines.Count + 1));
        }

        /// <summary>Chooses splines besides the selected one, as Ctrl+click does, such as all pieces of its road.</summary>
        private void SetMoreSplines(IEnumerable<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> splines)
        {
            moreSplines.Clear();
            foreach (var pair in splines)
                if (!ReferenceEquals(pair.Value, selectedSpline) && !moreSplines.Exists(other => ReferenceEquals(other.Value, pair.Value)))
                    moreSplines.Add(pair);
            mapCanvas?.SetMoreSelectedSplines(moreSplines.ConvertAll(pair => pair.Value));
        }

        private void SelectSpline(OmsiTileSummary tile, OmsiSplineRecord spline)
        {
            if (tile == null || spline == null)
                return;
            moreSplines.Clear();
            selection.Clear();
            selectedSplineTile = tile;
            selectedSpline = spline;
            mapCanvas?.SetSelectedSpline(spline);
            ApplySelection(tile);
            SetStatusT("spline.status.selected", ("id", spline.Id), ("path", spline.AssetPath), ("mode", CanEdit ? "edit" : chronoDate != null ? "chrono" : "readonly"));
        }

        private void ClearSelectedSpline()
        {
            moreSplines.Clear();
            if (selectedSpline == null)
                return;
            selectedSpline = null;
            selectedSplineTile = null;
            mapCanvas?.SetSelectedSpline(null);
        }

        /// <summary>Deletes the selected spline as one undo step; joined splines get an open end, references keep it.</summary>
        private void DeleteSelectedSpline()
        {
            if (selectedSpline == null || selectedSplineTile == null)
                return;
            if (EditingVariant)
            {
                DeleteSelectedSplineInVariant();
                return;
            }
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.delete_splines"));
                return;
            }
            OmsiSplineChangeResult result;
            try
            {
                result = session.DeleteSpline(currentSummary, selectedSplineTile, selectedSpline);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("splinevar.status.delete_failed", ("id", selectedSpline.Id), ("reason", exception.Message));
                return;
            }
            if (!result.Succeeded)
            {
                var reasons = new List<string>();
                foreach (var blocker in result.Blockers)
                    if (reasons.Count < 3)
                        reasons.Add(blocker.ToString());
                SetStatusT("spline.status.refs", ("id", selectedSpline.Id), ("count", result.Blockers.Count), ("reasons", string.Join("; ", reasons)));
                return;
            }
            var id = selectedSpline.Id;
            PushHistory(new ObjectEditStep { SplineChange = result.Change });
            ClearSelectedSpline();
            RefreshSplineViews(result.Change);
            RenderInspector();
            SetStatusT("spline.status.deleted", ("id", id));
        }

        /// <summary>
        /// While a spline handle is dragged: plans the joint, or the bend through the pointer, and shows the changed
        /// splines; on release builds it as one undo step, unless the plan has errors. With Alt the joint stays free of
        /// kinks, or the bent spline runs straight.
        /// </summary>
        private void PreviewSplineEnd(double x, double y, bool commit, bool alt)
        {
            if (draggedSplineEnd == null || currentSummary == null || mapCanvas == null)
                return;
            var move = bendingSpline
                ? OmsiSplineEditing.PlanBend(currentSummary, draggedSplineEnd.Tile, draggedSplineEnd.Spline, x, y, alt)
                : OmsiSplineEditing.PlanJointMove(currentSummary, draggedSplineEnd, x, y, alt);
            var pieces = new List<OmsiMapPoint[]>();
            foreach (var geometry in move.Splines)
                pieces.Add(OmsiMapGeometry.BuildSplinePolyline(geometry.Tile, geometry.Preview(), currentSummary.TileSizeMeters));
            if (!commit)
            {
                mapCanvas.SetRoadPreview(pieces, !move.HasErrors, move.Issues, null, null);
                var text = bendingSpline
                    ? Loc.T("spline.drag.bend", ("alt", alt ? "yes" : "no"), ("id", draggedSplineEnd.Spline.Id))
                    : Loc.T("spline.drag.move", ("which", draggedSplineEnd.AtEnd ? "end" : "start"), ("id", draggedSplineEnd.Spline.Id), ("joined", move.Splines.Count > 1 ? "yes" : "no"),
                        ("alt", alt ? "yes" : "no"));
                var twoArcs = move.Splines.FindAll(geometry => geometry.SplitOff);
                if (twoArcs.Count > 0)
                    text += Loc.T("spline.drag.arcs", ("list", string.Join(", #", twoArcs.ConvertAll(split => split.Spline.Id))));
                foreach (var issue in move.Issues)
                    text += "; " + issue.Message;
                SetStatus(text);
                return;
            }

            var id = draggedSplineEnd.Spline.Id;
            draggedSplineEnd = null;
            mapCanvas.ClearRoadPreview();
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.move_splines"));
                return;
            }
            if (move.HasErrors)
            {
                SetStatusT("spline.status.stays", ("id", id), ("reason", move.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message));
                return;
            }
            List<OmsiSplineChange> changes;
            IReadOnlyList<OmsiReference> blockers;
            try
            {
                changes = session.ApplyJointMove(currentSummary, move, out blockers);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is IOException)
            {
                Debug.LogException(exception);
                RefreshAfterChange(null);
                SetStatusT("spline.status.move_failed", ("id", id), ("reason", exception.Message));
                return;
            }
            if (changes == null)
            {
                RefreshAfterChange(null);
                SetStatus(Loc.T("spline.status.stays_split", ("id", id), ("references", DescribeReferences(blockers))));
                return;
            }
            if (changes.Count == 0)
                return;
            var parts = new List<ObjectEditStep>();
            var moved = new List<int>();
            foreach (var change in changes)
            {
                parts.Add(new ObjectEditStep { SplineChange = change });
                RefreshSplineViews(change);
                foreach (var changedId in change.SplineIds)
                    if (changedId != id && !moved.Contains(changedId))
                        moved.Add(changedId);
            }
            PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts, Description = Loc.T("spline.step.move", ("id", id)) });
            var warnings = move.Issues.FindAll(issue => issue.Severity == OmsiRoadIssueSeverity.Warning);
            var splits = move.Splines.FindAll(geometry => geometry.SplitOff);
            SetStatusT("spline.status.moved", ("bent", bendingSpline ? "yes" : "no"), ("id", id), ("hasMoved", moved.Count > 0 ? "yes" : "no"), ("moved", string.Join(", #", moved)),
                          ("hasSplit", splits.Count > 0 ? "yes" : "no"), ("split", string.Join(", #", splits.ConvertAll(split => split.Spline.Id))),
                          ("hasWarning", warnings.Count > 0 ? "yes" : "no"), ("warning", warnings.Count > 0 ? warnings[0].Message : string.Empty));
        }

        /// <summary>The junction an object file holds when the editor made it, read once; null for any other object.</summary>
        private OmsiGeneratedJunction MadeJunction(string assetPath)
        {
            if (currentSummary == null || !OmsiGeneratedJunction.IsMadeHere(assetPath))
                return null;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var key = root + "|" + assetPath;
            if (!madeJunctions.TryGetValue(key, out var junction))
            {
                // Made objects are never overwritten (ADR 026), so what was read stays true.
                junction = OmsiGeneratedJunction.Read(root, assetPath, RoadProfiles().Get);
                madeJunctions[key] = junction;
            }
            return junction;
        }

        /// <summary>
        /// Shows a handle on each arm of the one selected object when the editor made it as a junction, a little inside the
        /// connection point so the point's diamond stays visible; dragging one turns that arm.
        /// </summary>
        private void UpdateArmHandles()
        {
            if (mapCanvas == null)
                return;
            var item = selection.Count == 1 ? selection[0] : null;
            if (!CanEdit || item?.Tile == null || MadeJunction(item.Record.AssetPath) == null)
            {
                mapCanvas.SetArmHandles(null);
                return;
            }
            var size = currentSummary.TileSizeMeters;
            var centre = OmsiMapGeometry.TileLocalToWorld(item.Tile.X, item.Tile.Y, item.Record.LocalX, item.Record.LocalY, size);
            var handles = new List<OmsiMapPoint>();
            foreach (var port in Junctions().Ports(item.Tile, item.Record, size))
            {
                var length = Math.Sqrt((port.Position.X - centre.X) * (port.Position.X - centre.X) + (port.Position.Y - centre.Y) * (port.Position.Y - centre.Y));
                var inset = Math.Min(3.0, length / 3.0);
                var radians = port.OutwardHeadingDegrees * Math.PI / 180.0;
                handles.Add(new OmsiMapPoint(port.Position.X - Math.Sin(radians) * inset, port.Position.Y - Math.Cos(radians) * inset));
            }
            mapCanvas.SetArmHandles(handles);
        }

        /// <summary>An arm handle of the selected junction was grabbed: finds what is joined to each of its points.</summary>
        private void StartArmTurn(int arm)
        {
            armRebuild = null;
            draggedArm = arm;
            var item = selection.Count == 1 ? selection[0] : null;
            var made = item == null ? null : MadeJunction(item.Record.AssetPath);
            if (made == null || item.Tile == null)
                return;
            armRebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, item.Tile, item.Record);
            if (armRebuild == null)
                SetStatusT("lanes.status.mismatch", ("id", item.Record.Id));
        }

        /// <summary>
        /// While an arm handle is dragged: turns the arm towards the pointer in steps of 5 degrees, or freely with Alt, and
        /// shows the arms and the roads led to their points; on release makes the junction again under
        /// Sceneryobjects\Mapforge, puts it in place of the old one and leads the roads to it, as one undo step, unless the
        /// plan has errors.
        /// </summary>
        private void PreviewArmTurn(double x, double y, bool commit, bool alt)
        {
            if (armRebuild == null || currentSummary == null || mapCanvas == null)
                return;
            var rebuild = armRebuild;
            var junction = rebuild.Junction;
            var size = currentSummary.TileSizeMeters;
            var centre = OmsiMapGeometry.TileLocalToWorld(rebuild.Tile.X, rebuild.Tile.Y, junction.LocalX, junction.LocalY, size);
            var heading = OmsiRoadPlanner.ChordHeading(centre.X, centre.Y, x, y) - junction.HeadingDegrees;
            if (!alt)
                heading = Math.Round(heading / 5.0) * 5.0;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var plain = File.Exists(Path.Combine(root, PlainAsphalt)) ? PlainAsphalt : null;
            var turn = rebuild.PlanTurn(draggedArm, heading, plain);
            var unchanged = Math.Abs(OmsiRoadPlanner.Normalize(turn.HeadingDegrees - rebuild.Made.Arms[draggedArm].HeadingDegrees)) < 0.01;
            var degrees = turn.HeadingDegrees.ToString("0.#", CultureInfo.InvariantCulture) + "\u00b0";
            var issues = new List<OmsiRoadIssue>();
            foreach (var lead in turn.Leads)
                issues.AddRange(lead.Piece.Issues);
            if (!commit)
            {
                // The arms from the centre to their points, and the pieces leading the roads to them.
                var pieces = new List<OmsiMapPoint[]>();
                foreach (var port in turn.Ports)
                    pieces.Add(new[] { centre, port.Position });
                foreach (var lead in turn.Leads)
                    foreach (var piece in lead.Piece.Pieces)
                    {
                        var count = Math.Max(2, (int)Math.Ceiling(piece.LengthMeters / 2.0));
                        var points = new OmsiMapPoint[count + 1];
                        for (var step = 0; step <= count; step++)
                        {
                            piece.PointAt(piece.LengthMeters * step / count, out var pointX, out var pointY, out _);
                            points[step] = new OmsiMapPoint(pointX, pointY);
                        }
                        pieces.Add(points);
                    }
                mapCanvas.SetRoadPreview(pieces, !turn.HasErrors, issues, null, null);
                var text = Loc.T("junction.drag.turn", ("n", draggedArm + 1), ("id", junction.Id), ("degrees", degrees), ("alt", alt ? "yes" : "no"));
                var furthest = 0.0;
                foreach (var lead in turn.Leads)
                    furthest = Math.Max(furthest, lead.CutMeters);
                if (turn.Leads.Count > 0)
                    text += Loc.T("junction.drag.leads", ("meters", furthest));
                if (turn.Problem != null)
                    text += "; " + turn.Problem;
                foreach (var issue in issues)
                    text += "; " + issue.Message;
                SetStatus(text);
                return;
            }

            armRebuild = null;
            mapCanvas.ClearRoadPreview();
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.turn_arms"));
                return;
            }
            if (unchanged)
            {
                SetStatusT("junction.status.arm_stays", ("n", draggedArm + 1), ("id", junction.Id));
                return;
            }
            if (turn.HasErrors)
            {
                SetStatusT("lanes.status.stays", ("id", junction.Id), ("reason", turn.Problem ?? issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message));
                return;
            }
            string assetPath;
            try
            {
                // The markings, areas and arrows go along; their anchors are named by arm, so they follow the turned arm.
                var made = OmsiJunctionGenerator.Junction(turn.Arms, plain, "Mapforge " + rebuild.Made.Name, rebuild.Made.Markings, rebuild.Made.Areas,
                    rebuild.Made.Arrows, rebuild.Made.Connections, rebuild.Made.Signals);
                WithSignalReport(made);
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, rebuild.Made.Name, made);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("lanes.status.rebuild_failed", ("reason", exception.Message));
                return;
            }
            OmsiJunctionInsertionResult result;
            try
            {
                result = session.RebuildJunction(currentSummary, rebuild, turn, assetPath);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                RefreshAfterChange(null);
                SetStatusT("lanes.status.rebuild_failed_id", ("id", junction.Id), ("reason", exception.Message));
                return;
            }
            if (!result.Succeeded)
            {
                RefreshAfterChange(null);
                SetStatus(Loc.T("junction.status.stays_roads", ("id", junction.Id), ("references", DescribeReferences(result.Blockers))));
                return;
            }
            var steps = new List<object>(result.Steps);
            foreach (var step in steps)
                if (step is OmsiSplineChange change)
                    RefreshSplineViews(change);
            roadPorts = null;
            PushSteps(steps, Loc.T("junction.step.turn", ("n", draggedArm + 1), ("id", junction.Id)));
            var warnings = issues.FindAll(issue => issue.Severity == OmsiRoadIssueSeverity.Warning);
            SetStatusT("junction.status.turned", ("n", draggedArm + 1), ("id", junction.Id), ("degrees", degrees), ("name", Path.GetFileNameWithoutExtension(assetPath)),
                          ("hasWarning", warnings.Count > 0 ? "yes" : "no"), ("warning", warnings.Count > 0 ? warnings[0].Message : string.Empty));
        }

        /// <summary>
        /// Gives the ends of the selected spline new heights, as one undo step: a changed end takes the spline joined
        /// there along, and each spline keeps the height at its other end (<see cref="OmsiSplineEditing.PlanJointHeight"/>).
        /// </summary>
        private void SetSplineHeights(double startHeight, double endHeight)
        {
            if (selectedSpline == null || currentSummary == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.spline_heights"));
                return;
            }
            var id = selectedSpline.Id;
            var steps = new List<ObjectEditStep>();
            var warnings = new List<string>();
            string failure = null;
            foreach (var atEnd in new[] { false, true })
            {
                // A change reads the tiles again, so the spline is looked up anew for its second end.
                var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == id));
                var spline = tile?.Splines.Find(candidate => candidate.Id == id);
                if (spline == null)
                    break;
                var target = atEnd ? endHeight : startHeight;
                if (Math.Abs(target - (atEnd ? spline.EndHeight : spline.Height)) < 0.0005)
                    continue;
                var plan = OmsiSplineEditing.PlanJointHeight(currentSummary, new OmsiSplineEnd(tile, spline, atEnd), target,
                    Junctions()?.PortsAround(currentSummary, tile));
                if (plan.HasErrors)
                {
                    failure = plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message;
                    break;
                }
                OmsiSplineChange change;
                try
                {
                    change = session.UpdateSplines(currentSummary, plan.Splines);
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is IOException)
                {
                    Debug.LogException(exception);
                    failure = exception.Message;
                    break;
                }
                if (change == null)
                    continue;
                steps.Add(new ObjectEditStep { SplineChange = change });
                RefreshSplineViews(change);
                foreach (var issue in plan.Issues)
                    warnings.Add(issue.Message);
            }

            if (steps.Count > 0)
                PushHistory(steps.Count == 1 ? steps[0] : new ObjectEditStep { Group = steps, Description = Loc.T("spline.step.heights", ("id", id)) });
            if (failure != null)
            {
                SetStatusT("spline.heights.failed", ("id", id), ("failure", failure), ("saved", steps.Count > 0 ? "yes" : "no"));
                return;
            }
            if (steps.Count == 0)
            {
                SetStatusT("spline.heights.unchanged", ("id", id));
                return;
            }
            var joined = new List<string>();
            foreach (var step in steps)
                foreach (var changedId in step.SplineChange.SplineIds)
                    if (changedId != id && !joined.Contains("#" + changedId))
                        joined.Add("#" + changedId);
            SetStatusT("spline.heights.changed", ("id", id), ("hasJoined", joined.Count > 0 ? "yes" : "no"), ("joined", string.Join(", ", joined)),
                          ("hasWarnings", warnings.Count > 0 ? "yes" : "no"), ("warnings", string.Join("; ", warnings)));
        }

        /// <summary>Fits the terrain to the road of the selected spline, or stops fitting it, as one undo step.</summary>
        private void SetRoadTerrainFit(bool align)
        {
            if (selectedSpline == null || !CanEdit)
                return;
            OmsiSplineChange change;
            try
            {
                change = session.SetTerrainAlignment(currentSummary, session.BuiltRoadOf(currentSummary, selectedSpline), align);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT(align ? "terrain.road.fit_failed" : "terrain.road.restore_failed", ("reason", exception.Message)) /* i18n:keys */;
                return;
            }
            if (change == null)
            {
                SetStatusT("terrain.road.nothing");
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change });
            RefreshSplineViews(change);
            SetStatusT("terrain.road.changed", ("align", align ? "yes" : "no"), ("count", change.SplineIds.Count));
        }

        /// <summary>
        /// Fits the terrain anew around every road built here with terrain alignment, such as roads built before the way of
        /// fitting changed. The terrain follows from the original terrain and the roads alone, so this is no undo step.
        /// </summary>
        /// <summary>
        /// Makes a straight junction object in the profile chosen in the road tool, stores it under Sceneryobjects\Mapforge
        /// in the installation (ADR 026) and starts placing it, so it can be inserted into a road and tried in OMSI.
        /// </summary>
        /// <param name="profilePath">The profile to use; null takes the one chosen in the road tool.</param>
        /// <param name="arms">0 for a straight piece, 3 for a T junction, 4 for crossroads.</param>
        private void CreateTestJunction(string profilePath = null, int arms = 0)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.create_junctions"));
                return;
            }
            profilePath = profilePath ?? roadPanel?.Profile;
            if (profilePath == null)
            {
                SetStatusT("junction.test.choose_profile");
                return;
            }
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                var profile = new OmsiSplineProfileCatalog(root).Get(profilePath);
                if (profile == null)
                {
                    SetStatusT("junction.test.unreadable", ("path", profilePath));
                    return;
                }
                var kind = arms == 3 ? "T junction " : arms == 4 ? "Crossroads " : "Test ";
                var name = kind + Path.GetFileNameWithoutExtension(profilePath);
                OmsiGeneratedObject generated;
                if (arms >= 3)
                {
                    // The plain asphalt of the base game, where installed, so no lane markings run across the junction.
                    var plain = File.Exists(Path.Combine(root, PlainAsphalt)) ? PlainAsphalt : null;
                    var headings = arms == 3 ? new[] { 90.0, 270.0, 0.0 } : new[] { 0.0, 90.0, 180.0, 270.0 };
                    var layoutArms = new List<OmsiJunctionArm>();
                    foreach (var heading in headings)
                        layoutArms.Add(new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = profilePath, Profile = profile });
                    generated = OmsiJunctionGenerator.Junction(layoutArms, plain, "Mapforge " + name);
                }
                else
                    generated = OmsiJunctionGenerator.StraightPiece(profilePath, profile, 12.0, "Mapforge " + name);
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, name, generated);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("junction.test.failed", ("reason", exception.Message));
                return;
            }
            // Placing needs the clicks the road tool would take, and the library lists the new object after reading the
            // installation again.
            if (activeTool == EditorTool.Road)
                SetTool(EditorTool.Drag);
            StartInstallationScan(true);
            StartPlacement(assetPath);
            SetStatusT("junction.test.created", ("path", assetPath));
        }

        private void RefitRoadTerrain()
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.fit_terrain"));
                return;
            }
            List<OmsiTileSummary> tiles;
            try
            {
                tiles = session.RefitAllTerrain(currentSummary);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("terrain.all.failed", ("reason", exception.Message));
                return;
            }
            if (tiles.Count == 0)
            {
                SetStatusT("terrain.all.fits");
                return;
            }
            groundRenderer?.RefreshTerrain(tiles);
            objectRenderer?.RefreshTerrain(tiles, session.Source);
            if (roadTerrain != null)
                roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
            foreach (var tile in tiles)
                UpdateBrowserEntry(tile);
            UpdateMapStateLabel();
            SetStatusT("terrain.all.fitted", ("count", tiles.Count));
        }

        /// <summary>Draws the changed splines again from above and in 3D and updates the counts.</summary>
        private void RefreshSplineViews(OmsiSplineChange change)
        {
            // Free ends moved, so the guide lines are read again.
            roadGuideLines = null;
            mapCanvas?.RefreshSplines();
            // Rows along the changed splines follow them.
            objectRenderer?.RefreshPlacements();
            if (groundRenderer != null && !groundRenderer.RefreshSplines(change.Tiles))
                Debug.Log("Spline surfaces of the changed tiles are built once the ground has finished loading.");
            if (change.TerrainTiles.Count > 0)
            {
                groundRenderer?.RefreshTerrain(change.TerrainTiles);
                objectRenderer?.RefreshTerrain(change.TerrainTiles, session.Source);
                // The road tool follows the fitted terrain from now on.
                if (roadTerrain != null)
                    roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
            }
            groundRenderer?.RefreshTerrainHoles(currentSummary);
            if (roadPorts != null)
                UpdateRoadPorts();
            if (selectedSpline != null)
            {
                // Undo and redo read the tiles again; the selected record is found again by its ID, or the selection ends.
                var id = selectedSpline.Id;
                var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == id));
                if (tile == null)
                    ClearSelectedSpline();
                else
                {
                    selectedSplineTile = tile;
                    selectedSpline = tile.Splines.Find(spline => spline.Id == id);
                    // The splines chosen besides it are found again the same way; those gone leave the choice.
                    var more = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
                    foreach (var pair in moreSplines)
                    {
                        var moreTile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == pair.Value.Id));
                        if (moreTile != null)
                            more.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(moreTile, moreTile.Splines.Find(spline => spline.Id == pair.Value.Id)));
                    }
                    mapCanvas?.SetSelectedSpline(selectedSpline);
                    SetMoreSplines(more);
                }
            }
            UpdateCounts();
            foreach (var tile in change.Tiles)
                UpdateBrowserEntry(tile);
            UpdateMapStateLabel();
            RenderInspector();
            ScheduleRoutesRebuild();
        }

        private void BeginRoadTool()
        {
            if (roadPanel == null || mapCanvas == null || currentSummary == null)
                return;
            StartInstallationScan();
            UpdateRoadProfiles();
            roadPanel.SetStandalone(EditingVariant);
            roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
            objectsPanel.style.display = DisplayStyle.None;
            roadPanel.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            EndRoad();
            mapCanvas.BeginRoad();
            roadPorts = null;
            UpdateRoadPorts();
            SetStatus(EditingVariant ? Loc.T("roadbuild.status.start_variant", ("title", editedVariant.Title)) : Loc.T("roadbuild.status.start"));
        }

        private void EndRoadTool()
        {
            EndRoad();
            mapCanvas?.EndRoad();
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (roadBuilder != null)
                roadBuilder.style.display = DisplayStyle.None;
            if (pickingRowObject)
            {
                // The library stood in for the road builder to choose a row's object; it closes with the road tool.
                pickingRowObject = false;
                objectsPanel.UseOptions(null, null);
                if (!objectsPanelOpen)
                    objectsPanel.style.display = DisplayStyle.None;
            }
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            roadTerrain = null;
            roadPorts = null;
            roadPortChecks = null;
            pathModeProfile = null;
            RefreshJunctionMarkers();
        }

        /// <summary>The connection points of the installation's objects, read again when another installation is open.</summary>
        private OmsiJunctionCatalog Junctions()
        {
            if (currentSummary == null)
                return null;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (junctionCatalog == null || !string.Equals(root, junctionCatalogRoot, StringComparison.OrdinalIgnoreCase))
            {
                junctionCatalog = new OmsiJunctionCatalog(root);
                junctionCatalogRoot = root;
            }
            return junctionCatalog;
        }

        /// <summary>Reads every connection point of the map, if needed, and checks what each is joined to now.</summary>
        private void UpdateRoadPorts()
        {
            var catalog = Junctions();
            if (catalog == null)
                return;
            roadPorts = roadPorts ?? catalog.AllPorts(currentSummary);
            roadPortChecks = OmsiJunctionCatalog.Check(currentSummary, roadPorts);
            RefreshJunctionMarkers();
        }

        /// <summary>
        /// Shows the connection points that matter now: the free ones while building roads, and all points of a selected
        /// junction object with their state.
        /// </summary>
        private void RefreshJunctionMarkers()
        {
            if (mapCanvas == null)
                return;
            var markers = new List<JunctionMarker>();
            if (activeTool == EditorTool.Road && roadPortChecks != null)
                foreach (var check in roadPortChecks)
                    if (check.State != OmsiJunctionPortState.Connected)
                        markers.Add(Marker(check));
            if (selectedObject != null && selectedTile != null && currentSummary != null && selection.Count <= 1)
            {
                var catalog = Junctions();
                var pose = junctionPreview;
                var ports = catalog == null ? null
                    : pose == null ? catalog.Ports(selectedTile, selectedObject, currentSummary.TileSizeMeters)
                    : catalog.Ports(selectedTile, selectedObject, currentSummary.TileSizeMeters, pose[0], pose[1], pose[2], pose[3], pose[4], pose[5]);
                if (ports != null && ports.Count > 0)
                    foreach (var check in OmsiJunctionCatalog.Check(currentSummary, ports, null, catalog.PortsAround(currentSummary, selectedTile)))
                        markers.Add(Marker(check));
            }
            mapCanvas.SetJunctionMarkers(markers);
        }

        /// <summary>Moves the connection points of the selected object along while it is dragged or turned; null ends that.</summary>
        private void PreviewJunctionMarkers(Func<OmsiObjectRecord, double[]> pose)
        {
            junctionPreview = pose != null && selectedObject != null ? pose(selectedObject) : null;
            RefreshJunctionMarkers();
        }

        private static JunctionMarker Marker(OmsiJunctionPortCheck check)
        {
            return new JunctionMarker
            {
                Position = check.Port.Position, Height = (float)check.Port.Height, HeadingDegrees = (float)check.Port.OutwardHeadingDegrees, State = check.State
            };
        }

        /// <summary>Ends the road being drawn; the next click starts a new one.</summary>
        private void EndRoad()
        {
            roadStarted = false;
            roadStartJoin = null;
            roadStartPort = null;
            roadStartHeading = null;
            roadStartHeight = null;
            roadControl = null;
            parallelEnds.Clear();
            parallelSplineIds.Clear();
            UpdateRoadPreview();
        }

        /// <summary>The spline profiles of the installation and of the map, with the map's counts, for the road panel.</summary>
        private void UpdateRoadProfiles()
        {
            if (roadPanel == null || currentSummary == null)
                return;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in currentSummary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    counts.TryGetValue(spline.AssetPath, out var count);
                    counts[spline.AssetPath] = count + 1;
                }
            roadPanel.SetInstallation(OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath));
            roadPanel.SetProfiles(RoadProfilePaths(), counts);
            roadPanel.SetOrigins(contentOrigins, HideDlcContent);
        }

        private OmsiSplineEnd SnapEnd(OmsiMapPoint point)
        {
            // In 3D the snap distance grows with the distance of the camera, as the pixels per metre do.
            var reach = mapCanvas != null && mapCanvas.ThreeDimensional
                ? Math.Max(1.0, mapCanvas.OrbitDistance * 0.02)
                : Math.Max(0.5, RoadSnapPixels * (mapCanvas?.MetersPerPixel ?? 1f));
            var end = OmsiSplineEnd.FindFree(currentSummary, point.X, point.Y, reach);
            // The road never joins the end it starts from.
            if (end != null && roadStartJoin != null && ReferenceEquals(end.Spline, roadStartJoin.Spline) && end.AtEnd == roadStartJoin.AtEnd)
                return null;
            return end;
        }

        /// <summary>The free connection point of a junction object a road would snap to at a point, or null.</summary>
        private OmsiJunctionPort SnapPort(OmsiMapPoint point)
        {
            if (roadPorts == null)
                UpdateRoadPorts();
            if (roadPorts == null || roadPorts.Count == 0)
                return null;
            var reach = mapCanvas != null && mapCanvas.ThreeDimensional
                ? Math.Max(1.0, mapCanvas.OrbitDistance * 0.02)
                : Math.Max(0.5, RoadSnapPixels * (mapCanvas?.MetersPerPixel ?? 1f));
            var port = OmsiJunctionCatalog.FindFree(currentSummary, roadPorts, point.X, point.Y, reach);
            // The road never ends at the point it starts from.
            if (port != null && roadStartPort != null && ReferenceEquals(port.Object, roadStartPort.Object) && port.Index == roadStartPort.Index)
                return null;
            return port;
        }

        /// <summary>The free spline end or connection point a road snaps to at a point, whichever is nearer.</summary>
        private void Snap(OmsiMapPoint point, out OmsiSplineEnd join, out OmsiJunctionPort port)
        {
            join = null;
            port = null;
            if (roadPanel != null && !roadPanel.SnapToRoads)
                return;
            join = SnapEnd(point);
            port = SnapPort(point);
            if (join == null || port == null)
                return;
            if (Distance(port.Position, point) < Distance(join.Position(currentSummary.TileSizeMeters), point))
                join = null;
            else
                port = null;
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        private static string DescribePort(OmsiJunctionPort port)
        {
            return Loc.T("roadbuild.port", ("n", port.Index + 1), ("id", port.Object.Id), ("name", Path.GetFileNameWithoutExtension(port.Object.AssetPath)));
        }

        /// <summary>
        /// The road from the current start to the pointer: to a free spline end or connection point there if the road snaps
        /// to roads, else to the pointer snapped to right angles; <paramref name="end"/> is where it ends.
        /// </summary>
        private OmsiRoadPlan PlanRoad(OmsiMapPoint pointer, out OmsiSplineEnd endJoin, out OmsiJunctionPort endPort, out OmsiMapPoint end)
        {
            Snap(pointer, out endJoin, out endPort);
            end = endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position ?? SnapRoadPointer(pointer);
            return PlanRoadBetween(roadStart, roadStartHeading, roadStartHeight, roadStartJoin?.OutwardGradientPercent, end, endJoin, endPort);
        }

        /// <summary>
        /// The pointer snapped to right angles for the next point of the road (ADR 043), or as it is: the end of a Curved
        /// road square to the leg to its control point, a Free road ending straight on or square to its start, and other
        /// points square to the road continued, else to the reference of the start.
        /// </summary>
        private OmsiMapPoint SnapRoadPointer(OmsiMapPoint pointer)
        {
            roadGuideHits.Clear();
            if (roadPanel == null)
                return pointer;
            // The path tool's splines start and end on the paths already there (ADR 086, step 4); the path's line shows what it joined.
            var onPath = SnapToPath(pointer);
            if (onPath != null)
            {
                roadGuideHits.Add(new OmsiGuideLine(onPath.X, onPath.Y, onPath.HeadingDegrees, true));
                return new OmsiMapPoint(onPath.X, onPath.Y);
            }
            OmsiGuideLine direction = null;
            var point = pointer;
            if (roadStarted && roadPanel.SnapToAngles)
            {
                double x, y;
                var origin = roadStart;
                var snapped = false;
                if (roadPanel.Shape == OmsiRoadShape.Curved && roadControl.HasValue)
                {
                    origin = roadControl.Value;
                    var leg = OmsiRoadPlanner.ChordHeading(roadStart.X, roadStart.Y, origin.X, origin.Y);
                    snapped = OmsiRoadSnapping.SnapToAngle(origin.X, origin.Y, leg, 90.0, pointer.X, pointer.Y, out x, out y);
                }
                else if (roadPanel.Shape == OmsiRoadShape.Free && roadStartHeading.HasValue)
                {
                    // The end of a bending road lies on no straight line, so guides do not join in.
                    return OmsiRoadSnapping.SnapArcEnd(roadStart.X, roadStart.Y, roadStartHeading.Value, pointer.X, pointer.Y, out x, out y)
                        ? new OmsiMapPoint(x, y)
                        : pointer;
                }
                else
                    snapped = OmsiRoadSnapping.SnapToAngle(roadStart.X, roadStart.Y, roadStartHeading ?? roadReferenceHeading, 90.0, pointer.X, pointer.Y, out x, out y);
                if (snapped)
                {
                    point = new OmsiMapPoint(x, y);
                    direction = new OmsiGuideLine(origin.X, origin.Y, OmsiRoadPlanner.ChordHeading(origin.X, origin.Y, x, y), false);
                }
            }
            // On guide lines of the roads nearby; a right-angle direction keeps its direction and meets them where they cross it.
            if (roadPanel.SnapToGuides && OmsiRoadGuides.Snap(RoadGuideLines(), pointer.X, pointer.Y, RoadSnapReach(), RoadGuideReach, direction,
                    out var guideX, out var guideY, roadGuideHits))
                return new OmsiMapPoint(guideX, guideY);
            if (direction != null)
                roadGuideHits.Add(direction);
            return point;
        }

        /// <summary>
        /// The kind of path the chosen profile is for the path tool: 1 for people and 0 for vehicles, as OMSI's invisible
        /// sidewalks and streets are (<see cref="OmsiPathSnapping.PathToolKind"/>); null for any other profile.
        /// </summary>
        private int? RoadPathKind()
        {
            if (roadPanel?.Profile == null || currentSummary == null)
                return null;
            return OmsiPathSnapping.PathToolKind(RoadProfiles().Get(roadPanel.Profile));
        }

        /// <summary>The point of an existing path of the kind a path profile draws that the pointer snaps to, or null (other profiles, snapping off, paths not read yet).</summary>
        private OmsiPathSnap SnapToPath(OmsiMapPoint pointer)
        {
            var kind = RoadPathKind();
            if (!kind.HasValue || mapPaths == null || !roadPanel.SnapToRoads)
                return null;
            return OmsiPathSnapping.Nearest(mapPaths, kind.Value, pointer.X, pointer.Y, RoadSnapReach() * 1.6);
        }

        /// <summary>
        /// The way traffic goes on the vehicle path under a point the path tool snapped to: along its points, or against them for a
        /// path that runs the other way; null for other profiles and points off any path. A path drawn from or to the point leaves
        /// or reaches it with this heading, so that it joins the lane (ADR 089).
        /// </summary>
        private double? PathTrafficHeadingAt(OmsiMapPoint point)
        {
            if (RoadPathKind() != 0)
                return null;
            var snap = SnapToPath(point);
            if (snap == null || snap.DistanceMeters > 0.05)
                return null;
            return snap.Path.Direction == 1 ? (snap.HeadingDegrees + 180.0) % 360.0 : snap.HeadingDegrees;
        }

        /// <summary>
        /// Whether a road built now fits the terrain: as the road tool's switch says, never for the path tool's invisible splines, whose
        /// terrain alignment would cut holes into the ground along them (ADR 089).
        /// </summary>
        private bool FitTerrainForRoad()
        {
            return roadPanel.FitTerrain && RoadPathKind() == null;
        }

        private string pathModeProfile;

        /// <summary>
        /// Choosing a path profile (an invisible street or sidewalk) turns on the paths view and reads the map's paths, so
        /// the road tool can snap to them; says so once for each choice.
        /// </summary>
        private void UpdatePathMode()
        {
            var kind = RoadPathKind();
            var profile = kind.HasValue ? roadPanel.Profile : null;
            if (string.Equals(profile, pathModeProfile, StringComparison.Ordinal))
                return;
            pathModeProfile = profile;
            ShowPathOptions();
            if (!kind.HasValue)
                return;
            if (!pathsShown)
                ShowPaths(true);
            else if (mapPaths == null && pathsTask == null)
                StartPathsBuild(currentSummary, false);
            SetStatusT(kind.Value == 1 ? "roadbuild.path_mode.foot" : "roadbuild.path_mode.road"); // i18n:keys
        }

        /// <summary>
        /// Shows the width and direction of the chosen path profile in the road panel, or hides them for other profiles; the panel
        /// reports a change through <see cref="ApplyPathOptions"/>.
        /// </summary>
        private void ShowPathOptions()
        {
            if (roadPanel == null || currentSummary == null)
                return;
            var kind = RoadPathKind();
            var profile = kind.HasValue ? RoadProfiles().Get(roadPanel.Profile) : null;
            roadPanel.ShowPathOptions(kind, OmsiPathProfiles.WidthOf(profile) ?? 3.0, OmsiPathProfiles.IsBothWays(profile));
        }

        /// <summary>
        /// A new width or direction chosen for the paths: a path type with them is made from the chosen one (written below the project's
        /// Splines\Mapforge folder once) and chosen, so the splines the tool draws next have it.
        /// </summary>
        private void ApplyPathOptions()
        {
            var kind = RoadPathKind();
            if (!kind.HasValue || currentSummary == null || !CanEdit)
                return;
            string path;
            try
            {
                path = OmsiPathProfiles.Create(OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath), currentProjectName, roadPanel.Profile,
                    roadPanel.PathWidth, kind == 0 && roadPanel.PathBothWays);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.path_type.failed", ("reason", exception.Message));
                return;
            }
            if (!string.Equals(path, roadPanel.Profile, StringComparison.OrdinalIgnoreCase))
            {
                madeRoadTypes.Add(path);
                UpdateRoadProfiles();
                roadPanel.ChooseProfile(path);
            }
            ShowPathOptions();
            SetStatusT("roadbuild.path_type.chosen", ("name", Path.GetFileNameWithoutExtension(path)), ("width", roadPanel.PathWidth)); // i18n:keys
        }

        /// <summary>How far from the pointer a road snaps, in metres: a few pixels from above, more in 3D the further the camera is.</summary>
        private double RoadSnapReach()
        {
            return mapCanvas != null && mapCanvas.ThreeDimensional
                ? Math.Max(1.0, mapCanvas.OrbitDistance * 0.02)
                : Math.Max(0.5, RoadSnapPixels * (mapCanvas?.MetersPerPixel ?? 1f));
        }

        /// <summary>The guide lines of the map's free spline ends and connection points, read again after roads changed.</summary>
        private List<OmsiGuideLine> RoadGuideLines()
        {
            if (roadPorts == null)
                UpdateRoadPorts();
            if (roadGuideLines == null || !ReferenceEquals(roadGuidePorts, roadPorts))
            {
                roadGuideLines = OmsiRoadGuides.Of(currentSummary, roadPorts);
                roadGuidePorts = roadPorts;
            }
            return roadGuideLines;
        }

        /// <summary>
        /// Shows the road tool's construction lines: the legs of a curve and the guide lines the pointer snapped to, from
        /// their origin to the point they gave and a little beyond.
        /// </summary>
        private void ShowRoadGuides(OmsiMapPoint? snapped, params OmsiMapPoint[][] legs)
        {
            var lines = new List<OmsiMapPoint[]>(legs);
            if (snapped.HasValue)
                foreach (var line in roadGuideHits)
                {
                    var heading = line.HeadingDegrees * Math.PI / 180.0;
                    var at = line.Along(snapped.Value.X, snapped.Value.Y);
                    var from = line.BothWays ? Math.Min(0.0, at - RoadGuideOvershoot) : 0.0;
                    var along = line.BothWays ? Math.Max(0.0, at + RoadGuideOvershoot) : at + RoadGuideOvershoot;
                    lines.Add(new[]
                    {
                        new OmsiMapPoint(line.OriginX + Math.Sin(heading) * from, line.OriginY + Math.Cos(heading) * from),
                        new OmsiMapPoint(line.OriginX + Math.Sin(heading) * along, line.OriginY + Math.Cos(heading) * along)
                    });
                }
            mapCanvas.SetRoadGuides(lines);
        }

        /// <summary>
        /// The control point of a Curved road a click at the pointer sets: on the heading of the road it continues, as the
        /// road must leave that way, else the pointer snapped to right angles.
        /// </summary>
        private OmsiMapPoint ControlPointAt(OmsiMapPoint pointer)
        {
            if (!roadStartHeading.HasValue)
                return SnapRoadPointer(pointer);
            var heading = roadStartHeading.Value * Math.PI / 180.0;
            var along = Math.Max(1.0, (pointer.X - roadStart.X) * Math.Sin(heading) + (pointer.Y - roadStart.Y) * Math.Cos(heading));
            return new OmsiMapPoint(roadStart.X + Math.Sin(heading) * along, roadStart.Y + Math.Cos(heading) * along);
        }

        /// <summary>Plans a road with the road panel's settings from a start, with its heading, height and gradient where known, to an end.</summary>
        private OmsiRoadPlan PlanRoadBetween(OmsiMapPoint start, double? startHeading, double? startHeight, double? startGradient, OmsiMapPoint end,
            OmsiSplineEnd endJoin, OmsiJunctionPort endPort)
        {
            // The mode shapes the road from where it starts; parts after a junction or a transition run straight in Straight
            // mode and bend on tangentially otherwise.
            var fromStart = roadStarted && Math.Abs(start.X - roadStart.X) < 1e-6 && Math.Abs(start.Y - roadStart.Y) < 1e-6;
            var shape = roadPanel.Shape == OmsiRoadShape.Straight ? OmsiRoadShape.Straight : fromStart ? roadPanel.Shape : OmsiRoadShape.Free;
            var control = fromStart && shape == OmsiRoadShape.Curved ? roadControl : null;
            // A Straight road rounds the corner where it turns off a straight road built here, which is cut back for it.
            // In a chrono variant a road stands alone, so no corner is rounded: that would cut the road before back (ADR 066).
            var cornerSpline = fromStart && roundCorners && !EditingVariant && shape == OmsiRoadShape.Straight && roadStartJoin != null &&
                               roadStartJoin.Spline.RadiusMeters == 0.0 && session.IsBuiltHere(roadStartJoin.Spline)
                ? roadStartJoin.Spline
                : null;
            var request = new OmsiRoadRequest
            {
                StartX = start.X, StartY = start.Y, StartHeadingDegrees = startHeading, StartHeight = startHeight, EndX = end.X, EndY = end.Y,
                StartGradientPercent = startGradient, Shape = shape, ControlX = control?.X, ControlY = control?.Y,
                CornerRadiusMeters = cornerSpline != null ? roadPanel.CornerRadius : 0.0,
                StartTrimMaximumMeters = cornerSpline != null ? cornerSpline.LengthMeters - OmsiRoadPlanner.MinimumLength : 0.0,
                HeightMode = roadPanel.HeightMode, MaximumPieceLength = roadPanel.PieceLength, GradientWarningPercent = roadPanel.GradientWarningPercent,
                EndHeight = endJoin?.Height ?? endPort?.Height ?? (roadPanel.HeightMode == OmsiRoadHeightMode.Manual ? roadPanel.EndHeight : null),
                // Driving into the joined spline or junction continues opposite to the way a road would leave it.
                EndJoinHeadingDegrees = endJoin != null ? endJoin.OutwardHeadingDegrees + 180.0
                    : endPort != null ? endPort.OutwardHeadingDegrees + 180.0 : PathTrafficHeadingAt(end),
                HalfWidthMeters = RoadProfileHalfWidth()
            };
            return OmsiRoadPlanner.Plan(request, roadTerrain);
        }

        /// <summary>The spline profiles of the installation, read again when another installation is open.</summary>
        private OmsiSplineProfileCatalog RoadProfiles()
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (roadProfiles == null || !string.Equals(root, roadProfilesRoot, StringComparison.OrdinalIgnoreCase))
            {
                roadProfiles = new OmsiSplineProfileCatalog(root);
                roadProfilesRoot = root;
            }
            return roadProfiles;
        }

        /// <summary>The width of the chosen road profile from edge to edge, or null when there is none or it cannot be read.</summary>
        private double? RoadProfileWidth()
        {
            if (roadPanel?.Profile == null || currentSummary == null)
                return null;
            var profile = RoadProfiles().Get(roadPanel.Profile);
            return profile != null && profile.RightEdge > profile.LeftEdge ? profile.RightEdge - profile.LeftEdge : (double?)null;
        }

        /// <summary>Half the width of the chosen road profile, or null when there is none or it cannot be read.</summary>
        private double? RoadProfileHalfWidth()
        {
            if (roadPanel?.Profile == null || currentSummary == null)
                return null;
            var profile = RoadProfiles().Get(roadPanel.Profile);
            return profile != null && profile.RightEdge > profile.LeftEdge ? Math.Max(-profile.LeftEdge, profile.RightEdge) : (double?)null;
        }

        /// <summary>
        /// What a road to the target would meet (ADR 028, ADR 033): a road its end lies on and the roads it crosses on the
        /// way; nothing when junctions are switched off or a profile is no road, and no T junction when the road ends on a
        /// free spline end or connection point. The crossings are found on the road as planned now; building plans each
        /// part again from the junction before it.
        /// </summary>
        private void FindJunction(OmsiRoadPlan plan, OmsiMapPoint target, OmsiSplineEnd endJoin, OmsiJunctionPort endPort, out OmsiSplinePoint tee,
            out List<OmsiRoadCrossing> crossings)
        {
            tee = null;
            crossings = new List<OmsiRoadCrossing>();
            if (!roadPanel.MakeJunctions || plan.HasErrors || plan.Pieces.Count == 0 || roadPanel.Profile == null || !CanEdit)
                return;
            var profiles = RoadProfiles();
            if (!OmsiRoadJunctions.IsRoad(profiles.Get(roadPanel.Profile)))
                return;
            var ignore = new HashSet<int>(parallelSplineIds);
            if (roadStartJoin != null)
                ignore.Add(roadStartJoin.Spline.Id);
            if (endJoin != null)
                ignore.Add(endJoin.Spline.Id);
            if (endJoin == null && endPort == null)
                tee = OmsiRoadJunctions.RoadAt(currentSummary, profiles, target.X, target.Y);
            foreach (var crossing in OmsiRoadJunctions.Crossings(currentSummary, profiles, plan, 1.0, ignore))
                if (tee == null || crossing.Road.Spline.Id != tee.Spline.Id || crossing.Along < plan.LengthMeters - TeeReachMeters)
                    crossings.Add(crossing);
        }

        /// <summary>
        /// Makes a junction of the given arms under Sceneryobjects\Mapforge and inserts it into the road at a point, the
        /// main arms along the road and the others towards the pointer; adds the steps to <paramref name="steps"/>.
        /// </summary>
        private OmsiObjectRecord InsertGeneratedJunction(List<OmsiJunctionArm> arms, OmsiSplinePoint at, OmsiMapPoint pointer, List<object> steps, string friendlyName = null)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                var plain = File.Exists(Path.Combine(root, PlainAsphalt)) ? PlainAsphalt : null;
                var name = friendlyName ?? "Junction " + Path.GetFileNameWithoutExtension(arms[0].ProfilePath) + " x " + Path.GetFileNameWithoutExtension(arms[arms.Count - 1].ProfilePath);
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, name, OmsiJunctionGenerator.Junction(arms, plain, "Mapforge " + name));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.status.junction_failed", ("reason", exception.Message));
                return null;
            }
            var catalog = Junctions();
            var plan = OmsiJunctionInsertion.Plan(currentSummary, catalog, assetPath, at.Tile, at.Spline, at.Distance, pointer.X, pointer.Y,
                OmsiRoadJunctions.MainPair(catalog.Helpers(assetPath)));
            if (plan == null || plan.Problem != null)
            {
                SetStatusT("roadbuild.status.no_junction", ("reason", plan?.Problem ?? Loc.T("roadbuild.status.no_points")));
                return null;
            }
            OmsiJunctionInsertionResult result;
            try
            {
                result = session.InsertJunction(currentSummary, plan);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.status.insert_failed", ("reason", exception.Message));
                return null;
            }
            if (!result.Succeeded)
            {
                SetStatus(Loc.T("roadbuild.status.cut_blocked", ("references", DescribeReferences(result.Blockers))));
                return null;
            }
            foreach (var step in result.Steps)
                if (step is OmsiSplineChange change)
                    RefreshSplineViews(change);
            steps.AddRange(result.Steps);
            // The main road's arms get the right of way, the others give way (ADR 047).
            ApplyMadePriorities(result.Junction, arms, steps);
            // The road tool snaps to the new junction's points from now on.
            roadPorts = null;
            return result.Junction;
        }

        /// <summary>
        /// A click with the road tool's crossing switch on: cuts the road under the pointer and puts a short piece of the same
        /// road into it, with a pedestrian crossing in its middle, as one undo step (ADR 086, step 5). The piece is a made junction
        /// of two opposite arms, so its footpath and its vehicle lanes lie in one object, which is what lets cars give way to people.
        /// </summary>
        private void InsertCrossingPiece(OmsiMapPoint point)
        {
            if (EditingVariant)
            {
                SetStatusT("roadbuild.crossing.variant");
                return;
            }
            var profiles = RoadProfiles();
            var road = OmsiRoadJunctions.RoadAt(currentSummary, profiles, point.X, point.Y);
            if (road == null)
            {
                SetStatusT("roadbuild.crossing.no_road");
                return;
            }
            var path = road.Spline.AssetPath;
            var profile = profiles.Get(path);
            var arms = OmsiRoadJunctions.Arms(path, profile, path, profile, new double[0], new double[0]);
            arms[0].Crossing = new OmsiCrossing();
            // The layout says whether the road can carry a crossing (pavements on both sides, no rails), before anything is cut.
            var layout = OmsiJunctionLayout.Build(arms, null);
            if (layout.CrossingProblems.Length > 0 && layout.CrossingProblems[0] != null)
            {
                SetStatusT("roadbuild.crossing.refused", ("reason", layout.CrossingProblems[0]));
                return;
            }
            var steps = new List<object>();
            var junction = InsertGeneratedJunction(arms, road, point, steps, "Crossing " + Path.GetFileNameWithoutExtension(path));
            if (junction == null)
                return;
            PushSteps(steps, Loc.T("roadbuild.crossing.step", ("id", road.Spline.Id)));
            SetStatusT("roadbuild.crossing.done", ("id", junction.Id));
        }

        /// <summary>The connection point of a placed junction whose outward heading lies nearest a heading.</summary>
        private OmsiJunctionPort PortFacing(OmsiObjectRecord junction, double headingDegrees)
        {
            var tile = currentSummary.Tiles.Find(candidate => candidate.Objects.Contains(junction));
            OmsiJunctionPort best = null;
            foreach (var port in Junctions().Ports(tile, junction, currentSummary.TileSizeMeters))
                if (best == null || Math.Abs(OmsiRoadPlanner.Normalize(port.OutwardHeadingDegrees - headingDegrees)) <
                    Math.Abs(OmsiRoadPlanner.Normalize(best.OutwardHeadingDegrees - headingDegrees)))
                    best = port;
            return best;
        }

        /// <summary>Builds a road in the road panel's profile and adds its change to the steps; null with the reason shown when it cannot be built.</summary>
        private OmsiSplineChange BuildRoadPart(OmsiMapPoint start, double? startHeading, double? startHeight, double? startGradient, OmsiSplineEnd startJoin,
            OmsiMapPoint end, OmsiSplineEnd endJoin, OmsiJunctionPort endPort, List<object> steps)
        {
            var plan = PlanRoadBetween(start, startHeading, startHeight, startGradient, end, endJoin, endPort);
            if (plan.HasErrors)
            {
                SetStatusT("roadbuild.status.to_junction", ("reason", plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message));
                return null;
            }
            try
            {
                var change = session.CreateRoad(currentSummary, plan, roadPanel.Profile, startJoin, endJoin, FitTerrainForRoad());
                steps.Add(change);
                RefreshSplineViews(change);
                if (roadTerrain != null)
                    roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
                return change;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.status.build_failed", ("reason", exception.Message));
                return null;
            }
        }

        /// <summary>Takes back steps done so far, the last first, after a later one failed.</summary>
        private void RevertSteps(List<object> steps)
        {
            for (var index = steps.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (steps[index] is OmsiSplineChange change)
                    {
                        session.Undo(currentSummary, change);
                        RefreshSplineViews(change);
                    }
                    else if (steps[index] is OmsiObjectChange item)
                        session.Undo(currentSummary, item);
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
                {
                    Debug.LogException(exception);
                }
            }
            steps.Clear();
            roadPorts = null;
            RefreshAfterChange(null);
        }

        /// <summary>Records steps done together as one undo step with a description.</summary>
        private void PushSteps(List<object> steps, string description)
        {
            var parts = new List<ObjectEditStep>();
            foreach (var step in steps)
            {
                if (step is OmsiSplineChange change)
                    parts.Add(new ObjectEditStep { SplineChange = change });
                else if (step is OmsiObjectChange item)
                    // (a change of an attached object, such as a signal head on a mast, has no object record but its element id)
                    parts.Add(new ObjectEditStep { TileX = item.Tile.X, TileY = item.Tile.Y, ObjectId = item.Record?.Id ?? item.ElementId, Change = item });
            }
            PushHistory(new ObjectEditStep { Group = parts, Description = description });
            StartInstallationScan(true);
            RefreshAfterChange(null);
        }

        /// <summary>
        /// The arms of a transition at a free spline end (ADR 028): ahead (0°) the road panel's profile, which the new road
        /// leaves on or arrives on, behind (180°) the profile of the spline at the end, turned over when that spline runs
        /// towards the end.
        /// </summary>
        private List<OmsiJunctionArm> TransitionArms(OmsiSplineEnd join, bool arriving)
        {
            var profiles = RoadProfiles();
            return new List<OmsiJunctionArm>
            {
                new OmsiJunctionArm { HeadingDegrees = 0.0, ProfilePath = roadPanel.Profile, Profile = profiles.Get(roadPanel.Profile), Mirrored = arriving },
                new OmsiJunctionArm { HeadingDegrees = 180.0, ProfilePath = join.Spline.AssetPath, Profile = profiles.Get(join.Spline.AssetPath), Mirrored = join.AtEnd }
            };
        }

        private static OmsiProfileSections SectionsOf(OmsiJunctionArm arm)
        {
            return OmsiProfileSections.Of(arm.Mirrored ? arm.Profile.Mirrored() : arm.Profile);
        }

        /// <summary>True when a road joining a free spline end of another road differs in width from it, so a transition goes between them.</summary>
        private bool NeedsTransition(OmsiSplineEnd join, bool arriving)
        {
            if (join == null || !roadPanel.MakeJunctions || roadPanel.Profile == null || !CanEdit ||
                string.Equals(join.Spline.AssetPath?.Trim(), roadPanel.Profile.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            var arms = TransitionArms(join, arriving);
            if (!OmsiRoadJunctions.IsRoad(arms[0].Profile) || !OmsiRoadJunctions.IsRoad(arms[1].Profile))
                return false;
            return OmsiJunctionLayout.EdgeChange(SectionsOf(arms[0]), SectionsOf(arms[1])) >= 0.25;
        }

        /// <summary>
        /// Makes a transition from the road panel's profile to the spline at a free end and places it on that end, running on
        /// from it; returns the connection point the new road joins, or null with the reason shown.
        /// </summary>
        private OmsiJunctionPort PlaceTransition(OmsiSplineEnd join, bool arriving, List<object> steps)
        {
            var arms = TransitionArms(join, arriving);
            var length = OmsiJunctionLayout.TransitionLength(SectionsOf(arms[0]), SectionsOf(arms[1]));
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                var plain = File.Exists(Path.Combine(root, PlainAsphalt)) ? PlainAsphalt : null;
                var name = "Transition " + Path.GetFileNameWithoutExtension(arms[0].ProfilePath) + " x " + Path.GetFileNameWithoutExtension(arms[1].ProfilePath);
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, name, OmsiJunctionGenerator.Junction(arms, plain, "Mapforge " + name));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.status.transition_failed", ("reason", exception.Message));
                return null;
            }
            // The transition runs on from the end, its far half towards the new road.
            var size = currentSummary.TileSizeMeters;
            var heading = join.OutwardHeadingDegrees;
            var end = join.Position(size);
            var radians = heading * Math.PI / 180.0;
            var x = end.X + Math.Sin(radians) * length / 2.0;
            var y = end.Y + Math.Cos(radians) * length / 2.0;
            var tile = currentSummary.Tiles.Find(candidate => candidate.IsAvailable && candidate.X == (int)Math.Floor(x / size) && candidate.Y == (int)Math.Floor(y / size)) ??
                       join.Tile;
            try
            {
                var change = session.PlaceObject(currentSummary, tile, assetPath, x - tile.X * size, y - tile.Y * size, join.Height, heading);
                steps.Add(change);
                roadPorts = null;
                return PortFacing(change.Record, heading);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.status.transition_place_failed", ("reason", exception.Message));
                return null;
            }
        }

        /// <summary>Builds the road with a transition at the free spline end it starts from, ends at, or both.</summary>
        /// <param name="earlier">Steps already taken for this click, such as a rounded corner; undone with the rest when a part fails.</param>
        private void BuildWithTransitions(OmsiMapPoint target, OmsiSplineEnd endJoin, OmsiJunctionPort endPort, bool atStart, bool atEnd,
            List<object> earlier = null)
        {
            var steps = earlier ?? new List<object>();
            var start = roadStart;
            var startHeading = roadStartHeading;
            var startHeight = roadStartHeight;
            var startGradient = roadStartJoin?.OutwardGradientPercent;
            var startJoin = roadStartJoin;
            if (atStart)
            {
                var port = PlaceTransition(roadStartJoin, false, steps);
                if (port == null)
                    return;
                start = port.Position;
                startHeading = port.OutwardHeadingDegrees;
                startHeight = port.Height;
                startGradient = null;
                startJoin = null;
            }
            var end = endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position ?? target;
            if (atEnd)
            {
                var port = PlaceTransition(endJoin, true, steps);
                if (port == null)
                {
                    RevertSteps(steps);
                    return;
                }
                end = port.Position;
                endJoin = null;
                endPort = port;
            }
            var change = BuildRoadPart(start, startHeading, startHeight, startGradient, startJoin, end, endJoin, endPort, steps);
            if (change == null)
            {
                RevertSteps(steps);
                return;
            }
            PushSteps(steps, Loc.T("roadbuild.step.transitions", ("two", atStart && atEnd ? "yes" : "no")));
            var message = Loc.T("roadbuild.status.transitions", ("two", atStart && atEnd ? "yes" : "no"));
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(message);
                return;
            }
            ContinueRoadFrom(change);
            SetStatus(message + " " + Loc.T("roadbuild.status.go_on"));
        }

        /// <summary>
        /// Builds a road that crosses other roads, ends on one, or both (ADR 028, ADR 033): new crossroads go into each
        /// crossed road in turn, the road runs in parts from arm to arm, planned again from each junction, and a road it
        /// ends on gets a new T junction. Parallel roads given go across the roads they cross with crossroads of their own,
        /// as the second carriageway of a dual road does (ADR 051). All of it is one undo step; when a part of the road
        /// fails, the parts before it are taken back, and a parallel road that fails is left out with the reason shown.
        /// </summary>
        /// <param name="earlier">Steps already taken for this click, such as a rounded corner; undone with the rest when a part fails.</param>
        /// <param name="parallels">The parallel roads to build across too, by side; null for none.</param>
        private void BuildJunctions(OmsiMapPoint target, bool endsOnRoad, List<object> earlier = null, List<KeyValuePair<int, OmsiRoadPlan>> parallels = null)
        {
            Snap(target, out var endJoin, out var endPort);
            var steps = earlier ?? new List<object>();
            var junctionIds = new List<int>();
            var crossedIds = new List<int>();
            var lastParallelEnds = new Dictionary<int, KeyValuePair<int, bool>>(parallelEnds);
            var parts = new List<OmsiSplineChange>();
            if (!BuildAcross(roadStart, roadStartHeading, roadStartHeight, roadStartJoin?.Spline.Id, roadStartJoin?.AtEnd ?? false, target, endJoin?.Spline.Id,
                    endJoin?.AtEnd ?? false, endPort, endsOnRoad, steps, junctionIds, crossedIds, parts, out var tee, out var teeJunction))
                return;
            var parallelNote = parallels != null ? BuildParallelsAcross(parallels, lastParallelEnds, steps, junctionIds) : string.Empty;

            if (tee != null)
            {
                PushSteps(steps, DescribeJunctions(junctionIds.Count, true));
                EndRoad();
                SetStatus(Loc.T("roadbuild.status.tee", ("hasCrossed", crossedIds.Count > 0 ? "yes" : "no"), ("splines", DescribeSplines(crossedIds)), ("spline", tee.Spline.Id),
                    ("junction", teeJunction.Id), ("note", parallelNote)));
                return;
            }
            PushSteps(steps, DescribeJunctions(junctionIds.Count, false));
            var message = Loc.T("roadbuild.status.across", ("splines", DescribeSplines(crossedIds)), ("junctions", string.Join(", #", junctionIds)), ("note", parallelNote));
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(message);
                return;
            }
            ContinueRoadFrom(parts[parts.Count - 1]);
            SetStatus(message + " " + Loc.T("roadbuild.status.go_on"));
        }

        /// <summary>
        /// Builds a road in the road panel's profile from a start to a target in parts from arm to arm of new crossroads in
        /// each road it crosses, ending on a joined spline end, a connection point, a road it ends on with a new T junction,
        /// or free. Adds the steps, and the road's own parts in order to <paramref name="parts"/>; false with the reason
        /// shown and the steps taken back when a part fails.
        /// </summary>
        private bool BuildAcross(OmsiMapPoint start, double? startHeading, double? startHeight, int? startJoinId, bool startJoinAtEnd, OmsiMapPoint target,
            int? endJoinId, bool endJoinAtEnd, OmsiJunctionPort endPort, bool endsOnRoad, List<object> steps, List<int> junctionIds, List<int> crossedIds,
            List<OmsiSplineChange> parts, out OmsiSplinePoint tee, out OmsiObjectRecord teeJunction)
        {
            var profiles = RoadProfiles();
            tee = null;
            teeJunction = null;
            // Splines are read again after every part, so the joined ends are found again by their IDs.
            OmsiSplineEnd StartJoin()
            {
                var join = startJoinId.HasValue ? FindSplineEnd(startJoinId.Value, startJoinAtEnd) : null;
                return join != null && join.IsFree ? join : null;
            }
            OmsiSplineEnd endJoin = null;
            var built = junctionIds.Count;
            while (true)
            {
                var startJoin = StartJoin();
                if (startJoinId.HasValue && startJoin == null || !RefindEnd(endJoinId, endJoinAtEnd, out endJoin))
                {
                    FailJunctions(steps, Loc.T("roadbuild.fail.changed"));
                    return false;
                }
                var end = endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position ?? target;
                var plan = PlanRoadBetween(start, startHeading, startHeight, startJoin?.OutwardGradientPercent, end, endJoin, endPort);
                if (plan.HasErrors)
                {
                    FailJunctions(steps, plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
                    return false;
                }
                var crossing = NextCrossing(plan, target, startJoin, endJoin, endsOnRoad);
                if (crossing == null)
                    break;
                if (junctionIds.Count - built >= MaximumJunctionsPerRoad)
                {
                    FailJunctions(steps, Loc.T("roadbuild.fail.too_many", ("max", MaximumJunctionsPerRoad)));
                    return false;
                }
                var road = crossing.Road;
                var relative = CrossingAngle(crossing);
                if (Math.Abs(relative) < 30.0 || Math.Abs(relative) > 150.0)
                {
                    FailJunctions(steps, Loc.T("roadbuild.fail.shallow", ("id", road.Spline.Id), ("angle", Math.Abs(relative))));
                    return false;
                }
                // The new road arrives on the arm facing its start and leaves on the one ahead.
                var arms = OmsiRoadJunctions.Arms(road.Spline.AssetPath, profiles.Get(road.Spline.AssetPath), roadPanel.Profile, profiles.Get(roadPanel.Profile),
                    new[] { relative + 180.0 }, new[] { relative });
                var junction = InsertGeneratedJunction(arms, road, start, steps);
                if (junction == null)
                {
                    FailJunctionAt(steps, road.Spline.Id, junctionIds.Count - built);
                    return false;
                }
                junctionIds.Add(junction.Id);
                crossedIds.Add(road.Spline.Id);
                startJoin = StartJoin();
                var back = PortFacing(junction, crossing.HeadingDegrees + 180.0);
                var ahead = PortFacing(junction, crossing.HeadingDegrees);
                var part = startJoinId.HasValue && startJoin == null
                    ? null
                    : BuildRoadPart(start, startHeading, startHeight, startJoin?.OutwardGradientPercent, startJoin, back.Position, null, back, steps);
                if (part == null)
                {
                    RevertSteps(steps);
                    return false;
                }
                parts.Add(part);
                // The rest of the road leaves the junction on the arm ahead.
                start = ahead.Position;
                startHeading = ahead.OutwardHeadingDegrees;
                startHeight = ahead.Height;
                startJoinId = null;
            }

            tee = endsOnRoad && endJoin == null && endPort == null ? OmsiRoadJunctions.RoadAt(currentSummary, profiles, target.X, target.Y) : null;
            var lastStartJoin = StartJoin();
            if (tee != null)
            {
                var chain = OmsiSplineChain.Around(currentSummary, tee.Tile, tee.Spline, 1.0);
                chain.FrameAt(tee.Distance, out var point, out var heading, out _);
                var side = OmsiRoadJunctions.SideHeading(OmsiRoadPlanner.ChordHeading(point.X, point.Y, start.X, start.Y) - heading);
                // The new road arrives on the side arm.
                var arms = OmsiRoadJunctions.Arms(tee.Spline.AssetPath, profiles.Get(tee.Spline.AssetPath), roadPanel.Profile, profiles.Get(roadPanel.Profile),
                    new[] { side }, new double[0]);
                teeJunction = InsertGeneratedJunction(arms, tee, start, steps);
                if (teeJunction == null)
                {
                    FailJunctionAt(steps, tee.Spline.Id, junctionIds.Count - built);
                    return false;
                }
                lastStartJoin = StartJoin();
                var port = PortFacing(teeJunction, teeJunction.HeadingDegrees + side);
                var toTee = startJoinId.HasValue && lastStartJoin == null
                    ? null
                    : BuildRoadPart(start, startHeading, startHeight, lastStartJoin?.OutwardGradientPercent, lastStartJoin, port.Position, null, port, steps);
                if (toTee == null)
                {
                    RevertSteps(steps);
                    return false;
                }
                parts.Add(toTee);
                return true;
            }

            var finalEnd = endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position ?? target;
            var final = startJoinId.HasValue && lastStartJoin == null
                ? null
                : BuildRoadPart(start, startHeading, startHeight, lastStartJoin?.OutwardGradientPercent, lastStartJoin, finalEnd, endJoin, endPort, steps);
            if (final == null)
            {
                RevertSteps(steps);
                return false;
            }
            parts.Add(final);
            return true;
        }

        /// <summary>
        /// Builds the parallel roads of a road that crosses other roads (ADR 043, ADR 051), each across the same roads with
        /// crossroads of its own, joined to the parallel road of the part before on its side where they meet. A parallel road
        /// that cannot be built is left out with its own steps taken back. Adds the steps and junctions and returns what to
        /// add to the status.
        /// </summary>
        private string BuildParallelsAcross(List<KeyValuePair<int, OmsiRoadPlan>> parallels, Dictionary<int, KeyValuePair<int, bool>> lastEnds, List<object> steps,
            List<int> junctionIds)
        {
            parallelEnds.Clear();
            parallelSplineIds.Clear();
            var opposite = roadPanel.ParallelOpposite;
            var built = 0;
            var problems = new List<string>();
            foreach (var pair in parallels)
            {
                var parallel = pair.Value;
                var side = Loc.T(pair.Key < 0 ? "roadbuild.side.left" : "roadbuild.side.right"); // i18n:keys
                if (parallel.HasErrors)
                {
                    problems.Add(side + ": " + parallel.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
                    continue;
                }
                var first = parallel.Pieces[0];
                var lastPiece = parallel.Pieces[parallel.Pieces.Count - 1];
                lastPiece.PointAt(lastPiece.LengthMeters, out var endX, out var endY, out _);
                var start = new OmsiMapPoint(first.StartX, first.StartY);
                var target = new OmsiMapPoint(endX, endY);
                // The parallel road of the part before joins where this one meets it: at its start, or running the other way at its end.
                var join = lastEnds.TryGetValue(pair.Key, out var before) ? FindSplineEnd(before.Key, before.Value) : null;
                if (join != null && (!join.IsFree || Distance(join.Position(currentSummary.TileSizeMeters), opposite ? target : start) > 0.5))
                    join = null;
                var own = new List<object>();
                var ownJunctions = new List<int>();
                var parts = new List<OmsiSplineChange>();
                if (!BuildAcross(start, first.HeadingDegrees, first.StartHeight, opposite ? null : join?.Spline.Id, join?.AtEnd ?? false, target,
                        opposite ? join?.Spline.Id : null, join?.AtEnd ?? false, null, false, own, ownJunctions, new List<int>(), parts, out _, out _))
                {
                    problems.Add(side + ": " + (statusLabel?.text ?? Loc.T("roadbuild.fail.not_built")));
                    continue;
                }
                steps.AddRange(own);
                junctionIds.AddRange(ownJunctions);
                built++;
                foreach (var part in parts)
                    parallelSplineIds.UnionWith(part.SplineIds);
                // The next part's parallel road joins where this one ends ahead: its last spline, or running the other way its first.
                var lastPart = parts[parts.Count - 1];
                parallelEnds[pair.Key] = opposite
                    ? new KeyValuePair<int, bool>(parts[0].SplineIds[0], false)
                    : new KeyValuePair<int, bool>(lastPart.SplineIds[lastPart.SplineIds.Count - 1], true);
            }
            var parallelNote = built > 0 ? Loc.T("roadbuild.note.parallel_built", ("count", built), ("distance", roadPanel.ParallelDistance)) : string.Empty;
            foreach (var problem in problems)
                parallelNote += Loc.T("roadbuild.note.parallel_problem", ("problem", problem));
            return parallelNote;
        }

        /// <summary>A road crosses at most this many roads in one click; more would make a long undo step few would want.</summary>
        private const int MaximumJunctionsPerRoad = 12;

        /// <summary>
        /// The first road a planned part crosses, other than the splines it joins; with <paramref name="endsOnRoad"/> not the
        /// road it ends on near its end, where it gets a T junction instead.
        /// </summary>
        private OmsiRoadCrossing NextCrossing(OmsiRoadPlan plan, OmsiMapPoint target, OmsiSplineEnd startJoin, OmsiSplineEnd endJoin, bool endsOnRoad)
        {
            var profiles = RoadProfiles();
            var ignore = new HashSet<int>(parallelSplineIds);
            if (startJoin != null)
                ignore.Add(startJoin.Spline.Id);
            if (endJoin != null)
                ignore.Add(endJoin.Spline.Id);
            var tee = endsOnRoad ? OmsiRoadJunctions.RoadAt(currentSummary, profiles, target.X, target.Y) : null;
            foreach (var crossing in OmsiRoadJunctions.Crossings(currentSummary, profiles, plan, 1.0, ignore))
                if (tee == null || crossing.Road.Spline.Id != tee.Spline.Id || crossing.Along < plan.LengthMeters - TeeReachMeters)
                    return crossing;
            return null;
        }

        /// <summary>Near its end a road ending on another may cross that road's centre line; that is the T junction, not a crossing.</summary>
        private const double TeeReachMeters = 20.0;

        /// <summary>The angle of a planned road against the road it crosses, from -180° to 180°.</summary>
        private double CrossingAngle(OmsiRoadCrossing crossing)
        {
            var chain = OmsiSplineChain.Around(currentSummary, crossing.Road.Tile, crossing.Road.Spline, 1.0);
            chain.FrameAt(crossing.Road.Distance, out _, out var heading, out _);
            return OmsiRoadPlanner.Normalize(crossing.HeadingDegrees - heading);
        }

        /// <summary>Finds the spline end the road ends on again by its ID; false when it went.</summary>
        private bool RefindEnd(int? id, bool atEnd, out OmsiSplineEnd end)
        {
            end = null;
            if (!id.HasValue)
                return true;
            var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == id.Value));
            var spline = tile?.Splines.Find(candidate => candidate.Id == id.Value);
            if (spline == null)
                return false;
            end = new OmsiSplineEnd(tile, spline, atEnd);
            return true;
        }

        /// <summary>
        /// Takes back what was built when a junction could not go into a road, and says at which road, since a long road
        /// may cross several.
        /// </summary>
        private void FailJunctionAt(List<object> steps, int splineId, int builtBefore)
        {
            var reason = statusLabel?.text ?? string.Empty;
            if (steps.Count > 0)
                RevertSteps(steps);
            SetStatus(Loc.T("roadbuild.status.nothing_built", ("before", builtBefore), ("id", splineId), ("reason", reason)));
        }

        private void FailJunctions(List<object> steps, string reason)
        {
            if (steps.Count > 0)
                RevertSteps(steps);
            SetStatusT("roadbuild.cannot", ("reason", reason));
        }

        private static string DescribeJunctions(int crossroads, bool tee)
        {
            var parts = new List<string>();
            if (crossroads > 0)
                parts.Add(Loc.T("roadbuild.junction.crossroads", ("count", crossroads)));
            if (tee)
                parts.Add(Loc.T("roadbuild.junction.tee"));
            return Loc.T("roadbuild.junction.whole", ("what", string.Join(Loc.T("roadbuild.junction.and"), parts)), ("tail", tee && crossroads == 0 ? "to" : "through"));
        }

        private static string DescribeSplines(List<int> ids)
        {
            return Loc.T("roadbuild.splines", ("count", ids.Count), ("ids", string.Join(", #", ids)));
        }

        /// <summary>The spline end the road started from, found again by its ID after splines were read again; null when it went.</summary>
        private OmsiSplineEnd RefindStartJoin(int? id)
        {
            if (!id.HasValue || roadStartJoin == null)
                return null;
            var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == id.Value));
            var spline = tile?.Splines.Find(candidate => candidate.Id == id.Value);
            var join = spline != null ? new OmsiSplineEnd(tile, spline, roadStartJoin.AtEnd) : null;
            return join != null && join.IsFree ? join : null;
        }

        /// <summary>Goes on building from the end of the last spline of a change.</summary>
        private void ContinueRoadFrom(OmsiSplineChange change)
        {
            var lastId = change.SplineIds[change.SplineIds.Count - 1];
            var lastTile = currentSummary.Tiles.Find(tile => tile.Splines.Exists(spline => spline.Id == lastId));
            var last = lastTile?.Splines.Find(spline => spline.Id == lastId);
            if (last == null)
            {
                EndRoad();
                return;
            }
            roadStartJoin = new OmsiSplineEnd(lastTile, last, true);
            roadStartPort = null;
            roadControl = null;
            roadStart = roadStartJoin.Position(currentSummary.TileSizeMeters);
            roadStartHeading = roadStartJoin.OutwardHeadingDegrees;
            roadStartHeight = roadStartJoin.Height;
            UpdateRoadPreview();
        }

        /// <summary>Plans the road to the pointer and shows it, or the spline end a click would start from.</summary>
        private void UpdateRoadPreview()
        {
            if (mapCanvas == null || roadPanel == null || activeTool != EditorTool.Road || currentSummary == null)
                return;
            UpdatePathMode();
            if (!roadPointer.HasValue)
            {
                mapCanvas.ClearRoadPreview();
                return;
            }
            mapCanvas.SetRoadGuides(null);
            if (roadPanel.CrossingMode)
            {
                mapCanvas.ClearRoadPreview();
                roadPanel.ShowPlan(Loc.T("roadbuild.crossing.plan"), false);
                return;
            }
            if (roadPanel.ParkingMode)
            {
                mapCanvas.ClearRoadPreview();
                roadPanel.ShowPlan(Loc.T(parkingChain == null ? "roadbuild.parking.plan_first" : "roadbuild.parking.plan_second"), false); // i18n:keys
                return;
            }
            if (!roadStarted)
            {
                Snap(roadPointer.Value, out var join, out var port);
                // A new road can start on a guide line too.
                OmsiMapPoint? onGuide = null;
                if (join == null && port == null)
                {
                    var start = SnapRoadPointer(roadPointer.Value);
                    if (roadGuideHits.Count > 0)
                        onGuide = start;
                    ShowRoadGuides(onGuide);
                }
                mapCanvas.SetRoadPreview(null, true, null, join?.Position(currentSummary.TileSizeMeters) ?? port?.Position ?? onGuide, null);
                roadPanel.ShowPlan(join != null
                    ? Loc.T("roadbuild.plan.continue", ("id", join.Spline.Id), ("end", join.AtEnd ? "end" : "start"))
                    : port != null
                        ? Loc.T("roadbuild.plan.start_port", ("port", DescribePort(port)), ("profile", Path.GetFileNameWithoutExtension(port.ProfilePath)))
                        : Loc.T("roadbuild.plan.start"), false);
                return;
            }

            var offset = Math.Sqrt((roadPointer.Value.X - roadStart.X) * (roadPointer.Value.X - roadStart.X) +
                                   (roadPointer.Value.Y - roadStart.Y) * (roadPointer.Value.Y - roadStart.Y));
            if (offset < OmsiRoadPlanner.MinimumLength)
            {
                // Right after a click the pointer still rests on the new start.
                mapCanvas.SetRoadPreview(null, true, null, null, roadStart);
                roadPanel.ShowPlan(Loc.T("roadbuild.plan.next"), false);
                return;
            }
            if (roadPanel.Shape == OmsiRoadShape.Curved && !roadControl.HasValue)
            {
                // Curved: first the control point the road leaves towards.
                var control = ControlPointAt(roadPointer.Value);
                mapCanvas.SetRoadPreview(null, true, null, null, roadStart);
                ShowRoadGuides(roadStartHeading.HasValue ? (OmsiMapPoint?)null : control, new[] { roadStart, control });
                roadPanel.ShowPlan(Loc.T("roadbuild.plan.control", ("along", roadStartHeading.HasValue ? "yes" : "no")), false);
                return;
            }
            var plan = PlanRoad(roadPointer.Value, out var endJoin, out var endPort, out var planEnd);
            var joined = endJoin != null || endPort != null;
            if (roadPanel.Shape == OmsiRoadShape.Curved && roadControl.HasValue)
                ShowRoadGuides(joined ? (OmsiMapPoint?)null : planEnd, new[] { roadStart, roadControl.Value, planEnd });
            else
                ShowRoadGuides(joined ? (OmsiMapPoint?)null : planEnd);
            var pieces = new List<OmsiMapPoint[]>();
            var heights = new List<double[]>();
            var issues = new List<OmsiRoadIssue>(plan.Issues);
            var shownPlans = new List<OmsiRoadPlan> { plan };
            foreach (var pair in ParallelPlans(plan))
                shownPlans.Add(pair.Value);
            foreach (var shown in shownPlans)
            {
                if (!ReferenceEquals(shown, plan))
                    issues.AddRange(shown.Issues);
                foreach (var piece in shown.Pieces)
                {
                    var steps = Math.Max(2, (int)Math.Ceiling(piece.LengthMeters / 2.0));
                    var points = new OmsiMapPoint[steps + 1];
                    var pointHeights = new double[steps + 1];
                    for (var step = 0; step <= steps; step++)
                    {
                        piece.PointAt(piece.LengthMeters * step / steps, out var x, out var y, out var height);
                        points[step] = new OmsiMapPoint(x, y);
                        pointHeights[step] = height;
                    }
                    pieces.Add(points);
                    heights.Add(pointHeights);
                }
            }
            mapCanvas.SetRoadPreview(pieces, !plan.HasErrors && roadPanel.Profile != null, issues,
                endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position, roadStart, heights);
            FindJunction(plan, planEnd, endJoin, endPort, out var tee, out var crossings);
            var meets = string.Empty;
            if (crossings.Count == 1)
                meets = Loc.T("roadbuild.meets.one", ("id", crossings[0].Road.Spline.Id), ("along", crossings[0].Along));
            else if (crossings.Count > 1)
                meets = Loc.T("roadbuild.meets.many", ("count", crossings.Count), ("list", string.Join(", ", crossings.ConvertAll(crossing => crossing.Along.ToString("F0", CultureInfo.InvariantCulture)))));
            var shallow = crossings.Find(crossing => Math.Abs(CrossingAngle(crossing)) < 30.0 || Math.Abs(CrossingAngle(crossing)) > 150.0);
            if (shallow != null)
                meets += Loc.T("roadbuild.meets.shallow", ("id", shallow.Road.Spline.Id));
            if (tee != null)
                meets += Loc.T("roadbuild.meets.tee", ("id", tee.Spline.Id));
            if (meets.Length == 0 && (NeedsTransition(roadStartJoin, false) || NeedsTransition(endJoin, true)))
                meets = Loc.T("roadbuild.meets.transition");
            var besides = string.Empty;
            for (var index = plan.Issues.Count; index < issues.Count; index++)
                if (issues[index].Severity != OmsiRoadIssueSeverity.Note && besides.IndexOf(issues[index].Message, StringComparison.Ordinal) < 0)
                    besides += Loc.T("roadbuild.besides", ("severity", issues[index].Severity == OmsiRoadIssueSeverity.Error ? "error" : "warning"), ("message", issues[index].Message));
            roadPanel.ShowPlan(DescribePlan(plan, endJoin, endPort) + meets + besides,
                plan.HasErrors || issues.Exists(issue => issue.Severity != OmsiRoadIssueSeverity.Note));
        }

        private static string DescribePlan(OmsiRoadPlan plan, OmsiSplineEnd endJoin, OmsiJunctionPort endPort)
        {
            var text = plan.Pieces.Count == 0
                ? Loc.T("roadbuild.describe.none")
                : Loc.T("roadbuild.describe.main", ("length", plan.LengthMeters), ("pieces", plan.Pieces.Count), ("straight", plan.Pieces[0].RadiusMeters == 0 ? "yes" : "no"),
                    ("radius", Math.Abs(plan.Pieces[0].RadiusMeters)), ("corner", plan.StartTrimMeters > 0.0 ? "yes" : "no"), ("cornerRadius", plan.CornerRadiusMeters),
                    ("trim", plan.StartTrimMeters), ("gradient", plan.MaximumGradientPercent), ("height", plan.EndHeight),
                    ("join", endJoin != null ? "spline" : endPort != null ? "port" : "none"), ("id", endJoin?.Spline.Id ?? 0), ("port", endPort != null ? DescribePort(endPort) : string.Empty));
            foreach (var issue in plan.Issues)
                text += Loc.T("roadbuild.issue", ("severity", issue.Severity.ToString()), ("message", issue.Message));
            return text;
        }

        /// <summary>A click with the road tool: starts a road, or builds it to the click and continues from its end.</summary>
        /// <summary>A click of the road tool; roads it builds get the rows of objects their road types ask for (ADR 049).</summary>
        private void BuildRoadTo(double x, double y)
        {
            var before = undoSteps.Count > 0 ? undoSteps[undoSteps.Count - 1] : null;
            BuildRoadClick(x, y);
            var after = undoSteps.Count > 0 ? undoSteps[undoSteps.Count - 1] : null;
            if (after != null && !ReferenceEquals(after, before))
                LayRoadTypeRows(after);
        }

        private void BuildRoadClick(double x, double y)
        {
            if (currentSummary == null || roadPanel == null)
                return;
            if (!CanEdit && !CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("readonly.action.build_roads"));
                return;
            }
            var point = new OmsiMapPoint(x, y);
            roadPointer = point;
            if (roadPanel.CrossingMode)
            {
                InsertCrossingPiece(point);
                return;
            }
            if (roadPanel.ParkingMode)
            {
                ClickParkingRow(point);
                return;
            }
            ResetParkingRow();
            if (!roadStarted)
            {
                Snap(point, out var join, out var port);
                roadStartJoin = join;
                roadStartPort = port;
                roadStart = join?.Position(currentSummary.TileSizeMeters) ?? port?.Position ?? SnapRoadPointer(point);
                // A vehicle path started on a lane leaves it the way the traffic goes there (ADR 089).
                roadStartHeading = join?.OutwardHeadingDegrees ?? port?.OutwardHeadingDegrees ?? (join == null && port == null ? PathTrafficHeadingAt(roadStart) : null);
                roadStartHeight = join?.Height ?? port?.Height;
                if (join == null && port == null && roadPanel.HeightMode == OmsiRoadHeightMode.Manual)
                    roadStartHeight = roadTerrain != null && roadTerrain.TryGetHeight(point.X, point.Y, out var ground) ? ground : roadPanel.EndHeight;
                if (roadPanel.HeightMode == OmsiRoadHeightMode.Manual && roadStartHeight.HasValue)
                    roadPanel.SuggestEndHeight(roadStartHeight.Value);
                // A free start takes its right angles from a road nearby, else from north.
                roadReferenceHeading = roadStartHeading ?? OmsiRoadSnapping.NearbyRoadHeading(currentSummary, roadStart.X, roadStart.Y, RoadReferenceReach) ?? 0.0;
                roadStarted = true;
                UpdateRoadPreview();
                var next = Loc.T(roadPanel.Shape == OmsiRoadShape.Curved ? "roadbuild.next.curved" : "roadbuild.next.free"); // i18n:keys
                SetStatus(join != null ? Loc.T("roadbuild.started.continue", ("id", join.Spline.Id), ("next", next))
                    : port != null ? Loc.T("roadbuild.started.port", ("port", DescribePort(port)), ("next", next))
                    : Loc.T("roadbuild.started", ("next", next)));
                return;
            }

            if (roadPanel.Profile == null)
            {
                SetStatusT("roadbuild.status.choose_profile");
                return;
            }
            if (roadPanel.Shape == OmsiRoadShape.Curved && !roadControl.HasValue)
            {
                var control = ControlPointAt(point);
                if (Distance(control, roadStart) < OmsiRoadPlanner.MinimumLength)
                {
                    SetStatusT("roadbuild.status.control_far");
                    return;
                }
                roadControl = control;
                UpdateRoadPreview();
                SetStatusT("roadbuild.status.control_set");
                return;
            }
            var plan = PlanRoad(point, out var endJoin, out var endPort, out var end);
            if (plan.HasErrors)
            {
                SetStatusT("roadbuild.cannot", ("reason", plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message));
                return;
            }
            // In a variant the road stands alone: no junction objects, corners, parallel roads or terrain fitting (ADR 066).
            if (EditingVariant)
            {
                BuildRoadInVariant(plan, endJoin, endPort);
                return;
            }
            FindJunction(plan, end, endJoin, endPort, out var tee, out var crossings);
            var transitionAtStart = NeedsTransition(roadStartJoin, false);
            var transitionAtEnd = NeedsTransition(endJoin, true);
            if (tee != null || crossings.Count > 0 || transitionAtStart || transitionAtEnd)
            {
                // A corner is rounded first; the parts are then planned again from the end of its arc, in line with the road.
                List<object> corner = null;
                var before = SaveRoadStart();
                if (plan.StartTrimMeters > 0.0 && !transitionAtStart)
                {
                    corner = BuildCorner(plan);
                    if (corner == null)
                        return;
                }
                // Parallel roads go across the roads crossed with crossroads of their own (ADR 051); not yet round a corner or
                // to a road end, junction or connection point the road ends on.
                var parallelsAcross = crossings.Count > 0 && corner == null && tee == null && endJoin == null && endPort == null && !transitionAtStart
                    ? ParallelPlans(plan)
                    : null;
                roundCorners = false;
                try
                {
                    if (tee != null || crossings.Count > 0)
                        BuildJunctions(end, tee != null, corner, parallelsAcross);
                    else
                        BuildWithTransitions(end, endJoin, endPort, transitionAtStart, transitionAtEnd, corner);
                }
                finally
                {
                    roundCorners = true;
                }
                // When a part failed, the corner was taken back with it; the road goes on from where it was.
                if (corner != null && corner.Count > 0 && !SplineExists(((OmsiSplineChange)corner[corner.Count - 1]).SplineIds[0]))
                    RestoreRoadStart(before);
                if (parallelsAcross == null || parallelsAcross.Count == 0)
                {
                    // The next part starts its parallel roads anew.
                    parallelEnds.Clear();
                    parallelSplineIds.Clear();
                    if (roadPanel.ParallelLeft || roadPanel.ParallelRight)
                        SetStatus((statusLabel?.text ?? string.Empty) + " " + Loc.T("roadbuild.no_parallel", ("reason", crossings.Count > 0 ? "corner" : "other")));
                }
                return;
            }
            // Planned on the terrain as it is, before the road fits the terrain to itself.
            var parallels = ParallelPlans(plan);
            var parallelNote = string.Empty;
            var steps = new List<object>();
            OmsiSplineChange change;
            try
            {
                var startJoin = roadStartJoin;
                if (plan.StartTrimMeters > 0.0)
                {
                    // The road before is cut back to where the rounded corner begins.
                    var startJoinId = roadStartJoin.Spline.Id;
                    var cut = session.UpdateSplines(currentSummary, new[] { OmsiSplineEditing.CutBack(roadStartJoin, plan.StartTrimMeters, plan.Pieces[0].StartHeight) });
                    steps.Add(cut);
                    RefreshSplineViews(cut);
                    startJoin = RefindStartJoin(startJoinId);
                    if (startJoin == null)
                        throw new InvalidOperationException("the road before could not be found again after cutting it back.");
                }
                change = session.CreateRoad(currentSummary, plan, roadPanel.Profile, startJoin, endJoin, FitTerrainForRoad());
                steps.Add(change);
                parallelNote = BuildParallels(plan, parallels, steps);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                RevertSteps(steps);
                SetStatusT("roadbuild.status.build_failed", ("reason", exception.Message));
                return;
            }
            if (steps.Count > 1)
                PushHistory(new ObjectEditStep
                {
                    Group = steps.ConvertAll(step => new ObjectEditStep { SplineChange = (OmsiSplineChange)step }),
                    Description = Loc.T(parallels.Count > 0 ? "roadbuild.step.parallel" : "roadbuild.step.corner") // i18n:keys
                });
            else
                PushHistory(new ObjectEditStep { SplineChange = change });
            RefreshSplineViews(change);

            var warnings = plan.Issues.FindAll(issue => issue.Severity != OmsiRoadIssueSeverity.Note).Count;
            var built = Loc.T("roadbuild.built", ("meters", plan.LengthMeters), ("count", change.SplineIds.Count), ("rounding", plan.StartTrimMeters > 0.0 ? "yes" : "no"),
                ("radius", plan.CornerRadiusMeters), ("warnings", warnings), ("note", parallelNote));
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(Loc.T("roadbuild.status.joined", ("built", built), ("target", endJoin != null ? Loc.T("splinevar.spline", ("id", endJoin.Spline.Id)) : DescribePort(endPort))));
                return;
            }
            // The road continues from the end of its last spline.
            ContinueRoadFrom(change);
            SetStatus(Loc.T(roadStarted ? "splinevar.built.going" : "splinevar.built.done", ("built", built))) /* i18n:keys */;
        }

        /// <summary>
        /// Cuts the road before back and builds the arc that rounds the corner of a planned Straight road (ADR 043), then
        /// goes on from the arc's end; returns the steps, or null with the reason shown and nothing changed.
        /// </summary>
        private List<object> BuildCorner(OmsiRoadPlan plan)
        {
            var steps = new List<object>();
            var arc = new OmsiRoadPlan();
            foreach (var piece in plan.Pieces)
            {
                if (piece.RadiusMeters == 0.0)
                    break;
                arc.Pieces.Add(piece);
            }
            if (arc.Pieces.Count == 0)
                return steps;
            try
            {
                var joinId = roadStartJoin.Spline.Id;
                var cut = session.UpdateSplines(currentSummary, new[] { OmsiSplineEditing.CutBack(roadStartJoin, plan.StartTrimMeters, plan.Pieces[0].StartHeight) });
                steps.Add(cut);
                RefreshSplineViews(cut);
                var startJoin = RefindStartJoin(joinId);
                if (startJoin == null)
                    throw new InvalidOperationException("the road before could not be found again after cutting it back.");
                var change = session.CreateRoad(currentSummary, arc, roadPanel.Profile, startJoin, null, FitTerrainForRoad());
                steps.Add(change);
                RefreshSplineViews(change);
                ContinueRoadFrom(change);
                return steps;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                RevertSteps(steps);
                SetStatusT("roadbuild.status.corner_failed", ("reason", exception.Message));
                return null;
            }
        }

        /// <summary>The roads to build beside a planned road with the road panel's Parallel settings, by side (-1 left, 1 right).</summary>
        private List<KeyValuePair<int, OmsiRoadPlan>> ParallelPlans(OmsiRoadPlan plan)
        {
            var result = new List<KeyValuePair<int, OmsiRoadPlan>>();
            if (roadPanel == null || !(roadPanel.ParallelDistance > 0.0) || plan.HasErrors || plan.Pieces.Count == 0)
                return result;
            foreach (var side in new[] { -1, 1 })
                if (side < 0 ? roadPanel.ParallelLeft : roadPanel.ParallelRight)
                    result.Add(new KeyValuePair<int, OmsiRoadPlan>(side, OmsiRoadPlanner.Offset(plan, side * roadPanel.ParallelDistance, roadPanel.ParallelOpposite,
                        roadPanel.HeightMode, roadTerrain, RoadProfileHalfWidth(), RoadProfileWidth())));
            return result;
        }

        /// <summary>
        /// Builds the parallel roads of a road just built (ADR 043). Each joins the parallel road of the part before on its
        /// side where they meet, at its start or, running the other way, at its end; that road is cut back like the road
        /// itself for a rounded corner. Adds the changes to the steps and returns what to add to the status.
        /// </summary>
        private string BuildParallels(OmsiRoadPlan plan, List<KeyValuePair<int, OmsiRoadPlan>> parallels, List<object> steps)
        {
            var lastEnds = new Dictionary<int, KeyValuePair<int, bool>>(parallelEnds);
            parallelEnds.Clear();
            parallelSplineIds.Clear();
            if (parallels.Count == 0)
                return string.Empty;
            var opposite = roadPanel.ParallelOpposite;
            var ends = new Dictionary<int, KeyValuePair<int, bool>>();
            var problems = new List<string>();
            foreach (var pair in parallels)
            {
                var parallel = pair.Value;
                if (parallel.HasErrors)
                {
                    problems.Add(parallel.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
                    continue;
                }
                var first = parallel.Pieces[0];
                var last = parallel.Pieces[parallel.Pieces.Count - 1];
                last.PointAt(last.LengthMeters, out var endX, out var endY, out _);
                var joint = opposite ? new OmsiMapPoint(endX, endY) : new OmsiMapPoint(first.StartX, first.StartY);
                var known = lastEnds.TryGetValue(pair.Key, out var before);
                var join = known ? FindSplineEnd(before.Key, before.Value) : null;
                if (join != null && plan.StartTrimMeters > 0.0 && join.Spline.RadiusMeters == 0.0 && session.IsBuiltHere(join.Spline) &&
                    join.Spline.LengthMeters - plan.StartTrimMeters >= OmsiRoadPlanner.MinimumLength)
                {
                    var cut = session.UpdateSplines(currentSummary,
                        new[] { OmsiSplineEditing.CutBack(join, plan.StartTrimMeters, opposite ? parallel.EndHeight : first.StartHeight) });
                    steps.Add(cut);
                    RefreshSplineViews(cut);
                    join = FindSplineEnd(before.Key, before.Value);
                }
                if (join != null && (!join.IsFree || Distance(join.Position(currentSummary.TileSizeMeters), joint) > 0.5))
                    join = null;
                var change = session.CreateRoad(currentSummary, parallel, roadPanel.Profile, opposite ? null : join, opposite ? join : null, FitTerrainForRoad());
                steps.Add(change);
                RefreshSplineViews(change);
                parallelSplineIds.UnionWith(change.SplineIds);
                ends[pair.Key] = opposite
                    ? new KeyValuePair<int, bool>(change.SplineIds[0], false)
                    : new KeyValuePair<int, bool>(change.SplineIds[change.SplineIds.Count - 1], true);
            }
            foreach (var end in ends)
                parallelEnds[end.Key] = end.Value;
            return (ends.Count > 0 ? Loc.T("roadbuild.note.parallel_beside", ("count", ends.Count), ("distance", roadPanel.ParallelDistance)) : string.Empty) +
                   (problems.Count > 0 ? Loc.T("roadbuild.note.parallel_where", ("problems", string.Join("; ", problems))) : string.Empty);
        }

        /// <summary>A spline end found again by the spline's ID, or null when the spline went.</summary>
        private OmsiSplineEnd FindSplineEnd(int id, bool atEnd)
        {
            var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == id));
            var spline = tile?.Splines.Find(candidate => candidate.Id == id);
            return spline != null ? new OmsiSplineEnd(tile, spline, atEnd) : null;
        }

        /// <summary>Where the road being drawn goes on from, to restore it.</summary>
        private sealed class RoadStartState
        {
            public int? JoinId;
            public bool JoinAtEnd;
            public OmsiJunctionPort Port;
            public OmsiMapPoint Start;
            public double? Heading;
            public double? Height;
        }

        private RoadStartState SaveRoadStart()
        {
            return new RoadStartState
            {
                JoinId = roadStartJoin?.Spline.Id, JoinAtEnd = roadStartJoin?.AtEnd ?? false, Port = roadStartPort, Start = roadStart,
                Heading = roadStartHeading, Height = roadStartHeight
            };
        }

        private void RestoreRoadStart(RoadStartState state)
        {
            roadStartJoin = null;
            if (state.JoinId.HasValue)
            {
                var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == state.JoinId.Value));
                var spline = tile?.Splines.Find(candidate => candidate.Id == state.JoinId.Value);
                if (spline != null)
                    roadStartJoin = new OmsiSplineEnd(tile, spline, state.JoinAtEnd);
            }
            roadStartPort = state.Port;
            roadStart = state.Start;
            roadStartHeading = state.Heading;
            roadStartHeight = state.Height;
            roadControl = null;
            UpdateRoadPreview();
        }

        private bool SplineExists(int id)
        {
            return currentSummary.Tiles.Exists(tile => tile.Splines.Exists(spline => spline.Id == id));
        }

        /// <summary>
        /// Builds the routes and the AI paths again 300 ms after the last edit, those that were read; edits in between push
        /// the build back. A map without a timetable has no routes, but its paths still follow.
        /// </summary>
        private void ScheduleRoutesRebuild()
        {
            if (mapCanvas == null)
                return;
            var rebuildRoutes = routes != null || routesTask != null;
            var rebuildPaths = mapPaths != null || pathsTask != null;
            if (!rebuildRoutes && !rebuildPaths)
                return;
            routesRebuild?.Pause();
            routesRebuild = mapCanvas.schedule.Execute(() =>
            {
                routesRebuild = null;
                if (rebuildRoutes && currentSummary != null && chronoDate == null)
                    StartRoutesBuild(currentSummary);
                if (rebuildPaths && currentSummary != null)
                    StartPathsBuild(currentSummary, false);
            }).StartingIn(300);
        }

        /// <summary>Shows or hides the stops and line routes in the view from above.</summary>
        private void ShowRoutes(bool visible)
        {
            if (mapCanvas == null)
                return;
            visible &= routes != null;
            mapCanvas.ShowRoutes = visible;
            routesToggleButton?.EnableInClassList("view-control-active", visible);
            if (visible && routes != null)
                SetStatusT("routes.status.shown", ("stops", routes.Stops.Count), ("lines", routes.Lines.Count), ("missing", routes.MissingEntryCount));
        }

        /// <summary>Highlights a line on the map and in the line list; the highlighted line again shows all alike.</summary>
        private void HighlightLine(string name)
        {
            highlightedLine = name == highlightedLine ? null : name;
            mapCanvas?.HighlightLine(highlightedLine);
            foreach (var pair in lineEntries)
                pair.Value.EnableInClassList("browser-tile-entry-selected", pair.Key == highlightedLine);
            if (highlightedLine != null)
                ShowRoutes(true);
        }

        /// <summary>Reads the map again through the same session, keeping the view.</summary>
        private void ReloadMap()
        {
            LoadMap(session.GlobalConfigPath, currentProjectName, session.ProjectFilePath, true);
        }

        private void SelectObject(OmsiTileSummary tile, OmsiObjectRecord item, bool centerView)
        {
            if (tile == null || item == null)
                return;
            if (CompleteVarParentPick(item))
                return;

            selection.Clear();
            selection.Add(new MapSelectionItem { Tile = tile, Record = item });
            ApplySelection(tile);
            if (centerView)
                mapCanvas?.CenterOn(tile, item);
            SetStatusT("selection.status.object", ("id", item.Id), ("path", item.AssetPath), ("mode", CanEditObjects ? "edit" : chronoDate != null ? "chrono" : "readonly"));
        }

        /// <summary>Adds an object to the selection or takes it out (Ctrl+click).</summary>
        private void ToggleObject(OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (tile == null || item == null)
                return;
            var index = selection.FindIndex(entry => ReferenceEquals(entry.Record, item));
            if (index >= 0)
                selection.RemoveAt(index);
            else
                selection.Add(new MapSelectionItem { Tile = tile, Record = item });
            ApplySelection(selection.Count > 0 ? selection[selection.Count - 1].Tile : tile);
            SetStatus(DescribeSelection());
        }

        /// <summary>Adds the objects of a Ctrl+drag box to the selection.</summary>
        private void AddToSelection(List<MapSelectionItem> items)
        {
            var known = new HashSet<OmsiObjectRecord>();
            foreach (var entry in selection)
                known.Add(entry.Record);
            foreach (var item in items)
                if (known.Add(item.Record))
                    selection.Add(item);
            if (selection.Count == 0)
            {
                SetStatusT("selection.status.none_in_box");
                return;
            }
            ApplySelection(selection[selection.Count - 1].Tile);
            SetStatus(DescribeSelection());
        }

        private void SelectTile(OmsiTileSummary tile, bool centerView)
        {
            if (tile == null || currentSummary == null)
                return;
            EndVarParentPick(true);

            selection.Clear();
            ClearSelectedSpline();
            ApplySelection(tile);
            if (centerView)
                mapCanvas?.CenterOn(tile, null);
            SetStatusT("selection.status.tile", ("x", tile.X), ("y", tile.Y));
        }

        /// <summary>Makes the last selected object the primary one, highlights the tile and refreshes the views.</summary>
        private void ApplySelection(OmsiTileSummary tile)
        {
            // A menu open for the selection before would act on it. A right-click selects before its menu opens, so its own menu stays.
            CloseContextMenu();
            var previousTile = selectedTile;
            selectedTile = tile;
            selectedObject = selection.Count > 0 ? selection[selection.Count - 1].Record : null;
            if (selection.Count > 0)
                ClearSelectedSpline();
            if (!ReferenceEquals(previousTile, tile))
            {
                SetTileHighlight(previousTile, false);
                SetTileHighlight(tile, true);
                if (tile != null)
                    ScrollBrowserTo(tile);
            }
            mapCanvas?.SetSelection(tile, selection);
            if (selectionInfoLabel != null)
                selectionInfoLabel.text = selection.Count == 0 ? string.Empty : Loc.T("selection.count", ("count", selection.Count));
            UpdateToolbar();
            RenderInspector();
            UpdateArmHandles();
        }

        private string DescribeSelection()
        {
            if (selection.Count == 0)
                return Loc.T("selection.cleared");
            if (selection.Count == 1)
                return Loc.T("selection.describe.one", ("id", selectedObject.Id), ("path", selectedObject.AssetPath));
            return Loc.T("selection.describe.many", ("count", selection.Count));
        }

        private bool IsSelected(OmsiObjectRecord item)
        {
            return selection.Exists(entry => ReferenceEquals(entry.Record, item));
        }

        /// <summary>
        /// Saves moved or rotated objects into the workspace as one undo step. An object that ends up in another tile
        /// moves into that tile, unless references tie it to its own; then it stays there with outside coordinates. Moves and turns
        /// by values are saved together, all or none, as one write of each file they change (<see cref="OmsiMapSession.UpdateObjects"/>);
        /// a move into another tile or a copy in a chrono variant first saves those before it, so the files change in the order of the edits.
        /// </summary>
        private void CommitEdits(List<MapObjectEdit> edits)
        {
            if (edits == null || edits.Count == 0)
                return;
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.edit_it"));
                return;
            }

            var parts = new List<ObjectEditStep>();
            var pending = new ValueEditQueue();
            var kept = new List<string>();
            var transfers = new List<string>();
            var copies = new Dictionary<OmsiObjectRecord, OmsiObjectRecord>();
            var stale = new List<OmsiReference>();
            OmsiReferenceIndex references = null;
            string failure = null;
            foreach (var edit in edits)
            {
                try
                {
                    // A variant moves its own objects in their tile; others it replaces by a copy (ADR 042).
                    if (EditingVariant && !session.IsInEditedVariant(edit.Record))
                    {
                        if (SameValues(edit.Record, edit))
                            continue;
                        failure = SaveValueEdits(parts, pending);
                        if (failure != null)
                            break;
                        references = references ?? session.BuildReferenceIndex();
                        var copied = session.CopyInEditedVariant(currentSummary, edit.Tile, edit.Record, edit.LocalX, edit.LocalY, edit.Height,
                            edit.HeadingDegrees, edit.PitchDegrees, edit.BankDegrees, references);
                        if (copied.Succeeded)
                        {
                            parts.Add(new ObjectEditStep { Change = copied.Change });
                            copies[edit.Record] = copied.Change.Record;
                            stale.AddRange(copied.Change.StaleReferences);
                        }
                        else
                        {
                            kept.Add("#" + edit.Record.Id + " (" + DescribeReferences(copied.Blockers) + ")");
                        }
                        continue;
                    }
                    if (EditingVariant)
                    {
                        failure = QueueValueEdit(parts, pending, edit.Tile, edit.Record, edit.LocalX, edit.LocalY, edit);
                        if (failure != null)
                            break;
                        continue;
                    }
                    var target = OtherTileAt(edit.Tile, edit.LocalX, edit.LocalY, out var targetX, out var targetY);
                    if (target != null)
                    {
                        failure = SaveValueEdits(parts, pending);
                        if (failure != null)
                            break;
                        references = references ?? session.BuildReferenceIndex();
                        var result = session.TransferObject(currentSummary, edit.Tile, edit.Record, target, targetX, targetY, references);
                        if (result.Succeeded)
                        {
                            // The move took chrono selections and attached objects along; the next one reads them anew.
                            references = null;
                            parts.Add(new ObjectEditStep { Change = result.Change });
                            transfers.Add(Loc.T("objects.edit.transfer", ("id", edit.Record.Id), ("x", target.X), ("y", target.Y)));
                            // A transfer keeps height and orientation; an edit from the inspector may change them too.
                            failure = QueueValueEdit(parts, pending, target, edit.Record, edit.Record.LocalX, edit.Record.LocalY, edit);
                            if (failure != null)
                                break;
                            continue;
                        }
                        kept.Add("#" + edit.Record.Id + " (" + DescribeReferences(result.Blockers) + ")");
                    }
                    failure = QueueValueEdit(parts, pending, edit.Tile, edit.Record, edit.LocalX, edit.LocalY, edit);
                    if (failure != null)
                        break;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    failure = Loc.T("objects.edit.save_failed", ("id", edit.Record.Id), ("reason", exception.Message));
                    break;
                }
            }
            // The moves and turns queued before a failure are saved still, so the history and "saved before" hold all that was written.
            var last = SaveValueEdits(parts, pending);
            failure = failure ?? last;

            if (parts.Count > 0)
                PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts });
            // Copies stay selected in place of the objects they replace.
            foreach (var entry in selection)
                if (copies.TryGetValue(entry.Record, out var copy))
                    entry.Record = copy;
            RefreshAfterChange(null);

            if (failure != null)
            {
                SetStatus(failure + (parts.Count > 0 ? " " + Loc.T("objects.edit.saved_before") : string.Empty));
                return;
            }
            if (parts.Count == 0 && kept.Count > 0)
            {
                SetStatus(Loc.T("objects.edit.kept_in_variant", ("title", editedVariant.Title), ("list", JoinLimited(kept))));
                return;
            }
            if (parts.Count == 0)
            {
                SetStatusT(edits.Count == 1 ? "objects.edit.unchanged_one" : "objects.edit.unchanged_many", ("id", edits.Count == 1 ? edits[0].Record.Id : 0)) /* i18n:keys */;
                return;
            }
            var message = edits.Count == 1
                ? Loc.T("objects.edit.saved_one", ("id", edits[0].Record.Id), ("where", EditingVariant ? "variant" : "project"), ("title", EditingVariant ? editedVariant.Title : string.Empty))
                : Loc.T("objects.edit.saved_many", ("count", edits.Count), ("where", EditingVariant ? "variant" : "project"), ("title", EditingVariant ? editedVariant.Title : string.Empty));
            if (copies.Count > 0)
            {
                var named = new List<string>();
                foreach (var pair in copies)
                    named.Add("#" + pair.Key.Id + " by #" + pair.Value.Id);
                message += Loc.T("objects.edit.replaced", ("list", JoinLimited(named))) +
                           (stale.Count > 0 ? Loc.T("objects.edit.stale", ("refs", DescribeReferences(stale))) : string.Empty);
            }
            if (transfers.Count > 0)
                message += Loc.T("objects.edit.moved", ("list", JoinLimited(transfers)));
            if (kept.Count > 0)
                message += Loc.T(EditingVariant ? "objects.edit.not_changed" : "objects.edit.kept_in_tile", ("list", JoinLimited(kept))); // i18n:keys
            SetStatus(message + " " + Loc.T("objects.edit.undo"));
        }

        /// <summary>True when an edit leaves the object's place and orientation as written.</summary>
        private static bool SameValues(OmsiObjectRecord item, MapObjectEdit edit)
        {
            var after = new[] { edit.LocalX, edit.LocalY, edit.Height, edit.HeadingDegrees, edit.PitchDegrees, edit.BankDegrees };
            var before = Values(item);
            for (var index = 0; index < after.Length; index++)
                if (OmsiNumberFormat.Format(after[index]) != OmsiNumberFormat.Format(before[index]))
                    return false;
            return true;
        }

        /// <summary>A move or turn by values of <see cref="CommitEdits"/>, waiting to be saved with the others next to it.</summary>
        private sealed class PendingValueEdit
        {
            public OmsiTileSummary Tile;
            public OmsiObjectRecord Record;
            public double X;
            public double Y;
            public MapObjectEdit Edit;
        }

        /// <summary>The moves and turns by values waiting to be saved, in their order, and the objects they move.</summary>
        private sealed class ValueEditQueue
        {
            public readonly List<PendingValueEdit> Edits = new List<PendingValueEdit>();
            public readonly HashSet<OmsiObjectRecord> Records = new HashSet<OmsiObjectRecord>();
        }

        /// <summary>
        /// Queues the edit's height and orientation at a tile-local position, to be saved by <see cref="SaveValueEdits"/>. A second edit of
        /// an object already queued saves the queue first, so each part of the history holds one object's values before and after it.
        /// Returns the status of a failed save, or null.
        /// </summary>
        private string QueueValueEdit(List<ObjectEditStep> parts, ValueEditQueue pending, OmsiTileSummary tile, OmsiObjectRecord item, double x, double y,
            MapObjectEdit edit)
        {
            string failure = null;
            if (pending.Records.Contains(item))
                failure = SaveValueEdits(parts, pending);
            if (failure == null)
            {
                pending.Edits.Add(new PendingValueEdit { Tile = tile, Record = item, X = x, Y = y, Edit = edit });
                pending.Records.Add(item);
            }
            return failure;
        }

        /// <summary>
        /// Saves the queued moves and turns by values all or none and records one part of the history for each object they changed. A
        /// failure saves none of them and returns its status; null when they were saved. The queue is empty afterwards.
        /// </summary>
        private string SaveValueEdits(List<ObjectEditStep> parts, ValueEditQueue pending)
        {
            if (pending.Edits.Count == 0)
                return null;
            var queued = new List<PendingValueEdit>(pending.Edits);
            pending.Edits.Clear();
            pending.Records.Clear();
            var edits = new List<OmsiObjectValueEdit>(queued.Count);
            var before = new List<double[]>(queued.Count);
            foreach (var item in queued)
            {
                before.Add(Values(item.Record));
                edits.Add(new OmsiObjectValueEdit(item.Tile, item.Record, item.X, item.Y, item.Edit.Height, item.Edit.HeadingDegrees, item.Edit.PitchDegrees,
                    item.Edit.BankDegrees));
            }
            IReadOnlyList<bool> changed;
            try
            {
                changed = session.UpdateObjects(edits);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return queued.Count == 1
                    ? Loc.T("objects.edit.save_failed", ("id", queued[0].Record.Id), ("reason", exception.Message))
                    : Loc.T("objects.edit.save_failed_many", ("count", queued.Count), ("reason", exception.Message));
            }
            for (var index = 0; index < queued.Count; index++)
            {
                if (!changed[index])
                    continue;
                var item = queued[index];
                parts.Add(new ObjectEditStep { TileX = item.Tile.X, TileY = item.Tile.Y, ObjectId = item.Record.Id, Before = before[index], After = Values(item.Record) });
            }
            return null;
        }

        /// <summary>
        /// The other tile that contains a tile-local position, with the position in that tile; null when the position
        /// lies in its own tile or where the map has no tile.
        /// </summary>
        private OmsiTileSummary OtherTileAt(OmsiTileSummary tile, double x, double y, out double targetX, out double targetY)
        {
            var size = currentSummary.TileSizeMeters;
            targetX = x;
            targetY = y;
            if (x >= 0 && x < size && y >= 0 && y < size)
                return null;
            var tileX = tile.X + (int)Math.Floor(x / size);
            var tileY = tile.Y + (int)Math.Floor(y / size);
            var target = currentSummary.Tiles.Find(candidate => candidate.X == tileX && candidate.Y == tileY && candidate.IsAvailable);
            if (target == null || ReferenceEquals(target, tile))
                return null;
            targetX = x + (tile.X - tileX) * size;
            targetY = y + (tile.Y - tileY) * size;
            return target;
        }

        private static string JoinLimited(List<string> parts)
        {
            var shown = parts.GetRange(0, Math.Min(3, parts.Count));
            return parts.Count > shown.Count ? Loc.T("objects.join_more", ("list", string.Join(", ", shown)), ("count", parts.Count - shown.Count)) : string.Join(", ", shown);
        }

        private static double[] Values(OmsiObjectRecord item)
        {
            return new[] { item.LocalX, item.LocalY, item.Height, item.HeadingDegrees, item.PitchDegrees, item.BankDegrees };
        }

        /// <summary>
        /// Brings the selection and the views up to date after objects changed, moved between tiles, appeared or
        /// disappeared. Objects that no longer exist leave the selection.
        /// </summary>
        /// <param name="select">The new selection, or null to keep the current one.</param>
        private void RefreshAfterChange(IEnumerable<MapSelectionItem> select)
        {
            if (select != null)
            {
                var items = new List<MapSelectionItem>(select);
                selection.Clear();
                selection.AddRange(items);
            }
            for (var index = selection.Count - 1; index >= 0; index--)
            {
                var tile = TileHolding(selection[index]);
                if (tile == null && selection[index].Tile != null)
                {
                    // A chrono variant's overlay puts a copy in place of an object whose rules, type or labels changed; the ID finds it.
                    var id = selection[index].Record.Id;
                    var current = selection[index].Tile.Objects.Find(candidate => candidate.Id == id);
                    if (current != null)
                    {
                        selection[index].Record = current;
                        tile = selection[index].Tile;
                    }
                }
                if (tile == null)
                    selection.RemoveAt(index);
                else
                    selection[index].Tile = tile;
            }
            ApplySelection(selection.Count > 0 ? selection[selection.Count - 1].Tile : selectedTile);
            RefitTerrainAtJunctions();
            mapCanvas?.Refresh();
            objectRenderer?.RefreshPlacements();
            if (projectTab == ProjectTab.Entries && projectPanel != null && currentSummary != null)
                RenderProjectBrowser();
            groundRenderer?.RefreshTerrainHoles(currentSummary);
            // Moved stops and crossings move their routes.
            ScheduleRoutesRebuild();
            foreach (var tile in browserEntries.Keys)
                UpdateBrowserEntry(tile);
            UpdateCounts();
            UpdateMapStateLabel();
            // Undo may have put another file of the junction back; the markings tool reads it anew.
            if (activeTool == EditorTool.Markings)
                UpdateMarkingPreview();
            // Undo may have put other rules back; the priority tool reads the arms anew.
            if (activeTool == EditorTool.Priorities)
                RefreshPriorityTarget();
            // Undo may have put another file of the junction back; the lanes tool reads it anew.
            if (activeTool == EditorTool.Lanes)
                UpdateLanePreview();
            // So does the signals tool.
            if (activeTool == EditorTool.Signals)
                RefreshSignalTarget();
        }

        /// <summary>Starts the terrain tool: its options in the tool panel, and the map's road mode for pointing and holding.</summary>
        private void BeginTerrainTool()
        {
            if (terrainOptions == null || mapCanvas == null || currentSummary == null)
                return;
            objectsPanel.style.display = DisplayStyle.None;
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            terrainOptions.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.BeginRoad();
            terrainRampStart = null;
            if (terrainOptions.Painting)
                RefreshPaintTextures();
            ShowTerrainBrush();
            SetStatusT("terrain.status.start");
        }

        private void EndTerrainTool()
        {
            ReleaseTerrain();
            mapCanvas?.EndRoad();
            mapCanvas?.ClearRoadPreview();
            if (terrainOptions != null)
                terrainOptions.style.display = DisplayStyle.None;
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            terrainRampStart = null;
        }

        /// <summary>Draws the brush as a circle under the pointer, and a ramp from where it starts; says the height there.</summary>
        private void ShowTerrainBrush()
        {
            if (mapCanvas == null || terrainOptions == null || activeTool != EditorTool.Terrain)
                return;
            if (!roadPointer.HasValue)
            {
                mapCanvas.ClearRoadPreview();
                return;
            }
            var pointer = roadPointer.Value;
            var radius = terrainOptions.Radius;
            var marks = new List<OmsiMapPoint[]> { Circle(pointer, radius) };
            var sampler = new OmsiTerrainSampler(currentSummary, session.Source);
            var here = sampler.TryGetHeight(pointer.X, pointer.Y, out var height) ? Loc.T("terrain.here_height", ("height", height)) : Loc.T("terrain.no_terrain");
            if (terrainOptions.Mode.HasValue || terrainOptions.Painting)
            {
                mapCanvas.SetRoadPreview(marks, true, null, null, null);
                terrainOptions.ShowPlan(Loc.T("terrain.here", ("here", here)), false);
                return;
            }
            if (!terrainRampStart.HasValue)
            {
                mapCanvas.SetRoadPreview(marks, true, null, null, null);
                terrainOptions.ShowPlan(Loc.T("terrain.ramp_start", ("here", here)), false);
                return;
            }
            var start = terrainRampStart.Value;
            marks.Add(Circle(start, radius));
            marks.Add(new[] { start, pointer });
            mapCanvas.SetRoadPreview(marks, true, null, null, null);
            var length = Math.Sqrt((pointer.X - start.X) * (pointer.X - start.X) + (pointer.Y - start.Y) * (pointer.Y - start.Y));
            var text = Loc.T("terrain.ramp_over", ("length", length));
            if (sampler.TryGetHeight(start.X, start.Y, out var from) && sampler.TryGetHeight(pointer.X, pointer.Y, out var to) && length > 0.01)
                text += Loc.T("terrain.ramp_gradient", ("gradient", (to - from) / length * 100.0));
            terrainOptions.ShowPlan(text + Loc.T("terrain.ramp_end"), false);
        }

        private static OmsiMapPoint[] Circle(OmsiMapPoint centre, double radius)
        {
            var points = new OmsiMapPoint[49];
            for (var index = 0; index < points.Length; index++)
            {
                var angle = index * 2.0 * Math.PI / (points.Length - 1);
                points[index] = new OmsiMapPoint(centre.X + Math.Cos(angle) * radius, centre.Y + Math.Sin(angle) * radius);
            }
            return points;
        }

        /// <summary>The left button pressed in the terrain tool: a stroke starts, or a ramp gets its start or its end.</summary>
        private void PressTerrain(double x, double y)
        {
            if (!CanEdit || currentSummary == null || terrainOptions == null)
                return;
            roadPointer = new OmsiMapPoint(x, y);
            if (terrainOptions.Painting)
            {
                PressPaint();
                return;
            }
            if (!terrainOptions.Mode.HasValue)
            {
                ClickRamp(x, y);
                return;
            }
            ReleaseTerrain();
            try
            {
                terrainStroke = session.BeginTerrainStroke(currentSummary);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("terrain.status.cannot_shape", ("reason", exception.Message));
                return;
            }
            terrainStroke.FlattenTo = terrainOptions.FlattenHeight ?? terrainStroke.HeightAt(x, y) ?? 0.0;
            // A lake's surface: the tile's water if it has some, else a little under where the stroke starts (openMapper).
            var size = currentSummary.TileSizeMeters;
            var under = currentSummary.Tiles.Find(tile => tile.IsAvailable && tile.X == (int)Math.Floor(x / size) && tile.Y == (int)Math.Floor(y / size));
            terrainStroke.WaterLevel = under?.Water != null ? (under.Water[0] + under.Water[1] + under.Water[2] + under.Water[3]) / 4.0
                : (terrainStroke.HeightAt(x, y) ?? 0.0) - 0.3;
            terrainStroke.WaterDepth = terrainOptions.WaterDepth;
            // The first step comes at once.
            terrainSince = TerrainStepSeconds;
        }

        /// <summary>While the button is held, the brush works where the pointer is, a step at a time.</summary>
        private void TickTerrain()
        {
            if (paintStroke != null)
            {
                TickPaint();
                return;
            }
            if (terrainStroke == null || !roadPointer.HasValue || activeTool != EditorTool.Terrain || !terrainOptions.Mode.HasValue)
                return;
            terrainSince += Time.unscaledDeltaTime;
            if (terrainSince < TerrainStepSeconds)
                return;
            var seconds = Math.Min(terrainSince, 0.25);
            terrainSince = 0.0;
            List<OmsiTileSummary> touched;
            try
            {
                touched = session.ApplyTerrainBrush(currentSummary, terrainStroke, terrainOptions.Mode.Value, roadPointer.Value.X, roadPointer.Value.Y,
                    terrainOptions.Radius, terrainOptions.Strength, seconds);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("terrain.status.shape_failed", ("reason", exception.Message));
                ReleaseTerrain();
                return;
            }
            if (touched.Count > 0)
                RefreshGround(touched);
        }

        /// <summary>The button let go: the stroke is kept as one step.</summary>
        private void ReleaseTerrain()
        {
            ReleasePaint();
            var stroke = terrainStroke;
            terrainStroke = null;
            if (stroke == null)
                return;
            OmsiTerrainChange change;
            try
            {
                change = session.EndTerrainStroke(currentSummary, stroke);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                // The ground the stroke drew is back in the files as before it; after a failed roll-back the map is read again anyway.
                if (!(exception is OmsiRollBackFailedException))
                    RefreshGround(stroke.WrittenTiles);
                SetStatusT("terrain.status.not_kept", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("terrain.step.shaping") });
            RefreshGround(change.Tiles);
            ShowTerrainBrush();
            SetStatusT("terrain.status.shaped", ("count", change.Tiles.Count));
        }

        /// <summary>A click of the ramp: its start, or its end, which lays it as one step.</summary>
        private void ClickRamp(double x, double y)
        {
            if (!terrainRampStart.HasValue)
            {
                terrainRampStart = new OmsiMapPoint(x, y);
                ShowTerrainBrush();
                SetStatusT("terrain.status.ramp_end");
                return;
            }
            var start = terrainRampStart.Value;
            terrainRampStart = null;
            OmsiTerrainChange change;
            double gradient;
            try
            {
                var stroke = session.BeginTerrainStroke(currentSummary);
                session.ApplyTerrainRamp(stroke, start.X, start.Y, x, y, terrainOptions.Radius, out gradient);
                change = session.EndTerrainStroke(currentSummary, stroke);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("terrain.status.ramp_failed", ("reason", exception.Message));
                return;
            }
            ShowTerrainBrush();
            if (change == null)
            {
                SetStatusT("terrain.status.no_ramp");
                return;
            }
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("terrain.step.ramp") });
            RefreshGround(change.Tiles);
            var length = Math.Sqrt((x - start.X) * (x - start.X) + (y - start.Y) * (y - start.Y));
            SetStatusT("terrain.status.ramp_laid", ("gradient", gradient), ("length", length));
        }

        /// <summary>
        /// Writes the ground as a height map (ADR 040, stage 3): a 16-bit grey PNG and a .txt beside it with its lowest and
        /// highest height, where the user chooses, or at the path given.
        /// </summary>
        private void ExportHeightMap(string path)
        {
            if (currentSummary == null || session == null)
                return;
            var name = string.IsNullOrWhiteSpace(currentSummary.FriendlyName) ? currentSummary.Name : currentSummary.FriendlyName;
            path = path ?? WindowsFileDialog.SaveFile(Loc.T("filedialog.export_heights"), WindowsFileDialog.PngFilter, "png", SanitizeFileName(name) + "_heights.png");
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var export = session.ExportHeightMap(currentSummary);
                File.WriteAllBytes(path, export.Png);
                File.WriteAllText(Path.ChangeExtension(path, ".txt"), export.Note);
                SetStatusT("heightmap.status.written", ("width", export.Width), ("height", export.Height), ("lowest", export.Range.Lowest), ("highest", export.Range.Highest), ("file", Path.GetFileName(Path.ChangeExtension(path, ".txt"))));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException ||
                                              exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("heightmap.status.write_failed", ("reason", exception.Message));
            }
        }

        /// <summary>Reads a picture to lay over the map and shows its settings: the heights from the .txt beside it, or 0 to 100 m.</summary>
        private void ChooseHeightMap(string path)
        {
            if (currentSummary == null || terrainOptions == null)
                return;
            path = path ?? WindowsFileDialog.OpenFile(Loc.T("filedialog.open_heights"), WindowsFileDialog.HeightMapFilter);
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var bytes = File.ReadAllBytes(path);
                OmsiHeightRange? fromPicture = null;
                pendingHeightMap = OmsiTiff.IsTiff(bytes) ? OmsiTiff.Decode(bytes, out fromPicture) : OmsiPng.DecodeGrey(bytes);
                var note = Path.ChangeExtension(path, ".txt");
                // The heights in the .txt beside it, as openMapper writes them; else a TIFF's own heights in metres; else 0 to 100 m.
                var range = (File.Exists(note) ? OmsiHeightMap.ReadRange(File.ReadAllText(note)) : null) ?? fromPicture ?? new OmsiHeightRange { Lowest = 0, Highest = 100 };
                var info = string.Empty;
                if (OmsiHeightMap.Extent(currentSummary, out var minX, out var minY, out var maxX, out var maxY))
                {
                    var width = (maxX - minX + 1) * currentSummary.TileSizeMeters;
                    var height = (maxY - minY + 1) * currentSummary.TileSizeMeters;
                    info = Loc.T("heightmap.info", ("width", pendingHeightMap.Width), ("height", pendingHeightMap.Height), ("meterWidth", width), ("meterHeight", height),
                        ("pixel", width / Math.Max(1, pendingHeightMap.Width - 1)));
                }
                terrainOptions.ShowImport(Path.GetFileName(path), info, range, pendingHeightMap.Coarse);
                SetStatusT("heightmap.status.choose", ("file", Path.GetFileName(path)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException ||
                                              exception is ArgumentException)
            {
                Debug.LogException(exception);
                pendingHeightMap = null;
                SetStatusT("pictures.unreadable", ("reason", exception.Message));
            }
        }

        /// <summary>Lays the chosen height map over all tiles with the settings shown, as one step.</summary>
        private void LayHeightMap()
        {
            if (pendingHeightMap == null || terrainOptions == null || !CanEdit)
                return;
            var range = terrainOptions.ImportRange;
            if (!range.HasValue)
            {
                SetStatusT("heightmap.status.numbers");
                return;
            }
            var picture = new OmsiGreyPicture(pendingHeightMap.Width, pendingHeightMap.Height, (float[])pendingHeightMap.Values.Clone(), pendingHeightMap.Coarse);
            picture.Smooth(terrainOptions.ImportSmoothing);
            OmsiTerrainChange change;
            try
            {
                change = session.ImportHeightMap(currentSummary, picture, range.Value, terrainOptions.ImportAdds);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("heightmap.status.lay_failed", ("reason", exception.Message));
                return;
            }
            pendingHeightMap = null;
            terrainOptions.HideImport();
            if (change == null)
            {
                SetStatusT("heightmap.status.nothing");
                return;
            }
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("heightmap.step") });
            RefreshGround(change.Tiles);
            SetStatusT("heightmap.status.laid", ("count", change.Tiles.Count));
        }

        /// <summary>
        /// The tile's water ([water], ADR 040 stage 4): its height, and buttons to lay it, raise or lower it to the height
        /// typed, or take it away. A new water starts 1 m under the tile's lowest ground.
        /// </summary>
        private void AddWaterEditor(VisualElement container, OmsiTileSummary tile)
        {
            if (tile == null || !tile.IsAvailable || !tile.HasTerrain)
                return;
            var heading = new Label(Loc.T("water.heading").ToUpper(Loc.Manager?.Culture ?? System.Globalization.CultureInfo.CurrentCulture));
            heading.AddToClassList("inspector-section-heading");
            container.Add(heading);
            // Without water yet: the surface of the water that was levelled in this tile, if any, else a metre under the lowest ground.
            var suggested = tile.Water == null ? geoSettings?.SuggestedWaterOf(tile.X, tile.Y) : null;
            var level = tile.Water != null ? (tile.Water[0] + tile.Water[1] + tile.Water[2] + tile.Water[3]) / 4.0 : suggested ?? tile.TerrainMinHeight - 1.0;
            var field = AddEditField(container, "inspector.field.height", level);
            if (suggested.HasValue)
            {
                var hint = new Label(Loc.T("water.suggested"));
                hint.AddToClassList("inspector-record-empty");
                hint.style.whiteSpace = WhiteSpace.Normal;
                container.Add(hint);
            }
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var set = new Button(() =>
            {
                if (double.TryParse(field.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                    SetTileWater(tile, height);
                else
                    SetStatusT("water.status.height_number");
            })
            {
                text = Loc.T(tile.Water != null ? "water.set_height" : "water.add") /* i18n:keys */,
                tooltip = Loc.T("water.set.tooltip")};
            set.AddToClassList("map-control-button");
            set.AddToClassList("inspector-edit-apply");
            row.Add(set);
            if (tile.Water != null)
            {
                var remove = new Button(() => SetTileWater(tile, null)) { text = Loc.T("water.remove"), tooltip = Loc.T("water.remove.tooltip")};
                remove.AddToClassList("map-control-button");
                remove.AddToClassList("inspector-edit-delete");
                row.Add(remove);
            }
            container.Add(row);
            if (tile.Water == null)
                return;

            // Each corner its own height, as tile.map.water keeps them: water that slopes like a river.
            var corners = new[]
            {
                AddEditField(container, "inspector.field.sw", tile.Water[0]), AddEditField(container, "inspector.field.se", tile.Water[1]),
                AddEditField(container, "inspector.field.nw", tile.Water[2]), AddEditField(container, "inspector.field.ne", tile.Water[3])
            };
            var setCorners = new Button(() =>
            {
                var heights = new float[4];
                for (var corner = 0; corner < 4; corner++)
                    if (double.TryParse(corners[corner].value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                        heights[corner] = (float)height;
                    else
                    {
                        SetStatusT("water.status.corners_numbers");
                        return;
                    }
                SetTileWaterCorners(tile, heights);
            })
            {
                text = Loc.T("water.set_corners"),
                tooltip = Loc.T("water.set_corners.tooltip")};
            setCorners.AddToClassList("map-control-button");
            setCorners.AddToClassList("inspector-edit-apply");
            container.Add(setCorners);
        }

        private void SetTileWaterCorners(OmsiTileSummary tile, float[] corners)
        {
            if (!CanEdit)
                return;
            OmsiTerrainChange change;
            try
            {
                change = session.SetTileWaterCorners(currentSummary, tile, corners);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("water.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("water.step", ("x", tile.X), ("y", tile.Y)) });
            RefreshGround(change.Tiles);
            RenderInspector();
            SetStatusT("water.status.slope", ("low", Math.Min(Math.Min(corners[0], corners[1]), Math.Min(corners[2], corners[3]))), ("high", Math.Max(Math.Max(corners[0], corners[1]), Math.Max(corners[2], corners[3]))), ("x", tile.X), ("y", tile.Y));
        }

        private void SetTileWater(OmsiTileSummary tile, double? level)
        {
            if (!CanEdit)
                return;
            OmsiTerrainChange change;
            try
            {
                change = session.SetTileWater(currentSummary, tile, level);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("water.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("water.step", ("x", tile.X), ("y", tile.Y)) });
            RefreshGround(change.Tiles);
            RenderInspector();
            SetStatusT(level.HasValue ? "water.status.level" : "water.status.removed", ("level", level ?? 0.0), ("x", tile.X), ("y", tile.Y)) /* i18n:keys */;
        }

        /// <summary>Levels the ground under the selected objects (ADR 040, stage 2), as one step.</summary>
        private void LevelSelectedGround()
        {
            if (!CanEdit || currentSummary == null || selection.Count == 0)
                return;
            OmsiTerrainChange change;
            int levelled;
            try
            {
                change = session.LevelGroundUnder(currentSummary,
                    selection.ConvertAll(item => new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(TileHolding(item) ?? item.Tile, item.Record)), out levelled);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("level.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
            {
                SetStatusT(levelled == 0 ? "level.status.nothing" : "level.status.level") /* i18n:keys */;
                return;
            }
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("level.step", ("count", levelled)) });
            RefreshGround(change.Tiles);
            SetStatusT("level.status.done", ("count", levelled));
        }

        /// <summary>Draws the ground and the objects standing on it again where the terrain changed.</summary>
        private void RefreshGround(IEnumerable<OmsiTileSummary> tiles)
        {
            var list = new List<OmsiTileSummary>(tiles);
            if (list.Count == 0)
                return;
            groundRenderer?.RefreshTerrain(list);
            groundRenderer?.RefreshWater(currentSummary);
            objectRenderer?.RefreshTerrain(list, session.Source);
            if (roadTerrain != null)
                roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
            UpdateMapStateLabel();
            // The inspector tells the tile's range of heights and proposes a water height from it; it keeps where it was scrolled to.
            RenderInspector();
        }

        /// <summary>Starts the row tool: the object library with the row options beside it, and the map's road mode for pointing.</summary>
        private void BeginRowTool()
        {
            if (objectsPanel == null || mapCanvas == null || currentSummary == null)
                return;
            UpdateObjectsCatalog();
            StartInstallationScan();
            objectsPanel.UseOptions(rowOptions, Loc.T("rows.panel_hint"));
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            objectsPanel.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            mapCanvas.BeginRoad();
            rowLayout = null;
            rowObjects = null;
            rowStart = null;
            DeselectRow();
            rowOptions.ShowObject(rowAsset);
            rowOptions.ShowPlan(Loc.T(rowAsset == null ? "rows.plan.choose" : "rows.plan.point"), false); // i18n:keys
            SetStatusT("rows.status.start");
        }

        private void EndRowTool()
        {
            mapCanvas?.EndRoad();
            objectRenderer?.SetRowPreview(null, null);
            rowLaidAt = null;
            objectsPanel?.UseOptions(null, null);
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            rowLayout = null;
            rowObjects = null;
            rowStart = null;
            DeselectRow();
        }

        private void ChooseRowObject(string assetPath)
        {
            // With a row selected, the chosen object takes the place of its objects.
            if (selectedRow != null)
            {
                ChangeSelectedRow(assetPath);
                return;
            }
            rowAsset = assetPath;
            rowOptions?.ShowObject(assetPath);
            UpdateRowPreview();
        }

        /// <summary>
        /// While pointing in the row tool: the row a click would lay, or the existing row under the pointer, drawn as marks
        /// where its objects stand.
        /// </summary>
        private void UpdateRowPreview()
        {
            if (mapCanvas == null || currentSummary == null || rowOptions == null || activeTool != EditorTool.Rows)
                return;
            objectRenderer?.SetRowPreview(null, null);
            if (rowLaidAt.HasValue)
            {
                mapCanvas.ClearRoadPreview();
                rowOptions.ShowPlan(Loc.T("rows.plan.laid"), false);
                return;
            }
            if (selectedRow != null)
            {
                // The row as it would be with the values in the fields, until Enter changes it.
                var request = EditRequest(null);
                var name = Path.GetFileNameWithoutExtension(selectedRow.Request.AssetPath);
                if (OmsiAttachmentRows.Same(request, selectedRow.Request))
                {
                    ShowRowMarks(Placed(selectedRow), true);
                    rowOptions.ShowPlan(Loc.T("rows.plan.selected", ("name", name), ("objects", selectedRow.ObjectCount), ("records", selectedRow.Records.Count),
                        ("continues", selectedRow.Continues ? "yes" : "no")), false);
                    return;
                }
                rowLayout = rowLayout ?? new OmsiSplineAttachmentLayout(currentSummary);
                var plan = OmsiAttachmentRows.Replan(currentSummary, rowLayout, selectedRow, request);
                ShowRowMarks(plan.Objects, plan.Problem == null);
                if (plan.Problem == null)
                    objectRenderer?.SetRowPreview(request.AssetPath, plan.Objects);
                rowOptions.ShowPlan(plan.Problem ?? Loc.T("rows.plan.replan", ("count", plan.Objects.Count), ("old", selectedRow.ObjectCount)), plan.Problem != null);
                return;
            }
            if (!roadPointer.HasValue)
                return;
            var pointer = roadPointer.Value;
            var hovered = rowStart == null ? RowAt(pointer) : null;
            if (hovered.HasValue)
            {
                if (hoveredRow == null || !hoveredRow.Records.Exists(item => ReferenceEquals(item.Value, hovered.Value.Value)))
                    hoveredRow = RowOf(hovered.Value);
                var objects = hoveredRow != null ? Placed(hoveredRow) : Placed(hovered.Value);
                ShowRowMarks(objects, true);
                rowOptions.ShowPlan(Loc.T("rows.plan.hover", ("name", Path.GetFileNameWithoutExtension(hovered.Value.Value.AssetPath)), ("count", objects.Count)), false);
                return;
            }
            if (rowAsset == null)
            {
                mapCanvas.ClearRoadPreview();
                rowOptions.ShowPlan(Loc.T("rows.plan.choose"), true);
                return;
            }
            var plans = PlanRows(pointer, out var problem);
            if (plans == null)
            {
                mapCanvas.ClearRoadPreview();
                rowOptions.ShowPlan(problem, problem != null && problem != Loc.T("rows.plan.point"));
                return;
            }
            var marks = new List<OmsiPlacedAttachment>();
            var parts = 0;
            string failure = null;
            foreach (var plan in plans)
            {
                marks.AddRange(plan.Objects);
                parts += plan.Parts.Count;
                failure = failure ?? plan.Problem;
            }
            ShowRowMarks(marks, failure == null);
            if (failure == null)
                objectRenderer?.SetRowPreview(rowAsset, marks);
            rowOptions.ShowPlan(failure ?? Loc.T("rows.plan.marks", ("count", marks.Count), ("records", parts), ("phase", rowStart != null ? "end" : rowOptions.WholeRoad ? "lay" : "start")), failure != null);
        }

        /// <summary>
        /// The rows a click at the pointer would lay: on the side of the road the pointer is on, or on both sides; along the
        /// whole road, or from the first click to the pointer. Null with the reason when the pointer is on no road.
        /// </summary>
        private List<OmsiAttachmentRowPlan> PlanRows(OmsiMapPoint pointer, out string problem)
        {
            problem = null;
            rowLayout = rowLayout ?? new OmsiSplineAttachmentLayout(currentSummary);
            var reach = Math.Max(RowSnapMeters, RowSnapPixels * mapCanvas.MetersPerPixel);
            var at = rowStart ?? OmsiSplinePoint.Nearest(currentSummary, pointer.X, pointer.Y, reach);
            if (at == null)
            {
                problem = Loc.T("rows.plan.point");
                return null;
            }
            // The side of the road the pointer is on, seen along the spline.
            var size = currentSummary.TileSizeMeters;
            OmsiMapGeometry.GetLocalFrameAt(at.Spline, at.Distance, out var local, out var radians);
            var point = OmsiMapGeometry.TileLocalToWorld(at.Tile.X, at.Tile.Y, local.X, local.Y, size);
            var right = (pointer.X - point.X) * Math.Cos(radians) - (pointer.Y - point.Y) * Math.Sin(radians) >= 0.0;

            double? from = null, to = null;
            if (!rowOptions.WholeRoad)
            {
                var chain = rowLayout.Chain(at.Tile, at.Spline);
                var along = OmsiAttachmentRows.Project(rowLayout, chain, pointer.X, pointer.Y);
                from = rowStart != null ? rowStartAlong : along;
                to = along;
            }
            var plans = new List<OmsiAttachmentRowPlan>();
            foreach (var onRight in rowOptions.BothSides ? new[] { true, false } : new[] { right })
            {
                var distance = rowOptions.Offset ?? EdgeOffset(at.Spline, onRight);
                var request = new OmsiAttachmentRowRequest
                {
                    AssetPath = rowAsset, LateralOffset = onRight ? distance : -distance, Height = rowOptions.Height,
                    HeadingDegrees = rowOptions.HeadingDegrees + (onRight ? 0.0 : 180.0), Interval = rowOptions.Interval, Tilt = rowOptions.Tilt
                };
                plans.Add(OmsiAttachmentRows.Plan(currentSummary, rowLayout, at.Tile, at.Spline, request, from, to));
            }
            return plans;
        }

        /// <summary>A click in the row tool: selects a row under the pointer, starts a row, or lays it.</summary>
        private void ClickRow(double x, double y)
        {
            if (currentSummary == null || !CanEdit)
                return;
            var pointer = new OmsiMapPoint(x, y);
            roadPointer = pointer;
            if (rowStart == null)
            {
                var hovered = RowAt(pointer);
                if (hovered.HasValue)
                {
                    SelectRow(hovered.Value);
                    UpdateRowPreview();
                    if (selectedRow != null)
                        SetStatusT("rows.status.selected", ("path", hovered.Value.Value.AssetPath), ("id", hovered.Value.Value.Id));
                    return;
                }
            }
            // A click beside the selected row only lets it go.
            if (selectedRow != null)
            {
                DeselectRow();
                UpdateRowPreview();
                SetStatusT("rows.status.idle");
                return;
            }
            if (rowAsset == null)
            {
                SetStatusT("rows.status.choose_first");
                return;
            }
            if (!rowOptions.WholeRoad && rowStart == null)
            {
                rowLayout = rowLayout ?? new OmsiSplineAttachmentLayout(currentSummary);
                var at = OmsiSplinePoint.Nearest(currentSummary, x, y, Math.Max(RowSnapMeters, RowSnapPixels * mapCanvas.MetersPerPixel));
                if (at == null)
                {
                    SetStatusT("rows.status.point");
                    return;
                }
                rowStart = at;
                rowStartAlong = OmsiAttachmentRows.Project(rowLayout, rowLayout.Chain(at.Tile, at.Spline), x, y);
                UpdateRowPreview();
                SetStatusT("rows.status.end");
                return;
            }
            var plans = PlanRows(pointer, out var problem);
            if (plans == null)
            {
                SetStatusT("rows.status.problem", ("problem", problem));
                return;
            }
            var failed = plans.Find(plan => plan.Problem != null);
            if (failed != null)
            {
                SetStatusT("rows.status.not_laid", ("reason", failed.Problem));
                return;
            }
            var changes = new List<OmsiAttachmentChange>();
            try
            {
                foreach (var plan in plans)
                    changes.Add(session.AddAttachmentRow(currentSummary, plan));
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                for (var index = changes.Count - 1; index >= 0; index--)
                    session.Undo(currentSummary, changes[index]);
                SetStatusT("rows.status.lay_failed", ("reason", exception.Message));
                return;
            }
            rowStart = null;
            rowLayout = null;
            rowObjects = null;
            rowLaidAt = pointer;
            var parts = new List<ObjectEditStep>();
            var objects = 0;
            foreach (var change in changes)
                parts.Add(new ObjectEditStep { AttachmentChange = change });
            foreach (var plan in plans)
                objects += plan.Objects.Count;
            var name = Path.GetFileNameWithoutExtension(rowAsset);
            PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts, Description = Loc.T("rows.step.laid", ("name", name)) });
            RefreshAfterChange(null);
            UpdateRowPreview();
            SetStatusT("rows.status.laid", ("objects", objects), ("name", name), ("both", plans.Count > 1 ? "yes" : "no"));
        }

        /// <summary>Deletes the selected row, all its records in one step.</summary>
        private void DeleteSelectedRow()
        {
            if (selectedRow == null || !CanEdit)
                return;
            var row = selectedRow;
            var changes = new List<OmsiAttachmentChange>();
            string failure = null;
            try
            {
                foreach (var record in row.Records)
                {
                    var result = session.DeleteAttachmentRow(currentSummary, record.Key, record.Value);
                    if (!result.Succeeded)
                    {
                        failure = Loc.T("rows.status.stays_refs", ("refs", DescribeReferences(result.Blockers)));
                        break;
                    }
                    changes.Add(result.Change);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                failure = Loc.T("rows.status.delete_failed", ("reason", exception.Message));
            }
            if (failure != null)
            {
                for (var index = changes.Count - 1; index >= 0; index--)
                    session.Undo(currentSummary, changes[index]);
                SetStatus(failure);
                return;
            }
            DeselectRow();
            rowLayout = null;
            rowObjects = null;
            var parts = changes.ConvertAll(change => new ObjectEditStep { AttachmentChange = change });
            PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts, Description = Loc.T("rows.step.deleted", ("name", Path.GetFileNameWithoutExtension(row.Request.AssetPath))) });
            RefreshAfterChange(null);
            UpdateRowPreview();
            SetStatusT("rows.status.deleted", ("path", row.Request.AssetPath));
        }

        /// <summary>Selects the row a record belongs to and shows its values in the options.</summary>
        private void SelectRow(KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord> record)
        {
            selectedRow = RowOf(record);
            if (selectedRow == null)
            {
                DeselectRow();
                SetStatusT("rows.status.no_objects", ("path", record.Value.AssetPath), ("id", record.Value.Id));
                return;
            }
            // Values as when laying: the distance from the centre, the heading as on the right of the road.
            var request = selectedRow.Request;
            var left = request.LateralOffset < 0.0;
            rowOptions.BeginEditing(request.Interval, Math.Abs(request.LateralOffset), request.Height,
                ((request.HeadingDegrees - (left ? 180.0 : 0.0)) % 360.0 + 360.0) % 360.0, request.Tilt);
            rowOptions.ShowObject(request.AssetPath);
        }

        private void DeselectRow()
        {
            selectedRow = null;
            rowOptions?.EndEditing();
            rowOptions?.ShowObject(rowAsset);
        }

        /// <summary>The values in the options for the selected row, on its side of the road, with another object or its own.</summary>
        private OmsiAttachmentRowRequest EditRequest(string assetPath)
        {
            var row = selectedRow;
            var left = row.Request.LateralOffset < 0.0;
            var distance = rowOptions.Offset ?? EdgeOffset(row.Spline, !left);
            return new OmsiAttachmentRowRequest
            {
                AssetPath = assetPath ?? row.Request.AssetPath, LateralOffset = left ? -distance : distance, Height = rowOptions.Height,
                HeadingDegrees = ((rowOptions.HeadingDegrees + (left ? 180.0 : 0.0)) % 360.0 + 360.0) % 360.0, Interval = rowOptions.Interval,
                Tilt = rowOptions.Tilt
            };
        }

        /// <summary>Changes the selected row to the values in the options, as one step; it stays selected.</summary>
        private void ChangeSelectedRow(string assetPath)
        {
            if (selectedRow == null || !CanEdit)
                return;
            var request = EditRequest(assetPath);
            if (OmsiAttachmentRows.Same(request, selectedRow.Request))
            {
                UpdateRowPreview();
                return;
            }
            OmsiAttachmentEditResult result;
            try
            {
                result = session.ChangeAttachmentRow(currentSummary, selectedRow, request);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("rows.status.change_failed", ("reason", exception.Message));
                return;
            }
            if (!result.Succeeded)
            {
                SetStatus(result.Problem != null ? Loc.T("rows.status.stays_problem", ("reason", result.Problem)) : Loc.T("rows.status.stays_refs", ("refs", DescribeReferences(result.Blockers))));
                UpdateRowPreview();
                return;
            }
            var id = selectedRow.Records[0].Value.Id;
            rowLayout = null;
            rowObjects = null;
            PushHistory(new ObjectEditStep { AttachmentChange = result.Change });
            RefreshAfterChange(null);
            var kept = result.Change.Added.Find(item => !item.Value.IsRepeater && item.Value.Id == id);
            if (kept.Value == null)
                kept = result.Change.Added.Find(item => !item.Value.IsRepeater);
            SelectRow(kept);
            UpdateRowPreview();
            SetStatusT("rows.status.changed", ("name", Path.GetFileNameWithoutExtension(request.AssetPath)), ("hasCount", selectedRow != null ? "yes" : "no"), ("count", selectedRow?.ObjectCount ?? 0));
        }

        /// <summary>The row a record belongs to, with its parts on other tiles, or null when it has no objects along its road.</summary>
        private OmsiAttachmentRow RowOf(KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord> record)
        {
            rowLayout = rowLayout ?? new OmsiSplineAttachmentLayout(currentSummary);
            try
            {
                return OmsiAttachmentRows.RowOf(currentSummary, rowLayout, record.Key, record.Value);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning(exception.Message);
                return null;
            }
        }

        /// <summary>Without a distance typed, a row stands 0.5 m inside the edge of the road's profile, on the pavement.</summary>
        private double EdgeOffset(OmsiSplineRecord spline, bool onRight)
        {
            var profile = RoadProfiles().Get(spline.AssetPath);
            return profile == null ? 5.0 : Math.Max(0.5, (onRight ? profile.RightEdge : -profile.LeftEdge) - 0.5);
        }

        /// <summary>Where the objects of a record of the map stand.</summary>
        private List<OmsiPlacedAttachment> Placed(KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord> row)
        {
            rowLayout = rowLayout ?? new OmsiSplineAttachmentLayout(currentSummary);
            return rowLayout.Place(row.Key, row.Value);
        }

        /// <summary>Where the objects of a row of the map stand, in all its records.</summary>
        private List<OmsiPlacedAttachment> Placed(OmsiAttachmentRow row)
        {
            var objects = new List<OmsiPlacedAttachment>();
            foreach (var record in row.Records)
                objects.AddRange(Placed(record));
            return objects;
        }

        /// <summary>The row of the map with an object under the pointer, or null; the rows are laid out once while the tool lasts.</summary>
        private KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>? RowAt(OmsiMapPoint pointer)
        {
            if (rowObjects == null)
            {
                hoveredRow = null;
                rowObjects = new List<KeyValuePair<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>, List<OmsiPlacedAttachment>>>();
                foreach (var tile in currentSummary.Tiles)
                    foreach (var row in tile.SplineAttachments)
                        if (!row.IsRepeater)
                        {
                            var pair = new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(tile, row);
                            rowObjects.Add(new KeyValuePair<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>, List<OmsiPlacedAttachment>>(pair, Placed(pair)));
                        }
            }
            var reach = Math.Max(1.5, 8.0 * mapCanvas.MetersPerPixel);
            foreach (var entry in rowObjects)
                foreach (var item in entry.Value)
                    if (Math.Abs(item.WorldX - pointer.X) <= reach && Math.Abs(item.WorldY - pointer.Y) <= reach)
                        return entry.Key;
            return null;
        }

        /// <summary>Draws a mark where each object of a row stands, in the preview colour or red.</summary>
        private void ShowRowMarks(List<OmsiPlacedAttachment> objects, bool valid)
        {
            var marks = new List<OmsiMapPoint[]>();
            var half = Math.Max(0.6, 4.0 * mapCanvas.MetersPerPixel);
            foreach (var item in objects)
                marks.Add(new[]
                {
                    new OmsiMapPoint(item.WorldX, item.WorldY + half), new OmsiMapPoint(item.WorldX + half, item.WorldY),
                    new OmsiMapPoint(item.WorldX, item.WorldY - half), new OmsiMapPoint(item.WorldX - half, item.WorldY),
                    new OmsiMapPoint(item.WorldX, item.WorldY + half)
                });
            mapCanvas.SetRoadPreview(marks, valid, null, null, null);
        }

        /// <summary>
        /// Fits the terrain again when an editor-placed junction moved, appeared or went, since fitted roads may join it
        /// (ADR 022). The terrain follows from the original and the roads, so this is no undo step of its own. Only the tiles around
        /// the junctions that changed are fitted, which gives the terrain of a full fit as long as the terrain was fitted to the junctions
        /// as they were last (<see cref="OmsiMapSession.RefitTerrainAtJunctions"/>); when that is not known, such as after opening the map,
        /// after a failed fit or while no fitted road was there, the whole map is fitted.
        /// </summary>
        private void RefitTerrainAtJunctions()
        {
            if (!CanEdit || currentSummary == null)
                return;
            if (!currentSummary.Tiles.Exists(tile => tile.Splines.Exists(spline => spline.AlignsTerrain && session.ShapesTerrain(spline))))
            {
                // Junctions may move meanwhile without a fit, so the next fit cannot start from the junctions known.
                junctionTerrainPlaces = null;
                return;
            }
            List<OmsiTileSummary> tiles;
            try
            {
                var places = session.JunctionTerrainPlaces(currentSummary);
                var state = JunctionStateText(places);
                if (state == junctionTerrainState)
                    return;
                junctionTerrainState = state;
                var before = junctionTerrainPlaces;
                // Known again only once the fit succeeded; a failed fit leaves the next one to fit the whole map.
                junctionTerrainPlaces = null;
                tiles = session.RefitTerrainAtJunctions(currentSummary, before, places);
                junctionTerrainPlaces = places;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("terrain.junctions_failed", ("reason", exception.Message));
                return;
            }
            if (tiles.Count == 0)
                return;
            groundRenderer?.RefreshTerrain(tiles);
            objectRenderer?.RefreshTerrain(tiles, session.Source);
            if (roadTerrain != null)
                roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
        }

        /// <summary>The text of <see cref="OmsiMapSession.JunctionTerrainState"/> for these junctions.</summary>
        private static string JunctionStateText(IReadOnlyList<OmsiJunctionTerrainPlace> places)
        {
            var text = new System.Text.StringBuilder();
            foreach (var place in places)
                text.Append(place.State);
            return text.ToString();
        }

        private OmsiTileSummary TileHolding(MapSelectionItem item)
        {
            if (item.Tile != null && item.Tile.Objects.Contains(item.Record))
                return item.Tile;
            foreach (var tile in currentSummary.Tiles)
                if (tile.Objects.Contains(item.Record))
                    return tile;
            return null;
        }

        /// <summary>Whether the element is, or lies in, a field that takes typed text or numbers, where keys are typing.</summary>
        internal static bool InTextInput(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current is TextField || current is IntegerField || current is FloatField || current.ClassListContains(TextInputBaseField<string>.ussClassName))
                    return true;
            return false;
        }

        /// <summary>Keys that leave an open context menu open: none (the character half of a key), arrows, Enter and the modifier keys alone.</summary>
        internal static bool KeepsContextMenu(KeyCode key)
        {
            return key == KeyCode.None || key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.LeftArrow || key == KeyCode.RightArrow ||
                   key == KeyCode.Return || key == KeyCode.KeypadEnter || key >= KeyCode.RightShift && key <= KeyCode.AltGr;
        }

        /// <summary>Esc: the order comes from <see cref="EscapeOrder"/>; the actions are tried until one takes the key.</summary>
        private void OnEscape(KeyDownEvent evt)
        {
            var focused = evt.target as VisualElement;
            var state = new EscapeState
            {
                ContextMenuOpen = ContextMenuOpen,
                SearchHoldsFocus = objectsPanel != null && objectsPanel.SearchHolds(focused),
                FocusInObjectLibrary = objectsPanel != null && focused != null && objectsPanel.Contains(focused),
                PickingElementType = PickingElementType,
                PickingVarParent = PickingVarParent,
                PickingSplineType = PickingSplineType,
                PlacementAssetChosen = placementAsset != null,
                MapPlacing = mapCanvas != null && mapCanvas.IsPlacing,
                TypingInField = InTextInput(focused),
                ModalDialogShown = AnyModalDialogShown(),
                // The window for pictures from the air leaves keys to the map, except while it waits for a point to be clicked in the map.
                SatelliteWaitsForPick = IsShown(satelliteWindow) && (placementPickFor >= 0 || mapCanvas != null && mapCanvas.IsPickingPoint),
                ControlHeld = evt.ctrlKey || evt.commandKey,
                ObjectsPanelOpen = objectsPanelOpen,
                SelectionNotEmpty = selection.Count > 0,
            };
            foreach (var action in EscapeOrder.Order(state))
            {
                if (!RunEscapeAction(action))
                    continue;
                if (EscapeOrder.StopsPropagation(action))
                    evt.StopPropagation();
                return;
            }
        }

        /// <summary>Does what the action says; false when it found nothing to do (only for actions that <see cref="EscapeOrder.CanDecline"/>).</summary>
        private bool RunEscapeAction(EscapeAction action)
        {
            switch (action)
            {
                case EscapeAction.CloseContextMenu:
                    CloseContextMenu();
                    return true;
                case EscapeAction.CloseTopOverlay:
                    return CloseTopOverlay();
                case EscapeAction.EscapeActiveTool:
                    return EscapeActiveTool();
                case EscapeAction.EndVarParentPick:
                    EndVarParentPick(true);
                    return true;
                case EscapeAction.EndElementTypePick:
                    EndElementTypePick(null);
                    return true;
                case EscapeAction.EndSplineTypePick:
                    EndSplineTypePick(null);
                    return true;
                case EscapeAction.StopPlacement:
                    StopPlacement();
                    return true;
                case EscapeAction.StopPlacementWithStatus:
                    StopPlacement();
                    SetStatusT("place.status.stopped");
                    return true;
                case EscapeAction.CloseObjectsPanel:
                    ShowObjectsPanel(false);
                    return true;
                case EscapeAction.ClearSelection:
                    SelectTile(selectedTile, false);
                    return true;
                default:
                    return true;
            }
        }

        private void OnShortcut(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                OnEscape(evt);
                return;
            }
            // Any other key closes the menu first, as its entries were made for the state before, and then does what it always does.
            if (ContextMenuOpen && !KeepsContextMenu(evt.keyCode))
                CloseContextMenu();
            var focused = evt.target as VisualElement;
            // Keys are typing in a field, and a modal dialog takes them all. The window for pictures from the air leaves them to the map,
            // except while it waits for a point to be clicked in the map.
            if (InTextInput(focused) || AnyModalDialogShown() || IsShown(satelliteWindow) && (placementPickFor >= 0 || mapCanvas != null && mapCanvas.IsPickingPoint))
                return;

            if (!(evt.ctrlKey || evt.commandKey))
            {
                if (evt.keyCode == KeyCode.Delete && activeTool == EditorTool.Rows && selectedRow != null)
                {
                    DeleteSelectedRow();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.R && activeTool == EditorTool.Rows && rowOptions != null)
                {
                    rowOptions.TurnBy(evt.shiftKey ? -PlacementRotationStep : PlacementRotationStep);
                    SetStatusT("rows.status.turned", ("degrees", rowOptions.HeadingDegrees));
                    if (selectedRow != null)
                        ChangeSelectedRow(null);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Delete && activeTool == EditorTool.Markings && (markingSelected >= 0 || markingArea >= 0 || markingArrow >= 0))
                {
                    DeleteSelectedMarking();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Y && activeTool == EditorTool.Markings)
                {
                    NextMarkingStyle();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.R && mapCanvas != null && mapCanvas.IsPlacing)
                {
                    mapCanvas.PlacementHeading += evt.shiftKey ? -PlacementRotationStep : PlacementRotationStep;
                    objectsPanel?.ShowHeading(mapCanvas.PlacementHeading);
                    SetStatusT("place.status.heading", ("path", placementAsset), ("heading", mapCanvas.PlacementHeading));
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Delete && selection.Count > 0)
                {
                    DeleteSelectedObjects();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Delete && selectedSpline != null)
                {
                    DeleteSelectedSpline();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.W && activeTool == EditorTool.Move && mapCanvas != null)
                {
                    // W again while the move tool is active switches the arrows between the tile axes and the object's own.
                    mapCanvas.LocalArrowAxes = !mapCanvas.LocalArrowAxes;
                    SetStatusT(mapCanvas.LocalArrowAxes ? "move.axes.local" : "move.axes.global") /* i18n:keys */;
                    evt.StopPropagation();
                }
                // The tool keys (the tool table): Q, W and E choose their tool always; the others switch between their tool and
                // Drag, only while nothing is being placed. R in the rows tool, R while placing and W in the move tool are taken above.
                else if (TryGetToolOfShortcut(evt.keyCode, out var keyTool))
                {
                    SetTool(EditorToolRules.KeyTogglesWithDrag(keyTool) && activeTool == keyTool ? EditorTool.Drag : keyTool);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Alpha3 || evt.keyCode == KeyCode.Keypad3)
                {
                    ShowObjectsPanel(!objectsPanelOpen);
                    evt.StopPropagation();
                }
                return;
            }

            if (evt.keyCode == KeyCode.C && !evt.shiftKey)
            {
                CopySelection();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.V && !evt.shiftKey)
            {
                BeginPaste();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.D && !evt.shiftKey)
            {
                DuplicateSelection();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Z && !evt.shiftKey)
            {
                StepHistory(undoSteps, redoSteps, true);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shiftKey))
            {
                StepHistory(redoSteps, undoSteps, false);
                evt.StopPropagation();
            }
        }

        private void StepHistory(List<ObjectEditStep> from, List<ObjectEditStep> to, bool undo)
        {
            if (!CanEditObjects || from.Count == 0)
            {
                SetStatus(from.Count > 0 && session != null && session.CanEdit ? ReadOnlyMessage(undo ? "readonly.action.undo" : "readonly.action.redo") : Loc.T(undo ? "history.nothing_undo" : "history.nothing_redo")) /* i18n:keys */;
                return;
            }

            var step = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            if (step.TileChange != null)
            {
                StepTile(step, from, to, undo);
                return;
            }
            if (step.TileRevert != null)
            {
                StepTileRevert(step, from, to, undo);
                return;
            }
            var touched = new List<MapSelectionItem>();
            var applied = AppliedStates(step);
            try
            {
                ApplyStep(step, undo, touched);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                var message = Loc.T(undo ? "history.undo_failed" : "history.redo_failed", ("step", DescribeStep(step)), ("reason", exception.Message)) /* i18n:keys */;
                var refused = exception is OmsiStepRefusedException;
                // A failed roll-back or a part left done or undone: files and memory may disagree, so the map is read again, which
                // clears the history. A move by values is all or none like every other step, so it needs no exception here.
                if (exception is OmsiRollBackFailedException || !SameAppliedStates(step, applied))
                {
                    if (ReloadAfterFailedRollBack())
                        SetStatus(message);
                    else
                        SetStatusT("history.rollback_failed_reload_failed", ("reason", exception.Message));
                    return;
                }
                // Refused: nothing was written and trying again gives the same answer, so the step leaves the history. Any other
                // failure was rolled back: files and memory are as before, so the step goes back on top of its stack, to be tried again.
                if (!refused)
                    from.Add(step);
                RefreshAfterChange(null);
                SetStatus(message);
                return;
            }

            to.Add(step);
            RefreshAfterChange(touched);
            SetStatus(Loc.T(undo ? "history.undid" : "history.redid", ("step", DescribeStep(step)))) /* i18n:keys */;
        }

        /// <summary>Whether each part of a step is in effect, in the order of <see cref="ApplyStep"/>; parts without such a state add nothing.</summary>
        private static List<bool> AppliedStates(ObjectEditStep step)
        {
            var states = new List<bool>();
            AddAppliedStates(step, states);
            return states;
        }

        private static void AddAppliedStates(ObjectEditStep step, List<bool> states)
        {
            if (step.Group != null)
            {
                foreach (var part in step.Group)
                    AddAppliedStates(part, states);
                return;
            }
            if (step.SplineChange != null)
                states.Add(step.SplineChange.IsApplied);
            else if (step.RoadTypeChange != null)
                return;
            else if (step.TerrainChange != null)
                states.Add(step.TerrainChange.IsApplied);
            else if (step.AttachedChange != null)
                states.Add(step.AttachedChange.IsApplied);
            else if (step.AttachmentChange != null)
                states.Add(step.AttachmentChange.IsApplied);
            else if (step.Change != null)
                states.Add(step.Change.IsApplied);
            // A move or turn by values adds nothing: OmsiMapSession.UpdateObjects writes the files of a run of value parts all or none and
            // sets the records only after, so a run that fails has changed nothing and needs no taking back, and one that succeeded is taken
            // back by writing its values before. So StepGroup takes back only the parts done before a failed run.
        }

        /// <summary>True when every part of a step is in effect, or not, as before a failed undo or redo.</summary>
        private static bool SameAppliedStates(ObjectEditStep step, List<bool> before)
        {
            var now = AppliedStates(step);
            if (now.Count != before.Count)
                return false;
            for (var index = 0; index < now.Count; index++)
                if (now[index] != before[index])
                    return false;
            return true;
        }

        /// <summary>
        /// Undoes or redoes a step; the parts of a group are undone in reverse order. Collects the objects that exist
        /// afterwards, so they can be selected. Parts next to each other that move or turn objects by values are written together, all or
        /// none (<see cref="ApplyValueParts"/>). When a part of a group fails, it is taken back when it changed whether it is in effect,
        /// then the parts done before it, the last first, and the failure is thrown as it was (<see cref="StepGroup.Apply{T}"/>); when
        /// taking them back fails too, <see cref="OmsiRollBackFailedException"/> reports both.
        /// </summary>
        private void ApplyStep(ObjectEditStep step, bool undo, List<MapSelectionItem> touched)
        {
            if (step.Group != null)
            {
                var order = new List<ObjectEditStep>(step.Group);
                if (undo)
                    order.Reverse();
                var units = StepGroup.Runs(order, IsValuePart);
                var takenBack = new List<MapSelectionItem>();
                // A failed roll-back is noted like one the session reports; the undo or redo reads the map at once, which clears the note.
                // A run of value parts is taken back last part first, as single parts were, so an object moved twice ends where it was.
                StepGroup.Apply(units, unit => ApplyUnit(unit, undo, touched), unit => ApplyUnit(Reversed(unit), !undo, takenBack), UnitStates, NoteRollBackFailure);
                return;
            }

            if (step.TileRevert != null)
                throw new InvalidOperationException("Reverting a tile is a step of its own; it cannot be part of a group.");

            if (step.SplineChange != null)
            {
                if (undo)
                    session.Undo(currentSummary, step.SplineChange);
                else
                    session.Redo(currentSummary, step.SplineChange);
                RefreshSplineViews(step.SplineChange);
                return;
            }

            if (step.RoadTypeChange != null)
            {
                if (undo)
                    step.RoadTypeChange.Undo();
                else
                    step.RoadTypeChange.Redo();
                AfterRoadTypeChange(step.RoadTypeChange.AssetPath);
                return;
            }

            if (step.TerrainChange != null)
            {
                if (undo)
                    session.Undo(currentSummary, step.TerrainChange);
                else
                    session.Redo(currentSummary, step.TerrainChange);
                if (step.TerrainChange.Painted)
                    RefreshAfterPaintStep(step.TerrainChange);
                else
                    RefreshGround(step.TerrainChange.Tiles);
                return;
            }

            if (step.AttachedChange != null)
            {
                if (undo)
                    session.Undo(currentSummary, step.AttachedChange);
                else
                    session.Redo(currentSummary, step.AttachedChange);
                return;
            }

            if (step.AttachmentChange != null)
            {
                if (undo)
                    session.Undo(currentSummary, step.AttachmentChange);
                else
                    session.Redo(currentSummary, step.AttachmentChange);
                rowLayout = null;
                rowObjects = null;
                DeselectRow();
                return;
            }

            if (step.Change != null)
            {
                if (undo)
                    session.Undo(currentSummary, step.Change);
                else
                    session.Redo(currentSummary, step.Change);
                if (step.Change.ObjectExists)
                    Touch(touched, step.Change.CurrentTile, step.Change.CurrentRecord);
                return;
            }

            ApplyValueParts(new[] { step }, undo, touched);
        }

        /// <summary>True for a part that moves or turns an object by values, the part <see cref="ApplyStep"/> handles last.</summary>
        private static bool IsValuePart(ObjectEditStep step)
        {
            return step.Group == null && step.TileRevert == null && step.SplineChange == null && step.RoadTypeChange == null &&
                   step.TerrainChange == null && step.AttachedChange == null && step.AttachmentChange == null && step.Change == null &&
                   step.TileChange == null;
        }

        /// <summary>Applies one part of a group, or a run of value parts together.</summary>
        private void ApplyUnit(List<ObjectEditStep> unit, bool undo, List<MapSelectionItem> touched)
        {
            if (unit.Count == 1 && !IsValuePart(unit[0]))
                ApplyStep(unit[0], undo, touched);
            else
                ApplyValueParts(unit, undo, touched);
        }

        /// <summary>Whether each piece of a unit of <see cref="ApplyUnit"/> is in effect; value parts add nothing (<see cref="AddAppliedStates"/>).</summary>
        private static List<bool> UnitStates(List<ObjectEditStep> unit)
        {
            var states = new List<bool>();
            foreach (var part in unit)
                AddAppliedStates(part, states);
            return states;
        }

        private static List<ObjectEditStep> Reversed(List<ObjectEditStep> unit)
        {
            var reversed = new List<ObjectEditStep>(unit);
            reversed.Reverse();
            return reversed;
        }

        /// <summary>
        /// Moves or turns the objects of value parts to their values before or after, in the order given, writing every file once and all
        /// or none (<see cref="OmsiMapSession.UpdateObjects"/>): a failure leaves files and records as they were. The objects are found when
        /// the parts are applied, as a part done just before may have moved one into another tile. An object that no longer exists refuses
        /// the parts before anything is written.
        /// </summary>
        private void ApplyValueParts(IReadOnlyList<ObjectEditStep> parts, bool undo, List<MapSelectionItem> touched)
        {
            var edits = new List<OmsiObjectValueEdit>(parts.Count);
            foreach (var part in parts)
            {
                var tile = currentSummary.Tiles.Find(candidate => candidate.X == part.TileX && candidate.Y == part.TileY);
                var item = tile?.Objects.Find(candidate => candidate.Id == part.ObjectId);
                // Nothing is written, and trying again gives the same answer: the step is refused.
                if (item == null)
                    throw new OmsiStepRefusedException("Object #" + part.ObjectId + " no longer exists.");
                var target = undo ? part.Before : part.After;
                edits.Add(new OmsiObjectValueEdit(tile, item, target[0], target[1], target[2], target[3], target[4], target[5]));
            }
            session.UpdateObjects(edits);
            foreach (var edit in edits)
                Touch(touched, edit.Tile, edit.Record);
        }

        /// <summary>
        /// Undoes or redoes an added or deleted tile; reading the map again clears the history, so the step is put back alone. A refused
        /// step leaves the history; a step whose roll-back failed reads the map again, which clears the history; any other failure was
        /// rolled back, so the step goes back where it was and can be tried again.
        /// </summary>
        private void StepTile(ObjectEditStep step, List<ObjectEditStep> from, List<ObjectEditStep> to, bool undo)
        {
            try
            {
                session.ApplyTileChange(step.TileChange, undo);
            }
            catch (OmsiRollBackFailedException exception)
            {
                Debug.LogException(exception);
                var description = DescribeStep(step);
                if (ReloadAfterFailedRollBack())
                    SetStatus(Loc.T(undo ? "history.undo_failed" : "history.redo_failed", ("step", description), ("reason", exception.Message))) /* i18n:keys */;
                else
                    SetStatusT("history.rollback_failed_reload_failed", ("reason", exception.Message));
                return;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                // Refused: nothing was written and trying again gives the same answer, so the step leaves the history. Any other failure
                // was rolled back: files and memory are as before, so the step goes back on top of its stack.
                if (!(exception is OmsiStepRefusedException))
                    from.Add(step);
                SetStatus(Loc.T(undo ? "history.undo_failed" : "history.redo_failed", ("step", DescribeStep(step)), ("reason", exception.Message))) /* i18n:keys */;
                return;
            }
            // The step is done: the tiles changed, so the map is read again. When that fails, what is shown no longer matches the files.
            var stepDescription = DescribeStep(step);
            try
            {
                ReloadMap();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                undoSteps.Clear();
                redoSteps.Clear();
                UpdateToolbar();
                SetStatusT("history.rollback_failed_reload_failed", ("reason", exception.Message));
                return;
            }
            to.Add(step);
            SetStatus(Loc.T(undo ? "history.undid" : "history.redid", ("step", stepDescription))) /* i18n:keys */;
        }

        /// <summary>
        /// Undoes or redoes a revert of a tile, keeping the history: the tile's records are swapped in place. A refused step leaves
        /// files and summary as they were, so the revert stays as it is for good: the step and every step beyond it on its stack leave
        /// the history, as those need the revert the other way (<see cref="OmsiStepHistory.DropStepsBeyondRefusedRevert{T}"/>); the
        /// other stack stays. A step whose roll-back failed may have left part of itself in the files: the map is read again, which
        /// clears the history. Any other failure was rolled back, so the step goes back where it was and can be tried again.
        /// </summary>
        private void StepTileRevert(ObjectEditStep step, List<ObjectEditStep> from, List<ObjectEditStep> to, bool undo)
        {
            try
            {
                if (undo)
                    session.Undo(currentSummary, step.TileRevert);
                else
                    session.Redo(currentSummary, step.TileRevert);
            }
            catch (OmsiStepRefusedException exception)
            {
                Debug.LogException(exception);
                var dropped = OmsiStepHistory.DropStepsBeyondRefusedRevert(from);
                Debug.Log("History: " + (undo ? "undoing" : "redoing") + " the revert of tile " + step.TileRevert.X + ", " + step.TileRevert.Y +
                          " was refused; it and " + dropped + " step(s) beyond it left the history.");
                SetStatus(Loc.T(undo ? "history.undo_failed_dropped" : "history.redo_failed_dropped", ("step", DescribeStep(step)), ("reason", exception.Message))) /* i18n:keys */;
                return;
            }
            catch (OmsiRollBackFailedException exception)
            {
                Debug.LogException(exception);
                var description = DescribeStep(step);
                if (ReloadAfterFailedRollBack())
                    SetStatus(Loc.T(undo ? "history.undo_failed" : "history.redo_failed", ("step", description), ("reason", exception.Message))) /* i18n:keys */;
                else
                    SetStatusT("history.rollback_failed_reload_failed", ("reason", exception.Message));
                return;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                // Files and summary are as before: the step goes back on top of its stack, to be tried again.
                from.Add(step);
                SetStatus(Loc.T(undo ? "history.undo_failed" : "history.redo_failed", ("step", DescribeStep(step)), ("reason", exception.Message))) /* i18n:keys */;
                return;
            }
            to.Add(step);
            RefreshAfterTileRevert(step.TileRevert);
            SetStatus(Loc.T(undo ? "history.undid" : "history.redid", ("step", DescribeStep(step)))) /* i18n:keys */;
        }

        /// <summary>
        /// Reads the map again after a step whose roll-back failed, so the views show what the files hold; that clears the history, whose
        /// steps hold records that may no longer match the files. When reading fails as well, the history is cleared all the same. The
        /// caller sets the status afterwards, so the reason of the failure is not replaced by the reload's own message. Clears a failed
        /// roll-back noted for the next frame (<see cref="ReloadAfterPendingRollBack"/>), so the map is not read twice, also one noted
        /// while the map is read. Returns false when reading failed: then the caller says so (history.rollback_failed_reload_failed).
        /// </summary>
        private bool ReloadAfterFailedRollBack()
        {
            pendingRollBackFailure = null;
            try
            {
                ReloadMap();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                undoSteps.Clear();
                redoSteps.Clear();
                UpdateToolbar();
                return false;
            }
            finally
            {
                pendingRollBackFailure = null;
            }
        }

        /// <summary>
        /// Brings the views and tools up to date after a tile was reverted, or the revert undone or redone. The tile summaries stay,
        /// only the reverted tile's records changed, so the selection is found again by ID. What holds records of the summary ends as
        /// when another view is shown (<see cref="PresentSummary"/>): the picks waiting for a click, and the tools the tool table ends on
        /// every view; the rows tool lets go of its row.
        /// </summary>
        private void RefreshAfterTileRevert(OmsiTileRevertChange change)
        {
            LeaveTransientModes();
            if (EditorToolRules.EndsOnEveryView(activeTool))
            {
                EndToolForView();
                if (mapCanvas != null)
                    mapCanvas.Tool = activeTool;
            }
            if (activeTool == EditorTool.Rows)
            {
                rowLayout = null;
                rowObjects = null;
                rowStart = null;
                DeselectRow();
            }
            ClearSelectedSpline();
            // The revert fitted the terrain around the tile and its neighbours only, and undo and redo put those files back, so no fit
            // outside the step follows now. A junction lying outside its tile, or one that takes a road end over from a junction in the
            // tile, may still reach beyond them: the terrain is not known to be fitted to the junctions as they are, and the next fit
            // after a junction changed fits the whole map (RefitTerrainAtJunctions).
            junctionTerrainPlaces = null;
            if (CanEdit && currentSummary != null &&
                currentSummary.Tiles.Exists(tile => tile.Splines.Exists(spline => spline.AlignsTerrain && session.ShapesTerrain(spline))))
            {
                try
                {
                    junctionTerrainState = JunctionStateText(session.JunctionTerrainPlaces(currentSummary));
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
                {
                    // The junctions are compared again at the next edit, which fits the terrain to them then.
                    Debug.LogException(exception);
                    junctionTerrainState = null;
                }
            }
            roadGuideLines = null;
            mapCanvas?.RefreshSplines();
            if (change.Tile != null && groundRenderer != null && !groundRenderer.RefreshSplines(new[] { change.Tile }))
                Debug.Log("Spline surfaces of the reverted tile are built once the ground has finished loading.");
            var groundTiles = new List<OmsiTileSummary>(change.TerrainTiles);
            if (change.Tile != null && !groundTiles.Contains(change.Tile))
                groundTiles.Add(change.Tile);
            RefreshGround(groundTiles);
            if (roadPorts != null)
                UpdateRoadPorts();
            StartOutlineBuild(currentSummary);
            ResetPaths(currentSummary);
            RefreshAfterChange(null);
            UpdateToolbar();
        }

        /// <summary>Adds an empty tile at a free place beside the map, its terrain running on from its neighbours.</summary>
        private void AddTile(int x, int y)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.add_tiles"));
                return;
            }
            OmsiTileChange change;
            try
            {
                change = session.AddTile(currentSummary, x, y);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("tile.status.add_failed", ("x", x), ("y", y), ("reason", exception.Message));
                return;
            }
            ReloadMap();
            PushHistory(new ObjectEditStep { TileChange = change });
            var tile = currentSummary.Tiles.Find(candidate => candidate.X == x && candidate.Y == y);
            if (tile != null)
                SelectTile(tile, false);
            SetStatusT("tile.status.added", ("x", x), ("y", y));
            TileAddedForImagery(x, y);
        }

        /// <summary>Takes a tile out of the map, as long as nothing outside it needs it; otherwise says what does.</summary>
        private void DeleteTile(OmsiTileSummary tile)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.delete_tiles"));
                return;
            }
            OmsiTileDeletionResult result;
            try
            {
                result = session.DeleteTile(currentSummary, tile);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("tile.status.delete_failed", ("x", tile.X), ("y", tile.Y), ("reason", exception.Message));
                return;
            }
            if (!result.Succeeded)
            {
                SetStatus(Loc.T("tile.status.stays", ("x", tile.X), ("y", tile.Y), ("list", JoinLimited(new List<string>(result.Blockers)))));
                return;
            }
            ReloadMap();
            PushHistory(new ObjectEditStep { TileChange = result.Change });
            SetStatusT("tile.status.deleted", ("x", tile.X), ("y", tile.Y));
        }

        private static void Touch(List<MapSelectionItem> touched, OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (item == null)
                return;
            if (!touched.Exists(entry => ReferenceEquals(entry.Record, item)))
                touched.Add(new MapSelectionItem { Tile = tile, Record = item });
        }

        private static string DescribeStep(ObjectEditStep step)
        {
            if (step.Description != null)
                return step.Description;
            if (step.TileChange != null)
                return Loc.T(step.TileChange.Added ? "history.step.new_tile" : "history.step.deleted_tile", ("x", step.TileChange.X), ("y", step.TileChange.Y)); // i18n:keys
            if (step.TileRevert != null)
                return Loc.T("history.step.reverted_tile", ("x", step.TileRevert.X), ("y", step.TileRevert.Y));
            if (step.AttachedChange != null)
                return step.AttachedChange.Added.Count > 0 ? Loc.T("history.step.attached", ("name", Path.GetFileNameWithoutExtension(step.AttachedChange.Added[0].Value.AssetPath)))
                    : Loc.T("history.step.take_off", ("name", Path.GetFileNameWithoutExtension(step.AttachedChange.Removed[0].Value.AssetPath)));
            if (step.AttachmentChange != null)
                return Loc.T(/* i18n:keys */ step.AttachmentChange.Added.Count > 0 && step.AttachmentChange.Removed.Count > 0 ? "history.step.row_changed" :
                    step.AttachmentChange.Added.Count > 0 ? "history.step.row_new" : "history.step.row_deleted"); // i18n:keys
            if (step.Group != null)
            {
                var ids = new HashSet<int>();
                CollectIds(step, ids);
                return Loc.T("history.step.objects_changed", ("count", ids.Count));
            }
            if (step.SplineChange != null && step.SplineChange.Kind == OmsiSplineChangeKind.TerrainAlignment)
                return Loc.T("history.step.terrain_fit", ("count", step.SplineChange.SplineIds.Count));
            if (step.SplineChange != null && step.SplineChange.Kind == OmsiSplineChangeKind.Geometry)
                return Loc.T("history.step.spline_move", ("count", step.SplineChange.SplineIds.Count), ("id", step.SplineChange.SplineIds[0]));
            if (step.SplineChange != null)
                return Loc.T(/* i18n:keys */ step.SplineChange.Kind == OmsiSplineChangeKind.Creation ? "history.step.road_of" : "history.step.spline_deleted",
                    ("count", step.SplineChange.SplineIds.Count), ("id", step.SplineChange.SplineIds[0])); // i18n:keys
            if (step.Change == null)
                return Loc.T("history.step.object_changed", ("id", step.ObjectId));
            switch (step.Change.Kind)
            {
                case OmsiObjectChangeKind.Placement:
                    return Loc.T("history.step.placement", ("id", step.Change.Record.Id));
                case OmsiObjectChangeKind.Deletion:
                    return Loc.T("history.step.object_deleted", ("id", step.Change.Record.Id));
                case OmsiObjectChangeKind.Replacement:
                    return Loc.T("history.step.new_shape", ("id", step.Change.Record.Id));
                case OmsiObjectChangeKind.Rules:
                    return Loc.T("history.step.rules", ("id", step.Change.Record.Id));
                case OmsiObjectChangeKind.Copy:
                    return Loc.T("history.step.copy", ("id", step.Change.Record.Id), ("original", step.Change.ReplacedRecord.Id));
                default:
                    return Loc.T("history.step.object_move", ("id", step.Change.Record.Id), ("x", step.Change.TargetTile.X), ("y", step.Change.TargetTile.Y));
            }
        }

        private static void CollectIds(ObjectEditStep step, HashSet<int> ids)
        {
            if (step.Group != null)
                foreach (var part in step.Group)
                    CollectIds(part, ids);
            else if (step.SplineChange != null)
                ids.UnionWith(step.SplineChange.SplineIds);
            else
                ids.Add(step.Change?.Record?.Id ?? step.ObjectId);
        }

        private void PushHistory(ObjectEditStep step)
        {
            undoSteps.Add(step);
            if (undoSteps.Count > HistoryLimit)
                undoSteps.RemoveAt(0);
            redoSteps.Clear();
        }

        /// <summary>
        /// Deletes the selected objects as one undo step. Objects that other parts of the map still refer to stay and
        /// remain selected.
        /// </summary>
        private void DeleteSelectedObjects()
        {
            if (selection.Count == 0)
            {
                SetStatusT("delete.select_first");
                return;
            }
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.edit_it"));
                return;
            }

            var targets = new List<MapSelectionItem>(selection);
            var parts = new List<ObjectEditStep>();
            var kept = new List<MapSelectionItem>();
            var reasons = new List<string>();
            IReadOnlyList<OmsiReference> blockers = new OmsiReference[0];
            string failure = null;
            try
            {
                // One index for all objects; deleting objects does not add references.
                var references = session.BuildReferenceIndex();
                foreach (var entry in targets)
                {
                    var result = session.DeleteObject(currentSummary, entry.Tile, entry.Record, references);
                    if (result.Succeeded)
                    {
                        parts.Add(new ObjectEditStep { TileX = entry.Tile.X, TileY = entry.Tile.Y, ObjectId = entry.Record.Id, Change = result.Change });
                    }
                    else
                    {
                        kept.Add(entry);
                        blockers = result.Blockers;
                        reasons.Add("#" + entry.Record.Id + " by " + DescribeReferences(result.Blockers));
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                failure = Loc.T("delete.failed", ("reason", exception.Message));
            }

            if (parts.Count > 0)
                PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts });
            RefreshAfterChange(kept);

            if (failure != null)
                SetStatus(failure + (parts.Count > 0 ? " " + Loc.T("delete.before", ("count", parts.Count)) : string.Empty));
            else if (targets.Count == 1 && parts.Count == 1)
                SetStatusT("delete.done_one", ("id", targets[0].Record.Id), ("path", targets[0].Record.AssetPath), ("where", EditingVariant ? "variant" : "file"), ("title", EditingVariant ? editedVariant.Title : string.Empty), ("file", targets[0].Tile.FileName));
            else if (targets.Count == 1)
                SetStatus(Loc.T("delete.not_deleted", ("id", targets[0].Record.Id), ("refs", DescribeReferences(blockers))));
            else
                SetStatus(Loc.T("delete.done_many", ("done", parts.Count), ("total", targets.Count), ("hasKept", kept.Count > 0 ? "yes" : "no"), ("list", kept.Count > 0 ? JoinLimited(reasons) : string.Empty), ("any", parts.Count > 0 ? "yes" : "no")));
        }

        private static string DescribeReferences(IReadOnlyList<OmsiReference> references)
        {
            var described = new List<string>();
            foreach (var reference in references)
            {
                var text = reference.ToString();
                if (!described.Contains(text))
                    described.Add(text);
            }
            var shown = described.GetRange(0, Math.Min(3, described.Count));
            return described.Count > shown.Count ? Loc.T("objects.join_more", ("list", string.Join(", ", shown)), ("count", described.Count - shown.Count)) : string.Join(", ", shown);
        }

        /// <summary>
        /// Opens or closes the Objects tool's panel. Opening needs an editable map; it counts the objects the map uses
        /// and lists the installation's objects in the background. Closing ends placing.
        /// </summary>
        private void ShowObjectsPanel(bool show)
        {
            if (objectsPanelHost == null)
                return;
            if (!show)
            {
                if (placementAsset != null)
                    StopPlacement();
                objectsPanelOpen = false;
                objectsPanelHost.style.display = DisplayStyle.None;
                UpdateToolbar();
                return;
            }
            if (session == null || currentSummary == null)
            {
                SetStatusT("place.status.open_first");
                return;
            }
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.place_objects"));
                return;
            }

            // Tools with a panel of their own in the same place end first, or both panels would stand there at once and Esc
            // would close only one of them.
            if (activeTool != EditorTool.Drag && activeTool != EditorTool.Move && activeTool != EditorTool.Rotate)
                SetTool(EditorTool.Drag);
            UpdateObjectsCatalog();
            StartInstallationScan();
            objectsPanelOpen = true;
            objectsPanel.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            objectsPanel.FocusSearch();
            UpdateToolbar();
        }

        /// <summary>Counts the objects the map uses and hands them with the installation's objects to the panel.</summary>
        private void UpdateObjectsCatalog()
        {
            if (objectsPanel == null || currentSummary == null)
                return;
            usedAssetCounts.Clear();
            foreach (var tile in currentSummary.Tiles)
                foreach (var item in tile.Objects)
                    usedAssetCounts[item.AssetPath] = usedAssetCounts.TryGetValue(item.AssetPath, out var count) ? count + 1 : 1;
            objectsPanel.SetCatalog(installationCatalog, usedAssetCounts);
        }

        /// <summary>
        /// Lists the .sco files below Sceneryobjects of the OMSI installation once, on a worker thread, with their groups
        /// and kinds, and reads from Steam's file lists which of them belong to the base game, a DLC or
        /// neither.
        /// </summary>
        /// <param name="again">Reads the installation again, such as after the editor made an object.</param>
        private void StartInstallationScan(bool again = false)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (!again && installationScan != null && string.Equals(root, installationScanRoot, StringComparison.OrdinalIgnoreCase))
                return;

            installationScanRoot = root;
            installationCatalog = null;
            contentOrigins = null;
            var excludedFolders = ExcludedContentFolders(root);
            var scan = Task.Run(() => OmsiInstallationCatalog.Scan(root, OmsiContentOrigins.LikelySteamRoots(), excludedFolders));
            installationScan = scan;

            IVisualElementScheduledItem poll = null;
            poll = objectsPanel.schedule.Execute(() =>
            {
                if (!scan.IsCompleted)
                    return;
                poll.Pause();
                if (!ReferenceEquals(scan, installationScan))
                    return;
                if (scan.IsFaulted)
                {
                    Debug.LogException(scan.Exception);
                    installationCatalog = OmsiInstallationCatalog.Empty(Loc.T("place.installation_failed"));
                    contentOrigins = installationCatalog.Origins;
                    SetStatusT("place.status.list_failed", ("reason", scan.Exception?.GetBaseException().Message ?? string.Empty));
                }
                else
                {
                    installationCatalog = scan.Result;
                    contentOrigins = installationCatalog.Origins;
                    Debug.Log("Object origins: " + contentOrigins.Description + " Groups in " + installationCatalog.Groups.Count + " of " +
                              installationCatalog.Objects.Count + " objects.");
                }
                objectsPanel.SetCatalog(installationCatalog, usedAssetCounts);
                ApplyHideDlc();
                ShowFoldersLeftOut(installationCatalog);
                if (activeTool == EditorTool.Road)
                    UpdateRoadProfiles();
            }).Every(150);
        }

        /// <summary>Starts placement mode with an object file relative to the OMSI installation.</summary>
        private void StartPlacement(string assetPath)
        {
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.place_objects"));
                return;
            }
            clipboardPlacing = false;
            placingEntryName = pendingEntryName;
            pendingEntryName = null;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            placementHasAbsoluteHeight = false;
            var problem = (string)null;
            try
            {
                var scoPath = OmsiRelativePath.Resolve(root, assetPath);
                if (File.Exists(scoPath))
                {
                    var sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(scoPath));
                    placementHasAbsoluteHeight = sceneryObject.HasAbsoluteHeight || sceneryObject.HasSplineHelper;
                }
                else
                {
                    problem = " " + Loc.T("place.problem_missing");
                }

                // One outline is quick to compute; the preview needs it right away.
                if (!outlineCache.ContainsKey(assetPath) && string.Equals(root, outlineInstallationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    outlineCache[assetPath] = new OmsiObjectOutlineCatalog(root).Get(assetPath);
                    mapCanvas?.SetOutlines(outlineCache);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                problem = " " + Loc.T("place.problem_unreadable", ("reason", exception.Message));
            }

            placementAsset = assetPath;
            mapCanvas?.BeginPlacement(assetPath, mapCanvas.PlacementHeading);
            if (RandomHeadingOn && placingEntryName == null)
                RollPlacementHeading();
            if (mapCanvas != null)
                objectsPanel?.ShowHeading(mapCanvas.PlacementHeading);
            UpdateToolbar();
            mapCanvas?.Focus();
            SetStatus(Loc.T("place.status.placing", ("path", assetPath), ("problem", problem ?? string.Empty)));
        }

        private void PlaceChosenObject(MapPlacement placement)
        {
            if (placementAsset == null || !CanEditObjects)
            {
                StopPlacement();
                return;
            }
            if (clipboardPlacing)
            {
                PlaceClipboardAt(placement);
                return;
            }
            if (placement.Snapped && placementAttach != null)
            {
                AttachChosenObject(placement.KeepPlacing);
                return;
            }
            if (placement.Snapped && placementPlan != null)
            {
                InsertChosenJunction(placement);
                return;
            }
            if (placement.Tile == null || !placement.Tile.IsAvailable)
            {
                SetStatus(placement.Tile == null ? Loc.T("place.status.no_tile") : Loc.T("place.status.tile_missing", ("file", placement.Tile.FileName)));
                return;
            }

            if (placingEntryName != null)
            {
                PlaceEntryPoint(placement);
                return;
            }

            // OMSI stands objects on the terrain, but takes the height of junctions and [absheight] objects as it is: those
            // get the terrain height under the click, or they would sink into a hill at 0 m.
            var height = 0.0;
            if (placementHasAbsoluteHeight)
            {
                var world = OmsiMapGeometry.TileLocalToWorld(placement.Tile.X, placement.Tile.Y, placement.LocalX, placement.LocalY, currentSummary.TileSizeMeters);
                if (new OmsiTerrainSampler(currentSummary, session.Source).TryGetHeight(world.X, world.Y, out var ground))
                    height = ground;
            }
            OmsiObjectChange change;
            try
            {
                change = session.PlaceObject(currentSummary, placement.Tile, placementAsset, placement.LocalX, placement.LocalY, height, mapCanvas.PlacementHeading);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("place.status.failed", ("reason", exception.Message));
                return;
            }

            var assetPath = placementAsset;
            if (!placement.KeepPlacing)
                StopPlacement();
            else if (RandomHeadingOn)
                RollPlacementHeading();
            PushHistory(new ObjectEditStep { TileX = placement.Tile.X, TileY = placement.Tile.Y, ObjectId = change.Record.Id, Change = change });
            RefreshAfterChange(new[] { new MapSelectionItem { Tile = placement.Tile, Record = change.Record } });
            if (objectsPanelOpen)
                UpdateObjectsCatalog();
            SetStatus(Loc.T("place.status.placed", ("id", change.Record.Id), ("path", assetPath), ("x", placement.Tile.X), ("y", placement.Tile.Y), ("inVariant", EditingVariant ? "yes" : "no"),
                    ("title", EditingVariant ? editedVariant.Title : string.Empty), ("absolute", placementHasAbsoluteHeight ? "yes" : "no"), ("height", change.Record.Height)));
        }

        /// <summary>
        /// Snaps a junction being placed onto the road under the pointer, with two opposite connection points on its centre
        /// line and the other arms towards the pointer, and keeps the plan for the click; null places it freely.
        /// </summary>
        private PlacementSnap SnapPlacement(double worldX, double worldY)
        {
            placementPlan = null;
            placementAttach = null;
            if (placementAsset == null || !CanEdit || currentSummary == null || placingEntryName != null)
                return null;
            var catalog = Junctions();
            if (catalog == null || catalog.Helpers(placementAsset).Count < 2)
                return SnapToAttachPoint(worldX, worldY);
            var point = OmsiSplinePoint.Nearest(currentSummary, worldX, worldY, JunctionSnapMeters);
            var plan = point == null ? null
                : OmsiJunctionInsertion.Plan(currentSummary, catalog, placementAsset, point.Tile, point.Spline, point.Distance, worldX, worldY);
            if (plan == null)
            {
                ShowSnapMessage(null);
                return null;
            }
            placementPlan = plan;
            ShowSnapMessage(plan.Problem ?? Loc.T("place.snap.junction"));
            return new PlacementSnap { WorldX = plan.Position.X, WorldY = plan.Position.Y, HeadingDegrees = plan.HeadingDegrees, Height = plan.Height };
        }

        /// <summary>
        /// Snaps an object being placed onto the free attachment point of a placed object nearest to the pointer, such as
        /// the end of a mast's arm; null places it freely.
        /// </summary>
        private PlacementSnap SnapToAttachPoint(double worldX, double worldY)
        {
            var reach = Math.Max(AttachSnapMeters, AttachSnapPixels * mapCanvas.MetersPerPixel);
            if (objectRenderer == null || !objectRenderer.TryFindAttachPoint(worldX, worldY, reach, out var spot) || !spot.Tile.IsAvailable)
            {
                ShowSnapMessage(null);
                return null;
            }
            placementAttach = spot;
            ShowSnapMessage(Loc.T("place.snap.attach", ("n", spot.PointIndex + 1), ("parent", spot.ParentId), ("hasName", spot.ParentAsset != null ? "yes" : "no"),
                ("name", spot.ParentAsset != null ? Path.GetFileNameWithoutExtension(spot.ParentAsset) : string.Empty)));
            return new PlacementSnap
            {
                WorldX = spot.WorldX, WorldY = spot.WorldY, HeadingDegrees = (spot.HeadingDegrees + mapCanvas.PlacementHeading) % 360.0, Height = spot.Height
            };
        }

        /// <summary>Hangs the object being placed on the attachment point it snapped onto, as one undo step.</summary>
        private void AttachChosenObject(bool keepPlacing)
        {
            var spot = placementAttach;
            OmsiAttachedObjectChange change;
            try
            {
                change = session.AttachObject(currentSummary, spot.Tile, spot.ParentId, spot.PointIndex, placementAsset, mapCanvas.PlacementHeading);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("place.status.attach_failed", ("reason", exception.Message));
                return;
            }
            var assetPath = placementAsset;
            placementAttach = null;
            if (!keepPlacing)
                StopPlacement();
            PushHistory(new ObjectEditStep { AttachedChange = change });
            RefreshAfterChange(null);
            SetStatusT("place.status.hung", ("name", Path.GetFileNameWithoutExtension(assetPath)), ("id", change.Added[0].Value.Id), ("n", spot.PointIndex + 1), ("parent", spot.ParentId));
        }

        /// <summary>Takes an attached object off its parent, with what hangs on it, as one undo step.</summary>
        private void DeleteAttachedObject(OmsiTileSummary tile, OmsiAttachedObjectRecord record)
        {
            if (!CanEdit)
                return;
            OmsiAttachedObjectDeletionResult result;
            try
            {
                result = session.DeleteAttachedObject(currentSummary, tile, record);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("place.status.detach_failed", ("reason", exception.Message));
                return;
            }
            if (!result.Succeeded)
            {
                SetStatus(Loc.T("place.status.detach_stays", ("refs", DescribeReferences(result.Blockers))));
                return;
            }
            PushHistory(new ObjectEditStep { AttachedChange = result.Change });
            RefreshAfterChange(null);
            SetStatusT("place.status.took_off", ("name", Path.GetFileNameWithoutExtension(record.AssetPath)), ("id", record.Id), ("others", Math.Max(0, result.Change.Removed.Count - 1)));
        }

        private void ShowSnapMessage(string message)
        {
            if (message == placementSnapMessage)
                return;
            placementSnapMessage = message;
            if (message != null)
                SetStatus(message);
        }

        /// <summary>
        /// Inserts the junction being placed into the road it snapped to: cuts the road, places the object and joins both
        /// sides, as one undo step.
        /// </summary>
        private void InsertChosenJunction(MapPlacement placement)
        {
            var plan = placementPlan;
            if (plan.Problem != null)
            {
                SetStatus(plan.Problem);
                return;
            }
            OmsiJunctionInsertionResult result;
            try
            {
                result = session.InsertJunction(currentSummary, plan);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuild.status.insert_failed", ("reason", exception.Message));
                return;
            }
            if (!result.Succeeded)
            {
                SetStatus(Loc.T("roadbuild.status.cut_here", ("refs", DescribeReferences(result.Blockers))));
                return;
            }

            var parts = new List<ObjectEditStep>();
            foreach (var step in result.Steps)
            {
                if (step is OmsiSplineChange splineChange)
                {
                    parts.Add(new ObjectEditStep { SplineChange = splineChange });
                    RefreshSplineViews(splineChange);
                }
                else if (step is OmsiObjectChange objectChange)
                    parts.Add(new ObjectEditStep { TileX = objectChange.Tile.X, TileY = objectChange.Tile.Y, ObjectId = objectChange.Record.Id, Change = objectChange });
            }
            PushHistory(new ObjectEditStep { Group = parts, Description = Loc.T("place.step.junction", ("id", result.Junction.Id)) });

            var assetPath = placementAsset;
            if (!placement.KeepPlacing)
                StopPlacement();
            var tile = currentSummary.Tiles.Find(candidate => candidate.Objects.Contains(result.Junction));
            RefreshAfterChange(new[] { new MapSelectionItem { Tile = tile, Record = result.Junction } });
            if (objectsPanelOpen)
                UpdateObjectsCatalog();
            SetStatusT("roadbuild.status.junction_inserted", ("id", result.Junction.Id), ("path", assetPath));
        }

        private void StopPlacement()
        {
            placementPlan = null;
            placementSnapMessage = null;
            placementAsset = null;
            placingEntryName = null;
            clipboardPlacing = false;
            mapCanvas?.EndPlacement();
            objectsPanel?.ClearChosen();
            UpdateToolbar();
        }

        private void ApplyObjectEdit(OmsiObjectRecord item, TextField xField, TextField yField, TextField zField, TextField headingField,
            TextField pitchField, TextField bankField)
        {
            if (selectedTile == null)
                return;
            if (!TryParseField(xField, Loc.T("inspector.name.x"), out var x) || !TryParseField(yField, Loc.T("inspector.name.y"), out var y) ||
                !TryParseField(zField, Loc.T("inspector.name.height"), out var z) || !TryParseField(headingField, Loc.T("inspector.name.heading"), out var heading) ||
                !TryParseField(pitchField, Loc.T("inspector.name.pitch"), out var pitch) || !TryParseField(bankField, Loc.T("inspector.name.bank"), out var bank))
                return;
            CommitEdits(new List<MapObjectEdit>
            {
                new MapObjectEdit
                {
                    Tile = selectedTile, Record = item, LocalX = x, LocalY = y, Height = z, HeadingDegrees = heading,
                    PitchDegrees = pitch, BankDegrees = bank
                }
            });
        }

        /// <summary>Objects that join splines, such as crossings, keep their tilt; tilting them would tear the roads apart.</summary>
        private bool CanTilt(OmsiObjectRecord item)
        {
            return objectRenderer == null || !objectRenderer.HasSplineHelper(item.AssetPath);
        }

        private bool TryParseField(TextField field, string name, out double value)
        {
            var text = (field.value ?? string.Empty).Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.IsNaN(value) && !double.IsInfinity(value))
                return true;
            SetStatusT("inspector.status.not_number", ("name", name), ("value", field.value));
            return false;
        }

        private void RevertSelectedTile()
        {
            if (selectedTile == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.revert_tiles"));
                return;
            }
            if (session.IsTileAdded(selectedTile.FileName))
            {
                SetStatusT("tile.status.added_here", ("file", selectedTile.FileName));
                return;
            }
            var tile = selectedTile;
            // A terrain stroke still held has saved the heights it showed; it becomes its own step before the files go back.
            if (activeTool == EditorTool.Terrain)
                ReleaseTerrain();
            OmsiTileRevertChange change;
            try
            {
                change = session.RevertTile(currentSummary, tile.FileName);
            }
            catch (OmsiRollBackFailedException exception)
            {
                // Part of the revert may have stayed in the files: the map is read again, then the reason is shown.
                Debug.LogException(exception);
                if (ReloadAfterFailedRollBack())
                    SetStatusT("tile.status.revert_failed", ("file", tile.FileName), ("reason", exception.Message));
                else
                    SetStatusT("history.rollback_failed_reload_failed", ("reason", exception.Message));
                return;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("tile.status.revert_failed", ("file", tile.FileName), ("reason", exception.Message));
                return;
            }
            if (change == null)
            {
                SetStatusT("tile.status.revert_nothing", ("file", tile.FileName));
                return;
            }
            // The step keeps the history before it: the tile's records were swapped in place, the tile summaries stay.
            PushHistory(new ObjectEditStep { TileRevert = change });
            RefreshAfterTileRevert(change);
            SetStatusT("tile.status.reverted", ("file", tile.FileName));
        }

        private void OpenExportDialog()
        {
            if (session == null)
            {
                SetStatusT("export.status.open_first");
                return;
            }
            if (!session.CanEdit)
            {
                SetStatusT("export.status.save_first");
                return;
            }
            if (exportDialog == null || exportFolderField == null || exportNameField == null)
                return;

            var displayName = string.IsNullOrWhiteSpace(currentSummary.FriendlyName) ? currentSummary.Name : currentSummary.FriendlyName;
            // A map made with MapWorks goes back into its own folder under its own name (ADR 052).
            exportFolderField.SetValueWithoutNotify(Path.GetFileName(OmsiMapExporter.SuggestTargetFolder(session.Workspace)));
            exportNameField.SetValueWithoutNotify(ExportsInPlace() ? displayName : displayName + " (MapWorks)");
            UpdateExportTarget();
            exportDialog.style.display = DisplayStyle.Flex;
            if (chronoDate != null)
                SetStatusT("export.status.chrono_note");
        }

        /// <summary>
        /// Opens the dialog for a new map. The template is the given map, else the open map, else the map opened last;
        /// the user can choose another one.
        /// </summary>
        private void OpenNewMapDialog(string templateGlobalConfigPath)
        {
            if (newMapDialog == null || newMapForm == null)
                return;
            var templatePath = templateGlobalConfigPath ?? currentSummary?.GlobalConfigPath;
            if (templatePath == null)
                foreach (var entry in RecentFiles.Load())
                {
                    templatePath = entry.IsProject ? ProjectMap(entry.Path) : entry.Path;
                    if (templatePath != null && File.Exists(templatePath))
                        break;
                    templatePath = null;
                }
            newMapForm.Open(templatePath);
            newMapDialog.style.display = DisplayStyle.Flex;
        }

        /// <summary>The global.cfg a project file points to, or null when it cannot be read.</summary>
        private static string ProjectMap(string projectPath)
        {
            try
            {
                return File.Exists(projectPath) ? JsonUtility.FromJson<MapforgeProject>(File.ReadAllText(projectPath))?.globalConfigPath : null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates the map and opens it for editing with the project MapWorks keeps for its own maps (ADR 052), without
        /// asking where to save one.
        /// </summary>
        private void CreateNewMap(string templatePath, string folderName, string friendlyName, OmsiNewMapOptions options)
        {
            OmsiNewMapReport report;
            try
            {
                report = OmsiNewMap.Create(templatePath, folderName, friendlyName, options, DateTime.Now);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("export.status.create_failed", ("reason", exception.Message));
                return;
            }
            HidePopup(newMapDialog);

            var failure = Loc.T("export.created_open_prefix", ("folder", report.MapFolder));
            try
            {
                OpenOwnMap(report.GlobalConfigPath, folderName, failure, () =>
                    SetStatusT("export.status.created", ("folder", report.MapFolder), ("count", report.CopiedFiles.Count), ("name", friendlyName)));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus(failure + exception.Message);
            }
        }

        private string ExportTargetFolder(out string problem)
        {
            problem = null;
            var folderName = (exportFolderField.value ?? string.Empty).Trim();
            if (folderName.Length == 0 || folderName == "." || folderName == ".." ||
                folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                problem = Loc.T("export.problem.folder");
                return null;
            }
            if (string.IsNullOrWhiteSpace(exportNameField.value))
                problem = Loc.T("export.problem.name");
            return Path.Combine(Path.GetDirectoryName(session.Workspace.RootPath), folderName);
        }

        private void UpdateExportTarget()
        {
            if (session == null || !session.CanEdit || exportTargetLabel == null)
                return;

            var target = ExportTargetFolder(out var problem);
            if (problem == null && !OmsiMapExporter.CanWriteTo(session.Workspace, target, out var reason))
                problem = reason;
            var editedCount = session.ModifiedFiles().Count;
            exportTargetLabel.text = problem ?? Loc.T("export.target", ("kind", OmsiMapExporter.IsInPlace(session.Workspace, target) ? "inplace" : Directory.Exists(target) ? "replace" : "create"),
                                         ("target", target), ("count", editedCount));
            exportTargetLabel.EnableInClassList("export-problem", problem != null);
            exportConfirmButton?.SetEnabled(problem == null);
        }

        private void ExportMap()
        {
            var target = ExportTargetFolder(out var problem);
            if (problem != null)
            {
                SetStatus(problem);
                return;
            }

            try
            {
                var report = OmsiMapExporter.Export(session.Workspace, target, exportNameField.value);
                HidePopup(exportDialog);
                if (report.InPlace)
                {
                    // The map holds the edits now and the workspace is empty again; what the view shows stays the same.
                    RefreshAfterChange(null);
                    SetStatusT("export.status.wrote", ("count", report.EditedFiles.Count), ("folder", report.TargetFolder), ("name", exportNameField.value.Trim()));
                    return;
                }
                SetStatusT("export.status.exported", ("folder", report.TargetFolder), ("files", report.CopiedFileCount), ("edited", report.EditedFiles.Count), ("name", exportNameField.value.Trim()));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatusT("export.status.failed", ("reason", exception.Message));
            }
        }

        private void UpdateMapStateLabel()
        {
            UpdateProjectModeBadge();
            if (mapStateLabel == null || currentSummary == null)
                return;

            var mapName = string.IsNullOrWhiteSpace(currentSummary.FriendlyName)
                ? currentSummary.Name
                : currentSummary.FriendlyName;
            var mode = EditingVariant
                ? Loc.T("status.mode.variant", ("title", editedVariant.Title), ("count", session.ModifiedFiles().Count))
                : chronoDate != null
                ? Loc.T("status.mode.chrono", ("date", chronoDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                : CanEdit
                    ? Loc.T("status.mode.edited", ("count", session.ModifiedFiles().Count))
                    : Loc.T("status.mode.readonly");
            mapStateLabel.text = Loc.T("status.map", ("kind", string.IsNullOrWhiteSpace(currentProjectName) ? "map" : "project"), ("name", string.IsNullOrWhiteSpace(currentProjectName) ? mapName : currentProjectName),
                ("tiles", currentSummary.Tiles.Count), ("mode", mode));
            // The pill's tooltip carries the whole line, with the number of edited files; it is written again on a language change.
            if (projectPill != null)
                projectPill.tooltip = mapStateLabel.text;
        }

        private static string SanitizeFileName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var characters = value.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
                if (Array.IndexOf(invalidCharacters, characters[index]) >= 0)
                    characters[index] = '_';
            var result = new string(characters).Trim();
            return string.IsNullOrWhiteSpace(result) ? "OMSI Map" : result;
        }

        private static void TogglePopup(VisualElement popup, params VisualElement[] otherPopups)
        {
            if (popup == null)
                return;

            var show = popup.resolvedStyle.display == DisplayStyle.None;
            foreach (var otherPopup in otherPopups)
                HidePopup(otherPopup);
            popup.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static bool IsShown(VisualElement popup)
        {
            return OverlayRegistry.IsShown(popup);
        }

        private static void HidePopup(VisualElement popup)
        {
            OverlayRegistry.Hide(popup);
        }

        /// <summary>
        /// Ends what waits for one click and would act on a record of the view shown: a new type for an object or a spline, a variable
        /// parent, the object of a road type's row; and closes the context menu. Each ends as its Cancel does; nothing is written.
        /// </summary>
        private void LeaveTransientModes()
        {
            CloseContextMenu();
            EndElementTypePick(null);
            EndSplineTypePick(null);
            EndVarParentPick(false);
            EndRowObjectPick(null);
        }

        /// <summary>Makes a tool active; choosing one ends placing. Without a project only selecting is possible.</summary>
        private void SetTool(EditorTool tool)
        {
            if (currentSummary == null)
                return;
            var entry = ToolEntryOf(tool);
            if (!EditorToolRules.IsAllowed(tool, CanEdit, CanEditObjects))
            {
                SetStatus(ReadOnlyMessage(EditorToolRules.ReadOnlyKey(tool)));
                return;
            }
            // Another tool ends placing and the picks waiting for a click, and closes the object library, the list above the bottom bar and the
            // context menu.
            if (tool != activeTool)
            {
                ShowBarPopup(false);
                LeaveTransientModes();
            }
            EndSplineTypePick(null);
            ShowObjectsPanel(false);
            var previous = activeTool;
            // The end methods read activeTool: it names the new tool before the old one ends.
            activeTool = tool;
            if (mapCanvas != null)
                mapCanvas.Tool = tool;
            // The old tool ends before the new one begins, as both use the map's road mode.
            if (previous != tool)
                ToolEntryOf(previous).End?.Invoke();
            var again = previous == tool;
            var beginsAgain = EditorToolRules.BeginsAgain(tool);
            if (entry.Begin != null && (!again || beginsAgain))
                entry.Begin();
            // Choosing the tool that is on again keeps its panel in view, which closing the object library hid above.
            if (objectsPanelHost != null && again && entry.Begin != null && !beginsAgain)
                objectsPanelHost.style.display = DisplayStyle.Flex;
            UpdateToolbar();
            // Only Drag, Move and Rotate write a status here; the tools with a mode of their own write theirs when they begin.
            if (!EditorToolRules.IsSelectionTool(tool))
                return;
            SetStatusT(tool == EditorTool.Move ? "tool.status.move" : tool == EditorTool.Rotate ? "tool.status.rotate" : "tool.status.drag") /* i18n:keys */;
        }

        private void UpdateToolbar()
        {
            toolbar?.ShowState(activeTool, placementAsset != null || objectsPanelOpen, CanEdit, CanEditObjects, selection.Count > 0);
        }

        /// <summary>
        /// Gives a button a Lucide icon and, if given, a label next to it; a button draws its own text over its children,
        /// so the text moves into a label.
        /// </summary>
        private static void DecorateButton(Button button, string icon, string labelKey)
        {
            if (button == null)
                return;
            button.text = string.Empty;
            button.Add(new IconElement(icon));
            if (labelKey != null)
            {
                var label = LocBindings.NewLabel(labelKey); // i18n:keys
                label.pickingMode = PickingMode.Ignore;
                button.Add(label);
            }
        }

        /// <summary>Puts an accent icon before the title of a floating panel.</summary>
        private static void AddPanelIcon(VisualElement frame, string icon)
        {
            var header = frame?.Q(className: "floating-panel-header");
            if (header == null)
                return;
            var element = new IconElement(icon);
            element.AddToClassList("floating-panel-icon");
            header.Insert(0, element);
        }

        /// <summary>
        /// The map area that the inspector and the bottom bar leave free, in pixels from each edge, so fitting the map shows it
        /// there and not under a panel.
        /// </summary>
        private Vector4 FreeMapInsets()
        {
            if (mapCanvas == null)
                return Vector4.zero;
            var canvas = mapCanvas.worldBound;
            var right = inspectorFrame != null && inspectorShown ? canvas.xMax - inspectorFrame.worldBound.xMin : 0f;
            var bottom = bottomBar != null && bottomBar.resolvedStyle.display != DisplayStyle.None ? canvas.yMax - bottomBar.worldBound.yMin : 0f;
            // Before the first layout the bounds are not known yet.
            float Clean(float value) => float.IsNaN(value) ? 0f : Mathf.Max(0f, value);
            return new Vector4(0f, 0f, Clean(right), Clean(bottom));
        }

        /// <summary>
        /// Runs the map check in the background and shows its report: each topic with its outcome and findings, the
        /// tile worth looking at first, and the objects that belong to a DLC.
        /// </summary>
        private void ValidateLoadedMap()
        {
            if (currentSummary == null || session == null)
            {
                SetStatusT("check.status.open_first");
                return;
            }
            if (checkTask != null)
            {
                if (checkDialog != null)
                    checkDialog.style.display = DisplayStyle.Flex;
                return;
            }

            // The check covers the map as it is stored; a chrono view shows variants laid over it.
            var summary = baseSummary ?? currentSummary;
            var source = session.Source;
            var installationRoot = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var configName = Path.GetFileName(session.GlobalConfigPath);
            checkSections?.Clear();
            if (checkSummaryLabel != null)
                checkSummaryLabel.text = Loc.T("check.running", ("name", string.IsNullOrWhiteSpace(summary.FriendlyName) ? summary.Name : summary.FriendlyName), ("noChrono", chronoDate != null ? "yes" : "no"));
            if (checkDialog != null)
                checkDialog.style.display = DisplayStyle.Flex;
            SetStatusT("check.status.checking");
            // The objects made for the project are looked for alongside, reading every map of the installation.
            OmsiMadeObjectScan made = null;
            List<OmsiChainOffsetFinding> stale = null;
            var keep = CanEdit ? MadeObjectsStillNeeded() : null;
            var projectName = currentProjectName;
            var workspaceFolder = session.Workspace?.WorkspacePath;
            var task = Task.Run(() =>
            {
                var result = OmsiMapCheck.Run(summary, source, installationRoot, configName);
                stale = OmsiMapSession.StaleChainOffsets(summary);
                if (keep != null)
                    made = OmsiMadeObjects.Find(installationRoot, projectName, workspaceFolder, keep);
                return result;
            });
            checkTask = task;
            UpdateToolbar();
            IVisualElementScheduledItem poll = null;
            poll = rootElement.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                checkTask = null;
                UpdateToolbar();
                if (task.IsFaulted || !ReferenceEquals(summary, baseSummary ?? currentSummary))
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogException(task.Exception);
                        SetStatusT("check.status.failed", ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    }
                    HidePopup(checkDialog);
                    return;
                }
                var report = task.Result;
                report.Sections.Add(DlcSection());
                madeObjects = made;
                madeObjectsSection = made == null ? null : MadeObjectsSection(made);
                if (madeObjectsSection != null)
                    report.Sections.Add(madeObjectsSection);
                staleChainOffsets = stale;
                chainOffsetsSection = ChainOffsetsSection(stale);
                report.Sections.Add(chainOffsetsSection);
                lastCheck = report;
                ShowCheckReport(report);
            }).Every(100);
        }

        private void ShowCheckReport(OmsiMapCheckReport report)
        {
            if (checkSections == null || checkSummaryLabel == null)
                return;
            var problems = report.ProblemCount;
            checkSummaryLabel.text = problems == 0 ? Loc.T("check.summary.ok", ("count", report.Sections.Count)) : Loc.T("check.summary.problems", ("problems", problems), ("count", report.Sections.Count));
            SetStatusT("check.status.done", ("problems", problems));
            checkSections.Clear();
            foreach (var section in report.Sections)
            {
                var box = new VisualElement { userData = section.Key };
                box.AddToClassList("check-section");
                var header = new VisualElement();
                header.AddToClassList("check-section-header");
                var status = new Label(Loc.T(section.Status == OmsiCheckStatus.Passed ? "check.badge.ok" : section.Status == OmsiCheckStatus.Note ? "check.badge.note" : "check.badge.problem")) /* i18n:keys */;
                status.AddToClassList("check-status");
                status.AddToClassList(section.Status == OmsiCheckStatus.Passed ? "check-status-passed"
                    : section.Status == OmsiCheckStatus.Note ? "check-status-note" : "check-status-problem");
                header.Add(status);
                var title = new Label(section.Title);
                title.AddToClassList("check-section-title");
                header.Add(title);
                if (section.Tile != null)
                {
                    var tile = currentSummary.Tiles.Find(candidate => candidate.X == section.Tile.X && candidate.Y == section.Tile.Y);
                    var show = new Button(() =>
                    {
                        HidePopup(checkDialog);
                        if (tile != null)
                            SelectTile(tile, true);
                    }) { text = Loc.T("check.show_tile", ("x", section.Tile.X), ("y", section.Tile.Y))};
                    show.AddToClassList("map-control-button");
                    show.AddToClassList("check-section-button");
                    header.Add(show);
                }
                if (ReferenceEquals(section, madeObjectsSection) && madeObjects != null && madeObjects.Unused.Count > 0)
                    header.Add(DeleteMadeObjectsButton());
                if (ReferenceEquals(section, chainOffsetsSection) && staleChainOffsets != null && staleChainOffsets.Count > 0 && CanEdit && chronoDate == null)
                    header.Add(StoreChainOffsetsButton());
                box.Add(header);
                var summaryLabel = new Label(section.Summary);
                summaryLabel.AddToClassList("check-section-summary");
                box.Add(summaryLabel);
                AddFindings(box, section.Findings, 6);
                checkSections.Add(box);
            }
        }

        /// <summary>Lists the first findings and a button that shows the rest.</summary>
        private static void AddFindings(VisualElement box, List<string> findings, int shown)
        {
            var list = new VisualElement();
            box.Add(list);
            void Fill(int count)
            {
                list.Clear();
                for (var index = 0; index < Math.Min(count, findings.Count); index++)
                {
                    var finding = new Label("• " + findings[index]);
                    finding.AddToClassList("check-finding");
                    list.Add(finding);
                }
                if (findings.Count <= count)
                    return;
                var more = new Button(() => Fill(int.MaxValue)) { text = Loc.T("check.show_all", ("count", findings.Count))};
                more.AddToClassList("map-control-button");
                more.AddToClassList("check-more-button");
                list.Add(more);
            }
            Fill(shown);
        }

        /// <summary>
        /// The topic of the objects made for the project under Sceneryobjects\Mapforge (ADR 026): those that no map of the
        /// installation, no export and nothing undo could bring back names any more, such as junctions made again.
        /// </summary>
        private OmsiCheckSection MadeObjectsSection(OmsiMadeObjectScan scan)
        {
            var section = new OmsiCheckSection("Made objects", Loc.T("mapcheck.title.made")) { Status = OmsiCheckStatus.Passed };
            var unused = scan.Unused;
            var folder = OmsiGeneratedObjectStore.Folder + "\\" + Path.GetFileName(scan.ProjectFolder);
            if (scan.Objects.Count == 0)
                section.Summary = Loc.T("mapcheck.made.none", ("folder", folder));
            else if (unused.Count == 0)
                section.Summary = Loc.T("mapcheck.made.all_used", ("count", scan.Objects.Count), ("folder", folder));
            else
            {
                section.Status = OmsiCheckStatus.Note;
                section.Summary = Loc.T("mapcheck.made.unused", ("unused", unused.Count), ("count", scan.Objects.Count), ("folder", folder), ("size", Megabytes(scan.UnusedBytes)));
                foreach (var item in unused)
                    section.Findings.Add(Loc.T("mapcheck.made.finding", ("name", item.Name), ("size", Megabytes(item.Bytes))));
            }
            return section;
        }

        /// <summary>
        /// The topic of the splines whose stored chain offset is not where their chain puts them. openOMSI counts the rows
        /// along a spline from that value and runs its textures on from it; earlier versions of this editor wrote 0.
        /// </summary>
        private static OmsiCheckSection ChainOffsetsSection(List<OmsiChainOffsetFinding> stale)
        {
            var section = new OmsiCheckSection("Chain offsets", Loc.T("mapcheck.title.chain")) { Status = OmsiCheckStatus.Passed };
            if (stale.Count == 0)
            {
                section.Summary = Loc.T("mapcheck.chain.ok");
                return section;
            }
            section.Status = OmsiCheckStatus.Note;
            section.Summary = Loc.T("mapcheck.chain.stale", ("count", stale.Count));
            foreach (var item in stale)
                section.Findings.Add(Loc.T("mapcheck.chain.finding", ("id", item.Spline.Id), ("x", item.Tile.X), ("y", item.Tile.Y),
                    ("stored", item.Spline.ChainOffset.GetValueOrDefault()), ("start", item.ChainStart)));
            return section;
        }

        /// <summary>Stores the right chain offsets as one step that Ctrl+Z takes back.</summary>
        private Button StoreChainOffsetsButton()
        {
            var store = new Button(() =>
            {
                OmsiSplineChange change;
                try
                {
                    change = session.StoreChainOffsets(currentSummary);
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
                {
                    Debug.LogException(exception);
                    SetStatusT("chain.status.failed", ("reason", exception.Message));
                    return;
                }
                var count = staleChainOffsets.Count;
                if (change != null)
                {
                    PushHistory(new ObjectEditStep { SplineChange = change, Description = Loc.T("chain.step", ("count", count)) });
                    RefreshSplineViews(change);
                }
                staleChainOffsets = OmsiMapSession.StaleChainOffsets(currentSummary);
                chainOffsetsSection.Status = staleChainOffsets.Count == 0 ? OmsiCheckStatus.Passed : OmsiCheckStatus.Note;
                chainOffsetsSection.Summary = Loc.T("mapcheck.chain.stored", ("count", count), ("differ", staleChainOffsets.Count));
                chainOffsetsSection.Findings.Clear();
                if (lastCheck != null)
                    ShowCheckReport(lastCheck);
                SetStatusT("chain.status.stored", ("count", count));
            })
            {
                text = Loc.T("chain.store"),
                tooltip = Loc.T("chain.store.tooltip")};
            store.AddToClassList("map-control-button");
            store.AddToClassList("check-section-button");
            return store;
        }

        /// <summary>The objects made here that the open project could still bring back: by undo or redo, or while placing one.</summary>
        private List<string> MadeObjectsStillNeeded()
        {
            var keep = new List<string>();
            if (placementAsset != null)
                keep.Add(placementAsset);
            void Collect(ObjectEditStep step)
            {
                if (step.Group != null)
                    foreach (var part in step.Group)
                        Collect(part);
                if (step.Change != null)
                {
                    keep.Add(step.Change.Record.AssetPath);
                    if (step.Change.AssetBefore != null)
                        keep.Add(step.Change.AssetBefore);
                    if (step.Change.AssetAfter != null)
                        keep.Add(step.Change.AssetAfter);
                }
                if (step.TileChange != null)
                    keep.AddRange(step.TileChange.TileTexts());
                if (step.TileRevert != null)
                    keep.AddRange(step.TileRevert.TileTexts());
            }
            foreach (var step in undoSteps)
                Collect(step);
            foreach (var step in redoSteps)
                Collect(step);
            return keep;
        }

        /// <summary>Deletes the unused made objects on a second click, as it cannot be taken back.</summary>
        private Button DeleteMadeObjectsButton()
        {
            var armed = false;
            Button delete = null;
            delete = new Button(() =>
            {
                if (!armed)
                {
                    armed = true;
                    delete.text = Loc.T("made.click_again", ("count", madeObjects.Unused.Count));
                    delete.schedule.Execute(() =>
                    {
                        armed = false;
                        delete.text = Loc.T("made.delete");
                    }).StartingIn(4000);
                    return;
                }
                var problems = new List<string>();
                List<string> deleted;
                try
                {
                    deleted = OmsiMadeObjects.DeleteUnused(madeObjects, problems);
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogException(exception);
                    SetStatusT("made.status.failed", ("reason", exception.Message));
                    return;
                }
                madeObjects.Objects.RemoveAll(item => deleted.Contains(item.Name));
                madeObjectsSection.Status = problems.Count == 0 ? OmsiCheckStatus.Passed : OmsiCheckStatus.Note;
                madeObjectsSection.Summary = Loc.T("mapcheck.made.deleted", ("count", deleted.Count),
                    ("folder", OmsiGeneratedObjectStore.Folder + "\\" + Path.GetFileName(madeObjects.ProjectFolder)), ("problems", problems.Count));
                madeObjectsSection.Findings.Clear();
                madeObjectsSection.Findings.AddRange(problems);
                if (lastCheck != null)
                    ShowCheckReport(lastCheck);
                StartInstallationScan(true);
                SetStatusT("made.status.deleted", ("count", deleted.Count), ("hasProblem", problems.Count > 0 ? "yes" : "no"), ("problem", problems.Count > 0 ? problems[0] : string.Empty));
            })
            {
                text = Loc.T("made.delete"),
                tooltip = Loc.T("made.delete.tooltip")};
            delete.AddToClassList("map-control-button");
            delete.AddToClassList("check-section-button");
            delete.AddToClassList("inspector-edit-delete");
            return delete;
        }

        private static string Megabytes(long bytes)
        {
            return Loc.T("mapcheck.made.size", ("size", bytes / 1048576.0));
        }

        private OmsiCheckSection DlcSection()
        {
            var section = new OmsiCheckSection("DLC objects", Loc.T("mapcheck.title.dlc")) { Summary = DescribeDlcUse(out var dlcNone) };
            section.Status = dlcNone ? OmsiCheckStatus.Passed : OmsiCheckStatus.Note;
            return section;
        }

        /// <summary>
        /// Which objects of the map, also attached ones and rows along splines, belong to a DLC; players without it
        /// do not see them.
        /// </summary>
        private string DescribeDlcUse(out bool none)
        {
            none = false;
            if (contentOrigins == null)
                return Loc.T("mapcheck.dlc.reading");
            if (!contentOrigins.FromSteam && !contentOrigins.HasMarkedFolders)
                return Loc.T("mapcheck.dlc.unknown");
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var placements = 0;
            void Count(string assetPath)
            {
                if (contentOrigins.Of(assetPath) != OmsiContentOrigin.Dlc)
                    return;
                kinds.Add(assetPath);
                placements++;
            }
            foreach (var tile in currentSummary.Tiles)
            {
                foreach (var item in tile.Objects)
                    Count(item.AssetPath);
                foreach (var attached in tile.AttachedObjects)
                    Count(attached.AssetPath);
                foreach (var row in tile.SplineAttachments)
                    Count(row.AssetPath);
            }
            none = kinds.Count == 0;
            return none
                ? Loc.T("mapcheck.dlc.none")
                : Loc.T("mapcheck.dlc.some", ("kinds", kinds.Count), ("placements", placements), ("example", Path.GetFileName(First(kinds))));
        }

        private static string First(IEnumerable<string> items)
        {
            foreach (var item in items)
                return item;
            return string.Empty;
        }

        private void SetStatus(string message)
        {
            statusKey = null;
            statusArguments = null;
            ShowStatusText(message);
        }

        /// <summary>Writes the message; its tooltip holds the whole text, as the one line of the status bar cuts long messages off.</summary>
        private void ShowStatusText(string message)
        {
            if (statusLabel == null)
                return;
            statusLabel.text = OneLine(message);
            statusLabel.tooltip = message;
        }

        /// <summary>
        /// Shows a message from the language files. Unlike <see cref="SetStatus"/> it is written again when the language changes, as
        /// long as nothing else has replaced it.
        /// </summary>
        private void SetStatusT(string key, params (string Name, object Value)[] arguments) // i18n:keys
        {
            statusKey = key;
            statusArguments = arguments;
            ShowStatusText(Loc.T(key, arguments)); // i18n:keys
        }

        /// <summary>Writes the message of <see cref="SetStatusT"/> again in the language now in use.</summary>
        private void RefreshStatusText()
        {
            if (statusKey != null)
                ShowStatusText(Loc.T(statusKey, statusArguments)); // i18n:keys
        }

        private static void CreateBackgroundCamera()
        {
            var cameraObject = new GameObject("Editor Background Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            // The colour of the viewport around the map view, which is transparent so the ground camera shows.
            camera.backgroundColor = new Color(0.078f, 0.094f, 0.114f, 1f);
            camera.cullingMask = 0;
            camera.depth = -100f;
            camera.useOcclusionCulling = false;
        }

        /// <summary>Toggles the selection style of a tile list entry without rebuilding the list.</summary>
        private void SetTileHighlight(OmsiTileSummary tile, bool isSelected)
        {
            if (tile != null && browserEntries.TryGetValue(tile, out var entry))
                entry.EnableInClassList("browser-tile-entry-selected", isSelected);
        }

        private void ScrollBrowserTo(OmsiTileSummary tile)
        {
            if (!browserEntries.TryGetValue(tile, out var entry))
                return;
            var tileList = entry.GetFirstAncestorOfType<ScrollView>();
            tileList?.schedule.Execute(() =>
            {
                // The list may have been rebuilt meanwhile; only scroll to an entry that is still part of it.
                if (tileList.contentContainer.Contains(entry))
                    tileList.ScrollTo(entry);
            });
        }

        private static MapforgeEditorSettings LoadSettings()
        {
            var settings = new MapforgeEditorSettings();
            var asset = Resources.Load<TextAsset>(MapforgeEditorSettings.ResourceName);
            if (asset == null)
            {
                Debug.LogError(MapforgeEditorSettings.ResourceName + ".json was not found in Resources; using built-in defaults.");
                return settings;
            }

            try
            {
                JsonUtility.FromJsonOverwrite(asset.text, settings);
                settings.Validate();
                return settings;
            }
            catch (Exception exception)
            {
                Debug.LogError("Invalid " + MapforgeEditorSettings.ResourceName + ".json, using built-in defaults: " + exception.Message);
                return new MapforgeEditorSettings();
            }
        }

        private void FitMap()
        {
            mapCanvas?.FitToMap();
            UpdateZoomLabel();
            SetStatusT("map.status.fitted");
        }

        private void ChangeZoom(float factor)
        {
            if (currentSummary == null || mapCanvas == null)
                return;
            mapCanvas.Zoom(factor);
            UpdateZoomLabel();
        }

        private void CenterSelection()
        {
            if (selectedTile == null || mapCanvas == null)
                return;
            mapCanvas.CenterOn(selectedTile, selectedObject);
            UpdateZoomLabel();
            SetStatus(selectedObject != null ? Loc.T("map.status.centered_object", ("id", selectedObject.Id)) : Loc.T("map.status.centered_tile", ("x", selectedTile.X), ("y", selectedTile.Y)));
        }

        private void UpdateMapHeader()
        {
            if (currentSummary == null)
                return;

            var displayName = string.IsNullOrEmpty(currentSummary.FriendlyName)
                ? currentSummary.Name
                : currentSummary.FriendlyName;
            if (mapTitleLabel != null)
                mapTitleLabel.text = displayName;
            UpdateCounts();
            if (mapHintLabel != null)
            {
                var view = mapCanvas != null && mapCanvas.ThreeDimensional
                    ? Loc.T("map.hint.view3d")
                    : Loc.T("map.hint.top");
                mapHintLabel.text = view + Loc.T(CanEdit ? "map.hint.edit" : EditingVariant ? "map.hint.variant" : chronoDate != null ? "map.hint.chrono" : "map.hint.readonly", ("title", EditingVariant ? editedVariant.Title : string.Empty)) /* i18n:keys */;
                mapHintLabel.style.display = DisplayStyle.Flex;
            }
            SetBarContentVisible(true);
            UpdateBarButtons();
            UpdateToolbar();
            // The object library and the map check need the installation's objects and their origin.
            if (objectsPanel != null)
                StartInstallationScan();
            if (emptyState != null)
                emptyState.style.display = DisplayStyle.None;
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel()
        {
            if (zoomLabel == null || mapCanvas == null)
                return;
            var metersPerPixel = mapCanvas.MetersPerPixel;
            var text = mapCanvas.ThreeDimensional
                ? mapCanvas.OrbitDistance.ToString("0") + " m"
                : metersPerPixel >= 1f
                    ? metersPerPixel.ToString("0.#") + " m/px"
                    : (1f / metersPerPixel).ToString("0.#") + " px/m";
            if (zoomLabel.text != text)
                zoomLabel.text = text;
        }

        /// <summary>
        /// Draws the popup of the bottom bar (ADR 098) for the list chosen, <see cref="projectTab"/>; with the popup closed only the
        /// buttons of the bar are brought up to date. Drawing never opens the popup: that is <see cref="ShowProjectTab"/>'s job.
        /// </summary>
        private void RenderProjectBrowser()
        {
            UpdateBarButtons();
            if (projectPanel == null)
                return;

            projectPanel.Clear();
            browserEntries.Clear();
            lineEntries.Clear();
            if (currentSummary == null && barPopupOpen)
                ShowBarPopup(false);
            if (!barPopupOpen)
                return;

            // The list chosen stays through every redraw; one that has nothing to show now falls back to the tiles.
            if (!ProjectTabAvailable(projectTab))
                projectTab = ProjectTab.Tiles;
            if (barPopupTitle != null)
                LocBindings.Text(barPopupTitle, ProjectTabTitleKey(projectTab)); // i18n:keys
            var leftSide = projectTab == ProjectTab.Tiles || projectTab == ProjectTab.Map;
            barPopup?.EnableInClassList("bar-popup-right", !leftSide);
            barPopup?.EnableInClassList("bar-popup-tall", projectTab == ProjectTab.Tiles);
            switch (projectTab)
            {
                case ProjectTab.Map:
                    projectPanel.Add(MapTab());
                    break;
                case ProjectTab.Entries:
                    projectPanel.Add(ProjectTabPage(EntriesSection()));
                    break;
                case ProjectTab.Chrono:
                    projectPanel.Add(ProjectTabPage(ChronoSection()));
                    break;
                case ProjectTab.Lines:
                    projectPanel.Add(ProjectTabPage(LinesSection()));
                    break;
                case ProjectTab.Paths:
                    projectPanel.Add(ProjectTabPage(PathsSection()));
                    break;
                default:
                    projectPanel.Add(TilesTab());
                    break;
            }
        }

        /// <summary>
        /// The chrono variants with their dates, a date to show the map on and the way back to the base map. Clicking a
        /// variant shows its first day; in a project, Edit edits it (ADR 042), New variant makes one and variants made in the
        /// project can be deleted.
        /// </summary>
        private VisualElement ChronoSection()
        {
            var canEditVariants = session != null && session.CanEdit;
            var section = new VisualElement();
            section.AddToClassList("browser-chrono");
            var chronoHeading = new Label(Loc.T("chrono.foldout", ("count", chronoVariants.Count)));
            chronoHeading.AddToClassList("inspector-group-title");
            chronoHeading.style.marginBottom = 4;
            section.Add(chronoHeading);
            var state = new Label(EditingVariant
                ? Loc.T("chrono.note.editing", ("title", editedVariant.Title), ("date", chronoDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                : chronoDate == null
                    ? Loc.T("chrono.note.base", ("edit", canEditVariants ? "yes" : "no"))
                    : Loc.T("chrono.note.date", ("date", chronoDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("count", OmsiChrono.ActiveOn(chronoVariants, chronoDate.Value).Count)));
            state.AddToClassList("browser-chrono-state");
            section.Add(state);

            var row = new VisualElement();
            row.AddToClassList("browser-chrono-row");
            chronoDateField = new TextField { value = (chronoDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            chronoDateField.AddToClassList("browser-chrono-date");
            chronoDateField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    ShowChronoDateFromField();
            });
            row.Add(chronoDateField);
            var show = new Button(ShowChronoDateFromField) { text = Loc.T("chrono.show"), tooltip = Loc.T("chrono.show.tooltip")};
            show.AddToClassList("map-control-button");
            row.Add(show);
            var back = new Button(() => ShowChronoDate(null)) { text = Loc.T("chrono.base"), tooltip = Loc.T("chrono.base.tooltip")};
            back.AddToClassList("map-control-button");
            back.SetEnabled(chronoDate != null);
            row.Add(back);
            section.Add(row);

            var list = new VisualElement();
            foreach (var variant in chronoVariants)
            {
                var captured = variant;
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                var entry = new Button(() =>
                {
                    if (chronoDateField != null)
                        chronoDateField.value = (captured.FirstDay ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    ShowChronoDateFromField();
                })
                {
                    text = variant.ToString(), tooltip = Loc.T("chrono.variant.tooltip", ("folder", variant.Folder), ("hasLines", variant.DeactivatedLines.Count > 0 ? "yes" : "no"), ("lines", string.Join(", ", variant.DeactivatedLines)))};
                entry.AddToClassList("browser-tile-entry");
                entry.AddToClassList("browser-chrono-variant");
                entry.style.flexGrow = 1f;
                entry.style.flexShrink = 1f;
                var edited = EditingVariant && string.Equals(editedVariant.Name, variant.Name, StringComparison.OrdinalIgnoreCase);
                entry.EnableInClassList("browser-tile-entry-selected", edited || !EditingVariant && chronoDate != null && variant.IsActiveOn(chronoDate.Value));
                line.Add(entry);
                if (canEditVariants)
                {
                    var edit = new Button(() => EditChronoVariant(captured))
                    {
                        text = Loc.T(edited ? "chrono.editing" : "chrono.edit") /* i18n:keys */,
                        tooltip = Loc.T("chrono.edit.tooltip")};
                    edit.AddToClassList("map-control-button");
                    edit.SetEnabled(!edited && variant.FirstDay != null);
                    line.Add(edit);
                    if (session.IsMadeInProject(variant))
                    {
                        var delete = new Button(() => DeleteChronoVariant(captured))
                        {
                            text = Loc.T(ReferenceEquals(variantToDelete, captured) ? "chrono.delete_confirm" : "chrono.delete") /* i18n:keys */,
                            tooltip = Loc.T("chrono.delete.tooltip")};
                        delete.AddToClassList("map-control-button");
                        delete.AddToClassList("inspector-edit-delete");
                        delete.SetEnabled(!edited);
                        line.Add(delete);
                    }
                }
                list.Add(line);
            }
            section.Add(list);
            if (EditingVariant)
            {
                section.Add(VariantDatesEditor());
                section.Add(VariantLinesEditor());
            }
            if (canEditVariants)
                section.Add(NewVariantForm());
            return section;
        }

        /// <summary>The New variant button, or while it is open the form for the new variant's name and dates.</summary>
        private VisualElement NewVariantForm()
        {
            var box = new VisualElement();
            box.style.marginTop = 6;
            if (!newVariantFormOpen)
            {
                var open = new Button(() =>
                {
                    newVariantFormOpen = true;
                    RenderProjectBrowser();
                }) { text = Loc.T("chrono.new"), tooltip = Loc.T("chrono.new.tooltip")};
                open.AddToClassList("map-control-button");
                box.Add(open);
                return box;
            }

            var name = new TextField(Loc.T("chrono.field.name")) { value = "Road works" };
            var start = new TextField(Loc.T("chrono.field.first")) { value = (chronoDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            var end = new TextField(Loc.T("chrono.field.last")) { value = string.Empty, tooltip = Loc.T("chrono.field.last.tooltip")};
            foreach (var field in new[] { name, start, end })
            {
                field.AddToClassList("browser-chrono-date");
                box.Add(field);
            }
            var buttons = new VisualElement();
            buttons.AddToClassList("browser-chrono-row");
            var create = new Button(() =>
            {
                if (!TryParseDay(start.value, out var first))
                {
                    SetStatusT("chrono.status.first_day");
                    return;
                }
                DateTime? last = null;
                if (!string.IsNullOrWhiteSpace(end.value))
                {
                    if (!TryParseDay(end.value, out var lastDay))
                    {
                        SetStatusT("chrono.status.last_day");
                        return;
                    }
                    last = lastDay;
                }
                CreateChronoVariant(name.value, first, last);
            }) { text = Loc.T("chrono.make")};
            create.AddToClassList("map-control-button");
            buttons.Add(create);
            var cancel = new Button(() =>
            {
                newVariantFormOpen = false;
                RenderProjectBrowser();
            }) { text = Loc.T("chrono.cancel")};
            cancel.AddToClassList("map-control-button");
            buttons.Add(cancel);
            box.Add(buttons);
            return box;
        }

        /// <summary>Refreshes the map totals in the header and the project browser after objects were added or removed.</summary>
        private void UpdateCounts()
        {
            if (currentSummary == null)
                return;
            if (browserCountsLabel != null)
                browserCountsLabel.text = Loc.T("project.counts", ("tiles", currentSummary.Tiles.Count), ("objects", currentSummary.ObjectCount), ("splines", currentSummary.SplineCount),
                ("connections", currentSummary.AttachmentCount), ("terrain", currentSummary.TerrainTileCount), ("world", currentSummary.UsesWorldCoordinates ? "yes" : "no"),
                ("size", currentSummary.TileSizeMeters));
        }

        /// <summary>Refreshes one tile list entry, for example its edited marker, without rebuilding the list.</summary>
        private void UpdateBrowserEntry(OmsiTileSummary tile)
        {
            if (!browserEntries.TryGetValue(tile, out var entry))
                return;
            var edited = IsTileEdited(tile);
            entry.text = Loc.T("project.tile", ("x", tile.X), ("y", tile.Y), ("objects", tile.ObjectCount), ("splines", tile.SplineCount), ("edited", edited ? "yes" : "no"));
            entry.EnableInClassList("browser-tile-entry-edited", edited);
        }

        // What the inspector showed when it was last drawn, to keep its scroll position while that stays.
        private string inspectorScrollKey;

        private string InspectorScrollKey()
        {
            return (selectedTile != null ? selectedTile.X + "," + selectedTile.Y : "-") + "|" + (selectedObject?.Id.ToString() ?? "-") + "|" +
                   (selectedSpline?.Id.ToString() ?? "-") + "|" + selection.Count + "|" + inspectorTab;
        }

        private void RenderInspector()
        {
            if (inspectorPanel == null)
                return;

            // Drawn again for the same tile and selection, such as after a click in the traffic rules editor, the inspector
            // keeps where it was scrolled to.
            var scrollKey = InspectorScrollKey();
            var keptScroll = scrollKey == inspectorScrollKey ? inspectorPanel.Q<ScrollView>(className: "inspector-records")?.scrollOffset : null;
            inspectorScrollKey = scrollKey;
            inspectorPanel.Clear();
            // The traffic rules editor marks its lanes again when it shows.
            mapCanvas?.SetHighlightedPaths(null);
            RefreshJunctionMarkers();

            if (selectedTile == null)
            {
                var empty = new Label(Loc.T(currentSummary == null ? "project.no_map" : "project.no_tiles")) /* i18n:keys */;
                empty.AddToClassList("muted");
                inspectorPanel.Add(empty);
                return;
            }

            var title = new Label(Loc.T("inspector.tile", ("x", selectedTile.X), ("y", selectedTile.Y)));
            title.AddToClassList("inspector-title");
            inspectorPanel.Add(title);
            inspectorPanel.Add(InspectorTileSummary(selectedTile));

            ChooseInspectorTab();
            if (HasInspectorSelection)
                inspectorPanel.Add(InspectorTabStrip());
            inspectorActions = new VisualElement();
            inspectorActions.AddToClassList("inspector-actions");

            // The editor scrolls together with the record lists, so neither squeezes the other.
            var records = new ScrollView(ScrollViewMode.Vertical);
            records.AddToClassList("inspector-records");
            if (inspectorTab == InspectorTab.Tile)
                AddTileTab(records);
            else
            {
                if (selection.Count > 1)
                    AddSelectionSummary(records);
                else if (selectedObject != null)
                    AddObjectEditor(records, selectedObject);
                else if (selectedSpline != null)
                    AddSplineDetails(records, selectedSpline);
                else if (selectedRow != null && activeTool == EditorTool.Rows)
                    AddRowAttachedObjects(records);
                if (pathsShown && mapPaths != null && selection.Count <= 1 && (selectedObject != null || selectedSpline != null))
                    AddPathDetails(records, selectedTile, selectedObject?.Id ?? selectedSpline.Id, selectedObject == null);
            }
            inspectorPanel.Add(records);
            if (inspectorActions.childCount > 0)
                inspectorPanel.Add(inspectorActions);
            if (keptScroll.HasValue)
            {
                var offset = keptScroll.Value;
                // The new list has its size only after the next layout.
                records.contentContainer.RegisterCallback<GeometryChangedEvent>(KeepScroll);

                void KeepScroll(GeometryChangedEvent evt)
                {
                    records.contentContainer.UnregisterCallback<GeometryChangedEvent>(KeepScroll);
                    records.scrollOffset = offset;
                }
            }
        }

        private void AddInspectorValue(string labelKey, string value)
        {
            AddInspectorValue(inspectorPanel, labelKey, value);
        }

        private static void AddInspectorValue(VisualElement container, string labelKey, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("inspector-property");
            var name = LocBindings.NewLabel(labelKey); // i18n:keys
            name.AddToClassList("inspector-property-name");
            var content = new Label(value ?? string.Empty);
            content.AddToClassList("inspector-property-value");
            row.Add(name);
            row.Add(content);
            container.Add(row);
        }

        /// <summary>A heading label written in capitals as the language writes them, from a key.</summary>
        private static Label UpperLabel(string key, params (string Name, object Value)[] arguments) // i18n:keys
        {
            var label = new Label();
            LocBindings.TextUpper(label, key, arguments); // i18n:keys
            return label;
        }

        private static Label AddInspectorRecord(VisualElement container, string text, string className)
        {
            var record = new Label(text);
            record.AddToClassList(className);
            container.Add(record);
            return record;
        }

        private bool IsTileEdited(OmsiTileSummary tile)
        {
            return session != null && session.CanEdit && session.IsTileModified(tile.FileName);
        }

        /// <summary>The selection when it holds several objects: their count and IDs, and a button to delete them.</summary>
        private void AddSelectionSummary(VisualElement container)
        {
            var editor = new VisualElement();
            editor.AddToClassList("inspector-edit");
            var heading = UpperLabel("inspector.selected_objects");
            heading.AddToClassList("inspector-section-heading");
            editor.Add(heading);
            var ids = new List<string>();
            foreach (var entry in selection)
                ids.Add("#" + entry.Record.Id);
            var names = new Label(Loc.T("inspector.selected_ids", ("count", selection.Count), ("ids", string.Join(", ", ids.GetRange(0, Math.Min(12, ids.Count)))), ("more", ids.Count > 12 ? "yes" : "no")));
            names.AddToClassList("inspector-edit-name");
            editor.Add(names);

            var canEdit = CanEditObjects;
            if (canEdit)
            {
                if (CanEdit)
                {
                    var level = new Button(LevelSelectedGround)
                    {
                        text = Loc.T("inspector.level_group"),
                        tooltip = Loc.T("inspector.level_group.tooltip")};
                    level.AddToClassList("map-control-button");
                    level.AddToClassList("inspector-edit-apply");
                    inspectorActions.Add(level);
                }
                var randomize = new Button(RandomizeSelectedHeadings)
                {
                    text = Loc.T("inspector.randomize_headings"),
                    tooltip = Loc.T("inspector.randomize_headings.tooltip")};
                randomize.AddToClassList("map-control-button");
                randomize.AddToClassList("inspector-edit-apply");
                inspectorActions.Add(randomize);
                var delete = new Button(DeleteSelectedObjects) { text = Loc.T("inspector.delete_many", ("count", selection.Count))};
                delete.AddToClassList("map-control-button");
                delete.AddToClassList("inspector-edit-delete");
                inspectorActions.Add(delete);
            }
            var hint = new Label(Loc.T(canEdit ? "inspector.group_hint" : "inspector.readonly")) /* i18n:keys */;
            hint.AddToClassList("muted");
            hint.AddToClassList("inspector-edit-hint");
            editor.Add(hint);
            container.Add(editor);
        }

        /// <summary>
        /// The objects hanging on the selected object, directly or through another, by attachment point; each can be taken
        /// off. New ones are hung on by placing an object on a free point.
        /// </summary>
        private void AddAttachedObjects(VisualElement editor, OmsiObjectRecord item, bool canEdit)
        {
            var tile = TileHolding(new MapSelectionItem { Tile = selectedTile, Record = item }) ?? selectedTile;
            AddAttachedObjects(editor, tile, item.Id, canEdit, Loc.T("inspector.attach_hint_object"));
        }

        /// <summary>
        /// The objects hanging on the first object of the selected row, in each of its tiles (only the first object of a
        /// row can carry objects in OMSI and openOMSI).
        /// </summary>
        private void AddRowAttachedObjects(VisualElement editor)
        {
            if (selectedRow == null)
                return;
            foreach (var record in selectedRow.Records)
                if (!record.Value.IsRepeater)
                    AddAttachedObjects(editor, record.Key, record.Value.Id, CanEdit, Loc.T("inspector.attach_hint_row"));
        }

        private void AddAttachedObjects(VisualElement editor, OmsiTileSummary tile, int parentId, bool canEdit, string hintText)
        {
            var hanging = new List<KeyValuePair<int, OmsiAttachedObjectRecord>>();
            void Collect(int parentId, int depth)
            {
                foreach (var attached in tile.AttachedObjects)
                    if (attached.ParentId == parentId && depth < 8)
                    {
                        hanging.Add(new KeyValuePair<int, OmsiAttachedObjectRecord>(depth, attached));
                        Collect(attached.Id, depth + 1);
                    }
            }
            Collect(parentId, 0);
            if (hanging.Count == 0)
                return;
            AddInspectorGroup(editor, "object-attached", Loc.T("inspector.attached_objects"), true, body =>
            {
                foreach (var pair in hanging)
                {
                    var attached = pair.Value;
                    var row = new VisualElement();
                    row.AddToClassList("inspector-attached-row");
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginLeft = 12 * pair.Key;
                    var label = new Label(Loc.T("inspector.attached_item", ("n", attached.AttachPointIndex + 1), ("id", attached.Id), ("name", Path.GetFileNameWithoutExtension(attached.AssetPath)))) { tooltip = attached.AssetPath };
                    label.AddToClassList("inspector-edit-name");
                    label.style.flexGrow = 1f;
                    label.style.flexShrink = 1f;
                    row.Add(label);
                    if (CanEditObjects)
                    {
                        var typed = attached;
                        var retype = new Button(() => PickElementType(tile, typed)) { text = Loc.T("inspector.type_short"), tooltip = Loc.T("labels.change_type.tooltip")};
                        retype.AddToClassList("map-control-button");
                        row.Add(retype);
                    }
                    if (canEdit)
                    {
                        var captured = attached;
                        var off = new Button(() => DeleteAttachedObject(tile, captured)) { text = Loc.T("inspector.detach"), tooltip = Loc.T("inspector.detach.tooltip")};
                        off.AddToClassList("map-control-button");
                        off.AddToClassList("inspector-edit-delete");
                        row.Add(off);
                    }
                    body.Add(row);
                    AddTypeAndLabels(body, tile, attached, true);
                    if (!EditingVariant)
                        AddVarParentEditor(body, tile, attached, true);
                }
                if (canEdit)
                {
                    var hint = new Label(hintText);
                    hint.AddToClassList("muted");
                    hint.AddToClassList("inspector-edit-hint");
                    body.Add(hint);
                }
            });
        }

        /// <summary>Position and orientation fields for the selected object; read-only without a project.</summary>
        private void AddObjectEditor(VisualElement container, OmsiObjectRecord item)
        {
            var editor = new VisualElement();
            editor.AddToClassList("inspector-edit");
            var heading = UpperLabel("inspector.selected_object");
            heading.AddToClassList("inspector-section-heading");
            editor.Add(heading);
            var name = new Label("#" + item.Id + "  " + item.AssetPath);
            name.AddToClassList("inspector-edit-name");
            editor.Add(name);
            AddChronoOrigin(editor, item);

            var position = AddEditRow(editor, ("inspector.field.x", item.LocalX), ("inspector.field.y", item.LocalY), ("inspector.field.height", item.Height));
            var xField = position[0];
            var yField = position[1];
            var zField = position[2];
            var turning = AddEditRow(editor, ("inspector.field.heading", item.HeadingDegrees), ("inspector.field.pitch", item.PitchDegrees), ("inspector.field.bank", item.BankDegrees));
            var headingField = turning[0];
            var pitchField = turning[1];
            var bankField = turning[2];

            var canEdit = CanEditObjects;
            if (canEdit)
            {
                if (!CanTilt(item))
                {
                    pitchField.SetEnabled(false);
                    bankField.SetEnabled(false);
                    var locked = new Label(Loc.T("inspector.junction_note"));
                    locked.AddToClassList("muted");
                    locked.AddToClassList("inspector-edit-hint");
                    editor.Add(locked);
                }
            }
            else
            {
                foreach (var field in new[] { xField, yField, zField, headingField, pitchField, bankField })
                    field.SetEnabled(false);
                var hint = new Label(Loc.T("inspector.readonly"));
                hint.AddToClassList("muted");
                hint.AddToClassList("inspector-edit-hint");
                editor.Add(hint);
            }

            var holdingTile = TileHolding(new MapSelectionItem { Tile = selectedTile, Record = item }) ?? selectedTile;
            var entryPoint = EditingVariant ? null : session?.EntryPointOf(currentSummary, holdingTile, item);
            if (entryPoint != null)
                AddEntryPointGroup(editor, entryPoint);
            AddInspectorGroup(editor, "object-type", Loc.T("inspector.group.type"), false, body => AddTypeAndLabels(body, holdingTile, item, false, true));
            if (!EditingVariant)
                AddVarParentGroup(editor, holdingTile, item);
            if (!EditingVariant)
                AddBusStopGroup(editor, holdingTile, item);
            AddJunctionDetails(editor, item);
            // Taking off attached objects works on the base map's tiles only.
            AddAttachedObjects(editor, item, CanEdit);

            if (canEdit)
            {
                var apply = new Button(() => ApplyObjectEdit(item, xField, yField, zField, headingField, pitchField, bankField)) { text = Loc.T("inspector.apply")};
                apply.AddToClassList("map-control-button");
                apply.AddToClassList("inspector-edit-apply");
                inspectorActions.Add(apply);
                if (CanEdit)
                {
                    var level = new Button(LevelSelectedGround)
                    {
                        text = Loc.T("inspector.level"),
                        tooltip = Loc.T("inspector.level.tooltip")};
                    level.AddToClassList("map-control-button");
                    level.AddToClassList("inspector-edit-apply");
                    inspectorActions.Add(level);
                }
                var delete = entryPoint != null
                    ? new Button(() => DeleteEntryPoint(entryPoint)) { text = Loc.T("entry.delete") }
                    : new Button(DeleteSelectedObjects)
                    {
                        text = Loc.T(EditingVariant && !session.IsInEditedVariant(item) ? "inspector.delete_variant" : "inspector.delete_object") /* i18n:keys */};
                delete.AddToClassList("map-control-button");
                delete.AddToClassList("inspector-edit-delete");
                inspectorActions.Add(delete);
                if (CanEdit)
                    AddRevertTileButton(inspectorActions);
            }

            container.Add(editor);
        }

        /// <summary>While a chrono variant is edited: where the selected object comes from, and what editing it there does.</summary>
        private void AddChronoOrigin(VisualElement editor, OmsiObjectRecord item)
        {
            if (!EditingVariant)
                return;
            var origin = session.OriginOf(item);
            var label = new Label(session.IsInEditedVariant(item)
                    ? Loc.T("inspector.variant_note_own", ("title", editedVariant.Title))
                    : Loc.T("inspector.variant_note_other", ("origin", origin != null ? "variant" : "base"), ("originTitle", origin?.Title ?? string.Empty), ("title", editedVariant.Title)));
            label.AddToClassList("muted");
            label.AddToClassList("inspector-edit-hint");
            editor.Add(label);
        }

        /// <summary>
        /// The connection points of a junction object and what each is joined to, with a symbol and text per state; nothing
        /// for objects without [splinehelper].
        /// </summary>
        private void AddJunctionDetails(VisualElement container, OmsiObjectRecord item)
        {
            var catalog = Junctions();
            if (catalog == null || selectedTile == null)
                return;
            var problem = catalog.Problem(item.AssetPath);
            if (problem != null)
            {
                var unresolved = new Label(Loc.T("inspector.unresolved", ("problem", problem)));
                unresolved.AddToClassList("muted");
                unresolved.AddToClassList("inspector-edit-hint");
                container.Add(unresolved);
                return;
            }
            var ports = catalog.Ports(selectedTile, item, currentSummary.TileSizeMeters);
            if (ports.Count == 0)
                return;
            var checks = OmsiJunctionCatalog.Check(currentSummary, ports, null, catalog.PortsAround(currentSummary, selectedTile));
            var title = Loc.T("inspector.ports", ("connected", checks.FindAll(check => check.State == OmsiJunctionPortState.Connected).Count), ("total", checks.Count));
            AddInspectorGroup(container, "object-ports", title, true, body =>
            {
                foreach (var check in checks)
                {
                    var row = new VisualElement();
                    row.AddToClassList("junction-row");
                    var swatch = new VisualElement();
                    swatch.AddToClassList("junction-swatch");
                    swatch.AddToClassList("junction-swatch-" + check.State.ToString().ToLowerInvariant());
                    row.Add(swatch);
                    var text = new Label(StateName(check.State) + ": " + check.Message);
                    text.AddToClassList("junction-text");
                    row.Add(text);
                    body.Add(row);
                }
            });
        }

        private static string StateName(OmsiJunctionPortState state)
        {
            switch (state)
            {
                case OmsiJunctionPortState.Connected:
                    return Loc.T("inspector.port.connected");
                case OmsiJunctionPortState.NeedsReview:
                    return Loc.T("inspector.port.review");
                case OmsiJunctionPortState.Invalid:
                    return Loc.T("inspector.port.invalid");
                default:
                    return Loc.T("inspector.port.unconnected");
            }
        }

        private static TextField AddEditField(VisualElement container, string labelKey, double value)
        {
            var field = new TextField { value = OmsiNumberFormat.Format(value) };
            LocBindings.Text(field, labelKey); // i18n:keys
            field.AddToClassList("inspector-edit-field");
            container.Add(field);
            return field;
        }

        /// <summary>The spline joined at an end of the selected spline: by ID, or touching it without one, or open.</summary>
        private string DescribeJoin(OmsiSplineRecord spline, bool atEnd)
        {
            var id = atEnd ? spline.NextId : spline.PreviousId;
            if (id != 0)
                return "#" + id;
            var touching = selectedSplineTile == null || currentSummary == null
                ? null
                : OmsiSplineEditing.Neighbour(currentSummary, new OmsiSplineEnd(selectedSplineTile, spline, atEnd));
            return touching == null ? Loc.T("inspector.join.open") : Loc.T("inspector.join.touching", ("id", touching.Spline.Id));
        }

        /// <summary>The selected spline: its profile, geometry, heights and neighbours, and the way to delete it.</summary>
        private void AddSplineDetails(VisualElement container, OmsiSplineRecord spline)
        {
            var editor = new VisualElement();
            editor.AddToClassList("inspector-edit");
            var heading = UpperLabel("inspector.selected_spline");
            heading.AddToClassList("inspector-section-heading");
            editor.Add(heading);
            var gradient = spline.LengthMeters > 0 ? (spline.EndHeight - spline.Height) / spline.LengthMeters * 100.0 : 0.0;
            var text = Loc.T("inspector.spline_info", ("id", spline.Id), ("path", spline.AssetPath), ("length", spline.LengthMeters), ("straight", spline.RadiusMeters == 0 ? "yes" : "no"),
                    ("radius", spline.RadiusMeters), ("heading", spline.HeadingDegrees), ("from", spline.Height), ("to", spline.EndHeight), ("gradient", gradient),
                    ("startJoin", DescribeJoin(spline, false)), ("endJoin", DescribeJoin(spline, true)),
                    ("terrain", !spline.AlignsTerrain ? "none" : session != null && session.IsBuiltHere(spline) ? "here" : "original"));
            var details = new Label(text);
            details.AddToClassList("inspector-edit-name");
            editor.Add(details);
            if (CanEdit)
            {
                AddInspectorGroup(editor, "spline-heights", Loc.T("inspector.group.heights"), true, body =>
                {
                    var heights = AddEditRow(body, ("inspector.field.start_height", spline.Height), ("inspector.field.end_height", spline.EndHeight));
                    var startField = heights[0];
                    var endField = heights[1];
                    var apply = new Button(() =>
                    {
                        if (TryParseField(startField, Loc.T("inspector.name.start_height"), out var start) && TryParseField(endField, Loc.T("inspector.name.end_height"), out var end))
                            SetSplineHeights(start, end);
                    })
                    {
                        text = Loc.T("inspector.apply_heights"),
                        tooltip = Loc.T("inspector.apply_heights.tooltip")};
                    apply.AddToClassList("map-control-button");
                    apply.AddToClassList("inspector-edit-apply");
                    body.Add(apply);
                    if (session.IsBuiltHere(spline))
                    {
                        var aligned = spline.AlignsTerrain;
                        var fit = new Button(() => SetRoadTerrainFit(!aligned))
                        {
                            text = Loc.T(aligned ? "inspector.fit_stop" : "inspector.fit_start") /* i18n:keys */,
                            tooltip = Loc.T(aligned ? "inspector.fit_stop.tooltip" : "inspector.fit_start.tooltip") /* i18n:keys */};
                        fit.AddToClassList("map-control-button");
                        body.Add(fit);
                    }
                });
                AddInspectorGroup(editor, "spline-cross", Loc.T("inspector.group.cross"), false, body => AddSplineCrossFields(body, spline));
                AddInspectorGroup(editor, "spline-look", Loc.T("inspector.group.look"), false, body => AddSplineLookFields(body, spline));
            }
            if (CanEdit)
            {
                var delete = new Button(DeleteSelectedSpline) { text = Loc.T("inspector.delete_spline")};
                delete.AddToClassList("map-control-button");
                delete.AddToClassList("inspector-edit-delete");
                inspectorActions.Add(delete);
            }
            else if (CanEditSplinesInVariant)
                AddSplineVariantButtons(inspectorActions);
            container.Add(editor);
        }

        /// <summary>Reverting the edits of the selected tile; a tile this project added has no original to go back to.</summary>
        private void AddRevertTileButton(VisualElement container)
        {
            if (!IsTileEdited(selectedTile) || session.IsTileAdded(selectedTile.FileName))
                return;
            var revert = new Button(RevertSelectedTile) { text = Loc.T("inspector.revert_tile")};
            revert.AddToClassList("map-control-button");
            revert.AddToClassList("inspector-edit-revert");
            container.Add(revert);
        }

        /// <summary>
        /// Reverting the edits of an original tile, and deleting the tile; deleting asks for a second click within a few
        /// seconds, as it takes the whole tile out of the map.
        /// </summary>
        private void AddTileButtons(VisualElement container)
        {
            AddRevertTileButton(container);
            if (!CanEdit)
                return;
            var tile = selectedTile;
            AddWaterEditor(container, tile);
            AddImageryTileEditor(container, tile);
            var armed = false;
            Button delete = null;
            delete = new Button(() =>
            {
                if (armed)
                {
                    DeleteTile(tile);
                    return;
                }
                armed = true;
                delete.text = Loc.T("inspector.delete_tile_confirm", ("x", tile.X), ("y", tile.Y));
                delete.schedule.Execute(() =>
                {
                    armed = false;
                    delete.text = Loc.T("inspector.delete_tile");
                }).StartingIn(4000);
            })
            {
                text = Loc.T("inspector.delete_tile"),
                tooltip = Loc.T("inspector.delete_tile.tooltip")};
            delete.AddToClassList("map-control-button");
            delete.AddToClassList("inspector-edit-delete");
            container.Add(delete);
        }
    }
}
