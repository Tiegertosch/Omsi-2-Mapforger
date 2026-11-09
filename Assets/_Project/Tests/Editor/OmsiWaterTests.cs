using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Water from OpenStreetMap laid flat in the terrain heights taken from the world's elevation data.</summary>
    public sealed class OmsiWaterTests
    {
        // A box of about 1 km around Berlin; the cells are 2 m.
        private const double South = 52.495;
        private const double West = 13.395;
        private const double North = 52.505;
        private const double East = 13.405;

        private static OmsiLatLon Centre => new OmsiLatLon(52.5, 13.4);

        private static OmsiLatLon At(double eastMetres, double northMetres)
        {
            return OmsiGeoreference.Moved(Centre, eastMetres, northMetres);
        }

        private static List<OmsiLatLon> Square(double west, double south, double east, double north)
        {
            return new List<OmsiLatLon> { At(west, south), At(east, south), At(east, north), At(west, north), At(west, south) };
        }

        private static OmsiOsmElement Lake(List<OmsiLatLon> ring)
        {
            var element = new OmsiOsmElement { Way = ring };
            element.Tags["natural"] = "water";
            return element;
        }

        private static OmsiOsmElement Coast(params OmsiLatLon[] line)
        {
            var element = new OmsiOsmElement { Way = new List<OmsiLatLon>(line) };
            element.Tags["natural"] = "coastline";
            return element;
        }

        private static int BodyAt(OmsiWaterMask mask, double eastMetres, double northMetres)
        {
            var mercator = OmsiGeoreference.MercatorOf(At(eastMetres, northMetres).Latitude, At(eastMetres, northMetres).Longitude);
            return mask.BodyAt(mercator.X, mercator.Y);
        }

        private static OmsiWaterMask Mask(params OmsiOsmElement[] elements)
        {
            return OmsiWaterMask.Build(elements, South, West, North, East);
        }

        [Test]
        public void ALakeIsWaterInsideItsRingAndLandOutside()
        {
            var mask = Mask(Lake(Square(-100, -50, 100, 50)));
            Assert.AreEqual(1, mask.BodyCount);
            Assert.AreEqual(0, BodyAt(mask, 0, 0));
            Assert.AreEqual(0, BodyAt(mask, 95, 45));
            Assert.AreEqual(-1, BodyAt(mask, 105, 0));
            Assert.AreEqual(-1, BodyAt(mask, 0, 55));
            Assert.AreEqual(-1, BodyAt(mask, 400, 400), "outside every ring");
            Assert.AreEqual(-1, BodyAt(mask, 5000, 0), "outside the box");
        }

        [Test]
        public void AnIslandOfAMultipolygonIsLand()
        {
            var relation = new OmsiOsmElement
            {
                Relation = new List<KeyValuePair<bool, List<OmsiLatLon>>>
                {
                    new KeyValuePair<bool, List<OmsiLatLon>>(true, Square(-100, -100, 100, 100)),
                    new KeyValuePair<bool, List<OmsiLatLon>>(false, Square(-20, -20, 20, 20))
                }
            };
            relation.Tags["natural"] = "water";
            var mask = Mask(relation);
            Assert.AreEqual(0, BodyAt(mask, 60, 60));
            Assert.AreEqual(-1, BodyAt(mask, 0, 0), "the island");
        }

        [Test]
        public void AWayThatIsNotClosedIsNoAreaAndThingsThatAreNotWaterAreLeftOut()
        {
            var open = Lake(Square(-100, -50, 100, 50));
            open.Way.RemoveAt(open.Way.Count - 1);
            var wood = Lake(Square(200, 200, 300, 300));
            wood.Tags.Remove("natural");
            wood.Tags["natural"] = "wood";
            var mask = Mask(open, wood);
            Assert.AreEqual(0, mask.BodyCount);
        }

        [Test]
        public void WaterThatTouchesIsOneBodyAndSeparateWaterIsTwoAndAPuddleIsDropped()
        {
            var mask = Mask(Lake(Square(-100, -50, 0, 50)), Lake(Square(0, -50, 100, 50)), Lake(Square(200, 200, 260, 260)), Lake(Square(-300, -300, -297, -297)));
            Assert.AreEqual(2, mask.BodyCount);
            Assert.AreEqual(BodyAt(mask, -50, 0), BodyAt(mask, 50, 0));
            Assert.AreNotEqual(BodyAt(mask, 50, 0), BodyAt(mask, 230, 230));
            Assert.AreEqual(-1, BodyAt(mask, -298.5, -298.5), "3 m by 3 m is too little to be a body");
        }

        [Test]
        public void TheSeaLiesOnTheRightOfACoastlineWhateverItsDirectionAndEvenWhenOnlyAPieceIsThere()
        {
            // A coastline heading north: the sea is on its east.
            var north = Mask(Coast(At(0, -400), At(0, 400)));
            Assert.AreEqual(0, BodyAt(north, 150, 0), "east of a coast that heads north");
            Assert.AreEqual(-1, BodyAt(north, -150, 0), "west of it is land");
            // Heading south: the sea is on its west.
            var south = Mask(Coast(At(0, 400), At(0, -400)));
            Assert.AreEqual(0, BodyAt(south, -150, 0));
            Assert.AreEqual(-1, BodyAt(south, 150, 0));
            // Rows the piece does not reach have no coast to say anything.
            Assert.AreEqual(-1, BodyAt(north, 150, 450));
        }

        [Test]
        public void ABendingCoastlineWithTheSeaOnTheRightFollowsItsShape()
        {
            // North along the west side, east along the top, south along the east side: land is on the left of every stretch, so the sea
            // lies inside the loop, east of the first stretch, south of the second and west of the third.
            var mask = Mask(Coast(At(-300, -300), At(-300, 100), At(300, 100), At(300, -300)));
            Assert.AreEqual(0, BodyAt(mask, 0, 0), "inside the loop, on the right of every stretch");
            Assert.AreEqual(-1, BodyAt(mask, 0, 200), "above the eastward stretch, on its left");
            Assert.AreEqual(-1, BodyAt(mask, -400, 0), "west of the northward stretch, on its left");
        }

        [Test]
        public void TheSurfaceIsTheLowerOfTheBanksAndTheInsideAndTheBedLiesUnderIt()
        {
            var mask = Mask(Lake(Square(-100, -100, 100, 100)));
            OmsiMapPoint Low(double e, double n)
            {
                return OmsiGeoreference.MercatorOf(At(e, n).Latitude, At(e, n).Longitude);
            }
            var low = Low(-300, 300);
            var high = Low(300, -300);
            // The data: a block 20 m high inside the lake and the bank at 5 m, as at a quay.
            double Plateau(OmsiMapPoint point)
            {
                return mask.BodyAt(point.X, point.Y) >= 0 ? 20.0 : 5.0;
            }
            var levels = new OmsiWaterLevels(mask);
            levels.Add(low, high, Plateau);
            levels.Add(low, high, Plateau);
            levels.Finish();
            Assert.AreEqual(5.0, levels.Surface(0).Value, 1e-9, "no higher than the bank");
            Assert.AreEqual(5.0 - OmsiWaterLevels.BedDepth, levels.Bed(0).Value, 1e-9);

            // Water that the data has right: low inside, the bank above it.
            var honest = new OmsiWaterLevels(mask);
            honest.Add(low, high, point => mask.BodyAt(point.X, point.Y) >= 0 ? 3.0 : 6.0);
            honest.Finish();
            Assert.AreEqual(3.0, honest.Surface(0).Value, 1e-9, "no higher than what the data says inside");
            Assert.IsNull(honest.Surface(5), "a body that does not exist");
            Assert.AreEqual(1, honest.Decided().Count);
        }

        [Test]
        public void ABodyWithoutAnyHeightGetsNoSurfaceAndIsLeftAsItIs()
        {
            var mask = Mask(Lake(Square(-100, -100, 100, 100)));
            var levels = new OmsiWaterLevels(mask);
            levels.Finish();
            Assert.IsNull(levels.Surface(0));
            Assert.IsNull(levels.Bed(0));
        }

        [Test]
        public void TheWaterOfATileIsTakenFromItsBanksWhenTheDataInsideIsTooHigh()
        {
            var geo = OmsiGeoreference.Placed(new OmsiGeoAnchor { Latitude = 52.5, Longitude = 13.4 }, 300);
            var mask = Mask(Lake(Square(-50, -50, 50, 50)));
            // The elevation data: 5 m everywhere on land and a block 30 m high over the lake.
            OmsiRgbImage Tile(int z, int x, int y)
            {
                var rgb = new byte[256 * 256 * 3];
                for (var py = 0; py < 256; py++)
                    for (var px = 0; px < 256; px++)
                    {
                        var mercatorX = ((double)x * 256 + px + 0.5) / (256.0 * (1 << z));
                        var mercatorY = ((double)y * 256 + py + 0.5) / (256.0 * (1 << z));
                        var height = mask.BodyAt(mercatorX, mercatorY) >= 0 ? 30f : 5f;
                        var value = height + 32768f;
                        var red = (int)Math.Floor(value / 256f);
                        var green = (int)Math.Floor(value - red * 256f);
                        var index = (py * 256 + px) * 3;
                        rgb[index] = (byte)red;
                        rgb[index + 1] = (byte)green;
                        rgb[index + 2] = 0;
                    }
                return new OmsiRgbImage(256, 256, rgb);
            }
            var mosaic = OmsiAerial.HeightMosaic(geo, 0, 0, 300, 15, 0.0, Tile, CancellationToken.None);
            OmsiAerial.TileBox(geo, 0, 0, 300, 0.0, out var low, out var high);
            var levels = new OmsiWaterLevels(mask);
            levels.Add(low, high, mosaic.Height);
            levels.Finish();
            Assert.AreEqual(5.0, levels.Surface(0).Value, 0.2, "the bank, not the block");

            var raw = OmsiAerial.SampleHeights(geo, 0, 0, 300, 0.0, mosaic, null, null);
            var laid = OmsiAerial.SampleHeights(geo, 0, 0, 300, 0.0, mosaic, mask, levels);
            const int resolution = OmsiTerrain.Resolution;
            // The middle of the tile is the middle of the lake; the corner is far from it.
            Assert.AreEqual(30.0, raw[30 * resolution + 30], 0.3, "the data as it is");
            Assert.AreEqual(levels.Bed(0).Value, laid[30 * resolution + 30], 1e-3, "the bed of the lake");
            Assert.AreEqual(raw[0], laid[0], 1e-6f, "land is not touched");
            Assert.AreEqual(raw[60 * resolution + 60], laid[60 * resolution + 60], 1e-6f);
        }

        [Test]
        public void TheWaterSurfaceProposedForATileIsKeptInTheProjectAndCanBeTakenBack()
        {
            var settings = new OmsiGeoSettings();
            Assert.IsNull(settings.SuggestedWaterOf(3, -2));
            settings.SetSuggestedWater(3, -2, 2.125);
            settings.SetSuggestedWater(0, 0, -4.5);
            var again = OmsiGeoSettings.Parse(settings.ToString());
            Assert.AreEqual(2.125, again.SuggestedWaterOf(3, -2).Value, 1e-12);
            Assert.AreEqual(-4.5, again.SuggestedWaterOf(0, 0).Value, 1e-12);
            Assert.IsNull(again.SuggestedWaterOf(1, 1));
            again.SetSuggestedWater(3, -2, null);
            Assert.IsNull(OmsiGeoSettings.Parse(again.ToString()).SuggestedWaterOf(3, -2));
            Assert.IsNull(OmsiGeoSettings.Parse("latitude 52.5\nlongitude 13.4\n").SuggestedWaterOf(0, 0), "an older file has none");
        }

        [Test]
        public void TheLevelingOfWaterIsASettingThatDefaultsToOnAndIsKept()
        {
            Assert.IsTrue(OmsiGeoSettings.Parse("latitude 52.5\nlongitude 13.4\n").Heights.LevelWater, "an older file has no such line");
            var settings = OmsiGeoSettings.Parse("levelwater 0\n");
            Assert.IsFalse(settings.Heights.LevelWater);
            Assert.IsTrue(settings.ToString().Contains("levelwater 0\n"));
            settings.Heights.LevelWater = true;
            Assert.IsTrue(OmsiGeoSettings.Parse(settings.ToString()).Heights.LevelWater);
        }
    }
}
