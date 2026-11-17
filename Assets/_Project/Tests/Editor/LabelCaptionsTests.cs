using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The names of the label rows in the inspector (M4 "Labels"): <see cref="LabelCaptions"/> against the schema and the texts of every language.</summary>
    public sealed class LabelCaptionsTests
    {
        private static string LocalesFolder()
        {
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 6 && folder != null; depth++, folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(Path.Combine(folder, "locales")) && Directory.Exists(Path.Combine(folder, "Assets", "_Project", "Scripts")))
                    return Path.Combine(folder, "locales");
            Assert.Fail("The repository root with locales/ was not found from " + Directory.GetCurrentDirectory());
            return null;
        }

        private static IEnumerable<OmsiSceneryObjectKind> Kinds()
        {
            return System.Enum.GetValues(typeof(OmsiSceneryObjectKind)).Cast<OmsiSceneryObjectKind>();
        }

        [Test]
        public void EveryKindKeyHasACaptionKeyExceptTheSpareTreeRow()
        {
            foreach (var kind in Kinds())
                foreach (var key in OmsiLabelSchema.KindKeys(kind))
                    if (key == "tree_spare")
                        Assert.That(LabelCaptions.CaptionedKindKeys, Does.Not.Contain(key));
                    else
                        Assert.That(LabelCaptions.CaptionedKindKeys, Does.Contain(key), key);
        }

        [Test]
        public void EveryCaptionKeyExistsInAllLanguagesAndOnlyTakesTheIndex()
        {
            var keys = new HashSet<string> { LabelCaptions.GenericKey, LabelCaptions.TextKey, "labels.field.busstop.side_lht" };
            foreach (var kind in Kinds())
                foreach (var schemaKey in OmsiLabelSchema.KindKeys(kind))
                    keys.Add(LabelCaptions.KeyFor(OmsiLabelSchema.Build(kind, null, null).Fields.First(field => field.Key == schemaKey), false));
            var locales = LocaleLoader.List(LocalesFolder(), null);
            Assert.GreaterOrEqual(locales.Count, 8);
            foreach (var info in locales)
            {
                var messages = LocaleLoader.Load(info).Messages;
                foreach (var key in keys)
                {
                    Assert.IsTrue(messages.ContainsKey(key), info.Code + " lacks " + key);
                    var names = MessageFormat.Parse(messages[key]).Arguments.Select(argument => argument.Name).ToList();
                    CollectionAssert.IsSubsetOf(names, new[] { "index" }, info.Code + " " + key);
                }
            }
        }

        [Test]
        public void BusStopRowsAreNamedAndLeftHandTrafficChangesOnlyTheSide()
        {
            var rows = OmsiLabelSchema.Build(OmsiSceneryObjectKind.BusStop, null, null).RowsFor(0);
            Assert.AreEqual(7, rows.Count);
            Assert.AreEqual("labels.field.busstop.name", LabelCaptions.KeyFor(rows[0], false));
            Assert.AreEqual("labels.field.busstop.side", LabelCaptions.KeyFor(rows[5], false));
            Assert.AreEqual("labels.field.busstop.side_lht", LabelCaptions.KeyFor(rows[5], true));
            Assert.AreEqual("labels.field.busstop.subname", LabelCaptions.KeyFor(rows[6], true));
            Assert.IsTrue(LabelCaptions.NeedsTrafficSide(rows));
        }

        [Test]
        public void CarParkHasOneNamedRowAndAPlainObjectHasNone()
        {
            var rows = OmsiLabelSchema.Build(OmsiSceneryObjectKind.CarPark, null, null).RowsFor(0);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("labels.field.carpark.index", LabelCaptions.KeyFor(rows[0], false));
            Assert.AreEqual(0, OmsiLabelSchema.Build(OmsiSceneryObjectKind.None, null, null).RowsFor(0, LabelCaptions.SpareRows).Count);
        }

        [Test]
        public void ScriptVariablesShowTheirOwnNameAndGenericRowsTheirIndex()
        {
            var rows = OmsiLabelSchema.Build(OmsiSceneryObjectKind.None, new[] { "line_no", "dest" }, null).RowsFor(3);
            Assert.IsNull(LabelCaptions.KeyFor(rows[0], false));
            Assert.AreEqual("line_no", LabelCaptions.Text(rows[0], false));
            Assert.AreEqual("stringvar dest", LabelCaptions.Technical(rows[1]));
            Assert.AreEqual(LabelCaptions.GenericKey, LabelCaptions.KeyFor(rows[2], false));
            Assert.AreEqual(2, rows[2].Index);
            Assert.AreEqual("label 2", LabelCaptions.Technical(rows[2]));
        }

        [Test]
        public void ATextTextureOfTheStopNameIsARowEvenWithoutANameAndTheTreeSpareRowIsGeneric()
        {
            var rows = OmsiLabelSchema.Build(OmsiSceneryObjectKind.None, null, new[] { "0" }).RowsFor(0, LabelCaptions.SpareRows);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(LabelCaptions.TextKey, LabelCaptions.KeyFor(rows[0], false));
            Assert.AreEqual("texttexture, label 0", LabelCaptions.Technical(rows[0]));
            var tree = OmsiLabelSchema.Build(OmsiSceneryObjectKind.Tree, null, null).RowsFor(0);
            Assert.AreEqual(LabelCaptions.GenericKey, LabelCaptions.KeyFor(tree[3], false));
        }

        [Test]
        public void MoreExistingLabelsThanTheSchemaStayVisibleAsGenericRows()
        {
            var rows = OmsiLabelSchema.Build(OmsiSceneryObjectKind.BusStop, null, null).RowsFor(8, LabelCaptions.SpareRows);
            Assert.AreEqual(8, rows.Count);
            Assert.AreEqual(LabelCaptions.GenericKey, LabelCaptions.KeyFor(rows[7], false));
            Assert.AreEqual(7, rows[7].Index);
        }

        [Test]
        public void TypedRowsAreWrittenWithoutTrailingEmptyOnesAndNeverBelowTheCurrentCount()
        {
            var none = new string[0];
            Assert.AreEqual(0, OmsiLabelSchema.LabelsToWrite(none, new[] { "", "", "" }).Count);
            CollectionAssert.AreEqual(new[] { "Stop" }, OmsiLabelSchema.LabelsToWrite(none, new[] { "Stop", "", "" }));
            Assert.AreEqual(8, OmsiLabelSchema.LabelsToWrite(new string[8], new[] { "", "" }).Count);
        }
    }
}
