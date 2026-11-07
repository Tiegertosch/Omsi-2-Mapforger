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

        /// <summary>Binds the menu bar to keys; the UXML's own English texts stay as what shows before the language is known.</summary>
        private void SetUpLocalization(VisualElement root)
        {
            LocBindings.Text(root.Q<Button>("file-menu-button"), "menu.file");
            LocBindings.Text(root.Q<Button>("edit-menu-button"), "menu.edit");
            LocBindings.Text(root.Q<Button>("view-menu-button"), "menu.view");
            LocBindings.Text(root.Q<Button>("tools-menu-button"), "menu.tools");
            LocBindings.Tooltip(root.Q<Button>("tools-menu-button"), "menu.tools.tooltip");
            LocBindings.Text(root.Q<Button>("help-menu-button"), "menu.help");
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
            if (currentSummary != null)
            {
                RenderProjectBrowser();
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
            if (Array.IndexOf(args, "-i18nSelfTest") >= 0)
                Debug.Log("I18n: " + LocalizationService.SelfTest());
        }
    }
}
