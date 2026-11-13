using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Shows the <see cref="VisualElement.tooltip"/> of the element under the pointer as a card above it, in the style of the
    /// references in <c>UI Research/</c> (ADR 098, stage 2). The player draws no tooltips of its own. A text shaped like
    /// "Name (Key): text" with a name of up to three words gets it as an upper-case title and the key as a key cap; any other text shows as it is.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private const long TooltipDelayMilliseconds = 450;

        // "Drag (Q): Click objects..." or "Tiles: the list..."; the French space before the colon is allowed.
        private static readonly Regex TooltipHeading = new Regex(@"^([^:.!?\n]{1,40}?)\s?(?:\(([^)]{1,12})\))?\s?:\s+(.+)$", RegexOptions.Singleline);

        private VisualElement tooltipCard;
        private Label tooltipTitle;
        private Label tooltipKey;
        private Label tooltipText;
        private VisualElement tooltipTarget;
        private IVisualElementScheduledItem tooltipTimer;
        private bool tooltipSuppressed;

        private void SetUpTooltips(VisualElement root)
        {
            tooltipCard = new VisualElement { name = "shell-tooltip", pickingMode = PickingMode.Ignore };
            tooltipCard.AddToClassList("shell-tooltip");
            var heading = new VisualElement { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("shell-tooltip-heading");
            tooltipTitle = new Label { pickingMode = PickingMode.Ignore };
            tooltipTitle.AddToClassList("shell-tooltip-title");
            tooltipKey = new Label { pickingMode = PickingMode.Ignore };
            tooltipKey.AddToClassList("shell-tooltip-key");
            heading.Add(tooltipTitle);
            heading.Add(tooltipKey);
            tooltipText = new Label { pickingMode = PickingMode.Ignore };
            tooltipText.AddToClassList("shell-tooltip-text");
            tooltipCard.Add(heading);
            tooltipCard.Add(tooltipText);
            tooltipCard.style.display = DisplayStyle.None;
            tooltipCard.RegisterCallback<GeometryChangedEvent>(_ => PlaceTooltip());
            root.Add(tooltipCard);

            root.RegisterCallback<PointerOverEvent>(evt => PointTooltipAt(evt.target as VisualElement), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip(false));
            // A click or the wheel puts the card away until the pointer reaches another element.
            root.RegisterCallback<PointerDownEvent>(_ => HideTooltip(true), TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(_ => HideTooltip(true), TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(_ => HideTooltip(true), TrickleDown.TrickleDown);
        }

        internal static bool SplitCardTooltip(string tooltip, out string title, out string text)
        {
            var at = tooltip.IndexOf(LocBindings.TooltipSeparator);
            title = at < 0 ? string.Empty : tooltip.Substring(0, at);
            text = at < 0 ? string.Empty : tooltip.Substring(at + 1);
            return at >= 0;
        }

        /// <summary>The parts of a tooltip card. The title is not upper-cased yet; the key cap is empty when <see cref="HasKeyCap"/> is false.</summary>
        internal readonly struct TooltipParts
        {
            public TooltipParts(bool hasTitle, string title, bool hasKeyCap, string keyCap, string text, bool showsText)
            {
                HasTitle = hasTitle;
                Title = title;
                HasKeyCap = hasKeyCap;
                KeyCap = keyCap;
                Text = text;
                ShowsText = showsText;
            }

            public bool HasTitle { get; }
            public string Title { get; }
            public bool HasKeyCap { get; }
            public string KeyCap { get; }
            public string Text { get; }
            public bool ShowsText { get; }
        }

        /// <summary>
        /// Decides how a tooltip is shown: a card tooltip (explicit title, any length, no key cap), else "Name (Key): text" with a name of
        /// up to three words, else the whole text without a title.
        /// </summary>
        internal static TooltipParts SplitTooltip(string tooltip)
        {
            if (SplitCardTooltip(tooltip, out var cardTitle, out var cardText))
                return new TooltipParts(true, cardTitle, false, string.Empty, cardText, cardText.Length > 0);
            var match = TooltipHeading.Match(tooltip);
            // A longer head is the start of a sentence, not a name.
            var titled = match.Success && match.Groups[1].Value.Trim().Split(' ').Length <= 3;
            if (!titled)
                return new TooltipParts(false, string.Empty, false, string.Empty, tooltip, true);
            return new TooltipParts(true, match.Groups[1].Value.Trim(), match.Groups[2].Success, match.Groups[2].Value, match.Groups[3].Value, true);
        }

        private static VisualElement TooltipOwner(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (!string.IsNullOrEmpty(current.tooltip))
                    return current;
            return null;
        }

        private void PointTooltipAt(VisualElement element)
        {
            var owner = TooltipOwner(element);
            if (owner == tooltipTarget)
                return;
            HideTooltip(false);
            tooltipSuppressed = false;
            if (owner == null)
                return;
            tooltipTarget = owner;
            tooltipTimer = rootElement.schedule.Execute(() => ShowTooltip(owner)).StartingIn(TooltipDelayMilliseconds);
        }

        /// <summary>Shows the card of an element at once; the text is read now, so it follows a change of language.</summary>
        private void ShowTooltip(VisualElement owner)
        {
            tooltipTimer = null;
            if (tooltipCard == null || tooltipSuppressed || owner.panel == null || string.IsNullOrEmpty(owner.tooltip))
                return;
            tooltipTarget = owner;
            var parts = SplitTooltip(owner.tooltip);
            tooltipTitle.text = parts.HasTitle ? parts.Title.ToUpper(CultureInfo.CurrentCulture) : string.Empty;
            tooltipTitle.style.display = parts.HasTitle ? DisplayStyle.Flex : DisplayStyle.None;
            tooltipKey.text = parts.KeyCap;
            tooltipKey.style.display = parts.HasKeyCap ? DisplayStyle.Flex : DisplayStyle.None;
            tooltipText.text = parts.Text;
            tooltipText.style.display = parts.ShowsText ? DisplayStyle.Flex : DisplayStyle.None;
            // Hidden until placed, so it never flashes at its old place.
            tooltipCard.style.visibility = Visibility.Hidden;
            tooltipCard.style.display = DisplayStyle.Flex;
            tooltipCard.BringToFront();
            PlaceTooltip();
        }

        /// <summary>Puts the card above its element, left-aligned with it, or below when there is no room; it stays inside the window.</summary>
        private void PlaceTooltip()
        {
            if (tooltipCard == null || tooltipCard.resolvedStyle.display == DisplayStyle.None || tooltipTarget == null)
                return;
            if (tooltipTarget.panel == null)
            {
                HideTooltip(false);
                return;
            }
            var root = tooltipCard.parent;
            var size = tooltipCard.layout.size;
            if (root == null || float.IsNaN(size.x) || size.x <= 0f)
                return;
            var target = root.WorldToLocal(tooltipTarget.worldBound);
            var bounds = root.layout;
            const float gap = 6f;
            var x = Mathf.Clamp(target.xMin, 4f, Mathf.Max(4f, bounds.width - size.x - 4f));
            var y = TooltipTopEdge(target.yMin, root) - size.y - gap;
            if (y < 4f)
                y = Mathf.Min(target.yMax + gap, bounds.height - size.y - 4f);
            tooltipCard.style.left = x;
            tooltipCard.style.top = y;
            tooltipCard.style.visibility = Visibility.Visible;
        }

        /// <summary>
        /// The edge the card sits above: the top of the element, or, inside a tool catalog (<c>picker-panel</c>) but outside its
        /// header row, the top of that panel, so the card never covers the tabs.
        /// </summary>
        private float TooltipTopEdge(float elementTop, VisualElement root)
        {
            VisualElement panel = null;
            for (var current = tooltipTarget; current != null; current = current.parent)
            {
                if (current.ClassListContains("picker-header"))
                    return elementTop;
                if (current.ClassListContains("picker-panel"))
                {
                    panel = current;
                    break;
                }
            }
            return panel == null ? elementTop : Mathf.Min(elementTop, root.WorldToLocal(panel.worldBound).yMin);
        }

        private void HideTooltip(bool suppress)
        {
            tooltipTimer?.Pause();
            tooltipTimer = null;
            tooltipSuppressed |= suppress;
            if (!suppress)
                tooltipTarget = null;
            if (tooltipCard != null)
                tooltipCard.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Command line: <c>-tooltipAt x y</c> (panel pixels from the top left) shows the tooltip of the element at that point, as hovering
        /// would, for a screenshot; the text goes to the log as "Tooltip:".
        /// </summary>
        private void RunTooltipArguments(string[] args)
        {
            var index = Array.IndexOf(args, "-tooltipAt");
            if (index < 0 || index + 2 >= args.Length || rootElement?.panel == null)
                return;
            if (!float.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(args[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return;
            var owner = TooltipOwner(rootElement.panel.Pick(new Vector2(x, y)));
            Debug.Log("Tooltip: " + (owner == null ? "none" : owner.tooltip.Replace(LocBindings.TooltipSeparator.ToString(), " | ")));
            if (owner != null)
                ShowTooltip(owner);
        }
    }
}
