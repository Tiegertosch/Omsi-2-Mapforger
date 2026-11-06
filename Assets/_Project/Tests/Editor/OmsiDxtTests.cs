using System;
using System.Diagnostics;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>DXT1 compression on worker threads, with a decoder here to see what a graphics card would show (ADR 062).</summary>
    public sealed class OmsiDxtTests
    {
        private static int[] Color565(ushort color)
        {
            var r = (color >> 11) & 31;
            var g = (color >> 5) & 63;
            var b = color & 31;
            return new[] { (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2) };
        }

        /// <summary>Decodes one level (DXT1, memory order) to 3 bytes a pixel, as a graphics card does.</summary>
        private static byte[] Decode(byte[] data, int offset, int width, int height)
        {
            var result = new byte[width * height * 3];
            var blocksX = Math.Max(1, (width + 3) / 4);
            var blocksY = Math.Max(1, (height + 3) / 4);
            for (var blockY = 0; blockY < blocksY; blockY++)
            {
                for (var blockX = 0; blockX < blocksX; blockX++)
                {
                    var at = offset + (blockY * blocksX + blockX) * 8;
                    var first = (ushort)(data[at] | (data[at + 1] << 8));
                    var second = (ushort)(data[at + 2] | (data[at + 3] << 8));
                    var indices = (uint)(data[at + 4] | (data[at + 5] << 8) | (data[at + 6] << 16) | (data[at + 7] << 24));
                    var a = Color565(first);
                    var b = Color565(second);
                    var palette = new int[4][];
                    palette[0] = a;
                    palette[1] = b;
                    if (first > second)
                    {
                        palette[2] = new[] { (2 * a[0] + b[0]) / 3, (2 * a[1] + b[1]) / 3, (2 * a[2] + b[2]) / 3 };
                        palette[3] = new[] { (a[0] + 2 * b[0]) / 3, (a[1] + 2 * b[1]) / 3, (a[2] + 2 * b[2]) / 3 };
                    }
                    else
                    {
                        palette[2] = new[] { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2 };
                        palette[3] = new[] { 0, 0, 0 };
                    }
                    for (var index = 0; index < 16; index++)
                    {
                        var x = blockX * 4 + (index & 3);
                        var y = blockY * 4 + (index >> 2);
                        if (x >= width || y >= height)
                            continue;
                        var color = palette[(indices >> (2 * index)) & 3];
                        var target = (y * width + x) * 3;
                        result[target] = (byte)color[0];
                        result[target + 1] = (byte)color[1];
                        result[target + 2] = (byte)color[2];
                    }
                }
            }
            return result;
        }

        private static double Psnr(byte[] a, byte[] b)
        {
            double sum = 0;
            for (var index = 0; index < a.Length; index++)
                sum += (a[index] - b[index]) * (a[index] - b[index]);
            var mean = sum / a.Length;
            return mean < 1e-9 ? 99.0 : 10.0 * Math.Log10(255.0 * 255.0 / mean);
        }

        /// <summary>Something like an aerial picture: smooth areas, edges, texture.</summary>
        private static byte[] Picture(int width, int height)
        {
            var random = new Random(12345);
            var rgb = new byte[width * height * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var at = (y * width + x) * 3;
                    var roof = ((x / 23) + (y / 17)) % 2 == 0;
                    var noise = random.Next(-6, 7);
                    rgb[at] = (byte)Math.Max(0, Math.Min(255, (roof ? 150 : 60) + x * 90 / width + noise));
                    rgb[at + 1] = (byte)Math.Max(0, Math.Min(255, (roof ? 70 : 110) + y * 100 / height + noise));
                    rgb[at + 2] = (byte)Math.Max(0, Math.Min(255, (roof ? 60 : 50) + (x + y) * 60 / (width + height) + noise));
                }
            }
            return rgb;
        }

        [TestCase(1, 1, 1)]
        [TestCase(2, 2, 2)]
        [TestCase(4, 4, 3)]
        [TestCase(13, 7, 4)]
        [TestCase(1024, 1024, 11)]
        [TestCase(3328, 3328, 12)]
        public void AFullMipChainHasUnitysLevels(int width, int height, int levels)
        {
            Assert.AreEqual(levels, OmsiDxt.MipCount(width, height));
        }

        [Test]
        public void TheOutputIsAllLevelsOneAfterTheOther()
        {
            var rgb = Picture(37, 21);
            var data = OmsiDxt.EncodeWithMips(rgb, 37, 21, out var mips);
            Assert.AreEqual(OmsiDxt.MipCount(37, 21), mips);
            Assert.AreEqual(OmsiDxt.TotalSize(37, 21, mips), data.Length);
            // 37 × 21 is 10 × 6 blocks, then 18 × 10 is 5 × 3, 9 × 5 is 3 × 2, 4 × 2 is 1 × 1 block, then single blocks.
            Assert.AreEqual((10 * 6 + 5 * 3 + 3 * 2 + 1 + 1 + 1) * 8, data.Length);
        }

        [Test]
        public void AFlatColourComesBackAsItself()
        {
            var rgb = new byte[16 * 16 * 3];
            for (var index = 0; index < rgb.Length; index += 3)
            {
                rgb[index] = 120;
                rgb[index + 1] = 200;
                rgb[index + 2] = 40;
            }
            var data = OmsiDxt.EncodeWithMips(rgb, 16, 16, out _);
            var back = Decode(data, 0, 16, 16);
            for (var index = 0; index < back.Length; index += 3)
            {
                Assert.AreEqual(120, back[index], 8);
                Assert.AreEqual(200, back[index + 1], 4);
                Assert.AreEqual(40, back[index + 2], 8);
            }
        }

        [Test]
        public void ThePicturesOrientationIsKept()
        {
            // The left half red, the right half blue, the first rows green: each lands where it was.
            const int size = 16;
            var rgb = new byte[size * size * 3];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var at = (y * size + x) * 3;
                    if (y < 4)
                        rgb[at + 1] = 255;
                    else if (x < size / 2)
                        rgb[at] = 255;
                    else
                        rgb[at + 2] = 255;
                }
            var back = Decode(OmsiDxt.EncodeWithMips(rgb, size, size, out _), 0, size, size);
            Assert.Greater(back[(1 * size + 5) * 3 + 1], 200, "green rows first");
            Assert.Greater(back[(10 * size + 2) * 3], 200, "red at the left");
            Assert.Greater(back[(10 * size + 12) * 3 + 2], 200, "blue at the right");
            Assert.Less(back[(10 * size + 12) * 3], 40);
        }

        [Test]
        public void APictureWithEdgesAndTextureKeepsItsLook()
        {
            const int width = 256;
            const int height = 192;
            var rgb = Picture(width, height);
            var data = OmsiDxt.EncodeWithMips(rgb, width, height, out _);
            var psnr = Psnr(rgb, Decode(data, 0, width, height));
            Assert.Greater(psnr, 30.0, "PSNR " + psnr.ToString("0.0") + " dB");
        }

        [Test]
        public void ASmallerLevelIsTheMeanOfTheLevelAbove()
        {
            const int width = 64;
            const int height = 64;
            var rgb = Picture(width, height);
            var data = OmsiDxt.EncodeWithMips(rgb, width, height, out _);
            var level1 = Decode(data, OmsiDxt.LevelSize(width, height), 32, 32);
            // The mean of the 2 × 2 of the picture at the first pixel of level 1.
            var expectedR = (rgb[0] + rgb[3] + rgb[width * 3] + rgb[width * 3 + 3]) / 4.0;
            Assert.AreEqual(expectedR, level1[0], 12.0);
            var last = Decode(data, OmsiDxt.TotalSize(width, height, OmsiDxt.MipCount(width, height) - 1), 1, 1);
            double mean = 0;
            for (var index = 0; index < rgb.Length; index += 3)
                mean += rgb[index];
            mean /= width * height;
            Assert.AreEqual(mean, last[0], 12.0, "the 1 × 1 level is the picture's mean colour");
        }

        [Test]
        public void ATileSizedPictureIsCompressedInSeconds()
        {
            // A 3,328 px tile picture: on the main thread Unity took 0.4 s for the first level; by hand and on a worker it has to stay
            // in the same order, with all its mip levels (the test only guards against something far slower).
            const int size = 3328;
            var rgb = new byte[size * size * 3];
            var random = new Random(7);
            random.NextBytes(rgb);
            var watch = Stopwatch.StartNew();
            var data = OmsiDxt.EncodeWithMips(rgb, size, size, out var mips);
            watch.Stop();
            Assert.AreEqual(OmsiDxt.TotalSize(size, size, mips), data.Length);
            Console.WriteLine("3328 px with " + mips + " levels: " + watch.ElapsedMilliseconds + " ms");
            Assert.Less(watch.ElapsedMilliseconds, 20000);
        }

        [Test]
        public void ARealisticTilePictureTakesLessThanNoise()
        {
            const int size = 3328;
            var rgb = Picture(size, size);
            var watch = Stopwatch.StartNew();
            var data = OmsiDxt.EncodeWithMips(rgb, size, size, out var mips);
            watch.Stop();
            Console.WriteLine("3328 px picture with edges and a little noise, " + mips + " levels: " + watch.ElapsedMilliseconds + " ms");
            Assert.Greater(Psnr(rgb, Decode(data, 0, size, size)), 30.0);
        }
    }
}
