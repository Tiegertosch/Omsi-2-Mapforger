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
    /// Cant (banking across the road, in percent) and skew (the shift of a cross-section along the spline, for mitred corners) of the
    /// selected spline, in the inspector: ADR 072. A cant given to a joined end goes to the spline joined there too.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The fields for cant and skew under the heights of a selected spline, with the button that applies them.</summary>
        private void AddSplineCrossFields(VisualElement editor, OmsiSplineRecord spline)
        {
            var cant = AddEditRow(editor, ("inspector.field.cant_start", spline.CantStartPercent), ("inspector.field.cant_end", spline.CantEndPercent));
            var cantStart = cant[0];
            var cantEnd = cant[1];
            cantStart.tooltip = cantEnd.tooltip = Loc.T("inspector.cant.tooltip");
            TextField skewStart = null, skewEnd = null;
            if (spline.SkewStart.HasValue && spline.SkewEnd.HasValue)
            {
                var skew = AddEditRow(editor, ("inspector.field.skew_start", spline.SkewStart.Value), ("inspector.field.skew_end", spline.SkewEnd.Value));
                skewStart = skew[0];
                skewEnd = skew[1];
                skewStart.tooltip = skewEnd.tooltip = Loc.T("inspector.skew.tooltip");
            }
            var apply = new Button(() =>
            {
                if (!TryParseField(cantStart, Loc.T("inspector.name.cant_start"), out var start) || !TryParseField(cantEnd, Loc.T("inspector.name.cant_end"), out var end))
                    return;
                double? skewA = null, skewB = null;
                if (skewStart != null)
                {
                    if (!TryParseField(skewStart, Loc.T("inspector.name.skew_start"), out var a) || !TryParseField(skewEnd, Loc.T("inspector.name.skew_end"), out var b))
                        return;
                    skewA = a;
                    skewB = b;
                }
                SetSplineCross(start, end, skewA, skewB);
            })
            {
                text = Loc.T("inspector.apply_cross"),
                tooltip = Loc.T("inspector.apply_cross.tooltip")};
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("inspector-edit-apply");
            editor.Add(apply);
        }

        /// <summary>
        /// Gives the selected spline a new cant and skew as one undo step; a changed cant at a joined end goes to the spline joined there
        /// as well. <c>-splineCant start end [skewStart skewEnd]</c> does this from the command line.
        /// </summary>
        private void SetSplineCross(double cantStart, double cantEnd, double? skewStart, double? skewEnd)
        {
            if (selectedSpline == null || currentSummary == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.spline_cross"));
                return;
            }
            var id = selectedSpline.Id;
            var tile = currentSummary.Tiles.Find(candidate => candidate.Splines.Exists(spline => spline.Id == id));
            var record = tile?.Splines.Find(candidate => candidate.Id == id);
            if (record == null)
                return;
            var plan = OmsiSplineEditing.PlanCross(currentSummary, tile, record, cantStart, cantEnd, skewStart, skewEnd);
            if (plan.HasErrors)
            {
                SetStatusT("spline.cross.failed", ("id", id), ("failure", plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message));
                return;
            }
            OmsiSplineChange change;
            try
            {
                change = session.UpdateSplineCross(currentSummary, plan.Splines);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is IOException)
            {
                Debug.LogException(exception);
                SetStatusT("spline.cross.failed", ("id", id), ("failure", exception.Message));
                return;
            }
            if (change == null)
            {
                SetStatusT("spline.cross.unchanged", ("id", id));
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change });
            RefreshSplineViews(change);
            var joined = new List<string>();
            foreach (var changedId in change.SplineIds)
                if (changedId != id)
                    joined.Add("#" + changedId);
            var warnings = new List<string>();
            foreach (var issue in plan.Issues)
                warnings.Add(issue.Message);
            SetStatusT("spline.cross.changed", ("id", id), ("hasJoined", joined.Count > 0 ? "yes" : "no"), ("joined", string.Join(", ", joined)),
                ("hasWarnings", warnings.Count > 0 ? "yes" : "no"), ("warnings", string.Join("; ", warnings)));
        }

        /// <summary>The command-line argument <c>-splineCant start end [skewStart skewEnd]</c> (with <c>-selectSpline</c>); "Spline cross:" in the log.</summary>
        private void RunSplineCrossArguments(string[] args)
        {
            var index = Array.IndexOf(args, "-splineCant");
            if (index < 0 || index + 2 >= args.Length)
                return;
            if (!double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var start) ||
                !double.TryParse(args[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
                return;
            double? skewStart = null, skewEnd = null;
            if (index + 4 < args.Length && double.TryParse(args[index + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var skewA) &&
                double.TryParse(args[index + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out var skewB))
            {
                skewStart = skewA;
                skewEnd = skewB;
            }
            SetSplineCross(start, end, skewStart, skewEnd);
            Debug.Log("Spline cross: " + statusLabel?.text);
        }
    }
}
