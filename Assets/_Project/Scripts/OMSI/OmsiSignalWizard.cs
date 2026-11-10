using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The choices of the signal assistant; every time can be changed before the proposal is used.</summary>
    public sealed class OmsiSignalWizardOptions
    {
        /// <summary>Give left turns from a lane of their own a group and phase of their own, so they need not give way.</summary>
        public bool SeparateLeftTurns { get; set; } = true;

        public double GreenSeconds { get; set; } = 20;
        public double LeftGreenSeconds { get; set; } = 10;
        public double RedYellowSeconds { get; set; } = 1;
        public double YellowSeconds { get; set; } = 3;
        public double ClearanceSeconds { get; set; } = 2;
    }

    /// <summary>A signal group the assistant proposes: movements that may go together.</summary>
    public sealed class OmsiProposedGroup
    {
        /// <summary>The approaches it serves, by index from 0.</summary>
        public List<int> Approaches { get; } = new List<int>();

        /// <summary>True for a group of left turns from lanes of their own.</summary>
        public bool LeftTurns { get; internal set; }

        public List<int> Movements { get; } = new List<int>();

        /// <summary>The name written as the light's name: "Arm 1+3", "Arm 1+3 left".</summary>
        public string Name { get; set; }
    }

    /// <summary>What the assistant proposes for a crossing, with the program it makes and what it found checking it.</summary>
    public sealed class OmsiSignalProposal
    {
        public List<OmsiProposedGroup> Groups { get; } = new List<OmsiProposedGroup>();
        public OmsiSignalPhasePlan Plan { get; internal set; }
        public OmsiSignalProgram Program { get; internal set; }

        /// <summary>The group (light) of every movement of the crossing; -1 for footpaths and movements the plan leaves out.</summary>
        public int[] MovementLights { get; internal set; }

        /// <summary>Conflicts of movements that go at the same time though neither gives way. A proposal is conflict-free only without them.</summary>
        public List<OmsiSignalClash> HardClashes { get; } = new List<OmsiSignalClash>();

        /// <summary>Conflicts at the same time where one gives way (left turns, turning across footpaths).</summary>
        public List<OmsiSignalClash> PermittedClashes { get; } = new List<OmsiSignalClash>();

        /// <summary>Footpaths that cross a lane: the assistant gives them no light, which it says.</summary>
        public int CrossingFootpaths { get; internal set; }

        /// <summary>Lanes with left turns only that got a group of their own.</summary>
        public int SeparateLeftLanes { get; internal set; }

        /// <summary>Lanes with left turns only, whether separated or not.</summary>
        public int LeftLanes { get; internal set; }

        public bool ConflictFree => HardClashes.Count == 0;
    }

    /// <summary>
    /// The signal assistant (ADR 085, milestone 4; §9 of the brief): from the approaches, lanes and movements of a crossing it
    /// proposes signal groups and a basic program. Approaches facing each other share a group and a phase when no two of
    /// their movements conflict without giving way; left turns from lanes of their own get a group and phase of their own
    /// (when chosen); every other approach has a phase of its own. The program is a <see cref="OmsiSignalPhasePlan"/>, so it
    /// can be changed in the simple mode afterwards, and it is checked against the conflicts found in the paths: only a
    /// proposal without conflicts that do not give way is called conflict-free.
    /// </summary>
    public static class OmsiSignalWizard
    {
        /// <summary>
        /// The movements of a junction made here that start in a left-turn lane (ADR 048): their way's lane in lies after the
        /// lanes of the arm's profile. The turn lane forks off beside the lane next to it, so the paths alone show one lane.
        /// </summary>
        public static HashSet<int> TurnLaneMovements(OmsiSignalIntersection crossing, OmsiJunctionLayout layout)
        {
            if (crossing == null)
                throw new ArgumentNullException(nameof(crossing));
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            var result = new HashSet<int>();
            foreach (var movement in crossing.Movements)
            {
                var way = movement.Paths.Where(path => path < layout.Paths.Count).Select(path => layout.Paths[path].WayThrough).FirstOrDefault(key => key != null);
                if (way == null)
                    continue;
                var parts = way.Split('>')[0].Split('.');
                if (parts.Length == 2 && int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var arm) &&
                    int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lane) &&
                    arm >= 0 && arm < layout.Arms.Count && lane >= layout.Arms[arm].Profile.Lanes.Count)
                    result.Add(movement.Index);
            }
            return result;
        }

        /// <summary>Where vehicles on a path enter it (its start, or its end when it is driven backwards), and half its width.</summary>
        public static (double X, double Y, double HalfWidth) EntryOf(OmsiObjectPath path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            var ends = OmsiJunctionPriorities.EndsOf(path);
            return (ends.EntryX, ends.EntryY, path.WidthMeters / 2.0);
        }

        /// <param name="turnLaneMovements">
        /// Movements known to start in a left-turn lane (<see cref="TurnLaneMovements"/>); without, lanes of the crossing
        /// with nothing but left turns count.
        /// </param>
        public static OmsiSignalProposal Propose(OmsiSignalIntersection crossing, IList<OmsiSignalConflict> conflicts, OmsiSignalWizardOptions options,
            ICollection<int> turnLaneMovements = null)
        {
            if (crossing == null)
                throw new ArgumentNullException(nameof(crossing));
            if (conflicts == null)
                throw new ArgumentNullException(nameof(conflicts));
            options = options ?? new OmsiSignalWizardOptions();
            var proposal = new OmsiSignalProposal();
            var hard = new HashSet<(int, int)>(conflicts.Where(conflict => !conflict.Permitted).Select(conflict => (conflict.First, conflict.Second)));
            bool HardBetween(IEnumerable<int> first, IEnumerable<int> second) =>
                first.Any(a => second.Any(b => hard.Contains((Math.Min(a, b), Math.Max(a, b)))));

            // Lanes with nothing but left turns (and U-turns), on approaches with other lanes too.
            var leftLaneMovements = new Dictionary<int, List<int>>();
            if (turnLaneMovements != null)
                foreach (var group in turnLaneMovements.Where(index => index >= 0 && index < crossing.Movements.Count && crossing.Movements[index].Approach >= 0)
                             .GroupBy(index => crossing.Movements[index].Approach))
                {
                    proposal.LeftLanes++;
                    if (crossing.Movements.Any(movement => movement.Approach == group.Key && movement.Turn != OmsiMovementTurn.Pedestrian && !turnLaneMovements.Contains(movement.Index)))
                        leftLaneMovements[group.Key] = group.ToList();
                }
            else
            foreach (var approach in crossing.Approaches)
            {
                var lefts = approach.Lanes.Where(lane => lane.Movements.Count > 0 && lane.Movements.All(index =>
                    crossing.Movements[index].Turn == OmsiMovementTurn.Left || crossing.Movements[index].Turn == OmsiMovementTurn.UTurn)).ToList();
                proposal.LeftLanes += lefts.Count;
                if (lefts.Count > 0 && lefts.Count < approach.Lanes.Count)
                    leftLaneMovements[approach.Index] = lefts.SelectMany(lane => lane.Movements).ToList();
            }
            List<int> Movements(int approach, bool lefts)
            {
                var all = crossing.Movements.Where(movement => movement.Approach == approach && movement.Turn != OmsiMovementTurn.Pedestrian)
                    .Select(movement => movement.Index).ToList();
                if (!options.SeparateLeftTurns || !leftLaneMovements.TryGetValue(approach, out var left))
                    return lefts ? new List<int>() : all;
                return lefts ? left : all.Except(left).ToList();
            }

            // Approaches facing each other share a phase when that is safe.
            var used = new HashSet<int>();
            var phases = new List<List<OmsiProposedGroup>>();
            var approaches = crossing.Approaches.Select(approach => approach.Index).Where(index => Movements(index, false).Count + Movements(index, true).Count > 0).ToList();
            foreach (var first in approaches)
            {
                if (used.Contains(first))
                    continue;
                used.Add(first);
                var partner = approaches.Where(other => !used.Contains(other) && OmsiSignalConflicts.Opposite(crossing, first, other))
                    .OrderBy(other => Math.Abs(Math.Abs(OmsiRoadPlanner.Normalize(crossing.Approaches[first].InwardDegrees - crossing.Approaches[other].InwardDegrees)) - 180.0))
                    .Cast<int?>().FirstOrDefault();
                if (partner.HasValue && HardBetween(Movements(first, false), Movements(partner.Value, false)))
                    partner = null;
                var members = partner.HasValue ? new[] { first, partner.Value } : new[] { first };
                if (partner.HasValue)
                    used.Add(partner.Value);
                var main = new OmsiProposedGroup();
                main.Approaches.AddRange(members);
                // Alone, an approach's left turns face no oncoming traffic and go with the rest.
                foreach (var member in members)
                    main.Movements.AddRange(partner.HasValue ? Movements(member, false) : Movements(member, false).Concat(Movements(member, true)));
                var phase = new List<OmsiProposedGroup> { main };
                phases.Add(phase);
                if (!partner.HasValue)
                    continue;
                var leftPhases = new List<List<OmsiProposedGroup>>();
                var lefts = members.Where(member => Movements(member, true).Count > 0).ToList();
                if (lefts.Count == 2 && !HardBetween(Movements(lefts[0], true), Movements(lefts[1], true)))
                {
                    var both = new OmsiProposedGroup { LeftTurns = true };
                    both.Approaches.AddRange(lefts);
                    both.Movements.AddRange(lefts.SelectMany(member => Movements(member, true)));
                    leftPhases.Add(new List<OmsiProposedGroup> { both });
                }
                else
                    foreach (var member in lefts)
                    {
                        var own = new OmsiProposedGroup { LeftTurns = true };
                        own.Approaches.Add(member);
                        own.Movements.AddRange(Movements(member, true));
                        leftPhases.Add(new List<OmsiProposedGroup> { own });
                    }
                phases.AddRange(leftPhases);
            }
            proposal.SeparateLeftLanes = phases.SelectMany(phase => phase).Where(group => group.LeftTurns).Sum(group => group.Approaches.Count);

            // Groups, names and the movements' lights.
            foreach (var group in phases.SelectMany(phase => phase))
            {
                group.Name = "Arm " + string.Join("+", group.Approaches.Select(approach => (approach + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))) +
                             (group.LeftTurns ? " left" : string.Empty);
                proposal.Groups.Add(group);
            }
            proposal.MovementLights = Enumerable.Repeat(-1, crossing.Movements.Count).ToArray();
            for (var light = 0; light < proposal.Groups.Count; light++)
                foreach (var movement in proposal.Groups[light].Movements)
                    proposal.MovementLights[movement] = light;

            // The plan and the program it gives.
            var plan = new OmsiSignalPhasePlan
            {
                RedYellowSeconds = options.RedYellowSeconds, YellowSeconds = options.YellowSeconds, ClearanceSeconds = options.ClearanceSeconds
            };
            foreach (var phase in phases)
            {
                var planPhase = new OmsiSignalPlanPhase { GreenSeconds = phase.All(group => group.LeftTurns) ? options.LeftGreenSeconds : options.GreenSeconds };
                planPhase.Lights.AddRange(phase.Select(group => proposal.Groups.IndexOf(group)));
                plan.Phases.Add(planPhase);
            }
            proposal.Plan = plan;
            var program = new OmsiSignalProgram { CycleSeconds = 60 };
            foreach (var group in proposal.Groups)
                program.AddLight(group.Name);
            if (plan.Phases.Count > 0 && plan.Problems(program.Lights.Count).Count == 0)
                plan.WriteTo(program);
            proposal.Program = program;

            proposal.CrossingFootpaths = conflicts.Where(conflict => conflict.Kind == OmsiConflictKind.Pedestrian)
                .Select(conflict => crossing.Movements[conflict.First].Turn == OmsiMovementTurn.Pedestrian ? conflict.First : conflict.Second).Distinct().Count();
            foreach (var clash in OmsiSignalConflicts.Clashes(program, conflicts, movement => proposal.MovementLights[movement]))
                (clash.Conflict.Permitted ? proposal.PermittedClashes : proposal.HardClashes).Add(clash);
            return proposal;
        }
    }
}
