using System.Collections.Generic;
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
            var text = string.Empty;
            var warning = false;
            if (objectsPanelHost != null && objectsPanelHost.style.display != DisplayStyle.None)
            {
                if (!OpenToolHint(markingOptions, ref text, ref warning) && !OpenToolHint(terrainOptions, ref text, ref warning) &&
                    !OpenToolHint(roadPanel, ref text, ref warning))
                    OpenToolHint(objectsPanel, ref text, ref warning);
            }
            if (text == shownToolHint && warning == shownToolHintWarning)
                return;
            shownToolHint = text;
            shownToolHintWarning = warning;
            toolHintLabel.text = OneLine(text);
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
