using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiLabelSchemaTests
    {
        // The stock Sceneryobjects\Generic files, shortened to what matters.
        private const string BusStopSco = "[groups]\r\n1\r\nUtilities\r\n\r\n[friendlyname]\r\nBus Stop\r\n\r\n[onlyeditor]\r\n\r\n[mesh]\r\nbusstop.x\r\n\r\n" +
                                          "\t[busstop] macht aus dem Objekt eine Bushaltestelle\r\n\r\n[busstop]\r\n\r\n[boundingbox]\r\n1\r\n1\r\n1\r\n0\r\n0\r\n0.5\r\n";
        private const string CarParkSco = "[groups]\r\n1\r\nUtilities\r\n\r\n[friendlyname]\r\nCar park (passenger car)\r\n\r\n------------------------\r\n\r\n" +
                                          "[carpark_p]\r\n\r\n[onlyeditor]\r\n\r\n[mesh]\r\nparkplatz.x\r\n";
        private const string PeopleSco = "[groups]\r\n1\r\nUtilities\r\n\r\n[friendlyname]\r\nPeople Standing 1x3\r\n\r\n[passengercabin]\r\npeople_standing_small_pax.cfg\r\n\r\n" +
                                         "[onlyeditor]\r\n\r\n[mesh]\r\npeople_standing_small.x\r\n";

        private static readonly string[] BusStopKeys =
        {
            "busstop_name", "busstop_pass_enter_max", "busstop_pass_enter_min", "busstop_pass_exit", "busstop_dockdist", "busstop_side", "busstop_subname"
        };

        /// <summary>The name list of a [tree] type, as the .sco loader writes it (0x7b5bc6): three names and one empty slot.</summary>
        private static readonly string[] TreeNames = { "texture", "height", "ratio", "" };

        private string testRoot;

        [SetUp]
        public void CreateFolder()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-labelschema-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void ABusStopHasItsSevenFieldsInOmsiOrder()
        {
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(BusStopSco));
            var schema = OmsiLabelSchema.Build(sceneryObject, null);

            Assert.AreEqual(OmsiSceneryObjectKind.BusStop, sceneryObject.Kind);
            CollectionAssert.AreEqual(BusStopKeys, schema.Fields.Select(field => field.Key));
            CollectionAssert.AreEqual(Enumerable.Range(0, 7), schema.Fields.Select(field => field.Index));
            Assert.IsTrue(schema.Fields.All(field => field.Source == OmsiLabelFieldSource.Kind));
            // A stock stop record has 7 labels; the rows are exactly the seven fields.
            CollectionAssert.AreEqual(BusStopKeys, schema.RowsFor(7).Select(field => field.Key));
        }

        [Test]
        public void ACarParkHasItsParkingListIndex()
        {
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(CarParkSco));
            var schema = OmsiLabelSchema.Build(sceneryObject, null);

            Assert.AreEqual(OmsiSceneryObjectKind.CarPark, sceneryObject.Kind);
            CollectionAssert.AreEqual(new[] { "carpark_index" }, schema.Fields.Select(field => field.Key));
            Assert.AreEqual(1, schema.RowsFor(1).Count);
            Assert.AreEqual(1, schema.RowsFor(0).Count);
        }

        [Test]
        public void TheOriginalDialogReproducesBothScreenshotsWithATreesNameList()
        {
            // Bus stop with 7 labels: 7 names, then "Label 4..6" (Beschriftung 4, 5, 6), 10 rows.
            CollectionAssert.AreEqual(BusStopKeys.Concat(new[] { "generic:4", "generic:5", "generic:6" }),
                OmsiLabelSchema.OmsiDialogRows(OmsiSceneryObjectKind.BusStop, TreeNames, 7, 0));
            // Car park with 1 label: Parklistenindex, height, ratio and one unnamed row.
            CollectionAssert.AreEqual(new[] { "carpark_index", "name:height", "name:ratio", "name:" },
                OmsiLabelSchema.OmsiDialogRows(OmsiSceneryObjectKind.CarPark, TreeNames, 1, 0));
            // With their own name lists the dialog shows 7 rows and 1 row.
            Assert.AreEqual(7, OmsiLabelSchema.OmsiDialogRows(OmsiSceneryObjectKind.BusStop, BusStopKeys, 7, 0).Count);
            Assert.AreEqual(1, OmsiLabelSchema.OmsiDialogRows(OmsiSceneryObjectKind.CarPark, new[] { "Index" }, 1, 0).Count);
        }

        [Test]
        public void PeopleWaitingAtAStopHaveNoBusStopFields()
        {
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(PeopleSco));

            Assert.AreEqual(OmsiSceneryObjectKind.None, sceneryObject.Kind);
            Assert.AreEqual("people_standing_small_pax.cfg", sceneryObject.PassengerCabin);
            Assert.AreEqual(0, OmsiLabelSchema.Build(sceneryObject, null).Count);
        }

        [Test]
        public void ScriptStringVariablesNameTheLabelsInOrderAndFollowTheKindFields()
        {
            File.WriteAllText(Path.Combine(testRoot, "sign.sco"),
                "[stringvarnamelist]\r\n2\r\nscript\\a_strings.txt\r\nscript\\b_strings.txt\r\n\r\n[mesh]\r\nsign.x\r\n");
            Directory.CreateDirectory(Path.Combine(testRoot, "script"));
            File.WriteAllText(Path.Combine(testRoot, "script", "a_strings.txt"), "Line\r\n\r\nTerminus\r\n");
            File.WriteAllText(Path.Combine(testRoot, "script", "b_strings.txt"), "Via\r\n");
            File.WriteAllText(Path.Combine(testRoot, "stop.sco"), "[busstop]\r\n\r\n[stringvarnamelist]\r\n1\r\nscript\\b_strings.txt\r\n");

            var catalog = new OmsiLabelSchemaCatalog(testRoot);
            var sign = catalog.Get("sign.sco");

            CollectionAssert.AreEqual(new[] { "var:Line", "var:Terminus", "var:Via" }, sign.Fields.Select(field => field.Key));
            CollectionAssert.AreEqual(new[] { "Line", "Terminus", "Via" }, sign.Fields.Select(field => field.VariableName));
            Assert.IsTrue(sign.Fields.All(field => field.Source == OmsiLabelFieldSource.ScriptVariable));
            Assert.AreSame(sign, catalog.Get("SIGN.sco"), "read once per file");
            var stop = catalog.Get("stop.sco");
            Assert.AreEqual(8, stop.Count);
            Assert.AreEqual("var:Via", stop.Fields[7].Key);
            Assert.AreSame(OmsiLabelSchema.Empty, catalog.Get("missing.sco"));
            Assert.IsNotNull(catalog.LastProblem);
        }

        [Test]
        public void ATextTextureIndexMakesThatLabelAField()
        {
            var sceneryObject = OmsiSceneryObject.Parse(Encoding.ASCII.GetBytes(
                "[texttexture]\r\n2\r\nDIN-Ziffern eng 1\r\n64\r\n64\r\n0\r\n31\r\n97\r\n72\r\n\r\n" +
                "[texttexture_enh]\r\nTerminus\r\nArial\r\n128\r\n32\r\n0\r\n0\r\n0\r\n0\r\n0\r\n1\r\n"));
            var schema = OmsiLabelSchema.Build(sceneryObject, null);

            CollectionAssert.AreEqual(new[] { "2", "Terminus" }, sceneryObject.TextTextureSources);
            Assert.AreEqual(3, schema.Count);
            Assert.AreEqual(OmsiLabelFieldSource.Generic, schema.Fields[0].Source);
            Assert.AreEqual(OmsiLabelFieldSource.TextTexture, schema.Fields[2].Source);
            Assert.AreEqual(OmsiLabelField.GenericKey, schema.Fields[2].Key);
        }

        [Test]
        public void ARecordWithMoreLabelsThanTheSchemaKeepsAllOfThem()
        {
            var schema = OmsiLabelSchema.Build(OmsiSceneryObjectKind.CarPark, null, null);
            var rows = schema.RowsFor(3);

            CollectionAssert.AreEqual(new[] { "carpark_index", "generic", "generic" }, rows.Select(field => field.Key));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(field => field.Index));
            Assert.AreEqual(4, schema.RowsFor(0, 4).Count);
        }

        [Test]
        public void TrailingEmptyRowsAreNotWrittenButExistingLabelsStay()
        {
            var untouched = OmsiLabelSchema.LabelsToWrite(new string[0], new[] { "", "", "", "", "", "", "" });
            Assert.AreEqual(0, untouched.Count, "an untouched object stays at 0 labels");

            var named = OmsiLabelSchema.LabelsToWrite(new string[0], new[] { "U Rathaus", "10", "", "", "", "", "" });
            CollectionAssert.AreEqual(new[] { "U Rathaus", "10" }, named);

            // The stock pattern ends with an empty subname: editing the name keeps all 7.
            var stock = new[] { "Rathaus", "10", "0", "", "30", "0", "" };
            var edited = OmsiLabelSchema.LabelsToWrite(stock, new[] { "Rathaus Spandau", "10", "0", "", "30", "0", "" });
            CollectionAssert.AreEqual(new[] { "Rathaus Spandau", "10", "0", "", "30", "0", "" }, edited);

            // Rows the editor did not show keep their text.
            CollectionAssert.AreEqual(new[] { "A", "y", "z" }, OmsiLabelSchema.LabelsToWrite(new[] { "x", "y", "z" }, new[] { "A" }));
        }

        [Test]
        public void WritingLabelsToARecordWithoutLabelsChangesNothingElseAndClearingGoesBackToZero()
        {
            var original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            WriteUtf16(original, "global.cfg", "[name]", "Testmap", "", "[NextIDCode]", "20", "", "[map]", "0", "0", "tile_0_0.map", "");
            WriteUtf16(original, "tile_0_0.map", "[version]", "14", "",
                "[object]", "0", "Sceneryobjects\\Generic\\bus_stop.sco", "5", "10", "20", "0", "0", "0", "0", "0", "",
                "[object]", "0", "tree.sco", "6", "30", "40", "0", "0", "0", "0", "0", "");
            var session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
            var tile = session.Read().Tiles[0];
            var stop = tile.Objects.Single(item => item.Id == 5);
            var before = File.ReadAllBytes(Path.Combine(original, "tile_0_0.map"));

            var labels = OmsiLabelSchema.LabelsToWrite(stop.Labels, new[] { "U Rathaus", "10", "", "", "", "", "" });
            Assert.IsNotNull(session.SetElementLabels(tile, stop, labels));
            var after = session.Workspace.ReadAllBytes("tile_0_0.map");
            var expected = OmsiTextFile.Decode(before).Replace("0\r\n0\r\n0\r\n0\r\n0\r\n\r\n[object]\r\n0\r\ntree.sco",
                "0\r\n0\r\n0\r\n0\r\n2\r\nU Rathaus\r\n10\r\n\r\n[object]\r\n0\r\ntree.sco");
            CollectionAssert.AreEqual(OmsiTextFile.Encode(expected, OmsiTextEncoding.Utf16LittleEndian), after);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(original, "tile_0_0.map")), "the original map is never touched");

            // Clearing all labels writes the count 0 again: the file is as it was.
            Assert.IsNotNull(session.SetElementLabels(tile, tile.Objects.Single(item => item.Id == 5), new string[0]));
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes("tile_0_0.map"));
            Assert.AreEqual(0, tile.Objects.Single(item => item.Id == 5).Labels.Count);
        }

        private static void WriteUtf16(string folder, string name, params string[] lines)
        {
            File.WriteAllBytes(Path.Combine(folder, name), OmsiTextFile.Encode(string.Join("\r\n", lines) + "\r\n", OmsiTextEncoding.Utf16LittleEndian));
        }
    }
}
