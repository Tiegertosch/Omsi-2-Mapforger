using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The daily traffic curves of the open map (ADR 088): the Map tab's "Traffic curves..." button opens a dialog with a graph for the
    /// density of cars and one for passengers, each with a handle at every full hour to drag. Apply writes the curves that were changed
    /// into global.cfg of the project copy, with the fewest points that give the hours shown.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement trafficCurvesDialog;
        private TrafficCurveElement carCurve;
        private TrafficCurveElement passengerCurve;
        private Label trafficCurveReadout;

        private void SetUpTrafficCurves(VisualElement root)
        {
            trafficCurvesDialog = new VisualElement { name = "traffic-curves-dialog" };
            trafficCurvesDialog.AddToClassList("dialog-overlay");
            trafficCurvesDialog.style.display = DisplayStyle.None;
            var dialog = new VisualElement();
            dialog.AddToClassList("dialog");
            dialog.AddToClassList("content-folders");
            dialog.style.width = 640;
            var title = LocBindings.NewLabel("trafficcurves.title");
            title.AddToClassList("popup-title");
            dialog.Add(title);
            var description = LocBindings.NewLabel("trafficcurves.description");
            description.AddToClassList("popup-description");
            dialog.Add(description);

            carCurve = new TrafficCurveElement();
            passengerCurve = new TrafficCurveElement();
            trafficCurveReadout = new Label();
            trafficCurveReadout.AddToClassList("popup-description");
            dialog.Add(CurveSection("trafficcurves.cars", carCurve, "trafficcurves.cars.tooltip"));
            dialog.Add(CurveSection("trafficcurves.passengers", passengerCurve, "trafficcurves.passengers.tooltip"));
            dialog.Add(trafficCurveReadout);

            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = LocBindings.NewButton("trafficcurves.cancel", () => HidePopup(trafficCurvesDialog));
            cancel.AddToClassList("map-control-button");
            buttons.Add(cancel);
            var apply = LocBindings.NewButton("trafficcurves.apply", ApplyTrafficCurves);
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("dialog-primary");
            buttons.Add(apply);
            dialog.Add(buttons);
            trafficCurvesDialog.Add(dialog);
            root.Add(trafficCurvesDialog);
        }

        /// <summary>A heading with a reset button, the graph and the hours under it.</summary>
        private VisualElement CurveSection(string headingKey, TrafficCurveElement curve, string tooltipKey)
        {
            var section = new VisualElement();
            section.style.marginTop = 8;
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var heading = LocBindings.NewLabel(headingKey); // i18n:keys
            heading.AddToClassList("inspector-section-heading");
            heading.style.flexGrow = 1;
            heading.style.marginBottom = 0;
            LocBindings.Tooltip(heading, tooltipKey); // i18n:keys
            header.Add(heading);
            var reset = LocBindings.NewButton("trafficcurves.reset", () => curve.Reset());
            reset.AddToClassList("map-control-button");
            reset.AddToClassList("content-folders-small");
            header.Add(reset);
            section.Add(header);
            section.Add(curve);
            var hours = new VisualElement();
            hours.style.flexDirection = FlexDirection.Row;
            hours.style.justifyContent = Justify.SpaceBetween;
            foreach (var hour in new[] { 0, 6, 12, 18, 24 })
            {
                var tick = new Label(hour.ToString(CultureInfo.InvariantCulture) + ":00");
                tick.AddToClassList("popup-description");
                tick.style.fontSize = 10;
                tick.style.marginTop = 0;
                tick.style.marginBottom = 0;
                hours.Add(tick);
            }
            section.Add(hours);
            curve.HourHovered += hour => ShowCurveReadout(curve, hour);
            return section;
        }

        private void ShowCurveReadout(TrafficCurveElement curve, int hour)
        {
            trafficCurveReadout.text = Loc.T("trafficcurves.readout", ("hour", hour), ("value", curve.Value(hour)));
        }

        private void OpenTrafficCurves()
        {
            if (trafficCurvesDialog == null || currentSummary == null || session == null || !session.CanEdit)
                return;
            carCurve.SetCurve(OmsiTrafficCurve.Hourly(session.ReadTrafficCurve(false)));
            passengerCurve.SetCurve(OmsiTrafficCurve.Hourly(session.ReadTrafficCurve(true)));
            trafficCurveReadout.text = string.Empty;
            trafficCurvesDialog.style.display = DisplayStyle.Flex;
        }

        private void ApplyTrafficCurves()
        {
            if (session == null || !session.CanEdit)
                return;
            // Only a curve that was touched is written; one left alone keeps the points the map has.
            var cars = carCurve.IsChanged ? OmsiTrafficCurve.Simplify(carCurve.Values) : null;
            var passengers = passengerCurve.IsChanged ? OmsiTrafficCurve.Simplify(passengerCurve.Values) : null;
            bool changed;
            try
            {
                changed = session.WriteTrafficCurves(cars, passengers);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("trafficcurves.status.failed", ("reason", exception.Message));
                return;
            }
            HidePopup(trafficCurvesDialog);
            SetStatusT(changed ? "trafficcurves.status.changed" : "trafficcurves.status.unchanged"); // i18n:keys
        }

        /// <summary>
        /// Command line, with a project: <c>-trafficCurves</c> opens the dialog (for a screenshot); <c>-trafficCurveSet cars|passengers hour factor</c>
        /// (repeatable) drags that handle; <c>-trafficCurvesApply</c> writes the curves ("Traffic curves:" in the log).
        /// </summary>
        private void RunTrafficCurveArguments(string[] args)
        {
            if (Array.IndexOf(args, "-trafficCurves") < 0)
                return;
            OpenTrafficCurves();
            for (var index = 0; index + 3 < args.Length; index++)
            {
                if (args[index] != "-trafficCurveSet")
                    continue;
                var curve = string.Equals(args[index + 1], "passengers", StringComparison.OrdinalIgnoreCase) ? passengerCurve : carCurve;
                if (int.TryParse(args[index + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) &&
                    double.TryParse(args[index + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
                    curve.SetValue(hour, factor);
            }
            if (Array.IndexOf(args, "-trafficCurvesApply") >= 0)
            {
                ApplyTrafficCurves();
                Debug.Log("Traffic curves: " + statusLabel?.text);
            }
        }

        /// <summary>The button under the parking list button in the Map tab.</summary>
        private Button TrafficCurvesButton()
        {
            var button = new Button(OpenTrafficCurves) { text = Loc.T("trafficcurves.open"), tooltip = Loc.T("trafficcurves.open.tooltip") };
            button.AddToClassList("map-control-button");
            button.style.marginTop = 4;
            return button;
        }
    }
}
