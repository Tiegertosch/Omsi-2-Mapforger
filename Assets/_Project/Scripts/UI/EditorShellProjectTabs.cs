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
    /// The redesigned project browser (ADR 078): the name, counts and warnings of the map stay on top, below them tabs by task
    /// (Tiles, Map, Chrono, Lines, Paths) show one thing at a time. The tab chosen survives every redraw.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private enum ProjectTab
        {
            Tiles,
            Map,
            Entries,
            Chrono,
            Lines,
            Paths
        }

        private ProjectTab projectTab = ProjectTab.Tiles;
        private string tileBrowserFilter = string.Empty;

        private bool ProjectTabAvailable(ProjectTab tab)
        {
            switch (tab)
            {
                case ProjectTab.Chrono:
                    return chronoVariants.Count > 0 || session != null && session.CanEdit;
                case ProjectTab.Lines:
                    return routes != null && routes.Lines.Count > 0;
                case ProjectTab.Paths:
                    return pathsShown;
                default:
                    return true;
            }
        }

        /// <summary>Shows a tab of the project browser; one that is not available (no lines, paths hidden) falls back to the tiles.</summary>
        private void ShowProjectTab(ProjectTab tab)
        {
            projectTab = ProjectTabAvailable(tab) ? tab : ProjectTab.Tiles;
            RenderProjectBrowser();
        }

        /// <summary>The strip of tabs; only the tabs that have something to show are drawn.</summary>
        private VisualElement ProjectTabStrip(ProjectTab current)
        {
            var strip = new VisualElement();
            strip.AddToClassList("panel-tabs");
            strip.AddToClassList("project-tabs");
            var tabs = new[]
            {
                (ProjectTab.Tiles, "project.tab.tiles"), (ProjectTab.Map, "project.tab.map"), (ProjectTab.Entries, "project.tab.entries"), (ProjectTab.Chrono, "project.tab.chrono"),
                (ProjectTab.Lines, "project.tab.lines"), (ProjectTab.Paths, "project.tab.paths")
            };
            foreach (var (tab, key) in tabs)
            {
                if (!ProjectTabAvailable(tab))
                    continue;
                var captured = tab;
                var button = new Button(() => ShowProjectTab(captured));
                LocBindings.Text(button, key); // i18n:keys
                button.AddToClassList("panel-tab");
                button.EnableInClassList("panel-tab-active", tab == current);
                strip.Add(button);
            }
            return strip;
        }

        /// <summary>A tab that is not the tile list: a scrolling page.</summary>
        private static VisualElement ProjectTabPage(VisualElement content)
        {
            var page = new ScrollView(ScrollViewMode.Vertical);
            page.AddToClassList("browser-page");
            page.Add(content);
            return page;
        }

        /// <summary>The map tab: the button for its settings and what its author wrote about it.</summary>
        private VisualElement MapTab()
        {
            var content = new VisualElement();
            if (session != null && session.CanEdit)
                content.Add(MapSettingsButton());
                content.Add(ParkListButton());
            if (!string.IsNullOrWhiteSpace(currentSummary.Description))
            {
                var heading = new Label(Loc.T("project.about"));
                heading.AddToClassList("inspector-group-title");
                heading.style.marginTop = 8;
                heading.style.marginBottom = 4;
                content.Add(heading);
                var description = new Label(currentSummary.Description.Trim());
                description.AddToClassList("browser-map-description-text");
                content.Add(description);
            }
            return ProjectTabPage(content);
        }

        /// <summary>The lines of the timetable, each highlighted on the map by a click.</summary>
        private VisualElement LinesSection()
        {
            var section = new VisualElement();
            section.AddToClassList("browser-lines");
            var heading = new Label(Loc.T("project.lines", ("count", routes.Lines.Count)));
            heading.AddToClassList("inspector-group-title");
            heading.style.marginBottom = 4;
            section.Add(heading);
            var sorted = new List<OmsiRouteLine>(routes.Lines);
            sorted.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var line in sorted)
            {
                var name = line.Name;
                var entry = new Button(() => HighlightLine(name))
                {
                    text = Loc.T("project.line", ("name", name), ("stops", line.StopIds.Count), ("trips", line.ScheduledTripCount)),
                    tooltip = Loc.T("project.line.tooltip", ("name", name))
                };
                entry.AddToClassList("browser-tile-entry");
                entry.EnableInClassList("browser-tile-entry-selected", name == highlightedLine);
                section.Add(entry);
                lineEntries[name] = entry;
            }
            return section;
        }

        /// <summary>The tile tab: a filter by coordinates or file name above the list of tiles, which keeps the room that is left.</summary>
        private VisualElement TilesTab()
        {
            var tab = new VisualElement();
            tab.AddToClassList("browser-tiles-tab");
            var filter = new TextField { value = tileBrowserFilter };
            filter.AddToClassList("panel-search-field");
            filter.AddToClassList("compact-filter");
            filter.AddToClassList("browser-filter");
            filter.textEdition.placeholder = Loc.T("project.filter");
            filter.tooltip = Loc.T("project.filter.tooltip");
            tab.Add(filter);
            var tileList = new ScrollView(ScrollViewMode.Vertical);
            tileList.AddToClassList("browser-tile-list");
            tab.Add(tileList);
            filter.RegisterValueChangedCallback(evt =>
            {
                tileBrowserFilter = evt.newValue ?? string.Empty;
                FillTileList(tileList);
            });
            FillTileList(tileList);
            return tab;
        }

        private static bool TileMatches(string filter, OmsiTileSummary tile)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;
            var text = filter.Replace(" ", string.Empty);
            var coordinates = tile.X.ToString(CultureInfo.InvariantCulture) + "," + tile.Y.ToString(CultureInfo.InvariantCulture);
            return coordinates.Contains(text) || (tile.FileName ?? string.Empty).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Fills the list with the tiles that match the filter; entries that are not shown are not in <c>browserEntries</c>.</summary>
        private void FillTileList(ScrollView tileList)
        {
            tileList.Clear();
            browserEntries.Clear();
            foreach (var tile in currentSummary.Tiles)
            {
                if (!TileMatches(tileBrowserFilter, tile))
                    continue;
                var capturedTile = tile;
                var entry = new Button(() => SelectTile(capturedTile, true));
                entry.AddToClassList("browser-tile-entry");
                if (ReferenceEquals(tile, selectedTile))
                    entry.AddToClassList("browser-tile-entry-selected");
                if (!tile.IsAvailable)
                    entry.AddToClassList("browser-tile-entry-missing");
                tileList.Add(entry);
                browserEntries[tile] = entry;
                UpdateBrowserEntry(tile);
            }
            if (tileList.contentContainer.childCount == 0)
            {
                var none = new Label(Loc.T("project.no_match"));
                none.AddToClassList("muted");
                tileList.Add(none);
            }
        }

        /// <summary>
        /// Command line: <c>-projectTab tiles|map|entries|chrono|lines|paths</c> opens that tab of the project browser (for screenshots), and
        /// <c>-tileFilter Text</c> types a text into the filter of the tile tab.
        /// </summary>
        private void RunProjectTabArguments(string[] args)
        {
            string Value(string option)
            {
                var index = Array.IndexOf(args, option);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            var filter = Value("-tileFilter");
            if (filter != null)
                tileBrowserFilter = filter;
            var name = Value("-projectTab");
            if (name == null || !Enum.TryParse(name, true, out ProjectTab tab))
            {
                if (filter != null)
                    RenderProjectBrowser();
                return;
            }
            ShowProjectTab(tab);
        }
    }
}
