using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSignalTimelineTests
    {
        /// <summary>
        /// A 60 s program: light 0 red-yellow 2, green 25, yellow 3, then red to the end; light 1 red 32, green 24, red to the
        /// end, with a value of 1 (OMSI's other red) in its last phase.
        /// </summary>
        private static OmsiSignalProgram Program()
        {
            var program = new OmsiSignalProgram { CycleSeconds = 60 };
            program.Lights.Add(new OmsiSignalLight { Name = "Main" });
            program.Lights[0].Phases.AddRange(new[] { new OmsiSignalPhase(3, 2), new OmsiSignalPhase(6, 25), new OmsiSignalPhase(9, 3), new OmsiSignalPhase(0, 0) });
            program.Lights.Add(new OmsiSignalLight { Name = "Side" });
            program.Lights[1].Phases.AddRange(new[] { new OmsiSignalPhase(0, 32), new OmsiSignalPhase(6, 24), new OmsiSignalPhase(1, 0) });
            return program;
        }

        private static string Text(OmsiSignalProgram program, int light)
        {
            return string.Join(" ", program.Lights[light].Phases.Select(phase => FormattableString.Invariant($"{phase.State}/{phase.Seconds}")));
        }

        [Test]
        public void TheSegmentsCoverTheCycleAndTheLastHolds()
        {
            var segments = OmsiSignalTimeline.Segments(Program(), 0);

            CollectionAssert.AreEqual(new[] { 3, 6, 9, 0 }, segments.Select(segment => segment.State).ToArray());
            CollectionAssert.AreEqual(new[] { 0.0, 2, 27, 30 }, segments.Select(segment => segment.Start).ToArray());
            Assert.AreEqual(60, segments.Last().End);
            Assert.IsTrue(segments.Last().Holds);
            Assert.IsFalse(segments[0].Holds);
        }

        [Test]
        public void PhasesBeyondTheCycleAreNotShownAndAreReported()
        {
            var program = Program();
            program.Lights[1].Phases.Clear();
            program.Lights[1].Phases.AddRange(new[] { new OmsiSignalPhase(0, 50), new OmsiSignalPhase(6, 20), new OmsiSignalPhase(9, 5) });

            var segments = OmsiSignalTimeline.Segments(program, 1);

            CollectionAssert.AreEqual(new[] { 0, 6 }, segments.Select(segment => segment.State).ToArray());
            Assert.AreEqual(60, segments.Last().End);
            var issue = OmsiSignalTimeline.CheckTiming(program).Single(found => found.Kind == OmsiSignalIssueKind.PhasesBeyondCycle);
            Assert.AreEqual(1, issue.Light);
            Assert.AreEqual(OmsiSignalIssueLevel.Warning, issue.Level);
        }

        [Test]
        public void ANewGroupIsPaintedIntoAWholeCycle()
        {
            // A group just added is red the whole cycle, one stretch; dragging paints it, Split paints from a cut to the end.
            var program = new OmsiSignalProgram { CycleSeconds = 60 };
            program.AddLight("New");
            Assert.AreEqual(1, OmsiSignalTimeline.Segments(program, 0).Count);

            OmsiSignalTimeline.Paint(program, 0, 0, 25, OmsiSignalAspects.StateOf(OmsiSignalAspect.Green));
            OmsiSignalTimeline.Paint(program, 0, 25, 28, OmsiSignalAspects.StateOf(OmsiSignalAspect.Yellow));
            Assert.AreEqual("6/25 9/3 0/0", Text(program, 0));

            var red = OmsiSignalTimeline.Segments(program, 0).Last();
            OmsiSignalTimeline.Paint(program, 0, 58, red.End, OmsiSignalAspects.StateOf(OmsiSignalAspect.RedYellow));
            Assert.AreEqual("6/25 9/3 0/30 3/0", Text(program, 0));
            CollectionAssert.AreEqual(new[] { 0.0, 25, 28, 58 }, OmsiSignalTimeline.Segments(program, 0).Select(segment => segment.Start).ToArray());
        }

        [Test]
        public void PaintingWritesTheLightAnewAndKeepsWhatItDoesNotTouch()
        {
            var program = Program();

            // Red-yellow before the green of the side road, from 30 to 32.
            OmsiSignalTimeline.Paint(program, 1, 30, 32, OmsiSignalAspects.StateOf(OmsiSignalAspect.RedYellow));

            Assert.AreEqual("0/30 3/2 6/24 1/0", Text(program, 1));
            // The main road was not touched.
            Assert.AreEqual("3/2 6/25 9/3 0/0", Text(program, 0));
            // Painting a stretch with the value next to it merges them.
            OmsiSignalTimeline.Paint(program, 1, 56, 60, 6);
            Assert.AreEqual("0/30 3/2 6/0", Text(program, 1));
            Assert.AreEqual(6, program.StateAt(1, 59));
        }

        [Test]
        public void MovingAnEdgeStaysBetweenItsNeighboursAndADraggedOutStretchGoes()
        {
            var program = Program();

            // The edge between green and yellow moves back to 20.4 s: the green gets shorter, the yellow longer.
            OmsiSignalTimeline.MoveBoundary(program, 0, 2, 20.4);
            Assert.AreEqual("3/2 6/18.4 9/9.6 0/0", Text(program, 0));
            Assert.AreEqual(OmsiSignalAspect.Yellow, OmsiSignalAspects.Of(program.StateAt(0, 24)));

            // No further than the edge after it: the yellow is dragged out of existence.
            OmsiSignalTimeline.MoveBoundary(program, 0, 3, 5);
            Assert.AreEqual("3/2 6/18.4 0/0", Text(program, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => OmsiSignalTimeline.MoveBoundary(program, 0, 0, 3));
        }

        [Test]
        public void RemovingAndDuplicatingStretches()
        {
            var program = Program();

            OmsiSignalTimeline.RemoveSegment(program, 0, 2);
            Assert.AreEqual("3/2 6/28 0/0", Text(program, 0));
            OmsiSignalTimeline.RemoveSegment(program, 0, 0);
            Assert.AreEqual("6/30 0/0", Text(program, 0));

            program = Program();
            OmsiSignalTimeline.DuplicateSegment(program, 0, 2);
            // A second yellow straight after the first makes one of 6 s; the red after it moves on.
            Assert.AreEqual("3/2 6/25 9/6 0/0", Text(program, 0));
            program = Program();
            OmsiSignalTimeline.DuplicateSegment(program, 0, 1);
            // A green of 25 s twice runs past nothing yet: 2 + 50 + 3, red for the last 5 s.
            Assert.AreEqual("3/2 6/50 9/3 0/0", Text(program, 0));
        }

        [Test]
        public void TheCycleCutsOffOrLetsTheLastValueHold()
        {
            var program = Program();

            OmsiSignalTimeline.SetCycle(program, 40);
            Assert.AreEqual(40, program.CycleSeconds);
            Assert.AreEqual("3/2 6/25 9/3 0/0", Text(program, 0));
            // The side road's green from 32 is cut to 8 s, and its last phase is that green.
            Assert.AreEqual("0/32 6/0", Text(program, 1));

            OmsiSignalTimeline.SetCycle(program, 90);
            Assert.AreEqual(OmsiSignalAspect.Green, OmsiSignalAspects.Of(program.StateAt(1, 85)));
            OmsiSignalTimeline.SetCycle(program, 0);
            Assert.AreEqual(OmsiSignalTimeline.ShortestCycle, program.CycleSeconds);
        }

        [Test]
        public void TheTimingCheckFindsMissingYellowAndPointsOutsideTheCycle()
        {
            var program = Program();
            var clean = OmsiSignalTimeline.CheckTiming(program);
            // Light 1 goes from red to green without red-yellow: a hint, not more. Green to red without yellow: a warning.
            Assert.IsTrue(clean.Any(issue => issue.Light == 1 && issue.Kind == OmsiSignalIssueKind.RedToGreen && issue.Level == OmsiSignalIssueLevel.Hint));
            Assert.IsTrue(clean.Any(issue => issue.Light == 1 && issue.Kind == OmsiSignalIssueKind.GreenToRed && issue.Level == OmsiSignalIssueLevel.Warning));
            Assert.IsFalse(clean.Any(issue => issue.Light == 0));

            // As a pedestrian light the same light is fine, but a yellow is not.
            Assert.IsFalse(OmsiSignalTimeline.CheckTiming(program, new[] { 1 }).Any(issue => issue.Light == 1));
            Assert.IsTrue(OmsiSignalTimeline.CheckTiming(program, new[] { 0 }).Any(issue => issue.Light == 0 && issue.Kind == OmsiSignalIssueKind.PedestrianAspect));

            program.Holds.Add(new OmsiSignalHold { Light = 1, Seconds = 75 });
            Assert.IsTrue(OmsiSignalTimeline.CheckTiming(program).Any(issue => issue.Kind == OmsiSignalIssueKind.HoldOutOfRange && issue.Level == OmsiSignalIssueLevel.Error));
        }

        [Test]
        public void AnEditedProgramSurvivesTheFileRoundTrip()
        {
            var program = Program();
            OmsiSignalTimeline.Paint(program, 1, 30, 32, 3);
            OmsiSignalTimeline.MoveBoundary(program, 0, 2, 20.25);
            var plain = "[friendlyname]\r\nX\r\n\r\n[path]\r\n0\r\n0\r\n0\r\n0\r\n0\r\n10\r\n0\r\n0\r\n0\r\n3\r\n0\r\n0\r\n\r\n";
            var file = OmsiRecordFile.Parse(OmsiTextFile.Encode(plain, OmsiTextEncoding.Windows1252), OmsiSceneryRecordLayouts.Instance);
            program.PathLights.Add(0);

            program.WriteTo(file);
            var back = OmsiSignalProgram.Read(file.ToBytes());

            Assert.AreEqual(Text(program, 0), Text(back, 0));
            Assert.AreEqual(Text(program, 1), Text(back, 1));
            for (var second = 0.5; second < 60; second += 1)
            {
                Assert.AreEqual(program.StateAt(0, second), back.StateAt(0, second));
                Assert.AreEqual(program.StateAt(1, second), back.StateAt(1, second));
            }
        }
    }
}
