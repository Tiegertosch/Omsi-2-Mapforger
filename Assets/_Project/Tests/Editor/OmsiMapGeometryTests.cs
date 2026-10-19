using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiMapGeometryTests
    {
        private const double TileSize = 300.0;
        private const double Tolerance = 0.1;

        [Test]
        public void HeadingZeroRunsAlongPositiveY()
        {
            var end = BuildEnd(Tile(0, 0), Spline(10, 20, 0, 100, 0));

            Assert.AreEqual(10.0, end.X, Tolerance);
            Assert.AreEqual(120.0, end.Y, Tolerance);
        }

        [Test]
        public void HeadingNinetyRunsAlongPositiveX()
        {
            var end = BuildEnd(Tile(0, 0), Spline(10, 20, 90, 100, 0));

            Assert.AreEqual(110.0, end.X, Tolerance);
            Assert.AreEqual(20.0, end.Y, Tolerance);
        }

        [Test]
        public void PositiveRadiusTurnsClockwiseOnAQuarterCircle()
        {
            var end = BuildEnd(Tile(0, 0), Spline(0, 0, 0, Math.PI * 50.0, 100));

            Assert.AreEqual(100.0, end.X, Tolerance);
            Assert.AreEqual(100.0, end.Y, Tolerance);
        }

        [Test]
        public void TileOffsetIsAppliedToWorldCoordinates()
        {
            var end = BuildEnd(Tile(-1, 2), Spline(5, 5, 0, 10, 0));

            Assert.AreEqual(-295.0, end.X, Tolerance);
            Assert.AreEqual(615.0, end.Y, Tolerance);
        }

        [Test]
        public void InvalidLengthProducesNoPoints()
        {
            Assert.AreEqual(0, OmsiMapGeometry.BuildSplinePolyline(Tile(0, 0), Spline(0, 0, 0, 0, 0), TileSize).Length);
            Assert.AreEqual(0, OmsiMapGeometry.BuildSplinePolyline(Tile(0, 0), Spline(0, 0, 0, double.NaN, 0), TileSize).Length);
        }

        [Test]
        public void ClipsLineToTileBoundsInLocalMetres()
        {
            var clipped = OmsiMapGeometry.TryClipLineToTile(new OmsiMapPoint(-10, 150), new OmsiMapPoint(310, 150), 0, 0, TileSize,
                out var start, out var end);

            Assert.IsTrue(clipped);
            Assert.AreEqual(0.0, start.X, 1e-9);
            Assert.AreEqual(300.0, end.X, 1e-9);
            Assert.AreEqual(150.0, start.Y, 1e-9);
        }

        [Test]
        public void LineOutsideTileIsRejected()
        {
            Assert.IsFalse(OmsiMapGeometry.TryClipLineToTile(new OmsiMapPoint(400, 10), new OmsiMapPoint(500, 20), 0, 0, TileSize,
                out _, out _));
        }

        [Test]
        public void SplineIndexSplitsSplineAcrossNeighbourTiles()
        {
            var summary = new OmsiMapSummary();
            var startTile = Tile(0, 0);
            startTile.Splines.Add(Spline(250, 150, 90, 100, 0));
            summary.Tiles.Add(startTile);
            summary.Tiles.Add(Tile(1, 0));

            var index = OmsiSplineTileIndex.Build(summary);

            Assert.AreEqual(50.0, TotalLength(index, 0, 0), Tolerance);
            Assert.AreEqual(50.0, TotalLength(index, 1, 0), Tolerance);
            Assert.AreEqual(0, index.GetSegments(5, 5).Count);
            Assert.IsTrue(index.GetSegments(1, 0).All(segment => segment.Start.X >= -1e-9 && segment.End.X <= 50.0 + Tolerance));
        }

        [Test]
        public void SplineIndexIgnoresTilesThatAreNotPartOfTheMap()
        {
            var summary = new OmsiMapSummary();
            var startTile = Tile(0, 0);
            startTile.Splines.Add(Spline(250, 150, 90, 100, 0));
            summary.Tiles.Add(startTile);

            var index = OmsiSplineTileIndex.Build(summary);

            Assert.AreEqual(50.0, TotalLength(index, 0, 0), Tolerance);
            Assert.AreEqual(0, index.GetSegments(1, 0).Count);
        }

        private static OmsiMapPoint BuildEnd(OmsiTileSummary tile, OmsiSplineRecord spline)
        {
            var points = OmsiMapGeometry.BuildSplinePolyline(tile, spline, TileSize);
            Assert.Greater(points.Length, 1);
            return points[points.Length - 1];
        }

        private static double TotalLength(OmsiSplineTileIndex index, int tileX, int tileY)
        {
            return index.GetSegments(tileX, tileY).Sum(segment =>
                Math.Sqrt(Math.Pow(segment.End.X - segment.Start.X, 2) + Math.Pow(segment.End.Y - segment.Start.Y, 2)));
        }

        private static OmsiTileSummary Tile(int x, int y)
        {
            return new OmsiTileSummary { X = x, Y = y, FileName = "tile_" + x + "_" + y + ".map", IsAvailable = true };
        }

        private static OmsiSplineRecord Spline(double x, double y, double heading, double length, double radius)
        {
            return new OmsiSplineRecord
            {
                LocalX = x,
                LocalY = y,
                HeadingDegrees = heading,
                LengthMeters = length,
                RadiusMeters = radius
            };
        }
    }
}
