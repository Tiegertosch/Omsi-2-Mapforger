using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A spline record that a chrono variant's overlay shares with the base map and the copy with other rules that takes its place.</summary>
    internal sealed class OmsiSplineSwap
    {
        public OmsiTileSummary Tile;
        public OmsiSplineRecord Before;
        public OmsiSplineRecord After;
    }

    /// <summary>
    /// Rules of objects and splines while a chrono variant is edited (ADR 064). The elements the variant added itself are
    /// edited in its change list like in a tile. Any other element gets a [selobject] or [selspline] there with
    /// [kill_rule] and [rule] after it, as the stock variants do, and the base map stays as it was.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>Whether the rules of an element go into the edited variant's change list as commands, and not into the record's own file.</summary>
        private bool RulesViaSelection(object element)
        {
            return EditedVariant != null && !IsInEditedVariant(element);
        }

        /// <summary>The file that holds the record of an element whose rules are edited in place: its tile, or the variant's change list for an element the variant added.</summary>
        private string RulesFilePath(OmsiTileSummary tile, object element)
        {
            return EditedVariant != null && IsInEditedVariant(element) ? chronoOrigins[element].File : OmsiRelativePath.Normalize(tile.FileName);
        }

        private static OmsiPathRule CloneRule(OmsiPathRule rule)
        {
            return new OmsiPathRule(rule.PathIndex, rule.Name, rule.Value, rule.Extra);
        }

        /// <summary>
        /// Gives an object the rules <paramref name="after"/> by commands in the edited variant's change list, one undo step.
        /// A copy of the record takes its place in the tile, as the overlay shares the base map's record. Null when the rules are the same.
        /// </summary>
        private OmsiObjectChange SetObjectRulesInVariant(OmsiTileSummary tile, OmsiObjectRecord record, List<OmsiPathRule> after)
        {
            if (!tile.Objects.Contains(record))
                throw new ArgumentException("The object is not in the tile; it may have been replaced by a newer record.", nameof(record));
            var workspace = RequireWorkspace();
            var path = ChronoFileOf(tile);
            var before = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
            var file = before != null ? OmsiRecordFile.Parse(before, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(tile);
            if (!WriteRuleCommands(file, "selobject", record.Id, record.Rules, after))
                return null;
            var written = file.ToBytes();

            var copy = OmsiChrono.Copy(record);
            SetRules(copy, after);
            var listIndex = tile.Objects.IndexOf(record);
            var change = new OmsiObjectChange(OmsiObjectChangeKind.Rules, tile, record, listIndex < 0 ? tile.Objects.Count : listIndex)
            {
                RulesBefore = new List<OmsiPathRule>(record.Rules),
                RulesAfter = after,
                ElementId = record.Id,
                ElementBefore = record,
                ElementAfter = copy
            };
            AddFile(change, path, before, written);
            WriteFiles(change, false);
            ApplyElementChange(change, true);
            RegisterChronoFile(path);
            return change;
        }

        /// <summary>
        /// Sets the rules of splines while a variant is edited: for a spline of the base map or an earlier variant commands
        /// in the variant's change list, for one the variant added its own record. One undo step for all splines;
        /// null when nothing would change. Copies of the records take the shared ones' place.
        /// </summary>
        private OmsiSplineChange SetSplineRulesInVariant(IEnumerable<OmsiSplinePathRules> edits)
        {
            var workspace = RequireWorkspace();
            var files = new Dictionary<string, OmsiRecordFile>(StringComparer.OrdinalIgnoreCase);
            var befores = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            var change = new OmsiSplineChange(OmsiSplineChangeKind.Rules);
            foreach (var edit in edits)
            {
                var spline = edit.Spline;
                if (!edit.Tile.Splines.Contains(spline))
                    throw new InvalidOperationException("Spline " + spline.Id + " is not in " + edit.Tile.FileName + ".");
                var after = spline.Rules.Where(rule => !rule.Kills && !Replaced(rule.PathIndex, rule.Name, rule.Extra, false, edit.Paths, edit.Names))
                    .Select(CloneRule).ToList();
                after.AddRange(edit.Rules.Select(CloneRule));
                if (SameRules(spline.Rules.Where(rule => !rule.Kills), after))
                    continue;

                var own = IsInEditedVariant(spline);
                var path = own ? chronoOrigins[spline].File : ChronoFileOf(edit.Tile);
                if (!files.TryGetValue(path, out var file))
                {
                    var bytes = workspace.FileExists(path) ? workspace.ReadAllBytes(path) : null;
                    file = bytes != null ? OmsiRecordFile.Parse(bytes, OmsiTileRecordLayouts.Instance) : NewChronoTileFor(edit.Tile);
                    files.Add(path, file);
                    befores.Add(path, bytes);
                    order.Add(path);
                }
                if (own)
                {
                    OmsiSplineView view = null;
                    foreach (var candidate in OmsiTileRecords.Of<OmsiSplineView>(file))
                        if (candidate.Id == spline.Id)
                            view = candidate;
                    if (view == null)
                        throw new InvalidOperationException("Spline " + spline.Id + " was not found in " + path + ".");
                    ReplaceRules(file, view.Record, edit.Paths, edit.Names, edit.Rules);
                }
                else
                    WriteRuleCommands(file, "selspline", spline.Id, spline.Rules, after);

                var copy = OmsiChrono.Copy(spline);
                copy.Rules.Clear();
                copy.Rules.AddRange(after);
                change.Swaps.Add(new OmsiSplineSwap { Tile = edit.Tile, Before = spline, After = copy });
                if (!change.SplineIds.Contains(spline.Id))
                    change.SplineIds.Add(spline.Id);
                if (!change.Tiles.Contains(edit.Tile))
                    change.Tiles.Add(edit.Tile);
            }

            foreach (var path in order)
            {
                var written = files[path].ToBytes();
                if (befores[path] == null || !SameBytes(befores[path], written))
                    change.Files.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = befores[path], After = written });
            }
            if (change.Files.Count == 0)
                return null;
            ApplyFiles(change.Files, false);
            SwapSplines(change, true);
            foreach (var state in change.Files)
                RegisterChronoFile(state.RelativePath);
            return change;
        }

        /// <summary>
        /// Gives the copy of an object, made in an edited variant, the rules of the original as the overlay shows them. The
        /// modifier records copied from the file that holds the original lack what variants changed since. Returns the rules written.
        /// </summary>
        private static List<OmsiPathRule> SetEffectiveRules(OmsiRecordFile file, OmsiRecord element, IEnumerable<OmsiPathRule> rules)
        {
            var wanted = rules.Where(rule => !rule.Kills).Select(CloneRule).ToList();
            var at = file.Records.IndexOf(element);
            var end = at + 1;
            while (end < file.Records.Count && RuleSections.Contains(file.Records[end].Name))
                end++;
            var existing = new List<OmsiRecord>();
            for (var index = at + 1; index < end; index++)
                if (string.Equals(file.Records[index].Name, "rule", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(file.Records[index].Name, "kill_rule", StringComparison.OrdinalIgnoreCase))
                    existing.Add(file.Records[index]);
            var read = new List<OmsiPathRule>();
            foreach (var record in existing)
                if (OmsiChrono.TryReadRule(record, out var rule))
                    read.Add(rule);
            if (existing.Count == read.Count && read.TrueForAll(rule => !rule.Kills) && SameRules(read, wanted))
                return wanted;
            foreach (var record in existing)
            {
                file.RemoveRecord(record);
                end--;
            }
            foreach (var rule in wanted)
                file.InsertRecord(end++, RuleRecord(file, "rule", rule));
            return wanted;
        }

        /// <summary>Puts the copies of a spline change into the tiles, or the shared records back when undoing.</summary>
        private void SwapSplines(OmsiSplineChange change, bool forward)
        {
            foreach (var swap in change.Swaps)
            {
                var from = forward ? swap.Before : swap.After;
                var to = forward ? swap.After : swap.Before;
                var at = swap.Tile.Splines.IndexOf(from);
                if (at < 0)
                    throw new InvalidOperationException("Spline " + from.Id + " is no longer in " + swap.Tile.FileName + ".");
                swap.Tile.Splines[at] = to;
                if (chronoOrigins != null && chronoOrigins.TryGetValue(from, out var origin))
                    chronoOrigins[to] = origin;
            }
        }

        private void RegisterChronoFile(string path)
        {
            if (!EditedVariant.TileFiles.Exists(known => string.Equals(known, path, StringComparison.OrdinalIgnoreCase)))
                EditedVariant.TileFiles.Add(path);
        }

        /// <summary>Whether two rule lists hold the same rules, in any order.</summary>
        private static bool SameRules(IEnumerable<OmsiPathRule> a, IEnumerable<OmsiPathRule> b)
        {
            string Key(OmsiPathRule rule) => rule.PathIndex.ToString(CultureInfo.InvariantCulture) + "|" + rule.Name.ToLowerInvariant() + "|" +
                                             rule.Extra.ToString("R", CultureInfo.InvariantCulture) + "|" + rule.Value.ToString("R", CultureInfo.InvariantCulture);
            return a.Select(Key).OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(b.Select(Key).OrderBy(key => key, StringComparer.Ordinal));
        }

        /// <summary>
        /// Writes the commands that turn the rules <paramref name="before"/> of an element into <paramref name="after"/> into
        /// its block in a change list. For each path, name and extra number whose rules differ, the block holds at most one
        /// [kill_rule] (which takes away every earlier rule of that kind, whatever its value) and then the [rule]s wanted, so
        /// editing the same element again replaces its commands and does not pile up more. Returns false when nothing differs.
        /// </summary>
        private static bool WriteRuleCommands(OmsiRecordFile file, string selector, int id, IEnumerable<OmsiPathRule> before, IEnumerable<OmsiPathRule> after)
        {
            var was = before.Where(rule => !rule.Kills).ToList();
            var will = after.Where(rule => !rule.Kills).ToList();
            var keys = new List<OmsiPathRule>();
            foreach (var rule in was.Concat(will))
                if (!keys.Exists(known => OmsiChrono.SameRuleKey(known, rule)))
                    keys.Add(rule);
            keys.RemoveAll(key => SameRules(was.Where(rule => OmsiChrono.SameRuleKey(rule, key)), will.Where(rule => OmsiChrono.SameRuleKey(rule, key))));
            if (keys.Count == 0)
                return false;

            OpenSelectionBlock(file, selector, id, out var selectAt, out var blockEnd);
            var killedBefore = new List<OmsiPathRule>();
            for (var index = blockEnd - 1; index > selectAt; index--)
            {
                var record = file.Records[index];
                var isRule = string.Equals(record.Name, "rule", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(record.Name, "kill_rule", StringComparison.OrdinalIgnoreCase);
                if (!isRule || !OmsiChrono.TryReadRule(record, out var old) || !keys.Exists(key => OmsiChrono.SameRuleKey(key, old)))
                    continue;
                if (old.Kills)
                    killedBefore.Add(old);
                file.RemoveRecord(record);
                blockEnd--;
            }

            foreach (var key in keys)
            {
                var existing = was.Where(rule => OmsiChrono.SameRuleKey(rule, key)).ToList();
                var wanted = will.Where(rule => OmsiChrono.SameRuleKey(rule, key)).ToList();
                var earlierKill = killedBefore.Find(rule => OmsiChrono.SameRuleKey(rule, key));
                if (existing.Count > 0 || earlierKill != null)
                    file.InsertRecord(blockEnd++, RuleRecord(file, "kill_rule", existing.Count > 0 ? existing[0] : earlierKill));
                foreach (var rule in wanted)
                    file.InsertRecord(blockEnd++, RuleRecord(file, "rule", rule));
            }
            return true;
        }

        /// <summary>A [rule] or [kill_rule] as the stock maps write it: the counted rules with three decimals (<c>1 trafficdensity 1.000 0</c>).</summary>
        private static OmsiRecord RuleRecord(OmsiRecordFile file, string section, OmsiPathRule rule)
        {
            var counted = string.Equals(rule.Name, "speedlimit", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(rule.Name, "trafficdensity", StringComparison.OrdinalIgnoreCase);
            return file.CreateRecord(section, new[]
            {
                rule.PathIndex.ToString(CultureInfo.InvariantCulture), rule.Name,
                rule.Value.ToString(counted ? "0.000" : "0.###", CultureInfo.InvariantCulture), rule.Extra.ToString("0.###", CultureInfo.InvariantCulture)
            });
        }
    }
}
