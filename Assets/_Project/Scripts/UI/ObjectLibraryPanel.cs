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
    /// The panels of the Objects tool, laid out like the road menu (ADR 029, ADR 030): on the left the options, beside them
    /// the picker with tabs for the objects this map uses, the user's favourites, the library by folder and the kinds of
    /// object (buildings, trees, junctions, signs, stops, lights, rail, other), a search across all of them and a tile per
    /// object with a preview the object renderer draws from the model. A click on an object reports it so placing can start; the panel stays open for the next one. Favourites
    /// are a user preference, kept across maps and sessions. Objects of a DLC carry a tag and can be hidden, so maps for
    /// everyone use only the base game and freeware; folders of add-ons bought outside Steam can be marked as DLC by hand.
    /// </summary>
    internal sealed class ObjectLibraryPanel : VisualElement
    {
        private enum Tab { InMap, Favorites, Library, Category }

        /// <summary>At most this many tiles are built at once; a last tile shows more.</summary>
        private const int PageSize = 120;

        /// <summary>The list follows the search field this long after the last key, so typing does not sort a large library per letter.</summary>
        private const int SearchDelayMilliseconds = 150;

        private const string FavoritesKey = "Mapforge.ObjectFavorites";
        private const string HideDlcKey = "Mapforge.HideDlcObjects";
        private const string RandomHeadingKey = "Mapforge.RandomHeading";
        private const string DlcFoldersKey = "Mapforge.DlcFolders";
        private const string RootFolder = "Sceneryobjects";

        private sealed class Entry
        {
            public bool IsFolder;
            public string Path;
            public string Name;
            public bool IsDlc;

            /// <summary>A folder the user marked as DLC, or an object below one.</summary>
            public bool IsMarked;

            /// <summary>A folder whose objects all come with the base game; every player has them, so it cannot be marked as DLC.</summary>
            public bool IsBaseGame;

            public int Count;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<Button, KeyValuePair<Tab, OmsiObjectCategory>> tabButtons = new Dictionary<Button, KeyValuePair<Tab, OmsiObjectCategory>>();
        private readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> dlcFolders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly TextField search;
        private readonly VisualElement folderBar;
        private readonly Label folderLabel;
        private readonly ScrollView tiles;
        private readonly Label emptyLabel;
        private readonly Label titleLabel;
        private readonly Label info;
        private readonly Toggle hideDlc;
        private readonly TextField headingField;
        private readonly Toggle randomHeading;
        private readonly Dictionary<string, VisualElement> tileByPath = new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private IVisualElementScheduledItem searchRefresh;

        private IReadOnlyList<string> library;
        private IReadOnlyDictionary<string, List<string>> objectGroups = new Dictionary<string, List<string>>();
        private IReadOnlyDictionary<string, OmsiObjectCategory> categories = new Dictionary<string, OmsiObjectCategory>();
        private OmsiContentOrigins origins;
        private IReadOnlyDictionary<string, int> used = new Dictionary<string, int>();
        private Tab tab = Tab.InMap;
        private OmsiObjectCategory category = OmsiObjectCategory.Buildings;
        private string folder = RootFolder;
        private string chosen;
        private int shownPages = 1;

        // The options column beside the picker: for placing objects, or another tool's, such as the row tool's.
        private readonly VisualElement placingOptions;
        private VisualElement shownOptions;
        private string otherChosenHint;
        private string otherIdleHint;

        public ObjectLibraryPanel()
        {
            AddToClassList("picker-tool");
            LoadFavorites();
            LoadDlcFolders();

            // The options on the left.
            var options = new VisualElement();
            options.AddToClassList("tool-panel");
            options.AddToClassList("picker-options");
            var optionsTitle = LocBindings.NewLabel("objects.options");
            optionsTitle.AddToClassList("picker-options-title");
            options.Add(optionsTitle);
            hideDlc = new Toggle { value = PlayerPrefs.GetInt(HideDlcKey, 0) != 0 };
            hideDlc.AddToClassList("picker-option-toggle");
            hideDlc.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetInt(HideDlcKey, evt.newValue ? 1 : 0);
                PlayerPrefs.Save();
                Refresh();
            });
            Row(options, "objects.hide_dlc").Add(hideDlc);
            var headingRow = Row(options, "objects.heading");
            headingField = new TextField { value = "0" };
            LocBindings.Tooltip(headingField, "objects.heading.tooltip");
            headingField.AddToClassList("picker-field");
            headingField.RegisterValueChangedCallback(evt =>
            {
                if (double.TryParse(evt.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var heading) && !double.IsInfinity(heading))
                    HeadingChanged?.Invoke(heading);
            });
            LocBindings.Tooltip(headingRow, "objects.heading.tooltip");
            headingRow.Add(headingField);
            randomHeading = new Toggle { value = PlayerPrefs.GetInt(RandomHeadingKey, 0) != 0 };
            randomHeading.AddToClassList("picker-option-toggle");
            randomHeading.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetInt(RandomHeadingKey, evt.newValue ? 1 : 0);
                PlayerPrefs.Save();
                RandomHeadingChanged?.Invoke(evt.newValue);
            });
            var randomRow = Row(options, "objects.random_heading");
            LocBindings.Tooltip(randomRow, "objects.random_heading.tooltip");
            LocBindings.Tooltip(randomHeading, "objects.random_heading.tooltip");
            randomRow.Add(randomHeading);
            var hint = LocBindings.NewLabel("objects.hint");
            hint.AddToClassList("panel-info");
            options.Add(hint);
            Add(options);
            placingOptions = options;
            shownOptions = options;

            // The picker beside them.
            var picker = new VisualElement();
            picker.AddToClassList("tool-panel");
            picker.AddToClassList("picker-panel");
            var header = new VisualElement();
            header.AddToClassList("tool-panel-header");
            header.AddToClassList("picker-header");
            var badge = new VisualElement();
            badge.AddToClassList("tool-panel-badge");
            badge.Add(new IconElement("box"));
            header.Add(badge);
            var titles = new VisualElement();
            titles.AddToClassList("tool-panel-titles");
            titleLabel = new Label(Loc.T("objects.title"));
            titleLabel.AddToClassList("tool-panel-title");
            titleLabel.AddToClassList("picker-title");
            titles.Add(titleLabel);
            info = new Label();
            info.AddToClassList("tool-panel-subtitle");
            info.AddToClassList("picker-subtitle");
            titles.Add(info);
            header.Add(titles);
            var searchRow = new VisualElement();
            searchRow.AddToClassList("panel-search");
            searchRow.AddToClassList("picker-search");
            search = new TextField();
            search.AddToClassList("panel-search-field");
            search.textEdition.placeholder = Loc.T("objects.search");
            search.RegisterValueChangedCallback(_ =>
            {
                shownPages = 1;
                searchRefresh?.Pause();
                searchRefresh = schedule.Execute(Refresh).StartingIn(SearchDelayMilliseconds);
            });
            searchRow.Add(search);
            var searchIcon = new IconElement("search");
            searchIcon.AddToClassList("panel-search-icon");
            searchRow.Add(searchIcon);
            header.Add(searchRow);
            foldersButton = new Button(() => FoldersRequested?.Invoke());
            foldersButton.AddToClassList("panel-icon-button");
            foldersButton.Add(new IconElement("folder"));
            header.Add(foldersButton);
            SetFoldersLeftOut(0);
            var close = LocBindings.WithTooltip(new Button(() => CloseRequested?.Invoke()), "objects.close.tooltip");
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            picker.Add(header);

            var tabRow = new VisualElement();
            tabRow.AddToClassList("panel-tabs");
            tabRow.AddToClassList("picker-tabs");
            AddTab(tabRow, Tab.InMap, OmsiObjectCategory.Other, "objects.tab.in_map", "objects.tab.in_map.tooltip");
            AddTab(tabRow, Tab.Favorites, OmsiObjectCategory.Other, "objects.tab.favorites", "objects.tab.favorites.tooltip");
            AddTab(tabRow, Tab.Library, OmsiObjectCategory.Other, "objects.tab.library", "objects.tab.library.tooltip");
            foreach (OmsiObjectCategory each in Enum.GetValues(typeof(OmsiObjectCategory)))
                AddTab(tabRow, Tab.Category, each, CategoryTexts.ObjectNameKey(each), CategoryTexts.ObjectTooltipKey(each));
            picker.Add(tabRow);

            folderBar = new VisualElement();
            folderBar.AddToClassList("panel-folder-bar");
            var back = LocBindings.WithTooltip(new Button(GoUp), "objects.up.tooltip");
            back.AddToClassList("panel-icon-button");
            back.Add(new IconElement("chevron-left"));
            folderBar.Add(back);
            folderLabel = new Label();
            folderLabel.AddToClassList("panel-folder-label");
            folderBar.Add(folderLabel);
            picker.Add(folderBar);

            // The scroll bar always shows, so the tiles keep their places whatever the tab holds.
            tiles = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible, horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            tiles.AddToClassList("picker-tiles");
            tiles.contentContainer.AddToClassList("picker-tiles-content");
            picker.Add(tiles);
            emptyLabel = new Label();
            emptyLabel.AddToClassList("panel-info");
            emptyLabel.AddToClassList("picker-empty");
            picker.Add(emptyLabel);
            Add(picker);

            ShowTab(Tab.InMap, OmsiObjectCategory.Buildings);
        }

        /// <summary>
        /// Shows another tool's options beside the picker, such as the row tool's, with what to do with a chosen object;
        /// null brings back the options for placing objects. <paramref name="idleHint"/> ends the line above the list while nothing is
        /// chosen, in place of the one about placing.
        /// </summary>
        public void UseOptions(VisualElement options, string chosenHint, string idleHint = null)
        {
            var wanted = options ?? placingOptions;
            if (!ReferenceEquals(wanted, shownOptions))
            {
                Remove(shownOptions);
                Insert(0, wanted);
                shownOptions = wanted;
            }
            otherChosenHint = options == null ? null : chosenHint;
            otherIdleHint = options == null ? null : idleHint;
            UpdateInfo();
        }

        /// <summary>An object was clicked; placing it can start.</summary>
        public event Action<string> ObjectChosen;

        public event Action CloseRequested;

        /// <summary>The folder button was pressed: the shell opens the choice of folders (ADR 084).</summary>
        public event Action FoldersRequested;

        private readonly Button foldersButton;
        private int foldersLeftOut;

        /// <summary>Shows on the folder button how many folders of Sceneryobjects are left out; the button is marked while any are.</summary>
        public void SetFoldersLeftOut(int count)
        {
            foldersLeftOut = count;
            foldersButton.tooltip = Loc.T("objects.folders.tooltip", ("count", count));
            foldersButton.EnableInClassList("folders-left-out", count > 0);
        }

        /// <summary>A tile shows an object whose preview is not drawn yet; the shell has the object renderer draw it.</summary>
        public event Action<string> PreviewRequested;

        /// <summary>The user typed a heading for the next object.</summary>
        public event Action<double> HeadingChanged;

        /// <summary>Whether each object put down gets a random heading of its own.</summary>
        public bool RandomHeading => randomHeading.value;

        /// <summary>Ticks or clears the option without storing it, for a run started from the command line.</summary>
        public void SetRandomHeadingQuietly(bool on)
        {
            randomHeading.SetValueWithoutNotify(on);
        }

        /// <summary>The user ticked or cleared the random heading.</summary>
        public event Action<bool> RandomHeadingChanged;

        /// <summary>
        /// The objects to offer: the installation's catalog with every .sco below Sceneryobjects, their groups, kinds, origins
        /// and previews, or null while the installation is still being listed, and how often this map uses each object.
        /// Objects of the map join the library even if their file is missing. Applies the user's DLC marks to the origins.
        /// </summary>
        public void SetCatalog(OmsiInstallationCatalog catalog, IReadOnlyDictionary<string, int> usedCounts)
        {
            library = catalog?.Objects;
            used = usedCounts ?? new Dictionary<string, int>();
            origins = catalog?.Origins;
            origins?.SetMarkedFolders(dlcFolders);
            objectGroups = catalog?.Groups ?? new Dictionary<string, List<string>>();
            categories = catalog?.Categories ?? new Dictionary<string, OmsiObjectCategory>();
            UpdateHideDlcTooltip();
            Refresh();
        }

        private void UpdateHideDlcTooltip()
        {
            var origin = origins == null ? Loc.T("objects.origin.reading")
                : origins.FromSteam ? origins.Description
                : Loc.T("objects.origin.no_steam", ("description", origins.Description));
            hideDlc.tooltip = Loc.T("objects.hide_dlc.tooltip", ("origin", origin));
            hideDlc.parent.tooltip = hideDlc.tooltip;
        }

        /// <summary>The language changed: what the panel wrote from the language files is written again.</summary>
        public void RefreshTexts()
        {
            search.textEdition.placeholder = Loc.T("objects.search");
            UpdateHideDlcTooltip();
            SetFoldersLeftOut(foldersLeftOut);
            Refresh();
        }

        /// <summary>
        /// A preview the object renderer drew; null when the object has no model. The renderer keeps the images, the panel
        /// only shows them on the tiles it has built.
        /// </summary>
        public void SetPreview(string assetPath, Texture2D image)
        {
            if (tileByPath.TryGetValue(assetPath, out var tile))
                ShowImage(tile, image);
        }

        /// <summary>True while a tile of this object is built; the renderer keeps the previews of such objects in memory.</summary>
        public bool ShowsPreview(string assetPath)
        {
            return assetPath != null && tileByPath.ContainsKey(assetPath);
        }

        /// <summary>Shows the heading the next object is placed with, such as after R turned it.</summary>
        public void ShowHeading(double heading)
        {
            headingField.SetValueWithoutNotify(heading.ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>Placing ended; no object is highlighted any more.</summary>
        public void ClearChosen()
        {
            if (chosen != null && tileByPath.TryGetValue(chosen, out var tile))
                tile.RemoveFromClassList("picker-tile-selected");
            chosen = null;
            UpdateInfo();
        }

        public void FocusSearch()
        {
            search.Focus();
        }

        /// <summary>
        /// Opens a tab by name (inmap, favorites, library or the name of a kind such as Trees) with a folder of the library
        /// and search terms, so a check from the command line can show a state without clicks. Unknown names are ignored.
        /// </summary>
        public void ShowState(string tabName, string location, string searchText)
        {
            var kind = tab;
            var each = category;
            if (tabName != null)
            {
                if (Enum.TryParse<Tab>(tabName, true, out var named) && named != Tab.Category)
                    kind = named;
                foreach (OmsiObjectCategory candidate in Enum.GetValues(typeof(OmsiObjectCategory)))
                    if (string.Equals(OmsiObjectCategories.Name(candidate), tabName, StringComparison.OrdinalIgnoreCase))
                    {
                        kind = Tab.Category;
                        each = candidate;
                    }
            }
            if (!string.IsNullOrEmpty(location))
                folder = location.Replace('/', Path.DirectorySeparatorChar);
            if (searchText != null)
                search.SetValueWithoutNotify(searchText);
            ShowTab(kind, each);
        }

        private static VisualElement Row(VisualElement parent, string labelKey)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = LocBindings.NewLabel(labelKey); // i18n:keys
            text.AddToClassList("picker-option-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }

        private void AddTab(VisualElement row, Tab kind, OmsiObjectCategory each, string textKey, string tooltipKey)
        {
            var button = LocBindings.WithTooltip(LocBindings.NewButton(textKey, () => // i18n:keys
            {
                search.SetValueWithoutNotify(string.Empty);
                ShowTab(kind, each);
            }), tooltipKey); // i18n:keys
            button.AddToClassList("panel-tab");
            button.AddToClassList("picker-tab");
            tabButtons.Add(button, new KeyValuePair<Tab, OmsiObjectCategory>(kind, each));
            row.Add(button);
        }

        private void ShowTab(Tab kind, OmsiObjectCategory each)
        {
            tab = kind;
            category = each;
            shownPages = 1;
            Refresh();
        }

        private void GoUp()
        {
            var parent = Path.GetDirectoryName(folder);
            folder = string.IsNullOrEmpty(parent) ? RootFolder : parent;
            shownPages = 1;
            Refresh();
        }

        private bool IsDlc(string path)
        {
            return origins != null && origins.Of(path) == OmsiContentOrigin.Dlc;
        }

        private bool IsBaseGame(string path)
        {
            return origins != null && origins.Of(path) == OmsiContentOrigin.BaseGame;
        }

        /// <summary>DLC objects leave every tab but the map's own list while the toggle is on.</summary>
        private bool Hidden(string path)
        {
            return hideDlc.value && tab != Tab.InMap && IsDlc(path);
        }

        /// <summary>Rebuilds the tiles for the tab, folder and search terms.</summary>
        private void Refresh()
        {
            entries.Clear();
            var terms = (search.value ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var browsing = tab == Tab.Library && terms.Length == 0;
            foreach (var pair in tabButtons)
                pair.Key.EnableInClassList("panel-tab-active", terms.Length == 0 && pair.Value.Key == tab && (tab != Tab.Category || pair.Value.Value == category));
            folderBar.style.display = browsing && !string.Equals(folder, RootFolder, StringComparison.OrdinalIgnoreCase) ? DisplayStyle.Flex : DisplayStyle.None;
            folderLabel.text = folder.Replace(Path.DirectorySeparatorChar.ToString(), "  ›  ");

            if (browsing)
                ListFolder();
            else
            {
                var candidates = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                if (terms.Length > 0 || tab == Tab.Library)
                {
                    if (library != null)
                        candidates.UnionWith(library);
                    candidates.UnionWith(used.Keys);
                }
                else if (tab == Tab.InMap)
                    candidates.UnionWith(used.Keys);
                else if (tab == Tab.Favorites)
                    candidates.UnionWith(favorites);
                else if (library != null)
                    foreach (var path in library)
                        if (categories.TryGetValue(path, out var each) && each == category)
                            candidates.Add(path);
                var ordered = new List<string>(candidates);
                // The map's objects come most used first; elsewhere objects follow their names, whatever folder they come from.
                if (tab == Tab.InMap && terms.Length == 0)
                    ordered.Sort((first, second) => Uses(second).CompareTo(Uses(first)));
                else
                    ordered.Sort((first, second) =>
                    {
                        var order = string.Compare(Path.GetFileName(first), Path.GetFileName(second), StringComparison.OrdinalIgnoreCase);
                        return order != 0 ? order : string.Compare(first, second, StringComparison.OrdinalIgnoreCase);
                    });
                foreach (var candidate in ordered)
                    if ((terms.Length == 0 || Matches(candidate, terms) || GroupsMatch(candidate, terms)) && !Hidden(candidate))
                        entries.Add(ObjectEntry(candidate));
            }
            BuildTiles();
            UpdateInfo();
        }

        private int Uses(string path)
        {
            return used.TryGetValue(path, out var count) ? count : 0;
        }

        /// <summary>The subfolders and objects directly in the current folder, folders first.</summary>
        private void ListFolder()
        {
            var prefix = folder + Path.DirectorySeparatorChar;
            var subfolders = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var objects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            // Subfolders with at least one object that does not come with the base game; only these can be marked as DLC.
            var withOwnContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string path)
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Hidden(path))
                    return;
                var rest = path.Substring(prefix.Length);
                var separator = rest.IndexOf(Path.DirectorySeparatorChar);
                if (separator < 0)
                {
                    objects.Add(path);
                    return;
                }
                var name = rest.Substring(0, separator);
                subfolders.TryGetValue(name, out var count);
                subfolders[name] = count + 1;
                if (!IsBaseGame(path))
                    withOwnContent.Add(name);
            }
            if (library != null)
                foreach (var path in library)
                    Visit(path);
            // Objects of the map whose file the installation lacks still show, but none counts twice.
            foreach (var path in used.Keys)
                if (library == null || !ContainsPath(library, path))
                    Visit(path);

            foreach (var pair in subfolders)
            {
                var path = prefix + pair.Key;
                var baseGame = !withOwnContent.Contains(pair.Key);
                // A mark on a base game folder changes nothing, since the base game wins; it is not shown either.
                var marked = !baseGame && IsMarkedFolder(path);
                entries.Add(new Entry { IsFolder = true, Path = path, Name = pair.Key, Count = pair.Value, IsDlc = marked, IsMarked = marked, IsBaseGame = baseGame });
            }
            foreach (var path in objects)
                entries.Add(ObjectEntry(path));
        }

        /// <summary>True when every search term appears in the object's path or one of its groups.</summary>
        private bool GroupsMatch(string path, string[] terms)
        {
            if (!objectGroups.TryGetValue(path, out var names))
                return false;
            return Matches(path + "\n" + string.Join("\n", names), terms);
        }

        private static bool ContainsPath(IReadOnlyList<string> paths, string path)
        {
            // The library is sorted case-insensitively by the scan.
            var low = 0;
            var high = paths.Count - 1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                var order = string.Compare(paths[middle], path, StringComparison.OrdinalIgnoreCase);
                if (order == 0)
                    return true;
                if (order < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return false;
        }

        private Entry ObjectEntry(string path)
        {
            return new Entry
            {
                Path = path, Name = Path.GetFileNameWithoutExtension(path), Count = Uses(path), IsDlc = IsDlc(path),
                IsMarked = origins != null && origins.IsMarkedByHand(path)
            };
        }

        /// <summary>Builds the tiles of the current entries, a page at a time.</summary>
        private void BuildTiles()
        {
            tiles.Clear();
            tileByPath.Clear();
            AddTiles(0);
            emptyLabel.style.display = entries.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Adds the tiles from an entry on up to the pages shown, and a last tile for the next page. That tile only adds
        /// its page: the tiles before it stay as they are, so a long list is not built again with every page.
        /// </summary>
        private void AddTiles(int from)
        {
            var limit = Math.Min(entries.Count, PageSize * shownPages);
            for (var index = from; index < limit; index++)
                tiles.Add(entries[index].IsFolder ? FolderTile(entries[index]) : ObjectTile(entries[index]));
            if (limit >= entries.Count)
                return;
            Button more = null;
            more = new Button(() =>
            {
                more.RemoveFromHierarchy();
                shownPages++;
                AddTiles(limit);
            }) { text = Loc.T("objects.more", ("n", Math.Min(PageSize, entries.Count - limit)), ("total", entries.Count - limit)), tooltip = Loc.T("objects.more.tooltip") };
            more.AddToClassList("picker-tile");
            more.AddToClassList("picker-more");
            tiles.Add(more);
        }

        private VisualElement ObjectTile(Entry entry)
        {
            var path = entry.Path;
            var tile = new VisualElement
            {
                tooltip = objectGroups.TryGetValue(path, out var names) ? Loc.T("objects.tile.groups", ("path", path), ("groups", string.Join(", ", names))) : path
            };
            tile.AddToClassList("picker-tile");
            tile.AddToClassList("object-tile");
            if (string.Equals(path, chosen, StringComparison.OrdinalIgnoreCase))
                tile.AddToClassList("picker-tile-selected");
            var preview = new VisualElement { name = "preview", pickingMode = PickingMode.Ignore };
            preview.AddToClassList("picker-tile-preview");
            preview.AddToClassList("object-tile-preview");
            tile.Add(preview);
            // Registered first: a preview kept from before arrives while the request is still being made.
            tileByPath[path] = tile;
            // Until the renderer has drawn it, a spinner shows that something is happening.
            var spinner = new SpinnerElement { name = "spinner" };
            spinner.AddToClassList("object-tile-spinner");
            preview.Add(spinner);
            PreviewRequested?.Invoke(path);
            var name = new Label(entry.Name) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            if (entry.Count > 0)
            {
                var count = new Label(entry.Count.ToString(CultureInfo.InvariantCulture)) { tooltip = Loc.T("objects.tile.count", ("count", entry.Count)) };
                count.AddToClassList("picker-tile-count");
                tile.Add(count);
            }
            if (entry.IsDlc)
            {
                var tag = new Label(Loc.T("objects.dlc"))
                {
                    tooltip = Loc.T(entry.IsMarked ? "objects.dlc.tooltip_marked" : "objects.dlc.tooltip") // i18n:keys
                };
                tag.AddToClassList("picker-tile-tag");
                tile.Add(tag);
            }
            var star = new Button { name = "star" };
            star.AddToClassList("panel-icon-button");
            star.AddToClassList("favorite-button");
            star.AddToClassList("picker-tile-star");
            ShowFavorite(star, favorites.Contains(path));
            star.Add(new IconElement("star"));
            star.clicked += () => ToggleFavorite(path);
            tile.Add(star);
            tile.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement target && (target == star || star.Contains(target)))
                    return;
                Choose(path);
            });
            tileByPath[path] = tile;
            return tile;
        }

        private VisualElement FolderTile(Entry entry)
        {
            var tile = new VisualElement
            {
                tooltip = Loc.T("objects.folder.tooltip", ("name", entry.Name), ("baseGame", entry.IsBaseGame ? "yes" : "no"))
            };
            tile.AddToClassList("picker-tile");
            tile.AddToClassList("object-tile");
            var preview = new VisualElement { pickingMode = PickingMode.Ignore };
            preview.AddToClassList("picker-tile-preview");
            preview.AddToClassList("object-tile-preview");
            preview.AddToClassList("object-folder-preview");
            var icon = new IconElement("folder") { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("object-tile-icon");
            preview.Add(icon);
            var count = new Label(Loc.T("objects.folder.count", ("count", entry.Count))) { pickingMode = PickingMode.Ignore };
            count.AddToClassList("object-folder-count");
            preview.Add(count);
            tile.Add(preview);
            var name = new Label(entry.Name) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            if (entry.IsDlc)
            {
                var tag = new Label(Loc.T("objects.dlc")) { tooltip = Loc.T("objects.folder.dlc_tooltip") };
                tag.AddToClassList("picker-tile-tag");
                tile.Add(tag);
            }
            // Folders can be marked as DLC, for add-ons bought outside Steam that look like user content otherwise.
            Button mark = null;
            if (!entry.IsBaseGame)
            {
                mark = new Button
                {
                    tooltip = Loc.T(entry.IsMarked ? "objects.folder.unmark" : "objects.folder.mark") // i18n:keys
                };
                mark.AddToClassList("panel-icon-button");
                mark.AddToClassList("dlc-mark-button");
                mark.AddToClassList("picker-tile-star");
                mark.EnableInClassList("dlc-mark-on", entry.IsMarked);
                mark.Add(new IconElement("tag"));
                var path = entry.Path;
                mark.clicked += () => ToggleDlcFolder(path);
                tile.Add(mark);
            }
            tile.RegisterCallback<ClickEvent>(evt =>
            {
                if (mark != null && evt.target is VisualElement target && (target == mark || mark.Contains(target)))
                    return;
                folder = entry.Path;
                shownPages = 1;
                Refresh();
            });
            return tile;
        }

        /// <summary>Shows a drawn preview on a tile, or a box for an object without a model to draw.</summary>
        private static void ShowImage(VisualElement tile, Texture2D image)
        {
            var preview = tile.Q<VisualElement>("preview");
            tile.Q<SpinnerElement>("spinner")?.RemoveFromHierarchy();
            if (image != null)
            {
                preview.style.backgroundImage = new StyleBackground(image);
                return;
            }
            if (preview.Q<IconElement>("icon") != null)
                return;
            var icon = new IconElement("box") { name = "icon", tooltip = Loc.T("objects.no_preview") };
            icon.AddToClassList("object-tile-icon");
            preview.Add(icon);
        }

        private void Choose(string path)
        {
            if (chosen != null && tileByPath.TryGetValue(chosen, out var previous))
                previous.RemoveFromClassList("picker-tile-selected");
            chosen = path;
            if (tileByPath.TryGetValue(path, out var tile))
                tile.AddToClassList("picker-tile-selected");
            UpdateInfo();
            ObjectChosen?.Invoke(path);
        }

        /// <summary>How many objects of the map, and how many placements of them, belong to a DLC.</summary>
        private bool DlcUse(out int kinds, out int placements)
        {
            kinds = 0;
            placements = 0;
            foreach (var pair in used)
            {
                if (!IsDlc(pair.Key))
                    continue;
                kinds++;
                placements += pair.Value;
            }
            return kinds > 0;
        }

        private static bool Matches(string path, string[] terms)
        {
            foreach (var term in terms)
                if (path.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            return true;
        }

        private void UpdateInfo()
        {
            var folderCount = entries.FindAll(entry => entry.IsFolder).Count;
            var objectCount = entries.Count - folderCount;
            var usesDlc = DlcUse(out var kinds, out var placements);
            var dlcWarning = chosen == null && tab == Tab.InMap && usesDlc;
            titleLabel.text = chosen != null ? Path.GetFileNameWithoutExtension(chosen) : Loc.T("objects.title");
            titleLabel.tooltip = chosen;
            info.EnableInClassList("panel-info-warning", dlcWarning);
            if (chosen != null)
                info.text = otherChosenHint ?? Loc.T("objects.hint.chosen");
            else if (dlcWarning)
                info.text = Loc.T("objects.dlc_warning", ("kinds", kinds), ("placements", placements));
            else
                info.text = Loc.T("objects.info", ("folders", folderCount), ("objects", objectCount), ("hasHint", otherIdleHint != null ? "yes" : "no"), ("hint", otherIdleHint ?? string.Empty));
            info.tooltip = info.text;
            emptyLabel.text = Loc.T(library == null && tab != Tab.InMap ? "objects.empty.listing" // i18n:keys
                : tab == Tab.Favorites && favorites.Count == 0 ? "objects.empty.favorites"
                : tab == Tab.InMap && used.Count == 0 ? "objects.empty.in_map"
                : "objects.empty.none"); // i18n:keys
        }

        private static void ShowFavorite(Button star, bool favorite)
        {
            star.tooltip = Loc.T(favorite ? "objects.favorite.remove" : "objects.favorite.add"); // i18n:keys
            star.EnableInClassList("favorite-on", favorite);
        }

        private void ToggleFavorite(string path)
        {
            if (!favorites.Remove(path))
                favorites.Add(path);
            SaveFavorites();
            // Only the list of favourites changes with a star; elsewhere the tile shows it and the list stays as it is.
            var listsFavorites = tab == Tab.Favorites && string.IsNullOrWhiteSpace(search.value);
            if (listsFavorites || !tileByPath.TryGetValue(path, out var tile))
                Refresh();
            else
                ShowFavorite(tile.Q<Button>("star"), favorites.Contains(path));
        }

        private bool IsMarkedFolder(string path)
        {
            if (dlcFolders.Contains(path))
                return true;
            foreach (var folderPath in dlcFolders)
                if (path.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private void ToggleDlcFolder(string path)
        {
            if (!dlcFolders.Remove(path))
                dlcFolders.Add(path);
            PlayerPrefs.SetString(DlcFoldersKey, string.Join("\n", dlcFolders));
            PlayerPrefs.Save();
            origins?.SetMarkedFolders(dlcFolders);
            Refresh();
        }

        private void LoadDlcFolders()
        {
            dlcFolders.Clear();
            var stored = PlayerPrefs.GetString(DlcFoldersKey, string.Empty);
            foreach (var path in stored.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                dlcFolders.Add(path);
        }

        private void LoadFavorites()
        {
            favorites.Clear();
            var stored = PlayerPrefs.GetString(FavoritesKey, string.Empty);
            foreach (var path in stored.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                favorites.Add(path);
        }

        private void SaveFavorites()
        {
            PlayerPrefs.SetString(FavoritesKey, string.Join("\n", favorites));
            PlayerPrefs.Save();
        }
    }
}
