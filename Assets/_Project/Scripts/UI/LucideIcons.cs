using System.Collections.Generic;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Path data of the Lucide icons the editor uses, from lucide-static 0.454.0 (https://lucide.dev), under the ISC
    /// license; see THIRD_PARTY_NOTICES.md. Each icon lists the <c>d</c> attributes of its elements on a 24 × 24 grid;
    /// <c>line</c> elements are written as paths and <c>circle</c> elements as two half arcs. Add icons here as tools
    /// need them.
    /// </summary>
    internal static class LucideIcons
    {
        public static readonly Dictionary<string, string[]> Paths = new Dictionary<string, string[]>
        {
            ["mouse-pointer-2"] = new[]
            {
                "M4.037 4.688a.495.495 0 0 1 .651-.651l16 6.5a.5.5 0 0 1-.063.947l-6.124 1.58a2 2 0 0 0-1.438 1.435l-1.579 6.126a.5.5 0 0 1-.947.063z"
            },
            ["move"] = new[]
            {
                "M12 2v20", "m15 19-3 3-3-3", "m19 9 3 3-3 3", "M2 12h20", "m5 9-3 3 3 3", "m9 5 3-3 3 3"
            },
            ["rotate-cw"] = new[]
            {
                "M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8", "M21 3v5h-5"
            },
            ["trash-2"] = new[]
            {
                "M3 6h18", "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", "M10 11v6", "M14 11v6"
            },
            ["box"] = new[]
            {
                "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z",
                "m3.3 7 8.7 5 8.7-5", "M12 22V12"
            },
            ["x"] = new[] { "M18 6 6 18", "m6 6 12 12" },
            ["folder"] = new[]
            {
                "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"
            },
            ["star"] = new[]
            {
                "M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z"
            },
            // The circle cx 11, cy 11, r 8.
            ["search"] = new[] { "M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0", "m21 21-4.3-4.3" },
            ["chevron-left"] = new[] { "m15 18-6-6 6-6" }
        };
    }
}
