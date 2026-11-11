using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The vehicle groups of ailists.cfg: depots with type groups and buses, plain pools, edited line by line (ADR 095).</summary>
    public sealed class OmsiAiListFileTests
    {
        private const string Grundorf =
            "[aigroup_2]\r\nNormalCars\r\nvehicles\\VW_Golf_2\\AI_VW_Golf_2.bus\t70\r\nvehicles\\Citr_BX\\BX.ovh\t40\r\n[end]\r\n\r\n" +
            "[aigroup_depot]\r\nBusses\r\nGrundorf\r\n\r\n\r\n\r\n" +
            "[aigroup_depot_typgroup_2]\r\nVehicles\\MAN_NL_NG\\MAN_GN92_main.bus\r\n2530\tGRN-V 30\r\n2535\tGRN-V 35\r\n[end]\r\n\r\n" +
            "[aigroup_2]\r\nRegionalbahn\r\n\r\ntrains\\regio_grundorf.zug\r\n[end]\r\n\r\n";

        // Windows-1252 text, empty fields kept, as Berlin-Spandau has them.
        private static byte[] Spandau()
        {
            var text = "[aigroup_depot]\r\nHof Spandau\r\nSpandau 1986\r\n\r\n" +
                       "[aigroup_depot_typgroup_2]\r\nvehicles\\MAN_SD200\\MAN_SD77.bus\r\n2701\t\tBFB\t\t19870430\r\n2702\t\tMöbel\t\t\r\n[end]\r\n\r\n" +
                       "[aigroup_depot_typgroup_2]\r\nvehicles\\MAN_SD200\\MAN_SD80.bus\r\n1711\t\tHolz\t19860731\t\r\n[end]\r\n\r\n";
            return Latin1(text);
        }

        // Windows-1252 and Latin-1 agree for the characters the samples use.
        private static byte[] Latin1(string text)
        {
            return text.Select(character => (byte)character).ToArray();
        }

        private static string FromLatin1(byte[] bytes)
        {
            return new string(bytes.Select(value => (char)value).ToArray());
        }

        private static OmsiAiListFile Parse(string text)
        {
            return OmsiAiListFile.Parse(Encoding.UTF8.GetBytes(text));
        }

        private static string Text(OmsiAiListFile file)
        {
            return Encoding.UTF8.GetString(file.ToBytes());
        }

        [Test]
        public void ReadsDepotsAndPoolsAndWritesThemBackUnchanged()
        {
            var file = Parse(Grundorf);
            Assert.AreEqual(Grundorf, Text(file));
            var groups = file.Groups;
            CollectionAssert.AreEqual(new[] { "NormalCars", "Busses", "Regionalbahn" }, groups.Select(group => group.Name).ToArray());
            Assert.IsFalse(groups[0].IsDepot);
            CollectionAssert.AreEqual(new[] { "70", "40" }, groups[0].PoolVehicles.Select(vehicle => vehicle.Weight).ToArray());
            Assert.IsTrue(groups[1].IsDepot);
            Assert.AreEqual("Grundorf", groups[1].Hof);
            var type = groups[1].TypeGroups.Single();
            Assert.AreEqual(@"Vehicles\MAN_NL_NG\MAN_GN92_main.bus", type.File);
            CollectionAssert.AreEqual(new[] { "2530", "2535" }, type.Vehicles.Select(vehicle => vehicle.Number).ToArray());
            Assert.AreEqual("GRN-V 35", type.Vehicles[1].Registration);
            CollectionAssert.AreEqual(new[] { @"trains\regio_grundorf.zug" }, groups[2].PoolVehicles.Select(vehicle => vehicle.File).ToArray());
        }

        [Test]
        public void ASpandauFileKeepsItsEmptyFieldsAndItsEncoding()
        {
            var bytes = Spandau();
            var file = OmsiAiListFile.Parse(bytes);
            CollectionAssert.AreEqual(bytes, file.ToBytes());
            var vehicles = file.Groups[0].TypeGroups[0].Vehicles;
            Assert.AreEqual("Möbel", vehicles[1].Paint);
            Assert.AreEqual("19870430", vehicles[0].To);
            // One bus changes: only its line differs, the others keep their double tabs.
            file.SetVehicle(0, 1, 0, new OmsiDepotVehicle { Number = "1711", Registration = string.Empty, Paint = "Holz", From = "19860731", To = "19861231" });
            var changed = FromLatin1(file.ToBytes());
            var original = FromLatin1(bytes);
            var before = original.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var after = changed.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.AreEqual(before.Length, after.Length);
            var differing = Enumerable.Range(0, before.Length).Where(index => before[index] != after[index]).ToList();
            Assert.AreEqual(1, differing.Count);
            Assert.AreEqual("1711\t\tHolz\t19860731\t19861231", after[differing[0]]);
        }

        [Test]
        public void AnEditChangesExactlyOneLine()
        {
            var file = Parse(Grundorf);
            file.SetVehicle(1, 0, 1, new OmsiDepotVehicle { Number = "2536", Registration = "GRN-V 36" });
            var before = Grundorf.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var after = Text(file).Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.AreEqual(before.Length, after.Length);
            var differing = Enumerable.Range(0, before.Length).Where(index => before[index] != after[index]).ToList();
            Assert.AreEqual(1, differing.Count);
            Assert.AreEqual("2536\tGRN-V 36", after[differing[0]]);
        }

        [Test]
        public void ANewTypeGroupAndANewBusLandInsideTheirDepot()
        {
            var file = Parse(Grundorf);
            file.AddTypeGroup(1, @"Vehicles\MAN_SD200\MAN_SD77.bus");
            file.AddVehicle(1, 1, new OmsiDepotVehicle { Number = "2701", Registration = "B-V 1", From = "19870101" });
            file.AddVehicle(1, 0, new OmsiDepotVehicle { Number = "2540", Registration = "GRN-V 40" });
            var text = Text(file);
            Assert.Less(text.IndexOf("2540\tGRN-V 40", StringComparison.Ordinal), text.IndexOf("[aigroup_depot_typgroup_2]\r\nVehicles\\MAN_SD200", StringComparison.Ordinal));
            Assert.Less(text.IndexOf("2701\tB-V 1\t\t19870101", StringComparison.Ordinal), text.IndexOf("Regionalbahn", StringComparison.Ordinal));
            var depot = file.Groups[1];
            Assert.AreEqual(2, depot.TypeGroups.Count);
            CollectionAssert.AreEqual(new[] { "2530", "2535", "2540" }, depot.TypeGroups[0].Vehicles.Select(vehicle => vehicle.Number).ToArray());
            Assert.AreEqual("Regionalbahn", file.Groups[2].Name);
            file.RemoveTypeGroup(1, 1);
            file.RemoveVehicle(1, 0, 2);
            Assert.AreEqual(Grundorf, Text(file));
        }

        [Test]
        public void ANewDepotAndANewPoolAreAddedAtTheEnd()
        {
            var file = Parse("[aigroup_2]\r\nCars\r\nfile.bus\t1\r\n[end]");
            file.AddDepot("Busses", "Hof A");
            file.AddTypeGroup(1, @"vehicles\X\x.bus");
            file.AddVehicle(1, 0, new OmsiDepotVehicle { Number = "1", Registration = "A-1" });
            file.AddPool("Trucks");
            file.AddPoolVehicle(2, @"vehicles\T\t.bus", "5");
            var again = OmsiAiListFile.Parse(file.ToBytes()).Groups;
            CollectionAssert.AreEqual(new[] { "Cars", "Busses", "Trucks" }, again.Select(group => group.Name).ToArray());
            Assert.AreEqual("Hof A", again[1].Hof);
            Assert.AreEqual("A-1", again[1].TypeGroups.Single().Vehicles.Single().Registration);
            Assert.AreEqual("5", again[2].PoolVehicles.Single().Weight);
            Assert.AreEqual(1, again[0].PoolVehicles.Count);
            // A file that was not there starts from nothing.
            var fresh = OmsiAiListFile.Parse(null);
            fresh.AddDepot("Busses", "Hof A");
            Assert.AreEqual("Busses", OmsiAiListFile.Parse(fresh.ToBytes()).Groups.Single().Name);
        }

        [Test]
        public void ARemovedGroupTakesItsTypeGroupsAndNothingElse()
        {
            var file = Parse(Grundorf);
            file.AddTypeGroup(1, @"vehicles\X\x.bus");
            file.RemoveGroup(1);
            Assert.AreEqual(
                "[aigroup_2]\r\nNormalCars\r\nvehicles\\VW_Golf_2\\AI_VW_Golf_2.bus\t70\r\nvehicles\\Citr_BX\\BX.ovh\t40\r\n[end]\r\n\r\n" +
                "[aigroup_2]\r\nRegionalbahn\r\n\r\ntrains\\regio_grundorf.zug\r\n[end]\r\n\r\n", Text(file));
            file.RemoveGroup(0);
            Assert.AreEqual("Regionalbahn", file.Groups.Single().Name);
        }

        [Test]
        public void RefusesNamesTwiceAndTextTheFormatCannotHold()
        {
            var file = Parse(Grundorf);
            Assert.Throws<InvalidOperationException>(() => file.AddPool("busses"));
            Assert.Throws<InvalidOperationException>(() => file.SetName(0, "Regionalbahn"));
            Assert.Throws<ArgumentException>(() => file.AddPool("a\tb"));
            Assert.Throws<ArgumentException>(() => file.AddPool(" "));
            Assert.Throws<ArgumentException>(() => file.AddVehicle(1, 0, new OmsiDepotVehicle { Number = "1", From = "1987" }));
            Assert.Throws<ArgumentException>(() => file.AddPoolVehicle(0, "a.bus", "heavy"));
            // A file in Windows-1252 cannot take a character outside it.
            var spandau = OmsiAiListFile.Parse(Spandau());
            spandau.SetName(0, "Hof 中");
            Assert.Throws<ArgumentException>(() => spandau.ToBytes());
        }

        [Test]
        public void ARenamedGroupChangesTheToursThatNameItAndADeleteIsRefusedWhileTheyDo()
        {
            var root = Path.Combine(Path.GetTempPath(), "mapforge-ailists-" + Guid.NewGuid().ToString("N"));
            try
            {
                var map = Path.Combine(root, "maps", "Listtown");
                Directory.CreateDirectory(Path.Combine(map, "TTData"));
                var global = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[NextIDCode]\r\n100\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n\r\n")).ToArray();
                File.WriteAllBytes(Path.Combine(map, "global.cfg"), global);
                File.WriteAllBytes(Path.Combine(map, "tile_0_0.map"), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[version]\r\n14\r\n")).ToArray());
                File.WriteAllBytes(Path.Combine(map, "ailists.cfg"), Encoding.UTF8.GetBytes(Grundorf));
                File.WriteAllBytes(Path.Combine(map, "TTData", "1.ttl"), OmsiTimetableWriter.CreateLine("Busses"));
                var session = OmsiMapSession.OpenProject(Path.Combine(map, "global.cfg"), Path.Combine(root, "Projects", "Lists.omproj"));
                session.Read();
                var users = session.AiGroupUsers("busses");
                Assert.AreEqual(1, users.Count);
                var refused = Assert.Throws<InvalidOperationException>(() => session.RemoveAiGroup(1));
                StringAssert.Contains(users[0], refused.Message);

                var change = session.RenameAiGroup(1, "City buses");
                Assert.IsNotNull(change);
                Assert.AreEqual("City buses", session.ReadAiLists().Groups[1].Name);
                Assert.AreEqual("City buses", session.ReadLineFile("1").Tours[0].VehicleGroup);
                CollectionAssert.AreEqual(new[] { "NormalCars", "City buses", "Regionalbahn" }, session.ReadAiGroups().ToArray());

                session.EditAiLists(lists => lists.SetVehicle(1, 0, 0, new OmsiDepotVehicle { Number = "2531", Registration = "GRN-V 31" }));
                Assert.AreEqual("2531", session.ReadAiLists().Groups[1].TypeGroups[0].Vehicles[0].Number);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
