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
    /// Entry points (ADR 079): the project browser's Entries tab lists them and puts a new one down with a click in the map, and the
    /// inspector of the object an entry point stands on renames it or deletes it with its object.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // The name for the entry point the next placement makes; set only between asking for it and the placement starting.
        private string pendingEntryName;

        // The name of the entry point being placed, or null while an ordinary object is.
        private string placingEntryName;

        private string entryNameDraft = "Start";

        /// <summary>Entry points name tiles of the base map: not while a chrono variant is edited.</summary>
        private bool CanEditEntryPoints => CanEdit && !EditingVariant;

        /// <summary>The tab of the project browser: the entry points of the map, and the form for a new one.</summary>
        private VisualElement EntriesSection()
        {
            var section = new VisualElement();
            var entries = session.ReadEntryPoints(currentSummary);
            var heading = new Label(Loc.T("entry.heading", ("count", entries.Count)));
            heading.AddToClassList("inspector-group-title");
            heading.style.marginBottom = 4;
            section.Add(heading);
            var hint = new Label(Loc.T("entry.hint"));
            hint.AddToClassList("browser-map-description-text");
            hint.style.marginBottom = 6;
            section.Add(hint);
            foreach (var entry in entries)
            {
                var captured = entry;
                var row = new Button(() => SelectEntryPoint(captured))
                {
                    text = Loc.T("entry.row", ("name", entry.Name), ("x", entry.Tile?.X ?? 0), ("y", entry.Tile?.Y ?? 0)),
                    tooltip = Loc.T(entry.Object != null ? "entry.row.tooltip" : "entry.row.missing") /* i18n:keys */
                };
                row.AddToClassList("browser-tile-entry");
                row.EnableInClassList("browser-tile-entry-selected", entry.Object != null && IsSelected(entry.Object));
                row.EnableInClassList("browser-tile-entry-missing", entry.Object == null);
                row.SetEnabled(entry.Object != null);
                section.Add(row);
            }

            if (!CanEditEntryPoints)
            {
                var note = new Label(Loc.T("entry.readonly"));
                note.AddToClassList("muted");
                note.style.marginTop = 6;
                section.Add(note);
                return section;
            }
            var form = new VisualElement();
            form.style.marginTop = 8;
            var name = new TextField { value = entryNameDraft };
            LocBindings.Text(name, "entry.field.name");
            name.AddToClassList("inspector-edit-field");
            name.RegisterValueChangedCallback(evt => entryNameDraft = evt.newValue ?? string.Empty);
            form.Add(name);
            var place = new Button(() => StartEntryPlacement(entryNameDraft));
            LocBindings.Text(place, "entry.place");
            LocBindings.Tooltip(place, "entry.place.tooltip");
            place.AddToClassList("map-control-button");
            place.AddToClassList("inspector-edit-apply");
            form.Add(place);
            section.Add(form);
            return section;
        }

        private void SelectEntryPoint(OmsiEntryPoint entry)
        {
            if (entry?.Tile == null || entry.Object == null)
                return;
            SelectObject(entry.Tile, entry.Object, true);
            RenderProjectBrowser();
        }

        /// <summary>Starts putting a new entry point down with clicks; the name is checked first so a bad one fails before the pointer carries it.</summary>
        private void StartEntryPlacement(string name)
        {
            if (!CanEditEntryPoints)
            {
                SetStatus(ReadOnlyMessage("readonly.action.entry_points"));
                return;
            }
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.StartsWith("[", StringComparison.Ordinal))
            {
                SetStatusT("entry.status.bad_name");
                return;
            }
            pendingEntryName = name;
            SetTool(EditorTool.Drag);
            StartPlacement(OmsiNewMap.EntryPointObject);
            if (placingEntryName != null)
                SetStatusT("entry.status.placing", ("name", name));
        }

        /// <summary>A click while an entry point is carried: the object and its line in global.cfg, as one undo step.</summary>
        private void PlaceEntryPoint(MapPlacement placement)
        {
            var name = placingEntryName;
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
        /// <c>-entryDelete objectID</c> change the one standing on that object; "Entry point:" in the log. ADR 079.
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
                placingEntryName = args[add + 1];
                mapCanvas.BeginPlacement(OmsiNewMap.EntryPointObject, heading);
                PlaceEntryPoint(new MapPlacement { Tile = tile, LocalX = x, LocalY = y });
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
    }
}
