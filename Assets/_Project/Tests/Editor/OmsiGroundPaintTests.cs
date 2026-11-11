using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Ground texture masks and painting them (ADR 091).</summary>
    public sealed class OmsiGroundPaintTests
    {
        private string root;
        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-paint-" + Guid.NewGuid().ToString("N"));
            mapFolder = Path.Combine(root, "maps", "Painttown");
            Directory.CreateDirectory(Path.Combine(mapFolder, "texture", "map"));
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "",
                "[groundtex]", @"texture\gras.bmp", @"texture\gras_det.bmp", "0", "1", "60", "",
                "[groundtex]", @"texture\feld01.bmp", @"texture\feld_det.bmp", "6", "1", "30", "",
                "[groundtex]", @"texture\dreck.bmp", "", "5", "2", "30", "");
            WriteUtf16("tile_0_0.map", "[version]", "14", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "");
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private OmsiMapSession Open(out OmsiMapSummary summary)
        {
            var session = OmsiMapSession.OpenProject(Path.Combine(mapFolder, "global.cfg"), Path.Combine(root, "Projects", "Paint.omproj"));
            summary = session.Read();
            return session;
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }

        private static OmsiPaintDab Dab(double x, double y, double radius = 20.0, double hardness = 1.0, double strength = 1.0)
        {
            return new OmsiPaintDab { X = x, Y = y, Radius = radius, Hardness = hardness, Strength = strength };
        }

        [Test]
        public void AMaskWritesTheHeaderOfTheBaseGamesMasksAndReadsItselfBack()
        {
            var mask = new OmsiGroundMask(8);
            mask.Alpha[0] = 255;
            mask.Alpha[63] = 7;
            var bytes = mask.ToBytes();
            Assert.AreEqual(128 + 64, bytes.Length);
            // The header of Grundorf's tile_-1_-1.map.1.dds, for 512 pixels: dwFlags 0x1007, pixel format alpha 8 bit with mask 0xff, caps 0x1002.
            var expected = new byte[128];
            expected[0] = 0x44; expected[1] = 0x44; expected[2] = 0x53; expected[3] = 0x20; expected[4] = 0x7c;
            expected[8] = 0x07; expected[9] = 0x10; expected[13] = 0x02; expected[17] = 0x02;
            expected[76] = 0x20; expected[80] = 0x02; expected[88] = 0x08; expected[104] = 0xff; expected[108] = 0x02; expected[109] = 0x10;
            var big = new OmsiGroundMask(512).ToBytes();
            CollectionAssert.AreEqual(expected, big.Take(128).ToArray());

            var back = OmsiGroundMask.TryParse(bytes);
            Assert.IsNotNull(back);
            Assert.AreEqual(8, back.Size);
            CollectionAssert.AreEqual(mask.Alpha, back.Alpha);
            Assert.IsNull(OmsiGroundMask.TryParse(new byte[10]));
            Assert.AreEqual(9, OmsiGroundMask.ExponentOf(512));
        }

        [Test]
        public void AMaskWithMipmapsIsReadFromItsFirstLevel()
        {
            var mask = new OmsiGroundMask(4);
            mask.Alpha[5] = 200;
            var bytes = mask.ToBytes().Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray();
            bytes[8] = 0x07;
            bytes[10] = 0x02;
            bytes[28] = 3;
            var back = OmsiGroundMask.TryParse(bytes);
            Assert.AreEqual(200, back.Alpha[5]);
            Assert.AreEqual(16, back.Alpha.Length);
        }

        [Test]
        public void TheBrushIsSolidInsideItsHardnessAndFallsOffToTheEdge()
        {
            Assert.AreEqual(1.0, OmsiGroundPaint.Falloff(4.0, 10.0, 0.5), 1e-9);
            Assert.AreEqual(0.0, OmsiGroundPaint.Falloff(10.0, 10.0, 0.5), 1e-9);
            var middle = OmsiGroundPaint.Falloff(7.5, 10.0, 0.5);
            Assert.AreEqual(0.5, middle, 1e-9);
            Assert.Less(OmsiGroundPaint.Falloff(9.0, 10.0, 0.5), middle);
            Assert.AreEqual(0.0, OmsiGroundPaint.Falloff(1.0, 0.0, 0.5), 1e-9);
        }

        [Test]
        public void PaintingTakesTheLargerCoverageSoHoldingTheBrushStillChangesNothingMore()
        {
            var mask = new OmsiGroundMask(64);
            // A 64-pixel mask over 64 m: one pixel a metre; the dab is at the tile's middle.
            Assert.IsTrue(OmsiGroundPaint.Raise(mask, 0, 0, 64, Dab(32, 32, 10, 1.0, 0.8)));
            var once = (byte[])mask.Alpha.Clone();
            Assert.IsFalse(OmsiGroundPaint.Raise(mask, 0, 0, 64, Dab(32, 32, 10, 1.0, 0.8)));
            CollectionAssert.AreEqual(once, mask.Alpha);
            Assert.AreEqual(204, mask.Alpha[32 * 64 + 32]);
            Assert.AreEqual(0, mask.Alpha[0]);
        }

        [Test]
        public void ErasingTakesCoverageAwayAndTheTopRowOfAMaskIsTheNorthEdge()
        {
            var mask = new OmsiGroundMask(64);
            OmsiGroundPaint.Fill(mask, 255);
            // A dab 5 m south of the north edge touches rows near 0, not the last ones.
            Assert.IsTrue(OmsiGroundPaint.Lower(mask, 0, 0, 64, Dab(32, 64 - 5, 3)));
            Assert.AreEqual(0, mask.Alpha[5 * 64 + 32]);
            Assert.AreEqual(255, mask.Alpha[(64 - 6) * 64 + 32]);
            Assert.AreEqual(255, mask.Alpha[5 * 64]);
        }

        [Test]
        public void SofteningBlursAnEdgeAndLeavesAFlatAreaAlone()
        {
            var mask = new OmsiGroundMask(32);
            for (var row = 0; row < 32; row++)
                for (var column = 16; column < 32; column++)
                    mask.Alpha[row * 32 + column] = 255;
            var flat = new OmsiGroundMask(32);
            OmsiGroundPaint.Fill(flat, 90);
            Assert.IsFalse(OmsiGroundPaint.Soften(flat, 0, 0, 32, Dab(16, 16, 10)));
            Assert.IsTrue(OmsiGroundPaint.Soften(mask, 0, 0, 32, Dab(16, 16, 10)));
            Assert.That(mask.Alpha[16 * 32 + 15], Is.InRange(1, 254));
            Assert.That(mask.Alpha[16 * 32 + 16], Is.InRange(1, 254));
        }

        [Test]
        public void ADabOnTheSeamBetweenTwoTilesPaintsTheSameEdgeRowsOnBoth()
        {
            var session = Open(out var summary);
            var stroke = session.BeginPaintStroke();
            var touched = session.ApplyGroundPaint(summary, stroke, 1, OmsiPaintMode.Paint, Dab(300, 150, 15));
            Assert.AreEqual(2, touched.Count);
            var west = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds"));
            var east = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_1_0.map.1.dds"));
            Assert.AreEqual(64, west.Size);
            // The brush's middle at y 150 is the middle row of both tiles; the west tile's last column and the east tile's first both have it.
            var row = 32;
            Assert.AreEqual(255, west.Alpha[row * 64 + 63]);
            Assert.AreEqual(255, east.Alpha[row * 64]);
            Assert.AreEqual(west.Alpha[row * 64 + 63 - 1] > 0, east.Alpha[row * 64 + 1] > 0);
        }

        [Test]
        public void PaintingATextureTakesTheTexturesDrawnOverItAwayUnderTheBrushAndTheFirstTextureTakesAllAway()
        {
            var session = Open(out var summary);
            var stroke = session.BeginPaintStroke();
            session.ApplyGroundPaint(summary, stroke, 2, OmsiPaintMode.Paint, Dab(100, 100, 10));
            session.ApplyGroundPaint(summary, stroke, 1, OmsiPaintMode.Paint, Dab(100, 100, 10));
            var one = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds"));
            var two = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.2.dds"));
            // Pixel at (100, 100): column 100/300*64, row (300-100)/300*64 (texture 1 has 64 pixels, texture 2 has 32).
            Assert.AreEqual(255, one.Alpha[(int)(200 / 300.0 * 64) * 64 + (int)(100 / 300.0 * 64)]);
            Assert.AreEqual(0, two.Alpha[(int)(200 / 300.0 * 32) * 32 + (int)(100 / 300.0 * 32)]);

            session.ApplyGroundPaint(summary, stroke, 0, OmsiPaintMode.Paint, Dab(100, 100, 10));
            one = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds"));
            Assert.AreEqual(0, one.Alpha[(int)(200 / 300.0 * 64) * 64 + (int)(100 / 300.0 * 64)]);
        }

        [Test]
        public void AStrokeIsOneStepThatUndoRemovesAMaskItMadeAndRedoBringsBack()
        {
            var session = Open(out var summary);
            var stroke = session.BeginPaintStroke();
            session.ApplyGroundPaint(summary, stroke, 1, OmsiPaintMode.Paint, Dab(100, 100, 10));
            session.ApplyGroundPaint(summary, stroke, 1, OmsiPaintMode.Paint, Dab(120, 100, 10));
            var change = session.EndPaintStroke(stroke);
            Assert.IsNotNull(change);
            Assert.IsTrue(change.Painted);
            Assert.AreEqual(1, change.Files.Count);
            Assert.IsTrue(session.Workspace.FileExists(@"texture\map\tile_0_0.map.1.dds"));

            session.Undo(summary, change);
            Assert.IsFalse(session.Workspace.FileExists(@"texture\map\tile_0_0.map.1.dds"));
            session.Redo(summary, change);
            Assert.IsTrue(session.Workspace.FileExists(@"texture\map\tile_0_0.map.1.dds"));
            Assert.IsNull(session.EndPaintStroke(session.BeginPaintStroke()));
        }

        [Test]
        public void AnExistingMaskKeepsItsSizeAndErasingItIsUndoneToTheBytesFromBefore()
        {
            var big = new OmsiGroundMask(16);
            OmsiGroundPaint.Fill(big, 255);
            var original = big.ToBytes();
            File.WriteAllBytes(Path.Combine(mapFolder, "texture", "map", "tile_0_0.map.1.dds"), original);
            var session = Open(out var summary);
            var stroke = session.BeginPaintStroke();
            session.ApplyGroundPaint(summary, stroke, 1, OmsiPaintMode.Erase, Dab(150, 150, 30));
            var change = session.EndPaintStroke(stroke);
            var erased = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds"));
            Assert.AreEqual(16, erased.Size);
            Assert.AreEqual(0, erased.Alpha[8 * 16 + 8]);
            session.Undo(summary, change);
            CollectionAssert.AreEqual(original, session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds"));
        }

        [Test]
        public void ErasingTakesATextureAwayCompletelyWhateverTheStrengthIs()
        {
            var session = Open(out var summary);
            var stroke = session.BeginPaintStroke();
            session.ApplyGroundPaint(summary, stroke, 1, OmsiPaintMode.Paint, Dab(100, 100, 10));
            session.ApplyGroundPaint(summary, stroke, 1, OmsiPaintMode.Erase, Dab(100, 100, 10, 1.0, 0.5));
            var mask = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds"));
            Assert.AreEqual(0, mask.Alpha[(int)(200 / 300.0 * 64) * 64 + (int)(100 / 300.0 * 64)]);
        }

        [Test]
        public void CoveringATileFillsItsMaskAndClearsTheOthersAboveItAndUncoveringEmptiesIt()
        {
            var session = Open(out var summary);
            var tile = summary.Tiles.Find(candidate => candidate.X == 0 && candidate.Y == 0);
            var change = session.CoverGroundTile(summary, tile, 1, true);
            Assert.IsNotNull(change);
            var one = OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds"));
            Assert.IsTrue(one.Alpha.All(value => value == 255));
            Assert.IsNull(session.CoverGroundTile(summary, tile, 1, true));
            var cleared = session.CoverGroundTile(summary, tile, 1, false);
            Assert.IsNotNull(cleared);
            Assert.IsTrue(OmsiGroundMask.TryParse(session.Workspace.ReadAllBytes(@"texture\map\tile_0_0.map.1.dds")).Alpha.All(value => value == 0));
        }

        [Test]
        public void OnlyBusStopObjectsAreOfferedAsStopsUntilTheTimetableHasThem()
        {
            // The fixture's tile gets a bus stop, a tree and a shelter-like file (not a stop); the first is known to Busstops.cfg afterwards.
            WriteUtf16("tile_0_0.map", "[version]", "14", "",
                "[object]", "0", @"Sceneryobjects\Generic\bus_stop.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\Generic\tree.sco", "6", "30", "40", "0", "0", "0", "0", "0", "",
                "[object]", "0", @"Sceneryobjects\Generic\Haltestelle_2.sco", "7", "50", "60", "0", "0", "0", "0", "0", "");
            var session = Open(out var summary);
            var found = session.FindStopObjects(summary);
            CollectionAssert.AreEqual(new[] { 5 }, found.Select(stop => stop.ObjectId).ToArray());
            Assert.IsTrue(found.All(stop => stop.IsNew));
            Assert.AreEqual(10.0, found[0].World.X, 1e-6);
            Assert.AreEqual(20.0, found[0].World.Y, 1e-6);
            Directory.CreateDirectory(Path.Combine(mapFolder, "TTData"));
            File.WriteAllBytes(Path.Combine(mapFolder, "TTData", "Busstops.cfg"), Encoding.UTF8.GetBytes("[busstop]\r\nOne\r\n0\r\n5\r\n0.0\r\n0\r\n0\r\n\r\n"));
            session = Open(out summary);
            CollectionAssert.IsEmpty(session.FindStopObjects(summary).Select(stop => stop.ObjectId).ToArray());
            CollectionAssert.AreEqual(new[] { 5 }, session.ReadStopChoices(summary).Select(stop => stop.ObjectId).ToArray());
        }

        [Test]
        public void ANewGroundTextureIsAppendedAfterTheLastAndUndoTakesItAway()
        {
            var session = Open(out var summary);
            var before = session.ReadGroundTextures();
            Assert.AreEqual(3, before.Count);
            var change = session.AddGroundTexture(@"texture\sand.bmp", @"texture\noise_low.bmp", 9, 1.0, 40.0);
            Assert.IsTrue(change.TexturesChanged);
            var after = session.ReadGroundTextures();
            Assert.AreEqual(4, after.Count);
            Assert.AreEqual(3, after[3].Index);
            Assert.AreEqual(@"texture\sand.bmp", after[3].TexturePath);
            Assert.AreEqual(9, after[3].MaskSizeExponent);
            Assert.AreEqual(40.0, after[3].DetailRepeats, 1e-9);
            session.Undo(summary, change);
            Assert.AreEqual(3, session.ReadGroundTextures().Count);
        }
    }
}
