using System;
using OmsiMapEditor.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The shell's part of the interface language (ADR 068): binds the texts the UXML gives to keys, follows a language change by
    /// setting them anew and drawing the parts built in code again, and offers the command-line checks.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private bool languageSubscribed;

        /// <summary>The texts and tooltips of the elements of EditorShell.uxml that follow the language: element name, what it is, key.</summary>
        private static readonly (string Name, string Attribute, string Key)[] UxmlTexts =
        {
            ("project-title", "text", "panel.project.title"),
            ("project-empty", "text", "panel.project.empty"),
            ("inspector-title", "text", "panel.inspector.title"),
            ("inspector-empty", "text", "panel.inspector.empty"),
            ("bar-popup-close-button", "tooltip", "bar.popup.close"),
            ("inspector-collapse-button", "tooltip", "panel.inspector.close"),
            ("fit-map-button", "tooltip", "view.fit.tooltip"),
            ("zoom-out-button", "tooltip", "view.zoom_out.tooltip"),
            ("zoom-in-button", "tooltip", "view.zoom_in.tooltip"),
            ("center-selection-button", "tooltip", "view.center.tooltip"),
            ("ground-toggle-button", "tooltip", "view.textures.tooltip"),
            ("view3d-button", "tooltip", "view.3d.tooltip"),
            ("routes-toggle-button", "tooltip", "view.routes.tooltip"),
            ("new-map-menu-item", "text", "menu.file.new_map"),
            ("open-project-menu-item", "text", "menu.file.open_project"),
            ("save-project-menu-item", "text", "menu.file.save_project"),
            ("export-menu-item", "text", "menu.file.export"),
            ("open-map-menu-item", "text", "menu.file.open_map"),
            ("place-object-menu-item", "text", "menu.edit.place_object"),
            ("delete-object-menu-item", "text", "menu.edit.delete_selection"),
            ("refit-terrain-menu-item", "text", "menu.edit.refit_terrain"),
            ("test-junction-menu-item", "text", "menu.edit.test_junction"),
            ("test-junction-menu-item", "tooltip", "menu.edit.test_junction.tooltip"),
            ("tee-junction-menu-item", "text", "menu.edit.tee_junction"),
            ("tee-junction-menu-item", "tooltip", "menu.edit.tee_junction.tooltip"),
            ("cross-junction-menu-item", "text", "menu.edit.cross_junction"),
            ("cross-junction-menu-item", "tooltip", "menu.edit.cross_junction.tooltip"),
            ("undo-menu-item", "text", "menu.edit.undo"),
            ("redo-menu-item", "text", "menu.edit.redo"),
            ("inspector-menu-item", "text", "menu.view.inspector"),
            ("satellite-menu-item", "text", "menu.view.satellite"),
            ("satellite-menu-item", "tooltip", "menu.view.satellite.tooltip"),
            ("road-builder-menu-item", "text", "menu.view.road_builder"),
            ("road-builder-menu-item", "tooltip", "roadtool.new_type.tooltip"),
            ("check-map-menu-item", "text", "menu.view.check"),
            ("check-map-menu-item", "tooltip", "menu.tools.tooltip"),
            ("check-updates-menu-item", "text", "menu.help.check_updates"),
            ("check-updates-menu-item", "tooltip", "menu.help.check_updates.tooltip"),
            ("report-bug-menu-item", "text", "menu.help.report_bug"),
            ("report-bug-menu-item", "tooltip", "menu.help.report_bug.tooltip"),
            ("wiki-menu-item", "text", "menu.help.wiki"),
            ("wiki-menu-item", "tooltip", "menu.help.wiki.tooltip"),
        };

        /// <summary>Binds the menu bar to keys; the UXML's own English texts stay as what shows before the language is known.</summary>
        private void SetUpLocalization(VisualElement root)
        {
            LocBindings.Text(root.Q<Button>("file-menu-button"), "menu.file");
            LocBindings.Text(root.Q<Button>("edit-menu-button"), "menu.edit");
            LocBindings.Text(root.Q<Button>("tools-menu-button"), "menu.tools");
            LocBindings.Text(root.Q<Button>("help-menu-button"), "menu.help");
            foreach (var (name, attribute, key) in UxmlTexts)
            {
                var element = root.Q<VisualElement>(name);
                if (element == null)
                    continue;
                if (attribute == "tooltip")
                    LocBindings.Tooltip(element, key); // i18n:keys
                else
                    LocBindings.Text(element, key); // i18n:keys
            }
            if (Loc.Manager != null && !languageSubscribed)
            {
                Loc.Manager.LanguageChanged += OnLanguageChanged;
                languageSubscribed = true;
            }
        }

        /// <summary>Called when the shell goes: it stops listening to language changes.</summary>
        private void ReleaseLocalization()
        {
            if (languageSubscribed && Loc.Manager != null)
                Loc.Manager.LanguageChanged -= OnLanguageChanged;
            languageSubscribed = false;
        }

        /// <summary>A language change: the bound elements set their texts anew and what the shell draws from data is drawn again.</summary>
        private void OnLanguageChanged()
        {
            LocBindings.Refresh(rootElement);
            RefreshStatusText();
            if (currentSummary == null && mapStateLabel != null)
                mapStateLabel.text = Loc.T("status.no_map");
            else
                UpdateMapStateLabel();
            welcomeScreen?.Refresh();
            newMapForm?.RefreshTexts();
            markingOptions?.RefreshTexts();
            roadPanel?.RefreshTexts();
            roadBuilder?.RefreshTexts();
            terrainOptions?.RefreshTexts();
            objectsPanel?.RefreshTexts();
            lineOptions?.RefreshTexts();
            laneOptions?.RefreshTexts();
            // The hint of the open tool is written when the pointer moves; it is written again now, in the new language.
            if (currentSummary != null)
            {
                if (activeTool == EditorTool.Road)
                    UpdateRoadPreview();
                else if (activeTool == EditorTool.Terrain)
                    ShowTerrainBrush();
                else if (activeTool == EditorTool.Markings)
                    UpdateMarkingPreview();
                else if (activeTool == EditorTool.Lanes)
                    RefreshLaneTexts();
                else if (activeTool == EditorTool.Signals)
                    RefreshSignalTexts();
            }
            // An open map check report was written in the earlier language: the check runs again, as the report is made when it runs.
            if (currentSummary != null && checkDialog != null && checkDialog.IsShown)
                ValidateLoadedMap();
            satelliteWindow?.RefreshTexts();
            if (currentSummary != null)
            {
                RenderBarPopup();
                RenderInspector();
            }
        }

        /// <summary>
        /// Command line: <c>-switchLanguage code</c> changes the language after the interface was built, without keeping it, to see the
        /// live change; <c>-i18nSelfTest</c> logs a number, a date and a plural as the language in use writes them.
        /// </summary>
        private void RunLocalizationArguments(string[] args)
        {
            var switchIndex = Array.IndexOf(args, "-switchLanguage");
            if (switchIndex >= 0 && switchIndex + 1 < args.Length && Loc.Manager != null)
                Debug.Log("Switch language: " + (Loc.Manager.SetLanguage(args[switchIndex + 1]) ? "now " + Loc.Manager.Language : "no such language " + args[switchIndex + 1]));
            if (Array.IndexOf(args, "-openSettings") >= 0)
                OpenSettings();
            var chooseIndex = Array.IndexOf(args, "-chooseLanguage");
            if (chooseIndex >= 0 && chooseIndex + 1 < args.Length)
            {
                // As choosing from the list in the settings dialog: the choice is kept.
                OpenSettings();
                ChooseLanguage(args[chooseIndex + 1]);
                Debug.Log("Choose language: " + statusLabel?.text + " (kept: " + LocalizationService.Store.Stored + ")");
            }
            if (Array.IndexOf(args, "-i18nSelfTest") >= 0)
                Debug.Log("I18n: " + LocalizationService.SelfTest());
        }
    }
}
