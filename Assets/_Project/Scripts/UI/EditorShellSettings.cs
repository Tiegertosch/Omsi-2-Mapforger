using System;
using System.Collections.Generic;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The settings dialog (File, Settings...): at first only the interface language under General (ADR 068). The languages that can
    /// be chosen are a drop-down list, each named in its own language; a choice takes effect at once and is kept for the next start.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private ModalDialog settingsDialog;
        private VisualElement languageSlot;
        private DropdownField languageDropdown;
        private List<LocaleInfo> languageChoices = new List<LocaleInfo>();
        private const string HideDlcKey = "Mapforge.HideDlcObjects";
        private Toggle hideDlcToggle;
        private Label hideDlcHint;

        /// <summary>Whether the lists of objects and road types leave out what belongs to a DLC (kept under the key the object menu used).</summary>
        private bool HideDlcContent => PlayerPrefs.GetInt(HideDlcKey, 0) != 0;

        private PanelSettings interfacePanelSettings;
        private float configuredInterfaceScale = 1f;
        private EditorShellScale.Result? interfaceScaleResult;
        private int interfaceScaleWindowWidth;
        private DropdownField interfaceSizeDropdown;

        /// <summary>Calculates and sets the panel scale (Windows dpi, "Interface size", default factor, window limit).</summary>
        private void ApplyInterfaceScale()
        {
            if (interfacePanelSettings == null)
                return;
            interfaceScaleArguments = interfaceScaleArguments ?? Environment.GetCommandLineArgs();
            interfaceScaleResult = EditorShellScale.Apply(interfacePanelSettings, configuredInterfaceScale, interfaceScaleArguments, interfaceScaleResult);
            interfaceScaleWindowWidth = Screen.width;
            interfaceScaleDpi = Screen.dpi;
        }

        private string[] interfaceScaleArguments;
        private float interfaceScaleDpi;

        /// <summary>The window limit depends on the width and the factor on the dpi: calculate again when either changed (the window moved to another screen).</summary>
        private void KeepInterfaceScaleForWindow()
        {
            if (interfacePanelSettings != null && EditorShellScale.NeedsRecalculation(interfaceScaleWindowWidth, interfaceScaleDpi, Screen.width, Screen.dpi))
                ApplyInterfaceScale();
        }

        /// <summary>The choice as a plain number: the drop-down field did not show a trailing " %", so the unit stands in the label.</summary>
        private static string PercentText(int percent)
        {
            return percent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void SetUpInterfaceSizeSetting(VisualElement root)
        {
            var slot = root.Q<VisualElement>("settings-interface-size-list");
            if (slot == null)
                return;
            interfaceSizeDropdown = new DropdownField();
            interfaceSizeDropdown.AddToClassList("new-map-dropdown");
            interfaceSizeDropdown.AddToClassList("settings-dropdown");
            var choices = new List<string>();
            for (var percent = EditorShellScale.MinUserPercent; percent <= EditorShellScale.MaxUserPercent; percent += EditorShellScale.UserPercentStep)
                choices.Add(PercentText(percent));
            interfaceSizeDropdown.choices = choices;
            interfaceSizeDropdown.SetValueWithoutNotify(PercentText(EditorShellScale.LoadUserPercent()));
            interfaceSizeDropdown.RegisterValueChangedCallback(_ =>
            {
                var index = interfaceSizeDropdown.index;
                if (index < 0 || index >= choices.Count)
                    return;
                EditorShellScale.SaveUserPercent(EditorShellScale.MinUserPercent + index * EditorShellScale.UserPercentStep);
                ApplyInterfaceScale();
            });
            slot.Add(interfaceSizeDropdown);
            LocBindings.Tooltip(slot, "settings.interface_size.tooltip");
            LocBindings.Text(root.Q<Label>("settings-interface-size-label"), "settings.interface_size.label");
            LocBindings.Text(root.Q<Label>("settings-interface-size-hint"), "settings.interface_size.hint");
        }

        private void SetUpSettings(VisualElement root)
        {
            // Only a primary button: Close. Esc closes it all the same; the choices apply at once, so there is nothing to discard.
            settingsDialog = NewDialog(new DialogOptions
            {
                Name = "settings-dialog",
                TitleKey = "settings.title",
                Width = DialogWidth.Standard,
                Buttons = new[] { new DialogButton { TextKey = "settings.close", Role = DialogButtonRole.Primary, Name = "settings-close-button" } }
            });
            var body = settingsDialog.Body;
            SettingsHeading(body, "settings-general-heading", "settings.section.general");
            languageSlot = SettingsDropdownRow(body, "settings-language-label", "settings.language.label", "settings-language-list");
            SettingsHint(body, "settings-language-hint", "settings.language.hint");
            SettingsDropdownRow(body, "settings-interface-size-label", "settings.interface_size.label", "settings-interface-size-list");
            SettingsHint(body, "settings-interface-size-hint", "settings.interface_size.hint");
            var edgeToggle = new Toggle { name = "settings-inspector-edge" };
            edgeToggle.AddToClassList("form-toggle");
            body.Add(edgeToggle);
            SettingsHeading(body, "settings-content-heading", "settings.section.content");
            var dlcToggle = new Toggle { name = "settings-hide-dlc" };
            dlcToggle.AddToClassList("form-toggle");
            body.Add(dlcToggle);
            var dlcHint = new Label { name = "settings-hide-dlc-hint" };
            dlcHint.AddToClassList("popup-description");
            body.Add(dlcHint);
            var foldersButton = new Button { name = "content-folders-button" };
            foldersButton.AddToClassList("map-control-button");
            foldersButton.AddToClassList("settings-folders-button");
            body.Add(foldersButton);

            languageDropdown = new DropdownField();
            if (languageSlot != null)
            {
                languageDropdown.AddToClassList("new-map-dropdown");
                languageDropdown.AddToClassList("settings-dropdown");
                languageDropdown.RegisterValueChangedCallback(_ =>
                {
                    var index = languageDropdown.index;
                    if (index >= 0 && index < languageChoices.Count)
                        ChooseLanguage(languageChoices[index].Code);
                });
                languageSlot.Add(languageDropdown);
            }
            hideDlcToggle = dlcToggle;
            hideDlcHint = dlcHint;
            hideDlcToggle.SetValueWithoutNotify(HideDlcContent);
            hideDlcToggle.RegisterValueChangedCallback(change =>
            {
                PlayerPrefs.SetInt(HideDlcKey, change.newValue ? 1 : 0);
                PlayerPrefs.Save();
                ApplyHideDlc();
            });
            SetUpInterfaceSizeSetting(root);
            SetUpInspectorEdgeSetting(root);
            var menuItem = root.Q<Button>("settings-menu-item");
            var fileMenu = root.Q<VisualElement>("file-menu");
            if (menuItem != null)
                menuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenSettings();
                };

            LocBindings.Text(menuItem, "menu.file.settings");
            LocBindings.Text(hideDlcToggle, "settings.hide_dlc");
        }

        private static void SettingsHeading(VisualElement body, string name, string key)
        {
            var heading = LocBindings.NewLabel(key); // i18n:keys
            heading.name = name;
            heading.AddToClassList("inspector-section-heading");
            heading.AddToClassList("form-indent");
            body.Add(heading);
        }

        private static void SettingsHint(VisualElement body, string name, string key)
        {
            var hint = LocBindings.NewLabel(key); // i18n:keys
            hint.name = name;
            hint.AddToClassList("popup-description");
            body.Add(hint);
        }

        /// <summary>A row with a label at the left and an empty slot for a drop-down at the right; returns the slot.</summary>
        private static VisualElement SettingsDropdownRow(VisualElement body, string labelName, string labelKey, string slotName)
        {
            var row = new VisualElement();
            row.AddToClassList("dialog-row");
            var label = LocBindings.NewLabel(labelKey); // i18n:keys
            label.name = labelName;
            label.AddToClassList("dialog-row-text");
            row.Add(label);
            var slot = new VisualElement { name = slotName };
            slot.AddToClassList("settings-language-slot");
            row.Add(slot);
            body.Add(row);
            return slot;
        }

        /// <summary>Shows the dialog with the languages that can be chosen, the one in use marked.</summary>
        private void OpenSettings()
        {
            if (settingsDialog == null || languageSlot == null)
                return;
            languageChoices = Loc.Manager != null
                ? Loc.Manager.Languages.OrderBy(info => info.NativeName, StringComparer.CurrentCultureIgnoreCase).ToList()
                : new List<LocaleInfo>();
            languageDropdown.choices = languageChoices.Select(info => info.NativeName).ToList();
            MarkLanguage();
            ShowHideDlcHint();
            contentFoldersButton?.SetEnabled(currentSummary != null); // the folders are those of the open map's installation
            settingsDialog.Show();
        }

        /// <summary>Gives the lists of objects and road types the setting and what the installation's files are known to come from.</summary>
        private void ApplyHideDlc()
        {
            if (objectsPanel != null)
                objectsPanel.HideDlc = HideDlcContent;
            roadPanel?.SetOrigins(contentOrigins, HideDlcContent);
        }

        /// <summary>Says below the switch how DLC files are told apart: from Steam's lists, or only from folders marked as DLC.</summary>
        private void ShowHideDlcHint()
        {
            if (hideDlcHint == null)
                return;
            var origin = contentOrigins == null ? Loc.T("objects.origin.reading")
                : contentOrigins.FromSteam ? contentOrigins.Description
                : Loc.T("objects.origin.no_steam", ("description", contentOrigins.Description));
            hideDlcHint.text = Loc.T("settings.hide_dlc.hint", ("origin", origin));
        }

        private void MarkLanguage()
        {
            var index = languageChoices.FindIndex(info => string.Equals(info.Code, Loc.Manager?.Language, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                languageDropdown.SetValueWithoutNotify(languageChoices[index].NativeName);
        }

        private void ChooseLanguage(string code)
        {
            if (string.Equals(code, Loc.Manager?.Language, StringComparison.OrdinalIgnoreCase))
                return;
            if (LocalizationService.Choose(code))
            {
                MarkLanguage();
                ShowHideDlcHint();
                var info = languageChoices.Find(candidate => string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));
                SetStatus(Loc.T("settings.language.changed", ("language", info?.NativeName ?? code)));
            }
        }
    }
}
