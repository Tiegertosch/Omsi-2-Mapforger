using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiJunctionMarkingsTests
    {
        private const string ProfilePath = @"Splines\Test\road.sli";

        // An 8 m carriageway at 0.1 m with a lane each way, 2 m either side of the spline, and pavements beyond.
        private const string Profile =
            "[texture]\r\nside.bmp\r\n[texture]\r\nasphalt.bmp\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-7\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n-4\r\n0.1\r\n1\r\n0.2\r\n" +
            "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n0.1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n0.1\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.1\r\n0\r\n0.2\r\n[profilepnt]\r\n4\r\n0.25\r\n1\r\n0.2\r\n" +
            "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.25\r\n0\r\n0.2\r\n[profilepnt]\r\n7\r\n0.25\r\n1\r\n0.2\r\n" +
            "[path]\r\n1\r\n-5.5\r\n0.25\r\n1\r\n2\r\n[path]\r\n0\r\n-2\r\n0.1\r\n3.6\r\n1\r\n[path]\r\n0\r\n2\r\n0.1\r\n3.6\r\n0\r\n[path]\r\n1\r\n5.5\r\n0.25\r\n1\r\n2\r\n";

        private string installation;

        [SetUp]
        public void CreateInstallation()
        {
            installation = Path.Combine(Path.GetTempPath(), "mapforge-markings-" + Guid.NewGuid().ToString("N"));
            var textures = Path.Combine(installation, "Splines", "Test", "texture");
            Directory.CreateDirectory(textures);
            File.WriteAllText(Path.Combine(installation, "Splines", "Test", "road.sli"), Profile);
            File.WriteAllBytes(Path.Combine(textures, "side.bmp"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(textures, "asphalt.bmp"), new byte[] { 2 });
        }

        [TearDown]
        public void RemoveInstallation()
        {
            if (Directory.Exists(installation))
                Directory.Delete(installation, true);
        }

        /// <summary>A T junction: arm 0 forward, arm 1 east and arm 2 west, each 12 m long with its mouth at 10 m.</summary>
        private static List<OmsiJunctionArm> Tee()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), ProfilePath);
            return new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm { HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = profile }).ToList();
        }

        [Test]
        public void AnchorsSitOnTheCarriagewayEdgesAndLaneBoundariesOfEachArm()
        {
            var layout = OmsiJunctionLayout.Build(Tee(), null);

            var anchors = OmsiJunctionMarkings.Anchors(layout);

            // Three across each arm (two edges and the boundary between the lanes), at its end and at its mouth.
            Assert.AreEqual(3 * 2 * 3, anchors.Count);
            var east = anchors.Where(anchor => anchor.Arm == 1 && anchor.Row == 0).OrderBy(anchor => anchor.Index).ToList();
            CollectionAssert.AreEqual(new[] { "edge", "lane", "edge" }, east.Select(anchor => anchor.Kind));
            // Leaving east, the left edge lies north.
            Assert.AreEqual(12.0, east[0].X, 1e-9);
            Assert.AreEqual(4.0, east[0].Y, 1e-9);
            Assert.AreEqual(0.0, east[1].Y, 1e-9);
            Assert.AreEqual(-4.0, east[2].Y, 1e-9);
            Assert.AreEqual(0.1, east[1].Height, 1e-9);
            Assert.AreEqual(270.0, east[1].InwardDegrees, 1e-9);
            Assert.AreEqual("1.0.1", east[1].Key);
            var mouth = anchors.Single(anchor => anchor.Key == "1.1.1");
            Assert.AreEqual(10.0, mouth.X, 1e-9);
        }

        [Test]
        public void LinesRunStraightAcrossAndCurveLikeACircleRoundACorner()
        {
            var anchors = OmsiJunctionMarkings.Anchors(OmsiJunctionLayout.Build(Tee(), null));

            // East to west along the main road: straight through the centre.
            var through = OmsiJunctionMarkings.Path(anchors, new OmsiMarkingLine { From = "1.0.1", To = "2.0.1" });
            Assert.IsTrue(through.All(point => Math.Abs(point[1]) < 1e-9));
            Assert.AreEqual(12.0, through.First()[0], 1e-9);
            Assert.AreEqual(-12.0, through.Last()[0], 1e-9);

            // From the forward arm round to the east arm: close to a quarter circle about (12, 12) through both anchors.
            var corner = OmsiJunctionMarkings.Path(anchors, new OmsiMarkingLine { From = "0.0.1", To = "1.0.1" });
            var middle = corner[corner.Count / 2];
            Assert.AreEqual(12.0, Math.Sqrt((middle[0] - 12.0) * (middle[0] - 12.0) + (middle[1] - 12.0) * (middle[1] - 12.0)), 0.05);

            // Across one arm, such as a stop line, always straight.
            var stop = OmsiJunctionMarkings.Path(anchors, new OmsiMarkingLine { From = "0.1.0", To = "0.1.1", Curvature = 0.5 });
            Assert.AreEqual(2, stop.Count);
            // A missing anchor gives no line.
            Assert.IsNull(OmsiJunctionMarkings.Path(anchors, new OmsiMarkingLine { From = "0.1.0", To = "5.0.1" }));
        }

        [Test]
        public void MarkingsAreBuiltIntoTheJunctionAndReadBackFromItsFile()
        {
            var markings = new List<OmsiMarkingLine>
            {
                new OmsiMarkingLine { From = "1.0.1", To = "2.0.1", Style = OmsiMarkingStyle.LaneLine },
                new OmsiMarkingLine { From = "0.1.0", To = "0.1.1", Style = OmsiMarkingStyle.StopLine, Curvature = 0 },
                new OmsiMarkingLine { From = "0.0.0", To = "1.0.0", Style = OmsiMarkingStyle.YellowBoundary, Curvature = 0.25 }
            };

            var made = OmsiJunctionGenerator.Junction(Tee(), null, "Marked", markings);

            StringAssert.Contains("MapWorks markings: 1.0.1-2.0.1 laneline auto; 0.1.0-0.1.1 stop 0; 0.0.0-1.0.0 yellowboundary 0.25", made.Sco);
            StringAssert.Contains(OmsiJunctionMarkings.WhiteTexture, made.Model);
            StringAssert.Contains(OmsiJunctionMarkings.YellowTexture, made.Model);
            Assert.IsTrue(made.Files.ContainsKey(@"texture\" + OmsiJunctionMarkings.WhiteTexture));
            Assert.IsTrue(OmsiImageReader.TryDecode(made.Files[@"texture\" + OmsiJunctionMarkings.WhiteTexture], out var white));
            Assert.AreEqual(8, white.Width);

            var assetPath = OmsiGeneratedObjectStore.Write(installation, "Test", "Marked", made);
            Assert.IsTrue(File.Exists(Path.Combine(installation, Path.GetDirectoryName(assetPath), "texture", OmsiJunctionMarkings.YellowTexture)));
            var back = OmsiGeneratedJunction.Read(installation, assetPath, path => OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), path));
            Assert.AreEqual(3, back.Markings.Count);
            Assert.AreEqual(OmsiMarkingStyle.StopLine, back.Markings[1].Style);
            Assert.AreEqual(0.0, back.Markings[1].Curvature);
            Assert.IsNull(back.Markings[0].Curvature);
            Assert.AreEqual(0.25, back.Markings[2].Curvature);
            Assert.AreEqual(3, back.Arms.Count);
        }

        [Test]
        public void StripsLieJustAboveTheCarriagewayWithStrokesAlongTheLine()
        {
            var layout = OmsiJunctionLayout.Build(Tee(), null);
            var anchors = OmsiJunctionMarkings.Anchors(layout);
            var meshes = new List<OmsiGeneratedMesh>();

            // 24 m of lane line: strokes at 0-3, 9-12 and 18-21 m.
            OmsiJunctionMarkings.AddMeshes(anchors, new[] { new OmsiMarkingLine { From = "1.0.1", To = "2.0.1", Style = OmsiMarkingStyle.LaneLine } }, meshes);

            var mesh = meshes.Single();
            Assert.AreEqual(OmsiJunctionMarkings.WhiteTexture, mesh.Texture);
            var xs = new List<double>();
            for (var index = 0; index < mesh.Positions.Count; index += 3)
            {
                Assert.AreEqual(0.1 + OmsiJunctionMarkings.Lift, mesh.Positions[index + 1], 1e-9);
                Assert.AreEqual(0.06, Math.Abs(mesh.Positions[index + 2]), 1e-9);
                xs.Add(mesh.Positions[index]);
            }
            Assert.IsTrue(xs.Any(x => Math.Abs(x - 9.0) < 1e-6) && xs.Any(x => Math.Abs(x - 3.0) < 1e-6));
            Assert.IsFalse(xs.Any(x => x < 8.99 && x > 3.01));
            // Every triangle faces up.
            for (var index = 0; index < mesh.Triangles.Count; index += 3)
            {
                double X(int vertex) => mesh.Positions[mesh.Triangles[index + vertex] * 3];
                double Z(int vertex) => mesh.Positions[mesh.Triangles[index + vertex] * 3 + 2];
                Assert.Greater((Z(1) - Z(0)) * (X(2) - X(0)) - (X(1) - X(0)) * (Z(2) - Z(0)), 0.0);
            }
        }

        [Test]
        public void CrossingLinesFallIntoPiecesThatCanBeHiddenOrRestyledAndGiveAnchors()
        {
            var layout = OmsiJunctionLayout.Build(Tee(), null);
            // East to west through the centre, and from the forward arm round to the right edge of the east arm, crossing it.
            var through = new OmsiMarkingLine { From = "1.0.1", To = "2.0.1", Style = OmsiMarkingStyle.LaneLine };
            var turn = new OmsiMarkingLine { From = "0.0.1", To = "1.0.2", Style = OmsiMarkingStyle.LaneBoundary };
            var lines = new List<OmsiMarkingLine> { through, turn };

            var anchors = OmsiJunctionMarkings.AllAnchors(layout, lines);

            var crossing = anchors.Single(anchor => anchor.IsCrossing);
            Assert.AreEqual("x0.0.1~1.0.2~1.0.1~2.0.1~0", crossing.Key);
            Assert.AreEqual(0.0, crossing.Y, 0.02);
            Assert.Greater(crossing.X, 0.0);
            var pieces = OmsiJunctionMarkings.Pieces(anchors, lines);
            Assert.AreEqual(2, pieces.Count(piece => piece.Line == 0));
            Assert.AreEqual(2, pieces.Count(piece => piece.Line == 1));
            // The through line's pieces meet at the crossing, 12 m minus its x from the east end.
            Assert.AreEqual(12.0 - crossing.X, pieces.First(piece => piece.Line == 0).To, 0.05);

            // The western piece hidden, the eastern one a stop line: nothing is drawn west of the crossing.
            through.Pieces.Add(new OmsiMarkingPieceSetting { At = 0.75, Hidden = true });
            through.Pieces.Add(new OmsiMarkingPieceSetting { At = 0.1, Style = OmsiMarkingStyle.StopLine });
            pieces = OmsiJunctionMarkings.Pieces(anchors, lines);
            Assert.IsTrue(pieces.Single(piece => piece.Line == 0 && piece.Middle > 0.5).Hidden);
            Assert.AreEqual(OmsiMarkingStyle.StopLine, pieces.Single(piece => piece.Line == 0 && piece.Middle < 0.5).Style);
            var meshes = new List<OmsiGeneratedMesh>();
            OmsiJunctionMarkings.AddMeshes(OmsiJunctionMarkings.Anchors(layout), lines, meshes);
            var mesh = meshes.Single();
            for (var index = 0; index < mesh.Positions.Count; index += 3)
                Assert.Greater(mesh.Positions[index], -0.5);

            // A line from the crossing is straight, and the settings survive the .sco.
            var fromCrossing = new OmsiMarkingLine { From = crossing.Key, To = "2.0.0", Style = OmsiMarkingStyle.EdgeLine };
            Assert.AreEqual(2, OmsiJunctionMarkings.Path(anchors, fromCrossing).Count);
            lines.Add(fromCrossing);
            var back = OmsiJunctionMarkings.Read(OmsiJunctionMarkings.Write(lines));
            Assert.AreEqual(3, back.Count);
            Assert.AreEqual(crossing.Key, back[2].From);
            Assert.AreEqual(2, back[0].Pieces.Count);
            Assert.IsTrue(back[0].Pieces[0].Hidden);
            Assert.IsNull(back[0].Pieces[0].Style);
            Assert.AreEqual(OmsiMarkingStyle.StopLine, back[0].Pieces[1].Style);
            Assert.IsFalse(back[0].Pieces[1].Hidden);
        }

        [Test]
        public void TheSuggestionFollowsTheRightOfWay()
        {
            var arms = Tee();
            // East and west are the main road; the side road comes in from the north.
            arms[0].Main = true;
            arms[1].Main = true;

            var lines = OmsiJunctionMarkings.Suggest(OmsiJunctionLayout.Build(arms, null));

            Assert.AreEqual(3, lines.Count);
            var through = lines.Single(line => line.Style == OmsiMarkingStyle.LaneLine);
            CollectionAssert.AreEquivalent(new[] { "1.0.1", "2.0.1" }, new[] { through.From, through.To });
            // The broken edge line runs along the north side of the main road, across the mouth of the side road.
            var edge = lines.Single(line => line.Style == OmsiMarkingStyle.BrokenEdgeLine);
            CollectionAssert.AreEquivalent(new[] { "1.0.0", "2.0.2" }, new[] { edge.From, edge.To });
            // The give-way line covers the lane coming in, left of the side road's centre as seen leaving it.
            var giveWay = lines.Single(line => line.Style == OmsiMarkingStyle.GiveWayLine);
            CollectionAssert.AreEquivalent(new[] { "0.1.0", "0.1.1" }, new[] { giveWay.From, giveWay.To });

            // Without a main road every arm gets a stop line.
            var equal = OmsiJunctionMarkings.Suggest(OmsiJunctionLayout.Build(Tee(), null));
            Assert.AreEqual(3, equal.Count(line => line.Style == OmsiMarkingStyle.StopLine));

            // Lanes narrower than the carriageway, as on many OMSI roads, still find the edge beyond them.
            var narrow = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile.Replace("-2\r\n0.1\r\n3.6", "-1.2\r\n0.1\r\n2").Replace("\r\n2\r\n0.1\r\n3.6", "\r\n1.2\r\n0.1\r\n2")),
                ProfilePath);
            var narrowArms = new[] { 90.0, 270.0, 0.0 }.Select(heading => new OmsiJunctionArm
            {
                HeadingDegrees = heading, ProfilePath = ProfilePath, Profile = narrow, Main = heading != 0.0
            }).ToList();
            var narrowGiveWay = OmsiJunctionMarkings.Suggest(OmsiJunctionLayout.Build(narrowArms, null)).Single(line => line.Style == OmsiMarkingStyle.GiveWayLine);
            CollectionAssert.AreEquivalent(new[] { "0.1.0", "0.1.1" }, new[] { narrowGiveWay.From, narrowGiveWay.To });
        }

        [Test]
        public void APedestrianCrossingAcrossAnArmLiesInTheArmAsBarsAlongTheTraffic()
        {
            var anchors = OmsiJunctionMarkings.Anchors(OmsiJunctionLayout.Build(Tee(), null));
            var meshes = new List<OmsiGeneratedMesh>();

            // Across the east arm at its mouth (x = 10), from its north edge to its south edge, 8 m.
            OmsiJunctionMarkings.AddMeshes(anchors, new[] { new OmsiMarkingLine { From = "1.1.0", To = "1.1.2", Style = OmsiMarkingStyle.Crosswalk } }, meshes);

            var mesh = meshes.Single();
            var points = Enumerable.Range(0, mesh.Positions.Count / 3).Select(index => new[] { mesh.Positions[index * 3], mesh.Positions[index * 3 + 2] }).ToList();
            // 4 m long into the arm, away from the junction.
            Assert.AreEqual(10.0, points.Min(point => point[0]), 1e-6);
            Assert.AreEqual(14.0, points.Max(point => point[0]), 1e-6);
            // Eight bars of 0.5 m, 0.5 m apart, the first at the north edge.
            Assert.AreEqual(8 * 4, points.Count);
            Assert.AreEqual(4.0, points.Max(point => point[1]), 1e-6);
            Assert.IsTrue(points.Any(point => Math.Abs(point[1] - 3.5) < 1e-6));
            Assert.IsFalse(points.Any(point => point[1] < 3.49 && point[1] > 3.01));
        }

        [Test]
        public void AnAreaIsHatchedWithinItsOutlineAndFollowsTheLinesRoundIt()
        {
            var layout = OmsiJunctionLayout.Build(Tee(), null);
            var anchors = OmsiJunctionMarkings.Anchors(layout);
            // The east arm between its end and its mouth: 2 m by 8 m.
            var area = new OmsiMarkingArea { Fill = OmsiMarkingFill.Hatched };
            area.Corners.AddRange(new[] { "1.0.0", "1.0.2", "1.1.2", "1.1.0" });
            var meshes = new List<OmsiGeneratedMesh>();

            OmsiJunctionMarkings.AddMeshes(anchors, new OmsiMarkingLine[0], meshes, new[] { area });

            var mesh = meshes.Single();
            Assert.AreEqual(OmsiJunctionMarkings.WhiteTexture, mesh.Texture);
            var covered = 0.0;
            for (var index = 0; index < mesh.Triangles.Count; index += 3)
            {
                double X(int vertex) => mesh.Positions[mesh.Triangles[index + vertex] * 3];
                double Z(int vertex) => mesh.Positions[mesh.Triangles[index + vertex] * 3 + 2];
                var up = (Z(1) - Z(0)) * (X(2) - X(0)) - (X(1) - X(0)) * (Z(2) - Z(0));
                Assert.Greater(up, 0.0);
                covered += up / 2.0;
            }
            for (var index = 0; index < mesh.Positions.Count; index += 3)
            {
                Assert.That(mesh.Positions[index], Is.InRange(10.0 - 1e-6, 12.0 + 1e-6));
                Assert.That(mesh.Positions[index + 2], Is.InRange(-4.0 - 1e-6, 4.0 + 1e-6));
                Assert.AreEqual(0.1 + OmsiJunctionMarkings.AreaLift, mesh.Positions[index + 1], 1e-9);
            }
            // Stripes of 0.5 m with 1 m between cover about a third of the 16 m².
            Assert.That(covered, Is.InRange(16.0 / 3.0 - 1.0, 16.0 / 3.0 + 1.0));
            // The stripes run at 45° to the longest side, which runs north to south.
            Assert.AreEqual(-45.0, OmsiJunctionMarkings.StripeDegrees(anchors, area), 1e-9);

            // A side between two corners on a line follows it: here the quarter circle about (12, 12) round the corner.
            var corner = new OmsiMarkingLine { From = "0.0.2", To = "1.0.0", Style = OmsiMarkingStyle.EdgeLine };
            var round = new OmsiMarkingArea();
            round.Corners.AddRange(new[] { "0.0.2", "1.0.0", "1.0.1" });
            var outline = OmsiJunctionMarkings.Outline(anchors, new[] { corner }, round);
            Assert.Greater(outline.Count, 6);
            var middle = outline[outline.Count / 3];
            Assert.AreEqual(8.0, Math.Sqrt((middle[0] - 12.0) * (middle[0] - 12.0) + (middle[1] - 12.0) * (middle[1] - 12.0)), 0.05);
            // Without the line it runs straight, and a missing corner gives none.
            Assert.AreEqual(3, OmsiJunctionMarkings.Outline(anchors, new OmsiMarkingLine[0], round).Count);
            round.Corners.Add("7.0.0");
            Assert.IsNull(OmsiJunctionMarkings.Outline(anchors, new[] { corner }, round));
        }

        [Test]
        public void AreasAreBuiltIntoTheJunctionAndReadBackFromItsFile()
        {
            var area = new OmsiMarkingArea { Fill = OmsiMarkingFill.YellowHatched, Angle = -30.0 };
            area.Corners.AddRange(new[] { "1.0.0", "1.0.2", "x0.0.1~1.0.2~1.0.1~2.0.1~0" });

            var made = OmsiJunctionGenerator.Junction(Tee(), null, "Areas", null, new[] { area });

            StringAssert.Contains("MapWorks marking areas: yellowhatched -30 1.0.0,1.0.2,x0.0.1~1.0.2~1.0.1~2.0.1~0", made.Sco);
            StringAssert.DoesNotContain("MapWorks markings:", made.Sco);
            var assetPath = OmsiGeneratedObjectStore.Write(installation, "Test", "Areas", made);
            var back = OmsiGeneratedJunction.Read(installation, assetPath, path => OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), path));
            Assert.AreEqual(0, back.Markings.Count);
            var read = back.Areas.Single();
            Assert.AreEqual(OmsiMarkingFill.YellowHatched, read.Fill);
            Assert.AreEqual(-30.0, read.Angle);
            CollectionAssert.AreEqual(area.Corners, read.Corners);
        }

        [Test]
        public void ArrowsLieInTheirLanePointingTheWayTheLaneRuns()
        {
            var layout = OmsiJunctionLayout.Build(Tee(), null);
            var anchors = OmsiJunctionMarkings.Anchors(layout);

            // East of the mouth of the east arm (x = 10): north of its centre the lane coming in, south the one going out.
            var lane = OmsiJunctionMarkings.LaneAt(anchors, 13.0, 2.0, 12.0);
            CollectionAssert.AreEqual(new[] { "1.1.0", "1.1.1" }, lane.Select(anchor => anchor.Key));
            Assert.IsTrue(OmsiJunctionMarkings.RunsIn(layout, lane[0], lane[1]));
            var outbound = OmsiJunctionMarkings.LaneAt(anchors, 13.0, -2.0, 12.0);
            CollectionAssert.AreEqual(new[] { "1.1.1", "1.1.2" }, outbound.Select(anchor => anchor.Key));
            Assert.IsFalse(OmsiJunctionMarkings.RunsIn(layout, outbound[0], outbound[1]));
            Assert.IsNull(OmsiJunctionMarkings.LaneAt(anchors, 30.0, 2.0, 12.0));

            // A left arrow coming in 1 m behind the mouth: it ends at x = 11 and turns south, to the left of westward traffic.
            var meshes = new List<OmsiGeneratedMesh>();
            var left = new OmsiMarkingArrow { From = "1.1.0", To = "1.1.1", Kind = OmsiMarkingArrowKind.Left };
            OmsiJunctionMarkings.AddMeshes(anchors, new OmsiMarkingLine[0], meshes, null, new[] { left });
            var mesh = meshes.Single();
            var xs = Enumerable.Range(0, mesh.Positions.Count / 3).Select(index => mesh.Positions[index * 3]).ToList();
            var ys = Enumerable.Range(0, mesh.Positions.Count / 3).Select(index => mesh.Positions[index * 3 + 2]).ToList();
            Assert.AreEqual(16.0, xs.Max(), 1e-6);
            Assert.Greater(xs.Min(), 11.0 - 1e-6);
            Assert.AreEqual(2.0 - 1.0, ys.Min(), 1e-6);
            Assert.AreEqual(2.0 + 0.185, ys.Max(), 1e-6);
            for (var index = 0; index < mesh.Positions.Count; index += 3)
                Assert.AreEqual(0.1 + OmsiJunctionMarkings.Lift, mesh.Positions[index + 1], 1e-9);
            for (var index = 0; index < mesh.Triangles.Count; index += 3)
            {
                double X(int vertex) => mesh.Positions[mesh.Triangles[index + vertex] * 3];
                double Z(int vertex) => mesh.Positions[mesh.Triangles[index + vertex] * 3 + 2];
                Assert.Greater((Z(1) - Z(0)) * (X(2) - X(0)) - (X(1) - X(0)) * (Z(2) - Z(0)), 0.0);
            }

            // Straight on, out of the junction: from 1 m behind the mouth away from it, the tip at x = 16.
            var straight = new OmsiMarkingArrow { From = "1.1.1", To = "1.1.2", Kind = OmsiMarkingArrowKind.Straight, Outward = true };
            var outline = OmsiJunctionMarkings.ArrowOutlines(anchors, straight);
            Assert.AreEqual(1, outline.Count);
            Assert.AreEqual(16.0, outline[0].Max(point => point[0]), 1e-6);
            Assert.AreEqual(11.0, outline[0][0][0], 1e-6);
            Assert.AreEqual(-2.0 + 0.185, outline[0][0][1], 1e-6);
            Assert.AreEqual(outline[0][0][1], outline[0][outline[0].Count - 1][1], 1e-9);
        }

        [Test]
        public void ArrowsAreShapedLikeGermanDirectionArrows()
        {
            // Straight on: the shaft narrows from 0.37 m at the tail to 0.2 m under a head 0.77 m wide, the tip at 5 m.
            var straight = OmsiJunctionMarkings.ArrowShape(OmsiMarkingArrowKind.Straight).Single();
            Assert.AreEqual(0.37, straight[1][0] - straight[0][0], 1e-6);
            Assert.AreEqual(0.77, straight.Max(point => point[0]) - straight.Min(point => point[0]), 1e-6);
            Assert.AreEqual(5.0, straight.Max(point => point[1]), 1e-6);

            // A turn alone is the shaft bent into a hook, its tall head a metre out; to the left it is the mirror image.
            var right = OmsiJunctionMarkings.ArrowShape(OmsiMarkingArrowKind.Right).Single();
            var left = OmsiJunctionMarkings.ArrowShape(OmsiMarkingArrowKind.Left).Single();
            Assert.AreEqual(1.0, right.Max(point => point[0]), 1e-6);
            Assert.AreEqual(-1.0, left.Min(point => point[0]), 1e-6);
            Assert.Less(right.Max(point => point[1]), 5.0);
            Assert.Greater(right.Max(point => point[1]) - right.Where(point => point[0] > 0.41).Min(point => point[1]), 2.0);

            // Beside straight on, the turn branches off lower with a smaller head.
            var parts = OmsiJunctionMarkings.ArrowShape(OmsiMarkingArrowKind.StraightRight);
            Assert.AreEqual(2, parts.Count);
            var tip = parts[1].OrderByDescending(point => point[0]).First();
            Assert.AreEqual(1.055, tip[0], 1e-6);
            Assert.AreEqual(2.05, tip[1], 1e-6);

            // The lines shown are the border of the whole arrow: on each a centimetre one way is paint, the other way road.
            var anchors = OmsiJunctionMarkings.Anchors(OmsiJunctionLayout.Build(Tee(), null));
            foreach (var kind in new[] { OmsiMarkingArrowKind.StraightRight, OmsiMarkingArrowKind.LeftRight, OmsiMarkingArrowKind.All })
            {
                var shape = OmsiJunctionMarkings.ArrowShape(kind);
                // Coming in on the east arm, 1 m behind its mouth: the tail at x = 16, pointing west, its right to the north.
                var lines = OmsiJunctionMarkings.ArrowOutlines(anchors, new OmsiMarkingArrow { From = "1.1.0", To = "1.1.1", Kind = kind });
                Assert.IsNotEmpty(lines);
                bool PaintedAt(double x, double y) => Painted(shape, y - 2.0, 16.0 - x);
                foreach (var line in lines)
                    for (var index = 0; index + 1 < line.Count; index++)
                    {
                        var a = line[index];
                        var b = line[index + 1];
                        var length = Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                        var x = (a[0] + b[0]) / 2.0;
                        var y = (a[1] + b[1]) / 2.0;
                        var sideX = (b[1] - a[1]) / length * 0.01;
                        var sideY = -(b[0] - a[0]) / length * 0.01;
                        Assert.AreNotEqual(PaintedAt(x + sideX, y + sideY), PaintedAt(x - sideX, y - sideY), kind + " at " + x + ", " + y);
                    }
            }
        }

        private static bool Painted(List<List<double[]>> parts, double x, double y)
        {
            foreach (var ring in parts)
            {
                var inside = false;
                for (int index = 0, previous = ring.Count - 1; index < ring.Count; previous = index++)
                    if (ring[index][1] > y != ring[previous][1] > y &&
                        x < (ring[previous][0] - ring[index][0]) * (y - ring[index][1]) / (ring[previous][1] - ring[index][1]) + ring[index][0])
                        inside = !inside;
                if (inside)
                    return true;
            }
            return false;
        }

        [Test]
        public void ArrowsAreBuiltIntoTheJunctionAndReadBackFromItsFile()
        {
            var arrows = new[]
            {
                new OmsiMarkingArrow { From = "1.1.0", To = "1.1.1", Kind = OmsiMarkingArrowKind.StraightLeft, Distance = 2.5 },
                new OmsiMarkingArrow { From = "1.1.1", To = "1.1.2", Kind = OmsiMarkingArrowKind.All, Outward = true }
            };

            var made = OmsiJunctionGenerator.Junction(Tee(), null, "Arrows", null, null, arrows);

            StringAssert.Contains("MapWorks marking arrows: 1.1.0~1.1.1 straightleft 2.5; 1.1.1~1.1.2 all 1 out", made.Sco);
            StringAssert.Contains(OmsiJunctionMarkings.WhiteTexture, made.Model);
            var assetPath = OmsiGeneratedObjectStore.Write(installation, "Test", "Arrows", made);
            var back = OmsiGeneratedJunction.Read(installation, assetPath, path => OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(Profile), path));
            Assert.AreEqual(2, back.Arrows.Count);
            Assert.AreEqual(OmsiMarkingArrowKind.StraightLeft, back.Arrows[0].Kind);
            Assert.AreEqual(2.5, back.Arrows[0].Distance);
            Assert.IsFalse(back.Arrows[0].Outward);
            Assert.AreEqual("1.1.2", back.Arrows[1].To);
            Assert.IsTrue(back.Arrows[1].Outward);
            Assert.AreEqual(0, back.Areas.Count);
        }

        [Test]
        public void CircularCurvatureIsAThirdStraightOnAndAboutAHalfForAQuarterTurn()
        {
            Assert.AreEqual(1.0 / 3.0, OmsiJunctionMarkings.CircularCurvature(0, 1, 0, 1), 1e-9);
            Assert.AreEqual(4.0 / 3.0 * Math.Tan(Math.PI / 8.0) / (2.0 * Math.Sin(Math.PI / 4.0)), OmsiJunctionMarkings.CircularCurvature(0, 1, 1, 0), 1e-9);
        }
    }
}
