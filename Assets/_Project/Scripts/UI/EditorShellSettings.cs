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
        private VisualElement settingsDialog;
        private VisualElement languageSlot;
        private readonly DropdownField languageDropdown = new DropdownField();
        private List<LocaleInfo> languageChoices = new List<LocaleInfo>();
        private const string HideDlcKey = "Mapforge.HideDlcObjects";
        private Toggle hideDlcToggle;
        private Label hideDlcHint;

        /// <summary>Whether the lists of objects and road types leave out what belongs to a DLC (kept under the key the object menu used).</summary>
        private bool HideDlcContent => PlayerPrefs.GetInt(HideDlcKey, 0) != 0;

        private void SetUpSettings(VisualElement root)
        {
            settingsDialog = root.Q<VisualElement>("settings-dialog");
            RegisterOverlay(settingsDialog, true);
            languageSlot = root.Q<VisualElement>("settings-language-list");
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
            hideDlcToggle = root.Q<Toggle>("settings-hide-dlc");
            hideDlcHint = root.Q<Label>("settings-hide-dlc-hint");
            if (hideDlcToggle != null)
            {
                hideDlcToggle.SetValueWithoutNotify(HideDlcContent);
                hideDlcToggle.RegisterValueChangedCallback(change =>
                {
                    PlayerPrefs.SetInt(HideDlcKey, change.newValue ? 1 : 0);
                    PlayerPrefs.Save();
                    ApplyHideDlc();
                });
            }
            var menuItem = root.Q<Button>("settings-menu-item");
            var close = root.Q<Button>("settings-close-button");
            var fileMenu = root.Q<VisualElement>("file-menu");
            if (menuItem != null)
                menuItem.clicked += () =>
                {
                    HidePopup(fileMenu);
                    OpenSettings();
                };
            if (close != null)
                close.clicked += () => HidePopup(settingsDialog);

            LocBindings.Text(menuItem, "menu.file.settings");
            LocBindings.Text(root.Q<Label>("settings-title"), "settings.title");
            LocBindings.Text(root.Q<Label>("settings-general-heading"), "settings.section.general");
            LocBindings.Text(root.Q<Label>("settings-language-label"), "settings.language.label");
            LocBindings.Text(root.Q<Label>("settings-language-hint"), "settings.language.hint");
            LocBindings.Text(root.Q<Label>("settings-content-heading"), "settings.section.content");
            LocBindings.Text(hideDlcToggle, "settings.hide_dlc");
            LocBindings.Text(close, "settings.close");
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
            settingsDialog.style.display = DisplayStyle.Flex;
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
