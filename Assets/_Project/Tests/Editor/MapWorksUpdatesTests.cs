using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class MapWorksUpdatesTests
    {
        private const string ReleaseJson = @"{
  ""tag_name"": ""v1.2"",
  ""name"": ""MapWorks 1.2"",
  ""body"": ""- Report a bug\r\n- Updates"",
  ""html_url"": ""https://github.com/Tiegertosch/Omsi-2-Mapforger/releases/tag/v1.2"",
  ""draft"": false,
  ""prerelease"": false,
  ""published_at"": ""2026-11-02T10:30:00Z"",
  ""assets"": [
    { ""name"": ""Source.txt"", ""browser_download_url"": ""https://example.org/Source.txt"", ""size"": 3 },
    { ""name"": ""OMSI-MapWorks-1.2.0-win64.zip"", ""browser_download_url"": ""https://github.com/Tiegertosch/Omsi-2-Mapforger/releases/download/v1.2/OMSI-MapWorks-1.2.0-win64.zip"",
      ""size"": 123456, ""digest"": ""sha256:ABCDEF0123"" }
  ]
}";

        private string folder;

        [SetUp]
        public void CreateFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "MapWorksUpdatesTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        [Test]
        public void VersionsReadWithAndWithoutV()
        {
            Assert.IsTrue(MapWorksUpdates.TryParseVersion("v1.2", out var tag));
            Assert.AreEqual("1.2.0", MapWorksUpdates.Format(tag));
            Assert.IsTrue(MapWorksUpdates.TryParseVersion("1.0", out var application));
            Assert.AreEqual(new Version(1, 0, 0, 0), application);
            Assert.IsTrue(MapWorksUpdates.TryParseVersion("2.0.1-beta", out var beta));
            Assert.AreEqual("2.0.1", MapWorksUpdates.Format(beta));
            Assert.AreEqual("1.2.3.4", MapWorksUpdates.Format(new Version(1, 2, 3, 4)));
            Assert.IsFalse(MapWorksUpdates.TryParseVersion("latest", out _));
            Assert.IsFalse(MapWorksUpdates.TryParseVersion("", out _));
        }

        [Test]
        public void OnlyAHigherVersionIsNewer()
        {
            Assert.IsTrue(MapWorksUpdates.IsNewer(new MapWorksRelease { Tag = "v1.0.1" }, "1.0"));
            Assert.IsFalse(MapWorksUpdates.IsNewer(new MapWorksRelease { Tag = "v1.0.0" }, "1.0"));
            Assert.IsFalse(MapWorksUpdates.IsNewer(new MapWorksRelease { Tag = "v0.9" }, "1.0"));
            Assert.IsTrue(MapWorksUpdates.IsNewer(new MapWorksRelease { Tag = "v1.10" }, "1.9"));
            Assert.IsFalse(MapWorksUpdates.IsNewer(new MapWorksRelease { Tag = "nightly" }, "1.0"));
            Assert.IsFalse(MapWorksUpdates.IsNewer(null, "1.0"));
        }

        [Test]
        public void ReleaseTakesTheWindowsZip()
        {
            var release = MapWorksUpdates.ParseRelease(ReleaseJson);

            Assert.AreEqual("v1.2", release.Tag);
            Assert.AreEqual("1.2.0", release.Version);
            Assert.AreEqual("MapWorks 1.2", release.Name);
            Assert.AreEqual("- Report a bug\r\n- Updates", release.Notes);
            Assert.AreEqual(new DateTime(2026, 11, 2, 10, 30, 0, DateTimeKind.Utc), release.Published);
            Assert.AreEqual("OMSI-MapWorks-1.2.0-win64.zip", release.AssetName);
            StringAssert.EndsWith("/v1.2/OMSI-MapWorks-1.2.0-win64.zip", release.AssetUrl);
            Assert.AreEqual(123456, release.AssetSize);
            Assert.AreEqual("abcdef0123", release.AssetSha256);
        }

        [Test]
        public void ReleaseWithoutZipHasNoDownload()
        {
            var release = MapWorksUpdates.ParseRelease(@"{ ""tag_name"": ""v2.0"", ""assets"": [] }");

            Assert.IsNull(release.AssetUrl);
            Assert.AreEqual("v2.0", release.Name);
            Assert.Throws<InvalidDataException>(() => MapWorksUpdates.ParseRelease(@"{ ""message"": ""Not Found"" }"));
        }

        [Test]
        public void UnpackFindsTheProgramInItsFolder()
        {
            var zip = Path.Combine(folder, "update.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                Entry(archive, "OMSI MapWorks/OMSIMapWorks.exe", "exe");
                Entry(archive, "OMSI MapWorks/UnityPlayer.dll", "dll");
                Entry(archive, "OMSI MapWorks/OMSIMapWorks_Data/globalgamemanagers", "data");
            }

            var program = MapWorksUpdates.Unpack(zip, Path.Combine(folder, "staging"), "OMSIMapWorks.exe");

            Assert.AreEqual(Path.Combine(folder, "staging", "OMSI MapWorks"), program);
            Assert.AreEqual("data", File.ReadAllText(Path.Combine(program, "OMSIMapWorks_Data", "globalgamemanagers")));
        }

        [Test]
        public void UnpackRefusesFilesOutsideItsFolder()
        {
            var zip = Path.Combine(folder, "evil.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                Entry(archive, "../evil.txt", "evil");

            Assert.Throws<InvalidDataException>(() => MapWorksUpdates.Unpack(zip, Path.Combine(folder, "staging"), "OMSIMapWorks.exe"));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "evil.txt")));
        }

        [Test]
        public void UnpackRefusesAZipWithoutMapWorks()
        {
            var zip = Path.Combine(folder, "other.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                Entry(archive, "readme.txt", "hello");

            Assert.Throws<InvalidDataException>(() => MapWorksUpdates.Unpack(zip, Path.Combine(folder, "staging"), "OMSIMapWorks.exe"));
        }

        [Test]
        public void UnpackSaysWhenTheFolderIsTooDeep()
        {
            var zip = Path.Combine(folder, "deep.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                Entry(archive, "OMSI MapWorks/OMSIMapWorks_Data/Managed/" + new string('x', 240) + ".dll", "dll");

            var exception = Assert.Throws<PathTooLongException>(() => MapWorksUpdates.Unpack(zip, Path.Combine(folder, "staging"), "OMSIMapWorks.exe"));
            StringAssert.Contains("Move MapWorks to a shorter folder", exception.Message);
        }

        [Test]
        public void CommandLineArgumentsAreQuotedForWindows()
        {
            Assert.AreEqual("-updatedFrom", MapWorksUpdates.CommandLineArgument("-updatedFrom"));
            Assert.AreEqual("\"C:\\My Maps\\a.omproj\"", MapWorksUpdates.CommandLineArgument("C:\\My Maps\\a.omproj"));
            Assert.AreEqual("\"C:\\My Maps\\\\\"", MapWorksUpdates.CommandLineArgument("C:\\My Maps\\"));
            Assert.AreEqual("\"say \\\"hi\\\"\"", MapWorksUpdates.CommandLineArgument("say \"hi\""));
            Assert.AreEqual("\"\"", MapWorksUpdates.CommandLineArgument(""));
        }

        [Test]
        public void InstallScriptQuotesItsPaths()
        {
            var script = MapWorksUpdates.InstallScript(4242, "D:\\Tom's Tools\\MapWorks", "D:\\Tom's Tools\\MapWorks\\.mwu-1.2.0\\OMSI MapWorks",
                "D:\\Tom's Tools\\MapWorks\\.mwu-1.2.0", "OMSIMapWorks.exe", "C:\\Logs\\update.log", "1.0.0", "1.2.0",
                new[] { "-updatedFrom", "1.0.0", "-openProject", "C:\\My Maps\\a.omproj" });

            StringAssert.Contains("$program = 'D:\\Tom''s Tools\\MapWorks'\r\n", script);
            StringAssert.Contains("$arguments = '-updatedFrom 1.0.0 -openProject \"C:\\My Maps\\a.omproj\"'\r\n", script);
            StringAssert.Contains("Wait-Process -Id 4242 ", script);
            StringAssert.Contains("$old = '.mapworks-old'", script);
            StringAssert.Contains("$version = '1.2.0'", script);
            StringAssert.Contains("Set-ItemProperty -LiteralPath $key -Name DisplayVersion -Value $version", script);
        }

        [Test]
        public void NotesBecomePlainText()
        {
            var notes = MapWorksUpdates.NotesAsText("## What's new\r\n- **Report a bug** in the `Help` menu\r\n  * nested\r\n\r\nThanks!\r\n");

            Assert.AreEqual("What's new\n• Report a bug in the Help menu\n  • nested\n\nThanks!", notes);
            Assert.AreEqual(string.Empty, MapWorksUpdates.NotesAsText(null));
        }

        [Test]
        public void UpdatesComeOnlyOverHttps()
        {
            Assert.DoesNotThrow(() => MapWorksUpdates.CheckAddress(MapWorksUpdates.LatestReleaseUrl));
            Assert.DoesNotThrow(() => MapWorksUpdates.CheckAddress("http://127.0.0.1:8765/latest.json"));
            Assert.Throws<InvalidOperationException>(() => MapWorksUpdates.CheckAddress("http://example.org/latest.json"));
            Assert.Throws<InvalidOperationException>(() => MapWorksUpdates.CheckAddress("file:///C:/latest.json"));
        }

        private static void Entry(ZipArchive archive, string name, string text)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
                writer.Write(text);
        }
    }
}
