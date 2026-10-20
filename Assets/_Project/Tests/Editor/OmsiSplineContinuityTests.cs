using System;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSplineContinuityTests
    {
        [Test]
        public void StraightSplineFollowedByItsNeighbourIsConnected()
        {
            var summary = Map(300.0);
            var tile = Tile(summary, 0, 0);
            tile.Splines.Add(Spline(1, 0, 2, 100, 100, 90, 50, 0));
            tile.Splines.Add(Spline(2, 1, 0, 150, 100, 90, 20, 0));

            var report = OmsiSplineContinuity.Check(summary);

            Assert.AreEqual(1, report.LinkCount);
            Assert.AreEqual(1, report.ConnectedCount);
            Assert.AreEqual(0, report.Gaps.Count);
            Assert.AreEqual(0.0, report.LargestGapMeters, 1e-9);
        }

        [Test]
        public void ArcEndMeetsItsNeighbour()
        {
            var summary = Map(300.0);
            var tile = Tile(summary, 0, 0);
            // A right-hand quarter circle from (0, 0) heading north ends at (100, 100) heading east.
            tile.Splines.Add(Spline(1, 0, 2, 0, 0, 0, Math.PI * 50.0, 100));
            tile.Splines.Add(Spline(2, 1, 0, 100, 100, 90, 10, 0));

            var report = OmsiSplineContinuity.Check(summary);

            Assert.AreEqual(1, report.ConnectedCount);
        }

        [Test]
        public void NeighbourRunningInTheOppositeDirectionIsConnectedAtItsEnd()
        {
            var summary = Map(300.0);
            var tile = Tile(summary, 0, 0);
            tile.Splines.Add(Spline(1, 0, 2, 100, 100, 90, 50, 0));
            // Spline 2 runs west and ends where spline 1 ends.
            tile.Splines.Add(Spline(2, 0, 1, 170, 100, 270, 20, 0));

            var report = OmsiSplineContinuity.Check(summary);

            Assert.AreEqual(1, report.LinkCount);
            Assert.AreEqual(1, report.ConnectedCount);
        }

        [Test]
        public void ReportsGapsLargestFirstWithTheirTile()
        {
            var summary = Map(300.0);
            var tile = Tile(summary, 0, 0);
            tile.Splines.Add(Spline(1, 0, 2, 0, 0, 90, 50, 0));
            tile.Splines.Add(Spline(2, 1, 0, 52, 0, 90, 10, 0));
            tile.Splines.Add(Spline(3, 0, 4, 0, 50, 90, 50, 0));
            tile.Splines.Add(Spline(4, 3, 0, 60, 50, 90, 10, 0));

            var report = OmsiSplineContinuity.Check(summary);

            Assert.AreEqual(2, report.LinkCount);
            Assert.AreEqual(0, report.ConnectedCount);
            Assert.AreEqual(2, report.Gaps.Count);
            Assert.AreEqual(3, report.Gaps[0].SplineId);
            Assert.AreEqual(10.0, report.Gaps[0].DistanceMeters, 1e-9);
            Assert.AreSame(tile, report.Gaps[0].Tile);
            Assert.IsTrue(report.Gaps[0].IsMutual);
            Assert.AreEqual(2.0, report.Gaps[1].DistanceMeters, 1e-9);
            Assert.AreEqual(10.0, report.LargestGapMeters, 1e-9);
        }

        [Test]
        public void CrossTileLinkUsesTheMapTileSize()
        {
            // The same records meet on a 371.9 m world-coordinate grid but not on the classic 300 m grid.
            var worldMap = Map(OmsiMapSummary.WorldCoordinateTileSizeMeters);
            Tile(worldMap, 0, 0).Splines.Add(Spline(1, 0, 2, 361.9, 100, 90, 20, 0));
            Tile(worldMap, 1, 0).Splines.Add(Spline(2, 1, 0, 10, 100, 90, 20, 0));
            var classicMap = Map(OmsiMapSummary.ClassicTileSizeMeters);
            Tile(classicMap, 0, 0).Splines.Add(Spline(1, 0, 2, 361.9, 100, 90, 20, 0));
            Tile(classicMap, 1, 0).Splines.Add(Spline(2, 1, 0, 10, 100, 90, 20, 0));

            Assert.AreEqual(1, OmsiSplineContinuity.Check(worldMap).ConnectedCount);
            Assert.AreEqual(1, OmsiSplineContinuity.Check(classicMap).Gaps.Count);
        }

        [Test]
        public void MarksGapsWhereOnlyOneSplineNamesTheOther()
        {
            var summary = Map(300.0);
            var tile = Tile(summary, 0, 0);
            tile.Splines.Add(Spline(1, 0, 0, 0, 0, 90, 50, 0));
            tile.Splines.Add(Spline(2, 1, 0, 200, 0, 90, 10, 0));

            var report = OmsiSplineContinuity.Check(summary);

            Assert.AreEqual(1, report.Gaps.Count);
            Assert.IsFalse(report.Gaps[0].IsMutual);
        }

        [Test]
        public void CountsMissingNeighboursAndDuplicateIds()
        {
            var summary = Map(300.0);
            var tile = Tile(summary, 0, 0);
            tile.Splines.Add(Spline(1, 0, 99, 0, 0, 90, 50, 0));
            tile.Splines.Add(Spline(1, 0, 0, 0, 10, 90, 50, 0));

            var report = OmsiSplineContinuity.Check(summary);

            Assert.AreEqual(1, report.MissingNeighbourCount);
            Assert.AreEqual(1, report.DuplicateIdCount);
            Assert.AreEqual(0, report.LinkCount);
        }

        [Test]
        public void RejectsNegativeTolerance()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OmsiSplineContinuity.Check(Map(300.0), -1.0));
            Assert.Throws<ArgumentNullException>(() => OmsiSplineContinuity.Check(null));
        }

        private static OmsiMapSummary Map(double tileSize)
        {
            return new OmsiMapSummary { TileSizeMeters = tileSize };
        }

        private static OmsiTileSummary Tile(OmsiMapSummary summary, int x, int y)
        {
            var tile = new OmsiTileSummary { X = x, Y = y, FileName = "tile_" + x + "_" + y + ".map", IsAvailable = true };
            summary.Tiles.Add(tile);
            return tile;
        }

        private static OmsiSplineRecord Spline(int id, int previousId, int nextId, double x, double y, double heading,
            double length, double radius)
        {
            return new OmsiSplineRecord
            {
                Id = id,
                PreviousId = previousId,
                NextId = nextId,
                LocalX = x,
                LocalY = y,
                HeadingDegrees = heading,
                LengthMeters = length,
                RadiusMeters = radius
            };
        }
    }
}
