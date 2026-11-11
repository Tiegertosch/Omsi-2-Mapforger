using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiCrossingTests
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

        // The same road without footpaths.
        private const string BareProfile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n";

        private static OmsiJunctionLayout Tee(string profileText, OmsiCrossing crossingOnSideArm)
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(profileText), ProfilePath);
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList();
            arms[2].Crossing = crossingOnSideArm;
            return OmsiJunctionLayout.Build(arms, Texture);
        }

        private static (double X, double Y) End(OmsiJunctionPath path)
        {
            var radians = path.HeadingDegrees * Math.PI / 180.0;
            return (path.X + Math.Sin(radians) * path.LengthMeters, path.Y + Math.Cos(radians) * path.LengthMeters);
        }

        [Test]
        public void AnArmWithoutACrossingIsBuiltAsBefore()
        {
            var plain = Tee(Profile, null);
            Assert.IsTrue(plain.Arms.All(arm => Math.Abs(arm.Length - 12.0) < 1e-9));
            Assert.IsTrue(plain.CrossingProblems.All(problem => problem == null));
            Assert.IsFalse(plain.Paths.Any(path => path.Kind == 1 && Math.Abs(path.LengthMeters - 11.0) < 1e-6));
        }

        [Test]
        public void ACrossingMakesTheArmReachOutAndLaysAFootpathAcrossIt()
        {
            var layout = Tee(Profile, new OmsiCrossing());

            // The corner starts 12 m out; the crossing's centre is 3 m beyond, 3 m wide, and the arm reaches 2 m past its edge.
            var side = layout.Arms.Single(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            Assert.AreEqual(12.0 + 3.0 + 1.5 + 2.0, side.Length, 1e-9);
            Assert.IsNull(layout.CrossingProblems[layout.Arms.IndexOf(side)]);

            var across = layout.Paths.Single(path => path.Kind == 1 && path.RadiusMeters == 0.0 && Math.Abs(path.LengthMeters - 11.0) < 1e-6 && Math.Abs(path.Y - 15.0) < 1e-6);
            Assert.AreEqual(2, across.Direction);
            Assert.AreEqual(3.0, across.Width, 1e-9);
            Assert.AreEqual(5.5, across.X, 1e-6);
            Assert.AreEqual(270.0, across.HeadingDegrees, 1e-6);
            var end = End(across);
            Assert.AreEqual(-5.5, end.X, 1e-6);
            Assert.AreEqual(15.0, end.Y, 1e-6);
        }

        [Test]
        public void ThePavementPathsOfTheArmAreCutWhereTheCrossingJoinsThem()
        {
            var layout = Tee(Profile, new OmsiCrossing());
            var along = layout.Paths.Where(path => path.Kind == 1 && path.RadiusMeters == 0.0 && Math.Abs(Math.Abs(path.X) - 5.5) < 1e-6 &&
                                                   Math.Abs(path.HeadingDegrees - 180.0) < 1e-6).ToList();
            // On each side one piece from the end of the arm to the crossing and one on to the corner.
            Assert.AreEqual(4, along.Count);
            foreach (var side in new[] { 5.5, -5.5 })
            {
                var outer = along.Single(path => Math.Abs(path.X - side) < 1e-6 && Math.Abs(path.Y - 18.5) < 1e-6);
                Assert.AreEqual(3.5, outer.LengthMeters, 1e-6);
                var inner = along.Single(path => Math.Abs(path.X - side) < 1e-6 && Math.Abs(path.Y - 15.0) < 1e-6);
                Assert.AreEqual(3.0, inner.LengthMeters, 1e-6);
                Assert.AreEqual(2, inner.Direction);
            }
        }

        [Test]
        public void TheCrossingJoinsTheFootpathsAtBothEnds()
        {
            var layout = Tee(Profile, new OmsiCrossing());
            var across = layout.Paths.Single(path => path.Kind == 1 && Math.Abs(path.LengthMeters - 11.0) < 1e-6 && Math.Abs(path.Y - 15.0) < 1e-6);
            var others = layout.Paths.Where(path => path.Kind == 1 && path != across).ToList();
            foreach (var point in new[] { (across.X, across.Y), End(across) })
                Assert.IsTrue(others.Any(path => Near(point, (path.X, path.Y)) || Near(point, End(path))), "No footpath ends at " + point);
        }

        [Test]
        public void TheVehicleLanesRunStraightOnToTheCornerAndNoPathCrossesTheMiddleOfTheWay()
        {
            var layout = Tee(Profile, new OmsiCrossing());
            // The lane out of the arm leads from the corner's start to the arm's end; the one in runs the other way.
            var lanes = layout.Paths.Where(path => path.Kind == 0 && path.RadiusMeters == 0.0 && Math.Abs(path.LengthMeters - 6.5) < 1e-6).ToList();
            Assert.AreEqual(2, lanes.Count, string.Join(" | ", layout.Paths.Select(path => path.Kind + ":" + path.X + "," + path.Y + " len " + path.LengthMeters)));
        }

        [Test]
        public void AnArmWithoutPavementsKeepsItsLengthAndSaysWhy()
        {
            var layout = Tee(BareProfile, new OmsiCrossing());
            var index = layout.Arms.FindIndex(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            Assert.IsNotNull(layout.CrossingProblems[index]);
            Assert.IsNull(layout.Arms[index].Crossing);
            Assert.AreEqual(layout.Arms[(index + 1) % 3].Length, layout.Arms[index].Length, 1e-9);
        }

        [Test]
        public void TheCrossingsAreKeptWithTheJunctionAsALine()
        {
            var layout = Tee(Profile, new OmsiCrossing { DistanceMeters = 4.0, WidthMeters = 2.5 });
            var line = OmsiCrossing.Write(layout.Arms);
            var arm = layout.Arms.FindIndex(each => each.Crossing != null);
            Assert.AreEqual("MapWorks crossings: " + arm + " 4 2.5", line);
            var read = OmsiCrossing.Read("text\r\n" + line + "\r\nmore");
            Assert.AreEqual(1, read.Count);
            Assert.AreEqual(4.0, read[arm].DistanceMeters, 1e-9);
            Assert.AreEqual(2.5, read[arm].WidthMeters, 1e-9);
            Assert.IsNull(OmsiCrossing.Write(Tee(Profile, null).Arms));
        }

        [Test]
        public void ValuesOutOfRangeAreKeptWithinTheBounds()
        {
            var read = OmsiCrossing.Read("MapWorks crossings: 1 0.5 20");
            Assert.AreEqual(OmsiCrossing.ClosestMeters, read[1].DistanceMeters);
            Assert.AreEqual(OmsiCrossing.WidestMeters, read[1].WidthMeters);
        }

        [Test]
        public void ACrossingPieceIsAPlainStretchOfRoadWithTheCrossingInItsMiddle()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var piece = OmsiJunctionLayout.Build(new[] { 90.0, 270.0 }.Select(heading => new OmsiJunctionArm
                { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile, Crossing = heading < 100 ? new OmsiCrossing() : null }).ToList(), Texture);

            Assert.IsTrue(piece.IsTransition);
            Assert.IsNull(piece.CrossingProblems[0]);
            Assert.IsNotNull(piece.Arms[0].Crossing);
            // Long enough for the crossing (3 m wide) with room before and behind it: 11 m, so each arm 5.5 m.
            Assert.AreEqual(5.5, piece.Arms[0].Length, 1e-9);
            Assert.AreEqual(5.5, piece.Arms[1].Length, 1e-9);
            // The crossing's footpath runs across the carriageway from pavement to pavement, bound under its own key.
            var across = piece.Paths.Single(path => path.WayThrough == OmsiCrossing.WayKey(0));
            Assert.AreEqual(1, across.Kind);
            Assert.AreEqual(11.0, across.LengthMeters, 1e-6);
            CollectionAssert.AreEquivalent(new[] { OmsiCrossing.WayKey(0) }, piece.CrossingWays);
            // Each pavement path is cut in the middle: two pieces of 5.5 m on each side.
            var pieces = piece.Paths.Where(path => path.Kind == 1 && path.RadiusMeters == 0.0 && Math.Abs(path.LengthMeters - 5.5) < 1e-6).ToList();
            Assert.AreEqual(4, pieces.Count);
            // The zebra bars lie over the carriageway; the vehicle lanes run straight through the piece, crossing the footpath in one object.
            var bars = piece.Meshes.Single(mesh => mesh.Texture == OmsiJunctionMarkings.WhiteTexture);
            Assert.AreEqual(8 * 4, bars.Positions.Count / 3);
            Assert.AreEqual(2, piece.Paths.Count(path => path.Kind == 0));

            // Without a crossing the stretch has its footpaths whole, as before.
            var plain = OmsiJunctionLayout.Build(new[] { 90.0, 270.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList(), Texture);
            Assert.AreEqual(0, plain.CrossingWays.Count);
            Assert.IsFalse(plain.Meshes.Any(mesh => mesh.Texture == OmsiJunctionMarkings.WhiteTexture));
        }

        [Test]
        public void TheKerbOfACrossingPieceIsLoweredAtTheCrossingAndRisesAgainBeforeAndAfter()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            OmsiJunctionLayout Piece(bool crossing) => OmsiJunctionLayout.Build(new[] { 90.0, 270.0 }.Select(heading => new OmsiJunctionArm
                { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile, Crossing = crossing && heading < 100 ? new OmsiCrossing() : null }).ToList(), Texture);
            // Heights of the pavement at a place along the road (x, the arm runs east) and across it (y, up to 4 m is the kerb, 7 m the outer edge).
            double? HeightAt(OmsiJunctionLayout layout, double along, double across)
            {
                foreach (var mesh in layout.Meshes.Where(each => each.Texture == "side.bmp"))
                    for (var vertex = 0; vertex < mesh.Positions.Count / 3; vertex++)
                        if (Math.Abs(mesh.Positions[vertex * 3] - along) < 0.13 && Math.Abs(Math.Abs(mesh.Positions[vertex * 3 + 2]) - across) < 0.01 &&
                            mesh.Positions[vertex * 3 + 1] > 0.05)
                            return mesh.Positions[vertex * 3 + 1];
                return null;
            }
            var plain = Piece(false);
            var lowered = Piece(true);

            // Without a crossing the pavement is 0.25 m high everywhere, at the kerb too.
            Assert.AreEqual(0.25, HeightAt(plain, 0.0, 4.0).Value, 1e-6);
            // At the crossing the kerb is the carriageway's height (0.1) plus the 3 cm lip; the outer edge keeps its height, so the pavement slopes up.
            Assert.AreEqual(0.13, HeightAt(lowered, 0.0, 4.0).Value, 0.005);
            Assert.AreEqual(0.25, HeightAt(lowered, 0.0, 7.0).Value, 1e-6);
            // Past the ramp, 1.5 m beyond the crossing's 1.5 m half width, and at the ends of the piece it is whole again.
            Assert.AreEqual(0.25, HeightAt(lowered, 3.25, 4.0).Value, 0.01);
            Assert.AreEqual(0.25, HeightAt(lowered, 5.5, 4.0).Value, 1e-6);
            // In the ramp it lies between.
            var halfway = HeightAt(lowered, 2.25, 4.0).Value;
            Assert.That(halfway, Is.GreaterThan(0.14).And.LessThan(0.24));
            // The people's path across walks at the carriageway.
            Assert.AreEqual(0.13, lowered.Paths.Single(path => path.WayThrough == OmsiCrossing.WayKey(0)).Height, 1e-9);
        }

        [Test]
        public void ACrossingPieceRefusesAChangeOfCrossSectionAndARoadWithoutPavements()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var bare = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(BareProfile), ProfilePath);
            var change = OmsiJunctionLayout.Build(new[] { new OmsiJunctionArm { HeadingDegrees = 90.0, ProfilePath = ProfilePath, Profile = profile, Crossing = new OmsiCrossing() },
                new OmsiJunctionArm { HeadingDegrees = 270.0, ProfilePath = ProfilePath, Profile = bare } }, Texture);
            Assert.IsNotNull(change.CrossingProblems[0]);
            Assert.IsNull(change.Arms[0].Crossing);
            Assert.AreEqual(0, change.CrossingWays.Count);

            var noPavement = OmsiJunctionLayout.Build(new[] { 90.0, 270.0 }.Select(heading => new OmsiJunctionArm
                { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = bare, Crossing = heading < 100 ? new OmsiCrossing() : null }).ToList(), Texture);
            Assert.IsNotNull(noPavement.CrossingProblems[0]);
        }

        [Test]
        public void AnArmWithRailsRefusesTheCrossing()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);

            var withRails = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile + "[path]\r\n2\r\n0\r\n0.1\r\n1.4\r\n2\r\n"), ProfilePath);
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = withRails }).ToList();
            arms[2].Crossing = new OmsiCrossing();
            var layout = OmsiJunctionLayout.Build(arms, Texture);
            Assert.IsTrue(layout.CrossingProblems.Any(problem => problem != null && problem.Contains("rails")));
            Assert.IsTrue(layout.Arms.All(arm => arm.Crossing == null && Math.Abs(arm.Length - 12.0) < 1e-9));
        }

        [Test]
        public void AZebraCrossingIsPaintedAcrossTheCarriagewayAndTheStopLineMovesBeforeIt()
        {
            var layout = Tee(Profile, new OmsiCrossing());
            var bars = layout.Meshes.Single(mesh => mesh.Texture == OmsiJunctionMarkings.WhiteTexture);
            // An 8 m carriageway takes eight bars of 0.5 m with 0.5 m between, each two triangles.
            Assert.AreEqual(8 * 4, bars.Positions.Count / 3);
            Assert.AreEqual(8 * 6, bars.Triangles.Count);
            for (var vertex = 0; vertex < bars.Positions.Count / 3; vertex++)
            {
                var x = bars.Positions[vertex * 3];
                var y = bars.Positions[vertex * 3 + 2];
                Assert.That(x, Is.InRange(-4.0 - 1e-9, 4.0 + 1e-9));
                // The crossing's centre lies 15 m out and it is 3 m wide.
                Assert.That(y, Is.InRange(13.5 - 1e-9, 16.5 + 1e-9));
            }

            // The stop line of the arm lies 1.5 m before the bars, 18 m out; the anchors at its end stay at 12 m.
            var index = layout.Arms.FindIndex(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            var anchors = OmsiJunctionMarkings.Anchors(layout).Where(anchor => anchor.Arm == index).ToList();
            Assert.IsTrue(anchors.Where(anchor => anchor.Row == 1).All(anchor => Math.Abs(anchor.Y - 18.0) < 1e-9));
            Assert.IsTrue(anchors.Where(anchor => anchor.Row == 0).All(anchor => Math.Abs(anchor.Y - 12.0) < 1e-9));
            // Another arm keeps its stop line at its mouth.
            var other = OmsiJunctionMarkings.Anchors(layout).First(anchor => anchor.Arm != index && anchor.Row == 1);
            Assert.AreEqual(10.0, Math.Sqrt(other.X * other.X + other.Y * other.Y) - Math.Abs(other.Lateral) * 0.0, 2.0);
            Assert.IsFalse(Tee(Profile, null).Meshes.Any(mesh => mesh.Texture == OmsiJunctionMarkings.WhiteTexture));
        }

        private static List<OmsiJunctionArm> TeeArms(OmsiCrossing crossingOnSideArm)
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            var arms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList();
            arms[2].Crossing = crossingOnSideArm;
            return arms;
        }

        [Test]
        public void TheCrossingFootpathIsBoundToALightUnderAKeyOfItsOwn()
        {
            var layout = OmsiJunctionLayout.Build(TeeArms(new OmsiCrossing()), Texture);
            var index = layout.Arms.FindIndex(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            var key = OmsiCrossing.WayKey(index);
            CollectionAssert.AreEquivalent(new[] { key }, layout.CrossingWays);
            Assert.IsFalse(layout.WaysThrough.Contains(key));
            var across = layout.Paths.Single(path => path.WayThrough == key);
            Assert.IsTrue(across.StartsWay);
            Assert.AreEqual(1, across.Kind);
            // Without a crossing there is no such key.
            Assert.AreEqual(0, OmsiJunctionLayout.Build(TeeArms(null), Texture).CrossingWays.Count);
        }

        [Test]
        public void VehiclesAreHeldBeforeTheCrossingByTheLeadInOfTheirLaneAndTheCrossingFollowsItsOwnLight()
        {
            var layout = OmsiJunctionLayout.Build(TeeArms(new OmsiCrossing()), Texture);
            var index = layout.Arms.FindIndex(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            var signals = new OmsiJunctionSignals();
            signals.Program.CycleSeconds = 60;
            var vehicles = signals.Program.AddLight("Side");
            var feet = signals.Program.AddLight("Foot");
            var sideWays = layout.WaysThrough.Where(way => way.StartsWith(index + ".")).ToList();
            Assert.Greater(sideWays.Count, 0);
            foreach (var way in sideWays)
                signals.WayLights[way] = vehicles;
            signals.WayLights[OmsiCrossing.WayKey(index)] = feet;

            var made = OmsiJunctionGenerator.Junction(TeeArms(new OmsiCrossing()), Texture, "Crossing", signals: signals);

            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var program = OmsiSignalProgram.Read(bytes);
            for (var path = 0; path < layout.Paths.Count; path++)
            {
                var each = layout.Paths[path];
                var expected = each.LeadInLane != null && each.LeadInLane.StartsWith(index + ".") ? vehicles
                    : each.WayThrough == OmsiCrossing.WayKey(index) ? feet
                    : -1;
                Assert.AreEqual(expected, program.PathLights[path], "path " + path + " " + each.WayThrough + " " + each.LeadInLane);
            }
            // The arcs of the ways of the lane are not bound again inside the junction.
            Assert.IsFalse(layout.Paths.Where(path => path.StartsWay && path.WayThrough != null && sideWays.Contains(path.WayThrough))
                .Any(path => program.PathLights[layout.Paths.IndexOf(path)] >= 0));
            StringAssert.Contains(OmsiCrossing.WayKey(index) + "=" + feet, made.Sco);

            // Analysed again, the movements from the arm obey the light of the lead-in, and the crossing is a footpath of its own light.
            var crossing = OmsiSignalIntersection.Analyse(OmsiSceneryObject.Parse(bytes), program);
            var foot = crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && movement.Light == feet).ToList();
            Assert.AreEqual(1, foot.Count);
            Assert.Greater(crossing.Movements.Count(movement => movement.Light == vehicles), 0);
        }

        [Test]
        public void WaysThatObeyDifferentLightsKeepTheirOwnArcsAndNoLeadInIsBound()
        {
            var layout = OmsiJunctionLayout.Build(TeeArms(new OmsiCrossing()), Texture);
            var index = layout.Arms.FindIndex(arm => Math.Abs(arm.HeadingDegrees) < 1e-9);
            var signals = new OmsiJunctionSignals();
            signals.Program.CycleSeconds = 60;
            signals.Program.AddLight("A");
            signals.Program.AddLight("B");
            var sideWays = layout.WaysThrough.Where(way => way.StartsWith(index + ".")).OrderBy(way => way, StringComparer.Ordinal).ToList();
            Assert.GreaterOrEqual(sideWays.Count, 2);
            signals.WayLights[sideWays[0]] = 0;
            signals.WayLights[sideWays[1]] = 1;

            var made = OmsiJunctionGenerator.Junction(TeeArms(new OmsiCrossing()), Texture, "Split", signals: signals);

            var program = OmsiSignalProgram.Read(OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252));
            foreach (var path in layout.Paths.Where(path => path.LeadInLane != null))
                Assert.AreEqual(-1, program.PathLights[layout.Paths.IndexOf(path)]);
            foreach (var way in new[] { sideWays[0], sideWays[1] })
                Assert.AreEqual(signals.WayLights[way], program.PathLights[layout.Paths.FindIndex(path => path.StartsWay && path.WayThrough == way)]);
        }

        [Test]
        public void ATurnGivesWayToTheFootpathItCrossesOnTheExitButNotToTheOneOverItsOwnArm()
        {
            var layout = OmsiJunctionLayout.Build(TeeArms(new OmsiCrossing()), Texture);
            var made = OmsiJunctionGenerator.Junction(TeeArms(new OmsiCrossing()), Texture, "Conflicts");
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var scenery = OmsiSceneryObject.Parse(bytes);
            var crossing = OmsiSignalIntersection.Analyse(scenery, OmsiSignalProgram.Read(bytes));
            var foot = crossing.Movements.Single(movement => movement.Turn == OmsiMovementTurn.Pedestrian && movement.Paths.Any(path =>
                path < layout.Paths.Count && layout.Paths[path].WayThrough != null && layout.Paths[path].WayThrough.StartsWith("x")));
            var conflicts = OmsiSignalConflicts.Of(scenery, crossing).Where(conflict => conflict.Kind == OmsiConflictKind.Pedestrian &&
                (conflict.First == foot.Index || conflict.Second == foot.Index)).ToList();
            Assert.IsTrue(conflicts.Any(conflict => !conflict.Permitted), "traffic from the arm itself must stop for it");
            Assert.IsTrue(conflicts.Any(conflict => conflict.Permitted), "turns into the arm give way to it");
        }

        [Test]
        public void TheAssistantGivesACrossingALightGreenWithTheTrafficThatDoesNotCrossItAndKeepsItConflictFree()
        {
            var layout = OmsiJunctionLayout.Build(TeeArms(new OmsiCrossing()), Texture);
            var made = OmsiJunctionGenerator.Junction(TeeArms(new OmsiCrossing()), Texture, "Wizard");
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var scenery = OmsiSceneryObject.Parse(bytes);
            var crossing = OmsiSignalIntersection.Analyse(scenery, OmsiSignalProgram.Read(bytes));
            var conflicts = OmsiSignalConflicts.Of(scenery, crossing);
            var foot = crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && movement.Paths.Any(path =>
                path < layout.Paths.Count && layout.Paths[path].WayThrough != null && layout.Paths[path].WayThrough.StartsWith("x"))).Select(movement => movement.Index).ToList();
            Assert.AreEqual(1, foot.Count);

            var proposal = OmsiSignalWizard.Propose(crossing, conflicts, new OmsiSignalWizardOptions(), null, foot);

            var light = proposal.MovementLights[foot[0]];
            Assert.GreaterOrEqual(light, 0);
            Assert.IsTrue(proposal.Groups[light].Pedestrian);
            Assert.IsTrue(proposal.ConflictFree, string.Join("; ", proposal.HardClashes.Select(clash => clash.Conflict.First + "-" + clash.Conflict.Second)));
            Assert.AreEqual(0, proposal.CrossingFootpaths);
            // The check finds nothing wrong with the light the assistant wrote: its red and yellow and yellow show red on a pedestrian head.
            var checkedProgram = OmsiSignalCheck.Run(proposal.Program, crossing, conflicts, proposal.MovementLights, null, null, new HashSet<int> { light });
            Assert.IsFalse(checkedProgram.Any(finding => finding.Kind == OmsiSignalFindingKind.Timing && finding.First == light),
                string.Join("; ", checkedProgram.Where(finding => finding.First == light).Select(finding => finding.Kind + " " + finding.Level)));
            Assert.IsFalse(checkedProgram.Any(finding => finding.Kind == OmsiSignalFindingKind.PedestrianWindow));
            // It goes with the phase of the through road, not with the side arm it crosses.
            var phasesWithIt = proposal.Plan.Phases.Where(phase => phase.Lights.Contains(light)).ToList();
            Assert.AreEqual(1, phasesWithIt.Count);
            // The approaches whose traffic crosses it for certain are in none of its phases.
            var blocking = conflicts.Where(conflict => !conflict.Permitted && (conflict.First == foot[0] || conflict.Second == foot[0]))
                .Select(conflict => crossing.Movements[conflict.First == foot[0] ? conflict.Second : conflict.First].Approach).Distinct().ToList();
            Assert.Greater(blocking.Count, 0);
            Assert.IsFalse(phasesWithIt[0].Lights.Any(each => blocking.Any(approach => proposal.Groups[each].Approaches.Contains(approach))),
                "phases " + string.Join(" | ", proposal.Plan.Phases.Select(phase => string.Join(",", phase.Lights.Select(each => proposal.Groups[each].Name)))) +
                "; blocking approaches " + string.Join(",", blocking));
            // Long enough to reach the far side at the walking pace.
            Assert.GreaterOrEqual(phasesWithIt[0].GreenSeconds, Math.Ceiling(11.0 / 1.2 + 2.0));

            // Off, the footpath gets no light and is counted as one the assistant leaves out.
            var without = OmsiSignalWizard.Propose(crossing, conflicts, new OmsiSignalWizardOptions { PedestrianLights = false }, null, foot);
            Assert.AreEqual(-1, without.MovementLights[foot[0]]);
            Assert.AreEqual(1, without.CrossingFootpaths);
        }

        [Test]
        public void ACrossingLightWhoseGreenIsTooShortToCrossIsWarnedAbout()
        {
            var layout = OmsiJunctionLayout.Build(TeeArms(new OmsiCrossing()), Texture);
            var made = OmsiJunctionGenerator.Junction(TeeArms(new OmsiCrossing()), Texture, "Window");
            var bytes = OmsiTextFile.Encode(made.Sco, OmsiTextEncoding.Windows1252);
            var scenery = OmsiSceneryObject.Parse(bytes);
            var crossing = OmsiSignalIntersection.Analyse(scenery, OmsiSignalProgram.Read(bytes));
            var conflicts = OmsiSignalConflicts.Of(scenery, crossing);
            var foot = crossing.Movements.Where(movement => movement.Turn == OmsiMovementTurn.Pedestrian && movement.Paths.Any(path =>
                path < layout.Paths.Count && layout.Paths[path].WayThrough != null && layout.Paths[path].WayThrough.StartsWith("x"))).Select(movement => movement.Index).ToList();
            var proposal = OmsiSignalWizard.Propose(crossing, conflicts, new OmsiSignalWizardOptions(), null, foot);
            var light = proposal.MovementLights[foot[0]];
            var lights = proposal.MovementLights;

            Assert.IsFalse(OmsiSignalCheck.Run(proposal.Program, crossing, conflicts, lights, null, null, new HashSet<int> { light })
                .Any(finding => finding.Kind == OmsiSignalFindingKind.PedestrianWindow));

            // The same program with the crossing's green cut to 5 s: nobody would start across 11 m.
            var short5 = proposal.Plan.Copy();
            foreach (var phase in short5.Phases.Where(phase => phase.Lights.Contains(light)))
                phase.GreenSeconds = 5;
            var program = proposal.Program.Copy();
            short5.WriteTo(program);
            var findings = OmsiSignalCheck.Run(program, crossing, conflicts, lights, null, null, new HashSet<int> { light });
            var window = findings.Single(finding => finding.Kind == OmsiSignalFindingKind.PedestrianWindow);
            Assert.AreEqual(light, window.First);
            Assert.AreEqual(OmsiSignalIssueLevel.Warning, window.Level);
            Assert.AreEqual(Math.Ceiling(11.0 / 1.2 + 1.0), window.Amount);
        }

        [Test]
        public void FootpathsJoinByPlaceWhateverTheirHeadingButVehiclePathsNeedTheSameWay()
        {
            OmsiMapPath Make(int kind, double x0, double y0, double x1, double y1) =>
                new OmsiMapPath { Kind = kind, Direction = 0, Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint(x1, y1) } };
            // A path along y ending at (0, 10) and another leaving it to the east: a right angle.
            var footpaths = new List<OmsiMapPath> { Make(1, 0, 0, 0, 10), Make(1, 0, 10, 10, 10) };
            OmsiMapPaths.FindOpenEnds(footpaths);
            Assert.IsFalse(footpaths[0].OpenEnd);
            Assert.IsFalse(footpaths[1].OpenStart);
            var vehicles = new List<OmsiMapPath> { Make(0, 0, 0, 0, 10), Make(0, 0, 10, 10, 10) };
            OmsiMapPaths.FindOpenEnds(vehicles);
            Assert.IsTrue(vehicles[0].OpenEnd);
            Assert.IsTrue(vehicles[1].OpenStart);
        }

        private static bool Near((double X, double Y) a, (double X, double Y) b)
        {
            return Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;
        }
    }
}
