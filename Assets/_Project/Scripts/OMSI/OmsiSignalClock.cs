using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The cycle clock of a traffic light program for the preview (ADR 085, milestone 5): the position in the cycle, run on
    /// by time and held or moved by the stop and jump points with the requests given, exactly as openOMSI runs it
    /// (<c>crates/omsi-sim/src/traffic/lights.rs</c>, <c>TrafficLightController::advance</c>, ported here, AGPL like the
    /// rest of MapWorks). It changes nothing in any file; the states are the program's own (<see cref="OmsiSignalProgram.StateAt"/>).
    /// A stop holds the whole crossing's clock. With "if request" set, a point counts when nobody asks at its light (or at
    /// a light of another such point at the same time and target); without, when somebody asks. A stop never holds while a
    /// light shows red and yellow on its way to green. A short jump back taken while somebody asks is replayed once; a jump
    /// past the cycle's end starts the next cycle at 0.
    /// </summary>
    public sealed class OmsiSignalClock
    {
        private readonly OmsiSignalProgram program;
        private readonly List<int> passed = new List<int>();
        private int? rewound;

        public OmsiSignalClock(OmsiSignalProgram program)
        {
            this.program = program ?? throw new ArgumentNullException(nameof(program));
            Requests = new bool[program.Lights.Count];
        }

        /// <summary>The position in the cycle, in seconds.</summary>
        public double Time { get; private set; }

        /// <summary>True while a stop point holds the clock.</summary>
        public bool Held { get; private set; }

        /// <summary>Per light: somebody asks for it (a vehicle at its stop line, a pedestrian at the button).</summary>
        public bool[] Requests { get; }

        public double CycleLength => program.CycleLength;

        public int StateOf(int light) => program.StateAt(light, Time);

        /// <summary>Puts the clock at a second of the cycle, as a jump in the preview; the points there count as passed.</summary>
        public void JumpTo(double seconds)
        {
            var cycle = CycleLength;
            Time = ((seconds % cycle) + cycle) % cycle;
            Held = false;
            rewound = null;
            passed.Clear();
            for (var index = 0; index < program.Holds.Count; index++)
                if (Math.Abs(Distance(program.Holds[index].Seconds, Time, cycle)) < 1e-6)
                    passed.Add(index);
        }

        private static double Distance(double to, double from, double cycle)
        {
            var d = ((to - from) % cycle + cycle) % cycle;
            return d > cycle - 1e-6 ? 0.0 : d;
        }

        private bool Asked(int light) => light >= 0 && light < Requests.Length && Requests[light];

        /// <summary>Some light shows red and yellow on its way to green: a stop does not hold then.</summary>
        private bool InChangeover()
        {
            for (var light = 0; light < program.Lights.Count; light++)
                if (OmsiSignalAspects.Of(StateOf(light)) == OmsiSignalAspect.RedYellow &&
                    program.Lights[light].Phases.Any(phase => OmsiSignalAspects.Of(phase.State) == OmsiSignalAspect.Green ||
                                                              OmsiSignalAspects.Of(phase.State) == OmsiSignalAspect.GreenYellow))
                    return true;
            return false;
        }

        /// <summary>Runs the clock on by <paramref name="seconds"/>, honouring the stop and jump points with the current requests.</summary>
        public void Advance(double seconds)
        {
            var cycle = CycleLength;
            var left = Math.Max(0.0, seconds);
            var holds = program.Holds;
            Held = false;
            void ClearRewind(double moveBy)
            {
                if (!rewound.HasValue)
                    return;
                var source = holds[rewound.Value].Seconds;
                if ((Math.Abs(Time - source) < 1e-6 && moveBy > 1e-6) || (Time < source && Time + moveBy > source + 1e-6))
                    rewound = null;
            }
            // Every coincident point that does not act may be let past without losing time; authored jump loops stay bounded.
            for (var round = 0; round < 16 + holds.Count; round++)
            {
                var best = -1;
                var bestDistance = double.MaxValue;
                for (var index = 0; index < holds.Count; index++)
                {
                    if (rewound == index)
                        continue;
                    var d = Distance(holds[index].Seconds, Time, cycle);
                    if (d < 1e-6 && passed.Contains(index))
                        continue;
                    if (d <= left && d < bestDistance)
                    {
                        best = index;
                        bestDistance = d;
                    }
                }
                if (best < 0)
                {
                    if (left > 0.0)
                    {
                        ClearRewind(left);
                        if (left > 1e-6)
                            passed.Clear();
                    }
                    Time = ((Time + left) % cycle + cycle) % cycle;
                    return;
                }
                if (bestDistance > 1e-6)
                    passed.Clear();
                ClearRewind(bestDistance);
                Time = ((Time + bestDistance) % cycle + cycle) % cycle;
                left -= bestDistance;
                var point = holds[best];
                bool asked;
                if (point.IfNoRequest)
                    asked = holds.Any(other => other.IfNoRequest && other.Seconds == point.Seconds && Nullable.Equals(other.JumpTo, point.JumpTo) && Asked(other.Light));
                else
                    asked = Asked(point.Light);
                var active = point.IfNoRequest ? !asked : asked;
                var holdable = point.JumpTo.HasValue || !InChangeover();
                if (!active || !holdable)
                {
                    passed.Add(best);
                    continue;
                }
                if (point.JumpTo.HasValue)
                {
                    var to = point.JumpTo.Value;
                    var target = to < cycle ? ((to % cycle) + cycle) % cycle : 0.0;
                    var noProgress = Math.Abs(((target - point.Seconds) % cycle + cycle) % cycle) < 1e-6;
                    rewound = !point.IfNoRequest && to > 1e-6 && to < point.Seconds - 1e-6 ? best : (int?)null;
                    Time = target;
                    passed.Clear();
                    for (var index = 0; index < holds.Count; index++)
                        if (index != best && (((holds[index].Seconds - target) % cycle + cycle) % cycle < 1e-6 ||
                                              ((target - holds[index].Seconds) % cycle + cycle) % cycle < 1e-6))
                            passed.Add(index);
                    if (noProgress)
                        passed.Add(best);
                    if (left <= 0.0)
                        return;
                }
                else
                {
                    Time = point.Seconds;
                    Held = true;
                    passed.Clear();
                    return;
                }
            }
        }
    }
}
