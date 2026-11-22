using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The object list of the bottom bar (objects popup, plan Documentation/objects-popup-plan.md): every <c>[object]</c> placed on the map, by
    /// object file, by tile or flat, with a search and a filter by where the file comes from. One click selects the object and shows it
    /// in the map, Ctrl+click adds it to the selection. The list is a virtual <see cref="ListView"/>: only the rows in view exist as
    /// elements, so a map with tens of thousands of objects costs no more than one with a few. The data come from
    /// <see cref="OmsiPlacedObjectIndex"/>, which is built when the popup opens and again after changes (not per key typed), and is dropped when it closes.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The height of a row of the object list in logical pixels; the same value as <c>.objlist-row</c> plus its margin in EditorShell.uss.</summary>
        private const float ObjectRowHeight = 28f;

        private static readonly (OmsiPlacedObjectGrouping Grouping, string Key, string TooltipKey)[] ObjectGroupings =
        {
            (OmsiPlacedObjectGrouping.ByType, "objects.list.group.type", "objects.list.group.type.tooltip"),
            (OmsiPlacedObjectGrouping.ByTile, "objects.list.group.tile", "objects.list.group.tile.tooltip"),
            (OmsiPlacedObjectGrouping.Flat, "objects.list.group.flat", "objects.list.group.flat.tooltip")
        };

        private static readonly (string Key, string TooltipKey)[] ObjectOriginChips =
        {
            ("objects.list.origin.base", "objects.list.origin.base.tooltip"),
            ("objects.list.origin.dlc", "objects.list.origin.dlc.tooltip"),
            ("objects.list.origin.other", "objects.list.origin.other.tooltip")
        };

        /// <summary>The parts of one row of the list, found again when the row is bound to another entry.</summary>
        private sealed class ObjectRowParts
        {
            public VisualElement Card;
            public IconElement Chevron;
            public Label Name;
            public Label Detail;
            public VisualElement Dot;
            public int Index = -1;
        }

        // What the user chose; it survives a redraw and closing the popup, and starts anew with another map.
        private string objectListFilter = string.Empty;
        private OmsiPlacedObjectGrouping objectListGrouping = OmsiPlacedObjectGrouping.ByType;
        private readonly HashSet<string> objectListOpenTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> objectListOpenTiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly bool[] objectListOriginOn = { true, true, true };
        private float objectListScroll;
        private string objectListMapPath;

        // The index and the list exist while the popup shows them.
        private OmsiPlacedObjectIndex objectIndex;
        private OmsiPlacedObjectRows objectListRows;
        private ListView objectListView;
        private Label objectListSummary;
        private Label objectListEmpty;
        private VisualElement objectListChips;
        private bool objectIndexStale;
        private bool objectListRebuildPending;
        private bool objectListActivating;
        private double objectIndexMilliseconds;

        // The -object... arguments of a test run, applied once the list is up.
        private string objectArgOpenGroup;
        private int? objectArgClickId;
        private string objectArgTyped;

        private bool ObjectListOpen => barPopupOpen && projectTab == ProjectTab.Objects && objectListView != null && objectIndex != null && currentSummary != null;

        /// <summary>The number on the objects button: every object in the list, from 10,000 on in thousands (<c>29k</c>).</summary>
        internal static string ObjectBadgeText(int count)
        {
            if (count < 10000)
                return count.ToString(CultureInfo.InvariantCulture);
            return ((count + 500) / 1000).ToString(CultureInfo.InvariantCulture) + "k";
        }

        private void UpdateObjectBadge()
        {
            if (objectCountBadge == null)
                return;
            var count = 0;
            if (currentSummary != null)
                foreach (var tile in currentSummary.Tiles)
                    count += tile.Objects.Count;
            var text = ObjectBadgeText(count);
            if (objectCountBadge.text != text)
                objectCountBadge.text = text;
        }

        /// <summary>The popup page: search, arrangement, origin filter, summary and the list.</summary>
        private VisualElement ObjectsTab()
        {
            ResetObjectListForMap();
            objectListRebuildPending = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            RebuildObjectIndex();
            var tab = new VisualElement();
            tab.AddToClassList("objlist");

            var filter = new TextField { value = objectListFilter };
            filter.AddToClassList("panel-search-field");
            filter.AddToClassList("compact-filter");
            filter.AddToClassList("browser-filter");
            filter.textEdition.placeholder = Loc.T("objects.list.filter");
            filter.tooltip = Loc.T("objects.list.filter.tooltip");
            filter.SetEnabled(objectIndex.ObjectCount > 0);
            tab.Add(filter);

            var arrangement = new VisualElement();
            arrangement.AddToClassList("panel-tabs");
            foreach (var (grouping, key, tooltipKey) in ObjectGroupings)
            {
                var chosen = grouping;
                var button = new Button(() => SetObjectGrouping(chosen)) { text = Loc.T(key) }; // i18n:keys
                button.AddToClassList("panel-tab");
                button.EnableInClassList("panel-tab-active", grouping == objectListGrouping);
                LocBindings.TitledTooltip(button, key, tooltipKey); // i18n:keys
                arrangement.Add(button);
            }
            tab.Add(arrangement);

            objectListChips = new VisualElement();
            objectListChips.AddToClassList("objlist-chips");
            for (var i = 0; i < ObjectOriginChips.Length; i++)
            {
                var chip = i;
                var button = new Button { text = Loc.T(ObjectOriginChips[i].Key) }; // i18n:keys
                button.clicked += () => ToggleObjectOrigin(chip, button);
                button.AddToClassList("objlist-chip");
                button.EnableInClassList("objlist-chip-on", objectListOriginOn[i]);
                LocBindings.TitledTooltip(button, ObjectOriginChips[i].Key, ObjectOriginChips[i].TooltipKey); // i18n:keys
                objectListChips.Add(button);
            }
            // The origin is known only after the installation was read; until then the chips stay away.
            objectListChips.style.display = objectIndex.HasOrigins && objectIndex.ObjectCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            tab.Add(objectListChips);

            objectListSummary = new Label();
            objectListSummary.AddToClassList("objlist-summary");
            tab.Add(objectListSummary);

            objectListView = NewObjectListView();
            tab.Add(objectListView);
            objectListEmpty = new Label();
            objectListEmpty.AddToClassList("muted");
            objectListEmpty.style.display = DisplayStyle.None;
            tab.Add(objectListEmpty);

            filter.RegisterValueChangedCallback(evt =>
            {
                objectListFilter = evt.newValue ?? string.Empty;
                RebuildObjectRows(false, "filter");
            });
            // Down from the search field goes into the list.
            filter.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.DownArrow || objectListRows == null || objectListRows.Rows.Count == 0)
                    return;
                objectListView.Focus();
                objectListView.SetSelectionWithoutNotify(new[] { Math.Max(0, objectListView.selectedIndex) });
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            var indexMs = objectIndexMilliseconds;
            RebuildObjectRows(true, "open");
            Debug.Log("Object list: opened in " + watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms (index " + indexMs.ToString("0.0", CultureInfo.InvariantCulture) +
                      " ms for " + objectIndex.ObjectCount + " objects in " + objectIndex.Types.Count + " types and " + objectIndex.Tiles.Count + " tiles, origins " +
                      (objectIndex.HasOrigins ? "known" : "not yet known") + ").");
            return tab;
        }

        private ListView NewObjectListView()
        {
            var view = new ListView
            {
                fixedItemHeight = ObjectRowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                showBorder = false,
                reorderable = false,
                makeItem = MakeObjectRow,
                bindItem = BindObjectRow
            };
            view.AddToClassList("objlist-list");
            view.RegisterCallback<KeyDownEvent>(OnObjectListKey, TrickleDown.TrickleDown);
            view.Q<ScrollView>().verticalScroller.valueChanged += value =>
            {
                if (Time.realtimeSinceStartup >= objectListScrollHoldUntil)
                    objectListScroll = value;
            };
            return view;
        }

        /// <summary>
        /// The element a row of the list is: the list makes it the item itself (a fixed 28 px high, with Unity's own item classes), so it only
        /// carries the click and holds the card, which has the look of the row with the gap above and below it.
        /// </summary>
        private VisualElement MakeObjectRow()
        {
            var row = new VisualElement();
            row.AddToClassList("objlist-row");
            var parts = new ObjectRowParts
            {
                Card = new VisualElement { pickingMode = PickingMode.Ignore }, Chevron = new IconElement("chevron-right"), Name = new Label(), Detail = new Label(),
                Dot = new VisualElement { pickingMode = PickingMode.Ignore }
            };
            parts.Card.AddToClassList("objlist-card");
            parts.Chevron.AddToClassList("objlist-chevron");
            parts.Name.AddToClassList("objlist-name");
            parts.Name.pickingMode = PickingMode.Ignore;
            parts.Detail.AddToClassList("objlist-detail");
            parts.Detail.pickingMode = PickingMode.Ignore;
            parts.Dot.AddToClassList("objlist-dot");
            parts.Card.Add(parts.Chevron);
            parts.Card.Add(parts.Name);
            parts.Card.Add(parts.Detail);
            parts.Card.Add(parts.Dot);
            row.Add(parts.Card);
            row.userData = parts;
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (parts.Index >= 0)
                    ActivateObjectRow(parts.Index, evt.ctrlKey || evt.commandKey);
            });
            return row;
        }

        private void BindObjectRow(VisualElement element, int index)
        {
            var parts = (ObjectRowParts)element.userData;
            var rows = objectListRows;
            if (rows == null || index < 0 || index >= rows.Rows.Count)
            {
                parts.Index = -1;
                return;
            }
            parts.Index = index;
            var row = rows.Rows[index];
            var culture = CultureInfo.InvariantCulture;
            var isGroup = row.Kind == OmsiPlacedObjectRowKind.Group;
            var typeIndex = -1;
            var selected = false;
            if (isGroup)
            {
                var byType = objectListGrouping == OmsiPlacedObjectGrouping.ByType;
                typeIndex = byType ? row.Group : -1;
                var type = byType ? objectIndex.Types[row.Group] : null;
                parts.Name.text = byType ? type.FileName : Loc.T("objects.list.tile", ("x", objectIndex.Tiles[row.Group].X), ("y", objectIndex.Tiles[row.Group].Y));
                parts.Detail.text = Loc.T("objects.list.count", ("count", row.Count));
                element.tooltip = byType ? type.AssetPath : string.Empty;
                parts.Chevron.SetIcon(row.Expanded ? "chevron-down" : "chevron-right");
            }
            else
            {
                var entry = objectIndex.Entries[row.Entry];
                typeIndex = entry.TypeIndex;
                var type = objectIndex.Types[entry.TypeIndex];
                var id = "#" + entry.Record.Id.ToString(culture);
                var tile = entry.Tile.X.ToString(culture) + ", " + entry.Tile.Y.ToString(culture);
                switch (objectListGrouping)
                {
                    case OmsiPlacedObjectGrouping.ByType:
                        parts.Name.text = id;
                        parts.Detail.text = tile;
                        break;
                    case OmsiPlacedObjectGrouping.ByTile:
                        parts.Name.text = type.FileName;
                        parts.Detail.text = id;
                        break;
                    default:
                        parts.Name.text = type.FileName;
                        parts.Detail.text = id + "  ·  " + tile;
                        break;
                }
                element.tooltip = type.AssetPath;
                selected = IsSelected(entry.Record);
            }
            // Group headings and the objects under them differ by indent and weight; flat lists have objects only.
            parts.Card.EnableInClassList("objlist-row-group", isGroup);
            parts.Card.EnableInClassList("objlist-row-child", !isGroup && objectListGrouping != OmsiPlacedObjectGrouping.Flat);
            parts.Card.EnableInClassList("objlist-row-selected", selected);
            parts.Chevron.style.display = isGroup ? DisplayStyle.Flex : DisplayStyle.None;

            // The origin dot: on a group heading of the by-type list, and on every object row of the other two.
            var showsOrigin = typeIndex >= 0 && isGroup == (objectListGrouping == OmsiPlacedObjectGrouping.ByType);
            var origin = showsOrigin ? objectIndex.Types[typeIndex].Origin : null;
            parts.Dot.style.display = origin.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            parts.Dot.EnableInClassList("objlist-dot-base", origin == OmsiContentOrigin.BaseGame);
            parts.Dot.EnableInClassList("objlist-dot-dlc", origin == OmsiContentOrigin.Dlc);
            parts.Dot.EnableInClassList("objlist-dot-other", origin == OmsiContentOrigin.UserContent || origin == OmsiContentOrigin.Unknown);
        }

        /// <summary>A click or Enter on a row: a heading opens or closes, an object is selected and shown (Ctrl: added to the selection).</summary>
        private void ActivateObjectRow(int index, bool add)
        {
            if (objectListRows == null || index < 0 || index >= objectListRows.Rows.Count)
                return;
            var row = objectListRows.Rows[index];
            if (row.Kind == OmsiPlacedObjectRowKind.Group)
            {
                ToggleObjectGroup(index);
                return;
            }
            var entry = objectIndex.Entries[row.Entry];
            // The list keeps its place: only a click in the map scrolls it to the selection.
            objectListActivating = true;
            try
            {
                if (add)
                    ToggleObject(entry.Tile, entry.Record);
                else
                    SelectObject(entry.Tile, entry.Record, true);
            }
            finally
            {
                objectListActivating = false;
            }
        }

        private HashSet<string> OpenGroupsOf(OmsiPlacedObjectGrouping grouping)
        {
            return grouping == OmsiPlacedObjectGrouping.ByTile ? objectListOpenTiles : objectListOpenTypes;
        }

        private void ToggleObjectGroup(int index)
        {
            var row = objectListRows.Rows[index];
            // With search words every group with matches stays open; a heading then has nothing to toggle.
            if (!string.IsNullOrWhiteSpace(objectListFilter))
                return;
            var open = OpenGroupsOf(objectListGrouping);
            var key = objectIndex.GroupKey(objectListGrouping, row.Group);
            if (!open.Add(key))
                open.Remove(key);
            RebuildObjectRows(true, "toggle");
            objectListView.SetSelectionWithoutNotify(new[] { index });
        }

        private void SetObjectGrouping(OmsiPlacedObjectGrouping grouping)
        {
            if (grouping == objectListGrouping || !ObjectListOpen)
                return;
            objectListGrouping = grouping;
            var tabs = objectListView.parent?.Q<VisualElement>(className: "panel-tabs");
            if (tabs != null)
                for (var i = 0; i < ObjectGroupings.Length && i < tabs.childCount; i++)
                    tabs[i].EnableInClassList("panel-tab-active", ObjectGroupings[i].Grouping == grouping);
            RebuildObjectRows(false, "arrange");
        }

        private void ToggleObjectOrigin(int chip, Button button)
        {
            objectListOriginOn[chip] = !objectListOriginOn[chip];
            button.EnableInClassList("objlist-chip-on", objectListOriginOn[chip]);
            RebuildObjectRows(false, "origin");
        }

        /// <summary>The origins that pass, or null when all three chips are on (then types of unknown origin pass too).</summary>
        private HashSet<OmsiContentOrigin> ObjectOriginsShown()
        {
            if (objectListOriginOn[0] && objectListOriginOn[1] && objectListOriginOn[2])
                return null;
            var shown = new HashSet<OmsiContentOrigin>();
            if (objectListOriginOn[0])
                shown.Add(OmsiContentOrigin.BaseGame);
            if (objectListOriginOn[1])
                shown.Add(OmsiContentOrigin.Dlc);
            if (objectListOriginOn[2])
            {
                shown.Add(OmsiContentOrigin.UserContent);
                shown.Add(OmsiContentOrigin.Unknown);
            }
            return shown;
        }

        private void RebuildObjectIndex()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            objectIndex = OmsiPlacedObjectIndex.Build(currentSummary.Tiles, contentOrigins != null ? (Func<string, OmsiContentOrigin>)contentOrigins.Of : null);
            objectIndexStale = false;
            objectIndexMilliseconds = watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Builds the rows for the filter, arrangement and open groups, and gives them to the list.</summary>
        /// <param name="keepScroll">True keeps the place in the list (a group opened, a change); false starts at the top (a new search).</param>
        private void RebuildObjectRows(bool keepScroll, string reason)
        {
            if (objectListView == null || objectIndex == null)
                return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var filter = new OmsiPlacedObjectFilter(objectListFilter, ObjectOriginsShown());
            objectListRows = objectIndex.BuildRows(filter, objectListGrouping, OpenGroupsOf(objectListGrouping));
            var built = watch.Elapsed.TotalMilliseconds;
            var offset = keepScroll ? objectListScroll : 0f;
            objectListView.itemsSource = objectListRows.Rows;
            objectListView.ClearSelection();
            SetObjectListScroll(offset);
            UpdateObjectListSummary();
            var culture = CultureInfo.InvariantCulture;
            Debug.Log("Object list: " + reason + (objectListFilter.Length > 0 ? " '" + objectListFilter + "'" : string.Empty) + " -> " + objectListRows.Rows.Count + " rows (" +
                      objectListRows.MatchCount + " objects) in " + watch.Elapsed.TotalMilliseconds.ToString("0.0", culture) + " ms, rows " + built.ToString("0.0", culture) + " ms.");
        }

        // Until this time the scroll position the code sets is not overwritten by the one the (not yet laid out) list reports.
        private float objectListScrollHoldUntil;

        private void SetObjectListScroll(float offset)
        {
            ScrollObjectList(() => offset);
        }

        /// <summary>Brings a row into view, in the middle of the list; a row that is in view stays where it is.</summary>
        private void RevealObjectRow(int row)
        {
            var scroll = objectListView.Q<ScrollView>();
            ScrollObjectList(() =>
            {
                // A list that is not laid out yet has no size (NaN): the row is put about in the middle of a typical list then, and set right at the layout.
                var measured = scroll.contentViewport.layout.height;
                var viewport = float.IsNaN(measured) || measured <= 0f ? 300f : measured;
                var top = row * ObjectRowHeight;
                var current = float.IsNaN(scroll.scrollOffset.y) ? 0f : scroll.scrollOffset.y;
                if (measured > 0f && top >= current && top + ObjectRowHeight <= current + viewport)
                    return current;
                return Mathf.Max(0f, top - (viewport - ObjectRowHeight) * 0.5f);
            });
        }

        /// <summary>
        /// Sets the scroll offset the target gives now and again a little later: a list that was just built is laid out one tick after, and
        /// until then its content is too short for the offset.
        /// </summary>
        private void ScrollObjectList(Func<float> target)
        {
            var view = objectListView;
            var scroll = view.Q<ScrollView>();
            objectListScrollHoldUntil = Time.realtimeSinceStartup + 0.3f;
            void Apply()
            {
                if (view != objectListView)
                    return;
                var offset = target();
                if (float.IsNaN(offset))
                    offset = 0f;
                scroll.scrollOffset = new Vector2(0f, offset);
                objectListScroll = offset;
            }

            Apply();
            foreach (var delay in new long[] { 1, 40, 120 })
                view.schedule.Execute(Apply).ExecuteLater(delay);
            // Every layout of the content within the hold time sets it again, which is when the list first knows how long it is.
            EventCallback<GeometryChangedEvent> onGeometry = null;
            onGeometry = _ =>
            {
                if (view != objectListView || Time.realtimeSinceStartup > objectListScrollHoldUntil)
                {
                    scroll.contentContainer.UnregisterCallback(onGeometry);
                    return;
                }
                Apply();
            };
            scroll.contentContainer.RegisterCallback(onGeometry);
        }

        private void UpdateObjectListSummary()
        {
            var total = objectIndex.ObjectCount;
            var filtered = !string.IsNullOrWhiteSpace(objectListFilter) || ObjectOriginsShown() != null;
            string key;
            if (filtered)
                key = "objects.list.summary.match";
            else if (objectListGrouping == OmsiPlacedObjectGrouping.ByType)
                key = "objects.list.summary.type";
            else if (objectListGrouping == OmsiPlacedObjectGrouping.ByTile)
                key = "objects.list.summary.tile";
            else
                key = "objects.list.summary.flat";
            objectListSummary.text = total == 0 ? string.Empty
                : Loc.T(key, ("match", objectListRows.MatchCount), ("count", total), ("groups", objectListRows.GroupCount)); // i18n:keys
            var message = total == 0 ? "objects.list.empty" : objectListRows.MatchCount == 0 ? "objects.list.no_match" : null;
            objectListEmpty.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
            objectListView.style.display = message == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (message != null)
                objectListEmpty.text = Loc.T(message); // i18n:keys
        }

        /// <summary>Starts anew when another map is open: no search, every group closed, the top of the list.</summary>
        private void ResetObjectListForMap()
        {
            var path = currentSummary?.GlobalConfigPath ?? string.Empty;
            if (string.Equals(path, objectListMapPath, StringComparison.OrdinalIgnoreCase))
                return;
            objectListMapPath = path;
            objectListFilter = string.Empty;
            objectListOpenTypes.Clear();
            objectListOpenTiles.Clear();
            for (var i = 0; i < objectListOriginOn.Length; i++)
                objectListOriginOn[i] = true;
            objectListScroll = 0f;
        }

        /// <summary>Drops the index and the list when the popup shows something else or is closed.</summary>
        private void ReleaseObjectList()
        {
            objectIndex = null;
            objectListRows = null;
            objectListView = null;
            objectListSummary = null;
            objectListEmpty = null;
            objectListChips = null;
        }

        /// <summary>After a change of the objects (place, delete, move to another tile, undo, redo): one new index, 100 ms after the last change, only while the list is open.</summary>
        private void ScheduleObjectListRebuild()
        {
            objectIndexStale = true;
            if (!ObjectListOpen || objectListRebuildPending)
                return;
            objectListRebuildPending = true;
            objectListView.schedule.Execute(() =>
            {
                objectListRebuildPending = false;
                if (!ObjectListOpen || !objectIndexStale)
                    return;
                RebuildObjectIndex();
                RebuildObjectRows(true, "changed");
            }).ExecuteLater(100);
        }

        /// <summary>The installation scan is done: the types get their origin, and the chips and dots appear.</summary>
        private void OnObjectOriginsKnown()
        {
            if (!ObjectListOpen || contentOrigins == null)
                return;
            objectIndex.ResolveOrigins(contentOrigins.Of);
            objectListChips.style.display = objectIndex.ObjectCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            objectListView.RefreshItems();
        }

        /// <summary>The selection changed: the rows of the selected objects get their mark, and a click in the map brings the object into view.</summary>
        private void RefreshObjectListSelection()
        {
            if (!ObjectListOpen || objectListRows == null)
                return;
            objectListView.RefreshItems();
            if (objectListActivating || selection.Count != 1)
                return;
            var entry = objectIndex.EntryOf(selection[0].Record);
            if (entry < 0)
                return;
            var rows = objectListRows.Rows;
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == OmsiPlacedObjectRowKind.Object && rows[i].Entry == entry)
                {
                    RevealObjectRow(i);
                    return;
                }
        }

        /// <summary>Enter chooses the row; Right opens a heading and Left closes it or goes up to it; arrows, Home, End and Page keys are the list's own.</summary>
        private void OnObjectListKey(KeyDownEvent evt)
        {
            if (objectListRows == null)
                return;
            var index = objectListView.selectedIndex;
            if (index < 0 || index >= objectListRows.Rows.Count)
                return;
            var row = objectListRows.Rows[index];
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    ActivateObjectRow(index, evt.ctrlKey || evt.commandKey);
                    evt.StopPropagation();
                    break;
                case KeyCode.RightArrow:
                    if (row.Kind == OmsiPlacedObjectRowKind.Group && !row.Expanded)
                        ToggleObjectGroup(index);
                    evt.StopPropagation();
                    break;
                case KeyCode.LeftArrow:
                    if (row.Kind == OmsiPlacedObjectRowKind.Group && row.Expanded)
                        ToggleObjectGroup(index);
                    else if (row.Kind == OmsiPlacedObjectRowKind.Object)
                        for (var up = index - 1; up >= 0; up--)
                            if (objectListRows.Rows[up].Kind == OmsiPlacedObjectRowKind.Group)
                            {
                                objectListView.SetSelectionWithoutNotify(new[] { up });
                                objectListView.ScrollToItem(up);
                                break;
                            }
                    evt.StopPropagation();
                    break;
            }
        }

        /// <summary>
        /// Command line (test runs): <c>-objectFilter Text</c> types into the search field, <c>-objectGroup type|tile|flat</c> chooses the arrangement,
        /// <c>-objectType Text</c> types the text into the search field letter by letter (every letter is one search, timed in the log),
        /// <c>-objectOrigins base,dlc,other</c> keeps only the origins named, <c>-objectOpenGroup Name</c> opens a group (a file name, or <c>x,y</c> for a tile), <c>-objectClick ID</c> clicks the row of that object. Any of them
        /// opens the list. This part only reads the arguments; <see cref="RunObjectListActions"/> acts when the list is up.
        /// </summary>
        private bool ReadObjectListArguments(string[] args)
        {
            string Value(string option)
            {
                var at = Array.IndexOf(args, option);
                return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            }

            // Another map than the last starts with a clean list; do that now, so it does not wipe what the arguments set.
            ResetObjectListForMap();
            var any = false;
            var filter = Value("-objectFilter");
            if (filter != null)
            {
                objectListFilter = filter;
                any = true;
            }
            var group = Value("-objectGroup");
            if (group != null)
            {
                any = true;
                if (group.IndexOf("tile", StringComparison.OrdinalIgnoreCase) >= 0)
                    objectListGrouping = OmsiPlacedObjectGrouping.ByTile;
                else if (group.IndexOf("flat", StringComparison.OrdinalIgnoreCase) >= 0)
                    objectListGrouping = OmsiPlacedObjectGrouping.Flat;
                else
                    objectListGrouping = OmsiPlacedObjectGrouping.ByType;
            }
            var origins = Value("-objectOrigins");
            if (origins != null)
            {
                any = true;
                var wanted = origins.ToLowerInvariant();
                objectListOriginOn[0] = wanted.Contains("base");
                objectListOriginOn[1] = wanted.Contains("dlc");
                objectListOriginOn[2] = wanted.Contains("other");
            }
            objectArgOpenGroup = Value("-objectOpenGroup");
            any |= objectArgOpenGroup != null;
            objectArgTyped = Value("-objectType");
            any |= objectArgTyped != null;
            objectArgClickId = int.TryParse(Value("-objectClick"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null;
            any |= Array.IndexOf(args, "-objectClick") >= 0;
            return any;
        }

        private void RunObjectListActions()
        {
            if (!ObjectListOpen)
                return;
            if (objectArgOpenGroup != null)
            {
                var name = objectArgOpenGroup.Replace(" ", string.Empty);
                foreach (var type in objectIndex.Types)
                    if (string.Equals(type.FileName, name, StringComparison.OrdinalIgnoreCase))
                        objectListOpenTypes.Add(type.Key);
                if (name.IndexOf(',') > 0)
                    objectListOpenTiles.Add(name);
                objectArgOpenGroup = null;
                RebuildObjectRows(false, "open group");
            }
            if (objectArgTyped != null)
            {
                // Types the text letter by letter into the search field, as a user does; every letter is one search ("Object list: filter ..." in the log).
                var field = objectListView.parent.Q<TextField>();
                for (var length = 1; length <= objectArgTyped.Length; length++)
                    field.value = objectArgTyped.Substring(0, length);
                objectArgTyped = null;
            }
            if (objectArgClickId.HasValue)
            {
                var id = objectArgClickId.Value;
                objectArgClickId = null;
                var found = -1;
                for (var i = 0; i < objectIndex.Entries.Count && found < 0; i++)
                    if (objectIndex.Entries[i].Record.Id == id)
                        found = i;
                if (found < 0)
                {
                    Debug.LogWarning("-objectClick: the map has no object #" + id + ".");
                    return;
                }
                var entry = objectIndex.Entries[found];
                // A click needs the row: open the group that holds it first.
                if (objectListGrouping != OmsiPlacedObjectGrouping.Flat)
                {
                    OpenGroupsOf(objectListGrouping).Add(objectIndex.GroupKey(objectListGrouping, objectListGrouping == OmsiPlacedObjectGrouping.ByType ? entry.TypeIndex : entry.TileIndex));
                    RebuildObjectRows(false, "open group of #" + id);
                }
                var rows = objectListRows.Rows;
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Kind != OmsiPlacedObjectRowKind.Object || rows[i].Entry != found)
                        continue;
                    RevealObjectRow(i);
                    objectListView.SetSelectionWithoutNotify(new[] { i });
                    ActivateObjectRow(i, false);
                    Debug.Log("Object list: clicked row " + i + " of object #" + id + ".");
                    return;
                }
                Debug.LogWarning("-objectClick: object #" + id + " is hidden by the search.");
            }
        }
    }
}
