using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>A traffic light at the crossing piece (ADR 086, step 5): vehicle ways to bind and the template "Crossing on request".</summary>
    public sealed class OmsiCrossingPieceSignalTests
    {
        private const string ProfilePath = @"Splines\Test\road.sli";
        private const string Texture = @"Splines\Test\texture\plain.bmp";

        // 14 m wide: pavements 3 m wide, an 8 m carriageway, a lane each way and a footpath on each pavement used both ways.
        private const string Profile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-7\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.1\r\n1\r\n0.2\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.1\r\n0\r\n0.2\r\n[profilepnt]\r\n4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n7\r\n0.25\r\n1\r\n0.2\r\n" +
            "[path]\r\n1\r\n-5.5\r\n0.25\r\n1\r\n2\r\n[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n[path]\r\n1\r\n5.5\r\n0.25\r\n1\r\n2\r\n";

        private static List<OmsiJunctionArm> PieceArms(bool crossing)
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            return new[] { 90.0, 270.0 }.Select(heading => new OmsiJunctionArm
                { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile, Crossing = crossing && heading < 100 ? new OmsiCrossing() : null }).ToList();
        }

        /// <summary>The way a movement is bound by, as the signals tool finds it: that of the first path starting a way.</summary>
        private static string WayOf(OmsiJunctionLayout layout, OmsiSignalMovement movement)
        {
            foreach (var index in movement.Paths)
                if (index < layout.Paths.Count && layout.Paths[index].StartsWay)
                    return layout.Paths[index].WayThrough;
            return null;
        }

        [Test]
        public void EachLaneOfACrossingPieceIsAWayToBindThatStartsAtTheEndOfThePieceBeforeTheCrossing()
        {
            var piece = OmsiJunctionLayout.Build(PieceArms(true), Texture);

            Assert.IsTrue(piece.IsTransition);
            // One lane each way: from arm 0 lane 1 to arm 1 lane 2 and back (arm 1 sees the profile turned over).
            Assert.AreEqual(2, piece.WaysThrough.Count);
            var vehicles = piece.Paths.Where(path => path.Kind == 0).ToList();
            Assert.AreEqual(2, vehicles.Count);
            foreach (var path in vehicles)
            {
                Assert.IsTrue(path.StartsWay);
                CollectionAssert.Contains(piece.WaysThrough, path.WayThrough);
                // A vehicle stops at the start, 5.5 m from the middle: in front of the 3 m wide crossing.
                Assert.AreEqual(5.5, Math.Abs(path.X), 1e-6);
                Assert.Greater(Math.Abs(path.X), 1.5 + OmsiJunctionLayout.StopLineGap);
            }
            CollectionAssert.AreEquivalent(new[] { OmsiCrossing.WayKey(0) }, piece.CrossingWays);
            // The lanes tool knows no ways of a piece.
            CollectionAssert.IsEmpty(piece.LaneEnds());
        }

        [Test]
        public void APieceWithoutACrossingHasNoWaysAndAPieceWithoutLightsIsWrittenWithoutAny()
        {
            var plain = OmsiJunctionLayout.Build(PieceArms(false), Texture);
            CollectionAssert.IsEmpty(plain.WaysThrough);
            Assert.IsTrue(plain.Paths.All(path => path.WayThrough == null && !path.StartsWay));

            // The paths of a crossing piece are the same with or without its ways; only the signals tool uses the keys.
            var piece = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            var again = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            Assert.AreEqual(piece.Paths.Count, again.Paths.Count);
            var made = OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece");
            StringAssert.DoesNotContain("use_traffic_light", made.Sco);
            StringAssert.DoesNotContain("MapWorks signals", made.Sco);
            StringAssert.DoesNotContain("traffic_lights_group", made.Sco);
            Assert.AreEqual(made.Sco, OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece").Sco);
        }

        [Test]
        public void TheRequestTemplateFitsACrossingPieceWithAVehicleAndAPedestrianGroup()
        {
            var layout = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            var plain = OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece");
            var bytes = OmsiTextFile.Encode(plain.Sco, OmsiTextEncoding.Windows1252);
            var scenery = OmsiSceneryObject.Parse(bytes);
            var crossing = OmsiSignalIntersection.Analyse(scenery, OmsiSignalProgram.Read(bytes));
            Assert.AreEqual(2, crossing.Approaches.Count);
            var footpaths = new HashSet<int>(crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && WayOf(layout, movement) != null)
                .Select(movement => movement.Index));
            Assert.AreEqual(1, footpaths.Count, "only the footpath across the road can be bound");

            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.PedestrianRequest);
            var match = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), null, footpaths);

            Assert.IsTrue(match.Fits, string.Join(",", match.Unlit) + " / " + string.Join(",", match.EmptyLights));
            foreach (var movement in crossing.Movements)
            {
                var expected = movement.Turn != OmsiMovementTurn.Pedestrian ? 0 : footpaths.Contains(movement.Index) ? 1 : -1;
                Assert.AreEqual(expected, match.MovementLights[movement.Index], "movement " + movement.Index + " " + movement.Turn);
            }
            // Without footpaths to bind (a junction not made here), the pedestrian group stays idle and the template fits as before.
            var without = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>());
            Assert.IsTrue(without.Fits);
            CollectionAssert.IsEmpty(without.EmptyLights);
            CollectionAssert.AreEqual(new[] { 1 }, without.IdleLights);
            Assert.IsTrue(crossing.Movements.All(movement => without.MovementLights[movement.Index] == (movement.Turn == OmsiMovementTurn.Pedestrian ? -1 : 0)));
            CollectionAssert.AreEqual(without.MovementLights, OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), null, new HashSet<int>()).MovementLights);
        }

        [Test]
        public void WithoutFootpathsToBindTheRequestTemplateHoldsAtTheTrafficLightAsBefore()
        {
            var plain = OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece");
            var bytes = OmsiTextFile.Encode(plain.Sco, OmsiTextEncoding.Windows1252);
            var crossing = OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(bytes), OmsiSignalProgram.Read(bytes));
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.PedestrianRequest);

            var without = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), null, new HashSet<int>());
            var program = template.Program.Copy();
            without.AdaptHolds(program);
            Assert.AreEqual(1, program.Holds.Count);
            Assert.AreEqual(0, program.Holds[0].Light, "the stop point waits where vehicles ask, not at a light nobody obeys");
            Assert.AreEqual(1, template.Program.Holds[0].Light, "the template itself stays as it is");

            // With the footpath bound, the people's button holds the clock.
            var layout = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            var footpaths = new HashSet<int>(crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && WayOf(layout, movement) != null)
                .Select(movement => movement.Index));
            var bound = template.Program.Copy();
            OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), null, footpaths).AdaptHolds(bound);
            Assert.AreEqual(1, bound.Holds[0].Light);
        }

        [Test]
        public void TheUsersChoiceComesBeforeTheFootpathsGroup()
        {
            var layout = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            var bytes = OmsiTextFile.Encode(OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece").Sco, OmsiTextEncoding.Windows1252);
            var crossing = OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(bytes), OmsiSignalProgram.Read(bytes));
            var footpaths = new HashSet<int>(crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && WayOf(layout, movement) != null)
                .Select(movement => movement.Index));
            var foot = footpaths.Single();
            var other = crossing.Movements.First(movement => movement.Turn == OmsiMovementTurn.Pedestrian && !footpaths.Contains(movement.Index)).Index;
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.PedestrianRequest);

            var none = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), new Dictionary<int, int> { { foot, -1 }, { other, 0 } }, footpaths);
            Assert.AreEqual(-1, none.MovementLights[foot], "a footpath the user took off the light stays dark");
            Assert.AreEqual(-1, none.MovementLights[other], "a footpath that cannot be bound gets no light, whatever is chosen");
            CollectionAssert.AreEqual(new[] { 1 }, none.EmptyLights);
            var traffic = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), new Dictionary<int, int> { { foot, 0 } }, footpaths);
            Assert.AreEqual(0, traffic.MovementLights[foot]);
        }

        [Test]
        public void TheCrossingToolLeavesAPieceWithoutLightsAsItWas()
        {
            // The crossing tool passes the layout's ways as defaults; a piece has no chosen connections, so none are written,
            // and the lanes of a piece do not depend on them.
            var piece = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            var chosen = OmsiJunctionLayout.Build(PieceArms(true), Texture, piece.WaysThrough);
            Assert.AreEqual(piece.Paths.Count, chosen.Paths.Count);
            var made = OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece");
            Assert.AreEqual(OmsiLaneConnections.Read(made.Sco), null);
            var removed = OmsiJunctionGenerator.Junction(PieceArms(false), Texture, "Crossing piece");
            Assert.AreEqual(OmsiLaneConnections.Read(removed.Sco), null);
        }

        [Test]
        public void ACrossingPieceWithTheRequestTemplateIsWrittenBoundAndReadBack()
        {
            var layout = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            var plain = OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece");
            var plainBytes = OmsiTextFile.Encode(plain.Sco, OmsiTextEncoding.Windows1252);
            var crossing = OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(plainBytes), OmsiSignalProgram.Read(plainBytes));
            var footpaths = new HashSet<int>(crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && WayOf(layout, movement) != null)
                .Select(movement => movement.Index));
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.PedestrianRequest);
            var match = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), null, footpaths);

            // As the signals tool applies a template: the program, and each movement's way bound to its light.
            var signals = new OmsiJunctionSignals();
            signals.SetProgram(template.Program);
            foreach (var movement in crossing.Movements.Where(movement => match.MovementLights[movement.Index] >= 0))
                signals.WayLights[WayOf(layout, movement)] = match.MovementLights[movement.Index];
            var made = OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece", signals: signals);

            CollectionAssert.IsEmpty(made.LostSignalWays);
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var program = OmsiSignalProgram.Read(bytes);
            Assert.AreEqual(2, program.Lights.Count);
            Assert.AreEqual(1, program.Holds.Count);
            Assert.AreEqual(1, program.Holds[0].Light, "the people's button holds the clock, not the passing traffic");
            Assert.IsTrue(program.Holds[0].IfNoRequest);
            for (var path = 0; path < layout.Paths.Count; path++)
            {
                var each = layout.Paths[path];
                var expected = each.Kind == 0 ? 0 : each.WayThrough == OmsiCrossing.WayKey(0) ? 1 : -1;
                Assert.AreEqual(expected, program.PathLights[path], "path " + path);
            }
            var read = OmsiJunctionSignals.Read(bytes);
            Assert.NotNull(read);
            CollectionAssert.AreEquivalent(signals.WayLights, read.WayLights);
            // The paths are those of the piece without lights: the same count and places, with [use_traffic_light] after three of them.
            Assert.AreEqual(OmsiSceneryObject.Parse(plainBytes).Paths.Count, OmsiSceneryObject.Parse(bytes).Paths.Count);

            // Analysed again: the traffic of both ways obeys light 0, the crossing light 1, and the check finds no error.
            var again = OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(bytes), program);
            Assert.IsTrue(again.Movements.Where(movement => movement.Turn != OmsiMovementTurn.Pedestrian).All(movement => movement.Light == 0));
            Assert.AreEqual(1, again.Movements.Count(movement => movement.Turn == OmsiMovementTurn.Pedestrian && movement.Light == 1));
            var conflicts = OmsiSignalConflicts.Of(OmsiSceneryObject.Parse(bytes), again);
            var lights = again.Movements.Select(movement => movement.Light).ToList();
            var findings = OmsiSignalCheck.Run(program, again, conflicts, lights, null, null, new[] { 1 });
            CollectionAssert.IsEmpty(findings.Where(finding => finding.Level == OmsiSignalIssueLevel.Error).Select(finding => finding.Kind + " " + finding.First + " " + finding.Second));
            // The people's green lasts to the far side, and the traffic waits long enough after it.
            CollectionAssert.IsEmpty(findings.Where(finding => finding.Kind == OmsiSignalFindingKind.PedestrianWindow || finding.Kind == OmsiSignalFindingKind.ShortIntergreen)
                .Select(finding => finding.Kind + " " + finding.First + " " + finding.Second + " " + finding.Amount));
        }

        [Test]
        public void ATemplateMadeFromALitCrossingKeepsItsPedestrianGroup()
        {
            var template = OmsiSignalTemplates.Find(OmsiSignalTemplates.PedestrianRequest);
            Assert.DoesNotThrow(template.Validate);
            var text = OmsiSignalTemplate.FromText(template.ToText());
            CollectionAssert.AreEqual(new[] { OmsiSignalRole.Pedestrians }, text.Roles[1]);

            var layout = OmsiJunctionLayout.Build(PieceArms(true), Texture);
            var bytes = OmsiTextFile.Encode(OmsiJunctionGenerator.Junction(PieceArms(true), Texture, "Crossing piece").Sco, OmsiTextEncoding.Windows1252);
            var crossing = OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(bytes), OmsiSignalProgram.Read(bytes));
            var footpaths = new HashSet<int>(crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && WayOf(layout, movement) != null)
                .Select(movement => movement.Index));
            var first = OmsiSignalTemplateMatch.Match(template, crossing, new HashSet<int>(), null, footpaths);
            foreach (var movement in crossing.Movements)
                movement.Light = first.MovementLights[movement.Index];

            var (saved, imprecise) = OmsiSignalTemplate.FromCrossing("Mine", null, template.Program, crossing, new HashSet<int>());
            Assert.AreEqual(0, imprecise);
            CollectionAssert.Contains(saved.Roles[1], OmsiSignalRole.Pedestrians);
            var again = OmsiSignalTemplateMatch.Match(OmsiSignalTemplate.FromText(saved.ToText()), crossing, new HashSet<int>(), null, footpaths);
            CollectionAssert.AreEqual(first.MovementLights, again.MovementLights);

            // A pedestrian role with an arm, or a vehicle role without one, is refused.
            var broken = template.Copy();
            broken.Roles[1].Clear();
            broken.Roles[1].Add(new OmsiSignalRole(0, OmsiMovementTurn.Pedestrian));
            Assert.Throws<System.IO.InvalidDataException>(broken.Validate);
        }
    }
}
