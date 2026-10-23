using System;
using System.Collections.Generic;
using System.IO;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The panel of the Objects tool: scenery objects of the OMSI installation by folder and by the groups their files
    /// list, the ones this map uses and the user's favourites, with a search across all of them. Rows show the preview
    /// that the map editor nEditor cached for an object, where there is one. A click on an object reports it so placing can
    /// start; the panel stays open for the next one. Favourites are a user preference, kept across maps and sessions.
    /// Objects of a DLC carry a tag and can be hidden, so maps for everyone use only the base game and freeware; folders
    /// of add-ons bought outside Steam can be marked as DLC by hand.
    /// </summary>
    internal sealed class ObjectLibraryPanel : VisualElement
    {
        private enum Tab { Library, Groups, InMap, Favorites }

        /// <summary>Previews are 256 × 256 pixels; this many stay loaded, the least recently shown go first.</summary>
        private const int ThumbnailCacheSize = 160;

        private const string FavoritesKey = "Mapforge.ObjectFavorites";
        private const string HideDlcKey = "Mapforge.HideDlcObjects";
        private const string DlcFoldersKey = "Mapforge.DlcFolders";
        private const string RootFolder = "Sceneryobjects";

        private sealed class Entry
        {
            public bool IsFolder;

            /// <summary>A group of the Groups tab; like a folder it opens a list.</summary>
            public bool IsGroup;
            public string Path;
            public string Name;
            public string Detail;
            public bool IsDlc;

            /// <summary>A folder the user marked as DLC, or an object below one.</summary>
            public bool IsMarked;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<Tab, Button> tabs = new Dictionary<Tab, Button>();
        private readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> dlcFolders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly TextField search;
        private readonly VisualElement folderBar;
        private readonly Label folderLabel;
        private readonly ListView list;
        private readonly Label info;
        private readonly Toggle hideDlc;

        private IReadOnlyList<string> library;
        private IReadOnlyDictionary<string, List<string>> objectGroups = new Dictionary<string, List<string>>();
        private readonly SortedDictionary<string, List<string>> groupMembers = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private OmsiContentOrigins origins;
        private NEditorThumbnails thumbnails;
        private readonly Dictionary<string, Texture2D> thumbnailCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> thumbnailOrder = new LinkedList<string>();
        private string group;
        private IReadOnlyDictionary<string, int> used = new Dictionary<string, int>();
        private Tab tab = Tab.Library;
        private string folder = RootFolder;
        private string chosen;

        public ObjectLibraryPanel()
        {
            AddToClassList("tool-panel");
            LoadFavorites();
            LoadDlcFolders();

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
            AddTab(tabRow, Tab.Groups, "Groups", "Scenery objects by the groups their files list, such as Buildings or Trees");
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

            hideDlc = new Toggle("Hide DLC objects") { value = PlayerPrefs.GetInt(HideDlcKey, 0) != 0 };
            hideDlc.AddToClassList("panel-toggle");
            hideDlc.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetInt(HideDlcKey, evt.newValue ? 1 : 0);
                PlayerPrefs.Save();
                Refresh();
            });
            Add(hideDlc);

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
                fixedItemHeight = 44,
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
        /// The objects to offer: the installation's catalog with every .sco below Sceneryobjects, their groups, origins
        /// and previews, or null while the installation is still being listed, and how often this map uses each object.
        /// Objects of the map join the library even if their file is missing. Applies the user's DLC marks to the origins.
        /// </summary>
        public void SetCatalog(OmsiInstallationCatalog catalog, IReadOnlyDictionary<string, int> usedCounts)
        {
            library = catalog?.Objects;
            used = usedCounts ?? new Dictionary<string, int>();
            origins = catalog?.Origins;
            origins?.SetMarkedFolders(dlcFolders);
            if (!ReferenceEquals(thumbnails, catalog?.Thumbnails))
                ClearThumbnails();
            thumbnails = catalog?.Thumbnails;
            objectGroups = catalog?.Groups ?? new Dictionary<string, List<string>>();
            groupMembers.Clear();
            foreach (var pair in objectGroups)
                foreach (var name in pair.Value)
                {
                    if (!groupMembers.TryGetValue(name, out var members))
                        groupMembers[name] = members = new List<string>();
                    members.Add(pair.Key);
                }
            // Inside a group objects follow their names, whatever folder they come from.
            foreach (var members in groupMembers.Values)
                members.Sort((first, second) =>
                {
                    var order = string.Compare(Path.GetFileName(first), Path.GetFileName(second), StringComparison.OrdinalIgnoreCase);
                    return order != 0 ? order : string.Compare(first, second, StringComparison.OrdinalIgnoreCase);
                });
            if (catalog != null && group != null && !groupMembers.ContainsKey(group))
                group = null;
            hideDlc.tooltip = "Hide objects that belong to a DLC, so the map needs only the base game and freeware. " +
                              (origins == null ? "The origin of the files is still being read."
                                  : origins.FromSteam ? origins.Description
                                  : origins.Description + " Without Steam's lists only folders marked as DLC can be told apart.") +
                              " Folders of add-ons bought outside Steam can be marked as DLC with the tag button.";
            Refresh();
        }

        private bool IsDlc(string path)
        {
            return origins != null && origins.Of(path) == OmsiContentOrigin.Dlc;
        }

        /// <summary>DLC objects leave the library and the favourites while the toggle is on; the map's own list keeps them.</summary>
        private bool Hidden(string path)
        {
            return hideDlc.value && tab != Tab.InMap && IsDlc(path);
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

        /// <summary>
        /// Opens a tab by name (library, groups, inmap or favorites) with a folder of the library or a group, and search
        /// terms, so a check from the command line can show a state without clicks. Unknown names are ignored.
        /// </summary>
        public void ShowState(string tabName, string location, string searchText)
        {
            if (tabName != null && Enum.TryParse<Tab>(tabName, true, out var kind))
                tab = kind;
            if (!string.IsNullOrEmpty(location))
            {
                if (tab == Tab.Groups)
                    group = location;
                else
                    folder = location.Replace('/', Path.DirectorySeparatorChar);
            }
            if (searchText != null)
                search.SetValueWithoutNotify(searchText);
            ShowTab(tab);
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
            if (tab == Tab.Groups)
            {
                group = null;
                Refresh();
                return;
            }
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
            var browsingGroups = tab == Tab.Groups && terms.Length == 0;
            hideDlc.style.display = tab == Tab.InMap ? DisplayStyle.None : DisplayStyle.Flex;
            folderBar.style.display = browsing && !string.Equals(folder, RootFolder, StringComparison.OrdinalIgnoreCase) ||
                                      browsingGroups && group != null
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            folderLabel.text = tab == Tab.Groups
                ? "Groups  ›  " + group
                : folder.Replace(Path.DirectorySeparatorChar.ToString(), "  ›  ");

            if (browsing)
                ListFolder();
            else if (browsingGroups)
                ListGroup();
            else
            {
                var candidates = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                if (tab == Tab.Library)
                {
                    if (library != null)
                        candidates.UnionWith(library);
                    candidates.UnionWith(used.Keys);
                }
                else if (tab == Tab.Groups)
                    candidates.UnionWith(objectGroups.Keys);
                else if (tab == Tab.InMap)
                    candidates.UnionWith(used.Keys);
                else
                    candidates.UnionWith(favorites);
                foreach (var candidate in candidates)
                    if ((Matches(candidate, terms) || tab == Tab.Groups && GroupsMatch(candidate, terms)) && !Hidden(candidate))
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
                var marked = IsMarkedFolder(path);
                entries.Add(new Entry
                {
                    IsFolder = true, Path = path, Name = pair.Key, Detail = Count(pair.Value, "object"), IsDlc = marked, IsMarked = marked
                });
            }
            foreach (var path in objects)
                entries.Add(ObjectEntry(path, false));
        }

        /// <summary>The groups with their object counts, or the objects of the open group.</summary>
        private void ListGroup()
        {
            if (group == null)
            {
                // Large groups such as Buildings or Trees come first; many authors add groups with a single object.
                var counted = new List<KeyValuePair<string, int>>();
                foreach (var pair in groupMembers)
                {
                    var count = 0;
                    foreach (var path in pair.Value)
                        if (!Hidden(path))
                            count++;
                    if (count > 0)
                        counted.Add(new KeyValuePair<string, int>(pair.Key, count));
                }
                counted.Sort((first, second) => first.Value != second.Value
                    ? second.Value.CompareTo(first.Value)
                    : string.Compare(first.Key, second.Key, StringComparison.OrdinalIgnoreCase));
                foreach (var pair in counted)
                    entries.Add(new Entry { IsFolder = true, IsGroup = true, Path = pair.Key, Name = pair.Key, Detail = Count(pair.Value, "object") });
                return;
            }
            if (!groupMembers.TryGetValue(group, out var members))
                return;
            foreach (var path in members)
                if (!Hidden(path))
                    entries.Add(ObjectEntry(path, true));
        }

        /// <summary>True when every search term appears in the object's path or one of its groups.</summary>
        private bool GroupsMatch(string path, string[] terms)
        {
            if (!objectGroups.TryGetValue(path, out var names))
                return false;
            var text = path + "\n" + string.Join("\n", names);
            return Matches(text, terms);
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
            return new Entry
            {
                Path = path, Name = Path.GetFileNameWithoutExtension(path), Detail = detail, IsDlc = IsDlc(path),
                IsMarked = origins != null && origins.IsMarkedByHand(path)
            };
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
            var folderCount = entries.FindAll(entry => entry.IsFolder && !entry.IsGroup).Count;
            var groupCount = entries.FindAll(entry => entry.IsGroup).Count;
            var objectCount = entries.Count - folderCount - groupCount;
            var usesDlc = DlcUse(out var kinds, out var placements);
            var dlcWarning = chosen == null && tab == Tab.InMap && usesDlc;
            info.EnableInClassList("panel-info-warning", dlcWarning);
            if (chosen != null)
                info.text = "Click into the map to place " + Path.GetFileNameWithoutExtension(chosen) +
                            ". Shift+click keeps placing, R turns it, Esc stops.";
            else if (tab == Tab.Library && library == null)
                info.text = "Listing the scenery objects of the OMSI installation...";
            else if (tab == Tab.Favorites && favorites.Count == 0)
                info.text = "Mark objects with the star to find them here.";
            else if (tab == Tab.Groups && groupCount > 0)
                info.text = Count(groupCount, "group") + " from the [groups] of the object files. An object is in each group it names.";
            else if (tab == Tab.Groups && library != null && groupMembers.Count == 0)
                info.text = "No object file of the installation lists [groups].";
            else if (dlcWarning)
                info.text = "This map uses " + Count(kinds, "DLC object") + " in " + Count(placements, "placement") +
                            "; players without that DLC miss them. Maps for everyone should do without.";
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
            if (entry.IsGroup)
            {
                group = entry.Name;
                Refresh();
                return;
            }
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
            var thumbnail = new VisualElement { name = "row-thumbnail", pickingMode = PickingMode.Ignore };
            thumbnail.AddToClassList("panel-row-thumbnail");
            row.Add(thumbnail);
            var texts = new VisualElement();
            texts.AddToClassList("panel-row-texts");
            var name = new Label { name = "row-name" };
            name.AddToClassList("panel-row-name");
            texts.Add(name);
            var detail = new Label { name = "row-detail" };
            detail.AddToClassList("panel-row-detail");
            texts.Add(detail);
            row.Add(texts);
            var tag = new Label("DLC") { name = "row-tag", tooltip = "Part of a DLC: players without it do not see this object." };
            tag.AddToClassList("panel-row-tag");
            row.Add(tag);
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
            var mark = new Button { name = "row-mark" };
            mark.AddToClassList("panel-icon-button");
            mark.AddToClassList("dlc-mark-button");
            mark.Add(new IconElement("tag"));
            mark.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            mark.clicked += () =>
            {
                if (mark.userData is string path)
                    ToggleDlcFolder(path);
            };
            row.Add(mark);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var entry = entries[index];
            var preview = entry.IsFolder ? null : Thumbnail(entry.Path);
            var icon = row.Q<IconElement>("row-icon");
            icon.SetIcon(entry.IsGroup ? "layers" : entry.IsFolder ? "folder" : "box");
            icon.style.display = preview == null ? DisplayStyle.Flex : DisplayStyle.None;
            var thumbnail = row.Q("row-thumbnail");
            thumbnail.style.backgroundImage = preview == null ? new StyleBackground(StyleKeyword.None) : new StyleBackground(preview);
            thumbnail.style.display = preview == null ? DisplayStyle.None : DisplayStyle.Flex;
            row.Q<Label>("row-name").text = entry.Name;
            var detail = row.Q<Label>("row-detail");
            detail.text = entry.Detail;
            detail.style.display = string.IsNullOrEmpty(entry.Detail) ? DisplayStyle.None : DisplayStyle.Flex;
            var tag = row.Q<Label>("row-tag");
            tag.style.display = entry.IsDlc ? DisplayStyle.Flex : DisplayStyle.None;
            tag.tooltip = entry.IsMarked
                ? "Marked as DLC by hand: players without that add-on do not see these objects."
                : "Part of a DLC: players without it do not see this object.";
            var star = row.Q<Button>("row-star");
            star.userData = entry.IsFolder ? null : entry.Path;
            star.style.display = entry.IsFolder ? DisplayStyle.None : DisplayStyle.Flex;
            star.EnableInClassList("favorite-on", !entry.IsFolder && favorites.Contains(entry.Path));
            // Folders can be marked as DLC, for add-ons bought outside Steam that look like user content otherwise.
            var mark = row.Q<Button>("row-mark");
            var markable = entry.IsFolder && !entry.IsGroup;
            mark.userData = markable ? entry.Path : null;
            mark.style.display = markable ? DisplayStyle.Flex : DisplayStyle.None;
            mark.EnableInClassList("dlc-mark-on", markable && entry.IsMarked);
            mark.tooltip = markable && entry.IsMarked
                ? "Marked as DLC by hand; click to count this folder as user content again"
                : "Mark this folder as DLC, for add-ons bought outside Steam";
            row.tooltip = entry.IsGroup ? "Open the group " + entry.Name
                : entry.IsFolder ? "Open the folder " + entry.Name
                : objectGroups.TryGetValue(entry.Path, out var names) ? entry.Path + "\nGroups: " + string.Join(", ", names)
                : entry.Path;
        }

        /// <summary>
        /// The preview nEditor cached for an object, loaded on first use; null when there is none or it cannot be read.
        /// </summary>
        private Texture2D Thumbnail(string objectPath)
        {
            if (thumbnails == null || thumbnails.Count == 0)
                return null;
            if (thumbnailCache.TryGetValue(objectPath, out var cached))
            {
                thumbnailOrder.Remove(objectPath);
                thumbnailOrder.AddFirst(objectPath);
                return cached;
            }
            var file = thumbnails.PathFor(objectPath);
            Texture2D texture = null;
            if (file != null)
            {
                try
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(file), wrapMode = TextureWrapMode.Clamp };
                    if (!texture.LoadImage(File.ReadAllBytes(file), true))
                    {
                        UnityEngine.Object.Destroy(texture);
                        texture = null;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Could not read the preview " + file + ": " + exception.Message);
                    if (texture != null)
                        UnityEngine.Object.Destroy(texture);
                    texture = null;
                }
            }
            // Objects without a preview are remembered as well, so the folder is not asked again.
            thumbnailCache[objectPath] = texture;
            thumbnailOrder.AddFirst(objectPath);
            while (thumbnailOrder.Count > ThumbnailCacheSize)
            {
                var oldest = thumbnailOrder.Last.Value;
                thumbnailOrder.RemoveLast();
                if (thumbnailCache.TryGetValue(oldest, out var evicted) && evicted != null)
                    UnityEngine.Object.Destroy(evicted);
                thumbnailCache.Remove(oldest);
            }
            return texture;
        }

        private void ClearThumbnails()
        {
            foreach (var texture in thumbnailCache.Values)
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            thumbnailCache.Clear();
            thumbnailOrder.Clear();
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
