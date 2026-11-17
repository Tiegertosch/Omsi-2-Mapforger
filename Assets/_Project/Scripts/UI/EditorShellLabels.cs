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
        private TypePickContext typePickStart;

        private bool PickingElementType => typePickElement != null;

        /// <summary>The label lines of one element as they stand in the inspector, and what was written last.</summary>
        private sealed class LabelEdit
        {
            public OmsiTileSummary Tile;
            public object Element;
            public OmsiMapSummary Summary;
            public OmsiChronoVariant Variant;
            public string[] Written;
            public List<TextField> Fields;
            // Typed values that could not be written when the inspector was drawn anew (the map could not be edited then).
            public string[] Carried;

            public bool StillApplies(OmsiMapSummary nowSummary, OmsiChronoVariant nowVariant)
            {
                var inTile = Tile != null && (Element is OmsiObjectRecord item ? Tile.Objects.Contains(item) : Element is OmsiAttachedObjectRecord attached && Tile.AttachedObjects.Contains(attached));
                return LabelEditRules.StillApplies(Summary, Variant, nowSummary, nowVariant, inTile);
            }

            public string[] Current()
            {
                var values = new string[Fields.Count];
                for (var index = 0; index < values.Length; index++)
                    values[index] = Fields[index].value ?? string.Empty;
                return values;
            }
        }

        // The label lines the inspector shows now; Enter, leaving them and drawing the inspector anew write what was changed.
        private readonly List<LabelEdit> labelEdits = new List<LabelEdit>();

        // The note that typed lines were discarded; ShowStatusText adds it to every status of the same frame, so the caller's status does not hide it.
        private string discardNotice;
        private int discardNoticeFrame;

        // Lines typed while the map could not be edited: kept and shown again until they can be written.
        private readonly List<LabelEdit> carriedLabelEdits = new List<LabelEdit>();

        // The note of the selected element in a chrono variant that is edited, kept for the element and variant it was made for.
        private object variantNoteElement;
        private OmsiChronoVariant variantNoteVariant;
        private string variantNote = string.Empty;

        private static int ElementIdOf(object element)
        {
            return element is OmsiObjectRecord item ? item.Id : ((OmsiAttachedObjectRecord)element).Id;
        }

        private static string AssetPathOf(object element)
        {
            return element is OmsiObjectRecord item ? item.AssetPath : ((OmsiAttachedObjectRecord)element).AssetPath;
        }

        private static IReadOnlyList<string> LabelsOf(object element)
        {
            return element is OmsiObjectRecord item ? item.Labels : ((OmsiAttachedObjectRecord)element).Labels;
        }

        // The label schemas of the installation of the open map, read from the object files once per file; made again for another installation.
        private OmsiLabelSchemaCatalog labelSchemas;
        private string labelSchemaRoot;
        private readonly HashSet<string> labelSchemaChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The label fields the object file defines (kind, script variables, [texttexture]); empty when the file cannot be read, so the
        /// rows fall back to the labels the record has. A file that cannot be read is logged once.
        /// </summary>
        private OmsiLabelSchema SchemaFor(string assetPath)
        {
            if (currentSummary == null || string.IsNullOrWhiteSpace(assetPath))
                return OmsiLabelSchema.Empty;
            string root;
            try
            {
                root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            }
            catch (ArgumentException)
            {
                return OmsiLabelSchema.Empty;
            }
            if (string.IsNullOrEmpty(root))
                return OmsiLabelSchema.Empty;
            if (labelSchemas == null || !string.Equals(root, labelSchemaRoot, StringComparison.OrdinalIgnoreCase))
            {
                labelSchemas = new OmsiLabelSchemaCatalog(root);
                labelSchemaRoot = root;
                labelSchemaChecked.Clear();
            }
            var schema = labelSchemas.Get(assetPath);
            // LastProblem belongs to the file read last, which is this one when it was not read before.
            if (labelSchemaChecked.Add(OmsiLabelSchemaCatalog.KeyOf(assetPath) ?? assetPath) &&labelSchemas.LastProblem != null)
                Debug.LogWarning("Label names: " + labelSchemas.LastProblem);
            return schema;
        }

        private bool IsLeftHandTraffic()
        {
            try
            {
                return session != null && session.HasLeftHandTraffic();
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private static string DescribeElement(object element)
        {
            return Loc.T("labels.element", ("kind", element is OmsiObjectRecord ? "object" : "attached"), ("id", ElementIdOf(element)));
        }

        /// <summary>The text with a capital first letter, for a description that starts a sentence.</summary>
        private static string Capital(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>The icon button that starts choosing another object file for the element (<see cref="PickElementType"/>).</summary>
        private Button NewChangeTypeButton(OmsiTileSummary tile, object element)
        {
            var button = new Button(() => PickElementType(tile, element));
            LocBindings.TitledTooltip(button, "labels.change_type", "labels.change_type.tooltip");
            button.AddToClassList("panel-icon-button");
            button.Add(new IconElement("library"));
            // It may stand in the header of a group that folds on a click.
            button.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            return button;
        }

        /// <summary>
        /// The label lines of the element as text fields with a tag icon, the full width of the inspector. Enter and leaving the lines write
        /// them together as one undo step if one changed; Esc in a line takes its change back.
        /// </summary>
        /// <param name="nested">The element hangs on the selected object: a small heading, and the lines are indented.</param>
        private void AddLabelLines(VisualElement editor, OmsiTileSummary tile, object element, bool nested)
        {
            var labels = LabelsOf(element);
            // The rows the object type names (and any label past them); an object without a known schema and without labels has none.
            var schema = SchemaFor(AssetPathOf(element));
            var rows = schema.RowsFor(labels.Count, LabelCaptions.SpareRows);
            if (rows.Count == 0)
                return;
            var canEdit = CanEditObjects;
            var leftHandTraffic = LabelCaptions.NeedsTrafficSide(rows) && IsLeftHandTraffic();
            if (nested)
            {
                var heading = new Label();
                LocBindings.Text(heading, "labels.heading");
                heading.AddToClassList("muted");
                heading.style.marginLeft = 12;
                editor.Add(heading);
            }
            var box = new VisualElement();
            if (nested)
                box.style.marginLeft = 12;
            var written = new string[rows.Count];
            for (var index = 0; index < written.Length; index++)
                written[index] = index < labels.Count ? labels[index] ?? string.Empty : string.Empty;
            var edit = new LabelEdit
            {
                Tile = tile, Element = element, Summary = currentSummary, Variant = editedVariant,
                Written = written, Fields = new List<TextField>()
            };
            // Values typed before and not written (the map could not be edited then) come back into the lines.
            var carried = carriedLabelEdits.Find(old => ReferenceEquals(old.Element, element) && old.StillApplies(currentSummary, editedVariant));
            var restored = carried != null && carried.Carried.Length == rows.Count;
            if (restored && canEdit)
                carriedLabelEdits.Remove(carried);
            for (var index = 0; index < rows.Count; index++)
            {
                var line = index;
                var caption = LabelCaptions.Text(rows[index], leftHandTraffic);
                var captionLabel = new Label(caption) { pickingMode = PickingMode.Ignore };
                captionLabel.AddToClassList("muted");
                captionLabel.AddToClassList("label-line-caption");
                box.Add(captionLabel);
                var row = new VisualElement();
                row.AddToClassList("label-line");
                row.tooltip = caption + "\n" + LabelCaptions.Technical(rows[index]);
                var mark = new IconElement("tag") { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("label-line-icon");
                row.Add(mark);
                var field = new TextField { value = restored ? carried.Carried[index] : written[index] };
                field.textEdition.placeholder = string.Empty;
                field.AddToClassList("inspector-edit-field");
                field.AddToClassList("label-line-field");
                field.SetEnabled(canEdit);
                if (canEdit)
                    field.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                        {
                            evt.StopPropagation();
                            if (LabelEditRules.WritesOnEnter(edit.Written, edit.Current()))
                                box.schedule.Execute(() => CommitLabelEdit(edit, false));
                        }
                        else if (evt.keyCode == KeyCode.Escape)
                        {
                            evt.StopPropagation();
                            field.SetValueWithoutNotify(edit.Written[line]);
                        }
                    }, TrickleDown.TrickleDown);
                edit.Fields.Add(field);
                row.Add(field);
                box.Add(row);
            }
            if (canEdit)
            {
                // Moving on to another line of the element is still typing; leaving the lines writes them. The write waits for the event to end,
                // as it draws the inspector anew.
                box.RegisterCallback<FocusOutEvent>(evt =>
                {
                    var staysInLines = evt.relatedTarget is VisualElement target && box.Contains(target);
                    if (LabelEditRules.WritesOnFocusLoss(staysInLines, edit.Written, edit.Current()))
                        box.schedule.Execute(() => CommitLabelEdit(edit, true));
                });
                labelEdits.Add(edit);
            }
            editor.Add(box);
        }

        /// <summary>Writes the label lines of one element if one changed; once. <paramref name="keepSelection"/> leaves the selection as it is.</summary>
        private void CommitLabelEdit(LabelEdit edit, bool keepSelection)
        {
            var current = edit.Current();
            // Nothing to lose: written already (Enter, then leaving the lines) or never changed.
            if (!LabelEditRules.Differs(edit.Written, current))
                return;
            if (!labelEdits.Contains(edit) || !edit.StillApplies(currentSummary, editedVariant))
            {
                // The lines were typed in a state that is gone; never drop them without a word.
                labelEdits.Remove(edit);
                Debug.LogWarning("Label lines of " + DescribeElement(edit.Element) + " were typed in a state that is gone and are not written.");
                SetStatus(Loc.T("labels.status.not_written", ("element", DescribeElement(edit.Element))));
                return;
            }
            edit.Written = current;
            ApplyElementLabels(edit.Tile, edit.Element, OmsiLabelSchema.LabelsToWrite(LabelsOf(edit.Element), current), keepSelection);
        }

        /// <summary>
        /// Writes the label lines that were typed and not yet written, before the inspector is drawn anew or the state changes (undo, a
        /// map, a chrono variant): nothing typed is lost silently. The selection stays as it is. Lines that cannot be written now (the map
        /// is not editable) are kept and shown again; lines of another state are dropped.
        /// </summary>
        /// <param name="discard">Nothing is written: the map is read again after a failed roll-back and the workspace is considered damaged. Typed and carried lines go, with a note.</param>
        private void FlushLabelEdits(bool discard = false)
        {
            if (discard)
            {
                var lost = labelEdits.FindAll(edit => LabelEditRules.Decide(edit.StillApplies(currentSummary, editedVariant), LabelEditRules.Differs(edit.Written, edit.Current()), false, true) == LabelEditRules.FlushAction.Discard);
                lost.AddRange(carriedLabelEdits.FindAll(old => old.StillApplies(currentSummary, editedVariant) && !lost.Contains(old)));
                labelEdits.Clear();
                carriedLabelEdits.Clear();
                if (lost.Count > 0)
                {
                    Debug.LogWarning("Label lines typed for " + lost.Count + " element(s) were not written because the map is read again after a failed roll-back.");
                    discardNotice = Loc.T("labels.status.discarded_typed", ("element", DescribeElement(lost[0].Element)));
                    discardNoticeFrame = Time.frameCount;
                    SetStatus(discardNotice);
                }
                return;
            }
            carriedLabelEdits.RemoveAll(old => !old.StillApplies(currentSummary, editedVariant));
            if (labelEdits.Count == 0)
                return;
            // Lines drawn in another map, variant or state (or for an element that is gone) are dropped: they must not land in this one.
            var pending = labelEdits.FindAll(edit => LabelEditRules.Decide(edit.StillApplies(currentSummary, editedVariant), LabelEditRules.Differs(edit.Written, edit.Current()), CanEditObjects, false) != LabelEditRules.FlushAction.Drop);
            labelEdits.Clear();
            foreach (var edit in pending)
            {
                var typed = edit.Current();
                if (!CanEditObjects)
                {
                    // Not written now; the redrawn lines show what was typed (AddLabelLines).
                    edit.Carried = typed;
                    carriedLabelEdits.RemoveAll(old => ReferenceEquals(old.Element, edit.Element));
                    carriedLabelEdits.Add(edit);
                    SetStatus(Loc.T("labels.status.kept_typed", ("element", DescribeElement(edit.Element))));
                    continue;
                }
                edit.Written = typed;
                ApplyElementLabels(edit.Tile, edit.Element, OmsiLabelSchema.LabelsToWrite(LabelsOf(edit.Element), typed), true);
            }
        }

        /// <summary>Forgets lines typed in the state that is left: a map loads or the chrono variant changes. <see cref="FlushLabelEdits"/> comes first.</summary>
        private void ClearLabelEdits()
        {
            labelEdits.Clear();
            carriedLabelEdits.Clear();
        }
        /// <summary>
        /// While a chrono variant is edited and one object is selected: the line for the status bar that says where a new type or new
        /// labels of it are written. Empty otherwise.
        /// </summary>
        private string VariantNoteHint()
        {
            var element = EditingVariant && CanEditObjects && selection.Count == 1 ? selection[0].Record : null;
            if (element == null)
            {
                variantNoteElement = null;
                variantNoteVariant = null;
                variantNote = string.Empty;
                return variantNote;
            }
            if (!ReferenceEquals(element, variantNoteElement) || !ReferenceEquals(editedVariant, variantNoteVariant))
            {
                variantNoteElement = element;
                variantNoteVariant = editedVariant;
                variantNote = session.IsInEditedVariant(element)
                    ? Loc.T("labels.note.own", ("variant", editedVariant.Title))
                    : Loc.T("labels.note.other", ("variant", editedVariant.Title), ("origin", session.OriginOf(element) != null ? "variant" : "base"));
            }
            return variantNote;
        }

        private TypePickContext CurrentTypePickContext()
        {
            var items = new List<(int TileX, int TileY, int Id)>();
            foreach (var entry in selection)
                items.Add((entry.Tile?.X ?? 0, entry.Tile?.Y ?? 0, entry.Record.Id));
            if (selectedSpline != null)
                items.Add((selectedSplineTile?.X ?? 0, selectedSplineTile?.Y ?? 0, -selectedSpline.Id));
            return new TypePickContext(activeTool, TypePickRules.SelectionKey(items), currentSummary, overlays.AnyShown() || barPopupOpen);
        }

        /// <summary>Ends a type pick whose situation changed (<see cref="TypePickRules"/>): another tool, selection, view or window. Nothing is written.</summary>
        private void EnforceTypePick()
        {
            if (!PickingElementType)
                return;
            var reason = TypePickRules.Evaluate(typePickStart, CurrentTypePickContext());
            if (reason == TypePickEnd.None)
                return;
            Debug.Log("Pick type ended: " + TypePickRules.Describe(reason));
            EndElementTypePick(null);
        }

        /// <summary>
        /// Shows the object library as a mode of the Objects window to choose the new object file of an element: the same option column,
        /// the element's current file, and a cancel button. Esc, the cancel button or the close button leaves it as it is, and so does
        /// anything that changes the situation (<see cref="TypePickRules"/>).
        /// </summary>
        private void PickElementType(OmsiTileSummary tile, object element)
        {
            if (objectsPanel == null || currentSummary == null || tile == null || element == null)
                return;
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.object_types"));
                return;
            }
            if (PickingElementType)
                EndElementTypePick(null);
            // The tools with a window of their own in the same place would stand beside the library: Drag first (it may be refused, as
            // the signals tool asks before it is left).
            if (!TypePickRules.CanStartIn(activeTool))
            {
                SetTool(EditorTool.Drag);
                if (!TypePickRules.CanStartIn(activeTool))
                    return;
            }
            StopPlacement();
            var path = AssetPathOf(element);
            typePickOptions = ToolCatalog.Options(icons: true);
            var currentRow = OptionControls.Row(typePickOptions, "labels.pick.current");
            var currentName = new Label(Path.GetFileNameWithoutExtension(path)) { tooltip = DescribeElement(element) + "\n" + path };
            currentName.AddToClassList("option-row-text");
            currentRow.Add(currentName);
            var replaceRow = OptionControls.Row(typePickOptions, "labels.pick.row");
            LocBindings.Tooltip(replaceRow.Q<Label>(), "labels.pick.row.tooltip");
            OptionControls.Choice(OptionControls.Group(replaceRow), "x", "labels.pick.cancel", "labels.pick.cancel.tooltip", () => EndElementTypePick(null));
            UpdateObjectsCatalog();
            StartInstallationScan();
            typePickTile = tile;
            typePickElement = element;
            // The hint stands in the status bar's hint place only, after the count of the library.
            var hint = Loc.T("labels.pick.hint", ("element", DescribeElement(element)));
            objectsPanel.UseOptions(typePickOptions, hint, hint);
            objectsPanelHost.style.display = DisplayStyle.Flex;
            objectsPanel.style.display = DisplayStyle.Flex;
            objectsPanel.FocusSearch();
            typePickStart = CurrentTypePickContext();
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
                SetStatusT("labels.status.stays");
        }

        /// <param name="entryName">Set once the name dialog answered for an object that becomes an entry point (ADR 079); otherwise null, and such a change asks first.</param>
        private void ApplyElementType(OmsiTileSummary tile, object element, string assetPath, string entryName = null)
        {
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.object_types"));
                return;
            }
            if (entryName == null && HandleEntryPointTypeChange(tile, element, assetPath))
                return;
            OmsiObjectChange change;
            try
            {
                change = session.SetElementType(currentSummary, tile, element, assetPath, IsEntryPointAsset, entryName);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus(Loc.T("labels.status.type_failed", ("element", DescribeElement(element)), ("reason", exception.Message)));
                return;
            }
            if (change != null)
                EnsureOutline(assetPath);
            FinishElementChange(tile, change, Loc.T("labels.step.type", ("element", DescribeElement(element))),
                Loc.T("labels.done.type", ("element", Capital(DescribeElement(element))), ("name", Path.GetFileNameWithoutExtension(assetPath))),
                Loc.T("labels.same.type", ("element", Capital(DescribeElement(element))), ("name", Path.GetFileNameWithoutExtension(assetPath))));
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

        private void ApplyElementLabels(OmsiTileSummary tile, object element, IReadOnlyList<string> labels, bool keepSelection = false)
        {
            if (!CanEditObjects)
            {
                SetStatus(ReadOnlyMessage("readonly.action.labels"));
                return;
            }
            OmsiObjectChange change;
            OmsiTerrainChange stopName = null;
            var oldFirstLabel = LabelsOf(element).Count > 0 ? LabelsOf(element)[0] : string.Empty;
            try
            {
                change = session.SetElementLabels(tile, element, labels);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus(Loc.T("labels.status.labels_failed", ("element", DescribeElement(element)), ("reason", exception.Message)));
                return;
            }
            if (change != null)
            {
                try
                {
                    stopName = ListedStopRename(tile, element, oldFirstLabel, labels);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                                  exception is UnauthorizedAccessException)
                {
                    // The labels are written and get their step below; the list keeps the old name.
                    Debug.LogException(exception);
                }
            }
            FinishElementChange(tile, change, Loc.T("labels.step.labels", ("element", DescribeElement(element))),
                Loc.T("labels.done.labels", ("element", DescribeElement(element))), Loc.T("labels.same.labels", ("element", DescribeElement(element))), keepSelection, stopName);
            if (stopName != null)
                AfterBusStopRenamed();
        }

        /// <summary>
        /// A bus stop's first label is its name and Busstops.cfg keeps a copy (the "Bus stop" group writes both). When the first label of a stop
        /// that the timetable lists under its old name is changed here, the copy follows in the same undo step (<see cref="LabelEditRules.RenamesListedStop"/>);
        /// a stop the timetable does not list, or lists under another name, is left alone. Writes the file and returns its change; no history step.
        /// </summary>
        private OmsiTerrainChange ListedStopRename(OmsiTileSummary tile, object element, string oldFirstLabel, IReadOnlyList<string> labels)
        {
            if (!(element is OmsiObjectRecord) || EditingVariant || labels.Count == 0 || session == null || currentSummary == null)
                return null;
            var tileNow = currentSummary.Tiles.Find(each => each.X == tile.X && each.Y == tile.Y);
            var recordNow = tileNow?.Objects.Find(each => each.Id == ((OmsiObjectRecord)element).Id);
            var listed = recordNow == null ? null : session.BusStopName(currentSummary, tileNow, recordNow);
            if (!LabelEditRules.RenamesListedStop(oldFirstLabel, labels[0], listed))
                return null;
            return session.SetBusStopName(currentSummary, tileNow, recordNow, labels[0]);
        }

        /// <summary>One undo step for a change, the views refreshed, and the object it concerns selected, such as the parent of an attached object; <paramref name="keepSelection"/> leaves the selection as it is. A <paramref name="timetableChange"/> done with it joins the same step.</summary>
        private void FinishElementChange(OmsiTileSummary tile, OmsiObjectChange change, string description, string done, string same, bool keepSelection = false, OmsiTerrainChange timetableChange = null)
        {
            if (change == null)
            {
                SetStatus(same);
                return;
            }
            var parts = new List<ObjectEditStep> { new ObjectEditStep { TileX = tile.X, TileY = tile.Y, ObjectId = change.ElementId, Change = change, Description = description } };
            if (timetableChange != null)
                parts.Add(new ObjectEditStep { TerrainChange = timetableChange, Description = description });
            PushHistory(StepGroup.Join(parts, group => new ObjectEditStep { Group = group, Description = description }));
            var shown = change.CurrentRecord;
            RefreshAfterChange(shown != null && !keepSelection ? new[] { new MapSelectionItem { Tile = tile, Record = shown } } : null);
            SetStatus(Loc.T("labels.done", ("done", done), ("inVariant", EditingVariant ? "yes" : "no"), ("variant", EditingVariant ? editedVariant.Title : string.Empty)));
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
                    Debug.Log("Pick type: " + (toolHints.TryGetValue(objectsPanel, out var pickHint) ? pickHint.Text : string.Empty) + " (library " + (objectsPanel != null && objectsPanel.style.display == DisplayStyle.Flex ? "shown" : "hidden") + ")");
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
                    // An object that becomes an entry point asks for its name first: -placeEntryName / -placeEntryDraft answer or fill that dialog.
                    AnswerEntryNameFromArguments(args);
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
