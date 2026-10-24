using System;
using System.Collections.Generic;
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
            Assert.AreEqual(2, summary.NextIdCode);
            Assert.IsFalse(summary.UsesWorldCoordinates);
            Assert.AreEqual(300.0, summary.TileSizeMeters);
            var tile = summary.Tiles.Single();
            Assert.AreEqual(0, tile.X);
            Assert.AreEqual(0, tile.Y);
            Assert.IsTrue(tile.IsAvailable);
            // The entry point object, without which OMSI crashes while loading the map.
            var entryObject = tile.Objects.Single();
            Assert.AreEqual(@"Sceneryobjects\Generic\entrypoint_bus.sco", entryObject.AssetPath);
            Assert.AreEqual(1, entryObject.Id);
            Assert.AreEqual(150.0, entryObject.LocalX);
            Assert.AreEqual(150.0, entryObject.LocalY);
            Assert.IsTrue(tile.HasTerrain);
            Assert.AreEqual(0f, tile.TerrainMinHeight);
            Assert.AreEqual(0f, tile.TerrainMaxHeight);

            var config = OmsiRecordFile.Load(report.GlobalConfigPath, OmsiGlobalConfigLayouts.Instance);
            Assert.AreEqual(OmsiTextEncoding.Utf16LittleEndian, config.Encoding);
            Assert.AreEqual("File created with OMSI Map Editor on 23.10.2026 18:30:00", config.Preamble[0].Text);
            var names = config.Records.Select(record => record.Name).ToList();
            // The template's entry point is replaced by one on the new tile's object.
            var entryPoints = config.Records.Single(record => record.Name == "entrypoints");
            CollectionAssert.AreEqual(new[] { "1", "0", "1", "0", "150.000", "0.000", "150.000", "0.000000", "0.000000", "0.000000", "1.000000", "0", "Start" },
                entryPoints.AllValues().Take(13).ToArray());
            Assert.AreEqual(names.IndexOf("map") - 1, names.IndexOf("entrypoints"));
            CollectionAssert.DoesNotContain(names, "backgroundimage");
            CollectionAssert.DoesNotContain(names, "worldcoordinates");
            Assert.AreEqual(1, names.Count(name => name == "map"));
            // Settings of the template stay.
            Assert.AreEqual(@"texture\gras.bmp", config.Records.Single(record => record.Name == "groundtex").GetField(0));
            Assert.AreEqual(@"Money\DM\DM.cfg", config.Records.Single(record => record.Name == "moneysystem").GetField(0));
            var camera = config.Records.Single(record => record.Name == "mapcam");
            CollectionAssert.AreEqual(new[] { "0", "0", "150", "50", "150", "166.8", "-13.4", "42.5" }, camera.AllValues().Take(8).ToArray());
            Assert.IsTrue(config.IsExactRoundTrip);
            var check = OmsiMapCheck.Run(summary, new OmsiFolderSource(target), null);
            var entryCheck = check.Sections.Single(section => section.Title == "Entry points");
            Assert.AreEqual(OmsiCheckStatus.Passed, entryCheck.Status, string.Join("\n", entryCheck.Findings));
            Assert.AreEqual("1 of 1 entry points lie on their object.", entryCheck.Summary);

            var tileText = OmsiTextFile.Decode(File.ReadAllBytes(Path.Combine(target, "tile_0_0.map")), out var tileEncoding);
            Assert.AreEqual(OmsiTextEncoding.Utf16LittleEndian, tileEncoding);
            StringAssert.Contains("[version]\r\n14\r\n", tileText);
            Assert.AreEqual(4 + 61 * 61 * 4, File.ReadAllBytes(Path.Combine(target, "tile_0_0.map.terrain")).Length);
        }

        [Test]
        public void TakesTheChosenSizeHeightTrafficYearsMoneySeasonsAndGround()
        {
            var templatePath = Path.Combine(templateFolder, "global.cfg");
            var options = OmsiNewMapOptions.FromTemplate(templatePath);
            Assert.AreEqual(@"Money\DM\DM.cfg", options.MoneySystem);
            Assert.AreEqual(@"texture\gras.bmp", options.GroundTexture);
            Assert.IsFalse(options.LeftHandTraffic);
            options.Description = "Hills and fields.\nLine 1: Town <=> Village";
            options.Size = 3;
            options.GroundHeight = 42.5f;
            options.LeftHandTraffic = true;
            options.RealRail = true;
            options.CarTraffic = 0.5;
            options.PassengerTraffic = 2.0;
            options.FirstYear = 1990;
            options.LastYear = 1995;
            options.YearOffset = -30;
            options.MoneySystem = @"Money\Euro\Euro.cfg";
            options.TicketPack = @"TicketPacks\Hamburg\Hamburg.otp";
            options.Seasons = OmsiSeasonPreset.MildWinter;
            options.GroundTexture = @"Texture\feld01.bmp";

            var report = OmsiNewMap.Create(templatePath, "Hills", "Hills", options, new DateTime(2026, 10, 24));

            var summary = OmsiMapReader.Read(report.GlobalConfigPath);
            Assert.AreEqual(9, summary.Tiles.Count);
            Assert.AreEqual(0, summary.Tiles[0].X);
            Assert.AreEqual(0, summary.Tiles[0].Y);
            CollectionAssert.AreEquivalent(new[] { -1, 0, 1 }, summary.Tiles.Select(tile => tile.X).Distinct().ToArray());
            CollectionAssert.AreEquivalent(new[] { -1, 0, 1 }, summary.Tiles.Select(tile => tile.Y).Distinct().ToArray());
            Assert.IsTrue(summary.Tiles.All(tile => tile.HasTerrain && tile.TerrainMinHeight == 42.5f && tile.TerrainMaxHeight == 42.5f));
            Assert.AreEqual(1, summary.Tiles.Sum(tile => tile.ObjectCount));
            StringAssert.Contains("Line 1: Town <=> Village", summary.Description);

            var config = OmsiRecordFile.Load(report.GlobalConfigPath, OmsiGlobalConfigLayouts.Instance);
            OmsiRecord Single(string name) => config.Records.Single(record => record.Name == name);
            List<OmsiRecord> All(string name) => config.Records.Where(record => record.Name == name).ToList();
            Assert.AreEqual(1, All("LHT").Count);
            Assert.AreEqual(1, All("realrail").Count);
            CollectionAssert.AreEqual(new[] { "1990", "1995" }, Single("years").AllValues().Take(2).ToArray());
            Assert.AreEqual("-30", Single("realyearoffset").GetField(0));
            Assert.AreEqual(@"Money\Euro\Euro.cfg", Single("moneysystem").GetField(0));
            Assert.AreEqual(@"TicketPacks\Hamburg\Hamburg.otp", Single("ticketpack").GetField(0));
            Assert.AreEqual(@"Texture\feld01.bmp", Single("groundtex").GetField(0));
            Assert.AreEqual(@"texture\gras_det.bmp", Single("groundtex").GetField(1));
            // A mild year: four seasons, none with snow, each below its comment; the template's comment is gone.
            var seasons = All("addseason");
            CollectionAssert.AreEqual(new[] { "3", "1", "2", "3" }, seasons.Select(season => season.GetField(0)).ToArray());
            var text = OmsiTextFile.Decode(File.ReadAllBytes(report.GlobalConfigPath), out _);
            StringAssert.Contains("\r\n Winter:\r\n[addseason]\r\n3\r\n0\r\n75\r\n", text);
            Assert.AreEqual(4, text.Split(new[] { " Winter:", " Spring:", " Autumn:" }, StringSplitOptions.None).Length - 1);
            // The template has no curves: the standard day, halved for cars and doubled for passengers.
            var cars = All("trafficdensity_road");
            Assert.AreEqual(11, cars.Count);
            CollectionAssert.AreEqual(new[] { "7.000", "0.750" }, cars[3].AllValues().Take(2).ToArray());
            CollectionAssert.AreEqual(new[] { "7.000", "2.400" }, All("trafficdensity_passenger")[3].AllValues().Take(2).ToArray());
            // The entry point stands on the ground, the camera 50 m above it.
            Assert.AreEqual("42.500", Single("entrypoints").GetField(5));
            Assert.AreEqual("92.5", Single("mapcam").GetField(3));
            Assert.AreEqual("tile_0_0.map", All("map")[0].GetField(2));
            Assert.AreEqual(9, All("map").Count);
            var check = OmsiMapCheck.Run(summary, new OmsiFolderSource(report.MapFolder), null);
            Assert.AreEqual(OmsiCheckStatus.Passed, check.Sections.Single(section => section.Title == "Entry points").Status);

            // Summer only takes all seasons out; an unchanged option keeps the template's line.
            options.Seasons = OmsiSeasonPreset.SummerOnly;
            options.MoneySystem = @"Money\DM\DM.cfg";
            var summer = OmsiRecordFile.Parse(OmsiNewMap.CreateGlobalConfig(OmsiRecordFile.Load(templatePath, OmsiGlobalConfigLayouts.Instance), "S", "S",
                options, DateTime.Now), OmsiGlobalConfigLayouts.Instance);
            Assert.IsFalse(summer.Records.Any(record => record.Name == "addseason"));
            StringAssert.DoesNotContain("Winter", OmsiTextFile.Decode(summer.ToBytes(), out _));
            Assert.AreEqual(@"Money\DM\DM.cfg", summer.Records.Single(record => record.Name == "moneysystem").GetField(0));
        }

        [Test]
        public void ListsMoneyTicketsAndGroundTexturesOfTheInstallation()
        {
            var root = Directory.GetParent(mapsFolder).FullName;
            Directory.CreateDirectory(Path.Combine(root, "Money", "DM"));
            Directory.CreateDirectory(Path.Combine(root, "Money", "Euro"));
            Directory.CreateDirectory(Path.Combine(root, "TicketPacks", "Berlin_1"));
            Directory.CreateDirectory(Path.Combine(root, "Texture"));
            foreach (var file in new[] { @"Money\DM\DM.cfg", @"Money\Euro\Euro.cfg", @"Money\Euro\readme.txt", @"TicketPacks\Berlin_1\Berlin_1.otp",
                         @"Texture\gras.bmp", @"Texture\gras.dds", @"Texture\gras_det.bmp", @"Texture\feld01.bmp", @"Texture\noise_low.bmp", @"Texture\wall.bmp",
                         @"Texture\Sand.dds" })
                File.WriteAllText(Path.Combine(root, file), "x");

            CollectionAssert.AreEqual(new[] { @"Money\DM\DM.cfg", @"Money\Euro\Euro.cfg" }, OmsiNewMapContent.Files(root, "Money", ".cfg"));
            CollectionAssert.AreEqual(new[] { @"TicketPacks\Berlin_1\Berlin_1.otp" }, OmsiNewMapContent.Files(root, "TicketPacks", ".otp"));
            Assert.AreEqual(root, OmsiNewMapContent.InstallationOf(Path.Combine(templateFolder, "global.cfg")));
            // The template's ground first, then ground-like textures, each once, without detail, noise and walls.
            var template = OmsiRecordFile.Load(Path.Combine(templateFolder, "global.cfg"), OmsiGlobalConfigLayouts.Instance);
            CollectionAssert.AreEqual(new[] { @"texture\gras.bmp", @"Texture\feld01.bmp", @"Texture\Sand.dds" }, OmsiNewMapContent.GroundTextures(root, template));
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
            CollectionAssert.AreEqual(new[] { "name", "friendlyname", "description", "end", "version", "NextIDCode", "groundtex", "entrypoints", "map" }, names);
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
