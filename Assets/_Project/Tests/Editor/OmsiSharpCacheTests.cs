using System;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The kept compressed copy of a tile's full picture, so panning over the map does not decode and compress again.</summary>
    public sealed class OmsiSharpCacheTests
    {
        private static byte[] Data(int length)
        {
            var data = new byte[length];
            for (var index = 0; index < length; index++)
                data[index] = (byte)(index * 7 + 3);
            return data;
        }

        [Test]
        public void ACacheFileGivesBackItsTexture()
        {
            var data = Data(5000);
            var file = OmsiSharpCache.Write(3328, 3328, 13, 10, "123:456", data);
            Assert.IsTrue(OmsiSharpCache.TryRead(file, "123:456", out var width, out var height, out var mips, out var format, out var offset, out var length));
            Assert.AreEqual(3328, width);
            Assert.AreEqual(3328, height);
            Assert.AreEqual(13, mips);
            Assert.AreEqual(10, format);
            Assert.AreEqual(5000, length);
            var back = new byte[length];
            Array.Copy(file, offset, back, 0, length);
            CollectionAssert.AreEqual(data, back);
        }

        [Test]
        public void ACacheMadeForAnotherPictureIsNotUsed()
        {
            var file = OmsiSharpCache.Write(64, 64, 7, 10, "123:456", Data(100));
            Assert.IsFalse(OmsiSharpCache.TryRead(file, "123:457", out _, out _, out _, out _, out _, out _));
            Assert.IsFalse(OmsiSharpCache.TryRead(file, null, out _, out _, out _, out _, out _, out _));
        }

        [Test]
        public void ACutOrForeignFileIsNotUsed()
        {
            var file = OmsiSharpCache.Write(64, 64, 7, 10, "a", Data(100));
            var cut = new byte[file.Length - 10];
            Array.Copy(file, cut, cut.Length);
            Assert.IsFalse(OmsiSharpCache.TryRead(cut, "a", out _, out _, out _, out _, out _, out _));
            var longer = new byte[file.Length + 1];
            Array.Copy(file, longer, file.Length);
            Assert.IsFalse(OmsiSharpCache.TryRead(longer, "a", out _, out _, out _, out _, out _, out _));
            Assert.IsFalse(OmsiSharpCache.TryRead(new byte[] { 1, 2, 3 }, "a", out _, out _, out _, out _, out _, out _));
            Assert.IsFalse(OmsiSharpCache.TryRead(null, "a", out _, out _, out _, out _, out _, out _));
            var wrong = (byte[])file.Clone();
            wrong[0] = (byte)'X';
            Assert.IsFalse(OmsiSharpCache.TryRead(wrong, "a", out _, out _, out _, out _, out _, out _));
        }

        [Test]
        public void TheCacheFileLiesBesideThePictureAndAFreshPictureTakesItAway()
        {
            Assert.AreEqual("Mapforge\\Imagery\\tile_0_-1.map.sharp", OmsiSharpCache.PathFor("Mapforge\\Imagery\\tile_0_-1.map.jpg"));
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omsi-sharp-" + Guid.NewGuid().ToString("N"));
            var original = System.IO.Path.Combine(root, "map");
            System.IO.Directory.CreateDirectory(original);
            try
            {
                var workspace = new OmsiMapWorkspace(original, System.IO.Path.Combine(root, "workspace"));
                var tilePath = "Mapforge\\Imagery\\tile_0_0.map";
                workspace.Save(tilePath + ".jpg", new byte[] { 1, 2, 3, 4 });
                var stamp = workspace.Stamp(tilePath + ".jpg");
                Assert.IsNotNull(stamp);
                var sharp = OmsiSharpCache.PathFor(tilePath + ".jpg");
                workspace.Save(sharp, OmsiSharpCache.Write(8, 8, 4, 10, stamp, Data(64)));
                Assert.IsTrue(workspace.IsModified(sharp));
                // The name a cache carries leaves out the workspace's own counter, so it holds from one session to the next.
                var inner = stamp.Substring(stamp.IndexOf(':') + 1);
                var again = new OmsiMapWorkspace(original, System.IO.Path.Combine(root, "workspace")).Stamp(tilePath + ".jpg");
                Assert.AreEqual(inner, again.Substring(again.IndexOf(':') + 1));
            }
            finally
            {
                System.IO.Directory.Delete(root, true);
            }
        }
    }
}
