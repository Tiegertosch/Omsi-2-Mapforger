using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
                SetStatus(ReadOnlyMessage("change the type of splines"));
                return;
            }
            if (PickingElementType)
                EndElementTypePick(null);
            StopPlacement();
            StartInstallationScan();
            UpdateRoadProfiles();
            splinePickTile = selectedSplineTile;
            splinePickId = selectedSpline.Id;
            roadPanel.BeginPick("New type for spline #" + splinePickId, "Click the road type spline #" + splinePickId + " becomes in " + editedVariant.Title +
                                                                       "; it keeps its place. Esc leaves it as it is.");
            objectsPanel.style.display = DisplayStyle.None;
            roadPanel.style.display = DisplayStyle.Flex;
            objectsPanelHost.style.display = DisplayStyle.Flex;
            SetStatus("Click the new road type for spline #" + splinePickId + " in the panel.");
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
                SetStatus("The road type stays as it is.");
                return;
            }
            var spline = tile.Splines.Find(candidate => candidate.Id == id);
            if (spline == null)
            {
                SetStatus("Spline #" + id + " is no longer there.");
                return;
            }
            ApplySplineType(tile, spline, assetPath);
        }

        private void ApplySplineType(OmsiTileSummary tile, OmsiSplineRecord spline, string assetPath)
        {
            if (!CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("change the type of splines"));
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
                SetStatus("The type of spline #" + spline.Id + " could not be changed: " + exception.Message);
                return;
            }
            var name = Path.GetFileNameWithoutExtension(assetPath);
            if (change == null)
            {
                SetStatus("Spline #" + spline.Id + " already is " + name + ".");
                return;
            }
            var id = spline.Id;
            PushHistory(new ObjectEditStep { SplineChange = change, Description = "the new road type of spline #" + id });
            RefreshSplineViews(change);
            ResetPaths(currentSummary);
            RenderInspector();
            SetStatus("Spline #" + id + " is " + name + " now in " + editedVariant.Title + ". Ctrl+Z undoes it." +
                      (change.Notes.Count > 0 ? " " + string.Join(" ", change.Notes) : string.Empty));
        }

        /// <summary>
        /// A click that ends a road while a variant is edited (ADR 066): the planned pieces become splines in the variant's change
        /// list, standalone; a road that meets a free end of another spline only touches it, which openOMSI joins by position.
        /// </summary>
        private void BuildRoadInVariant(OmsiRoadPlan plan, OmsiSplineEnd endJoin, OmsiJunctionPort endPort)
        {
            if (!CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("build roads"));
                return;
            }
            if (plan.StartTrimMeters > 0.0)
            {
                SetStatus("In a variant a road cannot round a corner at the road before it: set the corner radius to 0 or start the road away from the end.");
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
                SetStatus("The road could not be built: " + exception.Message);
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change, Description = "the road in the variant" });
            RefreshSplineViews(change);
            ResetPaths(currentSummary);
            var warnings = plan.Issues.FindAll(issue => issue.Severity != OmsiRoadIssueSeverity.Note).Count;
            var built = "Built " + plan.LengthMeters.ToString("F1", CultureInfo.InvariantCulture) + " m of road in " + change.SplineIds.Count + " spline(s) in " +
                        editedVariant.Title + (warnings > 0 ? " with " + warnings + " warning(s)" : string.Empty);
            if (endJoin != null || endPort != null)
            {
                EndRoad();
                SetStatus(built + ", meeting " + (endJoin != null ? "spline #" + endJoin.Spline.Id : DescribePort(endPort)) + ". Click to start the next road.");
                return;
            }
            ContinueRoadFrom(change);
            SetStatus(built + (roadStarted ? ". Click to go on, Esc ends the road, Ctrl+Z takes it back." : "."));
        }

        /// <summary>Takes the selected spline away in the variant being edited; what else names it is told but does not stop it.</summary>
        private void DeleteSelectedSplineInVariant()
        {
            if (selectedSpline == null || selectedSplineTile == null)
                return;
            if (!CanEditSplinesInVariant)
            {
                SetStatus(ReadOnlyMessage("delete splines"));
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
                SetStatus("Could not delete spline #" + id + ": " + exception.Message);
                return;
            }
            PushHistory(new ObjectEditStep { SplineChange = change, Description = "the deletion of spline #" + id + " in the variant" });
            ClearSelectedSpline();
            RefreshSplineViews(change);
            ResetPaths(currentSummary);
            RenderInspector();
            var rest = new List<string>();
            if (change.RemovedRowCount > 0)
                rest.Add(change.RemovedRowCount + " row(s) of objects along it go with it");
            if (change.References.Count > 0)
                rest.Add("it is still named by " + string.Join(", ", change.References.GroupBy(reference => reference.Kind).Select(group => group.Key + " (" + group.Count() + ")")));
            SetStatus("Deleted spline #" + id + " in " + editedVariant.Title + (rest.Count > 0 ? ": " + string.Join("; ", rest) : string.Empty) + ". Ctrl+Z brings it back.");
        }

        /// <summary>The inspector's buttons for a spline in a variant being edited.</summary>
        private void AddSplineVariantButtons(VisualElement editor)
        {
            var change = new Button(PickSplineType)
            {
                text = "Change type...",
                tooltip = "Choose another road type for this spline in the variant ([typ] in its change list); the base map stays as it is"
            };
            change.AddToClassList("map-control-button");
            change.AddToClassList("inspector-edit-apply");
            editor.Add(change);
            var delete = new Button(DeleteSelectedSplineInVariant)
            {
                text = "Delete in variant",
                tooltip = "Takes this spline away from the date the variant starts ([delete] in its change list); the base map stays as it is"
            };
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
