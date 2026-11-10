using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One traffic phase of a <see cref="OmsiSignalPhasePlan"/>: the groups that may go, and for how long.</summary>
    public sealed class OmsiSignalPlanPhase
    {
        /// <summary>The lights (signal groups) green in this phase, by index.</summary>
        public List<int> Lights { get; } = new List<int>();

        public double GreenSeconds { get; set; }

        public OmsiSignalPlanPhase Copy()
        {
            var copy = new OmsiSignalPlanPhase { GreenSeconds = GreenSeconds };
            copy.Lights.AddRange(Lights);
            return copy;
        }
    }

    /// <summary>
    /// A traffic light program as a sequence of traffic phases (ADR 085, milestone 4; the simple mode of the brief, §8): in
    /// turn, the groups of each phase show red and yellow, green, then yellow, and all red for the clearance time before the
    /// next phase starts. A group green in two phases one after the other stays green in between. The plan is not stored:
    /// it is written into the program's phases (<see cref="WriteTo"/>) and found in them again (<see cref="Detect"/>), and a
    /// program counts as a plan only when writing the plan found gives the very same timeline, so nothing is lost by
    /// editing it this way.
    /// </summary>
    public sealed class OmsiSignalPhasePlan
    {
        private const double Tiny = 1e-3;

        public List<OmsiSignalPlanPhase> Phases { get; } = new List<OmsiSignalPlanPhase>();

        /// <summary>Red and yellow together before green; 0 goes from red straight to green.</summary>
        public double RedYellowSeconds { get; set; } = 1;

        public double YellowSeconds { get; set; } = 3;

        /// <summary>All red after the yellow of a phase, before the next phase starts.</summary>
        public double ClearanceSeconds { get; set; } = 2;

        /// <summary>The length of one phase from its red and yellow to the end of its clearance.</summary>
        public double LengthOf(int phase)
        {
            return RedYellowSeconds + Phases[phase].GreenSeconds + YellowSeconds + ClearanceSeconds;
        }

        public double CycleSeconds => Enumerable.Range(0, Phases.Count).Sum(LengthOf);

        public double StartOf(int phase)
        {
            var start = 0.0;
            for (var index = 0; index < phase; index++)
                start += LengthOf(index);
            return start;
        }

        public OmsiSignalPhasePlan Copy()
        {
            var copy = new OmsiSignalPhasePlan { RedYellowSeconds = RedYellowSeconds, YellowSeconds = YellowSeconds, ClearanceSeconds = ClearanceSeconds };
            copy.Phases.AddRange(Phases.Select(phase => phase.Copy()));
            return copy;
        }

        /// <summary>The reasons a plan cannot be written; empty when it can.</summary>
        public List<string> Problems(int lightCount)
        {
            var problems = new List<string>();
            if (Phases.Count == 0)
                problems.Add("no phases");
            if (RedYellowSeconds < 0 || YellowSeconds < 0 || ClearanceSeconds < 0)
                problems.Add("negative time");
            foreach (var phase in Phases)
            {
                if (phase.GreenSeconds <= 0)
                    problems.Add("phase without green time");
                if (phase.Lights.Any(light => light < 0 || light >= lightCount))
                    problems.Add("phase names a group that does not exist");
            }
            if (CycleSeconds < OmsiSignalTimeline.ShortestCycle)
                problems.Add("cycle too short");
            return problems;
        }

        /// <summary>
        /// Writes the plan into the program: its cycle and the phases of every light; a light in no phase is red all the
        /// time. Names, stop and jump points and path bindings stay as they are.
        /// </summary>
        public void WriteTo(OmsiSignalProgram program)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            var problems = Problems(program.Lights.Count);
            if (problems.Count > 0)
                throw new InvalidOperationException("The phase plan cannot be written: " + string.Join(", ", problems) + ".");
            var cycle = CycleSeconds;
            program.CycleSeconds = cycle;
            var red = OmsiSignalAspects.StateOf(OmsiSignalAspect.Red);
            for (var light = 0; light < program.Lights.Count; light++)
            {
                var stretches = new List<(double Start, double End, int State)>();
                var phase = 0;
                while (phase < Phases.Count)
                {
                    if (!Phases[phase].Lights.Contains(light))
                    {
                        phase++;
                        continue;
                    }
                    // A run of phases one after the other in which the light is green.
                    var last = phase;
                    while (last + 1 < Phases.Count && Phases[last + 1].Lights.Contains(light))
                        last++;
                    var start = StartOf(phase);
                    var greenEnd = StartOf(last) + RedYellowSeconds + Phases[last].GreenSeconds;
                    if (RedYellowSeconds > 0)
                        stretches.Add((start, start + RedYellowSeconds, OmsiSignalAspects.StateOf(OmsiSignalAspect.RedYellow)));
                    stretches.Add((start + RedYellowSeconds, greenEnd, OmsiSignalAspects.StateOf(OmsiSignalAspect.Green)));
                    if (YellowSeconds > 0)
                        stretches.Add((greenEnd, greenEnd + YellowSeconds, OmsiSignalAspects.StateOf(OmsiSignalAspect.Yellow)));
                    phase = last + 1;
                }
                var segments = new List<OmsiSignalSegment>();
                var at = 0.0;
                foreach (var stretch in stretches)
                {
                    if (stretch.Start > at + 1e-9)
                        segments.Add(new OmsiSignalSegment { Start = at, End = stretch.Start, State = red });
                    segments.Add(new OmsiSignalSegment { Start = stretch.Start, End = stretch.End, State = stretch.State });
                    at = stretch.End;
                }
                if (at < cycle - 1e-9)
                    segments.Add(new OmsiSignalSegment { Start = at, End = cycle, State = red });
                OmsiSignalTimeline.SetSegments(program, light, segments);
            }
        }

        /// <summary>
        /// The plan behind a program, or null when it has none: a value other than red, red and yellow, green or yellow
        /// (raw values other than the ones the plan writes, such as OMSI's other reds 1 and 2, count as others), red and
        /// yellow or yellow of differing lengths, no phase starting at 0 s, or anything else that writing the plan found
        /// would not give back exactly.
        /// </summary>
        public static OmsiSignalPhasePlan Detect(OmsiSignalProgram program)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            if (program.Lights.Count == 0)
                return null;
            var cycle = program.CycleLength;
            var all = Enumerable.Range(0, program.Lights.Count).Select(light => OmsiSignalTimeline.Segments(program, light)).ToList();
            var aspects = new[] { OmsiSignalAspect.Red, OmsiSignalAspect.RedYellow, OmsiSignalAspect.Green, OmsiSignalAspect.Yellow };
            if (all.Any(segments => segments.Any(segment => !aspects.Contains(OmsiSignalAspects.Of(segment.State)))))
                return null;

            double? redYellow = null, yellow = null;
            var starts = new List<double>();
            var ends = new List<double>();
            foreach (var segments in all)
                for (var index = 0; index < segments.Count; index++)
                {
                    var aspect = OmsiSignalAspects.Of(segments[index].State);
                    var before = index > 0 ? OmsiSignalAspects.Of(segments[index - 1].State) : OmsiSignalAspect.Red;
                    var after = index + 1 < segments.Count ? OmsiSignalAspects.Of(segments[index + 1].State) : OmsiSignalAspect.Red;
                    if (aspect == OmsiSignalAspect.RedYellow)
                    {
                        if (redYellow.HasValue && Math.Abs(redYellow.Value - segments[index].Seconds) > Tiny)
                            return null;
                        redYellow = segments[index].Seconds;
                        starts.Add(segments[index].Start);
                    }
                    if (aspect == OmsiSignalAspect.Green && before == OmsiSignalAspect.Red)
                        starts.Add(segments[index].Start);
                    if (aspect == OmsiSignalAspect.Yellow)
                    {
                        if (yellow.HasValue && Math.Abs(yellow.Value - segments[index].Seconds) > Tiny)
                            return null;
                        yellow = segments[index].Seconds;
                        ends.Add(segments[index].End);
                    }
                    if (aspect == OmsiSignalAspect.Green && after == OmsiSignalAspect.Red)
                        ends.Add(segments[index].End);
                }
            starts = starts.OrderBy(start => start).Aggregate(new List<double>(), (list, start) =>
            {
                if (list.Count == 0 || start - list[list.Count - 1] > Tiny)
                    list.Add(start);
                return list;
            });
            if (starts.Count == 0 || ends.Count == 0 || starts[0] > Tiny)
                return null;
            var plan = new OmsiSignalPhasePlan { RedYellowSeconds = redYellow ?? 0, YellowSeconds = yellow ?? 0 };
            // The clearance: from the end of a light's yellow to the next phase start.
            var end = ends[0];
            var next = starts.FirstOrDefault(start => start >= end - Tiny);
            plan.ClearanceSeconds = Math.Max(0, (starts.Any(start => start >= end - Tiny) ? next : cycle) - end);
            for (var phase = 0; phase < starts.Count; phase++)
            {
                var length = (phase + 1 < starts.Count ? starts[phase + 1] : cycle) - starts[phase];
                var green = length - plan.RedYellowSeconds - plan.YellowSeconds - plan.ClearanceSeconds;
                if (green <= Tiny)
                    return null;
                var planPhase = new OmsiSignalPlanPhase { GreenSeconds = Math.Round(green, 3) };
                var middle = starts[phase] + plan.RedYellowSeconds + green / 2;
                for (var light = 0; light < program.Lights.Count; light++)
                    if (OmsiSignalAspects.Of(program.StateAt(light, middle)) == OmsiSignalAspect.Green)
                        planPhase.Lights.Add(light);
                plan.Phases.Add(planPhase);
            }
            if (plan.Problems(program.Lights.Count).Count > 0)
                return null;

            // Only a plan that gives back the very same timeline counts.
            var again = program.Copy();
            plan.WriteTo(again);
            if (Math.Abs(again.CycleLength - cycle) > Tiny)
                return null;
            for (var light = 0; light < program.Lights.Count; light++)
                if (!SameTimeline(all[light], OmsiSignalTimeline.Segments(again, light)))
                    return null;
            return plan;
        }

        /// <summary>
        /// The simplified view of any program (§8 of the brief): the cycle cut wherever the set of groups that may go
        /// (green or dark) changes, with that set for each window, in order. Read-only; it says what goes when even where
        /// the program is no phase plan.
        /// </summary>
        public static List<(double From, double To, List<int> Lights)> Windows(OmsiSignalProgram program)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            var cycle = program.CycleLength;
            var cuts = new SortedSet<double> { 0.0, cycle };
            for (var light = 0; light < program.Lights.Count; light++)
                foreach (var segment in OmsiSignalTimeline.Segments(program, light))
                {
                    cuts.Add(segment.Start);
                    cuts.Add(segment.End);
                }
            var windows = new List<(double From, double To, List<int> Lights)>();
            var points = cuts.Where(cut => cut >= 0 && cut <= cycle).ToList();
            for (var index = 0; index + 1 < points.Count; index++)
            {
                var from = points[index];
                var to = points[index + 1];
                if (to - from < Tiny)
                    continue;
                var middle = (from + to) / 2;
                var going = Enumerable.Range(0, program.Lights.Count).Where(light => OmsiSignalAspects.AllowsGo(program.StateAt(light, middle))).ToList();
                if (windows.Count > 0 && windows[windows.Count - 1].Lights.SequenceEqual(going))
                    windows[windows.Count - 1] = (windows[windows.Count - 1].From, to, going);
                else
                    windows.Add((from, to, going));
            }
            return windows;
        }

        private static bool SameTimeline(List<OmsiSignalSegment> first, List<OmsiSignalSegment> second)
        {
            if (first.Count != second.Count)
                return false;
            for (var index = 0; index < first.Count; index++)
                if (first[index].State != second[index].State ||
                    Math.Abs(first[index].Start - second[index].Start) > Tiny || Math.Abs(first[index].End - second[index].End) > Tiny)
                    return false;
            return true;
        }
    }
}
