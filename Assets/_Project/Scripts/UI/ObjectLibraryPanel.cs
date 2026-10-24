using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        private const string FavoritesKey = "Mapforge.ObjectFavorites";
        private const string HideDlcKey = "Mapforge.HideDlcObjects";
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
        private readonly Dictionary<string, VisualElement> tileByPath = new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> rendered = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

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

        public ObjectLibraryPanel()
        {
            AddToClassList("picker-tool");
            LoadFavorites();
            LoadDlcFolders();

            // The options on the left.
            var options = new VisualElement();
            options.AddToClassList("tool-panel");
            options.AddToClassList("picker-options");
            var optionsTitle = new Label("Options");
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
            Row(options, "Hide DLC objects").Add(hideDlc);
            var headingRow = Row(options, "Heading (°)");
            headingField = new TextField { value = "0", tooltip = "The heading the next object is placed with; R turns it by 15°, Shift+R back." };
            headingField.AddToClassList("picker-field");
            headingField.RegisterValueChangedCallback(evt =>
            {
                if (double.TryParse(evt.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var heading) && !double.IsInfinity(heading))
                    HeadingChanged?.Invoke(heading);
            });
            headingRow.tooltip = headingField.tooltip;
            headingRow.Add(headingField);
            var hint = new Label("Shift+click keeps placing, R turns the object, Alt places a junction freely beside a road, Esc stops.");
            hint.AddToClassList("panel-info");
            options.Add(hint);
            Add(options);

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
            titleLabel = new Label("Objects");
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
            search.textEdition.placeholder = "Search all objects...";
            search.RegisterValueChangedCallback(_ =>
            {
                shownPages = 1;
                Refresh();
            });
            searchRow.Add(search);
            var searchIcon = new IconElement("search");
            searchIcon.AddToClassList("panel-search-icon");
            searchRow.Add(searchIcon);
            header.Add(searchRow);
            var close = new Button(() => CloseRequested?.Invoke()) { tooltip = "Close (3 or Esc)" };
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            picker.Add(header);

            var tabRow = new VisualElement();
            tabRow.AddToClassList("panel-tabs");
            tabRow.AddToClassList("picker-tabs");
            AddTab(tabRow, Tab.InMap, OmsiObjectCategory.Other, "In map", "Objects this map already uses, with how often");
            AddTab(tabRow, Tab.Favorites, OmsiObjectCategory.Other, "Favorites", "Objects you marked with the star");
            AddTab(tabRow, Tab.Library, OmsiObjectCategory.Other, "Library", "All scenery objects of the OMSI installation, by folder");
            foreach (OmsiObjectCategory each in Enum.GetValues(typeof(OmsiObjectCategory)))
                AddTab(tabRow, Tab.Category, each, OmsiObjectCategories.Name(each), TabTooltip(each));
            picker.Add(tabRow);

            folderBar = new VisualElement();
            folderBar.AddToClassList("panel-folder-bar");
            var back = new Button(GoUp) { tooltip = "Up one folder" };
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

        /// <summary>An object was clicked; placing it can start.</summary>
        public event Action<string> ObjectChosen;

        public event Action CloseRequested;

        /// <summary>A tile shows an object whose preview is not drawn yet; the shell has the object renderer draw it.</summary>
        public event Action<string> PreviewRequested;

        /// <summary>The user typed a heading for the next object.</summary>
        public event Action<double> HeadingChanged;

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
            hideDlc.tooltip = "Hide objects that belong to a DLC, so the map needs only the base game and freeware. " +
                              (origins == null ? "The origin of the files is still being read."
                                  : origins.FromSteam ? origins.Description
                                  : origins.Description + " Without Steam's lists only folders marked as DLC can be told apart.") +
                              " Folders of add-ons bought outside Steam can be marked as DLC with the tag button in Library.";
            hideDlc.parent.tooltip = hideDlc.tooltip;
            Refresh();
        }

        /// <summary>A preview the object renderer drew; null when the object has no model.</summary>
        public void SetPreview(string assetPath, Texture2D image)
        {
            rendered[assetPath] = image;
            if (tileByPath.TryGetValue(assetPath, out var tile))
                ShowImage(tile, image);
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

        private static VisualElement Row(VisualElement parent, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = new Label(label);
            text.AddToClassList("picker-option-label");
            row.Add(text);
            parent.Add(row);
            return row;
        }

        private void AddTab(VisualElement row, Tab kind, OmsiObjectCategory each, string text, string tooltip)
        {
            var button = new Button(() =>
            {
                search.SetValueWithoutNotify(string.Empty);
                ShowTab(kind, each);
            }) { text = text, tooltip = tooltip };
            button.AddToClassList("panel-tab");
            button.AddToClassList("picker-tab");
            tabButtons.Add(button, new KeyValuePair<Tab, OmsiObjectCategory>(kind, each));
            row.Add(button);
        }

        private static string TabTooltip(OmsiObjectCategory each)
        {
            switch (each)
            {
                case OmsiObjectCategory.Buildings: return "Houses, shops, industry, depots and other buildings";
                case OmsiObjectCategory.Trees: return "Trees, bushes and other plants";
                case OmsiObjectCategory.Junctions: return "Objects with connection points for roads, such as crossings";
                case OmsiObjectCategory.Signs: return "Traffic signs, traffic lights and signals";
                case OmsiObjectCategory.Stops: return "Bus and tram stops with shelters and poles";
                case OmsiObjectCategory.Lights: return "Street lights, masts and poles";
                case OmsiObjectCategory.Rail: return "Railway objects";
                default: return "Everything else";
            }
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
            var limit = Math.Min(entries.Count, PageSize * shownPages);
            for (var index = 0; index < limit; index++)
                tiles.Add(entries[index].IsFolder ? FolderTile(entries[index]) : ObjectTile(entries[index]));
            if (limit < entries.Count)
            {
                var more = new Button(() =>
                {
                    shownPages++;
                    BuildTiles();
                }) { text = "Show " + Math.Min(PageSize, entries.Count - limit) + " more of " + (entries.Count - limit), tooltip = "Show more objects" };
                more.AddToClassList("picker-tile");
                more.AddToClassList("picker-more");
                tiles.Add(more);
            }
            emptyLabel.style.display = entries.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement ObjectTile(Entry entry)
        {
            var path = entry.Path;
            var tile = new VisualElement
            {
                tooltip = objectGroups.TryGetValue(path, out var names) ? path + "\nGroups: " + string.Join(", ", names) : path
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
            if (rendered.TryGetValue(path, out var image))
                ShowImage(tile, image);
            else
            {
                // Until the renderer has drawn it, a spinner shows that something is happening.
                var spinner = new SpinnerElement { name = "spinner" };
                spinner.AddToClassList("object-tile-spinner");
                preview.Add(spinner);
                PreviewRequested?.Invoke(path);
            }
            var name = new Label(entry.Name) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            if (entry.Count > 0)
            {
                var count = new Label(entry.Count.ToString(CultureInfo.InvariantCulture)) { tooltip = entry.Count + "× in this map" };
                count.AddToClassList("picker-tile-count");
                tile.Add(count);
            }
            if (entry.IsDlc)
            {
                var tag = new Label("DLC")
                {
                    tooltip = entry.IsMarked
                        ? "Marked as DLC by hand: players without that add-on do not see this object."
                        : "Part of a DLC: players without it do not see this object."
                };
                tag.AddToClassList("picker-tile-tag");
                tile.Add(tag);
            }
            var star = new Button { tooltip = favorites.Contains(path) ? "Remove from favorites" : "Add to favorites" };
            star.AddToClassList("panel-icon-button");
            star.AddToClassList("favorite-button");
            star.AddToClassList("picker-tile-star");
            star.EnableInClassList("favorite-on", favorites.Contains(path));
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
                tooltip = "Open the folder " + entry.Name +
                          (entry.IsBaseGame ? "\nPart of the base game: every player has it, so it cannot be marked as DLC." : string.Empty)
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
            var count = new Label(entry.Count + (entry.Count == 1 ? " object" : " objects")) { pickingMode = PickingMode.Ignore };
            count.AddToClassList("object-folder-count");
            preview.Add(count);
            tile.Add(preview);
            var name = new Label(entry.Name) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            if (entry.IsDlc)
            {
                var tag = new Label("DLC") { tooltip = "Marked as DLC by hand: players without that add-on do not see these objects." };
                tag.AddToClassList("picker-tile-tag");
                tile.Add(tag);
            }
            // Folders can be marked as DLC, for add-ons bought outside Steam that look like user content otherwise.
            Button mark = null;
            if (!entry.IsBaseGame)
            {
                mark = new Button
                {
                    tooltip = entry.IsMarked
                        ? "Marked as DLC by hand; click to count this folder as user content again"
                        : "Mark this folder as DLC, for add-ons bought outside Steam"
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
            var icon = new IconElement("box") { name = "icon", tooltip = "No model to preview" };
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

        private static string Count(int count, string noun)
        {
            return count + " " + noun + (count == 1 ? string.Empty : "s");
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
            titleLabel.text = chosen != null ? Path.GetFileNameWithoutExtension(chosen) : "Objects";
            titleLabel.tooltip = chosen;
            info.EnableInClassList("panel-info-warning", dlcWarning);
            if (chosen != null)
                info.text = "Click into the map to place it; Shift+click keeps placing, R turns it, Esc stops.";
            else if (dlcWarning)
                info.text = "This map uses " + Count(kinds, "DLC object") + " in " + Count(placements, "placement") +
                            "; players without that DLC miss them.";
            else
                info.text = (folderCount > 0 ? Count(folderCount, "folder") + ", " : string.Empty) + Count(objectCount, "object") +
                            ". Click an object, then click into the map to place it.";
            info.tooltip = info.text;
            emptyLabel.text = library == null && tab != Tab.InMap ? "Listing the scenery objects of the OMSI installation..."
                : tab == Tab.Favorites && favorites.Count == 0 ? "Mark objects with the star to find them here."
                : tab == Tab.InMap && used.Count == 0 ? "This map uses no scenery objects yet."
                : "No object matches.";
        }

        private void ToggleFavorite(string path)
        {
            if (!favorites.Remove(path))
                favorites.Add(path);
            SaveFavorites();
            Refresh();
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
