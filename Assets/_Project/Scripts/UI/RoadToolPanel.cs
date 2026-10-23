using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.OMSI;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The panel of the road tool: the spline profile (.sli) to build with, where the heights come from, and what the road
    /// under the pointer would look like. Profiles the map already uses come first, with their counts. The shell plans
    /// and builds the road; the panel only holds the settings and shows the plan.
    /// </summary>
    internal sealed class RoadToolPanel : VisualElement
    {
        private readonly TextField search;
        private readonly ListView list;
        private readonly List<string> profiles = new List<string>();
        private readonly List<string> shown = new List<string>();
        private readonly Button followButton;
        private readonly Button manualButton;
        private readonly TextField endHeightField;
        private readonly TextField pieceLengthField;
        private readonly TextField gradientField;
        private readonly Label profileLabel;
        private readonly Label planLabel;
        private IReadOnlyDictionary<string, int> used = new Dictionary<string, int>();

        public RoadToolPanel()
        {
            AddToClassList("tool-panel");
            AddToClassList("road-panel");

            var header = new VisualElement();
            header.AddToClassList("tool-panel-header");
            var badge = new VisualElement();
            badge.AddToClassList("tool-panel-badge");
            badge.Add(new IconElement("spline"));
            header.Add(badge);
            var titles = new VisualElement();
            titles.AddToClassList("tool-panel-titles");
            var title = new Label("Road");
            title.AddToClassList("tool-panel-title");
            titles.Add(title);
            var subtitle = new Label("Click the points of a road; Esc ends it");
            subtitle.AddToClassList("tool-panel-subtitle");
            titles.Add(subtitle);
            header.Add(titles);
            var close = new Button(() => CloseRequested?.Invoke()) { tooltip = "Close the road tool (R)" };
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            Add(header);

            profileLabel = new Label();
            profileLabel.AddToClassList("road-profile");
            Add(profileLabel);

            var searchRow = new VisualElement();
            searchRow.AddToClassList("panel-search");
            search = new TextField();
            search.AddToClassList("panel-search-field");
            search.textEdition.placeholder = "Search spline profiles...";
            search.RegisterValueChangedCallback(_ => Refresh());
            searchRow.Add(search);
            var searchIcon = new IconElement("search");
            searchIcon.AddToClassList("panel-search-icon");
            searchRow.Add(searchIcon);
            Add(searchRow);

            list = new ListView
            {
                fixedItemHeight = 36,
                selectionType = SelectionType.Single,
                itemsSource = shown,
                makeItem = MakeRow,
                bindItem = BindRow
            };
            list.AddToClassList("panel-list");
            list.selectionChanged += _ =>
            {
                if (list.selectedIndex >= 0 && list.selectedIndex < shown.Count)
                    SetProfile(shown[list.selectedIndex]);
            };
            Add(list);

            var modes = new VisualElement();
            modes.AddToClassList("panel-tabs");
            followButton = new Button(() => SetHeightMode(OmsiRoadHeightMode.FollowTerrain))
            {
                text = "Follow terrain", tooltip = "Every piece of the road starts and ends on the terrain; the terrain itself is not changed."
            };
            followButton.AddToClassList("panel-tab");
            modes.Add(followButton);
            manualButton = new Button(() => SetHeightMode(OmsiRoadHeightMode.Manual))
            {
                text = "Manual height", tooltip = "The height runs evenly from the start to the end height below, whatever the terrain does."
            };
            manualButton.AddToClassList("panel-tab");
            modes.Add(manualButton);
            Add(modes);

            var fields = new VisualElement();
            fields.AddToClassList("road-fields");
            endHeightField = AddField(fields, "End height (m)", "0", "Height at the end of each new piece of road in manual mode");
            pieceLengthField = AddField(fields, "Piece length (m)", "25", "Longer roads are split into splines of at most this length, so they can follow the terrain");
            gradientField = AddField(fields, "Warn above (%)", "10", "Gradients steeper than this are marked; a check, not a rule");
            Add(fields);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);

            SetHeightMode(OmsiRoadHeightMode.FollowTerrain);
        }

        /// <summary>The panel wants to close the road tool.</summary>
        public event Action CloseRequested;

        /// <summary>A setting changed; the preview should be planned again.</summary>
        public event Action SettingsChanged;

        public string Profile { get; private set; }

        public OmsiRoadHeightMode HeightMode { get; private set; }

        /// <summary>The end height for manual mode, or null while the field holds no number.</summary>
        public double? EndHeight => Number(endHeightField, double.MinValue);

        public double PieceLength => Number(pieceLengthField, 1.0) ?? 25.0;

        public double GradientWarningPercent => Number(gradientField, 0.0) ?? 10.0;

        /// <summary>
        /// The profiles to offer: those of the installation and those the map uses, with their use counts. The most used
        /// profile of the map is chosen unless one is chosen already.
        /// </summary>
        public void SetProfiles(IEnumerable<string> installationProfiles, IReadOnlyDictionary<string, int> mapProfiles)
        {
            used = mapProfiles ?? new Dictionary<string, int>();
            profiles.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inMap = new List<KeyValuePair<string, int>>(used);
            inMap.Sort((left, right) => right.Value != left.Value ? right.Value.CompareTo(left.Value) : string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));
            foreach (var pair in inMap)
                if (seen.Add(pair.Key))
                    profiles.Add(pair.Key);
            if (installationProfiles != null)
                foreach (var path in installationProfiles)
                    if (seen.Add(path))
                        profiles.Add(path);
            if (Profile == null && profiles.Count > 0)
                SetProfile(profiles[0]);
            Refresh();
        }

        /// <summary>Fills the end height field, such as with the height where the road would start.</summary>
        public void SuggestEndHeight(double height)
        {
            endHeightField.SetValueWithoutNotify(height.ToString("0.00", CultureInfo.InvariantCulture));
        }

        /// <summary>What the road under the pointer would be, or the next step to take.</summary>
        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
        }

        private void SetProfile(string path)
        {
            Profile = path;
            used.TryGetValue(path, out var count);
            profileLabel.text = "Profile: " + Path.GetFileNameWithoutExtension(path) + (count > 0 ? "  (" + count + " in this map)" : string.Empty);
            profileLabel.tooltip = path;
            SettingsChanged?.Invoke();
        }

        private void SetHeightMode(OmsiRoadHeightMode mode)
        {
            HeightMode = mode;
            followButton.EnableInClassList("panel-tab-active", mode == OmsiRoadHeightMode.FollowTerrain);
            manualButton.EnableInClassList("panel-tab-active", mode == OmsiRoadHeightMode.Manual);
            endHeightField.SetEnabled(mode == OmsiRoadHeightMode.Manual);
            SettingsChanged?.Invoke();
        }

        private TextField AddField(VisualElement parent, string label, string value, string tooltip)
        {
            var field = new TextField(label) { value = value, tooltip = tooltip };
            field.AddToClassList("road-field");
            field.RegisterValueChangedCallback(_ => SettingsChanged?.Invoke());
            parent.Add(field);
            return field;
        }

        private static double? Number(TextField field, double minimum)
        {
            return double.TryParse(field.value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= minimum &&
                   !double.IsInfinity(value)
                ? value
                : (double?)null;
        }

        private void Refresh()
        {
            shown.Clear();
            var text = search.value?.Trim() ?? string.Empty;
            foreach (var path in profiles)
                if (text.Length == 0 || path.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    shown.Add(path);
            list.RefreshItems();
            var index = Profile == null ? -1 : shown.IndexOf(Profile);
            if (index >= 0)
                list.SetSelectionWithoutNotify(new[] { index });
            else
                list.ClearSelection();
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("panel-row");
            var icon = new IconElement("spline");
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
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var path = shown[index];
            used.TryGetValue(path, out var count);
            row.Q<Label>("row-name").text = Path.GetFileNameWithoutExtension(path);
            row.Q<Label>("row-detail").text = (count > 0 ? count + " in this map  •  " : string.Empty) + Path.GetDirectoryName(path);
            row.tooltip = path;
        }
    }
}
