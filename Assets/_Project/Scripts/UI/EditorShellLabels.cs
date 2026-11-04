using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A new object file and new labels for the selected object and for the objects hanging on it (ADR 060). The file is
    /// chosen in the object library, which stands in for its placing options meanwhile; the labels are text fields, one per
    /// line the object has. In a chrono variant the change becomes a [typ] or [relabel] after a [selobject].
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // While an element waits for its new object file, the library chooses it.
        private OmsiTileSummary typePickTile;
        private object typePickElement;
        private VisualElement typePickOptions;
        private Label typePickHint;

        private bool PickingElementType => typePickElement != null;

        private static int ElementIdOf(object element)
        {
            return element is OmsiObjectRecord item ? item.Id : ((OmsiAttachedObjectRecord)element).Id;
        }

        private static string DescribeElement(object element)
        {
            return (element is OmsiObjectRecord ? "object #" : "attached object #") + ElementIdOf(element);
        }

        /// <summary>The button to choose another object file, and the labels as text fields with a button that writes them together.</summary>
        /// <param name="nested">The element hangs on the selected object: a small heading, and the file button sits in its row.</param>
        private void AddTypeAndLabels(VisualElement editor, OmsiTileSummary tile, object element, bool nested)
        {
            var canEdit = CanEditObjects;
            var labels = element is OmsiObjectRecord item ? item.Labels : ((OmsiAttachedObjectRecord)element).Labels;
            if (canEdit && !nested)
            {
                var change = new Button(() => PickElementType(tile, element))
                {
                    text = "Change type...",
                    tooltip = "Choose another object file for it in the library; its place and labels stay"
                };
                change.AddToClassList("map-control-button");
                change.AddToClassList("inspector-edit-apply");
                editor.Add(change);
            }
            if (canEdit && !nested)
                AddVariantNote(editor, element);
            if (labels.Count == 0)
                return;

            var heading = new Label(nested ? "Labels" : "LABELS");
            heading.AddToClassList(nested ? "muted" : "inspector-section-heading");
            if (nested)
                heading.style.marginLeft = 12;
            editor.Add(heading);
            var fields = new List<TextField>();
            for (var index = 0; index < labels.Count; index++)
            {
                var field = new TextField("Line " + (index + 1)) { value = labels[index] };
                field.AddToClassList("inspector-edit-field");
                if (nested)
                    field.style.marginLeft = 12;
                field.SetEnabled(canEdit);
                fields.Add(field);
                editor.Add(field);
            }
            if (!canEdit)
                return;
            var apply = new Button(() => ApplyElementLabels(tile, element, fields.ConvertAll(field => field.value)))
            {
                text = "Apply labels",
                tooltip = "Writes all lines; they fill the object's string variables in order, such as a sign's text or the line number on a plate"
            };
            apply.AddToClassList("map-control-button");
            apply.AddToClassList("inspector-edit-apply");
            if (nested)
                apply.style.marginLeft = 12;
            editor.Add(apply);
        }

        /// <summary>While a chrono variant is edited: where a new type or new labels of the element are written.</summary>
        private void AddVariantNote(VisualElement editor, object element)
        {
            if (!EditingVariant)
                return;
            var own = session.IsInEditedVariant(element);
            var note = new Label(own
                ? "Added by " + editedVariant.Title + ": its own record changes."
                : "A new type or new labels are written into " + editedVariant.Title + " ([selobject] with [typ] or [relabel]); the " +
                  (session.OriginOf(element) != null ? "variant that adds it" : "base map") + " keeps its own.");
            note.AddToClassList("muted");
            note.AddToClassList("inspector-edit-hint");
            editor.Add(note);
        }

        /// <summary>Shows the object library to choose the new object file of an element; Esc or the Cancel button leaves it as it is.</summary>
        private void PickElementType(OmsiTileSummary tile, object element)
        {
            if (objectsPanel == null || currentSummary == null || tile == null || element == null)
                return;
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("change object types"));
                return;
            }
            if (PickingElementType)
                EndElementTypePick(null);
            if (activeTool == EditorTool.Road || activeTool == EditorTool.Rows)
                SetTool(EditorTool.Drag);
            StopPlacement();
            if (typePickOptions == null)
            {
                typePickOptions = new VisualElement();
                typePickOptions.AddToClassList("tool-panel");
                typePickOptions.AddToClassList("picker-options");
                var title = new Label("New object file");
                title.AddToClassList("picker-options-title");
                typePickOptions.Add(title);
                typePickHint = new Label();
                typePickHint.AddToClassList("panel-info");
                typePickOptions.Add(typePickHint);
                var cancel = new Button(() => EndElementTypePick(null)) { text = "Cancel", tooltip = "Leave the type as it is (Esc)" };
                cancel.AddToClassList("map-control-button");
                typePickOptions.Add(cancel);
            }
            typePickHint.text = "Choose the object that " + DescribeElement(element) + " becomes. It keeps its place, orientation and labels.";
            UpdateObjectsCatalog();
            StartInstallationScan();
            typePickTile = tile;
            typePickElement = element;
            objectsPanel.UseOptions(typePickOptions, "Click the new object for " + DescribeElement(element) + "; Esc leaves it as it is.",
                "Click the object that " + DescribeElement(element) + " becomes.");
            objectsPanelHost.style.display = DisplayStyle.Flex;
            objectsPanel.style.display = DisplayStyle.Flex;
            objectsPanel.FocusSearch();
            SetStatus("Click the new object for " + DescribeElement(element) + " in the library.");
        }

        /// <summary>Closes the library again, and gives the element the object file that was clicked; null leaves it as it is.</summary>
        private void EndElementTypePick(string assetPath)
        {
            if (!PickingElementType)
                return;
            var tile = typePickTile;
            var element = typePickElement;
            typePickTile = null;
            typePickElement = null;
            objectsPanel.UseOptions(null, null);
            objectsPanel.ClearChosen();
            if (!objectsPanelOpen)
            {
                objectsPanel.style.display = DisplayStyle.None;
                objectsPanelHost.style.display = DisplayStyle.None;
            }
            if (assetPath != null)
                ApplyElementType(tile, element, assetPath);
            else
                SetStatus("The type stays as it is.");
        }

        private void ApplyElementType(OmsiTileSummary tile, object element, string assetPath)
        {
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("change object types"));
                return;
            }
            OmsiObjectChange change;
            try
            {
                change = session.SetElementType(tile, element, assetPath);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The type of " + DescribeElement(element) + " could not be changed: " + exception.Message);
                return;
            }
            if (change != null)
                EnsureOutline(assetPath);
            FinishElementChange(tile, change, "the new type of " + DescribeElement(element),
                DescribeElement(element) + " is " + Path.GetFileNameWithoutExtension(assetPath) + " now", DescribeElement(element) + " already is " +
                Path.GetFileNameWithoutExtension(assetPath) + ".");
        }

        /// <summary>
        /// Computes the outline of an object file the map did not use before, as placing one does, so the object's footprint shows
        /// on the map and can be clicked; the outlines of the kinds a map has come with it when it opens.
        /// </summary>
        private void EnsureOutline(string assetPath)
        {
            if (currentSummary == null || outlineCache.ContainsKey(assetPath))
                return;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (!string.Equals(root, outlineInstallationRoot, StringComparison.OrdinalIgnoreCase))
                return;
            outlineCache[assetPath] = new OmsiObjectOutlineCatalog(root).Get(assetPath);
            mapCanvas?.SetOutlines(outlineCache);
        }

        private void ApplyElementLabels(OmsiTileSummary tile, object element, IReadOnlyList<string> labels)
        {
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("change labels"));
                return;
            }
            OmsiObjectChange change;
            try
            {
                change = session.SetElementLabels(tile, element, labels);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The labels of " + DescribeElement(element) + " could not be changed: " + exception.Message);
                return;
            }
            FinishElementChange(tile, change, "the labels of " + DescribeElement(element), "The labels of " + DescribeElement(element) + " are written",
                "The labels of " + DescribeElement(element) + " are the same.");
        }

        /// <summary>One undo step for a change, the views refreshed, and the object it concerns selected, such as the parent of an attached object.</summary>
        private void FinishElementChange(OmsiTileSummary tile, OmsiObjectChange change, string description, string done, string same)
        {
            if (change == null)
            {
                SetStatus(same);
                return;
            }
            PushHistory(new ObjectEditStep { TileX = tile.X, TileY = tile.Y, ObjectId = change.ElementId, Change = change, Description = description });
            var shown = change.CurrentRecord;
            RefreshAfterChange(shown != null ? new[] { new MapSelectionItem { Tile = tile, Record = shown } } : null);
            SetStatus(done + (EditingVariant ? " in " + editedVariant.Title : string.Empty) + ". Ctrl+Z undoes it.");
        }

        /// <summary>
        /// Command line (ADR 060): <c>-retype file.sco</c> and <c>-relabel line|line|...</c> change the selected object, or with
        /// <c>-elementId n</c> the attached object with that ID in its tile; the type comes first. <c>-pickType [file.sco]</c>
        /// opens the library to choose the new file and, with a file, clicks it. <c>-elementUndo [n]</c> and
        /// <c>-elementRedo [n]</c> then step through the history as Ctrl+Z and Ctrl+Y do.
        /// </summary>
        private void RunElementArguments(string[] args)
        {
            string Argument(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal) ? args[index + 1] : null;
            }

            var retype = Argument("-retype");
            var relabel = Argument("-relabel");
            var picking = Array.IndexOf(args, "-pickType") >= 0;
            if (retype != null || relabel != null || picking)
            {
                if (selection.Count != 1)
                {
                    Debug.LogWarning("-retype, -relabel and -pickType need one selected object.");
                    return;
                }
                var tile = TileHolding(selection[0]) ?? selection[0].Tile;
                var attached = false;
                var id = selection[0].Record.Id;
                if (int.TryParse(Argument("-elementId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementId))
                {
                    id = elementId;
                    attached = tile.AttachedObjects.Exists(candidate => candidate.Id == elementId);
                    if (!attached && !tile.Objects.Exists(candidate => candidate.Id == elementId))
                    {
                        Debug.LogWarning("-elementId " + elementId + " names no object or attached object in tile " + tile.X + ", " + tile.Y + ".");
                        return;
                    }
                }

                object Current()
                {
                    return attached ? (object)tile.AttachedObjects.Find(candidate => candidate.Id == id) : tile.Objects.Find(candidate => candidate.Id == id);
                }

                if (picking)
                {
                    PickElementType(tile, Current());
                    Debug.Log("Pick type: " + statusLabel?.text + " (library " + (objectsPanel != null && objectsPanel.style.display == DisplayStyle.Flex ? "shown" : "hidden") + ")");
                    var chosen = Argument("-pickType");
                    if (chosen != null)
                    {
                        EndElementTypePick(chosen);
                        Debug.Log("Pick type: " + statusLabel?.text);
                    }
                }
                if (retype != null)
                {
                    ApplyElementType(tile, Current(), retype);
                    Debug.Log("Retype: " + statusLabel?.text);
                }
                if (relabel != null)
                {
                    ApplyElementLabels(tile, Current(), relabel.Split('|'));
                    Debug.Log("Relabel: " + statusLabel?.text);
                }
            }

            if (Array.IndexOf(args, "-elementUndo") >= 0)
                for (var undo = int.TryParse(Argument("-elementUndo"), out var undoCount) ? undoCount : 1; undo > 0; undo--)
                {
                    StepHistory(undoSteps, redoSteps, true);
                    Debug.Log("Undo: " + statusLabel?.text);
                }
            if (Array.IndexOf(args, "-elementRedo") >= 0)
                for (var redo = int.TryParse(Argument("-elementRedo"), out var redoCount) ? redoCount : 1; redo > 0; redo--)
                {
                    StepHistory(redoSteps, undoSteps, false);
                    Debug.Log("Redo: " + statusLabel?.text);
                }
        }
    }
}
