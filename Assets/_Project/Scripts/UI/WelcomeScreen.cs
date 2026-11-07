using System;
using System.IO;
using OmsiMapEditor.Core.Localization;
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

            var title = new Label("OMSI MapWorks");
            title.AddToClassList("welcome-title");
            Add(title);
            var tagline = LocBindings.NewLabel("welcome.tagline");
            tagline.AddToClassList("welcome-tagline");
            Add(tagline);

            var cards = new VisualElement();
            cards.AddToClassList("welcome-cards");
            cards.Add(Card("folder", "welcome.card.open_project.title", "welcome.card.open_project.text",
                () => OpenProjectRequested?.Invoke(), null));
            cards.Add(Card("map", "welcome.card.open_map.title", "welcome.card.open_map.text",
                () => OpenMapRequested?.Invoke(), null));
            cards.Add(Card("plus", "welcome.card.new_map.title", "welcome.card.new_map.text",
                () => NewMapRequested?.Invoke(), null));
            Add(cards);

            var recentHeading = LocBindings.NewLabel("welcome.recent");
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
                var empty = LocBindings.NewLabel("welcome.recent.empty");
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
                    tooltip = exists ? entry.Path : Loc.T("welcome.recent.not_found.tooltip", ("path", entry.Path))
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
                var kind = LocBindings.NewLabel(!exists ? "welcome.recent.kind.not_found" : entry.IsProject ? "welcome.recent.kind.project" : "welcome.recent.kind.map"); // i18n:keys
                kind.AddToClassList("welcome-recent-kind");
                row.Add(kind);
                row.SetEnabled(exists);
                recentList.Add(row);
            }
        }

        /// <param name="titleKey">The keys of the card's title and text.</param>
        /// <param name="badge">A label such as "Planned" for a card that cannot be used yet; null for a usable card.</param>
        private static Button Card(string icon, string titleKey, string textKey, Action clicked, string badge)
        {
            var card = new Button(clicked);
            card.AddToClassList("welcome-card");
            var iconBox = new VisualElement();
            iconBox.AddToClassList("welcome-card-icon");
            iconBox.Add(new IconElement(icon));
            card.Add(iconBox);
            var heading = LocBindings.NewLabel(titleKey); // i18n:keys
            heading.AddToClassList("welcome-card-title");
            card.Add(heading);
            var text = LocBindings.NewLabel(textKey); // i18n:keys
            text.AddToClassList("welcome-card-text");
            card.Add(text);
            if (badge != null)
            {
                var tag = new Label(badge);
                tag.AddToClassList("welcome-card-badge");
                card.Add(tag);
                card.tooltip = Loc.T("welcome.card.planned.tooltip", ("title", Loc.T(titleKey))); // i18n:keys
                card.SetEnabled(false);
            }
            return card;
        }
    }
}
