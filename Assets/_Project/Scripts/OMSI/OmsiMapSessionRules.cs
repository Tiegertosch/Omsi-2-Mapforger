using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Changes of the [rule] records after objects, such as the right of way of a junction's paths (ADR 047).</summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>Records that modify the element written just before them, as <see cref="OmsiTileEditing"/> keeps them with it.</summary>
        private static readonly HashSet<string> RuleSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rule", "kill_rule", "varparent", "spline_terrain_align", "spline_terrain_align_2"
        };

        /// <summary>
        /// Sets the right of way of paths of an object: for each path given, its [rule] and [kill_rule] priority records go,
        /// and a path with a value gets one [rule] in their place, right after the object, as the stock maps write it
        /// (<c>4 priority 64 0</c>). The object's other rules and the priorities of other paths stay as they are. One undo
        /// step; null when the tile would not change.
        /// </summary>
        /// <param name="priorities">By path: the priority, or null for none, which leaves the default 128.</param>
        public OmsiObjectChange SetPriorities(OmsiTileSummary tile, OmsiObjectRecord record, IDictionary<int, double?> priorities)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (priorities == null)
                throw new ArgumentNullException(nameof(priorities));

            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var before = workspace.ReadAllBytes(tilePath);
            var file = OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance);
            var at = file.Records.IndexOf(FindObject(file, record.Id, tile.FileName).Record);
            var following = new List<OmsiRecord>();
            for (var index = at + 1; index < file.Records.Count && RuleSections.Contains(file.Records[index].Name); index++)
                following.Add(file.Records[index]);
            foreach (var rule in following)
                if (IsPriorityFor(rule, priorities))
                    file.RemoveRecord(rule);
            var added = new List<OmsiPathRule>();
            var insertAt = at + 1;
            foreach (var pair in priorities.OrderBy(pair => pair.Key))
            {
                if (!pair.Value.HasValue)
                    continue;
                file.InsertRecord(insertAt++, file.CreateRecord("rule", new[]
                {
                    pair.Key.ToString(CultureInfo.InvariantCulture), "priority", pair.Value.Value.ToString("0.###", CultureInfo.InvariantCulture), "0"
                }));
                added.Add(new OmsiPathRule { PathIndex = pair.Key, Name = "priority", Value = pair.Value.Value });
            }
            var after = file.ToBytes();
            if (before.SequenceEqual(after))
                return null;

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Rules, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex)
            {
                RulesBefore = new List<OmsiPathRule>(record.Rules)
            };
            // The new rules come first, as they lie right after the object; the others keep their order.
            change.RulesAfter = new List<OmsiPathRule>(added);
            change.RulesAfter.AddRange(record.Rules.Where(rule => !(string.Equals(rule.Name, "priority", StringComparison.OrdinalIgnoreCase) && priorities.ContainsKey(rule.PathIndex))));
            AddFile(change, tilePath, before, after);
            WriteFiles(change, false);
            SetRules(record, change.RulesAfter);
            return change;
        }

        private static bool IsPriorityFor(OmsiRecord record, IDictionary<int, double?> priorities)
        {
            return (string.Equals(record.Name, "rule", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Name, "kill_rule", StringComparison.OrdinalIgnoreCase)) &&
                   record.Fields.Count > 1 && string.Equals(record.GetField(1), "priority", StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(record.GetField(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var path) && priorities.ContainsKey(path);
        }

        private static void SetRules(OmsiObjectRecord record, List<OmsiPathRule> rules)
        {
            record.Rules.Clear();
            record.Rules.AddRange(rules);
        }
    }
}
