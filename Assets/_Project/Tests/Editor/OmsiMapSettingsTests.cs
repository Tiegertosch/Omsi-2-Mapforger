using System;
using System.IO;
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
            // The daily curve of cars is doubled, in every point.
            StringAssert.Contains("[trafficdensity_road]\r\n0\r\n1.000\r\n", text);
            StringAssert.Contains("[trafficdensity_road]\r\n12\r\n2.000\r\n", text);
            Assert.AreEqual("New name", summary.FriendlyName);
            Assert.AreEqual("New text", summary.Description);
            CollectionAssert.AreEqual(originalConfig, File.ReadAllBytes(Path.Combine(original, "global.cfg")), "The original map is not changed.");

            // What the file says now, written again, changes nothing.
            var again = CurrentOptions(summary);
            Assert.IsFalse(session.UpdateMapSettings(summary, "New name", again));
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
    }
}
