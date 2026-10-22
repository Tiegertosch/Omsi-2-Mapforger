using System.Collections.Generic;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Path data of the Lucide icons the editor uses, from lucide-static 0.454.0 (https://lucide.dev), under the ISC
    /// license; see THIRD_PARTY_NOTICES.md. Each icon lists the <c>d</c> attributes of its elements on a 24 × 24 grid;
    /// <c>line</c> elements are written as paths. Add icons here as tools need them.
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
            }
        };
    }
}
