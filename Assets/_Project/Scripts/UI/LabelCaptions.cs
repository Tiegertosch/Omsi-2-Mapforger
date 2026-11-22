using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The names of the label rows in the inspector (M4 "Labels"): maps the key of an <see cref="OmsiLabelField"/> to a text key of
    /// the interface texts. Script variables show their own name, which is not translated.
    /// </summary>
    internal static class LabelCaptions
    {
        /// <summary>Spare empty rows after the last label field. 0 shows only what the object type names (the original editor's extra rows are believed to be a stale-list artefact).</summary>
        public const int SpareRows = 0;

        public const string GenericKey = "labels.field.generic";
        public const string TextKey = "labels.field.text";

        private static readonly Dictionary<string, string> KindKeys = new Dictionary<string, string>
        {
            { "busstop_name", "labels.field.busstop.name" },
            { "busstop_pass_enter_max", "labels.field.busstop.pass_enter_max" },
            { "busstop_pass_enter_min", "labels.field.busstop.pass_enter_min" },
            { "busstop_pass_exit", "labels.field.busstop.pass_exit" },
            { "busstop_dockdist", "labels.field.busstop.dockdist" },
            { "busstop_side", "labels.field.busstop.side" },
            { "busstop_subname", "labels.field.busstop.subname" },
            { "carpark_index", "labels.field.carpark.index" },
            { "trafficlight_number", "labels.field.trafficlight.number" },
            { "entrypoint_name", "labels.field.name" },
            { "signal_name", "labels.field.name" },
            { "switch_name", "labels.field.name" },
            { "tree_texture", "labels.field.tree.texture" },
            { "tree_height", "labels.field.tree.height" },
            { "tree_ratio", "labels.field.tree.ratio" }
        };

        private const string SideLeftHandKey = "labels.field.busstop.side_lht";

        /// <summary>Every kind key of <see cref="OmsiLabelSchema.KindKeys"/> that has a caption of its own (the spare row of a tree has none).</summary>
        public static IReadOnlyCollection<string> CaptionedKindKeys => KindKeys.Keys;

        /// <summary>
        /// The text key naming a row, or null for a script variable (it shows its variable name). A row without a name of its own (generic, the
        /// spare tree row) gives <see cref="GenericKey"/>, which needs the row's index; a row a [texttexture] shows gives <see cref="TextKey"/>.
        /// </summary>
        public static string KeyFor(OmsiLabelField field, bool leftHandTraffic)
        {
            switch (field.Source)
            {
                case OmsiLabelFieldSource.ScriptVariable:
                    return null;
                case OmsiLabelFieldSource.TextTexture:
                    return TextKey;
                case OmsiLabelFieldSource.Kind:
                    if (leftHandTraffic && field.Key == "busstop_side")
                        return SideLeftHandKey;
                    return KindKeys.TryGetValue(field.Key, out var key) ? key : GenericKey;
                default:
                    return GenericKey;
            }
        }

        /// <summary>The caption of a row in the current language.</summary>
        public static string Text(OmsiLabelField field, bool leftHandTraffic)
        {
            var key = KeyFor(field, leftHandTraffic);
            if (key == null)
                return field.VariableName;
            if (key == GenericKey)
                return Loc.T("labels.field.generic", ("index", field.Index));
            return Loc.T(key); // i18n:keys
        }

        /// <summary>The technical name for the tooltip: the OMSI key, the script variable or the label's number.</summary>
        public static string Technical(OmsiLabelField field)
        {
            if (field.Source == OmsiLabelFieldSource.ScriptVariable)
                return "stringvar " + field.VariableName;
            if (field.Source == OmsiLabelFieldSource.Kind)
                return field.Key;
            return (field.Source == OmsiLabelFieldSource.TextTexture ? "texttexture, " : string.Empty) + "label " + field.Index;
        }

        /// <summary>Whether a schema needs to know the traffic side (only a bus stop's entrance/exit side differs).</summary>
        public static bool NeedsTrafficSide(IEnumerable<OmsiLabelField> rows)
        {
            foreach (var row in rows)
                if (row.Source == OmsiLabelFieldSource.Kind && row.Key == "busstop_side")
                    return true;
            return false;
        }
    }
}
