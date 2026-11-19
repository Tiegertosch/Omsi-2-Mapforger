using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>What a floating tool window says about itself: its icon, title, the tooltips of its title bar buttons and its sizes.</summary>
    internal sealed class FloatingWindowOptions
    {
        public string Icon;
        public string TitleKey;
        public string CollapseTooltipKey;
        public string ExpandTooltipKey;
        public string CloseTooltipKey;
        public string ResizeTooltipKey;

        /// <summary>The size the window takes at its first place, within what the area allows.</summary>
        public Vector2 DefaultSize = new Vector2(780f, 640f);

        /// <summary>The window is not made smaller than this by its grip.</summary>
        public Vector2 LeastSize = new Vector2(560f, 400f);
    }

    /// <summary>
    /// The frame the tool windows that float beside the map share (the signals window, the road type window; ADR 098): the title bar
    /// as the handle for moving (icon, title, detail text, collapse, close), a content area, the question about unsaved changes and the
    /// footer (state, discard, main action) at its bottom, and the grip for the size. It keeps itself inside the area around it and above
    /// the bottom bar, takes its first place from that area and keeps it while the element lives, so hiding and showing it again finds it
    /// where it was. The USS classes keep the names <c>signal-*</c> they were made with, so a window built on the frame looks as the
    /// signals window always did. A subclass fills <see cref="Content"/> and then calls <see cref="CompleteFrame"/>, which adds the
    /// question, the footer and the grip below what it put in.
    /// </summary>
    internal abstract class FloatingWindowFrame : VisualElement
    {
        /// <summary>The space kept to the edges of the area at the first place and when the window is made fit.</summary>
        public const float EdgeMargin = 8f;

        private readonly FloatingWindowOptions options;
        private readonly Label detailLabel = new Label();
        private readonly Button collapseButton;
        private readonly VisualElement promptRow = new VisualElement();
        private readonly Label promptLabel = new Label();
        private readonly FloatingWindow.Limits limits;
        private bool collapsed;
        private bool placed;
        private float expandedHeight;

        protected FloatingWindowFrame(FloatingWindowOptions options, Action close)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            AddToClassList("floating-panel");
            AddToClassList("signal-frame");

            // The title bar: the handle for moving the window.
            Header = new VisualElement();
            Header.AddToClassList("floating-panel-header");
            Header.AddToClassList("signal-header");
            var badge = new IconElement(options.Icon);
            badge.AddToClassList("floating-panel-icon");
            Header.Add(badge);
            var title = LocBindings.NewLabel(options.TitleKey); // i18n:keys
            title.AddToClassList("floating-panel-title");
            title.AddToClassList("signal-title");
            Header.Add(title);
            detailLabel.AddToClassList("signal-title-detail");
            Header.Add(detailLabel);
            collapseButton = new Button(() => Collapsed = !Collapsed);
            collapseButton.AddToClassList("panel-collapse-button");
            collapseButton.AddToClassList("signal-collapse-button");
            collapseButton.Add(new IconElement("minus"));
            LocBindings.Tooltip(collapseButton, options.CollapseTooltipKey); // i18n:keys
            Header.Add(collapseButton);
            var closeButton = LocBindings.WithTooltip(new Button(close), options.CloseTooltipKey); // i18n:keys
            closeButton.AddToClassList("panel-collapse-button");
            closeButton.Add(new IconElement("x"));
            Header.Add(closeButton);
            Add(Header);

            Content = new VisualElement();
            Content.AddToClassList("floating-panel-content");
            Content.AddToClassList("signal-content");
            Add(Content);

            // The window keeps its size within the area when that gets smaller (another resolution), above the bottom bar like at the first place.
            limits = new FloatingWindow.Limits
            {
                Least = options.LeastSize,
                Margin = EdgeMargin,
                Collapsed = () => collapsed,
                Height = () => collapsed ? expandedHeight : layout.height,
                HeightFitted = height => expandedHeight = height
            };
        }

        public VisualElement Header { get; }
        public VisualElement Content { get; }
        public VisualElement Footer { get; private set; }
        public Label StatusLabel { get; private set; }
        public Button DiscardButton { get; private set; }
        public Button ApplyButton { get; private set; }
        public Button PromptApply { get; private set; }
        public Button PromptDiscard { get; private set; }
        public Button PromptKeep { get; private set; }
        public VisualElement Grip { get; private set; }

        /// <summary>
        /// The room the bottom bar takes at the bottom of the area, with its gap; the window stays above it. The shell sets it from
        /// the measured height of the bar (<see cref="BottomBarLayout"/>).
        /// </summary>
        public float BottomInset
        {
            get => limits.BottomInset;
            set => limits.BottomInset = value;
        }

        /// <summary>
        /// Adds the question about unsaved changes, the footer and the grip under what the subclass put into <see cref="Content"/>, and
        /// makes the window movable and resizable. Called once, last in the subclass's constructor.
        /// </summary>
        protected void CompleteFrame()
        {
            if (Footer != null)
                throw new InvalidOperationException("The frame is complete already.");

            // The question about changes not applied, above the footer.
            promptRow.AddToClassList("signal-prompt");
            promptRow.style.display = DisplayStyle.None;
            promptLabel.AddToClassList("signal-prompt-text");
            promptRow.Add(promptLabel);
            PromptApply = new Button();
            PromptDiscard = new Button();
            PromptKeep = new Button();
            foreach (var button in new[] { PromptApply, PromptDiscard, PromptKeep })
            {
                button.AddToClassList("map-control-button");
                button.AddToClassList("signal-prompt-button");
                promptRow.Add(button);
            }
            PromptApply.AddToClassList("dialog-primary");
            Content.Add(promptRow);

            // The footer.
            Footer = new VisualElement();
            Footer.AddToClassList("signal-footer");
            StatusLabel = new Label();
            StatusLabel.AddToClassList("signal-footer-status");
            Footer.Add(StatusLabel);
            DiscardButton = new Button();
            DiscardButton.AddToClassList("map-control-button");
            DiscardButton.AddToClassList("signal-footer-button");
            ApplyButton = new Button();
            ApplyButton.AddToClassList("map-control-button");
            ApplyButton.AddToClassList("signal-footer-button");
            ApplyButton.AddToClassList("dialog-primary");
            Footer.Add(DiscardButton);
            Footer.Add(ApplyButton);
            Content.Add(Footer);

            Grip = new VisualElement();
            Grip.AddToClassList("signal-grip");
            LocBindings.Tooltip(Grip, options.ResizeTooltipKey); // i18n:keys
            Add(Grip);

            FloatingWindow.MakeMovable(this, Header, null, limits);
            FloatingWindow.MakeResizable(this, Grip, options.LeastSize, () => expandedHeight = layout.height, limits);

            // The window is opaque and a sibling of the map, so pointer and wheel over it never reach the map.
            RegisterCallback<GeometryChangedEvent>(_ => PlaceFirstTime());
        }

        public bool Collapsed
        {
            get => collapsed;
            set
            {
                if (collapsed == value)
                    return;
                collapsed = value;
                EnableInClassList("floating-panel-collapsed", value);
                EnableInClassList("signal-frame-collapsed", value);
                if (Grip != null)
                    Grip.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                // The height the window was given comes back when it opens again; collapsed it is as high as its title bar.
                if (value)
                {
                    expandedHeight = layout.height;
                    style.height = StyleKeyword.Auto;
                }
                else if (expandedHeight > 0f)
                    style.height = expandedHeight;
                collapseButton.Clear();
                collapseButton.Add(new IconElement(value ? "chevron-down" : "minus"));
                LocBindings.Tooltip(collapseButton, value ? options.ExpandTooltipKey : options.CollapseTooltipKey); // i18n:keys
            }
        }

        /// <summary>For checks by command line: the window at a place and in a size, as if the person had put it there.</summary>
        public void PlaceAt(float x, float y, float width, float height)
        {
            placed = true;
            if (width > 0f)
                style.width = width;
            if (height > 0f)
            {
                style.height = height;
                expandedHeight = height;
            }
            style.left = x;
            style.top = y;
        }

        /// <summary>The text beside the title (the crossing, the road type); empty for none.</summary>
        public void SetDetailText(string text)
        {
            detailLabel.text = text ?? string.Empty;
            detailLabel.tooltip = text ?? string.Empty;
            detailLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>The question about unsaved changes, given as its translation key so it follows a language change: shown while one waits for an answer.</summary>
        public void ShowPrompt(string key, bool show)
        {
            LocBindings.Text(promptLabel, key); // i18n:keys
            if (key == null)
                promptLabel.text = string.Empty;
            promptRow.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public bool PromptShown => promptRow.style.display == DisplayStyle.Flex;

        /// <summary>The window has taken its place and the layout shows it there: only then can something be kept clear of it.</summary>
        public bool IsLaidOut
        {
            get
            {
                if (!placed || resolvedStyle.display == DisplayStyle.None || layout.width < 1f || layout.height < 1f)
                    return false;
                return style.left.keyword == StyleKeyword.Undefined && style.top.keyword == StyleKeyword.Undefined &&
                       Mathf.Abs(layout.x - style.left.value.value) < 1f && Mathf.Abs(layout.y - style.top.value.value) < 1f;
            }
        }

        /// <summary>The visible area of the map beside the window, in panel pixels: the area of the parent above the bottom bar.</summary>
        public Rect VisibleMapArea()
        {
            if (parent == null)
                return default;
            var area = parent.worldBound;
            return Rect.MinMaxRect(area.xMin, area.yMin, area.xMax, Mathf.Max(area.yMin, area.yMax - BottomInset));
        }

        /// <summary>
        /// The first time the window is laid out it takes its size from the area around it and stands at its right, below the top edge and
        /// above the bottom bar; after that only the person moves it.
        /// </summary>
        private void PlaceFirstTime()
        {
            if (placed || parent == null || resolvedStyle.display == DisplayStyle.None)
                return;
            var area = parent.contentRect;
            if (area.width < 1f || area.height < 1f || layout.width < 1f)
                return;
            placed = true;
            const float Margin = EdgeMargin;
            var size = FloatingWindowRules.ClampSize(options.DefaultSize, options.LeastSize, FloatingWindowRules.MaxSize(new Vector2(area.width, area.height), Margin, BottomInset));
            style.width = size.x;
            expandedHeight = size.y;
            // A window collapsed before it was first laid out stays as high as its title bar.
            if (!collapsed)
                style.height = size.y;
            var place = FloatingWindowRules.FirstPlace(size, new Vector2(area.width, area.height), Margin);
            style.left = place.x;
            style.top = place.y;
        }
    }
}
