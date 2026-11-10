using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiContentFoldersTests
    {
        private string testRoot;
        private string installation;

        [SetUp]
        public void CreateInstallation()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-contentfolders-" + Guid.NewGuid().ToString("N"));
            installation = Path.Combine(testRoot, "OMSI 2");
            Write(@"Sceneryobjects\loose.sco");
            Write(@"Sceneryobjects\Trees_MC\birch.sco");
            Write(@"Sceneryobjects\Trees_MC\Deep\oak.sco");
            Write(@"Sceneryobjects\Buildings_RUE\house.sco");
            Write(@"Sceneryobjects\Mapforge\Grundorf\Junction\made.sco");
            Write(@"Sceneryobjects\Mapforge\Other map\Junction\other.sco");
            Write(@"Splines\Strassen\road.sli");
            Write(@"Splines\Rails\track.sli");
            Write(@"Splines\Mapforge\Grundorf\mine.sli");
        }

        [TearDown]
        public void DeleteInstallation()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        private void Write(string relative)
        {
            var path = Path.Combine(installation, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, OmsiTextFile.Encode("[groups]\r\n1\r\nTest\r\n", OmsiTextEncoding.Windows1252));
        }

        [Test]
        public void TheFoldersOfAnInstallationAreTheFirstLevelAndTheProjectFoldersOfTheEditor()
        {
            CollectionAssert.AreEqual(new[]
                {
                    @"Sceneryobjects\Buildings_RUE", @"Sceneryobjects\Mapforge\Grundorf", @"Sceneryobjects\Mapforge\Other map", @"Sceneryobjects\Trees_MC"
                },
                OmsiContentFolders.FoldersOf(installation, OmsiContentFolders.SceneryObjectsFolder));
            CollectionAssert.AreEqual(new[] { @"Splines\Mapforge\Grundorf", @"Splines\Rails", @"Splines\Strassen" },
                OmsiContentFolders.FoldersOf(installation, OmsiContentFolders.SplinesFolder));
            CollectionAssert.IsEmpty(OmsiContentFolders.FoldersOf(Path.Combine(testRoot, "missing"), OmsiContentFolders.SplinesFolder));
        }

        [Test]
        public void NormalizeKeepsFirstLevelFoldersAndProjectFoldersOfTheEditor()
        {
            Assert.AreEqual(@"Sceneryobjects\Trees_MC", OmsiContentFolders.Normalize("sceneryobjects/Trees_MC/"));
            Assert.AreEqual(@"Splines\Rails", OmsiContentFolders.Normalize(@"\SPLINES\Rails"));
            Assert.IsNull(OmsiContentFolders.Normalize(@"Sceneryobjects\Mapforge"));
            Assert.IsNull(OmsiContentFolders.Normalize(@"Splines\mapforge"));
            Assert.AreEqual(@"Sceneryobjects\Mapforge\Grundorf", OmsiContentFolders.Normalize(@"sceneryobjects\mapforge\Grundorf"));
            Assert.AreEqual(@"Splines\Mapforge\Grundorf", OmsiContentFolders.ProjectFolder(OmsiContentFolders.SplinesFolder, "Grundorf"));
            Assert.AreEqual(@"Sceneryobjects\Mapforge\My_map", OmsiContentFolders.ProjectFolder(OmsiContentFolders.SceneryObjectsFolder, "My/map"));
            Assert.IsNull(OmsiContentFolders.Normalize(@"Sceneryobjects\Trees_MC\Deep"));
            Assert.IsNull(OmsiContentFolders.Normalize("Sceneryobjects"));
            Assert.IsNull(OmsiContentFolders.Normalize(@"Vehicles\Bus"));
            Assert.IsNull(OmsiContentFolders.Normalize(@"Sceneryobjects\.."));
            Assert.IsNull(OmsiContentFolders.Normalize(" "));
        }

        [Test]
        public void TheScanLeavesOutTheChosenFoldersAndTheirSubfolders()
        {
            var excluded = new[] { @"Sceneryobjects\Trees_MC", @"Splines\Rails" };

            var catalog = OmsiInstallationCatalog.Scan(installation, Array.Empty<string>(), excluded);

            CollectionAssert.AreEqual(new[]
                {
                    @"Sceneryobjects\Buildings_RUE\house.sco", @"Sceneryobjects\loose.sco", @"Sceneryobjects\Mapforge\Grundorf\Junction\made.sco",
                    @"Sceneryobjects\Mapforge\Other map\Junction\other.sco"
                },
                catalog.Objects);
            CollectionAssert.AreEqual(new[] { @"Splines\Mapforge\Grundorf\mine.sli", @"Splines\Strassen\road.sli" }, catalog.SplineProfiles);
            Assert.IsFalse(catalog.Groups.Keys.Any(path => path.Contains("Trees_MC")));
            CollectionAssert.AreEquivalent(excluded, catalog.ExcludedFolders);
        }

        [Test]
        public void TheScanWithoutChoiceListsEverything()
        {
            var catalog = OmsiInstallationCatalog.Scan(installation, Array.Empty<string>());

            Assert.AreEqual(6, catalog.Objects.Count);
            Assert.AreEqual(3, catalog.SplineProfiles.Count);
            CollectionAssert.IsEmpty(catalog.ExcludedFolders);
        }

        [Test]
        public void TheEditorsFolderStaysButItsProjectFoldersCanBeLeftOut()
        {
            var folders = new OmsiContentFolders();
            folders.SetExcluded(installation, new[] { @"Sceneryobjects\Mapforge", @"Splines\Mapforge", @"Splines\Rails", @"Sceneryobjects\Mapforge\Other map" });

            CollectionAssert.AreEquivalent(new[] { @"Splines\Rails", @"Sceneryobjects\Mapforge\Other map" }, folders.ExcludedFor(installation));
            var catalog = OmsiInstallationCatalog.Scan(installation, Array.Empty<string>(), folders.ExcludedFor(installation));
            CollectionAssert.Contains(catalog.Objects, @"Sceneryobjects\Mapforge\Grundorf\Junction\made.sco");
            CollectionAssert.DoesNotContain(catalog.Objects, @"Sceneryobjects\Mapforge\Other map\Junction\other.sco");
            CollectionAssert.Contains(catalog.SplineProfiles, @"Splines\Mapforge\Grundorf\mine.sli");
        }

        [Test]
        public void EachInstallationKeepsItsOwnChoice()
        {
            var folders = new OmsiContentFolders();
            folders.SetExcluded(@"D:\SteamLibrary\steamapps\common\OMSI 2", new[] { @"Sceneryobjects\Trees_MC" });
            folders.SetExcluded(@"D:\openOMSI\", new[] { @"Splines\Rails" });

            CollectionAssert.AreEquivalent(new[] { @"Sceneryobjects\Trees_MC" }, folders.ExcludedFor(@"d:/steamlibrary/steamapps/common/omsi 2/"));
            CollectionAssert.AreEquivalent(new[] { @"Splines\Rails" }, folders.ExcludedFor(@"D:\openOMSI"));
            CollectionAssert.IsEmpty(folders.ExcludedFor(@"C:\Other"));

            folders.SetExcluded(@"D:\openOMSI", new string[0]);
            CollectionAssert.IsEmpty(folders.ExcludedFor(@"D:\openOMSI"));
            Assert.AreEqual(1, folders.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [Test]
        public void TheChoiceSurvivesSavingAndLoading()
        {
            var path = Path.Combine(testRoot, "Local", "OMSI MapWorks", "content-folders.txt");
            var folders = new OmsiContentFolders();
            folders.SetExcluded(installation, new[] { @"Sceneryobjects\Trees_MC", @"Splines\Rails" });
            folders.Save(path);
            folders.SetExcluded(installation, new[] { @"Sceneryobjects\Buildings_RUE" });
            folders.Save(path);

            var loaded = OmsiContentFolders.Load(path);

            CollectionAssert.AreEquivalent(new[] { @"Sceneryobjects\Buildings_RUE" }, loaded.ExcludedFor(installation));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }

        [Test]
        public void AMissingOrBrokenFileLeavesNothingOut()
        {
            CollectionAssert.IsEmpty(OmsiContentFolders.Load(Path.Combine(testRoot, "none.txt")).ExcludedFor(installation));

            var parsed = OmsiContentFolders.Parse("broken line\n\t\tSplines\\Rails\nD:\\OMSI 2\tVehicles\\Bus\nD:\\OMSI 2\tSplines\\Rails\r\n");

            CollectionAssert.AreEquivalent(new[] { @"Splines\Rails" }, parsed.ExcludedFor(@"D:\OMSI 2"));
        }

        [Test]
        public void SurfaceTexturesSkipProfilesInFoldersLeftOut()
        {
            var texture = Path.Combine(installation, "Splines", "Rails", "texture", "gravel.bmp");
            Directory.CreateDirectory(Path.GetDirectoryName(texture));
            File.WriteAllBytes(texture, new byte[] { 0 });
            File.WriteAllText(Path.Combine(installation, "Splines", "Rails", "track.sli"),
                "[texture]\r\ngravel.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n0\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n3\r\n0\r\n1\r\n1\r\n");

            var all = OmsiRoadTypeStore.SurfaceTextures(installation);
            var without = OmsiRoadTypeStore.SurfaceTextures(installation, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"Splines\Rails" });

            Assert.IsTrue(all.Exists(path => path.EndsWith("gravel.bmp", StringComparison.OrdinalIgnoreCase)), string.Join(", ", all));
            CollectionAssert.IsEmpty(without);
        }
    }
}
