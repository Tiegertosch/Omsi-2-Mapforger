using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiObjectLibraryInfoTests
    {
        [Test]
        public void ReadsTheGroupsOfASceneryObject()
        {
            var text = "[friendlyname]\r\nRow house\r\n\r\n[groups]\r\n3\r\nBuildings\r\nResidential\r\nRow House\r\n\r\n[mesh]\r\nmodel\\house.o3d\r\n";
            var groups = OmsiSceneryObjectGroups.Read(Encoding.UTF8.GetBytes(text));

            CollectionAssert.AreEqual(new[] { "Buildings", "Residential", "Row House" }, groups);
            CollectionAssert.IsEmpty(OmsiSceneryObjectGroups.Read(Encoding.UTF8.GetBytes("[mesh]\r\nmodel\\a.o3d\r\n")));
            // A count larger than the list stops at the next section; a group named twice counts once.
            CollectionAssert.AreEqual(new[] { "Trees" },
                OmsiSceneryObjectGroups.Read(Encoding.UTF8.GetBytes("[groups]\n4\nTrees\ntrees\n[mesh]\nx.o3d\n")));
        }

        [Test]
        public void NamesPreviewsLikeNEditor()
        {
            // Both names are taken from nEditor's preview cache in a real OMSI 2 installation.
            Assert.AreEqual("30er_reihensiedlung01_scoda73ca14943e9819290f8851762d30c6.png",
                NEditorThumbnails.FileNameFor(@"Sceneryobjects\Ruede\30er_reihensiedlung01.sco"));
            Assert.AreEqual("Ampel_Bus_1_sco8c1562219dd30d64e434318999eb4379.png",
                NEditorThumbnails.FileNameFor("Sceneryobjects/Verkehrszeichen_MC/Ampel_Bus_1.sco"));
        }

        [Test]
        public void FindsPreviewsInTheInstallation()
        {
            var root = Path.Combine(Path.GetTempPath(), "mapforge-thumbs-" + Guid.NewGuid().ToString("N"));
            try
            {
                var folder = Path.Combine(root, "Sceneryobjects", NEditorThumbnails.FolderName);
                Directory.CreateDirectory(folder);
                var name = NEditorThumbnails.FileNameFor(@"Sceneryobjects\Generic\bench.sco");
                File.WriteAllBytes(Path.Combine(folder, name), new byte[] { 0x89, 0x50 });

                var thumbnails = NEditorThumbnails.ForInstallation(root);

                Assert.AreEqual(1, thumbnails.Count);
                Assert.AreEqual(Path.Combine(folder, name), thumbnails.PathFor(@"Sceneryobjects\Generic\bench.sco"));
                Assert.IsNull(thumbnails.PathFor(@"Sceneryobjects\Generic\lamp.sco"));
                Assert.IsNull(NEditorThumbnails.ForInstallation(Path.Combine(root, "missing")).PathFor(@"Sceneryobjects\Generic\bench.sco"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
