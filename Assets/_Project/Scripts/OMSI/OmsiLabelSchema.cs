using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The special kinds Omsi.exe gives a scenery object type, with the values of its kind byte (type + 0x1c4, set by the
    /// .sco loader at 0x7b5051..0x7b518e). Only the kinds that matter for labels and the editor are named.
    /// </summary>
    public enum OmsiSceneryObjectKind
    {
        None = 0,
        BusStop = 1,
        EntryPoint = 3,
        TrafficLight = 4,
        Signal = 5,
        Tree = 6,
        CarPark = 7,
        HelpArrow = 8,
        Switch = 9
    }

    /// <summary>Where the meaning of a label row comes from.</summary>
    public enum OmsiLabelFieldSource
    {
        /// <summary>A fixed field of the object's kind, such as a bus stop's name.</summary>
        Kind,

        /// <summary>A string variable the object's script names in a [stringvarnamelist] file.</summary>
        ScriptVariable,

        /// <summary>No name, but a [texttexture] of the object shows the label.</summary>
        TextTexture,

        /// <summary>No known meaning: OMSI calls it "Label n".</summary>
        Generic
    }

    /// <summary>One row of an object's labels: its index in the record and what it means.</summary>
    public sealed class OmsiLabelField
    {
        public const string GenericKey = "generic";
        public const string VariablePrefix = "var:";

        internal OmsiLabelField(int index, string key, OmsiLabelFieldSource source, string variableName = null)
        {
            Index = index;
            Key = key;
            Source = source;
            VariableName = variableName;
        }

        /// <summary>Position in the record's labels, 0 for the first.</summary>
        public int Index { get; }

        /// <summary>
        /// Stable key for the UI to name the row: a kind field such as <c>busstop_name</c> (see <see cref="OmsiLabelSchema"/>
        /// for the list), <c>var:&lt;name&gt;</c> for a script string variable, or <see cref="GenericKey"/> for a row OMSI
        /// shows as "Label <see cref="Index"/>".
        /// </summary>
        public string Key { get; }

        public OmsiLabelFieldSource Source { get; }

        /// <summary>The script string variable for <see cref="OmsiLabelFieldSource.ScriptVariable"/>, else null.</summary>
        public string VariableName { get; }

        public bool IsGeneric => Source == OmsiLabelFieldSource.Generic || Source == OmsiLabelFieldSource.TextTexture;

        public override string ToString()
        {
            return Index.ToString(CultureInfo.InvariantCulture) + " " + Key;
        }
    }

    /// <summary>
    /// The label fields a scenery object has, derived from its .sco the way the original OMSI editor's "Object Labels" dialog
    /// does (Omsi.exe: names set by the .sco loader at 0x7b57d2, rows built at 0x76b4a0):
    /// <list type="number">
    /// <item>The object's kind gives fixed names in front: a bus stop 7, a car park, entry point, signal, switch and traffic
    /// light 1, a tree 3 named and one unnamed (4).</item>
    /// <item>The names of the script's string variables ([stringvarnamelist] files, one name per line) follow in order.</item>
    /// <item>Any further label is generic; OMSI numbers it by its index from 0 ("Label 7" is the eighth label).</item>
    /// </list>
    /// MapWorks adds one thing the dialog does not: a [texttexture] whose source is a label index makes that label a
    /// field even without a name, because the object shows it.
    /// <para>
    /// Kind keys with OMSI's texts (DEU_basic(_200).olf / ENG_basic(_200).olf):
    /// busstop_name (ED_Label_name: Name / Name), busstop_pass_enter_max (Einsteigende Fahrgäste (max) / Entering Pass. (max)),
    /// busstop_pass_enter_min (Einsteigende Fahrgäste (min) / Entering Pass. (min)), busstop_pass_exit (Aussteigende Fahrgäste /
    /// Exiting Pass.), busstop_dockdist (Anlege-Entfernung/Bahnsteiglänge / Docking distance/platform length), busstop_side
    /// (Ein-/Ausstiegsseite, 0 - R, 1 - L, 2 - beide / Entrance/exit side, 0 - R, 1 - L, 2 - both; on [LHT] maps OMSI shows
    /// ED_Label_Busstop_side_LHT: 0 - L, 1 - R), busstop_subname (Namenszusatz / Sub name), entrypoint_name, signal_name and
    /// switch_name (Name), trafficlight_number ("Nr. Traffic Light", not translated), carpark_index (ED_Label_index:
    /// Parklistenindex / Parking list index), tree_texture, tree_height, tree_ratio (internal names, not translated),
    /// tree_spare (no name). Generic rows: ED_Label + " " + index (Beschriftung / Label).
    /// </para>
    /// </summary>
    public sealed class OmsiLabelSchema
    {
        public static readonly OmsiLabelSchema Empty = new OmsiLabelSchema(new List<OmsiLabelField>());

        private readonly List<OmsiLabelField> fields;

        private OmsiLabelSchema(List<OmsiLabelField> fields)
        {
            this.fields = fields;
        }

        /// <summary>The fields the object type itself defines, by index, without generic rows past the last one.</summary>
        public IReadOnlyList<OmsiLabelField> Fields => fields;

        public int Count => fields.Count;

        /// <summary>The fixed names of a kind in label order, as the .sco loader writes them.</summary>
        public static IReadOnlyList<string> KindKeys(OmsiSceneryObjectKind kind)
        {
            switch (kind)
            {
                case OmsiSceneryObjectKind.BusStop:
                    return new[]
                    {
                        "busstop_name", "busstop_pass_enter_max", "busstop_pass_enter_min", "busstop_pass_exit", "busstop_dockdist",
                        "busstop_side", "busstop_subname"
                    };
                case OmsiSceneryObjectKind.EntryPoint:
                    return new[] { "entrypoint_name" };
                case OmsiSceneryObjectKind.Signal:
                    return new[] { "signal_name" };
                case OmsiSceneryObjectKind.Switch:
                    return new[] { "switch_name" };
                case OmsiSceneryObjectKind.TrafficLight:
                    return new[] { "trafficlight_number" };
                case OmsiSceneryObjectKind.CarPark:
                    return new[] { "carpark_index" };
                case OmsiSceneryObjectKind.Tree:
                    return new[] { "tree_texture", "tree_height", "tree_ratio", "tree_spare" };
                default:
                    return new string[0];
            }
        }

        /// <summary>
        /// Builds the schema of an object type.
        /// </summary>
        /// <param name="stringVariableNames">The names of the script's string variables in order (all [stringvarnamelist] files after each other).</param>
        public static OmsiLabelSchema Build(OmsiSceneryObjectKind kind, IReadOnlyList<string> stringVariableNames, IEnumerable<string> textTextureSources)
        {
            var result = new List<OmsiLabelField>();
            foreach (var key in KindKeys(kind))
                result.Add(new OmsiLabelField(result.Count, key, OmsiLabelFieldSource.Kind));
            if (stringVariableNames != null)
                foreach (var name in stringVariableNames)
                    result.Add(new OmsiLabelField(result.Count, OmsiLabelField.VariablePrefix + name, OmsiLabelFieldSource.ScriptVariable, name));
            if (textTextureSources != null)
                foreach (var source in textTextureSources)
                {
                    if (source == null || !int.TryParse(source.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                        index < 0 || index >= 4096)
                        continue;
                    while (result.Count <= index)
                        result.Add(new OmsiLabelField(result.Count, OmsiLabelField.GenericKey, OmsiLabelFieldSource.Generic));
                    if (result[index].Source == OmsiLabelFieldSource.Generic)
                        result[index] = new OmsiLabelField(index, OmsiLabelField.GenericKey, OmsiLabelFieldSource.TextTexture);
                }
            return new OmsiLabelSchema(result);
        }

        /// <summary>Builds the schema of a parsed .sco; <paramref name="stringVariableNames"/> are the names its lists hold.</summary>
        public static OmsiLabelSchema Build(OmsiSceneryObject sceneryObject, IReadOnlyList<string> stringVariableNames)
        {
            if (sceneryObject == null)
                throw new ArgumentNullException(nameof(sceneryObject));
            return Build(sceneryObject.Kind, stringVariableNames, sceneryObject.TextTextureSources);
        }

        /// <summary>
        /// The names in a [stringvarnamelist] file: one per line, blank lines skipped. Read with OMSI's encoding rules.
        /// </summary>
        public static List<string> ReadStringVariableNames(byte[] bytes)
        {
            var names = new List<string>();
            if (bytes == null)
                return names;
            var text = OmsiTextFile.Decode(bytes);
            foreach (var line in text.Split('\n'))
            {
                var name = line.Trim();
                if (name.Length > 0)
                    names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// The rows to show for a record with <paramref name="labelCount"/> labels: every schema field, and generic rows for
        /// labels past the schema, so no existing label is ever hidden. At least <paramref name="minimumRows"/> rows.
        /// </summary>
        public IReadOnlyList<OmsiLabelField> RowsFor(int labelCount, int minimumRows = 0)
        {
            var count = Math.Max(fields.Count, Math.Max(labelCount, minimumRows));
            var rows = new List<OmsiLabelField>(count);
            rows.AddRange(fields);
            for (var index = fields.Count; index < count; index++)
                rows.Add(new OmsiLabelField(index, OmsiLabelField.GenericKey, OmsiLabelFieldSource.Generic));
            return rows;
        }

        /// <summary>
        /// The labels to write after editing the rows: trailing empty rows are left out, but never below the count the record
        /// had, so an untouched object stays at 0 labels and no existing label is dropped. The rows past the edited list keep
        /// their current text.
        /// </summary>
        public static IReadOnlyList<string> LabelsToWrite(IReadOnlyList<string> current, IReadOnlyList<string> edited)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));
            if (edited == null)
                throw new ArgumentNullException(nameof(edited));
            var last = -1;
            for (var index = 0; index < edited.Count; index++)
                if (!string.IsNullOrEmpty(edited[index]))
                    last = index;
            var count = Math.Max(current.Count, last + 1);
            var result = new string[count];
            for (var index = 0; index < count; index++)
                result[index] = index < edited.Count ? edited[index] ?? string.Empty : current[index];
            return result;
        }

        /// <summary>
        /// The rows the original OMSI editor's dialog builds (0x76b4a0), for checking against its screenshots: the fixed names
        /// of the kind, then the type's name list from the first index the kind did not name, then "Label i" from the
        /// list's length up to max(<paramref name="minimumRows"/>, label count) - 1. An empty name stays an unnamed row.
        /// Keys are the kind keys, <c>name:&lt;text&gt;</c> for list entries and <c>generic:&lt;i&gt;</c>.
        /// </summary>
        public static List<string> OmsiDialogRows(OmsiSceneryObjectKind dialogKind, IReadOnlyList<string> typeNames, int labelCount, int minimumRows)
        {
            var rows = new List<string>();
            var fixedKeys = KindKeys(dialogKind);
            // The dialog has no fixed names for a tree; it shows the type's list from the first entry.
            var named = dialogKind == OmsiSceneryObjectKind.Tree ? 0 : fixedKeys.Count;
            for (var index = 0; index < named; index++)
                rows.Add(fixedKeys[index]);
            for (var index = named; index < typeNames.Count; index++)
                rows.Add("name:" + typeNames[index]);
            var limit = Math.Max(minimumRows, labelCount);
            for (var index = typeNames.Count; index < limit; index++)
                rows.Add("generic:" + index.ToString(CultureInfo.InvariantCulture));
            return rows;
        }
    }

    /// <summary>
    /// Label schemas of the scenery objects of one OMSI installation, read once per object file. Asset paths are relative to
    /// the installation folder, as tile files store them; a missing or unreadable file gives <see cref="OmsiLabelSchema.Empty"/>.
    /// </summary>
    public sealed class OmsiLabelSchemaCatalog
    {
        private readonly string installationRoot;
        private readonly Dictionary<string, OmsiLabelSchema> schemas = new Dictionary<string, OmsiLabelSchema>(StringComparer.OrdinalIgnoreCase);

        public OmsiLabelSchemaCatalog(string installationRoot)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("An installation folder is required.", nameof(installationRoot));
            this.installationRoot = Path.GetFullPath(installationRoot);
        }

        /// <summary>Why the last object file could not be read, for diagnostics; null when it was read.</summary>
        public string LastProblem { get; private set; }

        public OmsiLabelSchema Get(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return OmsiLabelSchema.Empty;
            var key = KeyOf(assetPath);
            if (key == null)
            {
                LastProblem = "Cannot read " + assetPath + ": not a path relative to the installation.";
                return OmsiLabelSchema.Empty;
            }
            if (!schemas.TryGetValue(key, out var schema))
            {
                schema = Build(assetPath);
                schemas[key] = schema;
            }
            return schema;
        }

        /// <summary>The cache key of an asset path: <c>Objects/x.sco</c>, <c>Objects\x.sco</c> and <c>.\Objects\x.sco</c> are one file. Null for a path that is not relative.</summary>
        public static string KeyOf(string assetPath)
        {
            try
            {
                return OmsiRelativePath.Normalize(assetPath);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private OmsiLabelSchema Build(string assetPath)
        {
            LastProblem = null;
            string path;
            OmsiSceneryObject sceneryObject;
            try
            {
                path = Path.Combine(installationRoot, OmsiRelativePath.Normalize(assetPath));
                sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException ||
                                              exception is NotSupportedException)
            {
                LastProblem = "Cannot read " + assetPath + ": " + exception.Message;
                return OmsiLabelSchema.Empty;
            }

            var folder = Path.GetDirectoryName(path) ?? installationRoot;
            var names = new List<string>();
            foreach (var list in sceneryObject.StringVariableLists)
            {
                var listPath = Path.Combine(folder, list.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    names.AddRange(OmsiLabelSchema.ReadStringVariableNames(File.ReadAllBytes(listPath)));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException ||
                                                  exception is NotSupportedException)
                {
                    // OMSI skips a list it cannot find; the labels stay generic from there on.
                    LastProblem = "Cannot read " + list + " of " + assetPath + ": " + exception.Message;
                }
            }
            return OmsiLabelSchema.Build(sceneryObject, names);
        }
    }
}
