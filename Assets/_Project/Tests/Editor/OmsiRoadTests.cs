using System;
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
            Assert.AreEqual(string.Empty, session.JunctionTerrainState(summary).Replace("500 0 0 100 270 2 0 0 0;", string.Empty));
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
        public void MovingAJointTangentiallyBendsBothSplinesAndUndoRestoresThem()
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

            // The joint between spline 10 and the new spline moves 10 m east.
            var joint = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 10), true);
            var move = OmsiSplineEditing.PlanJointMove(summary, joint, 110, 150, true);
            Assert.IsFalse(move.HasErrors, string.Join("; ", move.Issues));
            CollectionAssert.AreEquivalent(new[] { 10, road.SplineIds[0] }, move.Splines.Select(geometry => geometry.Spline.Id));
            var change = session.UpdateSplines(summary, move.Splines);

            var spline10 = summary.Tiles[0].Splines.Single(spline => spline.Id == 10);
            var next = summary.Tiles[0].Splines.Single(spline => spline.Id == road.SplineIds[0]);
            // Spline 10's start is free, so it runs straight from its start to the new point, at its heights.
            Assert.AreEqual(0.0, spline10.RadiusMeters);
            var moved = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], spline10, 300, true);
            Assert.AreEqual(110.0, moved.X, 1e-6);
            Assert.AreEqual(150.0, moved.Y, 1e-6);
            Assert.AreEqual(2.0, spline10.EndHeight, 1e-9);
            // The new spline starts there tangentially and still ends at 100, 170, at 3 m.
            var start = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], next, 300, false);
            var finish = OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], next, 300, true);
            Assert.AreEqual(110.0, start.X, 1e-6);
            Assert.AreEqual(100.0, finish.X, 1e-6);
            Assert.AreEqual(170.0, finish.Y, 1e-6);
            Assert.AreEqual(OmsiRoadPlanner.ChordHeading(100, 100, 110, 150), next.HeadingDegrees, 1e-6);
            Assert.AreNotEqual(0.0, next.RadiusMeters);
            Assert.AreEqual(3.0, next.EndHeight, 1e-9);
            var continuity = OmsiSplineContinuity.Check(session.Read());
            Assert.AreEqual(continuity.LinkCount, continuity.ConnectedCount);

            session.Undo(summary, change);
            CollectionAssert.AreEqual(tileBefore, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(100.0, OmsiMapGeometry.GetSplineEndpoint(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 10), 300, true).X, 1e-9);
        }

        [Test]
        public void MovingAJointReportsAKinkWhereTheNeighbourMeetsTheNextSplineAndMovesStartsBackwards()
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
            var joint = new OmsiSplineEnd(summary.Tiles[0], summary.Tiles[0].Splines.Single(spline => spline.Id == 10), true);
            var move = OmsiSplineEditing.PlanJointMove(summary, joint, 108, 150, true);
            Assert.That(move.Issues.Select(issue => issue.Message), Has.Some.Contains("now meets the next spline with a kink"));

            // Pulling the joint aside to 130, 140, past the next spline's end at 100, 190: staying tangential would turn the
            // next spline by more than half a circle, so it keeps its far end and heading and the kink moves to the joint.
            var back = OmsiSplineEditing.PlanJointMove(summary, joint, 130, 140, true);
            Assert.That(back.Issues.Select(issue => issue.Message), Has.Some.Contains("now meet with a kink"));
            foreach (var pulled in back.Splines)
                Assert.Less(pulled.LengthMeters, 80.0, "no loop for spline " + pulled.Spline.Id);

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
