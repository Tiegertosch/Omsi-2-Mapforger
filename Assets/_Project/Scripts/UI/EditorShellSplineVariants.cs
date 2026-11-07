using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A new road type for a spline and taking a spline away while a chrono variant is edited (ADR 065): the inspector's
    /// "Change type..." (the road panel chooses the profile) and "Delete in variant", also for the Delete key and the command line.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private OmsiTileSummary splinePickTile;
        private int splinePickId;

        private bool PickingSplineType => splinePickTile != null;

        /// <summary>Splines can get another type or go in the variant being edited; their ends, roads and the ground stay with the base map.</summary>
        private bool CanEditSplinesInVariant => EditingVariant && !ReadingMapInBackground;

        /// <summary>Opens the road panel as a chooser for the profile the selected spline becomes; Esc or closing the panel leaves it.</summary>
        private void PickSplineType()
        {
            if (selectedSpline == null || selectedSplineTile == null || roadPanel == null || objectsPanelHost == null)
                return;
            if (!CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("readonly.action.spline_type"));
                return;
            }
            if (PickingElementType)
                EndElementTypePick(null);
            StopPlacement();
            StartInstallationScan();
            UpdateRoadProfiles();
            splinePickTile = selectedSplineTile;
            splinePickId = selectedSpline.Id;
            roadPanel.BeginPick(Loc.T("splinevar.pick.title", ("id", splinePickId)), Loc.T("splinevar.pick.hint", ("id", splinePickId), ("variant", editedVariant.Title)));
            objectsPanel.style.display = DisplayStyle.None;
            roadPanel.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            SetStatus(Loc.T("splinevar.status.pick", ("id", splinePickId)));
        }

        /// <summary>Closes the chooser, and gives the spline the profile that was clicked; null leaves it as it is.</summary>
        private void EndSplineTypePick(string assetPath)
        {
            if (!PickingSplineType)
                return;
            var tile = splinePickTile;
            var id = splinePickId;
            splinePickTile = null;
            roadPanel.EndPick();
            if (activeTool != EditorTool.Road)
                roadPanel.style.display = DisplayStyle.None;
            if (!objectsPanelOpen && activeTool != EditorTool.Road)
                objectsPanelHost.style.display = DisplayStyle.None;
            if (assetPath == null)
            {
                SetStatusT("splinevar.status.stays");
                return;
            }
            var spline = tile.Splines.Find(candidate => candidate.Id == id);
            if (spline == null)
            {
                SetStatusT("splinevar.status.gone", ("id", id));
                return;
            }
            ApplySplineType(tile, spline, assetPath);
        }

        private void ApplySplineType(OmsiTileSummary tile, OmsiSplineRecord spline, string assetPath)
        {
            if (!CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("readonly.action.spline_type"));
                return;
            }
            OmsiSplineChange change;
            try
            {
                change = session.SetSplineTypeInVariant(tile, spline, assetPath);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("splinevar.status.type_failed", ("id", spline.Id), ("reason", exception.Message));
                return;
            }
            var name = Path.GetFileNameWithoutExtension(assetPath);
            if (change == null)
            {
                SetStatusT("splinevar.status.already", ("id", spline.Id), ("name", name));
                return;
            }
            var id = spline.Id;
            PushHistory(new ObjectEditStep { SplineChange = change, Description = Loc.T("splinevar.step.type", ("id", id)) });
            RefreshSplineViews(change);
            ResetPaths(currentSummary);
            RenderInspector();
            SetStatusT("splinevar.status.changed", ("id", id), ("name", name), ("variant", editedVariant.Title), ("hasNotes", change.Notes.Count > 0 ? "yes" : "no"),
                ("notes", string.Join(" ", change.Notes)));
        }

        /// <summary>
        /// A click that ends a road while a variant is edited (ADR 066): the planned pieces become splines in the variant's change
        /// list, standalone; a road that meets a free end of another spline only touches it, which openOMSI joins by position.
        /// </summary>
        private void BuildRoadInVariant(OmsiRoadPlan plan, OmsiSplineEnd endJoin, OmsiJunctionPort endPort)
        {
            if (!CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("readonly.action.build_roads"));
                return;
            }
            if (plan.StartTrimMeters > 0.0)
            {
                SetStatusT("splinevar.status.corner");
                return;
            }
            OmsiSplineChange change;
            try
            {
                change = session.CreateRoadInVariant(currentSummary, plan, roadPanel.Profile);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("splinevar.status.build_failed", ("reason", exception.Message));
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change, Description = Loc.T("splinevar.step.road") });
            RefreshSplineViews(change);
            ResetPaths(currentSummary);
            var warnings = plan.Issues.FindAll(issue => issue.Severity != OmsiRoadIssueSeverity.Note).Count;
            var built = Loc.T("splinevar.built", ("meters", plan.LengthMeters), ("count", change.SplineIds.Count), ("variant", editedVariant.Title), ("warnings", warnings));
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(Loc.T("splinevar.built.meeting", ("built", built), ("target", endJoin != null ? Loc.T("splinevar.spline", ("id", endJoin.Spline.Id)) : DescribePort(endPort))));
                return;
            }
            ContinueRoadFrom(change);
            SetStatus(Loc.T(roadStarted ? "splinevar.built.going" : "splinevar.built.done", ("built", built))); // i18n:keys
        }

        /// <summary>Takes the selected spline away in the variant being edited; what else names it is told but does not stop it.</summary>
        private void DeleteSelectedSplineInVariant()
        {
            if (selectedSpline == null || selectedSplineTile == null)
                return;
            if (!CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("readonly.action.delete_splines"));
                return;
            }
            OmsiSplineChange change;
            var id = selectedSpline.Id;
            try
            {
                change = session.DeleteSplineInVariant(currentSummary, selectedSplineTile, selectedSpline);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("splinevar.status.delete_failed", ("id", id), ("reason", exception.Message));
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change, Description = Loc.T("splinevar.step.delete", ("id", id)) });
            ClearSelectedSpline();
            RefreshSplineViews(change);
            ResetPaths(currentSummary);
            RenderInspector();
            var rest = new List<string>();
            if (change.RemovedRowCount > 0)
                rest.Add(Loc.T("splinevar.rest.rows", ("count", change.RemovedRowCount)));
            if (change.References.Count > 0)
                rest.Add(Loc.T("splinevar.rest.named", ("names", string.Join(", ", change.References.GroupBy(reference => reference.Kind).Select(group => group.Key + " (" + group.Count() + ")")))));
            SetStatus(Loc.T("splinevar.status.deleted", ("id", id), ("variant", editedVariant.Title), ("hasRest", rest.Count > 0 ? "yes" : "no"), ("rest", string.Join("; ", rest))));
        }

        /// <summary>The inspector's buttons for a spline in a variant being edited.</summary>
        private void AddSplineVariantButtons(VisualElement editor)
        {
            var change = LocBindings.WithTooltip(LocBindings.NewButton("splinevar.change_type", PickSplineType), "splinevar.change_type.tooltip");
            change.AddToClassList("map-control-button");
            change.AddToClassList("inspector-edit-apply");
            editor.Add(change);
            var delete = LocBindings.WithTooltip(LocBindings.NewButton("splinevar.delete", DeleteSelectedSplineInVariant), "splinevar.delete.tooltip");
            delete.AddToClassList("map-control-button");
            delete.AddToClassList("inspector-edit-delete");
            editor.Add(delete);
        }

        /// <summary>
        /// Command line (ADR 065), with a variant being edited and <c>-selectSpline</c>: <c>-splineType file.sli</c> gives the
        /// spline another profile, <c>-splineDelete</c> takes it away, <c>-splinePick [file.sli]</c> opens the chooser, with a file
        /// clicks it. <c>-splineUndo [n]</c> and <c>-splineRedo [n]</c> then step through the history as Ctrl+Z and Ctrl+Y do. The
        /// status goes to the log as "Spline type:", "Spline delete:", "Spline pick:", "Undo:" and "Redo:".
        /// </summary>
        private void RunSplineVariantArguments(string[] args)
        {
            string Argument(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal) ? args[index + 1] : null;
            }

            var type = Argument("-splineType");
            if (type != null && selectedSpline != null && selectedSplineTile != null)
            {
                ApplySplineType(selectedSplineTile, selectedSpline, type);
                Debug.Log("Spline type: " + statusLabel?.text);
            }
            if (Array.IndexOf(args, "-splinePick") >= 0)
            {
                PickSplineType();
                Debug.Log("Spline pick: " + statusLabel?.text);
                var chosen = Argument("-splinePick");
                if (chosen != null)
                {
                    EndSplineTypePick(chosen);
                    Debug.Log("Spline pick: " + statusLabel?.text);
                }
            }
            if (Array.IndexOf(args, "-splineDelete") >= 0)
            {
                DeleteSelectedSplineInVariant();
                Debug.Log("Spline delete: " + statusLabel?.text);
            }
            if (Array.IndexOf(args, "-splineUndo") >= 0)
                for (var undo = int.TryParse(Argument("-splineUndo"), out var undoCount) ? undoCount : 1; undo > 0; undo--)
                {
                    StepHistory(undoSteps, redoSteps, true);
                    Debug.Log("Undo: " + statusLabel?.text);
                }
            if (Array.IndexOf(args, "-splineRedo") >= 0)
                for (var redo = int.TryParse(Argument("-splineRedo"), out var redoCount) ? redoCount : 1; redo > 0; redo--)
                {
                    StepHistory(redoSteps, undoSteps, false);
                    Debug.Log("Redo: " + statusLabel?.text);
                }
        }
    }
}
