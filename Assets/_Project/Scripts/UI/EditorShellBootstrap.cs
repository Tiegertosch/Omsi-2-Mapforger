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

            /// <summary>Several steps done together, such as moving a selection; undone in reverse order.</summary>
            public List<ObjectEditStep> Group;
        }

        private const int HistoryLimit = 200;
        private const double PlacementRotationStep = 15.0;

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
        private Label newMapTemplateLabel;
        private TextField newMapFolderField;
        private TextField newMapNameField;
        private Label newMapTargetLabel;
        private Button newMapConfirmButton;
        private string newMapTemplatePath;
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
            newMapTemplateLabel = root.Q<Label>("new-map-template-label");
            newMapFolderField = root.Q<TextField>("new-map-folder-field");
            newMapNameField = root.Q<TextField>("new-map-name-field");
            newMapTargetLabel = root.Q<Label>("new-map-target-label");
            newMapConfirmButton = root.Q<Button>("new-map-confirm-button");
            var newMapTemplateButton = root.Q<Button>("new-map-template-button");
            var newMapCancelButton = root.Q<Button>("new-map-cancel-button");
            var fileMenu = root.Q<VisualElement>("file-menu");
            var helpMenu = root.Q<VisualElement>("help-menu");
            var viewMenu = root.Q<VisualElement>("view-menu");
            editMenu = root.Q<VisualElement>("edit-menu");
            var projectPanelMenuItem = root.Q<Button>("project-panel-menu-item");
            var inspectorMenuItem = root.Q<Button>("inspector-menu-item");
            var placeObjectMenuItem = root.Q<Button>("place-object-menu-item");
            var deleteObjectMenuItem = root.Q<Button>("delete-object-menu-item");
            var undoMenuItem = root.Q<Button>("undo-menu-item");
            var redoMenuItem = root.Q<Button>("redo-menu-item");
            objectsPanelHost = root.Q<VisualElement>("tool-panel-host");
            if (objectsPanelHost != null)
            {
                objectsPanel = new ObjectLibraryPanel();
                objectsPanel.ObjectChosen += StartPlacement;
                objectsPanel.CloseRequested += () => ShowObjectsPanel(false);
                objectsPanelHost.Add(objectsPanel);
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

            var mapView = root.Q<VisualElement>("map-view");
            if (mapView != null)
            {
                mapCanvas = new MapCanvasElement();
                mapCanvas.ObjectSelected += (tile, item) => SelectObject(tile, item, false);
                mapCanvas.TileClicked += tile => SelectTile(tile, false);
                mapCanvas.ObjectToggled += ToggleObject;
                mapCanvas.ObjectsBoxSelected += AddToSelection;
                mapCanvas.ObjectsEdited += CommitEdits;
                mapCanvas.PlacementRequested += PlaceChosenObject;
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
            if (newMapTemplateButton != null)
                newMapTemplateButton.clicked += ChooseNewMapTemplate;
            newMapFolderField?.RegisterValueChangedCallback(evt =>
            {
                // The display name follows the folder name until it is changed by hand.
                if (newMapNameField != null && newMapNameField.value == evt.previousValue)
                    newMapNameField.SetValueWithoutNotify(evt.newValue);
                UpdateNewMapTarget();
            });
            newMapNameField?.RegisterValueChangedCallback(_ => UpdateNewMapTarget());
            if (newMapConfirmButton != null)
                newMapConfirmButton.clicked += CreateNewMap;
            if (newMapCancelButton != null)
                newMapCancelButton.clicked += () => HidePopup(newMapDialog);
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
                mapCanvas.ObjectRenderer = objectRenderer;
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
            groundRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            if (objectRenderer != null)
                objectRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            view3DButton?.EnableInClassList("view-control-active", mapCanvas.ThreeDimensional);
            UpdateMapHeader();
            UpdateZoomLabel();
            SetStatus(mapCanvas.ThreeDimensional
                ? "3D view: right drag turns, middle drag moves, the wheel zooms. Click an object to select it" +
                  (session != null && session.CanEdit
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

            if (int.TryParse(Argument("-select", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectId))
            {
                foreach (var tile in currentSummary.Tiles)
                {
                    var item = tile.Objects.Find(candidate => candidate.Id == selectId);
                    if (item == null)
                        continue;
                    SelectObject(tile, item, false);
                    break;
                }
            }

            var toolName = Argument("-tool", 1);
            if (toolName != null && Enum.TryParse<EditorTool>(toolName, true, out var chosenTool))
                SetTool(chosenTool);

            if (Array.IndexOf(args, "-plain") >= 0 && groundRenderer != null && mapCanvas != null && !mapCanvas.ThreeDimensional)
                SetTextures(false);

            if (Array.IndexOf(args, "-check") >= 0)
                ValidateLoadedMap();

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
            }

            var screenshot = Argument("-screenshot", 1);
            if (screenshot != null)
            {
                var started = Time.realtimeSinceStartup;
                while ((groundRenderer.IsLoading || objectRenderer.IsLoading || outlinesBuilding || checkTask != null ||
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
            var projectPath = WindowsFileDialog.OpenFile("Open a Mapforge project", WindowsFileDialog.ProjectFilter);
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
                    throw new InvalidDataException("The selected file does not contain a Mapforge project.");
                if (project.formatVersion != MapforgeProject.CurrentFormatVersion)
                    throw new InvalidDataException("This Mapforge project uses an unsupported format version: " + project.formatVersion + ".");
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
                var projectPath = WindowsFileDialog.SaveFile("Save Mapforge project", WindowsFileDialog.ProjectFilter,
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
            var previousTile = selectedTile;
            var previousObjectId = selectedObject?.Id;

            session = newSession;
            currentSummary = summary;
            currentProjectName = projectName ?? string.Empty;
            undoSteps.Clear();
            redoSteps.Clear();
            StopPlacement();
            ShowObjectsPanel(false);
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
                mapCanvas.AllowEditing = session.CanEdit;
                // A map without a project can only be looked at; selecting is the only tool then.
                if (!session.CanEdit)
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
                      (session != null && session.CanEdit ? "" : " - read-only; save a project to edit"));
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
            if (session == null || !session.CanEdit)
            {
                SetStatus("The map is read-only. Save a Mapforge project (File > Save map project as...) to edit it.");
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
            mapCanvas?.Refresh();
            objectRenderer?.RefreshPlacements();
            groundRenderer?.RefreshTerrainHoles(currentSummary);
            foreach (var tile in browserEntries.Keys)
                UpdateBrowserEntry(tile);
            UpdateCounts();
            UpdateMapStateLabel();
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
                if (evt.keyCode == KeyCode.Escape && mapCanvas != null && mapCanvas.IsPlacing)
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
                    SetStatus("Placing " + placementAsset + " at heading " + mapCanvas.PlacementHeading.ToString("0") + "°.");
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Delete && selection.Count > 0)
                {
                    DeleteSelectedObjects();
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
            if (session == null || !session.CanEdit || from.Count == 0)
            {
                SetStatus(undo ? "Nothing to undo." : "Nothing to redo.");
                return;
            }

            var step = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
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

        private static void Touch(List<MapSelectionItem> touched, OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (!touched.Exists(entry => ReferenceEquals(entry.Record, item)))
                touched.Add(new MapSelectionItem { Tile = tile, Record = item });
        }

        private static string DescribeStep(ObjectEditStep step)
        {
            if (step.Group != null)
            {
                var ids = new HashSet<int>();
                CollectIds(step, ids);
                return "the change of " + ids.Count + " objects";
            }
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
            if (session == null || !session.CanEdit)
            {
                SetStatus("The map is read-only. Save a Mapforge project (File > Save map project as...) to edit it.");
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
            if (!session.CanEdit)
            {
                SetStatus("The map is read-only. Save a Mapforge project (File > Save map project as...) to place objects.");
                return;
            }

            UpdateObjectsCatalog();
            StartInstallationScan();
            objectsPanelOpen = true;
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
        /// and the previews nEditor cached, and reads from Steam's file lists which of them belong to the base game, a DLC or
        /// neither.
        /// </summary>
        private void StartInstallationScan()
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (installationScan != null && string.Equals(root, installationScanRoot, StringComparison.OrdinalIgnoreCase))
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
                              installationCatalog.Objects.Count + " objects, " + installationCatalog.Thumbnails.Count + " nEditor previews.");
                }
                objectsPanel.SetCatalog(installationCatalog, usedAssetCounts);
            }).Every(150);
        }

        /// <summary>Starts placement mode with an object file relative to the OMSI installation.</summary>
        private void StartPlacement(string assetPath)
        {
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
            UpdateToolbar();
            mapCanvas?.Focus();
            SetStatus("Placing " + assetPath + ": click into the map. Shift+click keeps placing, R turns the preview, Esc stops." + (problem ?? string.Empty));
        }

        private void PlaceChosenObject(MapPlacement placement)
        {
            if (placementAsset == null || session == null || !session.CanEdit)
            {
                StopPlacement();
                return;
            }
            if (placement.Tile == null || !placement.Tile.IsAvailable)
            {
                SetStatus(placement.Tile == null ? "Click inside a map tile to place the object." : "The tile file " + placement.Tile.FileName + " is missing.");
                return;
            }

            OmsiObjectChange change;
            try
            {
                change = session.PlaceObject(currentSummary, placement.Tile, placementAsset, placement.LocalX, placement.LocalY, 0, mapCanvas.PlacementHeading);
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
                      (placementHasAbsoluteHeight ? " Its height is absolute, not above the terrain; set Height in the inspector." : string.Empty));
        }

        private void StopPlacement()
        {
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
            if (session == null || !session.CanEdit || selectedTile == null)
                return;
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
                SetStatus("Exports are built from a project workspace. Save a Mapforge project first.");
                return;
            }
            if (exportDialog == null || exportFolderField == null || exportNameField == null)
                return;

            var displayName = string.IsNullOrWhiteSpace(currentSummary.FriendlyName) ? currentSummary.Name : currentSummary.FriendlyName;
            exportFolderField.SetValueWithoutNotify(Path.GetFileName(OmsiMapExporter.SuggestTargetFolder(session.Workspace)));
            exportNameField.SetValueWithoutNotify(displayName + " (Mapforge)");
            UpdateExportTarget();
            exportDialog.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Opens the dialog for a new map. The template is the given map, else the open map, else the map opened last;
        /// the user can choose another one.
        /// </summary>
        private void OpenNewMapDialog(string templateGlobalConfigPath)
        {
            if (newMapDialog == null || newMapFolderField == null || newMapNameField == null)
                return;
            newMapTemplatePath = templateGlobalConfigPath ?? currentSummary?.GlobalConfigPath;
            if (newMapTemplatePath == null)
                foreach (var entry in RecentFiles.Load())
                {
                    newMapTemplatePath = entry.IsProject ? ProjectMap(entry.Path) : entry.Path;
                    if (newMapTemplatePath != null && File.Exists(newMapTemplatePath))
                        break;
                    newMapTemplatePath = null;
                }
            newMapFolderField.SetValueWithoutNotify("New map");
            newMapNameField.SetValueWithoutNotify("New map");
            UpdateNewMapTarget();
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

        private void ChooseNewMapTemplate()
        {
            var path = WindowsFileDialog.OpenFile("Choose a map as template for the new map", WindowsFileDialog.GlobalConfigFilter);
            if (string.IsNullOrEmpty(path))
                return;
            newMapTemplatePath = path;
            UpdateNewMapTarget();
        }

        /// <summary>The problem that keeps the new map from being created, or null; also the folder it would go to.</summary>
        private string NewMapProblem(out string target)
        {
            target = null;
            if (string.IsNullOrEmpty(newMapTemplatePath) || !File.Exists(newMapTemplatePath))
                return "Choose a map of your OMSI installation as template; the new map goes next to it.";
            var mapsFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(newMapTemplatePath)));
            var folderName = newMapFolderField.value ?? string.Empty;
            var problem = OmsiNewMap.CheckFolderName(mapsFolder, folderName);
            if (problem != null)
                return problem;
            target = Path.Combine(mapsFolder, folderName);
            return string.IsNullOrWhiteSpace(newMapNameField.value) ? "Enter the name OMSI shows for the new map." : null;
        }

        private void UpdateNewMapTarget()
        {
            if (newMapTargetLabel == null)
                return;
            var problem = NewMapProblem(out var target);
            if (newMapTemplateLabel != null)
                newMapTemplateLabel.text = string.IsNullOrEmpty(newMapTemplatePath)
                    ? "No template chosen"
                    : "Template: " + Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(newMapTemplatePath)));
            newMapTargetLabel.text = problem ?? "Creates the map folder:\n" + target + "\nThen choose where to save its project.";
            newMapTargetLabel.EnableInClassList("export-problem", problem != null);
            newMapConfirmButton?.SetEnabled(problem == null);
        }

        /// <summary>Creates the map, saves a project for it where the user says, and opens it for editing.</summary>
        private void CreateNewMap()
        {
            var problem = NewMapProblem(out _);
            if (problem != null)
            {
                SetStatus(problem);
                return;
            }
            OmsiNewMapReport report;
            try
            {
                report = OmsiNewMap.Create(newMapTemplatePath, newMapFolderField.value, newMapNameField.value, DateTime.Now);
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
                    WindowsFileDialog.ProjectExtension, SanitizeFileName(newMapFolderField.value) + "." + WindowsFileDialog.ProjectExtension);
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
                          fullProjectPath + ". Select '" + newMapNameField.value.Trim() + "' in OMSI to try it.");
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
                                     (Directory.Exists(target) ? "Replaces the earlier Mapforge export in:\n" : "Creates the map folder:\n") +
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
            var mode = session != null && session.CanEdit
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
            if (tool != EditorTool.Drag && (session == null || !session.CanEdit))
            {
                SetStatus("The map is read-only. Save a Mapforge project (File > Save map project as...) to move or rotate objects.");
                return;
            }
            // Another tool ends placing and closes the object library.
            ShowObjectsPanel(false);
            activeTool = tool;
            if (mapCanvas != null)
                mapCanvas.Tool = tool;
            UpdateToolbar();
            SetStatus(tool == EditorTool.Move ? "Move: drag an arrow to move the selection east, north or, in 3D, up; Shift moves in whole metres."
                : tool == EditorTool.Rotate ? "Rotate: drag the round handle from above or a ring in 3D to turn the selected object; Shift snaps to 15°."
                : "Drag: click objects to select them and drag them to move them freely; Ctrl+click and Ctrl+drag select several.");
        }

        private void UpdateToolbar()
        {
            toolbar?.ShowState(activeTool, placementAsset != null || objectsPanelOpen, session != null && session.CanEdit, selection.Count > 0);
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
                return;

            var summary = currentSummary;
            var source = session.Source;
            var installationRoot = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var configName = Path.GetFileName(session.GlobalConfigPath);
            checkSections?.Clear();
            if (checkSummaryLabel != null)
                checkSummaryLabel.text = "Checking " + (string.IsNullOrWhiteSpace(summary.FriendlyName) ? summary.Name : summary.FriendlyName) + "...";
            if (checkDialog != null)
                checkDialog.style.display = DisplayStyle.Flex;
            SetStatus("Checking the map...");
            var task = Task.Run(() => OmsiMapCheck.Run(summary, source, installationRoot, configName));
            checkTask = task;
            IVisualElementScheduledItem poll = null;
            poll = rootElement.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                checkTask = null;
                if (task.IsFaulted || !ReferenceEquals(summary, currentSummary))
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
                mapHintLabel.text = view + (session != null && session.CanEdit
                    ? "\nTools: Q drag objects freely, W move along axis arrows (Shift: whole metres), E rotate (round handle from above, rings in 3D; Shift: 15° steps), " +
                      "Del delete, 3 place objects.\nCtrl+click or Ctrl+drag selects several. Ctrl+Z / Ctrl+Y undo and redo."
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
            else if (IsTileEdited(selectedTile))
                AddRevertTileButton(records);

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
                AddInspectorRecord(records, details, "inspector-record");
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

            var canEdit = session != null && session.CanEdit;
            if (canEdit)
            {
                var delete = new Button(DeleteSelectedObjects) { text = "Delete " + selection.Count + " objects" };
                delete.AddToClassList("map-control-button");
                delete.AddToClassList("inspector-edit-delete");
                editor.Add(delete);
            }
            var hint = new Label(canEdit
                ? "Drag one of them on the map to move all. Ctrl+click adds or removes an object, Esc clears the selection."
                : "Read-only. Save a Mapforge project (File > Save map project as...) to edit this map.");
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

            var xField = AddEditField(editor, "X (m)", item.LocalX);
            var yField = AddEditField(editor, "Y (m)", item.LocalY);
            var zField = AddEditField(editor, "Height (m)", item.Height);
            var headingField = AddEditField(editor, "Heading (°)", item.HeadingDegrees);
            var pitchField = AddEditField(editor, "Pitch (°)", item.PitchDegrees);
            var bankField = AddEditField(editor, "Bank (°)", item.BankDegrees);

            var canEdit = session != null && session.CanEdit;
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
                if (IsTileEdited(selectedTile))
                    AddRevertTileButton(editor);
            }
            else
            {
                foreach (var field in new[] { xField, yField, zField, headingField, pitchField, bankField })
                    field.SetEnabled(false);
                var hint = new Label("Read-only. Save a Mapforge project (File > Save map project as...) to edit this map.");
                hint.AddToClassList("muted");
                hint.AddToClassList("inspector-edit-hint");
                editor.Add(hint);
            }

            container.Add(editor);
        }

        private static TextField AddEditField(VisualElement container, string label, double value)
        {
            var field = new TextField(label) { value = OmsiNumberFormat.Format(value) };
            field.AddToClassList("inspector-edit-field");
            container.Add(field);
            return field;
        }

        private void AddRevertTileButton(VisualElement container)
        {
            var revert = new Button(RevertSelectedTile) { text = "Revert tile edits" };
            revert.AddToClassList("map-control-button");
            revert.AddToClassList("inspector-edit-revert");
            container.Add(revert);
        }
    }
}
