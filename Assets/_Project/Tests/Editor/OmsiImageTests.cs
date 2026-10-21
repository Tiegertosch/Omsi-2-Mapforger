using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiImageTests
    {
        [Test]
        public void ReadsBottomUpBmpFilesTopRowFirst()
        {
            // 2 × 2, 24 bit; stored rows: bottom (blue, white), then top (red, green). Each row is padded to 8 bytes.
            var bytes = Bmp(2, 2, 24, new byte[]
            {
                255, 0, 0, 255, 255, 255, 0, 0,
                0, 0, 255, 0, 255, 0, 0, 0
            });

            Assert.IsTrue(OmsiImageReader.TryDecode(bytes, out var image));

            Assert.AreEqual(4, image.Channels);
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, image.Pixels);
        }

        [Test]
        public void ReadsPalettedBmpFiles()
        {
            // 8 bit with a two-colour palette (BGRA order); one row of indices 1, 0 padded to four bytes.
            var bytes = Bmp(2, 1, 8, new byte[] { 1, 0, 0, 0 }, new byte[] { 0, 0, 0, 0, 10, 20, 30, 0 });

            Assert.IsTrue(OmsiImageReader.TryDecode(bytes, out var image));

            CollectionAssert.AreEqual(new byte[] { 30, 20, 10, 255, 0, 0, 0, 255 }, image.Pixels);
        }

        [Test]
        public void ReadsRunLengthTgaFiles()
        {
            // 3 × 1, 32 bit, top-left origin: a run of two red pixels, then one literal blue pixel (BGRA).
            var header = new byte[18];
            header[2] = 10;
            header[12] = 3;
            header[14] = 1;
            header[16] = 32;
            header[17] = 0x20;
            var bytes = header.Concat(new byte[] { 0x81, 0, 0, 255, 128, 0x00, 255, 0, 0, 255 }).ToArray();

            Assert.IsTrue(OmsiImageReader.TryDecode(bytes, out var image));

            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 128, 255, 0, 0, 128, 0, 0, 255, 255 }, image.Pixels);
        }

        [Test]
        public void ReadsAlphaMasksAsOneChannel()
        {
            var bytes = Dds(2, 2, 0x2, 0, 8, 0, 0, 0, 0xFF, new byte[] { 0, 64, 128, 255 });

            Assert.IsTrue(OmsiImageReader.TryDecode(bytes, out var image));

            Assert.AreEqual(1, image.Channels);
            CollectionAssert.AreEqual(new byte[] { 0, 64, 128, 255 }, image.Pixels);
        }

        [Test]
        public void DecodesDxt1AndDxt5Blocks()
        {
            // DXT1: colour 0 pure red, colour 1 pure blue; first pixel index 0, second index 1, the rest index 0.
            var dxt1 = Dds(4, 4, 0x4, 0x31545844, 0, 0, 0, 0, 0, new byte[] { 0x00, 0xF8, 0x1F, 0x00, 0x04, 0x00, 0x00, 0x00 });
            // DXT5: alpha 255 and 0 with the first pixel at index 1 (alpha 0), colour all red.
            var dxt5 = Dds(4, 4, 0x4, 0x35545844, 0, 0, 0, 0, 0, new byte[] { 255, 0, 1, 0, 0, 0, 0, 0, 0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0 });

            Assert.IsTrue(OmsiImageReader.TryDecode(dxt1, out var first));
            Assert.IsTrue(OmsiImageReader.TryDecode(dxt5, out var second));

            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, first.Pixels.Take(4).ToArray());
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, first.Pixels.Skip(4).Take(4).ToArray());
            Assert.AreEqual(0, second.Pixels[3]);
            Assert.AreEqual(255, second.Pixels[7]);
            Assert.AreEqual(255, second.Pixels[0]);
        }

        [Test]
        public void LeavesOtherFormatsToTheCallerAndRejectsDamagedFiles()
        {
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };
            var truncated = Bmp(2, 2, 24, new byte[16]).Take(60).ToArray();

            Assert.IsFalse(OmsiImageReader.TryDecode(jpeg, out _));
            Assert.Throws<InvalidDataException>(() => OmsiImageReader.TryDecode(truncated, out _));
            Assert.Throws<InvalidDataException>(() => OmsiImageReader.TryDecode(Dds(4, 4, 0x4, 0x30315844, 0, 0, 0, 0, 0, new byte[16]), out _));
        }

        [Test]
        public void ReadsGroundTexturesAndTheirMaskPaths()
        {
            var folder = Path.Combine(Path.GetTempPath(), "mapforge-ground-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "global.cfg"), OmsiTextFile.Encode(string.Join("\r\n",
                    "[groundtex]", @"texture\gras.bmp", @"texture\gras_det.bmp", "0", "1", "60", "",
                    "[groundtex]", @"Texture\str_k_kopfstein.bmp", @"Texture\noise_low.bmp", "9", "16", "4", "",
                    "[map]", "0", "-1", "tile_0_-1.map", "") + "\r\n", OmsiTextEncoding.Utf16LittleEndian));

                var textures = OmsiGroundTextures.Read(new OmsiFolderSource(folder));

                Assert.AreEqual(2, textures.Count);
                Assert.IsFalse(textures[0].HasMask);
                Assert.AreEqual(@"texture\gras.bmp", textures[0].TexturePath);
                Assert.AreEqual(60.0, textures[0].DetailRepeats);
                Assert.AreEqual(1, textures[1].Index);
                Assert.AreEqual(9, textures[1].MaskSizeExponent);
                Assert.AreEqual(16.0, textures[1].Repeats);
                Assert.AreEqual(Path.Combine("texture", "map", "tile_0_-1.map.1.dds"), textures[1].MaskPath("tile_0_-1.map"));
                Assert.Throws<InvalidOperationException>(() => textures[0].MaskPath("tile_0_-1.map"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static byte[] Bmp(int width, int height, int bits, byte[] pixels, byte[] palette = null)
        {
            palette = palette ?? new byte[0];
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                var dataOffset = 54 + palette.Length;
                writer.Write(Encoding.ASCII.GetBytes("BM"));
                writer.Write(dataOffset + pixels.Length);
                writer.Write(0);
                writer.Write(dataOffset);
                writer.Write(40);
                writer.Write(width);
                writer.Write(height);
                writer.Write((short)1);
                writer.Write((short)bits);
                writer.Write(0);
                writer.Write(pixels.Length);
                writer.Write(2835);
                writer.Write(2835);
                writer.Write(palette.Length / 4);
                writer.Write(0);
                writer.Write(palette);
                writer.Write(pixels);
                return stream.ToArray();
            }
        }

        private static byte[] Dds(int width, int height, int formatFlags, int fourCc, int bits, int red, int green, int blue, int alpha, byte[] data)
        {
            var bytes = new byte[128 + data.Length];
            Encoding.ASCII.GetBytes("DDS ").CopyTo(bytes, 0);
            BitConverter.GetBytes(124).CopyTo(bytes, 4);
            BitConverter.GetBytes(height).CopyTo(bytes, 12);
            BitConverter.GetBytes(width).CopyTo(bytes, 16);
            BitConverter.GetBytes(32).CopyTo(bytes, 76);
            BitConverter.GetBytes(formatFlags).CopyTo(bytes, 80);
            BitConverter.GetBytes(fourCc).CopyTo(bytes, 84);
            BitConverter.GetBytes(bits).CopyTo(bytes, 88);
            BitConverter.GetBytes(red).CopyTo(bytes, 92);
            BitConverter.GetBytes(green).CopyTo(bytes, 96);
            BitConverter.GetBytes(blue).CopyTo(bytes, 100);
            BitConverter.GetBytes(alpha).CopyTo(bytes, 104);
            data.CopyTo(bytes, 128);
            return bytes;
        }
    }
}
