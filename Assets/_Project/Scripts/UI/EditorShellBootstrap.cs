using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.Core;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>Creates the UI Toolkit shell when the player starts and connects it to the opened map.</summary>
    public sealed class EditorShellBootstrap : MonoBehaviour
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
        private Label mapStateLabel;
        private Label mapTitleLabel;
        private Label pointerInfoLabel;
        private Label selectionInfoLabel;
        private VisualElement emptyState;
        private VisualElement workspaceElement;
        private VisualElement projectFrame;
        private VisualElement inspectorFrame;
        private Button projectCollapseButton;
        private Button inspectorCollapseButton;
        private VisualElement toolbarHost;
        private MapToolbar toolbar;
        private EditorTool activeTool = EditorTool.Drag;
        private Label mapHintLabel;
        private Label zoomLabel;
        private Button toolsButton;
        private VisualElement mapControls;
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

        // A spline clicked in the map; it is shown in the inspector and can be deleted.
        private OmsiTileSummary selectedSplineTile;
        private OmsiSplineRecord selectedSpline;

        // Chrono view: the map as OMSI loads it on a date, with the variants active then laid over the base map. It is
        // read-only; the base map stays in baseSummary and returns unchanged.
        private OmsiMapSummary baseSummary;
        private List<OmsiChronoVariant> chronoVariants = new List<OmsiChronoVariant>();
        private DateTime? chronoDate;
        private Task<OmsiMapSummary> chronoTask;
        private int chronoGeneration;
        private TextField chronoDateField;

        /// <summary>
        /// Edits are possible: a project is open, the base map is shown, not a chrono date, and no map check or chrono
        /// overlay is reading the map's lists on a worker thread.
        /// </summary>
        private bool CanEdit => session != null && session.CanEdit && chronoDate == null && !ReadingMapInBackground;

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

        // Object outlines are computed in the background and kept per OMSI installation.
        private readonly Dictionary<string, OmsiObjectOutline> outlineCache = new Dictionary<string, OmsiObjectOutline>(StringComparer.OrdinalIgnoreCase);
        private string outlineInstallationRoot;
        private int outlineBuildGeneration;
        private bool outlinesBuilding;

        private void Awake()
        {
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
            root.style.flexGrow = 1f;
            root.style.width = Length.Percent(100f);
            root.style.height = Length.Percent(100f);
            var fileButton = root.Q<Button>("file-menu-button");
            var openMapMenuItem = root.Q<Button>("open-map-menu-item");
            var openProjectMenuItem = root.Q<Button>("open-project-menu-item");
            var saveProjectMenuItem = root.Q<Button>("save-project-menu-item");
            var exportMenuItem = root.Q<Button>("export-menu-item");
            exportDialog = root.Q<VisualElement>("export-dialog");
            exportFolderField = root.Q<TextField>("export-folder-field");
            exportNameField = root.Q<TextField>("export-name-field");
            exportTargetLabel = root.Q<Label>("export-target-label");
            exportConfirmButton = root.Q<Button>("export-confirm-button");
            var exportCancelButton = root.Q<Button>("export-cancel-button");
            checkDialog = root.Q<VisualElement>("check-dialog");
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
                    SetStatus("Map check report copied.");
                };
            if (checkCloseButton != null)
                checkCloseButton.clicked += () => HidePopup(checkDialog);
            var newMapMenuItem = root.Q<Button>("new-map-menu-item");
            newMapDialog = root.Q<VisualElement>("new-map-dialog");
            if (newMapDialog != null)
            {
                newMapForm = new NewMapDialog();
                newMapForm.Cancelled += () => HidePopup(newMapDialog);
                newMapForm.CreateRequested += CreateNewMap;
                newMapDialog.Add(newMapForm);
            }
            var fileMenu = root.Q<VisualElement>("file-menu");
            var helpMenu = root.Q<VisualElement>("help-menu");
            var viewMenu = root.Q<VisualElement>("view-menu");
            editMenu = root.Q<VisualElement>("edit-menu");
            var projectPanelMenuItem = root.Q<Button>("project-panel-menu-item");
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
                objectsPanel.ObjectChosen += StartPlacement;
                objectsPanel.CloseRequested += () => ShowObjectsPanel(false);
                objectsPanel.PreviewRequested += path => objectRenderer?.RequestPreview(path);
                objectsPanel.HeadingChanged += heading =>
                {
                    if (mapCanvas != null)
                        mapCanvas.PlacementHeading = heading;
                };
                objectsPanelHost.Add(objectsPanel);
                roadPanel = new RoadToolPanel();
                roadPanel.style.display = DisplayStyle.None;
                roadPanel.CloseRequested += () => SetTool(EditorTool.Drag);
                roadPanel.SettingsChanged += UpdateRoadPreview;
                objectsPanelHost.Add(roadPanel);
            }
            statusLabel = root.Q<Label>("status-message");
            mapStateLabel = root.Q<Label>("map-state");
            mapTitleLabel = root.Q<Label>("map-title");
            mapHintLabel = root.Q<Label>("map-hint");
            mapControls = root.Q<VisualElement>("map-controls");
            pointerInfoLabel = root.Q<Label>("pointer-info");
            selectionInfoLabel = root.Q<Label>("selection-info");
            emptyState = root.Q<VisualElement>("empty-state");
            if (emptyState != null)
            {
                var welcome = new WelcomeScreen();
                welcome.OpenProjectRequested += OpenProject;
                welcome.OpenMapRequested += OpenMap;
                welcome.NewMapRequested += () => OpenNewMapDialog(null);
                welcome.RecentChosen += OpenRecent;
                emptyState.Add(welcome);
            }
            workspaceElement = root.Q<VisualElement>("workspace");
            projectFrame = root.Q<VisualElement>("project-frame");
            inspectorFrame = root.Q<VisualElement>("inspector-frame");
            projectCollapseButton = root.Q<Button>("project-collapse-button");
            inspectorCollapseButton = root.Q<Button>("inspector-collapse-button");
            zoomLabel = root.Q<Label>("zoom-label");
            projectPanel = root.Q<VisualElement>("project-panel");
            inspectorPanel = root.Q<VisualElement>("inspector-panel");
            var editButton = root.Q<Button>("edit-menu-button");
            var viewButton = root.Q<Button>("view-menu-button");
            toolsButton = root.Q<Button>("tools-menu-button");
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
                mapCanvas.TileAddRequested += AddTile;
                mapCanvas.ObjectToggled += ToggleObject;
                mapCanvas.ObjectsBoxSelected += AddToSelection;
                mapCanvas.ObjectsEdited += CommitEdits;
                mapCanvas.PlacementRequested += PlaceChosenObject;
                mapCanvas.SplineClicked += SelectSpline;
                mapCanvas.RoadPointerMoved += (x, y) =>
                {
                    roadPointer = new OmsiMapPoint(x, y);
                    UpdateRoadPreview();
                };
                mapCanvas.RoadClicked += BuildRoadTo;
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
                    SetStatus((bendingSpline ? "Bending the spline" : "Moving the spline end") + " cancelled.");
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
            DecorateButton(groundToggleButton, "layers", "Textures");
            DecorateButton(view3DButton, "rotate-3d", "3D");
            DecorateButton(routesToggleButton, "route", "Routes");
            DecorateButton(projectCollapseButton, "chevron-left", null);
            DecorateButton(inspectorCollapseButton, "chevron-right", null);
            AddPanelIcon(projectFrame, "map");
            AddPanelIcon(inspectorFrame, "sliders-horizontal");

            if (projectCollapseButton != null)
                projectCollapseButton.clicked += () => SetPanelCollapsed(projectFrame, !IsPanelCollapsed(projectFrame));
            if (inspectorCollapseButton != null)
                inspectorCollapseButton.clicked += () => SetPanelCollapsed(inspectorFrame, !IsPanelCollapsed(inspectorFrame));
            if (projectPanelMenuItem != null)
                projectPanelMenuItem.clicked += () =>
                {
                    HidePopup(viewMenu);
                    SetPanelCollapsed(projectFrame, !IsPanelCollapsed(projectFrame));
                };
            if (inspectorMenuItem != null)
                inspectorMenuItem.clicked += () =>
                {
                    HidePopup(viewMenu);
                    SetPanelCollapsed(inspectorFrame, !IsPanelCollapsed(inspectorFrame));
                };

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
            if (toolsButton != null)
            {
                toolsButton.SetEnabled(false);
                toolsButton.clicked += ValidateLoadedMap;
            }
            if (helpButton != null)
                helpButton.clicked += () => TogglePopup(helpMenu, fileMenu, editMenu, viewMenu);
            if (helpCloseButton != null)
                helpCloseButton.clicked += () => HidePopup(helpMenu);

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
                mapCanvas.PlacementPreviewed += objectRenderer.SetPlacementPreview;
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
                mapCanvas.GroundHeight = groundRenderer.HeightAt;
            }
        }

        /// <summary>Keeps the ground camera on the screen rectangle and view of the map view.</summary>
        private void LateUpdate()
        {
            if (groundRenderer == null || mapCanvas == null || rootElement == null)
                return;
            var panelWidth = rootElement.worldBound.width;
            PlaceViewControls();
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
            groundRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            if (objectRenderer != null)
                objectRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            view3DButton?.EnableInClassList("view-control-active", mapCanvas.ThreeDimensional);
            UpdateMapHeader();
            UpdateZoomLabel();
            SetStatus(mapCanvas.ThreeDimensional
                ? "3D view: right drag turns, middle drag moves, the wheel zooms. Click an object to select it" +
                  (CanEdit
                      ? " and drag it; the move tool (W) moves it along arrows, also up, the rotate tool (E) turns it on rings: green heading, red pitch, blue bank."
                      : ".")
                : "Back to the map from above.");
        }

        private void ToggleGround()
        {
            if (groundRenderer == null || mapCanvas == null || mapCanvas.ThreeDimensional)
                return;
            SetTextures(!groundRenderer.GroundVisible);
            SetStatus(groundRenderer.GroundVisible ? "Ground textures shown." : "Ground textures hidden; tiles are drawn plain.");
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
                var path = WindowsFileDialog.OpenFile("Open an OMSI map configuration", WindowsFileDialog.GlobalConfigFilter);
                if (string.IsNullOrEmpty(path))
                    return;

                LoadMap(path, string.Empty, null, false);
                RememberCurrent();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not read map: " + exception.Message);
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
            if (mapPath == null && projectPath == null)
            {
                if (Array.IndexOf(args, "-newMap") >= 0)
                    OpenNewMapDialog(Argument("-newMap", 1) is string template && !template.StartsWith("-", StringComparison.Ordinal) ? template : null);
                // Without a map a screenshot shows the welcome screen.
                var welcomeShot = Argument("-screenshot", 1);
                if (welcomeShot == null)
                    yield break;
                for (var frame = 0; frame < 10; frame++)
                    yield return null;
                yield return new WaitForEndOfFrame();
                SaveScreenshot(welcomeShot);
                if (quit)
                    Application.Quit();
                yield break;
            }

            if (projectPath != null)
                OpenProjectFile(projectPath, false);
            else
            {
                try
                {
                    LoadMap(mapPath, string.Empty, null, false);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
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

            if (double.TryParse(Argument("-turnSelection", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var turnDegrees) &&
                mapCanvas != null && !mapCanvas.TurnSelection(turnDegrees))
                Debug.LogWarning("-turnSelection needs a project and at least two selected objects.");

            if (Array.IndexOf(args, "-plain") >= 0 && groundRenderer != null && mapCanvas != null && !mapCanvas.ThreeDimensional)
                SetTextures(false);

            if (Array.IndexOf(args, "-check") >= 0)
                ValidateLoadedMap();

            if (DateTime.TryParseExact(Argument("-chrono", 1) ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var chronoArgument))
            {
                ShowChronoDate(chronoArgument);
                var chronoStarted = Time.realtimeSinceStartup;
                while (chronoTask != null && Time.realtimeSinceStartup - chronoStarted < 120f)
                    yield return null;
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
                // The profile list fills once the installation is listed.
                var scanStarted = Time.realtimeSinceStartup;
                while (installationScan != null && !installationScan.IsCompleted && Time.realtimeSinceStartup - scanStarted < 120f)
                    yield return null;
                yield return null;
                roadPanel?.ChooseProfile(Argument("-roadProfile", 1));
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
                if (splineTile != null)
                    SelectSpline(splineTile, splineTile.Splines.Find(spline => spline.Id == splineId));
                if (selectedSpline != null &&
                    double.TryParse(Argument("-splineHeights", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var startHeight) &&
                    double.TryParse(Argument("-splineHeights", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var endHeight))
                    SetSplineHeights(startHeight, endHeight);
            }

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

            var fold = Argument("-fold", 1);
            if (fold == "project" || fold == "both")
                SetPanelCollapsed(projectFrame, true);
            if (fold == "inspector" || fold == "both")
                SetPanelCollapsed(inspectorFrame, true);

            var placeAsset = Argument("-place", 1);
            if (placeAsset != null)
            {
                StartPlacement(placeAsset);
                mapCanvas?.PointPlacementAtCenter();
                if (Array.IndexOf(args, "-placeCommit") >= 0)
                    mapCanvas?.ClickPlacement();
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

            var screenshot = Argument("-screenshot", 1);
            if (screenshot != null)
            {
                var started = Time.realtimeSinceStartup;
                while ((groundRenderer.IsLoading || objectRenderer.IsLoading || objectRenderer.PreviewsPending && Array.IndexOf(args, "-noPreviewWait") < 0 || outlinesBuilding || checkTask != null ||
                        (installationScan != null && !installationScan.IsCompleted)) && Time.realtimeSinceStartup - started < 300f)
                    yield return null;
                for (var frame = 0; frame < 10; frame++)
                    yield return null;
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
            var projectPath = WindowsFileDialog.OpenFile("Open a MapWorks project", WindowsFileDialog.ProjectFilter);
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
                LoadMap(project.globalConfigPath, projectName, fullProjectPath, false);
                if (remember)
                    RememberCurrent();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not open project: " + exception.Message);
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
                LoadMap(entry.Path, string.Empty, null, false);
                RememberCurrent();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not open map: " + exception.Message);
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
                SetStatus("Open an OMSI map before saving a project.");
                return;
            }

            try
            {
                var suggestedName = string.IsNullOrWhiteSpace(currentProjectName)
                    ? (string.IsNullOrWhiteSpace(currentSummary.Name) ? "OMSI Map" : currentSummary.Name)
                    : currentProjectName;
                var projectPath = WindowsFileDialog.SaveFile("Save MapWorks project", WindowsFileDialog.ProjectFilter,
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
                SetStatus("Project saved: " + fullProjectPath + ". Edits are saved to " + session.Workspace.WorkspacePath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not save project: " + exception.Message);
            }
        }

        /// <param name="projectFilePath">The project whose workspace is used; null opens the map read-only.</param>
        /// <param name="keepView">Keeps the view, selected tile and selected object, for example after reverting a tile.</param>
        private void LoadMap(string globalConfigPath, string projectName, string projectFilePath, bool keepView)
        {
            var newSession = string.IsNullOrEmpty(projectFilePath)
                ? OmsiMapSession.OpenReadOnly(globalConfigPath)
                : OmsiMapSession.OpenProject(globalConfigPath, projectFilePath);
            var summary = newSession.Read();

            session = newSession;
            junctionTerrainState = null;
            baseSummary = summary;
            chronoDate = null;
            chronoGeneration++;
            chronoTask = null;
            chronoVariants = ReadChronoVariants(newSession.Source);
            currentProjectName = projectName ?? string.Empty;
            undoSteps.Clear();
            redoSteps.Clear();
            StopPlacement();
            ShowObjectsPanel(false);
            routes = null;
            highlightedLine = null;
            routesToggleButton?.SetEnabled(false);
            PresentSummary(summary, keepView);
            StartRoutesBuild(summary);
            UpdateMapHeader();
            RenderProjectBrowser();
            RenderInspector();
            UpdateMapStateLabel();
            SetStatus(currentSummary.Name + ": " + currentSummary.Tiles.Count + " tiles loaded" +
                      (session.CanEdit ? " through the project workspace" : " read-only from the OMSI installation"));
            if (toolsButton != null)
                toolsButton.SetEnabled(true);
        }

        /// <summary>
        /// Shows a summary of the session's map in the views: the base map or a chrono overlay. The selected tile and
        /// object are found again by position and ID when <paramref name="keepView"/> is set.
        /// </summary>
        private void PresentSummary(OmsiMapSummary summary, bool keepView)
        {
            // Roads are drawn on the summary shown; another map or a chrono view ends the road tool and the spline selection.
            if (activeTool == EditorTool.Road)
            {
                activeTool = EditorTool.Drag;
                EndRoadTool();
            }
            ClearSelectedSpline();
            var previousTile = selectedTile;
            var previousObjectId = selectedObject?.Id;
            currentSummary = summary;
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
                mapCanvas.AllowEditing = CanEdit;
                // A map without a project or a chrono view can only be looked at; selecting is the only tool then.
                if (!CanEdit)
                    activeTool = EditorTool.Drag;
                mapCanvas.Tool = activeTool;
                mapCanvas.SetMap(summary, keepView);
                mapCanvas.SetSelection(selectedTile, selection);
                mapCanvas.TexturedView = groundRenderer != null && groundRenderer.GroundVisible;
                var installationRoot = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
                groundRenderer?.Load(summary, session.Source, Path.GetFileName(session.GlobalConfigPath), installationRoot,
                    mapCanvas.OriginX, mapCanvas.OriginY);
                objectRenderer?.Load(summary, session.Source, installationRoot, mapCanvas.OriginX, mapCanvas.OriginY);
            }
            StartOutlineBuild(summary);
            UpdateToolbar();
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

        private string ReadOnlyMessage(string action)
        {
            if (session != null && session.CanEdit && chronoDate == null && ReadingMapInBackground)
                return "The map is being read for the map check or a chrono date; " + action + " again in a moment.";
            return chronoDate != null
                ? "The map shows " + chronoDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                  " with its chrono variants and is read-only. Choose Base map in the project browser to " + action + "."
                : "The map is read-only. Save a MapWorks project (File > Save map project as...) to " + action + ".";
        }

        /// <summary>
        /// Shows the map as OMSI loads it on <paramref name="date"/>: the chrono variants active then are laid over a copy
        /// of the base map on a worker thread, and the timetable of the date drives the routes. Null returns to the base
        /// map. The chrono view is read-only; the base map and its edits are not touched.
        /// </summary>
        private void ShowChronoDate(DateTime? date)
        {
            if (session == null || baseSummary == null)
                return;
            var generation = ++chronoGeneration;
            StopPlacement();
            ShowObjectsPanel(false);
            if (date == null)
            {
                chronoTask = null;
                if (chronoDate == null)
                    return;
                chronoDate = null;
                PresentChrono(baseSummary, null);
                SetStatus(session.CanEdit ? "Base map shown; it can be edited again." : "Base map shown.");
                return;
            }

            var active = OmsiChrono.ActiveOn(chronoVariants, date.Value);
            var source = session.Source;
            var baseMap = baseSummary;
            var dateText = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            OmsiChronoOverlayReport report = null;
            var task = Task.Run(() => OmsiChrono.Overlay(baseMap, source, active, out report));
            chronoTask = task;
            UpdateToolbar();
            SetStatus("Laying " + active.Count + " chrono variant(s) of " + dateText + " over the map...");

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
                    SetStatus("The chrono variants of " + dateText + " could not be applied: " + task.Exception?.GetBaseException().Message);
                    return;
                }
                chronoDate = date.Value.Date;
                StopPlacement();
                ShowObjectsPanel(false);
                PresentChrono(task.Result, active);
                SetStatus(dateText + ": " + active.Count + " chrono variant(s) active, " + report.AddedObjects + " objects and " + report.AddedSplines +
                          " splines added, " + report.DeletedElements + " element(s) removed, " + report.ChangedTypes + " object(s) swapped" +
                          (report.UnresolvedSelections > 0 ? ", " + report.UnresolvedSelections + " selection(s) without element" : string.Empty) +
                          ". The chrono view is read-only.");
            }).Every(100);
        }

        private void PresentChrono(OmsiMapSummary summary, List<OmsiChronoVariant> variants)
        {
            PresentSummary(summary, true);
            StartRoutesBuild(summary, variants);
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
                SetStatus("Enter the date as year-month-day, for example 1992-04-05.");
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
            var task = Task.Run(() =>
            {
                var catalog = new OmsiObjectOutlineCatalog(root);
                var result = new Dictionary<string, OmsiObjectOutline>(StringComparer.OrdinalIgnoreCase);
                foreach (var assetPath in missing)
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
                    SetStatus("Object outlines could not be computed: " + task.Exception?.GetBaseException().Message);
                    return;
                }

                foreach (var pair in task.Result)
                    outlineCache[pair.Key] = pair.Value;
                mapCanvas.SetOutlines(outlineCache);
                var withOutline = 0;
                foreach (var kind in kinds)
                    if (outlineCache.TryGetValue(kind, out var outline) && outline.Points.Length >= 2)
                        withOutline++;
                SetStatus("Object outlines ready for " + withOutline + " of " + kinds.Count + " object kinds. Zoom in to see them.");
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
                    SetStatus("Bus routes could not be read: " + task.Exception?.GetBaseException().Message);
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

        private void SelectSpline(OmsiTileSummary tile, OmsiSplineRecord spline)
        {
            if (tile == null || spline == null)
                return;
            selection.Clear();
            selectedSplineTile = tile;
            selectedSpline = spline;
            mapCanvas?.SetSelectedSpline(spline);
            ApplySelection(tile);
            SetStatus("Selected spline #" + spline.Id + " (" + spline.AssetPath + ")" +
                      (CanEdit ? "; Del deletes it." : " - " + (chronoDate != null ? "chrono view, read-only" : "read-only; save a project to edit")));
        }

        private void ClearSelectedSpline()
        {
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
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("delete splines"));
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
                SetStatus("Could not delete spline #" + selectedSpline.Id + ": " + exception.Message);
                return;
            }
            if (!result.Succeeded)
            {
                var reasons = new List<string>();
                foreach (var blocker in result.Blockers)
                    if (reasons.Count < 3)
                        reasons.Add(blocker.ToString());
                SetStatus("Spline #" + selectedSpline.Id + " stays: " + result.Blockers.Count + " reference(s) still need it, such as " + string.Join("; ", reasons) + ".");
                return;
            }
            var id = selectedSpline.Id;
            PushHistory(new ObjectEditStep { SplineChange = result.Change });
            ClearSelectedSpline();
            RefreshSplineViews(result.Change);
            RenderInspector();
            SetStatus("Deleted spline #" + id + "; Ctrl+Z brings it back.");
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
                    ? (alt ? "Straightening spline #" : "Bending spline #") + draggedSplineEnd.Spline.Id + (alt ? string.Empty : "; Alt runs it straight")
                    : "Moving the " + (draggedSplineEnd.AtEnd ? "end" : "start") + " of spline #" + draggedSplineEnd.Spline.Id +
                      (move.Splines.Count > 1 ? " and the spline joined there" : string.Empty) +
                      (alt ? " without kinks" : "; Alt keeps the road free of kinks");
                var twoArcs = move.Splines.FindAll(geometry => geometry.SplitOff);
                if (twoArcs.Count > 0)
                    text += "; #" + string.Join(", #", twoArcs.ConvertAll(split => split.Spline.Id)) + " becomes two arcs";
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
                SetStatus(ReadOnlyMessage("move splines"));
                return;
            }
            if (move.HasErrors)
            {
                SetStatus("Spline #" + id + " stays: " + move.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
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
                SetStatus("Spline #" + id + " could not be moved: " + exception.Message);
                return;
            }
            if (changes == null)
            {
                RefreshAfterChange(null);
                SetStatus("Spline #" + id + " stays: a spline would be split into two arcs, but " + DescribeReferences(blockers) +
                          " still need it whole. Drag without Alt to keep the shapes instead.");
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
            PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts, Description = "moving spline #" + id });
            var warnings = move.Issues.FindAll(issue => issue.Severity == OmsiRoadIssueSeverity.Warning);
            var splits = move.Splines.FindAll(geometry => geometry.SplitOff);
            SetStatus((bendingSpline ? "Bent spline #" : "Moved spline #") + id + (moved.Count > 0 ? " and #" + string.Join(", #", moved) : string.Empty) +
                      (splits.Count > 0 ? "; split #" + string.Join(", #", splits.ConvertAll(split => split.Spline.Id)) + " into two arcs to stay free of kinks" : string.Empty) +
                      (warnings.Count > 0 ? "; " + warnings[0].Message : string.Empty) + ". Ctrl+Z takes it back.");
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
                SetStatus(ReadOnlyMessage("change spline heights"));
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
                PushHistory(steps.Count == 1 ? steps[0] : new ObjectEditStep { Group = steps, Description = "Heights of spline #" + id });
            if (failure != null)
            {
                SetStatus("The heights of spline #" + id + " could not be changed: " + failure +
                          (steps.Count > 0 ? " The change before it was saved; Ctrl+Z undoes it." : string.Empty));
                return;
            }
            if (steps.Count == 0)
            {
                SetStatus("The heights of spline #" + id + " are unchanged.");
                return;
            }
            var joined = new List<string>();
            foreach (var step in steps)
                foreach (var changedId in step.SplineChange.SplineIds)
                    if (changedId != id && !joined.Contains("#" + changedId))
                        joined.Add("#" + changedId);
            SetStatus("Changed the heights of spline #" + id + (joined.Count > 0 ? " and " + string.Join(", ", joined) : string.Empty) +
                      (warnings.Count > 0 ? "; " + string.Join("; ", warnings) : ".") + " Ctrl+Z takes it back.");
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
                SetStatus("The terrain could not be " + (align ? "fitted" : "restored") + ": " + exception.Message);
                return;
            }
            if (change == null)
            {
                SetStatus("Nothing to change on this road.");
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change });
            RefreshSplineViews(change);
            SetStatus((align ? "Terrain fitted to " : "Terrain no longer fitted to ") + change.SplineIds.Count + " spline(s) of this road; Ctrl+Z takes it back.");
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
                SetStatus(ReadOnlyMessage("create junctions"));
                return;
            }
            profilePath = profilePath ?? roadPanel?.Profile;
            if (profilePath == null)
            {
                SetStatus("Choose a spline profile in the road tool (R) first; the test junction is made in that profile.");
                return;
            }
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                var profile = new OmsiSplineProfileCatalog(root).Get(profilePath);
                if (profile == null)
                {
                    SetStatus("The profile " + profilePath + " could not be read.");
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
                SetStatus("The test junction could not be created: " + exception.Message);
                return;
            }
            // Placing needs the clicks the road tool would take, and the library lists the new object after reading the
            // installation again.
            if (activeTool == EditorTool.Road)
                SetTool(EditorTool.Drag);
            StartInstallationScan(true);
            StartPlacement(assetPath);
            SetStatus("Created " + assetPath + " in your OMSI installation. Click on a road to insert it, then export the map and try it in OMSI.");
        }

        private void RefitRoadTerrain()
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("fit the terrain"));
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
                SetStatus("The terrain could not be fitted: " + exception.Message);
                return;
            }
            if (tiles.Count == 0)
            {
                SetStatus("The terrain already fits all roads built here.");
                return;
            }
            groundRenderer?.RefreshTerrain(tiles);
            objectRenderer?.RefreshTerrain(tiles, session.Source);
            if (roadTerrain != null)
                roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
            foreach (var tile in tiles)
                UpdateBrowserEntry(tile);
            UpdateMapStateLabel();
            SetStatus("Terrain fitted again on " + tiles.Count + " tile(s).");
        }

        /// <summary>Draws the changed splines again from above and in 3D and updates the counts.</summary>
        private void RefreshSplineViews(OmsiSplineChange change)
        {
            mapCanvas?.RefreshSplines();
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
                    mapCanvas?.SetSelectedSpline(selectedSpline);
                }
            }
            UpdateCounts();
            foreach (var tile in change.Tiles)
                UpdateBrowserEntry(tile);
            UpdateMapStateLabel();
            RenderInspector();
            if (routes != null || routesTask != null)
                ScheduleRoutesRebuild();
        }

        private void BeginRoadTool()
        {
            if (roadPanel == null || mapCanvas == null || currentSummary == null)
                return;
            StartInstallationScan();
            UpdateRoadProfiles();
            roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
            objectsPanel.style.display = DisplayStyle.None;
            roadPanel.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            EndRoad();
            mapCanvas.BeginRoad();
            roadPorts = null;
            UpdateRoadPorts();
            SetStatus("Road: click to start a road, on a free spline end to continue it or on a free connection point of a junction; " +
                      "click again for each bend, Esc ends the road.");
        }

        private void EndRoadTool()
        {
            EndRoad();
            mapCanvas?.EndRoad();
            if (roadPanel != null)
                roadPanel.style.display = DisplayStyle.None;
            if (objectsPanelHost != null && !objectsPanelOpen)
                objectsPanelHost.style.display = DisplayStyle.None;
            roadTerrain = null;
            roadPorts = null;
            roadPortChecks = null;
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
            roadPanel.SetProfiles(installationCatalog?.SplineProfiles, counts);
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
            return "connection point " + (port.Index + 1) + " of #" + port.Object.Id + " " + Path.GetFileNameWithoutExtension(port.Object.AssetPath);
        }

        /// <summary>
        /// The road from the current start to <paramref name="target"/>, joining a free spline end or connection point there
        /// if there is one.
        /// </summary>
        private OmsiRoadPlan PlanRoad(OmsiMapPoint target, out OmsiSplineEnd endJoin, out OmsiJunctionPort endPort)
        {
            Snap(target, out endJoin, out endPort);
            var end = endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position ?? target;
            return PlanRoadBetween(roadStart, roadStartHeading, roadStartHeight, roadStartJoin?.OutwardGradientPercent, end, endJoin, endPort);
        }

        /// <summary>Plans a road with the road panel's settings from a start, with its heading, height and gradient where known, to an end.</summary>
        private OmsiRoadPlan PlanRoadBetween(OmsiMapPoint start, double? startHeading, double? startHeight, double? startGradient, OmsiMapPoint end,
            OmsiSplineEnd endJoin, OmsiJunctionPort endPort)
        {
            var request = new OmsiRoadRequest
            {
                StartX = start.X, StartY = start.Y, StartHeadingDegrees = startHeading, StartHeight = startHeight, EndX = end.X, EndY = end.Y,
                StartGradientPercent = startGradient,
                HeightMode = roadPanel.HeightMode, MaximumPieceLength = roadPanel.PieceLength, GradientWarningPercent = roadPanel.GradientWarningPercent,
                EndHeight = endJoin?.Height ?? endPort?.Height ?? (roadPanel.HeightMode == OmsiRoadHeightMode.Manual ? roadPanel.EndHeight : null),
                // Driving into the joined spline or junction continues opposite to the way a road would leave it.
                EndJoinHeadingDegrees = endJoin != null ? endJoin.OutwardHeadingDegrees + 180.0
                    : endPort != null ? endPort.OutwardHeadingDegrees + 180.0 : (double?)null,
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
            var ignore = new HashSet<int>();
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
        private OmsiObjectRecord InsertGeneratedJunction(List<OmsiJunctionArm> arms, OmsiSplinePoint at, OmsiMapPoint pointer, List<object> steps)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string assetPath;
            try
            {
                var plain = File.Exists(Path.Combine(root, PlainAsphalt)) ? PlainAsphalt : null;
                var name = "Junction " + Path.GetFileNameWithoutExtension(arms[0].ProfilePath) + " x " + Path.GetFileNameWithoutExtension(arms[arms.Count - 1].ProfilePath);
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, name, OmsiJunctionGenerator.Junction(arms, plain, "Mapforge " + name));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("The junction could not be made: " + exception.Message);
                return null;
            }
            var catalog = Junctions();
            var plan = OmsiJunctionInsertion.Plan(currentSummary, catalog, assetPath, at.Tile, at.Spline, at.Distance, pointer.X, pointer.Y,
                OmsiRoadJunctions.MainPair(catalog.Helpers(assetPath)));
            if (plan == null || plan.Problem != null)
            {
                SetStatus("No junction here: " + (plan?.Problem ?? "the made junction has no connection points."));
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
                SetStatus("The junction could not be inserted: " + exception.Message);
                return null;
            }
            if (!result.Succeeded)
            {
                SetStatus("The road cannot be cut for a junction here: " + DescribeReferences(result.Blockers) + ".");
                return null;
            }
            foreach (var step in result.Steps)
                if (step is OmsiSplineChange change)
                    RefreshSplineViews(change);
            steps.AddRange(result.Steps);
            // The road tool snaps to the new junction's points from now on.
            roadPorts = null;
            return result.Junction;
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
                SetStatus("The road cannot be built to the junction: " + plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
                return null;
            }
            try
            {
                var change = session.CreateRoad(currentSummary, plan, roadPanel.Profile, startJoin, endJoin, roadPanel.FitTerrain);
                steps.Add(change);
                RefreshSplineViews(change);
                if (roadTerrain != null)
                    roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
                return change;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("The road could not be built: " + exception.Message);
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
                    parts.Add(new ObjectEditStep { TileX = item.Tile.X, TileY = item.Tile.Y, ObjectId = item.Record.Id, Change = item });
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
                SetStatus("The transition could not be made: " + exception.Message);
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
                SetStatus("The transition could not be placed: " + exception.Message);
                return null;
            }
        }

        /// <summary>Builds the road with a transition at the free spline end it starts from, ends at, or both.</summary>
        private void BuildWithTransitions(OmsiMapPoint target, OmsiSplineEnd endJoin, OmsiJunctionPort endPort, bool atStart, bool atEnd)
        {
            var steps = new List<object>();
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
            PushSteps(steps, "the road with " + (atStart && atEnd ? "two transitions" : "a transition"));
            var message = "Built the road with " + (atStart && atEnd ? "transitions" : "a transition") + " to the road of another width. Ctrl+Z takes it all back.";
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(message);
                return;
            }
            ContinueRoadFrom(change);
            SetStatus(message + " Click to go on, Esc ends the road.");
        }

        /// <summary>
        /// Builds a road that crosses other roads, ends on one, or both (ADR 028, ADR 033): new crossroads go into each
        /// crossed road in turn, the road runs in parts from arm to arm, planned again from each junction, and a road it
        /// ends on gets a new T junction. All of it is one undo step; when a part fails, the parts before it are taken back.
        /// </summary>
        private void BuildJunctions(OmsiMapPoint target, bool endsOnRoad)
        {
            var profiles = RoadProfiles();
            Snap(target, out var endJoin, out var endPort);
            var endJoinId = endJoin?.Spline.Id;
            var endJoinAtEnd = endJoin?.AtEnd ?? false;
            var steps = new List<object>();
            var junctionIds = new List<int>();
            var crossedIds = new List<int>();
            var start = roadStart;
            var startHeading = roadStartHeading;
            var startHeight = roadStartHeight;
            var startJoinId = roadStartJoin?.Spline.Id;
            OmsiSplineChange last = null;
            while (true)
            {
                // Splines are read again after every part, so the joined ends are found again by their IDs.
                var startJoin = RefindStartJoin(startJoinId);
                if (startJoinId.HasValue && startJoin == null || !RefindEnd(endJoinId, endJoinAtEnd, out endJoin))
                {
                    FailJunctions(steps, "a spline the road joins changed on the way.");
                    return;
                }
                var end = endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position ?? target;
                var plan = PlanRoadBetween(start, startHeading, startHeight, startJoin?.OutwardGradientPercent, end, endJoin, endPort);
                if (plan.HasErrors)
                {
                    FailJunctions(steps, plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
                    return;
                }
                var crossing = NextCrossing(plan, target, startJoin, endJoin, endsOnRoad);
                if (crossing == null)
                    break;
                if (junctionIds.Count >= MaximumJunctionsPerRoad)
                {
                    FailJunctions(steps, "it crosses more than " + MaximumJunctionsPerRoad + " roads; build it in shorter parts.");
                    return;
                }
                var road = crossing.Road;
                var relative = CrossingAngle(crossing);
                if (Math.Abs(relative) < 30.0 || Math.Abs(relative) > 150.0)
                {
                    FailJunctions(steps, "it crosses spline #" + road.Spline.Id + " at " + Math.Abs(relative).ToString("0") +
                                         "°, too shallow for crossroads; switch off Make junctions to build it without one.");
                    return;
                }
                // The new road arrives on the arm facing its start and leaves on the one ahead.
                var arms = OmsiRoadJunctions.Arms(road.Spline.AssetPath, profiles.Get(road.Spline.AssetPath), roadPanel.Profile, profiles.Get(roadPanel.Profile),
                    new[] { relative + 180.0 }, new[] { relative });
                var junction = InsertGeneratedJunction(arms, road, start, steps);
                if (junction == null)
                {
                    FailJunctionAt(steps, road.Spline.Id, junctionIds.Count);
                    return;
                }
                junctionIds.Add(junction.Id);
                crossedIds.Add(road.Spline.Id);
                startJoin = RefindStartJoin(startJoinId);
                var back = PortFacing(junction, crossing.HeadingDegrees + 180.0);
                var ahead = PortFacing(junction, crossing.HeadingDegrees);
                if (startJoinId.HasValue && startJoin == null ||
                    BuildRoadPart(start, startHeading, startHeight, startJoin?.OutwardGradientPercent, startJoin, back.Position, null, back, steps) == null)
                {
                    RevertSteps(steps);
                    return;
                }
                // The rest of the road leaves the junction on the arm ahead.
                start = ahead.Position;
                startHeading = ahead.OutwardHeadingDegrees;
                startHeight = ahead.Height;
                startJoinId = null;
            }

            var tee = endsOnRoad && endJoin == null && endPort == null ? OmsiRoadJunctions.RoadAt(currentSummary, profiles, target.X, target.Y) : null;
            var lastStartJoin = RefindStartJoin(startJoinId);
            if (tee != null)
            {
                var chain = OmsiSplineChain.Around(currentSummary, tee.Tile, tee.Spline, 1.0);
                chain.FrameAt(tee.Distance, out var point, out var heading, out _);
                var side = OmsiRoadJunctions.SideHeading(OmsiRoadPlanner.ChordHeading(point.X, point.Y, start.X, start.Y) - heading);
                // The new road arrives on the side arm.
                var arms = OmsiRoadJunctions.Arms(tee.Spline.AssetPath, profiles.Get(tee.Spline.AssetPath), roadPanel.Profile, profiles.Get(roadPanel.Profile),
                    new[] { side }, new double[0]);
                var junction = InsertGeneratedJunction(arms, tee, start, steps);
                if (junction == null)
                {
                    FailJunctionAt(steps, tee.Spline.Id, junctionIds.Count);
                    return;
                }
                lastStartJoin = RefindStartJoin(startJoinId);
                var port = PortFacing(junction, junction.HeadingDegrees + side);
                if (startJoinId.HasValue && lastStartJoin == null ||
                    BuildRoadPart(start, startHeading, startHeight, lastStartJoin?.OutwardGradientPercent, lastStartJoin, port.Position, null, port, steps) == null)
                {
                    RevertSteps(steps);
                    return;
                }
                PushSteps(steps, DescribeJunctions(junctionIds.Count, true));
                EndRoad();
                SetStatus("Built the road" + (crossedIds.Count > 0 ? " across " + DescribeSplines(crossedIds) + " with new crossroads," : string.Empty) +
                          " joined to spline #" + tee.Spline.Id + " with a new T junction (#" + junction.Id + "). Ctrl+Z takes it all back.");
                return;
            }

            var finalEnd = endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position ?? target;
            if (startJoinId.HasValue && lastStartJoin == null ||
                (last = BuildRoadPart(start, startHeading, startHeight, lastStartJoin?.OutwardGradientPercent, lastStartJoin, finalEnd, endJoin, endPort, steps)) == null)
            {
                RevertSteps(steps);
                return;
            }
            PushSteps(steps, DescribeJunctions(junctionIds.Count, false));
            var message = "Built the road across " + DescribeSplines(crossedIds) + " with new crossroads (#" + string.Join(", #", junctionIds) +
                          "). Ctrl+Z takes it all back.";
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(message);
                return;
            }
            ContinueRoadFrom(last);
            SetStatus(message + " Click to go on, Esc ends the road.");
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
            var ignore = new HashSet<int>();
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
            SetStatus("Nothing was built" + (builtBefore > 0 ? ", also not the " + builtBefore + " crossroads before it" : string.Empty) +
                      ". At spline #" + splineId + ": " + reason);
        }

        private void FailJunctions(List<object> steps, string reason)
        {
            if (steps.Count > 0)
                RevertSteps(steps);
            SetStatus("This road cannot be built: " + reason);
        }

        private static string DescribeJunctions(int crossroads, bool tee)
        {
            var parts = new List<string>();
            if (crossroads > 0)
                parts.Add(crossroads == 1 ? "the crossroads" : crossroads + " crossroads");
            if (tee)
                parts.Add("the T junction");
            return string.Join(" and ", parts) + " and the road " + (tee && crossroads == 0 ? "to it" : "through them");
        }

        private static string DescribeSplines(List<int> ids)
        {
            return (ids.Count == 1 ? "spline #" : "splines #") + string.Join(", #", ids);
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
            if (!roadPointer.HasValue)
            {
                mapCanvas.ClearRoadPreview();
                return;
            }
            if (!roadStarted)
            {
                Snap(roadPointer.Value, out var join, out var port);
                mapCanvas.SetRoadPreview(null, true, null, join?.Position(currentSummary.TileSizeMeters) ?? port?.Position, null);
                roadPanel.ShowPlan(join != null
                    ? "Click to continue spline #" + join.Spline.Id + " from its " + (join.AtEnd ? "end" : "start") + "."
                    : port != null
                        ? "Click to start a road at " + DescribePort(port) + "; it suggests the profile " + Path.GetFileNameWithoutExtension(port.ProfilePath) + "."
                        : "Click to start a road. Free spline ends show as circles and free connection points of junctions as diamonds; " +
                          "starting on one continues it.", false);
                return;
            }

            var offset = Math.Sqrt((roadPointer.Value.X - roadStart.X) * (roadPointer.Value.X - roadStart.X) +
                                   (roadPointer.Value.Y - roadStart.Y) * (roadPointer.Value.Y - roadStart.Y));
            if (offset < OmsiRoadPlanner.MinimumLength)
            {
                // Right after a click the pointer still rests on the new start.
                mapCanvas.SetRoadPreview(null, true, null, null, roadStart);
                roadPanel.ShowPlan("Click where the road should go next; Esc ends it.", false);
                return;
            }
            var plan = PlanRoad(roadPointer.Value, out var endJoin, out var endPort);
            var pieces = new List<OmsiMapPoint[]>();
            var heights = new List<double[]>();
            foreach (var piece in plan.Pieces)
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
            mapCanvas.SetRoadPreview(pieces, !plan.HasErrors && roadPanel.Profile != null, plan.Issues,
                endJoin?.Position(currentSummary.TileSizeMeters) ?? endPort?.Position, roadStart, heights);
            FindJunction(plan, roadPointer.Value, endJoin, endPort, out var tee, out var crossings);
            var meets = string.Empty;
            if (crossings.Count == 1)
                meets = " Crosses spline #" + crossings[0].Road.Spline.Id + " after " + crossings[0].Along.ToString("F0") + " m: a click makes crossroads there.";
            else if (crossings.Count > 1)
                meets = " Crosses " + crossings.Count + " roads, after " + string.Join(", ", crossings.ConvertAll(crossing => crossing.Along.ToString("F0"))) +
                        " m: a click makes crossroads at each.";
            var shallow = crossings.Find(crossing => Math.Abs(CrossingAngle(crossing)) < 30.0 || Math.Abs(CrossingAngle(crossing)) > 150.0);
            if (shallow != null)
                meets += " Spline #" + shallow.Road.Spline.Id + " is crossed too shallowly for crossroads, so the road cannot be built with junctions.";
            if (tee != null)
                meets += " Ends on spline #" + tee.Spline.Id + ": a click joins it with a new T junction.";
            if (meets.Length == 0 && (NeedsTransition(roadStartJoin, false) || NeedsTransition(endJoin, true)))
                meets = " Joins a road of another width: a click puts a transition between them.";
            roadPanel.ShowPlan(DescribePlan(plan, endJoin, endPort) + meets, plan.HasErrors || plan.Issues.Exists(issue => issue.Severity == OmsiRoadIssueSeverity.Warning));
        }

        private static string DescribePlan(OmsiRoadPlan plan, OmsiSplineEnd endJoin, OmsiJunctionPort endPort)
        {
            var text = plan.Pieces.Count == 0
                ? "No road here."
                : plan.LengthMeters.ToString("F1") + " m in " + plan.Pieces.Count + " spline(s), " +
                  (plan.Pieces[0].RadiusMeters == 0 ? "straight" : "radius " + Math.Abs(plan.Pieces[0].RadiusMeters).ToString("F0") + " m") +
                  ", gradient up to " + plan.MaximumGradientPercent.ToString("F1") + " %, end height " + plan.EndHeight.ToString("F2") + " m" +
                  (endJoin != null ? "; joins spline #" + endJoin.Spline.Id : endPort != null ? "; joins " + DescribePort(endPort) : string.Empty) + ".";
            foreach (var issue in plan.Issues)
                text += "\n" + issue.Severity + ": " + issue.Message;
            return text;
        }

        /// <summary>A click with the road tool: starts a road, or builds it to the click and continues from its end.</summary>
        private void BuildRoadTo(double x, double y)
        {
            if (currentSummary == null || roadPanel == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("build roads"));
                return;
            }
            var point = new OmsiMapPoint(x, y);
            roadPointer = point;
            if (!roadStarted)
            {
                Snap(point, out var join, out var port);
                roadStartJoin = join;
                roadStartPort = port;
                roadStart = join?.Position(currentSummary.TileSizeMeters) ?? port?.Position ?? point;
                roadStartHeading = join?.OutwardHeadingDegrees ?? port?.OutwardHeadingDegrees;
                roadStartHeight = join?.Height ?? port?.Height;
                if (join == null && port == null && roadPanel.HeightMode == OmsiRoadHeightMode.Manual)
                    roadStartHeight = roadTerrain != null && roadTerrain.TryGetHeight(point.X, point.Y, out var ground) ? ground : roadPanel.EndHeight;
                if (roadPanel.HeightMode == OmsiRoadHeightMode.Manual && roadStartHeight.HasValue)
                    roadPanel.SuggestEndHeight(roadStartHeight.Value);
                roadStarted = true;
                UpdateRoadPreview();
                SetStatus(join != null ? "Continuing spline #" + join.Spline.Id + ". Click where the road should go; Esc ends it."
                    : port != null ? "Road started at " + DescribePort(port) + ", leaving it the way the point faces. Click where it should go; Esc ends it."
                    : "Road started. Click where it should go; it leaves straight towards the first click and then bends tangentially.");
                return;
            }

            if (roadPanel.Profile == null)
            {
                SetStatus("Choose a spline profile in the road panel first.");
                return;
            }
            var plan = PlanRoad(point, out var endJoin, out var endPort);
            if (plan.HasErrors)
            {
                SetStatus("This road cannot be built: " + plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
                return;
            }
            FindJunction(plan, point, endJoin, endPort, out var tee, out var crossings);
            if (tee != null || crossings.Count > 0)
            {
                BuildJunctions(point, tee != null);
                return;
            }
            var transitionAtStart = NeedsTransition(roadStartJoin, false);
            var transitionAtEnd = NeedsTransition(endJoin, true);
            if (transitionAtStart || transitionAtEnd)
            {
                BuildWithTransitions(point, endJoin, endPort, transitionAtStart, transitionAtEnd);
                return;
            }
            OmsiSplineChange change;
            try
            {
                change = session.CreateRoad(currentSummary, plan, roadPanel.Profile, roadStartJoin, endJoin, roadPanel.FitTerrain);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("The road could not be built: " + exception.Message);
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change });
            RefreshSplineViews(change);

            var warnings = plan.Issues.FindAll(issue => issue.Severity != OmsiRoadIssueSeverity.Note).Count;
            var built = "Built " + plan.LengthMeters.ToString("F1") + " m of road in " + change.SplineIds.Count + " spline(s)" +
                        (warnings > 0 ? " with " + warnings + " warning(s)" : string.Empty);
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(built + ", joined to " + (endJoin != null ? "spline #" + endJoin.Spline.Id : DescribePort(endPort)) + ". Click to start the next road.");
                return;
            }
            // The road continues from the end of its last spline.
            ContinueRoadFrom(change);
            SetStatus(built + (roadStarted ? ". Click to go on, Esc ends the road, Ctrl+Z takes it back." : "."));
        }

        /// <summary>Builds the routes again 300 ms after the last edit; edits in between push the build back.</summary>
        private void ScheduleRoutesRebuild()
        {
            if (mapCanvas == null)
                return;
            routesRebuild?.Pause();
            routesRebuild = mapCanvas.schedule.Execute(() =>
            {
                routesRebuild = null;
                if (currentSummary != null && chronoDate == null)
                    StartRoutesBuild(currentSummary);
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
                SetStatus(routes.Stops.Count + " stops and " + routes.Lines.Count + " lines shown" +
                          (routes.MissingEntryCount > 0 ? "; " + routes.MissingEntryCount + " driven paths are not on the map" : string.Empty) +
                          ". Click a line in the project browser to highlight it.");
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

            selection.Clear();
            selection.Add(new MapSelectionItem { Tile = tile, Record = item });
            ApplySelection(tile);
            if (centerView)
                mapCanvas?.CenterOn(tile, item);
            SetStatus("Selected object #" + item.Id + " (" + item.AssetPath + ")" +
                      (CanEdit ? "" : chronoDate != null ? " - chrono view, read-only" : " - read-only; save a project to edit"));
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
                SetStatus("There are no objects in the box.");
                return;
            }
            ApplySelection(selection[selection.Count - 1].Tile);
            SetStatus(DescribeSelection());
        }

        private void SelectTile(OmsiTileSummary tile, bool centerView)
        {
            if (tile == null || currentSummary == null)
                return;

            selection.Clear();
            ClearSelectedSpline();
            ApplySelection(tile);
            if (centerView)
                mapCanvas?.CenterOn(tile, null);
            SetStatus("Selected tile " + tile.X + ", " + tile.Y);
        }

        /// <summary>Makes the last selected object the primary one, highlights the tile and refreshes the views.</summary>
        private void ApplySelection(OmsiTileSummary tile)
        {
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
                selectionInfoLabel.text = selection.Count == 0 ? string.Empty
                    : selection.Count == 1 ? "1 object selected"
                    : selection.Count + " objects selected";
            UpdateToolbar();
            RenderInspector();
        }

        private string DescribeSelection()
        {
            if (selection.Count == 0)
                return "Selection cleared.";
            if (selection.Count == 1)
                return "Selected object #" + selectedObject.Id + " (" + selectedObject.AssetPath + ")";
            return selection.Count + " objects selected. Drag one of them to move all; Del deletes them; Ctrl+click adds or removes one.";
        }

        private bool IsSelected(OmsiObjectRecord item)
        {
            return selection.Exists(entry => ReferenceEquals(entry.Record, item));
        }

        /// <summary>
        /// Saves moved or rotated objects into the workspace as one undo step. An object that ends up in another tile
        /// moves into that tile, unless references tie it to its own; then it stays there with outside coordinates.
        /// </summary>
        private void CommitEdits(List<MapObjectEdit> edits)
        {
            if (edits == null || edits.Count == 0)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("edit it"));
                return;
            }

            var parts = new List<ObjectEditStep>();
            var kept = new List<string>();
            var transfers = new List<string>();
            OmsiReferenceIndex references = null;
            string failure = null;
            foreach (var edit in edits)
            {
                try
                {
                    var target = OtherTileAt(edit.Tile, edit.LocalX, edit.LocalY, out var targetX, out var targetY);
                    if (target != null)
                    {
                        references = references ?? session.BuildReferenceIndex();
                        var result = session.TransferObject(currentSummary, edit.Tile, edit.Record, target, targetX, targetY, references);
                        if (result.Succeeded)
                        {
                            parts.Add(new ObjectEditStep { Change = result.Change });
                            transfers.Add("#" + edit.Record.Id + " into tile " + target.X + ", " + target.Y);
                            // A transfer keeps height and orientation; an edit from the inspector may change them too.
                            AddValueStep(parts, target, edit.Record, edit.Record.LocalX, edit.Record.LocalY, edit);
                            continue;
                        }
                        kept.Add("#" + edit.Record.Id + " (" + DescribeReferences(result.Blockers) + ")");
                    }
                    AddValueStep(parts, edit.Tile, edit.Record, edit.LocalX, edit.LocalY, edit);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    failure = "Could not save object #" + edit.Record.Id + ": " + exception.Message;
                    break;
                }
            }

            if (parts.Count > 0)
                PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts });
            RefreshAfterChange(null);

            if (failure != null)
            {
                SetStatus(failure + (parts.Count > 0 ? " The changes before it were saved; Ctrl+Z undoes them." : string.Empty));
                return;
            }
            if (parts.Count == 0)
            {
                SetStatus(edits.Count == 1 ? "Object #" + edits[0].Record.Id + " is unchanged." : "The objects are unchanged.");
                return;
            }
            var message = edits.Count == 1
                ? "Object #" + edits[0].Record.Id + " saved to the project workspace."
                : edits.Count + " objects saved to the project workspace.";
            if (transfers.Count > 0)
                message += " Moved " + JoinLimited(transfers) + ".";
            if (kept.Count > 0)
                message += " Kept in their tile because references tie them to it: " + JoinLimited(kept) + ".";
            SetStatus(message + " Ctrl+Z undoes it.");
        }

        /// <summary>Saves the edit's height and orientation at a tile-local position and records it for undo.</summary>
        private void AddValueStep(List<ObjectEditStep> parts, OmsiTileSummary tile, OmsiObjectRecord item, double x, double y, MapObjectEdit edit)
        {
            var before = Values(item);
            if (session.UpdateObject(tile, item, x, y, edit.Height, edit.HeadingDegrees, edit.PitchDegrees, edit.BankDegrees))
                parts.Add(new ObjectEditStep { TileX = tile.X, TileY = tile.Y, ObjectId = item.Id, Before = before, After = Values(item) });
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
            return string.Join(", ", shown) + (parts.Count > shown.Count ? " and " + (parts.Count - shown.Count) + " more" : string.Empty);
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
                if (tile == null)
                    selection.RemoveAt(index);
                else
                    selection[index].Tile = tile;
            }
            ApplySelection(selection.Count > 0 ? selection[selection.Count - 1].Tile : selectedTile);
            RefitTerrainAtJunctions();
            mapCanvas?.Refresh();
            objectRenderer?.RefreshPlacements();
            groundRenderer?.RefreshTerrainHoles(currentSummary);
            // Moved stops and crossings move their routes.
            if (routes != null || routesTask != null)
                ScheduleRoutesRebuild();
            foreach (var tile in browserEntries.Keys)
                UpdateBrowserEntry(tile);
            UpdateCounts();
            UpdateMapStateLabel();
        }

        /// <summary>
        /// Fits the terrain again when an editor-placed junction moved, appeared or went, since fitted roads may join it
        /// (ADR 022). The terrain follows from the original and the roads, so this is no undo step of its own.
        /// </summary>
        private void RefitTerrainAtJunctions()
        {
            if (!CanEdit || currentSummary == null)
                return;
            if (!currentSummary.Tiles.Exists(tile => tile.Splines.Exists(spline => spline.AlignsTerrain && session.ShapesTerrain(spline))))
                return;
            List<OmsiTileSummary> tiles;
            try
            {
                var state = session.JunctionTerrainState(currentSummary);
                if (state == junctionTerrainState)
                    return;
                junctionTerrainState = state;
                tiles = session.RefitAllTerrain(currentSummary);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The terrain could not be fitted to the junctions: " + exception.Message);
                return;
            }
            if (tiles.Count == 0)
                return;
            groundRenderer?.RefreshTerrain(tiles);
            objectRenderer?.RefreshTerrain(tiles, session.Source);
            if (roadTerrain != null)
                roadTerrain = new OmsiTerrainSampler(currentSummary, session.Source);
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

        private void OnShortcut(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && (IsShown(exportDialog) || IsShown(newMapDialog) || IsShown(checkDialog)))
            {
                HidePopup(exportDialog);
                HidePopup(newMapDialog);
                HidePopup(checkDialog);
                evt.StopPropagation();
                return;
            }
            var focused = evt.target as VisualElement;
            // Esc in the object search still stops placing first, then closes the panel.
            if (evt.keyCode == KeyCode.Escape && objectsPanel != null && focused != null && objectsPanel.Contains(focused))
            {
                if (placementAsset != null)
                    StopPlacement();
                else
                    ShowObjectsPanel(false);
                evt.StopPropagation();
                return;
            }
            if (focused is TextField || focused?.GetFirstAncestorOfType<TextField>() != null || IsShown(exportDialog) || IsShown(newMapDialog) || IsShown(checkDialog))
                return;

            if (!(evt.ctrlKey || evt.commandKey))
            {
                if (evt.keyCode == KeyCode.Escape && activeTool == EditorTool.Road)
                {
                    if (roadStarted)
                    {
                        EndRoad();
                        SetStatus("Road ended. Click to start the next one, Esc leaves the road tool.");
                    }
                    else
                        SetTool(EditorTool.Drag);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape && mapCanvas != null && mapCanvas.IsPlacing)
                {
                    StopPlacement();
                    SetStatus("Placement stopped.");
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape && objectsPanelOpen)
                {
                    ShowObjectsPanel(false);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape && selection.Count > 0)
                {
                    // Not stopped: the map view also cancels a drag in progress.
                    SelectTile(selectedTile, false);
                }
                else if (evt.keyCode == KeyCode.R && mapCanvas != null && mapCanvas.IsPlacing)
                {
                    mapCanvas.PlacementHeading += evt.shiftKey ? -PlacementRotationStep : PlacementRotationStep;
                    objectsPanel?.ShowHeading(mapCanvas.PlacementHeading);
                    SetStatus("Placing " + placementAsset + " at heading " + mapCanvas.PlacementHeading.ToString("0") + "°.");
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.R && mapCanvas != null && !mapCanvas.IsPlacing)
                {
                    SetTool(activeTool == EditorTool.Road ? EditorTool.Drag : EditorTool.Road);
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
                else if (evt.keyCode == KeyCode.Q || evt.keyCode == KeyCode.W || evt.keyCode == KeyCode.E)
                {
                    SetTool(evt.keyCode == KeyCode.W ? EditorTool.Move : evt.keyCode == KeyCode.E ? EditorTool.Rotate : EditorTool.Drag);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Alpha3 || evt.keyCode == KeyCode.Keypad3)
                {
                    ShowObjectsPanel(!objectsPanelOpen);
                    evt.StopPropagation();
                }
                return;
            }

            if (evt.keyCode == KeyCode.Z && !evt.shiftKey)
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
            if (!CanEdit || from.Count == 0)
            {
                SetStatus(from.Count > 0 && session != null && session.CanEdit ? ReadOnlyMessage(undo ? "undo" : "redo") : undo ? "Nothing to undo." : "Nothing to redo.");
                return;
            }

            var step = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            if (step.TileChange != null)
            {
                StepTile(step, to, undo);
                return;
            }
            var touched = new List<MapSelectionItem>();
            try
            {
                ApplyStep(step, undo, touched);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RefreshAfterChange(null);
                SetStatus("Could not " + (undo ? "undo" : "redo") + " " + DescribeStep(step) + ": " + exception.Message);
                return;
            }

            to.Add(step);
            RefreshAfterChange(touched);
            SetStatus((undo ? "Undid " : "Redid ") + DescribeStep(step) + ".");
        }

        /// <summary>
        /// Undoes or redoes a step; the parts of a group are undone in reverse order. Collects the objects that exist
        /// afterwards, so they can be selected.
        /// </summary>
        private void ApplyStep(ObjectEditStep step, bool undo, List<MapSelectionItem> touched)
        {
            if (step.Group != null)
            {
                for (var index = 0; index < step.Group.Count; index++)
                    ApplyStep(step.Group[undo ? step.Group.Count - 1 - index : index], undo, touched);
                return;
            }

            if (step.SplineChange != null)
            {
                if (undo)
                    session.Undo(currentSummary, step.SplineChange);
                else
                    session.Redo(currentSummary, step.SplineChange);
                RefreshSplineViews(step.SplineChange);
                return;
            }

            if (step.Change != null)
            {
                if (undo)
                    session.Undo(currentSummary, step.Change);
                else
                    session.Redo(currentSummary, step.Change);
                if (step.Change.ObjectExists)
                    Touch(touched, step.Change.CurrentTile, step.Change.Record);
                return;
            }

            var tile = currentSummary.Tiles.Find(candidate => candidate.X == step.TileX && candidate.Y == step.TileY);
            var item = tile?.Objects.Find(candidate => candidate.Id == step.ObjectId);
            if (item == null)
                throw new InvalidOperationException("Object #" + step.ObjectId + " no longer exists.");
            var target = undo ? step.Before : step.After;
            session.UpdateObject(tile, item, target[0], target[1], target[2], target[3], target[4], target[5]);
            Touch(touched, tile, item);
        }

        /// <summary>Undoes or redoes an added or deleted tile; reading the map again clears the history, so the step is put back alone.</summary>
        private void StepTile(ObjectEditStep step, List<ObjectEditStep> to, bool undo)
        {
            try
            {
                session.ApplyTileChange(step.TileChange, undo);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not " + (undo ? "undo" : "redo") + " " + DescribeStep(step) + ": " + exception.Message);
                return;
            }
            ReloadMap();
            to.Add(step);
            SetStatus((undo ? "Undid " : "Redid ") + DescribeStep(step) + ".");
        }

        /// <summary>Adds an empty tile at a free place beside the map, its terrain running on from its neighbours.</summary>
        private void AddTile(int x, int y)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("add tiles"));
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
                SetStatus("Could not add tile " + x + ", " + y + ": " + exception.Message);
                return;
            }
            ReloadMap();
            PushHistory(new ObjectEditStep { TileChange = change });
            var tile = currentSummary.Tiles.Find(candidate => candidate.X == x && candidate.Y == y);
            if (tile != null)
                SelectTile(tile, false);
            SetStatus("Added tile " + x + ", " + y + " with terrain running on from its neighbours. Ctrl+Z takes it back; earlier steps can no longer be undone.");
        }

        /// <summary>Takes a tile out of the map, as long as nothing outside it needs it; otherwise says what does.</summary>
        private void DeleteTile(OmsiTileSummary tile)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("delete tiles"));
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
                SetStatus("Could not delete tile " + tile.X + ", " + tile.Y + ": " + exception.Message);
                return;
            }
            if (!result.Succeeded)
            {
                SetStatus("Tile " + tile.X + ", " + tile.Y + " stays: " + JoinLimited(new List<string>(result.Blockers)) + ".");
                return;
            }
            ReloadMap();
            PushHistory(new ObjectEditStep { TileChange = result.Change });
            SetStatus("Deleted tile " + tile.X + ", " + tile.Y + " from the map. Ctrl+Z brings it back; earlier steps can no longer be undone.");
        }

        private static void Touch(List<MapSelectionItem> touched, OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (!touched.Exists(entry => ReferenceEquals(entry.Record, item)))
                touched.Add(new MapSelectionItem { Tile = tile, Record = item });
        }

        private static string DescribeStep(ObjectEditStep step)
        {
            if (step.Description != null)
                return step.Description;
            if (step.TileChange != null)
                return (step.TileChange.Added ? "the new tile " : "the deletion of tile ") + step.TileChange.X + ", " + step.TileChange.Y;
            if (step.Group != null)
            {
                var ids = new HashSet<int>();
                CollectIds(step, ids);
                return "the change of " + ids.Count + " objects";
            }
            if (step.SplineChange != null && step.SplineChange.Kind == OmsiSplineChangeKind.TerrainAlignment)
                return "the terrain fit of " + step.SplineChange.SplineIds.Count + " spline(s)";
            if (step.SplineChange != null && step.SplineChange.Kind == OmsiSplineChangeKind.Geometry)
                return "the move of " + (step.SplineChange.SplineIds.Count == 1 ? "spline #" + step.SplineChange.SplineIds[0] : step.SplineChange.SplineIds.Count + " splines");
            if (step.SplineChange != null)
                return (step.SplineChange.Kind == OmsiSplineChangeKind.Creation ? "the road of " : "the deletion of ") +
                       (step.SplineChange.SplineIds.Count == 1 ? "spline #" + step.SplineChange.SplineIds[0] : step.SplineChange.SplineIds.Count + " splines");
            if (step.Change == null)
                return "the change of object #" + step.ObjectId;
            switch (step.Change.Kind)
            {
                case OmsiObjectChangeKind.Placement:
                    return "the placement of object #" + step.Change.Record.Id;
                case OmsiObjectChangeKind.Deletion:
                    return "the deletion of object #" + step.Change.Record.Id;
                default:
                    return "the move of object #" + step.Change.Record.Id + " into tile " + step.Change.TargetTile.X + ", " + step.Change.TargetTile.Y;
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
                ids.Add(step.Change?.Record.Id ?? step.ObjectId);
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
                SetStatus("Select an object to delete.");
                return;
            }
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("edit it"));
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
                failure = "Could not delete: " + exception.Message;
            }

            if (parts.Count > 0)
                PushHistory(parts.Count == 1 ? parts[0] : new ObjectEditStep { Group = parts });
            RefreshAfterChange(kept);

            if (failure != null)
                SetStatus(failure + (parts.Count > 0 ? " " + parts.Count + " object(s) were deleted before; Ctrl+Z restores them." : string.Empty));
            else if (targets.Count == 1 && parts.Count == 1)
                SetStatus("Deleted object #" + targets[0].Record.Id + " (" + targets[0].Record.AssetPath + ") from " + targets[0].Tile.FileName + ". Ctrl+Z restores it.");
            else if (targets.Count == 1)
                SetStatus("Object #" + targets[0].Record.Id + " was not deleted; it is still referenced by " + DescribeReferences(blockers) + ".");
            else
                SetStatus("Deleted " + parts.Count + " of " + targets.Count + " objects." +
                          (kept.Count > 0 ? " Still referenced and kept: " + JoinLimited(reasons) + "." : string.Empty) +
                          (parts.Count > 0 ? " Ctrl+Z restores them." : string.Empty));
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
            return string.Join(", ", shown) + (described.Count > shown.Count ? " and " + (described.Count - shown.Count) + " more" : string.Empty);
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
                SetStatus("Open an OMSI map before placing objects.");
                return;
            }
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("place objects"));
                return;
            }

            if (activeTool == EditorTool.Road)
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
            var scan = Task.Run(() => OmsiInstallationCatalog.Scan(root, OmsiContentOrigins.LikelySteamRoots()));
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
                    installationCatalog = OmsiInstallationCatalog.Empty("The installation could not be listed.");
                    contentOrigins = installationCatalog.Origins;
                    SetStatus("Could not list the scenery objects of the OMSI installation: " + scan.Exception?.GetBaseException().Message);
                }
                else
                {
                    installationCatalog = scan.Result;
                    contentOrigins = installationCatalog.Origins;
                    Debug.Log("Object origins: " + contentOrigins.Description + " Groups in " + installationCatalog.Groups.Count + " of " +
                              installationCatalog.Objects.Count + " objects.");
                }
                objectsPanel.SetCatalog(installationCatalog, usedAssetCounts);
                if (activeTool == EditorTool.Road)
                    UpdateRoadProfiles();
            }).Every(150);
        }

        /// <summary>Starts placement mode with an object file relative to the OMSI installation.</summary>
        private void StartPlacement(string assetPath)
        {
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("place objects"));
                return;
            }
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
                    problem = " The object file is missing in the OMSI installation; OMSI will not show it.";
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
                problem = " The object file could not be read: " + exception.Message;
            }

            placementAsset = assetPath;
            mapCanvas?.BeginPlacement(assetPath, mapCanvas.PlacementHeading);
            if (mapCanvas != null)
                objectsPanel?.ShowHeading(mapCanvas.PlacementHeading);
            UpdateToolbar();
            mapCanvas?.Focus();
            SetStatus("Placing " + assetPath + ": click into the map. Shift+click keeps placing, R turns the preview, Esc stops." + (problem ?? string.Empty));
        }

        private void PlaceChosenObject(MapPlacement placement)
        {
            if (placementAsset == null || !CanEdit)
            {
                StopPlacement();
                return;
            }
            if (placement.Snapped && placementPlan != null)
            {
                InsertChosenJunction(placement);
                return;
            }
            if (placement.Tile == null || !placement.Tile.IsAvailable)
            {
                SetStatus(placement.Tile == null ? "Click inside a map tile to place the object." : "The tile file " + placement.Tile.FileName + " is missing.");
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
                SetStatus("Could not place the object: " + exception.Message);
                return;
            }

            var assetPath = placementAsset;
            if (!placement.KeepPlacing)
                StopPlacement();
            PushHistory(new ObjectEditStep { TileX = placement.Tile.X, TileY = placement.Tile.Y, ObjectId = change.Record.Id, Change = change });
            RefreshAfterChange(new[] { new MapSelectionItem { Tile = placement.Tile, Record = change.Record } });
            if (objectsPanelOpen)
                UpdateObjectsCatalog();
            SetStatus("Placed object #" + change.Record.Id + " (" + assetPath + ") in tile " + placement.Tile.X + ", " + placement.Tile.Y +
                      ". Ctrl+Z undoes it." +
                      (placementHasAbsoluteHeight
                          ? " Its height is absolute, not above the terrain, and was set to the terrain under it, " + change.Record.Height.ToString("0.00", CultureInfo.InvariantCulture) + " m."
                          : string.Empty));
        }

        /// <summary>
        /// Snaps a junction being placed onto the road under the pointer, with two opposite connection points on its centre
        /// line and the other arms towards the pointer, and keeps the plan for the click; null places it freely.
        /// </summary>
        private PlacementSnap SnapPlacement(double worldX, double worldY)
        {
            placementPlan = null;
            if (placementAsset == null || !CanEdit || currentSummary == null)
                return null;
            var catalog = Junctions();
            if (catalog == null || catalog.Helpers(placementAsset).Count < 2)
                return null;
            var point = OmsiSplinePoint.Nearest(currentSummary, worldX, worldY, JunctionSnapMeters);
            var plan = point == null ? null
                : OmsiJunctionInsertion.Plan(currentSummary, catalog, placementAsset, point.Tile, point.Spline, point.Distance, worldX, worldY);
            if (plan == null)
            {
                ShowSnapMessage(null);
                return null;
            }
            placementPlan = plan;
            ShowSnapMessage(plan.Problem ?? "Click to insert the junction into the road; its other arms point to the side of the pointer. Alt places it freely.");
            return new PlacementSnap { WorldX = plan.Position.X, WorldY = plan.Position.Y, HeadingDegrees = plan.HeadingDegrees, Height = plan.Height };
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
                SetStatus("The junction could not be inserted: " + exception.Message);
                return;
            }
            if (!result.Succeeded)
            {
                SetStatus("The road cannot be cut here: " + DescribeReferences(result.Blockers) + ".");
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
            PushHistory(new ObjectEditStep { Group = parts, Description = "the junction #" + result.Junction.Id + " inserted into the road" });

            var assetPath = placementAsset;
            if (!placement.KeepPlacing)
                StopPlacement();
            var tile = currentSummary.Tiles.Find(candidate => candidate.Objects.Contains(result.Junction));
            RefreshAfterChange(new[] { new MapSelectionItem { Tile = tile, Record = result.Junction } });
            if (objectsPanelOpen)
                UpdateObjectsCatalog();
            SetStatus("Inserted junction #" + result.Junction.Id + " (" + assetPath + ") into the road and joined it on both sides. Ctrl+Z undoes it.");
        }

        private void StopPlacement()
        {
            placementPlan = null;
            placementSnapMessage = null;
            placementAsset = null;
            mapCanvas?.EndPlacement();
            objectsPanel?.ClearChosen();
            UpdateToolbar();
        }

        private void ApplyObjectEdit(OmsiObjectRecord item, TextField xField, TextField yField, TextField zField, TextField headingField,
            TextField pitchField, TextField bankField)
        {
            if (selectedTile == null)
                return;
            if (!TryParseField(xField, "X", out var x) || !TryParseField(yField, "Y", out var y) ||
                !TryParseField(zField, "Height", out var z) || !TryParseField(headingField, "Heading", out var heading) ||
                !TryParseField(pitchField, "Pitch", out var pitch) || !TryParseField(bankField, "Bank", out var bank))
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
            SetStatus(name + " is not a number: '" + field.value + "'.");
            return false;
        }

        private void RevertSelectedTile()
        {
            if (selectedTile == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("revert tile edits"));
                return;
            }
            if (session.IsTileAdded(selectedTile.FileName))
            {
                SetStatus(selectedTile.FileName + " was added in this project; delete the tile to take it out again.");
                return;
            }
            try
            {
                session.RevertTile(selectedTile.FileName);
                ReloadMap();
                SetStatus("Edits of " + selectedTile.FileName + " were discarded; the original tile is shown again. The undo history was cleared.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not revert " + selectedTile.FileName + ": " + exception.Message);
            }
        }

        private void OpenExportDialog()
        {
            if (session == null)
            {
                SetStatus("Open an OMSI map before exporting.");
                return;
            }
            if (!session.CanEdit)
            {
                SetStatus("Exports are built from a project workspace. Save a MapWorks project first.");
                return;
            }
            if (exportDialog == null || exportFolderField == null || exportNameField == null)
                return;

            var displayName = string.IsNullOrWhiteSpace(currentSummary.FriendlyName) ? currentSummary.Name : currentSummary.FriendlyName;
            exportFolderField.SetValueWithoutNotify(Path.GetFileName(OmsiMapExporter.SuggestTargetFolder(session.Workspace)));
            exportNameField.SetValueWithoutNotify(displayName + " (MapWorks)");
            UpdateExportTarget();
            exportDialog.style.display = DisplayStyle.Flex;
            if (chronoDate != null)
                SetStatus("The export holds the base map with your edits; chrono variants stay in their own folders as in the original.");
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

        /// <summary>Creates the map, saves a project for it where the user says, and opens it for editing.</summary>
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
                SetStatus("Could not create the map: " + exception.Message);
                return;
            }
            HidePopup(newMapDialog);

            try
            {
                var projectPath = WindowsFileDialog.SaveFile("Save the project of the new map", WindowsFileDialog.ProjectFilter,
                    WindowsFileDialog.ProjectExtension, SanitizeFileName(folderName) + "." + WindowsFileDialog.ProjectExtension);
                if (string.IsNullOrEmpty(projectPath))
                {
                    LoadMap(report.GlobalConfigPath, string.Empty, null, false);
                    RememberCurrent();
                    SetStatus("Created " + report.MapFolder + ". Save it as a project to edit it.");
                    return;
                }
                var fullProjectPath = Path.GetFullPath(projectPath);
                var project = new MapforgeProject
                {
                    projectName = Path.GetFileNameWithoutExtension(fullProjectPath),
                    globalConfigPath = report.GlobalConfigPath
                };
                File.WriteAllText(fullProjectPath, JsonUtility.ToJson(project, true), new System.Text.UTF8Encoding(false));
                LoadMap(report.GlobalConfigPath, project.projectName, fullProjectPath, false);
                RememberCurrent();
                SetStatus("Created " + report.MapFolder + " with " + report.CopiedFiles.Count + " helper files from the template, and the project " +
                          fullProjectPath + ". Select '" + friendlyName + "' in OMSI to try it.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("The map was created in " + report.MapFolder + ", but it could not be opened: " + exception.Message);
            }
        }

        private string ExportTargetFolder(out string problem)
        {
            problem = null;
            var folderName = (exportFolderField.value ?? string.Empty).Trim();
            if (folderName.Length == 0 || folderName == "." || folderName == ".." ||
                folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                problem = "Enter a folder name without path separators or special characters.";
                return null;
            }
            if (string.IsNullOrWhiteSpace(exportNameField.value))
                problem = "Enter the name OMSI shows for the exported map.";
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
            exportTargetLabel.text = problem ??
                                     (Directory.Exists(target) ? "Replaces the earlier MapWorks export in:\n" : "Creates the map folder:\n") +
                                     target + "\nEdited files included: " + editedCount;
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
                SetStatus("Exported to " + report.TargetFolder + ": " + report.CopiedFileCount + " files, " +
                          report.EditedFiles.Count + " edited. Select '" + exportNameField.value.Trim() + "' in OMSI.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Export failed: " + exception.Message);
            }
        }

        private void UpdateMapStateLabel()
        {
            if (mapStateLabel == null || currentSummary == null)
                return;

            var mapName = string.IsNullOrWhiteSpace(currentSummary.FriendlyName)
                ? currentSummary.Name
                : currentSummary.FriendlyName;
            var mode = chronoDate != null
                ? "chrono " + chronoDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", read-only"
                : CanEdit
                    ? session.ModifiedFiles().Count + " edited file(s)"
                    : "read-only";
            mapStateLabel.text = (string.IsNullOrWhiteSpace(currentProjectName) ? "Map: " + mapName : "Project: " + currentProjectName) +
                                 "  |  " + currentSummary.Tiles.Count + " tiles  |  " + mode;
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
            return popup != null && popup.resolvedStyle.display != DisplayStyle.None;
        }

        private static void HidePopup(VisualElement popup)
        {
            if (popup != null)
                popup.style.display = DisplayStyle.None;
        }

        /// <summary>Makes a tool active; choosing one ends placing. Without a project only selecting is possible.</summary>
        private void SetTool(EditorTool tool)
        {
            if (currentSummary == null)
                return;
            if (tool != EditorTool.Drag && (!CanEdit))
            {
                SetStatus(ReadOnlyMessage(tool == EditorTool.Road ? "build roads" : "move or rotate objects"));
                return;
            }
            // Another tool ends placing and closes the object library.
            ShowObjectsPanel(false);
            var wasRoad = activeTool == EditorTool.Road;
            activeTool = tool;
            if (mapCanvas != null)
                mapCanvas.Tool = tool;
            if (tool == EditorTool.Road)
                BeginRoadTool();
            else if (wasRoad)
                EndRoadTool();
            UpdateToolbar();
            if (tool == EditorTool.Road)
                return;
            SetStatus(tool == EditorTool.Move ? "Move: drag an arrow to move the selection east, north or, in 3D, up; Shift moves in whole metres."
                : tool == EditorTool.Rotate ? "Rotate: drag the round handle from above or a ring in 3D to turn the selected object; Shift snaps to 15°."
                : "Drag: click objects to select them and drag them to move them freely; Ctrl+click and Ctrl+drag select several.");
        }

        private void UpdateToolbar()
        {
            toolbar?.ShowState(activeTool, placementAsset != null || objectsPanelOpen, CanEdit, selection.Count > 0);
        }

        /// <summary>
        /// Gives a button a Lucide icon and, if given, a label next to it; a button draws its own text over its children,
        /// so the text moves into a label.
        /// </summary>
        private static void DecorateButton(Button button, string icon, string label)
        {
            if (button == null)
                return;
            button.text = string.Empty;
            button.Add(new IconElement(icon));
            if (label != null)
                button.Add(new Label(label) { pickingMode = PickingMode.Ignore });
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

        private static bool IsPanelCollapsed(VisualElement frame)
        {
            return frame != null && frame.ClassListContains("floating-panel-collapsed");
        }

        /// <summary>
        /// Collapses a floating panel to its title bar or opens it again. The arrow points the way the panel will go;
        /// the view controls move along with the inspector.
        /// </summary>
        private void SetPanelCollapsed(VisualElement frame, bool collapsed)
        {
            if (frame == null)
                return;
            frame.EnableInClassList("floating-panel-collapsed", collapsed);
            var isInspector = ReferenceEquals(frame, inspectorFrame);
            var button = isInspector ? inspectorCollapseButton : projectCollapseButton;
            if (button != null)
            {
                // The project browser folds to the left, the inspector to the right.
                button.Q<IconElement>()?.SetIcon(collapsed == isInspector ? "chevron-left" : "chevron-right");
                button.tooltip = (collapsed ? "Open the " : "Collapse the ") + (isInspector ? "inspector" : "project browser");
            }
        }

        /// <summary>
        /// Keeps the view controls just left of the inspector, open or folded, whatever width its title bar takes.
        /// </summary>
        private void PlaceViewControls()
        {
            if (mapControls == null || inspectorFrame == null || workspaceElement == null)
                return;
            var right = workspaceElement.worldBound.xMax - inspectorFrame.worldBound.xMin + 8f;
            if (float.IsNaN(right) || right < 0f || Mathf.Abs(mapControls.resolvedStyle.right - right) < 0.5f)
                return;
            mapControls.style.right = right;
        }

        /// <summary>
        /// The map area that panels and controls leave free, in pixels from each edge, so fitting the map shows it
        /// there and not under a panel.
        /// </summary>
        private Vector4 FreeMapInsets()
        {
            if (mapCanvas == null)
                return Vector4.zero;
            var canvas = mapCanvas.worldBound;
            var left = projectFrame != null && !IsPanelCollapsed(projectFrame) ? projectFrame.worldBound.xMax - canvas.xMin : 0f;
            var right = inspectorFrame != null && !IsPanelCollapsed(inspectorFrame) ? canvas.xMax - inspectorFrame.worldBound.xMin : 0f;
            var top = mapControls != null && mapControls.resolvedStyle.display != DisplayStyle.None ? mapControls.worldBound.yMax - canvas.yMin : 0f;
            var bottom = toolbar != null && toolbarHost.resolvedStyle.display != DisplayStyle.None ? canvas.yMax - toolbar.worldBound.yMin : 0f;
            // Before the first layout the bounds are not known yet.
            float Clean(float value) => float.IsNaN(value) ? 0f : Mathf.Max(0f, value);
            return new Vector4(Clean(left), Clean(top), Clean(right), Clean(bottom));
        }

        /// <summary>
        /// Runs the map check in the background and shows its report: each topic with its outcome and findings, the
        /// tile worth looking at first, and the objects that belong to a DLC.
        /// </summary>
        private void ValidateLoadedMap()
        {
            if (currentSummary == null || session == null)
            {
                SetStatus("Open a map before running validation.");
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
                checkSummaryLabel.text = "Checking " + (string.IsNullOrWhiteSpace(summary.FriendlyName) ? summary.Name : summary.FriendlyName) +
                                         (chronoDate != null ? " without chrono variants" : string.Empty) + "...";
            if (checkDialog != null)
                checkDialog.style.display = DisplayStyle.Flex;
            SetStatus("Checking the map...");
            var task = Task.Run(() => OmsiMapCheck.Run(summary, source, installationRoot, configName));
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
                        SetStatus("The map check failed: " + task.Exception?.GetBaseException().Message);
                    }
                    HidePopup(checkDialog);
                    return;
                }
                var report = task.Result;
                report.Sections.Add(DlcSection());
                lastCheck = report;
                ShowCheckReport(report);
            }).Every(100);
        }

        private void ShowCheckReport(OmsiMapCheckReport report)
        {
            if (checkSections == null || checkSummaryLabel == null)
                return;
            var problems = report.ProblemCount;
            checkSummaryLabel.text = problems == 0
                ? "No problems found in " + report.Sections.Count + " topics."
                : problems + " of " + report.Sections.Count + " topics have findings that look like mistakes in the map data.";
            SetStatus("Map check: " + (problems == 0 ? "passed." : problems + " topic(s) with problems; see the report."));
            checkSections.Clear();
            foreach (var section in report.Sections)
            {
                var box = new VisualElement();
                box.AddToClassList("check-section");
                var header = new VisualElement();
                header.AddToClassList("check-section-header");
                var status = new Label(section.Status == OmsiCheckStatus.Passed ? "OK" : section.Status == OmsiCheckStatus.Note ? "Note" : "Problem");
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
                    }) { text = "Show tile " + section.Tile.X + ", " + section.Tile.Y };
                    show.AddToClassList("map-control-button");
                    show.AddToClassList("check-section-button");
                    header.Add(show);
                }
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
                var more = new Button(() => Fill(int.MaxValue)) { text = "Show all " + findings.Count };
                more.AddToClassList("map-control-button");
                more.AddToClassList("check-more-button");
                list.Add(more);
            }
            Fill(shown);
        }

        private OmsiCheckSection DlcSection()
        {
            var section = new OmsiCheckSection("DLC objects") { Summary = DescribeDlcUse() };
            section.Status = section.Summary.StartsWith("No DLC", StringComparison.Ordinal) ? OmsiCheckStatus.Passed : OmsiCheckStatus.Note;
            return section;
        }

        /// <summary>
        /// Which objects of the map, also attached ones and rows along splines, belong to a DLC; players without it
        /// do not see them.
        /// </summary>
        private string DescribeDlcUse()
        {
            if (contentOrigins == null)
                return "The origin of the objects is still being read; run the check again in a moment.";
            if (!contentOrigins.FromSteam && !contentOrigins.HasMarkedFolders)
                return "DLC objects cannot be told apart without Steam's file lists or folders marked as DLC.";
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
            return kinds.Count == 0
                ? "No DLC objects: the map needs only the base game and freeware."
                : kinds.Count + " DLC object(s) in " + placements + " placement(s), such as " + Path.GetFileName(First(kinds)) +
                  "; players without that DLC miss them.";
        }

        private static string First(IEnumerable<string> items)
        {
            foreach (var item in items)
                return item;
            return string.Empty;
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = message;
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
            SetStatus("Map fitted to the view");
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
            SetStatus(selectedObject != null
                ? "Centered object #" + selectedObject.Id
                : "Centered tile " + selectedTile.X + ", " + selectedTile.Y);
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
                    ? "3D view: right drag turns the view, middle drag moves it, the wheel zooms."
                    : "From above: the wheel zooms, right or middle drag pans.";
                mapHintLabel.text = view + (CanEdit
                    ? "\nTools: Q drag objects freely, W move along axis arrows (Shift: whole metres), E rotate (round handle from above, rings in 3D; Shift: 15° steps), " +
                      "Del delete, 3 place objects.\nCtrl+click or Ctrl+drag selects several. Ctrl+Z / Ctrl+Y undo and redo."
                    : chronoDate != null
                        ? "\nClick an object to inspect it. The chrono view is read-only; choose Base map in the project browser to edit."
                        : "\nClick an object to inspect it. Save a project (File > Save map project as...) to edit the map.");
                mapHintLabel.style.display = DisplayStyle.Flex;
            }
            if (mapControls != null)
                mapControls.style.display = DisplayStyle.Flex;
            if (toolbarHost != null)
                toolbarHost.style.display = DisplayStyle.Flex;
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

        private void RenderProjectBrowser()
        {
            if (projectPanel == null)
                return;

            projectPanel.Clear();
            browserEntries.Clear();

            if (currentSummary == null)
            {
                var empty = new Label("No map loaded");
                empty.AddToClassList("muted");
                projectPanel.Add(empty);
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentProjectName))
            {
                var projectLabel = new Label("Project: " + currentProjectName);
                projectLabel.AddToClassList("browser-project-name");
                projectPanel.Add(projectLabel);
            }

            var mapName = new Label(string.IsNullOrEmpty(currentSummary.FriendlyName)
                ? currentSummary.Name
                : currentSummary.FriendlyName);
            mapName.AddToClassList("browser-map-name");
            projectPanel.Add(mapName);

            browserCountsLabel = new Label();
            browserCountsLabel.AddToClassList("browser-map-counts");
            projectPanel.Add(browserCountsLabel);
            UpdateCounts();

            var warnings = currentSummary.Warnings;
            if (warnings.Count > 0)
            {
                var warningLabel = new Label(warnings.Count + " import warning(s): " + warnings[0] + (warnings.Count > 1 ? "  (hover for all)" : string.Empty))
                {
                    tooltip = string.Join("\n", warnings.GetRange(0, Math.Min(warnings.Count, 40)))
                };
                warningLabel.AddToClassList("browser-map-warnings");
                projectPanel.Add(warningLabel);
            }
            if (!string.IsNullOrWhiteSpace(currentSummary.Description))
            {
                // The description can be long; it stays folded until asked for.
                var description = new Foldout { text = "About this map", value = false };
                description.AddToClassList("browser-map-description");
                description.Add(new Label(currentSummary.Description.Trim()));
                projectPanel.Add(description);
            }

            if (chronoVariants.Count > 0)
                projectPanel.Add(ChronoSection());

            lineEntries.Clear();
            if (routes != null && routes.Lines.Count > 0)
            {
                var lines = new Foldout { text = "Lines (" + routes.Lines.Count + ")", value = false };
                lines.AddToClassList("browser-lines");
                var lineList = new ScrollView(ScrollViewMode.Vertical);
                lineList.AddToClassList("browser-line-list");
                var sorted = new List<OmsiRouteLine>(routes.Lines);
                sorted.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var line in sorted)
                {
                    var name = line.Name;
                    var entry = new Button(() => HighlightLine(name))
                    {
                        text = "Line " + name + "     " + line.StopIds.Count + " stops  /  " + line.ScheduledTripCount + " trips",
                        tooltip = "Highlight line " + name + " on the map; click again to show all lines alike."
                    };
                    entry.AddToClassList("browser-tile-entry");
                    entry.EnableInClassList("browser-tile-entry-selected", name == highlightedLine);
                    lineList.Add(entry);
                    lineEntries[name] = entry;
                }
                lines.Add(lineList);
                projectPanel.Add(lines);
            }

            var tileList = new ScrollView(ScrollViewMode.Vertical);
            tileList.AddToClassList("browser-tile-list");
            foreach (var tile in currentSummary.Tiles)
            {
                var capturedTile = tile;
                var entry = new Button(() => SelectTile(capturedTile, true));
                entry.AddToClassList("browser-tile-entry");
                if (ReferenceEquals(tile, selectedTile))
                    entry.AddToClassList("browser-tile-entry-selected");
                if (!tile.IsAvailable)
                    entry.AddToClassList("browser-tile-entry-missing");
                tileList.Add(entry);
                browserEntries[tile] = entry;
                UpdateBrowserEntry(tile);
            }
            projectPanel.Add(tileList);
        }

        /// <summary>
        /// The chrono variants with their dates, a date to show the map on and the way back to the base map. Clicking a
        /// variant shows its first day.
        /// </summary>
        private VisualElement ChronoSection()
        {
            var section = new Foldout { text = "Chrono (" + chronoVariants.Count + " variants)", value = chronoDate != null };
            section.AddToClassList("browser-chrono");
            var state = new Label(chronoDate == null
                ? "Showing the base map. Enter a date to see the map as OMSI loads it then."
                : "Showing " + chronoDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " with " +
                  OmsiChrono.ActiveOn(chronoVariants, chronoDate.Value).Count + " active variant(s), read-only.");
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
            var show = new Button(ShowChronoDateFromField) { text = "Show", tooltip = "Show the map with the chrono variants active on this date" };
            show.AddToClassList("map-control-button");
            row.Add(show);
            var back = new Button(() => ShowChronoDate(null)) { text = "Base map", tooltip = "Return to the map without chrono variants; it can be edited again" };
            back.AddToClassList("map-control-button");
            back.SetEnabled(chronoDate != null);
            row.Add(back);
            section.Add(row);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("browser-line-list");
            foreach (var variant in chronoVariants)
            {
                var captured = variant;
                var entry = new Button(() =>
                {
                    if (chronoDateField != null)
                        chronoDateField.value = (captured.Start ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    ShowChronoDateFromField();
                })
                {
                    text = variant.ToString(), tooltip = variant.Folder + (variant.DeactivatedLines.Count > 0
                        ? "\nSwitches off the lines " + string.Join(", ", variant.DeactivatedLines)
                        : string.Empty)
                };
                entry.AddToClassList("browser-tile-entry");
                entry.AddToClassList("browser-chrono-variant");
                entry.EnableInClassList("browser-tile-entry-selected", chronoDate != null && variant.IsActiveOn(chronoDate.Value));
                list.Add(entry);
            }
            section.Add(list);
            return section;
        }

        /// <summary>Refreshes the map totals in the header and the project browser after objects were added or removed.</summary>
        private void UpdateCounts()
        {
            if (currentSummary == null)
                return;
            if (browserCountsLabel != null)
                browserCountsLabel.text = currentSummary.Tiles.Count + " tiles  •  " + currentSummary.ObjectCount + " objects  •  " +
                                          currentSummary.SplineCount + " splines\n" + currentSummary.AttachmentCount + " connections  •  terrain " +
                                          currentSummary.TerrainTileCount + "/" + currentSummary.Tiles.Count +
                                          (currentSummary.UsesWorldCoordinates
                                              ? "\nWorld coordinates, " + currentSummary.TileSizeMeters.ToString("0.0") + " m tiles"
                                              : string.Empty);
        }

        /// <summary>Refreshes one tile list entry, for example its edited marker, without rebuilding the list.</summary>
        private void UpdateBrowserEntry(OmsiTileSummary tile)
        {
            if (!browserEntries.TryGetValue(tile, out var entry))
                return;
            var edited = IsTileEdited(tile);
            entry.text = "Tile " + tile.X + ", " + tile.Y + "     " + tile.ObjectCount + " obj  /  " + tile.SplineCount + " spl" +
                         (edited ? "  • edited" : string.Empty);
            entry.EnableInClassList("browser-tile-entry-edited", edited);
        }

        private void RenderInspector()
        {
            if (inspectorPanel == null)
                return;

            inspectorPanel.Clear();
            RefreshJunctionMarkers();

            if (selectedTile == null)
            {
                var empty = new Label(currentSummary == null
                    ? "Open an OMSI map to inspect its tiles."
                    : "This map has no available tile entries.");
                empty.AddToClassList("muted");
                inspectorPanel.Add(empty);
                return;
            }

            var title = new Label("Tile " + selectedTile.X + ", " + selectedTile.Y);
            title.AddToClassList("inspector-title");
            inspectorPanel.Add(title);
            AddInspectorValue("File", selectedTile.FileName);
            if (currentSummary.UsesWorldCoordinates)
                AddInspectorValue("South-west corner", OmsiWorldTiles.DescribeCorner(selectedTile.X, selectedTile.Y) + ", edge " +
                                                       OmsiWorldTiles.EdgeMeters(selectedTile.Y + 0.5).ToString("F2") + " m");
            AddInspectorValue("Status", selectedTile.IsAvailable ? "Available" : "Missing");
            AddInspectorValue("Objects", selectedTile.ObjectCount.ToString());
            AddInspectorValue("Splines", selectedTile.SplineCount.ToString());
            AddInspectorValue("Connections", selectedTile.AttachmentCount.ToString());
            AddInspectorValue("Terrain", selectedTile.HasTerrain
                ? selectedTile.TerrainMinHeight.ToString("F1") + " to " + selectedTile.TerrainMaxHeight.ToString("F1") + " m"
                : "Unavailable");

            // The object editor scrolls together with the record lists, so neither squeezes the other.
            var records = new ScrollView(ScrollViewMode.Vertical);
            records.AddToClassList("inspector-records");
            if (selection.Count > 1)
                AddSelectionSummary(records);
            else if (selectedObject != null)
                AddObjectEditor(records, selectedObject);
            else if (selectedSpline != null)
                AddSplineDetails(records, selectedSpline);
            else
                AddTileButtons(records);

            var objectsHeading = new Label("OBJECT PLACEMENTS");
            objectsHeading.AddToClassList("inspector-section-heading");
            records.Add(objectsHeading);
            if (selectedTile.Objects.Count == 0)
                AddInspectorRecord(records, "No decoded objects.", "inspector-record-empty");
            foreach (var item in selectedTile.Objects)
            {
                var details = "#" + item.Id + "  " + item.AssetPath + "\n" +
                              "X " + item.LocalX.ToString("F1") + "  Y " + item.LocalY.ToString("F1") +
                              "  H " + item.Height.ToString("F1") + " m  •  " + item.HeadingDegrees.ToString("F1") + "°" +
                              (item.PitchDegrees != 0 || item.BankDegrees != 0
                                  ? "  P " + item.PitchDegrees.ToString("F1") + "°  B " + item.BankDegrees.ToString("F1") + "°"
                                  : string.Empty);
                var record = AddInspectorRecord(records, details, "inspector-record");
                record.AddToClassList("inspector-record-clickable");
                record.EnableInClassList("inspector-record-selected", IsSelected(item));
                var capturedItem = item;
                record.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.ctrlKey || evt.commandKey)
                        ToggleObject(selectedTile, capturedItem);
                    else
                        SelectObject(selectedTile, capturedItem, true);
                });
            }

            var splinesHeading = new Label("SPLINE PLACEMENTS");
            splinesHeading.AddToClassList("inspector-section-heading");
            records.Add(splinesHeading);
            if (selectedTile.Splines.Count == 0)
                AddInspectorRecord(records, "No decoded splines.", "inspector-record-empty");
            foreach (var spline in selectedTile.Splines)
            {
                var details = "#" + spline.Id + "  " + spline.AssetPath + "\n" +
                              "X " + spline.LocalX.ToString("F1") + "  Y " + spline.LocalY.ToString("F1") +
                              "  " + spline.LengthMeters.ToString("F1") + " m  •  R " + spline.RadiusMeters.ToString("F1") + " m";
                var record = AddInspectorRecord(records, details, "inspector-record");
                record.AddToClassList("inspector-record-clickable");
                record.EnableInClassList("inspector-record-selected", ReferenceEquals(spline, selectedSpline));
                var capturedSpline = spline;
                record.RegisterCallback<ClickEvent>(_ => SelectSpline(selectedTile, capturedSpline));
            }
            inspectorPanel.Add(records);
        }

        private void AddInspectorValue(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("inspector-property");
            var name = new Label(label);
            name.AddToClassList("inspector-property-name");
            var content = new Label(value ?? string.Empty);
            content.AddToClassList("inspector-property-value");
            row.Add(name);
            row.Add(content);
            inspectorPanel.Add(row);
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
            var heading = new Label("SELECTED OBJECTS");
            heading.AddToClassList("inspector-section-heading");
            editor.Add(heading);
            var ids = new List<string>();
            foreach (var entry in selection)
                ids.Add("#" + entry.Record.Id);
            var names = new Label(selection.Count + " objects: " + string.Join(", ", ids.GetRange(0, Math.Min(12, ids.Count))) +
                                  (ids.Count > 12 ? ", ..." : string.Empty));
            names.AddToClassList("inspector-edit-name");
            editor.Add(names);

            var canEdit = CanEdit;
            if (canEdit)
            {
                var delete = new Button(DeleteSelectedObjects) { text = "Delete " + selection.Count + " objects" };
                delete.AddToClassList("map-control-button");
                delete.AddToClassList("inspector-edit-delete");
                editor.Add(delete);
            }
            var hint = new Label(canEdit
                ? "Drag one of them on the map to move all; with Rotate (E) the handle in their middle turns them together. " +
                  "Ctrl+click adds or removes an object, Esc clears the selection."
                : "Read-only. Save a MapWorks project (File > Save map project as...) to edit this map.");
            hint.AddToClassList("muted");
            hint.AddToClassList("inspector-edit-hint");
            editor.Add(hint);
            container.Add(editor);
        }

        /// <summary>Position and orientation fields for the selected object; read-only without a project.</summary>
        private void AddObjectEditor(VisualElement container, OmsiObjectRecord item)
        {
            var editor = new VisualElement();
            editor.AddToClassList("inspector-edit");
            var heading = new Label("SELECTED OBJECT");
            heading.AddToClassList("inspector-section-heading");
            editor.Add(heading);
            var name = new Label("#" + item.Id + "  " + item.AssetPath);
            name.AddToClassList("inspector-edit-name");
            editor.Add(name);
            AddJunctionDetails(editor, item);

            var xField = AddEditField(editor, "X (m)", item.LocalX);
            var yField = AddEditField(editor, "Y (m)", item.LocalY);
            var zField = AddEditField(editor, "Height (m)", item.Height);
            var headingField = AddEditField(editor, "Heading (°)", item.HeadingDegrees);
            var pitchField = AddEditField(editor, "Pitch (°)", item.PitchDegrees);
            var bankField = AddEditField(editor, "Bank (°)", item.BankDegrees);

            var canEdit = CanEdit;
            if (canEdit)
            {
                if (!CanTilt(item))
                {
                    pitchField.SetEnabled(false);
                    bankField.SetEnabled(false);
                    var locked = new Label("Joins splines like a crossing: pitch and bank stay as they are, so the roads stay connected.");
                    locked.AddToClassList("muted");
                    locked.AddToClassList("inspector-edit-hint");
                    editor.Add(locked);
                }
                var apply = new Button(() => ApplyObjectEdit(item, xField, yField, zField, headingField, pitchField, bankField)) { text = "Apply" };
                apply.AddToClassList("map-control-button");
                apply.AddToClassList("inspector-edit-apply");
                editor.Add(apply);
                var delete = new Button(DeleteSelectedObjects) { text = "Delete object" };
                delete.AddToClassList("map-control-button");
                delete.AddToClassList("inspector-edit-delete");
                editor.Add(delete);
                AddRevertTileButton(editor);
            }
            else
            {
                foreach (var field in new[] { xField, yField, zField, headingField, pitchField, bankField })
                    field.SetEnabled(false);
                var hint = new Label("Read-only. Save a MapWorks project (File > Save map project as...) to edit this map.");
                hint.AddToClassList("muted");
                hint.AddToClassList("inspector-edit-hint");
                editor.Add(hint);
            }

            container.Add(editor);
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
                var unresolved = new Label("Unresolved: " + problem + " The object stays placed as it is.");
                unresolved.AddToClassList("muted");
                unresolved.AddToClassList("inspector-edit-hint");
                container.Add(unresolved);
                return;
            }
            var ports = catalog.Ports(selectedTile, item, currentSummary.TileSizeMeters);
            if (ports.Count == 0)
                return;
            var checks = OmsiJunctionCatalog.Check(currentSummary, ports, null, catalog.PortsAround(currentSummary, selectedTile));
            var heading = new Label("CONNECTION POINTS  " + checks.FindAll(check => check.State == OmsiJunctionPortState.Connected).Count + " of " +
                                    checks.Count + " connected");
            heading.AddToClassList("inspector-section-heading");
            container.Add(heading);
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
                container.Add(row);
            }
        }

        private static string StateName(OmsiJunctionPortState state)
        {
            switch (state)
            {
                case OmsiJunctionPortState.Connected:
                    return "Connected";
                case OmsiJunctionPortState.NeedsReview:
                    return "Needs review";
                case OmsiJunctionPortState.Invalid:
                    return "Invalid";
                default:
                    return "Unconnected";
            }
        }

        private static TextField AddEditField(VisualElement container, string label, double value)
        {
            var field = new TextField(label) { value = OmsiNumberFormat.Format(value) };
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
            return touching == null ? "open" : "#" + touching.Spline.Id + " (touching, no ID)";
        }

        /// <summary>The selected spline: its profile, geometry, heights and neighbours, and the way to delete it.</summary>
        private void AddSplineDetails(VisualElement container, OmsiSplineRecord spline)
        {
            var editor = new VisualElement();
            editor.AddToClassList("inspector-edit");
            var heading = new Label("SELECTED SPLINE");
            heading.AddToClassList("inspector-section-heading");
            editor.Add(heading);
            var gradient = spline.LengthMeters > 0 ? (spline.EndHeight - spline.Height) / spline.LengthMeters * 100.0 : 0.0;
            var text = "#" + spline.Id + "  " + spline.AssetPath + "\n" +
                       "Length " + spline.LengthMeters.ToString("F1") + " m  •  " +
                       (spline.RadiusMeters == 0 ? "straight" : "radius " + spline.RadiusMeters.ToString("F1") + " m") + "  •  heading " +
                       spline.HeadingDegrees.ToString("F1") + "°\n" +
                       "Height " + spline.Height.ToString("F2") + " to " + spline.EndHeight.ToString("F2") + " m  •  mean gradient " +
                       gradient.ToString("F1") + " %\n" +
                       "Joined at start: " + DescribeJoin(spline, false) + "  •  at end: " + DescribeJoin(spline, true) +
                       (spline.AlignsTerrain ? "\nTerrain fitted to it" + (session != null && session.IsBuiltHere(spline) ? " by this editor" : " (original map)") : string.Empty);
            var details = new Label(text);
            details.AddToClassList("inspector-edit-name");
            editor.Add(details);
            if (CanEdit)
            {
                var startField = AddEditField(editor, "Start height", spline.Height);
                var endField = AddEditField(editor, "End height", spline.EndHeight);
                var apply = new Button(() =>
                {
                    if (TryParseField(startField, "Start height", out var start) && TryParseField(endField, "End height", out var end))
                        SetSplineHeights(start, end);
                })
                {
                    text = "Apply heights",
                    tooltip = "Raises or lowers the ends of this spline together with the splines joined there; their other ends keep their height."
                };
                apply.AddToClassList("map-control-button");
                apply.AddToClassList("inspector-edit-apply");
                editor.Add(apply);
            }
            if (CanEdit && session.IsBuiltHere(spline))
            {
                var aligned = spline.AlignsTerrain;
                var fit = new Button(() => SetRoadTerrainFit(!aligned))
                {
                    text = aligned ? "Stop fitting terrain" : "Fit terrain to this road",
                    tooltip = aligned
                        ? "The terrain along this road returns to the original, as far as no other road changes it."
                        : "Lowers the ground under every spline of this road built here and shapes shoulders and slopes beside it."
                };
                fit.AddToClassList("map-control-button");
                editor.Add(fit);
            }
            if (CanEdit)
            {
                var delete = new Button(DeleteSelectedSpline) { text = "Delete spline" };
                delete.AddToClassList("map-control-button");
                delete.AddToClassList("inspector-edit-delete");
                editor.Add(delete);
            }
            container.Add(editor);
        }

        /// <summary>Reverting the edits of the selected tile; a tile this project added has no original to go back to.</summary>
        private void AddRevertTileButton(VisualElement container)
        {
            if (!IsTileEdited(selectedTile) || session.IsTileAdded(selectedTile.FileName))
                return;
            var revert = new Button(RevertSelectedTile) { text = "Revert tile edits" };
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
                delete.text = "Click again to delete tile " + tile.X + ", " + tile.Y;
                delete.schedule.Execute(() =>
                {
                    armed = false;
                    delete.text = "Delete tile";
                }).StartingIn(4000);
            })
            {
                text = "Delete tile",
                tooltip = "Takes the tile out of the map. Later tiles move up in the list and everything naming them follows. " +
                          "A tile stays while an entry point, timetable or something in another tile needs it."
            };
            delete.AddToClassList("map-control-button");
            delete.AddToClassList("inspector-edit-delete");
            container.Add(delete);
        }
    }
}
