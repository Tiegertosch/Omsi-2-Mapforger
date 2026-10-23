using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiContentOriginsTests
    {
        private string steamApps;
        private string installation;

        [SetUp]
        public void CreateLibrary()
        {
            steamApps = Path.Combine(Path.GetTempPath(), "mapforge-steam-" + Guid.NewGuid().ToString("N"), "steamapps");
            installation = Path.Combine(steamApps, "common", "OMSI 2");
            Directory.CreateDirectory(installation);
            Directory.CreateDirectory(Path.Combine(steamApps, "depotcache"));
        }

        [TearDown]
        public void DeleteLibrary()
        {
            var root = Directory.GetParent(steamApps).FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void SortsFilesIntoBaseGameDlcAndUserContentByDepot()
        {
            File.WriteAllText(Path.Combine(steamApps, "appmanifest_252530.acf"), string.Join("\n",
                "\"AppState\"", "{", "\t\"appid\"\t\t\"252530\"", "\t\"InstalledDepots\"", "\t{",
                "\t\t\"252531\"", "\t\t{", "\t\t\t\"manifest\"\t\t\"111\"", "\t\t\t\"size\"\t\t\"5\"", "\t\t}",
                "\t\t\"252599\"", "\t\t{", "\t\t\t\"manifest\"\t\t\"222\"", "\t\t\t\"dlcappid\"\t\t\"300000\"", "\t\t}",
                "\t}", "}"));
            File.WriteAllBytes(Path.Combine(steamApps, "depotcache", "252531_111.manifest"),
                Manifest(false, "Sceneryobjects\\Generic\\bench.sco", "Sceneryobjects\\Generic\\model\\bench.o3d"));
            File.WriteAllBytes(Path.Combine(steamApps, "depotcache", "252599_222.manifest"),
                Manifest(false, "Sceneryobjects\\Addon_Hamburg\\bus_stop.sco", "Sceneryobjects\\Generic\\addon_bin.sco"));

            var origins = OmsiContentOrigins.ForInstallation(installation, null);

            Assert.IsTrue(origins.FromSteam, origins.Description);
            Assert.AreEqual(OmsiContentOrigin.BaseGame, origins.Of(@"Sceneryobjects\Generic\bench.sco"));
            Assert.AreEqual(OmsiContentOrigin.BaseGame, origins.Of("sceneryobjects/generic/BENCH.sco"));
            // A DLC may add files to a base game folder; each file counts on its own.
            Assert.AreEqual(OmsiContentOrigin.Dlc, origins.Of(@"Sceneryobjects\Generic\addon_bin.sco"));
            Assert.AreEqual(OmsiContentOrigin.Dlc, origins.Of(@"Sceneryobjects\Addon_Hamburg\bus_stop.sco"));
            Assert.AreEqual(OmsiContentOrigin.UserContent, origins.Of(@"Sceneryobjects\Pennermasters Objekte\sign.sco"));
            StringAssert.Contains("1 DLC package", origins.Description);
        }

        [Test]
        public void FallsBackToTheBaseGameFoldersWithoutSteamLists()
        {
            var withoutManifest = OmsiContentOrigins.ForInstallation(installation, null);
            Assert.IsFalse(withoutManifest.FromSteam);
            Assert.AreEqual(OmsiContentOrigin.BaseGame, withoutManifest.Of(@"Sceneryobjects\Kreuz_MC\crossing.sco"));
            Assert.AreEqual(OmsiContentOrigin.Unknown, withoutManifest.Of(@"Sceneryobjects\Pennermasters Objekte\sign.sco"));

            // Encrypted file names cannot be read, so the base game is not known either.
            File.WriteAllText(Path.Combine(steamApps, "appmanifest_252530.acf"),
                "\"AppState\" { \"InstalledDepots\" { \"252531\" { \"manifest\" \"111\" } } }");
            File.WriteAllBytes(Path.Combine(steamApps, "depotcache", "252531_111.manifest"), Manifest(true, "abc"));
            var encrypted = OmsiContentOrigins.ForInstallation(installation, null);
            Assert.IsFalse(encrypted.FromSteam);
            Assert.AreEqual(OmsiContentOrigin.Unknown, encrypted.Of(@"Sceneryobjects\Addon\x.sco"));
        }

        [Test]
        public void CountsFoldersMarkedByHandAsDlcButNotBaseGameFiles()
        {
            File.WriteAllText(Path.Combine(steamApps, "appmanifest_252530.acf"),
                "\"AppState\" { \"InstalledDepots\" { \"252531\" { \"manifest\" \"111\" } } }");
            File.WriteAllBytes(Path.Combine(steamApps, "depotcache", "252531_111.manifest"),
                Manifest(false, "Sceneryobjects\\Generic\\bench.sco"));
            var origins = OmsiContentOrigins.ForInstallation(installation, null);
            Assert.AreEqual(OmsiContentOrigin.UserContent, origins.Of(@"Sceneryobjects\Bought Addon\kiosk.sco"));

            origins.SetMarkedFolders(new[] { @"Sceneryobjects\Bought Addon", "sceneryobjects/generic/" });

            Assert.IsTrue(origins.HasMarkedFolders);
            Assert.AreEqual(OmsiContentOrigin.Dlc, origins.Of(@"Sceneryobjects\Bought Addon\kiosk.sco"));
            Assert.AreEqual(OmsiContentOrigin.Dlc, origins.Of(@"Sceneryobjects\Bought Addon\deep\lamp.sco"));
            Assert.IsTrue(origins.IsMarkedByHand(@"sceneryobjects\bought addon\kiosk.sco"));
            // A folder whose name only begins like a marked one is not marked.
            Assert.AreEqual(OmsiContentOrigin.UserContent, origins.Of(@"Sceneryobjects\Bought Addon 2\kiosk.sco"));
            // What the base game ships stays base game, even inside a marked folder.
            Assert.AreEqual(OmsiContentOrigin.BaseGame, origins.Of(@"Sceneryobjects\Generic\bench.sco"));

            // Without Steam's lists the marks still tell DLC apart.
            var foldersOnly = OmsiContentOrigins.FoldersOnly("test");
            foldersOnly.SetMarkedFolders(new[] { @"Sceneryobjects\Bought Addon" });
            Assert.AreEqual(OmsiContentOrigin.Dlc, foldersOnly.Of(@"Sceneryobjects\Bought Addon\kiosk.sco"));
            Assert.AreEqual(OmsiContentOrigin.Unknown, foldersOnly.Of(@"Sceneryobjects\Other\kiosk.sco"));

            origins.SetMarkedFolders(null);
            Assert.IsFalse(origins.HasMarkedFolders);
            Assert.AreEqual(OmsiContentOrigin.UserContent, origins.Of(@"Sceneryobjects\Bought Addon\kiosk.sco"));
        }

        [Test]
        public void ReadsDepotsAndMarksDlcInAppManifests()
        {
            var depots = SteamAppManifest.InstalledDepots(
                "// comment\n\"AppState\" { \"InstalledDepots\" { \"1\" { \"manifest\" \"10\" } \"2\" { \"manifest\" \"20\" \"DLCAppID\" \"9\" } } }");

            Assert.AreEqual(2, depots.Count);
            Assert.AreEqual("1", depots[0].DepotId);
            Assert.AreEqual("10", depots[0].ManifestId);
            Assert.IsFalse(depots[0].IsDlc);
            Assert.IsTrue(depots[1].IsDlc);
            Assert.Throws<FormatException>(() => SteamAppManifest.InstalledDepots("\"AppState\" {"));
        }

        /// <summary>A depot manifest as Steam writes it: payload with one mapping per file name, metadata, end.</summary>
        private static byte[] Manifest(bool namesEncrypted, params string[] files)
        {
            var payload = new List<byte>();
            foreach (var file in files)
            {
                var mapping = new List<byte>();
                Bytes(mapping, 1, Encoding.UTF8.GetBytes(file));
                Varint(mapping, 2 << 3);
                Varint(mapping, 1234);
                Bytes(payload, 1, mapping.ToArray());
            }
            var metadata = new List<byte>();
            Varint(metadata, 1 << 3);
            Varint(metadata, 252531);
            Varint(metadata, 4 << 3);
            Varint(metadata, namesEncrypted ? 1UL : 0UL);

            var result = new List<byte>();
            Section(result, 0x71F617D0, payload);
            Section(result, 0x1F4812BE, metadata);
            Section(result, 0x32C415AB, new List<byte>());
            return result.ToArray();
        }

        private static void Section(List<byte> target, uint magic, List<byte> body)
        {
            target.AddRange(BitConverter.GetBytes(magic));
            target.AddRange(BitConverter.GetBytes(body.Count));
            target.AddRange(body);
        }

        private static void Bytes(List<byte> target, int field, byte[] value)
        {
            Varint(target, (ulong)(field << 3 | 2));
            Varint(target, (ulong)value.Length);
            target.AddRange(value);
        }

        private static void Varint(List<byte> target, ulong value)
        {
            while (value >= 0x80)
            {
                target.Add((byte)(value | 0x80));
                value >>= 7;
            }
            target.Add((byte)value);
        }
    }
}
