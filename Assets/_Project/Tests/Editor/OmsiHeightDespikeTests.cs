using System;
using System.Threading;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The world's elevation data holds single pixels far off their surroundings; they must not reach the map.</summary>
    public sealed class OmsiHeightDespikeTests
    {
        private const int Size = 32;

        private static void Encode(float metres, byte[] rgb, int pixel)
        {
            var value = metres + 32768f;
            var red = (int)Math.Floor(value / 256f);
            var green = (int)Math.Floor(value - red * 256f);
            var blue = (int)Math.Floor((value - red * 256f - green) * 256f);
            rgb[pixel * 3] = (byte)red;
            rgb[pixel * 3 + 1] = (byte)green;
            rgb[pixel * 3 + 2] = (byte)blue;
        }

        private static OmsiRgbImage Terrarium(Func<int, int, float> height, int size = Size)
        {
            var rgb = new byte[size * size * 3];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    Encode(height(x, y), rgb, y * size + x);
            return new OmsiRgbImage(size, size, rgb);
        }

        /// <summary>The height at the middle of a pixel of the one tile (1, 1) of zoom 2.</summary>
        private static double HeightAt(OmsiMosaic mosaic, int x, int y)
        {
            return mosaic.Height(new OmsiMapPoint((1 * Size + x + 0.5) / (4.0 * Size), (1 * Size + y + 0.5) / (4.0 * Size)));
        }

        private static OmsiMosaic Fetch(OmsiRgbImage tile)
        {
            return OmsiMosaic.Fetch(2, new OmsiMapPoint(0.26, 0.26), new OmsiMapPoint(0.27, 0.27), true, (z, x, y) => tile, CancellationToken.None);
        }

        [Test]
        public void ASinglePixelFarOffItsNeighboursIsReplacedByTheirMedian()
        {
            var mosaic = Fetch(Terrarium((x, y) => x == 10 && y == 10 ? -198.4f : 5f));
            Assert.AreEqual(5.0, HeightAt(mosaic, 10, 10), 1e-3);
        }

        [Test]
        public void ATwoByTwoSpikeAndOneAtTheEdgeOfTheTileGoToo()
        {
            var mosaic = Fetch(Terrarium((x, y) => (x >= 20 && x <= 21 && y >= 5 && y <= 6) || (x == 0 && y == 0) ? 80f : 5f));
            Assert.AreEqual(5.0, HeightAt(mosaic, 20, 5), 1e-3);
            Assert.AreEqual(5.0, HeightAt(mosaic, 21, 6), 1e-3);
            Assert.AreEqual(5.0, HeightAt(mosaic, 0, 0), 1e-3);
        }

        [Test]
        public void AStepSuchAsAQuayWallStaysAndSoDoesEveryHeightThatDoesNotStandOut()
        {
            // Water at 3 m, a quay at 12 m and a slope of 0.5 m a pixel behind it.
            var mosaic = Fetch(Terrarium((x, y) => x < 16 ? 3f : 12f + (x - 16) * 0.5f));
            Assert.AreEqual(3.0, HeightAt(mosaic, 15, 8), 1e-3);
            Assert.AreEqual(12.0, HeightAt(mosaic, 16, 8), 1e-3);
            Assert.AreEqual(12.0 + 10 * 0.5, HeightAt(mosaic, 26, 8), 1e-3);
        }

        [Test]
        public void ABlockOfNineHigherPixelsIsTakenForRealGround()
        {
            var mosaic = Fetch(Terrarium((x, y) => x >= 10 && x <= 12 && y >= 10 && y <= 12 ? 40f : 5f));
            Assert.AreEqual(40.0, HeightAt(mosaic, 11, 11), 1e-3);
        }

        [Test]
        public void ASpikeJustUnderTheLimitStays()
        {
            var mosaic = Fetch(Terrarium((x, y) => x == 10 && y == 10 ? 5f + (float)OmsiMosaic.SpikeMetres - 0.5f : 5f));
            Assert.AreEqual(5.0 + OmsiMosaic.SpikeMetres - 0.5, HeightAt(mosaic, 10, 10), 1e-2);
        }

        [Test]
        public void NoSpikeReachesAnyTerrainPointOfTwoTilesSideBySideAndTheirSharedEdgeAgrees()
        {
            // Flat 100 m with an isolated spike every 7 pixels of every source tile: no spike may reach any of the 61 x 61 points
            // of either tile, and the column the tiles share must have the same heights from both.
            OmsiRgbImage Spiky(int z, int x, int y)
            {
                return Terrarium((px, py) => px % 7 == 3 && py % 7 == 3 ? -198f : 100f, 256);
            }
            var geo = OmsiGeoreference.Placed(new OmsiGeoAnchor { Latitude = 52.5, Longitude = 13.4 }, 300);
            // Zoom 15 (pixels of about 3 m, as used) and zoom 13 (about 12 m, where a few pixels reach far beyond the terrain spacing).
            foreach (var zoom in new[] { 15, 13 })
                foreach (var smoothing in new[] { 0.0, 10.0 })
                {
                    var label = "zoom " + zoom + ", smoothing " + smoothing;
                    var west = OmsiAerial.SampleHeights(geo, 0, 0, 300, zoom, smoothing, Spiky, CancellationToken.None);
                    var east = OmsiAerial.SampleHeights(geo, 1, 0, 300, zoom, smoothing, Spiky, CancellationToken.None);
                    Assert.IsTrue(Array.TrueForAll(west, value => Math.Abs(value - 100f) < 1e-3f), "west tile, " + label);
                    Assert.IsTrue(Array.TrueForAll(east, value => Math.Abs(value - 100f) < 1e-3f), "east tile, " + label);
                    const int resolution = OmsiTerrain.Resolution;
                    for (var row = 0; row < resolution; row++)
                        Assert.AreEqual(west[row * resolution + resolution - 1], east[row * resolution], 1e-4f, label);
                }
        }
    }
}
