using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The Editor.OMSI side of saving, deleting and the state of a road type in the road type window (C8).</summary>
    public sealed class OmsiRoadTypeSavingTests
    {
        private const string Mine = @"Splines\Mapforge\Test\Town road.sli";
        private const string Other = @"Splines\Mapforge\Elsewhere\Town road.sli";
        private string installation;

        [SetUp]
        public void MakeInstallation()
        {
            installation = Path.Combine(Path.GetTempPath(), "mapforge-roadusage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(installation, "maps", "Test"));
        }

        [TearDown]
        public void DeleteInstallation()
        {
            if (Directory.Exists(installation))
                Directory.Delete(installation, true);
        }

        private OmsiMapSummary Map(params string[] tileLines)
        {
            var folder = Path.Combine(installation, "maps", "Test");
            File.WriteAllText(Path.Combine(folder, "global.cfg"), string.Join("\r\n", "[name]", "Test", "", "[map]", "0", "0", "tile_0_0.map", ""));
            File.WriteAllText(Path.Combine(folder, "tile_0_0.map"), string.Join("\r\n", new[] { "[version]", "14", "" }.Concat(tileLines)));
            return OmsiMapReader.Read(Path.Combine(folder, "global.cfg"));
        }

        private static string[] Spline(int id, string path)
        {
            return new[] { "[spline]", "0", path, id.ToString(), "0", "0", "18.25", "2.5", "147", "445.6", "143.18", "-120", "0", "0", "0", "0", "0", "0", "0", "", "" };
        }

        private static string[] Item(int id, string path)
        {
            return new[] { "[object]", "0", path, id.ToString(), "25.5", "243.25", "1.5", "90", "0", "0", "0", "", "" };
        }

        /// <summary>A junction file of the kind the editor makes: spline helpers naming the road types of its arms.</summary>
        private void WriteJunction(string name, params string[] arms)
        {
            var folder = Path.Combine(installation, "Sceneryobjects", "Mapforge", "Test", name);
            Directory.CreateDirectory(folder);
            var text = "Mapforge " + name + ": a junction of " + arms.Length + " roads\r\n";
            foreach (var arm in arms)
                text += "[splinehelper]\r\n" + arm + "\r\n0\r\n0\r\n0\r\n0\r\n\r\n";
            File.WriteAllText(Path.Combine(folder, name + ".sco"), text);
        }

        [Test]
        public void TheOpenMapsPiecesAndJunctionsAreCountedForARoadType()
        {
            WriteJunction("Cross", Mine, Mine, Other);
            WriteJunction("Elsewhere", Other, Other);
            var map = Map(Spline(1, Mine).Concat(Spline(2, "splines/mapforge/test/TOWN ROAD.sli")).Concat(Spline(3, Other))
                .Concat(Item(10, @"Sceneryobjects\Mapforge\Test\Cross\Cross.sco")).Concat(Item(11, @"Sceneryobjects\Mapforge\Test\Elsewhere\Elsewhere.sco"))
                .Concat(Item(12, @"Sceneryobjects\Trees\tree.sco")).ToArray());

            var usage = OmsiRoadTypeUsage.Of(map, installation, Mine);

            Assert.AreEqual(2, usage.Pieces, "either slash and any case name the same road type");
            Assert.AreEqual(1, usage.JunctionCount, "only a made junction with the road type on an arm");
            Assert.AreEqual(10, usage.Junctions.Single().Value.Id);
            Assert.IsFalse(usage.IsUnused);

            var other = OmsiRoadTypeUsage.Of(map, installation, Other);
            Assert.AreEqual(1, other.Pieces);
            Assert.AreEqual(2, other.JunctionCount);
        }

        [Test]
        public void AnUnusedRoadTypeHasNoUsesAndAMissingMapNone()
        {
            var map = Map(Spline(1, Other));
            Assert.IsTrue(OmsiRoadTypeUsage.Of(map, installation, Mine).IsUnused);
            Assert.IsTrue(OmsiRoadTypeUsage.Of(null, installation, Mine).IsUnused);
            Assert.IsTrue(OmsiRoadTypeUsage.Of(map, installation, null).IsUnused);
            // Without an installation the junction files cannot be read; the pieces still count.
            Assert.AreEqual(1, OmsiRoadTypeUsage.Of(map, null, Other).Pieces);
            Assert.AreEqual(0, OmsiRoadTypeUsage.Of(map, null, Other).JunctionCount);
        }

        [Test]
        public void ARoadTypeOfAnotherProjectIsNotMine()
        {
            Assert.IsTrue(OmsiRoadTypeOwnership.InProjectFolder(Mine, "Test"));
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(Other, "Test"));
            Assert.IsFalse(OmsiRoadTypeOwnership.InProjectFolder(@"Splines\Marcel\road.sli", "Test"));
        }

        [Test]
        public void ASavedCopyOfARoadTypeGoesIntoTheProjectFolderAndANameInUseGetsANumber()
        {
            var textures = Path.Combine(installation, "Splines", "Marcel", "texture");
            Directory.CreateDirectory(textures);
            foreach (var texture in new[] { "str_asphdrk.bmp", "str_side1.bmp", "str_gruenstreifen.bmp" })
                File.WriteAllBytes(Path.Combine(textures, texture), new byte[] { 1 });
            var type = OmsiRoadType.Parse("walk 3 | drive 3.25 back | drive 3.25 | walk 3");

            var first = OmsiRoadTypeStore.Write(installation, "Test", "Town road copy", type);
            var second = OmsiRoadTypeStore.Write(installation, "Test", "Town road copy", type);

            Assert.AreEqual(@"Splines\Mapforge\Test\Town road copy.sli", first);
            Assert.AreEqual(@"Splines\Mapforge\Test\Town road copy_2.sli", second);
            Assert.IsTrue(OmsiRoadTypeOwnership.InProjectFolder(first, "Test"));
            Assert.IsTrue(OmsiRoadTypeOwnership.InProjectFolder(second, "Test"));
        }

        [Test]
        public void AReplaceThatCannotWriteLeavesTheFileAndNoCopyBehind()
        {
            var textures = Path.Combine(installation, "Splines", "Marcel", "texture");
            Directory.CreateDirectory(textures);
            foreach (var texture in new[] { "str_asphdrk.bmp", "str_side1.bmp", "str_gruenstreifen.bmp" })
                File.WriteAllBytes(Path.Combine(textures, texture), new byte[] { 1 });
            var path = OmsiRoadTypeStore.Write(installation, "Test", "Locked", OmsiRoadType.Parse("walk 3 | drive 3.25 back | drive 3.25 | walk 3"));
            var file = Path.Combine(installation, path);
            var before = File.ReadAllBytes(file);
            File.SetAttributes(file, FileAttributes.ReadOnly);
            try
            {
                Assert.Catch<Exception>(() => OmsiRoadTypeStore.Replace(installation, path, OmsiRoadType.Parse("walk 4 | drive 3.5 back | drive 3.5 | walk 4")));

                Assert.IsFalse(File.Exists(file + ".tmp"), "the copy written beside it is taken away again");
                CollectionAssert.AreEqual(before, File.ReadAllBytes(file), "the road type as it was");
            }
            finally
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }

        [Test]
        public void TheNotesOfTakingARoadApartCarryTheirCodesAndKeepTheEnglishSentence()
        {
            var wide = OmsiRoadType.Parse("walk 4.9 | walk 4.9");
            var sli = OmsiTextFile.Encode(OmsiRoadTypeWriter.Write(wide).Sli, OmsiTextEncoding.Windows1252);
            var profile = OmsiSplineProfile.Parse(sli, @"Splines\Mapforge\Test\wide.sli");

            var notes = new List<OmsiRoadTypeNote>();
            var text = new List<string>();
            var typeWithNotes = OmsiRoadType.TakeApart(profile, notes);
            var typeWithText = OmsiRoadType.FromProfile(profile, text);

            Assert.AreEqual(typeWithText.Describe(), typeWithNotes.Describe());
            Assert.AreEqual(1, notes.Count);
            var note = notes[0];
            Assert.AreEqual(OmsiRoadTypeNoteCode.WidthFitted, note.Code);
            Assert.AreEqual(OmsiRoadLaneKind.Sidewalk, note.Kind);
            Assert.AreEqual(1, note.LaneNumber);
            Assert.AreEqual(4.9, note.Fitted, 1e-9);
            Assert.AreEqual(9.8, note.Original, 1e-9);
            Assert.AreEqual(text.Single(), note.ToString(), "the English sentence stays for the log and the command line");
            StringAssert.Contains("is 4.90 m wide instead of 9.80 m", note.ToString());
        }

        [Test]
        public void TheOtherNotesHaveCodesAndSentencesToo()
        {
            var limit = OmsiRoadTypeNote.LanesLimited(OmsiRoadType.MaximumLanes);
            Assert.AreEqual(OmsiRoadTypeNoteCode.LanesLimited, limit.Code);
            Assert.AreEqual(OmsiRoadType.MaximumLanes, limit.Limit);
            Assert.AreEqual("Only the first " + OmsiRoadType.MaximumLanes + " lanes are taken.", limit.ToString());
            var tracks = OmsiRoadTypeNote.TracksLeftOut();
            Assert.AreEqual(OmsiRoadTypeNoteCode.TracksLeftOut, tracks.Code);
            Assert.AreEqual("Tracks outside the carriageway are left out.", tracks.ToString());
        }

        [Test]
        public void AStaleDraftIsFreshAgainWhenTheFileReadsLikeItsSavedStateAgain()
        {
            var draft = new OmsiRoadTypeDraft(OmsiRoadType.Parse("walk 3 | drive 3.25 back | drive 3.25 | walk 3"), Mine);
            var before = OmsiRoadType.Parse("walk 3 | drive 3.25 back | drive 3.25 | walk 3");

            Assert.IsFalse(draft.CheckSaved(before));
            var version = draft.Version;
            Assert.IsTrue(draft.CheckSaved(OmsiRoadType.Parse("walk 3")), "undone on the map");
            Assert.IsTrue(draft.IsStale);
            Assert.Greater(draft.Version, version, "a view can tell to redraw");
            Assert.IsTrue(draft.CheckSaved(null), "a file that is gone is stale too");
            Assert.IsFalse(draft.CheckSaved(before), "redone on the map: as saved again");
            Assert.IsFalse(draft.IsStale);
        }
    }
}
