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
    /// inspector at the group in question, so the inspector no longer stands on the map all the time. A junction the editor made also
    /// opens the markings, traffic rules, lanes and traffic lights tools on itself, a signal head the lights of its crossing, and a row
    /// of objects along a road is edited or deleted (M5). The keys Up, Down, Enter and Space move over the entries and choose one.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private VisualElement contextMenuOverlay;
        private bool inspectorShown;

        /// <summary>The entry the keyboard has reached, counted over the entries of the open menu; -1 before the first arrow key.</summary>
        private int contextMenuFocus = -1;

        private bool ContextMenuOpen => contextMenuOverlay != null;

        /// <summary>The frame of the last key the open menu took (an arrow, Enter, Space or Esc); -10 before any.</summary>
        private int contextMenuKeyFrame = -10;

        /// <summary>
        /// UI Toolkit makes a second event of an arrow key, Enter, Space or Esc (<see cref="NavigationMoveEvent"/>, <see cref="NavigationSubmitEvent"/>,
        /// <see cref="NavigationCancelEvent"/>) and sends it, after the key event and whatever that did, to the element with the focus or to
        /// the panel. Stopping the key event does not stop it, so a list or scroll view under the menu (the object browser, the inspector)
        /// scrolled, or a focused button was pressed, with every key the menu took. The menu keeps these events from everything else while it
        /// is open and for the frame after its last key (it is closed by then when the key chose an entry or was Esc).
        /// </summary>
        private bool MenuTakesNavigation => ContextMenuOpen || Time.frameCount - contextMenuKeyFrame <= 1;

        private void SwallowNavigationForMenu(EventBase evt)
        {
            if (!MenuTakesNavigation || InTextInput(evt.target as VisualElement)) // in a field the keys are typing (see OnShortcut)
                return;
            if (evt is NavigationMoveEvent move && (move.direction == NavigationMoveEvent.Direction.Next || move.direction == NavigationMoveEvent.Direction.Previous))
                return; // Tab closes the menu and goes on as always
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void RegisterContextMenuNavigation(VisualElement root)
        {
            root.RegisterCallback<NavigationMoveEvent>(SwallowNavigationForMenu, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(SwallowNavigationForMenu, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(SwallowNavigationForMenu, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Whether the menu offers "Paths": always, with a map open. Choosing it switches the paths layer on and, while the paths are still
        /// being read, opens the group once they are (<see cref="openPathsGroupWhenRead"/>).
        /// </summary>
        private bool PathsGroupShown => currentSummary != null;

        private bool openPathsGroupWhenRead;

        /// <summary>How long finding out what an object's menu offers took the last time (diagnostics).</summary>
        private double lastObjectMenuStateMilliseconds;

        /// <summary>"Paths" of the context menu: the paths layer on, the inspector open at the group of the selection's paths.</summary>
        private void OpenPathsFromMenu()
        {
            if (!pathsShown)
                ShowPaths(true);
            if (mapPaths == null)
            {
                // The paths are read on a worker thread; the inspector shows the selection meanwhile and goes to the group when they arrive.
                openPathsGroupWhenRead = true;
                OpenInspector(InspectorTab.Selection);
                return;
            }
            openPathsGroupWhenRead = false;
            OpenInspector(InspectorTab.Selection, "paths");
        }

        /// <summary>Shows or hides the inspector; it is drawn for the selection either way.</summary>
        private void ShowInspector(bool show, bool draw = true)
        {
            inspectorShown = show;
            if (!show)
                openPathsGroupWhenRead = false;
            if (inspectorFrame != null)
                inspectorFrame.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                // Above the tool windows: hanging at a row or object under the object window or the tool options, it would stay hidden behind them.
                inspectorFrame?.BringToFront();
                UpdateInspectorPlace(); // beside the element at once; the layout and the tick correct it
            }
            // Drawn for what was selected while it was hidden (RenderInspector leaves a hidden inspector empty).
            if (show && draw && inspectorStale)
                RenderInspector();
        }

        /// <summary>The inspector was not drawn since the selection changed because it was hidden.</summary>
        private bool inspectorStale;

        /// <summary>
        /// Opens the inspector on a tab and, if given, unfolds a group and scrolls to it, such as "bus-stop" for the name of a bus stop.
        /// </summary>
        private void OpenInspector(InspectorTab? tab = null, string groupId = null)
        {
            ShowInspector(true, false);
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
            // The compact window keeps the group open when it is drawn again (the full view keeps its folds as the owner left them).
            if (!inspectorFull)
                compactGroupChoice[groupId] = true;
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
            contextMenuFocus = -1;
        }

        /// <summary>Selects what was right-clicked and shows the menu for it at the pointer.</summary>
        private void ShowContextMenu(MapContextHit hit)
        {
            CloseContextMenu();
            if (workspaceElement == null || currentSummary == null || hit?.Tile == null)
                return;
            var phases = System.Diagnostics.Stopwatch.StartNew();
            var startTime = Time.realtimeSinceStartupAsDouble;
            var startFrame = Time.frameCount;

            // A row of objects along a road lies under an object-free pointer first, then the spline; only where rows can be edited.
            if (hit.Object == null && !hit.Row.HasValue && CanEdit)
                hit.Row = RowUnderContext(hit);
            var onRow = hit.Object == null && hit.Row.HasValue;
            var rowMilliseconds = phases.Elapsed.TotalMilliseconds;
            phases.Restart();

            // The element under the pointer becomes the selection, unless it is one of several selected objects: the menu then acts on all of them.
            if (hit.Object != null)
            {
                if (!(selection.Count > 1 && IsSelected(hit.Object)))
                    SelectObject(hit.Tile, hit.Object, false);
                // A click that a pick took (variable parent) did not select: no menu, or it would act on the selection before.
                if (!IsSelected(hit.Object))
                    return;
            }
            else if (onRow)
            {
                // A row is no selection of the map: the menu acts on the row itself, and the selection stays as it was.
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

            var selectMilliseconds = phases.Elapsed.TotalMilliseconds;
            phases.Restart();
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
            else if (onRow)
                FillRowMenu(menu, hit.Row.Value, OmsiMapGeometry.TileLocalToWorld(hit.Tile.X, hit.Tile.Y, hit.LocalX, hit.LocalY, currentSummary.TileSizeMeters));
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
            var fillMilliseconds = phases.Elapsed.TotalMilliseconds;
            var syncMilliseconds = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
            var laidOut = false;
            menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!laidOut && mapCanvas != null)
                {
                    laidOut = true;
                    var culture = System.Globalization.CultureInfo.InvariantCulture;
                    Debug.Log("Context menu timing: pick " + mapCanvas.LastHitMilliseconds.ToString("0.0", culture) + " ms (ground " +
                              mapCanvas.LastGroundMilliseconds.ToString("0.0", culture) + ", objects " + mapCanvas.LastObjectPickMilliseconds.ToString("0.0", culture) +
                              ", splines " + mapCanvas.LastSplinePickMilliseconds.ToString("0.0", culture) + "), rows " + rowMilliseconds.ToString("0.0", culture) +
                              " ms, select " + selectMilliseconds.ToString("0.0", culture) + " ms (" + lastSelectionTimings + "; spline neighbours " +
                              lastJoinMilliseconds.ToString("0.0", culture) + "), menu " + fillMilliseconds.ToString("0.0", culture) + " ms (object state " + lastObjectMenuStateMilliseconds.ToString("0.0", culture) + ")" +
                              ", shell total " + syncMilliseconds.ToString("0.0", culture) + " ms, until laid out " +
                              ((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0).ToString("0.0", culture) + " ms in " + (Time.frameCount - startFrame) + " frames.");
                    // What the frames after the click cost: the menu appears on the first of them, drawn with the map.
                    var frames = new System.Text.StringBuilder();
                    var counted = 0;
                    var clickTime = startTime;
                    mapCanvas.schedule.Execute(() =>
                    {
                        if (counted < 8)
                            frames.Append(' ').Append((Time.unscaledDeltaTime * 1000f).ToString("0", culture));
                        if (++counted == 8)
                            Debug.Log("Context menu frames after the click (ms):" + frames + "; " + ((Time.realtimeSinceStartupAsDouble - clickTime) * 1000.0).ToString("0", culture) + " ms in all.");
                    }).Every(0).Until(() => counted >= 8);
                }
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
                    case ContextMenuAction.RandomizeHeadings: return RandomizeSelectedHeadings;
                    default: return DeleteSelectedObjects;
                }
            });
        }

        private void FillObjectMenu(VisualElement menu, OmsiTileSummary tile, OmsiObjectRecord item)
        {
            AddContextHeader(menu, Loc.T("context.object", ("id", item.Id), ("name", Path.GetFileName(item.AssetPath ?? string.Empty))));
            var stateWatch = System.Diagnostics.Stopwatch.StartNew();
            var holdingTile = TileHolding(new MapSelectionItem { Tile = tile, Record = item }) ?? tile;
            var entryPoint = EditingVariant ? null : session?.EntryPointOf(currentSummary, holdingTile, item);
            var scenery = PrioritySceneryObject(item.AssetPath);
            OmsiTileSummary crossingTile = null;
            OmsiObjectRecord crossing = null;
            var headOfMadeCrossing = scenery != null && scenery.IsTrafficLight && FindCrossingOfHead(holdingTile, item, out crossingTile, out crossing) &&
                                     MadeJunction(crossing.AssetPath) != null;
            var state = new ContextMenuState
            {
                CanEdit = CanEdit,
                CanEditObjects = CanEditObjects,
                PathsGroupShown = PathsGroupShown,
                IsEntryPoint = entryPoint != null,
                IsBusStop = !EditingVariant && session != null && session.IsBusStop(currentSummary, holdingTile, item),
                IsMadeJunction = MadeJunction(item.AssetPath) != null,
                HasPriorityArms = scenery != null && OmsiJunctionPriorities.Arms(scenery, item.Rules).Count > 0,
                // The traffic lights tool takes an object with two arms or more (TargetSignalJunction).
                HasSignalArms = scenery != null && OmsiJunctionPriorities.Arms(scenery, null).Count >= 2,
                IsHeadOfMadeCrossing = headOfMadeCrossing,
            };
            lastObjectMenuStateMilliseconds = stateWatch.Elapsed.TotalMilliseconds;
            AddContextEntries(menu, ContextMenuEntries.For(ContextMenuKind.Object, state), action =>
            {
                switch (action)
                {
                    case ContextMenuAction.OpenProperties: return () => OpenInspector(InspectorTab.Selection);
                    case ContextMenuAction.RenameEntryPoint: return () => OpenInspector(InspectorTab.Selection, "object-entry");
                    case ContextMenuAction.RenameBusStop: return () => OpenInspector(InspectorTab.Selection, "bus-stop");
                    case ContextMenuAction.ChangeType: return () => PickElementType(holdingTile, item);
                    case ContextMenuAction.EditLabels: return () => OpenInspector(InspectorTab.Selection, "object-type");
                    case ContextMenuAction.OpenPaths: return OpenPathsFromMenu;
                    // The junction is the selection; each tool begins on the one selected object (the same as choosing its button).
                    case ContextMenuAction.OpenMarkings: return () => SetTool(EditorTool.Markings);
                    case ContextMenuAction.OpenTrafficRules: return () => SetTool(EditorTool.Priorities);
                    case ContextMenuAction.OpenLanes: return () => SetTool(EditorTool.Lanes);
                    case ContextMenuAction.OpenTrafficLights: return () => SetTool(EditorTool.Signals);
                    case ContextMenuAction.OpenTrafficLightsOfHead: return () => OpenTrafficLightsOf(crossingTile, crossing);
                    case ContextMenuAction.Copy: return () => CopySelection();
                    case ContextMenuAction.Duplicate: return DuplicateSelection;
                    case ContextMenuAction.LevelGround: return LevelSelectedGround;
                    case ContextMenuAction.DeleteEntryPoint: return () => DeleteEntryPoint(entryPoint);
                    default: return DeleteSelectedObjects;
                }
            });
        }

        /// <summary>
        /// The crossing a signal head shows, by its [varparent]: an object of the head's tile or the eight around it (a [varparent] reaches no
        /// further, ADR 012). False for a head that names none or none that stands there.
        /// </summary>
        private bool FindCrossingOfHead(OmsiTileSummary headTile, OmsiObjectRecord head, out OmsiTileSummary tile, out OmsiObjectRecord crossing)
        {
            tile = null;
            crossing = null;
            if (!head.VarParentId.HasValue || currentSummary == null)
                return false;
            var crossingId = head.VarParentId.Value;
            foreach (var candidate in currentSummary.Tiles)
            {
                if (Math.Abs(candidate.X - headTile.X) > 1 || Math.Abs(candidate.Y - headTile.Y) > 1)
                    continue;
                var found = candidate.Objects.Find(item => item.Id == crossingId);
                if (found == null)
                    continue;
                tile = candidate;
                crossing = found;
                return true;
            }
            return false;
        }

        /// <summary>Selects the crossing of a signal head and begins the traffic lights tool on it, as choosing the tool with the crossing selected does.</summary>
        private void OpenTrafficLightsOf(OmsiTileSummary tile, OmsiObjectRecord crossing)
        {
            if (tile == null || crossing == null || !tile.Objects.Contains(crossing))
                return;
            SelectObject(tile, crossing, false);
            SetTool(EditorTool.Signals);
        }

        /// <summary>
        /// The row of objects along a road with an object at the pointer, the nearest of the tile and the eight around it; null when none.
        /// The rows are laid out anew: the map may have changed since the rows tool last ran.
        /// </summary>
        private KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>? RowUnderContext(MapContextHit hit)
        {
            if (currentSummary == null || mapCanvas == null || hit?.Tile == null)
                return null;
            var size = currentSummary.TileSizeMeters;
            var at = OmsiMapGeometry.TileLocalToWorld(hit.Tile.X, hit.Tile.Y, hit.LocalX, hit.LocalY, size);
            rowLayout = new OmsiSplineAttachmentLayout(currentSummary);
            rowObjects = null;
            var reach = mapCanvas.ThreeDimensional ? 1.5 : Math.Max(1.5, 8.0 * mapCanvas.MetersPerPixel);
            KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>? best = null;
            var bestDistance = double.MaxValue;
            foreach (var tile in currentSummary.Tiles)
            {
                if (Math.Abs(tile.X - hit.Tile.X) > 1 || Math.Abs(tile.Y - hit.Tile.Y) > 1)
                    continue;
                foreach (var record in tile.SplineAttachments)
                {
                    if (record.IsRepeater)
                        continue;
                    foreach (var placed in rowLayout.Place(tile, record))
                    {
                        var dx = Math.Abs(placed.WorldX - at.X);
                        var dy = Math.Abs(placed.WorldY - at.Y);
                        if (dx > reach || dy > reach)
                            continue;
                        var distance = dx * dx + dy * dy;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(tile, record);
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// How far the cross-section of a spline type reaches (left edge, right edge, height of its surface over the spline), for pointing at
        /// splines in 3D; null for a type without a surface or one that cannot be read.
        /// </summary>
        private Vector3? SplineExtentOf(string assetPath)
        {
            if (currentSummary == null || string.IsNullOrEmpty(assetPath))
                return null;
            var profile = RoadProfiles().Get(assetPath);
            if (profile == null || profile.EditorOnly || profile.Strips.Count == 0)
                return null;
            var lift = profile.LineKind == OmsiSplineLineKind.Overhead ? OmsiSplineProfile.OverheadHeight : profile.EdgeHeight;
            return new Vector3((float)profile.LeftEdge, (float)profile.RightEdge, (float)lift);
        }

        private void FillRowMenu(VisualElement menu, KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord> row, OmsiMapPoint clickedAt)
        {
            AddContextHeader(menu, Loc.T("context.row", ("id", row.Value.Id), ("name", Path.GetFileName(row.Value.AssetPath ?? string.Empty))));
            var state = new ContextMenuState { CanEdit = CanEdit };
            AddContextEntries(menu, ContextMenuEntries.For(ContextMenuKind.Row, state), action =>
            {
                if (action == ContextMenuAction.EditRow)
                    return () => EditRowFromMenu(row, clickedAt);
                return () => DeleteRowFromMenu(row);
            });
        }

        /// <summary>"Edit row": the rows tool with the row selected, as a click on it in the tool does.</summary>
        private void EditRowFromMenu(KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord> row, OmsiMapPoint? clickedAt = null)
        {
            SetTool(EditorTool.Rows);
            if (activeTool != EditorTool.Rows)
                return;
            SelectRow(row, clickedAt);
            UpdateRowPreview();
            if (selectedRow != null)
                SetStatusT("rows.status.selected", ("path", row.Value.AssetPath), ("id", row.Value.Id));
        }

        /// <summary>
        /// Delete with the pointer over a row of objects along a road, in the tools that work on the map's elements (Drag, Move, Rotate, Rows):
        /// the same way as "Delete row", one undo step. It comes after the selection: selected objects or a selected spline are deleted first,
        /// and an object under the pointer is not a row. Returns whether it took the key.
        /// </summary>
        private bool DeleteRowUnderPointer()
        {
            if (!CanEdit || mapCanvas == null || currentSummary == null || session == null)
                return false;
            if (activeTool != EditorTool.Drag && activeTool != EditorTool.Move && activeTool != EditorTool.Rotate && activeTool != EditorTool.Rows)
                return false;
            if (mapCanvas.IsPlacing || !mapCanvas.TryPointerHit(out var hit) || hit.Object != null)
                return false;
            var row = RowUnderContext(hit);
            if (!row.HasValue)
                return false;
            DeleteRowFromMenu(row.Value);
            return true;
        }

        /// <summary>"Delete row": the command of Delete in the rows tool, on the row of the menu.</summary>
        private void DeleteRowFromMenu(KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord> row)
        {
            rowLayout = null;
            selectedRow = RowOf(row);
            if (selectedRow == null)
            {
                SetStatusT("rows.status.no_objects", ("path", row.Value.AssetPath), ("id", row.Value.Id));
                return;
            }
            DeleteSelectedRow();
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
                    case ContextMenuAction.OpenPaths: return OpenPathsFromMenu;
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

        private List<Button> ContextMenuItems()
        {
            return contextMenuOverlay == null ? new List<Button>() : contextMenuOverlay.Query<Button>(className: "context-menu-item").ToList();
        }

        /// <summary>Puts the keyboard focus on an entry (or none with -1); it is shown by a class, as the keys stay with the shell.</summary>
        private void SetContextMenuFocus(List<Button> items, int index)
        {
            for (var position = 0; position < items.Count; position++)
                items[position].EnableInClassList("context-menu-item-focused", position == index);
            contextMenuFocus = index;
        }

        /// <summary>Moves the keyboard focus to the next active entry up or down; dividers, notes and disabled entries are skipped, and it wraps round.</summary>
        private void MoveContextMenuFocus(int direction)
        {
            var items = ContextMenuItems();
            var active = items.ConvertAll(item => item.enabledSelf && item.resolvedStyle.display != DisplayStyle.None);
            SetContextMenuFocus(items, ContextMenuNavigation.Next(active, contextMenuFocus, direction));
        }

        /// <summary>
        /// The keys of the open menu: Up and Down move the focus, Enter and Space choose the entry it is on. Returns whether the key was one of
        /// them; the others go on (and close the menu, see <see cref="KeepsContextMenu"/>).
        /// </summary>
        private bool HandleContextMenuKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter ||
                evt.keyCode == KeyCode.Space)
                contextMenuKeyFrame = Time.frameCount;
            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveContextMenuFocus(-1);
                    return true;
                case KeyCode.DownArrow:
                    MoveContextMenuFocus(1);
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    var items = ContextMenuItems();
                    if (contextMenuFocus >= 0 && contextMenuFocus < items.Count && items[contextMenuFocus].enabledSelf)
                        (items[contextMenuFocus].userData as Action)?.Invoke();
                    return true;
                default:
                    return false;
            }
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
                        Button item;
                        if (entry.TextKey == "context.delete_many")
                            item = AddContextItem(menu, new Label(Loc.T("context.delete_many", ("count", selection.Count))), actionOf(entry.Action), entry.Shortcut, entry.Danger);
                        else
                            item = AddContextItem(menu, entry.TextKey, actionOf(entry.Action), entry.Shortcut, entry.Danger);
                        if (entry.DisabledReasonKey != null)
                        {
                            // Greyed, with the reason as a small line under it (a tooltip on a disabled button is not reliable).
                            item.SetEnabled(false);
                            var reason = LocBindings.NewLabel(entry.DisabledReasonKey); // i18n:keys
                            reason.AddToClassList("context-menu-reason");
                            menu.Add(reason);
                        }
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
        /// Command line, for screenshots: <c>-inspector</c> opens the inspector, <c>-contextMenu target</c> opens the context menu of the
        /// target where it stands in the view from above, the way a right-click does: an object ID (also a junction or a signal head),
        /// <c>spline:ID</c> (at its middle), <c>tile:X,Y</c> (at its centre, empty ground) or <c>row:ID</c> (the row of objects along a road,
        /// by the ID of its first record, at its first object). <c>-contextFocus n</c> then moves the keyboard focus down n entries, and
        /// <c>-contextChoose Text</c> clicks the entry whose text starts so (such as "Rename bus").
        /// </summary>
        private void RunContextMenuArguments(string[] args)
        {
            if (Array.IndexOf(args, "-inspector") >= 0)
                ShowInspector(true);
            var index = Array.IndexOf(args, "-contextMenu");
            var pixelIndex = Array.IndexOf(args, "-contextPixel");
            if (currentSummary == null || mapCanvas == null || index < 0 && pixelIndex < 0)
                return;
            string target;
            MapContextHit hit;
            if (mapCanvas.ThreeDimensional)
            {
                // In 3D the place of the target on the screen is found by projecting it and the menu opens for what the pointer ray finds there.
                if (!TryContextHitIn3D(args, index, pixelIndex, out hit, out target))
                    return;
            }
            else
            {
                if (index < 0 || index + 1 >= args.Length)
                    return;
                target = args[index + 1];
                if (!TryContextHitOfArgument(target, out hit))
                    return;
                var world = OmsiMapGeometry.TileLocalToWorld(hit.Tile.X, hit.Tile.Y, hit.LocalX, hit.LocalY, currentSummary.TileSizeMeters);
                if (!mapCanvas.TryScreenPosition(world, out var screen))
                {
                    Debug.LogWarning("Context menu: " + target + " has no place on the screen.");
                    return;
                }
                hit.ScreenPosition = screen;
            }
            ShowContextMenu(hit);
            // -contextRepeat opens the same menu a second time, which shows what a right-click costs once everything it reads is known.
            if (Array.IndexOf(args, "-contextRepeat") >= 0)
                ShowContextMenu(hit);
            Debug.Log("Context menu: shown for " + target + (ContextMenuOpen ? "." : " was refused."));
            var focusIndex = Array.IndexOf(args, "-contextFocus");
            if (focusIndex >= 0 && focusIndex + 1 < args.Length &&
                int.TryParse(args[focusIndex + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var steps))
            {
                for (var step = 0; step < steps && ContextMenuOpen; step++)
                    MoveContextMenuFocus(1);
            }
            ChooseContextEntry(args);
        }

        /// <summary>
        /// The hit a right-click in the 3D view gives, for <c>-contextPixel x y</c> (a panel position) or for <c>-contextMenu target</c> at the place
        /// where the target is drawn. The pick is timed (the first, then the average of 100) and what it found is logged next to what was expected.
        /// </summary>
        private bool TryContextHitIn3D(string[] args, int menuIndex, int pixelIndex, out MapContextHit hit, out string target)
        {
            hit = null;
            target = string.Empty;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var floats = System.Globalization.NumberStyles.Float;
            Vector2 screen;
            MapContextHit expected = null;
            if (pixelIndex >= 0 && pixelIndex + 2 < args.Length && float.TryParse(args[pixelIndex + 1], floats, culture, out var pixelX) &&
                float.TryParse(args[pixelIndex + 2], floats, culture, out var pixelY))
            {
                screen = new Vector2(pixelX, pixelY);
                target = "pixel " + args[pixelIndex + 1] + " " + args[pixelIndex + 2];
            }
            else
            {
                if (menuIndex < 0 || menuIndex + 1 >= args.Length)
                    return false;
                target = args[menuIndex + 1];
                if (!TryContextHitOfArgument(target, out expected))
                    return false;
                var map = OmsiMapGeometry.TileLocalToWorld(expected.Tile.X, expected.Tile.Y, expected.LocalX, expected.LocalY, currentSummary.TileSizeMeters);
                var height = expected.Spline != null
                    ? expected.Spline.Height + (expected.Spline.EndHeight - expected.Spline.Height) / 2.0 + 0.1
                    : (double)mapCanvas.GroundAtWorld(map) +(expected.Object != null ? expected.Object.Height : 0.0);
                if (!mapCanvas.TryScreenPosition3D(expected.Tile, expected.LocalX, expected.LocalY, height, out screen))
                {
                    Debug.LogWarning("Context menu: " + target + " has no place on the screen in 3D.");
                    return false;
                }
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            hit = mapCanvas.ContextHitAtPanel(screen);
            var first = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            for (var repeat = 0; repeat < 100; repeat++)
                mapCanvas.ContextHitAtPanel(screen);
            var average = watch.Elapsed.TotalMilliseconds / 100.0;
            var found = hit == null ? "nothing"
                : hit.Object != null ? "object " + hit.Object.Id
                : hit.Spline != null ? "spline " + hit.Spline.Id
                : "tile " + hit.Tile.X + "," + hit.Tile.Y;
            Debug.Log("Context menu 3D: " + target + " at pixel " + screen.x.ToString("0", culture) + "," + screen.y.ToString("0", culture) + " found " + found +
                      "; first pick " + first.ToString("0.00", culture) + " ms, average of 100 " + average.ToString("0.000", culture) + " ms (spline part " +
                      mapCanvas.LastSplinePickMilliseconds.ToString("0.000", culture) + " ms, ribbons built in " +
                      mapCanvas.LastRibbonBuildMilliseconds.ToString("0.0", culture) + " ms once).");
            return hit != null;
        }

        /// <summary>The hit a right-click on the target of <c>-contextMenu</c> would give (see <see cref="RunContextMenuArguments"/>); false with a warning when it is not in the map.</summary>
        private bool TryContextHitOfArgument(string target, out MapContextHit hit)
        {
            hit = null;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var numbers = System.Globalization.NumberStyles.Integer;
            var colon = target.IndexOf(':');
            var kind = colon < 0 ? string.Empty : target.Substring(0, colon).ToLowerInvariant();
            var value = colon < 0 ? target : target.Substring(colon + 1);
            var size = currentSummary.TileSizeMeters;
            if (kind == "tile")
            {
                var parts = value.Split(',');
                if (parts.Length < 2 || !int.TryParse(parts[0], numbers, culture, out var x) || !int.TryParse(parts[1], numbers, culture, out var y))
                {
                    Debug.LogWarning("Context menu: tile:X,Y expected, got " + target + ".");
                    return false;
                }
                var found = currentSummary.Tiles.Find(candidate => candidate.X == x && candidate.Y == y);
                if (found == null)
                {
                    Debug.LogWarning("Context menu: tile " + x + ", " + y + " is not in the map.");
                    return false;
                }
                hit = new MapContextHit { Tile = found, LocalX = size / 2.0, LocalY = size / 2.0 };
                return true;
            }
            if (!int.TryParse(value, numbers, culture, out var id))
            {
                Debug.LogWarning("Context menu: no ID in " + target + ".");
                return false;
            }
            foreach (var tile in currentSummary.Tiles)
            {
                if (kind == "spline")
                {
                    var spline = tile.Splines.Find(candidate => candidate.Id == id);
                    if (spline == null)
                        continue;
                    OmsiMapGeometry.GetLocalFrameAt(spline, spline.LengthMeters / 2.0, out var local, out _);
                    hit = new MapContextHit { Tile = tile, Spline = spline, LocalX = local.X, LocalY = local.Y };
                    return true;
                }
                if (kind == "row")
                {
                    var record = tile.SplineAttachments.Find(candidate => candidate.Id == id && !candidate.IsRepeater);
                    if (record == null)
                        continue;
                    var pair = new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(tile, record);
                    var placed = Placed(pair);
                    if (placed.Count == 0)
                    {
                        Debug.LogWarning("Context menu: row " + id + " has no objects along its road.");
                        return false;
                    }
                    var origin = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, 0.0, 0.0, size);
                    hit = new MapContextHit { Tile = tile, Row = pair, LocalX = placed[0].WorldX - origin.X, LocalY = placed[0].WorldY - origin.Y };
                    return true;
                }
                var item = tile.Objects.Find(candidate => candidate.Id == id);
                if (item != null)
                {
                    hit = new MapContextHit { Tile = tile, Object = item, LocalX = item.LocalX, LocalY = item.LocalY };
                    return true;
                }
            }
            Debug.LogWarning("Context menu: " + target + " is not in the map.");
            return false;
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
