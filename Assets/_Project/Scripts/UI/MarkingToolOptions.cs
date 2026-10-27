using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The options of the markings tool (ADR 044): the style of the next line or of the selected one or its piece, the
    /// curvature, hiding a piece, the fill and stripe angle of areas, deleting a line or an area and the suggestion after
    /// the right of way. The shell picks the anchors,
    /// draws the lines and makes the junction again; the options only hold the settings. Favourite styles stand at the top,
    /// kept per user as in the road menu.
    /// </summary>
    internal sealed class MarkingToolOptions : VisualElement
    {
        private const string FavoritesKey = "Mapforge.MarkingFavorites";

        private readonly Label title;
        private readonly Label junctionLabel;
        private readonly VisualElement styleList;
        private readonly Dictionary<OmsiMarkingStyle, Button> styleButtons = new Dictionary<OmsiMarkingStyle, Button>();
        private readonly HashSet<OmsiMarkingStyle> favorites = new HashSet<OmsiMarkingStyle>();
        private readonly Button wholeLineButton;
        private readonly Button pieceButton;
        private readonly Button hideButton;
        private readonly TextField curvatureField;
        private readonly Dictionary<OmsiMarkingFill, Button> fillButtons = new Dictionary<OmsiMarkingFill, Button>();
        private readonly TextField angleField;
        private readonly Dictionary<OmsiMarkingArrowKind, Button> arrowButtons = new Dictionary<OmsiMarkingArrowKind, Button>();
        private readonly TextField distanceField;
        private readonly Button deleteButton;
        private readonly Label planLabel;

        // Rows that only lines, areas or arrows use; only those of the chosen kind show, so the panel stays short.
        private readonly VisualElement scopeRow;
        private readonly VisualElement curvatureRow;
        private readonly VisualElement angleRow;
        private readonly VisualElement distanceRow;
        private bool pieceSelected;

        public MarkingToolOptions()
        {
            AddToClassList("tool-panel");
            AddToClassList("picker-options");
            // Wider than other picker options, so two styles with their stars fit in a row.
            style.width = style.minWidth = style.maxWidth = 300f;
            title = new Label("Road markings");
            title.AddToClassList("picker-options-title");
            Add(title);

            junctionLabel = new Label("Click a junction made in this project.");
            junctionLabel.AddToClassList("panel-info");
            Add(junctionLabel);

            var auto = new Button(() => AutoRequested?.Invoke())
            {
                text = "Auto markings",
                tooltip = "Puts in a suggestion after the right of way in place of the junction's lines: lane lines along the main road, a broken " +
                          "edge line across each side road and give-way lines on the side roads; stop lines on every arm without a main road. Ctrl+Z brings the lines back."
            };
            auto.AddToClassList("map-control-button");
            Add(auto);

            LoadFavorites();
            // Two styles to a row, so the panel leaves the junction in view.
            styleList = new VisualElement();
            styleList.style.flexDirection = FlexDirection.Row;
            styleList.style.flexWrap = Wrap.Wrap;
            Add(styleList);
            BuildStyleList();

            var fillRow = Row("Restricted area");
            fillRow.tooltip = "Sperrfläche (sign 298): choose a colour, then click the anchors round the area and the first one again.";
            foreach (var info in OmsiMarkingFillInfo.All)
            {
                var fill = info.Fill;
                var button = new Button(() => ChooseFill(fill)) { text = info.Yellow ? "Yellow" : "White", tooltip = info.Description };
                button.AddToClassList("panel-tab");
                fillButtons[fill] = button;
                fillRow.Add(button);
            }

            var arrowRow = Row("Arrow");
            arrowRow.tooltip = "Direction arrow (sign 297): choose one, then click a lane of an arm.";
            foreach (var info in OmsiMarkingArrowInfo.All)
            {
                var kind = info.Kind;
                var button = new Button(() => ChooseArrow(kind)) { tooltip = info.Name };
                button.AddToClassList("panel-tab");
                button.style.paddingLeft = button.style.paddingRight = 0f;
                button.style.minWidth = 0f;
                button.Add(new ArrowGlyph(kind));
                arrowButtons[kind] = button;
                arrowRow.Add(button);
            }

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
            scopeRow = Row("Apply to");
            scopeRow.Add(scope);

            hideButton = new Button(() => PieceVisibilityToggled?.Invoke())
            {
                text = "Hide piece", tooltip = "Hides the clicked piece of the line between crossings, such as where a median crosses a turning lane; shows a hidden one again"
            };
            hideButton.AddToClassList("map-control-button");
            Add(hideButton);

            curvatureRow = Row("Curvature (%)");
            curvatureField = new TextField
            {
                value = string.Empty,
                tooltip = "How far the curve bulges between two arms, 0 to 80; empty bends it like a circle. Lines across one arm or from a crossing stay straight."
            };
            curvatureField.AddToClassList("picker-field");
            curvatureField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    CurvatureCommitted?.Invoke();
            }, TrickleDown.TrickleDown);
            curvatureField.RegisterCallback<FocusOutEvent>(_ => CurvatureCommitted?.Invoke());
            curvatureRow.tooltip = curvatureField.tooltip;
            curvatureRow.Add(curvatureField);

            angleRow = Row("Stripe angle (°)");
            angleField = new TextField
            {
                value = OmsiMarkingArea.DefaultAngle.ToString(CultureInfo.InvariantCulture),
                tooltip = "The angle of an area's stripes from its longest side, counter-clockwise; 45 as the StVO has it, -45 leans them the other way."
            };
            angleField.AddToClassList("picker-field");
            angleField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    AngleCommitted?.Invoke();
            }, TrickleDown.TrickleDown);
            angleField.RegisterCallback<FocusOutEvent>(_ => AngleCommitted?.Invoke());
            angleRow.tooltip = angleField.tooltip;
            angleRow.Add(angleField);

            distanceRow = Row("Arrow distance (m)");
            distanceField = new TextField
            {
                value = OmsiMarkingArrow.DefaultDistance.ToString(CultureInfo.InvariantCulture),
                tooltip = "How far behind the mouth of its arm the selected arrow ends, in metres; a new arrow lies where its lane is clicked."
            };
            distanceField.AddToClassList("picker-field");
            distanceField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    DistanceCommitted?.Invoke();
            }, TrickleDown.TrickleDown);
            distanceField.RegisterCallback<FocusOutEvent>(_ => DistanceCommitted?.Invoke());
            distanceRow.tooltip = distanceField.tooltip;
            distanceRow.Add(distanceField);

            deleteButton = new Button(() => DeleteRequested?.Invoke())
            {
                text = "Delete line", tooltip = "Deletes the selected line, area or arrow (Del); a line takes the lines and areas drawn from its crossings along"
            };
            deleteButton.AddToClassList("map-control-button");
            deleteButton.AddToClassList("inspector-edit-delete");
            Add(deleteButton);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = new Label("Y: next style, Del: delete, Esc: let go.");
            hint.AddToClassList("panel-info");
            Add(hint);
            SetPieceScope(false);
            ShowLine(null, null);
            ShowStyle(Style);
        }

        /// <summary>A style was chosen; the shell changes a selected line or piece to it.</summary>
        public event Action<OmsiMarkingStyle> StyleChosen;

        /// <summary>Enter was pressed in the curvature field or it lost focus; the shell changes a selected line.</summary>
        public event Action CurvatureCommitted;

        public event Action DeleteRequested;
        public event Action AutoRequested;
        public event Action PieceVisibilityToggled;

        /// <summary>A fill was chosen; the shell changes a selected area to it.</summary>
        public event Action<OmsiMarkingFill> FillChosen;

        /// <summary>Enter was pressed in the angle field or it lost focus; the shell changes a selected area.</summary>
        public event Action AngleCommitted;

        /// <summary>An arrow kind was chosen; the shell changes a selected arrow to it.</summary>
        public event Action<OmsiMarkingArrowKind> ArrowChosen;

        /// <summary>Enter was pressed in the distance field or it lost focus; the shell moves a selected arrow.</summary>
        public event Action DistanceCommitted;

        /// <summary>The kind of the next arrow while one is chosen, so clicks on lanes lay arrows; null otherwise.</summary>
        public OmsiMarkingArrowKind? ArrowKind { get; private set; }

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

        public OmsiMarkingStyle Style { get; private set; } = OmsiMarkingStyle.LaneLine;

        /// <summary>The fill of the next area while one is chosen, so clicks outline an area; null while they draw lines.</summary>
        public OmsiMarkingFill? AreaFill { get; private set; }

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

        /// <summary>The style after the current one in the list, favourites first, as Y switches it.</summary>
        public OmsiMarkingStyle NextStyle()
        {
            var order = Ordered();
            var index = order.IndexOf(Style);
            return order[(index + 1) % order.Count];
        }

        /// <summary>Chooses a style as a click on its button does.</summary>
        public void ChooseStyle(OmsiMarkingStyle style)
        {
            ShowStyle(style);
            StyleChosen?.Invoke(style);
        }

        /// <summary>
        /// Shows a selected line's style and curvature, or with null the values for the next line; with a piece, that piece's
        /// style in piece scope and whether it is hidden.
        /// </summary>
        public void ShowLine(OmsiMarkingLine line, OmsiMarkingPiece piece)
        {
            pieceSelected = piece != null;
            title.text = line == null ? "Road markings" : piece != null && PieceScope ? "Selected piece" : "Selected line";
            deleteButton.text = "Delete line";
            deleteButton.SetEnabled(line != null);
            hideButton.SetEnabled(piece != null);
            hideButton.text = piece != null && piece.Hidden ? "Show piece" : "Hide piece";
            pieceButton.SetEnabled(piece != null);
            if (line == null)
                return;
            ShowStyle(piece != null && PieceScope ? piece.Style : line.Style);
            curvatureField.SetValueWithoutNotify(line.Curvature.HasValue ? (line.Curvature.Value * 100.0).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty);
        }

        /// <summary>Chooses a fill for the next area, or of the selected one, as a click on its button does.</summary>
        public void ChooseFill(OmsiMarkingFill fill)
        {
            ShowFill(fill);
            FillChosen?.Invoke(fill);
        }

        /// <summary>Shows a selected area's fill and the angle of its stripes.</summary>
        public void ShowArea(OmsiMarkingArea area)
        {
            pieceSelected = false;
            title.text = "Selected area";
            deleteButton.text = "Delete area";
            deleteButton.SetEnabled(true);
            hideButton.SetEnabled(false);
            hideButton.text = "Hide piece";
            pieceButton.SetEnabled(false);
            ShowFill(area.Fill);
            angleField.SetValueWithoutNotify(area.Angle.ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>Chooses an arrow kind for the next arrow, or of the selected one, as a click on its button does.</summary>
        public void ChooseArrow(OmsiMarkingArrowKind kind)
        {
            ShowArrowKind(kind);
            ArrowChosen?.Invoke(kind);
        }

        /// <summary>Shows a selected arrow's kind and distance.</summary>
        public void ShowArrow(OmsiMarkingArrow arrow)
        {
            pieceSelected = false;
            title.text = "Selected arrow";
            deleteButton.text = "Delete arrow";
            deleteButton.SetEnabled(true);
            hideButton.SetEnabled(false);
            hideButton.text = "Hide piece";
            pieceButton.SetEnabled(false);
            ShowArrowKind(arrow.Kind);
            distanceField.SetValueWithoutNotify(arrow.Distance.ToString("0.##", CultureInfo.InvariantCulture));
        }

        public void ShowJunction(string text)
        {
            junctionLabel.text = text;
        }

        public void ShowPlan(string text, bool problem)
        {
            planLabel.text = text;
            planLabel.tooltip = text;
            planLabel.EnableInClassList("panel-info-warning", problem);
        }

        private void SetPieceScope(bool piece)
        {
            PieceScope = piece;
            wholeLineButton.EnableInClassList("panel-tab-active", !piece);
            pieceButton.EnableInClassList("panel-tab-active", piece);
            if (title != null && pieceSelected)
                title.text = piece ? "Selected piece" : "Selected line";
        }

        // A line style, a fill or an arrow kind is chosen at a time; it says what clicks on the junction make.
        private void ShowStyle(OmsiMarkingStyle style)
        {
            Style = style;
            ShowChoice(style, null, null);
        }

        private void ShowFill(OmsiMarkingFill fill)
        {
            ShowChoice(null, fill, null);
        }

        private void ShowArrowKind(OmsiMarkingArrowKind kind)
        {
            ShowChoice(null, null, kind);
        }

        private void ShowChoice(OmsiMarkingStyle? style, OmsiMarkingFill? fill, OmsiMarkingArrowKind? arrow)
        {
            AreaFill = fill;
            ArrowKind = arrow;
            foreach (var pair in styleButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == style);
            foreach (var pair in fillButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == fill);
            foreach (var pair in arrowButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == arrow);
            DisplayStyle Shown(bool shown) => shown ? DisplayStyle.Flex : DisplayStyle.None;
            scopeRow.style.display = hideButton.style.display = curvatureRow.style.display = Shown(style.HasValue);
            angleRow.style.display = Shown(fill.HasValue);
            distanceRow.style.display = Shown(arrow.HasValue);
        }

        /// <summary>The styles with the favourites first, each group in the order of the StVO list.</summary>
        private List<OmsiMarkingStyle> Ordered()
        {
            var order = new List<OmsiMarkingStyle>();
            foreach (var info in OmsiMarkingStyleInfo.All)
                if (favorites.Contains(info.Style))
                    order.Add(info.Style);
            foreach (var info in OmsiMarkingStyleInfo.All)
                if (!favorites.Contains(info.Style))
                    order.Add(info.Style);
            return order;
        }

        /// <summary>A row per style: its button and a star that makes it a favourite.</summary>
        private void BuildStyleList()
        {
            styleList.Clear();
            styleButtons.Clear();
            foreach (var style in Ordered())
            {
                var info = OmsiMarkingStyleInfo.Of(style);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.width = new Length(50f, LengthUnit.Percent);
                var button = new Button(() => ChooseStyle(style)) { text = info.Name, tooltip = info.Description };
                button.AddToClassList("panel-tab");
                button.style.flexGrow = 1f;
                button.EnableInClassList("panel-tab-active", style == Style && !AreaFill.HasValue && !ArrowKind.HasValue);
                row.Add(button);
                styleButtons[style] = button;
                var star = new Button { tooltip = favorites.Contains(style) ? "Remove from favourites" : "Add to favourites; favourites stand at the top" };
                star.AddToClassList("panel-icon-button");
                star.AddToClassList("favorite-button");
                star.EnableInClassList("favorite-on", favorites.Contains(style));
                star.Add(new IconElement("star"));
                star.clicked += () =>
                {
                    if (!favorites.Remove(style))
                        favorites.Add(style);
                    SaveFavorites();
                    BuildStyleList();
                };
                row.Add(star);
                styleList.Add(row);
            }
        }

        private VisualElement Row(string label)
        {
            var row = new VisualElement();
            row.AddToClassList("picker-option");
            var text = new Label(label);
            text.AddToClassList("picker-option-label");
            row.Add(text);
            Add(row);
            return row;
        }

        private void LoadFavorites()
        {
            favorites.Clear();
            foreach (var key in PlayerPrefs.GetString(FavoritesKey, string.Empty).Split('\n'))
                if (OmsiMarkingStyleInfo.ByKey(key.Trim()) is OmsiMarkingStyleInfo info)
                    favorites.Add(info.Style);
        }

        private void SaveFavorites()
        {
            var keys = new List<string>();
            foreach (var style in favorites)
                keys.Add(OmsiMarkingStyleInfo.Of(style).Key);
            PlayerPrefs.SetString(FavoritesKey, string.Join("\n", keys));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// An arrow kind drawn as the editor lays it, squeezed along its length as a driver sees it, in the element's text
        /// colour.
        /// </summary>
        private sealed class ArrowGlyph : VisualElement
        {
            private const float Size = 24f;

            private readonly OmsiMarkingArrowKind kind;

            public ArrowGlyph(OmsiMarkingArrowKind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                style.width = Size;
                style.height = Size;
                style.alignSelf = Align.Center;
                generateVisualContent += Draw;
                RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
            }

            private void Draw(MeshGenerationContext context)
            {
                var size = contentRect.size;
                if (size.x <= 0f || size.y <= 0f)
                    return;
                OmsiJunctionMarkings.ArrowParts(kind, out var shafts, out var heads);
                // 4 m across and the arrow's length fill the square.
                var across = size.x / 4f;
                var along = size.y / (float)OmsiJunctionMarkings.ArrowLength;
                Vector2 Map(double[] point) => new Vector2(size.x / 2f + (float)point[0] * across, size.y - (float)point[1] * along);

                var painter = context.painter2D;
                painter.strokeColor = resolvedStyle.color;
                painter.fillColor = resolvedStyle.color;
                painter.lineWidth = 2f;
                painter.lineJoin = LineJoin.Round;
                foreach (var shaft in shafts)
                {
                    painter.BeginPath();
                    painter.MoveTo(Map(shaft[0]));
                    for (var index = 1; index < shaft.Count; index++)
                        painter.LineTo(Map(shaft[index]));
                    painter.Stroke();
                }
                foreach (var head in heads)
                {
                    // Heads drawn half as wide again about their base, so they read at this size.
                    var baseX = (head[0][0] + head[1][0]) / 2.0;
                    var baseY = (head[0][1] + head[1][1]) / 2.0;
                    Vector2 Corner(double[] point) => Map(new[] { baseX + (point[0] - baseX) * 1.5, baseY + (point[1] - baseY) * 1.5 });
                    painter.BeginPath();
                    painter.MoveTo(Corner(head[0]));
                    painter.LineTo(Corner(head[1]));
                    painter.LineTo(Map(head[2]));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
            }
        }
    }
}
