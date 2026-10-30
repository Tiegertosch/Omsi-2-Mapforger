using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The menu of the markings tool (ADR 044), laid out like the road menu (ADR 029): on the left "Options" with the
    /// junction, "Auto markings" and the settings of the selected line, area or arrow, or of the next one; on the right a
    /// library of all markings in tabs, each drawn as it is laid, with favourites kept per user. One marking is chosen at a
    /// time and says what clicks on the junction make. The shell picks the anchors, draws the markings and makes the
    /// junction again; the menu only holds the settings.
    /// </summary>
    internal sealed class MarkingToolOptions : VisualElement
    {
        private const string FavoritesKey = "Mapforge.MarkingFavorites";

        private enum Kind
        {
            Line,
            Area,
            Arrow
        }

        private enum Tab
        {
            Lines,
            Areas,
            Arrows,
            Favorites
        }

        /// <summary>One entry of the library: a line style, a fill or an arrow kind.</summary>
        private struct Item : IEquatable<Item>
        {
            public Kind Kind;
            public OmsiMarkingStyle Style;
            public OmsiMarkingFill Fill;
            public OmsiMarkingArrowKind Arrow;

            public string Name =>
                Kind == Kind.Line ? OmsiMarkingStyleInfo.Of(Style).Name : Kind == Kind.Area ? OmsiMarkingFillInfo.Of(Fill).Name : OmsiMarkingArrowInfo.Of(Arrow).Name + " arrow";

            /// <summary>A name short enough for a tile; the full one heads the library and shows as the tile's tooltip.</summary>
            public string TileName
            {
                get
                {
                    if (Kind == Kind.Area)
                        return OmsiMarkingFillInfo.Of(Fill).Yellow ? "Yellow area" : "Restricted area";
                    if (Kind == Kind.Line)
                        return OmsiMarkingStyleInfo.Of(Style).Name;
                    var info = OmsiMarkingArrowInfo.Of(Arrow);
                    return info.Straight && info.Left && info.Right ? "All ways"
                        : info.Straight && info.Left ? "Straight, left"
                        : info.Straight && info.Right ? "Straight, right"
                        : info.Left && info.Right ? "Left, right"
                        : info.Left ? "Left" : info.Right ? "Right" : "Straight";
                }
            }

            public string Description =>
                Kind == Kind.Line ? OmsiMarkingStyleInfo.Of(Style).Description
                : Kind == Kind.Area ? OmsiMarkingFillInfo.Of(Fill).Description
                : "Direction arrow (sign 297), 5 m long: " + OmsiMarkingArrowInfo.Of(Arrow).Name.ToLowerInvariant();

            /// <summary>How the favourites keep it; line styles by their bare key, as before areas and arrows came.</summary>
            public string Key =>
                Kind == Kind.Line ? OmsiMarkingStyleInfo.Of(Style).Key : Kind == Kind.Area ? "area:" + OmsiMarkingFillInfo.Of(Fill).Key : "arrow:" + OmsiMarkingArrowInfo.Of(Arrow).Key;

            public bool Equals(Item other)
            {
                return Kind == other.Kind && (Kind == Kind.Line ? Style == other.Style : Kind == Kind.Area ? Fill == other.Fill : Arrow == other.Arrow);
            }
        }

        private readonly List<Item> items = new List<Item>();
        private readonly HashSet<string> favorites = new HashSet<string>();

        private readonly Label optionsTitle;
        private readonly Label junctionLabel;
        private readonly Label contextTitle;
        private readonly VisualElement scopeRow;
        private readonly Button wholeLineButton;
        private readonly Button pieceButton;
        private readonly Button hideButton;
        private readonly VisualElement curvatureRow;
        private readonly TextField curvatureField;
        private readonly VisualElement angleRow;
        private readonly TextField angleField;
        private readonly VisualElement distanceRow;
        private readonly TextField distanceField;
        private readonly Button deleteButton;

        private readonly Label choiceLabel;
        private readonly Label planLabel;
        private readonly Dictionary<Button, Tab> tabButtons = new Dictionary<Button, Tab>();
        private readonly ScrollView tiles;
        private readonly Label emptyLabel;

        private Item choice = new Item { Kind = Kind.Line, Style = OmsiMarkingStyle.LaneLine };
        private Tab tab = Tab.Lines;

        // What is selected on the junction: nothing, a line (and a piece of it), an area or an arrow.
        private Kind? selected;
        private bool pieceSelected;

        public MarkingToolOptions()
        {
            AddToClassList("picker-tool");
            foreach (var info in OmsiMarkingStyleInfo.All)
                items.Add(new Item { Kind = Kind.Line, Style = info.Style });
            foreach (var info in OmsiMarkingFillInfo.All)
                items.Add(new Item { Kind = Kind.Area, Fill = info.Fill });
            foreach (var info in OmsiMarkingArrowInfo.All)
                items.Add(new Item { Kind = Kind.Arrow, Arrow = info.Kind });
            LoadFavorites();

            // The options on the left.
            var options = new VisualElement();
            options.AddToClassList("tool-panel");
            options.AddToClassList("picker-options");
            optionsTitle = new Label("Options");
            optionsTitle.AddToClassList("picker-options-title");
            options.Add(optionsTitle);
            junctionLabel = new Label("Click a junction made in this project.");
            junctionLabel.AddToClassList("panel-info");
            junctionLabel.style.marginTop = 0f;
            options.Add(junctionLabel);
            var auto = new Button(() => AutoRequested?.Invoke())
            {
                text = "Auto markings",
                tooltip = "Puts in a suggestion after the right of way in place of the junction's lines: lane lines along the main road, a broken " +
                          "edge line across each side road and give-way lines on the side roads; stop lines on every arm without a main road. Areas and " +
                          "arrows stay. Ctrl+Z brings the lines back."
            };
            auto.AddToClassList("map-control-button");
            options.Add(auto);

            contextTitle = new Label();
            contextTitle.AddToClassList("picker-options-title");
            contextTitle.style.marginTop = 10f;
            options.Add(contextTitle);

            var scope = new VisualElement();
            scope.AddToClassList("panel-tabs");
            scope.AddToClassList("picker-segmented");
            wholeLineButton = new Button(() => SetPieceScope(false)) { text = "Whole line", tooltip = "Styles and curvature change the whole selected line" };
            pieceButton = new Button(() => SetPieceScope(true))
            {
                text = "This piece", tooltip = "A style changes only the piece of the line between crossings that was clicked"
            };
            foreach (var button in new[] { wholeLineButton, pieceButton })
            {
                button.AddToClassList("panel-tab");
                scope.Add(button);
            }
            scopeRow = Row(options, "Apply to");
            scopeRow.Add(scope);

            hideButton = new Button(() => PieceVisibilityToggled?.Invoke())
            {
                text = "Hide piece", tooltip = "Hides the clicked piece of the line between crossings, such as where a median crosses a turning lane; shows a hidden one again"
            };
            hideButton.AddToClassList("map-control-button");
            options.Add(hideButton);

            curvatureRow = Row(options, "Curvature (%)");
            curvatureField = Field(curvatureRow, string.Empty,
                "How far the curve bulges between two arms, 0 to 80; empty bends it like a circle. Lines across one arm or from a crossing stay straight.",
                () => CurvatureCommitted?.Invoke());
            angleRow = Row(options, "Stripe angle (°)");
            angleField = Field(angleRow, OmsiMarkingArea.DefaultAngle.ToString(CultureInfo.InvariantCulture),
                "The angle of an area's stripes from its longest side, counter-clockwise; 45 as the StVO has it, -45 leans them the other way.",
                () => AngleCommitted?.Invoke());
            distanceRow = Row(options, "Distance (m)");
            distanceField = Field(distanceRow, OmsiMarkingArrow.DefaultDistance.ToString(CultureInfo.InvariantCulture),
                "How far behind the mouth of its arm the selected arrow ends, in metres; a new arrow lies where its lane is clicked.",
                () => DistanceCommitted?.Invoke());

            deleteButton = new Button(() => DeleteRequested?.Invoke()) { text = "Delete", tooltip = "Deletes the selected marking (Del); a line takes the lines and areas drawn from its crossings along" };
            deleteButton.AddToClassList("map-control-button");
            deleteButton.AddToClassList("inspector-edit-delete");
            options.Add(deleteButton);

            var hint = new Label("Y: next in its tab, Del: delete, Esc: let go.");
            hint.AddToClassList("panel-info");
            options.Add(hint);
            Add(options);

            // The library beside them.
            var picker = new VisualElement();
            picker.AddToClassList("tool-panel");
            picker.AddToClassList("picker-panel");
            // Lower and narrower than the road library, so the junction above stays in view.
            picker.style.width = picker.style.minWidth = picker.style.maxWidth = 560f;
            picker.style.height = 300f;
            var header = new VisualElement();
            header.AddToClassList("tool-panel-header");
            header.AddToClassList("picker-header");
            var badge = new VisualElement();
            badge.AddToClassList("tool-panel-badge");
            badge.Add(new IconElement("brush"));
            header.Add(badge);
            var titles = new VisualElement();
            titles.AddToClassList("tool-panel-titles");
            choiceLabel = new Label();
            choiceLabel.AddToClassList("tool-panel-title");
            choiceLabel.AddToClassList("picker-title");
            titles.Add(choiceLabel);
            planLabel = new Label();
            planLabel.AddToClassList("tool-panel-subtitle");
            planLabel.AddToClassList("picker-subtitle");
            titles.Add(planLabel);
            header.Add(titles);
            var close = new Button(() => CloseRequested?.Invoke()) { tooltip = "Close the markings tool (M)" };
            close.AddToClassList("panel-icon-button");
            close.Add(new IconElement("x"));
            header.Add(close);
            picker.Add(header);

            var tabRow = new VisualElement();
            tabRow.AddToClassList("panel-tabs");
            tabRow.AddToClassList("picker-tabs");
            AddTab(tabRow, Tab.Lines, "Lines", "Lines between two anchors: click one anchor, then another");
            AddTab(tabRow, Tab.Areas, "Areas", "Restricted areas (sign 298): click the anchors round one, then the first again");
            AddTab(tabRow, Tab.Arrows, "Arrows", "Direction arrows (sign 297): click a lane of an arm");
            AddTab(tabRow, Tab.Favorites, "Favorites", "Markings you marked with the star");
            picker.Add(tabRow);

            tiles = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            tiles.AddToClassList("picker-tiles");
            tiles.contentContainer.AddToClassList("picker-tiles-content");
            picker.Add(tiles);
            emptyLabel = new Label("Mark markings with the star to find them here.");
            emptyLabel.AddToClassList("panel-info");
            emptyLabel.AddToClassList("picker-empty");
            emptyLabel.style.top = 110f;
            picker.Add(emptyLabel);
            Add(picker);

            SetPieceScope(false);
            ShowChoice(choice);
        }

        /// <summary>A style was chosen; the shell changes a selected line or piece to it.</summary>
        public event Action<OmsiMarkingStyle> StyleChosen;

        /// <summary>A fill was chosen; the shell changes a selected area to it.</summary>
        public event Action<OmsiMarkingFill> FillChosen;

        /// <summary>An arrow kind was chosen; the shell changes a selected arrow to it.</summary>
        public event Action<OmsiMarkingArrowKind> ArrowChosen;

        /// <summary>Enter was pressed in the curvature field or it lost focus; the shell changes a selected line.</summary>
        public event Action CurvatureCommitted;

        /// <summary>Enter was pressed in the angle field or it lost focus; the shell changes a selected area.</summary>
        public event Action AngleCommitted;

        /// <summary>Enter was pressed in the distance field or it lost focus; the shell moves a selected arrow.</summary>
        public event Action DistanceCommitted;

        public event Action DeleteRequested;
        public event Action AutoRequested;
        public event Action PieceVisibilityToggled;

        /// <summary>The menu wants to close the markings tool.</summary>
        public event Action CloseRequested;

        /// <summary>The style of the next line, or the last one chosen while an area or arrow is.</summary>
        public OmsiMarkingStyle Style { get; private set; } = OmsiMarkingStyle.LaneLine;

        /// <summary>The fill of the next area while one is chosen, so clicks outline an area; null otherwise.</summary>
        public OmsiMarkingFill? AreaFill => choice.Kind == Kind.Area ? choice.Fill : (OmsiMarkingFill?)null;

        /// <summary>The kind of the next arrow while one is chosen, so clicks on lanes lay arrows; null otherwise.</summary>
        public OmsiMarkingArrowKind? ArrowKind => choice.Kind == Kind.Arrow ? choice.Arrow : (OmsiMarkingArrowKind?)null;

        /// <summary>The name of what is chosen, such as "Lane line".</summary>
        public string ChoiceName => choice.Name;

        /// <summary>True when a style changes only the selected piece rather than its whole line.</summary>
        public bool PieceScope { get; private set; }

        /// <summary>The curvature typed, as a share of the line's chord, or null for a circle-like curve.</summary>
        public double? Curvature
        {
            get
            {
                var text = curvatureField.value?.Trim().Replace(',', '.') ?? string.Empty;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || double.IsInfinity(percent))
                    return null;
                return Math.Max(0.0, Math.Min(OmsiJunctionMarkings.MaximumCurvature, percent / 100.0));
            }
        }

        /// <summary>The angle of the stripes typed, in degrees from the area's longest side; 45 when nothing sensible is typed.</summary>
        public double Angle
        {
            get
            {
                var text = angleField.value?.Trim().Replace(',', '.') ?? string.Empty;
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees) && !double.IsInfinity(degrees) && !double.IsNaN(degrees)
                    ? degrees % 360.0
                    : OmsiMarkingArea.DefaultAngle;
            }
        }

        /// <summary>The distance typed for the selected arrow in metres, 0 to 50; the default when nothing sensible is typed.</summary>
        public double Distance
        {
            get
            {
                var text = distanceField.value?.Trim().Replace(',', '.') ?? string.Empty;
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres) && !double.IsInfinity(metres) && !double.IsNaN(metres)
                    ? Math.Max(0.0, Math.Min(50.0, metres))
                    : OmsiMarkingArrow.DefaultDistance;
            }
        }

        /// <summary>Chooses a style as a click on its tile does.</summary>
        public void ChooseStyle(OmsiMarkingStyle style)
        {
            Choose(new Item { Kind = Kind.Line, Style = style });
        }

        /// <summary>Chooses a fill for the next area, or of the selected one, as a click on its tile does.</summary>
        public void ChooseFill(OmsiMarkingFill fill)
        {
            Choose(new Item { Kind = Kind.Area, Fill = fill });
        }

        /// <summary>Chooses an arrow kind for the next arrow, or of the selected one, as a click on its tile does.</summary>
        public void ChooseArrow(OmsiMarkingArrowKind kind)
        {
            Choose(new Item { Kind = Kind.Arrow, Arrow = kind });
        }

        /// <summary>Chooses the marking after the chosen one among those of its kind, favourites first, as Y does.</summary>
        public void ChooseNext()
        {
            var order = new List<Item>();
            foreach (var favorite in new[] { true, false })
                foreach (var item in items)
                    if (item.Kind == choice.Kind && favorites.Contains(item.Key) == favorite)
                        order.Add(item);
            Choose(order[(order.IndexOf(choice) + 1) % order.Count]);
        }

        /// <summary>
        /// Shows a selected line's style and curvature, or with null the settings for the next marking; with a piece, that
        /// piece's style in piece scope and whether it is hidden.
        /// </summary>
        public void ShowLine(OmsiMarkingLine line, OmsiMarkingPiece piece)
        {
            selected = line != null ? Kind.Line : (Kind?)null;
            pieceSelected = piece != null;
            hideButton.SetEnabled(piece != null);
            hideButton.text = piece != null && piece.Hidden ? "Show piece" : "Hide piece";
            pieceButton.SetEnabled(piece != null);
            if (line != null)
            {
                curvatureField.SetValueWithoutNotify(line.Curvature.HasValue ? (line.Curvature.Value * 100.0).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty);
                ShowChoice(new Item { Kind = Kind.Line, Style = piece != null && PieceScope ? piece.Style : line.Style });
            }
            else
                ShowContext();
        }

        /// <summary>Shows a selected area's fill and the angle of its stripes.</summary>
        public void ShowArea(OmsiMarkingArea area)
        {
            selected = Kind.Area;
            pieceSelected = false;
            angleField.SetValueWithoutNotify(area.Angle.ToString("0.#", CultureInfo.InvariantCulture));
            ShowChoice(new Item { Kind = Kind.Area, Fill = area.Fill });
        }

        /// <summary>Shows a selected arrow's kind and distance.</summary>
        public void ShowArrow(OmsiMarkingArrow arrow)
        {
            selected = Kind.Arrow;
            pieceSelected = false;
            distanceField.SetValueWithoutNotify(arrow.Distance.ToString("0.##", CultureInfo.InvariantCulture));
            ShowChoice(new Item { Kind = Kind.Arrow, Arrow = arrow.Kind });
        }

        public void ShowJunction(string text)
        {
            junctionLabel.text = text;
        }

        /// <summary>Shows what the next click does, under the name of the chosen marking.</summary>
        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.tooltip = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
        }

        private void Choose(Item item)
        {
            ShowChoice(item);
            if (item.Kind == Kind.Line)
                StyleChosen?.Invoke(item.Style);
            else if (item.Kind == Kind.Area)
                FillChosen?.Invoke(item.Fill);
            else
                ArrowChosen?.Invoke(item.Arrow);
        }

        /// <summary>Makes a marking the chosen one: its tile lights up in its tab, and the options show its settings.</summary>
        private void ShowChoice(Item item)
        {
            choice = item;
            if (item.Kind == Kind.Line)
                Style = item.Style;
            choiceLabel.text = item.Name;
            choiceLabel.tooltip = item.Description;
            // The tab of its kind opens, unless it stands among the favourites shown.
            var own = item.Kind == Kind.Line ? Tab.Lines : item.Kind == Kind.Area ? Tab.Areas : Tab.Arrows;
            if (tab != own && !(tab == Tab.Favorites && favorites.Contains(item.Key)))
                tab = own;
            Refresh();
            ShowContext();
        }

        /// <summary>Shows the settings that belong to the selected marking, or to the next one of the chosen kind.</summary>
        private void ShowContext()
        {
            var kind = selected ?? choice.Kind;
            var noun = kind == Kind.Line ? (pieceSelected && PieceScope ? "piece" : "line") : kind == Kind.Area ? "area" : "arrow";
            contextTitle.text = (selected.HasValue ? "Selected " : "Next ") + noun;
            DisplayStyle Shown(bool shown) => shown ? DisplayStyle.Flex : DisplayStyle.None;
            scopeRow.style.display = hideButton.style.display = Shown(selected == Kind.Line);
            curvatureRow.style.display = Shown(kind == Kind.Line);
            angleRow.style.display = Shown(kind == Kind.Area);
            // A new arrow lies where its lane is clicked; only a selected one has a distance to change.
            distanceRow.style.display = Shown(selected == Kind.Arrow);
            deleteButton.style.display = Shown(selected.HasValue);
            deleteButton.text = "Delete " + noun.Replace("piece", "line");
        }

        private void SetPieceScope(bool piece)
        {
            PieceScope = piece;
            wholeLineButton.EnableInClassList("panel-tab-active", !piece);
            pieceButton.EnableInClassList("panel-tab-active", piece);
            if (contextTitle != null)
                ShowContext();
        }

        private void AddTab(VisualElement row, Tab kind, string text, string tooltip)
        {
            var button = new Button(() =>
            {
                tab = kind;
                Refresh();
            })
            {
                text = text, tooltip = tooltip
            };
            button.AddToClassList("panel-tab");
            button.AddToClassList("picker-tab");
            tabButtons.Add(button, kind);
            row.Add(button);
        }

        /// <summary>Builds the tiles of the open tab, favourites first in each.</summary>
        private void Refresh()
        {
            foreach (var pair in tabButtons)
                pair.Key.EnableInClassList("panel-tab-active", pair.Value == tab);
            tiles.Clear();
            var shown = 0;
            foreach (var favorite in new[] { true, false })
                foreach (var item in items)
                {
                    var inTab = tab == Tab.Favorites ? favorites.Contains(item.Key)
                        : (tab == Tab.Lines ? Kind.Line : tab == Tab.Areas ? Kind.Area : Kind.Arrow) == item.Kind && favorites.Contains(item.Key) == favorite;
                    if (!inTab || tab == Tab.Favorites && !favorite)
                        continue;
                    tiles.Add(MakeTile(item));
                    shown++;
                }
            emptyLabel.style.display = shown > 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement MakeTile(Item item)
        {
            var tile = new VisualElement { tooltip = item.Description };
            tile.AddToClassList("picker-tile");
            // Smaller than road tiles: a marking needs no more to be told apart.
            tile.style.width = 96f;
            tile.style.height = 76f;
            tile.EnableInClassList("picker-tile-selected", item.Equals(choice));
            var preview = new MarkingPreview(item);
            preview.AddToClassList("picker-tile-preview");
            preview.style.width = 84f;
            preview.style.height = 44f;
            tile.Add(preview);
            var name = new Label(item.TileName);
            name.AddToClassList("picker-tile-name");
            tile.Add(name);
            var star = new Button { tooltip = favorites.Contains(item.Key) ? "Remove from favorites" : "Add to favorites" };
            star.AddToClassList("panel-icon-button");
            star.AddToClassList("favorite-button");
            star.AddToClassList("picker-tile-star");
            star.EnableInClassList("favorite-on", favorites.Contains(item.Key));
            star.Add(new IconElement("star"));
            star.clicked += () =>
            {
                if (!favorites.Remove(item.Key))
                    favorites.Add(item.Key);
                SaveFavorites();
                Refresh();
            };
            tile.Add(star);
            tile.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement target && (target == star || star.Contains(target)))
                    return;
                Choose(item);
            });
            return tile;
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

        /// <summary>A number field in a row that reports Enter and leaving it.</summary>
        private static TextField Field(VisualElement row, string value, string tooltip, Action committed)
        {
            var field = new TextField { value = value, tooltip = tooltip };
            field.AddToClassList("picker-field");
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    committed();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => committed());
            row.tooltip = tooltip;
            row.Add(field);
            return field;
        }

        private void LoadFavorites()
        {
            favorites.Clear();
            foreach (var key in PlayerPrefs.GetString(FavoritesKey, string.Empty).Split('\n'))
                if (items.Exists(item => item.Key == key.Trim()))
                    favorites.Add(key.Trim());
        }

        private void SaveFavorites()
        {
            PlayerPrefs.SetString(FavoritesKey, string.Join("\n", favorites));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// A marking drawn as the editor lays it, white or yellow on the dark field: a line as a stretch of its strokes, an
        /// area as a hatched patch with its border, an arrow squeezed along its length as a driver sees it.
        /// </summary>
        private sealed class MarkingPreview : VisualElement
        {
            private static readonly Color White = new Color(0.94f, 0.94f, 0.92f);
            private static readonly Color Yellow = new Color(0.96f, 0.75f, 0.12f);

            private readonly Item item;

            public MarkingPreview(Item item)
            {
                this.item = item;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                var size = contentRect.size;
                if (size.x <= 0f || size.y <= 0f)
                    return;
                var painter = context.painter2D;
                if (item.Kind == Kind.Line)
                    DrawLine(painter, size, OmsiMarkingStyleInfo.Of(item.Style));
                else if (item.Kind == Kind.Area)
                    DrawArea(painter, size, OmsiMarkingFillInfo.Of(item.Fill));
                else
                    DrawArrow(painter, size, item.Arrow);
            }

            private static void DrawLine(Painter2D painter, Vector2 size, OmsiMarkingStyleInfo info)
            {
                painter.fillColor = info.Yellow ? Yellow : White;
                var middle = size.y / 2f;
                // Wider lines show wider; a crossing shows its bars across the line.
                var thickness = info.Beside ? size.y - 14f : 2f + (float)info.Width * 8f;
                var period = (float)(info.Stroke + info.Gap);
                var scale = period > 0f ? Mathf.Clamp((size.x - 12f) / (2.5f * period), 4f, 24f) : 1f;
                var offsets = info.TwoLines ? new[] { -3f, 3f } : new[] { 0f };
                foreach (var offset in offsets)
                {
                    var top = middle + offset - thickness / 2f;
                    var bottom = middle + offset + thickness / 2f;
                    if (period <= 0f)
                    {
                        Box(painter, 6f, top, size.x - 6f, bottom);
                        continue;
                    }
                    for (var start = 6f; start < size.x - 6f; start += period * scale)
                        Box(painter, start, top, Mathf.Min(start + (float)info.Stroke * scale, size.x - 6f), bottom);
                }
            }

            private static void DrawArea(Painter2D painter, Vector2 size, OmsiMarkingFillInfo info)
            {
                var colour = info.Yellow ? Yellow : White;
                var left = 8f;
                var right = size.x - 8f;
                var top = 6f;
                var bottom = size.y - 6f;
                painter.strokeColor = colour;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(left, top));
                painter.LineTo(new Vector2(right, top));
                painter.LineTo(new Vector2(right, bottom));
                painter.LineTo(new Vector2(left, bottom));
                painter.ClosePath();
                painter.Stroke();
                // Stripes at 45°, cut at the border: each runs from where it enters the patch to where it leaves it.
                painter.lineWidth = 4f;
                painter.lineCap = LineCap.Butt;
                var height = bottom - top;
                for (var start = left - height + 4f; start < right; start += 11f)
                {
                    var fromX = Mathf.Max(start, left);
                    var toX = Mathf.Min(start + height, right);
                    if (toX - fromX < 1f)
                        continue;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(fromX, bottom - (fromX - start)));
                    painter.LineTo(new Vector2(toX, bottom - (toX - start)));
                    painter.Stroke();
                }
            }

            private static void DrawArrow(Painter2D painter, Vector2 size, OmsiMarkingArrowKind kind)
            {
                // 3 m across and the arrow's length fill a box as high as the preview.
                var box = size.y - 6f;
                var across = box / 3f;
                var along = box / (float)OmsiJunctionMarkings.ArrowLength;
                Vector2 Map(double[] point) => new Vector2(size.x / 2f + (float)point[0] * across, size.y - 3f - (float)point[1] * along);
                painter.fillColor = White;
                foreach (var part in OmsiJunctionMarkings.ArrowShape(kind))
                {
                    painter.BeginPath();
                    painter.MoveTo(Map(part[0]));
                    for (var index = 1; index < part.Count; index++)
                        painter.LineTo(Map(part[index]));
                    painter.ClosePath();
                    painter.Fill();
                }
            }

            private static void Box(Painter2D painter, float left, float top, float right, float bottom)
            {
                if (right - left < 0.5f)
                    return;
                painter.BeginPath();
                painter.MoveTo(new Vector2(left, top));
                painter.LineTo(new Vector2(right, top));
                painter.LineTo(new Vector2(right, bottom));
                painter.LineTo(new Vector2(left, bottom));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
