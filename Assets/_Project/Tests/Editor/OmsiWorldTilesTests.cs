using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiWorldTilesTests
    {
        [Test]
        public void NumbersTilesLikeWebMercatorAtZoomSixteen()
        {
            // At the equator a zoom-16 tile is 611.5 m; Berlin-Spandau's tile 2402, 11279 lies at 52.5° N, 13.2° E.
            Assert.AreEqual(611.496, OmsiWorldTiles.EdgeMeters(0), 0.001);
            Assert.AreEqual(52.53, OmsiWorldTiles.LatitudeDegrees(11279), 0.01);
            Assert.AreEqual(13.19, OmsiWorldTiles.LongitudeDegrees(2402), 0.01);
            Assert.AreEqual(371.9, OmsiWorldTiles.EdgeMeters(11282), 0.05);
            // Further north the tiles get smaller.
            Assert.Less(OmsiWorldTiles.EdgeMeters(11290), OmsiWorldTiles.EdgeMeters(11249));
            Assert.AreEqual("52.5329° N, 13.1946° E", OmsiWorldTiles.DescribeCorner(2402, 11279));
        }

        [Test]
        public void MeasuresAcrossTileBordersAtTheScaleOfEachRow()
        {
            // A tile reaches north as far as the edge at its middle, and east as far as the edge at that latitude.
            var height = OmsiWorldTiles.EdgeMeters(11270.5);
            var northEdge = OmsiWorldTiles.EdgeMeters(11271);
            // The north-east corner of one tile is the south-east corner of the tile above it.
            Assert.AreEqual(0.0, OmsiWorldTiles.DistanceMeters(5, 11270, northEdge, height, 5, 11271, northEdge, 0), 0.001);
            // One metre east of a tile's east edge lies one metre into the next tile.
            var middleEdge = OmsiWorldTiles.EdgeMeters(11270 + 100 / height);
            Assert.AreEqual(0.0, OmsiWorldTiles.DistanceMeters(5, 11270, middleEdge + 1, 100, 6, 11270, 1, 100), 0.001);
            // Within a tile distances are plain metres.
            Assert.AreEqual(50.0, OmsiWorldTiles.DistanceMeters(5, 11270, 10, 10, 5, 11270, 40, 50), 0.01);
            // The rows differ by 2.7 cm, which a flat grid with one edge for all rows cannot follow.
            Assert.AreEqual(0.027, OmsiWorldTiles.EdgeMeters(11270) - OmsiWorldTiles.EdgeMeters(11271), 0.002);
        }

        [Test]
        public void ChecksLinksOfWorldCoordinateMapsInTheirTiles()
        {
            var edge = OmsiWorldTiles.EdgeMeters(11270.5);
            var summary = new OmsiMapSummary { TileSizeMeters = edge, UsesWorldCoordinates = true };
            var lower = new OmsiTileSummary { X = 2400, Y = 11270, FileName = "a.map", IsAvailable = true };
            var upper = new OmsiTileSummary { X = 2400, Y = 11271, FileName = "b.map", IsAvailable = true };
            summary.Tiles.Add(lower);
            summary.Tiles.Add(upper);
            // A northbound spline ends at the north edge of the lower tile; its neighbour starts on the upper tile's edge.
            lower.Splines.Add(new OmsiSplineRecord { Id = 1, NextId = 2, LocalX = 100, LocalY = OmsiWorldTiles.EdgeMeters(11270.5) - 50, HeadingDegrees = 0, LengthMeters = 50 });
            upper.Splines.Add(new OmsiSplineRecord { Id = 2, PreviousId = 1, LocalX = 100, LocalY = 0, HeadingDegrees = 0, LengthMeters = 50 });

            var report = OmsiSplineContinuity.Check(summary);

            Assert.AreEqual(1, report.ConnectedCount);
            Assert.Less(report.LargestGapMeters, 0.01);
        }
    }
}
