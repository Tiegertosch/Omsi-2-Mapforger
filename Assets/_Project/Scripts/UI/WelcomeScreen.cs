using System;
using System.IO;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The start view before a map is open: cards to open a project or an OMSI map or to create a new map, and the
    /// projects and maps opened last. It only reports choices; the shell opens them.
    /// </summary>
    internal sealed class WelcomeScreen : VisualElement
    {
        private readonly VisualElement recentList;

        public WelcomeScreen()
        {
            AddToClassList("welcome");

            var title = new Label("OMSI Map Editor");
            title.AddToClassList("welcome-title");
            Add(title);
            var tagline = new Label("Build worlds. Shape roads. Drive your vision.");
            tagline.AddToClassList("welcome-tagline");
            Add(tagline);

            var cards = new VisualElement();
            cards.AddToClassList("welcome-cards");
            cards.Add(Card("folder", "Open project", "Continue editing a Mapforge project (.omproj).",
                () => OpenProjectRequested?.Invoke(), null));
            cards.Add(Card("map", "Open OMSI map", "Look at a map of your OMSI installation. Save it as a project to edit it.",
                () => OpenMapRequested?.Invoke(), null));
            cards.Add(Card("plus", "New map", "Start an empty map with its first tile, based on a map of your installation.",
                () => NewMapRequested?.Invoke(), null));
            Add(cards);

            var recentHeading = new Label("Recent");
            recentHeading.AddToClassList("welcome-section");
            Add(recentHeading);
            recentList = new VisualElement();
            recentList.AddToClassList("welcome-recent");
            Add(recentList);
            Refresh();
        }

        public event Action OpenProjectRequested;
        public event Action OpenMapRequested;
        public event Action NewMapRequested;
        public event Action<RecentFile> RecentChosen;

        /// <summary>Lists the projects and maps opened last; ones whose file is gone are shown but cannot be opened.</summary>
        public void Refresh()
        {
            recentList.Clear();
            var entries = RecentFiles.Load();
            if (entries.Count == 0)
            {
                var empty = new Label("Projects and maps you open appear here.");
                empty.AddToClassList("welcome-empty");
                recentList.Add(empty);
                return;
            }
            foreach (var entry in entries)
            {
                var captured = entry;
                var exists = File.Exists(entry.Path);
                var row = new Button(() => RecentChosen?.Invoke(captured))
                {
                    tooltip = exists ? entry.Path : "Not found: " + entry.Path
                };
                row.AddToClassList("welcome-recent-row");
                var icon = new IconElement(entry.IsProject ? "folder" : "map");
                icon.AddToClassList("welcome-recent-icon");
                row.Add(icon);
                var texts = new VisualElement();
                texts.AddToClassList("welcome-recent-texts");
                var name = new Label(string.IsNullOrWhiteSpace(entry.Name) ? Path.GetFileNameWithoutExtension(entry.Path) : entry.Name);
                name.AddToClassList("welcome-recent-name");
                texts.Add(name);
                var location = new Label(entry.Path);
                location.AddToClassList("welcome-recent-path");
                texts.Add(location);
                row.Add(texts);
                var kind = new Label(!exists ? "Not found" : entry.IsProject ? "Project" : "Map, read-only");
                kind.AddToClassList("welcome-recent-kind");
                row.Add(kind);
                row.SetEnabled(exists);
                recentList.Add(row);
            }
        }

        /// <param name="badge">A label such as "Planned" for a card that cannot be used yet; null for a usable card.</param>
        private static Button Card(string icon, string title, string description, Action clicked, string badge)
        {
            var card = new Button(clicked);
            card.AddToClassList("welcome-card");
            var iconBox = new VisualElement();
            iconBox.AddToClassList("welcome-card-icon");
            iconBox.Add(new IconElement(icon));
            card.Add(iconBox);
            var heading = new Label(title);
            heading.AddToClassList("welcome-card-title");
            card.Add(heading);
            var text = new Label(description);
            text.AddToClassList("welcome-card-text");
            card.Add(text);
            if (badge != null)
            {
                var tag = new Label(badge);
                tag.AddToClassList("welcome-card-badge");
                card.Add(tag);
                card.tooltip = title + " is planned and not available yet.";
                card.SetEnabled(false);
            }
            return card;
        }
    }
}
