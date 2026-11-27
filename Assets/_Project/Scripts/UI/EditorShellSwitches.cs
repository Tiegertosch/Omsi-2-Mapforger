using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Switch branches on the map (ADR 105, M5): shown only while one switch object is selected, never as a permanent layer. Each [path] of the object
    /// is drawn with its switch position written beside it; the route view marks the switches a route passes (see <see cref="MapCanvasElement"/>).
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The .sco of the selected object when it is a switch; null for no selection, several, or another object.</summary>
        private OmsiSceneryObject SelectedSwitchObject()
        {
            if (selectedObject == null || selectedTile == null || currentSummary == null || selection.Count > 1)
                return null;
            var sceneryObject = PrioritySceneryObject(selectedObject.AssetPath);
            return sceneryObject != null && sceneryObject.IsSwitch ? sceneryObject : null;
        }

        /// <summary>Draws the branches of the selected switch object on the map, or removes them; called whenever the inspector is drawn again.</summary>
        private void RefreshSwitchBranches()
        {
            if (mapCanvas == null)
                return;
            var sceneryObject = SelectedSwitchObject();
            if (sceneryObject == null)
            {
                mapCanvas.SetSwitchBranches(null);
                return;
            }
            var branches = new List<KeyValuePair<OmsiMapPoint[], int?>>();
            // In the object frame with the path's height, so the 3D view draws them where the model stands, also while it is dragged.
            var local = new List<UnityEngine.Vector3[]>();
            foreach (var path in sceneryObject.Paths)
            {
                if (!(path.LengthMeters > 0.0) || double.IsInfinity(path.LengthMeters))
                    continue;
                branches.Add(new KeyValuePair<OmsiMapPoint[], int?>(
                    OmsiMapPaths.ObjectLane(selectedTile, selectedObject, path, currentSummary.TileSizeMeters), path.SwitchPosition));
                var lane = OmsiMapPaths.ObjectLaneLocal(path);
                var points = new UnityEngine.Vector3[lane.Length];
                for (var index = 0; index < lane.Length; index++)
                    points[index] = new UnityEngine.Vector3((float)lane[index].X, (float)path.Height, (float)lane[index].Y);
                local.Add(points);
            }
            mapCanvas.SetSwitchBranches(branches, selectedObject, local);
        }

        /// <summary>The read-only line of the inspector of a switch: how many positions it has.</summary>
        private void AddSwitchInspectorLine(UnityEngine.UIElements.VisualElement container)
        {
            var sceneryObject = SelectedSwitchObject();
            if (sceneryObject != null)
                AddInspectorValue(container, "inspector.switch", Loc.T("inspector.switch.value", ("count", sceneryObject.SwitchPositions.Value)));
        }
    }
}
