using System;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>OmsiMapPaths.ObjectLane is ObjectLaneLocal turned by the object's heading and moved to its position (ADR 105, M5).</summary>
    public sealed class OmsiMapPathsObjectLaneTests
    {
        private const double TileSize = 300.0;
        private const double Heading = 73.5;

        private static void AssertLaneIsLocalLaneTurnedAndMoved(OmsiObjectPath path)
        {
            var tile = new OmsiTileSummary { X = 3, Y = -2 };
            var record = new OmsiObjectRecord { LocalX = 120.25, LocalY = 80.5, HeadingDegrees = Heading };

            var local = OmsiMapPaths.ObjectLaneLocal(path);
            var lane = OmsiMapPaths.ObjectLane(tile, record, path, TileSize);

            Assert.AreEqual(local.Length, lane.Length);
            Assert.GreaterOrEqual(lane.Length, 3);
            var radians = Heading * Math.PI / 180.0;
            for (var index = 0; index < lane.Length; index++)
            {
                // Heading turns the object clockwise seen from above: its forward axis (0, 1) points to (sin h, cos h) in the tile.
                var tileX = record.LocalX + local[index].X * Math.Cos(radians) + local[index].Y * Math.Sin(radians);
                var tileY = record.LocalY - local[index].X * Math.Sin(radians) + local[index].Y * Math.Cos(radians);
                var expected = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, tileX, tileY, TileSize);
                Assert.AreEqual(expected.X, lane[index].X, 1e-9, "x of point " + index);
                Assert.AreEqual(expected.Y, lane[index].Y, 1e-9, "y of point " + index);
            }

            // Turning must keep distances: the world lane is as long as the local one.
            Assert.AreEqual(Distance(local), Distance(lane), 1e-6);
        }

        private static double Distance(OmsiMapPoint[] points)
        {
            var total = 0.0;
            for (var index = 1; index < points.Length; index++)
                total += Math.Sqrt(Math.Pow(points[index].X - points[index - 1].X, 2) + Math.Pow(points[index].Y - points[index - 1].Y, 2));
            return total;
        }

        [Test]
        public void ACurvedObjectPathIsItsLocalLaneTurnedByTheObjectHeadingAndMovedToItsPosition()
        {
            AssertLaneIsLocalLaneTurnedAndMoved(new OmsiObjectPath { X = 1.5, Y = 2.0, HeadingDegrees = 20, RadiusMeters = 40, LengthMeters = 25 });
        }

        [Test]
        public void AStraightObjectPathIsItsLocalLaneTurnedByTheObjectHeadingAndMovedToItsPosition()
        {
            AssertLaneIsLocalLaneTurnedAndMoved(new OmsiObjectPath { X = -3.0, Y = 0.5, HeadingDegrees = 0, RadiusMeters = 0, LengthMeters = 18 });
        }

        [Test]
        public void ARotatedStraightPathEndsAtTheStartPlusLengthAlongTheHeading()
        {
            var tile = new OmsiTileSummary { X = 0, Y = 0 };
            var record = new OmsiObjectRecord { LocalX = 100, LocalY = 100, HeadingDegrees = Heading };
            var lane = OmsiMapPaths.ObjectLane(tile, record, new OmsiObjectPath { LengthMeters = 20 }, TileSize);

            var radians = Heading * Math.PI / 180.0;
            var first = lane[0];
            var last = lane[lane.Length - 1];
            Assert.AreEqual(100.0, first.X, 1e-9);
            Assert.AreEqual(100.0, first.Y, 1e-9);
            Assert.AreEqual(20.0 * Math.Sin(radians), last.X - first.X, 1e-6);
            Assert.AreEqual(20.0 * Math.Cos(radians), last.Y - first.Y, 1e-6);
        }
    }
}
