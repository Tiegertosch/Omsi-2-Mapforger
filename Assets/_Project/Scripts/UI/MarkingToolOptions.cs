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
    /// curvature, hiding a piece, deleting a line and the suggestion after the right of way. The shell picks the anchors,
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
        private readonly Button deleteButton;
        private readonly Label planLabel;
        private bool pieceSelected;

        public MarkingToolOptions()
        {
            AddToClassList("tool-panel");
            AddToClassList("picker-options");
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
            var scopeRow = Row("Apply to");
            scopeRow.Add(scope);

            hideButton = new Button(() => PieceVisibilityToggled?.Invoke())
            {
                text = "Hide piece", tooltip = "Hides the clicked piece of the line between crossings, such as where a median crosses a turning lane; shows a hidden one again"
            };
            hideButton.AddToClassList("map-control-button");
            Add(hideButton);

            var curvatureRow = Row("Curvature (%)");
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

            deleteButton = new Button(() => DeleteRequested?.Invoke())
            {
                text = "Delete line", tooltip = "Deletes the selected line (Del), and the lines drawn from its crossings"
            };
            deleteButton.AddToClassList("map-control-button");
            deleteButton.AddToClassList("inspector-edit-delete");
            Add(deleteButton);

            planLabel = new Label();
            planLabel.AddToClassList("panel-info");
            Add(planLabel);
            var hint = new Label("Anchor, then anchor: a line. Click a line to select it and its piece. Y: next style, Del: delete, Esc: let go.");
            hint.AddToClassList("panel-info");
            Add(hint);
            SetPieceScope(false);
            ShowLine(null, null);
        }

        /// <summary>A style was chosen; the shell changes a selected line or piece to it.</summary>
        public event Action<OmsiMarkingStyle> StyleChosen;

        /// <summary>Enter was pressed in the curvature field or it lost focus; the shell changes a selected line.</summary>
        public event Action CurvatureCommitted;

        public event Action DeleteRequested;
        public event Action AutoRequested;
        public event Action PieceVisibilityToggled;

        public OmsiMarkingStyle Style { get; private set; } = OmsiMarkingStyle.LaneLine;

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
            deleteButton.SetEnabled(line != null);
            hideButton.SetEnabled(piece != null);
            hideButton.text = piece != null && piece.Hidden ? "Show piece" : "Hide piece";
            pieceButton.SetEnabled(piece != null);
            if (line == null)
                return;
            ShowStyle(piece != null && PieceScope ? piece.Style : line.Style);
            curvatureField.SetValueWithoutNotify(line.Curvature.HasValue ? (line.Curvature.Value * 100.0).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty);
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

        private void ShowStyle(OmsiMarkingStyle style)
        {
            Style = style;
            foreach (var pair in styleButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == style);
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
                button.EnableInClassList("panel-tab-active", style == Style);
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
    }
}
