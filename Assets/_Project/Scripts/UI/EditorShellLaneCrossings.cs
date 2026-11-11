using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Pedestrian crossings in the lanes tool: a row for each arm of the selected junction to give it a crossing, change
    /// its distance and width or take it away. The junction is made again with the crossing, as one undo step.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement laneCrossingRows;

        /// <summary>For screenshots: keeps the arm marked by <c>-crossing n hover</c> when the rows are made again.</summary>
        private bool laneHighlightPinned;

        private int laneHighlightArm;
        private TextField laneCrossingDistanceField;
        private TextField laneCrossingWidthField;

        private void CreateLaneCrossingOptions()
        {
            var title = LocBindings.NewLabel("lanes.crossing.title");
            title.AddToClassList("picker-options-title");
            title.style.marginTop = 8f;
            laneOptions.Add(title);
            laneCrossingDistanceField = LaneNumberRow("lanes.crossing.distance", OmsiCrossing.DefaultDistanceMeters, "lanes.crossing.distance.tooltip");
            laneCrossingWidthField = LaneNumberRow("lanes.crossing.width", OmsiCrossing.DefaultWidthMeters, "lanes.crossing.width.tooltip");
            laneCrossingRows = new VisualElement();
            laneOptions.Add(laneCrossingRows);
        }

        /// <summary>The distance and width in the fields, kept within the bounds a crossing allows.</summary>
        private OmsiCrossing LaneCrossingValues()
        {
            double Read(TextField field, double fallback, Func<double, double> clamp) =>
                double.TryParse(field.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var meters) ? clamp(meters) : fallback;
            return new OmsiCrossing
            {
                DistanceMeters = Read(laneCrossingDistanceField, OmsiCrossing.DefaultDistanceMeters, OmsiCrossing.ClampDistance),
                WidthMeters = Read(laneCrossingWidthField, OmsiCrossing.DefaultWidthMeters, OmsiCrossing.ClampWidth)
            };
        }

        /// <summary>A row for each arm of the junction: a crossing to add, or to take away or give the values in the fields.</summary>
        private void RefreshLaneCrossingRows(OmsiJunctionLayout layout)
        {
            if (laneCrossingRows == null)
                return;
            laneCrossingRows.Clear();
            if (!laneHighlightPinned)
                mapCanvas?.SetArmHighlight(null, default, 0.0);
            if (layout == null || laneMade == null)
                return;
            for (var arm = 0; arm < layout.Arms.Count && arm < laneMade.Arms.Count; arm++)
            {
                var crossing = laneMade.Arms[arm].Crossing;
                var each = arm;
                var direction = LaneArmDirection(layout.Arms[arm]);
                var toggle = new Button(() => ApplyCrossing(each, crossing == null ? LaneCrossingValues() : null));
                if (crossing == null)
                {
                    LocBindings.Text(toggle, "lanes.crossing.add", ("n", arm + 1), ("direction", direction));
                    LocBindings.Tooltip(toggle, "lanes.crossing.add.tooltip");
                }
                else
                {
                    LocBindings.Text(toggle, "lanes.crossing.remove", ("n", arm + 1), ("direction", direction),
                        ("distance", crossing.DistanceMeters), ("width", crossing.WidthMeters));
                    LocBindings.Tooltip(toggle, "lanes.crossing.remove.tooltip");
                }
                toggle.AddToClassList("map-control-button");
                toggle.style.whiteSpace = WhiteSpace.Normal;
                HighlightArmOnHover(toggle, layout.Arms[arm]);
                laneCrossingRows.Add(toggle);
                if (crossing == null)
                    continue;
                var values = new Button(() => ApplyCrossing(each, LaneCrossingValues()));
                LocBindings.Text(values, "lanes.crossing.values", ("n", arm + 1), ("direction", direction));
                LocBindings.Tooltip(values, "lanes.crossing.values.tooltip");
                values.AddToClassList("map-control-button");
                values.style.whiteSpace = WhiteSpace.Normal;
                HighlightArmOnHover(values, layout.Arms[arm]);
                laneCrossingRows.Add(values);
            }
        }

        /// <summary>
        /// Marks an arm of the selected junction on the map while the pointer is over a button that acts on it, so the
        /// right arm is easy to pick from its number and direction.
        /// </summary>
        private void HighlightArmOnHover(VisualElement button, OmsiJunctionArm arm)
        {
            button.RegisterCallback<PointerEnterEvent>(_ => ShowArmHighlight(arm));
            button.RegisterCallback<PointerLeaveEvent>(_ => mapCanvas?.SetArmHighlight(null, default, 0.0));
        }

        /// <summary>Marks the arm again after the lane preview was made again, while one is pinned for a screenshot.</summary>
        private void KeepPinnedArmHighlight()
        {
            if (laneHighlightPinned && laneMade != null && laneHighlightArm >= 0 && laneHighlightArm < laneMade.Arms.Count)
                ShowArmHighlight(laneMade.Arms[laneHighlightArm]);
        }

        private void ShowArmHighlight(OmsiJunctionArm arm)
        {
            if (mapCanvas == null || laneJunction == null)
                return;
            var radians = arm.HeadingDegrees * Math.PI / 180.0;
            var reach = Math.Max(arm.Length, 6.0) + 4.0;
            var centre = ObjectToWorld(laneTile, laneJunction, 0.0, 0.0);
            var end = ObjectToWorld(laneTile, laneJunction, Math.Sin(radians) * reach, Math.Cos(radians) * reach);
            mapCanvas.SetArmHighlight(centre, end, laneJunction.Height);
        }

        /// <summary>
        /// For command-line checks: opens the lanes tool on the selected junction and gives arm <c>n</c> (from 1) a crossing,
        /// with <c>n distance width</c> those values, or takes it away with <c>n off</c>. The status goes to the log as "Crossing:".
        /// </summary>
        private void RunCrossingArgument(IList<string> words)
        {
            SetTool(EditorTool.Lanes);
            if (words.Count == 0 || !int.TryParse(words[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var arm))
            {
                Debug.Log("Crossing: give the arm, from 1, then its distance and width in metres or off.");
                return;
            }
            if (words.Count >= 3)
            {
                laneCrossingDistanceField.value = words[1];
                laneCrossingWidthField.value = words[2];
            }
            if (words.Count >= 2 && words[1] == "hover" && arm >= 1 && laneMade != null && arm <= laneMade.Arms.Count)
            {
                // For screenshots: shows the arm as pointing at its buttons does.
                laneHighlightPinned = true;
                laneHighlightArm = arm - 1;
                ShowArmHighlight(laneMade.Arms[arm - 1]);
                return;
            }
            ApplyCrossing(arm - 1, words.Count >= 2 && words[1] == "off" ? null : LaneCrossingValues());
            Debug.Log("Crossing: " + statusLabel?.text);
        }

        /// <summary>
        /// Gives an arm of the selected junction a crossing with these values, or takes it away with null, making the
        /// junction again as one undo step. The ways through stay as they are.
        /// </summary>
        private void ApplyCrossing(int arm, OmsiCrossing crossing)
        {
            if (laneJunction == null || laneMade == null || arm < 0 || arm >= laneMade.Arms.Count)
            {
                SetStatusT("lanes.status.first");
                return;
            }
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.change_lanes"));
                return;
            }
            var made = laneMade;
            var before = made.Arms[arm].Crossing;
            made.Arms[arm].Crossing = crossing;
            try
            {
                var plain = PlainRoadTexture();
                var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, laneTile, laneJunction);
                if (rebuild == null)
                {
                    SetStatusT("lanes.status.mismatch", ("id", laneJunction.Id));
                    return;
                }
                OmsiJunctionLayout layout;
                try
                {
                    layout = OmsiJunctionLayout.Build(rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, plain).Arms, plain);
                }
                catch (ArgumentException exception)
                {
                    SetStatusT("lanes.status.layout_failed", ("id", laneJunction.Id), ("reason", exception.Message));
                    return;
                }
                if (crossing != null && arm < layout.CrossingProblems.Length && layout.CrossingProblems[arm] != null)
                {
                    SetStatusT("lanes.status.no_crossing", ("reason", layout.CrossingProblems[arm]));
                    return;
                }
                var connections = made.Connections == null ? null : new HashSet<string>(made.Connections, StringComparer.Ordinal);
                ApplyLaneConnections(connections, crossing == null
                    ? Loc.T("lanes.done.crossing_removed", ("n", arm + 1))
                    : Loc.T(before == null ? "lanes.done.crossing_added" : "lanes.done.crossing_changed", ("n", arm + 1), ("distance", crossing.DistanceMeters), ("width", crossing.WidthMeters)), // i18n:keys
                    layout.WaysThrough);
            }
            finally
            {
                // The junction made again is read from its own file; the one it replaced stays as it was, for undo.
                made.Arms[arm].Crossing = before;
            }
        }
    }
}
