using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The names and descriptions of marking styles, fills and arrow kinds in the language in use. The model in <c>Editor.OMSI</c>
    /// names them in English; the interface asks here. Each text has its key written out in full, so the key check can find it.
    /// </summary>
    internal static class MarkingTexts
    {
        // In the order of the enums.
        private static readonly string[] StyleNames =
        {
            "markings.style.laneline", "markings.style.boundary", "markings.style.double", "markings.style.edge", "markings.style.brokenedge",
            "markings.style.stop", "markings.style.giveway", "markings.style.zebra", "markings.style.yellowlaneline", "markings.style.yellowboundary"
        };

        private static readonly string[] StyleInline =
        {
            "markings.style.laneline.inline", "markings.style.boundary.inline", "markings.style.double.inline", "markings.style.edge.inline", "markings.style.brokenedge.inline",
            "markings.style.stop.inline", "markings.style.giveway.inline", "markings.style.zebra.inline", "markings.style.yellowlaneline.inline", "markings.style.yellowboundary.inline"
        };

        private static readonly string[] StyleDescriptions =
        {
            "markings.style.laneline.description", "markings.style.boundary.description", "markings.style.double.description", "markings.style.edge.description",
            "markings.style.brokenedge.description", "markings.style.stop.description", "markings.style.giveway.description", "markings.style.zebra.description",
            "markings.style.yellowlaneline.description", "markings.style.yellowboundary.description"
        };

        private static readonly string[] FillNames = { "markings.fill.hatched", "markings.fill.yellowhatched" };

        private static readonly string[] FillInline = { "markings.fill.hatched.inline", "markings.fill.yellowhatched.inline" };

        private static readonly string[] FillDescriptions = { "markings.fill.hatched.description", "markings.fill.yellowhatched.description" };

        private static readonly string[] ArrowNames =
        {
            "markings.arrow.straight", "markings.arrow.left", "markings.arrow.right", "markings.arrow.straightleft", "markings.arrow.straightright",
            "markings.arrow.leftright", "markings.arrow.all"
        };

        private static readonly string[] ArrowInline =
        {
            "markings.arrow.straight.inline", "markings.arrow.left.inline", "markings.arrow.right.inline", "markings.arrow.straightleft.inline",
            "markings.arrow.straightright.inline", "markings.arrow.leftright.inline", "markings.arrow.all.inline"
        };

        public static string StyleName(OmsiMarkingStyle style)
        {
            return Loc.T(StyleNames[(int)style]); // i18n:keys
        }

        /// <summary>The name as it reads inside a sentence.</summary>
        public static string StyleInlineName(OmsiMarkingStyle style)
        {
            return Loc.T(StyleInline[(int)style]); // i18n:keys
        }

        public static string StyleDescription(OmsiMarkingStyle style)
        {
            return Loc.T(StyleDescriptions[(int)style]); // i18n:keys
        }

        public static string FillName(OmsiMarkingFill fill)
        {
            return Loc.T(FillNames[(int)fill]); // i18n:keys
        }

        public static string FillInlineName(OmsiMarkingFill fill)
        {
            return Loc.T(FillInline[(int)fill]); // i18n:keys
        }

        public static string FillDescription(OmsiMarkingFill fill)
        {
            return Loc.T(FillDescriptions[(int)fill]); // i18n:keys
        }

        public static string ArrowName(OmsiMarkingArrowKind kind)
        {
            return Loc.T(ArrowNames[(int)kind]); // i18n:keys
        }

        public static string ArrowInlineName(OmsiMarkingArrowKind kind)
        {
            return Loc.T(ArrowInline[(int)kind]); // i18n:keys
        }
    }
}
