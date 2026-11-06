using System;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Placing a map without world coordinates by a reference point, a scale and two points (ADR 061).</summary>
    public sealed class OmsiGeoPlacementTests
    {
        private const double Tile = 300.0;

        private static OmsiGeoAnchor Anchor(int tileX = 0, int tileY = 0, double offsetX = 0, double offsetY = 0, double rotation = 0, double scale = 1)
        {
            return new OmsiGeoAnchor { TileX = tileX, TileY = tileY, OffsetX = offsetX, OffsetY = offsetY, Latitude = 52.5219, Longitude = 13.4132, RotationDegrees = rotation, Scale = scale };
        }

        [Test]
        public void ThePlacementOfAnOldProjectKeepsItsStampAndItsFile()
        {
            // geo.txt of version 1.0 has no offset and no scale; the pictures made for it must not count as out of date.
            var old = OmsiGeoSettings.Parse("tilex 2\ntiley -1\nlatitude 48.1\nlongitude 11.5\nrotation 12.5\nopacity 1\n");
            Assert.AreEqual(0.0, old.Anchor.OffsetX);
            Assert.AreEqual(0.0, old.Anchor.OffsetY);
            Assert.AreEqual(1.0, old.Anchor.Scale);
            var geo = OmsiGeoreference.Placed(old.Anchor, Tile);
            Assert.AreEqual("750 -150 48.1 11.5 12.5", geo.Stamp);
            var text = old.ToString();
            StringAssert.DoesNotContain("offsetx", text);
            StringAssert.DoesNotContain("offsety", text);
            StringAssert.DoesNotContain("scale", text);
        }

        [Test]
        public void TheReferencePointAndTheScaleAreKept()
        {
            var settings = new OmsiGeoSettings { Anchor = Anchor(3, -2, 41.5, -17.25, 20.0, 1.25) };
            var parsed = OmsiGeoSettings.Parse(settings.ToString());
            Assert.AreEqual(3, parsed.Anchor.TileX);
            Assert.AreEqual(-2, parsed.Anchor.TileY);
            Assert.AreEqual(41.5, parsed.Anchor.OffsetX);
            Assert.AreEqual(-17.25, parsed.Anchor.OffsetY);
            Assert.AreEqual(1.25, parsed.Anchor.Scale);
            Assert.AreNotEqual(OmsiGeoreference.Placed(Anchor(3, -2), Tile).Stamp, OmsiGeoreference.Placed(parsed.Anchor, Tile).Stamp);
        }

        [Test]
        public void TheReferencePointLiesAtItsLatitudeAndLongitude()
        {
            var geo = OmsiGeoreference.Placed(Anchor(1, 0, 40, -25, 33), Tile);
            var point = geo.LatLon(1 * Tile + 150 + 40, 0 * Tile + 150 - 25);
            Assert.AreEqual(52.5219, point.Latitude, 1e-9);
            Assert.AreEqual(13.4132, point.Longitude, 1e-9);
        }

        [Test]
        public void AScaleStretchesTheMapOverTheGround()
        {
            var plain = OmsiGeoreference.Placed(Anchor(), Tile);
            var doubled = OmsiGeoreference.Placed(Anchor(scale: 2.0), Tile);
            var a = plain.LatLon(150 + 1000, 150);
            var b = doubled.LatLon(150 + 500, 150);
            // 500 m of a map twice as large cover the ground 1,000 m do on a true one.
            Assert.AreEqual(a.Longitude, b.Longitude, 1e-9);
            var back = doubled.World(b.Latitude, b.Longitude);
            Assert.AreEqual(150 + 500, back.X, 1e-3);
            Assert.AreEqual(150, back.Y, 1e-3);
        }

        [Test]
        public void ChoosingAReferencePointDoesNotMoveTheMap()
        {
            var geo = OmsiGeoreference.Placed(Anchor(0, 0, 0, 0, 27, 1.1), Tile);
            var moved = geo.AnchorAt(1234.5, -678.25);
            Assert.AreEqual(4, moved.TileX);
            Assert.AreEqual(-3, moved.TileY);
            var again = OmsiGeoreference.Placed(moved, Tile);
            // The placement is a local approximation around its reference point, so another reference point agrees to decimetres
            // (1.5e-5 degrees are about 1 m) even 3 km away, and exactly at the new reference point.
            var at = again.LatLon(1234.5, -678.25);
            var was = geo.LatLon(1234.5, -678.25);
            Assert.AreEqual(was.Latitude, at.Latitude, 1e-12);
            Assert.AreEqual(was.Longitude, at.Longitude, 1e-12);
            foreach (var point in new[] { new OmsiMapPoint(0, 0), new OmsiMapPoint(1234.5, -678.25), new OmsiMapPoint(-2000, 1500), new OmsiMapPoint(800, 900) })
            {
                var before = geo.LatLon(point.X, point.Y);
                var after = again.LatLon(point.X, point.Y);
                Assert.AreEqual(before.Latitude, after.Latitude, 1.5e-5);
                Assert.AreEqual(before.Longitude, after.Longitude, 1.5e-5);
            }
            Assert.AreEqual(geo.Stamp == again.Stamp, false, "the stamp names the reference point, which moved");
        }

        [Test]
        public void TwoPointsFindTheTurnTheScaleAndThePlace()
        {
            var truth = OmsiGeoreference.Placed(Anchor(0, 0, 12, -30, 37, 1.3), Tile);
            var firstMap = new OmsiMapPoint(-400, 250);
            var secondMap = new OmsiMapPoint(900, -600);
            var first = truth.LatLon(firstMap.X, firstMap.Y);
            var second = truth.LatLon(secondMap.X, secondMap.Y);
            var fitted = OmsiGeoreference.FitTwoPoints(Tile, firstMap, first, secondMap, second, true);
            Assert.IsNotNull(fitted);
            Assert.AreEqual(37.0, fitted.RotationDegrees, 1e-2);
            Assert.AreEqual(1.3, fitted.Scale, 1e-4);
            var placed = OmsiGeoreference.Placed(fitted, Tile);
            foreach (var point in new[] { firstMap, secondMap, new OmsiMapPoint(0, 0), new OmsiMapPoint(-1500, 1200), new OmsiMapPoint(2000, 300) })
            {
                var expected = truth.LatLon(point.X, point.Y);
                var actual = placed.LatLon(point.X, point.Y);
                Assert.AreEqual(expected.Latitude, actual.Latitude, 2e-6, "latitude at " + point.X + ", " + point.Y);
                Assert.AreEqual(expected.Longitude, actual.Longitude, 2e-6, "longitude at " + point.X + ", " + point.Y);
            }
        }

        [Test]
        public void TwoPointsWithoutAScaleKeepTheScaleAtOne()
        {
            var truth = OmsiGeoreference.Placed(Anchor(0, 0, 0, 0, -140, 1.0), Tile);
            var firstMap = new OmsiMapPoint(100, 100);
            var secondMap = new OmsiMapPoint(-800, 450);
            var fitted = OmsiGeoreference.FitTwoPoints(Tile, firstMap, truth.LatLon(firstMap.X, firstMap.Y), secondMap, truth.LatLon(secondMap.X, secondMap.Y), false);
            Assert.AreEqual(-140.0, fitted.RotationDegrees, 1e-2);
            Assert.AreEqual(1.0, fitted.Scale);
            // The turn comes out between -180 and 180 whichever way the points lie.
            var flipped = OmsiGeoreference.FitTwoPoints(Tile, secondMap, truth.LatLon(secondMap.X, secondMap.Y), firstMap, truth.LatLon(firstMap.X, firstMap.Y), false);
            Assert.AreEqual(-140.0, flipped.RotationDegrees, 1e-2);
        }

        [TestCase("52.5200, 13.4050", 52.52, 13.405)]
        [TestCase("52.5200,13.4050", 52.52, 13.405)]
        [TestCase("  52.52 13.405 ", 52.52, 13.405)]
        [TestCase("52,52; 13,405", 52.52, 13.405)]
        [TestCase("52.52° N, 13.405° E", 52.52, 13.405)]
        [TestCase("33.8688° S, 151.2093° E", -33.8688, 151.2093)]
        [TestCase("40.7128, -74.0060", 40.7128, -74.006)]
        [TestCase("40.7128° N 74.0060° W", 40.7128, -74.006)]
        public void PastedCoordinatesAreRead(string text, double latitude, double longitude)
        {
            Assert.IsTrue(OmsiLatLon.TryParse(text, out var position), text);
            Assert.AreEqual(latitude, position.Latitude, 1e-12);
            Assert.AreEqual(longitude, position.Longitude, 1e-12);
        }

        [TestCase("")]
        [TestCase("52.52")]
        [TestCase("52.52, 13.4, 7")]
        [TestCase("91, 13")]
        [TestCase("52, 181")]
        [TestCase("Berlin")]
        public void TextThatIsNotAPositionIsRefused(string text)
        {
            Assert.IsFalse(OmsiLatLon.TryParse(text, out _), text);
        }

        [Test]
        public void MovingAPositionByMetresIsUndoneByMovingBack()
        {
            var start = new OmsiLatLon(52.5219, 13.4132);
            var moved = OmsiGeoreference.Moved(start, 100.0, 0.0);
            // 100 m east at 52.5° are 100 / (N · cos φ) radians of longitude, N = a / √(1 − e² sin² φ) on WGS 84: about 0.001473°.
            var phi = start.Latitude * Math.PI / 180.0;
            var prime = 6378137.0 / Math.Sqrt(1.0 - 0.0066943799901 * Math.Sin(phi) * Math.Sin(phi));
            Assert.AreEqual(100.0 / (prime * Math.Cos(phi)) * 180.0 / Math.PI, moved.Longitude - start.Longitude, 1e-8);
            Assert.AreEqual(start.Latitude, moved.Latitude, 1e-12);
            var back = OmsiGeoreference.Moved(moved, -100.0, 0.0);
            Assert.AreEqual(start.Longitude, back.Longitude, 1e-9);
            var north = OmsiGeoreference.Moved(start, 0.0, 100.0);
            Assert.AreEqual(100.0 / 111_300.0, north.Latitude - start.Latitude, 2e-6);
            Assert.AreEqual(start.Longitude, north.Longitude, 1e-12);
        }

        [Test]
        public void AStampNamesThePlacementItWasMadeFor()
        {
            var anchor = Anchor(2, -1, 12.5, -30.0, 25.0, 1.4);
            var geo = OmsiGeoreference.Placed(anchor, Tile);
            var read = OmsiGeoreference.FromStamp(geo.Stamp, Tile);
            Assert.IsNotNull(read);
            Assert.AreEqual(geo.Stamp, read.Stamp);
            var a = geo.LatLon(300, -700);
            var b = read.LatLon(300, -700);
            Assert.AreEqual(a.Latitude, b.Latitude, 1e-12);
            Assert.AreEqual(a.Longitude, b.Longitude, 1e-12);
            Assert.IsNull(OmsiGeoreference.FromStamp("world 371.9", 371.9));
            Assert.IsNull(OmsiGeoreference.FromStamp("750 -150 48.1", Tile));
            Assert.IsNull(OmsiGeoreference.FromStamp(null, Tile));
        }

        [Test]
        public void APictureIsLaidWhereTheNewPlacementWantsItsGround()
        {
            // A picture of the ground cut for one placement; under another, the ground at each of its points has to lie at the
            // map point the other placement gives that ground.
            var before = OmsiGeoreference.Placed(Anchor(0, 0, 0, 0, 10.0, 1.0), Tile);
            var after = OmsiGeoreference.Placed(Anchor(0, 0, 25, -40, 31.0, 1.1), Tile);
            Assert.IsTrue(OmsiGeoreference.Transition(before, after, out var scale, out var rotation, out var moveX, out var moveY));
            var radians = rotation * Math.PI / 180.0;
            foreach (var point in new[] { new OmsiMapPoint(0, 0), new OmsiMapPoint(300, 0), new OmsiMapPoint(300, 300), new OmsiMapPoint(-600, 450) })
            {
                var ground = before.LatLon(point.X, point.Y);
                var expected = after.World(ground.Latitude, ground.Longitude);
                var x = scale * (point.X * Math.Cos(radians) - point.Y * Math.Sin(radians)) + moveX;
                var y = scale * (point.X * Math.Sin(radians) + point.Y * Math.Cos(radians)) + moveY;
                Assert.AreEqual(expected.X, x, 0.5, "x at " + point.X + ", " + point.Y);
                Assert.AreEqual(expected.Y, y, 0.5, "y at " + point.X + ", " + point.Y);
            }
            // The same place needs no move.
            Assert.IsTrue(OmsiGeoreference.Transition(after, after, out scale, out rotation, out moveX, out moveY));
            Assert.AreEqual(1.0, scale, 1e-9);
            Assert.AreEqual(0.0, rotation, 1e-9);
            Assert.AreEqual(0.0, moveX, 1e-6);
            Assert.AreEqual(0.0, moveY, 1e-6);
            Assert.IsFalse(OmsiGeoreference.Transition(OmsiGeoreference.ForWorldCoordinates(371.9), after, out _, out _, out _, out _));
        }

        [Test]
        public void TwoPointsThatCoincideGiveNoPlacement()
        {
            var same = new OmsiLatLon(52.5, 13.4);
            Assert.IsNull(OmsiGeoreference.FitTwoPoints(Tile, new OmsiMapPoint(0, 0), same, new OmsiMapPoint(0, 0), new OmsiLatLon(52.51, 13.41), true));
            Assert.IsNull(OmsiGeoreference.FitTwoPoints(Tile, new OmsiMapPoint(0, 0), same, new OmsiMapPoint(50, 0), same, true));
        }
    }
}
