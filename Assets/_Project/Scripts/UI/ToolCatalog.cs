using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The window of a tool above the bottom bar (ADR 098, stage 2), built in one place for every tool: the catalog with one header row
    /// (its tabs as icons, a magnifier that unfolds the search, the X), the area of tiles under it and a note when it is empty; and the
    /// option column beside it. What a tool shows in them is its own; how they look is decided here and in the classes of
    /// EditorShell.uss.
    /// </summary>
    internal sealed class ToolCatalog
    {
        /// <param name="closeTooltipKey">The tooltip of the X.</param>
        /// <param name="searchTooltipKey">The tooltip and placeholder of the search, or null for a catalog without one.</param>
        /// <param name="scrollingTiles">True for a scrolling area of tiles; false leaves <see cref="Tiles"/> null for pages of the tool's own.</param>
        public ToolCatalog(string closeTooltipKey, Action close, string searchTooltipKey = null, bool scrollingTiles = true)
        {
            Panel = new VisualElement();
            Panel.AddToClassList("tool-panel");
            Panel.AddToClassList("picker-panel");
            var header = new VisualElement();
            header.AddToClassList("picker-header");
            Tabs = new VisualElement();
            Tabs.AddToClassList("panel-tabs");
            Tabs.AddToClassList("picker-tabs");
            header.Add(Tabs);
            if (searchTooltipKey != null)
            {
                searchRow = new VisualElement();
                searchRow.AddToClassList("panel-search");
                searchRow.AddToClassList("picker-search");
                Search = new TextField();
                Search.AddToClassList("panel-search-field");
                Search.textEdition.placeholder = Core.Localization.Loc.T(searchTooltipKey); // i18n:keys
                // Esc in the field folds the search, before the shell's own Esc, which leaves a focused text field alone.
                Search.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Escape)
                        return;
                    FoldSearch();
                    evt.StopPropagation();
                }, TrickleDown.TrickleDown);
                searchRow.Add(Search);
                header.Add(searchRow);
                searchToggle = LocBindings.WithTooltip(new Button(), searchTooltipKey); // i18n:keys
                searchToggle.AddToClassList("panel-icon-button");
                searchToggle.Add(new IconElement("search"));
                searchToggle.clicked += () =>
                {
                    if (searchRow.style.display == DisplayStyle.None)
                    {
                        ShowSearch(true);
                        Search.Focus();
                    }
                    else
                        FoldSearch();
                };
                header.Add(searchToggle);
                ShowSearch(false);
            }
            var closeButton = LocBindings.WithTooltip(new Button(close), closeTooltipKey); // i18n:keys
            closeButton.AddToClassList("panel-icon-button");
            closeButton.Add(new IconElement("x"));
            header.Add(closeButton);
            Panel.Add(header);
            if (scrollingTiles)
            {
                // The scroll bar always shows, so the tiles keep their places whatever the tab holds.
                Tiles = new ScrollView(ScrollViewMode.Vertical)
                {
                    verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible, horizontalScrollerVisibility = ScrollerVisibility.Hidden
                };
                Tiles.AddToClassList("picker-tiles");
                Tiles.contentContainer.AddToClassList("picker-tiles-content");
                Panel.Add(Tiles);
            }
            Empty = new Label();
            Empty.AddToClassList("panel-info");
            Empty.AddToClassList("picker-empty");
            Panel.Add(Empty);
        }

        private readonly VisualElement searchRow;
        private readonly Button searchToggle;

        /// <summary>The catalog: header, tiles, empty note.</summary>
        public VisualElement Panel { get; }

        /// <summary>The row the tabs stand in; add them with <see cref="AddTab"/>.</summary>
        public VisualElement Tabs { get; }

        /// <summary>The search field, or null without a search.</summary>
        public TextField Search { get; }

        /// <summary>The area of tiles, or null when the tool shows pages of its own.</summary>
        public ScrollView Tiles { get; }

        /// <summary>The note that lies over the tile area while it is empty.</summary>
        public Label Empty { get; }

        /// <summary>A tab as an icon; name and description show in its tooltip. The tool marks the open tab with <c>panel-tab-active</c>.</summary>
        public Button AddTab(string icon, string nameKey, string tooltipKey, Action clicked)
        {
            var button = new Button(clicked);
            LocBindings.TitledTooltip(button, nameKey, tooltipKey); // i18n:keys
            button.AddToClassList("panel-tab");
            button.AddToClassList("picker-tab");
            button.AddToClassList("picker-icon-tab");
            button.Add(new IconElement(icon));
            Tabs.Add(button);
            return button;
        }

        /// <summary>True while the search field is unfolded.</summary>
        public bool SearchOpen => searchRow != null && searchRow.style.display != DisplayStyle.None;

        /// <summary>Unfolds or folds the search; for a search set from the command line.</summary>
        public void ShowSearch(bool open)
        {
            if (searchRow == null)
                return;
            searchRow.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            searchToggle.EnableInClassList("picker-search-open", open);
        }

        private void FoldSearch()
        {
            ShowSearch(false);
            Search.value = string.Empty;
            Search.Blur();
        }

        /// <summary>The line a tool's catalog has to say: what the next click does, what is listed, a warning. The shell shows it in the status bar.</summary>
        public event Action<string, bool> HintChanged;

        public void ShowHint(string text, bool warning)
        {
            HintChanged?.Invoke(text, warning);
        }

        /// <summary>
        /// The option column of a tool, without a title: what it holds says what it is for. A scrolling column holds the rows in a
        /// <see cref="ScrollView"/> (add to it as to any element) and grows to the height of the catalog beside it before it scrolls.
        /// </summary>
        /// <param name="scrolling">True for a column that scrolls once its rows are taller than the catalog.</param>
        /// <param name="icons">True for the wider column of <see cref="OptionControls"/> rows: a label, then icons and small fields.</param>
        public static VisualElement Options(bool scrolling = false, bool icons = false)
        {
            VisualElement options = scrolling
                ? new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden }
                : new VisualElement();
            if (scrolling)
                options.AddToClassList("tool-options-scroll");
            options.AddToClassList("tool-panel");
            options.AddToClassList("picker-options");
            if (icons)
                options.AddToClassList("icon-options");
            return options;
        }

        /// <summary>
        /// A tile of the catalog with an icon over its name; its tooltip holds name and explanation. The tool marks the chosen one
        /// with <c>picker-tile-selected</c>.
        /// </summary>
        /// <param name="into">Where the tile goes: a page of the tool's own, or the area of tiles when null.</param>
        /// <param name="captioned">True to show the name under the icon always, not only while the pointer is over the tile.</param>
        public Button AddTile(string icon, string nameKey, string tooltipKey, Action clicked, VisualElement into = null, bool captioned = false)
        {
            var tile = new Button(clicked);
            LocBindings.TitledTooltip(tile, nameKey, tooltipKey); // i18n:keys
            tile.AddToClassList("picker-tile");
            if (captioned)
                tile.AddToClassList("picker-tile-captioned");
            var preview = new VisualElement { pickingMode = PickingMode.Ignore };
            preview.AddToClassList("picker-tile-preview");
            preview.AddToClassList("picker-tile-icon");
            preview.Add(new IconElement(icon));
            tile.Add(preview);
            var name = LocBindings.NewLabel(nameKey); // i18n:keys
            name.pickingMode = PickingMode.Ignore;
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            (into ?? Tiles.contentContainer).Add(tile);
            return tile;
        }

        /// <summary>
        /// A tile with a picture over its name, for a catalog of things that are told apart by how they look (the ground textures; the
        /// object library draws its own tiles). The name stays visible under the picture; a picture that is not there shows
        /// <paramref name="fallbackIcon"/>. The caller adds the tile and marks the chosen one with <c>picker-tile-selected</c>.
        /// </summary>
        /// <param name="image">The picture, scaled to the tile (up to 128 px is plenty), or null.</param>
        /// <param name="name">The name as text, not a key: it is a file or an item of the map.</param>
        /// <param name="description">The line the tooltip shows under the name, or empty.</param>
        public static Button ImageTile(Texture2D image, string fallbackIcon, string name, string description, Action clicked)
        {
            var tile = new Button(clicked) { tooltip = LocBindings.CardTooltip(name, description) };
            tile.AddToClassList("picker-tile");
            tile.AddToClassList("picker-tile-captioned");
            var preview = new VisualElement { pickingMode = PickingMode.Ignore };
            preview.AddToClassList("picker-tile-preview");
            if (image != null)
                preview.style.backgroundImage = new StyleBackground(image);
            else
            {
                preview.AddToClassList("picker-tile-icon");
                preview.Add(new IconElement(fallbackIcon));
            }
            tile.Add(preview);
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("picker-tile-name");
            tile.Add(label);
            return tile;
        }
    }
}
