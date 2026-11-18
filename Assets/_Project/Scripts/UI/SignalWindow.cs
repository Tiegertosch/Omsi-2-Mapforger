using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The window of the signals tool (ADR 085, ADR 098; decision of 15 November 2026): one window that floats beside the map like the one
    /// for pictures from the air and terrain, in five areas from top to bottom: the title bar (the system and its crossing, collapse and
    /// close), the navigation (signal groups, phases and times, check, assistant), the work area of the open page, the preview of the
    /// program, which stays in view whatever page is open, and the footer (the state of the changes, Discard, Apply). This class is the
    /// frame; the shell fills the pages and the preview and decides what they show.
    /// </summary>
    internal sealed class SignalWindow : VisualElement
    {
        /// <summary>The window is not made smaller than this by its grip.</summary>
        public static readonly Vector2 LeastSize = new Vector2(560f, 400f);

        private static readonly SignalArea[] AreaOrder = { SignalArea.Groups, SignalArea.Phases, SignalArea.Check, SignalArea.Assistant };
        private static readonly string[] AreaKeys = { "signals.nav.groups", "signals.nav.phases", "signals.nav.check", "signals.nav.assistant" };
        private static readonly string[] AreaTipKeys = { "signals.nav.groups.tooltip", "signals.nav.phases.tooltip", "signals.nav.check.tooltip", "signals.nav.assistant.tooltip" };
        private static readonly string[] AreaIcons = { "route", "list-ordered", "shield-check", "wand-sparkles" };

        private readonly Dictionary<SignalArea, Button> areaButtons = new Dictionary<SignalArea, Button>();
        private readonly Button[] subButtons = new Button[2];
        private readonly Label detailLabel = new Label();
        private readonly Button collapseButton;
        private readonly VisualElement promptRow = new VisualElement();
        private readonly Label promptLabel = new Label();
        /// <summary>The space kept to the edges of the area at the first place and when the window is made fit.</summary>
        public const float EdgeMargin = 8f;

        /// <summary>
        /// The room the bottom bar takes at the bottom of the area, with its gap; the window stays above it. The shell sets it from
        /// the measured height of the bar (<see cref="BottomBarLayout"/>).
        /// </summary>
        public float BottomInset
        {
            get => limits.BottomInset;
            set => limits.BottomInset = value;
        }

        private readonly FloatingWindow.Limits limits;
        private bool collapsed;
        private bool placed;
        private float expandedHeight;

        public SignalWindow(Action close, Action<SignalArea> areaChosen, Action<SignalTab> subPageChosen)
        {
            AddToClassList("floating-panel");
            AddToClassList("signal-frame");

            // 1. Title bar: the handle for moving the window.
            Header = new VisualElement();
            Header.AddToClassList("floating-panel-header");
            Header.AddToClassList("signal-header");
            var badge = new IconElement("traffic-light");
            badge.AddToClassList("floating-panel-icon");
            Header.Add(badge);
            var title = LocBindings.NewLabel("signals.window.title");
            title.AddToClassList("floating-panel-title");
            title.AddToClassList("signal-title");
            Header.Add(title);
            detailLabel.AddToClassList("signal-title-detail");
            Header.Add(detailLabel);
            collapseButton = new Button(() => Collapsed = !Collapsed);
            collapseButton.AddToClassList("panel-collapse-button");
            collapseButton.Add(new IconElement("minus"));
            LocBindings.Tooltip(collapseButton, "signals.window.collapse.tooltip");
            Header.Add(collapseButton);
            var closeButton = LocBindings.WithTooltip(new Button(close), "signals.close.tooltip");
            closeButton.AddToClassList("panel-collapse-button");
            closeButton.Add(new IconElement("x"));
            Header.Add(closeButton);
            Add(Header);

            Content = new VisualElement();
            Content.AddToClassList("floating-panel-content");
            Content.AddToClassList("signal-content");
            Add(Content);

            // 2. Navigation: the areas, the assistant by its name, and under them the pages of an area that has two.
            var nav = new VisualElement();
            nav.AddToClassList("panel-tabs");
            nav.AddToClassList("signal-nav");
            for (var index = 0; index < AreaOrder.Length; index++)
            {
                var area = AreaOrder[index];
                var button = LocBindings.WithTooltip(new Button(() => areaChosen(area)), AreaTipKeys[index]); // i18n:keys
                button.AddToClassList("panel-tab");
                button.AddToClassList("signal-nav-button");
                if (area == SignalArea.Assistant)
                    button.AddToClassList("signal-nav-assistant");
                button.Add(new IconElement(AreaIcons[index]));
                var label = LocBindings.NewLabel(AreaKeys[index]); // i18n:keys
                label.pickingMode = PickingMode.Ignore;
                button.Add(label);
                areaButtons[area] = button;
                nav.Add(button);
            }
            Content.Add(nav);
            SubBar = new VisualElement();
            SubBar.AddToClassList("signal-subbar");
            SubBar.style.display = DisplayStyle.None;
            for (var index = 0; index < subButtons.Length; index++)
            {
                subButtons[index] = new Button();
                subButtons[index].AddToClassList("signal-sub-button");
                var chosen = index;
                subButtons[index].clicked += () => subPageChosen(SubPages[chosen]);
                SubBar.Add(subButtons[index]);
            }
            Content.Add(SubBar);

            // 3. The work area: the pages, one at a time, each scrolling inside it.
            Body = new VisualElement();
            Body.AddToClassList("signal-body");
            Content.Add(Body);

            // 4. The preview, whatever the page.
            PreviewHost = new VisualElement();
            PreviewHost.AddToClassList("signal-preview");
            Content.Add(PreviewHost);

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

            // 5. The footer.
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
            LocBindings.Tooltip(Grip, "signals.window.resize.tooltip");
            Add(Grip);

            // The window keeps its size within the area when that gets smaller (another resolution), above the bottom bar like at the first place.
            limits = new FloatingWindow.Limits
            {
                Least = LeastSize,
                Margin = EdgeMargin,
                Collapsed = () => collapsed,
                Height = () => collapsed ? expandedHeight : layout.height,
                HeightFitted = height => expandedHeight = height
            };
            FloatingWindow.MakeMovable(this, Header, null, limits);
            FloatingWindow.MakeResizable(this, Grip, LeastSize, () => expandedHeight = layout.height, limits);

            // The window is opaque and a sibling of the map, so pointer and wheel over it never reach the map.
            RegisterCallback<GeometryChangedEvent>(_ => PlaceFirstTime());
        }

        /// <summary>The page behind each of the two switch buttons of an area with two pages.</summary>
        private SignalTab[] SubPages = { SignalTab.Phases, SignalTab.Timeline };

        public VisualElement Header { get; }
        public VisualElement Content { get; }
        public VisualElement SubBar { get; }
        public VisualElement Body { get; }
        public VisualElement PreviewHost { get; }
        public VisualElement Footer { get; }
        public Label StatusLabel { get; }
        public Button DiscardButton { get; }
        public Button ApplyButton { get; }
        public Button PromptApply { get; }
        public Button PromptDiscard { get; }
        public Button PromptKeep { get; }
        public VisualElement Grip { get; }

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
                LocBindings.Tooltip(collapseButton, value ? "signals.window.expand.tooltip" : "signals.window.collapse.tooltip"); // i18n:keys
            }
        }

        /// <summary>A page of the work area; it takes what room the area has and scrolls inside it.</summary>
        public void AddPage(VisualElement page)
        {
            page.AddToClassList("signal-page");
            Body.Add(page);
        }

        /// <summary>An area of the navigation can be chosen or not (the assistant needs a crossing it may change or keep a program of).</summary>
        public void SetAreaEnabled(SignalArea area, bool enabled)
        {
            areaButtons[area].SetEnabled(enabled);
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

        /// <summary>The crossing shown beside the title; empty for none.</summary>
        public void SetCrossingText(string text)
        {
            detailLabel.text = text ?? string.Empty;
            detailLabel.tooltip = text ?? string.Empty;
            detailLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Marks the area of the open page and offers the switch between the two pages of an area that has two.</summary>
        public void ShowArea(SignalTab page)
        {
            var area = SignalWindowRules.AreaOf(page);
            foreach (var pair in areaButtons)
                pair.Value.EnableInClassList("panel-tab-active", pair.Key == area);
            var two = SignalWindowRules.HasSubPages(area);
            SubBar.style.display = two ? DisplayStyle.Flex : DisplayStyle.None;
            if (!two)
                return;
            SubPages = area == SignalArea.Phases ? new[] { SignalTab.Phases, SignalTab.Timeline } : new[] { SignalTab.Wizard, SignalTab.Templates };
            var keys = area == SignalArea.Phases ? new[] { "signals.sub.phases", "signals.sub.timeline" } : new[] { "signals.sub.proposal", "signals.sub.templates" };
            var tips = area == SignalArea.Phases ? new[] { "signals.sub.phases.tooltip", "signals.sub.timeline.tooltip" }
                : new[] { "signals.sub.proposal.tooltip", "signals.sub.templates.tooltip" };
            for (var index = 0; index < subButtons.Length; index++)
            {
                LocBindings.Text(subButtons[index], keys[index]); // i18n:keys
                LocBindings.Tooltip(subButtons[index], tips[index]); // i18n:keys
                subButtons[index].EnableInClassList("panel-tab-active", SubPages[index] == page);
            }
        }

        /// <summary>The question about unapplied changes: shown while one waits for an answer.</summary>
        public void ShowPrompt(string text, bool show)
        {
            promptLabel.text = text ?? string.Empty;
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
            var size = FloatingWindowRules.ClampSize(new Vector2(780f, 640f), LeastSize, FloatingWindowRules.MaxSize(new Vector2(area.width, area.height), Margin, BottomInset));
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
