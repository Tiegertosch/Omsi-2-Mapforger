using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiRoadTests
    {
        /// <summary>Terrain given by a function of the world position; positions with no value lie outside the terrain.</summary>
        private sealed class FakeTerrain : IOmsiTerrainHeights
        {
            private readonly Func<double, double, double?> height;

            public FakeTerrain(Func<double, double, double?> height)
            {
                this.height = height;
            }

            public bool TryGetHeight(double worldX, double worldY, out double value)
            {
                var result = height(worldX, worldY);
                value = result ?? 0.0;
                return result.HasValue;
            }
        }

        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            mapFolder = Path.Combine(Path.GetTempPath(), "mapforge-roads-" + Guid.NewGuid().ToString("N"), "maps", "Roadtown");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            // Spline 10 runs 50 m north from 100, 100 of tile 0, 0; spline 11 carries a lamp row and cannot be deleted.
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "10", "0", "0", "100", "2", "100", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "11", "0", "0", "200", "0", "100", "0", "30", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[splineAttachement]", "0", "lamp.sco", "50", "1", "2", "0", "0", "0", "0", "0", "10", "30", "0", "0", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "");
            // The terrain rises 5 % to the east: 0.05 m per metre of world x.
            WriteTerrain("tile_0_0.map.terrain", 0);
            WriteTerrain("tile_1_0.map.terrain", 1);
            // The road profile in the installation two folders up: 8 m wide with kerbs 0.15 m high at its edges.
            var splines = Path.Combine(Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName, "Splines");
            Directory.CreateDirectory(splines);
            File.WriteAllText(Path.Combine(splines, "road.sli"),
                "[texture]\r\nroad.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-4\r\n0.15\r\n0\r\n1\r\n\r\n[profilepnt]\r\n4\r\n0.15\r\n1\r\n1\r\n");
        }

        [TearDown]
        public void DeleteMap()
        {
            var root = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void StraightRoadOnFlatTerrainIsSplitIntoEqualPiecesEndingAtThePoint()
        {
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 60, MaximumPieceLength = 25 },
                new FakeTerrain((x, y) => 3.0));

            Assert.IsFalse(plan.HasErrors, string.Join("; ", plan.Issues));
            Assert.AreEqual(3, plan.Pieces.Count);
            Assert.AreEqual(60.0, plan.LengthMeters, 1e-9);
            foreach (var piece in plan.Pieces)
            {
                Assert.AreEqual(20.0, piece.LengthMeters, 1e-9);
                Assert.AreEqual(0.0, piece.RadiusMeters);
                Assert.AreEqual(0.0, piece.HeadingDegrees, 1e-9);
                Assert.AreEqual(3.0, piece.StartHeight, 1e-9);
                Assert.AreEqual(0.0, piece.GradientStartPercent, 1e-9);
            }
            Assert.AreEqual(40.0, plan.Pieces[2].StartY, 1e-9);
            plan.Pieces[2].PointAt(20.0, out var endX, out var endY, out var endHeight);
            Assert.AreEqual(0.0, endX, 1e-9);
            Assert.AreEqual(60.0, endY, 1e-9);
            Assert.AreEqual(3.0, endHeight, 1e-9);
            CollectionAssert.IsEmpty(plan.Issues);
        }

        [Test]
        public void FollowingRisingFallingAndIrregularTerrainPutsEveryPieceBoundaryOnTheGround()
        {
            var rising = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 100 }, new FakeTerrain((x, y) => 0.05 * y));
            Assert.AreEqual(4, rising.Pieces.Count);
            Assert.IsTrue(rising.Pieces.All(piece => Math.Abs(piece.GradientStartPercent - 5.0) < 1e-9));
            Assert.AreEqual(5.0, rising.EndHeight, 1e-9);
            Assert.AreEqual(5.0, rising.MaximumGradientPercent, 1e-9);
            CollectionAssert.IsEmpty(rising.Issues);

            // 12 % down is steeper than the warning threshold of 10 %.
            var falling = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 50 }, new FakeTerrain((x, y) => -0.12 * y));
            Assert.AreEqual(-12.0, falling.Pieces[0].GradientStartPercent, 1e-9);
            Assert.IsFalse(falling.HasErrors);
            Assert.That(falling.Issues.Select(issue => issue.Message), Has.All.Contains("Gradient of 12.0 % is steeper than 10.0 %"));

            Func<double, double, double?> bumpy = (x, y) => 4.0 + Math.Sin(y / 7.0) * 2.0;
            var irregular = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 90, MaximumPieceLength = 10 },
                new FakeTerrain(bumpy));
            Assert.AreEqual(9, irregular.Pieces.Count);
            foreach (var piece in irregular.Pieces)
            {
                Assert.AreEqual(bumpy(piece.StartX, piece.StartY).Value, piece.StartHeight, 1e-9);
                piece.PointAt(piece.LengthMeters, out var x, out var y, out var height);
                Assert.AreEqual(bumpy(x, y).Value, height, 1e-9);
            }
        }

        [Test]
        public void ManualHeightsRunEvenlyAndAreCheckedAgainstTheTerrain()
        {
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, EndX = 0, EndY = 60, StartHeight = 10, EndHeight = 16, HeightMode = OmsiRoadHeightMode.Manual
            }, new FakeTerrain((x, y) => 0.0));

            Assert.IsTrue(plan.Pieces.All(piece => Math.Abs(piece.GradientStartPercent - 10.0) < 1e-9));
            Assert.AreEqual(16.0, plan.EndHeight, 1e-9);
            // 10 % is not steeper than the threshold; 10 to 16 m over flat ground at 0 is noted as high above it.
            Assert.That(plan.Issues.Select(issue => issue.ToString()), Is.EquivalentTo(new[] { "Note: The road runs up to 16.0 m above the terrain." }));

            var buried = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, EndX = 0, EndY = 60, StartHeight = 0, EndHeight = 0, HeightMode = OmsiRoadHeightMode.Manual
            }, new FakeTerrain((x, y) => y < 20 || y > 40 ? 0.0 : 2.0));
            Assert.That(buried.Issues.Select(issue => issue.Message), Has.Some.Contains("runs up to 2.0 m below the terrain"));
            Assert.IsFalse(buried.HasErrors);
        }

        [Test]
        public void SmoothRoadsKeepHeightAndGradientContinuousAndContinueAJoinedRoad()
        {
            Func<double, double, double?> bumpy = (x, y) => 4.0 + Math.Sin(y / 9.0) * 3.0 + y * 0.04;
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, EndX = 0, EndY = 200, HeightMode = OmsiRoadHeightMode.Smooth, StartHeight = 4, StartGradientPercent = 6
            }, new FakeTerrain(bumpy));
            Assert.IsFalse(plan.HasErrors, string.Join("; ", plan.Issues));
            Assert.AreEqual(8, plan.Pieces.Count);
            Assert.AreEqual(4.0, plan.Pieces[0].StartHeight, 1e-12);
            Assert.AreEqual(6.0, plan.Pieces[0].GradientStartPercent, 1e-9);
            for (var index = 0; index + 1 < plan.Pieces.Count; index++)
            {
                Assert.AreEqual(plan.Pieces[index].EndHeight, plan.Pieces[index + 1].StartHeight, 1e-9, "height at boundary " + index);
                Assert.AreEqual(plan.Pieces[index].GradientEndPercent, plan.Pieces[index + 1].GradientStartPercent, 1e-9, "gradient at boundary " + index);
            }
            // The bumps of 3 m every 57 m are averaged out: the steepest gradient stays far below that of following the
            // terrain in steps, which climbs and falls with every bump.
            var stepped = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 200, StartHeight = 4 },
                new FakeTerrain(bumpy));
            Assert.Less(plan.MaximumGradientPercent, stepped.MaximumGradientPercent / 2.0);
            Assert.Less(plan.MaximumGradientPercent, 12.0);
            // The smooth road stays near the ground on average, within the bumps.
            plan.Pieces[4].PointAt(0.0, out var x, out var y, out var height);
            Assert.AreEqual(bumpy(x, y).Value, height, 3.5);

            // Joining a road at the end keeps the end height exactly.
            var joined = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, EndX = 0, EndY = 80, HeightMode = OmsiRoadHeightMode.Smooth, StartHeight = 4, EndHeight = 9
            }, new FakeTerrain(bumpy));
            Assert.AreEqual(9.0, joined.EndHeight, 1e-9);
            Assert.AreEqual(9.0, joined.Pieces[joined.Pieces.Count - 1].EndHeight, 1e-9);

            // Following the terrain in steps notes the kink where it continues a road with another gradient.
            var kinked = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, EndX = 0, EndY = 50, StartHeight = 4, StartGradientPercent = 8
            }, new FakeTerrain((px, py) => 4.0));
            Assert.That(kinked.Issues.Select(issue => issue.ToString()), Has.Some.Contains("Note: The gradient changes from +8.0 % to +0.0 %"));
        }

        [Test]
        public void SplineHeightsFollowTheirGradientsAndEndHeight()
        {
            var spline = new OmsiSplineRecord { Height = 1, LengthMeters = 100, GradientStartPercent = 0, GradientEndPercent = 4, EndHeight = 3 };
            Assert.AreEqual(1.0, spline.HeightAt(0), 1e-12);
            Assert.AreEqual(1.0 + 50 * (0 + 2) / 200.0, spline.HeightAt(50), 1e-12);
            Assert.AreEqual(3.0, spline.HeightAt(100), 1e-12);
            Assert.AreEqual(4.0, spline.GradientAtPercent(100), 1e-12);
            // A [spline_h] whose stated end height differs: corrected evenly along the spline.
            var stated = new OmsiSplineRecord { Height = 1, LengthMeters = 100, GradientStartPercent = 0, GradientEndPercent = 4, EndHeight = 4 };
            Assert.AreEqual(4.0, stated.HeightAt(100), 1e-12);
            Assert.AreEqual(5.0, stated.GradientAtPercent(100), 1e-12);
        }

        [Test]
        public void ArcLeavesTangentiallyAndReachesThePoint()
        {
            // Heading north from 0, 0 to 20, 20 turns right by 90° on a radius of 20 m.
            OmsiRoadPlanner.TangentArc(0, 0, 0, 20, 20, out var length, out var radius, out var endHeading);
            Assert.AreEqual(20.0, radius, 1e-9);
            Assert.AreEqual(10.0 * Math.PI, length, 1e-9);
            Assert.AreEqual(90.0, endHeading, 1e-9);

            OmsiRoadPlanner.TangentArc(0, 0, 0, -20, 20, out _, out var leftRadius, out var leftHeading);
            Assert.AreEqual(-20.0, leftRadius, 1e-9);
            Assert.AreEqual(-90.0, leftHeading, 1e-9);

            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 20, EndY = 20 },
                new FakeTerrain((x, y) => 1.0));
            Assert.AreEqual(2, plan.Pieces.Count);
            Assert.AreEqual(45.0, plan.Pieces[1].HeadingDegrees, 1e-9);
            plan.Pieces[1].PointAt(plan.Pieces[1].LengthMeters, out var x, out var y, out _);
            Assert.AreEqual(20.0, x, 1e-9);
            Assert.AreEqual(20.0, y, 1e-9);
            Assert.AreEqual(90.0, plan.EndHeadingDegrees, 1e-9);
        }

        [Test]
        public void ContinuedRoadKeepsHeightAndHeadingAndMeetsJoinedRoadsWithoutAKink()
        {
            var terrain = new FakeTerrain((x, y) => 0.02 * y);
            var first = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 40 }, terrain);
            var second = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 40, StartHeadingDegrees = first.EndHeadingDegrees, StartHeight = first.EndHeight, EndX = 30, EndY = 70
            }, terrain);
            Assert.AreEqual(first.EndHeight, second.Pieces[0].StartHeight, 1e-12);
            Assert.AreEqual(first.EndHeadingDegrees, second.Pieces[0].HeadingDegrees, 1e-12);
            CollectionAssert.IsEmpty(second.Issues.Where(issue => issue.Message.Contains("kink")));

            // Joining a road that continues east at the end: arriving at 90° fits one arc, which both arcs then lie on.
            var smooth = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 20, EndY = 20, EndJoinHeadingDegrees = 90, EndHeight = 7
            }, terrain);
            CollectionAssert.IsEmpty(smooth.Issues.Where(issue => issue.Message.Contains("kink")));
            Assert.AreEqual(7.0, smooth.EndHeight, 1e-12);
            Assert.IsTrue(smooth.Pieces.All(piece => Math.Abs(piece.RadiusMeters - 20.0) < 1e-9));
            AssertSmooth(smooth, 20, 20, 90);
            // Arriving at 45° no single arc fits: two arcs meet both headings, where one arc used to leave a kink of 45°.
            var twoArcs = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 20, EndY = 20, EndJoinHeadingDegrees = 45, EndHeight = 7
            }, terrain);
            CollectionAssert.IsEmpty(twoArcs.Issues.Where(issue => issue.Message.Contains("kink")));
            Assert.AreEqual(45.0, twoArcs.EndHeadingDegrees, 1e-9);
            Assert.AreEqual(7.0, twoArcs.EndHeight, 1e-9);
            AssertSmooth(twoArcs, 20, 20, 45);
            // A road beside the joined one and parallel to it: an S-curve of two arcs turning opposite ways.
            var sideways = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 10, EndY = 60, EndJoinHeadingDegrees = 0, HeightMode = OmsiRoadHeightMode.Smooth,
                EndHeight = 1.2
            }, terrain);
            CollectionAssert.IsEmpty(sideways.Issues.Where(issue => issue.Severity != OmsiRoadIssueSeverity.Note));
            Assert.Greater(sideways.Pieces.First().RadiusMeters, 0.0);
            Assert.Less(sideways.Pieces.Last().RadiusMeters, 0.0);
            Assert.AreEqual(1.2, sideways.EndHeight, 1e-9);
            AssertSmooth(sideways, 10, 60, 0);
            // In line with both headings the road stays straight.
            var straight = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 0, EndY = 30, EndJoinHeadingDegrees = 0
            }, terrain);
            Assert.IsTrue(straight.Pieces.All(piece => piece.RadiusMeters == 0.0));
            AssertSmooth(straight, 0, 30, 0);
            // A point straight behind would need an endless radius, with one arc and with two: refused, nothing is built.
            var behind = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 0, EndY = -30 }, terrain);
            Assert.IsTrue(behind.HasErrors);
            CollectionAssert.IsEmpty(behind.Pieces);
            var behindJoined = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 0, EndY = -30, EndJoinHeadingDegrees = 0
            }, terrain);
            Assert.That(behindJoined.Issues.Select(issue => issue.Message), Has.Some.Contains("straight behind"));
            CollectionAssert.IsEmpty(behindJoined.Pieces);
            // A curve tighter than half the road's width folds its inner edge over: reported, still built.
            var tight = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 8, EndY = 0, EndJoinHeadingDegrees = 180, HalfWidthMeters = 5.5
            }, terrain);
            Assert.That(tight.Issues.Select(issue => issue.Message), Has.Some.Contains("tighter than half the road's width (5.5 m)"));
            AssertSmooth(tight, 8, 0, 180);
            Assert.IsFalse(OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 20, EndY = 0, EndJoinHeadingDegrees = 180, HalfWidthMeters = 5.5
            }, terrain).Issues.Exists(issue => issue.Message.Contains("tighter")));

            // With a free start the road is planned from the joined end: it arrives along the joined road and starts at the
            // requested point, its pieces running from start to end and their heights meeting.
            var backwards = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, EndX = 20, EndY = 20, EndJoinHeadingDegrees = 90, EndHeight = 7, MaximumPieceLength = 10
            }, terrain);
            CollectionAssert.IsEmpty(backwards.Issues.Where(issue => issue.Message.Contains("kink")));
            Assert.AreEqual(0.0, backwards.Pieces[0].StartX, 1e-12);
            Assert.AreEqual(0.0, backwards.Pieces[0].StartY, 1e-12);
            var last = backwards.Pieces[backwards.Pieces.Count - 1];
            last.PointAt(last.LengthMeters, out var endX, out var endY, out var endHeight);
            Assert.AreEqual(20.0, endX, 1e-6);
            Assert.AreEqual(20.0, endY, 1e-6);
            Assert.AreEqual(7.0, endHeight, 1e-9);
            OmsiMapGeometry.GetLocalFrameAt(last.AsRecord(), last.LengthMeters, out _, out var arrival);
            Assert.AreEqual(90.0, OmsiRoadPlanner.Normalize(arrival * 180.0 / Math.PI), 1e-6);
            for (var index = 1; index < backwards.Pieces.Count; index++)
            {
                backwards.Pieces[index - 1].PointAt(backwards.Pieces[index - 1].LengthMeters, out var x, out var y, out var height);
                Assert.AreEqual(backwards.Pieces[index].StartX, x, 1e-6);
                Assert.AreEqual(backwards.Pieces[index].StartY, y, 1e-6);
                Assert.AreEqual(backwards.Pieces[index].StartHeight, height, 1e-9);
            }
        }

        [Test]
        public void TerrainOutsideAndDegenerateInputAreErrorsNotZeroHeights()
        {
            var outside = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 100 },
                new FakeTerrain((x, y) => y <= 50 ? 1.0 : (double?)null));
            Assert.IsTrue(outside.HasErrors);
            Assert.That(outside.Issues.Select(issue => issue.Message), Has.Some.Contains("leaves the terrain"));
            // The preview keeps the last known height instead of falling to 0.
            Assert.AreEqual(1.0, outside.EndHeight, 1e-12);

            var noStart = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 0, StartY = 0, EndX = 0, EndY = 30 }, null);
            Assert.IsTrue(noStart.HasErrors);

            var tooShort = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 5, StartY = 5, EndX = 5.1, EndY = 5 }, new FakeTerrain((x, y) => 0.0));
            Assert.IsTrue(tooShort.HasErrors);
            CollectionAssert.IsEmpty(tooShort.Pieces);

            var invalid = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = double.NaN, StartY = 0, EndX = 10, EndY = 0 }, new FakeTerrain((x, y) => 0.0));
            Assert.IsTrue(invalid.HasErrors);
            CollectionAssert.IsEmpty(invalid.Pieces);
        }

        [Test]
        public void TerrainSamplerReadsTilesAndReportsPositionsWithoutTerrain()
        {
            var source = new OmsiFolderSource(mapFolder);
            var sampler = new OmsiTerrainSampler(OmsiMapReader.Read(source), source);
            Assert.IsTrue(sampler.TryGetHeight(100, 50, out var height));
            Assert.AreEqual(5.0, height, 1e-4);
            Assert.IsTrue(sampler.TryGetHeight(450, 50, out var east));
            Assert.AreEqual(22.5, east, 1e-4);
            Assert.IsFalse(sampler.TryGetHeight(-10, 50, out _));
            Assert.IsFalse(sampler.TryGetHeight(100, 400, out _));
            Assert.IsFalse(sampler.TryGetHeight(double.NaN, 0, out _));
        }

        [Test]
        public void RoadJoinsAFreeSplineEndAcrossTilesAndUndoRestoresTheFiles()
        {
            var session = OpenProject();
            var summary = session.Read();
            var originalTile = File.ReadAllBytes(Path.Combine(mapFolder, "tile_0_0.map"));
            var end = OmsiSplineEnd.FindFree(summary, 100, 151, 2.0);
            Assert.IsNotNull(end);
            Assert.IsTrue(end.AtEnd);
            Assert.AreEqual(10, end.Spline.Id);
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(end.OutwardHeadingDegrees), 1e-9);

            // From the end of spline 10 at 100, 150 east across the tile border to 340, 150, at the spline's height of 2 m.
            var start = end.Position(summary.TileSizeMeters);
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = start.X, StartY = start.Y, StartHeadingDegrees = end.OutwardHeadingDegrees, StartHeight = end.Height, EndX = 340, EndY = 150,
                HeightMode = OmsiRoadHeightMode.Manual, EndHeight = 2
            }, new OmsiTerrainSampler(summary, session.Source));
            var change = session.CreateRoad(summary, plan, @"Splines\road.sli", end, null);

            Assert.AreEqual(plan.Pieces.Count, change.SplineIds.Count);
            CollectionAssert.AreEqual(Enumerable.Range(100, plan.Pieces.Count), change.SplineIds);
            var all = summary.Tiles.SelectMany(tile => tile.Splines).ToList();
            var created = change.SplineIds.Select(id => all.Single(spline => spline.Id == id)).ToList();
            Assert.AreEqual(10, created[0].PreviousId);
            Assert.AreEqual(created[1].Id, created[0].NextId);
            Assert.AreEqual(0, created[created.Count - 1].NextId);
            Assert.AreEqual(100, all.Single(spline => spline.Id == 10).NextId);
            Assert.IsTrue(summary.Tiles[1].Splines.Count > 0, "pieces starting east of x 300 belong to tile 1, 0");
            Assert.AreEqual(summary.Tiles[0].Splines.Count + summary.Tiles[1].Splines.Count, summary.SplineCount);
            Assert.AreEqual(100 + plan.Pieces.Count, summary.NextIdCode);

            // The written files read back with the same geometry, and every join meets.
            var reread = session.Read();
            Assert.AreEqual(summary.SplineCount, reread.SplineCount);
            var continuity = OmsiSplineContinuity.Check(reread);
            // The pieces join each other and the first joins spline 10: one link per piece, all within 0.5 m.
            Assert.AreEqual(plan.Pieces.Count, continuity.LinkCount);
            Assert.AreEqual(continuity.LinkCount, continuity.ConnectedCount,
                string.Join("; ", continuity.Gaps.Select(gap => gap.SplineId + "-" + gap.NeighbourId + " " + gap.DistanceMeters)));
            Assert.AreEqual(0, continuity.MissingNeighbourCount);
            Assert.IsTrue(continuity.LargestGapMeters < 1e-6, continuity.LargestGapMeters.ToString());
            Assert.AreEqual(100 + plan.Pieces.Count, reread.NextIdCode);
            // The original map is never touched.
            CollectionAssert.AreEqual(originalTile, File.ReadAllBytes(Path.Combine(mapFolder, "tile_0_0.map")));

            session.Undo(summary, change);
            Assert.AreEqual(2, summary.Tiles[0].Splines.Count);
            Assert.AreEqual(0, summary.Tiles[1].Splines.Count);
            Assert.AreEqual(0, summary.Tiles[0].Splines.Single(spline => spline.Id == 10).NextId);
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));
            session.Redo(summary, change);
            Assert.AreEqual(100, summary.Tiles[0].Splines.Single(spline => spline.Id == 10).NextId);
        }

        [Test]
        public void DeletingASplineOpensItsNeighboursAndRidersOrReferencesBlockIt()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 170, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 2
            }, null);
            var road = session.CreateRoad(summary, plan, @"Splines\road.sli", end, null);
            var newSpline = summary.Tiles[0].Splines.Single(spline => spline.Id == road.SplineIds[0]);

            // Spline 10 is now joined to the new spline; deleting spline 10 opens the new spline's start.
            var spline10 = summary.Tiles[0].Splines.Single(spline => spline.Id == 10);
            var deleted = session.DeleteSpline(summary, summary.Tiles[0], spline10);
            Assert.IsTrue(deleted.Succeeded);
            Assert.IsFalse(summary.Tiles[0].Splines.Any(spline => spline.Id == 10));
            Assert.AreEqual(0, summary.Tiles[0].Splines.Single(spline => spline.Id == newSpline.Id).PreviousId);
            // The lamp row on spline 11 still names spline 11, now the first spline of the tile.
            var tileFile = OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            Assert.AreEqual(0, OmsiTileRecords.Of<OmsiSplineAttachmentView>(tileFile).Single().SplineIndex);
            Assert.AreEqual(11, OmsiTileRecords.Of<OmsiSplineView>(tileFile).First().Id);

            var blocked = session.DeleteSpline(summary, summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 11));
            Assert.IsFalse(blocked.Succeeded);
            Assert.That(blocked.Blockers.Select(reference => reference.Kind), Has.Some.EqualTo("attachment on spline"));
            Assert.IsTrue(summary.Tiles[0].Splines.Any(spline => spline.Id == 11));

            session.Undo(summary, deleted.Change);
            Assert.AreEqual(10, summary.Tiles[0].Splines.Single(spline => spline.Id == newSpline.Id).PreviousId);
            Assert.AreEqual(newSpline.Id, summary.Tiles[0].Splines.Single(spline => spline.Id == 10).NextId);
        }

        [Test]
        public void RoadsCannotJoinTakenEndsOrStartOutsideTheTiles()
        {
            var session = OpenProject();
            var summary = session.Read();
            var outside = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = -50, StartY = 10, StartHeight = 0, EndX = 50, EndY = 10, HeightMode = OmsiRoadHeightMode.Manual
            }, null);
            Assert.Throws<InvalidOperationException>(() => session.CreateRoad(summary, outside, @"Splines\road.sli", null, null));
            Assert.IsFalse(session.IsTileModified("tile_0_0.map"));

            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeight = 2, EndX = 100, EndY = 170, HeightMode = OmsiRoadHeightMode.Manual
            }, null);
            session.CreateRoad(summary, plan, @"Splines\road.sli", end, null);
            var taken = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 10), true);
            Assert.IsFalse(taken.IsFree);
            Assert.Throws<InvalidOperationException>(() => session.CreateRoad(summary, plan, @"Splines\road.sli", taken, null));
            Assert.IsNull(OmsiSplineEnd.FindFree(summary, 100, 150, 1.0));
        }

        [Test]
        public void TerrainLiesUnderTheRoadKeepsAShoulderAndSlopesBackToTheGround()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(
                "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.15\r\n0\r\n1\r\n[profilepnt]\r\n4\r\n0.15\r\n1\r\n1\r\n"), @"Splines\road.sli");
            Assert.AreEqual(-4.0, profile.LeftEdge);
            Assert.AreEqual(4.0, profile.RightEdge);
            Assert.AreEqual(0.15, profile.EdgeHeight, 1e-12);
            var tile = new OmsiTileSummary();
            // A road 2 m high running north from 150, 100 to 150, 200 over flat ground at 0: an embankment.
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 150, StartY = 100, StartHeight = 2, EndX = 150, EndY = 200, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual,
                MaximumPieceLength = 200
            }, null);
            var spline = plan.Pieces[0].AsRecord();
            var road = new OmsiShapedRoad(tile, spline, profile, 300);
            var flat = new float[OmsiTerrain.Resolution * OmsiTerrain.Resolution];
            var changed = OmsiTerrainShaping.Shape(flat, 0, 0, 300, new[] { road }, new OmsiTerrainShapeSettings());
            Assert.Greater(changed, 0);
            // Grid points every 5 m: column 30 is x 150 under the road, columns 31 and 32 are 1 m and 6 m outside its edge on
            // the shoulder, which reaches a grid diagonal of 7.07 m; all lie 5 cm below the kerb, as OMSI cuts the terrain
            // away under the road.
            Assert.AreEqual(2.10, Height(flat, 30, 30), 1e-5);
            Assert.AreEqual(2.10, Height(flat, 31, 30), 1e-5);
            Assert.AreEqual(2.10, Height(flat, 32, 30), 1e-5);
            // 11 m outside: the slope of 1:1.5 from the shoulder would end below the ground, so the ground stays.
            Assert.AreEqual(0.0, Height(flat, 33, 30), 1e-5);
            // Beyond the north end on its line the shoulder and slopes round off around the end: flat at 5 m, ground at 15 m.
            Assert.AreEqual(2.10, Height(flat, 30, 41), 1e-5);
            Assert.AreEqual(0.0, Height(flat, 30, 43), 1e-5);
            // Far away nothing changes.
            Assert.AreEqual(0.0, Height(flat, 0, 0), 0.0);

            // The same road through a hill at 6 m: a cutting.
            var hill = Enumerable.Repeat(6f, OmsiTerrain.Resolution * OmsiTerrain.Resolution).ToArray();
            OmsiTerrainShaping.Shape(hill, 0, 0, 300, new[] { road }, new OmsiTerrainShapeSettings());
            Assert.AreEqual(2.10, Height(hill, 30, 30), 1e-5);
            Assert.AreEqual(2.10, Height(hill, 32, 30), 1e-5);
            Assert.AreEqual(2.10 + (11.0 - 5.0 * Math.Sqrt(2.0)) / 1.5, Height(hill, 33, 30), 1e-5);
            Assert.AreEqual(6.0, Height(hill, 36, 30), 1e-5);
        }

        [Test]
        public void TerrainBetweenRoadsAtDifferentHeightsRampsFromKerbToKerbInsteadOfLeavingAWall()
        {
            var profile = OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(
                "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.15\r\n0\r\n1\r\n[profilepnt]\r\n4\r\n0.15\r\n1\r\n1\r\n"), @"Splines\road.sli");
            var tile = new OmsiTileSummary();
            // Two roads running north 20 m apart, at 2 m and 6 m: their kerbs are 12 m apart, closer than two shoulders.
            OmsiShapedRoad Road(double x, double height) => new OmsiShapedRoad(tile, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = x, StartY = 100, StartHeight = height, EndX = x, EndY = 200, EndHeight = height, HeightMode = OmsiRoadHeightMode.Manual,
                MaximumPieceLength = 200
            }, null).Pieces[0].AsRecord(), profile, 300);
            var low = Road(150, 2);
            var high = Road(170, 6);
            foreach (var order in new[] { new[] { low, high }, new[] { high, low } })
            {
                var heights = Enumerable.Repeat(4f, OmsiTerrain.Resolution * OmsiTerrain.Resolution).ToArray();
                OmsiTerrainShaping.Shape(heights, 0, 0, 300, order, new OmsiTerrainShapeSettings());
                // No height lies within both slopes: the terrain runs from kerb to kerb, 1 m from one and 11 m from the other
                // close to the near kerb, halfway in between, whichever road comes first.
                Assert.AreEqual((2.10 / 1.0 + 6.10 / 11.0) / (1.0 + 1.0 / 11.0), Height(heights, 31, 30), 1e-5);
                Assert.AreEqual((6.10 / 1.0 + 2.10 / 11.0) / (1.0 + 1.0 / 11.0), Height(heights, 33, 30), 1e-5);
                Assert.AreEqual(4.10, Height(heights, 32, 30), 1e-5);
            }
        }

        [Test]
        public void RoadsWithTerrainAlignmentFitTheTerrainAndLeaveTheOriginalAlone()
        {
            var session = OpenProject();
            var summary = session.Read();
            var originalTerrain = File.ReadAllBytes(Path.Combine(mapFolder, "tile_0_0.map.terrain"));
            // 2 m above the 5 % slope at x 100, which lies at 5 m: a cutting through the rising ground to the east.
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 200, StartHeight = 2, EndX = 100, EndY = 260, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null);
            var change = session.CreateRoad(summary, plan, @"Splines\road.sli", null, null, true);

            CollectionAssert.AreEquivalent(new[] { summary.Tiles[0], summary.Tiles[1] }.Where(tile => tile.X == 0), change.TerrainTiles);
            Assert.IsTrue(session.Workspace.IsModified("tile_0_0.map.terrain"));
            var terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            Assert.AreEqual(2.10, terrain[20, 46], 1e-4);
            Assert.AreEqual(2.10, terrain[21, 46], 1e-4);
            // Far from the road the ground is as it was.
            Assert.AreEqual(0.05 * 250, terrain[50, 46], 1e-4);
            Assert.IsTrue(summary.Tiles[0].Splines.Where(spline => change.SplineIds.Contains(spline.Id)).All(spline => spline.AlignsTerrain));
            var tileFile = OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            Assert.AreEqual(plan.Pieces.Count, tileFile.Records.Count(record => record.Name == "spline_terrain_align_2" && record.GetField(0) == "1"));
            // Splines of the original map keep the terrain they have.
            Assert.IsFalse(session.ShapesTerrain(summary.Tiles[0].Splines.Single(spline => spline.Id == 10)));

            // Fitting all roads again gives the same terrain.
            var fitted = session.Workspace.ReadAllBytes("tile_0_0.map.terrain");
            CollectionAssert.IsEmpty(session.RefitAllTerrain(summary));
            CollectionAssert.AreEqual(fitted, session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            // After the way of fitting changed, fitting again reshapes the tile, and changing it back restores it.
            session.TerrainShape.ShoulderMeters = 10.0;
            Assert.AreEqual(1, session.RefitAllTerrain(summary).Count);
            CollectionAssert.AreNotEqual(fitted, session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            session.TerrainShape.ShoulderMeters = 2.0;
            Assert.AreEqual(1, session.RefitAllTerrain(summary).Count);
            CollectionAssert.AreEqual(fitted, session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));

            // Undo returns to the original terrain, redo fits it again.
            session.Undo(summary, change);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map.terrain"));
            session.Redo(summary, change);
            Assert.IsTrue(session.Workspace.IsModified("tile_0_0.map.terrain"));

            // Deleting the road piece by piece gives the original terrain back.
            foreach (var id in change.SplineIds.AsEnumerable().Reverse())
            {
                var spline = summary.Tiles[0].Splines.Single(candidate => candidate.Id == id);
                Assert.IsTrue(session.DeleteSpline(summary, summary.Tiles[0], spline).Succeeded);
            }
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map.terrain"));
            Assert.IsFalse(tileFileHasAlignment(session));
            CollectionAssert.AreEqual(originalTerrain, File.ReadAllBytes(Path.Combine(mapFolder, "tile_0_0.map.terrain")));
        }

        [Test]
        public void TerrainIsLeftAloneBeyondARoadEndOnAJunctionAndLiesBelowTheSplineBeforeIt()
        {
            // A junction whose one connection point lies at its origin, facing south.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Test"));
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Test", "junction.sco"),
                "[splinehelper]\r\nSplines\\road.sli\r\n0\r\n0\r\n0\r\n180\r\n");
            var session = OpenProject();
            var summary = session.Read();
            summary.Tiles[0].Objects.Add(new OmsiObjectRecord { AssetPath = @"Sceneryobjects\Test\junction.sco", Id = 90, LocalX = 100, LocalY = 260, Height = 2 });
            // 2 m high at x 100, where the ground lies at 5 m, ending on the junction's point.
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 200, StartHeight = 2, EndX = 100, EndY = 260, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null);
            session.CreateRoad(summary, plan, @"Splines\road.sli", null, null, true);

            var terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            // Under the road 15 m before the end: 5 cm below the kerb, as everywhere.
            Assert.AreEqual(2.10, terrain[20, 49], 1e-4);
            // 5 m before the end, within a grid diagonal: 5 cm below the spline, under the junction's carriageway.
            Assert.AreEqual(1.95, terrain[20, 51], 1e-4);
            // Beside it the shoulder stays at the kerb.
            Assert.AreEqual(2.10, terrain[21, 51], 1e-4);
            // 5 m beyond the end, inside the junction, the ground stays as it was.
            Assert.AreEqual(5.0, terrain[20, 53], 1e-4);
            // The start is free, so slopes still round off around it.
            Assert.AreEqual(2.10, terrain[20, 39], 1e-4);
        }

        [Test]
        public void TerrainUnderAJunctionPlacedHereFollowsItsCarriagewayOnceAFittedRoadJoinsIt()
        {
            // A junction 20 m square whose one connection point lies 10 m behind its origin, facing back.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Test"));
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Test", "square.sco"),
                "[splinehelper]\r\nSplines\\road.sli\r\n0\r\n0\r\n-10\r\n180\r\n\r\n[boundingbox]\r\n20\r\n20\r\n1\r\n0\r\n0\r\n0\r\n");
            var session = OpenProject();
            var summary = session.Read();
            // Placed here: its ID lies above those of the original map. Its point is at 100, 260, where the road ends.
            var junction = new OmsiObjectRecord { AssetPath = @"Sceneryobjects\Test\square.sco", Id = 500, LocalX = 100, LocalY = 270, Height = 2 };
            summary.Tiles[0].Objects.Add(junction);
            Assert.AreEqual(string.Empty, session.JunctionTerrainState(summary).Replace(@"500 Sceneryobjects\Test\square.sco 0 0 100 270 2 0 0 0;", string.Empty));
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 200, StartHeight = 2, EndX = 100, EndY = 260, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null);
            session.CreateRoad(summary, plan, @"Splines\road.sli", null, null, true);

            var terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            // Under the junction, where the ground lies at 5 m and more: 5 cm below its carriageway at 2 m.
            Assert.AreEqual(1.95, terrain[20, 54], 1e-4);
            Assert.AreEqual(1.95, terrain[21, 55], 1e-4);
            // 5 m beside it on the shoulder: 5 cm below the kerb of the profile it suggests, 0.15 m high.
            Assert.AreEqual(2.10, terrain[23, 54], 1e-4);
            // 10 m beside it the cutting rises 1:1.5 from the shoulder, which reaches a grid diagonal.
            Assert.AreEqual(2.10 + (10.0 - 5.0 * Math.Sqrt(2.0)) / 1.5, terrain[24, 54], 1e-4);

            // Moved away from the road end, the junction is no longer joined: fitted again, the free road end rounds its
            // shoulder and slopes off around itself there instead.
            junction.LocalX = 150;
            Assert.AreEqual(1, session.RefitAllTerrain(summary).Count);
            terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            Assert.AreEqual(2.10 + (10.0 - 5.0 * Math.Sqrt(2.0)) / 1.5, terrain[20, 54], 1e-4);
            // A junction of the original map keeps its terrain even when a fitted road joins it.
            junction.LocalX = 100;
            junction.Id = 90;
            session.RefitAllTerrain(summary);
            terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            Assert.AreEqual(5.0, terrain[20, 54], 1e-4);
        }

        [Test]
        public void TheMapCheckSumsUpTheConnectionPointsOfJunctions()
        {
            // One point where spline 10 starts, 2 m high and facing along it, and one 10 m to the left with nothing there.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Test"));
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Test", "junction.sco"),
                "[splinehelper]\r\nSplines\\road.sli\r\n0\r\n0\r\n0\r\n0\r\n\r\n[splinehelper]\r\nSplines\\road.sli\r\n-10\r\n0\r\n0\r\n270\r\n");
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));
            summary.Tiles[0].Objects.Add(new OmsiObjectRecord { AssetPath = @"Sceneryobjects\Test\junction.sco", Id = 90, LocalX = 100, LocalY = 100, Height = 2 });

            var section = OmsiMapCheck.Run(summary, new OmsiFolderSource(mapFolder), installation).Sections.Single(candidate => candidate.Title == "Junctions");

            Assert.AreEqual(OmsiCheckStatus.Note, section.Status);
            Assert.AreEqual("1 of 2 connection points of 1 junction objects are connected; 0 to review, 0 invalid, 1 free.", section.Summary);
            CollectionAssert.AreEqual(new[] { "free: #90 junction in tile 0, 0: Point 2 is not connected." }, section.Findings);
            Assert.AreSame(summary.Tiles[0], section.Tile);
        }

        [Test]
        public void SplittingASplineKeepsTheRoadAndJoinsBothPartsAndTheNeighbour()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tileBefore = File.ReadAllBytes(Path.Combine(mapFolder, "tile_0_0.map"));
            // A curved road rising from 2 m to 5 m in one piece, and a straight road going on from its end.
            var curve = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 200, StartHeight = 2, EndX = 140, EndY = 250, EndHeight = 5, HeightMode = OmsiRoadHeightMode.Manual,
                MaximumPieceLength = 200
            }, null), @"Splines\road.sli", null, null);
            OmsiSplineRecord Spline(int id) => summary.Tiles[0].Splines.Single(candidate => candidate.Id == id);
            var curveId = curve.SplineIds.Single();
            var original = Spline(curveId);
            var end = new OmsiSplineEnd(summary.Tiles[0], original, true);
            var endPosition = end.Position(300);
            var on = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = endPosition.X, StartY = endPosition.Y, StartHeadingDegrees = end.OutwardHeadingDegrees, StartHeight = 5, EndX = 170, EndY = 270,
                EndHeight = 5, HeightMode = OmsiRoadHeightMode.Manual, MaximumPieceLength = 200
            }, null), @"Splines\road.sli", end, null);
            var onId = on.SplineIds.Single();
            var length = Spline(curveId).LengthMeters;
            var endHeading = new OmsiSplineEnd(summary.Tiles[0], Spline(curveId), true).OutwardHeadingDegrees;

            var result = session.SplitSpline(summary, summary.Tiles[0], Spline(curveId), 15);

            Assert.IsTrue(result.Succeeded);
            var newId = result.Change.SplineIds[1];
            var first = Spline(curveId);
            var second = Spline(newId);
            Assert.AreEqual(newId, first.NextId);
            Assert.AreEqual(curveId, second.PreviousId);
            Assert.AreEqual(onId, second.NextId);
            Assert.AreEqual(newId, Spline(onId).PreviousId);
            Assert.AreEqual(15.0, first.LengthMeters, 1e-6);
            Assert.AreEqual(length, first.LengthMeters + second.LengthMeters, 1e-6);
            Assert.AreEqual(first.RadiusMeters, second.RadiusMeters, 1e-9);
            // The parts meet without a gap, kink or step, and the second ends where the spline ended.
            var joint = new OmsiSplineEnd(summary.Tiles[0], first, true);
            var start = new OmsiSplineEnd(summary.Tiles[0], second, false);
            Assert.AreEqual(0.0, Distance(joint.Position(300), start.Position(300)), 1e-4);
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(joint.OutwardHeadingDegrees + 180.0 - start.OutwardHeadingDegrees), 1e-4);
            Assert.AreEqual(first.EndHeight, second.Height, 1e-4);
            Assert.AreEqual(first.GradientEndPercent, second.GradientStartPercent, 1e-4);
            Assert.AreEqual(0.0, Distance(endPosition, new OmsiSplineEnd(summary.Tiles[0], second, true).Position(300)), 1e-4);
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(endHeading - new OmsiSplineEnd(summary.Tiles[0], second, true).OutwardHeadingDegrees), 1e-4);
            Assert.AreEqual(5.0, second.EndHeight, 1e-4);

            // Undo puts the one spline back, redo splits it again.
            session.Undo(summary, result.Change);
            Assert.IsFalse(summary.Tiles[0].Splines.Exists(candidate => candidate.Id == newId));
            Assert.AreEqual(onId, Spline(curveId).NextId);
            session.Redo(summary, result.Change);
            Assert.AreEqual(newId, Spline(onId).PreviousId);

            // Too close to an end, and on a spline with a row of lamps along it, nothing is split.
            Assert.Throws<InvalidOperationException>(() => session.SplitSpline(summary, summary.Tiles[0], Spline(curveId), 0.2));
            var blocked = session.SplitSpline(summary, summary.Tiles[0], Spline(11), 10);
            Assert.IsFalse(blocked.Succeeded);
            CollectionAssert.IsNotEmpty(blocked.Blockers);
            // The original map is never written.
            CollectionAssert.AreEqual(tileBefore, File.ReadAllBytes(Path.Combine(mapFolder, "tile_0_0.map")));
        }

        [Test]
        public void AJunctionInsertedIntoARoadSitsOnItsCentreLineAndIsJoinedOnBothSides()
        {
            // A T junction: one arm 7 m ahead, two 7 m to the right and left, facing out.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Test"));
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Test", "tee.sco"),
                "[splinehelper]\r\nSplines\\road.sli\r\n0\r\n0\r\n7\r\n0\r\n\r\n" +
                "[splinehelper]\r\nSplines\\road.sli\r\n7\r\n0\r\n0\r\n90\r\n\r\n" +
                "[splinehelper]\r\nSplines\\road.sli\r\n-7\r\n0\r\n0\r\n-90\r\n");
            const string tee = @"Sceneryobjects\Test\tee.sco";
            var session = OpenProject();
            var summary = session.Read();
            // A straight road 100 m north from 100, 180 at 2 m, in four pieces.
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 180, StartHeight = 2, EndX = 100, EndY = 280, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            var tileBytes = session.Workspace.ReadAllBytes("tile_0_0.map");
            var catalog = new OmsiJunctionCatalog(installation);
            // The pointer lies east of the road at 230: the third arm points east.
            var spline = summary.Tiles[0].Splines.Single(candidate => candidate.Id == road.SplineIds[1]);
            var plan = OmsiJunctionInsertion.Plan(summary, catalog, tee, summary.Tiles[0], spline, 25.0, 110, 230);

            Assert.IsNull(plan.Problem);
            Assert.AreEqual(90.0, plan.HeadingDegrees, 1e-6);
            Assert.AreEqual(100.0, plan.Position.X, 1e-6);
            Assert.AreEqual(230.0, plan.Position.Y, 1e-6);
            Assert.AreEqual(2.0, plan.Height, 1e-6);
            Assert.AreEqual(223.0, plan.PortAtStart.Position.Y, 1e-6);
            Assert.AreEqual(237.0, plan.PortAtEnd.Position.Y, 1e-6);

            var result = session.InsertJunction(summary, plan);

            Assert.IsTrue(result.Succeeded);
            var ports = catalog.Ports(summary.Tiles[0], result.Junction, 300);
            var checks = OmsiJunctionCatalog.Check(summary, ports);
            Assert.AreEqual(OmsiJunctionPortState.Unconnected, checks[0].State);
            Assert.AreEqual(OmsiJunctionPortState.Connected, checks[1].State, checks[1].Message);
            Assert.AreEqual(OmsiJunctionPortState.Connected, checks[2].State, checks[2].Message);
            // No road runs under the junction, and the road keeps its two open ends plus one at each connection point.
            foreach (var piece in summary.Tiles[0].Splines.Where(candidate => candidate.Id >= road.SplineIds[0]))
            {
                OmsiMapGeometry.GetLocalFrameAt(piece, piece.LengthMeters / 2.0, out var middle, out _);
                Assert.IsFalse(middle.X > 99 && middle.X < 101 && middle.Y > 223 && middle.Y < 237, "spline " + piece.Id + " runs under the junction");
            }
            // Every piece runs north as the road did, also the one after the junction, so a profile that differs left and
            // right stays the right way round.
            foreach (var piece in summary.Tiles[0].Splines.Where(candidate => candidate.Id >= road.SplineIds[0]))
                Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(piece.HeadingDegrees), 1e-6, "spline " + piece.Id);
            var freeEnds = summary.Tiles[0].Splines.Where(candidate => candidate.Id >= road.SplineIds[0])
                .SelectMany(candidate => new[] { new OmsiSplineEnd(summary.Tiles[0], candidate, false), new OmsiSplineEnd(summary.Tiles[0], candidate, true) })
                .Count(end => end.IsFree);
            Assert.AreEqual(4, freeEnds);

            // Taking every step back, the last first, leaves the road as it was.
            foreach (var step in result.Steps.Reverse())
            {
                if (step is OmsiSplineChange change)
                    session.Undo(summary, change);
                else
                    session.Undo(summary, (OmsiObjectChange)step);
            }
            CollectionAssert.AreEqual(tileBytes, session.Workspace.ReadAllBytes("tile_0_0.map"));
        }

        [Test]
        public void TurningAnArmOfAMadeJunctionMakesItAgainAndLeadsEveryJoinedRoadToItsPoint()
        {
            // The map's road profile with a lane each way, so junctions can be made of it.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            File.WriteAllText(Path.Combine(installation, "Splines", "road.sli"),
                "[texture]\r\nroad.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-4\r\n0.15\r\n0\r\n1\r\n\r\n[profilepnt]\r\n4\r\n0.15\r\n1\r\n1\r\n" +
                "[path]\r\n0\r\n-2\r\n0.15\r\n3.5\r\n1\r\n[path]\r\n0\r\n2\r\n0.15\r\n3.5\r\n0\r\n");
            var profiles = new OmsiSplineProfileCatalog(installation);
            var road = profiles.Get(@"Splines\road.sli");
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var built = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 180, StartHeight = 2, EndX = 100, EndY = 280, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            // A T junction made of the road with its side arm east, inserted into the road at 230 ...
            var tee = OmsiJunctionGenerator.Junction(OmsiRoadJunctions.Arms(@"Splines\road.sli", road, @"Splines\road.sli", road, new double[0], new[] { 90.0 }),
                null, "Tee");
            var teePath = OmsiGeneratedObjectStore.Write(installation, "Roads", "Tee", tee);
            var catalog = new OmsiJunctionCatalog(installation);
            var spline = tile.Splines.Single(candidate => candidate.Id == built.SplineIds[1]);
            var insertion = session.InsertJunction(summary, OmsiJunctionInsertion.Plan(summary, catalog, teePath, tile, spline, 25.0, 110, 230,
                OmsiRoadJunctions.MainPair(catalog.Helpers(teePath))));
            Assert.IsTrue(insertion.Succeeded);
            var junction = insertion.Junction;
            // ... and a street leaving its east point.
            var east = catalog.Ports(tile, junction, 300).Single(port => Math.Abs(OmsiRoadPlanner.Normalize(port.OutwardHeadingDegrees - 90.0)) < 1e-6);
            session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = east.Position.X, StartY = east.Position.Y, StartHeadingDegrees = east.OutwardHeadingDegrees, StartHeight = east.Height,
                EndX = east.Position.X + 40, EndY = east.Position.Y, EndHeight = east.Height, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            var tileBytes = session.Workspace.ReadAllBytes("tile_0_0.map");

            var made = OmsiGeneratedJunction.Read(installation, teePath, profiles.Get);
            Assert.IsNotNull(made);
            Assert.IsTrue(made.SidesKnown);
            var rebuild = OmsiJunctionRebuild.Prepare(summary, catalog, made, tile, junction);
            for (var index = 0; index < made.Arms.Count; index++)
                Assert.IsNotNull(rebuild.JoinedAt(index), "no road at point " + (index + 1));
            var side = made.Arms.FindIndex(arm => !arm.Main);
            // The side arm stays 30° clear of the road's arms, between them.
            Assert.AreEqual(30.0, rebuild.PlanTurn(side, 10, null).HeadingDegrees, 1e-9);
            Assert.AreEqual(150.0, rebuild.PlanTurn(side, 200, null).HeadingDegrees, 1e-9);

            var turn = rebuild.PlanTurn(side, 60, null);
            Assert.IsFalse(turn.HasErrors, turn.Problem ?? string.Join("; ", turn.Leads.SelectMany(lead => lead.Piece.Issues).Select(issue => issue.Message)));
            // Nearer the street, the corner ahead needs more room: that arm grows past the 5 m piece leading to it, so its road
            // is cut 5 m beyond the new point, at the joint 0.7 m further on, and runs on from there as before.
            var ahead = made.Arms.FindIndex(arm => arm.Main && Math.Abs(arm.HeadingDegrees) < 1e-6);
            var aheadLead = turn.Leads.Single(lead => lead.Arm == ahead);
            Assert.Greater(aheadLead.Port.Position.Y, 242.0);
            Assert.AreEqual(255.0, aheadLead.Cut.Y, 1e-6);
            Assert.GreaterOrEqual(aheadLead.Cut.Y - aheadLead.Port.Position.Y, OmsiJunctionInsertion.ConnectorMeters);
            var beyond = tile.Splines.Single(candidate => Math.Abs(new OmsiSplineEnd(tile, candidate, false).Position(300).Y - 255.0) < 1e-6);
            var beyondLength = beyond.LengthMeters;
            var again = OmsiGeneratedObjectStore.Write(installation, "Roads", made.Name, OmsiJunctionGenerator.Junction(turn.Arms, null, "Tee"));
            var result = session.RebuildJunction(summary, rebuild, turn, again);
            Assert.IsTrue(result.Succeeded);
            StringAssert.EndsWith(@"Tee_2\Tee_2.sco", again);
            Assert.AreEqual(again, junction.AssetPath);
            // Every road meets its point again, the street at its new heading.
            var checks = OmsiJunctionCatalog.Check(summary, catalog.Ports(tile, junction, 300));
            foreach (var check in checks)
                Assert.AreEqual(OmsiJunctionPortState.Connected, check.State, check.Message);
            Assert.IsTrue(checks.Any(check => Math.Abs(OmsiRoadPlanner.Normalize(check.Port.OutwardHeadingDegrees - 60.0)) < 1e-6));
            Assert.AreEqual(beyondLength, tile.Splines.Single(candidate => candidate.Id == beyond.Id).LengthMeters, 1e-9);

            // Taking every step back, the last first, leaves the junction and the roads as they were.
            foreach (var step in result.Steps.Reverse())
            {
                if (step is OmsiSplineChange change)
                    session.Undo(summary, change);
                else
                    session.Undo(summary, (OmsiObjectChange)step);
            }
            Assert.AreEqual(teePath, junction.AssetPath);
            CollectionAssert.AreEqual(tileBytes, session.Workspace.ReadAllBytes("tile_0_0.map"));
            // Redoing the replacement names the junction made again.
            session.Redo(summary, (OmsiObjectChange)result.Steps[0]);
            Assert.AreEqual(again, junction.AssetPath);
        }

        [Test]
        public void ARowAlongARoadIsLaidAsOneRecordPerTileThatOmsiPlacesAsPlanned()
        {
            var session = OpenProject();
            var summary = session.Read();
            // A road east at y 100: from 200 to 290 in tile 0, then a second one built from 420 back west, ending on the
            // first, so the chain runs against it in tile 1.
            var first = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 200, StartY = 100, StartHeight = 2, EndX = 290, EndY = 100, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            var firstEnd = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == first.SplineIds.Last()), true);
            session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 420, StartY = 100, StartHeight = 2, StartHeadingDegrees = 270, EndX = 290, EndY = 100, EndHeight = 2,
                EndJoinHeadingDegrees = 270, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, firstEnd);
            var tilesBefore = new[] { session.Workspace.ReadAllBytes("tile_0_0.map"), session.Workspace.ReadAllBytes("tile_1_0.map") };
            var layout = new OmsiSplineAttachmentLayout(summary);
            var start = summary.Tiles[0].Splines.Single(spline => spline.Id == first.SplineIds[0]);
            var request = new OmsiAttachmentRowRequest { AssetPath = @"Sceneryobjects\lamp.sco", LateralOffset = 6, Interval = 20 };

            var plan = OmsiAttachmentRows.Plan(summary, layout, summary.Tiles[0], start, request);

            // 220 m of road: eleven lamps 20 m apart, 10 m in from both ends, 6 m south of the road (right of east).
            Assert.IsNull(plan.Problem);
            Assert.AreEqual(220.0, plan.Chain.Last().End, 1e-6);
            Assert.AreEqual(11, plan.Objects.Count);
            for (var number = 0; number < 11; number++)
            {
                Assert.AreEqual(210.0 + 20 * number, plan.Objects[number].WorldX, 1e-6);
                Assert.AreEqual(94.0, plan.Objects[number].WorldY, 1e-6);
            }
            // One record per tile: five lamps in tile 0, six in tile 1 counted from the chain's other end.
            Assert.AreEqual(2, plan.Parts.Count);
            Assert.AreSame(summary.Tiles[0], plan.Parts[0].Tile);
            Assert.AreEqual(5, plan.Parts[0].Count);
            Assert.AreSame(summary.Tiles[1], plan.Parts[1].Tile);
            Assert.AreEqual(6, plan.Parts[1].Count);
            Assert.AreEqual(-6.0, plan.Parts[1].LateralOffset, 1e-9);

            var change = session.AddAttachmentRow(summary, plan);

            // Read again from the files, OMSI's way of laying rows gives exactly the planned lamps.
            var reread = session.Read();
            var placed = new List<OmsiPlacedAttachment>();
            var rereadLayout = new OmsiSplineAttachmentLayout(reread);
            foreach (var tile in reread.Tiles)
                foreach (var row in tile.SplineAttachments.Where(item => item.AssetPath == @"Sceneryobjects\lamp.sco"))
                    placed.AddRange(rereadLayout.Place(tile, row));
            Assert.AreEqual(11, placed.Count);
            foreach (var lamp in plan.Objects)
                Assert.IsTrue(placed.Any(item => Math.Abs(item.WorldX - lamp.WorldX) < 0.01 && Math.Abs(item.WorldY - lamp.WorldY) < 0.01), "no lamp at " + lamp.WorldX);
            Assert.AreEqual(2, change.Added.Count);
            Assert.AreEqual(1, summary.Tiles[1].SplineAttachments.Count(item => item.AssetPath == @"Sceneryobjects\lamp.sco"));

            // Deleting the part in tile 1 and taking everything back leaves the tiles as they were.
            var deletion = session.DeleteAttachmentRow(summary, summary.Tiles[1], change.Added[1].Value);
            Assert.IsTrue(deletion.Succeeded);
            Assert.AreEqual(0, summary.Tiles[1].SplineAttachments.Count(item => item.AssetPath == @"Sceneryobjects\lamp.sco"));
            session.Undo(summary, deletion.Change);
            session.Undo(summary, change);
            CollectionAssert.AreEqual(tilesBefore[0], session.Workspace.ReadAllBytes("tile_0_0.map"));
            CollectionAssert.AreEqual(tilesBefore[1], session.Workspace.ReadAllBytes("tile_1_0.map"));
            Assert.AreEqual(0, summary.Tiles[0].SplineAttachments.Count(item => item.AssetPath == @"Sceneryobjects\lamp.sco"));
            session.Redo(summary, change);
            Assert.AreEqual(1, summary.Tiles[0].SplineAttachments.Count(item => item.AssetPath == @"Sceneryobjects\lamp.sco"));

            // Between two points along the road: from 30 m to 75 m, every 20 m.
            var part = OmsiAttachmentRows.Plan(summary, layout, summary.Tiles[0], start, request, 75, 30);
            Assert.AreEqual(new[] { 230.0, 250.0, 270.0 }, part.Objects.Select(item => Math.Round(item.WorldX, 6)).ToArray());
        }

        [Test]
        public void TheTerrainBrushRaisesTheGroundWhereTilesMeetAlikeAndIsTakenBackAsOneStep()
        {
            WriteUtf16("global.cfg", "[friendlyname]", "Roadtown", "", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            var session = OpenProject();
            var summary = session.Read();
            var stroke = session.BeginTerrainStroke(summary);

            // Raised for a second at half strength on the edge between the tiles, at x 300, where the slope lies at 15 m.
            var touched = session.ApplyTerrainBrush(summary, stroke, OmsiTerrainBrushMode.Raise, 300, 100, 20, 0.5, 1.0);
            CollectionAssert.AreEquivalent(summary.Tiles.Where(tile => tile.Y == 0 && tile.X <= 1), touched);
            var change = session.EndTerrainStroke(summary, stroke);

            var west = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            var east = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_1_0.map.terrain"));
            Assert.AreEqual(21.0, west[60, 20], 1e-4);
            Assert.AreEqual(21.0, east[0, 20], 1e-4);
            // 10 m off, (1 − 0.25)² of it.
            Assert.AreEqual(14.5 + 6.0 * 0.5625, west[58, 20], 1e-4);
            Assert.AreEqual(west[60, 22], east[0, 22], 1e-6);
            Assert.AreEqual(0.05 * 250, west[50, 20], 1e-4);
            Assert.IsTrue(session.Workspace.IsModified(@"Mapforge\Terrain\tile_0_0.map.terrain"));

            session.Undo(summary, change);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map.terrain"));
            Assert.IsFalse(session.Workspace.IsModified("tile_1_0.map.terrain"));
            Assert.IsFalse(session.Workspace.IsModified(@"Mapforge\Terrain\tile_0_0.map.terrain"));
            session.Redo(summary, change);
            Assert.AreEqual(21.0, OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_1_0.map.terrain"))[0, 20], 1e-4);

            // An export takes the ground as shown, not the shaped ground kept for the project.
            var target = Path.Combine(Directory.GetParent(mapFolder).FullName, "Roadtown (MapWorks)");
            var report = OmsiMapExporter.Export(session.Workspace, target, "Roadtown (MapWorks)");
            Assert.AreEqual(21.0, OmsiTerrain.Parse(File.ReadAllBytes(Path.Combine(target, "tile_1_0.map.terrain")))[0, 20], 1e-4);
            Assert.IsFalse(Directory.Exists(Path.Combine(target, "Mapforge")));
            Assert.IsFalse(report.EditedFiles.Exists(file => file.StartsWith("Mapforge", StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public void FlattenSmoothRoughenAndRampShapeTheGroundAsOpenMapperDoes()
        {
            var session = OpenProject();
            var summary = session.Read();
            var stroke = session.BeginTerrainStroke(summary);
            // Away from the roads at x 100 and 200: flatten at full strength puts the middle right on the height;
            // smoothing leaves an even slope as it is.
            stroke.FlattenTo = 10.0;
            session.ApplyTerrainBrush(summary, stroke, OmsiTerrainBrushMode.Flatten, 60, 200, 15, 1.0, 1.0);
            Assert.AreEqual(10.0, stroke.HeightAt(60, 200).Value, 1e-4);
            session.ApplyTerrainBrush(summary, stroke, OmsiTerrainBrushMode.Smooth, 240, 200, 15, 1.0, 1.0);
            Assert.AreEqual(12.0, stroke.HeightAt(240, 200).Value, 1e-4);
            // Roughening moves the ground by the rolls of that place.
            session.ApplyTerrainBrush(summary, stroke, OmsiTerrainBrushMode.Roughen, 250, 260, 15, 1.0, 0.5);
            Assert.AreEqual(12.5 + OmsiTerrainBrush.Rolls(250, 260) * 1.5, stroke.HeightAt(250, 260).Value, 1e-4);
            for (var x = -500.0; x < 500.0; x += 7.3)
            {
                Assert.LessOrEqual(Math.Abs(OmsiTerrainBrush.Rolls(x, x * 0.7)), 1.0);
                Assert.Less(Math.Abs(OmsiTerrainBrush.Rolls(x, 3) - OmsiTerrainBrush.Rolls(x + 0.05, 3)), 0.05);
            }

            // A ramp from the flattened 10 m at x 60 up the slope to 12.5 m at x 250: even between, as wide as the brush.
            session.ApplyTerrainRamp(stroke, 60, 200, 250, 200, 10, out var gradient);
            Assert.AreEqual(2.5 / 190 * 100, gradient, 1e-3);
            Assert.AreEqual(10.0 + 2.5 * 95 / 190, stroke.HeightAt(155, 200).Value, 1e-3);
            Assert.AreEqual(10.0 + 2.5 * 95 / 190, stroke.HeightAt(155, 205).Value, 1e-3);
            Assert.AreEqual(0.05 * 155, stroke.HeightAt(155, 215).Value, 1e-4);
            Assert.IsNotNull(session.EndTerrainStroke(summary, stroke));
        }

        [Test]
        public void GroundShapedByHandStaysWhenARoadFittedIntoItGoesAndNeverRisesThroughARoad()
        {
            var session = OpenProject();
            var summary = session.Read();
            // A road with terrain fitting at x 100, 2 m high in a cutting through the 5 m ground.
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 200, StartHeight = 2, EndX = 100, EndY = 260, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null, true);

            // Raising beside the road and on it: beside, the ground rises; on the road it stays where it was.
            var stroke = session.BeginTerrainStroke(summary);
            var onRoad = stroke.HeightAt(100, 230).Value;
            session.ApplyTerrainBrush(summary, stroke, OmsiTerrainBrushMode.Raise, 160, 230, 20, 0.5, 1.0);
            session.ApplyTerrainBrush(summary, stroke, OmsiTerrainBrushMode.Raise, 100, 230, 20, 0.5, 1.0);
            Assert.LessOrEqual(stroke.HeightAt(100, 230).Value, Math.Max(onRoad, 2.0 - 0.12) + 1e-4);
            var change = session.EndTerrainStroke(summary, stroke);
            Assert.AreEqual(0.05 * 160 + 6.0, OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"))[32, 46], 1e-4);

            // Without the road the shaped ground comes back, not the original: the raise at x 160 stays.
            foreach (var id in road.SplineIds.AsEnumerable().Reverse())
            {
                var spline = summary.Tiles[0].Splines.Single(item => item.Id == id);
                Assert.IsTrue(session.DeleteSpline(summary, summary.Tiles[0], spline).Succeeded);
            }
            var terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            Assert.AreEqual(0.05 * 160 + 6.0, terrain[32, 46], 1e-4);
            Assert.IsNotNull(change);
        }

        [Test]
        public void TheGroundUnderAnObjectIsLevelledWithItsFootAndEasesIntoTheGroundAround()
        {
            // A house 10 m wide and 6 m deep, a junction plate with its height absolute, and a tree, in the installation.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            var objects = Path.Combine(installation, "Sceneryobjects");
            Directory.CreateDirectory(objects);
            File.WriteAllText(Path.Combine(objects, "house.sco"), "[boundingbox]\r\n10\r\n6\r\n5\r\n0\r\n0\r\n2.5\r\n");
            File.WriteAllText(Path.Combine(objects, "plate.sco"), "[absheight]\r\n\r\n[boundingbox]\r\n8\r\n8\r\n1\r\n0\r\n0\r\n0.5\r\n");
            File.WriteAllText(Path.Combine(objects, "tree.sco"), "[tree]\r\ntree.bmp\r\n8\r\n12\r\n0.5\r\n0.7\r\n");
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var house = session.PlaceObject(summary, tile, @"Sceneryobjects\house.sco", 60, 250, 0, 0).Record;
            var turned = session.PlaceObject(summary, tile, @"Sceneryobjects\house.sco", 160, 250, 0, 90).Record;
            var plate = session.PlaceObject(summary, tile, @"Sceneryobjects\plate.sco", 250, 250, 20, 0).Record;
            var tree = session.PlaceObject(summary, tile, @"Sceneryobjects\tree.sco", 250, 200, 0, 0).Record;

            var change = session.LevelGroundUnder(summary, new[] { house, turned, plate, tree }.Select(item => new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, item)),
                out var levelled);

            Assert.AreEqual(3, levelled);
            var terrain = OmsiTerrain.Parse(session.Workspace.ReadAllBytes("tile_0_0.map.terrain"));
            // The house on the 5 % slope: 3 m under its middle, its whole outline at 3 m, 5 m past its side as it was.
            Assert.AreEqual(3.0, terrain[11, 50], 1e-4);
            Assert.AreEqual(3.0, terrain[13, 50], 1e-4);
            Assert.AreEqual(0.05 * 70, terrain[14, 50], 1e-4);
            // Turned a quarter, its 10 m run north and south: 5 m north of its middle is inside, 5 m east is 2 m out.
            Assert.AreEqual(8.0, terrain[32, 51], 1e-4);
            Assert.Greater(terrain[33, 50], 8.0 + 1e-3);
            Assert.Less(terrain[33, 50], 0.05 * 165 - 1e-3);
            // The plate keeps its absolute height of 20 m; the tree's ground is left alone.
            Assert.AreEqual(20.0, terrain[50, 50], 1e-4);
            Assert.AreEqual(0.05 * 250, terrain[50, 40], 1e-4);
            session.Undo(summary, change);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map.terrain"));
        }

        [Test]
        public void ObjectsHangOnAttachmentPointsAndGoWithTheirParent()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var mast = session.PlaceObject(summary, tile, @"Sceneryobjects\mast.sco", 100, 100, 0, 30).Record;
            var before = session.Workspace.ReadAllBytes("tile_0_0.map");

            // A lamp head on point 2 of the mast, and a sign on point 1 of the lamp head.
            var lamp = session.AttachObject(summary, tile, mast.Id, 1, @"Sceneryobjects\lamp.sco", 0);
            var lampRecord = lamp.Added.Single().Value;
            var sign = session.AttachObject(summary, tile, lampRecord.Id, 0, @"Sceneryobjects\sign.sco", 90);
            var reread = session.Read().Tiles[0];
            Assert.AreEqual(2, reread.AttachedObjects.Count);
            var readLamp = reread.AttachedObjects.Single(item => item.Id == lampRecord.Id);
            Assert.AreEqual(mast.Id, readLamp.ParentId);
            Assert.AreEqual(1, readLamp.AttachPointIndex);
            Assert.AreEqual(90.0, reread.AttachedObjects.Single(item => item.ParentId == lampRecord.Id).HeadingDegrees, 1e-9);
            Assert.Throws<InvalidOperationException>(() => session.AttachObject(summary, tile, mast.Id, 1, @"Sceneryobjects\lamp.sco", 0));

            // Deleting the mast takes both off with it; taking that back brings all three again.
            var deletion = session.DeleteObject(summary, tile, mast);
            Assert.IsTrue(deletion.Succeeded);
            Assert.AreEqual(2, deletion.Change.RemovedAttachedObjectCount);
            Assert.AreEqual(0, tile.AttachedObjects.Count);
            Assert.AreEqual(0, session.Read().Tiles[0].AttachedObjects.Count);
            session.Undo(summary, deletion.Change);
            Assert.AreEqual(2, tile.AttachedObjects.Count);
            Assert.AreEqual(2, session.Read().Tiles[0].AttachedObjects.Count);

            // Taking the lamp head off takes the sign with it, and only them.
            var off = session.DeleteAttachedObject(summary, tile, tile.AttachedObjects.Single(item => item.Id == lampRecord.Id));
            Assert.IsTrue(off.Succeeded);
            Assert.AreEqual(2, off.Change.Removed.Count);
            Assert.AreEqual(1, session.Read().Tiles[0].Objects.Count(item => item.Id == mast.Id));
            Assert.AreEqual(0, session.Read().Tiles[0].AttachedObjects.Count);
            session.Undo(summary, off.Change);
            session.Undo(summary, sign);
            session.Undo(summary, lamp);
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(0, tile.AttachedObjects.Count);
        }

        [Test]
        public void SplinesBuiltAcrossATileBorderStoreHowFarAlongTheirChainTheyBegin()
        {
            var session = OpenProject();
            var summary = session.Read();
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 200, StartY = 100, StartHeight = 2, EndX = 420, EndY = 100, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);

            // Read again from the files: each spline's last number is the length of the road before it, also past the border.
            var reread = session.Read();
            var splines = road.SplineIds.Select(id => reread.Tiles.SelectMany(tile => tile.Splines).Single(spline => spline.Id == id)).ToList();
            Assert.Greater(splines.Count, 1);
            Assert.IsTrue(reread.Tiles[1].Splines.Any(spline => road.SplineIds.Contains(spline.Id)), "the road reaches tile 1");
            var before = 0.0;
            foreach (var spline in splines)
            {
                Assert.AreEqual(before, spline.ChainOffset.Value, 1e-3, "spline " + spline.Id);
                before += spline.LengthMeters;
            }

            // A piece built on at the start moves every offset on by its length; taking it back moves them back.
            var start = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == road.SplineIds[0]), false);
            var piece = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 150, StartY = 100, StartHeight = 2, EndX = 200, EndY = 100, EndHeight = 2, EndJoinHeadingDegrees = 90, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, start);
            var built = session.Read().Tiles.SelectMany(tile => tile.Splines).ToList();
            Assert.AreEqual(50.0, built.Single(spline => spline.Id == road.SplineIds[0]).ChainOffset.Value, 1e-3);
            session.Undo(summary, piece);
            Assert.AreEqual(0.0, session.Read().Tiles.SelectMany(tile => tile.Splines).Single(spline => spline.Id == road.SplineIds[0]).ChainOffset.Value, 1e-3);
        }

        [Test]
        public void StoringChainOffsetsWrittenAs0KeepsTheRowsAlongThemWhereTheyStand()
        {
            var session = OpenProject();
            var summary = session.Read();
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 200, StartY = 100, StartHeight = 2, EndX = 420, EndY = 100, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            // As earlier versions of the editor wrote them: every chain offset 0.
            foreach (var name in new[] { "tile_0_0.map", "tile_1_0.map" })
            {
                var file = OmsiRecordFile.Parse(session.Workspace.ReadAllBytes(name), OmsiTileRecordLayouts.Instance);
                foreach (var view in OmsiTileRecords.Of<OmsiSplineView>(file))
                    view.ChainOffset = 0;
                session.Workspace.Save(name, file);
            }
            summary = session.Read();
            var stale = OmsiMapSession.StaleChainOffsets(summary);
            Assert.AreEqual(road.SplineIds.Count - 1, stale.Count);
            var first = summary.Tiles[0].Splines.Single(spline => spline.Id == road.SplineIds[0]);
            var plan = OmsiAttachmentRows.Plan(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], first,
                new OmsiAttachmentRowRequest { AssetPath = @"Sceneryobjects\lamp.sco", LateralOffset = 6, Interval = 20 });
            Assert.IsNull(plan.Problem);
            session.AddAttachmentRow(summary, plan);
            var lamps = Lamps(summary);
            Assert.AreEqual(11, lamps.Count);
            var before = new[] { session.Workspace.ReadAllBytes("tile_0_0.map"), session.Workspace.ReadAllBytes("tile_1_0.map") };

            var change = session.StoreChainOffsets(summary);

            Assert.IsNotNull(change);
            Assert.AreEqual(0, OmsiMapSession.StaleChainOffsets(session.Read()).Count);
            AssertSamePlaces(lamps, Lamps(session.Read()));
            Assert.IsNull(session.StoreChainOffsets(summary));
            session.Undo(summary, change);
            CollectionAssert.AreEqual(before[0], session.Workspace.ReadAllBytes("tile_0_0.map"));
            CollectionAssert.AreEqual(before[1], session.Workspace.ReadAllBytes("tile_1_0.map"));
        }

        [Test]
        public void ARowOnlyLaysObjectsAlongItsOwnTileAndNotBeforeItsOwnSpline()
        {
            var session = OpenProject();
            var summary = session.Read();
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 200, StartY = 100, StartHeight = 2, EndX = 420, EndY = 100, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            var layout = new OmsiSplineAttachmentLayout(summary);
            var tile0 = summary.Tiles[0];
            var first = tile0.Splines.Single(spline => spline.Id == road.SplineIds[0]);

            // Every 20 m from 10 m on along the first spline: the objects stop where the road's last spline of tile 0 ends,
            // which may reach a little past x 300.
            var record = new OmsiSplineAttachmentRecord
            {
                AssetPath = @"Sceneryobjects\lamp.sco", SplineIndex = tile0.Splines.IndexOf(first), LateralOffset = 6, StartDistance = 10, Interval = 20, Range = 200
            };
            var inTile0 = tile0.Splines.Where(spline => road.SplineIds.Contains(spline.Id)).Sum(spline => spline.LengthMeters);
            Assert.Less(inTile0, 220.0);
            var placed = layout.Place(tile0, record);
            Assert.AreEqual(Enumerable.Range(0, 11).Select(number => 10.0 + 20 * number).Where(along => along <= inTile0).Select(along => 200.0 + along).ToArray(),
                placed.Select(item => Math.Round(item.WorldX, 6)).ToArray());

            // A spline in tile 1 counts from its stored offset; a start before that offset is not drawn at all.
            var tile1 = summary.Tiles[1];
            var later = tile1.Splines.First(spline => road.SplineIds.Contains(spline.Id));
            var offset = later.ChainOffset.Value;
            Assert.Greater(offset, 90.0);
            var onLater = new OmsiSplineAttachmentRecord
            {
                AssetPath = @"Sceneryobjects\lamp.sco", SplineIndex = tile1.Splines.IndexOf(later), LateralOffset = 6, StartDistance = offset + 5, Interval = 0
            };
            Assert.AreEqual(1, layout.Place(tile1, onLater).Count);
            Assert.AreEqual(200.0 + offset + 5, layout.Place(tile1, onLater)[0].WorldX, 1e-6);
            onLater.StartDistance = 5;
            Assert.AreEqual(0, layout.Place(tile1, onLater).Count);

            // Laid by the tool, the row has a record in each tile, and each gives its tile's lamps.
            var plan = OmsiAttachmentRows.Plan(summary, layout, tile0, first, new OmsiAttachmentRowRequest { AssetPath = @"Sceneryobjects\lamp.sco", LateralOffset = 6, Interval = 20 });
            Assert.IsNull(plan.Problem);
            Assert.AreEqual(2, plan.Parts.Count);
            Assert.AreSame(tile1, plan.Parts[1].Tile);
            // Its start is its first lamp's distance along the road, as the stored offsets count it.
            Assert.AreEqual(plan.Objects[plan.Parts[0].Count].WorldX - 200.0, plan.Parts[1].StartDistance, 1e-6);
            session.AddAttachmentRow(summary, plan);
            Assert.AreEqual(11, Lamps(session.Read()).Count);
        }

        [Test]
        public void ARowLaidInPartsPerTileIsChangedAsOneKeepingItsRecords()
        {
            var session = OpenProject();
            var summary = session.Read();
            var start = BuildRoadAcrossTiles(session, summary);
            var laid = session.AddAttachmentRow(summary, OmsiAttachmentRows.Plan(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], start,
                new OmsiAttachmentRowRequest { AssetPath = @"Sceneryobjects\lamp.sco", LateralOffset = 6, Interval = 20 }));
            var tilesLaid = new[] { session.Workspace.ReadAllBytes("tile_0_0.map"), session.Workspace.ReadAllBytes("tile_1_0.map") };
            var ids = laid.Added.Select(item => item.Value.Id).ToArray();

            // Picked at its part in tile 1, the row is both records, seen along the road from its east end: lamps 10 m
            // to 210 m along it, 6 m to the left of west.
            var row = OmsiAttachmentRows.RowOf(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[1], laid.Added[1].Value);
            Assert.AreEqual(2, row.Records.Count);
            Assert.AreEqual(11, row.ObjectCount);
            Assert.AreEqual(-6.0, row.Request.LateralOffset, 1e-9);
            Assert.AreEqual(10.0, row.From, 1e-6);
            Assert.AreEqual(210.0, row.To, 1e-6);
            Assert.IsFalse(row.Continues);

            // Every 25 m and 8 m from the centre: nine lamps from the same first one on, in the same two records.
            var result = session.ChangeAttachmentRow(summary, row, new OmsiAttachmentRowRequest
            {
                AssetPath = row.Request.AssetPath, LateralOffset = -8, Height = row.Request.Height, HeadingDegrees = row.Request.HeadingDegrees, Interval = 25
            });
            Assert.IsTrue(result.Succeeded, result.Problem);
            CollectionAssert.AreEquivalent(ids, result.Change.Added.Select(item => item.Value.Id).ToArray());
            var lamps = Lamps(session.Read());
            Assert.AreEqual(Enumerable.Range(0, 9).Select(number => 210.0 + 25 * number).ToArray(), lamps.Select(item => Math.Round(item.WorldX, 6)).ToArray());
            Assert.IsTrue(lamps.All(item => Math.Abs(item.WorldY - 92.0) < 1e-6));
            Assert.AreEqual(9, Lamps(summary).Count);

            // Taken back, the tiles are as laid; done again, the nine lamps return.
            session.Undo(summary, result.Change);
            CollectionAssert.AreEqual(tilesLaid[0], session.Workspace.ReadAllBytes("tile_0_0.map"));
            CollectionAssert.AreEqual(tilesLaid[1], session.Workspace.ReadAllBytes("tile_1_0.map"));
            Assert.AreEqual(11, Lamps(summary).Count);
            session.Redo(summary, result.Change);
            Assert.AreEqual(9, Lamps(summary).Count);

            // Another object stands where the lamps stood.
            var changed = OmsiAttachmentRows.RowOf(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0],
                summary.Tiles[0].SplineAttachments.Single(item => item.AssetPath == @"Sceneryobjects\lamp.sco"));
            Assert.AreEqual(2, changed.Records.Count);
            var swap = session.ChangeAttachmentRow(summary, changed, new OmsiAttachmentRowRequest
            {
                AssetPath = @"Sceneryobjects\bench.sco", LateralOffset = changed.Request.LateralOffset, Height = changed.Request.Height,
                HeadingDegrees = changed.Request.HeadingDegrees, Interval = changed.Request.Interval
            });
            Assert.IsTrue(swap.Succeeded, swap.Problem);
            var reread = session.Read();
            var layout = new OmsiSplineAttachmentLayout(reread);
            var benches = reread.Tiles.SelectMany(tile => tile.SplineAttachments.Where(item => item.AssetPath == @"Sceneryobjects\bench.sco")
                .SelectMany(item => layout.Place(tile, item))).OrderBy(item => item.WorldX).ToList();
            AssertSamePlaces(lamps, benches);
            Assert.AreEqual(0, Lamps(reread).Count);
        }

        [Test]
        public void ARowShortenedToOneObjectLeavesTheTileItNoLongerReaches()
        {
            var session = OpenProject();
            var summary = session.Read();
            var start = BuildRoadAcrossTiles(session, summary);
            // Lamps at 280, 300 and 320: one in tile 0, two in tile 1.
            var laid = session.AddAttachmentRow(summary, OmsiAttachmentRows.Plan(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], start,
                new OmsiAttachmentRowRequest { AssetPath = @"Sceneryobjects\lamp.sco", LateralOffset = 6, Interval = 20 }, 80, 120));
            Assert.AreEqual(2, laid.Added.Count);
            var row = OmsiAttachmentRows.RowOf(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], laid.Added[0].Value);
            var request = new OmsiAttachmentRowRequest
            {
                AssetPath = row.Request.AssetPath, LateralOffset = row.Request.LateralOffset, Height = row.Request.Height, HeadingDegrees = row.Request.HeadingDegrees
            };

            // Every 40 m: lamps at 280 and 320, still one per tile.
            request.Interval = 40;
            var wider = session.ChangeAttachmentRow(summary, row, request);
            Assert.IsTrue(wider.Succeeded, wider.Problem);
            Assert.AreEqual(new[] { 280.0, 320.0 }, Lamps(session.Read()).Select(item => Math.Round(item.WorldX, 6)).ToArray());

            // A single lamp: the record in tile 1 goes, the one in tile 0 keeps its ID.
            var single = OmsiAttachmentRows.RowOf(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0],
                summary.Tiles[0].SplineAttachments.Single(item => item.AssetPath == @"Sceneryobjects\lamp.sco"));
            request.Interval = 0;
            var one = session.ChangeAttachmentRow(summary, single, request);
            Assert.IsTrue(one.Succeeded, one.Problem);
            Assert.AreEqual(new[] { 280.0 }, Lamps(session.Read()).Select(item => Math.Round(item.WorldX, 6)).ToArray());
            Assert.AreEqual(0, summary.Tiles[1].SplineAttachments.Count(item => item.AssetPath == @"Sceneryobjects\lamp.sco"));
            Assert.AreEqual(laid.Added[0].Value.Id, summary.Tiles[0].SplineAttachments.Single(item => item.AssetPath == @"Sceneryobjects\lamp.sco").Id);
            session.Undo(summary, one.Change);
            Assert.AreEqual(2, Lamps(summary).Count);
        }

        [Test]
        public void ARowContinuedOnAnotherTileGetsTheNewValuesInEveryContinuation()
        {
            var session = OpenProject();
            var summary = session.Read();
            var start = BuildRoadAcrossTiles(session, summary);
            var laid = session.AddAttachmentRow(summary, OmsiAttachmentRows.Plan(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], start,
                new OmsiAttachmentRowRequest { AssetPath = @"Sceneryobjects\lamp.sco", LateralOffset = 6, Interval = 20 }, 10, 50));
            var master = laid.Added.Single().Value;
            // A continuation on tile 1 with the master's values, as the OMSI editor writes them.
            var tile = Encoding.Unicode.GetString(session.Workspace.ReadAllBytes("tile_1_0.map"));
            tile += string.Join("\r\n", "[splineAttachement_repeater]", "0", "0", "3", @"Sceneryobjects\lamp.sco", master.Id.ToString(CultureInfo.InvariantCulture), "0",
                "6", "0", master.StartDistance.ToString(CultureInfo.InvariantCulture), "0", "0", "0", "20", master.Range.ToString(CultureInfo.InvariantCulture), "0", "0", "") + "\r\n";
            session.Workspace.Save("tile_1_0.map", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(tile.TrimStart('﻿'))).ToArray());
            summary = session.Read();
            var before = session.Workspace.ReadAllBytes("tile_1_0.map");
            var record = summary.Tiles[0].SplineAttachments.Single(item => item.Id == master.Id);

            var row = OmsiAttachmentRows.RowOf(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], record);
            Assert.IsTrue(row.Continues);
            Assert.AreEqual(1, row.Records.Count);
            var result = session.ChangeAttachmentRow(summary, row, new OmsiAttachmentRowRequest
            {
                AssetPath = @"Sceneryobjects\bench.sco", LateralOffset = 8, Height = 0.5, HeadingDegrees = 90, Interval = 20
            });
            Assert.IsTrue(result.Succeeded, result.Problem);

            var reread = session.Read();
            var rows = reread.Tiles.SelectMany(item => item.SplineAttachments).Where(item => item.Id == master.Id).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(1, rows.Count(item => item.IsRepeater));
            foreach (var item in rows)
            {
                Assert.AreEqual(@"Sceneryobjects\bench.sco", item.AssetPath);
                Assert.AreEqual(8.0, item.LateralOffset, 1e-9);
                Assert.AreEqual(0.5, item.Height, 1e-9);
                Assert.AreEqual(90.0, item.HeadingDegrees, 1e-9);
                Assert.AreEqual(master.StartDistance, item.StartDistance, 1e-6);
            }
            session.Undo(summary, result.Change);
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes("tile_1_0.map"));
        }

        /// <summary>
        /// A road east at y 100 from 200 to 290 in tile 0, then one built from 420 back west ending on it, so the chain runs
        /// against it in tile 1; returns the first spline.
        /// </summary>
        private static OmsiSplineRecord BuildRoadAcrossTiles(OmsiMapSession session, OmsiMapSummary summary)
        {
            var first = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 200, StartY = 100, StartHeight = 2, EndX = 290, EndY = 100, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            var firstEnd = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == first.SplineIds.Last()), true);
            session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 420, StartY = 100, StartHeight = 2, StartHeadingDegrees = 270, EndX = 290, EndY = 100, EndHeight = 2,
                EndJoinHeadingDegrees = 270, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, firstEnd);
            return summary.Tiles[0].Splines.Single(spline => spline.Id == first.SplineIds[0]);
        }

        [Test]
        public void RowsStayWhereTheyStandWhenARoadIsBuiltOnAtTheStartOfTheirChain()
        {
            var session = OpenProject();
            var summary = session.Read();
            // A road east from 200 to 290 at y 100 with a lamp every 20 m along it.
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 200, StartY = 100, StartHeight = 2, EndX = 290, EndY = 100, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, null);
            var first = summary.Tiles[0].Splines.Single(spline => spline.Id == road.SplineIds[0]);
            var plan = OmsiAttachmentRows.Plan(summary, new OmsiSplineAttachmentLayout(summary), summary.Tiles[0], first,
                new OmsiAttachmentRowRequest { AssetPath = @"Sceneryobjects\lamp.sco", LateralOffset = 6, Interval = 20 });
            session.AddAttachmentRow(summary, plan);
            var lamps = Lamps(summary);
            Assert.AreEqual(4, lamps.Count);

            // A piece built on at the road's start makes its chain begin 50 m earlier; the lamps stay.
            var start = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == road.SplineIds[0]), false);
            var before = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 150, StartY = 100, StartHeight = 2, EndX = 200, EndY = 100, EndHeight = 2, EndJoinHeadingDegrees = 90,
                HeightMode = OmsiRoadHeightMode.Manual
            }, null), @"Splines\road.sli", null, start);
            AssertSamePlaces(lamps, Lamps(summary));
            AssertSamePlaces(lamps, Lamps(session.Read()));

            // Deleting the piece again and taking that back keeps them too.
            var piece = summary.Tiles[0].Splines.Single(spline => spline.Id == before.SplineIds[0]);
            var deletion = session.DeleteSpline(summary, summary.Tiles[0], piece);
            Assert.IsTrue(deletion.Succeeded);
            AssertSamePlaces(lamps, Lamps(summary));
            session.Undo(summary, deletion.Change);
            AssertSamePlaces(lamps, Lamps(summary));
            session.Undo(summary, before);
            AssertSamePlaces(lamps, Lamps(session.Read()));
        }

        private static List<OmsiPlacedAttachment> Lamps(OmsiMapSummary summary)
        {
            var layout = new OmsiSplineAttachmentLayout(summary);
            var result = new List<OmsiPlacedAttachment>();
            foreach (var tile in summary.Tiles)
                foreach (var row in tile.SplineAttachments.Where(item => item.AssetPath == @"Sceneryobjects\lamp.sco"))
                    result.AddRange(layout.Place(tile, row));
            return result.OrderBy(item => item.WorldX).ToList();
        }

        private static void AssertSamePlaces(List<OmsiPlacedAttachment> expected, List<OmsiPlacedAttachment> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.AreEqual(expected[index].WorldX, actual[index].WorldX, 0.01, "lamp " + index);
                Assert.AreEqual(expected[index].WorldY, actual[index].WorldY, 0.01, "lamp " + index);
            }
        }

        [Test]
        public void RoadsMeetingOtherRoadsAreFoundWhereTheyEndOnOrCrossThem()
        {
            // The map's road profile, now with a lane each way: an 8 m carriageway, a road for junctions.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            File.WriteAllText(Path.Combine(installation, "Splines", "road.sli"),
                "[texture]\r\nroad.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-4\r\n0.15\r\n0\r\n1\r\n\r\n[profilepnt]\r\n4\r\n0.15\r\n1\r\n1\r\n" +
                "[path]\r\n0\r\n-2\r\n0.15\r\n3.5\r\n1\r\n[path]\r\n0\r\n2\r\n0.15\r\n3.5\r\n0\r\n");
            File.WriteAllText(Path.Combine(installation, "Splines", "rail.sli"),
                "[texture]\r\nrail.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-1.5\r\n0.2\r\n0\r\n1\r\n\r\n[profilepnt]\r\n1.5\r\n0.2\r\n1\r\n1\r\n" +
                "[path]\r\n2\r\n0\r\n0.2\r\n1.4\r\n2\r\n");
            var profiles = new OmsiSplineProfileCatalog(installation);
            Assert.IsTrue(OmsiRoadJunctions.IsRoad(profiles.Get(@"Splines\road.sli")));
            Assert.IsFalse(OmsiRoadJunctions.IsRoad(profiles.Get(@"Splines\rail.sli")));
            var summary = OmsiMapReader.Read(Path.Combine(mapFolder, "global.cfg"));

            // Spline 10 runs north from 100, 100 to 100, 150. A road east at 125 crosses it 20 m from its start.
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 80, StartY = 125, EndX = 120, EndY = 125, HeightMode = OmsiRoadHeightMode.Manual }, null);
            var crossing = OmsiRoadJunctions.FirstCrossing(summary, profiles, plan, 1.0, new int[0]);
            Assert.AreEqual(10, crossing.Road.Spline.Id);
            Assert.AreEqual(25.0, crossing.Road.Distance, 1e-6);
            Assert.AreEqual(20.0, crossing.Along, 1e-6);
            Assert.AreEqual(90.0, crossing.HeadingDegrees, 1e-6);
            // Ignored, or too near an end of the new road, it does not count.
            Assert.IsNull(OmsiRoadJunctions.FirstCrossing(summary, profiles, plan, 1.0, new[] { 10 }));
            Assert.IsNull(OmsiRoadJunctions.FirstCrossing(summary, profiles, plan, 25.0, new int[0]));

            // Further east it crosses spline 11 too, which runs north from 200, 100: both in the order the road meets them.
            var longer = OmsiRoadPlanner.Plan(new OmsiRoadRequest { StartX = 80, StartY = 125, EndX = 220, EndY = 125, HeightMode = OmsiRoadHeightMode.Manual }, null);
            var crossings = OmsiRoadJunctions.Crossings(summary, profiles, longer, 1.0, new int[0]);
            CollectionAssert.AreEqual(new[] { 10, 11 }, crossings.Select(each => each.Road.Spline.Id));
            Assert.AreEqual(20.0, crossings[0].Along, 1e-6);
            Assert.AreEqual(120.0, crossings[1].Along, 1e-6);
            Assert.AreEqual(25.0, crossings[1].Road.Distance, 1e-6);
            CollectionAssert.AreEqual(new[] { 11 }, OmsiRoadJunctions.Crossings(summary, profiles, longer, 1.0, new[] { 10 }).Select(each => each.Road.Spline.Id));

            // An end on its carriageway lies on it; 6 m beside it, past its 4 m half width, does not.
            Assert.AreEqual(10, OmsiRoadJunctions.RoadAt(summary, profiles, 101, 140).Spline.Id);
            Assert.IsNull(OmsiRoadJunctions.RoadAt(summary, profiles, 106, 140));

            // Side arms stay between 35° and 145° off the road, on their side.
            Assert.AreEqual(35.0, OmsiRoadJunctions.SideHeading(10), 1e-9);
            Assert.AreEqual(-145.0, OmsiRoadJunctions.SideHeading(-170), 1e-9);
            Assert.AreEqual(90.0, OmsiRoadJunctions.SideHeading(450), 1e-9);

            // A junction of the road and a street joining at 60°: the road's arms ahead and back are the pair to lay on it.
            var road = profiles.Get(@"Splines\road.sli");
            var junction = OmsiJunctionGenerator.Junction(OmsiRoadJunctions.Arms(@"Splines\road.sli", road, @"Splines\road.sli", road, new[] { 60.0 }, new double[0]), null, "Joined");
            var helpers = OmsiSceneryObject.Parse(OmsiTextFile.Encode(junction.Sco, OmsiTextEncoding.Windows1252)).SplineHelpers;
            var pair = OmsiRoadJunctions.MainPair(helpers);
            Assert.AreEqual(180.0, Math.Abs(OmsiRoadPlanner.Normalize(helpers[pair[0]].HeadingDegrees)), 1e-6);
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(helpers[pair[1]].HeadingDegrees), 1e-6);
            Assert.AreEqual(3, helpers.Count);
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        [Test]
        public void TerrainCanBeFittedToABuiltRoadLaterAndReleasedAgain()
        {
            var session = OpenProject();
            var summary = session.Read();
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 200, StartHeight = 2, EndX = 100, EndY = 260, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual
            }, null);
            var built = session.CreateRoad(summary, plan, @"Splines\road.sli", null, null);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map.terrain"));

            var piece = summary.Tiles[0].Splines.Single(spline => spline.Id == built.SplineIds[1]);
            var road = session.BuiltRoadOf(summary, piece);
            CollectionAssert.AreEquivalent(built.SplineIds, road.Select(pair => pair.Value.Id));
            // Spline 10 of the original map is never part of a road built here.
            Assert.IsFalse(session.IsBuiltHere(summary.Tiles[0].Splines.Single(spline => spline.Id == 10)));

            var fit = session.SetTerrainAlignment(summary, road, true);
            Assert.AreEqual(OmsiSplineChangeKind.TerrainAlignment, fit.Kind);
            Assert.IsTrue(session.Workspace.IsModified("tile_0_0.map.terrain"));
            Assert.IsTrue(tileFileHasAlignment(session));
            Assert.IsNull(session.SetTerrainAlignment(summary, session.BuiltRoadOf(summary, piece), true));

            session.Undo(summary, fit);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map.terrain"));
            Assert.IsFalse(tileFileHasAlignment(session));
            session.Redo(summary, fit);
            Assert.IsTrue(session.Workspace.IsModified("tile_0_0.map.terrain"));

            var release = session.SetTerrainAlignment(summary, session.BuiltRoadOf(summary, piece), false);
            Assert.AreEqual(plan.Pieces.Count, release.SplineIds.Count);
            Assert.IsFalse(session.Workspace.IsModified("tile_0_0.map.terrain"));
            Assert.IsFalse(tileFileHasAlignment(session));
        }

        [Test]
        public void MovingAJointWithoutKinksKeepsItsHeadingAndBendsSplinesWithFreeEndsAsOneArc()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 170, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 3
            }, null), @"Splines\road.sli", end, null);
            var tileBefore = session.Workspace.ReadAllBytes("tile_0_0.map");

            // The joint between spline 10 and the new spline moves 10 m east; both other ends are free.
            var joint = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 10), true);
            var move = OmsiSplineEditing.PlanJointMove(summary, joint, 110, 150, true);
            Assert.IsFalse(move.HasErrors, string.Join("; ", move.Issues));
            Assert.IsFalse(move.Splines.Any(geometry => geometry.SplitOff));
            CollectionAssert.AreEquivalent(new[] { 10, road.SplineIds[0] }, move.Splines.Select(geometry => geometry.Spline.Id));
            var changes = session.ApplyJointMove(summary, move, out var blockers);
            Assert.IsEmpty(blockers);

            var spline10 = summary.Tiles[0].Splines.Single(spline => spline.Id == 10);
            var next = summary.Tiles[0].Splines.Single(spline => spline.Id == road.SplineIds[0]);
            // Both bend through the new point, still heading north there, and keep their other ends and heights.
            var moved = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], spline10, 300, true);
            Assert.AreEqual(110.0, moved.X, 1e-6);
            Assert.AreEqual(150.0, moved.Y, 1e-6);
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(new OmsiSplineEnd(summary.Tiles[0], spline10, true).OutwardHeadingDegrees), 1e-6);
            Assert.AreNotEqual(0.0, spline10.RadiusMeters);
            Assert.AreEqual(100.0, OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], spline10, 300, false).Y, 1e-6);
            Assert.AreEqual(2.0, spline10.EndHeight, 1e-9);
            var start = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], next, 300, false);
            var finish = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], next, 300, true);
            Assert.AreEqual(110.0, start.X, 1e-6);
            Assert.AreEqual(100.0, finish.X, 1e-6);
            Assert.AreEqual(170.0, finish.Y, 1e-6);
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(next.HeadingDegrees), 1e-6);
            Assert.AreEqual(3.0, next.EndHeight, 1e-9);
            var continuity = OmsiSplineContinuity.Check(session.Read());
            Assert.AreEqual(continuity.LinkCount, continuity.ConnectedCount);

            for (var index = changes.Count - 1; index >= 0; index--)
                session.Undo(summary, changes[index]);
            CollectionAssert.AreEqual(tileBefore, session.Workspace.ReadAllBytes("tile_0_0.map"));
        }

        [Test]
        public void MovingAJointWithoutKinksSplitsASplineWithAJoinedOtherEndIntoTwoArcs()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            // Two pieces after spline 10: the first one's far end is joined to the second.
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 190, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 2, MaximumPieceLength = 20
            }, null), @"Splines\road.sli", end, null);
            Assert.AreEqual(2, road.SplineIds.Count);
            var tileBefore = session.Workspace.ReadAllBytes("tile_0_0.map");
            var configBefore = session.Workspace.ReadAllBytes("global.cfg");
            var tile = summary.Tiles[0];
            var joint = new OmsiSplineEnd(tile, tile.Splines.Single(spline => spline.Id == 10), true);

            // 8 m east: the first piece must leave the joint northwards and reach the second one northwards, an S of two arcs.
            var move = OmsiSplineEditing.PlanJointMove(summary, joint, 108, 150, true);
            Assert.IsFalse(move.HasErrors, string.Join("; ", move.Issues));
            Assert.That(move.Issues.Select(issue => issue.Message), Has.None.Contains("kink"));
            Assert.AreEqual(1, move.Splines.Count(geometry => geometry.SplitOff));
            var splinesBefore = tile.Splines.Count;
            var changes = session.ApplyJointMove(summary, move, out var blockers);
            Assert.IsEmpty(blockers);
            Assert.AreEqual(splinesBefore + 1, tile.Splines.Count);

            // From the joint to the second piece: positions and headings meet at every end, the joint still faces north.
            var first = tile.Splines.Single(spline => spline.Id == road.SplineIds[0]);
            var split = tile.Splines.Single(spline => spline.Id == first.NextId);
            var second = tile.Splines.Single(spline => spline.Id == road.SplineIds[1]);
            Assert.AreEqual(split.Id, second.PreviousId);
            var firstStart = OmsiMapGeometry.GetSplineEndpoint(tile, first, 300, false);
            Assert.AreEqual(108.0, firstStart.X, 1e-6);
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(first.HeadingDegrees), 1e-6);
            AssertMeet(tile, first, split);
            AssertMeet(tile, split, second);
            Assert.AreEqual(2.0, split.EndHeight, 1e-9);
            var continuity = OmsiSplineContinuity.Check(session.Read());
            Assert.AreEqual(continuity.LinkCount, continuity.ConnectedCount);

            for (var index = changes.Count - 1; index >= 0; index--)
                session.Undo(summary, changes[index]);
            CollectionAssert.AreEqual(tileBefore, session.Workspace.ReadAllBytes("tile_0_0.map"));
            CollectionAssert.AreEqual(configBefore, session.Workspace.ReadAllBytes("global.cfg"));

            // Moving the start of spline 11, whose end is free too, keeps its end and runs straight to the new start.
            var start11 = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 11), false);
            var startMove = OmsiSplineEditing.PlanJointMove(summary, start11, 195, 95);
            var geometry = startMove.Splines.Single();
            Assert.AreEqual(195.0, geometry.LocalX, 1e-6);
            Assert.AreEqual(95.0, geometry.LocalY, 1e-6);
            var preview = geometry.Preview();
            var tail = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], preview, 300, true);
            Assert.AreEqual(200.0, tail.X, 1e-6);
            Assert.AreEqual(130.0, tail.Y, 1e-6);
        }

        /// <summary>The end of one spline is the start of the next, with the same heading.</summary>
        private static void AssertMeet(OmsiTileSummary tile, OmsiSplineRecord before, OmsiSplineRecord after)
        {
            var end = OmsiMapGeometry.GetSplineEndpoint(tile, before, 300, true);
            var start = OmsiMapGeometry.GetSplineEndpoint(tile, after, 300, false);
            Assert.AreEqual(start.X, end.X, 1e-6, "x where #" + before.Id + " meets #" + after.Id);
            Assert.AreEqual(start.Y, end.Y, 1e-6, "y where #" + before.Id + " meets #" + after.Id);
            var heading = new OmsiSplineEnd(tile, before, true).OutwardHeadingDegrees;
            Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(heading - after.HeadingDegrees), 1e-6, "heading where #" + before.Id + " meets #" + after.Id);
        }

        [Test]
        public void MovingAJointKeepsTheShapeOfBothSplinesAndReportsTheKink()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 170, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 3
            }, null), @"Splines\road.sli", end, null);

            // Both splines are straight and stay straight; they meet with a kink at the moved joint instead of bending.
            var joint = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 10), true);
            var move = OmsiSplineEditing.PlanJointMove(summary, joint, 110, 150);
            Assert.IsFalse(move.HasErrors, string.Join("; ", move.Issues));
            Assert.AreEqual(2, move.Splines.Count);
            foreach (var geometry in move.Splines)
                Assert.AreEqual(0.0, geometry.RadiusMeters, "spline " + geometry.Spline.Id);
            // Spline 10 now heads 11.3° east of north, the new spline leaves the joint 26.6° west of north.
            Assert.That(move.Issues.Select(issue => issue.Message), Has.Some.Contains("now meet with a kink of 38°"));
            var next = move.Splines.Single(geometry => geometry.Spline.Id == road.SplineIds[0]).Preview();
            var finish = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], next, 300, true);
            Assert.AreEqual(100.0, finish.X, 1e-6);
            Assert.AreEqual(170.0, finish.Y, 1e-6);
        }

        [Test]
        public void BendingASplineRunsItThroughTheHandleAndAnotherJointMoveKeepsTheBend()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 170, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 3
            }, null), @"Splines\road.sli", end, null);
            var tile = summary.Tiles[0];
            var spline10 = tile.Splines.Single(spline => spline.Id == 10);

            // Spline 10 runs from 100, 100 to 100, 150; bent through 95, 125 it turns 45.2° to the right and keeps its ends.
            var bend = OmsiSplineEditing.PlanBend(summary, tile, spline10, 95, 125);
            Assert.IsFalse(bend.HasErrors, string.Join("; ", bend.Issues));
            var bent = bend.Splines.Single().Preview();
            Assert.AreEqual(45.24, bent.LengthMeters / bent.RadiusMeters * 180.0 / Math.PI, 0.01);
            var middle = OmsiSplineEditing.CurvePoint(tile, bent, 300);
            Assert.AreEqual(95.0, middle.X, 1e-6);
            Assert.AreEqual(125.0, middle.Y, 1e-6);
            var bentEnd = OmsiMapGeometry.GetSplineEndpoint(tile, bent, 300, true);
            Assert.AreEqual(100.0, bentEnd.X, 1e-6);
            Assert.AreEqual(150.0, bentEnd.Y, 1e-6);
            Assert.AreEqual(2.0, bent.EndHeight, 1e-9);
            Assert.That(bend.Issues.Select(issue => issue.Message), Has.Some.Contains("now meets #" + road.SplineIds[0] + " with a kink of 23°"));
            // Straight runs it along the chord.
            Assert.AreEqual(0.0, OmsiSplineEditing.PlanBend(summary, tile, spline10, 95, 125, true).Splines.Single().RadiusMeters);

            session.UpdateSplines(summary, bend.Splines);
            spline10 = tile.Splines.Single(spline => spline.Id == 10);
            var joint = new OmsiSplineEnd(tile, spline10, true);
            var moved = OmsiSplineEditing.PlanJointMove(summary, joint, 110, 150).Splines.Single(geometry => geometry.Spline.Id == 10);
            Assert.AreEqual(45.24, moved.LengthMeters / moved.RadiusMeters * 180.0 / Math.PI, 0.01);
        }

        [Test]
        public void RaisingAJointKeepsTheFarEndsAndMeetsSmoothlyWhereOneFarEndIsFree()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            // Two 20 m pieces north of spline 10, all level at 2 m.
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 190, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 2, MaximumPieceLength = 20
            }, null), @"Splines\road.sli", end, null);
            var tileBefore = session.Workspace.ReadAllBytes("tile_0_0.map");
            var tile = summary.Tiles[0];
            var joint = new OmsiSplineEnd(tile, tile.Splines.Single(spline => spline.Id == 10), true);

            var plan = OmsiSplineEditing.PlanJointHeight(summary, joint, 3.0);
            Assert.IsFalse(plan.HasErrors, string.Join("; ", plan.Issues));
            Assert.AreEqual(2, plan.Splines.Count);
            var change = session.UpdateSplines(summary, plan.Splines);

            var spline10 = tile.Splines.Single(spline => spline.Id == 10);
            var first = tile.Splines.Single(spline => spline.Id == road.SplineIds[0]);
            var second = tile.Splines.Single(spline => spline.Id == road.SplineIds[1]);
            Assert.AreEqual(2.0, spline10.Height, 1e-9);
            Assert.AreEqual(3.0, spline10.EndHeight, 1e-9);
            Assert.AreEqual(3.0, first.Height, 1e-9);
            Assert.AreEqual(2.0, first.EndHeight, 1e-9);
            // The piece beyond keeps meeting level; spline 10, free at its start, follows the gradient of the joint.
            Assert.AreEqual(0.0, first.GradientAtPercent(first.LengthMeters), 1e-9);
            Assert.AreEqual(first.GradientAtPercent(0.0), spline10.GradientAtPercent(spline10.LengthMeters), 1e-9);
            Assert.AreEqual(-10.0, first.GradientAtPercent(0.0), 1e-9);
            Assert.AreEqual(2.0, second.Height, 1e-9);
            Assert.That(plan.Issues.Select(issue => issue.Message), Has.Some.Contains("steeper than 10.0 %"));
            Assert.That(plan.Issues.Select(issue => issue.Message), Has.None.Contains("apart"));
            var continuity = OmsiSplineContinuity.Check(session.Read());
            Assert.AreEqual(continuity.LinkCount, continuity.ConnectedCount);

            session.Undo(summary, change);
            CollectionAssert.AreEqual(tileBefore, session.Workspace.ReadAllBytes("tile_0_0.map"));
        }

        [Test]
        public void ChangingTheHeightOfAFreeEndKeepsTheGradientAtTheJoinedOtherEnd()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 170, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 2
            }, null), @"Splines\road.sli", end, null);
            var tile = summary.Tiles[0];
            var start = new OmsiSplineEnd(tile, tile.Splines.Single(spline => spline.Id == 10), false);

            var plan = OmsiSplineEditing.PlanJointHeight(summary, start, 1.0);
            var geometry = plan.Splines.Single();
            Assert.AreEqual(1.0, geometry.Height, 1e-9);
            // 1 m over 50 m with a level end: the gradient runs from 4 % at the start to 0 at the joined end.
            Assert.AreEqual(4.0, geometry.GradientStartPercent, 1e-9);
            Assert.AreEqual(0.0, geometry.GradientEndPercent, 1e-9);
            Assert.IsEmpty(plan.Issues);
        }

        [Test]
        public void RaisingAJointBetweenSplinesJoinedOnBothSidesReportsTheChangeOfGradient()
        {
            var session = OpenProject();
            var summary = session.Read();
            var end = OmsiSplineEnd.FindFree(summary, 100, 150, 1.0);
            var road = session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 100, StartY = 150, StartHeadingDegrees = 0, StartHeight = 2, EndX = 100, EndY = 210, HeightMode = OmsiRoadHeightMode.Manual,
                EndHeight = 2, MaximumPieceLength = 20
            }, null), @"Splines\road.sli", end, null);
            Assert.AreEqual(3, road.SplineIds.Count);
            var tile = summary.Tiles[0];
            var first = tile.Splines.Single(spline => spline.Id == road.SplineIds[0]);
            var joint = new OmsiSplineEnd(tile, first, true);

            var plan = OmsiSplineEditing.PlanJointHeight(summary, joint, 2.5);
            Assert.AreEqual(2, plan.Splines.Count);
            session.UpdateSplines(summary, plan.Splines);
            first = tile.Splines.Single(spline => spline.Id == road.SplineIds[0]);
            var second = tile.Splines.Single(spline => spline.Id == road.SplineIds[1]);
            Assert.AreEqual(2.5, first.EndHeight, 1e-9);
            Assert.AreEqual(2.5, second.Height, 1e-9);
            Assert.AreEqual(0.0, first.GradientAtPercent(0.0), 1e-9);
            Assert.AreEqual(0.0, second.GradientAtPercent(second.LengthMeters), 1e-9);
            // 0.5 m over 20 m with level far ends: +5 % arriving, -5 % leaving.
            Assert.AreEqual(5.0, first.GradientAtPercent(first.LengthMeters), 1e-9);
            Assert.AreEqual(-5.0, second.GradientAtPercent(0.0), 1e-9);
            Assert.That(plan.Issues.Select(issue => issue.Message), Has.Some.Contains("10.0 % apart"));
        }

        [Test]
        public void SplinesMeetingEndToEndWithoutNeighbourIdsMoveAndRiseTogether()
        {
            // Spline 12 runs 20 m south from 100, 170 and ends where spline 10 ends; neither names the other, as at the
            // tile borders of Grundorf.
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[spline]", "0", @"Splines\road.sli", "10", "0", "0", "100", "0", "100", "0", "50", "0", "0", "0", "0", "0", "0", "0", "0", "",
                "[spline]", "0", @"Splines\road.sli", "12", "0", "0", "100", "0", "170", "180", "20", "0", "0", "0", "0", "0", "0", "0", "0", "");
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var joint = new OmsiSplineEnd(tile, tile.Splines.Single(spline => spline.Id == 10), true);

            var touching = OmsiSplineEditing.Neighbour(summary, joint);
            Assert.AreEqual(12, touching.Spline.Id);
            Assert.IsTrue(touching.AtEnd);

            var plan = OmsiSplineEditing.PlanJointHeight(summary, joint, 1.0);
            Assert.AreEqual(2, plan.Splines.Count);
            session.UpdateSplines(summary, plan.Splines);
            var spline10 = tile.Splines.Single(spline => spline.Id == 10);
            var spline12 = tile.Splines.Single(spline => spline.Id == 12);
            Assert.AreEqual(1.0, spline10.EndHeight, 1e-9);
            Assert.AreEqual(1.0, spline12.EndHeight, 1e-9);
            Assert.AreEqual(0.0, spline10.Height, 1e-9);
            Assert.AreEqual(0.0, spline12.Height, 1e-9);
            // Both other ends are free, so the joint takes the gradient from one to the other: level here.
            Assert.AreEqual(0.0, spline10.GradientAtPercent(spline10.LengthMeters), 1e-9);
            Assert.AreEqual(0.0, spline12.GradientAtPercent(spline12.LengthMeters), 1e-9);

            var move = OmsiSplineEditing.PlanJointMove(summary, new OmsiSplineEnd(tile, spline10, true), 105, 150);
            CollectionAssert.AreEquivalent(new[] { 10, 12 }, move.Splines.Select(geometry => geometry.Spline.Id));
        }

        [Test]
        public void ARoadEndOnAJunctionPointKeepsItsHeightAndTheRoadMeetsThePointLevel()
        {
            // A junction at the end of spline 10, 100, 150, with its point at its origin facing south into the spline.
            var installation = Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName;
            Directory.CreateDirectory(Path.Combine(installation, "Sceneryobjects", "Test"));
            File.WriteAllText(Path.Combine(installation, "Sceneryobjects", "Test", "junction.sco"),
                "[splinehelper]\r\nSplines\\road.sli\r\n0\r\n0\r\n0\r\n180\r\n");
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var junction = new OmsiObjectRecord { AssetPath = @"Sceneryobjects\Test\junction.sco", Id = 90, LocalX = 100, LocalY = 150, Height = 2 };
            tile.Objects.Add(junction);
            var ports = new OmsiJunctionCatalog(installation).Ports(tile, junction, 300);
            var spline10 = tile.Splines.Single(spline => spline.Id == 10);

            var onPoint = OmsiSplineEditing.PlanJointHeight(summary, new OmsiSplineEnd(tile, spline10, true), 3.0, ports);
            Assert.IsTrue(onPoint.HasErrors);
            Assert.That(onPoint.Issues.Select(issue => issue.Message), Has.Some.Contains("connection point of object #90"));

            // Raising the free start by 1 m: the spline still meets the point level, rising 4 % at the start.
            var geometry = OmsiSplineEditing.PlanJointHeight(summary, new OmsiSplineEnd(tile, spline10, false), 3.0, ports).Splines.Single();
            Assert.AreEqual(3.0, geometry.Height, 1e-9);
            Assert.AreEqual(-4.0, geometry.GradientStartPercent, 1e-9);
            Assert.AreEqual(0.0, geometry.GradientEndPercent, 1e-9);
            // Without the point the far end is free and the spline falls evenly.
            var free = OmsiSplineEditing.PlanJointHeight(summary, new OmsiSplineEnd(tile, spline10, false), 3.0).Splines.Single();
            Assert.AreEqual(-2.0, free.GradientStartPercent, 1e-9);
            Assert.AreEqual(-2.0, free.GradientEndPercent, 1e-9);
        }

        private static bool tileFileHasAlignment(OmsiMapSession session)
        {
            var tileFile = OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            return tileFile.Records.Any(record => record.Name == "spline_terrain_align_2");
        }

        /// <summary>
        /// Every piece starts where the one before ends, with its heading and height, and the last ends at the point with
        /// the heading.
        /// </summary>
        private static void AssertSmooth(OmsiRoadPlan plan, double endX, double endY, double endHeading)
        {
            for (var index = 0; index < plan.Pieces.Count; index++)
            {
                var piece = plan.Pieces[index];
                piece.PointAt(piece.LengthMeters, out var x, out var y, out var height);
                var heading = piece.RadiusMeters == 0.0 ? piece.HeadingDegrees : piece.HeadingDegrees + piece.LengthMeters / piece.RadiusMeters * 180.0 / Math.PI;
                var nextX = index + 1 < plan.Pieces.Count ? plan.Pieces[index + 1].StartX : endX;
                var nextY = index + 1 < plan.Pieces.Count ? plan.Pieces[index + 1].StartY : endY;
                var nextHeading = index + 1 < plan.Pieces.Count ? plan.Pieces[index + 1].HeadingDegrees : endHeading;
                Assert.AreEqual(nextX, x, 1e-6, "x at the end of piece " + index);
                Assert.AreEqual(nextY, y, 1e-6, "y at the end of piece " + index);
                Assert.AreEqual(0.0, OmsiRoadPlanner.Normalize(nextHeading - heading), 1e-6, "heading at the end of piece " + index);
                if (index + 1 < plan.Pieces.Count)
                    Assert.AreEqual(plan.Pieces[index + 1].StartHeight, height, 1e-6, "height at the end of piece " + index);
            }
        }

        private static double Height(float[] heights, int column, int row)
        {
            return heights[row * OmsiTerrain.Resolution + column];
        }

        private OmsiMapSession OpenProject()
        {
            var project = Path.Combine(Directory.GetParent(Directory.GetParent(mapFolder).FullName).FullName, "Projects", "Roads.omproj");
            return OmsiMapSession.OpenProject(Path.Combine(mapFolder, "global.cfg"), project);
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }

        private void WriteTerrain(string relative, int tileX)
        {
            var bytes = new byte[4 + OmsiTerrain.Resolution * OmsiTerrain.Resolution * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(60), 0, bytes, 0, 4);
            for (var row = 0; row < OmsiTerrain.Resolution; row++)
                for (var column = 0; column < OmsiTerrain.Resolution; column++)
                {
                    var worldX = tileX * 300.0 + column * 5.0;
                    Buffer.BlockCopy(BitConverter.GetBytes((float)(0.05 * worldX)), 0, bytes, 4 + (row * OmsiTerrain.Resolution + column) * 4, 4);
                }
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
