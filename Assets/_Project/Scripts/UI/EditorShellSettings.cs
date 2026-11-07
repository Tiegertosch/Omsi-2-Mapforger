using System;
using System.Collections.Generic;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The settings dialog (File, Settings...): at first only the interface language under General (ADR 068). The languages that can
    /// be chosen are buttons, each named in its own language; a choice takes effect at once and is kept for the next start.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement settingsDialog;
        private VisualElement languageList;
        private List<LocaleInfo> languageChoices = new List<LocaleInfo>();

        private void SetUpSettings(VisualElement root)
        {
            settingsDialog = root.Q<VisualElement>("settings-dialog");
            languageList = root.Q<VisualElement>("settings-language-list");
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
            LocBindings.Text(close, "settings.close");
        }

        /// <summary>Shows the dialog with the languages that can be chosen, the one in use marked.</summary>
        private void OpenSettings()
        {
            if (settingsDialog == null || languageList == null)
                return;
            languageChoices = Loc.Manager != null
                ? Loc.Manager.Languages.OrderBy(info => info.NativeName, StringComparer.CurrentCultureIgnoreCase).ToList()
                : new List<LocaleInfo>();
            languageList.Clear();
            foreach (var info in languageChoices)
            {
                var chosen = info;
                var button = new Button(() => ChooseLanguage(chosen.Code)) { text = info.NativeName, name = "language-" + info.Code };
                button.AddToClassList("panel-tab");
                languageList.Add(button);
            }
            MarkLanguage();
            settingsDialog.style.display = DisplayStyle.Flex;
        }

        private void MarkLanguage()
        {
            foreach (var button in languageList.Children())
                button.EnableInClassList("panel-tab-active", button.name == "language-" + Loc.Manager?.Language);
        }

        private void ChooseLanguage(string code)
        {
            if (string.Equals(code, Loc.Manager?.Language, StringComparison.OrdinalIgnoreCase))
                return;
            if (LocalizationService.Choose(code))
            {
                MarkLanguage();
                var info = languageChoices.Find(candidate => string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));
                SetStatus(Loc.T("settings.language.changed", ("language", info?.NativeName ?? code)));
            }
        }
    }
}
