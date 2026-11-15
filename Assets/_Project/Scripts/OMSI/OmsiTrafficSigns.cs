using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The right-of-way signs of the traffic rules tool (ADR 098, M4): what a sign on an arm of a junction says. The numbers are cast back by the map's sign drawing.</summary>
    public enum OmsiRightOfWaySign
    {
        /// <summary>No rule: the arm's paths keep the default priority.</summary>
        Normal,

        /// <summary>StVO 306, Vorfahrtstraße: the arm goes first.</summary>
        PriorityRoad,

        /// <summary>StVO 205, Vorfahrt gewähren: the arm gives way.</summary>
        Yield
    }

    /// <summary>What a speed or overtaking sign does to the lane paths of a road piece.</summary>
    public enum OmsiRoadRuleKind
    {
        /// <summary>[rule] speedlimit, in km/h.</summary>
        SpeedLimit,

        /// <summary>[rule] overtaking_prohib, StVO 276.</summary>
        OvertakingBan,

        /// <summary>Takes the speed limit away (the overtaking ban stays).</summary>
        ClearSpeedLimit,

        /// <summary>Takes the overtaking ban away (the speed limit stays).</summary>
        ClearOvertakingBan
    }

    /// <summary>The road pieces a speed edit is written for, and those it leaves out because they have no road lane.</summary>
    public sealed class OmsiRoadRuleEdits
    {
        public List<OmsiSplinePathRules> Edits { get; } = new List<OmsiSplinePathRules>();

        /// <summary>Pieces without a lane for vehicles, such as a footpath or a rail: their rules are not touched.</summary>
        public int Skipped { get; set; }
    }

    /// <summary>
    /// The traffic rules tool's signs as the files hold them (ADR 098, M4). The sign to value mapping of the right of way stands here
    /// and nowhere else. There is no stop sign and no "priority at the next junction": openOMSI reads the first as give way and the
    /// second as a priority road, so they would only repeat the two signs there are.
    /// </summary>
    public static class OmsiTrafficSigns
    {
        /// <summary>Speeds the tiles offer, in km/h.</summary>
        public static readonly int[] SpeedTiles = { 10, 30, 50, 60, 70, 80, 100 };

        /// <summary>The highest limit the tool writes, in km/h.</summary>
        public const double MaximumKmh = 250.0;

        /// <summary>The level an arm shows with the sign: main road for the priority road, give way for give way.</summary>
        public static OmsiPriorityLevel LevelOf(OmsiRightOfWaySign sign)
        {
            switch (sign)
            {
                case OmsiRightOfWaySign.PriorityRoad:
                    return OmsiPriorityLevel.Main;
                case OmsiRightOfWaySign.Yield:
                    return OmsiPriorityLevel.Yield;
                default:
                    return OmsiPriorityLevel.Normal;
            }
        }

        /// <summary>The sign an arm shows after its rules: priority road, give way or none.</summary>
        public static OmsiRightOfWaySign SignOf(OmsiPriorityArm arm)
        {
            if (arm == null)
                throw new ArgumentNullException(nameof(arm));
            return arm.Level == OmsiPriorityLevel.Main ? OmsiRightOfWaySign.PriorityRoad : arm.Level == OmsiPriorityLevel.Yield ? OmsiRightOfWaySign.Yield : OmsiRightOfWaySign.Normal;
        }

        /// <summary>
        /// The priority every road path of the arm gets for a sign, by path: a value, or null for none (which takes a rule away).
        /// A main road's value goes only to the paths its right of way covers, as <see cref="OmsiJunctionPriorities.Priorities"/> does.
        /// </summary>
        public static Dictionary<int, double?> Priorities(OmsiPriorityArm arm, OmsiRightOfWaySign sign)
        {
            if (arm == null)
                throw new ArgumentNullException(nameof(arm));
            var result = new Dictionary<int, double?>();
            foreach (var index in arm.Paths)
            {
                switch (sign)
                {
                    case OmsiRightOfWaySign.Yield:
                        result[index] = OmsiJunctionPriorities.YieldPriority;
                        break;
                    case OmsiRightOfWaySign.PriorityRoad:
                        result[index] = arm.MainPaths.Contains(index) ? OmsiJunctionPriorities.MainPriority : (double?)null;
                        break;
                    default:
                        result[index] = null;
                        break;
                }
            }
            return result;
        }

        /// <summary>Reads a speed typed in km/h, with a point or a comma; false for none, zero, a negative or one above <see cref="MaximumKmh"/>.</summary>
        public static bool TryParseKmh(string text, out double kmh)
        {
            kmh = 0.0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0 || value > MaximumKmh)
                return false;
            kmh = Math.Round(value, 1);
            return true;
        }

        /// <summary>The names of the rules a kind replaces on the lane paths of a piece; the traffic rules of other names stay.</summary>
        public static IList<string> RuleNames(OmsiRoadRuleKind kind)
        {
            switch (kind)
            {
                case OmsiRoadRuleKind.OvertakingBan:
                case OmsiRoadRuleKind.ClearOvertakingBan:
                    return new[] { "overtaking_prohib" };
                default:
                    return new[] { "speedlimit" };
            }
        }

        /// <summary>The paths of a road type that vehicles drive: its lanes of kind 0.</summary>
        public static List<int> RoadLanes(OmsiSplineProfile profile)
        {
            var lanes = new List<int>();
            if (profile == null)
                return lanes;
            for (var index = 0; index < profile.Lanes.Count; index++)
                if (profile.Lanes[index].Kind == 0)
                    lanes.Add(index);
            return lanes;
        }

        /// <summary>
        /// What to write for a sign on a set of road pieces: on every road lane of each piece the rules of the kind's name go and, for a
        /// speed or a ban, one rule comes in their place; an eraser only takes its own rule away. Other rules of the pieces (traffic density, vehicles, priority) are not in the edit's names.
        /// </summary>
        /// <param name="pieces">The pieces with their tiles.</param>
        /// <param name="profiles">The road type of an asset path, or null when it has none.</param>
        /// <param name="kmh">The limit for <see cref="OmsiRoadRuleKind.SpeedLimit"/>.</param>
        public static OmsiRoadRuleEdits Edits(IEnumerable<KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> pieces, Func<string, OmsiSplineProfile> profiles,
            OmsiRoadRuleKind kind, double kmh)
        {
            if (pieces == null)
                throw new ArgumentNullException(nameof(pieces));
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));
            if (kind == OmsiRoadRuleKind.SpeedLimit && !(kmh > 0.0 && kmh <= MaximumKmh))
                throw new ArgumentOutOfRangeException(nameof(kmh), "A speed limit is above 0 and at most " + MaximumKmh.ToString("0", CultureInfo.InvariantCulture) + " km/h.");
            var result = new OmsiRoadRuleEdits();
            foreach (var piece in pieces)
            {
                var lanes = RoadLanes(profiles(piece.Value.AssetPath));
                if (lanes.Count == 0)
                {
                    result.Skipped++;
                    continue;
                }
                var edit = new OmsiSplinePathRules { Tile = piece.Key, Spline = piece.Value, Paths = lanes, Names = RuleNames(kind),
                    KeepKillRules = kind == OmsiRoadRuleKind.ClearSpeedLimit || kind == OmsiRoadRuleKind.ClearOvertakingBan };
                foreach (var lane in lanes)
                {
                    if (kind == OmsiRoadRuleKind.SpeedLimit)
                        edit.Rules.Add(new OmsiPathRule(lane, "speedlimit", kmh));
                    else if (kind == OmsiRoadRuleKind.OvertakingBan)
                        edit.Rules.Add(new OmsiPathRule(lane, "overtaking_prohib", 0.0));
                }
                result.Edits.Add(edit);
            }
            return result;
        }

        /// <summary>The speed limit a piece shows: that of its lowest lane path that has one (a [kill_rule] takes it away), or null.</summary>
        public static double? SpeedOf(IEnumerable<OmsiPathRule> rules)
        {
            if (rules == null)
                return null;
            var list = rules as IReadOnlyList<OmsiPathRule> ?? new List<OmsiPathRule>(rules);
            Look(list, out var kmh, out _);
            return kmh;
        }

        /// <summary>True when any path of the piece has an overtaking ban that is not taken away again.</summary>
        public static bool HasOvertakingBan(IEnumerable<OmsiPathRule> rules)
        {
            if (rules == null)
                return false;
            var list = rules as IReadOnlyList<OmsiPathRule> ?? new List<OmsiPathRule>(rules);
            Look(list, out _, out var ban);
            return ban;
        }

        /// <summary>
        /// <see cref="SpeedOf"/> and <see cref="HasOvertakingBan"/> in one pass and without allocating, for the redraw of the map. On each path the
        /// last rule of a name counts; the speed is that of the lowest path that still has one.
        /// </summary>
        public static void Look(IReadOnlyList<OmsiPathRule> rules, out double? kmh, out bool ban)
        {
            kmh = null;
            ban = false;
            if (rules == null)
                return;
            var lowest = int.MaxValue;
            for (var index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                var speed = string.Equals(rule.Name, "speedlimit", StringComparison.OrdinalIgnoreCase);
                if (!speed && (ban || !string.Equals(rule.Name, "overtaking_prohib", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (rule.Kills || speed && rule.PathIndex >= lowest || IsOverruled(rules, index, speed))
                    continue;
                if (speed)
                {
                    lowest = rule.PathIndex;
                    kmh = rule.Value;
                }
                else
                    ban = true;
            }
        }

        // A later rule of the same name on the same path replaces this one (the pieces carry a handful of rules, so a scan beats a dictionary).
        private static bool IsOverruled(IReadOnlyList<OmsiPathRule> rules, int index, bool speed)
        {
            var path = rules[index].PathIndex;
            var name = speed ? "speedlimit" : "overtaking_prohib";
            for (var later = index + 1; later < rules.Count; later++)
                if (rules[later].PathIndex == path && string.Equals(rules[later].Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
