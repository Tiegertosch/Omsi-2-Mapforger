using System;
using System.Collections.Generic;
using System.IO;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The road builder in the road tool (ADR 049): New road type... in the road picker opens it in place of the picker;
    /// saving writes the road type under Splines\Mapforge in the installation, adds it to the picker's My roads and
    /// chooses it, ready to build.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private RoadBuilderPanel roadBuilder;

        // Road types made in this session, offered before the installation scan lists them.
        private readonly HashSet<string> madeRoadTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void CreateRoadBuilder()
        {
            roadBuilder = new RoadBuilderPanel();
            roadBuilder.style.display = DisplayStyle.None;
            roadBuilder.SaveRequested += SaveRoadType;
            roadBuilder.CloseRequested += () => ShowRoadBuilder(false);
            objectsPanelHost.Add(roadBuilder);
            roadPanel.NewRoadTypeRequested += () => ShowRoadBuilder(true);
        }

        private bool RoadBuilderShown => roadBuilder != null && roadBuilder.style.display == DisplayStyle.Flex;

        /// <summary>Shows the road builder in place of the road picker, starting a new road type, or the picker again.</summary>
        private void ShowRoadBuilder(bool show)
        {
            if (roadBuilder == null || roadPanel == null)
                return;
            if (show && activeTool != EditorTool.Road)
                SetTool(EditorTool.Road);
            if (show && currentSummary != null)
                roadBuilder.SetInstallation(OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath));
            if (show && !RoadBuilderShown)
                roadBuilder.Reset();
            roadBuilder.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            roadPanel.style.display = !show && activeTool == EditorTool.Road ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
                SetStatus("Road builder: pick a lane in the bar to change it, add lanes or start from a template, then save the road type.");
        }

        /// <summary>The spline profiles to offer: the installation's and the road types made since it was listed.</summary>
        private IEnumerable<string> RoadProfilePaths()
        {
            var paths = new List<string>();
            if (installationCatalog?.SplineProfiles != null)
                paths.AddRange(installationCatalog.SplineProfiles);
            foreach (var made in madeRoadTypes)
                if (!paths.Exists(path => string.Equals(path, made, StringComparison.OrdinalIgnoreCase)))
                    paths.Add(made);
            return paths;
        }

        private void SaveRoadType(string name, OmsiRoadType type)
        {
            if (currentSummary == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("make road types"));
                return;
            }
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string path;
            try
            {
                path = OmsiRoadTypeStore.Write(root, currentProjectName, name, type);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus("Road builder: the road type could not be made: " + exception.Message);
                return;
            }
            madeRoadTypes.Add(path);
            ShowRoadBuilder(false);
            UpdateRoadProfiles();
            roadPanel.ChooseProfile(path);
            roadPanel.ShowTab("My roads");
            SetStatus("Road builder: made " + Path.GetFileNameWithoutExtension(path) + " (" + type.Width.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                      " m) as " + path + " in your OMSI installation; it is chosen in the road tool, click on the map to build it.");
        }

        /// <summary>
        /// For command-line checks: opens the road builder, takes the lanes given (as in the .sli line, such as
        /// <c>"walk 3 | drive 3.25 back | drive 3.25 | walk 3"</c>), picks a lane or the line after lane n, and saves under a
        /// name when asked. The status goes to the log as "Road builder:".
        /// </summary>
        private void RunRoadBuilderArgument(string lanes, string select, string line, string saveName)
        {
            ShowRoadBuilder(true);
            if (!string.IsNullOrWhiteSpace(lanes))
            {
                if (OmsiRoadType.TryParse(lanes, out var type))
                    roadBuilder.SetType(type);
                else
                    Debug.LogWarning("Road builder: the lanes \"" + lanes + "\" cannot be read.");
            }
            if (int.TryParse(select, out var lane))
                roadBuilder.SelectLane(lane - 1);
            if (int.TryParse(line, out var after))
                roadBuilder.SelectLine(after - 1);
            if (saveName != null)
            {
                roadBuilder.SetName(saveName);
                SaveRoadType(roadBuilder.RoadName, roadBuilder.Type);
            }
            Debug.Log("Road builder: " + (roadBuilder.Type.Describe()) + " | " + statusLabel?.text);
        }
    }
}
