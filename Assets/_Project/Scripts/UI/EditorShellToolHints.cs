using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The hint of the open tool (ADR 098, M2): what the next click does, what the road would be, the height under the pointer. It has
    /// a place of its own at the right of the status bar, so it never replaces a message, and it is gone when the tool's window closes.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private Label toolHintLabel;
        private string shownToolHint = string.Empty;
        private bool shownToolHintWarning;
        private bool toolHintFitRegistered;

        // The last hint of each tool window, kept while the window is hidden: a window often says its line just before it shows.
        private readonly Dictionary<VisualElement, (string Text, bool Warning)> toolHints = new Dictionary<VisualElement, (string Text, bool Warning)>();

        /// <summary>A tool window has a new line to say; it shows while that window is on screen.</summary>
        private void ShowToolHint(VisualElement source, string text, bool warning)
        {
            if (source == null)
                return;
            toolHints[source] = (text ?? string.Empty, warning);
            RefreshToolHint();
        }

        /// <summary>
        /// Shows the hint of the tool window on screen, or none. Called every frame as well: the windows are shown and hidden in many
        /// places, and asking them is cheaper and safer than telling each place to say so.
        /// </summary>
        private void RefreshToolHint()
        {
            if (toolHintLabel == null)
                return;
            if (!toolHintFitRegistered)
            {
                toolHintFitRegistered = true;
                toolHintLabel.RegisterCallback<GeometryChangedEvent>(_ => FitToolHint());
                toolHintLabel.parent?.RegisterCallback<GeometryChangedEvent>(_ => FitToolHint());
            }
            var text = string.Empty;
            var warning = false;
            if (objectsPanelHost != null && objectsPanelHost.style.display != DisplayStyle.None)
            {
                if (!OpenToolHint(markingOptions, ref text, ref warning) && !OpenToolHint(trafficOptions, ref text, ref warning) && !OpenToolHint(terrainOptions, ref text, ref warning) &&
                    !OpenToolHint(lineOptions, ref text, ref warning) && !OpenToolHint(laneOptions, ref text, ref warning) &&
                    !OpenToolHint(roadPanel, ref text, ref warning))
                    OpenToolHint(objectsPanel, ref text, ref warning);
            }
            // The signals window floats beside the map and is not in the row of tool windows: it says its line while it is on screen.
            if (activeTool == EditorTool.Signals)
                OpenToolHint(signalOptions, ref text, ref warning);
            // Where a new type or new labels of the selected element go while a chrono variant is edited: only when no window says its line.
            if (text.Length == 0)
                text = VariantNoteHint();
            if (text == shownToolHint && warning == shownToolHintWarning)
                return;
            shownToolHint = text;
            shownToolHintWarning = warning;
            toolHintLabel.text = HintLine(text);
            toolHintLabel.style.width = StyleKeyword.Auto;
            toolHintLabel.tooltip = text;
            toolHintLabel.EnableInClassList("status-hint-shown", text.Length > 0);
            toolHintLabel.EnableInClassList("status-warning", warning);
        }

        /// <summary>
        /// A text for the one line of the status bar: its further lines, such as the findings of a road plan, follow the first after a
        /// dot instead of being cut off. The tooltip keeps the lines.
        /// </summary>
        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0)
                return text;
            return string.Join("  ·  ", text.Replace("\r", string.Empty).Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Gives the hint the width of its text (up to its max-width in the style sheet). Left to Yoga, a hint wider than the bar was shrunk
        /// and clamped to 65 % while the message stayed at the basis 0 of the shrink pass, so the rest of the bar stayed empty and the hint
        /// ended near 40 % (ADR 098, M6 step 5, Nachtrag).
        /// </summary>
        private void FitToolHint()
        {
            if (toolHintLabel == null || toolHintLabel.resolvedStyle.display == DisplayStyle.None || string.IsNullOrEmpty(toolHintLabel.text))
                return;
            var size = toolHintLabel.MeasureTextSize(toolHintLabel.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            var resolved = toolHintLabel.resolvedStyle;
            var wanted = UnityEngine.Mathf.Ceil(size.x + resolved.paddingLeft + resolved.paddingRight + resolved.borderLeftWidth + resolved.borderRightWidth) + 2f;
            if (float.IsNaN(wanted) || wanted <= 0f)
                return;
            // The bar gives the hint at most this share; a percentage max-width in the style sheet came out at about 300 px here.
            var bar = toolHintLabel.parent.layout.width;
            if (bar > 0f)
                wanted = UnityEngine.Mathf.Min(wanted, UnityEngine.Mathf.Floor(bar * MaxHintShare));
            var current = toolHintLabel.style.width;
            if (current.keyword == StyleKeyword.Undefined && UnityEngine.Mathf.Abs(current.value.value - wanted) < 0.5f)
                return;
            toolHintLabel.style.width = wanted;
        }

        /// <summary>The longest text the status bar gets for a hint of several lines (the hint has at most 65 % of a window of 1100 px).</summary>
        internal const int MaxJoinedHintLength = 105;

        private const float MaxHintShare = 0.65f;

        /// <summary>
        /// A hint for the one line of the status bar: its lines follow each other after a dot when they fit; when they do not (the findings of
        /// a road plan), the first line stays with the count of the others. The tooltip keeps all lines.
        /// </summary>
        internal static string HintLine(string text)
        {
            var line = OneLine(text);
            if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0 || line.Length <= MaxJoinedHintLength)
                return line;
            var lines = text.Replace("\r", string.Empty).Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                return line;
            return lines[0] + "  (" + Loc.T("status.hint.more", ("count", lines.Length - 1)) + ")";
        }

        /// <summary>True when this tool window is the one on screen; its last hint goes to <paramref name="text"/>.</summary>
        private bool OpenToolHint(VisualElement source, ref string text, ref bool warning)
        {
            if (source == null || source.style.display != DisplayStyle.Flex)
                return false;
            if (toolHints.TryGetValue(source, out var hint))
            {
                text = hint.Text;
                warning = hint.Warning;
            }
            return true;
        }
    }
}
