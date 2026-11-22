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
    /// program, which stays in view whatever page is open, and the footer (the state of the changes, Discard, Apply). The title bar, the
    /// footer, the question about changes and the behaviour of the window are the shared <see cref="FloatingWindowFrame"/>; this class is
    /// the navigation and the pages; the shell fills them and decides what they show.
    /// </summary>
    internal sealed class SignalWindow : FloatingWindowFrame
    {
        /// <summary>The window is not made smaller than this by its grip.</summary>
        public static readonly Vector2 LeastSize = new Vector2(560f, 400f);

        private static readonly SignalArea[] AreaOrder = { SignalArea.Groups, SignalArea.Phases, SignalArea.Check, SignalArea.Assistant };
        private static readonly string[] AreaKeys = { "signals.nav.groups", "signals.nav.phases", "signals.nav.check", "signals.nav.assistant" };
        private static readonly string[] AreaTipKeys = { "signals.nav.groups.tooltip", "signals.nav.phases.tooltip", "signals.nav.check.tooltip", "signals.nav.assistant.tooltip" };
        private static readonly string[] AreaIcons = { "route", "list-ordered", "shield-check", "wand-sparkles" };

        private readonly Dictionary<SignalArea, Button> areaButtons = new Dictionary<SignalArea, Button>();
        private readonly Button[] subButtons = new Button[2];

        public SignalWindow(Action close, Action<SignalArea> areaChosen, Action<SignalTab> subPageChosen)
            : base(new FloatingWindowOptions
            {
                Icon = "traffic-light",
                TitleKey = "signals.window.title",
                CollapseTooltipKey = "signals.window.collapse.tooltip",
                ExpandTooltipKey = "signals.window.expand.tooltip",
                CloseTooltipKey = "signals.close.tooltip",
                ResizeTooltipKey = "signals.window.resize.tooltip",
                DefaultSize = new Vector2(780f, 640f),
                LeastSize = LeastSize
            }, close)
        {
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

            // 5. The question about changes not applied and the footer.
            CompleteFrame();
        }

        /// <summary>The page behind each of the two switch buttons of an area with two pages.</summary>
        private SignalTab[] SubPages = { SignalTab.Phases, SignalTab.Timeline };

        public VisualElement SubBar { get; }
        public VisualElement Body { get; }
        public VisualElement PreviewHost { get; }

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

        /// <summary>The crossing shown beside the title; empty for none.</summary>
        public void SetCrossingText(string text)
        {
            SetDetailText(text);
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
    }
}
