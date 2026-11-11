using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>How two signal groups get on (the conflict matrix of the brief, §10).</summary>
    public enum OmsiGroupRelation
    {
        /// <summary>No movement of one meets one of the other.</summary>
        Compatible,

        /// <summary>They meet, but one gives way: turning against oncoming traffic or across a footpath.</summary>
        Conditional,

        /// <summary>They meet and neither gives way.</summary>
        Incompatible,

        /// <summary>One of them has no movements to look at.</summary>
        NotChecked
    }

    /// <summary>The groups × groups matrix of a crossing, found from the conflicts of their movements.</summary>
    public sealed class OmsiSignalMatrix
    {
        private readonly OmsiGroupRelation[,] relations;

        private OmsiSignalMatrix(int count)
        {
            Count = count;
            relations = new OmsiGroupRelation[count, count];
            Conflicts = new List<OmsiSignalConflict>[count, count];
        }

        public int Count { get; }

        /// <summary>The movement conflicts between two groups, the same list both ways round.</summary>
        public List<OmsiSignalConflict>[,] Conflicts { get; }

        public OmsiGroupRelation this[int first, int second] => relations[first, second];

        /// <param name="lightOf">The light each movement obeys, -1 for none.</param>
        public static OmsiSignalMatrix Build(int lights, IList<OmsiSignalConflict> conflicts, IList<int> lightOf)
        {
            if (conflicts == null)
                throw new ArgumentNullException(nameof(conflicts));
            if (lightOf == null)
                throw new ArgumentNullException(nameof(lightOf));
            var matrix = new OmsiSignalMatrix(lights);
            var hasMovements = new bool[lights];
            foreach (var light in lightOf)
                if (light >= 0 && light < lights)
                    hasMovements[light] = true;
            for (var first = 0; first < lights; first++)
            for (var second = 0; second < lights; second++)
            {
                matrix.Conflicts[first, second] = first <= second ? new List<OmsiSignalConflict>() : matrix.Conflicts[second, first];
                matrix.relations[first, second] = hasMovements[first] && hasMovements[second] ? OmsiGroupRelation.Compatible : OmsiGroupRelation.NotChecked;
            }
            foreach (var conflict in conflicts)
            {
                var a = conflict.First < lightOf.Count ? lightOf[conflict.First] : -1;
                var b = conflict.Second < lightOf.Count ? lightOf[conflict.Second] : -1;
                if (a < 0 || b < 0 || a >= lights || b >= lights || a == b)
                    continue;
                matrix.Conflicts[Math.Min(a, b), Math.Max(a, b)].Add(conflict);
                var now = matrix.relations[a, b];
                var found = conflict.Permitted ? OmsiGroupRelation.Conditional : OmsiGroupRelation.Incompatible;
                if (now == OmsiGroupRelation.Compatible || (now == OmsiGroupRelation.Conditional && found == OmsiGroupRelation.Incompatible))
                {
                    matrix.relations[a, b] = found;
                    matrix.relations[b, a] = found;
                }
            }
            return matrix;
        }
    }

    public enum OmsiSignalFindingKind
    {
        /// <summary>Incompatible groups may go at the same time.</summary>
        ConflictingGreen,

        /// <summary>Conditionally compatible groups go together: one has to give way.</summary>
        ConditionalGreen,

        /// <summary>Movements of one group meet each other; no program can keep them apart.</summary>
        WithinGroup,

        /// <summary>Too little time from the end of one group's green to the green of an incompatible one.</summary>
        ShortIntergreen,

        /// <summary>A head shows a group the program does not have.</summary>
        HeadWithoutGroup,

        /// <summary>A group with movements has no head; openOMSI shows its drivers nothing.</summary>
        GroupWithoutHead,

        /// <summary>No head names the crossing: openOMSI does not run the program at all.</summary>
        NoHeads,

        /// <summary>Vehicle movements that obey no group.</summary>
        UnboundMovements,

        /// <summary>A group shows dark at times: the crossing is unsignalled then, the priority signs count.</summary>
        DarkPhase,

        /// <summary>A finding of the timeline's timing check (<see cref="OmsiSignalTimeline.CheckTiming"/>).</summary>
        Timing,

        /// <summary>A pedestrian light shows green for less time than people need to cross: openOMSI's walkers do not start then.</summary>
        PedestrianWindow,

        /// <summary>Incompatible groups go together, allowed by a manual exception.</summary>
        AllowedByException
    }

    /// <summary>One finding of <see cref="OmsiSignalCheck"/>.</summary>
    public sealed class OmsiSignalFinding
    {
        public OmsiSignalIssueLevel Level { get; internal set; }
        public OmsiSignalFindingKind Kind { get; internal set; }

        /// <summary>The groups it concerns (-1 for none).</summary>
        public int First { get; internal set; } = -1;

        public int Second { get; internal set; } = -1;

        public double Seconds { get; internal set; }

        /// <summary>A count, where the kind has one (movements, heads, seconds of overlap).</summary>
        public double Amount { get; internal set; }

        /// <summary>The conflict it shows, for the map; null where none.</summary>
        public OmsiSignalConflict Conflict { get; internal set; }

        /// <summary>For <see cref="OmsiSignalFindingKind.Timing"/>: the finding of the timing check.</summary>
        public OmsiSignalIssue Timing { get; internal set; }
    }

    /// <summary>
    /// The check of a crossing's signals (ADR 085, milestone 5; §10 of the brief): conflicts let through by the program,
    /// intergreen times, heads and bindings, and the timing check of the timeline, each an error, warning or hint.
    /// Crossing paths of incompatible groups at the same time are errors; joining lanes and a footpath against straight
    /// traffic, as OMSI's own programs have them, warnings; turning traffic that gives way, a hint. A manual exception
    /// makes an error a warning and is named as such: it is no proof of a safe program.
    /// </summary>
    public static class OmsiSignalCheck
    {
        /// <summary>
        /// The shortest time from the end of one group's green (go) to the start of an incompatible group's green that
        /// passes without a warning: yellow 3 s and a little clearing. The assistant's defaults give 6 s.
        /// </summary>
        public const double ShortestIntergreen = 4.0;

        /// <param name="lightOf">The light each movement obeys, -1 for none.</param>
        /// <param name="headLights">The light each head of the crossing shows (its first label; null where it has none).</param>
        public static List<OmsiSignalFinding> Run(OmsiSignalProgram program, OmsiSignalIntersection crossing, IList<OmsiSignalConflict> conflicts,
            IList<int> lightOf, ICollection<(int, int)> exceptions, IList<int?> headLights, ICollection<int> pedestrianLights = null)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            if (crossing == null)
                throw new ArgumentNullException(nameof(crossing));
            exceptions = exceptions ?? new HashSet<(int, int)>();
            var findings = new List<OmsiSignalFinding>();
            var lights = program.Lights.Count;
            bool Dark(int light, double seconds) => OmsiSignalAspects.Of(program.StateAt(light, seconds)) == OmsiSignalAspect.Dark;

            // Conflicts the program lets through, one finding per pair of groups and kind, the most serious first.
            var clashes = OmsiSignalConflicts.Clashes(program, conflicts, movement => movement < lightOf.Count ? lightOf[movement] : -1);
            foreach (var group in clashes.GroupBy(clash => (Pair(lightOf[clash.Conflict.First], lightOf[clash.Conflict.Second]), Kind(clash))))
            {
                var clash = group.OrderByDescending(item => item.OverlapSeconds).First();
                var a = lightOf[clash.Conflict.First];
                var b = lightOf[clash.Conflict.Second];
                // A dark light leaves the crossing to its signs: not a conflict of the program.
                if (Dark(a, clash.Seconds + 0.01) || Dark(b, clash.Seconds + 0.01))
                    continue;
                var finding = new OmsiSignalFinding
                {
                    First = Math.Min(a, b), Second = Math.Max(a, b), Seconds = clash.Seconds, Amount = clash.OverlapSeconds, Conflict = clash.Conflict
                };
                // Within one group, turning traffic giving way to oncoming traffic is how the group is meant to run.
                if (clash.SameLight && clash.Conflict.Permitted)
                    continue;
                if (clash.SameLight)
                {
                    finding.Kind = OmsiSignalFindingKind.WithinGroup;
                    finding.Level = OmsiSignalIssueLevel.Hint;
                }
                else if (clash.Conflict.Permitted)
                {
                    finding.Kind = OmsiSignalFindingKind.ConditionalGreen;
                    finding.Level = OmsiSignalIssueLevel.Hint;
                }
                else if (exceptions.Contains(Pair(a, b)))
                {
                    finding.Kind = OmsiSignalFindingKind.AllowedByException;
                    finding.Level = OmsiSignalIssueLevel.Warning;
                }
                else
                {
                    finding.Kind = OmsiSignalFindingKind.ConflictingGreen;
                    finding.Level = clash.Conflict.Kind == OmsiConflictKind.Crossing ? OmsiSignalIssueLevel.Error : OmsiSignalIssueLevel.Warning;
                }
                findings.Add(finding);
            }

            // Intergreen: from the end of a group's go to the start of an incompatible group's go.
            var matrix = OmsiSignalMatrix.Build(lights, conflicts, lightOf);
            var cycle = program.CycleLength;
            for (var first = 0; first < lights; first++)
            for (var second = 0; second < lights; second++)
            {
                if (first == second || matrix[first, second] != OmsiGroupRelation.Incompatible || exceptions.Contains(Pair(first, second)))
                    continue;
                foreach (var end in GoEnds(program, first))
                {
                    var next = NextGoStart(program, second, end, cycle);
                    if (next.HasValue && next.Value < ShortestIntergreen - 1e-6 && next.Value > 1e-6)
                    {
                        findings.Add(new OmsiSignalFinding
                        {
                            Level = OmsiSignalIssueLevel.Warning, Kind = OmsiSignalFindingKind.ShortIntergreen, First = first, Second = second,
                            Seconds = end, Amount = next.Value
                        });
                        break;
                    }
                }
            }

            // Heads.
            if (headLights != null)
            {
                if (headLights.Count == 0 && lights > 0)
                    findings.Add(new OmsiSignalFinding { Level = OmsiSignalIssueLevel.Warning, Kind = OmsiSignalFindingKind.NoHeads });
                foreach (var group in headLights.Where(light => (light ?? 0) >= lights).GroupBy(light => light ?? 0))
                    findings.Add(new OmsiSignalFinding { Level = OmsiSignalIssueLevel.Error, Kind = OmsiSignalFindingKind.HeadWithoutGroup, First = group.Key, Amount = group.Count() });
                if (headLights.Count > 0)
                    for (var light = 0; light < lights; light++)
                        if (lightOf.Contains(light) && !headLights.Any(head => (head ?? 0) == light))
                            findings.Add(new OmsiSignalFinding { Level = OmsiSignalIssueLevel.Warning, Kind = OmsiSignalFindingKind.GroupWithoutHead, First = light });
            }

            // Movements without a group, dark phases, timing.
            var unbound = crossing.Movements.Count(movement => movement.Turn != OmsiMovementTurn.Pedestrian && (movement.Index >= lightOf.Count || lightOf[movement.Index] < 0));
            if (unbound > 0 && lights > 0)
                findings.Add(new OmsiSignalFinding { Level = OmsiSignalIssueLevel.Hint, Kind = OmsiSignalFindingKind.UnboundMovements, Amount = unbound });
            for (var light = 0; light < lights; light++)
            {
                var dark = OmsiSignalTimeline.Segments(program, light).FirstOrDefault(segment => OmsiSignalAspects.Of(segment.State) == OmsiSignalAspect.Dark);
                if (dark != null && program.Lights[light].Phases.Count > 0)
                    findings.Add(new OmsiSignalFinding { Level = OmsiSignalIssueLevel.Hint, Kind = OmsiSignalFindingKind.DarkPhase, First = light, Seconds = dark.Start });
            }
            // A walker starts across only when the green lasts until the far side (openOMSI: the length over the pace, and a second).
            if (pedestrianLights != null)
                foreach (var light in pedestrianLights.Where(each => each >= 0 && each < lights).OrderBy(each => each))
                {
                    var needed = crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && movement.Index < lightOf.Count && lightOf[movement.Index] == light)
                        .Select(movement => Math.Sqrt((movement.ExitX - movement.EntryX) * (movement.ExitX - movement.EntryX) + (movement.ExitY - movement.EntryY) * (movement.ExitY - movement.EntryY)) /
                                            PedestrianPace + 1.0).DefaultIfEmpty(0.0).Max();
                    if (needed <= 0.0 || program.Lights[light].Phases.Count == 0)
                        continue;
                    // The longest run of green (the shades of green count as one).
                    var longest = 0.0;
                    var run = 0.0;
                    foreach (var segment in OmsiSignalTimeline.Segments(program, light))
                    {
                        run = OmsiSignalAspects.Of(segment.State) == OmsiSignalAspect.Green ? run + segment.Seconds : 0.0;
                        longest = Math.Max(longest, run);
                    }
                    if (longest + 1e-6 < needed)
                        findings.Add(new OmsiSignalFinding { Level = OmsiSignalIssueLevel.Warning, Kind = OmsiSignalFindingKind.PedestrianWindow, First = light, Seconds = longest, Amount = Math.Ceiling(needed) });
                }
            foreach (var issue in OmsiSignalTimeline.CheckTiming(program, pedestrianLights))
                findings.Add(new OmsiSignalFinding { Level = issue.Level, Kind = OmsiSignalFindingKind.Timing, First = issue.Light, Seconds = issue.Seconds, Timing = issue });
            return findings.OrderBy(finding => finding.Level).ThenBy(finding => finding.First).ThenBy(finding => finding.Second).ToList();
        }

        /// <summary>How fast people are taken to cross, in metres a second (the assistant uses the same).</summary>
        public const double PedestrianPace = 1.2;

        private static (int, int) Pair(int first, int second) => first < second ? (first, second) : (second, first);

        /// <summary>Crossing paths are the serious kind; joining lanes and footpaths are listed apart.</summary>
        private static int Kind(OmsiSignalClash clash) =>
            clash.SameLight ? 3 : clash.Conflict.Permitted ? 2 : clash.Conflict.Kind == OmsiConflictKind.Crossing ? 0 : 1;

        /// <summary>The seconds at which a light stops letting traffic go (go before, no go after).</summary>
        private static IEnumerable<double> GoEnds(OmsiSignalProgram program, int light)
        {
            var segments = OmsiSignalTimeline.Segments(program, light);
            for (var index = 0; index < segments.Count; index++)
            {
                var next = segments[(index + 1) % segments.Count];
                if (OmsiSignalAspects.AllowsGo(segments[index].State) && !OmsiSignalAspects.AllowsGo(next.State) &&
                    OmsiSignalAspects.Of(segments[index].State) != OmsiSignalAspect.Dark)
                    yield return segments[index].End >= program.CycleLength - 1e-9 ? 0.0 : segments[index].End;
            }
        }

        /// <summary>Seconds from <paramref name="from"/> until a light next lets traffic go, within one cycle; null if it never does.</summary>
        private static double? NextGoStart(OmsiSignalProgram program, int light, double from, double cycle)
        {
            const double Step = 0.1;
            if (OmsiSignalAspects.AllowsGo(program.StateAt(light, from + 1e-6)))
                return 0.0;
            for (var t = Step; t <= cycle; t += Step)
                if (OmsiSignalAspects.AllowsGo(program.StateAt(light, from + t)))
                    return t;
            return null;
        }
    }
}
