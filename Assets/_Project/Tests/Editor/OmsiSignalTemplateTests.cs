using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Signal templates (ADR 085, milestone 6; §12 and cases 9 and 12 of the brief) on a crossroads the generator makes.</summary>
    public sealed class OmsiSignalTemplateTests
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

        private static OmsiSignalIntersection Crossing(int arms)
        {
            var profile = Profile();
            var list = Enumerable.Range(0, arms).Select(index => new OmsiJunctionArm { HeadingDegrees = index * 360.0 / arms, ProfilePath = ProfilePath, Profile = profile }).ToList();
            var made = OmsiJunctionGenerator.Junction(list, null, "Template");
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            return OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(bytes), OmsiSignalProgram.Read(bytes));
        }

        private static void AssertSameProgram(OmsiSignalProgram expected, OmsiSignalProgram actual)
        {
            Assert.AreEqual(expected.CycleLength, actual.CycleLength, 1e-9);
            Assert.AreEqual(expected.Lights.Count, actual.Lights.Count);
            for (var light = 0; light < expected.Lights.Count; light++)
            {
                Assert.AreEqual(expected.Lights[light].Name, actual.Lights[light].Name);
                CollectionAssert.AreEqual(expected.Lights[light].Phases.Select(phase => (phase.State, phase.Seconds)),
                    actual.Lights[light].Phases.Select(phase => (phase.State, phase.Seconds)));
            }
            CollectionAssert.AreEqual(expected.Holds.Select(hold => (hold.Light, hold.Seconds, hold.IfNoRequest, hold.JumpTo)),
                actual.Holds.Select(hold => (hold.Light, hold.Seconds, hold.IfNoRequest, hold.JumpTo)));
        }

        [Test]
        public void EveryBuiltInTemplateIsValidAndSurvivesItsText()
        {
            Assert.AreEqual(5, OmsiSignalTemplates.BuiltIn.Count);
            foreach (var template in OmsiSignalTemplates.BuiltIn)
            {
                Assert.DoesNotThrow(template.Validate, template.Id);
                var again = OmsiSignalTemplate.FromText(template.ToText());
                AssertSameProgram(template.Program, again.Program);
                Assert.AreEqual(template.Roles.Count, again.Roles.Count, template.Id);
                for (var light = 0; light < template.Roles.Count; light++)
                    CollectionAssert.AreEqual(template.Roles[light], again.Roles[light], template.Id);
            }
        }

        [Test]
        public void TheTwoPhaseTemplateFitsACrossroadsAndHasNoHardConflict()
        {
            var crossing = Crossing(4);
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.TwoPhase);
            var match = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>());

            Assert.IsTrue(match.Fits, string.Join(",", match.Unlit) + " / " + string.Join(",", match.EmptyLights));
            Assert.IsEmpty(match.Contested);
            Assert.AreEqual(2, template.Program.Lights.Count);
            // Arms 1 and 3 share light 0, arms 2 and 4 light 1.
            foreach (var movement in crossing.Movements.Where(movement => movement.Approach >= 0 && movement.Turn != OmsiMovementTurn.Pedestrian))
                Assert.AreEqual(movement.Approach % 2, match.MovementLights[movement.Index], "movement " + movement.Index);
            // The program is the assistant's kind: conflicts across arms never go together.
            var scenery = OmsiSceneryObject.Parse(OmsiTextFile.Encode(OmsiJunctionGenerator.Junction(
                Enumerable.Range(0, 4).Select(i => new OmsiJunctionArm { HeadingDegrees = i * 90.0, ProfilePath = ProfilePath, Profile = Profile() }).ToList(), null, "Template").Sco,
                OmsiTextEncoding.Windows1252));
            var conflicts = OmsiSignalConflicts.Of(scenery, crossing);
            var findings = OmsiSignalCheck.Run(template.Program, crossing, conflicts, match.MovementLights, null, null);
            CollectionAssert.IsEmpty(findings.Where(finding => finding.Level == OmsiSignalIssueLevel.Error).Select(finding => finding.Kind + " " + finding.First + " " + finding.Second));
        }

        [Test]
        public void AThreeArmCrossingMissesTheArmsAFourArmTemplateNeeds()
        {
            var crossing = Crossing(3);
            var match = OmsiSignalTemplateMatch.Match(OmsiSignalTemplates.Find(OmsiSignalTemplates.FourArms), crossing, new HashSet<int>());

            CollectionAssert.AreEqual(new[] { 3 }, match.MissingArms);
            Assert.IsFalse(match.Fits);
            Assert.AreEqual(1, match.EmptyLights.Count, "the light of the fourth arm has no movement");
            Assert.IsEmpty(match.Unlit, "every movement of the three arms has its light");
        }

        [Test]
        public void ATemplateForThreeArmsLeavesTheMovementsOfAFourthUnlit()
        {
            var crossing = Crossing(4);
            var match = OmsiSignalTemplateMatch.Match(OmsiSignalTemplates.Find(OmsiSignalTemplates.TJunction), crossing, new HashSet<int>());

            Assert.IsFalse(match.Fits);
            Assert.IsNotEmpty(match.Unlit);
            Assert.IsTrue(match.Unlit.All(index => crossing.Movements[index].Approach == 3));
        }

        [Test]
        public void ACorrectionBeforeApplyingCoversWhatDoesNotFit()
        {
            var crossing = Crossing(4);
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.TJunction);
            var unlit = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>()).Unlit;
            var overrides = unlit.ToDictionary(index => index, _ => 0);
            var corrected = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), overrides);

            Assert.IsEmpty(corrected.Unlit);
            Assert.IsTrue(unlit.All(index => corrected.MovementLights[index] == 0));
            // "No light" is a choice too.
            var none = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), unlit.ToDictionary(index => index, _ => -1));
            CollectionAssert.AreEquivalent(unlit, none.Unlit);
        }

        [Test]
        public void SeparateLeftLightsNeedLeftTurnLanesOfTheirOwn()
        {
            var crossing = Crossing(4);
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.SeparateLeft);
            Assert.AreEqual(4, template.Program.Lights.Count);
            var without = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>());
            // Without left-turn lanes the left turns share the lane of the others: the main lights have them, the left lights nothing.
            CollectionAssert.AreEqual(new[] { 1, 3 }, without.EmptyLights);

            var lefts = crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Left && movement.Approach >= 0).Select(movement => movement.Index).ToList();
            var with = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(lefts));
            Assert.IsTrue(with.Fits, string.Join(",", with.Unlit) + " / " + string.Join(",", with.EmptyLights));
            foreach (var index in lefts)
                Assert.IsTrue(with.MovementLights[index] == 1 || with.MovementLights[index] == 3, "left turn " + index);
        }

        [Test]
        public void ATemplateMadeFromACrossingGivesTheSameLightsBack()
        {
            var crossing = Crossing(4);
            var source = OmsiSignalTemplates.Find(OmsiSignalTemplates.FourArms);
            var first = OmsiSignalTemplateMatch.Match(source, crossing, new HashSet<int>());
            foreach (var movement in crossing.Movements)
                movement.Light = first.MovementLights[movement.Index];

            var (saved, imprecise) = OmsiSignalTemplate.FromCrossing("Mine", "four", source.Program, crossing, new HashSet<int>());
            Assert.AreEqual(0, imprecise);
            Assert.AreEqual("Mine", saved.Name);
            Assert.AreEqual(4, saved.ArmsNeeded);
            Assert.IsTrue(saved.Roles.All(roles => roles.All(role => role.Lane == OmsiSignalRoleLane.Any)));

            var again = OmsiSignalTemplateMatch.Match(OmsiSignalTemplate.FromText(saved.ToText()), crossing, new HashSet<int>());
            CollectionAssert.AreEqual(first.MovementLights, again.MovementLights);
            Assert.IsTrue(again.Fits);
        }

        [Test]
        public void ARoleThatCannotTellMovementsApartIsCounted()
        {
            // An arm and turn with two movements (several lanes) that obey different lights although none starts in a lane of its own.
            foreach (var arms in new[] { 5, 6, 7 })
            {
                var crossing = Crossing(arms);
                var pair = crossing.Movements.Where(movement => movement.Approach >= 0 && movement.Turn != OmsiMovementTurn.Pedestrian)
                    .GroupBy(movement => (movement.Approach, movement.Turn)).FirstOrDefault(group => group.Count() >= 2);
                if (pair == null)
                    continue;
                foreach (var movement in crossing.Movements)
                    movement.Light = movement.Approach;
                pair.First().Light = arms;
                var program = new OmsiSignalProgram { CycleSeconds = 60 };
                for (var light = 0; light <= arms; light++)
                    program.AddLight("L" + light);
                var (template, imprecise) = OmsiSignalTemplate.FromCrossing("X", null, program, crossing, new HashSet<int>());
                Assert.Greater(imprecise, 0);
                Assert.AreEqual(arms + 1, template.Roles.Count);
                return;
            }
            Assert.Inconclusive("no crossing with several lanes for one arm and turn");
        }

        [Test]
        public void TheRequestTemplateStaysGreenUntilSomeoneAsks()
        {
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.PedestrianRequest);
            var clock = new OmsiSignalClock(template.Program);
            for (var step = 0; step < 600; step++)
                clock.Advance(0.1);
            Assert.IsTrue(clock.Held);
            Assert.AreEqual(OmsiSignalAspects.StateOf(OmsiSignalAspect.Green), clock.StateOf(0));
            clock.Requests[0] = true;
            for (var step = 0; step < 110; step++)
                clock.Advance(0.1);
            Assert.AreEqual(OmsiSignalAspects.StateOf(OmsiSignalAspect.Yellow), clock.StateOf(0), "after the request the traffic gets yellow, time " + clock.Time);
            for (var step = 0; step < 40; step++)
                clock.Advance(0.1);
            Assert.AreEqual(OmsiSignalAspects.StateOf(OmsiSignalAspect.Red), clock.StateOf(0), "then red for the people to cross");
            clock.Requests[0] = false;
            for (var step = 0; step < 120; step++)
                clock.Advance(0.1);
            Assert.AreEqual(OmsiSignalAspects.StateOf(OmsiSignalAspect.Green), clock.StateOf(0), "green again after the red");
            for (var step = 0; step < 300; step++)
                clock.Advance(0.1);
            Assert.IsTrue(clock.Held, "held at the stop point until the next request");
            Assert.AreEqual(OmsiSignalAspects.StateOf(OmsiSignalAspect.Green), clock.StateOf(0));
        }

        [Test]
        public void ATemplateAppliedToAJunctionIsWrittenAndReadBackAsItWas()
        {
            // Brief case 9 (change groups and export again) with a template: the .sco holds the program and the bindings.
            var profile = Profile();
            var arms = Enumerable.Range(0, 4).Select(i => new OmsiJunctionArm { HeadingDegrees = i * 90.0, ProfilePath = ProfilePath, Profile = profile }).ToList();
            var plain = OmsiJunctionGenerator.Junction(arms, null, "Template");
            var scenery = OmsiSceneryObject.Parse(OmsiTextFile.Encode(plain.Sco, OmsiTextEncoding.Windows1252));
            var crossing = OmsiSignalIntersection.Analyse(scenery, OmsiSignalProgram.Read(OmsiTextFile.Encode(plain.Sco, OmsiTextEncoding.Windows1252)));
            var layout = OmsiJunctionLayout.Build(arms, null, null);
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.TwoPhase);
            var match = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>());

            var signals = new OmsiJunctionSignals();
            signals.SetProgram(template.Program);
            foreach (var movement in crossing.Movements.Where(movement => match.MovementLights[movement.Index] >= 0))
            {
                var path = movement.Paths.Select(index => layout.Paths[index]).First(candidate => candidate.StartsWay);
                signals.WayLights[path.WayThrough] = match.MovementLights[movement.Index];
            }
            var made = OmsiJunctionGenerator.Junction(arms, null, "Template", signals: signals);
            var read = OmsiJunctionSignals.Read(OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252));

            Assert.NotNull(read);
            AssertSameProgram(template.Program, read.Program);
            CollectionAssert.AreEquivalent(signals.WayLights, read.WayLights);
        }

        // ---- files ----------------------------------------------------------------------------------------------------------

        private static string Folder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "mapworks-templates-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Test]
        public void ATemplateIsSavedLoadedAndDeleted()
        {
            var folder = Folder();
            try
            {
                var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.TwoPhase).Copy();
                template.Id = null;
                template.Name = "My \"two\" phases / test";
                template.Description = "Line one\nline two";
                var path = OmsiSignalTemplates.Save(folder, template);
                StringAssert.EndsWith(".json", path);
                Assert.IsTrue(OmsiSignalTemplates.Exists(folder, template.Name));
                Assert.IsFalse(File.Exists(path + ".tmp"));

                var (loaded, problems) = OmsiSignalTemplates.Load(folder);
                Assert.IsEmpty(problems);
                Assert.AreEqual(1, loaded.Count);
                Assert.AreEqual(template.Name, loaded[0].Name);
                Assert.AreEqual(template.Description, loaded[0].Description);
                Assert.IsFalse(loaded[0].BuiltIn);
                AssertSameProgram(template.Program, loaded[0].Program);

                OmsiSignalTemplates.Delete(folder, template.Name);
                Assert.IsEmpty(OmsiSignalTemplates.Load(folder).Templates);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void AFileThatIsNoTemplateIsReportedAndTheOthersStillLoad()
        {
            var folder = Folder();
            try
            {
                var good = OmsiSignalTemplates.Find(OmsiSignalTemplates.FourArms).Copy();
                good.Id = null;
                good.Name = "Good";
                OmsiSignalTemplates.Save(folder, good);
                File.WriteAllText(Path.Combine(folder, "broken.json"), "{ \"formatVersion\": 1, \"lights\": [", Encoding.UTF8);
                File.WriteAllText(Path.Combine(folder, "future.json"), "{ \"formatVersion\": 9, \"lights\": [] }", Encoding.UTF8);
                File.WriteAllText(Path.Combine(folder, "empty.json"), "{ \"formatVersion\": 1, \"lights\": [] }", Encoding.UTF8);

                var (loaded, problems) = OmsiSignalTemplates.Load(folder);
                CollectionAssert.AreEqual(new[] { "Good" }, loaded.Select(template => template.Name));
                CollectionAssert.AreEquivalent(new[] { "broken.json", "future.json", "empty.json" }, problems.Select(problem => problem.File));
                Assert.IsTrue(problems.All(problem => problem.Problem.Length > 0));
                Assert.IsEmpty(OmsiSignalTemplates.Load(Path.Combine(folder, "missing")).Templates, "a folder that does not exist holds none");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void ATemplateWithTooFewListsOfMovementsOrNoNameIsRefused()
        {
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.TwoPhase).Copy();
            template.Roles.RemoveAt(1);
            Assert.Throws<InvalidDataException>(template.Validate);
            Assert.Throws<InvalidDataException>(() => OmsiSignalTemplate.FromText(OmsiSignalTemplates.Find(OmsiSignalTemplates.TwoPhase).ToText().Replace("\"arm\": 1", "\"arm\": -1")));
            var nameless = OmsiSignalTemplates.Find(OmsiSignalTemplates.TwoPhase).Copy();
            nameless.Name = "///";
            Assert.AreEqual("___", OmsiSignalTemplates.FileNameOf(nameless.Name));
            nameless.Name = "   ";
            Assert.Throws<ArgumentException>(() => OmsiSignalTemplates.Save(Path.GetTempPath(), nameless));
        }
    }
}
