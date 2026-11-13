using System;
using System.Collections.Generic;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The context menu of the map and the inspector on demand (ADR 098). A right-click selects what is under the pointer and lists what
    /// can be done with it: an object offers its properties, the name of a bus stop or entry point, its type and labels, copying and
    /// deleting; a spline its heights and deleting; empty ground the tile, pasting and reverting. The entries that edit open the
    /// inspector at the group in question, so the inspector no longer stands on the map all the time.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement contextMenuOverlay;
        private bool inspectorShown;

        private bool ContextMenuOpen => contextMenuOverlay != null;

        /// <summary>Whether the inspector has the group of paths that "AI paths and rules..." opens: only once the paths are read.</summary>
        private bool PathsGroupShown => pathsShown && mapPaths != null;

        /// <summary>Shows or hides the inspector; it is drawn for the selection either way.</summary>
        private void ShowInspector(bool show)
        {
            inspectorShown = show;
            if (inspectorFrame != null)
                inspectorFrame.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Opens the inspector on a tab and, if given, unfolds a group and scrolls to it, such as "bus-stop" for the name of a bus stop.
        /// </summary>
        private void OpenInspector(InspectorTab? tab = null, string groupId = null)
        {
            ShowInspector(true);
            if (tab.HasValue)
            {
                inspectorTab = tab.Value;
                inspectorTabOwner = InspectorSelectionIdentity();
            }
            RenderInspector();
            if (groupId != null)
                ScrollInspectorToGroup(groupId);
        }

        private void ScrollInspectorToGroup(string groupId)
        {
            var group = inspectorPanel?.Q<VisualElement>("group-" + groupId);
            var records = inspectorPanel?.Q<ScrollView>(className: "inspector-records");
            if (group == null || records == null)
                return;
            (group.Q(className: "inspector-group-header")?.userData as Action<bool>)?.Invoke(true);
            // The group has its place only after the next layout, and a frame that was hidden lays out late; the later run wins over the
            // scroll position RenderInspector keeps for the same selection.
            void Scroll()
            {
                records.scrollOffset = new Vector2(0f, Mathf.Max(0f, group.worldBound.y - records.contentContainer.worldBound.y));
            }
            void OnLaidOut(GeometryChangedEvent _)
            {
                records.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnLaidOut);
                records.schedule.Execute(Scroll);
            }
            records.contentContainer.RegisterCallback<GeometryChangedEvent>(OnLaidOut);
            records.schedule.Execute(() =>
            {
                records.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnLaidOut);
                Scroll();
                group.Q<TextField>()?.Focus();
            }).StartingIn(250);
        }

        private void CloseContextMenu()
        {
            if (contextMenuOverlay == null)
                return;
            contextMenuOverlay.RemoveFromHierarchy();
            contextMenuOverlay = null;
        }

        /// <summary>Selects what was right-clicked and shows the menu for it at the pointer.</summary>
        private void ShowContextMenu(MapContextHit hit)
        {
            CloseContextMenu();
            if (workspaceElement == null || currentSummary == null || hit?.Tile == null)
                return;

            // The element under the pointer becomes the selection, unless it is one of several selected objects: the menu then acts on all of them.
            if (hit.Object != null)
            {
                if (!(selection.Count > 1 && IsSelected(hit.Object)))
                    SelectObject(hit.Tile, hit.Object, false);
                // A click that a pick took (variable parent) did not select: no menu, or it would act on the selection before.
                if (!IsSelected(hit.Object))
                    return;
            }
            else if (hit.Spline != null)
            {
                SelectSpline(hit.Tile, hit.Spline);
                if (!ReferenceEquals(selectedSpline, hit.Spline))
                    return;
            }
            else
            {
                SelectTile(hit.Tile, false);
                if (!ReferenceEquals(selectedTile, hit.Tile))
                    return;
            }

            var overlay = new VisualElement();
            overlay.AddToClassList("context-menu-overlay");
            overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                CloseContextMenu();
                evt.StopPropagation();
            });
            var menu = new VisualElement();
            menu.AddToClassList("context-menu");
            menu.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

            if (hit.Object != null && selection.Count > 1)
                FillSelectionMenu(menu);
            else if (hit.Object != null)
                FillObjectMenu(menu, hit.Tile, hit.Object);
            else if (hit.Spline != null)
                FillSplineMenu(menu, hit.Spline);
            else
                FillTileMenu(menu, hit);

            overlay.Add(menu);
            workspaceElement.Add(overlay);
            contextMenuOverlay = overlay;
            var local = workspaceElement.WorldToLocal(hit.ScreenPosition);
            menu.style.left = local.x;
            menu.style.top = local.y;
            menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                // Once its size is known, the menu stays inside the workspace and above the bottom bar; near the bar it opens upwards.
                var bounds = workspaceElement.layout;
                var height = menu.layout.height;
                var lowest = bounds.height - FreeMapInsets().w - 4f;
                var top = local.y + height > lowest ? local.y - height : local.y;
                menu.style.left = Mathf.Max(0f, Mathf.Min(local.x, bounds.width - menu.layout.width - 4f));
                menu.style.top = Mathf.Max(0f, Mathf.Min(top, lowest - height));
            });
        }

        private void FillSelectionMenu(VisualElement menu)
        {
            AddContextHeader(menu, Loc.T("context.selection", ("count", selection.Count)));
            var state = new ContextMenuState { CanEdit = CanEdit, CanEditObjects = CanEditObjects };
            AddContextEntries(menu, ContextMenuEntries.For(ContextMenuKind.Selection, state), action =>
            {
                switch (action)
                {
                    case ContextMenuAction.OpenProperties: return () => OpenInspector(InspectorTab.Selection);
                    case ContextMenuAction.Copy: return () => CopySelection();
                    case ContextMenuAction.Duplicate: return DuplicateSelection;
                    case ContextMenuAction.LevelGround: return LevelSelectedGround;
                    default: return DeleteSelectedObjects;
                }
            });
        }

        private void FillObjectMenu(VisualElement menu, OmsiTileSummary tile, OmsiObjectRecord item)
        {
            AddContextHeader(menu, Loc.T("context.object", ("id", item.Id), ("name", Path.GetFileName(item.AssetPath ?? string.Empty))));
            var holdingTile = TileHolding(new MapSelectionItem { Tile = tile, Record = item }) ?? tile;
            var entryPoint = EditingVariant ? null : session?.EntryPointOf(currentSummary, holdingTile, item);
            var state = new ContextMenuState
            {
                CanEdit = CanEdit,
                CanEditObjects = CanEditObjects,
                PathsGroupShown = PathsGroupShown,
                IsEntryPoint = entryPoint != null,
                IsBusStop = !EditingVariant && session != null && session.IsBusStop(currentSummary, holdingTile, item),
            };
            AddContextEntries(menu, ContextMenuEntries.For(ContextMenuKind.Object, state), action =>
            {
                switch (action)
                {
                    case ContextMenuAction.OpenProperties: return () => OpenInspector(InspectorTab.Selection);
                    case ContextMenuAction.RenameEntryPoint: return () => OpenInspector(InspectorTab.Selection, "object-entry");
                    case ContextMenuAction.RenameBusStop: return () => OpenInspector(InspectorTab.Selection, "bus-stop");
                    case ContextMenuAction.ChangeType: return () => PickElementType(holdingTile, item);
                    case ContextMenuAction.EditLabels: return () => OpenInspector(InspectorTab.Selection, "object-type");
                    case ContextMenuAction.OpenPaths: return () => OpenInspector(InspectorTab.Selection, "paths");
                    case ContextMenuAction.Copy: return () => CopySelection();
                    case ContextMenuAction.Duplicate: return DuplicateSelection;
                    case ContextMenuAction.LevelGround: return LevelSelectedGround;
                    case ContextMenuAction.DeleteEntryPoint: return () => DeleteEntryPoint(entryPoint);
                    default: return DeleteSelectedObjects;
                }
            });
        }

        private void FillSplineMenu(VisualElement menu, OmsiSplineRecord spline)
        {
            AddContextHeader(menu, Loc.T("context.spline", ("id", spline.Id), ("name", Path.GetFileName(spline.AssetPath ?? string.Empty))));
            var state = new ContextMenuState
            {
                CanEdit = CanEdit,
                CanEditSplinesInVariant = CanEditSplinesInVariant,
                PathsGroupShown = PathsGroupShown,
            };
            AddContextEntries(menu, ContextMenuEntries.For(ContextMenuKind.Spline, state), action =>
            {
                switch (action)
                {
                    case ContextMenuAction.OpenProperties: return () => OpenInspector(InspectorTab.Selection);
                    case ContextMenuAction.EditSplineHeights: return () => OpenInspector(InspectorTab.Selection, "spline-heights");
                    case ContextMenuAction.OpenPaths: return () => OpenInspector(InspectorTab.Selection, "paths");
                    case ContextMenuAction.DeleteSpline: return DeleteSelectedSpline;
                    case ContextMenuAction.ChangeSplineType: return PickSplineType;
                    default: return DeleteSelectedSplineInVariant;
                }
            });
        }

        private void FillTileMenu(VisualElement menu, MapContextHit hit)
        {
            var tile = hit.Tile;
            AddContextHeader(menu, Loc.T("context.tile", ("x", tile.X), ("y", tile.Y)));
            var state = new ContextMenuState
            {
                CanEdit = CanEdit,
                CanEditObjects = CanEditObjects,
                ClipboardFilled = clipboard != null && clipboard.Items.Count > 0,
                TileRevertible = CanEdit && IsTileEdited(tile) && !session.IsTileAdded(tile.FileName),
            };
            AddContextEntries(menu, ContextMenuEntries.For(ContextMenuKind.Tile, state), action =>
            {
                switch (action)
                {
                    case ContextMenuAction.OpenTileProperties: return () => OpenInspector(InspectorTab.Tile);
                    case ContextMenuAction.Paste: return () => PasteHere(new MapPlacement { Tile = tile, LocalX = hit.LocalX, LocalY = hit.LocalY });
                    default: return RevertSelectedTile;
                }
            });
        }

        /// <summary>Adds the entries of <see cref="ContextMenuEntries"/> in order; <paramref name="actionOf"/> maps an entry's action to what it does.</summary>
        private void AddContextEntries(VisualElement menu, List<ContextMenuEntry> entries, Func<ContextMenuAction, Action> actionOf)
        {
            foreach (var entry in entries)
            {
                switch (entry.Type)
                {
                    case ContextMenuEntryType.Divider:
                        AddContextDivider(menu);
                        break;
                    case ContextMenuEntryType.ReadOnlyNote:
                        AddContextNote(menu);
                        break;
                    default:
                        if (entry.TextKey == "context.delete_many")
                            AddContextItem(menu, new Label(Loc.T("context.delete_many", ("count", selection.Count))), actionOf(entry.Action), entry.Shortcut, entry.Danger);
                        else
                            AddContextItem(menu, entry.TextKey, actionOf(entry.Action), entry.Shortcut, entry.Danger);
                        break;
                }
            }
        }

        private static void AddContextHeader(VisualElement menu, string text)
        {
            var header = new Label(text);
            header.AddToClassList("context-menu-header");
            menu.Add(header);
        }

        private static void AddContextDivider(VisualElement menu)
        {
            // A plain element: a Label keeps the height of a line of text even without one.
            var divider = new VisualElement();
            divider.AddToClassList("menu-divider");
            menu.Add(divider);
        }

        private static void AddContextNote(VisualElement menu)
        {
            var note = LocBindings.NewLabel("context.readonly");
            note.AddToClassList("context-menu-header");
            menu.Add(note);
        }

        /// <summary>An entry of the menu: its text from a key, the key of its shortcut at the right, and what it does once the menu is closed.</summary>
        private Button AddContextItem(VisualElement menu, string textKey, Action action, string shortcut = null, bool danger = false)
        {
            return AddContextItem(menu, LocBindings.NewLabel(textKey), action, shortcut, danger); // i18n:keys
        }

        private Button AddContextItem(VisualElement menu, Label text, Action action, string shortcut, bool danger)
        {
            Action chosen = () =>
            {
                CloseContextMenu();
                action();
            };
            var button = new Button(chosen) { userData = chosen };
            button.AddToClassList("menu-popup-item");
            button.AddToClassList("context-menu-item");
            text.AddToClassList("context-menu-text");
            text.EnableInClassList("context-menu-danger", danger);
            text.pickingMode = PickingMode.Ignore;
            button.Add(text);
            if (shortcut != null)
            {
                var key = new Label(shortcut) { pickingMode = PickingMode.Ignore };
                key.AddToClassList("context-menu-key");
                button.Add(key);
            }
            menu.Add(button);
            return button;
        }

        /// <summary>
        /// Command line, for screenshots: <c>-inspector</c> opens the inspector, <c>-contextMenu objectId</c> opens the context menu of that
        /// object where it stands in the view from above, the way a right-click does, and <c>-contextChoose Text</c> then clicks the entry
        /// whose text starts so (such as "Rename bus").
        /// </summary>
        private void RunContextMenuArguments(string[] args)
        {
            if (Array.IndexOf(args, "-inspector") >= 0)
                ShowInspector(true);
            var index = Array.IndexOf(args, "-contextMenu");
            if (index < 0 || index + 1 >= args.Length || currentSummary == null || mapCanvas == null)
                return;
            if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
                return;
            foreach (var tile in currentSummary.Tiles)
            {
                var item = tile.Objects.Find(candidate => candidate.Id == id);
                if (item == null)
                    continue;
                if (!mapCanvas.TryScreenPosition(tile, item, out var screen))
                {
                    Debug.LogWarning("Context menu: object " + id + " has no place on the screen (3D view?).");
                    return;
                }
                ShowContextMenu(new MapContextHit { Tile = tile, Object = item, LocalX = item.LocalX, LocalY = item.LocalY, ScreenPosition = screen });
                Debug.Log("Context menu: shown for object " + id + (ContextMenuOpen ? "." : " was refused."));
                ChooseContextEntry(args);
                return;
            }
            Debug.LogWarning("Context menu: object " + id + " is not in the map.");
        }

        private void ChooseContextEntry(string[] args)
        {
            var index = Array.IndexOf(args, "-contextChoose");
            if (index < 0 || index + 1 >= args.Length || contextMenuOverlay == null)
                return;
            var text = args[index + 1];
            foreach (var button in contextMenuOverlay.Query<Button>(className: "context-menu-item").ToList())
            {
                var label = button.Q<Label>(className: "context-menu-text");
                if (label == null || !label.text.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    continue;
                Debug.Log("Context menu: chose \"" + label.text + "\".");
                (button.userData as Action)?.Invoke();
                return;
            }
            Debug.LogWarning("Context menu: no entry starts with \"" + text + "\".");
        }
    }
}
