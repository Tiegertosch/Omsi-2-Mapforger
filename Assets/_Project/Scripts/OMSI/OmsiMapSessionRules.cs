using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What <see cref="OmsiMapSession.SetSplinePathRules"/> changes on one spline: rules of the names on the paths go, the rules given come.</summary>
    public sealed class OmsiSplinePathRules
    {
        public OmsiTileSummary Tile { get; set; }
        public OmsiSplineRecord Spline { get; set; }

        /// <summary>The paths whose rules of the names go; null for all.</summary>
        public ICollection<int> Paths { get; set; }

        public ICollection<string> Names { get; set; }
        public List<OmsiPathRule> Rules { get; } = new List<OmsiPathRule>();
    }

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

        /// <summary>The rules that say who may use a path and how fast (ADR 049, stage 4 of the AI paths).</summary>
        public static readonly IReadOnlyList<string> TrafficRuleNames = new[] { "speedlimit", "trafficdensity", "no_cars", "bus", "trucks" };

        /// <summary>
        /// Sets rules of an object's paths: on the paths given, its [rule] records of the names given go (a traffic density
        /// only for the first traffic group, as the others belong to the map's own groups), and the rules given take their
        /// place after the object's other rules. One undo step; null when the tile would not change.
        /// </summary>
        public OmsiObjectChange SetObjectPathRules(OmsiTileSummary tile, OmsiObjectRecord record, ICollection<int> paths, ICollection<string> names,
            IEnumerable<OmsiPathRule> rules)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var before = workspace.ReadAllBytes(tilePath);
            var file = OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance);
            var added = ReplaceRules(file, FindObject(file, record.Id, tile.FileName).Record, paths, names, rules);
            var after = file.ToBytes();
            if (before.SequenceEqual(after))
                return null;

            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Rules, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex)
            {
                RulesBefore = new List<OmsiPathRule>(record.Rules)
            };
            change.RulesAfter = new List<OmsiPathRule>(record.Rules.Where(rule => !Replaced(rule.PathIndex, rule.Name, rule.Extra, rule.Kills, paths, names)));
            change.RulesAfter.AddRange(added);
            AddFile(change, tilePath, before, after);
            WriteFiles(change, false);
            SetRules(record, change.RulesAfter);
            return change;
        }

        /// <summary>
        /// Sets rules of splines' paths as <see cref="SetObjectPathRules"/> does for an object, for several splines at once,
        /// such as every piece of a road: one undo step for all their tiles; null when nothing would change.
        /// </summary>
        public OmsiSplineChange SetSplinePathRules(OmsiMapSummary summary, IEnumerable<OmsiSplinePathRules> edits)
        {
            if (edits == null)
                throw new ArgumentNullException(nameof(edits));
            var files = new EditedFiles(RequireWorkspace());
            var change = new OmsiSplineChange(OmsiSplineChangeKind.Rules);
            foreach (var edit in edits)
            {
                var tileFile = files.Tile(edit.Tile.FileName);
                OmsiSplineView view = null;
                foreach (var candidate in OmsiTileRecords.Of<OmsiSplineView>(tileFile))
                    if (candidate.Id == edit.Spline.Id)
                        view = candidate;
                if (view == null)
                    throw new InvalidOperationException("Spline " + edit.Spline.Id + " was not found in " + edit.Tile.FileName + ".");
                ReplaceRules(tileFile, view.Record, edit.Paths, edit.Names, edit.Rules);
                if (!change.SplineIds.Contains(edit.Spline.Id))
                    change.SplineIds.Add(edit.Spline.Id);
                if (!change.Tiles.Contains(edit.Tile))
                    change.Tiles.Add(edit.Tile);
            }
            files.Collect(change.Files);
            if (change.Files.Count == 0)
                return null;
            WriteFiles(change.Files, false);
            RefreshSplines(summary, change);
            return change;
        }

        /// <summary>
        /// Takes the element's rules of the names given away from the paths given (all paths for null) and writes the new
        /// ones after the records that follow the element, where the stock maps keep them. Returns the rules written.
        /// </summary>
        private static List<OmsiPathRule> ReplaceRules(OmsiRecordFile file, OmsiRecord element, ICollection<int> paths, ICollection<string> names,
            IEnumerable<OmsiPathRule> rules)
        {
            var at = file.Records.IndexOf(element);
            var following = new List<OmsiRecord>();
            for (var index = at + 1; index < file.Records.Count && RuleSections.Contains(file.Records[index].Name); index++)
                following.Add(file.Records[index]);
            var end = at + 1 + following.Count;
            foreach (var record in following)
            {
                var isRule = string.Equals(record.Name, "rule", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(record.Name, "kill_rule", StringComparison.OrdinalIgnoreCase);
                if (!isRule || record.Fields.Count < 2 ||
                    !int.TryParse(record.GetField(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var path))
                    continue;
                var extra = 0.0;
                if (record.Fields.Count > 3)
                    double.TryParse(record.GetField(3), NumberStyles.Float, CultureInfo.InvariantCulture, out extra);
                if (Replaced(path, record.GetField(1), extra, false, paths, names))
                {
                    file.RemoveRecord(record);
                    end--;
                }
            }
            var written = new List<OmsiPathRule>();
            foreach (var rule in rules ?? Enumerable.Empty<OmsiPathRule>())
            {
                var counted = string.Equals(rule.Name, "speedlimit", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(rule.Name, "trafficdensity", StringComparison.OrdinalIgnoreCase);
                file.InsertRecord(end++, file.CreateRecord("rule", new[]
                {
                    rule.PathIndex.ToString(CultureInfo.InvariantCulture), rule.Name,
                    rule.Value.ToString(counted ? "0.000" : "0.###", CultureInfo.InvariantCulture), rule.Extra.ToString("0.###", CultureInfo.InvariantCulture)
                }));
                written.Add(new OmsiPathRule(rule.PathIndex, rule.Name, rule.Value, rule.Extra));
            }
            return written;
        }

        /// <summary>Whether a rule goes: one of the names on one of the paths; of the traffic densities only that of the first group.</summary>
        private static bool Replaced(int path, string name, double extra, bool kills, ICollection<int> paths, ICollection<string> names)
        {
            if (kills || paths != null && !paths.Contains(path) || names == null || !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                return false;
            return !string.Equals(name, "trafficdensity", StringComparison.OrdinalIgnoreCase) || Math.Abs(extra) < 0.5;
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
