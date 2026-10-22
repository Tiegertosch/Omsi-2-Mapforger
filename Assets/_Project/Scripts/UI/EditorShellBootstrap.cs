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
        private Label mapCountsLabel;
        private Label mapDescriptionLabel;
        private Label mapWarningsLabel;
        private Label mapHintLabel;
        private Label zoomLabel;
        private Label mapOverlayLabel;
        private Button emptyStateOpenButton;
        private Button toolsButton;
        private VisualElement mapLegend;
        private VisualElement mapControls;
        private VisualElement projectPanel;
        private VisualElement inspectorPanel;
        private MapCanvasElement mapCanvas;
        private MapGroundRenderer groundRenderer;
        private MapObjectRenderer objectRenderer;
        private VisualElement rootElement;
        private Button groundToggleButton;
        private Button view3DButton;
        private OmsiTileSummary selectedTile;
        private OmsiObjectRecord selectedObject;

        // All selected objects; selectedObject is the last of them.
        private readonly List<MapSelectionItem> selection = new List<MapSelectionItem>();
        private string currentProjectName = string.Empty;
        private OmsiMapSession session;
        private VisualElement exportDialog;
        private TextField exportFolderField;
        private TextField exportNameField;
        private Label exportTargetLabel;
        private Button exportConfirmButton;
        private VisualElement editMenu;
        private VisualElement placeDialog;
        private TextField placeSearchField;
        private Toggle placeUsedToggle;
        private ListView placeList;
        private Label placeInfoLabel;
        private Button placeConfirmButton;
        private Label browserCountsLabel;

        // Scenery objects to place: the .sco files of the OMSI installation, listed once in the background.
        private readonly List<string> placeItems = new List<string>();
        private readonly Dictionary<string, int> usedAssetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private List<string> installationAssets;
        private Task<List<string>> installationScan;
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
            var fileMenu = root.Q<VisualElement>("file-menu");
            var helpMenu = root.Q<VisualElement>("help-menu");
            editMenu = root.Q<VisualElement>("edit-menu");
            var placeObjectMenuItem = root.Q<Button>("place-object-menu-item");
            var deleteObjectMenuItem = root.Q<Button>("delete-object-menu-item");
            var undoMenuItem = root.Q<Button>("undo-menu-item");
            var redoMenuItem = root.Q<Button>("redo-menu-item");
            placeDialog = root.Q<VisualElement>("place-dialog");
            placeSearchField = root.Q<TextField>("place-search-field");
            placeUsedToggle = root.Q<Toggle>("place-used-toggle");
            placeList = root.Q<ListView>("place-list");
            placeInfoLabel = root.Q<Label>("place-info-label");
            placeConfirmButton = root.Q<Button>("place-confirm-button");
            var placeCancelButton = root.Q<Button>("place-cancel-button");
            emptyStateOpenButton = root.Q<Button>("empty-state-open-button");
            statusLabel = root.Q<Label>("status-message");
            mapStateLabel = root.Q<Label>("map-state");
            mapTitleLabel = root.Q<Label>("map-title");
            mapCountsLabel = root.Q<Label>("map-counts");
            mapDescriptionLabel = root.Q<Label>("map-description");
            mapWarningsLabel = root.Q<Label>("map-warnings");
            mapHintLabel = root.Q<Label>("map-hint");
            mapLegend = root.Q<VisualElement>("map-legend");
            mapControls = root.Q<VisualElement>("map-controls");
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
                    if (mapOverlayLabel != null)
                        mapOverlayLabel.text = text;
                    UpdateZoomLabel();
                };
                mapView.Add(mapCanvas);
                mapOverlayLabel = new Label { pickingMode = PickingMode.Ignore };
                mapOverlayLabel.AddToClassList("map-overlay");
                mapView.Add(mapOverlayLabel);
            }

            if (fileButton != null)
                fileButton.clicked += () => TogglePopup(fileMenu, helpMenu, editMenu);
            if (openMapMenuItem != null)
                openMapMenuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenMap();
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
            if (emptyStateOpenButton != null)
                emptyStateOpenButton.clicked += OpenMap;
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
                viewButton.clicked += () => ToggleSidePanels(projectPanel, inspectorPanel);
            if (editButton != null)
                editButton.clicked += () => TogglePopup(editMenu, fileMenu, helpMenu);
            if (placeObjectMenuItem != null)
                placeObjectMenuItem.clicked += () =>
                {
                    HidePopup(editMenu);
                    OpenPlaceDialog();
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
            if (placeList != null)
            {
                placeList.makeItem = () =>
                {
                    var item = new Label();
                    item.AddToClassList("place-list-item");
                    return item;
                };
                placeList.bindItem = (element, index) => ((Label)element).text = DescribePlaceItem(placeItems[index]);
                placeList.itemsSource = placeItems;
                placeList.selectionChanged += _ => UpdatePlaceInfo();
                placeList.itemsChosen += _ => ConfirmPlacement();
            }
            placeSearchField?.RegisterValueChangedCallback(_ => RefreshPlaceList());
            placeUsedToggle?.RegisterValueChangedCallback(_ => RefreshPlaceList());
            if (placeConfirmButton != null)
                placeConfirmButton.clicked += ConfirmPlacement;
            if (placeCancelButton != null)
                placeCancelButton.clicked += () => HidePopup(placeDialog);
            if (toolsButton != null)
            {
                toolsButton.SetEnabled(false);
                toolsButton.clicked += ValidateLoadedMap;
            }
            if (helpButton != null)
                helpButton.clicked += () => TogglePopup(helpMenu, fileMenu, editMenu);
            if (helpCloseButton != null)
                helpCloseButton.clicked += () => HidePopup(helpMenu);

            // Shortcuts work anywhere except inside a text field, which keeps its own text undo and Delete key.
            root.RegisterCallback<KeyDownEvent>(OnShortcut, TrickleDown.TrickleDown);

            CreateBackgroundCamera();
            groundRenderer = new GameObject("Map Ground").AddComponent<MapGroundRenderer>();
            groundRenderer.Loaded += SetStatus;
            objectRenderer = groundRenderer.gameObject.AddComponent<MapObjectRenderer>();
            objectRenderer.SetCamera(groundRenderer.ViewCamera);
            objectRenderer.Loaded += SetStatus;
            objectRenderer.ModelsChanged += () => mapCanvas?.UpdateModelFlags();
            if (mapCanvas != null)
            {
                mapCanvas.HasModel = objectRenderer.HasModel;
                mapCanvas.MovePreviewed += (items, offsetX, offsetY) =>
                {
                    var objects = new List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>>(items.Count);
                    foreach (var item in items)
                        objects.Add(new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(item.Tile, item.Record));
                    objectRenderer.PreviewMove(objects, offsetX, offsetY);
                };
                mapCanvas.HeadingPreviewed += objectRenderer.PreviewHeading;
                mapCanvas.PreviewEnded += objectRenderer.EndPreview;
            }
        }

        /// <summary>Keeps the ground camera on the screen rectangle and view of the map view.</summary>
        private void LateUpdate()
        {
            if (groundRenderer == null || mapCanvas == null || rootElement == null)
                return;
            var panelWidth = rootElement.worldBound.width;
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
            if (mapCanvas.ThreeDimensional)
                mapCanvas.LeaveOrbitView();
            else
            {
                StopPlacement();
                mapCanvas.EnterOrbitView();
            }
            groundRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            if (objectRenderer != null)
                objectRenderer.ThreeDimensional = mapCanvas.ThreeDimensional;
            if (view3DButton != null)
                view3DButton.text = mapCanvas.ThreeDimensional ? "3D: on" : "3D: off";
            UpdateMapHeader();
            SetStatus(mapCanvas.ThreeDimensional
                ? "3D view: right drag turns, left or middle drag moves, the wheel zooms. Switch 3D off to edit."
                : "Back to the map from above.");
        }

        private void ToggleGround()
        {
            if (groundRenderer == null || mapCanvas == null)
                return;
            groundRenderer.GroundVisible = !groundRenderer.GroundVisible;
            if (objectRenderer != null)
                objectRenderer.ObjectsVisible = groundRenderer.GroundVisible;
            mapCanvas.TexturedView = groundRenderer.GroundVisible;
            mapCanvas.Refresh();
            if (groundToggleButton != null)
                groundToggleButton.text = groundRenderer.GroundVisible ? "Textures: on" : "Textures: off";
            SetStatus(groundRenderer.GroundVisible ? "Ground textures shown." : "Ground textures hidden; tiles are drawn plain.");
        }

        private void OpenMap()
        {
            try
            {
                var path = WindowsFileDialog.OpenFile("Open an OMSI map configuration", WindowsFileDialog.GlobalConfigFilter);
                if (string.IsNullOrEmpty(path))
                    return;

                LoadMap(path, string.Empty, null, false);
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
        /// <c>-screenshot file.png</c>, taken once ground, objects and outlines are loaded, and <c>-quit</c>.
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
                yield break;

            if (projectPath != null)
                OpenProjectFile(projectPath);
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

            var screenshot = Argument("-screenshot", 1);
            if (screenshot != null)
            {
                var started = Time.realtimeSinceStartup;
                while ((groundRenderer.IsLoading || objectRenderer.IsLoading || outlinesBuilding) && Time.realtimeSinceStartup - started < 300f)
                    yield return null;
                for (var frame = 0; frame < 10; frame++)
                    yield return null;
                // At the end of the frame the screen holds the cameras and the UI on top.
                yield return new WaitForEndOfFrame();
                var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                shot.Apply();
                File.WriteAllBytes(Path.GetFullPath(screenshot), shot.EncodeToPNG());
                Destroy(shot);
                Debug.Log("Screenshot saved: " + Path.GetFullPath(screenshot));
            }
            if (quit)
                Application.Quit();
        }

        private void OpenProject()
        {
            var projectPath = WindowsFileDialog.OpenFile("Open a Mapforge project", WindowsFileDialog.ProjectFilter);
            if (!string.IsNullOrEmpty(projectPath))
                OpenProjectFile(projectPath);
        }

        private void OpenProjectFile(string projectPath)
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
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not open project: " + exception.Message);
            }
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
            HidePopup(placeDialog);
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
                            // A transfer keeps height and heading; an edit from the inspector may change them too.
                            AddValueStep(parts, target, edit.Record, edit.Record.LocalX, edit.Record.LocalY, edit.Height, edit.HeadingDegrees);
                            continue;
                        }
                        kept.Add("#" + edit.Record.Id + " (" + DescribeReferences(result.Blockers) + ")");
                    }
                    AddValueStep(parts, edit.Tile, edit.Record, edit.LocalX, edit.LocalY, edit.Height, edit.HeadingDegrees);
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

        private void AddValueStep(List<ObjectEditStep> parts, OmsiTileSummary tile, OmsiObjectRecord item, double x, double y, double z, double heading)
        {
            var before = Values(item);
            if (session.UpdateObject(tile, item, x, y, z, heading))
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
            return new[] { item.LocalX, item.LocalY, item.Height, item.HeadingDegrees };
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
            if (evt.keyCode == KeyCode.Escape && (IsShown(placeDialog) || IsShown(exportDialog)))
            {
                HidePopup(placeDialog);
                HidePopup(exportDialog);
                evt.StopPropagation();
                return;
            }
            var focused = evt.target as VisualElement;
            if (focused is TextField || focused?.GetFirstAncestorOfType<TextField>() != null || IsShown(placeDialog) || IsShown(exportDialog))
                return;

            if (!(evt.ctrlKey || evt.commandKey))
            {
                if (evt.keyCode == KeyCode.Escape && mapCanvas != null && mapCanvas.IsPlacing)
                {
                    StopPlacement();
                    SetStatus("Placement stopped.");
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
                else if (evt.keyCode == KeyCode.Delete && selection.Count > 0 && (mapCanvas == null || !mapCanvas.ThreeDimensional))
                {
                    DeleteSelectedObjects();
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
            session.UpdateObject(tile, item, target[0], target[1], target[2], target[3]);
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

        private void OpenPlaceDialog()
        {
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
            if (placeDialog == null || placeList == null)
                return;

            usedAssetCounts.Clear();
            foreach (var tile in currentSummary.Tiles)
                foreach (var item in tile.Objects)
                    usedAssetCounts[item.AssetPath] = usedAssetCounts.TryGetValue(item.AssetPath, out var count) ? count + 1 : 1;
            StartInstallationScan();
            RefreshPlaceList();
            placeDialog.style.display = DisplayStyle.Flex;
            placeSearchField?.Focus();
        }

        /// <summary>Lists the .sco files below Sceneryobjects of the OMSI installation once, on a worker thread.</summary>
        private void StartInstallationScan()
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (installationScan != null && string.Equals(root, installationScanRoot, StringComparison.OrdinalIgnoreCase))
                return;

            installationScanRoot = root;
            installationAssets = null;
            var scan = Task.Run(() =>
            {
                var result = new List<string>();
                var folder = Path.Combine(root, "Sceneryobjects");
                if (!Directory.Exists(folder))
                    return result;
                foreach (var path in Directory.EnumerateFiles(folder, "*.sco", SearchOption.AllDirectories))
                    result.Add(path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar));
                result.Sort(StringComparer.OrdinalIgnoreCase);
                return result;
            });
            installationScan = scan;

            IVisualElementScheduledItem poll = null;
            poll = placeDialog.schedule.Execute(() =>
            {
                if (!scan.IsCompleted)
                    return;
                poll.Pause();
                if (!ReferenceEquals(scan, installationScan))
                    return;
                if (scan.IsFaulted)
                {
                    Debug.LogException(scan.Exception);
                    installationAssets = new List<string>();
                    SetStatus("Could not list the scenery objects of the OMSI installation: " + scan.Exception?.GetBaseException().Message);
                }
                else
                {
                    installationAssets = scan.Result;
                }
                if (IsShown(placeDialog))
                    RefreshPlaceList();
            }).Every(150);
        }

        private void RefreshPlaceList()
        {
            if (placeList == null)
                return;

            var usedOnly = placeUsedToggle == null || placeUsedToggle.value;
            var candidates = new SortedSet<string>(usedAssetCounts.Keys, StringComparer.OrdinalIgnoreCase);
            if (!usedOnly && installationAssets != null)
                candidates.UnionWith(installationAssets);

            var terms = (placeSearchField?.value ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var previous = SelectedPlaceItem();
            placeItems.Clear();
            foreach (var candidate in candidates)
            {
                var matches = true;
                foreach (var term in terms)
                    if (candidate.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        matches = false;
                        break;
                    }
                if (matches)
                    placeItems.Add(candidate);
            }
            placeList.RefreshItems();
            var previousIndex = previous == null ? -1 : placeItems.FindIndex(item => string.Equals(item, previous, StringComparison.OrdinalIgnoreCase));
            if (previousIndex >= 0)
                placeList.SetSelectionWithoutNotify(new[] { previousIndex });
            else
                placeList.ClearSelection();
            UpdatePlaceInfo();
        }

        private string DescribePlaceItem(string assetPath)
        {
            return usedAssetCounts.TryGetValue(assetPath, out var count)
                ? assetPath + "    (" + count + "× in this map)"
                : assetPath;
        }

        private string SelectedPlaceItem()
        {
            var index = placeList?.selectedIndex ?? -1;
            return index >= 0 && index < placeItems.Count ? placeItems[index] : null;
        }

        private void UpdatePlaceInfo()
        {
            if (placeInfoLabel == null)
                return;

            var usedOnly = placeUsedToggle == null || placeUsedToggle.value;
            var scanning = !usedOnly && installationAssets == null;
            var selected = SelectedPlaceItem();
            placeInfoLabel.text = (selected ?? placeItems.Count + " object(s)" + (scanning ? "; listing the OMSI installation..." : string.Empty)) +
                                  (selected == null ? "\nSelect an object and choose Place, or double-click it." : string.Empty);
            placeConfirmButton?.SetEnabled(selected != null);
        }

        /// <summary>Closes the dialog and starts placement mode with the chosen object.</summary>
        private void ConfirmPlacement()
        {
            var assetPath = SelectedPlaceItem();
            if (assetPath == null)
                return;
            HidePopup(placeDialog);

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
            SetStatus("Placed object #" + change.Record.Id + " (" + assetPath + ") in tile " + placement.Tile.X + ", " + placement.Tile.Y +
                      ". Ctrl+Z undoes it." +
                      (placementHasAbsoluteHeight ? " Its height is absolute, not above the terrain; set Height in the inspector." : string.Empty));
        }

        private void StopPlacement()
        {
            placementAsset = null;
            mapCanvas?.EndPlacement();
        }

        private void ApplyObjectEdit(OmsiObjectRecord item, TextField xField, TextField yField, TextField zField, TextField headingField)
        {
            if (selectedTile == null)
                return;
            if (!TryParseField(xField, "X", out var x) || !TryParseField(yField, "Y", out var y) ||
                !TryParseField(zField, "Height", out var z) || !TryParseField(headingField, "Heading", out var heading))
                return;
            CommitEdits(new List<MapObjectEdit>
            {
                new MapObjectEdit { Tile = selectedTile, Record = item, LocalX = x, LocalY = y, Height = z, HeadingDegrees = heading }
            });
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

        private void ToggleSidePanels(VisualElement projectPanel, VisualElement inspectorPanel)
        {
            var show = projectPanel != null && projectPanel.resolvedStyle.display == DisplayStyle.None;
            if (projectPanel != null)
                projectPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (inspectorPanel != null)
                inspectorPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            SetStatus(show ? "Side panels shown" : "Side panels hidden");
        }

        private void ValidateLoadedMap()
        {
            if (currentSummary == null)
            {
                SetStatus("Open a map before running validation.");
                return;
            }

            var continuity = OmsiSplineContinuity.Check(currentSummary);
            var worstGap = continuity.Gaps.Count > 0 ? continuity.Gaps[0] : null;
            if (worstGap != null)
                SelectTile(worstGap.Tile, true);

            SetStatus(DescribeMapCheck(currentSummary, continuity));
        }

        private static string DescribeMapCheck(OmsiMapSummary summary, OmsiSplineContinuityReport continuity)
        {
            var importPart = summary.Warnings.Count == 0
                ? "no import warnings"
                : summary.Warnings.Count + " import warning(s), see the map header";
            var splinePart = continuity.ConnectedCount + " of " + continuity.LinkCount + " spline links meet";
            if (continuity.Gaps.Count > 0)
            {
                var worstGap = continuity.Gaps[0];
                splinePart += "; " + continuity.Gaps.Count + " gap(s) above " + continuity.ToleranceMeters.ToString("0.0#") +
                              " m, largest " + worstGap.DistanceMeters.ToString("0.00") + " m between splines " +
                              worstGap.SplineId + " and " + worstGap.NeighbourId +
                              (worstGap.IsMutual ? string.Empty : ", referenced from one side only") + " (selected tile)";
            }
            if (continuity.MissingNeighbourCount > 0)
                splinePart += "; " + continuity.MissingNeighbourCount + " link(s) point to splines that were not loaded";

            var passed = summary.Warnings.Count == 0 && continuity.Gaps.Count == 0 && continuity.MissingNeighbourCount == 0;
            return (passed ? "Map check passed: " : "Map check: ") + importPart + "; " + splinePart + ".";
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
            {
                mapTitleLabel.text = displayName;
                mapTitleLabel.RemoveFromClassList("viewport-label");
                mapTitleLabel.AddToClassList("map-title");
            }
            UpdateCounts();
            if (mapCountsLabel != null)
                mapCountsLabel.style.display = DisplayStyle.Flex;
            if (mapDescriptionLabel != null)
            {
                mapDescriptionLabel.text = currentSummary.Description ?? string.Empty;
                mapDescriptionLabel.style.display = string.IsNullOrWhiteSpace(currentSummary.Description)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
            if (mapWarningsLabel != null)
            {
                var warnings = currentSummary.Warnings;
                mapWarningsLabel.text = warnings.Count == 0
                    ? string.Empty
                    : warnings.Count + " import warning(s): " + warnings[0] + (warnings.Count > 1 ? "  (hover for all)" : string.Empty);
                mapWarningsLabel.tooltip = string.Join("\n", warnings.GetRange(0, Math.Min(warnings.Count, 40)));
                mapWarningsLabel.style.display = warnings.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (mapHintLabel != null)
            {
                mapHintLabel.text = mapCanvas != null && mapCanvas.ThreeDimensional
                    ? "3D view   Right drag: turn   Left or middle drag: move   Wheel: zoom   3D: off to edit"
                    : session != null && session.CanEdit
                    ? "Wheel: zoom   Right or middle drag: pan   Drag an object: move   Drag the round handle: rotate (Shift: 15° steps)   Ctrl+click or Ctrl+drag: select several   Edit > Place object...   Del: delete   Ctrl+Z / Ctrl+Y: undo / redo"
                    : "Wheel: zoom   Drag: pan   Click an object to inspect it   Save a project to move objects";
                mapHintLabel.style.display = DisplayStyle.Flex;
            }
            if (mapLegend != null)
                mapLegend.style.display = DisplayStyle.Flex;
            if (mapControls != null)
                mapControls.style.display = DisplayStyle.Flex;
            if (emptyStateOpenButton != null)
                emptyStateOpenButton.style.display = DisplayStyle.None;
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel()
        {
            if (zoomLabel == null || mapCanvas == null)
                return;
            var metersPerPixel = mapCanvas.MetersPerPixel;
            zoomLabel.text = metersPerPixel >= 1f
                ? metersPerPixel.ToString("0.#") + " m/px"
                : (1f / metersPerPixel).ToString("0.#") + " px/m";
        }

        private void RenderProjectBrowser()
        {
            if (projectPanel == null)
                return;

            projectPanel.Clear();
            browserEntries.Clear();
            var heading = new Label("PROJECT BROWSER");
            heading.AddToClassList("panel-heading");
            projectPanel.Add(heading);

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
                                          currentSummary.SplineCount + " splines";
            if (mapCountsLabel != null)
                mapCountsLabel.text = "Tiles: " + currentSummary.Tiles.Count + "    Objects: " + currentSummary.ObjectCount +
                                      "    Splines: " + currentSummary.SplineCount + "    Connections: " + currentSummary.AttachmentCount +
                                      "    Terrain: " + currentSummary.TerrainTileCount + "/" + currentSummary.Tiles.Count +
                                      (currentSummary.UsesWorldCoordinates
                                          ? "    World coordinates: " + currentSummary.TileSizeMeters.ToString("0.0") + " m tiles"
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
            var heading = new Label("INSPECTOR");
            heading.AddToClassList("panel-heading");
            inspectorPanel.Add(heading);

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
                              "  H " + item.Height.ToString("F1") + " m  •  " + item.HeadingDegrees.ToString("F1") + "°";
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

        /// <summary>Position and heading fields for the selected object; read-only without a project.</summary>
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

            var canEdit = session != null && session.CanEdit;
            if (canEdit)
            {
                var apply = new Button(() => ApplyObjectEdit(item, xField, yField, zField, headingField)) { text = "Apply" };
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
                foreach (var field in new[] { xField, yField, zField, headingField })
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
