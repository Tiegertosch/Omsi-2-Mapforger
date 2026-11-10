using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSignalIntersectionTests
    {
        // A crossroads of four one-lane approaches (x right, y forward). From the south a short entry piece (path 0) leads
        // on straight (1) and round a right turn (2): a shared lane. The others go straight through: north (3), west (4),
        // east (5). Path 6 is a footpath. Lights: 0 north-south, 1 east-west.
        private static string Path(double x, double y, double heading, double radius, double length, int kind = 0, int direction = 0)
        {
            // (invariant numbers: under a German culture the radius would be written with a comma and the path dropped)
            return FormattableString.Invariant(
                $"[path]\r\n{x}\r\n{y}\r\n0\r\n{heading}\r\n{radius}\r\n{length}\r\n0\r\n0\r\n{kind}\r\n3\r\n{direction}\r\n0\r\n\r\n");
        }

        private static readonly string Crossroads =
            "[traffic_lights_group]\r\n60\r\n\r\n" +
            "[traffic_light]\r\nNorthSouth\r\n\r\n[phase]\r\n6\r\n25\r\n\r\n[phase]\r\n0\r\n0\r\n\r\n" +
            "[traffic_light]\r\nEastWest\r\n\r\n[phase]\r\n0\r\n30\r\n\r\n[phase]\r\n6\r\n25\r\n\r\n[phase]\r\n0\r\n0\r\n\r\n" +
            Path(2, -15, 0, 0, 5) +
            Path(2, -10, 0, 0, 25) +
            Path(2, -10, 0, 8, Math.PI * 8 / 2) +
            Path(-2, 15, 180, 0, 30) + "[use_traffic_light]\r\n0\r\n\r\n" +
            Path(-15, -2, 90, 0, 30) + "[use_traffic_light]\r\n1\r\n\r\n" +
            Path(15, 2, 270, 0, 30) + "[use_traffic_light]\r\n1\r\n\r\n" +
            Path(-6, -8, 90, 0, 12, 1, 2);

        private static (OmsiSceneryObject Scenery, OmsiSignalProgram Program, OmsiSignalIntersection Crossing) Read()
        {
            var bytes = OmsiTextFile.Encode(Crossroads, OmsiTextEncoding.Windows1252);
            var scenery = OmsiSceneryObject.Parse(bytes);
            var program = OmsiSignalProgram.Read(bytes);
            return (scenery, program, OmsiSignalIntersection.Analyse(scenery, program));
        }

        [Test]
        public void TheApproachesLanesAndMovementsAreFoundFromThePaths()
        {
            var (_, _, crossing) = Read();

            Assert.AreEqual(4, crossing.Approaches.Count);
            var south = crossing.Approaches.Single(approach => Math.Abs(approach.InwardDegrees) < 1);
            Assert.AreEqual(1, south.Lanes.Count);
            Assert.AreEqual(0, south.Lanes[0].EntryPath);
            var fromSouth = south.Lanes[0].Movements.Select(index => crossing.Movements[index]).ToList();
            CollectionAssert.AreEquivalent(new[] { OmsiMovementTurn.Straight, OmsiMovementTurn.Right }, fromSouth.Select(movement => movement.Turn).ToArray());
            var straight = fromSouth.Single(movement => movement.Turn == OmsiMovementTurn.Straight);
            CollectionAssert.AreEqual(new[] { 0, 1 }, straight.Paths);
            var right = fromSouth.Single(movement => movement.Turn == OmsiMovementTurn.Right);
            CollectionAssert.AreEqual(new[] { 0, 2 }, right.Paths);
            var east = crossing.Approaches.Single(approach => Math.Abs(OmsiRoadPlanner.Normalize(approach.InwardDegrees - 270)) < 1);
            Assert.AreEqual(east.Index, right.ExitApproach);
            var north = crossing.Approaches.Single(approach => Math.Abs(OmsiRoadPlanner.Normalize(approach.InwardDegrees - 180)) < 1);
            Assert.AreEqual(north.Index, straight.ExitApproach);
            // The footpath crosses on its own.
            var foot = crossing.Movements.Single(movement => movement.Turn == OmsiMovementTurn.Pedestrian);
            Assert.AreEqual(-1, foot.Approach);
            CollectionAssert.AreEqual(new[] { 6 }, foot.Paths);
        }

        [Test]
        public void TheLightOfAMovementIsTheFirstBoundPathOfItsChain()
        {
            var (_, _, crossing) = Read();

            var fromNorth = crossing.Movements.Single(movement => movement.Paths[0] == 3);
            Assert.AreEqual(0, fromNorth.Light);
            Assert.AreEqual(3, fromNorth.ControlPath);
            Assert.IsTrue(crossing.Movements.Where(movement => movement.Paths[0] == 4 || movement.Paths[0] == 5).All(movement => movement.Light == 1));
            // The south lane has no light yet: its stop line would be at its first path, which both its movements share.
            var fromSouth = crossing.Movements.Where(movement => movement.Paths[0] == 0).ToList();
            Assert.IsTrue(fromSouth.All(movement => movement.Light == -1 && movement.ControlPath == 0));
            Assert.IsFalse(crossing.Movements.Any(movement => movement.HasSeveralLights));
            CollectionAssert.IsEmpty(crossing.LooseBoundPaths);
        }

        [Test]
        public void AssigningAMovementOfASharedLaneReachesTheOthersOfTheLane()
        {
            var (_, program, crossing) = Read();
            var straight = crossing.Movements.Single(movement => movement.Paths.SequenceEqual(new[] { 0, 1 }));

            var reached = crossing.Assign(program, straight, 0);

            Assert.AreEqual(2, reached.Count);
            Assert.AreEqual(0, program.PathLights[0]);
            Assert.IsTrue(crossing.Movements.Where(movement => movement.Paths[0] == 0).All(movement => movement.Light == 0));

            // A movement whose own path is bound later in its chain changes there, alone.
            var file = OmsiRecordFile.Parse(OmsiTextFile.Encode(Crossroads, OmsiTextEncoding.Windows1252), OmsiSceneryRecordLayouts.Instance);
            program.WriteTo(file);
            Assert.AreEqual(0, OmsiSignalProgram.Read(file).PathLights[0]);
            Assert.Throws<ArgumentOutOfRangeException>(() => crossing.Assign(program, straight, 5));
        }

        [Test]
        public void ANewLightIsRedAndRemovingOneRenumbersEverythingAfterIt()
        {
            var (_, program, _) = Read();
            program.Holds.Add(new OmsiSignalHold { Light = 1, Seconds = 10, IfNoRequest = true });
            program.Holds.Add(new OmsiSignalHold { Light = 0, Seconds = 5 });

            var added = program.AddLight(" Left turn ");

            Assert.AreEqual(2, added);
            Assert.AreEqual("Left turn", program.Lights[added].Name);
            for (var second = 0; second < 60; second += 7)
                Assert.AreEqual(OmsiSignalAspect.Red, OmsiSignalAspects.Of(program.StateAt(added, second)));
            program.PathLights[0] = added;

            var map = program.RemoveLight(0);

            CollectionAssert.AreEqual(new[] { -1, 0, 1 }, map);
            CollectionAssert.AreEqual(new[] { "EastWest", "Left turn" }, program.Lights.Select(light => light.Name).ToArray());
            // Path 3 obeyed the light taken away; paths 4 and 5 and the new binding move down.
            CollectionAssert.AreEqual(new[] { 1, -1, -1, -1, 0, 0, -1 }, program.PathLights);
            Assert.AreEqual(1, program.Holds.Count);
            Assert.AreEqual(0, program.Holds[0].Light);
            Assert.Throws<ArgumentOutOfRangeException>(() => program.RemoveLight(5));
        }
    }
}
