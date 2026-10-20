using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>Creates the initial UI Toolkit shell when the player starts.</summary>
    public sealed class EditorShellBootstrap : MonoBehaviour
    {
        private OmsiMapSummary currentSummary;
        private OmsiSplineTileIndex splineIndex;
        private readonly Dictionary<OmsiTileSummary, MapTileVisualElement> tileVisuals = new Dictionary<OmsiTileSummary, MapTileVisualElement>();
        private readonly Dictionary<OmsiTileSummary, Button> browserEntries = new Dictionary<OmsiTileSummary, Button>();
        private Label statusLabel;
        private Label mapStateLabel;
        private Label mapTitleLabel;
        private Label mapCountsLabel;
        private Label mapDescriptionLabel;
        private Label zoomLabel;
        private Button emptyStateOpenButton;
        private Button toolsButton;
        private VisualElement mapLegend;
        private VisualElement mapControls;
        private VisualElement projectPanel;
        private VisualElement inspectorPanel;
        private ScrollView mapContent;
        private OmsiTileSummary selectedTile;
        private MapTileVisualElement selectedTileVisual;
        private float zoomScale = 1f;
        private bool fitMapToViewport = true;
        private Vector2 lastMapViewportSize = new Vector2(-1f, -1f);
        private float renderedTilePixels = -1f;
        private string currentProjectName = string.Empty;
        private OmsiMapSession session;
        private int? selectedObjectId;
        private VisualElement exportDialog;
        private TextField exportFolderField;
        private TextField exportNameField;
        private Label exportTargetLabel;
        private Button exportConfirmButton;

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
            emptyStateOpenButton = root.Q<Button>("empty-state-open-button");
            mapContent = root.Q<ScrollView>("map-content");
            // Measure the scroll view itself, not its inner viewport: the viewport shrinks whenever a scrollbar
            // appears, which would change the fitted tile size and re-render the map in an endless loop.
            if (mapContent != null)
                mapContent.RegisterCallback<GeometryChangedEvent>(OnMapViewportGeometryChanged);
            statusLabel = root.Q<Label>("status-message");
            mapStateLabel = root.Q<Label>("map-state");
            mapTitleLabel = root.Q<Label>("map-title");
            mapCountsLabel = root.Q<Label>("map-counts");
            mapDescriptionLabel = root.Q<Label>("map-description");
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

            if (fileButton != null)
                fileButton.clicked += () => TogglePopup(fileMenu, helpMenu);
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
                zoomOutButton.clicked += () => ChangeZoom(1f / 1.25f);
            if (zoomInButton != null)
                zoomInButton.clicked += () => ChangeZoom(1.25f);
            if (centerSelectionButton != null)
                centerSelectionButton.clicked += CenterSelectedTile;

            if (viewButton != null)
                viewButton.clicked += () => ToggleSidePanels(projectPanel, inspectorPanel);
            if (editButton != null)
                editButton.SetEnabled(false);
            if (toolsButton != null)
            {
                toolsButton.SetEnabled(false);
                toolsButton.clicked += ValidateLoadedMap;
            }
            if (helpButton != null)
                helpButton.clicked += () => TogglePopup(helpMenu, fileMenu);
            if (helpCloseButton != null)
                helpCloseButton.clicked += () => HidePopup(helpMenu);

            CreateBackgroundCamera();
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

        private void OpenProject()
        {
            try
            {
                var projectPath = WindowsFileDialog.OpenFile("Open a Mapforge project", WindowsFileDialog.ProjectFilter);
                if (string.IsNullOrEmpty(projectPath))
                    return;

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
        /// <param name="keepView">Keeps zoom, selected tile and selected object, for example after an edit.</param>
        private void LoadMap(string globalConfigPath, string projectName, string projectFilePath, bool keepView)
        {
            var newSession = string.IsNullOrEmpty(projectFilePath)
                ? OmsiMapSession.OpenReadOnly(globalConfigPath)
                : OmsiMapSession.OpenProject(globalConfigPath, projectFilePath);
            var summary = newSession.Read();
            var previousTile = selectedTile;

            session = newSession;
            splineIndex = OmsiSplineTileIndex.Build(summary);
            currentSummary = summary;
            currentProjectName = projectName ?? string.Empty;
            selectedTile = null;
            if (keepView && previousTile != null)
                selectedTile = summary.Tiles.Find(tile => tile.X == previousTile.X && tile.Y == previousTile.Y);
            if (selectedTile == null)
                selectedTile = summary.Tiles.Count > 0 ? summary.Tiles[0] : null;
            if (!keepView)
            {
                selectedObjectId = null;
                fitMapToViewport = true;
                zoomScale = 1f;
            }

            UpdateMapHeader();
            RenderMapSummary();
            RenderProjectBrowser();
            RenderInspector();
            UpdateMapStateLabel();
            SetStatus(currentSummary.Name + ": " + currentSummary.Tiles.Count + " tiles loaded" +
                      (session.CanEdit ? " through the project workspace" : " read-only from the OMSI installation"));
            if (toolsButton != null)
                toolsButton.SetEnabled(true);
        }

        /// <summary>Reads the map again through the same session, keeping the view, after an edit.</summary>
        private void ReloadMap()
        {
            LoadMap(session.GlobalConfigPath, currentProjectName, session.ProjectFilePath, true);
        }

        private void SelectObject(int objectId)
        {
            selectedObjectId = objectId;
            selectedTileVisual?.SetSelectedObject(objectId);
            RenderInspector();
            SetStatus("Selected object #" + objectId + (session != null && session.CanEdit ? "" : " (read-only; save a project to edit)"));
        }

        private void SelectObjectFromMap(OmsiTileSummary tile, int objectId)
        {
            if (ReferenceEquals(tile, selectedTile))
                SelectObject(objectId);
        }

        private void ApplyObjectEdit(OmsiObjectRecord item, TextField xField, TextField yField, TextField zField, TextField headingField)
        {
            if (session == null || !session.CanEdit || selectedTile == null)
            {
                SetStatus("The map is read-only. Save a Mapforge project (File > Save map project as...) to edit it.");
                return;
            }
            if (!TryParseField(xField, "X", out var x) || !TryParseField(yField, "Y", out var y) ||
                !TryParseField(zField, "Height", out var z) || !TryParseField(headingField, "Heading", out var heading))
                return;

            try
            {
                if (!session.UpdateObject(selectedTile.FileName, item.Id, x, y, z, heading))
                {
                    SetStatus("Object #" + item.Id + " is unchanged.");
                    return;
                }
                ReloadMap();
                SetStatus("Object #" + item.Id + " saved to the project workspace (" + selectedTile.FileName + ").");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Could not save object #" + item.Id + ": " + exception.Message);
            }
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
                SetStatus("Edits of " + selectedTile.FileName + " were discarded; the original tile is shown again.");
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

        private static void TogglePopup(VisualElement popup, VisualElement otherPopup)
        {
            if (popup == null)
                return;

            var show = popup.resolvedStyle.display == DisplayStyle.None;
            if (otherPopup != null)
                otherPopup.style.display = DisplayStyle.None;
            popup.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
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
                SelectTile(worstGap.Tile);

            SetStatus(DescribeMapCheck(currentSummary, continuity));
        }

        private static string DescribeMapCheck(OmsiMapSummary summary, OmsiSplineContinuityReport continuity)
        {
            var importPart = summary.Warnings.Count == 0
                ? "no import warnings"
                : summary.Warnings.Count + " import warning(s), see the map overview";
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
            camera.backgroundColor = new Color(0.055f, 0.063f, 0.075f, 1f);
            camera.cullingMask = 0;
            camera.depth = -100f;
            camera.useOcclusionCulling = false;
        }

        private void SelectTile(OmsiTileSummary tile)
        {
            if (tile == null || currentSummary == null)
                return;

            var previousTile = selectedTile;
            if (!ReferenceEquals(previousTile, tile))
            {
                selectedObjectId = null;
                selectedTileVisual?.SetSelectedObject(null);
            }
            selectedTile = tile;
            SetTileHighlight(previousTile, false);
            SetTileHighlight(tile, true);
            selectedTileVisual = tileVisuals.TryGetValue(tile, out var visual) ? visual : null;
            var scrollTarget = selectedTileVisual;
            if (scrollTarget != null && mapContent != null)
                mapContent.schedule.Execute(() => ScrollToIfAttached(mapContent, scrollTarget));
            if (browserEntries.TryGetValue(tile, out var browserEntry))
            {
                var tileList = browserEntry.GetFirstAncestorOfType<ScrollView>();
                tileList?.schedule.Execute(() => ScrollToIfAttached(tileList, browserEntry));
            }
            RenderInspector();
            SetStatus("Selected tile " + tile.X + ", " + tile.Y);
        }

        /// <summary>Toggles the selection style on the existing map tile and browser entry without rebuilding them.</summary>
        private void SetTileHighlight(OmsiTileSummary tile, bool isSelected)
        {
            if (tile == null)
                return;
            if (tileVisuals.TryGetValue(tile, out var visual))
                visual.SetSelected(isSelected);
            if (browserEntries.TryGetValue(tile, out var entry))
                entry.EnableInClassList("browser-tile-entry-selected", isSelected);
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
            fitMapToViewport = true;
            zoomScale = 1f;
            UpdateZoomLabel();
            RenderMapSummary();
            SetStatus("Map fitted to viewport");
        }

        private void ChangeZoom(float factor)
        {
            if (currentSummary == null)
                return;

            if (fitMapToViewport)
            {
                fitMapToViewport = false;
                zoomScale = 1f;
            }
            zoomScale = Mathf.Clamp(zoomScale * factor, 0.5f, 3f);
            UpdateZoomLabel();
            RenderMapSummary();
            SetStatus("Map zoom " + Mathf.RoundToInt(zoomScale * 100f) + "%");
        }

        private void CenterSelectedTile()
        {
            if (selectedTileVisual == null || mapContent == null || selectedTile == null)
                return;

            var target = selectedTileVisual;
            mapContent.schedule.Execute(() =>
            {
                var tileBounds = target.worldBound;
                var viewportBounds = mapContent.contentViewport.worldBound;
                var currentOffset = mapContent.scrollOffset;
                mapContent.scrollOffset = currentOffset + new Vector2(
                    tileBounds.center.x - viewportBounds.center.x,
                    tileBounds.center.y - viewportBounds.center.y);
            });
            SetStatus("Centered tile " + selectedTile.X + ", " + selectedTile.Y);
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
            if (mapCountsLabel != null)
            {
                mapCountsLabel.text = "Tiles: " + currentSummary.Tiles.Count + "    Objects: " + currentSummary.ObjectCount +
                                      "    Splines: " + currentSummary.SplineCount + "    Connections: " + currentSummary.AttachmentCount +
                                      "    Terrain: " + currentSummary.TerrainTileCount + "/" + currentSummary.Tiles.Count +
                                      (currentSummary.UsesWorldCoordinates
                                          ? "    World coordinates: " + currentSummary.TileSizeMeters.ToString("0.0") + " m tiles"
                                          : string.Empty);
                mapCountsLabel.style.display = DisplayStyle.Flex;
            }
            if (mapDescriptionLabel != null)
            {
                mapDescriptionLabel.text = currentSummary.Description ?? string.Empty;
                mapDescriptionLabel.style.display = string.IsNullOrWhiteSpace(currentSummary.Description)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
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
            if (zoomLabel != null)
                zoomLabel.text = fitMapToViewport ? "Fit" : Mathf.RoundToInt(zoomScale * 100f) + "%";
        }

        private void OnMapViewportGeometryChanged(GeometryChangedEvent evt)
        {
            var size = evt.newRect.size;
            if ((size - lastMapViewportSize).sqrMagnitude < 4f)
                return;

            lastMapViewportSize = size;
            if (fitMapToViewport && currentSummary != null && mapContent != null)
                mapContent.schedule.Execute(() => RenderMapSummary(true));
        }

        private static void ScrollToIfAttached(ScrollView view, VisualElement target)
        {
            // A scheduled scroll can run after the map was rebuilt; the old tile is then no longer in the view.
            if (view != null && target != null && view.contentContainer.Contains(target))
                view.ScrollTo(target);
        }

        private void RenderMapSummary()
        {
            RenderMapSummary(false);
        }

        /// <summary>Rebuilds the map view; with <paramref name="onlyIfTileSizeChanged"/> it keeps the current view when the fitted tile size is unchanged.</summary>
        private void RenderMapSummary(bool onlyIfTileSizeChanged)
        {
            var container = mapContent;
            var summary = currentSummary;
            if (container == null || summary == null)
                return;

            const float tileGap = 10f;
            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            var tilePixels = -1f;
            if (summary.Tiles.Count > 0)
            {
                minX = maxX = summary.Tiles[0].X;
                minY = maxY = summary.Tiles[0].Y;
                foreach (var tile in summary.Tiles)
                {
                    minX = Math.Min(minX, tile.X);
                    maxX = Math.Max(maxX, tile.X);
                    minY = Math.Min(minY, tile.Y);
                    maxY = Math.Max(maxY, tile.Y);
                }
                tilePixels = GetTilePixelSize(maxX - minX + 1, maxY - minY + 1, tileGap);
            }

            if (onlyIfTileSizeChanged && Mathf.Abs(tilePixels - renderedTilePixels) < 0.5f)
                return;

            renderedTilePixels = tilePixels;
            container.Clear();
            tileVisuals.Clear();
            selectedTileVisual = null;
            if (summary.Tiles.Count > 0)
            {
                var columns = maxX - minX + 1;
                var rows = maxY - minY + 1;
                var mapGrid = new VisualElement();
                mapGrid.AddToClassList("map-grid");
                mapGrid.style.width = columns * (tilePixels + tileGap) - tileGap;
                mapGrid.style.height = rows * (tilePixels + tileGap) - tileGap;
                foreach (var tile in summary.Tiles)
                {
                    var visualTile = new MapTileVisualElement(tile, splineIndex?.GetSegments(tile.X, tile.Y), summary.TileSizeMeters, tilePixels >= 112f,
                        ReferenceEquals(tile, selectedTile), SelectTile, SelectObjectFromMap);
                    if (ReferenceEquals(tile, selectedTile))
                        visualTile.SetSelectedObject(selectedObjectId);
                    visualTile.style.left = (tile.X - minX) * (tilePixels + tileGap);
                    visualTile.style.top = (maxY - tile.Y) * (tilePixels + tileGap);
                    visualTile.style.width = tilePixels;
                    visualTile.style.height = tilePixels;
                    mapGrid.Add(visualTile);
                    tileVisuals[tile] = visualTile;
                    if (ReferenceEquals(tile, selectedTile))
                        selectedTileVisual = visualTile;
                }
                container.Add(mapGrid);
                var scrollTarget = selectedTileVisual;
                if (scrollTarget != null)
                    container.schedule.Execute(() => ScrollToIfAttached(container, scrollTarget));
            }

            foreach (var warning in summary.Warnings)
            {
                var warningLabel = new Label(warning);
                warningLabel.AddToClassList("map-warning");
                container.Add(warningLabel);
            }

            var source = new Label("Source: " + summary.RootPath);
            source.AddToClassList("map-source");
            container.Add(source);
        }

        private float GetTilePixelSize(int columns, int rows, float tileGap)
        {
            const float padding = 48f;
            // Room for a scrollbar, so the fitted size does not depend on whether one is currently shown.
            const float scrollbarReserve = 16f;
            const float maximum = 200f;
            const float minimum = 56f;
            var availableWidth = mapContent.resolvedStyle.width - padding - scrollbarReserve;
            var availableHeight = mapContent.resolvedStyle.height - padding - scrollbarReserve;
            if (float.IsNaN(availableWidth) || availableWidth <= 0f)
                availableWidth = minimum * columns;
            if (float.IsNaN(availableHeight) || availableHeight <= 0f)
                availableHeight = minimum * rows;

            var widthFit = (availableWidth - tileGap * (columns - 1)) / columns;
            var heightFit = (availableHeight - tileGap * (rows - 1)) / rows;
            var fitSize = Mathf.Clamp(Mathf.Min(maximum, widthFit, heightFit), minimum, maximum);
            return fitMapToViewport ? fitSize : Mathf.Clamp(fitSize * zoomScale, minimum, maximum * 2f);
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

            var mapCounts = new Label(currentSummary.Tiles.Count + " tiles  •  " +
                                      currentSummary.ObjectCount + " objects  •  " +
                                      currentSummary.SplineCount + " splines");
            mapCounts.AddToClassList("browser-map-counts");
            projectPanel.Add(mapCounts);

            var tileList = new ScrollView(ScrollViewMode.Vertical);
            tileList.AddToClassList("browser-tile-list");
            foreach (var tile in currentSummary.Tiles)
            {
                var capturedTile = tile;
                var entry = new Button(() => SelectTile(capturedTile))
                {
                    text = "Tile " + tile.X + ", " + tile.Y + "     " + tile.ObjectCount + " obj  /  " + tile.SplineCount + " spl" +
                           (IsTileEdited(tile) ? "  • edited" : string.Empty)
                };
                entry.AddToClassList("browser-tile-entry");
                if (IsTileEdited(tile))
                    entry.AddToClassList("browser-tile-entry-edited");
                if (ReferenceEquals(tile, selectedTile))
                    entry.AddToClassList("browser-tile-entry-selected");
                if (!tile.IsAvailable)
                    entry.AddToClassList("browser-tile-entry-missing");
                tileList.Add(entry);
                browserEntries[tile] = entry;
            }
            projectPanel.Add(tileList);
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
            var selectedObject = selectedObjectId.HasValue ? selectedTile.Objects.Find(item => item.Id == selectedObjectId.Value) : null;
            if (selectedObject != null)
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
                record.EnableInClassList("inspector-record-selected", item.Id == selectedObjectId);
                var objectId = item.Id;
                record.RegisterCallback<ClickEvent>(_ => SelectObject(objectId));
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
