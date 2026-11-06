using System;
using System.IO;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The folder of compressed tile pictures on this computer: held below its limit, oldest use first (ADR 062).</summary>
    public sealed class OmsiSharpStoreTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "omsi-store-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private static byte[] Bytes(int length, byte fill)
        {
            var bytes = new byte[length];
            for (var index = 0; index < length; index++)
                bytes[index] = fill;
            return bytes;
        }

        [Test]
        public void AProjectsCopiesLieUnderItsOwnKey()
        {
            var store = new OmsiSharpStore(root, 1000);
            var one = OmsiSharpStore.ProjectKey("C:\\Projects\\Map One.workspace");
            Assert.AreEqual(one, OmsiSharpStore.ProjectKey("c:\\projects\\map one.workspace\\"), "case and a closing slash do not matter");
            Assert.AreNotEqual(one, OmsiSharpStore.ProjectKey("C:\\Projects\\Map Two.workspace"));
            var file = store.FileFor(one, "tile_0_-1.map");
            StringAssert.StartsWith(root, file);
            StringAssert.EndsWith("tile_0_-1.map.sharp", file);
            StringAssert.Contains(one, file);
        }

        [Test]
        public void ACopyIsKeptAndGivenBack()
        {
            var store = new OmsiSharpStore(root, 10000);
            var file = store.FileFor("key", "tile_0_0.map");
            Assert.IsNull(store.Read(file));
            store.Write(file, Bytes(500, 7));
            CollectionAssert.AreEqual(Bytes(500, 7), store.Read(file));
            Assert.AreEqual(500, store.TotalBytes());
            store.Write(file, Bytes(300, 9));
            Assert.AreEqual(300, store.TotalBytes(), "writing again replaces the copy");
            store.Remove(file);
            Assert.AreEqual(0, store.TotalBytes());
            Assert.IsNull(store.Read(file));
        }

        [Test]
        public void TheCopiesUsedLongestAgoGoWhenTheLimitIsPassed()
        {
            var store = new OmsiSharpStore(root, 1000);
            var old = store.FileFor("key", "tile_0_0.map");
            var middle = store.FileFor("key", "tile_1_0.map");
            var fresh = store.FileFor("key", "tile_2_0.map");
            store.Write(old, Bytes(400, 1));
            store.Write(middle, Bytes(400, 2));
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-3));
            File.SetLastWriteTimeUtc(middle, DateTime.UtcNow.AddHours(-1));
            // 1,200 bytes are over the limit: the oldest goes, and so does enough to reach nine tenths (900).
            store.Write(fresh, Bytes(400, 3));
            Assert.IsNull(store.Read(old), "the oldest went");
            Assert.IsNotNull(store.Read(fresh), "what was just written stays");
            Assert.LessOrEqual(store.TotalBytes(), 900);
        }

        [Test]
        public void ReadingMarksACopyAsUsed()
        {
            var store = new OmsiSharpStore(root, 1000);
            var first = store.FileFor("key", "tile_0_0.map");
            var second = store.FileFor("key", "tile_1_0.map");
            store.Write(first, Bytes(400, 1));
            store.Write(second, Bytes(400, 2));
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddHours(-5));
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddHours(-4));
            // The older one is used now, so the other is the one that goes.
            Assert.IsNotNull(store.Read(first));
            store.Write(store.FileFor("key", "tile_2_0.map"), Bytes(400, 3));
            Assert.IsNotNull(store.Read(first));
            Assert.IsNull(store.Read(second));
        }

        [Test]
        public void ThereIsRoomUntilTheLimitIsReached()
        {
            var store = new OmsiSharpStore(root, 1000);
            Assert.IsTrue(store.HasRoom(1000));
            store.Write(store.FileFor("key", "tile_0_0.map"), Bytes(600, 1));
            Assert.IsTrue(store.HasRoom(400));
            Assert.IsFalse(store.HasRoom(401));
        }

        [Test]
        public void ClearingDeletesEveryCopyOfEveryProject()
        {
            var store = new OmsiSharpStore(root, 100000);
            store.Write(store.FileFor("one", "tile_0_0.map"), Bytes(100, 1));
            store.Write(store.FileFor("two", "tile_0_0.map"), Bytes(100, 2));
            Assert.AreEqual(200, store.TotalBytes());
            store.Clear();
            Assert.AreEqual(0, store.TotalBytes());
            Assert.IsNull(store.Read(store.FileFor("one", "tile_0_0.map")));
            Assert.IsEmpty(Directory.GetDirectories(root));
        }

        [Test]
        public void ACopyKeptBeforeTheStoreWasOpenedIsCounted()
        {
            var first = new OmsiSharpStore(root, 100000);
            first.Write(first.FileFor("key", "tile_0_0.map"), Bytes(250, 1));
            Assert.AreEqual(250, new OmsiSharpStore(root, 100000).TotalBytes());
        }
    }
}
