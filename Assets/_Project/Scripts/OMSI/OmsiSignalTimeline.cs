using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A stretch of the cycle during which a light shows one value.</summary>
    public sealed class OmsiSignalSegment
    {
        public double Start { get; internal set; }
        public double End { get; internal set; }
        public int State { get; internal set; }

        /// <summary>True for the stretch after the last phase that ends, where its value holds until the cycle starts again.</summary>
        public bool Holds { get; internal set; }

        public double Seconds => End - Start;
    }

    /// <summary>How serious a finding of the timing check is, as the brief names them (§10).</summary>
    public enum OmsiSignalIssueLevel
    {
        Error,
        Warning,
        Hint
    }

    public enum OmsiSignalIssueKind
    {
        /// <summary>Phases of a light start after the cycle has ended; openOMSI never shows them.</summary>
        PhasesBeyondCycle,

        /// <summary>A phase of 0 s between others, which shows nothing.</summary>
        EmptyPhase,

        /// <summary>A light goes from green straight to red, without yellow.</summary>
        GreenToRed,

        /// <summary>A light goes from red straight to green, without red and yellow (usual in some countries).</summary>
        RedToGreen,

        /// <summary>A stop or jump point lies outside the cycle, or names no light.</summary>
        HoldOutOfRange,

        /// <summary>A pedestrian light shows a value its head cannot show (only red and green).</summary>
        PedestrianAspect
    }

    public sealed class OmsiSignalIssue
    {
        public OmsiSignalIssueLevel Level { get; internal set; }
        public OmsiSignalIssueKind Kind { get; internal set; }

        /// <summary>The light it concerns, or -1.</summary>
        public int Light { get; internal set; } = -1;

        /// <summary>Where in the cycle, in seconds.</summary>
        public double Seconds { get; internal set; }
    }

    /// <summary>
    /// The phases of a program as stretches of its cycle, and the edits of the timeline (ADR 085, milestone 3). An edited
    /// light is written anew from its stretches: neighbours of one value merge, nothing is shorter than nothing, and the
    /// last stretch becomes a phase of 0 s that holds to the end of the cycle, as OMSI's own programs write it. Values of
    /// stretches that are not painted keep their number (OMSI's programs use 1, 2 and 8 too); painting writes 0, 3, 6, 9 or 12.
    /// </summary>
    public static class OmsiSignalTimeline
    {
        /// <summary>The shortest cycle MapWorks writes; 0 would mean "the longest light" and surprise whoever drags an edge.</summary>
        public const double ShortestCycle = 1.0;

        private const double Tiny = 1e-6;

        /// <summary>The stretches of a light over the cycle, from 0 to its end, without phases that start beyond it.</summary>
        public static List<OmsiSignalSegment> Segments(OmsiSignalProgram program, int light)
        {
            Check(program, light);
            var cycle = program.CycleLength;
            var result = new List<OmsiSignalSegment>();
            var phases = program.Lights[light].Phases;
            if (phases.Count == 0)
            {
                result.Add(new OmsiSignalSegment { Start = 0, End = cycle, State = OmsiSignalAspects.StateOf(OmsiSignalAspect.Green), Holds = true });
                return result;
            }
            var time = 0.0;
            foreach (var phase in phases)
            {
                if (time >= cycle - Tiny)
                    break;
                if (phase.Seconds <= Tiny)
                    continue;
                var end = Math.Min(cycle, time + phase.Seconds);
                Add(result, time, end, phase.State, false);
                time = end;
            }
            if (time < cycle - Tiny)
                Add(result, time, cycle, phases[phases.Count - 1].State, true);
            return result;
        }

        private static void Add(List<OmsiSignalSegment> list, double start, double end, int state, bool holds)
        {
            var last = list.Count > 0 ? list[list.Count - 1] : null;
            if (last != null && last.State == state)
            {
                last.End = end;
                last.Holds |= holds;
                return;
            }
            list.Add(new OmsiSignalSegment { Start = start, End = end, State = state, Holds = holds });
        }

        /// <summary>Writes a light from its stretches (which must cover the cycle in order): its phases, the last one of 0 s.</summary>
        public static void SetSegments(OmsiSignalProgram program, int light, IList<OmsiSignalSegment> segments)
        {
            Check(program, light);
            var cycle = program.CycleLength;
            var merged = new List<OmsiSignalSegment>();
            foreach (var segment in segments)
            {
                var start = Math.Max(0, Math.Min(cycle, segment.Start));
                var end = Math.Max(start, Math.Min(cycle, segment.End));
                if (end - start <= Tiny || double.IsNaN(start) || double.IsNaN(end))
                    continue;
                Add(merged, start, end, segment.State, false);
            }
            if (merged.Count == 0)
                merged.Add(new OmsiSignalSegment { Start = 0, End = cycle, State = OmsiSignalAspects.StateOf(OmsiSignalAspect.Red) });
            var phases = program.Lights[light].Phases;
            phases.Clear();
            for (var index = 0; index < merged.Count; index++)
                phases.Add(new OmsiSignalPhase(merged[index].State, index == merged.Count - 1 ? 0 : Round(merged[index].Seconds)));
        }

        /// <summary>Durations go to a thousandth of a second, so dragging does not leave 4.999999 in the file.</summary>
        private static double Round(double seconds)
        {
            return Math.Round(seconds, 3);
        }

        /// <summary>Shows <paramref name="state"/> from <paramref name="from"/> to <paramref name="to"/> seconds (clipped to the cycle).</summary>
        public static void Paint(OmsiSignalProgram program, int light, double from, double to, int state)
        {
            var cycle = program.CycleLength;
            from = Math.Max(0, Math.Min(cycle, from));
            to = Math.Max(from, Math.Min(cycle, to));
            if (to - from <= Tiny)
                return;
            var result = new List<OmsiSignalSegment>();
            foreach (var segment in Segments(program, light))
            {
                if (segment.End <= from || segment.Start >= to)
                {
                    result.Add(segment);
                    continue;
                }
                if (segment.Start < from)
                    result.Add(new OmsiSignalSegment { Start = segment.Start, End = from, State = segment.State });
                if (segment.End > to)
                    result.Add(new OmsiSignalSegment { Start = to, End = segment.End, State = segment.State });
            }
            result.Add(new OmsiSignalSegment { Start = from, End = to, State = state });
            SetSegments(program, light, result.OrderBy(segment => segment.Start).ToList());
        }

        /// <summary>
        /// Moves the edge between stretch <paramref name="index"/> - 1 and stretch <paramref name="index"/> to
        /// <paramref name="seconds"/>, no further than the edges beside it; a stretch dragged to nothing goes.
        /// </summary>
        public static void MoveBoundary(OmsiSignalProgram program, int light, int index, double seconds)
        {
            var segments = Segments(program, light);
            if (index <= 0 || index >= segments.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var low = segments[index - 1].Start;
            var high = segments[index].End;
            var at = Math.Max(low, Math.Min(high, seconds));
            segments[index - 1].End = at;
            segments[index].Start = at;
            SetSegments(program, light, segments);
        }

        /// <summary>Takes stretch <paramref name="index"/> away: the stretch before it (or, for the first, after it) takes its time.</summary>
        public static void RemoveSegment(OmsiSignalProgram program, int light, int index)
        {
            var segments = Segments(program, light);
            if (index < 0 || index >= segments.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (segments.Count == 1)
                return;
            if (index > 0)
                segments[index - 1].End = segments[index].End;
            else
                segments[1].Start = 0;
            segments.RemoveAt(index);
            SetSegments(program, light, segments);
        }

        /// <summary>
        /// Repeats stretch <paramref name="index"/> straight after itself; what came after moves on by its length, and what then
        /// runs past the end of the cycle is cut off.
        /// </summary>
        public static void DuplicateSegment(OmsiSignalProgram program, int light, int index)
        {
            var segments = Segments(program, light);
            if (index < 0 || index >= segments.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var copy = segments[index];
            var length = copy.Seconds;
            var result = new List<OmsiSignalSegment>();
            for (var position = 0; position < segments.Count; position++)
            {
                var segment = segments[position];
                if (position <= index)
                    result.Add(new OmsiSignalSegment { Start = segment.Start, End = segment.End, State = segment.State });
                if (position == index)
                    // (a different value in between keeps the two apart; the same value simply makes one longer stretch)
                    result.Add(new OmsiSignalSegment { Start = segment.End, End = segment.End + length, State = segment.State });
                if (position > index)
                    result.Add(new OmsiSignalSegment { Start = segment.Start + length, End = segment.End + length, State = segment.State });
            }
            SetSegments(program, light, result);
        }

        /// <summary>
        /// Sets the cycle length (at least <see cref="ShortestCycle"/>). Every light keeps its stretches in seconds: a shorter
        /// cycle cuts off what runs past its end, a longer one lets each light's last value hold longer.
        /// </summary>
        public static void SetCycle(OmsiSignalProgram program, double seconds)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentException("The cycle needs a length in seconds.", nameof(seconds));
            seconds = Math.Max(ShortestCycle, Round(seconds));
            var before = Enumerable.Range(0, program.Lights.Count).Select(light => Segments(program, light)).ToList();
            program.CycleSeconds = seconds;
            for (var light = 0; light < program.Lights.Count; light++)
            {
                var segments = before[light];
                if (segments.Count > 0)
                    segments[segments.Count - 1].End = Math.Max(segments[segments.Count - 1].End, seconds);
                SetSegments(program, light, segments);
            }
        }

        /// <summary>
        /// Checks the timing of a program: phases beyond the cycle and empty phases in between, green straight to red and
        /// red straight to green for vehicle lights, values a pedestrian head cannot show, and stop and jump points outside
        /// the cycle. Conflicts between movements are another check (milestone 5).
        /// </summary>
        /// <param name="pedestrianLights">The lights whose movements are all footpaths; their heads show red and green only.</param>
        public static List<OmsiSignalIssue> CheckTiming(OmsiSignalProgram program, ICollection<int> pedestrianLights = null)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            var issues = new List<OmsiSignalIssue>();
            var cycle = program.CycleLength;
            for (var light = 0; light < program.Lights.Count; light++)
            {
                var phases = program.Lights[light].Phases;
                var time = 0.0;
                for (var index = 0; index < phases.Count; index++)
                {
                    if (time >= cycle - Tiny && phases[index].Seconds > Tiny)
                    {
                        issues.Add(new OmsiSignalIssue { Level = OmsiSignalIssueLevel.Warning, Kind = OmsiSignalIssueKind.PhasesBeyondCycle, Light = light, Seconds = time });
                        break;
                    }
                    if (phases[index].Seconds <= Tiny && index < phases.Count - 1)
                        issues.Add(new OmsiSignalIssue { Level = OmsiSignalIssueLevel.Hint, Kind = OmsiSignalIssueKind.EmptyPhase, Light = light, Seconds = time });
                    time += phases[index].Seconds;
                }
                var pedestrian = pedestrianLights != null && pedestrianLights.Contains(light);
                var segments = Segments(program, light);
                for (var index = 0; index < segments.Count; index++)
                {
                    var aspect = OmsiSignalAspects.Of(segments[index].State);
                    if (pedestrian)
                    {
                        // A pedestrian head (ampel2) shows red for every state but 6 to 8; red and yellow and yellow are red there, as the
                        // phase plan writes them for every light. Only a state such as dark, which lets people go, is not meant.
                        if (aspect != OmsiSignalAspect.Red && aspect != OmsiSignalAspect.Green && aspect != OmsiSignalAspect.RedYellow && aspect != OmsiSignalAspect.Yellow)
                            issues.Add(new OmsiSignalIssue
                            {
                                Level = OmsiSignalIssueLevel.Warning, Kind = OmsiSignalIssueKind.PedestrianAspect, Light = light, Seconds = segments[index].Start
                            });
                        continue;
                    }
                    // The cycle runs round: the stretch after the last is the first.
                    if (segments.Count < 2)
                        continue;
                    var next = OmsiSignalAspects.Of(segments[(index + 1) % segments.Count].State);
                    var at = segments[index].End >= cycle - Tiny ? 0 : segments[index].End;
                    if ((aspect == OmsiSignalAspect.Green || aspect == OmsiSignalAspect.GreenYellow) && next == OmsiSignalAspect.Red)
                        issues.Add(new OmsiSignalIssue { Level = OmsiSignalIssueLevel.Warning, Kind = OmsiSignalIssueKind.GreenToRed, Light = light, Seconds = at });
                    else if (aspect == OmsiSignalAspect.Red && next == OmsiSignalAspect.Green)
                        issues.Add(new OmsiSignalIssue { Level = OmsiSignalIssueLevel.Hint, Kind = OmsiSignalIssueKind.RedToGreen, Light = light, Seconds = at });
                }
            }
            foreach (var hold in program.Holds)
                if (hold.Light < 0 || hold.Light >= program.Lights.Count || hold.Seconds > cycle + Tiny || hold.JumpTo.HasValue && hold.JumpTo.Value > cycle + Tiny)
                    issues.Add(new OmsiSignalIssue { Level = OmsiSignalIssueLevel.Error, Kind = OmsiSignalIssueKind.HoldOutOfRange, Light = hold.Light, Seconds = hold.Seconds });
            return issues;
        }

        private static void Check(OmsiSignalProgram program, int light)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            if (light < 0 || light >= program.Lights.Count)
                throw new ArgumentOutOfRangeException(nameof(light));
        }
    }
}
