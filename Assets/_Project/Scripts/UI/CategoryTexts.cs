using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The keys of the names and descriptions of the categories the libraries sort into. The model in <c>Editor.OMSI</c> names
    /// them in English; the interface binds its tabs to the keys here, so each key stands written out in full.
    /// </summary>
    internal static class CategoryTexts
    {
        // In the order of OmsiSplineCategory.
        private static readonly string[] SplineNames =
        {
            "roadtool.category.oneway", "roadtool.category.twolanes", "roadtool.category.fourlanes", "roadtool.category.sixlanes",
            "roadtool.category.median", "roadtool.category.rail", "roadtool.category.path", "roadtool.category.other"
        };

        private static readonly string[] SplineTooltips =
        {
            "roadtool.category.oneway.tooltip", "roadtool.category.twolanes.tooltip", "roadtool.category.fourlanes.tooltip", "roadtool.category.sixlanes.tooltip",
            "roadtool.category.median.tooltip", "roadtool.category.rail.tooltip", "roadtool.category.path.tooltip", "roadtool.category.other.tooltip"
        };

        // In the order of OmsiObjectCategory.
        private static readonly string[] ObjectNames =
        {
            "objects.category.buildings", "objects.category.trees", "objects.category.junctions", "objects.category.signs",
            "objects.category.stops", "objects.category.lights", "objects.category.rail", "objects.category.other"
        };

        private static readonly string[] ObjectTooltips =
        {
            "objects.category.buildings.tooltip", "objects.category.trees.tooltip", "objects.category.junctions.tooltip", "objects.category.signs.tooltip",
            "objects.category.stops.tooltip", "objects.category.lights.tooltip", "objects.category.rail.tooltip", "objects.category.other.tooltip"
        };

        public static string ObjectNameKey(OmsiObjectCategory category)
        {
            return ObjectNames[(int)category];
        }

        public static string ObjectTooltipKey(OmsiObjectCategory category)
        {
            return ObjectTooltips[(int)category];
        }

        public static string SplineNameKey(OmsiSplineCategory category)
        {
            return SplineNames[(int)category];
        }

        public static string SplineTooltipKey(OmsiSplineCategory category)
        {
            return SplineTooltips[(int)category];
        }
    }
}
