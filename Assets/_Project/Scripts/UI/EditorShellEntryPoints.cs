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
    /// Entry points (ADR 079): an entry point is chosen in the object library like any object; a click in the map with an
    /// <c>[entrypoint]</c> object asks for its name and writes the object and its line in global.cfg as one undo step. The inspector of the
    /// object an entry point stands on renames it or deletes it with its object, and the context menu offers both.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // True while the object chosen for placing is an [entrypoint] object: each click asks for a name first.
        private bool placementIsEntryPoint;

        private VisualElement entryNameDialog;
        private TextField entryNameField;
        private Label entryNameError;
        private Label entryNameTaken;

        // The names the map's entry points have, read when the dialog opens; the hint under the field compares against them.
        private HashSet<string> entryNamesInUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // What the answer of the name dialog does (a placing click or a type change); null while the dialog is closed, so a second answer cannot act twice.
        private Action<string> entryNameAnswer;
        private Button entryNameOk;

        private readonly Dictionary<string, bool> entryPointByAsset = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Entry points name tiles of the base map: not while a chrono variant is edited.</summary>
        private bool CanEditEntryPoints => CanEdit && !EditingVariant;

        /// <summary>Whether an object file is an <c>[entrypoint]</c> object: its file says so, and the editor's own entry point object always is.</summary>
        private bool IsEntryPointAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            if (string.Equals(assetPath, OmsiNewMap.EntryPointObject, StringComparison.OrdinalIgnoreCase))
                return true;
            if (entryPointByAsset.TryGetValue(assetPath, out var known))
                return known;
            var isEntry = false;
            try
            {
                var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
                var path = OmsiRelativePath.Resolve(root, assetPath);
                if (File.Exists(path))
                    isEntry = OmsiSceneryObject.Parse(File.ReadAllBytes(path)).IsEntryPoint;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException ||
                                              exception is ArgumentException)
            {
                // An object file that cannot be read is no entry point for the editor.
            }
            entryPointByAsset[assetPath] = isEntry;
            return isEntry;
        }

        /// <summary>The status text that says why an entry point cannot be put down now, or null when it can.</summary>
        private string EntryPlacementProblem()
        {
            if (!CanEditEntryPoints)
                return ReadOnlyMessage("readonly.action.entry_points");
            if (!session.CanRegisterEntryPoints(out _))
                return Loc.T("entry.status.no_list");
            return null;
        }

        /// <summary>
        /// A click in the map while an entry point object is carried: asks for the name, then <see cref="PlaceEntryPoint"/> makes it. Esc or
        /// Cancel puts nothing down; the object stays in hand for another click.
        /// </summary>
        private void AskEntryName(MapPlacement placement)
        {
            if (entryNameAnswer != null)
                return;
            var problem = EntryPlacementProblem();
            if (problem != null)
            {
                SetStatus(problem);
                return;
            }
            OpenEntryNameDialog("Start", false, name =>
            {
                // The map may have been read again while the dialog was open.
                if (currentSummary == null || session == null || placement.Tile == null || !currentSummary.Tiles.Contains(placement.Tile) || !CanEditObjects)
                {
                    SetStatusT("entry.status.stale");
                    return;
                }
                PlaceEntryPoint(placement, name);
            });
        }

        private void OpenEntryNameDialog(string wanted, bool forTypeChange, Action<string> answer)
        {
            EnsureEntryNameDialog();
            entryNameAnswer = answer;
            if (forTypeChange)
                LocBindings.Text(entryNameOk, "entry.name.ok_retype");
            else
                LocBindings.Text(entryNameOk, "entry.name.ok");
            entryNameError.style.display = DisplayStyle.None;
            entryNamesInUse = new HashSet<string>(session.ReadEntryPoints(currentSummary).ConvertAll(entry => entry.Name), StringComparer.OrdinalIgnoreCase);
            entryNameField.SetValueWithoutNotify(session.UniqueEntryName(currentSummary, wanted));
            UpdateEntryNameHint();
            entryNameDialog.style.display = DisplayStyle.Flex;
            entryNameField.schedule.Execute(() =>
            {
                entryNameField.Focus();
                entryNameField.SelectAll();
            });
        }

        private void EnsureEntryNameDialog()
        {
            if (entryNameDialog != null)
                return;
            entryNameDialog = new VisualElement { name = "entry-name-dialog" };
            entryNameDialog.AddToClassList("dialog-overlay");
            entryNameDialog.style.display = DisplayStyle.None;
            RegisterOverlay(entryNameDialog, true, () =>
            {
                entryNameAnswer = null;
                mapCanvas?.Focus();
            });
            var dialog = new VisualElement();
            dialog.AddToClassList("dialog");
            dialog.AddToClassList("content-folders"); // the same text colour as the other dialogs
            var title = LocBindings.NewLabel("entry.name.title");
            title.AddToClassList("popup-title");
            dialog.Add(title);
            var description = LocBindings.NewLabel("entry.name.text");
            description.AddToClassList("popup-description");
            dialog.Add(description);
            entryNameField = new TextField();
            LocBindings.Text(entryNameField, "entry.field.name");
            entryNameField.AddToClassList("dialog-field");
            entryNameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                evt.StopPropagation();
                ConfirmEntryName();
            });
            entryNameField.RegisterValueChangedCallback(_ => UpdateEntryNameHint());
            dialog.Add(entryNameField);
            entryNameTaken = LocBindings.NewLabel("entry.name.taken");
            entryNameTaken.AddToClassList("popup-description");
            entryNameTaken.AddToClassList("entry-name-taken");
            entryNameTaken.style.display = DisplayStyle.None;
            dialog.Add(entryNameTaken);
            entryNameError = LocBindings.NewLabel("entry.status.bad_name");
            entryNameError.AddToClassList("popup-description");
            entryNameError.AddToClassList("entry-name-error");
            entryNameError.style.display = DisplayStyle.None;
            dialog.Add(entryNameError);
            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            var cancel = LocBindings.NewButton("entry.name.cancel", CancelEntryName);
            cancel.AddToClassList("map-control-button");
            buttons.Add(cancel);
            entryNameOk = LocBindings.NewButton("entry.name.ok", ConfirmEntryName);
            entryNameOk.AddToClassList("map-control-button");
            entryNameOk.AddToClassList("dialog-primary");
            buttons.Add(entryNameOk);
            dialog.Add(buttons);
            entryNameDialog.Add(dialog);
            rootElement.Add(entryNameDialog);
        }

        /// <summary>Shows under the field that the name is already an entry point's; it only informs, the name can still be used.</summary>
        private void UpdateEntryNameHint()
        {
            if (entryNameTaken == null || entryNameField == null)
                return;
            var name = (entryNameField.value ?? string.Empty).Trim();
            entryNameTaken.style.display = name.Length > 0 && entryNamesInUse.Contains(name) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void CancelEntryName()
        {
            entryNameAnswer = null;
            HidePopup(entryNameDialog);
            mapCanvas?.Focus();
        }

        /// <summary>The answer of the name dialog: a bad name keeps it open with the reason; a good one makes the entry point.</summary>
        private void ConfirmEntryName()
        {
            var answer = entryNameAnswer;
            if (answer == null || entryNameField == null)
                return;
            var name = (entryNameField.value ?? string.Empty).Trim();
            if (name.Length == 0 || name.StartsWith("[", StringComparison.Ordinal))
            {
                entryNameError.style.display = DisplayStyle.Flex;
                return;
            }
            entryNameAnswer = null;
            HidePopup(entryNameDialog);
            mapCanvas?.Focus();
            answer(name);
        }

        /// <summary>
        /// Choosing an <c>[entrypoint]</c> object as the new type of an object that is none (or the other way round) must not leave an entry point
        /// global.cfg does not list (ADR 079). Returns true when the type change was handled here: refused with a status text, waiting for the name
        /// in the dialog (<see cref="ApplyElementType"/> goes on with it), or finished.
        /// </summary>
        private bool HandleEntryPointTypeChange(OmsiTileSummary tile, object element, string assetPath)
        {
            if (currentSummary == null || session == null)
                return false;
            var current = AssetPathOf(element);
            if (string.Equals(current, assetPath, StringComparison.Ordinal))
                return false;
            string problem;
            try
            {
                problem = session.EntryPointTypeProblem(currentSummary, tile, element, assetPath, IsEntryPointAsset);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                problem = exception.Message;
            }
            if (problem != null)
            {
                SetStatusT("entry.status.retype_refused", ("element", DescribeElement(element)), ("reason", problem));
                return true;
            }
            var item = element as OmsiObjectRecord;
            if (item == null || !IsEntryPointAsset(assetPath) || session.EntryPointOf(currentSummary, tile, item) != null)
                return false;
            // A new entry point: asks for its name, the object's own first label as the suggestion.
            if (entryNameAnswer != null)
                return true;
            var wanted = item.Labels.Count > 0 && !string.IsNullOrWhiteSpace(item.Labels[0]) ? item.Labels[0] : "Start";
            OpenEntryNameDialog(wanted, true, name =>
            {
                if (currentSummary == null || session == null || !currentSummary.Tiles.Contains(tile) || !tile.Objects.Contains(item) || !CanEditObjects)
                {
                    SetStatusT("entry.status.stale_retype");
                    return;
                }
                ApplyElementType(tile, element, assetPath, name);
            });
            return true;
        }

        /// <summary>The name asked for, the object and its line in global.cfg as one undo step.</summary>
        private void PlaceEntryPoint(MapPlacement placement, string name)
        {
            var problem = EntryPlacementProblem();
            if (problem != null)
            {
                SetStatus(problem);
                return;
            }
            var ground = 0.0;
            var world = OmsiMapGeometry.TileLocalToWorld(placement.Tile.X, placement.Tile.Y, placement.LocalX, placement.LocalY, currentSummary.TileSizeMeters);
            if (new OmsiTerrainSampler(currentSummary, session.Source).TryGetHeight(world.X, world.Y, out var found))
                ground = found;
            OmsiObjectChange change;
            try
            {
                change = session.AddEntryPoint(currentSummary, placement.Tile, name, placement.LocalX, placement.LocalY, ground, mapCanvas.PlacementHeading);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("entry.status.failed", ("reason", exception.Message));
                StopPlacement();
                return;
            }
            if (!placement.KeepPlacing)
                StopPlacement();
            PushHistory(new ObjectEditStep { TileX = placement.Tile.X, TileY = placement.Tile.Y, ObjectId = change.Record.Id, Change = change, Description = Loc.T("entry.step.add", ("name", name)) });
            RefreshAfterChange(new[] { new MapSelectionItem { Tile = placement.Tile, Record = change.Record } });
            if (objectsPanelOpen)
                UpdateObjectsCatalog();
            RenderProjectBrowser();
            SetStatusT("entry.status.added", ("name", name), ("x", placement.Tile.X), ("y", placement.Tile.Y));
        }

        /// <summary>The inspector's group for an object that is an entry point: its name, and a button that renames it.</summary>
        private void AddEntryPointGroup(VisualElement editor, OmsiEntryPoint entry)
        {
            AddInspectorGroup(editor, "object-entry", Loc.T("entry.inspector.heading"), true, body =>
            {
                var name = new TextField { value = entry.Name };
                LocBindings.Text(name, "entry.field.name");
                name.AddToClassList("inspector-edit-field");
                name.SetEnabled(CanEditEntryPoints);
                body.Add(name);
                if (CanEditEntryPoints)
                {
                    var rename = new Button(() => RenameEntryPoint(entry, name.value));
                    LocBindings.Text(rename, "entry.rename");
                    rename.AddToClassList("map-control-button");
                    rename.AddToClassList("inspector-edit-apply");
                    body.Add(rename);
                }
                var shared = new Label(Loc.T("entry.shared_hint"));
                shared.AddToClassList("muted");
                shared.AddToClassList("inspector-edit-hint");
                body.Add(shared);
            });
        }

        private void RenameEntryPoint(OmsiEntryPoint entry, string name)
        {
            if (!CanEditEntryPoints)
            {
                SetStatus(ReadOnlyMessage("readonly.action.entry_points"));
                return;
            }
            OmsiObjectChange change;
            try
            {
                change = session.RenameEntryPoint(currentSummary, entry, name);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatusT("entry.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
            {
                SetStatusT("entry.status.same");
                return;
            }
            PushHistory(new ObjectEditStep { TileX = entry.Tile.X, TileY = entry.Tile.Y, ObjectId = entry.ObjectId, Change = change, Description = Loc.T("entry.step.rename") });
            RefreshAfterChange(new[] { new MapSelectionItem { Tile = entry.Tile, Record = change.CurrentRecord ?? entry.Object } });
            RenderProjectBrowser();
            SetStatusT("entry.status.renamed", ("name", name.Trim()));
        }

        private void DeleteEntryPoint(OmsiEntryPoint entry)
        {
            if (!CanEditEntryPoints)
            {
                SetStatus(ReadOnlyMessage("readonly.action.entry_points"));
                return;
            }
            OmsiObjectChangeResult result;
            try
            {
                result = session.DeleteEntryPoint(currentSummary, entry);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                SetStatusT("entry.status.failed", ("reason", exception.Message));
                return;
            }
            if (result.Change == null)
            {
                SetStatusT("entry.status.blocked", ("references", DescribeReferences(result.Blockers)));
                return;
            }
            PushHistory(new ObjectEditStep { TileX = entry.Tile.X, TileY = entry.Tile.Y, ObjectId = entry.ObjectId, Change = result.Change, Description = Loc.T("entry.step.delete") });
            RefreshAfterChange(null);
            RenderProjectBrowser();
            SetStatusT("entry.status.deleted", ("name", entry.Name));
        }

        /// <summary>
        /// Command line, with a project: <c>-entryAdd Name tileX tileY x y [heading]</c> makes an entry point there, <c>-entryRename objectID Name</c> and
        /// <c>-entryDelete objectID</c> change the one standing on that object; "Entry point:" in the log. ADR 079. An entry point object chosen with
        /// <c>-place</c> asks for its name like a click does; <c>-placeEntryName Name</c> answers that dialog, <c>-placeEntryDraft Name</c> only types the name.
        /// </summary>
        private void RunEntryPointArguments(string[] args)
        {
            if (session == null || currentSummary == null)
                return;
            var add = Array.IndexOf(args, "-entryAdd");
            if (add >= 0 && add + 5 < args.Length &&
                int.TryParse(args[add + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileX) &&
                int.TryParse(args[add + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileY) &&
                double.TryParse(args[add + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(args[add + 5], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                var heading = 0.0;
                if (add + 6 < args.Length)
                    double.TryParse(args[add + 6], NumberStyles.Float, CultureInfo.InvariantCulture, out heading);
                var tile = currentSummary.Tiles.Find(candidate => candidate.X == tileX && candidate.Y == tileY);
                if (tile == null)
                {
                    Debug.Log("Entry point: no tile " + tileX + ", " + tileY);
                    return;
                }
                mapCanvas.BeginPlacement(OmsiNewMap.EntryPointObject, heading);
                PlaceEntryPoint(new MapPlacement { Tile = tile, LocalX = x, LocalY = y }, args[add + 1]);
                Debug.Log("Entry point: " + statusLabel?.text);
            }

            OmsiEntryPoint OfObject(string text)
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    return null;
                return session.ReadEntryPoints(currentSummary).Find(candidate => candidate.ObjectId == id);
            }

            var rename = Array.IndexOf(args, "-entryRename");
            if (rename >= 0 && rename + 2 < args.Length)
            {
                var entry = OfObject(args[rename + 1]);
                if (entry == null)
                    Debug.Log("Entry point: no entry point on object " + args[rename + 1]);
                else
                {
                    RenameEntryPoint(entry, args[rename + 2]);
                    Debug.Log("Entry point: " + statusLabel?.text);
                }
            }
            var delete = Array.IndexOf(args, "-entryDelete");
            if (delete >= 0 && delete + 1 < args.Length)
            {
                var entry = OfObject(args[delete + 1]);
                if (entry == null)
                    Debug.Log("Entry point: no entry point on object " + args[delete + 1]);
                else
                {
                    DeleteEntryPoint(entry);
                    Debug.Log("Entry point: " + statusLabel?.text);
                }
            }
        }

        /// <summary>After <c>-place</c> with <c>-placeCommit</c>: answers the name dialog of an entry point object with <c>-placeEntryName</c>, if given.</summary>
        private void AnswerEntryNameFromArguments(string[] args)
        {
            if (entryNameAnswer == null)
                return;
            // -placeEntryDraft types a name without answering, so a screenshot shows the dialog with its hint.
            var draft = Array.IndexOf(args, "-placeEntryDraft");
            if (draft >= 0 && draft + 1 < args.Length)
            {
                entryNameField.SetValueWithoutNotify(args[draft + 1]);
                UpdateEntryNameHint();
                return;
            }
            var index = Array.IndexOf(args, "-placeEntryName");
            if (index < 0 || index + 1 >= args.Length)
                return;
            entryNameField.SetValueWithoutNotify(args[index + 1]);
            UpdateEntryNameHint();
            ConfirmEntryName();
        }
    }
}
