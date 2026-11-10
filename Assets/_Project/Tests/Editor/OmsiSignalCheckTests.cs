using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The conflict matrix and the signal check (ADR 085, milestone 5) on a crossroads the generator makes.</summary>
    public sealed class OmsiSignalCheckTests
    {
        private const string ProfilePath = @"Splines\Test\road.sli";
        private const string L = "\r\n";

        private static OmsiSplineProfile Profile()
        {
            var text = "[texture]" + L + "side.bmp" + L + "[texture]" + L + "asphalt.bmp" + L +
                       "[profile]" + L + "0" + L + "[profilepnt]" + L + "-7" + L + "0.25" + L + "0" + L + "0.2" + L + "[profilepnt]" + L + "-4" + L + "0.25" + L + "1" + L + "0.2" + L +
                       "[profile]" + L + "0" + L + "[profilepnt]" + L + "-4" + L + "0.25" + L + "0" + L + "0.2" + L + "[profilepnt]" + L + "-4" + L + "0.1" + L + "1" + L + "0.2" + L +
                       "[profile]" + L + "1" + L + "[profilepnt]" + L + "-4" + L + "0.1" + L + "0" + L + "0.1" + L + "[profilepnt]" + L + "4" + L + "0.1" + L + "1" + L + "0.1" + L +
                       "[profile]" + L + "0" + L + "[profilepnt]" + L + "4" + L + "0.1" + L + "0" + L + "0.2" + L + "[profilepnt]" + L + "4" + L + "0.25" + L + "1" + L + "0.2" + L +
                       "[profile]" + L + "0" + L + "[profilepnt]" + L + "4" + L + "0.25" + L + "0" + L + "0.2" + L + "[profilepnt]" + L + "7" + L + "0.25" + L + "1" + L + "0.2" + L +
                       "[path]" + L + "0" + L + "-2" + L + "0.1" + L + "3.6" + L + "1" + L + "[path]" + L + "0" + L + "2" + L + "0.1" + L + "3.6" + L + "0" + L +
                       "[path]" + L + "1" + L + "-5.5" + L + "0.25" + L + "1" + L + "2" + L + "[path]" + L + "1" + L + "5.5" + L + "0.25" + L + "1" + L + "2" + L;
            return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(text), ProfilePath);
        }

        private static List<OmsiJunctionArm> Crossroads()
        {
            var profile = Profile();
            return new[] { 0.0, 90.0, 180.0, 270.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList();
        }

        private sealed class Made
        {
            public OmsiSignalIntersection Crossing;
            public List<OmsiSignalConflict> Conflicts;
            public OmsiSignalProposal Proposal;
        }

        private static Made Make()
        {
            var made = OmsiJunctionGenerator.Junction(Crossroads(), null, "Check");
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var scenery = OmsiSceneryObject.Parse(bytes);
            var crossing = OmsiSignalIntersection.Analyse(scenery, OmsiSignalProgram.Read(bytes));
            var conflicts = OmsiSignalConflicts.Of(scenery, crossing);
            return new Made { Crossing = crossing, Conflicts = conflicts, Proposal = OmsiSignalWizard.Propose(crossing, conflicts, null) };
        }

        /// <summary>One group per approach, every movement in the group of its approach.</summary>
        private static int[] ByApproach(OmsiSignalIntersection crossing)
        {
            return crossing.Movements.Select(movement => movement.Turn == OmsiMovementTurn.Pedestrian ? -1 : movement.Approach).ToArray();
        }

        private static OmsiSignalProgram AllGreen(int lights)
        {
            var program = new OmsiSignalProgram { CycleSeconds = 60 };
            for (var light = 0; light < lights; light++)
            {
                program.AddLight("G" + light);
                OmsiSignalTimeline.Paint(program, light, 0, 60, 6);
            }
            return program;
        }

        [Test]
        public void TheMatrixTellsOpposingFromCrossingApproaches()
        {
            var made = Make();
            var lights = ByApproach(made.Crossing);
            var matrix = OmsiSignalMatrix.Build(4, made.Conflicts, lights);

            for (var first = 0; first < 4; first++)
            for (var second = 0; second < 4; second++)
            {
                if (first == second)
                    continue;
                var opposite = OmsiSignalConflicts.Opposite(made.Crossing, first, second);
                // Opposing approaches meet only where one turns and gives way; neighbouring ones cross for good.
                Assert.AreEqual(opposite ? OmsiGroupRelation.Conditional : OmsiGroupRelation.Incompatible, matrix[first, second], first + " " + second);
                Assert.AreSame(matrix.Conflicts[first, second], matrix.Conflicts[second, first]);
            }
            Assert.AreEqual(OmsiGroupRelation.NotChecked, OmsiSignalMatrix.Build(5, made.Conflicts, lights)[0, 4], "a group without movements");
        }

        [Test]
        public void IncompatibleGroupsGreenTogetherAreAnError()
        {
            // Brief case 10: invalid simultaneous greens are found.
            var made = Make();
            var lights = ByApproach(made.Crossing);
            var findings = OmsiSignalCheck.Run(AllGreen(4), made.Crossing, made.Conflicts, lights, null, null);

            var errors = findings.Where(finding => finding.Level == OmsiSignalIssueLevel.Error && finding.Kind == OmsiSignalFindingKind.ConflictingGreen).ToList();
            Assert.IsNotEmpty(errors);
            Assert.IsTrue(errors.All(error => !OmsiSignalConflicts.Opposite(made.Crossing, error.First, error.Second)));
            Assert.IsTrue(errors.All(error => error.Conflict != null && error.Conflict.Kind == OmsiConflictKind.Crossing));
            // Opposing approaches green together: only a hint, their turning traffic gives way.
            Assert.IsTrue(findings.Any(finding => finding.Kind == OmsiSignalFindingKind.ConditionalGreen && finding.Level == OmsiSignalIssueLevel.Hint));
        }

        [Test]
        public void TheAssistantsProgramHasNoErrorsOrWarnings()
        {
            var made = Make();
            var findings = OmsiSignalCheck.Run(made.Proposal.Program, made.Crossing, made.Conflicts, made.Proposal.MovementLights, null, null);

            CollectionAssert.IsEmpty(findings.Where(finding => finding.Level != OmsiSignalIssueLevel.Hint).Select(finding => finding.Kind + " " + finding.First + " " + finding.Second));
        }

        [Test]
        public void AManualExceptionTurnsTheErrorIntoAWarningAndNamesItself()
        {
            var made = Make();
            var lights = ByApproach(made.Crossing);
            var program = AllGreen(2);
            var two = lights.Select(light => light == 0 || light == 1 ? light : -1).ToArray();
            Assert.IsFalse(OmsiSignalConflicts.Opposite(made.Crossing, 0, 1));

            var plain = OmsiSignalCheck.Run(program, made.Crossing, made.Conflicts, two, null, null);
            var excepted = OmsiSignalCheck.Run(program, made.Crossing, made.Conflicts, two, new HashSet<(int, int)> { (0, 1) }, null);

            Assert.IsTrue(plain.Any(finding => finding.Level == OmsiSignalIssueLevel.Error));
            Assert.IsFalse(excepted.Any(finding => finding.Level == OmsiSignalIssueLevel.Error));
            Assert.IsTrue(excepted.Any(finding => finding.Kind == OmsiSignalFindingKind.AllowedByException && finding.Level == OmsiSignalIssueLevel.Warning));
        }

        [Test]
        public void TooLittleTimeBetweenIncompatibleGreensIsAWarning()
        {
            var made = Make();
            var plan = made.Proposal.Plan.Copy();
            plan.ClearanceSeconds = 0;
            plan.RedYellowSeconds = 0;
            plan.YellowSeconds = 2;
            var program = made.Proposal.Program.Copy();
            plan.WriteTo(program);

            var findings = OmsiSignalCheck.Run(program, made.Crossing, made.Conflicts, made.Proposal.MovementLights, null, null);

            var short_ = findings.Where(finding => finding.Kind == OmsiSignalFindingKind.ShortIntergreen).ToList();
            Assert.IsNotEmpty(short_);
            Assert.AreEqual(2.0, short_[0].Amount, 0.11);
        }

        [Test]
        public void HeadsAreChecked()
        {
            var made = Make();
            var program = made.Proposal.Program;
            var lights = made.Proposal.MovementLights;

            var none = OmsiSignalCheck.Run(program, made.Crossing, made.Conflicts, lights, null, new List<int?>());
            Assert.IsTrue(none.Any(finding => finding.Kind == OmsiSignalFindingKind.NoHeads));

            var some = OmsiSignalCheck.Run(program, made.Crossing, made.Conflicts, lights, null, new List<int?> { 0, 5 });
            Assert.IsTrue(some.Any(finding => finding.Kind == OmsiSignalFindingKind.HeadWithoutGroup && finding.First == 5 && finding.Level == OmsiSignalIssueLevel.Error));
            Assert.IsTrue(some.Any(finding => finding.Kind == OmsiSignalFindingKind.GroupWithoutHead && finding.First == 1));
            Assert.IsFalse(some.Any(finding => finding.Kind == OmsiSignalFindingKind.GroupWithoutHead && finding.First == 0));
        }

        [Test]
        public void ExceptionsAreKeptThroughARebuildAndFollowRemovedGroups()
        {
            var signals = new OmsiJunctionSignals();
            var program = new OmsiSignalProgram { CycleSeconds = 60 };
            program.AddLight("A");
            program.AddLight("B");
            program.AddLight("C");
            signals.SetProgram(program);
            signals.Exceptions.Add(OmsiJunctionSignals.Pair(2, 0));
            signals.Exceptions.Add((1, 2));

            var made = OmsiJunctionGenerator.Junction(Crossroads(), null, "Exceptions", signals: signals);
            StringAssert.Contains("MapWorks signal exceptions: 0-2 1-2", made.Sco);
            var read = OmsiJunctionSignals.Read(OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252));
            CollectionAssert.AreEquivalent(new[] { (0, 2), (1, 2) }, read.Exceptions);

            // Removing group B (1): C becomes 1, the exception with B goes.
            read.RemapExceptions(new[] { 0, -1, 1 });
            CollectionAssert.AreEquivalent(new[] { (0, 1) }, read.Exceptions);
            Assert.IsEmpty(new OmsiJunctionSignals().Exceptions, "a new set of signals (the assistant's) has none");
        }
    }
}
