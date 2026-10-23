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
        public void ContinuedRoadKeepsHeightAndHeadingAndReportsKinksAtJoins()
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

            // Joining a road that continues east at the end: arriving at 90° is smooth, at 45° it is a kink.
            var smooth = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, StartHeadingDegrees = 0, EndX = 20, EndY = 20, EndJoinHeadingDegrees = 90, EndHeight = 7
            }, terrain);
            CollectionAssert.IsEmpty(smooth.Issues.Where(issue => issue.Message.Contains("kink")));
            Assert.AreEqual(7.0, smooth.EndHeight, 1e-12);
            var kinked = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 0, StartY = 0, EndX = 20, EndY = 20, EndJoinHeadingDegrees = 90
            }, terrain);
            Assert.That(kinked.Issues.Select(issue => issue.Message), Has.Some.Contains("kink of 45°"));
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
            // Grid points every 5 m: column 30 is x 150 under the road, column 31 is 1 m outside its edge on the shoulder.
            Assert.AreEqual(1.7, Height(flat, 30, 30), 1e-5);
            Assert.AreEqual(2.15, Height(flat, 31, 30), 1e-5);
            // 6 m outside: the slope of 1:1.5 from the shoulder would end below the ground, so the ground stays.
            Assert.AreEqual(0.0, Height(flat, 32, 30), 1e-5);
            // 5 m beyond the north end on its line the slopes round off around the end: 3 m past the shoulder they fall
            // 2 m, from 2.15 to 0.15.
            Assert.AreEqual(0.15, Height(flat, 30, 41), 1e-5);
            Assert.AreEqual(0.0, Height(flat, 30, 43), 1e-5);
            // Far away nothing changes.
            Assert.AreEqual(0.0, Height(flat, 0, 0), 0.0);

            // The same road through a hill at 6 m: a cutting.
            var hill = Enumerable.Repeat(6f, OmsiTerrain.Resolution * OmsiTerrain.Resolution).ToArray();
            OmsiTerrainShaping.Shape(hill, 0, 0, 300, new[] { road }, new OmsiTerrainShapeSettings());
            Assert.AreEqual(1.7, Height(hill, 30, 30), 1e-5);
            Assert.AreEqual(2.15, Height(hill, 31, 30), 1e-5);
            Assert.AreEqual(2.15 + 4.0 / 1.5, Height(hill, 32, 30), 1e-5);
            Assert.AreEqual(6.0, Height(hill, 36, 30), 1e-5);
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
            Assert.AreEqual(1.7, terrain[20, 46], 1e-4);
            Assert.AreEqual(2.15, terrain[21, 46], 1e-4);
            // Far from the road the ground is as it was.
            Assert.AreEqual(0.05 * 250, terrain[50, 46], 1e-4);
            Assert.IsTrue(summary.Tiles[0].Splines.Where(spline => change.SplineIds.Contains(spline.Id)).All(spline => spline.AlignsTerrain));
            var tileFile = OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            Assert.AreEqual(plan.Pieces.Count, tileFile.Records.Count(record => record.Name == "spline_terrain_align_2" && record.GetField(0) == "1"));
            // Splines of the original map keep the terrain they have.
            Assert.IsFalse(session.ShapesTerrain(summary.Tiles[0].Splines.Single(spline => spline.Id == 10)));

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

        private static bool tileFileHasAlignment(OmsiMapSession session)
        {
            var tileFile = OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("tile_0_0.map"), OmsiTileRecordLayouts.Instance);
            return tileFile.Records.Any(record => record.Name == "spline_terrain_align_2");
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
