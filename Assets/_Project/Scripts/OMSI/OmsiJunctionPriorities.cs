using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The right of way of an arm of a junction (ADR 047), as the stock maps set it with [rule] priority.</summary>
    public enum OmsiPriorityLevel
    {
        /// <summary>No rule: the paths keep the default 128.</summary>
        Normal,

        /// <summary>Vorfahrtstraße: the arm's paths straight on and turning right get 192; turning left, across the oncoming traffic, they keep 128.</summary>
        Main,

        /// <summary>Vorfahrt achten: every path of the arm gets 64.</summary>
        Yield
    }

    /// <summary>
    /// Where traffic comes into a junction object: the road paths that start there and those they lead on to inside the
    /// object, in the object frame (x right, y forward).
    /// </summary>
    public sealed class OmsiPriorityArm
    {
        /// <summary>The object's [path]s of this arm, entries first.</summary>
        public List<int> Paths { get; } = new List<int>();

        /// <summary>
        /// Those of <see cref="Paths"/> a main road's right of way covers, as the stock maps give it: straight on (turning less
        /// than 30°) and turning right, which crosses no oncoming traffic.
        /// </summary>
        public List<int> MainPaths { get; } = new List<int>();

        /// <summary>The middle of the arm's entries.</summary>
        public double X { get; internal set; }

        public double Y { get; internal set; }

        /// <summary>The heading traffic comes in with, in degrees as OMSI turns objects.</summary>
        public double InwardDegrees { get; internal set; }

        /// <summary>The right of way the object's rules give the arm now.</summary>
        public OmsiPriorityLevel Level { get; set; }
    }

    /// <summary>
    /// The right of way of junction objects (ADR 047), modelled on the priorities tool of Traffic for Cities: Skylines II:
    /// a junction's road paths fall into arms by where traffic comes in, and each arm gets a level that the object's
    /// [rule] priority records hold, as the stock maps do it (192 on the main road's paths straight on and to the right, 64
    /// on the side road's paths, none elsewhere). Right turns are told by their indicator, else by their bend, as for
    /// traffic on the right. openOMSI lets the higher priority go first where two paths meet; [blockpath] has no effect.
    /// </summary>
    public static class OmsiJunctionPriorities
    {
        public const double MainPriority = 192.0;
        public const double YieldPriority = 64.0;
        public const double DefaultPriority = 128.0;

        /// <summary>Entries this far apart and heading this much apart still belong to one arm.</summary>
        private const double ArmMeters = 15.0;

        private const double ArmDegrees = 30.0;

        /// <summary>A path turning less than this from start to end runs straight on.</summary>
        private const double StraightDegrees = 30.0;

        private struct Ends
        {
            public double EntryX, EntryY, EntryHeading, ExitX, ExitY, ExitHeading;
        }

        /// <summary>
        /// The arms of a junction object's road paths, with the level its rules give each; none for an object with fewer
        /// than two arms, which is no junction.
        /// </summary>
        /// <param name="rules">The object's rules in the tile, or null for none.</param>
        public static List<OmsiPriorityArm> Arms(OmsiSceneryObject sceneryObject, IList<OmsiPathRule> rules)
        {
            if (sceneryObject == null)
                throw new ArgumentNullException(nameof(sceneryObject));
            var ends = new Dictionary<int, Ends>();
            for (var index = 0; index < sceneryObject.Paths.Count; index++)
            {
                var path = sceneryObject.Paths[index];
                if (path.Kind == 0 && path.LengthMeters > 0.0 && !double.IsInfinity(path.LengthMeters))
                    ends[index] = EndsOf(path);
            }

            // Entries are paths no other path leads into.
            var entries = new List<int>();
            foreach (var pair in ends)
            {
                var fed = false;
                foreach (var other in ends)
                    if (other.Key != pair.Key && Joins(other.Value.ExitX, other.Value.ExitY, other.Value.ExitHeading, pair.Value.EntryX, pair.Value.EntryY, pair.Value.EntryHeading))
                        fed = true;
                if (!fed)
                    entries.Add(pair.Key);
            }
            entries.Sort();

            var arms = new List<OmsiPriorityArm>();
            var armOf = new Dictionary<int, OmsiPriorityArm>();
            foreach (var entry in entries)
            {
                var start = ends[entry];
                OmsiPriorityArm arm = null;
                foreach (var candidate in arms)
                {
                    var first = ends[candidate.Paths[0]];
                    if (Math.Abs(OmsiRoadPlanner.Normalize(start.EntryHeading - first.EntryHeading)) < ArmDegrees &&
                        Distance(start.EntryX, start.EntryY, first.EntryX, first.EntryY) < ArmMeters)
                        arm = candidate;
                }
                if (arm == null)
                {
                    arm = new OmsiPriorityArm { InwardDegrees = start.EntryHeading };
                    arms.Add(arm);
                }
                arm.Paths.Add(entry);
                armOf[entry] = arm;
            }
            foreach (var arm in arms)
            {
                arm.X = 0.0;
                arm.Y = 0.0;
                foreach (var entry in arm.Paths)
                {
                    arm.X += ends[entry].EntryX / arm.Paths.Count;
                    arm.Y += ends[entry].EntryY / arm.Paths.Count;
                }
            }

            // The paths only one arm's traffic goes on to inside the object belong to it too. A path that traffic of several
            // arms leads into, such as the piece out of the junction after a straight path and a turn, belongs to none.
            var feeders = new Dictionary<int, List<int>>();
            foreach (var pair in ends)
                if (!armOf.ContainsKey(pair.Key))
                {
                    feeders[pair.Key] = new List<int>();
                    foreach (var other in ends)
                        if (other.Key != pair.Key &&
                            Joins(other.Value.ExitX, other.Value.ExitY, other.Value.ExitHeading, pair.Value.EntryX, pair.Value.EntryY, pair.Value.EntryHeading))
                            feeders[pair.Key].Add(other.Key);
                }
            var shared = new HashSet<int>();
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var pair in feeders)
                {
                    if (armOf.ContainsKey(pair.Key) || shared.Contains(pair.Key))
                        continue;
                    OmsiPriorityArm only = null;
                    var known = true;
                    foreach (var feeder in pair.Value)
                    {
                        if (shared.Contains(feeder))
                            only = null;
                        else if (!armOf.TryGetValue(feeder, out var arm))
                        {
                            known = false;
                            break;
                        }
                        else if (only == null || only == arm)
                            only = only ?? arm;
                        else
                            only = null;
                        if (only == null)
                            break;
                    }
                    if (!known)
                        continue;
                    if (only == null)
                        shared.Add(pair.Key);
                    else
                    {
                        armOf[pair.Key] = only;
                        only.Paths.Add(pair.Key);
                    }
                    changed = true;
                }
            }

            foreach (var arm in arms)
            {
                foreach (var index in arm.Paths)
                {
                    var turn = OmsiRoadPlanner.Normalize(ends[index].ExitHeading - ends[index].EntryHeading);
                    var blinker = sceneryObject.Paths[index].Blinker;
                    if (Math.Abs(turn) < StraightDegrees || blinker == 3 || blinker != 2 && turn > 0.0)
                        arm.MainPaths.Add(index);
                }
                arm.Level = LevelOf(arm, rules);
            }
            return arms.Count >= 2 ? arms : new List<OmsiPriorityArm>();
        }

        /// <summary>
        /// The priority every road path of the arms should have for their levels, by path: a value, or null for none. Paths
        /// of no arm are left out, so their rules stay as they are.
        /// </summary>
        public static Dictionary<int, double?> Priorities(IEnumerable<OmsiPriorityArm> arms)
        {
            var result = new Dictionary<int, double?>();
            foreach (var arm in arms)
                foreach (var index in arm.Paths)
                    result[index] = arm.Level == OmsiPriorityLevel.Yield ? YieldPriority
                        : arm.Level == OmsiPriorityLevel.Main && arm.MainPaths.Contains(index) ? MainPriority
                        : (double?)null;
            return result;
        }

        /// <summary>
        /// The right of way of a junction the editor made, after its arms: the main road's arms get it, the others give way,
        /// as "Auto markings" draws them; without a main road there are no rules. The arms of the paths are matched to the
        /// made arms by the way traffic comes in, against each made arm's heading out of the junction.
        /// </summary>
        public static Dictionary<int, double?> ForMadeJunction(OmsiSceneryObject sceneryObject, IList<OmsiJunctionArm> madeArms)
        {
            if (madeArms == null)
                throw new ArgumentNullException(nameof(madeArms));
            var arms = Arms(sceneryObject, null);
            var anyMain = false;
            foreach (var made in madeArms)
                anyMain |= made.Main;
            foreach (var arm in arms)
            {
                OmsiJunctionArm nearest = null;
                var best = 45.0;
                foreach (var made in madeArms)
                {
                    var off = Math.Abs(OmsiRoadPlanner.Normalize(arm.InwardDegrees - (made.HeadingDegrees + 180.0)));
                    if (off < best)
                    {
                        best = off;
                        nearest = made;
                    }
                }
                arm.Level = !anyMain || nearest == null ? OmsiPriorityLevel.Normal : nearest.Main ? OmsiPriorityLevel.Main : OmsiPriorityLevel.Yield;
            }
            return Priorities(arms);
        }

        /// <summary>The next level a click gives an arm: normal, main road, give way, then normal again.</summary>
        public static OmsiPriorityLevel Next(OmsiPriorityLevel level)
        {
            return level == OmsiPriorityLevel.Normal ? OmsiPriorityLevel.Main : level == OmsiPriorityLevel.Main ? OmsiPriorityLevel.Yield : OmsiPriorityLevel.Normal;
        }

        /// <summary>A give-way rule on any path makes the arm give way; else a right-of-way rule on one of its main paths makes it a main road.</summary>
        private static OmsiPriorityLevel LevelOf(OmsiPriorityArm arm, IList<OmsiPathRule> rules)
        {
            if (rules == null)
                return OmsiPriorityLevel.Normal;
            var main = false;
            foreach (var index in arm.Paths)
            {
                double? value = null;
                foreach (var rule in rules)
                    if (rule.PathIndex == index && string.Equals(rule.Name, "priority", StringComparison.OrdinalIgnoreCase))
                        value = rule.Kills ? (double?)null : rule.Value;
                if (value < DefaultPriority - 0.5)
                    return OmsiPriorityLevel.Yield;
                if (value > DefaultPriority + 0.5 && arm.MainPaths.Contains(index))
                    main = true;
            }
            return main ? OmsiPriorityLevel.Main : OmsiPriorityLevel.Normal;
        }

        /// <summary>Where traffic comes onto a path and leaves it, with its headings; a path used against its points the other way round.</summary>
        private static Ends EndsOf(OmsiObjectPath path)
        {
            var lane = new OmsiSplineRecord
            {
                LocalX = path.X, LocalY = path.Y, HeadingDegrees = path.HeadingDegrees, LengthMeters = path.LengthMeters, RadiusMeters = path.RadiusMeters
            };
            OmsiMapGeometry.GetLocalFrameAt(lane, 0.0, out var start, out var startHeading);
            OmsiMapGeometry.GetLocalFrameAt(lane, path.LengthMeters, out var end, out var endHeading);
            var first = startHeading * 180.0 / Math.PI;
            var last = endHeading * 180.0 / Math.PI;
            return path.Direction == 1
                ? new Ends { EntryX = end.X, EntryY = end.Y, EntryHeading = last + 180.0, ExitX = start.X, ExitY = start.Y, ExitHeading = first + 180.0 }
                : new Ends { EntryX = start.X, EntryY = start.Y, EntryHeading = first, ExitX = end.X, ExitY = end.Y, ExitHeading = last };
        }

        private static bool Joins(double exitX, double exitY, double exitHeading, double entryX, double entryY, double entryHeading)
        {
            return Distance(exitX, exitY, entryX, entryY) <= OmsiMapPaths.LinkMeters && Math.Abs(OmsiRoadPlanner.Normalize(entryHeading - exitHeading)) < OmsiMapPaths.LinkDegrees;
        }

        private static double Distance(double ax, double ay, double bx, double by)
        {
            return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        }
    }
}
