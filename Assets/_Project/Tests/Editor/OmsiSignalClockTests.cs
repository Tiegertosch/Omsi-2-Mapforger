using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The preview clock (ADR 085, milestone 5), on the cases openOMSI's own tests use for its clock
    /// (<c>crates/omsi-sim/src/traffic/tests.rs</c>): the preview must run a program as openOMSI does.
    /// </summary>
    public sealed class OmsiSignalClockTests
    {
        private static OmsiSignalProgram Program(double cycle, (int State, double Seconds)[][] lights, (int Light, double At, bool IfNoRequest)[] stops,
            (int Light, double At, bool IfNoRequest, double To)[] jumps)
        {
            var program = new OmsiSignalProgram { CycleSeconds = cycle };
            foreach (var phases in lights)
            {
                var light = new OmsiSignalLight { Name = "L" + program.Lights.Count };
                light.Phases.AddRange(phases.Select(phase => new OmsiSignalPhase(phase.State, phase.Seconds)));
                program.Lights.Add(light);
            }
            foreach (var stop in stops)
                program.Holds.Add(new OmsiSignalHold { Light = stop.Light, Seconds = stop.At, IfNoRequest = stop.IfNoRequest });
            foreach (var jump in jumps)
                program.Holds.Add(new OmsiSignalHold { Light = jump.Light, Seconds = jump.At, IfNoRequest = jump.IfNoRequest, JumpTo = jump.To });
            return program;
        }

        private static void Run(OmsiSignalClock clock, double seconds, double step = 0.1)
        {
            for (var t = 0.0; t < seconds - 1e-9; t += step)
                clock.Advance(step);
        }

        [Test]
        public void ALevelCrossingWaitsForItsTrain()
        {
            var clock = new OmsiSignalClock(Program(22,
                new[] { new[] { (0, 15.0), (6, 4.0), (0, 1.0) }, new[] { (6, 2.0), (9, 12.0), (0, 5.0), (3, 1.0) } },
                new[] { (0, 1.0, true), (0, 16.0, false) }, new (int, double, bool, double)[0]));
            Run(clock, 60);
            Assert.AreEqual(1.0, clock.Time, 1e-6);
            Assert.IsTrue(clock.Held);
            Assert.AreEqual(6, clock.StateOf(1), "road green while no train comes");
            clock.Requests[0] = true;
            Run(clock, 20);
            Assert.AreEqual(16.0, clock.Time, 1e-6, "the train is still there");
            Assert.IsTrue(clock.Held);
            Assert.AreEqual(0, clock.StateOf(1));
            clock.Requests[0] = false;
            clock.Advance(1.0);
            Assert.AreEqual(17.0, clock.Time, 1e-3);
        }

        [Test]
        public void ABusPhaseIsSkippedWhenNoBusComes()
        {
            var program = Program(64, new[] { new[] { (0, 52.0), (3, 2.0), (6, 4.0), (9, 3.0), (0, 0.0) } },
                new (int, double, bool)[0], new[] { (0, 51.5, true, 61.5) });
            var clock = new OmsiSignalClock(program);
            clock.JumpTo(50);
            clock.Advance(2.0);
            Assert.AreEqual(62.0, clock.Time, 1e-3, "jumped");
            clock = new OmsiSignalClock(program);
            clock.JumpTo(50);
            clock.Requests[0] = true;
            clock.Advance(3.0);
            Assert.AreEqual(53.0, clock.Time, 1e-3);
            Assert.AreEqual(3, clock.StateOf(0), "the bus gets its phase");
        }

        [Test]
        public void ABackwardsJumpReplaysItsPhaseOnce()
        {
            var clock = new OmsiSignalClock(Program(12, new[] { new[] { (0, 2.0), (3, 2.0), (6, 4.0), (9, 2.0), (0, 0.0) } },
                new (int, double, bool)[0], new[] { (0, 8.0, false, 4.0) }));
            clock.JumpTo(7.9);
            clock.Requests[0] = true;
            Run(clock, 5);
            Assert.AreEqual(9, clock.StateOf(0), "the clock left the replayed green");
        }

        [Test]
        public void SimultaneousInactiveEventsDoNotStallTheCycle()
        {
            var phases = new[] { (0, 4.0), (6, 16.0) };
            var clock = new OmsiSignalClock(Program(20, new[] { phases, phases }, new[] { (0, 4.0, false) }, new[] { (1, 4.0, false, 12.0) }));
            clock.Advance(5.0);
            Assert.AreEqual(5.0, clock.Time, 1e-9);
            for (var round = 0; round < 40; round++)
                clock.Advance(1.0);
            Assert.AreEqual(5.0, clock.Time, 1e-6, "the clock completes later cycles too");
            Assert.IsFalse(clock.Held);
        }

        [Test]
        public void AJumpToZeroAtTheCycleBoundaryDoesNotFreezeTheProgram()
        {
            var lights = Enumerable.Range(0, 7).Select(_ => new (int, double)[0]).ToArray();
            lights[2] = new[] { (0, 29.0), (6, 12.0), (9, 2.0), (0, 25.0), (6, 12.0), (9, 2.0), (0, 1.0) };
            var clock = new OmsiSignalClock(Program(84, lights, new (int, double, bool)[0], new[] { (4, 64.0, true, 0.0), (4, 84.0, true, 0.0) }));
            clock.JumpTo(63.9);
            clock.Advance(0.2);
            Assert.AreEqual(0.1, clock.Time, 1e-5);
            clock.Advance(29.0);
            Assert.AreEqual(6, clock.StateOf(2), "the bus light reaches its next green");
        }

        [Test]
        public void SimultaneousStopsReleaseAndAreCheckedOnTheNextCycle()
        {
            var phases = new[] { (0, 4.0), (6, 16.0) };
            var clock = new OmsiSignalClock(Program(20, new[] { phases, phases, phases }, new[] { (0, 4.0, false), (1, 4.0, false), (2, 4.0, false) },
                new (int, double, bool, double)[0]));
            clock.Requests[2] = true;
            clock.Advance(5.0);
            Assert.AreEqual(4.0, clock.Time, 1e-9);
            Assert.IsTrue(clock.Held);
            clock.Advance(1.0);
            Assert.AreEqual(4.0, clock.Time, 1e-9, "an active stop remains held next frame");
            clock.Requests[2] = false;
            clock.Advance(1.0);
            Assert.AreEqual(5.0, clock.Time, 1e-9);
            Assert.IsFalse(clock.Held);
            clock.Advance(16.0);
            Assert.AreEqual(1.0, clock.Time, 1e-9);
            clock.Requests[2] = true;
            clock.Advance(4.0);
            Assert.AreEqual(4.0, clock.Time, 1e-9, "the released stop is evaluated after wrapping");
            Assert.IsTrue(clock.Held);
        }

        [Test]
        public void AStopDoesNotHoldTheChangeoverToGreen()
        {
            // A stop a second into red and yellow: the clock goes on to green.
            var clock = new OmsiSignalClock(Program(30, new[] { new[] { (0, 10.0), (3, 2.0), (6, 15.0), (9, 3.0) } }, new[] { (0, 11.0, true) },
                new (int, double, bool, double)[0]));
            clock.JumpTo(9);
            clock.Advance(4.0);
            Assert.IsFalse(clock.Held);
            Assert.AreEqual(13.0, clock.Time, 1e-6);
        }

        [Test]
        public void WithoutStopsAWholeCycleRunsThroughEveryStateAndStartsAgain()
        {
            // Brief case 14: a whole cycle simulated, each light through its states in order, then from the start again.
            var program = Program(52, new[] { new[] { (3, 1.0), (6, 20.0), (9, 3.0), (0, 0.0) }, new[] { (0, 26.0), (3, 1.0), (6, 20.0), (9, 3.0), (0, 0.0) } },
                new (int, double, bool)[0], new (int, double, bool, double)[0]);
            var clock = new OmsiSignalClock(program);
            var seen = Enumerable.Range(0, 2).Select(_ => new System.Collections.Generic.List<int>()).ToArray();
            for (var step = 0; step < 520; step++)
            {
                for (var light = 0; light < 2; light++)
                    if (seen[light].Count == 0 || seen[light][seen[light].Count - 1] != clock.StateOf(light))
                        seen[light].Add(clock.StateOf(light));
                clock.Advance(0.1);
            }
            CollectionAssert.AreEqual(new[] { 3, 6, 9, 0 }, seen[0]);
            CollectionAssert.AreEqual(new[] { 0, 3, 6, 9, 0 }, seen[1]);
            Assert.Less(Math.Min(clock.Time, 52 - clock.Time), 1e-6, "after one cycle the clock is at its start again");
        }
    }
}
