using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiMapSettingsTests
    {
        private string testRoot;
        private string original;
        private OmsiMapSession session;
        private byte[] originalConfig;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-settings-" + Guid.NewGuid().ToString("N"));
            original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            originalConfig = OmsiTextFile.Encode(
                "[name]\r\nTestmap\r\n\r\n[friendlyname]\r\nOld name\r\n\r\n[description]\r\nOld text\r\n[end]\r\n\r\n[NextIDCode]\r\n20\r\n\r\n" +
                "[years]\r\n2000\r\n2030\r\n\r\n[trafficdensity_road]\r\n0\r\n0.5\r\n\r\n[trafficdensity_road]\r\n12\r\n1\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n",
                OmsiTextEncoding.Utf16LittleEndian);
            File.WriteAllBytes(Path.Combine(original, "global.cfg"), originalConfig);
            File.WriteAllBytes(Path.Combine(original, "tile_0_0.map"), OmsiTextFile.Encode("[version]\r\n14\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        private OmsiNewMapOptions CurrentOptions(OmsiMapSummary summary)
        {
            var options = OmsiNewMapOptions.FromTemplate(OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("global.cfg"), OmsiGlobalConfigLayouts.Instance));
            options.Description = (summary.Description ?? string.Empty).Trim();
            return options;
        }

        [Test]
        public void ChangingTheSettingsWritesOnlyWhatDiffersAndLeavesTheOriginalMap()
        {
            var summary = session.Read();
            Assert.AreEqual("Old name", summary.FriendlyName);
            var options = CurrentOptions(summary);
            options.Description = "New text";
            options.LeftHandTraffic = true;
            options.FirstYear = 1990;
            options.CarTraffic = 2.0;

            Assert.IsTrue(session.UpdateMapSettings(summary, "New name", options));

            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes("global.cfg"));
            StringAssert.Contains("[name]\r\nTestmap\r\n", text);
            StringAssert.Contains("[friendlyname]\r\nNew name\r\n", text);
            StringAssert.Contains("New text", text);
            StringAssert.DoesNotContain("Old text", text);
            StringAssert.Contains("[LHT]", text);
            StringAssert.Contains("[years]\r\n1990\r\n2030\r\n", text);
            StringAssert.Contains("[NextIDCode]\r\n20\r\n", text);
            // The cars are at 2.00 times the standard day now; the curve keeps its own shape, its second point twice its first.
            var road = Curve(session.Workspace.ReadAllBytes("global.cfg"), "trafficdensity_road");
            Assert.AreEqual(2, road.Length);
            Assert.AreEqual(2.0, road[1] / road[0], 0.01);
            Assert.AreEqual(2.0, CurrentOptions(summary).CarTraffic);
            Assert.AreEqual("New name", summary.FriendlyName);
            Assert.AreEqual("New text", summary.Description);
            CollectionAssert.AreEqual(originalConfig, File.ReadAllBytes(Path.Combine(original, "global.cfg")), "The original map is not changed.");

            // What the file says now, written again, changes nothing.
            var again = CurrentOptions(summary);
            Assert.IsFalse(session.UpdateMapSettings(summary, "New name", again));
        }

        [Test]
        public void ChangingTheSeasonEntriesWritesThemIntoTheProjectCopyOnly()
        {
            var summary = session.Read();
            var options = CurrentOptions(summary);
            Assert.AreEqual(0, options.SeasonEntries.Count);
            options.SeasonEntries.Add(new OmsiSeasonEntry(OmsiSeasonKind.SnowyWinter, 0, 38));
            options.SeasonEntries.Add(new OmsiSeasonEntry(OmsiSeasonKind.Spring, 76, 134));

            Assert.IsTrue(session.UpdateMapSettings(summary, "Old name", options));

            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes("global.cfg"));
            StringAssert.Contains("Winter (snow):", text);
            StringAssert.Contains("[addseason]\r\n4\r\n0\r\n38\r\n", text);
            StringAssert.Contains("[addseason]\r\n1\r\n76\r\n134\r\n", text);
            CollectionAssert.AreEqual(originalConfig, File.ReadAllBytes(Path.Combine(original, "global.cfg")), "The original map is not changed.");
            Assert.IsFalse(session.UpdateMapSettings(summary, "Old name", CurrentOptions(summary)), "The same seasons again change nothing.");

            options = CurrentOptions(summary);
            options.SeasonEntries[1] = new OmsiSeasonEntry(OmsiSeasonKind.Spring, 90, 40);
            Assert.Throws<ArgumentException>(() => session.UpdateMapSettings(summary, "Old name", options));
        }

        [Test]
        public void OptionsReadFromTheProjectCopyKeepTheSavedValuesAndApplyingThemAgainChangesNothing()
        {
            var summary = session.Read();
            var options = CurrentOptions(summary);
            options.SeasonEntries.Add(new OmsiSeasonEntry(OmsiSeasonKind.SnowyWinter, 0, 38));
            options.SeasonEntries.Add(new OmsiSeasonEntry(OmsiSeasonKind.Spring, 76, 134));
            Assert.IsTrue(session.UpdateMapSettings(summary, "Old name", options));

            options = CurrentOptions(summary);
            options.SeasonEntries.RemoveAt(1);
            options.FirstYear = 1995;
            Assert.IsTrue(session.UpdateMapSettings(summary, "Old name", options));
            var savedCopy = session.Workspace.ReadAllBytes("global.cfg");

            // What the settings dialog does: read the project copy, not the original map.
            var reopened = OmsiNewMapOptions.FromTemplate(OmsiRecordFile.Parse(session.Workspace.ReadAllBytes("global.cfg"), OmsiGlobalConfigLayouts.Instance));
            reopened.Description = (summary.Description ?? string.Empty).Trim();
            Assert.AreEqual(1, reopened.SeasonEntries.Count);
            Assert.AreEqual(OmsiSeasonKind.SnowyWinter, reopened.SeasonEntries[0].Kind);
            Assert.AreEqual(1995, reopened.FirstYear);

            Assert.IsFalse(session.UpdateMapSettings(summary, "Old name", reopened));
            CollectionAssert.AreEqual(savedCopy, session.Workspace.ReadAllBytes("global.cfg"), "The project copy is unchanged.");
            CollectionAssert.AreEqual(originalConfig, File.ReadAllBytes(Path.Combine(original, "global.cfg")), "The original map is not changed.");
        }

        [Test]
        public void ANameIsRequiredAndSectionNamesAreRefused()
        {
            var summary = session.Read();
            var options = CurrentOptions(summary);
            Assert.Throws<ArgumentException>(() => session.UpdateMapSettings(summary, "  ", options));
            Assert.Throws<ArgumentException>(() => session.UpdateMapSettings(summary, "A [map] name", options));
            options.FirstYear = 2040;
            Assert.Throws<ArgumentException>(() => session.UpdateMapSettings(summary, "Fine", options), "The years must run forward.");
            Assert.AreEqual("Old name", summary.FriendlyName);
        }

        private static double[] Curve(byte[] config, string name)
        {
            var file = OmsiRecordFile.Parse(config, OmsiGlobalConfigLayouts.Instance);
            return file.Records.Where(record => record.Name == name).Select(record => record.TryGetDouble(1, out var value) ? value : double.NaN).ToArray();
        }

        private static OmsiRecordFile Config(string curves)
        {
            return OmsiRecordFile.Parse(OmsiTextFile.Encode("[name]\r\nTestmap\r\n\r\n[NextIDCode]\r\n20\r\n\r\n" + curves, OmsiTextEncoding.Utf16LittleEndian),
                OmsiGlobalConfigLayouts.Instance);
        }

        private static readonly double[] StandardHours = { 0, 4, 6, 7, 8, 10, 15, 16, 17, 20, 24 };
        private static readonly double[] StandardCars = { 0.1, 0, 1, 1.5, 1, 0.5, 0.6, 1, 1.5, 0.4, 0.1 };

        private static string CarCurve(Func<int, double> density)
        {
            var text = string.Empty;
            for (var point = 0; point < StandardHours.Length; point++)
                text += "[trafficdensity_road]\r\n" + StandardHours[point].ToString(CultureInfo.InvariantCulture) + "\r\n" +
                        density(point).ToString("0.000", CultureInfo.InvariantCulture) + "\r\n\r\n";
            return text;
        }

        [Test]
        public void TrafficFactorsAreReadOnTheStandardDay()
        {
            Assert.AreEqual(1.0, OmsiNewMap.CarTrafficFactor(Config(CarCurve(point => StandardCars[point]))));
            Assert.AreEqual(1.5, OmsiNewMap.CarTrafficFactor(Config(CarCurve(point => StandardCars[point] * 1.5))));
            Assert.AreEqual(1.0, OmsiNewMap.CarTrafficFactor(Config(string.Empty)), "Without a curve OMSI has the standard day.");
            Assert.AreEqual(1.0, OmsiNewMap.PassengerTrafficFactor(Config(CarCurve(point => StandardCars[point] * 1.5))), "Each curve on its own.");
            Assert.AreEqual(0.0, OmsiNewMap.CarTrafficFactor(Config(CarCurve(point => 0.0))));
            Assert.AreEqual(2.0, OmsiNewMap.CarTrafficFactor(Config("[trafficdensity_road]\r\n12\r\n5\r\n")), "Kept within the slider's range.");
        }

        [Test]
        public void ATrafficFactorIsSetOnTheStandardDayAndDoesNotMultiplyUp()
        {
            var config = Config(CarCurve(point => StandardCars[point]));
            var options = OmsiNewMapOptions.FromTemplate(config);
            Assert.AreEqual(1.0, options.CarTraffic);
            Assert.AreEqual(1.0, options.PassengerTraffic);
            options.CarTraffic = 1.5;
            OmsiNewMap.EditGlobalConfig(config, "Test", null, options);
            var read = OmsiNewMapOptions.FromTemplate(config);
            Assert.AreEqual(1.5, read.CarTraffic);
            Assert.AreEqual(1.0, read.PassengerTraffic);
            Assert.IsFalse(config.Records.Any(record => record.Name == "trafficdensity_passenger"), "No passenger curve is written at the standard factor.");

            var written = config.ToBytes();
            OmsiNewMap.EditGlobalConfig(config, "Test", null, read);
            CollectionAssert.AreEqual(written, config.ToBytes(), "The factor read back changes nothing.");

            read.CarTraffic = 1.0;
            OmsiNewMap.EditGlobalConfig(config, "Test", null, read);
            var curve = Curve(config.ToBytes(), "trafficdensity_road");
            for (var point = 0; point < StandardCars.Length; point++)
                Assert.AreEqual(StandardCars[point], curve[point], 0.002, "Back at 1: the standard day, not 1.5 times it.");
            Assert.AreEqual(1.0, OmsiNewMapOptions.FromTemplate(config).CarTraffic);
        }

        [Test]
        public void AnOwnCurveKeepsItsShapeAndAZeroCurveGetsTheStandardDay()
        {
            var config = Config("[trafficdensity_road]\r\n0\r\n0.2\r\n\r\n[trafficdensity_road]\r\n9\r\n2\r\n\r\n[trafficdensity_road]\r\n18\r\n0.8\r\n\r\n");
            var options = OmsiNewMapOptions.FromTemplate(config);
            var before = Curve(config.ToBytes(), "trafficdensity_road");
            options.CarTraffic = options.CarTraffic > 1.0 ? 0.5 : 1.8;
            var wanted = options.CarTraffic;
            OmsiNewMap.EditGlobalConfig(config, "Test", null, options);
            var after = Curve(config.ToBytes(), "trafficdensity_road");
            Assert.AreEqual(3, after.Length);
            Assert.AreEqual(before[1] / before[0], after[1] / after[0], 0.05);
            Assert.AreEqual(before[2] / before[0], after[2] / after[0], 0.05);
            Assert.AreEqual(wanted, OmsiNewMapOptions.FromTemplate(config).CarTraffic, 0.011);

            var zero = Config(CarCurve(point => 0.0));
            var zeroOptions = OmsiNewMapOptions.FromTemplate(zero);
            zeroOptions.CarTraffic = 0.8;
            OmsiNewMap.EditGlobalConfig(zero, "Test", null, zeroOptions);
            Assert.AreEqual(0.8, OmsiNewMapOptions.FromTemplate(zero).CarTraffic);
            Assert.AreEqual(StandardCars.Length, Curve(zero.ToBytes(), "trafficdensity_road").Length);
        }
    }
}
