using System;
using System.Collections.Generic;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The choice of folders (ADR 084): File, "Object and spline folders..." and the folder button of the object menu and the
    /// road menu open a dialog with a tick box for each folder right below Sceneryobjects and Splines of the map's OMSI
    /// installation. Folders left out are not read when the installation is listed, so the menus leave them out. The choice is
    /// kept on this computer for each installation (<see cref="OmsiContentFolders"/>).
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private OmsiContentFolders contentFolders;
        private VisualElement contentFoldersDialog;
        private Label contentFoldersInstallation;
        private ScrollView contentFoldersList;
        private string contentFoldersRoot;
        private readonly Dictionary<string, Toggle> contentFolderToggles = new Dictionary<string, Toggle>(StringComparer.OrdinalIgnoreCase);

        // Folders given with -excludeFolders, used instead of the stored choice for this run and not stored; null without the argument.
        private HashSet<string> commandLineExcludedFolders;
        private bool commandLineExcludedFoldersRead;

        private void SetUpContentFolders(VisualElement root)
        {
            var menuItem = root.Q<Button>("content-folders-menu-item");
            var fileMenu = root.Q<VisualElement>("file-menu");
            if (menuItem != null)
                menuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenContentFolders();
                };
            LocBindings.Text(menuItem, "menu.file.content_folders");
            LocBindings.Tooltip(menuItem, "menu.file.content_folders.tooltip");

            contentFoldersDialog = new VisualElement { name = "content-folders-dialog" };
            contentFoldersDialog.AddToClassList("dialog-overlay");
            contentFoldersDialog.style.display = DisplayStyle.None;
            RegisterOverlay(contentFoldersDialog, true);
            var dialog = new VisualElement();
            dialog.AddToClassList("dialog");
            dialog.AddToClassList("content-folders");
            var title = LocBindings.NewLabel("contentfolders.title");
            title.AddToClassList("popup-title");
            dialog.Add(title);
            var description = LocBindings.NewLabel("contentfolders.description");
            description.AddToClassList("popup-description");
            dialog.Add(description);
            contentFoldersInstallation = new Label();
            contentFoldersInstallation.AddToClassList("popup-description");
            contentFoldersInstallation.AddToClassList("content-folders-installation");
            dialog.Add(contentFoldersInstallation);
            contentFoldersList = new ScrollView(ScrollViewMode.Vertical);
            contentFoldersList.AddToClassList("content-folders-list");
            dialog.Add(contentFoldersList);
            var own = LocBindings.NewLabel("contentfolders.own");
            own.AddToClassList("popup-description");
            own.AddToClassList("content-folders-note");
            dialog.Add(own);
            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = LocBindings.NewButton("contentfolders.cancel", () => HidePopup(contentFoldersDialog));
            cancel.AddToClassList("map-control-button");
            buttons.Add(cancel);
            var apply = LocBindings.NewButton("contentfolders.apply", ApplyContentFolders);
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("dialog-primary");
            buttons.Add(apply);
            dialog.Add(buttons);
            contentFoldersDialog.Add(dialog);
            root.Add(contentFoldersDialog);
        }

        /// <summary>
        /// The folders left out for an installation: those of -excludeFolders in a run with it, else the stored choice; never
        /// the open project's own folders below Mapforge, where new junctions and road types go.
        /// </summary>
        private HashSet<string> ExcludedContentFolders(string installationRoot)
        {
            var result = ChosenContentFolders(installationRoot);
            result.Remove(OmsiContentFolders.ProjectFolder(OmsiContentFolders.SceneryObjectsFolder, currentProjectName));
            result.Remove(OmsiContentFolders.ProjectFolder(OmsiContentFolders.SplinesFolder, currentProjectName));
            return result;
        }

        private bool IsOpenProjectFolder(string folder)
        {
            return string.Equals(folder, OmsiContentFolders.ProjectFolder(OmsiContentFolders.SceneryObjectsFolder, currentProjectName), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(folder, OmsiContentFolders.ProjectFolder(OmsiContentFolders.SplinesFolder, currentProjectName), StringComparison.OrdinalIgnoreCase);
        }

        private HashSet<string> ChosenContentFolders(string installationRoot)
        {
            if (!commandLineExcludedFoldersRead)
            {
                commandLineExcludedFoldersRead = true;
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, "-excludeFolders");
                if (index >= 0 && index + 1 < args.Length)
                {
                    commandLineExcludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var folder in args[index + 1].Split(';'))
                    {
                        var normal = OmsiContentFolders.Normalize(folder);
                        if (normal != null)
                            commandLineExcludedFolders.Add(normal);
                    }
                    Debug.Log("Exclude folders: " + string.Join(", ", commandLineExcludedFolders));
                }
            }
            if (commandLineExcludedFolders != null)
                return new HashSet<string>(commandLineExcludedFolders, StringComparer.OrdinalIgnoreCase);
            if (contentFolders == null)
                contentFolders = OmsiContentFolders.Load(OmsiContentFolders.DefaultPath);
            return contentFolders.ExcludedFor(installationRoot);
        }

        /// <summary>Tells the object and road menus how many folders the listing left out; their empty notes say so.</summary>
        private void ShowFoldersLeftOut(OmsiInstallationCatalog catalog)
        {
            var objects = 0;
            var splines = 0;
            if (catalog != null)
                foreach (var folder in catalog.ExcludedFolders)
                    if (folder.StartsWith(OmsiContentFolders.SplinesFolder + "\\", StringComparison.OrdinalIgnoreCase))
                        splines++;
                    else
                        objects++;
            if (objectsPanel != null)
                objectsPanel.FoldersLeftOut = objects;
            if (roadPanel != null)
                roadPanel.FoldersLeftOut = splines;
        }

        /// <summary>Lists the folders of the map's installation with a tick for each that is read.</summary>
        private void OpenContentFolders()
        {
            if (contentFoldersDialog == null)
                return;
            if (currentSummary == null)
            {
                SetStatusT("contentfolders.status.no_map");
                return;
            }
            contentFoldersRoot = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            contentFoldersInstallation.text = Loc.T("contentfolders.installation", ("path", contentFoldersRoot));
            var excluded = ExcludedContentFolders(contentFoldersRoot);
            contentFoldersList.Clear();
            contentFolderToggles.Clear();
            try
            {
                AddFolderSection(OmsiContentFolders.SceneryObjectsFolder, "contentfolders.section.objects", excluded);
                AddFolderSection(OmsiContentFolders.SplinesFolder, "contentfolders.section.splines", excluded);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("contentfolders.status.list_failed", ("reason", exception.Message));
                return;
            }
            contentFoldersList.scrollOffset = Vector2.zero;
            contentFoldersDialog.style.display = DisplayStyle.Flex;
        }

        private void AddFolderSection(string top, string headingKey, HashSet<string> excluded)
        {
            var folders = OmsiContentFolders.FoldersOf(contentFoldersRoot, top);
            var header = new VisualElement();
            header.AddToClassList("content-folders-header");
            var heading = new Label(Loc.T(headingKey, ("count", folders.Count))); // i18n:keys
            heading.AddToClassList("inspector-section-heading");
            heading.AddToClassList("content-folders-heading");
            header.Add(heading);
            var toggles = new List<Toggle>();
            var all = new Button(() => toggles.ForEach(toggle => toggle.value = true)) { text = Loc.T("contentfolders.all"), tooltip = Loc.T("contentfolders.all.tooltip") };
            all.AddToClassList("map-control-button");
            all.AddToClassList("content-folders-small");
            header.Add(all);
            var none = new Button(() => toggles.ForEach(toggle => toggle.value = false)) { text = Loc.T("contentfolders.none"), tooltip = Loc.T("contentfolders.none.tooltip") };
            none.AddToClassList("map-control-button");
            none.AddToClassList("content-folders-small");
            header.Add(none);
            contentFoldersList.Add(header);
            if (folders.Count == 0)
            {
                var empty = new Label(Loc.T("contentfolders.empty"));
                empty.AddToClassList("panel-info");
                contentFoldersList.Add(empty);
                all.SetEnabled(false);
                none.SetEnabled(false);
                return;
            }
            foreach (var folder in folders)
            {
                // Below the top folder, so that project folders read "Mapforge\Grundorf".
                var name = folder.Substring(top.Length + 1);
                var toggle = new Toggle { text = name, value = !excluded.Contains(folder), tooltip = folder };
                toggle.AddToClassList("content-folders-toggle");
                if (IsOpenProjectFolder(folder))
                {
                    // The open project's folder stays in: MapWorks puts the junctions and road types it makes there.
                    toggle.text = Loc.T("contentfolders.this_project", ("name", name));
                    toggle.tooltip = Loc.T("contentfolders.this_project.tooltip");
                    toggle.value = true;
                    toggle.SetEnabled(false);
                    contentFoldersList.Add(toggle);
                    continue;
                }
                toggles.Add(toggle);
                contentFolderToggles[folder] = toggle;
                contentFoldersList.Add(toggle);
            }
        }

        /// <summary>Stores the folders left unticked and lists the installation again, so that the menus follow at once.</summary>
        private void ApplyContentFolders()
        {
            if (contentFoldersRoot == null)
                return;
            var excluded = new List<string>();
            foreach (var pair in contentFolderToggles)
                if (!pair.Value.value)
                    excluded.Add(pair.Key);
            SetContentFolders(contentFoldersRoot, excluded);
        }

        private void SetContentFolders(string installationRoot, IEnumerable<string> excluded)
        {
            var before = ExcludedContentFolders(installationRoot);
            if (contentFolders == null)
                contentFolders = OmsiContentFolders.Load(OmsiContentFolders.DefaultPath);
            contentFolders.SetExcluded(installationRoot, excluded);
            // A choice made here replaces the folders of -excludeFolders for the rest of the run.
            var wasCommandLine = commandLineExcludedFolders != null;
            commandLineExcludedFolders = null;
            var after = ExcludedContentFolders(installationRoot);
            HidePopup(contentFoldersDialog);
            if (after.SetEquals(before) && !wasCommandLine)
            {
                SetStatusT("contentfolders.status.unchanged");
                return;
            }
            string saveProblem = null;
            try
            {
                contentFolders.Save(OmsiContentFolders.DefaultPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // The choice still holds for this run; the status says it is not kept.
                Debug.LogException(exception);
                saveProblem = exception.Message;
            }
            roadBuilder?.SetExcludedFolders(after);
            if (currentSummary != null)
                StartInstallationScan(true);
            var splines = 0;
            foreach (var folder in after)
                if (folder.StartsWith(OmsiContentFolders.SplinesFolder + "\\", StringComparison.OrdinalIgnoreCase))
                    splines++;
            if (saveProblem != null)
                SetStatusT("contentfolders.status.failed", ("reason", saveProblem));
            else
                SetStatusT("contentfolders.status.applied", ("objects", after.Count - splines), ("splines", splines));
            Debug.Log("Content folders: " + statusLabel?.text);
        }

        /// <summary>
        /// Command line, with a map: <c>-contentFolders</c> opens the dialog (for a screenshot). <c>-excludeFolders "A;B"</c>, read
        /// when the installation is first listed, leaves out these folders for the run without storing them.
        /// </summary>
        private void RunContentFolderArguments(string[] args)
        {
            if (Array.IndexOf(args, "-contentFolders") >= 0)
                OpenContentFolders();
        }
    }
}
