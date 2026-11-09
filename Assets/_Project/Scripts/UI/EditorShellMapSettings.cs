using System;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The settings of the open map, changed after it was made (ADR 076): the project browser has a "Map settings..." button that opens the
    /// dialog of a new map with the values global.cfg has now, without template, folder, size and height. Apply writes them into global.cfg
    /// of the project copy.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The button under the map's counts in the project browser.</summary>
        private Button MapSettingsButton()
        {
            var button = new Button(OpenMapSettings) { text = Loc.T("mapsettings.open"), tooltip = Loc.T("mapsettings.open.tooltip") };
            button.AddToClassList("map-control-button");
            button.style.marginTop = 4;
            return button;
        }

        private void OpenMapSettings()
        {
            if (newMapDialog == null || newMapForm == null || currentSummary == null)
                return;
            if (session == null || !session.CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.map_settings"));
                return;
            }
            newMapForm.OpenForSettings(currentSummary.GlobalConfigPath, currentSummary.FriendlyName, currentSummary.Description);
            newMapDialog.style.display = DisplayStyle.Flex;
        }

        /// <summary>Writes what the dialog collected into global.cfg of the project copy and shows the new name.</summary>
        private void ApplyMapSettings(string friendlyName, OmsiNewMapOptions options)
        {
            if (session == null || !session.CanEdit || currentSummary == null)
                return;
            bool changed;
            try
            {
                changed = session.UpdateMapSettings(currentSummary, friendlyName, options);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("mapsettings.status.failed", ("reason", exception.Message));
                return;
            }
            HidePopup(newMapDialog);
            RenderProjectBrowser();
            UpdateMapStateLabel();
            SetStatusT(changed ? "mapsettings.status.changed" : "mapsettings.status.unchanged", ("name", friendlyName)); // i18n:keys
        }

        /// <summary>
        /// Command line, with a project: <c>-mapSettings</c> opens the dialog for the open map (for a screenshot), and
        /// <c>-mapSettingsApply Name left|right firstYear lastYear cars passengers</c> writes these settings without it ("Map settings:" in the log).
        /// </summary>
        private void RunMapSettingsArguments(string[] args)
        {
            if (Array.IndexOf(args, "-mapSettings") >= 0)
                OpenMapSettings();
            var index = Array.IndexOf(args, "-mapSettingsApply");
            if (index < 0 || index + 6 >= args.Length || session == null || currentSummary == null)
                return;
            var options = OmsiNewMapOptions.FromTemplate(OmsiRecordFile.Parse(session.Workspace.ReadAllBytes(Path.GetFileName(currentSummary.GlobalConfigPath)),
                OmsiGlobalConfigLayouts.Instance));
            options.Description = (currentSummary.Description ?? string.Empty).Trim();
            options.LeftHandTraffic = string.Equals(args[index + 2], "left", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(args[index + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first) &&
                int.TryParse(args[index + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var last))
            {
                options.FirstYear = first;
                options.LastYear = last;
            }
            if (double.TryParse(args[index + 5], NumberStyles.Float, CultureInfo.InvariantCulture, out var cars))
                options.CarTraffic = cars;
            if (double.TryParse(args[index + 6], NumberStyles.Float, CultureInfo.InvariantCulture, out var passengers))
                options.PassengerTraffic = passengers;
            ApplyMapSettings(args[index + 1], options);
            Debug.Log("Map settings: " + statusLabel?.text);
        }
    }
}
