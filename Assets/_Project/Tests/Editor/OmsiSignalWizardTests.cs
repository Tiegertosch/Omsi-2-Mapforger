using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// The signal assistant, conflicts and phase plans on junctions the generator makes (ADR 085, milestone 4): the cases of
    /// the brief (§16) one lane each way, two and four lanes each way, a left-turn lane, a shared lane and five arms.
    /// </summary>
    public sealed class OmsiSignalWizardTests
    {
        private const string ProfilePath = @"Splines\Test\road.sli";

        /// <summary>
        /// A road with <paramref name="lanes"/> lanes each way, 3.5 m wide, a pavement with a footpath on either side and,
        /// when asked, a raised 3 m median between the carriageways (a left-turn lane is taken out of it).
        /// </summary>
        private static OmsiSplineProfile Profile(int lanes, bool median = false)
        {
            string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
            var inner = median ? 1.5 : 0.0;
            var half = inner + 3.5 * lanes + 0.5;
            const string L = "\r\n";
            var text = new StringBuilder("[texture]" + L + "side.bmp" + L + "[texture]" + L + "asphalt.bmp" + L + "[texture]" + L + "grass.bmp" + L);
            void Section(int texture, double from, double fromHeight, double to, double toHeight) =>
                text.Append("[profile]" + L + texture + L + "[profilepnt]" + L + N(from) + L + N(fromHeight) + L + "0" + L + "0.2" + L +
                            "[profilepnt]" + L + N(to) + L + N(toHeight) + L + "1" + L + "0.2" + L);
            Section(0, -half - 3, 0.25, -half, 0.25);
            Section(0, -half, 0.25, -half, 0.1);
            if (median)
            {
                Section(1, -half, 0.1, -inner, 0.1);
                Section(2, -inner, 0.25, inner, 0.25);
                Section(1, inner, 0.1, half, 0.1);
            }
            else
                Section(1, -half, 0.1, half, 0.1);
            Section(0, half, 0.1, half, 0.25);
            Section(0, half, 0.25, half + 3, 0.25);
            for (var lane = 0; lane < lanes; lane++)
            {
                var x = inner + 1.75 + 3.5 * lane;
                text.Append("[path]" + L + "0" + L + N(-x) + L + "0.1" + L + "3.4" + L + "1" + L);
                text.Append("[path]" + L + "0" + L + N(x) + L + "0.1" + L + "3.4" + L + "0" + L);
            }
            text.Append("[path]" + L + "1" + L + N(-half - 1.5) + L + "0.25" + L + "1" + L + "2" + L);
            text.Append("[path]" + L + "1" + L + N(half + 1.5) + L + "0.25" + L + "1" + L + "2" + L);
            return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(text.ToString()), ProfilePath);
        }

        private static List<OmsiJunctionArm> Arms(int lanes, params double[] headings)
        {
            return Arms(Profile(lanes), headings);
        }

        private static List<OmsiJunctionArm> Arms(OmsiSplineProfile profile, params double[] headings)
        {
            return headings.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList();
        }

        private sealed class Made
        {
            public OmsiSceneryObject Scenery;
            public OmsiSignalIntersection Crossing;
            public List<OmsiSignalConflict> Conflicts;
            public OmsiSignalProposal Proposal;
        }

        private static Made Make(List<OmsiJunctionArm> arms, OmsiSignalWizardOptions options = null)
        {
            var made = OmsiJunctionGenerator.Junction(arms, null, "Wizard");
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var scenery = OmsiSceneryObject.Parse(bytes);
            var crossing = OmsiSignalIntersection.Analyse(scenery, OmsiSignalProgram.Read(bytes));
            var conflicts = OmsiSignalConflicts.Of(scenery, crossing);
            var turnLanes = OmsiSignalWizard.TurnLaneMovements(crossing, OmsiJunctionLayout.Build(arms, null));
            return new Made { Scenery = scenery, Crossing = crossing, Conflicts = conflicts, Proposal = OmsiSignalWizard.Propose(crossing, conflicts, options, turnLanes) };
        }

        /// <summary>What every proposal must be: every vehicle movement in a group, conflict-free, no timing findings, a phase plan.</summary>
        private static void AssertSound(Made made)
        {
            var proposal = made.Proposal;
            Assert.IsTrue(made.Crossing.Movements.Where(movement => movement.Turn != OmsiMovementTurn.Pedestrian).All(movement => proposal.MovementLights[movement.Index] >= 0),
                "every vehicle movement has a group");
            Assert.IsTrue(proposal.ConflictFree, "hard clashes: " + proposal.HardClashes.Count);
            CollectionAssert.IsEmpty(OmsiSignalTimeline.CheckTiming(proposal.Program).Select(issue => issue.Kind + " " + issue.Light + " " + issue.Seconds));
            var plan = OmsiSignalPhasePlan.Detect(proposal.Program);
            Assert.IsNotNull(plan, "the proposal is a phase plan");
            Assert.AreEqual(proposal.Plan.Phases.Count, plan.Phases.Count);
            // Footpaths of a junction made here go round its corners and cross no lane.
            Assert.AreEqual(0, proposal.CrossingFootpaths);
        }

        [Test]
        public void ACrossroadsWithOneLaneEachWayGetsTwoPhasesWithLeftTurnsGivingWay()
        {
            var made = Make(Arms(1, 0, 90, 180, 270));

            AssertSound(made);
            Assert.AreEqual(2, made.Proposal.Groups.Count);
            Assert.AreEqual(2, made.Proposal.Plan.Phases.Count);
            // The opposing arms go together; their left turns cross the oncoming traffic and give way.
            Assert.IsTrue(made.Proposal.Groups.All(group => group.Approaches.Count == 2));
            Assert.IsNotEmpty(made.Proposal.PermittedClashes);
            Assert.AreEqual(2 * (1 + 20 + 3 + 2), made.Proposal.Program.CycleLength, 1e-6);
            // Crossing movements of neighbouring arms conflict without giving way.
            Assert.IsTrue(made.Conflicts.Any(conflict => !conflict.Permitted && conflict.Kind == OmsiConflictKind.Crossing));
        }

        [Test]
        public void TwoAndFourLanesEachWayStillMakeTwoPhases()
        {
            foreach (var lanes in new[] { 2, 4 })
            {
                var made = Make(Arms(lanes, 0, 90, 180, 270));

                AssertSound(made);
                Assert.AreEqual(2, made.Proposal.Plan.Phases.Count, lanes + " lanes");
                Assert.IsTrue(made.Crossing.Approaches.All(approach => approach.Lanes.Count == lanes), lanes + " lanes found");
            }
        }

        [Test]
        public void ALeftTurnLaneGetsAPhaseOfItsOwnWhenChosen()
        {
            var arms = Arms(Profile(1, true), 0, 90, 180, 270);
            arms[0].TurnLane = new OmsiTurnLane();
            arms[2].TurnLane = new OmsiTurnLane();

            var separate = Make(arms);
            AssertSound(separate);
            Assert.AreEqual(2, separate.Proposal.LeftLanes);
            var lefts = separate.Proposal.Groups.Where(group => group.LeftTurns).ToList();
            Assert.IsNotEmpty(lefts);
            Assert.AreEqual(3, separate.Proposal.Plan.Phases.Count);
            Assert.IsTrue(lefts.All(group => group.Name.EndsWith(" left", StringComparison.Ordinal)));

            var together = Make(arms, new OmsiSignalWizardOptions { SeparateLeftTurns = false });
            AssertSound(together);
            Assert.AreEqual(2, together.Proposal.Plan.Phases.Count);
            Assert.IsFalse(together.Proposal.Groups.Any(group => group.LeftTurns));
        }

        [Test]
        public void AT_JunctionWithASharedLanePairsTheThroughRoad()
        {
            var made = Make(Arms(1, 90, 270, 0));

            AssertSound(made);
            Assert.AreEqual(2, made.Proposal.Plan.Phases.Count);
            // The side arm's one lane is shared by its left and right turns; they stay one group.
            var side = made.Crossing.Approaches.Single(approach => made.Proposal.Groups.Single(group => group.Approaches.Contains(approach.Index)).Approaches.Count == 1);
            Assert.AreEqual(1, side.Lanes.Count);
            Assert.GreaterOrEqual(side.Lanes[0].Movements.Count, 2);
        }

        [Test]
        public void FiveArmsGetAPhaseForEveryArmLeftAlone()
        {
            var made = Make(Arms(1, 0, 72, 144, 216, 288));

            AssertSound(made);
            Assert.AreEqual(5, made.Crossing.Approaches.Count);
            Assert.AreEqual(made.Proposal.Groups.Count, made.Proposal.Plan.Phases.Count);
            Assert.GreaterOrEqual(made.Proposal.Plan.Phases.Count, 3);
        }

        [Test]
        public void APhasePlanIsFoundAgainAndChangesWriteBack()
        {
            var made = Make(Arms(1, 0, 90, 180, 270));
            var program = made.Proposal.Program;
            var plan = OmsiSignalPhasePlan.Detect(program);

            plan.Phases[0].GreenSeconds = 30;
            plan.YellowSeconds = 4;
            plan.WriteTo(program);

            var again = OmsiSignalPhasePlan.Detect(program);
            Assert.IsNotNull(again);
            Assert.AreEqual(30, again.Phases[0].GreenSeconds, 1e-6);
            Assert.AreEqual(4, again.YellowSeconds, 1e-6);
            Assert.AreEqual(2 * (1 + 4 + 2) + 30 + 20, again.CycleSeconds, 1e-6);
            Assert.AreEqual(plan.CycleSeconds, program.CycleLength, 1e-6);
        }

        [Test]
        public void AGroupGreenInTwoPhasesInARowStaysGreenBetween()
        {
            var program = new OmsiSignalProgram();
            program.AddLight("Through");
            program.AddLight("Left");
            var plan = new OmsiSignalPhasePlan();
            plan.Phases.Add(new OmsiSignalPlanPhase { GreenSeconds = 20 });
            plan.Phases[0].Lights.Add(0);
            plan.Phases.Add(new OmsiSignalPlanPhase { GreenSeconds = 8 });
            plan.Phases[1].Lights.AddRange(new[] { 0, 1 });

            plan.WriteTo(program);

            // Through: red and yellow 1, green from 1 s to the end of the second phase's green (26 + 1 + 8), yellow 3, then red.
            Assert.AreEqual("3/1 6/34 9/3 0/0", string.Join(" ", program.Lights[0].Phases.Select(phase => FormattableString.Invariant($"{phase.State}/{phase.Seconds}"))));
            var found = OmsiSignalPhasePlan.Detect(program);
            Assert.IsNotNull(found);
            CollectionAssert.AreEqual(new[] { 0, 1 }, found.Phases[1].Lights);
        }

        [Test]
        public void TheSimplifiedViewCutsTheCycleWhereTheGroupsThatGoChange()
        {
            var program = new OmsiSignalProgram { CycleSeconds = 60 };
            program.AddLight("A");
            program.AddLight("B");
            OmsiSignalTimeline.Paint(program, 0, 0, 25, 6);
            OmsiSignalTimeline.Paint(program, 0, 25, 28, 9);
            OmsiSignalTimeline.Paint(program, 1, 20, 50, 6);

            var windows = OmsiSignalPhasePlan.Windows(program);

            CollectionAssert.AreEqual(new[] { 0.0, 20, 25, 50 }, windows.Select(window => window.From).ToArray());
            CollectionAssert.AreEqual(new[] { 0 }, windows[0].Lights);
            CollectionAssert.AreEqual(new[] { 0, 1 }, windows[1].Lights);
            CollectionAssert.AreEqual(new[] { 1 }, windows[2].Lights);
            CollectionAssert.IsEmpty(windows[3].Lights);
            Assert.AreEqual(60, windows[3].To, 1e-9);
        }

        [Test]
        public void ProgramsThatAreNoPhasePlanAreRecognised()
        {
            // Yellow of differing lengths.
            var program = new OmsiSignalProgram { CycleSeconds = 60 };
            program.AddLight("A");
            program.AddLight("B");
            OmsiSignalTimeline.Paint(program, 0, 0, 20, 6);
            OmsiSignalTimeline.Paint(program, 0, 20, 23, 9);
            OmsiSignalTimeline.Paint(program, 1, 30, 50, 6);
            OmsiSignalTimeline.Paint(program, 1, 50, 54, 9);
            Assert.IsNull(OmsiSignalPhasePlan.Detect(program));

            // OMSI's other red (1) would become 0: not a plan, so nothing is lost.
            var other = new OmsiSignalProgram { CycleSeconds = 30 };
            other.Lights.Add(new OmsiSignalLight { Name = "A" });
            other.Lights[0].Phases.AddRange(new[] { new OmsiSignalPhase(3, 1), new OmsiSignalPhase(6, 20), new OmsiSignalPhase(9, 3), new OmsiSignalPhase(1, 0) });
            Assert.IsNull(OmsiSignalPhasePlan.Detect(other));
            other.Lights[0].Phases[3] = new OmsiSignalPhase(0, 0);
            Assert.IsNotNull(OmsiSignalPhasePlan.Detect(other));
        }
    }
}
