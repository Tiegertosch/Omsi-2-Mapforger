using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiRoadTypesTests
    {
        private const string TwoLanes = "walk 3 | drive 3.25 back | drive 3.25 | walk 3";
        private const string ProfilePath = @"Splines\Mapforge\Test\Road.sli";

        private static OmsiSplineProfile Profile(string description, out OmsiRoadTypeFiles files)
        {
            files = OmsiRoadTypeWriter.Write(OmsiRoadType.Parse(description));
            return OmsiSplineProfile.Parse(OmsiTextFile.Encode(files.Sli, OmsiTextEncoding.Windows1252), ProfilePath);
        }

        [Test]
        public void ADescriptionReadsBackAsItWasWritten()
        {
            var type = OmsiRoadType.Parse("walk 3 | drive 3,5 back | drive | median 2 | shoulder 1.25");

            Assert.AreEqual(5, type.Lanes.Count);
            Assert.AreEqual(OmsiRoadLaneKind.Sidewalk, type.Lanes[0].Kind);
            Assert.AreEqual(3.5, type.Lanes[1].Width, 1e-9);
            Assert.AreEqual(OmsiRoadLaneDirection.Backward, type.Lanes[1].Direction);
            Assert.AreEqual(OmsiRoadLane.DefaultWidth(OmsiRoadLaneKind.Driving), type.Lanes[2].Width, 1e-9);
            Assert.AreEqual(OmsiRoadLaneDirection.Forward, type.Lanes[2].Direction);
            Assert.AreEqual("walk 3.00 | drive 3.50 back | drive 3.25 | median 2.00 | shoulder 1.25", type.Describe());
            Assert.AreEqual(type.Describe(), OmsiRoadType.Parse(OmsiRoadType.DescriptionPrefix + " " + type.Describe()).Describe());
            Assert.Throws<FormatException>(() => OmsiRoadType.Parse("train 2"));
            Assert.Throws<FormatException>(() => OmsiRoadType.Parse("drive 3 cars"));
            Assert.Throws<FormatException>(() => OmsiRoadType.Parse("drive wide"));
        }

        [Test]
        public void EveryTemplateCanBeMade()
        {
            foreach (var template in OmsiRoadType.Templates)
                CollectionAssert.IsEmpty(OmsiRoadType.Parse(template.Value).Problems(), template.Key);
        }

        [Test]
        public void LanesTooWideForTheirTextureAreRefused()
        {
            CollectionAssert.IsNotEmpty(OmsiRoadType.Parse("walk 6 | drive 3.25").Problems());
            CollectionAssert.IsNotEmpty(OmsiRoadType.Parse("drive 6").Problems());
            CollectionAssert.IsNotEmpty(OmsiRoadType.Parse("drive 2").Problems());
            // Kerbs on both sides lay a median in two halves, so it may be twice as wide.
            CollectionAssert.IsEmpty(OmsiRoadType.Parse("drive back | median 12 | drive").Problems());
            Assert.Throws<ArgumentException>(() => OmsiRoadTypeWriter.Write(OmsiRoadType.Parse("drive 6")));
        }

        [Test]
        public void MarkingsAreChosenAsOnGermanRoads()
        {
            var two = OmsiRoadType.Parse(TwoLanes);
            Assert.AreEqual(OmsiRoadMarking.None, two.MarkingAfter(0));
            Assert.AreEqual(OmsiRoadMarking.Dashed, two.MarkingAfter(1));
            Assert.AreEqual(OmsiRoadMarking.None, two.MarkingAfter(2));

            var four = OmsiRoadType.Parse("drive back | drive back | drive | drive");
            Assert.AreEqual(OmsiRoadMarking.Dashed, four.MarkingAfter(0));
            Assert.AreEqual(OmsiRoadMarking.Solid, four.MarkingAfter(1));

            var country = OmsiRoadType.Parse("shoulder 1 | drive back | drive | shoulder 1");
            Assert.AreEqual(OmsiRoadMarking.Solid, country.MarkingAfter(0));
            Assert.AreEqual(OmsiRoadMarking.Dashed, country.MarkingAfter(1));

            // A median splits the carriageways: each side counts its own lanes.
            var dual = OmsiRoadType.Parse("drive back | drive back | median | drive | drive");
            Assert.AreEqual(OmsiRoadMarking.Dashed, dual.MarkingAfter(0));
            Assert.AreEqual(OmsiRoadMarking.None, dual.MarkingAfter(1));
        }

        [Test]
        public void ATwoLaneRoadIsWrittenWithKerbsLevelsLanesAndADashedCentreLine()
        {
            var profile = Profile(TwoLanes, out var files);

            Assert.AreEqual(-6.25, profile.LeftEdge, 1e-6);
            Assert.AreEqual(6.25, profile.RightEdge, 1e-6);
            // Sidewalks used both ways, one lane against the spline on the left and one with it on the right.
            Assert.AreEqual(4, profile.Lanes.Count);
            Assert.AreEqual(1, profile.Lanes[0].Kind);
            Assert.AreEqual(2, profile.Lanes[0].Direction);
            Assert.AreEqual(OmsiRoadTypeWriter.RaisedHeight, profile.Lanes[0].Height, 1e-9);
            Assert.AreEqual(0, profile.Lanes[1].Kind);
            Assert.AreEqual(-1.625, profile.Lanes[1].X, 1e-6);
            Assert.AreEqual(1, profile.Lanes[1].Direction);
            Assert.AreEqual(3.25, profile.Lanes[1].Width, 1e-6);
            Assert.AreEqual(1.625, profile.Lanes[2].X, 1e-6);
            Assert.AreEqual(0, profile.Lanes[2].Direction);
            Assert.AreEqual(OmsiRoadTypeWriter.RoadHeight, profile.Lanes[2].Height, 1e-9);

            // Two kerb faces, upright from road to kerb height at the edges of the carriageway.
            var upright = profile.Strips.Where(strip => Math.Abs(strip.Points[0].X - strip.Points[1].X) < 1e-9).ToList();
            Assert.AreEqual(2, upright.Count);
            CollectionAssert.AreEquivalent(new[] { -3.25, 3.25 }, upright.Select(strip => Math.Round(strip.Points[0].X, 3)).ToArray());

            // The dashed centre line: an alpha texture repeating every 9 m, over the asphalt.
            var dash = profile.Textures.FindIndex(texture => texture.Path.EndsWith(OmsiRoadTypeWriter.DashTexture, StringComparison.OrdinalIgnoreCase));
            Assert.GreaterOrEqual(dash, 0);
            Assert.IsTrue(profile.Textures[dash].UsesAlpha);
            var line = profile.Strips.Single(strip => strip.TextureIndex == dash);
            Assert.AreEqual(-OmsiRoadTypeWriter.MarkingWidth / 2.0, line.Points[0].X, 1e-6);
            Assert.AreEqual(OmsiRoadTypeWriter.RoadHeight + OmsiRoadTypeWriter.MarkingLift, line.Points[0].Height, 1e-6);
            Assert.AreEqual(1.0 / OmsiRoadTypeWriter.DashRepeat, line.Points[0].VPerMeter, 1e-3);
            Assert.IsFalse(profile.Textures.Any(texture => texture.Path.EndsWith(OmsiRoadTypeWriter.WhiteTexture, StringComparison.OrdinalIgnoreCase)));

            // Textures of the base game to copy, and the dash made by the editor.
            CollectionAssert.AreEquivalent(new[] { OmsiRoadTypeWriter.SidewalkTexture, OmsiRoadTypeWriter.AsphaltTexture }, files.Textures);
            CollectionAssert.AreEquivalent(new[] { OmsiRoadTypeWriter.DashTexture }, files.Files.Keys);
            Assert.IsTrue(files.Sli.Contains("[heightprofile]\r\n-3.250\r\n3.250\r\n0.100\r\n0.100"));
            Assert.IsTrue(files.Sli.Contains("[heightprofile]\r\n-6.250\r\n-3.250\r\n0.250\r\n0.250"));
        }

        [Test]
        public void ARoadTypeIsSortedAndReadByJunctionsLikeTheBaseGameRoads()
        {
            var two = Profile(TwoLanes, out _);
            Assert.AreEqual(OmsiSplineCategory.TwoLanes, OmsiSplineCategories.Of(two));
            var sections = OmsiProfileSections.Of(two);
            Assert.AreEqual(3.25, sections.RoadLeft, 1e-6);
            Assert.AreEqual(3.25, sections.RoadRight, 1e-6);
            Assert.AreEqual(OmsiRoadTypeWriter.RoadHeight, sections.RoadHeight, 1e-6);
            Assert.AreEqual(OmsiRoadTypeWriter.RaisedHeight, sections.PavementHeight, 1e-6);
            Assert.GreaterOrEqual(sections.KerbTexture, 0);
            Assert.IsTrue(two.Textures[sections.RoadTexture].Path.EndsWith("str_asphdrk.bmp", StringComparison.OrdinalIgnoreCase));

            var dual = Profile("walk 3.5 | drive back | drive back | median 3 | drive | drive | walk 3.5", out _);
            Assert.AreEqual(OmsiSplineCategory.Median, OmsiSplineCategories.Of(dual));
            Assert.AreEqual(OmsiSplineCategory.OneWay, OmsiSplineCategories.Of(Profile("walk | drive | drive | walk", out _)));
            Assert.AreEqual(OmsiSplineCategory.FourLanes, OmsiSplineCategories.Of(Profile("drive back | drive back | drive | drive", out _)));
            Assert.AreEqual(OmsiSplineCategory.Path, OmsiSplineCategories.Of(Profile("walk 3", out _)));
        }

        [Test]
        public void AMedianWithKerbsOnBothSidesIsLaidInTwoHalves()
        {
            var profile = Profile("drive back | median 4 | drive", out _);

            var grass = profile.Textures.FindIndex(texture => texture.Path.EndsWith("str_gruenstreifen.bmp", StringComparison.OrdinalIgnoreCase));
            var level = profile.Strips.Where(strip => strip.TextureIndex == grass && Math.Abs(strip.Points[0].X - strip.Points[1].X) > 1e-6).ToList();
            Assert.AreEqual(2, level.Count);
            // Each half ends at the kerb texture by its own carriageway.
            Assert.AreEqual(0.973, level[0].Points[0].U, 1e-6);
            Assert.AreEqual(0.973, level[1].Points[1].U, 1e-6);
            // And a kerb face on each side.
            Assert.AreEqual(4, profile.Strips.Count(strip => strip.TextureIndex == grass));
        }

        [Test]
        public void LinesAndTexturesChosenForLanesAreKeptInTheDescription()
        {
            var type = OmsiRoadType.Parse("bus 3.5 back right=broad-dashed | bike | parking tex=\"Splines\\My Roads\\texture\\red asphalt.bmp\" | drive right=solid-dashed");

            Assert.AreEqual(OmsiRoadLaneKind.Bus, type.Lanes[0].Kind);
            Assert.AreEqual(OmsiRoadLaneDirection.Backward, type.Lanes[0].Direction);
            Assert.AreEqual(OmsiRoadMarking.BroadDashed, type.Lanes[0].RightMarking);
            Assert.AreEqual(OmsiRoadLane.DefaultWidth(OmsiRoadLaneKind.Bike), type.Lanes[1].Width, 1e-9);
            Assert.IsNull(type.Lanes[1].RightMarking);
            Assert.AreEqual(@"Splines\My Roads\texture\red asphalt.bmp", type.Lanes[2].Texture);
            Assert.AreEqual(OmsiRoadMarking.SolidDashed, type.Lanes[3].RightMarking);
            Assert.AreEqual(type.Describe(), OmsiRoadType.Parse(type.Describe()).Describe());
            StringAssert.Contains("tex=\"Splines\\My Roads\\texture\\red asphalt.bmp\"", type.Describe());
            Assert.Throws<FormatException>(() => OmsiRoadType.Parse("drive right=zigzag"));
        }

        [Test]
        public void BusAndBikeLanesGetABroadLineAndParkingNone()
        {
            var type = OmsiRoadType.Parse("walk | parking | bike | bus | drive | drive back | walk");

            Assert.AreEqual(OmsiRoadMarking.None, type.AutoMarking(1));
            Assert.AreEqual(OmsiRoadMarking.Broad, type.AutoMarking(2));
            Assert.AreEqual(OmsiRoadMarking.Broad, type.AutoMarking(3));
            Assert.AreEqual(OmsiRoadMarking.Dashed, type.AutoMarking(4));
            Assert.AreEqual(OmsiRoadMarking.Broad, OmsiRoadType.Parse("bike | drive").AutoMarking(0));

            // Bus lanes are driven like driving lanes; bike and parking lanes have no AI lane.
            var profile = Profile("walk | parking | bike | bus | drive | drive back | walk", out _);
            Assert.AreEqual(5, profile.Lanes.Count);
            Assert.AreEqual(3, profile.Lanes.Count(lane => lane.Kind == 0));
        }

        [Test]
        public void ChosenLinesArePaintedWithTheirStrokes()
        {
            var profile = Profile("drive back right=double | drive right=solid-dashed | drive right=broad-dashed | drive right=long | drive", out var files);

            var white = profile.Textures.FindIndex(texture => texture.Path.EndsWith(OmsiRoadTypeWriter.WhiteTexture, StringComparison.OrdinalIgnoreCase));
            var dash = profile.Textures.FindIndex(texture => texture.Path.EndsWith(OmsiRoadTypeWriter.DashTexture, StringComparison.OrdinalIgnoreCase));
            var half = profile.Textures.FindIndex(texture => texture.Path.EndsWith(OmsiRoadTypeWriter.HalfDashTexture, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(profile.Textures[half].UsesAlpha);
            // Boundaries at -4.875, -1.625, 1.625 and 4.875 m; the strokes of a line lie 0.12 m left and right of it.
            double Middle(OmsiProfileStrip strip) => (strip.Points[0].X + strip.Points[1].X) / 2.0;
            var whites = profile.Strips.Where(strip => strip.TextureIndex == white).Select(Middle).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { -4.995, -4.755, -1.745 }, whites.Select(x => Math.Round(x, 3)).ToArray());
            var dashes = profile.Strips.Where(strip => strip.TextureIndex == dash).OrderBy(Middle).ToList();
            Assert.AreEqual(2, dashes.Count);
            Assert.AreEqual(-1.505, Middle(dashes[0]), 1e-6);
            Assert.AreEqual(1.0 / OmsiRoadTypeWriter.DashRepeat, dashes[0].Points[0].VPerMeter, 1e-3);
            Assert.AreEqual(4.875, Middle(dashes[1]), 1e-6);
            Assert.AreEqual(1.0 / OmsiRoadTypeWriter.LongDashRepeat, dashes[1].Points[0].VPerMeter, 1e-3);
            var broad = profile.Strips.Single(strip => strip.TextureIndex == half);
            Assert.AreEqual(OmsiRoadTypeWriter.BroadWidth, broad.Points[1].X - broad.Points[0].X, 1e-6);
            Assert.AreEqual(1.625, Middle(broad), 1e-6);
            CollectionAssert.AreEquivalent(new[] { OmsiRoadTypeWriter.WhiteTexture, OmsiRoadTypeWriter.DashTexture, OmsiRoadTypeWriter.HalfDashTexture }, files.Files.Keys);
        }

        [Test]
        public void ALineAtAKerbLiesOnTheCarriagewayAsAnEdgeLine()
        {
            var profile = Profile("walk right=solid | drive back | drive right=solid | walk", out _);

            var white = profile.Textures.FindIndex(texture => texture.Path.EndsWith(OmsiRoadTypeWriter.WhiteTexture, StringComparison.OrdinalIgnoreCase));
            var lines = profile.Strips.Where(strip => strip.TextureIndex == white).Select(strip => Math.Round((strip.Points[0].X + strip.Points[1].X) / 2.0, 3)).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { -3.25 + OmsiRoadTypeWriter.KerbInset, 3.25 - OmsiRoadTypeWriter.KerbInset }, lines);
            // Between two raised lanes there is no carriageway to paint on.
            Assert.IsFalse(Profile("walk right=solid | median", out _).Textures.Any(texture => texture.Path.EndsWith(OmsiRoadTypeWriter.WhiteTexture, StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public void ATextureChosenForALaneCoversItOnceAcross()
        {
            const string red = @"Splines\Other\texture\red.bmp";
            var profile = Profile("drive tex=\"" + red + "\" | walk tex=\"" + red + "\"", out var files);

            var index = profile.Textures.FindIndex(texture => texture.Path.EndsWith("red.bmp", StringComparison.OrdinalIgnoreCase));
            var strips = profile.Strips.Where(strip => strip.TextureIndex == index).ToList();
            Assert.AreEqual(2, strips.Count);
            Assert.AreEqual(0.005, strips[0].Points[0].U, 1e-6);
            Assert.AreEqual(0.995, strips[0].Points[1].U, 1e-6);
            Assert.AreEqual(1.0 / 3.25, strips[0].Points[0].VPerMeter, 1e-3);
            // The kerb face keeps the sidewalk's own texture.
            Assert.IsTrue(files.Textures.Contains(OmsiRoadTypeWriter.SidewalkTexture));
            Assert.IsTrue(files.Textures.Contains(red));
            Assert.IsFalse(files.Textures.Contains(OmsiRoadTypeWriter.AsphaltTexture));
        }

        [Test]
        public void TheDashTextureIsATargaWithAClearGap()
        {
            Assert.IsTrue(OmsiImageReader.TryDecode(OmsiRoadTypeWriter.DashBitmap(1.0 / 3.0), out var image));
            Assert.AreEqual(4, image.Channels);
            var alphas = Enumerable.Range(0, image.Height).Select(row => image.Pixels[row * image.Width * 4 + 3]).ToArray();
            var opaque = alphas.Count(alpha => alpha == 255);
            Assert.AreEqual(image.Height, opaque + alphas.Count(alpha => alpha == 0));
            Assert.AreEqual(1.0 / 3.0, opaque / (double)image.Height, 0.01);
        }

        [Test]
        public void TheStoreWritesANewFileWithTexturesAndReadsTheLanesBack()
        {
            var installation = Path.Combine(Path.GetTempPath(), "mapforge-roadtype-" + Guid.NewGuid().ToString("N"));
            try
            {
                var marcel = Path.Combine(installation, "Splines", "Marcel", "texture");
                Directory.CreateDirectory(marcel);
                foreach (var texture in new[] { "str_asphdrk.bmp", "str_side1.bmp", "str_gruenstreifen.bmp" })
                    File.WriteAllBytes(Path.Combine(marcel, texture), new byte[] { 1 });
                File.WriteAllText(Path.Combine(marcel, "str_asphdrk.bmp.cfg"), "[moisture]");

                var type = OmsiRoadType.Parse(TwoLanes);
                var path = OmsiRoadTypeStore.Write(installation, "My project", "Town road", type);
                var again = OmsiRoadTypeStore.Write(installation, "My project", "Town road", type);

                Assert.AreEqual(@"Splines\Mapforge\My project\Town road.sli", path);
                Assert.AreEqual(@"Splines\Mapforge\My project\Town road_2.sli", again);
                Assert.IsTrue(OmsiRoadTypeStore.IsMade(path));
                Assert.IsFalse(OmsiRoadTypeStore.IsMade(@"Splines\Marcel\str_2spur.sli"));
                var textures = Path.Combine(installation, "Splines", "Mapforge", "My project", "texture");
                foreach (var file in new[] { "str_asphdrk.bmp", "str_asphdrk.bmp.cfg", "str_side1.bmp", OmsiRoadTypeWriter.DashTexture })
                    FileAssert.Exists(Path.Combine(textures, file));
                FileAssert.DoesNotExist(Path.Combine(textures, "str_gruenstreifen.bmp"));

                var read = OmsiRoadTypeStore.Read(File.ReadAllBytes(Path.Combine(installation, path)));
                Assert.AreEqual(type.Describe(), read.Describe());
                Assert.IsNull(OmsiRoadTypeStore.Read(OmsiTextFile.Encode("[texture]\r\na.bmp\r\n", OmsiTextEncoding.Windows1252)));

                // Without the base game's textures nothing is written.
                File.Delete(Path.Combine(marcel, "str_side1.bmp"));
                Assert.Throws<FileNotFoundException>(() => OmsiRoadTypeStore.Write(installation, "My project", "Other", type));
                FileAssert.DoesNotExist(Path.Combine(installation, "Splines", "Mapforge", "My project", "Other.sli"));
            }
            finally
            {
                if (Directory.Exists(installation))
                    Directory.Delete(installation, true);
            }
        }

        [Test]
        public void TexturesOfOneNameFromTwoFoldersAreKeptApartAndSurfacesAreListed()
        {
            var installation = Path.Combine(Path.GetTempPath(), "mapforge-roadtype-" + Guid.NewGuid().ToString("N"));
            try
            {
                var marcel = Path.Combine(installation, "Splines", "Marcel", "texture");
                var other = Path.Combine(installation, "Splines", "Other", "texture");
                Directory.CreateDirectory(marcel);
                Directory.CreateDirectory(other);
                foreach (var texture in new[] { "str_asphdrk.bmp", "str_side1.bmp", "str_gruenstreifen.bmp" })
                    File.WriteAllBytes(Path.Combine(marcel, texture), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(other, "str_asphdrk.bmp"), new byte[] { 2 });
                File.WriteAllText(Path.Combine(other, "str_asphdrk.bmp.cfg"), "[moisture]");

                var type = OmsiRoadType.Parse("drive | drive tex=\"Splines\\Other\\texture\\str_asphdrk.bmp\"");
                var path = OmsiRoadTypeStore.Write(installation, "P", "Mixed", type);

                var textures = Path.Combine(installation, "Splines", "Mapforge", "P", "texture");
                CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(textures, "str_asphdrk.bmp")));
                CollectionAssert.AreEqual(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(textures, "str_asphdrk_2.bmp")));
                FileAssert.Exists(Path.Combine(textures, "str_asphdrk_2.bmp.cfg"));
                var profile = OmsiSplineProfile.Parse(File.ReadAllBytes(Path.Combine(installation, path)), path);
                CollectionAssert.AreEqual(new[] { "str_asphdrk.bmp", "str_asphdrk_2.bmp" },
                    profile.Textures.Take(2).Select(texture => Path.GetFileName(texture.Path)).ToArray());
                // Writing it again finds both copies and adds none.
                OmsiRoadTypeStore.Write(installation, "P", "Mixed", type);
                FileAssert.DoesNotExist(Path.Combine(textures, "str_asphdrk_3.bmp"));

                // Surfaces come from the installation's profiles, most used first, without the editor's own or missing files.
                File.WriteAllText(Path.Combine(installation, "Splines", "Marcel", "a.sli"),
                    "[texture]\r\nstr_side1.bmp\r\n[texture]\r\nmissing.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n0\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n3\r\n0\r\n1\r\n1\r\n" +
                    "[profile]\r\n1\r\n[profilepnt]\r\n3\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n5\r\n0\r\n1\r\n1\r\n");
                File.WriteAllText(Path.Combine(installation, "Splines", "Marcel", "b.sli"),
                    "[texture]\r\nstr_side1.bmp\r\n[texture]\r\nstr_gruenstreifen.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n0\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n3\r\n0\r\n1\r\n1\r\n" +
                    "[profile]\r\n1\r\n[profilepnt]\r\n3\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n3\r\n0.2\r\n1\r\n1\r\n");
                var surfaces = OmsiRoadTypeStore.SurfaceTextures(installation);
                Assert.AreEqual(@"Splines\Marcel\texture\str_side1.bmp", surfaces[0]);
                CollectionAssert.DoesNotContain(surfaces, @"Splines\Marcel\texture\missing.bmp");
                // A texture only on an upright strip is no surface.
                CollectionAssert.DoesNotContain(surfaces, @"Splines\Marcel\texture\str_gruenstreifen.bmp");
                Assert.IsFalse(surfaces.Exists(OmsiRoadTypeStore.IsMade));
            }
            finally
            {
                if (Directory.Exists(installation))
                    Directory.Delete(installation, true);
            }
        }

        private static string BaseTextures()
        {
            var installation = Path.Combine(Path.GetTempPath(), "mapforge-roadtype-" + Guid.NewGuid().ToString("N"));
            var marcel = Path.Combine(installation, "Splines", "Marcel", "texture");
            Directory.CreateDirectory(marcel);
            foreach (var texture in new[] { "str_asphdrk.bmp", "str_side1.bmp", "str_gruenstreifen.bmp" })
                File.WriteAllBytes(Path.Combine(marcel, texture), new byte[] { 1 });
            return installation;
        }

        [Test]
        public void ARoadTypeIsWrittenAnewUnderItsPathAndUndoPutsTheFileBefore()
        {
            var installation = BaseTextures();
            try
            {
                var path = OmsiRoadTypeStore.Write(installation, "P", "Town road", OmsiRoadType.Parse(TwoLanes));
                var file = Path.Combine(installation, path);
                var before = File.ReadAllBytes(file);
                var wider = OmsiRoadType.Parse("walk 3 | drive 3.25 back | median 2 | drive 3.25 | walk 3");

                var change = OmsiRoadTypeStore.Replace(installation, path, wider);

                Assert.AreEqual(path, change.AssetPath);
                Assert.IsFalse(change.Deleted);
                FileAssert.DoesNotExist(Path.Combine(installation, "Splines", "Mapforge", "P", "Town road_2.sli"));
                FileAssert.DoesNotExist(file + ".tmp");
                Assert.AreEqual(wider.Describe(), OmsiRoadTypeStore.Read(File.ReadAllBytes(file)).Describe());
                // The median needs its texture, copied beside the others.
                FileAssert.Exists(Path.Combine(installation, "Splines", "Mapforge", "P", "texture", "str_gruenstreifen.bmp"));
                change.Undo();
                CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
                change.Redo();
                Assert.AreEqual(wider.Describe(), OmsiRoadTypeStore.Read(File.ReadAllBytes(file)).Describe());

                // Only road types the builder made are changed, and only those there.
                Assert.Throws<ArgumentException>(() => OmsiRoadTypeStore.Replace(installation, @"Splines\Marcel\str_2spur.sli", wider));
                Assert.Throws<FileNotFoundException>(() => OmsiRoadTypeStore.Replace(installation, @"Splines\Mapforge\P\Gone.sli", wider));
                // A road type that cannot be made leaves the file as it was.
                Assert.Throws<ArgumentException>(() => OmsiRoadTypeStore.Replace(installation, path, OmsiRoadType.Parse("drive 6")));
                Assert.AreEqual(wider.Describe(), OmsiRoadTypeStore.Read(File.ReadAllBytes(file)).Describe());

                var deletion = OmsiRoadTypeStore.Delete(installation, path);
                Assert.IsTrue(deletion.Deleted);
                FileAssert.DoesNotExist(file);
                FileAssert.Exists(Path.Combine(installation, "Splines", "Mapforge", "P", "texture", "str_asphdrk.bmp"));
                deletion.Undo();
                Assert.AreEqual(wider.Describe(), OmsiRoadTypeStore.Read(File.ReadAllBytes(file)).Describe());
            }
            finally
            {
                Directory.Delete(installation, true);
            }
        }

        [Test]
        public void ARoadTypeIsUsedWhileAMapAWorkspaceOrAMadeJunctionNamesIt()
        {
            var installation = BaseTextures();
            var workspace = Path.Combine(installation, "work");
            try
            {
                var path = OmsiRoadTypeStore.Write(installation, "P", "Town road", OmsiRoadType.Parse(TwoLanes));
                Assert.IsNull(OmsiRoadTypeStore.UsedBy(installation, workspace, path));

                // Tile files name it in any case and with either slash.
                var map = Path.Combine(installation, "maps", "Town");
                Directory.CreateDirectory(map);
                var tile = Path.Combine(map, "tile_0_0.map");
                File.WriteAllText(tile, "[spline]\r\n0\r\nsplines/mapforge/p/TOWN ROAD.sli\r\n");
                Assert.AreEqual(tile, OmsiRoadTypeStore.UsedBy(installation, workspace, path));
                File.WriteAllText(tile, "[spline]\r\n0\r\nSplines\\Mapforge\\P\\Town road_2.sli\r\n");
                Assert.IsNull(OmsiRoadTypeStore.UsedBy(installation, workspace, path));

                var junction = Path.Combine(installation, "Sceneryobjects", "Mapforge", "P", "Junction", "Junction.sco");
                Directory.CreateDirectory(Path.GetDirectoryName(junction));
                File.WriteAllText(junction, "[splinehelper]\r\n" + path + "\r\n");
                Assert.AreEqual(junction, OmsiRoadTypeStore.UsedBy(installation, workspace, path));
                File.Delete(junction);

                Directory.CreateDirectory(workspace);
                var edited = Path.Combine(workspace, "tile_1_0.map");
                File.WriteAllText(edited, path);
                Assert.AreEqual(edited, OmsiRoadTypeStore.UsedBy(installation, workspace, path));
            }
            finally
            {
                Directory.Delete(installation, true);
            }
        }

        [Test]
        public void ProfilesAreTakenApartIntoTheirLanes()
        {
            // The builder's own profiles come apart into the lanes they were made from.
            foreach (var lanes in new[]
                     {
                         TwoLanes, "walk 3.5 | drive 3.25 back | drive 3.25 back | median 3 | drive 3.25 | drive 3.25 | walk 3.5",
                         "walk 3 | drive 3.25 | drive 3.25 | walk 3", "shoulder 1 | drive 3.25 back | drive 3.25 | shoulder 1", "walk 4 | drive 3.5 back | drive 3.5"
                     })
            {
                var notes = new System.Collections.Generic.List<string>();
                var type = OmsiRoadType.FromProfile(Profile(lanes, out _), notes);
                Assert.IsNotNull(type, lanes);
                Assert.AreEqual(OmsiRoadType.Parse(lanes).Describe(), type.Describe(), lanes);
                CollectionAssert.IsEmpty(notes, lanes);
            }

            // Bus lanes have a [path] like driving lanes; bike and parking lanes have none and become a shoulder.
            var parking = OmsiRoadType.FromProfile(Profile("walk 2.5 | parking 2 | drive 3.25 back | drive 3.25 | walk 2.5", out _), null);
            Assert.AreEqual("walk 2.50 | shoulder 2.00 | drive 3.25 back | drive 3.25 | walk 2.50", parking.Describe());

            // A footpath is one sidewalk; a wide one is made as wide as its texture allows, with a note.
            Assert.AreEqual("walk 3.00", OmsiRoadType.FromProfile(Profile("walk 3", out _), null).Describe());
            var wide = OmsiRoadType.Parse("walk 4.9 | walk 4.9");
            var notesWide = new System.Collections.Generic.List<string>();
            var footpath = OmsiRoadType.FromProfile(OmsiSplineProfile.Parse(OmsiTextFile.Encode(OmsiRoadTypeWriter.Write(wide).Sli, OmsiTextEncoding.Windows1252), ProfilePath), notesWide);
            Assert.AreEqual("walk 4.90", footpath.Describe());
            Assert.AreEqual(1, notesWide.Count);

            // Without a carriageway or anyone walking there is nothing to take apart.
            var fence = OmsiSplineProfile.Parse(OmsiTextFile.Encode("[texture]\r\nfence.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n0\r\n0\r\n0\r\n1\r\n[profilepnt]\r\n0\r\n2\r\n1\r\n1\r\n",
                OmsiTextEncoding.Windows1252), @"Splines\x\fence.sli");
            Assert.IsNull(OmsiRoadType.FromProfile(fence, null));
        }

        [Test]
        public void TramLanesLayGroovedRailsAndATrackWithCarsOnItWhenShared()
        {
            var type = OmsiRoadType.Parse("walk 3 | drive 3.25 back | tram 3 back cars | tram 3 | drive 3.25 | walk 3");
            Assert.AreEqual("walk 3.00 | drive 3.25 back | tram 3.00 back cars | tram 3.00 | drive 3.25 | walk 3.00", type.Describe());
            Assert.IsTrue(type.Lanes[2].CarriesTraffic);
            Assert.IsFalse(type.Lanes[3].CarriesTraffic);
            StringAssert.Contains(" tram ", type.SuggestName());
            Assert.AreEqual("Tram track 6m", OmsiRoadType.Parse("tram back | tram").SuggestName());

            // A track shared with cars is a driving lane to the lines; one of its own is kept apart with a solid line.
            Assert.AreEqual(OmsiRoadMarking.Dashed, type.MarkingAfter(1));
            Assert.AreEqual(OmsiRoadMarking.Solid, type.MarkingAfter(2));
            Assert.AreEqual(OmsiRoadMarking.Solid, type.MarkingAfter(3));

            var profile = Profile(type.Describe(), out var files);
            var rails = profile.Lanes.Where(lane => lane.Kind == OmsiSplineCategories.RailLane).ToList();
            Assert.AreEqual(2, rails.Count);
            Assert.AreEqual(-1.5, rails[0].X, 1e-6);
            Assert.AreEqual(1, rails[0].Direction);
            Assert.AreEqual(1.5, rails[1].X, 1e-6);
            Assert.AreEqual(0, rails[1].Direction);
            // Cars share only the first track.
            Assert.IsTrue(profile.Lanes.Exists(lane => lane.Kind == 0 && Math.Abs(lane.X + 1.5) < 1e-6));
            Assert.IsFalse(profile.Lanes.Exists(lane => lane.Kind == 0 && Math.Abs(lane.X - 1.5) < 1e-6));
            StringAssert.Contains("[rail_enh]\r\n200\r\n0\r\n30\r\n0.01", files.Sli);
            Assert.IsTrue(files.Files.ContainsKey(OmsiRoadTypeWriter.GrooveTexture));
            CollectionAssert.AreEqual(new byte[] { (byte)'B', (byte)'M' }, files.Files[OmsiRoadTypeWriter.GrooveTexture].Take(2).ToArray());

            // Two strips per track, the head outside the gauge and the groove inside it.
            var groove = profile.Textures.FindIndex(texture => Path.GetFileName(texture.Path) == OmsiRoadTypeWriter.GrooveTexture);
            var strips = profile.Strips.Where(strip => strip.TextureIndex == groove).ToList();
            Assert.AreEqual(4, strips.Count);
            Assert.AreEqual(-1.5 - OmsiRoadTypeWriter.Gauge / 2.0 - OmsiRoadTypeWriter.RailHeadWidth, strips[0].Points[0].X, 1e-3);
            Assert.AreEqual(-1.5 - OmsiRoadTypeWriter.Gauge / 2.0 + OmsiRoadTypeWriter.RailGrooveWidth, strips[0].Points[1].X, 1e-3);

            // A street with tracks and lanes for cars stays a road, for the road menu and for junctions.
            Assert.AreNotEqual(OmsiSplineCategory.Rail, OmsiSplineCategories.Of(profile));
            Assert.IsTrue(OmsiRoadJunctions.IsRoad(profile));

            // Taken apart again, the tracks come back as tram lanes, shared where cars run on them.
            Assert.AreEqual(type.Describe(), OmsiRoadType.FromProfile(profile, null).Describe());
        }

        [Test]
        public void TracksRunStraightThroughAJunctionWithGroovedRails()
        {
            var tram = Profile("walk 3 | drive 3.25 back | tram 3 back cars | tram 3 cars | drive 3.25 | walk 3", out _);
            var plain = Profile(TwoLanes, out _);
            var arms = new[]
            {
                new OmsiJunctionArm { HeadingDegrees = 0.0, ProfilePath = ProfilePath, Profile = tram, Main = true },
                new OmsiJunctionArm { HeadingDegrees = 90.0, ProfilePath = ProfilePath, Profile = plain },
                new OmsiJunctionArm { HeadingDegrees = 180.0, ProfilePath = ProfilePath, Profile = tram, Main = true, Mirrored = true },
                new OmsiJunctionArm { HeadingDegrees = 270.0, ProfilePath = ProfilePath, Profile = plain, Mirrored = true }
            };

            var layout = OmsiJunctionLayout.Build(arms, null);

            var rails = layout.Paths.Where(path => path.Kind == OmsiSplineCategories.RailLane).ToList();
            Assert.AreEqual(2, rails.Count);
            var length = layout.Arms[0].Length + layout.Arms[2].Length;
            Assert.AreEqual(length, rails.Sum(path => path.LengthMeters) / 2.0, 1e-6);
            // Each track one way: the one used towards the junction on the first arm comes in along it, at its place.
            Assert.IsTrue(rails.All(path => path.Direction == 0 && Math.Abs(path.RadiusMeters) < 1e-9));
            Assert.IsTrue(rails.Exists(path => Math.Abs(path.X + 1.5) < 1e-6 && Math.Abs(path.HeadingDegrees - 180.0) < 1e-6));
            Assert.IsTrue(rails.Exists(path => Math.Abs(path.X - 1.5) < 1e-6 && Math.Abs(path.HeadingDegrees) < 1e-6));
            Assert.IsTrue(layout.Meshes.Exists(mesh => mesh.Texture == OmsiRoadTypeWriter.GrooveTexture && mesh.Triangles.Count > 0));

            var junction = OmsiJunctionGenerator.Junction(arms, null, "Tram junction");
            Assert.AreEqual(2, junction.Sco.Split(new[] { "[rail_enh]" }, StringSplitOptions.None).Length - 1);

            // Tracks that do not meet across the junction end at it.
            var shifted = Profile("walk 3 | drive 3.25 back | tram 4 back cars | tram 4 cars | drive 3.25 | walk 3", out _);
            arms[2].Profile = shifted;
            Assert.IsFalse(OmsiJunctionLayout.Build(arms, null).Paths.Exists(path => path.Kind == OmsiSplineCategories.RailLane));
        }

        [Test]
        public void BusPathsAreTheIndexesOfTheBusLanesInTheWrittenProfile()
        {
            foreach (var lanes in new[]
                     {
                         "walk 3 | bus 3.25 back | drive 3.25 back | drive 3.25 | bus 3.25 | walk 3",
                         "walk 3 | bus 3.25 back | tram 3 back cars | tram 3 | bus 3.25 | parking 2 | walk 3",
                         TwoLanes
                     })
            {
                var type = OmsiRoadType.Parse(lanes);
                var profile = Profile(lanes, out _);
                var x = -type.Width / 2.0;
                var expected = new List<int>();
                foreach (var lane in type.Lanes)
                {
                    if (lane.Kind == OmsiRoadLaneKind.Bus)
                        expected.Add(profile.Lanes.FindIndex(path => path.Kind == 0 && Math.Abs(path.X - (x + lane.Width / 2.0)) < 1e-6));
                    x += lane.Width;
                }
                CollectionAssert.AreEqual(expected, type.BusPaths(), lanes);
            }
            Assert.IsEmpty(OmsiRoadType.Parse(TwoLanes).BusPaths());
        }

        [Test]
        public void RowsStandByTheKerbInTheMiddleOrAtTheFarSide()
        {
            var type = OmsiRoadType.Parse("walk 3 row=\"Sceneryobjects\\Trees\\linde.sco\" every=15 | drive 3.25 back | median 2 row=\"Sceneryobjects\\x\\lamp.sco\" at=middle | " +
                                          "drive 3.25 | walk 3 row=\"Sceneryobjects\\Trees\\linde.sco\" at=outer");
            Assert.AreEqual(type.Describe(), OmsiRoadType.Parse(type.Describe()).Describe());
            StringAssert.Contains("every=15", type.Describe());
            StringAssert.Contains("at=middle", type.Describe());
            Assert.Throws<FormatException>(() => OmsiRoadType.Parse("walk 3 at=nowhere"));
            CollectionAssert.IsNotEmpty(OmsiRoadType.Parse("walk 3 row=\"a.sco\" every=1").Problems());

            var rows = type.Rows();
            Assert.AreEqual(3, rows.Count);
            // The left sidewalk from -7.25 to -4.25: by its kerb on the right, facing back along the road.
            Assert.AreEqual(-4.25 - OmsiRoadType.RowInset, rows[0].X, 1e-9);
            Assert.AreEqual(15.0, rows[0].Interval, 1e-9);
            Assert.AreEqual(180.0, rows[0].HeadingDegrees, 1e-9);
            Assert.AreEqual(OmsiRoadTypeWriter.RaisedHeight, rows[0].Height, 1e-9);
            Assert.AreEqual(0.0, rows[1].X, 1e-9);
            Assert.AreEqual(OmsiRoadLane.DefaultRowInterval, rows[1].Interval, 1e-9);
            // The right sidewalk from 4.25 to 7.25, at its far side.
            Assert.AreEqual(7.25 - OmsiRoadType.RowInset, rows[2].X, 1e-9);
            Assert.AreEqual(0.0, rows[2].HeadingDegrees, 1e-9);
            // Objects stand only beside the traffic: a row on a driving lane is refused and not laid.
            var onRoad = OmsiRoadType.Parse("walk 3 | drive 3.25 back row=\"Sceneryobjects\\Trees\\linde.sco\" | drive 3.25 | walk 3");
            Assert.IsTrue(onRoad.Problems().Exists(problem => problem.Contains("cannot stand on driving lane 2")));
            CollectionAssert.IsEmpty(onRoad.Rows());
            Assert.IsTrue(OmsiRoadLane.CanHoldRow(OmsiRoadLaneKind.Parking));
            Assert.IsFalse(OmsiRoadLane.CanHoldRow(OmsiRoadLaneKind.Tram));

            // A turn is kept and turns the objects on both sides alike.
            var hedge = OmsiRoadType.Parse("drive 3.25 back | median 3 row=\"Sceneryobjects\\Trees\\hedge.sco\" at=middle turn=90 | drive 3.25");
            Assert.AreEqual(hedge.Describe(), OmsiRoadType.Parse(hedge.Describe()).Describe());
            StringAssert.Contains("turn=90", hedge.Describe());
            Assert.AreEqual(90.0, hedge.Rows()[0].HeadingDegrees, 1e-9);
            Assert.AreEqual(0.0, hedge.Rows()[0].X, 1e-9);

            // A long object modelled across is turned to run along the road; a round one is not.
            OmsiObjectOutline Box(double across, double deep) => new OmsiObjectOutline
            {
                Points = new[] { new OmsiMapPoint(-across / 2, -deep / 2), new OmsiMapPoint(across / 2, -deep / 2), new OmsiMapPoint(across / 2, deep / 2), new OmsiMapPoint(-across / 2, deep / 2) }
            };
            Assert.AreEqual(90.0, OmsiRoadLane.SuggestedRowTurn(Box(20.0, 1.2)), 1e-9);
            Assert.AreEqual(0.0, OmsiRoadLane.SuggestedRowTurn(Box(1.2, 20.0)), 1e-9);
            Assert.AreEqual(0.0, OmsiRoadLane.SuggestedRowTurn(Box(4.0, 4.0)), 1e-9);
            Assert.AreEqual(0.0, OmsiRoadLane.SuggestedRowTurn(null), 1e-9);

            // Rows do not change the profile.
            Assert.AreEqual(OmsiRoadTypeWriter.Write(OmsiRoadType.Parse("walk 3 | drive 3.25 back | median 2 | drive 3.25 | walk 3")).Sli.Split('\n').Skip(2).ToArray(),
                OmsiRoadTypeWriter.Write(type).Sli.Split('\n').Skip(2).ToArray());
        }
    }
}
