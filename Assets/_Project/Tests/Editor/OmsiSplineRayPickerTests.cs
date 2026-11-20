using System;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The ray against splines of the 3D view (ADR 098, M5): width, height, margin and order along the ray.</summary>
    public sealed class OmsiSplineRayPickerTests
    {
        /// <summary>A road along x from -50 to 50 at the given heights, as 11 points one tenth of its length apart.</summary>
        private static OmsiSplineRibbon Road(string name, double left, double right, double startHeight = 0.0, double endHeight = 0.0, double lift = 0.0, double y = 0.0)
        {
            var x = new double[11];
            var ys = new double[11];
            for (var index = 0; index < x.Length; index++)
            {
                x[index] = -50.0 + 10.0 * index;
                ys[index] = y;
            }
            return new OmsiSplineRibbon(name, x, ys, startHeight, endHeight, left, right, lift);
        }

        /// <summary>The ray from a point above and south of the target through the target; returns the ribbon's owner or null.</summary>
        private static object Pick(OmsiSplineRibbon[] ribbons, double targetX, double targetHeight, double targetY, double maxDistance = 1000.0,
            double margin = 0.0, double marginPerMeter = 0.0, double cameraHeight = 20.0, double cameraBehind = 20.0, double? at = null)
        {
            double originX = targetX, originHeight = cameraHeight, originY = targetY - cameraBehind;
            var dx = targetX - originX;
            var dh = targetHeight - originHeight;
            var dy = targetY - originY;
            var length = Math.Sqrt(dx * dx + dh * dh + dy * dy);
            var hit = OmsiSplineRayPicker.Pick(ribbons, originX, originHeight, originY, dx / length, dh / length, dy / length, maxDistance, margin,
                marginPerMeter, out var distance);
            if (at.HasValue)
                Assert.AreEqual(at.Value, distance, 0.05);
            return hit?.Owner;
        }

        [Test]
        public void HitsTheMiddleOfTheRoadAtTheRayLength()
        {
            var ribbons = new[] { Road("a", -3, 3) };
            Assert.AreEqual("a", Pick(ribbons, 0, 0, 0, at: Math.Sqrt(20 * 20 + 20 * 20)));
        }

        [Test]
        public void MissesBesideTheRoadAndHitsWithinItsWidth()
        {
            var ribbons = new[] { Road("a", -3, 3) };
            Assert.IsNull(Pick(ribbons, 0, 0, 5));
            Assert.IsNull(Pick(ribbons, 0, 0, -5));
            Assert.AreEqual("a", Pick(ribbons, 0, 0, 2.5));
            Assert.AreEqual("a", Pick(ribbons, 0, 0, -2.5));
        }

        [Test]
        public void TheTwoSidesOfTheCrossSectionDiffer()
        {
            // Heading east: right of the road lies south (negative y). A cross-section from -4 to 2 reaches 4 m left (north), 2 m right.
            var ribbons = new[] { Road("a", -4, 2) };
            Assert.AreEqual("a", Pick(ribbons, 0, 0, 3.5), "left");
            Assert.IsNull(Pick(ribbons, 0, 0, -3.5), "right");
            Assert.AreEqual("a", Pick(ribbons, 0, 0, -1.5), "right inside");
        }

        [Test]
        public void MarginWidensTheRoadByMetresAndByDistance()
        {
            var ribbons = new[] { Road("a", -1, 1) };
            Assert.IsNull(Pick(ribbons, 0, 0, 1.4));
            Assert.AreEqual("a", Pick(ribbons, 0, 0, 1.4, margin: 0.5));
            // About 28 m of ray: 0.02 per metre is 0.57 m.
            Assert.AreEqual("a", Pick(ribbons, 0, 0, 1.5, marginPerMeter: 0.02));
            Assert.IsNull(Pick(ribbons, 0, 0, 1.7, marginPerMeter: 0.02));
        }

        [Test]
        public void ABridgeAboveARoadIsHitFirst()
        {
            var ribbons = new[] { Road("road", -3, 3), Road("bridge", -3, 3, 6, 6) };
            Assert.AreEqual("bridge", Pick(ribbons, 0, 6, 0));
            // Aimed at the road below, the ray meets the bridge deck on its way down.
            Assert.AreEqual("bridge", Pick(ribbons, 0, 0, 0, cameraBehind: 6, cameraHeight: 20));
        }

        [Test]
        public void TheHigherSurfaceAtTheSamePlaceWins()
        {
            var ribbons = new[] { Road("far", -3, 3, 0, 0, 0, 0), Road("near", -3, 3, 0, 0, 0.2, 0) };
            // Both at the same place: the one whose surface lies higher is met first.
            Assert.AreEqual("near", Pick(ribbons, 0, 0, 0));
        }

        [Test]
        public void TheHeightFollowsTheSlopeOfTheRoad()
        {
            var ribbons = new[] { Road("a", -3, 3, 0, 10) };
            // At x = 40 the surface is 9 m high.
            Assert.AreEqual("a", Pick(ribbons, 40, 9, 0, cameraHeight: 30));
            // Aimed at the ground under it, the ray passes the surface's height 6 m south of the road.
            Assert.IsNull(Pick(ribbons, 40, 0, 0, cameraHeight: 30));
        }

        [Test]
        public void TheLiftOfTheSurfaceCounts()
        {
            var ribbons = new[] { Road("wire", -0.3, 0.3, 0, 0, 2.5) };
            // Aimed at the ground under the wire, the ray passes the wire's height 2.5 m further south and misses it.
            Assert.IsNull(Pick(ribbons, 0, 0, 0, cameraHeight: 10, cameraBehind: 10));
            Assert.AreEqual("wire", Pick(ribbons, 0, 2.5, 0, cameraHeight: 10, cameraBehind: 10));
        }

        [Test]
        public void TheMaximumLengthAndUpwardRaysFindNothing()
        {
            var ribbons = new[] { Road("a", -3, 3) };
            Assert.IsNull(Pick(ribbons, 0, 0, 0, maxDistance: 10.0));
            var hit = OmsiSplineRayPicker.Pick(ribbons, 0, 5, -10, 0, 0.5, 0.866, 1000.0, 1.0, 0.01, out _);
            Assert.IsNull(hit);
        }

        [Test]
        public void ARayBeyondTheEndsOfTheRoadMisses()
        {
            var ribbons = new[] { Road("a", -3, 3) };
            Assert.IsNull(Pick(ribbons, 60, 0, 0));
            Assert.AreEqual("a", Pick(ribbons, 49, 0, 0));
        }

        [Test]
        public void ACurvedLineIsHitOnItsOwnSegment()
        {
            // A quarter circle of radius 40 from (0,0) heading north to (40,40).
            var x = new double[9];
            var y = new double[9];
            for (var index = 0; index < 9; index++)
            {
                var angle = index * Math.PI / 16.0;
                x[index] = 40.0 - 40.0 * Math.Cos(angle);
                y[index] = 40.0 * Math.Sin(angle);
            }
            var ribbons = new[] { new OmsiSplineRibbon("arc", x, y, 0, 0, -2, 2, 0) };
            Assert.AreEqual("arc", Pick(ribbons, x[4], 0, y[4]));
            Assert.IsNull(Pick(ribbons, 40, 0, 0), "the centre of the circle is not on the road");
        }

        [Test]
        public void ManyRibbonsAreSkippedByTheirBounds()
        {
            var many = new OmsiSplineRibbon[2000];
            for (var index = 0; index < many.Length; index++)
                many[index] = Road("r" + index, -3, 3, 0, 0, 0, 500.0 + index * 10.0);
            Assert.IsNull(Pick(many, 0, 0, 0, maxDistance: 100.0));
            many[1234] = Road("target", -3, 3, 0, 0, 0, 0);
            Assert.AreEqual("target", Pick(many, 0, 0, 0, maxDistance: 100.0));
        }

        [TestCase(0.0, 0.0, 10.0, 10.0, 5.0, 5.0, 20.0, 20.0, true)] // starts inside
        [TestCase(-5.0, 5.0, 15.0, 5.0, 0.0, 0.0, 10.0, 10.0, true)] // passes through, both ends outside
        [TestCase(-5.0, 15.0, 15.0, 15.0, 0.0, 0.0, 10.0, 10.0, false)] // runs past above
        [TestCase(-5.0, 24.0, 24.0, -5.0, 0.0, 0.0, 10.0, 10.0, true)] // cuts across a corner
        [TestCase(-5.0, 26.0, 26.0, -5.0, 0.0, 0.0, 10.0, 10.0, false)] // misses the corner
        [TestCase(5.0, -5.0, 5.0, 15.0, 0.0, 0.0, 10.0, 10.0, true)] // vertical through
        [TestCase(12.0, 5.0, 12.0, 5.0, 0.0, 0.0, 10.0, 10.0, false)] // a point outside
        public void ALineTouchesTheSelectionBoxOnlyWhereItCrossesOrEnters(double x0, double y0, double x1, double y1, double minX, double minY,
            double maxX, double maxY, bool expected)
        {
            Assert.AreEqual(expected, OmsiSplineRayPicker.SegmentTouchesRectangle(minX, minY, maxX, maxY, x0, y0, x1, y1));
        }

        [Test]
        public void ARibbonNeedsTwoPoints()
        {
            Assert.Throws<ArgumentException>(() => new OmsiSplineRibbon(null, new[] { 0.0 }, new[] { 0.0 }, 0, 0, -1, 1, 0));
        }
    }
}
