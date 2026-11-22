using System.Collections.Generic;
using System.Text;

namespace OmsiMapEditor.UI
{
    /// <summary>What a type pick looks at to know that the situation it began in has changed.</summary>
    internal readonly struct TypePickContext
    {
        public TypePickContext(EditorTool tool, string selectionKey, object view, bool otherWindowOpen)
        {
            Tool = tool;
            SelectionKey = selectionKey ?? string.Empty;
            View = view;
            OtherWindowOpen = otherWindowOpen;
        }

        /// <summary>The active tool.</summary>
        public EditorTool Tool { get; }

        /// <summary>The selection as <see cref="TypePickRules.SelectionKey"/> writes it.</summary>
        public string SelectionKey { get; }

        /// <summary>The map or chrono view shown (the summary of the views); only compared by reference.</summary>
        public object View { get; }

        /// <summary>A dialog, window or the list above the bottom bar is open.</summary>
        public bool OtherWindowOpen { get; }
    }

    /// <summary>Why a type pick ended without choosing a type.</summary>
    internal enum TypePickEnd
    {
        None,
        ToolChanged,
        SelectionChanged,
        ViewChanged,
        WindowOpened,
    }

    /// <summary>
    /// When choosing a new object file for an element (ADR 060, M4 "Labels") ends by itself: another tool, another selection, another
    /// map or chrono view, or another window. It is decided here, from the situation the pick began in and the one now, so the
    /// shell only asks. Esc, the cancel button and the close button of the library end it as before (<see cref="EscapeOrder"/>).
    /// </summary>
    internal static class TypePickRules
    {
        /// <summary>
        /// Whether a pick may begin with this tool active: the tools that work on the selection have no window in the place of the
        /// library. Any other tool is left for Drag first (<c>EditorToolRules.IsAllowed</c> lets Drag always be chosen).
        /// </summary>
        public static bool CanStartIn(EditorTool tool)
        {
            return EditorToolRules.IsSelectionTool(tool);
        }

        /// <summary>Why the pick ends, or <see cref="TypePickEnd.None"/> while the situation is the one it began in.</summary>
        public static TypePickEnd Evaluate(TypePickContext start, TypePickContext now)
        {
            if (start.Tool != now.Tool)
                return TypePickEnd.ToolChanged;
            if (!ReferenceEquals(start.View, now.View))
                return TypePickEnd.ViewChanged;
            if (!string.Equals(start.SelectionKey, now.SelectionKey, System.StringComparison.Ordinal))
                return TypePickEnd.SelectionChanged;
            // A window that stood open already when the pick began does not end it; one that opens meanwhile does.
            if (now.OtherWindowOpen && !start.OtherWindowOpen)
                return TypePickEnd.WindowOpened;
            return TypePickEnd.None;
        }

        /// <summary>The selection as text: its objects by tile and ID (a spline by its negated ID), in order.</summary>
        public static string SelectionKey(IEnumerable<(int TileX, int TileY, int Id)> items)
        {
            var key = new StringBuilder();
            if (items != null)
                foreach (var item in items)
                    key.Append(item.TileX).Append(',').Append(item.TileY).Append(':').Append(item.Id).Append(';');
            return key.ToString();
        }

        /// <summary>The name of the reason for the log of a test run.</summary>
        public static string Describe(TypePickEnd reason)
        {
            switch (reason)
            {
                case TypePickEnd.ToolChanged: return "another tool was chosen";
                case TypePickEnd.SelectionChanged: return "the selection changed";
                case TypePickEnd.ViewChanged: return "another map or chrono view was shown";
                case TypePickEnd.WindowOpened: return "another window opened";
                default: return "none";
            }
        }
    }
}
