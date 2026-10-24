using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What kind of scenery object a file holds, for the tabs of the object menu.</summary>
    public enum OmsiObjectCategory
    {
        Buildings,
        Trees,

        /// <summary>Objects with connection points for splines ([splinehelper]), such as crossings and turning loops.</summary>
        Junctions,

        /// <summary>Traffic signs, traffic lights and signals.</summary>
        Signs,

        /// <summary>Bus and tram stops with their shelters and poles.</summary>
        Stops,

        /// <summary>Street lights, masts and poles.</summary>
        Lights,

        /// <summary>Railway objects such as tracks, buffers, signals of the railway and catenary.</summary>
        Rail,

        Other
    }

    /// <summary>
    /// Sorts scenery objects into the tabs of the object menu. Authors name their [groups] in their own way, in German and
    /// English and often after streets, so the groups are matched by keywords, and the object file decides where it is
    /// clear: a [tree] is a tree, an object with [splinehelper] a junction. Without a matching group the folder names count.
    /// </summary>
    public static class OmsiObjectCategories
    {
        // In order: the first category whose keywords match wins, so rail signals count as rail and bus stop signs as stops.
        // Keywords count at the start of a word, so "tree" does not match "Street"; those starting with '*' are German
        // parts of words that may stand inside a compound, such as "baum" in "Straßenbaum".
        private static readonly KeyValuePair<OmsiObjectCategory, string[]>[] Keywords =
        {
            new KeyValuePair<OmsiObjectCategory, string[]>(OmsiObjectCategory.Trees,
                new[] { "tree", "*baum", "*bäume", "*baeume", "deciduous", "conifer", "plant", "*pflanz", "bush", "*busch", "*hecke", "hedge", "forest", "*wald", "vegetation" }),
            new KeyValuePair<OmsiObjectCategory, string[]>(OmsiObjectCategory.Junctions,
                new[] { "*kreuz", "crossing", "junction", "*einmünd", "*einmuend", "*wendeschleife", "turning loop" }),
            new KeyValuePair<OmsiObjectCategory, string[]>(OmsiObjectCategory.Stops,
                new[] { "*haltestell", "busstop", "bus stop", "*bushalt", "*wartehalle", "*wartehäus", "*wartehaeus", "shelter", "hst" }),
            new KeyValuePair<OmsiObjectCategory, string[]>(OmsiObjectCategory.Signs,
                new[] { "sign", "*schild", "*ampel", "traffic light", "signal", "*verkehrszeichen", "vz", "lsa", "*wegweiser" }),
            new KeyValuePair<OmsiObjectCategory, string[]>(OmsiObjectCategory.Lights,
                new[] { "light", "lamp", "*laterne", "*leuchte", "*beleuchtung", "mast", "pole", "*pfosten" }),
            new KeyValuePair<OmsiObjectCategory, string[]>(OmsiObjectCategory.Rail,
                new[] { "rail", "track", "*gleis", "*eisenbahn", "s-bahn", "tram", "*weiche", "*prellbock", "catenary", "*oberleitung", "*fahrleitung" }),
            new KeyValuePair<OmsiObjectCategory, string[]>(OmsiObjectCategory.Buildings,
                new[] { "building", "*gebaeude", "*gebäude", "*haus", "*häuser", "house", "residential", "commercial", "high rise", "*hochhaus", "industr",
                        "*kirche", "church", "school", "*schule", "education", "garage", "office", "shop", "*betriebshof", "depot", "*wohn", "*plattenbau", "villa" })
        };

        /// <param name="relativePath">The object's path relative to the installation; its file and folder names count when no group matches.</param>
        public static OmsiObjectCategory Of(string relativePath, IReadOnlyList<string> groups, bool isTree, bool hasSplineHelper)
        {
            if (isTree)
                return OmsiObjectCategory.Trees;
            if (hasSplineHelper)
                return OmsiObjectCategory.Junctions;
            // Authors go from the general to the particular, such as "East German Railway", "Lights": the last group first.
            if (groups != null)
                for (var index = groups.Count - 1; index >= 0; index--)
                {
                    var found = Match(groups[index]);
                    if (found.HasValue)
                        return found.Value;
                }
            var folder = System.IO.Path.GetDirectoryName(relativePath ?? string.Empty) ?? string.Empty;
            return Match(System.IO.Path.GetFileNameWithoutExtension(relativePath ?? string.Empty)) ?? Match(folder) ?? OmsiObjectCategory.Other;
        }

        private static OmsiObjectCategory? Match(string text)
        {
            var lower = text.ToLowerInvariant();
            foreach (var pair in Keywords)
                foreach (var keyword in pair.Value)
                {
                    var inside = keyword[0] == '*';
                    var word = inside ? keyword.Substring(1) : keyword;
                    for (var at = lower.IndexOf(word, StringComparison.Ordinal); at >= 0; at = lower.IndexOf(word, at + 1, StringComparison.Ordinal))
                        if (inside || at == 0 || !char.IsLetter(lower[at - 1]))
                            return pair.Key;
                }
            return null;
        }

        /// <summary>True when the text of an object file has a line [tree] or [splinehelper], read without parsing the whole file.</summary>
        public static void ReadMarks(string text, out bool isTree, out bool hasSplineHelper)
        {
            isTree = false;
            hasSplineHelper = false;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Equals("[tree]", StringComparison.OrdinalIgnoreCase))
                    isTree = true;
                else if (trimmed.Equals("[splinehelper]", StringComparison.OrdinalIgnoreCase))
                    hasSplineHelper = true;
            }
        }

        /// <summary>The name of a category for its tab.</summary>
        public static string Name(OmsiObjectCategory category)
        {
            switch (category)
            {
                case OmsiObjectCategory.Buildings: return "Buildings";
                case OmsiObjectCategory.Trees: return "Trees";
                case OmsiObjectCategory.Junctions: return "Junctions";
                case OmsiObjectCategory.Signs: return "Signs";
                case OmsiObjectCategory.Stops: return "Stops";
                case OmsiObjectCategory.Lights: return "Lights";
                case OmsiObjectCategory.Rail: return "Rail";
                default: return "Other";
            }
        }
    }
}
