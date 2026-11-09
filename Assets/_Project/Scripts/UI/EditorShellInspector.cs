using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The building blocks of the redesigned inspector (ADR 077): two tabs (the selection, and the tile with its lists), groups that
    /// fold and remember how you left them, compact rows (X, Y and height in one line) and a bar of buttons that stays below the
    /// scrolling part.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private enum InspectorTab
        {
            Selection,
            Tile
        }

        private const string InspectorFoldsKey = "Mapforge.InspectorFolds";

        // Most hits of the search in the tile tab that are listed.
        private const int TileListLimit = 10;

        private InspectorTab inspectorTab = InspectorTab.Selection;

        // The selection the tab was chosen for: another selection starts on the selection tab again.
        private string inspectorTabOwner = string.Empty;

        // The bar under the scrolling part; the editors put their buttons (Apply, Delete ...) here.
        private VisualElement inspectorActions;

        private string tileFilter = string.Empty;
        private Dictionary<string, bool> inspectorFolds;

        private bool HasInspectorSelection => selection.Count > 1 || selectedObject != null || selectedSpline != null || selectedRow != null && activeTool == EditorTool.Rows;

        private string InspectorSelectionIdentity()
        {
            return (selectedObject?.Id.ToString() ?? "-") + "|" + (selectedSpline?.Id.ToString() ?? "-") + "|" + selection.Count + "|" + (selectedRow != null && activeTool == EditorTool.Rows ? "row" : "-");
        }

        /// <summary>Decides which tab the inspector shows: the selection's when there is one that was not left on purpose, else the tile's.</summary>
        private void ChooseInspectorTab()
        {
            if (!HasInspectorSelection)
            {
                inspectorTab = InspectorTab.Tile;
                inspectorTabOwner = string.Empty;
                return;
            }
            var identity = InspectorSelectionIdentity();
            if (identity == inspectorTabOwner)
                return;
            inspectorTab = InspectorTab.Selection;
            inspectorTabOwner = identity;
        }

        /// <summary>The strip with the selection and tile tabs; only drawn while something is selected.</summary>
        private VisualElement InspectorTabStrip()
        {
            var strip = new VisualElement();
            strip.AddToClassList("panel-tabs");
            strip.AddToClassList("inspector-tabs");
            var selectionTab = new Button(() => ShowInspectorTab(InspectorTab.Selection));
            LocBindings.Text(selectionTab, "inspector.tab.selection");
            selectionTab.AddToClassList("panel-tab");
            selectionTab.EnableInClassList("panel-tab-active", inspectorTab == InspectorTab.Selection);
            strip.Add(selectionTab);
            var tileTab = new Button(() => ShowInspectorTab(InspectorTab.Tile));
            LocBindings.Text(tileTab, "inspector.tab.tile");
            tileTab.AddToClassList("panel-tab");
            tileTab.EnableInClassList("panel-tab-active", inspectorTab == InspectorTab.Tile);
            strip.Add(tileTab);
            return strip;
        }

        private void ShowInspectorTab(InspectorTab tab)
        {
            if (inspectorTab == tab)
                return;
            inspectorTab = tab;
            RenderInspector();
        }

        private bool IsFolded(string id, bool defaultOpen)
        {
            if (inspectorFolds == null)
            {
                inspectorFolds = new Dictionary<string, bool>(StringComparer.Ordinal);
                var stored = PlayerPrefs.GetString(InspectorFoldsKey, string.Empty);
                foreach (var part in stored.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = part.Split('=');
                    if (pair.Length == 2)
                        inspectorFolds[pair[0]] = pair[1] == "1";
                }
            }
            return !(inspectorFolds.TryGetValue(id, out var open) ? open : defaultOpen);
        }

        private void RememberFold(string id, bool open)
        {
            IsFolded(id, open);
            inspectorFolds[id] = open;
            var text = new StringBuilder();
            foreach (var pair in inspectorFolds)
                text.Append(pair.Key).Append('=').Append(pair.Value ? '1' : '0').Append(';');
            PlayerPrefs.SetString(InspectorFoldsKey, text.ToString());
        }

        /// <summary>
        /// A group with a title that folds and unfolds. How it was left is remembered by its id, also between runs; <paramref name="defaultOpen"/>
        /// is only how it starts. A group the fill left empty is not shown at all.
        /// </summary>
        private VisualElement AddInspectorGroup(VisualElement parent, string id, string title, bool defaultOpen, Action<VisualElement> fill)
        {
            var root = new VisualElement();
            root.AddToClassList("inspector-group");
            var header = new VisualElement();
            header.AddToClassList("inspector-group-header");
            var chevron = new IconElement("chevron-right");
            chevron.AddToClassList("inspector-group-chevron");
            header.Add(chevron);
            var label = new Label(title);
            label.AddToClassList("inspector-group-title");
            header.Add(label);
            root.Add(header);
            var body = new VisualElement();
            body.AddToClassList("inspector-group-body");
            root.Add(body);

            void Show(bool open)
            {
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                chevron.SetIcon(open ? "chevron-down" : "chevron-right");
            }

            Show(!IsFolded(id, defaultOpen));
            header.userData = (Action<bool>)Show;
            header.RegisterCallback<ClickEvent>(_ =>
            {
                var open = body.resolvedStyle.display == DisplayStyle.None;
                Show(open);
                RememberFold(id, open);
            });
            fill(body);
            if (body.childCount > 0)
                parent.Add(root);
            return body;
        }

        /// <summary>Some numbers in one line, each field with its label above it.</summary>
        private static TextField[] AddEditRow(VisualElement container, params (string Key, double Value)[] entries)
        {
            var row = new VisualElement();
            row.AddToClassList("inspector-edit-row");
            var fields = new TextField[entries.Length];
            for (var index = 0; index < entries.Length; index++)
                fields[index] = AddEditField(row, entries[index].Key, entries[index].Value);
            container.Add(row);
            return fields;
        }

        /// <summary>The line under the tile's name: how much it holds.</summary>
        private VisualElement InspectorTileSummary(OmsiTileSummary tile)
        {
            var summary = new Label(Loc.T("inspector.tile_summary", ("objects", tile.ObjectCount), ("splines", tile.SplineCount), ("connections", tile.AttachmentCount)));
            summary.AddToClassList("inspector-tile-summary");
            return summary;
        }

        /// <summary>The tile tab: its file, state and terrain, the tile's own buttons, and a search for an object or spline of the tile by ID or name.</summary>
        private void AddTileTab(VisualElement container)
        {
            var tile = selectedTile;
            var facts = new VisualElement();
            facts.AddToClassList("inspector-facts");
            AddInspectorValue(facts, "inspector.file", tile.FileName + "  (" + Loc.T(tile.IsAvailable ? "inspector.available" : "inspector.missing") /* i18n:keys */ + ")");
            if (currentSummary.UsesWorldCoordinates)
                AddInspectorValue(facts, "inspector.corner", Loc.T("inspector.edge", ("corner", OmsiWorldTiles.DescribeCorner(tile.X, tile.Y)), ("meters", OmsiWorldTiles.EdgeMeters(tile.Y + 0.5))));
            AddInspectorValue(facts, "inspector.terrain", tile.HasTerrain ? Loc.T("inspector.terrain_range", ("low", tile.TerrainMinHeight), ("high", tile.TerrainMaxHeight)) : Loc.T("inspector.unavailable"));
            container.Add(facts);

            AddTerrainExportButton(container, tile);
            AddTileButtons(container);

            var filter = new TextField { value = tileFilter };
            filter.AddToClassList("panel-search-field");
            filter.AddToClassList("inspector-filter");
            filter.AddToClassList("compact-filter");
            filter.textEdition.placeholder = Loc.T("inspector.filter");
            filter.tooltip = Loc.T("inspector.filter.tooltip");
            container.Add(filter);
            var lists = new VisualElement();
            container.Add(lists);
            filter.RegisterValueChangedCallback(evt =>
            {
                tileFilter = evt.newValue ?? string.Empty;
                FillTileLists(lists);
            });
            FillTileLists(lists);
        }

        private static bool MatchesFilter(string filter, int id, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;
            var text = filter.Trim();
            return id.ToString(CultureInfo.InvariantCulture).Contains(text) || (assetPath ?? string.Empty).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The objects and splines of the tile that match what was typed in the search; nothing is listed until something is typed.</summary>
        private void FillTileLists(VisualElement host)
        {
            host.Clear();
            var tile = selectedTile;
            if (tile == null || string.IsNullOrWhiteSpace(tileFilter))
                return;

            var shown = 0;
            var matching = 0;
            foreach (var item in tile.Objects)
            {
                if (!MatchesFilter(tileFilter, item.Id, item.AssetPath))
                    continue;
                matching++;
                if (shown >= TileListLimit)
                    continue;
                shown++;
                var details = "#" + item.Id + "  " + item.AssetPath + "\n" +
                              "X " + item.LocalX.ToString("F1") + "  Y " + item.LocalY.ToString("F1") +
                              "  H " + item.Height.ToString("F1") + " m  •  " + item.HeadingDegrees.ToString("F1") + "°";
                var record = AddInspectorRecord(host, details, "inspector-record");
                record.AddToClassList("inspector-record-clickable");
                record.EnableInClassList("inspector-record-selected", IsSelected(item));
                var capturedItem = item;
                record.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.ctrlKey || evt.commandKey)
                        ToggleObject(selectedTile, capturedItem);
                    else
                        SelectObject(selectedTile, capturedItem, true);
                });
            }
            foreach (var spline in tile.Splines)
            {
                if (!MatchesFilter(tileFilter, spline.Id, spline.AssetPath))
                    continue;
                matching++;
                if (shown >= TileListLimit)
                    continue;
                shown++;
                var details = "#" + spline.Id + "  " + spline.AssetPath + "\n" +
                              "X " + spline.LocalX.ToString("F1") + "  Y " + spline.LocalY.ToString("F1") +
                              "  " + spline.LengthMeters.ToString("F1") + " m  •  R " + spline.RadiusMeters.ToString("F1") + " m";
                var record = AddInspectorRecord(host, details, "inspector-record");
                record.AddToClassList("inspector-record-clickable");
                record.EnableInClassList("inspector-record-selected", ReferenceEquals(spline, selectedSpline));
                var capturedSpline = spline;
                record.RegisterCallback<ClickEvent>(_ => SelectSpline(selectedTile, capturedSpline));
            }
            if (matching == 0)
                AddInspectorRecord(host, Loc.T("inspector.no_match"), "inspector-record-empty");
            AddMoreNote(host, matching, shown);
        }

        private static void AddMoreNote(VisualElement host, int matching, int shown)
        {
            if (matching > shown)
                AddInspectorRecord(host, Loc.T("inspector.more", ("shown", shown), ("total", matching)), "inspector-record-empty");
        }
    }
}
