using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiNewMapTests
    {
        private string mapsFolder;
        private string templateFolder;

        [SetUp]
        public void CreateTemplate()
        {
            mapsFolder = Path.Combine(Path.GetTempPath(), "mapforge-newmap-" + Guid.NewGuid().ToString("N"), "maps");
            templateFolder = Path.Combine(mapsFolder, "Template");
            Directory.CreateDirectory(Path.Combine(templateFolder, "texture", "map"));
            Directory.CreateDirectory(Path.Combine(templateFolder, "TTData"));
            Write("global.cfg", string.Join("\r\n",
                "File created with OMSI-Editor on 26.11.2013 22:04:30", "",
                "[name]", "Template", "",
                "[friendlyname]", "Template (Tutorial)", "",
                "[description]", "A village.", "Lines: 76", "[end]", "",
                "[version]", "14", "",
                "[NextIDCode]", "8978", "",
                "[worldcoordinates]", "",
                "[backgroundimage]", "0", "", "300", "300", "0", "0", "",
                "[mapcam]", "1", "2", "186.8", "9.0", "152.8", "166.8", "-13.4", "42.5", "",
                "[moneysystem]", @"Money\DM\DM.cfg", "",
                "[groundtex]", @"texture\gras.bmp", @"texture\gras_det.bmp", "0", "1", "60", "",
                " Winter:",
                "[addseason]", "3", "38", "75", "",
                "[entrypoints]", "1", "4", "103", "0", "202.000", "9.000", "136.000", "0.000", "0.173", "0.000", "0.985", "4", "Nordspitze", "",
                "[map]", "0", "0", "tile_0_0.map", "",
                "[map]", "1", "0", "tile_1_0.map", "") + "\r\n", Encoding.Unicode, true);
            foreach (var helper in new[] { "ailists.cfg", "drivers.txt", "humans.txt", "parklist_p.txt", "registrations.txt", "Holidays.txt" })
                Write(helper, helper, Encoding.ASCII, false);
            foreach (var placeBound in new[] { "tile_0_0.map", "tile_0_0.map.terrain", "global_DEU.dsc", "laststn.osn", "signalroutes.cfg", "picture.jpg" })
                Write(placeBound, "template only", Encoding.ASCII, false);
            Write(Path.Combine("texture", "water.tga"), "water", Encoding.ASCII, false);
            Write(Path.Combine("texture", "map", "tile_0_0.map.roadmap.bmp"), "roadmap", Encoding.ASCII, false);
            Write(Path.Combine("TTData", "76.ttl"), "line", Encoding.ASCII, false);
        }

        [TearDown]
        public void DeleteMaps()
        {
            var root = Directory.GetParent(mapsFolder).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void CreatesAMapWithOneFlatTileAndTheTemplatesHelperFiles()
        {
            var created = new DateTime(2026, 10, 23, 18, 30, 0);
            var report = OmsiNewMap.Create(Path.Combine(templateFolder, "global.cfg"), "Neustadt", "Neustadt (Test)", created);

            var target = Path.Combine(mapsFolder, "Neustadt");
            Assert.AreEqual(target, report.MapFolder);
            CollectionAssert.AreEquivalent(new[] { "ailists.cfg", "drivers.txt", "humans.txt", "parklist_p.txt", "registrations.txt",
                "Holidays.txt", Path.Combine("texture", "water.tga") }, report.CopiedFiles);
            Assert.IsFalse(File.Exists(Path.Combine(target, "laststn.osn")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "global_DEU.dsc")));
            Assert.IsFalse(Directory.Exists(Path.Combine(target, "texture", "map")));
            Assert.IsTrue(Directory.Exists(Path.Combine(target, "TTData")));
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.Combine(target, "TTData")));
            StringAssert.StartsWith("-----------------------\r\nSignal Routes File", File.ReadAllText(Path.Combine(target, "signalroutes.cfg")));
            Assert.IsFalse(Directory.Exists(target + ".mapforge-new-tmp"));

            var summary = OmsiMapReader.Read(report.GlobalConfigPath);
            Assert.AreEqual("Neustadt", summary.Name);
            Assert.AreEqual("Neustadt (Test)", summary.FriendlyName);
            Assert.AreEqual("A new map, created with OMSI Map Editor.", summary.Description);
            Assert.AreEqual(1, summary.NextIdCode);
            Assert.IsFalse(summary.UsesWorldCoordinates);
            Assert.AreEqual(300.0, summary.TileSizeMeters);
            var tile = summary.Tiles.Single();
            Assert.AreEqual(0, tile.X);
            Assert.AreEqual(0, tile.Y);
            Assert.IsTrue(tile.IsAvailable);
            Assert.AreEqual(0, tile.ObjectCount);
            Assert.IsTrue(tile.HasTerrain);
            Assert.AreEqual(0f, tile.TerrainMinHeight);
            Assert.AreEqual(0f, tile.TerrainMaxHeight);

            var config = OmsiRecordFile.Load(report.GlobalConfigPath, OmsiGlobalConfigLayouts.Instance);
            Assert.AreEqual(OmsiTextEncoding.Utf16LittleEndian, config.Encoding);
            Assert.AreEqual("File created with OMSI Map Editor on 23.10.2026 18:30:00", config.Preamble[0].Text);
            var names = config.Records.Select(record => record.Name).ToList();
            CollectionAssert.DoesNotContain(names, "entrypoints");
            CollectionAssert.DoesNotContain(names, "backgroundimage");
            CollectionAssert.DoesNotContain(names, "worldcoordinates");
            Assert.AreEqual(1, names.Count(name => name == "map"));
            // Settings of the template stay.
            Assert.AreEqual(@"texture\gras.bmp", config.Records.Single(record => record.Name == "groundtex").GetField(0));
            Assert.AreEqual(@"Money\DM\DM.cfg", config.Records.Single(record => record.Name == "moneysystem").GetField(0));
            var camera = config.Records.Single(record => record.Name == "mapcam");
            CollectionAssert.AreEqual(new[] { "0", "0", "150", "50", "150", "166.8", "-13.4", "42.5" }, camera.AllValues().Take(8).ToArray());
            Assert.IsTrue(config.IsExactRoundTrip);

            var tileText = OmsiTextFile.Decode(File.ReadAllBytes(Path.Combine(target, "tile_0_0.map")), out var tileEncoding);
            Assert.AreEqual(OmsiTextEncoding.Utf16LittleEndian, tileEncoding);
            StringAssert.Contains("[version]\r\n14\r\n", tileText);
            Assert.AreEqual(4 + 61 * 61 * 4, File.ReadAllBytes(Path.Combine(target, "tile_0_0.map.terrain")).Length);
        }

        [Test]
        public void RefusesNamesThatAreTakenOrInvalid()
        {
            StringAssert.Contains("already exists", OmsiNewMap.CheckFolderName(mapsFolder, "Template"));
            Assert.IsNotNull(OmsiNewMap.CheckFolderName(mapsFolder, ""));
            Assert.IsNotNull(OmsiNewMap.CheckFolderName(mapsFolder, "Bad:Name"));
            Assert.IsNotNull(OmsiNewMap.CheckFolderName(mapsFolder, " Space"));
            Assert.IsNotNull(OmsiNewMap.CheckFolderName(mapsFolder, "Dot."));
            Assert.IsNull(OmsiNewMap.CheckFolderName(mapsFolder, "Neustadt"));

            Assert.Throws<InvalidOperationException>(() =>
                OmsiNewMap.Create(Path.Combine(templateFolder, "global.cfg"), "Template", "Copy", DateTime.Now));
            Assert.AreEqual("template only", File.ReadAllText(Path.Combine(templateFolder, "tile_0_0.map")));
        }

        [Test]
        public void AddsNamesAndDescriptionToATemplateWithoutThem()
        {
            var template = OmsiRecordFile.Parse(Encoding.ASCII.GetBytes("[version]\r\n14\r\n\r\n[groundtex]\r\na\r\nb\r\n0\r\n1\r\n60\r\n"),
                OmsiGlobalConfigLayouts.Instance);
            var bytes = OmsiNewMap.CreateGlobalConfig(template, "Neu", "Neu (Karte)", new DateTime(2026, 1, 2));
            var config = OmsiRecordFile.Parse(bytes, OmsiGlobalConfigLayouts.Instance);
            var names = config.Records.Select(record => record.Name).ToList();
            CollectionAssert.AreEqual(new[] { "name", "friendlyname", "description", "end", "version", "NextIDCode", "groundtex", "map" }, names);
            Assert.AreEqual("Neu (Karte)", config.Records[1].GetField(0));
        }

        private void Write(string relative, string text, Encoding encoding, bool byteOrderMark)
        {
            var bytes = encoding.GetBytes(text);
            if (byteOrderMark)
                bytes = encoding.GetPreamble().Concat(bytes).ToArray();
            File.WriteAllBytes(Path.Combine(templateFolder, relative), bytes);
        }
    }
}
