using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The panel of the Objects tool: scenery objects of the OMSI installation by folder, the ones this map uses and the
    /// user's favourites, with a search across all of them. A click on an object reports it so placing can start; the
    /// panel stays open for the next one. Favourites are a user preference, kept across maps and sessions.
    /// </summary>
    internal sealed class ObjectLibraryPanel : VisualElement
    {
        private enum Tab { Library, InMap, Favorites }

        private const string FavoritesKey = "Mapforge.ObjectFavorites";
        private const string RootFolder = "Sceneryobjects";

        private sealed class Entry
        {
            public bool IsFolder;
            public string Path;
            public string Name;
            public string Detail;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<Tab, Button> tabs = new Dictionary<Tab, Button>();
        private readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly TextField search;
        private readonly VisualElement folderBar;
        private readonly Label folderLabel;
        private readonly ListView list;
        private readonly Label info;

        private IReadOnlyList<string> library;
        private IReadOnlyDictionary<string, int> used = new Dictionary<string, int>();
        private Tab tab = Tab.Library;
        private string folder = RootFolder;
        private string chosen;

        public ObjectLibraryPanel()
        {
            AddToClassList("tool-panel");
            LoadFavorites();

            var header = new VisualElement();
            header.AddToClassList("tool-panel-header");
            var badge = new VisualElement();
            badge.AddToClassList("tool-panel-badge");
            badge.Add(new IconElement("box") { name = "badge-icon" });
            header.Add(badge);
            var titles = new VisualElement();
            titles.AddToClassList("tool-panel-titles");
            var title = new Label("Objects");
            title.AddToClassList("tool-panel-title");
            titles.Add(title);
            var subtitle = new Label("Place scenery objects");
            subtitle.AddToClassList("tool-panel-subtitle");
            titles.Add(subtitle);
            header.Add(titles);
            var close = new Button(() => CloseRequested?.Invoke()) { tooltip = "Close (3 or Esc)" };
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            Add(header);

            var tabRow = new VisualElement();
            tabRow.AddToClassList("panel-tabs");
            AddTab(tabRow, Tab.Library, "Library", "All scenery objects of the OMSI installation, by folder");
            AddTab(tabRow, Tab.InMap, "In map", "Objects this map already uses");
            AddTab(tabRow, Tab.Favorites, "Favorites", "Objects you marked with the star");
            Add(tabRow);

            var searchRow = new VisualElement();
            searchRow.AddToClassList("panel-search");
            search = new TextField();
            search.AddToClassList("panel-search-field");
            search.textEdition.placeholder = "Search objects...";
            search.RegisterValueChangedCallback(_ => Refresh());
            searchRow.Add(search);
            var searchIcon = new IconElement("search");
            searchIcon.AddToClassList("panel-search-icon");
            searchRow.Add(searchIcon);
            Add(searchRow);

            folderBar = new VisualElement();
            folderBar.AddToClassList("panel-folder-bar");
            var back = new Button(GoUp) { tooltip = "Up one folder" };
            back.AddToClassList("panel-icon-button");
            back.Add(new IconElement("chevron-left"));
            folderBar.Add(back);
            folderLabel = new Label();
            folderLabel.AddToClassList("panel-folder-label");
            folderBar.Add(folderLabel);
            Add(folderBar);

            list = new ListView
            {
                fixedItemHeight = 38,
                selectionType = SelectionType.Single,
                itemsSource = entries,
                makeItem = MakeRow,
                bindItem = BindRow
            };
            list.AddToClassList("panel-list");
            list.selectionChanged += _ => OnEntrySelected();
            Add(list);

            info = new Label();
            info.AddToClassList("panel-info");
            Add(info);

            ShowTab(Tab.Library);
        }

        /// <summary>An object was clicked; placing it can start.</summary>
        public event Action<string> ObjectChosen;

        public event Action CloseRequested;

        /// <summary>
        /// The objects to offer: every .sco below Sceneryobjects, or null while the installation is still being listed,
        /// and how often this map uses each object. Objects of the map join the library even if their file is missing.
        /// </summary>
        public void SetCatalog(IReadOnlyList<string> installationObjects, IReadOnlyDictionary<string, int> usedCounts)
        {
            library = installationObjects;
            used = usedCounts ?? new Dictionary<string, int>();
            Refresh();
        }

        /// <summary>Placing ended; no object is highlighted any more.</summary>
        public void ClearChosen()
        {
            chosen = null;
            list.ClearSelection();
        }

        public void FocusSearch()
        {
            search.Focus();
        }

        private void AddTab(VisualElement row, Tab kind, string text, string description)
        {
            var button = new Button(() => ShowTab(kind)) { text = text, tooltip = description };
            button.AddToClassList("panel-tab");
            tabs[kind] = button;
            row.Add(button);
        }

        private void ShowTab(Tab kind)
        {
            tab = kind;
            foreach (var pair in tabs)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == kind);
            Refresh();
        }

        private void GoUp()
        {
            var parent = Path.GetDirectoryName(folder);
            folder = string.IsNullOrEmpty(parent) ? RootFolder : parent;
            Refresh();
        }

        /// <summary>Rebuilds the list for the tab, folder and search terms.</summary>
        private void Refresh()
        {
            entries.Clear();
            var terms = (search.value ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var browsing = tab == Tab.Library && terms.Length == 0;
            folderBar.style.display = browsing && !string.Equals(folder, RootFolder, StringComparison.OrdinalIgnoreCase)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            folderLabel.text = folder.Replace(Path.DirectorySeparatorChar.ToString(), "  ›  ");

            if (browsing)
                ListFolder();
            else
            {
                var candidates = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                if (tab == Tab.Library)
                {
                    if (library != null)
                        candidates.UnionWith(library);
                    candidates.UnionWith(used.Keys);
                }
                else if (tab == Tab.InMap)
                    candidates.UnionWith(used.Keys);
                else
                    candidates.UnionWith(favorites);
                foreach (var candidate in candidates)
                    if (Matches(candidate, terms))
                        entries.Add(ObjectEntry(candidate, true));
            }

            list.RefreshItems();
            var index = chosen == null ? -1 : entries.FindIndex(entry => !entry.IsFolder && string.Equals(entry.Path, chosen, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                list.SetSelectionWithoutNotify(new[] { index });
            else
                list.ClearSelection();
            UpdateInfo();
        }

        /// <summary>The subfolders and objects directly in the current folder, folders first.</summary>
        private void ListFolder()
        {
            var prefix = folder + Path.DirectorySeparatorChar;
            var subfolders = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var objects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string path)
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
            }
            if (library != null)
                foreach (var path in library)
                    Visit(path);
            // Objects of the map whose file the installation lacks still show, but none counts twice.
            foreach (var path in used.Keys)
                if (library == null || !ContainsPath(library, path))
                    Visit(path);

            foreach (var pair in subfolders)
                entries.Add(new Entry { IsFolder = true, Path = prefix + pair.Key, Name = pair.Key, Detail = Count(pair.Value, "object") });
            foreach (var path in objects)
                entries.Add(ObjectEntry(path, false));
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

        private Entry ObjectEntry(string path, bool showFolder)
        {
            used.TryGetValue(path, out var count);
            var location = Path.GetDirectoryName(path) ?? string.Empty;
            if (location.StartsWith(RootFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                location = location.Substring(RootFolder.Length + 1);
            var detail = showFolder ? location : string.Empty;
            if (count > 0)
                detail += (detail.Length > 0 ? "  •  " : string.Empty) + count + "× in this map";
            return new Entry { Path = path, Name = Path.GetFileNameWithoutExtension(path), Detail = detail };
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
            if (chosen != null)
                info.text = "Click into the map to place " + Path.GetFileNameWithoutExtension(chosen) +
                            ". Shift+click keeps placing, R turns it, Esc stops.";
            else if (tab == Tab.Library && library == null)
                info.text = "Listing the scenery objects of the OMSI installation...";
            else if (tab == Tab.Favorites && favorites.Count == 0)
                info.text = "Mark objects with the star to find them here.";
            else
                info.text = (folderCount > 0 ? Count(folderCount, "folder") + ", " : string.Empty) + Count(objectCount, "object") +
                            ". Click an object, then click into the map to place it.";
        }

        private void OnEntrySelected()
        {
            var index = list.selectedIndex;
            if (index < 0 || index >= entries.Count)
                return;
            var entry = entries[index];
            if (entry.IsFolder)
            {
                folder = entry.Path;
                Refresh();
                return;
            }
            chosen = entry.Path;
            UpdateInfo();
            ObjectChosen?.Invoke(entry.Path);
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("panel-row");
            var icon = new IconElement("box") { name = "row-icon" };
            icon.AddToClassList("panel-row-icon");
            row.Add(icon);
            var texts = new VisualElement();
            texts.AddToClassList("panel-row-texts");
            var name = new Label { name = "row-name" };
            name.AddToClassList("panel-row-name");
            texts.Add(name);
            var detail = new Label { name = "row-detail" };
            detail.AddToClassList("panel-row-detail");
            texts.Add(detail);
            row.Add(texts);
            var star = new Button { name = "row-star", tooltip = "Add to or remove from favorites" };
            star.AddToClassList("panel-icon-button");
            star.AddToClassList("favorite-button");
            star.Add(new IconElement("star"));
            // The star must not select the row, which would start placing.
            star.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            star.clicked += () =>
            {
                if (star.userData is string path)
                    ToggleFavorite(path);
            };
            row.Add(star);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var entry = entries[index];
            row.Q<IconElement>("row-icon").SetIcon(entry.IsFolder ? "folder" : "box");
            row.Q<Label>("row-name").text = entry.Name;
            var detail = row.Q<Label>("row-detail");
            detail.text = entry.Detail;
            detail.style.display = string.IsNullOrEmpty(entry.Detail) ? DisplayStyle.None : DisplayStyle.Flex;
            var star = row.Q<Button>("row-star");
            star.userData = entry.IsFolder ? null : entry.Path;
            star.style.visibility = entry.IsFolder ? Visibility.Hidden : Visibility.Visible;
            star.EnableInClassList("favorite-on", !entry.IsFolder && favorites.Contains(entry.Path));
            row.tooltip = entry.IsFolder ? "Open the folder " + entry.Name : entry.Path;
        }

        private void ToggleFavorite(string path)
        {
            if (!favorites.Remove(path))
                favorites.Add(path);
            SaveFavorites();
            Refresh();
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
